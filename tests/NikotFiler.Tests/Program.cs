using System.IO;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Input;
using NikotFiler.Core;
using NikotFiler.App;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--shell-folder", var folder] && Environment.GetEnvironmentVariable("NIKOTFILER_TEST_SHELL_RESULT") is { } result)
        {
            File.WriteAllText(result, folder);
            return 0;
        }
        var root = Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? Path.GetTempPath(), "nikotFiler-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source"); var target = Path.Combine(root, "target");
            Directory.CreateDirectory(source); Directory.CreateDirectory(target);
            var history = new HistoryStore(Path.Combine(root, "history.jsonl")); var actions = new FileActions(history);
            var input = Path.Combine(source, "こんにちは.txt"); File.WriteAllText(input, "nikotFiler test");
            actions.Copy(input, target);
            Check(File.ReadAllText(Path.Combine(target, "こんにちは.txt")) == "nikotFiler test", "copy preserves content");
            var existing = Path.Combine(target, "こんにちは.txt");
            File.WriteAllText(existing, "existing content");
            actions.Copy(input, target);
            actions.Copy(input, target);
            var firstCopy = Path.Combine(target, "こんにちは - コピー.txt");
            var secondCopy = Path.Combine(target, "こんにちは - コピー (2).txt");
            Check(File.ReadAllText(existing) == "existing content" && File.ReadAllText(firstCopy) == "nikotFiler test" &&
                File.ReadAllText(secondCopy) == "nikotFiler test", "same-name file copies preserve existing files and extensions");
            Check(history.ReadLatest().Any(x => x.Kind == HistoryKind.Copy && x.Status == HistoryStatus.Succeeded && x.DestinationPath == secondCopy),
                "history records the renamed copy's destination");
            var planned = FileActions.PlanCopyDestinations([input], target)[0];
            File.WriteAllText(planned, "created after confirmation");
            Expect<IOException>(() => actions.CopyToPath(input, planned), "confirmed copy refuses a newly occupied name");
            Check(File.ReadAllText(planned) == "created after confirmation", "file added after copy planning is preserved");
            var sameFolderCopy = FileActions.PlanCopyDestinations([input], source)[0];
            Check(sameFolderCopy == Path.Combine(source, "こんにちは - コピー.txt"), "copy into the source folder plans a separate file");
            var reservedName = Path.Combine(source, Path.GetFileName(firstCopy));
            File.WriteAllText(reservedName, "another selected file");
            var batch = FileActions.PlanCopyDestinations([input, reservedName], source);
            Check(batch[0] == Path.Combine(source, Path.GetFileName(secondCopy)) &&
                batch[1] != batch[0] && batch[1] != reservedName, "batch planning reserves a different destination for each copy");
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
            var trashSample = Path.Combine(source, "trash-test-only.txt"); File.WriteAllText(trashSample, "temporary test fixture");
            Task.Run(() => actions.DeleteToRecycleBin(trashSample)).GetAwaiter().GetResult();
            Check(!File.Exists(trashSample), "worker-thread trash operation removes only the dummy fixture");
            Check(history.ReadLatest().Any(x => x.Kind == HistoryKind.Delete && x.Status == HistoryStatus.Succeeded), "trash operation is journaled");
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
            var outlineSettingsPath = Path.Combine(root, "outlines.json");
            File.WriteAllText(outlineSettingsPath, "{\"Version\":1,\"TextScale\":125}");
            Check(StateStore.Load<AppSettings>(outlineSettingsPath).ShowPaneOutlines, "existing settings default pane outlines to on");
            StateStore.Save(outlineSettingsPath, new AppSettings { ShowPaneOutlines = false });
            Check(!StateStore.Load<AppSettings>(outlineSettingsPath).ShowPaneOutlines, "outline off preference survives settings reload");
            Check(AppSettings.ValidScale(double.NaN) == 100 && AppSettings.ValidScale(999) == 250, "invalid font scale repaired");
            var list = FileEntry.ReadDirectory(source, CancellationToken.None);
            CheckHistoryLocations(source, target, input);
            Check(list[0].IsDirectory && list.Any(x => x.Name == "こんにちは.txt"), "folders sorted first and Unicode paths work");
            IntegrationChecks.Run(root, Check);

            // In-process regression: the selected files MUST be those in the actual
            // current tab, and retained controls must keep a tab's own selection.
            var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            ShellMenuChecks.Run(root, Check);
            IntegrationChecks.RenderSettings(root, Check);
            CheckCopyNameDialog(root);
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
            pane.CloseCurrent();
            var last = pane.Current;
            pane.CloseCurrent();
            Check(last is not null && pane.TabCount == 1 && ReferenceEquals(pane.Current, last), "last tab stays open instead of opening home");
            Check(!last!.CloseButton.IsEnabled && !last.CloseMenu.IsEnabled, "last-tab close controls are disabled");
            pane.CancelLoads(); restored.CancelLoads();
            RenderMainWindow(root);
            Console.WriteLine($"PASS: {_checks} checks"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            // Only remove the unique directory created above, after checking its root.
            var full = Path.GetFullPath(root);
            var temp = Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? Path.GetTempPath());
            if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("nikotFiler-tests-")) Directory.Delete(full, true);
        }
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); _checks++; }
    private static void CheckHistoryLocations(string source, string target, string file)
    {
        HistoryEntry Entry(HistoryKind kind, string path, string? destination = null, HistoryStatus status = HistoryStatus.Succeeded)
            => new(Guid.NewGuid(), DateTimeOffset.Now, kind, status, path, destination);
        Check(HistoryNavigation.GetFolder(Entry(HistoryKind.FolderVisit, source)) == source, "folder history opens that folder");
        Check(HistoryNavigation.GetFolder(Entry(HistoryKind.FileOpen, file)) == source, "file history opens containing folder");
        Check(HistoryNavigation.GetFolder(Entry(HistoryKind.Copy, file, Path.Combine(target, "nested", "nested.txt"))) == Path.Combine(target, "nested"), "file operation history opens destination parent");
        Check(HistoryNavigation.GetFolder(Entry(HistoryKind.Move, source, Path.Combine(target, "nested"))) == Path.Combine(target, "nested"), "folder operation history opens destination folder");
        Check(HistoryNavigation.GetFolder(Entry(HistoryKind.CreateFolder, target, Path.Combine(target, "folder"))) == Path.Combine(target, "folder"), "folder creation history opens created folder");
        Check(HistoryNavigation.GetFolder(Entry(HistoryKind.Delete, file)) == source, "deleted-item history opens containing folder");
        Check(HistoryNavigation.GetFolder(Entry(HistoryKind.Copy, file, null, HistoryStatus.Failed)) == source, "failed operation history opens source location");
        Expect<FileNotFoundException>(() => HistoryNavigation.GetFolder(Entry(HistoryKind.Move, file, Path.Combine(target, "missing"))), "missing destination is reported instead of opening wrong folder");
    }
    private static void RenderMainWindow(string root)
    {
        var sample = Path.Combine(root, "サンプル"); Directory.CreateDirectory(sample);
        foreach (var name in new[] { "ドキュメント", "プロジェクト", "画像" }) Directory.CreateDirectory(Path.Combine(sample, name));
        File.WriteAllText(Path.Combine(sample, "はじめに.txt"), "nikotFiler preview sample");
        File.WriteAllText(Path.Combine(sample, "ワークメモ.md"), "# sample");
        var profile = Path.Combine(root, "ui-profile");
        var sessionPath = Path.Combine(profile, "session.json");
        var bookmarkToRemove = Path.Combine(sample, "画像");
        StateStore.Save(sessionPath, new AppSession
        {
            Bookmarks = [sample, bookmarkToRemove],
            BookmarkLabels = { [bookmarkToRemove] = "画像の整理" }
        });
        App.ConfigureArguments(["--data-dir", profile, "--folder", sample]);
        var window = new MainWindow();
        window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1280, 780)); content.Arrange(new Rect(0, 0, 1280, 780)); content.UpdateLayout();
        PumpUntil(() => Descendants<PaneView>(content).All(p => p.Current is { IsLoading: false }));
        var sidebar = (StackPanel)window.FindName("Sidebar");
        var bookmarks = sidebar.Children.OfType<Button>().Where(button => button.ContextMenu is not null).ToArray();
        Check(bookmarks.Select(System.Windows.Automation.AutomationProperties.GetName).SequenceEqual(new[] { "サンプル", "画像の整理" }),
            "bookmarks show their folder name by default or their saved custom label");
        var removeBookmark = (MenuItem)bookmarks[1].ContextMenu.Items[1];
        removeBookmark.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(!sidebar.Children.Contains(bookmarks[1]) && Directory.Exists(bookmarkToRemove),
            "removing a bookmark keeps its target folder");
        var screenshots = Path.Combine(Environment.CurrentDirectory, "artifacts", "ui-checks"); Directory.CreateDirectory(screenshots);
        foreach (var scale in new[] { 100, 200 })
        {
            window.ApplyScale(scale);
            content.Measure(new Size(1280, 780)); content.Arrange(new Rect(0, 0, 1280, 780)); content.UpdateLayout();
            var image = new RenderTargetBitmap(1280, 780, 96, 96, PixelFormats.Pbgra32); image.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = File.Create(Path.Combine(screenshots, $"nikotFiler-{scale}.png")); encoder.Save(output);
            Check(Descendants<PaneView>(content).Count() == 2, $"both panes retained at {scale}%");
            Check(Application.Current.Resources["TextSize"] is double size && Math.Abs(size - 14 * scale / 100d) < .01, $"global font applied at {scale}%");
        }
        var originalPanes = Descendants<PaneView>(content).ToArray();
        var left = originalPanes.Single(p => Grid.GetColumn(p) == 0);
        var right = originalPanes.Single(p => Grid.GetColumn(p) == 2);
        Check(originalPanes.All(p => p.BorderThickness.Left > 0), "new profile starts with outlines on in both panes");
        var selectedEntry = left.Current!.Entries.First(x => !x.IsDirectory);
        left.Current.List.SelectedItem = selectedEntry;
        var existingList = left.Current.List;
        existingList.UpdateLayout();
        var otherEntry = left.Current.Entries.First(entry => entry != selectedEntry);
        var selectedRow = (ListViewItem)existingList.ItemContainerGenerator.ContainerFromItem(selectedEntry);
        var otherRow = (ListViewItem)existingList.ItemContainerGenerator.ContainerFromItem(otherEntry);
        existingList.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
            { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent, Source = otherRow });
        Check(left.SelectedEntries.SequenceEqual(new[] { otherEntry }), "right-clicking another row targets that visible row");
        existingList.SelectedItems.Add(selectedEntry);
        existingList.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
            { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent, Source = selectedRow });
        Check(left.SelectedEntries.Length == 2, "right-clicking within a multi-selection keeps the selected group");
        existingList.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
            { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent, Source = existingList });
        Check(left.SelectedEntries.Length == 0, "right-clicking blank list space does not retain a file operation target");
        existingList.SelectedItem = selectedEntry;
        window.SetPaneOutlines(false);
        left.SetActive(true);
        Check(originalPanes.All(p => p.BorderThickness.Left == 0) && ReferenceEquals(left.Current.List, existingList) && ReferenceEquals(left.Current.List.SelectedItem, selectedEntry), "outline toggle preserves selection and stays off when pane is activated");
        content.Measure(new Size(1280, 780)); content.Arrange(new Rect(0, 0, 1280, 780)); content.UpdateLayout();
        var noOutlineImage = new RenderTargetBitmap(1280, 780, 96, 96, PixelFormats.Pbgra32); noOutlineImage.Render(content);
        var noOutlineEncoder = new PngBitmapEncoder(); noOutlineEncoder.Frames.Add(BitmapFrame.Create(noOutlineImage));
        using (var output = File.Create(Path.Combine(screenshots, "nikotFiler-outlines-off-200.png"))) noOutlineEncoder.Save(output);
        var survivingTab = left.Current;
        right.CloseCurrent();
        Check(right.TabCount == 0 && right.Visibility == Visibility.Collapsed, "closing last right tab removes empty pane without replacement");
        Check(ReferenceEquals(left.Current, survivingTab) && left.TabCount == 1, "closing right pane preserves left control and path");
        left.CloseCurrent();
        Check(left.TabCount == 1 && ReferenceEquals(left.Current, survivingTab), "last tab across both panes is protected");
        window.SetTwoPanes(true);
        var rightTab = right.Current;
        Check(right.TabCount == 1 && right.Visibility == Visibility.Visible, "explicit two-pane request recreates right tab");
        left.CloseCurrent();
        Check(left.TabCount == 0 && left.Visibility == Visibility.Collapsed && Grid.GetColumn(right) == 0, "closing left pane promotes surviving right pane");
        Check(ReferenceEquals(right.Current, rightTab) && right.SelectedEntries.Length == 0, "pane promotion retains surviving tab without stale selection");
        CheckHistoryWindow(sample, screenshots);
        window.Close();
        var saved = StateStore.Load<AppSession>(sessionPath);
        Check(saved.Bookmarks.SequenceEqual(new[] { sample }) && !saved.BookmarkLabels.ContainsKey(bookmarkToRemove),
            "removing a bookmark also removes its saved label and keeps the other bookmark");
        Check(saved.Left.Paths.Count == 1 && saved.Right.Paths.Count == 0, "closed pane remains empty in saved session");
        var restarted = new MainWindow(); restarted.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var restartedRoot = (FrameworkElement)restarted.Content;
        restartedRoot.Measure(new Size(1280, 780)); restartedRoot.Arrange(new Rect(0, 0, 1280, 780)); restartedRoot.UpdateLayout();
        var restoredPanes = Descendants<PaneView>(restartedRoot).ToArray();
        Check(restoredPanes.Sum(p => p.TabCount) == 1 && restoredPanes.Single(p => Grid.GetColumn(p) == 2).TabCount == 0, "restart does not reopen closed pane or home tab");
        Check(restoredPanes.All(p => p.BorderThickness.Left == 0), "outline off preference is applied after window restart and pane promotion");
        restarted.SetPaneOutlines(true);
        Check(restoredPanes.All(p => p.BorderThickness.Left > 0), "outlines can be enabled again after restart");
        restarted.Close();
        var requested = Path.Combine(sample, "プロジェクト");
        App.ConfigureArguments(["--data-dir", profile, "--shell-folder", requested + @"\."]);
        var external = new MainWindow();
        external.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var externalRoot = (FrameworkElement)external.Content;
        externalRoot.Measure(new Size(1280, 780)); externalRoot.Arrange(new Rect(0, 0, 1280, 780)); externalRoot.UpdateLayout();
        var externalPanes = Descendants<PaneView>(externalRoot).ToArray();
        Check(externalPanes.Sum(pane => pane.TabCount) == 2 && externalPanes.Single(pane => Grid.GetColumn(pane) == 0).Current?.Path == requested,
            "a shell folder request opens a selected tab while keeping the previously saved tabs");
        external.Close();
    }
    private static void CheckHistoryWindow(string sample, string screenshots)
    {
        var entries = new[]
        {
            new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.FolderVisit, HistoryStatus.Viewed, sample),
            new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.FileOpen, HistoryStatus.Viewed, Path.Combine(sample, "はじめに.txt")),
            new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.Copy, HistoryStatus.Succeeded, Path.Combine(sample, "ワークメモ.md"), Path.Combine(sample, "copy.md"))
        };
        var history = new HistoryWindow(null, entries);
        var content = (FrameworkElement)history.Content;
        content.Measure(new Size(980, 640)); content.Arrange(new Rect(0, 0, 980, 640)); content.UpdateLayout();
        var list = Descendants<ListView>(content).Single(); var filter = Descendants<TextBox>(content).Single();
        Check(list.Items.Count == 3 && list.SelectedItem is HistoryItem, "history shows selectable rows");
        filter.Text = "コピー";
        Check(list.Items.Count == 1 && list.SelectedItem is HistoryItem { Entry.Kind: HistoryKind.Copy }, "history search includes operation kind");
        filter.Text = "存在しない検索語";
        Check(list.Items.Count == 0, "history filter handles no matches");
        filter.Text = "はじめに";
        Check(list.Items.Count == 1, "history search includes file names");
        filter.Text = ""; content.UpdateLayout();
        var image = new RenderTargetBitmap(980, 640, 96, 96, PixelFormats.Pbgra32); image.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var output = File.Create(Path.Combine(screenshots, "nikotFiler-history-200.png"))) encoder.Save(output);
        history.Close();
    }
    private static void CheckCopyNameDialog(string root)
    {
        var first = Path.Combine(root, "copy-dialog", "first");
        var second = Path.Combine(root, "copy-dialog", "second");
        var target = Path.Combine(root, "copy-dialog", "target");
        foreach (var directory in new[] { first, second, target }) Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(first, "報告書.xlsx"), "first dummy file");
        File.WriteAllText(Path.Combine(second, "報告書.xlsx"), "second dummy file");
        var existing = Path.Combine(target, "報告書.xlsx");
        File.WriteAllText(existing, "existing dummy file");
        var sources = new[] { FileEntry.ReadDirectory(first, default).Single(), FileEntry.ReadDirectory(second, default).Single() };
        var proposed = FileActions.PlanCopyDestinations(sources.Select(entry => entry.FullPath).ToArray(), target);
        var dialog = new CopyNameDialog(null, sources, target, proposed);
        var content = (FrameworkElement)dialog.Content;
        content.Measure(new Size(720, 680)); content.Arrange(new Rect(0, 0, 720, 680)); content.UpdateLayout();
        var inputs = Descendants<TextBox>(content).ToArray();
        Check(inputs.Select(input => input.Text).SequenceEqual(new[] { "報告書 - コピー.xlsx", "報告書 - コピー (2).xlsx" }),
            "conflicting copies have editable numbered name suggestions");
        dialog.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Check(inputs[0].SelectedText == "報告書 - コピー", "copy name editing initially keeps the extension outside the selection");
        Check(dialog.ValidateDestinations(), "suggested copy names can be accepted directly");
        foreach (var invalid in new[] { "", "../escape.xlsx", "CON.xlsx", "報告書.xlsx" })
        {
            inputs[0].Text = invalid;
            Check(!dialog.ValidateDestinations(), "copy name dialog rejects empty, invalid, reserved, or occupied names");
        }
        inputs[0].Text = "確認用.xlsx";
        inputs[1].Text = "確認用.xlsx";
        Check(!dialog.ValidateDestinations(), "copy name dialog rejects names duplicated within the batch");
        Check(File.ReadAllText(existing) == "existing dummy file" && Directory.GetFiles(target).Length == 1,
            "copy name entry and validation perform no file operations");
        inputs[1].Text = "提出用.xlsx";
        Check(dialog.ValidateDestinations(), "custom copy names are accepted after errors are corrected");

        var screenshots = Path.Combine(Environment.CurrentDirectory, "artifacts", "ui-checks");
        Directory.CreateDirectory(screenshots);
        foreach (var scale in new[] { 100, 200 })
        {
            Application.Current.Resources["TextSize"] = 14d * scale / 100;
            content.Measure(new Size(720, 680)); content.Arrange(new Rect(0, 0, 720, 680)); content.UpdateLayout();
            var image = new RenderTargetBitmap(720, 680, 96, 96, PixelFormats.Pbgra32);
            image.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = File.Create(Path.Combine(screenshots, $"nikotFiler-copy-name-{scale}.png"));
            encoder.Save(output);
        }
        Application.Current.Resources["TextSize"] = 14d;
        var actions = new FileActions(new HistoryStore(Path.Combine(root, "copy-dialog", "history.jsonl")));
        for (var i = 0; i < sources.Length; i++) actions.CopyToPath(sources[i].FullPath, dialog.DestinationPaths[i]);
        Check(File.ReadAllText(Path.Combine(target, "確認用.xlsx")) == "first dummy file" &&
            File.ReadAllText(Path.Combine(target, "提出用.xlsx")) == "second dummy file" &&
            File.ReadAllText(existing) == "existing dummy file", "confirmed custom names are used for the copied files");
        dialog.Close();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject element) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
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
