import Foundation

public struct StopRequest: Equatable, Sendable {
    public var title: String
    public var folder: String?
    public var status: String?
    public var pid: Int32
    public var isBusy: Bool { status == "busy" }
    public var warning: String? { isBusy ? "Claude Code is working; stopping now can leave a file edit unfinished." : nil }
}

public enum StopResult: Equatable, Sendable {
    case alreadyStopped
    case cancelled
    case stopped(signal: String)
    case didNotStop(pid: Int32)
}

/// Implements design flow 9. Never sends SIGKILL, and never closes the Terminal window.
public struct SessionStopper {
    public let locator: ConfigLocator
    public let processes: ProcessInfoProviding
    public var graceSeconds: TimeInterval = 5
    public var pollInterval: TimeInterval = 0.25

    public init(locator: ConfigLocator, processes: ProcessInfoProviding = ProcessInfoProvider()) {
        self.locator = locator
        self.processes = processes
    }

    /// True only if this pid is still the process the record describes, so a reused PID is never signalled.
    private func isSameProcess(_ pid: Int32, _ start: Date?) -> Bool {
        guard processes.isAlive(pid: pid) else { return false }
        guard let start, let actual = processes.startTime(pid: pid) else { return true }
        return abs(actual.timeIntervalSince(start)) <= 1
    }

    public func stop(row: SessionRow, confirm: (StopRequest) -> Bool) -> StopResult {
        let result = perform(row: row, confirm: confirm)
        EventLog(locator: locator).write(kind: "stop", profileId: row.profileId, detail: "\(row.sessionId) \(result)")
        return result
    }

    private func perform(row: SessionRow, confirm: (StopRequest) -> Bool) -> StopResult {
        guard row.live, let pid = row.pid, isSameProcess(pid, row.procStart) else { return .alreadyStopped }
        let request = StopRequest(title: row.title, folder: row.cwd, status: row.status, pid: pid)
        guard confirm(request) else { return .cancelled }
        // The user may have taken a while to answer.
        guard isSameProcess(pid, row.procStart) else { return .alreadyStopped }

        _ = processes.signal(pid: pid, SIGINT)
        if waitForExit(pid, row.procStart) { return .stopped(signal: "SIGINT") }
        guard isSameProcess(pid, row.procStart) else { return .stopped(signal: "SIGINT") }
        _ = processes.signal(pid: pid, SIGTERM)
        if waitForExit(pid, row.procStart) { return .stopped(signal: "SIGTERM") }
        return .didNotStop(pid: pid)
    }

    private func waitForExit(_ pid: Int32, _ start: Date?) -> Bool {
        let deadline = Date().addingTimeInterval(graceSeconds)
        while Date() < deadline {
            if !isSameProcess(pid, start) { return true }
            Thread.sleep(forTimeInterval: pollInterval)
        }
        return !isSameProcess(pid, start)
    }
}
