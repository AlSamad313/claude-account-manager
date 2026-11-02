import Foundation

/// A one-method test seam. The real implementation is used by both the app and cpm.
public protocol TerminalOpener {
    func open(commandFile: URL) throws
}

public struct RealTerminalOpener: TerminalOpener {
    public let runner: ProcessRunning

    public init(runner: ProcessRunning = ProcessRunner()) { self.runner = runner }

    public func open(commandFile: URL) throws {
        let r = runner.run(executable: "/usr/bin/open", args: ["-a", "Terminal", commandFile.path], env: nil, timeout: 15)
        guard r.exitCode == 0 else { throw ProfileError.failed("Could not open Terminal: \(r.stderr)") }
    }
}

public enum LauncherError: Error, Equatable, CustomStringConvertible {
    case shellNotInstalled
    case sessionIsLive(pid: Int32?)
    case folderMissing(String)
    case noOwner(String)
    case vsCodeRunning

    public var description: String {
        switch self {
        case .shellNotInstalled: return "The shell layer is not installed. Run: cpm install-shell"
        case .sessionIsLive(let pid): return "That session is already running\(pid.map { " (pid \($0))" } ?? ""). Stop it first, or switch to its window."
        case .folderMissing(let f): return "The session's folder no longer exists: \(f)"
        case .noOwner(let id): return "No profile holds session \(id)."
        case .vsCodeRunning: return "VS Code is already running, so it cannot switch profile. Quit VS Code, then try again."
        }
    }
}

/// The only module that opens Terminal sessions. The .command file only changes folder and
/// calls claude-as, so the launch rules exist once, in the wrapper (design flow 2).
public struct Launcher {
    public let locator: ConfigLocator
    public let opener: TerminalOpener
    public let runner: ProcessRunning
    public var isShellInstalled: () -> Bool
    public var maxFileAge: TimeInterval = 60

    public init(store: ProfileStore, opener: TerminalOpener = RealTerminalOpener(), runner: ProcessRunning = ProcessRunner()) {
        self.locator = store.locator
        self.opener = opener
        self.runner = runner
        let installer = ShellInstaller(store: store)
        self.isShellInstalled = { installer.isInstalled() }
    }

    private var runDir: URL { locator.profilesRoot.appendingPathComponent("run") }

    public func newSession(profile: Profile, folder: URL? = nil) throws {
        try launch(profile: profile, folder: folder ?? locator.home, arguments: [])
    }

    public func resume(session: SessionRow, owner: Profile, folderOverride: URL? = nil) throws {
        guard !session.live else { throw LauncherError.sessionIsLive(pid: session.pid) }
        // It comes from a file name or a record. Anything else would skip the wrapper's ownership check.
        guard UUID(uuidString: session.sessionId) != nil else { throw ProfileError.notAllowed("That session ID is not valid.") }
        let folder = folderOverride ?? session.cwd.map { URL(fileURLWithPath: $0) } ?? locator.home
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: folder.path, isDirectory: &isDir), isDir.boolValue else {
            throw LauncherError.folderMissing(folder.path)
        }
        try launch(profile: owner, folder: folder, arguments: ["--resume", session.sessionId])
    }

    public func login(profile: Profile) throws {
        try launch(profile: profile, folder: locator.home, arguments: ["auth", "login"])
    }

    /// Design flow 12. `open` itself reports when VS Code was already running.
    public func openVSCode(profile: Profile, folder: URL? = nil) throws {
        var args = ["-a", "Visual Studio Code", "--env", "CLAUDE_PROFILE_ID=\(profile.id)"]
        if let dir = profile.configDir { args += ["--env", "CLAUDE_CONFIG_DIR=\(dir)"] }
        if let folder { args.append(folder.path) }
        let r = runner.run(executable: "/usr/bin/open", args: args, env: nil, timeout: 15)
        if (r.stdout + r.stderr).contains("already running") { throw LauncherError.vsCodeRunning }
        guard r.exitCode == 0 else { throw ProfileError.failed("Could not open VS Code: \(r.stderr)") }
    }

    func launch(profile: Profile, folder: URL, arguments: [String]) throws {
        guard isShellInstalled() else { throw LauncherError.shellNotInstalled }
        let script = Self.commandFile(folder: folder.path, commandName: profile.commandName, arguments: arguments)
        cleanOldFiles()
        let file = runDir.appendingPathComponent("\(UUID().uuidString).command")
        try FileManager.default.createDirectory(at: runDir, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        try AtomicFile.write(Data(script.utf8), to: file, mode: 0o700)
        try opener.open(commandFile: file)
    }

    /// The folder comes from transcript data. It appears once, in single quotes, and is only
    /// ever used as "$dir": in double quotes, a $(...) in a folder name would run.
    public static func commandFile(folder: String, commandName: String, arguments: [String]) -> String {
        let args = ([commandName] + arguments).map(quote).joined(separator: " ")
        return """
        #!/bin/zsh
        dir=\(quote(folder))
        cd -- "$dir" || { print -ru2 -- "Folder not found: $dir"; exit 1 }
        exec "$HOME/.claude-profiles/bin/claude-as" \(args)

        """
    }

    public static func quote(_ s: String) -> String {
        "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }

    private func cleanOldFiles() {
        let fm = FileManager.default
        for name in (try? fm.contentsOfDirectory(atPath: runDir.path)) ?? [] where name.hasSuffix(".command") {
            let url = runDir.appendingPathComponent(name)
            if let m = (try? fm.attributesOfItem(atPath: url.path))?[.modificationDate] as? Date,
               Date().timeIntervalSince(m) > maxFileAge {
                try? fm.removeItem(at: url)
            }
        }
    }
}
