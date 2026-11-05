using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProfileUI.Tests;

/// Runs WPF views for tests: one UI thread with the app's Fluent theme, windows placed off screen,
/// and clicks made through UI Automation, the way a screen reader or a person presses a control.
static class Ui
{
    static readonly Lazy<Dispatcher> Thread = new(() =>
    {
        Dispatcher? d = null;
        using var ready = new ManualResetEventSlim();
        var t = new System.Threading.Thread(() =>
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.Light };
            d = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true, Name = "UI tests" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        ready.Wait();
        return d!;
    });

    /// Runs a test body on the UI thread and waits for it, passing on any failure.
    public static void Run(Func<Task> body) => Thread.Value.InvokeAsync(body).Task.Unwrap().GetAwaiter().GetResult();

    public static Window Host(FrameworkElement content, object model, double width = 420, double height = 900)
    {
        var w = new Window
        {
            Content = content, DataContext = model, Width = width, Height = height, Left = -30000, Top = 0,
            ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None,
        };
        w.Show();
        w.UpdateLayout();
        return w;
    }

    public static T Show<T>(T w) where T : Window
    {
        w.Left = -30000;
        w.Top = 0;
        w.ShowInTaskbar = false;
        w.ShowActivated = false;
        w.Show();
        w.UpdateLayout();
        return w;
    }

    /// Waits for a condition while the UI thread keeps running, so model work and bindings complete.
    public static async Task Until(Func<bool> condition, string what, int ms = 5000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        string? last = null;
        while (true)
        {
            try { if (condition()) return; }
            catch (AssertFailedException e) { last = e.Message; } // not on screen yet
            if (DateTime.UtcNow > end) Assert.Fail($"Timed out waiting for: {what}" + (last != null ? $" ({last})" : ""));
            await Task.Delay(20);
        }
    }

    // ---- Finding

    public static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>([root]);
        while (stack.Count > 0)
        {
            var e = stack.Pop();
            if (!seen.Add(e)) continue;
            yield return e;
            if (e is Visual or System.Windows.Media.Media3D.Visual3D)
                for (var i = VisualTreeHelper.GetChildrenCount(e) - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(e, i));
            // Logical children too: menu items that are not open, and links inside text.
            foreach (var c in LogicalTreeHelper.GetChildren(e).OfType<DependencyObject>()) stack.Push(c);
            if (e is TextBlock tb) foreach (var inline in Inlines(tb.Inlines)) stack.Push(inline);
        }
    }

    static IEnumerable<Inline> Inlines(InlineCollection list)
    {
        foreach (var i in list)
        {
            yield return i;
            if (i is Span s) foreach (var x in Inlines(s.Inlines)) yield return x;
        }
    }

    /// A link or other text element counts as shown when the text it sits in is.
    static bool Shown(DependencyObject e)
    {
        while (e is ContentElement c && LogicalTreeHelper.GetParent(c) is { } parent) e = parent;
        return e is not UIElement u || u.IsVisible;
    }

    public static T? Maybe<T>(DependencyObject root, Func<T, bool>? match = null, bool visibleOnly = true) where T : DependencyObject =>
        All(root).OfType<T>().FirstOrDefault(e => (!visibleOnly || Shown(e)) && (match == null || match(e)));

    public static T One<T>(DependencyObject root, Func<T, bool>? match = null, string? what = null) where T : DependencyObject =>
        Maybe(root, match) ?? throw new AssertFailedException($"Not on screen: {what ?? typeof(T).Name}");

    public static Button Button(DependencyObject root, string text) =>
        One<Button>(root, b => b.Content as string == text, $"button \"{text}\"");

    public static Button? MaybeButton(DependencyObject root, string text) => Maybe<Button>(root, b => b.Content as string == text);

    public static bool HasText(DependencyObject root, string text) =>
        Maybe<TextBlock>(root, t => t.Text.Contains(text, StringComparison.Ordinal)) != null;

    public static TextBox Box(DependencyObject root, string name) =>
        One<TextBox>(root, t => System.Windows.Automation.AutomationProperties.GetName(t) == name, $"box \"{name}\"");

    // ---- Acting

    /// Presses a button the way UI Automation does, which runs its Click handler and its command, and
    /// refuses a disabled button.
    public static void Click(ButtonBase b)
    {
        Assert.IsTrue(b.IsEnabled, $"\"{b.Content}\" is disabled");
        var peer = UIElementAutomationPeer.CreatePeerForElement(b);
        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
        else if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) toggle.Toggle();
    }

    public static void Click(Hyperlink link) =>
        ((IInvokeProvider)ContentElementAutomationPeer.CreatePeerForElement(link).GetPattern(PatternInterface.Invoke)).Invoke();

    /// A menu item, whether or not its menu is open.
    public static void Click(MenuItem item)
    {
        Assert.IsTrue(item.IsEnabled && item.Visibility == Visibility.Visible, $"menu item \"{item.Header}\" is not available");
        ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(item).GetPattern(PatternInterface.Invoke)).Invoke();
    }

    public static void Key(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target) ?? throw new AssertFailedException("not on screen");
        var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(down);
        if (down.Handled) return;
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
    }
}
