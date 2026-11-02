import Foundation
import ProfileCore

func usage() -> Never {
    print("""
    Usage:
      cpm add <displayName> <commandName> <#RRGGBB>   add a profile, then sign in with claude-as
      cpm keep <commandName>                          finish a sign-in
      cpm retry <commandName>                         sign out and sign in again
      cpm discard <commandName>                       remove a profile waiting for a sign-in
      cpm default [<commandName>]                     show or set the default profile
      cpm list [--profile <commandName>] [--live]     list sessions, tab-separated
      cpm new <commandName> [folder]                  open a Terminal session on a profile
      cpm resume <sessionId>                          resume on the profile that owns it
      cpm stop <sessionId> [--yes]                    stop a running session
      cpm code <commandName> [folder]                 open VS Code on a profile
      cpm remove <commandName> [--yes]                sign out a profile and move its folder to the Trash
      cpm doctor [--fix]                              check the setup
      cpm install-shell                               install the shell layer
      cpm uninstall [--yes]                           remove all added profiles and the shell layer
    """)
    exit(1)
}

func fail(_ message: String, code: Int32 = 1) -> Never {
    FileHandle.standardError.write(Data((message + "\n").utf8))
    exit(code)
}

let locator = ConfigLocator.current()
let store = ProfileStore(locator: locator)
let login = LoginCoordinator(store: store)
let args = Array(CommandLine.arguments.dropFirst())
guard let command = args.first else { usage() }
let rest = Array(args.dropFirst())

func profile(_ name: String) throws -> Profile {
    guard let p = try store.load().profile(commandName: name) else { throw ProfileError.notFound(name) }
    return p
}

func reconcile() throws {
    EventLog(locator: locator).rotate()
    let r = try Reconciler(store: store, login: login, remover: ProfileRemover(store: store)).run()
    for n in r.removalsResumed { print("Finished removing \(n).") }
    for n in r.loginNotices + r.accountNotices { print(n) }
    for n in r.rowsWithoutFolder { print("\(n): its folder is missing. Run: cpm doctor") }
}

func shellQuote(_ s: String) -> String {
    "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'"
}

/// Asks on a terminal. Without one, only --yes goes ahead.
func confirmed(_ question: String, yes: Bool) -> Bool {
    if yes { return true }
    guard isatty(STDIN_FILENO) == 1 else { fail("Nothing changed. Without a terminal to confirm on, use --yes.") }
    print("\(question) [y/N] ", terminator: "")
    return ["y", "yes"].contains(readLine()?.lowercased() ?? "")
}

func printWarnings(_ r: RemovalResult) {
    for w in r.warnings { print("Warning: \(w)") }
}

func tabSafe(_ s: String) -> String {
    s.replacingOccurrences(of: "\t", with: " ").replacingOccurrences(of: "\n", with: " ")
}

/// Uses the display list, so a desktop app session cannot be resumed or stopped from here.
func findRow(_ id: String, owner: Profile, index: SessionIndex) -> SessionRow? {
    index.sessions(for: [owner]).rows.first { $0.sessionId == id }
}

func printDoctor(_ report: DoctorReport) {
    for f in report.findings {
        let mark: String
        switch f.level {
        case .ok: mark = "ok   "
        case .note: mark = "note "
        case .fault: mark = "FAULT"
        }
        print("\(mark) \(f.message)")
        if let fix = f.fix { print("      fix: \(fix)") }
    }
}

do {
    switch command {
    case "add":
        guard rest.count == 3 else { usage() }
        try reconcile()
        let p = try store.create(displayName: rest[0], commandName: rest[1], color: rest[2])
        let cmd = try login.begin(profile: p)
        print("Added \(p.displayName). Sign in with:\n  \(cmd)")
        print("Check the browser is signed into the account you want, or use a private window. Then run:\n  cpm keep \(p.commandName)")

    case "keep":
        guard rest.count == 1 else { usage() }
        try reconcile()
        let result = try login.keep(profile: try profile(rest[0]))
        print("Kept: \(rest[0]) is signed in as \(result.account.email ?? "an account").")
        if let d = result.duplicateOf, let other = try store.load().profile(id: d) {
            print("Warning: \(other.displayName) already uses this account, so both share its usage limits.")
        }
        if let w = result.keychainWarning { print("Warning: \(w)") }

    case "retry":
        guard rest.count == 1 else { usage() }
        try reconcile()
        let cmd = try login.retry(profile: try profile(rest[0]))
        print("Signed out. Sign in again with:\n  \(cmd)\nThen run:\n  cpm keep \(rest[0])")

    case "discard":
        guard rest.count == 1 else { usage() }
        try reconcile()
        try login.discard(profile: try profile(rest[0]))
        print("Discarded \(rest[0]). Its folder is in the Trash.")

    case "default":
        try reconcile()
        if rest.isEmpty {
            print(try store.defaultProfile().commandName)
        } else if rest.count == 1 {
            let p = try profile(rest[0])
            try store.setDefault(id: p.id)
            print("Plain claude now uses \(p.displayName). Running sessions are not affected.")
        } else { usage() }

    case "list":
        var live = false
        var filter: String?
        var i = 0
        while i < rest.count {
            if rest[i] == "--live" { live = true; i += 1 }
            else if rest[i] == "--profile", i + 1 < rest.count { filter = rest[i + 1]; i += 2 }
            else { usage() }
        }
        try reconcile()
        let reg = try store.load()
        if let filter, reg.profile(commandName: filter) == nil { throw ProfileError.notFound(filter) }
        let profiles = reg.profiles.filter { filter == nil || $0.commandName == filter }
        let result = SessionIndex(locator: locator).sessions(for: profiles)
        print(["PROFILE", "STATUS", "TITLE", "FOLDER", "SESSION"].joined(separator: "\t"))
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd HH:mm"
        for r in result.rows where !live || r.live {
            let status = r.live ? "running \(r.status ?? "")".trimmingCharacters(in: .whitespaces) + (r.mayBeStale ? " (may be stale)" : "") : f.string(from: r.lastActivity)
            let cmd = reg.profile(id: r.profileId)?.commandName ?? r.profileId
            print([cmd, status, r.title, r.cwd ?? "-", r.sessionId].map(tabSafe).joined(separator: "\t"))
        }

    case "new":
        guard (1...2).contains(rest.count) else { usage() }
        try reconcile()
        let folder = rest.count == 2 ? URL(fileURLWithPath: rest[1]) : nil
        try Launcher(store: store).newSession(profile: try profile(rest[0]), folder: folder)
        print("Opened Terminal on \(rest[0]).")

    case "code":
        guard (1...2).contains(rest.count) else { usage() }
        try reconcile()
        let folder = rest.count == 2 ? URL(fileURLWithPath: rest[1]) : nil
        try Launcher(store: store).openVSCode(profile: try profile(rest[0]), folder: folder)
        print("Opened VS Code on \(rest[0]).")

    case "resume":
        guard rest.count == 1 else { usage() }
        try reconcile()
        let reg = try store.load()
        let index = SessionIndex(locator: locator)
        guard let owner = index.ownerOf(sessionId: rest[0], in: reg.profiles) else { throw LauncherError.noOwner(rest[0]) }
        guard let row = findRow(rest[0], owner: owner, index: index) else { fail("That session belongs to the desktop app. Resume it there.") }
        try Launcher(store: store).resume(session: row, owner: owner)
        print("Opened Terminal on \(owner.commandName), resuming \(row.title).")

    case "stop":
        let yes = rest.contains("--yes")
        let ids = rest.filter { $0 != "--yes" }
        guard ids.count == 1, rest.count == ids.count + (yes ? 1 : 0) else { usage() }
        try reconcile()
        let reg = try store.load()
        let index = SessionIndex(locator: locator)
        guard let owner = index.ownerOf(sessionId: ids[0], in: reg.profiles),
              let row = findRow(ids[0], owner: owner, index: index) else { fail("That session is not running, or belongs to the desktop app.") }
        let result = SessionStopper(locator: locator).stop(row: row) { req in
            print("Stop \"\(req.title)\" on \(owner.displayName), in \(req.folder ?? "unknown folder"), \(req.status ?? "status unknown")?")
            if let w = req.warning { print(w) }
            if yes { return true }
            guard isatty(STDIN_FILENO) == 1 else { fail("Not stopped. Without a terminal to confirm on, use --yes.") }
            print("Stop this session? [y/N] ", terminator: "")
            return ["y", "yes"].contains(readLine()?.lowercased() ?? "")
        }
        switch result {
        case .alreadyStopped: print("That session is not running.")
        case .cancelled: print("Not stopped.")
        case .stopped(let signal): print("Stopped with \(signal). The Terminal window stays open.")
        case .didNotStop(let pid): fail("It did not stop. Its process ID is \(pid).", code: 2)
        }

    case "remove":
        let yes = rest.contains("--yes")
        let names = rest.filter { $0 != "--yes" }
        guard names.count == 1, rest.count == names.count + (yes ? 1 : 0) else { usage() }
        try reconcile()
        let p = try profile(names[0])
        guard !p.builtin else { throw ProfileError.notAllowed("The built-in profile cannot be removed.") }
        print("This signs \(p.displayName) out, and moves its folder, history and backups to the Trash. Other profiles stay signed in.")
        if try store.defaultProfile().id == p.id { print("It is the default, so the built-in profile becomes the default.") }
        guard confirmed("Remove \(p.displayName)?", yes: yes) else {
            print("Not removed.")
            break
        }
        let result = try ProfileRemover(store: store).remove(profile: p)
        printWarnings(result)
        print("Removed \(p.displayName).")

    case "uninstall":
        let yes = rest == ["--yes"]
        guard rest.isEmpty || yes else { usage() }
        try reconcile()
        let added = try store.load().profiles.filter { !$0.builtin }
        print("This signs out \(added.count) added profile(s), and moves ~/.claude-profiles to the Trash, with their history and backups. ~/.claude and your current login are not touched. Quit the Claude Profiles app first, or it will recreate that folder.")
        guard confirmed("Uninstall?", yes: yes) else {
            print("Nothing changed.")
            break
        }
        let result = try ProfileRemover(store: store).uninstall()
        result.removed.forEach(printWarnings)
        print("Uninstalled. Now remove these two lines yourself, then open a new Terminal window:\n")
        print("~/.zshenv:\n  \(result.zshenvLine)\n")
        print("~/.zshrc:\n  \(result.zshrcLine)")

    case "doctor":
        // Without --fix, doctor must see faults rather than repair them.
        if rest == ["--fix"] {
            try reconcile()
            print("Rewrote default and names from the profile list.")
        } else if !rest.isEmpty { usage() }
        let report = Doctor(locator: locator).run()
        printDoctor(report)
        exit(report.hasFaults ? 2 : 0)

    case "install-shell":
        guard rest.isEmpty else { usage() }
        // Not arguments[0], which is just "cpm" when it was found on PATH.
        guard let me = Bundle.main.executableURL?.resolvingSymlinksInPath() else { fail("Cannot tell where cpm is running from.") }
        let result = try ShellInstaller(store: store).install(from: me.deletingLastPathComponent())
        print("Installed to \(locator.binDir.path).\n")
        print("Add one line to the end of each file, on a line of its own:\n")
        print("~/.zshenv:\n  \(result.zshenvLine)\n")
        print("~/.zshrc:\n  \(result.zshrcLine)\n")
        print("Or paste these. They add a newline first, because a file may not end with one:\n")
        print("  printf '\\n%s\\n' \(shellQuote(result.zshenvLine)) >> ~/.zshenv")
        print("  printf '\\n%s\\n' \(shellQuote(result.zshrcLine)) >> ~/.zshrc\n")
        print("Then open a new Terminal window and run: cpm doctor")

    default:
        usage()
    }
} catch let e as ProfileError {
    fail(e.description)
} catch let e as LauncherError {
    fail(e.description)
} catch {
    fail(error.localizedDescription)
}
