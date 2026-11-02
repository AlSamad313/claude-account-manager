import Foundation

public struct SessionRow: Equatable, Sendable {
    public var profileId: String
    public var sessionId: String
    public var cwd: String?
    public var title: String
    public var lastActivity: Date
    public var live: Bool
    public var status: String?
    public var entrypoint: String?
    public var pid: Int32?
    public var procStart: Date?
    public var mayBeStale: Bool
}

public struct SessionIndexResult: Equatable, Sendable {
    public var rows: [SessionRow]
    public var parseFailures: Int
}

/// Merged session list. For display only: safety checks use LiveSessionReader, never this,
/// because this hides desktop app sessions.
public final class SessionIndex {
    /// Hidden from the list. The Cowork value is added once S0 check 16 finds it. An unknown value is shown.
    public static let hiddenEntrypoints: Set<String> = ["claude-desktop"]

    public let locator: ConfigLocator
    public let live: LiveSessionReader
    private var cache: [String: (size: Int, modified: Date, summary: TranscriptSummary?)] = [:]

    public init(locator: ConfigLocator, processes: ProcessInfoProviding = ProcessInfoProvider()) {
        self.locator = locator
        self.live = LiveSessionReader(locator: locator, processes: processes)
    }

    public func sessions(for profiles: [Profile]) -> SessionIndexResult {
        var rows: [SessionRow] = []
        var failures = 0
        for p in profiles {
            let (summaries, failed) = transcripts(of: p)
            failures += failed
            var byId = Dictionary(summaries.map { ($0.sessionId, $0) }, uniquingKeysWith: { a, b in a.lastActivity > b.lastActivity ? a : b })
            // A live record with no transcript yet still gives a row. If a session has several
            // live records, the newest process wins.
            let liveRecords = live.liveRecords(for: p).sorted { ($0.procStart ?? .distantPast) < ($1.procStart ?? .distantPast) }
            var recordById: [String: LiveRecord] = [:]
            for r in liveRecords { recordById[r.sessionId] = r }
            for (id, rec) in recordById {
                let t = byId.removeValue(forKey: id)
                rows.append(row(p, id: id, summary: t, record: rec))
            }
            for (id, t) in byId { rows.append(row(p, id: id, summary: t, record: nil)) }
        }
        let shown = rows.filter { row in
            guard let e = row.entrypoint else { return true }
            return !Self.hiddenEntrypoints.contains(e)
        }
        return SessionIndexResult(rows: shown.sorted(by: Self.order), parseFailures: failures)
    }

    public func ownerOf(sessionId: String, in profiles: [Profile]) -> Profile? {
        let fm = FileManager.default
        for p in profiles {
            let root = locator.configDir(for: p).appendingPathComponent("projects")
            for project in (try? fm.contentsOfDirectory(atPath: root.path)) ?? [] {
                if fm.fileExists(atPath: root.appendingPathComponent(project).appendingPathComponent(sessionId + ".jsonl").path) { return p }
            }
            if live.liveRecords(for: p).contains(where: { $0.sessionId == sessionId }) { return p }
        }
        return nil
    }

    private static func order(_ a: SessionRow, _ b: SessionRow) -> Bool {
        if a.live != b.live { return a.live }
        if a.lastActivity != b.lastActivity { return a.lastActivity > b.lastActivity }
        return a.sessionId < b.sessionId
    }

    private func row(_ p: Profile, id: String, summary t: TranscriptSummary?, record r: LiveRecord?) -> SessionRow {
        let name = r?.name.flatMap { $0.isEmpty ? nil : $0 }
        return SessionRow(profileId: p.id, sessionId: id, cwd: r?.cwd ?? t?.cwd, title: name ?? t?.title ?? "Untitled",
                          lastActivity: t?.lastActivity ?? Date(), live: r != nil, status: r?.status,
                          entrypoint: r?.entrypoint ?? t?.entrypoint, pid: r?.pid, procStart: r?.procStart,
                          mayBeStale: r != nil && r?.procStart == nil)
    }

    private func transcripts(of p: Profile) -> ([TranscriptSummary], Int) {
        let fm = FileManager.default
        let root = locator.configDir(for: p).appendingPathComponent("projects")
        var out: [TranscriptSummary] = []
        var failed = 0
        var toRead: [(url: URL, size: Int, modified: Date)] = []
        for project in (try? fm.contentsOfDirectory(atPath: root.path)) ?? [] {
            let dir = root.appendingPathComponent(project)
            for file in (try? fm.contentsOfDirectory(atPath: dir.path)) ?? [] where file.hasSuffix(".jsonl") {
                let url = dir.appendingPathComponent(file)
                var st = stat()
                guard stat(url.path, &st) == 0 else {
                    failed += 1
                    continue
                }
                let size = Int(st.st_size)
                let modified = Date(timeIntervalSince1970: TimeInterval(st.st_mtimespec.tv_sec))
                if let c = cache[url.path], c.size == size, c.modified == modified {
                    if let summary = c.summary { out.append(summary) } else { failed += 1 }
                } else {
                    toRead.append((url, size, modified))
                }
            }
        }
        var read = [TranscriptSummary?](repeating: nil, count: toRead.count)
        read.withUnsafeMutableBufferPointer { buffer in
            let slots = UncheckedBox(buffer)
            DispatchQueue.concurrentPerform(iterations: toRead.count) { i in
                slots.value[i] = TranscriptReader.summary(of: toRead[i].url, size: toRead[i].size, modified: toRead[i].modified)
            }
        }
        for (i, item) in toRead.enumerated() {
            cache[item.url.path] = (item.size, item.modified, read[i])
            if let summary = read[i] { out.append(summary) } else { failed += 1 }
        }
        return (out, failed)
    }
}

/// Each concurrent iteration writes only its own slot.
private struct UncheckedBox<T>: @unchecked Sendable {
    let value: T
    init(_ value: T) { self.value = value }
}
