using System.Text.Json;
using System.Threading;
using Meridian.Storage.Archival;

namespace Meridian.QuantScript.Documents;

/// <summary>
/// File-system backed store for QuantScript notebook documents.
/// </summary>
public sealed class QuantScriptNotebookStore : IQuantScriptNotebookStore
{
    private static long _pathSuffixCounter;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public QuantScriptNotebookStore(QuantScriptOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ScriptsDirectory = options.ScriptsDirectory;
        NotebookExtension = options.NotebookExtension;
    }

    public string ScriptsDirectory { get; }

    public string NotebookExtension { get; }

    public IReadOnlyList<QuantScriptDocumentReference> ListDocuments()
    {
        if (!Directory.Exists(ScriptsDirectory))
            return Array.Empty<QuantScriptDocumentReference>();

        var notebooks = Directory.GetFiles(ScriptsDirectory, $"*{NotebookExtension}", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new QuantScriptDocumentReference(Path.GetFileName(path), path, QuantScriptDocumentKind.Notebook));

        var legacyScripts = Directory.GetFiles(ScriptsDirectory, "*.csx", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new QuantScriptDocumentReference(Path.GetFileName(path), path, QuantScriptDocumentKind.LegacyScript));

        return notebooks.Concat(legacyScripts).ToList();
    }

    public async Task<QuantScriptNotebookDocument> LoadNotebookAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<QuantScriptNotebookDocument>(stream, SerializerOptions, ct)
            .ConfigureAwait(false);

        if (document is null)
            throw new InvalidDataException($"Notebook '{path}' could not be deserialized.");

        if (document.Version != QuantScriptNotebookDocument.CurrentVersion)
        {
            throw new InvalidDataException(
                $"Notebook '{path}' has version {document.Version}; expected {QuantScriptNotebookDocument.CurrentVersion}.");
        }

        return EnsureCells(document);
    }

    public async Task<QuantScriptNotebookDocument> ImportLegacyScriptAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var source = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var title = Path.GetFileNameWithoutExtension(path);

        return new QuantScriptNotebookDocument
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Imported QuantScript" : title,
            Cells =
            [
                new QuantScriptNotebookCellDocument(Guid.NewGuid().ToString("N"), source)
            ]
        };
    }

    public async Task SaveNotebookAsync(string path, QuantScriptNotebookDocument document, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        var normalized = EnsureCells(document);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ScriptsDirectory);

        var serialized = JsonSerializer.Serialize(normalized, SerializerOptions);
        await AtomicFileWriter.WriteAsync(path, serialized, ct).ConfigureAwait(false);
    }

    public string GetSuggestedNotebookPath(string title)
    {
        Directory.CreateDirectory(ScriptsDirectory);

        var sanitized = string.Concat(
            (string.IsNullOrWhiteSpace(title) ? "quantscript-notebook" : title.Trim())
                .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch))
            .Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "quantscript-notebook";

        var suffix = $"{DateTime.UtcNow.Ticks:x}-{Interlocked.Increment(ref _pathSuffixCounter):x}";
        return Path.Combine(ScriptsDirectory, $"{sanitized}-{suffix}{NotebookExtension}");
    }

    private static QuantScriptNotebookDocument EnsureCells(QuantScriptNotebookDocument document)
    {
        var cells = document.Cells
            .Where(cell => cell is not null)
            .Select(cell => new QuantScriptNotebookCellDocument(
                string.IsNullOrWhiteSpace(cell.Id) ? Guid.NewGuid().ToString("N") : cell.Id,
                cell.Source ?? string.Empty,
                cell.Collapsed))
            .ToList();

        if (cells.Count == 0)
        {
            cells.Add(new QuantScriptNotebookCellDocument(
                Guid.NewGuid().ToString("N"),
                "var spy = Data.Prices(\"SPY\");" + Environment.NewLine + "Print(spy);"));
        }

        return new QuantScriptNotebookDocument
        {
            Title = string.IsNullOrWhiteSpace(document.Title) ? "QuantScript Notebook" : document.Title,
            Version = QuantScriptNotebookDocument.CurrentVersion,
            Metadata = document.Metadata,
            Cells = cells
        };
    }
}
