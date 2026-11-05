#!/bin/zsh
# Runs swift test with Swift Testing, with Xcode or with the Command Line Tools alone.
set -eu
clt="$(xcode-select -p)"
# Xcode finds Swift Testing by itself.
[[ $clt == *CommandLineTools* ]] || exec swift test "$@"
fw="$clt/Library/Developer/Frameworks"
lib="$clt/Library/Developer/usr/lib"
plugins="$clt/usr/lib/swift/host/plugins/testing"
exec swift test \
  -Xswiftc -F -Xswiftc "$fw" -Xswiftc -plugin-path -Xswiftc "$plugins" \
  -Xlinker -F -Xlinker "$fw" -Xlinker -rpath -Xlinker "$fw" -Xlinker -rpath -Xlinker "$lib" "$@"
