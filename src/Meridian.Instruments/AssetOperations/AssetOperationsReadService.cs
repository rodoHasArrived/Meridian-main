using System.Text.Json;
using Meridian.Instruments.FixedIncome;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.AssetOperations;

namespace Meridian.Instruments.AssetOperations;

public sealed class AssetOperationsReadService : IAssetOperationsQueryService
{
    private readonly IAssetOperationsProjectionStore? _projectionStore;
    private readonly ISecurityMasterQueryService? _securityMasterQueryService;
    private readonly IBondReferenceService? _bondReferenceService;

    public AssetOperationsReadService(
        IAssetOperationsProjectionStore? projectionStore = null,
        ISecurityMasterQueryService? securityMasterQueryService = null,
        IBondReferenceService? bondReferenceService = null)
    {
        _projectionStore = projectionStore;
        _securityMasterQueryService = securityMasterQueryService;
        _bondReferenceService = bondReferenceService;
    }

    public async Task<AssetOperationsDetailDto?> GetOperationsAsync(Guid securityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var published = _projectionStore is null
            ? null
            : await _projectionStore.GetAsync(securityId, ct).ConfigureAwait(false);
        if (published is not null)
        {
            return AssetOperationsProjectionBuilder.WithTermsObligationsTimeline(published);
        }

        var security = _securityMasterQueryService is null
            ? null
            : await _securityMasterQueryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        if (security is null)
        {
            return null;
        }

        return string.Equals(security.AssetClass, "Bond", StringComparison.OrdinalIgnoreCase)
            ? await BuildBondOperationsAsync(security, ct).ConfigureAwait(false)
            : BuildIdentityOnlyOperations(security);
    }

    public async Task<AssetOperationsReadinessDto?> GetReadinessAsync(Guid securityId, CancellationToken ct = default)
    {
        var detail = await GetOperationsAsync(securityId, ct).ConfigureAwait(false);
        return detail?.Readiness;
    }

    private async Task<AssetOperationsDetailDto> BuildBondOperationsAsync(SecurityDetailDto security, CancellationToken ct)
    {
        var bond = _bondReferenceService is null
            ? null
            : await _bondReferenceService.GetReferenceAsync(security.SecurityId, ct).ConfigureAwait(false);
        if (bond is null)
        {
            return BuildIdentityOnlyOperations(security);
        }

        return AssetOperationsProjectionBuilder.FromBondReference(bond, BuildSubject(security));
    }

    private static AssetOperationsDetailDto BuildIdentityOnlyOperations(SecurityDetailDto security)
    {
        var subject = BuildSubject(security);
        var readiness = new AssetOperationsReadinessDto(
            subject.SecurityId,
            "ReviewRequired",
            subject.OperationalProfile,
            ["Identity", "TermsHistory"],
            subject.OperationalProfile.Except(["Identity", "TermsHistory"], StringComparer.OrdinalIgnoreCase).ToArray(),
            ["No asset-operation domain projection has been published for this Security Master subject."],
            DateTimeOffset.UtcNow,
            "SecurityMaster",
            subject.SecurityId.ToString("D"));

        var detail = new AssetOperationsDetailDto(
            subject,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            readiness,
            []);
        return AssetOperationsProjectionBuilder.WithTermsObligationsTimeline(detail);
    }

    private static AssetOperationSubjectDto BuildSubject(SecurityDetailDto security)
        => new(
            security.SecurityId,
            security.AssetClass,
            security.DisplayName,
            ResolvePrimaryIdentifier(security.Identifiers),
            SecurityAssetClassCatalog.GetAssetOperationsCapabilities(security.AssetClass));

    private static string? ResolvePrimaryIdentifier(IReadOnlyList<SecurityIdentifierDto> identifiers)
    {
        var primary = identifiers.FirstOrDefault(static identifier => identifier.IsPrimary)
            ?? identifiers.FirstOrDefault();
        return primary is null ? null : $"{primary.Kind}:{primary.Value}";
    }
}

public sealed class AssetOperationsProjectionCommandService : IAssetOperationsCommandService
{
    private readonly IAssetOperationsProjectionStore _projectionStore;

    public AssetOperationsProjectionCommandService(IAssetOperationsProjectionStore projectionStore)
    {
        _projectionStore = projectionStore;
    }

    public async Task<AssetOperationsDetailDto> UpsertProjectionAsync(
        AssetOperationsProjectionDto projection,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct = default)
    {
        await _projectionStore.UpsertAsync(projection, approval, ct).ConfigureAwait(false);
        return await _projectionStore.GetAsync(projection.Subject.SecurityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Asset Operations projection was not readable after write.");
    }
}

public static class AssetOperationsProjectionBuilder
{
    private const string TimelineEngineVersion = "asset-terms-obligations-timeline-v1";
    private const string FixedIncomeCashFlowEngineVersion = "fixed-income-cash-flow-projection-v1";
    private const decimal DefaultPrincipalBasis = 100m;

    public static AssetOperationsProjectionDto FromDirectLending(
        LoanContractDetailDto contract,
        IReadOnlyList<ProjectionRunDto> projectionRuns,
        IReadOnlyDictionary<Guid, IReadOnlyList<ProjectedCashFlowDto>> projectedCashFlowsByRun,
        IReadOnlyList<CashTransactionDto> actualActivity,
        IReadOnlyList<ReconciliationRunDto> reconciliationRuns,
        IReadOnlyDictionary<Guid, IReadOnlyList<ReconciliationResultDto>> reconciliationResultsByRun)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var securityReference = contract.CurrentTerms.SecurityMasterReference
            ?? throw new InvalidOperationException("Direct lending Asset Operations projection requires a Security Master reference.");
        var securityId = securityReference.SecurityId;
        var subject = new AssetOperationSubjectDto(
            securityId,
            "DirectLoan",
            contract.FacilityName,
            securityReference.Symbol,
            SecurityAssetClassCatalog.GetAssetOperationsCapabilities("DirectLoan"));

        var terms = contract.TermsVersions.Select(version => new AssetTermsVersionDto(
            Guid.NewGuid(),
            securityId,
            version.VersionNumber,
            version.TermsHash,
            version.Terms.OriginationDate,
            version.RecordedAt,
            "DirectLending",
            contract.LoanId.ToString("D"),
            $"{contract.FacilityName} terms version {version.VersionNumber}")).ToArray();
        var lifecycle = new[]
        {
            new AssetLifecycleEventDto(
                Guid.NewGuid(),
                securityId,
                "LoanStatus",
                contract.Status.ToString(),
                contract.EffectiveDate,
                DateTimeOffset.UtcNow,
                "DirectLending",
                contract.LoanId.ToString("D"),
                $"Loan is {contract.Status}.")
        };
        var runs = projectionRuns.Select(run => new AssetCashFlowProjectionRunDto(
            run.ProjectionRunId,
            securityId,
            run.ProjectionAsOf,
            run.EngineVersion,
            run.Status.ToString(),
            run.GeneratedAt,
            "DirectLending",
            contract.LoanId.ToString("D"))).ToArray();
        var flows = projectionRuns
            .SelectMany(run => projectedCashFlowsByRun.TryGetValue(run.ProjectionRunId, out var runFlows) ? runFlows : [])
            .Select(flow => new AssetProjectedCashFlowDto(
                flow.ProjectedCashFlowId,
                flow.ProjectionRunId,
                securityId,
                flow.FlowSequenceNumber,
                flow.FlowType,
                flow.DueDate,
                flow.Amount,
                flow.Currency.ToString(),
                "Projected",
                flow.AccrualStartDate,
                flow.AccrualEndDate,
                flow.PrincipalBasis,
                flow.AnnualRate,
                "DirectLending",
                contract.LoanId.ToString("D"))).ToArray();
        var activity = actualActivity.Select(row => new AssetActualActivityDto(
            row.CashTransactionId,
            securityId,
            row.TransactionType,
            row.EffectiveDate,
            row.SettlementDate,
            row.Amount,
            row.Currency.ToString(),
            row.IsVoided ? "Voided" : "Posted",
            "DirectLending",
            contract.LoanId.ToString("D"),
            row.ExternalRef)).ToArray();
        var reconciliations = reconciliationRuns.Select(run => new AssetReconciliationRunDto(
            run.ReconciliationRunId,
            securityId,
            run.ProjectionRunId,
            run.Status,
            run.RequestedAt,
            run.CompletedAt,
            "DirectLending",
            contract.LoanId.ToString("D"))).ToArray();
        var reconciliationResults = reconciliationRuns
            .SelectMany(run => reconciliationResultsByRun.TryGetValue(run.ReconciliationRunId, out var results) ? results : [])
            .Select(result => new AssetReconciliationResultDto(
                result.ReconciliationResultId,
                result.ReconciliationRunId,
                securityId,
                result.MatchStatus,
                result.ExpectedAmount,
                result.ActualAmount,
                result.VarianceAmount,
                result.ExpectedDate,
                result.ActualDate,
                "DirectLending",
                contract.LoanId.ToString("D"),
                result.CashTransactionId?.ToString("D"))).ToArray();
        var ledger = new[]
        {
            new AssetLedgerProjectionDto(
                Guid.NewGuid(),
                securityId,
                "DirectLendingLedgerProjection",
                DateOnly.FromDateTime(DateTime.UtcNow),
                "Primary",
                "Ready",
                null,
                null,
                contract.CurrentTerms.BaseCurrency.ToString(),
                "LoanAccountingProjector",
                contract.LoanId.ToString("D"),
                securityReference.LedgerMappingReference)
        };

        var readyCapabilities = ReadyCapabilities(subject.OperationalProfile, terms, lifecycle, flows, activity, reconciliations, reconciliationResults, ledger);
        var readiness = BuildReadiness(subject, readyCapabilities, "DirectLending", contract.LoanId.ToString("D"));

        var projection = new AssetOperationsProjectionDto(
            subject,
            terms,
            lifecycle,
            runs,
            flows,
            activity,
            reconciliations,
            reconciliationResults,
            ledger,
            readiness,
            lifecycle);
        return WithTermsObligationsTimeline(projection);
    }

    public static AssetOperationsDetailDto FromBondReference(BondReferenceDto bond, AssetOperationSubjectDto subject)
    {
        var terms = new[]
        {
            new AssetTermsVersionDto(
                Guid.NewGuid(),
                subject.SecurityId,
                checked((int)Math.Min(bond.Version, int.MaxValue)),
                $"bond-reference:{bond.Version}",
                bond.Lifecycle?.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date),
                DateTimeOffset.UtcNow,
                "SecurityMaster",
                subject.SecurityId.ToString("D"),
                $"{bond.DisplayName} maturity/coupon reference terms")
        };
        IReadOnlyList<AssetLifecycleEventDto> lifecycle = bond.Lifecycle is null
            ? []
            :
            [
                new AssetLifecycleEventDto(
                    Guid.NewGuid(),
                    subject.SecurityId,
                    "BondLifecycle",
                    bond.Lifecycle.LifecycleStat.ToString(),
                    bond.Lifecycle.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    DateTimeOffset.UtcNow,
                    "SecurityMaster",
                    subject.SecurityId.ToString("D"),
                    $"Bond lifecycle is {bond.Lifecycle.LifecycleStat}.")
            ];
        var projectionRunId = Guid.NewGuid();
        var run = new AssetCashFlowProjectionRunDto(
            projectionRunId,
            subject.SecurityId,
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            FixedIncomeCashFlowEngineVersion,
            "Completed",
            DateTimeOffset.UtcNow,
            "SecurityMaster",
            subject.SecurityId.ToString("D"));
        var flows = BuildBondProjectedCashFlows(bond, projectionRunId).ToArray();
        var ledger = new[]
        {
            new AssetLedgerProjectionDto(
                Guid.NewGuid(),
                subject.SecurityId,
                "FixedIncomeLedgerProjection",
                flows.FirstOrDefault()?.DueDate ?? bond.Lifecycle?.MaturityDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date),
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
        var readiness = BuildReadiness(subject, readyCapabilities, "SecurityMaster", subject.SecurityId.ToString("D"));

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

    private static IEnumerable<AssetProjectedCashFlowDto> BuildBondProjectedCashFlows(BondReferenceDto bond, Guid projectionRunId)
    {
        var maturity = bond.Lifecycle?.MaturityDate;
        if (maturity is null)
        {
            yield break;
        }

        var issueDate = bond.Lifecycle?.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var paymentFrequency = ResolvePaymentFrequencyPerYear(bond.Lifecycle?.PaymentFrequency);
        var periodMonths = Math.Max(1, 12 / paymentFrequency);
        var principalBasis = ResolveBondPrincipalBasis(bond);
        var outstandingPrincipal = principalBasis;
        var sinkingFundEntries = bond.SinkingFund?.Schedule
            .Where(static entry => entry.Amount > 0m)
            .OrderBy(static entry => entry.SinkDate)
            .ToArray() ?? [];
        var sinkingFundIndex = 0;
        var sequenceNumber = 1;
        var accrualStart = issueDate;

        while (accrualStart < maturity.Value)
        {
            var accrualEnd = accrualStart.AddMonths(periodMonths);
            if (accrualEnd > maturity.Value)
            {
                accrualEnd = maturity.Value;
            }

            if (bond.AccrualConvention?.FixedCouponRate is decimal coupon && coupon > 0m && outstandingPrincipal > 0m)
            {
                var couponAmount = CalculateBondCouponAmount(
                    outstandingPrincipal,
                    coupon,
                    bond.AccrualConvention.DayCountConvention,
                    accrualStart,
                    accrualEnd,
                    paymentFrequency);

                if (couponAmount > 0m)
                {
                    yield return new AssetProjectedCashFlowDto(
                        Guid.NewGuid(),
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
                    Guid.NewGuid(),
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

            if (accrualEnd == maturity.Value)
            {
                break;
            }

            accrualStart = accrualEnd;
        }

        if (outstandingPrincipal > 0m)
        {
            yield return new AssetProjectedCashFlowDto(
                Guid.NewGuid(),
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

    public static AssetOperationsDetailDto WithTermsObligationsTimeline(AssetOperationsDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        if (detail.TermsObligationsTimeline is not null)
        {
            return detail;
        }

        return detail with
        {
            TermsObligationsTimeline = BuildTermsObligationsTimeline(
                detail.Subject,
                detail.TermsHistory,
                detail.LifecycleEvents,
                detail.CashFlowProjectionRuns,
                detail.ProjectedCashFlows,
                detail.ActualActivity,
                detail.ReconciliationResults,
                detail.LedgerProjections)
        };
    }

    private static AssetOperationsProjectionDto WithTermsObligationsTimeline(AssetOperationsProjectionDto projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (projection.TermsObligationsTimeline is not null)
        {
            return projection;
        }

        return projection with
        {
            TermsObligationsTimeline = BuildTermsObligationsTimeline(
                projection.Subject,
                projection.TermsHistory,
                projection.LifecycleEvents,
                projection.CashFlowProjectionRuns,
                projection.ProjectedCashFlows,
                projection.ActualActivity,
                projection.ReconciliationResults,
                projection.LedgerProjections)
        };
    }

    private static AssetTermsObligationsTimelineDto BuildTermsObligationsTimeline(
        AssetOperationSubjectDto subject,
        IReadOnlyList<AssetTermsVersionDto> terms,
        IReadOnlyList<AssetLifecycleEventDto> lifecycleEvents,
        IReadOnlyList<AssetCashFlowProjectionRunDto> projectionRuns,
        IReadOnlyList<AssetProjectedCashFlowDto> projectedCashFlows,
        IReadOnlyList<AssetActualActivityDto> actualActivity,
        IReadOnlyList<AssetReconciliationResultDto> reconciliationResults,
        IReadOnlyList<AssetLedgerProjectionDto> ledgerProjections)
    {
        var latestRun = projectionRuns
            .OrderByDescending(static run => run.GeneratedAt)
            .FirstOrDefault();
        var projectionAsOf = latestRun?.ProjectionAsOf
            ?? projectedCashFlows.OrderBy(static flow => flow.DueDate).FirstOrDefault()?.DueDate
            ?? terms.OrderByDescending(static row => row.EffectiveDate).FirstOrDefault()?.EffectiveDate
            ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var generatedAt = latestRun?.GeneratedAt ?? DateTimeOffset.UtcNow;
        var events = new List<AssetTermsObligationTimelineEventDto>();

        foreach (var flow in projectedCashFlows.OrderBy(static flow => flow.DueDate).ThenBy(static flow => flow.SequenceNumber))
        {
            var reconciliation = FindReconciliation(flow, reconciliationResults);
            var activity = FindActivity(flow, reconciliation, actualActivity);
            var ledgerProjection = FindLedgerProjection(flow, ledgerProjections);
            var status = ResolveTimelineStatus(flow, reconciliation, projectionAsOf);
            var actualAmount = reconciliation?.ActualAmount ?? activity?.Amount;
            var actualDate = reconciliation?.ActualDate ?? activity?.EffectiveDate;
            var variance = reconciliation?.VarianceAmount
                ?? (actualAmount.HasValue ? actualAmount.Value - flow.Amount : null);

            events.Add(new AssetTermsObligationTimelineEventDto(
                Guid.NewGuid(),
                subject.SecurityId,
                NormalizeTimelineEventKind(flow.FlowType),
                ResolveTimelineLane(flow.FlowType),
                flow.DueDate,
                actualDate,
                flow.AccrualStartDate,
                flow.AccrualEndDate,
                flow.Amount,
                actualAmount,
                variance,
                flow.Currency,
                status,
                flow.SourceDomain ?? "AssetOperations",
                flow.SourceEntityId,
                reconciliation?.EvidenceLink ?? activity?.EvidenceLink,
                ledgerProjection?.LedgerReferenceId,
                BuildCashFlowTimelineSummary(flow, status),
                BuildTimelineFlowPayload(flow, reconciliation, activity, ledgerProjection)));
        }

        foreach (var lifecycle in lifecycleEvents.OrderBy(static row => row.EffectiveDate))
        {
            events.Add(new AssetTermsObligationTimelineEventDto(
                Guid.NewGuid(),
                subject.SecurityId,
                lifecycle.EventType,
                "Lifecycle",
                lifecycle.EffectiveDate,
                null,
                null,
                null,
                null,
                null,
                null,
                string.Empty,
                lifecycle.LifecycleState,
                lifecycle.SourceDomain,
                lifecycle.SourceEntityId,
                null,
                null,
                lifecycle.Summary,
                lifecycle.ExtensionPayload));
        }

        var warnings = new List<string>();
        if (terms.Count == 0)
        {
            warnings.Add("No retained terms version is available for the timeline.");
        }

        if (projectedCashFlows.Count == 0)
        {
            warnings.Add("No projected cash-flow obligations are available for the timeline.");
        }

        return new AssetTermsObligationsTimelineDto(
            subject.SecurityId,
            projectionAsOf,
            latestRun is null ? TimelineEngineVersion : $"{latestRun.EngineVersion}+{TimelineEngineVersion}",
            warnings.Count == 0 ? "Ready" : "ReviewRequired",
            events.OrderBy(static row => row.EffectiveDate).ThenBy(static row => row.EventKind).ToArray(),
            warnings,
            generatedAt,
            latestRun?.SourceDomain ?? "AssetOperations",
            latestRun?.SourceEntityId ?? subject.SecurityId.ToString("D"));
    }

    private static decimal ResolveBondPrincipalBasis(BondReferenceDto bond)
    {
        var par = bond.Lifecycle?.Par is decimal parValue && parValue > 0m
            ? parValue
            : DefaultPrincipalBasis;
        var factor = bond.InflationLinked?.CurrentFactor is decimal currentFactor && currentFactor > 0m
            ? currentFactor
            : 1m;
        return RoundCash(par * factor);
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

    private static decimal CalculateBondCouponAmount(
        decimal principalBasis,
        decimal couponRatePercent,
        string? dayCountConvention,
        DateOnly accrualStart,
        DateOnly accrualEnd,
        int paymentFrequency)
    {
        var yearFraction = CalculateYearFraction(dayCountConvention, accrualStart, accrualEnd, paymentFrequency);
        return RoundCash(principalBasis * (couponRatePercent / 100m) * yearFraction);
    }

    private static decimal CalculateYearFraction(
        string? dayCountConvention,
        DateOnly accrualStart,
        DateOnly accrualEnd,
        int paymentFrequency)
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
        if (normalized is not null && normalized.Contains("ACT/360", StringComparison.OrdinalIgnoreCase))
        {
            return actualDays / 360m;
        }

        if (normalized is not null && (normalized.Contains("ACT/365", StringComparison.OrdinalIgnoreCase) ||
                                       normalized.Contains("ACTUAL/365", StringComparison.OrdinalIgnoreCase)))
        {
            return actualDays / 365m;
        }

        return 1m / Math.Max(1, paymentFrequency);
    }

    private static decimal Days360(DateOnly start, DateOnly end)
    {
        var startDay = Math.Min(start.Day, 30);
        var endDay = end.Day == 31 && startDay == 30 ? 30 : Math.Min(end.Day, 30);
        return ((end.Year - start.Year) * 360m) + ((end.Month - start.Month) * 30m) + (endDay - startDay);
    }

    private static decimal RoundCash(decimal amount)
        => decimal.Round(amount, 4, MidpointRounding.AwayFromZero);

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
            bond.AccrualConvention?.DayCountConvention,
            paymentFrequencyPerYear = paymentFrequency,
            principalBasis,
            outstandingPrincipal,
            couponRatePercent,
            accrualStart,
            accrualEnd,
            inflationFactor = bond.InflationLinked?.CurrentFactor
        });

    private static AssetReconciliationResultDto? FindReconciliation(
        AssetProjectedCashFlowDto flow,
        IReadOnlyList<AssetReconciliationResultDto> reconciliationResults)
        => reconciliationResults
            .Where(result =>
                result.ExpectedDate == flow.DueDate ||
                (result.ExpectedAmount.HasValue && decimal.Round(result.ExpectedAmount.Value, 4, MidpointRounding.AwayFromZero) == flow.Amount))
            .OrderByDescending(result => result.ExpectedDate == flow.DueDate && result.ExpectedAmount == flow.Amount)
            .FirstOrDefault();

    private static AssetActualActivityDto? FindActivity(
        AssetProjectedCashFlowDto flow,
        AssetReconciliationResultDto? reconciliation,
        IReadOnlyList<AssetActualActivityDto> actualActivity)
    {
        if (reconciliation?.ActualDate is DateOnly actualDate)
        {
            var matched = actualActivity.FirstOrDefault(activity =>
                activity.EffectiveDate == actualDate &&
                (!reconciliation.ActualAmount.HasValue || activity.Amount == reconciliation.ActualAmount.Value));
            if (matched is not null)
            {
                return matched;
            }
        }

        return actualActivity.FirstOrDefault(activity =>
            activity.EffectiveDate == flow.DueDate &&
            activity.Amount == flow.Amount);
    }

    private static AssetLedgerProjectionDto? FindLedgerProjection(
        AssetProjectedCashFlowDto flow,
        IReadOnlyList<AssetLedgerProjectionDto> ledgerProjections)
        => ledgerProjections
            .OrderBy(row => Math.Abs(row.AccountingDate.DayNumber - flow.DueDate.DayNumber))
            .FirstOrDefault(row => string.Equals(row.Currency, flow.Currency, StringComparison.OrdinalIgnoreCase));

    private static string ResolveTimelineStatus(
        AssetProjectedCashFlowDto flow,
        AssetReconciliationResultDto? reconciliation,
        DateOnly projectionAsOf)
    {
        if (reconciliation is not null)
        {
            return reconciliation.MatchStatus;
        }

        return flow.DueDate < projectionAsOf
            ? "MissingEvidence"
            : flow.Status;
    }

    private static string NormalizeTimelineEventKind(string flowType)
        => flowType switch
        {
            "Maturity" => "PrincipalMaturity",
            "Principal" => "PrincipalRepayment",
            _ => flowType
        };

    private static string ResolveTimelineLane(string flowType)
    {
        if (flowType.Contains("Coupon", StringComparison.OrdinalIgnoreCase))
        {
            return "Coupon";
        }

        if (flowType.Contains("Interest", StringComparison.OrdinalIgnoreCase))
        {
            return "Interest";
        }

        if (flowType.Contains("Principal", StringComparison.OrdinalIgnoreCase) ||
            flowType.Contains("Maturity", StringComparison.OrdinalIgnoreCase))
        {
            return "Principal";
        }

        if (flowType.Contains("Capital", StringComparison.OrdinalIgnoreCase) ||
            flowType.Contains("Distribution", StringComparison.OrdinalIgnoreCase))
        {
            return "Capital";
        }

        return "CashFlow";
    }

    private static string BuildCashFlowTimelineSummary(AssetProjectedCashFlowDto flow, string status)
        => $"{flow.FlowType} obligation due {flow.DueDate:yyyy-MM-dd} for {flow.Amount} {flow.Currency}; status {status}.";

    private static JsonElement BuildTimelineFlowPayload(
        AssetProjectedCashFlowDto flow,
        AssetReconciliationResultDto? reconciliation,
        AssetActualActivityDto? activity,
        AssetLedgerProjectionDto? ledgerProjection)
        => JsonSerializer.SerializeToElement(new
        {
            projectionRunId = flow.ProjectionRunId,
            projectedCashFlowId = flow.ProjectedCashFlowId,
            flow.SequenceNumber,
            flow.PrincipalBasis,
            flow.AnnualRate,
            reconciliationResultId = reconciliation?.ReconciliationResultId,
            actualActivityId = activity?.ActivityId,
            ledgerProjectionId = ledgerProjection?.LedgerProjectionId
        });

    private static IReadOnlyList<string> ReadyCapabilities(
        IReadOnlyList<string> capabilities,
        IReadOnlyList<AssetTermsVersionDto> terms,
        IReadOnlyList<AssetLifecycleEventDto> lifecycle,
        IReadOnlyList<AssetProjectedCashFlowDto> projectedCashFlows,
        IReadOnlyList<AssetActualActivityDto> actualActivity,
        IReadOnlyList<AssetReconciliationRunDto> reconciliationRuns,
        IReadOnlyList<AssetReconciliationResultDto> reconciliationResults,
        IReadOnlyList<AssetLedgerProjectionDto> ledgerProjections)
    {
        var ready = new List<string> { "Identity" };
        if (terms.Count > 0)
        {
            ready.Add("TermsHistory");
        }
        if (lifecycle.Count > 0)
        {
            ready.Add("LifecycleState");
            ready.Add("WorkflowAudit");
        }
        if (projectedCashFlows.Count > 0)
        {
            ready.Add("ProjectedCashFlows");
        }
        if (actualActivity.Count > 0)
        {
            ready.Add("ActualActivity");
        }
        if (reconciliationRuns.Count > 0 || reconciliationResults.Count > 0)
        {
            ready.Add("Reconciliation");
        }
        if (ledgerProjections.Count > 0)
        {
            ready.Add("LedgerProjection");
        }
        ready.Add("Evidence");

        return ready
            .Where(capability => capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AssetOperationsReadinessDto BuildReadiness(
        AssetOperationSubjectDto subject,
        IReadOnlyList<string> readyCapabilities,
        string sourceDomain,
        string? sourceEntityId)
    {
        var missing = subject.OperationalProfile
            .Except(readyCapabilities, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new AssetOperationsReadinessDto(
            subject.SecurityId,
            missing.Length == 0 ? "Ready" : "ReviewRequired",
            subject.OperationalProfile,
            readyCapabilities,
            missing,
            missing.Length == 0 ? [] : missing.Select(capability => $"{capability} projection has not been published.").ToArray(),
            DateTimeOffset.UtcNow,
            sourceDomain,
            sourceEntityId);
    }
}
