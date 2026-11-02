import Foundation
import Testing
@testable import ProfileCore

private final class Journal: @unchecked Sendable {
    var steps: [String] = []
}

private final class Keys: KeychainEditing {
    var names: Set<String>
    let journal: Journal
    var denyDelete = false
    init(_ names: Set<String>, journal: Journal) {
        self.names = names
        self.journal = journal
    }
    func listServiceNames() -> Set<String> { names }
    func delete(service: String) throws {
        if service == KeychainProbe.plainName { throw ProfileError.notAllowed("plain") }
        if denyDelete { throw ProfileError.failed("denied") }
        journal.steps.append("keychain:\(service)")
        names.remove(service)
    }
}

private final class Claude: ProcessRunning {
    let journal: Journal
    var exitCode: Int32 = 0
    var onLogout: (() -> Void)?
    var environments: [LaunchEnv?] = []
    init(journal: Journal) { self.journal = journal }
    func run(executable: String, args: [String], env: LaunchEnv?, timeout: TimeInterval) -> ProcessResult {
        journal.steps.append(args.joined(separator: " "))
        environments.append(env)
        onLogout?()
        return ProcessResult(stderr: exitCode == 0 ? "" : "no network", exitCode: exitCode)
    }
}

private let startDate = LiveSessionReader.parseProcStart("Sat Oct 31 10:58:34 2026")!

private struct World {
    let s: Sandbox
    let journal = Journal()
    let keys: Keys
    let claude: Claude
    let trashFails = TrashSwitch()

    init() throws {
        s = try Sandbox()
        keys = Keys([KeychainProbe.plainName, "Claude Code-credentials-abc"], journal: journal)
        claude = Claude(journal: journal)
        try s.write(#"{"oauthAccount":{"accountUuid":"u1","emailAddress":"a@e.com"}}"#, to: ".claude.json")
    }

    func remover(processes: ProcessInfoProviding = FakeProcesses()) throws -> ProfileRemover {
        let bin = s.home.appendingPathComponent("realbin")
        try FileManager.default.createDirectory(at: bin, withIntermediateDirectories: true)
        try Data("#!/bin/sh\n".utf8).write(to: bin.appendingPathComponent("claude"))
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: bin.appendingPathComponent("claude").path)
        let cli = ClaudeCLI(locator: s.locator, runner: claude, binary: ClaudeBinaryLocator(locator: s.locator, path: bin.path))
        let j = journal, sw = trashFails
        return ProfileRemover(store: s.store, cli: cli, keychain: keys, live: LiveSessionReader(locator: s.locator, processes: processes),
                              trash: { url in
                                  if sw.fail { throw ProfileError.failed("trash failed") }
                                  j.steps.append("trash:\(url.lastPathComponent)")
                                  try FileManager.default.removeItem(at: url)
                              })
    }

    func signedIn(name: String = "work", service: String? = "Claude Code-credentials-abc") throws -> Profile {
        let p = try s.store.create(displayName: name.capitalized, commandName: name, color: "#000000")
        try s.signIn(p, uuid: "u-\(name)", email: "\(name)@e.com")
        try s.store.completeLogin(id: p.id, account: Account(accountUuid: "u-\(name)", email: "\(name)@e.com", organizationName: nil), keychainService: service)
        return try s.store.load().profile(id: p.id)!
    }

    func text(_ url: URL) -> String { (try? String(contentsOf: url, encoding: .utf8)) ?? "" }
}

private final class TrashSwitch: @unchecked Sendable { var fail = false }

@Suite struct ProfileRemoverTests {
    @Test func removesInOrderAndKeepsTheRest() throws {
        let w = try World(); defer { w.s.cleanup() }
        let p = try w.signedIn()
        let other = try w.signedIn(name: "other", service: "Claude Code-credentials-other")
        w.keys.names.insert("Claude Code-credentials-other")
        try w.s.write("x", to: ".claude-profiles/backups/\(p.id)/b/file")
        let r = try w.remover().remove(profile: p)
        #expect(r.warnings.isEmpty, "\(r.warnings)")
        #expect(w.journal.steps == ["auth logout", "keychain:Claude Code-credentials-abc", "trash:\(p.id)", "trash:\(p.id)"])
        #expect(w.claude.environments[0]?.set["CLAUDE_CONFIG_DIR"] == p.configDir, "logout runs on this profile only")
        let reg = try w.s.store.load()
        #expect(reg.profile(id: p.id) == nil)
        #expect(reg.profile(id: other.id) != nil)
        #expect(w.keys.names.contains(KeychainProbe.plainName))
        #expect(w.keys.names.contains("Claude Code-credentials-other"))
        #expect(!FileManager.default.fileExists(atPath: p.configDir!))
        #expect(!w.text(w.s.locator.namesFile).contains(p.id))
    }

    @Test func whenLogoutRemovedTheItemThereIsNothingToDelete() throws {
        let w = try World(); defer { w.s.cleanup() }
        let p = try w.signedIn()
        w.claude.onLogout = { w.keys.names.remove("Claude Code-credentials-abc") }
        let r = try w.remover().remove(profile: p)
        #expect(r.warnings.isEmpty)
        #expect(!w.journal.steps.contains { $0.hasPrefix("keychain:") })
    }

    @Test func theDefaultMovesBeforeAnythingElse() throws {
        let w = try World(); defer { w.s.cleanup() }
        let p = try w.signedIn()
        try w.s.store.setDefault(id: p.id)
        var seen = ""
        var namesAtLogout = ""
        w.claude.onLogout = {
            seen = w.text(w.s.locator.defaultFile)
            namesAtLogout = w.text(w.s.locator.namesFile)
        }
        _ = try w.remover().remove(profile: p)
        #expect(seen == "builtin\n", "the default moved before logout ran")
        #expect(!namesAtLogout.contains(p.id), "nothing can start in this folder during removal")
        #expect(w.text(w.s.locator.defaultFile) == "builtin\n")
    }

    @Test func theBuiltinProfileIsRefused() throws {
        let w = try World(); defer { w.s.cleanup() }
        let b = try w.s.store.defaultProfile()
        #expect(throws: ProfileError.self) { try w.remover().remove(profile: b) }
        #expect(w.journal.steps.isEmpty)
    }

    @Test func thePlainKeychainItemIsNeverDeleted() throws {
        let w = try World(); defer { w.s.cleanup() }
        let p = try w.signedIn(service: KeychainProbe.plainName)
        let r = try w.remover().remove(profile: p)
        #expect(w.keys.names.contains(KeychainProbe.plainName))
        #expect(r.warnings.contains { $0.contains("still there") })
    }

    @Test func aLiveSessionRefusesAndChangesNothing() throws {
        let w = try World(); defer { w.s.cleanup() }
        let p = try w.signedIn()
        try w.s.store.setDefault(id: p.id)
        let session = #"{"pid":5,"sessionId":"x","procStart":"Sat Oct 31 10:58:34 2026","entrypoint":"claude-desktop"}"#
        try Data(session.utf8).write(to: { () -> URL in
            let d = URL(fileURLWithPath: p.configDir!).appendingPathComponent("sessions")
            try! FileManager.default.createDirectory(at: d, withIntermediateDirectories: true)
            return d.appendingPathComponent("5.json")
        }())
        let r = try w.remover(processes: FakeProcesses(alive: [5], starts: [5: startDate]))
        #expect(throws: ProfileError.self) { try r.remove(profile: p) }
        #expect(w.journal.steps.isEmpty)
        #expect(try w.s.store.load().profile(id: p.id)?.state == .ready)
        #expect(w.text(w.s.locator.defaultFile) == p.id + "\n")
    }

    @Test func aFailedLogoutWarnsAndStillFinishes() throws {
        let w = try World(); defer { w.s.cleanup() }
        let p = try w.signedIn()
        w.claude.exitCode = 1
        let r = try w.remover().remove(profile: p)
        #expect(r.warnings.first?.contains("no network") == true)
        #expect(try w.s.store.load().profile(id: p.id) == nil)
    }

    @Test func aDeniedKeychainDeleteSaysWhatToDo() throws {
        let w = try World(); defer { w.s.cleanup() }
        let p = try w.signedIn()
        w.keys.denyDelete = true
        let r = try w.remover().remove(profile: p)
        #expect(r.warnings.first?.contains("Keychain Access") == true)
        #expect(try w.s.store.load().profile(id: p.id) == nil)
    }

    @Test func noRecordedNameIsAWarningNotAGuess() throws {
        let w = try World(); defer { w.s.cleanup() }
        let p = try w.signedIn(service: nil)
        let r = try w.remover().remove(profile: p)
        #expect(r.warnings.first?.contains("No Keychain item was recorded") == true)
        #expect(w.keys.names.contains("Claude Code-credentials-abc"), "an unrecorded name is never guessed at")
    }

    @Test func aFailureLeavesItDeletingAndResumeFinishes() throws {
        let w = try World(); defer { w.s.cleanup() }
        let p = try w.signedIn()
        let r = try w.remover()
        w.trashFails.fail = true
        #expect(throws: ProfileError.self) { try r.remove(profile: p) }
        let stuck = try w.s.store.load().profile(id: p.id)
        #expect(stuck?.state == .deleting)
        #expect(!w.text(w.s.locator.namesFile).contains(p.id), "it cannot start while half removed")

        w.trashFails.fail = false
        let done = try r.resumePending()
        #expect(done.map(\.displayName) == ["Work"])
        #expect(try w.s.store.load().profile(id: p.id) == nil)
        #expect(!FileManager.default.fileExists(atPath: p.configDir!))
        #expect(try r.resumePending().isEmpty)
    }

    @Test func resumeWorksIfTheFolderIsAlreadyGone() throws {
        let w = try World(); defer { w.s.cleanup() }
        let p = try w.signedIn()
        try w.s.store.markDeleting(id: p.id)
        try FileManager.default.removeItem(atPath: p.configDir!)
        _ = try w.remover().resumePending()
        #expect(try w.s.store.load().profile(id: p.id) == nil)
        #expect(!w.journal.steps.contains("auth logout"), "logout would recreate the folder")
        #expect(!FileManager.default.fileExists(atPath: p.configDir!))
    }

    @Test func resumeWaitsForALiveSession() throws {
        let w = try World(); defer { w.s.cleanup() }
        let p = try w.signedIn()
        try w.s.store.markDeleting(id: p.id)
        let dir = URL(fileURLWithPath: p.configDir!).appendingPathComponent("sessions")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        try Data(#"{"pid":5,"sessionId":"x","procStart":"Sat Oct 31 10:58:34 2026"}"#.utf8).write(to: dir.appendingPathComponent("5.json"))
        let r = try w.remover(processes: FakeProcesses(alive: [5], starts: [5: startDate]))
        #expect(try r.resumePending().isEmpty)
        #expect(try w.s.store.load().profile(id: p.id)?.state == .deleting)
    }

    @Test func reconcilerResumesARemoval() throws {
        let w = try World(); defer { w.s.cleanup() }
        let p = try w.signedIn()
        try w.s.store.markDeleting(id: p.id)
        let reconciler = Reconciler(store: w.s.store, login: LoginCoordinator(store: w.s.store, keychain: w.keys), remover: try w.remover())
        let r = try reconciler.run()
        #expect(r.removalsResumed == ["Work"])
        #expect(try w.s.store.load().profile(id: p.id) == nil)
    }

    @Test func uninstallRemovesEverythingButTheBuiltinProfile() throws {
        let w = try World(); defer { w.s.cleanup() }
        let a = try w.signedIn()
        let b = try w.signedIn(name: "other", service: nil)
        try w.s.write("settings", to: ".claude/settings.json")
        let before = w.text(w.s.home.appendingPathComponent(".claude.json"))
        let r = try w.remover().uninstall()
        #expect(r.removed.count == 2)
        #expect(!FileManager.default.fileExists(atPath: w.s.locator.profilesRoot.path))
        #expect(!FileManager.default.fileExists(atPath: a.configDir!) && !FileManager.default.fileExists(atPath: b.configDir!))
        #expect(w.text(w.s.home.appendingPathComponent(".claude/settings.json")) == "settings")
        #expect(w.text(w.s.home.appendingPathComponent(".claude.json")) == before)
        #expect(w.keys.names.contains(KeychainProbe.plainName))
        #expect(r.zshrcLine.contains("claude-profiles.zsh"))
    }

    @Test func uninstallRefusesBeforeChangingAnything() throws {
        let w = try World(); defer { w.s.cleanup() }
        let a = try w.signedIn()
        let b = try w.signedIn(name: "other")
        let dir = URL(fileURLWithPath: b.configDir!).appendingPathComponent("sessions")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        try Data(#"{"pid":5,"sessionId":"x","procStart":"Sat Oct 31 10:58:34 2026"}"#.utf8).write(to: dir.appendingPathComponent("5.json"))
        let r = try w.remover(processes: FakeProcesses(alive: [5], starts: [5: startDate]))
        #expect(throws: ProfileError.self) { try r.uninstall() }
        #expect(w.journal.steps.isEmpty)
        #expect(try w.s.store.load().profile(id: a.id)?.state == .ready)
    }

    /// The real Trash, with a throwaway folder, which the test then clears out of the Trash.
    @Test func realTrashWorks() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let dir = s.home.appendingPathComponent("cpm-trash-test-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let moved = try ProfileRemover.moveToTrash(dir)
        #expect(!FileManager.default.fileExists(atPath: dir.path))
        if let moved { try FileManager.default.removeItem(at: moved) }
    }
}
