import ProfileCore
import SwiftUI

/// Which profile the "To" menu should hold once "From" changes: the current one if it is still
/// valid, otherwise the first valid one, or nothing if there is none. The built-in is never a target.
func copyTarget(source: String, current: String, targets: [Profile]) -> String {
    let valid = targets.filter { $0.id != source }.map(\.id)
    return valid.contains(current) ? current : (valid.first ?? "")
}

/// Design flow 6. Nothing is written until Apply, and everything shown is what Apply will do.
struct CopySetupView: View {
    @Bindable var model: AppModel
    @State private var sourceId = ""
    @State private var targetId = ""

    private var sources: [Profile] { model.profiles.filter { $0.state == .ready } }
    private var targets: [Profile] { model.profiles.filter { !$0.builtin && $0.state == .ready } }
    private var validTargets: [Profile] { targets.filter { $0.id != sourceId } }

    private static let groups: [(String, [CopyKind])] = [
        ("MCP SERVERS", [.mcpServer]),
        ("SETTINGS", [.permissions, .model, .env, .hook]),
        ("SKILLS, AGENTS, COMMANDS, CLAUDE.MD", [.skill, .agent, .command, .claudeMd]),
        ("PLUGINS", [.plugin]),
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Copy setup").font(.system(size: 15, weight: .semibold))
            if targets.isEmpty {
                Text("Add and sign in to another profile first. The built-in profile can be copied from, never into.")
                    .font(.system(size: 12)).fixedSize(horizontal: false, vertical: true)
                HStack { Spacer(); Button("Done") { model.route = .main } }
            } else {
                HStack {
                    Picker("From", selection: $sourceId) { ForEach(sources) { Text($0.displayName).tag($0.id) } }
                    Picker("To", selection: $targetId) {
                        if validTargets.isEmpty { Text("None").tag("") }
                        ForEach(validTargets) { Text($0.displayName).tag($0.id) }
                    }
                }
                .font(.system(size: 12))
                if let reason = model.copyPlan?.blockedReason { note(reason, tint: .red) }
                if let e = model.errorText { note(e, tint: .red) }
                results
                list
                Text("Not copyable: claude.ai connectors and account-synced plugins belong to the account. Connect them at claude.ai while signed in as \(model.profile(id: targetId)?.email ?? "the target account").")
                    .font(.system(size: 11.5)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                HStack {
                    Menu("Restore…") {
                        if model.backups.isEmpty { Text("No backups yet") }
                        ForEach(model.backups) { b in
                            Button(b.createdAt.formatted(date: .abbreviated, time: .shortened)) {
                                Task { await model.restore(backupId: b.id, targetId: targetId) }
                            }
                        }
                    }
                    .fixedSize()
                    Spacer()
                    Button("Done") { model.route = .main }
                    Button(applyLabel) { Task { await model.applyCopy() } }
                        .keyboardShortcut(.defaultAction)
                        .disabled(model.copyBusy || model.copyPlan == nil || model.copyPlan?.blockedReason != nil || model.copyPlan?.tickedCount == 0)
                }
            }
        }
        .onAppear(perform: chooseDefaults)
        .onChange(of: sourceId) {
            let next = copyTarget(source: sourceId, current: targetId, targets: targets)
            if next == targetId { reload() } else { targetId = next }
        }
        .onChange(of: targetId) { reload() }
    }

    private var applyLabel: String {
        if model.copyBusy { return "Copying…" }
        let n = model.copyPlan?.tickedCount ?? 0
        return n == 1 ? "Copy 1 item" : "Copy \(n) items"
    }

    private func chooseDefaults() {
        sourceId = model.profile(id: Profile.builtinID)?.state == .ready ? Profile.builtinID : (sources.first?.id ?? "")
        targetId = copyTarget(source: sourceId, current: "", targets: targets)
        reload()
    }

    private func reload() {
        guard !sourceId.isEmpty, !targetId.isEmpty, sourceId != targetId else {
            model.copyPlan = nil
            return
        }
        Task { await model.preparePreview(sourceId: sourceId, targetId: targetId) }
    }

    @ViewBuilder private var results: some View {
        if let r = model.copyResult {
            if r.failed.isEmpty {
                note("Copied \(r.done.count) item(s). \(targetBackedUp(r))", tint: .green)
            } else {
                note("Stopped after \(r.done.count) item(s). \(r.failed[0].itemId): \(r.failed[0].reason) Nothing after it was copied. \(targetBackedUp(r))", tint: .red)
            }
            removals(r.removalCommands)
        }
        if let r = model.restoreResult {
            note("Restored \(r.restored.count) and deleted \(r.removed.count) file(s).\(r.leftAlone.isEmpty ? "" : " Left alone because they changed since: \(r.leftAlone.map { ($0 as NSString).lastPathComponent }.joined(separator: ", ")).")", tint: .green)
            removals(r.removalCommands)
        }
    }

    private func targetBackedUp(_ r: ApplyResult) -> String { r.backupId == nil ? "" : "A backup is in ~/.claude-profiles/backups." }

    @ViewBuilder private func removals(_ commands: [String]) -> some View {
        if !commands.isEmpty {
            VStack(alignment: .leading, spacing: 3) {
                Text("Installed by Claude Code, so Restore cannot undo these. To remove:").font(.system(size: 11.5))
                Text(commands.joined(separator: "\n")).font(.system(size: 10, design: .monospaced)).textSelection(.enabled)
            }
        }
    }

    private var list: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 2) {
                if let plan = model.copyPlan {
                    if plan.items.isEmpty { Text("Nothing to copy from this profile.").foregroundStyle(.secondary) }
                    ForEach(Self.groups, id: \.0) { title, kinds in
                        let items = plan.items.filter { kinds.contains($0.kind) }
                        if !items.isEmpty {
                            Text(title).font(.system(size: 11, weight: .semibold)).foregroundStyle(.secondary).padding(.top, 8)
                            ForEach(items) { CopyItemRow(model: model, item: $0) }
                        }
                    }
                } else if validTargets.isEmpty {
                    Text("There is nothing to copy \(model.profile(id: sourceId)?.displayName ?? "this profile") into, because the built-in profile can only be copied from. Add another profile, or choose a different profile in From.")
                        .font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                } else {
                    ProgressView().controlSize(.small)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .frame(minHeight: 180, maxHeight: 330)
    }

    private func note(_ text: String, tint: Color) -> some View {
        Text(text).font(.system(size: 12)).padding(8).fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(tint.opacity(0.13), in: RoundedRectangle(cornerRadius: 8))
    }
}

private struct CopyItemRow: View {
    @Bindable var model: AppModel
    let item: CopyItem

    private func update(_ body: (inout CopyItem) -> Void) {
        guard let i = model.copyPlan?.items.firstIndex(where: { $0.id == item.id }) else { return }
        body(&model.copyPlan!.items[i])
    }

    private var target: String { model.copyPlan.flatMap { model.profile(id: $0.targetId)?.displayName } ?? "the target" }

    private var warnings: [String] {
        var w: [String] = []
        if !item.secrets.isEmpty { w.append("May contain a secret. Unticked by default. Copying puts any ticked value in plain text in \(target).") }
        if item.flaggedFile { w.append("This looks like it contains a secret. Unticked by default. Check it before copying.") }
        if item.pointsIntoSource { w.append("It points into the source profile's folder, so it would keep depending on that folder.") }
        if item.needsReLogin { w.append("Uses OAuth sign-in. You will need to sign in again in \(target).") }
        return w
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Toggle(isOn: Binding(get: { item.ticked }, set: { v in update { $0.ticked = v } })) {
                    Text(item.key).fontWeight(.medium).lineLimit(1).truncationMode(.middle)
                }
                .toggleStyle(.checkbox).disabled(!item.canBeTicked)
                Text(item.action.rawValue.capitalized).font(.system(size: 10, weight: .semibold))
                    .padding(.horizontal, 5).padding(.vertical, 1)
                    .background(badge.opacity(0.18), in: RoundedRectangle(cornerRadius: 4))
                Spacer(minLength: 0)
            }
            if !item.detail.isEmpty { Text(item.detail).font(.system(size: 11.5)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true).padding(.leading, 20) }
            ForEach(warnings, id: \.self) { Text($0).font(.system(size: 11.5)).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true).padding(.leading, 20) }
            if item.ticked, !item.secrets.isEmpty {
                ForEach(item.secrets, id: \.name) { field in
                    Toggle(isOn: Binding(get: { field.ticked }, set: { v in
                        update { $0.secrets[$0.secrets.firstIndex { $0.name == field.name }!].ticked = v }
                    })) {
                        Text("Include \(field.name) = \(SecretDetector.mask)").font(.system(size: 11.5))
                    }
                    .toggleStyle(.checkbox).padding(.leading, 20)
                }
            }
            if item.ticked, item.isFile, item.action == .change {
                Picker("If it exists", selection: Binding(get: { item.conflict }, set: { v in update { $0.conflict = v } })) {
                    ForEach(CopyConflict.allCases, id: \.self) { Text($0.rawValue.capitalized).tag($0) }
                }
                .pickerStyle(.segmented).font(.system(size: 11)).padding(.leading, 20).frame(maxWidth: 300)
            }
        }
        .padding(.vertical, 3)
    }

    private var badge: Color {
        switch item.action {
        case .add: return .green
        case .change: return .blue
        case .same: return .gray
        }
    }
}
