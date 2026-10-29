using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Instruments.FixedIncome;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Storage.AssetOperations;

namespace Meridian.Instruments.AssetOperations;

/// <summary>
/// Reads asset operations projections and their retained lifecycle evidence.
/// </summary>
public sealed class AssetOperationsReadService : IAssetOperationsQueryService
{
    private readonly IAssetOperationsProjectionStore? _projectionStore;
    private readonly ISecurityMasterQueryService? _securityMasterQueryService;
    private readonly IBondReferenceService? _bondReferenceService;
    private readonly AssetObligationProjectionService _obligationProjectionService;
    private readonly IInstrumentPositionProjectionStore? _positionProjectionStore;
    private readonly ISecurityMasterCashFlowService? _cashFlows;
    private readonly TimeProvider _clock;
    private readonly IBondCalculationPolicy? _bondPolicy;

    public AssetOperationsReadService(
        IAssetOperationsProjectionStore? projectionStore = null,
        ISecurityMasterQueryService? securityMasterQueryService = null,
        IBondReferenceService? bondReferenceService = null,
        AssetObligationProjectionService? obligationProjectionService = null,
        IInstrumentPositionProjectionStore? positionProjectionStore = null,
        ISecurityMasterCashFlowService? cashFlows = null,
        TimeProvider? clock = null,
        IBondCalculationPolicy? bondPolicy = null)
    {
        _projectionStore = projectionStore;
        _securityMasterQueryService = securityMasterQueryService;
        _bondReferenceService = bondReferenceService;
        _obligationProjectionService = obligationProjectionService ?? new AssetObligationProjectionService(bondPolicy);
        _positionProjectionStore = positionProjectionStore;
        _cashFlows = cashFlows;
        _clock = clock ?? TimeProvider.System;
        _bondPolicy = bondPolicy;
    }

    public Task<AssetOperationsDetailDto?> GetOperationsAsync(Guid securityId, CancellationToken ct = default)
        => GetOperationsAsOfAsync(securityId, new BondCalculationContext(_clock.GetUtcNow()), ct);

    /// <summary>Projects the same retained economic inputs at an explicit cutoff, without clock-dependent flow identity.</summary>
    public async Task<AssetOperationsDetailDto?> GetOperationsAsOfAsync(
        Guid securityId, BondCalculationContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();

        SecurityDetailDto? selectedSecurity = null;
        if (_bondPolicy is not null)
        {
            selectedSecurity = await SelectSecurityAsync(securityId, context, ct).ConfigureAwait(false);
            if (selectedSecurity is null)
            {
                return null;
            }
            var blocker = await _bondPolicy.GetBlockerAsync(selectedSecurity, ct).ConfigureAwait(false);
            if (blocker is not null)
            {
                var blocked = new StructuredCashFlowProjectionDto(securityId,
                    StructuredCashFlowSourceKind.CalculatedBullet, StructuredCashFlowScenario.Base,
                    context.AsOf, [], BlockedReason: blocker);
                var blockedDetail = await _obligationProjectionService.ProjectFromSecurityMasterAsync(
                    selectedSecurity, context, blocked, ct).ConfigureAwait(false);
                return AssetOperationsProjectionBuilder.WithFailClosedEvidenceReadiness(blockedDetail);
            }
        }

        // A present-day publication cannot stand in for a requested historical calculation.
        var published = _projectionStore is null || context.KnownAt.HasValue || context.SecurityVersion.HasValue
            || context.AsOf.UtcDateTime.Date != _clock.GetUtcNow().UtcDateTime.Date
            ? null
            : await _projectionStore.GetAsync(securityId, ct).ConfigureAwait(false);
        AssetOperationsDetailDto detail;
        if (published is not null)
        {
            detail = AssetOperationsProjectionBuilder.WithTermsObligationsTimeline(published);
        }
        else
        {
            var security = selectedSecurity ?? await SelectSecurityAsync(securityId, context, ct).ConfigureAwait(false);
            if (security is null)
            {
                return null;
            }

            detail = SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).CarriesBondCouponStructure
                ? await BuildBondOperationsAsync(security, context, ct).ConfigureAwait(false)
                : await _obligationProjectionService.ProjectFromSecurityMasterAsync(security, context, ct: ct).ConfigureAwait(false);
        }

        if (_positionProjectionStore is null)
        {
            return AssetOperationsProjectionBuilder.WithFailClosedEvidenceReadiness(detail);
        }

        var positions = await _positionProjectionStore
            .GetSecurityAsync(securityId, ct)
            .ConfigureAwait(false);
        if (positions.InstrumentRoles.Count == 0 &&
            positions.BookPositions.Count == 0 &&
            positions.PositionEconomicStates.Count == 0 &&
            positions.ProjectionLineages.Count == 0)
        {
            return AssetOperationsProjectionBuilder.WithFailClosedEvidenceReadiness(detail);
        }

        var merged = detail with
        {
            InstrumentRoles = positions.InstrumentRoles,
            BookPositions = positions.BookPositions,
            PositionEconomicStates = positions.PositionEconomicStates,
            ProjectionLineages = positions.ProjectionLineages
        };
        return AssetOperationsProjectionBuilder.WithFailClosedEvidenceReadiness(merged);
    }

    public async Task<AssetOperationsReadinessDto?> GetReadinessAsync(Guid securityId, CancellationToken ct = default)
    {
        var detail = await GetOperationsAsync(securityId, ct).ConfigureAwait(false);
        return detail?.Readiness;
    }

    private async Task<AssetOperationsDetailDto> BuildBondOperationsAsync(
        SecurityDetailDto security, BondCalculationContext context, CancellationToken ct)
    {
        var declared = SecurityVersionedTerms.ReadDeclaredScheduleConventions(security.AssetClass, security.AssetSpecificTerms);
        if (declared.Status != SecurityDeclaredConventionsStatus.None || context.KnownAt.HasValue || context.SecurityVersion.HasValue)
        {
            var projection = _cashFlows is null ? null : await _cashFlows.GetProjectionAsync(
                security, StructuredCashFlowScenario.Base, context, ct).ConfigureAwait(false);
            projection ??= new StructuredCashFlowProjectionDto(
                security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, StructuredCashFlowScenario.Base,
                context.AsOf, [], BlockedReason: "Declared bond economics require a retained cash-flow source and the shared cash-flow service.");
            return await _obligationProjectionService.ProjectFromSecurityMasterAsync(security, context, projection, ct).ConfigureAwait(false);
        }

        var bond = _bondReferenceService is null
            ? null
            : await _bondReferenceService.GetReferenceAsync(security.SecurityId, ct).ConfigureAwait(false);
        if (bond is null)
        {
            return await _obligationProjectionService.ProjectFromSecurityMasterAsync(security, context, ct: ct).ConfigureAwait(false);
        }

        return AssetOperationsProjectionBuilder.FromBondReference(bond, BuildSubject(security), context);
    }

    private Task<SecurityDetailDto?> SelectSecurityAsync(Guid securityId, BondCalculationContext context, CancellationToken ct)
        => _securityMasterQueryService is null
            ? Task.FromResult<SecurityDetailDto?>(null)
            : context.KnownAt is { } knownAt
                ? _securityMasterQueryService.GetRecordedByIdAsOfAsync(securityId, knownAt, ct)
                : _securityMasterQueryService.GetByIdAsync(securityId, ct);

    private static AssetOperationsDetailDto BuildIdentityOnlyOperations(SecurityDetailDto security)
    {
        var subject = BuildSubject(security);
        var readiness = new AssetOperationsReadinessDto(
            subject.SecurityId,
            "ReviewRequired",
            subject.OperationalProfile,
            ["Identity"],
            subject.OperationalProfile.Except(["Identity"], StringComparer.OrdinalIgnoreCase).ToArray(),
            ["Publish a reviewed Asset Operations projection and attach accepted retained evidence before treating this Security Master subject as ready."],
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

/// <summary>
/// Writes asset operations projections through the configured projection store.
/// </summary>
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

/// <summary>
/// Builds asset operations projection records from instrument and lifecycle inputs.
/// </summary>
public static partial class AssetOperationsProjectionBuilder
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

        var accountingDate = projectionRuns.MaxBy(static run => run.GeneratedAt)?.ProjectionAsOf
            ?? contract.EffectiveDate;
        var terms = contract.TermsVersions.Select(version => new AssetTermsVersionDto(
            DirectLendingProjectionIdentity(contract.LoanId, securityId,
                $"terms/{version.VersionNumber}/{version.TermsHash}"),
            securityId,
            version.VersionNumber,
            version.TermsHash,
            version.Terms.OriginationDate,
            version.RecordedAt,
            "DirectLending",
            contract.LoanId.ToString("D"),
            $"{contract.FacilityName} terms version {version.VersionNumber}",
            BuildDirectLendingTermsPayload(contract, version.Terms))).ToArray();
        var lifecycle = new[]
        {
            new AssetLifecycleEventDto(
                DirectLendingProjectionIdentity(contract.LoanId, securityId,
                    $"status/{contract.Status}/{contract.EffectiveDate:yyyy-MM-dd}/{contract.ActivationDate:yyyy-MM-dd}/{contract.CloseDate:yyyy-MM-dd}"),
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
                DirectLendingProjectionIdentity(contract.LoanId, securityId, $"ledger/{accountingDate:yyyy-MM-dd}"),
                securityId,
                "DirectLendingLedgerProjection",
                accountingDate,
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

    private static Guid DirectLendingProjectionIdentity(Guid loanId, Guid securityId, FormattableString item)
        => new(Sha256Digest.ComputeBytesUtf8(
            FormattableString.Invariant($"meridian/direct-lending/assets/{loanId:N}/{securityId:N}/{FormattableString.Invariant(item)}")).AsSpan(0, 16));

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

    public static AssetOperationsDetailDto WithFailClosedEvidenceReadiness(AssetOperationsDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var evidenceIsRequired = detail.Subject.OperationalProfile.Contains(
            "Evidence",
            StringComparer.OrdinalIgnoreCase);
        var evidenceWasClaimed = detail.Readiness.ReadyCapabilities.Contains(
            "Evidence",
            StringComparer.OrdinalIgnoreCase);
        var evidenceWasReportedMissing = detail.Readiness.MissingCapabilities.Contains(
            "Evidence",
            StringComparer.OrdinalIgnoreCase);
        if (!evidenceIsRequired && !evidenceWasClaimed && !evidenceWasReportedMissing)
        {
            return detail;
        }

        var suppliedEvidence = CollectRetainedEvidence(detail);
        var completeEvidence = CompleteEvidenceForSubject(detail.Subject.SecurityId, suppliedEvidence);
        var ready = detail.Readiness.ReadyCapabilities
            .Where(static capability =>
                !string.Equals(capability, "Evidence", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(capability, "Readiness", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (completeEvidence.Count > 0 && evidenceIsRequired)
        {
            ready.Add("Evidence");
        }

        var missingPrerequisites = detail.Subject.OperationalProfile
            .Where(static capability => !string.Equals(capability, "Readiness", StringComparison.OrdinalIgnoreCase))
            .Except(ready, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingPrerequisites.Length == 0 &&
            detail.Subject.OperationalProfile.Contains("Readiness", StringComparer.OrdinalIgnoreCase))
        {
            ready.Add("Readiness");
        }

        var missing = detail.Subject.OperationalProfile
            .Except(ready, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var warnings = detail.Readiness.Warnings
            .Where(static warning =>
                !warning.Contains("Evidence projection has not been published", StringComparison.OrdinalIgnoreCase) &&
                !warning.Contains("Attach complete accepted retained evidence", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (evidenceIsRequired && completeEvidence.Count == 0)
        {
            warnings.Add(suppliedEvidence.Count == 0
                ? MissingRetainedEvidenceWarning
                : "Retained evidence is incomplete or outside this Security Master subject scope. Attach accepted evidence with a SHA-256 content hash, source reference, reviewer, retention metadata, and the matching SecurityId.");
        }

        var readiness = detail.Readiness with
        {
            Status = missing.Length == 0 ? "Ready" : "ReviewRequired",
            ReadyCapabilities = ready,
            MissingCapabilities = missing,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
            RetainedEvidence = completeEvidence
        };
        return detail with { Readiness = readiness };
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
        var latestRun = projectionRuns.MaxBy(static run => run.GeneratedAt);
        var projectionAsOf = latestRun?.ProjectionAsOf
            ?? projectedCashFlows.MinBy(static flow => flow.DueDate)?.DueDate
            ?? terms.MaxBy(static row => row.EffectiveDate)?.EffectiveDate
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
                BondIdentity(subject.SecurityId, $"timeline-flow:{flow.ProjectedCashFlowId:N}"),
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
                BuildTimelineFlowPayload(flow, reconciliation, activity, ledgerProjection))
            {
                FormulaTrace = BuildCashFlowFormulaTrace(flow),
                LedgerReference = ledgerProjection?.LedgerReferenceId,
                NextAction = ResolveTimelineNextAction(status, ledgerProjection),
                RetainedEvidence = flow.RetainedEvidence
                    .Concat(reconciliation?.RetainedEvidence ?? [])
                    .Concat(activity?.RetainedEvidence ?? [])
                    .Concat(ledgerProjection?.RetainedEvidence ?? [])
                    .DistinctBy(static evidence => evidence.EvidenceId, StringComparer.Ordinal)
                    .ToArray()
            });
        }

        events.AddRange(AssetObligationProjectionService.BuildProjectedObligationEvents(
            subject,
            terms,
            projectionAsOf,
            ledgerProjections));

        foreach (var lifecycle in lifecycleEvents.OrderBy(static row => row.EffectiveDate))
        {
            events.Add(new AssetTermsObligationTimelineEventDto(
                BondIdentity(subject.SecurityId, $"timeline-lifecycle:{lifecycle.LifecycleEventId:N}"),
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
                lifecycle.ExtensionPayload)
            {
                FormulaTrace = "Lifecycle event retained from source domain.",
                NextAction = lifecycle.LifecycleState.Contains("Inactive", StringComparison.OrdinalIgnoreCase)
                    ? "Review inactive subject before downstream support consumes it."
                    : "No action required unless source evidence changes.",
                RetainedEvidence = lifecycle.RetainedEvidence
            });
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

        var variances = AssetObligationProjectionService.BuildTimelineVariances(
            subject,
            projectedCashFlows,
            actualActivity,
            reconciliationResults,
            projectionAsOf);
        var retainedEvidence = terms.SelectMany(static row => row.RetainedEvidence)
            .Concat(lifecycleEvents.SelectMany(static row => row.RetainedEvidence))
            .Concat(projectionRuns.SelectMany(static row => row.RetainedEvidence))
            .Concat(projectedCashFlows.SelectMany(static row => row.RetainedEvidence))
            .Concat(actualActivity.SelectMany(static row => row.RetainedEvidence))
            .Concat(reconciliationResults.SelectMany(static row => row.RetainedEvidence))
            .Concat(ledgerProjections.SelectMany(static row => row.RetainedEvidence))
            .Concat(events.SelectMany(static row => row.RetainedEvidence))
            .Concat(variances.SelectMany(static row => row.RetainedEvidence))
            .DistinctBy(static evidence => evidence.EvidenceId, StringComparer.Ordinal)
            .ToArray();
        var completeEvidence = CompleteEvidenceForSubject(subject.SecurityId, retainedEvidence);
        if (completeEvidence.Count == 0)
        {
            warnings.Add("Timeline rows are Expected/Projected support only. Attach complete accepted retained evidence before treating the timeline as ready.");
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
            latestRun?.SourceEntityId ?? subject.SecurityId.ToString("D"))
        {
            AssetClass = subject.AssetClass,
            Variances = variances,
            RetainedEvidence = completeEvidence
        };
    }

    private static decimal RoundCash(decimal amount)
        => decimal.Round(amount, 4, MidpointRounding.AwayFromZero);

    private static AssetReconciliationResultDto? FindReconciliation(
        AssetProjectedCashFlowDto flow,
        IReadOnlyList<AssetReconciliationResultDto> reconciliationResults)
    {
        AssetReconciliationResultDto? firstDateMatch = null;
        foreach (var result in reconciliationResults)
        {
            if (result.ExpectedDate != flow.DueDate)
            {
                continue;
            }

            firstDateMatch ??= result;
            if (AmountMatches(result.ExpectedAmount, flow.Amount))
            {
                return result;
            }
        }

        return firstDateMatch;
    }

    private static bool AmountMatches(decimal? expectedAmount, decimal actualAmount)
        => expectedAmount.HasValue && RoundCash(expectedAmount.Value) == RoundCash(actualAmount);

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
            .Where(row => string.Equals(row.Currency, flow.Currency, StringComparison.OrdinalIgnoreCase))
            .MinBy(row => Math.Abs(row.AccountingDate.DayNumber - flow.DueDate.DayNumber));

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

    private static string BuildCashFlowFormulaTrace(AssetProjectedCashFlowDto flow)
    {
        if (flow.ExtensionPayload is { } payload && payload.ValueKind == JsonValueKind.Object)
        {
            if (TryReadPayloadString(payload, "formula", out var formula))
            {
                return formula;
            }

            if (TryReadPayloadString(payload, "type", out var type))
            {
                return $"{flow.FlowType}: {type}";
            }
        }

        if (flow.PrincipalBasis.HasValue && flow.AnnualRate.HasValue && flow.AccrualStartDate.HasValue && flow.AccrualEndDate.HasValue)
        {
            return $"{flow.FlowType}: principal {flow.PrincipalBasis.Value} x rate {flow.AnnualRate.Value:P4} over {flow.AccrualStartDate:yyyy-MM-dd} to {flow.AccrualEndDate:yyyy-MM-dd}.";
        }

        return $"{flow.FlowType}: amount retained from {flow.SourceDomain ?? "AssetOperations"} projection.";
    }

    private static string ResolveTimelineNextAction(string status, AssetLedgerProjectionDto? ledgerProjection)
    {
        if (string.Equals(status, "Matched", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Reconciled", StringComparison.OrdinalIgnoreCase))
        {
            return ledgerProjection is null
                ? "Review ledger support before close."
                : "No action required unless evidence changes.";
        }

        if (string.Equals(status, "MissingEvidence", StringComparison.OrdinalIgnoreCase))
        {
            return "Attach actual activity evidence or open a reconciliation case.";
        }

        return ledgerProjection is null
            ? "Review ledger classification before posting support."
            : "Monitor due date and retain actual evidence when it arrives.";
    }

    private static bool TryReadPayloadString(JsonElement payload, string propertyName, out string value)
    {
        foreach (var property in payload.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }

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

    private static JsonElement BuildDirectLendingTermsPayload(LoanContractDetailDto contract, DirectLendingTermsDto terms)
        => JsonSerializer.SerializeToElement(new
        {
            engineVersion = AssetObligationProjectionService.EngineVersion,
            assetClass = "DirectLoan",
            currency = terms.BaseCurrency.ToString(),
            baseCurrency = terms.BaseCurrency.ToString(),
            contract.LoanId,
            contract.FacilityName,
            borrowerName = contract.Borrower.BorrowerName,
            terms.OriginationDate,
            terms.MaturityDate,
            terms.CommitmentAmount,
            terms.RateTypeKind,
            rateType = terms.RateTypeKind.ToString(),
            terms.FixedAnnualRate,
            terms.InterestIndexName,
            terms.SpreadBps,
            terms.FloorRate,
            terms.CapRate,
            terms.DayCountBasis,
            dayCountBasis = terms.DayCountBasis.ToString(),
            terms.PaymentFrequency,
            paymentFrequency = terms.PaymentFrequency.ToString(),
            terms.AmortizationType,
            amortizationType = terms.AmortizationType.ToString(),
            terms.CommitmentFeeRate,
            terms.DefaultRateSpreadBps,
            terms.PrepaymentAllowed,
            terms.CovenantsJson,
            terms.InterestOnlyMonths,
            terms.GracePeriodDays,
            terms.EffectiveRateFloor,
            terms.EffectiveRateCap,
            terms.PrepaymentPenaltyRate,
            securityMasterReference = terms.SecurityMasterReference
        });

    internal static IReadOnlyList<string> ReadyCapabilities(
        IReadOnlyList<string> capabilities,
        IReadOnlyList<AssetTermsVersionDto> terms,
        IReadOnlyList<AssetLifecycleEventDto> lifecycle,
        IReadOnlyList<AssetProjectedCashFlowDto> projectedCashFlows,
        IReadOnlyList<AssetActualActivityDto> actualActivity,
        IReadOnlyList<AssetReconciliationRunDto> reconciliationRuns,
        IReadOnlyList<AssetReconciliationResultDto> reconciliationResults,
        IReadOnlyList<AssetLedgerProjectionDto> ledgerProjections,
        IReadOnlyList<RetainedEvidenceIdentityDto>? retainedEvidence = null,
        Guid? evidenceSubjectId = null)
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
        if (evidenceSubjectId is Guid subjectId &&
            CompleteEvidenceForSubject(subjectId, retainedEvidence ?? []).Count > 0)
        {
            ready.Add("Evidence");
        }

        return ready
            .Where(capability => capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static AssetOperationsReadinessDto BuildReadiness(
        AssetOperationSubjectDto subject,
        IReadOnlyList<string> readyCapabilities,
        string sourceDomain,
        string? sourceEntityId,
        IReadOnlyList<RetainedEvidenceIdentityDto>? retainedEvidence = null,
        IReadOnlyList<string>? additionalWarnings = null,
        bool allowReady = true,
        DateTimeOffset? evaluatedAt = null)
    {
        var ready = readyCapabilities
            .Where(capability =>
                subject.OperationalProfile.Contains(capability, StringComparer.OrdinalIgnoreCase) &&
                !string.Equals(capability, "Readiness", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var missingPrerequisites = subject.OperationalProfile
            .Where(static capability => !string.Equals(capability, "Readiness", StringComparison.OrdinalIgnoreCase))
            .Except(ready, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (allowReady &&
            missingPrerequisites.Length == 0 &&
            subject.OperationalProfile.Contains("Readiness", StringComparer.OrdinalIgnoreCase))
        {
            ready.Add("Readiness");
        }

        var missing = subject.OperationalProfile
            .Except(ready, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var warnings = missing
            .Select(BuildMissingCapabilityWarning)
            .Concat(additionalWarnings ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var completeEvidence = CompleteEvidenceForSubject(subject.SecurityId, retainedEvidence ?? []);
        return new AssetOperationsReadinessDto(
            subject.SecurityId,
            allowReady && missing.Length == 0 ? "Ready" : "ReviewRequired",
            subject.OperationalProfile,
            ready,
            missing,
            warnings,
            evaluatedAt ?? DateTimeOffset.UtcNow,
            sourceDomain,
            sourceEntityId)
        {
            RetainedEvidence = completeEvidence
        };
    }

    private const string MissingRetainedEvidenceWarning =
        "Attach complete accepted retained evidence with a SHA-256 content hash, source reference, reviewer, retention metadata, and the matching SecurityId before treating this asset as ready.";

    private static string BuildMissingCapabilityWarning(string capability)
        => capability switch
        {
            "Evidence" => MissingRetainedEvidenceWarning,
            "ActualActivity" => "Retain actual activity or an approved no-activity assertion for the readiness period.",
            "Reconciliation" => "Publish reconciliation results or an approved no-activity reconciliation for this asset.",
            "Readiness" => "Resolve the listed readiness prerequisites before treating this asset as ready.",
            _ => $"Publish reviewed {capability} support for this asset."
        };

    private static IReadOnlyList<RetainedEvidenceIdentityDto> CompleteEvidenceForSubject(
        Guid securityId,
        IEnumerable<RetainedEvidenceIdentityDto> evidence)
        => evidence
            .Where(RetainedEvidenceIdentityValidator.IsComplete)
            .Where(item => Guid.TryParse(item.SubjectId, out var subjectId) && subjectId == securityId)
            .DistinctBy(static item => item.EvidenceId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<RetainedEvidenceIdentityDto> CollectRetainedEvidence(AssetOperationsDetailDto detail)
    {
        var evidence = new List<RetainedEvidenceIdentityDto>();
        Add(evidence, detail.RetainedEvidence);
        Add(evidence, detail.Readiness.RetainedEvidence);
        foreach (var row in detail.TermsHistory)
            Add(evidence, row.RetainedEvidence);
        foreach (var row in detail.LifecycleEvents)
            Add(evidence, row.RetainedEvidence);
        foreach (var row in detail.CashFlowProjectionRuns)
            Add(evidence, row.RetainedEvidence);
        foreach (var row in detail.ProjectedCashFlows)
            Add(evidence, row.RetainedEvidence);
        foreach (var row in detail.ActualActivity)
            Add(evidence, row.RetainedEvidence);
        foreach (var row in detail.ReconciliationRuns)
            Add(evidence, row.RetainedEvidence);
        foreach (var row in detail.ReconciliationResults)
            Add(evidence, row.RetainedEvidence);
        foreach (var row in detail.LedgerProjections)
            Add(evidence, row.RetainedEvidence);
        foreach (var row in detail.InstrumentRoles)
        {
            Add(evidence, row.RetainedEvidence);
            Add(evidence, row.OriginEvent?.RetainedEvidence);
        }
        foreach (var row in detail.BookPositions)
        {
            Add(evidence, row.RetainedEvidence);
            Add(evidence, row.CurrentEconomicState?.RetainedEvidence);
            Add(evidence, row.OriginEvent?.RetainedEvidence);
            Add(evidence, row.ProjectionLineage?.RetainedEvidence);
        }
        foreach (var row in detail.PositionEconomicStates)
        {
            Add(evidence, row.RetainedEvidence);
            Add(evidence, row.SourceEvent?.RetainedEvidence);
            Add(evidence, row.ProjectionLineage?.RetainedEvidence);
        }
        foreach (var row in detail.ProjectionLineages)
        {
            Add(evidence, row.RetainedEvidence);
            Add(evidence, row.TriggerEvent.RetainedEvidence);
        }
        if (detail.TermsObligationsTimeline is { } timeline)
        {
            Add(evidence, timeline.RetainedEvidence);
            foreach (var row in timeline.Events)
                Add(evidence, row.RetainedEvidence);
            foreach (var row in timeline.Variances)
                Add(evidence, row.RetainedEvidence);
        }

        return evidence;

        static void Add(
            ICollection<RetainedEvidenceIdentityDto> target,
            IEnumerable<RetainedEvidenceIdentityDto>? source)
        {
            foreach (var item in source ?? [])
            {
                if (item is not null)
                    target.Add(item);
            }
        }
    }
}
