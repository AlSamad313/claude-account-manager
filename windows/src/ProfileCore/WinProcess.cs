using System.Runtime.InteropServices;

namespace ProfileCore;

/// Every Windows API call in ProfileCore, apart from path canonicalisation in Shared. The rest of
/// ProfileCore reaches these through interfaces, so it can be tested.
public static class WinProcess
{
    // ---- Processes

    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint STILL_ACTIVE = 259;
    const int ERROR_ACCESS_DENIED = 5;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetExitCodeProcess(IntPtr h, out uint code);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);

    public enum ProcessCheck { NotRunning, Running, AccessDenied }

    /// Whether a PID is running, and its creation time as UTC when it can be read.
    public static (ProcessCheck State, DateTime? Start) Query(int pid)
    {
        if (pid <= 0) return (ProcessCheck.NotRunning, null);
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero)
            return Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED ? (ProcessCheck.AccessDenied, null) : (ProcessCheck.NotRunning, null);
        try
        {
            if (GetExitCodeProcess(h, out var code) && code != STILL_ACTIVE) return (ProcessCheck.NotRunning, null);
            return GetProcessTimes(h, out var c, out _, out _, out _)
                ? (ProcessCheck.Running, DateTime.FromFileTimeUtc(c))
                : (ProcessCheck.Running, null);
        }
        finally { CloseHandle(h); }
    }

    // ---- Replacing a file that readers have open

    const uint DELETE = 0x00010000, SYNCHRONIZE = 0x00100000;
    const uint SHARE_ALL = 7, OPEN_EXISTING = 3;
    const int FileRenameInfoEx = 22;
    const uint FILE_RENAME_FLAG_REPLACE_IF_EXISTS = 0x1, FILE_RENAME_FLAG_POSIX_SEMANTICS = 0x2;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetFileInformationByHandle(IntPtr h, int infoClass, IntPtr info, uint size);

    /// Renames source over target with POSIX semantics, so a reader that opened the target with
    /// delete sharing keeps reading the old file and does not block the rename. Plain MoveFileEx fails
    /// in that case. Returns false if the volume does not support it, or a reader holds the target
    /// without delete sharing; the caller then retries.
    public static bool ReplacePosix(string source, string target)
    {
        var h = CreateFileW(source, DELETE | SYNCHRONIZE, SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == new IntPtr(-1)) return false;
        var full = Path.GetFullPath(target);
        var nameBytes = full.Length * 2;
        // FILE_RENAME_INFO: Flags (4) + padding (4) + RootDirectory (8) + FileNameLength (4) + FileName.
        var size = 20 + nameBytes + 2;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            for (var i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
            Marshal.WriteInt32(buf, 0, (int)(FILE_RENAME_FLAG_REPLACE_IF_EXISTS | FILE_RENAME_FLAG_POSIX_SEMANTICS));
            Marshal.WriteIntPtr(buf, 8, IntPtr.Zero);
            Marshal.WriteInt32(buf, 16, nameBytes);
            Marshal.Copy(full.ToCharArray(), 0, buf + 20, full.Length);
            return SetFileInformationByHandle(h, FileRenameInfoEx, buf, (uint)size);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
            CloseHandle(h);
        }
    }

    // ---- Credential Manager: names only. The secret is never read.

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CREDENTIALW
    {
        public uint Flags, Type;
        public IntPtr TargetName, Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes, TargetAlias, UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredEnumerateW(string? filter, uint flags, out uint count, out IntPtr creds);

    [DllImport("advapi32.dll")]
    static extern void CredFree(IntPtr buffer);

    public static HashSet<string> CredentialNames(string prefix)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!CredEnumerateW(prefix + "*", 0, out var count, out var list)) return names;
        try
        {
            for (var i = 0; i < count; i++)
            {
                var p = Marshal.ReadIntPtr(list, i * IntPtr.Size);
                var c = Marshal.PtrToStructure<CREDENTIALW>(p);
                var name = Marshal.PtrToStringUni(c.TargetName);
                if (name != null) names.Add(name);
            }
        }
        finally { CredFree(list); }
        return names;
    }

    // ---- Environment change broadcast

    const int HWND_BROADCAST = 0xffff;
    const uint WM_SETTINGCHANGE = 0x001A;
    const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

    /// Tells running programs that the environment changed, so new windows from Explorer get the new PATH.
    public static void BroadcastEnvironmentChange() =>
        SendMessageTimeoutW(new IntPtr(HWND_BROADCAST), WM_SETTINGCHANGE, UIntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 5000, out _);

    // ---- Recycle Bin

    [StructLayout(LayoutKind.Sequential)]
    struct SHQUERYRBINFO { public uint cbSize; public long i64Size, i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBinW(string root, ref SHQUERYRBINFO info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperationW(ref SHFILEOPSTRUCTW op);

    const uint FO_DELETE = 3;
    const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_NOCONFIRMMKDIR = 0x200;

    public static bool HasRecycleBin(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        if (new DriveInfo(root).DriveType != DriveType.Fixed) return false;
        var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBinW(root, ref info) == 0;
    }

    /// Moves a file or folder to the Recycle Bin, with no dialogs. Returns the shell's error code; 0 is success.
    public static int MoveToRecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCTW
        {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(path) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOF_NOCONFIRMMKDIR,
        };
        var r = SHFileOperationW(ref op);
        return r != 0 ? r : op.fAnyOperationsAborted != 0 ? -1 : 0;
    }
}
