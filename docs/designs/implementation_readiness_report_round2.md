# Goldfish Review Report

**Target:** `docs/designs/claude-profile-manager.md` (`Lines 1022-1721`, "## Detailed Implementation Plan" to the end of the file; lines 1-1021 used as the specification)

**Verdict:** Not ready to build: 4 Blockers and 25 Majors each leave a design choice to the implementer or conflict with the specification or the toolchain, so fix them first.

## Reviewer Status

| Reviewer | Status |
| --- | --- |
| `egm-implementation-readiness` | completed |
| `egm-codebase-details` | completed |
| `egm-style` | completed |

## Severity Summary

| Severity | Consolidated findings |
| --- | --- |
| Blocker | 4 |
| Major | 25 |
| Nit | 31 |
| **Total** | **60** |

The reviewers raised 94 raw items, which merged into these 60.

| Reviewer | Raw Blocker | Raw Major | Raw Nit |
| --- | --- | --- | --- |
| `egm-implementation-readiness` | 2 | 19 | 10 |
| `egm-codebase-details` | 2 | 17 | 10 |
| `egm-style` | 0 | 10 | 24 |

`egm-implementation-readiness` stated 15 Majors in its summary, but its report lists 19, including 4 under "Other gaps, also Major". This report counts 19.

## How This Report Was Consolidated

* **Merged findings:** each ID belongs to one lead reviewer, and the other reviewers are listed under "Also raised by".
* **Severity conflicts:** where reviewers rated the same issue differently, the merged finding takes the highest rating. These cases are noted in the finding.
* **Decisions:** no reviewer reopened an item under "## Decisions".
* **Limits of the evidence:** reviewers could not run commands or open URLs, so runtime behaviour is untested, including `swift test`, signing, `launchctl`, zsh and FSEvents. Claims about tool behaviour are marked where they come from memory.
* **Credentials:** reviewers were told not to read credential values, Keychain contents or `.key` files. `egm-codebase-details` reports that it did not read them and checked local Claude Code files by key names and structure only.

**Checked directly by the synthesizer, by reading files:**
* the text of flow 2 (lines 494-506)
* the reconcile rule (lines 291-298)
* the Restore rule (lines 651-656)
* the S0 table (lines 755-778)
* the Keychain and watcher NFRs (lines 936-952)
* that the Command Line Tools tree has `Testing.framework`, and that its only `XCTest.swiftmodule` is under `MacOSX11.1.sdk`
* that no `Xcode*.app` exists directly under `/Applications`

## Prioritized Findings (Blockers -> Major -> Nits)

---

## Blockers

### Reconcile, login completion and the ProfileStore API do not fit together - Severity: Blocker
* **ID:** `implementation-readiness-2-1`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`
* **Affected Scope:** `Lines 1174-1201, 1249-1256, 1329-1336, 1677-1682, 1695-1699`; specification `291-298, 521-542`
* **Discrepancy Description**:
  * Terms says reconcile completes a `pendingLogin` row once `oauthAccount` appears, and file 11 owns reconcile. Completion needs AccountReader, KeychainProbe, the baseline diff and the 10-second retry, but `ProfileStore.init(locator:)` has none of them. LoginCoordinator sits above ProfileStore, so this is a dependency cycle.
  * File 65 has reconcile call `ProfileRemover.resumePending()`, and ProfileRemover calls `ProfileStore.removeRow`. This is a second cycle, and no S5 Modify entry changes file 11.
  * Flow 3 step 7 gives the user Keep or Sign out and retry, but any `cpm` run during the wait, even `cpm doctor`, could complete the login first. The plan does not say which wins.
  * ProfileStore is the only writer of `profiles.json`, yet it has no method to set or clear `keychainBaseline` or to mark a row `deleting`.
  * File 18 does not say where the detected service name is held between detection and `keep`.
* **Rationale & Recommendation**: The core write path cannot be coded without inventing its structure.
  * Add an orchestrator such as `Reconciler` above ProfileStore, LoginCoordinator and ProfileRemover, and keep ProfileStore to row and file operations.
  * Add `setKeychainBaseline`, `clearKeychainBaseline` and `markDeleting`.
  * State one rule for detected logins. For example, `cpm` completes automatically, and reconcile skips a row while the app's own wait is active, using a recorded owner and start time.
  * Add an S5 Modify entry for the file that hosts the reconcile-to-remover call.

### Later files depend on S0 data that S0 is never told to record - Severity: Blocker
* **ID:** `implementation-readiness-2-2`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details` (rated Major), `egm-style` (signature point only, rated Nit)
* **Affected Scope:** `Lines 1081-1086, 1258-1263, 1265-1272, 1345-1353`; specification `672-674, 1002-1005`
* **Discrepancy Description**:
  * Files 19 and 30 take key names from `docs/S0-results.md`, but file 3's list of recorded items has no key names, and no S0 check records them.
  * The required keys per CompatCheck format are stated nowhere. A valid `settings.json` has no key that is always present: a new profile's holds only `statusLine`, and the local built-in file has no `statusLine`. A wrong list would refuse every copy.
  * `canWrite(transcript)` has no meaning because transcripts are never written. `sample` is undefined, and the signature differs between the flow (`canWrite(format)`) and file 19 (`canWrite(format, sample)`).
  * No S0 check confirms the transcript keys that file 20 reads. Local inspection found `entrypoint` and the record types `custom-title` and `ai-title`, which reduces the risk, but the plan does not record this as a required S0 result.
* **Rationale & Recommendation**: This gate decides whether copy setup is refused, and the fixtures are built from the same missing data.
  * Add an S0 check that samples transcripts, and add "file-format key names" to file 3.
  * List the required keys per format in file 19, or name the S0 table that fills them.
  * Define `settings` as "top-level is a JSON object, and any listed key present has the expected type".
  * Limit `canWrite` to formats that are written, and name the sample file at each call site.

### The named test framework appears to be missing with Command Line Tools alone - Severity: Blocker
* **ID:** `codebase-details-2-1`
* **Reviewer Lens:** `egm-codebase-details`
* **Also raised by:** `egm-style` (rated Major), `egm-implementation-readiness` (rated Nit)
* **Affected Scope:** `Lines 1112-1114, 1434, 1705`; also line 1028, because every stage ends with its tests passing
* **Discrepancy Description**: File 6 says "XCTest; Command Line Tools are enough", and the README repeats this as a requirement. On this Mac, Command Line Tools provide `Testing.framework` under `Library/Developer/Frameworks`. The only `XCTest.swiftmodule` is in the old `MacOSX11.1.sdk`, and no `Xcode*.app` exists directly under `/Applications`. `import XCTest` will therefore probably fail in S1, and no S0 check would catch that early. The failure is inferred from the files present, because `swift test` could not be run.
* **Rationale & Recommendation**: Every stage's exit condition depends on `swift test`. Use Swift Testing, after confirming that the toolchain and `swift-tools-version: 5.10` support it, or list full Xcode as a requirement in files 6 and 38. Add a small `swift test` preflight to S0.

### The exact `.command` template can run a command from a hostile folder name - Severity: Blocker
* **ID:** `codebase-details-2-2`
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** `Lines 1465, 1491-1493`; specification `494-498, 505-506, 982`
* **Discrepancy Description**: Line 496 puts `<folder>` in double quotes: `print -u2 "Folder not found: <folder>"`. The quoting rule at line 505 only protects single-quoted text, and `$(...)` still expands inside double quotes. That branch runs when `cd` fails, and an invented transcript `cwd` such as `/x/$(cmd)` makes `cd` fail, so the hostile path reaches the one place where it executes. File 40 requires the file "exactly as in flow 2", while file 44 requires `$(...)` to stay inert, so the plan contradicts its own test. The risk table at line 982 names this threat. `print` without `-r` also interprets backslashes. This comes from zsh quoting rules and was not executed.
* **Rationale & Recommendation**: Transcript data can reach execution in a generated file. Assign the value once and never embed it again:
  ```zsh
  dir='<folder>'
  cd -- "$dir" || { print -ru2 -- "Folder not found: $dir"; exit 1 }
  ```
  Add a test with a hostile folder that does not exist, and assert that nothing ran.

---

## Majors

### The check 12 fallback misses files that depend on the login-wide setting - Severity: Major
* **ID:** `implementation-readiness-2-3`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1054, 1323-1327, 1338-1343, 1582-1589, 1683-1684`; specification `594-596, 768`
* **Discrepancy Description**: The row removes files 25, 26 and 36 and the doctor rows. It omits file 27 (writes and bootstraps the LaunchAgent), files 29 and 56 (copy the plist), file 65 (`launchctl unsetenv` and unload), file 38 and the related tests. The trigger is also inconsistent: flow 5 drops the setting only if the desktop app or Cowork is harmed, while the S0 table drops it if any of three conditions fails, including VS Code not following the default.
* **Rationale & Recommendation**: List every affected file. Choose one trigger, for example harm to the desktop app or Cowork means drop, and a VS Code failure means keep and record it.

### Several S0 fallbacks leave a choice open, and absent rows have no stated meaning - Severity: Major
* **ID:** `implementation-readiness-2-4`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1043-1057`; specification `755-778`
* **Discrepancy Description**:
  * Check 5 ("Remove Resume ... or limit it") and check 9 ("Accept one prompt, or use AppleScript") leave an open choice with no rule.
  * Check 5 omits files 42, 44, 51 and 52 and the `folderOverride` and "Choose folder" paths, and mixes file numbers with a command name.
  * Check 6 covers logout only, and if `auth login` itself fails, flow 3 has no path.
  * Check 15 names neither file 21 nor file 39, gives no text for the "extra confirmation" (which the S0 table at line 771 does not have), and does not say how the fallback is switched on. The test "reused PID not live" would no longer hold.
  * Checks 4, 10, 13, 14, 16, 17, 18, 20 and 22 have no row, and the text does not say whether that means no change or that the S0 column applies. Check 14 names no destination, check 10 does not cover files 13 and 24, check 18 has no replacement signals, and check 22 implies a wrapper change.
  * The check 7 row says the flow "already handles this", which is not a change to the plan.
* **Rationale & Recommendation**: Pick one outcome per check, or name the test result that selects it. State that this table lists file-level effects and that the S0 table is authoritative, and say what an absent row means. Add rows for checks 10, 14, 18 and 22, align check 15 with the S0 table, and delete the check 7 row.

### Checks 10 and 14 have no run step, and check 21 runs too late - Severity: Major
* **ID:** `implementation-readiness-2-5`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`, `egm-style` (placement wording)
* **Affected Scope:** `Lines 1107-1438, 1589`; specification `751-753, 770, 777`
* **Discrepancy Description**: Only file 56 has a "run the checks here" instruction, for checks 9 and 21, and it sits inside a build script's description. Nothing tells the implementer to run checks 10 and 14 in S1, although S1 is the first stage that depends on them. Check 14 is labelled S1 but partly needs the `.command` file and the app, which arrive in S2 and S3. Check 21 covers name listing without a prompt, and `cpm add` and `doctor` rely on that from S1, but the check runs in S3. Its fallback covers deletion only, not a failed or prompting list.
* **Rationale & Recommendation**: Add an explicit S1 exit step for checks 10 and 14, and split check 14 by what each stage can test. Run the `cpm` half of check 21 in S1, and add a listing-failure fallback. Move these instructions to stage-level lines.

### The sign-in path for a `loggedOut` profile is not designed - Severity: Major
* **ID:** `implementation-readiness-2-6`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`
* **Affected Scope:** `Lines 1188-1189, 1249-1256, 1329-1336, 1530-1533`; specification `278-279, 291-298`
* **Discrepancy Description**: File 49 lists a "sign in" action "calling ProfileCore", but no such method exists. `begin` assumes a new login and does not say whether `keychainService` is kept or re-detected. `markLoggedOut`, `markReady` and `updateAccount` have no named caller, and `updateAccount` cannot express "account gone". AppModel keeps only filtering and sorting, so the logic that compares accounts and changes state has no home. Reconcile covers only `pendingLogin`, so logging out or back in through `claude-as` leaves the state stale.
* **Rationale & Recommendation**: Add `LoginCoordinator.signIn(profile)` with defined Keychain behaviour, and one `refreshAccount(profile)` that performs all state transitions. Run it from reconcile as well as the app.

### Retrying a login conflicts with the Keychain baseline rule - Severity: Major
* **ID:** `implementation-readiness-2-7`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`
* **Affected Scope:** `Lines 1254, 1381-1385`; specification `528-538, 762`
* **Discrepancy Description**: File 34 requires "retry reuses the same name without error". The same folder hashes to the same Keychain name. If logout does not delete the item (check 6 only records this), the new baseline already contains it and the diff finds zero new names. Flow 3 turns that into no recorded name plus a warning, and removal later leaves the item behind.
* **Rationale & Recommendation**: State the rule for each logout result. Keep the name detected on the earlier attempt, treat it as valid on retry if it still exists, and remove it from the new baseline when the sign-out is the app's own.

### `cpm` cannot finish, retry or discard a login, and prints the wrong command - Severity: Major
* **ID:** `implementation-readiness-2-8`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1122-1135, 1253-1255, 1332-1336, 1477-1479, 1698-1699`; specification `521-522, 530, 533, 539-541`
* **Discrepancy Description**:
  * File 28 prints `claude-as <commandName>`, while flow 3 and file 40 use `claude-as <commandName> auth login`.
  * S1 ends with `cpm` as the only interface, but it has no keep, retry, discard or `loggedOut` sign-in command, and the duplicate-account warning has no decision point. A wrong or abandoned login cannot be fixed until S3.
  * The 10-minute timeout has no stored start time, so `cpm` polling once per run cannot compute it, and the app cannot compute it after a restart. `createdAt` would not reset on Retry.
  * The plan does not say what `cpm add` does when the shell layer is missing, in which case the printed command does not exist.
* **Rationale & Recommendation**: Correct the printed command. Define the CLI behaviour, for example keep automatically and print the warning, and add `cpm login <name>` and `cpm discard <name>`. Add `loginStartedAt`, and make `cpm add` refuse without the shell layer.

### `cpm doctor` can never report a broken `default`, and `--fix` adds nothing - Severity: Major
* **ID:** `implementation-readiness-2-9`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-style` (rated Nit)
* **Affected Scope:** `Lines 1289, 1335`; specification `291-296, 733`
* **Discrepancy Description**: Every `cpm` run starts with reconcile, which rewrites a missing or wrong `default` and `names`, so `cpm doctor` repairs the fault before checking it. The flow 11 row "`default` resolves to a profile" can never fire from `cpm`, and `--fix` ("runs reconcile") has nothing left to do. File 34's test passes only because it injects faults directly into Doctor. Reconcile also does not say whether it re-runs `apply-default-env` after a rewrite, and the fix for a `launchctl getenv` mismatch is not named.
* **Rationale & Recommendation**: Make `cpm doctor` run a read-only reconcile unless `--fix` is passed. State that `--fix` also runs `apply-default-env`, and write the fix text for each fault.

### Safety checks do not define an unreadable session record, and `discard` is unguarded - Severity: Major
* **ID:** `implementation-readiness-2-10`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** `Lines 1187, 1279-1282`; specification `254-256, 552`
* **Discrepancy Description**: `hasLiveSession` guards copy, Restore, removal and uninstall. The plan does not say what happens when a record cannot be parsed or has no `procStart`. If readers follow the general skip rule, a Claude Code format change makes the check report no live session, and removal would log out and Trash a running profile. `discard(id)` moves a folder to the Trash but is not among file 21's required callers, even though `names` holds `pendingLogin` rows and `claude-as` can start a session in one.
* **Rationale & Recommendation**: Fail closed: an unreadable record blocks the action and names the file. Add `discard` to the required callers, and add tests for both.

### The session join assumes one live record per session - Severity: Major
* **ID:** `implementation-readiness-2-11`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** `Lines 1445-1452, 1472`; specification `467-468, 778`
* **Discrepancy Description**: `SessionRow` holds one `pid` and `procStart` and joins on `sessionId`, but live records are per process, and check 22 exists because one session may be resumed twice. The plan does not say what the row shows, which process Stop signals, or which record wins when a dead one and a live one share an ID. Also undefined: `cpm resume` or `cpm stop` with an unknown ID, `ownerOf` returning nil, and `lastActivity` for a live record with no transcript.
* **Rationale & Recommendation**: Define the rule, for example prefer the live record, and have Stop act on all live PIDs or show each one. Give exit codes and messages for unknown IDs. Use `startedAt` for `lastActivity` when there is no transcript.

### Login detection cannot work as written while the panel is closed - Severity: Major
* **ID:** `implementation-readiness-2-12`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details` (rated Nit)
* **Affected Scope:** `Lines 1255, 1529-1531, 1562-1563, 1570-1573`; specification `158-161, 297, 532, 951-952`
* **Discrepancy Description**: The NFR allows one watcher while hidden, and that watcher ignores `profiles/`. A `.window`-style panel normally closes when the user moves to Terminal or the browser, which is exactly when login happens, so flow 3's FSEvents wait would not see it. This puts CUJ 1's 5-second promise at risk. The Add profile view has no stated host that stays open. File 49's "on file change" names no paths, and the built-in config is `~/.claude.json`, so a folder-only watcher would need `$HOME`. `MenuBarExtra` window style also appears to have no open or closed binding, so "any view is visible" has no named mechanism. The SwiftUI points are from memory and untested.
* **Rationale & Recommendation**: Host Add profile in a real window. Add an explicit NFR exception for one extra watcher on that profile's folder while a login is pending. List the paths watched in each state, say whether `$HOME` is watched or the built-in account is read only on panel open, and name the visibility mechanism.

### Copy setup has no defined path for changing an existing MCP server - Severity: Major
* **ID:** `implementation-readiness-2-13`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`
* **Affected Scope:** `Lines 1245-1247, 1608-1611, 1623, 1647-1656`; specification `618-620, 626, 654-656, 936-939`
* **Discrepancy Description**:
  * MCP items can be Add, Change or Same, with overwrite or rename, but the only write is `claude mcp add-json`. The 2.1.309 binary contains "MCP server already exists" and "already exists in user config", although the command path that emits them was not traced. Remove-then-add would lose the old entry, which is in no backup.
  * Computing Change or Same needs the target's `mcpServers`, but the NFR at lines 937-938 allows parsing it only in the source.
  * `removalCommand` values are undefined, file 3 records only install commands, and ClaudeCLI has no remove methods.
  * File 63 shows removal commands next to recent backups, but file 62's manifest excludes command-written changes, so they are lost after a restart.
  * `needsReLogin` has no detection rule.
* **Rationale & Recommendation**: Specify the Change sequence and its failure handling, or allow only skip for an existing name in v1. Correct the NFR to allow reading the target's `mcpServers`. Add removal commands to files 3 and 17, store executed commands in the manifest, and state the `needsReLogin` rule, for example a remote server type with no auth header.

### Skill copying and the rules for dropped secrets are undefined - Severity: Major
* **ID:** `implementation-readiness-2-14`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-style`, `egm-codebase-details`
* **Affected Scope:** `Lines 1608-1615, 1626-1632, 1655`; specification `632, 635-641`
* **Discrepancy Description**:
  * The flow 6 table says "Per file", while file 58 says "Skills copy per folder". A folder has no defined `beforeHash`, and "flags the whole file" has no folder meaning.
  * "`-copy` before the extension" does not cover a folder, and renaming does not change a skill's or agent's own frontmatter name.
  * Symlinks, binary files and nested command folders are not covered.
  * Flow 6 drops "that variable", but a matched `args` value has no variable name, and dropping a positional argument can change the command's meaning.
  * An item and each `SecretField` both have ticks, but file 63 mentions only item checkboxes.
  * `sourceValue` and `targetValue` have no type, and a "diff for Change" could show raw values of a flagged file or the target's `env`.
* **Rationale & Recommendation**: Decide skills are per folder with a defined hash and rename rule, and specify symlink and binary handling. For a masked arg, keep the whole server unticked with no partial copy. Define how the two ticks interact, and state what the diff masks.

### Plugin commands may defeat Restore for direct writes - Severity: Major
* **ID:** `implementation-readiness-2-15`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`
* **Affected Scope:** `Lines 1622-1623, 1647-1648`; specification `651-656, 764`
* **Discrepancy Description**: After-state hashes are recorded "once direct writes finish", and then the plugin commands run. If those commands rewrite `settings.json`, the hash no longer matches, and Restore lists the file and leaves it alone even with no user edits. Every direct change in that file then cannot be restored. Whether `claude plugin install` writes `settings.json` is unverified, and S0 check 8 does not record it.
* **Rationale & Recommendation**: Add this to check 8. Record the after-state after commands too, or run commands first and then merge.

### S4 omits files it must change, and the two steps are not divided - Severity: Major
* **ID:** `implementation-readiness-2-16`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1036, 1040-1041, 1245, 1327, 1495-1496, 1534, 1543-1544, 1564, 1600-1673`; specification `648`
* **Discrepancy Description**: S4 work is placed in file 17 (`addMCP` and others), file 49 ("S4 copy and restore") and file 50 ("Copy setup…"), and S5 items sit in files 49 and 53, but those stages have no matching Modify entries. S3 files as described would call S4 types that do not exist yet. File 35 also has no S2 entry for the generated `.command` case. Line 1600 says S4 runs in two steps, but files 58-64 are not divided between them, there is no test checkpoint between them, and flow 6's "second step of copy setup" reads as a step in the running product.
* **Rationale & Recommendation**: Add Modify entries wherever a later stage changes an earlier file. Say which files, cases and tests belong to each S4 part, and whether the user sees one flow or two.

### Tests cannot be isolated or built as specified - Severity: Major
* **ID:** `implementation-readiness-2-17`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1113-1114, 1155, 1329, 1358-1359, 1429, 1513-1517, 1591-1596`
* **Discrepancy Description**: File 57 tests AppModel in `ProfileUI` from `ProfileCoreTests`, which has no dependency on `ProfileUI`, and file 47 adds none. ProfileStore is a concrete `final class`, and AppModel's constructor is unspecified, so "calls the right ProfileCore method" has no seam. TestGuard must abort if "any resolved path" is under the real home, but no central path function exists: ConfigLocator covers only profile paths, and the initialisers for EventLog, BackupStore, ShellInstaller, Launcher and DefaultEnvApplier are unspecified. The `cpm add` end-to-end test does not say how the production home is resolved or how the test finds `cpm`.
* **Rationale & Recommendation**: Add a `ProfileUITests` target with named protocols for the actions. Give ConfigLocator, or one `Paths` type, every `~/.claude-profiles` path, and inject it into all modules. State the home source for `cpm` and how tests find the built binary.

### No concurrency model, and real-binary lookup has no owner for the app - Severity: Major
* **ID:** `implementation-readiness-2-18`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details` (rated Nit)
* **Affected Scope:** `Lines 1231-1233, 1238-1239, 1252-1256, 1445, 1528-1531`; specification `514-517`
* **Discrepancy Description**: All APIs are plain synchronous calls, including `poll` (up to a 10-second retry), ProcessRunner (a 5-second `zsh -lic`) and SessionIndex (5,000 transcripts). The plan does not say what runs off the main thread, how AppModel gets results back, or what happens when a timeout fires. `realBinary()` takes no input, but the rule differs by caller, and no file owns the app's shell read or defines its failure. ProcessRunner's timeout values and how `env` combines with the inherited environment are also unstated.
* **Rationale & Recommendation**: State the threading model, such as `async` functions or a background actor. Change the signature to `realBinary(path:)`. Name the file that owns the `zsh -lic` read and its failure result, and give the timeout values and environment merge rule.

### Removal failure handling is undefined - Severity: Major
* **ID:** `implementation-readiness-2-19`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details` (rated Nit)
* **Affected Scope:** `Lines 1247, 1680-1682`; specification `660-667`
* **Discrepancy Description**: A failed `claude auth logout` (network, timeout, missing binary) or a failed Trash move has no stated behaviour, so a `deleting` row could block on every reconcile or carry on. File 65 silently skips Keychain deletion when no name is recorded, while flow 3 and check 6 promise a warning. The manual step text is not written.
* **Rationale & Recommendation**: State continue or stop for each step, and write the warning and manual step text.

### ShellInstaller install, upgrade and uninstall ownership are unclear - Severity: Major
* **ID:** `implementation-readiness-2-20`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1318, 1326-1327, 1342-1343, 1579, 1683-1686`; specification `345, 401-402, 701-702, 708-710, 717-718, 784`
* **Discrepancy Description**:
  * `ShellInstaller.uninstall()` has no stated content, and `ProfileRemover.uninstall()` does the same work without calling it.
  * No method performs the "replace `bin/` when the version differs" upgrade, and no file says what writes `VERSION` or where its value comes from.
  * Flow 10 and line 784 say `make-cli.sh` installs to `bin/`, while file 29 stops at `build/cli/`.
  * Once installed, `cpm` runs from `bin/`, so "the folder beside the running cpm" is both source and destination. The on-disk layout has no plist in `bin/`, so a repeat install cannot find it.
  * `launchctl bootstrap` on a loaded agent generally returns an error (from memory), so the step is not safe to repeat, and a stub test would not catch this.
  * `isInstalled()` has no definition.
  * A running app could recreate `~/.claude-profiles` after uninstall through `load()`'s seeding, and uninstall does not address the app itself.
* **Rationale & Recommendation**: Give uninstall one owner. Specify the plist location, same-folder handling, `bootout` then `bootstrap`, the version source, the owner of the upgrade check, and `isInstalled()`. Use one `make-cli.sh` description in all three places, and state how uninstall deals with a running app.

### `scripts/s0-backup.sh` names a tool with no exclude option - Severity: Major
* **ID:** `implementation-readiness-2-21`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`
* **Affected Scope:** `Lines 1091-1095`
* **Discrepancy Description**: File 4 copies `~/.claude` "excluding `sessions/`" with `ditto`, but `/usr/share/man/man1/ditto.1` has no exclude option, only `--bom` for an include list. Copying first and then deleting would still copy the `.key` files for a moment. The restore steps are not written out, and the backup does not cover the desktop app's data, which check 12 could affect.
* **Rationale & Recommendation**: This script guards the author's existing login and history. Name a working command, such as `rsync -a --exclude`, and verify its metadata handling. Write out the restore steps, and back up the desktop app's data or add a recovery step. The reviewers offered copy-then-delete as a second option; it is not preferred because of the `.key` copy.

### Session ID goes into a generated script with no validation - Severity: Major
* **ID:** `codebase-details-2-3`
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** `Lines 1268, 1445, 1491-1493`; specification `449, 497, 505-506`
* **Discrepancy Description**: `sessionId` comes from a file name or a live record. The quoting rule covers only the folder, and the Name rules cover only `commandName`. A non-UUID ID also skips the wrapper's ownership check.
* **Rationale & Recommendation**: Launcher refuses any `sessionId` that is not a UUID. Add a hostile-ID test to file 44.

### Restore can delete a created file the user later edited - Severity: Major
* **ID:** `codebase-details-2-4`
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** `Line 1671`; specification `651-653`
* **Discrepancy Description**: The hash check applies only to backed-up files, and created files are simply deleted. That contradicts "Restore never discards later work" on line 653, and file 64 does not test this case.
* **Rationale & Recommendation**: Apply the after-hash check to created files, and add a test.

### Secret detection has gaps and undefined patterns - Severity: Major
* **ID:** `codebase-details-2-5`
* **Reviewer Lens:** `egm-codebase-details`
* **Also raised by:** `egm-implementation-readiness` (rated Nit), `egm-style` (test words only, rated Nit)
* **Affected Scope:** `Lines 1629-1632, 1672`; specification `87, 627, 635-641`
* **Discrepancy Description**:
  * "Hex or base64 runs of 40 or more" is not an exact regex. If `/` counts as a base64 character, ordinary long paths match, and a match unticks a whole file.
  * `sk-[A-Za-z0-9_-]{20,}` has no word boundary, so it can match inside `task-...`.
  * `permissions.allow` entries are ticked by default and not scanned, yet they can hold command text with tokens. MCP `url` values are not scanned either.
  * The test words `author` and `monkey` cannot come close to these value patterns, so that test exercises nothing.
  * The patterns do not cover `github_pat_`, `AKIA` or JWT shapes. The reviewer does not assert that this list is required.
* **Rationale & Recommendation**: Give exact regexes with boundaries. Scan `permissions` and `url`. Replace the test words with near misses, such as a 39-character hex string, and add path false-positive tests.

### Doctor's Keychain rule, `error` recovery and `setDefault` eligibility are undefined - Severity: Major
* **ID:** `codebase-details-2-6`
* **Reviewer Lens:** `egm-codebase-details`
* **Also raised by:** `egm-implementation-readiness`
* **Affected Scope:** `Lines 1132, 1188, 1192, 1335, 1384`; specification `209, 281, 283-285, 292, 728, 731`
* **Discrepancy Description**: "Recorded Keychain name exists" is a Fault, but the built-in row never has a name, and flow 3 can leave a `ready` row without one. The plan does not say whether doctor exits 2 forever in those cases, and CUJ 6 wants login presence reported for each profile. `error` is "fixed by the action `doctor` names", but no action is defined, no transition leaves `error`, and no rule says whether removal works on it or what happens if its folder returns. `setDefault` and `cpm default` have no rule for `pendingLogin`, `loggedOut`, `deleting` or `error` rows.
* **Rationale & Recommendation**: Add the rules and tests. For example, no recorded name is a Note, the built-in profile checks the plain name, an `error` row can be removed, and `setDefault` allows only `ready`.

### The panel measurement does not measure the panel - Severity: Major
* **ID:** `style-2-1`
* **Reviewer Lens:** `egm-style`
* **Also raised by:** `egm-implementation-readiness` (rated Nit), `egm-codebase-details` (rated Nit)
* **Affected Scope:** `Lines 1499-1504, 1712`; specification `986-989`
* **Discrepancy Description**: Limits sets a target for "the panel opening in under 1 second" and says S2 measures it. File 45 times only `SessionIndex`, in S2, before any panel exists. A shell script also has no named way to call a Swift API, no fixture generator is defined, and "the next step is an on-disk cache, which is decided then" both commits and defers.
* **Rationale & Recommendation**: Rename the script, for example to `measure-session-index.sh`, and describe the result as index time only. State where real panel-open time is measured, or restate the target. Name the driver, such as a hidden `cpm` subcommand or a test, and write "If missed, decide then whether to add an on-disk cache."

### The secret warning asserts something the app does not know - Severity: Major
* **ID:** `style-2-2`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1630-1632, 1658`; specification `635`
* **Discrepancy Description**: The message says "Contains a secret (API key)" and "puts the key in plain text". Flow 6 masks every `env` and `headers` value "whatever it looks like", and file 60 matches any long hex or base64 run, including non-secrets such as hashes. The message states a suspicion as fact and names a type the detector does not establish.
* **Rationale & Recommendation**: Reword, for example: "May contain a secret. Unticked by default. If copied, this value is stored in plain text in <target>."

---

## Nits

### The E2E checklist arrives after S1 - Severity: Nit
* **ID:** `implementation-readiness-2-22`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1040-1041, 1506-1509`
* **Discrepancy Description**: The checklist is created in S2, yet line 1040 says it gains each stage's journeys, and S1 delivers some.
* **Rationale & Recommendation**: Create it in S1.

### Named test gaps - Severity: Nit
* **ID:** `implementation-readiness-2-23`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1387-1429, 1477-1479, 1495-1496, 1705`
* **Discrepancy Description**: No named cases cover `profile-label`, `claude-profiles.zsh`, ProcessRunner timeouts, `cpm` exit codes, subcommands other than `add` (including the non-terminal `--yes` rule), own-session resume, or `--resume` of an ID found nowhere. The first wrapper case at line 1394 conflicts with the override case at line 1407. Line 1705 lists file 35 under `swift test`, but it runs through `zsh`.
* **Rationale & Recommendation**: Add the cases. Reword line 1394 to "set to a value inside `~/.claude-profiles/`". Remove file 35 from line 1705.

### Small flow and signature mismatches - Severity: Nit
* **ID:** `implementation-readiness-2-24`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1245, 1463, 1620`; specification `494-498, 600`
* **Discrepancy Description**: Flow 6 has `plan(source, target, selection)`, while file 59 has `plan(source, target)`. File 17 lists `login` with no caller, because login goes through Launcher. Flow 2's "exact" template has no `auth login` form.
* **Rationale & Recommendation**: Use one signature, remove `login` from file 17 or name its caller, and add the `auth login` form to flow 2.

### Log contents are not fixed - Severity: Nit
* **ID:** `implementation-readiness-2-25`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`, `egm-style`
* **Affected Scope:** `Lines 1200, 1223-1225, 1409, 1423`; specification `461`
* **Discrepancy Description**: The `launch.log` fields are undefined. File 11 logs all "changes", but file 14's kinds cover only copy, restore, delete, default and stop. File 14 replaces tabs and newlines with spaces, but the test at line 1423 says it "escapes tabs".
* **Rationale & Recommendation**: Define the fields and kinds. Change the test wording to "replaces tabs and newlines with spaces".

### Registry details are missing - Severity: Nit
* **ID:** `implementation-readiness-2-26`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`
* **Affected Scope:** `Lines 1129, 1136, 1181`; specification `287-289`
* **Discrepancy Description**: Unstated: the first `schemaVersion` value, behaviour on a newer version, `createdAt` date encoding, and the built-in seed row's state when it has no login.
* **Rationale & Recommendation**: State each one in file 7 or file 11.

### App details are unverified or unnamed - Severity: Nit
* **ID:** `implementation-readiness-2-27`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** `Lines 1562, 1572-1573`; specification `999-1001`
* **Discrepancy Description**: On macOS 14, a `WindowGroup` may open at launch in a menu-bar app (from memory, untested). The colour picker control is unspecified, and the mock is private to the author's account. The main watcher ignores only `profiles/`, so log and `run/` writes will wake the app.
* **Rationale & Recommendation**: Consider a single `Window` scene with explicit launch suppression, name the colour control, and extend the ignore list.

### The lint scope omits root scripts - Severity: Nit
* **ID:** `implementation-readiness-2-28`
* **Reviewer Lens:** `egm-implementation-readiness`
* **Also raised by:** `egm-codebase-details`
* **Affected Scope:** `Lines 1708-1709`
* **Discrepancy Description**: `bash -n` covers `scripts/*.sh` but not `make-cli.sh` or `make-app.sh`.
* **Rationale & Recommendation**: Add both.

### Transcript reader rules are incomplete - Severity: Nit
* **ID:** `codebase-details-2-7`
* **Reviewer Lens:** `egm-codebase-details`
* **Also raised by:** `egm-style`
* **Affected Scope:** `Lines 1268-1272, 1351, 1425`; specification `550-551`
* **Discrepancy Description**: Local transcripts hold many repeated title records (about 3,438 across 42 files), and the plan does not say the last one wins. It does not say which `cwd` to use, from which end the read extends, or whether 1 MB is per end or total. "Past 64 KB" is unclear when both ends are read. No local record starts with a top-level `{"type":"summary"`, so the `summary` fallback may be dead code or match the wrong key.
* **Rationale & Recommendation**: State last-wins, the first `cwd`, the read direction and limit. Describe the fixture as "a title outside both 64 KB windows", and confirm or drop `summary`.

### `enabledPlugins` and `extraKnownMarketplaces` are maps - Severity: Nit
* **ID:** `codebase-details-2-8`
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** `Line 1640`; specification `629`
* **Discrepancy Description**: The plan says these are "added", but local key structure shows maps (`name@marketplace: true`, and an object). It does not say what happens when a key exists with a different value.
* **Rationale & Recommendation**: State the conflict rule.

### `pointsIntoSource` has no matching rule - Severity: Nit
* **ID:** `codebase-details-2-9`
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** `Line 1609`; specification `642`
* **Discrepancy Description**: The plan does not say how path matching treats forms such as `~` and `$HOME`.
* **Rationale & Recommendation**: Define the matching forms and add a test for each.

### Small unspecified failure rules - Severity: Nit
* **ID:** `codebase-details-2-10`
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** `Lines 1187, 1319-1320`; specification `707`
* **Discrepancy Description**: The plan does not say whether Discard can run while a login is still running or after a Keychain item was created. It also does not say whether `logs/` exists before the LaunchAgent runs.
* **Rationale & Recommendation**: State both.

### Doctor's parse count and S0 teardown have small gaps - Severity: Nit
* **ID:** `codebase-details-2-11`
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** `Lines 1077-1078, 1445`; specification `739`
* **Discrepancy Description**: Doctor's parse count in S1 depends on S2's `SessionIndex` unless it uses file 20 directly. S0 teardown leaves Keychain items if logout does not remove them.
* **Rationale & Recommendation**: Point doctor at file 20, and add a teardown step for leftover test items.

### Seed name rules and `pidDomain` are not covered - Severity: Nit
* **ID:** `codebase-details-2-12`
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** `Lines 1279-1280`; specification `269-271, 287-288`
* **Discrepancy Description**: A seed `displayName` taken from an organization name may break the Name rules, for example by exceeding 40 characters. Live records also have `pidDomain` and `kind` keys that the plan does not mention. All three local records say `darwin` and `interactive`. Another `pidDomain`, for example for Cowork, could make PID checks wrong, but no other value was confirmed.
* **Rationale & Recommendation**: State how the seed name is shortened or cleaned, and consider adding `pidDomain` to S0 check 16.

### Undefined use of "broken" - Severity: Nit
* **ID:** `style-2-3`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1310-1311, 1351, 1416, 1425, 1485`
* **Discrepancy Description**: "Broken" stands for different conditions, and "`doctor` reports it" has an unclear "it".
* **Rationale & Recommendation**: Use the precise condition each time.

### Ambiguous "what is uncovered" - Severity: Nit
* **ID:** `style-2-4`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Line 1054`
* **Discrepancy Description**: "Uncovered" can mean "discovered" or "not covered".
* **Rationale & Recommendation**: Write "list the flow 5 cases that no longer follow the default".

### Vague "Revisit this whole plan" - Severity: Nit
* **ID:** `style-2-5`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Line 1053`; specification `767`
* **Discrepancy Description**: There is no end condition, and line 767 has a more concrete form.
* **Rationale & Recommendation**: Use "Change the plan to adopt or borrow it", and state who decides.

### "Whose subject changed" cannot be applied and is repeated - Severity: Nit
* **ID:** `style-2-6`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1079, 1438`; specification `678`
* **Discrepancy Description**: The rule appears three times, and the S0 table has no subject field.
* **Rationale & Recommendation**: State it once, and add a column listing the Claude Code behaviour each check covers.

### The reason is attached to the wrong clause - Severity: Nit
* **ID:** `style-2-7`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1076-1077`
* **Discrepancy Description**: "Because `/tmp` may clear at reboot and is a symlink" explains why `/tmp` is avoided, not why `realpath` is used.
* **Rationale & Recommendation**: Split it into two sentences, one for each reason.

### Name rules wording overreaches - Severity: Nit
* **ID:** `style-2-8`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1138-1139`
* **Discrepancy Description**: The text says `color` follows the Name rules, which cover only `commandName` and `displayName`.
* **Rationale & Recommendation**: Remove `color` from the first clause.

### Absolute claim about the decoder - Severity: Nit
* **ID:** `style-2-9`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Line 1172`
* **Discrepancy Description**: "Never held as values" is stronger than the mechanism supports, because the parser still reads the whole file.
* **Rationale & Recommendation**: Write "are never decoded into app types".

### "Written once" is inaccurate as worded - Severity: Nit
* **ID:** `style-2-10`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1217-1218`
* **Discrepancy Description**: Copy setup later writes the same file, "the script" is unnamed, and "the absolute path of `bin/profile-label <id>`" includes an argument in the path.
* **Rationale & Recommendation**: Write "StatusLineWriter writes the file only at creation, because `profile-label` reads the display name at run time".

### Over-compressed binary lookup text - Severity: Nit
* **ID:** `style-2-11`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1238-1239`
* **Discrepancy Description**: "The read in flow 2" has no clear referent, and "Resolved at each use" has no subject.
* **Rationale & Recommendation**: Write "The app gets `PATH` from the login-shell command in flow 2. The path is resolved again at each use."

### "Retry" is overloaded - Severity: Nit
* **ID:** `style-2-12`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1254-1256, 1383`
* **Discrepancy Description**: "Retry" means both the user's Retry action and the 10-second Keychain re-listing. "Reuses the same name" does not say which name.
* **Rationale & Recommendation**: Use "repeated listing" for the Keychain case, and write "the same Keychain name".

### Synonyms for test doubles - Severity: Nit
* **ID:** `style-2-13`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1233, 1359, 1391, 1414, 1490`
* **Discrepancy Description**: "Fake" and "stub" appear without a distinction.
* **Rationale & Recommendation**: Use one term, or define the difference once.

### Ambiguous "the plist" - Severity: Nit
* **ID:** `style-2-14`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1343, 1586-1587`
* **Discrepancy Description**: File 56 mentions `Info.plist` and then "the plist", meaning the LaunchAgent plist.
* **Rationale & Recommendation**: Write "the LaunchAgent plist".

### Dense parenthetical - Severity: Nit
* **ID:** `style-2-15`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Line 1479`
* **Discrepancy Description**: "(not on a terminal without `--yes` → exit 1)" takes several readings to parse.
* **Rationale & Recommendation**: Write "Exits 1 if input is not a terminal and `--yes` is absent."

### Loose sentences - Severity: Nit
* **ID:** `style-2-16`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1534, 1632`
* **Discrepancy Description**: "It keeps only filtering and sorting" is unclear, and file 39 also sorts. "Shows as `••••••`, with none of it shown" repeats itself.
* **Rationale & Recommendation**: Write "Its only logic is filtering and sorting", say which module owns sort order, and delete the final clause at line 1632.

### Misplaced or vague test names - Severity: Nit
* **ID:** `style-2-17`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1357, 1419, 1524, 1705`
* **Discrepancy Description**: File 48 specifies a test but names no test file, although the verification list counts it. `SupportTests` is vague for tests of many modules, and "Test support" means something else at line 1357.
* **Rationale & Recommendation**: Name file 48's test file, and give file 37's tests a more specific name.

### Metaphorical or telegraphic test descriptions - Severity: Nit
* **ID:** `style-2-18`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1374, 1384-1385, 1458, 1486, 1493, 1672`
* **Discrepancy Description**: "Hostile", "stays inert", "test seam" and "`mayBeStale` path" are less exact than a plain description. "A clean setup with the `.claude/.claude.json` note" is cryptic, and "true and false matches" does not state what is expected.
* **Rationale & Recommendation**: Describe the observable result.

### Non-parallel and informal stage table, repeated instructions - Severity: Nit
* **ID:** `style-2-19`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1025, 1030-1033, 1038, 1047, 1063`
* **Discrepancy Description**: The "you can" header uses second person, and the S6 cell does not complete its sentence. "With a backup in hand" and "no more logging out" are informal. "Stop. Do not start S1" says one thing twice. `git init` is instructed at both 1025 and 1063.
* **Rationale & Recommendation**: Use "Outcome" as the header with parallel phrases, and keep one `git init` instruction.

### Smaller terminology and wording points - Severity: Nit
* **ID:** `style-2-20`
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** `Lines 1069, 1081, 1182, 1373, 1435, 1471, 1494, 1501, 1645, 1710-1711, 1714-1715, 1719-1721`
* **Discrepancy Description**:
  * `s0-results/` and `docs/S0-results.md` are similar names for different things.
  * "Orphans", "app folders" and "shell layer" are not defined in Terms.
  * "Stop." as a Type / Scope value reads as a command.
  * "The risk sits in specific rules, not in line counts" is rhetorical.
  * "`cpm doctor` passes or names the fault" has no clear failing outcome.
  * The provenance comment and "Drafted with EGM" repeat each other.
* **Rationale & Recommendation**: Rename the raw folder, for example to `s0-raw/`, and define or replace the undefined terms. Use noun phrases for scope values, and shorten the coverage reason to "no percentage target; coverage is defined by the named cases". Define failure for the doctor step, and keep one provenance line unless a template requires both.

---

## Verified Against Local Files

`egm-codebase-details` confirmed these by key names and structure only:

* `~/.claude.json` has top-level `oauthAccount` with `accountUuid`, `emailAddress` and `organizationName` (by line position), and a top-level `mcpServers`.
* `~/.claude/.claude.json` exists and has no `oauthAccount` or `mcpServers` line, matching flow 11's note and Terms.
* `~/.claude/sessions/` holds `<pid>.json` files with `.key` files beside them, and the `.key` files were not opened. The JSON has `pid`, `sessionId`, `cwd`, `startedAt`, `procStart` (a string), `entrypoint`, `name` and `status`.
* The values `cli`, `claude-desktop`, `busy` and `idle` were observed, and a transcript also carries `entrypoint`.
* Subagent transcripts sit one level deeper, so the `projects/*/*.jsonl` glob correctly excludes them.
* `custom-title` and `ai-title` record types exist.
* `CLAUDE_CODE_DISABLE_TERMINAL_TITLE` appears in the 2.1.309 binary.
* Session records show versions 2.1.296 and 2.1.309.
* `libObservationMacros` is in Command Line Tools, so `@Observable` looks feasible, but it was not compiled.
* Stage file ranges are consistent, and all 67 entries carry Create or Modify.

## Not Verified

* Runtime behaviour of any kind, including the S0 assumptions, `swift test`, signing, Keychain prompts, `launchctl`, Terminal and `.command` behaviour, `path_helper`, zsh and FSEvents.
* The plan's 2.1.296 binary claims, because only the 2.1.308 and 2.1.309 binaries were found. Keychain naming was not checked.
* Whether `claude plugin install` changes `settings.json`, and the exact plugin command forms.
* The Cowork `entrypoint` value and the semantics of `procStart`.
* Xcode installed somewhere other than directly under `/Applications`.
