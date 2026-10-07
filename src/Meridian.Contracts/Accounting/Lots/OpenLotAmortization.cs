using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Reviewed inputs, never posting authority. The store resolves and locks these identities again.</summary>
[method: JsonConstructor]
public sealed record OpenLotAmortizationInstructionDto(
    OpenLotDto ExpectedLot,
    SecurityProjectionRecord Security,
    RetainedEvidenceIdentityDto SecurityEvidence,
    long ExpectedBookPositionVersion,
    DateOnly AsOfDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CalculationVersion = null)
{
    // Historical JSON has no version and retains the exact v1 calculation and fingerprint.
    // New callers using the original constructor shape always select the current model.
    public OpenLotAmortizationInstructionDto(OpenLotDto ExpectedLot, SecurityProjectionRecord Security,
        RetainedEvidenceIdentityDto SecurityEvidence, long ExpectedBookPositionVersion, DateOnly AsOfDate)
        : this(ExpectedLot, Security, SecurityEvidence, ExpectedBookPositionVersion, AsOfDate, OpenLotAmortization.ModelVersion)
    {
    }
}

public sealed record OpenLotAmortizationProjectionDto(
    decimal TransactionCostBasis,
    decimal FunctionalCostBasis,
    decimal TransactionMovement,
    decimal FunctionalMovement);

/// <summary>Bounded fixed-rate bullet projection using the shared financial kernels and acquisition FX.</summary>
public static class OpenLotAmortization
{
    public const string ModelVersion = "canonical-lot-amortization-v2";

    public static string SecurityHash(SecurityProjectionRecord security)
    {
        ArgumentNullException.ThrowIfNull(security);
        // PostgreSQL jsonb reorders object properties. Evidence identities must survive retention
        // and reload while still binding every value and array entry in the reviewed projection.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteOrderedJson(writer, JsonSerializer.SerializeToElement(security));
        return Sha256Digest.Compute(stream.ToArray());
    }

    public static OpenLotAmortizationProjectionDto Project(OpenLotAmortizationInstructionDto instruction)
    {
        try
        {
            return ProjectCore(instruction);
        }
        catch (ArithmeticException exception)
        {
            throw new ArgumentException("Amortization inputs exceed supported decimal calculation bounds.", nameof(instruction), exception);
        }
    }

    private static OpenLotAmortizationProjectionDto ProjectCore(OpenLotAmortizationInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        if (instruction.CalculationVersion is not null && instruction.CalculationVersion != ModelVersion)
            throw new ArgumentException("Amortization calculation version is unsupported.");
        var lot = instruction.ExpectedLot;
        OpenLotValidation.Validate(lot);
        var security = instruction.Security;
        var evidence = instruction.SecurityEvidence;
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(evidence);
        var acquisition = lot.Acquisition;
        if (acquisition.CorporateActionLineage is not null)
            throw new ArgumentException("Corporate-action successors require a separately reviewed amortization/yield continuation; inherited acquisition terms do not authorize a new schedule.");
        var terms = acquisition.FaceValueTerms;
        if (lot.Version <= 0 || lot.OpenQuantity <= 0 || acquisition.QuantityBasis != LotQuantityBasis.Face || terms is null
            || terms.BookedFactor != 1m || instruction.ExpectedBookPositionVersion <= 0)
            throw new ArgumentException("Amortization requires a versioned open face lot with unadjusted bullet principal and retained acquisition terms.");
        if (security.SecurityId != lot.SecurityId || security.Version <= 0 || security.Status != SecurityStatusDto.Active
            || security.Currency != acquisition.AcquisitionCurrency
            || DateOnly.FromDateTime(security.EffectiveFrom.UtcDateTime) > instruction.AsOfDate
            || (security.EffectiveTo is { } end && DateOnly.FromDateTime(end.UtcDateTime) < instruction.AsOfDate))
            throw new ArgumentException("Amortization Security Master identity, currency, status or effective scope is invalid.");
        if (!RetainedEvidenceIdentityValidator.IsComplete(evidence) || evidence.SubjectType != "SecurityMasterProjection"
            || evidence.SubjectId != security.SecurityId.ToString("D") || evidence.EvidenceVersion != security.Version
            || evidence.EffectiveDate > instruction.AsOfDate || !Sha256Digest.FixedEquals(evidence.ContentHashSha256, SecurityHash(security)))
            throw new ArgumentException("Amortization requires hash-bound versioned Security Master projection evidence.");
        var detail = new SecurityDetailDto(security.SecurityId, security.AssetClass, security.Status, security.DisplayName,
            security.Currency, security.CommonTerms, security.AssetSpecificTerms, security.Identifiers, security.Aliases,
            security.Version, security.EffectiveFrom, security.EffectiveTo);
        ValidateBulletReference(detail);
        var reference = StructuredCashFlowTermsResolver.Resolve(detail);
        var convention = DayCountConventions.Parse(reference.DayCountConvention);
        if (reference.MaturityDate is not { } maturity || maturity <= lot.AcquiredDate
            || instruction.AsOfDate <= lot.AcquiredDate || instruction.AsOfDate > maturity
            || convention is DayCountConvention.Unknown or DayCountConvention.Business252 or DayCountConvention.ActualActualIcma
                or DayCountConvention.ThirtyE360Isda
            || reference.HasFactorSchedule || reference.HasLegs || reference.HasPrincipalSchedule || reference.HasStepCouponSchedule
            || reference.InflationIndex is not null || reference.InflationIndexRatio is not null
            || reference.InflationBaseIndexValue is not null || (reference.CurrentFactor is { } factor && factor != 1m))
            throw new ArgumentException("Amortization requires complete supported day-count and fixed-rate bullet terms; structured successors require a separate workflow.");
        var faceLot = new FaceValueLot(lot.LotId, lot.SecurityId, lot.AcquiredDate, lot.OriginalQuantity,
            acquisition.TransactionCostBasis * terms.ParBasis / lot.OriginalQuantity, terms.BookedFactor, terms.ParBasis);
        decimal fullBasis;
        if (terms.AmortizationMethod == BondAmortizationMethod.StraightLine)
        {
            fullBasis = faceLot.AmortizedBasisAsOf(convention, maturity, instruction.AsOfDate);
        }
        else if (terms.AmortizationMethod == BondAmortizationMethod.ConstantYield && terms.EffectiveYield is { } annualYield
                 && annualYield > -1m && reference.CouponRate is { } coupon && coupon >= 0m)
        {
            var frequency = Frequency(reference.PaymentFrequency);
            if (instruction.CalculationVersion is null)
            {
                // Preserve the admission rule and arithmetic of retained v1 instructions.
                // Replaying an approved historical payload must not recalculate its journal.
                var periods = DayCountConventions.Fraction(convention, lot.AcquiredDate, maturity) * frequency;
                if (periods != decimal.Truncate(periods) || periods < 1m || periods > 1200m)
                    throw new ArgumentException("Constant-yield amortization requires one to 1200 level coupon periods.");
                fullBasis = faceLot.LegacyConstantYieldAmortizedBasisAsOf(convention, maturity, instruction.AsOfDate, coupon,
                    frequency, annualYield);
            }
            else
            {
                // The shared kernel validates calendar coupon boundaries. An Actual day-count
                // year fraction measures accrual, not the number of contractual payments.
                fullBasis = faceLot.ConstantYieldAmortizedBasisAsOf(convention, maturity, instruction.AsOfDate, coupon,
                    frequency, annualYield);
            }
        }
        else
        {
            throw new ArgumentException("Only retained straight-line and constant-yield amortization inputs are supported.");
        }
        // PostgreSQL journal and lot amounts share a 12-place decimal boundary. Round cumulative
        // targets once, then subtract retained basis: repeated periods do not accumulate rounded deltas.
        var transaction = decimal.Round(fullBasis * lot.OpenQuantity / lot.OriginalQuantity, 12, MidpointRounding.ToEven);
        var functional = decimal.Round(transaction * acquisition.AcquisitionFxRateToFunctional, 12, MidpointRounding.ToEven);
        if (transaction < 0m || functional < 0m)
            throw new ArgumentException("Amortized carrying basis cannot be negative.");
        var transactionMovement = transaction - lot.OpenTransactionCostBasis;
        var functionalMovement = functional - lot.OpenFunctionalCostBasis;
        if (decimal.Round(transactionMovement, 12, MidpointRounding.ToEven) != transactionMovement
            || decimal.Round(functionalMovement, 12, MidpointRounding.ToEven) != functionalMovement)
            throw new ArgumentException("Amortization carrying-basis movement must be exactly representable at the 12-decimal journal boundary; fractional retained basis requires a separate governed residual treatment.");
        return new(transaction, functional, transactionMovement, functionalMovement);
    }

    private static int Frequency(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "annual" or "annually" or "1" => 1,
        "semiannual" or "semi-annual" or "semiannually" or "2" => 2,
        "quarterly" or "4" => 4,
        "monthly" or "12" => 12,
        _ => throw new ArgumentException("Constant-yield amortization requires an explicit supported coupon frequency.")
    };

    private static void ValidateBulletReference(SecurityDetailDto detail)
    {
        var assetClass = SecurityAssetClassCatalog.GetOrDefault(detail.AssetClass).AssetClass;
        if (assetClass is not ("Bond" or "TreasuryBill" or "CommercialPaper"))
            throw new ArgumentException("Canonical amortization supports only Bond, TreasuryBill and CommercialPaper face lots.");
        if (detail.CommonTerms.ValueKind != JsonValueKind.Object || detail.AssetSpecificTerms.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Amortization requires complete object-shaped Security Master terms.");
        if (SecurityTermReader.TryGetProperty(detail.AssetSpecificTerms, "profileFields", out var profileFields)
            && profileFields.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Amortization requires valid governed profile-field authority.");
        var sources = StructuredCashFlowTermsResolver.EnumerateTermSources(detail).ToArray();
        if (FirstTerm(sources, ["currentFactor", "factor"]) is { } ownedFactor && ownedFactor.ValueKind != JsonValueKind.Null
            && OwnedDecimal(sources, ["currentFactor", "factor"]) is null)
            throw new ArgumentException("Amortization requires valid owned principal-factor authority; malformed factors cannot receive defaults.");
        if (assetClass == "Bond")
        {
            var couponType = OwnedString(sources, ["couponType", "couponKind"]);
            if (couponType is not ("Fixed" or "ZeroCoupon")
                || OwnedDecimal(sources, ["fixedCouponRate", "couponRate", "coupon", "annualRate"]) is not { } coupon
                || coupon < 0m || (couponType == "ZeroCoupon" && coupon != 0m))
                throw new ArgumentException("Bond amortization requires explicit supported Fixed or ZeroCoupon structure and a complete coupon rate.");
            var callable = OwnedString(sources, ["isCallable"]);
            if (!bool.TryParse(callable, out var isCallable) || isCallable)
                throw new ArgumentException("Bond amortization requires explicit non-callable bullet terms.");
        }
        // The cash-flow resolver is deliberately read-tolerant and skips malformed schedule rows.
        // This posting slice must refuse their ownership claims before that tolerance erases them.
        string[][] unsupportedSchedules =
        [
            ["factorScheduleEntries", "factorSchedule", "factorSchedules"],
            ["principalSchedule"], ["stepSchedule", "stepCouponSchedule", "couponSteps"],
            ["legs", "swapLegs", "cashFlowLegs"], ["sinkingFundSchedule", "sinkSchedule"]
        ];
        foreach (var aliases in unsupportedSchedules)
        {
            if (FirstTerm(sources, aliases) is { } schedule && schedule.ValueKind != JsonValueKind.Null
                && (schedule.ValueKind != JsonValueKind.Array || schedule.GetArrayLength() != 0))
                throw new ArgumentException("Amortization cannot use nonempty or malformed principal, factor, coupon or leg schedules.");
        }
        foreach (var name in new[] { "floatingIndex", "floatingRateIndex", "referenceIndex", "callDate", "preRefundDate", "mandatoryPutDate" })
        {
            if (FirstTerm(sources, [name]) is { } value && value.ValueKind != JsonValueKind.Null
                && !(value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())))
                throw new ArgumentException("Amortization does not support floating coupons, calls, puts or advance-refunding terms.");
        }
    }

    private static JsonElement? FirstTerm(IEnumerable<JsonElement> sources, string[] aliases)
    {
        foreach (var source in sources)
            foreach (var alias in aliases)
                if (SecurityTermReader.TryGetProperty(source, alias, out var value))
                    return value;
        return null;
    }

    private static string? OwnedString(IEnumerable<JsonElement> sources, string[] aliases)
    {
        foreach (var source in sources)
            foreach (var alias in aliases)
                if (SecurityTermReader.TryGetProperty(source, alias, out _))
                    return SecurityTermReader.ReadString(source, alias);
        return null;
    }

    private static decimal? OwnedDecimal(IEnumerable<JsonElement> sources, string[] aliases)
    {
        foreach (var source in sources)
            foreach (var alias in aliases)
                if (SecurityTermReader.TryGetProperty(source, alias, out _))
                    return SecurityTermReader.ReadDecimal(source, alias);
        return null;
    }

    internal static void WriteOrderedJson(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteOrderedJson(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
                WriteOrderedJson(writer, item);
            writer.WriteEndArray();
        }
        else
            element.WriteTo(writer);
    }
}
