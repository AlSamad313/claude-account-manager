import SwiftUI

/// One task in the manual. Button names here must match the ones on screen.
struct GuideTopic: Identifiable {
    let icon: String
    let title: String
    let summary: String
    var steps: [String] = []
    var tip: String?
    var commands: [(command: String, meaning: String)] = []
    var id: String { title }

    static let all: [GuideTopic] = [
        GuideTopic(icon: "plus.circle", title: "Add an account", summary: "Sign in once, and it stays signed in.",
                   steps: ["Press Add profile…", "Choose a name, command and colour.", "Sign in from the Terminal window that opens.", "Press Keep."],
                   tip: "Use a private browser window to pick the right account."),
        GuideTopic(icon: "star", title: "Change the default", summary: "Choose the account plain claude uses.",
                   steps: ["Press Make default on a profile."],
                   tip: "The menu bar icon shows the default's colour. Running sessions don't change."),
        GuideTopic(icon: "terminal", title: "Start a session", summary: "Open Terminal on any account.",
                   steps: ["Press New Terminal on a profile.", "Choose a folder, then press Open Terminal."]),
        GuideTopic(icon: "arrow.clockwise", title: "Resume a session", summary: "Continue on the right account, in the same folder.",
                   steps: ["Find it under Recent Sessions, or use the filter.", "Press Resume."]),
        GuideTopic(icon: "stop.circle", title: "Stop a session", summary: "End a running session.",
                   steps: ["Press Stop.", "Press Stop session to confirm."],
                   tip: "The Terminal window stays open."),
        GuideTopic(icon: "chevron.left.forwardslash.chevron.right", title: "Open VS Code", summary: "Use an account in VS Code.",
                   steps: ["Quit VS Code if it's running.", "Press ⋯ on a profile, then Open VS Code."]),
        GuideTopic(icon: "doc.on.doc", title: "Copy setup", summary: "Copy MCP servers, skills and settings to a profile.",
                   steps: ["Press Copy setup…", "Choose From and To.", "Tick what you want.", "Press Copy."],
                   tip: "Secrets start unticked. To undo a copy, use Restore…"),
        GuideTopic(icon: "slider.horizontal.3", title: "Rename or remove", summary: "Change a profile, or delete it.",
                   steps: ["Press ⋯ on a profile, then Settings…", "Change the name or colour, or press Remove…"],
                   tip: "Remove moves the profile's folder to the Trash. Other profiles stay signed in."),
        GuideTopic(icon: "stethoscope", title: "Fix a problem", summary: "Check that everything is set up.",
                   steps: ["Press Doctor.", "Press Repair files if it appears."]),
        GuideTopic(icon: "apple.terminal", title: "Terminal commands", summary: "The same actions, typed.",
                   commands: [("claude", "default profile"), ("claude-as <name>", "chosen profile"), ("cpm list", "all sessions"),
                              ("cpm new <name>", "start a session"), ("cpm resume <id>", "resume a session"), ("cpm stop <id>", "stop a session"),
                              ("cpm default <name>", "change the default"), ("cpm doctor", "check the setup")]),
        GuideTopic(icon: "info.circle", title: "Good to know", summary: "What this app doesn't do.",
                   tip: "The desktop app and Cowork stay on their own login. This app doesn't start by itself after a restart."),
    ]
}

/// A list of tasks, one open at a time. `showAll` is for rendering every topic to an image.
struct GuideTopics: View {
    var showAll = false
    @State private var open: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            ForEach(GuideTopic.all) { topic in
                let isOpen = showAll || open == topic.id
                VStack(alignment: .leading, spacing: 8) {
                    Button {
                        open = open == topic.id ? nil : topic.id
                    } label: {
                        header(topic, isOpen: isOpen)
                    }
                    .buttonStyle(.plain)
                    .accessibilityHint(isOpen ? "Collapse" : "Expand")
                    if isOpen { details(topic).padding(.leading, 34) }
                }
                .padding(.vertical, 7).padding(.horizontal, 8)
                .background(isOpen ? Color.primary.opacity(0.05) : Color.clear, in: RoundedRectangle(cornerRadius: 8))
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func header(_ topic: GuideTopic, isOpen: Bool) -> some View {
        HStack(spacing: 10) {
            Image(systemName: topic.icon).font(.system(size: 14)).foregroundStyle(.secondary).frame(width: 24)
            VStack(alignment: .leading, spacing: 1) {
                Text(topic.title).font(.system(size: 13, weight: .semibold))
                Text(topic.summary).font(.system(size: 11.5)).foregroundStyle(.secondary)
            }
            Spacer(minLength: 4)
            Image(systemName: isOpen ? "chevron.down" : "chevron.right").font(.system(size: 10, weight: .semibold)).foregroundStyle(.tertiary)
        }
        .contentShape(Rectangle())
    }

    private func details(_ topic: GuideTopic) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(Array(topic.steps.enumerated()), id: \.offset) { i, step in
                HStack(alignment: .firstTextBaseline, spacing: 7) {
                    Text("\(i + 1)").font(.system(size: 11, weight: .semibold)).foregroundStyle(.secondary).frame(width: 12)
                    Text(step).font(.system(size: 12.5)).fixedSize(horizontal: false, vertical: true)
                }
            }
            if !topic.commands.isEmpty {
                Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 4) {
                    ForEach(topic.commands, id: \.command) { c in
                        GridRow {
                            Text(c.command).font(.system(size: 11, design: .monospaced)).textSelection(.enabled)
                            Text(c.meaning).font(.system(size: 11.5)).foregroundStyle(.secondary)
                        }
                    }
                }
            }
            if let tip = topic.tip {
                Text(tip).font(.system(size: 11.5)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

struct GuideView: View {
    @Bindable var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Guide").font(.system(size: 15, weight: .semibold))
            ScrollView { GuideTopics() }
                .frame(minHeight: 300, maxHeight: 480)
            HStack {
                Spacer()
                Button("Done") { model.route = .main }.keyboardShortcut(.defaultAction)
            }
        }
    }
}
