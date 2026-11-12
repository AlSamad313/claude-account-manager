using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProfileCore;

public sealed record LoginResult(string? DuplicateOf);

public abstract record AccountChange
{
    public sealed record Unchanged : AccountChange;
    public sealed record Changed(string? From, string? To) : AccountChange;
}

/// The only module that writes profiles.json, default and names. The app and cpm are separate
/// processes, so each change holds the lock and re-reads from disk first.
public sealed class ProfileStore
{
    public ConfigLocator Locator { get; }
    public ICredentialProbe Credentials { get; }
    readonly Action<string> trash;
    readonly EventLog log;
    readonly StatusLineWriter statusLine;

    public ProfileStore(ConfigLocator locator, Action<string>? trash = null, ICredentialProbe? credentials = null)
    {
        Locator = locator;
        Credentials = credentials ?? new CredentialProbe();
        this.trash = trash ?? RecycleBin.MoveFolder;
        log = new EventLog(locator);
        statusLine = new StatusLineWriter(locator);
    }

    // ---- Reading

    public Registry Load() => WithLock(ReadOrSeed);

    public Profile DefaultProfile()
    {
        var r = Load();
        return r.ProfileById(r.DefaultProfileId) ?? r.ProfileById(Profile.BuiltinId) ?? throw ProfileException.NotFound(r.DefaultProfileId);
    }

    // ---- Changes

    /// Rewrites default and names from profiles.json, the source of truth.
    public void RepairFiles() => WithLock(() => { WriteTextFiles(ReadOrSeed()); return 0; });

    public Profile Create(string displayName, string commandName, string color)
    {
        Validate.DisplayName(displayName);
        Validate.CommandName(commandName);
        Validate.Color(color);
        return Change(r =>
        {
            if (r.ProfileByCommand(commandName) != null) throw ProfileException.CommandNameTaken(commandName);
            var id = NewId(r.Profiles.Select(p => p.Id));
            var dir = Locator.NewConfigDir(id);
            Directory.CreateDirectory(dir);
            var p = new Profile
            {
                Id = id, CommandName = commandName, DisplayName = displayName, Color = color, Builtin = false,
                ConfigDir = dir, CreatedAt = DateTime.UtcNow, State = ProfileState.PendingLogin,
            };
            statusLine.Write(p);
            r.Profiles.Add(p);
            return p;
        });
    }

    public void SetLoginStarted(string id, DateTime startedAt) => Update(id, p => { p.KeychainBaseline = null; p.LoginStartedAt = startedAt; });

    public void ClearLoginStarted(string id) => Update(id, p => p.LoginStartedAt = null);

    public LoginResult CompleteLogin(string id, Account account) => Change(r =>
    {
        var p = r.ProfileById(id) ?? throw ProfileException.NotFound(id);
        var dup = r.Profiles.FirstOrDefault(o => o.Id != id && o.AccountUuid == account.AccountUuid)?.Id;
        Apply(account, p);
        p.KeychainService = null;
        p.KeychainBaseline = null;
        p.LoginStartedAt = null;
        p.State = ProfileState.Ready;
        p.LastError = null;
        return new LoginResult(dup);
    });

    /// pendingLogin only. The folder goes to the Recycle Bin, without its login file.
    public void Discard(string id)
    {
        var dir = Change(r =>
        {
            var p = r.ProfileById(id) ?? throw ProfileException.NotFound(id);
            if (p.State != ProfileState.PendingLogin) throw new ProfileException("Only a profile waiting for a login can be discarded.");
            r.Profiles.Remove(p);
            return Locator.ConfigDir(p);
        });
        if (Directory.Exists(dir)) trash(dir);
    }

    public void MarkLoggedOut(string id) => Update(id, p => { p.State = ProfileState.LoggedOut; p.LastError = null; });

    public void MarkReady(string id, Account a) => Update(id, p => { Apply(a, p); p.State = ProfileState.Ready; p.LastError = null; });

    public void MarkError(string id, string reason) => Update(id, p => { p.State = ProfileState.Error; p.LastError = reason; });

    public void MarkDeleting(string id) => Change(r =>
    {
        var p = r.ProfileById(id) ?? throw ProfileException.NotFound(id);
        if (p.Builtin) throw new ProfileException("The built-in profile cannot be removed.");
        if (r.DefaultProfileId == id) r.DefaultProfileId = Profile.BuiltinId;
        p.State = ProfileState.Deleting;
        return 0;
    });

    public AccountChange UpdateAccount(string id, Account a) => Change<AccountChange>(r =>
    {
        var p = r.ProfileById(id) ?? throw ProfileException.NotFound(id);
        var old = p.AccountUuid;
        var oldEmail = p.Email;
        Apply(a, p);
        return old != null && old != a.AccountUuid ? new AccountChange.Changed(oldEmail, a.Email) : new AccountChange.Unchanged();
    });

    public void Rename(string id, string displayName)
    {
        Validate.DisplayName(displayName);
        Update(id, p => p.DisplayName = displayName);
    }

    public void SetColor(string id, string color)
    {
        Validate.Color(color);
        Update(id, p => p.Color = color);
    }

    public void SetDefault(string id)
    {
        Change(r =>
        {
            var p = r.ProfileById(id) ?? throw ProfileException.NotFound(id);
            if (p.State is not (ProfileState.Ready or ProfileState.LoggedOut or ProfileState.PendingLogin))
                throw new ProfileException($"{p.DisplayName} cannot be the default while its state is {Json(p.State)}.");
            r.DefaultProfileId = id;
            return 0;
        });
        log.Write("default", id, "default changed");
    }

    public void RemoveRow(string id) => Change(r =>
    {
        var p = r.ProfileById(id);
        if (p == null) return 0;
        if (p.Builtin) throw new ProfileException("The built-in profile cannot be removed.");
        if (r.DefaultProfileId == id) r.DefaultProfileId = Profile.BuiltinId;
        r.Profiles.Remove(p);
        return 0;
    });

    // ---- Internals

    static string Json(ProfileState s) => JsonSerializer.Serialize(s, CoreJson.Default.ProfileState).Trim('"');

    static void Apply(Account a, Profile p)
    {
        p.AccountUuid = a.AccountUuid;
        p.Email = a.Email;
        p.OrganizationName = a.OrganizationName;
    }

    void Update(string id, Action<Profile> body) => Change(r =>
    {
        body(r.ProfileById(id) ?? throw ProfileException.NotFound(id));
        return 0;
    });

    /// Lock, re-read, change, then write profiles.json, names, default, in that order, so default
    /// never names an ID that names does not hold yet.
    T Change<T>(Func<Registry, T> body) => WithLock(() =>
    {
        var r = ReadOrSeed();
        var result = body(r);
        WriteAll(r);
        return result;
    });

    T WithLock<T>(Func<T> body)
    {
        Directory.CreateDirectory(Locator.ProfilesRoot);
        using var _ = FileLock.Acquire(Locator.LockFile);
        return body();
    }

    Registry ReadOrSeed()
    {
        byte[]? data = null;
        try { data = File.ReadAllBytes(Locator.RegistryFile); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        if (data != null)
        {
            try
            {
                return JsonSerializer.Deserialize(data, CoreJson.Default.Registry) ?? throw new JsonException("empty");
            }
            catch (JsonException e)
            {
                throw new ProfileException($"{Locator.RegistryFile} is not readable: {e.Message} It was left unchanged.");
            }
        }
        var seed = new Profile
        {
            Id = Profile.BuiltinId, CommandName = "main", Color = "#2F6FDE", Builtin = true, ConfigDir = null, CreatedAt = DateTime.UtcNow,
        };
        var account = AccountReader.ReadAccount(Locator.ConfigFile(seed));
        var org = account?.OrganizationName;
        seed.DisplayName = org != null && IsValidDisplayName(org) ? org : "Main";
        if (account != null) Apply(account, seed);
        seed.State = account != null && Credentials.HasLogin(Locator, seed) ? ProfileState.Ready : ProfileState.LoggedOut;
        var reg = new Registry { SchemaVersion = 1, DefaultProfileId = Profile.BuiltinId, Profiles = [seed] };
        WriteAll(reg);
        return reg;
    }

    static bool IsValidDisplayName(string s)
    {
        try { Validate.DisplayName(s); return true; } catch (ProfileException) { return false; }
    }

    void WriteAll(Registry r)
    {
        WriteIfChanged(Locator.RegistryFile, JsonSerializer.SerializeToUtf8Bytes(r, CoreJson.Default.Registry));
        WriteTextFiles(r);
    }

    /// A rewrite with the same bytes would wake the app's file watcher for nothing.
    static void WriteIfChanged(string path, byte[] data)
    {
        try
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(data)) return;
        }
        catch (IOException) { }
        AtomicFile.Write(path, data);
    }

    void WriteTextFiles(Registry r)
    {
        var lines = r.Profiles.Where(p => p.IsInNames)
            .Select(p => string.Join('\t', p.CommandName, p.Id, p.DisplayName, p.Color, p.ConfigDir ?? "-"));
        var utf8 = new UTF8Encoding(false);
        WriteIfChanged(Locator.NamesFile, utf8.GetBytes(string.Join('\n', lines) + "\n"));
        WriteIfChanged(Locator.DefaultFile, utf8.GetBytes(r.DefaultProfileId + "\n"));
    }

    static string NewId(IEnumerable<string> existing)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var taken = existing.ToHashSet();
        while (true)
        {
            var id = new string(Enumerable.Range(0, 8).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
            if (!taken.Contains(id)) return id;
        }
    }
}
