#!/bin/zsh
# S0 backup of the built-in profile. Reads Keychain item names only, never secret data.
set -eu
dest="$HOME/claude-profile-backup-$(date +%Y-%m-%d)"
[[ -e "$dest" ]] && { print -u2 "Refusing: $dest already exists."; exit 1 }
live=0
for f in "$HOME"/.claude/sessions/*.json(N); do
  pid=$(/usr/bin/python3 -I -c 'import json,sys;print(json.load(open(sys.argv[1])).get("pid",""))' "$f" 2>/dev/null || true)
  [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null && live=$((live+1))
done
if (( live > 0 )); then
  print -u2 "Note: $live Claude Code session(s) are live. Claude Code replaces its files by rename,"
  print -u2 "so each copied file is whole, but the set may be from slightly different moments."
fi
umask 077
mkdir -p "$dest"
chmod 700 "$dest"
# sessions/ holds live records and .key files, and is excluded on purpose.
rsync -a --exclude '/sessions/' "$HOME/.claude/" "$dest/dot-claude/"
cp -p "$HOME/.claude.json" "$dest/dot-claude.json"
security dump-keychain 2>/dev/null | grep -E '"svce"<blob>="Claude Code' | sed -E 's/.*="(.*)"/\1/' | sort -u > "$dest/keychain-names.txt"
cat > "$dest/RESTORE.txt" <<R
To restore, with every Claude Code session and the Claude desktop app closed:
  rsync -a --delete --exclude '/sessions/' "$dest/dot-claude/" "\$HOME/.claude/"
  cp -p "$dest/dot-claude.json" "\$HOME/.claude.json"
This backup does not hold the Keychain login, the desktop app's data, or sessions/.
The Keychain item names at backup time are in keychain-names.txt.
This folder can contain MCP secrets. Keep it private and delete it when no longer needed.
R
print "Backup: $dest"
du -sh "$dest" | cut -f1
cat "$dest/keychain-names.txt"
