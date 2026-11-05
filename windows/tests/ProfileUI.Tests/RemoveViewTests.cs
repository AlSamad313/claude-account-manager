using System.Windows.Controls;
using ProfileCore;
using static ProfileUI.Tests.Ui;

namespace ProfileUI.Tests;

/// Removing a profile from the app (design flow 7), as AppFlowTests checks it on macOS.
[TestClass, DoNotParallelize]
public sealed class RemoveViewTests
{
    [TestMethod]
    public void RemoveProfileMovesTheDefaultAndClearsTheSelection() => Run(async () =>
    {
        using var h = new Harness();
        var a = h.Ready("Work", "work");
        h.S.Store.SetDefault(a.Id);
        await h.Model.Refresh();
        h.Model.SidebarSelection = a.Id;
        h.Model.Route = new Route.Remove(a.Id);
        await h.Model.RemoveProfile(a.Id);
        Assert.IsNull(h.Model.ProfileById(a.Id));
        Assert.AreEqual(Profile.BuiltinId, h.Model.DefaultProfileId);
        Assert.AreEqual("all", h.Model.SidebarSelection);
        Assert.IsTrue(h.Model.IsMain);
        Assert.IsNull(h.Model.ErrorText);
        Assert.IsFalse(Directory.Exists(a.ConfigDir));
        Assert.Contains(a.ConfigDir!, h.Trashed);
    });

    [TestMethod]
    public void RemoveIsRefusedDuringALiveSessionAndStaysPut() => Run(async () =>
    {
        using var h = new Harness();
        var a = h.Ready("Work", "work");
        h.Live(a, 5, "s1");
        await h.Model.Refresh();
        h.Model.Route = new Route.Remove(a.Id);
        await h.Model.RemoveProfile(a.Id);
        Assert.IsNotNull(h.Model.ProfileById(a.Id));
        Assert.AreEqual(new Route.Remove(a.Id), h.Model.Route);
        Assert.Contains("live session", h.Model.ErrorText ?? "");
        Assert.IsTrue(Directory.Exists(a.ConfigDir));
    });

    /// Through the screens: Settings, Remove…, Cancel back to Settings, then Remove profile.
    [TestMethod]
    public void TheButtonsWalkThroughIt() => Run(async () =>
    {
        using var h = new Harness();
        var a = h.Ready("Work", "work");
        h.S.Store.SetDefault(a.Id);
        await h.Model.Refresh();
        var w = Host(new PanelView(), h.Model);
        try
        {
            h.Model.Route = new Route.Settings(a.Id);
            Click(await UntilButton(w, "Remove…"));
            await Until(() => HasText(w, "Remove Work?"), "the question");
            Assert.IsTrue(HasText(w, "It is the default, so the built-in profile becomes the default first."));
            Assert.IsTrue(HasText(w, "Recycle Bin"));
            Click(Button(w, "Cancel"));
            await Until(() => h.Model.Route is Route.Settings, "back to Settings");
            Click(await UntilButton(w, "Remove…"));
            Click(await UntilButton(w, "Remove profile"));
            await Until(() => h.Model.IsMain && h.Model.ProfileById(a.Id) == null, "removed, and back to the list");
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void ALiveSessionTurnsTheButtonOff() => Run(async () =>
    {
        using var h = new Harness();
        var a = h.Ready("Work", "work");
        h.Live(a, 5, "s1");
        await h.Model.Refresh();
        var w = Host(new PanelView(), h.Model);
        try
        {
            h.Model.Route = new Route.Remove(a.Id);
            var remove = await UntilButton(w, "Remove profile");
            Assert.IsFalse(remove.IsEnabled);
            Assert.IsTrue(HasText(w, "This profile has a live session. Stop it first; removal is refused while one is running."));
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void TheBuiltinProfileHasNoRemoveButton() => Run(async () =>
    {
        using var h = new Harness();
        await h.Model.Refresh();
        var w = Host(new PanelView(), h.Model);
        try
        {
            h.Model.Route = new Route.Settings(Profile.BuiltinId);
            await Until(() => HasText(w, "Profile settings"), "Settings");
            Assert.IsFalse(All(w).OfType<Button>().Any(b => b.Content as string == "Remove…" && b.IsVisible));
            Assert.IsTrue(HasText(w, "cannot be removed"));
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void AnInterruptedRemovalFinishesOnRefresh() => Run(async () =>
    {
        using var h = new Harness();
        var a = h.Ready("Work", "work");
        h.S.Store.MarkDeleting(a.Id);
        await h.Model.Refresh();
        Assert.IsNull(h.Model.ProfileById(a.Id));
        Assert.AreEqual("Finished removing Work.", h.Model.NoticeText);
    });

    static async Task<Button> UntilButton(System.Windows.Window w, string name)
    {
        await Until(() => All(w).OfType<Button>().Any(b => b.Content as string == name && b.IsVisible), name);
        return All(w).OfType<Button>().First(b => b.Content as string == name && b.IsVisible);
    }
}
