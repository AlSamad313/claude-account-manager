import Foundation
import Testing
@testable import ProfileCore

private func reply(_ model: String, effort: String? = nil, perTurn: String? = nil, at time: String? = nil, sidechain: Bool = false, id: String? = nil) -> String {
    var message: [String: Any] = ["model": model, "content": [["type": "text", "text": "hi"]]]
    if let id { message["id"] = id }
    var o: [String: Any] = ["type": "assistant", "message": message]
    if let effort { o["effort"] = effort }
    if let perTurn { o["perTurnEffort"] = perTurn }
    if let time { o["timestamp"] = time }
    if sidechain { o["isSidechain"] = true }
    return String(decoding: try! JSONSerialization.data(withJSONObject: o, options: [.sortedKeys]), as: UTF8.self)
}

private func costLine(_ total: Double, _ byModel: [String: Double], unknown: Bool = false) -> String {
    let usage = byModel.mapValues { ["costUSD": $0, "outputTokens": 10] }
    let o: [String: Any] = ["type": "cost-state", "totalCostUSD": total, "hasUnknownModelCost": unknown, "modelUsage": usage]
    return String(decoding: try! JSONSerialization.data(withJSONObject: o, options: [.sortedKeys]), as: UTF8.self)
}

private func write(_ s: Sandbox, _ lines: [String], name: String = "t.jsonl") throws -> URL {
    let url = s.home.appendingPathComponent(name)
    try Data((lines.joined(separator: "\n") + "\n").utf8).write(to: url)
    return url
}

@Suite struct ModelHistoryTests {
    @Test func repliesFoldIntoRunsWithTheirStartTime() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let url = try write(s, [
            #"{"type":"user","cwd":"/w"}"#,
            reply("claude-opus-5-5", effort: "medium", perTurn: "medium", at: "2026-11-17T09:00:00.000Z"),
            reply("claude-opus-5-5", effort: "medium", perTurn: "medium", at: "2026-11-17T09:01:00.000Z"),
            reply("claude-fable-5-1", effort: "medium", perTurn: "medium", at: "2026-11-17T09:05:00.000Z"),
            reply("claude-fable-5-1", effort: "max", perTurn: "max", at: "2026-11-17T09:09:00Z"),
        ])
        let spans = ModelHistory.read(url)
        #expect(spans.map(\.setting) == [ModelSetting(model: "claude-opus-5-5", effort: "medium"),
                                          ModelSetting(model: "claude-fable-5-1", effort: "medium"),
                                          ModelSetting(model: "claude-fable-5-1", effort: "max")])
        #expect(spans.map(\.replies) == [2, 1, 1])
        #expect(spans[0].from == ModelHistory.date("2026-11-17T09:00:00Z"))
        #expect(spans[2].from != nil, "a time without fractional seconds is read too")
    }

    @Test func onlyTheMainConversationsRealRepliesCount() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let url = try write(s, [
            reply("claude-opus-5-5", perTurn: "high"),
            reply("claude-haiku-5-5", perTurn: "low", sidechain: true),
            reply("<synthetic>"),
            reply("claude-opus-5-5", perTurn: "high"),
        ])
        let spans = ModelHistory.read(url)
        #expect(spans.count == 1 && spans[0].replies == 2)
    }

    /// Claude Code writes a line per part of a reply, all with one message ID.
    @Test func aReplySpreadOverSeveralLinesCountsOnce() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let url = try write(s, [reply("claude-opus-5-5", perTurn: "medium", id: "m1"), reply("claude-opus-5-5", perTurn: "medium", id: "m1"),
                                reply("claude-opus-5-5", perTurn: "medium", id: "m1"), reply("claude-opus-5-5", perTurn: "medium", id: "m2")])
        #expect(ModelHistory.read(url).map(\.replies) == [2])
    }

    @Test func theNewestCostLineWinsAndModelsAreSortedByCost() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let url = try write(s, [reply("claude-opus-5-5"), costLine(1, ["claude-opus-5-5": 1]),
                                reply("claude-opus-5-5"), costLine(5.5, ["claude-haiku-5-5": 0.25, "claude-opus-5-5": 5.25], unknown: true)])
        var h = ModelHistory()
        h.update(url)
        #expect(h.cost?.totalUSD == 5.5)
        #expect(h.cost?.byModel.map(\.model) == ["claude-opus-5-5", "claude-haiku-5-5"])
        #expect(h.cost?.hasUnknownPrices == true)
        #expect(TranscriptReader.summary(of: url)?.cost == h.cost)
        let none = try write(s, [reply("claude-opus-5-5")], name: "none.jsonl")
        #expect(TranscriptReader.summary(of: none)?.cost == nil)
    }

    @Test func theNewestRecapWinsWithoutTheHint() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let away = { (t: String) in #"{"type":"system","subtype":"away_summary","content":"\#(t)"}"# }
        let url = try write(s, [away("First pass done."), reply("claude-opus-5-5"), away("Fixed the copy screen. Next, test it. (disable recaps in /config)"),
                                #"{"type":"system","subtype":"turn_duration","content":"ignored"}"#])
        var h = ModelHistory()
        h.update(url)
        #expect(h.recap == "Fixed the copy screen. Next, test it.")
        #expect(TranscriptReader.summary(of: url)?.recap == h.recap)
        let only = try write(s, [away("(disable recaps in /config)")], name: "hint.jsonl")
        #expect(ModelHistory.read(only).isEmpty && TranscriptReader.summary(of: only)?.recap == nil)
    }

    /// In a long file, the list reads only the two ends. A recap at the start may be out of date,
    /// so the list shows none rather than that one.
    @Test func theListIgnoresARecapOrCostFoundOnlyAtTheStart() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let filler = (0..<3000).map { _ in #"{"type":"user","message":{"content":"\#(String(repeating: "x", count: 80))"}}"# }
        let early = [#"{"type":"system","subtype":"away_summary","content":"Old recap."}"#, costLine(1, ["claude-opus-5-5": 1])]
        let url = try write(s, [#"{"type":"user","cwd":"/w"}"#, reply("claude-opus-5-5")] + early + filler
                               + [#"{"type":"system","subtype":"away_summary","content":"Newer recap."}"#] + filler + [#"{"type":"ai-title","aiTitle":"T"}"#])
        let summary = TranscriptReader.summary(of: url)
        #expect(summary?.recap == nil && summary?.cost == nil)
        #expect(ModelHistory.read(url).isEmpty == false)
        var h = ModelHistory()
        h.update(url)
        #expect(h.recap == "Newer recap." && h.cost?.totalUSD == 1)
    }

    @Test func changedFilesAndPullRequestsAreListedOnce() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let delta = { (p: String) in #"{"type":"file-history-delta","trackingPath":"\#(p)","messageId":"m"}"# }
        let url = try write(s, [delta("Sources/A.swift"), delta("README.md"), delta("Sources/A.swift"),
                                #"{"type":"pr-link","prUrl":"https://github.com/o/r/pull/7","prRepository":"o/r"}"#,
                                #"{"type":"pr-link","prUrl":"https://github.com/o/r/pull/7"}"#, #"{"type":"pr-link","prUrl":"javascript:alert(1)"}"#])
        var h = ModelHistory()
        h.update(url)
        #expect(h.changedFiles == ["Sources/A.swift", "README.md"])
        #expect(h.pullRequests == ["https://github.com/o/r/pull/7"], "only https links, once each")
    }

    @Test func costsAreShownInDollars() {
        #expect(SessionCost.format(56.4286) == "$56.43")
        #expect(SessionCost.format(0.001) == "<$0.01")
        #expect(SessionCost.format(0) == "$0.00")
    }

    @Test func perTurnEffortWinsAndOlderLinesFallBackToEffort() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let url = try write(s, [reply("claude-opus-5-5", effort: "medium", perTurn: "max"), reply("claude-opus-5-5", effort: "high"), reply("claude-opus-5-5")])
        #expect(ModelHistory.read(url).map(\.setting.effort) == ["max", "high", nil])
    }

    @Test func laterReadsTakeOnlyWhatWasAddedAndWaitForAWholeLine() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let url = try write(s, [reply("claude-opus-5-5", perTurn: "medium")])
        var h = ModelHistory()
        h.update(url)
        #expect(h.spans.map(\.replies) == [1])
        let half = reply("claude-fable-5-1", perTurn: "max")
        let file = try FileHandle(forWritingTo: url)
        try file.seekToEnd()
        try file.write(contentsOf: Data((reply("claude-opus-5-5", perTurn: "medium") + "\n" + half.prefix(20)).utf8))
        h.update(url)
        #expect(h.spans.map(\.replies) == [2], "the half-written line is not read yet")
        try file.write(contentsOf: Data((half.dropFirst(20) + "\n").utf8))
        try file.close()
        h.update(url)
        #expect(h.spans.map(\.setting.model) == ["claude-opus-5-5", "claude-fable-5-1"])
        #expect(h.spans == ModelHistory.read(url), "the same as reading it all at once")
        _ = try write(s, [reply("claude-haiku-5-5")])
        h.update(url)
        #expect(h.spans.map(\.setting.model) == ["claude-haiku-5-5"], "a shorter file is read again from the start")
    }

    @Test func aMissingOrBrokenFileGivesNoHistory() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        #expect(ModelHistory.read(s.home.appendingPathComponent("none.jsonl")).isEmpty)
        let url = try write(s, ["not json", #"{"type":"assistant""#])
        #expect(ModelHistory.read(url).isEmpty)
    }

    @Test func theSummaryTakesTheNewestReplyEvenInALongFile() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let filler = (0..<3000).map { _ in #"{"type":"user","message":{"content":"\#(String(repeating: "x", count: 80))"}}"# }
        let url = try write(s, [#"{"type":"user","cwd":"/w","entrypoint":"cli"}"#, reply("claude-opus-5-5", perTurn: "medium")]
                               + filler + [reply("claude-fable-5-2", perTurn: "max"), #"{"type":"ai-title","aiTitle":"T"}"#])
        #expect((try FileManager.default.attributesOfItem(atPath: url.path)[.size] as? Int ?? 0) > 2 * TranscriptReader.window)
        #expect(TranscriptReader.summary(of: url)?.setting == ModelSetting(model: "claude-fable-5-2", effort: "max"))
        let none = try write(s, [#"{"type":"user","cwd":"/w"}"#, #"{"type":"ai-title","aiTitle":"T"}"#], name: "none.jsonl")
        #expect(TranscriptReader.summary(of: none)?.setting == nil)
    }

    @Test func namesAreShortAndUnknownOnesAreLeftAlone() {
        #expect(ModelNames.model("claude-opus-5-5") == "Opus 5.5")
        #expect(ModelNames.model("claude-fable-5-1") == "Fable 5.1")
        #expect(ModelNames.model("claude-opus-5") == "Opus 5")
        #expect(ModelNames.model("claude-haiku-4-5-20251001") == "Haiku 4.5")
        #expect(ModelNames.model("claude-3-5-sonnet-20241022") == "claude-3-5-sonnet-20241022")
        #expect(ModelNames.model("some-model") == "some-model")
        #expect(ModelNames.effort("xhigh") == "Extra high")
        #expect(ModelNames.effort("medium") == "Medium")
    }

    @Test func aSessionRowCarriesTheModelAndTheIndexFindsItsFile() throws {
        let s = try Sandbox(); defer { s.cleanup() }
        let p = try s.store.defaultProfile()
        let dir = s.locator.configDir(for: p).appendingPathComponent("projects/proj")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let id = "11111111-2222-3333-4444-555555555555"
        try Data(([#"{"type":"user","cwd":"/w","entrypoint":"cli","sessionId":"\#(id)"}"#, reply("claude-opus-5-5", perTurn: "high")].joined(separator: "\n") + "\n").utf8)
            .write(to: dir.appendingPathComponent(id + ".jsonl"))
        let index = SessionIndex(locator: s.locator)
        #expect(index.sessions(for: [p]).rows.first?.setting == ModelSetting(model: "claude-opus-5-5", effort: "high"))
        #expect(index.transcriptURL(sessionId: id, profile: p)?.lastPathComponent == id + ".jsonl")
        #expect(index.transcriptURL(sessionId: "nope", profile: p) == nil)
    }
}

/// Only with CPM_REAL_SETUP set: reads your real transcripts, read-only, and prints only model
/// names, effort levels and counts.
@Suite struct RealModelHistory {
    @Test func summarise() throws {
        guard let home = ProcessInfo.processInfo.environment["CPM_REAL_SETUP"] else { return }
        let fm = FileManager.default
        var files: [(URL, Date)] = []
        for root in [home + "/.claude/projects"] + ((try? fm.contentsOfDirectory(atPath: home + "/.claude-profiles/profiles")) ?? []).map({ home + "/.claude-profiles/profiles/\($0)/projects" }) {
            for project in (try? fm.contentsOfDirectory(atPath: root)) ?? [] {
                for f in (try? fm.contentsOfDirectory(atPath: root + "/" + project)) ?? [] where f.hasSuffix(".jsonl") {
                    let url = URL(fileURLWithPath: root + "/" + project + "/" + f)
                    if let m = (try? fm.attributesOfItem(atPath: url.path))?[.modificationDate] as? Date { files.append((url, m)) }
                }
            }
        }
        var switched = 0, agree = 0, checked = 0, costed = 0, costAgree = 0, recaps = 0, recapAgree = 0, withFiles = 0, withPRs = 0, wrongRecaps = 0, wrongCosts = 0, blankRecaps = 0
        let start = Date()
        for (url, _) in files.sorted(by: { $0.1 > $1.1 }).prefix(60) {
            let spans = ModelHistory.read(url)
            if spans.count > 1 {
                switched += 1
                print("REAL", spans.map { "\(ModelNames.model($0.setting.model))/\($0.setting.effort ?? "-")x\($0.replies)" }.joined(separator: " > "))
            }
            if let last = spans.last { checked += 1; if TranscriptReader.summary(of: url)?.setting == last.setting { agree += 1 } }
            var full = ModelHistory()
            full.update(url)
            if let r = full.recap { recaps += 1; let l = TranscriptReader.summary(of: url)?.recap; if l == nil { blankRecaps += 1 } else if l != r { wrongRecaps += 1 } else { recapAgree += 1 } }
            if !full.changedFiles.isEmpty { withFiles += 1 }
            if !full.pullRequests.isEmpty { withPRs += 1 }
            if let c = full.cost {
                costed += 1
                if let l = TranscriptReader.summary(of: url)?.cost, l != c { wrongCosts += 1 } else { costAgree += 1 }
                print("REAL cost", c.byModel.map { "\(ModelNames.model($0.model)) \(SessionCost.format($0.usd))" }.joined(separator: ", "))
            }
        }
        print("REAL sessions with a cost:", costed, "list shows a wrong cost:", wrongCosts)
        print("REAL sessions with a recap:", recaps, "list shows a wrong recap:", wrongRecaps, "blank in list:", blankRecaps, "with changed files:", withFiles, "with PRs:", withPRs)
        print("REAL sessions with switches:", switched, "list matches history:", agree, "of", checked, String(format: "in %.2f s", Date().timeIntervalSince(start)))
    }
}
