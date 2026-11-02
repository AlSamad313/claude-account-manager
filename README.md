# Claude Profiles

Use several Claude accounts on one Mac, each logged in at the same time, without logging out.
Each account is a profile with its own Claude Code config folder.

The design is in [docs/designs/claude-profile-manager.md](docs/designs/claude-profile-manager.md).
Checks against real Claude Code behaviour are in [docs/S0-results.md](docs/S0-results.md).

Stage S1 is built: profiles, shell commands and `cpm`. The session list (S2) and the menu-bar app (S3) are not.

## Requirements

macOS 14, Command Line Tools, Claude Code, Terminal.app. Xcode is not needed.

## Build and test

```sh
./make-cli.sh                      # builds cpm and gathers files in build/cli
scripts/test.sh                    # Swift tests (plain `swift test` cannot find Swift Testing without Xcode)
zsh ShellTests/test_wrapper.zsh    # wrapper tests, with a temporary HOME
zsh ShellTests/test_cpm.zsh        # end-to-end tests, with a temporary HOME
```

Set `SIGN_IDENTITY` to sign `cpm` with one fixed identity, so macOS permissions survive rebuilds.
Otherwise it is signed ad hoc.

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
```

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
