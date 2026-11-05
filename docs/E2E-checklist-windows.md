# Hands-on test script: Windows

The Windows copy of `E2E-checklist.md`, with the same parts and step numbers. Where a step cannot match yet, it says
why. Stage W3 adds the tray app, so only copy setup (Part 8, stage W4) and removal (Part 11, stage W5) are still
missing. Steps marked "Windows only" have no macOS twin.

Work from top to bottom, because later parts depend on earlier ones. Allow about 2 to 3 hours for what exists now.
You can stop between any two parts.

Tick a box only when everything under **Expect** happened. If anything differs, don't work around it. Note the step
number and carry on if you can. "When something goes wrong" at the end says what to send.

Names used here:

- **Personal:** the account `~\.claude` is signed into today. This built-in profile has the command `main`.
- **Official:** your company account, added in Part 3 as the profile `WRP`, with the command `wrp`.
- **Test:** a throwaway profile, signed into Personal.

Paste each command into **Windows PowerShell** in Windows Terminal, one block at a time. Steps that use another
shell say so.

## Part 0: Before you start

- [ ] **0.1 Have ready:** the sign-ins for both accounts, a browser that can open a private window, and Windows
      Terminal.
- [ ] **0.2 VS Code:** not installed on this PC, so 6.11 and 6.12 are skipped. If you install it, close it now.
- [ ] **0.3 Record how things are now.** Keep this window's output for comparison.

  ```powershell
  cd <your clone of claude-account-manager>
  claude auth status
  powershell -NoProfile -ExecutionPolicy Bypass -File windows\scripts\s0-checks.ps1 names
  Get-FileHash $HOME\.claude\settings.json
  Test-Path $HOME\.claude-profiles
  [Environment]::GetEnvironmentVariable('Path', 'User')
  New-Item -ItemType Directory -Force $HOME\cpm-test\a, $HOME\cpm-test\b | Out-Null
  ```

  **Expect:** Personal is signed in. No Credential Manager names start with `Claude Code`. `Test-Path` says `False`.
  Keep the `Path` line: 2.2 compares against it.

**If plain `claude` ever stops working:** run `& $HOME\.local\bin\claude.exe`, which skips this tool. To turn the
tool off, run `cpm uninstall-shell` (or `& $HOME\.claude-profiles\bin\cpm.exe uninstall-shell`) and open a new
window. Your `~\.claude` is never written to.

**Safety rule:** only stop sessions this script tells you to start.

## Part 1: First launch, with no shell layer

If you already ran `cpm install-shell` after stage W2, the command line is set up: in 1.3, 1.7 and 1.8 you will not
see the notice, and in 2.1 the result says the folder was already first in `PATH`. That is fine.

- [ ] **1.1 Build and install.**

  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File windows\build.ps1 -Test -Install
  ```

  **Expect:** all tests pass, then "Installed in …\Programs\ClaudeProfiles and started". No Windows security prompt.
- [ ] **1.2 The icon.** **Expect:** a coloured dot in the notification area, or in the overflow (^) next to it, and a
      notification that says the icon may be there and can be dragged next to the clock. Drag it there. Hovering shows
      "Claude Profiles — plain claude uses" and the built-in profile's name. The panel has no taskbar button.
- [ ] **1.3 The panel** opened by itself above the tray. If not, left-click the dot. **Expect:** the title "Claude
      Profiles", "plain claude uses" and the built-in profile's name, and a "Filter by title, folder or profile"
      field. Also a "Set up command line first" notice, and the footer buttons Add profile…, Open window…, Copy setup…,
      Doctor and Exit, all on one row.
- [ ] **1.4 Look at the drawing.** The filter field takes typing, and the "⋯" menu opens. Nothing overlaps, is cut off
      or is unreadable, in light mode and in dark mode (Settings > Personalization > Colors). **Expect:** Esc closes the
      panel, and so does clicking somewhere else. Clicking the dot while the panel is open closes it.
- [ ] **1.5 Existing sessions.** **Expect:** your real Claude Code sessions are listed with sensible titles and folders.
      Desktop app sessions aren't shown.
- [ ] **1.6 Speed.** Close and reopen the panel a few times. **Expect:** no visible wait.
- [ ] **1.7 New Terminal is off.** **Expect:** it can't be clicked on any row.
- [ ] **1.8 Add profile… before setup.** **Expect:** a note to set up the command line first, and Continue to sign-in
      can't be clicked. Press Cancel.
- [ ] **1.9 Copy setup… with nothing to copy into.** **Expect:** "Add and sign in to another profile first. The
      built-in profile can be copied from, never into." Press Done.
- [ ] **1.10 Doctor.** **Expect:** your built-in account shows as signed in, and a fault says plain `claude` doesn't run
      the wrapper. Press Done.
- [ ] **1.11 Only one copy** (Windows only). Open the Start menu and start Claude Profiles again. **Expect:** no second
      dot; the panel of the running copy opens in front and has the focus (Esc closes it).
- [ ] **1.12 Where Windows lists it** (Windows only). **Expect:** Claude Profiles in Settings > Apps > Installed apps,
      and in Task Manager > Startup apps as enabled. Right-click the dot: Open Claude Profiles, Start with Windows
      (ticked) and Exit.

## Part 2: Set up the shell

- [ ] **2.1 Set up from the app.** In the panel's notice press Set up. **Expect:** the "Set up command line" step, with
      a box for PowerShell tab completion. Tick it and press Set up. **Expect:** "Done. Open a new terminal window to
      use claude-as.", a line about your user `PATH`, and the profile file the completion line went into. The notice
      is gone and New Terminal can be clicked.
- [ ] **2.2 Your `PATH` is intact.**

  ```powershell
  [Environment]::GetEnvironmentVariable('Path', 'User') -split ';'
  Get-Content $HOME\.claude-profiles\logs\writes.log | Select-String path
  ```

  **Expect:** the first entry is `…\.claude-profiles\bin`, and the rest match 0.3 in the same order. The log line
  holds the old value.
- [ ] **2.3 Open a new window and use it from now on.** Close Windows Terminal completely and open it again, so it
      reads the new `PATH`. Also note whether a new tab in an already open Terminal was enough (S0 check C12).

  ```powershell
  (Get-Command claude).Source
  (Get-Command cpm).Source
  cpm default
  claude auth status
  cpm doctor; "exit code $LASTEXITCODE"
  ```

  **Expect:** `claude` and `cpm` are under `.claude-profiles\bin`. The default is `main`, and the account is still
  Personal. Doctor shows `ok` for the real Claude Code path and for plain `claude` running the wrapper, with no
  `FAULT` lines and exit code 0.
- [ ] **2.4 Install by bare name, from another folder.** Nothing should be added twice.

  ```powershell
  cd $env:TEMP; cpm install-shell; cd -
  ([Environment]::GetEnvironmentVariable('Path', 'User') -split ';' | Where-Object { $_ -like '*claude-profiles*' }).Count
  ```

  **Expect:** "Installed to…" and "It was already first in your user PATH", then `1`.
- [ ] **2.5 Other ways in (design flow 5).** **Expect** Personal from each:
  - Command Prompt: `where claude` lists `.claude-profiles\bin\claude.exe` first, then `claude auth status`.
  - Git Bash: `which claude`, then `claude auth status`.
  - The Run dialog (Win+R): `cmd /k claude auth status`.
- [ ] **2.6 Tab completion** (Windows only). In a new PowerShell window type `claude-as ` and press Tab. **Expect:**
      `main`, and each further Tab moves through the profile names.

## Part 3: Add your official account from the app

This is the step most likely to feel broken, so watch it closely.

- [ ] **3.1 Start.** Panel: Add profile…. Display name `WRP`. **Expect:** the command name suggests `wrp`. Pick a
      colour, then Continue to sign-in.
- [ ] **3.2 A terminal window opens.** **Expect:** a window running `claude-as wrp auth login`, and the app on "Waiting
      for sign-in…". No Windows security prompt. If one appears, write down its wording. The panel will probably close
      by itself, which is fine.
- [ ] **3.3 Sign in as Official.** Your normal browser may already be on Personal, so copy the sign-in link from the
      terminal into a private window. Finish in the terminal.
- [ ] **3.4 The notification** (design D8). **Expect:** within about 5 seconds, a Windows notification "WRP signed in as
      <your official email>. Open to keep or retry." Click it. **Expect:** the panel on "Signed in", with your official
      email and organization. If no notification comes within 15 seconds, note it and click the dot instead.
- [ ] **3.5 Keep.** **Expect:** back to the main list, with a WRP row in your colour. Close the sign-in window.
- [ ] **3.6 Check both logins:**

  ```powershell
  claude-as wrp auth status
  claude auth status
  powershell -NoProfile -ExecutionPolicy Bypass -File windows\scripts\s0-checks.ps1 names
  cpm doctor
  ```

  **Expect:** Official, then Personal. Still no Credential Manager names. Doctor shows both signed in, with no faults.

## Part 4: Add from the command line, and the wrong-account paths

- [ ] **4.1 Bad input is refused.** Each should print an error and add nothing:

  ```powershell
  cpm add Bad --help '#000000'
  cpm add Bad 'a b' '#000000'
  cpm add Bad wrp '#000000'
  cpm add Bad bad red
  ```

- [ ] **4.2 Add Test.** `cpm add Test test '#2FA84F'`. **Expect:** the sign-in command and the `cpm keep` step.
- [ ] **4.3 Keep too early.** `cpm keep test`. **Expect:** refused, "has not signed in yet".
- [ ] **4.4 Sign in as Personal,** in your normal browser: `claude-as test auth login`.
- [ ] **4.5 Retry leaves other logins alone:**

  ```powershell
  cpm retry test
  claude-as test auth status
  claude auth status
  ```

  **Expect:** "Signed out", Test is logged out, and plain `claude` is **still Personal**.
- [ ] **4.6 Sign in again and keep:**

  ```powershell
  claude-as test auth login
  cpm keep test
  ```

  **Expect:** "Kept", plus a warning that another profile already uses this account.
- [ ] **4.7 Discard from the command line:**

  ```powershell
  cpm add Temp temp '#888888'
  cpm discard temp
  claude-as temp --version
  ```

  **Expect:** "Discarded … in the Recycle Bin, without its login", then "no profile named 'temp'" with a list of
  valid names.
- [ ] **4.8 Wrong account in the app.** Add profile…, name it `Temp2`, and sign in as Personal. **Expect:** "Signed in"
      with a warning that another profile already uses this account. Press Sign out and retry. **Expect:** back to
      waiting, with a new terminal window. Press Discard. **Expect:** the row has gone.
- [ ] **4.9 Leaving and coming back.** Add profile…, name it `Temp3`. Press Close on the waiting step, without signing
      in. **Expect:** the row shows Finish sign-in. Press it, then Discard.
- [ ] **4.10 Nothing left behind.** Close the extra windows, then:

  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File windows\scripts\s0-checks.ps1 names
  claude auth status
  ```

  **Expect:** no Credential Manager names. Plain `claude` is still Personal.

## Part 5: The default, and where it reaches

- [ ] **5.1 Start a session on the built-in.** In window A: `cd $HOME\cpm-test\a; claude`. Accept the trust prompt
      and send: `Say hello in one word.`
- [ ] **5.2 Change the default.** In window B: `cpm default wrp`. **Expect:** "Plain claude now uses WRP". Within a
      second or two the tray dot takes WRP's colour, and hovering it says "plain claude uses WRP".
- [ ] **5.3 Follows with no reload.** Still in window B:

  ```powershell
  claude auth status
  cmd /c claude auth status
  & 'C:\Program Files\Git\bin\bash.exe' -lc 'claude auth status'
  ```

  **Expect:** Official three times. This differs from macOS, where a login `bash` finds no `claude`: on Windows every
  shell reads `PATH` from the registry.
- [ ] **5.4 A running session doesn't move.** In window A, type `! claude auth status`. **Expect:** Personal. Then
      `/exit`.
- [ ] **5.5 New session follows.** In window A: `cd $HOME\cpm-test\b; claude`. Finish any first-run screens and send
      `Say hello in one word.` **Expect:** the tab title shows WRP, and the status line shows `● WRP` in its colour
      (S0 checks C8 and C11).
- [ ] **5.6 Nested stays put.** In window B: `cpm default main`. In window A: `! claude auth status`. **Expect:**
      Official. Then `/exit`.
- [ ] **5.7 Make default from the app.** On the WRP row press Make default. **Expect:** the DEFAULT mark and the tray
      dot move. Then set it back to `main` the same way.
- [ ] **5.8 A damaged default stops safely.** Right-click the dot and choose Exit first, because the running app
      repairs this file by itself. Then:

  ```powershell
  Remove-Item $HOME\.claude-profiles\default
  claude auth status
  cpm doctor; "exit code $LASTEXITCODE"
  cpm doctor --fix
  claude auth status
  ```

  **Expect:** the first `claude` refuses, "Claude Code was not started". Doctor shows a `FAULT` and exit code 2.
  After `--fix`, Personal works.
- [ ] **5.9 The app repairs it too.** Start Claude Profiles from the Start menu, then:

  ```powershell
  Remove-Item $HOME\.claude-profiles\default; Start-Sleep 3; Get-Content $HOME\.claude-profiles\default
  ```

  **Expect:** the file has come back. Doctor in the panel is clean.
- [ ] **5.10 Warnings.** The key is a dummy value, set only in this window:

  ```powershell
  claude-as nope
  $env:ANTHROPIC_API_KEY = 'x'; claude auth status; cpm doctor | Select-String ANTHROPIC; Remove-Item Env:ANTHROPIC_API_KEY
  $env:CLAUDE_CONFIG_DIR = "$HOME\.claude"; claude --version; Remove-Item Env:CLAUDE_CONFIG_DIR
  ```

  **Expect:** a list of valid names; a warning that the key is set; a Doctor note; and a message that this session
  ignores your default.

## Part 6: Sessions from the command line

- [ ] **6.1 Start.** `cpm new wrp $HOME\cpm-test\a`. **Expect:** a new Terminal window or tab opens. Send
      `Remember the word banana.` Then, in window B, run `cpm list --live`. **Expect:** a WRP row within 5 seconds.
      Copy its session ID, the last column. Below, `<id>` means this ID.
- [ ] **6.2 The title holds.** **Expect:** after Claude has replied, the tab title still shows WRP (S0 check C8).
- [ ] **6.3 Live resume is refused.** `cpm resume <id>`. **Expect:** "already running", with a process ID.
- [ ] **6.4 Find and resume.** Type `/exit` in that session. **Expect:** its window closes, because `cpm` opened it
      and Claude Code ended normally (design D6; on macOS it stays open). Then:

  ```powershell
  cpm list --profile wrp
  cpm resume <id>
  ```

  In the new window ask `What word did I ask you to remember?` and run `! pwd`. **Expect:** banana, a path ending in
  `cpm-test/a`, and WRP in the title.
- [ ] **6.5 Wrong profile is refused.** Leave that session running. In window B:

  ```powershell
  claude-as main --resume <id>
  claude --resume <id>
  ```

  **Expect:** both say it belongs to WRP, give the right command, and don't start.
- [ ] **6.6 Stop a busy session.** In the WRP session send `Run the shell command sleep 120, then tell me it finished.`
      and approve it. Then in window B:

  ```powershell
  cpm list --live
  cpm stop <id>
  ```

  **Expect:** `running busy` or `running shell`, a warning about an unfinished edit or a command that may keep
  running, and a note that Ctrl+C reaches every program in that window. Answer `n`. **Expect:** "Not stopped", and
  the session continues.
- [ ] **6.7 Stop it for real.** `cpm stop <id>` and answer `y`. **Expect:** "Stopped with Ctrl+C", then
      `sleep.exe` listed as still running and left alone. The window closes, because `cpm` opened it. `cpm list --live`
      no longer shows it. The `sleep` ends by itself within 2 minutes:
      `Get-Process sleep -ErrorAction SilentlyContinue`.
- [ ] **6.8 Stop, again and unknown.**

  ```powershell
  cpm stop <id> --yes
  cpm resume 99999999-2222-3333-4444-555555555555
  ```

  **Expect:** "not running", then "No profile holds session". Nothing starts.
- [ ] **6.9 A hostile folder name.** This must not create a file. The single quotes keep PowerShell from running it:

  ```powershell
  $odd = $HOME + '\cpm-test\odd & %PATH% ^ ; $(New-Item PWNED) name'
  New-Item -ItemType Directory $odd | Out-Null
  cpm new wrp $odd
  ```

  **Expect:** the session opens in that folder (`! pwd` shows it). Type `/exit`, then
  `Get-ChildItem $HOME\cpm-test, $HOME -Filter PWNED -Recurse -Depth 2` prints nothing.
- [ ] **6.10 Hand-started sessions count.** In window A: `cd $HOME\cpm-test\b; claude-as test`, and send a message.
      **Expect:** `cpm list --live` shows it on `test`. Keep it running for 6.13.
- [ ] **6.11 VS Code, closed.** Skip while VS Code is not installed; then `cpm code wrp` says "VS Code was not found".
      If it is installed: `cpm code wrp $HOME\cpm-test\a`. In VS Code's Claude Code, check the account, and in its
      terminal run `claude auth status`. **Expect:** Official.
- [ ] **6.12 VS Code, open.** `cpm code main`. **Expect:** it says to quit VS Code first, and nothing changes. Then quit
      VS Code.
- [ ] **6.13 Stop a window you opened yourself** (Windows only). `cpm stop <its id> --yes`. **Expect:** "Stopped with
      Ctrl+C", and window A stays open, back at the PowerShell prompt.
- [ ] **6.14 Started from inside Claude Code** (Windows only). In any Claude Code session, run `! cpm new test`. In the
      new window send `Say hi in one word.`, then `/exit`. **Expect:** `cpm list --profile test` shows that session
      with a title, so its transcript was saved (design flow 2, "Fresh environment").

## Part 7: Sessions from the app

- [ ] **7.1 New Terminal.** Panel: New Terminal on WRP. **Expect:** a terminal window with WRP in its title, no
      security prompt, and a running row within 5 seconds of reopening the panel. It opens in your home folder, so a
      trust prompt is normal. Send a short message, then `/exit`. **Expect:** the window closes (design D6).
- [ ] **7.2 The list stays fresh.** Close the panel. Run `cpm new test $HOME\cpm-test\b` and send a message. Reopen the
      panel. **Expect:** that session appears within a few seconds. If it doesn't update while the panel is open, note
      that.
- [ ] **7.3 Stop, busy.** In that session send `Run the shell command sleep 120, then tell me it finished.` and approve
      it. Press Stop on its row. **Expect:** a warning that Claude is working or that a command may keep running, and
      a note that Ctrl+C reaches every program in that window. Press Cancel, then Stop again, then Stop session.
      **Expect:** the row stops being live within 10 seconds, and a notice names `sleep.exe` as still running and left
      alone. The window closes, because the app opened it.
- [ ] **7.4 Filter and resume.** Type part of a title, then a folder, then a profile name. **Expect:** the list narrows
      each time. Press Resume on a WRP session. **Expect:** it continues in the right profile and folder. Then `/exit`.
- [ ] **7.5 Missing folder.** Run `cpm new wrp $HOME\cpm-test\b`, send a message, `/exit`, then
      `Remove-Item -Recurse $HOME\cpm-test\b`. Press Resume on it. **Expect:** "no longer exists", and Choose folder….
      Choose `cpm-test\a`. **Expect:** the panel stays open behind the folder dialog, and the session resumes there,
      or a window says clearly why not. Then `/exit`, and `New-Item -ItemType Directory $HOME\cpm-test\b`.
- [ ] **7.6 "⋯" menu.** On WRP, Open VS Code. With VS Code not installed, **Expect:** "VS Code was not found". If it is
      installed: it opens on Official; with it open, try again, and **Expect:** it says to quit first.
- [ ] **7.7 Settings.** "⋯", Settings… on WRP. Change the name to `WRP2` and the colour, then Done. **Expect:** the row
      and dot update, and the command stays `wrp`. A new `claude-as wrp` shows `● WRP2`. Change it back. On `main`,
      **Expect:** a note that it is your existing `.claude` folder. (Remove comes in stage W5.)
- [ ] **7.8 The window.** Open window…. **Expect:** a sidebar and a table. Selecting a profile filters, and the New
      Terminal button follows the selection. Right-click a profile: New Terminal, Open VS Code, Make default,
      Settings…. Right-click a session: Resume (or Stop… on a live one), and copy and open folder items. Enter resumes
      the selected session, and Delete on a live one asks to stop it. The window has a taskbar button.
- [ ] **7.9 Panel and window together.** With the window open, press Add profile… in the panel. **Expect:** the same
      step also appears over the window. Decide whether that bothers you, then Cancel.
- [ ] **7.10 Doctor** from the window's More menu lists every profile, and Done closes it.
- [ ] **7.11 CPU and memory.** Close the panel and window, and wait a minute. Look up Claude Profiles in Task Manager
      > Details (`ClaudeProfiles.exe`). **Expect:** about 0% CPU. Note its memory (design target: under 100 MB).
- [ ] **7.12 The window remembers** (Windows only). Move and resize the window, close it, and open it again. **Expect:**
      the same place and size. Closing it leaves the dot in the tray.
- [ ] **7.13 Start with Windows** (Windows only). Right-click the dot and clear Start with Windows. **Expect:** Task
      Manager > Startup apps no longer lists it (press Refresh, or reopen Task Manager). Tick it again.

## Part 8: Copy setup

Exit every WRP and Test session first. Variables for this part, in one PowerShell window that you keep open:

```powershell
function Dir-Of($c) { (Get-Content $HOME\.claude-profiles\names | Where-Object { $_.StartsWith("$c`t") }).Split("`t")[4] }
$W = Dir-Of wrp; $T = Dir-Of test
$W; $T
```

- [ ] **8.1 The list.** Panel: Copy setup…. **Expect:** From is Personal and To is WRP. Group headings (MCP SERVERS,
      SETTINGS, SKILLS…, PLUGINS) and Add, Change or Same on each row. An MCP server with `env` values is unticked,
      with a warning that it may contain a secret and that other programs can read it while it is being added. **No
      `skills\synced`.** The To list never offers Personal. Nothing is cut off, in light and dark mode.
- [ ] **8.2 Secrets are masked.** Tick a server that has `env` values. **Expect:** `Include env.… = ••••••` rows, each
      unticked, and no real value anywhere.
- [ ] **8.3 Nothing ticked.** Untick everything. **Expect:** the Copy button is off.
- [ ] **8.4 Refused while live.** Start `claude-as wrp` in another window. In the app, switch To to Test and back to
      WRP. **Expect:** "WRP has a live session", and Copy is off. `/exit`, switch To away and back. **Expect:** the
      message clears.
- [ ] **8.5 Copy a small set.** Tick only one skill, one plugin if you have any, and one server that has secrets, with
      its secrets unticked. Copy. **Expect:** "Copied 3 items." (fewer if you have no plugin), a backup note, and
      `claude-as wrp …` commands to remove the plugin and the server. Copy those commands somewhere.
- [ ] **8.6 It worked.**

  ```powershell
  Get-ChildItem "$W\skills"
  claude-as wrp plugin list
  claude-as wrp mcp list
  Get-FileHash $HOME\.claude\settings.json
  ```

  **Expect:** the skill, the plugin and the server, with no secret values in `claude-as wrp mcp get <name>`. The hash
  matches 0.3, unless you changed your settings yourself.
- [ ] **8.7 In a real session.** `claude-as wrp`. **Expect:** the status line still shows `● WRP`. Ask `Which skills
      can you see?` **Expect:** the copied one is listed. `/exit`.
- [ ] **8.8 A ticked secret, with a fake value:**

  ```powershell
  claude-as test mcp add sectest --scope user -e TOKEN=fake123 -- cmd /c echo hi
  ```

  Copy from Test to WRP with `sectest` and its `env.TOKEN` ticked. Then `claude-as wrp mcp get sectest`. **Expect:**
  it has TOKEN.
- [ ] **8.9 Changing an MCP server:**

  ```powershell
  claude-as wrp mcp add copytest --scope user -- cmd /c echo one
  claude-as test mcp add copytest --scope user -- cmd /c echo two
  ```

  Copy from WRP to Test. **Expect:** `copytest` shows Change. Tick it and copy. `claude-as test mcp get copytest`
  shows `one`.
- [ ] **8.10 Skip, Overwrite, Rename:**

  ```powershell
  Set-Content "$W\CLAUDE.md" one; Set-Content "$T\CLAUDE.md" two
  ```

  Copy from WRP to Test, with `CLAUDE.md` ticked. **Expect:** a Change badge and "If it exists:" with Skip,
  Overwrite and Rename. Do this three times, checking after each with `Get-ChildItem $T -Filter CLAUDE*; Get-Content "$T\CLAUDE.md"`:
  **Rename** gives `CLAUDE-copy.md` and leaves `two`. **Skip** changes nothing. **Overwrite** gives `one`.
- [ ] **8.11 Changed after the preview.** Open the preview with `CLAUDE.md` ticked as a Change, in the window so it
      stays open. In PowerShell, `Set-Content "$T\CLAUDE.md" three`. Press Copy. **Expect:** "changed since the
      preview. Nothing was copied", and `three` is still there.
- [ ] **8.12 Restore, and later work.** Run `Remove-Item "$T\CLAUDE-copy.md"; Set-Content "$T\CLAUDE.md" base;
      Set-Content "$W\CLAUDE.md" new`, and copy from WRP to Test with Overwrite. Then `Set-Content "$T\CLAUDE.md" edited`.
      Press Restore… and choose the newest. **Expect:** it lists `CLAUDE.md` as left alone, and `edited` is still there.
- [ ] **8.13 Read-only comes back** (Windows only). `Set-Content "$T\CLAUDE.md" base; Set-ItemProperty "$T\CLAUDE.md"
      IsReadOnly $true`. Copy from WRP to Test with Overwrite. **Expect:** it copies. Restore… the newest. **Expect:**
      `Get-Content "$T\CLAUDE.md"` says `base` and `(Get-Item "$T\CLAUDE.md").IsReadOnly` is `True`. Then
      `Set-ItemProperty "$T\CLAUDE.md" IsReadOnly $false`.
- [ ] **8.14 Restore undoes what's unchanged.** From Personal, To WRP, Restore… and pick the backup from 8.5.
      **Expect:** the skill is gone from `$W\skills`, and it shows the commands for what it can't undo.
- [ ] **8.15 The plugin and the server.** Run the commands you saved in 8.5, then `claude-as wrp plugin list` and
      `claude-as wrp mcp list`. **Expect:** both have gone. Also run `claude-as test mcp remove sectest --scope user`,
      `claude-as wrp mcp remove sectest --scope user`, and the same for `copytest` in both.

## Part 9: Reboot

- [ ] **9.1 Restart Windows** and sign in. **Expect:** the dot appears by itself (design D4; on macOS the app does not
      start by itself), without opening the panel.
- [ ] **9.2 All logins survived:**

  ```powershell
  claude auth status
  claude-as wrp auth status
  claude-as test auth status
  cpm doctor
  ```

  **Expect:** Personal, Official, Personal, and no faults. No Windows security prompts.

## Part 10: Desktop app, Cowork and rebuilds

- [ ] **10.1 Desktop app (S0 check C19).** Start a Code session in the Claude desktop app. **Expect:** `cpm list` does
      not show it, `cpm doctor` has no new fault, and Personal is still signed in afterwards.
- [ ] **10.2 Rebuild while a session runs.** With a session open, run
      `powershell -NoProfile -ExecutionPolicy Bypass -File windows\build.ps1 -Install`. **Expect:** it finishes,
      the dot goes away and comes back (the app is replaced and restarted), the running session is unaffected, and
      `Get-ChildItem $HOME\.claude-profiles\bin` shows an `.old-…` file, which the next install removes. The app
      refreshes `bin` from its own copy when it starts.

## Part 11: Removal

Stage W5. Until then, `cpm discard` (4.7) is the only way to remove a profile, and only before it is kept.

## Part 12: Finish

- [ ] **12.1 Optional: take the tool out of `PATH`.** `cpm uninstall-shell`, then open a new window. **Expect:**
      `(Get-Command claude).Source` is the real Claude Code, Personal is still signed in, and `claude-as ` + Tab no
      longer completes (the marked line is gone from your PowerShell profile). Your profiles stay.
      `cpm install-shell --completion` puts it back.
- [ ] **12.2 Optional: uninstall the app** (Windows only). Settings > Apps > Installed apps > Claude Profiles >
      Uninstall. **Expect:** the dot goes away, then a message that profiles and the command line tools are kept. The
      Start menu entry, the Startup apps entry and `%LOCALAPPDATA%\Programs\ClaudeProfiles` are gone, and
      `cpm list` still works. `build.ps1 -Install` puts the app back.
- [ ] **12.3** Delete `$HOME\cpm-test` if you like.

## When something goes wrong

Send the step number, what you saw (a screenshot of the window helps), and the last lines of these. If the app
showed "Something went wrong", its details are on an `error` line in `writes.log`.

```powershell
Get-Content $HOME\.claude-profiles\logs\writes.log -Tail 20
Get-Content $HOME\.claude-profiles\logs\launch.log -Tail 20
cpm doctor
```

Never send `.credentials.json` or anything from a `sessions` folder's `.key` files.
