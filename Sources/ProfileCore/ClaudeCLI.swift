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

    public func version() -> String? {
        guard let real = binary.realBinary() else { return nil }
        let r = runner.run(executable: real.path, args: ["--version"], env: nil, timeout: 15)
        guard r.exitCode == 0 else { return nil }
        return r.stdout.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private func run(_ args: [String], profile: Profile, timeout: TimeInterval) -> ProcessResult {
        guard let real = binary.realBinary() else {
            return ProcessResult(stderr: "Cannot find the real claude binary on PATH.", exitCode: 127)
        }
        return runner.run(executable: real.path, args: args, env: locator.environment(for: profile), timeout: timeout)
    }
}
