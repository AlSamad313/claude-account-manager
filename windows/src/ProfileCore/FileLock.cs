namespace ProfileCore;

/// The app and cpm are separate processes, so each change holds .lock. It is held by opening the file
/// with no sharing, retried every 50 ms for up to 10 seconds.
public static class FileLock
{
    public static IDisposable Acquire(string path, TimeSpan? timeout = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            catch (IOException)
            {
                throw new ProfileException($"Cannot lock {path}. Another Claude Profiles program may be stuck; try again.");
            }
        }
    }
}
