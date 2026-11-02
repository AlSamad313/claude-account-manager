#!/bin/zsh
# Helpers for the S0 checks. Reads names and account labels only, never secret data.
# Usage: s0-checks.sh names | status | prep | teardown
set -u
root="$HOME/.cpm-s0"

names() {
  security dump-keychain 2>/dev/null | grep -E '"svce"<blob>="Claude Code' \
    | sed -E 's/.*="(.*)"/\1/' | sort -u
}

one_status() {  # $1 = label, $2 = config folder, or "-" for the built-in profile
  local out
  if [[ "$2" == "-" ]]; then
    out=$(env -u CLAUDE_CONFIG_DIR claude auth status --json 2>/dev/null)
  else
    out=$(CLAUDE_CONFIG_DIR="$2" claude auth status --json 2>/dev/null)
  fi
  print -r -- "$out" | /usr/bin/python3 -I -c '
import json,sys
label=sys.argv[1]
try: d=json.load(sys.stdin)
except Exception: print(label,"status unreadable"); raise SystemExit
print(label,"logged in:",d.get("loggedIn"),"|",d.get("email","-"),"|",d.get("subscriptionType","-"),"|",d.get("configDirectory","-"))
' "$1"
}

case "${1:-}" in
  names) names ;;
  prep)
    mkdir -p "$root"/{1,2,3,5,18,22}
    chmod 700 "$root"
    print "Test folders ready under $(realpath "$root")"
    ;;
  status)
    one_status built-in -
    for n in 1 2 3; do
      [[ -d "$root/$n" ]] && one_status "folder-$n" "$(realpath "$root/$n")"
    done
    print -- "--- Keychain item names"
    names
    ;;
  teardown)
    launchctl unsetenv CLAUDE_CONFIG_DIR
    launchctl unsetenv CPM_S0_TEST
    for d in "$root"/*(/N); do
      CLAUDE_CONFIG_DIR="$(realpath "$d")" claude auth logout >/dev/null 2>&1
    done
    print "Logged out of test folders. Keychain names now:"
    names
    print "Check the names above, then remove $root yourself."
    ;;
  *) print -u2 "Usage: $0 names|status|prep|teardown"; exit 1 ;;
esac
