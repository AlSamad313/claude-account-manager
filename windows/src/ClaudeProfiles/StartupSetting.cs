using System.Globalization;
using System.Windows;
using Microsoft.Win32;

namespace ClaudeProfiles;

/// The app's own values, under HKCU\Software\ClaudeProfiles. Nothing here is about profiles.
static class AppSettings
{
    public static string KeyPath => @"Software\ClaudeProfiles" + App.Suffix;

    static object? Get(string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        return k?.GetValue(name);
    }

    static void Put(string name, object value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
        k.SetValue(name, value);
    }

    public static bool FirstRunDone { get => Get(nameof(FirstRunDone)) is int i && i != 0; set => Put(nameof(FirstRunDone), value ? 1 : 0); }
    /// Set once the user turned Start with Windows on or off, so registering again keeps their choice.
    public static bool StartupChosen { get => Get(nameof(StartupChosen)) is int i && i != 0; set => Put(nameof(StartupChosen), value ? 1 : 0); }
    public static string? WindowBounds { get => Get(nameof(WindowBounds)) as string; set => Put(nameof(WindowBounds), value ?? ""); }

    public static void DeleteAll() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
}

/// Design D4: one value under the Run key. It shows in Task Manager's Startup apps.
static class StartupSetting
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "ClaudeProfiles";

    public static string Command => $"\"{Environment.ProcessPath}\" --background";

    public static bool IsOn()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) is string;
    }

    /// The user's choice from the tray menu.
    public static void Set(bool on)
    {
        Write(on);
        AppSettings.StartupChosen = true;
    }

    public static void Write(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(ValueName, Command);
        else k.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// The window remembers its size and position, as Windows windows do.
static class WindowPlacement
{
    public static void Save(Window w)
    {
        var r = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.Width, w.Height) : w.RestoreBounds;
        if (r.IsEmpty) return;
        var max = w.WindowState == WindowState.Maximized ? 1 : 0;
        AppSettings.WindowBounds = string.Join(",", new[] { r.Left, r.Top, r.Width, r.Height }.Select(v => ((int)v).ToString(CultureInfo.InvariantCulture))) + "," + max;
    }

    /// Only if the saved place is still on a screen; otherwise Windows chooses.
    public static void Restore(Window w)
    {
        var parts = AppSettings.WindowBounds?.Split(',');
        if (parts is not { Length: 5 }) return;
        var n = new double[4];
        for (var i = 0; i < 4; i++) if (!double.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i])) return;
        var saved = new Rect(n[0], n[1], Math.Max(n[2], w.MinWidth), Math.Max(n[3], w.MinHeight));
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(saved, screen);
        if (visible.IsEmpty || visible.Width < 100 || visible.Height < 60) return;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = saved.Left;
        w.Top = saved.Top;
        w.Width = saved.Width;
        w.Height = saved.Height;
        if (parts[4] == "1") w.WindowState = WindowState.Maximized;
    }
}
