import Foundation

/// v1 writes one Claude Code file format directly: settings.json during copy setup.
public enum CompatCheck {
    /// True if the target's settings.json can be merged into. A missing key is not a fault,
    /// because a new profile's file holds only statusLine.
    public static func canWriteSettings(file: URL) -> Bool {
        guard FileManager.default.fileExists(atPath: file.path) else { return true }
        guard let data = try? Data(contentsOf: file),
              let top = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return false }
        if let v = top["permissions"] {
            guard let perms = v as? [String: Any] else { return false }
            for key in ["allow", "deny"] {
                if let list = perms[key], !(list is [String]) { return false }
            }
        }
        for key in ["hooks", "enabledPlugins", "extraKnownMarketplaces"] {
            if let v = top[key], !(v is [String: Any]) { return false }
        }
        if let v = top["model"], !(v is String) { return false }
        if let v = top["env"], !(v is [String: String]) { return false }
        return true
    }
}
