using System.IO;
using System.Windows;
using System.Diagnostics;

namespace NikotFiler.App;

public partial class App : Application
{
    internal static string UserDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nikotFiler");
    public static string DataDirectory { get; private set; } = UserDataDirectory;
    public static string? InitialPath { get; private set; }
    private static string? _shellNamespace;
    private InstanceRelay? _relay;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            ConfigureArguments(e.Args);
            if (_shellNamespace is not null)
            {
                var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
                start.ArgumentList.Add(_shellNamespace);
                Process.Start(start);
                Shutdown();
                return;
            }
            _relay = new InstanceRelay(DataDirectory);
            if (!_relay.IsPrimary)
            {
                await _relay.ForwardAsync(InitialPath);
                Shutdown();
                return;
            }
            var window = new MainWindow();
            window.Show();
            _ = _relay.ListenAsync(path => Dispatcher.InvokeAsync(() =>
            {
                if (path is null) { if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); }
                else window.OpenFolder(path);
            }).Task);
        }
        catch (Exception ex)
        {
            MessageBox.Show("nikotFiler を開けませんでした。\n" + ex.Message, "nikotFiler", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _relay?.Dispose();
        base.OnExit(e);
    }

    internal static void ConfigureArguments(string[] args)
    {
        InitialPath = null;
        _shellNamespace = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--data-dir" && i + 1 < args.Length) DataDirectory = Path.GetFullPath(args[++i]);
            else if (args[i] == "--folder" && i + 1 < args.Length) InitialPath = Path.GetFullPath(args[++i]);
            else if (args[i] == "--shell-folder" && i + 1 < args.Length)
            {
                var path = args[++i];
                if (path.StartsWith("::{", StringComparison.Ordinal) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    _shellNamespace = path.EndsWith(@"\.") ? path[..^2] : path;
                else InitialPath = Path.GetFullPath(path);
            }
        }
    }
}
