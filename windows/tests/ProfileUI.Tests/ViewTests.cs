using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ProfileCore;
using ProfileUI;
using static ProfileUI.Tests.Ui;

namespace ProfileUI.Tests;

/// The real panel, flow steps and window, over a temporary home. Each test clicks and types the way
/// the E2E checklist does, then checks the screen and the files.
[TestClass, DoNotParallelize]
public sealed class PanelViewTests
{
    static async Task<(Harness H, Window W)> Panel(bool installed = true, string? shellSource = null, Action<Harness>? setup = null)
    {
        var h = new Harness(installed, shellSource);
        setup?.Invoke(h);
        await h.Model.Refresh();
        return (h, Host(new PanelView(), h.Model));
    }

    static void Close(Harness h, Window w)
    {
        w.Close();
        h.Dispose();
    }

    [TestMethod]
    public void E2E_1_3_ThePanelShowsTitleDefaultAndTheSetupNotice() => Run(async () =>
    {
        var (h, w) = await Panel(installed: false);
        try
        {
            Assert.IsTrue(HasText(w, "Claude Profiles"));
            Assert.IsTrue(HasText(w, h.Model.DefaultLine));
            Assert.IsTrue(HasText(w, "Set up command line first"));
            foreach (var b in new[] { "Add profile…", "Open window…", "Doctor", "Exit" }) Button(w, b);
            Assert.IsTrue(HasText(w, "Filter by title, folder or profile"), "the filter hint");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_1_7_NewTerminalIsOffUntilTheCommandLineIsSetUp() => Run(async () =>
    {
        var (h, w) = await Panel(installed: false, setup: h => h.Ready("Work", "work"));
        try
        {
            Assert.IsFalse(Button(w, "New Terminal").IsEnabled);
            h.MarkInstalled();
            await h.Model.Refresh();
            await Until(() => Button(w, "New Terminal").IsEnabled, "New Terminal enabled");
            await Until(() => !HasText(w, "Set up command line first"), "notice gone");
            Click(Button(w, "New Terminal"));
            await Until(() => h.Opener.CommandLines.Count == 1, "a terminal opened");
            StringAssert.Contains(h.Opener.CommandLines[0], "--launch");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_1_8_AddProfileBeforeSetupCannotContinue() => Run(async () =>
    {
        var (h, w) = await Panel(installed: false);
        try
        {
            Click(Button(w, "Add profile…"));
            await Until(() => HasText(w, "Display name"), "the add step");
            Box(w, "Display name").Text = "Work";
            Assert.IsFalse(Button(w, "Continue to sign-in").IsEnabled);
            Assert.IsTrue(HasText(w, "Set up the command line first"));
            Click(Button(w, "Cancel"));
            await Until(() => h.Model.IsMain, "back to the list");
            Assert.AreEqual(1, h.S.Store.Load().Profiles.Count, "nothing was added");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_1_5_And_7_4_TheFilterNarrowsTheSessions() => Run(async () =>
    {
        var (h, w) = await Panel(setup: h =>
        {
            h.Transcript(h.Builtin, Harness.Id(1), "alpha task", @"C:\code\one");
            h.Transcript(h.Builtin, Harness.Id(2), "beta task", @"C:\code\two");
            h.Transcript(h.Builtin, Harness.Id(3), "gamma task", @"D:\elsewhere");
        });
        try
        {
            foreach (var t in new[] { "alpha task", "beta task", "gamma task" }) Assert.IsTrue(HasText(w, t), t);
            var filter = Box(w, "Filter by title, folder or profile");
            filter.Text = "beta";
            await Until(() => !HasText(w, "alpha task") && HasText(w, "beta task"), "title filter");
            Assert.IsFalse(HasText(w, "Filter by title, folder or profile"), "the hint hides while there is text");
            filter.Text = @"d:\else";
            await Until(() => HasText(w, "gamma task") && !HasText(w, "beta task"), "folder filter");
            filter.Text = "zzz";
            await Until(() => HasText(w, "No sessions match this filter."), "empty text");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_3_AddSignInAndKeep() => Run(async () =>
    {
        var (h, w) = await Panel();
        try
        {
            Click(Button(w, "Add profile…"));
            await Until(() => HasText(w, "Display name"), "the add step");
            Box(w, "Display name").Text = "My Work";
            await Until(() => HasText(w, "claude-as my-work"), "the suggested command name");
            Click(One<Button>(w, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Colour #8B5CF6", "purple swatch"));
            Click(Button(w, "Continue to sign-in"));
            await Until(() => HasText(w, "Waiting for sign-in…"), "the waiting step");
            Assert.AreEqual(1, h.Opener.CommandLines.Count, "the sign-in window opened");
            Assert.IsTrue(HasText(w, "claude-as my-work auth login"));
            var p = h.S.Store.Load().Profiles.Single(x => x.CommandName == "my-work");
            Assert.AreEqual("#8B5CF6", p.Color);
            Assert.AreEqual(ProfileState.PendingLogin, p.State);

            h.S.SignIn(p, "u-7", "work@example.com");
            await h.Model.Tick();
            await Until(() => HasText(w, "Signed in") && HasText(w, "work@example.com"), "the signed-in step");
            Assert.IsTrue(HasText(w, "Organization: Org"));
            Click(Button(w, "Keep"));
            await Until(() => h.Model.IsMain, "back to the list");
            Assert.AreEqual(ProfileState.Ready, h.S.Store.Load().ProfileById(p.Id)!.State);
            await Until(() => HasText(w, "My Work") && HasText(w, "work@example.com"), "the new row");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_4_8_TheSameAccountWarnsAndRetrySignsOut() => Run(async () =>
    {
        var (h, w) = await Panel(setup: h => h.Ready("Personal", "personal", uuid: "u-1"));
        try
        {
            await h.Model.AddProfile("Temp2", "temp2", "#0F8A7E");
            var p = h.S.Store.Load().Profiles.Single(x => x.CommandName == "temp2");
            h.S.SignIn(p, "u-1", "personal@example.com");
            await h.Model.Tick();
            await Until(() => HasText(w, "The profile Personal already uses this account"), "the duplicate warning");
            Click(Button(w, "Sign out and retry"));
            await Until(() => HasText(w, "Waiting for sign-in…"), "back to waiting");
            Assert.AreEqual(2, h.Opener.CommandLines.Count, "a new sign-in window");
            Click(Button(w, "Discard"));
            await Until(() => h.Model.IsMain, "back to the list");
            Assert.IsNull(h.S.Store.Load().Profiles.FirstOrDefault(x => x.CommandName == "temp2"));
            await Until(() => !HasText(w, "Temp2"), "the row has gone");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_4_9_CloseThenFinishSignInThenDiscard() => Run(async () =>
    {
        var (h, w) = await Panel();
        try
        {
            await h.Model.AddProfile("Temp3", "temp3", "#0F8A7E");
            await Until(() => HasText(w, "Waiting for sign-in…"), "the waiting step");
            Click(Button(w, "Close"));
            await Until(() => h.Model.IsMain, "back to the list");
            Click(Button(w, "Finish sign-in"));
            await Until(() => HasText(w, "Waiting for sign-in…"), "the waiting step again");
            Click(Button(w, "Discard"));
            await Until(() => h.Model.IsMain && MaybeButton(w, "Finish sign-in") == null, "the row has gone");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_5_7_MakeDefaultMovesTheMark() => Run(async () =>
    {
        var (h, w) = await Panel(setup: h => h.Ready("Work", "work"));
        try
        {
            Click(One<Hyperlink>(w, l => new TextRange(l.ContentStart, l.ContentEnd).Text == "Make default", "Make default"));
            await Until(() => h.Model.DefaultLine == "plain claude uses Work", "the default moved");
            Assert.AreEqual("work", h.S.Store.Load().ProfileById(h.Model.DefaultProfileId)!.CommandName);
            Assert.AreEqual("work\n", File.ReadAllText(Path.Combine(h.S.Locator.ProfilesRoot, "default")).Replace(h.Model.DefaultProfileId, "work"));
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_7_7_SettingsRenamesAndRecoloursButKeepsTheCommand() => Run(async () =>
    {
        Profile? work = null;
        var (h, w) = await Panel(setup: h => work = h.Ready("Work", "work"));
        try
        {
            var settings = Maybe<MenuItem>(w, m => m.Header as string == "Settings…" && m.DataContext is ProfileItem { Name: "Work" }, visibleOnly: false)
                ?? throw new AssertFailedException("Settings… for Work");
            Click(settings);
            await Until(() => HasText(w, "Profile settings"), "the settings step");
            Assert.IsTrue(HasText(w, "Command: claude-as work"));
            Assert.IsFalse(HasText(w, "This is your existing .claude folder"));
            Click(One<Button>(w, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Colour #C2337A", "pink swatch"));
            await Until(() => h.S.Store.Load().ProfileById(work!.Id)!.Color == "#C2337A", "the colour saved");
            Box(w, "Display name").Text = "Work 2";
            Click(Button(w, "Done"));
            await Until(() => h.Model.IsMain && h.S.Store.Load().ProfileById(work!.Id)!.DisplayName == "Work 2", "the name saved");
            Assert.AreEqual("work", h.S.Store.Load().ProfileById(work!.Id)!.CommandName);
            await Until(() => HasText(w, "Work 2"), "the row updated");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_7_7_TheBuiltInSaysItIsYourExistingFolder() => Run(async () =>
    {
        var (h, w) = await Panel();
        try
        {
            h.Model.ProfileItems.Single(i => i.Profile.Builtin).Settings.Execute(null);
            await Until(() => HasText(w, "Profile settings"), "the settings step");
            Assert.IsTrue(HasText(w, "This is your existing .claude folder"));
            Assert.IsNull(MaybeButton(w, "Remove"));
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_1_10_DoctorListsFindingsAndDoneCloses() => Run(async () =>
    {
        var (h, w) = await Panel();
        try
        {
            Click(Button(w, "Doctor"));
            await Until(() => h.Model.DoctorReport != null && !h.Model.DoctorRunning, "doctor finished");
            await Until(() => h.Model.DoctorReport!.Findings.All(f => HasText(w, f.Message)), "every finding shown");
            Click(Button(w, "Done"));
            await Until(() => h.Model.IsMain, "back to the list");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_2_1_SetUpCommandLineWithCompletion() => Run(async () =>
    {
        var source = Path.Combine(Path.GetTempPath(), "cpm-ui-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        foreach (var f in new[] { "cpm.exe", "claude.exe", ShellInstaller.CompletionScript }) File.WriteAllText(Path.Combine(source, f), "x");
        var (h, w) = await Panel(installed: false, shellSource: source, setup: h => h.Ready("Work", "work"));
        try
        {
            Click(Button(w, "Set up"));
            await Until(() => HasText(w, "Set up command line") && MaybeButton(w, "Cancel") != null, "the setup step");
            Click(One<CheckBox>(w, what: "the completion box"));
            Click(Button(w, "Set up"));
            await Until(() => HasText(w, "Done. Open a new terminal window to use claude-as."), "the done text");
            Assert.IsTrue(HasText(w, "The bin folder is now first in your user PATH."));
            Assert.IsTrue(HasText(w, "Added tab completion to"));
            Assert.AreEqual(1, h.UserPath.Writes);
            StringAssert.StartsWith(h.UserPath.Value, h.S.Locator.BinDir);
            Click(Button(w, "Done"));
            await Until(() => h.Model.IsMain && Button(w, "New Terminal").IsEnabled, "ready to launch");
        }
        finally { Close(h, w); Directory.Delete(source, true); }
    });

    [TestMethod]
    public void E2E_7_3_StopABusySessionAfterACancel() => Run(async () =>
    {
        var (h, w) = await Panel(setup: h =>
        {
            h.Transcript(h.Builtin, Harness.Id(5), "long job", h.S.Home);
            h.Live(h.Builtin, 4242, Harness.Id(5), status: "busy");
        });
        try
        {
            await Until(() => HasText(w, "running, busy"), "the live row");
            Click(Button(w, "Stop"));
            await Until(() => HasText(w, "Stop this session?"), "the question");
            Assert.IsTrue(HasText(w, "This sends Ctrl+C"));
            Assert.IsNotNull(new StopRequest("t", null, "busy", 1).Warning);
            Assert.IsTrue(HasText(w, new StopRequest("t", null, "busy", 1).Warning!), "the busy warning");
            Click(Button(w, "Cancel"));
            await Until(() => h.Model.IsMain, "back to the list");
            Assert.AreEqual(0, h.Helper.Sends, "Cancel sends nothing");

            Click(Button(w, "Stop"));
            await Until(() => MaybeButton(w, "Stop session") != null, "the question again");
            Click(Button(w, "Stop session"));
            await Until(() => h.Model.IsMain && !h.Model.Rows.Single().Live, "stopped");
            Assert.AreEqual(1, h.Helper.Sends);
            Assert.IsNull(h.Model.ErrorText);
            await Until(() => MaybeButton(w, "Resume") != null, "the row offers Resume");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_6_3_ALiveSessionOffersStopNotResume() => Run(async () =>
    {
        var (h, w) = await Panel(setup: h =>
        {
            h.Transcript(h.Builtin, Harness.Id(6), "running one", h.S.Home);
            h.Live(h.Builtin, 4343, Harness.Id(6));
        });
        try
        {
            await Until(() => MaybeButton(w, "Stop") != null, "Stop");
            Assert.IsNull(MaybeButton(w, "Resume"));
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_7_5_AMissingFolderOffersChooseFolder() => Run(async () =>
    {
        var (h, w) = await Panel(setup: h => h.Transcript(h.Builtin, Harness.Id(1), "gone", Path.Combine(h.S.Home, "no-such")));
        try
        {
            Click(Button(w, "Resume"));
            await Until(() => HasText(w, "no longer exists"), "the banner");
            Button(w, "Choose folder…");
            Assert.AreEqual(0, h.Opener.CommandLines.Count);
            Click(Button(w, "Cancel"));
            await Until(() => !HasText(w, "no longer exists"), "banner gone");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void ErrorsShowAndCanBeDismissed() => Run(async () =>
    {
        var (h, w) = await Panel(installed: false);
        try
        {
            h.Model.ErrorText = "Something specific went wrong.";
            await Until(() => HasText(w, "Something specific went wrong."), "the error");
            Click(One<Button>(w, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Dismiss", "dismiss"));
            await Until(() => !HasText(w, "Something specific went wrong."), "dismissed");
        }
        finally { Close(h, w); }
    });
}

[TestClass, DoNotParallelize]
public sealed class MainWindowTests
{
    [TestMethod]
    public void E2E_7_8_SidebarFiltersAndTheButtonFollows() => Run(async () =>
    {
        using var h = new Harness();
        var work = h.Ready("Work", "work");
        h.Transcript(h.Builtin, Harness.Id(1), "on main", h.S.Home);
        h.Transcript(work, Harness.Id(2), "on work", h.S.Home);
        await h.Model.Refresh();
        var w = Show(new MainWindow(h.Model));
        try
        {
            Assert.AreEqual(3, w.SidebarList.Items.Count);
            await Until(() => w.Sessions.Items.Count == 2, "both sessions");
            Assert.AreEqual($"New Terminal on {h.Builtin.DisplayName}", w.NewTerminalButton.Content);
            w.SidebarList.SelectedItem = h.Model.Sidebar.Single(s => s.Id == work.Id);
            await Until(() => w.Sessions.Items.Count == 1, "only Work");
            Assert.AreEqual("on work", ((SessionItem)w.Sessions.Items[0]).Title);
            Assert.AreEqual("New Terminal on Work", w.NewTerminalButton.Content);
            Click(w.NewTerminalButton);
            await Until(() => h.Opener.CommandLines.Count == 1, "a terminal opened");
            Assert.IsTrue(HasText(w, "1 session. Running ones are rechecked every 2 seconds"));
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void E2E_7_8_EnterResumesAndDeleteAsksToStop() => Run(async () =>
    {
        using var h = new Harness();
        h.Transcript(h.Builtin, Harness.Id(1), "old one", h.S.Home, minutesAgo: 30);
        h.Transcript(h.Builtin, Harness.Id(2), "live one", h.S.Home, minutesAgo: 1);
        h.Live(h.Builtin, 5151, Harness.Id(2));
        await h.Model.Refresh();
        var w = Show(new MainWindow(h.Model));
        try
        {
            await Until(() => w.Sessions.Items.Count == 2, "both sessions");
            var old = h.Model.WindowSessions.Single(s => !s.IsLive);
            var live = h.Model.WindowSessions.Single(s => s.IsLive);

            w.Sessions.SelectedItem = live;
            Key(w.Sessions, System.Windows.Input.Key.Enter);
            Assert.AreEqual(0, h.Opener.CommandLines.Count, "Enter does not resume a live session");

            w.Sessions.SelectedItem = old;
            Key(w.Sessions, System.Windows.Input.Key.Delete);
            Assert.IsTrue(h.Model.IsMain, "Delete does nothing on a stopped session");
            Key(w.Sessions, System.Windows.Input.Key.Enter);
            await Until(() => h.Opener.CommandLines.Count == 1, "Enter resumed it");

            w.Sessions.SelectedItem = h.Model.WindowSessions.Single(s => s.IsLive);
            Key(w.Sessions, System.Windows.Input.Key.Delete);
            await Until(() => h.Model.Route is Route.Stop && HasText(w, "Stop this session?"), "the stop question over the window");
            Click(Button(w, "Cancel"));
            await Until(() => h.Model.IsMain, "back");
            Assert.AreEqual(0, h.Helper.Sends);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void TheSelectionSurvivesTheRecheck() => Run(async () =>
    {
        using var h = new Harness();
        h.Transcript(h.Builtin, Harness.Id(1), "one", h.S.Home, minutesAgo: 30);
        h.Transcript(h.Builtin, Harness.Id(2), "two", h.S.Home, minutesAgo: 20);
        await h.Model.Refresh();
        var w = Show(new MainWindow(h.Model));
        try
        {
            await Until(() => w.Sessions.Items.Count == 2, "both");
            w.Sessions.SelectedItem = h.Model.WindowSessions.Single(s => s.Title == "one");
            await h.Model.Tick();
            Assert.AreEqual("one", ((SessionItem)w.Sessions.SelectedItem).Title, "unchanged list");
            h.Transcript(h.Builtin, Harness.Id(3), "three", h.S.Home, minutesAgo: 1);
            await h.Model.Tick();
            await Until(() => w.Sessions.Items.Count == 3, "the new session");
            Assert.AreEqual("one", ((SessionItem?)w.Sessions.SelectedItem)?.Title, "selection kept after the list changed");
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void E2E_7_9_APanelStepAlsoShowsOverTheWindow() => Run(async () =>
    {
        using var h = new Harness();
        await h.Model.Refresh();
        var panel = Host(new PanelView(), h.Model);
        var w = Show(new MainWindow(h.Model));
        try
        {
            Click(Button(panel, "Add profile…"));
            await Until(() => w.Overlay.IsVisible && HasText(w.Overlay, "Add profile"), "the step over the window");
            Click(Button(w.Overlay, "Cancel"));
            await Until(() => !w.Overlay.IsVisible && h.Model.IsMain, "both back");
        }
        finally { w.Close(); panel.Close(); }
    });

    [TestMethod]
    public void E2E_7_10_DoctorRunsOnceWithTwoHosts() => Run(async () =>
    {
        using var h = new Harness();
        await h.Model.Refresh();
        var panel = Host(new PanelView(), h.Model);
        panel.Hide();
        var w = Show(new MainWindow(h.Model));
        try
        {
            var runs = 0;
            h.Model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppModel.DoctorRunning) && h.Model.DoctorRunning) runs++; };
            h.Model.Route = new Route.Doctor();
            await Until(() => h.Model.DoctorReport != null && !h.Model.DoctorRunning, "doctor finished");
            await Task.Delay(300);
            Assert.AreEqual(1, runs, "only the visible host started Doctor");
        }
        finally { w.Close(); panel.Close(); }
    });
}
