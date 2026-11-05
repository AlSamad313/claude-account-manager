# Changelog

Newest first. There are no versioned releases yet.

## Unreleased

### Added

- New Terminal asks which folder to start in.
- A Guide button with a short manual, in the panel and the window.
- A menu-bar icon that takes the default profile's colour, and a new app icon.
- Contributor documentation, issue and pull request templates, and CI.
- `scripts/make-release.sh`, which packs a universal app for download.
- The start of a Windows version in `windows/`: `cpm` and the `claude` and `claude-as` wrappers.
- CI on Windows, and line-ending rules so both platforms can work on the same files.

### Fixed

- Two backups made in the same millisecond could share a folder, so the second replaced the first.
- A command's output could come back empty when the Mac was busy.

## First working version

- Profiles with separate logins, a default, and the `claude` and `claude-as` shell wrappers.
- The `cpm` command.
- A menu-bar app and window: sessions, sign-in, Doctor.
- Copy setup, with preview, masked secrets, backups and Restore.
- Removing a profile, and uninstalling.
