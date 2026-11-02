import Foundation

/// Merges into a settings.json. Unknown keys are kept. statusLine and apiKeyHelper are never
/// touched, because no item names them. The same function gives the preview and the written result.
public enum SettingsMerger {
    public static func newPermissions(source: [String: Any], target: [String: Any], key: String) -> [String] {
        let from = (source["permissions"] as? [String: Any])?[key] as? [String] ?? []
        let have = Set((target["permissions"] as? [String: Any])?[key] as? [String] ?? [])
        var seen = Set<String>()
        return from.filter { !have.contains($0) && seen.insert($0).inserted }
    }

    public static func addPermissions(_ rules: [String], key: String, to target: inout [String: Any]) {
        var perms = target["permissions"] as? [String: Any] ?? [:]
        var list = perms[key] as? [String] ?? []
        for r in rules where !list.contains(r) { list.append(r) }
        perms[key] = list
        target["permissions"] = perms
    }

    /// One entry of a hooks event, with its matcher.
    public static func hookEntries(_ settings: [String: Any]) -> [(event: String, matcher: String, entry: [String: Any])] {
        guard let hooks = settings["hooks"] as? [String: Any] else { return [] }
        var out: [(String, String, [String: Any])] = []
        for (event, value) in hooks {
            for entry in value as? [[String: Any]] ?? [] {
                out.append((event, entry["matcher"] as? String ?? "", entry))
            }
        }
        return out.sorted { ($0.0, $0.1) < ($1.0, $1.1) }
    }

    /// The hooks in `entry` that the target does not already have under this event and matcher.
    public static func newHooks(event: String, matcher: String, entry: [String: Any], target: [String: Any]) -> [[String: Any]] {
        var have: [String] = []
        for h in hookEntries(target) where h.event == event && h.matcher == matcher {
            for hook in h.entry["hooks"] as? [[String: Any]] ?? [] { have.append(canonical(hook)) }
        }
        return (entry["hooks"] as? [[String: Any]] ?? []).filter { !have.contains(canonical($0)) }
    }

    public static func addHooks(_ new: [[String: Any]], event: String, matcher: String, to target: inout [String: Any]) {
        guard !new.isEmpty else { return }
        var hooks = target["hooks"] as? [String: Any] ?? [:]
        var entries = hooks[event] as? [[String: Any]] ?? []
        if let i = entries.firstIndex(where: { ($0["matcher"] as? String ?? "") == matcher }) {
            entries[i]["hooks"] = (entries[i]["hooks"] as? [[String: Any]] ?? []) + new
        } else {
            var e: [String: Any] = ["hooks": new]
            if !matcher.isEmpty { e["matcher"] = matcher }
            entries.append(e)
        }
        hooks[event] = entries
        target["hooks"] = hooks
    }

    /// Slashes stay as they are, so a path can be searched for in the result.
    public static func canonical(_ v: Any) -> String {
        guard JSONSerialization.isValidJSONObject(v),
              let d = try? JSONSerialization.data(withJSONObject: v, options: [.sortedKeys, .withoutEscapingSlashes]) else { return "\(v)" }
        return String(decoding: d, as: UTF8.self)
    }
}
