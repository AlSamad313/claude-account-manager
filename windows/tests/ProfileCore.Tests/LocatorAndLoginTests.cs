using ProfileCore;

namespace ProfileCore.Tests;

[TestClass]
public sealed class ConfigLocatorTests
{
    [TestMethod]
    public void BuiltinNeverSetsConfigDir()
    {
        using var s = new Sandbox();
        var p = s.Store.DefaultProfile();
        Assert.IsTrue(p.Builtin);
        var env = s.Locator.Environment(p);
        Assert.IsFalse(env.Set.ContainsKey("CLAUDE_CONFIG_DIR"));
        Assert.IsTrue(env.Unset.Contains("CLAUDE_CONFIG_DIR"));
        Assert.AreEqual(Path.Combine(s.Home, ".claude.json"), s.Locator.ConfigFile(p));
        Assert.AreEqual(Path.Combine(s.Home, ".claude", ".credentials.json"), s.Locator.CredentialsFile(p));
    }

    [TestMethod]
    public void AddedProfileUsesStoredPathUnchanged()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("Personal", "personal", "#E8772E");
        Assert.AreEqual(p.ConfigDir, s.Locator.Environment(p).Set["CLAUDE_CONFIG_DIR"]);
        Assert.AreEqual(p.ConfigDir + "\\.claude.json", s.Locator.ConfigFile(p));
    }

    [TestMethod]
    public void NewConfigDirIsExact()
    {
        using var s = new Sandbox();
        s.Store.Load();
        var dir = s.Locator.NewConfigDir("abc");
        Assert.IsFalse(dir.Contains('~'), dir);
        Assert.IsFalse(dir.Contains('/'), dir);
        Assert.IsFalse(dir.EndsWith('\\'), dir);
        Assert.IsFalse(dir.StartsWith(@"\\?\", StringComparison.Ordinal), dir);
        Assert.IsTrue(char.IsUpper(dir[0]) && dir[1] == ':', dir);
        Assert.IsTrue(dir.EndsWith(@"\.claude-profiles\profiles\abc", StringComparison.Ordinal), dir);
    }

    [TestMethod]
    public void NewConfigDirExpandsShortNames()
    {
        // The temp folder on this kind of PC is often reached through an 8.3 name such as ABDULS~1.
        var shortHome = Path.GetTempPath();
        var s = new ConfigLocator(shortHome, Path.Combine(shortHome, "cpm-test-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(s.ProfilesRoot);
        try
        {
            var dir = s.NewConfigDir("abc");
            Assert.IsFalse(dir.Contains('~'), dir);
        }
        finally { Directory.Delete(s.ProfilesRoot); }
    }

    [TestMethod]
    public void LoginVariablesAreNeverTouched()
    {
        using var s = new Sandbox();
        var env = s.Locator.Environment(s.Store.DefaultProfile());
        foreach (var v in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN" })
        {
            Assert.IsFalse(env.Set.ContainsKey(v));
            Assert.IsFalse(env.Unset.Contains(v));
        }
    }
}

[TestClass]
public sealed class LoginCoordinatorTests
{
    static (LoginCoordinator C, FakeRunner Runner) Coordinator(Sandbox s, FakeProcesses? procs = null)
    {
        var bin = s.PathOf("realbin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "claude.exe"), "");
        var runner = new FakeRunner();
        var cli = new ClaudeCLI(s.Locator, runner, new ClaudeBinaryLocator(s.Locator, bin));
        return (new LoginCoordinator(s.Store, cli, procs ?? new FakeProcesses()), runner);
    }

    static Profile Pending(Sandbox s) => s.Store.Create("A", "a", "#000000");
    static Profile Reload(Sandbox s, string id) => s.Store.Load().ProfileById(id)!;

    [TestMethod]
    public void BeginReturnsTheCommandAndRecordsTheStart()
    {
        using var s = new Sandbox();
        var p = Pending(s);
        Assert.AreEqual("claude-as a auth login", Coordinator(s).C.Begin(p));
        var q = Reload(s, p.Id);
        Assert.IsTrue(q.KeychainBaseline == null && q.LoginStartedAt != null);
    }

    [TestMethod]
    public void PollNeverCompletes()
    {
        using var s = new Sandbox();
        var c = Coordinator(s).C;
        var p = Pending(s);
        c.Begin(p);
        Assert.IsInstanceOfType<LoginState.Waiting>(c.Poll(Reload(s, p.Id)));
        s.SignIn(p, "u");
        var d = c.Poll(Reload(s, p.Id)) as LoginState.Detected;
        Assert.AreEqual("u", d?.Account.AccountUuid);
        Assert.AreEqual(ProfileState.PendingLogin, Reload(s, p.Id).State);
    }

    [TestMethod]
    public void AnAccountWithoutTheLoginFileIsNotASignIn()
    {
        using var s = new Sandbox();
        var c = Coordinator(s).C;
        var p = Pending(s);
        c.Begin(p);
        s.SignIn(p, "u");
        File.Delete(s.Locator.CredentialsFile(p));
        Assert.IsInstanceOfType<LoginState.Waiting>(c.Poll(Reload(s, p.Id)));
    }

    [TestMethod]
    public void PollTimesOut()
    {
        using var s = new Sandbox();
        var (c0, _) = Coordinator(s);
        var p = Pending(s);
        c0.Begin(p);
        var bin = s.PathOf("realbin");
        var c = new LoginCoordinator(s.Store, new ClaudeCLI(s.Locator, new FakeRunner(), new ClaudeBinaryLocator(s.Locator, bin)))
        {
            Now = () => DateTime.UtcNow + LoginCoordinator.Timeout + TimeSpan.FromSeconds(5),
        };
        Assert.IsInstanceOfType<LoginState.TimedOut>(c.Poll(Reload(s, p.Id)));
    }

    [TestMethod]
    public void KeepCompletesTheLogin()
    {
        using var s = new Sandbox();
        var c = Coordinator(s).C;
        var p = Pending(s);
        c.Begin(p);
        s.SignIn(p, "u", "me@example.com");
        var r = c.Keep(Reload(s, p.Id));
        Assert.AreEqual("me@example.com", r.Account.Email);
        var q = Reload(s, p.Id);
        Assert.IsTrue(q.State == ProfileState.Ready && q.KeychainService == null && q.LoginStartedAt == null);
        StringAssert.Contains(s.Text(Path.Combine(s.Locator.LogsDir, "writes.log")), "login kept");
    }

    [TestMethod]
    public void KeepWithoutASignInIsRefused()
    {
        using var s = new Sandbox();
        var c = Coordinator(s).C;
        var p = Pending(s);
        c.Begin(p);
        Assert.ThrowsExactly<ProfileException>(() => c.Keep(Reload(s, p.Id)));
    }

    [TestMethod]
    public void RetryLogsOutWithTheProfilesFolder()
    {
        using var s = new Sandbox();
        var (c, runner) = Coordinator(s);
        var p = Pending(s);
        s.SignIn(p, "u");
        c.Retry(p);
        Assert.IsTrue(runner.Calls[0].Args.SequenceEqual(["auth", "logout"]));
        Assert.AreEqual(p.ConfigDir, runner.Calls[0].Env!.Set["CLAUDE_CONFIG_DIR"]);
        Assert.IsNotNull(Reload(s, p.Id).LoginStartedAt);
    }

    [TestMethod]
    public void AFailedLogoutStopsRetry()
    {
        using var s = new Sandbox();
        var (c, runner) = Coordinator(s);
        runner.Handler = (_, _) => new ProcessResult(Stderr: "boom", ExitCode: 1);
        var p = Pending(s);
        s.SignIn(p, "u");
        StringAssert.Contains(Assert.ThrowsExactly<ProfileException>(() => c.Retry(p)).Message, "boom");
    }

    [TestMethod]
    public void DiscardRefusesWhileASessionIsLive()
    {
        using var s = new Sandbox();
        var procs = new FakeProcesses();
        var c = Coordinator(s, procs).C;
        var p = Pending(s);
        var start = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        procs.Running[4242] = start;
        s.Write($$"""{"pid":4242,"sessionId":"x","procStart":"{{start.ToFileTimeUtc()}}"}""", $".claude-profiles/profiles/{p.Id}/sessions/4242.json");
        Assert.ThrowsExactly<ProfileException>(() => c.Discard(p));
        procs.Running.Clear();
        c.Discard(p);
        Assert.IsFalse(Directory.Exists(p.ConfigDir));
    }
}

[TestClass]
public sealed class ReconcilerTests
{
    static Reconciler R(Sandbox s) => new(s.Store, new LoginCoordinator(s.Store));

    [TestMethod]
    public void OrphanIsListedAndNotDeleted()
    {
        using var s = new Sandbox();
        s.Store.Load();
        s.Write("x", ".claude-profiles/profiles/orphan/file");
        CollectionAssert.AreEqual(new[] { "orphan" }, R(s).Run().OrphanFolders);
        Assert.IsTrue(File.Exists(s.PathOf(".claude-profiles/profiles/orphan/file")));
    }

    [TestMethod]
    public void AppFoldersAreNotOrphans()
    {
        using var s = new Sandbox();
        s.Write("x", ".claude-profiles/bin/x");
        s.Write("x", ".claude-profiles/logs/x");
        Assert.AreEqual(0, R(s).Run().OrphanFolders.Count);
    }

    [TestMethod]
    public void RowWithNoFolderBecomesError()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        Directory.Delete(p.ConfigDir!, true);
        R(s).Run();
        Assert.AreEqual(ProfileState.Error, s.Store.Load().ProfileById(p.Id)?.State);
        Assert.IsFalse(s.Text(s.Locator.NamesFile).Contains($"a\t{p.Id}"));
    }

    [TestMethod]
    public void ARowRecoversWhenItsFolderComesBack()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        Directory.Delete(p.ConfigDir!, true);
        R(s).Run();
        Directory.CreateDirectory(p.ConfigDir!);
        R(s).Run();
        var back = s.Store.Load().ProfileById(p.Id)!;
        Assert.IsTrue(back.State == ProfileState.LoggedOut && back.LastError == null);
        s.SignIn(p, "u9");
        R(s).Run();
        Assert.AreEqual(ProfileState.Ready, s.Store.Load().ProfileById(p.Id)?.State);
    }

    [TestMethod]
    public void ADetectedLoginIsReportedAndNeverCompleted()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        s.SignIn(p, "u", "me@example.com");
        var r = R(s).Run();
        Assert.AreEqual(1, r.LoginNotices.Count);
        StringAssert.Contains(r.LoginNotices[0], "me@example.com");
        StringAssert.Contains(r.LoginNotices[0], "cpm keep a");
        Assert.AreEqual(ProfileState.PendingLogin, s.Store.Load().ProfileById(p.Id)?.State);
    }

    [TestMethod]
    public void LogoutAndLoginInsideAProfileAreNoticed()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        s.SignIn(p, "1", "x@e.com");
        s.Store.CompleteLogin(p.Id, new Account("1", "x@e.com", null));
        s.SignIn(p, "2", "y@e.com");
        StringAssert.Contains(R(s).Run().AccountNotices.FirstOrDefault() ?? "", "y@e.com");
        File.Delete(s.Locator.CredentialsFile(p)); // what auth logout removes on Windows
        R(s).Run();
        Assert.AreEqual(ProfileState.LoggedOut, s.Store.Load().ProfileById(p.Id)?.State);
        s.SignIn(p, "2");
        R(s).Run();
        Assert.AreEqual(ProfileState.Ready, s.Store.Load().ProfileById(p.Id)?.State);
    }

    [TestMethod]
    public void MissingDefaultIsRepaired()
    {
        using var s = new Sandbox();
        s.Store.Load();
        File.Delete(s.Locator.DefaultFile);
        R(s).Run();
        Assert.IsTrue(File.Exists(s.Locator.DefaultFile));
    }
}
