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

# Run by name from PATH, in another folder, as you would after installing.
output=$(cd /tmp && $e cpm install-shell 2>&1); code=$?
(( code == 0 )) && ok || fail "cpm install-shell works by name, got $code" "$output"

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

# S2: sessions
sid=11111111-2222-3333-4444-555555555555
mkdir -p $dir/projects/p
print -r -- '{"type":"user","cwd":"'$home'","entrypoint":"cli","sessionId":"'$sid'"}' > $dir/projects/p/$sid.jsonl
print -r -- '{"type":"ai-title","aiTitle":"Fix the thing","sessionId":"'$sid'"}' >> $dir/projects/p/$sid.jsonl
output=$($e $cpm list 2>&1)
has "list shows the session" "$output" "Fix the thing"
has "list tags the profile" "$output" "personal"
output=$($e $cpm list --profile main 2>&1)
hasnt "list filters by profile" "$output" "Fix the thing"
output=$($e $cpm list --live 2>&1)
hasnt "list --live hides stopped sessions" "$output" "Fix the thing"
output=$($e $cpm list --profile nosuch 2>&1); code=$?
(( code == 1 )) && ok || fail "list of an unknown profile exits 1, got $code" "$output"

# A real live process, recorded the way Claude Code records it. Its start time comes from ps,
# converted to UTC, as procStart is.
/bin/sleep 60 &
lp=$!
utc=$(/usr/bin/python3 -I -c "
import subprocess, datetime as d
s = subprocess.run(['ps','-o','lstart=','-p','$lp'],capture_output=True,text=True).stdout.strip()
t = d.datetime.strptime(' '.join(s.split()),'%a %b %d %H:%M:%S %Y').astimezone(d.timezone.utc)
print(t.strftime('%a %b %d %H:%M:%S %Y'))")
lid=22222222-2222-3333-4444-555555555555
mkdir -p $dir/sessions
print -r -- '{"pid":'$lp',"sessionId":"'$lid'","procStart":"'$utc'","cwd":"'$home'","status":"busy","entrypoint":"cli","name":"Live one"}' > $dir/sessions/$lp.json
output=$($e $cpm list --live 2>&1)
has "a live process is listed as running" "$output" "running busy"
has "with its title" "$output" "Live one"
output=$($e $cpm stop $lid </dev/null 2>&1); code=$?
(( code == 1 )) && ok || fail "stop with no terminal and no --yes exits 1, got $code" "$output"
kill -0 $lp 2>/dev/null && ok || fail "and leaves the process running"
output=$($e $cpm resume $lid 2>&1); code=$?
(( code == 1 )) && ok || fail "resume of a live session exits 1, got $code" "$output"
has "and says it is running" "$output" "already running"
output=$($e $cpm stop $lid --yes 2>&1); code=$?
has "stop warns about a busy session" "$output" "unfinished"
# A background job started from a script ignores SIGINT, so this also exercises the SIGTERM step.
has "stop falls back to SIGTERM" "$output" "Stopped with SIGTERM"
sleep 0.2; kill -0 $lp 2>/dev/null && { fail "the process ended"; kill $lp } || ok
wait $lp 2>/dev/null
output=$($e $cpm stop $lid --yes 2>&1)
has "a stopped session with no transcript is gone" "$output" "not running"

output=$($e $cpm resume 99999999-2222-3333-4444-555555555555 2>&1); code=$?
(( code == 1 )) && ok || fail "resume of an unknown ID exits 1, got $code" "$output"
has "and says so clearly" "$output" "No profile holds"

# S5: refusals only here, because a real removal uses the real Trash. Swift tests cover the rest.
output=$($e $cpm remove main --yes 2>&1); code=$?
(( code == 1 )) && ok || fail "the built-in profile cannot be removed, got $code" "$output"
has "and says so" "$output" "built-in profile cannot be removed"
output=$($e $cpm remove nosuch --yes 2>&1); code=$?
(( code == 1 )) && ok || fail "an unknown name exits 1, got $code" "$output"
output=$($e $cpm remove personal </dev/null 2>&1); code=$?
(( code == 1 )) && ok || fail "remove with no terminal and no --yes exits 1, got $code" "$output"
has "and changes nothing" "$output" "Nothing changed"
has "the profile is still there" "$(<$r/names)" "personal"
output=$($e $cpm uninstall </dev/null 2>&1); code=$?
(( code == 1 )) && ok || fail "uninstall with no terminal and no --yes exits 1, got $code" "$output"
[[ -d $r ]] && ok || fail "uninstall changed nothing"

# A generated .command file, run for real against the stub, using the installed wrapper.
script=$(mktemp /private/tmp/cpm-cmd-XXXXXX)
cat > $script <<CMD
#!/bin/zsh
dir='$home'
cd -- "\$dir" || { print -ru2 -- "Folder not found: \$dir"; exit 1 }
exec "\$HOME/.claude-profiles/bin/claude-as" 'personal' '--resume' '$sid'
CMD
output=$($e zsh $script 2>&1)
has "a .command file reaches the wrapper on the right profile" "$output" "CLAUDE_CONFIG_DIR=$dir"
has "with the resume arguments" "$output" "ARGS=--resume $sid"
rm -f $script

rm -rf $home
print -r -- "$passes passed, $fails failed"
(( fails == 0 ))
