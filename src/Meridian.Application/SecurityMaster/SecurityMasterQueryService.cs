using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterQueryService : ISecurityMasterQueryService, Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService
{
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ISecurityMasterStore _store;
    private readonly SecurityMasterAggregateRebuilder _rebuilder;

    public SecurityMasterQueryService(
        ISecurityMasterEventStore eventStore,
        ISecurityMasterStore store,
        SecurityMasterAggregateRebuilder rebuilder)
    {
        _eventStore = eventStore;
        _store = store;
        _rebuilder = rebuilder ?? throw new ArgumentNullException(nameof(rebuilder));
    }

    public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
        => _store.GetDetailAsync(securityId, ct);

    public async Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default)
    {
        var projection = await _store.GetByIdentifierAsync(
            identifierKind,
            identifierValue,
            provider,
            DateTimeOffset.UtcNow,
            includeInactive: true,
            ct).ConfigureAwait(false);

        return projection is null ? null : SecurityMasterMapping.ToDetail(projection);
    }

    public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
        => _store.SearchAsync(request, ct);

    public async Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
    {
        var history = await _eventStore.LoadAsync(request.SecurityId, ct).ConfigureAwait(false);
        return history.Count <= request.Take ? history : history.Take(request.Take).ToArray();
    }

    public async Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
    {
        var projection = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        return await _rebuilder.RebuildEconomicDefinitionAsync(securityId, projection, ct).ConfigureAwait(false);
    }

    public async Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
    {
        var detail = await _store.GetDetailAsync(securityId, ct).ConfigureAwait(false);
        if (detail is null)
            return null;

        var common = detail.CommonTerms;

        return new TradingParametersDto(
            SecurityId: securityId,
            LotSize: ReadDecimal(common, "lotSize"),
            TickSize: ReadDecimal(common, "tickSize"),
            ContractMultiplier: ReadDecimal(common, "contractMultiplier"),
            MarginRequirementPct: ReadDecimal(common, "marginRequirementPct"),
            TradingHoursUtc: ReadString(common, "tradingHoursUtc"),
            CircuitBreakerThresholdPct: ReadDecimal(common, "circuitBreakerThresholdPct"),
            AsOf: asOf);
    }

    private static decimal? ReadDecimal(System.Text.Json.JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.Number
            ? prop.GetDecimal() : null;

    private static string? ReadString(System.Text.Json.JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String
            ? prop.GetString() : null;

    public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
        => _eventStore.LoadCorporateActionsAsync(securityId, ct);

    public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<PreferredEquityTermsDto?>(null);
}
