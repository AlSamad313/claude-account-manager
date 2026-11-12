using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace ClaudeProfiles;

/// Design D9. `--register` is run by `build.ps1 -Install` after it copies the app into place.
/// `--uninstall-app` is what Settings > Apps > Installed apps runs.
static partial class AppInstall
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClaudeProfiles";
    const string Name = "Claude Profiles";

    /// Where `build.ps1 -Install` puts the app. Only this folder is ever deleted by --uninstall-app.
    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ClaudeProfiles");

    static string Shortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk");

    static string Exe => Environment.ProcessPath ?? throw new InvalidOperationException("Unknown program path.");

    public static bool HasShortcut() => File.Exists(Shortcut);

    /// Notifications show the app's name only when the process carries the shortcut's ID.
    public static void UseAppId() => _ = SetCurrentProcessExplicitAppUserModelID(App.AppId);

    public static int Register()
    {
        try
        {
            WriteShortcut(Shortcut, Exe, App.AppId);
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                var dir = Path.GetDirectoryName(Exe)!;
                k.SetValue("DisplayName", Name);
                k.SetValue("DisplayVersion", typeof(AppInstall).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
                k.SetValue("Publisher", "Claude Profiles contributors");
                k.SetValue("DisplayIcon", Exe);
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", $"\"{Exe}\" --uninstall-app");
                k.SetValue("NoModify", 1);
                k.SetValue("NoRepair", 1);
                k.SetValue("EstimatedSize", (int)(DirSize(dir) / 1024));
            }
            // D4: on by default, unless the user already chose.
            if (!AppSettings.StartupChosen) StartupSetting.Write(true);
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException)
        {
            Console.Error.WriteLine($"Could not register Claude Profiles: {e.Message}");
            return 1;
        }
    }

    /// Exits any running copy, removes the shortcut, the startup value, the entry and the app
    /// folder. Leaves .claude-profiles alone.
    public static int Uninstall(Func<bool> exitRunningCopy)
    {
        if (!exitRunningCopy())
        {
            MessageBox.Show("Claude Profiles is still running. Exit it from its tray menu, then try again.", Name,
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return 1;
        }
        TryDelete(() => File.Delete(Shortcut));
        TryDelete(() => StartupSetting.Write(false));
        TryDelete(() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false));
        TryDelete(AppSettings.DeleteAll);
        var dir = Path.GetDirectoryName(Exe)!;
        if (string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(InstallDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            DeleteAfterExit(dir);
        MessageBox.Show(
            "Claude Profiles was uninstalled.\n\nYour profiles, and the command line tools in .claude-profiles\\bin, are kept. " +
            "To remove the profiles and the tools too, run: cpm uninstall. To take only the tools off your PATH, run: cpm uninstall-shell",
            Name, MessageBoxButton.OK, MessageBoxImage.Information);
        return 0;
    }

    static void TryDelete(Action a)
    {
        try { a(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// The running exe is inside the folder, so a hidden command deletes it two seconds later.
    static void DeleteAfterExit(string dir)
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var psi = new ProcessStartInfo(cmd)
        {
            Arguments = $"/d /c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{dir}\"",
            CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath(),
        };
        Process.Start(psi)?.Dispose();
    }

    static long DirSize(string dir)
    {
        try { return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch (IOException) { return 0; }
    }

    // ---- Shortcut with an AppUserModelID

    static void WriteShortcut(string path, string target, string appId)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(target);
            link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            link.SetDescription("Run Claude Code on several accounts");
            var store = (IPropertyStore)link;
            var key = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
            var value = new PropVariant { vt = 31, pointer = Marshal.StringToCoTaskMemUni(appId) };
            try
            {
                store.SetValue(ref key, ref value);
                store.Commit();
            }
            finally { Marshal.FreeCoTaskMem(value.pointer); }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ((IPersistFile)link).Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink;

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int max, IntPtr data, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short key);
        void SetHotkey(short key);
        void GetShowCmd(out int cmd);
        void SetShowCmd(int cmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder path, int max, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistFile
    {
        void GetClassID(out Guid id);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PropertyKey(Guid fmtid, uint pid)
    {
        public Guid fmtid = fmtid;
        public uint pid = pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointer;
        [FieldOffset(16)] public IntPtr pad;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);
}
