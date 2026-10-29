using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Instruments.AssetOperations;

/// <summary>
/// Builds asset obligation projections from canonical instrument and lifecycle evidence.
/// </summary>
public sealed partial class AssetObligationProjectionService
{
    public const string EngineVersion = "asset-obligation-projection-v1";

    private const string SecurityMasterSourceDomain = "SecurityMaster";
    private readonly IBondCalculationPolicy? _bondPolicy;

    public AssetObligationProjectionService(IBondCalculationPolicy? bondPolicy = null)
        => _bondPolicy = bondPolicy;

    public AssetOperationsDetailDto ProjectFromSecurityMaster(SecurityDetailDto security)
        => ProjectFromSecurityMaster(security, new BondCalculationContext(DateTimeOffset.UtcNow));

    /// <summary>Maps a canonical calculation without repeating coupon or principal arithmetic.</summary>
    public AssetOperationsDetailDto ProjectFromSecurityMaster(
        SecurityDetailDto security, BondCalculationContext context,
        StructuredCashFlowProjectionDto? calculatedProjection = null)
        => ProjectCore(security, context, _bondPolicy is null
            || !SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).CarriesBondCouponStructure ? calculatedProjection
            : BlockedProjection(security, context, "Live bond policy requires the asynchronous obligation projection entry point."));

    /// <summary>Evaluates live cutover policy before exposing projected obligation economics.</summary>
    public async Task<AssetOperationsDetailDto> ProjectFromSecurityMasterAsync(
        SecurityDetailDto security, BondCalculationContext context,
        StructuredCashFlowProjectionDto? calculatedProjection = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();
        var blocker = _bondPolicy is null ? null : await _bondPolicy.GetBlockerAsync(security, ct).ConfigureAwait(false);
        return ProjectCore(security, context, blocker is null ? calculatedProjection : BlockedProjection(security, context, blocker));
    }

    private static StructuredCashFlowProjectionDto BlockedProjection(
        SecurityDetailDto security, BondCalculationContext context, string reason)
        => new(security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
            StructuredCashFlowScenario.Base, context.AsOf, [], BlockedReason: reason);

    private static AssetOperationsDetailDto ProjectCore(
        SecurityDetailDto security, BondCalculationContext context,
        StructuredCashFlowProjectionDto? calculatedProjection)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(context);

        var generatedAt = context.AsOf;
        var projectionAsOf = DateOnly.FromDateTime(generatedAt.UtcDateTime.Date);
        var subject = BuildSubject(security);
        var projectionRunId = AssetOperationsProjectionBuilder.BondIdentity(
            security.SecurityId,
            $"run:{security.Version}:{projectionAsOf:yyyy-MM-dd}:{context.KnownAt:O}:{AssetOperationsProjectionBuilder.BondProjectionInputKey(calculatedProjection)}");
        var terms = new[]
        {
            new AssetTermsVersionDto(
                AssetOperationsProjectionBuilder.BondIdentity(security.SecurityId, $"terms:{security.Version}"),
                security.SecurityId,
                checked((int)Math.Min(security.Version, int.MaxValue)),
                $"security-master:{security.Version}",
                DateOnly.FromDateTime(security.EffectiveFrom.UtcDateTime.Date),
                generatedAt,
                SecurityMasterSourceDomain,
                security.SecurityId.ToString("D"),
                $"{security.DisplayName} Security Master terms projected for Asset Operations review",
                BuildSecurityMasterTermsPayload(security))
        };
        var lifecycle = new[]
        {
            new AssetLifecycleEventDto(
                AssetOperationsProjectionBuilder.BondIdentity(security.SecurityId, $"status:{security.Version}"),
                security.SecurityId,
                "SecurityMasterStatus",
                security.Status.ToString(),
                DateOnly.FromDateTime(security.EffectiveFrom.UtcDateTime.Date),
                generatedAt,
                SecurityMasterSourceDomain,
                security.SecurityId.ToString("D"),
                $"Security Master subject is {security.Status}.",
                JsonSerializer.SerializeToElement(new
                {
                    engineVersion = EngineVersion,
                    security.Version,
                    security.EffectiveFrom,
                    security.EffectiveTo
                }))
        };
        var declaresConventions = SecurityVersionedTerms.ReadDeclaredScheduleConventions(
            security.AssetClass, security.AssetSpecificTerms).Status != SecurityDeclaredConventionsStatus.None;
        var blockedReason = calculatedProjection?.BlockedReason
            ?? (declaresConventions && calculatedProjection is null
                ? "Declared bond economics require the shared cash-flow calculation; legacy fallback is disabled."
                : null);
        var run = new AssetCashFlowProjectionRunDto(
            projectionRunId,
            security.SecurityId,
            projectionAsOf,
            calculatedProjection?.ScheduleEvidence is null ? EngineVersion : "asset-obligation-declared-v2",
            blockedReason is null ? "Completed" : "Blocked",
            generatedAt,
            SecurityMasterSourceDomain,
            security.SecurityId.ToString("D"));
        IReadOnlyList<AssetProjectedCashFlowDto> flows = blockedReason is not null ? []
            : calculatedProjection is not null
                ? AssetOperationsProjectionBuilder.MapCanonicalBondCashFlows(security, calculatedProjection, projectionRunId)
                : BuildProjectedCashFlowsFromSecurityTerms(security, projectionRunId, projectionAsOf).ToArray();
        var ledger = BuildLedgerSupport(security, projectionAsOf, flows).ToArray();
        var readyCapabilities = AssetOperationsProjectionBuilder.ReadyCapabilities(
            subject.OperationalProfile,
            terms,
            lifecycle,
            flows,
            [],
            [],
            [],
            ledger);
        var readiness = AssetOperationsProjectionBuilder.BuildReadiness(
            subject,
            readyCapabilities,
            SecurityMasterSourceDomain,
            security.SecurityId.ToString("D"),
            additionalWarnings:
            [
                "Only Expected/Projected support was synthesized from Security Master. Publish a reviewed Asset Operations projection and attach accepted retained evidence before downstream use.",
                .. (blockedReason is null ? Array.Empty<string>() : new[] { blockedReason })
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
        return AssetOperationsProjectionBuilder.WithTermsObligationsTimeline(detail);
    }

    public static IReadOnlyList<AssetTermsObligationTimelineEventDto> BuildProjectedObligationEvents(
        AssetOperationSubjectDto subject,
        IReadOnlyList<AssetTermsVersionDto> terms,
        DateOnly projectionAsOf,
        IReadOnlyList<AssetLedgerProjectionDto> ledgerProjections)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(ledgerProjections);

        var term = terms
            .OrderByDescending(static row => row.EffectiveDate)
            .ThenByDescending(static row => row.RecordedAt)
            .FirstOrDefault();
        if (term?.ExtensionPayload is not { } payload || payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return [];
        }

        var events = new List<AssetTermsObligationTimelineEventDto>();
        var currency = TryReadString(payload, out var currencyValue, "currency", "baseCurrency")
            ? currencyValue!
            : string.Empty;
        var ledgerReference = ledgerProjections
            .OrderBy(static row => row.AccountingDate)
            .FirstOrDefault()?.LedgerReferenceId;

        if (IsFixedIncome(subject.AssetClass))
        {
            AddFixedIncomeObligations(events, subject, term, payload, projectionAsOf, currency, ledgerReference);
        }

        if (IsDirectLoan(subject.AssetClass))
        {
            AddDirectLoanObligations(events, subject, term, payload, projectionAsOf, currency, ledgerReference);
        }

        if (IsPrivateFund(subject.AssetClass))
        {
            AddPrivateFundObligations(events, subject, term, payload, projectionAsOf, currency, ledgerReference);
        }

        if (IsStructuredOrCustom(subject.AssetClass))
        {
            AddStructuredOrCustomObligations(events, subject, term, payload, projectionAsOf, currency, ledgerReference);
        }

        return events
            .OrderBy(static row => row.EffectiveDate)
            .ThenBy(static row => row.EventLane)
            .ThenBy(static row => row.EventKind)
            .ToArray();
    }

    public static IReadOnlyList<AssetTimelineVarianceDto> BuildTimelineVariances(
        AssetOperationSubjectDto subject,
        IReadOnlyList<AssetProjectedCashFlowDto> projectedCashFlows,
        IReadOnlyList<AssetActualActivityDto> actualActivity,
        IReadOnlyList<AssetReconciliationResultDto> reconciliationResults,
        DateOnly projectionAsOf)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(projectedCashFlows);
        ArgumentNullException.ThrowIfNull(actualActivity);
        ArgumentNullException.ThrowIfNull(reconciliationResults);

        var variances = new List<AssetTimelineVarianceDto>();
        foreach (var result in reconciliationResults.OrderBy(static row => row.ExpectedDate ?? row.ActualDate))
        {
            var expectedDate = result.ExpectedDate ?? result.ActualDate ?? projectionAsOf;
            var dateMismatch = result.ExpectedDate.HasValue
                && result.ActualDate.HasValue
                && result.ExpectedDate.Value != result.ActualDate.Value;
            var amountMismatch = result.VarianceAmount.HasValue && result.VarianceAmount.Value != 0m;
            var reviewStatus = !string.Equals(result.MatchStatus, "Matched", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(result.MatchStatus, "Reconciled", StringComparison.OrdinalIgnoreCase);

            if (!dateMismatch && !amountMismatch && !reviewStatus)
            {
                continue;
            }

            variances.Add(new AssetTimelineVarianceDto(
                AssetOperationsProjectionBuilder.BondIdentity(subject.SecurityId, $"variance:{result.ReconciliationResultId:N}"),
                subject.SecurityId,
                "CashFlowVariance",
                expectedDate,
                result.ActualDate,
                result.ExpectedAmount,
                result.ActualAmount,
                result.VarianceAmount,
                string.Empty,
                result.MatchStatus,
                result.SourceDomain,
                result.SourceEntityId,
                BuildVarianceSummary(result),
                result.EvidenceLink)
            {
                RetainedEvidence = result.RetainedEvidence
            });
        }

        foreach (var flow in projectedCashFlows.Where(flow => flow.DueDate < projectionAsOf))
        {
            var hasReconciliation = reconciliationResults.Any(result =>
                result.ExpectedDate == flow.DueDate ||
                (result.ExpectedAmount.HasValue && RoundCash(result.ExpectedAmount.Value) == RoundCash(flow.Amount)));
            if (hasReconciliation)
            {
                continue;
            }

            var hasActual = actualActivity.Any(activity =>
                activity.EffectiveDate == flow.DueDate &&
                RoundCash(activity.Amount) == RoundCash(flow.Amount));
            if (hasActual)
            {
                continue;
            }

            variances.Add(new AssetTimelineVarianceDto(
                AssetOperationsProjectionBuilder.BondIdentity(subject.SecurityId, $"missing:{flow.ProjectedCashFlowId:N}"),
                subject.SecurityId,
                NormalizeVarianceEventKind(flow.FlowType),
                flow.DueDate,
                null,
                flow.Amount,
                null,
                null,
                flow.Currency,
                "MissingEvidence",
                flow.SourceDomain ?? "AssetOperations",
                flow.SourceEntityId,
                $"{flow.FlowType} expected on {flow.DueDate:yyyy-MM-dd} has no retained actual activity or reconciliation evidence.")
            {
                RetainedEvidence = flow.RetainedEvidence
            });
        }

        return variances
            .OrderBy(static row => row.ExpectedDate)
            .ThenBy(static row => row.EventKind)
            .ToArray();
    }

    private static AssetOperationSubjectDto BuildSubject(SecurityDetailDto security)
        => new(
            security.SecurityId,
            security.AssetClass,
            security.DisplayName,
            ResolvePrimaryIdentifier(security.Identifiers),
            SecurityAssetClassCatalog.GetAssetOperationsCapabilities(security.AssetClass));

    private static IEnumerable<AssetProjectedCashFlowDto> BuildProjectedCashFlowsFromSecurityTerms(
        SecurityDetailDto security,
        Guid projectionRunId,
        DateOnly projectionAsOf)
    {
        if (!IsFixedIncome(security.AssetClass))
        {
            yield break;
        }

        var reference = BuildFixedIncomeReference(security, projectionAsOf);
        if (reference is null)
        {
            yield break;
        }

        foreach (var flow in AssetOperationsProjectionBuilder.ProjectFixedIncomeCashFlowsAsOf(reference, projectionRunId, projectionAsOf))
        {
            yield return flow;
        }
    }

    private static IEnumerable<AssetLedgerProjectionDto> BuildLedgerSupport(
        SecurityDetailDto security,
        DateOnly projectionAsOf,
        IReadOnlyList<AssetProjectedCashFlowDto> flows)
    {
        if (!SecurityAssetClassCatalog
            .GetAssetOperationsCapabilities(security.AssetClass)
            .Contains("LedgerProjection", StringComparer.OrdinalIgnoreCase))
        {
            yield break;
        }

        yield return new AssetLedgerProjectionDto(
            AssetOperationsProjectionBuilder.BondIdentity(security.SecurityId, $"ledger:{security.Version}:{projectionAsOf:yyyy-MM-dd}"),
            security.SecurityId,
            "AssetObligationLedgerSupport",
            flows.OrderBy(static flow => flow.DueDate).FirstOrDefault()?.DueDate ?? projectionAsOf,
            "Primary",
            flows.Count == 0 ? "ManualReview" : "Draftable",
            null,
            null,
            security.Currency,
            EngineVersion,
            security.SecurityId.ToString("D"),
            $"security-master:{security.SecurityId:D}");
    }

    private static void AddFixedIncomeObligations(
        List<AssetTermsObligationTimelineEventDto> events,
        AssetOperationSubjectDto subject,
        AssetTermsVersionDto term,
        JsonElement payload,
        DateOnly projectionAsOf,
        string currency,
        string? ledgerReference)
    {
        if (TryReadDate(payload, out var callDate, "callDate", "mandatoryPutDate", "preRefundDate") && callDate >= projectionAsOf)
        {
            events.Add(BuildObligationEvent(
                subject,
                term,
                projectionAsOf,
                "CallPutReview",
                "Obligations",
                callDate,
                currency,
                "Review callable, put, or pre-refund terms before the contractual option date.",
                "Security Master call/put date retained in economic terms.",
                ledgerReference,
                "Review option exercise and downstream ledger treatment."));
        }

        if (IsStructuredOrCustom(subject.AssetClass) ||
            TryReadDecimal(payload, out _, "currentFactor", "factor") ||
            TryReadString(payload, out _, "factorSchedule", "factorSource"))
        {
            events.Add(BuildObligationEvent(
                subject,
                term,
                projectionAsOf,
                "FactorUpdate",
                "Obligations",
                NextMonthlyDate(projectionAsOf),
                currency,
                "Retain factor update evidence before principal/paydown support is treated as current.",
                "Factor update cadence inferred from retained structured/factor terms.",
                ledgerReference,
                "Attach provider, trustee, or client-approved factor evidence."));
        }

        events.Add(BuildObligationEvent(
            subject,
            term,
            projectionAsOf,
            "CorporateActionCheckpoint",
            "Evidence",
            NextMonthlyDate(projectionAsOf),
            currency,
            "Check provider corporate-action evidence before close and posting support.",
            "Monthly corporate-action checkpoint generated for fixed-income schedule support.",
            ledgerReference,
            "Review corporate-action and lifecycle evidence."));
    }

    private static void AddDirectLoanObligations(
        List<AssetTermsObligationTimelineEventDto> events,
        AssetOperationSubjectDto subject,
        AssetTermsVersionDto term,
        JsonElement payload,
        DateOnly projectionAsOf,
        string currency,
        string? ledgerReference)
    {
        var nextPayment = NextFrequencyDate(
            projectionAsOf,
            TryReadString(payload, out var frequency, "paymentFrequency") ? frequency : null);
        if (TryReadString(payload, out _, "covenantsJson", "covenantSchedule", "covenants"))
        {
            events.Add(BuildObligationEvent(
                subject,
                term,
                projectionAsOf,
                "CovenantTest",
                "Obligations",
                NextQuarterDate(projectionAsOf),
                currency,
                "Run covenant test and retain borrower support before treating loan support as current.",
                "Covenant obligation inferred from retained direct-lending covenant terms.",
                ledgerReference,
                "Collect borrower covenant package and record test result."));
        }

        if (TryReadString(payload, out var rateType, "rateTypeKind", "rateType") &&
            string.Equals(rateType, "Floating", StringComparison.OrdinalIgnoreCase))
        {
            events.Add(BuildObligationEvent(
                subject,
                term,
                projectionAsOf,
                "RateReset",
                "Obligations",
                nextPayment,
                currency,
                "Reset floating-rate terms from retained index evidence.",
                "Rate reset generated from floating-rate direct-lending terms.",
                ledgerReference,
                "Attach index observation and approve the reset."));
        }

        if (TryReadDecimal(payload, out var commitmentFeeRate, "commitmentFeeRate") && commitmentFeeRate > 0m)
        {
            events.Add(BuildObligationEvent(
                subject,
                term,
                projectionAsOf,
                "CommitmentFee",
                "CashFlow",
                nextPayment,
                currency,
                "Project commitment-fee collection/review from retained unfunded commitment terms.",
                $"commitment fee = unfunded commitment x {commitmentFeeRate:P4} x day-count fraction",
                ledgerReference,
                "Review fee accrual and cash evidence."));
        }

        if (TryReadDate(payload, out var maturity, "maturityDate") && maturity > projectionAsOf)
        {
            events.Add(BuildObligationEvent(
                subject,
                term,
                projectionAsOf,
                "BorrowerNotice",
                "Obligations",
                maturity.AddDays(-10),
                currency,
                "Send or retain borrower maturity notice before the final principal date.",
                "Notice date generated ten days before retained maturity.",
                ledgerReference,
                "Retain borrower notice evidence."));
        }
    }

    private static void AddPrivateFundObligations(
        List<AssetTermsObligationTimelineEventDto> events,
        AssetOperationSubjectDto subject,
        AssetTermsVersionDto term,
        JsonElement payload,
        DateOnly projectionAsOf,
        string currency,
        string? ledgerReference)
    {
        var navDate = TryReadDate(payload, out var retainedNavDate, "navDate", "valuationDate")
            ? retainedNavDate
            : projectionAsOf;
        var nextStatementDate = AdvanceAfter(navDate, projectionAsOf, 3);
        _ = TryReadDecimal(payload, out var unfundedCommitment, "unfundedAmount", "unfundedCommitment");

        events.Add(BuildObligationEvent(
            subject,
            term,
            projectionAsOf,
            "NavStatement",
            "Evidence",
            nextStatementDate,
            currency,
            "Expected NAV statement from administrator or GP.",
            "Quarterly NAV statement cadence inferred from retained NAV date.",
            ledgerReference,
            "Attach administrator or GP NAV statement."));
        events.Add(BuildObligationEvent(
            subject,
            term,
            projectionAsOf,
            "CapitalAccountStatement",
            "Evidence",
            nextStatementDate,
            currency,
            "Expected capital-account statement support.",
            "Quarterly capital-account statement cadence inferred from retained private-fund terms.",
            ledgerReference,
            "Attach capital-account statement and reconcile activity."));
        events.Add(BuildObligationEvent(
            subject,
            term,
            projectionAsOf,
            "UnfundedCommitmentReview",
            "Obligations",
            NextQuarterDate(projectionAsOf),
            currency,
            "Review unfunded commitment exposure and related close support.",
            "Unfunded commitment review generated from retained commitment terms.",
            ledgerReference,
            "Confirm unfunded commitment and update close blockers.",
            expectedAmount: unfundedCommitment));
        events.Add(BuildObligationEvent(
            subject,
            term,
            projectionAsOf,
            "CapitalCallEvidenceSlot",
            "Evidence",
            projectionAsOf.AddDays(30),
            currency,
            "Reserve evidence slot for capital-call notices.",
            "Capital-call evidence slot generated because amount/date may be unknown until GP notice arrives.",
            ledgerReference,
            "Attach GP notice when received."));
        events.Add(BuildObligationEvent(
            subject,
            term,
            projectionAsOf,
            "DistributionEvidenceSlot",
            "Evidence",
            projectionAsOf.AddDays(30),
            currency,
            "Reserve evidence slot for distribution notices.",
            "Distribution evidence slot generated because amount/date may be unknown until GP notice arrives.",
            ledgerReference,
            "Attach distribution notice when received."));
    }

    private static void AddStructuredOrCustomObligations(
        List<AssetTermsObligationTimelineEventDto> events,
        AssetOperationSubjectDto subject,
        AssetTermsVersionDto term,
        JsonElement payload,
        DateOnly projectionAsOf,
        string currency,
        string? ledgerReference)
    {
        if (IsFactorAmortizing(subject.AssetClass))
        {
            events.Add(BuildObligationEvent(
                subject,
                term,
                projectionAsOf,
                "TrusteeReport",
                "Evidence",
                NextMonthlyDate(projectionAsOf),
                currency,
                "Expected trustee or servicer report for structured-credit support.",
                "Monthly trustee-report cadence generated for structured-credit terms.",
                ledgerReference,
                "Attach trustee or servicer report."));
            events.Add(BuildObligationEvent(
                subject,
                term,
                projectionAsOf,
                "CollateralTape",
                "Evidence",
                NextMonthlyDate(projectionAsOf),
                currency,
                "Expected collateral tape before factor and valuation support is current.",
                "Monthly collateral-tape cadence generated for structured-credit terms.",
                ledgerReference,
                "Attach collateral tape and resolve validation issues."));
            events.Add(BuildObligationEvent(
                subject,
                term,
                projectionAsOf,
                "FactorUpdate",
                "Obligations",
                NextMonthlyDate(projectionAsOf),
                currency,
                "Expected factor update for paydown, valuation, and ledger support.",
                "Factor update generated from structured-credit factor terms.",
                ledgerReference,
                "Retain current factor evidence."));
        }

        events.Add(BuildObligationEvent(
            subject,
            term,
            projectionAsOf,
            "ValuationReview",
            "Obligations",
            NextQuarterDate(projectionAsOf),
            currency,
            "Review valuation support before treating downstream close support as ready.",
            "Quarterly valuation review generated from retained asset terms.",
            ledgerReference,
            "Attach valuation support or record review blocker."));

        if (SecurityAssetClassCatalog.GetOrDefault(subject.AssetClass).RequiresGovernedProfile ||
            TryReadString(payload, out _, "customProfileId"))
        {
            events.Add(BuildObligationEvent(
                subject,
                term,
                projectionAsOf,
                "CustomProfileEvidenceReview",
                "Obligations",
                NextQuarterDate(projectionAsOf),
                currency,
                "Review custom-profile evidence and approved field lineage.",
                "Custom profile review generated from retained profile-backed terms.",
                ledgerReference,
                "Confirm profile evidence and close blockers."));
            events.Add(BuildObligationEvent(
                subject,
                term,
                projectionAsOf,
                "ObligationCloseEvidence",
                "Handoff",
                NextQuarterDate(projectionAsOf),
                currency,
                "Confirm close evidence before downstream ledger or reporting support consumes the asset.",
                "Close-evidence handoff generated from governed custom asset terms.",
                ledgerReference,
                "Resolve close blockers or mark manual review."));
        }
    }

    private static AssetTermsObligationTimelineEventDto BuildObligationEvent(
        AssetOperationSubjectDto subject,
        AssetTermsVersionDto term,
        DateOnly projectionAsOf,
        string eventKind,
        string lane,
        DateOnly dueDate,
        string currency,
        string summary,
        string formulaTrace,
        string? ledgerReference,
        string nextAction,
        decimal? expectedAmount = null)
    {
        var status = dueDate < projectionAsOf
            ? "MissingEvidence"
            : "Projected";
        return new AssetTermsObligationTimelineEventDto(
            AssetOperationsProjectionBuilder.BondIdentity(subject.SecurityId, $"obligation:{term.TermsVersionId:N}:{eventKind}:{dueDate:yyyy-MM-dd}"),
            subject.SecurityId,
            eventKind,
            lane,
            dueDate,
            null,
            null,
            null,
            expectedAmount,
            null,
            null,
            currency,
            status,
            term.SourceDomain,
            term.SourceEntityId,
            null,
            ledgerReference,
            summary,
            JsonSerializer.SerializeToElement(new
            {
                engineVersion = EngineVersion,
                termsVersionId = term.TermsVersionId,
                term.VersionNumber,
                obligationKind = eventKind
            }))
        {
            FormulaTrace = formulaTrace,
            LedgerReference = ledgerReference,
            NextAction = status == "MissingEvidence" ? $"Past due: {nextAction}" : nextAction,
            RetainedEvidence = term.RetainedEvidence
        };
    }

    // The wrapped asset-specific terms are read at the class's current terms version (ADR-024), once,
    // with the record's own asset class, so both term walks below and every TryReadX see one payload.
    private static JsonElement BuildSecurityMasterTermsPayload(SecurityDetailDto security)
        => JsonSerializer.SerializeToElement(new
        {
            engineVersion = EngineVersion,
            security.AssetClass,
            security.Currency,
            security.Version,
            security.EffectiveFrom,
            security.EffectiveTo,
            security.CommonTerms,
            AssetSpecificTerms = SecurityAssetClassTermsUpgrader.UpgradeForRead(security.AssetClass, security.AssetSpecificTerms)
        });

    // Each branch reads a catalog fact instead of naming classes, so a class the catalog declares
    // as (say) contractual-schedule fixed income receives fixed-income obligations without this
    // file learning its name. Deposit was the first casualty of the old list: ContractualSchedule
    // in the catalog, absent here. Direct loans share that cash-flow behaviour but have their own
    // obligations and projector, so they are carved out by instrument type rather than by name.
    private static bool IsFixedIncome(string? assetClass)
        => IsFixedIncome(SecurityAssetClassCatalog.GetOrDefault(assetClass));

    private static bool IsFixedIncome(SecurityAssetClassDescriptor descriptor)
        => descriptor.CashFlowBehavior is SecurityCashFlowBehavior.ContractualSchedule or SecurityCashFlowBehavior.FactorAmortizing
           && !IsDirectLoan(descriptor);

    /// <summary>
    /// The bond projector's accrual convention carries the coupon in percentage points (4.5 for
    /// 4.5%). Deposit terms declare <c>interestRate</c> without a unit convention and are written
    /// both ways (0.045 and 4.5), so they are normalised the way the Security Master cash-flow
    /// service already does: a value at or below 1 is a fraction. Bond-family classes keep their
    /// percentage-point convention untouched (a 0.5 coupon is half a percent).
    /// </summary>
    private static decimal? NormalizeCouponRatePercent(string? assetClass, decimal? couponRate)
    {
        if (couponRate is not { } rate)
        {
            return null;
        }

        return SecurityAssetClassCatalog.GetOrDefault(assetClass).QuotesRatesAsFractions
               && rate <= 1m
            ? rate * 100m
            : rate;
    }

    private static bool IsFactorAmortizing(string? assetClass)
        => SecurityAssetClassCatalog.GetOrDefault(assetClass).CashFlowBehavior == SecurityCashFlowBehavior.FactorAmortizing;

    private static bool IsDirectLoan(string? assetClass)
        => IsDirectLoan(SecurityAssetClassCatalog.GetOrDefault(assetClass));

    private static bool IsDirectLoan(SecurityAssetClassDescriptor descriptor)
        => descriptor.InstrumentTypes?.Contains(InstrumentType.DirectLoan) == true;

    // No declared fact singles out fund interests (commitment lots and provider-supplied flows are
    // shared with guarantees), so this is the one place that names a class — through the catalog,
    // so aliases resolve and a label the catalog does not know is not a fund interest.
    private static bool IsPrivateFund(string? assetClass)
        => IsPrivateFund(SecurityAssetClassCatalog.GetOrDefault(assetClass));

    private static bool IsPrivateFund(SecurityAssetClassDescriptor descriptor)
        => string.Equals(descriptor.AssetClass, PrivateFundInterestAssetClass, StringComparison.Ordinal);

    // Factor-amortizing structures, classes that must carry a governed custom profile, and the
    // remaining ops-capable alternatives (real estate, guarantees, private company equity) that no
    // other branch owns.
    private static bool IsStructuredOrCustom(string? assetClass)
    {
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(assetClass);
        return descriptor.CashFlowBehavior == SecurityCashFlowBehavior.FactorAmortizing
            || descriptor.RequiresGovernedProfile
            || (descriptor.AssetOperationsCapabilities is not null
                && !IsFixedIncome(descriptor)
                && !IsDirectLoan(descriptor)
                && !IsPrivateFund(descriptor));
    }

    private const string PrivateFundInterestAssetClass = "PrivateFundInterest";

    private static string? ResolvePrimaryIdentifier(IReadOnlyList<SecurityIdentifierDto> identifiers)
    {
        var primary = identifiers.FirstOrDefault(static identifier => identifier.IsPrimary)
            ?? identifiers.FirstOrDefault();
        return primary is null ? null : $"{primary.Kind}:{primary.Value}";
    }

    // The obligation steps take the contractual schedule kernel's month step (ADR-025), which clamps
    // the day to the target month and throws ArgumentOutOfRangeException past 9999-12-31, as the
    // legacy month arithmetic did (SCH-DEF-011).
    private static DateOnly NextMonthlyDate(DateOnly asOf)
    {
        var monthStart = new DateOnly(asOf.Year, asOf.Month, 1);
        return ScheduleTenor.Months(1).Advance(monthStart, 1);
    }

    private static DateOnly NextQuarterDate(DateOnly asOf)
    {
        // The as-of always falls inside the quarter that starts here, so one step reaches the next.
        var quarterStartMonth = (((asOf.Month - 1) / 3) * 3) + 1;
        var quarterStart = new DateOnly(asOf.Year, quarterStartMonth, 1);
        return ScheduleTenor.Months(3).Advance(quarterStart, 1);
    }

    private static DateOnly NextFrequencyDate(DateOnly asOf, string? paymentFrequency)
    {
        var months = NormalizeFrequencyMonths(paymentFrequency);
        return ScheduleTenor.Months(months).Advance(asOf, 1);
    }

    // The first date after the as-of on the grid compounded from the anchor, so a month-end anchor
    // drifts; an anchor already after the as-of is returned unchanged. The only caller passes 3.
    private static DateOnly AdvanceAfter(DateOnly anchor, DateOnly asOf, int months)
    {
        var tenor = ScheduleTenor.Months(months);
        var next = anchor;
        while (next <= asOf)
        {
            next = tenor.Advance(next, 1);
        }

        return next;
    }

    private static int NormalizeFrequencyMonths(string? paymentFrequency)
    {
        if (string.IsNullOrWhiteSpace(paymentFrequency))
        {
            return 3;
        }

        var normalized = paymentFrequency
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
        if (int.TryParse(normalized, out var periodsPerYear) && periodsPerYear > 0)
        {
            return Math.Max(1, 12 / Math.Clamp(periodsPerYear, 1, 12));
        }

        return normalized switch
        {
            "MONTHLY" => 1,
            "QUARTERLY" => 3,
            "SEMIANNUAL" or "SEMIANNUALLY" or "SEMIYEARLY" => 6,
            "ANNUAL" or "ANNUALLY" or "YEARLY" => 12,
            "BULLET" => 12,
            _ => 3
        };
    }

    // Obligation-term probing shares the coercion rules with the cash-flow resolver via
    // SecurityTermReader; only the payload-source walk (which spans the wrapped Security Master
    // payload) is local to this projection.
    private static bool TryReadString(JsonElement payload, out string? value, params string[] propertyNames)
    {
        value = SecurityTermReader.ReadString(EnumerateTermSources(payload), propertyNames);
        return value is not null;
    }

    private static bool TryReadDecimal(JsonElement payload, out decimal? value, params string[] propertyNames)
    {
        value = SecurityTermReader.ReadDecimal(EnumerateTermSources(payload), propertyNames);
        return value is not null;
    }

    private static bool TryReadDate(JsonElement payload, out DateOnly value, params string[] propertyNames)
    {
        var resolved = SecurityTermReader.ReadDate(EnumerateTermSources(payload), propertyNames);
        value = resolved ?? default;
        return resolved is not null;
    }

    private static IEnumerable<JsonElement> EnumerateTermSources(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        yield return payload;
        if (SecurityTermReader.TryGetProperty(payload, "assetSpecificTerms", out var assetSpecificTerms) &&
            assetSpecificTerms.ValueKind == JsonValueKind.Object)
        {
            yield return assetSpecificTerms;
            if (SecurityTermReader.TryGetProperty(assetSpecificTerms, "profileFields", out var profileFields) &&
                profileFields.ValueKind == JsonValueKind.Object)
            {
                yield return profileFields;
            }
        }

        if (SecurityTermReader.TryGetProperty(payload, "commonTerms", out var commonTerms) &&
            commonTerms.ValueKind == JsonValueKind.Object)
        {
            yield return commonTerms;
        }
    }

    /// <summary>
    /// Term sources with the GOVERNED nested profile fields first: for profile-backed records the
    /// values under <c>assetSpecificTerms.profileFields</c> are the authoritative typed evidence,
    /// and an envelope-root pass-through copy must not shadow them. Mirrors the nested-first
    /// precedence of <c>StructuredCashFlowTermsResolver</c>.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateNestedFirstTermSources(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (SecurityTermReader.TryGetProperty(payload, "assetSpecificTerms", out var assetSpecificTerms) &&
            assetSpecificTerms.ValueKind == JsonValueKind.Object)
        {
            if (SecurityTermReader.TryGetProperty(assetSpecificTerms, "profileFields", out var profileFields) &&
                profileFields.ValueKind == JsonValueKind.Object)
            {
                yield return profileFields;
            }

            yield return assetSpecificTerms;
        }

        yield return payload;
    }

    private static string BuildVarianceSummary(AssetReconciliationResultDto result)
    {
        if (result.VarianceAmount.HasValue && result.VarianceAmount.Value != 0m)
        {
            return $"Expected {result.ExpectedAmount} but actual evidence retained {result.ActualAmount}; variance {result.VarianceAmount}.";
        }

        if (result.ExpectedDate.HasValue && result.ActualDate.HasValue && result.ExpectedDate.Value != result.ActualDate.Value)
        {
            return $"Expected date {result.ExpectedDate:yyyy-MM-dd} differs from actual date {result.ActualDate:yyyy-MM-dd}.";
        }

        return $"Reconciliation status is {result.MatchStatus}.";
    }

    private static string NormalizeVarianceEventKind(string flowType)
        => flowType switch
        {
            "Maturity" => "PrincipalMaturityMissing",
            "Principal" => "PrincipalRepaymentMissing",
            _ => $"{flowType}Missing"
        };

    private static decimal RoundCash(decimal amount)
        => decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
}
