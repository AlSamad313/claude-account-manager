using System.Text;
using System.Text.Json;
using ProfileCore;
using ProfileCore.Shared;

namespace ProfileCore.Tests;

static class SessionData
{
    public static readonly DateTime Start = new(2026, 11, 4, 10, 58, 34, DateTimeKind.Utc);

    public static string Line(object o) => JsonSerializer.Serialize(o) + "\n";

    public static string Transcript(Sandbox s, Profile p, string id, string cwd = @"C:\w", string? title = "T", string entrypoint = "cli",
                                    double ageSeconds = 100)
    {
        var dir = Path.Combine(s.Locator.ConfigDir(p), "projects", "proj");
        Directory.CreateDirectory(dir);
        var text = Line(new { type = "user", cwd, entrypoint, sessionId = id });
        if (title != null) text += Line(new { type = "ai-title", aiTitle = title, sessionId = id });
        var path = Path.Combine(dir, id + ".jsonl");
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-ageSeconds));
        return path;
    }

    public static void Record(Sandbox s, Profile p, int pid, string id, string name = "", string status = "idle", string entrypoint = "cli",
                              string? start = null, string? pidDomain = null)
    {
        var dir = Path.Combine(s.Locator.ConfigDir(p), "sessions");
        Directory.CreateDirectory(dir);
        var o = new Dictionary<string, object?>
        {
            ["pid"] = pid, ["sessionId"] = id, ["procStart"] = start ?? Start.ToFileTimeUtc().ToString(), ["cwd"] = @"C:\live",
            ["status"] = status, ["entrypoint"] = entrypoint, ["name"] = name,
        };
        if (pidDomain != null) o["pidDomain"] = pidDomain;
        File.WriteAllText(Path.Combine(dir, $"{pid}.json"), JsonSerializer.Serialize(o));
    }

    public static FakeProcesses Alive(params int[] pids)
    {
        var f = new FakeProcesses();
        foreach (var p in pids) f.Running[p] = Start;
        return f;
    }
}

[TestClass]
public sealed class TranscriptReaderTests
{
    [TestMethod]
    public void NewestTitleWinsAndCustomBeatsAI()
    {
        using var s = new Sandbox();
        var t = SessionData.Line(new { type = "user", cwd = @"C:\w", entrypoint = "cli", message = new { content = "secret text" } });
        t += SessionData.Line(new { type = "ai-title", aiTitle = "old", sessionId = "x" });
        t += SessionData.Line(new { type = "ai-title", aiTitle = "new", sessionId = "x" });
        s.Write(t, "abc.jsonl");
        var r = TranscriptReader.Summary(s.PathOf("abc.jsonl"));
        Assert.AreEqual("new", r?.Title);
        Assert.AreEqual(@"C:\w", r?.Cwd);
        Assert.AreEqual("cli", r?.Entrypoint);
        Assert.AreEqual("abc", r?.SessionId);
        t += SessionData.Line(new { type = "custom-title", customTitle = "mine", sessionId = "x" });
        s.Write(t, "abc.jsonl");
        Assert.AreEqual("mine", TranscriptReader.Summary(s.PathOf("abc.jsonl"))?.Title);
    }

    [TestMethod]
    public void NeverUsesUserTextAsTitle()
    {
        using var s = new Sandbox();
        s.Write(SessionData.Line(new { type = "user", message = new { content = "sk-secret" } }), "a.jsonl");
        Assert.IsNull(TranscriptReader.Summary(s.PathOf("a.jsonl"))?.Title);
    }

    [TestMethod]
    public void FindsATitlePastTheWindowInALargeFile()
    {
        using var s = new Sandbox();
        var filler = SessionData.Line(new { type = "assistant", x = new string('y', 2000) });
        var sb = new StringBuilder();
        for (var i = 0; i < 60; i++) sb.Append(filler);
        sb.Append(SessionData.Line(new { type = "ai-title", aiTitle = "deep", sessionId = "x" }));
        for (var i = 0; i < 60; i++) sb.Append(filler);
        s.Write(sb.ToString(), "a.jsonl");
        Assert.IsTrue(new FileInfo(s.PathOf("a.jsonl")).Length > 2 * TranscriptReader.Window);
        Assert.AreEqual("deep", TranscriptReader.Summary(s.PathOf("a.jsonl"))?.Title);
    }

    [TestMethod]
    public void ReadsCrlfLines()
    {
        using var s = new Sandbox();
        s.Write("{\"type\":\"user\",\"cwd\":\"C:\\\\w\"}\r\n{\"type\":\"ai-title\",\"aiTitle\":\"crlf\"}\r\n", "a.jsonl");
        Assert.AreEqual("crlf", TranscriptReader.Summary(s.PathOf("a.jsonl"))?.Title);
    }

    [TestMethod]
    public void BrokenFileGivesNull()
    {
        using var s = new Sandbox();
        s.Write("not json\nalso not\n", "a.jsonl");
        Assert.IsNull(TranscriptReader.Summary(s.PathOf("a.jsonl")));
        Assert.IsNull(TranscriptReader.Summary(s.PathOf("none.jsonl")));
    }

    [TestMethod]
    public void ReadsAFileClaudeCodeHasOpenForWriting()
    {
        using var s = new Sandbox();
        s.Write(SessionData.Line(new { type = "ai-title", aiTitle = "open" }), "a.jsonl");
        using var writer = new FileStream(s.PathOf("a.jsonl"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        Assert.AreEqual("open", TranscriptReader.Summary(s.PathOf("a.jsonl"))?.Title);
    }
}

[TestClass]
public sealed class SessionIndexTests
{
    [TestMethod]
    public void RowsPerProfileSortedLiveFirstThenNewest()
    {
        using var s = new Sandbox();
        var b = s.Store.DefaultProfile();
        var a = s.Store.Create("A", "a", "#000000");
        SessionData.Transcript(s, b, "old", ageSeconds: 500);
        SessionData.Transcript(s, a, "new", ageSeconds: 10);
        SessionData.Transcript(s, a, "running", ageSeconds: 900);
        SessionData.Record(s, a, 7, "running");
        var rows = new SessionIndex(s.Locator, SessionData.Alive(7)).Sessions([b, a]).Rows;
        CollectionAssert.AreEqual(new[] { "running", "new", "old" }, rows.Select(r => r.SessionId).ToArray());
        Assert.IsTrue(rows[0].Live);
        Assert.AreEqual(a.Id, rows[0].ProfileId);
        Assert.AreEqual(7, rows[0].Pid);
        Assert.AreEqual(b.Id, rows[2].ProfileId);
    }

    [TestMethod]
    public void DesktopIsHiddenAndUnknownIsShown()
    {
        using var s = new Sandbox();
        var b = s.Store.DefaultProfile();
        SessionData.Transcript(s, b, "d", entrypoint: "claude-desktop");
        SessionData.Transcript(s, b, "v", entrypoint: "claude-vscode");
        SessionData.Transcript(s, b, "u", entrypoint: "something-new");
        SessionData.Record(s, b, 8, "live-desktop", entrypoint: "claude-desktop");
        var index = new SessionIndex(s.Locator, SessionData.Alive(8));
        CollectionAssert.AreEquivalent(new[] { "v", "u" }, index.Sessions([b]).Rows.Select(r => r.SessionId).ToArray());
        // The safety check still sees the hidden one.
        Assert.IsTrue(index.Live.HasLiveSession(b));
    }

    [TestMethod]
    public void ALiveRecordWithNoTranscriptGivesARow()
    {
        using var s = new Sandbox();
        var b = s.Store.DefaultProfile();
        SessionData.Record(s, b, 9, "fresh", name: "My session");
        var rows = new SessionIndex(s.Locator, SessionData.Alive(9)).Sessions([b]).Rows;
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("My session", rows[0].Title);
        Assert.IsTrue(rows[0].Live);
        Assert.AreEqual(@"C:\live", rows[0].Cwd);
    }

    [TestMethod]
    public void TitleOrderAndUntitled()
    {
        using var s = new Sandbox();
        var b = s.Store.DefaultProfile();
        SessionData.Transcript(s, b, "x", title: "From transcript");
        SessionData.Transcript(s, b, "y", title: null);
        SessionData.Record(s, b, 3, "x", name: "From record");
        var rows = new SessionIndex(s.Locator, SessionData.Alive(3)).Sessions([b]).Rows;
        Assert.AreEqual("From record", rows.First(r => r.SessionId == "x").Title);
        Assert.AreEqual("Untitled", rows.First(r => r.SessionId == "y").Title);
    }

    [TestMethod]
    public void AReusedPidIsNotLiveAndABrokenFileIsCounted()
    {
        using var s = new Sandbox();
        var b = s.Store.DefaultProfile();
        SessionData.Transcript(s, b, "x");
        SessionData.Record(s, b, 4, "x");
        s.Write("not json\n", ".claude/projects/proj/broken.jsonl");
        var procs = new FakeProcesses();
        procs.Running[4] = SessionData.Start.AddSeconds(99);
        var r = new SessionIndex(s.Locator, procs).Sessions([b]);
        Assert.AreEqual(1, r.Rows.Count);
        Assert.IsFalse(r.Rows[0].Live);
        Assert.AreEqual(1, r.ParseFailures);
    }

    [TestMethod]
    public void AnUnparsableStartIsMarkedMayBeStale()
    {
        using var s = new Sandbox();
        var b = s.Store.DefaultProfile();
        SessionData.Record(s, b, 5, "x", start: "not a date");
        var rows = new SessionIndex(s.Locator, SessionData.Alive(5)).Sessions([b]).Rows;
        Assert.IsTrue(rows[0].Live);
        Assert.IsTrue(rows[0].MayBeStale);
    }

    [TestMethod]
    public void ARecordFromAnotherComputerIsNotListedAsLiveButBlocksSafetyChecks()
    {
        using var s = new Sandbox();
        var b = s.Store.DefaultProfile();
        SessionData.Record(s, b, 6, "remote", pidDomain: "win32:SOME-OTHER-PC");
        var index = new SessionIndex(s.Locator, SessionData.Alive(6));
        Assert.AreEqual(0, index.Sessions([b]).Rows.Count);
        Assert.IsTrue(index.Live.HasLiveSession(b));
        var old = Path.Combine(s.Locator.ConfigDir(b), "sessions", "6.json");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddMinutes(-11));
        Assert.IsFalse(index.Live.HasLiveSession(b));
    }

    [TestMethod]
    public void ARecordFromThisComputerCountsNormally()
    {
        using var s = new Sandbox();
        var b = s.Store.DefaultProfile();
        SessionData.Record(s, b, 6, "here", pidDomain: LiveSessionReader.ThisPidDomain.ToLowerInvariant());
        Assert.IsTrue(new SessionIndex(s.Locator, SessionData.Alive(6)).Sessions([b]).Rows.Single().Live);
    }

    [TestMethod]
    public void CacheSeesAChangedFile()
    {
        using var s = new Sandbox();
        var b = s.Store.DefaultProfile();
        SessionData.Transcript(s, b, "x", title: "First");
        var index = new SessionIndex(s.Locator, new FakeProcesses());
        Assert.AreEqual("First", index.Sessions([b]).Rows[0].Title);
        SessionData.Transcript(s, b, "x", title: "Second title", ageSeconds: 5);
        Assert.AreEqual("Second title", index.Sessions([b]).Rows[0].Title);
    }

    [TestMethod]
    public void OwnerOfFindsTheRightProfile()
    {
        using var s = new Sandbox();
        var b = s.Store.DefaultProfile();
        var a = s.Store.Create("A", "a", "#000000");
        SessionData.Transcript(s, a, "mine");
        SessionData.Record(s, b, 11, "only-live");
        var index = new SessionIndex(s.Locator, SessionData.Alive(11));
        Assert.AreEqual(a.Id, index.OwnerOf("mine", [b, a])?.Id);
        Assert.AreEqual(b.Id, index.OwnerOf("only-live", [b, a])?.Id);
        Assert.IsNull(index.OwnerOf("none", [b, a]));
    }

    [TestMethod]
    public void ParsesTheMacDateForm()
    {
        using var doc = JsonDocument.Parse("""{"a":"Sat Oct 31 10:58:34 2026","b":"Sun Nov  1 09:05:00 2026"}""");
        Assert.AreEqual(new DateTime(2026, 10, 31, 10, 58, 34, DateTimeKind.Utc), LiveSessionReader.ParseProcStart(doc.RootElement.GetProperty("a")));
        Assert.IsNotNull(LiveSessionReader.ParseProcStart(doc.RootElement.GetProperty("b")));
    }
}

sealed class FakeOpener : ITerminalOpener
{
    public List<string> CommandLines { get; } = [];
    public List<string> Requests { get; } = [];
    public List<string> RequestFiles { get; } = [];

    public void Open(string commandLine, string workingDir)
    {
        CommandLines.Add(commandLine);
        var file = commandLine[(commandLine.IndexOf("--launch ", StringComparison.Ordinal) + 9)..].Trim('"');
        RequestFiles.Add(file);
        Requests.Add(File.ReadAllText(file));
    }

    public LaunchRequest Request(int i) => LaunchRequest.Parse(Encoding.UTF8.GetBytes(Requests[i]))!;
}

[TestClass]
public sealed class LauncherTests
{
    const string GoodId = "11111111-2222-3333-4444-555555555555";

    static Launcher Make(Sandbox s, FakeOpener o, bool installed = true) => new(s.Store, o) { IsShellInstalled = () => installed };

    static SessionRow Row(string? cwd, string id = GoodId, bool live = false) =>
        new("x", id, cwd, "T", DateTime.UtcNow, live, null, "cli", live ? 4 : null, null, false);

    [TestMethod]
    public void CommandLineHoldsOnlyTheBinAndRequestPaths()
    {
        using var s = new Sandbox();
        var o = new FakeOpener();
        var a = s.Store.Create("A", "a", "#000000");
        var odd = Path.Combine(s.Home, "odd & %PATH% ^ '$(x) ; name");
        Directory.CreateDirectory(odd);
        Make(s, o).NewSession(a, odd);
        var bin = Path.Combine(s.Locator.BinDir, "claude-as.exe");
        StringAssert.StartsWith(o.CommandLines[0], Launcher.Quote(bin) + " --launch ");
        Assert.IsFalse(o.CommandLines[0].Contains("odd"));
        Assert.IsFalse(o.CommandLines[0].Contains("CLAUDE_CONFIG_DIR"));
        var req = o.Request(0);
        Assert.AreEqual("a", req.CommandName);
        Assert.AreEqual(odd, req.Folder);
        Assert.AreEqual(0, req.Arguments.Count);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(s.Locator.ProfilesRoot, "run")), Path.GetDirectoryName(o.RequestFiles[0]));
    }

    [TestMethod]
    public void NewSessionDefaultsToHomeAndRefusesAMissingFolder()
    {
        using var s = new Sandbox();
        var o = new FakeOpener();
        var l = Make(s, o);
        l.NewSession(s.Store.DefaultProfile());
        Assert.AreEqual(s.Home, o.Request(0).Folder);
        Assert.ThrowsExactly<LauncherException>(() => l.NewSession(s.Store.DefaultProfile(), Path.Combine(s.Home, "nope")));
        Assert.AreEqual(1, o.CommandLines.Count);
    }

    [TestMethod]
    public void ResumeUsesTheOwnerAndTheSessionFolder()
    {
        using var s = new Sandbox();
        var o = new FakeOpener();
        var a = s.Store.Create("A", "a", "#000000");
        Make(s, o).Resume(Row(s.Home), a);
        var req = o.Request(0);
        Assert.AreEqual(s.Home, req.Folder);
        Assert.AreEqual("a", req.CommandName);
        CollectionAssert.AreEqual(new[] { "--resume", GoodId }, req.Arguments.ToArray());
    }

    [TestMethod]
    public void ResumeRefusesAnIdThatIsNotAUuid()
    {
        using var s = new Sandbox();
        var o = new FakeOpener();
        Assert.Throws<ProfileException>(() => Make(s, o).Resume(Row(s.Home, id: "--dangerously-skip-permissions"), s.Store.DefaultProfile()));
        Assert.AreEqual(0, o.CommandLines.Count);
    }

    [TestMethod]
    public void ResumeRefusesALiveRow()
    {
        using var s = new Sandbox();
        var o = new FakeOpener();
        var e = Assert.ThrowsExactly<LauncherException>(() => Make(s, o).Resume(Row(@"C:\", live: true), s.Store.DefaultProfile()));
        StringAssert.Contains(e.Message, "already running (pid 4)");
        Assert.AreEqual(0, o.CommandLines.Count);
    }

    [TestMethod]
    public void MissingFolderOffersOverride()
    {
        using var s = new Sandbox();
        var o = new FakeOpener();
        var l = Make(s, o);
        var b = s.Store.DefaultProfile();
        var e = Assert.ThrowsExactly<LauncherException>(() => l.Resume(Row(@"C:\no\such\place"), b));
        StringAssert.Contains(e.Message, @"C:\no\such\place");
        l.Resume(Row(@"C:\no\such\place"), b, folderOverride: s.Home);
        Assert.AreEqual(s.Home, o.Request(0).Folder);
    }

    [TestMethod]
    public void RefusesWhenTheShellIsNotInstalled()
    {
        using var s = new Sandbox();
        var o = new FakeOpener();
        Assert.ThrowsExactly<LauncherException>(() => Make(s, o, installed: false).NewSession(s.Store.DefaultProfile()));
        Assert.AreEqual(0, o.CommandLines.Count);
    }

    [TestMethod]
    public void OldRequestsAreCleanedUp()
    {
        using var s = new Sandbox();
        var o = new FakeOpener();
        var b = s.Store.DefaultProfile();
        var l = Make(s, o);
        l.NewSession(b);
        var first = o.RequestFiles[0];
        // A recent file stays, because its window may not have read it yet.
        l.NewSession(b);
        Assert.IsTrue(File.Exists(first));
        File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddSeconds(-120));
        l.NewSession(b);
        Assert.IsFalse(File.Exists(first));
    }

    [TestMethod]
    public void LoginCommand()
    {
        using var s = new Sandbox();
        var o = new FakeOpener();
        var a = s.Store.Create("A", "a", "#000000");
        Make(s, o).Login(a);
        CollectionAssert.AreEqual(new[] { "auth", "login" }, o.Request(0).Arguments.ToArray());
    }

    [TestMethod]
    [DataRow("plain", "plain")]
    [DataRow(@"C:\Users\A B\x.launch", "\"C:\\Users\\A B\\x.launch\"")]
    [DataRow(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [DataRow("a\"b", "\"a\\\"b\"")]
    [DataRow("", "\"\"")]
    public void QuoteFollowsTheWindowsRule(string input, string expected) => Assert.AreEqual(expected, Launcher.Quote(input));

    [TestMethod]
    public void LaunchRequestRoundTripsAndRejectsBadShapes()
    {
        var r = new LaunchRequest("w", @"D:\a ""b""", ["--resume", "x"], new DateTime(2026, 11, 4, 10, 50, 45, DateTimeKind.Utc));
        var back = LaunchRequest.Parse(r.ToJson())!;
        Assert.AreEqual(r.CommandName, back.CommandName);
        Assert.AreEqual(r.Folder, back.Folder);
        CollectionAssert.AreEqual(r.Arguments.ToArray(), back.Arguments.ToArray());
        Assert.AreEqual(r.CreatedAt, back.CreatedAt);
        foreach (var bad in new[] { "", "[]", "not json", """{"commandName":"w","folder":"x","arguments":[1],"createdAt":"2026-11-04T10:50:45Z"}""",
                                    """{"commandName":"w","folder":"x","arguments":[],"createdAt":"soon"}""", """{"commandName":"w","arguments":[],"createdAt":"2026-11-04T10:50:45Z"}""" })
            Assert.IsNull(LaunchRequest.Parse(Encoding.UTF8.GetBytes(bad)), bad);
    }
}

sealed class FakeSender(Func<int, int, string?> onSend) : IConsoleHelper
{
    public int Sends { get; private set; }
    public string? SendCtrlC(int pid) => onSend(pid, ++Sends);
    public IReadOnlyList<int> ConsoleMembers(int pid) => [];
}

[TestClass]
public sealed class SessionStopperTests
{
    static SessionRow LiveRow(string status = "idle") =>
        new("p", "s", @"C:\w", "Title", DateTime.UtcNow, true, status, "cli", 50, SessionData.Start, false);

    static SessionStopper Make(Sandbox s, FakeProcesses procs, FakeSender sender) =>
        new(s.Locator, procs, sender) { Grace = TimeSpan.FromMilliseconds(60), PollInterval = TimeSpan.FromMilliseconds(10) };

    [TestMethod]
    public void AlreadyStoppedNeverAsks()
    {
        using var s = new Sandbox();
        var sender = new FakeSender((_, _) => null);
        var asked = false;
        var r = Make(s, new FakeProcesses(), sender).Stop(LiveRow(), _ => asked = true);
        Assert.IsInstanceOfType<StopResult.AlreadyStopped>(r);
        Assert.IsFalse(asked);
        Assert.AreEqual(0, sender.Sends);
    }

    [TestMethod]
    public void AReusedPidIsNeverSent()
    {
        using var s = new Sandbox();
        var procs = new FakeProcesses();
        procs.Running[50] = SessionData.Start.AddMinutes(5);
        var sender = new FakeSender((_, _) => null);
        Assert.IsInstanceOfType<StopResult.AlreadyStopped>(Make(s, procs, sender).Stop(LiveRow(), _ => true));
        Assert.AreEqual(0, sender.Sends);
    }

    [TestMethod]
    public void CancelledSendsNothingAndBusyWarns()
    {
        using var s = new Sandbox();
        var sender = new FakeSender((_, _) => null);
        StopRequest? seen = null;
        var r = Make(s, SessionData.Alive(50), sender).Stop(LiveRow("busy"), q => { seen = q; return false; });
        Assert.IsInstanceOfType<StopResult.Cancelled>(r);
        Assert.AreEqual(0, sender.Sends);
        Assert.IsNotNull(seen?.Warning);
        Assert.AreEqual(50, seen!.Pid);
    }

    [TestMethod]
    public void OneCtrlCStopsItAndReportsChildrenLeftRunning()
    {
        using var s = new Sandbox();
        var procs = SessionData.Alive(50);
        procs.Running[60] = SessionData.Start.AddSeconds(5);
        procs.Running[61] = SessionData.Start.AddSeconds(6);
        procs.Children[50] = [new(60, "sleep.exe", SessionData.Start.AddSeconds(5)), new(61, "bash.exe", SessionData.Start.AddSeconds(6))];
        var sender = new FakeSender((pid, _) => { procs.Running.Remove(pid); procs.Running.Remove(61); return null; });
        var r = Make(s, procs, sender).Stop(LiveRow(), _ => true);
        var stopped = (StopResult.Stopped)r;
        Assert.AreEqual(1, stopped.CtrlCs);
        Assert.AreEqual("sleep.exe", stopped.LeftRunning.Single().Name);
        StringAssert.Contains(File.ReadAllText(Path.Combine(s.Locator.LogsDir, "writes.log")), "stop");
    }

    [TestMethod]
    public void ASecondCtrlCIsSentIfTheFirstDidNotWork()
    {
        using var s = new Sandbox();
        var procs = SessionData.Alive(50);
        var sender = new FakeSender((pid, n) => { if (n == 2) procs.Running.Remove(pid); return null; });
        Assert.AreEqual(2, ((StopResult.Stopped)Make(s, procs, sender).Stop(LiveRow(), _ => true)).CtrlCs);
    }

    [TestMethod]
    public void ReportsDidNotStopAndSendsNothingStronger()
    {
        using var s = new Sandbox();
        var sender = new FakeSender((_, _) => null);
        var r = Make(s, SessionData.Alive(50), sender).Stop(LiveRow(), _ => true);
        Assert.AreEqual(50, ((StopResult.DidNotStop)r).Pid);
        Assert.AreEqual(2, sender.Sends);
    }

    [TestMethod]
    public void SaysWhyWhenItCannotSend()
    {
        using var s = new Sandbox();
        var sender = new FakeSender((_, _) => "no console");
        Assert.AreEqual("no console", ((StopResult.CouldNotSend)Make(s, SessionData.Alive(50), sender).Stop(LiveRow(), _ => true)).Reason);
    }

    [TestMethod]
    public void EndedWhileTheUserWasAnswering()
    {
        using var s = new Sandbox();
        var procs = SessionData.Alive(50);
        var sender = new FakeSender((_, _) => null);
        var r = Make(s, procs, sender).Stop(LiveRow(), _ => { procs.Running.Remove(50); return true; });
        Assert.IsInstanceOfType<StopResult.AlreadyStopped>(r);
        Assert.AreEqual(0, sender.Sends);
    }
}

[TestClass]
public sealed class ProcessTreeTests
{
    /// Real processes: this test process starts cmd, which starts ping.
    [TestMethod, DoNotParallelize] // counts ping.exe under this test process
    public void DescendantsFindsChildrenAndGrandchildren()
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/d /c ping -n 4 127.0.0.1 >nul")
            { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            List<string> names = [];
            for (var i = 0; i < 40 && !names.Contains("PING.EXE", StringComparer.OrdinalIgnoreCase); i++)
            {
                Thread.Sleep(50);
                names = new ProcessInfoProvider().Descendants(Environment.ProcessId).Select(d => d.Name).ToList();
            }
            CollectionAssert.Contains(names.Select(n => n.ToLowerInvariant()).ToList(), "cmd.exe", string.Join(",", names));
            CollectionAssert.Contains(names.Select(n => n.ToLowerInvariant()).ToList(), "ping.exe", string.Join(",", names));
        }
        finally { p.Kill(true); }
    }
}

[TestClass]
public sealed class OrphanCommandTests
{
    static string? BuiltWrapper()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "build", "bin", "claude.exe");
            if (File.Exists(f)) return f;
        }
        return null;
    }

    /// The Git Bash shape, with cmd: outer cmd owns a hidden console; an inner cmd starts ping there
    /// and exits at once, so ping has no living parent. It must still be found, through the console.
    [TestMethod, DoNotParallelize] // counts ping.exe under this test process
    public void FindsACommandWhoseParentHasExitedThroughItsConsole()
    {
        var wrapper = BuiltWrapper();
        if (wrapper == null) Assert.Inconclusive("Run build.ps1 -Test, which publishes the wrapper first.");
        using var s = new Sandbox();
        Directory.CreateDirectory(s.Locator.BinDir);
        File.Copy(wrapper!, Path.Combine(s.Locator.BinDir, "claude.exe"));
        using var outer = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
            "/d /c \"cmd /d /c start /b ping -n 8 127.0.0.2 >nul & ping -n 8 127.0.0.3 >nul\"") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            var helper = new ConsoleHelper(s.Locator);
            IReadOnlyList<ProcEntry> found = [];
            List<string> withoutConsole = [];
            for (var i = 0; i < 60; i++)
            {
                Thread.Sleep(100);
                withoutConsole = new ProcessInfoProvider().Descendants(Environment.ProcessId).Select(e => e.Name.ToLowerInvariant()).ToList();
                found = new ProcessInfoProvider().Descendants(Environment.ProcessId, helper.ConsoleMembers);
                if (found.Count(e => e.Name.Equals("PING.EXE", StringComparison.OrdinalIgnoreCase)) == 2) break;
            }
            Assert.AreEqual(2, found.Count(e => e.Name.Equals("PING.EXE", StringComparison.OrdinalIgnoreCase)), string.Join(",", found.Select(e => e.Name)));
            Assert.AreEqual(1, withoutConsole.Count(n => n == "ping.exe"), "the parent links alone should miss the orphan");
            Assert.IsFalse(found.Any(e => e.Name.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase)));
        }
        // The orphan ping is outside the tree, so Kill misses it; it ends by itself within 8 seconds.
        finally { outer.Kill(true); }
    }

    [TestMethod]
    public void ShellStatusWarnsThatTheCommandMayKeepRunning()
    {
        StringAssert.Contains(new StopRequest("t", null, "shell", 1).Warning, "keep running");
        Assert.IsNull(new StopRequest("t", null, "idle", 1).Warning);
    }
}
