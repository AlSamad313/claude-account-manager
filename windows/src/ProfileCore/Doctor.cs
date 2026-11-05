using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProfileCore.Shared;

namespace ProfileCore;

public enum FindingLevel { Ok, Note, Fault }

public sealed record Finding(FindingLevel Level, string Message, string? Fix = null, string? ProfileId = null);

public sealed record DoctorReport(IReadOnlyList<Finding> Findings)
{
    public bool HasFaults => Findings.Any(f => f.Level == FindingLevel.Fault);
}

/// What doctor reads from the machine, so tests can supply their own.
public interface IMachine
{
    IPathSource Path { get; }
    /// Login variables set for new processes or this one: names only.
    IEnumerable<string> LoginVariablesSet(IEnumerable<string> names);
    IEnumerable<string> PowerShellProfiles();
    bool? SmartAppControlOn();
}

public sealed class RealMachine : IMachine
{
    public IPathSource Path { get; } = new RegistryPath();

    public IEnumerable<string> LoginVariablesSet(IEnumerable<string> names) => names.Where(n =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(n)) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(n, EnvironmentVariableTarget.User)) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(n, EnvironmentVariableTarget.Machine)));

    public IEnumerable<string> PowerShellProfiles()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var dir in new[] { "WindowsPowerShell", "PowerShell" })
            foreach (var f in new[] { "Microsoft.PowerShell_profile.ps1", "profile.ps1" })
                yield return System.IO.Path.Combine(docs, dir, f);
    }

    public bool? SmartAppControlOn()
    {
        using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy");
        return k?.GetValue("VerifiedAndReputablePolicyState") is int v ? v is 1 or 2 : null;
    }
}

/// Implements the flow 11 table. Reads only; a fix is a separate step.
public sealed partial class Doctor(ConfigLocator locator, ICredentialProbe? credentials = null, IProcessRunner? runner = null, IMachine? machine = null)
{
    static readonly string[] LoginVars = ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN"];
    static readonly string[] Extensions = [".com", ".exe", ".bat", ".cmd"];

    readonly ICredentialProbe credentials = credentials ?? new CredentialProbe();
    readonly IProcessRunner runner = runner ?? new ProcessRunner();
    readonly IMachine machine = machine ?? new RealMachine();

    /// Reads the registry without seeding it, so doctor changes nothing.
    Registry? ReadRegistry()
    {
        try { return JsonSerializer.Deserialize(File.ReadAllBytes(locator.RegistryFile), CoreJson.Default.Registry); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public DoctorReport Run()
    {
        var f = new List<Finding>();
        var reg = ReadRegistry();
        if (reg == null) return new([new(FindingLevel.Fault, $"No readable profile list at {locator.RegistryFile}.", "cpm install-shell")]);

        foreach (var p in reg.Profiles) CheckProfile(p, f);

        var known = reg.Profiles.Where(p => p.ConfigDir != null).Select(p => System.IO.Path.GetFileName(p.ConfigDir!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(locator.ProfilesDir))
            foreach (var orphan in Directory.EnumerateDirectories(locator.ProfilesDir).Select(System.IO.Path.GetFileName).Order())
                if (orphan != null && !orphan.StartsWith('.') && !known.Contains(orphan))
                    f.Add(new(FindingLevel.Note, $"Folder profiles\\{orphan} has no profile row. It was left alone."));

        CheckDefault(reg, f);
        CheckBinaries(f);
        foreach (var v in machine.LoginVariablesSet(LoginVars))
            f.Add(new(FindingLevel.Note, $"{v} is set. Claude Code reports it instead of a profile's login."));
        CheckPowerShellProfiles(f);
        foreach (var n in credentials.CredentialManagerNames().Order())
            f.Add(new(FindingLevel.Note, $"Credential Manager has an item named {n}. Claude Code may have changed where it keeps logins."));
        CheckAccess(f);
        if (!WinProcess.HasRecycleBin(locator.ProfilesRoot))
            f.Add(new(FindingLevel.Note, $"The drive holding {locator.ProfilesRoot} has no Recycle Bin, so removing a profile would stop before deleting its folder."));
        if (machine.SmartAppControlOn() == true)
            f.Add(new(FindingLevel.Note, "Smart App Control is on. Windows may block the self-built, unsigned claude.exe wrapper."));
        if (File.Exists(System.IO.Path.Combine(locator.BuiltinDir, ".claude.json")))
            f.Add(new(FindingLevel.Note, "~\\.claude\\.claude.json exists. The app never reads it."));
        return new(f);
    }

    void CheckProfile(Profile p, List<Finding> f)
    {
        if (!p.Builtin)
        {
            if (!Directory.Exists(locator.ConfigDir(p)))
            {
                f.Add(new(FindingLevel.Fault, $"{p.DisplayName}: its folder is missing.", "Restore the folder, or remove the profile.", p.Id));
                return;
            }
            if (p.ConfigDir != locator.NewConfigDir(p.Id))
                f.Add(new(FindingLevel.Fault, $"{p.DisplayName}: its stored path is not the one made for its ID, such as a short 8.3 name or another spelling. It may look logged out.", null, p.Id));
        }
        if (p.State == ProfileState.PendingLogin)
        {
            f.Add(new(FindingLevel.Note, $"{p.DisplayName}: waiting for a login.", $"claude-as {p.CommandName} auth login, then cpm keep {p.CommandName}", p.Id));
            return;
        }
        var a = SignIn.Account(locator, credentials, p);
        if (a != null) f.Add(new(FindingLevel.Ok, $"{p.DisplayName}: signed in as {a.Email ?? "an account"}.", null, p.Id));
        else f.Add(new(FindingLevel.Fault, $"{p.DisplayName}: not signed in.", $"claude-as {p.CommandName} auth login", p.Id));
    }

    void CheckDefault(Registry reg, List<Finding> f)
    {
        var text = NamesFile.ReadDefault(locator.DefaultFile);
        var inNames = NamesFile.Read(locator.NamesFile)?.Any(n => n.Id == reg.DefaultProfileId) == true;
        if (text == reg.DefaultProfileId && inNames)
            f.Add(new(FindingLevel.Ok, $"Default profile: {reg.ProfileById(reg.DefaultProfileId)?.DisplayName ?? reg.DefaultProfileId}."));
        else
            f.Add(new(FindingLevel.Fault, "The default or names file does not match the profile list. Plain claude will refuse to start.", "cpm doctor --fix"));
    }

    /// For a new process: the system PATH, then the user PATH, as every shell searches them.
    void CheckBinaries(List<Finding> f)
    {
        var path = RegistryPath.Combined(machine.Path);
        var real = RealClaude.Find(path, locator.BinDir);
        if (real.Exe != null)
        {
            f.Add(new(FindingLevel.Ok, $"Real Claude Code: {real.Exe}."));
            var v = runner.Run(real.Exe, ["--version"], null, TimeSpan.FromSeconds(15));
            if (v.ExitCode == 0) f.Add(new(FindingLevel.Note, $"Claude Code version: {v.Stdout.Trim()}."));
        }
        else if (real.ScriptOnly != null)
            f.Add(new(FindingLevel.Fault, $"Only {real.ScriptOnly} was found, and it is never run.", "Install the native Claude Code (claude.exe)."));
        else
            f.Add(new(FindingLevel.Fault, "Cannot find the real claude.exe on your PATH."));

        var wrapper = System.IO.Path.Combine(locator.BinDir, "claude.exe");
        var first = FirstClaude(path);
        if (first != null && PathCanon.Same(PathCanon.Canonical(first), PathCanon.Canonical(wrapper)))
            f.Add(new(FindingLevel.Ok, "Plain claude runs the wrapper, in a new window."));
        else
            f.Add(new(FindingLevel.Fault,
                $"Plain claude runs {first ?? "nothing"}, not the wrapper, so it ignores your default. System PATH: {machine.Path.SystemPath} User PATH: {machine.Path.UserPath}",
                "cpm install-shell, then open a new terminal window"));
    }

    /// The file a shell would run for "claude": the first PATH entry holding claude with a runnable extension.
    static string? FirstClaude(string path)
    {
        foreach (var raw in path.Split(';'))
        {
            var e = RealClaude.Clean(raw);
            if (e == null) continue;
            foreach (var ext in Extensions)
            {
                var c = System.IO.Path.Combine(e, "claude" + ext);
                if (File.Exists(c)) return c;
            }
        }
        return null;
    }

    [GeneratedRegex(@"^\s*(function\s+(global:)?claude\b|(Set|New)-Alias\b.*\bclaude\b)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ClaudeDefinition();

    void CheckPowerShellProfiles(List<Finding> f)
    {
        foreach (var file in machine.PowerShellProfiles())
        {
            string text;
            try { text = File.ReadAllText(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (ClaudeDefinition().IsMatch(text))
                f.Add(new(FindingLevel.Note, $"{file} defines an alias or function named claude. PowerShell runs it before the wrapper."));
        }
    }

    /// Notes any account other than this user, SYSTEM and Administrators that has access.
    void CheckAccess(List<Finding> f)
    {
        try
        {
            var dir = new DirectoryInfo(locator.ProfilesRoot);
            if (!dir.Exists) return;
            var me = WindowsIdentity.GetCurrent().User;
            var allowed = new HashSet<string> { "S-1-5-18", "S-1-5-32-544", "S-1-3-0", "S-1-3-4" };
            if (me != null) allowed.Add(me.Value);
            var others = new SortedSet<string>();
            foreach (FileSystemAccessRule r in dir.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (r.AccessControlType == AccessControlType.Allow && !allowed.Contains(r.IdentityReference.Value))
                    others.Add(Name((SecurityIdentifier)r.IdentityReference));
            foreach (var o in others)
                f.Add(new(FindingLevel.Note, $"{locator.ProfilesRoot} gives access to {o}. It holds your profiles' logins."));
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or InvalidOperationException) { }
    }

    static string Name(SecurityIdentifier sid)
    {
        try { return sid.Translate(typeof(NTAccount)).Value; } catch (IdentityNotMappedException) { return sid.Value; }
    }
}
