import Foundation

/// Runs Claude Code's own commands with a profile's environment. A failure is returned.
public struct ClaudeCLI {
    public let locator: ConfigLocator
    public let runner: ProcessRunning
    public let binary: ClaudeBinaryLocator

    public init(locator: ConfigLocator, runner: ProcessRunning = ProcessRunner(), binary: ClaudeBinaryLocator? = nil) {
        self.locator = locator
        self.runner = runner
        self.binary = binary ?? ClaudeBinaryLocator(locator: locator)
    }

    public func logout(profile: Profile) -> ProcessResult { run(["auth", "logout"], profile: profile, timeout: 30) }

    /// User scope, so it lands in the profile's top-level mcpServers (S0 check 8).
    public func addMCP(name: String, json: String, profile: Profile) -> ProcessResult {
        run(["mcp", "add-json", name, json, "--scope", "user"], profile: profile, timeout: 60)
    }

    public func removeMCP(name: String, profile: Profile) -> ProcessResult {
        run(["mcp", "remove", name, "--scope", "user"], profile: profile, timeout: 60)
    }

    public func addMarketplace(source: String, profile: Profile) -> ProcessResult {
        run(["plugin", "marketplace", "add", source], profile: profile, timeout: 180)
    }

    public func installPlugin(name: String, profile: Profile) -> ProcessResult {
        run(["plugin", "install", name], profile: profile, timeout: 180)
    }

    public func version() -> String? {
        guard let real = binary.realBinary() else { return nil }
        let r = runner.run(executable: real.path, args: ["--version"], env: LaunchEnv(set: ["PATH": binary.path]), timeout: 15)
        guard r.exitCode == 0 else { return nil }
        return r.stdout.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private func run(_ args: [String], profile: Profile, timeout: TimeInterval) -> ProcessResult {
        guard let real = binary.realBinary() else {
            return ProcessResult(stderr: "Cannot find the real claude binary on PATH.", exitCode: 127)
        }
        var env = locator.environment(for: profile)
        env.set["PATH"] = binary.path  // plugin commands run git
        return runner.run(executable: real.path, args: args, env: env, timeout: timeout)
    }
}
