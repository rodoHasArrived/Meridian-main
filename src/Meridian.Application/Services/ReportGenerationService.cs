using Meridian.Application.Logging;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Generates report-packs that combine Security Master instrument definitions,
/// ledger snapshots, and portfolio data into structured governance reports.
/// </summary>
public sealed class ReportGenerationService
{
    private readonly ISecurityMasterQueryService _securityMaster;
    private readonly ILogger _log = LoggingSetup.ForContext<ReportGenerationService>();

    public ReportGenerationService(ISecurityMasterQueryService securityMaster)
    {
        _securityMaster = securityMaster ?? throw new ArgumentNullException(nameof(securityMaster));
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a full report pack for the given fund ledger snapshot.
    /// The pack includes a trial-balance section, per-asset-class breakdown,
    /// and security-enrichment rows for every symbol that can be resolved.
    /// </summary>
    public async Task<ReportPack> GenerateAsync(
        ReportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _log.Information(
            "Generating {ReportKind} report for {FundId} asOf {AsOf}",
            request.ReportKind, request.FundId, request.AsOf);

        var trialBalance = request.FundLedger.ConsolidatedTrialBalance();

        // Resolve Security Master details for all symbols present in the ledger.
        var enrichedRows = await EnrichWithSecurityMasterAsync(trialBalance, ct)
            .ConfigureAwait(false);

        var assetClassSections = BuildAssetClassSections(enrichedRows);

        return new ReportPack(
            ReportId:    Guid.NewGuid(),
            FundId:      request.FundId,
            ReportKind:  request.ReportKind,
            AsOf:        request.AsOf,
            GeneratedAt: DateTimeOffset.UtcNow,
            TrialBalance: enrichedRows,
            AssetClassSections: assetClassSections,
            TotalNetAssets: enrichedRows.Sum(r => r.NetBalance));
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<EnrichedLedgerRow>> EnrichWithSecurityMasterAsync(
        IReadOnlyDictionary<LedgerAccount, decimal> trialBalance,
        CancellationToken ct)
    {
        var rows = new List<EnrichedLedgerRow>(trialBalance.Count);

        foreach (var (account, balance) in trialBalance)
        {
            ct.ThrowIfCancellationRequested();

            SecurityDetailDto? detail = null;
            if (!string.IsNullOrWhiteSpace(account.Symbol))
            {
                try
                {
                    detail = await _securityMaster
                        .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, account.Symbol, null, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Debug(ex, "Could not enrich ledger account {Symbol} from Security Master", account.Symbol);
                }
            }

            rows.Add(new EnrichedLedgerRow(
                AccountName:  account.Name,
                AccountType:  account.AccountType.ToString(),
                Symbol:       account.Symbol,
                Currency:     detail?.Currency,
                AssetClass:   detail?.AssetClass,
                DisplayName:  detail?.DisplayName,
                NetBalance:   balance));
        }

        return rows
            .OrderBy(r => r.AccountType)
            .ThenBy(r => r.AccountName, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<AssetClassSection> BuildAssetClassSections(
        IReadOnlyList<EnrichedLedgerRow> rows)
        => rows
            .GroupBy(r => r.AssetClass ?? "Unclassified", StringComparer.OrdinalIgnoreCase)
            .Select(g => new AssetClassSection(
                AssetClass: g.Key,
                Rows:       g.ToList(),
                Total:      g.Sum(r => r.NetBalance)))
            .OrderBy(s => s.AssetClass, StringComparer.Ordinal)
            .ToList();
}

// ── Request / result models ────────────────────────────────────────────────────

/// <summary>Type of governance report to generate.</summary>
public enum ReportKind
{
    TrialBalance,
    NavSummary,
    AssetAllocation,
    ReconciliationPack
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
