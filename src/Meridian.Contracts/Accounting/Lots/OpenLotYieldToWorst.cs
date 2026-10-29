using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Nominal annual yield, compounded at the contractual coupon frequency.</summary>
public sealed record OpenLotRedemptionYieldDto(string Kind, DateOnly Date, decimal PricePercentOfPar, decimal AnnualYield);

/// <summary>Holder-controlled puts are valued but cannot force an adverse issuer redemption.</summary>
public sealed record OpenLotYieldToWorstDto(OpenLotRedemptionYieldDto Selected,
    IReadOnlyList<OpenLotRedemptionYieldDto> Alternatives);

/// <summary>
/// Deterministic acquisition-price yield comparison for fixed European whole redemptions on the
/// regular coupon grid. Windows, make-whole prices and partial exercise require additional inputs.
/// </summary>
public static class OpenLotYieldToWorst
{
    public static OpenLotYieldToWorstDto Select(OpenLotDto lot, SecurityProjectionRecord security)
    {
        OpenLotValidation.Validate(lot);
        if (lot.SecurityId != security.SecurityId || lot.Acquisition.FaceValueTerms is not { } terms)
            throw new ArgumentException("Redemption yields require matching Security Master identity and retained face terms.");
        var detail = new SecurityDetailDto(security.SecurityId, security.AssetClass, security.Status, security.DisplayName,
            security.Currency, security.CommonTerms, security.AssetSpecificTerms, security.Identifiers, security.Aliases,
            security.Version, security.EffectiveFrom, security.EffectiveTo);
        OpenLotAmortization.ValidateBulletReference(detail, automaticRedemption: true, remainingFace: true);
        var reference = StructuredCashFlowTermsResolver.Resolve(detail);
        if (reference.MaturityDate is not { } maturity || reference.CouponRate is not { } coupon || coupon < 0m)
            throw new ArgumentException("Redemption yields require explicit maturity and fixed coupon terms.");
        var frequency = Frequency(reference.PaymentFrequency);
        var candidates = new List<OpenLotRedemptionYieldDto> { Price("Maturity", maturity, 100m) };
        foreach (var kind in new[] { "Call", "Put" })
        {
            var name = kind == "Call" ? SecurityVersionedTerms.CallScheduleKey : SecurityVersionedTerms.PutScheduleKey;
            JsonElement? owned = null;
            foreach (var source in StructuredCashFlowTermsResolver.EnumerateTermSources(detail))
            {
                if (source.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Redemption yields require complete object-shaped reference terms.");
                foreach (var property in source.EnumerateObject())
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind != JsonValueKind.Null)
                    {
                        if (owned is not null)
                            throw new ArgumentException("Redemption schedules require one unambiguous owning term source.");
                        owned = property.Value;
                    }
            }
            if (owned is not { } schedule)
                continue;
            if (schedule.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Redemption schedules must be arrays; malformed authority cannot be omitted.");
            foreach (var row in schedule.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Redemption schedule entries must be complete objects.");
                var prefix = kind.ToLowerInvariant();
                var dateText = SecurityTermReader.ReadString(row, prefix + "Date");
                if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var date)
                    || date <= lot.AcquiredDate || date > maturity
                    || SecurityTermReader.ReadString(row, "exerciseStyle") != "European"
                    || SecurityTermReader.ReadString(row, "priceBasis") != "PercentOfPar"
                    || SecurityTermReader.ReadDecimal(row, prefix + "Price") is not { } price || price <= 0m
                    || (SecurityTermReader.ReadString(row, prefix + "EndDate") is { } end && end != dateText)
                    || (kind == "Call" && (SecurityTermReader.ReadString(row, "priceType") != "Fixed"
                        || SecurityTermReader.ReadString(row, "partialRedemption") is not (null or "WholeOnly"))))
                    throw new ArgumentException("Automatic yield comparison requires explicit fixed-price European whole redemption dates within the lot's life.");
                // Make-whole spreads, notice windows and alternate cadence are not interpreted as
                // fixed point redemptions, even when a stray fixed price is also present.
                foreach (var member in new[] { "makeWholeSpreadBps", "minimumNoticeDays", "maximumNoticeDays", "exerciseFrequency" })
                    if (SecurityTermReader.TryGetProperty(row, member, out var value) && value.ValueKind != JsonValueKind.Null)
                        throw new ArgumentException("Redemption notice, cadence and make-whole calculations require a separate supported schedule treatment.");
                candidates.Add(Price(kind, date, price));
            }
        }
        var ordered = candidates.OrderBy(candidate => candidate.Date).ThenBy(candidate => candidate.Kind, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.PricePercentOfPar).ToArray();
        if (ordered.Distinct().Count() != ordered.Length)
            throw new ArgumentException("Duplicate redemption alternatives are ambiguous.");
        var selected = ordered.Where(candidate => candidate.Kind != "Put").OrderBy(candidate => candidate.AnnualYield)
            .ThenBy(candidate => candidate.Date).ThenBy(candidate => candidate.Kind, StringComparer.Ordinal).First();
        return new(selected, ordered);

        OpenLotRedemptionYieldDto Price(string kind, DateOnly date, decimal price)
        {
            // Re-denominate principal to the redemption amount; scale coupon inversely so the
            // actual cash coupon and acquisition price stay identical. Par basis is a quote unit,
            // whereas schedule prices are always percentages of par.
            var ratio = price / 100m;
            var face = new FaceValueLot(lot.LotId, lot.SecurityId, lot.AcquiredDate,
                lot.OriginalQuantity * ratio, lot.Acquisition.TransactionCostBasis * terms.ParBasis
                    / lot.OriginalQuantity / ratio, terms.BookedFactor, terms.ParBasis);
            return new(kind, date, price, face.RegularScheduleAnnualYield(date, coupon / ratio, frequency));
        }
    }

    internal static int Frequency(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "annual" or "annually" or "1" => 1,
        "semiannual" or "semi-annual" or "semiannually" or "2" => 2,
        "quarterly" or "4" => 4,
        "monthly" or "12" => 12,
        _ => throw new ArgumentException("Redemption yields require an explicit regular coupon frequency.")
    };
}
