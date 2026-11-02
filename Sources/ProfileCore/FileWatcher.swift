import CoreServices
import Foundation

/// Watches folders, not single files, because Claude Code and AtomicFile replace files by rename.
public final class FileWatcher {
    private var stream: FSEventStreamRef?
    private let queue = DispatchQueue(label: "local.claude-profiles.filewatcher")
    private let ignoring: [String]
    private let onChange: () -> Void

    /// Groups events for 200 ms. Events under an `ignoring` path do not count.
    public init?(paths: [String], ignoring: [String] = [], onChange: @escaping () -> Void) {
        self.ignoring = ignoring.map { $0.hasSuffix("/") ? $0 : $0 + "/" }
        self.onChange = onChange
        var context = FSEventStreamContext(version: 0, info: Unmanaged.passUnretained(self).toOpaque(), retain: nil, release: nil, copyDescription: nil)
        let callback: FSEventStreamCallback = { _, info, count, rawPaths, _, _ in
            guard let info else { return }
            let me = Unmanaged<FileWatcher>.fromOpaque(info).takeUnretainedValue()
            let paths = Unmanaged<CFArray>.fromOpaque(rawPaths).takeUnretainedValue() as? [String] ?? []
            me.handle(paths)
        }
        guard let s = FSEventStreamCreate(nil, callback, &context, paths as CFArray, FSEventStreamEventId(kFSEventStreamEventIdSinceNow), 0.2,
                                          FSEventStreamCreateFlags(kFSEventStreamCreateFlagFileEvents | kFSEventStreamCreateFlagUseCFTypes)) else { return nil }
        stream = s
        FSEventStreamSetDispatchQueue(s, queue)
        FSEventStreamStart(s)
    }

    private func handle(_ paths: [String]) {
        let relevant = paths.contains { path in !ignoring.contains { path.hasPrefix($0) } }
        if relevant { onChange() }
    }

    public func stop() {
        guard let s = stream else { return }
        FSEventStreamStop(s)
        FSEventStreamInvalidate(s)
        FSEventStreamRelease(s)
        stream = nil
    }

    deinit { stop() }
}
