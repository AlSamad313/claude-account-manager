using System.Diagnostics;
using System.Text;

namespace ProfileCore;

public sealed record ProcessResult(string Stdout = "", string Stderr = "", int ExitCode = 0, bool TimedOut = false);

public interface IProcessRunner
{
    ProcessResult Run(string exe, IReadOnlyList<string> args, LaunchEnv? env, TimeSpan timeout);
}

/// The one way ProfileCore runs external commands. Arguments are quoted by .NET with the
/// CommandLineToArgvW rules (S0 check C16).
public sealed class ProcessRunner : IProcessRunner
{
    public ProcessResult Run(string exe, IReadOnlyList<string> args, LaunchEnv? env, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var k in env?.Unset ?? []) psi.Environment.Remove(k);
        foreach (var (k, v) in env?.Set ?? []) psi.Environment[k] = v;

        Process p;
        try { p = Process.Start(psi)!; }
        catch (Exception e) { return new ProcessResult(Stderr: $"Could not run {exe}: {e.Message}", ExitCode: 127); }
        using (p)
        {
            p.StandardInput.Close();
            // Own threads, not the shared pool: when the pool is busy, a queued read can finish too late and lose the output.
            var stdout = new PipeReader(p.StandardOutput);
            var stderr = new PipeReader(p.StandardError);
            var timedOut = !p.WaitForExit(timeout);
            if (timedOut)
            {
                try { p.Kill(entireProcessTree: true); } catch (Exception) { }
                p.WaitForExit(2000);
            }
            // A child that keeps the pipes open must not hang the caller.
            var grace = Stopwatch.StartNew();
            var outText = stdout.Result(TimeSpan.FromSeconds(1));
            var errText = stderr.Result(TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(Math.Min(grace.Elapsed.Ticks, TimeSpan.TicksPerSecond)));
            var code = p.HasExited ? p.ExitCode : -1;
            return new ProcessResult(outText, errText, code, timedOut);
        }
    }

    private sealed class PipeReader
    {
        private readonly Thread _thread;
        private string _text = "";

        public PipeReader(StreamReader reader)
        {
            _thread = new Thread(() =>
            {
                // The pipe closes under this read when a child that kept it open outlives the caller.
                try { _text = reader.ReadToEnd(); } catch (Exception) { }
            }) { IsBackground = true };
            _thread.Start();
        }

        public string Result(TimeSpan wait) => _thread.Join(wait) ? _text : "";
    }
}
