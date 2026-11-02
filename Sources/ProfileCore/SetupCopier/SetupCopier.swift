import Foundation

/// Design flow 6. Only the items in the flow's table are offered; nothing else can be copied.
/// Direct writes match the preview exactly. Claude Code's own install commands show the intended
/// change and may also write caches and registries, which Restore cannot undo.
public struct SetupCopier {
    public let store: ProfileStore
    public let locator: ConfigLocator
    public let cli: ClaudeCLI
    public let live: LiveSessionReader
    public let backups: BackupStore

    public init(store: ProfileStore, cli: ClaudeCLI? = nil, live: LiveSessionReader? = nil) {
        self.store = store
        self.locator = store.locator
        self.cli = cli ?? ClaudeCLI(locator: store.locator)
        self.live = live ?? LiveSessionReader(locator: store.locator)
        self.backups = BackupStore(locator: store.locator)
    }

    // MARK: Plan

    public func plan(source: Profile, target: Profile) throws -> CopyPlan {
        guard !target.builtin else { throw ProfileError.notAllowed("The built-in profile can be copied from, never into.") }
        guard source.id != target.id else { throw ProfileError.notAllowed("Choose two different profiles.") }
        let targetSettingsFile = settingsFile(target)
        let src = readJSON(settingsFile(source))
        let dst = readJSON(targetSettingsFile)
        var items: [CopyItem] = []
        items += mcpItems(source: source, target: target)
        items += permissionItems(src, dst)
        items += hookItems(src, dst, source: source)
        items += modelItem(src, dst)
        items += envItems(src, dst)
        items += pluginItems(src, dst, source: source, target: target)
        items += fileItems(source: source, target: target)

        var hashes: [String: String?] = [targetSettingsFile.path: sha256(of: targetSettingsFile)]
        for item in items where item.isFile {
            // updateValue, because assigning nil through a subscript would forget that the file did not exist.
            for path in filePairs(item, source: source, target: target).map(\.dest.path) { hashes.updateValue(sha256(of: URL(fileURLWithPath: path)), forKey: path) }
        }
        return CopyPlan(sourceId: source.id, targetId: target.id, items: items, blockedReason: blockedReason(target), beforeHashes: hashes)
    }

    private func blockedReason(_ target: Profile) -> String? {
        if target.state != .ready { return "\(target.displayName) is not signed in." }
        if live.hasLiveSession(target) { return "\(target.displayName) has a live session. Stop it or wait for it to end, then copy." }
        if !CompatCheck.canWriteSettings(file: settingsFile(target)) { return "\(target.displayName)'s settings.json has an unexpected shape, so it was left alone." }
        return nil
    }

    private func settingsFile(_ p: Profile) -> URL { locator.configDir(for: p).appendingPathComponent("settings.json") }

    private func readJSON(_ url: URL) -> [String: Any] {
        guard let data = try? Data(contentsOf: url) else { return [:] }
        return (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] ?? [:]
    }

    private func mcpServers(_ p: Profile) -> [String: [String: Any]] {
        readJSON(locator.configFile(for: p))["mcpServers"] as? [String: [String: Any]] ?? [:]
    }

    private func mcpItems(source: Profile, target: Profile) -> [CopyItem] {
        let have = mcpServers(target)
        let sourceDir = locator.configDir(for: source).path
        return mcpServers(source).sorted { $0.key < $1.key }.map { name, server in
            var item = CopyItem(id: "mcp:\(name)", kind: .mcpServer, key: name, action: .add)
            if let old = have[name] {
                item.action = SettingsMerger.canonical(old) == SettingsMerger.canonical(server) ? .same : .change
            }
            for key in (server["env"] as? [String: Any] ?? [:]).keys.sorted() { item.secrets.append(SecretField(name: "env.\(key)")) }
            for key in (server["headers"] as? [String: Any] ?? [:]).keys.sorted() { item.secrets.append(SecretField(name: "headers.\(key)")) }
            for (i, arg) in (server["args"] as? [String] ?? []).enumerated() where SecretDetector.looksSecret(arg) {
                item.secrets.append(SecretField(name: "args[\(i)]", isArgument: true))
            }
            // A URL cannot be dropped and still work, so it is treated like an argument.
            if let url = server["url"] as? String, SecretDetector.looksSecret(url) { item.secrets.append(SecretField(name: "url", isArgument: true)) }
            let text = ([server["command"] as? String ?? ""] + (server["args"] as? [String] ?? [])).joined(separator: " ")
            item.pointsIntoSource = text.contains(sourceDir)
            let type = server["type"] as? String
            item.needsReLogin = (type == "http" || type == "sse") && (server["headers"] == nil)
            item.detail = type ?? "stdio"
            item.removalCommand = "CLAUDE_CONFIG_DIR=\(Launcher.quote(target.configDir ?? "")) claude mcp remove \(Launcher.quote(name)) --scope user"
            item.ticked = item.action != .same && item.secrets.isEmpty && !item.pointsIntoSource
            return item
        }
    }

    private func permissionItems(_ src: [String: Any], _ dst: [String: Any]) -> [CopyItem] {
        ["allow", "deny"].compactMap { key in
            guard ((src["permissions"] as? [String: Any])?[key] as? [String])?.isEmpty == false else { return nil }
            let new = SettingsMerger.newPermissions(source: src, target: dst, key: key)
            let exists = (dst["permissions"] as? [String: Any])?[key] != nil
            var item = CopyItem(id: "perm:\(key)", kind: .permissions, key: "permissions.\(key)", action: new.isEmpty ? .same : (exists ? .change : .add))
            item.detail = new.isEmpty ? "No new rules." : new.map { "+ \($0)" }.joined(separator: ", ") + (exists ? ". Existing rules kept." : "")
            item.flaggedFile = new.contains(where: SecretDetector.looksSecret)
            item.ticked = !new.isEmpty && !item.flaggedFile
            return item
        }
    }

    private func hookItems(_ src: [String: Any], _ dst: [String: Any], source: Profile) -> [CopyItem] {
        let sourceDir = locator.configDir(for: source).path
        return SettingsMerger.hookEntries(src).enumerated().map { i, h in
            let new = SettingsMerger.newHooks(event: h.event, matcher: h.matcher, entry: h.entry, target: dst)
            let label = h.matcher.isEmpty ? h.event : "\(h.event) (\(h.matcher))"
            var item = CopyItem(id: "hook:\(i):\(h.event):\(h.matcher)", kind: .hook, key: label, action: new.isEmpty ? .same : .add)
            let text = SettingsMerger.canonical(new)
            item.pointsIntoSource = text.contains(sourceDir)
            item.flaggedFile = SecretDetector.looksSecret(text)
            item.detail = new.isEmpty ? "No differences." : "\(new.count) hook(s)."
            return item
        }
    }

    private func modelItem(_ src: [String: Any], _ dst: [String: Any]) -> [CopyItem] {
        guard let model = src["model"] as? String else { return [] }
        let old = dst["model"] as? String
        var item = CopyItem(id: "model", kind: .model, key: "model", action: old == model ? .same : (old == nil ? .add : .change))
        item.detail = old == model ? "No differences." : (old.map { "\($0) to \(model)" } ?? model)
        return [item]
    }

    private func envItems(_ src: [String: Any], _ dst: [String: Any]) -> [CopyItem] {
        let from = src["env"] as? [String: String] ?? [:]
        let have = dst["env"] as? [String: String] ?? [:]
        return from.keys.sorted().map { key in
            var item = CopyItem(id: "env:\(key)", kind: .env, key: "env.\(key)", action: have[key] == from[key] ? .same : (have[key] == nil ? .add : .change))
            item.detail = "Value: \(SecretDetector.mask)"
            return item
        }
    }

    private func pluginItems(_ src: [String: Any], _ dst: [String: Any], source: Profile, target: Profile) -> [CopyItem] {
        let wanted = (src["enabledPlugins"] as? [String: Bool] ?? [:]).filter(\.value).keys.sorted()
        let have = dst["enabledPlugins"] as? [String: Bool] ?? [:]
        return wanted.map { name in
            var item = CopyItem(id: "plugin:\(name)", kind: .plugin, key: name, action: have[name] == true ? .same : .add)
            item.detail = "Installed fresh with Claude Code's own command. Restore cannot undo this."
            item.removalCommand = "CLAUDE_CONFIG_DIR=\(Launcher.quote(target.configDir ?? "")) claude plugin uninstall \(Launcher.quote(name))"
            item.ticked = item.action == .add
            return item
        }
    }

    // MARK: Files

    private struct FilePair { let source: URL; let dest: URL }

    private func fileItems(source: Profile, target: Profile) -> [CopyItem] {
        var items: [CopyItem] = []
        let from = locator.configDir(for: source), to = locator.configDir(for: target)
        for (folder, kind) in [("skills", CopyKind.skill), ("agents", .agent), ("commands", .command)] {
            let dir = from.appendingPathComponent(folder)
            // skills/synced comes from the account, through its own sync, and is never copied.
            let names = ((try? FileManager.default.contentsOfDirectory(atPath: dir.path)) ?? [])
                .filter { !$0.hasPrefix(".") && !(folder == "skills" && $0 == "synced") }.sorted()
            for name in names {
                items.append(fileItem(kind: kind, key: "\(folder)/\(name)", from: dir.appendingPathComponent(name), to: to.appendingPathComponent(folder).appendingPathComponent(name)))
            }
        }
        let claudeMd = from.appendingPathComponent("CLAUDE.md")
        if FileManager.default.fileExists(atPath: claudeMd.path) {
            items.append(fileItem(kind: .claudeMd, key: "CLAUDE.md", from: claudeMd, to: to.appendingPathComponent("CLAUDE.md")))
        }
        return items
    }

    private func fileItem(kind: CopyKind, key: String, from: URL, to: URL) -> CopyItem {
        let pairs = Self.expand(from: from, to: to)
        let existing = pairs.filter { FileManager.default.fileExists(atPath: $0.dest.path) }
        let same = pairs.count == existing.count && pairs.allSatisfy { sha256(of: $0.source) == sha256(of: $0.dest) }
        var item = CopyItem(id: "file:\(key)", kind: kind, key: key, action: same ? .same : (FileManager.default.fileExists(atPath: to.path) ? .change : .add))
        item.flaggedFile = pairs.contains { SecretDetector.fileLooksSecret($0.source) }
        item.detail = item.action == .change ? "Exists in the target. Choose skip, overwrite or rename when ticked." : ""
        item.ticked = item.action == .add && !item.flaggedFile
        return item
    }

    /// Every file under a skill folder. A symbolic link is followed, so the target never depends on the source.
    /// A link back to a parent folder is skipped, and so is anything that is not a plain file, such as a pipe.
    private static func expand(from: URL, to: URL, parents: Set<String> = []) -> [FilePair] {
        let fm = FileManager.default
        let real = from.resolvingSymlinksInPath()
        var isDir: ObjCBool = false
        guard fm.fileExists(atPath: real.path, isDirectory: &isDir) else { return [] }
        guard isDir.boolValue else {
            let type = (try? fm.attributesOfItem(atPath: real.path))?[.type] as? FileAttributeType
            return type == .typeRegular ? [FilePair(source: real, dest: to)] : []
        }
        guard !parents.contains(real.path) else { return [] }
        var out: [FilePair] = []
        for name in ((try? fm.contentsOfDirectory(atPath: real.path)) ?? []).sorted() where name != ".DS_Store" {
            out += expand(from: real.appendingPathComponent(name), to: to.appendingPathComponent(name), parents: parents.union([real.path]))
        }
        return out
    }

    private func filePairs(_ item: CopyItem, source: Profile, target: Profile) -> [FilePair] {
        var to = locator.configDir(for: target).appendingPathComponent(item.key)
        if item.conflict == .rename, item.action == .change { to = Self.renamed(to) }
        return Self.expand(from: locator.configDir(for: source).appendingPathComponent(item.key), to: to)
    }

    /// Adds -copy before the extension, then a number, so a rename never overwrites anything.
    static func renamed(_ url: URL) -> URL {
        let ext = url.pathExtension
        let stem = url.deletingPathExtension().lastPathComponent
        var n = 1
        while true {
            let base = n == 1 ? "\(stem)-copy" : "\(stem)-copy-\(n)"
            let candidate = url.deletingLastPathComponent().appendingPathComponent(ext.isEmpty ? base : "\(base).\(ext)")
            if !FileManager.default.fileExists(atPath: candidate.path) { return candidate }
            n += 1
        }
    }

    // MARK: Apply

    public func apply(_ plan: CopyPlan) throws -> ApplyResult {
        let reg = try store.load()
        guard let source = reg.profile(id: plan.sourceId), let target = reg.profile(id: plan.targetId) else {
            throw ProfileError.notFound(plan.targetId)
        }
        // Everything is checked again, because the preview may be minutes old.
        guard !target.builtin else { throw ProfileError.notAllowed("The built-in profile can be copied from, never into.") }
        if let reason = blockedReason(target) { throw ProfileError.notAllowed(reason) }

        let chosen = plan.items.filter { $0.ticked && $0.action != .same }
        let filePairs = chosen.filter(\.isFile).filter { !($0.action == .change && $0.conflict == .skip) }
            .flatMap { self.filePairs($0, source: source, target: target) }
        let settingsItems = chosen.filter { [.permissions, .hook, .model, .env].contains($0.kind) }
        let settingsPath = settingsFile(target)

        for (path, hash) in plan.beforeHashes where filePairs.contains(where: { $0.dest.path == path }) || (path == settingsPath.path && !settingsItems.isEmpty) {
            if sha256(of: URL(fileURLWithPath: path)) != hash {
                throw ProfileError.notAllowed("\((path as NSString).lastPathComponent) changed since the preview. Nothing was copied. Look at the preview again.")
            }
        }

        var touched = filePairs.map(\.dest.path)
        if !settingsItems.isEmpty { touched.append(settingsPath.path) }
        let begun = try backups.begin(profileId: target.id, paths: touched)
        let backupId = begun.id
        var manifest = begun.manifest

        var done: [String] = []
        var failed: [CopyFailure] = []
        let fm = FileManager.default

        // Direct writes first. A failure stops everything after it, and keeps the backup.
        do {
            if !settingsItems.isEmpty {
                var merged = readJSON(settingsPath)
                let src = readJSON(settingsFile(source))
                for item in settingsItems { merge(item, from: src, into: &merged) }
                let data = try JSONSerialization.data(withJSONObject: merged, options: [.prettyPrinted, .sortedKeys])
                try AtomicFile.write(data, to: settingsPath, mode: 0o600)
                done += settingsItems.map(\.id)
            }
            for item in chosen where item.isFile {
                if item.action == .change, item.conflict == .skip { continue }
                for pair in self.filePairs(item, source: source, target: target) {
                    try fm.createDirectory(at: pair.dest.deletingLastPathComponent(), withIntermediateDirectories: true)
                    let mode = ((try? fm.attributesOfItem(atPath: pair.source.path))?[.posixPermissions] as? NSNumber)?.int16Value ?? 0o600
                    try AtomicFile.write(try Data(contentsOf: pair.source), to: pair.dest, mode: mode_t(mode))
                }
                done.append(item.id)
            }
        } catch {
            failed.append(CopyFailure(itemId: "files", reason: error.localizedDescription))
        }

        // Then Claude Code's own commands, which Restore cannot undo.
        var removals: [String] = []
        if failed.isEmpty {
            let sourceSettings = readJSON(settingsFile(source))
            let servers = mcpServers(source)
            for item in chosen where item.kind == .mcpServer || item.kind == .plugin {
                let r = item.kind == .mcpServer
                    ? installMCP(item, server: servers[item.key] ?? [:], target: target)
                    : installPlugin(item, sourceSettings: sourceSettings, source: source, target: target)
                if let reason = r {
                    failed.append(CopyFailure(itemId: item.id, reason: reason))
                    break
                }
                done.append(item.id)
                if let c = item.removalCommand { removals.append(c) }
            }
        }
        // After the commands, because a plugin install rewrites settings.json, and Restore would
        // otherwise take that for later work and leave the file alone.
        for i in manifest.entries.indices { manifest.entries[i].after = sha256(of: URL(fileURLWithPath: manifest.entries[i].path)) }
        manifest.removalCommands = removals
        try backups.save(manifest, id: backupId)
        locator.logEvent(kind: "copy", profileId: target.id, detail: "\(done.count) items from \(source.id), \(failed.count) failed, backup \(backupId)")
        return ApplyResult(done: done, failed: failed, backupId: backupId, removalCommands: removals)
    }

    private func merge(_ item: CopyItem, from src: [String: Any], into merged: inout [String: Any]) {
        switch item.kind {
        case .permissions:
            let key = item.key.replacingOccurrences(of: "permissions.", with: "")
            SettingsMerger.addPermissions(SettingsMerger.newPermissions(source: src, target: merged, key: key), key: key, to: &merged)
        case .hook:
            for (i, h) in SettingsMerger.hookEntries(src).enumerated() where item.id == "hook:\(i):\(h.event):\(h.matcher)" {
                SettingsMerger.addHooks(SettingsMerger.newHooks(event: h.event, matcher: h.matcher, entry: h.entry, target: merged), event: h.event, matcher: h.matcher, to: &merged)
            }
        case .model:
            merged["model"] = src["model"]
        case .env:
            let key = item.key.replacingOccurrences(of: "env.", with: "")
            var env = merged["env"] as? [String: String] ?? [:]
            env[key] = (src["env"] as? [String: String])?[key]
            merged["env"] = env
        default:
            break
        }
    }

    /// Returns a reason if it failed.
    private func installMCP(_ item: CopyItem, server: [String: Any], target: Profile) -> String? {
        var s = server
        for field in item.secrets where !field.ticked {
            if field.isArgument { return "It has a secret in its arguments, which was left out, so the server was not copied." }
            let parts = field.name.split(separator: ".", maxSplits: 1).map(String.init)
            guard parts.count == 2, var group = s[parts[0]] as? [String: Any] else { continue }
            group.removeValue(forKey: parts[1])
            s[parts[0]] = group
        }
        guard JSONSerialization.isValidJSONObject(s),
              let data = try? JSONSerialization.data(withJSONObject: s, options: [.sortedKeys]) else { return "Its entry could not be read." }
        // Claude Code will not add over an existing name, and its old entry is in no backup.
        let old = item.action == .change ? mcpServers(target)[item.key] : nil
        if item.action == .change {
            let r = cli.removeMCP(name: item.key, profile: target)
            if r.exitCode != 0 { return "Could not replace it: \(r.stderr)" }
        }
        let r = cli.addMCP(name: item.key, json: String(decoding: data, as: UTF8.self), profile: target)
        if r.exitCode == 0 { return nil }
        let reason = r.stderr.isEmpty ? r.stdout : r.stderr
        guard item.action == .change else { return reason }
        if let old, let oldData = try? JSONSerialization.data(withJSONObject: old, options: [.sortedKeys]),
           cli.addMCP(name: item.key, json: String(decoding: oldData, as: UTF8.self), profile: target).exitCode == 0 {
            return "\(reason) Its old entry was put back."
        }
        return "\(reason) Its old entry was removed and could not be put back, so add it again in that profile."
    }

    private func installPlugin(_ item: CopyItem, sourceSettings: [String: Any], source: Profile, target: Profile) -> String? {
        let marketplace = item.key.split(separator: "@").dropFirst().first.map(String.init)
        if let marketplace, let origin = marketplaceSource(marketplace, settings: sourceSettings, source: source) {
            let r = cli.addMarketplace(source: origin, profile: target)
            // Already added is fine, and the install below is the real test.
            if r.exitCode != 0, !(r.stdout + r.stderr).lowercased().contains("already") { return r.stderr.isEmpty ? r.stdout : r.stderr }
        }
        let r = cli.installPlugin(name: item.key, profile: target)
        return r.exitCode == 0 ? nil : (r.stderr.isEmpty ? r.stdout : r.stderr)
    }

    /// Reads only where the marketplace comes from. The registry itself is never copied.
    private func marketplaceSource(_ name: String, settings: [String: Any], source: Profile) -> String? {
        func origin(_ entry: Any?) -> String? {
            guard let s = (entry as? [String: Any])?["source"] as? [String: Any] else { return nil }
            return (s["repo"] ?? s["url"] ?? s["path"]) as? String
        }
        if let o = origin((settings["extraKnownMarketplaces"] as? [String: Any])?[name]) { return o }
        let known = readJSON(locator.configDir(for: source).appendingPathComponent("plugins/known_marketplaces.json"))
        return origin(known[name])
    }

    // MARK: Restore

    public func restore(profile target: Profile, backupId: String) throws -> RestoreResult {
        guard !target.builtin else { throw ProfileError.notAllowed("The built-in profile is never written to.") }
        guard !live.hasLiveSession(target) else { throw ProfileError.notAllowed("\(target.displayName) has a live session. Stop it first.") }
        let m = try backups.load(profileId: target.id, id: backupId)
        let fm = FileManager.default
        var result = RestoreResult(restored: [], removed: [], leftAlone: [], removalCommands: m.removalCommands)
        for e in m.entries {
            let url = URL(fileURLWithPath: e.path)
            let now = sha256(of: url)
            guard now == e.after else {
                // Already back to how it was: nothing to do, and nothing to warn about.
                if now != e.before { result.leftAlone.append(e.path) }
                continue
            }
            if e.existedBefore, let backup = backups.backupFile(e, profileId: target.id, id: backupId) {
                try AtomicFile.write(try Data(contentsOf: backup), to: url, mode: mode_t(e.mode ?? 0o600))
                result.restored.append(e.path)
            } else {
                try fm.removeItem(at: url)
                result.removed.append(e.path)
                removeEmptyParents(of: url, stoppingAt: locator.configDir(for: target))
            }
        }
        locator.logEvent(kind: "restore", profileId: target.id, detail: "\(result.restored.count) restored, \(result.removed.count) removed, \(result.leftAlone.count) left alone, backup \(backupId)")
        return result
    }

    private func removeEmptyParents(of url: URL, stoppingAt stop: URL) {
        var dir = url.deletingLastPathComponent()
        let fm = FileManager.default
        while dir.path.hasPrefix(stop.path + "/"), ((try? fm.contentsOfDirectory(atPath: dir.path)) ?? ["x"]).isEmpty {
            try? fm.removeItem(at: dir)
            dir = dir.deletingLastPathComponent()
        }
    }
}
