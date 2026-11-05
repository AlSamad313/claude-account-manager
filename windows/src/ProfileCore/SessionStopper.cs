using System.Globalization;

namespace ProfileCore;

public sealed record StopRequest(string Title, string? Folder, string? Status, int Pid)
{
    public bool IsBusy => Status is "busy" or "shell";

    public string? Warning => Status switch
    {
        "busy" => "Claude Code is working; stopping now can leave a file edit unfinished.",
        "shell" => "Claude Code is running a command; it may keep running after the session ends.",
        _ => null,
    };

    /// The console Ctrl+C reaches every program in that window (design flow 9).
    public const string WindowNote = "Ctrl+C reaches every program in that window, so another program you started there may stop too.";
}

public abstract record StopResult
{
    public sealed record AlreadyStopped : StopResult;
    public sealed record Cancelled : StopResult;
    public sealed record Stopped(int CtrlCs, IReadOnlyList<ProcEntry> LeftRunning) : StopResult;
    public sealed record DidNotStop(int Pid) : StopResult;
    public sealed record CouldNotSend(string Reason) : StopResult;

    /// For the event log.
    public string Describe() => this switch
    {
        Stopped s => $"stopped after {s.CtrlCs} Ctrl+C; left running: " +
                     (s.LeftRunning.Count == 0 ? "none" : string.Join(", ", s.LeftRunning.Select(c => $"{c.Name} {c.Pid}"))),
        DidNotStop d => $"did not stop, pid {d.Pid}",
        CouldNotSend c => $"could not send: {c.Reason}",
        Cancelled => "cancelled",
        _ => "already stopped",
    };
}

/// The two console jobs that need a helper process, because attaching to another console changes
/// the caller's console, and the app and cpm must keep their own.
public interface IConsoleHelper
{
    /// Sends one console Ctrl+C to the console pid is attached to. Null on success, otherwise why not.
    string? SendCtrlC(int pid);

    /// The processes on the console pid is attached to. Empty if that cannot be read.
    IReadOnlyList<int> ConsoleMembers(int pid);
}

/// Flow 9: bin\claude.exe --send-ctrl-c <pid> and --console-processes <pid>, run hidden.
public sealed class ConsoleHelper(ConfigLocator locator, IProcessRunner? runner = null) : IConsoleHelper
{
    readonly IProcessRunner runner = runner ?? new ProcessRunner();
    string Helper => Path.Combine(locator.BinDir, "claude.exe");

    public string? SendCtrlC(int pid)
    {
        if (!File.Exists(Helper)) return "The command line tools are not installed. Run: cpm install-shell";
        var r = runner.Run(Helper, ["--send-ctrl-c", pid.ToString(CultureInfo.InvariantCulture)], null, TimeSpan.FromSeconds(10));
        return r.ExitCode == 0 ? null : (r.Stderr.Trim().Length > 0 ? r.Stderr.Trim() : $"The helper ended with code {r.ExitCode}.");
    }

    public IReadOnlyList<int> ConsoleMembers(int pid)
    {
        if (!File.Exists(Helper)) return [];
        var r = runner.Run(Helper, ["--console-processes", pid.ToString(CultureInfo.InvariantCulture)], null, TimeSpan.FromSeconds(10));
        if (r.ExitCode != 0) return [];
        return r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).Where(n => n > 0).ToList();
    }
}

/// Implements design flow 9. Never forces a session to end, and never closes a window.
public sealed class SessionStopper(ConfigLocator locator, IProcessInfo? processes = null, IConsoleHelper? helper = null)
{
    readonly LiveSessionReader live = new(locator, processes);
    readonly IConsoleHelper helper = helper ?? new ConsoleHelper(locator);
    public TimeSpan Grace { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public StopResult Stop(SessionRow row, Func<StopRequest, bool> confirm)
    {
        var result = Perform(row, confirm);
        new EventLog(locator).Write("stop", row.ProfileId, $"{row.SessionId} {result.Describe()}");
        return result;
    }

    StopResult Perform(SessionRow row, Func<StopRequest, bool> confirm)
    {
        if (!row.Live || row.Pid is not { } pid || !live.IsSameProcess(pid, row.ProcStart)) return new StopResult.AlreadyStopped();
        if (!confirm(new StopRequest(row.Title, row.Cwd, row.Status, pid))) return new StopResult.Cancelled();
        // The user may have taken a while to answer.
        if (!live.IsSameProcess(pid, row.ProcStart)) return new StopResult.AlreadyStopped();

        // Step 7 needs the list from before the stop: afterwards the links are gone.
        var children = live.Processes.Descendants(pid, helper.ConsoleMembers);
        for (var n = 1; n <= 2; n++)
        {
            if (helper.SendCtrlC(pid) is { } why) return n == 1 ? new StopResult.CouldNotSend(why) : new StopResult.DidNotStop(pid);
            if (WaitForExit(pid, row.ProcStart)) return new StopResult.Stopped(n, LeftRunning(children));
        }
        return new StopResult.DidNotStop(pid);
    }

    List<ProcEntry> LeftRunning(IReadOnlyList<ProcEntry> children) =>
        children.Where(c => live.IsSameProcess(c.Pid, c.Start)).ToList();

    bool WaitForExit(int pid, DateTime? start)
    {
        var deadline = DateTime.UtcNow + Grace;
        while (DateTime.UtcNow < deadline)
        {
            if (!live.IsSameProcess(pid, start)) return true;
            Thread.Sleep(PollInterval);
        }
        return !live.IsSameProcess(pid, start);
    }
}
