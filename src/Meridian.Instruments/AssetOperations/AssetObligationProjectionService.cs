using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Instruments.AssetOperations;

public sealed class AssetObligationProjectionService
{
    public const string EngineVersion = "asset-obligation-projection-v1";

    private const string SecurityMasterSourceDomain = "SecurityMaster";

    public AssetOperationsDetailDto ProjectFromSecurityMaster(SecurityDetailDto security)
    {
        ArgumentNullException.ThrowIfNull(security);

        var generatedAt = DateTimeOffset.UtcNow;
        var projectionAsOf = DateOnly.FromDateTime(generatedAt.UtcDateTime.Date);
        var subject = BuildSubject(security);
        var projectionRunId = Guid.NewGuid();
        var terms = new[]
        {
            new AssetTermsVersionDto(
                Guid.NewGuid(),
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
                Guid.NewGuid(),
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
        var run = new AssetCashFlowProjectionRunDto(
            projectionRunId,
            security.SecurityId,
            projectionAsOf,
            EngineVersion,
            "Completed",
            generatedAt,
            SecurityMasterSourceDomain,
            security.SecurityId.ToString("D"));
        var flows = BuildProjectedCashFlowsFromSecurityTerms(security, projectionRunId, projectionAsOf).ToArray();
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
                "Only Expected/Projected support was synthesized from Security Master. Publish a reviewed Asset Operations projection and attach accepted retained evidence before downstream use."
            ],
            allowReady: false);

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
                Guid.NewGuid(),
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
                Guid.NewGuid(),
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

        foreach (var flow in AssetOperationsProjectionBuilder.ProjectFixedIncomeCashFlows(reference, projectionRunId))
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
            Guid.NewGuid(),
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

    private static BondReferenceDto? BuildFixedIncomeReference(SecurityDetailDto security, DateOnly projectionAsOf)
    {
        // Fixed-income cash-flow terms (maturity, issue, par, coupon, day-count, frequency, factor)
        // are resolved once through the shared cash-flow terms resolver instead of re-probing here.
        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        if (terms.MaturityDate is not DateOnly maturity)
        {
            return null;
        }

        var issueDate = terms.IssueDate ?? DateOnly.FromDateTime(security.EffectiveFrom.UtcDateTime.Date);

        // Obligation-specific lifecycle/identity terms the cash-flow resolver does not model.
        var payload = BuildSecurityMasterTermsPayload(security);
        _ = TryReadString(payload, out var issuerName, "issuerName", "issuer", "gpSponsor");

        // The applied factor entry is resolved ONCE and shared: it seeds the principal basis AND
        // bounds the sinking-fund schedule. A dated factor already reflects every principal event
        // up to its as-of, so passing those entries to the projector as well would subtract them a
        // second time (a 0.8 factor reflecting a prior 20 payment must not project 60 at maturity
        // on a 100 face). An undated scalar factor is assumed current, so completed payments before
        // the projection as-of are likewise excluded; with no factor at all, completed payments
        // still reduce the opening balance (via a synthesized full-face factor) and only current or
        // future instalments project.
        var appliedFactorEntry = ResolveAppliedFactorEntry(terms, projectionAsOf);
        // A scalar currentFactor with a retained factorDate is DATED evidence: it reflects
        // principal only through that date, exactly like a schedule entry, so treating it as
        // current would leave a January balance standing across February's completed payment.
        // Only a genuinely undated scalar is assumed current (reflects everything before today).
        // The date resolves NESTED-FIRST (governed profileFields before the envelope root),
        // matching StructuredCashFlowTermsResolver: for profile-backed records the governed
        // nested factorDate is authoritative, and a contradictory outer pass-through date must
        // not shift which completed payments the factor is deemed to reflect.
        DateOnly? scalarFactorDate = SecurityTermReader.ReadDate(
            EnumerateNestedFirstTermSources(payload), "factorDate", "currentFactorDate");
        // A FUTURE-dated scalar does not describe today's balance at all: a 0.8 factor effective
        // next month must not reduce today's face by 20% (understating projected interest and
        // maturity principal on ledger-support outputs). It is excluded here and below — the
        // latest eligible schedule point or the unfactored balance governs instead.
        var scalarFactorApplies = terms.CurrentFactor is not null
            && (scalarFactorDate is not DateOnly scalarAsOf || scalarAsOf <= projectionAsOf);
        DateOnly? factorReflectsThrough = appliedFactorEntry?.AsOfDate
            ?? (scalarFactorApplies
                ? (scalarFactorDate ?? projectionAsOf.AddDays(-1))
                : null);

        // Contractual payments before the projection as-of have already OCCURRED — the run is as of
        // today, so projecting them again would report a completed instalment as newly due (and
        // open a MissingEvidence variance against it). Completed payments the factor does not
        // already reflect (all of them when no factor exists, those after its as-of otherwise) are
        // excluded from the projected schedule below and reduce the opening principal instead.
        // Contractual schedules also need a RESOLVABLE principal basis here: without par/face the
        // 100-unit default in ResolveBondPrincipalBasis would cap real instalments, so basis-less
        // records do not attach a sinking fund at all.
        var hasPrincipalBasis = terms.PrincipalFace is > 0m;
        var completedPostFactorPrincipal = 0m;
        if (terms.HasPrincipalSchedule && hasPrincipalBasis)
        {
            completedPostFactorPrincipal = terms.PrincipalSchedule!
                .Where(entry => entry.PaymentDate >= issueDate
                    && entry.PaymentDate <= maturity
                    && entry.PaymentDate < projectionAsOf
                    && (factorReflectsThrough is not DateOnly reflectedCutoff
                        || entry.PaymentDate > reflectedCutoff))
                .Sum(static entry => entry.Amount);
        }

        return new BondReferenceDto(
            security.SecurityId,
            security.DisplayName,
            security.Currency,
            issuerName,
            null,
            ResolvePrimaryIdentifier(security.Identifiers) ?? security.SecurityId.ToString("D"),
            new BondLifecycleDto(
                security.SecurityId,
                security.Status == SecurityStatusDto.Active ? BondLifecycleStat.Active : BondLifecycleStat.Retired,
                issueDate,
                TryReadDate(payload, out var callDate, "callDate") ? callDate : null,
                maturity,
                TryReadDate(payload, out _, "callDate"),
                security.Version,
                terms.PrincipalFace,
                PaymentFrequency: terms.PaymentFrequency),
            terms.CouponRate is null
                ? null
                : new BondAccrualConventionDto(
                    security.SecurityId,
                    terms.DayCountConvention,
                    null,
                    null,
                    "Fixed",
                    terms.CouponRate,
                    null,
                    null,
                    security.Version),
            security.Version,
            SinkingFund: terms.HasPrincipalSchedule && hasPrincipalBasis
                ? new BondSinkingFundDto(
                    security.SecurityId,
                    terms.PrincipalSchedule!
                        .Where(entry => entry.PaymentDate >= issueDate
                            && entry.PaymentDate <= maturity
                            && entry.PaymentDate >= projectionAsOf
                            && (factorReflectsThrough is not DateOnly reflectedThrough
                                || entry.PaymentDate > reflectedThrough))
                        .Select(static entry => new BondSinkingFundEntryDto(entry.PaymentDate, entry.Amount))
                        .ToArray(),
                    SinkFrequency: null,
                    IsProRata: false,
                    Version: security.Version)
                : null,
            InflationLinked: BuildFactorReference(security, terms, payload, appliedFactorEntry, completedPostFactorPrincipal, scalarFactorApplies));
    }

    /// <summary>
    /// The typed factor entry in effect on <paramref name="projectionAsOf"/> — the latest schedule
    /// point dated on or before that day — or <see langword="null"/> when no dated point applies.
    /// </summary>
    private static StructuredFactorScheduleEntry? ResolveAppliedFactorEntry(
        StructuredCashFlowTerms terms, DateOnly projectionAsOf)
    {
        for (var i = terms.FactorSchedule.Count - 1; i >= 0; i--)
        {
            if (terms.FactorSchedule[i].AsOfDate <= projectionAsOf)
            {
                return terms.FactorSchedule[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the pool/index factor for the reference from the typed factor schedule first — the
    /// applied entry resolved by <see cref="ResolveAppliedFactorEntry"/> — falling back to the
    /// scalar <c>currentFactor</c>. Without this, a record whose newest factor lives only in
    /// <c>factorScheduleEntries</c> reaches <c>ResolveBondPrincipalBasis</c> with no factor at all
    /// and is projected at full (factor 1) or stale principal.
    /// </summary>
    private static BondInflationLinkedDto? BuildFactorReference(
        SecurityDetailDto security,
        StructuredCashFlowTerms terms,
        JsonElement payload,
        StructuredFactorScheduleEntry? scheduled,
        decimal completedPostFactorPrincipal,
        bool scalarFactorApplies)
    {
        var effectiveFactor = scheduled?.Factor ?? (scalarFactorApplies ? terms.CurrentFactor : null);
        if (effectiveFactor is null
            && completedPostFactorPrincipal > 0m
            && terms.PrincipalFace is decimal fullFace
            && fullFace > 0m)
        {
            // No factor asserts the balance, but completed contractual payments have still
            // occurred: seed the full-face factor so the reduction below absorbs them — otherwise
            // a factor-less sinker projects completed instalments as newly due from full face.
            effectiveFactor = 1m;
        }

        if (effectiveFactor is null)
        {
            return null;
        }

        // The applied factor reflects principal only THROUGH its as-of date (full face when no
        // factor exists). Contractual payments completed before the projection as-of and not yet
        // inside the factor are excluded from the projected schedule, so the opening principal
        // must absorb them here — otherwise the projection starts from a balance those payments
        // already reduced.
        if (completedPostFactorPrincipal > 0m
            && terms.PrincipalFace is decimal face
            && face > 0m)
        {
            effectiveFactor = Math.Max(0m, effectiveFactor.Value - (completedPostFactorPrincipal / face));
        }

        // The retained factor date must be the SAME governed nested-first date the opening-balance
        // calculation resolved: re-reading outer-first here would stamp Asset Operations lineage
        // with a pass-through date inconsistent with the factor actually applied.
        var effectiveFactorDate = scheduled?.AsOfDate
            ?? SecurityTermReader.ReadDate(
                EnumerateNestedFirstTermSources(payload), "factorDate", "currentFactorDate");
        return new BondInflationLinkedDto(
            security.SecurityId,
            null,
            null,
            null,
            effectiveFactor,
            effectiveFactorDate,
            null,
            security.Version);
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
        if (string.Equals(subject.AssetClass, "StructuredCredit", StringComparison.OrdinalIgnoreCase))
        {
            events.Add(BuildObligationEvent(
                subject,
                term,
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
            "ValuationReview",
            "Obligations",
            NextQuarterDate(projectionAsOf),
            currency,
            "Review valuation support before treating downstream close support as ready.",
            "Quarterly valuation review generated from retained asset terms.",
            ledgerReference,
            "Attach valuation support or record review blocker."));

        if (string.Equals(subject.AssetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase) ||
            TryReadString(payload, out _, "customProfileId"))
        {
            events.Add(BuildObligationEvent(
                subject,
                term,
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
        var status = dueDate < DateOnly.FromDateTime(DateTime.UtcNow.Date)
            ? "MissingEvidence"
            : "Projected";
        return new AssetTermsObligationTimelineEventDto(
            Guid.NewGuid(),
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
            security.AssetSpecificTerms
        });

    private static bool IsFixedIncome(string? assetClass)
        => assetClass is not null &&
           (string.Equals(assetClass, "Bond", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assetClass, "TreasuryBill", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assetClass, "CommercialPaper", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assetClass, "CertificateOfDeposit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assetClass, "StructuredCredit", StringComparison.OrdinalIgnoreCase));

    private static bool IsDirectLoan(string? assetClass)
        => string.Equals(assetClass, "DirectLoan", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrivateFund(string? assetClass)
        => string.Equals(assetClass, "PrivateFundInterest", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(assetClass, "PrivateFund", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(assetClass, "PartnershipInterest", StringComparison.OrdinalIgnoreCase);

    private static bool IsStructuredOrCustom(string? assetClass)
        => string.Equals(assetClass, "StructuredCredit", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(assetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(assetClass, "RealEstateHolding", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(assetClass, "CommitmentGuarantee", StringComparison.OrdinalIgnoreCase);

    private static string? ResolvePrimaryIdentifier(IReadOnlyList<SecurityIdentifierDto> identifiers)
    {
        var primary = identifiers.FirstOrDefault(static identifier => identifier.IsPrimary)
            ?? identifiers.FirstOrDefault();
        return primary is null ? null : $"{primary.Kind}:{primary.Value}";
    }

    private static DateOnly NextMonthlyDate(DateOnly asOf)
    {
        var monthStart = new DateOnly(asOf.Year, asOf.Month, 1);
        return monthStart.AddMonths(1);
    }

    private static DateOnly NextQuarterDate(DateOnly asOf)
    {
        var quarterStartMonth = (((asOf.Month - 1) / 3) * 3) + 1;
        var next = new DateOnly(asOf.Year, quarterStartMonth, 1);
        while (next <= asOf)
        {
            next = next.AddMonths(3);
        }

        return next;
    }

    private static DateOnly NextFrequencyDate(DateOnly asOf, string? paymentFrequency)
    {
        var months = NormalizeFrequencyMonths(paymentFrequency);
        return asOf.AddMonths(months);
    }

    private static DateOnly AdvanceAfter(DateOnly anchor, DateOnly asOf, int months)
    {
        var next = anchor;
        while (next <= asOf)
        {
            next = next.AddMonths(months);
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
