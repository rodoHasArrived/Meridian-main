using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Meridian.Contracts.Ledger.LedgerDimensionTags;

namespace Meridian.Reporting;

/// <summary>
/// Generates report packs that combine Security Master instrument definitions,
/// ledger snapshots, and portfolio data into structured governance reports.
/// </summary>
public sealed class ReportGenerationService
{
    private readonly ISecurityMasterQueryService _securityMaster;
    private readonly ILogger<ReportGenerationService> _log;

    public ReportGenerationService(
        ISecurityMasterQueryService securityMaster,
        ILogger<ReportGenerationService>? log = null)
    {
        _securityMaster = securityMaster ?? throw new ArgumentNullException(nameof(securityMaster));
        _log = log ?? NullLogger<ReportGenerationService>.Instance;
    }

    public async Task<ReportPack> GenerateAsync(
        ReportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _log.LogInformation(
            "Generating {ReportKind} report for {FundId} asOf {AsOf}",
            request.ReportKind, request.FundId, request.AsOf);

        // Freeze the exact journal boundary first. Every balance, dimension, and receipt count is
        // derived from this one materialized set so a report cannot mix pre- and post-as-of state.
        var frozenJournal = request.FundLedger
            .ConsolidatedJournalEntries()
            .Where(entry => entry.Timestamp <= request.AsOf)
            .ToArray();
        var trialBalance = BuildTrialBalance(frozenJournal);
        var dimensionsByAccount = BuildDimensionsByAccount(frozenJournal);
        var referencesBySymbol = await CaptureSecurityReferencesAsync(
                trialBalance.Keys,
                request.AsOf,
                ct)
            .ConfigureAwait(false);
        var snapshotInputs = trialBalance
            .Select(pair => new ReportingSnapshotRowInput(
                pair.Key.Name,
                pair.Key.AccountType.ToString(),
                pair.Key.Symbol,
                pair.Value,
                dimensionsByAccount.GetValueOrDefault(pair.Key),
                pair.Key.Symbol is null
                    ? null
                    : referencesBySymbol.GetValueOrDefault(pair.Key.Symbol)))
            .ToArray();
        var snapshot = CertifiedReportingSnapshotBuilder.Build(
            request.FundId,
            request.AsOf,
            request.ReportKind,
            request.SnapshotSource,
            snapshotInputs,
            frozenJournal.Length,
            frozenJournal.Sum(static entry => entry.Lines.Count));
        var assetClassSections = BuildAssetClassSections(snapshot.Rows);

        return new ReportPack(
            ReportId: Guid.NewGuid(),
            FundId: request.FundId,
            ReportKind: request.ReportKind,
            AsOf: request.AsOf,
            GeneratedAt: DateTimeOffset.UtcNow,
            TrialBalance: snapshot.Rows,
            AssetClassSections: assetClassSections,
            TotalNetAssets: snapshot.Rows.Sum(static row => row.NetBalance),
            SnapshotReceipt: snapshot.Receipt);
    }

    private async Task<IReadOnlyDictionary<string, SecurityMasterReportingReference?>> CaptureSecurityReferencesAsync(
        IEnumerable<LedgerAccount> accounts,
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        var references = new Dictionary<string, SecurityMasterReportingReference?>(
            StringComparer.OrdinalIgnoreCase);
        var symbols = accounts
            .Select(static account => account.Symbol)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(static symbol => symbol!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static symbol => symbol, StringComparer.Ordinal)
            .ToArray();
        foreach (var symbol in symbols)
        {
            ct.ThrowIfCancellationRequested();
            references[symbol] = await SecurityMasterReportingLookup
                .TryGetReportingReferenceByTickerAsync(_securityMaster, _log, symbol, asOf, ct)
                .ConfigureAwait(false);
        }

        return references;
    }

    private static IReadOnlyDictionary<LedgerAccount, decimal> BuildTrialBalance(
        IReadOnlyList<JournalEntry> journalEntries)
    {
        var totals = new Dictionary<LedgerAccount, (decimal Debits, decimal Credits)>();
        foreach (var line in journalEntries.SelectMany(static entry => entry.Lines))
        {
            totals.TryGetValue(line.Account, out var current);
            totals[line.Account] = (current.Debits + line.Debit, current.Credits + line.Credit);
        }

        return totals.ToDictionary(
            static pair => pair.Key,
            static pair => Meridian.Ledger.Ledger.CalculateNetBalance(
                pair.Key,
                pair.Value.Debits,
                pair.Value.Credits));
    }

    private static IReadOnlyDictionary<LedgerAccount, LedgerDimensionSetDto> BuildDimensionsByAccount(
        IReadOnlyList<JournalEntry> journalEntries)
    {
        var accumulators = new Dictionary<LedgerAccount, AccountDimensionAccumulator>();

        foreach (var journalEntry in journalEntries)
        {
            foreach (var line in journalEntry.Lines)
            {
                if (!accumulators.TryGetValue(line.Account, out var accumulator))
                {
                    accumulator = new AccountDimensionAccumulator();
                    accumulators[line.Account] = accumulator;
                }

                if (line.Dimensions is null)
                {
                    accumulator.HasUndimensionedLine = true;
                    continue;
                }

                accumulator.Dimensions.Add(line.Dimensions);
            }
        }

        return accumulators
            .Select(pair => (pair.Key, Dimensions: pair.Value.ToMergedDimensions()))
            .Where(static pair => pair.Dimensions is not null)
            .ToDictionary(static pair => pair.Key, static pair => pair.Dimensions!);
    }

    internal static LedgerDimensionSetDto? MergeDimensions(IReadOnlyList<LedgerLineDimensionSet> dimensions)
    {
        if (dimensions.Count == 0)
        {
            return null;
        }

        var merged = new LedgerDimensionSetDto(
            FundId: SingleString(dimensions, static item => item.FundId),
            EntityId: SingleString(dimensions, static item => item.EntityId),
            SleeveId: SingleString(dimensions, static item => item.SleeveId),
            StrategyId: SingleString(dimensions, static item => item.StrategyId),
            InvestorId: SingleString(dimensions, static item => item.InvestorId),
            CapitalAccountId: SingleString(dimensions, static item => item.CapitalAccountId),
            InstrumentId: SingleGuid(dimensions, static item => item.InstrumentId),
            TaxLotId: SingleString(dimensions, static item => item.TaxLotId),
            CostCenterId: SingleString(dimensions, static item => item.CostCenterId),
            CounterpartyId: SingleString(dimensions, static item => item.CounterpartyId),
            ExternalGlDimensions: MergeExternalGlDimensions(dimensions),
            OrganizationId: SingleString(dimensions, static item => item.OrganizationId),
            PortfolioId: SingleString(dimensions, static item => item.PortfolioId),
            BookId: SingleString(dimensions, static item => item.BookId),
            AccountId: SingleString(dimensions, static item => item.AccountId),
            CustomerId: SingleString(dimensions, static item => item.CustomerId),
            VendorId: SingleString(dimensions, static item => item.VendorId),
            ProjectId: SingleString(dimensions, static item => item.ProjectId))
        {
            PositionId = SingleGuid(dimensions, static item => item.PositionId)
        };

        return HasAnyDimension(merged) ? merged : null;
    }

    private static string? SingleString(
        IReadOnlyList<LedgerLineDimensionSet> dimensions,
        Func<LedgerLineDimensionSet, string?> selector)
    {
        var values = dimensions
            .Select(selector)
            .Select(TrimOrNull)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static Guid? SingleGuid(
        IReadOnlyList<LedgerLineDimensionSet> dimensions,
        Func<LedgerLineDimensionSet, Guid?> selector)
    {
        var values = dimensions
            .Select(selector)
            .Distinct()
            .ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static IReadOnlyDictionary<string, string> MergeExternalGlDimensions(
        IReadOnlyList<LedgerLineDimensionSet> dimensions)
    {
        var keys = dimensions
            .SelectMany(static item => item.ExternalGlDimensions.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            var values = dimensions
                .Select(item => item.ExternalGlDimensions.TryGetValue(key, out var value) ? TrimOrNull(value) : null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (values is [string value])
            {
                merged[key] = value;
            }
        }

        return merged;
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<AssetClassSection> BuildAssetClassSections(
        IReadOnlyList<EnrichedLedgerRow> rows)
        => rows
            .GroupBy(r => r.AssetFamily ?? r.AssetClass ?? "Unclassified", StringComparer.OrdinalIgnoreCase)
            .Select(g => new AssetClassSection(
                AssetClass: g.Key,
                Rows: g.ToList(),
                Total: g.Sum(r => r.NetBalance)))
            .OrderBy(s => s.AssetClass, StringComparer.Ordinal)
            .ToList();

}

/// <summary>Type of governance report to generate.</summary>
public enum ReportKind
{
    TrialBalance,
    NavSummary,
    AssetAllocation,
    ReconciliationPack,
    PerformanceReport,
    HoldingsReport,
    CapitalAccountStatement,
    InvestorStatement,
    BoardPacket,
    AuditPackage,
    CertifiedDataset,
    CustomReport
}

/// <summary>Request payload for <see cref="ReportGenerationService.GenerateAsync"/>.</summary>
public sealed record ReportRequest(
    string FundId,
    DateTimeOffset AsOf,
    FundLedgerBook FundLedger,
    ReportKind ReportKind = ReportKind.TrialBalance,
    ReportingSnapshotSourceContext? SnapshotSource = null);

/// <summary>A single ledger row enriched with Security Master classification data.</summary>
public sealed record EnrichedLedgerRow(
    string AccountName,
    string AccountType,
    string? Symbol,
    string? Currency,
    string? AssetClass,
    string? PrimaryIdentifierKind,
    string? PrimaryIdentifierValue,
    string? SubType,
    string? AssetFamily,
    string? IssuerType,
    string? RiskCountry,
    string LookupQuality,
    string? DisplayName,
    decimal NetBalance,
    LedgerDimensionSetDto? Dimensions = null);

file sealed class AccountDimensionAccumulator
{
    public bool HasUndimensionedLine { get; set; }

    public List<LedgerLineDimensionSet> Dimensions { get; } = [];

    public LedgerDimensionSetDto? ToMergedDimensions() =>
        HasUndimensionedLine ? null : ReportGenerationService.MergeDimensions(Dimensions);
}

/// <summary>A section of a report grouped by asset class.</summary>
public sealed record AssetClassSection(
    string AssetClass,
    IReadOnlyList<EnrichedLedgerRow> Rows,
    decimal Total);

/// <summary>A complete report pack ready for distribution or storage.</summary>
public sealed record ReportPack(
    Guid ReportId,
    string FundId,
    ReportKind ReportKind,
    DateTimeOffset AsOf,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<EnrichedLedgerRow> TrialBalance,
    IReadOnlyList<AssetClassSection> AssetClassSections,
    decimal TotalNetAssets,
    ReportingSnapshotReceipt? SnapshotReceipt = null);
