using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;

namespace Meridian.Execution.Services;

public sealed partial class PaperSessionPersistenceService
{
    private const string NativeJournalCurrency = "USD";

    private static (PaperTradingPortfolio Portfolio, PaperSessionJournalProjection Journal) ProjectDurableSession(
        PaperSession session, IReadOnlyList<PaperSessionFillRecord> fills,
        IReadOnlyList<PaperSessionSettlementRecord> settlements, IReadOnlyList<PaperFinancingCharge> charges)
    {
        var journal = new PaperSessionJournalProjection(session.SessionId,
            session.FinancialAccountId ?? PaperTradingPortfolio.DefaultAccountId,
            session.StrategyId, session.JournalCurrency!, fills);
        var portfolio = new PaperTradingPortfolio(session.InitialCash, journal.SourceLedger, session.CreatedAt);
        journal.CaptureOpening(session.InitialCash, session.CreatedAt);
        ReplayInto(portfolio, fills.Select(static fill => fill.Fill).ToArray(), settlements,
            fills.ToDictionary(static fill => fill.FillId, static fill => (fill.AccountingEvidenceVersion, fill.FinancialAccountId)),
            charges, journal);
        return (portfolio, journal);
    }

    private async Task<PaperSession> LoadDurableSessionAsync(PersistedSessionRecord record, CancellationToken ct)
    {
        if (_store is not { SupportsJournalBatches: true }
            || record.JournalSchemaVersion != PaperSessionJournalBatch.CurrentSchemaVersion
            || record.JournalCurrency != NativeJournalCurrency || record.AccountingContext is not null)
            throw new InvalidDataException("The retained paper-session journal version or scope is unsupported.");

        var session = new PaperSession
        {
            SessionId = record.SessionId,
            StrategyId = record.StrategyId,
            StrategyName = record.StrategyName,
            InitialCash = record.InitialCash,
            CreatedAt = record.CreatedAt,
            ClosedAt = record.ClosedAt,
            IsActive = record.IsActive,
            Symbols = record.Symbols.ToList(),
            MatchingModelVersion = record.MatchingModelVersion,
            CostModelVersion = record.CostModelVersion,
            FinancialAccountId = record.FinancialAccountId,
            JournalCurrency = record.JournalCurrency,
            IsAccountDefault = record.IsAccountDefault
        };
        var fills = await _store.LoadFillRecordsAsync(session.SessionId, ct).ConfigureAwait(false);
        foreach (var fill in fills)
            fill.Validate();
        var settlements = await LoadValidatedSettlementsAsync(session.SessionId, ct).ConfigureAwait(false);
        var retained = ReadReceipts(session.SessionId,
            await _store.LoadJournalBatchesAsync(session.SessionId, ct).ConfigureAwait(false));
        var charges = retained.Values.Select(static receipt => receipt.Batch.Financing).OfType<PaperFinancingCharge>().ToArray();
        var (portfolio, journal) = ProjectDurableSession(session, fills, settlements, charges);

        // Check every retained entry against its accepted source before repairing a missing batch.
        // Opening capital and acknowledged fills cannot be missing: those sources were published
        // only after the journal append. An interrupted, unacknowledged effect can be recovered.
        ValidateReceipts(journal.Batches, retained);
        var pending = fills.Where(static fill => !fill.IsApplied).ToDictionary(static fill => fill.FillId.ToString("D"));
        foreach (var batch in journal.Batches.Where(batch => !retained.ContainsKey(batch.BatchId)))
        {
            if (!session.IsActive || (batch.Purpose != PaperSessionJournalPurpose.Settlement
                && (batch.Purpose != PaperSessionJournalPurpose.Fill || !pending.ContainsKey(batch.SourceId))))
                throw new InvalidDataException($"Paper-session journal effect '{batch.BatchId:D}' is missing from a published session.");
            var receipt = await AppendVerifiedBatchAsync(batch, ct).ConfigureAwait(false);
            retained.Add(batch.BatchId, receipt);
        }
        foreach (var fill in pending.Values)
        {
            if (!session.IsActive)
                throw new InvalidDataException("A closed paper session retains a fill pending acknowledgement.");
            await _store.MarkFillAppliedAsync(session.SessionId, fill.FillId, fill.CanonicalHash, ct).ConfigureAwait(false);
        }

        session.Portfolio = portfolio;
        session.DurableLedger = journal.Ledger;
        session.DurableReceipts = retained;
        foreach (var fill in fills)
            AddPublishedDurableFill(session, fill);
        session.SettlementHistory.AddRange(settlements);
        session.FinancingHistory.AddRange(charges);
        var orders = await _store.LoadOrderHistoryAsync(session.SessionId, ct).ConfigureAwait(false);
        session.OrderHistory.AddRange(orders.Select(CloneOrderState));
        return session;
    }

    private static Dictionary<Guid, PaperSessionJournalReceipt> ReadReceipts(
        string sessionId, IReadOnlyList<PaperSessionJournalReceipt> receipts)
    {
        var retained = new Dictionary<Guid, PaperSessionJournalReceipt>();
        foreach (var receipt in receipts)
        {
            receipt.Validate(sessionId);
            if (!retained.TryAdd(receipt.Batch.BatchId, receipt))
                throw new InvalidDataException("A paper-session journal effect was retained more than once.");
        }
        return retained;
    }

    private static void ValidateReceipts(IReadOnlyList<PaperSessionJournalBatch> projected,
        IReadOnlyDictionary<Guid, PaperSessionJournalReceipt> retained)
    {
        var expected = projected.ToDictionary(static batch => batch.BatchId);
        foreach (var (id, receipt) in retained)
        {
            if (!expected.TryGetValue(id, out var batch) || !Sha256Digest.FixedEquals(receipt.ContentHash, batch.ComputeHash()))
                throw new InvalidDataException($"Paper-session journal effect '{id:D}' disagrees with its retained source; posted history cannot change.");
        }
    }

    private async Task<PaperSessionJournalReceipt> AppendVerifiedBatchAsync(PaperSessionJournalBatch batch, CancellationToken ct)
    {
        var receipt = await _store!.AppendJournalBatchAsync(batch, ct).ConfigureAwait(false);
        receipt.Validate(batch.SessionId);
        if (!Sha256Digest.FixedEquals(receipt.ContentHash, batch.ComputeHash()))
            throw new InvalidDataException("The durable paper journal receipt does not match the requested batch.");
        return receipt;
    }

    private async Task RecordDurableFillAsync(PaperSession session, PaperSessionFillRecord fill, CancellationToken ct)
    {
        if (session.DurableFills.Find(existing => existing.FillId == fill.FillId) is { } applied)
        {
            if (!Sha256Digest.FixedEquals(applied.CanonicalHash, fill.CanonicalHash) || !applied.HasSameAccountingProvenance(fill))
                throw new InvalidDataException($"Paper-session fill '{fill.FillId:D}' has conflicting content.");
            return;
        }
        if (!session.IsActive)
            throw new InvalidOperationException("The paper session is closed and cannot retain a new fill.");
        if (fill.Fill.FilledQuantity <= 0m || fill.Fill.FillPrice is null)
            throw new InvalidDataException("A durable paper fill requires positive quantity and an execution price.");

        var sourceId = fill.FillId.ToString("D");
        var batchId = PaperSessionJournalBatch.GetId(session.SessionId,
            session.FinancialAccountId ?? PaperTradingPortfolio.DefaultAccountId, sourceId, PaperSessionJournalPurpose.Fill);
        EnsureDurableEffectCanProceed(session, batchId);

        // Retain the execution claim even when projection fails. Keep it pending until both the
        // journal append and acknowledgement complete so another effect cannot get ahead of it.
        ct.ThrowIfCancellationRequested();
        session.PendingDurableEffect = batchId;
        var result = await _store!.TryAppendFillAsync(session.SessionId, fill, CancellationToken.None).ConfigureAwait(false);
        if (result.Status == PaperSessionFillAppendStatus.Conflict)
            throw new InvalidDataException($"Paper-session fill '{fill.FillId:D}' has a conflicting durable claim.");

        var (portfolio, journal) = ProjectDurableSession(session, [.. session.DurableFills, fill],
            session.SettlementHistory, session.FinancingHistory);
        var batch = journal.Batches.Single(candidate => candidate.Purpose == PaperSessionJournalPurpose.Fill && candidate.SourceId == sourceId);
        ValidateReceipts(journal.Batches, session.DurableReceipts);
        var receipt = await AppendVerifiedBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
        await _store.MarkFillAppliedAsync(session.SessionId, fill.FillId, fill.CanonicalHash, CancellationToken.None).ConfigureAwait(false);
        lock (session.SyncRoot)
        {
            AddPublishedDurableFill(session, fill);
            PublishDurableEffect(session, portfolio, journal, receipt);
        }
    }

    private async Task<bool> RecordDurableSettlementAsync(PaperSession session, PaperSessionSettlementRecord settlement, CancellationToken ct)
    {
        if (settlement.AccountId != PaperTradingPortfolio.DefaultAccountId)
            throw new InvalidDataException("The settlement belongs to a different portfolio account.");
        var (portfolio, journal) = ProjectDurableSession(session, session.DurableFills,
            [.. session.SettlementHistory, settlement], session.FinancingHistory);
        var batch = journal.Batches.Single(candidate => candidate.Purpose == PaperSessionJournalPurpose.Settlement
            && candidate.SourceId == settlement.Key);
        EnsureDurableEffectCanProceed(session, batch.BatchId);
        ValidateReceipts(journal.Batches, session.DurableReceipts);

        ct.ThrowIfCancellationRequested();
        session.PendingDurableEffect = batch.BatchId;
        var status = await _store!.TryAppendSettlementAsync(session.SessionId, settlement, CancellationToken.None).ConfigureAwait(false);
        if (status == PaperSessionSettlementAppendStatus.Conflict
            || (status == PaperSessionSettlementAppendStatus.ExistingSame
                && !(await _store.LoadSettlementsAsync(session.SessionId, CancellationToken.None).ConfigureAwait(false)).Contains(settlement)))
            throw new InvalidDataException("A conflicting paper settlement is already retained.");
        var receipt = await AppendVerifiedBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
        lock (session.SyncRoot)
        {
            session.SettlementHistory.Add(settlement);
            PublishDurableEffect(session, portfolio, journal, receipt);
        }
        return true;
    }

    private async Task<bool> RecordDurableFinancingAsync(PaperSession session, PaperFinancingCharge charge, CancellationToken ct)
    {
        var (portfolio, journal) = ProjectDurableSession(session, session.DurableFills,
            session.SettlementHistory, [.. session.FinancingHistory, charge]);
        var batch = journal.Batches.Single(candidate => candidate.Purpose == PaperSessionJournalPurpose.Financing
            && candidate.SourceId == charge.SourceEventId.ToString("D"));
        EnsureDurableEffectCanProceed(session, batch.BatchId);
        ValidateReceipts(journal.Batches, session.DurableReceipts);

        // The assessment and its two journal legs are in the same atomic receipt.
        ct.ThrowIfCancellationRequested();
        session.PendingDurableEffect = batch.BatchId;
        var receipt = await AppendVerifiedBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
        lock (session.SyncRoot)
        {
            session.FinancingHistory.Add(charge);
            PublishDurableEffect(session, portfolio, journal, receipt);
        }
        return true;
    }

    private static void AddPublishedDurableFill(PaperSession session, PaperSessionFillRecord fill)
    {
        session.DurableFills.Add(fill with { IsApplied = true });
        session.FillHistory.Add(CloneExecutionReport(fill.Fill));
        session.AppliedFillHashes.Add(fill.FillId, fill.CanonicalHash);
        session.FillAccountingProvenance.Add(fill.FillId, (fill.AccountingEvidenceVersion, fill.FinancialAccountId));
    }

    private static void EnsureDurableEffectCanProceed(PaperSession session, Guid? candidate = null)
    {
        if (session.PendingDurableEffect is { } pending && pending != candidate)
            throw new InvalidOperationException($"Paper-session journal effect '{pending:D}' must be recovered before further changes.");
    }

    private static void PublishDurableEffect(PaperSession session, PaperTradingPortfolio portfolio,
        PaperSessionJournalProjection journal, PaperSessionJournalReceipt receipt)
    {
        session.Portfolio = portfolio;
        session.DurableLedger = journal.Ledger;
        session.DurableReceipts.Add(receipt.Batch.BatchId, receipt);
        session.PendingDurableEffect = null;
    }

    private async Task<(PaperTradingPortfolio Portfolio, PaperSessionJournalProjection Journal,
        IReadOnlyList<PaperSessionFillRecord> Fills, IReadOnlyList<PaperSessionSettlementRecord> Settlements,
        IReadOnlyList<PaperFinancingCharge> Charges)> ReadVerifiedDurableProjectionAsync(PaperSession session, CancellationToken ct)
    {
        EnsureDurableEffectCanProceed(session);
        var fills = await _store!.LoadFillRecordsAsync(session.SessionId, ct).ConfigureAwait(false);
        if (fills.Any(static fill => !fill.IsApplied))
            throw new InvalidDataException("The selected paper session has pending fill delivery.");
        foreach (var fill in fills)
            fill.Validate();
        var settlements = await LoadValidatedSettlementsAsync(session.SessionId, ct).ConfigureAwait(false);
        var retained = ReadReceipts(session.SessionId,
            await _store.LoadJournalBatchesAsync(session.SessionId, ct).ConfigureAwait(false));
        // Published keys must still be present. A verification read must never repair the journal.
        if (session.DurableReceipts.Keys.Any(id => !retained.ContainsKey(id)))
            throw new InvalidDataException("A published paper-session journal receipt is missing.");
        var charges = retained.Values.Select(static receipt => receipt.Batch.Financing).OfType<PaperFinancingCharge>().ToArray();
        var (portfolio, journal) = ProjectDurableSession(session, fills, settlements, charges);
        ValidateReceipts(journal.Batches, retained);
        if (journal.Batches.Count != retained.Count)
            throw new InvalidDataException("The selected paper session's journal is incomplete.");
        return (portfolio, journal, fills, settlements, charges);
    }

    /// <summary>Compares the independently replayed portfolio to the verified journal at a single cutoff.</summary>
    public async Task<PaperSessionCutoffEvidence?> GetCutoffEvidenceAsync(
        string sessionId, DateTimeOffset cutoff, CancellationToken ct = default)
    {
        using var operation = EnterOperation(ct);
        ct = operation.Token;
        await InitialiseAsync(ct).ConfigureAwait(false);
        if (!CurrentSessions.TryGetValue(sessionId, out var session))
            return null;

        var gate = GetSessionGate(sessionId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var accountId = session.FinancialAccountId ?? PaperTradingPortfolio.DefaultAccountId;
            if (session.JournalCurrency is null || cutoff < session.CreatedAt)
                return new(sessionId, accountId, session.JournalCurrency, cutoff, null, null, [],
                    "The session has no verified journal coverage at this cutoff.");
            var verified = await ReadVerifiedDurableProjectionAsync(session, ct).ConfigureAwait(false);
            var (portfolio, cutoffJournal) = ProjectDurableSession(session,
                verified.Fills.Where(fill => fill.Fill.Timestamp <= cutoff).ToArray(),
                verified.Settlements.Where(settlement => settlement.SettledAt <= cutoff).ToArray(),
                verified.Charges.Where(charge => charge.OccurredAt <= cutoff).ToArray());
            var journalCash = verified.Journal.Ledger.GetBalanceAsOf(LedgerAccounts.Cash, cutoff);
            var breaks = portfolio.CheckLedgerIntegrity().ToList();
            if (journalCash != portfolio.Cash)
                breaks.Add(new(accountId, "CashAtCutoff", portfolio.Cash, journalCash));
            var sourceHash = Sha256Digest.ComputeUtf8(string.Join("\n", cutoffJournal.Batches
                .OrderBy(batch => batch.BatchId).Select(batch => batch.ComputeHash())));
            return new(sessionId, accountId, session.JournalCurrency, cutoff, journalCash, portfolio.Cash, breaks, null)
            {
                SourceHashSha256 = sourceHash
            };
        }
        finally { gate.Release(); }
    }
}

/// <summary>Read-only paper accounting proof for one session and cutoff.</summary>
public sealed record PaperSessionCutoffEvidence(string SessionId, string AccountId, string? Currency,
    DateTimeOffset Cutoff, decimal? JournalCash, decimal? PortfolioCash,
    IReadOnlyList<PaperLedgerIntegrityBreak> Breaks, string? CoverageBlocker)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceHashSha256 { get; init; }
}
