using Meridian.Application.Accounts;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Builds an account-scoped controller readiness score from provider, ledger,
/// Security Master, reconciliation, and approval/casework evidence.
/// </summary>
public sealed class FundAccountCloseReadinessService
{
    private const int ProviderWeight = 18;
    private const int SecurityMasterWeight = 18;
    private const int LedgerWeight = 18;
    private const int ReconciliationWeight = 24;
    private const int CorporateActionWeight = 10;
    private const int ApprovalWeight = 12;

    private readonly IAccountQueryService _accounts;
    private readonly ILogger<FundAccountCloseReadinessService> _logger;
    private readonly ProviderLedgerReconciliationService? _providerLedgerReconciliation;
    private readonly IReconciliationBreakQueueRepository? _breakQueueRepository;

    public FundAccountCloseReadinessService(
        IAccountQueryService accounts,
        ILogger<FundAccountCloseReadinessService> logger,
        ProviderLedgerReconciliationService? providerLedgerReconciliation = null,
        IReconciliationBreakQueueRepository? breakQueueRepository = null)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providerLedgerReconciliation = providerLedgerReconciliation;
        _breakQueueRepository = breakQueueRepository;
    }

    public async Task<FundAccountCloseReadinessDto?> GetAsync(Guid accountId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var account = await _accounts.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        var evaluatedAt = DateTimeOffset.UtcNow;
        var accountReadiness = await _accounts.GetReadinessAsync(accountId, ct).ConfigureAwait(false);
        var latestSnapshot = await _accounts.GetLatestBalanceSnapshotAsync(accountId, ct).ConfigureAwait(false);
        var latestReconciliation = _providerLedgerReconciliation is null
            ? null
            : await _providerLedgerReconciliation.GetLatestAsync(accountId, ct).ConfigureAwait(false);
        var openCases = await LoadOpenAccountCasesAsync(accountId, latestReconciliation, ct).ConfigureAwait(false);

        var components = new List<FundAccountCloseReadinessComponentDto>(capacity: 6);
        var blockers = new List<FundAccountCloseReadinessBlockerDto>();

        AddProviderFreshnessComponent(components, blockers, accountReadiness, latestReconciliation, evaluatedAt);
        AddSecurityMasterComponent(components, blockers, accountReadiness, latestReconciliation);
        AddLedgerPostingComponent(components, blockers, account, accountReadiness, latestSnapshot, latestReconciliation);
        AddReconciliationComponent(components, blockers, latestReconciliation);
        AddCorporateActionFactorComponent(components, blockers, latestReconciliation);
        AddApprovalComponent(components, blockers, latestReconciliation, openCases);

        var score = Math.Clamp(components.Sum(static component => component.Score), 0, 100);
        var status = components.Any(static component => component.Status == FundAccountCloseReadinessStatusDto.Blocked) ||
                     blockers.Any(static blocker => string.Equals(blocker.Severity, "Critical", StringComparison.OrdinalIgnoreCase))
            ? FundAccountCloseReadinessStatusDto.Blocked
            : components.Any(static component => component.Status == FundAccountCloseReadinessStatusDto.ReviewRequired)
                ? FundAccountCloseReadinessStatusDto.ReviewRequired
                : FundAccountCloseReadinessStatusDto.Ready;

        var nextActions = blockers
            .GroupBy(static blocker => blocker.Code, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Select(static blocker => new FundAccountCloseReadinessActionDto(
                blocker.Code,
                BuildActionLabel(blocker),
                blocker.Severity,
                blocker.RouteHint))
            .ToArray();

        _logger.LogDebug(
            "Computed close readiness for fund account {AccountId}: {Status} score {Score}",
            accountId,
            status,
            score);

        return new FundAccountCloseReadinessDto(
            accountId,
            evaluatedAt,
            status,
            IsReadyToClose: status == FundAccountCloseReadinessStatusDto.Ready && score == 100,
            Score: score,
            LatestProviderSyncedAt: latestReconciliation?.Summary.ProviderSyncedAt ?? accountReadiness?.LastSuccessfulSyncAt,
            LatestLedgerAsOfDate: latestSnapshot?.AsOfDate ?? latestReconciliation?.Summary.InternalAsOfDate,
            LatestReconciliationRunId: latestReconciliation?.Summary.ReconciliationRunId,
            Components: components,
            Blockers: blockers,
            NextActions: nextActions);
    }

    private async Task<IReadOnlyList<ReconciliationBreakQueueItem>> LoadOpenAccountCasesAsync(
        Guid accountId,
        ProviderLedgerReconciliationDetailDto? latestReconciliation,
        CancellationToken ct)
    {
        if (_breakQueueRepository is null)
        {
            return [];
        }

        var open = await _breakQueueRepository.GetAllAsync(ReconciliationBreakQueueStatus.Open, ct).ConfigureAwait(false);
        var inReview = await _breakQueueRepository.GetAllAsync(ReconciliationBreakQueueStatus.InReview, ct).ConfigureAwait(false);
        var accountIdText = accountId.ToString("D");
        var accountIdCompact = accountId.ToString("N");
        var securityIds = latestReconciliation?.SecurityMasterPassports?
            .Select(static passport => passport.SecurityId)
            .Where(static securityId => securityId.HasValue)
            .Select(static securityId => securityId!.Value)
            .Distinct()
            .ToArray() ?? [];

        // The queue is file-backed and already bounded by operator casework volume; filter in memory.
        return open.Concat(inReview)
            .GroupBy(static item => item.BreakId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Where(item => IsAccountScopedCase(item, accountIdText, accountIdCompact) ||
                           IsSecurityMasterCaseForHeldSecurity(item, securityIds))
            .ToArray();
    }

    private static bool IsAccountScopedCase(
        ReconciliationBreakQueueItem item,
        string accountIdText,
        string accountIdCompact) =>
        string.Equals(item.FundAccountId, accountIdText, StringComparison.OrdinalIgnoreCase) ||
        item.BreakId.Contains(accountIdCompact, StringComparison.OrdinalIgnoreCase) ||
        (item.RoutingTarget?.Contains(accountIdText, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsSecurityMasterCaseForHeldSecurity(
        ReconciliationBreakQueueItem item,
        IReadOnlyList<Guid> securityIds)
    {
        if (securityIds.Count == 0 ||
            (!string.Equals(item.Team, "Security Master", StringComparison.OrdinalIgnoreCase) &&
             !(item.ExceptionRoute?.StartsWith("security-master/", StringComparison.OrdinalIgnoreCase) ?? false)))
        {
            return false;
        }

        return securityIds.Any(securityId => CaseReferencesSecurity(item, securityId));
    }

    private static bool CaseReferencesSecurity(ReconciliationBreakQueueItem item, Guid securityId)
    {
        var securityIdText = securityId.ToString("D");
        var securityIdCompact = securityId.ToString("N");
        return string.Equals(item.RoutingDetail, securityIdText, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.RoutingDetail, securityIdCompact, StringComparison.OrdinalIgnoreCase) ||
               item.BreakId.Contains(securityIdCompact, StringComparison.OrdinalIgnoreCase) ||
               (item.RoutingTarget?.Contains(securityIdText, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (item.ExplainabilitySummary?.Contains(securityIdText, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (item.ExplainabilitySummary?.Contains(securityIdCompact, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (item.UpstreamSyncCursor?.Contains(securityIdCompact, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static void AddProviderFreshnessComponent(
        ICollection<FundAccountCloseReadinessComponentDto> components,
        ICollection<FundAccountCloseReadinessBlockerDto> blockers,
        AccountReadinessSnapshotDto? accountReadiness,
        ProviderLedgerReconciliationDetailDto? latestReconciliation,
        DateTimeOffset evaluatedAt)
    {
        var route = latestReconciliation is null
            ? null
            : BuildRoute(UiApiRoutes.FundAccountBrokerageSyncReconciliationLatest, latestReconciliation.Summary.AccountId);
        var providerIssues = accountReadiness?.Issues
            .Where(static issue =>
                issue.Code.Contains("provider_link", StringComparison.OrdinalIgnoreCase) ||
                issue.Code.Contains("sync.", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];

        if (latestReconciliation is null && accountReadiness is null)
        {
            AddComponent(components, blockers, "provider-freshness", "Provider data freshness", ProviderWeight,
                FundAccountCloseReadinessStatusDto.Blocked, "Critical",
                "No account readiness or provider-ledger reconciliation evidence is available.",
                "close.provider_data.missing", "ProviderData", route);
            return;
        }

        if (providerIssues.Any(static issue => issue.Severity == AccountReadinessSeverityDto.Critical) ||
            latestReconciliation?.Checks.Any(static check =>
                check.CheckId == "provider-projection-available" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Blocked) == true)
        {
            AddComponent(components, blockers, "provider-freshness", "Provider data freshness", ProviderWeight,
                FundAccountCloseReadinessStatusDto.Blocked, "Critical",
                "Provider projection, provider link, or latest account sync is unavailable.",
                "close.provider_data.blocked", "ProviderData", route);
            return;
        }

        var providerSyncedAt = latestReconciliation?.Summary.ProviderSyncedAt;
        var staleAfter = TimeSpan.FromMinutes(latestReconciliation?.Summary.ProviderStaleAfterMinutes ?? 30);
        if (providerIssues.Length > 0 ||
            accountReadiness?.FreshUntil < evaluatedAt ||
            (providerSyncedAt.HasValue && evaluatedAt - providerSyncedAt.Value > staleAfter))
        {
            AddComponent(components, blockers, "provider-freshness", "Provider data freshness", ProviderWeight,
                FundAccountCloseReadinessStatusDto.ReviewRequired, "Warning",
                "Provider data exists but freshness or sync warnings require controller review.",
                "close.provider_data.review", "ProviderData", route);
            return;
        }

        AddComponentOnly(components, "provider-freshness", "Provider data freshness", ProviderWeight,
            FundAccountCloseReadinessStatusDto.Ready, "Info", "Provider data is fresh enough for close readiness.", route);
    }

    private static void AddSecurityMasterComponent(
        ICollection<FundAccountCloseReadinessComponentDto> components,
        ICollection<FundAccountCloseReadinessBlockerDto> blockers,
        AccountReadinessSnapshotDto? accountReadiness,
        ProviderLedgerReconciliationDetailDto? latestReconciliation)
    {
        var route = latestReconciliation is null
            ? null
            : BuildRoute(UiApiRoutes.FundAccountBrokerageSyncReconciliationLatest, latestReconciliation.Summary.AccountId);
        var securityIssues = accountReadiness?.Issues
            .Where(static issue => issue.Code.Contains("security_master", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
        var unresolvedPassports = latestReconciliation?.SecurityMasterPassports?
            .Count(static passport => passport.Status is ProviderSecurityMasterPassportStatusDto.Unresolved or ProviderSecurityMasterPassportStatusDto.Blocked) ?? 0;
        var securityBreaks = latestReconciliation?.Summary.SecurityIssueCount ?? 0;

        if (latestReconciliation is null)
        {
            AddComponent(components, blockers, "security-master-completeness", "Security Master completeness", SecurityMasterWeight,
                FundAccountCloseReadinessStatusDto.Blocked, "Critical",
                "No provider-ledger reconciliation exists to prove Security Master coverage.",
                "close.security_master.unproven", "SecurityMaster", route);
            return;
        }

        if (securityIssues.Any(static issue => issue.Severity == AccountReadinessSeverityDto.Critical) ||
            latestReconciliation.Checks.Any(static check =>
                check.CheckId.StartsWith("security-master:", StringComparison.OrdinalIgnoreCase) &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Blocked))
        {
            AddComponent(components, blockers, "security-master-completeness", "Security Master completeness", SecurityMasterWeight,
                FundAccountCloseReadinessStatusDto.Blocked, "Critical",
                "Security Master identity coverage is blocked for one or more provider positions.",
                "close.security_master.blocked", "SecurityMaster", route);
            return;
        }

        if (securityIssues.Length > 0 || unresolvedPassports > 0 || securityBreaks > 0)
        {
            AddComponent(components, blockers, "security-master-completeness", "Security Master completeness", SecurityMasterWeight,
                FundAccountCloseReadinessStatusDto.ReviewRequired, "Warning",
                "Security Master coverage has unresolved mappings or passport warnings.",
                "close.security_master.review", "SecurityMaster", route);
            return;
        }

        AddComponentOnly(components, "security-master-completeness", "Security Master completeness", SecurityMasterWeight,
            FundAccountCloseReadinessStatusDto.Ready, "Info", "Provider positions are covered by Security Master evidence.", route);
    }

    private static void AddLedgerPostingComponent(
        ICollection<FundAccountCloseReadinessComponentDto> components,
        ICollection<FundAccountCloseReadinessBlockerDto> blockers,
        AccountSummaryDto account,
        AccountReadinessSnapshotDto? accountReadiness,
        AccountBalanceSnapshotDto? latestSnapshot,
        ProviderLedgerReconciliationDetailDto? latestReconciliation)
    {
        var route = BuildRoute(UiApiRoutes.FundAccountBrokerageSyncReconciliationLatest, account.AccountId);
        var ledgerIssues = accountReadiness?.Issues
            .Where(static issue => issue.Code.Contains("ledger", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];

        if (string.IsNullOrWhiteSpace(account.LedgerReference) ||
            ledgerIssues.Any(static issue => issue.Severity == AccountReadinessSeverityDto.Critical))
        {
            AddComponent(components, blockers, "ledger-posting-status", "Ledger posting status", LedgerWeight,
                FundAccountCloseReadinessStatusDto.Blocked, "Critical",
                "The account is not mapped to a ledger reference for posting.",
                "close.ledger.mapping_missing", "Ledger", route);
            return;
        }

        if (latestSnapshot is null && latestReconciliation?.Summary.InternalAsOfDate is null)
        {
            AddComponent(components, blockers, "ledger-posting-status", "Ledger posting status", LedgerWeight,
                FundAccountCloseReadinessStatusDto.Blocked, "Critical",
                "No internal ledger balance snapshot is available for close validation.",
                "close.ledger.snapshot_missing", "Ledger", route);
            return;
        }

        AddComponentOnly(components, "ledger-posting-status", "Ledger posting status", LedgerWeight,
            FundAccountCloseReadinessStatusDto.Ready, "Info", "Internal ledger balance evidence is available.", route);
    }

    private static void AddReconciliationComponent(
        ICollection<FundAccountCloseReadinessComponentDto> components,
        ICollection<FundAccountCloseReadinessBlockerDto> blockers,
        ProviderLedgerReconciliationDetailDto? latestReconciliation)
    {
        var route = latestReconciliation is null
            ? null
            : BuildRoute(UiApiRoutes.FundAccountBrokerageSyncReconciliationLatest, latestReconciliation.Summary.AccountId);

        if (latestReconciliation is null)
        {
            AddComponent(components, blockers, "provider-ledger-reconciliation", "Provider-ledger reconciliation", ReconciliationWeight,
                FundAccountCloseReadinessStatusDto.Blocked, "Critical",
                "No provider-ledger reconciliation has been run for this account.",
                "close.reconciliation.missing", "Reconciliation", route);
            return;
        }

        if (latestReconciliation.Summary.Status == ProviderLedgerReconciliationStatusDto.Blocked)
        {
            AddComponent(components, blockers, "provider-ledger-reconciliation", "Provider-ledger reconciliation", ReconciliationWeight,
                FundAccountCloseReadinessStatusDto.Blocked, "Critical",
                "Provider-ledger reconciliation is blocked by missing required source data.",
                "close.reconciliation.blocked", "Reconciliation", route);
            return;
        }

        if (latestReconciliation.Summary.OpenBreakCount > 0)
        {
            AddComponent(components, blockers, "provider-ledger-reconciliation", "Provider-ledger reconciliation", ReconciliationWeight,
                FundAccountCloseReadinessStatusDto.ReviewRequired, "Warning",
                $"{latestReconciliation.Summary.OpenBreakCount} provider-ledger break(s) still require review or sign-off.",
                "close.reconciliation.breaks_open", "Reconciliation", route);
            return;
        }

        var shadowBreakCount = latestReconciliation.ShadowBookComparison?.BreakLineCount ?? 0;
        if (shadowBreakCount > 0)
        {
            AddComponent(components, blockers, "provider-ledger-reconciliation", "Provider-ledger reconciliation", ReconciliationWeight,
                FundAccountCloseReadinessStatusDto.ReviewRequired, "Warning",
                $"{shadowBreakCount} retained shadow-book comparison line(s) still differ from provider or custodian evidence.",
                "close.reconciliation.shadow_book_breaks", "Reconciliation", route);
            return;
        }

        AddComponentOnly(components, "provider-ledger-reconciliation", "Provider-ledger reconciliation", ReconciliationWeight,
            FundAccountCloseReadinessStatusDto.Ready, "Info", "Provider and internal ledger evidence is reconciled or signed off.", route);
    }

    private static void AddCorporateActionFactorComponent(
        ICollection<FundAccountCloseReadinessComponentDto> components,
        ICollection<FundAccountCloseReadinessBlockerDto> blockers,
        ProviderLedgerReconciliationDetailDto? latestReconciliation)
    {
        var route = latestReconciliation is null
            ? null
            : BuildRoute(UiApiRoutes.FundAccountBrokerageSyncReconciliationLatest, latestReconciliation.Summary.AccountId);

        if (latestReconciliation is null)
        {
            AddComponent(components, blockers, "corporate-action-factor-readiness", "Corporate action and factor readiness", CorporateActionWeight,
                FundAccountCloseReadinessStatusDto.Blocked, "Critical",
                "No provider-ledger reconciliation exists to prove corporate-action and factor-schedule readiness.",
                "close.corporate_actions.unproven", "CorporateActions", route);
            return;
        }

        var readiness = latestReconciliation.CorporateActionReadiness;
        if (readiness is null)
        {
            AddComponent(components, blockers, "corporate-action-factor-readiness", "Corporate action and factor readiness", CorporateActionWeight,
                FundAccountCloseReadinessStatusDto.Blocked, "Critical",
                "Provider-ledger reconciliation did not retain corporate-action readiness evidence.",
                "close.corporate_actions.missing", "CorporateActions", route);
            return;
        }

        var blockedCandidateCount = readiness.EvidenceCandidates.Count(static candidate =>
            candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Blocked);
        if (readiness.Status == ProviderLedgerReconciliationCheckStatusDto.Blocked || blockedCandidateCount > 0)
        {
            AddComponent(components, blockers, "corporate-action-factor-readiness", "Corporate action and factor readiness", CorporateActionWeight,
                FundAccountCloseReadinessStatusDto.Blocked, "Critical",
                "Corporate-action or factor-schedule readiness is blocked by missing provider projection, capability metadata, or Security Master attribution.",
                "close.corporate_actions.blocked", "CorporateActions", route);
            return;
        }

        var breakCandidateCount = readiness.EvidenceCandidates.Count(static candidate =>
            candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Break);
        if (readiness.Status == ProviderLedgerReconciliationCheckStatusDto.Break ||
            readiness.MissingFeeds.Count > 0 ||
            breakCandidateCount > 0)
        {
            var message = breakCandidateCount > 0
                ? $"{breakCandidateCount} corporate-action or factor evidence candidate(s) require controller review before close."
                : "Corporate-action or factor-schedule feeds are degraded and require controller review before close.";
            AddComponent(components, blockers, "corporate-action-factor-readiness", "Corporate action and factor readiness", CorporateActionWeight,
                FundAccountCloseReadinessStatusDto.ReviewRequired, "Warning",
                message,
                "close.corporate_actions.review", "CorporateActions", route);
            return;
        }

        var hasSecurityMasterScheduleSupport = readiness.SecurityMasterScheduleFeeds.Any(static feed =>
            feed.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
            feed.CanUpdateSecurityMaster &&
            feed.CanSupportLedgerValuation &&
            IsCloseSupportingScheduleFeed(feed));
        if (readiness.FixedIncomeOrStructuredPositionCount > 0 && !hasSecurityMasterScheduleSupport)
        {
            AddComponent(components, blockers, "corporate-action-factor-readiness", "Corporate action and factor readiness", CorporateActionWeight,
                FundAccountCloseReadinessStatusDto.ReviewRequired, "Warning",
                $"{readiness.FixedIncomeOrStructuredPositionCount} fixed-income or structured position(s) require matched Security Master schedule feed evidence that can update Security Master history and support ledger valuation before close.",
                "close.corporate_actions.factor_evidence_missing", "CorporateActions", route);
            return;
        }

        AddComponentOnly(
            components,
            "corporate-action-factor-readiness",
            "Corporate action and factor readiness",
            CorporateActionWeight,
            FundAccountCloseReadinessStatusDto.Ready,
            "Info",
            readiness.EvidenceCandidates.Count == 0
                ? "No corporate-action-sensitive provider evidence is present for this close."
                : hasSecurityMasterScheduleSupport
                    ? "Corporate-action, income/principal cash, and Security Master schedule feed evidence are retained, attributed, and ledger-valuation ready."
                    : "Corporate-action, income/principal cash, and factor-schedule evidence candidates are retained and attributed.",
            route);
    }

    private static bool IsCloseSupportingScheduleFeed(ProviderSecurityMasterScheduleFeedDto feed) =>
        string.Equals(feed.FeedKind, "SecurityMasterFactorHistory", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(feed.FeedKind, "SecurityMasterLoanSchedule", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(feed.FeedKind, "SecurityMasterPrincipalSchedule", StringComparison.OrdinalIgnoreCase);

    private static void AddApprovalComponent(
        ICollection<FundAccountCloseReadinessComponentDto> components,
        ICollection<FundAccountCloseReadinessBlockerDto> blockers,
        ProviderLedgerReconciliationDetailDto? latestReconciliation,
        IReadOnlyList<ReconciliationBreakQueueItem> openCases)
    {
        var route = latestReconciliation is null
            ? UiApiRoutes.ReconciliationBreakQueue
            : BuildRoute(UiApiRoutes.FundAccountBrokerageSyncReconciliationLatest, latestReconciliation.Summary.AccountId);

        var pendingSignoffCases = openCases
            .Where(static item => !string.Equals(item.SignoffStatus, "signed-off", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (pendingSignoffCases.Any(static item => item.Severity is ReconciliationBreakSeverity.Critical or ReconciliationBreakSeverity.High))
        {
            AddComponent(components, blockers, "approvals-casework", "Approvals and casework", ApprovalWeight,
                FundAccountCloseReadinessStatusDto.Blocked, "Critical",
                "Critical or high-severity account casework is still pending sign-off.",
                "close.approvals.critical_pending", "Approvals", route);
            return;
        }

        if (pendingSignoffCases.Length > 0 || latestReconciliation?.Summary.OpenBreakCount > 0)
        {
            AddComponent(components, blockers, "approvals-casework", "Approvals and casework", ApprovalWeight,
                FundAccountCloseReadinessStatusDto.ReviewRequired, "Warning",
                "Account casework or provider-ledger breaks are awaiting owner review or sign-off.",
                "close.approvals.pending", "Approvals", route);
            return;
        }

        AddComponentOnly(components, "approvals-casework", "Approvals and casework", ApprovalWeight,
            FundAccountCloseReadinessStatusDto.Ready, "Info", "No account-scoped approval blockers are open.", route);
    }

    private static void AddComponent(
        ICollection<FundAccountCloseReadinessComponentDto> components,
        ICollection<FundAccountCloseReadinessBlockerDto> blockers,
        string key,
        string label,
        int weight,
        FundAccountCloseReadinessStatusDto status,
        string severity,
        string reason,
        string blockerCode,
        string category,
        string? routeHint)
    {
        AddComponentOnly(components, key, label, weight, status, severity, reason, routeHint);
        blockers.Add(new FundAccountCloseReadinessBlockerDto(
            blockerCode,
            category,
            severity,
            reason,
            routeHint));
    }

    private static void AddComponentOnly(
        ICollection<FundAccountCloseReadinessComponentDto> components,
        string key,
        string label,
        int weight,
        FundAccountCloseReadinessStatusDto status,
        string severity,
        string reason,
        string? routeHint)
    {
        var score = status switch
        {
            FundAccountCloseReadinessStatusDto.Ready => weight,
            FundAccountCloseReadinessStatusDto.ReviewRequired => Math.Max(1, weight / 2),
            _ => 0
        };

        components.Add(new FundAccountCloseReadinessComponentDto(
            key,
            label,
            status,
            score,
            weight,
            severity,
            reason,
            routeHint));
    }

    private static string BuildRoute(string routeTemplate, Guid accountId) =>
        routeTemplate.Replace("{accountId}", accountId.ToString("D"), StringComparison.Ordinal);

    private static string BuildActionLabel(FundAccountCloseReadinessBlockerDto blocker) =>
        blocker.Category switch
        {
            "ProviderData" => "Refresh provider evidence",
            "SecurityMaster" => "Resolve Security Master coverage",
            "Ledger" => "Repair ledger posting evidence",
            "Reconciliation" => "Review provider-ledger reconciliation",
            "CorporateActions" => "Review corporate-action evidence",
            "Approvals" => "Complete approval sign-off",
            _ => "Review close readiness blocker"
        };
}
