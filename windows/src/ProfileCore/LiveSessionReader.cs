using System.Globalization;
using System.Text.Json;

namespace ProfileCore;

public sealed record ProcEntry(int Pid, string Name, DateTime? Start);

public interface IProcessInfo
{
    (WinProcess.ProcessCheck State, DateTime? Start) Query(int pid);

    /// Processes started by pid, and by those, and so on. For Stop to report what outlives a session.
    /// consoleMembers lists the processes on a console that one of them owns (see ProcessInfoProvider).
    IReadOnlyList<ProcEntry> Descendants(int pid, Func<int, IReadOnlyList<int>>? consoleMembers = null);
}

public sealed class ProcessInfoProvider : IProcessInfo
{
    public (WinProcess.ProcessCheck State, DateTime? Start) Query(int pid) => WinProcess.Query(pid);

    /// Parent links alone miss most commands: Claude Code's Git Bash starts each one through a
    /// process that exits at once (seen in W2 testing), so the command has no living parent. It stays
    /// on the hidden console of the bash that started it, so the processes on any console owned by
    /// a descendant (one with a conhost.exe child) are added too. The session's own console is not
    /// used, because the user's shell is on it. conhost.exe itself is never listed.
    public IReadOnlyList<ProcEntry> Descendants(int pid, Func<int, IReadOnlyList<int>>? consoleMembers = null)
    {
        var all = WinProcess.Snapshot();
        var names = new Dictionary<int, string>();
        foreach (var (p, _, name) in all) names[p] = name;
        var start = new Dictionary<int, DateTime?>();
        DateTime? StartOf(int p) => start.TryGetValue(p, out var s) ? s : start[p] = WinProcess.Query(p).Start;
        var rootStart = StartOf(pid);
        var found = new List<ProcEntry>();
        var queue = new Queue<int>([pid]);
        var seen = new HashSet<int> { pid };
        void Add(int p)
        {
            if (!seen.Add(p)) return;
            if (!names.TryGetValue(p, out var name)) return;
            var cs = StartOf(p);
            if (!IsConhost(name)) found.Add(new ProcEntry(p, name, cs));
            queue.Enqueue(p);
        }
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            var ownsConsole = false;
            foreach (var (p, pp, name) in all)
            {
                if (pp != parent) continue;
                // A parent PID can be reused: a real child starts after its parent.
                var ps = StartOf(parent);
                var cs = StartOf(p);
                if (ps != null && cs != null && cs < ps) continue;
                if (IsConhost(name)) ownsConsole = true;
                Add(p);
            }
            if (ownsConsole && parent != pid && consoleMembers != null)
                foreach (var m in consoleMembers(parent))
                {
                    // Only processes started after the session; anything older was there first.
                    var ms = StartOf(m);
                    if (rootStart == null || ms == null || ms >= rootStart) Add(m);
                }
        }
        return found;
    }

    static bool IsConhost(string name) => name.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase);
}

public sealed record LiveRecord(int Pid, string SessionId, DateTime? ProcStart, string? Cwd, string? Status, string? Entrypoint,
                                string? Name, string? PidDomain = null, DateTime Modified = default)
{
    /// A record from another computer, such as a synced folder. Its PID means nothing here.
    public bool IsForeign => PidDomain != null && !string.Equals(PidDomain, LiveSessionReader.ThisPidDomain, StringComparison.OrdinalIgnoreCase);
}

/// Reader for sessions\*.json. Never opens the .key files beside them. Results are not filtered,
/// because the display filter must never apply to safety checks.
public sealed class LiveSessionReader(ConfigLocator locator, IProcessInfo? processes = null)
{
    public static string ThisPidDomain => "win32:" + Environment.MachineName;

    public IProcessInfo Processes { get; } = processes ?? new ProcessInfoProvider();

    /// A record that cannot be read could be a session starting up, so it counts as live for this long.
    public TimeSpan UnreadableGrace { get; init; } = TimeSpan.FromSeconds(60);

    /// A record from another computer counts as live, for safety checks, while it changed this recently.
    public TimeSpan ForeignGrace { get; init; } = TimeSpan.FromMinutes(10);

    /// On Windows, procStart is a JSON string of digits: a file time, 100 ns ticks since 1601 UTC
    /// (S0 check C9). A number is accepted too, and the macOS date string, so recorded test data reads.
    public static DateTime? ParseProcStart(JsonElement v)
    {
        long ticks;
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString()!;
            if (!long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out ticks)) return ParseMacDate(s);
        }
        else if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt64(out ticks)) return null;
        try { return DateTime.FromFileTimeUtc(ticks); } catch (ArgumentOutOfRangeException) { return null; }
    }

    /// "Sat Oct 31 10:58:34 2026", in UTC, as macOS writes it.
    static DateTime? ParseMacDate(string s)
    {
        var normal = string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return DateTime.TryParseExact(normal, "ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture,
                                      DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;
    }

    /// True only if pid is still the process the record describes, so a reused PID never counts.
    public bool IsSameProcess(int pid, DateTime? recordedStart)
    {
        var (state, start) = Processes.Query(pid);
        if (state == WinProcess.ProcessCheck.NotRunning) return false;
        if (recordedStart == null || start == null) return true; // cannot tell, so it counts as live
        return Math.Abs((recordedStart.Value - start.Value).TotalSeconds) <= 1;
    }

    public bool IsLive(LiveRecord r) => !r.IsForeign && IsSameProcess(r.Pid, r.ProcStart);

    public List<LiveRecord> LiveRecords(Profile p) => Read(p).Records.Where(IsLive).ToList();

    /// True if anything may be running. An unreadable, recent record counts, and so does a recent
    /// record from another computer, so a safety check fails closed.
    public bool HasLiveSession(Profile p)
    {
        var r = Read(p);
        return r.UnreadableRecent || r.Records.Any(x => x.IsForeign ? DateTime.UtcNow - x.Modified < ForeignGrace : IsLive(x));
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
            return new LiveRecord(pid, sid.GetString()!, ParseProcStart(ps), S(o, "cwd"), S(o, "status"), S(o, "entrypoint"), S(o, "name"),
                                  S(o, "pidDomain"), File.GetLastWriteTimeUtc(file));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    static string? S(JsonElement o, string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
