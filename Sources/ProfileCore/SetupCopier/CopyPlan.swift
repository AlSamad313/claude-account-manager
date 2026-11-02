import Foundation

public enum CopyKind: String, CaseIterable, Sendable {
    case mcpServer, permissions, hook, model, env, plugin, skill, agent, command, claudeMd
}

public enum CopyAction: String, Sendable { case add, change, same }

/// What to do when the target already has a different file.
public enum CopyConflict: String, CaseIterable, Sendable { case skip, overwrite, rename }

public struct SecretField: Equatable, Sendable {
    public var name: String
    public var isArgument: Bool  // a positional arg cannot be dropped, only ticked or left with the server
    public var ticked = false

    public init(name: String, isArgument: Bool = false, ticked: Bool = false) {
        self.name = name
        self.isArgument = isArgument
        self.ticked = ticked
    }
}

public struct CopyItem: Identifiable, Equatable, Sendable {
    public var id: String
    public var kind: CopyKind
    public var key: String
    public var action: CopyAction
    /// A short description for the preview. It never holds a secret value.
    public var detail: String = ""
    public var secrets: [SecretField] = []
    public var flaggedFile = false
    public var pointsIntoSource = false
    public var needsReLogin = false
    public var conflict: CopyConflict = .skip
    public var isFile: Bool { [.skill, .agent, .command, .claudeMd].contains(kind) }
    public var removalCommand: String?
    public var ticked = false

    public var canBeTicked: Bool { action != .same }
}

public struct CopyPlan: Equatable, Sendable {
    public var sourceId: String
    public var targetId: String
    public var items: [CopyItem]
    /// Why nothing can be written right now, if so.
    public var blockedReason: String?
    /// Hash of each file `apply` may touch, as the preview saw it. Nil for a file that did not exist.
    public var beforeHashes: [String: String?]

    public var tickedCount: Int { items.filter(\.ticked).count }
}

public struct CopyFailure: Equatable, Sendable {
    public var itemId: String
    public var reason: String
}

public struct ApplyResult: Equatable, Sendable {
    public var done: [String]
    public var failed: [CopyFailure]
    public var backupId: String?
    /// Ways to undo what Restore cannot, one per item Claude Code's own commands installed.
    public var removalCommands: [String]
}

public struct RestoreResult: Equatable, Sendable {
    public var restored: [String]
    public var removed: [String]
    /// Changed since the copy, so left alone rather than discarding later work.
    public var leftAlone: [String]
    public var removalCommands: [String]
}

public struct BackupInfo: Identifiable, Equatable, Sendable {
    public var id: String
    public var profileId: String
    public var createdAt: Date
    public var removalCommands: [String]
}
