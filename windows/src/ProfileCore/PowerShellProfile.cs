using System.Text;

namespace ProfileCore;

/// Design flow 10 step 4: one marked line in the user's PowerShell profiles that loads tab
/// completion. Only lines ending with the marker are ever added or removed; nothing else in the
/// file changes, and its encoding is kept.
public sealed class PowerShellProfile(ConfigLocator locator, string? documentsDir = null)
{
    public const string Marker = "# claude-profiles";
    public const string Line = ". \"$env:USERPROFILE\\.claude-profiles\\bin\\claude-profiles.ps1\"   " + Marker;

    readonly string docs = documentsDir ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// Windows PowerShell 5 and PowerShell 7 each have their own profile for the console host.
    public IEnumerable<string> Files =>
        new[] { "WindowsPowerShell", "PowerShell" }.Select(d => Path.Combine(docs, d, "Microsoft.PowerShell_profile.ps1"));

    /// A profile is changed if it exists or its folder does, so a PowerShell that is not installed
    /// gets nothing. Windows PowerShell's folder may not exist yet on a fresh account, so that one is
    /// created if neither exists. Returns the files changed.
    public List<string> Add()
    {
        var changed = new List<string>();
        var targets = Files.Where(f => File.Exists(f) || Directory.Exists(Path.GetDirectoryName(f))).ToList();
        if (targets.Count == 0) targets.Add(Files.First());
        foreach (var f in targets)
        {
            var (text, enc) = Read(f);
            if (HasLine(text)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            var sep = text.Contains("\r\n") || text.Length == 0 ? "\r\n" : "\n";
            Write(f, text + sep + Line + sep, enc);
            new EventLog(locator).Write("completion", null, $"added the completion line to {f}");
            changed.Add(f);
        }
        return changed;
    }

    /// Takes out only the marked line, and the blank line Add put before it.
    public List<string> Remove()
    {
        var changed = new List<string>();
        foreach (var f in Files.Where(File.Exists))
        {
            var (text, enc) = Read(f);
            if (!HasLine(text)) continue;
            var lines = text.Split('\n').ToList();
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                if (!lines[i].TrimEnd('\r').TrimEnd().EndsWith(Marker, StringComparison.Ordinal)) continue;
                lines.RemoveAt(i);
                if (i > 0 && lines[i - 1].TrimEnd('\r').Length == 0) lines.RemoveAt(i - 1);
            }
            Write(f, string.Join('\n', lines), enc);
            new EventLog(locator).Write("completion", null, $"removed the completion line from {f}");
            changed.Add(f);
        }
        return changed;
    }

    public bool IsAdded() => Files.Where(File.Exists).Any(f => HasLine(Read(f).Text));

    static bool HasLine(string text) =>
        text.Split('\n').Any(l => l.TrimEnd('\r').TrimEnd().EndsWith(Marker, StringComparison.Ordinal));

    static (string Text, Encoding Enc) Read(string f)
    {
        if (!File.Exists(f)) return ("", new UTF8Encoding(true));
        var bytes = File.ReadAllBytes(f);
        Encoding enc = bytes is [0xEF, 0xBB, 0xBF, ..] ? new UTF8Encoding(true)
            : bytes is [0xFF, 0xFE, ..] ? Encoding.Unicode
            : bytes is [0xFE, 0xFF, ..] ? Encoding.BigEndianUnicode
            : new UTF8Encoding(false);
        using var r = new StreamReader(new MemoryStream(bytes), enc, detectEncodingFromByteOrderMarks: true);
        return (r.ReadToEnd(), enc);
    }

    /// A new file gets a BOM, so Windows PowerShell 5 reads the non-ASCII path in it correctly.
    static void Write(string f, string text, Encoding enc)
    {
        var preamble = enc.GetPreamble();
        var body = enc.GetBytes(text);
        AtomicFile.Write(f, [.. preamble, .. body]);
    }
}
