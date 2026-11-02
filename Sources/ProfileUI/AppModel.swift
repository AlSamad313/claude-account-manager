import Foundation
import Observation
import ProfileCore
import SwiftUI

/// What the model calls. Tests pass fakes for the parts that touch the machine.
public struct AppEnvironment {
    public var store: ProfileStore
    public var login: LoginCoordinator
    public var launcher: Launcher
    public var index: SessionIndex
    public var stopper: SessionStopper
    public var doctor: Doctor
    public var installer: ShellInstaller
    public var copier: SetupCopier
    public var remover: ProfileRemover
    /// Where the shell files are bundled. Nil outside an app bundle.
    public var shellSource: URL?

    public init(store: ProfileStore, login: LoginCoordinator, launcher: Launcher, index: SessionIndex, stopper: SessionStopper,
                doctor: Doctor, installer: ShellInstaller, copier: SetupCopier, remover: ProfileRemover, shellSource: URL?) {
        self.store = store
        self.login = login
        self.launcher = launcher
        self.index = index
        self.stopper = stopper
        self.doctor = doctor
        self.installer = installer
        self.copier = copier
        self.remover = remover
        self.shellSource = shellSource
    }

    public static func live(locator: ConfigLocator = .current(), shellSource: URL?) -> AppEnvironment {
        let store = ProfileStore(locator: locator)
        // An app opened from the Dock does not get the shell's PATH, and needs it to find claude.
        let cli = ClaudeCLI(locator: locator, binary: .fromLoginShell(locator: locator))
        return AppEnvironment(store: store, login: LoginCoordinator(store: store, cli: cli), launcher: Launcher(store: store),
                              index: SessionIndex(locator: locator), stopper: SessionStopper(locator: locator),
                              doctor: Doctor(locator: locator), installer: ShellInstaller(store: store), copier: SetupCopier(store: store, cli: cli), remover: ProfileRemover(store: store, cli: cli), shellSource: shellSource)
    }
}

public enum Route: Equatable, Sendable {
    case main
    case add
    case waiting(profileId: String)
    case settings(profileId: String)
    case doctor
    case shell
    case stop(sessionId: String)
    case copy
    case remove(profileId: String)
}

/// Shared by the menu-bar panel and the window. It keeps only filtering and sorting; every
/// action calls ProfileCore. Slow work runs on one background queue, so the UI never waits.
@MainActor
@Observable
public final class AppModel {
    public static let panelRowLimit = 8

    public var route: Route = .main
    public var filter = ""
    public var sidebarSelection: String = "all"
    public var noticeText: String?
    public var errorText: String?
    public private(set) var profiles: [Profile] = []
    public private(set) var defaultProfileId = Profile.builtinID
    public private(set) var rows: [SessionRow] = []
    public private(set) var parseFailures = 0
    public private(set) var shellInstalled = false
    public private(set) var doctorReport: DoctorReport?
    public private(set) var doctorRunning = false
    public private(set) var installResult: InstallResult?
    public private(set) var loginState: LoginState?
    public private(set) var loginBusy = false
    public private(set) var stopBusy = false
    public var missingFolderRow: SessionRow?
    public var copyPlan: CopyPlan?
    public private(set) var copyResult: ApplyResult?
    public private(set) var restoreResult: RestoreResult?
    public private(set) var copyBusy = false
    public private(set) var backups: [BackupInfo] = []
    public private(set) var removeBusy = false

    private let env: AppEnvironment
    private let queue = DispatchQueue(label: "local.claude-profiles.model")
    private var watcher: FileWatcher?
    private var visibleCount = 0
    private var ticker: Task<Void, Never>?
    /// The newest action started from a button, so a test can wait for it.
    var lastAction: Task<Void, Never>?

    public init(env: AppEnvironment) { self.env = env }

    // MARK: Derived

    public var defaultProfile: Profile? { profiles.first { $0.id == defaultProfileId } }
    public func profile(id: String) -> Profile? { profiles.first { $0.id == id } }
    public func liveCount(profileId: String) -> Int { rows.filter { $0.profileId == profileId && $0.live }.count }
    public func row(sessionId: String) -> SessionRow? { rows.first { $0.sessionId == sessionId } }

    /// Matches title, folder and profile name.
    public var filteredRows: [SessionRow] {
        let q = filter.trimmingCharacters(in: .whitespaces).lowercased()
        return rows.filter { r in
            if sidebarSelection != "all", r.profileId != sidebarSelection { return false }
            guard !q.isEmpty else { return true }
            let name = profile(id: r.profileId)?.displayName ?? ""
            return [r.title, r.cwd ?? "", name].contains { $0.lowercased().contains(q) }
        }
    }

    /// The panel ignores the window's sidebar selection.
    public var panelRows: [SessionRow] {
        let q = filter.trimmingCharacters(in: .whitespaces).lowercased()
        let matching = rows.filter { r in
            guard !q.isEmpty else { return true }
            let name = profile(id: r.profileId)?.displayName ?? ""
            return [r.title, r.cwd ?? "", name].contains { $0.lowercased().contains(q) }
        }
        return Array(matching.prefix(Self.panelRowLimit))
    }

    // MARK: Loading

    private struct Snapshot {
        var profiles: [Profile]
        var defaultId: String
        var rows: [SessionRow]
        var failures: Int
        var installed: Bool
        var notices: [String]
    }

    private func onQueue<T>(_ body: @escaping () throws -> T) async -> Result<T, Error> {
        await withCheckedContinuation { c in
            queue.async {
                c.resume(returning: Result { try body() })
            }
        }
    }

    /// Once, when the app starts.
    public func prepareAtLaunch() async {
        let env = env
        _ = await onQueue {
            EventLog(locator: env.store.locator).rotate()
            if let source = env.shellSource { try env.installer.updateIfInstalled(from: source) }
        }
        await refresh()
    }

    /// Full load: reconcile, profiles, sessions.
    public func refresh() async {
        let env = env
        let result = await onQueue { () -> Snapshot in
            let reconcile = try Reconciler(store: env.store, login: env.login, remover: env.remover).run()
            let reg = try env.store.load()
            let sessions = env.index.sessions(for: reg.profiles)
            return Snapshot(profiles: reg.profiles, defaultId: reg.defaultProfileId, rows: sessions.rows, failures: sessions.parseFailures,
                            installed: env.installer.isInstalled(), notices: reconcile.accountNotices)
        }
        switch result {
        case .success(let s):
            profiles = s.profiles
            defaultProfileId = s.defaultId
            rows = s.rows
            parseFailures = s.failures
            shellInstalled = s.installed
            if let n = s.notices.first { noticeText = n }
        case .failure(let e):
            errorText = Self.message(e)
        }
    }

    /// Light recheck while something is visible. Writes nothing.
    public func tick() async {
        let env = env
        let current = profiles
        if case .success(let r) = await onQueue({ env.index.sessions(for: current) }) {
            rows = r.rows
            parseFailures = r.parseFailures
        }
        if case .waiting(let id) = route { await pollLogin(profileId: id) }
    }

    // MARK: Visibility

    /// One watcher for the icon colour and for changes made with cpm, ignoring profile folders.
    public func startWatching() {
        guard watcher == nil else { return }
        let locator = env.store.locator
        try? FileManager.default.createDirectory(at: locator.profilesRoot, withIntermediateDirectories: true)
        watcher = FileWatcher(paths: [locator.profilesRoot.path], ignoring: [locator.profilesDir.path, locator.logsDir.path, locator.profilesRoot.appendingPathComponent("run").path]) { [weak self] in
            Task { @MainActor in await self?.refresh() }
        }
    }

    public func viewAppeared() {
        visibleCount += 1
        Task { await refresh() }
        guard ticker == nil else { return }
        ticker = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(2))
                if Task.isCancelled { break }
                await self?.tick()
            }
        }
    }

    public func viewDisappeared() {
        visibleCount = max(0, visibleCount - 1)
        if visibleCount == 0 {
            ticker?.cancel()
            ticker = nil
        }
    }

    // MARK: Sessions

    public func newSession(profile: Profile) { launch { try $0.launcher.newSession(profile: profile) } }

    public func openVSCode(profile: Profile) { launch { try $0.launcher.openVSCode(profile: profile) } }

    public func resume(_ row: SessionRow, folderOverride: URL? = nil) {
        guard let owner = profile(id: row.profileId) else { return }
        lastAction = Task {
            let env = env
            let result = await onQueue { try env.launcher.resume(session: row, owner: owner, folderOverride: folderOverride) }
            if case .failure(let e) = result {
                if case LauncherError.folderMissing = e {
                    missingFolderRow = row
                } else {
                    errorText = Self.message(e)
                }
            }
        }
    }

    public func requestStop(_ row: SessionRow) { route = .stop(sessionId: row.sessionId) }

    /// The confirmation was already shown by the view, so this confirms straight through.
    public func confirmStop(_ row: SessionRow) async {
        stopBusy = true
        defer { stopBusy = false }
        let env = env
        let result = await onQueue { env.stopper.stop(row: row, confirm: { _ in true }) }
        route = .main
        switch result {
        case .success(.stopped), .success(.alreadyStopped), .success(.cancelled): break
        case .success(.didNotStop(let pid)): errorText = "It did not stop. Its process ID is \(pid)."
        case .failure(let e): errorText = Self.message(e)
        }
        await refresh()
    }

    // MARK: Profiles

    public func addProfile(displayName: String, commandName: String, color: String) async {
        let env = env
        let result = await onQueue { () -> Profile in
            let p = try env.store.create(displayName: displayName, commandName: commandName, color: color)
            try env.login.begin(profile: p)
            let fresh = try env.store.load().profile(id: p.id) ?? p
            try env.launcher.login(profile: fresh)
            return fresh
        }
        await refresh()
        switch result {
        case .success(let p):
            errorText = nil
            loginState = .waiting
            route = .waiting(profileId: p.id)
        case .failure(let e):
            errorText = Self.message(e)
        }
    }

    public func pollLogin(profileId: String) async {
        guard let p = profile(id: profileId) else { return }
        let env = env
        if case .success(let state) = await onQueue({ try env.login.poll(profile: p) }) { loginState = state }
    }

    public func keepLogin(profileId: String) async {
        guard let p = profile(id: profileId) else { return }
        loginBusy = true
        defer { loginBusy = false }
        let env = env
        let result = await onQueue { try env.login.keep(profile: p) }
        switch result {
        case .success(let k):
            noticeText = k.keychainWarning
            route = .main
            loginState = nil
        case .failure(let e):
            errorText = Self.message(e)
        }
        await refresh()
    }

    public func retryLogin(profileId: String) async {
        guard let p = profile(id: profileId) else { return }
        let env = env
        let result = await onQueue {
            try env.login.retry(profile: p)
            let fresh = try env.store.load().profile(id: p.id) ?? p
            try env.launcher.login(profile: fresh)
        }
        if case .failure(let e) = result { errorText = Self.message(e) }
        loginState = .waiting
        await refresh()
    }

    public func discardLogin(profileId: String) async {
        guard let p = profile(id: profileId) else { return }
        let env = env
        let result = await onQueue { try env.login.discard(profile: p) }
        if case .failure(let e) = result { errorText = Self.message(e) }
        route = .main
        loginState = nil
        await refresh()
    }

    /// For a profile that has been signed out: opens Terminal to sign in again.
    public func signIn(profile: Profile) { launch { try $0.launcher.login(profile: profile) } }

    public func rename(profile: Profile, to name: String) { change { try $0.store.rename(id: profile.id, displayName: name) } }
    public func setColor(profile: Profile, color: String) { change { try $0.store.setColor(id: profile.id, color: color) } }
    public func setDefault(profile: Profile) { change { try $0.store.setDefault(id: profile.id) } }

    // MARK: Copy setup

    public func preparePreview(sourceId: String, targetId: String) async {
        guard let source = profile(id: sourceId), let target = profile(id: targetId) else { return }
        let env = env
        copyResult = nil
        restoreResult = nil
        let result = await onQueue { () -> (CopyPlan, [BackupInfo]) in
            (try env.copier.plan(source: source, target: target), env.copier.backups.list(profileId: target.id))
        }
        switch result {
        case .success(let (plan, list)):
            copyPlan = plan
            backups = list
            errorText = nil
        case .failure(let e):
            copyPlan = nil
            errorText = Self.message(e)
        }
    }

    public func applyCopy() async {
        guard let plan = copyPlan else { return }
        copyBusy = true
        defer { copyBusy = false }
        let env = env
        let result = await onQueue { try env.copier.apply(plan) }
        switch result {
        case .success(let r):
            copyResult = r
            errorText = nil
        case .failure(let e):
            errorText = Self.message(e)
        }
        await preparePreview(sourceId: plan.sourceId, targetId: plan.targetId, keepingResult: true)
    }

    private func preparePreview(sourceId: String, targetId: String, keepingResult: Bool) async {
        let kept = copyResult
        await preparePreview(sourceId: sourceId, targetId: targetId)
        copyResult = kept
    }

    public func restore(backupId: String, targetId: String) async {
        guard let target = profile(id: targetId) else { return }
        let env = env
        let result = await onQueue { try env.copier.restore(profile: target, backupId: backupId) }
        switch result {
        case .success(let r):
            restoreResult = r
            errorText = nil
        case .failure(let e):
            errorText = Self.message(e)
        }
    }

    // MARK: Removing

    public func hasLiveSession(profileId: String) -> Bool { liveCount(profileId: profileId) > 0 }

    public func removeProfile(profileId: String) async {
        guard let p = profile(id: profileId) else { return }
        removeBusy = true
        defer { removeBusy = false }
        let env = env
        let result = await onQueue { try env.remover.remove(profile: p) }
        switch result {
        case .success(let r):
            noticeText = r.warnings.first
            errorText = nil
            route = .main
            if sidebarSelection == profileId { sidebarSelection = "all" }
        case .failure(let e):
            errorText = Self.message(e)
        }
        await refresh()
    }

    // MARK: Doctor and shell

    public func runDoctor() async {
        doctorRunning = true
        defer { doctorRunning = false }
        let env = env
        if case .success(let r) = await onQueue({ env.doctor.run() }) { doctorReport = r }
    }

    public func fixAndRerunDoctor() async {
        await refresh()
        await runDoctor()
    }

    public func setUpShell() async {
        guard let source = env.shellSource else {
            errorText = "The shell files are not bundled with this build. Run cpm install-shell instead."
            return
        }
        let env = env
        let result = await onQueue { try env.installer.install(from: source) }
        switch result {
        case .success(let r): installResult = r
        case .failure(let e): errorText = Self.message(e)
        }
        await refresh()
    }

    // MARK: Helpers

    private func launch(_ body: @escaping (AppEnvironment) throws -> Void) {
        let env = env
        lastAction = Task {
            if case .failure(let e) = await onQueue({ try body(env) }) {
                errorText = Self.message(e)
            } else {
                errorText = nil
            }
            await tick()
        }
    }

    private func change(_ body: @escaping (AppEnvironment) throws -> Void) {
        let env = env
        lastAction = Task {
            if case .failure(let e) = await onQueue({ try body(env) }) { errorText = Self.message(e) }
            await refresh()
        }
    }

    static func message(_ e: Error) -> String {
        if let e = e as? ProfileError { return e.description }
        if let e = e as? LauncherError { return e.description }
        return e.localizedDescription
    }
}

extension Color {
    /// From "#RRGGBB". Validated names only reach here, so a bad value falls back to grey.
    public init(hex: String) {
        let digits = hex.dropFirst()
        guard hex.hasPrefix("#"), digits.count == 6, let v = UInt32(digits, radix: 16) else {
            self = .gray
            return
        }
        self = Color(red: Double((v >> 16) & 0xFF) / 255, green: Double((v >> 8) & 0xFF) / 255, blue: Double(v & 0xFF) / 255)
    }
}
