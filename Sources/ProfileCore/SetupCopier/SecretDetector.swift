import Foundation

/// Fixed value patterns. Every value in env and headers is masked regardless; these are for
/// positional args and free text, where there is no key to go by.
public enum SecretDetector {
    public static let mask = "••••••"

    private static let patterns: [NSRegularExpression] = [
        #"\bsk-[A-Za-z0-9_-]{20,}"#,
        #"ghp_[A-Za-z0-9]{20,}"#,
        #"xox[bap]-[A-Za-z0-9-]+"#,
        #"\b[0-9a-fA-F]{40,}\b"#,
    ].map { try! NSRegularExpression(pattern: $0) }

    /// No slash, because long file paths would match. Needs upper, lower and a digit, because long words and names do not.
    private static let base64 = try! NSRegularExpression(pattern: #"[A-Za-z0-9+]{40,}={0,2}"#)

    public static func looksSecret(_ s: String) -> Bool {
        let range = NSRange(s.startIndex..., in: s)
        if patterns.contains(where: { $0.firstMatch(in: s, range: range) != nil }) { return true }
        return base64.matches(in: s, range: range).contains { m in
            guard let r = Range(m.range, in: s) else { return false }
            let v = s[r]
            return v.contains(where: \.isUppercase) && v.contains(where: \.isLowercase) && v.contains(where: \.isNumber)
        }
    }

    /// For a file of free text: a match flags the whole file, with no masking inside it.
    /// Files over 1 MB, and files that are not text, are not scanned.
    public static func fileLooksSecret(_ url: URL) -> Bool {
        guard let attrs = try? FileManager.default.attributesOfItem(atPath: url.path),
              (attrs[.size] as? Int ?? 0) <= 1_000_000,
              let data = try? Data(contentsOf: url),
              let text = String(data: data, encoding: .utf8) else { return false }
        return looksSecret(text)
    }
}
