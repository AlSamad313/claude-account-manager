# Stage 8 Intermediate Zoom Out: Claude Profile Manager

**Target:** `claude-profile-manager.md` (lines 1-1579)
**Verdict:** Changes required: 0 Blockers, 9 Major, 9 Nits. All line references are to the file as reviewed.

The root problem is still clear, and the core approach is still sound. Many of the changes since Stage 5 simplified the design. Complexity has crept in at four points:
- the login-wide default
- a second launch implementation
- logic in the wrapper that breaks its own "no JSON" rule
- the Restore and secret-scan rules in copy setup

The Implementation Plan has also fallen behind the flows, and three earlier Majors (M3, M5, M11) are unchanged. Size since Stage 5: the document grew from 1205 to 1579 lines, the plan from 52 to 56 files, and the S0 checks from 11 in the brief to 21.

## Review basis and limits
- **Read in full:** the target, persona, standard, `zoom_out_report.md`, `concepts_brief.md` and `concept_input.md`.
- **Not read:** `comprehension_report.md`, `critic_report.md` and `goldfish_review_report.md`. They were neither supplied nor linked by the document. Additions were identified by comparing the document with the Stage 5 report and the brief, so which stage caused each change is inferred, not confirmed. A mitigation may have a reason recorded only in those reports.
- **Context sufficiency (section 0):** passes.
- **Code reuse:** there is no code to search. The repo holds only documents in `docs/designs`.
- **Project alignment (section 1):** no root `README.md` or `context.md` exists, so this was checked against the brief.
- **Unverified:** no URLs were opened (including the mock) and no commands were run. Claude Code behaviour is unverified, and no credentials were read.
- **Author decisions:** not reopened as requirements. Where a decision carries a cost, the report says so and suggests how to contain it.

## Are the additions justified?

**Justified. Keep these; several of them simplify the design:**
- Wrapper script instead of a sourced function (Alternative L, lines 405-408, 853-859). This removes a class of shell-closing errors and is the structural fix for earlier N8 and N9.
- `CLAUDE_PROFILE_ID` for nested calls (lines 445-447).
- Stopping on a broken default (lines 138-139, 416-417). This directly serves problem 3.
- Warning about login variables instead of silently unsetting them (lines 134-135, 454-457).
- The user adding the shell lines by hand (line 662).
- The "Live session" definition that ignores the display filter (lines 247-249).
- The exact-path rule for `configDir` (lines 236-243) and the name rules (lines 262-264). Both are load-bearing.
- Quoting `.command` values (line 468).
- The single allow-list table for copy setup (lines 574-596).
- Central backups (line 383).
- One S0 table with three stop conditions (lines 691-693).
- `ProcessRunner` (lines 328-330).

**Not justified, or too heavy for the benefit:** see Majors 1, 2, 3 and 6, and Nits a, b and c.

## Major

### 1. The login-wide setting costs a lot and sits inside S1 (new)
**Affected scope:** lines 253-254, 303-305, 363-365, 514-548, 708, 714, 886-890, 894-897; files 53-55; S1 row at line 964

**Simplicity rationale:**
- This is an author decision (lines 132-133, 850-851). It nevertheless reaches well beyond the problem statement, and it now accounts for:
  - a script, a Swift caller, a LaunchAgent and a plist
  - two `doctor` rows and a test file
  - S0 checks 12 and 17
  - a risk row and extra rollback steps that require logging out and back in
- It is the only part of the design that changes the environment of every app in the login, which is why flow 5 needs a 13-row table saying where the default applies.
- It adds silent wrong-account cases, the very problem the tool exists to remove. For example, line 538 says "Possibly not" for apps opened before the LaunchAgent runs, and line 539 covers apps that are already running.
- No CUJ needs it beyond one sentence in CUJ 4 (lines 179-181).
- The design already says it can be removed without affecting the rest (lines 546-548), so it does not need to ship with the S1 core.
- Check 12 joins a benefit and a risk into one result. Under the table at line 708, a VS Code failure alone drops the setting, while flow 5 (lines 546-548) drops it only for desktop app or Cowork damage. If VS Code does not follow the setting, its benefit is mostly gone. If VS Code passes and the desktop app breaks, the author may prefer a different choice.

**Fix:** keep the author's goal, but move the LaunchAgent, script and applier out of S1 into their own stage after the wrapper is in use. Split check 12 into a benefit check (VS Code) and a safety check (desktop app and Cowork), and state what each result means.

### 2. The app reimplements the wrapper's launch rules (new)
**Affected scope:** lines 449-452, 461-473; file 8 (lines 1064-1066); file 22 (lines 1231-1238); file 23 (lines 1243-1244); line 386

**Simplicity rationale:**
- Flow 1 (zsh) and flow 2 (Swift generating shell) each implement the same list: set or unset the variable, `CLAUDE_PROFILE_ID`, the title variable, the window title, login-variable warnings, binary lookup and the launch log. This duplicates the exact rules the design treats as most dangerous to get wrong.
- The app also has to run `zsh -lic` to get `PATH` (line 473), which starts an interactive shell from a GUI app on every launch.
- `launch.log` ends up with writers in two languages (line 386), while `EventLog` rotates that file at 1 MB (line 1131).
- `claude-as` already does all of this and is installed before the app can launch anything.

**Fix:** have the `.command` file do only `cd '<folder>'` and then `exec ~/.claude-profiles/bin/claude-as '<commandName>' [--resume '<id>']`.
- Terminal's own shell provides `PATH`, subject to check 14.
- The app inherits the resume guard, warnings and logging from the wrapper.
- Launcher becomes very small, and the launch rules live in one place.
- Binary lookup stays only for `CompatCheck` and `ClaudeCLI`.
- The app must confirm the shell layer is installed before launching. Flow 10 already makes that a setup step.
- This changes brief decision 15 (line 286), which the author accepted, so it needs the author's confirmation.

### 3. The wrapper's new checks break its "no JSON" rule (new)
**Affected scope:** lines 307-308, 407, 422-429; file 16 (lines 1163-1173); test at lines 1403-1404; brief lines 113, 283

**Simplicity rationale:**
- The wrapper exists to read plain files and stay free of JSON (diagram at lines 307-308, line 407, brief decision 13).
- The "already running" check (lines 427-429) must read `sessions/*.json` across a profile. The refusal hint's `<folder>` (line 425) needs a folder that, as far as the design shows, comes from transcript data.
- Neither has a stated parsing method, and file 16 does not mention the live check at all.
- The check also tests only "a live pid", without the `procStart` match the design requires everywhere else (lines 506-508). A reused PID could therefore refuse a valid resume, with no way around it.
- The author decided only the ownership refusal (lines 128-129). No decision or CUJ asks for a wrapper-level live check, and no S0 check tests what Claude Code does when one session is resumed twice.

**Fix:**
- Keep the ownership check, which needs only a file-name glob.
- Drop the live check from the wrapper, or add an S0 check first and keep it only if a double resume causes harm. The app already shows Stop instead of Resume for a running session (line 474).
- Have the refusal name the owner and the command, without the folder.

### 4. `default` cannot be regenerated from the source of truth, and lost updates remain (earlier M4, partly fixed)
**Affected scope:** lines 276-278, 321, 394-399, 524; file 6 (lines 1026-1042); file 10 (lines 1096-1102)

**Simplicity rationale:**
- The new rule says `default` and `names` are regenerated from `profiles.json` (line 278). Neither the field list (lines 394-397) nor `Registry` (line 1042) records which profile is the default. So `default` cannot be rebuilt.
- This matters more now, because plain `claude` stops when `default` is broken (line 416).
- The original M4 problem is also still open. The app and `cpm` each run `ProfileStore`, atomic rename does not prevent lost updates, and no re-read or lock is specified.

**Fix:** add `defaultId` to `Registry`. State one rule for `ProfileStore`: take a lock file, re-read from disk, change, then write. No daemon is needed.

### 5. S1 still cannot deliver "no more logging out" (earlier M3, unchanged)
**Affected scope:** lines 350-352, 649-655, 958-968; files 22, 28, 31, 48

**Simplicity rationale:**
- Each stage is meant to be usable on its own (line 958), but `cpm` still has no add command.
- The add flow is in file 28 (S3) and depends on Launcher (S2).
- `cpm list` and `cpm stop` are in S1's file 31 but need S2 modules. The transcript parse check in file 15 needs S2's file 18.
- Nothing installs `cpm` in S1. Shell files and `cpm` reach the user through the app bundle (line 649, file 35), which is S3. `install-shell` also needs the plist from that bundle.
- New: "Sign out and retry" in flow 3 (S3) needs the logout method on `ClaudeCLI`, which is file 48 in S4.

**Fix:**
- Add a minimal `cpm add <name>` to S1.
- Define how `cpm` is installed before S3.
- Assign subcommands to stages.
- Move `ClaudeCLI` out of `SetupCopier/` and into the earliest stage that uses it.
- Remove file 30 (`FileWatcher`) from S1, since nothing there watches files.

### 6. Copy setup has new rules that may not work in practice (earlier M8 and N6, partly fixed)
**Affected scope:** lines 554-557, 570-572, 579, 585-590, 600-604; file 44, file 45, file 48 (lines 1477-1479), file 50 (lines 1495-1498); brief line 105; stage table line 967

**Simplicity rationale:**
- **Fixed from M8:** the two-tier promise (lines 554-557) is the right, honest statement. Check 8 picking one MCP path is also right.
- **Two MCP paths have returned:** file 48 reports "not available" so `SetupCopier` "uses the fallback", which contradicts file 44's "one path".
- **Restore may rarely work for `.claude.json`:** Restore acts only if a file's hash still equals the after-state hash (lines 600-602). The brief observed Claude Code rewriting this file every few minutes (line 105). Once the target profile has been used, any backed-up `.claude.json` would therefore be listed and left alone. The hash rule is safe, but Restore can't do what CUJ 5 implies for MCP changes.
- **Secret scan on prose:** scanning skills, agents, commands and `CLAUDE.md` (line 588) uses a key pattern containing `key` and `auth` (line 1449) plus long hex or base64 values. It is unclear what a "key" even is in Markdown, and masking "its values" (line 589) has no defined meaning in free text. It could untick many ordinary skills and undercut the "Yes for new files" default at line 585.
- **Stale test:** line 1496 says plugin registries are "never touched", while line 557 says install commands may write them. Line 1498 says restore gives byte-identical files, while line 602 says changed files are left alone.
- **Unassigned items:** `permissions`, `model` and `env` belong to neither step in the stage table (line 967).

**Fix:**
- Keep one MCP path and delete the runtime fallback.
- For Restore, state the limit for `.claude.json`, or scope Restore to files that Claude Code does not rewrite and keep only a backup path for the rest.
- For free-text files, use only value-shape patterns (`sk-`, long tokens) and flag the whole file. Do not attempt to mask inside it.
- Assign the three items to a step, or drop `model` and `env` from v1.
- The earlier closing advice still stands: reassess S4's scope once S1-S3 are in real use. Flow 6 is now the largest section of rules in the design, and it remains the only feature that merges content into Claude Code's files.

### 7. The exact-version allow-list will still produce a permanent banner, and S0 is now a heavier rerun (earlier M5, unchanged and worse)
**Affected scope:** lines 339-341, 619-626, 676, 691-693, 987; file 14 (lines 1138-1143), file 29 (line 1300), file 36 (line 1355), file 51 (lines 1510-1512), line 1572

**Simplicity rationale:**
- `testedVersions` is still an exact-version list compiled into the app, starting at `2.1.296`. The banner will usually be showing and will soon be ignored.
- The maintenance duty is now rerunning a 21-check manual checklist, including a reboot, after each minor update (line 987), plus fixtures per version (line 1355). That is a poor fit for a one-person tool.
- Only 3 of the 21 checks can stop the project.
- `CompatCheck` and `Doctor` also overlap on version and Keychain name checks (lines 339-341, 675-676).

**Fix:**
- Keep the shape-based `canWrite` check, which is the real protection.
- Reduce the version comparison to an informational `doctor` line.
- Rerun only the checks whose subject changed, and run stage-specific checks in the stage that needs them (for example 8, 15, 16, 18 and 20).
- Merge `CompatCheck`'s report into `Doctor`, leaving `canWrite` as the one thing other modules call.

### 8. The `TerminalOpener` seam still has no real second user (earlier M11, unchanged)
**Affected scope:** lines 470, 1234-1237, 1299, 1320-1325, 1416; file 5 (lines 1016-1018)

**Simplicity rationale:**
- `cpm` has no command that opens Terminal, yet the `/usr/bin/open` implementation is described as "used by the CLI and tests", and file 42's tests use a fake.
- Line 1236 still places the AppKit implementation in `ProfileUI`, while lines 1299 and 1325 place it in file 29.
- `ProfileUI` is still a separate library with one consumer and no test target.

**Fix:** use `open -a Terminal <file>` through `ProcessRunner` for the app, subject to check 9. Keep only a test seam, and merge `ProfileUI` into the app target unless something justifies keeping it apart.

### 9. The Implementation Plan has fallen behind the flows, and in one place contradicts them (new)
**Affected scope:** as listed below

**Simplicity rationale:** mitigations were applied to the flows and only partly carried down, and because the plan restates the rules, every later change has to be made twice. An implementer following the plan would build the wrong behaviour:
- **Direct contradiction:** the shell test expects "missing `default` file → warning and built-in", while lines 138-139 and 416-417 require stopping. A missing default falling back to built-in is the fail-open case the decision rejected.
- **Wrapper:** file 16 says "follow flow 1 exactly" and then restates it differently. It warns only about `ANTHROPIC_API_KEY` where flow 1 covers three variables. It also omits the override rule, the live check, `CLAUDE_CODE_DISABLE_TERMINAL_TITLE` and the log line. File 15 likewise mentions only `ANTHROPIC_API_KEY` (line 1154).
- **Launcher and environment:** file 8 (lines 1064-1066) and file 22 omit the title variable and the warnings that flow 2 requires. Line 314 says ConfigLocator returns "the environment to launch with", so this rule has no single home.
- **Doctor:** file 15 and the flow 11 table list different checks. For example, plugin registries appear only at line 1156.
- **Verification:** line 1570 covers "CUJ (1–6)" although there are 8 CUJs, so stop and removal are never walked through.
- **`ClaudeCLI`:** its logout method is added as an aside in file 56 (line 1546) and is not in file 48.

**Fix:** correct the test. Have files 8, 15, 16 and 22 point to their flow rather than restating it, and extend the E2E list to all 8 CUJs.

## Nit

**N-a. A Keychain bookkeeping failure marks a working login as `error`** (lines 274, 489-491, 707; earlier N2 stands). Flow 3 sets `error` when it finds zero or several new names, yet the login itself may be fine, and the name is needed mainly for cleanup and a `doctor` check. Check 7's fallback is also milder ("skip Keychain cleanup and warn"). Use `ready` with no recorded name and a warning. If check 6 shows logout removes the item, `delete` and its guards can also go.

**N-b. Flow 1's override path has no defined end** (lines 412-413, 430-438). "Skip to login variables" runs on into lines 432-433, which set or unset the variable using an `id` that was never assigned. The test at lines 1401-1402 shows the intent is clear, so the pseudocode needs an explicit exit.

**N-c. The status line remains as a second label with undisclosed costs** (lines 79-83, 113-115, 360-362, 594; files 12, 17). Earlier M1 was properly taken to the author and is closed. However, the Alternative I rejection (lines 832-834) compares it with the window title, not with the status line that the design also keeps. A user's own status line can't be copied into an added profile, and this cost is not stated. The status line's main remaining value is labelling added-profile sessions that bypass the wrapper. State this, or drop it if the window title proves enough.

**N-d. Disabling Claude Code's own title has a cost that is not stated** (lines 50, 435, 465). The fix for earlier M2 is reasonable, subject to check 19. It does change Claude Code's visible behaviour, though, despite line 50's "without changing how Claude Code itself works". Say so in the goal.

**N-e. The removal order asks its question too late** (lines 217, 608-615). Flow 7 runs logout and Keychain deletion before "asking about its history", so cancelling at that point leaves a logged-out profile in `deleting`. CUJ 8 confirms first. No alternative to the Trash is defined either, so one confirmation at the start would be enough. Also, `beginDelete`/`finishDelete` have no caller in S1 (line 1098), since `cpm remove` and the Remove button arrive in S5. Flow 7 also doesn't say what happens to `backups/<id>/`.

**N-f. Profile folders share a root with app folders** (lines 372-387, 276-277, 881). The one "idle" watcher on `~/.claude-profiles/` would, if recursive as FSEvents normally is, also see transcript and session writes for every added profile while the app is hidden. Reconcile must also exclude `bin`, `backups`, `logs` and `run` from "a folder with no row". This is cheap to fix before any `configDir` is created, using `~/.claude-profiles/profiles/<id>/`, and impossible to change afterwards without logging out.

**N-g. Account re-reads may be too frequent, and the text disagrees** (lines 279-280, 510-511, 1254-1255). File 24 can be read as parsing `.claude.json`, which the brief calls large, for every profile every 2 seconds, while flow 4 rechecks only live status. Re-read when the panel opens or when the file changes.

**N-h. Two zsh scripts share one logic block, with no shared file** (lines 421-438, file 16). `claude` and `claude-as` share nearly all their logic, and `profile-label` and `apply-default-env` also parse `names`. Use one script that branches on its invocation name, with a small shared reader.

**N-i. Check 20 has no decision attached** (line 716). A pass changes nothing, and the failure text says what happens anyway. State what passing would change, or remove the check.

## Status of Stage 5 findings

| Finding | Status | Note |
| --- | --- | --- |
| B1 copy into built-in | Resolved | Lines 110, 137, 559 |
| M1 Alternative I | Closed by author decision | Residual in N-c |
| M2 window title check | Addressed | Check 19 and a named fallback; outcome depends on the test |
| M3 S1 delivery | **Stands** | Major 5 |
| M4 multi-process writes | **Partly stands** | Major 4; the fix also introduced a gap |
| M5 version allow-list | **Stands, worse** | Major 7 |
| M6 existing tools | Resolved | Check 11 runs first; adopt, borrow or reject recorded |
| M7 S0 table | Resolved | One table; volume noted in Major 7 |
| M8 copy guarantees | **Partly stands** | Major 6 |
| M9 two backup locations | Resolved | Line 383 |
| M10 module boundaries | Mostly resolved | Rule and grep test fixed. Diagram still omits `StatusLineWriter`, `ClaudeBinaryLocator`, `TerminalOpener`, `FileWatcher`, `TranscriptReader`, `LiveSessionReader`, `ProcessInfoProvider`, `AtomicFile`. `ProcessRunner`'s rule (line 328) doesn't say how `DefaultEnvApplier`'s script, `Doctor`'s `launchctl getenv` (line 1152) or the `zsh -lic` read run |
| M11 Terminal opener | **Stands** | Major 8 |
| N1 full window | Resolved | Now a goal (line 72); still no CUJ, which is acceptable |
| N2 Keychain machinery | **Stands** | N-a |
| N3 watching and polling | Resolved | Lines 512, 881; recursion residual in N-f |
| N4 caching | Partly stands | Binary cache gone (line 1244). The transcript cache is still unmeasured but looks reasonable at the stated target; let the S2 measurement decide |
| N5 logging | Partly stands | Shell launches now logged. Plain logs, rotation and per-module `os_log` are still more than one user needs (lines 343-345, 1129-1133) |
| N6 secret detection | Partly stands | Full masking fixed. Heuristic remains (lines 572-573 vs 584, and Major 6). Line 1452 still says `Doctor` uses `SecretDetector`, but file 15 does not, so file 45 belongs in S4 |
| N7 trust grants, hook paths | Resolved | Lines 591-596 |
| N8 flow 1 shell errors | Resolved | The wrapper script removes them |
| N9 reach and API key | Resolved | Flow 5 table; warnings; login-shell `doctor` checks |
| N10 small unused items | Partly stands | `cpm names` still unused (lines 351, 1316 vs 1172). `createdAt` has no reader. `organizationName` and `lastError` are missing from lines 394-397. `setCommandName` goes beyond the goal's "display name only" (lines 62, 1094, 1291). The plugin-registry check remains (line 1156). `loggedOut` and the stray-file `doctor` issue are fixed |
| N11 altitude and stale text | Partly stands | Pseudocode remains in flows 1, 3 and 9, and the 250 ms interval at line 637 is still detail too low for this section. Flow 1's pseudocode is defensible because its order is a safety rule. Lines 954 and 1514-1519 describe a draft location and a move that are already done. Files 51-52 are missing from the stage table (lines 961-969). File 56 appears before file 55 |

## Suggested order
1. Fix the small, cheap items: the shell test and the `defaultId` gap (Majors 9 and 4).
2. Take Majors 1 and 2 to the author, since both touch accepted decisions. Major 2 removes the most complexity for the least loss.
3. Simplify the wrapper (Major 3), then fix S1 (Major 5).
4. Decide on folder layout (N-f) before S1, because it cannot change later without logging out.
5. Then simplify `CompatCheck`, copy setup and the opener (Majors 7, 6, 8).
