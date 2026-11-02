# Claude Profiles

Use several Claude accounts on one Mac, each logged in at the same time, without logging out.
Each account is a profile with its own Claude Code config folder.

The design is in [docs/designs/claude-profile-manager.md](docs/designs/claude-profile-manager.md).
Checks against real Claude Code behaviour are in [docs/S0-results.md](docs/S0-results.md).

Stages S1 to S4 are built: profiles, shell commands, `cpm`, the menu-bar app with its window, and copy setup. Profile
removal (S5) is not.

## Requirements

macOS 14, Command Line Tools, Claude Code, Terminal.app. Xcode is not needed.

## Build and test

```sh
./make-cli.sh                      # builds cpm and gathers files in build/cli
scripts/test.sh                    # Swift tests (plain `swift test` cannot find Swift Testing without Xcode)
zsh ShellTests/test_wrapper.zsh    # wrapper tests, with a temporary HOME
zsh ShellTests/test_cpm.zsh        # end-to-end tests, with a temporary HOME
CPM_REAL_CLAUDE=/opt/homebrew/bin scripts/test.sh    # also runs real claude mcp commands in throwaway folders
scripts/measure-panel.sh           # times listing 5,000 generated transcripts
CPM_RENDER_DIR=/some/dir scripts/test.sh    # also writes PNGs of the views. System controls render as placeholders
```

Set `SIGN_IDENTITY` to sign `cpm` with one fixed identity, so macOS permissions survive rebuilds.
Otherwise it is signed ad hoc.

## The app

```sh
./make-app.sh --install            # builds ClaudeProfiles.app and copies it to ~/Applications
open ~/Applications/ClaudeProfiles.app
```

It lives in the menu bar, as a dot in the default profile's colour. "Set up shell" does the same as `cpm install-shell`.
It starts sessions through `claude-as`, so nothing launches until that is done and the two lines are added.

## Copy setup

"Copy setup…" in the app copies from one profile into an added profile, never into the built-in one. It refuses while the
target has a live session, including a desktop app one.

- **Copied:** MCP servers, `permissions`, `hooks`, `model`, `env`, plugins, skills, agents, commands, `CLAUDE.md`. Nothing else
  is offered.
- **Not copied:** claude.ai connectors and account-synced plugins (they belong to the account), login tokens, `apiKeyHelper`,
  `statusLine`, trust grants and history.
- **Secrets:** values in `env` and `headers` are masked and left out unless you tick them. A file that looks like it holds one is
  flagged and unticked. A ticked MCP secret is passed on Claude Code's command line for a moment while it installs.
- **Undo:** "Restore…" puts back files the app wrote, and deletes those it created. A file changed since is left alone. It cannot undo
  MCP servers and plugins, because Claude Code's own commands install those; the app shows the command that removes each one.

## Install

```sh
build/cli/cpm install-shell
```

This copies files to `~/.claude-profiles/bin/` and prints two lines for you to add, one to `~/.zshenv` and
one to the end of `~/.zshrc`. It never edits those files. Open a new Terminal window and run `cpm doctor`.

Your existing `~/.claude` becomes the built-in profile, named `main`. The tool never writes to it.

## Daily use

```sh
cpm add Work work '#2F6FDE'       # add a profile
claude-as work auth login         # sign in. Check the browser is on the right account, or use a private window
cpm keep work                     # finish. Also: cpm retry work, cpm discard work

claude                            # the default profile
claude-as work                    # a named profile
cpm default work                  # change the default. Running sessions are not affected
cpm doctor                        # check the setup; cpm doctor --fix repairs default and names

cpm list                          # every profile's sessions, running ones first; --live, --profile work
cpm new work ~/Projects/x         # open a Terminal session on a profile
cpm resume <sessionId>            # resume on the profile that owns it, in its original folder
cpm stop <sessionId>              # asks first; interrupt, then terminate. The window stays open
cpm code work                     # open VS Code on a profile. Quit VS Code first if it is running
```

`cpm list` hides desktop app sessions. They cannot be resumed or stopped from `cpm`.

## Where the default applies

Plain `claude` follows the default in any zsh with the two lines added, and in bash and scripts started from
those shells. It does not reach `bash -l`, a full-path call, cron or launchd jobs, a shell alias that calls a full path, or
apps opened from the Dock. The desktop app and Cowork keep their own login and `~/.claude`. Design flow 5 lists
every case.

`ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN` and `CLAUDE_CODE_OAUTH_TOKEN` are never removed. If one is set you get a
warning, because Claude Code reports it instead of the profile's login.

If `default` or `names` is damaged, plain `claude` stops rather than risk the wrong account.

## Turning it off

Delete the two added lines and open a new shell. `claude` then runs Claude Code directly on `~/.claude`.

## When Claude Code updates

Rerun the S0 checks whose subject changed ([docs/S0-checklist.md](docs/S0-checklist.md)), then the tests above.
`cpm doctor` prints the version as a note.

## No App Sandbox

The tool must read `~/.claude*` and the process list, which a sandboxed app cannot do.
