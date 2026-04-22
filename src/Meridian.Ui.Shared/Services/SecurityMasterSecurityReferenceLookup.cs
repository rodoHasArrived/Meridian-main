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

    public Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
        => GetByCanonicalAsync(
            new SecurityReferenceLookupRequest(
                IdentifierKind: SecurityIdentifierKind.Ticker.ToString(),
                IdentifierValue: symbol,
                Symbol: symbol,
                Source: "symbol"),
            ct);

    public async Task<WorkstationSecurityReference?> GetByCanonicalAsync(
        SecurityReferenceLookupRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedIdentifierKind = request.IdentifierKind;
        var normalizedIdentifierValue = request.IdentifierValue;
        var lookupPath = "none";
        SecurityDetailDto? detail = null;

        if (request.SecurityId is Guid securityId)
        {
            detail = await _queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            lookupPath = "security-id";
        }

        if (detail is null)
        {
            var identifierValue = normalizedIdentifierValue;
            if (string.IsNullOrWhiteSpace(identifierValue))
            {
                identifierValue = string.IsNullOrWhiteSpace(request.Symbol) ? null : request.Symbol;
                if (identifierValue is not null && string.IsNullOrWhiteSpace(normalizedIdentifierKind))
                {
                    normalizedIdentifierKind = SecurityIdentifierKind.Ticker.ToString();
                }
            }

            if (!string.IsNullOrWhiteSpace(identifierValue))
            {
                if (!Enum.TryParse<SecurityIdentifierKind>(normalizedIdentifierKind, ignoreCase: true, out var kind))
                {
                    kind = SecurityIdentifierKind.Ticker;
                }

                detail = await _queryService
                    .GetByIdentifierAsync(kind, identifierValue, request.Venue, ct)
                    .ConfigureAwait(false);

                normalizedIdentifierKind = kind.ToString();
                normalizedIdentifierValue = identifierValue;
                lookupPath = request.Venue is null ? "identifier" : "identifier+venue";
            }
        }

        if (detail is null)
        {
            return null;
        }

        var primaryIdentifier = detail.Identifiers
            .FirstOrDefault(static identifier => identifier.IsPrimary)?.Value
            ?? detail.Identifiers.FirstOrDefault()?.Value;

        var inferred = request.SecurityId is null
            && !string.IsNullOrWhiteSpace(request.Symbol)
            && !string.Equals(normalizedIdentifierValue, request.Symbol, StringComparison.OrdinalIgnoreCase);

        return new WorkstationSecurityReference(
            SecurityId: detail.SecurityId,
            DisplayName: detail.DisplayName,
            AssetClass: detail.AssetClass,
            Currency: detail.Currency,
            Status: detail.Status,
            PrimaryIdentifier: primaryIdentifier,
            SubType: DeriveSubType(detail.AssetClass),
            CoverageStatus: WorkstationSecurityCoverageStatus.Resolved,
            MatchedIdentifierKind: normalizedIdentifierKind,
            MatchedIdentifierValue: normalizedIdentifierValue,
            MatchedProvider: request.Venue,
            ResolutionReason: request.Source,
            LookupPath: lookupPath,
            LookupSource: request.Source,
            IsInferredMatch: inferred);
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
