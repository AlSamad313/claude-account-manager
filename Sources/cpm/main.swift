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
      cpm doctor [--fix]                              check the setup
      cpm install-shell                               install the shell layer
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
    let r = try Reconciler(store: store, login: login).run()
    for n in r.loginNotices + r.accountNotices { print(n) }
    for n in r.rowsWithoutFolder { print("\(n): its folder is missing. Run: cpm doctor") }
}

func shellQuote(_ s: String) -> String {
    "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'"
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
        let me = URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath()
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
} catch {
    fail(error.localizedDescription)
}
