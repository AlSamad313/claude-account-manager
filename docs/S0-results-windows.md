# S0 results (Windows)

Claude Code 2.1.318 (native `claude.exe`), Windows 11 Pro 10.0.26200, x64, Windows PowerShell 5.1, Windows
Terminal. No account emails are recorded here. Check numbers are those in the Windows design's "Windows S0
checks" table.

**Decision to continue: not yet.** C16, one of the two checks that can stop the project, passes. C1, the
other, needs the sign-ins in Part A of `docs/S0-checklist-windows.md`.

## Backup

Done 2026-11-04: `%USERPROFILE%\claude-profile-backup-2026-11-04`, 33.4 MB, permissions for this user only,
`sessions\` and the login file `.credentials.json` excluded, restore steps in `RESTORE.txt`. Taken with 2
sessions live (this one and a desktop app one), so files may be from slightly different moments. Credential
Manager names with `claude` or `anthropic` at that time: none.

## Status

| # | Status | Result |
| --- | --- | --- |
| C1 | To do | Part A. Before: built-in login file present, no Credential Manager names |
| C2 | To do | Part A |
| C3 | To do | Part A |
| C4 | To do | Part B |
| C5 | **Pass** | Details below |
| C6 | To do | Part B |
| C7 | To do | Part B. The helper itself works: details below |
| C8 | To do | Part B |
| C9 | **Pass so far** | Both live records matched to 0.000 s, including a desktop app one. Elevated session still to see (Part B) |
| C10 | Partly | `cli` and `claude-desktop` seen. `status`: `busy`, `idle`. `claude -p`, VS Code and Cowork still to see |
| C11 | Stage W1 | |
| C12 | Stage W1 | Baseline below |
| C13 | **Done** | Details below |
| C14 | **Done, one pass of searching** | Details below |
| C15 | Waiting | VS Code is not installed on this PC |
| C16 | **Pass** | Details below |
| C17, C18, C19 | Stage W1 | Need the wrapper |
| C20 | Stage W3 | |
| C21 | Stage W1 | Smart App Control is off on this PC |
| C22 | Stage W1 | |

## C5

In logged-out test folder 5, `claude plugin marketplace add google/elephant-goldfish-model` and `claude plugin
install egm@egm` both exited 0. `installPath` is `<folder>\plugins\cache\egm\egm\0.1.0`, inside the test
folder. `claude mcp add` and `mcp add-json --scope user` (C16 run) wrote to the test folder's own `.claude.json`
and nothing to `%USERPROFILE%\.claude.json`.

## C7: the helper

Run on `ping -t` in a hidden console: `s0 ctrlc <pid>` (FreeConsole, AttachConsole, SetConsoleCtrlHandler(NULL,
TRUE), GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0) in a throwaway process) ended it after 1.1 s with one Ctrl+C.
So the mechanism in design flow 9 works. Whether Claude Code ends cleanly on it is the check itself.

## C9 and the `procStart` format

`procStart` is a **JSON string of digits**, such as `"134382630457202312"`, not a number and not the macOS date
string. Read as Windows file time (100 ns ticks since 1601-01-01 UTC), it is 2026-11-04 10:50:45 UTC, and it
equalled `GetProcessTimes` creation time **exactly** for both live records: a `cli` one and a `claude-desktop` one.
Opened with `PROCESS_QUERY_LIMITED_INFORMATION`. `pidDomain` was `win32:desktop-5kn5ifl` on both.

## C12 baseline

Before any install: the only `claude` on `PATH` is `%USERPROFILE%\.local\bin\claude.exe`, entry 11 of the **user**
`PATH`. Nothing in the system `PATH`. No `claude.cmd`, no PowerShell alias, no PowerShell profile file. The effective
execution policy for normal shells is Restricted (all scopes Undefined), so a PowerShell profile line would not
run until the user allows scripts. This matches the note in design flow 10.

## C13: file-format key names

- **Session records:** the macOS keys, plus `asksForConvertedSkills`, and `hostSessionId` on the desktop app's
  record: `asksForConvertedSkills`, `cwd`, `entrypoint`, `hostSessionId`, `kind`, `messagingSocketPath`, `name`,
  `nameSince`, `nameSource`, `peerFeatures`, `peerProtocol`, `pid`, `pidDomain`, `procStart`, `sessionId`,
  `startedAt`, `status`, `statusUpdatedAt`, `updatedAt`, `version`. `messagingSocketPath` is a named pipe.
- **Transcripts:** 3 on this PC, all with a `custom-title` or `ai-title` line in the first or last 64 KB. Largest 1.9 MB.
- **`settings.json`** (built-in): `autoUpdatesChannel`, `enabledPlugins`, `extraKnownMarketplaces`, `modelSettings`, `theme`.
- **`.claude.json`**: `oauthAccount`, `mcpServers` (in test folders) and `projects` at the top level, as on macOS.
- **Login file:** `%USERPROFILE%\.claude\.credentials.json`, top-level key `claudeAiOauth`. Key name only; never
  opened again by the scripts.

## C14: existing tools

From two web searches, not from installing anything. Not exhaustive.

| Tool | What the sources say | Result |
| --- | --- | --- |
| [Claude Account Switcher](https://github.com/brenoluizdev/claude-account-switcher), also [akon47.ClaudeAccountSwitcher](https://wingetgui.com/apps/akon47-ClaudeAccountSwitcher) on winget | Windows tray app with a flyout panel. Named accounts, each a `CLAUDE_CONFIG_DIR` folder; opens a terminal with it set; can run accounts in parallel. Young project | Borrow ideas for the tray flyout. Not adopted: no session list, resume, stop, copy setup or default for plain `claude` seen |
| [claude-switchboard](https://pypi.org/project/claude-switchboard/) | CLI menu, PowerShell installer, a config folder per profile | Reject: no app or session list |
| [claude-use](https://github.com/ExaDev/claude-use) | Profile launcher, Scoop package, per-identity login files on Windows | Reject: launcher only. Supports the login-file finding |
| [claude-code-profiles](https://quinnjr.github.io/claude-code-profiles/) | PowerShell and cmd scripts: list, default, which | Reject: no session list |
| [Nemo-Illusionist switcher](https://github.com/Nemo-Illusionist/claude-code-account-switcher/wiki) | Rust CLI, binds accounts to folders | Reject, as on macOS |

No tool found combines profiles, one session list across them, and an app on Windows, so the plan stands.

## C16

`s0 args` sent 17 values through Claude Code's own argument parsing, with the quoting rule the wrapper will use
(`CpmS0.Quote` in `windows/scripts/s0-checks.ps1`). It sent them twice: as separate arguments to `claude mcp add
… -- cmd <values>`, and inside one JSON argument to `claude mcp add-json`. It then read them back from the
test folder's `.claude.json`. **All 34 came back unchanged.** The values included: spaces, an embedded `"`, a
trailing `\`, `\"`, a UNC path ending in `\`, an empty string, a lone space, `%PATH%` and `!BANG!`, `^ & | < >`,
a tab, mixed accented, CJK and emoji text, `;`, `'`, and leading `-` and `--flag=value`. No account usage.

## Fallbacks taken

None yet.

## Teardown

Not yet. Test folders 4, 5 and 16 hold test data: an MCP entry was added and then removed, and the `egm` plugin
is installed. `s0 teardown` and deleting `%USERPROFILE%\.cpm-s0` clear them.
