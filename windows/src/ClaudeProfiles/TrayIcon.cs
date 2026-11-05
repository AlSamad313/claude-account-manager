using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClaudeProfiles;

/// The coloured dot in the notification area, in the default profile's colour. WPF has no tray
/// icon, so this is WinForms' NotifyIcon. Its balloon call shows as a normal Windows notification (D8).
public sealed partial class TrayIcon : IDisposable
{
    readonly NotifyIcon icon = new();
    readonly ToolStripMenuItem startItem;
    IntPtr iconHandle;
    string? shownColor;
    Action? onBalloonClick;

    public Action Clicked { get; init; } = () => { };
    public Action OpenWindow { get; init; } = () => { };
    public Action ExitApp { get; init; } = () => { };
    public Func<bool> StartWithWindows { get; init; } = () => false;
    public Func<bool, bool> SetStartWithWindows { get; init; } = _ => false;
    /// No notifications, for automated test copies.
    public bool Quiet { get; init; }

    public TrayIcon()
    {
        startItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = false };
        startItem.Click += (_, _) => startItem.Checked = SetStartWithWindows(!startItem.Checked);
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open Claude Profiles");
        open.Font = new Font(open.Font, System.Drawing.FontStyle.Bold);
        open.Click += (_, _) => OpenWindow();
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitApp();
        menu.Items.AddRange([open, startItem, new ToolStripSeparator(), exit]);
        menu.Opening += (_, _) => startItem.Checked = StartWithWindows();
        icon.ContextMenuStrip = menu;
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Clicked(); };
        icon.BalloonTipClicked += (_, _) => { var a = onBalloonClick; onBalloonClick = null; a?.Invoke(); };
        icon.BalloonTipClosed += (_, _) => onBalloonClick = null;
    }

    /// Redraws the dot only when the colour changed. The tooltip is limited to 127 characters.
    public void Update(string color, string defaultLine)
    {
        var text = $"Claude Profiles — {defaultLine}";
        icon.Text = text.Length > 127 ? text[..126] + "…" : text;
        if (color != shownColor)
        {
            shownColor = color;
            var old = iconHandle;
            using (var bmp = Dot(color))
            {
                iconHandle = bmp.GetHicon();
            }
            icon.Icon = Icon.FromHandle(iconHandle);
            if (old != IntPtr.Zero) DestroyIcon(old);
        }
        icon.Visible = true;
    }

    public void Notify(string title, string text, Action onClick)
    {
        if (Quiet) return;
        onBalloonClick = onClick;
        icon.ShowBalloonTip(10_000, title, text, ToolTipIcon.None);
    }

    static Bitmap Dot(string hex)
    {
        var size = SystemInformation.SmallIconSize.Width;
        var c = ColorTranslator.FromHtml(hex is { Length: 7 } && hex[0] == '#' ? hex : "#8A8F98");
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var pad = Math.Max(1f, size / 8f);
        var r = new RectangleF(pad, pad, size - 2 * pad - 1, size - 2 * pad - 1);
        using var fill = new SolidBrush(c);
        g.FillEllipse(fill, r);
        // A faint ring keeps dark colours visible on a dark taskbar, and light ones on a light one.
        using var ring = new Pen(Color.FromArgb(110, 255, 255, 255), Math.Max(1f, size / 16f));
        g.DrawEllipse(ring, r);
        return bmp;
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        if (iconHandle != IntPtr.Zero) DestroyIcon(iconHandle);
        iconHandle = IntPtr.Zero;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr handle);
}
