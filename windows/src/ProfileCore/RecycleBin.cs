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

    /// A file or a folder. A folder goes as MoveFolder does.
    public static void Move(string path)
    {
        if (Directory.Exists(path)) { MoveFolder(path); return; }
        if (!File.Exists(path)) return;
        if (!WinProcess.HasRecycleBin(path))
            throw new ProfileException($"{path} is on a drive with no Recycle Bin, so it was not removed. Delete it yourself if you want it gone.");
        var r = WinProcess.MoveToRecycleBin(path);
        if (r != 0 || File.Exists(path))
            throw new ProfileException($"Could not move {path} to the Recycle Bin (code {r}). A program may have it open.");
    }
}
