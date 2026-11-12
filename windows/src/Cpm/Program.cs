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
          cpm default [<commandName>]                     show or set the default profile
          cpm doctor [--fix]                              check the setup
          cpm install-shell                               install claude, claude-as and cpm, and put them first in your PATH
          cpm uninstall-shell                             take them out of your PATH again

        Coming in later stages of the Windows build: list, new, resume, stop, code, remove, uninstall.
        """;

    static readonly string[] Later = ["list", "new", "resume", "stop", "code", "remove", "uninstall"];

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
            var r = new Reconciler(store, login).Run();
            foreach (var n in r.LoginNotices.Concat(r.AccountNotices)) Console.WriteLine(n);
            foreach (var n in r.RowsWithoutFolder) Console.WriteLine($"{n}: its folder is missing. Run: cpm doctor");
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
                    if (rest.Length != 0) return Fail(Usage);
                    var me = Environment.ProcessPath ?? throw new ProfileException("Cannot tell where cpm is running from.");
                    var r = new ShellInstaller(store).Install(Path.GetDirectoryName(me)!);
                    Console.WriteLine($"Installed to {locator.BinDir}.");
                    Console.WriteLine(r.PathChanged
                        ? "Put it first in your user PATH. The old value is in logs\\writes.log."
                        : "It was already first in your user PATH.");
                    Console.WriteLine("Open a new terminal window to use claude-as, then run: cpm doctor");
                    return 0;
                }
                case "uninstall-shell":
                {
                    if (rest.Length != 0) return Fail(Usage);
                    var removed = new UserPath(locator).Remove();
                    Console.WriteLine(removed
                        ? $"Took {locator.BinDir} out of your user PATH. Open a new terminal window for plain claude to run Claude Code directly. Profiles and their files are unchanged."
                        : $"{locator.BinDir} was not in your user PATH. Nothing changed.");
                    return 0;
                }
                default:
                    if (Later.Contains(command)) return Fail($"cpm {command} is not in this build yet. It comes in a later stage of the Windows build.");
                    return Fail(Usage);
            }
        }
        catch (ProfileException e)
        {
            return Fail(e.Message);
        }
    }

    static int Fail(string message, int code = 1)
    {
        Console.Error.WriteLine(message);
        return code;
    }
}
