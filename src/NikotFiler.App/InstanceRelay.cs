using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace NikotFiler.App;

internal sealed class InstanceRelay : IDisposable
{
    private readonly string _name;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    public bool IsPrimary { get; }

    public InstanceRelay(string dataDirectory)
    {
        var identity = Path.GetFullPath(dataDirectory).TrimEnd('\\').ToUpperInvariant();
        _name = "nikotFiler-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        _mutex = new Mutex(true, @"Local\" + _name, out var created);
        IsPrimary = created;
    }

    public async Task ForwardAsync(string? path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await writer.WriteLineAsync(path ?? "");
        if (await reader.ReadLineAsync(timeout.Token) != "ok") throw new IOException("起動中の nikotFiler にフォルダーを渡せませんでした。");
    }

    public async Task ListenAsync(Func<string?, Task> open)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var path = await reader.ReadLineAsync(timeout.Token);
                if (path is null || path.Length > 32767) continue;
                await open(path.Length == 0 ? null : Path.GetFullPath(path));
                await writer.WriteLineAsync("ok");
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ArgumentException) { }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
