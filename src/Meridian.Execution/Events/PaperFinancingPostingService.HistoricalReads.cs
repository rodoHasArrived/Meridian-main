using System.Text.Json;
using Meridian.Execution.Serialization;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;

namespace Meridian.Execution.Events;

public sealed partial class PaperFinancingPostingService
{
    /// <summary>Reads a completed historical checkpoint without posting, initializing, or
    /// acknowledging its outbox. A changed context never acquires posting authority here.</summary>
    internal async Task<IReadOnlyList<PaperFinancingCharge>> ReadHistoricalSourcesAsync(
        TradeFillLedgerPostingContext context, string sessionId, CancellationToken ct)
    {
        context.Validate();
        if (context.AggregateId != _context.AggregateId || context.LedgerBookId != _context.LedgerBookId
            || context.AccountingPolicyId != _context.AccountingPolicyId
            || context.AccountingPolicyVersion != _context.AccountingPolicyVersion)
            throw new InvalidDataException("The historical accounting scope is outside this host's configured authority.");

        var root = Path.Combine(new TradeFillPostingStoreOptions(_rootDirectory, context).ScopeDirectory, "financing");
        // Reusing a scope label for a different immutable context is unsupported. In
        // particular, do not read through the current writer's exclusive namespace lease.
        if (string.Equals(root, Path.GetDirectoryName(_snapshotPath), StringComparison.Ordinal))
            throw new InvalidDataException("Historical accounting requires a distinct retained posting-scope label.");
        var markerPath = Path.Combine(root, "initialized.json");
        var snapshotPath = Path.Combine(root, "sources.snapshot.json");
        var walPath = Path.Combine(root, "wal");
        if (!File.Exists(markerPath) || !File.Exists(snapshotPath) || !Directory.Exists(walPath))
            throw new InvalidDataException("Historical financing coverage is unavailable; its retained checkpoint is missing.");

        // Open only an existing lease: a read must not manufacture an empty historical store.
        // An active owner makes the checkpoint unavailable rather than racing its publication.
        await using var lease = new FileStream(Path.Combine(root, "writer.lock"), FileMode.Open,
            FileAccess.Read, FileShare.None);
        var marker = JsonSerializer.Deserialize(await File.ReadAllTextAsync(markerPath, ct).ConfigureAwait(false),
            ExecutionJsonContext.Default.PaperFinancingInitializationMarker);
        var snapshot = JsonSerializer.Deserialize(await File.ReadAllTextAsync(snapshotPath, ct).ConfigureAwait(false),
            ExecutionJsonContext.Default.PaperFinancingPostingSnapshot);
        if (marker is null || marker.Version != 1 || marker.Scope != context
            || snapshot is null || snapshot.Version != 1 || snapshot.Scope != context
            || snapshot.Sequence < 0 || snapshot.States is null)
            throw new InvalidDataException("Historical financing checkpoint scope or version is invalid.");

        var retainedStates = new Dictionary<Guid, PaperFinancingPostingState>();
        foreach (var state in snapshot.States)
        {
            if (state?.Charge is null)
                throw new InvalidDataException("Historical financing checkpoint contains an empty source.");
            ValidateRetainedState(state);
            if (!retainedStates.TryAdd(state.Charge.SourceEventId, state))
                throw new InvalidDataException("Historical financing checkpoint repeats a source identity.");
            if (state.Charge.SessionId == sessionId && !state.Posted)
                throw new InvalidDataException("Historical financing has pending delivery; recovery requires its original accounting owner.");
            if (state.Charge.SessionId == sessionId && state.Charge.AccountId != Services.PaperTradingPortfolio.DefaultAccountId)
                throw new InvalidDataException("The historical single-account session has conflicting financing account evidence.");
        }
        // Enumeration checks checksums but skips and counts corrupt records even in Halt
        // mode. Enforce the count below without initializing or opening a WAL writer.
        // A newer tail requires original-owner recovery, never an implicit ack.
        await using var wal = new WriteAheadLog(walPath, new WalOptions
        {
            SyncMode = WalSyncMode.EveryWrite,
            CorruptionMode = WalCorruptionMode.Halt,
            ArchiveAfterTruncate = false
        });
        await foreach (var record in wal.GetUncommittedRecordsAsync(ct).ConfigureAwait(false))
        {
            var payload = JsonSerializer.Deserialize(record.DeserializePayload<string>()!,
                ExecutionJsonContext.Default.PaperFinancingWalPayload);
            if (payload?.State?.Charge is null || payload.Scope != context
                || record.RecordType is not ("FinancingAccepted" or "FinancingPosted")
                || payload.State.Posted != (record.RecordType == "FinancingPosted"))
                throw new InvalidDataException("Historical financing WAL scope or transition is invalid.");
            ValidateRetainedState(payload.State);
            if (record.Sequence > snapshot.Sequence)
                throw new InvalidDataException("Historical financing has an uncheckpointed delivery; original-owner recovery is required.");
            if (!retainedStates.TryGetValue(payload.State.Charge.SourceEventId, out var retained)
                || retained.Charge != payload.State.Charge || retained.NativeCurrency != payload.State.NativeCurrency
                || payload.State.Posted && !retained.Posted)
                throw new InvalidDataException("Historical financing WAL conflicts with its retained checkpoint.");
        }
        if (wal.CorruptedRecordCount != 0)
            throw new InvalidDataException("Historical financing WAL is corrupt; original-owner recovery is required.");
        return snapshot.States.Where(state => state.Charge.SessionId == sessionId)
            .Select(static state => state.Charge).OrderBy(static charge => charge.OccurredAt).ToArray();
    }
}
