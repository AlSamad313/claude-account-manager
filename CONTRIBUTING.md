# Contributing to Claude Profiles

Bug reports, fixes, documentation and features are all welcome. By contributing, you agree that your work is released under the [MIT License](LICENSE), and that you'll follow the
[Code of Conduct](CODE_OF_CONDUCT.md).

## Before you start

- **Small fix:** just open a pull request.
- **New feature or larger change:** open an issue first, so we can agree on the approach before you spend time on it.
- **Security problem:** don't open a public issue. See [SECURITY.md](SECURITY.md).

Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) first. The **safety rules** there protect people's logins and history. A change that breaks one won't be merged.

## Two platforms, one project

| | macOS | Windows |
|---|---|---|
| Code | `Sources/`, `shell/`, `Tests/`, `ShellTests/` | `windows/` |
| Language | Swift and zsh | C# on .NET 10 |
| Status | Working | In progress, see [windows/README.md](windows/README.md) |
| Design | [claude-profile-manager.md](docs/designs/claude-profile-manager.md) | [claude-profile-manager-windows.md](docs/designs/claude-profile-manager-windows.md), which lists only the differences |

The two share no code, but they share the same features and safety rules. If you change behaviour on one platform, say in your pull request whether the other needs the same change, and open an issue if it does. You only need to build and test the platform you changed: CI runs both on every pull request.

## Set up on macOS

You need a Mac with macOS 14 or later, and Xcode or the Command Line Tools with Swift 6.

```sh
git clone https://github.com/<your-username>/claude-account-manager.git
cd claude-account-manager
./make-cli.sh                      # builds cpm into build/cli
./make-app.sh                      # builds build/ClaudeProfiles.app
```

Building installs nothing. `./make-app.sh --install` copies the app to `~/Applications`.

## Set up on Windows

See [windows/README.md](windows/README.md) for what to install. Then:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows\build.ps1 -Test   # build and run the tests
```

Building installs nothing. Only `-Install` changes your `PATH`.

## Run the tests on macOS

```sh
zsh scripts/test.sh                # Swift tests
zsh ShellTests/test_wrapper.zsh    # wrapper tests, with a temporary HOME
zsh ShellTests/test_cpm.zsh        # end-to-end cpm tests, with a temporary HOME
```

Use `zsh scripts/test.sh`, not plain `swift test`, because it also works with the Command Line Tools alone. These tests use fakes and temporary folders, and need no Claude account.

Optional checks:

```sh
CPM_REAL_CLAUDE=/opt/homebrew/bin zsh scripts/test.sh   # also runs real claude mcp commands in throwaway folders
CPM_REAL_SETUP=$HOME zsh scripts/test.sh --filter RealSetupPreview   # previews your real setup, read-only; prints names only
CPM_RENDER_DIR=/some/dir zsh scripts/test.sh            # writes PNGs of the views. System controls show as placeholders
scripts/measure-panel.sh                                # times listing 5,000 generated transcripts
```

Don't paste the output of `CPM_REAL_SETUP` into an issue without reading it first, because it names your MCP servers, plugins and skills.

## Testing by hand

Automated tests can't sign in, click real controls or open Terminal. If you change the app, the wrapper or anything that touches Claude Code's files, run the matching part of
[docs/E2E-checklist.md](docs/E2E-checklist.md) on your own Mac. Use a throwaway profile for anything that stops sessions or removes data.

## Coding style

- **Match the code around you:** naming, comment density and idiom. Comments say why, not what.
- **Keep logic in `ProfileCore`.** Views stay thin, and slow work stays off the main thread.
- **Run external commands through `ProcessRunning`,** and write files through `AtomicFile`, so tests can fake them and a crash can't leave a half-written file.
- **Quote every value that goes into a shell script.** See `Launcher.quote`.
- **Write for users plainly.** Error messages say what happened and what to do next. Keep the Guide short: a test limits its length.
- **No new dependencies** without discussing them first. The project has none today, on purpose.

## Add tests

Every behaviour change needs a test, and every bug fix needs a test that fails without the fix. Tests must never touch the real home folder. `TestGuard` stops them if they try, so build paths from a `Sandbox`.

## When Claude Code changes

This tool relies on Claude Code's folders and file formats, which can change without notice. If you find a break after a Claude Code update:

1. Note the Claude Code version in your issue or pull request.
2. Rerun the checks in [docs/S0-checklist.md](docs/S0-checklist.md) whose subject changed.
3. Fail safe: if a format can't be read, refuse the risky action rather than guess.

## Send a pull request

1. Create a branch from `main`, named for the change, such as `fix-copy-preview`. Don't name a branch `windows`, because Git confuses it with the folder.
2. Make one focused change.
3. Run the tests for the platform you changed.
4. Update the README, the Guide, the checklist and [CHANGELOG.md](CHANGELOG.md) if behaviour changed.
5. Open the pull request and fill in the template.

**Keep personal data out:** no emails, usernames, tokens, or real paths and project names, in code, tests, logs, screenshots or commit messages. Use `you@example.com` and `/Users/me/...`. Commits show your Git email publicly, so consider your [GitHub noreply address](https://docs.github.com/en/account-and-profile/setting-up-and-managing-your-personal-account-on-github/managing-email-preferences/setting-your-commit-email-address).

If an AI tool helped write your change, that's fine. You're still responsible for understanding it and checking that it works.

## Ideas to work on

These are open if you want them. Comment on the issue to claim one. Issues labelled
[good first issue](https://github.com/AlSamad313/claude-account-manager/labels/good%20first%20issue) are a good place to start.

- [#6](https://github.com/AlSamad313/claude-account-manager/issues/6) Support for terminals other than Terminal.app, such as iTerm2
- [#7](https://github.com/AlSamad313/claude-account-manager/issues/7) An option to start the app at login
- [#8](https://github.com/AlSamad313/claude-account-manager/issues/8) A signed and notarized release, or a Homebrew cask, so the macOS warning goes away
- [#9](https://github.com/AlSamad313/claude-account-manager/issues/9) Support for bash and fish as the main shell
- [#10](https://github.com/AlSamad313/claude-account-manager/issues/10) Guide entries that jump straight to the feature they describe
- [#11](https://github.com/AlSamad313/claude-account-manager/issues/11) Testing on more macOS and Claude Code versions

## Making a release

For maintainers:

1. Update the version in `Resources/Info.plist` and the heading in `CHANGELOG.md`, and merge to `main`.
2. Run `zsh scripts/make-release.sh`. It builds a universal app, and puts a zip and checksum in `build/release`. It publishes nothing.
3. Test the zip on a clean account or a second Mac, including the macOS warning.
4. Tag the commit and upload both files:

   ```sh
   git tag v0.1.0 && git push origin v0.1.0
   gh release create v0.1.0 build/release/ClaudeProfiles-0.1.0-macos-universal.zip* --draft --notes-file <notes>
   ```

5. Read the draft on GitHub, then publish it.

Set `SIGN_IDENTITY` to sign with your own certificate. Removing the macOS warning also needs notarization, which isn't set up yet.

## Questions

Open an issue. There's no such thing as a silly question here, and unclear docs count as a bug.
