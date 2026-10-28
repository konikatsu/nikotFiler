using System.IO;
using System.Windows;

namespace NikotFiler.App;

public partial class App : Application
{
    public static string DataDirectory { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nikotFiler");
    public static string? InitialPath { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigureArguments(e.Args);
        new MainWindow().Show();
    }
    internal static void ConfigureArguments(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--data-dir" && i + 1 < args.Length) DataDirectory = Path.GetFullPath(args[++i]);
            else if (args[i] == "--folder" && i + 1 < args.Length) InitialPath = Path.GetFullPath(args[++i]);
        }
    }
}
