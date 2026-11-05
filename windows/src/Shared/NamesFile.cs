// Compiled into both ProfileCore and the wrapper, so both read names and default the same way.
// Plain text only: the wrapper never parses JSON.
using System.Text;

namespace ProfileCore.Shared;

/// One line of the names file: command name, ID, display name, colour, configDir ("-" for built-in).
internal sealed record NameEntry(string CommandName, string Id, string DisplayName, string Color, string ConfigDir)
{
    public bool IsBuiltin => ConfigDir == "-";
}

internal static class NamesFile
{
    /// Opened with full sharing, so a reader never blocks the store's replace.
    public static string? ReadText(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
            return r.ReadToEnd();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// Null when the file cannot be read. Lines without five fields are skipped.
    public static List<NameEntry>? Read(string path)
    {
        var text = ReadText(path);
        if (text == null) return null;
        var list = new List<NameEntry>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var f = line.Split('\t');
            if (f.Length < 5 || f[0].Length == 0 || f[1].Length == 0) continue;
            list.Add(new NameEntry(f[0], f[1], f[2], f[3], f[4]));
        }
        return list;
    }

    /// The first line, trimmed; null if missing or empty.
    public static string? ReadDefault(string path)
    {
        var text = ReadText(path);
        if (text == null) return null;
        var first = text.Split('\n')[0].Trim();
        return first.Length == 0 ? null : first;
    }
}
