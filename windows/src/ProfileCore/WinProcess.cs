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

    // ---- Process tree

    const uint TH32CS_SNAPPROCESS = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);

    /// Every running process: PID, parent PID and exe name.
    public static List<(int Pid, int Parent, string Name)> Snapshot()
    {
        var list = new List<(int, int, string)>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == new IntPtr(-1)) return list;
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snap, ref e)) return list;
            do list.Add(((int)e.th32ProcessID, (int)e.th32ParentProcessID, e.szExeFile));
            while (Process32NextW(snap, ref e));
        }
        finally { CloseHandle(snap); }
        return list;
    }

    // ---- Starting a program in a new console window, with a fresh user environment

    const uint TOKEN_QUERY = 0x0008, TOKEN_DUPLICATE = 0x0002;
    const uint CREATE_NEW_CONSOLE = 0x00000010, CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFOW
    {
        public uint cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool DestroyEnvironmentBlock(IntPtr env);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcessW(string? app, char[] commandLine, IntPtr pa, IntPtr ta, bool inherit, uint flags,
                                      IntPtr env, string? cwd, ref STARTUPINFOW si, out PROCESS_INFORMATION pi);

    /// Design flow 2: a new console window, which Windows gives to the default terminal, with the
    /// environment a program started from the Start menu gets. Nothing is inherited from the caller,
    /// so a calling Claude Code session's markers never reach the new window. Returns the PID.
    public static int StartInNewConsole(string commandLine, string workingDir)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE, out var token))
            throw new ProfileException($"Could not read this user's token (error {Marshal.GetLastWin32Error()}).");
        var env = IntPtr.Zero;
        try
        {
            if (!CreateEnvironmentBlock(out env, token, false))
                throw new ProfileException($"Could not build a fresh environment (error {Marshal.GetLastWin32Error()}).");
            var si = new STARTUPINFOW { cb = (uint)Marshal.SizeOf<STARTUPINFOW>() };
            var cmd = (commandLine + "\0").ToCharArray();
            if (!CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, false, CREATE_NEW_CONSOLE | CREATE_UNICODE_ENVIRONMENT,
                                env, workingDir, ref si, out var pi))
                throw new ProfileException($"Could not open a terminal window (error {Marshal.GetLastWin32Error()}).");
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            return (int)pi.dwProcessId;
        }
        finally
        {
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
            CloseHandle(token);
        }
    }

    /// The environment a program started from the Start menu gets, as variables. For programs
    /// started with ProcessStartInfo, such as VS Code (flow 12), so no caller's markers leak in.
    public static Dictionary<string, string> FreshEnvironment()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE, out var token))
            throw new ProfileException($"Could not read this user's token (error {Marshal.GetLastWin32Error()}).");
        var env = IntPtr.Zero;
        try
        {
            if (!CreateEnvironmentBlock(out env, token, false))
                throw new ProfileException($"Could not build a fresh environment (error {Marshal.GetLastWin32Error()}).");
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var p = env;
            while (true)
            {
                var s = Marshal.PtrToStringUni(p);
                if (string.IsNullOrEmpty(s)) break;
                var eq = s.IndexOf('=', 1);
                if (eq > 0) result[s[..eq]] = s[(eq + 1)..];
                p += (s.Length + 1) * 2;
            }
            return result;
        }
        finally
        {
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
            CloseHandle(token);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, char[] name, ref uint size);

    /// The full path of a process's exe, or null if it cannot be read.
    public static string? ImagePath(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var buf = new char[32768];
            var size = (uint)buf.Length;
            return QueryFullProcessImageNameW(h, 0, buf, ref size) ? new string(buf, 0, (int)size) : null;
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

    /// For `cpm uninstall`: a hidden cmd waits a few seconds, for cpm to exit, then deletes the bin
    /// folder and the lock file, and removes the profiles folder only if that leaves it empty.
    /// Returns false, and does nothing, for a path cmd could misread.
    public static bool DeleteFolderAfterExit(string profilesRoot)
    {
        var root = Path.GetFullPath(profilesRoot).TrimEnd('\\');
        if (!string.Equals(Path.GetFileName(root), ".claude-profiles", StringComparison.OrdinalIgnoreCase)) return false;
        if (root.IndexOfAny(['"', '%', '^', '!']) >= 0) return false;
        var bin = Path.Combine(root, "bin");
        var lockFile = Path.Combine(root, ".lock");
        var script = $"ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{bin}\" & del /q /a \"{lockFile}\" & rmdir \"{root}\"";
        var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        psi.Arguments = "/d /s /c \"" + script + "\"";
        using var _ = System.Diagnostics.Process.Start(psi);
        return true;
    }
}
