# Goldfish Review Report

**Target:** `docs/designs/claude-profile-manager.md` (`Lines 951-1578`, "## Detailed Implementation Plan" to the end of the file)

**Mode:** REVIEW. **GoldfishContext:** none. **Round:** 1.

## Reviewer Status

| Reviewer | Status |
| --- | --- |
| `egm-implementation-readiness` | completed |
| `egm-codebase-details` | completed |
| `egm-style` | completed |

No reviewer failed or needed a retry.

## Verdict

**Not ready to build.** Both readiness lenses found that the plan fails the Zero-Decision Rule and the rule that each phase is stable, buildable and usable.

| Severity | Count |
| --- | --- |
| Blocker | 8 |
| Major | 27 |
| Nit | 21 |
| **Total** | **56** |

These are counts after deduplication. The raw reports held 5 Blockers, 18 Majors and 9 Nits (codebase-details), 4, 21 and 9 (implementation-readiness), and 1, 11 and 16 (style).

## How this report was consolidated

* **IDs:** each finding has one stable ID, `<reviewer>-1-<n>`. Merged findings take the ID of one lead reviewer, and the others appear under "Reviewer Lens".
* **Severity:** a merged finding carries the highest severity any reviewer gave it. Where reviewers disagreed, the finding says so.
* **Checks by the synthesizer:** the plan text and main specification references behind the Blockers and most Majors were re-read against the document. All quoted plan defects below were confirmed there. Nothing was run. Local key-name checks were done by codebase-details and were not repeated.
* **Corrections to raw reports:**
  * implementation-readiness cited two lines that do not exist ("1589", "1590"). They are normalized to the specification lines it meant, 588-590 and 915-918.
  * codebase-details said the filtered `sessions(for:)` is the only live-session API described. File 20 (lines 1205-1210) also reads records with no filter, so `codebase-details-1-4` is worded more narrowly.
* **Unverified claims:** claims about macOS, SwiftPM or Claude Code behaviour that no reviewer could test are marked "unverified". They are also listed at the end of the report.
* **Secrets:** no reviewer reported a credential value, a Keychain content or a `.key` file content, and this report contains none.

## Prioritized Findings (Blockers -> Major -> Nits)

---

## Blockers

### `implementation-readiness-1-1` S1 has no way to add a profile, and "no more logging out" is unsupported - Severity: Blocker
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`, `egm-style` (rated Major)
* **Affected Scope:** `Lines 958-959, 964, 1315-1318`
* **Discrepancy Description**: The S1 row promises "no more logging out", and line 958 says each stage is usable on its own. `cpm` has no add or login subcommand (1315-1318). Launcher, which flow 3 step 4 needs, is S2 (file 22). The Add Profile UI is S3 (file 28). S2 has a similar gap: it promises launch and resume, but the UI is S3 and `cpm` has no launch or resume subcommand.
* **Rationale & Recommendation**: Either add `cpm add <displayName> <commandName> <color>` and pull the launch path it needs into S1, or change the S1 deliverable and move "no more logging out" to S3. Do the same for S2 (`cpm new` and `cpm resume`, or a redefined deliverable). State per stage what the user can do when it ends.

### `implementation-readiness-1-2` Wrapper test requires the fallback the author rejected - Severity: Blocker
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1397-1398, 1402-1403` (spec `138-139, 416-417`, mitigation at `904`)
* **Discrepancy Description**: The test case "missing `default` file → warning and built-in" contradicts flow 1 and the recorded Decision, which require plain `claude` to print the problem and exit 1 without starting. It also contradicts "broken `default` → plain `claude` exits 1" in the same list. As written, the test forces the wrong-account behaviour this design exists to prevent.
* **Rationale & Recommendation**: Replace it with "missing `default` → prints the problem and `cpm doctor`, exit 1, real binary not run". Add or merge cases for an unreadable `default` and for an ID not in `names`.

### `implementation-readiness-1-3` S5 has no implementation plan - Severity: Blocker
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details` (Major), `egm-style` (Major)
* **Affected Scope:** `Lines 968, 1098, 1112-1113, 1291, 1317, 1350, 1546` (spec `217, 273, 608-615, 892-898`)
* **Discrepancy Description**: S5 is only "changes to 10, 15, 31", with no file section, and the test heading stops at S4. Undefined items:
  * The module that owns flow 7 and `cpm uninstall`, and the sequence for each.
  * The live-session refusal. S1's `beginDelete` has no such check and no caller, and could Trash a logged-in folder and orphan its Keychain item.
  * Crash resume from `deleting`, which `reconcile` omits, and whether each step is idempotent.
  * The choices about what happens to history.
  * What changes in file 15.

  The stage row also omits files S5 needs: 11, 24, 28 and 48. Logout lives in file 48 (S4, disclosed only at line 1546), yet "Sign out and retry" is S3.
* **Rationale & Recommendation**: Add a real S5 section with an ordered algorithm, signatures, and tests for removal order, refusal, resume and uninstall. Move logout into a location available by S3.

### `implementation-readiness-1-4` The S0 gate cannot run with S0's files and could give a false project stop - Severity: Blocker
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details` (Major, `/tmp` part only), `egm-style` (out-of-remit note)
* **Affected Scope:** `Lines 963, 984-988, 1002-1008` (spec `236-243, 698, 705-706, 710, 715, 717`)
* **Discrepancy Description**: Two problems:
  * **Test folder location:** test folders sit under `/tmp/cpm-s0/`. Check 2 can stop the project and must survive a reboot, but `/tmp` is cleared at reboot (unverified). `/tmp` is also a symlink to `/private/tmp` (unverified), and Terms says a symlinked spelling changes the Keychain name.
  * **Missing artifacts:** checks 9 and 21 need a signed app, but signing arrives with file 35 in S3. Checks 10 and 14 need status-line and wrapper scripts, which arrive in S1.
* **Rationale & Recommendation**: Use a persistent canonical path such as `~/.cpm-s0/`. Add a minimal signed probe app and throwaway wrapper stubs to S0, or move those checks to a named early point in S1 and stop describing them as running before building.

### `codebase-details-1-1` The registry schema has nowhere to store the default - Severity: Blocker
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** `Lines 1026-1042, 1101-1102` (spec `278, 394-397`)
* **Discrepancy Description**: `Registry` holds only `schemaVersion` and `profiles`, and `Profile` has no default flag. But `profiles.json` is the source of truth and `default` is regenerated from it after every change. As specified, this is impossible, so the implementer must invent a schema.
* **Rationale & Recommendation**: Add `defaultProfileId: String` to `Registry` and to the field list at lines 394-397. Define how reconcile behaves when the `default` file disagrees or is missing.

### `codebase-details-1-2` S1 does not build, and its files depend on later stages - Severity: Blocker
* **Reviewer Lens:** `egm-codebase-details`; also `egm-implementation-readiness`, `egm-style`
* **Affected Scope:** `Lines 961-968, 1015-1019, 1138-1158, 1315-1318, 1347`
* **Discrepancy Description**:
  * File 5 (S1) declares `ProfileUI` and `ClaudeProfiles`, whose sources arrive in S3. SwiftPM rejects targets with no sources (unverified).
  * File 31 (S1) includes `list` and `stop`, which need files 21 and 23 (S2).
  * File 15 (S1) needs the transcript parse count (file 18) and the binary path (file 23).
  * File 14 (S1) needs `ClaudeBinaryLocator` (file 23) and a session-record reader (file 20).
  * `cpm install-shell` (S1) installs from bundle resources, but `make-app.sh` is S3.
* **Rationale & Recommendation**: Move 18, 20 and 23 to S1, or state which checks and subcommands are stubbed per stage. Add per-stage `Package.swift` targets or placeholder sources. Define an S1 install path from `shell/`, or move a minimal `make-app.sh` into S1.

### `codebase-details-1-3` Flow 3 has no owner - Severity: Blocker
* **Reviewer Lens:** `egm-codebase-details`; also `egm-implementation-readiness` (rated Major)
* **Affected Scope:** `Lines 1090-1092, 1257-1259, 1288-1290` (spec `482-495`)
* **Discrepancy Description**: No module owns flow 3 steps 3-8: the Keychain baseline, the watch for `oauthAccount`, the 10-second retry, the zero-or-several-names error, Keep or Sign out, and timeout with Retry or Discard. AppModel has "no logic of its own", file 28 is views only, and ProfileStore offers only `create` and `completeLogin`. Further gaps:
  * `completeLogin` has no return for the duplicate-account result tested at line 1375.
  * The timeout length is not given.
  * No mechanism detects a closed Terminal window without Apple Events.
  * Launcher has no parameter for the browser note.
  * Retrying can reuse the same Keychain name, which is then not "new" against the old baseline, so a valid login becomes `error`.
* **Rationale & Recommendation**: Add a named ProfileCore module such as `ProfileLoginFlow` or `LoginCoordinator`, with explicit states, a timeout value, a rule for retaking the baseline on retry, and tests.

### `codebase-details-1-4` No named live-session query that bypasses the display filter - Severity: Blocker
* **Reviewer Lens:** `egm-codebase-details`; also `egm-implementation-readiness` (rated Major), `egm-style` (rated Major)
* **Affected Scope:** `Lines 1205-1221, 1265, 1439-1441, 1488-1489` (spec `247-249, 560, 600, 608, 892`)
* **Discrepancy Description**: Terms says the desktop and Cowork filter never applies to safety checks. `SessionIndex.sessions(for:)` applies that filter, and its `live` field is what a caller would naturally use. File 44's "no live session" and file 49's blocked reason name no source. File 20 can read records with no filter (synthesizer correction), but no per-profile safety query exists. No test covers a hidden live record blocking a write. The state also has three names: `isRunning`, `live` and "running count". Severity is contested, and the Blocker rating stands because a wrong choice lets a copy, Restore or removal run over a live Cowork session.
* **Rationale & Recommendation**: Add an unfiltered `liveRecords(for:)` and `hasLiveSession(profile)`. Require them in copy, Restore, removal and uninstall. Add tests that a live desktop or Cowork record blocks each. Use one word per concept.

---

## Major

### `implementation-readiness-1-5` S0 has no revert, no defined pass conditions and no durable output - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 979, 984-988, 1007-1008, 1138, 1208, 1218-1219, 1360, 1442, 1476-1477`
* **Discrepancy Description**: No step reverts check 12's `launchctl setenv` or removes test logins and Keychain items. File 2 takes "the pass condition from the assumptions table", but that table has no pass-condition column. Later files defer to S0 values (tolerance, `entrypoint` values, subcommands, MCP path, fixture shapes), yet results go to the git-ignored `s0-results/`. The decision to continue has no artifact.
* **Rationale & Recommendation**: Add a committed, email-free `docs/S0-results.md` with named constants and fallback decisions, per-check pass conditions, and teardown steps. Make later items cite those fields.

### `implementation-readiness-1-6` The plan has no branch for S0 fallbacks - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`
* **Affected Scope:** `Lines 1096, 1151-1153, 1209, 1215-1217, 1523-1539, 1549-1554` (spec `546-548, 703, 491`)
* **Discrepancy Description**: If check 12 fails, the specification drops the LaunchAgent and `apply-default-env`, but files 10, 15, 31, 53, 54, 55 and flow 10 remain unconditional. If check 15 falls back, "may be stale" has no `SessionRow` field or UI. If check 7 fails, the table says skip cleanup and warn, while flow 3 sets `error` on every login.
* **Rationale & Recommendation**: Give each fallback its exact file changes, or a configuration switch.

### `implementation-readiness-1-7` File 16 says "follow flow 1 exactly" but its recap differs - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1163-1173, 1390-1404` (spec `412-438`)
* **Discrepancy Description**: The "in that order" list omits the override branch, the live-session refusal, `CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1` and the `launch.log` append. Tests omit the title variable, the two extra login variables, `-r <sid>`, a value inside `~/.claude-profiles/`, and the launch-log line. The stub at line 1391 does not print the extra variables, so it cannot assert them.
* **Rationale & Recommendation**: Delete the recap and keep "implement flow 1", plus only details the flow lacks. Add the missing test cases and extend the stub.

### `implementation-readiness-1-8` The override path and wrapper parsing are undefined - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`
* **Affected Scope:** `Lines 1163-1169, 1129-1131, 1401-1402` (spec `407, 412-413, 425, 427, 432-434`)
* **Discrepancy Description**: The override jump skips profile selection, yet later steps use `id` for `CONFIG_DIR`, `CLAUDE_PROFILE_ID` and the title. Whether the resume checks apply during an override is not stated. With "no `jq`", nothing says how zsh extracts `sessionId` and `pid` from `sessions/*.json`, how it gets `<folder>` for the refusal text, or where the `version` value logged by `launch` comes from.
* **Rationale & Recommendation**: Give exact override semantics and a defined extraction method per field, with fixture tests.

### `implementation-readiness-1-9` The Swift launch path drops required environment and behaviour - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1064-1066, 1070-1071, 1231-1232, 1369, 1418-1421` (spec `462-468, 79-80`)
* **Discrepancy Description**: `LaunchEnv` is never defined. It and the `.command` contents omit `CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1` and the login-variable warnings. `cd '<folder>'` has no failure branch, though flow 2 must print a message and stop. File 42 tests none of these. Without the title variable, Claude Code may overwrite the profile title (check 19).
* **Rationale & Recommendation**: Define `LaunchEnv`, give the exact `.command` template, and assert every line in file 42.

### `implementation-readiness-1-10` Multi-file and two-process writes can stop `claude` - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** `Lines 1101-1102` (spec `321, 416-417`)
* **Discrepancy Description**: The app and `cpm` both run ProfileStore with no lock and no reload before writing, so a stale app can overwrite a `cpm` change. The three files have no write order. A wrapper that sees `default` pointing at an ID not yet in `names`, or just removed from it, exits 1.
* **Rationale & Recommendation**: Hold a `flock` during read-modify-write and reload first. Add order: when adding, `names` before `default`; when removing, `default` first and the `names` entry later. State how reconcile regenerates the files after a crash.

### `implementation-readiness-1-11` Built-in row values, wrapper visibility by state and name validation are undefined - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1027-1030, 1044-1045, 1087, 1178-1179` (spec `262-264, 375, 654, 874`)
* **Discrepancy Description**: `commandName` is required, unique and "not `builtin`", but the built-in row's command name, display name and colour are never given. The plan does not say whether `pendingLogin`, `loggedOut`, `error` or `deleting` rows appear in `names`, so `claude-as` could start in a folder being trashed. File 6 validates only `commandName`, restating "no spaces" instead of the regex. `displayName` and `color` have no validation, though both reach shell-read files, terminal escapes and `.command` files. Line 1179 also drops "control characters".
* **Rationale & Recommendation**: Give literal seed values and a state-to-`names` table. Add validators for all three fields, with hostile-value tests. Refer to "Name rules in Terms" rather than restating them.

### `implementation-readiness-1-12` Shell installation lacks an owner, exact text and resource sources - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`
* **Affected Scope:** `Lines 1181, 1317, 1347-1348, 1505-1508, 1537-1539` (spec `650-663`)
* **Discrepancy Description**: "Set up shell" and `cpm install-shell` share no ProfileCore module, and no UI file has the item. `make-app.sh` bundles `cpm` and `shell/` but not the LaunchAgent plist. No rule says how a copied `cpm` finds bundle resources. Also unspecified:
  * The exact `~/.zshenv` and `~/.zshrc` lines.
  * The plist placeholder and the `launchctl` load command.
  * The upgrade version marker.
  * The rule that rerunning does not reset `default`.
  * Signing of `cpm`, which calls KeychainProbe, while check 21 covers only the app.
* **Rationale & Recommendation**: Add `ShellInstaller`, a manifest with a version file, literal line text and idempotency tests. State how `cpm` is signed and whether check 21 covers it.

### `implementation-readiness-1-13` `apply-default-env` has no failure semantics - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** `Lines 1526-1531, 1536-1539, 1554`
* **Discrepancy Description**: Not defined: behaviour when `default` or `names` is broken at login (leave stale, unset or fail), where LaunchAgent stdout and stderr go, what happens when `setDefault` runs before `bin/` exists, and whether `setDefault` throws after `default` has been written.
* **Rationale & Recommendation**: Specify each case and its exit code.

### `implementation-readiness-1-14` Tests have no isolation from the real machine - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** `Lines 1060-1068, 1371-1377, 1388-1404, 1551`
* **Discrepancy Description**: `ConfigLocator` is static and fixed to `~/.claude-profiles`, with no injected root. ProfileStore tests could overwrite the real registry and `default`. Doctor and `launchctl` tests could touch the real login-wide setting. Shell tests never say to set a temporary `HOME`.
* **Rationale & Recommendation**: Inject the root and home directory. Require a temporary `HOME` in shell tests. Add a guard that aborts if tests resolve to the real paths.

### `implementation-readiness-1-15` Copy plan data cannot express flow 6's rules - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`
* **Affected Scope:** `Lines 1430-1433, 1438-1443` (spec `563-592`)
* **Discrepancy Description**: `CopyItem` has no before-state hash, conflict resolution (skip, overwrite, rename), rename rule, per-variable or per-secret tick, "points into source folder" flag, masked value or removal command. `selection` and the value types are undefined. `apply` returns only `BackupID`, so it cannot report which items finished after a failure. The plan also leaves undefined:
  * How install-command steps are ordered with file writes.
  * Skills' copy unit.
  * The rule for omitting unticked secrets (drop the variable or skip the server).
  * Eligible target and source states.
* **Rationale & Recommendation**: Give complete types, an `ApplyResult` and the omission rule.

### `implementation-readiness-1-16` Restore and backup logic has contradictions and gaps - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`
* **Affected Scope:** `Lines 1052, 1441-1443, 1467-1469` (spec `383-384, 563-565, 600-604`)
* **Discrepancy Description**: `copyForBackup` "keeps bytes and mode", but backup files must be 0600, and the manifest has no original mode, so Restore cannot restore it. When `claude mcp add-json` or a plugin install changes `.claude.json`, the plan does not say whether that file is in the manifest. If it is, Restore undoes changes the specification says it cannot undo. If it is not, that breaks "back up every file the plan touches". After-state capture timing is undefined, and no data path carries the removal command to the UI.
* **Rationale & Recommendation**: Store 0600 copies with the original mode in the manifest, and extend the restore test to mode. Give an explicit rule for each write type.

### `implementation-readiness-1-17` JSON rewrite and settings merge are under-specified - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`
* **Affected Scope:** `Lines 1458-1462` (spec `572, 577-584, 866-869`)
* **Discrepancy Description**: Nothing says how to change only `mcpServers` while preserving all other content, key order and number fidelity, or how whole-document parsing fits the NFR's "parse only…". The merger omits `extraKnownMarketplaces`, `model` and per-variable `env`. Hook duplicate handling is undefined.
* **Rationale & Recommendation**: Choose one JSON strategy, use a table-driven merge per flow 6 key, and add a golden test.

### `implementation-readiness-1-18` SecretDetector cannot be implemented consistently - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1448-1453` (spec `584, 588-590`)
* **Discrepancy Description**: "Long base64/hex" and "known token shape" have no thresholds or list. The key regex matches words such as `author` and `monkey`. Scanning is undefined for positional `args` and for free text in skills and `CLAUDE.md`, where no key exists, as is masking and ticking there. Settings `env` is missing from the Applies-to list. There is no test file for this security control. Doctor's use appears only here and not in file 15, and an S1 file sits under the S4 heading.
* **Rationale & Recommendation**: Give exact regexes and minimum lengths, a line-level match model, and a positive and negative test corpus. State the use in file 15.

### `implementation-readiness-1-19` CompatCheck is non-actionable and drops a specification duty - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`
* **Affected Scope:** `Lines 1138-1143` (spec `339-341, 622-626`)
* **Discrepancy Description**: "Each format the app reads", "unexpected shape", and the source of each sample are undefined, so `canWrite` is left to the implementer. The profile environment used for `claude --version` is unstated. The Keychain-name check the specification assigns here is missing.
* **Rationale & Recommendation**: Define a format enum with required keys and types per format, and add the Keychain-name probe or assign it clearly to Doctor.

### `implementation-readiness-1-20` Doctor differs from flow 11 - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1148-1158, 1193, 1318, 1452, 1545` (spec `276-277, 667-669, 681, 686`)
* **Discrepancy Description**:
  * It does not say environment checks run in a login shell.
  * It has no health-line output and omits folders without rows.
  * It states the folder check twice, and "`configDir` is unchanged" has no baseline.
  * It adds a plugin-registry check not in flow 11.
  * `messagingSocketPath` appears only here and is unexplained.
  * The parse-failure count has no data path, because `sessions(for:)` returns only rows.
  * ProcessRunner's user list omits Doctor, though `launchctl getenv` must run through it.
  * Exit-code mapping for notes is undefined.
* **Rationale & Recommendation**: Use one table of check, level, source, fix text and exit effect, aligned with flow 11. Define a `SessionIndexResult` that includes failure counts.

### `implementation-readiness-1-21` S3 UI and action coverage is missing - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`
* **Affected Scope:** `Lines 1228-1229, 1255-1259, 1264-1269, 1275-1277, 1288-1292` (spec `279, 474-475`)
* **Discrepancy Description**: "With Resume and Stop" (1267) conflicts with Stop instead of Resume for a running session (474). File 22 has no running-session guard. "Choose folder" has no action or view, and `resume` has no folder parameter. No rule gives the folder for "New Terminal" or the login window, or says what happens when `cwd` is absent. Not assigned to any file: Sign in, Retry and Discard, the panel's profile filter (CUJ 3), filter fields, the account-change notice, and Copy, Remove and Set up shell actions in AppModel. "Every panel refresh" against "every 2 seconds" (1255) is ambiguous, and it matters for re-parsing `.claude.json`.
* **Rationale & Recommendation**: Define the action list and folder rules, for example `resume(session:, folderOverride:)` and a fixed default folder. Fix the refresh rule.

### `implementation-readiness-1-22` `.command` cleanup and binary lookup leave behaviour open - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-style`, `egm-codebase-details`
* **Affected Scope:** `Lines 1237-1238, 1243-1244, 1421, 1544` (spec `473, 475-476`)
* **Discrepancy Description**: "Older than one launch" is not a unit of age or testable, and two quick launches could delete a file before Terminal reads it. `/bin/zsh -lic` runs user startup files, which may print text or hang, yet no output parsing or timeout is stated, and ProcessRunner has no timeout or output contract.
* **Rationale & Recommendation**: Use an age rule such as "older than 60 seconds". Specify a delimiter or last-line parse, and a timeout.

### `implementation-readiness-1-23` Test coverage does not meet the plan's own bar - Severity: Major
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1019, 1491-1498, 1565-1568` (spec `915-918`)
* **Discrepancy Description**: "Every module has tests for each rule" cannot be verified, because no rule list exists. No cases are listed for AccountReader, StatusLineWriter, EventLog, CompatCheck, FileWatcher, ClaudeBinaryLocator, TranscriptReader boundaries, ProcessInfoProvider, SecretDetector, SettingsMerger, BackupStore, ClaudeCLI, ProcessRunner, `cpm` or `profile-label`. File 50 omits hash-mismatch stops, mid-copy failure, Restore leaving changed files alone, Restore refusal, built-in target refusal, source-folder flagging, and `apiKeyHelper` and `oauthAccount` exclusion. S2's required measurement (5,000 transcripts, under 1 second) has no owner or threshold action, and the fixed 64 KB read (1192) has no fallback if the title sits deeper.
* **Rationale & Recommendation**: Replace the sentence with a rule-to-test list, or name exempt modules. Add per-module cases, a performance script and per-stage exit gates.

### `codebase-details-1-5` `claude mcp add-json` scope is unspecified, and the default writes elsewhere - Severity: Major
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** `Lines 1474-1477, 1442` (spec `579`, S0 check 8 at `704`)
* **Discrepancy Description**: The 2.1.296 binary's help text declares `-s, --scope <scope>` with default `local` for `add-json`. Flow 6 says the item lives in top-level `mcpServers`, which corresponds to user scope, and `~/.claude.json` has `mcpServers` at the top level and nested. A default-scope install would not land where the preview promises and would depend on the working directory.
* **Rationale & Recommendation**: Fix the exact command, including `--scope user`, the working folder, and a before-and-after check, in files 4 and 48, and make check 8 assert it. State that project-nested `mcpServers` are not copied.

### `codebase-details-1-6` ProfileStore does not cover the state machine - Severity: Major
* **Reviewer Lens:** `egm-codebase-details`; also `egm-implementation-readiness`
* **Affected Scope:** `Lines 1086-1099` (spec `270-280`)
* **Discrepancy Description**: Missing: Discard for `pendingLogin`, Retry, transitions `ready↔loggedOut` and into `error`, writing an updated cached account with the login-changed notice, resuming `deleting` after a crash, and reporting a folder with no row.
* **Rationale & Recommendation**: Add these methods with signatures and tests.

### `codebase-details-1-7` E2E covers CUJs 1-6 of 8 and points to the wrong file - Severity: Major
* **Reviewer Lens:** `egm-codebase-details`; also `egm-style`, `egm-implementation-readiness`
* **Affected Scope:** `Lines 1569-1572, 984-986` (spec `141-220`)
* **Discrepancy Description**: CUJ 7 (stop a busy session) and CUJ 8 (remove the default profile) are unverified. E2E is "scripted in `docs/S0-checklist.md`", but file 2 defines that file as S0 checks only. "Manual" and "scripted" also read as opposites.
* **Rationale & Recommendation**: Cover CUJs 1-8, and either expand file 2's scope or use a separate E2E checklist. Write "step-by-step" instead of "scripted".

### `codebase-details-1-8` The join between live records and transcripts is undefined - Severity: Major
* **Reviewer Lens:** `egm-codebase-details`; also `egm-implementation-readiness`
* **Affected Scope:** `Lines 1205-1206, 1215-1219` (spec `712`, CUJ 2)
* **Discrepancy Description**: A new session may have a live record before any transcript exists, so CUJ 2's 5-second goal depends on an outer join, but file 21 only says "Joins". `SessionRow` has no `entrypoint`, pid or `procStart`. `live`'s type and the precedence between record `name` and transcript `title` are undefined. The check 16 fallback ("hide nothing") is not stated.
* **Rationale & Recommendation**: State outer-join rules and field precedence, include the fields stop and filtering need, and add tests. Check that a live session's ID always matches its transcript name (unverified).

### `style-1-1` `ANTHROPIC_API_KEY` is used where the specification means all login variables - Severity: Major
* **Reviewer Lens:** `egm-style`; also `egm-implementation-readiness`, `egm-codebase-details`
* **Affected Scope:** `Lines 1066, 1154, 1166, 1391, 1396` (spec `430-431, 466, 681`, CUJ 6 at `203`)
* **Discrepancy Description**: The specification covers `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN` and `CLAUDE_CODE_OAUTH_TOKEN`. Only file 37 (line 1369) uses the specification's term. A reader would implement and test one variable.
* **Rationale & Recommendation**: Use "login variables" throughout, and test all three.

### `style-1-2` Absolute test claims conflict with flow 6 - Severity: Major
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1495, 1497, 1498` (spec `555-557, 600-604`)
* **Discrepancy Description**: "`apply` writes exactly the planned result" and "plugin registries … never touched" conflict with the statement that install commands may write caches and registries the plan does not list. "Restore gives byte-identical files" omits the limits for files changed since the copy and for install commands.
* **Rationale & Recommendation**: Qualify these as "direct file writes", "never written by SetupCopier itself", and "restore of files unchanged since the copy".

### `style-1-3` TerminalOpener's description contradicts itself and refers to an absent command - Severity: Major
* **Reviewer Lens:** `egm-style`; also `egm-implementation-readiness` (Nit), `egm-codebase-details` (Nit)
* **Affected Scope:** `Lines 1017, 1236-1237, 1299, 1324-1325, 1416`
* **Discrepancy Description**: Line 1236 places the AppKit implementation in `ProfileUI`, while lines 1299 and 1325 place it in file 29 (`ClaudeProfiles`), which has no direct `ProfileCore` dependency. Lines 1237 and 1324-1325 say "the CLI" uses `open -a Terminal`, but no `cpm` subcommand opens a session. Line 1325 says tests use it, while line 1416 uses a fake.
* **Rationale & Recommendation**: Pick one location. State which `cpm` command launches a session, or limit this implementation to tests.

### `style-1-4` "The one place that runs Claude Code or `launchctl`" is contradicted - Severity: Major
* **Reviewer Lens:** `egm-style`; also `egm-codebase-details`
* **Affected Scope:** `Lines 1526-1529, 1544-1547, 1551` (also `328`, outside the range)
* **Discrepancy Description**: File 53's zsh script runs `launchctl`, and DefaultEnvApplier runs that script. File 15 needs `launchctl getenv` but is not a listed user. File 55's stub on `PATH` differs from "Tests use a fake" without saying where each applies.
* **Rationale & Recommendation**: Reword to "the one Swift API for running external commands", list every caller including Doctor, and state which test uses which double.

---

## Nits

### `implementation-readiness-1-24` Process start time and liveness details - Severity: Nit
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`
* **Affected Scope:** `Lines 1003-1005, 1198-1199`
* **Discrepancy Description**: S0 compares `procStart` to `ps lstart` (one-second local time), while production uses sysctl `p_starttime`, so the tolerance is measured against a different source. `kill(pid, 0)` returning `EPERM` must count as alive. `p_starttime` is a C macro Swift may not import (unverified).
* **Rationale & Recommendation**: Use the same source in S0. Fix this despite the Nit rating, because it guards signal delivery. State the `EPERM` rule and the exact field.

### `implementation-readiness-1-25` S0 backup details - Severity: Nit
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** `Lines 993-997`
* **Discrepancy Description**: The plan does not set the destination to 0700, name the Keychain-list file, give a restore procedure, or say whether Claude Code must be idle. Copying `sessions/*.key` sits awkwardly with NFR 866-867.
* **Rationale & Recommendation**: Specify these and state the NFR exception explicitly.

### `implementation-readiness-1-26` `launch.log` details are undefined - Severity: Nit
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`
* **Affected Scope:** `Lines 1129-1133` (spec `437`)
* **Discrepancy Description**: The wrapper's log format, escaping of tabs and newlines in folder names, interaction with rotation, `logs/` creation, and "logging failure must not block launch" are undefined. `EventLog.write` has no field for a stop result.
* **Rationale & Recommendation**: Define the format and failure rule.

### `implementation-readiness-1-27` `setCommandName` goes beyond the specification - Severity: Nit
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1094, 1291` (spec `62-63`)
* **Discrepancy Description**: The goal says rename is "display name only", and the "warns caller" contract and text are undefined.
* **Rationale & Recommendation**: Confirm or drop it. If kept, state the warning text.

### `implementation-readiness-1-28` Status-line script details - Severity: Nit
* **Reviewer Lens:** `egm-implementation-readiness`; also `egm-style`, `egm-codebase-details`
* **Affected Scope:** `Lines 1122, 1178-1180, 1562-1564`
* **Discrepancy Description**: The status-line command uses `~`, and shell expansion there is unverified. `profile-label` has no behaviour for an unknown ID, and its ANSI form is not stated. `zsh -n` omits `shell/profile-label`, and `scripts/*.sh` have no syntax check.
* **Rationale & Recommendation**: Use an absolute path unless expansion is verified. Define these behaviours and extend the static check.

### `implementation-readiness-1-29` `cpm` output is unspecified - Severity: Nit
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** `Lines 1315-1318`
* **Discrepancy Description**: `list` output, `names` output, non-TTY `stop` without `--yes`, and `--profile` lookup rules are unspecified, though `cpm` is the S1 user interface.
* **Rationale & Recommendation**: Specify each.

### `codebase-details-1-9` The first-user-message title fallback goes beyond the specification - Severity: Nit
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** `Line 1190` (spec `503-504`)
* **Discrepancy Description**: The fallback can surface pasted secrets or injected command text in the menu.
* **Rationale & Recommendation**: Keep it only with an explicit decision and a rule for skipping `isMeta` entries.

### `codebase-details-1-10` Toolchain is unstated - Severity: Nit
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** `Lines 1015, 1505, 1538`
* **Discrepancy Description**: `swift-tools-version`, the test framework, the Xcode or Command Line Tools requirement and the `launchctl` load method are unstated.
* **Rationale & Recommendation**: State each.

### `codebase-details-1-11` Icon tinting and icon generation - Severity: Nit
* **Reviewer Lens:** `egm-codebase-details`; also `egm-implementation-readiness`, `egm-style`
* **Affected Scope:** `Lines 1298, 1337-1338`
* **Discrepancy Description**: `MenuBarExtra` labels may render as template images and ignore tint (unverified), which would break CUJ 4. Line 1337 names no generation method or `CFBundleIconFile`.
* **Rationale & Recommendation**: Specify a non-template rendered image, add it to S0, and describe how the icon is generated.

### `style-1-5` Stale drafting references - Severity: Nit
* **Reviewer Lens:** `egm-style`; also `egm-codebase-details`, `egm-implementation-readiness`
* **Affected Scope:** `Lines 953-955, 1514-1519`
* **Discrepancy Description**: "This draft", `design_draft.md` and "at the end of drafting" are stale. codebase-details found the files already in `docs/designs/`, no `design_draft.md`, and no `.git`, while line 953 says the repository root holds them. No step says `git init`. File 1 does not address review reports in that folder.
* **Rationale & Recommendation**: Remove file 52 or reword it as a repository-layout statement. Add a `git init` step.

### `style-1-6` Stage table gaps - Severity: Nit
* **Reviewer Lens:** `egm-style`; also `egm-codebase-details`, `egm-implementation-readiness`
* **Affected Scope:** `Lines 961-969`
* **Discrepancy Description**: Files 51 and 52 are in no stage. Line 967 omits `permissions`, `model`, `env` and `extraKnownMarketplaces`. It also places hooks in step 2, while flow 6 (line 597) places only plugins there. "Their checks" at line 969 is undefined.
* **Rationale & Recommendation**: Correct the table, and name the checks or describe them.

### `style-1-7` Removal vocabulary - Severity: Nit
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1101-1104, 1112, 1530, 1553`
* **Discrepancy Description**: Terms and flow 7 say "remove", while files 10, 11 and 55 say "deleted", "delete flow" and "deleting the default". "Renamed to another folder" clashes with "display name only".
* **Rationale & Recommendation**: Reserve "delete" for the `deleting` state, the Keychain item and files. Use "moved" for folders.

### `style-1-8` Term drift for Keychain name and binary - Severity: Nit
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1114, 1151, 1232, 1383`
* **Discrepancy Description**: "Unsuffixed" differs from the specification's "plain `Claude Code-credentials`". "`claude` binary path" and "`<claude path>`" differ from "real binary".
* **Rationale & Recommendation**: Use one term for each.

### `style-1-9` Required UI wording lives behind an opaque code - Severity: Nit
* **Reviewer Lens:** `egm-style`; also `egm-implementation-readiness`, `egm-codebase-details`
* **Affected Scope:** `Line 1486`
* **Discrepancy Description**: "(R2-M4)" is opaque, and the wording is outside this document. codebase-details found it at `concepts_brief.md` line 144, in a section that also carries superseded rules.
* **Rationale & Recommendation**: Copy the wording into the plan.

### `style-1-10` Repetition in file 23 - Severity: Nit
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1243-1246`
* **Discrepancy Description**: "At every launch … with no cache across launches" says the same thing twice, and the signal sequence repeats flow 9.
* **Rationale & Recommendation**: Keep one phrase and "implements flow 9".

### `style-1-11` "Current SwiftUI" is vague - Severity: Nit
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1015-1016, 1252`
* **Discrepancy Description**: The stated reason may be wrong. `MenuBarExtra` and its window style date from macOS 13, while `@Observable` needs macOS 14 (unverified).
* **Rationale & Recommendation**: Check this, then name the API that sets the minimum.

### `style-1-12` "Used by every module that writes" overclaims - Severity: Nit
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1050-1053, 1130`
* **Discrepancy Description**: EventLog appends, which is not a temp-file rename.
* **Rationale & Recommendation**: Write "every module that replaces a file".

### `style-1-13` Terse or undefined phrases - Severity: Nit
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 985-988, 1094, 1208-1209, 1219, 1291, 1337, 1510, 1554`
* **Discrepancy Description**: These phrases need detail: "Explicit; warns caller", "(with warning)", "Leaves `default` correct", "A plain generated icon". "The S0 check", "found in S0" and "recorded in S0" name no check. The table has three names. The rerun trigger differs: "each Claude Code minor update" against "When Claude Code updates".
* **Rationale & Recommendation**: State the content, cite checks 15 and 16, use one table name, and use one trigger.

### `style-1-14` Structure and numbering - Severity: Nit
* **Reviewer Lens:** `egm-style`; also `egm-implementation-readiness`, `egm-codebase-details`
* **Affected Scope:** `Lines 973-975, 1160, 1175, 1285, 1350, 1379, 1414, 1491, 1523, 1541, 1549`
* **Discrepancy Description**: File 56 comes before 55. Stage and file headings are both H4. "Tests (S1–S4)" covers only S1 and S2 files. Several heading paths are not relative to the repository root. Shell files are placed inconsistently between `shell/bin/` and `shell/`.
* **Rationale & Recommendation**: Renumber, separate heading levels, retitle the test section, and use full paths.

### `style-1-15` Field-name issues - Severity: Nit
* **Reviewer Lens:** `egm-style`; also `egm-codebase-details`
* **Affected Scope:** `Lines 1037, 1040, 1077-1078, 1475-1476, 1546` (spec `394-397`)
* **Discrepancy Description**: `oauthAccount.displayName` collides with the profile's `displayName`, and no use for it is stated. `organizationName` and `lastError` are absent from the specification's field list. ClaudeCLI's logout method is disclosed only in file 56.
* **Rationale & Recommendation**: Qualify or drop the field, update the field list, and move logout into the ClaudeCLI method list.

### `style-1-16` Readability - Severity: Nit
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 985, 1102, 1233-1237, 1256, 1317, 1390-1404, 1418-1420, 1486, 1508`
* **Discrepancy Description**: Lines 1390-1404 are one run-on list with related cases apart. Lines 1233-1237 pack a rationale into one sentence. Lines 1418 and 1420 overlap. Several lines break the 80-column wrap.
* **Rationale & Recommendation**: Use one bullet per case, split the sentence, merge the overlap, and rewrap.

---

## Confirmed by `egm-codebase-details` (key names and structure only)

* `~/.claude/sessions/` holds `<pid>.json` plus `.key` files, so a `*.json` glob excludes keys.
* Session records have the keys `pid`, `procStart`, `sessionId`, `cwd`, `status`, `entrypoint`, `version`, `name`, `startedAt` and `messagingSocketPath`, plus `kind` and `updatedAt`, which file 20 does not decode.
* `~/.claude.json` has top-level `oauthAccount` and `mcpServers`, with `accountUuid`, `emailAddress`, `organizationName` and `displayName` present.
* `~/.claude/.claude.json` exists, so Doctor's stray-file note is relevant.
* `~/.claude/settings.json` has `model`, `enabledPlugins` and `extraKnownMarketplaces`, and no `statusLine`.
* Transcripts contain the key names `cwd`, `entrypoint`, `customTitle`, `aiTitle` and `summary`.
* `plugins/synced/` exists, and the plugin registries contain install-location keys.
* Binary 2.1.296 is installed and contains `CLAUDE_CODE_DISABLE_TERMINAL_TITLE`, `add-json`, and the `-credentials` and `Claude Code` strings.
* S0 check numbering agrees between line 984 and the table, with check 11 first and only checks 1-3 as stoppers.

## Not verified

No reviewer could run commands or open URLs, so these remain open:

* The Keychain hash algorithm and the Keychain item list.
* `SecItemCopyMatching` behaviour on `claude`-created items, and prompts.
* `security dump-keychain` behaviour.
* `launchctl setenv` inheritance and `path_helper` ordering.
* `/tmp` clearing and symlink behaviour on this Mac.
* `NSWorkspace` opening with no prompt.
* Whether the Terminal window stays open after `exec` exits. No S0 check covers this for CUJ 7.
* SwiftPM's empty-target behaviour, `MenuBarExtra` tinting and the minimum macOS version for its APIs, and Swift import of `p_starttime`.
* Plugin and marketplace command behaviour under `CLAUDE_CONFIG_DIR`.
* Signal behaviour, and how `procStart` compares with process start time.
* Actual `entrypoint` and `status` values, which were deliberately not read.
* Transcript position of titles, and sessions with no transcript.
* The claude.ai mock link.
* External open-source tools (S0 check 11).
