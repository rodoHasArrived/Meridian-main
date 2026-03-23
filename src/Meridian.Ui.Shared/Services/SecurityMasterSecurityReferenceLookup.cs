using Meridian.Application.SecurityMaster;
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
            PrimaryIdentifier: primaryIdentifier);
    }
}
