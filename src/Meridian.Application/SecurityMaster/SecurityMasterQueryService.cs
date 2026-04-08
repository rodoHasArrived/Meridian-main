using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using System.Text.Json;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterQueryService : ISecurityMasterQueryService, Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService
    , ISecurityMasterRuntimeStatus
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

    public bool IsAvailable => true;

    public string AvailabilityDescription => "Security Master is available.";

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

    public async Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
    {
        var projection = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (projection is null ||
            !string.Equals(projection.AssetClass, "Equity", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var assetSpecific = projection.AssetSpecificTerms;
        var classification = ReadString(assetSpecific, "classification");
        if (classification is not ("Preferred" or "ConvertiblePreferred") ||
            !assetSpecific.TryGetProperty("preferredTerms", out var preferredTerms) ||
            preferredTerms.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        JsonElement? participationTerms =
            preferredTerms.TryGetProperty("participationTerms", out var participation) &&
            participation.ValueKind == JsonValueKind.Object
                ? participation
                : null;
        var liquidationPreference = ReadRequiredObject(preferredTerms, "liquidationPreference");
        var dividendType = ReadRequiredString(preferredTerms, "dividendType");

        return new PreferredEquityTermsDto(
            SecurityId: securityId,
            Classification: classification,
            DividendRate: ReadDecimal(preferredTerms, "dividendRate"),
            DividendType: dividendType,
            IsCumulative: string.Equals(dividendType, "Cumulative", StringComparison.OrdinalIgnoreCase),
            RedemptionPrice: ReadDecimal(preferredTerms, "redemptionPrice"),
            RedemptionDate: ReadDateOnly(preferredTerms, "redemptionDate"),
            CallableDate: ReadDateOnly(preferredTerms, "callableDate"),
            ParticipatesInCommonDividends: ReadBoolean(participationTerms, "participatesInCommonDividends") ?? false,
            AdditionalDividendThreshold: ReadDecimal(participationTerms, "additionalDividendThreshold"),
            LiquidationPreferenceKind: ReadRequiredString(liquidationPreference, "kind"),
            LiquidationPreferenceMultiple: ReadDecimal(liquidationPreference, "multiple"),
            Version: projection.Version);
    }

    public async Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
    {
        var projection = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (projection is null ||
            !string.Equals(projection.AssetClass, "Equity", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var assetSpecific = projection.AssetSpecificTerms;
        var classification = ReadString(assetSpecific, "classification");
        if (classification is not ("Convertible" or "ConvertiblePreferred") ||
            !assetSpecific.TryGetProperty("convertibleTerms", out var convertibleTerms) ||
            convertibleTerms.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new ConvertibleEquityTermsDto(
            SecurityId: securityId,
            Classification: classification,
            UnderlyingSecurityId: ReadRequiredGuid(convertibleTerms, "underlyingSecurityId"),
            ConversionRatio: ReadRequiredDecimal(convertibleTerms, "conversionRatio"),
            ConversionPrice: ReadDecimal(convertibleTerms, "conversionPrice"),
            ConversionStartDate: ReadDateOnly(convertibleTerms, "conversionStartDate"),
            ConversionEndDate: ReadDateOnly(convertibleTerms, "conversionEndDate"),
            Version: projection.Version);
    }

    private static decimal? ReadDecimal(System.Text.Json.JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.Number
            ? prop.GetDecimal() : null;

    private static decimal? ReadDecimal(JsonElement? element, string propertyName)
        => element.HasValue ? ReadDecimal(element.Value, propertyName) : null;

    private static string? ReadString(System.Text.Json.JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String
            ? prop.GetString() : null;

    private static string ReadRequiredString(JsonElement element, string propertyName)
        => ReadString(element, propertyName)
            ?? throw new InvalidOperationException($"Missing required string '{propertyName}' in Security Master terms payload.");

    private static decimal ReadRequiredDecimal(JsonElement element, string propertyName)
        => ReadDecimal(element, propertyName)
            ?? throw new InvalidOperationException($"Missing required decimal '{propertyName}' in Security Master terms payload.");

    private static Guid ReadRequiredGuid(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) &&
           prop.ValueKind == JsonValueKind.String &&
           Guid.TryParse(prop.GetString(), out var guid)
            ? guid
            : throw new InvalidOperationException($"Missing required guid '{propertyName}' in Security Master terms payload.");

    private static DateOnly? ReadDateOnly(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) &&
           prop.ValueKind == JsonValueKind.String &&
           DateOnly.TryParse(prop.GetString(), out var date)
            ? date
            : null;

    private static bool? ReadBoolean(JsonElement? element, string propertyName)
        => element.HasValue &&
           element.Value.TryGetProperty(propertyName, out var prop) &&
           prop.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? prop.GetBoolean()
            : null;

    private static JsonElement ReadRequiredObject(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Object
            ? prop
            : throw new InvalidOperationException($"Missing required object '{propertyName}' in Security Master terms payload.");

    public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
        => _eventStore.LoadCorporateActionsAsync(securityId, ct);
}
