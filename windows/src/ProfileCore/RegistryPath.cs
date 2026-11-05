using Microsoft.Win32;

namespace ProfileCore;

/// The PATH a new process gets: the system PATH, then the user PATH, from the registry. A running
/// process's own PATH may be older, so doctor and the app use this.
public interface IPathSource
{
    string SystemPath { get; }
    string UserPath { get; }
}

public sealed class RegistryPath : IPathSource
{
    const string SystemKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    public string SystemPath => Read(Microsoft.Win32.Registry.LocalMachine, SystemKey);
    public string UserPath => Read(Microsoft.Win32.Registry.CurrentUser, "Environment");

    static string Read(RegistryKey hive, string key)
    {
        using var k = hive.OpenSubKey(key);
        return Environment.ExpandEnvironmentVariables(k?.GetValue("Path") as string ?? "");
    }

    public static string Combined(IPathSource s) => string.Join(';', new[] { s.SystemPath, s.UserPath }.Where(p => p.Length > 0));
}
