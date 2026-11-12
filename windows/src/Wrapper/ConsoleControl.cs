using System.Runtime.InteropServices;

namespace Wrapper;

/// The wrapper's console calls.
internal static class ConsoleControl
{
    const int STD_OUTPUT_HANDLE = -11;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int which);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetConsoleMode(IntPtr h, out uint mode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetConsoleTitleW(string title);

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
}
