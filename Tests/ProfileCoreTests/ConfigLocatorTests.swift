import Foundation
import Testing
@testable import ProfileCore

@Suite struct ConfigLocatorTests {
    @Test func builtinNeverSetsConfigDir() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.defaultProfile()
        #expect(p.builtin)
        let env = s.locator.environment(for: p)
        #expect(env.set["CLAUDE_CONFIG_DIR"] == nil)
        #expect(env.unset.contains("CLAUDE_CONFIG_DIR"))
        #expect(s.locator.configFile(for: p).path == s.home.appendingPathComponent(".claude.json").path)
    }

    @Test func addedProfileUsesStoredPathUnchanged() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.create(displayName: "Personal", commandName: "personal", color: "#E8772E")
        #expect(s.locator.environment(for: p).set["CLAUDE_CONFIG_DIR"] == p.configDir)
        #expect(s.locator.configFile(for: p).path == p.configDir! + "/.claude.json")
    }

    @Test func newConfigDirIsExact() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        _ = try s.store.load()
        let dir = s.locator.newConfigDir(id: "abc")
        #expect(!dir.contains("~"))
        #expect(!dir.hasSuffix("/"))
        #expect(dir.hasSuffix("/.claude-profiles/profiles/abc"))
        #expect(dir == dir.precomposedStringWithCanonicalMapping)
        let parent = (dir as NSString).deletingLastPathComponent
        #expect(ClaudeBinaryLocator.resolved(parent) == parent)
    }

    @Test func loginVariablesAreNeverTouched() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.defaultProfile()
        let env = s.locator.environment(for: p)
        for v in ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN"] {
            #expect(env.set[v] == nil)
            #expect(!env.unset.contains(v))
        }
    }
}
