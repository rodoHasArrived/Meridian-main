using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Application.SecurityMaster.CashFlow;

// The factory is deliberately internal: writable terms remain governed by ADR-024's separate
// rollout. Raw spellings are parsed strictly here, never coerced to legacy schedule defaults.
internal static class ScheduleSpecFactory
{
    internal sealed record Plan(ScheduleGenerationResult Schedule, bool RateIsPercent, bool LevelCash);

    internal static JsonElement? ReadBlock(SecurityDetailDto security, int? legIndex = null)
    {
        if (SecurityTermsEnvelope.IsProfileGovernedPayload(security.AssetClass, security.AssetSpecificTerms))
            return null;
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(security.AssetClass);
        var field = legIndex is null
            ? SecurityAssetTermsSchema.Field(descriptor.AssetClass, "scheduleConventions")
            : SecurityAssetTermsSchema.ElementFields(descriptor.AssetClass, "legs")
                .FirstOrDefault(field => field.Key == "scheduleConventions");
        if (field is null || SecurityTermsEnvelope.ResolveAssetClassTermsVersion(security.AssetSpecificTerms) < field.SinceTermsVersion)
            return null;

        foreach (var source in StructuredCashFlowTermsResolver.EnumerateTermSources(security))
        {
            if (legIndex is null)
            {
                var block = Property(source, "scheduleConventions");
                if (block is { } value && SecurityAssetTermsSchema.IsPopulatedTermValue(value))
                    return value;
            }
            else
            {
                foreach (var key in new[] { "legs", "swapLegs", "cashFlowLegs" })
                {
                    var container = Property(source, key);
                    if (container is not { ValueKind: JsonValueKind.Array } array)
                        continue;
                    // Mirrors the resolver's first usable container and object-row ordering.
                    var rows = array.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object).ToArray();
                    if (rows.Length == 0)
                        continue;
                    return legIndex.Value < rows.Length ? Property(rows[legIndex.Value], "scheduleConventions") : null;
                }
            }
        }

        return null;
    }

    internal static Plan? Create(
        JsonElement? declaration, DateOnly start, DateOnly end, string? frequency,
        IBusinessDayCalendarSource calendars)
    {
        if (declaration is not { } block || !SecurityAssetTermsSchema.IsPopulatedTermValue(block))
            return null;
        if (block.ValueKind != JsonValueKind.Object)
            throw Refused("scheduleConventions must be an object.");

        if (!SecurityTermConventions.TryParsePaymentFrequency(frequency, out var cadence))
            throw Refused("Declared schedules require a known payment frequency.");
        var tenor = cadence switch
        {
            SecurityPaymentFrequency.Daily => ScheduleTenor.Days(1),
            SecurityPaymentFrequency.Weekly => ScheduleTenor.Weeks(1),
            SecurityPaymentFrequency.BiWeekly => ScheduleTenor.Weeks(2),
            SecurityPaymentFrequency.AtMaturity => ScheduleTenor.Term,
            _ when cadence.MonthsPerPeriod() is int months => ScheduleTenor.Months(months),
            _ => throw Refused("Irregular payment frequency requires an authoritative schedule.")
        };

        var direction = ParseEnum(block, "direction", ScheduleDirection.Forward);
        var stub = ScheduleStubPolicy.Short;
        if (Text(block, "stubPolicy") is { } stubToken && !SecurityTermConventions.TryParseStubPolicy(stubToken, out stub))
            throw Refused("Unknown declared stubPolicy.");
        var roll = ScheduleRoll.AnchorDay;
        var rollToken = Text(block, "rollConvention");
        if (rollToken is not null && !SecurityTermConventions.TryParseRollConvention(rollToken, out roll))
            throw Refused("Unknown declared rollConvention.");
        if (Boolean(block, "endOfMonthRule") == true)
        {
            if (rollToken is not null && roll != ScheduleRoll.AnchorDay && roll != ScheduleRoll.EndOfMonth)
                throw Refused("endOfMonthRule conflicts with rollConvention.");
            roll = ScheduleRoll.EndOfMonth;
        }

        var convention = BusinessDayConvention.Unadjusted;
        if (Text(block, "businessDayConvention") is { } conventionToken
            && !SecurityTermConventions.TryParseBusinessDayConvention(conventionToken, out convention))
            throw Refused("Unknown declared businessDayConvention.");
        var lag = Integer(block, "paymentLagBusinessDays") ?? 0;
        if (lag < 0)
            throw Refused("paymentLagBusinessDays cannot be negative.");
        IBusinessDayCalendar? calendar = null;
        if (Property(block, "paymentCalendars") is { ValueKind: not JsonValueKind.Null } ids)
        {
            if (ids.ValueKind != JsonValueKind.Array)
                throw Refused("paymentCalendars must be an array of calendar ids.");
            var names = ids.EnumerateArray().Select(id => id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString())
                ? id.GetString()! : throw Refused("paymentCalendars contains an invalid calendar id.")).ToArray();
            if (names.Length > 0 && !calendars.TryResolve(names, out calendar))
                throw Refused("A declared payment calendar cannot be resolved.");
        }
        // No fallback to commonTerms.holidayCalendarId or WEEKENDS: an empty declaration is not
        // authorization to substitute another holiday set.
        if (calendar is null && (convention != BusinessDayConvention.Unadjusted || lag > 0))
            throw Refused("Declared business-day adjustment or payment lag requires paymentCalendars.");
        var payment = new BusinessDayAdjustment(convention, calendar);
        var accrual = Boolean(block, "accrualAdjusted") == true ? payment : BusinessDayAdjustment.Unadjusted;
        var unit = Text(block, "couponRateUnit");
        if (unit is null || !(unit.Equals("Percent", StringComparison.OrdinalIgnoreCase) || unit.Equals("Fraction", StringComparison.OrdinalIgnoreCase)))
            throw Refused("Declared schedules require couponRateUnit (Percent or Fraction).");
        var basis = Text(block, "couponCashBasis");
        if (basis is null || !(basis.Equals("Level", StringComparison.OrdinalIgnoreCase) || basis.Equals("DayCount", StringComparison.OrdinalIgnoreCase)))
            throw Refused("Declared schedules require couponCashBasis (Level or DayCount).");
        var schedule = ScheduleGenerator.Generate(new ScheduleSpec(start, end, tenor, direction,
            Roll: roll, StubPolicy: stub, FirstRegularDate: Date(block, "firstRegularDate"),
            LastRegularDate: Date(block, "lastRegularDate"), AccrualAdjustment: accrual,
            PaymentAdjustment: payment, PaymentLagBusinessDays: lag));
        if (!schedule.Succeeded)
            throw Refused(schedule.Failure.Message);
        if (schedule.Diagnostics.Count > 0)
            throw Refused("Declared schedule exceeds the available calendar coverage.");
        if (basis.Equals("Level", StringComparison.OrdinalIgnoreCase) && tenor.PeriodsPerYear is null)
            throw Refused("Level coupon cash requires a fixed annual cadence.");
        return new Plan(schedule, unit.Equals("Percent", StringComparison.OrdinalIgnoreCase), basis.Equals("Level", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonElement? Property(JsonElement source, string key)
    {
        if (source.ValueKind != JsonValueKind.Object)
            return null;
        var matches = source.EnumerateObject().Where(property => property.Name.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1)
            throw Refused($"Ambiguous declared schedule member '{key}'.");
        return matches.Length == 1 ? matches[0].Value : null;
    }

    private static string? Text(JsonElement source, string key)
        => Property(source, key) is not { ValueKind: not JsonValueKind.Null } value ? null
            : value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!.Trim()
            : throw Refused($"{key} must be a nonblank string.");

    private static bool? Boolean(JsonElement source, string key)
        => Property(source, key) is not { ValueKind: not JsonValueKind.Null } value ? null
            : value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean()
            : throw Refused($"{key} must be a boolean.");

    private static int? Integer(JsonElement source, string key)
        => Property(source, key) is not { ValueKind: not JsonValueKind.Null } value ? null
            : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number
            : throw Refused($"{key} must be an integer.");

    private static DateOnly? Date(JsonElement source, string key)
        => Text(source, key) is not { } token ? null
            : DateOnly.TryParseExact(token, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date
            : throw Refused($"{key} must be an ISO date.");

    private static T ParseEnum<T>(JsonElement source, string key, T fallback) where T : struct, Enum
        => Text(source, key) is not { } token ? fallback
            : Enum.GetNames<T>().Any(name => name.Equals(token, StringComparison.OrdinalIgnoreCase))
                && Enum.TryParse<T>(token, true, out var parsed) ? parsed
            : throw Refused($"Unknown declared {key}.");

    internal sealed class RefusalException(string reason) : InvalidOperationException($"Declared schedule blocked: {reason}")
    {
    }

    private static RefusalException Refused(string reason) => new(reason);
}
