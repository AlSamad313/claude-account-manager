#!/bin/zsh
# Builds cpm and gathers it with the shell files in build/cli/, from which
# `build/cli/cpm install-shell` installs them. Installs nothing itself.
set -eu
cd "${0:A:h}"
swift build -c release --product cpm
out=build/cli
rm -rf "$out"
mkdir -p "$out"
cp "$(swift build -c release --show-bin-path)/cpm" "$out/cpm"
cp shell/bin/* "$out/"
chmod 755 "$out/cpm" "$out/claude" "$out/claude-as" "$out/profile-label"
if [[ -n ${SIGN_IDENTITY-} ]]; then
  codesign --force --sign "$SIGN_IDENTITY" "$out/cpm"
  print -r -- "Signed cpm with $SIGN_IDENTITY."
else
  # Ad-hoc is enough to run. Each rebuild changes the signature, so macOS may ask again.
  codesign --force --sign - "$out/cpm"
  print -r -- "Signed cpm ad-hoc. Set SIGN_IDENTITY to keep macOS permissions across rebuilds."
fi
print -r -- "Built $out. Next: $out/cpm install-shell"
