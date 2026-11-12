#!/bin/zsh
# Builds a universal app (Apple Silicon and Intel) and packs it as a zip, with a checksum,
# in build/release. Publishes nothing. Without SIGN_IDENTITY the app is signed ad hoc, so
# macOS warns on first open; the README explains what to do.
set -eu
cd "${0:A:h:h}"
version=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' Resources/Info.plist)
out=build/release
rm -rf "$out"
mkdir -p "$out/bin"
for product in ClaudeProfiles cpm; do
  inputs=()
  for arch in arm64 x86_64; do
    # Keep the builder's home folder, and so their username, out of the binaries: the prefix map
    # makes source paths relative, and the linker's -S drops debug entries naming the object files.
    swift build -c release --product "$product" --triple "$arch-apple-macosx14.0" --scratch-path ".build/release-$arch" \
      -Xswiftc -file-prefix-map -Xswiftc "$PWD=." -Xlinker -S
    inputs+=("$(swift build -c release --triple "$arch-apple-macosx14.0" --scratch-path ".build/release-$arch" --show-bin-path)/$product")
  done
  lipo -create "${inputs[@]}" -output "$out/bin/$product"
done
PREBUILT_BIN=$PWD/$out/bin ./make-app.sh
zip_name="ClaudeProfiles-$version-macos-universal.zip"
# ditto keeps the code signature intact, which plain zip may not.
ditto -c -k --keepParent build/ClaudeProfiles.app "$out/$zip_name"
(cd "$out" && shasum -a 256 "$zip_name" > "$zip_name.sha256")
rm -rf "$out/bin"
print -r -- "Packed $out/$zip_name"
