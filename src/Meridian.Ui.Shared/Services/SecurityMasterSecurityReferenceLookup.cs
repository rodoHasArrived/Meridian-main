using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Adapts Security Master query services to workstation-facing symbol enrichment.
/// </summary>
public sealed class SecurityMasterSecurityReferenceLookup : ISecurityReferenceLookup
{
    private readonly ISecurityMasterQueryService _queryService;

    public SecurityMasterSecurityReferenceLookup(ISecurityMasterQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    public async Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        var detail = await _queryService
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, symbol, provider: null, ct)
            .ConfigureAwait(false);
        if (detail is null)
        {
            return null;
        }

        var primaryIdentifier = detail.Identifiers
            .FirstOrDefault(static identifier => identifier.IsPrimary)?.Value
            ?? detail.Identifiers.FirstOrDefault()?.Value;

        return new WorkstationSecurityReference(
            SecurityId: detail.SecurityId,
            DisplayName: detail.DisplayName,
            AssetClass: detail.AssetClass,
            Currency: detail.Currency,
            Status: detail.Status,
            PrimaryIdentifier: primaryIdentifier,
            SubType: DeriveSubType(detail.AssetClass),
            CoverageStatus: WorkstationSecurityCoverageStatus.Resolved,
            MatchedIdentifierKind: SecurityIdentifierKind.Ticker.ToString(),
            MatchedIdentifierValue: symbol,
            MatchedProvider: null,
            ResolutionReason: null);
    }

    /// <summary>
    /// Derives the most likely sub-type from the asset class string without requiring a full
    /// aggregate rebuild. Returns null for asset classes that do not have a unique sub-type
    /// (e.g. Equity, which can be CommonShare, Adr, or ReitShare).
    /// </summary>
    internal static string? DeriveSubType(string? assetClass) => assetClass switch
    {
        "Bond" => "Bond",
        "TreasuryBill" => "TreasuryBill",
        "Option" => "OptionContract",
        "Future" => "FutureContract",
        "Swap" => "SwapContract",
        "DirectLoan" => "DirectLoan",
        "Deposit" => "Deposit",
        "MoneyMarketFund" => "MoneyMarket",
        "CertificateOfDeposit" => "CertificateOfDeposit",
        "CommercialPaper" => "CommercialPaper",
        "Repo" => "Repo",
        _ => null
    };
}
