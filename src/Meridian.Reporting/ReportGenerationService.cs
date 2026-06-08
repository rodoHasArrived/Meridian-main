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

        var enrichedRows = await EnrichWithSecurityMasterAsync(trialBalance, ct)
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
        CancellationToken ct)
    {
        var rows = new List<EnrichedLedgerRow>(trialBalance.Count);

        foreach (var (account, balance) in trialBalance)
        {
            ct.ThrowIfCancellationRequested();

            SecurityDetailDto? detail = null;
            SecurityEconomicDefinitionRecord? economicDefinition = null;
            if (!string.IsNullOrWhiteSpace(account.Symbol))
            {
                try
                {
                    detail = await _securityMaster
                        .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, account.Symbol, null, ct)
                        .ConfigureAwait(false);

                    if (detail is not null)
                    {
                        economicDefinition = await _securityMaster
                            .GetEconomicDefinitionByIdAsync(detail.SecurityId, ct)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Could not enrich ledger account {Symbol} from Security Master", account.Symbol);
                }
            }

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
                NetBalance: balance));
        }

        return rows
            .OrderBy(r => r.AccountType)
            .ThenBy(r => r.AccountName, StringComparer.Ordinal)
            .ToList();
    }

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
    decimal NetBalance);

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
