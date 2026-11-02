import AppKit
import ProfileCore
import SwiftUI

/// The swatches offered for a new profile.
let profileColors = ["#E8772E", "#0F8A7E", "#8B5CF6", "#C2337A", "#7A6A1F", "#44546A"]

/// Shows whichever step the model's route names. Used by the panel and in a sheet in the window.
public struct FlowView: View {
    @Bindable var model: AppModel
    public init(model: AppModel) { self.model = model }

    public var body: some View {
        Group {
            switch model.route {
            case .main: EmptyView()
            case .add: AddProfileView(model: model)
            case .waiting(let id): WaitingForLoginView(model: model, profileId: id)
            case .settings(let id): ProfileSettingsView(model: model, profileId: id)
            case .doctor: DoctorView(model: model)
            case .shell: ShellSetupView(model: model)
            case .stop(let id): StopConfirmation(model: model, sessionId: id)
            case .copy: CopySetupView(model: model)
            }
        }
        .padding(16)
    }
}

private struct FlowTitle: View {
    let text: String
    var body: some View { Text(text).font(.system(size: 15, weight: .semibold)) }
}

struct AddProfileView: View {
    @Bindable var model: AppModel
    @State private var name = ""
    @State private var command = ""
    @State private var color = profileColors[0]
    @State private var busy = false

    /// Suggested from the display name until the user types their own.
    private var suggested: String {
        let s = name.lowercased().map { $0.isLetter && $0.isASCII || $0.isNumber ? $0 : "-" }
        return String(String(s).trimmingCharacters(in: CharacterSet(charactersIn: "-")).prefix(24))
    }

    private var commandName: String { command.isEmpty ? suggested : command }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            FlowTitle(text: "Add profile")
            VStack(alignment: .leading, spacing: 4) {
                Text("Display name").font(.system(size: 12))
                TextField("Work", text: $name).textFieldStyle(.roundedBorder)
            }
            VStack(alignment: .leading, spacing: 4) {
                Text("Command name").font(.system(size: 12))
                TextField(suggested.isEmpty ? "work" : suggested, text: $command).textFieldStyle(.roundedBorder)
                Text("Used as claude-as \(commandName.isEmpty ? "name" : commandName). It cannot change later.")
                    .font(.system(size: 11.5)).foregroundStyle(.secondary)
            }
            VStack(alignment: .leading, spacing: 6) {
                Text("Colour").font(.system(size: 12))
                HStack(spacing: 12) {
                    ForEach(profileColors, id: \.self) { c in
                        Button { color = c } label: {
                            Circle().fill(Color(hex: c)).frame(width: 24, height: 24)
                                .overlay(Circle().stroke(Color.primary, lineWidth: c == color ? 2 : 0).padding(-3))
                        }
                        .buttonStyle(.plain).accessibilityLabel("Colour \(c)")
                    }
                }
            }
            if !model.shellInstalled {
                Text("Set up the shell first, because sign-in runs through claude-as.").font(.system(size: 12)).foregroundStyle(.orange)
            }
            HStack {
                Spacer()
                Button("Cancel") { model.route = .main }
                Button("Continue to sign-in") {
                    busy = true
                    Task {
                        await model.addProfile(displayName: name, commandName: commandName, color: color)
                        busy = false
                    }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(busy || name.isEmpty || commandName.isEmpty || !model.shellInstalled)
            }
            if let e = model.errorText { Text(e).font(.system(size: 12)).foregroundStyle(.red).fixedSize(horizontal: false, vertical: true) }
        }
    }
}

struct WaitingForLoginView: View {
    @Bindable var model: AppModel
    let profileId: String

    private var profile: Profile? { model.profile(id: profileId) }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            switch model.loginState {
            case .detected(let account, let duplicateOf):
                FlowTitle(text: "Signed in")
                VStack(alignment: .leading, spacing: 2) {
                    Text("Account: \(account.email ?? "unknown")").fontWeight(.semibold)
                    Text("Organization: \(account.organizationName ?? "unknown")")
                }
                if let d = duplicateOf, let other = model.profile(id: d) {
                    warning("The profile \(other.displayName) already uses this account. Both would share its usage limits.")
                }
                Text("Is this the account you meant to add?").font(.system(size: 12)).foregroundStyle(.secondary)
                HStack {
                    Button("Discard") { Task { await model.discardLogin(profileId: profileId) } }
                    Spacer()
                    Button("Sign out and retry") { Task { await model.retryLogin(profileId: profileId) } }
                    Button("Keep") { Task { await model.keepLogin(profileId: profileId) } }
                        .keyboardShortcut(.defaultAction).disabled(model.loginBusy)
                }
            case .timedOut:
                FlowTitle(text: "Still not signed in")
                Text("Sign-in for \(profile?.displayName ?? "this profile") has not finished.").fixedSize(horizontal: false, vertical: true)
                buttons
            default:
                FlowTitle(text: "Waiting for sign-in…")
                Text("A Terminal window opened running claude-as \(profile?.commandName ?? "") auth login. Finish signing in there.")
                    .fixedSize(horizontal: false, vertical: true)
                warning("Check the browser is signed into the account you want, or use a private window. Otherwise this profile may log into an account you already added.")
                buttons
            }
            if let e = model.errorText { Text(e).font(.system(size: 12)).foregroundStyle(.red).fixedSize(horizontal: false, vertical: true) }
        }
        .task { await model.pollLogin(profileId: profileId) }
    }

    private var buttons: some View {
        HStack {
            Button("Close") { model.route = .main }
            Spacer()
            Button("Discard") { Task { await model.discardLogin(profileId: profileId) } }
            Button("Retry") { Task { await model.retryLogin(profileId: profileId) } }
        }
    }

    private func warning(_ text: String) -> some View {
        Text(text).font(.system(size: 12)).padding(10).fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color.orange.opacity(0.15), in: RoundedRectangle(cornerRadius: 8))
    }
}

struct ProfileSettingsView: View {
    @Bindable var model: AppModel
    let profileId: String
    @State private var name = ""

    private var profile: Profile? { model.profile(id: profileId) }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            FlowTitle(text: "Profile settings")
            if let p = profile {
                VStack(alignment: .leading, spacing: 4) {
                    Text("Display name").font(.system(size: 12))
                    TextField("Name", text: $name).textFieldStyle(.roundedBorder)
                        .onAppear { name = p.displayName }
                        .onSubmit { if name != p.displayName { model.rename(profile: p, to: name) } }
                }
                VStack(alignment: .leading, spacing: 6) {
                    Text("Colour").font(.system(size: 12))
                    HStack(spacing: 12) {
                        ForEach(([p.color] + profileColors.filter { $0 != p.color }), id: \.self) { c in
                            Button { model.setColor(profile: p, color: c) } label: {
                                Circle().fill(Color(hex: c)).frame(width: 24, height: 24)
                                    .overlay(Circle().stroke(Color.primary, lineWidth: c == p.color ? 2 : 0).padding(-3))
                            }
                            .buttonStyle(.plain).accessibilityLabel("Colour \(c)")
                        }
                    }
                }
                Text("Command: claude-as \(p.commandName). Fixed when the profile was created. Renaming changes only the display name; the folder and sign-in stay the same.")
                    .font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                Text("Account: \(p.email ?? "not signed in")").font(.system(size: 12)).foregroundStyle(.secondary)
                if p.builtin {
                    Text("This is your existing ~/.claude. The app never writes to it, so it has no status line and cannot be removed.")
                        .font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
                HStack {
                    Button("Open VS Code on \(p.displayName)") { model.openVSCode(profile: p) }
                    if p.id != model.defaultProfileId, p.state == .ready {
                        Button("Make default") { model.setDefault(profile: p) }
                    }
                }.controlSize(.small)
                Text("Quit VS Code first if it is running. Opened from the Dock, it uses the built-in profile.")
                    .font(.system(size: 11.5)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                HStack {
                    Spacer()
                    Button("Done") {
                        if name != p.displayName { model.rename(profile: p, to: name) }
                        model.route = .main
                    }.keyboardShortcut(.defaultAction)
                }
            }
        }
    }
}

struct DoctorView: View {
    @Bindable var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            FlowTitle(text: "Doctor")
            if model.doctorRunning && model.doctorReport == nil {
                ProgressView().controlSize(.small)
            }
            if let report = model.doctorReport {
                ScrollView {
                    VStack(alignment: .leading, spacing: 6) {
                        ForEach(Array(report.findings.enumerated()), id: \.offset) { _, f in
                            HStack(alignment: .top, spacing: 6) {
                                Image(systemName: icon(f.level)).foregroundStyle(tint(f.level)).frame(width: 14)
                                VStack(alignment: .leading, spacing: 1) {
                                    Text(f.message).fixedSize(horizontal: false, vertical: true)
                                    if let fix = f.fix { Text("Fix: \(fix)").font(.system(size: 11.5)).foregroundStyle(.secondary).textSelection(.enabled) }
                                }
                            }
                            .font(.system(size: 12))
                        }
                    }
                }.frame(maxHeight: 340)
            }
            HStack {
                if model.doctorReport?.hasFaults == true {
                    Button("Repair files") { Task { await model.fixAndRerunDoctor() } }
                }
                Spacer()
                Button("Done") { model.route = .main }.keyboardShortcut(.defaultAction)
            }
        }
        .task { await model.runDoctor() }
    }

    private func icon(_ l: Finding.Level) -> String {
        switch l {
        case .ok: return "checkmark.circle"
        case .note: return "info.circle"
        case .fault: return "exclamationmark.triangle"
        }
    }

    private func tint(_ l: Finding.Level) -> Color {
        switch l {
        case .ok: return .green
        case .note: return .secondary
        case .fault: return .red
        }
    }
}

struct ShellSetupView: View {
    @Bindable var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            FlowTitle(text: "Set up shell")
            if let r = model.installResult {
                Text("Installed. Paste these two commands into Terminal, then open a new window. They add a newline first, because a file may not end with one.")
                    .font(.system(size: 12)).fixedSize(horizontal: false, vertical: true)
                let lines = Self.commands(r)
                Text(lines).font(.system(size: 10.5, design: .monospaced)).textSelection(.enabled)
                    .padding(8).frame(maxWidth: .infinity, alignment: .leading)
                    .background(.quaternary, in: RoundedRectangle(cornerRadius: 6))
                HStack {
                    Button("Copy commands") {
                        NSPasteboard.general.clearContents()
                        NSPasteboard.general.setString(lines, forType: .string)
                    }
                    Spacer()
                    Button("Done") { model.route = .main }.keyboardShortcut(.defaultAction)
                }
            } else {
                Text("The app starts sessions through claude-as, so nothing can launch until the shell layer is installed. This copies files to ~/.claude-profiles/bin. It never edits your shell files; you add two lines yourself.")
                    .font(.system(size: 12)).fixedSize(horizontal: false, vertical: true)
                HStack {
                    Spacer()
                    Button("Cancel") { model.route = .main }
                    Button("Install") { Task { await model.setUpShell() } }.keyboardShortcut(.defaultAction)
                }
            }
            if let e = model.errorText { Text(e).font(.system(size: 12)).foregroundStyle(.red).fixedSize(horizontal: false, vertical: true) }
        }
    }

    static func commands(_ r: InstallResult) -> String {
        let q = { (s: String) in "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'" }
        return "printf '\\n%s\\n' \(q(r.zshenvLine)) >> ~/.zshenv\nprintf '\\n%s\\n' \(q(r.zshrcLine)) >> ~/.zshrc"
    }
}

struct StopConfirmation: View {
    @Bindable var model: AppModel
    let sessionId: String
    // Held here, so the row cannot change under the question while the list refreshes.
    @State private var held: SessionRow?

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            FlowTitle(text: "Stop this session?")
            if let row = held ?? model.row(sessionId: sessionId) {
                Text(row.title).fontWeight(.semibold).lineLimit(2)
                Text("\(model.profile(id: row.profileId)?.displayName ?? "Unknown") · \(row.cwd ?? "unknown folder") · \(row.status ?? "status unknown")")
                    .font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                if row.status == "busy" {
                    Text("Claude Code is working. Stopping now can leave a file edit unfinished.")
                        .font(.system(size: 12)).padding(10).frame(maxWidth: .infinity, alignment: .leading)
                        .background(Color.red.opacity(0.12), in: RoundedRectangle(cornerRadius: 8))
                }
                Text("The Terminal window stays open.").font(.system(size: 12)).foregroundStyle(.secondary)
                HStack {
                    Spacer()
                    Button("Cancel") { model.route = .main }
                    Button("Stop session", role: .destructive) { Task { await model.confirmStop(row) } }.disabled(model.stopBusy)
                }
            } else {
                Text("That session is no longer running.")
                HStack { Spacer(); Button("Done") { model.route = .main } }
            }
        }
        .onAppear { held = model.row(sessionId: sessionId) }
    }
}
