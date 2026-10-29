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
    string? CalculationVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    OpenLotAmortizationReversalDto? Reversal = null)
{
    /// <summary>Retained authority for continuing an unchanged schedule after a one-for-one exchange.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenLotAmortizationContinuationDto? Continuation { get; init; }

    /// <summary>Explicit v3 policy: continue the acquisition schedule on the retained remaining face.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AmortizeRemainingFace { get; init; }

    /// <summary>Deterministic comparison retained for independent review and recomputed at posting.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenLotYieldToWorstDto? YieldToWorst { get; init; }

    // Historical JSON has no version and retains the exact v1 calculation and fingerprint.
    // New callers using the original constructor shape always select the current model.
    public OpenLotAmortizationInstructionDto(OpenLotDto ExpectedLot, SecurityProjectionRecord Security,
        RetainedEvidenceIdentityDto SecurityEvidence, long ExpectedBookPositionVersion, DateOnly AsOfDate)
        : this(ExpectedLot, Security, SecurityEvidence, ExpectedBookPositionVersion, AsOfDate, OpenLotAmortization.ModelVersion)
    {
    }
}

/// <summary>Reviewed assertion only; posting verifies it against the immutable original mutation.</summary>
public sealed record OpenLotAmortizationReversalDto(
    Guid MutationBatchId, Guid JournalEntryId, OpenLotDto RestoresLot);

public sealed record OpenLotAmortizationProjectionDto(
    decimal TransactionCostBasis,
    decimal FunctionalCostBasis,
    decimal TransactionMovement,
    decimal FunctionalMovement);

/// <summary>Bounded fixed-rate bullet projection using the shared financial kernels and acquisition FX.</summary>
public static class OpenLotAmortization
{
    public const string ModelVersion = "canonical-lot-amortization-v2";
    public const string ExtendedModelVersion = "canonical-lot-amortization-v3";
    public const string DeclaredModelVersion = "canonical-lot-amortization-v4";
    public const string UnifiedDeclaredModelVersion = "canonical-lot-amortization-v5";
    /// <summary>Frozen regular-calendar model retained for immutable instruction replay.</summary>
    public const string CalendarModelVersion = ModelVersion;

    public static bool IsCurrentVersion(string? version)
        => version is ModelVersion or ExtendedModelVersion or DeclaredModelVersion or UnifiedDeclaredModelVersion;

    public static string SecurityHash(SecurityProjectionRecord security)
    {
        ArgumentNullException.ThrowIfNull(security);
        // PostgreSQL jsonb reorders object properties. Evidence identities must survive retention
        // and reload while still binding every value and array entry in the reviewed projection.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteOrderedJson(writer, JsonSerializer.SerializeToElement(security, OpenLotAmortizationJsonContext.Default.SecurityProjectionRecord));
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
        if (instruction.CalculationVersion is not null && !IsCurrentVersion(instruction.CalculationVersion))
            throw new ArgumentException("Amortization calculation version is unsupported.");
        var extended = instruction.CalculationVersion == ExtendedModelVersion;
        if ((!extended && (instruction.AmortizeRemainingFace || instruction.YieldToWorst is not null))
            || (extended && !instruction.AmortizeRemainingFace && instruction.YieldToWorst is null))
            throw new ArgumentException("Extended amortization policy requires an explicit v3 reviewed instruction.");
        var lot = instruction.ExpectedLot;
        OpenLotValidation.Validate(lot);
        if (lot.Acquisition.Direction != OpenLotDirection.Long)
            throw new ArgumentException("Canonical amortization currently supports Long lots only; short liability amortization requires a separately reviewed rule.");
        var security = instruction.Security;
        var evidence = instruction.SecurityEvidence;
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(evidence);
        var acquisition = lot.Acquisition;
        var scheduleLot = OpenLotAmortizationContinuation.Validate(instruction);
        var terms = acquisition.FaceValueTerms;
        if (lot.Version <= 0 || lot.OpenQuantity <= 0 || acquisition.QuantityBasis != LotQuantityBasis.Face || terms is null
            || (!instruction.AmortizeRemainingFace && terms.BookedFactor != 1m) || instruction.ExpectedBookPositionVersion <= 0)
            throw new ArgumentException("Amortization requires a versioned open face lot with unadjusted bullet principal and retained acquisition terms.");
        // A paid-down lot is not amortized: its premium or discount is realized pro rata through
        // factor paydowns and the remainder through the reduced basis at disposal.
        if (lot.CurrentFactor is not null && !instruction.AmortizeRemainingFace)
            throw new ArgumentException("Amortization refuses a lot that holds a paydown factor; its premium or discount is realized through factor paydowns and disposal.");
        var retainedReversal = instruction.Reversal is not null;
        if (security.SecurityId != lot.SecurityId || security.Version <= 0
            || (security.Status != SecurityStatusDto.Active && !(retainedReversal && security.Status == SecurityStatusDto.Inactive))
            || security.Currency != acquisition.AcquisitionCurrency
            || (!retainedReversal && (DateOnly.FromDateTime(security.EffectiveFrom.UtcDateTime) > instruction.AsOfDate
                || (security.EffectiveTo is { } end && DateOnly.FromDateTime(end.UtcDateTime) < instruction.AsOfDate))))
            throw new ArgumentException("Amortization Security Master identity, currency, status or effective scope is invalid.");
        if (!RetainedEvidenceIdentityValidator.IsComplete(evidence) || evidence.SubjectType != "SecurityMasterProjection"
            || evidence.SubjectId != security.SecurityId.ToString("D") || evidence.EvidenceVersion != security.Version
            || (!retainedReversal && evidence.EffectiveDate > instruction.AsOfDate)
            || !Sha256Digest.FixedEquals(evidence.ContentHashSha256, SecurityHash(security)))
            throw new ArgumentException("Amortization requires hash-bound versioned Security Master projection evidence.");
        if (instruction.Reversal is { } reversal)
        {
            // Current reference evidence proves identity; the immutable before/after receipt
            // supplies the inverse economics. Current terms do not authorize a new accrual here.
            OpenLotValidation.Validate(reversal.RestoresLot);
            var restored = reversal.RestoresLot;
            if (reversal.MutationBatchId == Guid.Empty || reversal.JournalEntryId == Guid.Empty
                || restored.Version >= lot.Version
                || !SameLot(restored with
                {
                    Version = lot.Version,
                    OpenTransactionCostBasis = lot.OpenTransactionCostBasis,
                    OpenFunctionalCostBasis = lot.OpenFunctionalCostBasis
                }, lot))
                throw new ArgumentException("Amortization reversal must retain the original lot identity, quantity and acquisition facts.");
            return new(restored.OpenTransactionCostBasis, restored.OpenFunctionalCostBasis,
                restored.OpenTransactionCostBasis - lot.OpenTransactionCostBasis,
                restored.OpenFunctionalCostBasis - lot.OpenFunctionalCostBasis);
        }
        var detail = new SecurityDetailDto(security.SecurityId, security.AssetClass, security.Status, security.DisplayName,
            security.Currency, security.CommonTerms, security.AssetSpecificTerms, security.Identifiers, security.Aliases,
            security.Version, security.EffectiveFrom, security.EffectiveTo);
        var followsDeclaredSchedule = instruction.CalculationVersion is DeclaredModelVersion or UnifiedDeclaredModelVersion
            && AnyTerm(StructuredCashFlowTermsResolver.EnumerateTermSources(detail),
                SecurityVersionedTerms.ScheduleConventionsKey, SecurityAssetTermsSchema.IsPopulatedTermValue);
        if (instruction.CalculationVersion is DeclaredModelVersion or UnifiedDeclaredModelVersion && !followsDeclaredSchedule)
            throw new ArgumentException("Declared amortization model requires explicit contractual schedule conventions.");
        ValidateBulletReference(detail, instruction.YieldToWorst is not null, instruction.AmortizeRemainingFace, followsDeclaredSchedule);
        var reference = StructuredCashFlowTermsResolver.Resolve(detail);
        OpenLotRedemptionYieldDto? redemption = null;
        if (instruction.YieldToWorst is { } reviewed)
        {
            var calculated = OpenLotYieldToWorst.Select(scheduleLot, security);
            if (reviewed.Selected != calculated.Selected || reviewed.Alternatives is null
                || !reviewed.Alternatives.SequenceEqual(calculated.Alternatives))
                throw new ArgumentException("Yield-to-worst selection differs from the complete retained contractual alternatives.");
            redemption = calculated.Selected;
        }
        var convention = DayCountConventions.Parse(reference.DayCountConvention);
        var redemptionDate = redemption?.Date ?? reference.MaturityDate;
        if (redemptionDate is not { } maturity || maturity <= lot.AcquiredDate
            || instruction.AsOfDate <= lot.AcquiredDate || instruction.AsOfDate > maturity
            || convention is DayCountConvention.Unknown or DayCountConvention.Business252
            || (!followsDeclaredSchedule && convention is DayCountConvention.ActualActualIcma or DayCountConvention.ThirtyE360Isda)
            || reference.HasFactorSchedule || reference.HasLegs || reference.HasPrincipalSchedule || reference.HasStepCouponSchedule
            || reference.InflationIndex is not null || reference.InflationIndexRatio is not null
            || reference.InflationBaseIndexValue is not null
            || (reference.CurrentFactor is { } factor && factor != (instruction.AmortizeRemainingFace
                ? lot.CurrentFactor ?? terms.BookedFactor : 1m)))
            throw new ArgumentException("Amortization requires complete supported day-count and fixed-rate bullet terms; structured successors require a separate workflow.");
        var redemptionRatio = (redemption?.PricePercentOfPar ?? 100m) / 100m;
        var faceLot = new FaceValueLot(lot.LotId, lot.SecurityId, scheduleLot.AcquiredDate, scheduleLot.OriginalQuantity * redemptionRatio,
            scheduleLot.Acquisition.TransactionCostBasis * terms.ParBasis / scheduleLot.OriginalQuantity / redemptionRatio,
            terms.BookedFactor, terms.ParBasis);
        decimal fullBasis;
        if (followsDeclaredSchedule)
        {
            fullBasis = instruction.CalculationVersion == UnifiedDeclaredModelVersion
                ? OpenLotUnifiedAmortization.Project(faceLot, detail, reference, convention,
                    instruction.AsOfDate, terms.AmortizationMethod, terms.EffectiveYield)
                : OpenLotDeclaredAmortization.Project(faceLot, detail, reference, convention,
                    instruction.AsOfDate, terms.AmortizationMethod, terms.EffectiveYield);
        }
        else if (terms.AmortizationMethod == BondAmortizationMethod.StraightLine)
        {
            fullBasis = faceLot.AmortizedBasisAsOf(convention, maturity, instruction.AsOfDate);
        }
        else if (terms.AmortizationMethod == BondAmortizationMethod.ConstantYield
                 && (redemption?.AnnualYield ?? terms.EffectiveYield) is { } annualYield
                 && annualYield > -1m && reference.CouponRate is { } coupon && coupon >= 0m)
        {
            var frequency = Frequency(reference.PaymentFrequency);
            coupon /= redemptionRatio;
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
                // Model v2 admits regular calendar schedules only: the kernel's odd-first-period
                // support needs a new governed calculation version before it can post.
                fullBasis = faceLot.RegularScheduleConstantYieldAmortizedBasisAsOf(convention, maturity,
                    instruction.AsOfDate, coupon, frequency, annualYield);
            }
        }
        else
        {
            throw new ArgumentException("Only retained straight-line and constant-yield amortization inputs are supported.");
        }
        // PostgreSQL journal and lot amounts share a 12-place decimal boundary. Round cumulative
        // targets once, then subtract retained basis: repeated periods do not accumulate rounded deltas.
        // Durable quantity retains booked face after a paydown. Apply the held-factor ratio once;
        // a partial disposal scales booked quantity independently of that principal decline.
        var remainingFactor = instruction.AmortizeRemainingFace ? (lot.CurrentFactor ?? terms.BookedFactor) / terms.BookedFactor : 1m;
        var transaction = decimal.Round(fullBasis * lot.OpenQuantity / scheduleLot.OriginalQuantity * remainingFactor, 12, MidpointRounding.ToEven);
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

    public static bool SameLot(OpenLotDto left, OpenLotDto right)
        => JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(left, OpenLotAmortizationJsonContext.Default.OpenLotDto),
            JsonSerializer.SerializeToElement(right, OpenLotAmortizationJsonContext.Default.OpenLotDto));

    public static bool SameInstruction(OpenLotAmortizationInstructionDto? left, OpenLotAmortizationInstructionDto? right)
        => JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(left, OpenLotAmortizationJsonContext.Default.OpenLotAmortizationInstructionDto),
            JsonSerializer.SerializeToElement(right, OpenLotAmortizationJsonContext.Default.OpenLotAmortizationInstructionDto));

    private static int Frequency(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "annual" or "annually" or "1" => 1,
        "semiannual" or "semi-annual" or "semiannually" or "2" => 2,
        "quarterly" or "4" => 4,
        "monthly" or "12" => 12,
        _ => throw new ArgumentException("Constant-yield amortization requires an explicit supported coupon frequency.")
    };

    internal static void ValidateBulletReference(SecurityDetailDto detail, bool automaticRedemption, bool remainingFace,
        bool followsDeclaredSchedule = false)
    {
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(detail.AssetClass);
        var assetClass = descriptor.AssetClass;
        var factorBacked = remainingFace && descriptor.LotUnit == SecurityLotUnitKind.FaceValue
            && descriptor.CashFlowBehavior == SecurityCashFlowBehavior.FactorAmortizing;
        if (!descriptor.SupportsCanonicalBulletAmortization && !factorBacked)
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
        if (SecurityAssetClassCatalog.GetOrDefault(assetClass).CarriesBondCouponStructure || factorBacked)
        {
            var couponType = OwnedString(sources, ["couponType", "couponKind"]);
            var coupon = OwnedDecimal(sources, ["fixedCouponRate", "couponRate", "coupon", "annualRate"]);
            if (followsDeclaredSchedule && couponType == "ZeroCoupon" && coupon is null)
                coupon = 0m;
            if (couponType is not ("Fixed" or "ZeroCoupon") || coupon is null
                || coupon < 0m || (couponType == "ZeroCoupon" && coupon != 0m))
                throw new ArgumentException("Bond amortization requires explicit supported Fixed or ZeroCoupon structure and a complete coupon rate.");
            var callable = OwnedString(sources, ["isCallable"]);
            if (!bool.TryParse(callable, out var isCallable) || (isCallable && !automaticRedemption))
                throw new ArgumentException("Bond amortization requires explicit non-callable bullet terms.");
            if (isCallable && automaticRedemption
                && !AnyTerm(sources, SecurityVersionedTerms.CallScheduleKey,
                    static value => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0))
                throw new ArgumentException("Callable amortization requires the complete explicit call schedule for automatic comparison.");
            if (!isCallable && automaticRedemption && AnyTerm(sources, SecurityVersionedTerms.CallScheduleKey,
                    static value => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0))
                throw new ArgumentException("Call schedule and callable flag disagree; repair the authoritative terms before automatic comparison.");
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
        // Declared call and put schedules (readable from terms version 3, ADR-024) are optionality a
        // bullet cannot honour; an empty list declares none, and a malformed one is not read as absent.
        foreach (var name in new[] { SecurityVersionedTerms.CallScheduleKey, SecurityVersionedTerms.PutScheduleKey })
        {
            if (!automaticRedemption && AnyTerm(sources, name, static schedule => schedule.ValueKind != JsonValueKind.Null
                    && (schedule.ValueKind != JsonValueKind.Array || schedule.GetArrayLength() != 0)))
                throw new ArgumentException("Amortization does not support floating coupons, calls, puts or advance-refunding terms.");
        }
        // A declared scheduleConventions block (ADR-024; writable from the contract step) moves payment
        // dates, stubs and the coupon unit, and the bullet coupon grid follows none of them. Unset
        // spellings declare nothing.
        if (!followsDeclaredSchedule && AnyTerm(sources, SecurityVersionedTerms.ScheduleConventionsKey, SecurityAssetTermsSchema.IsPopulatedTermValue))
            throw new ArgumentException("Amortization does not support declared schedule conventions; the bullet coupon grid follows none of them.");
        foreach (var name in new[] { "floatingIndex", "floatingRateIndex", "referenceIndex", "callDate", "preRefundDate", "mandatoryPutDate" })
        {
            if (FirstTerm(sources, [name]) is { } value && value.ValueKind != JsonValueKind.Null
                && !(value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())))
                throw new ArgumentException("Amortization does not support floating coupons, calls, puts or advance-refunding terms.");
        }
    }

    // Declared schedule terms are checked in every source the cash-flow resolver reads and under every
    // casing: an unset spelling in one source never hides a populated one in another (ADR-024, ADR-025).
    private static bool AnyTerm(IEnumerable<JsonElement> sources, string name, Func<JsonElement, bool> refuses)
    {
        foreach (var source in sources)
            if (source.ValueKind == JsonValueKind.Object)
                foreach (var property in source.EnumerateObject())
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && refuses(property.Value))
                        return true;
        return false;
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

    private static void WriteOrderedJson(Utf8JsonWriter writer, JsonElement element)
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

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SecurityProjectionRecord))]
[JsonSerializable(typeof(OpenLotDto))]
[JsonSerializable(typeof(OpenLotAmortizationInstructionDto))]
internal sealed partial class OpenLotAmortizationJsonContext : JsonSerializerContext;
