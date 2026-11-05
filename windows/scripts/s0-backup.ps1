# W0 backup of the built-in profile (Windows design, S0 checks).
# Never copies or opens the login file (.credentials.json), so a restore needs a fresh sign-in
# only if the login itself was lost. Records Credential Manager names only, never secrets.
#
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File windows\scripts\s0-backup.ps1
$ErrorActionPreference = 'Stop'
$h = $env:USERPROFILE
$dest = Join-Path $h ("claude-profile-backup-" + (Get-Date -Format 'yyyy-MM-dd'))
if (Test-Path -LiteralPath $dest) {
    [Console]::Error.WriteLine("Refusing: $dest already exists.")
    exit 1
}

$live = 0
foreach ($f in @(Get-ChildItem -LiteralPath "$h\.claude\sessions" -Filter '*.json' -ErrorAction SilentlyContinue)) {
    try {
        $o = Get-Content -Raw -LiteralPath $f.FullName | ConvertFrom-Json
        if ($o.pid -and (Get-Process -Id $o.pid -ErrorAction SilentlyContinue)) { $live++ }
    } catch {}
}
if ($live -gt 0) {
    [Console]::Error.WriteLine("Note: $live Claude Code session(s) are live. Claude Code replaces its files by rename,")
    [Console]::Error.WriteLine("so each copied file is whole, but the set may be from slightly different moments.")
}

New-Item -ItemType Directory -Path $dest | Out-Null
# Only this user. Copied files inherit this, because robocopy below does not copy permissions.
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
& icacls.exe $dest /inheritance:r /grant:r "*${sid}:(OI)(CI)F" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls failed on $dest" }

# sessions\ holds live records and .key files, and is excluded on purpose, as on macOS.
& robocopy.exe "$h\.claude" "$dest\dot-claude" /E /COPY:DAT /DCOPY:DAT /XD "$h\.claude\sessions" /XF .credentials.json /R:1 /W:1 /NFL /NDL /NP /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with code $LASTEXITCODE" }
Copy-Item -LiteralPath "$h\.claude.json" -Destination "$dest\dot-claude.json"

$names = @(& cmdkey.exe /list | Select-String -Pattern '^\s*Target:.*(claude|anthropic)' | ForEach-Object { $_.Line.Trim() })
Set-Content -LiteralPath "$dest\credential-names.txt" -Value $names -Encoding UTF8

$restore = @"
To restore, with every Claude Code session and the Claude desktop app closed, in PowerShell:
  robocopy "$dest\dot-claude" "`$env:USERPROFILE\.claude" /E /COPY:DAT /XD sessions
  Copy-Item "$dest\dot-claude.json" "`$env:USERPROFILE\.claude.json"
This copies files back over the current ones and keeps files created since. It does not delete them.
This backup does not hold the login file (.credentials.json), sessions\, or the desktop app's data.
If the login was lost, sign in again with: claude auth login
Credential Manager names at backup time are in credential-names.txt (empty is normal).
This folder can contain MCP secrets. Keep it private and delete it when no longer needed.
"@
Set-Content -LiteralPath "$dest\RESTORE.txt" -Value $restore -Encoding UTF8

$mb = [math]::Round(((Get-ChildItem -LiteralPath $dest -Recurse -Force -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Output "Backup: $dest"
Write-Output "$mb MB"
if (Test-Path -LiteralPath "$dest\dot-claude\.credentials.json") { throw "The login file was copied. Delete $dest and report this." }
Write-Output "Login file not copied: ok"
if ($names.Count -eq 0) { Write-Output "Credential Manager names: none" } else { $names }
