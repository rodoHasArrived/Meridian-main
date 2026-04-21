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

    private static bool? ReadBool(System.Text.Json.JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) &&
            (prop.ValueKind == System.Text.Json.JsonValueKind.True || prop.ValueKind == System.Text.Json.JsonValueKind.False)
            ? prop.GetBoolean() : null;

    private static DateOnly? ReadDateOnly(System.Text.Json.JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != System.Text.Json.JsonValueKind.String)
            return null;
        return DateOnly.TryParseExact(prop.GetString(), "yyyy-MM-dd", out var date) ? date : null;
    }

    private static bool IsNullOrUndefined(System.Text.Json.JsonElement element)
        => element.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined;

    private static System.Text.Json.JsonElement? ReadObject(System.Text.Json.JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.Object
            ? prop
            : null;

    public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
        => _eventStore.LoadCorporateActionsAsync(securityId, ct);

    public async Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
    {
        var projection = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (projection is null || !string.Equals(projection.AssetClass, "Equity", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!projection.AssetSpecificTerms.TryGetProperty("preferredTerms", out var pt) ||
            IsNullOrUndefined(pt))
            return null;

        var assetSpecific = projection.AssetSpecificTerms;
        var participation = ReadObject(pt, "participationTerms");
        var liquidationPreference = ReadObject(pt, "liquidationPreference");
        var dividendType = ReadString(pt, "dividendType");

        return new PreferredEquityTermsDto(
            SecurityId: securityId,
            Classification: ReadString(assetSpecific, "classification") ?? ReadString(pt, "classification"),
            DividendRate: ReadDecimal(pt, "dividendRate"),
            DividendType: dividendType,
            IsCumulative: ReadBool(pt, "isCumulative") ?? (string.Equals(dividendType, "Cumulative", StringComparison.OrdinalIgnoreCase) ? true : null),
            RedemptionPrice: ReadDecimal(pt, "redemptionPrice"),
            RedemptionDate: ReadDateOnly(pt, "redemptionDate"),
            CallableDate: ReadDateOnly(pt, "callableDate"),
            ParticipatesInCommonDividends: ReadBool(pt, "participatesInCommonDividends") ?? (participation.HasValue ? ReadBool(participation.Value, "participatesInCommonDividends") : null),
            AdditionalDividendThreshold: ReadDecimal(pt, "additionalDividendThreshold") ?? (participation.HasValue ? ReadDecimal(participation.Value, "additionalDividendThreshold") : null),
            LiquidationPreferenceKind: ReadString(pt, "liquidationPreferenceKind") ?? (liquidationPreference.HasValue ? ReadString(liquidationPreference.Value, "kind") : null),
            LiquidationPreferenceMultiple: ReadDecimal(pt, "liquidationPreferenceMultiple") ?? (liquidationPreference.HasValue ? ReadDecimal(liquidationPreference.Value, "multiple") : null),
            Version: projection.Version);
    }

    public async Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
    {
        var projection = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (projection is null || !string.Equals(projection.AssetClass, "Equity", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!projection.AssetSpecificTerms.TryGetProperty("convertibleTerms", out var convertibleTermsEl) ||
            IsNullOrUndefined(convertibleTermsEl))
            return null;

        Guid? underlyingId = null;
        if (convertibleTermsEl.TryGetProperty("underlyingSecurityId", out var uidProp) &&
            uidProp.ValueKind == System.Text.Json.JsonValueKind.String &&
            Guid.TryParse(uidProp.GetString(), out var parsedGuid))
        {
            underlyingId = parsedGuid;
        }

        var assetSpecific = projection.AssetSpecificTerms;
        var classification = ReadString(assetSpecific, "classification");

        return new ConvertibleEquityTermsDto(
            SecurityId: securityId,
            Classification: classification,
            UnderlyingSecurityId: underlyingId,
            ConversionRatio: ReadDecimal(convertibleTermsEl, "conversionRatio"),
            ConversionPrice: ReadDecimal(convertibleTermsEl, "conversionPrice"),
            ConversionStartDate: ReadDateOnly(convertibleTermsEl, "conversionStartDate"),
            ConversionEndDate: ReadDateOnly(convertibleTermsEl, "conversionEndDate"),
            Version: projection.Version);
    }
}
