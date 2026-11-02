# S0 checklist

The table in `docs/designs/claude-profile-manager.md` defines each check and its pass
condition. This file gives the steps for the ones that need you. Record results in
`docs/S0-results.md`. Test folders are under `~/.cpm-s0/`.

Before starting: `zsh scripts/s0-checks.sh prep`

## Part A: logins (checks 1, 2, 3, 4, 6, 7, 20)

Each login opens the browser. Check the browser is signed into the account you want, or use
a private window. The built-in profile plus these two folders makes three profiles. These checks are
about folders, so reusing an account is allowed, but it will not show two accounts side by side.

1. `zsh scripts/s0-checks.sh names`, and note the list.
2. For each of 1 and 2, using a different account for 2 if you have one, run in a terminal:
   `CLAUDE_CONFIG_DIR="$(realpath ~/.cpm-s0/1)" claude auth login`
   After each one, run `zsh scripts/s0-checks.sh names`. One new name should appear each time.
3. `zsh scripts/s0-checks.sh status`. The built-in profile must still show today's account.
4. Reboot. Run `status` again. All three must still be logged in. **Checks 1, 2 and 3 can stop
   the project.**

## Part B: sessions (checks 5, 18, 19, 22, 13)

Use folder 1 and a throwaway working folder. Each uses a little of that account's usage.

- **Check 5:** `cd ~/.cpm-s0/5`, start `CLAUDE_CONFIG_DIR="$(realpath ~/.cpm-s0/1)" claude`, say
  "hello", exit, note the session ID it prints. Resume with `--resume <id>` from the same folder
  and from `~`. Does the conversation continue, and with the same ID?
- **Check 19:** in Terminal.app, run `print -n "\e]0;TEST TITLE\a"`, then
  `CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1 claude`. Ask it to run a command. Is the title still
  TEST TITLE at the end?
- **Check 18:** start a session, and from another window run `kill -INT <pid>`, then `kill -TERM
  <pid>` if needed. Does it end within 10 seconds, and does the Terminal window stay open?
- **Check 22:** resume one session in two windows at once. What happens?
- **Check 13:** set each of `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN` and
  `CLAUDE_CODE_OAUTH_TOKEN` to a dummy value in turn and run `claude auth status`. Which login
  does it report? Do not use a real key.

## Part C: login-wide setting (check 12)

Check 17 already passed, so Terminal.app does not need restarting, and a session running in Terminal is not
affected. Folder 2 is used because it is a different account from the built-in profile, so the result shows.

1. Close VS Code and the Claude desktop app.
2. `launchctl setenv CLAUDE_CONFIG_DIR "$(realpath ~/.cpm-s0/2)"`
3. Open VS Code's Claude Code. Which account does it use? (benefit)
4. Open the Claude desktop app and Cowork. Which account? Still logged in? Is your history all there? (safety)
5. **Always afterwards:** `launchctl unsetenv CLAUDE_CONFIG_DIR`, then restart those apps.

## Teardown, when all parts are done

`zsh scripts/s0-checks.sh teardown`. Check the Keychain names it prints, then remove `~/.cpm-s0`.
The plain `Claude Code-credentials` item must still be there.

Keep `~/claude-profile-backup-2026-11-02` until S1 works, then delete it, because it can hold
MCP secrets.

## Later checks

Check 10 and 14 run in S1. Check 9 and 21 run at the start of S3. After a Claude Code update, rerun
only the checks whose subject changed.
