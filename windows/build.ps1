<#
.SYNOPSIS
  Builds the Windows version of Claude Profiles.

.DESCRIPTION
  build.ps1            build cpm.exe and the claude.exe wrapper into build\bin, with Native AOT
  build.ps1 -Test      also run the tests
  build.ps1 -Install   also install them: cpm install-shell (copies to %USERPROFILE%\.claude-profiles\bin
                       and puts that folder first in your user PATH)

  Needs the .NET 10 SDK and the Visual Studio Build Tools with "Desktop development with C++".
  Scripts are blocked by default on Windows, so run it as:
    powershell -NoProfile -ExecutionPolicy Bypass -File windows\build.ps1 -Test
#>
param([switch]$Test, [switch]$Install)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here 'build'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
# The SDK's first run otherwise adds %USERPROFILE%\.dotnet\tools to the user PATH, and makes an HTTPS certificate.
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'

function Fail($message) { Write-Host $message -ForegroundColor Red; Pop-Location; exit 1 }
# global.json, which picks the SDK and the test runner, is read from the working folder.
Push-Location $here

# .NET 10 SDK: on PATH, or the per-user or machine install.
$dotnet = $null
foreach ($c in @((Get-Command dotnet.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source),
                 "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe", "$env:ProgramFiles\dotnet\dotnet.exe")) {
    if ($c -and (Test-Path $c) -and ((& $c --list-sdks 2>$null) -match '^10\.')) { $dotnet = $c; break }
}
if (-not $dotnet) {
    Fail ("The .NET 10 SDK is missing. Install it with:`n  winget install Microsoft.DotNet.SDK.10`n" +
          "or per user, without admin rights: https://dot.net/v1/dotnet-install.ps1 -Channel 10.0")
}

# Native AOT links with the C++ build tools. Their helper finds vswhere.exe on PATH, which a
# window opened before the Build Tools were installed does not have.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
$vswhere = Join-Path $installer 'vswhere.exe'
$vc = if (Test-Path $vswhere) { & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath }
if (-not $vc) {
    Fail ("The Visual Studio C++ build tools are missing. Native AOT needs them. Install with:`n" +
          "  winget install Microsoft.VisualStudio.BuildTools --override `"--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended`"")
}
if (($env:PATH -split ';') -notcontains $installer) { $env:PATH = "$installer;$env:PATH" }

function Publish($project, $dir) {
    & $dotnet publish (Join-Path $here $project) -c Release -r win-x64 -o (Join-Path $out $dir) --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { Fail "Build failed: $project" }
}

Write-Host 'Building cpm.exe and claude.exe...'
Publish 'src\Wrapper\Wrapper.csproj' 'bin'
Publish 'src\Cpm\Cpm.csproj' 'bin'
Get-ChildItem (Join-Path $out 'bin') -Filter *.pdb | Remove-Item
Write-Host "Built into $out\bin"

if ($Test) {
    Write-Host 'Running tests...'
    Publish 'tests\FakeClaude\FakeClaude.csproj' 'fake'
    $failed = $false
    foreach ($t in 'tests\ProfileCore.Tests\ProfileCore.Tests.csproj', 'tests\Wrapper.Tests\Wrapper.Tests.csproj') {
        & $dotnet test --project (Join-Path $here $t)
        if ($LASTEXITCODE -ne 0) { $failed = $true }
    }
    if ($failed) { Fail 'Tests failed.' }
    Write-Host 'All tests passed.' -ForegroundColor Green
}

if ($Install) {
    & (Join-Path $out 'bin\cpm.exe') install-shell
    if ($LASTEXITCODE -ne 0) { Fail 'Install failed.' }
}

Pop-Location
