import Foundation
import Testing
@testable import ProfileCore

@Suite struct ProfileStoreTests {
    private func text(_ url: URL) throws -> String { try String(contentsOf: url, encoding: .utf8) }
    private func mode(_ url: URL) throws -> Int { try FileManager.default.attributesOfItem(atPath: url.path)[.posixPermissions] as! Int }

    @Test func seedRowUsesOrganizationNameWhenPresent() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        try s.write(#"{"oauthAccount":{"accountUuid":"u1","emailAddress":"a@example.com","organizationName":"Acme"}}"#, to: ".claude.json")
        let p = try s.store.defaultProfile()
        #expect(p.id == "builtin" && p.commandName == "main" && p.displayName == "Acme" && p.color == "#2F6FDE")
        #expect(p.state == .ready && p.configDir == nil)
    }

    @Test func seedRowFallsBackToMain() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.defaultProfile()
        #expect(p.displayName == "Main" && p.state == .loggedOut)
    }

    @Test func seedNeverWritesToBuiltinFolder() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        _ = try s.store.create(displayName: "P", commandName: "p", color: "#000000")
        #expect(!FileManager.default.fileExists(atPath: s.locator.builtinDir.path))
    }

    @Test func createWritesFilesInOrderWithModes() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.create(displayName: "Personal", commandName: "personal", color: "#E8772E")
        #expect(p.state == .pendingLogin)
        #expect(try text(s.locator.namesFile).contains("personal\t\(p.id)\tPersonal\t#E8772E\t\(p.configDir!)\n"))
        #expect(try text(s.locator.namesFile).contains("main\tbuiltin\t"))
        #expect(try text(s.locator.defaultFile) == "builtin\n")
        for f in [s.locator.registryFile, s.locator.namesFile, s.locator.defaultFile] { #expect(try mode(f) == 0o600) }
        #expect(try mode(URL(fileURLWithPath: p.configDir!)) == 0o700)
        let settings = try text(URL(fileURLWithPath: p.configDir!).appendingPathComponent("settings.json"))
        #expect(settings.contains("profile-label"))
        #expect(settings.contains(p.id))
    }

    @Test func renameChangesOnlyTheDisplayName() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try s.store.rename(id: p.id, displayName: "B")
        let q = try s.store.load().profile(id: p.id)!
        #expect(q.displayName == "B" && q.commandName == "a" && q.configDir == p.configDir)
        #expect(try text(s.locator.namesFile).contains("a\t\(p.id)\tB\t"))
    }

    @Test func hostileNamesAreRefused() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        for bad in ["a b", "A", "", "builtin", "$(x)", "a/b", String(repeating: "a", count: 25)] {
            #expect(throws: ProfileError.self) { try s.store.create(displayName: "X", commandName: bad, color: "#000000") }
        }
        for bad in ["", "a\tb", "a\nb", "a\u{1B}]0;x", String(repeating: "a", count: 41)] {
            #expect(throws: ProfileError.self) { try s.store.create(displayName: bad, commandName: "ok", color: "#000000") }
        }
        for bad in ["red", "#12345", "#GGGGGG", "#123456;x"] {
            #expect(throws: ProfileError.self) { try s.store.create(displayName: "X", commandName: "ok", color: bad) }
        }
    }

    @Test func commandNamesAreUnique() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        _ = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        #expect(throws: ProfileError.commandNameTaken("a")) { try s.store.create(displayName: "B", commandName: "a", color: "#000000") }
    }

    @Test func setDefaultRewritesDefaultFile() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try s.store.setDefault(id: p.id)
        #expect(try text(s.locator.defaultFile) == p.id + "\n")
        #expect(try s.store.defaultProfile().id == p.id)
    }

    @Test func repairRewritesBrokenFiles() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try s.store.setDefault(id: p.id)
        try Data("nonsense\n".utf8).write(to: s.locator.defaultFile)
        try FileManager.default.removeItem(at: s.locator.namesFile)
        try s.store.repairFiles()
        #expect(try text(s.locator.defaultFile) == p.id + "\n")
        #expect(try text(s.locator.namesFile).contains("a\t\(p.id)\t"))
    }

    @Test func anUnchangedFileIsNotRewritten() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        _ = try s.store.load()
        let old = Date().addingTimeInterval(-500)
        for f in [s.locator.registryFile, s.locator.namesFile, s.locator.defaultFile] {
            try FileManager.default.setAttributes([.modificationDate: old], ofItemAtPath: f.path)
        }
        try s.store.repairFiles()
        for f in [s.locator.registryFile, s.locator.namesFile, s.locator.defaultFile] {
            let m = try FileManager.default.attributesOfItem(atPath: f.path)[.modificationDate] as! Date
            #expect(abs(m.timeIntervalSince(old)) < 2)
        }
    }

    @Test func unreadableRegistryIsNotOverwritten() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        _ = try s.store.load()
        try Data("{broken".utf8).write(to: s.locator.registryFile)
        #expect(throws: ProfileError.self) { try s.store.load() }
        #expect(try text(s.locator.registryFile) == "{broken")
    }

    @Test func aStaleProcessDoesNotOverwriteANewerChange() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let other = ProfileStore(locator: s.locator, trash: { try FileManager.default.removeItem(at: $0) })
        _ = try s.store.load()
        let a = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        let b = try other.create(displayName: "B", commandName: "b", color: "#000000")
        try s.store.rename(id: a.id, displayName: "A2")
        let r = try other.load()
        #expect(r.profile(id: a.id)?.displayName == "A2")
        #expect(r.profile(id: b.id) != nil)
    }

    @Test func concurrentChangesAreAllKept() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        _ = try s.store.load()
        DispatchQueue.concurrentPerform(iterations: 20) { i in
            let store = ProfileStore(locator: s.locator)
            _ = try? store.create(displayName: "P\(i)", commandName: "p\(i)", color: "#000000")
        }
        #expect(try s.store.load().profiles.count == 21)
    }

    @Test func builtinRowIsProtected() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        _ = try s.store.load()
        #expect(throws: ProfileError.self) { try s.store.removeRow(id: "builtin") }
        #expect(throws: ProfileError.self) { try s.store.markDeleting(id: "builtin") }
    }

    @Test func statesControlNames() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        #expect(try text(s.locator.namesFile).contains("a\t\(p.id)"))
        try s.store.markDeleting(id: p.id)
        #expect(try !text(s.locator.namesFile).contains("a\t\(p.id)"))
    }

    @Test func markDeletingMovesTheDefaultFirst() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try s.store.setDefault(id: p.id)
        try s.store.markDeleting(id: p.id)
        #expect(try text(s.locator.defaultFile) == "builtin\n")
    }

    @Test func discardOnlyWhilePending() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try s.signIn(p, uuid: "u")
        try s.store.completeLogin(id: p.id, account: Account(accountUuid: "u", email: nil, organizationName: nil), keychainService: nil)
        #expect(throws: ProfileError.self) { try s.store.discard(id: p.id) }
        let q = try s.store.create(displayName: "B", commandName: "b", color: "#000000")
        try s.store.discard(id: q.id)
        #expect(try s.store.load().profile(id: q.id) == nil)
        #expect(!FileManager.default.fileExists(atPath: q.configDir!))
    }

    @Test func duplicateAccountIsReported() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let a = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        let b = try s.store.create(displayName: "B", commandName: "b", color: "#000000")
        let acc = Account(accountUuid: "same", email: nil, organizationName: nil)
        #expect(try s.store.completeLogin(id: a.id, account: acc, keychainService: nil).duplicateOf == nil)
        #expect(try s.store.completeLogin(id: b.id, account: acc, keychainService: nil).duplicateOf == a.id)
    }

    @Test func accountChangeIsNoticed() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let a = try s.store.create(displayName: "A", commandName: "a", color: "#000000")
        try s.store.completeLogin(id: a.id, account: Account(accountUuid: "1", email: "x@e.com", organizationName: nil), keychainService: nil)
        #expect(try s.store.updateAccount(id: a.id, account: Account(accountUuid: "1", email: "x@e.com", organizationName: nil)) == .unchanged)
        #expect(try s.store.updateAccount(id: a.id, account: Account(accountUuid: "2", email: "y@e.com", organizationName: nil)) == .changed(from: "x@e.com", to: "y@e.com"))
    }
}
