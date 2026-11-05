# Claude Profiles for Windows

The Windows version of Claude Profiles: several Claude Code accounts side by side, each in its own
`CLAUDE_CONFIG_DIR` folder. The design is `../docs/designs/claude-profile-manager-windows.md`.

**Status: stage W4.** The tray app and its window, with Copy setup, `cpm` (add, keep, retry, discard, default, list, new,
resume, stop, code, doctor, install-shell, uninstall-shell) and the `claude` / `claude-as` wrapper work.
Removing a profile, and uninstalling (W5), are still to come.

## Requirements

- Windows 11, x64
- The .NET 10 SDK: `winget install Microsoft.DotNet.SDK.10`, or per user without admin rights with
  Microsoft's `https://dot.net/v1/dotnet-install.ps1 -Channel 10.0`
- The Visual Studio Build Tools with the C++ workload, for Native AOT:
  `winget install Microsoft.VisualStudio.BuildTools --override "--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"`
- The native Claude Code (`claude.exe`). The npm `claude.cmd` is not supported.

## Build, test, install

Windows blocks scripts by default, so run the build script with `-ExecutionPolicy Bypass`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows\build.ps1          # build into windows\build
powershell -NoProfile -ExecutionPolicy Bypass -File windows\build.ps1 -Test    # and run the tests
powershell -NoProfile -ExecutionPolicy Bypass -File windows\build.ps1 -Install # and install the app
```

`-Install` copies the app to `%LOCALAPPDATA%\Programs\ClaudeProfiles`, adds a Start menu shortcut, an
entry in Settings > Apps > Installed apps and a Start with Windows value, and starts it. Its coloured dot
appears by the clock, or in the overflow (^) area. On first start the panel asks you to **Set up command
line**. That copies `cpm.exe`, `claude.exe` and `claude-as.exe` to `%USERPROFILE%\.claude-profiles\bin`,
puts that folder first in your user `PATH`, and can add tab completion to your PowerShell profile. The old
`PATH` is saved in `%USERPROFILE%\.claude-profiles\logs\writes.log`. `cpm install-shell [--completion]` does
the same from the command line.

Then add profiles from the panel, or in a new terminal window:

```powershell
cpm doctor
cpm add Personal personal "#E8772E"
claude-as personal auth login
cpm keep personal
claude-as personal
```

Plain `claude` runs the default profile, which starts as your existing login. `cpm default <name>` or Make
default in the panel changes it.

To undo the `PATH` change and the completion line: `cpm uninstall-shell`. To remove the app: Settings >
Apps > Installed apps > Claude Profiles > Uninstall. Neither touches your profiles or `~\.claude`.

## Layout

```text
src/ProfileCore/     the rules: profiles, sign-in, sessions, stop, copy setup, doctor, install (class library)
src/Shared/          the names reader, real-binary rule and launch request, compiled into ProfileCore and the wrapper
src/Wrapper/         claude.exe / claude-as.exe (Native AOT); never reads JSON
src/Cpm/             cpm.exe (Native AOT)
src/ProfileUI/       the app's model, panel, flow steps and window (WPF library)
src/ClaudeProfiles/  ClaudeProfiles.exe: tray icon, panel placement, one running copy, install and uninstall
src/Shell/           claude-profiles.ps1, the PowerShell tab completion
tests/               MSTest: ProfileCore.Tests, ProfileUI.Tests (model, views, and the built app through UI Automation),
                     Wrapper.Tests, and FakeClaude for the wrapper tests
scripts/             the stage W0 check scripts, and measure-panel.ps1 (cpm list on 5,000 generated transcripts)
```
