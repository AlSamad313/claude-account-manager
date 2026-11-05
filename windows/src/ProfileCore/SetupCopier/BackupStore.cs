using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfileCore;

public sealed class BackupEntry
{
    public string Path { get; set; } = "";
    public bool ExistedBefore { get; set; }
    /// Stands in for the macOS file mode: put back on a restored file.
    public bool ReadOnly { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public string? BackupFile { get; set; }
}

public sealed class BackupManifest
{
    [JsonConverter(typeof(IsoDate))] public DateTime CreatedAt { get; set; }
    public string ProfileId { get; set; } = "";
    public List<BackupEntry> Entries { get; set; } = [];
    public List<string> RemovalCommands { get; set; } = [];
}

/// .claude-profiles\backups\<id>\<time>\, inside the user's profile folder, so only the user can
/// read it. Files that Claude Code's own commands change are not listed, and Restore cannot undo them.
public sealed class BackupStore(ConfigLocator locator)
{
    public const int Keep = 10;

    public string Folder(string profileId) => System.IO.Path.Combine(locator.ProfilesRoot, "backups", profileId);

    public static string? Hash(string path)
    {
        try { return File.Exists(path) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// Copies each file that exists now, and records its state.
    public (string Id, BackupManifest Manifest) Begin(string profileId, IReadOnlyList<string> paths)
    {
        var root = Folder(profileId);
        Directory.CreateDirectory(root);
        // Named by the time, so names sort in order. One copy runs at a time, so a taken name just
        // moves on a millisecond.
        var stamp = DateTime.UtcNow;
        string id, dir;
        while (true)
        {
            id = stamp.ToString("yyyy-MM-dd'T'HH-mm-ss.fff'Z'", CultureInfo.InvariantCulture);
            dir = System.IO.Path.Combine(root, id);
            if (!Directory.Exists(dir)) break;
            stamp = stamp.AddMilliseconds(1);
        }
        Directory.CreateDirectory(dir);
        var m = new BackupManifest { CreatedAt = DateTime.UtcNow, ProfileId = profileId };
        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            if (!File.Exists(path))
            {
                m.Entries.Add(new BackupEntry { Path = path });
                continue;
            }
            var name = $"file-{i}";
            AtomicFile.Write(System.IO.Path.Combine(dir, name), File.ReadAllBytes(path));
            m.Entries.Add(new BackupEntry
            {
                Path = path, ExistedBefore = true, Before = Hash(path), BackupFile = name,
                ReadOnly = File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly),
            });
        }
        return (id, m);
    }

    public void Save(BackupManifest m, string id)
    {
        AtomicFile.Write(System.IO.Path.Combine(Folder(m.ProfileId), id, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(m, CoreJson.Default.BackupManifest));
        Trim(m.ProfileId);
    }

    public BackupManifest Load(string profileId, string id)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllBytes(System.IO.Path.Combine(Folder(profileId), id, "manifest.json")), CoreJson.Default.BackupManifest)
                ?? throw new ProfileException("That backup is empty.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ProfileException($"Cannot read that backup: {e.Message}");
        }
    }

    public string? BackupFile(BackupEntry e, string profileId, string id) =>
        e.BackupFile is { } f ? System.IO.Path.Combine(Folder(profileId), id, f) : null;

    /// Newest first.
    public List<BackupInfo> List(string profileId)
    {
        var list = new List<BackupInfo>();
        foreach (var id in Names(profileId))
        {
            try
            {
                var m = Load(profileId, id);
                list.Add(new BackupInfo(id, profileId, m.CreatedAt, m.RemovalCommands));
            }
            catch (ProfileException) { }
        }
        return list;
    }

    IEnumerable<string> Names(string profileId)
    {
        var root = Folder(profileId);
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root).Select(System.IO.Path.GetFileName).OfType<string>().OrderByDescending(n => n, StringComparer.Ordinal);
    }

    void Trim(string profileId)
    {
        foreach (var old in Names(profileId).Skip(Keep).ToList())
        {
            try { Directory.Delete(System.IO.Path.Combine(Folder(profileId), old), recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
