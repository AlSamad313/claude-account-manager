import Foundation

public struct TranscriptSummary: Equatable, Sendable {
    public var sessionId: String
    public var cwd: String?
    public var title: String?
    public var entrypoint: String?
    public var lastActivity: Date
    /// The model and effort of the newest reply that was read.
    public var setting: ModelSetting? = nil
    public var cost: SessionCost? = nil
    public var recap: String? = nil
}

/// Reader for projects/*/*.jsonl. Reads the ends of a file, because transcripts can be tens of MB.
/// Never uses a user message as a title, because it could show pasted secrets in the menu.
public enum TranscriptReader {
    static let window = 64 * 1024
    static let maxWindow = 1024 * 1024

    public static func summary(of url: URL) -> TranscriptSummary? {
        var st = stat()
        guard stat(url.path, &st) == 0 else { return nil }
        return summary(of: url, size: Int(st.st_size), modified: Date(timeIntervalSince1970: TimeInterval(st.st_mtimespec.tv_sec)))
    }

    static func summary(of url: URL, size: Int, modified: Date) -> TranscriptSummary? {
        guard let h = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? h.close() }

        var fields = read(handle: h, size: size, window: window)
        if fields.title == nil || fields.setting == nil, size > 2 * window {
            fields = read(handle: h, size: size, window: maxWindow)
        }
        if fields.lines == 0 { return nil }
        return TranscriptSummary(sessionId: url.deletingPathExtension().lastPathComponent, cwd: fields.cwd,
                                 title: fields.title, entrypoint: fields.entrypoint, lastActivity: modified, setting: fields.setting, cost: fields.cost, recap: fields.recap)
    }

    private struct Fields {
        var lines = 0
        var cwd: String?, entrypoint: String?, custom: String?, ai: String?, summary: String?
        var setting: ModelSetting?
        var cost: SessionCost?
        var recap: String?
        var title: String? { custom ?? ai ?? summary }
    }

    private static func read(handle h: FileHandle, size: Int, window: Int) -> Fields {
        var f = Fields()
        // Later lines overwrite earlier ones, so the newest title wins.
        let parts = ranges(size: size, window: window)
        for (i, (offset, length, partialFirst)) in parts.enumerated() {
            // A recap or cost is kept only from the end of the file. An older one from the start
            // could be out of date, and the pane reads the whole file anyway.
            let atEnd = i == parts.count - 1
            guard (try? h.seek(toOffset: UInt64(offset))) != nil,
                  let data = try? h.read(upToCount: length) else { continue }
            var parts = data.split(separator: UInt8(ascii: "\n"), omittingEmptySubsequences: true)
            if partialFirst, !parts.isEmpty { parts.removeFirst() }
            if offset + length < size, !parts.isEmpty { parts.removeLast() }
            for line in parts {
                // Parsing every line is slow. Once cwd and entrypoint are known, only title and reply lines matter.
                let mayMatter = f.cwd == nil || f.entrypoint == nil || contains(line, "-title\"") || contains(line, "\"summary\"")
                    || (contains(line, "\"assistant\"") && contains(line, "\"model\"")) || contains(line, "\"cost-state\"") || contains(line, "\"away_summary\"")
                guard mayMatter else {
                    f.lines += 1
                    continue
                }
                guard let obj = (try? JSONSerialization.jsonObject(with: Data(line))) as? [String: Any] else { continue }
                f.lines += 1
                if let v = obj["cwd"] as? String, f.cwd == nil { f.cwd = v }
                if let v = obj["entrypoint"] as? String, f.entrypoint == nil { f.entrypoint = v }
                if let s = ModelSetting.from(obj) { f.setting = s }
                if atEnd, let c = SessionCost.from(obj) { f.cost = c }
                if atEnd, let r = Recap.from(obj) { f.recap = r }
                switch obj["type"] as? String {
                case "custom-title": if let v = obj["customTitle"] as? String { f.custom = v }
                case "ai-title": if let v = obj["aiTitle"] as? String { f.ai = v }
                case "summary": if let v = obj["summary"] as? String { f.summary = v }
                default: break
                }
            }
        }
        return f
    }

    static func contains(_ bytes: Data.SubSequence, _ mark: StaticString) -> Bool {
        bytes.withUnsafeBytes { memmem($0.baseAddress, $0.count, mark.utf8Start, mark.utf8CodeUnitCount) != nil }
    }

    private static func ranges(size: Int, window: Int) -> [(Int, Int, Bool)] {
        if size <= 2 * window { return [(0, size, false)] }
        return [(0, window, false), (size - window, window, true)]
    }
}
