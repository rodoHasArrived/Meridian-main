using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Application.Services;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Composes the selected-security trust snapshot used by the Security Master workstation.
/// The endpoint is selection-scoped and additive, so the heavier downstream checks only run
/// when the operator explicitly loads or refreshes a security drill-in.
/// </summary>
public sealed class SecurityMasterWorkbenchQueryService : ISecurityMasterWorkbenchQueryService
{
    private readonly Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService _queryService;
    private readonly ISecurityMasterConflictService _conflictService;
    private readonly ISecurityMasterIngestStatusService _ingestStatusService;
    private readonly IStrategyRepository _strategyRepository;
    private readonly PortfolioReadService _portfolioReadService;
    private readonly LedgerReadService _ledgerReadService;
    private readonly IReconciliationRunService? _reconciliationRunService;
    private readonly ReportGenerationService _reportGenerationService;

    public SecurityMasterWorkbenchQueryService(
        Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService queryService,
        ISecurityMasterConflictService conflictService,
        ISecurityMasterIngestStatusService ingestStatusService,
        IStrategyRepository strategyRepository,
        PortfolioReadService portfolioReadService,
        LedgerReadService ledgerReadService,
        ReportGenerationService reportGenerationService,
        IReconciliationRunService? reconciliationRunService = null)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _conflictService = conflictService ?? throw new ArgumentNullException(nameof(conflictService));
        _ingestStatusService = ingestStatusService ?? throw new ArgumentNullException(nameof(ingestStatusService));
        _strategyRepository = strategyRepository ?? throw new ArgumentNullException(nameof(strategyRepository));
        _portfolioReadService = portfolioReadService ?? throw new ArgumentNullException(nameof(portfolioReadService));
        _ledgerReadService = ledgerReadService ?? throw new ArgumentNullException(nameof(ledgerReadService));
        _reportGenerationService = reportGenerationService ?? throw new ArgumentNullException(nameof(reportGenerationService));
        _reconciliationRunService = reconciliationRunService;
    }

    public async Task<SecurityMasterTrustSnapshotDto?> GetTrustSnapshotAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var detailTask = _queryService.GetByIdAsync(securityId, ct);
        var historyTask = _queryService.GetHistoryAsync(new SecurityHistoryRequest(securityId, 50), ct);
        var economicTask = _queryService.GetEconomicDefinitionByIdAsync(securityId, ct);
        var tradingTask = _queryService.GetTradingParametersAsync(securityId, DateTimeOffset.UtcNow, ct);
        var corporateActionsTask = _queryService.GetCorporateActionsAsync(securityId, ct);
        var conflictsTask = _conflictService.GetOpenConflictsAsync(ct);

        await Task.WhenAll(
            detailTask,
            historyTask,
            economicTask,
            tradingTask,
            corporateActionsTask,
            conflictsTask).ConfigureAwait(false);

        var detail = await detailTask.ConfigureAwait(false);
        if (detail is null)
        {
            return null;
        }

        var history = (await historyTask.ConfigureAwait(false))
            .OrderByDescending(static item => item.EventTimestamp)
            .ToArray();
        var economic = await economicTask.ConfigureAwait(false);
        var trading = await tradingTask.ConfigureAwait(false);
        var corporateActions = (await corporateActionsTask.ConfigureAwait(false))
            .OrderByDescending(static action => action.ExDate)
            .ToArray();
        var selectedConflicts = (await conflictsTask.ConfigureAwait(false))
            .Where(conflict =>
                conflict.SecurityId == securityId &&
                string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static conflict => conflict.DetectedAt)
            .ToArray();

        var winningSource = ParseWinningSource(economic?.Provenance);
        var downstreamImpact = await BuildDownstreamImpactAsync(detail, fundProfileId, ct).ConfigureAwait(false);
        var assessments = selectedConflicts
            .Select(conflict => AssessConflict(conflict, detail, economic, trading, winningSource, downstreamImpact))
            .ToArray();
        var trustPosture = BuildTrustPosture(economic, trading, corporateActions, assessments, winningSource);
        var provenanceCandidates = BuildProvenanceCandidates(detail, winningSource, assessments);
        var recommendedActions = BuildRecommendedActions(detail, trustPosture, assessments, downstreamImpact);

        return new SecurityMasterTrustSnapshotDto(
            SecurityId: detail.SecurityId,
            Security: MapToWorkstationSecurity(detail, economic),
            Identity: new SecurityIdentityDrillInDto(
                SecurityId: detail.SecurityId,
                DisplayName: detail.DisplayName,
                AssetClass: detail.AssetClass,
                Status: detail.Status,
                Version: detail.Version,
                EffectiveFrom: detail.EffectiveFrom,
                EffectiveTo: detail.EffectiveTo,
                Identifiers: detail.Identifiers,
                Aliases: detail.Aliases),
            EconomicDefinition: MapToEconomicDefinition(detail, economic, winningSource),
            TrustPosture: trustPosture,
            ProvenanceCandidates: provenanceCandidates,
            ConflictAssessments: assessments,
            DownstreamImpact: downstreamImpact,
            RecommendedActions: recommendedActions,
            History: history,
            CorporateActions: corporateActions,
            RetrievedAtUtc: DateTimeOffset.UtcNow);
    }

    public async Task<BulkResolveSecurityMasterConflictsResult> BulkResolveConflictsAsync(
        BulkResolveSecurityMasterConflictsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var requestedConflictIds = request.ConflictIds?
            .Where(static conflictId => conflictId != Guid.Empty)
            .Distinct()
            .ToArray()
            ?? [];

        if (requestedConflictIds.Length == 0)
        {
            return new BulkResolveSecurityMasterConflictsResult(
                Requested: 0,
                Eligible: 0,
                Resolved: 0,
                Skipped: 0,
                ResolvedConflictIds: [],
                SkippedReasons: new Dictionary<Guid, string>());
        }

        var openConflicts = await _conflictService.GetOpenConflictsAsync(ct).ConfigureAwait(false);
        var openConflictMap = openConflicts
            .Where(conflict => string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(static conflict => conflict.ConflictId, static conflict => conflict);

        var resolvedConflictIds = new List<Guid>(requestedConflictIds.Length);
        var skippedReasons = new Dictionary<Guid, string>();
        var contextCache = new Dictionary<Guid, SecurityWorkbenchContext>();
        var eligibleCount = 0;

        foreach (var conflictId in requestedConflictIds)
        {
            ct.ThrowIfCancellationRequested();

            if (!openConflictMap.TryGetValue(conflictId, out var conflict))
            {
                skippedReasons[conflictId] = "Conflict is not open or no longer exists.";
                continue;
            }

            if (!contextCache.TryGetValue(conflict.SecurityId, out var context))
            {
                var loadedContext = await LoadContextAsync(conflict.SecurityId, request.FundProfileId, ct).ConfigureAwait(false);
                if (loadedContext is null)
                {
                    skippedReasons[conflictId] = "Security snapshot could not be loaded for bulk review.";
                    continue;
                }

                context = loadedContext;
                contextCache[conflict.SecurityId] = context;
            }

            var assessment = AssessConflict(
                conflict,
                context.Detail,
                context.EconomicDefinition,
                context.TradingParameters,
                context.WinningSource,
                context.DownstreamImpact);

            if (!assessment.IsBulkEligible)
            {
                skippedReasons[conflictId] = assessment.BulkIneligibilityReason ?? "Conflict does not meet the low-risk bulk policy.";
                continue;
            }

            eligibleCount++;
            var updated = await _conflictService
                .ResolveAsync(
                    new ResolveConflictRequest(
                        ConflictId: conflictId,
                        Resolution: assessment.RecommendedResolution,
                        ResolvedBy: request.ResolvedBy,
                        Reason: request.Reason),
                    ct)
                .ConfigureAwait(false);

            if (updated is null)
            {
                skippedReasons[conflictId] = "Conflict could not be resolved by the server.";
                continue;
            }

            resolvedConflictIds.Add(conflictId);
        }

        return new BulkResolveSecurityMasterConflictsResult(
            Requested: requestedConflictIds.Length,
            Eligible: eligibleCount,
            Resolved: resolvedConflictIds.Count,
            Skipped: requestedConflictIds.Length - resolvedConflictIds.Count,
            ResolvedConflictIds: resolvedConflictIds,
            SkippedReasons: skippedReasons);
    }

    private async Task<SecurityWorkbenchContext?> LoadContextAsync(
        Guid securityId,
        string? fundProfileId,
        CancellationToken ct)
    {
        var detailTask = _queryService.GetByIdAsync(securityId, ct);
        var economicTask = _queryService.GetEconomicDefinitionByIdAsync(securityId, ct);
        var tradingTask = _queryService.GetTradingParametersAsync(securityId, DateTimeOffset.UtcNow, ct);

        await Task.WhenAll(detailTask, economicTask, tradingTask).ConfigureAwait(false);

        var detail = await detailTask.ConfigureAwait(false);
        if (detail is null)
        {
            return null;
        }

        var economic = await economicTask.ConfigureAwait(false);
        var trading = await tradingTask.ConfigureAwait(false);
        var winningSource = ParseWinningSource(economic?.Provenance);
        var downstreamImpact = await BuildDownstreamImpactAsync(detail, fundProfileId, ct).ConfigureAwait(false);

        return new SecurityWorkbenchContext(detail, economic, trading, winningSource, downstreamImpact);
    }

    private async Task<SecurityMasterDownstreamImpactDto> BuildDownstreamImpactAsync(
        SecurityDetailDto detail,
        string? fundProfileId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fundProfileId))
        {
            return new SecurityMasterDownstreamImpactDto(
                FundProfileId: null,
                IsScoped: false,
                Severity: SecurityMasterImpactSeverity.Unknown,
                Summary: "Not scoped to a fund profile. Downstream impact is unknown.",
                PortfolioExposureSummary: "Portfolio impact is not scoped.",
                LedgerExposureSummary: "Ledger impact is not scoped.",
                ReconciliationExposureSummary: "Reconciliation impact is not scoped.",
                ReportPackExposureSummary: "Report-pack impact is not scoped.",
                MatchedRunCount: 0,
                PortfolioExposureCount: 0,
                LedgerExposureCount: 0,
                ReconciliationExposureCount: 0,
                ReportPackExposureCount: 0,
                Links: []);
        }

        var normalizedFundProfileId = fundProfileId.Trim();
        var relatedRuns = await LoadFundRunsAsync(normalizedFundProfileId, ct).ConfigureAwait(false);
        if (relatedRuns.Count == 0)
        {
            return new SecurityMasterDownstreamImpactDto(
                FundProfileId: normalizedFundProfileId,
                IsScoped: true,
                Severity: SecurityMasterImpactSeverity.None,
                Summary: $"Fund profile {normalizedFundProfileId} has no recorded runs in the workstation store.",
                PortfolioExposureSummary: "No scoped portfolio exposure detected.",
                LedgerExposureSummary: "No scoped ledger exposure detected.",
                ReconciliationExposureSummary: "No scoped reconciliation exposure detected.",
                ReportPackExposureSummary: "No scoped report-pack exposure detected.",
                MatchedRunCount: 0,
                PortfolioExposureCount: 0,
                LedgerExposureCount: 0,
                ReconciliationExposureCount: 0,
                ReportPackExposureCount: 0,
                Links: []);
        }

        var portfolioTasks = relatedRuns
            .Select(run => _portfolioReadService.BuildSummaryAsync(run, ct))
            .ToArray();
        var ledgerTasks = relatedRuns
            .Select(run => _ledgerReadService.BuildSummaryAsync(run, ct))
            .ToArray();

        var combinedTasks = portfolioTasks
            .Cast<Task>()
            .Concat(ledgerTasks)
            .ToArray();

        await Task.WhenAll(combinedTasks).ConfigureAwait(false);

        var portfolios = new List<PortfolioSummary?>(portfolioTasks.Length);
        foreach (var task in portfolioTasks)
        {
            portfolios.Add(await task.ConfigureAwait(false));
        }

        var ledgers = new List<LedgerSummary?>(ledgerTasks.Length);
        foreach (var task in ledgerTasks)
        {
            ledgers.Add(await task.ConfigureAwait(false));
        }

        var normalizedIdentifiers = BuildNormalizedIdentifierSet(detail);
        var portfolioRunCount = 0;
        var portfolioExposureCount = 0;

        foreach (var portfolio in portfolios.Where(static portfolio => portfolio is not null))
        {
            var matches = portfolio!.Positions.Count(position => MatchesSecurity(position.Symbol, position.Security?.SecurityId, detail, normalizedIdentifiers));
            if (matches == 0)
            {
                continue;
            }

            portfolioRunCount++;
            portfolioExposureCount += matches;
        }

        var ledgerRunCount = 0;
        var ledgerExposureCount = 0;
        foreach (var ledger in ledgers.Where(static ledger => ledger is not null))
        {
            var matches = ledger!.TrialBalance.Count(line => MatchesSecurity(line.Symbol, line.Security?.SecurityId, detail, normalizedIdentifiers));
            if (matches == 0)
            {
                continue;
            }

            ledgerRunCount++;
            ledgerExposureCount += matches;
        }

        var reconciliationRunCount = 0;
        var reconciliationExposureCount = 0;
        if (_reconciliationRunService is not null)
        {
            foreach (var run in relatedRuns)
            {
                ct.ThrowIfCancellationRequested();

                var detailResult = await _reconciliationRunService.GetLatestForRunAsync(run.RunId, ct).ConfigureAwait(false)
                    ?? await _reconciliationRunService.RunAsync(new ReconciliationRunRequest(run.RunId), ct).ConfigureAwait(false);
                if (detailResult?.SecurityCoverageIssues is null)
                {
                    continue;
                }

                var issueMatches = detailResult.SecurityCoverageIssues.Count(issue =>
                    MatchesSecurity(issue.Symbol, securityId: null, detail, normalizedIdentifiers));
                if (issueMatches == 0)
                {
                    continue;
                }

                reconciliationRunCount++;
                reconciliationExposureCount += issueMatches;
            }
        }

        var reportPackExposureCount = 0;
        if (relatedRuns.Any(run => run.Metrics?.Ledger is not null))
        {
            var report = await _reportGenerationService
                .GenerateAsync(
                    new ReportRequest(
                        FundId: normalizedFundProfileId,
                        AsOf: DateTimeOffset.UtcNow,
                        FundLedger: BuildFundLedgerBook(normalizedFundProfileId, relatedRuns)),
                    ct)
                .ConfigureAwait(false);

            reportPackExposureCount = report.TrialBalance.Count(row =>
                MatchesSecurity(row.Symbol, securityId: null, detail, normalizedIdentifiers));
        }

        var severity = DetermineImpactSeverity(
            portfolioExposureCount,
            ledgerExposureCount,
            reconciliationExposureCount,
            reportPackExposureCount);

        var links = new List<SecurityMasterImpactLinkDto>(4);
        if (portfolioExposureCount > 0)
        {
            links.Add(new SecurityMasterImpactLinkDto(
                Target: "portfolio",
                Label: "Open Portfolio Impact",
                Summary: $"{portfolioExposureCount} position(s) across {portfolioRunCount} run(s) reference this security.",
                Severity: SecurityMasterImpactSeverity.Low,
                IsActive: true));
        }

        if (ledgerExposureCount > 0)
        {
            links.Add(new SecurityMasterImpactLinkDto(
                Target: "ledger",
                Label: "Open Ledger Impact",
                Summary: $"{ledgerExposureCount} ledger line(s) across {ledgerRunCount} run(s) reference this security.",
                Severity: SecurityMasterImpactSeverity.Medium,
                IsActive: true));
        }

        if (reconciliationExposureCount > 0)
        {
            links.Add(new SecurityMasterImpactLinkDto(
                Target: "reconciliation",
                Label: "Open Reconciliation Impact",
                Summary: $"{reconciliationExposureCount} reconciliation issue(s) across {reconciliationRunCount} run(s) reference this security.",
                Severity: SecurityMasterImpactSeverity.High,
                IsActive: true));
        }

        if (reportPackExposureCount > 0)
        {
            links.Add(new SecurityMasterImpactLinkDto(
                Target: "reportPack",
                Label: "Open Report Pack Impact",
                Summary: $"{reportPackExposureCount} report-pack row(s) reference this security in the current fund scope.",
                Severity: SecurityMasterImpactSeverity.High,
                IsActive: true));
        }

        return new SecurityMasterDownstreamImpactDto(
            FundProfileId: normalizedFundProfileId,
            IsScoped: true,
            Severity: severity,
            Summary: BuildImpactSummary(relatedRuns.Count, portfolioExposureCount, ledgerExposureCount, reconciliationExposureCount, reportPackExposureCount),
            PortfolioExposureSummary: portfolioExposureCount == 0
                ? "No scoped portfolio exposure detected."
                : $"{portfolioExposureCount} position(s) across {portfolioRunCount} run(s) reference this security.",
            LedgerExposureSummary: ledgerExposureCount == 0
                ? "No scoped ledger exposure detected."
                : $"{ledgerExposureCount} ledger line(s) across {ledgerRunCount} run(s) reference this security.",
            ReconciliationExposureSummary: reconciliationExposureCount == 0
                ? "No scoped reconciliation exposure detected."
                : $"{reconciliationExposureCount} reconciliation issue(s) across {reconciliationRunCount} run(s) reference this security.",
            ReportPackExposureSummary: reportPackExposureCount == 0
                ? "No scoped report-pack exposure detected."
                : $"{reportPackExposureCount} report-pack row(s) reference this security.",
            MatchedRunCount: relatedRuns.Count,
            PortfolioExposureCount: portfolioExposureCount,
            LedgerExposureCount: ledgerExposureCount,
            ReconciliationExposureCount: reconciliationExposureCount,
            ReportPackExposureCount: reportPackExposureCount,
            Links: links);
    }

    private async Task<IReadOnlyList<StrategyRunEntry>> LoadFundRunsAsync(string fundProfileId, CancellationToken ct)
    {
        var runs = new List<StrategyRunEntry>();
        await foreach (var run in _strategyRepository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (string.Equals(run.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            {
                runs.Add(run);
            }
        }

        return runs
            .OrderByDescending(static run => run.StartedAt)
            .ThenBy(static run => run.RunId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static FundLedgerBook BuildFundLedgerBook(string fundProfileId, IReadOnlyList<StrategyRunEntry> runs)
    {
        var ledgerBook = new FundLedgerBook(fundProfileId);
        foreach (var run in runs)
        {
            foreach (var journalEntry in run.Metrics?.Ledger?.Journal ?? [])
            {
                ledgerBook.FundLedger.Post(journalEntry);
            }
        }

        return ledgerBook;
    }

    private static SecurityMasterConflictAssessmentDto AssessConflict(
        SecurityMasterConflict conflict,
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        TradingParametersDto? trading,
        WinningSourceInfo? winningSource,
        SecurityMasterDownstreamImpactDto downstreamImpact)
    {
        var currentValue = ExtractCurrentFieldValue(conflict.FieldPath, detail, economic, trading);
        var winningSide = ResolveWinningSide(conflict, currentValue, winningSource);

        var currentWinningValue = winningSide switch
        {
            ConflictSide.ProviderB => conflict.ValueB,
            _ => conflict.ValueA
        };
        var challengerValue = winningSide switch
        {
            ConflictSide.ProviderB => conflict.ValueA,
            _ => conflict.ValueB
        };
        var currentWinningSource = winningSide switch
        {
            ConflictSide.ProviderB => conflict.ProviderB,
            _ => conflict.ProviderA
        };
        var challengerSource = winningSide switch
        {
            ConflictSide.ProviderB => conflict.ProviderA,
            _ => conflict.ProviderB
        };
        var recommendation = BuildRecommendation(currentWinningValue, challengerValue);
        var recommendedResolution = recommendation switch
        {
            SecurityMasterConflictRecommendationKind.DismissAsEquivalent => "Dismiss",
            SecurityMasterConflictRecommendationKind.Challenger => winningSide == ConflictSide.ProviderB ? "AcceptA" : "AcceptB",
            SecurityMasterConflictRecommendationKind.PreserveWinner => winningSide == ConflictSide.ProviderB ? "AcceptB" : "AcceptA",
            _ => string.Empty
        };

        var impactSeverity = DetermineConflictImpactSeverity(conflict, downstreamImpact);
        var impactSummary = BuildConflictImpactSummary(conflict.FieldPath, impactSeverity, downstreamImpact);
        var impactDetail = BuildConflictImpactDetail(conflict.FieldPath, impactSeverity, downstreamImpact);
        var winnerBlank = string.IsNullOrWhiteSpace(currentWinningValue);
        var challengerPresent = !string.IsNullOrWhiteSpace(challengerValue);
        var isOpen = string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase);
        var isBulkEligible = isOpen
            && impactSeverity is SecurityMasterImpactSeverity.None or SecurityMasterImpactSeverity.Low
            && (recommendation == SecurityMasterConflictRecommendationKind.DismissAsEquivalent
                || (winnerBlank && challengerPresent));

        var bulkIneligibilityReason = isBulkEligible
            ? null
            : !isOpen
                ? "Conflict is not open."
                : recommendation == SecurityMasterConflictRecommendationKind.ManualReview
                    ? "Conflict requires manual review."
                    : impactSeverity is SecurityMasterImpactSeverity.Unknown
                        ? "Downstream impact is not scoped."
                        : $"Impact severity is {impactSeverity}.";

        var recommendedWinner = recommendation switch
        {
            SecurityMasterConflictRecommendationKind.DismissAsEquivalent =>
                $"{currentWinningSource} and {challengerSource} normalize to the same value.",
            SecurityMasterConflictRecommendationKind.Challenger =>
                $"Accept {challengerSource} because the current winning value is blank.",
            SecurityMasterConflictRecommendationKind.PreserveWinner =>
                $"Preserve {currentWinningSource} as the current winner.",
            _ =>
                $"Manual review required between {currentWinningSource} and {challengerSource}."
        };

        return new SecurityMasterConflictAssessmentDto(
            Conflict: conflict,
            CurrentWinningValue: currentWinningValue,
            ChallengerValue: challengerValue,
            CurrentWinningSource: currentWinningSource,
            ChallengerSource: challengerSource,
            Recommendation: recommendation,
            RecommendedResolution: recommendedResolution,
            RecommendedWinner: recommendedWinner,
            ImpactSeverity: impactSeverity,
            ImpactSummary: impactSummary,
            ImpactDetail: impactDetail,
            IsBulkEligible: isBulkEligible,
            BulkIneligibilityReason: bulkIneligibilityReason);
    }

    private static SecurityMasterConflictRecommendationKind BuildRecommendation(string? currentWinningValue, string? challengerValue)
    {
        if (AreEquivalent(currentWinningValue, challengerValue))
        {
            return SecurityMasterConflictRecommendationKind.DismissAsEquivalent;
        }

        if (string.IsNullOrWhiteSpace(currentWinningValue) && !string.IsNullOrWhiteSpace(challengerValue))
        {
            return SecurityMasterConflictRecommendationKind.Challenger;
        }

        if (!string.IsNullOrWhiteSpace(currentWinningValue) && !string.IsNullOrWhiteSpace(challengerValue))
        {
            return SecurityMasterConflictRecommendationKind.PreserveWinner;
        }

        return SecurityMasterConflictRecommendationKind.ManualReview;
    }

    private static ConflictSide ResolveWinningSide(
        SecurityMasterConflict conflict,
        string? currentValue,
        WinningSourceInfo? winningSource)
    {
        var normalizedCurrentValue = NormalizeComparableString(currentValue);
        if (!string.IsNullOrWhiteSpace(normalizedCurrentValue))
        {
            var matchesA = string.Equals(NormalizeComparableString(conflict.ValueA), normalizedCurrentValue, StringComparison.Ordinal);
            var matchesB = string.Equals(NormalizeComparableString(conflict.ValueB), normalizedCurrentValue, StringComparison.Ordinal);

            if (matchesA && !matchesB)
            {
                return ConflictSide.ProviderA;
            }

            if (matchesB && !matchesA)
            {
                return ConflictSide.ProviderB;
            }
        }

        if (!string.IsNullOrWhiteSpace(winningSource?.SourceSystem))
        {
            var sourceMatchesA = string.Equals(winningSource.SourceSystem, conflict.ProviderA, StringComparison.OrdinalIgnoreCase);
            var sourceMatchesB = string.Equals(winningSource.SourceSystem, conflict.ProviderB, StringComparison.OrdinalIgnoreCase);
            if (sourceMatchesA && !sourceMatchesB)
            {
                return ConflictSide.ProviderA;
            }

            if (sourceMatchesB && !sourceMatchesA)
            {
                return ConflictSide.ProviderB;
            }
        }

        return ConflictSide.ProviderA;
    }

    private static SecurityMasterTrustPostureDto BuildTrustPosture(
        SecurityEconomicDefinitionRecord? economic,
        TradingParametersDto? trading,
        IReadOnlyList<CorporateActionDto> corporateActions,
        IReadOnlyList<SecurityMasterConflictAssessmentDto> assessments,
        WinningSourceInfo? winningSource)
    {
        var missingTradingFields = GetMissingTradingParameterFields(economic?.AssetClass, trading);
        var openConflictCount = assessments.Count;
        var upcomingCorporateActions = corporateActions
            .Where(action => action.ExDate >= DateOnly.FromDateTime(DateTime.UtcNow))
            .OrderBy(static action => action.ExDate)
            .Take(5)
            .ToArray();
        var corporateActionsTrusted = upcomingCorporateActions.Length == 0;

        var tone = openConflictCount > 0
            ? SecurityMasterTrustTone.Blocked
            : missingTradingFields.Count > 0 || !corporateActionsTrusted
                ? SecurityMasterTrustTone.Review
                : SecurityMasterTrustTone.Trusted;

        var trustScore = Math.Clamp(
            100
            - (openConflictCount * 25)
            - (missingTradingFields.Count * 10)
            - (upcomingCorporateActions.Length > 0 ? 10 : 0),
            0,
            100);

        var summary = tone switch
        {
            SecurityMasterTrustTone.Blocked =>
                $"Trust is blocked by {openConflictCount} open conflict{(openConflictCount == 1 ? string.Empty : "s")}.",
            SecurityMasterTrustTone.Review when missingTradingFields.Count > 0 =>
                $"Golden copy is stable, but trading readiness is incomplete: {string.Join(", ", missingTradingFields)}.",
            SecurityMasterTrustTone.Review =>
                "Golden copy is stable, but upcoming corporate actions still require operator review.",
            SecurityMasterTrustTone.Trusted =>
                "Golden copy is trusted for downstream governance workflows.",
            _ =>
                "Trust posture is unavailable."
        };

        var corporateActionReadiness = upcomingCorporateActions.Length == 0
            ? "No upcoming corporate actions are scheduled in the current review window."
            : upcomingCorporateActions.Length == 1
                ? $"Upcoming {upcomingCorporateActions[0].EventType} on {upcomingCorporateActions[0].ExDate:yyyy-MM-dd} should be reviewed before downstream close."
                : $"{upcomingCorporateActions.Length} upcoming corporate actions should be reviewed before downstream close.";

        return new SecurityMasterTrustPostureDto(
            Tone: tone,
            TrustScore: trustScore,
            Summary: summary,
            GoldenCopySource: winningSource?.SourceSystem ?? "Unknown source",
            GoldenCopyRule: "Preserve winner unless the current winner is blank or the values are equivalent.",
            TradingParametersStatus: missingTradingFields.Count == 0
                ? trading is null
                    ? "Trading parameters could not be confirmed from the query surface."
                    : $"Trading parameters complete as of {trading.AsOf.LocalDateTime:g}."
                : $"Trading parameters incomplete: missing {string.Join(", ", missingTradingFields)}.",
            CorporateActionReadiness: corporateActionReadiness,
            HasOpenConflicts: openConflictCount > 0,
            OpenConflictCount: openConflictCount,
            TradingParametersComplete: missingTradingFields.Count == 0,
            HasUpcomingCorporateActions: upcomingCorporateActions.Length > 0,
            CorporateActionsTrusted: corporateActionsTrusted);
    }

    private static IReadOnlyList<SecurityMasterSourceCandidateDto> BuildProvenanceCandidates(
        SecurityDetailDto detail,
        WinningSourceInfo? winningSource,
        IReadOnlyList<SecurityMasterConflictAssessmentDto> assessments)
    {
        var candidates = new List<SecurityMasterSourceCandidateDto>(assessments.Count + 1);
        if (winningSource is not null)
        {
            candidates.Add(new SecurityMasterSourceCandidateDto(
                ConflictId: null,
                FieldPath: "EconomicDefinition",
                SourceSystem: winningSource.SourceSystem,
                DisplayValue: detail.DisplayName,
                IsWinningSource: true,
                AsOf: winningSource.AsOf,
                UpdatedBy: winningSource.UpdatedBy,
                Reason: winningSource.Reason,
                SourceRecordId: winningSource.SourceRecordId,
                ImpactSeverity: SecurityMasterImpactSeverity.None));
        }

        foreach (var assessment in assessments)
        {
            candidates.Add(new SecurityMasterSourceCandidateDto(
                ConflictId: assessment.Conflict.ConflictId,
                FieldPath: assessment.Conflict.FieldPath,
                SourceSystem: assessment.ChallengerSource,
                DisplayValue: assessment.ChallengerValue ?? string.Empty,
                IsWinningSource: false,
                AsOf: assessment.Conflict.DetectedAt,
                UpdatedBy: null,
                Reason: assessment.ImpactSummary,
                SourceRecordId: null,
                ImpactSeverity: assessment.ImpactSeverity));
        }

        return candidates;
    }

    private static IReadOnlyList<SecurityMasterRecommendedActionDto> BuildRecommendedActions(
        SecurityDetailDto detail,
        SecurityMasterTrustPostureDto trustPosture,
        IReadOnlyList<SecurityMasterConflictAssessmentDto> assessments,
        SecurityMasterDownstreamImpactDto downstreamImpact)
    {
        var actions = new List<SecurityMasterRecommendedActionDto>();
        var selectedConflict = assessments.FirstOrDefault(static assessment =>
            string.Equals(assessment.Conflict.Status, "Open", StringComparison.OrdinalIgnoreCase));

        if (selectedConflict is not null)
        {
            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.ResolveSelectedConflict,
                Title: $"Resolve {FormatFieldLabel(selectedConflict.Conflict.FieldPath)}",
                Detail: $"{selectedConflict.RecommendedWinner} {selectedConflict.ImpactSummary}",
                IsPrimary: true,
                IsEnabled: selectedConflict.Recommendation != SecurityMasterConflictRecommendationKind.ManualReview,
                ConflictId: selectedConflict.Conflict.ConflictId));
        }

        var bulkEligibleCount = assessments.Count(static assessment => assessment.IsBulkEligible);
        if (bulkEligibleCount > 0)
        {
            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.BulkResolveLowRiskConflicts,
                Title: "Apply low-risk bulk resolutions",
                Detail: $"{bulkEligibleCount} conflict(s) qualify for low-risk bulk assist.",
                IsPrimary: selectedConflict is null,
                IsEnabled: true));
        }

        var missingTradingFields = GetMissingTradingParameterFields(detail.AssetClass, null);
        if (!trustPosture.TradingParametersComplete)
        {
            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.BackfillTradingParameters,
                Title: "Backfill trading parameters",
                Detail: trustPosture.TradingParametersStatus,
                IsPrimary: false,
                IsEnabled: true));
        }

        if (!trustPosture.CorporateActionsTrusted)
        {
            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.ReviewCorporateActions,
                Title: "Review corporate actions",
                Detail: trustPosture.CorporateActionReadiness,
                IsPrimary: false,
                IsEnabled: true));
        }

        foreach (var link in downstreamImpact.Links.OrderBy(GetImpactLinkOrder))
        {
            var kind = link.Target switch
            {
                "reconciliation" => SecurityMasterRecommendedActionKind.OpenReconciliationImpact,
                "ledger" => SecurityMasterRecommendedActionKind.OpenLedgerImpact,
                "reportPack" => SecurityMasterRecommendedActionKind.OpenReportPackImpact,
                "portfolio" => SecurityMasterRecommendedActionKind.OpenPortfolioImpact,
                _ => SecurityMasterRecommendedActionKind.RefreshTrustSnapshot
            };

            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: kind,
                Title: link.Label,
                Detail: link.Summary,
                IsPrimary: false,
                IsEnabled: link.IsActive,
                Target: link.Target));
        }

        actions.Add(new SecurityMasterRecommendedActionDto(
            Kind: SecurityMasterRecommendedActionKind.EditSelectedSecurity,
            Title: "Edit selected security",
            Detail: "Make a governed amendment to the golden copy after completing triage.",
            IsPrimary: false,
            IsEnabled: true));

        return actions;
    }

    private static int GetImpactLinkOrder(SecurityMasterImpactLinkDto link)
        => link.Target switch
        {
            "reconciliation" => 0,
            "ledger" => 1,
            "reportPack" => 2,
            "portfolio" => 3,
            _ => 4
        };

    private static SecurityMasterImpactSeverity DetermineImpactSeverity(
        int portfolioExposureCount,
        int ledgerExposureCount,
        int reconciliationExposureCount,
        int reportPackExposureCount)
    {
        if (reconciliationExposureCount > 0 || reportPackExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.High;
        }

        if (ledgerExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.Medium;
        }

        if (portfolioExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.Low;
        }

        return SecurityMasterImpactSeverity.None;
    }

    private static SecurityMasterImpactSeverity DetermineConflictImpactSeverity(
        SecurityMasterConflict conflict,
        SecurityMasterDownstreamImpactDto downstreamImpact)
    {
        if (!downstreamImpact.IsScoped)
        {
            return SecurityMasterImpactSeverity.Unknown;
        }

        var normalizedFieldPath = conflict.FieldPath.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (normalizedFieldPath.Contains("CorporateAction", StringComparison.OrdinalIgnoreCase) &&
            downstreamImpact.ReportPackExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.High;
        }

        if (normalizedFieldPath.Contains("Identifier", StringComparison.OrdinalIgnoreCase) &&
            downstreamImpact.ReconciliationExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.High;
        }

        if (normalizedFieldPath.Contains("TradingParameters", StringComparison.OrdinalIgnoreCase) &&
            downstreamImpact.LedgerExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.Medium;
        }

        return downstreamImpact.Severity;
    }

    private static string BuildImpactSummary(
        int matchedRunCount,
        int portfolioExposureCount,
        int ledgerExposureCount,
        int reconciliationExposureCount,
        int reportPackExposureCount)
    {
        if (portfolioExposureCount == 0 &&
            ledgerExposureCount == 0 &&
            reconciliationExposureCount == 0 &&
            reportPackExposureCount == 0)
        {
            return $"No downstream exposure detected across {matchedRunCount} scoped run(s).";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{matchedRunCount} scoped run(s) checked • " +
            $"portfolio {portfolioExposureCount}, ledger {ledgerExposureCount}, reconciliation {reconciliationExposureCount}, report pack {reportPackExposureCount}.");
    }

    private static string BuildConflictImpactSummary(
        string fieldPath,
        SecurityMasterImpactSeverity severity,
        SecurityMasterDownstreamImpactDto downstreamImpact)
    {
        if (severity == SecurityMasterImpactSeverity.Unknown)
        {
            return "Downstream impact is not scoped.";
        }

        return severity switch
        {
            SecurityMasterImpactSeverity.High =>
                $"{FormatFieldLabel(fieldPath)} is high impact because downstream governance workflows already reference this security.",
            SecurityMasterImpactSeverity.Medium =>
                $"{FormatFieldLabel(fieldPath)} already feeds ledger-facing workflows.",
            SecurityMasterImpactSeverity.Low =>
                $"{FormatFieldLabel(fieldPath)} only affects low-risk scoped portfolio exposure today.",
            _ =>
                $"{FormatFieldLabel(fieldPath)} has no detected scoped downstream exposure."
        };
    }

    private static string BuildConflictImpactDetail(
        string fieldPath,
        SecurityMasterImpactSeverity severity,
        SecurityMasterDownstreamImpactDto downstreamImpact)
    {
        if (severity == SecurityMasterImpactSeverity.Unknown)
        {
            return $"{FormatFieldLabel(fieldPath)} cannot be bulk-resolved because no fund scope is active.";
        }

        return severity switch
        {
            SecurityMasterImpactSeverity.High =>
                $"{FormatFieldLabel(fieldPath)} should be reviewed manually before reconciliation or report-pack consumers ingest the change.",
            SecurityMasterImpactSeverity.Medium =>
                $"{FormatFieldLabel(fieldPath)} reaches ledger-facing workflows. Keep the resolution explicit and operator-reviewed.",
            SecurityMasterImpactSeverity.Low =>
                $"{FormatFieldLabel(fieldPath)} is limited to scoped portfolio posture and can participate in low-risk bulk assist when the recommendation is deterministic.",
            _ =>
                $"{FormatFieldLabel(fieldPath)} has no detected scoped downstream exposure."
        };
    }

    private static SecurityMasterWorkstationDto MapToWorkstationSecurity(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic)
    {
        var primaryIdentifier = detail.Identifiers.FirstOrDefault(static identifier => identifier.IsPrimary)
            ?? detail.Identifiers.FirstOrDefault();

        return new SecurityMasterWorkstationDto(
            SecurityId: detail.SecurityId,
            DisplayName: detail.DisplayName,
            Status: detail.Status,
            Classification: new SecurityClassificationSummaryDto(
                AssetClass: detail.AssetClass,
                SubType: economic?.SubType,
                PrimaryIdentifierKind: primaryIdentifier?.Kind.ToString(),
                PrimaryIdentifierValue: primaryIdentifier?.Value),
            EconomicDefinition: new SecurityEconomicDefinitionSummaryDto(
                Currency: detail.Currency,
                Version: detail.Version,
                EffectiveFrom: detail.EffectiveFrom,
                EffectiveTo: detail.EffectiveTo,
                SubType: economic?.SubType,
                AssetFamily: economic?.AssetFamily,
                IssuerType: economic?.IssuerType));
    }

    private static SecurityMasterEconomicDefinitionDrillInDto MapToEconomicDefinition(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        WinningSourceInfo? winningSource)
    {
        return new SecurityMasterEconomicDefinitionDrillInDto(
            SecurityId: detail.SecurityId,
            AssetClass: detail.AssetClass,
            Currency: detail.Currency,
            Version: detail.Version,
            EffectiveFrom: detail.EffectiveFrom,
            EffectiveTo: detail.EffectiveTo,
            AssetFamily: economic?.AssetFamily,
            SubType: economic?.SubType,
            IssuerType: economic?.IssuerType,
            RiskCountry: economic?.RiskCountry,
            WinningSourceSystem: winningSource?.SourceSystem,
            WinningSourceRecordId: winningSource?.SourceRecordId,
            WinningSourceAsOf: winningSource?.AsOf,
            WinningSourceUpdatedBy: winningSource?.UpdatedBy,
            WinningSourceReason: winningSource?.Reason);
    }

    private static WinningSourceInfo? ParseWinningSource(JsonElement? provenance)
    {
        if (!provenance.HasValue || provenance.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var sourceSystem = TryGetJsonString(provenance.Value, "sourceSystem");
        if (string.IsNullOrWhiteSpace(sourceSystem))
        {
            return null;
        }

        return new WinningSourceInfo(
            SourceSystem: sourceSystem,
            SourceRecordId: TryGetJsonString(provenance.Value, "sourceRecordId"),
            AsOf: TryGetJsonDateTimeOffset(provenance.Value, "asOf"),
            UpdatedBy: TryGetJsonString(provenance.Value, "updatedBy"),
            Reason: TryGetJsonString(provenance.Value, "reason"));
    }

    private static string? ExtractCurrentFieldValue(
        string fieldPath,
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        TradingParametersDto? trading)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return null;
        }

        var segments = fieldPath
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        if (segments[0].Equals("Identifiers", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length == 1 || segments[1].Equals("Primary", StringComparison.OrdinalIgnoreCase))
            {
                return detail.Identifiers.FirstOrDefault(static identifier => identifier.IsPrimary)?.Value
                    ?? detail.Identifiers.FirstOrDefault()?.Value;
            }

            var kindSegment = segments[1];
            var identifier = detail.Identifiers.FirstOrDefault(identifier =>
                identifier.Kind.ToString().Equals(kindSegment, StringComparison.OrdinalIgnoreCase));
            if (identifier is not null)
            {
                return identifier.Value;
            }

            var alias = detail.Aliases.FirstOrDefault(alias =>
                alias.AliasKind.Equals(kindSegment, StringComparison.OrdinalIgnoreCase));
            return alias?.AliasValue;
        }

        if (segments[0].Equals("TradingParameters", StringComparison.OrdinalIgnoreCase))
        {
            return segments.Last().ToLowerInvariant() switch
            {
                "lotsize" => FormatNullableDecimal(trading?.LotSize),
                "ticksize" => FormatNullableDecimal(trading?.TickSize),
                "contractmultiplier" => FormatNullableDecimal(trading?.ContractMultiplier),
                "marginrequirementpct" => FormatNullableDecimal(trading?.MarginRequirementPct),
                "tradinghoursutc" => trading?.TradingHoursUtc,
                "circuitbreakerthresholdpct" => FormatNullableDecimal(trading?.CircuitBreakerThresholdPct),
                _ => null
            };
        }

        if (segments[0].Equals("EconomicDefinition", StringComparison.OrdinalIgnoreCase))
        {
            return segments.Last().ToLowerInvariant() switch
            {
                "displayname" => detail.DisplayName,
                "currency" => detail.Currency,
                "assetclass" => detail.AssetClass,
                "assetfamily" => economic?.AssetFamily,
                "subtype" => economic?.SubType,
                "issuertype" => economic?.IssuerType,
                _ => null
            };
        }

        var roots = new List<JsonElement>(4);
        if (economic is not null)
        {
            roots.Add(economic.Classification);
            roots.Add(economic.CommonTerms);
            roots.Add(economic.EconomicTerms);
        }

        roots.Add(detail.CommonTerms);
        roots.Add(detail.AssetSpecificTerms);

        foreach (var root in roots)
        {
            if (TryReadJsonPath(root, segments, out var value) ||
                TryReadJsonPath(root, segments.Skip(1), out value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryReadJsonPath(
        JsonElement root,
        IEnumerable<string> segments,
        out string? value)
    {
        value = null;
        var current = root;
        var hasAny = false;

        foreach (var segment in segments)
        {
            hasAny = true;
            if (current.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!TryGetPropertyCaseInsensitive(current, segment, out current))
            {
                return false;
            }
        }

        if (!hasAny)
        {
            return false;
        }

        value = current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => current.GetRawText()
        };

        return true;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) ||
                property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeComparableString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is ' ' or '-' or '/' or '.' or '_')
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }

    private static bool AreEquivalent(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && string.Equals(
               NormalizeComparableString(left),
               NormalizeComparableString(right),
               StringComparison.Ordinal);

    private static HashSet<string> BuildNormalizedIdentifierSet(SecurityDetailDto detail)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in detail.Identifiers)
        {
            identifiers.Add(NormalizeComparableString(identifier.Value));
        }

        foreach (var alias in detail.Aliases.Where(static alias => alias.IsEnabled))
        {
            identifiers.Add(NormalizeComparableString(alias.AliasValue));
        }

        identifiers.Add(NormalizeComparableString(detail.DisplayName));
        return identifiers;
    }

    private static bool MatchesSecurity(
        string? symbol,
        Guid? securityId,
        SecurityDetailDto detail,
        ISet<string> normalizedIdentifiers)
    {
        if (securityId.HasValue && securityId.Value == detail.SecurityId)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        return normalizedIdentifiers.Contains(NormalizeComparableString(symbol));
    }

    private static IReadOnlyList<string> GetMissingTradingParameterFields(string? assetClass, TradingParametersDto? trading)
    {
        var missingFields = new List<string>(4);

        if (trading?.LotSize is null or <= 0)
        {
            missingFields.Add("lot size");
        }

        if (trading?.TickSize is null or <= 0)
        {
            missingFields.Add("tick size");
        }

        if (string.IsNullOrWhiteSpace(trading?.TradingHoursUtc))
        {
            missingFields.Add("trading hours");
        }

        if (RequiresContractMultiplier(assetClass) && trading?.ContractMultiplier is null or <= 0)
        {
            missingFields.Add("contract multiplier");
        }

        return missingFields;
    }

    private static bool RequiresContractMultiplier(string? assetClass)
        => assetClass is not null &&
           (assetClass.Equals("Option", StringComparison.OrdinalIgnoreCase)
            || assetClass.Equals("Future", StringComparison.OrdinalIgnoreCase)
            || assetClass.Equals("Swap", StringComparison.OrdinalIgnoreCase)
            || assetClass.Equals("Warrant", StringComparison.OrdinalIgnoreCase));

    private static string FormatFieldLabel(string? fieldPath)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return "Field";
        }

        var raw = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
        var builder = new StringBuilder(raw.Length + 8);
        for (var index = 0; index < raw.Length; index++)
        {
            var character = raw[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(raw[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(character);
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(builder.ToString().Replace('_', ' ').ToLowerInvariant());
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
        => TryGetPropertyCaseInsensitive(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DateTimeOffset? TryGetJsonDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyCaseInsensitive(element, propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(property.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static string? FormatNullableDecimal(decimal? value)
        => value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : null;

    private sealed record SecurityWorkbenchContext(
        SecurityDetailDto Detail,
        SecurityEconomicDefinitionRecord? EconomicDefinition,
        TradingParametersDto? TradingParameters,
        WinningSourceInfo? WinningSource,
        SecurityMasterDownstreamImpactDto DownstreamImpact);

    private sealed record WinningSourceInfo(
        string SourceSystem,
        string? SourceRecordId,
        DateTimeOffset? AsOf,
        string? UpdatedBy,
        string? Reason);

    private enum ConflictSide : byte
    {
        ProviderA = 0,
        ProviderB = 1
    }
}
