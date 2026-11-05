namespace ProfileCore;

public abstract record LoginState
{
    public sealed record Waiting : LoginState;
    public sealed record Detected(Account Account, string? DuplicateOf) : LoginState;
    public sealed record TimedOut : LoginState;
}

public sealed record KeepResult(Account Account, string? DuplicateOf);

/// Owns flow 3. Nothing completes a login except Keep. On Windows the login is a file in the
/// profile's folder, so no Credential Manager baseline is taken (design flow 3).
public sealed class LoginCoordinator(ProfileStore store, ClaudeCLI? cli = null, IProcessInfo? processes = null)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    readonly ClaudeCLI cli = cli ?? new ClaudeCLI(store.Locator);
    public Func<DateTime> Now { get; init; } = () => DateTime.UtcNow;

    ConfigLocator L => store.Locator;

    /// Returns the command to sign in with.
    public string Begin(Profile p)
    {
        store.SetLoginStarted(p.Id, Now());
        return $"claude-as {p.CommandName} auth login";
    }

    /// Reads only.
    public LoginState Poll(Profile p)
    {
        var account = SignIn.Account(L, store.Credentials, p);
        if (account != null)
        {
            var dup = store.Load().Profiles.FirstOrDefault(o => o.Id != p.Id && o.AccountUuid == account.AccountUuid)?.Id;
            return new LoginState.Detected(account, dup);
        }
        if (p.LoginStartedAt is { } started && Now() - started > Timeout) return new LoginState.TimedOut();
        return new LoginState.Waiting();
    }

    public KeepResult Keep(Profile p)
    {
        if (p.State != ProfileState.PendingLogin) throw new ProfileException($"{p.DisplayName} is not waiting for a login.");
        var account = SignIn.Account(L, store.Credentials, p)
            ?? throw new ProfileException($"{p.DisplayName} has not signed in yet. Run: claude-as {p.CommandName} auth login");
        var result = store.CompleteLogin(p.Id, account);
        new EventLog(L).Write("login", p.Id, "login kept");
        return new KeepResult(account, result.DuplicateOf);
    }

    /// Signs out first, so the next sign-in starts clean.
    public string Retry(Profile p)
    {
        SignOutIfSignedIn(p);
        return Begin(p);
    }

    /// Also signs out, so no login is left behind.
    public void Discard(Profile p)
    {
        if (new LiveSessionReader(L, processes).HasLiveSession(p))
            throw new ProfileException($"{p.DisplayName} has a live session. Stop it first.");
        SignOutIfSignedIn(p);
        store.Discard(p.Id);
    }

    void SignOutIfSignedIn(Profile p)
    {
        if (AccountReader.ReadAccount(L.ConfigFile(p)) == null && !store.Credentials.HasLogin(L, p)) return;
        var r = cli.Logout(p);
        if (r.ExitCode != 0) throw new ProfileException($"Could not sign out: {(r.Stderr.Trim().Length > 0 ? r.Stderr.Trim() : "exit code " + r.ExitCode)}");
    }
}
