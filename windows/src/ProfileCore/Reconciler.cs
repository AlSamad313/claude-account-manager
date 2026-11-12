namespace ProfileCore;

public sealed class ReconcileResult
{
    public List<string> OrphanFolders { get; } = [];
    public List<string> RowsWithoutFolder { get; } = [];
    public List<string> LoginNotices { get; } = [];
    public List<string> AccountNotices { get; } = [];
    public List<string> RemovalsResumed { get; } = [];
    public List<string> RemovalWarnings { get; } = [];
}

/// Runs when the app or cpm starts. It never completes a login.
public sealed class Reconciler(ProfileStore store, LoginCoordinator login, ProfileRemover? remover = null)
{
    public ReconcileResult Run()
    {
        var result = new ReconcileResult();
        store.RepairFiles();
        // A removal that still fails (say, a file held open) stays `deleting` and is tried again next time.
        if (remover != null)
            try { foreach (var r in remover.ResumePending()) { result.RemovalsResumed.Add(r.DisplayName); result.RemovalWarnings.AddRange(r.Warnings); } }
            catch (ProfileException e) { result.RemovalWarnings.Add(e.Message); }
        var registry = store.Load();
        var l = store.Locator;

        var known = registry.Profiles.Where(p => p.ConfigDir != null).Select(p => Path.GetFileName(p.ConfigDir!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(l.ProfilesDir))
            result.OrphanFolders.AddRange(Directory.EnumerateDirectories(l.ProfilesDir).Select(Path.GetFileName)
                .Where(n => n != null && !n.StartsWith('.') && !known.Contains(n)).Select(n => n!).Order());

        foreach (var p in registry.Profiles)
        {
            if (!p.Builtin && !Directory.Exists(l.ConfigDir(p)))
            {
                if (p.State is not (ProfileState.Error or ProfileState.Deleting)) store.MarkError(p.Id, "Its folder is missing.");
                result.RowsWithoutFolder.Add(p.DisplayName);
                continue;
            }
            var account = SignIn.Account(l, store.Credentials, p);
            switch (p.State, account)
            {
                case (ProfileState.PendingLogin, not null):
                    if (login.Poll(p) is LoginState.Detected d)
                        result.LoginNotices.Add($"{p.DisplayName} signed in as {d.Account.Email ?? "an account"}. Run: cpm keep {p.CommandName}, cpm retry {p.CommandName} or cpm discard {p.CommandName}");
                    break;
                case (ProfileState.Ready, null):
                    store.MarkLoggedOut(p.Id);
                    break;
                case (ProfileState.LoggedOut, not null):
                case (ProfileState.Error, not null): // its folder came back
                    store.MarkReady(p.Id, account);
                    break;
                case (ProfileState.Error, null):
                    store.MarkLoggedOut(p.Id);
                    break;
                case (ProfileState.Ready, not null):
                    if (store.UpdateAccount(p.Id, account) is AccountChange.Changed c)
                        result.AccountNotices.Add($"{p.DisplayName} changed account, from {c.From ?? "unknown"} to {c.To ?? "unknown"}.");
                    break;
            }
        }
        return result;
    }
}
