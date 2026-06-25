using System.Runtime.InteropServices;
using System.Text.Json;
using Meridian.Core.Contracts;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Persists provider credentials to {DataRoot}/provider-credentials.json.
/// File is created with restricted OS permissions (Unix: 600).
/// All public methods are thread-safe; a SemaphoreSlim guards read-modify-write cycles.
/// </summary>
public sealed class ProviderCredentialStore : IProviderCredentialStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ProviderCredentialStore(string dataRoot)
    {
        _filePath = Path.Combine(dataRoot, "provider-credentials.json");
    }

    public async Task SaveCredentialsAsync(
        string moduleId,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(values);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync(ct).ConfigureAwait(false);
            store[moduleId] = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
            await PersistStoreAsync(store, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetCredentialsAsync(
        string moduleId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync(ct).ConfigureAwait(false);
            return store.TryGetValue(moduleId, out var creds)
                ? creds
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlySet<string>> GetStoredKeyNamesAsync(
        string moduleId,
        CancellationToken ct = default)
    {
        var creds = await GetCredentialsAsync(moduleId, ct).ConfigureAwait(false);
        return new HashSet<string>(
            creds.Where(kv => !string.IsNullOrEmpty(kv.Value)).Select(kv => kv.Key),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task DeleteCredentialsAsync(string moduleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync(ct).ConfigureAwait(false);
            store.Remove(moduleId);
            await PersistStoreAsync(store, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, Dictionary<string, string>>> LoadStoreAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json, JsonOptions);
            return result ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task PersistStoreAsync(
        Dictionary<string, Dictionary<string, string>> store,
        CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // On Unix: pre-create the destination file with owner-only permissions (chmod 600)
        // before the first write. AtomicFileWriter copies security metadata from the destination
        // to its temp file when the destination already exists, so the temp file inherits 600
        // before the atomic rename — eliminating the brief window where the file is world-readable.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !File.Exists(_filePath))
        {
            try
            {
                using (File.Open(_filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                File.SetUnixFileMode(_filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception)
            {
                // Pre-creation failure is non-fatal; permissions will still be applied post-write.
            }
        }

        var json = JsonSerializer.Serialize(store, JsonOptions);
        await AtomicFileWriter.WriteAsync(_filePath, json, ct).ConfigureAwait(false);
    }
}
