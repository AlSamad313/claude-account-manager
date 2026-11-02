# S0 results

Claude Code 2.1.296 (CLI). A desktop app session reported 2.1.309. macOS, Apple Silicon, Command
Line Tools, no Xcode. No account emails are recorded here.

**Decision to continue: yes, 2026-11-02.** Checks 1, 2 and 3, the only ones that can stop the project, pass. Check 12
removed the login-wide setting from the design. Still to run: checks 10 and 14 in S1, checks 9 and 21 at the start of
S3. Still partial: check 16 (no Cowork record seen), and checks 18, 19 and 22 have limits noted below.

## Backup

Done 2026-11-02: `~/claude-profile-backup-2026-11-02`, mode 0700, 800 MB, `sessions/` excluded,
restore steps in `RESTORE.txt`. Taken with 3 sessions live, so files may be from slightly
different moments. Keychain names at that time: `Claude Code-credentials` only.

## Status

| # | Status | Result |
| --- | --- | --- |
| 1 | **Pass** | Folder 1's login added one item, `Claude Code-credentials-55479d16`, and left the plain item alone. The suffix equals the first 8 hex characters of SHA-256 of the folder's real path. Folder 2: see the Check 1 note |
| 2 | **Pass** | Built-in and folder 1 (one account) and folder 2 (a second account) all reported logged in, with their own accounts, after a reboot on 2026-11-02. Only `claude auth status` was read after the reboot; no request was sent |
| 3 | **Pass** | Built-in reports the same account, with the plain Keychain item, after both other logins and a reboot |
| 4 | **Pass** | Folder 1's `.claude.json` holds `oauthAccount` |
| 5 | **Pass** | Details below. In `-p` mode |
| 6 | **Pass** | `claude auth login` works in a custom folder. `claude auth logout` there removed only that folder's Keychain item and its `oauthAccount`; the others stayed logged in. So removal may be able to rely on logout alone |
| 7 | **Pass** | Each login made one new item. A logout removes it and the next login recreates the same name |
| 8 | **Pass** | Details below. Logged-out test folder |
| 9 | S3 | |
| 10 | S1 | |
| 11 | **Done, one pass of searching** | Details below |
| 12 | **Done: login-wide setting removed** | Benefit passed, safety failed. Details below. Replaced by a per-app VS Code launch at the author's request |
| 13 | **Done for `auth status`** | Details below. Effect on real requests not tested |
| 14 | **Pass, with one exception** | Details below. Tested on copies of the author's shell files under a temporary HOME, not yet in a real install |
| 15 | **Pass** | Details below |
| 16 | Partly | Details below. Cowork value still unknown |
| 17 | **Pass** | A harmless variable set with `launchctl setenv` reached a new Terminal.app instance started afterwards. Variable removed |
| 18 | **Pass for signals** | With status `busy`, a session in a pty ended 1.1 s after SIGINT. An idle one took 0.8 s. SIGTERM was not needed. Whether the Terminal window stays open is untested |
| 19 | **Pass at the signal level** | Details below. Not yet seen in a real Terminal.app window |
| 20 | **Pass** | In folder 1, `claude auth status --json` shows the account and its `configDirectory`. It could replace reading `.claude.json`, but the plan does not depend on that |
| 21 | S3 | |
| 22 | Partly | Details below. No harm seen in `-p` mode. Two interactive sessions untested |
| 23 | **Done** | Details below |
| 25 | **Pass** | With VS Code closed, `open -a "Visual Studio Code" --env ...` set both values on its process. With it running, `open` left the values alone and printed that the app "was already running and so the additional environment variables could not be set" |
| 24 | **Pass** | `scripts/test.sh` ran a Swift Testing test in a scratch package. Plain `swift test` fails with "no such module 'Testing'", and `import XCTest` also fails |

## Check 8

Run with `CLAUDE_CONFIG_DIR` set to a logged-out test folder:

- `claude mcp add-json <name> <json> --scope user`: the entry landed in that folder's top-level
  `mcpServers`, and not in `~/.claude.json`.
- `claude plugin marketplace add google/elephant-goldfish-model`, then `claude plugin install
  egm@egm`: `installPath` is inside the test folder.

## Check 11: existing tools

From web searches and one README, not from installing anything. Not exhaustive.

| Tool | What the sources say | Result |
| --- | --- | --- |
| Claude Switcher (Symbioose), MIT | Menu bar. Swaps one active login in place; never mentions `CLAUDE_CONFIG_DIR`; no session list | Reject: not simultaneous logins |
| claude-switchboard | Isolated profile folders, launcher, shares MCP and `CLAUDE.md`. Page could not be read in full | Worth a look as a stopgap, and for ideas on sharing setup |
| textaccounts | Profiles via `CLAUDE_CONFIG_DIR`. Says Keychain entries are per profile from Claude Code 2.1.56 | Borrow: supports check 1 |
| claude-use, Nemo-Illusionist switcher | Profile launcher; Rust CLI binding accounts to directories | Reject: no session list or app |
| Claude Usage Tracker, Toki | Menu-bar usage trackers with some account switching | Reject: usage focus |
| claude-session-manager, persistent-claude-code | Linux GTK apps that resume sessions | Reject: not macOS. Borrow transcript ideas if needed |

No tool found combines simultaneous profiles, a session list across them, and a macOS app, so the
plan stands.

## Check 1 note

Each login added one Keychain item named with the first 8 hex characters of SHA-256 of the folder's real path:
folder 1 `55479d16`, folder 2 `0ad19a50`. Folder 2 was logged out and back in. Logout removed its item, and the login created the same name again, so a
baseline taken after logout still sees it as new. 

**Two accounts side by side:** folder 2 is logged into a different account and organization from the built-in
profile and folder 1. All three report logged in at once. Each folder answered a one-word request at the same time.

## Check 5

In test folder 1, `claude -p --resume <id>` continued the conversation with the same session ID from the
original folder and from `~`.

## Check 19

In a pty, with a prompt sent: by default Claude Code sent title sequences (`✳ Claude Code`). With
`CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1` it sent none, while the session went busy and replied. So the setting
stops Claude Code overwriting the wrapper's title.

## Check 22

Two `claude -p --resume <id>` runs at once both succeeded with the same ID, and one transcript file remained.
Neither was refused. The app showing Stop rather than Resume for a live session stands. No wrapper check added.

## Check 12

With `launchctl setenv CLAUDE_CONFIG_DIR` pointing at folder 2 (the second account), on 2026-11-02:

- **VS Code:** its Claude Code (bundled 2.1.161) ran in folder 2 on that folder's own login, so it followed the
  setting. An extension's own binary never reaches the wrapper, so only this setting can move it.
- **Cowork:** stayed on the personal account, still logged in.
- **Desktop app Code tab:** showed the personal account and all earlier sessions, but its session (2.1.309) **ran in
  folder 2**. The desktop app passes its own `CLAUDE_CODE_OAUTH_TOKEN`, which explains the account staying personal. Its
  transcript was written to folder 2, and it read that folder's settings rather than `~/.claude`.

So the login is not harmed, but desktop sessions' local files split into whichever profile is the default, whenever
that is an added profile. With the built-in profile as default the variable is unset, so nothing changes. Whether such
a session can be resumed after the default changes was not tested.

## Check 13

Dummy values on the built-in profile, reading `claude auth status --json`:

| Set | `authMethod` | Account shown |
| --- | --- | --- |
| Nothing | `claude.ai` | Yes |
| `ANTHROPIC_API_KEY` | `api_key` (`apiKeySource` names the variable) | No |
| `ANTHROPIC_AUTH_TOKEN` | `oauth_token` | No |
| `CLAUDE_CODE_OAUTH_TOKEN` | `oauth_token` | No |

So each one replaces the profile's login as far as status reports, and the warning can say so plainly.

## Check 14

With the two lines added to copies of the real `~/.zshenv`, `~/.zprofile` and `~/.zshrc`: a login shell, an interactive
shell, `zsh -c`, `bash -c` and `sh` scripts started from a login shell all reach the wrapper. **`bash -l` does not:** it
reruns `path_helper` and finds `/opt/homebrew/bin/claude`, so it uses the built-in profile. The real binary is found
correctly from each.

`~/.zshenv` does not end with a newline, so a plain `>>` would join the new line to the last `export PATH` line and break
both. `cpm install-shell` therefore prints commands that add a newline first.

## Check 15

`procStart` is a date string in **UTC**, such as `Sat Oct 31 10:58:34 2026`. Read as UTC, it matched the
`sysctl` start time to the second on all 3 live records, including one from the desktop app. In Swift
the field is `kinfo.kp_proc.p_un.__p_starttime.tv_sec`. It imports without trouble. `ps -o lstart`
prints local time, so it must not be compared directly.

## Check 16

`entrypoint`: `cli`, `claude-desktop`, `claude-vscode` (VS Code, sessions show `status` as absent), `sdk-cli` in transcripts for `claude -p`. `status`: `busy`, `idle`. `kind`: `interactive`. No Cowork record
was seen. Some records also have `bridgeSessionId` and `hostSessionId`.

## Check 23: file-format key names

- **Session records**, always present: `cwd`, `entrypoint`, `kind`, `messagingSocketPath`, `name`,
  `nameSince`, `nameSource`, `peerFeatures`, `peerProtocol`, `pid`, `pidDomain`, `procStart`, `sessionId`,
  `startedAt`, `status`, `statusUpdatedAt`, `updatedAt`, `version`.
- **Transcripts**: JSON lines with a `type`. Titles: `custom-title` lines with `customTitle` and
  `sessionId`; `ai-title` lines with `aiTitle` and `sessionId`. `user` lines carry `cwd`, `entrypoint`,
  `sessionId`, `timestamp`, `version`. Title lines repeat through a file.
- **Title position:** of 43 local transcripts, 42 have a title in the first or last 64 KB, and 1 has no
  title. None has titles only deeper. The largest is 72.9 MB, so whole-file reads are not an option.
- **`settings.json`** (built-in): `model`, `enabledPlugins`, `extraKnownMarketplaces`, `tui`,
  `agentPushNotifEnabled`, `useAutoModeDuringPlan`.
- **`.claude.json`**: `oauthAccount` and `mcpServers` are objects at the top level.

## Fallbacks taken

Check 12: login-wide setting dropped, replaced by a per-app VS Code launch (design flow 12).

## Teardown

Done 2026-11-02: test folders logged out, Keychain back to the plain `Claude Code-credentials` item only, built-in
still logged in, `~/.cpm-s0` removed. `~/claude-profile-backup-2026-11-02` is kept until S1 works.
