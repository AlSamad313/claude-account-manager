import AppKit
import ProfileCore
import ProfileUI
import SwiftUI

@main
struct ClaudeProfilesApp: App {
    @State private var model: AppModel

    init() {
        let shell = Bundle.main.resourceURL?.appendingPathComponent("shell")
        let m = AppModel(env: .live(shellSource: shell))
        m.startWatching()
        Task { await m.prepareAtLaunch() }
        _model = State(initialValue: m)
    }

    var body: some Scene {
        MenuBarExtra {
            PanelHost(model: model)
        } label: {
            Image(nsImage: MenuBarIcon.image(hex: model.defaultProfile?.color ?? "#8A8F98"))
                .accessibilityLabel("Claude Profiles, default \(model.defaultProfile?.displayName ?? "unknown")")
        }
        .menuBarExtraStyle(.window)

        Window("Claude Profiles", id: "main") {
            MainWindowView(model: model)
        }
        .defaultSize(width: 1100, height: 640)
    }
}

/// Lives inside the scene, so it can read the action that opens the window.
private struct PanelHost: View {
    @Bindable var model: AppModel
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        MenuPanelView(model: model) {
            openWindow(id: "main")
            NSApp.activate()
        }
    }
}

/// Not a template image, so macOS does not recolour it.
private enum MenuBarIcon {
    static func image(hex: String) -> NSImage {
        let size = NSSize(width: 18, height: 18)
        let color = NSColor(Color(hex: hex))
        let image = NSImage(size: size, flipped: false) { rect in
            color.setFill()
            NSBezierPath(ovalIn: rect.insetBy(dx: 3, dy: 3)).fill()
            NSColor.white.withAlphaComponent(0.85).setStroke()
            let ring = NSBezierPath(ovalIn: rect.insetBy(dx: 3, dy: 3))
            ring.lineWidth = 1
            ring.stroke()
            return true
        }
        image.isTemplate = false
        return image
    }
}
