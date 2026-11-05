using System.Text.Json;

namespace ProfileCore;

public sealed record TranscriptSummary(string SessionId, string? Cwd, string? Title, string? Entrypoint, DateTime LastActivity);

/// Reader for projects\*\*.jsonl. Reads the ends of a file, because transcripts can be tens of MB.
/// Never uses a user message as a title, because it could show pasted secrets in the list.
public static class TranscriptReader
{
    public const int Window = 64 * 1024;
    public const int MaxWindow = 1024 * 1024;

    public static TranscriptSummary? Summary(string path)
    {
        var f = new FileInfo(path);
        return f.Exists ? Summary(path, f.Length, f.LastWriteTimeUtc) : null;
    }

    internal static TranscriptSummary? Summary(string path, long size, DateTime modified)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var fields = Read(fs, size, Window);
            if (fields.Title == null && size > 2L * Window) fields = Read(fs, size, MaxWindow);
            if (fields.Lines == 0) return null;
            return new TranscriptSummary(Path.GetFileNameWithoutExtension(path), fields.Cwd, fields.Title, fields.Entrypoint, modified);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    sealed class Fields
    {
        public int Lines;
        public string? Cwd, Entrypoint, Custom, Ai, SummaryText;
        public string? Title => Custom ?? Ai ?? SummaryText;
    }

    static readonly byte[] TitleMark = "-title\""u8.ToArray();
    static readonly byte[] SummaryMark = "\"summary\""u8.ToArray();

    static Fields Read(FileStream fs, long size, int window)
    {
        var f = new Fields();
        // Later lines overwrite earlier ones, so the newest title wins.
        foreach (var (offset, length, partialFirst) in Ranges(size, window))
        {
            var buf = new byte[length];
            fs.Position = offset;
            var got = 0;
            while (got < length)
            {
                var n = fs.Read(buf, got, length - got);
                if (n == 0) break;
                got += n;
            }
            var lines = Split(buf.AsMemory(0, got));
            if (partialFirst && lines.Count > 0) lines.RemoveAt(0);
            if (offset + length < size && lines.Count > 0) lines.RemoveAt(lines.Count - 1);
            foreach (var line in lines)
            {
                // Parsing every line is slow. Once cwd and entrypoint are known, only title lines matter.
                var span = line.Span;
                var mayMatter = f.Cwd == null || f.Entrypoint == null || span.IndexOf(TitleMark) >= 0 || span.IndexOf(SummaryMark) >= 0;
                if (!mayMatter) { f.Lines++; continue; }
                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); } catch (JsonException) { continue; }
                using (doc)
                {
                    var o = doc.RootElement;
                    if (o.ValueKind != JsonValueKind.Object) continue;
                    f.Lines++;
                    f.Cwd ??= S(o, "cwd");
                    f.Entrypoint ??= S(o, "entrypoint");
                    switch (S(o, "type"))
                    {
                        case "custom-title": if (S(o, "customTitle") is { } c) f.Custom = c; break;
                        case "ai-title": if (S(o, "aiTitle") is { } a) f.Ai = a; break;
                        case "summary": if (S(o, "summary") is { } s) f.SummaryText = s; break;
                    }
                }
            }
        }
        return f;
    }

    static List<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> data)
    {
        var list = new List<ReadOnlyMemory<byte>>();
        var start = 0;
        var span = data.Span;
        for (var i = 0; i <= span.Length; i++)
        {
            if (i < span.Length && span[i] != (byte)'\n') continue;
            var end = i > start && span[i - 1] == (byte)'\r' ? i - 1 : i;
            if (end > start) list.Add(data[start..end]);
            start = i + 1;
        }
        return list;
    }

    static IEnumerable<(long Offset, int Length, bool PartialFirst)> Ranges(long size, int window)
    {
        if (size <= 2L * window) return [(0, (int)size, false)];
        return [(0, window, false), (size - window, window, true)];
    }

    static string? S(JsonElement o, string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
