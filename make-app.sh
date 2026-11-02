#!/bin/zsh
# Builds build/ClaudeProfiles.app. `--install` copies it to ~/Applications.
set -eu
cd "${0:A:h}"
# scripts/make-release.sh passes binaries it has already built.
if [[ -n ${PREBUILT_BIN-} ]]; then
  bin=$PREBUILT_BIN
else
  swift build -c release --product ClaudeProfiles
  swift build -c release --product cpm
  bin=$(swift build -c release --show-bin-path)
fi
app=build/ClaudeProfiles.app
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources/shell"
cp "$bin/ClaudeProfiles" "$app/Contents/MacOS/ClaudeProfiles"
cp Resources/Info.plist "$app/Contents/Info.plist"
cp Resources/AppIcon.icns "$app/Contents/Resources/AppIcon.icns"
cp Resources/BurstMask.png "$app/Contents/Resources/BurstMask.png"
cp "$bin/cpm" shell/bin/* "$app/Contents/Resources/shell/"
chmod 755 "$app/Contents/Resources/shell/"{cpm,claude,claude-as,profile-label}
identity=${SIGN_IDENTITY:--}
codesign --force --sign "$identity" "$app/Contents/Resources/shell/cpm"
codesign --force --sign "$identity" "$app"
if [[ $identity == - ]]; then
  print -r -- "Signed ad-hoc. Set SIGN_IDENTITY to keep macOS permissions across rebuilds."
else
  print -r -- "Signed with $identity."
fi
if [[ ${1-} == --install ]]; then
  mkdir -p "$HOME/Applications"
  rm -rf "$HOME/Applications/ClaudeProfiles.app"
  cp -R "$app" "$HOME/Applications/"
  print -r -- "Installed to ~/Applications/ClaudeProfiles.app"
else
  print -r -- "Built $app."
fi
