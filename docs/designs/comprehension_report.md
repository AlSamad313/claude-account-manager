## What this proposes

A personal, macOS-only menu-bar app written in Swift, plus zsh functions and a small CLI (`cpm`), that treats each Claude account as a browser-style profile with its own Claude Code config folder, so all accounts stay logged in at once. The existing `~/.claude` setup becomes a built-in first profile, and added profiles live in `~/.claude-profiles/<id>/` and are selected by setting `CLAUDE_CONFIG_DIR`. Plain `claude` and `claude-as <name>` read small plain-text files on every launch, so the terminal and the app agree on the default even when the app is not running. The app lists sessions across all profiles with live status, opens new or resumed sessions in Terminal.app through generated `.command` files, stops sessions, and copies selected setup between profiles with a preview, backups, restore and secret masking. Work begins with S0, a backup and a set of manual checks that decide whether to continue, and then proceeds through stages S1 to S5, each meant to be usable on its own.

## How the existing system works, per this document

Claude Code keeps one login per config folder, and today the author has one folder (`~/.claude`, with its config in `~/.claude.json`) and several accounts, so switching means logging out and back in through a browser. `CLAUDE_CONFIG_DIR` already selects another folder, and each folder is believed to have its own Keychain item with a `Claude Code-credentials` name prefix. Inside a folder, `projects/*/*.jsonl` holds transcripts and `sessions/*.json` holds live process records (`pid`, `procStart`, `entrypoint`, `status` and others), with `.key` files beside them. `/resume` shows only the current folder's sessions, and a Terminal window does not show which account it uses. Much of this comes from reading one installed version (2.1.296) or from memory, so the document treats it as assumptions until the S0 checks pass.

## Findings

**Stage:** 6 (Comprehension Test). **Target:** `claude-profile-manager.md`, lines 1-1205. FeatureRange 1-623, DetailedRange 624-1205. No document standard applied.

**Result:** 0 Blockers, 14 Majors, 12 Nits. The reviewer could paraphrase every section. The main gaps are places where parts of the plan contradict each other or do not connect.

**Not consulted:** `concepts_brief.md` and `concept_input.md` were not supplied, so references to them are judged only on what this document says about them. The mock URL was not opened. No credentials, Keychain contents or `.key` files were read.

### Major

**1. Severity: Major. Scope: S0 checks, lines 110-115, 431-441, 493, 657-661, 885, 1029.**
- The exit rule says checks 1-7 and 9 must pass to start S1, but the assumptions table gives fallbacks for checks 5, 6, 7 and 9 (drop Resume, warn, record by other means, accept a prompt). A reader cannot tell whether a failed check 5 stops the project or just removes Resume.
- Line 657 says checks run 1-11 plus `procStart`, but the table covers only 1-9 and "new". Check 10 is never defined anywhere, and check 11 appears only inside Alternative C.
- Other S0 outputs have no check at all, such as the `entrypoint` values (line 885) and the observed key names used for fixtures (line 1029).
- The term "S0" is also used from line 110, but the stage table that explains it is at line 636.

**2. Severity: Major. Scope: stage plan and flow ownership, lines 631-632, 637-641, 329-345, 397, 592, 955, 982-987, 1136-1141.**
- S1 claims "no more logging out" and every stage is said to be usable on its own, yet S1 has no way to create a profile. `cpm` has no add subcommand, the add-profile UI is S3 (file 28), and the Launcher that step 4 of flow 3 needs is S2 (file 22).
- No file owns the flow 3 sequence (snapshot Keychain names, launch, watch, diff, complete).
- `cpm list` and `cpm stop` are in file 31 under S1 but need S2 modules.
- No file implements the logout required by flow 7 and `cpm uninstall`. `ClaudeCLI` lists only MCP, marketplace and plugin methods.
- The S5 row omits file 28, even though its Remove action is marked S5 (line 958).

**3. Severity: Major. Scope: built-in profile write rules, lines 20-21, 56-57, 104-105, 267-269, 376-386, 543-544.**
- The document says the built-in profile is "not moved or changed", that `~/.claude` is "never written by the app", and that the author chose to keep it unwritten.
- It also defines copy setup into the built-in profile, with a dedicated backup location and a write to `~/.claude.json`.
- Copying skills, `CLAUDE.md` or `settings.json` into that target would have to write inside `~/.claude`.
- A reader cannot tell whether the built-in profile is a valid copy target, or for which items.

**4. Severity: Major. Scope: claim that only copy setup writes Claude Code's files, line 104-105 versus lines 333-334 and 791-795.**
- The Non-Goals say copy setup is the only feature that writes into Claude Code's files.
- Profile creation also writes `settings.json` with a `statusLine`, and `settings.json` is listed among the files copy setup merges.

**5. Severity: Major. Scope: copy allow-list and trust grants, lines 105, 383-390, 574-579, 1158.**
- The "fixed list" or "allow-listed" keys and folders are relied on but never listed in one place.
- `settings.json` is merged "key by key", which reads as all keys rather than an allow-list.
- "Never copied: ... unless ticked" (lines 388-390) does not say whether "unless ticked" covers the whole list or only trust grants. Credentials being tickable would matter a great deal.
- Line 579 makes trust grants tickable, while the test at line 1158 says they are not copied, with no condition stated.

**6. Severity: Major. Scope: flow 1 pseudocode, lines 298-311.**
- The `--resume` ownership check is written after `exec command claude`, so read literally it can never run.
- It also sits outside the subshell block, so its `exit 1` is unclear. So is the `exit 1` for an unknown name.
- In a sourced zsh function, a literal `exit` would close the user's shell. That is the same harm the `exec` note at lines 314-316 sets out to avoid.
- The intended order and the use of exit versus return need stating.

**7. Severity: Major. Scope: "exact stored path" and Keychain naming, lines 199, 248, 303, 433, 735, 785, 1036.**
- The added profile's path must be the "exact stored path", but no path field exists in `profiles.json` (lines 284-287), `names` (line 273) or the `Profile` struct (lines 699-712).
- The document never explains why exactness matters. The reviewer infers that the Keychain item name depends on the path string, and that the built-in profile uses the "unsuffixed" name refused at line 785, but neither is stated.
- This is the mechanism whose failure stops the whole design (line 433).

**8. Severity: Major. Scope: `ANTHROPIC_API_KEY` handling, lines 185, 301, 321, 736, 823, 833.**
- Every launch unsets this variable and `doctor` reports it as "stray", but the document never says why or what breaks if it stays set.
- It also does not say what happens to a user who sets it on purpose. This is a side effect on user configuration that relies on unstated knowledge.

**9. Severity: Major. Scope: where plain `claude` follows the default, lines 21-23, 70-73, 162-163, 230, 539, 554.**
- The goals say the terminal and app "always agree" and CUJ 4 says "any terminal".
- The mechanism is a function sourced from `~/.zshrc`, which reaches only zsh shells that source that file.
- The document never says plainly that bash, scripts, VS Code and other callers ignore the default and run on the built-in folder. Lines 539 and 554 only hint at this when discussing labels.

**10. Severity: Major. Scope: "only module" ownership claims, lines 244, 240, and 325, 404, 909-911, 1136.**
- "Launcher is the only module that starts processes" conflicts with `ClaudeBinaryLocator` (`/bin/zsh -lc`), `CompatCheck` (`claude --version`) and `ClaudeCLI`.
- "ProfileStore is the only writer of `profiles.json`" conflicts with `ClaudeBinaryLocator` caching a path in "`profiles.json` metadata".
- That metadata field exists nowhere in the schema (lines 284-287, 713).

**11. Severity: Major. Scope: `CLAUDE_CONFIG_DIR` source-tree test, lines 740-742, 823, 1037-1038, 1188.**
- The test fails if the string appears outside `ConfigLocator.swift` and `Launcher.swift`.
- `Doctor.swift` must check for a stray `CLAUDE_CONFIG_DIR`, so as described the test would fail unless Doctor gets the name some other way. The document does not say how.
- It also does not say whether "source tree" includes `shell/`, where the variable has to appear.
- Launcher's exemption is unexplained, since lines 238-239 and 320 say the environment comes from ConfigLocator.

**12. Severity: Major. Scope: component and file inventories, lines 215-220, 259-260, 270-281, 410, 793, 847, 982-987.**
- The Components section lists `cpm` as `doctor`, `list`, `default`, `names`, but `stop`, `install-shell`, `remove` and `uninstall` exist too. Line 119 already relies on `cpm stop`.
- `SessionStopper`, `Doctor`, `AccountReader`, `ClaudeCLI` and `ClaudeBinaryLocator` are missing from the module list.
- "Files on disk" omits `~/.claude-profiles/bin/` and `~/.claude-profiles/backups/builtin/`, both of which later sections rely on.

**13. Severity: Major. Scope: install workflow, lines 984-986, 1012-1017, 1169.**
- `make-app.sh` assembles and installs only the app.
- Nothing says how `cpm` gets onto `PATH`, even though the README tells the user to run `cpm install-shell`.
- Nothing says where that command finds files 16 and 17 at runtime, since they are not package resources (line 692) or bundle contents.
- The first-run order is also unstated: if the shell layer is installed before the app has created `default` and `names`, the fallback warning fires on each launch.

**14. Severity: Major. Scope: definition of a session, lines 64-66, 145-147, 202-203, 883.**
- A session is defined as a transcript file with an optional live process, and rows are joined by `sessionId`.
- The document does not say whether a live record with no transcript yet produces a row, or when Claude Code creates the transcript.
- The 5-second goal in CUJ 2 depends on this answer.

### Nit

**15. Severity: Nit. Scope: "decision N", lines 25, 239, 468, 479, 535, 565, 728, 1148.** Numbered decisions and "R2-M4" point to `concepts_brief.md`, which has no link or path at line 25. Line 1148 makes UI wording depend on that file. Only its intended later location is given (line 1180).

**16. Severity: Nit. Scope: "option A", lines 78, 544, 556.** The options are never listed, and the label is easily confused with Alternative A (lines 457-468), which is a different thing. Alternative H is the rejected option, yet its rejection also cites "option A".

**17. Severity: Nit. Scope: undefined project and product terms.**
- "Cowork" and "Remote Control" (lines 91-94, 616).
- "Goldfish" and "EGM" (lines 601, 1205).
- "Round 1 reviewers" and "the success test" (line 476).
- `cpm` is never expanded.
- The `.cam-backups` prefix (line 278) is unexplained and does not match any other name here.

**18. Severity: Nit. Scope: "v1", lines 94, 102, 119, 258.** The stage table never says which stages make up v1.

**19. Severity: Nit. Scope: `TerminalOpener` location, lines 901-902 versus 966 and 994.** The AppKit implementation is placed in `ProfileUI` in one place and in file 29, which is in `Sources/ClaudeProfiles`, in the others.

**20. Severity: Nit. Scope: built-in profile fields, lines 58, 193-194, 273, 715, 774.**
- The built-in profile's `commandName` and initial `displayName` are never given, so it is unclear what `claude-as <name>` uses for it.
- "Renamed to another folder" muddles the rule that rename changes only the display name.

**21. Severity: Nit. Scope: schema and validation drift, lines 284-287, 708, 711, 715, 845.**
- `organizationName` and `lastError` are in the struct but not in the prose schema.
- The ban on tabs and newlines in display names has no validation home, because line 715 covers only `commandName`.

**22. Severity: Nit. Scope: profile states, lines 241, 287, 343, 759-760, 933.**
- Nothing says what moves a profile into `loggedOut`, how a profile leaves `error`, or what the "Sign in" action does.
- The flow 3 timeout has no value.
- `reconcile` lists the mismatches it detects but not what it does about them.

**23. Severity: Nit. Scope: hidden-state behaviour, lines 65-66, 357-360, 582-584.**
- The 5-second and 10-second goals hold only while a view is visible, which the Goals section does not say.
- "No background work while hidden" sits beside FSEvents watchers on `sessions/` while hidden, with no stated purpose for those watchers.

**24. Severity: Nit. Scope: logging and test coverage claims, lines 83-84, 245, 252, 831-839, 1192.**
- "Logs of every launch" is not met by the shell functions, which log nothing.
- Launcher and EventLog are both described as writing the launch log.
- "Every module has tests" has no named test file for `CompatCheck`, `EventLog`, `AccountReader` or `StatusLineWriter`.

**25. Severity: Nit. Scope: unexplained specifics, lines 349, 572, 824, 826, 903.**
- `.key` files, `.credentials` files and `messagingSocketPath` are named without saying what they are.
- A stray `~/.claude/.claude.json` is flagged without saying why it is a fault.
- "Older than one launch" is vague.

**26. Severity: Nit. Scope: repository and stage leftovers, lines 626-628, 640, 1055, 1176-1181.**
- "This draft" and file 52 describe a move from `design_draft.md` to the path this document already has.
- No S0 file or step performs the "becomes a git repository" action.
- Files 51 and 52 have no stage.
- `tests/shell` and `Tests/` differ only by case, so they would be one folder on a default case-insensitive macOS volume.
- "Step 1" and "step 2" at line 640 could mean delivery steps or wizard steps (compare line 391).
- General `settings.json` copying is in neither step.
