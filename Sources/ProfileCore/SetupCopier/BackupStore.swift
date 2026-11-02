import CryptoKit
import Foundation

func sha256(of url: URL) -> String? {
    guard let data = try? Data(contentsOf: url) else { return nil }
    return SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
}

/// ~/.claude-profiles/backups/<id>/<time>/. Folders 0700, files 0600. Files that Claude Code's own
/// commands change are not listed, and Restore cannot undo them.
public struct BackupStore {
    public struct Entry: Codable, Equatable {
        public var path: String
        public var existedBefore: Bool
        public var mode: Int?
        public var before: String?
        public var after: String?
        public var backupFile: String?
    }

    public struct Manifest: Codable, Equatable {
        public var createdAt: Date
        public var profileId: String
        public var entries: [Entry]
        public var removalCommands: [String]
    }

    public static let keep = 10
    public let locator: ConfigLocator

    public init(locator: ConfigLocator) { self.locator = locator }

    private func folder(profileId: String) -> URL { locator.profilesRoot.appendingPathComponent("backups/\(profileId)") }

    /// Copies each file that exists now, and records its state.
    public func begin(profileId: String, paths: [String]) throws -> (id: String, manifest: Manifest) {
        let fm = FileManager.default
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let id = f.string(from: Date()).replacingOccurrences(of: ":", with: "-")
        let dir = folder(profileId: profileId).appendingPathComponent(id)
        try fm.createDirectory(at: dir, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        try fm.setAttributes([.posixPermissions: 0o700], ofItemAtPath: folder(profileId: profileId).path)
        var entries: [Entry] = []
        for (i, path) in paths.enumerated() {
            let url = URL(fileURLWithPath: path)
            guard fm.fileExists(atPath: path) else {
                entries.append(Entry(path: path, existedBefore: false, mode: nil, before: nil, after: nil, backupFile: nil))
                continue
            }
            let name = "file-\(i)"
            try AtomicFile.write(try Data(contentsOf: url), to: dir.appendingPathComponent(name), mode: 0o600)
            let mode = (try? fm.attributesOfItem(atPath: path))?[.posixPermissions] as? Int
            entries.append(Entry(path: path, existedBefore: true, mode: mode, before: sha256(of: url), after: nil, backupFile: name))
        }
        return (id, Manifest(createdAt: Date(), profileId: profileId, entries: entries, removalCommands: []))
    }

    public func save(_ m: Manifest, id: String) throws {
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .sortedKeys]
        e.dateEncodingStrategy = .iso8601
        let dir = folder(profileId: m.profileId).appendingPathComponent(id)
        try AtomicFile.write(try e.encode(m), to: dir.appendingPathComponent("manifest.json"), mode: 0o600)
        trim(profileId: m.profileId)
    }

    public func load(profileId: String, id: String) throws -> Manifest {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .iso8601
        let url = folder(profileId: profileId).appendingPathComponent(id).appendingPathComponent("manifest.json")
        return try d.decode(Manifest.self, from: try Data(contentsOf: url))
    }

    public func backupFile(_ e: Entry, profileId: String, id: String) -> URL? {
        e.backupFile.map { folder(profileId: profileId).appendingPathComponent(id).appendingPathComponent($0) }
    }

    /// Newest first.
    public func list(profileId: String) -> [BackupInfo] {
        let names = (try? FileManager.default.contentsOfDirectory(atPath: folder(profileId: profileId).path)) ?? []
        return names.sorted(by: >).compactMap { id in
            guard let m = try? load(profileId: profileId, id: id) else { return nil }
            return BackupInfo(id: id, profileId: profileId, createdAt: m.createdAt, removalCommands: m.removalCommands)
        }
    }

    private func trim(profileId: String) {
        let names = ((try? FileManager.default.contentsOfDirectory(atPath: folder(profileId: profileId).path)) ?? []).sorted(by: >)
        for old in names.dropFirst(Self.keep) {
            try? FileManager.default.removeItem(at: folder(profileId: profileId).appendingPathComponent(old))
        }
    }
}
