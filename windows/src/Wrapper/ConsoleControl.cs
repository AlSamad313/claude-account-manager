using System.Runtime.InteropServices;

namespace Wrapper;

/// The wrapper's console calls.
internal static class ConsoleControl
{
    const int STD_OUTPUT_HANDLE = -11;
    const uint CTRL_C_EVENT = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int which);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetConsoleMode(IntPtr h, out uint mode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetConsoleTitleW(string title);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint group);

    /// True if standard output is a console, as zsh's [[ -t 1 ]]. Piped output gets no title.
    public static bool StdoutIsConsole() => GetConsoleMode(GetStdHandle(STD_OUTPUT_HANDLE), out _);

    public static void SetTitle(string title)
    {
        if (StdoutIsConsole()) SetConsoleTitleW(title);
    }

    /// While Claude Code runs, Ctrl+C and Ctrl+Break must not end the wrapper, or the shell would take
    /// the console back with Claude Code still running in it. Claude Code still gets the event,
    /// because console control events reach every process on the console. A handler is used, not
    /// SetConsoleCtrlHandler(NULL, TRUE), because that flag is inherited and would reach Claude Code.
    public static void IgnoreCtrlC()
    {
        Console.CancelKeyPress += (_, e) => e.Cancel = true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint GetConsoleProcessList(uint[] list, uint count);

    /// The processes attached to the console of pid, other than this helper. Stop uses it to find
    /// commands a session started: Git Bash starts them through a process that exits at once, so
    /// they have no living parent, but they stay on that bash's hidden console.
    public static (List<uint> Pids, string? Error) ConsoleMembers(uint pid)
    {
        FreeConsole();
        if (!AttachConsole(pid)) return ([], $"Could not reach the console of process {pid} (error {Marshal.GetLastWin32Error()}).");
        try
        {
            var buf = new uint[1024];
            var n = GetConsoleProcessList(buf, (uint)buf.Length);
            var me = (uint)Environment.ProcessId;
            return (buf.Take((int)Math.Min(n, (uint)buf.Length)).Where(p => p != me).ToList(), null);
        }
        finally { FreeConsole(); }
    }

    /// Design flow 9 step 3, run in a hidden helper process: leave this console, join the session's,
    /// and raise Ctrl+C there as if the user pressed it. The NULL handler keeps the helper alive; it
    /// is safe here because the helper starts nothing. Null on success, otherwise the reason.
    public static string? SendCtrlC(uint pid)
    {
        FreeConsole();
        if (!AttachConsole(pid)) return $"Could not reach the console of process {pid} (error {Marshal.GetLastWin32Error()}). It may not be running in a terminal window.";
        try
        {
            SetConsoleCtrlHandler(IntPtr.Zero, true);
            if (!GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)) return $"Could not send Ctrl+C (error {Marshal.GetLastWin32Error()}).";
            // The event is delivered on a new thread in each process; give it a moment before leaving.
            Thread.Sleep(100);
            return null;
        }
        finally { FreeConsole(); }
    }
}
