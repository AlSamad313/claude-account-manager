# Concept input: Claude two-account session manager (macOS)

Date: 2026-10-31. Author: Abdul Samad Shahid.

## Problem
One person, one Mac, two Claude accounts: an official (company) account and a personal account. The user owns the company, so there is no employer policy issue with running both on this machine. Today they log out and back in to switch accounts. They cannot keep track of which Claude Code sessions exist on which account. The user uses Claude Code CLI, the Claude desktop app, Cowork, and Remote Control mode.

## Anchored hypothesis
A macOS desktop window app that keeps one Claude Code config folder per account (via the `CLAUDE_CONFIG_DIR` environment variable). From the app, the user can start a new Claude Code CLI session for a chosen account. The session opens in a new Terminal.app window. The app lists past and running CLI sessions for both accounts, and lets the user resume, rename/tag, search and stop them. A zsh shell function replaces `claude` so sessions started by hand are tracked too: it reads the current default account from a settings file the app controls, sets that account's config folder, and writes a "running" record (process ID, account, folder, start time) before starting the real `claude`. `claude-work` and `claude-personal` commands pin a specific account. For the desktop app and Cowork, the app only needs to open them logged into the right account. It does not list or manage their conversations.

## Success
The user can see and check all sessions, especially Claude Code CLI sessions, on both accounts in one place, without logging in and out.

## Facts already verified
- Claude Code 2.1.296 is installed via Homebrew cask at /opt/homebrew/Caskroom/claude-code@latest/2.1.296/claude.
- Reading the installed binary: when `CLAUDE_CONFIG_DIR` is set, the macOS Keychain login is saved under the service name `Claude Code-credentials-<first 8 hex chars of sha256(config dir path)>`. The default `~/.claude` uses `Claude Code-credentials` with no suffix. So two config folders hold two independent logins. Moving or renaming a config folder changes the suffix and forces a new login. This was read from code, not tested with two real logins.
- Each config folder stores CLI session history as JSONL transcript files under `<config dir>/projects/`.

## Not yet verified
- Whether the Claude desktop app (Claude.app, Electron) can run two separate logins, e.g. via separate user-data folders or two copies of the app.
- Whether Remote Control mode follows `CLAUDE_CONFIG_DIR` for its account.

## Accepted limitations (user agreed)
- Claude Code started outside the zsh shell (VS Code extension, scripts calling the binary directly) is not tracked as running.
- "Running" records go stale after a crash or abrupt terminal close; the app must check whether each process ID is still alive.
- Changing the default account only affects new sessions.

## Scope
- Terminal: Terminal.app only.
- Plain `claude` = the user-selected default account (either one); explicit commands start a specific account.
- The user is open to using an existing open-source tool instead of building one, if one covers this.
