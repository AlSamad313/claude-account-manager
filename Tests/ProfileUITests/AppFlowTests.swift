import AppKit
import Foundation
import ProfileCore
import SwiftUI
import Testing
@testable import ProfileUI

private final class FakeKeychain: KeychainEditing {
    var names: Set<String> = ["Claude Code-credentials"]
    func listServiceNames() -> Set<String> { names }
    func delete(service: String) throws { names.remove(service) }
}

private final class FakeOpener: TerminalOpener {
    var texts: [String] = []
    func open(commandFile: URL) throws { texts.append(try String(contentsOf: commandFile, encoding: .utf8)) }
}

private struct FakeProcesses: ProcessInfoProviding {
    var alive: Set<Int32> = []
    var starts: [Int32: Date] = [:]
    func isAlive(pid: Int32) -> Bool { alive.contains(pid) }
    func startTime(pid: Int32) -> Date? { starts[pid] }
    func signal(pid: Int32, _ sig: Int32) -> Bool { true }
}

private final class FakeRunner: ProcessRunning {
    var calls: [[String]] = []
    func run(executable: String, args: [String], env: LaunchEnv?, timeout: TimeInterval) -> ProcessResult {
        calls.append(args)
        return ProcessResult(stdout: "CPM_PATH_MARKER\n/usr/bin\n\nCPM_VARS_MARKER\n")
    }
}

private let startText = "Sat Oct 31 10:58:34 2026"
private let startDate = LiveSessionReader.parseProcStart(startText)!

@MainActor
private final class Harness {
    let home: URL
    let locator: ConfigLocator
    let store: ProfileStore
    let keychain = FakeKeychain()
    let opener = FakeOpener()
    let runner = FakeRunner()
    let model: AppModel

    init(installed: Bool = true, processes: FakeProcesses = FakeProcesses(alive: [7], starts: [7: startDate])) throws {
        home = URL(fileURLWithPath: "/private/tmp/cpm-ui-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: home, withIntermediateDirectories: true)
        let real = NSHomeDirectoryForUser(NSUserName()) ?? NSHomeDirectory()
        precondition(!home.path.hasPrefix(real), "Test home must not be under the real home")
        locator = ConfigLocator(home: home)
        store = ProfileStore(locator: locator, trash: { try FileManager.default.removeItem(at: $0) })
        let bin = home.appendingPathComponent("realbin")
        try FileManager.default.createDirectory(at: bin, withIntermediateDirectories: true)
        try Data("#!/bin/sh\n".utf8).write(to: bin.appendingPathComponent("claude"))
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: bin.appendingPathComponent("claude").path)
        let cli = ClaudeCLI(locator: locator, runner: runner, binary: ClaudeBinaryLocator(locator: locator, path: bin.path))
        var login = LoginCoordinator(store: store, keychain: keychain, cli: cli)
        login.keychainWaitSeconds = 0
        var launcher = Launcher(store: store, opener: opener, runner: runner)
        launcher.isShellInstalled = { installed }
        let installer = ShellInstaller(store: store)
        if installed {
            try FileManager.default.createDirectory(at: locator.binDir, withIntermediateDirectories: true)
            for f in ["cpm", "claude", "claude-as", "claude-core.zsh", "claude-profiles.zsh", "profile-label"] {
                try Data("#!/bin/sh\n".utf8).write(to: locator.binDir.appendingPathComponent(f))
            }
        }
        var stopper = SessionStopper(locator: locator, processes: processes)
        stopper.graceSeconds = 0.05
        stopper.pollInterval = 0.01
        model = AppModel(env: AppEnvironment(store: store, login: login, launcher: launcher,
                                             index: SessionIndex(locator: locator, processes: processes), stopper: stopper,
                                             doctor: Doctor(locator: locator, keychain: keychain, runner: runner), installer: installer,
                                             copier: SetupCopier(store: store, cli: cli, live: LiveSessionReader(locator: locator, processes: processes)),
                                             remover: ProfileRemover(store: store, cli: cli, keychain: keychain, live: LiveSessionReader(locator: locator, processes: processes),
                                                                     trash: { try FileManager.default.removeItem(at: $0) }),
                                             shellSource: nil))
    }

    func builtinAccount(org: String = "Acme") throws {
        let json = #"{"oauthAccount":{"accountUuid":"u1","emailAddress":"me@example.com","organizationName":"\#(org)"}}"#
        try Data(json.utf8).write(to: home.appendingPathComponent(".claude.json"))
    }

    func signIn(_ p: Profile, uuid: String, email: String) throws {
        let json = #"{"oauthAccount":{"accountUuid":"\#(uuid)","emailAddress":"\#(email)","organizationName":"Org"}}"#
        try Data(json.utf8).write(to: locator.configFile(for: p))
    }

    func transcript(_ p: Profile, id: String, title: String, cwd: String, age: TimeInterval = 100) throws {
        let dir = locator.configDir(for: p).appendingPathComponent("projects/proj")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let url = dir.appendingPathComponent(id + ".jsonl")
        let first = #"{"type":"user","cwd":"\#(cwd)","entrypoint":"cli","sessionId":"\#(id)"}"#
        let second = #"{"type":"ai-title","aiTitle":"\#(title)","sessionId":"\#(id)"}"#
        try Data((first + "\n" + second + "\n").utf8).write(to: url)
        try FileManager.default.setAttributes([.modificationDate: Date().addingTimeInterval(-age)], ofItemAtPath: url.path)
    }

    func live(_ p: Profile, pid: Int32, id: String, status: String = "busy", title: String = "Running one") throws {
        let dir = locator.configDir(for: p).appendingPathComponent("sessions")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let json = #"{"pid":\#(pid),"sessionId":"\#(id)","procStart":"\#(startText)","cwd":"/w","status":"\#(status)","entrypoint":"cli","name":"\#(title)"}"#
        try Data(json.utf8).write(to: dir.appendingPathComponent("\(pid).json"))
    }

    /// Waits for the action a button started.
    func settle() async { await model.lastAction?.value }

    deinit { try? FileManager.default.removeItem(at: home) }
}

@Suite @MainActor struct AppFlowTests {
    @Test func copyPreviewThenApplyThenRestore() async throws {
        let h = try Harness()
        try h.builtinAccount()
        try FileManager.default.createDirectory(at: h.locator.builtinDir, withIntermediateDirectories: true)
        try Data("memory".utf8).write(to: h.locator.builtinDir.appendingPathComponent("CLAUDE.md"))
        let a = try h.store.create(displayName: "A", commandName: "a", color: "#000000")
        try h.signIn(a, uuid: "u2", email: "a@example.com")
        try h.store.completeLogin(id: a.id, account: Account(accountUuid: "u2", email: "a@example.com", organizationName: nil), keychainService: nil)
        await h.model.refresh()
        await h.model.preparePreview(sourceId: Profile.builtinID, targetId: a.id)
        #expect(h.model.copyPlan?.tickedCount == 1)
        await h.model.applyCopy()
        let dest = h.locator.configDir(for: a).appendingPathComponent("CLAUDE.md")
        #expect(FileManager.default.fileExists(atPath: dest.path))
        #expect(h.model.copyResult?.failed.isEmpty == true)
        let backup = try #require(h.model.backups.first)
        await h.model.restore(backupId: backup.id, targetId: a.id)
        #expect(!FileManager.default.fileExists(atPath: dest.path))
        #expect(h.model.restoreResult?.removed.count == 1)
    }

    @Test func removeProfileMovesTheDefaultAndClearsTheSelection() async throws {
        let h = try Harness()
        try h.builtinAccount()
        let a = try h.store.create(displayName: "A", commandName: "a", color: "#000000")
        try h.signIn(a, uuid: "u2", email: "a@example.com")
        try h.store.completeLogin(id: a.id, account: Account(accountUuid: "u2", email: "a@example.com", organizationName: nil), keychainService: nil)
        try h.store.setDefault(id: a.id)
        await h.model.refresh()
        h.model.sidebarSelection = a.id
        h.model.route = .remove(profileId: a.id)
        await h.model.removeProfile(profileId: a.id)
        #expect(h.model.profile(id: a.id) == nil)
        #expect(h.model.defaultProfileId == Profile.builtinID)
        #expect(h.model.sidebarSelection == "all")
        #expect(h.model.route == .main)
        #expect(!FileManager.default.fileExists(atPath: a.configDir!))
    }

    @Test func removeIsRefusedDuringALiveSessionAndStaysPut() async throws {
        let h = try Harness()
        let a = try h.store.create(displayName: "A", commandName: "a", color: "#000000")
        try h.signIn(a, uuid: "u2", email: "a@example.com")
        try h.store.completeLogin(id: a.id, account: Account(accountUuid: "u2", email: "a@example.com", organizationName: nil), keychainService: nil)
        try h.live(a, pid: 7, id: "running")
        await h.model.refresh()
        #expect(h.model.hasLiveSession(profileId: a.id))
        h.model.route = .remove(profileId: a.id)
        await h.model.removeProfile(profileId: a.id)
        #expect(h.model.errorText?.contains("live session") == true)
        #expect(h.model.route == .remove(profileId: a.id))
        #expect(h.model.profile(id: a.id) != nil)
    }

    @Test func aBuiltinTargetIsAnErrorNotACrash() async throws {
        let h = try Harness()
        let a = try h.store.create(displayName: "A", commandName: "a", color: "#000000")
        await h.model.refresh()
        await h.model.preparePreview(sourceId: a.id, targetId: Profile.builtinID)
        #expect(h.model.copyPlan == nil)
        #expect(h.model.errorText?.contains("never into") == true)
    }

    @Test func refreshLoadsProfilesAndSessions() async throws {
        let h = try Harness()
        try h.builtinAccount()
        let b = try h.store.defaultProfile()
        try h.transcript(b, id: "s1", title: "Fix the thing", cwd: "/w")
        await h.model.refresh()
        #expect(h.model.profiles.count == 1)
        #expect(h.model.defaultProfile?.displayName == "Acme")
        #expect(h.model.rows.map(\.title) == ["Fix the thing"])
        #expect(h.model.shellInstalled)
    }

    @Test func filterMatchesTitleFolderAndProfileName() async throws {
        let h = try Harness()
        let b = try h.store.defaultProfile()
        let a = try h.store.create(displayName: "Personal", commandName: "personal", color: "#000000")
        try h.transcript(b, id: "s1", title: "Alpha", cwd: "/one")
        try h.transcript(a, id: "s2", title: "Beta", cwd: "/two")
        await h.model.refresh()
        h.model.filter = "alph"
        #expect(h.model.panelRows.map(\.sessionId) == ["s1"])
        h.model.filter = "/two"
        #expect(h.model.panelRows.map(\.sessionId) == ["s2"])
        h.model.filter = "PERSONAL"
        #expect(h.model.filteredRows.map(\.sessionId) == ["s2"])
        h.model.filter = ""
        h.model.sidebarSelection = a.id
        #expect(h.model.filteredRows.map(\.sessionId) == ["s2"])
        #expect(h.model.panelRows.count == 2)
    }

    @Test func panelShowsEightLiveFirst() async throws {
        let h = try Harness()
        let b = try h.store.defaultProfile()
        for i in 0..<12 { try h.transcript(b, id: "s\(i)", title: "T\(i)", cwd: "/w", age: Double(100 + i)) }
        try h.live(b, pid: 7, id: "running")
        await h.model.refresh()
        #expect(h.model.panelRows.count == AppModel.panelRowLimit)
        #expect(h.model.panelRows[0].sessionId == "running" && h.model.panelRows[0].live)
        #expect(h.model.liveCount(profileId: b.id) == 1)
    }

    @Test func newSessionCallsLauncherThroughClaudeAs() async throws {
        let h = try Harness()
        let a = try h.store.create(displayName: "A", commandName: "a", color: "#000000")
        await h.model.refresh()
        h.model.newSession(profile: a)
        await h.settle()
        #expect(h.opener.texts.first?.contains("claude-as\" 'a'") == true)
        #expect(h.model.errorText == nil)
    }

    @Test func withoutTheShellNothingLaunchesAndItSaysWhy() async throws {
        let h = try Harness(installed: false)
        let b = try h.store.defaultProfile()
        await h.model.refresh()
        #expect(!h.model.shellInstalled)
        h.model.newSession(profile: b)
        await h.settle()
        #expect(h.opener.texts.isEmpty)
        #expect(h.model.errorText?.contains("install-shell") == true)
    }

    @Test func resumeUsesTheOwner() async throws {
        let h = try Harness()
        let a = try h.store.create(displayName: "A", commandName: "a", color: "#000000")
        try h.transcript(a, id: "11111111-2222-3333-4444-555555555555", title: "T", cwd: h.home.path)
        await h.model.refresh()
        h.model.resume(h.model.rows[0])
        await h.settle()
        #expect(h.opener.texts.first?.contains("'a' '--resume' '11111111-2222-3333-4444-555555555555'") == true)
    }

    /// With one added profile as From, To has nothing valid. It used to wait on a spinner forever.
    @Test func copyTargetFollowsTheSource() async throws {
        let h = try Harness()
        let a = try h.store.create(displayName: "A", commandName: "a", color: "#000000")
        let b = try h.store.create(displayName: "B", commandName: "b", color: "#000000")
        #expect(copyTarget(source: Profile.builtinID, current: "", targets: [a]) == a.id)
        #expect(copyTarget(source: a.id, current: a.id, targets: [a]) == "")
        #expect(copyTarget(source: a.id, current: a.id, targets: [a, b]) == b.id)
        #expect(copyTarget(source: Profile.builtinID, current: b.id, targets: [a, b]) == b.id)
    }

    @Test func aMissingFolderOffersChooseFolder() async throws {
        let h = try Harness()
        let b = try h.store.defaultProfile()
        try h.transcript(b, id: "11111111-2222-3333-4444-555555555555", title: "T", cwd: "/no/such/place")
        await h.model.refresh()
        h.model.resume(h.model.rows[0])
        await h.settle()
        #expect(h.model.missingFolderRow?.sessionId == "11111111-2222-3333-4444-555555555555")
        #expect(h.opener.texts.isEmpty)
        #expect(h.model.errorText == nil)
    }

    @Test func stopGoesThroughConfirmationThenSignals() async throws {
        let h = try Harness()
        let b = try h.store.defaultProfile()
        try h.live(b, pid: 7, id: "running")
        await h.model.refresh()
        let row = try #require(h.model.row(sessionId: "running"))
        h.model.requestStop(row)
        #expect(h.model.route == .stop(sessionId: "running"))
        await h.model.confirmStop(row)
        #expect(h.model.route == .main)
        // The fake process never dies, so the model must say so.
        #expect(h.model.errorText?.contains("did not stop") == true)
    }

    @Test func addProfileOpensSignInThenKeepCompletes() async throws {
        let h = try Harness()
        await h.model.refresh()
        await h.model.addProfile(displayName: "Work", commandName: "work", color: "#0F8A7E")
        let p = try #require(h.model.profiles.first { $0.commandName == "work" })
        #expect(h.model.route == .waiting(profileId: p.id))
        #expect(h.opener.texts.first?.contains("'work' 'auth' 'login'") == true)

        // Nothing completes until the user chooses Keep.
        try h.signIn(p, uuid: "u2", email: "w@example.com")
        h.keychain.names.insert("Claude Code-credentials-abc")
        await h.model.tick()
        if case .detected(let a, _) = h.model.loginState { #expect(a.email == "w@example.com") } else { Issue.record("not detected") }
        #expect(h.model.profile(id: p.id)?.state == .pendingLogin)

        await h.model.keepLogin(profileId: p.id)
        #expect(h.model.route == .main)
        let kept = try #require(h.model.profile(id: p.id))
        #expect(kept.state == .ready && kept.keychainService == "Claude Code-credentials-abc")
    }

    @Test func duplicateAccountIsFlagged() async throws {
        let h = try Harness()
        try h.builtinAccount()
        await h.model.refresh()
        await h.model.addProfile(displayName: "Dup", commandName: "dup", color: "#000000")
        let p = try #require(h.model.profiles.first { $0.commandName == "dup" })
        try h.signIn(p, uuid: "u1", email: "me@example.com")
        await h.model.pollLogin(profileId: p.id)
        if case .detected(_, let dup) = h.model.loginState { #expect(dup == Profile.builtinID) } else { Issue.record("not detected") }
    }

    @Test func badNamesShowAnErrorAndStayOnTheForm() async throws {
        let h = try Harness()
        await h.model.refresh()
        h.model.route = .add
        await h.model.addProfile(displayName: "X", commandName: "bad name", color: "#000000")
        #expect(h.model.errorText?.contains("not a valid command name") == true)
        #expect(h.model.route == .add)
    }

    @Test func discardRemovesAPendingProfile() async throws {
        let h = try Harness()
        await h.model.refresh()
        await h.model.addProfile(displayName: "Work", commandName: "work", color: "#000000")
        let p = try #require(h.model.profiles.first { $0.commandName == "work" })
        await h.model.discardLogin(profileId: p.id)
        #expect(h.model.profile(id: p.id) == nil)
        #expect(h.model.route == .main)
    }

    @Test func setDefaultRenameAndColourAreSaved() async throws {
        let h = try Harness()
        let a = try h.store.create(displayName: "A", commandName: "a", color: "#000000")
        await h.model.refresh()
        h.model.setDefault(profile: a)
        await h.settle()
        #expect(h.model.defaultProfileId == a.id)
        h.model.rename(profile: a, to: "B")
        await h.settle()
        #expect(h.model.profile(id: a.id)?.displayName == "B")
        h.model.setColor(profile: a, color: "#123456")
        await h.settle()
        #expect(h.model.profile(id: a.id)?.color == "#123456")
    }

    @Test func anAccountChangeGivesANotice() async throws {
        let h = try Harness()
        let a = try h.store.create(displayName: "A", commandName: "a", color: "#000000")
        try h.signIn(a, uuid: "1", email: "x@example.com")
        try h.store.completeLogin(id: a.id, account: Account(accountUuid: "1", email: "x@example.com", organizationName: nil), keychainService: nil)
        try h.signIn(a, uuid: "2", email: "y@example.com")
        await h.model.refresh()
        #expect(h.model.noticeText?.contains("y@example.com") == true)
    }

    @Test func vsCodeRunningSaysToQuitIt() async throws {
        let h = try Harness()
        let b = try h.store.defaultProfile()
        // FakeRunner returns no "already running", so this checks the arguments and no error.
        await h.model.refresh()
        h.model.openVSCode(profile: b)
        await h.settle()
        #expect(h.runner.calls.contains { $0.contains("Visual Studio Code") })
        #expect(h.model.errorText == nil)
    }

    @Test func tickWritesNothing() async throws {
        let h = try Harness()
        _ = try h.store.defaultProfile()
        await h.model.refresh()
        let old = Date().addingTimeInterval(-500)
        try FileManager.default.setAttributes([.modificationDate: old], ofItemAtPath: h.locator.registryFile.path)
        await h.model.tick()
        await h.model.refresh()
        let m = try FileManager.default.attributesOfItem(atPath: h.locator.registryFile.path)[.modificationDate] as! Date
        #expect(abs(m.timeIntervalSince(old)) < 2, "an unchanged registry must not wake the file watcher")
    }

    @Test func aWatcherReportsChangesAndIgnoresProfileFolders() async throws {
        let h = try Harness()
        let root = h.locator.profilesRoot
        try FileManager.default.createDirectory(at: h.locator.profilesDir.appendingPathComponent("p"), withIntermediateDirectories: true)
        try await Task.sleep(for: .milliseconds(300))
        let counter = Counter()
        let w = FileWatcher(paths: [root.path], ignoring: [h.locator.profilesDir.path]) { counter.add() }
        defer { w?.stop() }
        try await Task.sleep(for: .milliseconds(500))
        let before = counter.value
        try Data("x".utf8).write(to: h.locator.profilesDir.appendingPathComponent("p/transcript"))
        try await Task.sleep(for: .milliseconds(700))
        #expect(counter.value == before, "a write inside a profile folder must not wake the app")
        try Data("x".utf8).write(to: root.appendingPathComponent("default"))
        try await Task.sleep(for: .milliseconds(900))
        #expect(counter.value > before)
    }
}

private final class Counter: @unchecked Sendable {
    private let lock = NSLock()
    private var n = 0
    var value: Int { lock.lock(); defer { lock.unlock() }; return n }
    func add() { lock.lock(); n += 1; lock.unlock() }
}

/// Writes PNGs of the real views when CPM_RENDER_DIR is set, so they can be looked at.
@Suite @MainActor struct RenderViews {
    private func save<V: View>(_ view: V, _ name: String, in dir: String) {
        let r = ImageRenderer(content: view.environment(\.colorScheme, .light).background(Color(nsColor: .windowBackgroundColor)))
        r.scale = 2
        guard let image = r.nsImage, let tiff = image.tiffRepresentation,
              let png = NSBitmapImageRep(data: tiff)?.representation(using: .png, properties: [:]) else { return }
        try? png.write(to: URL(fileURLWithPath: dir).appendingPathComponent(name + ".png"))
    }

    @Test func render() async throws {
        guard let dir = ProcessInfo.processInfo.environment["CPM_RENDER_DIR"] else { return }
        let h = try Harness()
        try h.builtinAccount(org: "Company")
        let b = try h.store.defaultProfile()
        let a = try h.store.create(displayName: "Personal", commandName: "personal", color: "#E8772E")
        try h.signIn(a, uuid: "u2", email: "you@gmail.example")
        try h.store.completeLogin(id: a.id, account: Account(accountUuid: "u2", email: "you@gmail.example", organizationName: nil), keychainService: nil)
        try h.transcript(b, id: "s1", title: "Urdu page layout", cwd: "/Users/me/Projects/mabel-website", age: 7000)
        try h.transcript(a, id: "s2", title: "Minesweeper end card", cwd: "/Users/me/Projects/minesweeper-ad", age: 90000)
        try h.live(b, pid: 7, id: "running", title: "Phase 4 estimation ledger")
        await h.model.refresh()
        save(MenuPanelView(model: h.model, openWindow: {}), "panel", in: dir)
        h.model.route = .add
        save(MenuPanelView(model: h.model, openWindow: {}), "add", in: dir)
        h.model.route = .stop(sessionId: "running")
        save(FlowView(model: h.model).frame(width: 400), "stop", in: dir)
        h.model.route = .settings(profileId: a.id)
        save(FlowView(model: h.model).frame(width: 400), "settings", in: dir)
        try Data(#"{"oauthAccount":{"accountUuid":"u1"},"mcpServers":{"figma":{"command":"node"},"github":{"command":"node","env":{"GITHUB_TOKEN":"x"}}}}"#.utf8).write(to: h.home.appendingPathComponent(".claude.json"))
        try FileManager.default.createDirectory(at: h.locator.builtinDir, withIntermediateDirectories: true)
        try Data(#"{"model":"opus","permissions":{"allow":["Bash(git status)"]},"enabledPlugins":{"p@m":true}}"#.utf8).write(to: h.locator.builtinDir.appendingPathComponent("settings.json"))
        await h.model.refresh()
        await h.model.preparePreview(sourceId: b.id, targetId: a.id)
        h.model.route = .copy
        save(FlowView(model: h.model).frame(width: 440), "copy", in: dir)
    }
}
