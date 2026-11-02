# Claude Profile Manager (macOS)

*   **Author:** Abdul Samad Shahid
*   **Status:** Proposed. Review concluded 2026-11-02. The 4 blockers in
    [`implementation_readiness_report_round2.md`](implementation_readiness_report_round2.md)
    are fixed; its 25 major and 31 minor findings are still open. These fixes have not
    had a further independent review.
*   **Date:** 2026-11-02

--------------------------------------------------------------------------------

## Overview

Claude Profile Manager is a small native macOS app for one person who uses
several Claude accounts on one Mac. It works like browser profiles: each
profile is one Claude account with its own Claude Code config folder, and every
profile stays logged in at the same time. The app lives in the menu bar. From
it the user can see every Claude Code CLI session on every profile, start a new
Terminal.app session on any profile, resume an old session on the profile that
owns it, choose which profile plain `claude` uses, and copy setup (settings,
MCP servers, skills, plugins, hooks) from one profile to another. It can also
stop a running session.

The existing `~/.claude` folder, and the account logged into it today, becomes
the first profile and is not moved or changed. Plain `claude` follows the default
profile wherever the wrapper reaches it (the table in flow 5 lists exactly where),
and the app can open VS Code on a chosen profile.

Source: [`concepts_brief.md`](concepts_brief.md), the brainstorm brief.

## The Problem

Claude Code keeps one login per config folder. Today the author has one config
folder (`~/.claude`) and several accounts (an official company account and a
personal account, with more expected). Using a different account means logging
out and logging back in. This causes three problems:

1.  **Switching is slow and breaks flow.** Every switch is a logout, a browser
    login, and a wait. Sessions on the other account cannot run at the same
    time.
2.  **Sessions are hard to find.** Claude Code's own `/resume` list shows only
    the sessions of the folder in use. There is no single place that shows
    which sessions exist, on which account, in which project folder, and which
    are running right now.
3.  **Work ends up on the wrong account.** Nothing in a Terminal window shows
    which account it is using, so it is easy to start work on the wrong one.

Claude Code already supports separate config folders through the
`CLAUDE_CONFIG_DIR` environment variable, and each folder keeps its own login.
What is missing is a way to create and name these folders, pick between them,
see all their sessions together, and keep their setups in step. The proposed
solution is a menu-bar app and a shell command that manage a list of profiles
on top of that variable, without changing how Claude Code itself works.

## Goals

*   **Many accounts, no logging out.** One Claude account per profile, all
    logged in at the same time and still logged in after a reboot. There is no
    fixed limit on profiles; three or more are tested, and Limits gives the
    design target.
*   **The app never writes to `~/.claude` or `~/.claude.json`.** They and their
    Keychain login become the built-in first profile, unchanged. The app labels
    it from the account email and organization that Claude Code already stores.
*   **Browser-style profile control.** In the app, the user can add a profile
    and log it in, rename it (display name only), pick a colour, set it as the
    default, and remove it.
*   **One list of sessions.** The menu bar shows profiles, the default, and
    recent and running Claude Code CLI sessions for all profiles, each tagged
    with its profile. A new session appears within 5 seconds; a closed one is
    shown as stopped within 10 seconds.
*   **Start, resume and stop on the right profile.** "New Terminal" opens
    Terminal.app on a chosen profile. "Resume" always reopens a session on the
    profile that owns it, in the folder it was started in. "Stop" ends a running
    session after confirmation, from the app or with `cpm stop`.
*   **A full window as well as the menu bar.** "Open window…" shows the same
    profiles and sessions in a normal window with a profile sidebar.
*   **Plain `claude` follows the default wherever the wrapper reaches it.**
    `claude-as <name>` uses a named profile. Both work when the app is not running.
    Flow 5 lists every case.
*   **VS Code on a chosen profile.** "Open VS Code" in the app, or `cpm code <name>`,
    opens VS Code so its Claude Code uses that profile (flow 12). Opened from the
    Dock, it uses the built-in profile.
*   **The current profile is visible.** Every Terminal window running `claude`
    or `claude-as` shows the profile in its title (S0 check 19). Each added
    profile's Claude Code status line also shows it. The built-in profile gets no
    status line, because that would mean writing to `~/.claude/settings.json`;
    built-in sessions started without the wrapper show no label.
*   **Copy setup between profiles.** The user picks items from one profile to
    copy into another added profile and sees a preview first. Direct file writes
    can be undone from a backup; changes made by Claude Code's own install
    commands show how to remove them. Values that look like secrets are left out
    unless the user ticks them. Flow 6 lists what can be copied.
*   **Problems can be diagnosed.** A `doctor` check, a health line per profile
    (flow 11), and logs of launches, copies, deletions, default changes and stops.
*   **The author can own the code.** Plain Swift in a git repository that the
    author can read, build and change, with a README covering build, signing,
    and what to do when Claude Code updates.

## Non-Goals

*   **Desktop app and Cowork conversations.** The app does not list or manage
    them. Opening the desktop app on a chosen account is a later stage and only
    if a test shows it is possible.
*   **Remote Control account handling** in v1. It needs its own test first.
*   **Moving running programs to a new default.** A session or app that is
    already running keeps the profile it started with.
*   **Copying claude.ai connectors** (Gmail, Drive, Figma and similar) **or
    account-synced plugins.** They belong to the account on Anthropic's servers,
    not to the local folder. Each account connects them itself on claude.ai.
*   **Two profiles on the same account.** The app warns about it but does not
    support it as a feature.
*   **Terminals other than Terminal.app.**
*   **Per-profile Terminal colour themes**, which need AppleScript.
*   **Copying into the built-in profile** in v1. It can be a source only.
*   **Tags, full-text search of transcripts, and usage display** in v1.
*   **Changing Claude Code's own records.** The app never writes transcripts or
    `sessions/` files. It writes into Claude Code's files in two cases only: copy
    setup, into the items listed in flow 6, and the `statusLine` written when an
    added profile is created.
*   **Distribution.** No other users, no notarization, no App Store.

## Open Questions

*   **Unverified Claude Code behaviour:** see the S0 table in the Technical
    Plan. The design treats each row as an assumption until its check passes.

## Decisions

Made by the author on 2026-11-01:

*   Stop session is in v1, in both the app and `cpm stop` (flow 9).
*   The shell refuses `--resume` with a session ID owned by another profile
    and names the owning profile (flow 1).
*   The named command is `claude-as <name>`.
*   The built-in profile gets no status line (see rejected Alternative H).
*   Plain `claude` follows the default through a wrapper on `PATH` (flow 5).
*   No login-wide setting, because S0 check 12 showed it splits the desktop app's
    session files. VS Code gets a per-profile launch instead (flow 12; see rejected
    Alternative K). Chosen by Claude at the author's request, 2026-11-02.
*   `ANTHROPIC_API_KEY` and the other login variables are never removed. A launch
    where one is set prints a warning (flow 1).
*   What copy setup may copy is fixed by one table (flow 6).
*   The built-in profile is a copy source only in v1.
*   If plain `claude` cannot read its default, it stops rather than using the
    built-in profile (flow 1).
*   A profile's command name is fixed when it is created. Rename changes only the
    display name.
*   `.command` files call `claude-as`, so the launch rules exist once, in the wrapper
    (flow 2; see rejected Alternative M). The app needs the shell layer installed
    before it can launch anything.
*   Added profiles live in `~/.claude-profiles/profiles/<id>/`, apart from the app's
    own folders.

## Critical User Journeys (CUJs)

### CUJ 1: Add a second account

*   **Given**: The app is installed. Only the built-in profile exists, logged
    into the company account.
*   **When**: The user chooses "Add profile…", enters a display name, a command
    name and a colour, and logs in with the personal account in the Terminal
    window the app opens.
*   **Then**: Within 5 seconds of login the app shows the detected email and
    organization and asks whether to keep the login. The new profile appears in the
    menu with its colour. The built-in profile is still logged in. If the
    login used an account another profile already has, the app warns.

### CUJ 2: Start a session on a chosen profile

*   **Given**: Two or more profiles are logged in.
*   **When**: The user clicks "New Terminal" on the personal profile, or runs
    `claude-as personal` in a Terminal.app window.
*   **Then**: With "New Terminal", a new Terminal.app window opens; with the
    command, the session starts in that window. Either way the window title and
    Claude Code status line show the personal profile, and the session appears
    in the app's list tagged with that profile within 5 seconds.

### CUJ 3: Find and resume an old session

*   **Given**: Sessions exist on several profiles in several project folders.
*   **When**: The user opens the menu bar panel, finds a session (filter by
    text or profile), and clicks "Resume".
*   **Then**: Terminal.app opens in that session's original folder, on the
    profile that owns the session, and the conversation continues.

### CUJ 4: Change the default profile

*   **Given**: The built-in profile is the default and a session is running on
    it.
*   **When**: The user sets the personal profile as default in the app.
*   **Then**: The running session is unaffected. The next plain `claude` reached
    by the wrapper starts on the personal profile, with no shell reload. Apps opened
    from the Dock, and the desktop app, are not affected. The menu
    bar icon colour changes to the personal profile's colour.

### CUJ 5: Copy setup to a new profile

*   **Given**: The built-in profile has MCP servers, skills and a `CLAUDE.md`.
    The personal profile has no running sessions.
*   **When**: The user chooses "Copy setup…", picks the built-in profile as the
    source and the personal profile as the target, and ticks items.
*   **Then**: A preview lists each item as Add, Change or Same, flags items
    containing secrets (unticked) and MCP servers that will need a new login.
    After confirming, the app backs up the target, writes only the ticked
    items, and offers Restore. The target's status line still names the
    target. If the target has a running session, the copy is refused.

### CUJ 6: Diagnose a wrong-account session

*   **Given**: The user suspects a session is on the wrong account.
*   **When**: The user opens "Doctor" in the app or runs it in the shell.
*   **Then**: It reports, per profile, the folder, the logged-in email, the
    Keychain login present or missing, the Claude Code version, and whether `ANTHROPIC_API_KEY` or another login variable is set (S0 check 13),
    naming the specific fault.

### CUJ 7: Stop a busy session

*   **Given**: A session is running on a profile and Claude Code is working.
*   **When**: The user clicks "Stop" or runs `cpm stop <sessionId>`.
*   **Then**: A confirmation shows the profile, folder and title, and warns that
    a file edit may be left unfinished. After confirming, the session ends and
    its row shows as stopped within 10 seconds. The Terminal window stays open.

### CUJ 8: Remove the default profile

*   **Given**: An added profile is the default and has no live sessions.
*   **When**: The user removes it and confirms what happens to its history.
*   **Then**: The built-in profile becomes the default first. The profile's login
    is removed, its folder goes to the Trash, and the other profiles stay logged
    in. If the profile has a live session, removal is refused.

## Technical Plan (Architecture)

### Terms

*   **Account:** an Anthropic login, identified by `oauthAccount.accountUuid`
    in a config file.
*   **Profile:** a config folder with a fixed ID, plus a display name, a
    command name, a colour and at most one account.
*   **Built-in profile:** `~/.claude`, run with `CLAUDE_CONFIG_DIR` unset, so
    its config file is `$HOME/.claude.json`. It cannot be moved or removed from
    the app.
*   **Added profile:** `~/.claude-profiles/profiles/<id>/`, run with
    `CLAUDE_CONFIG_DIR` set to the exact value stored in its `configDir` field.
    Its config file is assumed to be `<configDir>/.claude.json` (S0 check 4).
*   **Why the path must be exact:** from reading the code in the installed
    2.1.296 binary, not from documentation, Claude Code names a folder's Keychain login with a hash of the `CLAUDE_CONFIG_DIR`
    string, and uses the plain name `Claude Code-credentials` when the variable
    is unset. A different spelling of the same folder (`~`, a trailing slash, a
    symlink) would look for a different login and appear logged out. So
    `configDir` is created once as an absolute path with no `~`, no trailing
    slash and no symlink, in Unicode NFC form, and is then only ever read,
    never rebuilt. The built-in profile has `configDir = null`.
*   **Default:** the profile plain `claude` uses. It is read at every launch.
*   **Session:** a transcript file (session ID plus owning profile), with an
    optional live process found in that profile's `sessions/*.json`.
*   **Live session:** any record in a profile's `sessions/*.json` whose process is
    alive, whatever its `entrypoint`. The display filter that hides desktop app
    and Cowork rows never applies to safety checks.
*   **Wrapper:** the `claude` and `claude-as` scripts in
    `~/.claude-profiles/bin/`, first on `PATH`, which choose a profile and then
    run the real Claude Code binary.
*   **`cpm`:** this project's command-line tool (Claude Profile Manager).
*   **Cowork, Remote Control:** other ways of using Claude. Cowork runs through
    the Claude desktop app, and Remote Control drives a Claude Code session from
    elsewhere.
*   **Stages:** S0 is a backup and a set of checks before any product code. S1 to
    S5 build the product. S6 is optional later work. The stage table in the
    Detailed Implementation Plan lists each.
*   **Name rules:** `commandName` is `[a-z0-9-]{1,24}`, unique, and not `builtin`.
    `displayName` is 1 to 40 characters with no tabs, newlines or control
    characters.

Profile states:

| State | Meaning | Moves to |
| --- | --- | --- |
| `pendingLogin` | Folder created, no login yet | `ready` when `oauthAccount` appears; discarded on Discard |
| `ready` | `oauthAccount` present in the config file | `loggedOut` when it disappears; `deleting` on removal |
| `loggedOut` | The user logged out inside the profile | `ready` when `oauthAccount` returns, through "Sign in" |
| `deleting` | Removal started | Removed; a crash resumes removal at the next start |
| `error` | A row has no folder | Fixed by the action `doctor` names |

`names` holds rows in `pendingLogin`, `ready` and `loggedOut`, so `claude-as` can
sign in to a new profile. It never holds rows in `deleting` or `error`, so
`claude-as` cannot start in a folder being moved to the Trash.

Seed row for the built-in profile: `id` `builtin`, `commandName` `main`,
`displayName` from the account's organization name, otherwise `Main`, and colour
`#2F6FDE`.

Reconcile runs whenever the app or `cpm` starts, except `cpm doctor` without `--fix`,
which must see faults rather than repair them. A folder in
`~/.claude-profiles/profiles/` with no row is listed in `doctor` and never deleted.
A row with no folder becomes `error`. A `deleting` row continues its removal. Reconcile
never completes a login: a `pendingLogin` row whose config file now has `oauthAccount`
is only reported, because the user must choose Keep. `profiles.json` is the source of truth, including the default
(`defaultProfileId`), and `default` and `names` are rewritten from it whenever
they disagree or are missing. The app re-reads each profile's account when the
panel opens and when the config file changes, so a login changed inside a profile
updates the label and shows a notice.

### Components

```text
    ClaudeProfiles.app (menu bar panel + window, LSUIElement)
              │
              ▼
    ProfileUI (SwiftUI views + view models)         cpm (command-line tool)
              │                                        │
              └───────────────────┬────────────────────┘
                                  ▼
  ProfileCore (no SwiftUI/AppKit)
    ConfigLocator  ProfileStore  AccountReader  SessionIndex  Launcher
    ProcessRunner  SessionStopper  SetupCopier  ClaudeCLI  KeychainProbe
    LoginCoordinator  ProfileRemover  ShellInstaller
    CompatCheck  Doctor  EventLog
       │               │              │              │              │
       │ writes        │ reads        │ opens        │ writes       │ names; delete
       ▼               ▼              ▼              ▼              ▼ recorded item
  ~/.claude-profiles/  <profile>/     run/*.command  listed items   macOS Keychain
  profiles.json        projects/      Terminal.app   in an added
  default, names       sessions/*.json               profile

  bin/claude, bin/claude-as ── read default and names on every call
                               (no JSON, app need not be running) ──► real claude binary
```

*   **ProfileCore** is a Swift library with no UI code. Everything that touches
    disk, processes or the Keychain lives here, so the app and the CLI behave
    the same way.
    *   **ConfigLocator** is the single function that turns a profile into
        paths: config folder, config file, and the environment to launch with.
        It reads the stored `configDir`. For the built-in profile it returns
        "unset `CLAUDE_CONFIG_DIR`", because setting that variable to `~/.claude`
        would point Claude Code at a different config file and login. No other
        Swift code builds these paths. The shell scripts read the same value from
        `names`.
    *   **ProfileStore** is the only module that writes `profiles.json`, `default`
        and `names`. It owns the profile states and runs reconcile. The app and
        `cpm` are separate processes, so every change takes a lock on
        `~/.claude-profiles/.lock` and re-reads from disk first.
    *   **LoginCoordinator** owns flow 3.
    *   **ProfileRemover** owns flow 7 and `cpm uninstall`.
    *   **ShellInstaller** owns flow 10.
    *   **SessionIndex** is read-only. It lists transcripts and live processes
        per profile.
    *   **AccountReader** reads only the `oauthAccount` keys from a config file.
    *   **Launcher** is the only module that opens Terminal sessions. It writes
        `.command` files.
    *   **ProcessRunner** is the one Swift API for running external commands. When
        it runs Claude Code, the environment comes from ConfigLocator. CompatCheck,
        ClaudeCLI (install and logout commands), Doctor and Launcher use it.
    *   **SessionStopper** sends the signals in flow 9.
    *   **SetupCopier** builds a copy plan (a diff) and applies it. It writes
        into added profiles only.
    *   **KeychainProbe** lists Keychain item *names* with the
        `Claude Code-credentials` prefix, checks if a name exists, and deletes a
        recorded name. It never reads secret data, and it never deletes the plain
        `Claude Code-credentials` item, which is the built-in login.
    *   **CompatCheck** checks the shape of each Claude Code file the app depends
        on. Other modules ask it `canWrite`.
    *   **Doctor** runs the checks in flow 11.
    *   **EventLog** is the only Swift module that writes `writes.log`, and it
        rotates both logs.
*   **ProfileUI** holds the SwiftUI views and view models. The menu-bar panel
    and the full window share row views and all logic.
*   **ClaudeProfiles.app** is the menu-bar app shell. It ships an "Open
    window…" item in v1.
*   **cpm** is a command-line tool on top of ProfileCore: in S1 `add`, `keep`, `retry`,
    `discard`, `doctor`, `default`, `install-shell`; in S2 `list`, `new`, `resume`, `stop`; in S5
    `remove`, `uninstall`.
*   **`bin/claude` and `bin/claude-as`** are plain zsh scripts, not generated,
    installed to `~/.claude-profiles/bin/` and placed first on `PATH`. `claude`
    uses the default; `claude-as <name>` uses a named profile. They read
    `default` and `names`, then run the real binary.
*   **`bin/claude-profiles.zsh`** is sourced at the end of `~/.zshrc`, if the file
    exists. It puts `~/.claude-profiles/bin` first on `PATH` again and registers
    tab completion.
*   **`bin/profile-label`** is the command each added profile's status line runs,
    with the profile ID. It reads the display name from `names`, so a rename needs
    no settings change.

### Files on disk

```text
~/.claude                     built-in profile folder        (never written by the app)
~/.claude.json                built-in config file           (never written by the app)
~/.claude-profiles/
  profiles.json               registry, mode 0600, schemaVersion
  default                     one line: profile ID, mode 0600
  names                       lines: <commandName><TAB><id><TAB><displayName><TAB><color><TAB><configDir>
                              mode 0600 (configDir is "-" for the built-in profile)
  .lock                       held during each change to the three files above
  bin/                        cpm, claude, claude-as, profile-label, claude-profiles.zsh, VERSION
  profiles/<id>/              an added profile's CLAUDE_CONFIG_DIR
    .claude.json              that profile's config file (S0 check 4)
    settings.json             includes a generated statusLine for this profile
    projects/  sessions/      written by Claude Code, read by SessionIndex
  backups/<id>/<time>/        backups made before each copy; folders 0700, files 0600;
                              the last 10 per profile are kept
  run/                        temporary .command files, mode 0700
  logs/launch.log             one line per launch, written by the wrapper
  logs/writes.log             one line per copy, restore, delete, default change or stop
~/.zshenv                     one added line: puts ~/.claude-profiles/bin first on PATH
~/.zshrc                      one added line at the end: sources claude-profiles.zsh
```

`profiles.json` holds `schemaVersion`, `defaultProfileId` and, per profile: `id`,
`commandName`, `displayName`, `color`, `builtin`, `configDir`, `createdAt`,
`accountUuid`, `email` and `organizationName` (cached copies), `keychainBaseline` and
`loginStartedAt` (used only while waiting for a login), `keychainService` (recorded at first login),
`state` (`pendingLogin`, `ready`, `loggedOut`, `deleting`, `error`) and `lastError`. Every file the app replaces is
written to a temporary file in the same folder and then renamed over the
original, so a reader never sees half a file.

### How the main flows work

#### Flow 1: Launching from the shell

`claude` and `claude-as <name>` are scripts
in `~/.claude-profiles/bin/`, first on `PATH`. They read `default` and `names`
with plain zsh (no `jq`). Each runs as its own process, so nothing it does can
change or close the user's shell.

```text
claude [args]:
  if CLAUDE_CONFIG_DIR is set and outside ~/.claude-profiles/:
    deliberate override: keep it, warn on stderr, export
    CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1, log, exec the real binary. Nothing below runs.
  id := $CLAUDE_PROFILE_ID if set and in names     (nested call: keep the parent's profile)
        else contents of ~/.claude-profiles/default
  default missing, unreadable, or its ID is not in names:
    print the problem and "run cpm doctor", exit 1  (does not start)
claude-as <name> [args]:
  id := look up <name> in ~/.claude-profiles/names
  name not found, or names unreadable: print valid names if any, exit 1
both, in this order:
  if args contain --resume <sid>, --resume=<sid> or -r <sid>, and <sid> is a UUID:
    if <sid>.jsonl is not under this profile's projects/*/ but is under another profile's:
      print "Session <sid> belongs to profile <owner>.
             Use: claude-as <owner-command> --resume <sid>"
      exit 1
  login variables: for each of ANTHROPIC_API_KEY, ANTHROPIC_AUTH_TOKEN and
    CLAUDE_CODE_OAUTH_TOKEN that is set: warn on stderr; leave it set
  if id is built-in: unset CLAUDE_CONFIG_DIR
  else:              export CLAUDE_CONFIG_DIR=<configDir from names>
  export CLAUDE_PROFILE_ID=<id>
  export CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1
  if stdout is a terminal: set the window title to the profile's display name
  append one line to logs/launch.log
  exec the real claude binary with [args]
```

The ownership check needs only a file-name search, so the wrapper reads no JSON. It
does not check whether the session is already running; the app shows Stop instead
of Resume for a live session, and S0 check 22 tests what happens if one session is
resumed twice.

A value inside `~/.claude-profiles/` is never treated as an override, because it
may be inherited from an app opened on a profile, or be a removed profile's old
path.

`CLAUDE_PROFILE_ID` matters because Claude Code and its tools can start
`claude` themselves. Without it, a `claude -p` run from inside a Personal
session would reach the wrapper and move to whatever the default is.

**Finding the real binary.** The wrapper and the app use one rule: search each
`PATH` entry, skip any that resolves to the `~/.claude-profiles/bin` folder, and
use the first `claude` found. The wrapper therefore never calls itself and keeps
working after a Homebrew update. If none is found, it prints an error and stops.

The warning reads, for example: "ANTHROPIC_API_KEY is set. Claude Code may use it
instead of this profile's login." The tool never unsets these variables, so a key
set on purpose keeps working. S0 check 13 tests what each one actually does and
the wording.

#### Flow 2: Launching from the app

Launcher writes a `.command` file to `~/.claude-profiles/run/`, mode 0700, then
runs `/usr/bin/open -a Terminal <file>`, which needs no Apple Events permission
(S0 check 9). The file is exactly:

```text
#!/bin/zsh
dir='<folder>'
cd -- "$dir" || { print -ru2 -- "Folder not found: $dir"; exit 1 }
exec "$HOME/.claude-profiles/bin/claude-as" '<commandName>' [--resume '<sessionId>']
```

The wrapper then does everything in flow 1: environment, warnings, window title,
launch log, ownership check and finding the real binary. The launch rules therefore
exist in one place. Terminal's shell provides `PATH`, which S0 check 14 confirms.
The built-in profile is launched the same way, with its command name `main`.

A folder path comes from transcript data, so it appears once, assigned to `dir` in
single quotes with each `'` written as `'\''`, and is only ever used as `"$dir"`. It
must never be written into double quotes, where `$(...)` would run. This was run on
this Mac: the earlier double-quoted form did execute a command from a folder name, and
this form does not. `commandName` already follows the Name rules in Terms.

If the shell layer is not installed, the app does not launch anything; it offers "Set
up shell" (flow 10). A live row shows Stop only, and any other row shows Resume only.
If a session's folder no longer exists, the app offers "Choose folder". New Terminal
and login windows start in `$HOME`. `.command` files older than 60 seconds are deleted
at the next launch.

The app itself runs Claude Code directly only through ClaudeCLI, for commands such as
logout and install. A GUI app does not get the shell's `PATH`, so for those it runs
`/bin/zsh -lic`, printing a unique marker line and then `$PATH`, with a 5-second
timeout, and applies the real-binary rule in flow 1.

#### Flow 3: Adding a profile and detecting the login

LoginCoordinator owns this flow. The app waits and shows progress. `cpm add` prints the
command to run and exits. Once the login is done, every `cpm` run prints who signed in
and asks for `cpm keep <commandName>`, `cpm retry <commandName>` or `cpm discard
<commandName>`. Nothing completes a login except Keep.

```text
1. User enters display name, command name, colour.
2. ProfileStore creates the folder, writes the statusLine into its settings.json, and
   adds a registry row with state pendingLogin. The row is in names from this point.
3. LoginCoordinator records the current Claude Code-credentials* item names as
   keychainBaseline. Each retry takes a new baseline.
4. Launcher opens Terminal running `claude-as <commandName> auth login`, with the note
   "Check the browser is signed into the account you want, or use a private window."
5. It watches the folder (FSEvents) until .claude.json contains oauthAccount. It waits
   up to 10 minutes. Closing the Terminal window cannot be detected, so Retry and
   Discard stay visible while waiting.
6. It lists Keychain names again, repeating for up to 10 seconds, because the item may
   be written after .claude.json. One new name is stored as keychainService (S0 check 7).
   Zero or several new names: state ready, no recorded name, and a warning that
   removal will need a manual Keychain step. The login itself still works.
7. App shows the detected email and organization: Keep / Sign out and retry.
   accountUuid already used by another profile → warning.
8. State becomes ready. A timeout leaves pendingLogin with Retry / Discard.
```

#### Flow 4: Listing sessions

For each profile, SessionIndex reads
`<profile>/projects/*/*.jsonl` for past sessions and `<profile>/sessions/*.json`
for live ones (only `*.json`, never the `.key` files next to them). For each
transcript it reads only what it needs for a row (session ID, folder, Claude
Code's own title, last change time; title is the record `name`, then `customTitle`,
`aiTitle`, `summary`, then "Untitled") and caches the result in memory by path, size
and change time. A file it cannot parse is skipped and counted in `doctor`, without
hiding the other rows. A live record counts as running only if its `pid` is
alive and its `procStart` matches the process start time macOS reports for that
PID, so a reused PID is never shown as running (S0 check 15). A live record with no
transcript yet still gives a row. Rows with `entrypoint` `claude-desktop` or the
Cowork value (S0 check 16) are hidden from the list; an unknown value is shown.
Safety checks ignore this filter. The app reads all of this when the panel opens and then rechecks live status
every 2 seconds while the panel or window is visible, which meets the 5-second and
10-second goals. Nothing is polled while hidden.

#### Flow 5: Changing the default

The app or `cpm default <name>` calls ProfileStore, which rewrites `default`. The
wrapper reads it at every launch, so the next `claude` uses the new default with no
reload, and running sessions are not touched. The app watches `default` and `names`,
so a change made with `cpm` shows in the menu-bar icon colour.

Where the default applies:

| Started from | Follows the default? |
| --- | --- |
| zsh, new or already open, with the two shell lines added | Yes, through the wrapper, on the next `claude` |
| bash or a script started from those shells | Yes, through the wrapper on `PATH` |
| `bash -l`, after macOS `path_helper` reorders `PATH` | No; uses the built-in profile (S0 check 14) |
| A shell alias for `claude` | No, if the alias calls a full path; the alias wins |
| Calling the full path to `claude` | No; uses the built-in profile |
| cron jobs and launchd jobs | No; use the built-in profile |
| VS Code opened from the Dock, and its Claude Code | No; uses the built-in profile. Use "Open VS Code" (flow 12) |
| Claude desktop app and Cowork | No; they keep their own login and `~/.claude` |
| A session already running | No, by design |
| A process that sets `CLAUDE_CONFIG_DIR` to a folder outside `~/.claude-profiles/` | No; its value wins, with a warning |
| A `claude` call from inside a running session | No, by design; it stays on that session's profile |

#### Flow 6: Copying setup

`SetupCopier.plan(source, target, selection)` returns a
copy plan: each item marked Add, Change or Same, conflicts, secrets found, and
MCP servers that will need a new login. The preview shows the plan. What it
promises depends on how an item is written: direct file writes match the preview
exactly, while Claude Code's own install commands show the intended change and
may also write caches and registries the plan does not list. Rules:

*   The built-in profile can be a source, never a target, in v1.
*   Refuse if the target profile has a live session, and re-check immediately
    before writing. Nothing holds a lock, so a session started during the copy is
    possible; the before-state hashes below catch the common case.
*   Back up every file the plan touches to
    `~/.claude-profiles/backups/<id>/<time>/` first. Each backup has a manifest:
    files changed, files created, and a hash of each file before and after.
*   `apply` re-checks each file's before-state hash and stops if a file changed
    since the preview.
*   If a step fails, stop, keep the backup, and report which items were done and
    which were not.
*   MCP servers: installed only with `claude mcp add-json <name> <json> --scope
    user` under the target's environment (S0 check 8). v1 never writes a config
    file directly. Project-level `mcpServers` are not copied.
*   What may be copied is fixed by this table. Anything not listed is not
    copied and is not offered:

    | Item | Where it lives | How it is copied | Ticked by default |
    | --- | --- | --- | --- |
    | MCP servers | top-level `mcpServers` in the config file | `claude mcp add-json --scope user` | Yes, unless a secret is found |
    | `permissions.allow`, `permissions.deny` | `settings.json` | Lists combined, no duplicates | Yes |
    | `hooks` | `settings.json` | Merged by event and matcher | No |
    | `enabledPlugins`, `extraKnownMarketplaces` | `settings.json` | Added as a set, then installed (below) | Yes |
    | `model` | `settings.json` | Replaces the target's value | No |
    | `env` | `settings.json` | Per variable; values masked | No |
    | Skills, agents, commands | `skills/`, `agents/`, `commands/` | Per file: skip, overwrite or rename | Yes for new files |
    | `CLAUDE.md` | profile folder | Skip, overwrite or rename | No if the target has one |

*   Secrets. Every value in `env` and `headers` is masked, whatever it looks like.
    `args` and free text (skills, agents, commands, hooks, `CLAUDE.md`) are scanned
    with fixed value patterns (file list: SecretDetector). A match in `args` is
    masked. A match in free text flags and unticks the whole file, with no masking
    inside it. An item with a masked value is unticked, and the user can tick it. If
    a secret is left out, that variable is dropped, and the preview says if the
    server may not run without it.
*   A hook or MCP command that points into the source profile's folder is flagged
    and unticked, because it would keep depending on that folder.
*   These can never be ticked in v1: login credentials and tokens stored by Claude
    Code, `apiKeyHelper`, `statusLine`, trust grants (`hasTrustDialogAccepted`, `allowedTools`),
    `oauthAccount`, `projects/`, `sessions/`, caches, plugin registries, and
    `plugins/synced/`.
*   Plugins (second step of copy setup): copy only the `enabledPlugins` and
    `extraKnownMarketplaces` entries, then install with Claude Code's own
    plugin commands under the target's environment (S0 check 8).
*   Restore refuses during a live session. It deletes files the copy created, and
    puts a backed-up file back, with its original mode, only if its hash still equals
    the after-state hash. A file changed since is listed and left alone, so Restore
    never discards later work. Files changed by Claude Code's own install commands
    are not in the manifest, and Restore cannot undo them; the app shows the command
    that removes each one.

#### Flow 7: Removing a profile

ProfileRemover owns this. One confirmation comes first, naming the profile and saying
its folder and backups go to the Trash. Refuse while it has a live session. Then, in
this order, each step safe to repeat: if it is the default, make the built-in profile
the default; mark the row `deleting`; run
`claude auth logout` under that profile's environment (S0 check 6); delete the
recorded `keychainService` item only if it exists and the name matches exactly; move
the folder and its backups to the Trash; remove the row. A crash continues from the
`deleting` state at the next reconcile. The plain `Claude Code-credentials` item is
never deleted. The built-in profile has no remove action.

#### Flow 8: Checking compatibility

v1 writes one Claude Code file format directly: `settings.json` during copy setup.
`CompatCheck.canWriteSettings(file)` is true when the file's top level is a JSON object
and each key the merger touches, if present, has the expected type. A key that is absent
is not a fault, because a new profile's file holds only `statusLine` and the built-in
file holds no `statusLine`. If it is false, SetupCopier refuses to write. Readers do not
need this check: they skip what they cannot parse, and `doctor` counts it. There is no
version list and no banner, because Claude Code updates too often for one to stay
meaningful; `doctor` prints the version as a note. If a profile's recorded
`keychainService` no longer exists after an update, `doctor` says the naming may have
changed and that the fix is to sign in again. After an update, rerun only the S0 checks
whose subject changed.

#### Flow 9: Stopping a session

`SessionStopper.stop(session)` is shared by the app and the CLI:

```text
1. Read the live record; confirm pid is alive and procStart matches. Mismatch → "already stopped".
2. Show confirmation: profile, folder, title, and status (busy/idle, if present).
   Busy → extra line: "Claude Code is working; stopping now can leave a file edit unfinished."
   (cpm stop asks y/N unless --yes.)
3. Send SIGINT. Wait up to 5 seconds, checking every 250 ms.
4. Still alive (same pid + procStart) → send SIGTERM. Wait 5 seconds more.
5. Still alive → report "did not stop" and give the pid. No SIGKILL from the app.
6. Log the result to writes.log.
```

The Terminal window stays open after the process ends; the app does not
close it.

#### Flow 10: Installing and upgrading

ShellInstaller owns this. `make-cli.sh` builds and signs `cpm`, and puts it and the
shell files in `~/.claude-profiles/bin/`. `make-app.sh --install` puts the app in
`~/Applications`, and the app carries the same files as bundle resources. "Set up
shell" in the app, or `cpm install-shell`, runs these steps, each safe to repeat:

```text
1. Create ~/.claude-profiles and the built-in row, if missing. Never reset the default.
2. Copy the files to bin/ from the folder beside the running cpm, or from the bundle.
   Write bin/VERSION.
3. Show two lines for the user to add:
   ~/.zshenv:           path=("$HOME/.claude-profiles/bin" $path)
   end of ~/.zshrc:     [[ -r "$HOME/.claude-profiles/bin/claude-profiles.zsh" ]] && source "$HOME/.claude-profiles/bin/claude-profiles.zsh"
4. doctor confirms claude resolves to the wrapper in a new shell.
```

The tool makes no change to `~/.zshenv` or `~/.zshrc` itself. On upgrade, the app
replaces `bin/` when its bundled version differs from `bin/VERSION`.

#### Flow 11: Running doctor

The app and `cpm doctor` run the same checks. The app sees
its own environment rather than a shell's, so environment checks run in a login
shell.

| Check | Level | Output |
| --- | --- | --- |
| Each profile's folder exists and `configDir` equals the path made for its ID | Fault | Names the profile; state `error` |
| Folder in `~/.claude-profiles/profiles/` with no row | Note | Lists it; never deleted |
| Logged in (`oauthAccount` present), showing the email | Fault | "Not signed in" with the sign-in step |
| Recorded Keychain name exists | Fault | Says the login is missing or the naming changed |
| Claude Code version | Note | Prints the version |
| `default` resolves to a profile | Fault | Names the broken file; `cpm doctor --fix` rewrites it from `profiles.json` |
| Real binary found; `claude` on `PATH` resolves to the wrapper | Fault | Shows both paths |
| Login variables set | Note | Names each variable and why it matters |
| Keychain names with the prefix that match no profile | Note | Lists names only |
| Transcripts that failed to parse | Note | Count per profile |
| `~/.claude/.claude.json` exists | Note | The app never reads it |

A fault gives `cpm doctor` exit code 2. Notes do not affect the exit code.

The per-profile health line in the app is a one-line summary of the first four
checks.

#### Flow 12: Opening VS Code on a profile

"Open VS Code" on a profile row, or `cpm code <commandName> [folder]`, runs `/usr/bin/open
-a "Visual Studio Code" --env CLAUDE_CONFIG_DIR=<configDir> --env CLAUDE_PROFILE_ID=<id>
[folder]`, through Launcher, so nothing changes system-wide. For the built-in profile it
sets only `CLAUDE_PROFILE_ID`. VS Code holds one environment for all its windows, so a
running VS Code would ignore a new value. If VS Code is running, the tool never closes
it: it says which profile it started on, if known, and asks the user to quit it first.
S0 check 25 confirmed both cases on 2026-11-02; `open` itself reports when VS Code was already running.

### Assumptions to confirm in S0 before building

Each of these comes from reading one installed version (2.1.296) or from
memory, not from a test. This table is the only list of S0 checks. Check 11 runs
first. Only the three checks marked "Yes" can stop the project. Checks that need
something built run when it exists: "S1" after the shell scripts, "S3" after
`make-app.sh`. Results go in `docs/S0-results.md`.

| # | When | Assumption | Passes when | Stops the project? | If it fails |
| --- | --- | --- | --- | --- | --- |
| 1 | S0 | Each config folder path gets its own Keychain item | The built-in profile plus two test folders give two new item names, with the plain item unchanged | Yes | The design does not work |
| 2 | S0 | Three or more profiles stay logged in at once, across a reboot | `claude auth status` shows each account after a reboot | Yes | The design does not work |
| 3 | S0 | The built-in profile with the variable unset keeps today's login | Same account shown, before and after the other logins | Yes | Stop and investigate before any change |
| 4 | S0 | An added profile's config file is `<dir>/.claude.json` | `oauthAccount` found there | No | ConfigLocator changes; nothing else does |
| 5 | S0 | `claude --resume <id>` works under a custom folder and from a different working folder, keeping the same session ID | Conversation continues with the same ID | No | Resume is dropped from the app, or runs only from the original folder |
| 6 | S0 | `claude auth login` and `logout` work under a custom folder (both exist in 2.1.296) | Login and logout affect only that folder; record whether logout removes the Keychain item | No | Removal deletes only the recorded Keychain item and warns; "Sign out and retry" becomes a manual step |
| 7 | S0 | One login creates exactly one new prefixed Keychain item | One new name | No | Save no name and warn (flow 3) |
| 8 | S0 | `claude mcp add-json --scope user`, `claude plugin install` and `claude plugin marketplace` work with the variable set (all exist in 2.1.296) | MCP entry lands in that folder's top-level `mcpServers`; plugin installs in that folder | No | Mark that item not copyable |
| 9 | S3 | Opening a `.command` file from a signed app shows no permission prompt | No prompt | No | Accept one prompt, or use AppleScript |
| 10 | S1 | A status line command can show the profile | Label appears in a session | No | Added profiles rely on the window title |
| 11 | S0, first | No existing open-source tool already does this. Time-boxed; each tool recorded as adopt, borrow or reject | Each tool has a recorded result | No | Change the plan to adopt or borrow it |
| 12 | S0 | Done 2026-11-02. A login-wide `launchctl setenv` moves VS Code's Claude Code, but also moves the desktop app's Code tab session files | — | No | Failed on safety, so the login-wide setting was removed from the design |
| 13 | S0 | What `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN` and `CLAUDE_CODE_OAUTH_TOKEN` each do when set alongside a login | Behaviour recorded for each | No | Reword the warning to match what was seen |
| 14 | S1 | `~/.claude-profiles/bin` stays first on `PATH` after macOS `path_helper` and Homebrew's `shellenv`; `claude` resolves to the wrapper in a login shell, a non-login shell, `bash -l` and a script; the real binary is found from a `.command` file; the app's `PATH` read works | Wrapper resolves in each, and the `.command` file reaches the real binary | No | Change where the `PATH` line goes |
| 15 | S0 | `procStart` can be compared with the macOS process start time from `sysctl` | They agree to 1 second for live sessions | No | Use PID-alive only, and mark rows "may be stale" |
| 16 | S0 | The `entrypoint` values for the CLI, desktop app and Cowork, and the `status` values (seen so far: `cli`, `claude-desktop`, `busy`, `idle`) | Cowork value recorded | No | Show all rows except `claude-desktop` |
| 17 | S0 | Done 2026-11-02. Terminal.app started after `launchctl setenv` inherits the value. No longer needed by the design | — | No | — |
| 18 | S0 | SIGINT, then SIGTERM, ends a session cleanly, and the Terminal window stays open | Session ends within 10 seconds; window open | No | Change the wording and the signals in flow 9 |
| 19 | S0 | With `CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1`, the window title stays set for a full session, including after tool use | Title unchanged at the end | No | Return to the author with rejected Alternative I (`--settings`) as the fallback |
| 20 | S0 | `claude auth status --json` gives the account under a custom folder (the command exists in 2.1.296) | Account shown. If it passes, record it as a possible replacement for reading `.claude.json` | No | AccountReader keeps reading `.claude.json` |
| 21 | S3 | A self-signed, unsandboxed app and `cpm` can list these Keychain names without a prompt, and delete an item created by `claude` | No prompt; item deleted | No | Removal prints the manual step |
| 22 | S0 | What Claude Code does when one session is resumed twice | Behaviour recorded | No | If harmful, add a wrapper check that uses `pid` and `procStart` |
| 23 | S0 | File-format key names. Samples local session records, transcripts, `settings.json` and `.claude.json`, names only, and records them in `docs/S0-results.md`, including how often a transcript's title is in its first or last 64 KB | Key names recorded | No | Change the required keys in file 19 and the fixtures in file 30 |
| 25 | S0 | `open -a "Visual Studio Code" --env ...` gives VS Code that value when it is not running. A running VS Code ignores it | Values seen on VS Code's process in each case | No | Drop "Open VS Code"; opening from a shell where `CLAUDE_CONFIG_DIR` is set is the manual route |
| 24 | S0 | `scripts/test.sh` runs a Swift Testing test with Command Line Tools alone | One test passes (done 2026-11-02 in a scratch package) | No | List full Xcode as a requirement |

### Build and signing

One Swift package with targets `ProfileCore`, `ProfileUI`, `ClaudeProfiles`
(the app) and `cpm`, plus test targets; targets are added as their stage arrives.
`make-cli.sh` builds and signs `cpm` and installs the shell files. `make-app.sh`
builds with `swift build`, assembles the `.app` bundle with an `Info.plist` that sets
`LSUIElement`, and signs it. Both use one fixed identity (a free Apple Development
certificate or a self-signed one), so macOS permission grants survive rebuilds.
Scripts were picked over an Xcode project because every part stays a readable text
file that the author and Claude can edit and diff. No notarization, no App Sandbox
(the app must read `~/.claude*` and the process list), no hardened runtime.

## Alternatives Considered

### Alternative A: Shell layer only (no app)

Profile folders, the plain-text `default` and `names` files, `claude-as <name>`,
and a per-profile status line, with nothing else.

*   **Pros**: A small shell script. Removes logging out. Nothing to sign or
    bundle.
*   **Cons**: Sessions are still seen one profile at a time through `/resume`.
    No single list, no live status, no visual default.
*   **Reason for Rejection**: Not rejected outright. Its wrapper scripts are the
    bottom layer of the chosen design. On its own it does not meet the author's
    requirement for a desktop app.

### Alternative B: Shell layer plus a session-list CLI (no GUI)

Alternative A plus a command that lists all profiles' sessions together,
shows which are running, and resumes a session on the profile that owns it.

*   **Pros**: Covers listing, live status and resume with read-only code. Brainstorm
    reviewers judged it enough for the author's aim of seeing all sessions.
*   **Cons**: No menu bar, no window, no visual profile switcher.
*   **Reason for Rejection**: The author requires a desktop app in every case. Its
    logic is kept as `SessionIndex` and `Launcher` in
    ProfileCore, and the `cpm` CLI exposes part of it.

### Alternative C: Use an existing open-source tool

Details are in S0 check 11 in [`S0-results.md`](../S0-results.md). Menu-bar account switchers such as
Claude Switcher swap one active login in place. Profile launchers such as claude-switchboard and
textaccounts use `CLAUDE_CONFIG_DIR` but offer no app or session list. Session apps found are for Linux.

*   **Pros**: No new code if one fits. claude-switchboard may be a usable stopgap until S1.
*   **Cons**: None found combines simultaneous profiles, a session list across them, and a macOS app.
*   **Reason for Rejection**: Rejected on one pass of web searches and one README, without installing
    anything. The plan changes if a better fit turns up.

### Alternative D: Share one `projects/` folder across all profiles

Symlink every profile's `projects/` to one folder so `/resume` shows every
session.

*   **Pros**: Very small. Uses Claude Code's own `/resume` list.
*   **Cons**: Loses which profile owns each session. Makes it easy to resume a
    session on the wrong account.
*   **Reason for Rejection**: It brings back the wrong-account problem the tool
    exists to remove.

### Alternative E: Electron or Tauri app instead of SwiftUI

*   **Pros**: Web technology; Tauri apps are also fairly small.
*   **Cons**: Electron bundles a browser engine and uses more memory when idle.
    Both add a JavaScript layer on top of the macOS calls the app needs
    (Keychain, process list, FSEvents).
*   **Reason for Rejection**: The author asked for a lightweight app, and the app
    is macOS-only, so native SwiftUI does the job with less in between.

### Alternative F: Open Terminal with AppleScript (`do script`)

*   **Pros**: Full control of Terminal windows: tabs, colour themes, closing the
    window.
*   **Cons**: Needs the macOS Automation permission. That grant is tied to the
    app's signature, so with an unstable signature the prompt comes back after
    every rebuild.
*   **Reason for Rejection**: `.command` files opened with `/usr/bin/open` need no
    such permission (S0 check 9). AppleScript stays available for later
    per-profile Terminal colour themes.

### Alternative G: Copy setup by copying config files whole

*   **Pros**: Simple to write.
*   **Cons**: Plugin registries hold absolute paths into the source profile, so
    copied plugins point at the wrong folder. Copying a whole `.claude.json`
    carries over the source account's identity. Writing while Claude Code is
    running can lose one of the writes.
*   **Reason for Rejection**: It damages the target profile. The chosen design
    copies only listed keys and files, and installs MCP servers and plugins
    with Claude Code's own commands (flow 6).

### Alternative H: Show the built-in profile's name by adding a status line to `~/.claude/settings.json`

*   **Pros**: Every built-in session shows its profile, however it was started
    (VS Code, scripts, desktop app).
*   **Cons**: Writes into the built-in profile, which the goals say stays
    untouched. Can collide with a status line the author sets later.
*   **Reason for Rejection**: The author chose to keep `~/.claude` unwritten
    (2026-11-01). The built-in profile is labelled by window title only.

### Alternative I: Pass the status line at each launch with `--settings`

Claude Code 2.1.296 accepts `--settings <file-or-json>` to load extra settings
for one launch.

*   **Pros**: Nothing written to any `settings.json`, and copy setup can never
    carry one profile's label into another.
*   **Cons**: Sessions started outside the shell command and the app show no
    label. How it combines with the user's own status line is untested.
*   **Reason for Rejection**: The author chose the window title on the basis that
    sessions started without the wrapper would show no label this way either. It
    is the named fallback if S0 check 19 fails.

### Alternative J: An Xcode project instead of `make-app.sh`

*   **Pros**: Xcode handles bundling, signing and `Info.plist` through its UI.
*   **Cons**: The project file is large, hard to read and hard to diff, which
    makes it harder for the author and Claude to maintain.
*   **Reason for Rejection**: The author must be able to read and change every part.
    The scripts keep every build step in a readable text file.

### Alternative M: The app writes all the launch rules into each `.command` file

*   **Pros**: The app works before the shell layer is installed. The `.command` file
    can be read on its own.
*   **Cons**: The launch rules exist twice, in zsh and in Swift-generated shell, so
    the two can drift apart on the rules that matter most. It also needs a `zsh -lic`
    `PATH` read at each launch.
*   **Reason for Rejection**: The author chose one copy of the rules (2026-11-02).

### Alternative K: A login-wide setting with `launchctl setenv`

The app sets `CLAUDE_CONFIG_DIR` for the whole macOS login, and a LaunchAgent reapplies it at each login.

*   **Pros**: Dock-opened apps, including VS Code, follow the default with no extra step.
*   **Cons**: S0 check 12 showed the desktop app's Code tab then runs in the default profile's folder while showing
    the desktop app's own account, so its session files and settings split. It also needs a LaunchAgent, and adds
    wrong-account cases for apps already running.
*   **Reason for Rejection**: The harm outweighs the convenience. "Open VS Code" (flow 12) covers the app that
    benefited, without touching the desktop app.

### Alternative L: A zsh function instead of a wrapper script

*   **Pros**: No file on `PATH`.
*   **Cons**: Reaches only zsh shells that source `~/.zshrc`. Bash and scripts
    ignore it. `exit` or `exec` in it would close the user's shell.
*   **Reason for Rejection**: It cannot meet the author's requirement that plain
    `claude` follows the default outside interactive zsh.

--------------------------------------------------------------------------------

## Non-Functional Requirements

*   **Security & Privacy**
    *   Never read Keychain secret data, `.credentials` files,
        `sessions/*.key` files or token fields. From `.claude.json` parse only
        `oauthAccount` (for labels) and, during copy setup, `mcpServers` in the
        source. The app never writes a `.claude.json`. Backups copy whole files.
    *   Registry, `default` and `names` files are mode 0600. `.command` files
        are mode 0700 and deleted on the next launch.
    *   Copy setup masks secret-looking values and leaves them out unless
        ticked. Login credentials and trust grants are never copied.
    *   `commandName` and `displayName` follow the name rules in Terms, and
        `.command` values are quoted as in flow 2, because folder paths come from
        transcript data and go into a file that gets run.
    *   No App Sandbox; the README states why.
*   **Performance**
    *   No process stays running besides the app. While the panel and window are hidden, the app
        polls nothing and holds one FSEvents watcher on `~/.claude-profiles/`,
        ignoring `profiles/`.
    *   Transcript rows are cached in memory by path, size and change time, so
        reopening the panel re-reads only changed files.
*   **Rollout & Rollback**
    *   Before S1: back up `~/.claude` and `~/.claude.json` (S0).
    *   Turning off the shell layer: remove the added lines from `~/.zshenv` and
        `~/.zshrc`, then open a new shell. The built-in profile works as before.
    *   Copy setup: Restore, within the limits in flow 6.
    *   Full uninstall: `cpm uninstall` refuses while any added profile has a live
        session. It runs logout and Keychain cleanup for
        each added profile, moves `~/.claude-profiles` to the Trash, and prints the
        `~/.zshenv` and `~/.zshrc` lines to remove.
        `~/.claude` and `~/.claude.json` are not touched.

## Risks & Mitigations

| Risk | Impact | Mitigation | How it is detected |
| --- | --- | --- | --- |
| Session starts on the wrong account | Work and usage land on the wrong account | Wrapper, profile in window title and status line, plain `claude` stops on a broken default | Session rows tagged by profile; `doctor` |
| VS Code opened from the Dock runs on the built-in profile | Work lands on the wrong account | "Open VS Code" labels the profile; Terminal title and status line show it there too | Session rows tagged by profile |
| Claude Code update changes a file format | Empty list, or a bad copy | Readers skip bad files; writes refused on an unexpected shape | `canWrite`; `doctor` parse count |
| Claude Code update changes Keychain naming | Added profiles look logged out | Sign in again; old items listed, never guessed at | `doctor` recorded-name check |
| Copy racing a session | A lost write to the target's config | Refuse on a live session, re-check before writing, before-state hashes | `apply` stops and reports |
| Shell file edits go wrong | `claude` not found, or not the wrapper | The user adds the lines; `~/.zshrc` line checks the file exists; removal is deleting two lines | `doctor` compares `PATH` results |
| Wrong Keychain item deleted | Another profile is logged out | Only the recorded name; the plain built-in name is refused | Tests under a test prefix; `writes.log` |
| Hostile folder name in a transcript | Commands run from a `.command` file | Quoting rule in flow 2; tests with hostile values | Unit tests |

### Limits

Nothing here has been measured. The design target is 10 profiles and 5,000
transcripts, with the panel opening in under 1 second at that size. S2 measures
this. A row may need more than a partial read if Claude Code's title sits deep
in a large file; S2 also records whether that happens.

--------------------------------------------------------------------------------

## Additional Resources

*   [`concepts_brief.md`](concepts_brief.md) — brainstorm brief, with two rounds of
    independent review and decisions 1 to 18.
*   [`concept_input.md`](concept_input.md) — original concept input (superseded by
    the brief).
*   [Clickable mock](https://claude.ai/artifact/HxChm8Z1EiwmjeLt1P1z7v) of the
    menu-bar panel, full window and copy setup, reviewed by the author on
    2026-11-01. It uses sample data and is private to the author's account.
*   Claude Code 2.1.296: the code in the installed binary (Keychain naming, terminal
    title setting), `--help` output (`--settings`, `--resume`, `auth`, `mcp`,
    `plugin`), and local observation of `~/.claude/sessions/<pid>.json` and
    `~/.claude.json` key names.

## Future Improvements

*   **Copying into the built-in profile**, if the author later accepts writing to
    `~/.claude`.
*   **`--settings` status line for every profile** (Alternative I), so the
    built-in profile gets an in-session label without writing to
    `~/.claude/settings.json`.
*   **Desktop app and Cowork launcher** on a chosen account, and Remote Control
    handling, after their own tests (stage S6).
*   **Per-profile Terminal colour themes** with AppleScript.
*   **Tags, full-text transcript search, usage display.**
*   **Other terminals** (iTerm2, Ghostty).

--------------------------------------------------------------------------------

## Detailed Implementation Plan

The repository root is `~/claude-account-manager/`, and design documents and review
reports stay in `docs/designs/`. S0 runs `git init`. Paths below are relative to the
root. No third-party Swift dependencies; `cpm` parses its own arguments.

Work runs in stages. Each stage ends with its tests passing:

| Stage | When it ends, you can | Files |
| --- | --- | --- |
| S0 | Know whether to continue, with a backup in hand | 1–5 |
| S1 | Add profiles with `cpm add`, log in once each, and use `claude` and `claude-as` with no more logging out; `cpm doctor` works | 6–38 (25, 26, 36 unused) |
| S2 | List, start, resume and stop sessions from `cpm` | 39–46 |
| S3 | Do all of that, and add profiles, from the menu-bar app and window | 47–57 |
| S4 | Copy setup. Step 1: skills, agents, commands, `CLAUDE.md`, MCP, `permissions`, `model`, `env`. Step 2: plugins and marketplaces, hooks | 58–64 |
| S5 | Remove a profile, and uninstall | 65–67 |
| S6 | Optional later work: desktop app, Cowork and Remote Control, each after its own test. Not planned here | — |

The README (file 38) grows at each stage. `docs/E2E-checklist.md` (file 46) gains each
stage's journeys.

**If an S0 check fails:**

| Check | Change to the plan |
| --- | --- |
| 1, 2, 3 | Stop. Do not start S1 |
| 5 | Remove Resume from files 40, 49, 50 and `cpm resume`, or limit it to the original folder |
| 6 | Files 17 and 65: no logout call; removal warns. Flow 3: "Sign out and retry" shows manual steps |
| 7 | Flow 3 step 6 already handles this: no name saved, and a warning |
| 8 | Mark the failing item not copyable in file 59 |
| 9 | Accept one prompt, or use AppleScript in file 40 |
| 25 | Remove `openVSCode` from file 40, `cpm code` from file 42 and the button from file 50 |
| 11 | Revisit this whole plan for each tool marked adopt or borrow |
| 15 | `SessionRow.mayBeStale` is true for every live row and the row says so; file 41 compares PID only and asks for extra confirmation |
| 19 | Return to the author with rejected Alternative I as the fallback |
| 21 | Files 12 and 65 print the manual Keychain step instead of deleting |

### Target Files to Create/Modify

#### S0: setup and checks

Run `git init`, then:

##### 1. `.gitignore` (Create)

*   **Type / Scope**: Git configuration.
*   **Proposed Changes**: Ignore `.build/`, `build/`, `*.app`, `.DS_Store`, and
    `s0-results/` (raw check output may contain account emails).

##### 2. `docs/S0-checklist.md` (Create)

*   **Type / Scope**: Manual test checklist.
*   **Proposed Changes**: One section per check in the S0 table, check 11 first, each
    with exact steps and what to record. Pass conditions come from the table. Test
    folders are `~/.cpm-s0/<n>/`, written with `realpath`, because `/tmp` may clear at
    reboot and is a symlink. Teardown at the end: `claude auth logout` in each test folder, remove `~/.cpm-s0/`.
    After a Claude Code update, rerun only the checks whose subject changed.

##### 3. `docs/S0-results.md` (Create)

*   **Type / Scope**: Committed results, with no emails.
*   **Proposed Changes**: Date and Claude Code version per check, pass or fail, the
    fallback taken, and constants later files use: `procStart` format and tolerance,
    `entrypoint` values, exact install commands, and the file-format key names from check
    23. Records the decision to continue.

##### 4. `scripts/s0-backup.sh` (Create)

*   **Type / Scope**: Shell script.
*   **Proposed Changes**: Asks the user to stop other Claude Code sessions first. Copies
    `~/.claude` (excluding `sessions/`) and `~/.claude.json` with `ditto` to
    `~/claude-profile-backup-<date>/`, mode 0700, and writes `Claude Code-credentials*`
    item *names* to `keychain-names.txt`, from `security dump-keychain` attributes,
    never `-d`. Refuses if the destination exists. Prints the restore steps.

##### 5. `scripts/s0-checks.sh` (Create)

*   **Type / Scope**: Shell script.
*   **Proposed Changes**: Helpers for the automatable checks: create test folders; list
    Keychain item names before and after a step; print the config file location under
    a custom folder; print a session record's `procStart` next to the `sysctl` start
    time; run `claude mcp add-json --scope user` and the plugin commands with
    `CLAUDE_CONFIG_DIR` set to a test folder, and show where the entry landed. Writes to
    `s0-results/`. Logging in is done by hand.

#### S1: core, profiles and shell

##### 6. `Package.swift` (Create)

*   **Type / Scope**: Swift package manifest.
*   **Proposed Changes**: `swift-tools-version: 6.0`, macOS 14 minimum for
    `@Observable`, Swift Testing (`import Testing`). Full Xcode is not required. Command
    Line Tools alone have no XCTest, and find Swift Testing only with extra paths, which
    `scripts/test.sh` (file 29) supplies. Both were tried on this Mac. Declares only `ProfileCore`,
    `cpm` and `ProfileCoreTests` (with resource folder `Fixtures`). S3 adds `ProfileUI`
    and `ClaudeProfiles`.

##### 7. `Sources/ProfileCore/Model/Profile.swift` (Create)

*   **Type / Scope**: Data model.
*   **Proposed Changes**:
    ```text
    struct Profile: Codable, Identifiable {
      id: String            // 8 lowercase base32 chars; "builtin" for the built-in profile
      commandName: String   // fixed at creation
      displayName: String
      color: String         // "#RRGGBB"
      builtin: Bool
      configDir: String?    // set once at creation, never rebuilt; nil for built-in
      createdAt: Date
      accountUuid: String?; email: String?; organizationName: String?  // cached
      keychainBaseline: [String]?  // only while waiting for a login
      loginStartedAt: Date?        // only while waiting; the timeout runs from here
      keychainService: String?     // recorded at first login; nil for built-in
      state: ProfileState   // pendingLogin | ready | loggedOut | deleting | error
      lastError: String?
    }
    struct Registry: Codable { schemaVersion: Int; defaultProfileId: String; profiles: [Profile] }
    ```
    Validators for `commandName`, `displayName` and `color` follow the Name rules in
    Terms; `color` must match `^#[0-9A-Fa-f]{6}$`. Seed row values are in Terms.

##### 8. `Sources/ProfileCore/AtomicFile.swift` (Create)

*   **Type / Scope**: File utility.
*   **Proposed Changes**: `write(_ data: Data, to: URL, mode: mode_t)` writes to a
    temporary file in the same folder, `fsync`s, sets the mode, and renames over the
    target. Used by every module that replaces a file.

##### 9. `Sources/ProfileCore/ConfigLocator.swift` (Create)

*   **Type / Scope**: Path resolution.
*   **Proposed Changes**:
    ```text
    struct LaunchEnv { set: [String: String]; unset: [String] }
    struct ConfigLocator {
      init(home: URL, profilesRoot: URL)   // injected, so tests never touch real paths
      func configDir(for: Profile) -> URL     // <home>/.claude, or the stored configDir
      func newConfigDir(id: String) -> String // only place a configDir is built
      func configFile(for: Profile) -> URL    // <home>/.claude.json or <dir>/.claude.json
      func environment(for: Profile) -> LaunchEnv
          // built-in: unset CLAUDE_CONFIG_DIR. Added: set it to the stored configDir.
          // Always set CLAUDE_PROFILE_ID. Never touches the login variables.
          // Used for commands the app runs directly. Sessions get theirs from the wrapper.
    }
    ```
    No other Swift file builds these paths.

##### 10. `Sources/ProfileCore/AccountReader.swift` (Create)

*   **Type / Scope**: Reader for `.claude.json`.
*   **Proposed Changes**: `readAccount(configFile:) -> Account?` decodes only
    `oauthAccount.accountUuid`, `emailAddress` and `organizationName`, with a decoder
    that ignores all other keys, so tokens and project data are never held as values.

##### 11. `Sources/ProfileCore/ProfileStore.swift` (Create)

*   **Type / Scope**: Registry owner.
*   **Proposed Changes**:
    ```text
    final class ProfileStore {
      init(locator: ConfigLocator)
      func load() throws -> Registry    // creates the seed row if missing
      func repairFiles() throws         // rewrites default and names from profiles.json
      func create(displayName, commandName, color) throws -> Profile
                                        // pendingLogin; folder 0700; statusLine (file 13)
      func completeLogin(id, account, keychainService?) throws -> LoginResult
                                        // LoginResult.duplicateOf: another profile's id
      func discard(id) throws           // pendingLogin only; folder to Trash
      func setKeychainBaseline(id, names, startedAt) / clearKeychainBaseline(id)
      func markLoggedOut(id) / markReady(id, account) / markError(id, reason) / markDeleting(id)
      func updateAccount(id, account) -> AccountChange  // for the changed-login notice
      func rename(id, displayName) throws   // display name only
      func setColor(id, color) throws
      func setDefault(id) throws
      func defaultProfile() -> Profile
      func removeRow(id) throws         // called only by ProfileRemover (file 65)
    }
    ```
    Every change: take `flock` on `.lock`, re-read `profiles.json`, change it, then write
    `profiles.json` (0600), `names` (0600), `default` (0600), in that order, with
    `AtomicFile`. A new row is written before anything points to it. Removing moves
    `default` first. Changes log through EventLog. The built-in row cannot be removed or
    given a `keychainService`.

##### 12. `Sources/ProfileCore/KeychainProbe.swift` (Create)

*   **Type / Scope**: Keychain access, names only.
*   **Proposed Changes**: `listServiceNames(prefix: "Claude Code-credentials") ->
    Set<String>` with `SecItemCopyMatching`, class generic password,
    `kSecReturnAttributes: true`, `kSecMatchLimitAll`, and no `kSecReturnData`.
    `exists(service:) -> Bool`. `delete(service:)` is called only by ProfileRemover, and
    refuses the plain `Claude Code-credentials` name. The prefix is injected for tests.

##### 13. `Sources/ProfileCore/StatusLineWriter.swift` (Create)

*   **Type / Scope**: Status line for added profiles.
*   **Proposed Changes**: When a profile is created, writes `settings.json` in its folder
    with a `statusLine` of type `command` running the absolute path of
    `bin/profile-label <id>`. Refuses to run for the built-in profile. The settings file
    is written once, because the script reads the display name.

##### 14. `Sources/ProfileCore/EventLog.swift` (Create)

*   **Type / Scope**: Logging.
*   **Proposed Changes**: `write(kind, profileId, detail, backupPath?)` with kinds copy,
    restore, delete, default, stop. Appends one tab-separated line, with tabs and newlines
    in values replaced by spaces. Rotates `writes.log` and `launch.log` at 1 MB keeping 3
    files, checked when the app or `cpm` starts. A logging failure never blocks the action.

##### 15. `Sources/ProfileCore/ProcessRunner.swift` (Create)

*   **Type / Scope**: Process execution.
*   **Proposed Changes**: `run(executable, args, env: LaunchEnv?, timeout) -> Result
    (stdout, stderr, exitCode)`, the one Swift API for running external commands, used
    by every module listed in Components. Behind a protocol; unit tests use a fake.

##### 16. `Sources/ProfileCore/ClaudeBinaryLocator.swift` (Create)

*   **Type / Scope**: Finding the real binary.
*   **Proposed Changes**: `realBinary() -> URL?` applies the rule in flow 1, for ClaudeCLI
    only. `cpm` uses its own `PATH`. The app uses the read in flow 2. Resolved at each use.

##### 17. `Sources/ProfileCore/ClaudeCLI.swift` (Create)

*   **Type / Scope**: Runs Claude Code's own commands.
*   **Proposed Changes**: Through ProcessRunner, with the profile's environment from
    ConfigLocator: `login`, `logout`, `version`, and in S4 `addMCP` (`claude mcp add-json
    <name> <json> --scope user`), `addMarketplace`, `installPlugin`. Exact plugin
    commands come from `docs/S0-results.md`. A failure is returned; there is no fallback.

##### 18. `Sources/ProfileCore/LoginCoordinator.swift` and `Reconciler.swift` (Create)

*   **Type / Scope**: Owns flow 3; runs reconcile.
*   **Proposed Changes**: Dependencies run one way: Reconciler uses LoginCoordinator and, from
    S5, ProfileRemover; both use ProfileStore; ProfileStore uses neither.

    LoginCoordinator: `begin(profile) -> LoginCommand` stores the baseline through
    `setKeychainBaseline`. `poll(profile) -> LoginState` reads only and returns `waiting`,
    `detected(Account, duplicateOf?)` or `timedOut` (10 minutes from `loginStartedAt`). `retry(profile)` logs out first if a login exists, then takes the new baseline, because logout removes the item and the next login recreates the same name (S0 check 6). `keep(profile)` is the only thing that
    completes a login: it diffs Keychain names against the baseline, with the retry for up to
    10 seconds, calls `completeLogin`, then `clearKeychainBaseline`. No detected value is
    stored between `poll` and `keep`. The app polls on file changes; `cpm` polls once at each
    run and prints a notice.

    Reconciler: `run() -> ReconcileResult` calls `ProfileStore.repairFiles()`, lists folders
    with no row, marks rows with no folder through `markError`, and returns pending-login
    notices from LoginCoordinator. It calls `ProfileRemover.resumePending()` from S5 (file 67).

##### 19. `Sources/ProfileCore/CompatCheck.swift` (Create)

*   **Type / Scope**: `settings.json` shape check.
*   **Proposed Changes**: `canWriteSettings(file: URL) -> Bool`, called by SetupCopier on the
    target's `settings.json`, or true if that file does not exist. Top level must be a JSON
    object. If present: `permissions` object with `allow` and `deny` string arrays, `hooks`
    object, `enabledPlugins` object, `extraKnownMarketplaces` object, `model` string, `env`
    object of strings. All other keys are ignored and kept.

##### 20. `Sources/ProfileCore/TranscriptReader.swift` (Create)

*   **Type / Scope**: Reader for `projects/*/*.jsonl`.
*   **Proposed Changes**: `summary(of: URL) -> TranscriptSummary?` with `sessionId` (file
    name), `cwd`, `title` (the newest `customTitle` from a `custom-title` record, then the
    newest `aiTitle` from an `ai-title` record, then `summary`, or nil; key names recorded by
    S0 check 23), `lastActivity`
    (file change time), `entrypoint`. Reads the first and last 64 KB, extending up to
    1 MB if no title key is found. Never uses a user message as a title, because it could
    show pasted secrets in the menu. Unknown lines are skipped; nil on an unreadable file.

##### 21. `Sources/ProfileCore/ProcessInfoProvider.swift` and `LiveSessionReader.swift` (Create)

*   **Type / Scope**: macOS process queries; reader for `sessions/*.json`.
*   **Proposed Changes**: `isAlive(pid)` is `kill(pid, 0)`, with `EPERM` counting as alive.
    `startTime(pid)` uses `sysctl` `KERN_PROC_PID`. `signal(pid, sig)`. Behind a protocol.
    `LiveSessionReader` globs `*.json` only and decodes `pid`, `procStart`, `sessionId`,
    `cwd`, `status`, `entrypoint`, `name`, `startedAt`. A record missing `pid`, `procStart`
    or `sessionId` counts as unreadable. It parses `procStart` as a UTC date string (S0 check
    15; `ps -o lstart` prints local time and must not be compared with it). `liveRecords(for: Profile)` and `hasLiveSession(_:)` are never
    filtered, and copy, Restore, removal and uninstall must use them.

##### 22. `Sources/ProfileCore/Doctor.swift` (Create)

*   **Type / Scope**: Diagnostics shared by app and `cpm`.
*   **Proposed Changes**: Implements the flow 11 table. `run() -> DoctorReport` with one
    `Finding(level, profileId?, message, fix?)` per check; environment checks run in a
    login shell through ProcessRunner. `--fix` runs reconcile.

##### 23. `shell/bin/claude`, `shell/bin/claude-as`, `shell/bin/claude-core.zsh`, `shell/bin/claude-profiles.zsh` (Create)

*   **Type / Scope**: zsh scripts on `PATH`; a zsh file sourced from `~/.zshrc`.
*   **Proposed Changes**: `claude` and `claude-as` implement flow 1, sharing `claude-core.zsh`. They read `names`
    with zsh field splitting on tabs, and no `jq`. `claude-profiles.zsh` puts the bin
    folder first on `PATH` again and registers completion for `claude-as` from `names`.

##### 24. `shell/bin/profile-label` (Create)

*   **Type / Scope**: zsh script used as the status line command.
*   **Proposed Changes**: Takes a profile ID, reads its display name and colour from
    `names`, and prints `● <displayName>` with the colour as a 24-bit ANSI escape. Prints
    nothing for an unknown ID.

##### 27. `Sources/ProfileCore/ShellInstaller.swift` (Create)

*   **Type / Scope**: Owns flow 10.
*   **Proposed Changes**: `install(from: URL) -> InstallResult`, including the two literal
    lines to show. Safe to repeat. `isInstalled() -> Bool`. `uninstall()`, used in S5.

##### 28. `Sources/cpm/main.swift` (Create)

*   **Type / Scope**: Command-line tool.
*   **Proposed Changes**: S1 subcommands: `add <displayName> <commandName> <color>`
    (creates the profile, prints `claude-as <commandName> auth login`), `keep <commandName>`,
    `retry <commandName>`, `discard <commandName>`, `doctor [--fix]`, `default [<name>]`,
    `install-shell`. Each run starts with Reconciler, except `doctor` without `--fix`. Exit codes: 0 ok, 1 user
    error, 2 check failed.

##### 29. `make-cli.sh` and `scripts/test.sh` (Create)

*   **Type / Scope**: Build scripts.
*   **Proposed Changes**: `swift build -c release --product cpm`; signs `cpm` with
    `SIGN_IDENTITY`, or ad hoc if unset; copies `cpm` and `shell/bin/*`
    to `build/cli/`, from which `cpm install-shell` installs them.
    `scripts/test.sh` runs `swift test` with the Command Line Tools paths: `-F` for
    `Library/Developer/Frameworks`, `-plugin-path` for `usr/lib/swift/host/plugins/testing`,
    and linker `-F` and `-rpath` for the same folders. S0 check 24 proves it works.

##### 30. `Tests/ProfileCoreTests/Fixtures/` (Create)

*   **Type / Scope**: Test data.
*   **Proposed Changes**: Written by the tests themselves in `TestGuard.swift` and each suite, rather than stored as files. Built from the key names S0 check 23 records. Fake config folders:
    `builtin-home/.claude.json` and
    `added/<id>/.claude.json` with fake `oauthAccount` values and no tokens;
    `sessions/*.json` plus a decoy `.key` file, including `claude-desktop` records;
    `projects/` transcripts including one broken file and one with a title beyond 64 KB;
    `settings.json` samples. Never copied from real files.

##### 31. `Tests/ProfileCoreTests/TestGuard.swift` (Create)

*   **Type / Scope**: Test support.
*   **Proposed Changes**: Makes a temporary home for each test, and aborts the test if any
    resolved path is under the real home.

##### 32. `Tests/ProfileCoreTests/ConfigLocatorTests.swift` (Create)

*   **Type / Scope**: Unit tests.
*   **Proposed Changes**: Built-in never produces a set `CLAUDE_CONFIG_DIR`; added profile
    produces the stored `configDir` unchanged; `newConfigDir` has no `~`, trailing slash
    or symlink and is NFC; login variables never appear in `LaunchEnv`.

##### 33. `Tests/ProfileCoreTests/ProfileStoreTests.swift` (Create)

*   **Type / Scope**: Unit tests.
*   **Proposed Changes**: Create, rename (folder and command name unchanged), set default,
    duplicate `accountUuid`, discard, state changes, `repairFiles` (`default` missing or wrong
    is rewritten), built-in row
    protections, hostile names refused, a stale process does not overwrite a newer change,
    write order, atomic writes, file modes.

##### 34. `Tests/ProfileCoreTests/KeychainProbeTests.swift`, `LoginCoordinatorTests.swift`, `DoctorTests.swift` (Create)

*   **Type / Scope**: Unit tests.
*   **Proposed Changes**: Keychain tests under `cpm-test-credentials*`: list, exists, delete
    only the given name, refuse the plain name. Reconciler: orphan listed, not deleted; row
    with no folder becomes `error`; a detected login is reported and never completed;
    `cpm doctor` without `--fix` changes nothing. LoginCoordinator: only `keep` completes; one new name recorded;
    zero or several leaves `ready` with a warning; late Keychain item found within 10
    seconds; retry reuses the same name without error; timeout. Doctor names each injected
    fault, passes a clean setup with the `.claude/.claude.json` note, and notes do not
    change the exit code.

##### 35. `ShellTests/test_wrapper.zsh` and `ShellTests/bin/claude` (Create)

*   **Type / Scope**: Shell tests with a temporary `HOME`, kept in `ShellTests/` because
    `tests/` and `Tests/` would be one folder on a default case-insensitive macOS disk.
*   **Proposed Changes**: A stub `claude` prints its arguments and `CLAUDE_CONFIG_DIR`,
    `CLAUDE_PROFILE_ID`, `CLAUDE_CODE_DISABLE_TERMINAL_TITLE` and the three login variables.
    Cases:
    *   Default built-in → variable unset, including when the parent has it set.
    *   `claude-as <name>` → exact `configDir`.
    *   Default changed between calls → next call uses it with no reload.
    *   `CLAUDE_PROFILE_ID` set → stays on that profile.
    *   `CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1` always set.
    *   Each login variable set → still set in the child, one warning on stderr, stdout clean.
    *   Piped stdout → no title escape.
    *   Missing, unreadable or unknown-ID `default` → prints the problem and `cpm doctor`,
        exit 1, real binary not run.
    *   Unknown name, or unreadable `names` → exit 1, real binary not run.
    *   `--resume <sid>`, `--resume=<sid>` and `-r <sid>` of another profile's ID → refusal
        naming the owner, real binary not run. A non-UUID value is passed through.
    *   Wrapper finds the real binary, never calls itself, and exits with an error if none.
    *   `CLAUDE_CONFIG_DIR` outside `~/.claude-profiles/` → kept, one warning, real binary run.
    *   `CLAUDE_CONFIG_DIR` inside it, including a removed profile's old path → ignored.
    *   One line added to `launch.log`; an unwritable log does not block launch.
    *   Run from bash and from a script as well as zsh.

##### 37. `Tests/ProfileCoreTests/SupportTests.swift` and `ShellInstallerTests.swift` (Create)

*   **Type / Scope**: Unit tests.
*   **Proposed Changes**: AccountReader ignores other keys. StatusLineWriter refuses
    built-in and writes an absolute path. EventLog escapes tabs and rotates. CompatCheck is
    true on a file with only `statusLine`, on an unknown key and on a missing file, and false
    on a non-object top level and on `permissions` of the wrong type. ClaudeBinaryLocator skips the bin folder, including when
    spelled differently. TranscriptReader finds a title past 64 KB, returns nil on a broken
    file, and never returns user text. ProcessInfoProvider treats `EPERM` as alive.
    LiveSessionReader never opens `.key` files, and `hasLiveSession` sees a
    `claude-desktop` record. ClaudeCLI passes `--scope user`. ShellInstaller is safe to
    repeat and never resets the default. `cpm add` end to end with a stub `claude`.

##### 38. `README.md` (Create)

*   **Type / Scope**: Documentation.
*   **Proposed Changes**: What it does; requirements (macOS 14, Command Line Tools, Claude
    Code, Terminal.app; no Xcode); running tests with `scripts/test.sh`; S0 checklist link; build and signing; installing the shell layer;
    where the default applies (flow 5 table); daily use; copy limits (connectors, synced
    plugins, secrets, OAuth MCP re-login); why there is no App Sandbox; uninstall; and
    "When Claude Code updates": rerun the S0 checks whose subject changed, then `scripts/test.sh`.

#### S2: sessions, launching, stopping

##### 39. `Sources/ProfileCore/SessionIndex.swift` (Create)

*   **Type / Scope**: Merged session list.
*   **Proposed Changes**: `sessions(for profiles) -> SessionIndexResult(rows, parseFailures)`.
    `SessionRow`: `profileId`, `sessionId`, `cwd`, `title`, `lastActivity`, `live`, `status`,
    `entrypoint`, `pid`, `procStart`, `mayBeStale`. Outer join of transcripts and live
    records on `sessionId`, so a live record with no transcript still gives a row. Title
    order as in flow 4. `live` means alive with a matching `procStart`. Applies the display
    filter in flow 4; safety checks use file 21, not this. In-memory cache keyed on path,
    size and change time. Sorted live first, then by last activity. `ownerOf(sessionId) ->
    Profile?`.

##### 40. `Sources/ProfileCore/TerminalOpener.swift` and `Launcher.swift` (Create)

*   **Type / Scope**: Starts Terminal sessions.
*   **Proposed Changes**: `protocol TerminalOpener { func open(commandFile: URL) throws }`
    is a one-method test seam. Its one real implementation runs `/usr/bin/open -a Terminal
    <file>` through ProcessRunner, for both the app and `cpm`.
    ```text
    func newSession(profile, folder: URL = home) throws
    func resume(session: SessionRow, folderOverride: URL? = nil) throws
    func login(profile) throws   // claude-as <commandName> auth login
    func openVSCode(profile, folder: URL?) throws  // flow 12; refuses if VS Code is running
    ```
    Writes the file exactly as in flow 2 (0700), with the owner from `ownerOf`. Refuses if
    `ShellInstaller.isInstalled()` is false, and refuses to resume a live row. Deletes
    `.command` files older than 60 seconds.

##### 41. `Sources/ProfileCore/SessionStopper.swift` (Create)

*   **Type / Scope**: Stop.
*   **Proposed Changes**: `stop(row, confirm:)` implements flow 9.

##### 42. `Sources/cpm/main.swift` (Modify)

*   **Type / Scope**: Command-line tool.
*   **Proposed Changes**: Adds `list [--profile <commandName>] [--live]` (tab-separated
    profile, status, title, folder, session ID), `new <commandName> [folder]`, `code <commandName> [folder]`, `resume
    <sessionId>`, `stop <sessionId> [--yes]` (not on a terminal without `--yes` → exit 1).

##### 43. `Tests/ProfileCoreTests/SessionIndexTests.swift` (Create)

*   **Type / Scope**: Unit tests.
*   **Proposed Changes**: Rows per profile; `claude-desktop` hidden, unknown value shown;
    live record with no transcript gives a row; broken transcript counted; `.key` file never
    opened; reused PID not live; `mayBeStale` path; `ownerOf`.

##### 44. `Tests/ProfileCoreTests/LauncherTests.swift` and `SessionStopperTests.swift` (Create)

*   **Type / Scope**: Unit tests with fake `TerminalOpener` and `ProcessInfoProvider`.
*   **Proposed Changes**: Every line of the `.command` file, including `main` for the
    built-in profile and no `CLAUDE_CONFIG_DIR` line in the file; a hostile folder name (an
    apostrophe, a newline, `$(...)`) stays inert, run for real against a folder that does
    not exist and asserting that nothing ran; a `cd` failure stops; resume uses the owner,
    refuses a live row, honours `folderOverride`; refuses when the shell layer is missing;
    `openVSCode` passes the profile's values and refuses while VS Code is running; mode 0700; old files removed. The shell tests (file 35) run one generated file against
    the stub `claude`, end to end.
    Stop: SIGINT then SIGTERM timing; mismatched start time → "already stopped"; no SIGKILL.

##### 45. `scripts/measure-panel.sh` (Create)

*   **Type / Scope**: Measurement.
*   **Proposed Changes**: Times `SessionIndex` on generated fixtures of 10 profiles and 5,000
    transcripts against the 1-second target in Limits, and records whether titles sit past
    64 KB. If missed, the next step is an on-disk cache, which is decided then.

##### 46. `docs/E2E-checklist.md` (Create)

*   **Type / Scope**: Step-by-step manual checklist.
*   **Proposed Changes**: CUJs 1 to 8, added as each stage delivers them.

#### S3: menu-bar app and window

##### 47. `Package.swift` (Modify)

*   **Type / Scope**: Swift package manifest.
*   **Proposed Changes**: Adds library `ProfileUI` (depends on `ProfileCore`) and executable
    `ClaudeProfiles` (depends on `ProfileUI` and `ProfileCore`).

##### 48. `Sources/ProfileCore/FileWatcher.swift` (Create)

*   **Type / Scope**: FSEvents wrapper.
*   **Proposed Changes**: `watch(paths, ignoring:, onChange)` with a 200 ms delay to group
    events. Watches folders, not single files, because Claude Code and `AtomicFile` replace
    files by rename. Test: a change is reported, and one inside an ignored folder is not.

##### 49. `Sources/ProfileUI/AppModel.swift` (Create)

*   **Type / Scope**: Shared view model (`@Observable`).
*   **Proposed Changes**: Holds profiles, default, session rows, doctor summary. Loads on
    panel open; while any view is visible, rechecks live status every 2 seconds. Re-reads
    accounts on panel open and on file change, showing a notice if one changed. Actions, all
    calling ProfileCore: new session, open VS Code, resume (with `folderOverride`), stop, add profile,
    retry login, discard login, sign in (for `loggedOut`), rename, set default, set colour,
    set up shell, open doctor; S4 copy and restore; S5 remove. It keeps only filtering and
    sorting; the filter matches title, folder and profile name.

##### 50. `Sources/ProfileUI/MenuPanelView.swift` (Create)

*   **Type / Scope**: SwiftUI view.
*   **Proposed Changes**: Filter field; one row per profile (colour dot, display name, email,
    live count, default marker, "New Terminal", "Open VS Code", and "Not signed in — Sign in" when needed);
    8 most recent sessions, live first, each tagged with its profile, live rows with Stop and
    others with Resume; footer items "Open window…", "Add profile…", "Set default ▸", "Copy
    setup…" (S4), "Doctor", and "Set up shell" until installed.

##### 51. `Sources/ProfileUI/MainWindowView.swift` (Create)

*   **Type / Scope**: SwiftUI view.
*   **Proposed Changes**: Sidebar of profiles (with "All"), a table of sessions for the
    selection (title, folder, profile, last activity, status), toolbar with filter, New
    Terminal, Resume or Stop. Uses the row views from file 52.

##### 52. `Sources/ProfileUI/Rows.swift` (Create)

*   **Type / Scope**: SwiftUI views.
*   **Proposed Changes**: `ProfileRowView`, `SessionRowView`, and `ProfileBadge` (colour dot
    plus name), including "may be stale" and the "Choose folder" prompt.

##### 53. `Sources/ProfileUI/AddProfileView.swift`, `ProfileSettingsView.swift`, `DoctorView.swift`, `StopConfirmation.swift` (Create)

*   **Type / Scope**: SwiftUI views.
*   **Proposed Changes**: Add profile steps from flow 3 (name, command name, colour; waiting
    with the browser note, Retry and Discard; Keep or Sign out and retry; duplicate-account
    warning). Settings sheet for rename, colour, set default, and Remove (S5). Doctor list
    grouped by profile with fix hints, and the health line. Stop confirmation text from flow 9.

##### 54. `Sources/ClaudeProfiles/ClaudeProfilesApp.swift` (Create)

*   **Type / Scope**: App entry point.
*   **Proposed Changes**: `MenuBarExtra` with `.menuBarExtraStyle(.window)`, drawn as a
    non-template `NSImage` in the default profile's colour, so macOS does not recolour it; a
    `WindowGroup` opened by "Open window…". One FSEvents watcher on `~/.claude-profiles/`
    for the icon colour, ignoring `profiles/`.

##### 55. `Resources/Info.plist`, `Resources/AppIcon.icns`, `scripts/make-icon.sh` (Create)

*   **Type / Scope**: App bundle metadata and icon.
*   **Proposed Changes**: `CFBundleIdentifier` `local.claude-profiles`, `LSUIElement` true,
    `LSMinimumSystemVersion` 14.0, `CFBundleIconFile`, version strings. No
    `NSAppleEventsUsageDescription`. The script generates a single-colour `.icns`.

##### 56. `make-app.sh` (Create)

*   **Type / Scope**: Build script.
*   **Proposed Changes**: `swift build -c release`; assembles
    `build/ClaudeProfiles.app/Contents/{MacOS,Resources,Info.plist}`, including `cpm`
    and `shell/bin/*` as resources; signs with `SIGN_IDENTITY`, failing with
    instructions if unset; no hardened runtime, no entitlements. `--install` copies the app to
    `~/Applications`. Run S0 checks 9 and 21 here.

##### 57. `Tests/ProfileCoreTests/AppFlowTests.swift` (Create)

*   **Type / Scope**: Unit tests. Views are exempt from unit tests and covered by
    `docs/E2E-checklist.md`.
*   **Proposed Changes**: AppModel actions call the right ProfileCore method; the filter
    matches title, folder and profile name; an account change gives a notice.

#### S4: copy setup

Run in two steps, as in the stage table.

##### 58. `Sources/ProfileCore/SetupCopier/CopyPlan.swift` (Create)

*   **Type / Scope**: Data model.
*   **Proposed Changes**:
    ```text
    CopyItem(kind: mcpServer | setting | skill | agent | command | claudeMd | plugin | hook,
      key, action: add | change | same, sourceValue, targetValue, beforeHash?,
      conflict: skip | overwrite | rename, pointsIntoSource: Bool, needsReLogin: Bool,
      secrets: [SecretField(name, masked, ticked)], flaggedFile: Bool,
      writeMethod: direct | command(removalCommand), ticked: Bool)
    CopyPlan(source, target, items, blockedReason?)
    ApplyResult(done: [CopyItem], failed: [(CopyItem, reason)], backupId)
    ```
    Rename adds `-copy` before the extension. Skills copy per folder.

##### 59. `Sources/ProfileCore/SetupCopier/SetupCopier.swift` (Create)

*   **Type / Scope**: Copy logic.
*   **Proposed Changes**: `plan(source, target) -> CopyPlan` and `apply(plan) -> ApplyResult`.
    Source and target must be `ready`; target must be an added profile. `apply` re-checks
    `hasLiveSession`, `CompatCheck.canWriteSettings` on the target and before-state hashes, backs up (file 62), does
    direct writes through `AtomicFile`, then runs commands through ClaudeCLI. On failure it
    stops and returns what was done. `restore(backupId)` follows flow 6.

##### 60. `Sources/ProfileCore/SetupCopier/SecretDetector.swift` (Create)

*   **Type / Scope**: Secret detection.
*   **Proposed Changes**: Value patterns: `sk-[A-Za-z0-9_-]{20,}`, `ghp_[A-Za-z0-9]{20,}`,
    `xox[bap]-[A-Za-z0-9-]+`, and hex or base64 runs of 40 or more characters. Applied to
    `args` and free text as in flow 6. `env` and `headers` values, in MCP entries and in
    `settings.json`, are always masked. A masked value shows as `••••••`, with none of it shown.

##### 61. `Sources/ProfileCore/SetupCopier/SettingsMerger.swift` (Create)

*   **Type / Scope**: `settings.json` merge.
*   **Proposed Changes**: Parses with `JSONSerialization` and keeps unknown keys; key order is
    not kept, and the backup holds the original. Follows the flow 6 table row by row: lists
    combined without duplicates; `hooks` merged by event and matcher, with an identical hook
    not added twice; `enabledPlugins` and `extraKnownMarketplaces` added; `model` replaced;
    `env` per variable. Produces both the preview and the written result.

##### 62. `Sources/ProfileCore/SetupCopier/BackupStore.swift` (Create)

*   **Type / Scope**: Backups.
*   **Proposed Changes**: `~/.claude-profiles/backups/<id>/<ISO time>/`, folders 0700 and files
    0600. `manifest.json`: files changed with original mode, files created, SHA-256 before and
    after (after-state recorded once direct writes finish). Files changed by commands are not
    listed. Keeps the last 10 per profile.

##### 63. `Sources/ProfileUI/CopySetupView.swift` (Create)

*   **Type / Scope**: SwiftUI view.
*   **Proposed Changes**: Pick source (any `ready` profile) and target (added profiles only);
    items grouped by kind with Add, Change or Same, a diff for Change, checkboxes and skip,
    overwrite or rename choices; Apply; list of recent backups with Restore and the removal
    commands. Shows the blocked reason on a live session. Messages:
    *   "Contains a secret (API key). Unticked by default. Copying puts the key in plain text in <target>."
    *   "Uses OAuth sign-in. You will need to sign in again in <target>."
    *   "Not copyable: claude.ai connectors belong to the account. Connect them at claude.ai while signed in as <email>."

##### 64. `Tests/ProfileCoreTests/SetupCopierTests.swift`, `SecretDetectorTests.swift`, `SettingsMergerTests.swift`, `BackupStoreTests.swift`, `Fixtures/golden/` (Create)

*   **Type / Scope**: Golden-file and unit tests.
*   **Proposed Changes**: The plan matches a stored golden plan. Direct writes match the plan.
    Unticked secrets never written. `statusLine`, `apiKeyHelper`, `oauthAccount` and trust
    grants never copied. Plugin registries and `plugins/synced/` never written by SetupCopier
    itself. Copy refused with a live session, including a `claude-desktop` record. Built-in
    target refused. Before-state hash mismatch stops. Mid-copy failure reports done and failed.
    Source-folder flagging. Restore: unchanged files are byte-identical with their mode;
    changed files listed and left alone; created files deleted; refused on a live session.
    SecretDetector: true and false matches, including `author` and `monkey`, which do not match.
    SettingsMerger: each table row.

#### S5: removal and uninstall

##### 65. `Sources/ProfileCore/ProfileRemover.swift` (Create)

*   **Type / Scope**: Owns flow 7 and uninstall.
*   **Proposed Changes**: `remove(profile) throws`: refuses on `hasLiveSession`, then the flow 7
    order, each step safe to repeat, skipping Keychain deletion if no name was recorded.
    `resumePending()` continues any `deleting` row and is called by Reconciler.
    `uninstall() throws`: refuses if any added profile has a live session; removes each added
    profile; moves `~/.claude-profiles` to the Trash; prints the two lines to remove and the instruction
    to log out and back in.

##### 66. `Tests/ProfileCoreTests/ProfileRemoverTests.swift` (Create)

*   **Type / Scope**: Unit tests.
*   **Proposed Changes**: Order of steps; default moved first; refused on a live session,
    including `claude-desktop`; a crash at each step resumes correctly; plain Keychain name never
    deleted; built-in cannot be removed; uninstall refusal and order.

##### 67. `Sources/ProfileCore/Reconciler.swift`, `Sources/cpm/main.swift`, `Sources/ProfileUI/ProfileSettingsView.swift`, `Sources/ProfileUI/AppModel.swift` (Modify)

*   **Type / Scope**: Reconcile; command-line tool; UI.
*   **Proposed Changes**: Reconciler gains the `ProfileRemover.resumePending()` call. Adds `cpm
    remove <commandName>` and `cpm uninstall`, each with one confirmation, and the Remove
    button.

### Verification Plan

*   **Compilation/Build**: `swift build`, `./make-cli.sh` from S1, and `./make-app.sh` from S3,
    with `SIGN_IDENTITY` set.
*   **Unit Tests**: `scripts/test.sh`. Named cases are in files 32–37, 43–44, 48, 57, 64 and 66.
    SwiftUI views and `Info.plist` are exempt and covered by the E2E checklist.
*   **Shell Tests**: `zsh ShellTests/test_wrapper.zsh`.
*   **Static Analysis & Linting**: `swift build -Xswiftc -warnings-as-errors`; `zsh -n` on every
    file in `shell/bin/`; `bash -n` on `scripts/*.sh`.
*   **Test Coverage Requirement**: the named cases above; no percentage target, because the risk
    sits in specific rules, not in line counts.
*   **Performance**: `scripts/measure-panel.sh` in S2.
*   **E2E/Integration**: `docs/E2E-checklist.md`, step by step: three profiles logged in across
    a reboot; CUJs 1 to 8; a rebuild brings back no permission prompt; after a Claude Code update,
    `cpm doctor` passes or names the fault.

--------------------------------------------------------------------------------

<!-- Provenance: Indicates initial structural drafting via EGM. -->

*Drafted with EGM*
