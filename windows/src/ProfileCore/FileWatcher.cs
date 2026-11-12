namespace ProfileCore;

/// Calls back when profiles.json, default or names change, for example after a cpm command, so the
/// tray icon colour and the lists follow. Only the top of .claude-profiles is watched: profile
/// folders, logs and run requests change all the time and are read on their own schedule.
/// Bursts are merged into one call. A buffer overflow calls back too, because the caller re-reads
/// everything anyway.
public sealed class FileWatcher : IDisposable
{
    readonly FileSystemWatcher watcher;
    readonly Timer timer;
    readonly Action onChange;
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

    public FileWatcher(string folder, Action onChange)
    {
        this.onChange = onChange;
        Directory.CreateDirectory(folder);
        timer = new Timer(_ => this.onChange(), null, Timeout.Infinite, Timeout.Infinite);
        watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Changed += (_, e) => Seen(e.Name);
        watcher.Created += (_, e) => Seen(e.Name);
        watcher.Deleted += (_, e) => Seen(e.Name);
        watcher.Renamed += (_, e) => { Seen(e.Name); Seen(e.OldName); };
        watcher.Error += (_, _) => Kick();
        watcher.EnableRaisingEvents = true;
    }

    /// The files cpm and the app write. Temporary files from AtomicFile end in .tmp and are ignored;
    /// the rename onto the real name is what counts.
    static readonly HashSet<string> Watched = new(StringComparer.OrdinalIgnoreCase) { "profiles.json", "default", "names" };

    void Seen(string? name)
    {
        if (name != null && Watched.Contains(name)) Kick();
    }

    void Kick() => timer.Change(Quiet, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        watcher.Dispose();
        timer.Dispose();
    }
}
