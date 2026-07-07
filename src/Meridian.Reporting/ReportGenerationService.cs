using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

        var trialBalance = request.FundLedger.ConsolidatedTrialBalance();
        var dimensionsByAccount = BuildDimensionsByAccount(request.FundLedger);

        var enrichedRows = await EnrichWithSecurityMasterAsync(trialBalance, dimensionsByAccount, ct)
            .ConfigureAwait(false);

        var assetClassSections = BuildAssetClassSections(enrichedRows);

        return new ReportPack(
            ReportId: Guid.NewGuid(),
            FundId: request.FundId,
            ReportKind: request.ReportKind,
            AsOf: request.AsOf,
            GeneratedAt: DateTimeOffset.UtcNow,
            TrialBalance: enrichedRows,
            AssetClassSections: assetClassSections,
            TotalNetAssets: enrichedRows.Sum(r => r.NetBalance));
    }

    private async Task<IReadOnlyList<EnrichedLedgerRow>> EnrichWithSecurityMasterAsync(
        IReadOnlyDictionary<LedgerAccount, decimal> trialBalance,
        IReadOnlyDictionary<LedgerAccount, LedgerDimensionSetDto> dimensionsByAccount,
        CancellationToken ct)
    {
        var rows = new List<EnrichedLedgerRow>(trialBalance.Count);

        foreach (var (account, balance) in trialBalance)
        {
            ct.ThrowIfCancellationRequested();

            var detail = await SecurityMasterReportingLookup
                .TryGetByTickerAsync(_securityMaster, _log, account.Symbol, ct)
                .ConfigureAwait(false);
            var economicDefinition = detail is null
                ? null
                : await SecurityMasterReportingLookup
                    .TryGetEconomicDefinitionAsync(_securityMaster, _log, detail.SecurityId, account.Symbol, ct)
                    .ConfigureAwait(false);

            var primaryIdentifier = economicDefinition?.Identifiers
                .FirstOrDefault(static identifier => identifier.IsPrimary);
            var lookupQuality = ResolveLookupQuality(detail, economicDefinition, primaryIdentifier);

            rows.Add(new EnrichedLedgerRow(
                AccountName: account.Name,
                AccountType: account.AccountType.ToString(),
                Symbol: account.Symbol,
                Currency: detail?.Currency,
                AssetClass: detail?.AssetClass,
                PrimaryIdentifierKind: primaryIdentifier?.Kind.ToString(),
                PrimaryIdentifierValue: primaryIdentifier?.Value,
                SubType: economicDefinition?.SubType,
                AssetFamily: economicDefinition?.AssetFamily,
                IssuerType: economicDefinition?.IssuerType,
                RiskCountry: economicDefinition?.RiskCountry,
                LookupQuality: lookupQuality,
                DisplayName: detail?.DisplayName,
                NetBalance: balance,
                Dimensions: dimensionsByAccount.GetValueOrDefault(account)));
        }

        return rows
            .OrderBy(r => r.AccountType)
            .ThenBy(r => r.AccountName, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyDictionary<LedgerAccount, LedgerDimensionSetDto> BuildDimensionsByAccount(
        FundLedgerBook fundLedger)
    {
        var accumulators = new Dictionary<LedgerAccount, AccountDimensionAccumulator>();

        foreach (var journalEntry in fundLedger.ConsolidatedJournalEntries())
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
            ProjectId: SingleString(dimensions, static item => item.ProjectId));

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

    private static bool HasAnyDimension(LedgerDimensionSetDto dimensions) =>
        dimensions.FundId is not null ||
        dimensions.EntityId is not null ||
        dimensions.SleeveId is not null ||
        dimensions.StrategyId is not null ||
        dimensions.InvestorId is not null ||
        dimensions.CapitalAccountId is not null ||
        dimensions.InstrumentId is not null ||
        dimensions.TaxLotId is not null ||
        dimensions.CostCenterId is not null ||
        dimensions.CounterpartyId is not null ||
        dimensions.ExternalGlDimensions.Count > 0 ||
        dimensions.OrganizationId is not null ||
        dimensions.PortfolioId is not null ||
        dimensions.BookId is not null ||
        dimensions.AccountId is not null ||
        dimensions.CustomerId is not null ||
        dimensions.VendorId is not null ||
        dimensions.ProjectId is not null;

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

    private static string ResolveLookupQuality(
        SecurityDetailDto? detail,
        SecurityEconomicDefinitionRecord? economicDefinition,
        SecurityIdentifierDto? primaryIdentifier)
    {
        if (detail is null)
            return "missing";

        if (economicDefinition is null)
            return "partial";

        var hasPrimaryIdentifier = primaryIdentifier is not null
                                   && !string.IsNullOrWhiteSpace(primaryIdentifier.Value);
        var hasGovernanceDimensions =
            !string.IsNullOrWhiteSpace(economicDefinition.SubType)
            && !string.IsNullOrWhiteSpace(economicDefinition.AssetFamily)
            && !string.IsNullOrWhiteSpace(economicDefinition.IssuerType)
            && !string.IsNullOrWhiteSpace(economicDefinition.RiskCountry);

        return hasPrimaryIdentifier && hasGovernanceDimensions
            ? "resolved"
            : "partial";
    }
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
    ReportKind ReportKind = ReportKind.TrialBalance);

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
    decimal TotalNetAssets);
