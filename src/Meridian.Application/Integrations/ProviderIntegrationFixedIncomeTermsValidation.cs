using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Application.Integrations;

/// <summary>Reviews the explicit fixed-income import profile through the canonical schedule kernels.</summary>
internal static class ProviderIntegrationFixedIncomeTermsValidation
{
    private static readonly IBusinessDayCalendarSource BuiltInCalendars =
        new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);

    public static void AddValidationIssues(
        ProviderCapabilityKindDto capability, JsonObject mappedRecord, ICollection<ValidationIssueDto> issues,
        IBusinessDayCalendarSource? calendars = null)
    {
        // This reserved result is server-derived for every capability, never supplied by a mapping.
        mappedRecord.Remove("fixedIncomePaymentPreview");
        if (capability != ProviderCapabilityKindDto.SecurityReferenceData || mappedRecord["security"] is not JsonObject security ||
            Text(security, "importTermsProfile") != "fixed-income-v2")
            return;

        var before = issues.Count;
        void Refuse(string field, string message) => issues.Add(new(
            "fixed-income.conventions.invalid", ProviderIntegrationIssueSeverityDto.Critical, message,
            $"security.{field}", "Review conventions and the sample payment before staging terms."));

        if (Text(security, "currencyReview") != "Reviewed")
            Refuse("currencyReview", "Currency must be an explicit reviewed source or mapping choice; set currencyReview to Reviewed after review.");
        var currency = Text(security, "currency");
        if (currency is null || currency.Length != 3 || currency.Any(character => !char.IsLetter(character)))
            Refuse("currency", "An explicit three-letter currency is required; USD is not assumed.");
        var paymentCalendars = Array(security, "paymentCalendars", Refuse);
        var settlementCalendars = Array(security, "settlementCalendars", Refuse);
        var calls = Array(security, SecurityVersionedTerms.CallScheduleKey, Refuse);
        var factors = Array(security, "factorHistory", Refuse);
        if (paymentCalendars is null || settlementCalendars is null || calls is null || factors is null)
            return;
        if (paymentCalendars.Any(value => value is not JsonValue v || !v.TryGetValue<string>(out var id) || string.IsNullOrWhiteSpace(id)) ||
            settlementCalendars.Any(value => value is not JsonValue v || !v.TryGetValue<string>(out var id) || string.IsNullOrWhiteSpace(id)))
            Refuse("paymentCalendars", "Calendars must be arrays of nonblank calendar IDs; [] explicitly declares none.");
        if (!Enum.TryParse<BusinessDayConvention>(Text(security, "settlementConvention"), out var settlementConvention) ||
            !Enum.IsDefined(settlementConvention))
            Refuse("settlementConvention", "Choose an explicit recognized settlement business-day convention.");
        var settlementDays = Number(security, "settlementDays");
        if (settlementDays is null or < 0 or > 30 || decimal.Truncate(settlementDays.Value) != settlementDays.Value)
            Refuse("settlementDays", "Settlement days must be an explicit whole number from 0 through 30.");
        if ((settlementDays > 0 || settlementConvention != BusinessDayConvention.Unadjusted) && settlementCalendars.Count == 0)
            Refuse("settlementCalendars", "Settlement lag and adjusted settlement need an explicitly selected calendar.");
        if (!bool.TryParse(Text(security, "accrualAdjusted"), out var accrualAdjusted))
            Refuse("accrualAdjusted", "Declare accrualAdjusted as true or false.");
        if (Text(security, "couponRateUnit") is not ("Percent" or "Fraction"))
            Refuse("couponRateUnit", "Coupon units must explicitly be Percent or Fraction.");
        if (Text(security, "couponCashBasis") is not ("Level" or "DayCount"))
            Refuse("couponCashBasis", "Coupon cash basis must explicitly be Level or DayCount.");
        if (!Enum.TryParse<BusinessDayConvention>(Text(security, "businessDayConvention"), out var paymentConvention) ||
            !Enum.IsDefined(paymentConvention))
            Refuse("businessDayConvention", "Choose an explicit recognized payment business-day convention.");
        if (!DayCountConventions.TryParseTerm(Text(security, "dayCount"), out var dayCount))
            Refuse("dayCount", "Choose an explicit recognized day-count convention; no fallback is used.");
        var coupon = Number(security, "coupon");
        if (coupon is null or < 0)
            Refuse("coupon", "An explicit nonnegative coupon and its Percent or Fraction unit are required.");
        if (issues.Count != before)
            return;

        var normalizedTerms = new JsonObject
        {
            [SecurityTermsEnvelope.FormatKey] = SecurityTermsFormats.Flat,
            [SecurityTermsEnvelope.AssetClassTermsVersionKey] = 3,
            ["issueDate"] = Text(security, "issueDate"),
            ["maturity"] = Text(security, "maturityDate"),
            ["couponType"] = "Fixed",
            ["couponRate"] = coupon,
            ["dayCount"] = Text(security, "dayCount"),
            ["paymentFrequency"] = Text(security, "paymentFrequency"),
            ["isCallable"] = calls.Count > 0,
            [SecurityVersionedTerms.CallScheduleKey] = calls.DeepClone(),
            ["factorScheduleEntries"] = factors.DeepClone(),
            [SecurityVersionedTerms.ScheduleConventionsKey] = new JsonObject
            {
                ["couponRateUnit"] = Text(security, "couponRateUnit"),
                ["couponCashBasis"] = Text(security, "couponCashBasis"),
                ["businessDayConvention"] = Text(security, "businessDayConvention"),
                ["accrualAdjusted"] = accrualAdjusted,
                ["paymentCalendars"] = paymentCalendars.DeepClone()
            }
        };
        var callDates = calls.OfType<JsonObject>().Select(row => Text(row, "callDate"))
            .Where(value => value is not null).Order(StringComparer.Ordinal).ToArray();
        if (callDates.Length > 0)
            normalizedTerms["callDate"] = callDates[0];

        try
        {
            var termsElement = JsonSerializer.SerializeToElement(normalizedTerms);
            // Report malformed source factors at their import field before the canonical terms
            // resolver (also used by schedule admission) raises a broader terms exception.
            var economicSchedules = SecurityEconomicScheduleParser.Parse(termsElement);
            foreach (var diagnostic in economicSchedules.Diagnostics)
                Refuse("factorHistory", diagnostic.Message);
            if (!economicSchedules.IsValid)
                return;
            ScheduleTermsWriteRules.ValidateDeclarations("Bond", termsElement, 3);
            var detail = new SecurityDetailDto(Guid.Empty, "Bond", SecurityStatusDto.Active,
                Text(security, "name") ?? "Import payment preview", currency!,
                JsonSerializer.SerializeToElement(new { }), termsElement, [], [], 1, DateTimeOffset.MinValue, null);
            var terms = StructuredCashFlowTermsResolver.Resolve(detail);
            var declared = ScheduleSpecFactory.Resolve(detail, terms, calendars ?? BuiltInCalendars);
            if (declared.Status != DeclaredScheduleStatus.Declared)
            {
                Refuse("paymentCalendars", declared.Refusal?.Message ?? "Explicit schedule conventions are required.");
                return;
            }
            if (settlementCalendars.Count > 0 && !(calendars ?? BuiltInCalendars).TryResolve(
                settlementCalendars.Select(value => value!.GetValue<string>()).ToArray(), out _))
            {
                Refuse("settlementCalendars", "A selected settlement calendar is unavailable in the import preview. Load verified calendar evidence before staging; WEEKENDS is used only when explicitly selected.");
                return;
            }
            var schedule = ScheduleGenerator.Generate(declared.Spec!);
            if (!schedule.Succeeded)
            {
                Refuse("paymentFrequency", schedule.Failure!.Message);
                return;
            }
            if (schedule.Diagnostics.Count > 0)
            {
                Refuse("paymentCalendars", "Calendar evidence does not cover the sample schedule: " +
                    string.Join("; ", schedule.Diagnostics.Select(diagnostic => diagnostic.Message)));
                return;
            }
            var period = schedule.Periods[0];
            if (!ScheduleDayCount.TryPeriodFraction(dayCount, period, schedule, out var fullFraction, out var refusal))
            {
                Refuse("dayCount", refusal);
                return;
            }
            var rate = Text(security, "couponRateUnit") == "Percent" ? coupon!.Value / 100m : coupon!.Value;
            var cashBasis = Text(security, "couponCashBasis");
            if (cashBasis == "Level" && schedule.Spec.Tenor.PeriodsPerYear is null)
            {
                Refuse("couponCashBasis", "A level coupon needs a regular frequency; use DayCount for AtMaturity.");
                return;
            }
            const decimal sampleFace = 100_000m;
            var factorRows = economicSchedules.FactorSchedule.Entries.OrderBy(row => row.AsOfDate).ToArray();
            var boundaries = factorRows.Select(row => row.AsOfDate)
                .Where(date => date > period.AccrualStart && date < period.AccrualEnd)
                .Prepend(period.AccrualStart).Append(period.AccrualEnd).Distinct().Order().ToArray();
            var payment = 0m;
            for (var index = 0; index < boundaries.Length - 1; index++)
            {
                var start = boundaries[index];
                var factor = factorRows.LastOrDefault(row => row.AsOfDate <= start)?.Factor ?? 1m;
                if (!BondContractualEconomics.TryAccruedCoupon(period, schedule, dayCount, rate, cashBasis,
                        start, out var beforeCoupon, out refusal) ||
                    !BondContractualEconomics.TryAccruedCoupon(period, schedule, dayCount, rate, cashBasis,
                        boundaries[index + 1], out var afterCoupon, out refusal))
                {
                    Refuse("dayCount", refusal!);
                    return;
                }
                payment += sampleFace * factor * (afterCoupon - beforeCoupon) / 100m;
            }
            mappedRecord["fixedIncomePaymentPreview"] = new JsonObject
            {
                ["sampleOriginalFace"] = Exact(sampleFace),
                ["currency"] = currency,
                ["coupon"] = Exact(coupon!.Value),
                ["couponRateUnit"] = Text(security, "couponRateUnit"),
                ["couponCashBasis"] = cashBasis,
                ["paymentFrequency"] = Text(security, "paymentFrequency"),
                ["dayCount"] = Text(security, "dayCount"),
                ["accrualStart"] = period.AccrualStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["accrualEnd"] = period.AccrualEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["paymentDate"] = period.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["yearFraction"] = Exact(fullFraction),
                ["sampleCouponPayment"] = Exact(payment),
                ["paymentCalendars"] = paymentCalendars.DeepClone(),
                ["businessDayConvention"] = Text(security, "businessDayConvention"),
                ["settlementDays"] = Exact(settlementDays!.Value),
                ["settlementConvention"] = Text(security, "settlementConvention"),
                ["settlementCalendars"] = settlementCalendars.DeepClone(),
                ["callScheduleCount"] = calls.Count,
                ["factorHistoryCount"] = factors.Count,
                ["normalizedTermsJson"] = normalizedTerms.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                ["scheduleKernelVersion"] = ScheduleGenerator.KernelVersion,
                ["calendarEvidence"] = JsonSerializer.SerializeToNode(schedule.CalendarsUsed)
            };
        }
        catch (InvalidOperationException exception)
        {
            Refuse(SecurityVersionedTerms.ScheduleConventionsKey, exception.Message);
        }
        catch (OverflowException)
        {
            Refuse("coupon", "The sample payment exceeds the supported decimal range; review coupon units and dated factors.");
        }
    }

    private static JsonArray? Array(JsonObject source, string field, Action<string, string> refuse)
    {
        if (source[field] is JsonArray array)
            return array;
        if (Text(source, field) is { } text)
        {
            try
            {
                if (JsonNode.Parse(text) is JsonArray parsed)
                {
                    source[field] = parsed;
                    return parsed;
                }
            }
            catch (JsonException) { }
        }
        refuse(field, $"{field} must be an explicit JSON array; use [] to declare none.");
        return null;
    }

    private static string? Text(JsonObject source, string key)
        => source[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : null;
    private static decimal? Number(JsonObject source, string key)
        => source[key] is JsonValue value && value.TryGetValue<decimal>(out var number) ? number
            : decimal.TryParse(Text(source, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    private static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
