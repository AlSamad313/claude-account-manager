import Foundation
import Testing
@testable import ProfileCore

@Suite struct SecretDetectorTests {
    @Test func catchesTokenShapes() {
        for s in ["sk-abcdefghijklmnopqrstuvwx", "x sk-ant-api03-abcdefghijklmnopqrst y", "ghp_abcdefghijklmnopqrstuvwxyz012345",
                  "xoxb-123-abc", String(repeating: "a1", count: 25), "QWxhZGRpbjpvcGVuIHNlc2FtZQQWxhZGRpbjpvcGVuIHNlc2FtZQ=="] {
            #expect(SecretDetector.looksSecret(s), "\(s)")
        }
    }

    @Test func ordinaryTextIsNotASecret() {
        for s in ["author", "monkey", "auth token key password", "a skill about tokens", "sk-short", "--verbose",
                  "/usr/local/bin/node", "@scope/some-package", "https://example.com/mcp",
                  "/Users/someone/Documents/Projects/a/very/long/path/that/goes/on/and/on/here",
                  "thisisalongwordwithnodigitsoruppercaselettersinitatallreally",
                  "task-abcdefghijklmnopqrstuvwxyz", "disk-usage-analyzer-plugin-for-claude",
                  String(repeating: "a", count: 39) ] {
            #expect(!SecretDetector.looksSecret(s), "\(s)")
        }
    }
}

@Suite struct SettingsMergerTests {
    @Test func permissionsCombineWithNoDuplicates() {
        var t: [String: Any] = ["permissions": ["allow": ["a"]], "other": 1]
        let s: [String: Any] = ["permissions": ["allow": ["a", "b", "b"]]]
        let new = SettingsMerger.newPermissions(source: s, target: t, key: "allow")
        #expect(new == ["b"])
        SettingsMerger.addPermissions(new, key: "allow", to: &t)
        #expect((t["permissions"] as? [String: Any])?["allow"] as? [String] == ["a", "b"])
        #expect(t["other"] as? Int == 1)
    }

    private func json(_ text: String) -> [String: Any] {
        try! JSONSerialization.jsonObject(with: Data(text.utf8)) as! [String: Any]
    }

    @Test func hooksMergeByEventAndMatcherWithoutDuplicates() {
        var t = json(#"{"hooks":{"Stop":[{"matcher":"m","hooks":[{"type":"command","command":"x"}]}]}}"#)
        let s = json(#"{"hooks":{"Stop":[{"matcher":"m","hooks":[{"command":"x","type":"command"},{"type":"command","command":"y"}]}]}}"#)
        let e0 = SettingsMerger.hookEntries(s)[0]
        let new = SettingsMerger.newHooks(event: "Stop", matcher: "m", entry: e0.entry, target: t)
        #expect(new.count == 1, "the same hook with its keys in another order is not added twice")
        SettingsMerger.addHooks(new, event: "Stop", matcher: "m", to: &t)
        let e = SettingsMerger.hookEntries(t)
        #expect(e.count == 1 && (e[0].entry["hooks"] as? [[String: Any]])?.count == 2)
        SettingsMerger.addHooks([["type": "command", "command": "z"]], event: "Start", matcher: "", to: &t)
        #expect(SettingsMerger.hookEntries(t).count == 2)
    }

    @Test func canonicalKeepsSlashes() {
        #expect(SettingsMerger.canonical(["a": "/x/y"]).contains("/x/y"))
    }
}

/// Answers Claude Code's own commands, optionally failing one.
private final class FakeClaude: ProcessRunning {
    var calls: [[String]] = []
    var failOn: String?
    var failOnlyOnce = false
    var onRun: (([String]) -> Void)?
    func run(executable: String, args: [String], env: LaunchEnv?, timeout: TimeInterval) -> ProcessResult {
        calls.append(args)
        onRun?(args)
        if let f = failOn, args.joined(separator: " ").contains(f) {
            if failOnlyOnce { failOn = nil }
            return ProcessResult(stderr: "boom", exitCode: 1)
        }
        return ProcessResult()
    }
}

private struct World {
    let s: Sandbox
    let source: Profile
    let target: Profile
    let claude = FakeClaude()
    let copier: SetupCopier
    var srcDir: URL { s.locator.configDir(for: source) }
    var dstDir: URL { s.locator.configDir(for: target) }

    init(processes: ProcessInfoProviding = FakeProcesses()) throws {
        s = try Sandbox()
        try s.write(#"{"oauthAccount":{"accountUuid":"u1","emailAddress":"a@e.com"},"mcpServers":{}}"#, to: ".claude.json")
        source = try s.store.defaultProfile()
        var t = try s.store.create(displayName: "Target", commandName: "target", color: "#000000")
        try s.signIn(t, uuid: "u2")
        try s.store.completeLogin(id: t.id, account: Account(accountUuid: "u2", email: "b@e.com", organizationName: nil), keychainService: nil)
        t = try s.store.load().profile(id: t.id)!
        target = t
        let bin = s.home.appendingPathComponent("realbin")
        try FileManager.default.createDirectory(at: bin, withIntermediateDirectories: true)
        try Data("#!/bin/sh\n".utf8).write(to: bin.appendingPathComponent("claude"))
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: bin.appendingPathComponent("claude").path)
        let cli = ClaudeCLI(locator: s.locator, runner: claude, binary: ClaudeBinaryLocator(locator: s.locator, path: bin.path))
        copier = SetupCopier(store: s.store, cli: cli, live: LiveSessionReader(locator: s.locator, processes: processes))
    }

    func sourceSettings(_ json: String) throws { try s.write(json, to: ".claude/settings.json") }
    func sourceConfig(_ mcp: String) throws {
        try s.write(#"{"oauthAccount":{"accountUuid":"u1"},"primaryApiKey":"sk-never-copied-abcdefghijkl","hasTrustDialogAccepted":true,"mcpServers":\#(mcp)}"#, to: ".claude.json")
    }
    func sourceFile(_ path: String, _ text: String) throws { try s.write(text, to: ".claude/\(path)") }
    func targetFile(_ path: String, _ text: String) throws {
        let url = dstDir.appendingPathComponent(path)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data(text.utf8).write(to: url)
    }
    func targetText(_ path: String) -> String? { try? String(contentsOf: dstDir.appendingPathComponent(path), encoding: .utf8) }
    func targetSettings() throws -> [String: Any] { try JSONSerialization.jsonObject(with: Data(contentsOf: dstDir.appendingPathComponent("settings.json"))) as! [String: Any] }
    func plan() throws -> CopyPlan { try copier.plan(source: source, target: target) }
    func item(_ p: CopyPlan, _ id: String) -> CopyItem? { p.items.first { $0.id == id } }
}

private func setTick(_ p: inout CopyPlan, _ id: String, _ v: Bool = true, _ change: (inout CopyItem) -> Void = { _ in }) {
    let i = p.items.firstIndex { $0.id == id }!
    p.items[i].ticked = v
    change(&p.items[i])
}

private let startDate = LiveSessionReader.parseProcStart("Sat Oct 31 10:58:34 2026")!

@Suite struct SetupCopierTests {
    @Test func theBuiltinProfileIsNeverATarget() throws {
        let w = try World(); defer { w.s.cleanup() }
        #expect(throws: ProfileError.self) { try w.copier.plan(source: w.target, target: w.source) }
        var p = try w.plan()
        p.targetId = w.source.id
        #expect(throws: ProfileError.self) { try w.copier.apply(p) }
    }

    /// Its old entry is in no backup, so a failed add must not lose it.
    @Test func aFailedMCPChangePutsTheOldEntryBack() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceConfig(#"{"srv":{"command":"new"}}"#)
        try w.targetFile(".claude.json", #"{"oauthAccount":{"accountUuid":"u2"},"mcpServers":{"srv":{"command":"old"}}}"#)
        var p = try w.plan()
        #expect(w.item(p, "mcp:srv")?.action == .change)
        setTick(&p, "mcp:srv")
        w.claude.failOn = "add-json"
        w.claude.failOnlyOnce = true
        let r = try w.copier.apply(p)
        #expect(r.failed.first?.reason.contains("old entry was put back") == true)
        let adds = w.claude.calls.filter { $0.contains("add-json") }
        #expect(adds.count == 2 && adds[1].joined().contains("old"))
    }

    @Test func aPluginInstallDoesNotBlockRestoringSettings() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceSettings(#"{"model":"m","enabledPlugins":{"tool@market":true}}"#)
        var p = try w.plan()
        setTick(&p, "model")
        setTick(&p, "plugin:tool@market")
        let settings = w.dstDir.appendingPathComponent("settings.json")
        w.claude.onRun = { args in
            if args.starts(with: ["plugin", "install"]) { try? Data(#"{"model":"m","enabledPlugins":{"tool@market":true}}"#.utf8).write(to: settings) }
        }
        let r = try w.copier.apply(p)
        #expect(r.failed.isEmpty)
        let restored = try w.copier.restore(profile: w.target, backupId: try #require(r.backupId))
        #expect(restored.leftAlone.isEmpty)
        #expect(w.targetText("settings.json")?.contains("\"model\"") != true)
    }

    @Test func aLinkLoopOrAPipeInASkillDoesNotHangThePreview() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceFile("skills/loop/SKILL.md", "hi")
        let dir = w.srcDir.appendingPathComponent("skills/loop")
        try FileManager.default.createSymbolicLink(atPath: dir.appendingPathComponent("self").path, withDestinationPath: dir.path)
        #expect(mkfifo(dir.appendingPathComponent("pipe").path, 0o600) == 0)
        var p = try w.plan()
        setTick(&p, "file:skills/loop")
        #expect(try w.copier.apply(p).failed.isEmpty)
        let copied = try FileManager.default.contentsOfDirectory(atPath: w.dstDir.appendingPathComponent("skills/loop").path)
        #expect(copied == ["SKILL.md"])
    }

    @Test func accountSyncedSkillsAreNeverOffered() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceFile("skills/synced/abc/SKILL.md", "from the account")
        try w.sourceFile("skills/mine/SKILL.md", "mine")
        let p = try w.plan()
        #expect(w.item(p, "file:skills/synced") == nil)
        #expect(w.item(p, "file:skills/mine") != nil)
    }

    @Test func aFileCreatedSinceThePreviewStopsTheCopy() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceFile("CLAUDE.md", "new")
        let p = try w.plan()
        try w.targetFile("CLAUDE.md", "written after the preview")
        #expect(throws: ProfileError.self) { try w.copier.apply(p) }
        #expect(w.targetText("CLAUDE.md") == "written after the preview")
    }

    @Test func aSecretInAURLOrPermissionIsCaught() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceConfig(#"{"srv":{"type":"http","url":"https://x.example/mcp?key=sk-abcdefghijklmnopqrstuvwx"}}"#)
        try w.sourceSettings(#"{"permissions":{"allow":["Bash(curl -H 'x: sk-abcdefghijklmnopqrstuvwx')"]}}"#)
        let p = try w.plan()
        #expect(w.item(p, "mcp:srv")?.ticked == false && w.item(p, "mcp:srv")?.secrets.first?.name == "url")
        #expect(w.item(p, "perm:allow")?.ticked == false && w.item(p, "perm:allow")?.flaggedFile == true)
    }

    @Test func defaultsMatchTheTable() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceSettings(#"{"model":"opus","env":{"A":"1"},"permissions":{"allow":["x"],"deny":["y"]},"hooks":{"Stop":[{"hooks":[{"type":"command","command":"z"}]}]},"enabledPlugins":{"a@m":true,"off@m":false},"statusLine":{"type":"command","command":"s"},"apiKeyHelper":"k","theme":"dark"}"#)
        try w.sourceConfig(#"{"plain":{"command":"node"},"withenv":{"command":"node","env":{"T":"v"}}}"#)
        try w.sourceFile("skills/s1/SKILL.md", "hello")
        try w.sourceFile("agents/a.md", "agent")
        try w.sourceFile("commands/c.md", "cmd")
        try w.sourceFile("CLAUDE.md", "memory")
        try w.sourceFile("sessions/1.json", "{}")
        try w.sourceFile("projects/p/t.jsonl", "{}")
        let p = try w.plan()
        func t(_ id: String) -> Bool? { w.item(p, id)?.ticked }
        #expect(t("mcp:plain") == true)
        #expect(t("mcp:withenv") == false)
        #expect(t("perm:allow") == true && t("perm:deny") == true)
        #expect(p.items.first { $0.kind == .hook }?.ticked == false)
        #expect(t("model") == false && t("env:A") == false)
        #expect(t("plugin:a@m") == true && t("plugin:off@m") == nil)
        #expect(t("file:skills/s1") == true && t("file:agents/a.md") == true && t("file:commands/c.md") == true && t("file:CLAUDE.md") == true)
        let ids = Set(p.items.map(\.id))
        for never in ["statusLine", "apiKeyHelper", "theme", "sessions", "projects", "oauthAccount", "hasTrustDialogAccepted", "primaryApiKey"] {
            #expect(!ids.contains { $0.contains(never) }, "\(never) must not be offered")
        }
    }

    @Test func directWritesMatchThePreviewAndAreMinimal() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceSettings(#"{"permissions":{"allow":["x"]},"model":"opus","statusLine":{"type":"command","command":"source-label"}}"#)
        try w.sourceFile("skills/s1/SKILL.md", "hello")
        try w.sourceFile("skills/s1/more/x.txt", "nested")
        let before = try w.targetSettings()
        let r = try w.copier.apply(try w.plan())
        #expect(r.failed.isEmpty)
        let after = try w.targetSettings()
        #expect((after["permissions"] as? [String: Any])?["allow"] as? [String] == ["x"])
        #expect(after["model"] == nil, "unticked items are not written")
        #expect(SettingsMerger.canonical(after["statusLine"]!) == SettingsMerger.canonical(before["statusLine"]!), "the target keeps its own status line")
        #expect(w.targetText("skills/s1/SKILL.md") == "hello")
        #expect(w.targetText("skills/s1/more/x.txt") == "nested")
    }

    @Test func aSymlinkedSkillIsCopiedNotLinked() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.s.write("real", to: "elsewhere/SKILL.md")
        try FileManager.default.createDirectory(at: w.srcDir.appendingPathComponent("skills"), withIntermediateDirectories: true)
        try FileManager.default.createSymbolicLink(at: w.srcDir.appendingPathComponent("skills/linked"), withDestinationURL: w.s.home.appendingPathComponent("elsewhere"))
        _ = try w.copier.apply(try w.plan())
        #expect(w.targetText("skills/linked/SKILL.md") == "real")
        let attrs = try FileManager.default.attributesOfItem(atPath: w.dstDir.appendingPathComponent("skills/linked").path)
        #expect(attrs[.type] as? FileAttributeType == .typeDirectory)
    }

    @Test func conflictsSkipOverwriteAndRename() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceFile("CLAUDE.md", "new")
        try w.targetFile("CLAUDE.md", "old")
        var p = try w.plan()
        #expect(w.item(p, "file:CLAUDE.md")?.action == .change && w.item(p, "file:CLAUDE.md")?.ticked == false)
        setTick(&p, "file:CLAUDE.md") { $0.conflict = .skip }
        _ = try w.copier.apply(p)
        #expect(w.targetText("CLAUDE.md") == "old")

        p = try w.plan()
        setTick(&p, "file:CLAUDE.md") { $0.conflict = .rename }
        _ = try w.copier.apply(p)
        #expect(w.targetText("CLAUDE.md") == "old" && w.targetText("CLAUDE-copy.md") == "new")

        p = try w.plan()
        setTick(&p, "file:CLAUDE.md") { $0.conflict = .rename }
        _ = try w.copier.apply(p)
        #expect(w.targetText("CLAUDE-copy.md") == "new" && w.targetText("CLAUDE-copy-2.md") == "new", "a rename never overwrites")

        p = try w.plan()
        setTick(&p, "file:CLAUDE.md") { $0.conflict = .overwrite }
        _ = try w.copier.apply(p)
        #expect(w.targetText("CLAUDE.md") == "new")
    }

    @Test func secretsInEnvAndHeadersAreDroppedUnlessTicked() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceConfig(#"{"srv":{"command":"node","env":{"TOKEN":"sekret-value","PLAIN":"x"},"headers":{"Authorization":"Bearer z"}}}"#)
        var p = try w.plan()
        let item = try #require(w.item(p, "mcp:srv"))
        #expect(!item.ticked)
        #expect(Set(item.secrets.map(\.name)) == ["env.TOKEN", "env.PLAIN", "headers.Authorization"])
        #expect(!item.detail.contains("sekret"), "the preview never shows a value")

        setTick(&p, "mcp:srv") { i in i.secrets[i.secrets.firstIndex { $0.name == "env.PLAIN" }!].ticked = true }
        _ = try w.copier.apply(p)
        let json = try #require(w.claude.calls.first { $0.starts(with: ["mcp", "add-json"]) }?[3])
        #expect(json.contains("PLAIN") && !json.contains("sekret-value") && !json.contains("Bearer"))
        #expect(w.claude.calls.first { $0.starts(with: ["mcp", "add-json"]) }?.suffix(2) == ["--scope", "user"])
    }

    @Test func aSecretInArgsBlocksThatServerUntilTicked() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceConfig(#"{"srv":{"command":"node","args":["--key","sk-abcdefghijklmnopqrstuvwx"]}}"#)
        var p = try w.plan()
        setTick(&p, "mcp:srv")
        let r = try w.copier.apply(p)
        #expect(r.failed.first?.itemId == "mcp:srv")
        #expect(!w.claude.calls.contains { $0.contains("add-json") })
        p = try w.plan()
        setTick(&p, "mcp:srv") { $0.secrets[0].ticked = true }
        #expect(try w.copier.apply(p).failed.isEmpty)
        #expect(w.claude.calls.contains { $0.contains("add-json") })
    }

    @Test func aChangedServerIsReplacedAndASameOneIsLeft() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceConfig(#"{"srv":{"command":"new"},"same":{"command":"s"}}"#)
        try w.targetFile(".claude.json", #"{"oauthAccount":{"accountUuid":"u2"},"mcpServers":{"srv":{"command":"old"},"same":{"command":"s"}}}"#)
        let p = try w.plan()
        #expect(w.item(p, "mcp:srv")?.action == .change && w.item(p, "mcp:same")?.action == .same && w.item(p, "mcp:same")?.ticked == false)
        _ = try w.copier.apply(p)
        let verbs = w.claude.calls.map { $0.prefix(2).joined(separator: " ") }
        #expect(verbs == ["mcp remove", "mcp add-json"])
    }

    @Test func aCommandIntoTheSourceFolderIsFlagged() throws {
        let w = try World(); defer { w.s.cleanup() }
        let dir = w.srcDir.path
        try w.sourceConfig(#"{"srv":{"command":"\#(dir)/tool.sh"}}"#)
        try w.sourceSettings(#"{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"\#(dir)/hook.sh"}]}]}}"#)
        let p = try w.plan()
        #expect(w.item(p, "mcp:srv")?.pointsIntoSource == true && w.item(p, "mcp:srv")?.ticked == false)
        #expect(p.items.first { $0.kind == .hook }?.pointsIntoSource == true)
    }

    @Test func aSecretLookingFileIsFlaggedAndUnticked() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceFile("skills/bad/SKILL.md", "use sk-abcdefghijklmnopqrstuvwx here")
        try w.sourceFile("skills/ok/SKILL.md", "about auth tokens and keys")
        let p = try w.plan()
        #expect(w.item(p, "file:skills/bad")?.flaggedFile == true && w.item(p, "file:skills/bad")?.ticked == false)
        #expect(w.item(p, "file:skills/ok")?.flaggedFile == false && w.item(p, "file:skills/ok")?.ticked == true)
    }

    @Test func aLiveSessionBlocksIncludingADesktopOne() throws {
        let w = try World(processes: FakeProcesses(alive: [5], starts: [5: startDate])); defer { w.s.cleanup() }
        try w.targetFile("sessions/5.json", #"{"pid":5,"sessionId":"x","procStart":"Sat Oct 31 10:58:34 2026","entrypoint":"claude-desktop"}"#)
        try w.sourceFile("CLAUDE.md", "x")
        let p = try w.plan()
        #expect(p.blockedReason?.contains("live session") == true)
        #expect(throws: ProfileError.self) { try w.copier.apply(p) }
        #expect(w.targetText("CLAUDE.md") == nil)
    }

    @Test func aFileChangedSinceThePreviewStopsEverything() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceSettings(#"{"permissions":{"allow":["x"]}}"#)
        try w.sourceFile("CLAUDE.md", "new")
        let p = try w.plan()
        try w.targetFile("settings.json", #"{"changed":true}"#)
        #expect(throws: ProfileError.self) { try w.copier.apply(p) }
        #expect(w.targetText("CLAUDE.md") == nil, "nothing is written")
        #expect(w.targetText("settings.json") == #"{"changed":true}"#)
    }

    @Test func aWrongShapeRefusesToWrite() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.targetFile("settings.json", #"{"permissions":"nonsense"}"#)
        #expect(try w.plan().blockedReason?.contains("unexpected shape") == true)
    }

    @Test func aFailureStopsAndReportsWhatWasDone() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceConfig(#"{"a":{"command":"1"},"b":{"command":"2"},"c":{"command":"3"}}"#)
        try w.sourceFile("CLAUDE.md", "memory")
        w.claude.failOn = "add-json b"
        let r = try w.copier.apply(try w.plan())
        #expect(r.done.contains("mcp:a") && r.done.contains("file:CLAUDE.md"))
        #expect(r.failed == [CopyFailure(itemId: "mcp:b", reason: "boom")])
        #expect(!r.done.contains("mcp:c"), "nothing after a failure runs")
        #expect(r.backupId != nil)
    }

    @Test func pluginsInstallWithTheirMarketplace() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceSettings(#"{"enabledPlugins":{"p@m":true},"extraKnownMarketplaces":{"m":{"source":{"source":"github","repo":"org/repo"}}}}"#)
        let r = try w.copier.apply(try w.plan())
        #expect(r.failed.isEmpty)
        #expect(w.claude.calls == [["plugin", "marketplace", "add", "org/repo"], ["plugin", "install", "p@m"]])
        #expect(r.removalCommands.first?.contains("plugin uninstall") == true)
        #expect(!FileManager.default.fileExists(atPath: w.dstDir.appendingPathComponent("plugins/installed_plugins.json").path), "SetupCopier never writes a plugin registry itself")
    }

    @Test func restoreUndoesDirectWritesWithMode() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceSettings(#"{"permissions":{"allow":["x"]}}"#)
        try w.sourceFile("skills/s1/SKILL.md", "hello")
        let settings = w.dstDir.appendingPathComponent("settings.json")
        try FileManager.default.setAttributes([.posixPermissions: 0o640], ofItemAtPath: settings.path)
        let original = try Data(contentsOf: settings)
        let r = try w.copier.apply(try w.plan())
        #expect(try Data(contentsOf: settings) != original)
        let res = try w.copier.restore(profile: w.target, backupId: try #require(r.backupId))
        #expect(try Data(contentsOf: settings) == original)
        #expect(try FileManager.default.attributesOfItem(atPath: settings.path)[.posixPermissions] as? Int == 0o640)
        #expect(res.removed.count == 1 && res.leftAlone.isEmpty)
        #expect(!FileManager.default.fileExists(atPath: w.dstDir.appendingPathComponent("skills").path), "empty folders it made are gone")
    }

    @Test func restoreLeavesLaterWorkAlone() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceFile("CLAUDE.md", "copied")
        try w.sourceFile("agents/a.md", "agent")
        let r = try w.copier.apply(try w.plan())
        try w.targetFile("CLAUDE.md", "edited later")
        let res = try w.copier.restore(profile: w.target, backupId: try #require(r.backupId))
        #expect(w.targetText("CLAUDE.md") == "edited later", "a file changed since the copy is left alone")
        #expect(res.leftAlone.count == 1)
        #expect(w.targetText("agents/a.md") == nil)
    }

    @Test func restoreRefusesDuringALiveSession() throws {
        var p = FakeProcesses()
        let w0 = try World(processes: p); defer { w0.s.cleanup() }
        try w0.sourceFile("CLAUDE.md", "x")
        let r = try w0.copier.apply(try w0.plan())
        try w0.targetFile("sessions/5.json", #"{"pid":5,"sessionId":"x","procStart":"Sat Oct 31 10:58:34 2026"}"#)
        p = FakeProcesses(alive: [5], starts: [5: startDate])
        let liveCopier = SetupCopier(store: w0.s.store, cli: w0.copier.cli, live: LiveSessionReader(locator: w0.s.locator, processes: p))
        #expect(throws: ProfileError.self) { try liveCopier.restore(profile: w0.target, backupId: r.backupId!) }
        #expect(w0.targetText("CLAUDE.md") == "x")
    }

    @Test func backupsAreStoredPrivatelyAndTrimmed() throws {
        let w = try World(); defer { w.s.cleanup() }
        try w.sourceSettings(#"{"permissions":{"allow":["x"]}}"#)
        let r = try w.copier.apply(try w.plan())
        let dir = w.s.locator.profilesRoot.appendingPathComponent("backups/\(w.target.id)/\(r.backupId!)")
        let fm = FileManager.default
        #expect(try fm.attributesOfItem(atPath: dir.path)[.posixPermissions] as? Int == 0o700)
        #expect(try fm.attributesOfItem(atPath: dir.appendingPathComponent("manifest.json").path)[.posixPermissions] as? Int == 0o600)
        let store = BackupStore(locator: w.s.locator)
        for _ in 0..<(BackupStore.keep + 3) {
            let b = try store.begin(profileId: w.target.id, paths: [])
            try store.save(b.manifest, id: b.id)
        }
        #expect(store.list(profileId: w.target.id).count == BackupStore.keep)
    }
}

/// Only runs with CPM_REAL_SETUP set: a preview of the real ~/.claude, read-only, into a throwaway
/// target. Prints names, never values.
@Suite struct RealSetupPreview {
    @Test func preview() throws {
        guard let real = ProcessInfo.processInfo.environment["CPM_REAL_SETUP"] else { return }
        let s = try Sandbox(); defer { s.cleanup() }
        try FileManager.default.createSymbolicLink(at: s.home.appendingPathComponent(".claude"), withDestinationURL: URL(fileURLWithPath: real + "/.claude"))
        try FileManager.default.createSymbolicLink(at: s.home.appendingPathComponent(".claude.json"), withDestinationURL: URL(fileURLWithPath: real + "/.claude.json"))
        let source = try s.store.defaultProfile()
        var t = try s.store.create(displayName: "T", commandName: "t", color: "#000000")
        try s.signIn(t, uuid: "u2")
        try s.store.completeLogin(id: t.id, account: Account(accountUuid: "u2", email: nil, organizationName: nil), keychainService: nil)
        t = try s.store.load().profile(id: t.id)!
        let started = Date()
        let p = try SetupCopier(store: s.store).plan(source: source, target: t)
        print("PREVIEW took", round(Date().timeIntervalSince(started) * 100) / 100, "s, blocked:", p.blockedReason ?? "no")
        for i in p.items {
            print("PREVIEW", i.kind.rawValue, i.action.rawValue, i.ticked ? "ticked" : "off", i.flaggedFile ? "FLAGGED" : "", i.pointsIntoSource ? "POINTS-INTO-SOURCE" : "", i.needsReLogin ? "relogin" : "", i.secrets.isEmpty ? "" : "secrets:\(i.secrets.count)", i.key)
        }
    }
}

/// Only runs with CPM_REAL_CLAUDE set, against the real claude command, in throwaway folders.
@Suite struct RealClaudeTests {
    @Test func realMCPCommandsLandInTheTargetAndReplaceWorks() throws {
        guard let path = ProcessInfo.processInfo.environment["CPM_REAL_CLAUDE"] else { return }
        let s = try Sandbox(); defer { s.cleanup() }
        try s.write(#"{"oauthAccount":{"accountUuid":"u1"},"mcpServers":{"t1":{"type":"stdio","command":"/usr/bin/true","args":["one"],"env":{"KEEP":"kept","DROP":"dropped"}}}}"#, to: ".claude.json")
        let source = try s.store.defaultProfile()
        let target = try s.store.create(displayName: "T", commandName: "t", color: "#000000")
        try s.signIn(target, uuid: "u2")
        try s.store.completeLogin(id: target.id, account: Account(accountUuid: "u2", email: nil, organizationName: nil), keychainService: nil)
        let t = try s.store.load().profile(id: target.id)!
        let cli = ClaudeCLI(locator: s.locator, binary: ClaudeBinaryLocator(locator: s.locator, path: path))
        let copier = SetupCopier(store: s.store, cli: cli)

        var p = try copier.plan(source: source, target: t)
        setTick(&p, "mcp:t1") { i in i.secrets[i.secrets.firstIndex { $0.name == "env.KEEP" }!].ticked = true }
        var r = try copier.apply(p)
        #expect(r.failed.isEmpty, "\(r.failed)")

        func targetServers() throws -> [String: [String: Any]] {
            let top = try JSONSerialization.jsonObject(with: Data(contentsOf: s.locator.configFile(for: t))) as! [String: Any]
            #expect(top["oauthAccount"] != nil, "Claude Code's write kept the account")
            return top["mcpServers"] as? [String: [String: Any]] ?? [:]
        }
        var servers = try targetServers()
        #expect(servers["t1"]?["command"] as? String == "/usr/bin/true")
        #expect((servers["t1"]?["env"] as? [String: String]) == ["KEEP": "kept"], "the unticked secret was dropped")

        // Changed in the source, so the target's copy is replaced.
        try s.write(#"{"oauthAccount":{"accountUuid":"u1"},"mcpServers":{"t1":{"type":"stdio","command":"/usr/bin/false"}}}"#, to: ".claude.json")
        p = try copier.plan(source: source, target: t)
        #expect(p.items.first { $0.id == "mcp:t1" }?.action == .change)
        r = try copier.apply(p)
        #expect(r.failed.isEmpty, "\(r.failed)")
        servers = try targetServers()
        #expect(servers["t1"]?["command"] as? String == "/usr/bin/false")
    }
}
