using ProfileCore;

namespace ProfileCore.Tests;

[TestClass]
public sealed class ProfileStoreTests
{
    [TestMethod]
    public void SeedRowUsesOrganizationNameWhenPresent()
    {
        using var s = new Sandbox();
        s.Write("""{"oauthAccount":{"accountUuid":"u1","emailAddress":"a@example.com","organizationName":"Acme"}}""", ".claude.json");
        s.Write("{}", ".claude/.credentials.json");
        var p = s.Store.DefaultProfile();
        Assert.IsTrue(p.Id == "builtin" && p.CommandName == "main" && p.DisplayName == "Acme" && p.Color == "#2F6FDE");
        Assert.IsTrue(p.State == ProfileState.Ready && p.ConfigDir == null);
    }

    [TestMethod]
    public void SeedRowFallsBackToMain()
    {
        using var s = new Sandbox();
        var p = s.Store.DefaultProfile();
        Assert.IsTrue(p.DisplayName == "Main" && p.State == ProfileState.LoggedOut);
    }

    [TestMethod]
    public void SeedWithAnAccountButNoLoginFileIsLoggedOut()
    {
        using var s = new Sandbox();
        s.Write("""{"oauthAccount":{"accountUuid":"u1"}}""", ".claude.json");
        Assert.AreEqual(ProfileState.LoggedOut, s.Store.DefaultProfile().State);
    }

    [TestMethod]
    public void SeedNeverWritesToBuiltinFolder()
    {
        using var s = new Sandbox();
        s.Store.Create("P", "p", "#000000");
        Assert.IsFalse(Directory.Exists(s.Locator.BuiltinDir));
        Assert.IsFalse(File.Exists(Path.Combine(s.Home, ".claude.json")));
    }

    [TestMethod]
    public void CreateWritesFilesInOrder()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("Personal", "personal", "#E8772E");
        Assert.AreEqual(ProfileState.PendingLogin, p.State);
        var names = s.Text(s.Locator.NamesFile);
        StringAssert.Contains(names, $"personal\t{p.Id}\tPersonal\t#E8772E\t{p.ConfigDir}\n");
        StringAssert.Contains(names, "main\tbuiltin\t");
        Assert.IsFalse(names.Contains('\r'), "LF line ends");
        Assert.AreEqual("builtin\n", s.Text(s.Locator.DefaultFile));
        foreach (var f in new[] { s.Locator.RegistryFile, s.Locator.NamesFile, s.Locator.DefaultFile })
        {
            var bytes = File.ReadAllBytes(f);
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB, $"{f} has no BOM");
        }
        Assert.IsTrue(Directory.Exists(p.ConfigDir));
        var settings = s.Text(Path.Combine(p.ConfigDir!, "settings.json"));
        StringAssert.Contains(settings, "--profile-label " + p.Id);
        StringAssert.Contains(settings, "claude.exe");
    }

    [TestMethod]
    public void RegistryFileUsesTheMacSchema()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        var json = s.Text(s.Locator.RegistryFile);
        StringAssert.Contains(json, "\"schemaVersion\": 1");
        StringAssert.Contains(json, "\"defaultProfileId\": \"builtin\"");
        StringAssert.Contains(json, "\"state\": \"pendingLogin\"");
        StringAssert.Contains(json, "\"commandName\": \"a\"");
        StringAssert.Matches(json, new System.Text.RegularExpressions.Regex("\"createdAt\": \"\\d{4}-\\d\\d-\\d\\dT\\d\\d:\\d\\d:\\d\\dZ\""));
        Assert.IsFalse(json.Contains("keychainService"), "null keys are left out, as Swift does");
        Assert.IsTrue(json.IndexOf("\"builtin\":", StringComparison.Ordinal) < json.IndexOf("\"color\":", StringComparison.Ordinal), "keys sorted");
        Assert.AreEqual(p.Id, new ProfileStore(s.Locator).Load().ProfileByCommand("a")!.Id, "round trip");
    }

    [TestMethod]
    public void RenameChangesOnlyTheDisplayName()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        s.Store.Rename(p.Id, "B");
        var q = s.Store.Load().ProfileById(p.Id)!;
        Assert.IsTrue(q.DisplayName == "B" && q.CommandName == "a" && q.ConfigDir == p.ConfigDir);
        StringAssert.Contains(s.Text(s.Locator.NamesFile), $"a\t{p.Id}\tB\t");
    }

    [TestMethod]
    public void HostileNamesAreRefused()
    {
        using var s = new Sandbox();
        foreach (var bad in new[] { "a b", "A", "", "builtin", "$(x)", "a/b", "a\\b", "--help", "-x", "con:", new string('a', 25) })
            Assert.ThrowsExactly<ProfileException>(() => s.Store.Create("X", bad, "#000000"), bad);
        foreach (var bad in new[] { "", "a\tb", "a\nb", "a\u001B]0;x", "a\u200Eb", new string('a', 41) })
            Assert.ThrowsExactly<ProfileException>(() => s.Store.Create(bad, "ok", "#000000"), bad);
        foreach (var bad in new[] { "red", "#12345", "#GGGGGG", "#123456;x" })
            Assert.ThrowsExactly<ProfileException>(() => s.Store.Create("X", "ok", bad), bad);
    }

    [TestMethod]
    public void CommandNamesAreUnique()
    {
        using var s = new Sandbox();
        s.Store.Create("A", "a", "#000000");
        var e = Assert.ThrowsExactly<ProfileException>(() => s.Store.Create("B", "a", "#000000"));
        StringAssert.Contains(e.Message, "already uses");
    }

    [TestMethod]
    public void SetDefaultRewritesDefaultFile()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        s.Store.SetDefault(p.Id);
        Assert.AreEqual(p.Id + "\n", s.Text(s.Locator.DefaultFile));
        Assert.AreEqual(p.Id, s.Store.DefaultProfile().Id);
    }

    [TestMethod]
    public void RepairRewritesBrokenFiles()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        s.Store.SetDefault(p.Id);
        File.WriteAllText(s.Locator.DefaultFile, "nonsense\n");
        File.Delete(s.Locator.NamesFile);
        s.Store.RepairFiles();
        Assert.AreEqual(p.Id + "\n", s.Text(s.Locator.DefaultFile));
        StringAssert.Contains(s.Text(s.Locator.NamesFile), $"a\t{p.Id}\t");
    }

    [TestMethod]
    public void AnUnchangedFileIsNotRewritten()
    {
        using var s = new Sandbox();
        s.Store.Load();
        var old = DateTime.UtcNow.AddSeconds(-500);
        var files = new[] { s.Locator.RegistryFile, s.Locator.NamesFile, s.Locator.DefaultFile };
        foreach (var f in files) File.SetLastWriteTimeUtc(f, old);
        s.Store.RepairFiles();
        foreach (var f in files) Assert.IsTrue(Math.Abs((File.GetLastWriteTimeUtc(f) - old).TotalSeconds) < 2, f);
    }

    [TestMethod]
    public void UnreadableRegistryIsNotOverwritten()
    {
        using var s = new Sandbox();
        s.Store.Load();
        File.WriteAllText(s.Locator.RegistryFile, "{broken");
        Assert.ThrowsExactly<ProfileException>(() => s.Store.Load());
        Assert.AreEqual("{broken", s.Text(s.Locator.RegistryFile));
    }

    [TestMethod]
    public void AStaleProcessDoesNotOverwriteANewerChange()
    {
        using var s = new Sandbox();
        var other = s.NewStore();
        s.Store.Load();
        var a = s.Store.Create("A", "a", "#000000");
        var b = other.Create("B", "b", "#000000");
        s.Store.Rename(a.Id, "A2");
        var r = other.Load();
        Assert.AreEqual("A2", r.ProfileById(a.Id)?.DisplayName);
        Assert.IsNotNull(r.ProfileById(b.Id));
    }

    [TestMethod]
    public void ConcurrentChangesAreAllKept()
    {
        using var s = new Sandbox();
        s.Store.Load();
        Parallel.For(0, 20, i => s.NewStore().Create($"P{i}", $"p{i}", "#000000"));
        Assert.AreEqual(21, s.Store.Load().Profiles.Count);
    }

    [TestMethod]
    public void AReplaceWaitsForAReaderThatBlocksIt()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        // A reader that does not share delete access blocks the replace until it closes.
        var reader = new FileStream(s.Locator.NamesFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(async () => { await Task.Delay(300); reader.Dispose(); });
        s.Store.Rename(p.Id, "B");
        release.Wait();
        StringAssert.Contains(s.Text(s.Locator.NamesFile), "\tB\t");
    }

    [TestMethod]
    public void AReaderWithFullSharingNeverBlocksAReplace()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        using var reader = new FileStream(s.Locator.NamesFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var t = System.Diagnostics.Stopwatch.StartNew();
        s.Store.Rename(p.Id, "B");
        Assert.IsTrue(t.ElapsedMilliseconds < 1000, $"took {t.ElapsedMilliseconds} ms");
    }

    [TestMethod]
    public void BuiltinRowIsProtected()
    {
        using var s = new Sandbox();
        s.Store.Load();
        Assert.ThrowsExactly<ProfileException>(() => s.Store.RemoveRow("builtin"));
        Assert.ThrowsExactly<ProfileException>(() => s.Store.MarkDeleting("builtin"));
    }

    [TestMethod]
    public void StatesControlNames()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        StringAssert.Contains(s.Text(s.Locator.NamesFile), $"a\t{p.Id}");
        s.Store.MarkDeleting(p.Id);
        Assert.IsFalse(s.Text(s.Locator.NamesFile).Contains($"a\t{p.Id}"));
    }

    [TestMethod]
    public void MarkDeletingMovesTheDefaultFirst()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        s.Store.SetDefault(p.Id);
        s.Store.MarkDeleting(p.Id);
        Assert.AreEqual("builtin\n", s.Text(s.Locator.DefaultFile));
    }

    [TestMethod]
    public void DiscardOnlyWhilePending()
    {
        using var s = new Sandbox();
        var p = s.Store.Create("A", "a", "#000000");
        s.SignIn(p, "u");
        s.Store.CompleteLogin(p.Id, new Account("u", null, null));
        Assert.ThrowsExactly<ProfileException>(() => s.Store.Discard(p.Id));
        var q = s.Store.Create("B", "b", "#000000");
        s.Store.Discard(q.Id);
        Assert.IsNull(s.Store.Load().ProfileById(q.Id));
        Assert.IsFalse(Directory.Exists(q.ConfigDir));
    }

    [TestMethod]
    public void DuplicateAccountIsReported()
    {
        using var s = new Sandbox();
        var a = s.Store.Create("A", "a", "#000000");
        var b = s.Store.Create("B", "b", "#000000");
        var acc = new Account("same", null, null);
        Assert.IsNull(s.Store.CompleteLogin(a.Id, acc).DuplicateOf);
        Assert.AreEqual(a.Id, s.Store.CompleteLogin(b.Id, acc).DuplicateOf);
    }

    [TestMethod]
    public void AccountChangeIsNoticed()
    {
        using var s = new Sandbox();
        var a = s.Store.Create("A", "a", "#000000");
        s.Store.CompleteLogin(a.Id, new Account("1", "x@e.com", null));
        Assert.IsInstanceOfType<AccountChange.Unchanged>(s.Store.UpdateAccount(a.Id, new Account("1", "x@e.com", null)));
        Assert.AreEqual(new AccountChange.Changed("x@e.com", "y@e.com"), s.Store.UpdateAccount(a.Id, new Account("2", "y@e.com", null)));
    }
}
