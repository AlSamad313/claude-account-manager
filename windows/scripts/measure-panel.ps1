# Times `cpm list` against the Limits target in the design: 10 profiles and 5,000 transcripts,
# under 1 second. Port of scripts/measure-panel.sh. Uses generated data in a temporary
# USERPROFILE, so nothing real is read or written. This times reading the sessions, not a panel
# opening, because no panel exists until stage W3. Run build.ps1 first.
$ErrorActionPreference = 'Stop'
$cpm = Join-Path $PSScriptRoot '..\build\bin\cpm.exe' | Resolve-Path
$home0 = Join-Path ([IO.Path]::GetTempPath()) ("cpm-measure-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $home0 | Out-Null
$saved = $env:USERPROFILE
try {
    $env:USERPROFILE = $home0
    foreach ($i in 1..9) { & $cpm add "P$i" "p$i" '#000000' | Out-Null }

    $dirs = @("$home0\.claude") + @(Get-ChildItem "$home0\.claude-profiles\profiles" -Directory | Sort-Object Name | ForEach-Object FullName)
    $utf8 = New-Object Text.UTF8Encoding $false
    $filler = '{"type":"assistant","x":"' + ('y' * 2000) + '"}' + "`n"
    $small = $filler * 10    # 20 KB
    $large = $filler * 200   # 400 KB, past the 64 KB ends
    foreach ($n in 0..4999) {
        $d = "$($dirs[$n % $dirs.Count])\projects\p$($n % 20)"
        if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force $d | Out-Null }
        $sid = [guid]::NewGuid().ToString()
        $head = '{"type":"user","cwd":"C:\\w","entrypoint":"cli","sessionId":"' + $sid + '"}' + "`n"
        $title = '{"type":"ai-title","aiTitle":"Session ' + $n + '","sessionId":"' + $sid + '"}' + "`n"
        # One in ten is large. One in fifty has no title, which forces the wider read.
        $body = if ($n % 10 -eq 0) { $large } else { $small }
        $tail = if ($n % 50 -eq 0) { '' } else { $title }
        [IO.File]::WriteAllText("$d\$sid.jsonl", $head + $body + $tail, $utf8)
    }

    $t = [Diagnostics.Stopwatch]::StartNew(); & $cpm default | Out-Null; $t.Stop()
    'cpm default (no sessions read): {0:N2} s' -f $t.Elapsed.TotalSeconds
    foreach ($run in 1..3) {
        $t = [Diagnostics.Stopwatch]::StartNew()
        $count = @(& $cpm list).Count
        $t.Stop()
        "run ${run}: $count rows in {0:N2} s" -f $t.Elapsed.TotalSeconds
    }
    'Target: under 1 s. Each run is a new process, so the in-memory cache does not help here;'
    'the app keeps it between panel opens. The first run also pays for a cold file cache.'
}
finally {
    $env:USERPROFILE = $saved
    Remove-Item -Recurse -Force $home0 -ErrorAction SilentlyContinue
}
