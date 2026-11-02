# Goldfish Review Report

**Target:** `docs/designs/claude-profile-manager.md` (`Lines 1-732`)

## Reviewer Status

| Reviewer | Status |
| --- | --- |
| `egm-design` | completed |
| `egm-structural` | completed |
| `egm-style` | completed |

No reviewer needed a retry.

**Severity breakdown:** 1 Blocker, 23 Major, 15 Nit (39 findings after deduplication).

## Review Limits

*   **Scope:** Lines 733 onward were not reviewed. Findings about missing content, such as the install flow and the S0 definitions, may be partly answered in "Detailed Implementation Plan". The spec section still has the gap as written.
*   **Not run or opened:** Reviewers could not run commands or open URLs. All Claude Code, `launchctl`, FSEvents, Keychain and macOS behaviour is unverified, as is the clickable mock. Findings that depend on this behaviour are risks to test, not confirmed defects.
*   **Not supplied:** `concepts_brief.md` was not provided, so decisions 1 to 18 and the "success test" are unchecked.
*   **Sensitive data:** No reviewer reported reading credential values, Keychain contents or `.key` files, and the synthesizer read none.
*   **Checks made during synthesis:** The findings' main claims and line references were compared with the target and the standard. One line reference was corrected, and one cited conflict was removed because CUJ 5 uses the built-in profile as the source, not the target.

**Severity decisions where reviewers disagreed or the rating changed:**

| Finding | Reviewer ratings | Final | Reason |
| --- | --- | --- | --- |
| `structural-1-1` | structural Blocker, design Major | Blocker | The standard says "must", and the spec states no scalability limit. The structural reviewer noted that the scattered risk content could justify Major on its own, but the scalability gap stands either way. |
| `structural-1-2` | structural Major, style Major, design Nit | Major | It is a direct contradiction in a security control. |
| `structural-1-3` | structural Major, style Major, design Nit | Major | The diagram describes an approach that Alternative L rejects. |
| `structural-1-5` | structural Major, style Major, design Nit | Major | These statements set the architecture's module boundaries. |
| `structural-1-6` | structural Major, style Major, design Nit | Major | Alternative C's rejection relies on a check that has no row. |
| `style-1-1` | style Major, design Nit | Major | The Goals promise more than the design's own table delivers. |
| `design-1-14` | design Nit | Major | Raised by the synthesizer. Unvalidated folder paths are written into an executable `.command` file, so this is a launch failure and an injection risk. |
| `style-1-4` | style Major, structural Nit | Nit | Lowered by the synthesizer. Citing an alternative by letter does lead to the recorded decision, and both reviewers who assessed the Alternatives Rule said the section meets it. Only the wording needs fixing. |

## Prioritized Findings (Blockers -> Major -> Nits)

### No Risks & Mitigations section and no scalability limits - Severity: Blocker
*   **ID:** `structural-1-1`
*   **Reviewer Lens:** `egm-structural`, `egm-design`
*   **Affected Scope:** `Lines 216-223, 397-410, 488-492, 511-529, 676-704` (no dedicated section exists)
*   **Discrepancy Description**: The standard requires the spec to identify failure modes, security implications, scalability limits and their mitigations. This design changes the login-wide environment, edits shell startup files, deletes Keychain items and signals processes, but its risk content is scattered across the flows, the S0 table and the NFRs. No limit or target is stated for profile count, transcript count or size, watcher count, or cold panel open, even though line 53 promises "any number of profiles". The spec does not say whether the row cache is held in memory or on disk, and no cache file is listed. It is also unproven that a row needs only a partial read, because Claude Code's title may sit anywhere in a large file. The largest drift risk has no recovery plan: if an update changes the Keychain name hash, every added profile could look logged out, and CompatCheck does not probe login names.
*   **Rationale & Recommendation**: The standard's language is mandatory. Add an H2 "Risks & Mitigations" table with risk, impact, mitigation and detection. Cover at least a wrong-account launch, the login-wide variable reaching the desktop app and Cowork, file-format and Keychain-naming changes in updates, a copy racing a session, shell-file edits and Keychain deletion. State tested limits or say that none were tested, set a panel-time target at N transcripts, add a Keychain-naming probe to CompatCheck, and define the recovery procedure.

### The wrapper overwrites an explicit `CLAUDE_CONFIG_DIR` - Severity: Major
*   **ID:** `design-1-1`
*   **Reviewer Lens:** `egm-design`, `egm-structural`
*   **Affected Scope:** `Lines 348-349, 434`
*   **Discrepancy Description**: The coverage table says a process that sets `CLAUDE_CONFIG_DIR` itself keeps its own value. The wrapper pseudocode always unsets or exports the variable, so `CLAUDE_CONFIG_DIR=/x claude` silently moves to the default profile. The wrapper also cannot tell a deliberate value from one inherited from `launchctl setenv`, so honouring any set value would break default switching.
*   **Rationale & Recommendation**: Decide the behaviour and make the table and pseudocode match. Either limit the row to full-path calls, or define how a deliberate override is detected, for example a value that differs from the login-wide value and from every registered `configDir`. Add a test for it.

### The "existing setup is untouched" promise conflicts with copy setup - Severity: Major
*   **ID:** `design-1-2`
*   **Reviewer Lens:** `egm-design`, `egm-structural`, `egm-style`
*   **Affected Scope:** `Lines 20-21, 56-57, 298-300, 450-453, 461-470, 629-633`
*   **Discrepancy Description**: The doc says `~/.claude` is "not moved or changed", "left exactly as they are" and "never written by the app", and Alternative H is rejected to keep it unwritten. Lines 299-300 and 451-453 expect the built-in profile to be a copy target, and the copy table writes `settings.json`, `skills/`, `agents/`, `commands/` and `CLAUDE.md`, all of which would land in `~/.claude`. "The existing setup is safe" is also a judgement, not a testable property.
*   **Rationale & Recommendation**: Choose one rule. Either refuse the built-in profile as a copy target in v1 and delete the exception text, or restate the guarantee everywhere as "written only by an explicit, backed-up copy". Reword the Goal heading as a property that can be checked.

### Restore can destroy later data and "preview equals result" does not hold - Severity: Major
*   **ID:** `design-1-3`
*   **Reviewer Lens:** `egm-design`, `egm-style`
*   **Affected Scope:** `Lines 446-450, 454-455, 476-479, 699`
*   **Discrepancy Description**: A byte-for-byte Restore made days later reverts everything Claude Code wrote to that file since, not just the copied keys. The live-session refusal is stated for copy, not Restore. Files marked Add have no backup and are not removed. Claude Code's install commands may also write outside the plan, for example plugin caches and registries. "The preview and the result cannot differ" is unsupported, because the target can change between `plan` and `apply`, a session can start after the check because nothing holds a lock, and install commands can fail partway with no defined outcome.
*   **Rationale & Recommendation**: Record a manifest for each copy, including created files and a before-state hash. Revalidate hashes at `apply`. Make Restore refuse on live sessions, and warn or do a key-level revert if a file has changed. Define partial-failure behaviour and weaken the "cannot differ" claim.

### The desktop-app and Cowork filter could weaken live-session checks - Severity: Major
*   **ID:** `design-1-4`
*   **Reviewer Lens:** `egm-design`
*   **Affected Scope:** `Lines 406-407, 449, 481`
*   **Discrepancy Description**: Records for the desktop app and Cowork are filtered out of the session list. If the copy refusal reuses that filtered view, a copy could write while those processes use the same config. "Live session" is not defined for copy, removal or Restore.
*   **Rationale & Recommendation**: Base the safety checks on unfiltered live records and say so in the spec.

### The window title may not persist, and it is the built-in profile's only label - Severity: Major
*   **ID:** `design-1-5`
*   **Reviewer Lens:** `egm-design`, `egm-style`
*   **Affected Scope:** `Lines 76-81, 127-128, 157, 351, 631-633`
*   **Discrepancy Description**: The wrapper sets the title once and then `exec`s Claude Code. If Claude Code sets its own terminal title (recalled by the reviewer, not verified), the label is overwritten. The built-in profile holds the company account and has no status line, so wrong-account work (problem 3) could stay unsolved for the most sensitive account. No S0 check covers this.
*   **Rationale & Recommendation**: Add an S0 check that the title survives a full session. Name a fallback, such as a Claude Code setting that disables title changes if one exists, or the `--settings` route in Alternative I. Reword the goal if the title cannot hold.

### App path discovery likely finds the wrapper, not the real binary - Severity: Major
*   **ID:** `design-1-6`
*   **Reviewer Lens:** `egm-design`, `egm-structural`
*   **Affected Scope:** `Lines 315, 359-361, 372-376, 529`
*   **Discrepancy Description**: `/bin/zsh -lc 'command -v claude'` runs `~/.zshenv`, which puts the wrapper first on `PATH`, so the "full path" is usually the wrapper. A later `.zprofile` step could reorder `PATH` and return the real binary instead, so the result depends on check 14. The flow does not say which one app launches should use, and `doctor` and `claude --version` may report on the wrong program. `-lc` skips `.zshrc`, so a `claude` added to `PATH` there may not be found. A path cached "once" has no invalidation, which conflicts with the "keeps working after a Homebrew update" reasoning. This comes from zsh startup-file rules and was not run.
*   **Rationale & Recommendation**: State whether the app launches the wrapper or the real binary. Use one resolution rule shared with flow 1, re-resolve at each launch or on failure, and cover "not found" and self-recursion when `PATH` spells the bin folder differently. Add this to the S0 table.

### A broken `default` or `names` file falls back to the wrong account - Severity: Major
*   **ID:** `design-1-7`
*   **Reviewer Lens:** `egm-design`
*   **Affected Scope:** `Lines 41-42, 340-342`
*   **Discrepancy Description**: "Missing or broken file → warn, use the built-in profile" applies to both commands. `claude-as personal` with a damaged `names` file could start on the company account with only a stderr line, which scripts may hide. This recreates the problem the tool exists to solve and conflicts with the exit 1 for an unknown name. A `default` holding an ID missing from `names` is not defined.
*   **Rationale & Recommendation**: `claude-as` must exit non-zero whenever it cannot resolve the name. For plain `claude`, make the fallback an explicit decision and consider failing closed when not on a terminal. Define the stale-ID case.

### Resume has several undefined failure modes - Severity: Major
*   **ID:** `design-1-8`
*   **Reviewer Lens:** `egm-design`
*   **Affected Scope:** `Lines 67-69, 166-167, 343-346, 372, 521`
*   **Discrepancy Description**: Five cases are undefined.
    *   Resuming a session that is already running could give two processes writing one transcript, and nothing refuses or warns.
    *   No behaviour is defined when the original folder was deleted or moved, and a failed `cd` must not continue in the wrong folder.
    *   The refusal message gives a command but not where to run it, which matters if resume by ID depends on the working folder (unverified). Check 5 covers only the custom config folder.
    *   The argument match misses `--resume=<sid>` and non-ID resume terms, so the ownership guard can be bypassed.
    *   `<sid>` goes into a path lookup with no format validation.
*   **Rationale & Recommendation**: Specify each case. Extend check 5 to cover a different working folder and whether a resume keeps the same session ID.

### Install, upgrade and first-run steps are missing - Severity: Major
*   **ID:** `design-1-9`
*   **Reviewer Lens:** `egm-design`
*   **Affected Scope:** `Lines 142, 285-293, 306, 315-318, 695-703`
*   **Discrepancy Description**: No flow says who edits `~/.zshenv` and `~/.zshrc`, installs `bin/`, or loads the LaunchAgent. It also does not say when consent is asked for, how repeat installs avoid duplicate lines, or how scripts are updated on upgrade. CUJ 1 starts from "the app is installed". `cpm` is not in the `bin/` list, so its place on `PATH` is undefined, and `~/.zshenv` does not appear in Components. These are edits to the user's dotfiles in a design whose first promise is safety.
*   **Rationale & Recommendation**: Add an install and upgrade flow, with `cpm install` or an equivalent, and cover it in `doctor`.

### Rollback and uninstall do not restore "exactly as before" - Severity: Major
*   **ID:** `design-1-10`
*   **Reviewer Lens:** `egm-design`
*   **Affected Scope:** `Lines 432, 695-704`
*   **Discrepancy Description**: The design's own table says login-wide changes do not reach running apps, so after rollback, new windows of a running Terminal.app keep the old `CLAUDE_CONFIG_DIR`, and with no wrapper any `claude` there uses that profile. After `cpm uninstall` that path is in the Trash, and Claude Code may recreate an empty, logged-out folder (unverified). Uninstall does not refuse on live sessions. The `.zshrc` line remains after `~/.claude-profiles` is trashed, so shells may report a missing file unless the line is guarded.
*   **Rationale & Recommendation**: Tell the user to restart running apps or log out, refuse uninstall while sessions are live, guard the source line, and have `doctor` or uninstall detect a stale value.

### Profile states and reconcile are named but not defined - Severity: Major
*   **ID:** `design-1-11`
*   **Reviewer Lens:** `egm-design`, `egm-structural`
*   **Affected Scope:** `Lines 265, 323-324, 388-394, 481-486`
*   **Discrepancy Description**: "Reconcile on start" has no specification. There are no transitions for `loggedOut`, `deleting` or `error`, and no way to detect logout is described. Running `/logout` then `/login` with another account inside a profile leaves the cached `accountUuid`, `email` and label stale, which is especially harmful for a tool that labels accounts. A crash during removal has no recovery. Removal changes the default last, so until then the login-wide setting and wrapper point at a trashed folder, which a launch could recreate.
*   **Rationale & Recommendation**: Add a state table with recovery rules, re-read the account on each panel refresh, and make the default change the first step of removal.

### Keychain listing and deletion are assumed, and login detection has a race - Severity: Major
*   **ID:** `design-1-12`
*   **Reviewer Lens:** `egm-design`
*   **Affected Scope:** `Lines 271-273, 388-391, 483, 516-523`
*   **Discrepancy Description**: No S0 check confirms a self-signed, unsandboxed app can list these item names without a prompt, or delete an item created by the `claude` binary. Checks 1, 2 and 7 cover only how items are created. Step 6 lists names once after `.claude.json` gains `oauthAccount`, so if the Keychain item is written later, the result is zero new names and a false `error` state. "Sign out and retry" depends on check 6, but the failure column covers only removal. Nothing explicitly bars deleting the plain `Claude Code-credentials` item, which is the built-in login.
*   **Rationale & Recommendation**: Add an S0 check for listing and deleting. Poll with a timeout at step 6. Add a hard guard that refuses to delete the plain name.

### Backups and copy rules leave secret and dependency gaps - Severity: Major
*   **ID:** `design-1-13`
*   **Reviewer Lens:** `egm-design`
*   **Affected Scope:** `Lines 450-457, 465-470, 677-682`
*   **Discrepancy Description**: Backups hold whole config and settings files, which may include MCP secrets, but the NFRs give them no file mode, retention limit or cleanup. Lines 677-680 say only certain `.claude.json` keys are read, while backup and rewrite handle every byte. Secret detection is defined only for MCP values and `env`, yet hooks, skills, agents, commands and `CLAUDE.md` can also hold secrets, and new skill files are ticked by default. A copied hook can reference a script under the source profile, which is not a copyable item, so the target stays dependent on the source folder.
*   **Rationale & Recommendation**: Set 0700/0600 modes and retention for backups. Reword lines 677-680 to separate parsing from byte-level copying. Scan every item, and flag or rewrite hook references into the source profile.

### Names and quoted values have no validation rules - Severity: Major
*   **ID:** `design-1-14`
*   **Reviewer Lens:** `egm-design`
*   **Affected Scope:** `Lines 144-145, 304, 351, 368-369, 382`
*   **Discrepancy Description**: No rules exist for `commandName` and `displayName`: uniqueness, reserved names, or tabs, newlines and control characters. These can break the TAB-separated `names` file and inject terminal escapes into titles. "Single-quoted values" does not say how embedded single quotes are escaped, even though folder paths come from transcript data and are written into an executable `.command` file.
*   **Rationale & Recommendation**: Define validation for both names and the escaping rule for `.command` values, and add tests with hostile values, including a folder name containing an apostrophe.

### Trust grants are both never copyable and copyable if ticked - Severity: Major
*   **ID:** `structural-1-2`
*   **Reviewer Lens:** `egm-structural`, `egm-style`, `egm-design`
*   **Affected Scope:** `Lines 84, 472-473, 683-684`
*   **Discrepancy Description**: Flow 6 says trust grants "can never be ticked in v1". NFR Security says "Trust grants are not copied unless ticked". Separately, "secrets are left out unless the user ticks them" sits beside "credentials and tokens" that can never be ticked, with no stated difference between the two classes.
*   **Rationale & Recommendation**: Change line 684 to "Trust grants are never copied", or delete it. Define which secret-looking values can be ticked (masked `env`, `headers`, `args`) and which cannot.

### The components diagram and module lists do not match the flows - Severity: Major
*   **ID:** `structural-1-3`
*   **Reviewer Lens:** `egm-structural`, `egm-style`, `egm-design`
*   **Affected Scope:** `Lines 238-243, 245-253, 283-284, 285-293, 123, 495, 555, 700`
*   **Discrepancy Description**: Line 253 says `claude-profiles.zsh` defines `claude` and `claude-as` and reads the two plain-text files. Lines 285-290 give that work to `bin/` scripts, and Alternative L rejects the function approach. Alternative A still calls `claude-profiles.zsh` the bottom layer. `SessionStopper` appears in no module list. `cpm` is listed with four commands, yet `cpm stop` and `cpm uninstall` are relied on. The diagram omits `bin/`, `apply-default-env`, the LaunchAgent and `launchctl`. Its Keychain arrow says "names only", although KeychainProbe also deletes items.
*   **Rationale & Recommendation**: Redraw the diagram from the current design, complete the module and command lists, and name the `bin/` scripts plus the plain-text files on line 555.

### The Files on disk layout is incomplete - Severity: Major
*   **ID:** `structural-1-4`
*   **Reviewer Lens:** `egm-structural`, `egm-style`, `egm-design`
*   **Affected Scope:** `Lines 289, 297-319, 452, 681`
*   **Discrepancy Description**: `claude-profiles.zsh` has no listed location, though `~/.zshrc` sources it. `~/.claude-profiles/backups/builtin/<time>/` is missing from the tree. `profile-label` is listed in `bin/` but never described, and the status line command, including what happens on rename, is not specified. Line 681 says `default` and `names` are 0600, but the tree records a mode only for `profiles.json`. Line 505 logs a stop to `writes.log`, while line 314's list of logged events omits stops.
*   **Rationale & Recommendation**: Make the tree the complete inventory, describe `profile-label` in Components, and align the log list with the flows.

### Module ownership rules are contradicted by the flows - Severity: Major
*   **ID:** `structural-1-5`
*   **Reviewer Lens:** `egm-structural`, `egm-style`, `egm-design`
*   **Affected Scope:** `Lines 107-109, 268-269, 276-277, 293, 375, 383-384, 454, 477-478, 482, 489`
*   **Discrepancy Description**: "Launcher is the only module that starts processes", yet ProfileStore runs `apply-default-env`, SetupCopier runs install commands, profile removal runs logout, and CompatCheck runs `claude --version`. Launcher writes the launch log, and so does EventLog. "Copy setup is the only feature that writes into Claude Code's files" ignores the `statusLine` written to `settings.json` when a profile is added.
*   **Rationale & Recommendation**: Route these through one named module or narrow the rule, for example to "the only module that starts user-facing sessions". Give log writing to one module, and add the generated `statusLine` as an exception on lines 107-109.

### S0 checks are inconsistent and incomplete - Severity: Major
*   **ID:** `structural-1-6`
*   **Reviewer Lens:** `egm-structural`, `egm-style`, `egm-design`
*   **Affected Scope:** `Lines 114-119, 405, 406, 429, 499, 502-503, 516-529, 577-581`
*   **Discrepancy Description**: The table runs 1-9, "new", 12, 13, 14. Check 10 does not appear. Check 11 has no row, but it is the only verification behind Alternative C's rejection, which rests on "as far as known" and names "from reviewer memory". "new" is not a usable ID, so line 405 has to describe it in words. Open Questions omits checks 12-14, which can drop a stated goal, and it names login under a custom folder, which has no explicit row. Assumptions without a row include the `entrypoint` values, busy/idle status, Terminal.app inheriting the login-wide value, and SIGINT and SIGTERM stopping a session cleanly.
*   **Rationale & Recommendation**: Number every check, restore or explain 10 and 11, and add the missing assumptions. Have Open Questions point to the table instead of partly repeating it. State Alternative C's rejection as provisional until check 11 is done.

### Stop session is committed scope but missing from the Goals, Overview and CUJs - Severity: Major
*   **ID:** `structural-1-7`
*   **Reviewer Lens:** `egm-structural`, `egm-style`, `egm-design`
*   **Affected Scope:** `Lines 11-18, 51-89, 123, 138-200, 494-509`
*   **Discrepancy Description**: Line 123 places Stop session in v1 and flow 9 specifies it, but it has no goal, Overview mention or CUJ, even though it is the feature that signals live processes. Removing a profile and Restore also have no journey, although both are named in the Goals.
*   **Rationale & Recommendation**: Add Stop to the Overview and Goals, add a Given/When/Then CUJ for it, and consider one for removal.

### Coverage claims overstate what the default reaches - Severity: Major
*   **ID:** `style-1-1`
*   **Reviewer Lens:** `egm-style`, `egm-design`, `egm-structural`
*   **Affected Scope:** `Lines 21-23, 70-75, 174-176, 425-435, 653`
*   **Discrepancy Description**: "Followed everywhere it can be" and "as far as possible" cannot be falsified. "Every shell and script" and "any terminal or script" are broader than the table, which covers zsh plus "bash or a script started from those shells". Line 73 says Dock and Finder apps "also use it" without qualification, while line 430 makes that conditional on a child process and check 12, and line 431 adds a timing exception. Cron, launchd jobs, `bash -l` after `path_helper`, and a shell alias for `claude` are not addressed.
*   **Rationale & Recommendation**: Use a heading such as "Plain `claude` follows the default where the wrapper or login-wide setting reaches it". State the conditional in the Goal and CUJ 4, leave the full list to the table, add the missing rows, and extend check 14. Replace "the first moments after login" with "before the LaunchAgent has run".

### Other absolute claims are contradicted or not measurable - Severity: Major
*   **ID:** `style-1-2`
*   **Reviewer Lens:** `egm-style`, `egm-structural`
*   **Affected Scope:** `Lines 53, 85-86, 314, 409-410, 518, 688-690`
*   **Discrepancy Description**: "Logs of every launch" conflicts with flow 1, whose wrappers log nothing. "Every write" is broader than the list on line 314. "No background work while hidden" conflicts with the FSEvents watchers held while hidden. "Any number of profiles" goes beyond check 1, which covers three or more.
*   **Rationale & Recommendation**: Narrow each statement to what the design guarantees. List what the logs cover, replace "no background work" with "no polling while hidden", and state the profile count actually tested.

### Undefined references block a fresh reader - Severity: Major
*   **ID:** `style-1-3`
*   **Reviewer Lens:** `egm-style`, `egm-structural`, `egm-design`
*   **Affected Scope:** `Lines 25, 77, 81, 114, 204-226, 263, 439, 556, 564, 623, 642, 670, 710-711, 726`
*   **Discrepancy Description**: "S0", "S1" and "S6" have no definition in the reviewed scope. "Decision 7/11/12/17" points to an external file with no path. "Round 1 reviewers", "the success test" and "Goldfish" are unexplained. "The shell command" could mean `claude`, `claude-as` or `cpm`. "Login-wide setting" and "wrapper" carry load in the design but are absent from Terms. The standard's core test is that a fresh reader can understand the design.
*   **Rationale & Recommendation**: Add these to Terms. State each cited decision's content where it is relied on, or give the brief's path. Replace "the shell command" with the actual command name.

### Only `ANTHROPIC_API_KEY` gets an auth warning - Severity: Nit
*   **ID:** `design-1-15`
*   **Reviewer Lens:** `egm-design`
*   **Affected Scope:** `Lines 134-135, 199, 347, 528`
*   **Discrepancy Description**: Other variables may also override the login, such as `ANTHROPIC_AUTH_TOKEN` or `CLAUDE_CODE_OAUTH_TOKEN`. The reviewer named these from memory, and they are unverified.
*   **Rationale & Recommendation**: Check these in S0 and cover any that apply in the warning and `doctor`.

### Open Questions is mostly closed decisions - Severity: Nit
*   **ID:** `structural-1-8`
*   **Reviewer Lens:** `egm-structural`, `egm-style`
*   **Affected Scope:** `Lines 74, 80, 112-136, 346, 365, 494, 632, 654`
*   **Discrepancy Description**: Only one bullet is open, and the other two lists are resolved items that repeat the body text. "Author decision 2026-11-01" also recurs inline.
*   **Rationale & Recommendation**: Move resolved items to a "Decisions" section, one line each, and remove the inline attributions.

### Heading levels and alternative order are off - Severity: Nit
*   **ID:** `structural-1-9`
*   **Reviewer Lens:** `egm-structural`, `egm-style`
*   **Affected Scope:** `Lines 625-670, 708, 720`
*   **Discrepancy Description**: "Additional Resources" and "Future Improvements" are H3s that fall under Non-Functional Requirements in the outline. The alternatives run H, I, K, L, J.
*   **Rationale & Recommendation**: Promote both headings to H2. Move J before K without changing any letters, because they are cited elsewhere.

### Flows use bold text instead of headings - Severity: Nit
*   **ID:** `structural-1-10`
*   **Reviewer Lens:** `egm-structural`
*   **Affected Scope:** `Lines 330, 368, 379, 397, 412, 423, 443, 481, 488, 494`
*   **Discrepancy Description**: Flows are cited throughout but have no headings to link to or navigate by.
*   **Rationale & Recommendation**: Make the flows H4s.

### Line wrapping and diagram alignment - Severity: Nit
*   **ID:** `structural-1-11`
*   **Reviewer Lens:** `egm-structural`, `egm-style`
*   **Affected Scope:** `Lines 231-232, 322, 372, 703`
*   **Discrepancy Description**: Lines 322, 372 and 703 break the file's wrap width. The diagram's top border is one column narrower than the box contents.
*   **Rationale & Recommendation**: Rewrap the lines and fix the border.

### External references cannot be followed - Severity: Nit
*   **ID:** `structural-1-12`
*   **Reviewer Lens:** `egm-structural`
*   **Affected Scope:** `Lines 25, 710-715`
*   **Discrepancy Description**: `concepts_brief.md` and `concept_input.md` have no path or link. The doc says the mock is private to the author's account, so a fresh reader cannot use it. Whether the URL resolves was not verified.
*   **Rationale & Recommendation**: Use relative links, and describe the panel well enough that the spec stands without the mock.

### `doctor` and the health line are not specified - Severity: Nit
*   **ID:** `structural-1-13`
*   **Reviewer Lens:** `egm-structural`
*   **Affected Scope:** `Lines 85, 195-200`
*   **Discrepancy Description**: The `doctor` flow and the "health line per profile" are not specified beyond CUJ 6.
*   **Rationale & Recommendation**: Add a short flow that lists the checks and their outputs, including the health line.

### Decisions are cited by rejected alternatives and an undefined "option A" - Severity: Nit
*   **ID:** `style-1-4`
*   **Reviewer Lens:** `egm-style`, `egm-structural`
*   **Affected Scope:** `Lines 80, 127-128, 133, 631-633, 644`
*   **Discrepancy Description**: Line 128 cites Alternative H for "no status line", and line 133 cites Alternative K for wrapper plus login-wide setting, but H and K are the rejected designs. "Option A" is never defined and reads as Alternative A. Alternative I's only reason for rejection is "Not chosen by the author (option A)", which is the weakest entry under the Alternatives Rule.
*   **Rationale & Recommendation**: Write "rejected Alternative H" and "rejected Alternative K", replace "option A" with "keep `~/.claude` unwritten", and give Alternative I a specific reason.

### CUJ 2's outcome does not fit one of its triggers - Severity: Nit
*   **ID:** `style-1-5`
*   **Reviewer Lens:** `egm-style`, `egm-structural`, `egm-design`
*   **Affected Scope:** `Lines 76-77, 104, 155-159`
*   **Discrepancy Description**: Typing the command "in any terminal" does not make "Terminal.app open", and the shell command does not open windows (line 77). "Any terminal" also sits oddly with the non-goal on line 104.
*   **Rationale & Recommendation**: Split the outcomes by trigger, and reword line 77 to "windows running the command".

### Grammar error - Severity: Nit
*   **ID:** `style-1-6`
*   **Reviewer Lens:** `egm-style`, `egm-structural`, `egm-design`
*   **Affected Scope:** `Line 72`
*   **Discrepancy Description**: In "and work when the app is not running", the verb has no clear subject.
*   **Rationale & Recommendation**: Use "Both work when the app is not running."

### Unquantified size and comparison words - Severity: Nit
*   **ID:** `style-1-7`
*   **Reviewer Lens:** `egm-style`
*   **Affected Scope:** `Lines 11, 238, 283, 291, 550, 564, 588, 596-597, 601`
*   **Discrepancy Description**: "Small", "very small", "short", "fairly small", "uses more memory" and "less in between" have no measure. "Web technology" is not a clear advantage.
*   **Rationale & Recommendation**: Delete these words or give a number. Replace line 601 with the concrete reason: no browser engine or JavaScript layer between the app and the macOS calls.

### Slogans and subjective phrasing - Severity: Nit
*   **ID:** `style-1-8`
*   **Reviewer Lens:** `egm-style`
*   **Affected Scope:** `Lines 34, 53, 60, 87, 200, 439`
*   **Discrepancy Description**: "Slow and breaks flow", "Many accounts, no logging out", "Browser-style", "can own the code", "Plain Swift", "naming the specific fault" and "causes harm" are imprecise. Line 527 already defines the harm test.
*   **Rationale & Recommendation**: State the facts, for example "Each switch needs a logout, a browser login and a wait." Reuse line 527's wording on line 439.

### Repeated descriptions - Severity: Nit
*   **ID:** `style-1-9`
*   **Reviewer Lens:** `egm-style`
*   **Affected Scope:** `Lines 20-23, 70-75, 134-135, 174-176, 285-288, 330-333, 359-361, 363-366, 371, 412-421`
*   **Discrepancy Description**: The default behaviour, the wrapper mechanics and the API-key warning are each explained several times, which has already allowed drift between copies (`style-1-1`, `structural-1-3`).
*   **Rationale & Recommendation**: Keep one full statement in the flow and cross-reference it elsewhere.

### Wording presumes outcomes or is ambiguous - Severity: Nit
*   **ID:** `style-1-10`
*   **Reviewer Lens:** `egm-style`
*   **Affected Scope:** `Lines 105, 148, 216-220, 365-366, 716-718`
*   **Discrepancy Description**: "S0 check 13 confirms the wording" presumes the result, while line 528 allows for rewording. "(these need AppleScript)" could also cover other terminals. "Asks the user to keep it" has an unclear "it". "The version read" does not say what was inspected, and the sources on lines 716-718 do not obviously support the Keychain hashing claim.
*   **Rationale & Recommendation**: Use "S0 check 13 tests the wording", attach the parenthetical only to colour themes, use "asks whether to keep the login", and state the source of the hash claim.

### `.cam-backups` matches no other name - Severity: Nit
*   **ID:** `style-1-11`
*   **Reviewer Lens:** `egm-style`, `egm-structural`, `egm-design`
*   **Affected Scope:** `Lines 311, 450`
*   **Discrepancy Description**: `.cam-backups` matches none of `cpm`, `ClaudeProfiles` or `claude-profiles`, and looks like a leftover name.
*   **Rationale & Recommendation**: Rename it, for example to `.cpm-backups`.
