#!/bin/zsh
# Generates Resources/AppIcon.icns: the burst on a rounded square, from Resources/BurstMask.png.
# The mask was drawn with Codex image generation, then cut out. The app icon cannot follow the
# default profile, so it uses one fixed colour; the menu-bar icon (MenuBarIcon.swift) does follow it.
set -eu
repo=${0:A:h:h}
work=$(mktemp -d /private/tmp/cpm-icon-XXXXXX)
trap 'rm -rf "$work"' EXIT
cat > "$work/draw.swift" <<'SWIFT'
import AppKit
let out = CommandLine.arguments[1]
let mask = NSImage(contentsOfFile: CommandLine.arguments[2])!
for (name, px) in [("16x16", 16), ("16x16@2x", 32), ("32x32", 32), ("32x32@2x", 64), ("128x128", 128),
                   ("128x128@2x", 256), ("256x256", 256), ("256x256@2x", 512), ("512x512", 512), ("512x512@2x", 1024)] {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8, samplesPerPixel: 4,
                               hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let s = CGFloat(px)
    let inset = s * 0.1
    NSColor(red: 0.85, green: 0.47, blue: 0.34, alpha: 1).setFill()
    NSBezierPath(roundedRect: NSRect(x: inset, y: inset, width: s - 2 * inset, height: s - 2 * inset), xRadius: s * 0.18, yRadius: s * 0.18).fill()
    let side = s * 0.58
    mask.draw(in: NSRect(x: (s - side) / 2, y: (s - side) / 2, width: side, height: side), from: .zero, operation: .sourceOver, fraction: 1)
    NSGraphicsContext.restoreGraphicsState()
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(out)/icon_\(name).png"))
}
SWIFT
mkdir "$work/AppIcon.iconset"
swift "$work/draw.swift" "$work/AppIcon.iconset" "$repo/Resources/BurstMask.png"
iconutil -c icns "$work/AppIcon.iconset" -o "$repo/Resources/AppIcon.icns"
print -r -- "Wrote Resources/AppIcon.icns"
