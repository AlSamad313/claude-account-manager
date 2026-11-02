import Foundation

public struct TranscriptSummary: Equatable, Sendable {
    public var sessionId: String
    public var cwd: String?
    public var title: String?
    public var entrypoint: String?
    public var lastActivity: Date
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
        if fields.title == nil, size > 2 * window {
            fields = read(handle: h, size: size, window: maxWindow)
        }
        if fields.lines == 0 { return nil }
        return TranscriptSummary(sessionId: url.deletingPathExtension().lastPathComponent, cwd: fields.cwd,
                                 title: fields.title, entrypoint: fields.entrypoint, lastActivity: modified)
    }

    private struct Fields {
        var lines = 0
        var cwd: String?, entrypoint: String?, custom: String?, ai: String?, summary: String?
        var title: String? { custom ?? ai ?? summary }
    }

    private static func read(handle h: FileHandle, size: Int, window: Int) -> Fields {
        var f = Fields()
        // Later lines overwrite earlier ones, so the newest title wins.
        for (offset, length, partialFirst) in ranges(size: size, window: window) {
            guard (try? h.seek(toOffset: UInt64(offset))) != nil,
                  let data = try? h.read(upToCount: length) else { continue }
            var parts = data.split(separator: UInt8(ascii: "\n"), omittingEmptySubsequences: true)
            if partialFirst, !parts.isEmpty { parts.removeFirst() }
            if offset + length < size, !parts.isEmpty { parts.removeLast() }
            for line in parts {
                // Parsing every line is slow. Once cwd and entrypoint are known, only title lines matter.
                let mayMatter = f.cwd == nil || f.entrypoint == nil || contains(line, "-title\"") || contains(line, "\"summary\"")
                guard mayMatter else {
                    f.lines += 1
                    continue
                }
                guard let obj = (try? JSONSerialization.jsonObject(with: Data(line))) as? [String: Any] else { continue }
                f.lines += 1
                if let v = obj["cwd"] as? String, f.cwd == nil { f.cwd = v }
                if let v = obj["entrypoint"] as? String, f.entrypoint == nil { f.entrypoint = v }
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

    private static func contains(_ bytes: Data.SubSequence, _ mark: StaticString) -> Bool {
        bytes.withUnsafeBytes { memmem($0.baseAddress, $0.count, mark.utf8Start, mark.utf8CodeUnitCount) != nil }
    }

    private static func ranges(size: Int, window: Int) -> [(Int, Int, Bool)] {
        if size <= 2 * window { return [(0, size, false)] }
        return [(0, window, false), (size - window, window, true)]
    }
}
