import Foundation

public struct ReconcileResult: Equatable, Sendable {
    public var orphanFolders: [String] = []
    public var rowsWithoutFolder: [String] = []
    public var loginNotices: [String] = []
    public var accountNotices: [String] = []
    public var removalsResumed: [String] = []
}

/// Runs when the app or cpm starts. Dependencies run one way: Reconciler uses LoginCoordinator,
/// ProfileRemover and ProfileStore; ProfileStore uses none of them. It never completes a login.
public struct Reconciler {
    public let store: ProfileStore
    public let login: LoginCoordinator
    public let remover: ProfileRemover?

    public init(store: ProfileStore, login: LoginCoordinator, remover: ProfileRemover? = nil) {
        self.store = store
        self.login = login
        self.remover = remover
    }

    public func run() throws -> ReconcileResult {
        var result = ReconcileResult()
        try store.repairFiles()
        if let remover { result.removalsResumed = try remover.resumePending().map(\.displayName) }
        let registry = try store.load()
        let locator = store.locator
        let fm = FileManager.default

        let known = Set(registry.profiles.compactMap(\.configDir).map { ($0 as NSString).lastPathComponent })
        let folders = (try? fm.contentsOfDirectory(atPath: locator.profilesDir.path)) ?? []
        result.orphanFolders = folders.filter { !$0.hasPrefix(".") && !known.contains($0) }.sorted()

        for p in registry.profiles {
            if !p.builtin, !fm.fileExists(atPath: locator.configDir(for: p).path) {
                if p.state != .error && p.state != .deleting {
                    try store.markError(id: p.id, reason: "Its folder is missing.")
                }
                result.rowsWithoutFolder.append(p.displayName)
                continue
            }
            let account = AccountReader.readAccount(configFile: locator.configFile(for: p))
            switch (p.state, account) {
            case (.pendingLogin, .some):
                if case .detected(let a, _) = try login.poll(profile: p) {
                    result.loginNotices.append("\(p.displayName) signed in as \(a.email ?? "an account"). Run: cpm keep \(p.commandName), cpm retry \(p.commandName) or cpm discard \(p.commandName)")
                }
            case (.ready, .none):
                try store.markLoggedOut(id: p.id)
            case (.loggedOut, .some(let a)):
                try store.markReady(id: p.id, account: a)
            case (.error, .some(let a)):  // its folder came back
                try store.markReady(id: p.id, account: a)
            case (.error, .none):
                try store.markLoggedOut(id: p.id)
            case (.ready, .some(let a)):
                if case .changed(let from, let to) = try store.updateAccount(id: p.id, account: a) {
                    result.accountNotices.append("\(p.displayName) changed account, from \(from ?? "unknown") to \(to ?? "unknown").")
                }
            default:
                break
            }
        }
        return result
    }
}
