using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ProfileUI;

namespace ClaudeProfiles;

/// The tray panel: borderless, 440 px wide, above the tray like the Wi-Fi flyout (D7). It has no
/// taskbar button, and closes on Esc or when it loses focus.
public sealed partial class PanelWindow : Window
{
    const double PanelWidth = 440, PanelHeight = 620, Gap = 12;

    readonly AppModel model;
    DateTime hiddenAt;

    public Action OpenWindow { get; init; } = () => { };
    public Action ExitApp { get; init; } = () => { };
    /// For automated test copies, which cannot take focus from the test runner.
    public bool KeepOpenWhenInactive { get; init; }

    public PanelWindow(AppModel model)
    {
        this.model = model;
        DataContext = model;
        Title = "Claude Profiles";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = PanelWidth;
        Height = PanelHeight;
        var view = new PanelView { OpenWindowRequested = () => OpenWindow(), ExitRequested = () => ExitApp() };
        var border = new Border { Child = view, BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeColorDefaultBrush");
        border.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        Content = border;
        Deactivated += (_, _) => { if (UiState.ModalOpen == 0 && !KeepOpenWhenInactive) Hide(); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) model.ViewAppeared();
            else
            {
                hiddenAt = DateTime.UtcNow;
                model.ViewDisappeared();
            }
        };
        PreviewKeyDown += Key;
    }

    /// True just after the panel hid itself because the tray icon was clicked, so that click does
    /// not open it again.
    public bool JustHidden => DateTime.UtcNow - hiddenAt < TimeSpan.FromMilliseconds(400);

    void Key(object sender, KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        e.Handled = true;
        if (model.IsFlow)
        {
            model.ErrorText = null;
            model.Route = Route.Home;
        }
        else Hide();
    }

    public void ShowAboveTray()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        Place(hwnd);
        Show();
        Place(hwnd);
        Activate();
        SetForegroundWindow(hwnd);
    }

    /// In the corner of the work area next to the tray, on the screen that holds the taskbar.
    void Place(IntPtr hwnd)
    {
        var bar = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        var found = SHAppBarMessage(ABM_GETTASKBARPOS, ref bar) != 0;
        var edge = found ? bar.uEdge : ABE_BOTTOM;
        var monitor = found
            ? MonitorFromRect(ref bar.rc, MONITOR_DEFAULTTOPRIMARY)
            : MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        var dpi = GetDpiForMonitor(monitor, 0, out var dx, out _) == 0 ? dx / 96.0 : 1.0;
        var work = info.rcWork;
        var w = (int)(PanelWidth * dpi);
        var h = (int)Math.Min(PanelHeight * dpi, work.Bottom - work.Top - 2 * Gap * dpi);
        var gap = (int)(Gap * dpi);
        var x = edge == ABE_LEFT ? work.Left + gap : work.Right - w - gap;
        var y = edge == ABE_TOP ? work.Top + gap : work.Bottom - h - gap;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    const uint ABM_GETTASKBARPOS = 5;
    const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_BOTTOM = 3;
    const uint MONITOR_DEFAULTTOPRIMARY = 1;
    const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [LibraryImport("shell32.dll")]
    private static partial UIntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromRect(ref RECT rect, uint flags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);
}
