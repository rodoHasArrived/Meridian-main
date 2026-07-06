using System.Collections.Generic;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;



namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Manages structured cash flow source assignments and delegates projections to
/// <see cref="IStructuredCashFlowProvider"/> implementations.
/// Client-provided sources take precedence and remain until explicitly changed,
/// consistent with the Clearwater cash flow governance model.
/// </summary>
public sealed class SecurityMasterCashFlowService : ISecurityMasterCashFlowService
{
    private readonly ISecurityMasterCashFlowStore _store;
    private readonly IReadOnlyList<IStructuredCashFlowProvider> _providers;
    private readonly ISecurityMasterQueryService _queryService;
    private readonly ILogger<SecurityMasterCashFlowService> _logger;

    private static readonly TimeSpan StalenessThreshold = TimeSpan.FromDays(7);

    public SecurityMasterCashFlowService(
        ISecurityMasterCashFlowStore store,
        IEnumerable<IStructuredCashFlowProvider> providers,
        ISecurityMasterQueryService queryService,
        ILogger<SecurityMasterCashFlowService> logger)
    {
        _store = store;
        _providers = providers?.ToList() ?? [];
        _queryService = queryService;
        _logger = logger;
    }

    public Task<SecurityCashFlowSourceDto?> GetCashFlowSourceAsync(Guid securityId, CancellationToken ct = default)
        => _store.GetSourceAsync(securityId, ct);

    public async Task UpsertCashFlowSourceAsync(UpsertCashFlowSourceRequest request, CancellationToken ct = default)
    {
        var existing = await _store.GetSourceAsync(request.SecurityId, ct).ConfigureAwait(false);

        // Client-provided sources remain authoritative against automated provider refreshes; an
        // operator can still clear them with an explicit Force update (e.g. when the client source
        // is stale or expired).
        if (existing is { IsClientOverride: true } && !request.IsClientOverride && !request.Force)
        {
            _logger.LogWarning(
                "Skipped cash flow source update for {SecurityId}: existing client-provided source is authoritative (use Force to override).",
                request.SecurityId);
            return;
        }

        var record = new SecurityCashFlowSourceDto(
            request.SecurityId,
            request.SourceKind,
            DateTimeOffset.UtcNow,
            request.IsClientOverride,
            request.ClientConfirmedBy,
            request.IsClientOverride ? DateTimeOffset.UtcNow : null);

        await _store.UpsertSourceAsync(record, ct).ConfigureAwait(false);
    }

    public async Task<StructuredCashFlowProjectionDto?> GetProjectionAsync(
        Guid securityId, StructuredCashFlowScenario scenario, CancellationToken ct = default)
    {
        var assignment = await _store.GetSourceAsync(securityId, ct).ConfigureAwait(false);
        if (assignment is null)
        {
            _logger.LogDebug("No cash flow source assigned for security {SecurityId}.", securityId);
            return null;
        }

        if (assignment.LastUpdatedUtc.HasValue
            && DateTimeOffset.UtcNow - assignment.LastUpdatedUtc.Value > StalenessThreshold)
        {
            _logger.LogWarning(
                "Cash flow source for security {SecurityId} was last updated {DaysAgo} days ago and may be stale.",
                securityId,
                (int)(DateTimeOffset.UtcNow - assignment.LastUpdatedUtc.Value).TotalDays);
        }

        // Client-provided source: no external provider delegation; caller must supply projections directly.
        if (assignment.SourceKind == StructuredCashFlowSourceKind.ClientProvided)
        {
            _logger.LogDebug(
                "Security {SecurityId} uses client-provided cash flows; no provider projection available.",
                securityId);
            return null;
        }

        var security = await _queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        if (security is null)
        {
            _logger.LogWarning(
                "Security {SecurityId} not found when retrieving cash flow projections.", securityId);
            return null;
        }

        if (assignment.SourceKind is StructuredCashFlowSourceKind.CalculatedBullet
            or StructuredCashFlowSourceKind.CalculatedSinker)
        {
            return BuildCalculatedProjection(security, assignment.SourceKind, scenario);
        }

        var isin = security.Identifiers
            .FirstOrDefault(i => i.Kind == SecurityIdentifierKind.Isin)?.Value;

        var provider = _providers.FirstOrDefault(
            p => MapSourceKindToProviderId(assignment.SourceKind) == p.ProviderId);

        if (provider is null)
        {
            _logger.LogDebug(
                "No registered provider for source kind {SourceKind} for security {SecurityId}.",
                assignment.SourceKind, securityId);
            return null;
        }

        return await provider.GetProjectedCashFlowsAsync(
            securityId, isin, DateTimeOffset.UtcNow, scenario, ct).ConfigureAwait(false);
    }

    private static StructuredCashFlowProjectionDto BuildCalculatedProjection(
        SecurityDetailDto security,
        StructuredCashFlowSourceKind sourceKind,
        StructuredCashFlowScenario scenario)
    {
        var asOf = DateTimeOffset.UtcNow;
        if (!TryReadDate(security, out var maturity, "maturityDate", "maturity", "legalFinalMaturity"))
        {
            return new StructuredCashFlowProjectionDto(security.SecurityId, sourceKind, scenario, asOf, []);
        }

        var issueDate = TryReadDate(security, out var issue, "issueDate", "originationDate", "effectiveDate")
            ? issue
            : DateOnly.FromDateTime(security.EffectiveFrom.UtcDateTime.Date);
        var asOfDate = DateOnly.FromDateTime(asOf.UtcDateTime.Date);
        if (issueDate > maturity)
        {
            return new StructuredCashFlowProjectionDto(security.SecurityId, sourceKind, scenario, asOf, []);
        }

        _ = TryReadDecimal(security, out var principalValue, "par", "originalFace", "notional", "principal", "principalAmount");
        _ = TryReadDecimal(security, out var factorValue, "currentFactor", "factor");
        _ = TryReadDecimal(security, out var couponValue, "fixedCouponRate", "couponRate", "coupon", "annualRate");
        _ = TryReadString(security, out var frequencyValue, "paymentFrequency", "paymentFrequencyPerYear", "couponFrequency");
        _ = TryReadString(security, out var dayCountValue, "dayCountConvention", "dayCount", "dayCountBasis");

        var principalBasis = principalValue is > 0m ? principalValue.Value : 100m;
        var factor = factorValue is > 0m ? factorValue.Value : 1m;
        var outstanding = RoundCash(principalBasis * factor);
        var annualRate = NormalizeAnnualRate(couponValue ?? 0m) + ScenarioRateShift(scenario);
        if (annualRate < 0m)
        {
            annualRate = 0m;
        }

        var periodMonths = Math.Max(1, 12 / ResolvePaymentFrequencyPerYear(frequencyValue));
        var dates = BuildPaymentDates(issueDate, maturity, periodMonths)
            .Where(date => date >= asOfDate)
            .ToArray();
        if (dates.Length == 0)
        {
            return new StructuredCashFlowProjectionDto(security.SecurityId, sourceKind, scenario, asOf, []);
        }

        var entries = new List<StructuredCashFlowScheduleEntry>(dates.Length);
        var accrualStart = issueDate;
        var sinkerPrincipal = sourceKind == StructuredCashFlowSourceKind.CalculatedSinker
            ? RoundCash(outstanding / dates.Length)
            : 0m;
        for (var i = 0; i < dates.Length; i++)
        {
            var date = dates[i];
            while (accrualStart.AddMonths(periodMonths) < date)
            {
                accrualStart = accrualStart.AddMonths(periodMonths);
            }

            var interest = annualRate > 0m && outstanding > 0m
                ? RoundCash(outstanding * annualRate * CalculateYearFraction(dayCountValue, accrualStart, date, periodMonths))
                : 0m;
            var principal = 0m;
            var isLast = i == dates.Length - 1;
            if (sourceKind == StructuredCashFlowSourceKind.CalculatedBullet && isLast)
            {
                principal = outstanding;
            }
            else if (sourceKind == StructuredCashFlowSourceKind.CalculatedSinker)
            {
                principal = isLast ? outstanding : decimal.Min(outstanding, sinkerPrincipal);
            }

            outstanding = RoundCash(outstanding - principal);
            entries.Add(new StructuredCashFlowScheduleEntry(
                ToUtcDateTimeOffset(date),
                RoundCash(principal),
                interest,
                principalBasis == 0m ? 0m : RoundCash(outstanding / principalBasis)));
            accrualStart = date;
        }

        return new StructuredCashFlowProjectionDto(security.SecurityId, sourceKind, scenario, asOf, entries);
    }

    private static string MapSourceKindToProviderId(StructuredCashFlowSourceKind kind) => kind switch
    {
        StructuredCashFlowSourceKind.MIAC => "miac",
        StructuredCashFlowSourceKind.MoodysAnalytics => "moodys-analytics",
        _ => string.Empty
    };

    private static IEnumerable<DateOnly> BuildPaymentDates(DateOnly issueDate, DateOnly maturity, int periodMonths)
    {
        var date = issueDate;
        while (date < maturity)
        {
            date = date.AddMonths(periodMonths);
            if (date > maturity)
            {
                date = maturity;
            }

            yield return date;
        }
    }

    private static int ResolvePaymentFrequencyPerYear(string? paymentFrequency)
    {
        if (string.IsNullOrWhiteSpace(paymentFrequency))
        {
            return 1;
        }

        var normalized = paymentFrequency
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
        if (int.TryParse(normalized, out var parsed) && parsed > 0)
        {
            return Math.Clamp(parsed, 1, 12);
        }

        return normalized switch
        {
            "MONTHLY" => 12,
            "QUARTERLY" => 4,
            "SEMIANNUAL" or "SEMIANNUALLY" or "SEMIYEARLY" or "HALFYEARLY" => 2,
            "ANNUAL" or "ANNUALLY" or "YEARLY" => 1,
            _ => 1
        };
    }

    private static decimal CalculateYearFraction(
        string? dayCountConvention,
        DateOnly accrualStart,
        DateOnly accrualEnd,
        int periodMonths)
    {
        if (accrualEnd <= accrualStart)
        {
            return 0m;
        }

        var normalized = dayCountConvention?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (normalized is not null && normalized.Contains("30/360", StringComparison.OrdinalIgnoreCase))
        {
            return Days360(accrualStart, accrualEnd) / 360m;
        }

        var actualDays = accrualEnd.DayNumber - accrualStart.DayNumber;
        if (normalized is not null && (normalized.Contains("ACT/360", StringComparison.OrdinalIgnoreCase) ||
                                       normalized.Contains("ACTUAL/360", StringComparison.OrdinalIgnoreCase)))
        {
            return actualDays / 360m;
        }

        if (normalized is not null && (normalized.Contains("ACT/365", StringComparison.OrdinalIgnoreCase) ||
                                       normalized.Contains("ACTUAL/365", StringComparison.OrdinalIgnoreCase)))
        {
            return actualDays / 365m;
        }

        return periodMonths / 12m;
    }

    private static decimal Days360(DateOnly start, DateOnly end)
    {
        var startDay = Math.Min(start.Day, 30);
        var endDay = end.Day == 31 && startDay == 30 ? 30 : Math.Min(end.Day, 30);
        return ((end.Year - start.Year) * 360m) + ((end.Month - start.Month) * 30m) + (endDay - startDay);
    }

    private static decimal NormalizeAnnualRate(decimal coupon)
        => coupon > 1m ? coupon / 100m : coupon;

    private static decimal ScenarioRateShift(StructuredCashFlowScenario scenario)
        => scenario switch
        {
            StructuredCashFlowScenario.Up100 => 0.01m,
            StructuredCashFlowScenario.Up200 => 0.02m,
            StructuredCashFlowScenario.Up300 => 0.03m,
            StructuredCashFlowScenario.Down100 => -0.01m,
            StructuredCashFlowScenario.Down200 => -0.02m,
            StructuredCashFlowScenario.Down300 => -0.03m,
            StructuredCashFlowScenario.Stress => 0.03m,
            _ => 0m
        };

    private static DateTimeOffset ToUtcDateTimeOffset(DateOnly date)
        => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static decimal RoundCash(decimal amount)
        => decimal.Round(amount, 4, MidpointRounding.AwayFromZero);

    private static bool TryReadString(SecurityDetailDto security, out string? value, params string[] propertyNames)
    {
        foreach (var source in EnumerateTermSources(security))
        {
            foreach (var propertyName in propertyNames)
            {
                if (!TryGetProperty(source, propertyName, out var property))
                {
                    continue;
                }

                if (property.ValueKind == JsonValueKind.String)
                {
                    value = property.GetString();
                    return !string.IsNullOrWhiteSpace(value);
                }

                if (property.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    value = property.ToString();
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    private static bool TryReadDecimal(SecurityDetailDto security, out decimal? value, params string[] propertyNames)
    {
        foreach (var source in EnumerateTermSources(security))
        {
            foreach (var propertyName in propertyNames)
            {
                if (!TryGetProperty(source, propertyName, out var property))
                {
                    continue;
                }

                if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
                {
                    value = number;
                    return true;
                }

                if (property.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(property.GetString(), out var parsed))
                {
                    value = parsed;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    private static bool TryReadDate(SecurityDetailDto security, out DateOnly value, params string[] propertyNames)
    {
        foreach (var source in EnumerateTermSources(security))
        {
            foreach (var propertyName in propertyNames)
            {
                if (!TryGetProperty(source, propertyName, out var property) ||
                    property.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var raw = property.GetString();
                if (DateOnly.TryParse(raw, out value))
                {
                    return true;
                }

                if (DateTimeOffset.TryParse(raw, out var timestamp))
                {
                    value = DateOnly.FromDateTime(timestamp.UtcDateTime.Date);
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static IEnumerable<JsonElement> EnumerateTermSources(SecurityDetailDto security)
    {
        yield return security.AssetSpecificTerms;
        if (TryGetProperty(security.AssetSpecificTerms, "profileFields", out var profileFields) &&
            profileFields.ValueKind == JsonValueKind.Object)
        {
            yield return profileFields;
        }

        yield return security.CommonTerms;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
