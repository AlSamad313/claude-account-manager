using System.Windows;
using System.Windows.Controls;
using ProfileCore;
using ProfileUI;
using static ProfileUI.Tests.Ui;

namespace ProfileUI.Tests;

/// Copy setup in the panel, over a temporary home, as E2E checklist part 8 runs it. Claude Code's own
/// commands are faked; files are real.
[TestClass, DoNotParallelize]
public sealed class CopySetupViewTests
{
    static async Task<(Harness H, Window W)> Open(Action<Harness>? setup = null)
    {
        var h = new Harness();
        setup?.Invoke(h);
        await h.Model.Refresh();
        var w = Host(new PanelView(), h.Model);
        Click(Button(w, "Copy setup…"));
        return (h, w);
    }

    static void Close(Harness h, Window w)
    {
        w.Close();
        h.Dispose();
    }

    /// The built-in profile signed in, with a setup to copy, and a signed-in Work profile.
    static Profile Setup(Harness h)
    {
        h.SignInBuiltin();
        h.S.Write("""{"permissions":{"allow":["Bash(npm test)"]}}""", ".claude/settings.json");
        h.S.Write("hello", ".claude/skills/s1/SKILL.md");
        h.S.Write("memory", ".claude/CLAUDE.md");
        return h.Ready("Work", "work");
    }

    static CheckBox Tick(Window w, string key) => One<CheckBox>(w, c => AutomationPropertiesName(c) == key, $"checkbox \"{key}\"");

    static string AutomationPropertiesName(DependencyObject d) => System.Windows.Automation.AutomationProperties.GetName(d);

    [TestMethod]
    public void E2E_8_1_WithNoOtherProfileItSaysWhatToDo() => Run(async () =>
    {
        var (h, w) = await Open();
        try
        {
            await Until(() => HasText(w, "Add and sign in to another profile first"), "the explanation");
            Click(Button(w, "Done"));
            await Until(() => h.Model.IsMain, "back to the list");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_8_2_PreviewThenCopyThenRestore() => Run(async () =>
    {
        Profile? work = null;
        var (h, w) = await Open(h => work = Setup(h));
        try
        {
            await Until(() => h.Model.CopyPlan != null, "the preview");
            Assert.AreEqual(Profile.BuiltinId, One<ComboBox>(w, c => AutomationPropertiesName(c) == "From").SelectedValue, "the built-in profile is the default source");
            Assert.AreEqual(work!.Id, One<ComboBox>(w, c => AutomationPropertiesName(c) == "To").SelectedValue);
            foreach (var title in new[] { "SETTINGS", "SKILLS, AGENTS, COMMANDS, CLAUDE.MD" }) Assert.IsTrue(HasText(w, title), title);
            Assert.IsTrue(HasText(w, "+ Bash(npm test)"));
            Assert.IsTrue(HasText(w, "signed in as work@example.com"), "the account note names the target account");
            Assert.IsNotNull(MaybeButton(w, "Copy 3 items"));
            Assert.IsFalse(File.Exists(Path.Combine(work.ConfigDir!, "CLAUDE.md")), "nothing is written by the preview");

            Click(Tick(w, "CLAUDE.md"));
            await Until(() => MaybeButton(w, "Copy 2 items") != null, "the count follows the ticks");
            Click(Button(w, "Copy 2 items"));
            await Until(() => HasText(w, "Copied 2 items."), "the result");
            Assert.IsTrue(HasText(w, @"A backup is in .claude-profiles\backups."));
            Assert.AreEqual("hello", File.ReadAllText(Path.Combine(work.ConfigDir!, "skills", "s1", "SKILL.md")));
            Assert.IsFalse(File.Exists(Path.Combine(work.ConfigDir!, "CLAUDE.md")), "an unticked item is not copied");
            StringAssert.Contains(File.ReadAllText(Path.Combine(work.ConfigDir!, "settings.json")), "Bash(npm test)");
            await Until(() => h.Model.CopyPlan?.Items.First(i => i.Id == "file:skills/s1").Action == CopyAction.Same, "the preview reloads after copying");

            var restore = Button(w, "Restore…");
            Click(restore);
            await Until(() => restore.ContextMenu.Items.Count == 1, "the backup in the Restore menu");
            var item = restore.ContextMenu.Items.OfType<MenuItem>().Single();
            Assert.IsTrue(item.IsEnabled, "a real backup, not \"No backups yet\"");
            Click(item);
            await Until(() => HasText(w, "Restored 1 and deleted 1 file."), "the restore result");
            Assert.IsFalse(File.Exists(Path.Combine(work.ConfigDir!, "skills", "s1", "SKILL.md")));
            Assert.DoesNotContain("npm test", File.ReadAllText(Path.Combine(work.ConfigDir!, "settings.json")));

            Click(Button(w, "Done"));
            await Until(() => h.Model.IsMain && h.Model.CopyPlan == null, "back to the list, with the preview cleared");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_8_3_ASecretStaysOutUnlessTicked() => Run(async () =>
    {
        var (h, w) = await Open(h =>
        {
            Setup(h);
            h.S.Write("""{"oauthAccount":{"accountUuid":"u-builtin","emailAddress":"me@example.com"},"mcpServers":{"srv":{"command":"node","env":{"TOKEN":"sekret-value"}}}}""", ".claude.json");
        });
        try
        {
            await Until(() => h.Model.CopyPlan != null, "the preview");
            var srv = Tick(w, "srv");
            Assert.IsFalse(srv.IsChecked, "a server with env values starts unticked");
            Assert.IsTrue(HasText(w, "May contain a secret"));
            Assert.IsTrue(HasText(w, "other programs you run can read it from Claude Code's command line"), "the Windows warning");
            Assert.IsFalse(HasText(w, "sekret"), "a value is never shown");
            Assert.IsNull(Maybe<CheckBox>(w, c => c.Content as string == "Include env.TOKEN = ••••••"));

            Click(srv);
            await Until(() => Maybe<CheckBox>(w, c => c.Content as string == "Include env.TOKEN = ••••••") != null, "the secret's own tick");
            Click(Button(w, "Copy 4 items"));
            await Until(() => h.Model.CopyResult != null, "the result");
            var json = h.Claude.Calls.Single(c => c.Args.Contains("add-json")).Args[3];
            StringAssert.Contains(json, "\"command\":\"node\"");
            Assert.DoesNotContain("sekret", json, "the unticked secret was left out");
            await Until(() => Box(w, "Commands to remove").Text.Contains("claude-as work mcp remove srv --scope user", StringComparison.Ordinal), "how to undo the install");
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_8_4_AnExistingFileOffersSkipOverwriteOrRename() => Run(async () =>
    {
        Profile? work = null;
        var (h, w) = await Open(h =>
        {
            work = Setup(h);
            File.WriteAllText(Path.Combine(work.ConfigDir!, "CLAUDE.md"), "theirs");
        });
        try
        {
            await Until(() => h.Model.CopyPlan != null, "the preview");
            var box = Tick(w, "CLAUDE.md");
            Assert.IsFalse(box.IsChecked, "an existing file starts unticked");
            Assert.IsTrue(HasText(w, "Exists in the target."));
            Click(box);
            await Until(() => Maybe<RadioButton>(w, r => r.Content as string == "Rename") != null, "the choices");
            One<RadioButton>(w, r => r.Content as string == "Rename").IsChecked = true;
            Click(Button(w, "Copy 3 items"));
            await Until(() => h.Model.CopyResult != null, "the result");
            Assert.AreEqual("theirs", File.ReadAllText(Path.Combine(work!.ConfigDir!, "CLAUDE.md")));
            Assert.AreEqual("memory", File.ReadAllText(Path.Combine(work.ConfigDir!, "CLAUDE-copy.md")));
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_8_5_ALiveSessionInTheTargetBlocksCopying() => Run(async () =>
    {
        var (h, w) = await Open(h => h.Live(Setup(h), 4242, Harness.Id(1)));
        try
        {
            await Until(() => h.Model.CopyPlan != null, "the preview");
            Assert.IsTrue(HasText(w, "Work has a live session. Stop it or wait for it to end, then copy."));
            Assert.IsFalse(Button(w, "Copy 3 items").IsEnabled);
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void E2E_8_6_ChangingFromKeepsToValid() => Run(async () =>
    {
        Profile? work = null, home = null;
        var (h, w) = await Open(h =>
        {
            work = Setup(h);
            home = h.Ready("Home", "home", "u-2");
        });
        try
        {
            await Until(() => h.Model.CopyPlan != null, "the preview");
            var from = One<ComboBox>(w, c => AutomationPropertiesName(c) == "From");
            var to = One<ComboBox>(w, c => AutomationPropertiesName(c) == "To");
            CollectionAssert.AreEqual(new[] { work!.Id, home!.Id }, to.Items.Cast<Profile>().Select(p => p.Id).ToArray(), "the built-in profile is never offered in To");
            from.SelectedValue = work.Id;
            await Until(() => h.Model.CopyPlan?.SourceId == work.Id, "a preview from Work");
            Assert.AreEqual(home.Id, to.SelectedValue, "To moves off the profile now in From");
            Assert.AreEqual(home.Id, h.Model.CopyPlan!.TargetId);
            CollectionAssert.DoesNotContain(to.Items.Cast<Profile>().Select(p => p.Id).ToArray(), work.Id);
        }
        finally { Close(h, w); }
    });

    [TestMethod]
    public void TheWindowOpensCopySetupFromItsMenu() => Run(async () =>
    {
        using var h = new Harness();
        Setup(h);
        await h.Model.Refresh();
        var w = Show(new MainWindow(h.Model));
        try
        {
            Click(All(w).OfType<MenuItem>().First(m => m.Header as string == "Copy setup…"));
            await Until(() => h.Model.Route is Route.Copy && h.Model.CopyPlan != null, "the preview over the window");
            await Until(() => MaybeButton(w, "Copy 3 items") != null, "its button");
        }
        finally { w.Close(); }
    });
}

[TestClass, DoNotParallelize]
public sealed class PanelLayoutTests
{
    /// The tray panel is 460 wide (PanelWindow). At the usual text size every footer button fits on one
    /// row inside it; with larger text the row wraps instead of cutting a button off.
    [TestMethod]
    public void TheFooterButtonsFitThePanel() => Run(async () =>
    {
        using var h = new Harness();
        await h.Model.Refresh();
        var w = Host(new PanelView(), h.Model, width: 460);
        try
        {
            await Until(() => MaybeButton(w, "Exit")?.ActualWidth > 0, "laid out");
            var root = (FrameworkElement)w.Content;
            double right = 0;
            foreach (var name in new[] { "Add profile…", "Open window…", "Copy setup…", "Doctor", "Guide" })
            {
                var b = Button(w, name);
                var x = b.TranslatePoint(new Point(0, 0), root).X;
                Assert.IsGreaterThanOrEqualTo(right, x, $"{name} overlaps the button before it");
                Assert.AreEqual(b.DesiredSize.Width, b.ActualWidth + b.Margin.Left + b.Margin.Right, 0.5, $"{name} is squeezed");
                Assert.AreEqual(Button(w, "Exit").TranslatePoint(new Point(0, 0), root).Y, b.TranslatePoint(new Point(0, 0), root).Y, 0.5, $"{name} is on the same row");
                right = x + b.ActualWidth;
            }
            var exit = Button(w, "Exit");
            Assert.IsLessThanOrEqualTo(exit.TranslatePoint(new Point(0, 0), root).X, right, "Guide runs into Exit");
            Assert.IsLessThanOrEqualTo(root.ActualWidth, exit.TranslatePoint(new Point(exit.ActualWidth, 0), root).X, "Exit is cut off");
        }
        finally { w.Close(); }
    });
}
