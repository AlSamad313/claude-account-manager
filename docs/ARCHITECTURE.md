# Architecture

How the code fits together, and the rules it has to keep. For the reasoning behind each decision, see [the design](designs/claude-profile-manager.md).

This page describes the macOS code. The Windows version in `windows/` has its own code and keeps the same safety rules. [Its design](designs/claude-profile-manager-windows.md) lists what differs, such as the login being a file instead of a Keychain item.

## The main idea

Claude Code keeps its login and data in one folder, which `CLAUDE_CONFIG_DIR` can point elsewhere. It names the Keychain item after that folder, so each folder has its own login.
A profile is therefore one folder, plus a row in a list. Everything else in this project makes that easy to use.

```text
~/.claude                       the built-in profile, never written to
~/.claude-profiles/
  profiles.json                 the list of profiles (the source of truth)
  default, names                plain-text copies the shell wrapper reads quickly
  bin/                          claude, claude-as, cpm and helper scripts
  profiles/<id>/                one added profile's Claude Code folder
  backups/<id>/                 Copy setup backups
  logs/                         launch.log, writes.log
  run/                          short-lived .command files
```

## The layers

```text
 ClaudeProfiles app    cpm command        shell/bin wrappers
 (SwiftUI)             (Sources/cpm)      (zsh: claude, claude-as)
        |                   |                     |
    ProfileUI               |                     |
        |                   |                     |
        +----- ProfileCore -+                     |
                   |                              |
                   +---- ~/.claude-profiles ------+
                                 |
                          real claude binary
```

- **`shell/bin/`** decides which folder a `claude` command uses, then runs the real binary. It has no Swift dependency and must stay fast, because it runs on every `claude` call.
- **`ProfileCore`** holds all the logic, with no UI.
- **`ProfileUI`** holds `AppModel` and the views. `AppModel` calls `ProfileCore` and runs slow work on a background queue.
- **`Sources/cpm`** is the command line, and **`Sources/ClaudeProfiles`** is the app's entry point.

## Where things live in `ProfileCore`

| Area | Files |
|---|---|
| Profile list | `Model/Profile.swift`, `ProfileStore.swift`, `Reconciler.swift` |
| Paths and accounts | `ConfigLocator.swift`, `AccountReader.swift` |
| Signing in | `LoginCoordinator.swift`, `KeychainProbe.swift` |
| Sessions | `SessionIndex.swift`, `LiveSessionReader.swift`, `TranscriptReader.swift`, `SessionStopper.swift` |
| Launching | `Launcher.swift` |
| Running commands | `ProcessRunner.swift`, `ClaudeBinaryLocator.swift`, `ClaudeCLI.swift` |
| Copy setup | `SetupCopier/` |
| Removal and install | `ProfileRemover.swift`, `ShellInstaller.swift` |
| Checks | `Doctor.swift`, `CompatCheck.swift` |
| Support | `AtomicFile.swift`, `EventLog.swift`, `FileWatcher.swift`, `StatusLineWriter.swift` |

## Safety rules

Every change must keep these. Many have tests, and a pull request that breaks one won't be merged.

**Logins and data**

1. **Never write to `~/.claude` or `~/.claude.json`.** The built-in profile can be copied from, but never into, and can't be removed.
2. **Never read login tokens, or the `.key` files beside session records.** List Keychain item names only. Delete only the item recorded for a profile being removed, and never the plain `Claude Code-credentials` item.
3. **Never put a secret in a log, an error message or the screen.** `env` and `headers` values stay masked, whatever they look like.
4. **Never remove `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN` or `CLAUDE_CODE_OAUTH_TOKEN`.** Warn instead.
5. **Delete by moving to the Trash,** so a mistake can be undone.

**Choosing the account**

6. **Fail closed.** If `default` or `names` can't be read, plain `claude` refuses to start rather than guess.
7. **Nested calls stay on their profile.** A `claude` run from inside a session keeps that session's account, through `CLAUDE_PROFILE_ID`.
8. **A session resumes only on the profile that owns it.**
9. **No login-wide environment setting.** `launchctl setenv` splits the desktop app's files between folders ([S0 check 12](S0-results.md)).

**Running things**

10. **Quote every value that reaches a shell.** A `.command` file may only change folder and run `claude-as`, and a folder value is used only as `"$dir"`. Folder names come from transcripts, so treat them as hostile.
11. **Never edit the user's shell files.** Print the lines for them to add.

**Destructive actions**

12. **Refuse while a session is running.** Copy, Restore, removal and uninstall check `hasLiveSession`. This includes desktop app sessions, even though the list hides them, and an unreadable recent record counts as running.
13. **Copy only what's on the allow-list.** Account-synced content such as `skills/synced` and `plugins/synced` is never offered.
14. **Restore never discards later work.** A file changed since the copy is left alone.

**Tests**

15. **Tests never touch the real home folder.** `TestGuard` enforces this. Anything real runs only behind `CPM_REAL_*` variables.

## Depending on Claude Code

These aren't public APIs, and they may change:

- `sessions/<pid>.json`: pid, start time, session ID, folder, status, entry point
- `projects/*/*.jsonl`: transcripts, including the title lines
- `.claude.json`: account details and `mcpServers`
- The Keychain naming rule, and the commands `claude auth`, `claude mcp` and `claude plugin`

When reading any of these, skip what you can't parse for display, but block on it for safety. `cpm doctor` reports unreadable transcripts. [S0-checklist.md](S0-checklist.md) has the checks to rerun after a Claude Code update.

## Test layout

| Location | Covers |
|---|---|
| `Tests/ProfileCoreTests` | Core logic, with fakes for processes, Keychain and Terminal |
| `Tests/ProfileUITests` | `AppModel` flows, the icon, the Guide, and optional view renders |
| `ShellTests/test_wrapper.zsh` | The wrappers, with a temporary `HOME` and a stub `claude` |
| `ShellTests/test_cpm.zsh` | The built `cpm`, end to end, with a temporary `HOME` |
| [E2E-checklist.md](E2E-checklist.md) | What only a person can check |
