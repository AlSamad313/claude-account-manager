# Changelog

Newest first. There are no versioned releases yet.

## Unreleased

### Added

- New Terminal asks which folder to start in.
- A Guide button with a short manual, in the panel and the window.
- A menu-bar icon that takes the default profile's colour, and a new app icon.
- Contributor documentation, issue and pull request templates, and CI.
- `scripts/make-release.sh`, which packs a universal app for download.

### Fixed

- Two backups made in the same millisecond could share a folder, so the second replaced the first.

## First working version

- Profiles with separate logins, a default, and the `claude` and `claude-as` shell wrappers.
- The `cpm` command.
- A menu-bar app and window: sessions, sign-in, Doctor.
- Copy setup, with preview, masked secrets, backups and Restore.
- Removing a profile, and uninstalling.
