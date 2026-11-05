using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProfileCore;

[JsonConverter(typeof(JsonStringEnumConverter<ProfileState>))]
public enum ProfileState
{
    [JsonStringEnumMemberName("pendingLogin")] PendingLogin,
    [JsonStringEnumMemberName("ready")] Ready,
    [JsonStringEnumMemberName("loggedOut")] LoggedOut,
    [JsonStringEnumMemberName("deleting")] Deleting,
    [JsonStringEnumMemberName("error")] Error,
}

/// A row of profiles.json. The same schema as macOS. Properties are in key order, so the file's
/// keys are sorted as the Swift encoder writes them.
public sealed class Profile
{
    public const string BuiltinId = "builtin";

    public string? AccountUuid { get; set; }
    public bool Builtin { get; set; }
    public string Color { get; set; } = "";             // "#RRGGBB"
    public string CommandName { get; set; } = "";       // fixed at creation
    public string? ConfigDir { get; set; }              // set once at creation, never rebuilt; null for built-in
    [JsonConverter(typeof(IsoDate))] public DateTime CreatedAt { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public string Id { get; set; } = "";
    public List<string>? KeychainBaseline { get; set; } // always null on Windows
    public string? KeychainService { get; set; }        // always null on Windows
    public string? LastError { get; set; }
    [JsonConverter(typeof(IsoDateOrNull))] public DateTime? LoginStartedAt { get; set; } // only while waiting
    public string? OrganizationName { get; set; }
    public ProfileState State { get; set; }

    /// names lists only profiles the wrapper may start in.
    [JsonIgnore] public bool IsInNames => State is ProfileState.PendingLogin or ProfileState.Ready or ProfileState.LoggedOut;
}

public sealed class Registry
{
    public string DefaultProfileId { get; set; } = Profile.BuiltinId;
    public List<Profile> Profiles { get; set; } = [];
    public int SchemaVersion { get; set; } = 1;

    public Profile? ProfileById(string id) => Profiles.FirstOrDefault(p => p.Id == id);
    public Profile? ProfileByCommand(string name) => Profiles.FirstOrDefault(p => p.CommandName == name);
}

public sealed record Account(string AccountUuid, string? Email, string? OrganizationName);

public class ProfileException(string message) : Exception(message)
{
    public static ProfileException InvalidCommandName(string n) =>
        new($"'{n}' is not a valid command name. Use 1 to 24 lowercase letters, digits or hyphens, starting with a letter or digit.");
    public static ProfileException InvalidDisplayName(string n) =>
        new($"'{n}' is not a valid display name. Use 1 to 40 characters with no tabs, newlines or control characters.");
    public static ProfileException InvalidColor(string c) => new($"'{c}' is not a valid colour. Use #RRGGBB.");
    public static ProfileException CommandNameTaken(string n) => new($"A profile already uses the command name '{n}'.");
    public static ProfileException NotFound(string n) => new($"No profile named '{n}'.");
}

public static partial class Validate
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,23}$")] private static partial Regex CommandNameRule();
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")] private static partial Regex ColorRule();

    public static void CommandName(string s)
    {
        if (!CommandNameRule().IsMatch(s) || s == Profile.BuiltinId) throw ProfileException.InvalidCommandName(s);
    }

    public static void DisplayName(string s)
    {
        var bad = s.Any(c => char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format);
        if (s.Length == 0 || new StringInfo(s).LengthInTextElements > 40 || bad) throw ProfileException.InvalidDisplayName(s);
    }

    public static void Color(string s)
    {
        if (!ColorRule().IsMatch(s)) throw ProfileException.InvalidColor(s);
    }
}
