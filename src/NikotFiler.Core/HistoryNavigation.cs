namespace NikotFiler.Core;

public static class HistoryNavigation
{
    public static string GetFolder(HistoryEntry entry)
    {
        var source = Path.GetFullPath(entry.SourcePath);
        if (entry.Kind == HistoryKind.FolderVisit) return source;
        if (entry.Kind is HistoryKind.FileOpen or HistoryKind.Delete) return Parent(source);
        if (entry.Kind == HistoryKind.CreateFolder)
            return entry.Status == HistoryStatus.Succeeded && entry.DestinationPath is { Length: > 0 } created
                ? Path.GetFullPath(created) : source;

        if (entry.Status == HistoryStatus.Succeeded && entry.DestinationPath is { Length: > 0 } destination)
        {
            destination = Path.GetFullPath(destination);
            if (Directory.Exists(destination)) return destination;
            if (File.Exists(destination)) return Parent(destination);
            throw new FileNotFoundException("操作先のファイルまたはフォルダーが見つかりません。", destination);
        }
        return Directory.Exists(source) ? source : Parent(source);
    }

    private static string Parent(string path) => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))
        ?? throw new DirectoryNotFoundException("履歴の親フォルダーが見つかりません。");
}
