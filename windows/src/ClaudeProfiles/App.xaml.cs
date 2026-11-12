using System.ComponentModel;
using System.Windows;
using ProfileCore;
using ProfileUI;

namespace ClaudeProfiles;

/// The tray app. One copy runs per user session; starting it again opens the running copy's panel.
public partial class App : Application
{
    public const string AppId = "ClaudeProfiles.App";

    /// For automated tests only: CLAUDE_PROFILES_TEST_INSTANCE=<name> gives this copy its own instance
    /// names and settings key, so it runs beside an installed copy without touching it. A test copy
    /// cannot register or uninstall, shows no notifications, never changes Start with Windows, and
    /// keeps its panel open when it loses focus, so UI Automation can drive it.
    public static readonly string? TestInstance =
        Environment.GetEnvironmentVariable("CLAUDE_PROFILES_TEST_INSTANCE") is { Length: > 0 and <= 40 } t
        && t.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? t : null;
    public static string Suffix => TestInstance is { } t ? "." + t : "";

    static readonly string InstanceName = @"Local\ClaudeProfiles.Instance" + Suffix;
    static readonly string ShowEventName = @"Local\ClaudeProfiles.Show" + Suffix;
    static readonly string WindowEventName = @"Local\ClaudeProfiles.Window" + Suffix;
    static readonly string ExitEventName = @"Local\ClaudeProfiles.Exit" + Suffix;

    Mutex? instance;
    EventWaitHandle? showEvent;
    EventWaitHandle? exitEvent;
    EventWaitHandle? windowEvent;
    AppModel? model;
    TrayIcon? tray;
    PanelWindow? panel;
    MainWindow? window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, x) =>
        {
            // Logged and shown, and the app keeps running: a tray tool that vanishes looks broken.
            try { new EventLog(ConfigLocator.Current()).Write("error", null, x.Exception.ToString().ReplaceLineEndings(" | ")); } catch (IOException) { }
            MessageBox.Show($"Something went wrong: {x.Exception.Message}\n\nDetails are in .claude-profiles\\logs\\writes.log.", "Claude Profiles",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            x.Handled = true;
        };
        var args = e.Args;
        if (TestInstance != null && (args.Contains("--register") || args.Contains("--uninstall-app"))) { Finish(2); return; }
        if (args.Contains("--register")) { Finish(AppInstall.Register()); return; }
        if (args.Contains("--uninstall-app")) { Finish(AppInstall.Uninstall(SignalExit)); return; }
        // build.ps1 -Install uses this before it replaces the files of a running copy.
        if (args.Contains("--exit")) { Finish(SignalExit() ? 0 : 1); return; }

        instance = new Mutex(true, InstanceName, out var first);
        if (!first)
        {
            instance.Dispose();
            instance = null;
            // This process may bring a window to the front (it was just started by the user); the running
            // copy may not, and its panel would hide again at once. So pass the right on first.
            _ = AllowSetForegroundWindow(-1);
            // --window opens the running copy's window; otherwise its panel opens.
            if (EventWaitHandle.TryOpenExisting(args.Contains("--window") ? WindowEventName : ShowEventName, out var show)) using (show) show.Set();
            Shutdown(0);
            return;
        }
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        windowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WindowEventName);
        Listen(showEvent, () => ShowPanel());
        Listen(windowEvent, ShowWindow);
        Listen(exitEvent, ExitApp);

        if (TestInstance == null && AppInstall.HasShortcut()) AppInstall.UseAppId();
        model = new AppModel(AppEnvironment.Live(Path.Combine(AppContext.BaseDirectory, "tools")));
        model.SignInDetected += (p, a) => tray?.Notify(
            "Sign-in finished", $"{p.DisplayName} signed in as {a.Email ?? "an account"}. Open to keep or retry.",
            () => { model.ErrorText = null; model.Route = new Route.Waiting(p.Id); ShowPanel(); });
        model.PropertyChanged += ModelChanged;

        panel = new PanelWindow(model) { OpenWindow = ShowWindow, ExitApp = ExitApp, KeepOpenWhenInactive = TestInstance != null };
        tray = new TrayIcon
        {
            Clicked = TogglePanel, OpenWindow = ShowWindow, ExitApp = ExitApp, Quiet = TestInstance != null,
            StartWithWindows = StartupSetting.IsOn,
            SetStartWithWindows = on => { if (TestInstance == null) StartupSetting.Set(on); return StartupSetting.IsOn(); },
        };
        tray.Update(model.DefaultColor, model.DefaultLine);
        model.StartWatching(a => Dispatcher.BeginInvoke(a));
        _ = Start(background: args.Contains("--background"), openWindow: args.Contains("--window"));
    }

    async Task Start(bool background, bool openWindow)
    {
        await model!.PrepareAtLaunch();
        model.StartBackground();
        if (openWindow) ShowWindow();
        if (AppSettings.FirstRunDone) { if (!background && !model.ShellInstalled) ShowPanel(); return; }
        AppSettings.FirstRunDone = true;
        if (!model.ShellInstalled) ShowPanel();
        tray!.Notify("Claude Profiles is running",
            "Its icon may be in the overflow (^) area by the clock. Drag it next to the clock to keep it in view.", () => ShowPanel());
    }

    void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppModel.DefaultColor) or nameof(AppModel.DefaultLine)) tray?.Update(model!.DefaultColor, model.DefaultLine);
    }

    void Listen(EventWaitHandle handle, Action action)
    {
        var t = new Thread(() =>
        {
            try
            {
                while (handle.WaitOne()) Dispatcher.BeginInvoke(action);
            }
            catch (ObjectDisposedException) { }
        }) { IsBackground = true, Name = "instance signal" };
        t.Start();
    }

    /// Asks a running copy to exit and waits up to 10 seconds for it. True when none is left.
    static bool SignalExit()
    {
        if (!EventWaitHandle.TryOpenExisting(ExitEventName, out var exit)) return true;
        using (exit) exit.Set();
        for (var i = 0; i < 50; i++)
        {
            if (!Mutex.TryOpenExisting(InstanceName, out var m)) return true;
            m.Dispose();
            Thread.Sleep(200);
        }
        return false;
    }

    void Finish(int code) => Shutdown(code);

    // ---- Panel and window

    void TogglePanel()
    {
        if (panel == null) return;
        if (panel.IsVisible) panel.Hide();
        else if (!panel.JustHidden) ShowPanel();
    }

    void ShowPanel() => panel?.ShowAboveTray();

    void ShowWindow()
    {
        if (model == null) return;
        panel?.Hide();
        if (window == null)
        {
            window = new MainWindow(model);
            WindowPlacement.Restore(window);
            window.Closing += (_, _) => WindowPlacement.Save(window);
            window.Closed += (_, _) => window = null;
        }
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    void ExitApp()
    {
        window?.Close();
        panel?.Close();
        tray?.Dispose();
        tray = null;
        model?.Dispose();
        Shutdown(0);
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        showEvent?.Dispose();
        exitEvent?.Dispose();
        windowEvent?.Dispose();
        if (instance != null)
        {
            instance.ReleaseMutex();
            instance.Dispose();
        }
        base.OnExit(e);
    }
}
