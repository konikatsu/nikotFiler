using System.Text.Json;

namespace NikotFiler.Core;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public double TextScale { get; set; } = 100;
    public bool TwoPanes { get; set; } = true;
    public bool ShowPaneOutlines { get; set; } = true;
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 820;
    public bool Maximized { get; set; }
    public static double ValidScale(double value) => double.IsFinite(value) ? Math.Clamp(value, 80, 250) : 100;
}

public sealed class PaneSession
{
    public List<string> Paths { get; set; } = [];
    public int SelectedIndex { get; set; }
}

public sealed class AppSession
{
    public int Version { get; set; } = 1;
    public PaneSession Left { get; set; } = new();
    public PaneSession Right { get; set; } = new();
    public int ActivePane { get; set; }
    public List<string> Bookmarks { get; set; } = [];
}

public static class StateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static T Load<T>(string path) where T : new()
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (File.Exists(candidate) && JsonSerializer.Deserialize<T>(File.ReadAllText(candidate), Options) is { } value)
                    return value;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return new T();
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }
}

public sealed record FileEntry(string FullPath, string Name, bool IsDirectory, long? Size, DateTime Modified)
{
    public string Glyph => IsDirectory ? "\uE8B7" : "\uE8A5";
    public string Kind => IsDirectory ? "フォルダー" : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext + " ファイル" : "ファイル";
    public string SizeText => Size is not { } bytes ? "—" : bytes < 1024 ? $"{bytes:N0} B" : bytes < 1048576 ? $"{bytes / 1024d:N1} KB" : bytes < 1073741824 ? $"{bytes / 1048576d:N1} MB" : $"{bytes / 1073741824d:N1} GB";
    public string ModifiedText => Modified.ToString("yyyy/MM/dd HH:mm");

    public static List<FileEntry> ReadDirectory(string path, CancellationToken cancellationToken)
    {
        var result = new List<FileEntry>();
        foreach (var item in Directory.EnumerateFileSystemEntries(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(item);
                if ((info.Attributes & FileAttributes.Hidden) != 0 || (info.Attributes & FileAttributes.System) != 0) continue;
                var directory = (info.Attributes & FileAttributes.Directory) != 0;
                result.Add(new FileEntry(item, info.Name, directory, directory ? null : info.Length, info.LastWriteTime));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
