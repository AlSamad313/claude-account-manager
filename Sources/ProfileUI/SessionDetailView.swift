import ProfileCore
import SwiftUI

/// The window's side pane for one session: the model and effort in use, and every switch.
struct SessionDetailView: View {
    @Bindable var model: AppModel
    /// Off only for rendering to an image, which cannot draw inside a scroll view.
    var scrolls = true

    var body: some View {
        if let row = model.selectedRow {
            if scrolls { ScrollView { content(row) } } else { content(row) }
        } else {
            Text("Select a session").foregroundStyle(.secondary).frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    private func content(_ row: SessionRow) -> some View {
        VStack(alignment: .leading, spacing: 16) {
            header(row)
            summary(row)
            now(row)
            switches
            cost(row)
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func header(_ row: SessionRow) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(row.title).font(.system(size: 14, weight: .semibold)).fixedSize(horizontal: false, vertical: true)
            HStack(spacing: 6) {
                ProfileBadge(color: model.profile(id: row.profileId)?.color ?? "#8A8F98", size: 9)
                Text(model.profile(id: row.profileId)?.displayName ?? "Unknown")
                if row.live { Text("· Running").foregroundStyle(.green) }
            }
            .font(.system(size: 12))
            Text(row.cwd ?? "Unknown folder").font(.system(size: 11.5)).foregroundStyle(.secondary).lineLimit(2).truncationMode(.middle)
        }
    }

    /// The history's last entry is newer than the row's, which comes from the list's last refresh.
    private func now(_ row: SessionRow) -> some View {
        let current = model.history?.last?.setting ?? row.setting
        return VStack(alignment: .leading, spacing: 6) {
            sectionTitle(row.live ? "In use now" : "Last used")
            if let current {
                Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 4) {
                    GridRow { label("Model"); Text(ModelNames.model(current.model)) }
                    GridRow { label("Effort"); Text(current.effort.map(ModelNames.effort) ?? "Not recorded") }
                }
                .font(.system(size: 12.5))
            } else {
                Text("No replies yet.").font(.system(size: 12)).foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder private var switches: some View {
        VStack(alignment: .leading, spacing: 6) {
            sectionTitle("Switches in this session")
            switch model.history {
            case nil:
                ProgressView().controlSize(.small)
            case let spans? where spans.count <= 1:
                Text(spans.isEmpty ? "No replies yet." : "None. Every reply used the same model and effort.")
                    .font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            case let spans?:
                Text(spans.count == 2 ? "Switched once." : "Switched \(spans.count - 1) times.")
                    .font(.system(size: 12)).foregroundStyle(.secondary)
                ForEach(Array(spans.enumerated()), id: \.offset) { i, span in
                    HStack(alignment: .firstTextBaseline, spacing: 8) {
                        Text(span.from.map(time) ?? "–").font(.system(size: 11, design: .monospaced)).foregroundStyle(.secondary)
                            .frame(width: 64, alignment: .leading)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(describe(span.setting)).font(.system(size: 12.5, weight: i == spans.count - 1 ? .semibold : .regular))
                            Text(span.replies == 1 ? "1 reply" : "\(span.replies) replies").font(.system(size: 11)).foregroundStyle(.secondary)
                        }
                    }
                }
            }
        }
    }

    /// Claude Code's recap, then what the session changed. All of it comes from the transcript.
    private func summary(_ row: SessionRow) -> some View {
        let details = model.sessionDetails
        let recap = details?.recap ?? row.recap
        let files = details?.changedFiles ?? []
        let prs = details?.pullRequests ?? []
        return VStack(alignment: .leading, spacing: 6) {
            sectionTitle("Summary")
            if let recap {
                Text(recap).font(.system(size: 12.5)).fixedSize(horizontal: false, vertical: true).textSelection(.enabled)
            } else {
                note("No recap yet. Claude Code writes these itself, and not every session has one.")
            }
            if !files.isEmpty {
                Text(files.count == 1 ? "Changed 1 file" : "Changed \(files.count) files").font(.system(size: 12, weight: .medium)).padding(.top, 2)
                ForEach(files.suffix(6), id: \.self) { f in
                    Text(f).font(.system(size: 11, design: .monospaced)).foregroundStyle(.secondary).lineLimit(1).truncationMode(.head)
                }
                if files.count > 6 { note("and \(files.count - 6) more") }
            }
            ForEach(prs, id: \.self) { u in
                if let url = URL(string: u) {
                    Link(destination: url) { Text(url.pathComponents.suffix(3).joined(separator: "/")).font(.system(size: 12)) }
                }
            }
        }
    }

    /// The pane's figure is newer than the row's while the session is selected.
    private func cost(_ row: SessionRow) -> some View {
        let cost = model.sessionCost ?? row.cost
        return VStack(alignment: .leading, spacing: 6) {
            sectionTitle("API-equivalent cost")
            if let cost {
                Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 4) {
                    ForEach(cost.byModel, id: \.model) { m in
                        GridRow {
                            Text(ModelNames.model(m.model))
                            Text(SessionCost.format(m.usd)).monospacedDigit().gridColumnAlignment(.trailing)
                        }
                    }
                    if cost.byModel.count != 1 {
                        GridRow {
                            Text("Total").fontWeight(.semibold)
                            Text(SessionCost.format(cost.totalUSD)).fontWeight(.semibold).monospacedDigit()
                        }
                    }
                }
                .font(.system(size: 12.5))
                note("What these tokens would cost at API prices, as Claude Code calculates it. It includes models its subagents used. A subscription isn't charged this way.")
                if cost.hasUnknownPrices { note("Claude Code had no price for some models, so the total may be too low.") }
                if row.live { note("Claude Code saves this mostly when a session closes, so it may be behind.") }
            } else {
                note(row.live ? "Not saved yet. Claude Code saves it mostly when a session closes." : "Claude Code didn't save a cost for this session.")
            }
        }
    }

    private func note(_ t: String) -> some View {
        Text(t).font(.system(size: 11)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
    }

    private func describe(_ s: ModelSetting) -> String {
        ModelNames.model(s.model) + (s.effort.map { " · " + ModelNames.effort($0) } ?? "")
    }

    private func time(_ d: Date) -> String {
        Calendar.current.isDateInToday(d) ? d.formatted(date: .omitted, time: .shortened) : d.formatted(.dateTime.month(.abbreviated).day())
    }

    private func sectionTitle(_ t: String) -> some View {
        Text(t.uppercased()).font(.system(size: 11, weight: .semibold)).foregroundStyle(.secondary)
    }

    private func label(_ t: String) -> some View { Text(t).foregroundStyle(.secondary) }
}
