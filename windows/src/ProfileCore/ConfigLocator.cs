using ProfileCore.Shared;

namespace ProfileCore;

public sealed class LaunchEnv
{
    public Dictionary<string, string> Set { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Unset { get; } = [];
}

/// The one place that turns a profile into paths and an environment. Home and the profiles root are
/// passed in, so tests never touch real paths.
public sealed class ConfigLocator(string home, string? profilesRoot = null)
{
    public string Home { get; } = home;
    public string ProfilesRoot { get; } = profilesRoot ?? Path.Combine(home, ".claude-profiles");

    /// Uses USERPROFILE when it is set, so a test or a temporary home is honoured.
    public static ConfigLocator Current() =>
        new(System.Environment.GetEnvironmentVariable("USERPROFILE") is { Length: > 0 } h ? h : System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));

    public string BuiltinDir => Path.Combine(Home, ".claude");
    public string ProfilesDir => Path.Combine(ProfilesRoot, "profiles");
    public string BinDir => Path.Combine(ProfilesRoot, "bin");
    public string LogsDir => Path.Combine(ProfilesRoot, "logs");
    public string RegistryFile => Path.Combine(ProfilesRoot, "profiles.json");
    public string DefaultFile => Path.Combine(ProfilesRoot, "default");
    public string NamesFile => Path.Combine(ProfilesRoot, "names");
    public string LockFile => Path.Combine(ProfilesRoot, ".lock");

    public string ConfigDir(Profile p) => p.ConfigDir ?? BuiltinDir;

    public string ConfigFile(Profile p) =>
        p.ConfigDir == null ? Path.Combine(Home, ".claude.json") : Path.Combine(p.ConfigDir, ".claude.json");

    /// The login file. Only whether it exists is ever checked.
    public string CredentialsFile(Profile p) => Path.Combine(ConfigDir(p), ".credentials.json");

    /// The only place a configDir is built: the long, final path of the profiles folder, so no 8.3
    /// names, drive letter in upper case, backslashes. The profiles root must exist.
    public string NewConfigDir(string id) => PathCanon.Canonical(ProfilesRoot) + "\\profiles\\" + id;

    /// For commands run directly. Sessions get their environment from the wrapper. The built-in
    /// profile removes CLAUDE_CONFIG_DIR, never sets it, not even to "".
    public LaunchEnv Environment(Profile p)
    {
        var env = new LaunchEnv();
        env.Set["CLAUDE_PROFILE_ID"] = p.Id;
        if (p.ConfigDir != null) env.Set["CLAUDE_CONFIG_DIR"] = p.ConfigDir;
        else env.Unset.Add("CLAUDE_CONFIG_DIR");
        return env;
    }
}
