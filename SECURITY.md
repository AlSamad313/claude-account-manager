# Security Policy

This tool works next to people's Claude logins, so security reports matter here.

## Reporting a problem

**Please don't open a public issue.** On this repository's **Security** tab, choose **Report a vulnerability**, which sends a private report to the maintainer. Include what you found, how to reproduce it and
which versions you used. Don't include real tokens or personal data.

You should get a reply within about a week. This is a volunteer project, so there's no guaranteed fix time. Only the latest code on `main` is supported.

## What counts

For example:

- Reading, printing, logging or copying a login token or `.key` file
- Writing to `~/.claude` or `~/.claude.json`
- A folder name, session ID or profile name that can run a command
- Starting a session on the wrong account without telling the user
- Exposing a secret during Copy setup beyond what the screen says

## What the tool can and can't protect

- It runs with your user's full permissions and has no App Sandbox.
- Each login is kept in the macOS Keychain by Claude Code itself. The tool lists Keychain item names, and deletes only a removed profile's own item. It never reads the values.
- A secret you choose to tick in Copy setup is stored in plain text in the target profile, as Claude Code stores it, and is briefly visible on the command line while it installs.
- Any program running as you could read the same files, so this tool doesn't protect you from malware already on your Mac.
