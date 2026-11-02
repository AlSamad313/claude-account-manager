# Shared launch rules for claude and claude-as (design flow 1). Sourced, not run.
# Reads two plain-text files and never parses JSON.

root="$HOME/.claude-profiles"
bin_dir="$root/bin"
names_file="$root/names"
default_file="$root/default"

# Fields of the chosen profile, filled in by find_by_*.
p_cmd="" p_id="" p_disp="" p_dir=""

find_by() {  # $1 = column name (cmd or id), $2 = value
  local cmd id disp color dir
  [[ -r "$names_file" ]] || return 1
  while IFS=$'\t' read -r cmd id disp color dir; do
    if { [[ $1 == cmd && $cmd == "$2" ]] || [[ $1 == id && $id == "$2" ]] }; then
      p_cmd=$cmd p_id=$id p_disp=$disp p_dir=$dir
      return 0
    fi
  done < "$names_file"
  return 1
}

list_names() {
  local cmd rest
  [[ -r "$names_file" ]] || return 0
  while IFS=$'\t' read -r cmd rest; do print -ru2 -- "  $cmd"; done < "$names_file"
}

real_binary() {
  local d want=${bin_dir:A}
  for d in $path; do
    [[ -n $d && ${d:A} != $want && -x $d/claude && ! -d $d/claude ]] || continue
    print -r -- "$d/claude"
    return 0
  done
  return 1
}

resume_id() {  # prints the session ID in the arguments, if there is one
  local i=1 n=$# a
  while (( i <= n )); do
    a=${@[i]}
    case $a in
      --resume=*) print -r -- "${a#--resume=}"; return ;;
      --resume|-r) (( i < n )) && print -r -- "${@[i+1]}"; return ;;
    esac
    (( i++ ))
  done
}

folder_of() {  # $1 = configDir field
  [[ $1 == - ]] && print -r -- "$HOME/.claude" || print -r -- "$1"
}

check_owner() {  # refuses a session that belongs to another profile
  local sid=$1 own hits cmd id disp color dir
  [[ $sid =~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$' ]] || return 0
  own=$(folder_of "$p_dir")
  hits=("$own"/projects/*/"$sid".jsonl(N))
  (( $#hits )) && return 0
  while IFS=$'\t' read -r cmd id disp color dir; do
    [[ $id == "$p_id" ]] && continue
    hits=("$(folder_of "$dir")"/projects/*/"$sid".jsonl(N))
    if (( $#hits )); then
      print -ru2 -- "Session $sid belongs to profile $disp."
      print -ru2 -- "Use: claude-as $cmd --resume $sid"
      exit 1
    fi
  done < "$names_file"
}

warn_login_vars() {
  local v
  for v in ANTHROPIC_API_KEY ANTHROPIC_AUTH_TOKEN CLAUDE_CODE_OAUTH_TOKEN; do
    [[ -n ${(P)v-} ]] && print -ru2 -- "$v is set. Claude Code will report it instead of this profile's login."
  done
  return 0
}

log_launch() {  # $1 = label, $2 = folder. A logging failure never blocks a launch.
  local line
  line="$(date -u +%Y-%m-%dT%H:%M:%SZ)	$1	${PWD//$'\t'/ }	$2"
  { print -r -- "${line//$'\n'/ }" >> "$root/logs/launch.log"; } 2>/dev/null || true
}

run_real() {
  local real
  real=$(real_binary) || { print -ru2 -- "claude-profiles: cannot find the real claude binary on PATH."; exit 1 }
  exec "$real" "$@"
}

launch() {
  local sid
  sid=$(resume_id "$@")
  [[ -n $sid ]] && check_owner "$sid"
  warn_login_vars
  if [[ $p_dir == - ]]; then
    unset CLAUDE_CONFIG_DIR
  else
    export CLAUDE_CONFIG_DIR=$p_dir
  fi
  export CLAUDE_PROFILE_ID=$p_id
  export CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1
  if [[ -t 1 ]]; then
    printf '\033]0;%s\007' "$p_disp" > /dev/tty 2>/dev/null
  fi
  log_launch "$p_id" "$(folder_of "$p_dir")"
  run_real "$@"
}

main_claude() {
  # A value outside our folder was set on purpose. A value inside may be stale.
  if [[ -n ${CLAUDE_CONFIG_DIR-} && ${CLAUDE_CONFIG_DIR:A} != ${root:A}/* ]]; then
    print -ru2 -- "CLAUDE_CONFIG_DIR is set to $CLAUDE_CONFIG_DIR, so this session ignores your default profile."
    export CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1
    log_launch override "$CLAUDE_CONFIG_DIR"
    run_real "$@"
  fi
  if [[ -n ${CLAUDE_PROFILE_ID-} ]] && find_by id "$CLAUDE_PROFILE_ID"; then
    launch "$@"
  fi
  local id=""
  [[ -r $default_file ]] && IFS= read -r id < "$default_file"
  if [[ -z $id ]] || ! find_by id "$id"; then
    print -ru2 -- "claude-profiles: cannot read your default profile from $default_file."
    print -ru2 -- "Claude Code was not started, to avoid using the wrong account. Run: cpm doctor --fix"
    exit 1
  fi
  launch "$@"
}

main_claude_as() {
  if (( $# == 0 )); then
    print -ru2 -- "Usage: claude-as <name> [claude arguments]"
    list_names
    exit 1
  fi
  local name=$1
  shift
  if ! find_by cmd "$name"; then
    print -ru2 -- "claude-profiles: no profile named '$name'. Valid names:"
    list_names
    exit 1
  fi
  launch "$@"
}
