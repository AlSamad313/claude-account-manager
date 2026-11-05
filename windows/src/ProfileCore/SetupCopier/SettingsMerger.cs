using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProfileCore;

/// Reading and writing Claude Code's JSON files as plain trees, so unknown keys survive. Written
/// by hand over Utf8JsonWriter, because cpm is compiled ahead of time.
public static class JsonTree
{
    /// The top-level object of a file, or an empty one if it is missing or not an object.
    public static JsonObject Read(string path)
    {
        try { return JsonNode.Parse(File.ReadAllBytes(path)) as JsonObject ?? []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    public static string? Str(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    public static IEnumerable<string> Strings(JsonNode? n) => (n as JsonArray)?.Select(Str).OfType<string>() ?? [];

    /// Every string in a tree, keys excluded. Used to look for paths and secrets.
    public static IEnumerable<string> AllStrings(JsonNode? n)
    {
        switch (n)
        {
            case JsonObject o: foreach (var (_, v) in o) foreach (var s in AllStrings(v)) yield return s; break;
            case JsonArray a: foreach (var v in a) foreach (var s in AllStrings(v)) yield return s; break;
            default: if (Str(n) is { } x) yield return x; break;
        }
    }

    /// Keys sorted at every level, and slashes and non-ASCII left as they are, so equal trees give
    /// equal text, and a path can be searched for in the result.
    public static string Canonical(JsonNode? n) => Encoding.UTF8.GetString(Bytes(n, indented: false));

    /// As Swift's .prettyPrinted and .sortedKeys write it.
    public static byte[] Pretty(JsonNode n) => Bytes(n, indented: true);

    static byte[] Bytes(JsonNode? n, bool indented)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            Write(w, n);
        return ms.ToArray();
    }

    static void Write(Utf8JsonWriter w, JsonNode? n)
    {
        switch (n)
        {
            case null: w.WriteNullValue(); break;
            case JsonObject o:
                w.WriteStartObject();
                foreach (var (k, v) in o.OrderBy(p => p.Key, StringComparer.Ordinal)) { w.WritePropertyName(k); Write(w, v); }
                w.WriteEndObject();
                break;
            case JsonArray a:
                w.WriteStartArray();
                foreach (var v in a) Write(w, v);
                w.WriteEndArray();
                break;
            default: n.WriteTo(w); break;
        }
    }
}

/// Merges into a settings.json. Unknown keys are kept. statusLine and apiKeyHelper are never
/// touched, because no item names them. The same function gives the preview and the written result.
public static class SettingsMerger
{
    public static List<string> NewPermissions(JsonObject source, JsonObject target, string key)
    {
        var have = JsonTree.Strings((target["permissions"] as JsonObject)?[key]).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return JsonTree.Strings((source["permissions"] as JsonObject)?[key]).Where(r => !have.Contains(r) && seen.Add(r)).ToList();
    }

    public static void AddPermissions(IEnumerable<string> rules, string key, JsonObject target)
    {
        if (target["permissions"] is not JsonObject perms) target["permissions"] = perms = [];
        if (perms[key] is not JsonArray list) perms[key] = list = [];
        var have = JsonTree.Strings(list).ToHashSet(StringComparer.Ordinal);
        foreach (var r in rules) if (have.Add(r)) list.Add((JsonNode)JsonValue.Create(r));
    }

    public sealed record HookEntry(string Event, string Matcher, JsonObject Entry);

    /// One entry of a hooks event, with its matcher, in a fixed order.
    public static List<HookEntry> HookEntries(JsonObject settings)
    {
        if (settings["hooks"] is not JsonObject hooks) return [];
        var out_ = new List<HookEntry>();
        foreach (var (ev, value) in hooks)
            foreach (var e in (value as JsonArray)?.OfType<JsonObject>() ?? [])
                out_.Add(new(ev, JsonTree.Str(e["matcher"]) ?? "", e));
        return out_.OrderBy(h => h.Event, StringComparer.Ordinal).ThenBy(h => h.Matcher, StringComparer.Ordinal).ToList();
    }

    /// The hooks in an entry that the target does not already have under this event and matcher.
    public static List<JsonObject> NewHooks(HookEntry h, JsonObject target)
    {
        var have = HookEntries(target).Where(t => t.Event == h.Event && t.Matcher == h.Matcher)
            .SelectMany(t => (t.Entry["hooks"] as JsonArray)?.OfType<JsonObject>() ?? []).Select(JsonTree.Canonical).ToHashSet(StringComparer.Ordinal);
        return ((h.Entry["hooks"] as JsonArray)?.OfType<JsonObject>() ?? []).Where(x => !have.Contains(JsonTree.Canonical(x))).ToList();
    }

    public static void AddHooks(List<JsonObject> hooksToAdd, string ev, string matcher, JsonObject target)
    {
        if (hooksToAdd.Count == 0) return;
        if (target["hooks"] is not JsonObject hooks) target["hooks"] = hooks = [];
        if (hooks[ev] is not JsonArray entries) hooks[ev] = entries = [];
        var entry = entries.OfType<JsonObject>().FirstOrDefault(e => (JsonTree.Str(e["matcher"]) ?? "") == matcher);
        if (entry == null)
        {
            entry = [];
            if (matcher.Length > 0) entry["matcher"] = matcher;
            entries.Add((JsonNode)entry);
        }
        if (entry["hooks"] is not JsonArray list) entry["hooks"] = list = [];
        foreach (var h in hooksToAdd) list.Add((JsonNode)h.DeepClone());
    }
}

/// A settings.json the copier may write: missing, or with the known keys in the shapes it expects.
public static class CompatCheck
{
    public static bool CanWriteSettings(string file)
    {
        if (!File.Exists(file)) return true;
        JsonObject top;
        try { if (JsonNode.Parse(File.ReadAllBytes(file)) is not JsonObject o) return false; top = o; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return false; }
        if (top["permissions"] is { } p)
        {
            if (p is not JsonObject perms) return false;
            foreach (var key in new[] { "allow", "deny" })
                if (perms[key] is { } list && (list is not JsonArray a || a.Any(x => JsonTree.Str(x) == null))) return false;
        }
        foreach (var key in new[] { "hooks", "enabledPlugins", "extraKnownMarketplaces" })
            if (top[key] is { } v && v is not JsonObject) return false;
        if (top["model"] is { } m && JsonTree.Str(m) == null) return false;
        if (top["env"] is { } env && (env is not JsonObject eo || eo.Any(x => JsonTree.Str(x.Value) == null))) return false;
        return true;
    }
}
