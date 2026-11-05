using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ProfileCore;
using ProfileUI;
using static ProfileUI.Tests.Ui;

namespace ProfileUI.Tests;

/// New Terminal asks for a folder, as on macOS since 41bf56e.
[TestClass, DoNotParallelize]
public sealed class NewTerminalFolderTests
{
    [TestMethod]
    public void NewTerminalStartsInTheChosenFolder() => Run(async () =>
    {
        using var h = new Harness();
        var a = h.Ready("A", "a");
        var folder = Path.Combine(h.S.Home, "it's here");
        Directory.CreateDirectory(folder);
        h.ChosenFolder = folder;
        await h.Model.Refresh();
        await h.Model.NewSession(a);
        CollectionAssert.AreEqual(new[] { "A" }, h.FolderAskedFor);
        Assert.HasCount(1, h.Opener.CommandLines);
        Assert.IsNull(h.Model.ErrorText);
        // The folder travels in the launch request, which the wrapper reads in the new window.
        var request = Directory.EnumerateFiles(h.Model.Env.Launcher.RunDir, "*" + ProfileCore.Shared.LaunchRequest.Extension).Single();
        Assert.AreEqual(folder, ProfileCore.Shared.LaunchRequest.Parse(File.ReadAllBytes(request))!.Folder, "the session starts in the chosen folder");
    });

    [TestMethod]
    public void CancellingTheFolderStartsNothing() => Run(async () =>
    {
        using var h = new Harness();
        var a = h.Ready("A", "a");
        h.ChosenFolder = null;
        await h.Model.Refresh();
        await h.Model.NewSession(a);
        Assert.IsEmpty(h.Opener.CommandLines);
        Assert.IsNull(h.Model.ErrorText);
    });

    [TestMethod]
    public void EveryNewTerminalButtonAsks() => Run(async () =>
    {
        using var h = new Harness();
        var work = h.Ready("Work", "work");
        await h.Model.Refresh();
        var panel = Host(new PanelView(), h.Model);
        try
        {
            Click(One<Button>(panel, b => b.Content as string == "New Terminal" && b.DataContext is ProfileItem p && p.Profile.Id == work.Id, "Work's New Terminal"));
            await Until(() => h.Opener.CommandLines.Count == 1, "a terminal from the panel");
        }
        finally { panel.Close(); }
        var w = Show(new MainWindow(h.Model));
        try
        {
            Click(w.NewTerminalButton);
            await Until(() => h.Opener.CommandLines.Count == 2, "a terminal from the window");
            // Ctrl+T: the window's own binding, since a test can't hold Ctrl down.
            var ctrlT = w.InputBindings.OfType<System.Windows.Input.KeyBinding>()
                .Single(b => b.Key == System.Windows.Input.Key.T && b.Modifiers == System.Windows.Input.ModifierKeys.Control);
            ctrlT.Command.Execute(null);
            await Until(() => h.Opener.CommandLines.Count == 3, "a terminal from Ctrl+T");
        }
        finally { w.Close(); }
        Assert.HasCount(3, h.FolderAskedFor, "one folder question per terminal");
    });
}

[TestClass, DoNotParallelize]
public sealed class GuideTests
{
    [TestMethod]
    public void EveryTopicStaysShortAndHasNoPersonalDetails()
    {
        Assert.HasCount(GuideTopic.All.Length, GuideTopic.All.Select(t => t.Title).Distinct());
        Assert.IsLessThanOrEqualTo(12, GuideTopic.All.Length);
        foreach (var t in GuideTopic.All)
        {
            Assert.IsLessThanOrEqualTo(55, t.Summary.Length, t.Summary);
            Assert.IsLessThanOrEqualTo(4, t.Steps.Length, t.Title);
            Assert.IsTrue(t.Steps.Length > 0 || t.Commands != null || t.Tip != null, $"{t.Title} has nothing to show");
            foreach (var s in t.Steps) Assert.IsLessThanOrEqualTo(55, s.Length, s);
            foreach (var text in t.Steps.Append(t.Summary).Append(t.Tip ?? "")) Assert.DoesNotContain("@", text);
        }
    }

    /// Button names in the steps are the ones on screen, in Windows words.
    [TestMethod]
    public void TheStepsUseWindowsWords()
    {
        var all = string.Join(" ", GuideTopic.All.SelectMany(t => t.Steps.Append(t.Summary).Append(t.Tip ?? "")));
        foreach (var mac in new[] { "Terminal window", "Trash", "menu bar", "Quit", "Open Terminal" })
            Assert.DoesNotContain(mac, all, $"\"{mac}\" is a macOS word");
    }

    [TestMethod]
    public void TheGuideOpensOneTopicAtATimeAndCloses() => Run(async () =>
    {
        using var h = new Harness();
        await h.Model.Refresh();
        var w = Host(new PanelView(), h.Model);
        try
        {
            Click(Button(w, "Guide"));
            await Until(() => h.Model.Route is Route.Guide && Maybe<Expander>(w) != null, "the Guide");
            var topics = All(w).OfType<Expander>().Distinct().ToList();
            Expander Topic(string title) => topics.Single(e => System.Windows.Automation.AutomationProperties.GetName(e) == title);
            Assert.HasCount(GuideTopic.All.Length, topics);
            Assert.IsTrue(topics.All(e => !e.IsExpanded), "all folded at first");
            Topic("Add an account").IsExpanded = true;
            Topic("Start a session").IsExpanded = true;
            Assert.IsFalse(Topic("Add an account").IsExpanded, "opening one folds the other");
            await Until(() => HasText(w, "Choose a folder, then press Select Folder."), "the open topic's steps");
            Assert.IsFalse(HasText(w, "Press Copy setup…"), "a folded topic's steps are hidden");
            Click(Button(w, "Done"));
            await Until(() => h.Model.IsMain, "back to the list");
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void F1OpensTheGuideInTheWindow() => Run(async () =>
    {
        using var h = new Harness();
        await h.Model.Refresh();
        var w = Show(new MainWindow(h.Model));
        try
        {
            Key(w, System.Windows.Input.Key.F1);
            await Until(() => h.Model.Route is Route.Guide, "the Guide");
        }
        finally { w.Close(); }
    });
}

/// The tray icon, as MenuBarIconTests checks the macOS one.
[TestClass, DoNotParallelize]
public sealed class TrayImageTests
{
    static BitmapSource Mask()
    {
        Assert.IsNotNull(TrayImage.BundledMask, "Resources/BurstMask.png is bundled");
        return TrayImage.BundledMask!;
    }

    [TestMethod]
    public void TheTileTakesTheProfileColour() => Run(() =>
    {
        // Just inside the corner radius, away from the burst.
        var c = TrayImage.Pixel(TrayImage.Render("#2F6FDE", 36, Mask()), 6, 6);
        Assert.IsGreaterThan(c.R + 70, (int)c.B, $"{c}");
        var o = TrayImage.Pixel(TrayImage.Render("#E8772E", 36, Mask()), 6, 6);
        Assert.IsGreaterThan(o.B + 70, (int)o.R, $"{o}");
        Assert.AreEqual(0, TrayImage.Pixel(TrayImage.Render("#E8772E", 36, Mask()), 0, 0).A, "the corner is transparent");
        return Task.CompletedTask;
    });

    [TestMethod]
    public void TheBurstIsWhiteOnADarkColourAndDarkOnALightOne() => Run(() =>
    {
        Assert.AreEqual(Colors.White, TrayImage.BurstColor(TrayImage.ParseColor("#2F6FDE")));
        Assert.AreEqual(Colors.White, TrayImage.BurstColor(TrayImage.ParseColor("#E8772E")));
        Assert.AreNotEqual(Colors.White, TrayImage.BurstColor(TrayImage.ParseColor("#FFF2A0")));
        // The middle of the burst is drawn in that colour.
        var mid = TrayImage.Pixel(TrayImage.Render("#2F6FDE", 72, Mask()), 36, 15);
        Assert.IsTrue(mid.R > 200 && mid.G > 200, $"a white ray at the top: {mid}");
        return Task.CompletedTask;
    });

    [TestMethod]
    public void WithoutTheMaskItStillShowsTheColour() => Run(() =>
    {
        var c = TrayImage.Pixel(TrayImage.Render("#2F6FDE", 36, null), 6, 6);
        Assert.IsGreaterThan(c.R + 70, (int)c.B);
        return Task.CompletedTask;
    });

    [TestMethod]
    public void ABadColourFallsBackToGrey() => Run(() =>
    {
        Assert.AreEqual(TrayImage.ParseColor(AppModel.Grey), TrayImage.ParseColor("not a colour"));
        return Task.CompletedTask;
    });

    /// Only with CPM_RENDER_DIR set: PNGs of the tray icon at the sizes Windows uses, to look at.
    [TestMethod]
    public void Render() => Run(() =>
    {
        if (Environment.GetEnvironmentVariable("CPM_RENDER_DIR") is not { Length: > 0 } dir) return Task.CompletedTask;
        foreach (var (name, hex) in new[] { ("blue", "#2F6FDE"), ("orange", "#E8772E"), ("teal", "#0F8A7E"), ("purple", "#8B5CF6"), ("cream", "#FFF2A0") })
            foreach (var size in new[] { 16, 24, 32, 72 })
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(TrayImage.Render(hex, size, Mask())));
                using var f = File.Create(Path.Combine(dir, $"tray-{name}-{size}.png"));
                enc.Save(f);
            }
        return Task.CompletedTask;
    });
}
