using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NikotFiler.App;

internal sealed class SettingsWindow : Window
{
    private readonly DefaultFileManager _registration;
    private readonly string _dataDirectory;
    private readonly TextBlock _status;
    private readonly Button _enable;
    private readonly Button _restore;

    public SettingsWindow(Window? owner, DefaultFileManager registration, string dataDirectory)
    {
        Owner = owner;
        Title = "設定";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = Math.Min(720, SystemParameters.WorkArea.Width);
        MinWidth = 440;
        MaxHeight = SystemParameters.WorkArea.Height * .85;
        SizeToContent = SizeToContent.Height;
        ShowInTaskbar = false;
        _registration = registration;
        _dataDirectory = dataDirectory;

        var layout = new DockPanel { Margin = new Thickness(26), Background = Brushes.White };
        var close = new Button { Content = "閉じる", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom);
        layout.Children.Add(close);
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "既定のファイラー", FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock
        {
            Text = "フォルダーやドライブを開くときに nikotFiler を使います。この Windows ユーザーの設定に適用されます。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 12)
        });
        content.Children.Add(new TextBlock
        {
            Text = "Codex など、Windows にフォルダーを開くよう依頼するアプリにも適用されます。Win+E や「ファイルの場所を開く」など、エクスプローラーを直接呼び出す操作は Windows 側に残ります。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        content.Children.Add(_status);
        var buttons = new WrapPanel();
        _enable = new Button { Content = "既定のファイラーとして使う", Style = (Style)Application.Current.FindResource("PrimaryButton") };
        _restore = new Button { Content = "元の設定に戻す" };
        _enable.Click += (_, _) => ChangeRegistration(true);
        _restore.Click += (_, _) => ChangeRegistration(false);
        buttons.Children.Add(_enable);
        buttons.Children.Add(_restore);
        content.Children.Add(buttons);
        content.Children.Add(new TextBlock
        {
            Text = "登録時に起動用アプリと元の設定を、このユーザーの nikotFiler 保存領域に保管します。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("Muted"),
            Margin = new Thickness(0, 16, 0, 0)
        });
        layout.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = layout;
        RefreshStatus();
    }

    private void ChangeRegistration(bool enable)
    {
        try
        {
            if (enable)
            {
                var executable = DefaultFileManager.InstallApplication(AppContext.BaseDirectory, _dataDirectory);
                _registration.Enable(executable);
                RefreshStatus();
                _status.Text = "nikotFiler を既定のファイラーに設定しました。";
            }
            else
            {
                var preserved = _registration.Restore();
                RefreshStatus();
                _status.Text = preserved == 0 ? "元の設定に戻しました。" : "元の設定に戻しました。登録後に他のアプリが変更した項目は、その設定を引き継ぎました。";
            }
        }
        catch (Exception ex)
        {
            _status.Text = "設定を変更できませんでした。\n" + ex.Message;
            _enable.IsEnabled = !_registration.HasBackup;
            _restore.IsEnabled = _registration.HasBackup;
        }
    }

    private void RefreshStatus()
    {
        _enable.IsEnabled = !_registration.HasBackup;
        _restore.IsEnabled = _registration.HasBackup;
        try
        {
            _status.Text = _registration.IsEnabled ? "現在は nikotFiler が登録されています。" :
                _registration.HasBackup ? "登録の一部が変更されています。「元の設定に戻す」で登録を解除できます。" : "Windows の現在の設定を使っています。";
        }
        catch (Exception ex) { _status.Text = "登録の状態を確認できませんでした。\n" + ex.Message; }
    }
}
