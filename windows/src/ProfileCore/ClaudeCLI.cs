namespace ProfileCore;

/// Runs Claude Code's own commands with a profile's environment. A failure is returned, not thrown.
public sealed class ClaudeCLI(ConfigLocator locator, IProcessRunner? runner = null, ClaudeBinaryLocator? binary = null)
{
    readonly IProcessRunner runner = runner ?? new ProcessRunner();
    public ClaudeBinaryLocator Binary { get; } = binary ?? new ClaudeBinaryLocator(locator);

    public ProcessResult Logout(Profile p) => Run(["auth", "logout"], p, TimeSpan.FromSeconds(30));

    /// User scope, so it lands in the profile's top-level mcpServers (S0 check C5).
    public ProcessResult AddMcp(string name, string json, Profile p) => Run(["mcp", "add-json", name, json, "--scope", "user"], p, TimeSpan.FromSeconds(60));
    public ProcessResult RemoveMcp(string name, Profile p) => Run(["mcp", "remove", name, "--scope", "user"], p, TimeSpan.FromSeconds(60));
    public ProcessResult AddMarketplace(string source, Profile p) => Run(["plugin", "marketplace", "add", source], p, TimeSpan.FromSeconds(180));
    public ProcessResult InstallPlugin(string name, Profile p) => Run(["plugin", "install", name], p, TimeSpan.FromSeconds(180));

    public string? Version()
    {
        var real = Binary.RealBinary();
        if (real == null) return null;
        var r = runner.Run(real, ["--version"], null, TimeSpan.FromSeconds(15));
        return r.ExitCode == 0 ? r.Stdout.Trim() : null;
    }

    ProcessResult Run(string[] args, Profile p, TimeSpan timeout)
    {
        var real = Binary.RealBinary();
        if (real == null) return new ProcessResult(Stderr: "Cannot find the real claude.exe on PATH.", ExitCode: 127);
        var env = locator.Environment(p);
        env.Set["PATH"] = Binary.Path; // plugin commands run git
        return runner.Run(real, args, env, timeout);
    }
}
