# S0 checklist (Windows)

The table in `docs/designs/claude-profile-manager-windows.md` ("Windows S0 checks") defines each check
and its pass condition. This file gives the steps for the ones that need you. Record results in
`docs/S0-results-windows.md`. Test folders are under `%USERPROFILE%\.cpm-s0\`.

Use **Windows PowerShell** in **Windows Terminal** unless a step says otherwise. Scripts are not allowed
to run by default on this PC, so the helper below runs them with `-ExecutionPolicy Bypass` for that one
call only. Paste this first in each new window:

```powershell
$repo = 'D:\MultiAccount\claude-account-manager'
function s0 { powershell -NoProfile -ExecutionPolicy Bypass -File "$repo\windows\scripts\s0-checks.ps1" @args }
function in-folder($n) { $env:CLAUDE_CONFIG_DIR = "$env:USERPROFILE\.cpm-s0\$n" }
function out-folder { Remove-Item Env:CLAUDE_CONFIG_DIR -ErrorAction SilentlyContinue }
```

Already done on 2026-11-04: the backup, `s0 prep`, and the checks that need no sign-in (C5, C9 in
part, C13, C14, C16). See the results file.

## Part A: logins (C1, C2, C3)

C1 is the check that can stop the project. Each sign-in opens the browser. Check the browser is signed
into the account you want, or use a private window. Use a **different account for folder 2** if you have
one, so two accounts are seen side by side.

1. `s0 names`. Note the list: no Credential Manager names, and only the built-in login file.
2. For folder 1, then folder 2:

   ```powershell
   in-folder 1; claude auth login; out-folder
   s0 names
   ```

   **Expect:** after each, that folder's login file is `True`, and still no Credential Manager names.
3. `s0 status`. **Expect:** the built-in profile shows today's account, folder 1 and folder 2 their own,
   and `configDirectory` is each test folder (C2).
4. **Restart Windows.** Run `s0 status` again. **Expect:** all three still signed in, with the same accounts.
5. **C3:** `in-folder 1; claude auth logout; out-folder`, then `s0 status`. **Expect:** folder 1 is signed
   out and its login file is `False`. The built-in profile and folder 2 are still signed in. Sign folder 1
   back in as in step 2.

## Part B: sessions (C4, C6, C7, C8, C9)

These use folder 1 and a little of its account's usage.

- **C4, resume.** `cd $env:USERPROFILE\.cpm-s0\4; in-folder 1; claude`. Say "Remember the word banana",
  then `/exit` and note the session ID it prints. Run `claude --resume <id>` from the same folder, then
  `cd ~` and run it again. Ask "Which word?". **Expect:** banana, with the same ID. Then `/exit` and `out-folder`.
- **C8, title.** In a new Windows Terminal tab:

  ```powershell
  $host.UI.RawUI.WindowTitle = 'TEST TITLE'; $env:CLAUDE_CODE_DISABLE_TERMINAL_TITLE = '1'; in-folder 1; claude
  ```

  Ask it to run `dir`. **Expect:** the tab still says TEST TITLE at the end. `/exit`, then close the tab.
- **C7, stop.** Run it four times: idle and busy, each in Windows Terminal and in a classic console.
  1. Open the window. For a classic console, press Win+R and run `conhost.exe powershell`.
  2. In it: `in-folder 1; claude`, and send "Say hi".
  3. For the busy runs, send "Run the shell command sleep 120, then tell me it finished", and approve it.
  4. In another window, find the PID: `s0 procstart`. It is the `cli` row in `.cpm-s0\1`, with status `idle` or `busy`.
  5. Send Ctrl+C: `s0 ctrlc <pid>`.
  6. **Record:** whether it ended, after how long, and whether one Ctrl+C was enough. If it said STILL
     RUNNING, run `s0 ctrlc <pid> -Twice` and record that. Then record whether the window stayed open and
     what the shell shows.
- **C9, elevated.** Open Windows Terminal **as administrator**, run `in-folder 1; claude`, and leave it.
  In a normal window run `s0 procstart`. **Expect:** that row shows `running` with `diff=0.000s`, not
  `access denied`. `/exit` the admin session.
- **C6, login variables.** Dummy values only, never a real key:

  ```powershell
  foreach ($v in 'ANTHROPIC_API_KEY','ANTHROPIC_AUTH_TOKEN','CLAUDE_CODE_OAUTH_TOKEN') {
    Set-Item "Env:$v" 'dummy'; "$v -> " + (claude auth status --json); Remove-Item "Env:$v"
  }
  ```

  **Record** the `authMethod` and whether an account is shown, for each.

## Part C: other entry points (C10)

- **C10.** In folder 1, run `in-folder 1; claude -p "Say hi"; out-folder`, then in the Claude desktop app
  start a Code session, and run `s0 keys` while it runs. **Record** the `entrypoint` values. Cowork: if
  you use it, start a task and run `s0 keys` again.

## Later checks

These need something built first:
- At the start of stage W1: C11, C12, C17, C18, C19, C21, C22.
- At the start of stage W3: C20.
- C15 needs VS Code, which is not installed on this PC. Run it when it is.

After a Claude Code update, rerun only the checks whose subject changed. `s0 procstart`, `s0 keys` and
`s0 args` are quick and need no sign-in.

## Teardown, when all parts are done

`s0 teardown`. **Expect:** each test folder's login file `False`, the built-in one `True`, and no Credential
Manager names. Then remove `%USERPROFILE%\.cpm-s0` yourself. Keep
`%USERPROFILE%\claude-profile-backup-2026-11-04` until stage W1 works, then delete it,
because it can hold MCP secrets.
