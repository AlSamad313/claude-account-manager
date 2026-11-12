# Changelog

Newest first. There are no versioned releases yet.

## Unreleased

### Added

- New Terminal asks which folder to start in.
- A Guide button with a short manual, in the panel and the window.
- A menu-bar icon that takes the default profile's colour, and a new app icon.
- Contributor documentation, issue and pull request templates, and CI.
- `scripts/make-release.sh`, which packs a universal app for download.
- A Windows version in `windows/`: `cpm`, the `claude` and `claude-as` wrappers, and a tray app and window with sessions, sign-in, Settings, Doctor, Copy setup, the Guide, removing a profile and `cpm uninstall`. It installs per user, with a Start menu entry, Start with Windows and an Installed apps entry.
- CI on Windows, and line-ending rules so both platforms can work on the same files.

### Fixed

- Two backups made in the same millisecond could share a folder, so the second replaced the first.
- A command's output could come back empty when the computer was busy, on macOS and Windows.
- On Windows, another copy of the `claude` wrapper on `PATH` could be mistaken for the real Claude Code. macOS has the same gap (#4).

## First working version

- Profiles with separate logins, a default, and the `claude` and `claude-as` shell wrappers.
- The `cpm` command.
- A menu-bar app and window: sessions, sign-in, Doctor.
- Copy setup, with preview, masked secrets, backups and Restore.
- Removing a profile, and uninstalling.
