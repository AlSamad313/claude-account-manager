import Foundation

public enum ProfileState: String, Codable, Sendable {
    case pendingLogin, ready, loggedOut, deleting, error
}

public struct Profile: Codable, Identifiable, Equatable, Sendable {
    public static let builtinID = "builtin"

    public var id: String
    public var commandName: String   // fixed at creation
    public var displayName: String
    public var color: String         // "#RRGGBB"
    public var builtin: Bool
    public var configDir: String?    // set once at creation, never rebuilt; nil for built-in
    public var createdAt: Date
    public var accountUuid: String?
    public var email: String?
    public var organizationName: String?
    public var keychainBaseline: [String]?  // only while waiting for a login
    public var loginStartedAt: Date?        // only while waiting; the timeout runs from here
    public var keychainService: String?     // recorded at first login; nil for built-in
    public var state: ProfileState
    public var lastError: String?

    /// `names` lists only profiles the wrapper may start in.
    public var isInNames: Bool { state == .pendingLogin || state == .ready || state == .loggedOut }
}

public struct Registry: Codable, Equatable, Sendable {
    public var schemaVersion: Int
    public var defaultProfileId: String
    public var profiles: [Profile]

    public func profile(id: String) -> Profile? { profiles.first { $0.id == id } }
    public func profile(commandName: String) -> Profile? { profiles.first { $0.commandName == commandName } }
}

public struct Account: Equatable, Sendable {
    public var accountUuid: String
    public var email: String?
    public var organizationName: String?

    public init(accountUuid: String, email: String?, organizationName: String?) {
        self.accountUuid = accountUuid
        self.email = email
        self.organizationName = organizationName
    }
}

public enum ProfileError: Error, Equatable, CustomStringConvertible {
    case invalidCommandName(String)
    case invalidDisplayName(String)
    case invalidColor(String)
    case commandNameTaken(String)
    case notFound(String)
    case notAllowed(String)
    case failed(String)

    public var description: String {
        switch self {
        case .invalidCommandName(let n): return "'\(n)' is not a valid command name. Use 1 to 24 lowercase letters, digits or hyphens."
        case .invalidDisplayName(let n): return "'\(n)' is not a valid display name. Use 1 to 40 characters with no tabs, newlines or control characters."
        case .invalidColor(let c): return "'\(c)' is not a valid colour. Use #RRGGBB."
        case .commandNameTaken(let n): return "A profile already uses the command name '\(n)'."
        case .notFound(let n): return "No profile named '\(n)'."
        case .notAllowed(let m), .failed(let m): return m
        }
    }
}

public enum Validate {
    public static func commandName(_ s: String) throws {
        let ok = s.wholeMatch(of: #/[a-z0-9-]{1,24}/#) != nil && s != Profile.builtinID
        if !ok { throw ProfileError.invalidCommandName(s) }
    }

    public static func displayName(_ s: String) throws {
        let hasControl = s.unicodeScalars.contains { $0.properties.generalCategory == .control || $0.properties.generalCategory == .format }
        if s.isEmpty || s.count > 40 || hasControl { throw ProfileError.invalidDisplayName(s) }
    }

    public static func color(_ s: String) throws {
        if s.wholeMatch(of: ##/#[0-9A-Fa-f]{6}/##) == nil { throw ProfileError.invalidColor(s) }
    }
}
