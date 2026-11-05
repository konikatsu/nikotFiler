using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace NikotFiler.App;

internal sealed class DefaultFileManager(RegistryKey classes, string backupPath)
{
    public bool HasBackup => File.Exists(backupPath);

    public bool IsEnabled => HasBackup && ReadBackup().Changes.All(change =>
    {
        using var key = classes.OpenSubKey(change.Path);
        return IsApplied(key, change);
    });

    public static string InstallApplication(string sourceDirectory, string dataDirectory)
    {
        var files = new[] { "nikotFiler.exe", "nikotFiler.dll", "NikotFiler.Core.dll", "nikotFiler.deps.json", "nikotFiler.runtimeconfig.json" };
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files) hash.AppendData(File.ReadAllBytes(System.IO.Path.Combine(sourceDirectory, file)));
        var directory = System.IO.Path.Combine(dataDirectory, "integration", "app", Convert.ToHexString(hash.GetHashAndReset()));
        var staging = directory + "." + Guid.NewGuid().ToString("N");
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(staging);
            foreach (var file in files) File.Copy(System.IO.Path.Combine(sourceDirectory, file), System.IO.Path.Combine(staging, file));
            Directory.Move(staging, directory);
        }
        return System.IO.Path.Combine(directory, "nikotFiler.exe");
    }

    public void Enable(string executable)
    {
        executable = System.IO.Path.GetFullPath(executable);
        if (!File.Exists(executable)) throw new FileNotFoundException("登録する EXE が見つかりません。", executable);
        using var guard = Lock();
        if (HasBackup) throw new IOException("前の登録情報が残っています。先に「元の設定に戻す」を選んでください。");
        // Append a dot so a drive root's trailing backslash cannot escape its closing quote.
        var command = $"\"{executable}\" --shell-folder \"%1\\.\"";
        var backup = new RegistrationBackup();
        foreach (var type in new[] { "Directory", "Drive" })
        {
            Capture(backup, $@"{type}\shell", "", "nikotFiler");
            Capture(backup, $@"{type}\shell\nikotFiler", "", "nikotFiler で開く");
            Capture(backup, $@"{type}\shell\nikotFiler\command", "", command);
            Capture(backup, $@"{type}\shell\open\command", "", command);
            Capture(backup, $@"{type}\shell\open\command", "DelegateExecute", "");
        }
        // Electron opens directories with the explicit Folder/explore verb.
        Capture(backup, @"Folder\shell\explore\command", "", command);
        Capture(backup, @"Folder\shell\explore\command", "DelegateExecute", "");
        var temporary = backupPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, backup, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(true);
            }
            File.Move(temporary, backupPath);
        }
        finally { File.Delete(temporary); }
        try
        {
            foreach (var change in backup.Changes)
            {
                using var key = classes.CreateSubKey(change.Path);
                key.SetValue(change.Name, change.Applied, RegistryValueKind.String);
            }
            NotifyShell();
        }
        catch
        {
            RestoreCore(backup);
            throw;
        }
    }

    public int Restore()
    {
        using var guard = Lock();
        if (!HasBackup) return 0;
        return RestoreCore(ReadBackup());
    }

    private int RestoreCore(RegistrationBackup backup)
    {
        var preserved = 0;
        var modifiedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in backup.Changes)
        {
            using var key = classes.OpenSubKey(change.Path);
            if (!IsApplied(key, change) && !MatchesOriginal(key, change)) modifiedKeys.Add(change.Path);
        }
        foreach (var change in backup.Changes.AsEnumerable().Reverse())
        {
            using var key = classes.OpenSubKey(change.Path, writable: true);
            if (key is null) continue;
            // A command and its DelegateExecute value form one handler. Preserve them together.
            if (modifiedKeys.Contains(change.Path))
            {
                preserved++;
                continue;
            }
            if (!IsApplied(key, change)) continue;
            if (change.HadValue) key.SetValue(change.Name, OriginalValue(change), change.Kind);
            else key.DeleteValue(change.Name, throwOnMissingValue: false);
        }
        foreach (var path in backup.CreatedKeys.OrderByDescending(path => path.Length))
        {
            using var key = classes.OpenSubKey(path);
            if (key is not null && key.ValueCount == 0 && key.SubKeyCount == 0) classes.DeleteSubKey(path, false);
        }
        NotifyShell();
        File.Delete(backupPath);
        return preserved;
    }

    private static bool IsApplied(RegistryKey? key, RegistrationChange change) =>
        key?.GetValueNames().Contains(change.Name, StringComparer.OrdinalIgnoreCase) == true &&
        key.GetValueKind(change.Name) == RegistryValueKind.String &&
        Equals(key.GetValue(change.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), change.Applied);

    private static bool MatchesOriginal(RegistryKey? key, RegistrationChange change)
    {
        var exists = key?.GetValueNames().Contains(change.Name, StringComparer.OrdinalIgnoreCase) == true;
        if (!change.HadValue) return !exists;
        return exists && key!.GetValueKind(change.Name) == change.Kind &&
            JsonSerializer.SerializeToElement(key.GetValue(change.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)).GetRawText() == change.Original.GetRawText();
    }

    private void Capture(RegistrationBackup backup, string path, string name, string applied)
    {
        using var key = classes.OpenSubKey(path);
        var hadValue = key?.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase) == true;
        backup.Changes.Add(new RegistrationChange
        {
            Path = path,
            Name = name,
            Applied = applied,
            HadValue = hadValue,
            Kind = hadValue ? key!.GetValueKind(name) : RegistryValueKind.String,
            Original = JsonSerializer.SerializeToElement(hadValue ? key!.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null)
        });
        for (var part = path; part.Length > 0;)
        {
            using var parent = classes.OpenSubKey(part);
            if (parent is not null) break;
            backup.CreatedKeys.Add(part);
            var separator = part.LastIndexOf('\\');
            part = separator < 0 ? "" : part[..separator];
        }
    }

    private RegistrationBackup ReadBackup() => JsonSerializer.Deserialize<RegistrationBackup>(File.ReadAllText(backupPath))
        ?? throw new IOException("元の設定のバックアップを読み込めませんでした。");

    private FileStream Lock()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backupPath)!);
        return new FileStream(backupPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static object OriginalValue(RegistrationChange change) => change.Kind switch
    {
        RegistryValueKind.String or RegistryValueKind.ExpandString => change.Original.GetString()!,
        RegistryValueKind.DWord => change.Original.GetInt32(),
        RegistryValueKind.QWord => change.Original.GetInt64(),
        RegistryValueKind.MultiString => change.Original.Deserialize<string[]>()!,
        RegistryValueKind.Binary or RegistryValueKind.None => change.Original.Deserialize<byte[]>()!,
        _ => throw new IOException("元の設定の形式を復元できません。")
    };

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    internal sealed class RegistrationBackup
    {
        public List<RegistrationChange> Changes { get; set; } = [];
        public HashSet<string> CreatedKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class RegistrationChange
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string Applied { get; set; } = "";
        public bool HadValue { get; set; }
        public RegistryValueKind Kind { get; set; }
        public JsonElement Original { get; set; }
    }
}
