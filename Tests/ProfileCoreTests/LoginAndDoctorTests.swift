import Foundation
import Security
import Testing
@testable import ProfileCore

@Suite struct KeychainProbeTests {
    /// Real Keychain, under a test prefix, with an item this process created.
    @Test func listExistsDeleteUnderTestPrefix() throws {
        let prefix = "cpm-test-credentials-\(UUID().uuidString)"
        let service = prefix + "-one"
        let add: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
                                  kSecAttrAccount as String: "cpm-test", kSecValueData as String: Data("x".utf8)]
        #expect(SecItemAdd(add as CFDictionary, nil) == errSecSuccess)
        let probe = KeychainProbe(prefix: prefix)
        #expect(probe.listServiceNames() == [service])
        #expect(probe.exists(service: service))
        try probe.delete(service: service)
        #expect(!probe.exists(service: service))
    }

    @Test func refusesThePlainNameAndForeignNames() {
        let probe = KeychainProbe()
        #expect(throws: ProfileError.self) { try probe.delete(service: KeychainProbe.plainName) }
        #expect(throws: ProfileError.self) { try probe.delete(service: "Some Other App") }
    }
}

@Suite struct LoginCoordinatorTests {
    private func coordinator(_ s: Sandbox, keychain: FakeKeychain, runner: FakeRunner = FakeRunner()) -> LoginCoordinator {
        let stubBin = ClaudeBinaryLocator(locator: s.locator, path: "/usr/bin")
        var c = LoginCoordinator(store: s.store, keychain: keychain, cli: ClaudeCLI(locator: s.locator, runner: runner, binary: stubBin))
        c.keychainWaitSeconds = 2
        return c
    }

    private func pending(_ s: Sandbox) throws -> Profile { try s.store.create(displayName: "A", commandName: "a", color: "#000000") }
    private func reload(_ s: Sandbox, _ id: String) throws -> Profile { try s.store.load().profile(id: id)! }

    @Test func beginReturnsTheCommandAndRecordsBaseline() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let k = FakeKeychain()
        let p = try pending(s)
        #expect(try coordinator(s, keychain: k).begin(profile: p) == "claude-as a auth login")
        let q = try reload(s, p.id)
        #expect(q.keychainBaseline == [KeychainProbe.plainName] && q.loginStartedAt != nil)
    }

    @Test func pollNeverCompletes() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let c = coordinator(s, keychain: FakeKeychain())
        let p = try pending(s)
        try c.begin(profile: p)
        #expect(try c.poll(profile: reload(s, p.id)) == .waiting)
        try s.signIn(p, uuid: "u")
        if case .detected(let a, _) = try c.poll(profile: reload(s, p.id)) { #expect(a.accountUuid == "u") } else { Issue.record("not detected") }
        #expect(try reload(s, p.id).state == .pendingLogin)
    }

    @Test func pollTimesOut() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        var c = coordinator(s, keychain: FakeKeychain())
        let p = try pending(s)
        try c.begin(profile: p)
        c.now = { Date().addingTimeInterval(LoginCoordinator.timeout + 5) }
        #expect(try c.poll(profile: reload(s, p.id)) == .timedOut)
    }

    @Test func keepRecordsTheOneNewName() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let k = FakeKeychain()
        let c = coordinator(s, keychain: k)
        let p = try pending(s)
        try c.begin(profile: p)
        try s.signIn(p, uuid: "u", email: "me@example.com")
        k.names.insert("Claude Code-credentials-abc")
        let r = try c.keep(profile: reload(s, p.id))
        #expect(r.keychainWarning == nil && r.account.email == "me@example.com")
        let q = try reload(s, p.id)
        #expect(q.state == .ready && q.keychainService == "Claude Code-credentials-abc")
        #expect(q.keychainBaseline == nil && q.loginStartedAt == nil)
    }

    @Test func keepFindsALateKeychainItem() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let k = FakeKeychain()
        let c = coordinator(s, keychain: k)
        let p = try pending(s)
        try c.begin(profile: p)
        try s.signIn(p, uuid: "u")
        k.sequence = [[KeychainProbe.plainName], [KeychainProbe.plainName, "Claude Code-credentials-late"]]
        #expect(try c.keep(profile: reload(s, p.id)).keychainWarning == nil)
        #expect(try reload(s, p.id).keychainService == "Claude Code-credentials-late")
    }

    @Test func zeroOrSeveralNewNamesStillWorkWithAWarning() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let k = FakeKeychain()
        let c = coordinator(s, keychain: k)
        let p = try pending(s)
        try c.begin(profile: p)
        try s.signIn(p, uuid: "u")
        k.names = [KeychainProbe.plainName, "Claude Code-credentials-a", "Claude Code-credentials-b"]
        let r = try c.keep(profile: reload(s, p.id))
        #expect(r.keychainWarning != nil)
        let q = try reload(s, p.id)
        #expect(q.state == .ready && q.keychainService == nil)
    }

    @Test func keepWithoutASignInIsRefused() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let c = coordinator(s, keychain: FakeKeychain())
        let p = try pending(s)
        try c.begin(profile: p)
        #expect(throws: ProfileError.self) { try c.keep(profile: reload(s, p.id)) }
    }

    @Test func retryLogsOutBeforeTakingTheBaseline() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let k = FakeKeychain([KeychainProbe.plainName, "Claude Code-credentials-x"])
        let runner = FakeRunner()
        runner.handler = { _, _ in
            k.names.remove("Claude Code-credentials-x")
            return ProcessResult()
        }
        let bin = s.home.appendingPathComponent("stub")
        try FileManager.default.createDirectory(at: bin, withIntermediateDirectories: true)
        try Data("#!/bin/sh\n".utf8).write(to: bin.appendingPathComponent("claude"))
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: bin.appendingPathComponent("claude").path)
        let withBin = LoginCoordinator(store: s.store, keychain: k, cli: ClaudeCLI(locator: s.locator, runner: runner, binary: ClaudeBinaryLocator(locator: s.locator, path: bin.path)))
        let p = try pending(s)
        try s.signIn(p, uuid: "u")
        try withBin.retry(profile: p)
        #expect(runner.calls.first?.1 == ["auth", "logout"])
        #expect(runner.calls.first?.2?.set["CLAUDE_CONFIG_DIR"] == p.configDir)
        #expect(try reload(s, p.id).keychainBaseline == [KeychainProbe.plainName])
    }
}

@Suite struct ReconcilerTests {
    private func reconciler(_ s: Sandbox) -> Reconciler {
        Reconciler(store: s.store, login: LoginCoordinator(store: s.store, keychain: FakeKeychain()))
    }

    @Test func orphanIsListedAndNotDeleted() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        _ = try s.store.load()
        try s.write("x", to: ".claude-profiles/profiles/orphan/file")
        let r = try reconciler(s).run()
        #expect(r.orphanFolders == ["orphan"])
        #expect(FileManager.default.fileExists(atPath: s.home.appendingPathComponent(".claude-profiles/profiles/orphan/file").path))
    }

    @Test func appFoldersAreNotOrphans() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        try s.write("x", to: ".claude-profiles/bin/x")
        try s.write("x", to: ".claude-profiles/logs/x")
        #expect(try reconciler(s).run().orphanFolders.isEmpty)
    }

    @Test func rowWithNoFolderBecomesError() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try FileManager.default.removeItem(atPath: p.configDir!)
        _ = try reconciler(s).run()
        #expect(try s.store.load().profile(id: p.id)?.state == .error)
        #expect(try !String(contentsOf: s.locator.namesFile, encoding: .utf8).contains("a\t\(p.id)"))
    }

    @Test func aDetectedLoginIsReportedAndNeverCompleted() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try s.signIn(p, uuid: "u", email: "me@example.com")
        let r = try reconciler(s).run()
        #expect(r.loginNotices.count == 1 && r.loginNotices[0].contains("me@example.com") && r.loginNotices[0].contains("cpm keep a"))
        #expect(try s.store.load().profile(id: p.id)?.state == .pendingLogin)
    }

    @Test func logoutAndLoginInsideAProfileAreNoticed() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try s.signIn(p, uuid: "1", email: "x@e.com")
        try s.store.completeLogin(id: p.id, account: Account(accountUuid: "1", email: "x@e.com", organizationName: nil), keychainService: nil)
        try s.signIn(p, uuid: "2", email: "y@e.com")
        #expect(try reconciler(s).run().accountNotices.first?.contains("y@e.com") == true)
        try FileManager.default.removeItem(at: s.locator.configFile(for: p))
        _ = try reconciler(s).run()
        #expect(try s.store.load().profile(id: p.id)?.state == .loggedOut)
        try s.signIn(p, uuid: "2")
        _ = try reconciler(s).run()
        #expect(try s.store.load().profile(id: p.id)?.state == .ready)
    }

    @Test func missingDefaultIsRepaired() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        _ = try s.store.load()
        try FileManager.default.removeItem(at: s.locator.defaultFile)
        _ = try reconciler(s).run()
        #expect(FileManager.default.fileExists(atPath: s.locator.defaultFile.path))
    }
}

@Suite struct DoctorTests {
    /// A login shell that reports the wrapper on PATH, and a real binary beside it.
    private func doctor(_ s: Sandbox, keychain: FakeKeychain = FakeKeychain(), wrapper: Bool = true) throws -> Doctor {
        let bin = s.home.appendingPathComponent("realbin")
        try FileManager.default.createDirectory(at: bin, withIntermediateDirectories: true)
        let real = bin.appendingPathComponent("claude")
        try Data("#!/bin/sh\n".utf8).write(to: real)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: real.path)
        try FileManager.default.createDirectory(at: s.locator.binDir, withIntermediateDirectories: true)
        let wrap = s.locator.binDir.appendingPathComponent("claude")
        try Data("#!/bin/sh\n".utf8).write(to: wrap)
        let runner = FakeRunner()
        let locator = s.locator
        runner.handler = { exe, args in
            if exe == real.path { return ProcessResult(stdout: "2.1.296 (Claude Code)\n") }
            if args.last?.contains("CPM_PATH_MARKER") == true {
                let w = wrapper ? wrap.path : real.path
                return ProcessResult(stdout: "noise\nCPM_PATH_MARKER\n\(locator.binDir.path):\(bin.path)\n\(w)\n")
            }
            return ProcessResult(stdout: "CPM_VARS_MARKER\n")
        }
        return Doctor(locator: s.locator, keychain: keychain, runner: runner)
    }

    @Test func cleanSetupPassesWithTheStrayFileAsANote() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        try s.write(#"{"oauthAccount":{"accountUuid":"u"}}"#, to: ".claude.json")
        try s.write("{}", to: ".claude/.claude.json")
        _ = try s.store.load()
        let r = try doctor(s).run()
        #expect(!r.hasFaults, "\(r.findings)")
        #expect(r.findings.contains { $0.level == .note && $0.message.contains(".claude/.claude.json") })
    }

    @Test func namesEachFault() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        try s.write(#"{"oauthAccount":{"accountUuid":"u"}}"#, to: ".claude.json")
        _ = try s.store.load()
        let d = try doctor(s)

        // Default file broken.
        try Data("nonsense\n".utf8).write(to: s.locator.defaultFile)
        #expect(d.run().findings.contains { $0.level == .fault && $0.fix == "cpm doctor --fix" })
        try s.store.repairFiles()

        // Added profile whose folder is missing.
        let p = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try FileManager.default.removeItem(atPath: p.configDir!)
        #expect(d.run().findings.contains { $0.level == .fault && $0.message.contains("folder is missing") })
    }

    @Test func wrapperNotFirstIsAFault() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        try s.write(#"{"oauthAccount":{"accountUuid":"u"}}"#, to: ".claude.json")
        _ = try s.store.load()
        #expect(try doctor(s, wrapper: false).run().findings.contains { $0.level == .fault && $0.message.contains("not the wrapper") })
    }

    @Test func missingKeychainLoginIsAFaultAndOrphansAreNotes() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        try s.write(#"{"oauthAccount":{"accountUuid":"u"}}"#, to: ".claude.json")
        let p = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try s.signIn(p, uuid: "v")
        try s.store.completeLogin(id: p.id, account: Account(accountUuid: "v", email: nil, organizationName: nil), keychainService: "Claude Code-credentials-gone")
        let r = try doctor(s, keychain: FakeKeychain([KeychainProbe.plainName, "Claude Code-credentials-other"])).run()
        #expect(r.findings.contains { $0.level == .fault && $0.message.contains("-gone") })
        #expect(r.findings.contains { $0.level == .note && $0.message.contains("-other") })
    }

    @Test func notesDoNotCountAsFaults() {
        #expect(!DoctorReport(findings: [Finding(level: .note, message: "x")]).hasFaults)
        #expect(DoctorReport(findings: [Finding(level: .fault, message: "x")]).hasFaults)
    }

    @Test func doctorChangesNothing() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        _ = try s.store.load()
        try Data("nonsense\n".utf8).write(to: s.locator.defaultFile)
        _ = try doctor(s).run()
        #expect(try String(contentsOf: s.locator.defaultFile, encoding: .utf8) == "nonsense\n")
    }
}
