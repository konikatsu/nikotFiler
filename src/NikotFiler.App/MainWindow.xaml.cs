using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NikotFiler.Core;

namespace NikotFiler.App;

public partial class MainWindow : Window
{
    private readonly PaneView _left = new("左の画面");
    private readonly PaneView _right = new("右の画面");
    private PaneView _active;
    private readonly AppSettings _settings;
    private readonly AppSession _session;
    private readonly HistoryStore _history;
    private readonly FileActions _actions;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private CancellationTokenSource? _operation;
    private bool _restoring = true;
    private bool _busy;
    private bool _updating;
    private string SettingsPath => Path.Combine(App.DataDirectory, "settings.json");
    private string SessionPath => Path.Combine(App.DataDirectory, "session.json");
    private PaneView Other => ReferenceEquals(_active, _left) ? _right : _left;

    public MainWindow()
    {
        _active = _left;
        _settings = StateStore.Load<AppSettings>(SettingsPath);
        _session = StateStore.Load<AppSession>(SessionPath);
        _history = new HistoryStore(Path.Combine(App.DataDirectory, "history.jsonl"));
        _actions = new FileActions(_history);
        InitializeComponent();
        Width = double.IsFinite(_settings.Width) ? Math.Clamp(_settings.Width, 860, SystemParameters.WorkArea.Width) : 1280;
        Height = double.IsFinite(_settings.Height) ? Math.Clamp(_settings.Height, 560, SystemParameters.WorkArea.Height) : 820;
        if (_settings.Maximized) WindowState = WindowState.Maximized;
        PaneHost.Children.Add(_left); Grid.SetColumn(_right, 2); PaneHost.Children.Add(_right);
        foreach (var pane in new[] { _left, _right })
        {
            pane.Activated += SetActive;
            pane.Changed += () => { UpdateChrome(); ScheduleSave(); };
            pane.OpenRequested += entry => _ = OpenAsync(entry);
            pane.ContextRequested += () => { if (pane.Current is { } tab) tab.List.ContextMenu = BuildFileMenu(); };
        }
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveState(); };
        SizeChanged += (_, _) => ScheduleSave();
        StateChanged += (_, _) => ScheduleSave();
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        var home = App.InitialPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = App.InitialPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents)) documents = home;
        _left.Restore(_session.Left, home); _right.Restore(_session.Right, documents);
        SetActive(_session.ActivePane == 1 && _settings.TwoPanes ? _right : _left);
        ApplyScale(AppSettings.ValidScale(_settings.TextScale), false);
        ApplyView(); BuildSidebar();
        _restoring = false; UpdateChrome();
        Status.Text = "フォルダーを選んで、はじめましょう。  Ctrl＋ホイールで文字を拡大できます";
    }

    private void SetActive(PaneView pane)
    {
        if (!_settings.TwoPanes && ReferenceEquals(pane, _right)) return;
        _active = pane; _left.SetActive(pane == _left); _right.SetActive(pane == _right); UpdateChrome();
    }
    private void UpdateChrome()
    {
        if (!IsInitialized || _updating) return;
        _updating = true;
        try
        {
            if (!Address.IsKeyboardFocusWithin) Address.Text = _active.CurrentPath;
            if (!FilterBox.IsKeyboardFocusWithin) FilterBox.Text = _active.Current?.FilterText ?? "";
            BackButton.IsEnabled = !_busy && _active.Current?.Back.Count > 0;
            ForwardButton.IsEnabled = !_busy && _active.Current?.Forward.Count > 0;
            var count = _active.SelectedEntries.Length;
            CopyButton.IsEnabled = MoveButton.IsEnabled = !_busy && count > 0;
            Status.Text = _active.Current?.IsLoading == true ? "読み込み中…" : count > 0 ? $"{count:N0} 件を選択" : $"{_active.Current?.List.Items.Count ?? 0:N0} 項目  ·  Ctrl＋ホイールで文字サイズを変更";
            Destination.Text = _settings.TwoPanes ? "コピー先: " + (Other == _right ? "右" : "左") + " › " + PaneView.FolderName(Other.CurrentPath) : "コピー先は実行時に選べます";
            Destination.ToolTip = _settings.TwoPanes ? Other.CurrentPath : "コピー・移動時にフォルダー選択を開きます";
        }
        finally { _updating = false; }
    }

    private void BuildSidebar()
    {
        Sidebar.Children.Clear();
        void Heading(string text) => Sidebar.Children.Add(new TextBlock { Text = text, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(11, 16, 0, 8) });
        void Place(string label, string path, string glyph)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), Margin = new Thickness(0, 0, 11, 0), Foreground = (Brush)FindResource("Muted") });
            content.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis });
            var button = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = path, Padding = new Thickness(11, 9, 4, 9), Margin = new Thickness(0, 1, 0, 1) };
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            button.Click += async (_, _) => { if (!_busy) await NavigateAsync(path); };
            Sidebar.Children.Add(button);
        }
        Heading("よく使う場所");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Place("ホーム", home, "\uE80F");
        foreach (var item in new[] { ("デスクトップ", Environment.SpecialFolder.DesktopDirectory, "\uE7F4"), ("ドキュメント", Environment.SpecialFolder.MyDocuments, "\uE8A5"), ("ピクチャ", Environment.SpecialFolder.MyPictures, "\uEB9F") })
        {
            var path = Environment.GetFolderPath(item.Item2); if (path.Length > 0) Place(item.Item1, path, item.Item3);
        }
        Place("ダウンロード", Path.Combine(home, "Downloads"), "\uE896");
        Heading("ブックマーク");
        foreach (var path in _session.Bookmarks ?? []) if (!string.IsNullOrWhiteSpace(path)) Place(PaneView.FolderName(path), path, "\uE734");
        var add = new Button { Content = "＋ 現在地を追加", Foreground = (Brush)FindResource("Accent") };
        add.Click += (_, _) =>
        {
            if (_busy || _active.CurrentPath.Length == 0) return;
            _session.Bookmarks ??= [];
            if (!_session.Bookmarks.Contains(_active.CurrentPath, StringComparer.OrdinalIgnoreCase)) _session.Bookmarks.Add(_active.CurrentPath);
            BuildSidebar(); ScheduleSave();
        }; Sidebar.Children.Add(add);
        Heading("この PC");
        foreach (var drive in DriveInfo.GetDrives()) Place(drive.Name, drive.RootDirectory.FullName, "\uEDA2");
        var history = new Button { Content = "閲覧・操作履歴", Margin = new Thickness(0, 20, 0, 5) };
        history.Click += (_, _) => ShowHistory(); Sidebar.Children.Add(history);
    }

    private async Task NavigateAsync(string path)
    {
        if (_busy) return;
        await _active.NavigateAsync(path);
        if (_active.Current?.Error.Length == 0)
            try { _history.RecordVisit(HistoryKind.FolderVisit, _active.CurrentPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = "履歴を保存できませんでした: " + ex.Message; }
        UpdateChrome();
    }
    private async Task OpenAsync(FileEntry entry)
    {
        if (_busy) return;
        if (entry.IsDirectory) { await NavigateAsync(entry.FullPath); return; }
        try
        {
            _history.RecordVisit(HistoryKind.FileOpen, entry.FullPath);
            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError("ファイルを開けませんでした", ex.Message); }
    }
    private async void AddressKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await NavigateAsync(Address.Text); _active.Current?.List.Focus(); } }
    private async void BackClick(object sender, RoutedEventArgs e) { if (!_busy) await _active.GoBackAsync(); }
    private async void ForwardClick(object sender, RoutedEventArgs e) { if (!_busy) await _active.GoBackAsync(true); }
    private async void UpClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try { if (Directory.GetParent(_active.CurrentPath) is { } parent) await NavigateAsync(parent.FullName); }
        catch (Exception ex) { ShowError("上のフォルダーを開けませんでした", ex.Message); }
    }
    private async void RefreshClick(object sender, RoutedEventArgs e) { if (!_busy) await _active.RefreshAsync(); }
    private void FilterChanged(object sender, TextChangedEventArgs e) { if (!_updating) { _active.SetFilter(FilterBox.Text); UpdateChrome(); } }

    internal void ApplyScale(double scale, bool save = true)
    {
        var old = AppSettings.ValidScale(_settings.TextScale);
        scale = AppSettings.ValidScale(scale); _settings.TextScale = scale;
        Application.Current.Resources["TextSize"] = 14d * scale / 100;
        _left.ScaleColumns(scale / old); _right.ScaleColumns(scale / old);
        // Newly created columns start at 100%; initial restoration scales them once.
        if (!save && scale != 100) { _left.ScaleColumns(scale / 100); _right.ScaleColumns(scale / 100); }
        FontButton.Content = $"Aa  {scale:0}%";
        SidebarColumn.Width = new GridLength(192 * Math.Min(scale / 100, 1.4));
        Status.Text = $"文字の大きさ {scale:0}%  ·  Ctrl+0 で標準に戻す";
        if (save) ScheduleSave();
    }
    private void FontClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var scale in new double[] { 80, 100, 110, 125, 150, 175, 200, 250 })
        {
            var item = new MenuItem { Header = scale == 100 ? "100%（標準）" : $"{scale:0}%", IsCheckable = true, IsChecked = Math.Abs(_settings.TextScale - scale) < .1 };
            item.Click += (_, _) => ApplyScale(scale); menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var custom = new MenuItem { Header = "数値で指定…" };
        custom.Click += (_, _) =>
        {
            var dialog = new ActionDialog(this, "文字の大きさ", "80～250% の範囲で指定できます。", "変更する", _settings.TextScale.ToString("0"));
            if (dialog.ShowDialog() == true)
            {
                if (double.TryParse(dialog.Value, out var value) && double.IsFinite(value) && value >= 80 && value <= 250) ApplyScale(value);
                else ShowError("文字の大きさ", "80～250 の数値を入力してください。");
            }
        }; menu.Items.Add(custom); OpenMenu(FontButton, menu);
    }
    private void WindowMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        ApplyScale(_settings.TextScale + (e.Delta > 0 ? 10 : -10)); e.Handled = true;
    }
    private void ViewClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var two in new[] { false, true })
        {
            var item = new MenuItem { Header = two ? "左右2画面" : "1画面", IsCheckable = true, IsChecked = _settings.TwoPanes == two };
            item.Click += (_, _) => { if (_busy) return; _settings.TwoPanes = two; ApplyView(); ScheduleSave(); }; menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var sidebar = new MenuItem { Header = "サイドバーを表示", IsCheckable = true, IsChecked = SidebarScroll.Visibility == Visibility.Visible };
        sidebar.Click += (_, _) => { SidebarScroll.Visibility = sidebar.IsChecked ? Visibility.Visible : Visibility.Collapsed; SidebarColumn.Width = sidebar.IsChecked ? new GridLength(192) : new GridLength(0); };
        menu.Items.Add(sidebar); OpenMenu(ViewButton, menu);
    }
    private void ApplyView()
    {
        _right.Visibility = Divider.Visibility = _settings.TwoPanes ? Visibility.Visible : Visibility.Collapsed;
        RightColumn.Width = _settings.TwoPanes ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        DividerColumn.Width = new GridLength(_settings.TwoPanes ? 10 : 0);
        ViewButton.Content = _settings.TwoPanes ? "2画面 ▾" : "1画面 ▾";
        if (!_settings.TwoPanes) SetActive(_left); UpdateChrome();
    }
    private static void OpenMenu(FrameworkElement target, ContextMenu menu) { menu.PlacementTarget = target; menu.IsOpen = true; }

    private ContextMenu BuildFileMenu()
    {
        var menu = new ContextMenu();
        void Item(string name, Action action, bool enabled = true) { var item = new MenuItem { Header = name, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        var selected = _active.SelectedEntries;
        Item("開く", () => { if (_active.SelectedEntries.FirstOrDefault() is { } entry) _ = OpenAsync(entry); }, selected.Length == 1);
        Item("新しいタブで開く", () => { if (_active.SelectedEntries.FirstOrDefault() is { IsDirectory: true } entry) _active.AddTab(entry.FullPath); }, selected is [{ IsDirectory: true }]);
        menu.Items.Add(new Separator());
        Item("コピー    Ctrl+C", () => PutClipboard(false), selected.Length > 0);
        Item("切り取り    Ctrl+X", () => PutClipboard(true), selected.Length > 0);
        Item("貼り付け    Ctrl+V", () => _ = PasteAsync());
        Item("名前変更    F2", Rename, selected.Length == 1);
        Item("ごみ箱へ    Delete", () => _ = DeleteAsync(), selected.Length > 0);
        menu.Items.Add(new Separator());
        Item("新規フォルダー", NewFolder);
        Item("現在のパスをコピー", () => { try { Clipboard.SetText(_active.CurrentPath); } catch (Exception ex) { ShowError("コピーできませんでした", ex.Message); } });
        return menu;
    }
    private void MoreClick(object sender, RoutedEventArgs e) => OpenMenu((Button)sender, BuildFileMenu());
    private void NewFolderClick(object sender, RoutedEventArgs e) => NewFolder();
    private async void NewFolder()
    {
        if (_busy) return;
        var parent = _active.CurrentPath;
        var dialog = new ActionDialog(this, "新規フォルダー", "作成先: " + parent, "作成する", "新しいフォルダー");
        if (dialog.ShowDialog() != true) return;
        try { _actions.CreateFolder(parent, dialog.Value); await _active.RefreshAsync(); Status.Text = "フォルダーを作成しました"; }
        catch (Exception ex) { ShowError("フォルダーを作成できませんでした", ex.Message); }
    }
    private async void Rename()
    {
        if (_busy || _active.SelectedEntries is not [var entry]) return;
        var dialog = new ActionDialog(this, "名前変更", "対象: " + entry.FullPath, "変更する", entry.Name);
        if (dialog.ShowDialog() != true || dialog.Value == entry.Name) return;
        try { _actions.Rename(entry.FullPath, dialog.Value); await _active.RefreshAsync(); Status.Text = "名前を変更しました"; }
        catch (Exception ex) { ShowError("名前を変更できませんでした", ex.Message); }
    }
    private async void CopyClick(object sender, RoutedEventArgs e) => await TransferAsync(false);
    private async void MoveClick(object sender, RoutedEventArgs e) => await TransferAsync(true);
    private async Task TransferAsync(bool move)
    {
        if (_busy) return;
        var selected = _active.SelectedEntries;
        if (selected.Length == 0) return;
        string? destination = _settings.TwoPanes && Directory.Exists(Other.CurrentPath) ? Other.CurrentPath : null;
        if (destination is null)
        {
            var picker = new OpenFolderDialog { Title = move ? "移動先を選ぶ" : "コピー先を選ぶ" };
            if (picker.ShowDialog(this) != true) return; destination = picker.FolderName;
        }
        await RunFilesAsync(selected, destination, move ? "移動" : "コピー");
    }
    private async Task DeleteAsync()
    {
        if (!_busy && _active.SelectedEntries.Length > 0) await RunFilesAsync(_active.SelectedEntries, null, "ごみ箱へ送る");
    }
    private async Task RunFilesAsync(FileEntry[] selected, string? destination, string action)
    {
        if (_busy || selected.Length == 0) return;
        var names = string.Join("\n", selected.Take(8).Select(x => "・" + x.Name));
        if (selected.Length > 8) names += $"\nほか {selected.Length - 8} 件";
        var description = $"{selected.Length} 件を{action}操作です。\n\n{names}\n\n元: {Path.GetDirectoryName(selected[0].FullPath)}";
        description += destination is null ? "\n\nごみ箱から元に戻せます。" : "\n先: " + destination + "\n\n同名の項目は上書きしません。";
        var dialog = new ActionDialog(this, action, description, action);
        if (dialog.ShowDialog() != true) return;
        _operation = new CancellationTokenSource(); var cancellation = _operation.Token;
        SetBusy(true);
        var completed = 0; var failures = new List<string>(); var cancelled = false;
        var progress = new Progress<string>(name => ProgressText.Text = $"{action}中 {completed}/{selected.Length} 件  ·  {name}");
        try
        {
            foreach (var entry in selected)
            {
                cancellation.ThrowIfCancellationRequested();
                ProgressText.Text = $"{action}中 {completed}/{selected.Length} 件  ·  {entry.Name}";
                try
                {
                    await Task.Run(() =>
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (destination is null) _actions.DeleteToRecycleBin(entry.FullPath);
                        else if (action == "移動") _actions.Move(entry.FullPath, destination);
                        else _actions.Copy(entry.FullPath, destination, cancellation, progress);
                    }, cancellation);
                    completed++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { failures.Add(entry.Name + ": " + ex.Message); }
            }
        }
        catch (OperationCanceledException) { cancelled = true; }
        finally
        {
            _operation.Dispose(); _operation = null; SetBusy(false);
            await Task.WhenAll(_left.RefreshAsync(), _right.RefreshAsync());
        }
        Status.Text = $"{completed} 件完了" + (cancelled ? " · 中止しました" : "") + (failures.Count > 0 ? $" · {failures.Count} 件失敗" : "");
        if (cancelled || failures.Count > 0)
            ShowError(action + "の結果", $"{completed} 件完了。" + (cancelled ? "中止しました。\n" : "\n") + string.Join("\n", failures.Take(8)) + (destination is not null ? "\n\n途中までコピーされた項目がコピー先に残る場合があります。内容を確認してください。" : ""));
    }
    private void SetBusy(bool busy)
    {
        _busy = busy;
        _left.IsWorking = _right.IsWorking = busy;
        _left.IsEnabled = _right.IsEnabled = Address.IsEnabled = Sidebar.IsEnabled = FilterBox.IsEnabled = !busy;
        ProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; UpdateChrome();
    }
    private void CancelClick(object sender, RoutedEventArgs e) { _operation?.Cancel(); ProgressText.Text = "中止しています…"; }

    private void PutClipboard(bool cut)
    {
        if (_busy || _active.SelectedEntries.Length == 0) return;
        try
        {
            var paths = new StringCollection(); paths.AddRange(_active.SelectedEntries.Select(x => x.FullPath).ToArray());
            var data = new DataObject(); data.SetFileDropList(paths);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 1)));
            Clipboard.SetDataObject(data, true); Status.Text = cut ? "切り取りました。貼り付け先を開いて Ctrl+V" : "コピーしました。貼り付け先を開いて Ctrl+V";
        }
        catch (Exception ex) { ShowError("クリップボードにコピーできませんでした", ex.Message); }
    }
    private async Task PasteAsync()
    {
        if (_busy) return;
        try
        {
            if (!Clipboard.ContainsFileDropList()) { Status.Text = "貼り付けるファイルがありません"; return; }
            var entries = Clipboard.GetFileDropList().Cast<string>().Select(path => new FileEntry(path, Path.GetFileName(path), Directory.Exists(path), null, DateTime.Now)).ToArray();
            var move = Clipboard.GetData("Preferred DropEffect") is MemoryStream stream && BitConverter.ToInt32(stream.ToArray()) == 2;
            await RunFilesAsync(entries, _active.CurrentPath, move ? "移動" : "コピー");
        }
        catch (Exception ex) { ShowError("貼り付けできませんでした", ex.Message); }
    }

    private void ShowError(string title, string text) { new ActionDialog(this, title, text, "閉じる").ShowDialog(); }
    private void ShowHistory()
    {
        try
        {
            var items = _history.ReadLatest().Take(100).Select(x => $"{x.Timestamp:MM/dd HH:mm}  {x.Kind switch { HistoryKind.FolderVisit => "閲覧", HistoryKind.FileOpen => "開く", HistoryKind.Copy => "コピー", HistoryKind.Move => "移動", HistoryKind.Rename => "名前変更", HistoryKind.Delete => "ごみ箱", _ => "フォルダー作成" }}  {x.Status switch { HistoryStatus.Failed => "失敗", HistoryStatus.Started => "要確認", _ => "" }}\n{x.SourcePath}" + (x.DestinationPath is not null ? " → " + x.DestinationPath : ""));
            ShowError("閲覧・操作履歴（最新100件）", string.Join("\n\n", items) is { Length: > 0 } text ? text : "履歴はまだありません。");
        }
        catch (Exception ex) { ShowError("履歴を開けませんでした", ex.Message); }
    }
    private void HelpClick(object sender, RoutedEventArgs e) => ShowError("nikotFiler の使い方", "フォルダーをダブルクリックして開きます。\n\n＋ / Ctrl+T　タブを追加\n× / 中クリック / Ctrl+W　タブを閉じる\nCtrl+Tab　次のタブ\nF6　左右の画面を切り替える\nCtrl+L　パスを入力\nCtrl+F　名前を絞り込む\nCtrl+C / X / V　コピー / 切り取り / 貼り付け\nF2　名前変更　　Delete　ごみ箱へ\nF5　再読込　　Alt+↑　上のフォルダー\nAlt+← / →　戻る / 進む\n\nAa / Ctrl+ホイール　文字の大きさ\nCtrl+0　文字を標準に戻す\n\nプレビュー版 0.1.0");

    private async void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (_busy) { if (e.Key == Key.Escape) _operation?.Cancel(); return; }
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && e.Key == Key.L) { Address.Focus(); Address.SelectAll(); }
        else if (ctrl && e.Key == Key.F) { FilterBox.Focus(); FilterBox.SelectAll(); }
        else if (ctrl && e.Key == Key.T) _active.AddTab(_active.CurrentPath);
        else if (ctrl && e.Key == Key.W) _active.CloseCurrent();
        else if (ctrl && e.Key == Key.Tab) _active.NextTab();
        else if (ctrl && e.Key is Key.D0 or Key.NumPad0) ApplyScale(100);
        else if (ctrl && e.Key is Key.OemPlus or Key.Add or Key.OemSemicolon) ApplyScale(_settings.TextScale + 10);
        else if (ctrl && e.Key is Key.OemMinus or Key.Subtract) ApplyScale(_settings.TextScale - 10);
        else if (e.Key == Key.F6 && _settings.TwoPanes) { SetActive(Other); _active.Current?.List.Focus(); }
        else if (e.Key == Key.F5) await _active.RefreshAsync();
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.Left) await _active.GoBackAsync();
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.Right) await _active.GoBackAsync(true);
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.Up) UpClick(sender, e);
        else if (e.OriginalSource is TextBox) return;
        else if (ctrl && e.Key == Key.C) PutClipboard(false);
        else if (ctrl && e.Key == Key.X) PutClipboard(true);
        else if (ctrl && e.Key == Key.V) await PasteAsync();
        else if (ctrl && e.Key == Key.A && _active.Current is { } tab) tab.List.SelectAll();
        else if (e.Key == Key.F2) Rename();
        else if (e.Key == Key.Delete) await DeleteAsync();
        else if (e.Key == Key.Back) await _active.GoBackAsync();
        else return;
        e.Handled = true;
    }

    private void ScheduleSave() { if (_restoring) return; _saveTimer.Stop(); _saveTimer.Start(); }
    private void SaveState()
    {
        if (_restoring) return;
        try
        {
            _settings.Width = RestoreBounds.Width; _settings.Height = RestoreBounds.Height; _settings.Maximized = WindowState == WindowState.Maximized;
            StateStore.Save(SettingsPath, _settings);
            _session.Left = _left.Snapshot(); _session.Right = _right.Snapshot(); _session.ActivePane = _active == _right ? 1 : 0;
            StateStore.Save(SessionPath, _session);
        }
        catch (Exception ex) { Status.Text = "設定を保存できませんでした: " + ex.Message; }
    }
    private void WindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_busy) { e.Cancel = true; _operation?.Cancel(); Status.Text = "処理を中止しています。完了後に閉じてください"; return; }
        _saveTimer.Stop(); SaveState(); _left.CancelLoads(); _right.CancelLoads();
    }
}
