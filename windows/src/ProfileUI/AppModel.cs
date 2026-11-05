using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ProfileCore;

namespace ProfileUI;

/// What the model calls. Tests build it over a temporary home with fakes for the parts that touch
/// the machine.
public sealed class AppEnvironment
{
    public required ProfileStore Store { get; init; }
    public required LoginCoordinator Login { get; init; }
    public required Launcher Launcher { get; init; }
    public required SessionIndex Index { get; init; }
    public required SessionStopper Stopper { get; init; }
    public required Doctor Doctor { get; init; }
    public required ShellInstaller Installer { get; init; }
    public required SetupCopier Copier { get; init; }
    /// The app's own copy of the bin files. Null when there is none, as in tests.
    public string? ShellSource { get; init; }

    public static AppEnvironment Live(string? shellSource)
    {
        var locator = ConfigLocator.Current();
        var store = new ProfileStore(locator);
        return new AppEnvironment
        {
            Store = store, Login = new LoginCoordinator(store), Launcher = new Launcher(store), Index = new SessionIndex(locator),
            Stopper = new SessionStopper(locator), Doctor = new Doctor(locator), Installer = new ShellInstaller(store), Copier = new SetupCopier(store),
            ShellSource = shellSource,
        };
    }
}

public abstract record Route
{
    public sealed record Main : Route;
    public sealed record Add : Route;
    public sealed record Waiting(string ProfileId) : Route;
    public sealed record Settings(string ProfileId) : Route;
    public sealed record Doctor : Route;
    public sealed record Shell : Route;
    public sealed record Stop(string SessionId) : Route;
    public sealed record Copy : Route;
    public sealed record Guide : Route;

    public static readonly Route Home = new Main();
}

/// A command that runs an action. Availability is shown by the views, so it is always enabled.
public sealed class Command(Action run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}

/// A profile as the lists show it.
public sealed class ProfileItem
{
    public required Profile Profile { get; init; }
    public required bool IsDefault { get; init; }
    public required string Subtitle { get; init; }
    public required bool ShellInstalled { get; init; }
    public string Name => Profile.DisplayName;
    public string Color => Profile.Color;
    public string Id => Profile.Id;
    public bool IsPending => Profile.State == ProfileState.PendingLogin;
    public bool IsLoggedOut => Profile.State == ProfileState.LoggedOut;
    public bool IsUsable => Profile.State is ProfileState.Ready or ProfileState.Error;
    public bool ShowMakeDefault => IsUsable && !IsDefault && Profile.State == ProfileState.Ready;
    public required ICommand NewTerminal { get; init; }
    public required ICommand MakeDefault { get; init; }
    public required ICommand FinishSignIn { get; init; }
    public required ICommand SignIn { get; init; }
    public required ICommand OpenVSCode { get; init; }
    public required ICommand Settings { get; init; }
}

/// A session as the lists show it.
public sealed class SessionItem
{
    public required SessionRow Row { get; init; }
    public required string ProfileName { get; init; }
    public required string Color { get; init; }
    public required string When { get; init; }
    public required bool ShellInstalled { get; init; }
    public string Title => Row.Title;
    public string Folder => Row.Cwd ?? "unknown folder";
    public bool IsLive => Row.Live;
    public string Subtitle => $"{ProfileName} · {When} · {Folder}";
    public required ICommand Resume { get; init; }
    public required ICommand Stop { get; init; }
}

/// One entry of the window's sidebar: "All profiles", then each profile.
public sealed record SidebarItem(string Id, string Name, string Subtitle, string Color, bool IsDefault, ProfileItem? Profile);

/// Shared by the tray panel and the window. It keeps only filtering and display; every action
/// calls ProfileCore. Work runs one job at a time off the UI thread, so the UI never waits.
public sealed class AppModel(AppEnvironment env) : INotifyPropertyChanged
{
    public const int PanelRowLimit = 8;
    public const string Grey = "#8A8F98";

    /// The swatches offered for a profile, as on macOS.
    public static readonly string[] ProfileColors = ["#E8772E", "#0F8A7E", "#8B5CF6", "#C2337A", "#7A6A1F", "#44546A"];

    /// Suggested from the display name until the user types their own: ASCII letters and digits,
    /// each run of anything else as one hyphen, no hyphens at the ends, at most 24 characters.
    public static string SuggestCommandName(string displayName)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(displayName.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return s.Length > 24 ? s[..24].TrimEnd('-') : s;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// A pending profile has signed in (design D8). Raised once per sign-in, and not while the
    /// waiting step for it is on screen.
    public event Action<Profile, Account>? SignInDetected;

    public AppEnvironment Env => env;
    readonly SemaphoreSlim work = new(1, 1);
    FileWatcher? watcher;
    int visibleCount;
    CancellationTokenSource? ticker;
    CancellationTokenSource? background;
    readonly HashSet<string> notified = [];
    Func<DateTime> now = () => DateTime.Now;
    internal Func<DateTime> Now { get => now; set => now = value; }

    // ---- State

    Route route = Route.Home;
    public Route Route { get => route; set { if (Set(ref route, value)) { Raise(nameof(IsFlow)); Raise(nameof(IsMain)); } } }
    public bool IsMain => Route is Route.Main;
    public bool IsFlow => !IsMain;

    string filter = "";
    public string Filter { get => filter; set { if (Set(ref filter, value)) Rebuild(); } }

    string sidebarSelection = "all";
    public string SidebarSelection
    {
        get => sidebarSelection;
        set { if (Set(ref sidebarSelection, value ?? "all")) { Rebuild(); Raise(nameof(TargetName)); } }
    }

    string? noticeText;
    public string? NoticeText { get => noticeText; set => Set(ref noticeText, value); }
    string? errorText;
    public string? ErrorText { get => errorText; set => Set(ref errorText, value); }

    public IReadOnlyList<Profile> Profiles { get; private set; } = [];
    public string DefaultProfileId { get; private set; } = Profile.BuiltinId;
    public IReadOnlyList<SessionRow> Rows { get; private set; } = [];
    public int ParseFailures { get; private set; }
    public bool ShellInstalled { get; private set; }
    public bool ShowShellNotice => !ShellInstalled;

    public DoctorReport? DoctorReport { get; private set; }
    public bool DoctorRunning { get; private set; }
    public InstallResult? InstallResult { get; private set; }
    public LoginState? LoginState { get; private set; }
    public bool LoginBusy { get; private set; }
    public bool StopBusy { get; private set; }
    public bool Busy { get; private set; }

    SessionRow? missingFolderRow;
    public SessionRow? MissingFolderRow { get => missingFolderRow; set { if (Set(ref missingFolderRow, value)) Raise(nameof(MissingFolderText)); } }
    public string? MissingFolderText => MissingFolderRow is { } r ? $"The folder for \"{r.Title}\" no longer exists: {r.Cwd ?? "unknown"}." : null;

    // ---- Derived, rebuilt whenever the inputs change

    public Profile? DefaultProfile => Profiles.FirstOrDefault(p => p.Id == DefaultProfileId);
    public string DefaultColor => DefaultProfile?.Color ?? Grey;
    public string DefaultLine => $"plain claude uses {DefaultProfile?.DisplayName ?? "unknown"}";
    public Profile? ProfileById(string id) => Profiles.FirstOrDefault(p => p.Id == id);
    public SessionRow? Row(string sessionId) => Rows.FirstOrDefault(r => r.SessionId == sessionId);
    public int LiveCount(string profileId) => Rows.Count(r => r.ProfileId == profileId && r.Live);

    public IReadOnlyList<ProfileItem> ProfileItems { get; private set; } = [];
    public IReadOnlyList<SessionItem> PanelSessions { get; private set; } = [];
    public IReadOnlyList<SessionItem> WindowSessions { get; private set; } = [];
    public IReadOnlyList<SidebarItem> Sidebar { get; private set; } = [];
    public bool PanelEmpty => PanelSessions.Count == 0;
    public string PanelEmptyText => Filter.Length == 0 ? "No sessions yet." : "No sessions match this filter.";
    public string WindowStatusLine =>
        $"{WindowSessions.Count} session{(WindowSessions.Count == 1 ? "" : "s")}. Running ones are rechecked every 2 seconds while this window is open." +
        (ParseFailures > 0 ? $" {ParseFailures} transcripts could not be read." : "");

    /// The window's New Terminal button uses the selected profile, or the default for "All profiles".
    public Profile? Target => SidebarSelection == "all" ? DefaultProfile : ProfileById(SidebarSelection);
    public string TargetName => $"New Terminal on {Target?.DisplayName ?? "default"}";

    bool Matches(SessionRow r)
    {
        var q = Filter.Trim();
        if (q.Length == 0) return true;
        var name = ProfileById(r.ProfileId)?.DisplayName ?? "";
        return new[] { r.Title, r.Cwd ?? "", name }.Any(s => s.Contains(q, StringComparison.CurrentCultureIgnoreCase));
    }

    void Rebuild()
    {
        var matching = Rows.Where(Matches).ToList();
        var profiles = Profiles.Select(ItemFor).ToList();
        // Lists are swapped only when what they show changed, so a list keeps its selection and
        // focus across the 2-second rechecks.
        if (Swap(ProfileItems, profiles, ProfileKey)) { ProfileItems = profiles; Raise(nameof(ProfileItems)); }
        var panel = matching.Take(PanelRowLimit).Select(SessionFor).ToList();
        if (Swap(PanelSessions, panel, SessionKey)) { PanelSessions = panel; Raise(nameof(PanelSessions)); }
        var window = matching.Where(r => SidebarSelection == "all" || r.ProfileId == SidebarSelection).Select(SessionFor).ToList();
        if (Swap(WindowSessions, window, SessionKey)) { WindowSessions = window; Raise(nameof(WindowSessions)); }
        List<SidebarItem> sidebar = [new SidebarItem("all", "All profiles", $"{Rows.Count} sessions", Grey, false, null),
                   .. ProfileItems.Select(i => new SidebarItem(i.Id, i.Name, i.Subtitle, i.Color, i.IsDefault, i))];
        if (Swap(Sidebar, sidebar, s => s with { Profile = null })) { Sidebar = sidebar; Raise(nameof(Sidebar)); }
        foreach (var n in new[] { nameof(PanelEmpty), nameof(PanelEmptyText), nameof(WindowStatusLine), nameof(DefaultProfile), nameof(DefaultColor),
                                  nameof(DefaultLine), nameof(Target), nameof(TargetName), nameof(ShellInstalled), nameof(ShowShellNotice),
                                  nameof(Profiles), nameof(Rows) })
            Raise(n);
    }

    static bool Swap<T, K>(IReadOnlyList<T> old, IReadOnlyList<T> fresh, Func<T, K> key) =>
        !old.Select(key).SequenceEqual(fresh.Select(key));

    static string ProfileKey(ProfileItem i) =>
        string.Join("\u0001", i.Id, i.Name, i.Color, i.Profile.State, i.IsDefault, i.Subtitle, i.ShellInstalled);

    static (SessionRow, string, string, string, bool) SessionKey(SessionItem i) => (i.Row, i.When, i.ProfileName, i.Color, i.ShellInstalled);

    ProfileItem ItemFor(Profile p)
    {
        var live = LiveCount(p.Id);
        var who = p.State switch
        {
            ProfileState.Error => $"Problem: {p.LastError ?? "unknown"}",
            ProfileState.Deleting => "Being removed",
            ProfileState.LoggedOut => "Signed out",
            _ => p.Email ?? "Not signed in",
        };
        return new ProfileItem
        {
            Profile = p, IsDefault = p.Id == DefaultProfileId, ShellInstalled = ShellInstalled,
            Subtitle = live > 0 ? $"{who} · {live} running" : who,
            NewTerminal = new Command(() => _ = NewSession(p)),
            MakeDefault = new Command(() => _ = SetDefault(p)),
            FinishSignIn = new Command(() => { ErrorText = null; Route = new Route.Waiting(p.Id); _ = PollLogin(p.Id); }),
            SignIn = new Command(() => _ = SignIn(p)),
            OpenVSCode = new Command(() => _ = OpenVSCode(p)),
            Settings = new Command(() => { ErrorText = null; Route = new Route.Settings(p.Id); }),
        };
    }

    SessionItem SessionFor(SessionRow r)
    {
        var p = ProfileById(r.ProfileId);
        return new SessionItem
        {
            Row = r, ProfileName = p?.DisplayName ?? "Unknown", Color = p?.Color ?? Grey, When = When(r), ShellInstalled = ShellInstalled,
            Resume = new Command(() => _ = Resume(r)),
            Stop = new Command(() => RequestStop(r)),
        };
    }

    string When(SessionRow r)
    {
        if (r.Live)
        {
            var s = r.Status is { } st ? $"running, {st}" : "running";
            return r.MayBeStale ? $"{s}, may be stale" : s;
        }
        return Relative(r.LastActivity.ToLocalTime(), Now());
    }

    /// "5 minutes ago", as the macOS named relative format reads.
    public static string Relative(DateTime then, DateTime now)
    {
        var d = now - then;
        if (d < TimeSpan.FromMinutes(1)) return "just now";
        if (d < TimeSpan.FromHours(1)) return Plural((int)d.TotalMinutes, "minute");
        if (d < TimeSpan.FromDays(1) && then.Date == now.Date) return Plural((int)d.TotalHours, "hour");
        if (then.Date == now.Date.AddDays(-1)) return "yesterday";
        if (d < TimeSpan.FromDays(7)) return Plural(Math.Max(2, (now.Date - then.Date).Days), "day");
        return then.ToString("d", CultureInfo.CurrentCulture);
    }

    static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";

    // ---- Running work

    async Task<(T? Value, Exception? Error)> Run<T>(Func<T> body)
    {
        await work.WaitAsync();
        try { return (await Task.Run(body), null); }
        catch (Exception e) { return (default, e); }
        finally { work.Release(); }
    }

    Task<(bool Value, Exception? Error)> Run(Action body) => Run(() => { body(); return true; });

    public static string Message(Exception e) => e is ProfileException ? e.Message : $"Something went wrong: {e.Message}";

    // ---- Loading

    /// Once, when the app starts: rotate the log, refresh bin\ from the app's copy, load.
    public async Task PrepareAtLaunch()
    {
        await Run(() =>
        {
            new EventLog(env.Store.Locator).Rotate();
            if (env.ShellSource != null && Directory.Exists(env.ShellSource)) env.Installer.UpdateIfInstalled(env.ShellSource);
        });
        await Refresh();
    }

    sealed record Snapshot(Registry Reg, SessionIndexResult Sessions, bool Installed, List<string> Notices);

    /// Full load: reconcile, profiles, sessions.
    public async Task Refresh()
    {
        var (s, e) = await Run(() =>
        {
            var reconcile = new Reconciler(env.Store, env.Login).Run();
            var reg = env.Store.Load();
            return new Snapshot(reg, env.Index.Sessions(reg.Profiles), env.Installer.IsInstalled(), reconcile.AccountNotices);
        });
        if (e != null) { ErrorText = Message(e); return; }
        Profiles = s!.Reg.Profiles;
        DefaultProfileId = s.Reg.DefaultProfileId;
        Rows = s.Sessions.Rows;
        ParseFailures = s.Sessions.ParseFailures;
        ShellInstalled = s.Installed;
        if (s.Notices.FirstOrDefault() is { } n) NoticeText = n;
        Rebuild();
        await CheckSignIns();
    }

    /// Light recheck while something is visible. Writes nothing.
    public async Task Tick()
    {
        var current = Profiles;
        var (r, e) = await Run(() => env.Index.Sessions(current));
        if (e == null)
        {
            Rows = r!.Rows;
            ParseFailures = r.ParseFailures;
            Rebuild();
        }
        if (Route is Route.Waiting w) await PollLogin(w.ProfileId);
    }

    /// Design D8. The panel is usually closed while the user signs in, so pending profiles are
    /// polled in the background and a sign-in raises SignInDetected once.
    public async Task CheckSignIns()
    {
        var pending = Profiles.Where(p => p.State == ProfileState.PendingLogin).ToList();
        notified.IntersectWith(pending.Select(p => p.Id));
        foreach (var p in pending)
        {
            var (state, e) = await Run(() => env.Login.Poll(p));
            if (e != null || state is not LoginState.Detected d) continue;
            var onScreen = Route is Route.Waiting w && w.ProfileId == p.Id && visibleCount > 0;
            if (notified.Add(p.Id) && !onScreen) SignInDetected?.Invoke(p, d.Account);
        }
    }

    // ---- Visibility

    /// One watcher for the icon colour and for changes made with cpm.
    public void StartWatching(Action<Action> onUiThread)
    {
        watcher ??= new FileWatcher(env.Store.Locator.ProfilesRoot, () => onUiThread(() => _ = Refresh()));
    }

    /// Polls pending sign-ins every 3 seconds while any profile waits for one, even with nothing on screen.
    public void StartBackground()
    {
        if (background != null) return;
        background = new CancellationTokenSource();
        _ = Loop(TimeSpan.FromSeconds(3), background.Token, async () =>
        {
            if (Profiles.Any(p => p.State == ProfileState.PendingLogin)) await CheckSignIns();
        });
    }

    public void ViewAppeared()
    {
        visibleCount++;
        _ = Refresh();
        if (ticker != null) return;
        ticker = new CancellationTokenSource();
        _ = Loop(TimeSpan.FromSeconds(2), ticker.Token, Tick);
    }

    public void ViewDisappeared()
    {
        visibleCount = Math.Max(0, visibleCount - 1);
        if (visibleCount > 0) return;
        ticker?.Cancel();
        ticker = null;
    }

    static async Task Loop(TimeSpan every, CancellationToken token, Func<Task> body)
    {
        using var timer = new PeriodicTimer(every);
        try
        {
            while (await timer.WaitForNextTickAsync(token)) await body();
        }
        catch (OperationCanceledException) { }
    }

    // ---- Sessions

    /// Asks which folder a new session starts in, given the profile's name; null when cancelled.
    /// Replaced in tests. Windows' folder picker opens on the folder chosen last, which it remembers.
    public Func<string, string?> ChooseFolder { get; set; } = UiState.ChooseFolder;

    /// New Terminal: asks which folder to start in, as on macOS. Cancelling starts nothing.
    public Task NewSession(Profile p) => ChooseFolder(p.DisplayName) is { } folder ? NewSession(p, folder) : Task.CompletedTask;
    public Task NewSession(Profile p, string folder) => Launch(() => env.Launcher.NewSession(p, folder));
    public Task NewSessionOnTarget() => Target is { } t ? NewSession(t) : Task.CompletedTask;
    public Task OpenVSCode(Profile p) => Launch(() => env.Launcher.OpenVSCode(p));

    public async Task Resume(SessionRow row, string? folderOverride = null)
    {
        if (ProfileById(row.ProfileId) is not { } owner) return;
        var (_, e) = await Run(() => env.Launcher.Resume(row, owner, folderOverride));
        if (e is LauncherException { MissingFolder: not null } && folderOverride == null)
            MissingFolderRow = row;
        else ErrorText = e == null ? null : Message(e);
    }

    public void RequestStop(SessionRow row)
    {
        ErrorText = null;
        Route = new Route.Stop(row.SessionId);
    }

    /// The view showed the question already, so this confirms straight through.
    public async Task ConfirmStop(SessionRow row)
    {
        StopBusy = true;
        Raise(nameof(StopBusy));
        var (result, e) = await Run(() => env.Stopper.Stop(row, _ => true));
        StopBusy = false;
        Raise(nameof(StopBusy));
        Route = Route.Home;
        switch (result)
        {
            case StopResult.Stopped { LeftRunning.Count: > 0 } s:
                NoticeText = "Stopped. Still running, and left alone: " + string.Join(", ", s.LeftRunning.Select(c => $"{c.Name} (process ID {c.Pid})")) + ".";
                break;
            case StopResult.DidNotStop d: ErrorText = $"It did not stop. Its process ID is {d.Pid}."; break;
            case StopResult.CouldNotSend c: ErrorText = $"Not stopped: {c.Reason}"; break;
        }
        if (e != null) ErrorText = Message(e);
        await Refresh();
    }

    // ---- Profiles

    public async Task AddProfile(string displayName, string commandName, string color)
    {
        SetBusy(true);
        var (p, e) = await Run(() =>
        {
            var created = env.Store.Create(displayName, commandName, color);
            env.Login.Begin(created);
            var fresh = env.Store.Load().ProfileById(created.Id) ?? created;
            env.Launcher.Login(fresh);
            return fresh;
        });
        SetBusy(false);
        await Refresh();
        if (e != null) { ErrorText = Message(e); return; }
        ErrorText = null;
        SetLogin(new LoginState.Waiting());
        Route = new Route.Waiting(p!.Id);
    }

    public async Task PollLogin(string profileId)
    {
        if (ProfileById(profileId) is not { } p) return;
        var (state, e) = await Run(() => env.Login.Poll(p));
        if (e == null) SetLogin(state);
    }

    public async Task KeepLogin(string profileId)
    {
        if (ProfileById(profileId) is not { } p) return;
        LoginBusy = true;
        Raise(nameof(LoginBusy));
        var (_, e) = await Run(() => env.Login.Keep(p));
        LoginBusy = false;
        Raise(nameof(LoginBusy));
        if (e != null) ErrorText = Message(e);
        else
        {
            Route = Route.Home;
            SetLogin(null);
        }
        await Refresh();
    }

    public async Task RetryLogin(string profileId)
    {
        if (ProfileById(profileId) is not { } p) return;
        var (_, e) = await Run(() =>
        {
            env.Login.Retry(p);
            env.Launcher.Login(env.Store.Load().ProfileById(p.Id) ?? p);
        });
        ErrorText = e == null ? null : Message(e);
        notified.Remove(profileId);
        SetLogin(new LoginState.Waiting());
        await Refresh();
    }

    public async Task DiscardLogin(string profileId)
    {
        if (ProfileById(profileId) is not { } p) return;
        var (_, e) = await Run(() => env.Login.Discard(p));
        if (e != null) ErrorText = Message(e);
        else
        {
            Route = Route.Home;
            SetLogin(null);
        }
        await Refresh();
    }

    /// For a profile that was signed out: opens a terminal window to sign in again.
    public Task SignIn(Profile p) => Launch(() => env.Launcher.Login(p));

    public Task Rename(Profile p, string name) => Change(() => env.Store.Rename(p.Id, name));
    public Task SetColor(Profile p, string color) => Change(() => env.Store.SetColor(p.Id, color));
    public Task SetDefault(Profile p) => Change(() => env.Store.SetDefault(p.Id));

    // ---- Doctor and command line

    public async Task RunDoctor()
    {
        DoctorRunning = true;
        Raise(nameof(DoctorRunning));
        var (r, _) = await Run(() => env.Doctor.Run());
        DoctorRunning = false;
        if (r != null) DoctorReport = r;
        Raise(nameof(DoctorRunning));
        Raise(nameof(DoctorReport));
    }

    // ---- Copy setup (design flow 6). Nothing is written until ApplyCopy.

    public CopyPlan? CopyPlan { get; private set; }
    public ApplyResult? CopyResult { get; private set; }
    public RestoreResult? RestoreResult { get; private set; }
    public bool CopyBusy { get; private set; }
    public IReadOnlyList<BackupInfo> Backups { get; private set; } = [];

    public IReadOnlyList<Profile> CopySources => Profiles.Where(p => p.State == ProfileState.Ready).ToList();
    /// The built-in profile is never a target.
    public IReadOnlyList<Profile> CopyTargets => Profiles.Where(p => !p.Builtin && p.State == ProfileState.Ready).ToList();

    /// Which profile "To" should hold once "From" changes: the current one if it is still valid,
    /// otherwise the first valid one, or none.
    public static string? CopyTarget(string? source, string? current, IEnumerable<Profile> targets)
    {
        var valid = targets.Where(t => t.Id != source).Select(t => t.Id).ToList();
        return current != null && valid.Contains(current) ? current : valid.FirstOrDefault();
    }

    /// The built-in profile if it is signed in, as it usually holds the setup to copy.
    public string? DefaultCopySource() =>
        CopySources.FirstOrDefault(p => p.Builtin)?.Id ?? CopySources.FirstOrDefault()?.Id;

    void RaiseCopy()
    {
        foreach (var n in new[] { nameof(CopyPlan), nameof(CopyResult), nameof(RestoreResult), nameof(CopyBusy), nameof(Backups) }) Raise(n);
    }

    /// The view changed a tick or a conflict choice.
    public void CopyPlanEdited() => Raise(nameof(CopyPlan));

    public async Task PreparePreview(string? sourceId, string? targetId, bool keepResult = false)
    {
        if (!keepResult) { CopyResult = null; RestoreResult = null; }
        var source = sourceId == null ? null : ProfileById(sourceId);
        var target = targetId == null ? null : ProfileById(targetId);
        if (source == null || target == null || source.Id == target.Id)
        {
            CopyPlan = null;
            RaiseCopy();
            return;
        }
        var (r, e) = await Run(() => (Plan: env.Copier.Plan(source, target), List: env.Copier.Backups.List(target.Id)));
        if (e != null)
        {
            CopyPlan = null;
            ErrorText = Message(e);
        }
        else
        {
            CopyPlan = r.Plan;
            Backups = r.List;
            ErrorText = null;
        }
        RaiseCopy();
    }

    public async Task ApplyCopy()
    {
        if (CopyPlan is not { } plan || CopyBusy) return;
        CopyBusy = true;
        RestoreResult = null;
        RaiseCopy();
        var (r, e) = await Run(() => env.Copier.Apply(plan));
        CopyBusy = false;
        if (e != null) ErrorText = Message(e);
        else
        {
            CopyResult = r;
            ErrorText = null;
        }
        RaiseCopy();
        await PreparePreview(plan.SourceId, plan.TargetId, keepResult: true);
    }

    public async Task RestoreBackup(string backupId, string targetId, string? sourceId)
    {
        if (ProfileById(targetId) is not { } target) return;
        CopyBusy = true;
        CopyResult = null;
        RaiseCopy();
        var (r, e) = await Run(() => env.Copier.Restore(target, backupId));
        CopyBusy = false;
        if (e != null) ErrorText = Message(e);
        else
        {
            RestoreResult = r;
            ErrorText = null;
        }
        RaiseCopy();
        await PreparePreview(sourceId, targetId, keepResult: true);
    }

    public void ClearCopy()
    {
        CopyPlan = null;
        CopyResult = null;
        RestoreResult = null;
        Backups = [];
        RaiseCopy();
    }

    public async Task FixAndRerunDoctor()
    {
        await Run(() => env.Store.RepairFiles());
        await Refresh();
        await RunDoctor();
    }

    public void ClearInstallResult()
    {
        InstallResult = null;
        Raise(nameof(InstallResult));
    }

    /// Design D1: puts the bin folder first in the user PATH, and adds tab completion if asked.
    public async Task SetUpCommandLine(bool completion)
    {
        if (env.ShellSource == null || !Directory.Exists(env.ShellSource))
        {
            ErrorText = "This build has no copy of the command line tools. Run: windows\\build.ps1 -Install";
            return;
        }
        SetBusy(true);
        var (r, e) = await Run(() => env.Installer.Install(env.ShellSource, completion));
        SetBusy(false);
        if (e != null) ErrorText = Message(e);
        else
        {
            ErrorText = null;
            InstallResult = r;
            Raise(nameof(InstallResult));
        }
        await Refresh();
    }

    // ---- Helpers

    async Task Launch(Action body)
    {
        var (_, e) = await Run(body);
        ErrorText = e == null ? null : Message(e);
        await Tick();
    }

    async Task Change(Action body)
    {
        var (_, e) = await Run(body);
        if (e != null) ErrorText = Message(e);
        await Refresh();
    }

    void SetLogin(LoginState? s)
    {
        LoginState = s;
        Raise(nameof(LoginState));
    }

    void SetBusy(bool b)
    {
        Busy = b;
        Raise(nameof(Busy));
    }

    bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        watcher?.Dispose();
        ticker?.Cancel();
        background?.Cancel();
    }
}
