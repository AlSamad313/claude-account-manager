import AppKit
import ProfileCore
import SwiftUI

public struct ProfileBadge: View {
    let color: String
    var size: CGFloat = 12

    public init(color: String, size: CGFloat = 12) {
        self.color = color
        self.size = size
    }

    public var body: some View {
        Circle().fill(Color(hex: color)).frame(width: size, height: size).accessibilityHidden(true)
    }
}

struct ProfileRowView: View {
    @Bindable var model: AppModel
    let profile: Profile

    private var isDefault: Bool { profile.id == model.defaultProfileId }

    private var subtitle: String {
        let live = model.liveCount(profileId: profile.id)
        let who = profile.email ?? "Not signed in"
        return live > 0 ? "\(who) · \(live) running" : who
    }

    var body: some View {
        HStack(spacing: 9) {
            ProfileBadge(color: profile.color)
            VStack(alignment: .leading, spacing: 1) {
                HStack(spacing: 6) {
                    Text(profile.displayName).fontWeight(.semibold).lineLimit(1)
                    if isDefault {
                        Text("DEFAULT").font(.system(size: 10, weight: .semibold))
                            .padding(.horizontal, 5).padding(.vertical, 1)
                            .background(.quaternary, in: RoundedRectangle(cornerRadius: 4))
                    }
                }
                Text(subtitle).font(.system(size: 11.5)).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
            }
            Spacer(minLength: 4)
            actions
        }
        .padding(.vertical, 4).padding(.horizontal, 6)
    }

    @ViewBuilder private var actions: some View {
        switch profile.state {
        case .pendingLogin:
            Button("Finish sign-in") { model.route = .waiting(profileId: profile.id) }.controlSize(.small)
        case .loggedOut:
            Button("Sign in") { model.signIn(profile: profile) }.controlSize(.small).disabled(!model.shellInstalled)
        default:
            if !isDefault {
                Button("Make default") { model.setDefault(profile: profile) }.buttonStyle(.link).font(.system(size: 11.5))
            }
            Button("New Terminal") { model.chooseFolderAndStart(profile: profile) }.controlSize(.small).disabled(!model.shellInstalled)
        }
        Menu {
            Button("Open VS Code") { model.openVSCode(profile: profile) }
            Button("Settings…") { model.route = .settings(profileId: profile.id) }
        } label: {
            Image(systemName: "ellipsis")
        }
        .menuStyle(.borderlessButton).menuIndicator(.hidden).fixedSize().accessibilityLabel("More for \(profile.displayName)")
    }
}

struct SessionRowView: View {
    @Bindable var model: AppModel
    let row: SessionRow

    private var profile: Profile? { model.profile(id: row.profileId) }

    private var when: String {
        if row.live {
            let status = row.status.map { "running, \($0)" } ?? "running"
            return row.mayBeStale ? "\(status), may be stale" : status
        }
        return row.lastActivity.formatted(.relative(presentation: .named))
    }

    var body: some View {
        HStack(spacing: 9) {
            ProfileBadge(color: profile?.color ?? "#8A8F98", size: 8)
            VStack(alignment: .leading, spacing: 1) {
                Text(row.title).lineLimit(1).truncationMode(.tail)
                Text("\(profile?.displayName ?? "Unknown") · \(when) · \(row.cwd ?? "unknown folder")")
                    .font(.system(size: 11.5)).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
            }
            Spacer(minLength: 4)
            SessionAction(model: model, row: row)
        }
        .padding(.vertical, 4).padding(.horizontal, 6)
    }
}

/// A live row shows Stop only, and any other row shows Resume only.
struct SessionAction: View {
    @Bindable var model: AppModel
    let row: SessionRow

    var body: some View {
        if row.live {
            Button("Stop", role: .destructive) { model.requestStop(row) }.controlSize(.small)
        } else {
            Button("Resume") { model.resume(row) }.controlSize(.small).disabled(!model.shellInstalled)
        }
    }
}

/// Errors and notices, shown at the top of the panel and the window.
struct MessageBanners: View {
    @Bindable var model: AppModel

    var body: some View {
        if let e = model.errorText {
            banner(e, tint: .red) { model.errorText = nil }
        }
        if let n = model.noticeText {
            banner(n, tint: .orange) { model.noticeText = nil }
        }
        if let row = model.missingFolderRow {
            VStack(alignment: .leading, spacing: 6) {
                Text("The folder for \"\(row.title)\" no longer exists: \(row.cwd ?? "unknown").").fixedSize(horizontal: false, vertical: true)
                HStack {
                    Button("Choose folder…") {
                        let panel = NSOpenPanel()
                        panel.canChooseDirectories = true
                        panel.canChooseFiles = false
                        NSApp.activate()
                        if panel.runModal() == .OK, let url = panel.url { model.resume(row, folderOverride: url) }
                        model.missingFolderRow = nil
                    }
                    Button("Cancel") { model.missingFolderRow = nil }
                }.controlSize(.small)
            }
            .font(.system(size: 12)).padding(8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color.orange.opacity(0.15), in: RoundedRectangle(cornerRadius: 8))
            .padding(8)
        }
    }

    private func banner(_ text: String, tint: Color, dismiss: @escaping () -> Void) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Text(text).fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
            Button(action: dismiss) { Image(systemName: "xmark") }.buttonStyle(.plain).accessibilityLabel("Dismiss")
        }
        .font(.system(size: 12)).padding(8)
        .background(tint.opacity(0.15), in: RoundedRectangle(cornerRadius: 8))
        .padding(8)
    }
}
