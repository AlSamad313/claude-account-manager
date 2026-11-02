import Foundation

public struct LoginResult: Equatable, Sendable {
    public var duplicateOf: String?  // another profile's id, if it uses the same account
}

public enum AccountChange: Equatable, Sendable {
    case unchanged
    case changed(from: String?, to: String?)
}

/// The only module that writes profiles.json, default and names. The app and cpm are
/// separate processes, so each change holds a lock and re-reads from disk first.
public final class ProfileStore {
    public let locator: ConfigLocator
    private let trash: (URL) throws -> Void
    private let log: EventLog
    private let statusLine: StatusLineWriter

    public init(locator: ConfigLocator, trash: ((URL) throws -> Void)? = nil) {
        self.locator = locator
        self.trash = trash ?? { try FileManager.default.trashItem(at: $0, resultingItemURL: nil) }
        self.log = EventLog(locator: locator)
        self.statusLine = StatusLineWriter(locator: locator)
    }

    // MARK: Reading

    public func load() throws -> Registry {
        try withLock { try self.readOrSeed() }
    }

    public func defaultProfile() throws -> Profile {
        let r = try load()
        guard let p = r.profile(id: r.defaultProfileId) ?? r.profile(id: Profile.builtinID) else {
            throw ProfileError.notFound(r.defaultProfileId)
        }
        return p
    }

    // MARK: Changes

    /// Rewrites default and names from profiles.json, the source of truth.
    public func repairFiles() throws {
        try withLock {
            let r = try self.readOrSeed()
            try self.writeTextFiles(r)
        }
    }

    public func create(displayName: String, commandName: String, color: String) throws -> Profile {
        try Validate.displayName(displayName)
        try Validate.commandName(commandName)
        try Validate.color(color)
        return try change { r in
            if r.profile(commandName: commandName) != nil { throw ProfileError.commandNameTaken(commandName) }
            let id = Self.newID(existing: r.profiles.map(\.id))
            let dir = self.locator.newConfigDir(id: id)
            try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
            let p = Profile(id: id, commandName: commandName, displayName: displayName, color: color, builtin: false,
                            configDir: dir, createdAt: Date(), state: .pendingLogin)
            try self.statusLine.write(for: p)
            r.profiles.append(p)
            return p
        }
    }

    public func setKeychainBaseline(id: String, names: [String], startedAt: Date) throws {
        try update(id) { $0.keychainBaseline = names; $0.loginStartedAt = startedAt }
    }

    public func clearKeychainBaseline(id: String) throws {
        try update(id) { $0.keychainBaseline = nil; $0.loginStartedAt = nil }
    }

    @discardableResult
    public func completeLogin(id: String, account: Account, keychainService: String?) throws -> LoginResult {
        try change { r in
            guard let i = r.profiles.firstIndex(where: { $0.id == id }) else { throw ProfileError.notFound(id) }
            let dup = r.profiles.first { $0.id != id && $0.accountUuid == account.accountUuid }?.id
            Self.apply(account, to: &r.profiles[i])
            r.profiles[i].keychainService = keychainService
            r.profiles[i].keychainBaseline = nil
            r.profiles[i].loginStartedAt = nil
            r.profiles[i].state = .ready
            r.profiles[i].lastError = nil
            return LoginResult(duplicateOf: dup)
        }
    }

    /// pendingLogin only. The folder goes to the Trash.
    public func discard(id: String) throws {
        let dir: URL = try change { r in
            guard let i = r.profiles.firstIndex(where: { $0.id == id }) else { throw ProfileError.notFound(id) }
            guard r.profiles[i].state == .pendingLogin else { throw ProfileError.notAllowed("Only a profile waiting for a login can be discarded.") }
            let dir = self.locator.configDir(for: r.profiles[i])
            r.profiles.remove(at: i)
            return dir
        }
        if FileManager.default.fileExists(atPath: dir.path) { try trash(dir) }
    }

    public func markLoggedOut(id: String) throws { try update(id) { $0.state = .loggedOut } }

    public func markReady(id: String, account: Account) throws {
        try update(id) { Self.apply(account, to: &$0); $0.state = .ready; $0.lastError = nil }
    }

    public func markError(id: String, reason: String) throws {
        try update(id) { $0.state = .error; $0.lastError = reason }
    }

    public func markDeleting(id: String) throws {
        try change { r in
            guard let i = r.profiles.firstIndex(where: { $0.id == id }) else { throw ProfileError.notFound(id) }
            guard !r.profiles[i].builtin else { throw ProfileError.notAllowed("The built-in profile cannot be removed.") }
            if r.defaultProfileId == id { r.defaultProfileId = Profile.builtinID }
            r.profiles[i].state = .deleting
        }
    }

    @discardableResult
    public func updateAccount(id: String, account: Account) throws -> AccountChange {
        try change { r in
            guard let i = r.profiles.firstIndex(where: { $0.id == id }) else { throw ProfileError.notFound(id) }
            let old = r.profiles[i].accountUuid
            let oldEmail = r.profiles[i].email
            Self.apply(account, to: &r.profiles[i])
            if let old, old != account.accountUuid { return .changed(from: oldEmail, to: account.email) }
            return .unchanged
        }
    }

    public func rename(id: String, displayName: String) throws {
        try Validate.displayName(displayName)
        try update(id) { $0.displayName = displayName }
    }

    public func setColor(id: String, color: String) throws {
        try Validate.color(color)
        try update(id) { $0.color = color }
    }

    public func setDefault(id: String) throws {
        try change { r in
            guard let p = r.profile(id: id) else { throw ProfileError.notFound(id) }
            guard p.state == .ready || p.state == .loggedOut || p.state == .pendingLogin else {
                throw ProfileError.notAllowed("\(p.displayName) cannot be the default while its state is \(p.state.rawValue).")
            }
            r.defaultProfileId = id
        }
        log.write(kind: "default", profileId: id, detail: "default changed")
    }

    public func removeRow(id: String) throws {
        try change { r in
            guard let p = r.profile(id: id) else { return }
            guard !p.builtin else { throw ProfileError.notAllowed("The built-in profile cannot be removed.") }
            if r.defaultProfileId == id { r.defaultProfileId = Profile.builtinID }
            r.profiles.removeAll { $0.id == id }
        }
    }

    // MARK: Internals

    private static func apply(_ a: Account, to p: inout Profile) {
        p.accountUuid = a.accountUuid
        p.email = a.email
        p.organizationName = a.organizationName
    }

    private func update(_ id: String, _ body: @escaping (inout Profile) -> Void) throws {
        try change { r in
            guard let i = r.profiles.firstIndex(where: { $0.id == id }) else { throw ProfileError.notFound(id) }
            body(&r.profiles[i])
        }
    }

    /// Lock, re-read, change, then write profiles.json, names, default, in that order, so
    /// default never names an ID that names does not hold yet.
    @discardableResult
    private func change<T>(_ body: (inout Registry) throws -> T) throws -> T {
        try withLock {
            var r = try self.readOrSeed()
            let result = try body(&r)
            try self.writeAll(r)
            return result
        }
    }

    private func withLock<T>(_ body: () throws -> T) throws -> T {
        let fm = FileManager.default
        try fm.createDirectory(at: locator.profilesRoot, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        let fd = open(locator.lockFile.path, O_RDWR | O_CREAT, 0o600)
        guard fd >= 0 else { throw ProfileError.failed("Cannot open \(locator.lockFile.path)") }
        defer { close(fd) }
        guard flock(fd, LOCK_EX) == 0 else { throw ProfileError.failed("Cannot lock \(locator.lockFile.path)") }
        defer { flock(fd, LOCK_UN) }
        return try body()
    }

    private func readOrSeed() throws -> Registry {
        if let data = try? Data(contentsOf: locator.registryFile) {
            do {
                return try Self.decoder.decode(Registry.self, from: data)
            } catch {
                throw ProfileError.failed("\(locator.registryFile.path) is not readable: \(error.localizedDescription). It was left unchanged.")
            }
        }
        let account = AccountReader.readAccount(configFile: locator.home.appendingPathComponent(".claude.json"))
        let name = account?.organizationName.flatMap { (try? Validate.displayName($0)) != nil ? $0 : nil } ?? "Main"
        let seed = Profile(id: Profile.builtinID, commandName: "main", displayName: name, color: "#2F6FDE", builtin: true,
                           configDir: nil, createdAt: Date(), accountUuid: account?.accountUuid, email: account?.email,
                           organizationName: account?.organizationName, state: account == nil ? .loggedOut : .ready)
        let r = Registry(schemaVersion: 1, defaultProfileId: Profile.builtinID, profiles: [seed])
        try writeAll(r)
        return r
    }

    private func writeAll(_ r: Registry) throws {
        try writeIfChanged(try Self.encoder.encode(r), to: locator.registryFile)
        try writeTextFiles(r)
    }

    /// A rewrite with the same bytes would wake the app's file watcher for nothing.
    private func writeIfChanged(_ data: Data, to url: URL) throws {
        if let old = try? Data(contentsOf: url), old == data { return }
        try AtomicFile.write(data, to: url, mode: 0o600)
    }

    private func writeTextFiles(_ r: Registry) throws {
        let lines = r.profiles.filter(\.isInNames).map { p in
            [p.commandName, p.id, p.displayName, p.color, p.configDir ?? "-"].joined(separator: "\t")
        }
        try writeIfChanged(Data((lines.joined(separator: "\n") + "\n").utf8), to: locator.namesFile)
        try writeIfChanged(Data((r.defaultProfileId + "\n").utf8), to: locator.defaultFile)
    }

    private static func newID(existing: [String]) -> String {
        let alphabet = Array("abcdefghijklmnopqrstuvwxyz234567")
        while true {
            let id = String((0..<8).map { _ in alphabet.randomElement()! })
            if !existing.contains(id) { return id }
        }
    }

    private static let encoder: JSONEncoder = {
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .sortedKeys]
        e.dateEncodingStrategy = .iso8601
        return e
    }()

    private static let decoder: JSONDecoder = {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .iso8601
        return d
    }()
}

extension Profile {
    init(id: String, commandName: String, displayName: String, color: String, builtin: Bool, configDir: String?,
         createdAt: Date, accountUuid: String? = nil, email: String? = nil, organizationName: String? = nil, state: ProfileState) {
        self.init(id: id, commandName: commandName, displayName: displayName, color: color, builtin: builtin,
                  configDir: configDir, createdAt: createdAt, accountUuid: accountUuid, email: email,
                  organizationName: organizationName, keychainBaseline: nil, loginStartedAt: nil,
                  keychainService: nil, state: state, lastError: nil)
    }
}
