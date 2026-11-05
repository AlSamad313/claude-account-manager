# Claude Profiles

Use several Claude Code accounts on one Mac, all signed in at the same time, without logging in and out.

Each account is a profile, like a browser profile. You choose one as the default, start a session on any of them, and see every
session in one place. It comes as a menu-bar app, a `cpm` command, and a small shell wrapper.

> **Independent project.** This is not made by, endorsed by or affiliated with Anthropic. Claude and Claude Code are products of
> Anthropic. You need your own Claude accounts and must follow their terms.

## What it does

- **Keeps accounts separate.** Each profile has its own Claude Code config folder, login, settings and history.
- **Follows a default.** Plain `claude` uses the default profile, and the menu-bar icon shows its colour.
- **Starts sessions anywhere.** New Terminal asks for a folder, then opens Terminal.app on that account.
- **Shows every session.** Running and recent sessions across profiles, including ones you started by hand. You can filter, resume and stop them.
- **Copies setup between profiles.** MCP servers, skills, plugins and settings, with a preview, hidden secrets and Restore.
- **Opens VS Code on a profile.**

Press **Guide** in the app for a short manual.

## Status

Early. It has been tested by hand on one Mac, on macOS 26 with Claude Code 2.1.296. Other versions may work, and
reports are welcome. Because Claude Code's file formats aren't a public API, an update to Claude Code can break this tool.

## Requirements

- macOS 14 or later, on Apple Silicon or Intel
- [Claude Code](https://claude.com/claude-code)
- zsh and Terminal.app, which macOS includes
- To build from source: Xcode, or the Command Line Tools with Swift 6

## Install

### Download

1. Download the zip from the [latest release](https://github.com/AlSamad313/claude-account-manager/releases/latest).
2. Unzip it, and move `ClaudeProfiles.app` to `/Applications` or `~/Applications`.
3. Open it. **macOS will warn that it can't verify the app,** because this project isn't signed with a paid Apple Developer certificate.
   If you trust this download:
   - **macOS 15 or later:** press Done, then go to System Settings > Privacy & Security, scroll down and press **Open Anyway**.
   - **macOS 14:** right-click the app, then choose **Open**.

To check the download first, compare `shasum -a 256 ClaudeProfiles-*.zip` with the `.sha256` file in the release. You can also read the code and build it yourself.

### Build from source

```sh
git clone https://github.com/AlSamad313/claude-account-manager.git
cd claude-account-manager
./make-app.sh --install             # builds the app into ~/Applications
open ~/Applications/ClaudeProfiles.app
```

### Set up

The app lives in the menu bar and has no Dock icon. In the panel, press **Set up shell…**, then **Install**, and paste the two commands it shows into Terminal. They add one line each to `~/.zshenv`
and `~/.zshrc`. The tool never edits those files itself. Open a new Terminal window, then run `cpm doctor` to check.

Your existing `~/.claude` becomes the built-in profile, named `main`. The tool never writes to it.

To use only the commands, run `./make-cli.sh && build/cli/cpm install-shell` instead.

## Daily use

In the app, use **Add profile…**, **New Terminal**, **Resume**, **Stop** and **Make default**. The same actions work from Terminal:

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

## Limits

- **The desktop app and Cowork stay on their own login.** The desktop app ignores a custom data folder in signed builds, so this tool can't open it on a second account.
- **Where the default reaches.** It works in zsh with the two lines added, and in bash and scripts started from those shells. It does not reach `bash -l`, full-path calls,
  cron or launchd jobs, shell aliases that call a full path, or apps opened from the Dock. [Design flow 5](docs/designs/claude-profile-manager.md) lists every case.
- **API keys.** `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN` and `CLAUDE_CODE_OAUTH_TOKEN` are never removed. If one is set you get a warning, because Claude Code reports it instead of the profile's login.
- **Damaged files.** If `default` or `names` is damaged, plain `claude` stops rather than risk the wrong account.
- **No App Sandbox.** The tool must read `~/.claude*` and the process list, which a sandboxed app can't do.

## Copy setup

- **Copied:** MCP servers, `permissions`, `hooks`, `model`, `env`, plugins, skills, agents, commands, `CLAUDE.md`. Nothing else is offered.
- **Not copied:** claude.ai connectors and account-synced plugins and skills, login tokens, `apiKeyHelper`, `statusLine`, trust grants and history.
- **Secrets:** values in `env` and `headers` are masked and left out unless you tick them. A file that looks like it holds one is flagged and unticked. A ticked MCP secret is passed on
  Claude Code's command line for a moment while it installs, and is stored in plain text in the target profile.
- **Undo:** Restore puts back files the app wrote, and deletes those it created. A file changed since is left alone. It can't undo MCP servers and plugins, so the app shows the command that removes each one.

Copying is refused while the target has a live session, and the built-in profile can be copied from but never into.

## Remove or turn it off

- **One profile:** Settings… > Remove… in the app, or `cpm remove work`. It signs that profile out and moves its folder, history and backups to the Trash. Other profiles stay signed in.
- **Pause it, keeping your profiles:** delete the two added lines and open a new shell. `claude` then runs Claude Code directly on `~/.claude`.
- **Everything:** quit the app, then run `cpm uninstall`. It removes every added profile, moves `~/.claude-profiles` to the Trash, and prints the two lines for you to delete. Then delete the app from `~/Applications`.
  `~/.claude`, `~/.claude.json` and your original login are not touched.

## Contributing

Contributions are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), then [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), which also lists the safety rules every change must keep.
To report a security problem, see [SECURITY.md](SECURITY.md).

## Documentation

| Document | For |
|---|---|
| [CONTRIBUTING.md](CONTRIBUTING.md) | Building, testing and sending a change |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | How the code fits together, and the safety rules |
| [docs/E2E-checklist.md](docs/E2E-checklist.md) | The hands-on test script |
| [docs/S0-checklist.md](docs/S0-checklist.md), [docs/S0-results.md](docs/S0-results.md) | Checks against real Claude Code behaviour |
| [docs/designs/](docs/designs/) | The original design and its review history |
| [CHANGELOG.md](CHANGELOG.md) | What changed |

## License

[MIT](LICENSE)
