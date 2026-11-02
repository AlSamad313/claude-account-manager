# End-to-end checklist

Step by step, on the real machine, after `cpm install-shell` and the two added lines. Entries are added as
each stage delivers them. Tick each one against the build under test.

## S1: profiles

- [ ] **CUJ 1, add a second account.** `cpm add Work work '#2F6FDE'`, `claude-as work auth login` in a private
      browser window, `cpm keep work`. It names the account. `claude auth status` in another shell still shows
      the first account.
- [ ] **CUJ 4, change the default.** With a session running, `cpm default work`. The running session is unaffected,
      and the next plain `claude` is on Work, with no shell reload.
- [ ] **Three profiles across a reboot.** All still logged in afterwards.
- [ ] **S0 check 10.** The status line in a Work session shows the profile.
- [ ] **S0 check 14 in a real install.** `cpm doctor` reports that plain `claude` runs the wrapper.
- [ ] **CUJ 6, diagnose.** `cpm doctor` names each profile's folder and account. With `ANTHROPIC_API_KEY=x` set in
      your shell, it adds a note. Removing `~/.claude-profiles/default` gives a fault, and `cpm doctor --fix` repairs it.

## S2: sessions from the command line

- [ ] **CUJ 2, start a session.** `cpm new work`. A Terminal window opens, its title shows the profile, and `cpm list
      --live` shows it within 5 seconds.
- [ ] **CUJ 3, find and resume.** `cpm list --profile work`, then `cpm resume <id>`. The conversation continues in
      its original folder on Work.
- [ ] **Wrong profile refused.** `claude-as main --resume <a Work session ID>` names Work and does not start.
- [ ] **CUJ 7, stop a busy session.** `cpm stop <id>` shows the warning. The session ends, the window stays open,
      and `cpm list` shows it stopped within 10 seconds.
- [ ] **VS Code.** With VS Code closed, `cpm code work`, and its Claude Code shows Work. With it open, `cpm code main`
      says to quit it first.
- [ ] **S0 check 19 in a real window.** The window title still shows the profile after Claude Code has done some work.

## S3: the app

- [ ] **Launch.** `./make-app.sh --install`, open it. A coloured dot appears in the menu bar, with no Dock icon. Clicking it
      opens the panel.
- [ ] **Controls look right.** The filter field, "⋯" menu and "Make default" link draw and respond. (The automated image
      check cannot show system controls.)
- [ ] **Set up shell.** With the shell not installed, the panel says so and New Terminal is off. "Set up shell…" installs and
      shows the two commands, and "Copy commands" copies them.
- [ ] **CUJ 1 from the app.** "Add profile…", use a private browser window, and the panel moves on by itself once signed in. It
      shows the account, with Keep. A duplicate account warns.
- [ ] **CUJ 2.** "New Terminal" opens a window with the profile in its title, and the row shows running within 5 seconds.
      **S0 check 9:** no permission prompt appears.
- [ ] **CUJ 3.** Filter, then Resume. It continues on the right profile, in the original folder. With the folder deleted, it offers
      "Choose folder…".
- [ ] **CUJ 4.** "Make default" changes the dot's colour, and the next `claude` follows. `cpm default main` in Terminal
      changes the dot too, with the panel closed.
- [ ] **CUJ 7.** Stop on a busy row shows the warning. The window stays open, and the row shows stopped within 10 seconds.
- [ ] **Window.** "Open window…" shows the sidebar and table. Selecting a profile filters. Sheets for Add, Doctor and Stop work.
- [ ] **VS Code.** The "⋯" menu opens VS Code on that profile. With it running, the app says to quit it first.
- [ ] **Doctor.** Lists each profile and any fault. "Repair files" fixes a deleted `default`.
- [ ] **Rebuild.** Rebuild with `SIGN_IDENTITY` set: no permission prompt returns.
- [ ] **Panel speed.** It opens without a visible wait on your real sessions.

## S4: copy setup

- [ ] **CUJ 5.** Copy from the built-in profile into Work. The preview lists each item as Add, Change or Same. A server with a
      secret is unticked. Copy some items, and they work in a Work session.
- [ ] **Refusal.** With a session running on Work, the screen says so and Copy is off.
- [ ] **Status line.** After copying, a Work session still shows Work.
- [ ] **Restore.** Restore deletes what the copy created. A file you edited after the copy is listed and left alone.
- [ ] **Real plugin install.** A plugin copies across and works. Its removal command works.

## Later stages

- **S5:** CUJ 8, remove the default profile.
