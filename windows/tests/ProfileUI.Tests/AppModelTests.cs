using System.Text.Json;
using ProfileCore;
using ProfileCore.Tests;
using ProfileUI;

namespace ProfileUI.Tests;

sealed class RecordingOpener : ITerminalOpener
{
    public List<string> CommandLines { get; } = [];
    public void Open(string commandLine, string workingDir) => CommandLines.Add(commandLine);
}

/// An AppModel over a temporary home. Nothing here starts a terminal, changes PATH or runs Claude Code.
sealed class Harness : IDisposable
{
    public Sandbox S { get; } = new();
    public FakeProcesses Processes { get; } = new();
    public RecordingOpener Opener { get; } = new();
    public FakeUserPathStore UserPath { get; } = new(null);
    /// Ctrl+C "works": the process is gone at once.
    public StopHelper Helper { get; }
    public AppModel Model { get; }

    public Harness(bool installed = true, string? shellSource = null)
    {
        Helper = new StopHelper(Processes);
        if (installed) MarkInstalled();
        var login = new LoginCoordinator(S.Store, new ClaudeCLI(S.Locator, new FakeRunner()), Processes);
        var env = new AppEnvironment
        {
            Store = S.Store, Login = login,
            Launcher = new Launcher(S.Store, Opener) { IsShellInstalled = () => File.Exists(Path.Combine(S.Locator.BinDir, "claude-as.exe")) },
            Index = new SessionIndex(S.Locator, Processes),
            Stopper = new SessionStopper(S.Locator, Processes, Helper) { Grace = TimeSpan.FromMilliseconds(300), PollInterval = TimeSpan.FromMilliseconds(20) },
            Doctor = new Doctor(S.Locator, S.Credentials, new FakeRunner(), new FakeMachine(new FakePath("", ""))),
            Installer = new ShellInstaller(S.Store, new ProfileCore.UserPath(S.Locator, UserPath), new PowerShellProfile(S.Locator, Path.Combine(S.Home, "Documents"))),
            ShellSource = shellSource,
        };
        Model = new AppModel(env) { Now = () => new DateTime(2026, 11, 5, 12, 0, 0) };
    }

    public void MarkInstalled()
    {
        Directory.CreateDirectory(S.Locator.BinDir);
        foreach (var (target, _) in ShellInstaller.Files) File.WriteAllText(Path.Combine(S.Locator.BinDir, target), "");
    }

    public Profile Builtin => S.Store.Load().ProfileById(Profile.BuiltinId)!;

    /// A profile that finished signing in.
    public Profile Ready(string name, string command, string uuid = "u-1")
    {
        var p = S.Store.Create(name, command, "#0F8A7E");
        S.SignIn(p, uuid, $"{command}@example.com");
        new LoginCoordinator(S.Store, new ClaudeCLI(S.Locator, new FakeRunner())).Keep(S.Store.Load().ProfileById(p.Id)!);
        return S.Store.Load().ProfileById(p.Id)!;
    }

    public string Transcript(Profile p, string id, string title, string cwd, int minutesAgo = 5)
    {
        var dir = Path.Combine(S.Locator.ConfigDir(p), "projects", "proj");
        Directory.CreateDirectory(dir);
        var text = JsonSerializer.Serialize(new { type = "user", cwd, entrypoint = "cli", sessionId = id }) + "\n" +
                   JsonSerializer.Serialize(new { type = "ai-title", aiTitle = title, sessionId = id }) + "\n";
        var path = Path.Combine(dir, id + ".jsonl");
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
        return path;
    }

    /// A running session record, matching a process the fake process list says is running.
    public void Live(Profile p, int pid, string id, string status = "idle")
    {
        var start = new DateTime(2026, 11, 4, 10, 58, 34, DateTimeKind.Utc);
        Processes.Running[pid] = start;
        var dir = Path.Combine(S.Locator.ConfigDir(p), "sessions");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{pid}.json"), JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["pid"] = pid, ["sessionId"] = id, ["procStart"] = start.ToFileTimeUtc().ToString(), ["cwd"] = S.Home,
            ["status"] = status, ["entrypoint"] = "cli", ["name"] = "",
        }));
    }

    public static string Id(int n) => $"00000000-0000-0000-0000-{n:D12}";

    public void Dispose()
    {
        Model.Dispose();
        S.Dispose();
    }
}

[TestClass]
public sealed class AppModelTests
{
    [TestMethod]
    public void RelativeTimesReadLikeMacOS()
    {
        var now = new DateTime(2026, 11, 5, 12, 0, 0);
        Assert.AreEqual("just now", AppModel.Relative(now.AddSeconds(-20), now));
        Assert.AreEqual("1 minute ago", AppModel.Relative(now.AddMinutes(-1), now));
        Assert.AreEqual("5 minutes ago", AppModel.Relative(now.AddMinutes(-5), now));
        Assert.AreEqual("3 hours ago", AppModel.Relative(now.AddHours(-3), now));
        Assert.AreEqual("yesterday", AppModel.Relative(now.AddDays(-1), now));
        Assert.AreEqual("3 days ago", AppModel.Relative(now.AddDays(-3), now));
        Assert.AreEqual(now.AddDays(-30).ToString("d"), AppModel.Relative(now.AddDays(-30), now));
    }

    [TestMethod]
    public void SuggestsACommandNameFromTheDisplayName()
    {
        Assert.AreEqual("work", AppModel.SuggestCommandName("Work"));
        Assert.AreEqual("work-account-2", AppModel.SuggestCommandName("  Work  Account #2 "));
        Assert.AreEqual("", AppModel.SuggestCommandName("éé"));
        Assert.AreEqual(24, AppModel.SuggestCommandName(new string('a', 40)).Length);
    }

    [TestMethod]
    public async Task LoadsProfilesAndSaysWhatPlainClaudeUses()
    {
        using var h = new Harness(installed: false);
        await h.Model.Refresh();
        Assert.IsNull(h.Model.ErrorText);
        Assert.AreEqual(1, h.Model.ProfileItems.Count);
        Assert.AreEqual($"plain claude uses {h.Builtin.DisplayName}", h.Model.DefaultLine);
        Assert.IsFalse(h.Model.ShellInstalled);
        Assert.IsTrue(h.Model.ShowShellNotice);
        Assert.IsTrue(h.Model.ProfileItems[0].IsDefault);
    }

    [TestMethod]
    public async Task PanelShowsEightSessionsAndTheFilterSearchesTitleFolderAndProfile()
    {
        using var h = new Harness();
        var work = h.Ready("Work", "work");
        for (var i = 0; i < 10; i++) h.Transcript(h.Builtin, Harness.Id(i), $"task {i}", @"C:\code\app", minutesAgo: 10 + i);
        h.Transcript(work, Harness.Id(99), "zeta report", @"D:\reports", minutesAgo: 1);
        await h.Model.Refresh();
        Assert.AreEqual(AppModel.PanelRowLimit, h.Model.PanelSessions.Count);
        Assert.AreEqual(11, h.Model.WindowSessions.Count);
        Assert.AreEqual("zeta report", h.Model.PanelSessions[0].Title, "newest first");

        h.Model.Filter = "ZETA";
        Assert.AreEqual(1, h.Model.PanelSessions.Count);
        h.Model.Filter = @"d:\rep";
        Assert.AreEqual(1, h.Model.PanelSessions.Count);
        h.Model.Filter = "work";
        Assert.AreEqual(1, h.Model.WindowSessions.Count, "matches the profile name");
        h.Model.Filter = "nothing like this";
        Assert.IsTrue(h.Model.PanelEmpty);
        Assert.AreEqual("No sessions match this filter.", h.Model.PanelEmptyText);
    }

    [TestMethod]
    public async Task SidebarPicksTheProfileForTheTableAndTheNewTerminalButton()
    {
        using var h = new Harness();
        var work = h.Ready("Work", "work");
        h.Transcript(h.Builtin, Harness.Id(1), "one", @"C:\a");
        h.Transcript(work, Harness.Id(2), "two", @"C:\b");
        await h.Model.Refresh();
        Assert.AreEqual(3, h.Model.Sidebar.Count);
        Assert.AreEqual("2 sessions", h.Model.Sidebar[0].Subtitle);
        Assert.AreEqual($"New Terminal on {h.Builtin.DisplayName}", h.Model.TargetName);

        h.Model.SidebarSelection = work.Id;
        Assert.AreEqual("two", h.Model.WindowSessions.Single().Title);
        Assert.AreEqual("New Terminal on Work", h.Model.TargetName);
        await h.Model.NewSessionOnTarget();
        StringAssert.Contains(h.Opener.CommandLines.Single(), "--launch");
    }

    [TestMethod]
    public async Task UnchangedListsAreKeptSoSelectionSurvivesARecheck()
    {
        using var h = new Harness();
        h.Transcript(h.Builtin, Harness.Id(1), "one", @"C:\a");
        await h.Model.Refresh();
        var before = h.Model.WindowSessions;
        var profiles = h.Model.ProfileItems;
        var raised = new List<string>();
        h.Model.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);
        await h.Model.Tick();
        Assert.AreSame(before, h.Model.WindowSessions);
        Assert.AreSame(profiles, h.Model.ProfileItems);
        CollectionAssert.DoesNotContain(raised, nameof(AppModel.WindowSessions));

        h.Transcript(h.Builtin, Harness.Id(2), "two", @"C:\b");
        await h.Model.Tick();
        Assert.AreNotSame(before, h.Model.WindowSessions);
        Assert.AreEqual(2, h.Model.WindowSessions.Count);
    }

    [TestMethod]
    public async Task ASignInIsAnnouncedOnce()
    {
        using var h = new Harness();
        var p = h.S.Store.Create("Work", "work", "#0F8A7E");
        await h.Model.Refresh();
        var seen = new List<string>();
        h.Model.SignInDetected += (profile, account) => seen.Add($"{profile.DisplayName} {account.Email}");
        await h.Model.CheckSignIns();
        Assert.AreEqual(0, seen.Count, "not signed in yet");

        h.S.SignIn(p, "u-9", "work@example.com");
        await h.Model.CheckSignIns();
        await h.Model.CheckSignIns();
        CollectionAssert.AreEqual(new[] { "Work work@example.com" }, seen);
        Assert.AreEqual(ProfileState.PendingLogin, h.S.Store.Load().ProfileById(p.Id)!.State, "nothing is kept without Keep");
    }

    [TestMethod]
    public async Task AddingAProfileOpensSignInAndWaits()
    {
        using var h = new Harness();
        await h.Model.Refresh();
        await h.Model.AddProfile("Work", "work", "#8B5CF6");
        Assert.IsNull(h.Model.ErrorText);
        var p = h.S.Store.Load().Profiles.Single(x => x.CommandName == "work");
        Assert.AreEqual(ProfileState.PendingLogin, p.State);
        Assert.AreEqual(new Route.Waiting(p.Id), h.Model.Route);
        Assert.IsInstanceOfType<LoginState.Waiting>(h.Model.LoginState);
        Assert.AreEqual(1, h.Opener.CommandLines.Count);
        Assert.IsTrue(h.Model.ProfileItems.Single(i => i.Id == p.Id).IsPending);

        h.S.SignIn(p, "u-5", "w@example.com");
        await h.Model.PollLogin(p.Id);
        Assert.IsInstanceOfType<LoginState.Detected>(h.Model.LoginState);
        await h.Model.KeepLogin(p.Id);
        Assert.AreEqual(Route.Home, h.Model.Route);
        Assert.AreEqual(ProfileState.Ready, h.S.Store.Load().ProfileById(p.Id)!.State);
    }

    [TestMethod]
    public async Task ABadNameIsShownAndNothingOpens()
    {
        using var h = new Harness();
        await h.Model.Refresh();
        await h.Model.AddProfile("Main again", "main", "#8B5CF6");
        Assert.IsNotNull(h.Model.ErrorText);
        Assert.AreEqual(0, h.Opener.CommandLines.Count);
        Assert.IsTrue(h.Model.IsMain);
    }

    [TestMethod]
    public async Task ResumingIntoAMissingFolderOffersToChooseOne()
    {
        using var h = new Harness();
        h.Transcript(h.Builtin, Harness.Id(1), "gone", Path.Combine(h.S.Home, "no-such-folder"));
        await h.Model.Refresh();
        var row = h.Model.WindowSessions.Single().Row;
        await h.Model.Resume(row);
        Assert.AreEqual(row, h.Model.MissingFolderRow);
        Assert.IsNull(h.Model.ErrorText);
        Assert.AreEqual(0, h.Opener.CommandLines.Count);

        await h.Model.Resume(row, h.S.Home);
        Assert.AreEqual(1, h.Opener.CommandLines.Count);
    }

    [TestMethod]
    public async Task CommandLineSetupNeedsTheAppsCopyOfTheTools()
    {
        using var h = new Harness(installed: false);
        await h.Model.SetUpCommandLine(completion: false);
        StringAssert.Contains(h.Model.ErrorText, "build.ps1 -Install");
        Assert.AreEqual(0, h.UserPath.Writes);
    }

    [TestMethod]
    public async Task CommandLineSetupCopiesToolsAndPutsBinFirst()
    {
        var source = Path.Combine(Path.GetTempPath(), "cpm-ui-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        try
        {
            foreach (var f in new[] { "cpm.exe", "claude.exe", ShellInstaller.CompletionScript }) File.WriteAllText(Path.Combine(source, f), "x");
            using var h = new Harness(installed: false, shellSource: source);
            await h.Model.Refresh();
            await h.Model.SetUpCommandLine(completion: false);
            Assert.IsNull(h.Model.ErrorText);
            Assert.IsTrue(h.Model.InstallResult!.PathChanged);
            Assert.IsTrue(h.Model.ShellInstalled);
            StringAssert.StartsWith(h.UserPath.Value, h.S.Locator.BinDir);
            Assert.IsFalse(Directory.Exists(Path.Combine(h.S.Home, "Documents")), "no completion unless asked");
        }
        finally { Directory.Delete(source, recursive: true); }
    }
}

sealed class StopHelper(FakeProcesses processes) : IConsoleHelper
{
    public int Sends { get; private set; }

    public string? SendCtrlC(int pid)
    {
        Sends++;
        processes.Running.Remove(pid);
        return null;
    }

    public IReadOnlyList<int> ConsoleMembers(int pid) => [];
}
