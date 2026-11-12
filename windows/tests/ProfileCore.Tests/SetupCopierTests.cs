using System.Diagnostics;
using System.Text.Json.Nodes;
using ProfileCore;

namespace ProfileCore.Tests;

[TestClass]
public sealed class SecretDetectorTests
{
    [TestMethod]
    public void CatchesTokenShapes()
    {
        foreach (var s in new[] { "sk-abcdefghijklmnopqrstuvwx", "x sk-ant-api03-abcdefghijklmnopqrst y", "ghp_abcdefghijklmnopqrstuvwxyz012345",
                                  "xoxb-123-abc", string.Concat(Enumerable.Repeat("a1", 25)), "QWxhZGRpbjpvcGVuIHNlc2FtZQQWxhZGRpbjpvcGVuIHNlc2FtZQ==" })
            Assert.IsTrue(SecretDetector.LooksSecret(s), s);
    }

    [TestMethod]
    public void OrdinaryTextIsNotASecret()
    {
        foreach (var s in new[] { "author", "monkey", "auth token key password", "a skill about tokens", "sk-short", "--verbose",
                                  @"C:\Program Files\nodejs\node.exe", "@scope/some-package", "https://example.com/mcp", "/c", "npx",
                                  @"C:\Users\me\Documents\Projects\a\very\long\path\that\goes\on\and\on\here",
                                  "thisisalongwordwithnodigitsoruppercaselettersinitatallreally",
                                  "task-abcdefghijklmnopqrstuvwxyz", "disk-usage-analyzer-plugin-for-claude", new string('a', 39) })
            Assert.IsFalse(SecretDetector.LooksSecret(s), s);
    }
}

[TestClass]
public sealed class SettingsMergerTests
{
    static JsonObject J(string text) => (JsonObject)JsonNode.Parse(text)!;

    [TestMethod]
    public void PermissionsCombineWithNoDuplicates()
    {
        var t = J("""{"permissions":{"allow":["a"]},"other":1}""");
        var s = J("""{"permissions":{"allow":["a","b","b"]}}""");
        var added = SettingsMerger.NewPermissions(s, t, "allow");
        CollectionAssert.AreEqual(new[] { "b" }, added);
        SettingsMerger.AddPermissions(added, "allow", t);
        Assert.AreEqual("""{"other":1,"permissions":{"allow":["a","b"]}}""", JsonTree.Canonical(t));
    }

    [TestMethod]
    public void HooksMergeByEventAndMatcherWithoutDuplicates()
    {
        var t = J("""{"hooks":{"Stop":[{"matcher":"m","hooks":[{"type":"command","command":"x"}]}]}}""");
        var s = J("""{"hooks":{"Stop":[{"matcher":"m","hooks":[{"command":"x","type":"command"},{"type":"command","command":"y"}]}]}}""");
        var added = SettingsMerger.NewHooks(SettingsMerger.HookEntries(s)[0], t);
        Assert.HasCount(1, added, "the same hook with its keys in another order is not added twice");
        SettingsMerger.AddHooks(added, "Stop", "m", t);
        var e = SettingsMerger.HookEntries(t);
        Assert.HasCount(1, e);
        Assert.HasCount(2, (JsonArray)e[0].Entry["hooks"]!);
        SettingsMerger.AddHooks([J("""{"type":"command","command":"z"}""")], "Start", "", t);
        Assert.HasCount(2, SettingsMerger.HookEntries(t));
    }

    [TestMethod]
    public void CanonicalKeepsSlashesAndSortsKeys()
    {
        Assert.AreEqual("""{"a":"/x/y","b":"C:\\x é"}""", JsonTree.Canonical(J("""{"b":"C:\\x é","a":"/x/y"}""")));
    }

    [TestMethod]
    public void UnexpectedShapesAreRefused()
    {
        using var s = new Sandbox();
        foreach (var (json, ok) in new[] { ("{}", true), ("""{"permissions":{"allow":["x"]},"env":{"A":"1"},"model":"m"}""", true),
                                           ("""{"permissions":"x"}""", false), ("""{"permissions":{"allow":[1]}}""", false),
                                           ("""{"env":{"A":1}}""", false), ("""{"hooks":[]}""", false), ("[]", false), ("not json", false) })
        {
            s.Write(json, "settings.json");
            Assert.AreEqual(ok, CompatCheck.CanWriteSettings(s.PathOf("settings.json")), json);
        }
        Assert.IsTrue(CompatCheck.CanWriteSettings(s.PathOf("missing.json")));
    }
}

/// A built-in source and a signed-in target, with Claude Code's own commands faked.
sealed class CopyWorld : IDisposable
{
    public Sandbox S { get; } = new();
    public Profile Source { get; }
    public Profile Target { get; private set; }
    public FakeRunner Claude { get; } = new();
    public FakeProcesses Processes { get; } = new();
    public SetupCopier Copier { get; }
    public string SrcDir => S.Locator.ConfigDir(Source);
    public string DstDir => S.Locator.ConfigDir(Target);
    public List<string[]> Calls => Claude.Calls.Select(c => c.Args.ToArray()).ToList();

    public CopyWorld()
    {
        S.Write("""{"oauthAccount":{"accountUuid":"u1","emailAddress":"a@example.com"},"mcpServers":{}}""", ".claude.json");
        Source = S.Store.DefaultProfile();
        var t = S.Store.Create("Target", "target", "#000000");
        S.SignIn(t, "u2", "b@example.com");
        S.Store.CompleteLogin(t.Id, new Account("u2", "b@example.com", null));
        Target = S.Store.Load().ProfileById(t.Id)!;
        var bin = S.PathOf("realbin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "claude.exe"), "");
        var cli = new ClaudeCLI(S.Locator, Claude, new ClaudeBinaryLocator(S.Locator, bin));
        Copier = new SetupCopier(S.Store, cli, new LiveSessionReader(S.Locator, Processes));
    }

    public void SourceSettings(string json) => S.Write(json, ".claude/settings.json");
    public void SourceConfig(string mcp) =>
        S.Write($$"""{"oauthAccount":{"accountUuid":"u1"},"primaryApiKey":"sk-never-copied-abcdefghijkl","hasTrustDialogAccepted":true,"mcpServers":{{mcp}}}""", ".claude.json");
    public void SourceFile(string path, string text) => S.Write(text, ".claude/" + path);
    public void TargetFile(string path, string text)
    {
        var p = Path.Combine(DstDir, path.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }
    public string TargetPath(string path) => Path.Combine(DstDir, path.Replace('/', '\\'));
    public string? TargetText(string path) => File.Exists(TargetPath(path)) ? File.ReadAllText(TargetPath(path)) : null;
    public JsonObject TargetSettings() => JsonTree.Read(TargetPath("settings.json"));
    public CopyPlan Plan() => Copier.Plan(Source, Target);
    public static CopyItem? Item(CopyPlan p, string id) => p.Items.FirstOrDefault(i => i.Id == id);
    public static CopyItem Tick(CopyPlan p, string id, bool v = true)
    {
        var i = Item(p, id) ?? throw new AssertFailedException($"No item {id}");
        i.Ticked = v;
        return i;
    }

    /// Fails each Claude Code command whose arguments contain the text; once, if asked.
    public void FailOn(string text, bool once = false)
    {
        var armed = true;
        Claude.Handler = (_, args) =>
        {
            if (armed && string.Join(' ', args).Contains(text, StringComparison.Ordinal))
            {
                if (once) armed = false;
                return new ProcessResult(Stderr: "boom", ExitCode: 1);
            }
            return new ProcessResult();
        };
    }

    public void Dispose() => S.Dispose();
}

[TestClass]
public sealed class SetupCopierTests
{
    [TestMethod]
    public void TheBuiltinProfileIsNeverATarget()
    {
        using var w = new CopyWorld();
        Assert.ThrowsExactly<ProfileException>(() => w.Copier.Plan(w.Target, w.Source));
        var p = w.Plan();
        p.TargetId = w.Source.Id;
        Assert.ThrowsExactly<ProfileException>(() => w.Copier.Apply(p));
    }

    /// Its old entry is in no backup, so a failed add must not lose it.
    [TestMethod]
    public void AFailedMcpChangePutsTheOldEntryBack()
    {
        using var w = new CopyWorld();
        w.SourceConfig("""{"srv":{"command":"new"}}""");
        w.TargetFile(".claude.json", """{"oauthAccount":{"accountUuid":"u2"},"mcpServers":{"srv":{"command":"old"}}}""");
        var p = w.Plan();
        Assert.AreEqual(CopyAction.Change, CopyWorld.Item(p, "mcp:srv")?.Action);
        CopyWorld.Tick(p, "mcp:srv");
        w.FailOn("add-json", once: true);
        var r = w.Copier.Apply(p);
        StringAssert.Contains(r.Failed[0].Reason, "old entry was put back");
        var adds = w.Calls.Where(c => c.Contains("add-json")).ToList();
        Assert.HasCount(2, adds);
        StringAssert.Contains(string.Join(' ', adds[1]), "old");
    }

    [TestMethod]
    public void APluginInstallDoesNotBlockRestoringSettings()
    {
        using var w = new CopyWorld();
        w.SourceSettings("""{"model":"m","enabledPlugins":{"tool@market":true}}""");
        var p = w.Plan();
        CopyWorld.Tick(p, "model");
        CopyWorld.Tick(p, "plugin:tool@market");
        w.Claude.Handler = (_, args) =>
        {
            if (args.Take(2).SequenceEqual(["plugin", "install"])) File.WriteAllText(w.TargetPath("settings.json"), """{"model":"m","enabledPlugins":{"tool@market":true}}""");
            return new ProcessResult();
        };
        var r = w.Copier.Apply(p);
        Assert.IsEmpty(r.Failed);
        var restored = w.Copier.Restore(w.Target, r.BackupId!);
        Assert.IsEmpty(restored.LeftAlone);
        Assert.DoesNotContain("\"model\"", w.TargetText("settings.json") ?? "");
    }

    [TestMethod]
    public void ALinkLoopInASkillDoesNotHangThePreview()
    {
        using var w = new CopyWorld();
        w.SourceFile("skills/loop/SKILL.md", "hi");
        var dir = Path.Combine(w.SrcDir, "skills", "loop");
        Junction(Path.Combine(dir, "self"), dir);
        Junction(Path.Combine(dir, "broken"), Path.Combine(w.S.Home, "gone"));
        var p = w.Plan();
        CopyWorld.Tick(p, "file:skills/loop");
        Assert.IsEmpty(w.Copier.Apply(p).Failed);
        CollectionAssert.AreEqual(new[] { "SKILL.md" }, Directory.EnumerateFileSystemEntries(w.TargetPath("skills/loop")).Select(Path.GetFileName).ToArray());
    }

    /// A junction needs no special right, unlike a symbolic link. Removing it later leaves its target.
    static void Junction(string link, string target)
    {
        Directory.CreateDirectory(target);
        using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/d /c mklink /J \"{link}\" \"{target}\"")
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        p.WaitForExit();
        Assert.AreEqual(0, p.ExitCode, "mklink /J");
        if (target.EndsWith("gone", StringComparison.Ordinal)) Directory.Delete(target);
    }

    [TestMethod]
    public void AJunctionedSkillIsCopiedNotLinked()
    {
        using var w = new CopyWorld();
        w.S.Write("real", "elsewhere/SKILL.md");
        Directory.CreateDirectory(Path.Combine(w.SrcDir, "skills"));
        Junction(Path.Combine(w.SrcDir, "skills", "linked"), w.S.PathOf("elsewhere"));
        w.Copier.Apply(w.Plan());
        Assert.AreEqual("real", w.TargetText("skills/linked/SKILL.md"));
        Assert.IsNull(new DirectoryInfo(w.TargetPath("skills/linked")).LinkTarget, "a real folder, not a link");
    }

    [TestMethod]
    public void AccountSyncedSkillsAreNeverOffered()
    {
        using var w = new CopyWorld();
        w.SourceFile("skills/synced/abc/SKILL.md", "from the account");
        w.SourceFile("skills/mine/SKILL.md", "mine");
        var p = w.Plan();
        Assert.IsNull(CopyWorld.Item(p, "file:skills/synced"));
        Assert.IsNotNull(CopyWorld.Item(p, "file:skills/mine"));
    }

    [TestMethod]
    public void AFileCreatedSinceThePreviewStopsTheCopy()
    {
        using var w = new CopyWorld();
        w.SourceFile("CLAUDE.md", "new");
        var p = w.Plan();
        w.TargetFile("CLAUDE.md", "written after the preview");
        Assert.ThrowsExactly<ProfileException>(() => w.Copier.Apply(p));
        Assert.AreEqual("written after the preview", w.TargetText("CLAUDE.md"));
    }

    [TestMethod]
    public void ASecretInAUrlOrPermissionIsCaught()
    {
        using var w = new CopyWorld();
        w.SourceConfig("""{"srv":{"type":"http","url":"https://x.example/mcp?key=sk-abcdefghijklmnopqrstuvwx"}}""");
        w.SourceSettings("""{"permissions":{"allow":["Bash(curl -H 'x: sk-abcdefghijklmnopqrstuvwx')"]}}""");
        var p = w.Plan();
        Assert.IsFalse(CopyWorld.Item(p, "mcp:srv")!.Ticked);
        Assert.AreEqual("url", CopyWorld.Item(p, "mcp:srv")!.Secrets[0].Name);
        Assert.IsFalse(CopyWorld.Item(p, "perm:allow")!.Ticked);
        Assert.IsTrue(CopyWorld.Item(p, "perm:allow")!.FlaggedFile);
    }

    [TestMethod]
    public void DefaultsMatchTheTable()
    {
        using var w = new CopyWorld();
        w.SourceSettings("""{"model":"opus","env":{"A":"1"},"permissions":{"allow":["x"],"deny":["y"]},"hooks":{"Stop":[{"hooks":[{"type":"command","command":"z"}]}]},"enabledPlugins":{"a@m":true,"off@m":false},"statusLine":{"type":"command","command":"s"},"apiKeyHelper":"k","theme":"dark"}""");
        w.SourceConfig("""{"plain":{"command":"node"},"withenv":{"command":"node","env":{"T":"v"}}}""");
        w.SourceFile("skills/s1/SKILL.md", "hello");
        w.SourceFile("agents/a.md", "agent");
        w.SourceFile("commands/c.md", "cmd");
        w.SourceFile("CLAUDE.md", "memory");
        w.SourceFile("sessions/1.json", "{}");
        w.SourceFile("projects/p/t.jsonl", "{}");
        w.SourceFile(".credentials.json", "{}");
        var p = w.Plan();
        bool? T(string id) => CopyWorld.Item(p, id)?.Ticked;
        Assert.IsTrue(T("mcp:plain"));
        Assert.IsFalse(T("mcp:withenv"));
        Assert.IsTrue(T("perm:allow") == true && T("perm:deny") == true);
        Assert.IsFalse(p.Items.First(i => i.Kind == CopyKind.Hook).Ticked);
        Assert.IsTrue(T("model") == false && T("env:A") == false);
        Assert.IsTrue(T("plugin:a@m") == true && T("plugin:off@m") == null);
        Assert.IsTrue(T("file:skills/s1") == true && T("file:agents/a.md") == true && T("file:commands/c.md") == true && T("file:CLAUDE.md") == true);
        foreach (var never in new[] { "statusLine", "apiKeyHelper", "theme", "sessions", "projects", "oauthAccount", "hasTrustDialogAccepted", "primaryApiKey", "credentials" })
            Assert.IsFalse(p.Items.Any(i => i.Id.Contains(never, StringComparison.OrdinalIgnoreCase)), $"{never} must not be offered");
    }

    [TestMethod]
    public void DirectWritesMatchThePreviewAndAreMinimal()
    {
        using var w = new CopyWorld();
        w.SourceSettings("""{"permissions":{"allow":["x"]},"model":"opus","statusLine":{"type":"command","command":"source-label"}}""");
        w.SourceFile("skills/s1/SKILL.md", "hello");
        w.SourceFile("skills/s1/more/x.txt", "nested");
        var before = w.TargetSettings();
        var r = w.Copier.Apply(w.Plan());
        Assert.IsEmpty(r.Failed);
        var after = w.TargetSettings();
        Assert.AreEqual("""["x"]""", JsonTree.Canonical(after["permissions"]!["allow"]));
        Assert.IsNull(after["model"], "unticked items are not written");
        Assert.AreEqual(JsonTree.Canonical(before["statusLine"]), JsonTree.Canonical(after["statusLine"]), "the target keeps its own status line");
        Assert.IsNotNull(after["statusLine"]);
        Assert.AreEqual("hello", w.TargetText("skills/s1/SKILL.md"));
        Assert.AreEqual("nested", w.TargetText("skills/s1/more/x.txt"));
    }

    [TestMethod]
    public void ConflictsSkipOverwriteAndRename()
    {
        using var w = new CopyWorld();
        w.SourceFile("CLAUDE.md", "new");
        w.TargetFile("CLAUDE.md", "old");
        var p = w.Plan();
        Assert.AreEqual(CopyAction.Change, CopyWorld.Item(p, "file:CLAUDE.md")!.Action);
        Assert.IsFalse(CopyWorld.Item(p, "file:CLAUDE.md")!.Ticked);
        CopyWorld.Tick(p, "file:CLAUDE.md").Conflict = CopyConflict.Skip;
        w.Copier.Apply(p);
        Assert.AreEqual("old", w.TargetText("CLAUDE.md"));

        p = w.Plan();
        CopyWorld.Tick(p, "file:CLAUDE.md").Conflict = CopyConflict.Rename;
        w.Copier.Apply(p);
        Assert.AreEqual("old", w.TargetText("CLAUDE.md"));
        Assert.AreEqual("new", w.TargetText("CLAUDE-copy.md"));

        p = w.Plan();
        CopyWorld.Tick(p, "file:CLAUDE.md").Conflict = CopyConflict.Rename;
        w.Copier.Apply(p);
        Assert.AreEqual("new", w.TargetText("CLAUDE-copy-2.md"), "a rename never overwrites");

        p = w.Plan();
        CopyWorld.Tick(p, "file:CLAUDE.md").Conflict = CopyConflict.Overwrite;
        w.Copier.Apply(p);
        Assert.AreEqual("new", w.TargetText("CLAUDE.md"));
    }

    [TestMethod]
    public void SecretsInEnvAndHeadersAreDroppedUnlessTicked()
    {
        using var w = new CopyWorld();
        w.SourceConfig("""{"srv":{"command":"node","env":{"TOKEN":"sekret-value","PLAIN":"x"},"headers":{"Authorization":"Bearer z"}}}""");
        var p = w.Plan();
        var item = CopyWorld.Item(p, "mcp:srv")!;
        Assert.IsFalse(item.Ticked);
        CollectionAssert.AreEquivalent(new[] { "env.TOKEN", "env.PLAIN", "headers.Authorization" }, item.Secrets.Select(f => f.Name).ToArray());
        Assert.DoesNotContain("sekret", item.Detail, "the preview never shows a value");

        item.Ticked = true;
        item.Secrets.First(f => f.Name == "env.PLAIN").Ticked = true;
        w.Copier.Apply(p);
        var call = w.Calls.First(c => c.Take(2).SequenceEqual(["mcp", "add-json"]));
        StringAssert.Contains(call[3], "PLAIN");
        Assert.DoesNotContain("sekret-value", call[3]);
        Assert.DoesNotContain("Bearer", call[3]);
        CollectionAssert.AreEqual(new[] { "--scope", "user" }, call[^2..]);
    }

    [TestMethod]
    public void ASecretInArgsBlocksThatServerUntilTicked()
    {
        using var w = new CopyWorld();
        w.SourceConfig("""{"srv":{"command":"cmd","args":["/c","npx","tool","--key","sk-abcdefghijklmnopqrstuvwx"]}}""");
        var p = w.Plan();
        Assert.AreEqual("args[4]", CopyWorld.Item(p, "mcp:srv")!.Secrets.Single().Name, "arguments after cmd /c are scanned");
        CopyWorld.Tick(p, "mcp:srv");
        var r = w.Copier.Apply(p);
        Assert.AreEqual("mcp:srv", r.Failed[0].ItemId);
        Assert.IsFalse(w.Calls.Any(c => c.Contains("add-json")));
        p = w.Plan();
        CopyWorld.Tick(p, "mcp:srv").Secrets[0].Ticked = true;
        Assert.IsEmpty(w.Copier.Apply(p).Failed);
        Assert.IsTrue(w.Calls.Any(c => c.Contains("add-json")));
    }

    [TestMethod]
    public void AChangedServerIsReplacedAndASameOneIsLeft()
    {
        using var w = new CopyWorld();
        w.SourceConfig("""{"srv":{"command":"new"},"same":{"command":"s"}}""");
        w.TargetFile(".claude.json", """{"oauthAccount":{"accountUuid":"u2"},"mcpServers":{"srv":{"command":"old"},"same":{"command":"s"}}}""");
        var p = w.Plan();
        Assert.AreEqual(CopyAction.Change, CopyWorld.Item(p, "mcp:srv")!.Action);
        Assert.AreEqual(CopyAction.Same, CopyWorld.Item(p, "mcp:same")!.Action);
        Assert.IsFalse(CopyWorld.Item(p, "mcp:same")!.Ticked);
        w.Copier.Apply(p);
        CollectionAssert.AreEqual(new[] { "mcp remove", "mcp add-json" }, w.Calls.Select(c => string.Join(' ', c.Take(2))).ToArray());
    }

    /// Windows spellings of the same folder: either slash, any case, the 8.3 short form, through the
    /// home folder. A sibling folder whose name starts the same does not count.
    [TestMethod]
    public void ACommandIntoTheSourceFolderIsFlaggedHoweverItIsSpelled()
    {
        using var w = new CopyWorld();
        var dir = w.SrcDir;
        var shortHome = ShortPath(w.S.Home);
        string[] spellings = [dir + @"\tool.cmd", dir.Replace('\\', '/').ToUpperInvariant() + "/tool.cmd", @"%USERPROFILE%\.claude\tool.cmd", "~/.claude/x"];
        if (shortHome != null && shortHome != w.S.Home) spellings = [.. spellings, Path.Combine(shortHome, ".claude", "tool.cmd")];
        foreach (var s in spellings)
        {
            w.SourceConfig("""{"srv":{"command":"cmd","args":["/c",""" + Q(s) + "]}}");
            var item = CopyWorld.Item(w.Plan(), "mcp:srv")!;
            Assert.IsTrue(item.PointsIntoSource, s);
            Assert.IsFalse(item.Ticked, s);
        }
        w.SourceConfig("""{"srv":{"command":""" + Q(Path.Combine(w.S.Home, ".claude-profiles", "bin", "x.exe")) + "}}");
        Assert.IsFalse(CopyWorld.Item(w.Plan(), "mcp:srv")!.PointsIntoSource, ".claude-profiles is not .claude");

        w.SourceSettings("""{"hooks":{"Stop":[{"hooks":[{"type":"command","command":""" + Q($"powershell -File \"{dir}\\hook.ps1\"") + "}]}]}}");
        Assert.IsTrue(w.Plan().Items.First(i => i.Kind == CopyKind.Hook).PointsIntoSource);
    }

    static string Q(string s) => JsonValue.Create(s).ToJsonString();

    static string? ShortPath(string p)
    {
        using var proc = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/d /c for %I in (\"{p}\") do @echo %~sI")
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        var s = proc.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit();
        return s.Length > 0 ? s : null;
    }

    [TestMethod]
    public void ASecretLookingFileIsFlaggedAndUnticked()
    {
        using var w = new CopyWorld();
        w.SourceFile("skills/bad/SKILL.md", "use sk-abcdefghijklmnopqrstuvwx here");
        w.SourceFile("skills/ok/SKILL.md", "about auth tokens and keys");
        var p = w.Plan();
        Assert.IsTrue(CopyWorld.Item(p, "file:skills/bad")!.FlaggedFile);
        Assert.IsFalse(CopyWorld.Item(p, "file:skills/bad")!.Ticked);
        Assert.IsFalse(CopyWorld.Item(p, "file:skills/ok")!.FlaggedFile);
        Assert.IsTrue(CopyWorld.Item(p, "file:skills/ok")!.Ticked);
    }

    static readonly DateTime Start = new(2026, 10, 31, 10, 58, 34, DateTimeKind.Utc);

    [TestMethod]
    public void ALiveSessionBlocksIncludingADesktopOne()
    {
        using var w = new CopyWorld();
        w.Processes.Running[5] = Start;
        w.TargetFile("sessions/5.json", $$"""{"pid":5,"sessionId":"x","procStart":"{{Start.ToFileTimeUtc()}}","entrypoint":"claude-desktop"}""");
        w.SourceFile("CLAUDE.md", "x");
        var p = w.Plan();
        StringAssert.Contains(p.BlockedReason, "live session");
        Assert.ThrowsExactly<ProfileException>(() => w.Copier.Apply(p));
        Assert.IsNull(w.TargetText("CLAUDE.md"));
    }

    [TestMethod]
    public void AFileChangedSinceThePreviewStopsEverything()
    {
        using var w = new CopyWorld();
        w.SourceSettings("""{"permissions":{"allow":["x"]}}""");
        w.SourceFile("CLAUDE.md", "new");
        var p = w.Plan();
        w.TargetFile("settings.json", """{"changed":true}""");
        Assert.ThrowsExactly<ProfileException>(() => w.Copier.Apply(p));
        Assert.IsNull(w.TargetText("CLAUDE.md"), "nothing is written");
        Assert.AreEqual("""{"changed":true}""", w.TargetText("settings.json"));
    }

    [TestMethod]
    public void AWrongShapeRefusesToWrite()
    {
        using var w = new CopyWorld();
        w.TargetFile("settings.json", """{"permissions":"nonsense"}""");
        StringAssert.Contains(w.Plan().BlockedReason, "unexpected shape");
    }

    [TestMethod]
    public void AFailureStopsAndReportsWhatWasDone()
    {
        using var w = new CopyWorld();
        w.SourceConfig("""{"a":{"command":"1"},"b":{"command":"2"},"c":{"command":"3"}}""");
        w.SourceFile("CLAUDE.md", "memory");
        w.FailOn("add-json b");
        var r = w.Copier.Apply(w.Plan());
        CollectionAssert.IsSubsetOf(new[] { "mcp:a", "file:CLAUDE.md" }, r.Done);
        CollectionAssert.AreEqual(new[] { new CopyFailure("mcp:b", "boom") }, r.Failed);
        CollectionAssert.DoesNotContain(r.Done, "mcp:c", "nothing after a failure runs");
        Assert.IsNotNull(r.BackupId);
    }

    [TestMethod]
    public void PluginsInstallWithTheirMarketplace()
    {
        using var w = new CopyWorld();
        w.SourceSettings("""{"enabledPlugins":{"p@m":true},"extraKnownMarketplaces":{"m":{"source":{"source":"github","repo":"org/repo"}}}}""");
        var r = w.Copier.Apply(w.Plan());
        Assert.IsEmpty(r.Failed);
        CollectionAssert.AreEqual(new[] { "plugin marketplace add org/repo", "plugin install p@m" }, w.Calls.Select(c => string.Join(' ', c)).ToArray());
        Assert.AreEqual("claude-as target plugin uninstall p@m", r.RemovalCommands.Single());
        Assert.IsFalse(File.Exists(w.TargetPath("plugins/installed_plugins.json")), "SetupCopier never writes a plugin registry itself");
    }

    [TestMethod]
    public void RemovalCommandsWorkInEitherShell()
    {
        using var w = new CopyWorld();
        w.SourceConfig("""{"my server":{"command":"node"}}""");
        Assert.AreEqual("claude-as target mcp remove \"my server\" --scope user", CopyWorld.Item(w.Plan(), "mcp:my server")!.RemovalCommand);
    }

    [TestMethod]
    public void RestoreUndoesDirectWritesWithReadOnly()
    {
        using var w = new CopyWorld();
        w.SourceSettings("""{"permissions":{"allow":["x"]}}""");
        w.SourceFile("skills/s1/SKILL.md", "hello");
        var settings = w.TargetPath("settings.json");
        var original = File.ReadAllBytes(settings);
        File.SetAttributes(settings, FileAttributes.ReadOnly);
        try
        {
            var r = w.Copier.Apply(w.Plan());
            Assert.IsEmpty(r.Failed);
            CollectionAssert.AreNotEqual(original, File.ReadAllBytes(settings));
            var res = w.Copier.Restore(w.Target, r.BackupId!);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(settings));
            Assert.IsTrue(File.GetAttributes(settings).HasFlag(FileAttributes.ReadOnly), "read-only comes back");
            Assert.HasCount(1, res.Removed);
            Assert.IsEmpty(res.LeftAlone);
            Assert.IsFalse(Directory.Exists(w.TargetPath("skills")), "empty folders it made are gone");
        }
        finally { File.SetAttributes(settings, FileAttributes.Normal); }
    }

    [TestMethod]
    public void RestoreLeavesLaterWorkAlone()
    {
        using var w = new CopyWorld();
        w.SourceFile("CLAUDE.md", "copied");
        w.SourceFile("agents/a.md", "agent");
        var r = w.Copier.Apply(w.Plan());
        w.TargetFile("CLAUDE.md", "edited later");
        var res = w.Copier.Restore(w.Target, r.BackupId!);
        Assert.AreEqual("edited later", w.TargetText("CLAUDE.md"), "a file changed since the copy is left alone");
        Assert.HasCount(1, res.LeftAlone);
        Assert.IsNull(w.TargetText("agents/a.md"));
    }

    [TestMethod]
    public void RestoreRefusesDuringALiveSession()
    {
        using var w = new CopyWorld();
        w.SourceFile("CLAUDE.md", "x");
        var r = w.Copier.Apply(w.Plan());
        w.TargetFile("sessions/5.json", $$"""{"pid":5,"sessionId":"x","procStart":"{{Start.ToFileTimeUtc()}}"}""");
        w.Processes.Running[5] = Start;
        Assert.ThrowsExactly<ProfileException>(() => w.Copier.Restore(w.Target, r.BackupId!));
        Assert.AreEqual("x", w.TargetText("CLAUDE.md"));
    }

    [TestMethod]
    public void RestoreTouchesOnlyTheProfilesOwnFolder()
    {
        using var w = new CopyWorld();
        w.SourceFile("CLAUDE.md", "x");
        var r = w.Copier.Apply(w.Plan());
        var manifest = Path.Combine(w.Copier.Backups.Folder(w.Target.Id), r.BackupId!, "manifest.json");
        var outside = w.S.PathOf("outside.txt");
        File.WriteAllText(outside, "keep");
        var hash = BackupStore.Hash(outside);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(w.TargetPath("CLAUDE.md").Replace("\\", "\\\\"), outside.Replace("\\", "\\\\"))
            .Replace(BackupStore.Hash(w.TargetPath("CLAUDE.md"))!, hash!));
        w.Copier.Restore(w.Target, r.BackupId!);
        Assert.AreEqual("keep", File.ReadAllText(outside));
    }

    [TestMethod]
    public void BackupsAreKeptInTheProfilesFolderAndTrimmed()
    {
        using var w = new CopyWorld();
        w.SourceSettings("""{"permissions":{"allow":["x"]}}""");
        var r = w.Copier.Apply(w.Plan());
        var dir = Path.Combine(w.S.Locator.ProfilesRoot, "backups", w.Target.Id, r.BackupId!);
        Assert.IsTrue(File.Exists(Path.Combine(dir, "manifest.json")));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "file-0")));
        var store = w.Copier.Backups;
        for (var i = 0; i < BackupStore.Keep + 3; i++)
        {
            var b = store.Begin(w.Target.Id, []);
            store.Save(b.Manifest, b.Id);
        }
        Assert.HasCount(BackupStore.Keep, store.List(w.Target.Id));
        StringAssert.Contains(File.ReadAllText(w.S.PathOf(".claude-profiles/logs/writes.log")), "\tcopy\t");
    }
}

/// Only runs with CPM_REAL_CLAUDE set to the folder holding the real claude.exe. Real `claude mcp`
/// commands, in throwaway folders: the target's CLAUDE_CONFIG_DIR is inside the test home.
[TestClass]
public sealed class RealClaudeCopyTests
{
    [TestMethod]
    public void RealMcpCommandsLandInTheTargetAndReplaceWorks()
    {
        if (Environment.GetEnvironmentVariable("CPM_REAL_CLAUDE") is not { Length: > 0 } path) { Assert.Inconclusive("Set CPM_REAL_CLAUDE to run this."); return; }
        using var s = new Sandbox();
        var exe = Path.Combine(Environment.SystemDirectory, "where.exe").Replace("\\", "\\\\");
        s.Write("""{"oauthAccount":{"accountUuid":"u1"},"mcpServers":{"t1":{"type":"stdio","command":""" + "\"" + exe + "\"" +
                ""","args":["/c","one"],"env":{"KEEP":"kept","DROP":"dropped"}}}}""", ".claude.json");
        var source = s.Store.DefaultProfile();
        var t = s.Store.Create("T", "t", "#000000");
        s.SignIn(t, "u2");
        s.Store.CompleteLogin(t.Id, new Account("u2", null, null));
        t = s.Store.Load().ProfileById(t.Id)!;
        var copier = new SetupCopier(s.Store, new ClaudeCLI(s.Locator, binary: new ClaudeBinaryLocator(s.Locator, path)));

        var p = copier.Plan(source, t);
        CopyWorld.Tick(p, "mcp:t1").Secrets.First(f => f.Name == "env.KEEP").Ticked = true;
        var r = copier.Apply(p);
        Assert.IsEmpty(r.Failed, string.Join("; ", r.Failed));

        JsonObject Servers()
        {
            var top = JsonTree.Read(s.Locator.ConfigFile(t));
            Assert.IsNotNull(top["oauthAccount"], "Claude Code's write kept the account");
            return top["mcpServers"] as JsonObject ?? [];
        }
        var servers = Servers();
        Assert.AreEqual(Path.Combine(Environment.SystemDirectory, "where.exe"), JsonTree.Str(servers["t1"]?["command"]));
        Assert.AreEqual("""{"KEEP":"kept"}""", JsonTree.Canonical(servers["t1"]?["env"]), "the unticked secret was dropped");
        CollectionAssert.AreEqual(new[] { "/c", "one" }, JsonTree.Strings(servers["t1"]?["args"]).ToArray(), "arguments survive the command line");

        // Changed in the source, so the target's copy is replaced.
        s.Write("""{"oauthAccount":{"accountUuid":"u1"},"mcpServers":{"t1":{"type":"stdio","command":"cmd","args":["/c","echo \"two words\""]}}}""", ".claude.json");
        p = copier.Plan(source, t);
        Assert.AreEqual(CopyAction.Change, CopyWorld.Item(p, "mcp:t1")!.Action);
        r = copier.Apply(p);
        Assert.IsEmpty(r.Failed, string.Join("; ", r.Failed));
        servers = Servers();
        Assert.AreEqual("cmd", JsonTree.Str(servers["t1"]?["command"]));
        CollectionAssert.AreEqual(new[] { "/c", "echo \"two words\"" }, JsonTree.Strings(servers["t1"]?["args"]).ToArray(), "quotes survive");
    }
}
