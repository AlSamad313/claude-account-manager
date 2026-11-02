import Foundation
import Security

public protocol KeychainNames {
    func listServiceNames() -> Set<String>
}

/// Keychain item names only. Never asks for secret data.
public struct KeychainProbe: KeychainNames {
    public static let plainName = "Claude Code-credentials"
    public let prefix: String

    public init(prefix: String = KeychainProbe.plainName) { self.prefix = prefix }

    public func listServiceNames() -> Set<String> {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecReturnAttributes as String: true,
            kSecMatchLimit as String: kSecMatchLimitAll,
        ]
        var result: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
              let items = result as? [[String: Any]] else { return [] }
        return Set(items.compactMap { $0[kSecAttrService as String] as? String }.filter { $0.hasPrefix(prefix) })
    }

    public func exists(service: String) -> Bool { listServiceNames().contains(service) }

    /// Called only when removing a profile. The plain name is the built-in login and is refused.
    public func delete(service: String) throws {
        guard service != Self.plainName else {
            throw ProfileError.notAllowed("The built-in login is never deleted.")
        }
        guard service.hasPrefix(prefix) else {
            throw ProfileError.notAllowed("'\(service)' is not one of this tool's items.")
        }
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service]
        let status = SecItemDelete(query as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw ProfileError.failed("Could not delete Keychain item \(service) (status \(status)).")
        }
    }
}
