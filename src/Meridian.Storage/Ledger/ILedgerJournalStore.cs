using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public interface ILedgerJournalStore
{
    Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default);

    // SEC-005 slice 4c-ii: fund-scoped reads are scoped to the caller's tenant via the stamped
    // tenant_id column inside the Postgres store, resolved from the ambient IFundScopeTenantAccessor
    // (fail-open when no tenant is in scope). The store interface is intentionally unchanged so the
    // ~50 internal/worker/service call sites and other implementations are unaffected.
    Task<IReadOnlyList<LedgerJournalEntryRecord>> QueryAsync(
        LedgerJournalEntryQuery query,
        CancellationToken ct = default)
        => Task.FromException<IReadOnlyList<LedgerJournalEntryRecord>>(
            new NotSupportedException("This ledger journal store does not support scoped journal queries."));

    Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default);

    Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
        Guid? ledgerBookId = null,
        string? status = null,
        string? fundProfileId = null,
        Guid? fundStructureNodeId = null,
        CancellationToken ct = default);

    Task<LedgerAccountingPeriod> SavePeriodAsync(
        LedgerAccountingPeriod period,
        long expectedVersion,
        PeriodCloseEventRecord? closeEvent = null,
        CancellationToken ct = default);

    Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
        string? fundProfileId = null,
        Guid? fundStructureNodeId = null,
        FundStructureNodeKindDto? fundStructureNodeKind = null,
        CancellationToken ct = default);

    Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default);

    Task<LedgerAccountTaxLotPolicyRecord> SaveTaxLotPolicyAsync(
        LedgerAccountTaxLotPolicyRecord policy,
        CancellationToken ct = default)
        => Task.FromException<LedgerAccountTaxLotPolicyRecord>(
            new NotSupportedException("This ledger journal store does not support tax-lot policy persistence."));

    Task<IReadOnlyList<LedgerAccountTaxLotPolicyRecord>> ListTaxLotPoliciesAsync(
        Guid ledgerBookId,
        CancellationToken ct = default)
        => Task.FromException<IReadOnlyList<LedgerAccountTaxLotPolicyRecord>>(
            new NotSupportedException("This ledger journal store does not support tax-lot policy persistence."));

    Task<LedgerTaxLotRecord> SaveTaxLotAsync(
        LedgerTaxLotRecord lot,
        CancellationToken ct = default)
        => Task.FromException<LedgerTaxLotRecord>(
            new NotSupportedException("This ledger journal store does not support tax-lot persistence."));

    Task<IReadOnlyList<LedgerTaxLotRecord>> ListOpenTaxLotsAsync(
        Guid ledgerBookId,
        LedgerAccount account,
        CancellationToken ct = default)
        => Task.FromException<IReadOnlyList<LedgerTaxLotRecord>>(
            new NotSupportedException("This ledger journal store does not support tax-lot persistence."));

    Task<IReadOnlyList<LedgerTaxLotRecord>> GetTaxLotsByIdsAsync(
        Guid ledgerBookId,
        IReadOnlyList<Guid> taxLotRecordIds,
        CancellationToken ct = default)
        => Task.FromException<IReadOnlyList<LedgerTaxLotRecord>>(
            new NotSupportedException("This ledger journal store does not support authoritative tax-lot identity reads."));

    /// <summary>
    /// Resolves one immutable atomic tax-lot posting batch together with its journal and mutation
    /// evidence. Public Posted projections use this read to prove that a supplied mutation-batch
    /// identity is durable authority rather than caller-provided lifecycle metadata.
    /// </summary>
    Task<AtomicTaxLotJournalResult?> GetAtomicTaxLotPostingAsync(
        Guid mutationBatchId,
        CancellationToken ct = default)
        => Task.FromException<AtomicTaxLotJournalResult?>(
            new NotSupportedException("This ledger journal store does not support atomic tax-lot authority reads."));
}

public interface IAtomicLedgerPeriodCloseStore
{
    /// <summary>
    /// Hard-closes a retained period only after rechecking revenue and expense balances while
    /// holding the same exclusion boundary used by journal appends. The balance guard, period
    /// CAS, and close-event insert must commit or roll back together.
    /// </summary>
    Task<LedgerAccountingPeriod> SaveHardClosedPeriodAsync(
        LedgerAccountingPeriod period,
        long expectedVersion,
        PeriodCloseEventRecord closeEvent,
        CancellationToken ct = default);
}

public interface ITransactionalLedgerJournalStore :
    ILedgerJournalStore,
    IAtomicLedgerPeriodCloseStore
{
    Task AppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerJournalEntryWrite entry,
        CancellationToken ct = default);
}

/// <summary>
/// Persists an immutable journal and its acquisition or disposal tax-lot consequences in one
/// serializable database transaction. Implementations must treat an identical batch fingerprint
/// as an exact replay and reject every partial-identity collision.
/// </summary>
public interface IAtomicTaxLotJournalStore
{
    Task<AtomicTaxLotJournalResult> AppendAssetPostingAsync(
        AtomicTaxLotJournalCommand command,
        CancellationToken ct = default);
}

/// <summary>
/// Optional optimized lookup for every retained journal that collides with a proposed posting
/// identity. Journal-entry and command identifiers are global identities; source-event and
/// idempotency identities are scoped to the accounting aggregate.
/// </summary>
public interface ILedgerPostingIdentityCollisionLookup
{
    Task<IReadOnlyList<LedgerJournalEntryRecord>> FindPostingIdentityCollisionsAsync(
        LedgerPostingIdentity identity,
        CancellationToken ct = default);
}

public sealed record LedgerPostingIdentity(
    Guid JournalEntryId,
    Guid AggregateId,
    Guid? CommandId,
    Guid? SourceEventId,
    string? IdempotencyKey)
{
    public static LedgerPostingIdentity FromWrite(LedgerJournalEntryWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(write.Entry);

        return new LedgerPostingIdentity(
            write.Entry.JournalEntryId,
            write.AggregateId,
            write.CommandId,
            write.SourceEventId,
            NormalizeOptional(write.Entry.Metadata.IdempotencyKey));
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Compatibility lookup for journal stores that predate the optimized collision contract.
/// Aggregate reads still enforce all aggregate-scoped identities and any global identity retained
/// by that aggregate. Durable stores should implement <see cref="ILedgerPostingIdentityCollisionLookup"/>
/// so journal-entry and command identities are checked across every aggregate efficiently.
/// </summary>
public static class LedgerPostingIdentityCollisionLookupExtensions
{
    public static async Task<IReadOnlyList<LedgerJournalEntryRecord>> FindPostingIdentityCollisionsAsync(
        this ILedgerJournalStore store,
        LedgerPostingIdentity identity,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.JournalEntryId == Guid.Empty)
            throw new ArgumentException("Journal entry id is required.", nameof(identity));
        if (identity.AggregateId == Guid.Empty)
            throw new ArgumentException("Aggregate id is required.", nameof(identity));

        if (store is ILedgerPostingIdentityCollisionLookup optimized)
        {
            return await optimized
                .FindPostingIdentityCollisionsAsync(identity, ct)
                .ConfigureAwait(false);
        }

        var retained = await store
            .GetByAggregateAsync(identity.AggregateId, ct)
            .ConfigureAwait(false);
        return retained
            .Where(record => IsCollision(record, identity))
            .ToArray();
    }

    internal static bool IsCollision(
        LedgerJournalEntryRecord record,
        LedgerPostingIdentity identity)
    {
        if (record.Entry.JournalEntryId == identity.JournalEntryId)
            return true;

        if (identity.CommandId.HasValue && record.CommandId == identity.CommandId)
            return true;

        if (record.AggregateId != identity.AggregateId)
            return false;

        if (identity.SourceEventId.HasValue && record.SourceEventId == identity.SourceEventId)
            return true;

        return identity.IdempotencyKey is not null
            && string.Equals(
                record.Entry.Metadata.IdempotencyKey?.Trim(),
                identity.IdempotencyKey,
                StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record LedgerJournalEntryWrite(
    JournalEntry Entry,
    Guid AggregateId,
    Guid PeriodId,
    Guid? CommandId = null,
    Guid? CorrelationId = null,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1",
    string? RuleId = null,
    string? RuleVersion = null,
    Guid? SourceEventId = null,
    Guid? SourceJournalEntryId = null,
    LedgerPostingKindDto PostingKind = LedgerPostingKindDto.Originating,
    LedgerAdjustmentApprovalMetadataDto? AdjustmentApproval = null,
    AccountingPostingCommandDto? PostingCommand = null,
    Guid? LedgerBookId = null);

public sealed record LedgerJournalEntryQuery(
    Guid? LedgerBookId = null,
    Guid? PeriodId = null,
    Guid? AggregateId = null,
    LedgerLineDimensionSet? LineDimensions = null,
    string? AccountName = null,
    DateTimeOffset? OccurredFrom = null,
    DateTimeOffset? OccurredTo = null,
    Guid? SourceEventId = null);

public sealed record LedgerJournalEntryRecord(
    JournalEntry Entry,
    Guid AggregateId,
    Guid PeriodId,
    Guid? CommandId,
    Guid? CorrelationId,
    long GlobalSequence,
    DateTimeOffset CreatedAt,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1",
    string? RuleId = null,
    string? RuleVersion = null,
    Guid? SourceEventId = null,
    Guid? SourceJournalEntryId = null,
    LedgerPostingKindDto PostingKind = LedgerPostingKindDto.Originating,
    LedgerAdjustmentApprovalMetadataDto? AdjustmentApproval = null);

public sealed record LedgerAccountingPeriod(
    Guid PeriodId,
    Guid? LedgerBookId,
    int FiscalYear,
    int PeriodNo,
    string Label,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    long Version);

public sealed record LedgerBookRecord(
    Guid LedgerBookId,
    string FundProfileId,
    Guid FundStructureNodeId,
    FundStructureNodeKindDto FundStructureNodeKind,
    string DisplayName,
    string BaseCurrency,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Description = null,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1");

public sealed record PeriodCloseEventRecord(
    Guid EventId,
    Guid PeriodId,
    string PriorStatus,
    string NewStatus,
    string ClosedBy,
    string Notes,
    DateTimeOffset RecordedAt);

/// <param name="WashSalePolicy">
/// Wash-sale deferral policy for this account, effective from the same date as the relief method.
/// Defaults to <see cref="Meridian.Ledger.WashSalePolicy.Disabled"/> so an account that has never
/// been configured keeps recognizing losses in full, exactly as before.
/// </param>
public sealed record LedgerAccountTaxLotPolicyRecord(
    Guid PolicyRecordId,
    Guid LedgerBookId,
    LedgerAccount Account,
    LedgerTaxLotReliefMethod ReliefMethod,
    string PolicyId,
    DateOnly EffectiveDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Rationale = null,
    WashSalePolicy? WashSalePolicy = null)
{
    /// <summary>The configured wash-sale policy, or the disabled default.</summary>
    public WashSalePolicy EffectiveWashSalePolicy => WashSalePolicy ?? Meridian.Ledger.WashSalePolicy.Disabled;
}

public sealed record LedgerTaxLotRecord(
    Guid TaxLotRecordId,
    Guid LedgerBookId,
    LedgerAccount Account,
    string LotId,
    DateOnly AcquiredDate,
    decimal OriginalQuantity,
    decimal OpenQuantity,
    decimal UnitCost,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? SourceJournalEntryId = null,
    string? EvidenceRef = null,
    long Version = 0,
    Guid? OriginatingMutationBatchId = null,
    Guid? LastMutationBatchId = null,
    Guid SecurityId = default,
    Guid BookPositionId = default);

public enum AtomicTaxLotMutationKind
{
    Acquisition = 0,
    Disposal = 1
}

public sealed record LedgerTaxLotDisposalSelection(
    Guid TaxLotRecordId,
    string LotId,
    long ExpectedVersion,
    decimal ExpectedOpenQuantity,
    decimal Quantity,
    int SelectionOrdinal,
    string SelectionEvidenceId,
    decimal ExpectedUnitCost = 0m,
    decimal ExpectedCostBasis = 0m);

public sealed record AtomicTaxLotJournalCommand(
    Guid MutationBatchId,
    Guid LedgerBookId,
    LedgerJournalEntryWrite Journal,
    Guid SourceEventId,
    string IdempotencyKey,
    string CanonicalFingerprint,
    long ExpectedPeriodVersion,
    AtomicTaxLotMutationKind MutationKind,
    IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence,
    LedgerTaxLotRecord? AcquisitionLot = null,
    IReadOnlyList<LedgerTaxLotDisposalSelection>? DisposalSelections = null,
    Guid? CorrectsMutationBatchId = null,
    string? ReliefMethod = null,
    string? PolicyRevision = null)
{
    public static AtomicTaxLotJournalCommand Create(
        Guid mutationBatchId,
        Guid ledgerBookId,
        LedgerJournalEntryWrite journal,
        Guid sourceEventId,
        string idempotencyKey,
        long expectedPeriodVersion,
        AtomicTaxLotMutationKind mutationKind,
        IReadOnlyList<RetainedEvidenceIdentityDto> retainedEvidence,
        LedgerTaxLotRecord? acquisitionLot = null,
        IReadOnlyList<LedgerTaxLotDisposalSelection>? disposalSelections = null,
        Guid? correctsMutationBatchId = null,
        string? reliefMethod = null,
        string? policyRevision = null)
    {
        var command = new AtomicTaxLotJournalCommand(
            mutationBatchId,
            ledgerBookId,
            journal,
            sourceEventId,
            idempotencyKey,
            CanonicalFingerprint: string.Empty,
            expectedPeriodVersion,
            mutationKind,
            retainedEvidence,
            acquisitionLot,
            disposalSelections,
            correctsMutationBatchId,
            reliefMethod,
            policyRevision);
        return command.WithComputedFingerprint();
    }

    public AtomicTaxLotJournalCommand WithComputedFingerprint()
        => this with { CanonicalFingerprint = AtomicTaxLotJournalFingerprint.Compute(this) };
}

public sealed record LedgerTaxLotMutationRecord(
    Guid MutationRecordId,
    Guid MutationBatchId,
    AtomicTaxLotMutationKind MutationKind,
    Guid TaxLotRecordId,
    string LotId,
    int SelectionOrdinal,
    decimal QuantityBefore,
    decimal QuantityDelta,
    decimal QuantityAfter,
    decimal UnitCost,
    decimal CostBasis,
    long ExpectedVersion,
    long ResultVersion,
    string SelectionEvidenceId,
    Guid JournalEntryId,
    Guid SourceEventId,
    IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence,
    DateTimeOffset RecordedAt,
    LedgerTaxLotRecord? LotBefore,
    LedgerTaxLotRecord LotAfter,
    Guid? CorrectsMutationBatchId = null,
    string? ReliefMethod = null,
    string? PolicyRevision = null,
    Guid SecurityId = default,
    Guid BookPositionId = default);

public sealed record AtomicTaxLotJournalResult(
    Guid MutationBatchId,
    AtomicTaxLotMutationKind MutationKind,
    string CanonicalFingerprint,
    bool IsExactReplay,
    LedgerJournalEntryRecord Journal,
    IReadOnlyList<LedgerTaxLotRecord> MutatedLots,
    IReadOnlyList<LedgerTaxLotMutationRecord> Mutations,
    IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence,
    Guid? CorrectsMutationBatchId = null,
    string? ReliefMethod = null,
    string? PolicyRevision = null);
