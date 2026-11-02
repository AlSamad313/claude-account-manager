import Foundation

/// The same rule as the wrapper: the first `claude` on PATH, skipping our own bin folder.
public struct ClaudeBinaryLocator {
    public let locator: ConfigLocator
    public let path: String

    public init(locator: ConfigLocator, path: String? = nil) {
        self.locator = locator
        self.path = path ?? ProcessInfo.processInfo.environment["PATH"] ?? ""
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

    static func resolved(_ p: String) -> String {
        guard let r = realpath(p, nil) else { return (p as NSString).standardizingPath }
        defer { free(r) }
        return String(cString: r)
    }
}
