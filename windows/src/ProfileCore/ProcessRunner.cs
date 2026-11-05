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
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            var timedOut = !p.WaitForExit(timeout);
            if (timedOut)
            {
                try { p.Kill(entireProcessTree: true); } catch (Exception) { }
                p.WaitForExit(2000);
            }
            // A child that keeps the pipes open must not hang the caller.
            Task.WaitAll([outTask, errTask], TimeSpan.FromSeconds(1));
            var code = p.HasExited ? p.ExitCode : -1;
            return new ProcessResult(outTask.IsCompletedSuccessfully ? outTask.Result : "",
                                     errTask.IsCompletedSuccessfully ? errTask.Result : "", code, timedOut);
        }
    }
}
