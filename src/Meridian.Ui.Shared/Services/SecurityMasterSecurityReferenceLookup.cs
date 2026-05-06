using System.Text.Json;
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
            if (detail is not null && MatchesRequest(detail, request))
            {
                lookupPath = "security-id";
            }
            else
            {
                detail = null;
            }
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

        var riskCountry = TryGetCommonTermsString(detail.CommonTerms, "countryOfRisk");

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
            IsInferredMatch: inferred,
            RiskCountry: riskCountry);
    }

    private static bool MatchesRequest(SecurityDetailDto detail, SecurityReferenceLookupRequest request)
    {
        var requestedSymbol = request.Symbol;
        var requestedIdentifier = request.IdentifierValue;

        if (string.IsNullOrWhiteSpace(requestedSymbol) && string.IsNullOrWhiteSpace(requestedIdentifier))
        {
            return true;
        }

        return detail.Identifiers.Any(
            identifier =>
                (!string.IsNullOrWhiteSpace(requestedSymbol)
                 && string.Equals(identifier.Value, requestedSymbol, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(requestedIdentifier)
                    && string.Equals(identifier.Value, requestedIdentifier, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Attempts to read a string property from a <see cref="System.Text.Json.JsonElement"/>
    /// CommonTerms blob. Returns null if the property is absent, null, or not a string.
    /// </summary>
    internal static string? TryGetCommonTermsString(JsonElement commonTerms, string propertyName)
    {
        if (commonTerms.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!commonTerms.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
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
