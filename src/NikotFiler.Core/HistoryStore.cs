using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NikotFiler.Core;

public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    public string FilePath { get; }

    public HistoryStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "nikotFiler", "history.jsonl");
    }

    public void Append(HistoryEntry entry)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.WriteLine(JsonSerializer.Serialize(entry, JsonOptions));
            writer.Flush();
            stream.Flush(true);
        }
    }

    public void RecordVisit(HistoryKind kind, string path)
    {
        if (kind is not (HistoryKind.FolderVisit or HistoryKind.FileOpen))
            throw new ArgumentException("Visit kind required.", nameof(kind));
        Append(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, kind,
            HistoryStatus.Viewed, Path.GetFullPath(path)));
    }

    public IReadOnlyList<HistoryEntry> ReadLatest()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath)) return [];
            var entries = new Dictionary<Guid, HistoryEntry>();
            foreach (var line in File.ReadLines(FilePath))
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<HistoryEntry>(line, JsonOptions);
                    if (entry is not null) entries[entry.Id] = entry;
                }
                catch (JsonException) { /* A truncated final line must not hide older history. */ }
            }
            return entries.Values.OrderByDescending(e => e.Timestamp).ToArray();
        }
    }

    public void ExecuteLogged(HistoryKind kind, string source, string? destination, Action action)
    {
        if (kind is HistoryKind.FolderVisit)
            throw new ArgumentException("Operation kind required.", nameof(kind));

        var id = Guid.NewGuid();
        var start = new HistoryEntry(id, DateTimeOffset.Now, kind,
            HistoryStatus.Started, Path.GetFullPath(source),
            destination is null ? null : Path.GetFullPath(destination));

        // Refuse to change files when the history cannot be written.
        Append(start);
        try { action(); }
        catch (Exception ex)
        {
            try
            {
                Append(start with { Timestamp = DateTimeOffset.Now, Status = HistoryStatus.Failed,
                    Error = ex.Message });
            }
            catch (IOException) { /* The Started record remains as an unresolved operation. */ }
            throw;
        }
        // A completion-write error leaves the Started record, rather than falsely
        // reporting that an already completed file action failed.
        Append(start with { Timestamp = DateTimeOffset.Now, Status = HistoryStatus.Succeeded });
    }
}
