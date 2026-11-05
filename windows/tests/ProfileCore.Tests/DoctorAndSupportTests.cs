using System.Text.Json;
using Microsoft.Win32;
using ProfileCore;
using ProfileCore.Shared;

namespace ProfileCore.Tests;

[TestClass]
public sealed class DoctorTests
{
    /// A machine whose PATH has the wrapper first (or not), and a real claude.exe after it.
    static (Doctor D, FakeMachine M) Doctor(Sandbox s, bool wrapperFirst = true)
    {
        var real = s.PathOf("realbin");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "claude.exe"), "");
        Directory.CreateDirectory(s.Locator.BinDir);
        File.WriteAllText(Path.Combine(s.Locator.BinDir, "claude.exe"), "");
        var user = wrapperFirst ? $"{s.Locator.BinDir};{real}" : $"{real};{s.Locator.BinDir}";
        var runner = new FakeRunner { Handler = (exe, _) => new ProcessResult(exe.StartsWith(real, StringComparison.OrdinalIgnoreCase) ? "2.1.318 (Claude Code)\n" : "") };
        var m = new FakeMachine(new FakePath(@"C:\Windows\System32", user));
        return (new Doctor(s.Locator, s.Credentials, runner, m), m);
    }

    static void SignedInBuiltin(Sandbox s)
    {
        s.Write("""{"oauthAccount":{"accountUuid":"u"}}""", ".claude.json");
        s.Write("{}", ".claude/.credentials.json");
    }

    static bool Has(DoctorReport r, FindingLevel level, string text) => r.Findings.Any(f => f.Level == level && f.Message.Contains(text, StringComparison.Ordinal));

    [TestMethod]
    public void CleanSetupPassesWithTheStrayFileAsANote()
    {
        using var s = new Sandbox();
        SignedInBuiltin(s);
        s.Write("{}", ".claude/.claude.json");
        s.Store.Load();
        var r = Doctor(s).D.Run();
        Assert.IsFalse(r.HasFaults, string.Join("\n", r.Findings));
        Assert.IsTrue(Has(r, FindingLevel.Note, @"~\.claude\.claude.json"));
        Assert.IsTrue(Has(r, FindingLevel.Note, "2.1.318"));
    }

    [TestMethod]
    public void NamesEachFault()
    {
        using var s = new Sandbox();
        SignedInBuiltin(s);
        s.Store.Load();
        var d = Doctor(s).D;
        File.WriteAllText(s.Locator.DefaultFile, "nonsense\n");
        Assert.IsTrue(d.Run().Findings.Any(f => f.Level == FindingLevel.Fault && f.Fix == "cpm doctor --fix"));
        s.Store.RepairFiles();
        var p = s.Store.Create("A", "a", "#000000");
        Directory.Delete(p.ConfigDir!, true);
        Assert.IsTrue(Has(d.Run(), FindingLevel.Fault, "folder is missing"));
    }

    [TestMethod]
    public void WrapperNotFirstIsAFault()
    {
        using var s = new Sandbox();
        SignedInBuiltin(s);
        s.Store.Load();
        Assert.IsTrue(Has(Doctor(s, wrapperFirst: false).D.Run(), FindingLevel.Fault, "not the wrapper"));
    }

    [TestMethod]
    public void ACmdBeforeTheWrapperWins()
    {
        using var s = new Sandbox();
        SignedInBuiltin(s);
        s.Store.Load();
        var (d, m) = Doctor(s);
        var npm = s.PathOf("npm");
        Directory.CreateDirectory(npm);
        File.WriteAllText(Path.Combine(npm, "claude.cmd"), "");
        ((FakePath)m.Path).SystemPath = npm;
        Assert.IsTrue(Has(d.Run(), FindingLevel.Fault, "claude.cmd, not the wrapper"));
    }

    [TestMethod]
    public void OnlyACmdIsAFaultPointingToTheNativeInstaller()
    {
        using var s = new Sandbox();
        SignedInBuiltin(s);
        s.Store.Load();
        var npm = s.PathOf("npm");
        Directory.CreateDirectory(npm);
        File.WriteAllText(Path.Combine(npm, "claude.cmd"), "");
        var d = new Doctor(s.Locator, s.Credentials, new FakeRunner(), new FakeMachine(new FakePath("", npm)));
        var r = d.Run();
        Assert.IsTrue(r.Findings.Any(f => f.Level == FindingLevel.Fault && f.Message.Contains("claude.cmd") && f.Fix!.Contains("native")));
    }

    [TestMethod]
    public void SignedOutAndWrongSpellingAreFaults()
    {
        using var s = new Sandbox();
        SignedInBuiltin(s);
        var p = s.Store.Create("A", "a", "#000000");
        s.SignIn(p, "v");
        s.Store.CompleteLogin(p.Id, new Account("v", null, null));
        var d = Doctor(s).D;
        Assert.IsTrue(Has(d.Run(), FindingLevel.Ok, "A: signed in"));
        File.Delete(s.Locator.CredentialsFile(p));
        Assert.IsTrue(Has(d.Run(), FindingLevel.Fault, "A: not signed in"));
        var json = s.Text(s.Locator.RegistryFile).Replace(JsonEncodedText.Encode(p.ConfigDir!).ToString(), JsonEncodedText.Encode(p.ConfigDir!.ToLowerInvariant()).ToString());
        File.WriteAllText(s.Locator.RegistryFile, json);
        Assert.IsTrue(Has(d.Run(), FindingLevel.Fault, "stored path"));
    }

    [TestMethod]
    public void NotesForTheMachine()
    {
        using var s = new Sandbox();
        SignedInBuiltin(s);
        s.Store.Load();
        var (d, m) = Doctor(s);
        m.LoginVars.Add("ANTHROPIC_API_KEY");
        m.SmartAppControl = true;
        s.Write("function claude { 'hi' }", "ps/profile.ps1");
        m.Profiles.Add(s.PathOf("ps/profile.ps1"));
        s.Credentials.Names.Add("Claude Code-credentials");
        s.Write("x", ".claude-profiles/profiles/orphan/x");
        var r = d.Run();
        Assert.IsFalse(r.HasFaults, string.Join("\n", r.Findings));
        foreach (var text in new[] { "ANTHROPIC_API_KEY is set", "Smart App Control", "alias or function named claude", "Credential Manager", @"profiles\orphan" })
            Assert.IsTrue(Has(r, FindingLevel.Note, text), text);
    }

    [TestMethod]
    public void NotesDoNotCountAsFaults()
    {
        Assert.IsFalse(new DoctorReport([new(FindingLevel.Note, "x")]).HasFaults);
        Assert.IsTrue(new DoctorReport([new(FindingLevel.Fault, "x")]).HasFaults);
    }

    [TestMethod]
    public void DoctorChangesNothing()
    {
        using var s = new Sandbox();
        s.Store.Load();
        File.WriteAllText(s.Locator.DefaultFile, "nonsense\n");
        Doctor(s).D.Run();
        Assert.AreEqual("nonsense\n", s.Text(s.Locator.DefaultFile));
    }
}

[TestClass]
public sealed class SupportTests
{
    [TestMethod]
    public void ReadsOnlyTheAccount()
    {
        using var s = new Sandbox();
        s.Write("""{"oauthAccount":{"accountUuid":"u","emailAddress":"e@x.com","organizationName":"O","accessToken":"secret"},"projects":{}}""", "c.json");
        Assert.AreEqual(new Account("u", "e@x.com", "O"), AccountReader.ReadAccount(s.PathOf("c.json")));
    }

    [TestMethod]
    public void MissingOrBrokenGivesNull()
    {
        using var s = new Sandbox();
        Assert.IsNull(AccountReader.ReadAccount(s.PathOf("none.json")));
        s.Write("{broken", "b.json");
        Assert.IsNull(AccountReader.ReadAccount(s.PathOf("b.json")));
        s.Write("""{"oauthAccount":{"emailAddress":"e"}}""", "n.json");
        Assert.IsNull(AccountReader.ReadAccount(s.PathOf("n.json")));
    }

    [TestMethod]
    public void StatusLineRefusesTheBuiltinProfile()
    {
        using var s = new Sandbox();
        Assert.ThrowsExactly<ProfileException>(() => new StatusLineWriter(s.Locator).Write(s.Store.DefaultProfile()));
    }

    [TestMethod]
    public void StatusLineQuotesTheWrapperPath()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(p.ConfigDir!, "settings.json")));
        var cmd = doc.RootElement.GetProperty("statusLine").GetProperty("command").GetString();
        Assert.AreEqual($"\"{s.Locator.BinDir}\\claude.exe\" --profile-label {p.Id}", cmd);
        Assert.AreEqual("command", doc.RootElement.GetProperty("statusLine").GetProperty("type").GetString());
    }

    [TestMethod]
    public void EventLogEscapesTabsAndNewlines()
    {
        using var s = new Sandbox();
        new EventLog(s.Locator).Write("k", null, "a\tb\nc");
        var line = s.Text(Path.Combine(s.Locator.LogsDir, "writes.log"));
        Assert.AreEqual(5, line.TrimEnd('\n').Split('\t').Length);
        StringAssert.Contains(line, "a b c");
    }

    [TestMethod]
    public void EventLogRotates()
    {
        using var s = new Sandbox();
        var log = new EventLog(s.Locator) { MaxBytes = 10 };
        for (var i = 0; i < 5; i++) { log.Write("k", null, new string('x', 20)); log.Rotate(); }
        Assert.IsTrue(File.Exists(Path.Combine(s.Locator.LogsDir, "writes.log.3")));
        Assert.IsFalse(File.Exists(Path.Combine(s.Locator.LogsDir, "writes.log.4")));
    }

    [TestMethod]
    public void SkipsOurBinFolderHoweverItIsSpelled()
    {
        using var s = new Sandbox();
        var real = s.PathOf("real");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "claude.exe"), "");
        Directory.CreateDirectory(s.Locator.BinDir);
        File.WriteAllText(Path.Combine(s.Locator.BinDir, "claude.exe"), "");
        foreach (var spelling in new[] { s.Locator.BinDir, s.Locator.BinDir + "\\", s.Locator.BinDir.ToUpperInvariant(),
                                         s.Locator.BinDir + "\\..\\bin", "\"" + s.Locator.BinDir + "\"", s.Locator.BinDir.Replace('\\', '/') })
            Assert.AreEqual(Path.Combine(real, "claude.exe"), new ClaudeBinaryLocator(s.Locator, $"{spelling};;{real}").RealBinary(), spelling);
    }

    [TestMethod]
    public void NeverPicksAScript()
    {
        using var s = new Sandbox();
        var npm = s.PathOf("npm");
        Directory.CreateDirectory(npm);
        File.WriteAllText(Path.Combine(npm, "claude.cmd"), "");
        var b = new ClaudeBinaryLocator(s.Locator, npm);
        Assert.IsNull(b.RealBinary());
        Assert.AreEqual(Path.Combine(npm, "claude.cmd"), b.ScriptOnly());
    }

    [TestMethod]
    public void ParsesProcStartAsFileTime()
    {
        // S0 check C9: a JSON string of digits.
        using var doc = JsonDocument.Parse("""{"a":"134382630457202312","b":134382630457202312,"c":"Wed Nov  4 10:50:45 2026","d":"soon"}""");
        var expected = DateTime.FromFileTimeUtc(134382630457202312);
        Assert.AreEqual(expected, LiveSessionReader.ParseProcStart(doc.RootElement.GetProperty("a")));
        Assert.AreEqual(expected, LiveSessionReader.ParseProcStart(doc.RootElement.GetProperty("b")));
        Assert.AreEqual(new DateTime(2026, 11, 4, 10, 50, 45, DateTimeKind.Utc), LiveSessionReader.ParseProcStart(doc.RootElement.GetProperty("c")));
        Assert.IsNull(LiveSessionReader.ParseProcStart(doc.RootElement.GetProperty("d")));
        Assert.AreEqual(new DateTime(2026, 11, 4, 10, 50, 45, DateTimeKind.Utc), new DateTime(expected.Ticks - expected.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc));
    }

    static string Record(int pid, DateTime start) => $$"""{"pid":{{pid}},"sessionId":"s{{pid}}","procStart":"{{start.ToFileTimeUtc()}}","status":"idle"}""";

    [TestMethod]
    public void AReusedPidIsNotLive()
    {
        using var s = new Sandbox();
        var start = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        var procs = new FakeProcesses();
        procs.Running[7] = start.AddMinutes(5);
        s.Write(Record(7, start), ".claude/sessions/7.json");
        var r = new LiveSessionReader(s.Locator, procs);
        Assert.AreEqual(0, r.LiveRecords(s.Store.DefaultProfile()).Count);
        procs.Running[7] = start.AddMilliseconds(400);
        Assert.AreEqual(1, r.LiveRecords(s.Store.DefaultProfile()).Count);
    }

    [TestMethod]
    public void ADeadProcessIsNotLiveAndAccessDeniedIs()
    {
        using var s = new Sandbox();
        var start = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        var procs = new FakeProcesses();
        s.Write(Record(8, start), ".claude/sessions/8.json");
        var r = new LiveSessionReader(s.Locator, procs);
        Assert.IsFalse(r.HasLiveSession(s.Store.DefaultProfile()));
        procs.Denied.Add(8); // an elevated session: cannot read its start time
        Assert.IsTrue(r.HasLiveSession(s.Store.DefaultProfile()));
    }

    [TestMethod]
    public void AnUnreadableRecentRecordFailsClosed()
    {
        using var s = new Sandbox();
        s.Write("{half", ".claude/sessions/9.json");
        var r = new LiveSessionReader(s.Locator, new FakeProcesses());
        Assert.IsTrue(r.HasLiveSession(s.Store.DefaultProfile()));
        File.SetLastWriteTimeUtc(s.PathOf(".claude/sessions/9.json"), DateTime.UtcNow.AddMinutes(-5));
        Assert.IsFalse(r.HasLiveSession(s.Store.DefaultProfile()));
    }

    [TestMethod]
    public void NeverOpensKeyFiles()
    {
        using var s = new Sandbox();
        s.Write("secret", ".claude/sessions/1.key");
        using var held = new FileStream(s.PathOf(".claude/sessions/1.key"), FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.IsFalse(new LiveSessionReader(s.Locator, new FakeProcesses()).HasLiveSession(s.Store.DefaultProfile()));
    }

    [TestMethod]
    public void RealProcessInfoAgreesWithItself()
    {
        var me = Environment.ProcessId;
        var (state, start) = WinProcess.Query(me);
        Assert.AreEqual(WinProcess.ProcessCheck.Running, state);
        Assert.IsTrue(Math.Abs((start!.Value - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds) < 1);
        Assert.AreEqual(WinProcess.ProcessCheck.NotRunning, WinProcess.Query(int.MaxValue - 1).State);
    }

    // With every shared worker thread busy, output must still arrive. CI found this on a small machine.
    // Alone, because it starves the pool that file-watcher and timing tests running beside it need.
    [TestMethod, DoNotParallelize]
    public void RunnerCapturesOutputWhileTheThreadPoolIsBusy()
    {
        // Not disposed: queued work may still reach it after the test ends.
        var release = new ManualResetEventSlim();
        for (var i = 0; i < 256; i++) ThreadPool.QueueUserWorkItem(_ => release.Wait());
        try
        {
            var r = new ProcessRunner().Run(Cmd, ["/d", "/c", "echo out& echo err>&2"], null, TimeSpan.FromSeconds(5));
            StringAssert.Contains(r.Stdout, "out");
            StringAssert.Contains(r.Stderr, "err");
        }
        finally { release.Set(); }
    }

    [TestMethod]
    public void RunnerCapturesOutputAndAppliesEnv()
    {
        Environment.SetEnvironmentVariable("CPM_TEST_GONE", "inherited");
        var env = new LaunchEnv();
        env.Set["CPM_TEST_VALUE"] = "hello world";
        env.Unset.Add("CPM_TEST_GONE");
        var r = new ProcessRunner().Run(Cmd, ["/d", "/c", "echo %CPM_TEST_VALUE%& echo [%CPM_TEST_GONE%]& echo err 1>&2& exit /b 3"], env, TimeSpan.FromSeconds(20));
        StringAssert.Contains(r.Stdout, "hello world");
        StringAssert.Contains(r.Stdout, "[%CPM_TEST_GONE%]");
        StringAssert.Contains(r.Stderr, "err");
        Assert.AreEqual(3, r.ExitCode);
    }

    [TestMethod, DoNotParallelize] // starts ping, which the process-tree tests count
    public void RunnerTimesOut()
    {
        var t = System.Diagnostics.Stopwatch.StartNew();
        var r = new ProcessRunner().Run(Cmd, ["/d", "/c", "ping -n 30 127.0.0.1 >nul"], null, TimeSpan.FromSeconds(1));
        Assert.IsTrue(r.TimedOut);
        Assert.IsTrue(t.Elapsed < TimeSpan.FromSeconds(10), t.Elapsed.ToString());
    }

    [TestMethod]
    public void RunnerReportsAMissingExecutable()
    {
        var r = new ProcessRunner().Run(@"C:\no\such\program.exe", [], null, TimeSpan.FromSeconds(5));
        Assert.AreEqual(127, r.ExitCode);
        StringAssert.Contains(r.Stderr, "Could not run");
    }

    static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [TestMethod]
    public void NamesReaderSkipsBrokenLinesAndReadsCrLf()
    {
        using var s = new Sandbox();
        s.Write("main\tbuiltin\tMain\t#2F6FDE\t-\r\nbroken line\nb\tid2\tB\t#000000\tC:\\x\n", "names");
        var n = NamesFile.Read(s.PathOf("names"))!;
        Assert.AreEqual(2, n.Count);
        Assert.IsTrue(n[0].IsBuiltin && n[1].ConfigDir == @"C:\x");
        Assert.IsNull(NamesFile.Read(s.PathOf("missing")));
    }
}

[TestClass]
public sealed class InstallTests
{
    static string Source(Sandbox s)
    {
        var src = s.PathOf("build");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "cpm.exe"), "cpm v1");
        File.WriteAllText(Path.Combine(src, "claude.exe"), "wrapper v1");
        return src;
    }

    [TestMethod]
    public void InstallsAndIsSafeToRepeat()
    {
        using var s = new Sandbox();
        var path = new FakeUserPathStore(@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;C:\Tools");
        var i = new ShellInstaller(s.Store, new UserPath(s.Locator, path));
        var r = i.Install(Source(s));
        Assert.IsTrue(r.PathChanged);
        Assert.AreEqual("wrapper v1", s.Text(Path.Combine(s.Locator.BinDir, "claude-as.exe")));
        Assert.AreEqual("1\n", s.Text(Path.Combine(s.Locator.BinDir, "VERSION")));
        Assert.AreEqual($@"{s.Locator.BinDir};%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;C:\Tools", path.Value);
        Assert.AreEqual(RegistryValueKind.ExpandString, path.Kind);
        Assert.AreEqual(1, path.Broadcasts);
        StringAssert.Contains(s.Text(Path.Combine(s.Locator.LogsDir, "writes.log")), @"user PATH before change: %USERPROFILE%\AppData");
        var again = i.Install(Source(s));
        Assert.IsFalse(again.PathChanged);
        Assert.AreEqual(0, again.Updated.Count);
        Assert.AreEqual(1, path.Writes);
        Assert.IsTrue(i.IsInstalled());
    }

    [TestMethod]
    public void NeverResetsTheDefault()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        s.Store.SetDefault(p.Id);
        new ShellInstaller(s.Store, new UserPath(s.Locator, new FakeUserPathStore(null))).Install(Source(s));
        Assert.AreEqual(p.Id + "\n", s.Text(s.Locator.DefaultFile));
    }

    [TestMethod]
    public void ARunningExeIsRenamedNotOverwritten()
    {
        using var s = new Sandbox();
        var i = new ShellInstaller(s.Store, new UserPath(s.Locator, new FakeUserPathStore(null)));
        var src = Source(s);
        i.Install(src);
        File.WriteAllText(Path.Combine(src, "claude.exe"), "wrapper v2");
        var target = Path.Combine(s.Locator.BinDir, "claude.exe");
        // Held as a running image is: readable and renamable, not writable.
        using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            i.Install(src);
        Assert.AreEqual("wrapper v2", s.Text(target));
        Assert.AreEqual(1, Directory.GetFiles(s.Locator.BinDir, "claude.exe.old-*").Length);
        i.Install(src);
        Assert.AreEqual(0, Directory.GetFiles(s.Locator.BinDir, "*.old-*").Length, "old copies go at the next install");
    }

    [TestMethod]
    public void AMissingFileIsAnError()
    {
        using var s = new Sandbox();
        var src = s.PathOf("empty");
        Directory.CreateDirectory(src);
        Assert.ThrowsExactly<ProfileException>(() => new ShellInstaller(s.Store, new UserPath(s.Locator, new FakeUserPathStore(null))).Install(src));
    }

    [TestMethod]
    public void PathMovesOnlyOurEntryAndKeepsTheRest()
    {
        using var s = new Sandbox();
        var bin = s.Locator.BinDir;
        var store = new FakeUserPathStore($@"C:\A;{bin.ToUpperInvariant()}\;;C:\B;{bin}", RegistryValueKind.String);
        var u = new UserPath(s.Locator, store);
        Assert.IsTrue(u.EnsureFirst());
        Assert.AreEqual($@"{bin};C:\A;;C:\B", store.Value);
        Assert.AreEqual(RegistryValueKind.String, store.Kind, "type unchanged");
        Assert.IsTrue(u.IsFirst());
        Assert.IsTrue(u.Remove());
        Assert.AreEqual(@"C:\A;;C:\B", store.Value);
        Assert.IsFalse(u.Remove());
    }

    [TestMethod]
    public void AnOversizedPathIsNotWritten()
    {
        using var s = new Sandbox();
        var store = new FakeUserPathStore(new string('x', UserPath.MaxLength));
        Assert.ThrowsExactly<ProfileException>(() => new UserPath(s.Locator, store).EnsureFirst());
        Assert.AreEqual(0, store.Writes);
    }
}
