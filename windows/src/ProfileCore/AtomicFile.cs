namespace ProfileCore;

public static class AtomicFile
{
    /// Writes a temporary file in the same folder, then moves it over the target, so a reader sees
    /// the old file or the new one, never half of one. The move uses POSIX rename semantics, so a
    /// reader with delete sharing, such as the wrapper, never blocks it. Windows still refuses while
    /// another program holds the file without delete sharing, so it retries for up to 2 seconds.
    public static void Write(string path, byte[] data)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(data);
                fs.Flush(flushToDisk: true);
            }
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (true)
            {
                if (WinProcess.ReplacePosix(tmp, path)) return;
                try
                {
                    File.Move(tmp, path, overwrite: true);
                    return;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    if (DateTime.UtcNow >= deadline)
                        throw new ProfileException($"Cannot replace {path}: another program has it open. Close it and try again.");
                    Thread.Sleep(50);
                }
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
        }
    }
}
