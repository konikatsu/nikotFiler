using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NikotFiler.App;

internal static class IntegrationChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var fixture = @"Software\nikotFiler-tests\" + Guid.NewGuid().ToString("N");
        using var classes = Registry.CurrentUser.CreateSubKey(fixture);
        try
        {
            using (var old = classes.CreateSubKey(@"Directory\shell")) old.SetValue("", "previousFiler");
            using (var old = classes.CreateSubKey(@"Folder\shell\explore\command"))
            {
                old.SetValue("", @"%SystemRoot%\explorer.exe", RegistryValueKind.ExpandString);
                old.SetValue("DelegateExecute", "previousDelegate");
                old.SetValue("OtherValue", new byte[] { 1, 2, 3 });
            }
            var before = Snapshot(classes);
            var backup = Path.Combine(root, "integration", "backup.json");
            var registration = new DefaultFileManager(classes, backup);
            check(!registration.IsEnabled && !registration.HasBackup, "default filer registration starts disabled");
            var executable = Path.Combine(AppContext.BaseDirectory, "NikotFiler.Tests.exe");
            registration.Enable(executable);
            check(registration.IsEnabled && File.Exists(backup), "registration backs up prior values and installs all folder handlers");

            var dummyFolder = Path.Combine(root, "shell folder with spaces");
            Directory.CreateDirectory(dummyFolder);
            CheckShellCommand(classes, "Directory", null, dummyFolder, root, check);
            CheckShellCommand(classes, "Directory", "open", dummyFolder, root, check);
            CheckShellCommand(classes, "Folder", "explore", dummyFolder, root, check);
            CheckShellCommand(classes, "Drive", null, dummyFolder, root, check);
            var refused = false;
            try { registration.Enable(executable); } catch (IOException) { refused = true; }
            check(refused && registration.IsEnabled, "enabling twice preserves the original backup");
            check(registration.Restore() == 0 && !registration.HasBackup && Snapshot(classes) == before,
                "restoring exactly recovers prior values, types, and previously missing registry keys");

            registration.Enable(executable);
            using (var other = classes.CreateSubKey(@"Drive\shell")) other.SetValue("", "laterFiler");
            check(registration.Restore() == 1, "restore detects a later third-party change");
            using (var other = classes.OpenSubKey(@"Drive\shell"))
                check(Equals(other!.GetValue(""), "laterFiler"), "restore preserves a later third-party default filer");
            check(registration.Restore() == 0, "restoring an already disabled registration is harmless");
            registration.Enable(executable);
            using (var other = classes.CreateSubKey(@"Folder\shell\explore\command")) other.SetValue("", "laterCommand");
            check(registration.Restore() == 2, "restore preserves a later replacement of a paired command handler");
            using (var other = classes.OpenSubKey(@"Folder\shell\explore\command"))
                check(Equals(other!.GetValue(""), "laterCommand") && Equals(other.GetValue("DelegateExecute"), ""),
                    "restore does not reactivate an old delegate over a later application's command");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(fixture, false); }

        App.ConfigureArguments(["--shell-folder", @"C:\."]);
        check(App.InitialPath == @"C:\", "shell launch preserves drive roots");
        App.ConfigureArguments(["--shell-folder", @"\\dummy-server\share\."]);
        check(App.InitialPath?.TrimEnd('\\') == @"\\dummy-server\share", "shell launch parses UNC roots without accessing the network");
        App.ConfigureArguments([]);

        var profile = Path.Combine(root, "relay-profile");
        using var primary = new InstanceRelay(profile);
        using var secondary = new InstanceRelay(profile);
        check(primary.IsPrimary && !secondary.IsPrimary, "one instance owns a profile");
        var received = new ConcurrentQueue<string?>();
        var listener = Task.Run(() => primary.ListenAsync(path => { received.Enqueue(path); return Task.CompletedTask; }));
        Task.Run(async () =>
        {
            await secondary.ForwardAsync(root);
            await secondary.ForwardAsync(null);
        }).GetAwaiter().GetResult();
        check(received.TryDequeue(out var opened) && opened == root && received.TryDequeue(out var activation) && activation is null,
            "later launches forward folders and activation requests to the existing instance");
    }

    public static void RenderSettings(string root, Action<bool, string> check)
    {
        var fixture = @"Software\nikotFiler-tests\" + Guid.NewGuid().ToString("N");
        using var classes = Registry.CurrentUser.CreateSubKey(fixture);
        try
        {
            var profile = Path.Combine(root, "settings-profile");
            App.ConfigureArguments(["--data-dir", profile]);
            var registration = new DefaultFileManager(classes, Path.Combine(profile, "integration", "backup.json"));
            var settings = new SettingsWindow(null, registration, profile);
            var content = (DockPanel)settings.Content;
            var scroll = content.Children.OfType<ScrollViewer>().Single();
            var panel = (StackPanel)scroll.Content;
            var buttons = panel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().ToArray();
            check(buttons[0].IsEnabled && !buttons[1].IsEnabled, "settings exposes enable and restore actions with current state");
            buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(registration.IsEnabled && !buttons[0].IsEnabled && buttons[1].IsEnabled,
                "the settings enable button installs the package and registers the default filer");
            using (var command = classes.OpenSubKey(@"Directory\shell\nikotFiler\command"))
                check(command!.GetValue("")!.ToString()!.Contains(Path.Combine(profile, "integration", "app")),
                    "registration uses a private installed application copy");
            var screenshots = Path.Combine(Environment.CurrentDirectory, "artifacts", "ui-checks");
            Directory.CreateDirectory(screenshots);
            foreach (var scale in new[] { 100, 200 })
            {
                Application.Current.Resources["TextSize"] = 14d * scale / 100;
                content.Measure(new Size(720, 680)); content.Arrange(new Rect(0, 0, 720, 680)); content.UpdateLayout();
                var image = new RenderTargetBitmap(720, 680, 96, 96, PixelFormats.Pbgra32); image.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = File.Create(Path.Combine(screenshots, $"nikotFiler-settings-{scale}.png")); encoder.Save(output);
            }
            Application.Current.Resources["TextSize"] = 14d;
            buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!registration.HasBackup && classes.SubKeyCount == 0 && buttons[0].IsEnabled,
                "the settings restore button removes its registration and resets its controls");
            settings.Close();
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(fixture, false); }
    }

    private static string Snapshot(RegistryKey key)
    {
        var values = key.GetValueNames().Order().Select(name => new
        {
            name,
            kind = key.GetValueKind(name),
            value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
        });
        var children = key.GetSubKeyNames().Order().Select(name =>
        {
            using var child = key.OpenSubKey(name);
            return new { name, value = Snapshot(child!) };
        });
        return JsonSerializer.Serialize(new { values, children });
    }

    private static void CheckShellCommand(RegistryKey classes, string type, string? verb, string folder, string root, Action<bool, string> check)
    {
        using var typeKey = classes.OpenSubKey(type);
        var result = Path.Combine(root, "shell-result.txt");
        File.Delete(result);
        var previous = Environment.GetEnvironmentVariable("NIKOTFILER_TEST_SHELL_RESULT");
        Environment.SetEnvironmentVariable("NIKOTFILER_TEST_SHELL_RESULT", result);
        try
        {
            var info = new ShellExecuteInfo
            {
                Size = Marshal.SizeOf<ShellExecuteInfo>(),
                Mask = 0x00000143, // CLASSKEY | NOCLOSEPROCESS | NOASYNC; only the isolated test key is used.
                File = folder,
                Verb = verb,
                ClassKey = typeKey!.Handle.DangerousGetHandle(),
                Show = 0
            };
            if (!ShellExecuteEx(ref info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (info.Process == IntPtr.Zero || WaitForSingleObject(info.Process, 10000) != 0)
                    throw new TimeoutException("The isolated shell handler did not finish.");
            }
            finally { if (info.Process != IntPtr.Zero) CloseHandle(info.Process); }
            check(File.Exists(result) && Path.GetFullPath(File.ReadAllText(result)) == folder,
                $"Windows launches the registered {type}/{verb ?? "default"} handler with the exact folder argument");
        }
        finally { Environment.SetEnvironmentVariable("NIKOTFILER_TEST_SHELL_RESULT", previous); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size;
        public uint Mask;
        public IntPtr Window;
        public string? Verb;
        public string? File;
        public string? Parameters;
        public string? Directory;
        public int Show;
        public IntPtr Instance;
        public IntPtr IdList;
        public string? Class;
        public IntPtr ClassKey;
        public uint HotKey;
        public IntPtr Icon;
        public IntPtr Process;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
