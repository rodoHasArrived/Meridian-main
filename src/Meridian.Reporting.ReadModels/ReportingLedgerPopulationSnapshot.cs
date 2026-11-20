using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>Frozen journals, tax relief and corporate-action lot evidence used by both activity rows and financial statements.</summary>
public sealed record ReportingLedgerPopulationSnapshot(
    LedgerAmountScopeDto Scope,
    LedgerReportPackRequest Request,
    ImmutableArray<LedgerJournalEntryRecord> Journals,
    ImmutableArray<LedgerTaxLotReliefProjection> TaxLotReliefProjections)
{
    public ImmutableArray<ReportLedgerAmountBindingDto> ReportAmounts { get; init; } = [];

    public long PeriodVersion { get; init; }

    public ImmutableArray<IReadOnlyDictionary<string, string>> DatasetRows { get; init; } = [];

    /// <summary>Corporate-action lot evidence captured with the journals; empty for legacy populations.</summary>
    public ImmutableArray<CanonicalCorporateActionLotReport> CorporateActionLotReports { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LedgerPeriodCloseSnapshot? ComparativeClose { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Relief posting lines are value tuples; their fields must survive retained replay.
        IncludeFields = true
    };

    public ReportingRetainedLedgerPopulation Retain()
    {
        var payload = JsonSerializer.Serialize(this, JsonOptions);
        var hash = Sha256Digest.ComputeUtf8(payload);
        return new ReportingRetainedLedgerPopulation(
            $"ledger-population-{hash[..32]}", hash, payload,
            Journals.IsEmpty ? 0 : Journals.Max(static journal => journal.GlobalSequence),
            Journals.Length, Journals.Sum(static journal => journal.Entry.Lines.Count));
    }

    public static ReportingLedgerPopulationSnapshot Decode(ReportingAuthoritativeSourceCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var population = checkpoint.LedgerPopulation
            ?? throw new ReportingGovernanceException("The report has no retained ledger population; recertification is required.");
        if (string.IsNullOrWhiteSpace(population.PayloadJson)
            || !Sha256Digest.IsWellFormed(population.ContentHashSha256)
            || !Sha256Digest.FixedEquals(population.ContentHashSha256, Sha256Digest.ComputeUtf8(population.PayloadJson))
            || population.SnapshotId != $"ledger-population-{population.ContentHashSha256[..32]}"
            || checkpoint.EvidenceIds.IsDefaultOrEmpty
            || checkpoint.EvidenceIds.Any(string.IsNullOrWhiteSpace)
            || checkpoint.EvidenceIds.Count(evidence => evidence.StartsWith("ledger-population:", StringComparison.Ordinal)) != 1
            || !checkpoint.EvidenceIds.Contains($"ledger-population:{population.SnapshotId}:{population.ContentHashSha256}"))
            throw new ReportingGovernanceException("The retained ledger population digest or checkpoint binding is invalid.");

        ReportingLedgerPopulationSnapshot snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<ReportingLedgerPopulationSnapshot>(population.PayloadJson, JsonOptions)
                ?? throw new JsonException("Empty retained ledger population.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or LedgerValidationException)
        {
            throw new ReportingGovernanceException($"The retained ledger population cannot be replayed: {exception.Message}");
        }

        if (snapshot.Scope is null || snapshot.Request is null
            || snapshot.Journals.IsDefault || snapshot.TaxLotReliefProjections.IsDefault
            || snapshot.DatasetRows.IsDefault || snapshot.CorporateActionLotReports.IsDefault
            || snapshot.Journals.Any(static journal => journal is null || journal.Entry is null)
            || snapshot.DatasetRows.Any(static row => row is null || row.Values.Any(static value => value is null))
            || snapshot.Scope.TenantId != checkpoint.TenantId
            || snapshot.Scope.CompanyId != checkpoint.CompanyId
            || snapshot.Scope.FundProfileId != checkpoint.FundId
            || snapshot.Scope.LedgerBookId.ToString("D") != checkpoint.LedgerBookId
            || snapshot.Scope.PeriodId.ToString("D") != checkpoint.AccountingPeriodId
            || snapshot.Request.FundId != checkpoint.FundId
            || snapshot.Request.PeriodId != checkpoint.AccountingPeriodId
            || snapshot.Request.AsOf != checkpoint.CutoffUtc
            || snapshot.Journals.Length != population.JournalEntryCount
            || snapshot.Journals.Sum(static journal => journal.Entry.Lines.Count) != population.LedgerLineCount
            || (snapshot.Journals.IsEmpty ? 0 : snapshot.Journals.Max(static journal => journal.GlobalSequence)) != population.HighestGlobalSequence
            || population.HighestGlobalSequence != checkpoint.HighestGlobalSequence
            || snapshot.Journals.Select(static journal => journal.GlobalSequence).Distinct().Count() != snapshot.Journals.Length
            || snapshot.Journals.Any(journal => journal.GlobalSequence <= 0 || journal.PeriodId == Guid.Empty
                || journal.AccountingBasis.ToString() != checkpoint.AccountingBasis
                || journal.Entry.Timestamp > checkpoint.CutoffUtc
                || journal.Entry.Lines.Any(line => line.Dimensions?.FundId != checkpoint.FundId
                    || line.Dimensions?.BookId != checkpoint.LedgerBookId
                    || line.Dimensions?.OrganizationId != checkpoint.OrganizationId)))
            throw new ReportingGovernanceException("The retained ledger population is outside the certified accounting scope or sequence boundary.");
        if (!Sha256Digest.FixedEquals(checkpoint.CheckpointHash, snapshot.ComputeCheckpointHash(checkpoint))
            || checkpoint.CheckpointId != ReportingRetainedLedgerPopulationValidation.BuildCheckpointId(checkpoint.CheckpointHash))
            throw new ReportingGovernanceException("The retained population does not reproduce the certified source checkpoint digest.");
        return snapshot;
    }

    public string ComputeCheckpointHash(ReportingAuthoritativeSourceCheckpoint checkpoint)
        => ReportingRetainedLedgerPopulationValidation.ComputeCheckpointHash(
            checkpoint, Scope.LedgerBookId, Scope.PeriodId, PeriodVersion, DatasetRows);

    public LedgerFinancialReportPack Replay()
    {
        var ledger = new Meridian.Ledger.Ledger();
        foreach (var record in Journals.OrderBy(static record => record.GlobalSequence))
            ledger.Post(record.Entry);
        return LedgerReportPackBuilder.Build(ledger, Request, taxLotReliefProjections: TaxLotReliefProjections,
            corporateActionLotReports: CorporateActionLotReports, priorStatementControl: ReplayComparativeClose());
    }

    private LedgerStatementControlPack? ReplayComparativeClose()
    {
        if (Request.StatementControl?.Comparison is not { } comparison)
        {
            if (ComparativeClose is not null)
                throw new LedgerValidationException("A retained comparative close requires an exact statement definition.");
            return null;
        }

        if (ComparativeClose is not { } snapshot || Request.LockedPeriod is null
            || snapshot.LedgerBookId != Scope.LedgerBookId || snapshot.PeriodId != comparison.PeriodId
            || snapshot.PeriodVersion != comparison.CloseSnapshotVersion
            || !Sha256Digest.FixedEquals(snapshot.ContentHashSha256, comparison.CloseSnapshotHashSha256))
            throw new LedgerValidationException("The comparative period lacks its exact retained close snapshot.");

        LedgerPeriodCloseSnapshotPayload prior;
        try { prior = snapshot.VerifyIntegrity(); }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw new LedgerValidationException("The comparative close snapshot failed integrity verification.", exception);
        }
        var start = new DateTimeOffset(prior.Period.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(prior.Period.EndDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        if (prior.Period.StartDate != comparison.StartDate || prior.Period.EndDate != comparison.EndDate
            || end >= Request.PeriodStart || prior.Entries.Any(item => item.AccountingBasis != Request.StatementControl.AccountingBasis)
            || !Sha256Digest.FixedEquals(snapshot.ContentHashSha256,
                LedgerPeriodCloseSnapshot.Capture(prior.Period, prior.CloseEvent,
                    Journals.Where(item => item.Entry.Timestamp <= end).ToArray()).ContentHashSha256))
            throw new LedgerValidationException("The comparative hard-close population differs from the corresponding current retained history.");

        var definition = new LedgerStatementControlDefinition(Request.StatementControl.Version, Request.StatementControl.AccountingBasis)
        {
            CloseSnapshotVersion = comparison.CloseSnapshotVersion,
            CloseSnapshotHashSha256 = comparison.CloseSnapshotHashSha256,
            CashFxRevaluationJournalIds = comparison.CashFxRevaluationJournalIds
        };
        var request = new LedgerReportPackRequest($"{Request.ReportId}-comparative-{snapshot.PeriodId:N}", Request.FundId,
            snapshot.PeriodId.ToString("D"), start, end, end, Request.BaseCurrency, Request.GeneratedBy, snapshot.ClosedAt,
            lineDimensions: Request.LineDimensions) { StatementControl = definition };
        var ledger = new Meridian.Ledger.Ledger();
        foreach (var record in prior.Entries.OrderBy(static item => item.GlobalSequence))
            ledger.Post(record.Entry);
        return LedgerReportPackBuilder.Build(ledger, request).StatementControl;
    }
}
