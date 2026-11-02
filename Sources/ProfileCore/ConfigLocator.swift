import Foundation

public struct LaunchEnv: Equatable, Sendable {
    public var set: [String: String]
    public var unset: [String]

    public init(set: [String: String] = [:], unset: [String] = []) {
        self.set = set
        self.unset = unset
    }
}

/// The one place in Swift that turns a profile into paths and an environment.
/// `home` and `profilesRoot` are passed in, so tests never touch real paths.
public struct ConfigLocator: Sendable {
    public let home: URL
    public let profilesRoot: URL

    public init(home: URL, profilesRoot: URL? = nil) {
        self.home = home
        self.profilesRoot = profilesRoot ?? home.appendingPathComponent(".claude-profiles")
    }

    /// Uses HOME when it is set, so a test or a temporary HOME is honoured.
    public static func current() -> ConfigLocator {
        let env = ProcessInfo.processInfo.environment["HOME"] ?? NSHomeDirectory()
        return ConfigLocator(home: URL(fileURLWithPath: env))
    }

    public var builtinDir: URL { home.appendingPathComponent(".claude") }
    public var profilesDir: URL { profilesRoot.appendingPathComponent("profiles") }
    public var binDir: URL { profilesRoot.appendingPathComponent("bin") }
    public var logsDir: URL { profilesRoot.appendingPathComponent("logs") }
    public var registryFile: URL { profilesRoot.appendingPathComponent("profiles.json") }
    public var defaultFile: URL { profilesRoot.appendingPathComponent("default") }
    public var namesFile: URL { profilesRoot.appendingPathComponent("names") }
    public var lockFile: URL { profilesRoot.appendingPathComponent(".lock") }

    public func configDir(for p: Profile) -> URL {
        guard let dir = p.configDir else { return builtinDir }
        return URL(fileURLWithPath: dir)
    }

    public func configFile(for p: Profile) -> URL {
        if p.configDir == nil { return home.appendingPathComponent(".claude.json") }
        return configDir(for: p).appendingPathComponent(".claude.json")
    }

    /// The only place a configDir is built. Claude Code names a folder's Keychain login with
    /// a hash of this exact string, so it has no ~, no trailing slash and no symlink, and is NFC.
    public func newConfigDir(id: String) -> String {
        let parent = URL(fileURLWithPath: profilesRoot.path).resolvingSymlinksInPath().path
        var real = parent
        if let r = realpath(parent, nil) {
            real = String(cString: r)
            free(r)
        }
        return "\(real)/profiles/\(id)".precomposedStringWithCanonicalMapping
    }

    /// For commands the app runs directly. Sessions get their environment from the wrapper.
    /// The built-in profile must never set CLAUDE_CONFIG_DIR, even to ~/.claude.
    public func environment(for p: Profile) -> LaunchEnv {
        var env = LaunchEnv(set: ["CLAUDE_PROFILE_ID": p.id])
        if let dir = p.configDir {
            env.set["CLAUDE_CONFIG_DIR"] = dir
        } else {
            env.unset.append("CLAUDE_CONFIG_DIR")
        }
        return env
    }
}
