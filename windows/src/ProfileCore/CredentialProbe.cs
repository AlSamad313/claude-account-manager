namespace ProfileCore;

public interface ICredentialProbe
{
    /// Whether the profile's .credentials.json exists. It is never opened.
    bool HasLogin(ConfigLocator locator, Profile p);

    /// Credential Manager names starting with "Claude Code", for doctor only: on this Claude Code the
    /// login is a file, so any name here means the login storage changed (S0 check C1).
    IReadOnlySet<string> CredentialManagerNames();
}

public sealed class CredentialProbe : ICredentialProbe
{
    public const string Prefix = "Claude Code";

    public bool HasLogin(ConfigLocator locator, Profile p) => File.Exists(locator.CredentialsFile(p));

    public IReadOnlySet<string> CredentialManagerNames() => WinProcess.CredentialNames(Prefix);
}

public static class SignIn
{
    /// Signed in means both: the account in .claude.json, and the login file.
    public static Account? Account(ConfigLocator locator, ICredentialProbe probe, Profile p)
    {
        var a = AccountReader.ReadAccount(locator.ConfigFile(p));
        return a != null && probe.HasLogin(locator, p) ? a : null;
    }
}
