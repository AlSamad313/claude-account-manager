import Foundation
import Testing
@testable import ProfileCore

/// A temporary home for one test. Aborts if a path would land under the real home.
struct Sandbox {
    let home: URL
    let locator: ConfigLocator
    let store: ProfileStore

    init() throws {
        let base = URL(fileURLWithPath: "/private/tmp").appendingPathComponent("cpm-test-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        let realHome = NSHomeDirectoryForUser(NSUserName()) ?? NSHomeDirectory()
        precondition(!base.path.hasPrefix(realHome), "Test home must not be under the real home")
        home = base
        locator = ConfigLocator(home: base)
        precondition(!locator.profilesRoot.path.hasPrefix(realHome), "Test paths must not be under the real home")
        // Tests never use the real Trash.
        store = ProfileStore(locator: locator, trash: { try FileManager.default.removeItem(at: $0) })
    }

    func write(_ text: String, to path: String) throws {
        let url = home.appendingPathComponent(path)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data(text.utf8).write(to: url)
    }

    func signIn(_ p: Profile, uuid: String, email: String = "a@example.com") throws {
        let json = #"{"oauthAccount":{"accountUuid":"\#(uuid)","emailAddress":"\#(email)","organizationName":"Org"},"mcpServers":{}}"#
        try Data(json.utf8).write(to: locator.configFile(for: p))
    }

    func cleanup() { try? FileManager.default.removeItem(at: home) }
}

final class FakeKeychain: KeychainNames {
    var names: Set<String>
    /// Each call to list takes the next set, so an item can appear late.
    var sequence: [Set<String>] = []
    init(_ names: Set<String> = [KeychainProbe.plainName]) { self.names = names }
    func listServiceNames() -> Set<String> {
        if !sequence.isEmpty { names = sequence.removeFirst() }
        return names
    }
}

final class FakeRunner: ProcessRunning {
    var calls: [(String, [String], LaunchEnv?)] = []
    var handler: (String, [String]) -> ProcessResult = { _, _ in ProcessResult() }
    func run(executable: String, args: [String], env: LaunchEnv?, timeout: TimeInterval) -> ProcessResult {
        calls.append((executable, args, env))
        return handler(executable, args)
    }
}

struct FakeProcesses: ProcessInfoProviding {
    var alive: Set<Int32> = []
    var starts: [Int32: Date] = [:]
    func isAlive(pid: Int32) -> Bool { alive.contains(pid) }
    func startTime(pid: Int32) -> Date? { starts[pid] }
    func signal(pid: Int32, _ sig: Int32) -> Bool { true }
}
