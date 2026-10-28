using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using NikotFiler.Core;

namespace NikotFiler.App;

public sealed class HistoryWindow : Window
{
    private readonly ListView _list = new() { SelectionMode = SelectionMode.Single };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly Button _open = new() { Content = "この場所を開く", IsDefault = true };
    private bool _resolving;
    private bool _closed;
    public string? SelectedFolder { get; private set; }

    public HistoryWindow(Window? owner, IReadOnlyList<HistoryEntry> entries)
    {
        if (owner is not null) Owner = owner;
        Title = "履歴から開く"; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = Math.Min(980, SystemParameters.WorkArea.Width); Height = Math.Min(640, SystemParameters.WorkArea.Height * .85);
        MinWidth = 540; MinHeight = 350; ShowInTaskbar = false;
        var layout = new DockPanel { Margin = new Thickness(22) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = "履歴から開く", FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "項目を選んで Enter、またはダブルクリックすると、その場所へ移動します。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
        var search = new TextBox { ToolTip = "名前・パス・操作の種類で履歴を絞り込み" };
        System.Windows.Automation.AutomationProperties.SetName(search, "履歴を絞り込み");
        header.Children.Add(new TextBlock { Text = "履歴を絞り込み", Margin = new Thickness(0, 0, 0, 5) }); header.Children.Add(search);
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(_message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var close = new Button { Content = "閉じる", IsCancel = true };
        _open.Style = (Style)Application.Current.FindResource("PrimaryButton");
        buttons.Children.Add(close); buttons.Children.Add(_open); footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var grid = new Grid(); grid.Children.Add(_list); grid.Children.Add(_empty); layout.Children.Add(grid);
        Content = layout;
        var scale = Application.Current.Resources["TextSize"] is double size ? size / 14 : 1;
        var columns = new GridView();
        columns.Columns.Add(new GridViewColumn { Header = "日時", Width = 155 * scale, DisplayMemberBinding = new Binding(nameof(HistoryItem.When)) });
        columns.Columns.Add(new GridViewColumn { Header = "種類", Width = 110 * scale, DisplayMemberBinding = new Binding(nameof(HistoryItem.KindText)) });
        columns.Columns.Add(new GridViewColumn { Header = "場所", Width = 580 * scale, DisplayMemberBinding = new Binding(nameof(HistoryItem.PathText)) });
        _list.View = columns;
        _list.ItemsSource = entries.Select(entry => new HistoryItem(entry)).ToArray();
        _list.SelectionChanged += (_, _) => _open.IsEnabled = !_resolving && _list.SelectedItem is HistoryItem;
        search.TextChanged += (_, _) =>
        {
            var query = search.Text.Trim();
            CollectionViewSource.GetDefaultView(_list.ItemsSource).Filter = item => item is HistoryItem row &&
                (row.PathText.Contains(query, StringComparison.CurrentCultureIgnoreCase) || row.KindText.Contains(query, StringComparison.CurrentCultureIgnoreCase));
            _message.Text = ""; UpdateSelection();
        };
        _list.MouseDoubleClick += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(_list, e.OriginalSource as DependencyObject) is ListViewItem) OpenSelected();
        };
        _open.Click += (_, _) => OpenSelected();
        Closed += (_, _) => _closed = true;
        Loaded += (_, _) => search.Focus();
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        _list.SelectedIndex = _list.Items.Count > 0 ? 0 : -1;
        _open.IsEnabled = !_resolving && _list.SelectedItem is HistoryItem;
        _empty.Text = _list.Items.Count == 0 ? "表示できる履歴がありません" : "";
    }

    private async void OpenSelected()
    {
        if (_resolving || _list.SelectedItem is not HistoryItem row) return;
        _resolving = true; _open.IsEnabled = false; _message.Text = "場所を確認しています…";
        try
        {
            var folder = await Task.Run(() =>
            {
                var path = HistoryNavigation.GetFolder(row.Entry);
                if (!Directory.Exists(path)) throw new DirectoryNotFoundException("この場所は見つからないか、アクセスできません。");
                return path;
            });
            if (_closed) return;
            SelectedFolder = folder; DialogResult = true;
        }
        catch (Exception ex)
        {
            if (!_closed) _message.Text = "開けませんでした: " + ex.Message + "\n別の履歴を選んでください。";
        }
        finally
        {
            _resolving = false;
            if (!_closed) _open.IsEnabled = _list.SelectedItem is HistoryItem;
        }
    }
}

public sealed record HistoryItem(HistoryEntry Entry)
{
    public string When => Entry.Timestamp.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
    public string KindText => (Entry.Kind switch
    {
        HistoryKind.FolderVisit => "フォルダー", HistoryKind.FileOpen => "ファイル",
        HistoryKind.Copy => "コピー", HistoryKind.Move => "移動", HistoryKind.Rename => "名前変更",
        HistoryKind.Delete => "ごみ箱", _ => "フォルダー作成"
    }) + (Entry.Status switch { HistoryStatus.Failed => "（失敗）", HistoryStatus.Started => "（要確認）", _ => "" });
    public string PathText => Entry.SourcePath + (Entry.DestinationPath is { Length: > 0 } destination ? " → " + destination : "");
}
