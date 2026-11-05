using System.Globalization;
using System.Text.Json;

namespace ProfileCore;

public interface IProcessInfo
{
    (WinProcess.ProcessCheck State, DateTime? Start) Query(int pid);
}

public sealed class ProcessInfoProvider : IProcessInfo
{
    public (WinProcess.ProcessCheck State, DateTime? Start) Query(int pid) => WinProcess.Query(pid);
}

public sealed record LiveRecord(int Pid, string SessionId, DateTime? ProcStart, string? Cwd, string? Status, string? Entrypoint, string? Name);

/// Reader for sessions\*.json. Never opens the .key files beside them. Results are not filtered,
/// because the display filter must never apply to safety checks.
public sealed class LiveSessionReader(ConfigLocator locator, IProcessInfo? processes = null)
{
    readonly IProcessInfo processes = processes ?? new ProcessInfoProvider();

    /// A record that cannot be read could be a session starting up, so it counts as live for this long.
    public TimeSpan UnreadableGrace { get; init; } = TimeSpan.FromSeconds(60);

    /// On Windows, procStart is a JSON string of digits: a file time, 100 ns ticks since 1601 UTC
    /// (S0 check C9). A number is accepted too.
    public static DateTime? ParseProcStart(JsonElement v)
    {
        long ticks;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out ticks)) { }
        else if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out ticks)) { }
        else return null;
        try { return DateTime.FromFileTimeUtc(ticks); } catch (ArgumentOutOfRangeException) { return null; }
    }

    public bool IsLive(LiveRecord r)
    {
        var (state, start) = processes.Query(r.Pid);
        if (state == WinProcess.ProcessCheck.NotRunning) return false;
        if (r.ProcStart == null || start == null) return true; // cannot tell, so it counts as live
        return Math.Abs((r.ProcStart.Value - start.Value).TotalSeconds) <= 1;
    }

    public List<LiveRecord> LiveRecords(Profile p) => Read(p).Records.Where(IsLive).ToList();

    /// True if anything may be running. An unreadable, recent record counts, so a safety check fails closed.
    public bool HasLiveSession(Profile p)
    {
        var r = Read(p);
        return r.UnreadableRecent || r.Records.Any(IsLive);
    }

    (List<LiveRecord> Records, bool UnreadableRecent) Read(Profile p)
    {
        var dir = Path.Combine(locator.ConfigDir(p), "sessions");
        var records = new List<LiveRecord>();
        var unreadable = false;
        if (!Directory.Exists(dir)) return (records, false);
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var rec = Parse(file);
            if (rec != null) records.Add(rec);
            else if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < UnreadableGrace) unreadable = true;
        }
        return (records, unreadable);
    }

    internal static LiveRecord? Parse(string file)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            var o = doc.RootElement;
            if (o.ValueKind != JsonValueKind.Object) return null;
            if (!o.TryGetProperty("pid", out var pidEl) || !pidEl.TryGetInt32(out var pid)) return null;
            if (!o.TryGetProperty("sessionId", out var sid) || sid.ValueKind != JsonValueKind.String) return null;
            if (!o.TryGetProperty("procStart", out var ps)) return null;
            return new LiveRecord(pid, sid.GetString()!, ParseProcStart(ps), S(o, "cwd"), S(o, "status"), S(o, "entrypoint"), S(o, "name"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    static string? S(JsonElement o, string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
