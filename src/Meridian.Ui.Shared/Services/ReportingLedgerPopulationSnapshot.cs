using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>Frozen journals and tax relief used by both activity rows and financial statements.</summary>
public sealed record ReportingLedgerPopulationSnapshot(
    LedgerAmountScopeDto Scope,
    LedgerReportPackRequest Request,
    ImmutableArray<LedgerJournalEntryRecord> Journals,
    ImmutableArray<LedgerTaxLotReliefProjection> TaxLotReliefProjections)
{
    public ImmutableArray<ReportLedgerAmountBindingDto> ReportAmounts { get; init; } = [];

    public long PeriodVersion { get; init; }

    public ImmutableArray<IReadOnlyDictionary<string, string>> DatasetRows { get; init; } = [];

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
            || snapshot.DatasetRows.IsDefault || snapshot.Journals.Any(static journal => journal is null || journal.Entry is null)
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
            || checkpoint.CheckpointId != $"ledger-checkpoint-{checkpoint.CheckpointHash[..32]}")
            throw new ReportingGovernanceException("The retained population does not reproduce the certified source checkpoint digest.");
        return snapshot;
    }

    public string ComputeCheckpointHash(ReportingAuthoritativeSourceCheckpoint checkpoint)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("sourceKind", checkpoint.SourceKind);
            writer.WriteString("tenantId", checkpoint.TenantId);
            writer.WriteString("organizationId", checkpoint.OrganizationId);
            writer.WriteString("companyId", checkpoint.CompanyId);
            writer.WriteString("fundId", checkpoint.FundId);
            writer.WriteString("ledgerBookId", Scope.LedgerBookId);
            writer.WriteString("accountingPeriodId", Scope.PeriodId);
            writer.WriteString("accountingBasis", checkpoint.AccountingBasis);
            writer.WriteString("asOfDate", checkpoint.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteString("cutoffUtc", checkpoint.CutoffUtc.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteNumber("periodVersion", PeriodVersion);
            writer.WriteNumber("highestGlobalSequence", checkpoint.HighestGlobalSequence);
            writer.WriteString("ledgerPopulationHash", checkpoint.LedgerPopulation!.ContentHashSha256);
            writer.WriteStartArray("rows");
            foreach (var row in DatasetRows)
            {
                writer.WriteStartObject();
                foreach (var pair in row.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                    writer.WriteString(pair.Key, pair.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Sha256Digest.Compute(stream.ToArray());
    }

    public LedgerFinancialReportPack Replay()
    {
        var ledger = new Meridian.Ledger.Ledger();
        foreach (var record in Journals.OrderBy(static record => record.GlobalSequence))
            ledger.Post(record.Entry);
        return LedgerReportPackBuilder.Build(ledger, Request, taxLotReliefProjections: TaxLotReliefProjections);
    }
}
