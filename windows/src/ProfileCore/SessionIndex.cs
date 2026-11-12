namespace ProfileCore;

public sealed record SessionRow(string ProfileId, string SessionId, string? Cwd, string Title, DateTime LastActivity, bool Live,
                                string? Status, string? Entrypoint, int? Pid, DateTime? ProcStart, bool MayBeStale);

public sealed record SessionIndexResult(IReadOnlyList<SessionRow> Rows, int ParseFailures);

/// Merged session list. For display only: safety checks use LiveSessionReader, never this,
/// because this hides desktop app sessions.
public sealed class SessionIndex(ConfigLocator locator, IProcessInfo? processes = null)
{
    /// Hidden from the list. S0 check C10 saw cli, claude-desktop and sdk-cli; an unknown value is shown.
    public static readonly HashSet<string> HiddenEntrypoints = ["claude-desktop"];

    public LiveSessionReader Live { get; } = new(locator, processes);
    readonly Dictionary<string, (long Size, DateTime Modified, TranscriptSummary? Summary)> cache = new(StringComparer.OrdinalIgnoreCase);
    readonly object cacheLock = new();

    public SessionIndexResult Sessions(IEnumerable<Profile> profiles)
    {
        var rows = new List<SessionRow>();
        var failures = 0;
        foreach (var p in profiles)
        {
            var (summaries, failed) = Transcripts(p);
            failures += failed;
            var byId = new Dictionary<string, TranscriptSummary>();
            foreach (var t in summaries)
                if (!byId.TryGetValue(t.SessionId, out var old) || t.LastActivity > old.LastActivity) byId[t.SessionId] = t;
            // A live record with no transcript yet still gives a row. If a session has several live
            // records, the newest process wins.
            var recordById = new Dictionary<string, LiveRecord>();
            foreach (var r in Live.LiveRecords(p).OrderBy(r => r.ProcStart ?? DateTime.MinValue)) recordById[r.SessionId] = r;
            foreach (var (id, rec) in recordById)
            {
                byId.Remove(id, out var t);
                rows.Add(Row(p, id, t, rec));
            }
            foreach (var (id, t) in byId) rows.Add(Row(p, id, t, null));
        }
        var shown = rows.Where(r => r.Entrypoint == null || !HiddenEntrypoints.Contains(r.Entrypoint))
            .OrderByDescending(r => r.Live).ThenByDescending(r => r.LastActivity).ThenBy(r => r.SessionId, StringComparer.Ordinal);
        return new SessionIndexResult(shown.ToList(), failures);
    }

    public Profile? OwnerOf(string sessionId, IEnumerable<Profile> profiles)
    {
        foreach (var p in profiles)
        {
            var root = Path.Combine(locator.ConfigDir(p), "projects");
            if (Directory.Exists(root) && Directory.EnumerateDirectories(root).Any(d => File.Exists(Path.Combine(d, sessionId + ".jsonl")))) return p;
            if (Live.LiveRecords(p).Any(r => r.SessionId == sessionId)) return p;
        }
        return null;
    }

    static SessionRow Row(Profile p, string id, TranscriptSummary? t, LiveRecord? r)
    {
        var name = string.IsNullOrEmpty(r?.Name) ? null : r.Name;
        return new SessionRow(p.Id, id, r?.Cwd ?? t?.Cwd, name ?? t?.Title ?? "Untitled", t?.LastActivity ?? DateTime.UtcNow, r != null,
                              r?.Status, r?.Entrypoint ?? t?.Entrypoint, r?.Pid, r?.ProcStart, r != null && r.ProcStart == null);
    }

    (List<TranscriptSummary>, int) Transcripts(Profile p)
    {
        var root = Path.Combine(locator.ConfigDir(p), "projects");
        var output = new List<TranscriptSummary>();
        var failed = 0;
        var toRead = new List<(string Path, long Size, DateTime Modified)>();
        if (!Directory.Exists(root)) return (output, 0);
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*.jsonl"))
            {
                (long, DateTime, TranscriptSummary?) c;
                bool hit;
                lock (cacheLock) hit = cache.TryGetValue(file.FullName, out c);
                if (hit && c.Item1 == file.Length && c.Item2 == file.LastWriteTimeUtc)
                {
                    if (c.Item3 != null) output.Add(c.Item3); else failed++;
                }
                else toRead.Add((file.FullName, file.Length, file.LastWriteTimeUtc));
            }
        }
        var read = new TranscriptSummary?[toRead.Count];
        Parallel.For(0, toRead.Count, i => read[i] = TranscriptReader.Summary(toRead[i].Path, toRead[i].Size, toRead[i].Modified));
        lock (cacheLock)
            for (var i = 0; i < toRead.Count; i++)
            {
                cache[toRead[i].Path] = (toRead[i].Size, toRead[i].Modified, read[i]);
                if (read[i] is { } s) output.Add(s); else failed++;
            }
        return (output, failed);
    }
}
