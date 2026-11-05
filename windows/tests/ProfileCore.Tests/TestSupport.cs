using Microsoft.Win32;
using ProfileCore;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace ProfileCore.Tests;

/// A temporary home. Tests never touch the real profiles, Recycle Bin, PATH or Credential Manager.
sealed class Sandbox : IDisposable
{
    public string Home { get; }
    public ConfigLocator Locator { get; }
    public FakeCredentials Credentials { get; } = new();
    public ProfileStore Store { get; }

    public Sandbox()
    {
        Home = Path.Combine(Path.GetTempPath(), "cpm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Home);
        var realHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (PathsEqual(Home, realHome)) throw new InvalidOperationException("Test home must not be the real home");
        Locator = new ConfigLocator(Home);
        if (PathsEqual(Locator.ProfilesRoot, Path.Combine(realHome, ".claude-profiles")))
            throw new InvalidOperationException("Test paths must not be the real profiles folder");
        Store = NewStore();
    }

    public ProfileStore NewStore() => new(Locator, trash: d => Directory.Delete(d, recursive: true), credentials: Credentials);

    static bool PathsEqual(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public string PathOf(string relative) => Path.Combine(Home, relative.Replace('/', '\\'));

    public void Write(string text, string relative)
    {
        var path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// What a sign-in leaves on Windows: the account in .claude.json and the login file.
    public void SignIn(Profile p, string uuid, string email = "a@example.com")
    {
        Directory.CreateDirectory(Locator.ConfigDir(p));
        File.WriteAllText(Locator.ConfigFile(p),
            $$$"""{"oauthAccount":{"accountUuid":"{{{uuid}}}","emailAddress":"{{{email}}}","organizationName":"Org"},"mcpServers":{}}""");
        File.WriteAllText(Locator.CredentialsFile(p), "{}");
    }

    public string Text(string path) => File.ReadAllText(path);

    public void Dispose()
    {
        try { Directory.Delete(Home, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// The login file check is real (it only tests a file exists); Credential Manager names are faked.
sealed class FakeCredentials : ICredentialProbe
{
    public HashSet<string> Names { get; } = [];
    public bool HasLogin(ConfigLocator locator, Profile p) => File.Exists(locator.CredentialsFile(p));
    public IReadOnlySet<string> CredentialManagerNames() => Names;
}

sealed class FakeRunner : IProcessRunner
{
    public List<(string Exe, IReadOnlyList<string> Args, LaunchEnv? Env)> Calls { get; } = [];
    public Func<string, IReadOnlyList<string>, ProcessResult> Handler { get; set; } = (_, _) => new ProcessResult();

    public ProcessResult Run(string exe, IReadOnlyList<string> args, LaunchEnv? env, TimeSpan timeout)
    {
        lock (Calls) Calls.Add((exe, args, env));
        return Handler(exe, args);
    }
}

sealed class FakeProcesses : IProcessInfo
{
    public Dictionary<int, DateTime?> Running { get; } = [];
    public HashSet<int> Denied { get; } = [];

    public (WinProcess.ProcessCheck State, DateTime? Start) Query(int pid) =>
        Denied.Contains(pid) ? (WinProcess.ProcessCheck.AccessDenied, null)
        : Running.TryGetValue(pid, out var s) ? (WinProcess.ProcessCheck.Running, s)
        : (WinProcess.ProcessCheck.NotRunning, null);
}

sealed class FakePath(string system, string user) : IPathSource
{
    public string SystemPath { get; set; } = system;
    public string UserPath { get; set; } = user;
}

sealed class FakeMachine(IPathSource path) : IMachine
{
    public IPathSource Path { get; } = path;
    public HashSet<string> LoginVars { get; } = [];
    public List<string> Profiles { get; } = [];
    public bool? SmartAppControl { get; set; } = false;
    public IEnumerable<string> LoginVariablesSet(IEnumerable<string> names) => names.Where(LoginVars.Contains);
    public IEnumerable<string> PowerShellProfiles() => Profiles;
    public bool? SmartAppControlOn() => SmartAppControl;
}

sealed class FakeUserPathStore(string? value, RegistryValueKind kind = RegistryValueKind.ExpandString) : IUserPathStore
{
    public string? Value { get; private set; } = value;
    public RegistryValueKind Kind { get; private set; } = kind;
    public int Writes { get; private set; }
    public int Broadcasts { get; private set; }

    public (string? Value, RegistryValueKind Kind) Read() => (Value, Kind);
    public void Write(string value, RegistryValueKind kind) { Value = value; Kind = kind; Writes++; }
    public void Broadcast() => Broadcasts++;
}
