import Foundation

public struct ProcessResult: Equatable, Sendable {
    public var stdout: String
    public var stderr: String
    public var exitCode: Int32
    public var timedOut: Bool

    public init(stdout: String = "", stderr: String = "", exitCode: Int32 = 0, timedOut: Bool = false) {
        self.stdout = stdout
        self.stderr = stderr
        self.exitCode = exitCode
        self.timedOut = timedOut
    }
}

public protocol ProcessRunning {
    func run(executable: String, args: [String], env: LaunchEnv?, timeout: TimeInterval) -> ProcessResult
}

/// The one Swift API for running external commands.
public struct ProcessRunner: ProcessRunning {
    public init() {}

    public func run(executable: String, args: [String], env: LaunchEnv?, timeout: TimeInterval) -> ProcessResult {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: executable)
        p.arguments = args
        var e = ProcessInfo.processInfo.environment
        for k in env?.unset ?? [] { e.removeValue(forKey: k) }
        for (k, v) in env?.set ?? [:] { e[k] = v }
        p.environment = e
        let outPipe = Pipe(), errPipe = Pipe()
        p.standardOutput = outPipe
        p.standardError = errPipe
        p.standardInput = FileHandle.nullDevice

        let box = OutputBox()
        let group = DispatchGroup()
        group.enter()
        DispatchQueue.global().async {
            box.setOut(outPipe.fileHandleForReading.readDataToEndOfFile())
            group.leave()
        }
        group.enter()
        DispatchQueue.global().async {
            box.setErr(errPipe.fileHandleForReading.readDataToEndOfFile())
            group.leave()
        }
        do {
            try p.run()
        } catch {
            try? outPipe.fileHandleForWriting.close()
            try? errPipe.fileHandleForWriting.close()
            group.wait()
            return ProcessResult(stderr: "Could not run \(executable): \(error.localizedDescription)", exitCode: 127)
        }
        try? outPipe.fileHandleForWriting.close()
        try? errPipe.fileHandleForWriting.close()

        var timedOut = false
        let deadline = Date().addingTimeInterval(timeout)
        while p.isRunning {
            if Date() > deadline {
                timedOut = true
                p.terminate()
                break
            }
            Thread.sleep(forTimeInterval: 0.02)
        }
        p.waitUntilExit()
        // A child that keeps the pipes open must not hang the caller.
        _ = group.wait(timeout: .now() + 1)
        let (out, err) = box.values()
        return ProcessResult(stdout: String(decoding: out, as: UTF8.self), stderr: String(decoding: err, as: UTF8.self),
                             exitCode: p.terminationStatus, timedOut: timedOut)
    }
}

private final class OutputBox: @unchecked Sendable {
    private let lock = NSLock()
    private var out = Data(), err = Data()
    func setOut(_ d: Data) { lock.lock(); out = d; lock.unlock() }
    func setErr(_ d: Data) { lock.lock(); err = d; lock.unlock() }
    func values() -> (Data, Data) { lock.lock(); defer { lock.unlock() }; return (out, err) }
}
