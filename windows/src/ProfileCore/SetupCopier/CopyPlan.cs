namespace ProfileCore;

public enum CopyKind { McpServer, Permissions, Hook, Model, Env, Plugin, Skill, Agent, Command, ClaudeMd }

public enum CopyAction { Add, Change, Same }

/// What to do when the target already has a different file.
public enum CopyConflict { Skip, Overwrite, Rename }

public sealed class SecretField(string name, bool isArgument = false)
{
    public string Name { get; } = name;
    /// A positional argument or a URL cannot be dropped, only ticked or left with the server.
    public bool IsArgument { get; } = isArgument;
    public bool Ticked { get; set; }
}

/// One row of the preview. The view changes only Ticked, Conflict and the secrets' Ticked.
public sealed class CopyItem(string id, CopyKind kind, string key, CopyAction action)
{
    public string Id { get; } = id;
    public CopyKind Kind { get; } = kind;
    public string Key { get; } = key;
    public CopyAction Action { get; set; } = action;
    /// A short description for the preview. It never holds a secret value.
    public string Detail { get; set; } = "";
    public List<SecretField> Secrets { get; } = [];
    public bool FlaggedFile { get; set; }
    public bool PointsIntoSource { get; set; }
    public bool NeedsReLogin { get; set; }
    public CopyConflict Conflict { get; set; } = CopyConflict.Skip;
    public string? RemovalCommand { get; set; }
    public bool Ticked { get; set; }

    public bool IsFile => Kind is CopyKind.Skill or CopyKind.Agent or CopyKind.Command or CopyKind.ClaudeMd;
    public bool CanBeTicked => Action != CopyAction.Same;
}

public sealed class CopyPlan(string sourceId, string targetId, List<CopyItem> items, string? blockedReason, Dictionary<string, string?> beforeHashes)
{
    public string SourceId { get; } = sourceId;
    public string TargetId { get; set; } = targetId;
    public List<CopyItem> Items { get; } = items;
    /// Why nothing can be written right now, if so.
    public string? BlockedReason { get; } = blockedReason;
    /// Hash of each file Apply may touch, as the preview saw it. Null for a file that did not exist.
    public Dictionary<string, string?> BeforeHashes { get; } = beforeHashes;

    public int TickedCount => Items.Count(i => i.Ticked);
}

public sealed record CopyFailure(string ItemId, string Reason);

public sealed record ApplyResult(List<string> Done, List<CopyFailure> Failed, string? BackupId, List<string> RemovalCommands);

/// LeftAlone: changed since the copy, so left alone rather than discarding later work.
public sealed record RestoreResult(List<string> Restored, List<string> Removed, List<string> LeftAlone, List<string> RemovalCommands);

public sealed record BackupInfo(string Id, string ProfileId, DateTime CreatedAt, List<string> RemovalCommands);
