using ProfileCore.Shared;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ProfileCore;

/// Design flow 6. Only the items in the flow's table are offered; nothing else can be copied.
/// Direct writes match the preview exactly. Claude Code's own install commands show the intended
/// change and may also write caches and registries, which Restore cannot undo.
public sealed partial class SetupCopier(ProfileStore store, ClaudeCLI? cli = null, LiveSessionReader? live = null)
{
    readonly ConfigLocator locator = store.Locator;
    public ClaudeCLI Cli { get; } = cli ?? new ClaudeCLI(store.Locator);
    public LiveSessionReader Live { get; } = live ?? new LiveSessionReader(store.Locator);
    public BackupStore Backups { get; } = new(store.Locator);

    const string BuiltinRefusal = "The built-in profile can be copied from, never into.";

    // ---- Plan

    public CopyPlan Plan(Profile source, Profile target)
    {
        if (target.Builtin) throw new ProfileException(BuiltinRefusal);
        if (source.Id == target.Id) throw new ProfileException("Choose two different profiles.");
        var targetSettings = SettingsFile(target);
        var src = JsonTree.Read(SettingsFile(source));
        var dst = JsonTree.Read(targetSettings);
        var forms = SourceForms(source);
        var items = new List<CopyItem>();
        items.AddRange(McpItems(source, target, forms));
        items.AddRange(PermissionItems(src, dst));
        items.AddRange(HookItems(src, dst, forms));
        items.AddRange(ModelItem(src, dst));
        items.AddRange(EnvItems(src, dst));
        items.AddRange(PluginItems(src, dst, target));
        items.AddRange(FileItems(source, target));

        var hashes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [targetSettings] = BackupStore.Hash(targetSettings) };
        foreach (var item in items.Where(i => i.IsFile))
            foreach (var pair in FilePairs(item, source, target)) hashes[pair.Dest] = BackupStore.Hash(pair.Dest);
        return new CopyPlan(source.Id, target.Id, items, BlockedReason(target), hashes);
    }

    string? BlockedReason(Profile target)
    {
        if (target.State != ProfileState.Ready) return $"{target.DisplayName} is not signed in.";
        if (Live.HasLiveSession(target)) return $"{target.DisplayName} has a live session. Stop it or wait for it to end, then copy.";
        if (!CompatCheck.CanWriteSettings(SettingsFile(target))) return $"{target.DisplayName}'s settings.json has an unexpected shape, so it was left alone.";
        return null;
    }

    string SettingsFile(Profile p) => Path.Combine(locator.ConfigDir(p), "settings.json");

    Dictionary<string, JsonObject> McpServers(Profile p)
    {
        var d = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (JsonTree.Read(locator.ConfigFile(p))["mcpServers"] is JsonObject servers)
            foreach (var (name, v) in servers) if (v is JsonObject o) d[name] = o;
        return d;
    }

    IEnumerable<CopyItem> McpItems(Profile source, Profile target, List<string> forms)
    {
        var have = McpServers(target);
        foreach (var (name, server) in McpServers(source).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var item = new CopyItem($"mcp:{name}", CopyKind.McpServer, name, CopyAction.Add);
            if (have.TryGetValue(name, out var old))
                item.Action = JsonTree.Canonical(old) == JsonTree.Canonical(server) ? CopyAction.Same : CopyAction.Change;
            foreach (var group in new[] { "env", "headers" })
                if (server[group] is JsonObject o)
                    foreach (var key in o.Select(p => p.Key).Order(StringComparer.Ordinal)) item.Secrets.Add(new SecretField($"{group}.{key}"));
            var args = (server["args"] as JsonArray)?.Select(JsonTree.Str).ToList() ?? [];
            // Every argument, including the ones after cmd /c, which MCP servers on Windows often use.
            for (var i = 0; i < args.Count; i++)
                if (args[i] is { } a && SecretDetector.LooksSecret(a)) item.Secrets.Add(new SecretField($"args[{i}]", isArgument: true));
            // A URL cannot be dropped and still work, so it is treated like an argument.
            if (JsonTree.Str(server["url"]) is { } url && SecretDetector.LooksSecret(url)) item.Secrets.Add(new SecretField("url", isArgument: true));
            item.PointsIntoSource = PointsInto(new[] { JsonTree.Str(server["command"]) }.Concat(args).OfType<string>(), forms);
            var type = JsonTree.Str(server["type"]);
            item.NeedsReLogin = type is "http" or "sse" && server["headers"] == null;
            item.Detail = type ?? "stdio";
            item.RemovalCommand = $"claude-as {target.CommandName} mcp remove {Quote(name)} --scope user";
            item.Ticked = item.Action != CopyAction.Same && item.Secrets.Count == 0 && !item.PointsIntoSource;
            yield return item;
        }
    }

    static IEnumerable<CopyItem> PermissionItems(JsonObject src, JsonObject dst)
    {
        foreach (var key in new[] { "allow", "deny" })
        {
            if (!JsonTree.Strings((src["permissions"] as JsonObject)?[key]).Any()) continue;
            var added = SettingsMerger.NewPermissions(src, dst, key);
            var exists = (dst["permissions"] as JsonObject)?[key] != null;
            var item = new CopyItem($"perm:{key}", CopyKind.Permissions, $"permissions.{key}",
                                    added.Count == 0 ? CopyAction.Same : exists ? CopyAction.Change : CopyAction.Add);
            item.Detail = added.Count == 0 ? "No new rules." : string.Join(", ", added.Select(r => "+ " + r)) + (exists ? ". Existing rules kept." : "");
            item.FlaggedFile = added.Any(SecretDetector.LooksSecret);
            item.Ticked = added.Count > 0 && !item.FlaggedFile;
            yield return item;
        }
    }

    static IEnumerable<CopyItem> HookItems(JsonObject src, JsonObject dst, List<string> forms)
    {
        var entries = SettingsMerger.HookEntries(src);
        for (var i = 0; i < entries.Count; i++)
        {
            var h = entries[i];
            var added = SettingsMerger.NewHooks(h, dst);
            var label = h.Matcher.Length == 0 ? h.Event : $"{h.Event} ({h.Matcher})";
            var item = new CopyItem(HookId(i, h), CopyKind.Hook, label, added.Count == 0 ? CopyAction.Same : CopyAction.Add);
            var strings = added.SelectMany(JsonTree.AllStrings).ToList();
            item.PointsIntoSource = PointsInto(strings, forms);
            item.FlaggedFile = strings.Any(SecretDetector.LooksSecret);
            item.Detail = added.Count == 0 ? "No differences." : added.Count == 1 ? "1 hook." : $"{added.Count} hooks.";
            yield return item;
        }
    }

    static string HookId(int i, SettingsMerger.HookEntry h) => $"hook:{i}:{h.Event}:{h.Matcher}";

    static IEnumerable<CopyItem> ModelItem(JsonObject src, JsonObject dst)
    {
        if (JsonTree.Str(src["model"]) is not { } model) yield break;
        var old = JsonTree.Str(dst["model"]);
        var item = new CopyItem("model", CopyKind.Model, "model", old == model ? CopyAction.Same : old == null ? CopyAction.Add : CopyAction.Change)
        {
            Detail = old == model ? "No differences." : old != null ? $"{old} to {model}" : model,
        };
        yield return item;
    }

    static Dictionary<string, string> Env(JsonObject settings)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (settings["env"] is JsonObject o) foreach (var (k, v) in o) if (JsonTree.Str(v) is { } s) d[k] = s;
        return d;
    }

    static IEnumerable<CopyItem> EnvItems(JsonObject src, JsonObject dst)
    {
        var from = Env(src);
        var have = Env(dst);
        foreach (var key in from.Keys.Order(StringComparer.Ordinal))
        {
            var action = !have.TryGetValue(key, out var old) ? CopyAction.Add : old == from[key] ? CopyAction.Same : CopyAction.Change;
            yield return new CopyItem($"env:{key}", CopyKind.Env, $"env.{key}", action) { Detail = $"Value: {SecretDetector.Mask}" };
        }
    }

    static IEnumerable<string> EnabledPlugins(JsonObject settings) =>
        (settings["enabledPlugins"] as JsonObject)?.Where(p => p.Value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.True)
            .Select(p => p.Key) ?? [];

    static IEnumerable<CopyItem> PluginItems(JsonObject src, JsonObject dst, Profile target)
    {
        var have = EnabledPlugins(dst).ToHashSet(StringComparer.Ordinal);
        foreach (var name in EnabledPlugins(src).Order(StringComparer.Ordinal))
        {
            var item = new CopyItem($"plugin:{name}", CopyKind.Plugin, name, have.Contains(name) ? CopyAction.Same : CopyAction.Add)
            {
                Detail = "Installed fresh with Claude Code's own command. Restore cannot undo this.",
                RemovalCommand = $"claude-as {target.CommandName} plugin uninstall {Quote(name)}",
            };
            item.Ticked = item.Action == CopyAction.Add;
            yield return item;
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._@/:+-]+$")]
    private static partial Regex PlainWord();

    /// For a command shown to the user to paste into PowerShell or Command Prompt.
    static string Quote(string s) => PlainWord().IsMatch(s) ? s : "\"" + s.Replace("\"", "") + "\"";

    // ---- Pointing into the source folder

    /// The ways a command may name the source folder: as stored, as its long final path, as its 8.3
    /// short form, and through the home folder.
    List<string> SourceForms(Profile source)
    {
        var dir = locator.ConfigDir(source);
        var full = PathCanon.Canonical(dir);
        var forms = new List<string> { PathCanon.Tidy(dir), full };
        // 8.3 short names for the start of the path and long ones after, at every level, as a path
        // built on a short %TEMP% or home folder has. The whole path short is the last of these.
        for (var at = full.IndexOf('\\', 3); ; at = full.IndexOf('\\', at + 1))
        {
            var head = at < 0 ? full : full[..at];
            if (ShortPath(head) is { } s) forms.Add(PathCanon.Tidy(s) + (at < 0 ? "" : full[at..]));
            if (at < 0) break;
        }
        if (PathCanon.IsInside(dir, locator.Home))
        {
            var rel = PathCanon.Tidy(dir)[(PathCanon.Tidy(locator.Home).Length + 1)..];
            forms.AddRange(["~\\" + rel, "%USERPROFILE%\\" + rel, "$env:USERPROFILE\\" + rel, "$HOME\\" + rel, "${env:USERPROFILE}\\" + rel]);
        }
        return forms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// Case does not matter and / is the same as \. The folder name must end where it is found, so
    /// .claude does not match .claude-profiles.
    internal static bool PointsInto(IEnumerable<string> texts, List<string> forms)
    {
        foreach (var t in texts)
        {
            var n = t.Replace('/', '\\');
            foreach (var f in forms)
            {
                for (var i = n.IndexOf(f, StringComparison.OrdinalIgnoreCase); i >= 0; i = n.IndexOf(f, i + 1, StringComparison.OrdinalIgnoreCase))
                {
                    var end = i + f.Length;
                    if (end == n.Length || n[end] is '\\' or '"' or '\'' or ' ' or ';' or ',' or ')' or '&' or '|') return true;
                }
            }
        }
        return false;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetShortPathNameW(string longPath, [Out] char[]? buffer, uint size);

    static string? ShortPath(string path)
    {
        var size = GetShortPathNameW(path, null, 0);
        if (size == 0) return null;
        var buf = new char[size];
        var n = GetShortPathNameW(path, buf, size);
        return n == 0 || n >= size ? null : new string(buf, 0, (int)n);
    }

    // ---- Files

    sealed record FilePair(string Source, string Dest);

    static readonly string[] Skipped = [".DS_Store", "desktop.ini", "Thumbs.db"];

    IEnumerable<CopyItem> FileItems(Profile source, Profile target)
    {
        string from = locator.ConfigDir(source), to = locator.ConfigDir(target);
        foreach (var (folder, kind) in new[] { ("skills", CopyKind.Skill), ("agents", CopyKind.Agent), ("commands", CopyKind.Command) })
        {
            var dir = Path.Combine(from, folder);
            if (!Directory.Exists(dir)) continue;
            // skills\synced comes from the account, through its own sync, and is never copied.
            var names = Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).OfType<string>()
                .Where(n => !n.StartsWith('.') && !Skipped.Contains(n, StringComparer.OrdinalIgnoreCase)
                            && !(folder == "skills" && n.Equals("synced", StringComparison.OrdinalIgnoreCase)))
                .Order(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names) yield return FileItem(kind, $"{folder}/{name}", Path.Combine(dir, name), Path.Combine(to, folder, name));
        }
        var claudeMd = Path.Combine(from, "CLAUDE.md");
        if (File.Exists(claudeMd)) yield return FileItem(CopyKind.ClaudeMd, "CLAUDE.md", claudeMd, Path.Combine(to, "CLAUDE.md"));
    }

    static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);

    static CopyItem FileItem(CopyKind kind, string key, string from, string to)
    {
        var pairs = Expand(from, to);
        var same = pairs.All(p => File.Exists(p.Dest) && BackupStore.Hash(p.Source) == BackupStore.Hash(p.Dest));
        var item = new CopyItem($"file:{key}", kind, key, same ? CopyAction.Same : Exists(to) ? CopyAction.Change : CopyAction.Add);
        item.FlaggedFile = pairs.Any(p => SecretDetector.FileLooksSecret(p.Source));
        item.Detail = item.Action == CopyAction.Change ? "Exists in the target. Choose skip, overwrite or rename when ticked." : "";
        item.Ticked = item.Action == CopyAction.Add && !item.FlaggedFile;
        return item;
    }

    /// Every file under a skill folder. A symbolic link or junction is followed, so the target never
    /// depends on the source. A link back to a parent folder is skipped, and so is a broken link.
    static List<FilePair> Expand(string from, string to, HashSet<string>? parents = null)
    {
        string real;
        try
        {
            FileSystemInfo info = Directory.Exists(from) ? new DirectoryInfo(from) : new FileInfo(from);
            if (info.LinkTarget != null) info = info.ResolveLinkTarget(returnFinalTarget: true) ?? info;
            real = info.FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
        if (Directory.Exists(real))
        {
            var canon = PathCanon.Canonical(real);
            if (parents?.Contains(canon) == true) return [];
            var inner = new HashSet<string>(parents ?? [], StringComparer.OrdinalIgnoreCase) { canon };
            var out_ = new List<FilePair>();
            IEnumerable<string> names;
            try { names = Directory.EnumerateFileSystemEntries(real).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
            foreach (var name in names.Where(n => !Skipped.Contains(n, StringComparer.OrdinalIgnoreCase)))
                out_.AddRange(Expand(Path.Combine(real, name), Path.Combine(to, name), inner));
            return out_;
        }
        if (File.Exists(real) && !File.GetAttributes(real).HasFlag(FileAttributes.Device)) return [new FilePair(real, to)];
        return [];
    }

    List<FilePair> FilePairs(CopyItem item, Profile source, Profile target)
    {
        var rel = item.Key.Replace('/', '\\');
        var to = Path.Combine(locator.ConfigDir(target), rel);
        if (item.Conflict == CopyConflict.Rename && item.Action == CopyAction.Change) to = Renamed(to);
        return Expand(Path.Combine(locator.ConfigDir(source), rel), to);
    }

    /// Adds -copy before the extension, then a number, so a rename never overwrites anything.
    internal static string Renamed(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var ext = Path.GetExtension(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(dir, (n == 1 ? $"{stem}-copy" : $"{stem}-copy-{n}") + ext);
            if (!Exists(candidate)) return candidate;
        }
    }

    /// A read-only file is made writable first; its backup records that, and Restore puts it back.
    static void WriteOver(string path, byte[] data)
    {
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        AtomicFile.Write(path, data);
    }

    // ---- Apply

    public ApplyResult Apply(CopyPlan plan)
    {
        var reg = store.Load();
        var source = reg.ProfileById(plan.SourceId);
        var target = reg.ProfileById(plan.TargetId);
        if (source == null || target == null) throw ProfileException.NotFound(plan.TargetId);
        // Everything is checked again, because the preview may be minutes old.
        if (target.Builtin) throw new ProfileException(BuiltinRefusal);
        if (BlockedReason(target) is { } reason) throw new ProfileException(reason);

        var chosen = plan.Items.Where(i => i.Ticked && i.Action != CopyAction.Same).ToList();
        var fileItems = chosen.Where(i => i.IsFile && !(i.Action == CopyAction.Change && i.Conflict == CopyConflict.Skip)).ToList();
        var pairs = fileItems.ToDictionary(i => i.Id, i => FilePairs(i, source, target));
        var settingsItems = chosen.Where(i => i.Kind is CopyKind.Permissions or CopyKind.Hook or CopyKind.Model or CopyKind.Env).ToList();
        var settingsPath = SettingsFile(target);

        var dests = pairs.Values.SelectMany(p => p).Select(p => p.Dest).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, hash) in plan.BeforeHashes)
        {
            var used = dests.Contains(path) || (settingsItems.Count > 0 && string.Equals(path, settingsPath, StringComparison.OrdinalIgnoreCase));
            if (used && BackupStore.Hash(path) != hash)
                throw new ProfileException($"{Path.GetFileName(path)} changed since the preview. Nothing was copied. Look at the preview again.");
        }

        var touched = dests.ToList();
        if (settingsItems.Count > 0) touched.Add(settingsPath);
        var (backupId, manifest) = Backups.Begin(target.Id, touched);

        var done = new List<string>();
        var failed = new List<CopyFailure>();

        // Direct writes first. A failure stops everything after it, and keeps the backup.
        try
        {
            if (settingsItems.Count > 0)
            {
                var merged = JsonTree.Read(settingsPath);
                var src = JsonTree.Read(SettingsFile(source));
                foreach (var item in settingsItems) Merge(item, src, merged);
                WriteOver(settingsPath, JsonTree.Pretty(merged));
                done.AddRange(settingsItems.Select(i => i.Id));
            }
            foreach (var item in fileItems)
            {
                foreach (var pair in pairs[item.Id]) WriteOver(pair.Dest, File.ReadAllBytes(pair.Source));
                done.Add(item.Id);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ProfileException)
        {
            failed.Add(new CopyFailure("files", e.Message));
        }

        // Then Claude Code's own commands, which Restore cannot undo.
        var removals = new List<string>();
        if (failed.Count == 0)
        {
            var sourceSettings = JsonTree.Read(SettingsFile(source));
            var servers = McpServers(source);
            foreach (var item in chosen.Where(i => i.Kind is CopyKind.McpServer or CopyKind.Plugin))
            {
                var why = item.Kind == CopyKind.McpServer
                    ? InstallMcp(item, servers.GetValueOrDefault(item.Key) ?? [], target)
                    : InstallPlugin(item, sourceSettings, source, target);
                if (why != null)
                {
                    failed.Add(new CopyFailure(item.Id, why));
                    break;
                }
                done.Add(item.Id);
                if (item.RemovalCommand is { } c) removals.Add(c);
            }
        }
        // After the commands, because a plugin install rewrites settings.json, and Restore would
        // otherwise take that for later work and leave the file alone.
        foreach (var e in manifest.Entries) e.After = BackupStore.Hash(e.Path);
        manifest.RemovalCommands = removals;
        Backups.Save(manifest, backupId);
        new EventLog(locator).Write("copy", target.Id, $"{done.Count} items from {source.Id}, {failed.Count} failed, backup {backupId}");
        return new ApplyResult(done, failed, backupId, removals);
    }

    static void Merge(CopyItem item, JsonObject src, JsonObject merged)
    {
        switch (item.Kind)
        {
            case CopyKind.Permissions:
                var key = item.Key["permissions.".Length..];
                SettingsMerger.AddPermissions(SettingsMerger.NewPermissions(src, merged, key), key, merged);
                break;
            case CopyKind.Hook:
                var entries = SettingsMerger.HookEntries(src);
                for (var i = 0; i < entries.Count; i++)
                    if (item.Id == HookId(i, entries[i]))
                        SettingsMerger.AddHooks(SettingsMerger.NewHooks(entries[i], merged), entries[i].Event, entries[i].Matcher, merged);
                break;
            case CopyKind.Model:
                merged["model"] = src["model"]?.DeepClone();
                break;
            case CopyKind.Env:
                var name = item.Key["env.".Length..];
                if (merged["env"] is not JsonObject env) merged["env"] = env = [];
                env[name] = src["env"]?[name]?.DeepClone();
                break;
        }
    }

    static string Output(ProcessResult r) => (r.Stderr.Length > 0 ? r.Stderr : r.Stdout).Trim();

    /// Returns a reason if it failed.
    string? InstallMcp(CopyItem item, JsonObject server, Profile target)
    {
        var s = (JsonObject)server.DeepClone();
        foreach (var field in item.Secrets.Where(f => !f.Ticked))
        {
            if (field.IsArgument) return "It has a secret in its arguments, which was left out, so the server was not copied.";
            var parts = field.Name.Split('.', 2);
            if (parts.Length == 2 && s[parts[0]] is JsonObject group) group.Remove(parts[1]);
        }
        var json = JsonTree.Canonical(s);
        // Claude Code will not add over an existing name, and its old entry is in no backup.
        var old = item.Action == CopyAction.Change ? McpServers(target).GetValueOrDefault(item.Key) : null;
        if (item.Action == CopyAction.Change)
        {
            var rm = Cli.RemoveMcp(item.Key, target);
            if (rm.ExitCode != 0) return $"Could not replace it: {Output(rm)}";
        }
        var r = Cli.AddMcp(item.Key, json, target);
        if (r.ExitCode == 0) return null;
        var reason = Output(r);
        if (item.Action != CopyAction.Change) return reason;
        if (old != null && Cli.AddMcp(item.Key, JsonTree.Canonical(old), target).ExitCode == 0) return $"{reason} Its old entry was put back.";
        return $"{reason} Its old entry was removed and could not be put back, so add it again in that profile.";
    }

    string? InstallPlugin(CopyItem item, JsonObject sourceSettings, Profile source, Profile target)
    {
        var at = item.Key.IndexOf('@');
        if (at >= 0 && MarketplaceSource(item.Key[(at + 1)..], sourceSettings, source) is { } origin)
        {
            var m = Cli.AddMarketplace(origin, target);
            // Already added is fine, and the install below is the real test.
            if (m.ExitCode != 0 && !(m.Stdout + m.Stderr).Contains("already", StringComparison.OrdinalIgnoreCase)) return Output(m);
        }
        var r = Cli.InstallPlugin(item.Key, target);
        return r.ExitCode == 0 ? null : Output(r);
    }

    /// Reads only where the marketplace comes from. The registry itself is never copied.
    string? MarketplaceSource(string name, JsonObject settings, Profile source)
    {
        static string? Origin(JsonNode? entry) =>
            entry?["source"] is JsonObject s ? JsonTree.Str(s["repo"]) ?? JsonTree.Str(s["url"]) ?? JsonTree.Str(s["path"]) : null;
        if (Origin((settings["extraKnownMarketplaces"] as JsonObject)?[name]) is { } o) return o;
        var known = JsonTree.Read(Path.Combine(locator.ConfigDir(source), "plugins", "known_marketplaces.json"));
        return Origin(known[name]);
    }

    // ---- Restore

    public RestoreResult Restore(Profile target, string backupId)
    {
        if (target.Builtin) throw new ProfileException("The built-in profile is never written to.");
        if (Live.HasLiveSession(target)) throw new ProfileException($"{target.DisplayName} has a live session. Stop it first.");
        var m = Backups.Load(target.Id, backupId);
        var result = new RestoreResult([], [], [], m.RemovalCommands);
        var root = locator.ConfigDir(target);
        foreach (var e in m.Entries)
        {
            // Only files inside the profile's own folder, whatever the manifest says.
            if (!PathCanon.IsInside(e.Path, root)) continue;
            var now = BackupStore.Hash(e.Path);
            if (now != e.After)
            {
                // Already back to how it was: nothing to do, and nothing to warn about.
                if (now != e.Before) result.LeftAlone.Add(e.Path);
                continue;
            }
            if (e.ExistedBefore && Backups.BackupFile(e, target.Id, backupId) is { } backup)
            {
                WriteOver(e.Path, File.ReadAllBytes(backup));
                if (e.ReadOnly) File.SetAttributes(e.Path, File.GetAttributes(e.Path) | FileAttributes.ReadOnly);
                result.Restored.Add(e.Path);
            }
            else
            {
                File.Delete(e.Path);
                result.Removed.Add(e.Path);
                RemoveEmptyParents(e.Path, root);
            }
        }
        new EventLog(locator).Write("restore", target.Id,
            $"{result.Restored.Count} restored, {result.Removed.Count} removed, {result.LeftAlone.Count} left alone, backup {backupId}");
        return result;
    }

    static void RemoveEmptyParents(string file, string stop)
    {
        var dir = Path.GetDirectoryName(file);
        while (dir != null && PathCanon.IsInside(dir, stop) && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            try { Directory.Delete(dir); } catch (IOException) { return; }
            dir = Path.GetDirectoryName(dir);
        }
    }
}
