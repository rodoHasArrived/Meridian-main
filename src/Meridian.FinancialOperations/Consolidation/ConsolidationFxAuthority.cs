using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Consolidation;

public sealed class ConsolidationFxAuthority(IConsolidationFxStore store) : IConsolidationFxAuthority
{
    public async Task<ConsolidationFxContextDto> ResolveAsync(Guid eliminationBookId, DateOnly asOf, CancellationToken ct = default)
    {
        var configuration = await store.GetConfigurationAsync(eliminationBookId, asOf, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("An approved effective FX configuration is required for this elimination book.");
        ConsolidationFxCurrencyRules.Validate(configuration);
        var rates = await store.GetRateSetAsync(configuration.TenantId, configuration.CompanyId,
            configuration.RateSetId, configuration.RateSetRevision, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The configured approved rate-set revision is unavailable.");
        ConsolidationFxCurrencyRules.Validate(rates);
        var revisions = await store.ListRateSetsAsync(configuration.TenantId, configuration.CompanyId, ct).ConfigureAwait(false);
        if (revisions.Any(x => x.RateSetId == rates.RateSetId && x.Revision > rates.Revision))
            throw new InvalidOperationException("The selected approved FX rate set was superseded; approve a new book configuration and obtain renewed review.");
        return new(configuration, rates);
    }

    public Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
        => store.AcquireAuthorityLeaseAsync(ct);
}
