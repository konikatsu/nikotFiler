using System.IO;
using System.Windows;
using NikotFiler.App;
using NikotFiler.Core;

internal static class ShellMenuChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "shell-menu");
        Directory.CreateDirectory(folder);
        var first = Path.Combine(folder, "メニュー確認.txt");
        var second = Path.Combine(folder, "複数選択.txt");
        File.WriteAllText(first, "first dummy file");
        File.WriteAllText(second, "second dummy file");
        using var menu = new WindowsContextMenu([first]);
        var commands = Enumerable.Range(1, menu.CommandCount).ToDictionary(id => (uint)id, id => menu.GetVerb((uint)id));
        check(commands.Values.Contains("copy") && commands.Values.Contains("delete") && commands.Values.Contains("properties"),
            "Windows supplies standard file context menu commands for the selected dummy file");
        check(commands.Where(pair => pair.Value is "copy" or "delete" or "properties").All(pair => menu.GetLabel(pair.Key).Length > 0),
            "native context menu commands expose their displayed labels");
        check(new[] { "copy", "cut", "paste", "delete", "rename" }.All(WindowsContextMenu.UsesAppFileOperation) &&
            !WindowsContextMenu.UsesAppFileOperation("properties"), "file-changing standard verbs are handled by app operations");
        var owner = new Window();
        var rejected = false;
        try { menu.Invoke(owner, commands.Single(pair => pair.Value == "delete").Key); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && File.Exists(first), "the native menu cannot bypass the app's confirmed recycle-bin deletion");
        using var multiple = new WindowsContextMenu([first, second]);
        check(Enumerable.Range(1, multiple.CommandCount).Any(id => multiple.GetVerb((uint)id) == "copy"),
            "Windows context menu accepts the entire visible multi-selection");
        using var directoryMenu = new WindowsContextMenu([folder]);
        check(directoryMenu.GetLabel(WindowsContextMenu.NewTabCommand) == "新しいタブで開く",
            "folder context menus offer the app's new-tab action");
        check(File.ReadAllText(first) == "first dummy file" && File.ReadAllText(second) == "second dummy file",
            "creating and disposing native context menus leaves dummy files intact");
        var entry = new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.ShellCommand, HistoryStatus.Succeeded, first);
        check(new HistoryItem(entry).KindText == "Windows メニュー" && HistoryNavigation.GetFolder(entry) == folder,
            "shell-command history is labeled accurately and navigates without replaying the command");
        owner.Close();
    }
}
