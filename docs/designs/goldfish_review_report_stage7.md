Status: resolved (auto accept, 2026-11-01: 34 fixes applied, 5 declined. Issue 2 was applied with one change: an override is a value outside `~/.claude-profiles/`, because a value inside it may be an inherited login-wide setting or a removed profile's old path.)

# Goldfish Annotated Review Report

**Target:** `claude-profile-manager.md` (`Lines 1-732`)

**Stage:** 7 (Critic Review), round 1. **Source findings:** `critic_report.md`, which holds the full discrepancy descriptions and rationale; each entry below summarises them.

**Result:** 34 PROPOSE FIX, 5 DECLINE, 0 CONTEST, 0 ESCALATE.

No finding was contested. All 24 Blocker and Major findings point at text that is actually in the document. All fixes below are text changes to the design; none has been tested.

**Two fixes set product behaviour rather than only repair text, so read them first:** `design-1-2` (the built-in profile becomes source-only for copy setup) and `design-1-7` (plain `claude` stops instead of falling back to the company account when its files are broken).

## Prioritized Findings & AI Suggestions

### [Issue 1] [`egm-structural`, `egm-design`] - Severity: High
*   **Affected Scope:** Lines 216-223, 397-410, 488-492, 511-529, 676-704 (no dedicated section)
*   **Discrepancy Description:** No Risks & Mitigations section and no scalability limits (`structural-1-1`).
*   **Rationale:** The standard requires failure modes, security implications and scalability limits. Line 53 promises "any number of profiles" with no tested limit.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** The standard is mandatory, and the design changes login-wide settings, shell files and Keychain items.
*   ***Proposed Edit:***
    *   Location: new H2 "Risks & Mitigations" after Non-Functional Requirements.
    *   Change: Add a table of risk, impact, mitigation and detection covering: wrong-account launch; login-wide variable reaching the desktop app and Cowork; Claude Code update changing file formats or the Keychain naming; copy racing a session; shell-file edits; Keychain deletion. Add "Limits": design target 10 profiles and 5,000 transcripts, panel open under 1 second at that size, none tested yet (S2 measures it). State that the row cache is in memory only. Add a Keychain-naming probe to CompatCheck: if an added profile's recorded `keychainService` no longer exists after an update, say that the naming may have changed, and give the recovery step (sign in again; old items are listed by `doctor`).

### [Issue 2] [`egm-design`, `egm-structural`] - Severity: Medium
*   **Affected Scope:** Lines 348-349, 434
*   **Discrepancy Description:** The wrapper overwrites an explicit `CLAUDE_CONFIG_DIR`, but the table says the caller's value wins (`design-1-1`).
*   **Rationale:** `CLAUDE_CONFIG_DIR=/x claude` moves to the default with no notice.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Introduced by the wrapper edit. The wrapper can tell a deliberate value from an inherited one: an inherited value always equals a registered `configDir`.
*   ***Proposed Edit:***
    *   Location: flow 1 pseudocode; flow 5 table row "A process that sets".
    *   Change: Add to flow 1: "if `CLAUDE_CONFIG_DIR` is set and matches no registered `configDir`: treat it as a deliberate override, keep it, print one warning on stderr, and skip profile selection". Table row becomes "A process that sets `CLAUDE_CONFIG_DIR` to a folder that is not a profile | No; its value wins, with a warning". Add a case to the shell tests.

### [Issue 3] [`egm-design`, `egm-structural`, `egm-style`] - Severity: Medium
*   **Affected Scope:** Lines 20-21, 56-57, 298-300, 450-453, 461-470, 629-633
*   **Discrepancy Description:** "Existing setup untouched" conflicts with copy setup targeting the built-in profile (`design-1-2`). Same as Stage 5 B1.
*   **Rationale:** An implementer cannot tell which rule wins.
*   ***AI Suggestion:*** PROPOSE FIX (sets product behaviour)
*   ***Justification:*** Source-only keeps the first promise true, matches the author's choice to keep `~/.claude` unwritten, and removes the riskiest write. **Cost:** you cannot copy another account's setup into your current account in v1.
*   ***Proposed Edit:***
    *   Location: Goals; Files on disk; flow 6; S4 file entries.
    *   Change: Goal heading becomes "The app never writes to `~/.claude` or `~/.claude.json`". Flow 6 adds "The built-in profile can be a source, never a target, in v1". Delete the built-in backup path and the `~/.claude.json` "written only by copy setup" note. Add "copy into the built-in profile" to Future Improvements.

### [Issue 4] [`egm-design`, `egm-style`] - Severity: Medium
*   **Affected Scope:** Lines 446-450, 454-455, 476-479, 699
*   **Discrepancy Description:** Restore can destroy later data, and "preview and result cannot differ" does not hold (`design-1-3`).
*   **Rationale:** Byte-for-byte Restore undoes what Claude Code wrote since. Added files stay. Install commands write outside the plan.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** The claim is false as written, and the failure loses user data.
*   ***Proposed Edit:***
    *   Location: flow 6; Goals; file 47 and file 44 entries.
    *   Change: Each copy records a manifest: files changed, files created, and a hash of each file before and after. `apply` re-checks the before-hash and stops if a file changed. Restore refuses during a live session, deletes created files, and restores a file only if its hash still equals the after-hash; otherwise it lists the file and leaves it alone. Replace "cannot differ" with two tiers: direct writes match the preview exactly; Claude Code's install commands show the intended change and cannot be undone by Restore (their manual removal is shown). If a step fails, stop, keep the backup and report which items were done.

### [Issue 5] [`egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 406-407, 449, 481
*   **Discrepancy Description:** The desktop-app and Cowork filter could weaken live-session checks (`design-1-4`).
*   **Rationale:** A copy could write while those processes use the same config.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** One sentence removes the ambiguity.
*   ***Proposed Edit:***
    *   Location: Terms; flows 4, 6, 7.
    *   Change: Add to Terms: "Live session: any record in a profile's `sessions/*.json` whose process is alive, whatever its `entrypoint`. The display filter never applies to safety checks." Use it in copy, Restore and removal.

### [Issue 6] [`egm-design`, `egm-style`] - Severity: Medium
*   **Affected Scope:** Lines 76-81, 127-128, 157, 351, 631-633
*   **Discrepancy Description:** The window title may not persist, and it is the built-in profile's only label (`design-1-5`).
*   **Rationale:** Claude Code may overwrite it. The reviewer's recollection is supported: the installed 2.1.296 binary contains `CLAUDE_CODE_DISABLE_TERMINAL_TITLE`.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Without this the author's chosen label may show nothing.
*   ***Proposed Edit:***
    *   Location: flow 1, flow 2, S0 table, Goals.
    *   Change: Wrapper and `.command` files set `CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1` before `exec`. Add S0 check: title stays for a full session, including after tool use. If it fails: return to the author with Alternative I (`--settings`) as the fallback.

### [Issue 7] [`egm-design`, `egm-structural`] - Severity: Medium
*   **Affected Scope:** Lines 315, 359-361, 372-376, 529
*   **Discrepancy Description:** App path discovery likely finds the wrapper rather than the real binary (`design-1-6`).
*   **Rationale:** `~/.zshenv` puts the wrapper first; the cached path is never refreshed.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Introduced by the wrapper edit, and the "keeps working after a Homebrew update" claim depends on it.
*   ***Proposed Edit:***
    *   Location: flow 2; flow 1 note; file 23.
    *   Change: One rule shared by wrapper and app: search each `PATH` entry, skip any that resolves to the `~/.claude-profiles/bin` folder, and use the first `claude` found. The app gets `PATH` from `/bin/zsh -lic 'print -r -- $PATH'` so `.zshrc` entries count. Resolve at every launch, with no cache across launches. Not found: show an error and stop. Doctor and `--version` use this real binary. Add to S0 check 14.

### [Issue 8] [`egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 41-42, 340-342
*   **Discrepancy Description:** A broken `default` or `names` file falls back to the built-in company account, even for `claude-as` (`design-1-7`).
*   **Rationale:** Recreates the wrong-account problem, with only a stderr line.
*   ***AI Suggestion:*** PROPOSE FIX (sets product behaviour)
*   ***Justification:*** Your requirement is that the default be followed; silently using the company account is the failure this tool exists to prevent. **Cost:** if those files are damaged, plain `claude` stops working until you run `cpm doctor` or reinstall.
*   ***Proposed Edit:***
    *   Location: flow 1.
    *   Change: Replace the fallback with: "`claude-as`: cannot resolve the name → list valid names, exit 1. Plain `claude`: `default` missing, unreadable, or holding an ID not in `names` → print the problem and the fix (`cpm doctor`), exit 1, and do not start. To run anyway, call `claude-as <name>` or the real binary." Before install, the wrapper is not on `PATH`, so this does not affect a fresh machine.

### [Issue 9] [`egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 67-69, 166-167, 343-346, 372, 521
*   **Discrepancy Description:** Resume has five undefined failure modes (`design-1-8`).
*   **Rationale:** Two processes writing one transcript; missing folder; refusal message lacks a folder; `--resume=<sid>` bypasses the guard; `<sid>` unvalidated.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Each is a concrete failure on a named goal.
*   ***Proposed Edit:***
    *   Location: flows 1 and 2; S0 check 5.
    *   Change: Already running → the app shows Stop, not Resume, and the wrapper refuses with the PID. Folder missing → the `.command` stops with the message and does not continue elsewhere; the app offers "Choose folder". Refusal message adds `cd <folder> &&`. Match `--resume <sid>`, `--resume=<sid>`, `-r <sid>`. `<sid>` must match the UUID format to be checked, otherwise pass through to Claude Code. Extend check 5: resume from a different working folder, and whether the session ID changes.

### [Issue 10] [`egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 142, 285-293, 306, 315-318, 695-703
*   **Discrepancy Description:** No install, upgrade or first-run flow for shell files and the LaunchAgent (`design-1-9`).
*   **Rationale:** These edit the user's dotfiles; file 31 has part of it, but the spec has no flow and `cpm` has no place on `PATH`.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Stage 6 raised this too (finding 13).
*   ***Proposed Edit:***
    *   Location: new flow 10; files 31 and 35; Files on disk.
    *   Change: `make-app.sh --install` puts the app in `~/Applications`; the app and `cpm` carry the shell files as bundle resources. `cpm` lives at `~/.claude-profiles/bin/cpm`, copied by the app's "Set up shell" step or `make-app.sh`. Order: create `~/.claude-profiles` with the built-in row and `default`, install `bin/`, load the LaunchAgent, then show the two lines for the user to add, with `doctor` confirming. Install is safe to repeat. On upgrade, the app replaces `bin/` when its bundled version differs.

### [Issue 11] [`egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 432, 695-704
*   **Discrepancy Description:** Rollback and uninstall do not restore "exactly as before" (`design-1-10`).
*   **Rationale:** Running apps keep the old value, which then points at a trashed folder.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Introduced by the login-wide setting.
*   ***Proposed Edit:***
    *   Location: NFR Rollout & Rollback; `cpm uninstall`.
    *   Change: Uninstall refuses while any added profile has a live session. Ends by telling the user to log out and back in, or restart Terminal.app and other open apps. The `~/.zshrc` line tests for the file before sourcing. Replace "exactly as before" with "after you log out and back in".

### [Issue 12] [`egm-design`, `egm-structural`] - Severity: Medium
*   **Affected Scope:** Lines 265, 323-324, 388-394, 481-486
*   **Discrepancy Description:** Profile states and reconcile are named, not defined (`design-1-11`).
*   **Rationale:** `/logout` then `/login` inside a profile leaves a stale label. Removal changes the default last.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** A tool that labels accounts must not show the wrong one. `claude auth status --json` exists in 2.1.296 and may be a better source than `.claude.json`.
*   ***Proposed Edit:***
    *   Location: new state table in the Technical Plan; flow 7; flow 4.
    *   Change: State table with transitions: `pendingLogin` → `ready` (login found) or discarded; `ready` ↔ `loggedOut` (`oauthAccount` gone or back); `ready` → `deleting` → removed; any → `error` with a recovery action. Reconcile: folder without a row is listed in `doctor`, never deleted; row without a folder → `error`. The account is read from the config file at every panel refresh; a change updates the label and shows a notice. Flow 7 order: move the default first, then logout, Keychain, Trash, row. A crash in `deleting` resumes on next start. Add to S0: does `claude auth status --json` give the account under a custom folder.

### [Issue 13] [`egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 271-273, 388-391, 483, 516-523
*   **Discrepancy Description:** Keychain listing and deletion are assumed, login detection has a race, and nothing bars deleting the built-in login item (`design-1-12`).
*   **Rationale:** A wrong delete logs out the company account.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** File 11 already refuses the unsuffixed name, but lines 1-732 do not state it. The other two gaps are real.
*   ***Proposed Edit:***
    *   Location: flow 3 step 6; flow 7; S0 table.
    *   Change: Step 6 polls for up to 10 seconds before marking `error`. Flow 7 states: "The plain `Claude Code-credentials` item is never deleted." Add S0 check: a self-signed, unsandboxed app can list these names without a prompt and delete an item created by `claude`; if not, removal prints the manual step.

### [Issue 14] [`egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 450-457, 465-470, 677-682
*   **Discrepancy Description:** Backups have no modes or retention, secret scanning is partial, and copied hooks can depend on the source folder (`design-1-13`).
*   **Rationale:** Backups hold whole config files, which may hold MCP secrets.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Also resolves Stage 5 M9 and N7.
*   ***Proposed Edit:***
    *   Location: flow 6; NFR Security; files 45, 47.
    *   Change: All backups go to `~/.claude-profiles/backups/<id>/<time>/`, folders 0700 and files 0600, keeping the last 10 per profile. Reword the Security line: "parses only these keys; backups and rewrites copy whole files". Scan skills, agents, commands, hooks and `CLAUDE.md` for secrets; a match is unticked. A hook or MCP command pointing into the source folder is flagged and unticked.

### [Issue 15] [`egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 144-145, 304, 351, 368-369, 382
*   **Discrepancy Description:** Names and quoted values have no validation rules (`design-1-14`).
*   **Rationale:** Folder paths from transcripts go into an executable `.command` file, which is an injection risk.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Transcript content is untrusted input to a file that gets run.
*   ***Proposed Edit:***
    *   Location: Terms or Files on disk; flow 2; files 6, 22, 42.
    *   Change: `commandName`: `[a-z0-9-]{1,24}`, unique, not `builtin`. `displayName`: 1 to 40 characters, no tabs, newlines or control characters. `.command` values: wrapped in single quotes, with each `'` written as `'\''`; all other values come from the registry. Tests use a folder containing an apostrophe, a newline and `$(...)`.

### [Issue 16] [`egm-structural`, `egm-style`, `egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 84, 472-473, 683-684
*   **Discrepancy Description:** Trust grants are both never copyable and copyable if ticked (`structural-1-2`).
*   **Rationale:** My flow 6 edit missed the NFR line.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Direct contradiction in a security control.
*   ***Proposed Edit:***
    *   Location: line 684; line 84.
    *   Change: "Trust grants are never copied." Define: `env`, `headers` and `args` values are masked secrets that can be ticked; login credentials and tokens stored by Claude Code can never be ticked.

### [Issue 17] [`egm-structural`, `egm-style`, `egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 238-243, 245-253, 283-293, 123, 495, 555, 700
*   **Discrepancy Description:** The diagram and module lists do not match the flows (`structural-1-3`).
*   **Rationale:** They still show the rejected zsh-function approach.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Stale from the wrapper edit.
*   ***Proposed Edit:***
    *   Location: Components.
    *   Change: Redraw the diagram with `bin/` scripts, `apply-default-env`, the LaunchAgent and `launchctl`. Keychain arrow reads "names; delete recorded item". List all modules including `SessionStopper`, `Doctor`, `AccountReader`, `ClaudeCLI`, `DefaultEnvApplier`. List all `cpm` commands. Fix Alternative A's "bottom layer" and line 555.

### [Issue 18] [`egm-structural`, `egm-style`, `egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 289, 297-319, 452, 505, 681
*   **Discrepancy Description:** The Files on disk layout is incomplete (`structural-1-4`).
*   **Rationale:** `claude-profiles.zsh`, backups and `profile-label` lack a place or description.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** The tree should be the full inventory.
*   ***Proposed Edit:***
    *   Location: Files on disk; Components.
    *   Change: Add `claude-profiles.zsh` under `bin/`, `backups/<id>/<time>/`, modes for every file, and `cpm` in `bin/`. Describe `profile-label`: run by the status line with the profile ID, reads the display name from `names`, so a rename needs no settings change. Add stops to the logged events.

### [Issue 19] [`egm-structural`, `egm-style`, `egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 107-109, 268-269, 276-277, 293, 375, 383-384, 454, 477-478, 482, 489
*   **Discrepancy Description:** "Only module" rules are contradicted by the flows (`structural-1-5`).
*   **Rationale:** Same as Stage 5 M10 and Stage 6 findings 4, 10, 11.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Raised independently in three stages.
*   ***Proposed Edit:***
    *   Location: Components; Non-Goals; file 8; file 37; verification plan.
    *   Change: Add `ProcessRunner`, the one place that runs Claude Code or `launchctl`, taking its environment from ConfigLocator. Launcher is "the only module that opens Terminal sessions". EventLog alone writes logs. Non-Goals: "copy setup and the `statusLine` written when a profile is created are the only writes". Replace the source-text search test with tests on `LaunchEnv` and on generated `.command` contents.

### [Issue 20] [`egm-structural`, `egm-style`, `egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 114-119, 405-406, 429, 499, 502-503, 516-529, 577-581
*   **Discrepancy Description:** S0 checks are inconsistent and incomplete (`structural-1-6`).
*   **Rationale:** Numbering gaps; check 11 is missing though it backs Alternative C's rejection; the exit rule disagrees with the fallbacks (Stage 5 M7, Stage 6 finding 1).
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Most repeated finding in the review so far.
*   ***Proposed Edit:***
    *   Location: S0 table; Open Questions; Alternative C; file 2.
    *   Change: One numbered table is the only authority, with a "Stops the project?" column; the exit rule is deleted. Stops the project: 1, 2, 3 only. Add rows for: status line; existing tools (adopt, borrow or reject, done first and time-boxed); `procStart`; `entrypoint` values; `status` values; Terminal.app inheriting the login-wide value; SIGINT and SIGTERM stopping a session cleanly; plus the new checks from issues 6, 12 and 13. Alternative C's rejection is marked provisional until that check is done.

### [Issue 21] [`egm-structural`, `egm-style`, `egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 11-18, 51-89, 123, 138-200, 494-509
*   **Discrepancy Description:** Stop session is v1 scope but has no goal, Overview mention or CUJ (`structural-1-7`).
*   **Rationale:** It is the feature that signals live processes.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** The Stop decision was added after those sections were approved.
*   ***Proposed Edit:***
    *   Location: Overview; Goals; CUJs.
    *   Change: Mention it in the Overview. Add a Goal. Add CUJ 7 (stop a busy session) and CUJ 8 (remove a profile, including the default). Add the full window to Goals, resolving Stage 5 N1.

### [Issue 22] [`egm-style`, `egm-design`, `egm-structural`] - Severity: Medium
*   **Affected Scope:** Lines 21-23, 70-75, 174-176, 425-435, 653
*   **Discrepancy Description:** Coverage claims overstate what the default reaches (`style-1-1`).
*   **Rationale:** "Every shell and script" is broader than the table.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Your requirement is reach; the doc should state accurately how far it goes.
*   ***Proposed Edit:***
    *   Location: Overview; Goal heading; CUJ 4; flow 5 table; S0 check 14.
    *   Change: Goal becomes "Plain `claude` follows the default wherever the wrapper or the login-wide setting reaches it", pointing to the table. Add rows: cron and launchd jobs (no wrapper, and only some get the setting); `bash -l` after `path_helper`; a shell alias for `claude`. Replace "the first moments after login" with "before the LaunchAgent has run". Add the conditional to CUJ 4.

### [Issue 23] [`egm-style`, `egm-structural`] - Severity: Medium
*   **Affected Scope:** Lines 53, 85-86, 314, 409-410, 518, 688-690
*   **Discrepancy Description:** Other absolute claims are contradicted or not measurable (`style-1-2`).
*   **Rationale:** "Logs of every launch", "no background work", "any number of profiles".
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Also resolves Stage 5 N3 and N5.
*   ***Proposed Edit:***
    *   Location: Goals; flows 4 and 5; NFRs.
    *   Change: List what the logs cover, and add one log line to the wrappers so launches from the shell count. Use "no polling while hidden". Keep one watcher on `~/.claude-profiles/` while hidden and remove the watchers on `sessions/`. Profiles: "no fixed limit; tested with three or more; design target in Limits".

### [Issue 24] [`egm-style`, `egm-structural`, `egm-design`] - Severity: Medium
*   **Affected Scope:** Lines 25, 77, 81, 114, 204-226, 263, 439, 556, 564, 623, 642, 670, 710-711, 726
*   **Discrepancy Description:** Undefined references block a fresh reader (`style-1-3`).
*   **Rationale:** S0, S1, S6, decision numbers, "the shell command", "wrapper", "login-wide setting".
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** "Do not contest a comprehension gap."
*   ***Proposed Edit:***
    *   Location: Terms; each citation.
    *   Change: Add to Terms: stage names S0 to S6, wrapper, login-wide setting, live session, `cpm`, Cowork, Remote Control. Replace each "decision N" with its content or a relative link to `concepts_brief.md`. Replace "the shell command" with the command's name. Remove "Round 1 reviewers", "the success test" and "Goldfish".

### [Issue 25] [`egm-design`] - Severity: Low
*   **Affected Scope:** Lines 134-135, 199, 347, 528
*   **Discrepancy Description:** Only `ANTHROPIC_API_KEY` gets an auth warning (`design-1-15`).
*   **Rationale:** Other variables may override the login.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Checked: `ANTHROPIC_AUTH_TOKEN` and `CLAUDE_CODE_OAUTH_TOKEN` both appear in the 2.1.296 binary, though what they do has not been tested.
*   ***Proposed Edit:***
    *   Location: flow 1; `doctor`; S0 check 13.
    *   Change: Warn on, and report in `doctor`, all three variables. None is ever removed. Check 13 tests all three.

### [Issue 26] [`egm-structural`, `egm-style`] - Severity: Low
*   **Affected Scope:** Lines 74, 80, 112-136, 346, 365, 494, 632, 654
*   **Discrepancy Description:** Open Questions is mostly closed decisions (`structural-1-8`).
*   **Rationale:** Repeats the body and has already allowed drift.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Low cost, and it removes copies of decisions.
*   ***Proposed Edit:***
    *   Location: Open Questions.
    *   Change: Move resolved items to a "Decisions" section, one line each. Keep only the S0 pointer open.

### [Issue 27] [`egm-structural`, `egm-style`] - Severity: Low
*   **Affected Scope:** Lines 625-670, 708, 720
*   **Discrepancy Description:** Heading levels and alternative order are off (`structural-1-9`).
*   **Rationale:** Outline reads wrongly.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Mechanical.
*   ***Proposed Edit:***
    *   Location: as listed.
    *   Change: Promote both to H2. Move Alternative J before K, keeping the letters.

### [Issue 28] [`egm-structural`] - Severity: Low
*   **Affected Scope:** Lines 330, 368, 379, 397, 412, 423, 443, 481, 488, 494
*   **Discrepancy Description:** Flows use bold text, not headings (`structural-1-10`).
*   **Rationale:** They are cited throughout.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Mechanical, and it helps navigation in a long document.
*   ***Proposed Edit:***
    *   Location: each flow.
    *   Change: Make each flow an H4.

### [Issue 29] [`egm-structural`, `egm-style`] - Severity: Low
*   **Affected Scope:** Lines 231-232, 322, 372, 703
*   **Discrepancy Description:** Line wrapping and diagram alignment (`structural-1-11`).
*   **Rationale:** Cosmetic.
*   ***AI Suggestion:*** DECLINE
*   ***Justification:*** The diagram is being redrawn in issue 17; wrapping has no effect on meaning.
*   ***Proposed Edit:*** None.

### [Issue 30] [`egm-structural`] - Severity: Low
*   **Affected Scope:** Lines 25, 710-715
*   **Discrepancy Description:** External references cannot be followed (`structural-1-12`).
*   **Rationale:** No path to the brief; the mock is private.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Relative links are trivial. The doc already describes the panel in flow 4 and the CUJs.
*   ***Proposed Edit:***
    *   Location: line 25; Additional Resources.
    *   Change: Use relative links to `concepts_brief.md` and `concept_input.md`.

### [Issue 31] [`egm-structural`] - Severity: Low
*   **Affected Scope:** Lines 85, 195-200
*   **Discrepancy Description:** `doctor` and the health line are not specified (`structural-1-13`).
*   **Rationale:** Only CUJ 6 describes them.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Issues 2, 11, 12 and 25 add checks, so a single list is needed.
*   ***Proposed Edit:***
    *   Location: new flow 11.
    *   Change: One list of checks with outputs, including the per-profile health line. Treat the existing `~/.claude/.claude.json` as a note, not a fault, so a clean setup passes (Stage 5 N10).

### [Issue 32] [`egm-style`, `egm-structural`] - Severity: Low
*   **Affected Scope:** Lines 80, 127-128, 133, 631-633, 644
*   **Discrepancy Description:** Decisions are cited by rejected alternatives and an undefined "option A" (`style-1-4`).
*   **Rationale:** "Option A" reads as Alternative A. Alternative I has no technical rejection reason.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Honest record, and Stage 5 M1 raised the same point.
*   ***Proposed Edit:***
    *   Location: as listed.
    *   Change: Write "keep `~/.claude` unwritten" in place of "option A". Alternative I's reason: "Passing a settings file at each launch was not chosen in favour of the window title, on the basis that sessions started outside the wrapper would show no label. It is the named fallback if the title check fails."

### [Issue 33] [`egm-style`, `egm-structural`, `egm-design`] - Severity: Low
*   **Affected Scope:** Lines 76-77, 104, 155-159
*   **Discrepancy Description:** CUJ 2's outcome does not fit one trigger (`style-1-5`).
*   **Rationale:** Typing a command does not open Terminal.app.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** A one-line correction to a test case.
*   ***Proposed Edit:***
    *   Location: CUJ 2; line 77.
    *   Change: Split the outcome by trigger. Line 77: "windows running `claude` or `claude-as`".

### [Issue 34] [`egm-style`, `egm-structural`, `egm-design`] - Severity: Low
*   **Affected Scope:** Line 72
*   **Discrepancy Description:** Grammar error (`style-1-6`).
*   **Rationale:** The verb has no clear subject.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Rewritten as part of issue 22.
*   ***Proposed Edit:***
    *   Location: line 72.
    *   Change: "Both work when the app is not running."

### [Issue 35] [`egm-style`] - Severity: Low
*   **Affected Scope:** Lines 11, 238, 283, 291, 550, 564, 588, 596-597, 601
*   **Discrepancy Description:** Unquantified size and comparison words (`style-1-7`).
*   **Rationale:** "Small", "fairly small", "less in between".
*   ***AI Suggestion:*** DECLINE
*   ***Justification:*** These do not change any decision. The one that matters, line 601, is covered by keeping the concrete reason in Alternative E.
*   ***Proposed Edit:*** None.

### [Issue 36] [`egm-style`] - Severity: Low
*   **Affected Scope:** Lines 34, 53, 60, 87, 200, 439
*   **Discrepancy Description:** Slogans and subjective phrasing (`style-1-8`).
*   **Rationale:** "Slow and breaks flow", "can own the code".
*   ***AI Suggestion:*** DECLINE
*   ***Justification:*** Goal headings are short labels with precise text beneath them. "Causes harm" is fixed by issue 22's edits to that passage.
*   ***Proposed Edit:*** None.

### [Issue 37] [`egm-style`] - Severity: Low
*   **Affected Scope:** Lines 20-23, 70-75, 134-135, 174-176, 285-288, 330-333, 359-361, 363-366, 371, 412-421
*   **Discrepancy Description:** Repeated descriptions (`style-1-9`).
*   **Rationale:** Copies have already drifted.
*   ***AI Suggestion:*** DECLINE
*   ***Justification:*** Issues 17, 22 and 26 remove the copies that drifted. A full de-duplication pass would churn text just before the remaining stages.
*   ***Proposed Edit:*** None.

### [Issue 38] [`egm-style`] - Severity: Low
*   **Affected Scope:** Lines 105, 148, 216-220, 365-366, 716-718
*   **Discrepancy Description:** Wording presumes outcomes or is ambiguous (`style-1-10`).
*   **Rationale:** "S0 check 13 confirms the wording" presumes the result; the source of the hash claim is unclear.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** The source of the hash claim matters, because the design can fail on it.
*   ***Proposed Edit:***
    *   Location: as listed.
    *   Change: "S0 check 13 tests the wording". Attach "(these need AppleScript)" to colour themes only. "Asks whether to keep the login". Add source: "from reading the code in the installed 2.1.296 binary, not from documentation".

### [Issue 39] [`egm-style`, `egm-structural`, `egm-design`] - Severity: Low
*   **Affected Scope:** Lines 311, 450
*   **Discrepancy Description:** `.cam-backups` matches no other name (`style-1-11`).
*   **Rationale:** Leftover name.
*   ***AI Suggestion:*** DECLINE
*   ***Justification:*** Issue 14 removes this folder entirely.
*   ***Proposed Edit:*** None.
