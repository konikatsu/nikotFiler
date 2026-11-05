using System.IO;
using System.Windows;
using System.Windows.Controls;
using NikotFiler.Core;

namespace NikotFiler.App;

internal sealed class CopyNameDialog : Window
{
    private readonly string _destinationDirectory;
    private readonly string[] _proposedPaths;
    private readonly Dictionary<int, TextBox> _names = [];
    private readonly TextBlock _error;
    public string[] DestinationPaths { get; private set; }

    public CopyNameDialog(Window? owner, FileEntry[] sources, string destinationDirectory, string[] proposedPaths)
    {
        Owner = owner;
        Title = "コピー先の名前";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = Math.Min(720, SystemParameters.WorkArea.Width);
        MinWidth = 440;
        MaxHeight = SystemParameters.WorkArea.Height * .85;
        SizeToContent = SizeToContent.Height;
        ShowInTaskbar = false;
        Background = System.Windows.Media.Brushes.White;
        _destinationDirectory = destinationDirectory;
        _proposedPaths = (string[])proposedPaths.Clone();
        DestinationPaths = (string[])proposedPaths.Clone();

        var layout = new DockPanel { Margin = new Thickness(26), Background = System.Windows.Media.Brushes.White };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "コピー先の名前", FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock
        {
            Text = "同名のファイルがあります。保存する名前を確認・変更してください。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 12)
        });
        heading.Children.Add(new TextBlock
        {
            Text = "保存先  " + destinationDirectory,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = destinationDirectory,
            Margin = new Thickness(0, 0, 0, 18)
        });
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick, Visibility = Visibility.Collapsed };
        footer.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "やめる", IsCancel = true });
        var confirm = new Button { Content = $"{sources.Length} 件をコピー", IsDefault = true, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        confirm.Click += (_, _) => { if (ValidateDestinations()) DialogResult = true; };
        buttons.Children.Add(confirm);
        footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);

        var rows = new StackPanel();
        for (var i = 0; i < sources.Length; i++)
        {
            rows.Children.Add(new TextBlock
            {
                Text = "元  " + sources[i].Name,
                TextWrapping = TextWrapping.Wrap,
                ToolTip = sources[i].FullPath
            });
            var name = Path.GetFileName(proposedPaths[i]);
            if (name != sources[i].Name)
            {
                rows.Children.Add(new TextBlock { Text = "保存する名前", Margin = new Thickness(0, 6, 0, 4) });
                var input = new TextBox { Text = name, Margin = new Thickness(0, 0, 0, 18) };
                System.Windows.Automation.AutomationProperties.SetName(input, sources[i].Name + " のコピー名");
                _names.Add(i, input);
                rows.Children.Add(input);
            }
            else rows.Children.Add(new TextBlock { Text = "先  " + name, Margin = new Thickness(0, 6, 0, 18), TextWrapping = TextWrapping.Wrap });
        }
        layout.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = layout;
        Loaded += (_, _) =>
        {
            if (_names.Values.FirstOrDefault() is not { } input) return;
            input.Focus();
            input.Select(0, Path.GetFileNameWithoutExtension(input.Text).Length);
        };
    }

    internal bool ValidateDestinations()
    {
        var paths = (string[])_proposedPaths.Clone();
        foreach (var (index, input) in _names)
        {
            var name = input.Text;
            if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 255 ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
                return Reject(input, "使用できるファイル名を入力してください。");

            var deviceName = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (deviceName is "CON" or "PRN" or "AUX" or "NUL" ||
                (deviceName.Length == 4 && (deviceName.StartsWith("COM") || deviceName.StartsWith("LPT")) &&
                 "123456789¹²³".Contains(deviceName[3])))
                return Reject(input, "この名前は Windows で予約されています。別の名前を入力してください。");

            paths[index] = Path.Combine(_destinationDirectory, name);
            if (Path.Exists(paths[index])) return Reject(input, "この名前はすでに使われています。別の名前を入力してください。");
        }
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < paths.Length; i++)
        {
            if (unique.Add(paths[i])) continue;
            var input = _names.GetValueOrDefault(i) ?? _names.Values.First();
            return Reject(input, "コピーする項目どうしで名前が重複しています。別の名前を入力してください。");
        }
        DestinationPaths = paths;
        _error.Visibility = Visibility.Collapsed;
        return true;
    }

    private bool Reject(TextBox input, string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
        input.Focus();
        return false;
    }
}
