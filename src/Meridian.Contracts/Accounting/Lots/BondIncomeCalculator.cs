using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Versioned signed-cash interest method, independent of market-return attribution.</summary>
public static class BondIncomeCalculator
{
    public const string ModelVersion = "bond-income-us-gaap-v1";
    private const decimal Quantum = 0.000000000001m;

    public static BondIncomePolicyDto ReadPolicy(AccountingPolicyDto policy)
    {
        if (policy.AccountingBasis != AccountingBasisKindDto.Gaap)
            throw new ArgumentException("Bond income requires an explicit US GAAP amortized-cost book policy.");
        using var document = JsonDocument.Parse(policy.RulesJson);
        if (!document.RootElement.TryGetProperty("bondIncome", out var element))
            throw new ArgumentException("The authoritative book policy has no adopted bond-income model.");
        var value = element.Deserialize<BondIncomePolicyDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new ArgumentException("The book bond-income policy is empty.");
        if (value.ModelVersion != ModelVersion || !Enum.IsDefined(value.FloaterPolicy)
            || !element.EnumerateObject().Any(p => p.Name.Equals("floaterPolicy", StringComparison.OrdinalIgnoreCase))
            || element.EnumerateObject().Any(p => p.Name.Equals("bookedTargetPrecisionVersion", StringComparison.OrdinalIgnoreCase)
                && (p.Value.ValueKind != JsonValueKind.String || p.Value.GetString() != BondIncomeBookedPrecision.PolicyVersion)))
            throw new ArgumentException("The book bond-income model or floater policy is unsupported.");
        return value;
    }

    public static void ValidateAcquisition(BondIncomeAcquisitionDto value, decimal retainedCost)
    {
        if (value.KnownAt == default || value.KnownAt.Offset != TimeSpan.Zero || value.CleanConsideration <= 0m
            || value.IndexRatio <= 0m || value.Fees is null || value.Evidence is null || value.Evidence.Count == 0
            || value.Evidence.Any(e => !RetainedEvidenceIdentityValidator.IsComplete(e)))
            throw new ArgumentException("Bond income requires retained reviewed acquisition cost, index ratio and knowledge cutoff.");
        if (value.Fees.Any(f => f.Amount < 0m || !Enum.IsDefined(f.Treatment)
                || string.IsNullOrWhiteSpace(f.SourceReference) || !value.Evidence.Contains(f.Evidence))
            || value.Fees.Select(f => f.SourceReference).Distinct(StringComparer.Ordinal).Count() != value.Fees.Count)
            throw new ArgumentException("Acquisition fees require unique source identities, typed treatment and covered retained evidence.");
        var eligible = value.Fees.Where(f => f.Treatment == BondIncomeFeeTreatment.EligibleDirectAcquisitionCost).Sum(f => f.Amount);
        if (Math.Abs(value.CleanConsideration + eligible - retainedCost) > Quantum)
            throw new ArgumentException("Retained acquisition basis must equal clean consideration plus eligible capitalized fees exactly once.");
    }

    public static BondIncomeResultDto Project(OpenLotDto lot, BondIncomeInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(instruction.Policy);
        ArgumentNullException.ThrowIfNull(instruction.Evidence);
        ArgumentNullException.ThrowIfNull(instruction.Calls);
        OpenLotValidation.Validate(lot);
        var acquisition = lot.Acquisition.BondIncome
            ?? throw new ArgumentException("The authoritative lot has no reviewed bond-income acquisition facts.");
        ValidateAcquisition(acquisition, lot.Acquisition.TransactionCostBasis);
        if (lot.Acquisition.Direction != OpenLotDirection.Long || lot.Acquisition.QuantityBasis != LotQuantityBasis.Face
            || lot.Acquisition.FaceValueTerms is not { } face || lot.Version <= 0 || lot.OpenQuantity <= 0m
            || instruction.LedgerBookId != lot.LedgerBookId || instruction.AcquisitionDate != lot.AcquiredDate
            || instruction.ThroughDate <= lot.AcquiredDate || instruction.KnownAt < acquisition.KnownAt
            || instruction.KnownAt.Offset != TimeSpan.Zero || instruction.Policy.ModelVersion != ModelVersion
            || !Enum.IsDefined(instruction.Policy.FloaterPolicy) || instruction.PaymentsPerYear is not (1 or 2 or 4 or 12)
            || string.IsNullOrWhiteSpace(instruction.AccountingPolicyId) || string.IsNullOrWhiteSpace(instruction.AccountingPolicyVersion))
            throw new ArgumentException("Bond income requires a versioned performing long face lot and explicit book policy/cutoffs.");
        if (instruction.Fees is null || !instruction.Fees.SequenceEqual(acquisition.Fees)
            || instruction.AcquisitionAccruedInterest != acquisition.PurchasedAccruedInterest
            || instruction.AcquisitionIndexRatio != acquisition.IndexRatio || instruction.CurrentIndexRatio <= 0m
            || instruction.PrincipalForIndexation < 0m || instruction.Resets is null
            || instruction.Evidence.Any(e => !RetainedEvidenceIdentityValidator.IsComplete(e)))
            throw new ArgumentException("Bond income inputs differ from retained acquisition or observation evidence.");
        if (!instruction.Policy.CapitalizeEligibleDirectCosts
            && instruction.Fees.Any(f => f.Treatment == BondIncomeFeeTreatment.EligibleDirectAcquisitionCost && f.Amount != 0m))
            throw new ArgumentException("This book policy has not adopted capitalization of eligible direct acquisition costs.");
        ValidateProjection(instruction.AcquisitionProjection, lot.SecurityId);
        ValidateProjection(instruction.CurrentProjection, lot.SecurityId);
        ValidateProjection(instruction.AmortizationProjection, lot.SecurityId, allowEmpty: true);
        if (instruction.CurrentProjection.CashFlows.Any(f => f.PaymentDate > lot.AcquiredDate
                && f.PaymentDate <= instruction.ThroughDate && f.IsForecast))
            throw new ArgumentException("Recognized coupon income requires observed inputs for elapsed contractual coupons.");

        var share = lot.OpenQuantity / lot.OriginalQuantity;
        var originalCost = lot.Acquisition.TransactionCostBasis;
        var initialClean = originalCost;
        if (instruction.PurchasedCouponTail is { } purchasedTail
            && (lot.OpenQuantity != lot.OriginalQuantity || instruction.AcquisitionAccruedInterest != BondIncomeCouponRounding.AllocatedCash(
                    lot.TaxLotRecordId, instruction, purchasedTail.PaymentDate, purchasedTail.AccrualStart)))
            throw new ArgumentException("Purchased coupon separation requires an unreduced lot and exact signed native purchased entitlement.");
        var calibration = CalibrationFlows(instruction, instruction.AcquisitionProjection.CashFlows,
            lot.AcquiredDate, initialClean, lot.OriginalQuantity);
        var yield = BondIncomePeriodMath.Solve(calibration, lot.AcquiredDate, initialClean,
            instruction.AcquisitionAccruedInterest, instruction.AcquisitionProjection, instruction);
        var certificate = BondIncomePeriodMath.Certificate(calibration, lot.AcquiredDate, initialClean,
            instruction.AcquisitionAccruedInterest, instruction.AcquisitionProjection, instruction);
        var currentYield = yield;
        var activeFlows = instruction.AcquisitionProjection.CashFlows;
        var previousDate = lot.AcquiredDate;
        var clean = initialClean;
        var opening = instruction.AcquisitionProjection;
        var openingAccrued = instruction.AcquisitionAccruedInterest;
        var events = instruction.Resets.Select(r => (r.Date, Reset: (BondIncomeResetDto?)r))
            .Concat(instruction.Policy.AmortizeQualifyingCallablePremium
                ? instruction.Calls.Where(c => c.Date > lot.AcquiredDate && c.Date <= instruction.ThroughDate
                    && c.Date < instruction.AcquisitionProjection.CashFlows.Max(f => f.PaymentDate))
                    .Select(c => (c.Date, Reset: (BondIncomeResetDto?)null)) : [])
            .OrderBy(e => e.Date).ThenBy(e => e.Reset is null).ToArray();
        foreach (var item in events)
        {
            if (item.Date < previousDate || item.Date > instruction.ThroughDate
                || item.Reset is not null && instruction.Policy.FloaterPolicy != BondIncomeFloaterPolicy.ProspectiveReset)
                throw new ArgumentException("Reset snapshots require ordered observed prospective-policy reset dates.");
            if (item.Reset is { } reset)
                ValidateProjection(reset.Projection, lot.SecurityId);
            var resetProjection = item.Reset?.Projection ?? opening with
            {
                Context = opening.Context with { SettlementDate = item.Date },
                AccruedInterest = 0m,
                ContractualAccruedInterest = 0m,
                SettlementAccrualFraction = 0m,
                SettlementAccrualPeriodUnits = 0m
            };
            clean = BondIncomePeriodMath.Roll(activeFlows, previousDate, item.Date, clean, currentYield,
                openingAccrued, opening, resetProjection, instruction);
            activeFlows = item.Reset?.Projection.CashFlows ?? activeFlows;
            opening = resetProjection;
            openingAccrued = resetProjection.ContractualAccruedInterest ?? resetProjection.AccruedInterest
                ?? throw new ArgumentException("Observed reset accrual is unavailable.");
            calibration = CalibrationFlows(instruction, activeFlows, item.Date, clean, lot.OriginalQuantity);
            currentYield = BondIncomePeriodMath.Solve(calibration, item.Date, clean, openingAccrued, opening, instruction);
            certificate = BondIncomePeriodMath.Certificate(calibration, item.Date, clean, openingAccrued, opening, instruction);
            previousDate = item.Date;
        }
        clean = BondIncomePeriodMath.Roll(activeFlows, previousDate, instruction.ThroughDate, clean, currentYield,
            openingAccrued, opening, instruction.AmortizationProjection, instruction);
        // Scheduled principal becomes due through its own governed principal workflow. It is not
        // a disposal gain or loss and this amortization mutation must not release that basis again.
        var postedUnderlying = lot.OriginalQuantity * (1m - (lot.CurrentFactor ?? face.BookedFactor) / face.BookedFactor);
        var principal = PrincipalWalk(lot, instruction, postedUnderlying);
        var principalDue = principal.DueCash;
        var postedPrincipal = principal.PostedBaselineCash;
        var baseline = (clean + principalDue - postedPrincipal) * share;
        var remainingIndexation = (lot.OriginalQuantity - principal.UnderlyingDue)
            * (instruction.CurrentIndexRatio - instruction.AcquisitionIndexRatio);
        var carryingIndexation = instruction.Policy.RecognizeObservedIndexation
            ? (remainingIndexation + principal.DueIndexation - principal.PostedIndexation) * share : 0m;
        var indexation = instruction.Policy.RecognizeObservedIndexation
            ? (remainingIndexation + principal.DueIndexation) * share : 0m;
        if (!instruction.Policy.RecognizeObservedIndexation && instruction.CurrentIndexRatio != instruction.AcquisitionIndexRatio)
            throw new ArgumentException("Observed indexation requires an adopted recognition policy.");
        var closing = Round(baseline + carryingIndexation);
        if (closing < 0m)
            throw new ArgumentException("Bond amortized carrying basis cannot be negative.");
        var completedRawCoupon = (instruction.TerminalEntitlement is null
            ? instruction.CurrentProjection.CashFlows.Where(f => f.PaymentDate > lot.AcquiredDate && f.PaymentDate <= instruction.ThroughDate)
            : BondIncomeCouponRounding.EarnedFlows(instruction)).Sum(f => f.Interest) * share;
        var completedCoupon = instruction.CouponCashAllocations is null ? completedRawCoupon
            : BondIncomeCouponRounding.CompletedCash(lot.TaxLotRecordId, instruction);
        var coupon = completedCoupon + ((instruction.TerminalEntitlement is null ? instruction.CurrentAccruedInterest : 0m)
            - instruction.AcquisitionAccruedInterest) * share;
        var amortization = Round(baseline - (originalCost - postedPrincipal) * share);
        var capitalized = instruction.Fees.Where(f => f.Treatment == BondIncomeFeeTreatment.EligibleDirectAcquisitionCost).Sum(f => f.Amount) * share;
        var expense = instruction.Fees.Where(f => f.Treatment == BondIncomeFeeTreatment.Expense).Sum(f => f.Amount) * share;
        var tax = instruction.Fees.Where(f => f.Treatment == BondIncomeFeeTreatment.WithholdingTax).Sum(f => f.Amount) * share;
        var accrued = Round(instruction.CurrentAccruedInterest * share);
        if (instruction.AccountingYieldCertificate is { } retainedCertificate && retainedCertificate != certificate)
            throw new ArgumentException("The retained accounting yield certificate differs from canonical recomputation.");
        var components = new List<BondIncomeJournalComponentDto>();
        AddSigned(components, accrued >= 0m ? "InterestReceivable" : "InterestPayable", accrued, true);
        AddSigned(components, "CouponIncome", -accrued, true);
        var movement = closing - lot.OpenTransactionCostBasis;
        AddSigned(components, "BondCarryingValue", movement, true);
        AddSigned(components, "AmortizationAndIndexationIncome", -movement, true);
        return new(ModelVersion, instruction.Policy.FloaterPolicy, Round(instruction.ZeroCoupon ? currentYield
                : (decimal)Math.Pow((double)(1m + currentYield), instruction.PaymentsPerYear) - 1m),
            lot.OpenTransactionCostBasis, closing, Round(coupon), amortization, Round(indexation), Round(principalDue * share),
            Round(capitalized), Round(expense), Round(tax), Math.Max(accrued, 0m), Math.Max(-accrued, 0m), components)
        {
            CumulativeIncomeFromDate = lot.AcquiredDate,
            AccountingYieldCertificate = certificate,
            CouponCashRoundingAdjustment = Round(completedCoupon - completedRawCoupon),
            BookedTarget = BondIncomeBookedPrecision.Targets(closing, Round(closing * lot.Acquisition.AcquisitionFxRateToFunctional))
        };
    }

    public static OpenLotAmortizationProjectionDto ProjectCanonical(OpenLotAmortizationInstructionDto inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs.ExpectedLot);
        ArgumentNullException.ThrowIfNull(inputs.Security);
        ArgumentNullException.ThrowIfNull(inputs.SecurityEvidence);
        OpenLotValidation.Validate(inputs.ExpectedLot);
        BondIncomeBookedPrecision.RequireRepresentable(inputs.ExpectedLot.OpenTransactionCostBasis);
        BondIncomeBookedPrecision.RequireRepresentable(inputs.ExpectedLot.OpenFunctionalCostBasis);
        if (inputs.BondIncome is not { } income || inputs.YieldToWorst is not null || inputs.AmortizeRemainingFace
            || inputs.Continuation is not null || inputs.ExpectedBookPositionVersion <= 0 || income.AccountId == Guid.Empty)
            throw new ArgumentException("Bond income requires its explicit versioned instruction without legacy policy companions.");
        if (income.CouponCashAllocations is null && income.CurrentProjection.CashFlows.Any(f => f.Interest != 0m
            && f.PaymentDate > income.AcquisitionDate && f.PaymentDate <= income.ThroughDate))
            throw new ArgumentException("Canonical completed coupon income requires authoritative holder-level native cash allocation.");
        var security = inputs.Security;
        if (security.SecurityId != inputs.ExpectedLot.SecurityId || security.Version <= 0
            || security.Status != SecurityStatusDto.Active && !(inputs.Reversal is not null && security.Status == SecurityStatusDto.Inactive)
            || security.Currency != inputs.ExpectedLot.Acquisition.AcquisitionCurrency
            || inputs.AsOfDate != income.ThroughDate
            || !RetainedEvidenceIdentityValidator.IsComplete(inputs.SecurityEvidence)
            || inputs.SecurityEvidence.SubjectId != security.SecurityId.ToString("D")
            || inputs.SecurityEvidence.EvidenceVersion != security.Version
            || inputs.SecurityEvidence.SubjectType != "SecurityMasterProjection"
            || !Sha256Digest.FixedEquals(inputs.SecurityEvidence.ContentHashSha256, OpenLotAmortization.SecurityHash(security)))
            throw new ArgumentException("Bond income requires hash-bound authoritative Security Master and lot identity.");
        if (inputs.Reversal is { } reversal)
        {
            OpenLotValidation.Validate(reversal.RestoresLot);
            var restored = reversal.RestoresLot;
            BondIncomeBookedPrecision.RequireRepresentable(restored.OpenTransactionCostBasis);
            BondIncomeBookedPrecision.RequireRepresentable(restored.OpenFunctionalCostBasis);
            var current = inputs.ExpectedLot;
            if (reversal.MutationBatchId == Guid.Empty || reversal.JournalEntryId == Guid.Empty
                || restored.Version >= current.Version
                || !OpenLotAmortization.SameLot(restored with
                {
                    Version = current.Version,
                    OpenTransactionCostBasis = current.OpenTransactionCostBasis,
                    OpenFunctionalCostBasis = current.OpenFunctionalCostBasis
                }, current))
                throw new ArgumentException("Income reversal must restore the exact immutable original lot basis.");
            return new(restored.OpenTransactionCostBasis, restored.OpenFunctionalCostBasis,
                restored.OpenTransactionCostBasis - current.OpenTransactionCostBasis,
                restored.OpenFunctionalCostBasis - current.OpenFunctionalCostBasis);
        }
        if (security.EffectiveFrom > income.ThroughDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            || security.EffectiveTo is { } ends && ends <= income.ThroughDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            || !ReadQualifyingCalls(security).SequenceEqual(income.Calls))
            throw new ArgumentException("Income terms must be effective and callable-premium dates must match retained fixed-price calls.");
        var result = Project(inputs.ExpectedLot, income);
        var booked = result.BookedTarget!;
        if (income.BookedTarget != booked)
            throw new ArgumentException("Bond income must retain the exact versioned raw/booked target and rounding residual proof.");
        return new(booked.BookedTransactionTarget, booked.BookedFunctionalTarget,
            booked.BookedTransactionTarget - inputs.ExpectedLot.OpenTransactionCostBasis,
            booked.BookedFunctionalTarget - inputs.ExpectedLot.OpenFunctionalCostBasis);
    }

    private static void ValidateProjection(BondWorkflowResult projection, Guid securityId, bool allowEmpty = false)
    {
        if (projection is null || projection.SecurityId != securityId || projection.IsBlocked
            || projection.CashFlows.Count > 1200 || !allowEmpty && projection.CashFlows.Count == 0 || projection.Inputs.Count == 0)
            throw new ArgumentException("Bond income requires complete retained contractual projection inputs.");
    }

    private static (decimal DueCash, decimal UnderlyingDue, decimal PostedBaselineCash,
        decimal DueIndexation, decimal PostedIndexation) PrincipalWalk(OpenLotDto lot, BondIncomeInstructionDto instruction,
        decimal postedUnderlying)
    {
        var prior = lot.OriginalQuantity;
        var due = 0m;
        var underlyingDue = 0m;
        var postedCash = 0m;
        var indexed = 0m;
        var postedIndexed = 0m;
        var remainingPosted = postedUnderlying;
        foreach (var group in instruction.AcquisitionProjection.CashFlows
            .Where(f => f.Principal > 0m && f.PaymentDate > lot.AcquiredDate && f.PaymentDate <= instruction.ThroughDate)
            .GroupBy(f => f.PaymentDate).OrderBy(g => g.Key))
        {
            var cash = group.Sum(f => f.Principal);
            var after = group.Min(f => f.OutstandingPrincipal);
            var released = prior - after;
            if (released <= 0m || after < 0m)
                throw new ArgumentException("Principal schedule requires consistent retained unindexed outstanding face.");
            var actualCash = instruction.CurrentProjection.CashFlows.Where(f => f.PaymentDate == group.Key).Sum(f => f.Principal);
            var indexChange = actualCash - cash;
            var postedShare = Math.Min(remainingPosted, released) / released;
            postedCash += cash * postedShare;
            postedIndexed += indexChange * postedShare;
            remainingPosted -= released * postedShare;
            due += cash;
            underlyingDue += released;
            indexed += indexChange;
            prior = after;
        }
        if (remainingPosted > Quantum)
            throw new ArgumentException("Posted principal reduction lacks matching contractual principal evidence.");
        return (due, underlyingDue, postedCash, indexed, postedIndexed);
    }

    public static IReadOnlyList<BondIncomeCallDto> ReadQualifyingCalls(SecurityProjectionRecord security)
    {
        var calls = new List<BondIncomeCallDto>();
        var field = SecurityAssetTermsSchema.Field(security.AssetClass, SecurityVersionedTerms.CallScheduleKey);
        var decoding = SecurityVersionedTerms.Decoding(security.AssetClass, security.AssetSpecificTerms, writeMode: false);
        if (field is null || !decoding.Decodes(field))
            return calls;
        foreach (var source in StructuredCashFlowTermsResolver.EnumerateTermSources(security.ToDetail()))
        {
            if (!SecurityTermReader.TryGetProperty(source, SecurityVersionedTerms.CallScheduleKey, out var schedule)
                || schedule.ValueKind == JsonValueKind.Null)
                continue;
            if (schedule.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Callable-premium accounting requires an unambiguous retained call schedule.");
            foreach (var row in schedule.EnumerateArray())
            {
                if (SecurityTermReader.ReadString(row, "exerciseStyle") != "European"
                    || SecurityTermReader.ReadString(row, "priceType") != "Fixed"
                    || SecurityTermReader.ReadString(row, "priceBasis") != "PercentOfPar"
                    || SecurityTermReader.ReadString(row, "partialRedemption") is not (null or "WholeOnly")
                    || SecurityTermReader.ReadDecimal(row, "callPrice") is not { } price || price <= 0m
                    || !DateOnly.TryParseExact(SecurityTermReader.ReadString(row, "callDate"), "yyyy-MM-dd", out var date))
                    throw new ArgumentException("Callable-premium accounting supports explicit preset whole fixed-price calls only.");
                calls.Add(new(date, price));
            }
        }
        if (calls.Select(c => c.Date).Distinct().Count() != calls.Count)
            throw new ArgumentException("Callable-premium dates cannot have competing contractual prices.");
        return calls.OrderBy(c => c.Date).ToArray();
    }

    private static IReadOnlyList<BondWorkflowCashFlow> CalibrationFlows(BondIncomeInstructionDto instruction,
        IReadOnlyList<BondWorkflowCashFlow> flows, DateOnly from, decimal carrying, decimal originalFace)
    {
        if (!instruction.Policy.AmortizeQualifyingCallablePremium)
            return flows;
        var next = instruction.Calls.FirstOrDefault(c => c.Date > from);
        if (next is null)
            return flows;
        var atCall = flows.Where(f => f.PaymentDate == next.Date && f.AccrualStart < f.AccrualEnd).ToArray();
        if (atCall.Length == 0 && !instruction.ZeroCoupon)
            throw new ArgumentException("Callable-premium accounting requires a supported coupon-boundary call date.");
        var outstanding = flows.Where(f => f.PaymentDate <= next.Date).GroupBy(f => f.PaymentDate)
            .OrderBy(g => g.Key).LastOrDefault()?.Min(f => f.OutstandingPrincipal) ?? originalFace;
        var ratio = atCall.Length != 0 ? atCall[0].IndexRatio
            : flows.First(f => f.PaymentDate > from).IndexRatio;
        var redemption = outstanding * ratio * next.PricePer100 / 100m;
        if (carrying <= redemption)
            return flows;
        // A call changes the accounting amortization horizon, not actual ownership or cash.
        // Rolling still consumes the contractual schedule; this synthetic redemption is only
        // used in the effective-yield calibration and is never emitted as a payment.
        var prefix = flows.Where(f => f.PaymentDate <= next.Date).ToList();
        prefix.Add(new(next.Date, next.Date, next.Date, 0m, redemption, 0m, 1m, false, []));
        return prefix;
    }

    private static void AddSigned(List<BondIncomeJournalComponentDto> lines, string component, decimal amount, bool positiveDebit)
    {
        if (amount == 0m)
            return;
        var debit = positiveDebit ? Math.Max(amount, 0m) : Math.Max(-amount, 0m);
        var credit = positiveDebit ? Math.Max(-amount, 0m) : Math.Max(amount, 0m);
        lines.Add(new(component, Round(debit), Round(credit)));
    }

    private static decimal Round(decimal value) => decimal.Round(value, 12, MidpointRounding.ToEven);
}
