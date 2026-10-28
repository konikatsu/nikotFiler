using Microsoft.VisualBasic.FileIO;

namespace NikotFiler.Core;

public sealed class FileActions(HistoryStore history)
{
    public void Copy(string source, string destinationDirectory, CancellationToken cancellationToken = default, IProgress<string>? progress = null)
    {
        source = ExistingItem(source);
        destinationDirectory = ExistingDirectory(destinationDirectory);
        var destination = TargetPath(source, destinationDirectory);
        EnsureTargetAvailable(source, destination);
        history.ExecuteLogged(HistoryKind.Copy, source, destination, () =>
        {
            if (Directory.Exists(source)) CopyDirectory(source, destination, cancellationToken, progress);
            else CopyFile(source, destination, cancellationToken, progress);
        });
    }

    public void Move(string source, string destinationDirectory)
    {
        source = ExistingItem(source);
        destinationDirectory = ExistingDirectory(destinationDirectory);
        var destination = TargetPath(source, destinationDirectory);
        EnsureTargetAvailable(source, destination);
        history.ExecuteLogged(HistoryKind.Move, source, destination, () =>
        {
            if (Directory.Exists(source)) Directory.Move(source, destination);
            else File.Move(source, destination);
        });
    }

    public void Rename(string source, string newName)
    {
        source = ExistingItem(source);
        if (string.IsNullOrWhiteSpace(newName) || newName is "." or ".." ||
            newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            newName.Contains('/') || newName.Contains('\\'))
            throw new ArgumentException("ファイル名に使用できない文字があります。", nameof(newName));
        var destination = Path.Combine(Path.GetDirectoryName(source)!, newName);
        EnsureTargetAvailable(source, destination);
        history.ExecuteLogged(HistoryKind.Rename, source, destination, () =>
        {
            if (Directory.Exists(source)) Directory.Move(source, destination);
            else File.Move(source, destination);
        });
    }

    public void DeleteToRecycleBin(string source)
    {
        source = ExistingItem(source);
        history.ExecuteLogged(HistoryKind.Delete, source, null, () =>
        {
            if (Directory.Exists(source))
                FileSystem.DeleteDirectory(source, UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
            else
                FileSystem.DeleteFile(source, UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        });
    }

    public void CreateFolder(string parentDirectory, string name)
    {
        parentDirectory = ExistingDirectory(parentDirectory);
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/') || name.Contains('\\'))
            throw new ArgumentException("フォルダ名に使用できない文字があります。", nameof(name));
        var destination = Path.Combine(parentDirectory, name);
        if (Path.Exists(destination)) throw new IOException("同名のフォルダまたはファイルがあります。");
        history.ExecuteLogged(HistoryKind.CreateFolder, parentDirectory, destination,
            () => Directory.CreateDirectory(destination));
    }

    private static string ExistingItem(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Path.Exists(fullPath)) throw new FileNotFoundException("ファイルまたはフォルダが見つかりません。", fullPath);
        return fullPath;
    }

    private static string ExistingDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException(fullPath);
        return fullPath;
    }

    private static string TargetPath(string source, string destinationDirectory) =>
        Path.Combine(destinationDirectory, Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));

    private static void EnsureTargetAvailable(string source, string destination)
    {
        var fullDestination = Path.GetFullPath(destination);
        if (Path.Exists(fullDestination)) throw new IOException("移動先に同名の項目があります。上書きはしません。");
        if (Directory.Exists(source) &&
            fullDestination.StartsWith(Path.TrimEndingDirectorySeparator(source) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new IOException("フォルダを自分自身の中へコピー・移動できません。");
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken, IProgress<string>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Reject junctions and symbolic links to avoid following an unexpected tree.
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("リンクされたフォルダのコピーには対応していません。");
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFileSystemEntries(source))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("リンクを含むフォルダのコピーには対応していません。");
            var target = Path.Combine(destination, Path.GetFileName(path));
            if (Directory.Exists(path)) CopyDirectory(path, target, cancellationToken, progress);
            else CopyFile(path, target, cancellationToken, progress);
        }
    }

    private static void CopyFile(string source, string destination, CancellationToken cancellationToken, IProgress<string>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("リンクされたファイルのコピーには対応していません。");
        progress?.Report(Path.GetFileName(source));
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1048576);
        var buffer = new byte[1048576];
        int count;
        while ((count = input.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(buffer, 0, count);
        }
        output.Flush(true);
        output.Dispose();
        File.SetLastWriteTime(destination, File.GetLastWriteTime(source));
    }
}
