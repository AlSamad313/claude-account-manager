using ProfileCore;

namespace ProfileCore.Tests;

/// A remover whose every outside effect is faked and written to a journal: Claude Code's logout,
/// the Recycle Bin, the user PATH, $PROFILE, the startup value and the app's mutex.
sealed class RemoverWorld : IDisposable
{
    public Sandbox S { get; } = new();
    public List<string> Journal { get; } = [];
    public FakeRunner Claude { get; } = new();
    public FakeProcesses Processes { get; } = new();
    public FakeUserPathStore PathStore { get; }
    public string Docs { get; }
    public bool TrashFails { get; set; }
    public bool AppRunning { get; set; }
    public bool BinInUse { get; set; }
    public List<string> DeletedLater { get; } = [];

    public RemoverWorld()
    {
        PathStore = new FakeUserPathStore($@"{S.Locator.BinDir};C:\Tools");
        Docs = S.PathOf("Documents");
        var real = S.PathOf("realbin");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "claude.exe"), "");
        Claude.Handler = (_, args) => { Journal.Add(string.Join(' ', args)); OnLogout?.Invoke(); return new ProcessResult(Stderr: LogoutFails ? "no network" : "", ExitCode: LogoutFails ? 1 : 0); };
        RealBin = real;
    }

    public string RealBin { get; }
    public Action? OnLogout { get; set; }
    public bool LogoutFails { get; set; }

    public ProfileRemover Remover() => new(S.Store,
        cli: new ClaudeCLI(S.Locator, Claude, new ClaudeBinaryLocator(S.Locator, RealBin)),
        live: new LiveSessionReader(S.Locator, Processes),
        trash: path =>
        {
            if (TrashFails) throw new ProfileException("trash failed");
            Journal.Add("trash:" + Path.GetFileName(path));
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true); else File.Delete(path);
        },
        userPath: new UserPath(S.Locator, PathStore),
        powerShell: new PowerShellProfile(S.Locator, Docs),
        removeStartupValue: () => Journal.Add("startup"),
        appRunning: () => AppRunning,
        deleteLater: root => { DeletedLater.Add(root); return true; });

    public Profile SignedIn(string name = "work")
    {
        var p = S.Store.Create(char.ToUpperInvariant(name[0]) + name[1..], name, "#000000");
        S.SignIn(p, "u-" + name, name + "@example.com");
        S.Store.CompleteLogin(p.Id, new Account("u-" + name, name + "@example.com", null));
        return S.Store.Load().ProfileById(p.Id)!;
    }

    public void Live(Profile p, int pid = 5)
    {
        SessionData.Record(S, p, pid, "x");
        Processes.Running[pid] = SessionData.Start;
    }

    public string Text(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

    public void Dispose() => S.Dispose();
}

[TestClass]
public sealed class ProfileRemoverTests
{
    [TestMethod]
    public void RemovesInOrderAndKeepsTheRest()
    {
        using var w = new RemoverWorld();
        var p = w.SignedIn();
        var other = w.SignedIn("other");
        w.S.Write("x", $".claude-profiles/backups/{p.Id}/b/file");
        var r = w.Remover().Remove(p);
        Assert.IsEmpty(r.Warnings, string.Join(" ", r.Warnings));
        CollectionAssert.AreEqual(new[] { "auth logout", "trash:" + p.Id, "trash:" + p.Id }, w.Journal);
        Assert.AreEqual(p.ConfigDir, w.Claude.Calls[0].Env!.Set["CLAUDE_CONFIG_DIR"], "logout runs on this profile only");
        var reg = w.S.Store.Load();
        Assert.IsNull(reg.ProfileById(p.Id));
        Assert.IsNotNull(reg.ProfileById(other.Id));
        Assert.IsTrue(File.Exists(w.S.Locator.CredentialsFile(other)), "other profiles stay signed in");
        Assert.IsFalse(Directory.Exists(p.ConfigDir));
        Assert.DoesNotContain(p.Id, w.Text(w.S.Locator.NamesFile));
    }

    /// Flow 7 step 5: the login file is deleted outright before the folder goes to the Recycle Bin.
    [TestMethod]
    public void TheLoginFileNeverReachesTheRecycleBin()
    {
        using var w = new RemoverWorld();
        var p = w.SignedIn();
        var cred = w.S.Locator.CredentialsFile(p);
        bool? credAtTrash = null;
        var remover = new ProfileRemover(w.S.Store,
            cli: new ClaudeCLI(w.S.Locator, w.Claude, new ClaudeBinaryLocator(w.S.Locator, w.RealBin)),
            live: new LiveSessionReader(w.S.Locator, w.Processes),
            trash: path => { if (path == p.ConfigDir) credAtTrash = File.Exists(cred); Directory.Delete(path, true); });
        remover.Remove(p);
        Assert.IsFalse(credAtTrash, "logout did not remove it here, so the remover deleted it first");
    }

    [TestMethod]
    public void TheDefaultMovesBeforeAnythingElse()
    {
        using var w = new RemoverWorld();
        var p = w.SignedIn();
        w.S.Store.SetDefault(p.Id);
        string seen = "", names = "";
        w.OnLogout = () => { seen = w.Text(w.S.Locator.DefaultFile); names = w.Text(w.S.Locator.NamesFile); };
        w.Remover().Remove(p);
        Assert.AreEqual(Profile.BuiltinId, seen.Trim(), "the default moved before logout ran");
        Assert.DoesNotContain(p.Id, names, "nothing can start in this folder during removal");
        Assert.AreEqual(Profile.BuiltinId, w.Text(w.S.Locator.DefaultFile).Trim());
    }

    [TestMethod]
    public void TheBuiltinProfileIsRefused()
    {
        using var w = new RemoverWorld();
        var b = w.S.Store.DefaultProfile();
        Assert.ThrowsExactly<ProfileException>(() => w.Remover().Remove(b));
        Assert.IsEmpty(w.Journal);
    }

    [TestMethod]
    public void AFolderOutsideTheProfilesFolderIsRefused()
    {
        using var w = new RemoverWorld();
        var p = w.SignedIn();
        var outside = w.S.PathOf("elsewhere");
        Directory.CreateDirectory(outside);
        var reg = File.ReadAllText(w.S.Locator.RegistryFile).Replace(p.ConfigDir!.Replace("\\", "\\\\"), outside.Replace("\\", "\\\\"));
        File.WriteAllText(w.S.Locator.RegistryFile, reg);
        var moved = w.S.Store.Load().ProfileById(p.Id)!;
        Assert.AreEqual(outside, moved.ConfigDir);
        Assert.ThrowsExactly<ProfileException>(() => w.Remover().Remove(moved));
        Assert.IsEmpty(w.Journal);
        Assert.IsTrue(Directory.Exists(outside));
        Assert.AreEqual(ProfileState.Ready, w.S.Store.Load().ProfileById(p.Id)!.State);
    }

    [TestMethod]
    public void ALiveSessionRefusesAndChangesNothing()
    {
        using var w = new RemoverWorld();
        var p = w.SignedIn();
        w.S.Store.SetDefault(p.Id);
        w.Live(p);
        Assert.ThrowsExactly<ProfileException>(() => w.Remover().Remove(p));
        Assert.IsEmpty(w.Journal);
        Assert.AreEqual(ProfileState.Ready, w.S.Store.Load().ProfileById(p.Id)!.State);
        Assert.AreEqual(p.Id, w.Text(w.S.Locator.DefaultFile).Trim());
    }

    [TestMethod]
    public void AFailedLogoutWarnsAndStillFinishes()
    {
        using var w = new RemoverWorld();
        var p = w.SignedIn();
        w.LogoutFails = true;
        var r = w.Remover().Remove(p);
        Assert.Contains("no network", r.Warnings.Single());
        Assert.IsNull(w.S.Store.Load().ProfileById(p.Id));
        Assert.IsFalse(Directory.Exists(p.ConfigDir));
    }

    [TestMethod]
    public void AFailureLeavesItDeletingAndResumeFinishes()
    {
        using var w = new RemoverWorld();
        var p = w.SignedIn();
        var r = w.Remover();
        w.TrashFails = true;
        Assert.ThrowsExactly<ProfileException>(() => r.Remove(p));
        Assert.AreEqual(ProfileState.Deleting, w.S.Store.Load().ProfileById(p.Id)!.State);
        Assert.DoesNotContain(p.Id, w.Text(w.S.Locator.NamesFile), "it cannot start while half removed");
        Assert.IsFalse(File.Exists(w.S.Locator.CredentialsFile(p)), "the login is gone even so");

        w.TrashFails = false;
        CollectionAssert.AreEqual(new[] { "Work" }, r.ResumePending().Select(x => x.DisplayName).ToList());
        Assert.IsNull(w.S.Store.Load().ProfileById(p.Id));
        Assert.IsFalse(Directory.Exists(p.ConfigDir));
        Assert.IsEmpty(r.ResumePending());
    }

    [TestMethod]
    public void ResumeWorksIfTheFolderIsAlreadyGone()
    {
        using var w = new RemoverWorld();
        var p = w.SignedIn();
        w.S.Store.MarkDeleting(p.Id);
        Directory.Delete(p.ConfigDir!, true);
        w.Remover().ResumePending();
        Assert.IsNull(w.S.Store.Load().ProfileById(p.Id));
        Assert.DoesNotContain("auth logout", w.Journal, "logout would recreate the folder");
        Assert.IsFalse(Directory.Exists(p.ConfigDir));
    }

    [TestMethod]
    public void ResumeWaitsForALiveSession()
    {
        using var w = new RemoverWorld();
        var p = w.SignedIn();
        w.S.Store.MarkDeleting(p.Id);
        w.Live(p);
        Assert.IsEmpty(w.Remover().ResumePending());
        Assert.AreEqual(ProfileState.Deleting, w.S.Store.Load().ProfileById(p.Id)!.State);
    }

    [TestMethod]
    public void ReconcilerResumesARemoval()
    {
        using var w = new RemoverWorld();
        var p = w.SignedIn();
        w.S.Store.MarkDeleting(p.Id);
        var r = new Reconciler(w.S.Store, new LoginCoordinator(w.S.Store), w.Remover()).Run();
        CollectionAssert.AreEqual(new[] { "Work" }, r.RemovalsResumed);
        Assert.IsNull(w.S.Store.Load().ProfileById(p.Id));
    }

    [TestMethod]
    public void AReconcileWhoseRemovalStillFailsCarriesOn()
    {
        using var w = new RemoverWorld();
        var p = w.SignedIn();
        w.S.Store.MarkDeleting(p.Id);
        w.TrashFails = true;
        var r = new Reconciler(w.S.Store, new LoginCoordinator(w.S.Store), w.Remover()).Run();
        Assert.IsEmpty(r.RemovalsResumed);
        Assert.Contains("trash failed", r.RemovalWarnings.Single());
        Assert.AreEqual(ProfileState.Deleting, w.S.Store.Load().ProfileById(p.Id)!.State);
    }

    [TestMethod]
    public void UninstallRemovesEverythingButTheBuiltinProfile()
    {
        using var w = new RemoverWorld();
        var a = w.SignedIn();
        var b = w.SignedIn("other");
        w.S.Write("settings", ".claude/settings.json");
        w.S.Write("{}", ".claude.json");
        var before = w.Text(w.S.PathOf(".claude.json"));
        Directory.CreateDirectory(w.S.Locator.BinDir);
        File.WriteAllText(Path.Combine(w.S.Locator.BinDir, "cpm.exe"), "");
        var ps = new PowerShellProfile(w.S.Locator, w.Docs);
        ps.Add();
        Assert.IsNotEmpty(ps.Remove(), "the test's $PROFILE line is found");
        ps.Add();

        var r = w.Remover().Uninstall();
        Assert.HasCount(2, r.Removed);
        Assert.IsFalse(Directory.Exists(w.S.Locator.ProfilesRoot));
        Assert.IsFalse(Directory.Exists(a.ConfigDir) || Directory.Exists(b.ConfigDir));
        Assert.AreEqual("settings", w.Text(w.S.PathOf(".claude/settings.json")));
        Assert.AreEqual(before, w.Text(w.S.PathOf(".claude.json")));
        Assert.IsTrue(r.PathRemoved);
        Assert.AreEqual(@"C:\Tools", w.PathStore.Value, "only our PATH entry goes");
        Assert.IsNotEmpty(r.CompletionRemoved);
        Assert.Contains("startup", w.Journal);
        Assert.IsFalse(r.BinLeft);
        Assert.IsEmpty(w.DeletedLater);
    }

    [TestMethod]
    public void UninstallRefusesBeforeChangingAnything()
    {
        using var w = new RemoverWorld();
        var a = w.SignedIn();
        var b = w.SignedIn("other");
        w.Live(b);
        Assert.ThrowsExactly<ProfileException>(() => w.Remover().Uninstall());
        Assert.IsEmpty(w.Journal);
        Assert.AreEqual(ProfileState.Ready, w.S.Store.Load().ProfileById(a.Id)!.State);
        Assert.AreEqual(0, w.PathStore.Writes);
    }

    [TestMethod]
    public void UninstallRefusesWhileTheAppRuns()
    {
        using var w = new RemoverWorld();
        var a = w.SignedIn();
        w.AppRunning = true;
        var e = Assert.ThrowsExactly<ProfileException>(() => w.Remover().Uninstall());
        Assert.Contains("tray menu", e.Message);
        Assert.IsEmpty(w.Journal);
        Assert.IsTrue(Directory.Exists(a.ConfigDir));
    }

    /// cpm runs from bin, so it cannot delete itself: a hidden cmd deletes bin after cpm exits.
    [TestMethod]
    public void ABinFileInUseIsDeletedLater()
    {
        using var w = new RemoverWorld();
        w.SignedIn();
        Directory.CreateDirectory(w.S.Locator.BinDir);
        var cpm = Path.Combine(w.S.Locator.BinDir, "cpm.exe");
        File.WriteAllText(cpm, "");
        File.WriteAllText(Path.Combine(w.S.Locator.BinDir, "claude-as.exe"), "");
        UninstallResult r;
        using (new FileStream(cpm, FileMode.Open, FileAccess.Read, FileShare.Read))
            r = w.Remover().Uninstall();
        Assert.IsTrue(r.BinLeft);
        Assert.IsTrue(r.BinDeletedLater);
        CollectionAssert.AreEqual(new[] { w.S.Locator.ProfilesRoot }, w.DeletedLater);
        Assert.IsFalse(File.Exists(Path.Combine(w.S.Locator.BinDir, "claude-as.exe")), "what can go now goes now");
        var left = Directory.EnumerateFileSystemEntries(w.S.Locator.ProfilesRoot).Select(Path.GetFileName).ToList();
        Assert.Contains("bin", left);
        Assert.IsTrue(left.All(n => n is "bin" or ".lock"), "only bin and the lock file wait: " + string.Join(", ", left));
    }

    /// Only with CPM_REAL_RECYCLE_BIN=1: the real Recycle Bin, with a throwaway folder from the sandbox,
    /// named cpm-recycle-test so it is easy to find and empty afterwards.
    [TestMethod]
    public void TheRealRecycleBinTakesAFileAndAFolder()
    {
        using var s = new Sandbox();
        if (Environment.GetEnvironmentVariable("CPM_REAL_RECYCLE_BIN") != "1") { Assert.Inconclusive("Set CPM_REAL_RECYCLE_BIN=1 to use the real Recycle Bin."); return; }
        if (!WinProcess.HasRecycleBin(s.Home)) { Assert.Inconclusive("The temp drive has no Recycle Bin."); return; }
        var dir = s.PathOf("cpm-recycle-test");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".credentials.json"), "{}");
        var file = s.PathOf("cpm-recycle-test.txt");
        File.WriteAllText(file, "x");
        RecycleBin.Move(dir);
        RecycleBin.Move(file);
        Assert.IsFalse(Directory.Exists(dir));
        Assert.IsFalse(File.Exists(file));
    }
}
