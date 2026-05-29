using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Builds the shared drilldown payload for a retained report-pack ledger amount.
/// </summary>
public sealed class LedgerAmountProvenanceService
{
    private readonly IGovernanceReportPackRepository? _reportPackRepository;
    private readonly IReconciliationBreakQueueRepository? _breakQueueRepository;

    public LedgerAmountProvenanceService(
        IGovernanceReportPackRepository? reportPackRepository,
        IReconciliationBreakQueueRepository? breakQueueRepository = null)
    {
        _reportPackRepository = reportPackRepository;
        _breakQueueRepository = breakQueueRepository;
    }

    public async Task<LedgerAmountProvenanceDetailDto?> GetAsync(
        Guid reportId,
        string scopeKey,
        CancellationToken ct = default)
    {
        if (_reportPackRepository is null || string.IsNullOrWhiteSpace(scopeKey))
        {
            return null;
        }

        var snapshot = await _reportPackRepository.GetAsync(reportId, ct).ConfigureAwait(false);
        if (snapshot is null)
        {
            return null;
        }

        var normalizedScopeKey = scopeKey.Trim();
        var linePointers = snapshot.Provenance.LineagePointers
            .Where(pointer =>
                string.Equals(pointer.ScopeType, "line", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pointer.ScopeKey, normalizedScopeKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var ledgerPointer = linePointers.FirstOrDefault(pointer =>
            string.Equals(pointer.EvidenceType, "ledger-account", StringComparison.OrdinalIgnoreCase));
        if (ledgerPointer is null)
        {
            return null;
        }

        var (accountName, symbol) = ParseScopeKey(normalizedScopeKey);
        var securityPointer = linePointers.FirstOrDefault(pointer =>
            string.Equals(pointer.EvidenceType, "security", StringComparison.OrdinalIgnoreCase));
        var securityEvidenceIds = BuildSecurityEvidenceIds(securityPointer);
        var securityId = ExtractSecurityId(securityEvidenceIds);
        var reconciliationPointer = snapshot.Provenance.LineagePointers.FirstOrDefault(pointer =>
            string.Equals(pointer.EvidenceType, "reconciliation-summary", StringComparison.OrdinalIgnoreCase));
        var retainedEvidence = snapshot.Provenance.LineagePointers
            .Where(pointer =>
                string.Equals(pointer.ScopeType, "report", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pointer.ScopeKey, normalizedScopeKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pointer.ScopeKey, "reconciliation", StringComparison.OrdinalIgnoreCase))
            .Select(ToEvidence)
            .ToArray();
        var relatedCases = await FindRelatedCasesAsync(
            normalizedScopeKey,
            accountName,
            symbol,
            securityEvidenceIds,
            ct).ConfigureAwait(false);
        var providerCaseEvidence = relatedCases
            .Where(static item => IsProviderEvidenceCase(item))
            .Select(item => ToProviderCaseEvidence(item, ledgerPointer.Amount))
            .ToArray();
        var evidence = retainedEvidence
            .Concat(providerCaseEvidence)
            .GroupBy(static item => $"{item.EvidenceType}:{item.EvidenceId}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        var relatedCaseIds = relatedCases
            .Select(static item => item.BreakId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var relatedCaseSummaries = relatedCases
            .Select(ToReconciliationCase)
            .ToArray();
        var strategyRuns = BuildStrategyRunLinks(
            snapshot.Provenance.RelatedRunIds,
            snapshot.Provenance.LineagePointers,
            normalizedScopeKey);
        var warnings = BuildWarnings(evidence, securityPointer, reconciliationPointer, relatedCaseIds, strategyRuns);
        var latestApproval = snapshot.LifecycleEvents
            .Where(static item =>
                item.ToStatus is GovernanceReportPackStatusDto.Approved or GovernanceReportPackStatusDto.Retained)
            .OrderByDescending(static item => item.ChangedAt)
            .FirstOrDefault();

        return new LedgerAmountProvenanceDetailDto(
            ReportId: snapshot.ReportId,
            ScopeKey: normalizedScopeKey,
            AccountName: accountName,
            Symbol: symbol,
            Amount: ledgerPointer.Amount ?? 0m,
            Currency: snapshot.Currency,
            Evidence: evidence,
            SecurityMaster: securityPointer is null || string.IsNullOrWhiteSpace(symbol)
                ? null
                : new LedgerAmountSecurityMasterLinkDto(
                    symbol,
                    securityPointer.Route,
                    securityPointer.RelatedEvidenceIds ?? [],
                    securityPointer.EvidenceCount ?? securityPointer.RelatedEvidenceIds?.Count ?? 0,
                    securityPointer.CapturedAt,
                    securityId,
                    securityPointer.DisplayLabel,
                    securityPointer.SourceSystem,
                    securityPointer.EvidenceId),
            Reconciliation: new LedgerAmountReconciliationStateDto(
                snapshot.Provenance.ReconciliationRunCount,
                snapshot.Provenance.OpenReconciliationBreakCount,
                reconciliationPointer?.Route,
                relatedCaseIds,
                relatedCaseSummaries),
            Approval: new LedgerAmountApprovalStateDto(
                snapshot.Status,
                latestApproval?.Actor,
                latestApproval?.ChangedAt,
                snapshot.LifecycleEvents.Count),
            ReportUsage: new LedgerAmountReportUsageDto(
                snapshot.ReportId,
                snapshot.DisplayName,
                snapshot.ReportKind,
                snapshot.FundProfileId,
                snapshot.AsOf,
                snapshot.GeneratedAt,
                snapshot.Currency,
                UiApiRoutes.WithParam(UiApiRoutes.FundReportPackById, "reportId", snapshot.ReportId.ToString("D"))),
            Warnings: warnings,
            StrategyRuns: strategyRuns);
    }

    private async Task<IReadOnlyList<ReconciliationBreakQueueItem>> FindRelatedCasesAsync(
        string scopeKey,
        string accountName,
        string? symbol,
        IReadOnlyList<string> securityEvidenceIds,
        CancellationToken ct)
    {
        if (_breakQueueRepository is null)
        {
            return [];
        }

        var items = await _breakQueueRepository.GetAllAsync(null, ct).ConfigureAwait(false);
        return items
            .Where(item =>
                Contains(item.BreakId, scopeKey) ||
                Contains(item.RoutingDetail, scopeKey) ||
                Contains(item.ExplainabilitySummary, scopeKey) ||
                Contains(item.ExplainabilitySummary, accountName) ||
                (!string.IsNullOrWhiteSpace(symbol) && Contains(item.ExplainabilitySummary, symbol)) ||
                securityEvidenceIds.Any(evidenceId => CaseReferencesEvidenceId(item, evidenceId)))
            .OrderByDescending(static item => item.LastUpdatedAt)
            .GroupBy(static item => item.BreakId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static LedgerAmountReconciliationCaseDto ToReconciliationCase(ReconciliationBreakQueueItem item)
        => new(
            item.BreakId,
            item.Status.ToString(),
            item.LifecycleState.ToString(),
            item.AssignedTo,
            item.Team,
            item.RequiredSignoffRole,
            item.SignoffStatus,
            item.ExceptionRoute,
            item.RecommendedAction,
            item.DetectedAt,
            item.LastUpdatedAt,
            item.Severity.ToString(),
            item.Variance,
            item.ToleranceBand,
            item.ReviewedBy,
            item.ReviewedAt,
            item.ResolvedBy,
            item.ResolvedAt,
            item.ResolutionNote,
            item.SignoffHistory?.Count ?? 0,
            item.SlaPolicyId,
            item.SlaDueAt,
            item.SlaBreached,
            item.SlaState,
            item.AgeBand,
            item.BusinessAgeHours);

    private static LedgerAmountProvenanceEvidenceDto ToEvidence(FundReportPackLineagePointerDto pointer)
        => new(
            pointer.EvidenceType,
            pointer.EvidenceId,
            pointer.DisplayLabel,
            pointer.Route,
            pointer.SourceSystem,
            pointer.RelatedEvidenceIds,
            pointer.EvidenceCount,
            pointer.Amount,
            pointer.CapturedAt);

    private static LedgerAmountProvenanceEvidenceDto ToProviderCaseEvidence(
        ReconciliationBreakQueueItem item,
        decimal? ledgerAmount)
    {
        var relatedIds = new List<string> { item.BreakId };
        if (!string.IsNullOrWhiteSpace(item.RoutingDetail))
        {
            relatedIds.Add(item.RoutingDetail);
        }

        return new LedgerAmountProvenanceEvidenceDto(
            EvidenceType: "provider-event",
            EvidenceId: string.IsNullOrWhiteSpace(item.UpstreamSyncCursor) ? item.BreakId : item.UpstreamSyncCursor,
            DisplayLabel: item.StrategyName,
            Route: item.RoutingTarget,
            SourceSystem: string.IsNullOrWhiteSpace(item.CustodianId) ? "provider" : item.CustodianId,
            RelatedEvidenceIds: relatedIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            EvidenceCount: relatedIds.Count,
            Amount: ledgerAmount,
            CapturedAt: item.LastUpstreamSyncAt ?? item.LastUpdatedAt,
            ProviderEventId: ExtractExplainabilityValue(item.ExplainabilitySummary, "providerEventId"),
            ProviderEventType: ExtractExplainabilityValue(item.ExplainabilitySummary, "candidate"),
            ProviderEvidenceSource: ExtractExplainabilityValue(item.ExplainabilitySummary, "evidenceSource"),
            RequiredFeed: ExtractExplainabilityValue(item.ExplainabilitySummary, "requiredFeed"),
            SecurityId: ExtractExplainabilityValue(item.ExplainabilitySummary, "securityId"),
            LedgerEffectKind: ExtractExplainabilityValue(item.ExplainabilitySummary, "ledgerEffect"),
            PrincipalAmount: ExtractExplainabilityDecimal(item.ExplainabilitySummary, "principalAmount"),
            IncomeAmount: ExtractExplainabilityDecimal(item.ExplainabilitySummary, "incomeAmount"),
            JournalPreviewLineCount: ExtractExplainabilityInt(item.ExplainabilitySummary, "journalLines"));
    }

    private static bool IsProviderEvidenceCase(ReconciliationBreakQueueItem item)
        => Contains(item.ExceptionRoute, "provider-ledger") ||
           Contains(item.StrategyName, "provider") ||
           Contains(item.UpstreamSyncCursor, "provider") ||
           !string.IsNullOrWhiteSpace(item.CustodianId) ||
           !string.IsNullOrWhiteSpace(item.ExternalAccountId);

    private static IReadOnlyList<string> BuildSecurityEvidenceIds(FundReportPackLineagePointerDto? securityPointer)
    {
        if (securityPointer is null)
        {
            return [];
        }

        return new[] { securityPointer.EvidenceId }
            .Concat(securityPointer.RelatedEvidenceIds ?? [])
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Guid? ExtractSecurityId(IReadOnlyList<string> securityEvidenceIds)
    {
        foreach (var evidenceId in securityEvidenceIds)
        {
            if (Guid.TryParse(evidenceId, out var securityId))
            {
                return securityId;
            }
        }

        return null;
    }

    private static bool CaseReferencesEvidenceId(ReconciliationBreakQueueItem item, string evidenceId)
    {
        if (string.IsNullOrWhiteSpace(evidenceId))
        {
            return false;
        }

        return Contains(item.BreakId, evidenceId) ||
               Contains(item.RoutingDetail, evidenceId) ||
               Contains(item.RoutingTarget, evidenceId) ||
               Contains(item.ExplainabilitySummary, evidenceId) ||
               Contains(item.UpstreamSyncCursor, evidenceId);
    }

    private static IReadOnlyList<LedgerAmountStrategyRunLinkDto> BuildStrategyRunLinks(
        IReadOnlyList<string> relatedRunIds,
        IReadOnlyList<FundReportPackLineagePointerDto> lineagePointers,
        string scopeKey)
    {
        var runPointers = lineagePointers
            .Where(pointer =>
                string.Equals(pointer.EvidenceType, "run", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pointer.EvidenceType, "strategy-run", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var links = new List<LedgerAmountStrategyRunLinkDto>(relatedRunIds.Count + runPointers.Length);
        foreach (var pointer in runPointers)
        {
            links.Add(new LedgerAmountStrategyRunLinkDto(
                pointer.EvidenceId,
                pointer.DisplayLabel,
                pointer.Route,
                pointer.SourceSystem,
                pointer.CapturedAt,
                string.Equals(pointer.ScopeKey, scopeKey, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var runId in relatedRunIds.Where(static item => !string.IsNullOrWhiteSpace(item)))
        {
            if (links.Any(link => string.Equals(link.RunId, runId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            links.Add(new LedgerAmountStrategyRunLinkDto(runId));
        }

        return links
            .GroupBy(static item => item.RunId, StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
                group.OrderByDescending(static item => item.IsLineScoped)
                    .ThenBy(static item => string.IsNullOrWhiteSpace(item.Route))
                    .First())
            .OrderBy(static item => item.RunId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildWarnings(
        IReadOnlyList<LedgerAmountProvenanceEvidenceDto> evidence,
        FundReportPackLineagePointerDto? securityPointer,
        FundReportPackLineagePointerDto? reconciliationPointer,
        IReadOnlyList<string> relatedCases,
        IReadOnlyList<LedgerAmountStrategyRunLinkDto> strategyRuns)
    {
        var warnings = new List<string>();
        if (!evidence.Any(static item =>
                string.Equals(item.SourceSystem, "provider", StringComparison.OrdinalIgnoreCase) ||
                item.SourceSystem?.Contains("provider", StringComparison.OrdinalIgnoreCase) == true ||
                item.EvidenceType.Contains("provider", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("No retained provider-event pointer is attached to this ledger amount.");
        }

        if (securityPointer is null)
        {
            warnings.Add("No retained Security Master pointer is attached to this ledger amount.");
        }

        if (reconciliationPointer is null)
        {
            warnings.Add("No retained reconciliation summary pointer is attached to this report pack.");
        }

        if (relatedCases.Count == 0)
        {
            warnings.Add("No durable reconciliation case is linked to this ledger amount.");
        }

        if (strategyRuns.Count == 0)
        {
            warnings.Add("No retained strategy/run pointer is attached to this ledger amount.");
        }

        return warnings;
    }

    private static (string AccountName, string? Symbol) ParseScopeKey(string scopeKey)
    {
        var parts = scopeKey.Split(':', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1])
            ? (parts[0], parts[1])
            : (scopeKey, null);
    }

    private static bool Contains(string? value, string expected)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Contains(expected, StringComparison.OrdinalIgnoreCase);

    private static string? ExtractExplainabilityValue(string? summary, string key)
    {
        if (string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var prefix = $"{key}=";
        foreach (var part in summary.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = part[prefix.Length..].Trim();
            return string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "unresolved", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : value;
        }

        return null;
    }

    private static decimal? ExtractExplainabilityDecimal(string? summary, string key)
        => decimal.TryParse(
            ExtractExplainabilityValue(summary, key),
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;

    private static int? ExtractExplainabilityInt(string? summary, string key)
        => int.TryParse(
            ExtractExplainabilityValue(summary, key),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
}
