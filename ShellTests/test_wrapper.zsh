#!/bin/zsh
# Tests the wrapper against a stub claude, with a temporary HOME. Touches nothing real.
emulate -L zsh
repo=${0:A:h:h}
fails=0 passes=0

ok()   { (( ++passes )); }
fail() { (( fails++ )); print -r -- "FAIL: $1"; [[ -n ${2-} ]] && print -r -- "$2" }
has()  { [[ $2 == *"$3"* ]] && ok || fail "$1: expected '$3'" "$2" }
hasnt() { [[ $2 != *"$3"* ]] && ok || fail "$1: did not expect '$3'" "$2" }

setup() {
  home=$(mktemp -d /private/tmp/cpm-wrapper-XXXXXX)
  home=${home:A}
  r=$home/.claude-profiles
  mkdir -p $r/bin $r/logs $r/profiles/aaaa1111 $home/.claude/projects/p
  cp $repo/shell/bin/* $r/bin/
  chmod +x $r/bin/*
  added=$r/profiles/aaaa1111
  print -r -- $'main\tbuiltin\tCompany\t#2F6FDE\t-' > $r/names
  print -r -- $'personal\taaaa1111\tPersonal\t#E8772E\t'$added >> $r/names
  print -r -- builtin > $r/default
  stubpath="$r/bin:$repo/ShellTests/bin:/usr/bin:/bin"
}
# Runs a wrapper with a clean environment plus any NAME=value arguments before --.
run() {
  local -a extra=()
  while [[ $1 != -- ]]; do extra+=$1; shift; done
  shift
  output=$(env -i HOME=$home PATH=$stubpath PWD=$home $extra "$@" 2>&1)
  code=$?
}
setup

run -- zsh $r/bin/claude --flag
has "default built-in, args kept" "$output" "ARGS=--flag"
has "default built-in, variable unset" "$output" "CLAUDE_CONFIG_DIR=<unset>"
has "profile ID set" "$output" "CLAUDE_PROFILE_ID=builtin"
has "title variable always set" "$output" "TITLE_OFF=1"

run CLAUDE_CONFIG_DIR=$added -- zsh $r/bin/claude
has "parent value inside our folder is ignored" "$output" "CLAUDE_CONFIG_DIR=<unset>"

run CLAUDE_CONFIG_DIR=$r/profiles/removed -- zsh $r/bin/claude
has "a removed profile's old path is ignored" "$output" "CLAUDE_CONFIG_DIR=<unset>"
hasnt "no override warning for it" "$output" "ignores your default"

run -- zsh $r/bin/claude-as personal
has "claude-as uses the exact configDir" "$output" "CLAUDE_CONFIG_DIR=$added"
has "claude-as sets the ID" "$output" "CLAUDE_PROFILE_ID=aaaa1111"

run -- zsh $r/bin/claude-as main
has "claude-as main unsets the variable" "$output" "CLAUDE_CONFIG_DIR=<unset>"

print -r -- aaaa1111 > $r/default
run -- zsh $r/bin/claude
has "default changed: next call uses it" "$output" "CLAUDE_CONFIG_DIR=$added"
print -r -- builtin > $r/default

run CLAUDE_PROFILE_ID=aaaa1111 -- zsh $r/bin/claude
has "nested call stays on its profile" "$output" "CLAUDE_CONFIG_DIR=$added"

run CLAUDE_PROFILE_ID=nosuch -- zsh $r/bin/claude
has "unknown nested ID falls to the default" "$output" "CLAUDE_PROFILE_ID=builtin"

for v in ANTHROPIC_API_KEY ANTHROPIC_AUTH_TOKEN CLAUDE_CODE_OAUTH_TOKEN; do
  run $v=dummy -- zsh $r/bin/claude
  has "$v warns" "$output" "$v is set"
  has "$v stays set" "$output" "=dummy"
  stdout=$(env -i HOME=$home PATH=$stubpath $v=dummy zsh $r/bin/claude 2>/dev/null)
  hasnt "$v warning not on stdout" "$stdout" "is set."
done

stdout=$(env -i HOME=$home PATH=$stubpath zsh $r/bin/claude | cat)
hasnt "piped stdout has no title escape" "$stdout" $'\033]0;'

mv $r/default $r/default.saved
run -- zsh $r/bin/claude
(( code == 1 )) && ok || fail "missing default: exit 1, got $code" "$output"
has "missing default names the fix" "$output" "cpm doctor"
hasnt "missing default: real binary not run" "$output" "ARGS="
mv $r/default.saved $r/default

print -r -- nosuch > $r/default
run -- zsh $r/bin/claude
(( code == 1 )) && ok || fail "unknown default ID: exit 1, got $code" "$output"
hasnt "unknown default ID: real binary not run" "$output" "ARGS="
print -r -- builtin > $r/default

run -- zsh $r/bin/claude-as nosuch
(( code == 1 )) && ok || fail "unknown name: exit 1, got $code" "$output"
has "unknown name lists valid names" "$output" "personal"
hasnt "unknown name: real binary not run" "$output" "ARGS="

mv $r/names $r/names.saved
run -- zsh $r/bin/claude-as personal
(( code == 1 )) && ok || fail "unreadable names: exit 1, got $code" "$output"
hasnt "unreadable names: real binary not run" "$output" "ARGS="
run -- zsh $r/bin/claude
(( code == 1 )) && ok || fail "plain claude, no names: exit 1, got $code" "$output"
mv $r/names.saved $r/names

sid=11111111-2222-3333-4444-555555555555
mkdir -p $added/projects/x
: > $added/projects/x/$sid.jsonl
for form in "--resume $sid" "--resume=$sid" "-r $sid"; do
  run -- zsh $r/bin/claude-as main ${=form}
  (( code == 1 )) && ok || fail "$form of another profile's ID: exit 1, got $code" "$output"
  has "$form names the owner" "$output" "belongs to profile Personal"
  has "$form gives the command" "$output" "claude-as personal --resume $sid"
  hasnt "$form: real binary not run" "$output" "ARGS="
done
run -- zsh $r/bin/claude-as personal --resume $sid
has "own session resumes" "$output" "ARGS=--resume $sid"
run -- zsh $r/bin/claude-as main --resume some-search-term
has "non-UUID value is passed through" "$output" "ARGS=--resume some-search-term"
run -- zsh $r/bin/claude-as main --resume 99999999-2222-3333-4444-555555555555
has "unknown UUID is left to Claude Code" "$output" "ARGS=--resume 99999999"

run CLAUDE_CONFIG_DIR=$home/elsewhere -- zsh $r/bin/claude
has "outside value is kept" "$output" "CLAUDE_CONFIG_DIR=$home/elsewhere"
has "outside value warns" "$output" "ignores your default"
has "outside value gets the title variable" "$output" "TITLE_OFF=1"
hasnt "outside value: no profile ID" "$output" "CLAUDE_PROFILE_ID=builtin"

: > $r/logs/launch.log
run -- zsh $r/bin/claude
lines=$(wc -l < $r/logs/launch.log)
(( lines == 1 )) && ok || fail "one line added to launch.log, got $lines"
rm -f $r/logs/launch.log; chmod 500 $r/logs
run -- zsh $r/bin/claude
has "unwritable log does not block launch" "$output" "ARGS="
chmod 700 $r/logs

output=$(env -i HOME=$home PATH=$stubpath /bin/bash -c "claude-as personal" 2>&1)
has "reached from bash by name" "$output" "CLAUDE_CONFIG_DIR=$added"
cat > $home/s.sh <<'E'
#!/bin/sh
claude
E
output=$(env -i HOME=$home PATH=$stubpath /bin/sh $home/s.sh 2>&1)
has "reached from a script by name" "$output" "CLAUDE_PROFILE_ID=builtin"

output=$(env -i HOME=$home PATH=$r/bin:/usr/bin:/bin zsh $r/bin/claude 2>&1); code=$?
(( code == 1 )) && ok || fail "no real binary: exit 1, got $code" "$output"
has "no real binary says so" "$output" "cannot find the real claude"

output=$(env -i HOME=$home PATH=$r/bin/../bin:$repo/ShellTests/bin:/usr/bin:/bin zsh $r/bin/claude 2>&1)
has "bin folder spelled differently is still skipped" "$output" "ARGS="

output=$(env -i HOME=$home PATH=$stubpath zsh $r/bin/profile-label aaaa1111)
has "profile-label prints the name" "$output" "Personal"
output=$(env -i HOME=$home PATH=$stubpath zsh $r/bin/profile-label nosuch)
[[ -z $output ]] && ok || fail "profile-label prints nothing for an unknown ID" "$output"

rm -rf $home
print -r -- "$passes passed, $fails failed"
(( fails == 0 ))
