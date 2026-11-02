# Sourced from the end of ~/.zshrc. macOS path_helper and Homebrew can move the bin
# folder after ~/.zshenv runs, so this puts it first again.
path=("$HOME/.claude-profiles/bin" ${path:#$HOME/.claude-profiles/bin})

_claude_as() {
  local names="$HOME/.claude-profiles/names"
  [[ -r $names ]] || return 1
  (( CURRENT == 2 )) && compadd -- ${${(f)"$(<$names)"}%%$'\t'*}
}
(( $+functions[compdef] )) && compdef _claude_as claude-as
