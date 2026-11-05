using System.Diagnostics;
using System.Windows.Automation;
using Microsoft.Win32;
using ProfileCore.Tests;

namespace ProfileUI.Tests;

/// The built ClaudeProfiles.exe, started against a temporary home as a test copy
/// (CLAUDE_PROFILES_TEST_INSTANCE), and driven through Windows UI Automation. It runs beside an
/// installed copy without touching it: its own instance names and settings key, no notifications,
/// no registering, and no Start with Windows change. build.ps1 builds the exe before the tests.
[TestClass, DoNotParallelize]
public sealed class AppProcessTests
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static string? BuiltApp()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "build", "app", "ClaudeProfiles.exe");
            if (File.Exists(f)) return f;
        }
        return null;
    }

    sealed class TestApp : IDisposable
    {
        public Sandbox S { get; } = new();
        public string Name { get; } = "t" + Guid.NewGuid().ToString("N")[..10];
        public string Exe { get; }
        public Process? Main { get; private set; }
        public string SettingsKey => @"Software\ClaudeProfiles." + Name;

        public TestApp(string exe) => Exe = exe;

        public Process Start(params string[] args) => Launch(true, args);

        Process Launch(bool main, string[] args)
        {
            var psi = new ProcessStartInfo(Exe) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment["USERPROFILE"] = S.Home;
            psi.Environment["CLAUDE_PROFILES_TEST_INSTANCE"] = Name;
            var p = Process.Start(psi)!;
            if (main) Main ??= p;
            return p;
        }

        public int RunToEnd(params string[] args)
        {
            using var p = Launch(false, args);
            Assert.IsTrue(p.WaitForExit(20_000), $"{string.Join(' ', args)} did not finish");
            return p.ExitCode;
        }

        public void Dispose()
        {
            if (Main is { HasExited: false })
            {
                RunToEnd("--exit");
                if (!Main.WaitForExit(10_000)) Main.Kill();
            }
            Main?.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(SettingsKey, throwOnMissingSubKey: false);
            S.Dispose();
        }
    }

    static AutomationElement WaitWindow(Process p, Func<AutomationElement, bool> match, string what, int ms = 20_000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        var cond = new PropertyCondition(AutomationElement.ProcessIdProperty, p.Id);
        while (DateTime.UtcNow < end)
        {
            foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, cond))
                if (match(w)) return w;
            Thread.Sleep(100);
        }
        throw new AssertFailedException($"No window: {what}");
    }

    static AutomationElement? Find(AutomationElement root, string name, ControlType? type = null)
    {
        Condition c = new PropertyCondition(AutomationElement.NameProperty, name);
        if (type != null) c = new AndCondition(c, new PropertyCondition(AutomationElement.ControlTypeProperty, type));
        return root.FindFirst(TreeScope.Descendants, c);
    }

    static AutomationElement Wait(AutomationElement root, string name, ControlType? type = null, int ms = 10_000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            if (Find(root, name, type) is { } e && !e.Current.IsOffscreen) return e;
            Thread.Sleep(100);
        }
        throw new AssertFailedException($"Not on screen: {name}");
    }

    static bool HasTextContaining(AutomationElement root, string part) =>
        root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
            .Cast<AutomationElement>().Any(e => e.Current.Name.Contains(part, StringComparison.Ordinal));

    static void WaitText(AutomationElement root, string part, int ms = 10_000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (!HasTextContaining(root, part))
        {
            if (DateTime.UtcNow > end) throw new AssertFailedException($"No text containing: {part}");
            Thread.Sleep(100);
        }
    }

    static void Press(AutomationElement root, string button) =>
        ((InvokePattern)Wait(root, button, ControlType.Button).GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    static TestApp? Make()
    {
        var exe = BuiltApp();
        if (exe == null) { Assert.Inconclusive("Run build.ps1 -Test, which builds the app first."); return null; }
        return new TestApp(exe);
    }

    static (string? Run, string? FirstRun) RealValues()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        using var app = Registry.CurrentUser.OpenSubKey(@"Software\ClaudeProfiles");
        return (run?.GetValue("ClaudeProfiles") as string, app?.GetValue("WindowBounds") as string);
    }

    [TestMethod]
    public void FirstStartOpensThePanelWithTheSetupNotice()
    {
        using var app = Make()!;
        var before = RealValues();
        var p = app.Start();
        var panel = WaitWindow(p, w => w.Current.Name == "Claude Profiles" && Find(w, "Add profile…", ControlType.Button) != null, "the panel");
        WaitText(panel, "Set up command line first");
        WaitText(panel, "plain claude uses");
        Assert.IsFalse(((AutomationElement)Wait(panel, "Filter by title, folder or profile", ControlType.Edit)).Current.IsOffscreen);

        // Doctor and back, through the real buttons.
        Press(panel, "Doctor");
        WaitText(panel, "Plain claude runs");
        Press(panel, "Done");
        Wait(panel, "Add profile…", ControlType.Button);

        // Add profile before setup: Continue is off.
        Press(panel, "Add profile…");
        var name = Wait(panel, "Display name", ControlType.Edit);
        ((ValuePattern)name.GetCurrentPattern(ValuePattern.Pattern)).SetValue("UI Test");
        WaitText(panel, "claude-as ui-test");
        Assert.IsFalse(Wait(panel, "Continue to sign-in", ControlType.Button).Current.IsEnabled);
        Press(panel, "Cancel");
        Wait(panel, "Add profile…", ControlType.Button);

        Assert.AreEqual(before, RealValues(), "the installed copy's values are untouched");
        Assert.AreEqual(1, Registry.CurrentUser.OpenSubKey(app.SettingsKey)?.GetValue("FirstRunDone"), "first run recorded under the test key");
    }

    [TestMethod]
    public void OneCopyRunsAndASecondStartOpensItsWindow()
    {
        using var app = Make()!;
        var p = app.Start();
        WaitWindow(p, w => w.Current.Name == "Claude Profiles", "the panel");
        Assert.AreEqual(0, app.RunToEnd(), "a second start hands over and exits");
        Assert.AreEqual(0, app.RunToEnd("--window"));
        var window = WaitWindow(p, w => Find(w, "Sessions", ControlType.DataGrid) != null, "the main window");
        var list = Wait(window, "Profiles", ControlType.List);
        var items = list.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        Assert.AreEqual(2, items.Count, "All profiles, and the built-in profile");
        WaitText(window, "Running ones are rechecked every 2 seconds");

        Press(window, "Add profile…");
        Wait(window, "Display name", ControlType.Edit);
        Press(window, "Cancel");

        Assert.IsFalse(p.HasExited);
        Assert.AreEqual(1, Process.GetProcessesByName("ClaudeProfiles").Count(x => { using (x) return FromExe(x, app.Exe); }), "one copy from this exe");
        Assert.AreEqual(0, app.RunToEnd("--exit"));
        Assert.IsTrue(p.WaitForExit(10_000), "it exits when asked");
        Assert.IsNotNull(Registry.CurrentUser.OpenSubKey(app.SettingsKey)?.GetValue("WindowBounds"), "the window's place is kept");
    }

    static bool FromExe(Process x, string exe)
    {
        try { return string.Equals(x.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase); }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    [TestMethod]
    public void ATestCopyCannotRegisterOrUninstall()
    {
        using var app = Make()!;
        var before = RealValues();
        Assert.AreEqual(2, app.RunToEnd("--register"));
        Assert.AreEqual(2, app.RunToEnd("--uninstall-app"));
        Assert.AreEqual(before, RealValues());
    }

    [TestMethod]
    public void IdleUsesAlmostNoCpuAndLogsNoErrors()
    {
        using var app = Make()!;
        Directory.CreateDirectory(app.S.Locator.BinDir);
        foreach (var f in new[] { "cpm.exe", "claude.exe", "claude-as.exe" }) File.WriteAllText(Path.Combine(app.S.Locator.BinDir, f), "");
        var p = app.Start("--background");
        Thread.Sleep(8000);
        p.Refresh();
        var a = p.TotalProcessorTime;
        Thread.Sleep(10_000);
        p.Refresh();
        var used = (p.TotalProcessorTime - a).TotalSeconds;
        Assert.IsTrue(used < 0.3, $"{used:F2} CPU seconds in 10 seconds while idle");
        var mb = p.WorkingSet64 / (1024 * 1024);
        Console.WriteLine($"Idle: {used:F3} CPU s per 10 s, {mb} MB working set");
        var log = Path.Combine(app.S.Locator.LogsDir, "writes.log");
        if (File.Exists(log)) Assert.IsFalse(File.ReadAllLines(log).Any(l => l.Contains("\terror\t", StringComparison.Ordinal)), "no error lines");
        Assert.AreEqual(0, WaitWindowsCount(p), "background start with the command line set up shows nothing");
    }

    static int WaitWindowsCount(Process p) =>
        AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, p.Id))
            .Cast<AutomationElement>().Count(w => w.Current.Name == "Claude Profiles" && !w.Current.IsOffscreen);
}
