namespace ProfileCore;

public static class RecycleBin
{
    /// Moves a folder to the Recycle Bin. Refuses, rather than deleting outright, when its drive has
    /// none. The login file is deleted first, so a login never sits in the Recycle Bin.
    public static void MoveFolder(string dir)
    {
        if (!Directory.Exists(dir)) return;
        if (!WinProcess.HasRecycleBin(dir))
            throw new ProfileException($"{dir} is on a drive with no Recycle Bin, so it was not removed. Delete it yourself if you want it gone.");
        var cred = Path.Combine(dir, ".credentials.json");
        if (File.Exists(cred)) File.Delete(cred);
        var r = WinProcess.MoveToRecycleBin(dir);
        if (r != 0 || Directory.Exists(dir))
            throw new ProfileException($"Could not move {dir} to the Recycle Bin (code {r}). A program may have a file in it open.");
    }
}
