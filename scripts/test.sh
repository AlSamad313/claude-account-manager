#!/bin/zsh
# Runs swift test with Swift Testing, using Command Line Tools alone (no Xcode).
set -eu
clt="$(xcode-select -p)"
fw="$clt/Library/Developer/Frameworks"
lib="$clt/Library/Developer/usr/lib"
plugins="$clt/usr/lib/swift/host/plugins/testing"
exec swift test \
  -Xswiftc -F -Xswiftc "$fw" -Xswiftc -plugin-path -Xswiftc "$plugins" \
  -Xlinker -F -Xlinker "$fw" -Xlinker -rpath -Xlinker "$fw" -Xlinker -rpath -Xlinker "$lib" "$@"
