# Hands-on test script

Work from top to bottom, because later parts depend on earlier ones. Allow about 2 to 3 hours. You can stop between any two parts.

Tick a box only when everything under **Expect** happened. If anything differs, don't work around it. Note the step number and
carry on if you can. "When something goes wrong" at the end says what to send.

Names used here:

- **Personal:** the account `~/.claude` is signed into today. This built-in profile has the command `main`.
- **Official:** a second Claude account, which becomes the profile `Work`, with the command `work`.
- **Test:** a throwaway profile, signed into Personal, and removed in Part 11.

Paste each command into Terminal.app, one block at a time.

## Part 0: Before you start

- [ ] **0.1 Have ready:** the sign-ins for both accounts, a browser that can open a private window, and Terminal.app.
- [ ] **0.2 Quit VS Code** with Cmd-Q. The desktop Claude app can stay open.
- [ ] **0.3 Record how things are now.** Keep this window's output for comparison.

  ```sh
  cd ~/claude-account-manager
  claude auth status
  zsh scripts/s0-checks.sh names
  shasum ~/.claude/settings.json
  ls ~/.claude-profiles
  mkdir -p ~/cpm-test/a ~/cpm-test/b
  ```

  **Expect:** Personal is signed in. Keychain shows only the plain `Claude Code-credentials`. `~/.claude-profiles` does not exist.

**If plain `claude` ever stops working:** run `/opt/homebrew/bin/claude`, which skips this tool. To turn the tool off, delete the two added
lines from `~/.zshenv` and `~/.zshrc` and open a new window. Your `~/.claude` is never written to.

**Safety rule:** only press Stop on sessions this script tells you to start.

## Part 1: First launch, with no shell layer

- [ ] **1.1 Build.** Run `./make-app.sh --install`. **Expect:** it finishes with no error.
- [ ] **1.2 Open it the way a normal app opens.** In Finder press Cmd-Shift-G, go to `~/Applications`, and double-click `ClaudeProfiles`.
      Don't use Terminal for this. **Expect:** a coloured dot in the menu bar, and no Dock icon. If you can't see the dot, it may be hidden
      behind the notch or a full bar. Note that, and quit some other menu-bar apps to check.
- [ ] **1.3 Open the panel** by clicking the dot. **Expect:** the title "Claude Profiles", "plain claude uses" and the built-in profile's name, and a
      filter field. Also a "Set up shell first" notice, and the footer buttons Add profile…, Open window…, Copy setup…, Doctor and Quit.
- [ ] **1.4 Look at the drawing.** The filter field takes typing, and the "⋯" menu opens. Nothing overlaps, is cut off or is unreadable.
- [ ] **1.5 Existing sessions.** **Expect:** your real Claude Code sessions are listed with sensible titles and folders. Desktop app sessions aren't shown.
- [ ] **1.6 Speed.** Close and reopen the panel a few times. **Expect:** no visible wait.
- [ ] **1.7 New Terminal is off.** **Expect:** it can't be clicked on any row.
- [ ] **1.8 Add profile… before setup.** **Expect:** "Set up the shell first". Press Cancel.
- [ ] **1.9 Copy setup… before setup.** **Expect:** "Add and sign in to another profile first". Press Done.
- [ ] **1.10 Doctor.** **Expect:** your built-in account shows as signed in, and a fault says plain `claude` doesn't run the wrapper. Press Done.

## Part 2: Set up the shell

- [ ] **2.1 Install.** In the panel: Set up shell…, then Install. **Expect:** it shows two commands. Press Copy commands, then Done.
- [ ] **2.2 Add the lines.** Paste into Terminal and press Return, then run:

  ```sh
  tail -2 ~/.zshenv
  tail -1 ~/.zshrc
  ```

  **Expect:** your old last `.zshenv` line is intact, and each new line is on a line of its own.
- [ ] **2.3 Open a new Terminal window** (Cmd-N) and use it from now on. Run:

  ```sh
  whence -p claude
  whence -p cpm
  cpm default
  claude auth status
  cpm doctor; echo "exit code $?"
  ```

  **Expect:** `claude` and `cpm` are under `~/.claude-profiles/bin`. The default is `main`, and the account is still Personal. Doctor shows `ok` for
  the real Claude Code path and "Plain claude runs the wrapper", with no `FAULT` lines and exit code 0.
- [ ] **2.4 Install by bare name, from another folder.** This was a bug the audit fixed. Don't add the lines again.

  ```sh
  (cd /tmp && cpm install-shell)
  grep -c claude-profiles ~/.zshenv ~/.zshrc
  ```

  **Expect:** "Installed to…" and no "Missing" error. Each file shows `1`, so nothing was added twice.
- [ ] **2.5 Panel again.** **Expect:** the notice has gone and New Terminal is on, within a few seconds of reopening.

## Part 3: Add your official account from the app

This is the step most likely to feel broken, so watch it closely.

- [ ] **3.1 Start.** Panel: Add profile…. Display name `Work`. **Expect:** the command name suggests `work`. Pick a colour, then Continue to sign-in.
- [ ] **3.2 Terminal opens.** **Expect:** a window running `claude-as work auth login`, and the app on "Waiting for sign-in…". **No macOS permission prompt** (S0 check 9).
      If one appears, write down its exact wording. The panel will probably close by itself, which is fine.
- [ ] **3.3 Sign in as Official.** Your normal browser may already be on Personal, so copy the sign-in link from Terminal into a private window. Finish in Terminal.
- [ ] **3.4 Click the dot.** **Expect:** the waiting step, or "Signed in", with your official email and organization within about 5 seconds. If it stays on waiting for 15
      seconds, note it, close and reopen the panel, then try Open window….
- [ ] **3.5 Keep.** **Expect:** back to the main list, with a Work row in your colour. Close the sign-in Terminal window.
- [ ] **3.6 Check both logins, in a Terminal window:**

  ```sh
  claude-as work auth status
  claude auth status
  zsh scripts/s0-checks.sh names
  cpm doctor
  ```

  **Expect:** Official, then Personal. Keychain has the plain item and one new `Claude Code-credentials-…` item. Doctor shows both signed in, with no faults.

## Part 4: Add from the command line, and the wrong-account paths

- [ ] **4.1 Bad input is refused.** Each should print an error and add nothing:

  ```sh
  cpm add Bad --help '#000000'
  cpm add 'Bad' 'a b' '#000000'
  cpm add Bad work '#000000'
  cpm add Bad bad red
  ```

- [ ] **4.2 Add Test.** Run `cpm add Test test '#2FA84F'`. **Expect:** it prints the sign-in command and the `cpm keep` step.
- [ ] **4.3 Keep too early.** Run `cpm keep test`. **Expect:** refused, "has not signed in yet".
- [ ] **4.4 Sign in as Personal,** in your normal browser: `claude-as test auth login`.
- [ ] **4.5 Retry leaves other logins alone:**

  ```sh
  cpm retry test
  claude-as test auth status
  claude auth status
  ```

  **Expect:** "Signed out", Test is logged out, and plain `claude` is **still Personal**.
- [ ] **4.6 Sign in again and keep:**

  ```sh
  claude-as test auth login
  cpm keep test
  ```

  **Expect:** "Kept", plus a warning that another profile already uses this account.
- [ ] **4.7 Discard from the command line:**

  ```sh
  cpm add Temp temp '#888888'
  cpm discard temp
  claude-as temp --version
  ```

  **Expect:** "Discarded", then "no profile named 'temp'" with a list of valid names.
- [ ] **4.8 Wrong account in the app.** Add profile…, name it `Temp2`, and sign in as Personal. **Expect:** "Signed in" with a duplicate warning. Press Sign out and retry.
      **Expect:** back to waiting, with a new Terminal window. Press Discard. **Expect:** the row has gone.
- [ ] **4.9 Leaving and coming back.** Add profile…, name it `Temp3`. Press Close on the waiting step, without signing in. **Expect:** the row shows Finish sign-in. Press it, then Discard.
- [ ] **4.10 Nothing left behind.** Close the extra Terminal windows, then run:

  ```sh
  zsh scripts/s0-checks.sh names
  claude auth status
  ```

  **Expect:** only the plain item plus those for Work and Test. Plain `claude` is still Personal.

## Part 5: The default, and where it reaches

- [ ] **5.1 Start a session on the built-in.** In window A:

  ```sh
  cd ~/cpm-test/a && claude
  ```

  Accept the trust prompt and send: `Say hello in one word.`
- [ ] **5.2 Change the default with the panel closed.** In window B: `cpm default work`. **Expect:** "Plain claude now uses Work", the dot changes to Work's colour within a
      few seconds, and the panel header agrees.
- [ ] **5.3 Follows with no reload.** Still in window B:

  ```sh
  claude auth status
  bash -c 'claude auth status'
  bash -lc 'claude auth status'
  ```

  **Expect:** Official, Official, then `claude: command not found`. If a `bash -l` shell has no Homebrew on its `PATH`, it finds no `claude`. Otherwise you'll see Personal. Either way this is
  a documented exception, and it doesn't follow the default.
- [ ] **5.4 A running session doesn't move.** In window A, type `! claude auth status`. **Expect:** Personal. Then `/exit`.
- [ ] **5.5 New session follows.** In window A:

  ```sh
  cd ~/cpm-test/b && claude
  ```

  Finish any first-run screens and send `Say hello in one word.` **Expect:** the title shows Work, and the status line shows `● Work` (S0 check 10).
- [ ] **5.6 Nested stays put.** In window B: `cpm default main`. In window A: `! claude auth status`. **Expect:** Official. Then `/exit`.
- [ ] **5.7 Make default from the app.** On the Work row press Make default. **Expect:** the DEFAULT mark and dot move. Then set it back to `main` the same way.
- [ ] **5.8 A damaged default stops safely.** Press Quit in the panel first, because the running app repairs this file by itself. Then:

  ```sh
  rm ~/.claude-profiles/default
  claude auth status
  cpm doctor; echo "exit code $?"
  cpm doctor --fix
  claude auth status
  ```

  **Expect:** the first `claude` refuses, "Claude Code was not started". Doctor shows a `FAULT` with a non-zero exit code. After `--fix`, Personal works.
- [ ] **5.9 The app repairs it too.** Reopen the app from `~/Applications`, then:

  ```sh
  rm ~/.claude-profiles/default; sleep 5; cat ~/.claude-profiles/default
  ```

  **Expect:** the file has come back. Doctor in the panel is clean.
- [ ] **5.10 Warnings:**

  ```sh
  claude-as nope
  ANTHROPIC_API_KEY=x claude auth status
  ANTHROPIC_API_KEY=x cpm doctor | grep ANTHROPIC
  CLAUDE_CONFIG_DIR=$HOME/.claude claude --version
  ```

  **Expect:** a list of valid names; a warning that the key is set; a Doctor note; and a message that this session ignores your default.

## Part 6: Sessions from the command line

- [ ] **6.1 Start.** `cpm new work ~/cpm-test/a`. **Expect:** a new window opens. Send `Remember the word banana.` and then, in another window, run `cpm list --live`.
      **Expect:** a Work row within 5 seconds. Copy its session ID, which is the last column. Below, `<id>` means this ID.
- [ ] **6.2 The title holds.** **Expect:** after Claude has replied, the window title still shows Work (S0 check 19).
- [ ] **6.3 Live resume is refused.** `cpm resume <id>`. **Expect:** "already running".
- [ ] **6.4 Find and resume.** Type `/exit` in that session, then:

  ```sh
  cpm list --profile work
  cpm resume <id>
  ```

  In the new window ask `What word did I ask you to remember?` and run `! pwd`. **Expect:** banana, `~/cpm-test/a`, and Work in the title.
- [ ] **6.5 Wrong profile is refused.** Leave that session running. In another window:

  ```sh
  claude-as main --resume <id>
  claude --resume <id>
  ```

  **Expect:** both say it belongs to Work, give the right command, and don't start.
- [ ] **6.6 Stop a busy session.** In the Work session send `Run the shell command sleep 120, then tell me it finished.` and approve it. Then in another window:

  ```sh
  cpm list --live
  cpm stop <id>
  ```

  **Expect:** `running busy`, and a warning about an unfinished edit. Answer `n`. **Expect:** "Not stopped", and the session continues.
- [ ] **6.7 Stop it for real.** `cpm stop <id>` and answer `y`. **Expect:** "Stopped with…", the window stays open, and `cpm list --live` drops it within 10 seconds.
- [ ] **6.8 Stop, again and unknown.**

  ```sh
  cpm stop <id> --yes
  cpm resume 99999999-2222-3333-4444-555555555555
  ```

  **Expect:** "not running", then "No profile holds session". Nothing starts.
- [ ] **6.9 A hostile folder name.** This command must not create a file:

  ```sh
  mkdir -p ~/cpm-test/'odd '"'"'$(touch PWNED) name'
  cpm new work ~/cpm-test/'odd '"'"'$(touch PWNED) name'
  ```

  **Expect:** the session opens in that folder. Type `/exit`, then `find ~/cpm-test ~ -maxdepth 2 -name PWNED` prints nothing.
- [ ] **6.10 Hand-started sessions count.** In Terminal, `cd ~/cpm-test/b && claude-as test`, and send a message. **Expect:** `cpm list --live` shows it on `test`. Then `/exit`.
- [ ] **6.11 VS Code, closed.** `cpm code work ~/cpm-test/a`. In VS Code's Claude Code, check the account, and in its terminal run `claude auth status`. **Expect:** Official.
- [ ] **6.12 VS Code, open.** `cpm code main`. **Expect:** it says to quit VS Code first, and nothing changes. Then quit VS Code.

## Part 7: Sessions from the app

- [ ] **7.1 New Terminal.** Panel: New Terminal on Work. **Expect:** a window with Work in its title, no permission prompt, and a running row within 5 seconds of reopening the panel.
      It first asks for a folder: Cancel must start nothing, then try again, choose one and press Open Terminal. A trust prompt is normal in a new folder. Send a short message, then `/exit`.
- [ ] **7.2 The list stays fresh.** Close the panel. Run `cpm new test ~/cpm-test/b` and send a message. Reopen the panel. **Expect:** that session appears within a few seconds. If it doesn't update
      while the panel is open, note that. This is a known risk.
- [ ] **7.3 Stop, busy.** In that session send `Run the shell command sleep 120, then tell me it finished.` and approve it. Press Stop on its row. **Expect:** a warning that Claude is working. Press Cancel,
      then Stop again, then Stop session. **Expect:** the window stays open, and the row stops being live within 10 seconds.
- [ ] **7.4 Filter and resume.** Type part of a title, then a folder, then a profile name. **Expect:** the list narrows each time. Press Resume on a Work session. **Expect:** it continues in the right profile and folder.
      Then `/exit`.
- [ ] **7.5 Missing folder.** Run `cpm new work ~/cpm-test/b`, send a message, `/exit`, then `rm -r ~/cpm-test/b`. Press Resume on it. **Expect:** "no longer exists", and "Choose folder…". Choose `~/cpm-test/a`.
      **Expect:** it resumes there, or says clearly why not. Then `/exit`, and `mkdir ~/cpm-test/b`.
- [ ] **7.6 "⋯" menu.** On Work, Open VS Code. **Expect:** it opens on Official. With it open, try again. **Expect:** it says to quit first. Then quit VS Code.
- [ ] **7.7 Settings.** "⋯", Settings… on Work. Change the name to `Work2` and the colour, then Done. **Expect:** the row and dot update, and the command stays `work`. A new `claude-as work` shows `● Work2`.
      Change it back. On `main`, **Expect:** no Remove button, and a note that it is your existing `~/.claude`.
- [ ] **7.8 The window.** Open window…. **Expect:** a sidebar and a table. Selecting a profile filters, and the New Terminal button follows the selection. Right-click a profile: New Terminal, Open VS Code, Make default, Settings….
- [ ] **7.9 Panel and window together.** With the window open, press Add profile… in the panel. **Expect:** the same step also appears as a sheet in the window. Decide whether that bothers you, then Cancel.
- [ ] **7.10 Doctor sheet** from the window lists every profile, and Done closes it.
- [ ] **7.11 CPU and memory.** Close the panel and window, and wait a minute. Look up `ClaudeProfiles` in Activity Monitor. **Expect:** about 0% CPU. Note its memory, which was about 74 MB at launch with no profiles.

## Part 8: Copy setup

Exit every Work and Test session first. Variables for this part:

```sh
W=$(awk -F'\t' '$1=="work"{print $5}' ~/.claude-profiles/names)
T=$(awk -F'\t' '$1=="test"{print $5}' ~/.claude-profiles/names)
echo "$W"; echo "$T"
```

- [ ] **8.1 The list.** No automated check has seen this screen. Use Open window… then Copy setup…. From the built-in, to Work. **Expect:** group headings and Add/Change/Same badges.
      `linkedin` and `rea` are unticked with a warning that they may contain a secret. `meme-video` is flagged. **`skills/synced` isn't there.** Checkboxes and text are readable, and the list scrolls.
      The "To" menu never offers the built-in.
- [ ] **8.2 Secrets are masked.** Tick `rea`. **Expect:** `Include env.… = ••••••` rows, each unticked, with no real value shown anywhere.
- [ ] **8.3 Nothing ticked.** Untick everything. **Expect:** the Copy button is off.
- [ ] **8.4 Refused while live.** Start `claude-as work` in Terminal. Switch To away and back. **Expect:** "Work has a live session", and Copy is off. `/exit`, then switch To away and back. **Expect:** the message clears.
- [ ] **8.5 Copy a small set.** Tick only `skills/reference-to-animation`, the `egm` plugin, and `rea` with its secrets unticked. Copy. **Expect:** "Copied 3 item(s)…", and removal commands for the plugin and server. Save those commands.
- [ ] **8.6 It worked.**

  ```sh
  ls "$W/skills"
  claude-as work plugin list
  claude-as work mcp get rea
  shasum ~/.claude/settings.json
  ```

  **Expect:** the skill, the plugin and the server, with no secret values. The `shasum` matches 0.3, unless you changed settings yourself.
- [ ] **8.7 In a real session.** `claude-as work`. **Expect:** the status line still shows `● Work`. Ask `Which skills can you see?` **Expect:** the copied one is listed. `/exit`.
- [ ] **8.8 A ticked secret, using a fake value:**

  ```sh
  claude-as test mcp add sectest --scope user -e TOKEN=fake123 -- /bin/echo hi
  ```

  Copy from Test to Work with `sectest` and its `env.TOKEN` ticked. Then `claude-as work mcp get sectest`. **Expect:** it has TOKEN.
- [ ] **8.9 Changing an MCP server:**

  ```sh
  claude-as work mcp add copytest --scope user -- /bin/echo one
  claude-as test mcp add copytest --scope user -- /bin/echo two
  ```

  Copy from Work to Test. **Expect:** `copytest` shows Change. Tick it and copy. `claude-as test mcp get copytest` shows `one`.
- [ ] **8.10 Skip, Overwrite, Rename:**

  ```sh
  print one > "$W/CLAUDE.md"; print two > "$T/CLAUDE.md"
  ```

  Copy from Work to Test, with `CLAUDE.md` ticked. **Expect:** a Change badge and an "If it exists" choice. Do this three times, checking after each with `ls "$T"; cat "$T/CLAUDE.md"`:
  **Rename** gives `CLAUDE-copy.md` and leaves `two`. **Skip** changes nothing. **Overwrite** gives `one`.
- [ ] **8.11 Changed after the preview.** Open the preview with `CLAUDE.md` ticked as a Change, using the window so that it stays open. In Terminal, `print three > "$T/CLAUDE.md"`. Press Copy.
      **Expect:** "changed since the preview. Nothing was copied", and `three` is still there.
- [ ] **8.12 Restore, and later work.** Run `rm -f "$T/CLAUDE-copy.md"` and `print base > "$T/CLAUDE.md"`, then `print new > "$W/CLAUDE.md"`, and copy from Work to Test with Overwrite. Then `print edited > "$T/CLAUDE.md"`.
      Press Restore… and choose the newest. **Expect:** it lists `CLAUDE.md` as left alone, and `edited` is still there.
- [ ] **8.13 Restore undoes what's unchanged.** Going back to From built-in, To Work, Restore… and pick the backup from 8.5. **Expect:** the skill is deleted from `$W/skills`, and it shows commands for what it can't undo.
- [ ] **8.14 The plugin.** Run the saved plugin removal command, then `claude-as work plugin list`. **Expect:** the plugin has gone. Also run the saved `rea` command and check with `claude-as work mcp list`.

## Part 9: Reboot

- [ ] **9.1 Restart the Mac** and open Terminal. **The app won't start by itself,** because there is no login item. Open it from `~/Applications`.
- [ ] **9.2 All logins survived:**

  ```sh
  claude auth status
  claude-as work auth status
  claude-as test auth status
  cpm doctor
  ```

  **Expect:** Personal, Official, Personal, and no faults. No Keychain or permission prompts.

## Part 10: Desktop app, Cowork and rebuilds

This checks the tool has not disturbed the desktop app. It does not open the desktop app on a second account, which this tool cannot do.

- [ ] **10.1 Claude desktop app.** Open it. **Expect:** Personal, with your earlier sessions, and it still works.
- [ ] **10.2 Cowork.** **Expect:** Personal, and it still works.
- [ ] **10.3 Desktop session stays hidden.** Start a Code tab session. **Expect:** `cpm list --live` doesn't show it. End it.
- [ ] **10.4 Rebuild.** Press Quit in the panel, then run `./make-app.sh --install` and reopen it from `~/Applications`. **Expect:** all profiles are still there. Note any permission prompt. Without a signing identity,
      a prompt here may be expected. If you have one, set `SIGN_IDENTITY` and confirm no prompt returns.

## Part 11: Removal

- [ ] **11.1 Refused while live.** Start `claude-as test` and leave it running. Run `cpm remove test`. **Expect:** "has a live session". In the app: Settings… on Test, then Remove…. **Expect:** the red notice, and no way to go ahead. `/exit`.
- [ ] **11.2 Answer no.** `cpm remove test`, then `n`. **Expect:** "Not removed", and `claude-as test auth status` is still signed in.
- [ ] **11.3 The built-in can't go.** `cpm remove main`. **Expect:** refused.
- [ ] **11.4 Remove the default, from the app.** `cpm default test`. Settings… on Test, Remove…. **Expect:** text saying the built-in becomes the default. Press Remove profile. **No Keychain prompt** (S0 check 21). If one appears, write down its wording.
- [ ] **11.5 Check it:**

  ```sh
  cpm default
  claude auth status
  claude-as work auth status
  claude-as test --version
  zsh scripts/s0-checks.sh names
  ```

  **Expect:** `main`; **Personal still signed in**, although Test shared that account; Official; "no profile named 'test'"; and Test's Keychain item gone, with the plain and Work items still there. The folder is in the Trash.
- [ ] **11.6 Remove from the command line.** Add a profile with `cpm add Temp temp '#888888'`, sign in with `claude-as temp auth login`, then `cpm keep temp`. Run `cpm remove temp` and answer `y`. **Expect:** "Removed Temp", and other logins are unaffected.

## Part 12: Finish

- [ ] **12.1 Clear the test data from Work:**

  ```sh
  claude-as work mcp remove copytest --scope user
  claude-as work mcp remove sectest --scope user
  rm -f "$W/CLAUDE.md"
  rm -r ~/cpm-test
  ```

- [ ] **12.2 Final check.** `cpm doctor` shows no faults, and plain `claude` works.
- [ ] **12.3 Optional, and only if you want everything gone.** `cpm uninstall` removes Work too, so you'd sign in again later. Press Quit in the panel, run it, and answer `n` first. **Expect:** "Nothing changed". If you really want it, run it again and
      answer `y`, then delete the two lines it prints and the app from `~/Applications`. In a new window `claude auth status` shows Personal.

## When something goes wrong

Send me:

1. The step number, what you expected, and what happened.
2. A screenshot, if it's something on screen.
3. The output of `cpm doctor`.
4. The output of `tail -30 ~/.claude-profiles/logs/*`.

Don't paste sign-in codes, tokens or API keys. Your email showing in Doctor output is fine to send, but it stays out of the repo.
