using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.AccountingSystem;

/// <summary>
/// Retains external-GL imports, mapping profiles and export packages (including their human
/// certifications) across restarts. Each collection is one JSON document replaced atomically, so a
/// crash leaves either the previous or the new document. Single-process local authority: writers
/// in one process are serialized per directory; it does not fence a second host on the same files.
/// </summary>
public sealed class AccountingSystemIntegrationStateStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string _directory;
    private readonly SemaphoreSlim _gate;

    public AccountingSystemIntegrationStateStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        _gate = Gates.GetOrAdd(_directory, static _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>Reads one retained collection; an absent document is an empty collection.</summary>
    internal IReadOnlyDictionary<string, T> Load<T>(string collection, JsonSerializerOptions options)
    {
        var path = PathFor(collection);
        if (!File.Exists(path))
            return new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var rows = JsonSerializer.Deserialize<Dictionary<string, T>>(File.ReadAllText(path), options);
            if (rows is null || rows.Values.Any(static row => row is null))
                throw new JsonException("The document is empty or holds a null record.");
            return new Dictionary<string, T>(rows, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException exception)
        {
            // Starting empty would silently drop retained certifications and let the next write
            // replace the unreadable document.
            throw new InvalidOperationException(
                $"Retained accounting-system integration state '{collection}' cannot be read; restore or remove the file before continuing.",
                exception);
        }
    }

    /// <summary>
    /// Writes <paramref name="key"/> to the retained collection, then publishes it to
    /// <paramref name="rows"/>. A failed write leaves the in-memory collection unchanged, so a
    /// caller never sees state that would not survive a restart.
    /// </summary>
    internal async Task RetainAsync<T>(
        string collection,
        ConcurrentDictionary<string, T> rows,
        string key,
        T value,
        JsonSerializerOptions options,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var next = new SortedDictionary<string, T>(rows, StringComparer.OrdinalIgnoreCase) { [key] = value };
            Directory.CreateDirectory(_directory);
            await AtomicFileWriter.WriteAsync(PathFor(collection), JsonSerializer.Serialize(next, options), ct)
                .ConfigureAwait(false);
            rows[key] = value;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string PathFor(string collection) => Path.Combine(_directory, collection + ".json");
}
