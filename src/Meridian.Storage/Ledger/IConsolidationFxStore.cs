using Meridian.Contracts.Ledger;

namespace Meridian.Storage.Ledger;

public interface IConsolidationFxStore
{
    Task RetainRateSetAsync(ConsolidationFxRateSetDto rateSet, CancellationToken ct = default);
    Task<ConsolidationFxRateSetDto?> GetRateSetAsync(string tenantId, string companyId,
        Guid rateSetId, int revision, CancellationToken ct = default);
    Task<IReadOnlyList<ConsolidationFxRateSetDto>> ListRateSetsAsync(string tenantId,
        string companyId, CancellationToken ct = default);
    Task RetainConfigurationAsync(ConsolidationFxBookConfigurationDto configuration, CancellationToken ct = default);
    Task<ConsolidationFxBookConfigurationDto?> GetConfigurationAsync(Guid ledgerBookId,
        DateOnly asOf, CancellationToken ct = default);
    Task<ConsolidationFxBookConfigurationDto?> GetConfigurationRevisionAsync(Guid ledgerBookId,
        int revision, CancellationToken ct = default);
    /// <summary>Shared authority lease; configuration writers hold the exclusive side through commit.</summary>
    Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default);
}

public interface IConsolidationFxAuthority
{
    Task<ConsolidationFxContextDto> ResolveAsync(Guid eliminationBookId, DateOnly asOf,
        CancellationToken ct = default);
    Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default);
}
