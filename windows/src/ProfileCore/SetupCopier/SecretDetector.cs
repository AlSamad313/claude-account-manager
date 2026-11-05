using System.Text;
using System.Text.RegularExpressions;

namespace ProfileCore;

/// Fixed value patterns. Every value in env and headers is masked regardless; these are for
/// positional args and free text, where there is no key to go by. The same patterns as macOS.
public static partial class SecretDetector
{
    public const string Mask = "••••••";

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_-]{20,}|ghp_[A-Za-z0-9]{20,}|xox[bap]-[A-Za-z0-9-]+|\b[0-9a-fA-F]{40,}\b")]
    private static partial Regex Tokens();

    /// No slash, because long file paths would match. Needs upper, lower and a digit, because long words and names do not.
    [GeneratedRegex(@"[A-Za-z0-9+]{40,}={0,2}")]
    private static partial Regex Base64();

    public static bool LooksSecret(string s)
    {
        if (Tokens().IsMatch(s)) return true;
        foreach (Match m in Base64().Matches(s))
        {
            var v = m.Value;
            if (v.Any(char.IsAsciiLetterUpper) && v.Any(char.IsAsciiLetterLower) && v.Any(char.IsAsciiDigit)) return true;
        }
        return false;
    }

    /// For a file of free text: a match flags the whole file, with no masking inside it.
    /// Files over 1 MB, and files that are not UTF-8 text, are not scanned.
    public static bool FileLooksSecret(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 1_000_000) return false;
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(File.ReadAllBytes(path));
            return LooksSecret(text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException) { return false; }
    }
}
