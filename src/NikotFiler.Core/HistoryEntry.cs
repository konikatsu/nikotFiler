namespace NikotFiler.Core;

public enum HistoryKind
{
    FolderVisit,
    FileOpen,
    Copy,
    Move,
    Rename,
    Delete,
    CreateFolder
}

public enum HistoryStatus
{
    Viewed,
    Started,
    Succeeded,
    Failed
}

public sealed record HistoryEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    HistoryKind Kind,
    HistoryStatus Status,
    string SourcePath,
    string? DestinationPath = null,
    string? Error = null);
