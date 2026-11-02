# Zoom-Out Review: Claude Profile Manager design

**Stage:** 5 (Initial Zoom Out)
**Target:** `claude-profile-manager.md`, lines 1-1205
**Verdict:** Changes required: 1 Blocker, 11 Major, 11 Nit.

The root problem is clear and the core approach is sound: a `CLAUDE_CONFIG_DIR` profile list with a shell layer and a read-only session index. The main problems are:
- the one write path that contradicts the safety goal
- a stage plan that does not deliver what it claims
- machinery in the later stages that S0 results or simpler choices could remove

## Review basis and limits
- **Read in full:** the target, the persona, the standard, `concepts_brief.md` and `concept_input.md`.
- **Context sufficiency (standard section 0):** passes. The objective and motivation are clear.
- **Code reuse:** not applicable inside the repo. The repo holds only the three documents in `docs/designs`, so there is no code to reuse.
- **Project alignment (section 1):** cannot be checked against a root document, because no `README.md` or `context.md` exists yet. Checked against the brief instead.
- **External checks:** the reviewer could not open URLs or run commands. The private mock and all external tools are unchecked. Claims about Claude Code behaviour marked "from memory" need testing.
- **Author decisions:** the GUI requirement, copy setup and stop session were not reopened as requirements. Where a decision has a cost or a contradiction, the report says so.

## Blocker

### B1. Copy setup into the built-in profile contradicts the safety goal
**Affected scope:** lines 20, 56-59, 267-269, 375-381, 541-545, 1127-1129

**Rationale:**
- The goals say `~/.claude` and `~/.claude.json` are "left exactly as they are".
- The files table says `~/.claude` is "never written by the app".
- Alternative H was rejected specifically to avoid writing `~/.claude/settings.json`.
- Flow 6 nevertheless allows the built-in profile as a copy target. That writes skills, agents, commands, `CLAUDE.md` and merged `settings.json` into `~/.claude`, and writes `~/.claude.json`, the file holding the existing account's identity.
- Lines 268-269 acknowledge only the `.claude.json` write, and line 267 still says "never written". An implementer cannot tell which rule wins.
- No CUJ needs the built-in as a target. CUJ 5 uses it as the source.

**Simpler fix:** make the built-in profile source-only in v1. This restores the invariant, removes the special backup path (M9) and removes the riskiest write in the product. If the author wants it as a target, the goal, the files table and the Alternative H rationale must all be rewritten to say so.

## Major

### M1. Alternative I looks better than the chosen status line, but was rejected without a technical reason
**Affected scope:** lines 74-79, 123-124, 537-557, 613-615; files 12 and 17 (lines 788-795, 841-847); line 385

**Rationale:**
- The chosen approach needs a generated `settings.json` per profile, `StatusLineWriter`, a "never copy statusLine" rule, and it still leaves the built-in profile with no in-session label.
- Passing `--settings` from the shell function and the `.command` file needs no write to any `settings.json`, removes the copy-collision rule, and could label the built-in profile too without touching `~/.claude`.
- Its stated con is that sessions started outside the shell command and the app show no label. That barely applies to added profiles, because those launchers are the normal way to set the variable.
- The rejection reason is only "Not chosen by the author (option A)". Option A appears to have been framed as the answer to a built-in write question.

**Fix:** add an S0 check on how `--settings` combines with a user's own status line, and take this back to the author with this comparison. If the check passes, it should replace the chosen approach. `profile-label` may remain as the command the setting runs.

### M2. Window title is the built-in profile's only label, and no check covers it
**Affected scope:** lines 74-79, 123-124, 145, 304, 320, 897; S0 table lines 431-441

**Rationale:**
- From memory and unverified: Claude Code sets the terminal title itself while running. If so, the escape sequence is overwritten shortly after launch.
- The built-in account would then show no label at all, which is problem 3 (lines 41-42) on the account currently used most.
- The S0 table has no check that the title stays set.

**Fix:** add this as an S0 check that can stop or change the design. Decide on the fallback together with M1.

### M3. S1 cannot deliver "no more logging out"
**Affected scope:** lines 631-637, 329-345, 761, 955, 982-987

**Rationale:**
- Line 631 says each stage is usable on its own, and S1 promises no more logging out.
- `cpm` has no add or login command.
- The only add flow is `AddProfileView` in S3, and it depends on `Launcher` from S2. So S1 has no way to create a profile.
- File 31 is in S1, but `list` needs `SessionIndex` and `stop` needs `SessionStopper`, both from S2.
- File 15 is in S1, but its parse-failure check needs `TranscriptReader` from S2.
- Nothing installs `cpm` on `PATH`. `install-shell` copies only files 16 and 17, `make-app.sh` handles only the app, and `make-app.sh` is itself S3. So `cpm install-shell` has no defined way to reach the user in S1.

**Fix:** add a minimal `cpm add <name>` to S1 (create the folder and row, print the `claude-as` login command, detect the login on the next `cpm` run), define how `cpm` is installed, and assign subcommands to stages.

### M4. "Only writer" is a module rule, but two processes run that module
**Affected scope:** lines 240-241, 288, 362-366, 772-773, 1043-1046

**Rationale:**
- The app and `cpm` each hold a `ProfileStore`.
- Atomic rename prevents torn files, not lost updates. A long-running app with an older in-memory registry can overwrite a change made by `cpm`, because every change rewrites `profiles.json`.
- The app watches `default` and `names`, not `profiles.json`.
- The three files are also renamed one at a time, so a crash can leave them disagreeing. `reconcile` does not cover that case.
- Line 363 says the change rewrites `default`, while lines 772-773 say all three files are rewritten.

**Fix:** use the smallest rule that closes this: take one lock file, re-read from disk before each change, and treat `profiles.json` as the source of truth, regenerating the other two files during reconcile. No daemon is needed.

### M5. Exact-version allow-list will produce a permanent banner
**Affected scope:** lines 403-407, 660, 809-814, 1172-1174

**Rationale:**
- `testedVersions` is a compiled-in list that starts at the exact version `2.1.296`. That patch number suggests very frequent releases.
- Keeping it current means editing and rebuilding for each update, so the banner will usually be showing and will soon be ignored.
- The checklist rerun rule says "minor update", but the list holds exact versions, so the two do not match.
- The real protection is the shape check and refusing to write. Readers already skip and count bad files.

**Fix:** keep the shape-based `canWrite` check. Drop the version comparison, or reduce it to one informational line in `doctor`, or at most compare major.minor.

### M6. "Use an existing tool" is rejected before anyone checks
**Affected scope:** lines 482-493, 660-661; `concept_input.md` line 31; brief decision 6

**Rationale:**
- The author said they are open to an existing tool, and the standard requires leveraging existing solutions.
- Alternative C rests on names "from reviewer memory", and states "Their claims have not been checked."
- The check is narrowed to "reusable parsing code", sits in S0 as check 11, and is outside the exit rule, while 52 files are planned.
- The design also relies on files such as `.claude.json`. From memory and unverified, Claude Code may have official auth commands, such as an auth status output, which would be better than reading `.claude.json`.

**Fix:** make this a time-boxed first step of S0, with a recorded result for each tool: adopt, borrow or reject. Check 6 should also record whether official commands expose account identity.

### M7. S0 exit rule and the assumptions table disagree, and the table is incomplete
**Affected scope:** lines 110-115, 431-441, 493, 657, 660-661

**Rationale:**
- The exit rule says checks 1-7 and 9 must pass to start S1.
- The table gives "continue" fallbacks for checks 4, 5, 6, 7 and 9. Examples are "Resume is dropped" and "Accept one prompt".
- Either those are not gates or the fallbacks are dead text.
- Checks 10 and 11 are referenced but missing from the table, so a reader must open the brief to learn what they are. Check 10 is the status line, which is a stated goal.
- The brief's check of whether deleting a Keychain item prompts was also dropped.

**Fix:** make one table the authority, listing all checks with a Gate or Fallback column. Remove the separate exit rule.

### M8. Copy setup overclaims its guarantees and carries two code paths
**Affected scope:** lines 81-82, 370-372, 379-382, 391-394, 439, 1099-1103, 1139-1141, 1156-1160

**Rationale:**
- "The preview and the result cannot differ" and "can undo it from a backup" hold only for direct file writes.
- Claude Code's own MCP and plugin commands write what they choose, including caches and registries. The plan does not list those files, so they are not backed up and Restore cannot undo them.
- The golden test says plugin registries are "never touched" (lines 1158-1159), yet installing a plugin necessarily changes the target's registry.
- A runtime switch between the CLI path and a key-level write means building and testing two MCP paths for a question S0 answers once.

**Fix:** let check 8 choose one MCP path, and ship only that. Then state the guarantee honestly in two tiers: direct writes get an exact preview and byte-for-byte restore, while install commands get an intent preview and a separate uninstall or manual-cleanup note.

### M9. Backups live in two places, one inside Claude Code's folder
**Affected scope:** lines 278, 375-378, 399, 1127-1129

**Rationale:**
- Added profiles back up to `<target>/.cam-backups/`, and the built-in profile to `~/.claude-profiles/backups/builtin/`.
- The split exists only to keep `~/.claude` clean, but the same central location works for every profile.
- Putting backups inside a profile adds an unknown folder to a Claude Code config directory, and removal sends them to the Trash with the profile.

**Fix:** use `~/.claude-profiles/backups/<id>/<time>/` for all profiles. This removes a branch and the leftover `.cam` name. The files table (lines 270-281) also omits `backups/` and `bin/`.

### M10. The module boundaries are already untrue
**Affected scope:** lines 218-221, 244, 397, 410, 592, 740-742, 811, 823, 909-910, 1136

**Rationale:**
- "Launcher is the only module that starts processes" is contradicted by `ClaudeBinaryLocator`, `CompatCheck` (`claude --version`), `ClaudeCLI`, and the logout runs in removal and uninstall.
- The diagram and component list omit `SessionStopper`, `Doctor`, `ClaudeCLI`, `AccountReader`, `StatusLineWriter`, `ClaudeBinaryLocator`, `TerminalOpener` and `FileWatcher`.
- The grep test fails as specified. `Doctor.swift` must contain the string `CLAUDE_CONFIG_DIR` to detect a stray variable, and `shell/claude-profiles.zsh` contains it too if "source tree" means the whole repo.
- File 48 has no logout method, although flow 7 needs one.

**Fix:** define one process-runner seam that takes `LaunchEnv`, restate the rule as "all Claude Code runs get their environment from ConfigLocator", update the diagram, and replace the string-grep test with tests on `LaunchEnv` and on the launch output.

### M11. The app's Terminal opener has an implementation with no real caller, and a misplaced one
**Affected scope:** lines 688-692, 900-903, 966, 982-987, 992-994, 1077

**Rationale:**
- Line 993 says the `/usr/bin/open` implementation is used by the CLI and tests.
- `cpm` has no command that opens Terminal, and file 42's tests use a fake.
- The AppKit implementation is placed in `ProfileUI` at line 902 but in file 29 at lines 966 and 994.
- The whole seam exists so that a single `NSWorkspace` call stays out of `ProfileCore`.
- `ProfileUI` is itself a separate library with one consumer and no test target.

**Fix:** use `/usr/bin/open -a Terminal <file>` for the app too, subject to check 9, and keep only a small test seam. Merge `ProfileUI` into the app target unless a second consumer or a test target justifies it.

## Nit

### N1. Full window has no goal or CUJ
**Affected scope:** lines 63-66, 126-185, 254-258, 938-944

The goals and CUJs describe only the menu bar. The brief's "ship in v1" was a round 2 proposal (line 276). "Adds layout, not logic" also understates the work, because `MainWindowView` has its own sidebar, table and toolbar, and only the rows are shared. Add a goal, or defer it until the 8-row panel proves too small.

### N2. Keychain machinery may be mostly unnecessary
**Affected scope:** lines 247-249, 335-340, 396-398, 437-438, 777-786

If check 6 shows logout removes the item, then `delete`, its guards and much of the snapshot flow have little left to do. Make their inclusion depend on checks 6 and 7. Rated a Nit because the recording must happen at first login, so it cannot simply be added later.

### N3. Watching and polling overlap
**Affected scope:** lines 357-360, 582-584, 921-923, 972-977

The 2-second poll alone meets the 5-second and 10-second goals while visible. FSEvents on `sessions/` while hidden contradicts "no background work while hidden" (line 360) and serves nothing, since data loads when the panel opens. Keep one watcher on `~/.claude-profiles/` for the icon colour, and poll while visible.

### N4. Unmeasured caching
**Affected scope:** lines 325-327, 351, 585-586, 713, 909-911

- The binary path is cached "in `profiles.json` metadata", but `Registry` has no such field and `ProfileStore` is meant to be the only writer. Use an in-memory value.
- The transcript cache has no measured need. Measure first; sorting by change time and parsing only visible rows may be enough, with a cache added if filtering is slow.

### N5. Logging goal is not met, and logging is doubled
**Affected scope:** lines 83-84, 252-253, 280, 293-311, 800-804

The goal says "every launch", but shell launches write no log. Either say app launches only, or add a line to the shell function. Plain log files plus `os_log` with per-module categories plus rotation is a lot for one user; pick one.

### N6. Secret detection is heuristic where a simple rule is safer
**Affected scope:** lines 382, 1108-1113; brief line 147

Mask all `env` and `headers` values unconditionally, as the brief recommended, and use patterns only for `args`. A pattern miss would otherwise show a real secret as an ordinary value. Showing the first 4 characters also reveals part of the secret and a recognisable prefix. Line 1112 places `SecretDetector` in S1 for Doctor, but file 15 never uses it and prints no such values.

### N7. Trust grants and hook paths are unclear
**Affected scope:** lines 388-390, 1090-1092; brief lines 141, 349

"Never copied: credentials, ... trust grants ... unless ticked" can be read as allowing credentials to be ticked. `CopyItem` has no trust-grant kind and file 50 tests that they are not copied, so remove the option for v1. Separately, the brief's handling of hook and MCP command paths pointing into the source folder was dropped. A copied hook could run scripts from the source profile. Restore a simple rule: flag such items and leave them unticked.

### N8. Flow 1 has shell errors in its pseudocode
**Affected scope:** lines 299-309, 836, 1063

- The `--resume` check appears after `exec`, so as written it never runs. File 16 has the right order.
- `exit 1` in a function sourced by an interactive shell closes the window if it runs outside the subshell, the same trap line 314 warns about. Use `return 1`.
- From memory: printing the title to stdout could also corrupt piped output such as `claude -p ... | jq`. Write to `/dev/tty` only when interactive.

### N9. Reach of the default and the API-key rule are not stated
**Affected scope:** lines 49, 70-73, 185, 301, 321, 325, 736, 823

- The `claude` function exists only where `.zshrc` is sourced. VS Code, scripts and other non-interactive callers ignore the default and use the built-in profile. State this in Non-Goals.
- `/bin/zsh -lc` does not read `.zshrc`, so a `PATH` set only there will not resolve.
- The design silently unsets `ANTHROPIC_API_KEY` on every launch with no stated reason, despite "without changing how Claude Code itself works". State why.
- App `doctor` sees the GUI environment rather than a shell's, so its stray-variable result differs from `cpm doctor`.

### N10. Small unused items
**Affected scope:** lines 260, 287, 708-711, 765, 824, 837, 983

- `cpm names` is unused, because completion reads the `names` file directly.
- No flow sets the `loggedOut` state.
- `createdAt` has no reader.
- `organizationName` and `lastError` are missing from the field list at lines 284-287.
- `setCommandName` goes beyond the stated rename goal.
- The stray `~/.claude/.claude.json` already exists on this machine per the brief (line 88), so `doctor` may report a fault on a clean setup, against the acceptance criterion at brief line 374.
- Plugin-registry checks guard against something the design never does.

### N11. Altitude and stale text
**Affected scope:** lines 215-216, 259-260, 297-312, 331-345, 412-421, 626-627, 1176-1181

- Flows 1, 3 and 9 place step-level pseudocode (including the 250 ms check interval) in the Technical Plan. Move it to the implementation section, leaving the rules behind. Struct and signature detail in files 6, 8 and 10 is acceptable under "Detailed Implementation Plan".
- The component list omits `cpm stop`, `install-shell`, `remove` and `uninstall`.
- Lines 626-627 and file 52 describe a draft location and a move that are already complete.
- `README.md` and `design_draft.md` entries are missing from the stage table.

## Suggested order of action
1. Decide B1, and take M1 back to the author.
2. Add the new S0 checks and unify the table (M2, M6, M7).
3. Fix S1 and multi-process writes (M3, M4).
4. Simplify `CompatCheck`, copy setup, backups and module boundaries (M5, M8-M11).

Once S1-S3 are in use, reassess S4 scope. It is the largest risk, it is the only feature that writes into Claude Code's files, and its value can then be judged from real use.
