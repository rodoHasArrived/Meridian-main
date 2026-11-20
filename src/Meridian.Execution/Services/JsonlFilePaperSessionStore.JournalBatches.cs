using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Core.IO;
using Meridian.Execution.Serialization;

namespace Meridian.Execution.Services;

public sealed partial class JsonlFilePaperSessionStore
{
    public bool SupportsJournalBatches => true;

    public async Task<PaperSessionJournalReceipt> AppendJournalBatchAsync(
        PaperSessionJournalBatch batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.Validate();
        var contentHash = batch.ComputeHash();
        var path = _pathGuard.ResolvePath(batch.SessionId, "journal-batches", $"{batch.BatchId:N}.json");
        var lockPath = _pathGuard.ResolvePath(".journal-receipts.lock");

        await using var lease = await ExclusiveFileLock.AcquireAsync(lockPath, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        if (File.Exists(path))
        {
            var retained = await ReadJournalReceiptAsync(batch.SessionId, path, ct).ConfigureAwait(false);
            if (!Sha256Digest.FixedEquals(retained.ContentHash, contentHash))
                throw new InvalidDataException($"Paper journal effect '{batch.BatchId:D}' is already retained with different content.");
            return retained;
        }

        var receipt = new PaperSessionJournalReceipt(batch, contentHash);
        await WriteAtomicAsync(path,
            JsonSerializer.Serialize(receipt, ExecutionJsonContext.Default.PaperSessionJournalReceipt),
            ct).ConfigureAwait(false);
        return receipt;
    }

    public async Task<IReadOnlyList<PaperSessionJournalReceipt>> LoadJournalBatchesAsync(
        string sessionId, CancellationToken ct = default)
    {
        var directory = _pathGuard.ResolvePath(sessionId, "journal-batches");
        if (!Directory.Exists(directory))
            return [];

        var receipts = new List<PaperSessionJournalReceipt>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            receipts.Add(await ReadJournalReceiptAsync(sessionId, path, ct).ConfigureAwait(false));
        return receipts;
    }

    private async Task<PaperSessionJournalReceipt> ReadJournalReceiptAsync(
        string sessionId, string path, CancellationToken ct)
    {
        _pathGuard.EnsurePath(path);
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var receipt = JsonSerializer.Deserialize(json, ExecutionJsonContext.Default.PaperSessionJournalReceipt)
                ?? throw new InvalidDataException("A retained paper journal receipt was empty.");
            receipt.Validate(sessionId);
            if (!string.Equals(Path.GetFileName(path), $"{receipt.Batch.BatchId:N}.json", StringComparison.Ordinal))
                throw new InvalidDataException("A retained paper journal receipt has an incorrect batch identity.");
            return receipt;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("A retained paper journal receipt could not be decoded.", exception);
        }
    }
}
