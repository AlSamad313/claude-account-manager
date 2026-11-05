using System.Text.Json;

namespace ProfileCore;

public static class AccountReader
{
    /// Reads only oauthAccount's three keys. Tokens and project data are never kept.
    public static Account? ReadAccount(string configFile)
    {
        try
        {
            using var fs = new FileStream(configFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("oauthAccount", out var a) || a.ValueKind != JsonValueKind.Object) return null;
            var uuid = Str(a, "accountUuid");
            return uuid == null ? null : new Account(uuid, Str(a, "emailAddress"), Str(a, "organizationName"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    static string? Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
