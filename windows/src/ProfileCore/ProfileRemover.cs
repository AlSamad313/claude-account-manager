using Microsoft.Win32;
using ProfileCore.Shared;

namespace ProfileCore;

public sealed record RemovalResult(string DisplayName, IReadOnlyList<string> Warnings);

/// BinLeft: some of bin was in use. BinDeletedLater: a hidden cmd deletes it once cpm has exited.
public sealed record UninstallResult(IReadOnlyList<RemovalResult> Removed, bool PathRemoved, IReadOnlyList<string> CompletionRemoved, bool BinLeft, bool BinDeletedLater);

/// Design flow 7 and `cpm uninstall`. Every step is safe to repeat, so a crash at any point continues
/// from the `deleting` state at the next reconcile. The built-in profile, ~\.claude and ~\.claude.json
/// are never touched.
public sealed class ProfileRemover
{
    readonly ProfileStore store;
    readonly ConfigLocator l;
    readonly ClaudeCLI cli;
    readonly LiveSessionReader live;
    readonly Action<string> trash;
    readonly UserPath userPath;
    readonly PowerShellProfile powerShell;
    readonly Action removeStartupValue;
    readonly Func<bool> appRunning;
    readonly Func<string, bool> deleteLater;

    /// The defaults are the real ones. Tests replace every one that reaches outside the profiles folder.
    public ProfileRemover(ProfileStore store, ClaudeCLI? cli = null, LiveSessionReader? live = null, Action<string>? trash = null,
                          UserPath? userPath = null, PowerShellProfile? powerShell = null, Action? removeStartupValue = null,
                          Func<bool>? appRunning = null, Func<string, bool>? deleteLater = null)
    {
        this.store = store;
        l = store.Locator;
        this.cli = cli ?? new ClaudeCLI(l);
        this.live = live ?? new LiveSessionReader(l);
        this.trash = trash ?? RecycleBin.Move;
        this.userPath = userPath ?? new UserPath(l);
        this.powerShell = powerShell ?? new PowerShellProfile(l);
        this.removeStartupValue = removeStartupValue ?? StartupValue.Remove;
        this.appRunning = appRunning ?? AppInstance.IsRunning;
        this.deleteLater = deleteLater ?? WinProcess.DeleteFolderAfterExit;
    }

    public RemovalResult Remove(Profile p)
    {
        if (p.Builtin) throw new ProfileException("The built-in profile cannot be removed.");
        if (live.HasLiveSession(p)) throw new ProfileException($"{p.DisplayName} has a live session. Stop it first.");
        CheckFolder(p);
        // Moves the default to the built-in profile and takes the row out of names first,
        // so nothing can start in this folder from here on.
        store.MarkDeleting(p.Id);
        return Finish(p);
    }

    /// Continues any removal a crash interrupted. A profile that now has a live session waits.
    public List<RemovalResult> ResumePending()
    {
        var done = new List<RemovalResult>();
        foreach (var p in store.Load().Profiles.Where(p => p.State == ProfileState.Deleting && !p.Builtin && !live.HasLiveSession(p)).ToList())
            done.Add(Finish(p));
        return done;
    }

    /// Only a folder inside .claude-profiles\profiles is ever removed.
    void CheckFolder(Profile p)
    {
        if (p.ConfigDir is not { } dir || !PathCanon.IsInside(dir, l.ProfilesDir) || PathCanon.Same(dir, l.ProfilesDir))
            throw new ProfileException($"{p.DisplayName}'s folder is not inside {l.ProfilesDir}, so it was not removed.");
    }

    RemovalResult Finish(Profile p)
    {
        CheckFolder(p);
        var warnings = new List<string>();
        var folder = l.ConfigDir(p);
        var cred = l.CredentialsFile(p);

        // Only when signed in: logout would otherwise recreate a folder that is already gone.
        if (Directory.Exists(folder) && (File.Exists(cred) || AccountReader.ReadAccount(l.ConfigFile(p)) != null))
        {
            var r = cli.Logout(p);
            if (r.ExitCode != 0)
                warnings.Add($"Could not sign {p.DisplayName} out: {r.Stderr.Trim()}. Its login file was deleted anyway.");
        }
        // A login token is never put in the Recycle Bin (flow 7 step 5).
        if (File.Exists(cred)) File.Delete(cred);

        var backups = Path.Combine(l.ProfilesRoot, "backups", p.Id);
        if (Directory.Exists(backups)) trash(backups);
        if (Directory.Exists(folder)) trash(folder);

        store.RemoveRow(p.Id);
        new EventLog(l).Write("delete", p.Id, $"removed {p.CommandName}");
        return new RemovalResult(p.DisplayName, warnings);
    }

    /// Refuses while the app runs or any added profile has a live session, before anything changes.
    /// Then removes every added profile, the PATH entry, the completion line and the startup value,
    /// and moves .claude-profiles to the Recycle Bin. Its bin folder holds only copies of this tool,
    /// some of which may be running (cpm itself), so it is deleted outright once they have exited.
    public UninstallResult Uninstall()
    {
        if (appRunning()) throw new ProfileException("Claude Profiles is running. Exit it from its tray menu first, or it will recreate .claude-profiles. Nothing was changed.");
        var added = store.Load().Profiles.Where(p => !p.Builtin).ToList();
        if (added.FirstOrDefault(live.HasLiveSession) is { } busy)
            throw new ProfileException($"{busy.DisplayName} has a live session. Stop it first. Nothing was changed.");
        foreach (var p in added) CheckFolder(p);

        var removed = added.Select(Remove).ToList();
        var pathRemoved = userPath.Remove();
        var completion = powerShell.Remove();
        removeStartupValue();

        var root = l.ProfilesRoot;
        if (Directory.Exists(root))
            foreach (var item in Directory.EnumerateFileSystemEntries(root).ToList())
                if (!PathCanon.Same(item, l.BinDir) && !PathCanon.Same(item, l.LockFile)) trash(item);

        var left = false;
        if (Directory.Exists(l.BinDir))
        {
            foreach (var f in Directory.EnumerateFiles(l.BinDir))
                try { File.Delete(f); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { left = true; }
            if (!left) Directory.Delete(l.BinDir, recursive: true);
        }
        var later = left && deleteLater(root);
        if (!left)
        {
            try { if (File.Exists(l.LockFile)) File.Delete(l.LockFile); } catch (IOException) { }
            try { if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root); } catch (IOException) { }
        }
        return new UninstallResult(removed, pathRemoved, completion, left, later);
    }
}

/// The app's Start with Windows value (design D4), which `cpm uninstall` takes out.
public static class StartupValue
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string Name = "ClaudeProfiles";

    public static void Remove()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        k?.DeleteValue(Name, throwOnMissingValue: false);
    }
}

/// Whether the tray app is running, by the single-instance mutex it holds (App.xaml.cs).
public static class AppInstance
{
    public static string MutexName
    {
        get
        {
            var t = Environment.GetEnvironmentVariable("CLAUDE_PROFILES_TEST_INSTANCE");
            var ok = t is { Length: > 0 and <= 40 } && t.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
            return @"Local\ClaudeProfiles.Instance" + (ok ? "." + t : "");
        }
    }

    public static bool IsRunning()
    {
        if (!Mutex.TryOpenExisting(MutexName, out var m)) return false;
        m.Dispose();
        return true;
    }
}
