using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterQueryService :
    ISecurityMasterQueryService,
    Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService,
    ISecurityMasterReportingQueryService
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

    public async Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default)
    {
        var current = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        var asOfProjection = await _rebuilder.RebuildAsOfAsync(securityId, asOfUtc, current, ct).ConfigureAwait(false);
        return asOfProjection is null ? null : SecurityMasterMapping.ToDetail(asOfProjection);
    }

    public async Task<SecurityDetailDto?> GetRecordedByIdAsOfAsync(
        Guid securityId,
        DateTimeOffset asOfUtc,
        CancellationToken ct = default)
    {
        var current = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        var asOfProjection = await _rebuilder
            .RebuildRecordedAsOfAsync(securityId, asOfUtc, current, ct)
            .ConfigureAwait(false);
        return asOfProjection is null ? null : SecurityMasterMapping.ToDetail(asOfProjection);
    }

    public async Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null)
    {
        var asOf = asOfUtc ?? DateTimeOffset.UtcNow;
        var projection = await TryGetProjectionByIdentifierAsync(
                identifierKind,
                identifierValue,
                provider,
                asOf,
                allowIdentityFallback: asOfUtc is not null,
                ct)
            .ConfigureAwait(false);
        if (projection is null)
        {
            return null;
        }

        if (asOfUtc is null)
        {
            return SecurityMasterMapping.ToDetail(projection);
        }

        // An explicit historical lookup must return the terms as recorded at that time —
        // resolving the identifier as-of and then returning the current projection would
        // silently hand back today's terms under yesterday's identity.
        var asOfProjection = await _rebuilder.RebuildAsOfAsync(projection.SecurityId, asOf, projection, ct)
            .ConfigureAwait(false);
        return asOfProjection is null ? null : SecurityMasterMapping.ToDetail(asOfProjection);
    }

    public async Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!HasProfileSearchCriteria(request))
        {
            return await _store.SearchAsync(request, ct).ConfigureAwait(false);
        }

        var query = request.Query?.Trim() ?? string.Empty;
        var rows = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        return rows
            .Where(record => MatchesSearchRequest(record, request, query))
            .OrderBy(static record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static record => record.SecurityId)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Max(0, request.Take))
            .Select(SecurityMasterDbMapper.ToSummary)
            .ToArray();
    }

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

    public async Task<SecurityMasterReportingReference?> GetReportingReferenceByIdentifierAsOfAsync(
        SecurityIdentifierKind identifierKind,
        string identifierValue,
        string? provider,
        DateTimeOffset asOfUtc,
        CancellationToken ct = default)
    {
        var detail = await GetByIdentifierAsync(
                identifierKind,
                identifierValue,
                provider,
                ct,
                asOfUtc)
            .ConfigureAwait(false);
        if (detail is null)
        {
            return null;
        }

        var events = await _eventStore.LoadAsync(detail.SecurityId, ct).ConfigureAwait(false);
        if (events.Count == 0)
        {
            var currentEconomicDefinition = await GetEconomicDefinitionByIdAsync(detail.SecurityId, ct)
                .ConfigureAwait(false);
            return new SecurityMasterReportingReference(
                detail,
                currentEconomicDefinition,
                asOfUtc,
                SecurityMasterReportingResolutionMode.CurrentProjectionFallback);
        }

        var sourceEvent = events.LastOrDefault(@event => @event.EventTimestamp <= asOfUtc);
        if (sourceEvent is null)
        {
            return null;
        }

        var currentProjection = await _store.GetProjectionAsync(detail.SecurityId, ct).ConfigureAwait(false);
        var historicalProjection = await _rebuilder
            .RebuildAsOfAsync(detail.SecurityId, asOfUtc, currentProjection, ct)
            .ConfigureAwait(false);
        if (historicalProjection is null)
        {
            return null;
        }

        return new SecurityMasterReportingReference(
            SecurityMasterMapping.ToDetail(historicalProjection),
            SecurityEconomicDefinitionAdapter.ToEconomicRecord(historicalProjection),
            asOfUtc,
            SecurityMasterReportingResolutionMode.HistoricalEvent,
            sourceEvent.GlobalSequence,
            sourceEvent.StreamVersion,
            sourceEvent.EventTimestamp);
    }

    public async Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
    {
        var detail = await _store.GetDetailAsync(securityId, ct).ConfigureAwait(false);
        if (detail is null)
            return null;

        var common = detail.CommonTerms;

        return new TradingParametersDto(
            SecurityId: securityId,
            LotSize: ReadDecimal(common, "lotSize") ?? ReadDecimal(common, "minimumTradeIncrement"),
            TickSize: ReadDecimal(common, "tickSize") ?? ReadDecimal(common, "priceIncrement"),
            ContractMultiplier: ReadDecimal(common, "contractMultiplier"),
            MarginRequirementPct: ReadDecimal(common, "marginRequirementPct"),
            TradingHoursUtc: ReadString(common, "tradingHoursUtc"),
            CircuitBreakerThresholdPct: ReadDecimal(common, "circuitBreakerThresholdPct"),
            AsOf: asOf)
        {
            IsMarginable = ReadBool(common, "isMarginable"),
            IsShortable = ReadBool(common, "isShortable"),
            IsEasyToBorrow = ReadBool(common, "isEasyToBorrow"),
            IsFractionable = ReadBool(common, "isFractionable"),
            MinimumOrderSize = ReadDecimal(common, "minimumOrderSize"),
            MinimumTradeIncrement = ReadDecimal(common, "minimumTradeIncrement"),
            PriceIncrement = ReadDecimal(common, "priceIncrement")
        };
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

    private async Task<SecurityProjectionRecord?> TryGetProjectionByIdentifierAsync(
        SecurityIdentifierKind identifierKind,
        string identifierValue,
        string? provider,
        DateTimeOffset asOf,
        bool allowIdentityFallback,
        CancellationToken ct)
    {
        var providerCandidates = BuildLookupCandidates(provider, SecurityIdentifierNormalizer.NormalizeProvider);
        foreach (var valueCandidate in BuildLookupCandidates(identifierValue, value => SecurityIdentifierNormalizer.NormalizeValue(identifierKind, value)).Where(static candidate => candidate is not null))
        {
            foreach (var providerCandidate in providerCandidates)
            {
                var exactProjection = await _store.GetByIdentifierAsync(
                        identifierKind,
                        valueCandidate!,
                        providerCandidate,
                        asOf,
                        includeInactive: true,
                        ct)
                    .ConfigureAwait(false);

                if (exactProjection is not null)
                {
                    return exactProjection;
                }
            }
        }

        var normalizedValue = SecurityIdentifierNormalizer.NormalizeValue(identifierKind, identifierValue);
        if (normalizedValue.Length == 0)
        {
            return null;
        }

        var normalizedProvider = SecurityIdentifierNormalizer.NormalizeProvider(provider);
        var universe = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        var asOfMatch = universe.FirstOrDefault(candidate =>
            MatchesIdentifier(candidate, identifierKind, normalizedValue, normalizedProvider, asOf));
        if (asOfMatch is not null || !allowIdentityFallback)
        {
            return asOfMatch;
        }

        // As-of fallback: an identifier row's recorded validity window is frequently its
        // data-entry time rather than the real-world assignment time, so a point-in-time lookup
        // can miss a security whose identity is genuinely stable. When nothing is active at the
        // requested as-of, resolve by identity while ignoring the temporal window. The caller's
        // as-of term rebuild still governs which economic terms are returned, so this only
        // restores identity resolution — it never hands back today's terms under yesterday's
        // identity.
        var identifierKindText = identifierKind.ToString();
        return universe.FirstOrDefault(candidate =>
            MatchesIdentifierIgnoringWindow(candidate, identifierKind, identifierKindText, normalizedValue, normalizedProvider));
    }

    private static IReadOnlyList<string?> BuildLookupCandidates(string? value, Func<string?, string> normalize)
    {
        var results = new List<string?>(2);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        static void AddCandidate(List<string?> target, HashSet<string> seenValues, string? candidate)
        {
            if (candidate is null)
            {
                if (!seenValues.Contains("<null>"))
                {
                    target.Add(null);
                    seenValues.Add("<null>");
                }

                return;
            }

            if (candidate.Length == 0 || !seenValues.Add(candidate))
            {
                return;
            }

            target.Add(candidate);
        }

        AddCandidate(results, seen, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        AddCandidate(results, seen, normalize(value));
        return results;
    }

    private static bool MatchesIdentifier(
        SecurityProjectionRecord candidate,
        SecurityIdentifierKind identifierKind,
        string normalizedValue,
        string normalizedProvider,
        DateTimeOffset asOf)
    {
        if (candidate.Identifiers.Any(identifier =>
                identifier.Kind == identifierKind
                && identifier.ValidFrom <= asOf
                && (!identifier.ValidTo.HasValue || identifier.ValidTo.Value > asOf)
                && SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier).Equals(normalizedValue, StringComparison.Ordinal)
                && ProviderMatches(identifier.Provider, identifier.NormalizedProvider, normalizedProvider)))
        {
            return true;
        }

        if (candidate.Aliases.Any(alias =>
                string.Equals(alias.AliasKind, identifierKind.ToString(), StringComparison.OrdinalIgnoreCase)
                && alias.ValidFrom <= asOf
                && (!alias.ValidTo.HasValue || alias.ValidTo.Value > asOf)
                && alias.IsEnabled
                && SecurityIdentifierNormalizer.NormalizeValue(identifierKind, alias.AliasValue).Equals(normalizedValue, StringComparison.Ordinal)
                && ProviderMatches(alias.Provider, normalizedProvider)))
        {
            return true;
        }

        return string.Equals(candidate.PrimaryIdentifierKind, identifierKind.ToString(), StringComparison.OrdinalIgnoreCase)
               && SecurityIdentifierNormalizer.NormalizeValue(identifierKind, candidate.PrimaryIdentifierValue).Equals(normalizedValue, StringComparison.Ordinal);
    }

    private static bool MatchesIdentifierIgnoringWindow(
        SecurityProjectionRecord candidate,
        SecurityIdentifierKind identifierKind,
        string identifierKindText,
        string normalizedValue,
        string normalizedProvider)
    {
        if (candidate.Identifiers.Any(identifier =>
                identifier.Kind == identifierKind
                && SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier).Equals(normalizedValue, StringComparison.Ordinal)
                && ProviderMatches(identifier.Provider, identifier.NormalizedProvider, normalizedProvider)))
        {
            return true;
        }

        if (candidate.Aliases.Any(alias =>
                string.Equals(alias.AliasKind, identifierKindText, StringComparison.OrdinalIgnoreCase)
                && alias.IsEnabled
                && SecurityIdentifierNormalizer.NormalizeValue(identifierKind, alias.AliasValue).Equals(normalizedValue, StringComparison.Ordinal)
                && ProviderMatches(alias.Provider, normalizedProvider)))
        {
            return true;
        }

        return string.Equals(candidate.PrimaryIdentifierKind, identifierKindText, StringComparison.OrdinalIgnoreCase)
               && SecurityIdentifierNormalizer.NormalizeValue(identifierKind, candidate.PrimaryIdentifierValue).Equals(normalizedValue, StringComparison.Ordinal);
    }

    private static bool ProviderMatches(string? provider, string normalizedProvider)
        => normalizedProvider.Length == 0
           || SecurityIdentifierNormalizer.NormalizeProvider(provider).Equals(normalizedProvider, StringComparison.Ordinal);

    private static bool ProviderMatches(string? provider, string? normalizedProvider, string expectedNormalizedProvider)
        => expectedNormalizedProvider.Length == 0
           || SecurityIdentifierNormalizer.NormalizeProvider(normalizedProvider ?? provider).Equals(expectedNormalizedProvider, StringComparison.Ordinal);

    private static bool HasProfileSearchCriteria(SecuritySearchRequest request)
        => !string.IsNullOrWhiteSpace(request.CustomProfileId)
           || request.ProfileVersion.HasValue
           || !string.IsNullOrWhiteSpace(request.ProfileFieldKey)
           || !string.IsNullOrWhiteSpace(request.ProfileFieldValue);

    private static bool MatchesSearchRequest(SecurityProjectionRecord record, SecuritySearchRequest request, string query)
    {
        if (request.ActiveOnly && record.Status != SecurityStatusDto.Active)
        {
            return false;
        }

        if (!MatchesProfileCriteria(record, request))
        {
            return false;
        }

        return query.Length == 0 || MatchesTextQuery(record, query);
    }

    private static bool MatchesProfileCriteria(SecurityProjectionRecord record, SecuritySearchRequest request)
    {
        if (!IsProfileBacked(record))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.CustomProfileId)
            && !JsonStringEquals(record.AssetSpecificTerms, "customProfileId", request.CustomProfileId))
        {
            return false;
        }

        if (request.ProfileVersion.HasValue
            && (!record.AssetSpecificTerms.TryGetProperty("profileVersion", out var profileVersion)
                || profileVersion.ValueKind != System.Text.Json.JsonValueKind.Number
                || !profileVersion.TryGetInt32(out var version)
                || version != request.ProfileVersion.Value))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.ProfileFieldKey)
            && !TryGetProfileField(record, request.ProfileFieldKey, out _))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.ProfileFieldValue))
        {
            if (string.IsNullOrWhiteSpace(request.ProfileFieldKey))
            {
                return ProfileFieldsContain(record, request.ProfileFieldValue);
            }

            return TryGetProfileField(record, request.ProfileFieldKey, out var profileField)
                   && JsonElementContains(profileField, request.ProfileFieldValue);
        }

        return true;
    }

    private static bool IsProfileBacked(SecurityProjectionRecord record)
        => record.AssetSpecificTerms.ValueKind == System.Text.Json.JsonValueKind.Object
           && record.AssetSpecificTerms.TryGetProperty("customProfileId", out var profileId)
           && profileId.ValueKind == System.Text.Json.JsonValueKind.String
           && !string.IsNullOrWhiteSpace(profileId.GetString());

    private static bool MatchesTextQuery(SecurityProjectionRecord record, string query)
        => Contains(record.DisplayName, query)
           || Contains(record.AssetClass, query)
           || Contains(record.PrimaryIdentifierValue, query)
           || record.Identifiers.Any(identifier => Contains(identifier.Value, query) || Contains(identifier.Kind.ToString(), query))
           || ProfileFieldsContain(record, query);

    private static bool TryGetProfileField(SecurityProjectionRecord record, string fieldKey, out System.Text.Json.JsonElement profileField)
    {
        profileField = default;
        return record.AssetSpecificTerms.ValueKind == System.Text.Json.JsonValueKind.Object
               && record.AssetSpecificTerms.TryGetProperty("profileFields", out var profileFields)
               && profileFields.ValueKind == System.Text.Json.JsonValueKind.Object
               && profileFields.TryGetProperty(fieldKey, out profileField);
    }

    private static bool ProfileFieldsContain(SecurityProjectionRecord record, string value)
    {
        if (record.AssetSpecificTerms.ValueKind != System.Text.Json.JsonValueKind.Object
            || !record.AssetSpecificTerms.TryGetProperty("profileFields", out var profileFields)
            || profileFields.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return false;
        }

        foreach (var field in profileFields.EnumerateObject())
        {
            if (Contains(field.Name, value) || JsonElementContains(field.Value, value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool JsonElementContains(System.Text.Json.JsonElement element, string expected)
        => element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => Contains(element.GetString(), expected),
            System.Text.Json.JsonValueKind.Number => Contains(element.GetRawText(), expected),
            System.Text.Json.JsonValueKind.True => Contains("true", expected),
            System.Text.Json.JsonValueKind.False => Contains("false", expected),
            _ => Contains(element.GetRawText(), expected)
        };

    private static bool JsonStringEquals(System.Text.Json.JsonElement element, string propertyName, string expected)
        => element.ValueKind == System.Text.Json.JsonValueKind.Object
           && element.TryGetProperty(propertyName, out var property)
           && property.ValueKind == System.Text.Json.JsonValueKind.String
           && string.Equals(property.GetString(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string expected)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains(expected, StringComparison.OrdinalIgnoreCase);
}
