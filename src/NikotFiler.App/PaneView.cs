using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using NikotFiler.Core;

namespace NikotFiler.App;

// Each tab owns its actual list control. Commands read selection from this control,
// never from a second, potentially stale copy of the selected items.
public sealed class PaneView : Border
{
    private readonly TabControl _tabs = new();
    private readonly TextBlock _label = new();
    private readonly List<FolderTab> _folders = [];
    private readonly string _name;
    public event Action<PaneView>? Activated;
    public event Action? Changed;
    public event Action<FileEntry>? OpenRequested;
    public event Action? ContextRequested;
    public FolderTab? Current => _folders.FirstOrDefault(t => ReferenceEquals(t.Tab, _tabs.SelectedItem));
    public FileEntry[] SelectedEntries => Current?.List.SelectedItems.Cast<FileEntry>().ToArray() ?? [];
    public bool IsWorking { get; set; }
    public string CurrentPath => Current?.Path ?? "";

    public PaneView(string name)
    {
        _name = name;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Background = Brushes.White;
        Padding = new Thickness(7, 0, 7, 7);
        var dock = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(4, 8, 1, 5) };
        _label.FontWeight = FontWeights.SemiBold;
        var add = new Button { Content = "+", ToolTip = "新しいタブ (Ctrl+T)", Padding = new Thickness(9, 2, 9, 2) };
        System.Windows.Automation.AutomationProperties.SetName(add, name + "：新しいタブ");
        add.Click += (_, _) => AddTab(CurrentPath);
        DockPanel.SetDock(add, Dock.Right); header.Children.Add(add);
        var all = new Button { Content = "⌄", ToolTip = "タブの一覧", Padding = new Thickness(8, 2, 8, 2) };
        all.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            foreach (var folder in _folders)
            {
                var item = new MenuItem { Header = folder.Path, IsCheckable = true, IsChecked = folder == Current };
                item.Click += (_, _) => _tabs.SelectedItem = folder.Tab;
                menu.Items.Add(item);
            }
            menu.PlacementTarget = all; menu.IsOpen = true;
        };
        DockPanel.SetDock(all, Dock.Right); header.Children.Add(all);
        header.Children.Add(_label);
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        dock.Children.Add(_tabs); Child = dock;
        _tabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, _tabs)) return;
            Activated?.Invoke(this); Changed?.Invoke();
        };
        PreviewMouseDown += (_, _) => Activated?.Invoke(this);
        GotKeyboardFocus += (_, _) => Activated?.Invoke(this);
        SetActive(false);
    }

    public void SetActive(bool active)
    {
        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#78B6A0" : "#E3E9E7"));
        _label.Text = _name + (active ? "   • 操作中" : "");
        _label.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#16766B" : "#74848A"));
    }

    public void Restore(PaneSession? state, string fallback)
    {
        foreach (var path in state?.Paths?.Where(p => !string.IsNullOrWhiteSpace(p)) ?? []) AddTab(path, false);
        if (_folders.Count == 0) AddTab(fallback, false);
        _tabs.SelectedIndex = Math.Clamp(state?.SelectedIndex ?? 0, 0, _folders.Count - 1);
    }

    public PaneSession Snapshot() => new() { Paths = _folders.Select(f => f.Path).ToList(), SelectedIndex = Math.Max(0, _tabs.SelectedIndex) };

    public void AddTab(string? path = null, bool select = true)
    {
        var folder = new FolderTab(path is { Length: > 0 } ? path : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var title = new TextBlock { MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis };
        var close = new Button { Content = "×", Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(7, 0, 0, 0), ToolTip = "タブを閉じる (Ctrl+W)" };
        var tabHeader = new StackPanel { Orientation = Orientation.Horizontal }; tabHeader.Children.Add(title); tabHeader.Children.Add(close);
        folder.Title = title;
        folder.Tab.Header = tabHeader;
        close.Click += (_, e) => { e.Handled = true; CloseTab(folder); };
        folder.Tab.PreviewMouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { e.Handled = true; CloseTab(folder); } };
        var menu = new ContextMenu();
        var duplicate = new MenuItem { Header = "タブを複製" }; duplicate.Click += (_, _) => AddTab(folder.Path); menu.Items.Add(duplicate);
        var closeItem = new MenuItem { Header = "タブを閉じる" }; closeItem.Click += (_, _) => CloseTab(folder); menu.Items.Add(closeItem);
        folder.Tab.ContextMenu = menu;
        var list = folder.List;
        var view = new GridView();
        var template = new DataTemplate();
        var line = new FrameworkElementFactory(typeof(StackPanel)); line.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var icon = new FrameworkElementFactory(typeof(TextBlock)); icon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
        icon.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(159, 141, 76)));
        icon.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 9, 0)); icon.SetBinding(TextBlock.TextProperty, new Binding(nameof(FileEntry.Glyph))); line.AppendChild(icon);
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding(nameof(FileEntry.Name))); line.AppendChild(name); template.VisualTree = line;
        view.Columns.Add(new GridViewColumn { Header = "名前", CellTemplate = template, Width = 220 });
        view.Columns.Add(new GridViewColumn { Header = "サイズ", DisplayMemberBinding = new Binding(nameof(FileEntry.SizeText)), Width = 88 });
        view.Columns.Add(new GridViewColumn { Header = "更新日時", DisplayMemberBinding = new Binding(nameof(FileEntry.ModifiedText)), Width = 150 });
        list.View = view;
        list.SelectionChanged += (_, _) => Changed?.Invoke();
        list.MouseDoubleClick += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListViewItem { Content: FileEntry entry }) OpenRequested?.Invoke(entry);
        };
        list.KeyDown += (_, e) => { if (e.Key == Key.Enter && list.SelectedItem is FileEntry item) { OpenRequested?.Invoke(item); e.Handled = true; } };
        list.ContextMenu = new ContextMenu();
        list.ContextMenuOpening += (_, e) =>
        {
            e.Handled = true; Activated?.Invoke(this); ContextRequested?.Invoke();
            if (list.ContextMenu is { } contextMenu) { contextMenu.PlacementTarget = list; contextMenu.IsOpen = true; }
        };
        list.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListViewItem row && !row.IsSelected)
            { list.SelectedItems.Clear(); row.IsSelected = true; }
        };
        list.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is not GridViewColumnHeader { Column: { } column }) return;
            var property = column.Header?.ToString() switch { "サイズ" => nameof(FileEntry.Size), "更新日時" => nameof(FileEntry.Modified), _ => nameof(FileEntry.Name) };
            folder.Descending = folder.SortProperty == property && !folder.Descending;
            folder.SortProperty = property;
            var collection = CollectionViewSource.GetDefaultView(list.ItemsSource);
            if (collection is null) return;
            using (collection.DeferRefresh())
            {
                collection.SortDescriptions.Clear(); collection.SortDescriptions.Add(new SortDescription(nameof(FileEntry.IsDirectory), ListSortDirection.Descending));
                collection.SortDescriptions.Add(new SortDescription(property, folder.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending));
            }
        }));
        var grid = new Grid(); grid.Children.Add(list);
        folder.Message.VerticalAlignment = VerticalAlignment.Center; folder.Message.HorizontalAlignment = HorizontalAlignment.Center;
        folder.Message.TextWrapping = TextWrapping.Wrap; folder.Message.Margin = new Thickness(25); folder.Message.IsHitTestVisible = false;
        folder.Message.Foreground = new SolidColorBrush(Color.FromRgb(116, 132, 138)); grid.Children.Add(folder.Message);
        var scale = Application.Current.Resources["TextSize"] is double size ? size / 14 : 1;
        foreach (var column in view.Columns) column.Width *= scale;
        folder.Tab.Content = grid; _folders.Add(folder); _tabs.Items.Add(folder.Tab);
        if (select) _tabs.SelectedItem = folder.Tab;
        _ = NavigateAsync(folder, folder.Path, false);
        Changed?.Invoke();
    }

    public void CloseCurrent() { if (Current is { } current) CloseTab(current); }
    private void CloseTab(FolderTab folder)
    {
        if (IsWorking) return;
        folder.LoadCancellation?.Cancel(); _folders.Remove(folder); _tabs.Items.Remove(folder.Tab);
        if (_folders.Count == 0) AddTab(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Changed?.Invoke();
    }
    public void NextTab() { if (_tabs.Items.Count > 0) _tabs.SelectedIndex = (_tabs.SelectedIndex + 1) % _tabs.Items.Count; }

    public Task NavigateAsync(string path) => Current is { } current ? NavigateAsync(current, path, true) : Task.CompletedTask;
    private async Task NavigateAsync(FolderTab folder, string path, bool pushHistory)
    {
        folder.LoadCancellation?.Cancel(); var cancellation = new CancellationTokenSource(); folder.LoadCancellation = cancellation;
        try
        {
            path = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            if (pushHistory && !string.Equals(folder.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                folder.Back.Push(folder.Path); folder.Forward.Clear();
            }
            folder.Path = path; folder.IsLoading = true;
            folder.Title.Text = FolderName(path); folder.Tab.ToolTip = path;
            folder.List.ItemsSource = null; folder.Entries = []; folder.Error = "";
            folder.Message.Text = "読み込み中…"; Changed?.Invoke();
            var items = await Task.Run(() => FileEntry.ReadDirectory(path, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested || !_folders.Contains(folder)) return;
            folder.Entries = items;
            folder.List.ItemsSource = items;
            Filter(folder, folder.FilterText);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (cancellation.IsCancellationRequested) return;
            folder.Error = "開けませんでした\n" + ex.Message + "\n\nF5 で再試行できます";
            folder.Message.Text = folder.Error;
        }
        finally { if (!cancellation.IsCancellationRequested) { folder.IsLoading = false; Changed?.Invoke(); } }
    }

    public Task RefreshAsync() => Current is { } current ? NavigateAsync(current, current.Path, false) : Task.CompletedTask;
    public async Task GoBackAsync(bool forward = false)
    {
        if (Current is not { } current) return;
        var from = forward ? current.Forward : current.Back; var to = forward ? current.Back : current.Forward;
        if (from.Count == 0) return;
        to.Push(current.Path); await NavigateAsync(current, from.Pop(), false);
    }
    public void SetFilter(string text) { if (Current is { } current) Filter(current, text); }
    private static void Filter(FolderTab folder, string text)
    {
        folder.FilterText = text;
        if (CollectionViewSource.GetDefaultView(folder.List.ItemsSource) is { } view)
            view.Filter = item => item is FileEntry entry && entry.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase);
        folder.Message.Text = folder.Error.Length > 0 ? folder.Error : folder.List.Items.Count == 0 ? text.Length > 0 ? "一致する項目がありません" : "このフォルダーは空です" : "";
    }

    public void ScaleColumns(double ratio)
    {
        foreach (var folder in _folders)
            if (folder.List.View is GridView grid)
                foreach (var column in grid.Columns) if (double.IsFinite(column.Width)) column.Width *= ratio;
    }
    public void CancelLoads() { foreach (var folder in _folders) folder.LoadCancellation?.Cancel(); }
    public static string FolderName(string path)
    {
        var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)); return name.Length > 0 ? name : path;
    }
}

public sealed class FolderTab(string path)
{
    public string Path { get; set; } = path;
    public TabItem Tab { get; } = new();
    public TextBlock Title { get; set; } = new();
    public ListView List { get; } = new() { SelectionMode = SelectionMode.Extended };
    public TextBlock Message { get; } = new();
    public List<FileEntry> Entries { get; set; } = [];
    public Stack<string> Back { get; } = new();
    public Stack<string> Forward { get; } = new();
    public CancellationTokenSource? LoadCancellation { get; set; }
    public string FilterText { get; set; } = "";
    public string Error { get; set; } = "";
    public bool IsLoading { get; set; } = true;
    public string SortProperty { get; set; } = "";
    public bool Descending { get; set; }
}
