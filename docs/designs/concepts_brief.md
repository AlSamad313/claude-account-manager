# Concept Brief: Claude Profile Manager (macOS)

Author: Abdul Samad Shahid
Date: 2026-11-01
Source concept: concept_input.md (superseded by this brief)
Mode: BRAINSTORM (multi-lens review, round 2)

## Reviewer Status (round 2)

| Reviewer | Role title | Status |
| --- | --- | --- |
| egm-design | Skeptical Architect Brainstormer | completed |
| egm-codebase-details | Technical Accuracy & Reuse Brainstormer | completed |
| egm-zoom-out | Simplicity & Scope Brainstormer | completed |
| egm-comprehension | Naive Reader & UX Ergonomics Brainstormer | completed |
| egm-implementation-readiness | Implementation Readiness Brainstormer | completed |
| egm-structural | Structure & Decomposition Brainstormer | completed |

All six reviewers completed on the first attempt. None is marked not run. Round 1 (2026-10-31) also had all six completed.

Limits on the evidence:
- The reviewers could not open URLs or run commands.
- Only egm-codebase-details read local files. It read key names and structure only, and opened no credential, Keychain, `.key` or token values. Its findings are labelled "local observation" or "from memory".
- The main session verified two facts before this round: the key list of `~/.claude/sessions/<pid>.json`, and the key list of the `oauthAccount` object in `~/.claude.json`.
- CLI subcommand names (`claude plugin install`, `claude mcp add-json`, a logout command) come from memory and are unverified.

---

## Overview

One person uses several Claude accounts on one Mac. Today they switch by logging out and back in, and they lose track of which Claude Code session belongs to which account.

The concept is a small native macOS app that works like browser profiles. Each profile is one Claude account with its own Claude Code config folder, selected through `CLAUDE_CONFIG_DIR`. The user can:
- add any number of profiles, each logged in once and kept logged in;
- mark one profile as the default;
- start a Terminal.app session on any profile at any time, and resume past sessions on the profile that owns them;
- see past and running CLI sessions for all profiles in one list;
- copy settings, MCP servers, skills, plugins and hooks from one profile to another.

`~/.claude`, and the account logged into it today, becomes the built-in first profile and is left untouched. The app starts as a menu-bar dropdown and later grows a normal window. A shell command (`claude-p <name>`) and plain `claude` use the same profile list and default as the app. Claude writes most of the code, and the author must be able to read and build it.

Round 2 reviewers agree on five points:
1. The profile model is sound, but the brief still carried two-account text that contradicted it. This revision removes that text.
2. The built-in profile must keep `CLAUDE_CONFIG_DIR` unset. Setting it to `~/.claude`, even once, points Claude Code at a different, near-empty config file.
3. Copy-setup is the only feature that writes into Claude Code's own files, and it is the riskiest. Plugins and MCP servers should be copied as intent and installed with Claude Code's own commands, not copied file by file.
4. The app should never work out Keychain item names itself. It should record the name when a profile first logs in.
5. Terminal should be opened with generated `.command` files rather than AppleScript, and the app should be signed with a stable identity so macOS permission grants survive rebuilds.

---

## Analysis

Findings are deduplicated across lenses. Severity uses Blocker, Major and Nit. The lenses that raised each finding are in brackets.

### Round 1 findings: status

| ID | Finding (short) | Status after round 2 |
| --- | --- | --- |
| B1 | Migrating the existing login is undefined | Resolved by decisions 1, 8 and 11 (`~/.claude` stays, new profiles use fixed-ID folders, nothing is moved). Detail added in R2-B2. |
| B2 | Keychain naming read from one version only | Open. S0 spikes must now cover three or more profiles. |
| B3 | Own "running" records duplicate `sessions/*.json` | Resolved by decision 3. |
| B4 | "Session" and success test undefined | Resolved. Session defined in decision 2; the actions and acceptance criteria are defined in this revision. |
| B5 | A Terminal window does not show its account | Open, and harder with many profiles and copy-setup (R2-M3). |
| M1 | GUI may not be needed | Superseded by decision 7 (GUI required). |
| M2 | Resume must bind to the owning account | Open. Carried into the Launcher design. |
| M3 | Desktop/Cowork transcripts mixed into `projects/` | Open. Filter by `entrypoint`. |
| M4 | Undocumented internals | Open and wider (R2-M10). |
| M5 | Shell wrapper fragile | Replaced by the shell shim design in decision 13. Its rules (`command claude`, `exec`, explicit env) still apply. |
| M6 | PID reuse | Resolved through B3 (`pid` plus `procStart`). |
| M7 | No debugging plan | Open, expanded in R2-M11. |
| M8 | Desktop app, Cowork, Remote Control unverified | Resolved as a non-goal for v1. |
| M9 | Terminal launch and stop | Partly resolved. Launch method proposed in decision 15; "stop" still open. |
| M10 | `.claude.json` location and settings drift | Partly resolved by decision 10. Mechanism covered by R2-B2, R2-B4 and R2-B5. |
| M11 | Reuse Claude Code's own titles | Open; adopted as a goal. |
| N1 | Cost and resource load | Still valid. |
| N2 | App-owned default file may be unnecessary | Superseded by the plain-text default file in decision 13. |
| N3 | Naming of accounts | Resolved by fixed profile IDs. |
| N4 | Security of settings file | Carried into the Security section. |
| N5 | Usage display | Deferred. |

### Round 2 Blockers

**R2-B1. The brief contradicted its own decisions.** [structural, implementation-readiness, comprehension, zoom-out, design]
The title, Overview, Goals ("two independent logins"), Non-Goals ("a full windowed GUI app"), Alternatives, Staging (`claude-work`/`claude-personal`, GUI last) and acceptance criteria still described a two-account tool without a GUI. Decision 7 sat after 11, and decision 4 asked a question decision 9 had already answered. An implementer could not tell which text wins.
Status: addressed in this revision. Every section is rewritten around profiles, and the decisions run 1 to 18 in order.

**R2-B2. The built-in profile must never set `CLAUDE_CONFIG_DIR`, and its label must come from `$HOME/.claude.json`.** [codebase-details; supported by structural, implementation-readiness, design]
Local observation: there are two config files. `~/.claude.json` is large and holds `oauthAccount`, `mcpServers` and per-project state. `~/.claude/.claude.json` is small and has no `oauthAccount` and no `mcpServers`. It was probably created by a process (likely the desktop app) that ran with `CLAUDE_CONFIG_DIR=~/.claude`. If the app or `claude-p` sets the variable to `~/.claude` for the built-in profile, Claude Code reads the small file: no login, no MCP servers, no project trust. The Keychain name probably also gains a hash suffix, which forces a new login.
Recommendation: model the built-in profile in code as `configDir = nil` (variable unset, config at `$HOME/.claude.json`). Every other profile has variable = exact folder path, config at `<dir>/.claude.json`. Resolve the config file location through one function. `doctor` flags the stray `~/.claude/.claude.json`.

**R2-B3. Deleting a profile cannot safely compute its Keychain item name.** [design, implementation-readiness, zoom-out, codebase-details]
The name `Claude Code-credentials-<hash>` depends on an undocumented hash whose input is unknown. A wrong guess could delete another profile's login, or the built-in one.
Recommendation:
- At first login, list generic-password service names with the `Claude Code-credentials` prefix before and after (attributes only, never data, via `SecItemCopyMatching` with `kSecReturnAttributes`). Store the new name in the profile record as `keychainService`.
- Treat a profile as logged in when its `.claude.json` has `oauthAccount`, not by checking the Keychain.
- To delete: refuse while the profile has live sessions; run Claude Code's own logout with that profile's folder (spike which command exists); then delete the stored item only if it exists and its name matches exactly. Never touch the built-in profile.
- MCP OAuth tokens may be stored in the same item or in separate items (from memory). Spike this. Report unknown leftovers as manual cleanup rather than guessing.
- `doctor` lists prefixed items that match no profile.

**R2-B4. Copying plugins file by file breaks the target profile.** [codebase-details; supported by design, zoom-out, implementation-readiness, structural]
Local observation: `plugins/installed_plugins.json` (with `"version": 2`) holds absolute `installPath` values into `~/.claude/plugins/cache/...`, and each `known_marketplaces.json` entry has an absolute `installLocation`. `cache/` and `marketplaces/` are git checkouts. A copied registry points into the source profile's folder, so an update or delete in one profile corrupts the other.
Recommendation: copy only the intent, the `enabledPlugins` and `extraKnownMarketplaces` entries in `settings.json`. Then run Claude Code's own marketplace and plugin install commands with `CLAUDE_CONFIG_DIR` set to the target (names from memory; verify). Never copy `cache/`, `marketplaces/` or `plugins/synced/`.

**R2-B5. Writing into `.claude.json` can collide with a running Claude Code.** [design, codebase-details, implementation-readiness, structural]
Local observation: `~/.claude/backups/` holds `.claude.json.backup.<epoch-ms>` files minutes apart, so Claude Code rewrites the file often. The file holds `oauthAccount`, per-account caches and per-project state next to `mcpServers`. Copying the whole file would carry over the source account's identity. Merging while a target session runs loses one of the writes.
Recommendation:
- Prefer Claude Code's own command for MCP servers (from memory: `claude mcp add-json <name> '<json>' --scope user`, run with the target's folder).
- If writing directly: change only allow-listed keys, write a temp file and rename it over the original, back up first, and refuse while `sessions/*.json` shows a live session on the target.
- The allow-list is the top-level `mcpServers`, and on request per-project `mcpServers` and `enabledMcpjsonServers`/`disabledMcpjsonServers`. Never copy `hasTrustDialogAccepted` or `allowedTools` unless the user asks, because they are security grants.
- Copying into the built-in profile writes `~/.claude.json`, the one path that touches the existing login. A backup is mandatory there.

**R2-B6. Nothing defined where the profile list and default live, or how the shell shares them.** [design, implementation-readiness, zoom-out, structural, codebase-details]
The app, plain `claude` and `claude-p <name>` must agree on the profile list and the default. zsh cannot parse JSON without `jq`, and the shell must work while the app is not running.
Recommendation: see decision 13. The app owns a JSON registry and also writes two plain-text files atomically. The shell reads them on every call. No daemon, no IPC, no `.zshrc` regeneration.

### Round 2 Major

**R2-M1. Profile lifecycle states and the login flow are undefined, and login has a trap.** [design, implementation-readiness, comprehension, structural]
OAuth login opens the browser. The browser is often already signed into claude.ai with another account, so a new profile can silently log into an account another profile already uses.
Recommendation: states are `pendingLogin`, `ready`, `loggedOut`, `deleting`, `error`. Flow:
1. "Add profile…" asks for a display name, a short command name and a colour.
2. The app creates `~/.claude-profiles/<id>/`.
3. It opens Terminal running `claude` under that folder, with the note "Make sure the browser is signed into the account you want, or use a private window."
4. It watches the folder (FSEvents on the folder, since the file is likely replaced atomically) until `oauthAccount.accountUuid` appears, within 5 seconds of login finishing.
5. It shows the detected email and organization with Keep / Sign out and retry. A duplicate `accountUuid` shows the warning from decision 8 here.
6. A closed window or timeout leaves the profile `pendingLogin` with Retry and Discard. The menu shows "Not signed in — Sign in".
On app start, reconcile folders with no registry row and rows with no folder.

**R2-M2. Renaming breaks `claude-p <name>` if the command uses the display name.** [design, implementation-readiness, comprehension]
Recommendation: each profile has a fixed ID (folder name), an editable display name, and a short command name. The command name changes only on purpose, with a warning. Rename changes only the display name and says "folder and sign-in unchanged". Tab completion lists command names. An unknown name prints the list of valid profiles.

**R2-M3. The in-window indicator can be overwritten by copy-setup.** [comprehension, implementation-readiness; supports round-1 B5]
B5 put the profile label in each folder's `settings.json` `statusLine`. Copying settings would then show the source profile's name in the target, which defeats the purpose.
Recommendation: never copy `statusLine`. Generate it when a profile is created, from the profile's own identity or `CLAUDE_CONFIG_DIR`, not a hard-coded name. Windows opened by the app also set the title by escape sequence. A window with no running `claude` shows nothing; this is accepted.

**R2-M4. Copy-setup conflict rules and preview wording are not set.** [comprehension, design, implementation-readiness, zoom-out, structural]
Recommendation:
- Per item: Add / Change (with diff) / Same, each with a checkbox. On conflict the target's value wins unless the user ticks the item.
- `settings.json`: key-by-key merge, target wins by default; union and dedupe `permissions.allow`/`deny`; merge `hooks` by event plus matcher; merge `enabledPlugins` as a set; never copy `statusLine`.
- Skills, agents, commands, `CLAUDE.md`: per-item file copy with skip / overwrite / rename.
- Hooks or MCP `command` values pointing into the source folder: offer copy file / keep path / skip.
- The dry-run preview is produced by the same merge function that writes.
- Back up target files to `<target>/.cam-backups/<timestamp>/` and offer one-step Restore.
- Proposed wording: "Contains a secret (API key). Unticked by default. Copying puts the key in plain text in <target>." / "Uses OAuth sign-in. You will need to sign in again in <target>." / "Not copyable: claude.ai connectors belong to the account. Connect them at claude.ai while signed in as <email>."

**R2-M5. Secrets hide in more places than MCP `env`.** [design, codebase-details, zoom-out]
Secrets can sit in MCP `env`, `headers` and `args`, and in `apiKeyHelper`. Local observation: both user-scope MCP servers have `env` blocks. Recommendation: show env and header keys by name with masked values, leave values out by default, and let the user opt in per item.

**R2-M6. Account-synced plugins are account-owned, like connectors.** [codebase-details]
Local observation: `plugins/synced/<uuid>_<uuid>/` holds plugins synced from claude.ai (manifest, marketplaces, per-plugin `.mcp.json` and skills). The folder name looks like org UUID plus account UUID. Decision 10 now lists these as not copyable; they arrive through each account's own sync.

**R2-M7. `sessions/` holds secret `.key` files.** [codebase-details]
Local observation: next to each `<pid>.json` there is a `<pid>.<sha256>.key` file. The reader must glob `sessions/*.json` only. Copy and backup never include `sessions/`. `doctor` never prints `messagingSocketPath` contents.

**R2-M8. Opening Terminal.app and signing.** [design, implementation-readiness, zoom-out, structural, codebase-details]
- AppleScript `do script` needs the Automation (TCC) permission. That grant is tied to the app's code signature, and an ad-hoc signature changes on every rebuild, so the prompt would return after each build.
- Recommendation: write a `.command` file (mode 0700, single-quoted values) that sets the title, `cd`s to the folder, sets or unsets `CLAUDE_CONFIG_DIR`, unsets `ANTHROPIC_API_KEY`, and `exec`s the full path of `claude` with optional `--resume <id>`. Open it with `NSWorkspace.open(_:withApplicationAt:)`. This needs no Apple Events and no `NSAppleEventsUsageDescription`. Delete old files.
- A GUI app does not inherit the shell PATH, so Homebrew's `claude` is not on it. Resolve the binary once with `/bin/zsh -lc 'command -v claude'`, cache it, and show it in `doctor`.
- Per-profile Terminal colour settings need AppleScript. Defer them.
- Local observation: `~/.claude.json` has Terminal setup keys (`appleTerminalSetupInProgress`, `appleTerminalBackupPath`). Each new profile will probably offer Claude Code's Terminal setup again.
- Signing: notarization is not needed, because a locally built app has no quarantine flag. Use a stable identity (a free Apple Development certificate or a self-signed one) so permission and Keychain grants survive rebuilds. No App Sandbox, since the app reads `~/.claude*` and the process table. No hardened runtime needed.
- `swift build` does not produce a `.app` bundle. A menu-bar-only app needs an Info.plist with `LSUIElement`. Use a small Xcode project or a documented `make-app.sh` that assembles and signs the bundle.

**R2-M9. The menu-bar contents and path to the window are not specified.** [comprehension, design, zoom-out, structural]
Recommendation:
- Use `MenuBarExtra` with the window style (a panel), not a plain menu, so it can hold a filter field.
- The menu-bar icon carries the default profile's colour.
- One row per profile: colour dot, display name, email, running-session count, a marker on the default, and "New Terminal".
- Below: recent sessions, live first, 5 to 8 total, each tagged with its profile, with Resume.
- Then "Open window…", "Add profile…", "Set default ▸", "Doctor".
- Ship "Open window…" in v1. Put views and a shared view-model layer in a library so `MenuBarExtra` and `WindowGroup` render the same views.
- Load data when the panel opens; check liveness only while it is visible.

**R2-M10. Reliance on undocumented formats now covers more files.** [all six]
Formats depended on: Keychain naming, `sessions/*.json`, `projects/` JSONL, `.claude.json` keys, `installed_plugins.json` (`version: 2`), `.marketplaces.json` (`parserVersion`), and the shape of `hooks` in settings. The desktop app also bundles its own newer CLI.
Recommendation: one read-only module per format, tests against scrubbed saved files per tested version, a `testedVersions` constant compared with `claude --version` and with the `version` field in session records, a visible banner on an untested version, and refusal to write when a file's shape or version field is unexpected. A README section "When Claude Code updates" describes the steps.

**R2-M11. Observability is not specified for the profile model.** [design, structural, codebase-details, implementation-readiness]
Recommendation:
- A per-profile health line: folder exists; logged in, with email from `oauthAccount` only; recorded Keychain item exists; Claude Code version against tested; default resolves.
- `doctor` in the CLI and the app, both calling the same check: resolved paths, binary path, stray `CLAUDE_CONFIG_DIR`/`ANTHROPIC_API_KEY`, stray `~/.claude/.claude.json`, plugin registries pointing outside their own folder, orphan Keychain items.
- `os_log` with one subsystem and one category per module.
- Append-only logs: launches (profile, folder, binary, version, PID, start time) and writes (copy, delete, default change, with key names changed and the backup path). Rotate them.

**R2-M12. Module boundaries were undefined.** [structural, zoom-out]
See decision 18.

**R2-M13. "Stop session" is still undefined.** [implementation-readiness, zoom-out]
With `.command` launching, the app cannot control the Terminal window. Stop would mean a signal to the PID from `sessions/*.json`: SIGINT, then SIGTERM after a few seconds, after checking `procStart`. Zoom-out proposes deferring stop, since closing the window works. Kept as an open question.

### Round 2 Nits

- **R2-N1. The separate `claude-sessions` CLI is optional.** Zoom-out: ship the core and the app first; keep a small CLI for `doctor` and list. Others keep it as a thin adapter. [zoom-out, structural]
- **R2-N2. Command name.** `claude-p` is unclear. Pick the final name, support tab completion and names with spaces (or forbid spaces in command names). [comprehension]
- **R2-N3. "Tools (skills, plugins, hooks)" can be confused with MCP tools.** Say "extensions" or list the items. [comprehension]
- **R2-N4. Uninstall.** Remove the shell `source` line and `~/.claude-profiles` after running Keychain cleanup per profile. `~/.claude` and `~/.claude.json` stay untouched. [design, implementation-readiness]
- **R2-N5. Delete moves the folder to Trash** (`NSWorkspace.recycle`), never `rm`. Deleting the default moves the default to the built-in profile. [implementation-readiness, comprehension]
- **R2-N6. Cost.** FSEvents watchers and a few small JSON reads. No background helper process. A login item is optional. [design]

---

## Goals vs. Non-Goals

### Glossary
- **Account:** an Anthropic login, identified by `oauthAccount.accountUuid`.
- **Profile:** a fixed-ID config folder plus a display name, a command name, a colour and at most one account.
- **Built-in profile:** `~/.claude` with `CLAUDE_CONFIG_DIR` unset, config at `$HOME/.claude.json`. It cannot be moved or deleted from the app.
- **Default:** the profile that plain `claude` uses. It is read at every launch, so changing it affects only sessions started afterwards.
- **Session:** a transcript (session ID plus owning profile) with an optional live process from `sessions/*.json`.

### Goals (v1)
- Any number of profiles, each with its own Claude account, all logged in at the same time with no logging out.
- `~/.claude`, `~/.claude.json` and their Keychain entry left untouched as the built-in profile, labelled from its `oauthAccount` email and organization.
- Profile lifecycle in the app: add and log in, rename (display name only), set default, and remove.
- A menu-bar app (`MenuBarExtra`) showing profiles, the default, recent and live sessions, with "New Terminal" per profile and Resume per session. An "Open window…" item that reuses the same views.
- Resume always runs under the profile that owns the transcript, in the session's original folder.
- Live status from Claude Code's own `sessions/*.json`, checked with `pid` plus `procStart`. Claude Code's own titles shown.
- Plain `claude` and `claude-p <name>` use the same profile list and default as the app, and work while the app is not running.
- The profile shown in every app-opened window and in each profile's status line.
- Copy setup between profiles with a preview, a backup and Restore, leaving secrets out by default.
- `doctor`, a health line per profile, and launch and write logs.
- A plain Swift repository the author can read and build, with a README covering build, signing, and what to do when Claude Code updates.

### Non-Goals (v1)
- Listing or managing desktop app or Cowork conversations.
- Two logins in the desktop app, and Remote Control account handling. Each needs its own spike.
- Copying claude.ai connectors or account-synced plugins. They belong to the account.
- Two profiles on the same account (warned about, not supported as a goal).
- Terminals other than Terminal.app, and per-profile Terminal colour settings that need AppleScript.
- Tags, full-text search over transcripts, and usage display (later stages).
- Writing to Claude Code's transcripts or `sessions/` files. Copy-setup is the only feature that writes into Claude Code's files, and only into allow-listed keys and folders.
- Distribution to other users, notarization, and App Store packaging.

---

## Alternatives Weighed

| Option | Covers | Cost | Reviewer view |
| --- | --- | --- | --- |
| A. Shell layer only: profile folders, plain-text default file, `claude-p <name>`, per-profile status line | Removes logging out; history one profile at a time | Small shell script | Kept as the bottom layer of the chosen design (stage S1). Not enough alone, because decision 7 requires a GUI. |
| B. A plus a merged session list and resume in a small CLI | List, resume, live status | Small, read-only | Kept as the core library's behaviour, exposed by an optional thin CLI. |
| C. Existing open-source tools (from memory, unverified): account switchers named like "cc-switch"; session managers claude-squad, ccmanager, history viewers; ccusage | Unknown | Evaluation time only | Still worth a short check for reusable parsing code. None is known to handle many config folders, profile copy or a menu bar. |
| D. Symlink all profiles' `projects/` to one shared folder | One list via `/resume` | Very small | Rejected. Loses the profile label and invites wrong-account resumes. |
| E. Native SwiftUI menu-bar app over A and B, one Swift package (chosen) | All goals | Largest: bundling, signing, Terminal launch, copy-setup | Chosen by decision 7. SwiftUI over Electron or Tauri: no bundled browser engine, low idle memory, macOS-only matches the scope. The GUI holds no logic of its own. |
| F. Launch Terminal with AppleScript `do script` | Full window control | Automation prompt, reset on every ad-hoc rebuild | Rejected for v1 in favour of `.command` files. Kept for later Terminal colour settings. |
| G. Copy-setup by direct file copy of config files | Simple | Breaks plugins, risks lost writes, copies account identity | Rejected. Use allow-listed keys and Claude Code's own install commands (decision 17). |

---

## Key Decisions & Staging for Next Steps

Decisions marked "author decision" are fixed. Decisions marked "proposed" are for the author to confirm.

### Decisions
1. **Profile layout (proposed 2026-10-31, confirmed by decisions 8 and 11).** `~/.claude` stays as the built-in profile with `CLAUDE_CONFIG_DIR` unset. Each new profile gets a new folder at a fixed path. Nothing is moved; files are copied, never moved.
2. **Definition of session (proposed 2026-10-31).** A transcript (session ID plus owning profile) with an optional live process taken from `sessions/*.json`.
3. **Running tracking (proposed 2026-10-31).** Read Claude Code's own `sessions/*.json` (glob `*.json` only) and check `pid` plus `procStart`. The shell layer writes no records of its own.
4. **Default profile (closed 2026-11-01 by decision 9).** There is one visible default. Plain `claude` follows it, read at every launch. The default is shown in the menu-bar icon colour and in the profile list.
5. **Shared configuration (superseded 2026-11-01 by decision 10).** Nothing is shared or symlinked. Setup moves between profiles only by explicit copy. Credentials, `projects/`, `sessions/`, caches and plugin registries are never copied.
6. **Build vs. reuse (proposed 2026-10-31).** Spend a short time on the option C tools for reusable parsing code before writing it.
7. **GUI is required (author decision, 2026-11-01).** The author wants a lightweight desktop app in every case, so option E is v1. One Swift package: a core library, an optional small command-line tool, and a native SwiftUI app. Both the app and the command call the same core. Native SwiftUI over Electron or Tauri, because it adds no bundled browser engine and uses little memory when idle. It is macOS-only, which matches the scope.
8. **Multiple profiles (author decision, 2026-11-01).** The user can create any number of profiles. A profile is one config folder plus a display name, a colour and its own login.
   - Folder paths use a fixed ID (`~/.claude-profiles/<id>`), never the display name. Renaming changes only the display name. Moving the folder would log it out.
   - Deleting a profile also removes its Keychain entry and asks before deleting its history (mechanism: R2-B3).
   - Clarified 2026-11-01: a profile means one Claude account, and the user can add as many accounts as they want. Two profiles on the same account are not a goal. The app warns, not blocks, if a new profile logs into an account another profile already uses, since both share that account's usage limits.
   - `~/.claude` is the built-in first profile and cannot be deleted or moved from the app.
   - Fixed two-account commands are replaced by one command that takes a profile name (`claude-p <name>`), plus optional aliases bound to the profile ID.
9. **Browser-style profile model (author decision, 2026-11-01).** Works like Chrome profiles: add any number of accounts, mark one as default, start any profile at any time, and open a Terminal.app session for a chosen profile from the app. Changing the default affects only new sessions.
10. **Copy setup between profiles (author decision, 2026-11-01).** The user can copy settings, MCP servers, skills, plugins and hooks from any profile to another. Limits:
    - claude.ai connectors (Gmail, Drive, Figma and similar) cannot be copied. They are attached to the account on Anthropic's servers. Each account connects them on claude.ai. The app can at most show which connectors each account has, if readable (unverified).
    - Account-synced plugins in `plugins/synced/` cannot be copied either; they arrive through each account's own sync (added in round 2, R2-M6).
    - Locally configured MCP servers can be copied. Entries that hold API keys or tokens in plain text would copy those secrets; the copy screen shows this and leaves values out by default.
    - MCP servers that log in with OAuth need to log in again in the new profile, because their tokens live in the Keychain under the old profile.
    - The copy merges into the target, not overwrites it, and shows what changes before it writes.
11. **App form, first profile, maintenance (author decisions, 2026-11-01).**
    - The long-term target is a normal window with a profile switcher. v1 starts as a menu-bar dropdown, with views built so the window reuses them. Round 2 proposes shipping "Open window…" in v1 (R2-M9).
    - The account logged into `~/.claude` today becomes the built-in first profile, unchanged, labelled automatically from the email and organization in `$HOME/.claude.json` `oauthAccount`.
    - Claude writes and maintains most of the code. The author must be able to read, build and change it: plain Swift in a git repository, a README describing how to build and run it, no closed or generated-only parts.
12. **Built-in profile handling (accepted by author 2026-11-01, R2-B2).** In code the built-in profile is `configDir = nil`. No code path ever writes `CLAUDE_CONFIG_DIR=~/.claude`. Config file location is resolved by one function.
13. **Registry and default sync (accepted by author 2026-11-01, R2-B6).**
    - `~/.claude-profiles/profiles.json` (mode 0600, written via temp file and rename, with `schemaVersion` and a `.bak` on migration). Per profile: `id`, `commandName`, `displayName`, `color`, `builtin`, `createdAt`, `accountUuid` and `email` (cached from `oauthAccount`), `keychainService` (as recorded), `state`.
    - The app also writes `~/.claude-profiles/default` (one line: a profile ID) and `~/.claude-profiles/names` (`commandName<TAB>id` lines), atomically.
    - The zsh shim reads both on every call, never parses JSON, unsets `CLAUDE_CONFIG_DIR` for the built-in ID, uses `command claude` and `exec`, sets `CLAUDE_CONFIG_DIR` and unsets `ANTHROPIC_API_KEY` explicitly, and falls back to the built-in profile with a warning on stderr if a file is missing or broken.
    - The app watches these files so a default changed from the shell shows in the menu.
14. **Keychain handling (accepted by author 2026-11-01, R2-B3).** Record the Keychain service name at first login by listing attribute names before and after. Never compute the hash. Never read secret data. Delete only the recorded name, after Claude Code's own logout.
15. **Terminal launch (accepted by author 2026-11-01, R2-M8).** Generated `.command` files opened with `NSWorkspace`, no AppleScript in v1. Full binary path resolved from a login shell and cached.
16. **Signing and build (accepted by author 2026-11-01, R2-M8).** Stable signing identity (free Apple Development certificate or self-signed), no notarization, no sandbox, no hardened runtime. Xcode project or documented `make-app.sh` to produce the `.app`.
17. **Copy-setup mechanics (accepted by author 2026-11-01, R2-B4, R2-B5, R2-M4).** Allow-listed items only. MCP servers and plugins are copied as intent and installed with Claude Code's own commands under the target's folder where those commands exist; otherwise key-level atomic writes. Refuse while the target has a live session. Back up and offer Restore. Never copy `statusLine`, `hasTrustDialogAccepted` or `allowedTools` by default. Ship in two steps: skills, agents, commands, `CLAUDE.md` and MCP first; plugins and hooks second.
18. **Module decomposition (accepted by author 2026-11-01, R2-M12).** One Swift package:
    - `ProfileStore`: only writer of the registry and the shell files; lifecycle states; reconcile on start.
    - `SessionIndex`: read-only; sessions per profile with `profileId`, `sessionId`, `cwd`, `title`, `mtime`, `entrypoint`, `live`; cache keyed on path, size and mtime; per-file failure isolation; filters desktop/Cowork by `entrypoint`.
    - `Launcher`: only module that starts processes; writes `.command` files and the launch log.
    - `SetupCopier`: `plan(src, dst, selection) -> CopyPlan` (diff, conflicts, secrets found) and `apply(plan)`.
    - `KeychainProbe`: list prefixed names, `exists`; `delete` called only from profile deletion.
    - `CompatCheck`: version, sample parse, registry shape; returns a report used by `doctor` in both CLI and app.
    - Core imports neither SwiftUI nor AppKit. A shared view-model layer sits above it. The app and CLI are thin adapters. The zsh shim is a separate plain file.

### Security
- Never read Keychain secret data, `.credentials` files, `sessions/*.key` files or token fields. Read `oauthAccount` keys only.
- Registry and shell files mode 0600; `.command` files 0700, deleted after use.
- Copy-setup masks secret values and leaves them out by default. Security grants are not copied by default.
- No App Sandbox; this is stated in the README with the reason.

### Rollback and uninstall
- S1: remove the `source` line from `.zshrc`; the built-in profile works as before.
- Copy-setup: Restore from `<target>/.cam-backups/<timestamp>/`, byte-identical.
- Full uninstall: per-profile Keychain cleanup, remove `~/.claude-profiles`, remove the app. `~/.claude` and `~/.claude.json` are never touched.

### Risks

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| Built-in profile accidentally run with `CLAUDE_CONFIG_DIR` set | Medium | Built-in login, MCP and trust appear lost | Decision 12; shell tests with polluted env; `doctor` check |
| Lost write to `.claude.json` during copy | Medium | Target config or login state damaged | Live-session refusal, atomic key-level writes, backup |
| Claude Code update changes a file format | High over time | Empty list or bad copy | `CompatCheck`, visible banner, refuse writes on unknown shape |
| Wrong Keychain item deleted | Low | Another profile logged out | Recorded name only; never touch built-in |
| New profile logs into an already-used account | Medium | Shared usage limits, confusion | Browser note at login; duplicate `accountUuid` warning |
| Permission prompts return after each rebuild | High if ad-hoc signed | Repeated prompts | Stable signing identity; `.command` launch |

### Open questions for the author
- ~~Which account owns today's `~/.claude`?~~ Resolved (decision 11).
- ~~Is a GUI a hard requirement?~~ Resolved (decision 7).
- ~~Can two profiles share one account?~~ Resolved (decision 8).
- ~~New profile: start empty or copy?~~ Resolved (decision 10).
- ~~Which window form?~~ Resolved (decision 11).
- ~~Visible default or no unpinned default?~~ Resolved (decision 4).
- Is "stop session" needed in v1, or is closing the Terminal window enough? (R2-M13)
- Should resuming one profile's session under another profile be blocked, or only impossible from the app?
- Confirm proposed decisions 12 to 18, including the final command name (R2-N2).
- What happens if Claude.app cannot hold two logins, or if Remote Control ignores `CLAUDE_CONFIG_DIR`?

### Staging
- **S0, verification spikes and backup (no product code).** Back up `~/.claude` and `~/.claude.json`, and note the existing Keychain item name. Then:
  1. Three or more profiles logged in at once, kept across a restart. Confirm one Keychain item each.
  2. Path variants (`~`, trailing slash, symlink) and whether they change the suffix.
  3. The built-in profile with the variable unset still uses the existing login.
  4. Where `.claude.json` and `sessions/` land under a custom folder.
  5. `claude --resume <id>` works in a custom folder.
  6. The command that triggers first login, and Claude Code's logout command, under a custom folder.
  7. Keychain item names created by one login plus one MCP OAuth login (names only). Deleting an item from the app does or does not prompt.
  8. Plugin and MCP install commands work with `CLAUDE_CONFIG_DIR` set to a target folder.
  9. Opening Terminal with a `.command` file from a signed app: no Automation prompt.
  10. A status line can show the profile.
  11. Short check of option C tools.
  Exit criterion: spikes 1 to 7 and 9 pass.
- **S1, core and shell.** `ProfileStore`, `KeychainProbe`, `CompatCheck`, the registry and shell files, the zsh shim with `claude-p` and tab completion, per-profile status line, `doctor`. Usable on its own: no more logging out.
- **S2, sessions and launching.** `SessionIndex` and `Launcher`: merged list, resume in the owning profile and folder, live status, desktop/Cowork filter, launch log. Optional CLI list.
- **S3, menu-bar app.** Profile rows, default, New Terminal, recent and live sessions with Resume, Add profile with login detection, rename, set default, "Open window…", health line.
- **S4, copy-setup.** `SetupCopier` with dry run, backup and Restore. Step 1: skills, agents, commands, `CLAUDE.md`, MCP servers. Step 2: plugins (reinstall) and hooks (path rewrite or flag).
- **S5, full window and remaining lifecycle.** Window form, filter and search, tags in a sidecar file, full profile delete with Keychain cleanup, and stop if confirmed.
- **S6, conditional.** Desktop app / Cowork launcher and Remote Control, only after their own spikes pass.

### Test plan
- Core unit tests against fixture config folders with fake, secret-free `sessions/*.json`, `.claude.json` and plugin registries, one set per tested Claude Code version.
- Golden-file tests for the merge and the dry-run diff.
- Shell tests with a stub `claude` on PATH that prints its environment, run under a parent shell with `CLAUDE_CONFIG_DIR` and `ANTHROPIC_API_KEY` already set.
- Keychain tests under a test-only service prefix.
- A scripted checklist for the S0 spikes, rerun after each Claude Code minor update.

### Acceptance criteria
- Three or more profiles stay logged in at the same time across a reboot. Switching never needs a logout.
- After S1, every transcript that existed before is still listed and resumable, and `~/.claude` works without the shim.
- No code path sets `CLAUDE_CONFIG_DIR` for the built-in profile.
- With `CLAUDE_CONFIG_DIR` set to another profile in the parent shell, `claude-p <name>` still uses `<name>`, and plain `claude` uses the default.
- Changing the default never changes an already running session, and the next plain `claude` uses the new default with no shell reload.
- A new login is detected within 5 seconds of finishing. A duplicate-account login shows the warning.
- Renaming leaves the folder, the command name and the Keychain item unchanged.
- Deleting removes only that profile's recorded Keychain item, and `doctor` then reports no orphan items.
- Every session row and every app-opened window shows the correct profile. Resume always uses the profile that owns the transcript.
- A new session appears with the correct profile within 5 seconds. A killed Terminal window shows the session as stopped within 10 seconds. A reused PID is never shown as running.
- The copy dry run equals what is written. An unticked secret is never written. After a copy, the target's status line still names the target. Restore gives byte-identical files.
- Copy refuses while the target has a live session.
- Rebuilding the app does not bring back any permission prompt.
- `doctor` passes on a clean setup and names the specific fault when one is injected. After a Claude Code upgrade, the startup check passes or shows a visible warning.
