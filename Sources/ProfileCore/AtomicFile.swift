import Foundation

public enum AtomicFile {
    /// Writes to a temporary file in the same folder, then renames it over the target,
    /// so a reader sees the old file or the new one, never half of one.
    public static func write(_ data: Data, to url: URL, mode: mode_t) throws {
        let dir = url.deletingLastPathComponent()
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let tmp = dir.appendingPathComponent(".\(url.lastPathComponent).\(UUID().uuidString).tmp")
        let fd = open(tmp.path, O_WRONLY | O_CREAT | O_EXCL, mode)
        guard fd >= 0 else { throw ProfileError.failed("Cannot write \(tmp.path)") }
        var ok = false
        defer {
            close(fd)
            if !ok { unlink(tmp.path) }
        }
        let wrote = data.withUnsafeBytes { Darwin.write(fd, $0.baseAddress, $0.count) }
        guard wrote == data.count, fsync(fd) == 0, fchmod(fd, mode) == 0 else {
            throw ProfileError.failed("Cannot write \(tmp.path)")
        }
        guard rename(tmp.path, url.path) == 0 else {
            throw ProfileError.failed("Cannot replace \(url.path)")
        }
        ok = true
    }
}
