using ProfileCore.Shared;

namespace ProfileCore;

public sealed record InstallResult(bool PathChanged, IReadOnlyList<string> Updated);

/// Owns flow 10 steps 1 to 3. Safe to repeat, and never resets the default.
public sealed class ShellInstaller(ProfileStore store, UserPath? userPath = null)
{
    public const string Version = "1";

    /// bin file name, and the file in the source folder it comes from. One wrapper exe serves as both.
    public static readonly (string Target, string Source)[] Files =
        [("cpm.exe", "cpm.exe"), ("claude.exe", "claude.exe"), ("claude-as.exe", "claude.exe")];

    readonly UserPath userPath = userPath ?? new UserPath(store.Locator);
    ConfigLocator L => store.Locator;

    public InstallResult Install(string sourceDir)
    {
        store.Load();
        Directory.CreateDirectory(L.BinDir);
        Directory.CreateDirectory(L.LogsDir);
        DeleteOldCopies();
        var updated = new List<string>();
        foreach (var (target, source) in Files)
        {
            var from = Path.Combine(sourceDir, source);
            if (!File.Exists(from)) throw new ProfileException($"Missing {from}. Run build.ps1 first.");
            var to = Path.Combine(L.BinDir, target);
            if (PathCanon.Same(PathCanon.Canonical(from), PathCanon.Canonical(to))) continue;
            var data = File.ReadAllBytes(from);
            if (File.Exists(to) && File.ReadAllBytes(to).AsSpan().SequenceEqual(data)) continue;
            Replace(to, data);
            updated.Add(target);
        }
        AtomicFile.Write(Path.Combine(L.BinDir, "VERSION"), System.Text.Encoding.UTF8.GetBytes(Version + "\n"));
        store.RepairFiles();
        var changed = userPath.EnsureFirst();
        return new InstallResult(changed, updated);
    }

    public bool IsInstalled() => Files.All(f => File.Exists(Path.Combine(L.BinDir, f.Target)));

    /// A running exe cannot be overwritten, but it can be renamed. So a busy target is renamed to
    /// <name>.old-<time> first, and old copies are deleted at the next install.
    static void Replace(string to, byte[] data)
    {
        var tmp = to + ".new";
        File.WriteAllBytes(tmp, data);
        try
        {
            File.Move(tmp, to, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            File.Move(to, $"{to}.old-{DateTime.UtcNow:yyyyMMddHHmmss}");
            File.Move(tmp, to);
        }
    }

    void DeleteOldCopies()
    {
        foreach (var f in Directory.EnumerateFiles(L.BinDir, "*.old-*"))
        {
            try { File.Delete(f); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
