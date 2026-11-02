import Foundation

/// The same rule as the wrapper: the first `claude` on PATH, skipping our own bin folder.
public struct ClaudeBinaryLocator {
    public let locator: ConfigLocator
    private let pathProvider: () -> String
    public var path: String { pathProvider() }

    public init(locator: ConfigLocator, path: String? = nil) {
        self.locator = locator
        let fixed = path ?? ProcessInfo.processInfo.environment["PATH"] ?? ""
        self.pathProvider = { fixed }
    }

    /// For the app. An app opened from the Dock does not get the shell's PATH, so it would not
    /// find Homebrew's claude. Reads it from a login shell the first time it is needed.
    public static func fromLoginShell(locator: ConfigLocator, runner: ProcessRunning = ProcessRunner()) -> ClaudeBinaryLocator {
        let cache = PathCache()
        return ClaudeBinaryLocator(locator: locator) {
            cache.value { LoginShell.path(runner: runner) } ?? ProcessInfo.processInfo.environment["PATH"] ?? ""
        }
    }

    private init(locator: ConfigLocator, provider: @escaping () -> String) {
        self.locator = locator
        self.pathProvider = provider
    }

    public func realBinary() -> URL? {
        let own = Self.resolved(locator.binDir.path)
        let fm = FileManager.default
        for entry in path.split(separator: ":").map(String.init) where !entry.isEmpty {
            if Self.resolved(entry) == own { continue }
            let candidate = (entry as NSString).appendingPathComponent("claude")
            var isDir: ObjCBool = false
            if fm.fileExists(atPath: candidate, isDirectory: &isDir), !isDir.boolValue, fm.isExecutableFile(atPath: candidate) {
                return URL(fileURLWithPath: candidate)
            }
        }
        return nil
    }

    public static func resolved(_ p: String) -> String {
        guard let r = realpath(p, nil) else { return (p as NSString).standardizingPath }
        defer { free(r) }
        return String(cString: r)
    }
}

public enum LoginShell {
    static let marker = "CPM_PATH_MARKER"

    /// A shell's startup files may print text or hang, so this uses a marker and a timeout.
    public static func path(runner: ProcessRunning, shell: String = "/bin/zsh") -> String? {
        let r = runner.run(executable: shell, args: ["-lic", "print -r -- \(marker); print -r -- $PATH"], env: nil, timeout: 5)
        let lines = r.stdout.components(separatedBy: "\n")
        guard let i = lines.lastIndex(of: marker), i + 1 < lines.count, !lines[i + 1].isEmpty else { return nil }
        return lines[i + 1]
    }
}

private final class PathCache: @unchecked Sendable {
    private let lock = NSLock()
    private var stored: String?

    func value(_ read: () -> String?) -> String? {
        lock.lock()
        defer { lock.unlock() }
        if stored == nil { stored = read() }
        return stored
    }
}
