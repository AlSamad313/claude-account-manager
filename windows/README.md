# Claude Profiles for Windows

The Windows version of Claude Profiles: several Claude Code accounts side by side, each in its own
`CLAUDE_CONFIG_DIR` folder. The design is `../docs/designs/claude-profile-manager-windows.md`.

**Status: stage W1.** `cpm` (add, keep, retry, discard, default, doctor, install-shell, uninstall-shell)
and the `claude` / `claude-as` wrapper work. Listing and stopping sessions (W2), the tray app (W3), copy
setup (W4) and removal (W5) are still to come.

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
powershell -NoProfile -ExecutionPolicy Bypass -File windows\build.ps1          # build into windows\build\bin
powershell -NoProfile -ExecutionPolicy Bypass -File windows\build.ps1 -Test    # and run the tests
powershell -NoProfile -ExecutionPolicy Bypass -File windows\build.ps1 -Install # and install
```

`-Install` runs `cpm install-shell`. It copies `cpm.exe`, `claude.exe` and `claude-as.exe` to
`%USERPROFILE%\.claude-profiles\bin`, and puts that folder first in your user `PATH`. The old `PATH` is
saved in `%USERPROFILE%\.claude-profiles\logs\writes.log`. Open a new terminal window, then:

```powershell
cpm doctor
cpm add Personal personal "#E8772E"
claude-as personal auth login
cpm keep personal
claude-as personal
```

Plain `claude` runs the default profile, which starts as your existing login. `cpm default <name>` changes it.

To undo the `PATH` change: `cpm uninstall-shell`. Your profiles and `~\.claude` are not touched.

## Layout

```text
src/ProfileCore/   the rules: profiles, sign-in, doctor, install (class library)
src/Shared/        the names reader and real-binary rule, compiled into ProfileCore and the wrapper
src/Wrapper/       claude.exe / claude-as.exe (Native AOT); never reads JSON
src/Cpm/           cpm.exe (Native AOT)
tests/             MSTest: ProfileCore.Tests, Wrapper.Tests, and FakeClaude for the wrapper tests
scripts/           the stage W0 check scripts
```
