import Foundation

/// The model and effort level Claude Code used for one reply.
public struct ModelSetting: Equatable, Sendable {
    public var model: String
    public var effort: String?

    public init(model: String, effort: String?) {
        self.model = model
        self.effort = effort
    }

    /// Only the main conversation's replies count. A subagent's reply is on a side chain, and
    /// "<synthetic>" marks an error placeholder rather than a model.
    static func from(_ obj: [String: Any]) -> ModelSetting? {
        guard obj["type"] as? String == "assistant", obj["isSidechain"] as? Bool != true,
              let model = (obj["message"] as? [String: Any])?["model"] as? String,
              !model.isEmpty, !model.hasPrefix("<") else { return nil }
        // perTurnEffort is what the reply used. Older Claude Code wrote only effort.
        return ModelSetting(model: model, effort: (obj["perTurnEffort"] as? String) ?? (obj["effort"] as? String))
    }
}

/// What a session's tokens would cost at API prices, as Claude Code itself calculates it. Claude Code
/// writes this as a "cost-state" line, mostly when a session closes. It includes subagents' models.
public struct SessionCost: Equatable, Sendable {
    public struct ModelCost: Equatable, Sendable {
        public var model: String
        public var usd: Double
    }

    public var totalUSD: Double
    /// Most expensive first.
    public var byModel: [ModelCost]
    /// Claude Code had no price for a model, so the total may be too low.
    public var hasUnknownPrices: Bool

    static func from(_ obj: [String: Any]) -> SessionCost? {
        guard obj["type"] as? String == "cost-state", let usage = obj["modelUsage"] as? [String: Any] else { return nil }
        let byModel = usage.compactMap { model, value -> ModelCost? in
            guard let usd = ((value as? [String: Any])?["costUSD"] as? NSNumber)?.doubleValue else { return nil }
            return ModelCost(model: model, usd: usd)
        }.sorted { $0.usd != $1.usd ? $0.usd > $1.usd : $0.model < $1.model }
        let total = (obj["totalCostUSD"] as? NSNumber)?.doubleValue ?? byModel.reduce(0) { $0 + $1.usd }
        return SessionCost(totalUSD: total, byModel: byModel, hasUnknownPrices: obj["hasUnknownModelCost"] as? Bool ?? false)
    }

    /// "$12.34", or "<$0.01" for a cost that rounds to nothing.
    public static func format(_ usd: Double) -> String {
        if usd > 0, usd < 0.005 { return "<$0.01" }
        return usd.formatted(.currency(code: "USD").precision(.fractionLength(2)))
    }
}

/// A run of replies with the same model and effort.
public struct ModelSpan: Equatable, Sendable {
    public var setting: ModelSetting
    public var from: Date?
    public var replies: Int
}

/// What a session worked on: Claude Code's own recap (an "away_summary" line). It ends with a
/// hint about turning recaps off, which is dropped.
public enum Recap {
    static func from(_ obj: [String: Any]) -> String? {
        guard obj["type"] as? String == "system", obj["subtype"] as? String == "away_summary",
              let text = obj["content"] as? String else { return nil }
        let clean = text.replacingOccurrences(of: #"\s*\(disable recaps[^)]*\)\s*$"#, with: "", options: .regularExpression)
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return clean.isEmpty ? nil : clean
    }
}

/// Reads a session's details: which models and effort levels it used, in order, its cost, its
/// newest recap, the files it changed and its pull requests. The first read covers the
/// whole file, so it is for one session at a time, off the main thread. Later reads take only
/// what was added, because a running session keeps growing.
public struct ModelHistory: Sendable {
    public private(set) var spans: [ModelSpan] = []
    /// The newest cost line read, if Claude Code has written one.
    public private(set) var cost: SessionCost?
    public private(set) var recap: String?
    /// Paths relative to the session's folder, in the order they were first changed.
    public private(set) var changedFiles: [String] = []
    public private(set) var pullRequests: [String] = []
    private var offset: UInt64 = 0
    /// Claude Code writes one line per part of a reply (thinking, text, each tool call), all with
    /// the same message ID, so a reply is counted once per ID.
    private var counted: Set<String> = []

    public init() {}

    public static func read(_ url: URL) -> [ModelSpan] {
        var h = ModelHistory()
        h.update(url)
        return h.spans
    }

    /// A file that got shorter was replaced, so it is read again from the start.
    public mutating func update(_ url: URL) {
        guard let h = try? FileHandle(forReadingFrom: url) else { return }
        defer { try? h.close() }
        let size = (try? h.seekToEnd()) ?? 0
        if size < offset { self = ModelHistory() }
        guard size > offset, (try? h.seek(toOffset: offset)) != nil else { return }
        var pending = Data()
        while let chunk = try? h.read(upToCount: 1 << 20), !chunk.isEmpty {
            pending.append(chunk)
            var start = pending.startIndex
            while let end = pending[start...].firstIndex(of: UInt8(ascii: "\n")) {
                take(Data(pending[start..<end]))
                offset += UInt64(end - start + 1)
                start = pending.index(after: end)
            }
            pending = Data(pending[start...])
        }
        // A line without its newline may still be being written, so it waits for the next read.
    }

    private mutating func take(_ line: Data) {
        let other = ["\"cost-state\"", "\"away_summary\"", "\"file-history-delta\"", "\"pr-link\""].contains { TranscriptReader.contains(line[...], $0) }
        guard other || (TranscriptReader.contains(line[...], "\"assistant\"") && TranscriptReader.contains(line[...], "\"model\"")),
              let obj = (try? JSONSerialization.jsonObject(with: line)) as? [String: Any] else { return }
        if let c = SessionCost.from(obj) {
            cost = c
            return
        }
        if let r = Recap.from(obj) {
            recap = r
            return
        }
        switch obj["type"] as? String {
        case "file-history-delta":
            if let p = obj["trackingPath"] as? String, !p.isEmpty, !changedFiles.contains(p) { changedFiles.append(p) }
            return
        case "pr-link":
            if let u = obj["prUrl"] as? String, u.hasPrefix("https://"), !pullRequests.contains(u) { pullRequests.append(u) }
            return
        default:
            break
        }
        guard let s = ModelSetting.from(obj) else { return }
        if let id = (obj["message"] as? [String: Any])?["id"] as? String, !counted.insert(id).inserted { return }
        if spans.last?.setting == s {
            spans[spans.count - 1].replies += 1
        } else {
            spans.append(ModelSpan(setting: s, from: (obj["timestamp"] as? String).flatMap(Self.date), replies: 1))
        }
    }

    static func date(_ s: String) -> Date? {
        (try? Date.ISO8601FormatStyle(includingFractionalSeconds: true).parse(s)) ?? (try? Date.ISO8601FormatStyle().parse(s))
    }
}

/// Short names for the screen. Anything not recognised is shown as it is.
public enum ModelNames {
    /// "claude-opus-5-5" becomes "Opus 5.5", and a trailing date is dropped.
    public static func model(_ id: String) -> String {
        let parts = id.split(separator: "-").map(String.init)
        guard parts.count >= 3, parts[0] == "claude", parts[1].allSatisfy(\.isLetter) else { return id }
        let version = parts.dropFirst(2).prefix { $0.count <= 2 && $0.allSatisfy(\.isNumber) }
        guard !version.isEmpty else { return id }
        return parts[1].prefix(1).uppercased() + parts[1].dropFirst() + " " + version.joined(separator: ".")
    }

    public static func effort(_ level: String) -> String {
        switch level {
        case "xhigh": return "Extra high"
        default: return level.prefix(1).uppercased() + level.dropFirst()
        }
    }
}
