# Helpers for the Windows S0 checks (docs/S0-checklist-windows.md).
# Reads names, key names and account labels only. Never opens .credentials.json or sessions\*.key.
#
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File windows\scripts\s0-checks.ps1 <command>
#   prep                 create test folders under %USERPROFILE%\.cpm-s0
#   status               sign-in state of the built-in profile and test folders 1-3   (C1, C2, C3)
#   names                Credential Manager names and which folders have a login file (C1)
#   procstart            session records' procStart against the real process start   (C9, C10)
#   keys                 file-format key names, and where transcript titles sit      (C13)
#   path                 every claude on PATH, in the order Windows searches          (C12)
#   args                 argument round trip through claude mcp add / add-json        (C16, C5)
#   plugins [mkt] [plg]  plugin marketplace add and install in a test folder          (C5)
#   ctrlc <pid> [-Twice] send console Ctrl+C to a process and time its exit           (C7)
#   teardown             sign out of test folders and list what is left
param([Parameter(Position = 0)][string]$Command, [Parameter(Position = 1)][string]$Arg1,
      [Parameter(Position = 2)][string]$Arg2, [switch]$Twice)
$ErrorActionPreference = 'Stop'
$h = $env:USERPROFILE
$root = Join-Path $h '.cpm-s0'
$builtin = Join-Path $h '.claude'

# C# 5, because Windows PowerShell 5.1 compiles with the .NET Framework compiler.
Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class CpmS0 {
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint group);

    // Creation time as Windows file time. -1: not running. -2: access denied.
    public static long CreationTime(int pid) {
        IntPtr h = OpenProcess(0x1000, false, pid);  // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) { return Marshal.GetLastWin32Error() == 5 ? -2 : -1; }
        try {
            uint code;
            if (!GetExitCodeProcess(h, out code) || code != 259) { return -1; }  // STILL_ACTIVE
            long c, e, k, u;
            if (!GetProcessTimes(h, out c, out e, out k, out u)) { return -2; }
            return c;
        } finally { CloseHandle(h); }
    }

    // Call only in a throwaway process: it gives up this process's console.
    public static int SendCtrlC(uint pid) {
        FreeConsole();
        if (!AttachConsole(pid)) { return Marshal.GetLastWin32Error(); }
        SetConsoleCtrlHandler(IntPtr.Zero, true);
        if (!GenerateConsoleCtrlEvent(0, 0)) { return Marshal.GetLastWin32Error(); }
        return 0;
    }

    // The quoting rule the wrapper will use: CommandLineToArgvW and the C runtime split it back.
    public static string Quote(string a) {
        if (a.Length > 0 && a.IndexOfAny(new char[] { ' ', '\t', '\n', '\v', '"' }) < 0) { return a; }
        StringBuilder sb = new StringBuilder("\"");
        int bs = 0;
        foreach (char c in a) {
            if (c == '\\') { bs++; continue; }
            if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); bs = 0; continue; }
            sb.Append('\\', bs); bs = 0; sb.Append(c);
        }
        sb.Append('\\', bs * 2);
        sb.Append('"');
        return sb.ToString();
    }

    public static string Join(string[] args) {
        string[] q = new string[args.Length];
        for (int i = 0; i < args.Length; i++) { q[i] = Quote(args[i]); }
        return string.Join(" ", q);
    }

    // Runs exe with exactly these arguments. configDir null: CLAUDE_CONFIG_DIR removed.
    public static string[] Run(string exe, string[] args, string configDir, int timeoutMs) {
        ProcessStartInfo psi = new ProcessStartInfo(exe, Join(args));
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        psi.CreateNoWindow = true;
        if (configDir == null) { psi.EnvironmentVariables.Remove("CLAUDE_CONFIG_DIR"); }
        else { psi.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = configDir; }
        using (Process p = Process.Start(psi)) {
            p.StandardInput.Close();
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch {} return new string[] { "timeout", o.Result, e.Result }; }
            p.WaitForExit();
            return new string[] { p.ExitCode.ToString(), o.Result, e.Result };
        }
    }
}
'@

function Real-Claude {
    $own = Join-Path $h '.claude-profiles\bin'
    foreach ($c in @(Get-Command claude.exe -CommandType Application -All -ErrorAction SilentlyContinue)) {
        if ((Split-Path $c.Source -Parent).TrimEnd('\') -ne $own) { return $c.Source }
    }
    throw 'No claude.exe found on PATH.'
}

function Test-Folders { @(1, 2, 3) | ForEach-Object { Join-Path $root "$_" } | Where-Object { Test-Path -LiteralPath $_ } }

function Ensure-Folder([string]$n) {
    $d = Join-Path $root $n
    if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d | Out-Null }
    return (Get-Item -LiteralPath $d).FullName
}

function Show-Status([string]$label, [string]$dir) {
    $r = [CpmS0]::Run((Real-Claude), @('auth', 'status', '--json'), $dir, 30000)
    $cfg = if ($dir) { $dir } else { $builtin }
    $login = Test-Path -LiteralPath (Join-Path $cfg '.credentials.json')
    try {
        $d = $r[1] | ConvertFrom-Json
        Write-Output ("{0}: loggedIn={1} | {2} | {3} | org={4} | configDirectory={5} | login file={6}" -f $label, $d.loggedIn, $d.email, $d.authMethod, $d.orgName, $d.configDirectory, $login)
    } catch {
        Write-Output ("{0}: status unreadable (exit {1}) | login file={2}" -f $label, $r[0], $login)
    }
}

function Show-Names {
    $n = @(& cmdkey.exe /list | Select-String -Pattern '^\s*Target:.*(claude|anthropic)' | ForEach-Object { $_.Line.Trim() })
    Write-Output '--- Credential Manager names containing claude or anthropic'
    if ($n.Count -eq 0) { Write-Output '(none)' } else { $n }
    Write-Output '--- Login files (existence only)'
    Write-Output ("built-in: {0}" -f (Test-Path -LiteralPath "$builtin\.credentials.json"))
    foreach ($d in Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue) {
        Write-Output ("folder-{0}: {1}" -f $d.Name, (Test-Path -LiteralPath (Join-Path $d.FullName '.credentials.json')))
    }
}

function Read-Chunk([string]$path, [long]$offset, [int]$count) {
    $fs = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        $fs.Seek($offset, 'Begin') | Out-Null
        $buf = New-Object byte[] $count
        $n = $fs.Read($buf, 0, $count)
        return [System.Text.Encoding]::UTF8.GetString($buf, 0, $n)
    } finally { $fs.Close() }
}

switch ($Command) {
    'prep' {
        foreach ($n in @('1', '2', '3', '4', '5', '7', '16')) { Ensure-Folder $n | Out-Null }
        $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        & icacls.exe $root /inheritance:r /grant:r "*${sid}:(OI)(CI)F" | Out-Null
        Write-Output "Test folders ready under $((Get-Item -LiteralPath $root).FullName)"
    }
    'status' {
        Show-Status 'built-in' $null
        foreach ($d in Test-Folders) { Show-Status ("folder-" + (Split-Path $d -Leaf)) (Get-Item -LiteralPath $d).FullName }
        Show-Names
    }
    'names' { Show-Names }
    'procstart' {
        $dirs = @($builtin) + @(Test-Folders)
        foreach ($dir in $dirs) {
            foreach ($f in @(Get-ChildItem -LiteralPath (Join-Path $dir 'sessions') -Filter '*.json' -ErrorAction SilentlyContinue)) {
                try { $o = Get-Content -Raw -LiteralPath $f.FullName | ConvertFrom-Json } catch { Write-Output "$($f.Name): unreadable"; continue }
                $ps = $o.procStart
                $rec = $null
                # Seen on 2.1.318: a JSON string of digits, Windows file time (100 ns ticks since 1601 UTC).
                if ("$ps" -match '^\d{15,19}$') { $rec = [DateTime]::FromFileTimeUtc([long]"$ps") }
                $actual = [CpmS0]::CreationTime([int]$o.pid)
                $state = switch ($actual) { -1 { 'not running' } -2 { 'access denied' } default { 'running' } }
                $diff = '-'
                if ($actual -ge 0 -and $rec) { $diff = '{0:N3}s' -f ([math]::Abs(([long]"$ps" - $actual) / 1e7)) }
                Write-Output ("{0} pid={1} {2} procStart={3} ({4}Z) diff={5} entrypoint={6} status={7} pidDomain={8}" -f (Split-Path $dir -Leaf), $o.pid, $state, $ps, $(if ($rec) { $rec.ToString('s') } else { 'not a file time' }), $diff, $o.entrypoint, $o.status, $o.pidDomain)
            }
        }
    }
    'keys' {
        $dirs = @($builtin) + @(Test-Folders)
        $recKeys = @{}; $entry = @{}; $status = @{}
        foreach ($dir in $dirs) {
            foreach ($f in @(Get-ChildItem -LiteralPath (Join-Path $dir 'sessions') -Filter '*.json' -ErrorAction SilentlyContinue)) {
                try { $o = Get-Content -Raw -LiteralPath $f.FullName | ConvertFrom-Json } catch { continue }
                foreach ($k in $o.PSObject.Properties.Name) { $recKeys[$k] = 1 }
                $entry["$($o.entrypoint)"] = 1 + [int]$entry["$($o.entrypoint)"]
                $status["$($o.status)"] = 1 + [int]$status["$($o.status)"]
            }
        }
        Write-Output ('Session record keys: ' + (($recKeys.Keys | Sort-Object) -join ', '))
        Write-Output ('entrypoint values: ' + (($entry.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', '))
        Write-Output ('status values: ' + (($status.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', '))
        $total = 0; $headOrTail = 0; $none = 0; $largest = 0
        foreach ($dir in $dirs) {
            foreach ($t in @(Get-ChildItem -LiteralPath (Join-Path $dir 'projects') -Filter '*.jsonl' -Recurse -File -ErrorAction SilentlyContinue)) {
                $total++
                if ($t.Length -gt $largest) { $largest = $t.Length }
                $n = [int][math]::Min(65536, $t.Length)
                $text = Read-Chunk $t.FullName 0 $n
                if ($t.Length -gt 65536) { $text += "`n" + (Read-Chunk $t.FullName ($t.Length - 65536) 65536) }
                if ($text -match '"type":"(custom-title|ai-title)"') { $headOrTail++ } else { $none++ }
            }
        }
        Write-Output ("Transcripts: {0}; title in first or last 64 KB: {1}; not found there: {2}; largest: {3:N1} MB" -f $total, $headOrTail, $none, ($largest / 1MB))
        foreach ($file in @("$builtin\settings.json", "$h\.claude.json")) {
            if (Test-Path -LiteralPath $file) {
                $o = Get-Content -Raw -LiteralPath $file | ConvertFrom-Json
                Write-Output ("{0} top-level keys: {1}" -f (Split-Path $file -Leaf), (($o.PSObject.Properties.Name | Sort-Object) -join ', '))
            }
        }
    }
    'path' {
        $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
        $user = [Environment]::GetEnvironmentVariable('Path', 'User')
        $i = 0
        Write-Output '--- claude files in search order (system PATH, then user PATH, from the registry)'
        foreach ($pair in @(@('system', $machine), @('user', $user))) {
            foreach ($e in ($pair[1] -split ';')) {
                if (-not $e) { continue }
                $i++
                $x = [Environment]::ExpandEnvironmentVariables($e)
                foreach ($ext in @('.exe', '.cmd', '.bat', '.ps1', '')) {
                    $p = Join-Path $x "claude$ext"
                    if (Test-Path -LiteralPath $p -PathType Leaf) { Write-Output ("#{0} {1}: {2}" -f $i, $pair[0], $p) }
                }
            }
        }
        Write-Output '--- This shell: Get-Command claude -All'
        Get-Command claude -All -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ("{0} {1}" -f $_.CommandType, $(if ($_.Source) { $_.Source } else { $_.Definition })) }
        Write-Output '--- Old copies left by self-update next to the real binary'
        Get-ChildItem -LiteralPath (Split-Path (Real-Claude) -Parent) -Filter 'claude.exe.old*' -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }
        Write-Output "--- PowerShell profile: $PROFILE exists=$(Test-Path -LiteralPath $PROFILE)"
        Write-Output ('Execution policy (effective): ' + (Get-ExecutionPolicy))
    }
    'args' {
        # C16: arguments must arrive exactly as given. Uses a logged-out test folder and no account usage.
        $dir = Ensure-Folder '16'
        $exe = Real-Claude
        $values = @('plain', 'two words', 'quote"inside', 'ends with backslash\', 'back\slash\"quote', '\\server\share\', '', ' ', 'percent %PATH% and !BANG!', 'caret ^ amp & pipe | lt < gt >', "tab`there", ('unicode e-acute: ' + [char]0x00E9 + ' cjk: ' + [char]0x4E2D + ' emoji: ' + [char]::ConvertFromUtf32(0x1F600)), 'semi;colon', "single'quote", '-dash-start', '--flag=value')
        $fail = 0
        foreach ($name in @('cpmargs', 'cpmjson')) { [CpmS0]::Run($exe, @('mcp', 'remove', $name, '--scope', 'user'), $dir, 60000) | Out-Null }
        # 1. Positional arguments: claude mcp add <name> --scope user -- cmd <values...>
        $r = [CpmS0]::Run($exe, @('mcp', 'add', '--scope', 'user', 'cpmargs', '--', 'cmd') + $values, $dir, 60000)
        Write-Output "mcp add exit=$($r[0]) $($r[2].Trim())"
        # 2. One JSON argument full of quotes and backslashes.
        $json = (@{ type = 'stdio'; command = 'cmd'; args = $values } | ConvertTo-Json -Compress)
        $r = [CpmS0]::Run($exe, @('mcp', 'add-json', 'cpmjson', $json, '--scope', 'user'), $dir, 60000)
        Write-Output "mcp add-json exit=$($r[0]) $($r[2].Trim())"
        $cfgFile = Join-Path $dir '.claude.json'
        $landed = if (Test-Path -LiteralPath $cfgFile) { (Get-Content -Raw -LiteralPath $cfgFile -Encoding UTF8 | ConvertFrom-Json).mcpServers } else { $null }
        $inHome = (Get-Content -Raw -LiteralPath "$h\.claude.json" -Encoding UTF8 | ConvertFrom-Json).mcpServers
        Write-Output ("Landed in test folder's .claude.json: {0}. Leaked into built-in ~\.claude.json: {1}" -f [bool]$landed, [bool]($inHome -and ($inHome.PSObject.Properties.Name -contains 'cpmargs' -or $inHome.PSObject.Properties.Name -contains 'cpmjson')))
        foreach ($name in @('cpmargs', 'cpmjson')) {
            $got = if ($landed) { @($landed.$name.args) } else { @() }
            for ($k = 0; $k -lt $values.Count; $k++) {
                $want = $values[$k]; $have = if ($k -lt $got.Count) { $got[$k] } else { '<missing>' }
                $ok = [string]::Equals($want, $have, 'Ordinal')
                if (-not $ok) { $fail++ }
                Write-Output ("{0} [{1}] {2}  sent={3}  got={4}" -f $name, $k, $(if ($ok) { 'ok  ' } else { 'DIFF' }), [CpmS0]::Quote($want), [CpmS0]::Quote([string]$have))
            }
            if ($got.Count -ne $values.Count) { $fail++; Write-Output "$name count: sent $($values.Count), got $($got.Count)" }
        }
        foreach ($name in @('cpmargs', 'cpmjson')) { [CpmS0]::Run($exe, @('mcp', 'remove', $name, '--scope', 'user'), $dir, 60000) | Out-Null }
        Write-Output $(if ($fail -eq 0) { 'C16 args: PASS' } else { "C16 args: $fail difference(s)" })
    }
    'plugins' {
        $dir = Ensure-Folder '5'
        $exe = Real-Claude
        $mkt = if ($Arg1) { $Arg1 } else { 'google/elephant-goldfish-model' }
        $plg = if ($Arg2) { $Arg2 } else { 'egm@egm' }
        $r = [CpmS0]::Run($exe, @('plugin', 'marketplace', 'add', $mkt), $dir, 180000)
        Write-Output "marketplace add exit=$($r[0])"
        $r = [CpmS0]::Run($exe, @('plugin', 'install', $plg), $dir, 180000)
        Write-Output "plugin install exit=$($r[0])"
        $reg = Join-Path $dir 'plugins\installed_plugins.json'
        if (Test-Path -LiteralPath $reg) {
            Get-Content -Raw -LiteralPath $reg | Select-String -Pattern '"installPath"\s*:\s*"([^"]+)"' -AllMatches | ForEach-Object { $_.Matches } | ForEach-Object { Write-Output ("installPath: " + $_.Groups[1].Value) }
        } else { Write-Output "No plugins\installed_plugins.json in the test folder" }
        Write-Output ("Inside the test folder: " + [bool](Get-ChildItem -LiteralPath (Join-Path $dir 'plugins') -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1))
    }
    'ctrlc' {
        if (-not $Arg1) { throw 'Usage: ctrlc <pid> [-Twice]' }
        $target = [int]$Arg1
        $start = [CpmS0]::CreationTime($target)
        if ($start -lt 0) { throw "Process $target is not running, or cannot be read." }
        $self = $MyInvocation.MyCommand.Path
        $sent = 0
        $t0 = Get-Date
        foreach ($round in @(1, 2)) {
            if ($round -eq 2 -and -not $Twice) { break }
            $p = Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$self`"", 'ctrlc-inner', $target) -WindowStyle Hidden -PassThru -Wait
            $sent++
            Write-Output ("Ctrl+C #{0} sent (helper exit {1})" -f $round, $p.ExitCode)
            $deadline = (Get-Date).AddSeconds(5)
            while ((Get-Date) -lt $deadline) {
                if ([CpmS0]::CreationTime($target) -ne $start) { break }
                Start-Sleep -Milliseconds 250
            }
            if ([CpmS0]::CreationTime($target) -ne $start) { break }
        }
        $gone = [CpmS0]::CreationTime($target) -ne $start
        Write-Output ("Process {0}: {1} after {2:N1}s and {3} Ctrl+C" -f $target, $(if ($gone) { 'ended' } else { 'STILL RUNNING' }), ((Get-Date) - $t0).TotalSeconds, $sent)
    }
    'ctrlc-inner' { exit ([CpmS0]::SendCtrlC([uint32]$Arg1)) }
    'teardown' {
        $exe = Real-Claude
        foreach ($d in Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue) {
            [CpmS0]::Run($exe, @('auth', 'logout'), $d.FullName, 30000) | Out-Null
        }
        Write-Output 'Signed out of the test folders.'
        Show-Names
        Write-Output "The built-in login file must still be there. Then remove $root yourself."
    }
    default {
        [Console]::Error.WriteLine('Usage: s0-checks.ps1 prep|status|names|procstart|keys|path|args|plugins|ctrlc <pid> [-Twice]|teardown')
        exit 1
    }
}
