// Runs the published wrapper against a fake claude.exe, with a temporary USERPROFILE and a clean
// environment. Touches nothing real. Port of ShellTests/test_wrapper.zsh, plus Windows cases.
using System.Diagnostics;
using System.Text;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Wrapper.Tests;

sealed class Home : IDisposable
{
    public string Dir { get; }
    public string Root => Path.Combine(Dir, ".claude-profiles");
    public string Bin => Path.Combine(Root, "bin");
    public string FakeBin => Path.Combine(Dir, "fakebin");
    public string Added => Path.Combine(Root, "profiles", "aaaa1111");
    public string StubPath => $@"{Bin};{FakeBin};{Environment.SystemDirectory}";

    public Home()
    {
        var build = Build.Dir;
        Dir = Path.Combine(Path.GetTempPath(), "cpm-wrapper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Bin);
        Directory.CreateDirectory(Path.Combine(Root, "logs"));
        Directory.CreateDirectory(Added);
        Directory.CreateDirectory(Path.Combine(Dir, ".claude", "projects", "p"));
        Directory.CreateDirectory(FakeBin);
        File.Copy(Path.Combine(build, "bin", "claude.exe"), Path.Combine(Bin, "claude.exe"));
        File.Copy(Path.Combine(build, "bin", "claude.exe"), Path.Combine(Bin, "claude-as.exe"));
        File.Copy(Path.Combine(build, "fake", "claude.exe"), Path.Combine(FakeBin, "claude.exe"));
        WriteNames();
        SetDefault("builtin");
    }

    public void WriteNames() => File.WriteAllText(Path.Combine(Root, "names"),
        "main\tbuiltin\tCompany\t#2F6FDE\t-\n" + $"personal\taaaa1111\tPersonal\t#E8772E\t{Added}\n", new UTF8Encoding(false));

    public void SetDefault(string id) => File.WriteAllText(Path.Combine(Root, "default"), id + "\n");

    public string Wrapper(string name) => Path.Combine(Bin, name + ".exe");

    public Run Run(string exe, IEnumerable<string> args, Dictionary<string, string>? extra = null, string? path = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false), WorkingDirectory = Dir,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment.Clear();
        psi.Environment["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot");
        psi.Environment["windir"] = Environment.GetEnvironmentVariable("windir");
        psi.Environment["TEMP"] = Path.GetTempPath();
        psi.Environment["USERPROFILE"] = Dir;
        psi.Environment["PATH"] = path ?? StubPath;
        psi.Environment["PATHEXT"] = ".COM;.EXE;.BAT;.CMD";
        foreach (var (k, v) in extra ?? []) psi.Environment[k] = v;
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(20_000)) { p.Kill(true); throw new TimeoutException(exe); }
        return new Run(o.Result, e.Result, p.ExitCode);
    }

    public Run Claude(params string[] args) => Run(Wrapper("claude"), args);
    public Run ClaudeAs(params string[] args) => Run(Wrapper("claude-as"), args);

    public void Dispose()
    {
        try { Directory.Delete(Dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

sealed record Run(string Stdout, string Stderr, int Code)
{
    public string All => Stdout + Stderr;
    public bool RealRan => Stdout.Contains("ARGC=");
    public void Has(string s) => StringAssert.Contains(All, s);
    public void Hasnt(string s) => Assert.IsFalse(All.Contains(s, StringComparison.Ordinal), $"did not expect '{s}' in:\n{All}");
}

static class Build
{
    /// windows\build, found from the test assembly's folder. build.ps1 -Test publishes into it first.
    public static string Dir
    {
        get
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                var b = Path.Combine(d.FullName, "build");
                if (File.Exists(Path.Combine(b, "bin", "claude.exe")) && File.Exists(Path.Combine(b, "fake", "claude.exe"))) return b;
            }
            Assert.Inconclusive("Run build.ps1 -Test, which publishes the wrapper and the fake claude.exe first.");
            return "";
        }
    }
}

[TestClass]
public sealed class WrapperTests
{
    [TestMethod]
    public void DefaultBuiltinKeepsArgsAndUnsetsTheVariable()
    {
        using var h = new Home();
        var r = h.Claude("--flag");
        r.Has("ARGS=--flag");
        r.Has("CLAUDE_CONFIG_DIR=<unset>");
        r.Has("CLAUDE_PROFILE_ID=builtin");
        r.Has("CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1");
    }

    [TestMethod]
    public void ABlankValueIsRemovedNotPassedOn()
    {
        using var h = new Home();
        var r = h.Run(h.Wrapper("claude"), [], new() { ["CLAUDE_CONFIG_DIR"] = "" });
        r.Has("CLAUDE_CONFIG_DIR=<unset>");
        r.Hasnt("ignores your default");
    }

    [TestMethod]
    public void ParentValueInsideOurFolderIsIgnored()
    {
        using var h = new Home();
        h.Run(h.Wrapper("claude"), [], new() { ["CLAUDE_CONFIG_DIR"] = h.Added }).Has("CLAUDE_CONFIG_DIR=<unset>");
    }

    [TestMethod]
    public void ARemovedProfilesOldPathIsIgnored()
    {
        using var h = new Home();
        var r = h.Run(h.Wrapper("claude"), [], new() { ["CLAUDE_CONFIG_DIR"] = Path.Combine(h.Root, "profiles", "removed") });
        r.Has("CLAUDE_CONFIG_DIR=<unset>");
        r.Hasnt("ignores your default");
    }

    [TestMethod]
    public void ClaudeAsUsesTheExactConfigDir()
    {
        using var h = new Home();
        var r = h.ClaudeAs("personal");
        r.Has($"CLAUDE_CONFIG_DIR={h.Added}\n");
        r.Has("CLAUDE_PROFILE_ID=aaaa1111");
        h.ClaudeAs("main").Has("CLAUDE_CONFIG_DIR=<unset>");
    }

    [TestMethod]
    public void AChangedDefaultIsUsedByTheNextCall()
    {
        using var h = new Home();
        h.SetDefault("aaaa1111");
        h.Claude().Has($"CLAUDE_CONFIG_DIR={h.Added}");
    }

    [TestMethod]
    public void ANestedCallStaysOnItsProfile()
    {
        using var h = new Home();
        h.Run(h.Wrapper("claude"), [], new() { ["CLAUDE_PROFILE_ID"] = "aaaa1111" }).Has($"CLAUDE_CONFIG_DIR={h.Added}");
        h.Run(h.Wrapper("claude"), [], new() { ["CLAUDE_PROFILE_ID"] = "nosuch" }).Has("CLAUDE_PROFILE_ID=builtin");
    }

    [TestMethod]
    public void LoginVariablesWarnOnStderrAndStaySet()
    {
        using var h = new Home();
        foreach (var v in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN" })
        {
            var r = h.Run(h.Wrapper("claude"), [], new() { [v] = "dummy" });
            StringAssert.Contains(r.Stderr, $"{v} is set");
            StringAssert.Contains(r.Stdout, $"{v}=dummy");
            Assert.IsFalse(r.Stdout.Contains("is set."), v);
        }
    }

    [TestMethod]
    public void AMissingOrUnknownDefaultStopsPlainClaude()
    {
        using var h = new Home();
        File.Delete(Path.Combine(h.Root, "default"));
        var r = h.Claude();
        Assert.AreEqual(1, r.Code);
        r.Has("cpm doctor");
        Assert.IsFalse(r.RealRan);
        h.SetDefault("nosuch");
        r = h.Claude();
        Assert.AreEqual(1, r.Code);
        Assert.IsFalse(r.RealRan);
    }

    [TestMethod]
    public void AnUnknownNameListsTheValidOnes()
    {
        using var h = new Home();
        var r = h.ClaudeAs("nosuch");
        Assert.AreEqual(1, r.Code);
        r.Has("personal");
        Assert.IsFalse(r.RealRan);
        r = h.ClaudeAs();
        Assert.AreEqual(1, r.Code);
        r.Has("Usage: claude-as");
    }

    [TestMethod]
    public void UnreadableNamesStopsBoth()
    {
        using var h = new Home();
        File.Delete(Path.Combine(h.Root, "names"));
        var r = h.ClaudeAs("personal");
        Assert.AreEqual(1, r.Code);
        Assert.IsFalse(r.RealRan);
        r = h.Claude();
        Assert.AreEqual(1, r.Code);
        Assert.IsFalse(r.RealRan);
    }

    [TestMethod]
    public void ResumingAnotherProfilesSessionIsRefused()
    {
        using var h = new Home();
        const string sid = "11111111-2222-3333-4444-555555555555";
        Directory.CreateDirectory(Path.Combine(h.Added, "projects", "x"));
        File.WriteAllText(Path.Combine(h.Added, "projects", "x", sid + ".jsonl"), "");
        foreach (var form in new[] { new[] { "--resume", sid }, new[] { "--resume=" + sid }, new[] { "-r", sid } })
        {
            var r = h.ClaudeAs(["main", .. form]);
            Assert.AreEqual(1, r.Code, string.Join(' ', form));
            r.Has("belongs to profile Personal");
            r.Has($"claude-as personal --resume {sid}");
            Assert.IsFalse(r.RealRan);
        }
        h.ClaudeAs("personal", "--resume", sid).Has($"ARGS=--resume {sid}");
        h.ClaudeAs("main", "--resume", "some-search-term").Has("ARGS=--resume some-search-term");
        h.ClaudeAs("main", "--resume", "99999999-2222-3333-4444-555555555555").Has("ARGS=--resume 99999999");
    }

    [TestMethod]
    public void AnOutsideValueIsKeptWithAWarning()
    {
        using var h = new Home();
        var other = Path.Combine(h.Dir, "elsewhere");
        var r = h.Run(h.Wrapper("claude"), [], new() { ["CLAUDE_CONFIG_DIR"] = other });
        r.Has($"CLAUDE_CONFIG_DIR={other}");
        r.Has("ignores your default");
        r.Has("CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1");
        r.Hasnt("CLAUDE_PROFILE_ID=builtin");
    }

    [TestMethod]
    public void EachLaunchAddsOneLogLineAndAFailedLogDoesNotBlock()
    {
        using var h = new Home();
        var log = Path.Combine(h.Root, "logs", "launch.log");
        h.Claude();
        Assert.AreEqual(1, File.ReadAllLines(log).Length);
        Assert.AreEqual(4, File.ReadAllLines(log)[0].Split('\t').Length);
        Directory.Delete(Path.Combine(h.Root, "logs"), true);
        File.WriteAllText(Path.Combine(h.Root, "logs"), "not a folder");
        Assert.IsTrue(h.Claude().RealRan);
    }

    [TestMethod]
    public void ReachedByNameFromCmdAndPowerShell()
    {
        using var h = new Home();
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        h.Run(cmd, ["/d", "/c", "claude-as personal"]).Has($"CLAUDE_CONFIG_DIR={h.Added}");
        File.WriteAllText(Path.Combine(h.Dir, "s.cmd"), "@claude\r\n");
        h.Run(cmd, ["/d", "/c", Path.Combine(h.Dir, "s.cmd")]).Has("CLAUDE_PROFILE_ID=builtin");
        var ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        h.Run(ps, ["-NoProfile", "-Command", "claude-as personal"]).Has("CLAUDE_PROFILE_ID=aaaa1111");
    }

    [TestMethod]
    public void NoRealBinarySaysSo()
    {
        using var h = new Home();
        var r = h.Run(h.Wrapper("claude"), [], path: $"{h.Bin};{Environment.SystemDirectory}");
        Assert.AreEqual(1, r.Code);
        r.Has("cannot find the real claude");
        var npm = Path.Combine(h.Dir, "npm");
        Directory.CreateDirectory(npm);
        File.WriteAllText(Path.Combine(npm, "claude.cmd"), "@echo ARGC=ran\r\n");
        r = h.Run(h.Wrapper("claude"), [], path: $"{h.Bin};{npm};{Environment.SystemDirectory}");
        Assert.AreEqual(1, r.Code);
        r.Has("claude.cmd, which is not run");
        Assert.IsFalse(r.RealRan);
    }

    [TestMethod]
    public void TheBinFolderSpelledDifferentlyIsStillSkipped()
    {
        using var h = new Home();
        foreach (var spelling in new[] { h.Bin + @"\..\bin", h.Bin.ToUpperInvariant(), h.Bin + "\\", "\"" + h.Bin + "\"", h.Bin.Replace('\\', '/') })
            Assert.IsTrue(h.Run(h.Wrapper("claude"), [], path: $"{spelling};{h.FakeBin};{Environment.SystemDirectory}").RealRan, spelling);
    }

    [TestMethod]
    public void ExitCodesPassThrough()
    {
        using var h = new Home();
        Assert.AreEqual(7, h.Run(h.Wrapper("claude"), ["-p", "x"], new() { ["FAKE_EXIT"] = "7" }).Code);
        Assert.AreEqual(0, h.Claude().Code);
    }

    [TestMethod]
    public void ArgumentsRoundTripUnchanged()
    {
        // The S0 check C16 values: quotes, backslashes, empty strings, non-ASCII, shell characters.
        string[] values =
        [
            "plain", "with space", "embedded \"quote\"", @"trailing\", @"back\""slash", @"\\server\share\", "", " ",
            "%PATH%", "!BANG!", "^ & | < >", "tab\there", "Ünïcødé 中文 😀", ";", "'", "-x", "--flag=value",
        ];
        using var h = new Home();
        var r = h.ClaudeAs(["personal", .. values]);
        r.Has($"ARGC={values.Length}\n");
        for (var i = 0; i < values.Length; i++) r.Has($"ARG{i}=[{values[i]}]\n");
    }

    [TestMethod]
    public void ProfileLabelPrintsTheNameInItsColour()
    {
        using var h = new Home();
        var r = h.Run(h.Wrapper("claude"), ["--profile-label", "aaaa1111"]);
        Assert.AreEqual("\u001b[38;2;232;119;46m\u25CF\u001b[0m Personal\n", r.Stdout);
        Assert.AreEqual("", h.Run(h.Wrapper("claude"), ["--profile-label", "nosuch"]).Stdout);
        Assert.IsFalse(File.Exists(Path.Combine(h.Root, "logs", "launch.log")), "the status line is not a launch");
    }

    [TestMethod]
    public void LaunchTimeIsSmall()
    {
        // S0 check C22 records the real number; this only catches a large regression.
        using var h = new Home();
        h.Claude();
        var direct = Time(() => h.Run(Path.Combine(h.FakeBin, "claude.exe"), []));
        var wrapped = Time(() => h.Claude());
        Assert.IsTrue(wrapped - direct < 250, $"wrapper added {wrapped - direct} ms");
    }

    static double Time(Action a)
    {
        var best = double.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            var t = Stopwatch.StartNew();
            a();
            best = Math.Min(best, t.Elapsed.TotalMilliseconds);
        }
        return best;
    }
}
