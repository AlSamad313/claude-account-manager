import Foundation
import Testing
@testable import ProfileCore

private let startText = "Sat Oct 31 10:58:34 2026"
private var startDate: Date { LiveSessionReader.parseProcStart(startText)! }

private func writeTranscript(_ s: Sandbox, _ p: Profile, id: String, cwd: String = "/w", title: String? = "T", entrypoint: String = "cli", age: TimeInterval = 100) throws {
    let dir = s.locator.configDir(for: p).appendingPathComponent("projects/proj")
    try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    var text = #"{"type":"user","cwd":"\#(cwd)","entrypoint":"\#(entrypoint)","sessionId":"\#(id)"}"# + "\n"
    if let title { text += #"{"type":"ai-title","aiTitle":"\#(title)","sessionId":"\#(id)"}"# + "\n" }
    let url = dir.appendingPathComponent(id + ".jsonl")
    try Data(text.utf8).write(to: url)
    try FileManager.default.setAttributes([.modificationDate: Date().addingTimeInterval(-age)], ofItemAtPath: url.path)
}

private func writeRecord(_ s: Sandbox, _ p: Profile, pid: Int32, id: String, name: String = "", status: String = "idle", entrypoint: String = "cli", start: String = startText) throws {
    let dir = s.locator.configDir(for: p).appendingPathComponent("sessions")
    try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    let json = #"{"pid":\#(pid),"sessionId":"\#(id)","procStart":"\#(start)","cwd":"/live","status":"\#(status)","entrypoint":"\#(entrypoint)","name":"\#(name)"}"#
    try Data(json.utf8).write(to: dir.appendingPathComponent("\(pid).json"))
}

@Suite struct SessionIndexTests {
    @Test func rowsPerProfileSortedLiveFirstThenNewest() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let b = try s.store.defaultProfile()
        let a = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try writeTranscript(s, b, id: "old", age: 500)
        try writeTranscript(s, a, id: "new", age: 10)
        try writeTranscript(s, a, id: "running", age: 900)
        try writeRecord(s, a, pid: 7, id: "running")
        let index = SessionIndex(locator: s.locator, processes: FakeProcesses(alive: [7], starts: [7: startDate]))
        let rows = index.sessions(for: [b, a]).rows
        #expect(rows.map(\.sessionId) == ["running", "new", "old"])
        #expect(rows[0].live && rows[0].profileId == a.id && rows[0].pid == 7)
        #expect(rows[2].profileId == b.id)
    }

    @Test func desktopIsHiddenAndUnknownIsShown() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let b = try s.store.defaultProfile()
        try writeTranscript(s, b, id: "d", entrypoint: "claude-desktop")
        try writeTranscript(s, b, id: "v", entrypoint: "claude-vscode")
        try writeTranscript(s, b, id: "u", entrypoint: "something-new")
        try writeRecord(s, b, pid: 8, id: "live-desktop", entrypoint: "claude-desktop")
        let index = SessionIndex(locator: s.locator, processes: FakeProcesses(alive: [8], starts: [8: startDate]))
        #expect(Set(index.sessions(for: [b]).rows.map(\.sessionId)) == ["v", "u"])
        // The safety check still sees the hidden one.
        #expect(index.live.hasLiveSession(b))
    }

    @Test func aLiveRecordWithNoTranscriptGivesARow() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let b = try s.store.defaultProfile()
        try writeRecord(s, b, pid: 9, id: "fresh", name: "My session")
        let rows = SessionIndex(locator: s.locator, processes: FakeProcesses(alive: [9], starts: [9: startDate])).sessions(for: [b]).rows
        #expect(rows.count == 1 && rows[0].title == "My session" && rows[0].live && rows[0].cwd == "/live")
    }

    @Test func titleOrderAndUntitled() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let b = try s.store.defaultProfile()
        try writeTranscript(s, b, id: "x", title: "From transcript")
        try writeTranscript(s, b, id: "y", title: nil)
        try writeRecord(s, b, pid: 3, id: "x", name: "From record")
        let rows = SessionIndex(locator: s.locator, processes: FakeProcesses(alive: [3], starts: [3: startDate])).sessions(for: [b]).rows
        #expect(rows.first { $0.sessionId == "x" }?.title == "From record")
        #expect(rows.first { $0.sessionId == "y" }?.title == "Untitled")
    }

    @Test func aReusedPIDIsNotLiveAndABrokenFileIsCounted() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let b = try s.store.defaultProfile()
        try writeTranscript(s, b, id: "x")
        try writeRecord(s, b, pid: 4, id: "x")
        try s.write("not json\n", to: ".claude/projects/proj/broken.jsonl")
        let r = SessionIndex(locator: s.locator, processes: FakeProcesses(alive: [4], starts: [4: startDate.addingTimeInterval(99)])).sessions(for: [b])
        #expect(r.rows.count == 1 && !r.rows[0].live)
        #expect(r.parseFailures == 1)
    }

    @Test func anUnparsableStartIsMarkedMayBeStale() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let b = try s.store.defaultProfile()
        try writeRecord(s, b, pid: 5, id: "x", start: "not a date")
        let rows = SessionIndex(locator: s.locator, processes: FakeProcesses(alive: [5], starts: [5: startDate])).sessions(for: [b]).rows
        #expect(rows[0].live && rows[0].mayBeStale)
    }

    @Test func cacheSeesAChangedFile() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let b = try s.store.defaultProfile()
        try writeTranscript(s, b, id: "x", title: "First")
        let index = SessionIndex(locator: s.locator, processes: FakeProcesses())
        #expect(index.sessions(for: [b]).rows[0].title == "First")
        try writeTranscript(s, b, id: "x", title: "Second title", age: 5)
        #expect(index.sessions(for: [b]).rows[0].title == "Second title")
    }

    @Test func ownerOfFindsTheRightProfile() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let b = try s.store.defaultProfile()
        let a = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try writeTranscript(s, a, id: "mine")
        let index = SessionIndex(locator: s.locator, processes: FakeProcesses())
        #expect(index.ownerOf(sessionId: "mine", in: [b, a])?.id == a.id)
        #expect(index.ownerOf(sessionId: "none", in: [b, a]) == nil)
    }
}

final class FakeOpener: TerminalOpener {
    var opened: [URL] = []
    var texts: [String] = []
    func open(commandFile: URL) throws {
        opened.append(commandFile)
        texts.append(try String(contentsOf: commandFile, encoding: .utf8))
    }
}

@Suite struct LauncherTests {
    private func launcher(_ s: Sandbox, _ o: FakeOpener, runner: FakeRunner = FakeRunner(), installed: Bool = true) -> Launcher {
        var l = Launcher(store: s.store, opener: o, runner: runner)
        l.isShellInstalled = { installed }
        return l
    }

    private let goodId = "11111111-2222-3333-4444-555555555555"

    private func row(id: String? = nil, cwd: String?, live: Bool = false) -> SessionRow {
        SessionRow(profileId: "x", sessionId: id ?? goodId, cwd: cwd, title: "T", lastActivity: Date(), live: live, status: nil,
                   entrypoint: "cli", pid: live ? 4 : nil, procStart: nil, mayBeStale: false)
    }

    @Test func fileHasExactlyTheseLines() {
        let text = Launcher.commandFile(folder: "/a b", commandName: "main", arguments: ["--resume", "id"])
        #expect(text == """
        #!/bin/zsh
        dir='/a b'
        cd -- "$dir" || { print -ru2 -- "Folder not found: $dir"; exit 1 }
        exec "$HOME/.claude-profiles/bin/claude-as" 'main' '--resume' 'id'

        """)
    }

    @Test func fileHasNoConfigDirLine() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let o = FakeOpener()
        let a = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try launcher(s, o).newSession(profile: a)
        #expect(!o.texts[0].contains("CLAUDE_CONFIG_DIR"))
        #expect(o.texts[0].contains("claude-as\" 'a'"))
    }

    /// Runs the generated file for real against a folder that does not exist.
    @Test func hostileFoldersStayInert() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let canary = s.home.appendingPathComponent("PWNED")
        for hostile in ["/x/$(touch \(canary.path))", "/x/`touch \(canary.path)`", "/x/a'b; touch \(canary.path); '", "/x/a\ntouch \(canary.path)\n"] {
            let script = Launcher.commandFile(folder: hostile, commandName: "main", arguments: [])
            let file = s.home.appendingPathComponent("t.command")
            try Data(script.utf8).write(to: file)
            let r = ProcessRunner().run(executable: "/bin/zsh", args: ["-f", file.path], env: LaunchEnv(set: ["HOME": s.home.path]), timeout: 5)
            #expect(r.exitCode == 1)
            #expect(r.stderr.contains("Folder not found"))
            #expect(!FileManager.default.fileExists(atPath: canary.path), "ran a command from: \(hostile)")
        }
    }

    @Test func resumeUsesTheOwnerAndTheSessionFolder() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let o = FakeOpener()
        let a = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try launcher(s, o).resume(session: row(cwd: s.home.path), owner: a)
        #expect(o.texts[0].contains("dir='\(s.home.path)'"))
        #expect(o.texts[0].contains("'a' '--resume' '\(goodId)'"))
    }

    @Test func resumeRefusesAnIDThatIsNotAUUID() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let o = FakeOpener()
        var r = row(cwd: s.home.path)
        r.sessionId = "--dangerously-skip-permissions"
        #expect(throws: ProfileError.self) { try launcher(s, o).resume(session: r, owner: try s.store.defaultProfile()) }
        #expect(o.opened.isEmpty)
    }

    @Test func resumeRefusesALiveRow() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let o = FakeOpener()
        #expect(throws: LauncherError.sessionIsLive(pid: 4)) { try launcher(s, o).resume(session: row(cwd: "/", live: true), owner: try s.store.defaultProfile()) }
        #expect(o.opened.isEmpty)
    }

    @Test func missingFolderOffersOverride() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let o = FakeOpener()
        let b = try s.store.defaultProfile()
        let l = launcher(s, o)
        #expect(throws: LauncherError.folderMissing("/no/such/place")) { try l.resume(session: row(cwd: "/no/such/place"), owner: b) }
        try l.resume(session: row(cwd: "/no/such/place"), owner: b, folderOverride: s.home)
        #expect(o.texts[0].contains("dir='\(s.home.path)'"))
    }

    @Test func refusesWhenTheShellIsNotInstalled() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let o = FakeOpener()
        #expect(throws: LauncherError.shellNotInstalled) { try launcher(s, o, installed: false).newSession(profile: try s.store.defaultProfile()) }
        #expect(o.opened.isEmpty)
    }

    @Test func modeAndCleanup() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let o = FakeOpener()
        let b = try s.store.defaultProfile()
        let l = launcher(s, o)
        try l.newSession(profile: b)
        let first = o.opened[0]
        #expect(try FileManager.default.attributesOfItem(atPath: first.path)[.posixPermissions] as? Int == 0o700)
        // A recent file stays, because Terminal may not have read it yet.
        try l.newSession(profile: b)
        #expect(FileManager.default.fileExists(atPath: first.path))
        try FileManager.default.setAttributes([.modificationDate: Date().addingTimeInterval(-120)], ofItemAtPath: first.path)
        try l.newSession(profile: b)
        #expect(!FileManager.default.fileExists(atPath: first.path))
    }

    @Test func loginCommand() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let o = FakeOpener()
        let a = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try launcher(s, o).login(profile: a)
        #expect(o.texts[0].contains("'a' 'auth' 'login'"))
    }

    @Test func openVSCodePassesTheProfilesValues() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let r = FakeRunner()
        let l = launcher(s, FakeOpener(), runner: r)
        let a = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try l.openVSCode(profile: a, folder: s.home)
        #expect(r.calls[0].0 == "/usr/bin/open")
        #expect(r.calls[0].1 == ["-a", "Visual Studio Code", "--env", "CLAUDE_PROFILE_ID=\(a.id)", "--env", "CLAUDE_CONFIG_DIR=\(a.configDir!)", s.home.path])
        // The built-in profile never sets CLAUDE_CONFIG_DIR.
        try l.openVSCode(profile: try s.store.defaultProfile())
        #expect(!r.calls[1].1.contains { $0.hasPrefix("CLAUDE_CONFIG_DIR") })
    }

    @Test func openVSCodeSaysWhenItIsAlreadyRunning() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let r = FakeRunner()
        r.handler = { _, _ in ProcessResult(stderr: "Application /Applications/Visual Studio Code.app was already running and so the additional environment variables could not be set.") }
        #expect(throws: LauncherError.vsCodeRunning) { try launcher(s, FakeOpener(), runner: r).openVSCode(profile: try s.store.defaultProfile()) }
    }
}

/// Dies after a set number of signals, recording what it received.
private final class SignalLog: ProcessInfoProviding, @unchecked Sendable {
    var alive = true
    var signals: [Int32] = []
    var diesOn: Set<Int32> = []
    var start: Date
    var replacedAfterAsking = false
    init(start: Date) { self.start = start }
    func isAlive(pid: Int32) -> Bool { alive }
    func startTime(pid: Int32) -> Date? { start }
    func signal(pid: Int32, _ sig: Int32) -> Bool {
        signals.append(sig)
        if diesOn.contains(sig) { alive = false }
        return true
    }
}

@Suite struct SessionStopperTests {
    private func row(status: String? = "idle", live: Bool = true) -> SessionRow {
        SessionRow(profileId: "p", sessionId: "s", cwd: "/w", title: "Title", lastActivity: Date(), live: live, status: status,
                   entrypoint: "cli", pid: 42, procStart: startDate, mayBeStale: false)
    }

    private func stopper(_ s: Sandbox, _ log: SignalLog) -> SessionStopper {
        var st = SessionStopper(locator: s.locator, processes: log)
        st.graceSeconds = 0.05
        st.pollInterval = 0.01
        return st
    }

    @Test func sigintIsEnough() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let log = SignalLog(start: startDate)
        log.diesOn = [SIGINT]
        #expect(stopper(s, log).stop(row: row(), confirm: { _ in true }) == .stopped(signal: "SIGINT"))
        #expect(log.signals == [SIGINT])
    }

    @Test func sigtermFollowsAndNeverSigkill() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let log = SignalLog(start: startDate)
        log.diesOn = [SIGTERM]
        #expect(stopper(s, log).stop(row: row(), confirm: { _ in true }) == .stopped(signal: "SIGTERM"))
        #expect(log.signals == [SIGINT, SIGTERM])
        let stubborn = SignalLog(start: startDate)
        #expect(stopper(s, stubborn).stop(row: row(), confirm: { _ in true }) == .didNotStop(pid: 42))
        #expect(stubborn.signals == [SIGINT, SIGTERM])
    }

    @Test func aReusedPIDIsNeverSignalled() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let log = SignalLog(start: startDate.addingTimeInterval(500))
        #expect(stopper(s, log).stop(row: row(), confirm: { _ in true }) == .alreadyStopped)
        #expect(log.signals.isEmpty)
    }

    @Test func aPIDReusedWhileAskingIsNotSignalled() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let log = SignalLog(start: startDate)
        let result = stopper(s, log).stop(row: row(), confirm: { _ in
            log.start = startDate.addingTimeInterval(500)
            return true
        })
        #expect(result == .alreadyStopped)
        #expect(log.signals.isEmpty)
    }

    @Test func cancelSendsNothing() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let log = SignalLog(start: startDate)
        #expect(stopper(s, log).stop(row: row(), confirm: { _ in false }) == .cancelled)
        #expect(log.signals.isEmpty)
    }

    @Test func busyWarnsAndTheResultIsLogged() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let log = SignalLog(start: startDate)
        log.diesOn = [SIGINT]
        var seen: StopRequest?
        _ = stopper(s, log).stop(row: row(status: "busy"), confirm: { seen = $0; return true })
        #expect(seen?.warning?.contains("unfinished") == true)
        #expect(StopRequest(title: "", folder: nil, status: "idle", pid: 1).warning == nil)
        let text = try String(contentsOf: s.locator.logsDir.appendingPathComponent("writes.log"), encoding: .utf8)
        #expect(text.contains("stop") && text.contains("SIGINT"))
    }

    /// A real child process, ended by a real signal.
    @Test func stopsARealProcess() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sleep")
        p.arguments = ["30"]
        try p.run()
        defer { if p.isRunning { p.terminate() } }
        let info = ProcessInfoProvider()
        var st = SessionStopper(locator: s.locator, processes: ZombieAware(p))
        st.pollInterval = 0.05
        var r = row()
        r.pid = p.processIdentifier
        r.procStart = info.startTime(pid: p.processIdentifier)
        #expect(st.stop(row: r, confirm: { _ in true }) == .stopped(signal: "SIGINT"))
    }
}

/// An exited child stays a zombie until reaped, which kill(pid, 0) still counts as alive.
private struct ZombieAware: ProcessInfoProviding {
    let child: Process
    init(_ c: Process) { child = c }
    func isAlive(pid: Int32) -> Bool { child.isRunning }
    func startTime(pid: Int32) -> Date? { ProcessInfoProvider().startTime(pid: pid) }
    func signal(pid: Int32, _ sig: Int32) -> Bool { ProcessInfoProvider().signal(pid: pid, sig) }
}
