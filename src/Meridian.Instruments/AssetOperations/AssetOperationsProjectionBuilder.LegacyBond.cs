using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Instruments.AssetOperations;

/// <summary>Version-one bond behavior retained for pre-cutover calculations and explicit historical replay.</summary>
public static partial class AssetOperationsProjectionBuilder
{
    public static AssetOperationsDetailDto FromBondReference(
        BondReferenceDto bond, AssetOperationSubjectDto subject, BondCalculationContext? context = null)
    {
        context ??= new BondCalculationContext(DateTimeOffset.UtcNow);
        var asOfDate = DateOnly.FromDateTime(context.AsOf.UtcDateTime.Date);
        var terms = new[]
        {
            new AssetTermsVersionDto(
                BondIdentity(subject.SecurityId, $"terms:{bond.Version}"),
                subject.SecurityId,
                checked((int)Math.Min(bond.Version, int.MaxValue)),
                $"bond-reference:{bond.Version}",
                bond.Lifecycle?.IssueDate ?? asOfDate,
                context.AsOf,
            "SecurityMaster",
            subject.SecurityId.ToString("D"),
            $"{bond.DisplayName} maturity/coupon reference terms",
            BuildBondTermsPayload(bond))
        };
        IReadOnlyList<AssetLifecycleEventDto> lifecycle = bond.Lifecycle is null
            ? []
            :
            [
                new AssetLifecycleEventDto(
                    BondIdentity(subject.SecurityId, $"lifecycle:{bond.Version}:{asOfDate:yyyy-MM-dd}"),
                    subject.SecurityId,
                    "BondLifecycle",
                    bond.Lifecycle.LifecycleStat.ToString(),
                    bond.Lifecycle.IssueDate ?? asOfDate,
                    context.AsOf,
                    "SecurityMaster",
                    subject.SecurityId.ToString("D"),
                    $"Bond lifecycle is {bond.Lifecycle.LifecycleStat}.")
            ];
        var projectionRunId = BondIdentity(subject.SecurityId, $"run:{bond.Version}:{asOfDate:yyyy-MM-dd}:{context.KnownAt:O}");
        var run = new AssetCashFlowProjectionRunDto(
            projectionRunId,
            subject.SecurityId,
            asOfDate,
            FixedIncomeCashFlowEngineVersion,
            "Completed",
            context.AsOf,
            "SecurityMaster",
            subject.SecurityId.ToString("D"));
        var flows = ProjectFixedIncomeCashFlowsAsOf(bond, projectionRunId, asOfDate).ToArray();
        var ledger = new[]
        {
            new AssetLedgerProjectionDto(
                BondIdentity(subject.SecurityId, $"ledger:{bond.Version}:{asOfDate:yyyy-MM-dd}"),
                subject.SecurityId,
                "FixedIncomeLedgerProjection",
                flows.FirstOrDefault()?.DueDate ?? bond.Lifecycle?.MaturityDate ?? asOfDate,
                "Primary",
                "Ready",
                null,
                null,
                bond.Currency,
                "SecurityMasterAccountingEventService",
                subject.SecurityId.ToString("D"),
                $"security-master:{subject.SecurityId:D}")
        };
        var readyCapabilities = ReadyCapabilities(subject.OperationalProfile, terms, lifecycle, flows, [], [], [], ledger);
        var readiness = BuildReadiness(
            subject,
            readyCapabilities,
            "SecurityMaster",
            subject.SecurityId.ToString("D"),
            additionalWarnings:
            [
                "Bond terms, cash flows, and ledger support are Expected/Projected until reviewed retained evidence is attached and an Asset Operations projection is published."
            ],
            allowReady: false,
            evaluatedAt: context.AsOf);

        var detail = new AssetOperationsDetailDto(
            subject,
            terms,
            lifecycle,
            [run],
            flows,
            [],
            [],
            [],
            ledger,
            readiness,
            lifecycle);
        return WithTermsObligationsTimeline(detail);
    }

    internal static IReadOnlyList<AssetProjectedCashFlowDto> ProjectFixedIncomeCashFlows(
        BondReferenceDto bond,
        Guid projectionRunId)
        => BuildBondProjectedCashFlows(bond, projectionRunId, null).ToArray();

    internal static IReadOnlyList<AssetProjectedCashFlowDto> ProjectFixedIncomeCashFlowsAsOf(
        BondReferenceDto bond,
        Guid projectionRunId,
        DateOnly asOfDate)
        => BuildBondProjectedCashFlows(bond, projectionRunId, asOfDate).ToArray();

    private static IEnumerable<AssetProjectedCashFlowDto> BuildBondProjectedCashFlows(
        BondReferenceDto bond, Guid projectionRunId, DateOnly? asOfDate)
    {
        var maturity = bond.Lifecycle?.MaturityDate;
        if (maturity is null)
        {
            yield break;
        }

        var issueDate = bond.Lifecycle?.IssueDate ?? asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var paymentFrequency = SecurityTermConventions.LegacyCouponPaymentsPerYear(bond.Lifecycle?.PaymentFrequency);
        var periodMonths = Math.Max(1, 12 / paymentFrequency);
        var principalBasis = ResolveBondPrincipalBasis(bond);
        var outstandingPrincipal = principalBasis;
        var dayCountConvention = bond.AccrualConvention?.DayCountConvention;
        var sinkingFundEntries = bond.SinkingFund?.Schedule
            .Where(static entry => entry.Amount > 0m)
            .OrderBy(static entry => entry.SinkDate)
            .ToArray() ?? [];
        var sinkingFundIndex = 0;
        var sequenceNumber = 1;
        var tenor = ScheduleTenor.Months(periodMonths);
        var schedule = ScheduleGenerator.Generate(ScheduleSpec.CompoundedForward(issueDate, maturity.Value, tenor));
        if (!schedule.Succeeded)
        {
            throw new InvalidOperationException(schedule.Failure.Message);
        }

        foreach (var period in schedule.Periods)
        {
            if (period.IsFinal)
            {
                // Each legacy period stepped its start by one tenor before capping the result at
                // maturity, so a maturity whose closing compounded grid date lies past 9999-12-31 (a
                // far-dated or perpetual sentinel off the grid) threw ArgumentOutOfRangeException as the
                // closing period began. The kernel treats that date as an overshoot and closes at
                // maturity; evaluating it here, after the earlier periods' arithmetic, keeps the failure
                // and its order (SCH-DEF-010). Advance throws the same exception type.
                _ = tenor.Advance(period.UnadjustedStart, 1);
            }

            var accrualStart = period.AccrualStart;
            var accrualEnd = period.AccrualEnd;

            if (bond.AccrualConvention?.FixedCouponRate is decimal coupon && coupon > 0m && outstandingPrincipal > 0m)
            {
                var couponAmount = 0m;
                var segmentStart = accrualStart;
                var segmentOutstanding = outstandingPrincipal;
                var segmentSinkIndex = sinkingFundIndex;
                while (segmentSinkIndex < sinkingFundEntries.Length &&
                       sinkingFundEntries[segmentSinkIndex].SinkDate < accrualEnd &&
                       segmentOutstanding > 0m)
                {
                    var sink = sinkingFundEntries[segmentSinkIndex++];
                    var sinkAmount = RoundCash(sink.Amount);
                    if (sink.SinkDate <= segmentStart)
                    {
                        segmentOutstanding = RoundCash(
                            decimal.Max(0m, segmentOutstanding - sinkAmount));
                        continue;
                    }

                    couponAmount += CalculateBondCouponSegmentAmount(
                        segmentOutstanding,
                        coupon,
                        dayCountConvention,
                        segmentStart,
                        sink.SinkDate,
                        paymentFrequency,
                        accrualStart,
                        accrualEnd);
                    segmentOutstanding = RoundCash(
                        decimal.Max(0m, segmentOutstanding - sinkAmount));
                    segmentStart = sink.SinkDate;
                }

                couponAmount = RoundCash(couponAmount + CalculateBondCouponSegmentAmount(
                    segmentOutstanding,
                    coupon,
                    dayCountConvention,
                    segmentStart,
                    accrualEnd,
                    paymentFrequency,
                    accrualStart,
                    accrualEnd));

                if (couponAmount > 0m)
                {
                    yield return new AssetProjectedCashFlowDto(
                        SchedulePeriodId.Derive(bond.SecurityId, "Coupon", period),
                        projectionRunId,
                        bond.SecurityId,
                        sequenceNumber++,
                        "Coupon",
                        accrualEnd,
                        couponAmount,
                        bond.Currency,
                        "Projected",
                        accrualStart,
                        accrualEnd,
                        outstandingPrincipal,
                        decimal.Round(coupon / 100m, 8, MidpointRounding.AwayFromZero),
                        "SecurityMaster",
                        bond.SecurityId.ToString("D"),
                        BuildBondFlowPayload(
                            "fixed-coupon",
                            bond,
                            paymentFrequency,
                            principalBasis,
                            outstandingPrincipal,
                            coupon,
                            accrualStart,
                            accrualEnd));
                }
            }

            while (sinkingFundIndex < sinkingFundEntries.Length &&
                   sinkingFundEntries[sinkingFundIndex].SinkDate <= accrualEnd &&
                   sinkingFundEntries[sinkingFundIndex].SinkDate < maturity.Value &&
                   outstandingPrincipal > 0m)
            {
                var entry = sinkingFundEntries[sinkingFundIndex++];
                var principalRepayment = decimal.Min(outstandingPrincipal, RoundCash(entry.Amount));
                if (principalRepayment <= 0m)
                {
                    continue;
                }

                outstandingPrincipal = RoundCash(outstandingPrincipal - principalRepayment);
                yield return new AssetProjectedCashFlowDto(
                    SchedulePeriodId.Derive(bond.SecurityId, "PrincipalRepayment", entry.SinkDate, entry.SinkDate),
                    projectionRunId,
                    bond.SecurityId,
                    sequenceNumber++,
                    "PrincipalRepayment",
                    entry.SinkDate,
                    principalRepayment,
                    bond.Currency,
                    "Projected",
                    null,
                    entry.SinkDate,
                    RoundCash(outstandingPrincipal + principalRepayment),
                    null,
                    "SecurityMaster",
                    bond.SecurityId.ToString("D"),
                    BuildBondFlowPayload(
                        "sinking-fund-principal",
                        bond,
                        paymentFrequency,
                        principalBasis,
                        RoundCash(outstandingPrincipal + principalRepayment),
                        null,
                        null,
                        entry.SinkDate));
            }
        }

        if (outstandingPrincipal > 0m)
        {
            yield return new AssetProjectedCashFlowDto(
                SchedulePeriodId.Derive(bond.SecurityId, "Maturity", maturity.Value, maturity.Value),
                projectionRunId,
                bond.SecurityId,
                sequenceNumber,
                "Maturity",
                maturity.Value,
                RoundCash(outstandingPrincipal),
                bond.Currency,
                "Projected",
                null,
                maturity,
                RoundCash(outstandingPrincipal),
                null,
                "SecurityMaster",
                bond.SecurityId.ToString("D"),
                BuildBondFlowPayload(
                    "maturity-principal",
                    bond,
                    paymentFrequency,
                    principalBasis,
                    outstandingPrincipal,
                    null,
                    null,
                    maturity.Value));
        }
    }

    private static decimal ResolveBondPrincipalBasis(BondReferenceDto bond)
    {
        var par = bond.Lifecycle?.Par is decimal parValue && parValue > 0m
            ? parValue
            : DefaultPrincipalBasis;
        // Zero is a REAL factor — a fully amortized pool — and must not be conflated with a
        // missing one: substituting 1 would project full-face principal (and draftable ledger
        // support) for a security with nothing outstanding. Only an absent or negative factor
        // falls back to 1.
        var factor = (bond.PrincipalFactor?.CurrentFactor ?? bond.InflationLinked?.CurrentFactor) is decimal currentFactor && currentFactor >= 0m
            ? currentFactor
            : 1m;
        return RoundCash(par * factor);
    }

    private static decimal CalculateBondCouponSegmentAmount(
        decimal principalBasis,
        decimal couponRatePercent,
        string? dayCountConvention,
        DateOnly segmentStart,
        DateOnly segmentEnd,
        int paymentFrequency,
        DateOnly couponPeriodStart,
        DateOnly couponPeriodEnd)
    {
        var convention = DayCountConventions.Parse(dayCountConvention);
        // Preserve the historical one-period fallback for an absent convention, while apportioning that
        // period across principal events so multiple segments cannot each receive a full coupon.
        var couponPeriodDays = Math.Max(1, couponPeriodEnd.DayNumber - couponPeriodStart.DayNumber);
        var yearFraction = convention == DayCountConvention.Unknown
            ? (decimal)(segmentEnd.DayNumber - segmentStart.DayNumber) /
              couponPeriodDays /
              Math.Max(1, paymentFrequency)
            : DayCountConventions.Fraction(convention, segmentStart, segmentEnd);
        return principalBasis * (couponRatePercent / 100m) * yearFraction;
    }

    private static JsonElement BuildBondFlowPayload(
        string formula,
        BondReferenceDto bond,
        int paymentFrequency,
        decimal principalBasis,
        decimal outstandingPrincipal,
        decimal? couponRatePercent,
        DateOnly? accrualStart,
        DateOnly accrualEnd)
        => JsonSerializer.SerializeToElement(new
        {
            formula,
            engineVersion = FixedIncomeCashFlowEngineVersion,
            dayCountConvention = bond.AccrualConvention?.DayCountConvention,
            paymentFrequencyPerYear = paymentFrequency,
            principalBasis,
            outstandingPrincipal,
            couponRatePercent,
            accrualStart,
            accrualEnd,
            principalFactor = bond.PrincipalFactor?.CurrentFactor,
            inflationFactor = bond.InflationLinked?.CurrentFactor
        });

    private static JsonElement BuildBondTermsPayload(BondReferenceDto bond)
        => JsonSerializer.SerializeToElement(new
        {
            engineVersion = AssetObligationProjectionService.EngineVersion,
            assetClass = "Bond",
            currency = bond.Currency,
            bond.DisplayName,
            bond.IssuerName,
            bond.PrimaryIdentifier,
            issueDate = bond.Lifecycle?.IssueDate,
            callDate = bond.Lifecycle?.CallDate,
            maturityDate = bond.Lifecycle?.MaturityDate,
            legalFinalMaturity = bond.Lifecycle?.LegalFinalMaturity,
            preRefundDate = bond.Lifecycle?.PreRefundDate,
            mandatoryPutDate = bond.Lifecycle?.MandatoryPutDate,
            par = bond.Lifecycle?.Par,
            paymentFrequency = bond.Lifecycle?.PaymentFrequency,
            dayCountConvention = bond.AccrualConvention?.DayCountConvention,
            couponKind = bond.AccrualConvention?.CouponKind,
            fixedCouponRate = bond.AccrualConvention?.FixedCouponRate,
            floatingRateIndex = bond.AccrualConvention?.FloatingRateIndex,
            floatingSpreadBps = bond.AccrualConvention?.FloatingSpreadBps,
            sinkingFundSchedule = bond.SinkingFund?.Schedule,
            factorSchedule = bond.SinkingFund is null ? null : "sinking-fund",
            inflationIndex = bond.InflationLinked?.InflationIndex,
            currentFactor = bond.PrincipalFactor?.CurrentFactor ?? bond.InflationLinked?.CurrentFactor,
            factorDate = bond.PrincipalFactor?.FactorDate ?? bond.InflationLinked?.FactorDate,
            principalFactor = bond.PrincipalFactor,
            inflationIndexRatio = bond.InflationLinked?.CurrentFactor,
            scheduleConventions = bond.ScheduleConventions,
            callSchedule = bond.CallSchedule,
            putSchedule = bond.PutSchedule
        });

}
