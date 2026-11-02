import AppKit
import SwiftUI

/// The menu-bar icon: a burst on a rounded square in the default profile's colour, so the colour
/// shows which account plain `claude` uses. Not a template image, so macOS does not recolour it.
public enum MenuBarIcon {
    public static var bundledMask: NSImage? {
        Bundle.main.url(forResource: "BurstMask", withExtension: "png").flatMap(NSImage.init(contentsOf:))
    }

    public static func image(hex: String, mask: NSImage? = bundledMask) -> NSImage {
        let color = NSColor(Color(hex: hex))
        let burst = burstColor(on: color)
        let image = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { rect in
            let tile = rect.insetBy(dx: 1, dy: 1)
            color.setFill()
            NSBezierPath(roundedRect: tile, xRadius: 4, yRadius: 4).fill()
            if let mask {
                tinted(mask, burst).draw(in: tile.insetBy(dx: 1, dy: 1), from: .zero, operation: .sourceOver, fraction: 1)
            } else {
                // Without the mask, the colour still shows the default.
                burst.setFill()
                NSBezierPath(ovalIn: tile.insetBy(dx: 5, dy: 5)).fill()
            }
            return true
        }
        image.isTemplate = false
        return image
    }

    /// White, unless the profile's colour is too light for white to show.
    static func burstColor(on color: NSColor) -> NSColor {
        let c = color.usingColorSpace(.sRGB) ?? color
        let luminance = 0.299 * c.redComponent + 0.587 * c.greenComponent + 0.114 * c.blueComponent
        return luminance > 0.72 ? NSColor(white: 0.1, alpha: 1) : .white
    }

    private static func tinted(_ mask: NSImage, _ color: NSColor) -> NSImage {
        NSImage(size: mask.size, flipped: false) { rect in
            mask.draw(in: rect)
            color.setFill()
            rect.fill(using: .sourceIn)
            return true
        }
    }
}
