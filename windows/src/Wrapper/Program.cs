// claude.exe and claude-as.exe (design flow 1). One exe; what it does depends on its own file name.
// Reads names and default on every call, never JSON. The app need not be running.
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ProfileCore.Shared;

namespace Wrapper;

internal static partial class Program
{
    static readonly string[] LoginVars = ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN"];

    static string Home => Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string Root => Path.Combine(Home, ".claude-profiles");
    static string BinDir => Path.Combine(Root, "bin");
    static string NamesPath => Path.Combine(Root, "names");
    static string DefaultPath => Path.Combine(Root, "default");

    static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--profile-label") return ProfileLabel(args.Length > 1 ? args[1] : "");
        var me = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "claude");
        return me.Equals("claude-as", StringComparison.OrdinalIgnoreCase) ? MainClaudeAs(args) : MainClaude(args);
    }

    static int MainClaude(string[] args)
    {
        // A value outside our folder was set on purpose. A value inside may be stale. Blank counts as unset.
        var cfg = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrEmpty(cfg) && !PathCanon.IsInside(PathCanon.Canonical(cfg), PathCanon.Canonical(Root)))
        {
            Err($"CLAUDE_CONFIG_DIR is set to {cfg}, so this session ignores your default profile.");
            var env = new Dictionary<string, string?> { ["CLAUDE_CODE_DISABLE_TERMINAL_TITLE"] = "1" };
            LogLaunch("override", cfg);
            return RunReal(args, env);
        }
        var names = NamesFile.Read(NamesPath);
        var nested = Environment.GetEnvironmentVariable("CLAUDE_PROFILE_ID");
        if (names != null && !string.IsNullOrEmpty(nested))
        {
            var p = names.FirstOrDefault(n => n.Id == nested);
            if (p != null) return Launch(p, names, args);
        }
        var id = NamesFile.ReadDefault(DefaultPath);
        var chosen = id == null || names == null ? null : names.FirstOrDefault(n => n.Id == id);
        if (chosen == null)
        {
            Err($"claude-profiles: cannot read your default profile from {DefaultPath}.");
            Err("Claude Code was not started, to avoid using the wrong account. Run: cpm doctor --fix");
            return 1;
        }
        return Launch(chosen, names!, args);
    }

    static int MainClaudeAs(string[] args)
    {
        var names = NamesFile.Read(NamesPath);
        if (args.Length == 0)
        {
            Err("Usage: claude-as <name> [claude arguments]");
            ListNames(names);
            return 1;
        }
        if (names == null)
        {
            Err($"claude-profiles: cannot read {NamesPath}. Run: cpm doctor --fix");
            return 1;
        }
        var p = names.FirstOrDefault(n => n.CommandName == args[0]);
        if (p == null)
        {
            Err($"claude-profiles: no profile named '{args[0]}'. Valid names:");
            ListNames(names);
            return 1;
        }
        return Launch(p, names, args[1..]);
    }

    static int Launch(NameEntry p, List<NameEntry> names, string[] args)
    {
        var sid = ResumeId(args);
        if (sid != null && !CheckOwner(sid, p, names)) return 1;
        foreach (var v in LoginVars)
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v)))
                Err($"{v} is set. Claude Code will report it instead of this profile's login.");
        // The built-in profile removes the variable. A blank value would make Claude Code report signed out.
        var env = new Dictionary<string, string?>
        {
            ["CLAUDE_CONFIG_DIR"] = p.IsBuiltin ? null : p.ConfigDir,
            ["CLAUDE_PROFILE_ID"] = p.Id,
            ["CLAUDE_CODE_DISABLE_TERMINAL_TITLE"] = "1",
        };
        ConsoleControl.SetTitle(p.DisplayName);
        LogLaunch(p.Id, FolderOf(p.ConfigDir));
        return RunReal(args, env);
    }

    static int RunReal(string[] args, Dictionary<string, string?> env)
    {
        var found = RealClaude.Find(Environment.GetEnvironmentVariable("PATH") ?? "", BinDir);
        if (found.Exe == null)
        {
            Err("claude-profiles: cannot find the real claude binary on PATH.");
            if (found.ScriptOnly != null)
                Err($"Found {found.ScriptOnly}, which is not run. Install the native Claude Code: https://docs.claude.com/en/docs/claude-code/setup");
            return 1;
        }
        var psi = new ProcessStartInfo(found.Exe) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var (k, v) in env)
        {
            if (v == null) psi.Environment.Remove(k);
            else psi.Environment[k] = v;
        }
        ConsoleControl.IgnoreCtrlC();
        Process child;
        try { child = Process.Start(psi)!; }
        catch (Exception e)
        {
            Err($"claude-profiles: could not start {found.Exe}: {e.Message}");
            return 1;
        }
        child.WaitForExit();
        return child.ExitCode;
    }

    /// The session ID in --resume <id>, --resume=<id> or -r <id>, if there is one.
    static string? ResumeId(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--resume=", StringComparison.Ordinal)) return a["--resume=".Length..];
            if (a is "--resume" or "-r") return i + 1 < args.Length ? args[i + 1] : null;
        }
        return null;
    }

    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex Uuid();

    /// Refuses a session that belongs to another profile. Anything that is not a UUID, or that no
    /// profile has, is left to Claude Code.
    static bool CheckOwner(string sid, NameEntry p, List<NameEntry> names)
    {
        if (!Uuid().IsMatch(sid)) return true;
        if (HasTranscript(FolderOf(p.ConfigDir), sid)) return true;
        foreach (var other in names)
        {
            if (other.Id == p.Id) continue;
            if (!HasTranscript(FolderOf(other.ConfigDir), sid)) continue;
            Err($"Session {sid} belongs to profile {other.DisplayName}.");
            Err($"Use: claude-as {other.CommandName} --resume {sid}");
            return false;
        }
        return true;
    }

    static bool HasTranscript(string folder, string sid)
    {
        var projects = Path.Combine(folder, "projects");
        if (!Directory.Exists(projects)) return false;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(projects))
                if (File.Exists(Path.Combine(dir, sid + ".jsonl"))) return true;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    static string FolderOf(string configDir) => configDir == "-" ? Path.Combine(Home, ".claude") : configDir;

    static void ListNames(List<NameEntry>? names)
    {
        foreach (var n in names ?? []) Err("  " + n.CommandName);
    }

    /// A logging failure never blocks a launch.
    static void LogLaunch(string label, string folder)
    {
        try
        {
            var line = string.Join('\t', DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), label,
                                   Clean(Environment.CurrentDirectory), Clean(folder)) + "\n";
            File.AppendAllText(Path.Combine(Root, "logs", "launch.log"), line, new UTF8Encoding(false));
        }
        catch (Exception) { }
    }

    static string Clean(string s) => s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    /// The status line command for an added profile: a coloured dot and the display name.
    /// Prints nothing for an unknown ID. Written as UTF-8 bytes, whatever the console code page.
    static int ProfileLabel(string id)
    {
        var p = NamesFile.Read(NamesPath)?.FirstOrDefault(n => n.Id == id);
        if (p == null) return 0;
        var hex = p.Color.TrimStart('#');
        var dot = "●";
        if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            dot = $"\u001b[38;2;{(rgb >> 16) & 255};{(rgb >> 8) & 255};{rgb & 255}m●\u001b[0m";
        var bytes = new UTF8Encoding(false).GetBytes($"{dot} {p.DisplayName}\n");
        using var o = Console.OpenStandardOutput();
        o.Write(bytes);
        return 0;
    }

    static void Err(string s) => Console.Error.WriteLine(s);
}
