import Foundation

public struct Finding: Equatable, Sendable {
    public enum Level: String, Sendable { case ok, note, fault }
    public var level: Level
    public var profileId: String?
    public var message: String
    public var fix: String?

    public init(level: Level, profileId: String? = nil, message: String, fix: String? = nil) {
        self.level = level
        self.profileId = profileId
        self.message = message
        self.fix = fix
    }
}

public struct DoctorReport: Equatable, Sendable {
    public var findings: [Finding]
    public var hasFaults: Bool { findings.contains { $0.level == .fault } }
}

/// Implements the flow 11 table. Reads only; a fix is a separate step.
public struct Doctor {
    public let locator: ConfigLocator
    public let keychain: KeychainNames
    public let runner: ProcessRunning
    public var loginShell = "/bin/zsh"

    public init(locator: ConfigLocator, keychain: KeychainNames = KeychainProbe(), runner: ProcessRunning = ProcessRunner()) {
        self.locator = locator
        self.keychain = keychain
        self.runner = runner
    }

    /// Reads the registry without seeding it, so doctor changes nothing.
    private func readRegistry() -> Registry? {
        guard let data = try? Data(contentsOf: locator.registryFile) else { return nil }
        let d = JSONDecoder()
        d.dateDecodingStrategy = .iso8601
        return try? d.decode(Registry.self, from: data)
    }

    public func run() -> DoctorReport {
        var f: [Finding] = []
        guard let reg = readRegistry() else {
            return DoctorReport(findings: [Finding(level: .fault, message: "No readable profile list at \(locator.registryFile.path).", fix: "cpm install-shell")])
        }
        let fm = FileManager.default
        let names = keychain.listServiceNames()

        for p in reg.profiles { checkProfile(p, keychainNames: names, into: &f) }

        let known = Set(reg.profiles.compactMap(\.configDir).map { ($0 as NSString).lastPathComponent })
        for orphan in ((try? fm.contentsOfDirectory(atPath: locator.profilesDir.path)) ?? []).sorted()
        where !orphan.hasPrefix(".") && !known.contains(orphan) {
            f.append(Finding(level: .note, message: "Folder profiles/\(orphan) has no profile row. It was left alone."))
        }

        checkDefault(reg, into: &f)
        checkBinaries(into: &f)
        checkLoginVars(into: &f)

        let recorded = Set(reg.profiles.compactMap(\.keychainService))
        for extra in names.sorted() where extra != KeychainProbe.plainName && !recorded.contains(extra) {
            f.append(Finding(level: .note, message: "Keychain item \(extra) matches no recorded profile."))
        }

        let failed = reg.profiles.reduce(0) { $0 + unreadableTranscripts($1) }
        if failed > 0 { f.append(Finding(level: .note, message: "\(failed) transcript(s) could not be read.")) }

        if fm.fileExists(atPath: locator.builtinDir.appendingPathComponent(".claude.json").path) {
            f.append(Finding(level: .note, message: "~/.claude/.claude.json exists. The app never reads it."))
        }
        return DoctorReport(findings: f)
    }

    private func checkProfile(_ p: Profile, keychainNames: Set<String>, into f: inout [Finding]) {
        let fm = FileManager.default
        if !p.builtin {
            if !fm.fileExists(atPath: locator.configDir(for: p).path) {
                f.append(Finding(level: .fault, profileId: p.id, message: "\(p.displayName): its folder is missing.", fix: "Restore the folder, or remove the profile."))
                return
            }
            if p.configDir != locator.newConfigDir(id: p.id) {
                f.append(Finding(level: .fault, profileId: p.id, message: "\(p.displayName): its stored path is not the one made for its ID. It may look logged out."))
            }
        }
        if p.state == .pendingLogin {
            f.append(Finding(level: .note, profileId: p.id, message: "\(p.displayName): waiting for a login.", fix: "claude-as \(p.commandName) auth login, then cpm keep \(p.commandName)"))
            return
        }
        if let a = AccountReader.readAccount(configFile: locator.configFile(for: p)) {
            f.append(Finding(level: .ok, profileId: p.id, message: "\(p.displayName): signed in as \(a.email ?? "an account")."))
        } else {
            f.append(Finding(level: .fault, profileId: p.id, message: "\(p.displayName): not signed in.", fix: "claude-as \(p.commandName) auth login"))
        }
        if let service = p.keychainService, !keychainNames.contains(service) {
            f.append(Finding(level: .fault, profileId: p.id, message: "\(p.displayName): its recorded Keychain login \(service) is missing, or Claude Code's naming changed.", fix: "Sign in again: claude-as \(p.commandName) auth login"))
        }
    }

    private func checkDefault(_ reg: Registry, into f: inout [Finding]) {
        let text = (try? String(contentsOf: locator.defaultFile, encoding: .utf8))?.trimmingCharacters(in: .whitespacesAndNewlines)
        let inNames = ((try? String(contentsOf: locator.namesFile, encoding: .utf8)) ?? "")
            .split(separator: "\n").contains { $0.split(separator: "\t").dropFirst().first.map(String.init) == reg.defaultProfileId }
        if text == reg.defaultProfileId && inNames {
            f.append(Finding(level: .ok, message: "Default profile: \(reg.profile(id: reg.defaultProfileId)?.displayName ?? reg.defaultProfileId)."))
        } else {
            f.append(Finding(level: .fault, message: "The default or names file does not match the profile list. Plain claude will refuse to start.", fix: "cpm doctor --fix"))
        }
    }

    private func checkBinaries(into f: inout [Finding]) {
        // Environment checks run in a login shell, because the app's own PATH differs.
        let wrapper = locator.binDir.appendingPathComponent("claude").path
        let r = runner.run(executable: loginShell, args: ["-lic", "print -r -- CPM_PATH_MARKER; print -r -- $PATH; whence -p claude"], env: nil, timeout: 5)
        let lines = r.stdout.components(separatedBy: "\n")
        guard let i = lines.lastIndex(of: "CPM_PATH_MARKER"), i + 1 < lines.count else {
            f.append(Finding(level: .note, message: "Could not read your shell's PATH."))
            return
        }
        let path = lines[i + 1]
        let resolved = i + 2 < lines.count ? lines[i + 2] : ""
        if let real = ClaudeBinaryLocator(locator: locator, path: path).realBinary() {
            f.append(Finding(level: .ok, message: "Real Claude Code: \(real.path)."))
            let v = runner.run(executable: real.path, args: ["--version"], env: nil, timeout: 15)
            if v.exitCode == 0 { f.append(Finding(level: .note, message: "Claude Code version: \(v.stdout.trimmingCharacters(in: .whitespacesAndNewlines)).")) }
        } else {
            f.append(Finding(level: .fault, message: "Cannot find the real claude binary on your PATH."))
        }
        if ClaudeBinaryLocator.resolved(resolved) == ClaudeBinaryLocator.resolved(wrapper) {
            f.append(Finding(level: .ok, message: "Plain claude runs the wrapper."))
        } else {
            f.append(Finding(level: .fault, message: "Plain claude runs \(resolved.isEmpty ? "nothing" : resolved), not the wrapper, so it ignores your default.", fix: "cpm install-shell, then add the two lines it shows"))
        }
    }

    private func checkLoginVars(into f: inout [Finding]) {
        let r = runner.run(executable: loginShell, args: ["-lic", "print -r -- CPM_VARS_MARKER; for v in ANTHROPIC_API_KEY ANTHROPIC_AUTH_TOKEN CLAUDE_CODE_OAUTH_TOKEN; do [[ -n ${(P)v-} ]] && print -r -- $v; done; true"], env: nil, timeout: 5)
        let lines = r.stdout.components(separatedBy: "\n")
        guard let i = lines.lastIndex(of: "CPM_VARS_MARKER") else { return }
        for v in lines[(i + 1)...] where !v.isEmpty {
            f.append(Finding(level: .note, message: "\(v) is set in your shell. Claude Code reports it instead of a profile's login."))
        }
    }

    private func unreadableTranscripts(_ p: Profile) -> Int {
        let root = locator.configDir(for: p).appendingPathComponent("projects")
        let fm = FileManager.default
        var failed = 0
        for project in (try? fm.contentsOfDirectory(atPath: root.path)) ?? [] {
            let dir = root.appendingPathComponent(project)
            for file in (try? fm.contentsOfDirectory(atPath: dir.path)) ?? [] where file.hasSuffix(".jsonl") {
                if TranscriptReader.summary(of: dir.appendingPathComponent(file)) == nil { failed += 1 }
            }
        }
        return failed
    }
}
