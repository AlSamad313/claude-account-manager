import Foundation

public protocol KeychainEditing: KeychainNames {
    func delete(service: String) throws
}

extension KeychainProbe: KeychainEditing {}

public struct RemovalResult: Equatable, Sendable {
    public var displayName: String
    public var warnings: [String] = []
}

public struct UninstallResult: Equatable, Sendable {
    public var removed: [RemovalResult]
    public var zshenvLine: String
    public var zshrcLine: String
}

/// Owns design flow 7 and uninstall. Every step is safe to repeat, so a crash at any point
/// continues from the `deleting` state at the next reconcile. The built-in profile is never touched.
public struct ProfileRemover {
    public let store: ProfileStore
    public let locator: ConfigLocator
    public let cli: ClaudeCLI
    public let keychain: KeychainEditing
    public let live: LiveSessionReader
    private let trash: (URL) throws -> Void

    public init(store: ProfileStore, cli: ClaudeCLI? = nil, keychain: KeychainEditing = KeychainProbe(),
                live: LiveSessionReader? = nil, trash: ((URL) throws -> Void)? = nil) {
        self.store = store
        self.locator = store.locator
        self.cli = cli ?? ClaudeCLI(locator: store.locator)
        self.keychain = keychain
        self.live = live ?? LiveSessionReader(locator: store.locator)
        self.trash = trash ?? { _ = try ProfileRemover.moveToTrash($0) }
    }

    /// Returns where it went, so a test can clean up after itself.
    @discardableResult
    public static func moveToTrash(_ url: URL) throws -> URL? {
        var result: NSURL?
        try FileManager.default.trashItem(at: url, resultingItemURL: &result)
        return result as URL?
    }

    public func remove(profile p: Profile) throws -> RemovalResult {
        guard !p.builtin else { throw ProfileError.notAllowed("The built-in profile cannot be removed.") }
        guard !live.hasLiveSession(p) else {
            throw ProfileError.notAllowed("\(p.displayName) has a live session. Stop it first.")
        }
        // Moves the default to the built-in profile first, and takes the row out of `names`,
        // so nothing can start in this folder from here on.
        try store.markDeleting(id: p.id)
        return try finish(p)
    }

    /// Continues any removal a crash interrupted. A profile that now has a live session waits.
    public func resumePending() throws -> [RemovalResult] {
        var out: [RemovalResult] = []
        for p in try store.load().profiles where p.state == .deleting && !p.builtin && !live.hasLiveSession(p) {
            out.append(try finish(p))
        }
        return out
    }

    private func finish(_ p: Profile) throws -> RemovalResult {
        var result = RemovalResult(displayName: p.displayName)
        let fm = FileManager.default
        let folder = locator.configDir(for: p)

        // Logout removes the profile's Keychain item (S0 check 6), so it comes first.
        if fm.fileExists(atPath: folder.path), AccountReader.readAccount(configFile: locator.configFile(for: p)) != nil {
            let r = cli.logout(profile: p)
            if r.exitCode != 0 {
                result.warnings.append("Could not sign \(p.displayName) out: \(r.stderr.trimmingCharacters(in: .whitespacesAndNewlines))")
            }
        }

        // Only the name recorded at login, never a calculated one. Refused for the plain name.
        if let service = p.keychainService, keychain.listServiceNames().contains(service) {
            do {
                try keychain.delete(service: service)
            } catch {
                result.warnings.append("Keychain item \(service) is still there. To remove it, open Keychain Access and delete that item.")
            }
        }
        if p.keychainService == nil, p.accountUuid != nil {
            result.warnings.append("No Keychain item was recorded for \(p.displayName). `cpm doctor` lists any item left behind.")
        }

        let backups = locator.profilesRoot.appendingPathComponent("backups/\(p.id)")
        if fm.fileExists(atPath: backups.path) { try trash(backups) }
        if fm.fileExists(atPath: folder.path) { try trash(folder) }

        try store.removeRow(id: p.id)
        locator.logEvent(kind: "delete", profileId: p.id, detail: "removed \(p.commandName)")
        return result
    }

    /// Refuses while any added profile has a live session, before anything is changed.
    /// ~/.claude and ~/.claude.json are not touched, and the shell lines are the user's to remove.
    public func uninstall() throws -> UninstallResult {
        let added = try store.load().profiles.filter { !$0.builtin }
        if let busy = added.first(where: { live.hasLiveSession($0) }) {
            throw ProfileError.notAllowed("\(busy.displayName) has a live session. Stop it first. Nothing was changed.")
        }
        var removed: [RemovalResult] = []
        for p in added { removed.append(try remove(profile: p)) }
        let root = locator.profilesRoot
        if FileManager.default.fileExists(atPath: root.path) { try trash(root) }
        return UninstallResult(removed: removed, zshenvLine: ShellInstaller.zshenvLine, zshrcLine: ShellInstaller.zshrcLine)
    }
}
