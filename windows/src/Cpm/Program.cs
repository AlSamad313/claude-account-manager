// cpm.exe: the command line. Same commands and wording as the macOS cpm, where the stage is built.
using ProfileCore;

namespace Cpm;

internal static class Program
{
    const string Usage = """
        Usage:
          cpm add <displayName> <commandName> <#RRGGBB>   add a profile, then sign in with claude-as
          cpm keep <commandName>                          finish a sign-in
          cpm retry <commandName>                         sign out and sign in again
          cpm discard <commandName>                       remove a profile waiting for a sign-in
          cpm remove <commandName> [--yes]                sign out a profile and move its folder to the Recycle Bin
          cpm default [<commandName>]                     show or set the default profile
          cpm list [--profile <commandName>] [--live]     list sessions
          cpm new <commandName> [<folder>]                start a session in a new terminal window
          cpm resume <sessionId>                          resume a session, on the profile that owns it
          cpm stop <sessionId> [--yes]                    stop a running session with Ctrl+C
          cpm doctor [--fix]                              check the setup
          cpm code <commandName> [<folder>]               open VS Code on a profile (quit VS Code first)
          cpm install-shell [--completion]                install claude, claude-as and cpm, and put them first in your PATH;
                                                          --completion also adds tab completion to your PowerShell profile
          cpm uninstall-shell                             take them out of your PATH, and the completion line out again
          cpm uninstall [--yes]                           remove all added profiles and the command line setup
        """;

    static string TabSafe(string s) => s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    /// Uses the display list, so a desktop app session cannot be resumed or stopped from here.
    static SessionRow? FindRow(string id, Profile owner, SessionIndex index) =>
        index.Sessions([owner]).Rows.FirstOrDefault(r => r.SessionId == id);

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        if (args.Length == 0) return Fail(Usage);
        var command = args[0];
        var rest = args[1..];
        var locator = ConfigLocator.Current();
        var store = new ProfileStore(locator);
        var login = new LoginCoordinator(store);

        Profile Find(string name) => store.Load().ProfileByCommand(name) ?? throw ProfileException.NotFound(name);

        void Reconcile()
        {
            new EventLog(locator).Rotate();
            var r = new Reconciler(store, login, new ProfileRemover(store)).Run();
            foreach (var n in r.LoginNotices.Concat(r.AccountNotices)) Console.WriteLine(n);
            foreach (var n in r.RowsWithoutFolder) Console.WriteLine($"{n}: its folder is missing. Run: cpm doctor");
            foreach (var n in r.RemovalsResumed) Console.WriteLine($"Finished removing {n}.");
            foreach (var n in r.RemovalWarnings) Console.WriteLine(n);
        }

        try
        {
            switch (command)
            {
                case "add":
                {
                    if (rest.Length != 3) return Fail(Usage);
                    Reconcile();
                    var p = store.Create(rest[0], rest[1], rest[2]);
                    var cmd = login.Begin(p);
                    Console.WriteLine($"Added {p.DisplayName}. Sign in with:\n  {cmd}");
                    Console.WriteLine($"Check the browser is signed into the account you want, or use a private window. Then run:\n  cpm keep {p.CommandName}");
                    return 0;
                }
                case "keep":
                {
                    if (rest.Length != 1) return Fail(Usage);
                    Reconcile();
                    var r = login.Keep(Find(rest[0]));
                    Console.WriteLine($"Kept: {rest[0]} is signed in as {r.Account.Email ?? "an account"}.");
                    if (r.DuplicateOf != null && store.Load().ProfileById(r.DuplicateOf) is { } other)
                        Console.WriteLine($"Warning: {other.DisplayName} already uses this account, so both share its usage limits.");
                    return 0;
                }
                case "retry":
                {
                    if (rest.Length != 1) return Fail(Usage);
                    Reconcile();
                    var cmd = login.Retry(Find(rest[0]));
                    Console.WriteLine($"Signed out. Sign in again with:\n  {cmd}\nThen run:\n  cpm keep {rest[0]}");
                    return 0;
                }
                case "discard":
                {
                    if (rest.Length != 1) return Fail(Usage);
                    Reconcile();
                    login.Discard(Find(rest[0]));
                    Console.WriteLine($"Discarded {rest[0]}. Its folder is in the Recycle Bin, without its login.");
                    return 0;
                }
                case "default":
                {
                    Reconcile();
                    if (rest.Length == 0) { Console.WriteLine(store.DefaultProfile().CommandName); return 0; }
                    if (rest.Length != 1) return Fail(Usage);
                    var p = Find(rest[0]);
                    store.SetDefault(p.Id);
                    Console.WriteLine($"Plain claude now uses {p.DisplayName}. Running sessions are not affected.");
                    return 0;
                }
                case "list":
                {
                    var live = false;
                    string? filter = null;
                    for (var i = 0; i < rest.Length;)
                    {
                        if (rest[i] == "--live") { live = true; i++; }
                        else if (rest[i] == "--profile" && i + 1 < rest.Length) { filter = rest[i + 1]; i += 2; }
                        else return Fail(Usage);
                    }
                    Reconcile();
                    var reg = store.Load();
                    if (filter != null && reg.ProfileByCommand(filter) == null) throw ProfileException.NotFound(filter);
                    var profiles = reg.Profiles.Where(p => filter == null || p.CommandName == filter);
                    var result = new SessionIndex(locator).Sessions(profiles);
                    Console.WriteLine(string.Join('\t', "PROFILE", "STATUS", "TITLE", "FOLDER", "SESSION"));
                    foreach (var r in result.Rows.Where(r => !live || r.Live))
                    {
                        var status = r.Live
                            ? $"running {r.Status}".Trim() + (r.MayBeStale ? " (may be stale)" : "")
                            : r.LastActivity.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                        var cmd = reg.ProfileById(r.ProfileId)?.CommandName ?? r.ProfileId;
                        Console.WriteLine(string.Join('\t', new[] { cmd, status, r.Title, r.Cwd ?? "-", r.SessionId }.Select(TabSafe)));
                    }
                    if (result.ParseFailures > 0) Console.Error.WriteLine($"{result.ParseFailures} transcript file(s) could not be read.");
                    return 0;
                }
                case "new":
                {
                    if (rest.Length is < 1 or > 2) return Fail(Usage);
                    Reconcile();
                    new Launcher(store).NewSession(Find(rest[0]), rest.Length == 2 ? rest[1] : null);
                    Console.WriteLine($"Opened a terminal window on {rest[0]}.");
                    return 0;
                }
                case "code":
                {
                    if (rest.Length is < 1 or > 2) return Fail(Usage);
                    Reconcile();
                    new Launcher(store).OpenVSCode(Find(rest[0]), rest.Length == 2 ? rest[1] : null);
                    Console.WriteLine($"Opened VS Code on {rest[0]}.");
                    return 0;
                }
                case "resume":
                {
                    if (rest.Length != 1) return Fail(Usage);
                    Reconcile();
                    var reg = store.Load();
                    var index = new SessionIndex(locator);
                    var owner = index.OwnerOf(rest[0], reg.Profiles) ?? throw LauncherException.NoOwner(rest[0]);
                    var row = FindRow(rest[0], owner, index);
                    if (row == null) return Fail("That session belongs to the desktop app. Resume it there.");
                    new Launcher(store).Resume(row, owner);
                    Console.WriteLine($"Opened a terminal window on {owner.CommandName}, resuming {row.Title}.");
                    return 0;
                }
                case "stop":
                {
                    var yes = rest.Contains("--yes");
                    var ids = rest.Where(a => a != "--yes").ToArray();
                    if (ids.Length != 1 || rest.Length != ids.Length + (yes ? 1 : 0)) return Fail(Usage);
                    Reconcile();
                    var reg = store.Load();
                    var index = new SessionIndex(locator);
                    var owner = index.OwnerOf(ids[0], reg.Profiles);
                    var row = owner == null ? null : FindRow(ids[0], owner, index);
                    if (owner == null || row == null) return Fail("That session is not running, or belongs to the desktop app.");
                    var noTerminal = false;
                    var result = new SessionStopper(locator).Stop(row, req =>
                    {
                        Console.WriteLine($"Stop \"{req.Title}\" on {owner.DisplayName}, in {req.Folder ?? "unknown folder"}, {req.Status ?? "status unknown"}?");
                        if (req.Warning != null) Console.WriteLine(req.Warning);
                        Console.WriteLine(StopRequest.WindowNote);
                        if (yes) return true;
                        if (Console.IsInputRedirected) { noTerminal = true; return false; }
                        Console.Write("Stop this session? [y/N] ");
                        return (Console.ReadLine()?.Trim().ToLowerInvariant()) is "y" or "yes";
                    });
                    switch (result)
                    {
                        case StopResult.AlreadyStopped: Console.WriteLine("That session is not running."); return 0;
                        case StopResult.Cancelled:
                            return noTerminal ? Fail("Not stopped. Without a terminal to confirm on, use --yes.") : Ok("Not stopped.");
                        case StopResult.Stopped s:
                            Console.WriteLine($"Stopped with {(s.CtrlCs == 1 ? "Ctrl+C" : "a second Ctrl+C")}. Its window goes back to its shell, or closes if cpm or the app opened it.");
                            if (s.LeftRunning.Count > 0)
                            {
                                Console.WriteLine("These programs it started are still running, and were left alone:");
                                foreach (var c in s.LeftRunning) Console.WriteLine($"  {c.Name} (process ID {c.Pid})");
                            }
                            return 0;
                        case StopResult.DidNotStop d: return Fail($"It did not stop. Its process ID is {d.Pid}.", 2);
                        case StopResult.CouldNotSend c: return Fail($"Not stopped: {c.Reason}");
                        default: return Fail("Unknown result.");
                    }
                }
                case "doctor":
                {
                    // Without --fix, doctor must see faults rather than repair them.
                    if (rest is ["--fix"]) { Reconcile(); Console.WriteLine("Rewrote default and names from the profile list."); }
                    else if (rest.Length != 0) return Fail(Usage);
                    var report = new Doctor(locator).Run();
                    foreach (var f in report.Findings)
                    {
                        Console.WriteLine($"{f.Level switch { FindingLevel.Ok => "ok   ", FindingLevel.Note => "note ", _ => "FAULT" }} {f.Message}");
                        if (f.Fix != null) Console.WriteLine($"      fix: {f.Fix}");
                    }
                    return report.HasFaults ? 2 : 0;
                }
                case "install-shell":
                {
                    if (rest is not ([] or ["--completion"])) return Fail(Usage);
                    var me = Environment.ProcessPath ?? throw new ProfileException("Cannot tell where cpm is running from.");
                    var r = new ShellInstaller(store).Install(Path.GetDirectoryName(me)!, completion: rest.Length == 1);
                    Console.WriteLine($"Installed to {locator.BinDir}.");
                    Console.WriteLine(r.PathChanged
                        ? "Put it first in your user PATH. The old value is in logs\\writes.log."
                        : "It was already first in your user PATH.");
                    foreach (var f in r.CompletionAdded ?? []) Console.WriteLine($"Added tab completion to {f}.");
                    Console.WriteLine("Open a new terminal window to use claude-as, then run: cpm doctor");
                    return 0;
                }
                case "uninstall-shell":
                {
                    if (rest.Length != 0) return Fail(Usage);
                    var removed = new UserPath(locator).Remove();
                    foreach (var f in new PowerShellProfile(locator).Remove()) Console.WriteLine($"Took the completion line out of {f}.");
                    Console.WriteLine(removed
                        ? $"Took {locator.BinDir} out of your user PATH. Open a new terminal window for plain claude to run Claude Code directly. Profiles and their files are unchanged."
                        : $"{locator.BinDir} was not in your user PATH. Nothing changed.");
                    return 0;
                }
                case "remove":
                {
                    var yes = rest.Contains("--yes");
                    var names = rest.Where(a => a != "--yes").ToArray();
                    if (names.Length != 1 || rest.Length != names.Length + (yes ? 1 : 0)) return Fail(Usage);
                    Reconcile();
                    var p = Find(names[0]);
                    if (p.Builtin) return Fail("The built-in profile cannot be removed.");
                    Console.WriteLine($"This signs {p.DisplayName} out, deletes its login file, and moves its folder, history and backups to the Recycle Bin. Other profiles stay signed in.");
                    if (store.DefaultProfile().Id == p.Id) Console.WriteLine("It is the default, so the built-in profile becomes the default.");
                    switch (Confirm($"Remove {p.DisplayName}?", yes))
                    {
                        case false: return Ok("Not removed.");
                        case null: return Fail("Not removed. Without a terminal to confirm on, use --yes.");
                    }
                    var r = new ProfileRemover(store).Remove(p);
                    foreach (var w in r.Warnings) Console.WriteLine(w);
                    return Ok($"Removed {p.DisplayName}.");
                }
                case "uninstall":
                {
                    var yes = rest is ["--yes"];
                    if (rest.Length != 0 && !yes) return Fail(Usage);
                    Reconcile();
                    var added = store.Load().Profiles.Count(p => !p.Builtin);
                    Console.WriteLine($"This signs out {added} added profile(s) and moves .claude-profiles to the Recycle Bin, with their history and backups. " +
                                      "It takes the bin folder out of your user PATH, the completion line out of your PowerShell profile, and Claude Profiles out of Start with Windows. " +
                                      "~\\.claude and your current login are not touched. Exit the Claude Profiles app first.");
                    switch (Confirm("Uninstall?", yes))
                    {
                        case false: return Ok("Nothing changed.");
                        case null: return Fail("Nothing changed. Without a terminal to confirm on, use --yes.");
                    }
                    var u = new ProfileRemover(store).Uninstall();
                    foreach (var w in u.Removed.SelectMany(r => r.Warnings)) Console.WriteLine(w);
                    foreach (var f in u.CompletionRemoved) Console.WriteLine($"Took the completion line out of {f}.");
                    if (u.PathRemoved) Console.WriteLine($"Took {locator.BinDir} out of your user PATH.");
                    if (u.BinLeft)
                        Console.WriteLine(u.BinDeletedLater
                            ? $"{locator.BinDir} is in use by this command, so it is deleted a few seconds after it exits."
                            : $"{locator.BinDir} is in use. Delete it yourself once nothing is running from it.");
                    Console.WriteLine("Uninstalled. Open a new terminal window, where plain claude runs Claude Code directly. " +
                                      "To remove the app too, use Settings > Apps > Installed apps.");
                    return 0;
                }
                default:
                    return Fail(Usage);
            }
        }
        catch (ProfileException e)
        {
            return Fail(e.Message);
        }
    }

    /// True to go ahead, false when the user said no, null when there is no terminal to ask on.
    static bool? Confirm(string question, bool yes)
    {
        if (yes) return true;
        if (Console.IsInputRedirected) return null;
        Console.Write($"{question} [y/N] ");
        return (Console.ReadLine()?.Trim().ToLowerInvariant()) is "y" or "yes";
    }

    static int Ok(string message)
    {
        Console.WriteLine(message);
        return 0;
    }

    static int Fail(string message, int code = 1)
    {
        Console.Error.WriteLine(message);
        return code;
    }
}
