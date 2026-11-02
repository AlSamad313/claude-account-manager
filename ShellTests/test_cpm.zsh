#!/bin/zsh
# End to end: the built cpm plus the wrapper, with a temporary HOME and a stub claude.
# A fake login writes the config file. Run ./make-cli.sh first.
emulate -L zsh
repo=${0:A:h:h}
cli=$repo/build/cli
[[ -x $cli/cpm ]] || { print -r -- "Run ./make-cli.sh first."; exit 1 }
fails=0 passes=0
ok()   { (( ++passes )); }
fail() { (( fails++ )); print -r -- "FAIL: $1"; [[ -n ${2-} ]] && print -r -- "$2" }
has()  { [[ $2 == *"$3"* ]] && ok || fail "$1: expected '$3'" "$2" }
hasnt() { [[ $2 != *"$3"* ]] && ok || fail "$1: did not expect '$3'" "$2" }

home=$(mktemp -d /private/tmp/cpm-e2e-XXXXXX); home=${home:A}
r=$home/.claude-profiles
print -r -- '{"oauthAccount":{"accountUuid":"u-builtin","emailAddress":"me@example.com","organizationName":"Acme"}}' > $home/.claude.json
stub=$repo/ShellTests/bin
e=(env -i HOME=$home PATH=$r/bin:$stub:/usr/bin:/bin)

output=$($e $cli/cpm install-shell 2>&1); has "install prints the zshrc line" "$output" "claude-profiles.zsh"
[[ -x $r/bin/cpm && -x $r/bin/claude && -r $r/bin/VERSION ]] && ok || fail "install copies the files"
[[ ! -e $home/.zshrc && ! -e $home/.zshenv ]] && ok || fail "install leaves shell files alone"
cpm=$r/bin/cpm

output=$($e claude --x 2>&1)
has "built-in works straight after install" "$output" "CLAUDE_CONFIG_DIR=<unset>"

output=$($e $cpm default 2>&1); has "default starts as main" "$output" "main"

output=$($e $cpm add Personal personal '#E8772E' 2>&1); code=$?
(( code == 0 )) && ok || fail "add exits 0" "$output"
has "add prints the sign-in command" "$output" "claude-as personal auth login"
dir=$(print -r -- $r/profiles/*(/)); [[ -d $dir ]] && ok || fail "add makes one folder"
[[ $dir == $home/.claude-profiles/profiles/* ]] && ok || fail "folder under profiles/" "$dir"
has "status line written" "$(<$dir/settings.json)" "profile-label"

output=$($e claude-as personal auth login 2>&1)
has "sign-in runs in the new folder" "$output" "CLAUDE_CONFIG_DIR=$dir"
has "sign-in arguments are passed" "$output" "ARGS=auth login"

output=$($e $cpm keep personal 2>&1); code=$?
(( code == 1 )) && ok || fail "keep before sign-in exits 1, got $code" "$output"

print -r -- '{"oauthAccount":{"accountUuid":"u-2","emailAddress":"two@example.com","organizationName":"Two"}}' > $dir/.claude.json
output=$($e $cpm default 2>&1)
has "a detected login is reported" "$output" "signed in as two@example.com"
has "and asks for keep" "$output" "cpm keep personal"
has "not completed by itself" "$(<$r/profiles.json)" '"pendingLogin"'

output=$($e $cpm keep personal 2>&1); code=$?
(( code == 0 )) && ok || fail "keep exits 0" "$output"
has "keep names the account" "$output" "two@example.com"
has "state is ready" "$(<$r/profiles.json)" '"ready"'

output=$($e $cpm default personal 2>&1); has "default set" "$output" "Personal"
output=$($e claude 2>&1); has "plain claude follows the default" "$output" "CLAUDE_CONFIG_DIR=$dir"
output=$($e claude-as main 2>&1); has "claude-as main is built-in" "$output" "CLAUDE_CONFIG_DIR=<unset>"

rm $r/default
output=$($e claude 2>&1); code=$?
(( code == 1 )) && ok || fail "plain claude stops on a missing default, got $code" "$output"
hasnt "and does not run" "$output" "ARGS="
output=$($e $cpm doctor 2>&1); code=$?
(( code == 2 )) && ok || fail "doctor reports a fault with exit 2, got $code" "$output"
[[ ! -e $r/default ]] && ok || fail "doctor alone repairs nothing"
output=$($e $cpm doctor --fix 2>&1)
[[ -r $r/default ]] && ok || fail "doctor --fix rewrites default" "$output"
output=$($e claude 2>&1); has "claude works again, on the same default" "$output" "CLAUDE_CONFIG_DIR=$dir"

output=$($e $cpm add Dup dup '#000000' 2>&1)
dup=$(print -r -- ${${(f)"$(print -rl -- $r/profiles/*(/))"}:#$dir})
print -r -- '{"oauthAccount":{"accountUuid":"u-2","emailAddress":"two@example.com"}}' > $dup/.claude.json
output=$($e $cpm keep dup 2>&1); has "duplicate account warns" "$output" "already uses this account"

output=$($e $cpm add 'Bad' 'bad name' '#000000' 2>&1); code=$?
(( code == 1 )) && ok || fail "bad command name exits 1, got $code" "$output"

output=$($e $cpm nonsense 2>&1); code=$?
(( code == 1 )) && ok || fail "unknown subcommand exits 1, got $code" "$output"

rm -rf $home
print -r -- "$passes passed, $fails failed"
(( fails == 0 ))
