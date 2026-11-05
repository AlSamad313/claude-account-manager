using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClaudeProfiles;

/// The icon in the notification area, in the default profile's colour. WPF has no tray
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

    /// Redraws the icon only when the colour changed. The tooltip is limited to 127 characters.
    public void Update(string color, string defaultLine)
    {
        var text = $"Claude Profiles — {defaultLine}";
        icon.Text = text.Length > 127 ? text[..126] + "…" : text;
        if (color != shownColor)
        {
            shownColor = color;
            var old = iconHandle;
            using (var bmp = Burst(color))
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

    /// The burst on the default profile's colour (ProfileUI.TrayImage), at the tray's icon size for this DPI.
    static Bitmap Burst(string hex)
    {
        var size = SystemInformation.SmallIconSize.Width;
        var src = ProfileUI.TrayImage.Render(hex, size, ProfileUI.TrayImage.BundledMask);
        var px = new byte[size * size * 4];
        src.CopyPixels(px, size * 4, 0);
        // WPF's pixels are premultiplied; GDI+ wants them straight.
        for (var i = 0; i < px.Length; i += 4)
        {
            var a = px[i + 3];
            if (a is 0 or 255) continue;
            for (var k = 0; k < 3; k++) px[i + k] = (byte)Math.Min(255, px[i + k] * 255 / a);
        }
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, size, size), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        try
        {
            for (var y = 0; y < size; y++) Marshal.Copy(px, y * size * 4, data.Scan0 + y * data.Stride, size * 4);
        }
        finally { bmp.UnlockBits(data); }
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
