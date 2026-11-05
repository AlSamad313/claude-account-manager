using System.Text;
using ProfileCore;

namespace ProfileCore.Tests;

[TestClass]
public sealed class PowerShellProfileTests
{
    static (Sandbox S, PowerShellProfile P, string Docs) Make()
    {
        var s = new Sandbox();
        var docs = s.PathOf("Documents");
        return (s, new PowerShellProfile(s.Locator, docs), docs);
    }

    [TestMethod]
    public void WithNoProfileItAddsWindowsPowerShellsWithABom()
    {
        var (s, p, docs) = Make();
        using var _ = s;
        var changed = p.Add();
        var file = Path.Combine(docs, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
        CollectionAssert.AreEqual(new[] { file }, changed);
        Assert.IsFalse(Directory.Exists(Path.Combine(docs, "PowerShell")), "PowerShell 7 is not installed here");
        var bytes = File.ReadAllBytes(file);
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        StringAssert.Contains(Encoding.UTF8.GetString(bytes), PowerShellProfile.Line);
        Assert.IsTrue(p.IsAdded());
        Assert.AreEqual(0, p.Add().Count, "added once only");
    }

    [TestMethod]
    public void KeepsTheUsersLinesAndEncodingAndRemovesOnlyItsOwn()
    {
        var (s, p, docs) = Make();
        using var _ = s;
        var ps7 = Path.Combine(docs, "PowerShell", "Microsoft.PowerShell_profile.ps1");
        var ps5 = Path.Combine(docs, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(ps7)!);
        Directory.CreateDirectory(Path.GetDirectoryName(ps5)!);
        var mine = "Set-Alias g git\r\n# about claude-profiles, not the marker line\r\n";
        File.WriteAllBytes(ps7, new UTF8Encoding(false).GetBytes(mine));
        var utf16 = "Import-Module posh-git\r\n";
        File.WriteAllBytes(ps5, [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(utf16)]);

        Assert.AreEqual(2, p.Add().Count);
        var after7 = File.ReadAllBytes(ps7);
        Assert.AreNotEqual(0xEF, after7[0], "no BOM added to a file without one");
        StringAssert.StartsWith(Encoding.UTF8.GetString(after7), mine);
        var after5 = File.ReadAllBytes(ps5);
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFE }, after5[..2]);
        StringAssert.Contains(Encoding.Unicode.GetString(after5[2..]), PowerShellProfile.Line);

        Assert.AreEqual(2, p.Remove().Count);
        Assert.AreEqual(mine, File.ReadAllText(ps7));
        CollectionAssert.AreEqual((byte[])[.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(utf16)], File.ReadAllBytes(ps5));
        Assert.IsFalse(p.IsAdded());
        Assert.AreEqual(0, p.Remove().Count);
    }

    [TestMethod]
    public void InstallAddsCompletionOnlyWhenAsked()
    {
        var (s, p, docs) = Make();
        using var _ = s;
        var src = s.PathOf("build");
        Directory.CreateDirectory(src);
        foreach (var f in new[] { "cpm.exe", "claude.exe", ShellInstaller.CompletionScript }) File.WriteAllText(Path.Combine(src, f), "x");
        var i = new ShellInstaller(s.Store, new UserPath(s.Locator, new FakeUserPathStore(null)), p);
        Assert.IsNull(i.Install(src).CompletionAdded);
        Assert.IsTrue(File.Exists(Path.Combine(s.Locator.BinDir, ShellInstaller.CompletionScript)));
        Assert.IsFalse(Directory.Exists(docs));
        Assert.AreEqual(1, i.Install(src, completion: true).CompletionAdded!.Count);
        Assert.IsTrue(p.IsAdded());
    }

    [TestMethod]
    public void CompletionNeedsTheScript()
    {
        var (s, p, _) = Make();
        using var __ = s;
        var src = s.PathOf("build");
        Directory.CreateDirectory(src);
        foreach (var f in new[] { "cpm.exe", "claude.exe" }) File.WriteAllText(Path.Combine(src, f), "x");
        var i = new ShellInstaller(s.Store, new UserPath(s.Locator, new FakeUserPathStore(null)), p);
        Assert.ThrowsExactly<ProfileException>(() => i.Install(src, completion: true));
        Assert.IsFalse(p.IsAdded());
    }
}

[TestClass]
public sealed class UpdateIfInstalledTests
{
    static string Source(Sandbox s, string version)
    {
        var src = s.PathOf("app-tools");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "cpm.exe"), "cpm " + version);
        File.WriteAllText(Path.Combine(src, "claude.exe"), "wrapper " + version);
        return src;
    }

    [TestMethod]
    public void DoesNothingWhenNotInstalled()
    {
        using var s = new Sandbox();
        var path = new FakeUserPathStore(null);
        var updated = new ShellInstaller(s.Store, new UserPath(s.Locator, path)).UpdateIfInstalled(Source(s, "v1"));
        Assert.AreEqual(0, updated.Count);
        Assert.IsFalse(Directory.Exists(s.Locator.BinDir));
        Assert.AreEqual(0, path.Writes);
    }

    [TestMethod]
    public void ReplacesChangedFilesAndLeavesPathAlone()
    {
        using var s = new Sandbox();
        var installPath = new FakeUserPathStore(null);
        new ShellInstaller(s.Store, new UserPath(s.Locator, installPath)).Install(Source(s, "v1"));
        var path = new FakeUserPathStore(@"C:\elsewhere");
        var i = new ShellInstaller(s.Store, new UserPath(s.Locator, path));
        Assert.AreEqual(0, i.UpdateIfInstalled(Source(s, "v1")).Count, "same files");
        CollectionAssert.AreEquivalent(new[] { "cpm.exe", "claude.exe", "claude-as.exe" }, i.UpdateIfInstalled(Source(s, "v2")).ToList());
        Assert.AreEqual("wrapper v2", File.ReadAllText(Path.Combine(s.Locator.BinDir, "claude-as.exe")));
        Assert.AreEqual(0, path.Writes);
    }
}

[TestClass]
public sealed class FileWatcherTests
{
    [TestMethod]
    public void CallsBackOnceForABurstOnTheWatchedFiles()
    {
        using var s = new Sandbox();
        var root = s.Locator.ProfilesRoot;
        var calls = 0;
        using var seen = new ManualResetEventSlim();
        using var w = new FileWatcher(root, () => { Interlocked.Increment(ref calls); seen.Set(); });
        File.WriteAllText(Path.Combine(root, "profiles.json"), "{}");
        File.WriteAllText(Path.Combine(root, "default"), "main\n");
        File.WriteAllText(Path.Combine(root, "names"), "x");
        Assert.IsTrue(seen.Wait(TimeSpan.FromSeconds(5)));
        Thread.Sleep(FileWatcher.Quiet * 3);
        Assert.AreEqual(1, Volatile.Read(ref calls));
    }

    [TestMethod]
    public void IgnoresOtherFilesAndFolders()
    {
        using var s = new Sandbox();
        var root = s.Locator.ProfilesRoot;
        var calls = 0;
        using var w = new FileWatcher(root, () => Interlocked.Increment(ref calls));
        File.WriteAllText(Path.Combine(root, "profiles.json.tmp"), "{}");
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        File.WriteAllText(Path.Combine(root, "logs", "events.log"), "x");
        Thread.Sleep(FileWatcher.Quiet * 4);
        Assert.AreEqual(0, Volatile.Read(ref calls));
    }
}

[TestClass]
public sealed class OpenVSCodeTests
{
    sealed class FakeStarter : IProgramStarter
    {
        public List<(string Exe, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string?> Env, string Dir)> Calls { get; } = [];
        public void Start(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string?> envChanges, string workingDir) =>
            Calls.Add((exe, args, envChanges, workingDir));
    }

    static Launcher Make(Sandbox s, FakeStarter st, string? code = @"C:\VS\Code.exe", bool running = false) =>
        new(s.Store, new FakeOpener()) { Starter = st, FindVSCode = () => code, IsVSCodeRunning = _ => running };

    [TestMethod]
    public void StartsCodeWithTheProfilesFolder()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("Work", "work", "#0F8A7E");
        var st = new FakeStarter();
        Make(s, st).OpenVSCode(p, s.Home);
        var c = st.Calls.Single();
        Assert.AreEqual(@"C:\VS\Code.exe", c.Exe);
        CollectionAssert.AreEqual(new[] { s.Home }, c.Args.ToList());
        Assert.AreEqual(s.Locator.ConfigDir(p), c.Env["CLAUDE_CONFIG_DIR"]);
        Assert.AreEqual(p.Id, c.Env["CLAUDE_PROFILE_ID"]);
    }

    [TestMethod]
    public void TheBuiltInProfileRemovesTheVariable()
    {
        using var s = new Sandbox();
        var builtin = s.Store.Load().ProfileById(Profile.BuiltinId)!;
        var st = new FakeStarter();
        Make(s, st).OpenVSCode(builtin);
        var c = st.Calls.Single();
        Assert.AreEqual(0, c.Args.Count);
        Assert.IsTrue(c.Env.ContainsKey("CLAUDE_CONFIG_DIR"));
        Assert.IsNull(c.Env["CLAUDE_CONFIG_DIR"]);
    }

    [TestMethod]
    public void RefusesWhenCodeIsRunningMissingOrTheFolderIsGone()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("Work", "work", "#0F8A7E");
        var st = new FakeStarter();
        var running = Assert.ThrowsExactly<LauncherException>(() => Make(s, st, running: true).OpenVSCode(p));
        StringAssert.Contains(running.Message, "VS Code");
        Assert.ThrowsExactly<LauncherException>(() => Make(s, st, code: null).OpenVSCode(p));
        var gone = Assert.ThrowsExactly<LauncherException>(() => Make(s, st).OpenVSCode(p, s.PathOf("missing")));
        Assert.AreEqual(s.PathOf("missing"), gone.MissingFolder);
        Assert.AreEqual(0, st.Calls.Count);
    }
}
