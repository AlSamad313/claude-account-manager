import Foundation

public protocol ProcessInfoProviding {
    func isAlive(pid: Int32) -> Bool
    func startTime(pid: Int32) -> Date?
    func signal(pid: Int32, _ sig: Int32) -> Bool
}

public struct ProcessInfoProvider: ProcessInfoProviding {
    public init() {}

    public func isAlive(pid: Int32) -> Bool {
        if kill(pid, 0) == 0 { return true }
        return errno == EPERM
    }

    public func startTime(pid: Int32) -> Date? {
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_PID, pid]
        var info = kinfo_proc()
        var size = MemoryLayout<kinfo_proc>.stride
        guard sysctl(&mib, 4, &info, &size, nil, 0) == 0, size > 0 else { return nil }
        let t = info.kp_proc.p_un.__p_starttime
        return Date(timeIntervalSince1970: TimeInterval(t.tv_sec))
    }

    public func signal(pid: Int32, _ sig: Int32) -> Bool { kill(pid, sig) == 0 }
}

public struct LiveRecord: Equatable, Sendable {
    public var pid: Int32
    public var sessionId: String
    public var procStart: Date?
    public var cwd: String?
    public var status: String?
    public var entrypoint: String?
    public var name: String?
}

/// Reader for sessions/*.json. Never opens the .key files beside them. Results are not filtered,
/// because the display filter must never apply to safety checks.
public struct LiveSessionReader {
    public let locator: ConfigLocator
    public let processes: ProcessInfoProviding
    /// A record that cannot be read could be a session starting up, so it counts as live for this long.
    public var unreadableGrace: TimeInterval = 60

    public init(locator: ConfigLocator, processes: ProcessInfoProviding = ProcessInfoProvider()) {
        self.locator = locator
        self.processes = processes
    }

    /// procStart is a date string in UTC. `ps -o lstart` prints local time, so never compare with that.
    public static func parseProcStart(_ s: String) -> Date? {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "EEE MMM d HH:mm:ss yyyy"
        let normal = s.split(whereSeparator: \.isWhitespace).joined(separator: " ")
        return f.date(from: normal)
    }

    public func isLive(_ r: LiveRecord) -> Bool {
        guard processes.isAlive(pid: r.pid) else { return false }
        guard let recorded = r.procStart, let actual = processes.startTime(pid: r.pid) else { return true }
        return abs(recorded.timeIntervalSince(actual)) <= 1
    }

    public func liveRecords(for p: Profile) -> [LiveRecord] {
        read(p).records.filter(isLive)
    }

    /// True if anything may be running. An unreadable, recent record counts, so a safety check
    /// fails closed.
    public func hasLiveSession(_ p: Profile) -> Bool {
        let r = read(p)
        return r.unreadableRecent || r.records.contains(where: isLive)
    }

    private func read(_ p: Profile) -> (records: [LiveRecord], unreadableRecent: Bool) {
        let dir = locator.configDir(for: p).appendingPathComponent("sessions")
        let fm = FileManager.default
        let names = (try? fm.contentsOfDirectory(atPath: dir.path)) ?? []
        var records: [LiveRecord] = []
        var unreadableRecent = false
        for name in names where name.hasSuffix(".json") {
            let url = dir.appendingPathComponent(name)
            if let rec = Self.parse(url) {
                records.append(rec)
            } else if let m = (try? fm.attributesOfItem(atPath: url.path))?[.modificationDate] as? Date,
                      Date().timeIntervalSince(m) < unreadableGrace {
                unreadableRecent = true
            }
        }
        return (records, unreadableRecent)
    }

    static func parse(_ url: URL) -> LiveRecord? {
        guard let data = try? Data(contentsOf: url),
              let o = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let pid = (o["pid"] as? NSNumber)?.int32Value,
              let sessionId = o["sessionId"] as? String,
              let start = o["procStart"] as? String else { return nil }
        return LiveRecord(pid: pid, sessionId: sessionId, procStart: parseProcStart(start), cwd: o["cwd"] as? String,
                          status: o["status"] as? String, entrypoint: o["entrypoint"] as? String, name: o["name"] as? String)
    }
}
