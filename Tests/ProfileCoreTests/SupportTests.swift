import Foundation
import Testing
@testable import ProfileCore

@Suite struct AccountReaderTests {
    @Test func readsOnlyTheAccount() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        try s.write(#"{"oauthAccount":{"accountUuid":"u","emailAddress":"a@e.com","organizationName":"O","x":1},"primaryApiKey":"nope"}"#, to: "c.json")
        #expect(AccountReader.readAccount(configFile: s.home.appendingPathComponent("c.json")) == Account(accountUuid: "u", email: "a@e.com", organizationName: "O"))
    }

    @Test func missingOrBrokenGivesNil() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        try s.write("{", to: "c.json")
        #expect(AccountReader.readAccount(configFile: s.home.appendingPathComponent("c.json")) == nil)
        #expect(AccountReader.readAccount(configFile: s.home.appendingPathComponent("none.json")) == nil)
    }
}

@Suite struct StatusLineWriterTests {
    @Test func refusesTheBuiltinProfile() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.defaultProfile()
        #expect(throws: ProfileError.self) { try StatusLineWriter(locator: s.locator).write(for: p) }
        #expect(!FileManager.default.fileExists(atPath: s.locator.builtinDir.appendingPathComponent("settings.json").path))
    }

    @Test func writesAnAbsolutePath() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        let text = try String(contentsOf: URL(fileURLWithPath: p.configDir!).appendingPathComponent("settings.json"), encoding: .utf8)
        #expect(text.contains(s.locator.binDir.path.replacingOccurrences(of: "/", with: "\\/")))
        #expect(!text.contains("~"))
    }
}

@Suite struct EventLogTests {
    @Test func escapesTabsAndNewlines() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        EventLog(locator: s.locator).write(kind: "stop", profileId: "p", detail: "a\tb\nc")
        let text = try String(contentsOf: s.locator.logsDir.appendingPathComponent("writes.log"), encoding: .utf8)
        #expect(text.split(separator: "\n").count == 1)
        #expect(text.contains("a b c"))
    }

    @Test func rotates() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        var log = EventLog(locator: s.locator)
        log.maxBytes = 10
        log.write(kind: "stop", profileId: "p", detail: "long enough to rotate")
        log.rotate()
        #expect(FileManager.default.fileExists(atPath: s.locator.logsDir.appendingPathComponent("writes.log.1").path))
        #expect(!FileManager.default.fileExists(atPath: s.locator.logsDir.appendingPathComponent("writes.log").path))
    }

    @Test func aFailureDoesNotThrow() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        try s.write("a file, not a folder", to: ".claude-profiles/logs")
        EventLog(locator: s.locator).write(kind: "stop", profileId: nil, detail: "x")
    }
}

@Suite struct CompatCheckTests {
    private func can(_ s: Sandbox, _ json: String) throws -> Bool {
        try s.write(json, to: "settings.json")
        return CompatCheck.canWriteSettings(file: s.home.appendingPathComponent("settings.json"))
    }

    @Test func acceptsKnownAndUnknownShapes() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        #expect(try can(s, #"{"statusLine":{"type":"command"}}"#))
        #expect(try can(s, #"{"someNewKey":[1,2]}"#))
        #expect(try can(s, #"{"permissions":{"allow":["a"]},"model":"m","env":{"A":"b"},"hooks":{}}"#))
        #expect(CompatCheck.canWriteSettings(file: s.home.appendingPathComponent("missing.json")))
    }

    @Test func refusesWrongShapes() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        #expect(try !can(s, "[]"))
        #expect(try !can(s, "{broken"))
        #expect(try !can(s, #"{"permissions":"x"}"#))
        #expect(try !can(s, #"{"permissions":{"allow":[1]}}"#))
        #expect(try !can(s, #"{"model":1}"#))
        #expect(try !can(s, #"{"env":{"A":1}}"#))
    }
}

@Suite struct ClaudeBinaryLocatorTests {
    private func makeExec(_ url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data("#!/bin/sh\n".utf8).write(to: url)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: url.path)
    }

    @Test func skipsOurBinFolderHoweverItIsSpelled() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        try makeExec(s.locator.binDir.appendingPathComponent("claude"))
        let real = s.home.appendingPathComponent("real/claude")
        try makeExec(real)
        for spelling in [s.locator.binDir.path, s.locator.binDir.path + "/", s.locator.binDir.path + "/../bin"] {
            let found = ClaudeBinaryLocator(locator: s.locator, path: "\(spelling):\(real.deletingLastPathComponent().path)").realBinary()
            #expect(found?.path == real.path)
        }
    }

    @Test func nilWhenNothingFound() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        #expect(ClaudeBinaryLocator(locator: s.locator, path: "/nonexistent:\(s.home.path)").realBinary() == nil)
    }
}

@Suite struct TranscriptReaderTests {
    private func line(_ o: [String: Any]) throws -> String { String(decoding: try JSONSerialization.data(withJSONObject: o), as: UTF8.self) + "\n" }

    @Test func newestTitleWinsAndCustomBeatsAI() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        var t = try line(["type": "user", "cwd": "/w", "entrypoint": "cli", "message": ["content": "secret text"]])
        t += try line(["type": "ai-title", "aiTitle": "old", "sessionId": "x"])
        t += try line(["type": "ai-title", "aiTitle": "new", "sessionId": "x"])
        try s.write(t, to: "abc.jsonl")
        var r = TranscriptReader.summary(of: s.home.appendingPathComponent("abc.jsonl"))
        #expect(r?.title == "new" && r?.cwd == "/w" && r?.entrypoint == "cli" && r?.sessionId == "abc")
        t += try line(["type": "custom-title", "customTitle": "mine", "sessionId": "x"])
        try s.write(t, to: "abc.jsonl")
        r = TranscriptReader.summary(of: s.home.appendingPathComponent("abc.jsonl"))
        #expect(r?.title == "mine")
    }

    @Test func neverUsesUserTextAsTitle() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        try s.write(try line(["type": "user", "message": ["content": "sk-secret"]]), to: "a.jsonl")
        #expect(TranscriptReader.summary(of: s.home.appendingPathComponent("a.jsonl"))?.title == nil)
    }

    @Test func findsATitlePastTheWindowInALargeFile() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let filler = try line(["type": "assistant", "x": String(repeating: "y", count: 2000)])
        var t = String(repeating: filler, count: 60)
        t += try line(["type": "ai-title", "aiTitle": "deep", "sessionId": "x"])
        t += String(repeating: filler, count: 60)
        try s.write(t, to: "a.jsonl")
        #expect(t.utf8.count > 2 * TranscriptReader.window)
        #expect(TranscriptReader.summary(of: s.home.appendingPathComponent("a.jsonl"))?.title == "deep")
    }

    @Test func brokenFileGivesNil() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        try s.write("not json\nalso not\n", to: "a.jsonl")
        #expect(TranscriptReader.summary(of: s.home.appendingPathComponent("a.jsonl")) == nil)
        #expect(TranscriptReader.summary(of: s.home.appendingPathComponent("none.jsonl")) == nil)
    }
}

@Suite struct LiveSessionReaderTests {
    private func record(_ s: Sandbox, pid: Int32, start: String = "Sat Oct 31 10:58:34 2026", entrypoint: String = "cli") throws {
        try s.write(#"{"pid":\#(pid),"sessionId":"sid\#(pid)","procStart":"\#(start)","cwd":"/w","status":"idle","entrypoint":"\#(entrypoint)","messagingSocketPath":"/x"}"#, to: ".claude/sessions/\(pid).json")
    }

    private var startDate: Date { LiveSessionReader.parseProcStart("Sat Oct 31 10:58:34 2026")! }

    @Test func parsesProcStartAsUTC() {
        let d = LiveSessionReader.parseProcStart("Sat Oct 31 10:58:34 2026")
        let c = Calendar(identifier: .gregorian).dateComponents(in: TimeZone(identifier: "UTC")!, from: d!)
        #expect(c.hour == 10 && c.minute == 58 && c.day == 31)
        #expect(LiveSessionReader.parseProcStart("Sun Nov  1 09:05:00 2026") != nil)
    }

    @Test func aReusedPIDIsNotLive() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.defaultProfile()
        try record(s, pid: 10)
        let later = LiveSessionReader(locator: s.locator, processes: FakeProcesses(alive: [10], starts: [10: startDate.addingTimeInterval(500)]))
        #expect(later.liveRecords(for: p).isEmpty)
        #expect(!later.hasLiveSession(p))
        let same = LiveSessionReader(locator: s.locator, processes: FakeProcesses(alive: [10], starts: [10: startDate]))
        #expect(same.liveRecords(for: p).count == 1)
    }

    @Test func aDeadProcessIsNotLive() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.defaultProfile()
        try record(s, pid: 10)
        #expect(!LiveSessionReader(locator: s.locator, processes: FakeProcesses()).hasLiveSession(p))
    }

    @Test func aDesktopRecordCountsForSafetyChecks() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.defaultProfile()
        try record(s, pid: 11, entrypoint: "claude-desktop")
        #expect(LiveSessionReader(locator: s.locator, processes: FakeProcesses(alive: [11], starts: [11: startDate])).hasLiveSession(p))
    }

    @Test func anUnreadableRecentRecordFailsClosed() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.defaultProfile()
        try s.write("{broken", to: ".claude/sessions/12.json")
        var r = LiveSessionReader(locator: s.locator, processes: FakeProcesses())
        #expect(r.hasLiveSession(p))
        r.unreadableGrace = 0
        #expect(!r.hasLiveSession(p))
    }

    @Test func neverOpensKeyFiles() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.defaultProfile()
        try s.write("secret", to: ".claude/sessions/13.abc.key")
        var r = LiveSessionReader(locator: s.locator, processes: FakeProcesses())
        r.unreadableGrace = 1000
        #expect(!r.hasLiveSession(p))
    }

    @Test func realProcessInfoAgreesWithItself() {
        let me = ProcessInfo.processInfo.processIdentifier
        let info = ProcessInfoProvider()
        #expect(info.isAlive(pid: me))
        #expect(info.startTime(pid: me) != nil)
        #expect(info.isAlive(pid: 1))  // EPERM counts as alive
        #expect(!info.isAlive(pid: 999_999))
    }
}

@Suite struct ProcessRunnerTests {
    @Test func capturesOutputAndAppliesEnv() {
        let r = ProcessRunner().run(executable: "/bin/sh", args: ["-c", "echo $CPM_X; echo err >&2; echo ${CPM_Y-unset}"],
                                    env: LaunchEnv(set: ["CPM_X": "1"], unset: ["CPM_Y"]), timeout: 5)
        #expect(r.exitCode == 0 && r.stdout == "1\nunset\n" && r.stderr == "err\n")
    }

    @Test func timesOut() {
        let r = ProcessRunner().run(executable: "/bin/sleep", args: ["10"], env: nil, timeout: 0.2)
        #expect(r.timedOut)
    }

    @Test func missingExecutableIsReported() {
        #expect(ProcessRunner().run(executable: "/nonexistent", args: [], env: nil, timeout: 1).exitCode == 127)
    }
}

@Suite struct ShellInstallerTests {
    private func source(_ s: Sandbox) throws -> URL {
        let dir = s.home.appendingPathComponent("build/cli")
        for f in ShellInstaller.files { try s.write("#!/bin/sh\n", to: "build/cli/\(f)") }
        return dir
    }

    @Test func installsAndIsSafeToRepeat() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let i = ShellInstaller(store: s.store)
        #expect(!i.isInstalled())
        let src = try source(s)
        let r = try i.install(from: src)
        #expect(i.isInstalled())
        #expect(r.zshrcLine.contains("[[ -r"))
        _ = try i.install(from: src)
        #expect(FileManager.default.fileExists(atPath: s.locator.binDir.appendingPathComponent("VERSION").path))
    }

    @Test func neverResetsTheDefault() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try s.store.setDefault(id: p.id)
        _ = try ShellInstaller(store: s.store).install(from: try source(s))
        #expect(try s.store.defaultProfile().id == p.id)
    }

    @Test func aMissingFileIsAnError() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        #expect(throws: ProfileError.self) { try ShellInstaller(store: s.store).install(from: s.home) }
    }

    @Test func neverTouchesShellStartupFiles() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        try s.write("x", to: ".zshrc")
        _ = try ShellInstaller(store: s.store).install(from: try source(s))
        #expect(try String(contentsOf: s.home.appendingPathComponent(".zshrc"), encoding: .utf8) == "x")
        #expect(!FileManager.default.fileExists(atPath: s.home.appendingPathComponent(".zshenv").path))
    }
}
