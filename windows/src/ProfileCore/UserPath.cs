using Microsoft.Win32;
using ProfileCore.Shared;

namespace ProfileCore;

/// The user PATH value in HKCU\Environment, unexpanded, with its registry type.
public interface IUserPathStore
{
    (string? Value, RegistryValueKind Kind) Read();
    void Write(string value, RegistryValueKind kind);
    void Broadcast();
}

public sealed class RegistryUserPathStore : IUserPathStore
{
    public (string? Value, RegistryValueKind Kind) Read()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Environment");
        if (k?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string v) return (null, RegistryValueKind.ExpandString);
        return (v, k.GetValueKind("Path"));
    }

    public void Write(string value, RegistryValueKind kind)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Environment", writable: true);
        k.SetValue("Path", value, kind);
    }

    public void Broadcast() => WinProcess.BroadcastEnvironmentChange();
}

/// Design D1 and flow 10 step 3: puts the bin folder first in the user PATH, or takes it out. Only an
/// entry equal to the bin folder, ignoring case and a trailing backslash, is ever moved or removed;
/// every other entry keeps its text and order. The old value goes to writes.log first.
public sealed class UserPath(ConfigLocator locator, IUserPathStore? store = null)
{
    public const int MaxLength = 32_767;
    readonly IUserPathStore store = store ?? new RegistryUserPathStore();

    bool IsOurs(string entry)
    {
        var e = RealClaude.Clean(entry);
        return e != null && (PathCanon.Same(e, locator.BinDir) || PathCanon.Same(PathCanon.Canonical(e), PathCanon.Canonical(locator.BinDir)));
    }

    public bool IsFirst()
    {
        var (value, _) = store.Read();
        var entries = (value ?? "").Split(';');
        return entries.Length > 0 && IsOurs(entries[0]) && entries.Skip(1).All(e => !IsOurs(e));
    }

    /// Returns false if nothing needed changing.
    public bool EnsureFirst()
    {
        if (IsFirst()) return false;
        var (value, kind) = store.Read();
        var rest = string.IsNullOrEmpty(value) ? [] : value.Split(';').Where(e => !IsOurs(e));
        var next = string.Join(';', new[] { locator.BinDir }.Concat(rest));
        Save(value, next, kind);
        return true;
    }

    /// Returns false if the bin folder was not in the user PATH.
    public bool Remove()
    {
        var (value, kind) = store.Read();
        if (string.IsNullOrEmpty(value)) return false;
        var entries = value.Split(';');
        if (!entries.Any(IsOurs)) return false;
        Save(value, string.Join(';', entries.Where(e => !IsOurs(e))), kind);
        return true;
    }

    void Save(string? old, string next, RegistryValueKind kind)
    {
        if (next.Length > MaxLength)
            throw new ProfileException($"Your user PATH would be over {MaxLength} characters, so it was not changed. Add {locator.BinDir} to the start of it yourself, in Settings > System > About > Advanced system settings > Environment Variables.");
        new EventLog(locator).Write("path", null, "user PATH before change: " + (old ?? "<none>"));
        store.Write(next, kind is RegistryValueKind.String or RegistryValueKind.ExpandString ? kind : RegistryValueKind.ExpandString);
        store.Broadcast();
    }
}
