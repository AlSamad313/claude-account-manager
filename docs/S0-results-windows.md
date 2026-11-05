# S0 results (Windows)

Claude Code 2.1.318 (native `claude.exe`), Windows 11 Pro 10.0.26200, x64, Windows PowerShell 5.1, Windows
Terminal. No account emails are recorded here. Check numbers are those in the Windows design's "Windows S0
checks" table.

**Decision to continue: yes, 2026-11-05.** C1 and C16, the two checks that can stop the project, pass. Two different accounts side by side (part of C1) still needs a second account; the login being one file per folder makes it very likely to pass, so stage W1 starts without it.

## Backup

Done 2026-11-04: `%USERPROFILE%\claude-profile-backup-2026-11-04`, 33.4 MB, permissions for this user only,
`sessions\` and the login file `.credentials.json` excluded, restore steps in `RESTORE.txt`. Taken with 2
sessions live (this one and a desktop app one), so files may be from slightly different moments. Credential
Manager names with `claude` or `anthropic` at that time: none.

## Status

| # | Status | Result |
| --- | --- | --- |
| C1 | **Pass, one account** | Built-in and folders 1 and 2 all signed in before and after a restart on 2026-11-05, each with its own login file, and no Credential Manager names. All three use the same account, so two accounts side by side is still to see. Details below |
| C2 | **Pass** | Each folder reports its own `configDirectory`; `.claude.json` holds the account |
| C3 | **Pass** | Signing out folder 2 removed only its `.credentials.json` and `oauthAccount`. Built-in and folder 1 stayed signed in |
| C4 | **Pass** | In `-p` mode: same session ID and conversation from the original folder and from `%USERPROFILE%` |
| C5 | **Pass** | Details below |
| C6 | **Pass** | Same as macOS. Details below |
| C7 | **Pass** | One Ctrl+C ended every session within 2.6 s, exit code 0, in Windows Terminal and in a classic console, idle and busy. A busy session's running command outlives it. Details below |
| C8 | **Pass** | Title held in all 4 runs, including with a tool running. Control without the variable: Claude Code renamed the tab |
| C9 | **Pass so far** | Both live records matched to 0.000 s, including a desktop app one, and every test session since. An elevated session needs a UAC click and was not run; the design handles access denied either way |
| C10 | Partly | `cli`, `claude-desktop`, and `sdk-cli` for `claude -p`. `status`: `busy`, `idle`. VS Code and Cowork still to see |
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

## C1

Folders 1 and 2 were signed in with `claude auth login` under `CLAUDE_CONFIG_DIR`. Windows was restarted (boot at 13:20 on
2026-11-05). Then `s0 status` showed the built-in profile, folder 1 and folder 2 all `loggedIn=True`, each with
`configDirectory` its own folder and its own `.credentials.json`. Folder 3, never signed in, stayed signed out. Credential
Manager has no `claude` or `anthropic` names, so on Windows the login is the file in each folder and nothing else.
All three used the same account; the built-in one was not changed by the other sign-ins.

## C5

In logged-out test folder 5, `claude plugin marketplace add google/elephant-goldfish-model` and `claude plugin
install egm@egm` both exited 0. `installPath` is `<folder>\plugins\cache\egm\egm\0.1.0`, inside the test
folder. `claude mcp add` and `mcp add-json --scope user` (C16 run) wrote to the test folder's own `.claude.json`
and nothing to `%USERPROFILE%\.claude.json`.

## C6

In folder 1, each variable set to a dummy value, then `claude auth status --json`:

| Set | `authMethod` | Account shown |
| --- | --- | --- |
| Nothing | `claude.ai` | Yes |
| `ANTHROPIC_API_KEY` | `api_key` (`apiKeySource` names the variable) | No |
| `ANTHROPIC_AUTH_TOKEN` | `oauth_token` | No |
| `CLAUDE_CODE_OAUTH_TOKEN` | `oauth_token` | No |

The same as macOS check 13, so the wrapper's warning wording carries over.

## C7: stopping a session

The helper first ended `ping -t` in a hidden console after 1.1 s. Then it was run on real interactive sessions, on
2026-11-05. Each session was started in a new window running Windows PowerShell with `-NoExit`, in folder 1, with an
opening prompt. Idle: "Say hi in one word", stopped once it went `idle`. Busy: "Run the shell command sleep 90",
allowed with `--allowedTools 'Bash(sleep:*)'`, stopped while `sleep` ran. `s0 ctrlc <pid>` then sent one console Ctrl+C.

| Terminal | Status | Ended after | Ctrl+C needed | Exit code | Window and shell |
| --- | --- | --- | --- | --- | --- |
| Windows Terminal | idle | 1.3 s | 1 | 0 | Open, back at the prompt |
| Windows Terminal | busy | 2.3 s | 1 | 0 | Open, back at the prompt |
| Classic console (`conhost.exe`) | idle | 1.0 s | 1 | 0 | Open, back at the prompt |
| Classic console | busy | 2.4 s | 1 | 0 | Open, back at the prompt |

Claude Code printed its usual "Resume this session with: claude --resume <id>" each time. The second Ctrl+C in
design flow 9 was never needed.

**A busy session's running command outlives it.** In both busy runs, Git Bash's `sleep.exe` kept running after
Claude Code exited; it was stopped by hand. Design flow 9 now notes this. Whether macOS behaves the same was not
checked there.

New windows opened with no terminal named were handed to Windows Terminal (`OpenConsole.exe` started by the
default-terminal handoff). `conhost.exe <command>` gave a classic console.

## Inherited session variables

Found while starting C7. A process started from inside a Claude Code session inherits its markers:
`CLAUDE_CODE_CHILD_SESSION`, `CLAUDE_CODE_SESSION_ID`, `CLAUDE_CODE_ENTRYPOINT`, `CLAUDE_CODE_MESSAGING_SOCKET`,
`CLAUDE_CODE_MESSAGING_TOKEN`, `CLAUDE_CODE_SESSION_ATTENDED`, `CLAUDE_PID`, `CLAUDECODE` and `AI_AGENT`. An
interactive `claude` started in a new window with these set showed "Transcript saving is off — inherited
CLAUDE_CODE_CHILD_SESSION marker" and wrote no session record. On macOS, `open -a Terminal` starts a fresh
environment, so this never arose there. On Windows a new process copies its parent's, so the design's Launcher
now gives new windows a fresh user environment (design flow 2). The checks above removed the markers first.

## Test setup in folder 1

For C7 and C8, folder 1's own `.claude.json` was given `hasCompletedOnboarding` and trust for `.cpm-s0\7`, and its
`settings.json` a theme, so first-run screens did not block a session started with a prompt. The original is kept
as `.claude.json.before-c7`. Only the test folder was changed.

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

## Blank `CLAUDE_CONFIG_DIR`

Found while starting C1. With `CLAUDE_CONFIG_DIR` set to an empty string, `claude auth status` reports
`loggedIn=false` and a blank `configDirectory`. It does not fall back to `%USERPROFILE%\.claude`. The wrapper must
remove the variable for the built-in profile, never set it to `""`. `s0 status` had this bug for its built-in line
(PowerShell passes `$null` to a `[string]` parameter as `""`); `CpmS0.Run` now treats empty as unset. Other checks
always pass a folder, so their results are unaffected.

## Fallbacks taken

None yet.

## Teardown

Not yet. Test folders 4, 5 and 16 hold test data: an MCP entry was added and then removed, and the `egm` plugin
is installed. `s0 teardown` and deleting `%USERPROFILE%\.cpm-s0` clear them.
