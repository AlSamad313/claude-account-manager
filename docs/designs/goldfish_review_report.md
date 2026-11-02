Status: resolved (auto accept, 2026-11-02: 53 fixes applied, 3 declined. D1 and D3 applied on the author's answer later that day; D2 declined, so the login-wide setting stays in S1.)

# Goldfish Annotated Review Report

**Target:** `claude-profile-manager.md` (`Lines 951-1578`, plus the matching specification sections where a fix needs them)

**Stage:** 9 (Implementation Readiness), round 1. **Source findings:** `implementation_readiness_report.md`, which holds the full descriptions and rationale; each entry below summarises them. The Stage 7 report is kept as `goldfish_review_report_stage7.md`.

No finding is contested: the plan defects are in the text. All proposed fixes are text changes to the design and none is tested.

**Checked on this machine while resolving (Claude Code 2.1.296, key names and non-secret values only):**
- `claude mcp add-json` has `-s, --scope`, default `local`.
- `claude auth login` has `--email`, which pre-fills the login page.
- Session records show `entrypoint` values `cli` and `claude-desktop`, `status` values `busy` and `idle`, and `procStart` as a date string such as `Sat Oct 31 10:58:34 2026`. No Cowork record was present, so its value is still unknown.

## Author decisions needed (from Stage 8)

These touch decisions you already made, so they are not applied unless you say so. Each fix below is written to work either way.

*   **D1: `.command` files call `claude-as`.** The file does `cd '<folder>'` then `exec ~/.claude-profiles/bin/claude-as '<commandName>' [--resume '<id>']`. **Gain:** launch rules exist once, so the app cannot drift from the wrapper; removes the app's `zsh -lic` `PATH` read at launch, and most of `implementation-readiness-1-9` and `1-22`. **Cost:** changes your accepted launch decision, and the app needs the shell layer installed before it can launch anything (flow 10 already makes this a setup step).
*   **D2: Move the login-wide setting out of S1 into its own stage, after the wrapper is in use, and split check 12.** **Gain:** S1 is smaller; a VS Code failure and desktop-app damage are judged separately. **Cost:** until that stage ships, Dock-opened apps and full-path calls stay on the built-in profile. You still get it in v1.
*   **D3: Put added profiles in `~/.claude-profiles/profiles/<id>/`.** **Gain:** app folders and profile folders stay apart, and the idle watcher need not see every transcript write. **Cost:** none now. After a profile exists, this cannot change without logging it out.

## Prioritized Findings & AI Suggestions

### [Issue 1] [`egm-implementation-readiness`, `egm-codebase-details`, `egm-style`] - Severity: High
*   **Affected Scope:** Lines 958-959, 964, 1315-1318
*   **Discrepancy Description:** S1 has no way to add a profile, and S2 has no way to launch or resume (`implementation-readiness-1-1`).
*   **Rationale:** Each stage is said to be usable alone.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Raised at Stages 5, 6, 8 and 9.
*   ***Proposed Edit:***
    *   Location: stage table; file 31.
    *   Change: Add a "When this stage ends, you can" column. S1: `cpm add <displayName> <commandName> <color>` creates the folder and row, then prints `claude-as <commandName>` to log in; next `cpm` run detects the login. S2: add `cpm new <name> [folder]` and `cpm resume <sessionId>`. "No more logging out" stays in S1, which now delivers it.

### [Issue 2] [`egm-implementation-readiness`, `egm-codebase-details`, `egm-style`] - Severity: High
*   **Affected Scope:** Lines 1397-1398, 1402-1403
*   **Discrepancy Description:** A wrapper test requires the fallback you rejected (`implementation-readiness-1-2`).
*   **Rationale:** It forces the wrong-account behaviour.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Direct contradiction of your decision.
*   ***Proposed Edit:***
    *   Location: file 40.
    *   Change: "missing, unreadable, or unknown-ID `default` → prints the problem and `cpm doctor`, exit 1, real binary not run".

### [Issue 3] [`egm-implementation-readiness`, `egm-codebase-details`, `egm-style`] - Severity: High
*   **Affected Scope:** Lines 968, 1098, 1112-1113, 1291, 1317, 1350, 1546
*   **Discrepancy Description:** S5 has no implementation plan (`implementation-readiness-1-3`).
*   **Rationale:** No owner, sequence, refusal, crash resume or tests.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Removal deletes logins; it cannot be left to the implementer.
*   ***Proposed Edit:***
    *   Location: new S5 section; files 10, 48.
    *   Change: New file `Sources/ProfileCore/ProfileRemover.swift` owns flow 7 and `cpm uninstall`: one confirmation first; refuse on `hasLiveSession`; then the flow 7 order, each step safe to repeat; `resume()` continues `deleting` rows at start. Backups for that profile go to the Trash too. Test file `ProfileRemoverTests.swift` covers order, refusal, resume and uninstall. Remove `beginDelete`/`finishDelete` from S1. Move logout into `ClaudeCLI`, placed in S1 (see issue 6).

### [Issue 4] [`egm-implementation-readiness`, `egm-codebase-details`, `egm-style`] - Severity: High
*   **Affected Scope:** Lines 963, 984-988, 1002-1008
*   **Discrepancy Description:** S0 cannot run with S0's files, and `/tmp` could give a false stop (`implementation-readiness-1-4`).
*   **Rationale:** `/tmp` may clear at reboot and is a symlink.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** A false result on a stop-the-project check would end the project wrongly.
*   ***Proposed Edit:***
    *   Location: files 2, 4; S0 table.
    *   Change: Test folders live at `~/.cpm-s0/<n>/` using `realpath`. S0 runs checks 1-8, 11-13 and 15-20. Check 10 and 14 run in S1 once files 16 and 17 exist. Check 9 and 21 run at the start of S3 once `make-app.sh` exists. None of the moved checks can stop the project. State this in the table.

### [Issue 5] [`egm-codebase-details`] - Severity: High
*   **Affected Scope:** Lines 1026-1042, 1101-1102
*   **Discrepancy Description:** The registry has nowhere to store the default (`codebase-details-1-1`).
*   **Rationale:** `default` cannot be regenerated.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Plain `claude` stops when `default` is broken, so it must be rebuildable.
*   ***Proposed Edit:***
    *   Location: file 6; Files on disk field list; Terms reconcile.
    *   Change: `Registry` gains `defaultProfileId: String` (seed `"builtin"`). Reconcile rewrites `default` and `names` from `profiles.json` whenever they disagree or are missing, and `cpm doctor --fix` does the same.

### [Issue 6] [`egm-codebase-details`, `egm-implementation-readiness`, `egm-style`] - Severity: High
*   **Affected Scope:** Lines 961-968, 1015-1019, 1138-1158, 1315-1318, 1347
*   **Discrepancy Description:** S1 does not build; its files depend on later stages (`codebase-details-1-2`).
*   **Rationale:** Targets with no sources, and `list`, `stop`, `doctor` and install reach forward.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Staging must compile.
*   ***Proposed Edit:***
    *   Location: stage table; files 5, 14, 15, 31, 35, 48.
    *   Change: `Package.swift` grows per stage: S1 declares `ProfileCore`, `cpm`, tests; S3 adds the app. Move files 18, 20, 23 (locator part) and 48 to S1. `cpm` subcommands per stage: S1 `add`, `doctor`, `default`, `install-shell`; S2 `list`, `new`, `resume`, `stop`; S5 `remove`, `uninstall`. `make-app.sh` splits: `make-cli.sh` (S1) builds and signs `cpm` and copies `shell/` to `~/.claude-profiles/bin/`; `make-app.sh` (S3) builds the app. `install-shell` reads from the folder beside the running `cpm`.

### [Issue 7] [`egm-codebase-details`, `egm-implementation-readiness`] - Severity: High
*   **Affected Scope:** Lines 1090-1092, 1257-1259, 1288-1290
*   **Discrepancy Description:** Flow 3 has no owner (`codebase-details-1-3`).
*   **Rationale:** Baseline, watch, retry and timeout are unassigned; retry can turn a valid login into `error`.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** This is the first thing a user does.
*   ***Proposed Edit:***
    *   Location: new file `Sources/ProfileCore/LoginCoordinator.swift`; files 10, 28.
    *   Change: Owns flow 3 with states `waiting`, `detected(Account, duplicate: Bool)`, `timedOut`. Timeout 10 minutes. Takes a new Keychain baseline at each attempt. A closed Terminal window is not detected, because `.command` launch gives no Apple Events; the waiting screen shows Retry and Discard all along. Launch uses `claude auth login` under that profile (S0 check 6). Zero or several new names leaves the profile `ready` with no recorded name and a warning, not `error` (Stage 8 N-a; matches the check 7 fallback).

### [Issue 8] [`egm-codebase-details`, `egm-implementation-readiness`, `egm-style`] - Severity: High
*   **Affected Scope:** Lines 1205-1221, 1265, 1439-1441, 1488-1489
*   **Discrepancy Description:** No named live-session query bypasses the display filter (`codebase-details-1-4`).
*   **Rationale:** A copy could run over a live desktop or Cowork session.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Terms promises this and nothing delivers it.
*   ***Proposed Edit:***
    *   Location: files 20, 21, 44, 49, 50; new remover.
    *   Change: `LiveSessionReader.liveRecords(for: Profile)` and `hasLiveSession(_:)`, never filtered, required by copy, Restore, removal and uninstall. Tests: a live `claude-desktop` record blocks each. Use "live" as the one word; retire `isRunning` and "running count" in code.

### [Issue 9] [`egm-implementation-readiness`, `egm-codebase-details`, `egm-style`] - Severity: Medium
*   **Affected Scope:** Lines 979, 984-988, 1007-1008, 1138, 1208, 1218-1219, 1360, 1442, 1476-1477
*   **Discrepancy Description:** S0 has no revert, pass conditions or durable output (`implementation-readiness-1-5`).
*   **Rationale:** Later files depend on S0 values, yet results are git-ignored.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Otherwise S0 results are lost.
*   ***Proposed Edit:***
    *   Location: S0 table; files 1, 2; new `docs/S0-results.md`.
    *   Change: Add a "Passes when" column. File 2 gets teardown: `launchctl unsetenv`, delete test logins with `claude auth logout`, remove `~/.cpm-s0/`. Committed, email-free `docs/S0-results.md` records constants (tolerance, `entrypoint` values, install commands, chosen MCP path) and each fallback taken. Later files cite those names.

### [Issue 10] [`egm-implementation-readiness`, `egm-codebase-details`] - Severity: Medium
*   **Affected Scope:** Lines 1096, 1151-1153, 1209, 1215-1217, 1523-1539, 1549-1554
*   **Discrepancy Description:** No plan branch for S0 fallbacks (`implementation-readiness-1-6`).
*   **Rationale:** Dropping the login-wide setting leaves unconditional files.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** One section suffices.
*   ***Proposed Edit:***
    *   Location: new "If a check fails" table after the stage table.
    *   Change: One row per fallback naming files. Check 12 fails: remove 53, 54, 55 and related `doctor` rows; `setDefault` skips the applier. Check 15 fails: `SessionRow.mayBeStale`, shown in the row. Check 7 fails: per issue 7.

### [Issue 11] [`egm-implementation-readiness`, `egm-codebase-details`, `egm-style`] - Severity: Medium
*   **Affected Scope:** Lines 1163-1173, 1390-1404
*   **Discrepancy Description:** File 16's recap differs from flow 1 (`implementation-readiness-1-7`).
*   **Rationale:** Two versions of a safety rule.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Stage 8 Major 9 raised it too.
*   ***Proposed Edit:***
    *   Location: files 16, 40.
    *   Change: File 16 says "implement flow 1" and lists only what the flow lacks. File 40's stub prints all variables; add cases for the title variable, all three login variables, `-r <sid>`, a value inside `~/.claude-profiles/`, and the log line; one bullet per case.

### [Issue 12] [`egm-implementation-readiness`, `egm-codebase-details`] - Severity: Medium
*   **Affected Scope:** Lines 1163-1169, 1129-1131, 1401-1402
*   **Discrepancy Description:** The override path and wrapper parsing are undefined (`implementation-readiness-1-8`).
*   **Rationale:** `id` is never set; no method for reading JSON. Includes Stage 8 Major 3.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** The "already running" check was my addition, breaks the no-JSON rule and can refuse a valid resume on a reused PID.
*   ***Proposed Edit:***
    *   Location: flow 1; file 16; check table.
    *   Change: Override: warn, set the title variable, log, `exec` immediately; no profile variables, no resume check. **Remove the wrapper's "already running" check**; the app shows Stop instead of Resume. Add a check on what Claude Code does when one session is resumed twice. Ownership check uses only a file-name glob. Refusal prints owner and command without the folder.

### [Issue 13] [`egm-implementation-readiness`, `egm-codebase-details`, `egm-style`] - Severity: Medium
*   **Affected Scope:** Lines 1064-1066, 1070-1071, 1231-1232, 1369, 1418-1421
*   **Discrepancy Description:** The Swift launch path drops required environment and behaviour (`implementation-readiness-1-9`).
*   **Rationale:** `LaunchEnv` undefined; no `cd` failure branch.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Depends on D1.
*   ***Proposed Edit:***
    *   Location: files 8, 22, 42.
    *   Change: Define `LaunchEnv { set: [String: String], unset: [String] }`. Give the exact `.command` template; `cd '<folder>' || { print message; exit 1; }`. **With D1:** template is `cd` then `exec claude-as`. **Without D1:** template lists every flow 2 line. File 42 asserts each line.

### [Issue 14] [`egm-implementation-readiness`] - Severity: Medium
*   **Affected Scope:** Lines 1101-1102
*   **Discrepancy Description:** Two-process and multi-file writes can stop `claude` (`implementation-readiness-1-10`).
*   **Rationale:** No lock; order can leave `default` naming an ID not in `names`.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Stage 5 M4 and Stage 8 Major 4 raised this; the fix is small.
*   ***Proposed Edit:***
    *   Location: file 10.
    *   Change: Every change: `flock` on `~/.claude-profiles/.lock`, re-read `profiles.json`, change, write `profiles.json`, then `names`, then `default`. Adding writes the new row before anything points to it. Removing moves `default` first. A crash is repaired by reconcile.

### [Issue 15] [`egm-implementation-readiness`, `egm-codebase-details`, `egm-style`] - Severity: Medium
*   **Affected Scope:** Lines 1027-1030, 1044-1045, 1087, 1178-1179
*   **Discrepancy Description:** Built-in row values, `names` visibility by state and validators are undefined (`implementation-readiness-1-11`).
*   **Rationale:** `claude-as` could start in a folder being trashed.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Literal values remove a decision.
*   ***Proposed Edit:***
    *   Location: files 6, 10; Terms.
    *   Change: Seed row: `id` `builtin`, `commandName` `main`, `displayName` from the account's organization name, else `Main`; colour `#2F6FDE`. `names` holds rows in `ready`, `loggedOut` and `pendingLogin`; not `deleting` or `error`. Validators for `commandName`, `displayName` and `color` follow the rules in Terms, with hostile-value tests.

### [Issue 16] [`egm-implementation-readiness`, `egm-codebase-details`] - Severity: Medium
*   **Affected Scope:** Lines 1181, 1317, 1347-1348, 1505-1508, 1537-1539
*   **Discrepancy Description:** Shell installation lacks an owner, exact text and resource sources (`implementation-readiness-1-12`).
*   **Rationale:** These edit startup files.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Literal text prevents mistakes.
*   ***Proposed Edit:***
    *   Location: new `Sources/ProfileCore/ShellInstaller.swift`; flow 10.
    *   Change: Owns install; both "Set up shell" and `cpm install-shell` call it. Literal lines: `~/.zshenv`: `path=("$HOME/.claude-profiles/bin" $path)`; `~/.zshrc`: `[[ -r "$HOME/.claude-profiles/bin/claude-profiles.zsh" ]] && source "$HOME/.claude-profiles/bin/claude-profiles.zsh"`. Loads with `launchctl bootstrap gui/$UID`. Writes `bin/VERSION`; rerunning never resets `default`. `cpm` is signed with the same identity; check 21 covers `cpm` as well as the app. Bundle includes the plist. Idempotency tests.

### [Issue 17] [`egm-implementation-readiness`] - Severity: Medium
*   **Affected Scope:** Lines 1526-1531, 1536-1539, 1554
*   **Discrepancy Description:** `apply-default-env` has no failure semantics (`implementation-readiness-1-13`).
*   **Rationale:** Broken inputs at login are undefined.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Leaving a stale value is a wrong-account risk.
*   ***Proposed Edit:***
    *   Location: file 53, 54.
    *   Change: Broken inputs: `launchctl unsetenv`, exit 1. So a Dock app falls to the built-in profile, and `doctor` reports it. LaunchAgent stdout and stderr go to `logs/default-env.log`. `setDefault` writes files first, then runs the applier; a failure is reported but does not undo the default, because the wrapper still honours it.

### [Issue 18] [`egm-implementation-readiness`] - Severity: Medium
*   **Affected Scope:** Lines 1060-1068, 1371-1377, 1388-1404, 1551
*   **Discrepancy Description:** Tests have no isolation from the real machine (`implementation-readiness-1-14`).
*   **Rationale:** They could overwrite your real registry and login-wide setting.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Must be fixed before any test exists.
*   ***Proposed Edit:***
    *   Location: files 8, 38, 40, 55; Test plan.
    *   Change: `ConfigLocator` becomes a struct taking `home` and `profilesRoot`. Shell tests set a temporary `HOME`. A test guard aborts if a resolved path is under the real home. `launchctl` is always a stub in tests.

### [Issue 19] [`egm-implementation-readiness`, `egm-codebase-details`] - Severity: Medium
*   **Affected Scope:** Lines 1430-1433, 1438-1443
*   **Discrepancy Description:** Copy plan data cannot express flow 6's rules (`implementation-readiness-1-15`).
*   **Rationale:** No hashes, conflict choices or partial results.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Needed to implement the safety rules.
*   ***Proposed Edit:***
    *   Location: files 43, 44.
    *   Change: Add to `CopyItem`: `beforeHash`, `conflict: skip | overwrite | rename`, `pointsIntoSource`, `secrets: [SecretField(name, masked, ticked)]`, `writeMethod: direct | command(removal)`. `apply -> ApplyResult(done, failed, backupId)`. Order: direct writes, then commands. An unticked secret drops that variable; a server left unable to run is shown as such in the preview. Skills copy per folder. Target must be `ready` and added; source must be `ready`.

### [Issue 20] [`egm-implementation-readiness`, `egm-codebase-details`] - Severity: Medium
*   **Affected Scope:** Lines 1052, 1441-1443, 1467-1469
*   **Discrepancy Description:** Restore and backup logic has contradictions (`implementation-readiness-1-16`).
*   **Rationale:** Modes conflict; install commands undefined in the manifest. Includes Stage 8 Major 6 on `.claude.json`.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Otherwise Restore's promise is unclear.
*   ***Proposed Edit:***
    *   Location: files 7, 47; flow 6.
    *   Change: Backups are stored 0600 with the original mode in the manifest. Files changed by commands are not in the manifest; the app shows each removal command. Restore states plainly that `.claude.json` is rewritten often by Claude Code, so it will usually be listed as changed and left alone; the backup stays available.

### [Issue 21] [`egm-implementation-readiness`, `egm-codebase-details`] - Severity: Medium
*   **Affected Scope:** Lines 1458-1462
*   **Discrepancy Description:** JSON rewrite and settings merge are under-specified (`implementation-readiness-1-17`).
*   **Rationale:** Key order and fidelity matter in a file you did not write.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** One choice removes the ambiguity.
*   ***Proposed Edit:***
    *   Location: file 46; NFR.
    *   Change: `settings.json` is parsed with `JSONSerialization` and rewritten preserving unknown keys; key order is not preserved, and the backup keeps the original. `.claude.json` is never written directly by v1: MCP goes through `claude mcp add-json` only (see issue 28). The merger follows the flow 6 table row by row. Golden tests.

### [Issue 22] [`egm-implementation-readiness`, `egm-codebase-details`, `egm-style`] - Severity: Medium
*   **Affected Scope:** Lines 1448-1453
*   **Discrepancy Description:** SecretDetector cannot be implemented consistently (`implementation-readiness-1-18`).
*   **Rationale:** The key regex matches `author` and `monkey`. Free text has no keys.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** A security control needs exact rules.
*   ***Proposed Edit:***
    *   Location: file 45; flow 6; new `SecretDetectorTests.swift`; stage table.
    *   Change: `env` and `headers`: every value is masked, no pattern needed. `args`: value patterns only. Value patterns: `sk-[A-Za-z0-9_-]{20,}`, `ghp_[A-Za-z0-9]{20,}`, `xox[bap]-`, and hex or base64 runs of 40 or more. Free text (skills, `CLAUDE.md`, hooks): value patterns only; a match flags and unticks the whole file, with no masking inside it. Doctor does not use it, so file 45 moves to S4.

### [Issue 23] [`egm-implementation-readiness`, `egm-codebase-details`] - Severity: Medium
*   **Affected Scope:** Lines 1138-1143
*   **Discrepancy Description:** CompatCheck is non-actionable (`implementation-readiness-1-19`).
*   **Rationale:** Formats undefined. Includes Stage 5 M5 and Stage 8 Major 7: the exact-version banner will nearly always show.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Raised at three stages; the shape check is the real protection.
*   ***Proposed Edit:***
    *   Location: file 14, 29; flow 8; flow 11; file 2.
    *   Change: Format enum with required keys per format: `sessionRecord`, `transcript`, `configFile`, `settings`. `canWrite` returns false if keys are missing. **Remove the banner and `testedVersions`**; `doctor` prints the version as a note. Keychain-name probe moves to Doctor. After an update, rerun only the checks whose subject changed.

### [Issue 24] [`egm-implementation-readiness`, `egm-codebase-details`, `egm-style`] - Severity: Medium
*   **Affected Scope:** Lines 1148-1158, 1193, 1318, 1452, 1545
*   **Discrepancy Description:** Doctor differs from flow 11 (`implementation-readiness-1-20`).
*   **Rationale:** Two lists of checks.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** One list.
*   ***Proposed Edit:***
    *   Location: file 15; flow 11; file 21.
    *   Change: File 15 says "implement the flow 11 table", which gains level, source and exit effect columns. Add "folder with no row". Remove plugin registries and `messagingSocketPath`. Unchanged `configDir` means it equals `newConfigDir(id)`. `sessions(for:)` returns `SessionIndexResult(rows, parseFailures)`. Notes do not affect the exit code; faults give exit 2.

### [Issue 25] [`egm-implementation-readiness`, `egm-codebase-details`] - Severity: Medium
*   **Affected Scope:** Lines 1228-1229, 1255-1259, 1264-1269, 1275-1277, 1288-1292
*   **Discrepancy Description:** S3 UI and action coverage is missing (`implementation-readiness-1-21`).
*   **Rationale:** Sign in, Retry, Discard, Choose folder, profile filter and notice have no home.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Zero-Decision Rule.
*   ***Proposed Edit:***
    *   Location: files 22, 24, 25, 28.
    *   Change: `resume(session:, folderOverride:)`. New Terminal and login windows start in `$HOME`. Live rows show Stop only; others show Resume only. List every AppModel action. Filter matches title, folder and profile name. The account is re-read on panel open and on file change, not every 2 seconds (Stage 8 N-g).

### [Issue 26] [`egm-implementation-readiness`, `egm-style`, `egm-codebase-details`] - Severity: Medium
*   **Affected Scope:** Lines 1237-1238, 1243-1244, 1421, 1544
*   **Discrepancy Description:** `.command` cleanup and binary lookup leave behaviour open (`implementation-readiness-1-22`).
*   **Rationale:** "Older than one launch" is not testable; `zsh -lic` can hang.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Small.
*   ***Proposed Edit:***
    *   Location: files 22, 23, 56.
    *   Change: Delete `.command` files older than 60 seconds. `PATH` read prints a unique marker line then `$PATH`, parsed from after the marker, with a 5-second timeout. `ProcessRunner` returns stdout, stderr and exit code, with a timeout. (With D1, the `PATH` read is only for `CompatCheck` and `ClaudeCLI`.)

### [Issue 27] [`egm-implementation-readiness`, `egm-codebase-details`, `egm-style`] - Severity: Medium
*   **Affected Scope:** Lines 1019, 1491-1498, 1565-1568
*   **Discrepancy Description:** Test coverage does not meet the plan's own bar (`implementation-readiness-1-23`).
*   **Rationale:** No rule list; missing modules; S2 measure has no owner.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** The stated bar cannot be verified.
*   ***Proposed Edit:***
    *   Location: Verification Plan; test files.
    *   Change: Replace the sentence with named test files for each module listed in the finding, each with its cases, and name exempt modules (views, `Info.plist`). Add `scripts/measure-panel.sh` in S2 with the Limits target; if missed, cache to disk is the next step. Read beyond 64 KB when no title key is found, up to 1 MB.

### [Issue 28] [`egm-codebase-details`] - Severity: Medium
*   **Affected Scope:** Lines 1474-1477, 1442
*   **Discrepancy Description:** `claude mcp add-json` defaults to `local` scope (`codebase-details-1-5`).
*   **Rationale:** Confirmed on this machine. Without `--scope user` it would not land where the preview says.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Direct mismatch.
*   ***Proposed Edit:***
    *   Location: files 4, 48; flow 6; check 8.
    *   Change: Command is `claude mcp add-json <name> <json> --scope user`. Check 8 asserts the entry lands in top-level `mcpServers`. State that project-nested `mcpServers` are not copied. Keep one MCP path with no runtime fallback (Stage 8 Major 6).

### [Issue 29] [`egm-codebase-details`, `egm-implementation-readiness`] - Severity: Medium
*   **Affected Scope:** Lines 1086-1099
*   **Discrepancy Description:** ProfileStore does not cover the state machine (`codebase-details-1-6`).
*   **Rationale:** Discard, Retry and transitions are missing.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Terms defines states that no method moves between.
*   ***Proposed Edit:***
    *   Location: file 10, 38.
    *   Change: Add `discard(id)`, `markLoggedOut(id)`, `markReady(id, account)`, `markError(id, reason)`, `updateAccount(id, account) -> AccountChange`, and a reconcile result listing orphans. Tests for each.

### [Issue 30] [`egm-codebase-details`, `egm-style`, `egm-implementation-readiness`] - Severity: Medium
*   **Affected Scope:** Lines 1569-1572, 984-986
*   **Discrepancy Description:** E2E covers CUJs 1-6 of 8 and points to the wrong file (`codebase-details-1-7`).
*   **Rationale:** Stop and removal are never walked through.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Mechanical.
*   ***Proposed Edit:***
    *   Location: Verification Plan; new `docs/E2E-checklist.md`.
    *   Change: Cover CUJs 1-8 in a separate step-by-step checklist.

### [Issue 31] [`egm-codebase-details`, `egm-implementation-readiness`] - Severity: Medium
*   **Affected Scope:** Lines 1205-1206, 1215-1219
*   **Discrepancy Description:** The join of live records and transcripts is undefined (`codebase-details-1-8`).
*   **Rationale:** A new session may have no transcript yet, which affects CUJ 2.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Local key names confirm the sources.
*   ***Proposed Edit:***
    *   Location: files 18, 20, 21.
    *   Change: Outer join on `sessionId`. Live record with no transcript still gives a row. Title order: record `name`, then `customTitle`, `aiTitle`, `summary`, then "Untitled". `SessionRow` gains `entrypoint`, `pid`, `procStart`. Display filter shows `cli` and hides `claude-desktop`; the Cowork value comes from check 16, and an unknown value is shown.

### [Issue 32] [`egm-style`, `egm-implementation-readiness`, `egm-codebase-details`] - Severity: Medium
*   **Affected Scope:** Lines 1066, 1154, 1166, 1391, 1396
*   **Discrepancy Description:** `ANTHROPIC_API_KEY` used where all login variables are meant (`style-1-1`).
*   **Rationale:** An implementer would test one variable.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Mechanical.
*   ***Proposed Edit:***
    *   Location: as listed.
    *   Change: Use "login variables" and test all three.

### [Issue 33] [`egm-style`] - Severity: Medium
*   **Affected Scope:** Lines 1495, 1497, 1498
*   **Discrepancy Description:** Absolute test claims conflict with flow 6 (`style-1-2`).
*   **Rationale:** Install commands can write registries.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Tests must match the promise.
*   ***Proposed Edit:***
    *   Location: file 50.
    *   Change: "direct writes match the plan", "never written by SetupCopier itself", "restore of files unchanged since the copy is byte-identical and restores the mode". Add the cases from finding 1-23.

### [Issue 34] [`egm-style`, `egm-implementation-readiness`, `egm-codebase-details`] - Severity: Medium
*   **Affected Scope:** Lines 1017, 1236-1237, 1299, 1324-1325, 1416
*   **Discrepancy Description:** TerminalOpener contradicts itself (`style-1-3`).
*   **Rationale:** Includes Stage 5 M11 and Stage 8 Major 8.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Raised at three stages.
*   ***Proposed Edit:***
    *   Location: files 22, 29, 32, 5.
    *   Change: Both the app and `cpm new`/`cpm resume` open files with `/usr/bin/open -a Terminal <file>` through `ProcessRunner`, subject to check 9. Remove the AppKit implementation. Keep `TerminalOpener` as a one-method test seam. Keep `ProfileUI` as a library, so the window and panel can be previewed apart from the app shell.

### [Issue 35] [`egm-style`, `egm-codebase-details`] - Severity: Medium
*   **Affected Scope:** Lines 1526-1529, 1544-1547, 1551, 328
*   **Discrepancy Description:** "The one place that runs Claude Code or `launchctl`" is contradicted (`style-1-4`).
*   **Rationale:** File 53's script runs `launchctl`; Doctor is missing.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Rule must be true.
*   ***Proposed Edit:***
    *   Location: Components; file 56.
    *   Change: "The one Swift API for running external commands." List all callers: CompatCheck, ClaudeCLI, Doctor, DefaultEnvApplier, ShellInstaller, Launcher. Unit tests use a fake; only file 55 uses a stub on `PATH`.

### [Issue 36] [`egm-implementation-readiness`, `egm-codebase-details`] - Severity: Low
*   **Affected Scope:** Lines 1003-1005, 1198-1199
*   **Discrepancy Description:** Process start time and liveness details (`implementation-readiness-1-24`).
*   **Rationale:** It guards signal delivery.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** The record is a one-second date string, so one source is needed.
*   ***Proposed Edit:***
    *   Location: files 4, 19, 20.
    *   Change: Parse `procStart` as seen on this machine. Compare with `sysctl` start time to 1 second in both S0 and production. `kill` returning `EPERM` means alive. Check 15 confirms the Swift field is usable.

### [Issue 37] [`egm-implementation-readiness`] - Severity: Low
*   **Affected Scope:** Lines 993-997
*   **Discrepancy Description:** S0 backup details (`implementation-readiness-1-25`).
*   **Rationale:** No restore procedure; backup copies `.key` files.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** This backup protects the login you rely on.
*   ***Proposed Edit:***
    *   Location: file 3.
    *   Change: Destination 0700; names file `keychain-names.txt`; stop other Claude Code sessions first; print restore steps; exclude `sessions/`.

### [Issue 38] [`egm-implementation-readiness`, `egm-codebase-details`] - Severity: Low
*   **Affected Scope:** Lines 1129-1133
*   **Discrepancy Description:** `launch.log` details (`implementation-readiness-1-26`).
*   **Rationale:** Format and failure rule undefined. Includes Stage 5 N5.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Small.
*   ***Proposed Edit:***
    *   Location: files 13, 16.
    *   Change: Tab-separated fields with tabs and newlines replaced by spaces. Logging failure never blocks launch. Only EventLog rotates, at start. Remove per-module `os_log`; keep the two logs.

### [Issue 39] [`egm-implementation-readiness`, `egm-codebase-details`, `egm-style`] - Severity: Low
*   **Affected Scope:** Lines 1094, 1291
*   **Discrepancy Description:** `setCommandName` goes beyond the spec (`implementation-readiness-1-27`).
*   **Rationale:** Goal says display name only.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Simplest correct option.
*   ***Proposed Edit:***
    *   Location: files 10, 28.
    *   Change: Remove it from v1. The command name is fixed at creation.

### [Issue 40] [`egm-implementation-readiness`, `egm-style`, `egm-codebase-details`] - Severity: Low
*   **Affected Scope:** Lines 1122, 1178-1180, 1562-1564
*   **Discrepancy Description:** Status-line script details (`implementation-readiness-1-28`).
*   **Rationale:** `~` expansion unverified.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Absolute paths avoid the question.
*   ***Proposed Edit:***
    *   Location: files 12, 17; Verification Plan.
    *   Change: Write an absolute path. Unknown ID prints nothing. Colour uses 24-bit ANSI. Add `profile-label` and `scripts/*.sh` to the syntax check.

### [Issue 41] [`egm-implementation-readiness`] - Severity: Low
*   **Affected Scope:** Lines 1315-1318
*   **Discrepancy Description:** `cpm` output is unspecified (`implementation-readiness-1-29`).
*   **Rationale:** `cpm` is the S1 interface.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Short.
*   ***Proposed Edit:***
    *   Location: file 31.
    *   Change: `list` prints tab-separated profile, status, title, folder, session ID. `stop` when not on a terminal without `--yes` exits 1. `--profile` matches `commandName`. Remove `cpm names`, because completion reads the file.

### [Issue 42] [`egm-codebase-details`] - Severity: Low
*   **Affected Scope:** Line 1190
*   **Discrepancy Description:** First-user-message fallback can surface pasted secrets (`codebase-details-1-9`).
*   **Rationale:** It would show in the menu.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Issue 31 replaces it.
*   ***Proposed Edit:***
    *   Location: file 18.
    *   Change: Remove the fallback. Rows without a title show "Untitled" and the folder.

### [Issue 43] [`egm-codebase-details`] - Severity: Low
*   **Affected Scope:** Lines 1015, 1505, 1538
*   **Discrepancy Description:** Toolchain is unstated (`codebase-details-1-10`).
*   **Rationale:** Zero-Decision Rule.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Short.
*   ***Proposed Edit:***
    *   Location: file 5; README.
    *   Change: `swift-tools-version: 5.10`, XCTest, Command Line Tools sufficient; S0 confirms what is installed.

### [Issue 44] [`egm-codebase-details`, `egm-implementation-readiness`, `egm-style`] - Severity: Low
*   **Affected Scope:** Lines 1298, 1337-1338
*   **Discrepancy Description:** Icon tinting and generation (`codebase-details-1-11`).
*   **Rationale:** `MenuBarExtra` may ignore tint, which would break CUJ 4.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** A rendered image avoids the unknown.
*   ***Proposed Edit:***
    *   Location: files 29, 34.
    *   Change: Draw the icon as a non-template `NSImage` in the profile's colour. App icon: a single-colour `.icns` from a script in `scripts/`.

### [Issue 45] [`egm-style`, `egm-codebase-details`, `egm-implementation-readiness`] - Severity: Low
*   **Affected Scope:** Lines 953-955, 1514-1519
*   **Discrepancy Description:** Stale drafting references (`style-1-5`).
*   **Rationale:** No `design_draft.md`; no `git init` step.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Mechanical.
*   ***Proposed Edit:***
    *   Location: intro; file 52; file 1.
    *   Change: Replace file 52 with a layout statement. Add `git init` to S0. Keep review reports in `docs/designs/`.

### [Issue 46] [`egm-style`, `egm-codebase-details`, `egm-implementation-readiness`] - Severity: Low
*   **Affected Scope:** Lines 961-969
*   **Discrepancy Description:** Stage table gaps (`style-1-6`).
*   **Rationale:** Files 51-52 and four copy items have no stage.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Rewritten by issues 1 and 6.
*   ***Proposed Edit:***
    *   Location: stage table.
    *   Change: S4 step 1 adds `permissions`, `model`, `env`. Step 2: plugins and marketplaces, hooks. README grows each stage.

### [Issue 47] [`egm-style`] - Severity: Low
*   **Affected Scope:** Lines 1101-1104, 1112, 1530, 1553
*   **Discrepancy Description:** Removal vocabulary (`style-1-7`).
*   **Rationale:** "Delete" and "remove" mix.
*   ***AI Suggestion:*** DECLINE
*   ***Justification:*** Meaning is clear in context, and the S5 rewrite covers the main passages.
*   ***Proposed Edit:*** None.

### [Issue 48] [`egm-style`] - Severity: Low
*   **Affected Scope:** Lines 1114, 1151, 1232, 1383
*   **Discrepancy Description:** Term drift for Keychain name and binary (`style-1-8`).
*   **Rationale:** "Unsuffixed" versus "plain".
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Safety term; quick.
*   ***Proposed Edit:***
    *   Location: as listed.
    *   Change: "plain `Claude Code-credentials`" and "real binary".

### [Issue 49] [`egm-style`, `egm-implementation-readiness`, `egm-codebase-details`] - Severity: Low
*   **Affected Scope:** Line 1486
*   **Discrepancy Description:** Required UI wording sits behind a code (`style-1-9`).
*   **Rationale:** The wording is elsewhere.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Three short strings.
*   ***Proposed Edit:***
    *   Location: file 49.
    *   Change: Copy the three messages into the entry.

### [Issue 50] [`egm-style`] - Severity: Low
*   **Affected Scope:** Lines 1243-1246
*   **Discrepancy Description:** Repetition in file 23 (`style-1-10`).
*   **Rationale:** Same thing said twice.
*   ***AI Suggestion:*** DECLINE
*   ***Justification:*** Issue 26 rewrites this entry.
*   ***Proposed Edit:*** None.

### [Issue 51] [`egm-style`] - Severity: Low
*   **Affected Scope:** Lines 1015-1016, 1252
*   **Discrepancy Description:** "Current SwiftUI" is vague (`style-1-11`).
*   **Rationale:** `@Observable` sets the minimum (unverified).
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** One phrase.
*   ***Proposed Edit:***
    *   Location: file 5.
    *   Change: "macOS 14 minimum, for `@Observable`".

### [Issue 52] [`egm-style`] - Severity: Low
*   **Affected Scope:** Lines 1050-1053, 1130
*   **Discrepancy Description:** "Used by every module that writes" overclaims (`style-1-12`).
*   **Rationale:** EventLog appends.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** One phrase.
*   ***Proposed Edit:***
    *   Location: file 7.
    *   Change: "every module that replaces a file".

### [Issue 53] [`egm-style`] - Severity: Low
*   **Affected Scope:** Lines 985-988, 1094, 1208-1209, 1219, 1291, 1337, 1510, 1554
*   **Discrepancy Description:** Terse or undefined phrases (`style-1-13`).
*   **Rationale:** "The S0 check" names no check.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Mostly removed by other fixes.
*   ***Proposed Edit:***
    *   Location: as listed.
    *   Change: Cite checks 15 and 16 by number. Use "S0 table". One rerun trigger: "after a Claude Code update".

### [Issue 54] [`egm-style`, `egm-implementation-readiness`, `egm-codebase-details`] - Severity: Low
*   **Affected Scope:** Lines 973-975, 1160, 1175, 1285, 1350, 1379, 1414, 1491, 1523, 1541, 1549
*   **Discrepancy Description:** Structure and numbering (`style-1-14`).
*   **Rationale:** File 56 precedes 55; H4 for both levels; shell paths inconsistent.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** The plan is being reorganised by stage anyway.
*   ***Proposed Edit:***
    *   Location: plan headings.
    *   Change: Stage headings H4, file headings H5. Renumber in order. All scripts under `shell/`, with full paths.

### [Issue 55] [`egm-style`, `egm-codebase-details`] - Severity: Low
*   **Affected Scope:** Lines 1037, 1040, 1077-1078, 1475-1476, 1546
*   **Discrepancy Description:** Field-name issues (`style-1-15`).
*   **Rationale:** `oauthAccount.displayName` collides; logout hidden in file 56.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Small.
*   ***Proposed Edit:***
    *   Location: files 6, 9, 48; field list.
    *   Change: Stop reading `oauthAccount.displayName`. Add `organizationName` and `lastError` to the field list. List logout in file 48.

### [Issue 56] [`egm-style`] - Severity: Low
*   **Affected Scope:** Lines 985, 1102, 1233-1237, 1256, 1317, 1390-1404, 1418-1420, 1486, 1508
*   **Discrepancy Description:** Readability (`style-1-16`).
*   **Rationale:** Long lists and wrap width.
*   ***AI Suggestion:*** DECLINE
*   ***Justification:*** The rewrite handles the run-on list (issue 11); wrap width has no effect on meaning.
*   ***Proposed Edit:*** None.
