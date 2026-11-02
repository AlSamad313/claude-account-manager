import Foundation

public struct InstallResult: Equatable, Sendable {
    public var zshenvLine: String
    public var zshrcLine: String
}

/// Owns flow 10. Safe to repeat, never resets the default, and never edits the user's shell files.
public struct ShellInstaller {
    public static let zshenvLine = #"path=("$HOME/.claude-profiles/bin" $path)"#
    public static let zshrcLine = #"[[ -r "$HOME/.claude-profiles/bin/claude-profiles.zsh" ]] && source "$HOME/.claude-profiles/bin/claude-profiles.zsh""#
    static let files = ["cpm", "claude", "claude-as", "claude-core.zsh", "claude-profiles.zsh", "profile-label"]

    public let locator: ConfigLocator
    public let store: ProfileStore
    public var version = "1"

    public init(store: ProfileStore) {
        self.store = store
        self.locator = store.locator
    }

    public func install(from source: URL) throws -> InstallResult {
        _ = try store.load()
        let fm = FileManager.default
        try fm.createDirectory(at: locator.binDir, withIntermediateDirectories: true)
        try fm.createDirectory(at: locator.logsDir, withIntermediateDirectories: true)
        for name in Self.files {
            let from = source.appendingPathComponent(name)
            guard fm.fileExists(atPath: from.path) else { throw ProfileError.failed("Missing \(from.path). Run make-cli.sh first.") }
            let to = locator.binDir.appendingPathComponent(name)
            if ClaudeBinaryLocator.resolved(from.path) == ClaudeBinaryLocator.resolved(to.path) { continue }
            let data = try Data(contentsOf: from)
            try AtomicFile.write(data, to: to, mode: 0o755)
        }
        try AtomicFile.write(Data((version + "\n").utf8), to: locator.binDir.appendingPathComponent("VERSION"), mode: 0o644)
        try store.repairFiles()
        return InstallResult(zshenvLine: Self.zshenvLine, zshrcLine: Self.zshrcLine)
    }

    public func isInstalled() -> Bool {
        let fm = FileManager.default
        return Self.files.allSatisfy { fm.fileExists(atPath: locator.binDir.appendingPathComponent($0).path) }
    }
}
