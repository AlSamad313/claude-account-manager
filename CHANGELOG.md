# Changelog

Newest first.

## Unreleased

### Added

- macOS: the window's session list shows each session's model and effort level. Selecting a session opens a pane with every model or
  effort switch made in it, with times and reply counts. Read from the transcript, so nothing extra is recorded.
- macOS: a summary of what each session worked on, in the pane and as a tooltip on its title: Claude Code's own recap, the files it changed
  and any pull requests.
- macOS: the API-equivalent cost of each session, in the list and per model in the pane, as Claude Code calculates it, including subagents' models.

## 0.1.0 (2026-11-13)

### Added

- New Terminal asks which folder to start in.
- A Guide button with a short manual, in the panel and the window.
- A menu-bar icon that takes the default profile's colour, and a new app icon.
- Contributor documentation, issue and pull request templates, and CI.
- `scripts/make-release.sh`, which packs a universal app for download. It keeps the builder's home folder and username out of the binaries.
- The start of a Windows version in `windows/`: `cpm` and the `claude` and `claude-as` wrappers.
- CI on Windows, and line-ending rules so both platforms can work on the same files.

### Fixed

- Two backups made in the same millisecond could share a folder, so the second replaced the first.
- A command's output could come back empty when the computer was busy, on macOS and Windows.

## First working version

- Profiles with separate logins, a default, and the `claude` and `claude-as` shell wrappers.
- The `cpm` command.
- A menu-bar app and window: sessions, sign-in, Doctor.
- Copy setup, with preview, masked secrets, backups and Restore.
- Removing a profile, and uninstalling.
