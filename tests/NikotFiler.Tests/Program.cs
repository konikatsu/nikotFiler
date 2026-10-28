using System.IO;
using System.Windows.Threading;
using NikotFiler.Core;
using NikotFiler.App;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "nikotFiler-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source"); var target = Path.Combine(root, "target");
            Directory.CreateDirectory(source); Directory.CreateDirectory(target);
            var history = new HistoryStore(Path.Combine(root, "history.jsonl")); var actions = new FileActions(history);
            var input = Path.Combine(source, "こんにちは.txt"); File.WriteAllText(input, "nikotFiler test");
            actions.Copy(input, target);
            Check(File.ReadAllText(Path.Combine(target, "こんにちは.txt")) == "nikotFiler test", "copy preserves content");
            Expect<IOException>(() => actions.Copy(input, target), "copy refuses overwrite");
            Check(File.ReadAllText(input) == "nikotFiler test", "copy leaves source intact");
            actions.Rename(Path.Combine(target, "こんにちは.txt"), "renamed.txt");
            actions.Move(Path.Combine(target, "renamed.txt"), source);
            Check(File.Exists(Path.Combine(source, "renamed.txt")) && !File.Exists(Path.Combine(target, "renamed.txt")), "rename and move");
            actions.CreateFolder(target, "folder");
            Check(Directory.Exists(Path.Combine(target, "folder")), "new folder");
            Expect<ArgumentException>(() => actions.Rename(input, "../escape.txt"), "reject path traversal");
            Expect<IOException>(() => actions.Copy(source, source), "reject copying into itself");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Expect<OperationCanceledException>(() => actions.Copy(input, target, cancellation.Token), "cancel before writing");
            Check(!File.Exists(Path.Combine(target, "こんにちは.txt")), "cancelled copy creates no target");
            var folderSource = Path.Combine(source, "nested"); Directory.CreateDirectory(folderSource);
            File.WriteAllText(Path.Combine(folderSource, "nested.txt"), "nested"); actions.Copy(folderSource, target);
            Check(File.ReadAllText(Path.Combine(target, "nested", "nested.txt")) == "nested", "recursive copy");
            var blocker = Path.Combine(root, "blocker"); File.WriteAllText(blocker, "not a directory");
            var blocked = new FileActions(new HistoryStore(Path.Combine(blocker, "history.jsonl")));
            Expect<IOException>(() => blocked.CreateFolder(target, "blocked"), "unwritable history blocks operation");
            Check(!Directory.Exists(Path.Combine(target, "blocked")), "blocked operation made no change");
            var before = history.ReadLatest().Count; File.AppendAllText(history.FilePath, "{truncated\n");
            Check(history.ReadLatest().Count == before, "truncated history tail preserves older entries");
            Check(history.ReadLatest().Any(x => x.Status == HistoryStatus.Failed), "failed operation recorded");
            var statePath = Path.Combine(root, "settings.json");
            StateStore.Save(statePath, new AppSettings { TextScale = 175 }); StateStore.Save(statePath, new AppSettings { TextScale = 200 });
            Check(StateStore.Load<AppSettings>(statePath).TextScale == 200, "font setting persists");
            File.WriteAllText(statePath, "{broken");
            Check(StateStore.Load<AppSettings>(statePath).TextScale == 175, "settings recover backup");
            Check(AppSettings.ValidScale(double.NaN) == 100 && AppSettings.ValidScale(999) == 250, "invalid font scale repaired");
            var list = FileEntry.ReadDirectory(source, CancellationToken.None);
            Check(list[0].IsDirectory && list.Any(x => x.Name == "こんにちは.txt"), "folders sorted first and Unicode paths work");

            // In-process regression: the selected files MUST be those in the actual
            // current tab, and retained controls must keep a tab's own selection.
            var app = new App(); app.InitializeComponent();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var pane = new PaneView("test"); pane.AddTab(source);
            PumpUntil(() => pane.Current is { IsLoading: false });
            var first = pane.Current!; first.List.SelectedItem = first.Entries.First(x => x.Name == "こんにちは.txt");
            Check(pane.SelectedEntries is [{ Name: "こんにちは.txt" }], "visible selection is authoritative");
            pane.AddTab(target); PumpUntil(() => pane.Current is { IsLoading: false });
            Check(pane.SelectedEntries.Length == 0, "new tab never inherits hidden selection");
            pane.NextTab();
            Check(ReferenceEquals(pane.Current, first) && pane.SelectedEntries is [{ Name: "こんにちは.txt" }], "switch back preserves control and selection");
            pane.CloseCurrent();
            Check(pane.SelectedEntries.Length == 0, "closing selected tab never targets its old files");
            pane.AddTab(source); PumpUntil(() => pane.Current is { IsLoading: false });
            var snapshot = pane.Snapshot();
            Check(snapshot.Paths.Count == 2 && snapshot.Paths[snapshot.SelectedIndex] == source, "arbitrary tabs included in saved session");
            var restored = new PaneView("restored"); restored.Restore(snapshot, source);
            Check(restored.Snapshot().Paths.SequenceEqual(snapshot.Paths), "all tabs restore in order");
            pane.CancelLoads(); restored.CancelLoads();
            Console.WriteLine($"PASS: {_checks} checks"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            // Only remove the unique directory created above, after checking its root.
            var full = Path.GetFullPath(root);
            var temp = Path.GetFullPath(Path.GetTempPath());
            if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("nikotFiler-tests-")) Directory.Delete(full, true);
        }
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); _checks++; }
    private static void Expect<T>(Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { _checks++; return; } throw new Exception(label);
    }
    private static void PumpUntil(Func<bool> ready)
    {
        var started = DateTime.UtcNow;
        while (!ready())
        {
            if (DateTime.UtcNow - started > TimeSpan.FromSeconds(10)) throw new TimeoutException("WPF tab did not load");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(5);
        }
    }
}
