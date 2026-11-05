// Compiled into both ProfileCore, which writes launch requests, and the wrapper, which reads them
// (design flow 2). Data only: nothing in it is ever run or passed through a shell.
using System.Globalization;
using System.Text.Json;

namespace ProfileCore.Shared;

internal sealed record LaunchRequest(string CommandName, string Folder, IReadOnlyList<string> Arguments, DateTime CreatedAt)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);
    public const string Extension = ".launch";

    public byte[] ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("commandName", CommandName);
            w.WriteString("folder", Folder);
            w.WriteStartArray("arguments");
            foreach (var a in Arguments) w.WriteStringValue(a);
            w.WriteEndArray();
            w.WriteString("createdAt", CreatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// Null unless every field is present with the right type.
    public static LaunchRequest? Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            var r = new Utf8JsonReader(json);
            using var doc = JsonDocument.ParseValue(ref r);
            var o = doc.RootElement;
            if (o.ValueKind != JsonValueKind.Object) return null;
            if (!o.TryGetProperty("commandName", out var c) || c.ValueKind != JsonValueKind.String) return null;
            if (!o.TryGetProperty("folder", out var f) || f.ValueKind != JsonValueKind.String) return null;
            if (!o.TryGetProperty("arguments", out var a) || a.ValueKind != JsonValueKind.Array) return null;
            if (!o.TryGetProperty("createdAt", out var t) || t.ValueKind != JsonValueKind.String) return null;
            var args = new List<string>();
            foreach (var x in a.EnumerateArray())
            {
                if (x.ValueKind != JsonValueKind.String) return null;
                args.Add(x.GetString()!);
            }
            if (!DateTime.TryParse(t.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var created)) return null;
            return new LaunchRequest(c.GetString()!, f.GetString()!, args, created);
        }
        catch (JsonException) { return null; }
    }
}
