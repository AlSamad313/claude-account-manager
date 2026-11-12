using System.Text;

namespace ProfileCore;

/// writes.log, and rotation of both logs. A logging failure never blocks the action.
public sealed class EventLog(ConfigLocator locator)
{
    public long MaxBytes { get; init; } = 1_000_000;

    public void Write(string kind, string? profileId, string detail, string? backupPath = null)
    {
        var fields = new[] { DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), kind, profileId ?? "-", detail, backupPath ?? "-" };
        var line = string.Join('\t', fields.Select(Clean)) + "\n";
        try
        {
            Directory.CreateDirectory(locator.LogsDir);
            File.AppendAllText(Path.Combine(locator.LogsDir, "writes.log"), line, new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static string Clean(string s) => s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    /// Checked when the app or cpm starts. Keeps 3 old files.
    public void Rotate()
    {
        foreach (var name in new[] { "writes.log", "launch.log" })
        {
            var path = Path.Combine(locator.LogsDir, name);
            try
            {
                var f = new FileInfo(path);
                if (!f.Exists || f.Length < MaxBytes) continue;
                File.Delete(path + ".3");
                foreach (var i in new[] { 2, 1 })
                    if (File.Exists($"{path}.{i}")) File.Move($"{path}.{i}", $"{path}.{i + 1}", overwrite: true);
                File.Move(path, path + ".1", overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
