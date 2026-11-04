# Claude Profile Manager (Windows)

*   **Author:** Abdul Samad Shahid
*   **Status:** Proposed. Decisions D1 to D9 settled 2026-11-04: macOS features and rules, Windows conventions. Not yet reviewed.
*   **Date:** 2026-11-04
*   **Base design:** [`claude-profile-manager.md`](claude-profile-manager.md) (macOS). This document
    lists only what is different on Windows. Every rule, flow and decision in the base design
    holds unless a section here replaces it. Flow numbers refer to the base design.

--------------------------------------------------------------------------------

## Overview

A Windows version of Claude Profile Manager: several Claude accounts on one Windows PC, each
logged in at the same time, each in its own Claude Code config folder. It has the same parts
as the Mac version: a `claude` / `claude-as` wrapper first on `PATH`, the `cpm` command, and an
app, here in the system tray instead of the menu bar.

It is a separate implementation in C# (.NET), in `windows/` in this repository. It shares
nothing with the Swift code at build time. It keeps the same file formats in
`~/.claude-profiles/` (`profiles.json`, `default`, `names`), the same profile states, the same
copy-setup table and the same safety rules, so the two versions behave the same way and one
design document stays the source of truth for the rules.

## What stays the same

*   Profiles are config folders selected with `CLAUDE_CONFIG_DIR`. The built-in profile is
    `%USERPROFILE%\.claude` with the variable unset, and the tool never writes to it or to
    `%USERPROFILE%\.claude.json`.
*   Terms, name rules, profile states and reconcile (base design, Terms).
*   Flow 1's launch rules: deliberate override, `CLAUDE_PROFILE_ID` for nested calls, the
    ownership check on `--resume`, the login-variable warning, stop on a broken default.
*   Flow 3: nothing completes a login except Keep.
*   Flow 4: a record is live only if its PID is alive and its start time matches.
    Desktop app rows are hidden, and safety checks ignore that filter.
*   Flow 6: the copy table, secrets handling, backups, before-state hashes and Restore.
*   Flow 7: refuse on a live session, move the default first, `deleting` state, resume after a crash.
*   Flow 11 and flow 12, with the changes below.
*   Non-goals: desktop app and Cowork conversations, Remote Control, two profiles on one account,
    copying into the built-in profile, distribution.

## Seen on this PC

Claude Code 2.1.318, Windows 11 Pro, on 2026-11-04. Key names only; no values were recorded.

| What | Seen |
| --- | --- |
| Where the login is | `%USERPROFILE%\.claude\.credentials.json`, top-level key `claudeAiOauth`. No Keychain equivalent is involved |
| Config file | `%USERPROFILE%\.claude.json`, with `oauthAccount` at the top level |
| Real binary | `%USERPROFILE%\.local\bin\claude.exe`, a native console exe. Its folder is in the **user** `PATH`. A previous version sat beside it as `claude.exe.old.<n>` after a self-update |
| Session records | `sessions\<pid>.json` with the same keys as on macOS, plus `asksForConvertedSkills`, and `hostSessionId` on some |
| `procStart` | A JSON string of digits, not a date string: Windows file time, 100 ns ticks since 1601-01-01 UTC. `134382630457202312` is 2026-11-04 10:50:45 UTC, which matched the process start time to the second for both live records |
| `pidDomain` | `win32:<computer name>` |
| `messagingSocketPath` | A named pipe, `\\.\pipe\LOCAL\cc-msg-<hex>` |
| `entrypoint` | `cli` and `claude-desktop` seen. The desktop app's sessions are in `%USERPROFILE%\.claude\sessions` too |
| Transcript folders | `projects\<encoded cwd>\<sessionId>.jsonl`, for example `D--MultiAccount` for `D:\MultiAccount` |
| Home path | `C:\Users\Abdul Samad`: it has a space, and an 8.3 short form `C:\Users\ABDULS~1` that some tools return |
| Terminal | Windows Terminal (`wt.exe`) is installed. Git for Windows is installed |
| Build tools | No .NET SDK (only the .NET Core 3.1 runtime). No Visual Studio C++ build tools. No VS Code |

So the Windows login lives in a file inside each config folder. That removes the Keychain
parts of the design: the baseline, the recorded item name, and the path hash that made the
exact `configDir` spelling matter. The `procStart` format is different, and the current
parser would treat every live PID as a match.

## Goals

The base design's goals, with these changes:

*   **Tray app instead of menu bar.** A coloured dot in the notification area, in the default
    profile's colour. Clicking it opens the panel; "Open window…" opens the full window.
*   **Plain `claude` follows the default wherever the wrapper is reached.** On Windows that is any
    process that reads the user `PATH`, including PowerShell, Command Prompt, Git Bash, VS Code's
    terminal and the Run dialog (flow 5 table below).
*   **New sessions open in the user's default terminal**, which is Windows Terminal on this PC.
*   **The author can own the code.** Plain C# in `windows/`, built with the `dotnet` command line
    and one PowerShell script. No Visual Studio solution is required to build.

## Non-Goals

The base design's non-goals, plus:

*   **WSL.** Claude Code inside WSL has its own home folder and `~/.claude`. Not listed, not managed.
*   **Claude Code installed with npm** (`claude.cmd`). The wrapper runs only the native
    `claude.exe` (flow 1 below). `cpm doctor` explains how to switch.
*   **Sharing profiles between the Mac and Windows versions.** The file formats match so the
    rules match, not so that one folder can be used by both.
*   **ARM64 Windows** in v1. It may build, but it is not tested.
*   **Terminals other than the Windows default terminal**, and per-profile terminal colour schemes.

## Decisions

Made by the author on 2026-11-04:

*   C# on .NET, not Swift. Swift runs on Windows but SwiftUI does not, so a second UI stack
    would be needed anyway. Alternative N.
*   The code lives in `windows/` in this repository.
*   A design document first, then a Windows S0 round, then the stages.

Also on 2026-11-04 the author asked for the same experience as on the Mac. They then asked for
Windows UI and UX conventions to win wherever they differ, using Claude's judgement. So the rule
is: **the same features, rules and safety as macOS, presented the way a Windows user expects.**
Each decision below says which way it went and why.

*   **D1. "Set up command line" edits the user `PATH` itself.** This differs from macOS, which prints
    two lines to paste. Windows users expect a setup step to do this, and nothing on Windows matches
    "paste this into your shell file". The risk that made macOS print the lines is handled
    differently: the tool only adds or removes its own one entry, writes the old value to
    `writes.log` first, and `cpm uninstall` takes the entry out again. Tab completion in PowerShell
    is a checkbox, off by default, because it means editing the user's own `$PROFILE` file (flow 10).
    Alternative P.
*   **D2. Stop never forces a session to end.** Same as macOS, which sends SIGINT, then SIGTERM, and
    never SIGKILL. Windows has no signals. Stop sends Ctrl+C, then a second Ctrl+C where SIGTERM would
    come, then reports "did not stop" with the PID (flow 9). Mac S0 check 18 found that SIGINT
    alone ended a session in about a second. Alternative Q.
*   **D3. Wrappers are compiled exes, built with Native AOT.** On macOS the wrapper is a zsh script
    that starts at once. A compiled native exe is the Windows way to keep that, and to pass
    arguments through unchanged in every shell (Alternative O). The Visual Studio C++ build tools
    are a build requirement, as Command Line Tools are on macOS. There is no slower fallback build.
*   **D4. Start with Windows, on by default.** This differs from macOS, which has no login item. On
    Windows, a tray tool that disappears after a restart looks broken, and tray tools normally
    start with Windows. "Start with Windows" is a tick in the tray menu. It is one value under
    `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, which turning it off, uninstalling the app or
    `cpm uninstall` removes. It shows in Task Manager's Startup apps, where Windows users look for it.
*   **D5. No third-party code, including in tests.** Same as macOS, which has no third-party Swift
    dependencies and tests with Apple's Swift Testing. The Windows version tests with MSTest, which
    is Microsoft's.
*   **D6. App-opened terminal windows close when Claude Code ends normally.** This differs from macOS,
    where Terminal.app keeps the window. On Windows, closing a tab when its program ends is the
    normal behaviour. The window stays open, with "Press Enter to close", only when something went
    wrong: a non-zero exit code, a missing folder, or a refused launch, so an error is never lost
    (flow 2).
*   **D7. Windows 11 look.** WPF's Fluent theme: it follows the system light or dark mode and accent
    colour, and uses the system font. The panel opens above the tray the way the Wi-Fi and volume
    flyouts do, and Esc closes it.
*   **D8. A Windows notification when a sign-in finishes.** The tray panel is usually closed while the
    user signs in, in the browser and terminal. So, when the app sees a new sign-in, it shows a
    notification: "<name> signed in as <email>. Open to keep or retry." Clicking it opens the panel
    at that step. Nothing is kept without the user pressing Keep, as on macOS. This uses the tray
    icon's own notification call, which needs no extra library.
*   **D9. Listed in Settings > Apps > Installed apps.** `build.ps1 -Install` registers a per-user
    uninstall entry. Uninstalling there removes the app, its Start menu shortcut and its startup
    value. It leaves profiles alone and says that `cpm uninstall` removes them, as on macOS, where
    deleting the app leaves profiles too.

## Open Questions

*   Every row of the Windows S0 table is an assumption until its check passes.

## Experience: macOS to Windows

The same screens, steps and features as the macOS app (`Sources/ProfileUI`, `docs/E2E-checklist.md`),
presented the Windows way (D1 to D9). `docs/E2E-checklist-windows.md` keeps the macOS checklist's
parts and order, so the two can be compared.

| macOS | Windows |
| --- | --- |
| Coloured dot in the menu bar, in the default profile's colour; no Dock icon | Coloured dot in the notification area, in the default profile's colour. Hovering shows "Claude Profiles — plain claude uses <name>". The panel has no taskbar button. Windows first puts new icons in the overflow (^) area; the first-run notification says how to pin it |
| Click the dot: a 400-point-wide panel | Left-click the dot: the same panel, 400 px wide, above the tray like the Wi-Fi flyout (D7). It closes on Esc or when it loses focus. Right-click shows the usual tray menu: Open Claude Profiles, Start with Windows (tick), Exit |
| Panel: "Claude Profiles", "plain claude uses <name>", "Filter by title, folder or profile", "Set up shell first" notice, profile rows, session rows, footer Add profile…, Open window…, Copy setup…, Doctor, Quit | The same text and order, with Windows words: "Set up command line" instead of "Set up shell", Exit instead of Quit |
| No sign-in alert; the step shows when you open the panel | A Windows notification when a sign-in is seen; clicking it opens that step (D8) |
| Light or dark from macOS | Light or dark and accent colour from Windows (D7) |
| Not started after a restart | Starts with Windows unless that tick is cleared (D4) |
| Profile row: dot, name, DEFAULT badge, Make default link, New Terminal, "⋯" (Open VS Code, Settings…), Sign in / Finish sign-in while not signed in | The same |
| Session row: title, "<profile> · <when> · <folder>", Stop for a live row, Resume otherwise | The same |
| Flows inside the panel: add, waiting for sign-in, keep or retry, settings, remove, doctor, set up shell, stop, copy setup | The same flows and wording. "Terminal window" becomes "terminal window", and "Trash" becomes "Recycle Bin" |
| Open window…: sidebar with All profiles and each profile, a session table, "<n> sessions. Running ones are rechecked every 2 seconds while this window is open.", "New Terminal on <name>"; flows as sheets | The same layout. Flows open as a dialog over the window. Right-clicking a profile or a session gives its actions; Enter resumes the selected session and Delete asks to stop a live one. The window has a taskbar button, because on Windows a window without one is hard to get back to, and it remembers its size and position. Closing it leaves the app in the tray |
| Live status rechecked every 2 seconds only while the panel or window shows; a new session within 5 seconds, a stopped one within 10 | The same |
| Idle: about 0% CPU; about 74 MB at launch with no profiles (E2E 7.11) | About 0% CPU when idle, and under 100 MB, measured in the Windows E2E |
| New Terminal: a Terminal.app window with the profile in its title; the window stays open after Claude Code exits | A window in the default terminal (Windows Terminal here) with the profile in its title. It closes when Claude Code ends normally, and stays with "Press Enter to close" after an error (D6) |
| Status line `● <name>` in the profile's colour | The same, from `claude.exe --profile-label` |
| `claude`, `claude-as <name>`, `cpm …` with the same commands and messages | The same, in PowerShell, Command Prompt and Git Bash |
| Tab completion of profile names after `claude-as` in zsh | Tab completion in PowerShell, if its checkbox was ticked (D1) |
| "Set up shell…" → Install → two commands → "Copy commands" → open a new window | "Set up command line…" → a checkbox for PowerShell tab completion → Set up → "Done. Open a new terminal window to use claude-as." (D1) |
| `./make-app.sh --install` puts the app in `~/Applications` | `windows\build.ps1 -Install` puts it in `%LOCALAPPDATA%\Programs\ClaudeProfiles\`, adds a Start menu shortcut and an Installed apps entry (D9), and starts it |
| No permission prompts, because a locally built app has no quarantine flag | No SmartScreen prompt, because a locally built exe has no Mark of the Web. Smart App Control is off on this PC (S0 check C21) |
| Remove moves the folder to the Trash | Remove moves the folder to the Recycle Bin, after deleting its login file outright (flow 7) |
| `cpm uninstall` leaves the app; you delete it from `~/Applications` | `cpm uninstall` removes profiles, the `PATH` entry and the startup value. The app is removed from Settings > Apps > Installed apps (D9) |
| Dialog buttons in macOS order, default on the right | Windows order: the default button first, then Cancel, on the right. Enter presses the default and Esc cancels |

## Technical Plan (Architecture)

### Terms: changes

*   **Login file:** `<config folder>\.credentials.json`. Its contents are never read. Only
    whether it exists is checked.
*   **`configDir`:** still created once and then only read, but now for consistency, not
    because a hash depends on it. Its form is an absolute long path from
    `GetFinalPathNameByHandle`: drive letter in upper case, backslashes, no trailing
    backslash, no 8.3 short names such as `ABDULS~1`, no `\\?\` prefix. Paths are compared
    without regard to case, as Windows does.
*   **Wrapper:** `claude.exe` and `claude-as.exe` in `%USERPROFILE%\.claude-profiles\bin\`,
    first in the user `PATH`.
*   **`$HOME`** in the base design means `%USERPROFILE%` here. Tests set `USERPROFILE`, and
    ProfileCore reads it from the environment as the Mac code reads `HOME`.

The profile fields `keychainBaseline` and `keychainService` stay in the `profiles.json` schema and
are always `null` on Windows. `loginStartedAt` is still used.

### Components

```text
    ClaudeProfiles.exe (tray icon + panel + window, WPF)
              │
              ▼
    ProfileUI (WPF views + view models)               cpm.exe (command line)
              │                                          │
              └────────────────────┬─────────────────────┘
                                   ▼
  ProfileCore (class library, no WPF)
    ConfigLocator  ProfileStore  AccountReader  SessionIndex  Launcher
    ProcessRunner  SessionStopper  SetupCopier  ClaudeCLI  CredentialProbe
    LoginCoordinator  ProfileRemover  ShellInstaller  RecycleBin
    CompatCheck  Doctor  EventLog  WinProcess (P/Invoke)
       │                │               │                 │
       ▼                ▼               ▼                 ▼
  .claude-profiles\  <profile>\     run\*.launch      listed items in
  profiles.json      projects\      new console       an added profile
  default, names     sessions\*.json window

  bin\claude.exe, bin\claude-as.exe (Wrapper project, Native AOT)
     reads default and names on every call, never JSON,
     app need not be running ─────────────────────────► real claude.exe
```

Changes from the base components:

*   **KeychainProbe → CredentialProbe.** `hasLogin(profile)` returns whether the profile's
    `.credentials.json` exists. It never opens the file. It also lists names in Windows
    Credential Manager that start with `Claude Code` (names only), for doctor, in case a later
    Claude Code moves the login there (S0 check C1).
*   **WinProcess** holds every Windows API call: process alive, start time, console Ctrl+C,
    starting a process in a new console. It is the only file with P/Invoke, so the rest of
    ProfileCore stays testable through interfaces, as in Swift (`IProcessInfo`,
    `ITerminalOpener`, `IProcessRunner`, `ICredentialProbe`).
*   **RecycleBin** moves a folder to the Recycle Bin through the shell (`IFileOperation`). It
    refuses, rather than deleting outright, when the drive has no Recycle Bin.
*   **FileWatcher** uses `FileSystemWatcher`. A buffer overflow event triggers a full re-read.
*   **Wrapper** is its own small project that compiles one source file shared with ProfileCore:
    the `names` and `default` reader. One exe is built and copied as `claude.exe` and
    `claude-as.exe`; it decides what to do from its own file name. The same exe also answers
    `--profile-label <id>` (the status line) and `--send-ctrl-c <pid>` (flow 9).

### Files on disk: changes

```text
%USERPROFILE%\.claude              built-in profile folder        (never written)
%USERPROFILE%\.claude.json         built-in config file           (never written)
%USERPROFILE%\.claude-profiles\
  profiles.json  default  names  .lock     same formats as macOS. UTF-8, no BOM, LF line ends
  bin\         cpm.exe, claude.exe, claude-as.exe, claude-profiles.ps1, VERSION
  profiles\<id>\                  an added profile's CLAUDE_CONFIG_DIR
    .claude.json  .credentials.json  settings.json  projects\  sessions\
  backups\<id>\<time>\            as macOS
  run\<uuid>.launch               launch requests from the app, deleted when read (flow 2)
  logs\launch.log  logs\writes.log
HKCU\Environment\Path             the bin folder first, added by "Set up command line" (D1)
$PROFILE                          optional marked line that loads claude-profiles.ps1 (D1)
HKCU\...\CurrentVersion\Run\ClaudeProfiles         start with Windows (D4)
HKCU\...\CurrentVersion\Uninstall\ClaudeProfiles   Installed apps entry (D9)
%LOCALAPPDATA%\Programs\ClaudeProfiles\   the app, with a copy of the bin files
Start menu\Programs\Claude Profiles.lnk   shortcut made by build.ps1 -Install
```

There are no file modes. Everything sits under `%USERPROFILE%`, whose default permissions
allow only the user, SYSTEM and Administrators. `doctor` warns if `.claude-profiles` gives access
to anyone else. The `names` file's `configDir` column holds a Windows path; tabs and newlines
cannot occur in one.

Every file the tool replaces is written to a temporary file in the same folder and then moved
over the original with `File.Replace` or `MoveFileEx(MOVEFILE_REPLACE_EXISTING)`. Windows refuses
that move while another program has the file open without sharing it, so each replace retries
for up to 2 seconds and then reports which program may hold it. The wrapper opens `names` and
`default` with full sharing, so it never blocks a replace.

`.lock` is held by opening it with `FileShare.None`, retried every 50 ms for up to 10 seconds.

### Flow 1: launching from the shell

The rules are the base design's. What changes is how they run.

```text
claude.exe [args] / claude-as.exe <name> [args]:
  same choices as base flow 1: override, CLAUDE_PROFILE_ID, default, names, ownership check,
  login-variable warning
  build the environment: set or remove CLAUDE_CONFIG_DIR, set CLAUDE_PROFILE_ID and
    CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1
  if a console is attached: set its title to the display name (SetConsoleTitleW)
  append one line to logs\launch.log
  find the real claude.exe (below)
  start it as a child in the same console, with the same standard handles, and [args]
  while it runs: ignore Ctrl+C and Ctrl+Break in the wrapper itself
  when it exits: exit with its exit code
```

**No `exec`.** Windows cannot replace a process with another, so the wrapper starts Claude Code
as a child and waits. Three rules follow from that:

*   The wrapper must ignore console Ctrl+C while the child runs. Otherwise one Ctrl+C would end the
    wrapper, the shell would take the console back, and Claude Code would keep running in the
    same window. Claude Code still gets Ctrl+C, because console control events reach every
    process on the console.
*   The wrapper does not put Claude Code in a Job object. If the wrapper is ended from Task
    Manager, Claude Code keeps running, as it would with no wrapper. A kill-on-close job would
    end Claude Code without warning in the middle of an edit.
*   Exit codes pass through unchanged, so scripts that check `claude -p` keep working.

**Arguments.** Windows passes one command-line string, and each program splits it. The wrapper
quotes each argument with the rules `CommandLineToArgvW` and the C runtime use (.NET
`ProcessStartInfo.ArgumentList`). S0 check C16 confirms that `claude.exe` splits them the same way,
including quotes, backslashes before quotes, empty arguments and non-ASCII text. If it fails,
`claude.exe` (default) passes the rest of its own command line through as-is, and only
`claude-as.exe` re-quotes.

**Finding the real binary.** Search each `PATH` entry in order, skip any that resolves to the bin
folder, and use the first `claude.exe`. `claude.cmd`, `claude.bat` and `claude.ps1` are never run.
Running a batch file passes arguments through `cmd.exe` parsing, which cannot be quoted safely. If
only one of those is found, the wrapper says so and points to the native installer.

**Search order on Windows.** PowerShell finds aliases and functions before files, and every shell
looks at the system `PATH` before the user `PATH`. So the wrapper wins only if no `claude` comes
earlier. On this PC the real binary's folder is in the user `PATH`, so putting the bin folder first
in the user `PATH` is enough. `doctor` checks the real order (flow 11).

**Title.** `SetConsoleTitleW` sets the tab title in Windows Terminal and the window title in a
classic console. S0 check C8 confirms that `CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1` keeps Claude
Code from replacing it.

**Completion.** `bin\claude-profiles.ps1` registers an argument completer for `claude-as` that
reads names from `names`, as `claude-profiles.zsh` does for zsh. It does nothing if `names` cannot
be read. The user adds one line to their PowerShell profile to load it (flow 10). Command Prompt has
no completion. Git Bash is not covered in v1.

### Flow 2: launching from the app

There is no `.command` file. The app writes a launch request, which is data and is never run:

```text
run\<uuid>.launch  (UTF-8 JSON)
  { "commandName": "work", "folder": "D:\\Projects\\x", "arguments": ["--resume", "<uuid>"],
    "createdAt": "2026-11-04T10:50:45Z" }
```

Then it starts `bin\claude-as.exe --launch <path to that file>` in a new console window
(`CreateProcess` with `CREATE_NEW_CONSOLE`). Windows opens that in the user's default terminal,
which is Windows Terminal on this PC. The wrapper, in launch mode:

```text
1. Accepts the file only if it is directly inside run\, is under 60 seconds old, and parses.
   Reads it, then deletes it.
2. Changes to `folder`. If it is missing: prints "Folder not found: <folder>", waits for Enter, exits 1.
3. Runs the claude-as rules for commandName and arguments, exactly as flow 1.
4. When Claude Code exits with code 0: the wrapper exits and the window closes.
   With any other code: prints "Claude Code ended with code <n>. Press Enter to close." and waits.
```

Step 4 follows D6. A window closing when its program ends normally is what Windows users expect.
Keeping it open after an error means an ownership refusal or a failed sign-in is never lost. After
a successful sign-in the window may close at once, and the app's notification (D8) shows who signed
in. A request older than 60 seconds is deleted at the next launch.

The folder never passes through a command line or a shell, so hostile folder names from
transcript data have no way to run anything. The command line holds only the fixed bin path and
the request path, both under `%USERPROFILE%\.claude-profiles`. A Windows user name cannot contain
`"` or `;`.

Without the shell layer installed, the app launches nothing, as on macOS.

**Finding the real binary from the app.** An app started before the user changed `PATH` still has
the old `PATH`. The app therefore reads `Path` from the registry (system, then user, with `%VAR%`
expanded) each time it runs a command, instead of the base design's `zsh -lic` read.

### Flow 3: adding a profile and detecting the login

The same steps, without the Keychain:

```text
3. (removed) No baseline is taken.
5. Watch the folder until .claude.json holds oauthAccount and .credentials.json exists.
6. (removed) Nothing is recorded. keychainService stays null.
```

Removal never needs a manual credential step, because the login is a file in the folder that is
removed (flow 7).

### Flow 4: listing sessions

*   `procStart` is a JSON string of 15 to 19 digits, read as 100 ns ticks since 1601-01-01 UTC. A JSON number is accepted too. The string format from
    macOS is also accepted, so the same parser works on recorded test data. Anything else
    counts as unknown, and the row says "may be stale".
*   **Alive:** `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` and `GetExitCodeProcess` returns
    `STILL_ACTIVE`. Access denied counts as alive, as `EPERM` does on macOS.
*   **Start time:** `GetProcessTimes` creation time, compared within 1 second (S0 check C9).
*   **`pidDomain`:** a record whose `pidDomain` is present and is not `win32:<this computer name>`
    describes a process on another machine. Its PID is never checked or signalled here. For safety
    checks it counts as live if the file changed in the last 10 minutes.
*   File times come from `FileInfo.LastWriteTimeUtc`, replacing `stat`.

### Flow 5: where the default applies

On Windows, `PATH` comes from the registry and almost every new process gets it, so the wrapper is
reached far more widely than on macOS.

| Started from | Follows the default? |
| --- | --- |
| PowerShell, Command Prompt, Git Bash, opened after `PATH` was set | Yes |
| A shell or app that was already running when `PATH` was set | No, until it is restarted. It has the old `PATH` |
| VS Code's integrated terminal | Yes, if VS Code was started after `PATH` was set |
| VS Code's Claude Code extension | No. It runs its own bundled binary. Use "Open VS Code" (flow 12) |
| Run dialog, scheduled tasks, scripts | Yes |
| A PowerShell alias or function named `claude` | No; it wins. `doctor` checks the PowerShell profile for one |
| A `claude` earlier in the system `PATH` | No; it wins. `doctor` reports it |
| Calling the full path to `claude.exe` | No; uses the built-in profile |
| Claude desktop app and Cowork | No, if they use their own binary (S0 check C19) |
| WSL | No; not supported |
| A process that set `CLAUDE_CONFIG_DIR` outside `.claude-profiles` | No; its value wins, with a warning |
| A `claude` call from inside a running session | No, by design; it stays on that profile |

### Flow 6: copying setup

The rules and table are unchanged. Windows details:

*   "Points into the source profile's folder" compares paths without case, with `\` and `/` treated
    as the same, and also checks the 8.3 short form of the folder.
*   A restored file gets back its read-only attribute, which stands in for the file mode.
*   MCP servers often run through `cmd /c npx …` on Windows. They are copied as JSON, never run, so
    nothing changes. The secret scan applies to every argument, including the ones after `/c`.
*   A ticked MCP secret is passed on Claude Code's command line for a moment, as on macOS. On
    Windows, other programs run by the same user can read a process's command line. The preview
    says so.

### Flow 7: removing a profile

```text
1-3. As base: confirm, refuse on a live session, move the default, mark deleting.
4.   claude auth logout under the profile's environment (S0 check C3).
5.   If .credentials.json is still there (logout failed or did not remove it), delete that one
     file permanently. A login token is never put in the Recycle Bin.
6.   Move the folder and its backups to the Recycle Bin. If the drive has no Recycle Bin, stop
     and say so. The row stays `deleting` and nothing is lost.
7.   Remove the row.
```

Step 5 replaces the Keychain step. The built-in profile's `.credentials.json` is never touched,
and ProfileRemover refuses any path that is not inside `.claude-profiles\profiles\`.

`cpm uninstall` is the same as on macOS. It also takes the bin folder out of the user `PATH`,
removes the completion line from `$PROFILE` if the tool added it, and removes the startup value
(D1, D4). It leaves the app installed; Settings > Apps > Installed apps removes that (D9).

### Flow 8: compatibility

Unchanged, except doctor checks that `.credentials.json` exists where Keychain names were
checked. If a later Claude Code stores Windows logins elsewhere, profiles will look signed out,
and doctor lists any Credential Manager names that start with `Claude Code`.

### Flow 9: stopping a session

Windows has no signals. The nearest thing to SIGINT is a console Ctrl+C event, and it can only be
sent from a process attached to the same console.

```text
1-2. As base: check the PID and start time, and confirm with the user.
3.   Start a hidden helper: bin\claude.exe --send-ctrl-c <pid>. The helper calls FreeConsole,
     AttachConsole(pid), SetConsoleCtrlHandler(NULL, TRUE) so it is not ended itself, and
     GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0).
4.   Wait up to 5 seconds, checking every 250 ms.
5.   Still alive (same PID and start time): send a second Ctrl+C, where macOS sends SIGTERM.
     Wait up to 5 seconds more.
6.   Still alive: report "did not stop" and give the PID. Nothing stronger is sent (D2).
7.   Log the result.
```

A helper process does this because attaching to another console changes the calling process's
console, and the app and `cpm` must keep their own.

The event reaches **every program in that console window**, as if the user pressed Ctrl+C there.
The wrapper ignores it, and the shell that started the wrapper is waiting and does nothing. Any
other program the user started in the same window may also stop. The confirmation says so.

The app never closes a window itself. Once the session ends, a window the user opened goes back to
its shell, and an app-opened window follows D6. The confirmation and `cpm stop` therefore say "The
session ends. Its window goes back to its shell, or closes if the app opened it", instead of the
macOS "The Terminal window stays open".

If S0 check C7 fails, Stop is removed from v1 and the app shows the PID and the window to close
instead. A forced end is Alternative Q.

### Flow 10: installing and upgrading

```text
1. Create .claude-profiles and the built-in row, if missing. Never reset the default.
2. Copy cpm.exe, claude.exe, claude-as.exe to bin\ from the folder beside the running cpm.exe,
   or from the app's install folder. Write bin\VERSION.
   An exe that is running cannot be overwritten: rename it to <name>.old-<time> first, and
   delete old copies at the next install.
3. User PATH (D1), in HKCU\Environment\Path:
     if the bin folder is already the first entry: nothing to do
     otherwise: log the old value to writes.log, remove any other copy of the bin folder entry,
     put it first, write the value back with its type (REG_EXPAND_SZ) unchanged, and tell
     running programs with WM_SETTINGCHANGE "Environment", so new windows from Explorer get it
4. If the user ticked tab completion: add one marked line to $PROFILE (PowerShell 7 and Windows
   PowerShell, each if it exists or its folder does), starting with a newline:
     . "$env:USERPROFILE\.claude-profiles\bin\claude-profiles.ps1"   # claude-profiles
   Skip a profile that already has the marked line.
5. Say "Done. Open a new terminal window to use claude-as." Shells and apps already open keep
   the old PATH until they are restarted.
6. doctor confirms that claude resolves to the wrapper for a new process.
```

Each step is safe to repeat. `cpm install-shell` does the same, with `--completion` for step 4. Only
an entry equal to the bin folder, ignoring case and a trailing backslash, is ever moved or removed,
and every other entry keeps its text and order. If the user `PATH` is close to the registry's size
limit, or cannot be written, nothing is changed and the error is shown with the command to run by
hand. The `$PROFILE` line is found again by its `# claude-profiles` marker, so removal takes out only
that line. If PowerShell's execution policy stops profiles from running, the line has no effect.
Doctor gives that as a note, and nothing else depends on it.

`build.ps1 -Install` installs the app per user in `%LOCALAPPDATA%\Programs\ClaudeProfiles\`. It adds
a Start menu shortcut, which carries the app's ID so its notifications show its name. It also adds
the startup value (D4) and the uninstall entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\ClaudeProfiles` (D9), then starts the app.
The uninstall entry runs `ClaudeProfiles.exe --uninstall-app`. That exits any running copy, removes
the shortcut, the startup value, the entry and the app folder, and leaves `.claude-profiles` alone.
The app holds one named mutex so only one copy runs; starting it again opens the panel of the one
already running. On upgrade, it replaces `bin\` when its version differs from `bin\VERSION`, as on
macOS.

On first start, if the shell layer is not installed, the panel opens by itself with the "Set up
command line" notice. A notification says the icon may be in the overflow (^) area and can be
dragged next to the clock.

### Flow 11: doctor

Rows that change:

| Check | Level | Output |
| --- | --- | --- |
| Logged in: `oauthAccount` present and `.credentials.json` exists | Fault | "Not signed in" with the sign-in step |
| ~~Recorded Keychain name exists~~ | — | Removed |
| Credential Manager names starting with `Claude Code` | Note | Names only; a sign that the login storage changed |
| `claude` resolves to the wrapper: the system `PATH` then the user `PATH` from the registry, with the real-binary rule | Fault | Shows both paths and any `claude` that comes first |
| PowerShell profile defines an alias or function named `claude` | Note | Names the profile file |
| Real binary is `claude.exe`; a `claude.cmd` is on `PATH` | Fault if only the `.cmd` exists | Points to the native installer |
| A profile's `configDir` uses an 8.3 short name or a different spelling of its path | Fault | Names the profile |
| `.claude-profiles` gives access to other users | Note | Names the account |
| The profiles drive has a Recycle Bin | Note | Removal would stop at step 6 |
| Smart App Control is on and the wrapper is unsigned | Note | Windows may block the wrapper (S0 check C21) |

### Flow 12: opening VS Code on a profile

Launcher finds `Code.exe` (user install, then system install, then `code` on `PATH`). If any
`Code.exe` from that install is running, it refuses, as on macOS, because a new `Code.exe` hands its
request to the running one and its environment is lost. Otherwise it starts `Code.exe [folder]`
directly with `CLAUDE_CONFIG_DIR` and `CLAUDE_PROFILE_ID` set on that process (unset
`CLAUDE_CONFIG_DIR` for the built-in profile). S0 check C15 confirms that the extension gets them.

### Status line

Each added profile's `settings.json` gets:

```json
"statusLine": { "type": "command", "command": "\"C:\\Users\\Abdul Samad\\.claude-profiles\\bin\\claude.exe\" --profile-label <id>" }
```

The path is quoted because home folders can have spaces. How Claude Code on Windows runs this
command, and whether colour codes show, is S0 check C11. If Git Bash runs it, the path is
written with forward slashes instead.

### Build

*   **.NET 10 SDK** (the current long-term support release), `windows/` as:

    ```text
    windows/
      ClaudeProfiles.slnx         solution, for editors only; build.ps1 does not need it
      Directory.Build.props        shared settings: nullable on, warnings as errors
      src/ProfileCore/             class library, net10.0-windows
      src/Cpm/                     console app → cpm.exe
      src/Wrapper/                 console app, Native AOT → claude.exe / claude-as.exe
      src/ProfileUI/               WPF views and view models
      src/ClaudeProfiles/          WPF tray app → ClaudeProfiles.exe (no console window)
      src/Shell/claude-profiles.ps1  PowerShell completion for claude-as
      tests/ProfileCore.Tests/     MSTest
      tests/Wrapper.Tests/         MSTest; runs the built wrapper against a fake claude.exe
      tests/ProfileUI.Tests/       MSTest; view models only
      build.ps1                    build, test, publish to build\, -Install
    ```

*   **Requirements**, in the style of the macOS README: Windows 11, the .NET 10 SDK, the Visual
    Studio Build Tools with "Desktop development with C++" (for Native AOT, D3), the native
    Claude Code (`claude.exe`), and Windows Terminal or the classic console. Visual Studio
    itself is not needed. Neither the SDK nor the C++ tools are on this PC yet:
    `winget install Microsoft.DotNet.SDK.10` and `winget install Microsoft.VisualStudio.2022.BuildTools`
    with the C++ workload. `build.ps1` checks for both and says what is missing.
*   **Tray icon:** WPF has none, so the app uses WinForms `NotifyIcon` (built into .NET) inside a WPF
    app. Its balloon call shows as a normal Windows notification (D8). The panel is a borderless
    WPF window placed above the tray, on the screen that holds the taskbar.
*   **Look:** WPF's built-in Fluent theme (`ThemeMode="System"`), with per-monitor DPI awareness, so
    it follows light or dark mode, the accent colour and display scaling (D7).
*   **Signing:** optional. `SIGN_CERT` names a certificate thumbprint, and `build.ps1` signs with
    `signtool`. Otherwise the exes are unsigned (S0 check C21).

## Windows S0 checks

Run these in stage W0, before stage W1, in a copy of the base S0 process: back up `%USERPROFILE%\.claude` and
`%USERPROFILE%\.claude.json` first (with `robocopy`, excluding `sessions` and the login file), and use test folders
under `%USERPROFILE%\.cpm-s0\`. Results go in `docs/S0-results-windows.md`. "Base" is the macOS
check with the same subject.

| # | Base | When | Assumption | Passes when | Stops the project? | If it fails |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | 1, 2, 3 | W0 | Each `CLAUDE_CONFIG_DIR` folder keeps its own login in `<dir>\.credentials.json`, the built-in login stays in `~\.claude\.credentials.json`, and nothing goes to Credential Manager | Built-in plus two folders (two accounts) all logged in, before and after a reboot; built-in account unchanged; no new Credential Manager names | **Yes** | The design does not work |
| C2 | 4, 20 | W0 | An added profile's config file is `<dir>\.claude.json`; `auth status --json` agrees | `oauthAccount` found there | No | ConfigLocator changes |
| C3 | 6, 7 | W0 | `auth logout` under a folder removes only that folder's `.credentials.json` and `oauthAccount` | Other profiles still logged in | No | Removal deletes the file itself (flow 7 step 5 already does) |
| C4 | 5 | W0 | `--resume <id>` works under a custom folder and from another working folder | Same conversation and ID | No | As base |
| C5 | 8 | W0 | `mcp add-json --scope user`, `plugin marketplace add` and `plugin install` land in the folder | As base, with Windows paths | No | As base |
| C6 | 13 | W0 | What each login variable does | Recorded | No | Reword the warning |
| C7 | 18 | W0 | A console Ctrl+C sent by an attached helper ends an interactive session, idle and busy, in Windows Terminal and in a classic console, with the wrapper as parent. Record whether one or two are needed, and what the shell does | Ends within 10 seconds; window stays open; wrapper exits with Claude Code's code | No | Stop is removed from v1 (flow 9) |
| C8 | 19 | W0 | With `CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1`, a title set with `SetConsoleTitleW` stays for a whole session in Windows Terminal | Title unchanged at the end | No | As base: `--settings` status line (Alternative I) |
| C9 | 15 | W0 | `procStart` matches `GetProcessTimes` to 1 second. Partly seen: 2 records, a CLI one and a desktop app one, matched exactly on 2026-11-04 | All live records match, including a desktop app one and one in an elevated window | No | As base: PID only, rows "may be stale" |
| C10 | 16 | W0 | `entrypoint` values on Windows: `cli` and `claude-desktop` seen; VS Code, `claude -p` and Cowork still to see | Recorded | No | Hide only `claude-desktop` |
| C11 | 10 | W1 | A status line command runs on Windows: which shell runs it, a quoted path with a space, colour codes | Label shows in a session | No | Added profiles rely on the window title |
| C12 | 14 | W1 | The wrapper resolves first in new PowerShell 5 and 7, Command Prompt, Git Bash, VS Code's terminal and the Run dialog, and still does after `claude update` | Wrapper reached in each | No | Change the `PATH` advice |
| C13 | 23 | W0 | File-format key names on Windows: session records, transcripts, `settings.json`, `.claude.json`. Partly seen (session record keys, above) | Recorded | No | Change required keys and fixtures |
| C14 | 11 | W0, first | No existing Windows tool already does this. Time-boxed | Each tool recorded as adopt, borrow or reject | No | Revisit the plan |
| C15 | 25 | W0 | `Code.exe` started with the two variables gives them to the Claude Code extension; a running VS Code ignores them | Values seen in the extension's sessions | No | Drop "Open VS Code" |
| C16 | — | W0 | Arguments pass through the wrapper unchanged: spaces, quotes, `\"`, trailing `\`, empty strings, non-ASCII, `%PATH%` | Real `claude.exe` sees each argument as given | **Yes**, unless the pass-through fallback in flow 1 works | Pass the raw command line through |
| C17 | — | W1 | Ctrl+C typed in a wrapped session works as without the wrapper; the wrapper survives it and returns Claude Code's exit code | Same behaviour | No | Fix the wrapper's console handling |
| C18 | — | W1 | `claude update` while a wrapped session runs: the update renames the running `claude.exe` and the session is unaffected | Session and update both fine | No | Doctor warns to update with no sessions running |
| C19 | 12 | W1 | The desktop app and Cowork use their own binary, not `claude` on `PATH`, once the wrapper is first on `PATH` | Their sessions stay in `~\.claude`, and `launch.log` shows no line for them | No | Document it in flow 5 and warn in doctor |
| C20 | 9 | W3 | Starting a process in a new console from the tray app opens the default terminal, with no prompt | Window opens in Windows Terminal | No | Start `wt.exe` explicitly (Alternative R) |
| C21 | 21 | W1 | Windows Defender and Smart App Control allow a self-built, unsigned `claude.exe` wrapper. Smart App Control was seen off on this PC on 2026-11-04 | Runs with no block or prompt | No | Sign with a local certificate |
| C22 | — | W1 | Time the Native AOT wrapper adds to each launch | Under 50 ms | No | Find what is slow before stage W2; the wrapper stays compiled (D3) |

Base checks 17 and 24 have no Windows equivalent. Check 22 (one session resumed twice) is
not repeated, because it tests Claude Code, not the platform.

## Alternatives Considered

### Alternative N: Swift on Windows, sharing ProfileCore

*   **Pros:** One codebase for the rules. Swift's Windows toolchain builds Foundation code.
*   **Cons:** SwiftUI and AppKit do not exist on Windows, so the app still needs a second UI stack.
    ProfileCore uses `flock`, `sysctl`, `Darwin.write`, FSEvents, the Keychain and `trashItem`,
    so a large part needs a Windows version either way. The toolchain is less common on Windows,
    and a fast wrapper exe is harder to build.
*   **Reason for Rejection:** The author chose C# (2026-11-04). The rules are shared through the
    design and the file formats instead of through code.

### Alternative O: Wrapper as `claude.cmd` or `claude.ps1`

*   **Pros:** No compiler. Readable, like the zsh scripts.
*   **Cons:** A `.cmd` wrapper prints "Terminate batch job (Y/N)?" after Ctrl+C, and `cmd.exe`
    changes arguments with `%`, `^`, `&` and quotes, which cannot be fixed fully. A `.ps1` is not
    found by Command Prompt or Git Bash, and PowerShell's execution policy can block it. Both start
    slowly.
*   **Reason for Rejection:** The wrapper must pass any argument through unchanged in every shell.

### Alternative P: print the `PATH` command for the user to run, as macOS does

*   **Pros:** Matches the macOS rule that the tool never edits the user's shell setup. The user sees
    exactly what changes.
*   **Cons:** Pasting a registry command into PowerShell is not how Windows tools are set up, and
    an error in pasting it can damage `PATH`. Windows has no shell file whose last line the user
    owns, so the macOS reason fits less well.
*   **Reason for Rejection:** D1. The tool's own change is narrower than a pasted command (one
    entry, the old value logged, removed on uninstall). If writing fails, the command is still
    shown for the user to run.

### Alternative Q: Stop with `TerminateProcess` if Ctrl+C fails

*   **Pros:** Stop always works.
*   **Cons:** It is SIGKILL: no clean-up, and a file edit can be left half done. The base design
    rules it out.
*   **Reason for Rejection:** D2 keeps the macOS rule. The user can still close the window
    themselves.

### Alternative R: Open sessions with `wt.exe` arguments

*   **Pros:** Control over new window or tab, tab title and colour.
*   **Cons:** `wt.exe` gives `;` its own meaning and parses its arguments again, so a folder or
    argument from transcript data would need its own escaping, the same problem the base design
    solved for `.command` files. It ties the tool to Windows Terminal.
*   **Reason for Rejection:** A new console plus a launch request file passes no data on a command
    line, and respects the user's default terminal. Kept as the fallback for S0 check C20.

### Alternative S: WinUI 3 instead of WPF

*   **Pros:** A newer look, Fluent controls.
*   **Cons:** Needs the Windows App SDK, more packaging steps, and has no tray icon of its own.
*   **Reason for Rejection:** WPF ships with .NET and builds from the command line with nothing extra.

## Non-Functional Requirements: changes

*   **Security & Privacy**
    *   Never open `.credentials.json` or `sessions\*.key`, only check that they exist. Flow 7 step 5
        deletes an added profile's `.credentials.json` without reading it.
    *   Launch requests are data, never scripts (flow 2). The wrapper runs only `claude.exe`, never a
        batch file (flow 1).
    *   `.claude-profiles` keeps the default user-only permissions; doctor checks them.
*   **Performance**
    *   The wrapper adds under 50 ms to each launch (S0 check C22).
    *   While hidden, the app holds one `FileSystemWatcher` on `.claude-profiles` (ignoring `profiles\`)
        and polls nothing.
*   **Rollout & Rollback**
    *   Turning off: remove the bin folder from the user `PATH` (Settings > System > About >
        Advanced system settings > Environment Variables) and open a new shell. The old value is
        also in `writes.log`.
    *   Uninstall: as base. `cpm uninstall` also takes out the `PATH` entry, the `$PROFILE` line
        and the startup value. Installed apps removes the app.

## Risks & Mitigations: additions

| Risk | Impact | Mitigation | How it is detected |
| --- | --- | --- | --- |
| A `claude` earlier on `PATH`, or a PowerShell alias, beats the wrapper | Sessions start on the built-in profile | Doctor checks the real search order and the PowerShell profile | `doctor`; rows tagged by profile |
| Arguments change on the way through the wrapper | A prompt or flag reaches Claude Code altered | One quoting rule, tests with hostile values, pass-through fallback | Wrapper tests; S0 check C16 |
| Ctrl+C to a console stops other programs in that window | Unexpected stop of another program | Confirmation says so; Stop only for live rows | E2E checklist |
| A self-update replaces `claude.exe` while the wrapper runs | Launch fails for a moment | Wrapper looks for the binary at each launch, never caches it | S0 check C18 |
| Editing the user `PATH` damages it | Programs stop being found | Only the tool's own entry is moved or removed; old value logged first; value type kept; nothing written if near the size limit | `UserPath` tests on a test registry key; `writes.log` |
| Smart App Control or Defender blocks the unsigned wrapper | `claude` does not start | Doctor note; optional signing | S0 check C21 |
| A login token ends up in the Recycle Bin | Someone with the PC could restore it | `.credentials.json` is deleted outright before the folder is recycled | ProfileRemover tests |
| Windows refuses a file replace while a reader holds it | A write fails | Retries for 2 seconds, then a clear error; the wrapper shares its reads | Tests with a held file |

## Future Improvements

*   Tab completion in Git Bash.
*   A jump list on the Start menu shortcut with each profile's New Terminal.
*   Per-profile Windows Terminal colour schemes through a Terminal profile fragment.
*   WSL profiles.
*   An MSIX or winget package, if it is ever shared.

## Detailed Implementation Plan

Paths are relative to `windows/`. Each stage ends with its tests passing, as in the base plan.
The behaviour of each module is the base design's; the file list below says only what each holds.

| Stage | When it ends, you can | Main files |
| --- | --- | --- |
| W0 | Know whether to continue, with a backup in hand | `../docs/S0-checklist-windows.md`, `../docs/S0-results-windows.md`, `scripts/s0-backup.ps1`, `scripts/s0-checks.ps1` |
| W1 | `cpm add`, log in, use `claude` and `claude-as`; `cpm doctor` | `Directory.Build.props`, `build.ps1`; ProfileCore: `Model/Profile.cs`, `ConfigLocator.cs`, `ProfileStore.cs`, `FileLock.cs`, `AtomicFile.cs`, `AccountReader.cs`, `CredentialProbe.cs`, `LoginCoordinator.cs`, `ClaudeBinaryLocator.cs`, `RegistryPath.cs`, `ProcessRunner.cs`, `ClaudeCLI.cs`, `ShellInstaller.cs`, `Reconciler.cs`, `Doctor.cs`, `EventLog.cs`, `StatusLineWriter.cs`; `Shared/NamesFile.cs`; `src/Wrapper/Program.cs`, `ConsoleControl.cs`; `src/Cpm/Program.cs`; tests |
| W2 | List, start, resume and stop sessions from `cpm` | `WinProcess.cs`, `LiveSessionReader.cs`, `TranscriptReader.cs`, `SessionIndex.cs`, `Launcher.cs`, `LaunchRequest.cs`, `SessionStopper.cs`; wrapper `--launch` and `--send-ctrl-c`; `scripts/measure-panel.ps1`; `../docs/E2E-checklist-windows.md` |
| W3 | Do all of that from the tray app and window | `src/ProfileUI/*`, `src/ClaudeProfiles/App.xaml`, `TrayIcon.cs`, `Notifications.cs`, `FileWatcher.cs`, `StartupSetting.cs`, `AppInstall.cs` (shortcut, Installed apps entry, `--uninstall-app`); ProfileCore `UserPath.cs`, `PowerShellProfile.cs`; `src/Shell/claude-profiles.ps1` |
| W4 | Copy setup, in the same two steps as base S4 | `SetupCopier/CopyPlan.cs`, `SecretDetector.cs`, `SettingsMerger.cs`, `BackupStore.cs`, `SetupCopier.cs`, `src/ProfileUI/CopySetupView.xaml` |
| W5 | Remove a profile, and uninstall | `ProfileRemover.cs`, `RecycleBin.cs` |

**Tests to port first.** These carry the rules, so each Swift test has a C# case with the same name:
the ownership check on `--resume`, a broken `default` stopping plain `claude`, the built-in profile
never setting `CLAUDE_CONFIG_DIR`, a reused PID not counting as live, a hostile folder name (here:
in a launch request), secrets masked in copy setup, Restore leaving a changed file alone, removal
resuming after a crash. Windows adds: `procStart` as file-time ticks, argument round trips through
the wrapper, a replace while a reader holds the file, and `.credentials.json` never reaching the
Recycle Bin.

**If a Windows S0 check fails:**

| Check | Change to the plan |
| --- | --- |
| C1 | Stop. Do not start stage W1 |
| C16 | Use the raw command-line pass-through; if that also fails, stop |
| C7 | Remove Stop from `cpm` and the app (flow 9) |
| C8 | Return to the author with Alternative I |
| C9 | `mayBeStale` on every live row, as base |
| C11 | Added profiles rely on the window title |
| C15 | Remove "Open VS Code" and `cpm code` |
| C19 | Add the row to flow 5 and a doctor warning |
| C20 | Use `wt.exe` (Alternative R) |

### Verification Plan

*   **Build:** `windows/build.ps1`, warnings as errors.
*   **Unit and wrapper tests:** `windows/build.ps1 -Test`. Tests set `USERPROFILE` to a temporary
    folder and use a fake `claude.exe` that prints its arguments and environment.
*   **Real Claude Code:** with `CPM_REAL_CLAUDE` set, the tests also run `claude mcp` commands in
    throwaway folders, as on macOS.
*   **Performance:** `scripts/measure-panel.ps1` with 5,000 generated transcripts, and the wrapper
    launch time (S0 check C22).
*   **E2E:** `docs/E2E-checklist-windows.md`, a copy of the macOS checklist with the same parts and
    step numbers, and Windows commands. Where a step cannot match, it says why. It adds each shell
    in the flow 5 table.
