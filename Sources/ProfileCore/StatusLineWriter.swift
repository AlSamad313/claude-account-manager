import Foundation

/// Status line for added profiles. Written once at creation, because profile-label reads the
/// display name. The built-in profile is refused: the app never writes to ~/.claude.
public struct StatusLineWriter {
    public let locator: ConfigLocator

    public func write(for p: Profile) throws {
        guard !p.builtin, p.configDir != nil else {
            throw ProfileError.notAllowed("The app never writes to ~/.claude.")
        }
        let label = locator.binDir.appendingPathComponent("profile-label").path
        let quoted = "'" + label.replacingOccurrences(of: "'", with: "'\\''") + "'"
        let settings: [String: Any] = ["statusLine": ["type": "command", "command": "\(quoted) \(p.id)"]]
        let data = try JSONSerialization.data(withJSONObject: settings, options: [.prettyPrinted, .sortedKeys])
        try AtomicFile.write(data, to: locator.configDir(for: p).appendingPathComponent("settings.json"), mode: 0o600)
    }
}
