import Foundation

public enum LoginState: Equatable, Sendable {
    case waiting
    case detected(Account, duplicateOf: String?)
    case timedOut
}

public struct KeepResult: Equatable, Sendable {
    public var account: Account
    public var duplicateOf: String?
    public var keychainWarning: String?
}

/// Owns flow 3. Nothing completes a login except keep().
public struct LoginCoordinator {
    public static let timeout: TimeInterval = 600

    public let store: ProfileStore
    public let keychain: KeychainNames
    public let cli: ClaudeCLI
    public var now: () -> Date = Date.init
    public var keychainWaitSeconds: Int = 10

    public init(store: ProfileStore, keychain: KeychainNames = KeychainProbe(), cli: ClaudeCLI? = nil) {
        self.store = store
        self.keychain = keychain
        self.cli = cli ?? ClaudeCLI(locator: store.locator)
    }

    /// Records which Keychain names exist now, and returns the command to sign in with.
    @discardableResult
    public func begin(profile: Profile) throws -> String {
        try store.setKeychainBaseline(id: profile.id, names: keychain.listServiceNames().sorted(), startedAt: now())
        return "claude-as \(profile.commandName) auth login"
    }

    /// Reads only.
    public func poll(profile: Profile) throws -> LoginState {
        if let account = AccountReader.readAccount(configFile: store.locator.configFile(for: profile)) {
            let r = try store.load()
            let dup = r.profiles.first { $0.id != profile.id && $0.accountUuid == account.accountUuid }?.id
            return .detected(account, duplicateOf: dup)
        }
        if let started = profile.loginStartedAt, now().timeIntervalSince(started) > Self.timeout { return .timedOut }
        return .waiting
    }

    public func keep(profile: Profile) throws -> KeepResult {
        guard profile.state == .pendingLogin else {
            throw ProfileError.notAllowed("\(profile.displayName) is not waiting for a login.")
        }
        guard let account = AccountReader.readAccount(configFile: store.locator.configFile(for: profile)) else {
            throw ProfileError.notAllowed("\(profile.displayName) has not signed in yet. Run: claude-as \(profile.commandName) auth login")
        }
        let baseline = Set(profile.keychainBaseline ?? [])
        var new = keychain.listServiceNames().subtracting(baseline)
        var waited = 0
        while new.isEmpty, waited < keychainWaitSeconds {
            // The item may be written after .claude.json.
            Thread.sleep(forTimeInterval: 1)
            waited += 1
            new = keychain.listServiceNames().subtracting(baseline)
        }
        let service = new.count == 1 ? new.first : nil
        let result = try store.completeLogin(id: profile.id, account: account, keychainService: service)
        store.locator.logEvent(kind: "login", profileId: profile.id, detail: "login kept")
        let warning = service == nil
            ? "Could not tell which Keychain item is this login (\(new.count) new). The login works; removing this profile may need a manual Keychain step."
            : nil
        return KeepResult(account: account, duplicateOf: result.duplicateOf, keychainWarning: warning)
    }

    /// Logs out first if a login exists, because logout removes the item and the next login
    /// recreates the same name, which a baseline taken before logout would not see as new.
    @discardableResult
    public func retry(profile: Profile) throws -> String {
        if AccountReader.readAccount(configFile: store.locator.configFile(for: profile)) != nil {
            let r = cli.logout(profile: profile)
            guard r.exitCode == 0 else { throw ProfileError.failed("Could not sign out: \(r.stderr)") }
        }
        return try begin(profile: profile)
    }

    /// Also signs out, so no Keychain item is left behind.
    public func discard(profile: Profile) throws {
        if LiveSessionReader(locator: store.locator).hasLiveSession(profile) {
            throw ProfileError.notAllowed("\(profile.displayName) has a live session. Stop it first.")
        }
        if AccountReader.readAccount(configFile: store.locator.configFile(for: profile)) != nil {
            let r = cli.logout(profile: profile)
            guard r.exitCode == 0 else { throw ProfileError.failed("Could not sign out: \(r.stderr)") }
        }
        try store.discard(id: profile.id)
    }
}

extension ConfigLocator {
    func logEvent(kind: String, profileId: String?, detail: String) {
        EventLog(locator: self).write(kind: kind, profileId: profileId, detail: detail)
    }
}
