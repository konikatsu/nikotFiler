using System.Windows;
using System.Windows.Controls;

namespace NikotFiler.App;

public sealed class ActionDialog : Window
{
    private readonly TextBox? _input;
    public string Value => _input?.Text.Trim() ?? "";

    public ActionDialog(Window owner, string title, string description, string action, string? initialValue = null)
    {
        Owner = owner; Title = title; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.CanResize;
        MaxWidth = Math.Min(900, SystemParameters.WorkArea.Width); MaxHeight = SystemParameters.WorkArea.Height * .85;
        MinWidth = 440; ShowInTaskbar = false;
        var content = new StackPanel { Margin = new Thickness(26), MaxWidth = 680 };
        content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        if (initialValue is not null)
        {
            _input = new TextBox { Text = initialValue, MinWidth = 340, Margin = new Thickness(0, 0, 0, 18) };
            System.Windows.Automation.AutomationProperties.SetName(_input, "名前"); content.Children.Add(_input);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "やめる", IsCancel = true, IsDefault = initialValue is null };
        var confirm = new Button { Content = action, IsDefault = initialValue is not null, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        confirm.Click += (_, _) => { if (_input is null || Value.Length > 0) DialogResult = true; else _input.Focus(); };
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); content.Children.Add(buttons);
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => { if (_input is not null) { _input.Focus(); _input.SelectAll(); } else cancel.Focus(); };
    }
}
