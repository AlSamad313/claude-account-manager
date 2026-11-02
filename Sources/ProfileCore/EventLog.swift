import Foundation

/// writes.log, and rotation of both logs. A logging failure never blocks the action.
public struct EventLog {
    public let locator: ConfigLocator
    public var maxBytes: Int = 1_000_000

    public init(locator: ConfigLocator) { self.locator = locator }

    public func write(kind: String, profileId: String?, detail: String, backupPath: String? = nil) {
        let fields = [ISO8601DateFormatter().string(from: Date()), kind, profileId ?? "-", detail, backupPath ?? "-"]
        let line = fields.map(Self.clean).joined(separator: "\t") + "\n"
        append(line, to: locator.logsDir.appendingPathComponent("writes.log"))
    }

    static func clean(_ s: String) -> String {
        s.replacingOccurrences(of: "\t", with: " ").replacingOccurrences(of: "\n", with: " ")
    }

    private func append(_ line: String, to url: URL) {
        let fm = FileManager.default
        try? fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        if !fm.fileExists(atPath: url.path) { fm.createFile(atPath: url.path, contents: nil, attributes: [.posixPermissions: 0o600]) }
        guard let h = try? FileHandle(forWritingTo: url) else { return }
        defer { try? h.close() }
        _ = try? h.seekToEnd()
        try? h.write(contentsOf: Data(line.utf8))
    }

    /// Checked when the app or cpm starts. Keeps 3 old files.
    public func rotate() {
        for name in ["writes.log", "launch.log"] {
            let url = locator.logsDir.appendingPathComponent(name)
            guard let size = (try? FileManager.default.attributesOfItem(atPath: url.path))?[.size] as? Int, size >= maxBytes else { continue }
            let fm = FileManager.default
            try? fm.removeItem(atPath: url.path + ".3")
            for i in [2, 1] { try? fm.moveItem(atPath: url.path + ".\(i)", toPath: url.path + ".\(i + 1)") }
            try? fm.moveItem(atPath: url.path, toPath: url.path + ".1")
        }
    }
}
