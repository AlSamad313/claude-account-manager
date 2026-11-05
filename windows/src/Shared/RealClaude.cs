// Compiled into both ProfileCore and the wrapper: the rule for finding the real claude.exe.
using System.Runtime.InteropServices;

namespace ProfileCore.Shared;

internal sealed record RealClaudeResult(string? Exe, string? ScriptOnly);

internal static class RealClaude
{
    static readonly string[] Scripts = ["claude.cmd", "claude.bat", "claude.ps1"];

    /// The first claude.exe on PATH, skipping any entry that is the bin folder however it is
    /// spelled. A claude.cmd, .bat or .ps1 is never run; ScriptOnly names one if that is all there is.
    public static RealClaudeResult Find(string path, string binDir)
    {
        var own = PathCanon.Canonical(binDir);
        string? script = null;
        foreach (var raw in path.Split(';'))
        {
            var entry = Clean(raw);
            if (entry == null) continue;
            if (PathCanon.Same(PathCanon.Canonical(entry), own)) continue;
            var exe = Path.Combine(entry, "claude.exe");
            if (File.Exists(exe)) return new RealClaudeResult(exe, null);
            if (script == null)
                foreach (var s in Scripts)
                {
                    var c = Path.Combine(entry, s);
                    if (File.Exists(c)) { script = c; break; }
                }
        }
        return new RealClaudeResult(null, script);
    }

    /// A PATH entry with quotes and variables removed, or null if it is empty or not a valid path.
    public static string? Clean(string raw)
    {
        var e = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
        if (e.Length == 0) return null;
        try { return Path.GetFullPath(e); }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (PathTooLongException) { return null; }
    }
}

internal static class PathCanon
{
    /// The long, final form of an existing path: links followed, no 8.3 names, no \\?\ prefix,
    /// drive letter in upper case, no trailing backslash. A missing path gets the text rules only.
    public static string Canonical(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); } catch (Exception) { full = path; }
        return Tidy(FinalPath(full) ?? full);
    }

    public static bool Same(string a, string b) => string.Equals(Tidy(a), Tidy(b), StringComparison.OrdinalIgnoreCase);

    /// True if child is inside parent, not equal to it.
    public static bool IsInside(string child, string parent)
    {
        var c = Tidy(child);
        var p = Tidy(parent);
        return c.Length > p.Length + 1 && c.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public static string Tidy(string p)
    {
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + p[8..];
        else if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p[4..];
        p = p.Replace('/', '\\');
        if (p.Length > 3) p = p.TrimEnd('\\');
        if (p.Length >= 2 && p[1] == ':' && char.IsAsciiLetterLower(p[0])) p = char.ToUpperInvariant(p[0]) + p[1..];
        return p;
    }

    const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    const uint OPEN_EXISTING = 3;
    const uint SHARE_ALL = 7;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandleW(IntPtr h, [Out] char[] buffer, uint size, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);

    static string? FinalPath(string path)
    {
        var h = CreateFileW(path, 0, SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (h == new IntPtr(-1)) return null;
        try
        {
            var buf = new char[1024];
            var n = GetFinalPathNameByHandleW(h, buf, (uint)buf.Length, 0);
            if (n == 0) return null;
            if (n > buf.Length)
            {
                buf = new char[n];
                n = GetFinalPathNameByHandleW(h, buf, (uint)buf.Length, 0);
                if (n == 0 || n > buf.Length) return null;
            }
            return new string(buf, 0, (int)n);
        }
        finally { CloseHandle(h); }
    }
}
