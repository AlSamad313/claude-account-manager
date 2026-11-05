using ProfileCore.Shared;

namespace ProfileCore;

/// The same rule as the wrapper (Shared/RealClaude.cs): the first claude.exe on PATH, skipping the
/// bin folder. cpm uses its own PATH; the app passes the registry's (RegistryPath).
public sealed class ClaudeBinaryLocator(ConfigLocator locator, string? path = null)
{
    public string Path { get; } = path ?? Environment.GetEnvironmentVariable("PATH") ?? "";

    public string? RealBinary() => RealClaude.Find(Path, locator.BinDir).Exe;

    /// A claude.cmd, .bat or .ps1 found where no claude.exe is.
    public string? ScriptOnly() => RealClaude.Find(Path, locator.BinDir).ScriptOnly;
}
