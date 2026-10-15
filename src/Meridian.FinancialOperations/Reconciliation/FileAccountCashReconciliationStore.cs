using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>Explicitly provisioned atomic state with a cross-process lease spanning each mutation.</summary>
public sealed class FileAccountCashReconciliationStore
{
    private const string Marker = "meridian-account-cash-reconciliation-v1";
    private readonly string _directory;
    private string StatePath => Path.Combine(_directory, "state.json");
    private string MarkerPath => Path.Combine(_directory, "initialized");

    public FileAccountCashReconciliationStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _directory = Path.Combine(Path.GetFullPath(dataRoot), "reconciliation", "account-cash-continuity");
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        if (File.Exists(MarkerPath))
        {
            _ = await ReadAsync(ct).ConfigureAwait(false);
            return;
        }
        if (File.Exists(StatePath))
            throw new InvalidOperationException("Unprovisioned continuity state requires explicit recovery.");
        await WriteAsync(new(1, [], []), ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(MarkerPath, Marker, ct).ConfigureAwait(false);
    }

    internal async Task<T> WithStateAsync<T>(
        Func<AccountCashReconciliationDocument, (AccountCashReconciliationDocument Document, T Result)> action,
        bool write,
        CancellationToken ct)
    {
        if (!Directory.Exists(_directory))
            throw new InvalidOperationException("Account cash continuity has not been provisioned.");
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        var original = await ReadAsync(ct).ConfigureAwait(false);
        var (updated, result) = action(original);
        if (write && !ReferenceEquals(original, updated))
            await WriteAsync(updated, ct).ConfigureAwait(false);
        return result;
    }

    private async Task<FileStream> AcquireAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(Path.Combine(_directory, "state.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33)
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<AccountCashReconciliationDocument> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(MarkerPath) || await File.ReadAllTextAsync(MarkerPath, ct).ConfigureAwait(false) != Marker)
            throw new InvalidOperationException("Account cash continuity initialization is missing or invalid.");
        try
        {
            var bytes = await File.ReadAllBytesAsync(StatePath, ct).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize(bytes, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationEnvelope)
                ?? throw new InvalidOperationException("Account cash continuity state is empty.");
            var document = envelope.Document;
            if (document is null || document.SchemaVersion != 1 || document.Runs is null || document.Cases is null
                || !Sha256Digest.IsCanonical(envelope.ContentHash) || !Sha256Digest.FixedEquals(envelope.ContentHash, Hash(document))
                || document.Cases.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count() != document.Cases.Count)
                throw new InvalidOperationException("Account cash continuity state failed integrity validation.");
            return document;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new InvalidOperationException("Retained account cash continuity state is unavailable or corrupt; restore it before retrying.", ex);
        }
    }

    private Task WriteAsync(AccountCashReconciliationDocument document, CancellationToken ct)
        => AtomicFileWriter.WriteAsync(StatePath, JsonSerializer.SerializeToUtf8Bytes(
            new AccountCashReconciliationEnvelope(Hash(document), document), AccountCashReconciliationJsonContext.Default.AccountCashReconciliationEnvelope), ct);

    private static string Hash(AccountCashReconciliationDocument document)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(document, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationDocument));
}
