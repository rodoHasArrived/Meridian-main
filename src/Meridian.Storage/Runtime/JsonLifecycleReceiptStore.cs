using System.Text.Json;
using Meridian.Contracts.Lifecycle;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Runtime;

public sealed record LifecycleReceiptStoreOptions
{
    public required string DataRoot { get; init; }
    public int RetainedReceiptCount { get; init; } = 50;
}

/// <summary>
/// Atomic, bounded JSON lifecycle receipt store rooted outside the install directory.
/// </summary>
public sealed class JsonLifecycleReceiptStore : ILifecycleReceiptStore
{
    private readonly string _receiptDirectory;
    private readonly int _retainedReceiptCount;

    public JsonLifecycleReceiptStore(LifecycleReceiptStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataRoot);

        _receiptDirectory = Path.Combine(options.DataRoot, "runtime", "lifecycle", "receipts");
        _retainedReceiptCount = Math.Max(1, options.RetainedReceiptCount);
    }

    public async ValueTask WriteHostReceiptAsync(
        LifecycleShutdownReceiptDto receipt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var path = Path.Combine(_receiptDirectory, $"host-{receipt.SessionId}.json");
        var json = JsonSerializer.Serialize(
            receipt,
            LifecycleContractsJsonContext.Default.LifecycleShutdownReceiptDto);
        await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
        PruneReceipts("host-*.json", path);
    }

    public async ValueTask<LifecycleShutdownReceiptDto?> ReadLatestHostReceiptAsync(
        CancellationToken ct = default)
    {
        if (!Directory.Exists(_receiptDirectory))
        {
            return null;
        }

        var latest = new DirectoryInfo(_receiptDirectory)
            .EnumerateFiles("host-*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (latest is null)
        {
            return null;
        }

        await using var stream = new FileStream(
            latest.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync(
            stream,
            LifecycleContractsJsonContext.Default.LifecycleShutdownReceiptDto,
            ct).ConfigureAwait(false);
    }

    public async ValueTask WriteSessionReceiptAsync(
        LifecycleSessionReceiptDto receipt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var path = Path.Combine(_receiptDirectory, $"session-{receipt.SessionId}.json");
        var json = JsonSerializer.Serialize(
            receipt,
            LifecycleContractsJsonContext.Default.LifecycleSessionReceiptDto);
        await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
        PruneReceipts("session-*.json", path);
    }

    private void PruneReceipts(string pattern, string currentPath)
    {
        var staleReceipts = new DirectoryInfo(_receiptDirectory)
            .EnumerateFiles(pattern, SearchOption.TopDirectoryOnly)
            .Where(file => !string.Equals(file.FullName, currentPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(_retainedReceiptCount - 1)
            .ToArray();

        foreach (var staleReceipt in staleReceipts)
        {
            try
            {
                staleReceipt.Delete();
            }
            catch (IOException)
            {
                // Retention cleanup is best-effort; a locked receipt remains available for evidence.
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve the receipt when the current process cannot prove delete authority.
            }
        }
    }
}
