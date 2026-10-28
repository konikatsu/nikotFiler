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
        for (var i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--data-dir" && i + 1 < e.Args.Length) DataDirectory = Path.GetFullPath(e.Args[++i]);
            else if (e.Args[i] == "--folder" && i + 1 < e.Args.Length) InitialPath = Path.GetFullPath(e.Args[++i]);
        }
        new MainWindow().Show();
    }
}
