import AppKit
import ProfileCore
import SwiftUI

public struct MenuPanelView: View {
    @Bindable var model: AppModel
    let openWindow: () -> Void

    public init(model: AppModel, openWindow: @escaping () -> Void) {
        self.model = model
        self.openWindow = openWindow
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if model.route == .main {
                content
            } else {
                FlowView(model: model)
            }
        }
        .frame(width: 400)
        .onAppear { model.viewAppeared() }
        .onDisappear { model.viewDisappeared() }
    }

    private var content: some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .firstTextBaseline) {
                    Text("Claude Profiles").font(.system(size: 14, weight: .semibold))
                    Spacer()
                    Text("plain claude uses \(model.defaultProfile?.displayName ?? "unknown")")
                        .font(.system(size: 12)).foregroundStyle(.secondary).lineLimit(1)
                }
                TextField("Filter by title, folder or profile", text: $model.filter).textFieldStyle(.roundedBorder)
            }
            .padding(EdgeInsets(top: 12, leading: 14, bottom: 10, trailing: 14))
            Divider()
            MessageBanners(model: model)
            if !model.shellInstalled {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Set up shell first").fontWeight(.semibold)
                    Text("The app starts sessions through claude-as, so nothing can launch until the shell layer is installed.")
                        .fixedSize(horizontal: false, vertical: true)
                    Button("Set up shell…") { model.route = .shell }.controlSize(.small)
                }
                .font(.system(size: 12)).padding(10)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Color.orange.opacity(0.15), in: RoundedRectangle(cornerRadius: 8))
                .padding(8)
            }
            section("PROFILES") {
                ForEach(model.profiles) { ProfileRowView(model: model, profile: $0) }
            }
            Divider()
            section("RECENT SESSIONS") {
                if model.panelRows.isEmpty {
                    Text(model.filter.isEmpty ? "No sessions yet." : "No sessions match this filter.")
                        .foregroundStyle(.secondary).padding(.horizontal, 6).padding(.vertical, 8)
                } else {
                    ForEach(model.panelRows, id: \.sessionId) { SessionRowView(model: model, row: $0) }
                }
            }
            Divider()
            LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())], spacing: 6) {
                footerButton("Add profile…") { model.errorText = nil; model.route = .add }
                footerButton("Open window…", action: openWindow)
                footerButton("Copy setup…") { model.errorText = nil; model.route = .copy }
                footerButton("Doctor") { model.route = .doctor }
                footerButton("Guide") { model.route = .guide }
                footerButton("Quit") { NSApp.terminate(nil) }
            }
            .padding(8)
        }
    }

    private func section<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).font(.system(size: 11, weight: .semibold)).foregroundStyle(.secondary).padding(.horizontal, 6)
            content()
        }
        .padding(EdgeInsets(top: 8, leading: 8, bottom: 6, trailing: 8))
    }

    private func footerButton(_ title: String, action: @escaping () -> Void) -> some View {
        Button(action: action) { Text(title).frame(maxWidth: .infinity) }
    }
}

public struct MainWindowView: View {
    @Bindable var model: AppModel

    public init(model: AppModel) { self.model = model }

    private var target: Profile? {
        model.sidebarSelection == "all" ? model.defaultProfile : model.profile(id: model.sidebarSelection)
    }

    private var flowShown: Binding<Bool> {
        Binding(get: { model.route != .main }, set: { if !$0 { model.route = .main } })
    }

    public var body: some View {
        NavigationSplitView {
            VStack(spacing: 0) {
                List(selection: $model.sidebarSelection) {
                    Label { Text("All profiles") } icon: { ProfileBadge(color: "#8A8F98") }.tag("all")
                    Section("Profiles") {
                        ForEach(model.profiles) { p in
                            HStack(spacing: 8) {
                                ProfileBadge(color: p.color)
                                VStack(alignment: .leading, spacing: 0) {
                                    Text(p.displayName).lineLimit(1)
                                    Text(p.email ?? "Not signed in").font(.caption).foregroundStyle(.secondary).lineLimit(1)
                                }
                                Spacer(minLength: 0)
                                if p.id == model.defaultProfileId { Text("default").font(.caption2).foregroundStyle(.secondary) }
                            }
                            .tag(p.id)
                            .contextMenu {
                                Button("New Terminal") { model.chooseFolderAndStart(profile: p) }.disabled(!model.shellInstalled)
                                Button("Open VS Code") { model.openVSCode(profile: p) }
                                if p.id != model.defaultProfileId { Button("Make default") { model.setDefault(profile: p) } }
                                Button("Settings…") { model.route = .settings(profileId: p.id) }
                            }
                        }
                    }
                }
                Divider()
                VStack(spacing: 6) {
                    Button { model.errorText = nil; model.route = .add } label: { Text("Add profile…").frame(maxWidth: .infinity) }
                    Button { model.errorText = nil; model.route = .copy } label: { Text("Copy setup…").frame(maxWidth: .infinity) }
                    Button { model.route = .doctor } label: { Text("Doctor").frame(maxWidth: .infinity) }
                    Button { model.route = .guide } label: { Text("Guide").frame(maxWidth: .infinity) }
                }.padding(8)
            }
            .navigationSplitViewColumnWidth(min: 220, ideal: 260)
        } detail: {
            VStack(spacing: 0) {
                MessageBanners(model: model)
                if !model.shellInstalled {
                    HStack {
                        Text("Set up the shell first, because sessions start through claude-as.").font(.system(size: 12))
                        Spacer()
                        Button("Set up shell…") { model.route = .shell }.controlSize(.small)
                    }
                    .padding(8).background(Color.orange.opacity(0.15))
                }
                Table(model.filteredRows, selection: .constant(nil as String?)) {
                    TableColumn("Session") { Text($0.title).lineLimit(1) }
                    TableColumn("Folder") { Text($0.cwd ?? "unknown").foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle) }
                    TableColumn("Profile") { r in
                        HStack(spacing: 6) {
                            ProfileBadge(color: model.profile(id: r.profileId)?.color ?? "#8A8F98", size: 9)
                            Text(model.profile(id: r.profileId)?.displayName ?? "Unknown").lineLimit(1)
                        }
                    }.width(min: 90, ideal: 120)
                    TableColumn("Status") { r in
                        if r.live {
                            Text(r.status.map { "Running, \($0)" } ?? "Running").fontWeight(.semibold).foregroundStyle(.green)
                        } else {
                            Text(r.lastActivity.formatted(.relative(presentation: .named))).foregroundStyle(.secondary)
                        }
                    }.width(min: 90, ideal: 120)
                    TableColumn("") { SessionAction(model: model, row: $0) }.width(70)
                }
                Divider()
                Text("\(model.filteredRows.count) sessions. Running ones are rechecked every 2 seconds while this window is open.\(model.parseFailures > 0 ? " \(model.parseFailures) transcripts could not be read." : "")")
                    .font(.system(size: 12)).foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading).padding(.horizontal, 12).padding(.vertical, 8)
            }
            .toolbar {
                ToolbarItem(placement: .primaryAction) {
                    Button("New Terminal on \(target?.displayName ?? "default")") {
                        if let t = target { model.chooseFolderAndStart(profile: t) }
                    }
                    .disabled(!model.shellInstalled || target == nil)
                }
            }
        }
        .searchable(text: $model.filter, prompt: "Title, folder or profile")
        .navigationTitle("Claude Profiles")
        .frame(minWidth: 860, minHeight: 480)
        .sheet(isPresented: flowShown) { FlowView(model: model).frame(width: 420) }
        .onAppear { model.viewAppeared() }
        .onDisappear { model.viewDisappeared() }
    }
}

extension SessionRow: Identifiable {
    public var id: String { sessionId }
}
