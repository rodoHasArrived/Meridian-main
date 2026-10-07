using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.AccountingClose;

public sealed partial class AccountingClosePreparationService
{
    private sealed record TemplateEntry(string TenantId, string CompanyId, ClosePlanTemplateDto Template);
    private sealed record PreviewEntry(string TenantId, string CompanyId, PreviewClosePreparationRequestDto Request,
        ClosePreparationPreviewDto Preview, string SourceFingerprint, string TargetFingerprint, int LatestTemplateVersion);
    private sealed record CreationEntry(string TenantId, string CompanyId, string IdempotencyKey,
        Guid PreviewId, Guid WorkflowId, Guid TemplateId, int TemplateVersion, Guid SourceWorkflowId,
        Guid TargetBookId, Guid TargetPeriodId, DateTimeOffset CreatedAtUtc, string CreatedBy,
        bool Completed, IReadOnlyList<ClosePreparationHistoryDto> History, bool StartAttempted = false, bool Abandoned = false);
    private sealed record PreparationDocument(int SchemaVersion, IReadOnlyList<TemplateEntry> Templates,
        IReadOnlyList<PreviewEntry> Previews, IReadOnlyList<CreationEntry> Creations);
    private sealed record PreparationEnvelope(string ContentHash, PreparationDocument Document);

    private async Task<IAsyncDisposable> AcquireAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_directory is null)
                return new PreparationLease(_gate, null);
            Directory.CreateDirectory(_directory);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // The inode is never removed: every process sharing retained preparation state serializes on it.
                    var file = new FileStream(Path.Combine(_directory, "preparation.lock"), FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None);
                    return new PreparationLease(_gate, file);
                }
                catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33)
                {
                    await Task.Delay(25, ct).ConfigureAwait(false);
                }
            }
        }
        catch { _gate.Release(); throw; }
    }

    private async Task<PreparationDocument> ReadAsync(CancellationToken ct)
    {
        if (_directory is null)
            return Copy(_memory);
        var path = Path.Combine(_directory, "preparation.json");
        var marker = Path.Combine(_directory, "initialized");
        if (!File.Exists(path) && !File.Exists(marker))
            return new(1, [], [], []);
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<PreparationEnvelope>(bytes, JsonOptions)
                ?? throw new InvalidOperationException("The preparation state is empty.");
            var state = envelope.Document;
            if (state is null || state.SchemaVersion != 1 || state.Templates is null || state.Previews is null || state.Creations is null
                || envelope.ContentHash != Hash(state)
                || state.Templates.GroupBy(entry => (entry.Template.TemplateId, entry.Template.Version)).Any(group => group.Count() > 1)
                || state.Previews.GroupBy(entry => entry.Preview.PreviewId).Any(group => group.Count() > 1)
                || state.Creations.Where(entry => !entry.Abandoned).GroupBy(entry => (entry.TargetBookId, entry.TargetPeriodId)).Any(group => group.Count() > 1))
                throw new InvalidOperationException("The retained preparation state failed integrity validation.");
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException("The retained preparation state is unavailable; restore it before retrying.", ex);
        }
    }

    private async Task SaveAsync(PreparationDocument state, CancellationToken ct)
    {
        var claimedPreviews = state.Creations.Select(creation => creation.PreviewId).ToHashSet();
        var now = _timeProvider.GetUtcNow();
        // Claimed previews are recovery evidence, including completed and abandoned requests.
        // Unclaimed previews are disposable after their creation window closes.
        state = state with
        {
            Previews = state.Previews.Where(entry => entry.Preview.ExpiresAtUtc > now
                || claimedPreviews.Contains(entry.Preview.PreviewId)).ToArray()
        };
        if (_directory is null)
        { _memory = Copy(state); return; }
        await AtomicFileWriter.WriteAsync(Path.Combine(_directory, "preparation.json"),
            JsonSerializer.Serialize(new PreparationEnvelope(Hash(state), state), JsonOptions), ct).ConfigureAwait(false);
        // Presence survives service restart and detects accidental loss of an initialized snapshot.
        await AtomicFileWriter.WriteAsync(Path.Combine(_directory, "initialized"), "meridian-close-preparation-v1", ct).ConfigureAwait(false);
    }

    private static string Hash<T>(T value) => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), JsonOptions)!;

    private sealed class PreparationLease(SemaphoreSlim gate, FileStream? file) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            { if (file is not null) await file.DisposeAsync().ConfigureAwait(false); }
            finally { gate.Release(); }
        }
    }
}
