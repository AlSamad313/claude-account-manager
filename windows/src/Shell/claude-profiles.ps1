# Tab completion of profile names after claude-as, for PowerShell. Loaded by one marked line in
# $PROFILE, which "Set up command line" adds only if its checkbox is ticked (design D1).
# Reads names, as the wrapper does. Does nothing if names cannot be read.
Register-ArgumentCompleter -Native -CommandName 'claude-as', 'claude-as.exe' -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)
    # Only the first argument is a profile name.
    $elements = @($commandAst.CommandElements)
    if ($elements.Count -gt 2 -or ($elements.Count -eq 2 -and $wordToComplete -eq '')) { return }
    $names = Join-Path $env:USERPROFILE '.claude-profiles\names'
    try { $lines = [IO.File]::ReadAllLines($names) } catch { return }
    foreach ($line in $lines) {
        $f = $line.Split("`t")
        if ($f.Count -lt 5 -or -not $f[0].StartsWith($wordToComplete)) { continue }
        [System.Management.Automation.CompletionResult]::new($f[0], $f[0], 'ParameterValue', $f[2])
    }
}
