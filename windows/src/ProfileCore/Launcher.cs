using System.Text;
using ProfileCore.Shared;

namespace ProfileCore;

/// A one-method test seam: starts a command line in a new console window.
public interface ITerminalOpener
{
    void Open(string commandLine, string workingDir);
}

public sealed class RealTerminalOpener : ITerminalOpener
{
    public void Open(string commandLine, string workingDir) => WinProcess.StartInNewConsole(commandLine, workingDir);
}

/// Starts a program directly, with given changes to a fresh user environment. A null value removes the variable.
public interface IProgramStarter
{
    void Start(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string?> envChanges, string workingDir);
}

public sealed class RealProgramStarter : IProgramStarter
{
    public void Start(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string?> envChanges, string workingDir)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = workingDir };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment.Clear();
        foreach (var (k, v) in WinProcess.FreshEnvironment()) psi.Environment[k] = v;
        foreach (var (k, v) in envChanges)
        {
            if (v == null) psi.Environment.Remove(k);
            else psi.Environment[k] = v;
        }
        try { System.Diagnostics.Process.Start(psi)?.Dispose(); }
        catch (System.ComponentModel.Win32Exception e) { throw new ProfileException($"Could not start {exe}: {e.Message}"); }
    }
}

/// Finds VS Code, and tells whether that install is running (design flow 12).
public static class VSCode
{
    public static string? Find(string? path = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var c in new[] { Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"), Path.Combine(programs, "Microsoft VS Code", "Code.exe") })
            if (File.Exists(c)) return c;
        // code.cmd lives in <install>\bin, beside nothing else; Code.exe is one folder up.
        foreach (var entry in (path ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Shared.RealClaude.Clean(entry) is not { } dir) continue;
            if (!File.Exists(Path.Combine(dir, "code.cmd"))) continue;
            var exe = Path.Combine(Path.GetDirectoryName(dir.TrimEnd('\\')) ?? dir, "Code.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    /// A new Code.exe hands its request to a running one of the same install, and the new
    /// environment is lost, so a running copy means the profile cannot be applied.
    public static bool IsRunning(string codeExe) =>
        System.Diagnostics.Process.GetProcessesByName("Code").Any(p =>
        {
            using (p) return WinProcess.ImagePath(p.Id) is { } img && Shared.PathCanon.Same(img, codeExe);
        });
}

public sealed class LauncherException(string message, string? missingFolder = null) : ProfileException(message)
{
    /// Set when the folder to open no longer exists, so the app can offer to choose another.
    public string? MissingFolder { get; } = missingFolder;

    public static LauncherException VSCodeRunning() => new("VS Code is already running, so it cannot switch profile. Quit VS Code, then try again.");
    public static LauncherException VSCodeMissing() => new("VS Code was not found. Install it from https://code.visualstudio.com, then try again.");
    public static LauncherException ShellNotInstalled() => new("The command line tools are not installed. Run: cpm install-shell");
    public static LauncherException SessionIsLive(int? pid) =>
        new($"That session is already running{(pid is { } p ? $" (pid {p})" : "")}. Stop it first, or switch to its window.");
    public static LauncherException FolderMissing(string f) => new($"The session's folder no longer exists: {f}", f);
    public static LauncherException NoOwner(string id) => new($"No profile holds session {id}.");
}

/// The only module that opens terminal windows. It writes a launch request (data, never run) and
/// starts bin\claude-as.exe --launch <file>, so the launch rules exist once, in the wrapper (flow 2).
public sealed class Launcher
{
    readonly ConfigLocator locator;
    readonly ITerminalOpener opener;
    public Func<bool> IsShellInstalled { get; init; }
    public Func<DateTime> Now { get; init; } = () => DateTime.UtcNow;

    public Launcher(ProfileStore store, ITerminalOpener? opener = null)
    {
        locator = store.Locator;
        this.opener = opener ?? new RealTerminalOpener();
        var installer = new ShellInstaller(store);
        IsShellInstalled = installer.IsInstalled;
    }

    public string RunDir => Path.Combine(locator.ProfilesRoot, "run");

    public void NewSession(Profile p, string? folder = null)
    {
        var dir = folder == null ? locator.Home : Path.GetFullPath(folder);
        if (!Directory.Exists(dir)) throw LauncherException.FolderMissing(dir);
        Launch(p, dir, []);
    }

    public void Resume(SessionRow session, Profile owner, string? folderOverride = null)
    {
        if (session.Live) throw LauncherException.SessionIsLive(session.Pid);
        // It comes from a file name or a record. Anything else would skip the wrapper's ownership check.
        if (!Guid.TryParseExact(session.SessionId, "D", out _)) throw new ProfileException("That session ID is not valid.");
        var folder = folderOverride ?? session.Cwd ?? locator.Home;
        if (!Directory.Exists(folder)) throw LauncherException.FolderMissing(folder);
        Launch(owner, folder, ["--resume", session.SessionId]);
    }

    public void Login(Profile p) => Launch(p, locator.Home, ["auth", "login"]);

    public IProgramStarter Starter { get; init; } = new RealProgramStarter();
    public Func<string?> FindVSCode { get; init; } = () => VSCode.Find();
    public Func<string, bool> IsVSCodeRunning { get; init; } = VSCode.IsRunning;

    /// Flow 12. Code.exe is started directly with the profile's variables; the built-in profile
    /// removes CLAUDE_CONFIG_DIR. No shell or terminal is involved, so the wrapper is not needed.
    public void OpenVSCode(Profile p, string? folder = null)
    {
        var exe = FindVSCode() ?? throw LauncherException.VSCodeMissing();
        string? dir = null;
        if (folder != null)
        {
            dir = Path.GetFullPath(folder);
            if (!Directory.Exists(dir)) throw LauncherException.FolderMissing(dir);
        }
        if (IsVSCodeRunning(exe)) throw LauncherException.VSCodeRunning();
        var env = locator.Environment(p);
        var changes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in env.Set) changes[k] = v;
        foreach (var k in env.Unset) changes[k] = null;
        Starter.Start(exe, dir == null ? [] : [dir], changes, dir ?? locator.Home);
        new EventLog(locator).Write("vscode", p.Id, dir ?? "(no folder)");
    }

    void Launch(Profile p, string folder, string[] arguments)
    {
        if (!IsShellInstalled()) throw LauncherException.ShellNotInstalled();
        Directory.CreateDirectory(RunDir);
        CleanOldFiles();
        var file = Path.Combine(RunDir, Guid.NewGuid().ToString("D") + LaunchRequest.Extension);
        AtomicFile.Write(file, new LaunchRequest(p.CommandName, folder, arguments, Now()).ToJson());
        opener.Open(CommandLine(Path.Combine(locator.BinDir, "claude-as.exe"), "--launch", file), locator.Home);
    }

    /// Only the fixed bin path and the request path, both under .claude-profiles. The folder from
    /// transcript data never reaches a command line.
    public static string CommandLine(params string[] args) => string.Join(' ', args.Select(Quote));

    /// The CommandLineToArgvW rule: quotes around, backslashes doubled only before a quote.
    public static string Quote(string a)
    {
        if (a.Length > 0 && a.IndexOfAny([' ', '\t', '"']) < 0) return a;
        var sb = new StringBuilder("\"");
        var slashes = 0;
        foreach (var ch in a)
        {
            if (ch == '\\') { slashes++; continue; }
            sb.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            sb.Append(ch);
        }
        sb.Append('\\', slashes * 2).Append('"');
        return sb.ToString();
    }

    /// A request the wrapper never read, because its window failed to open, goes at the next launch.
    void CleanOldFiles()
    {
        foreach (var f in Directory.EnumerateFiles(RunDir, "*" + LaunchRequest.Extension))
        {
            try { if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > LaunchRequest.MaxAge) File.Delete(f); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
