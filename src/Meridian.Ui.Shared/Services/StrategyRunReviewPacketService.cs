using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Builds a single review packet for Research, Trading, and Governance to inspect
/// the same run, portfolio, ledger, reconciliation, brokerage, and coverage evidence.
/// </summary>
public sealed class StrategyRunReviewPacketService
{
    private readonly StrategyRunReadService _runReadService;
    private readonly StrategyRunContinuityService _continuityService;
    private readonly IServiceProvider _services;
    private readonly ILogger<StrategyRunReviewPacketService> _logger;

    public StrategyRunReviewPacketService(
        StrategyRunReadService runReadService,
        StrategyRunContinuityService continuityService,
        IServiceProvider services,
        ILogger<StrategyRunReviewPacketService> logger)
    {
        _runReadService = runReadService ?? throw new ArgumentNullException(nameof(runReadService));
        _continuityService = continuityService ?? throw new ArgumentNullException(nameof(continuityService));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StrategyRunReviewPacketDto?> GetAsync(
        string runId,
        Guid? fundAccountId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ct.ThrowIfCancellationRequested();

        var runTask = _runReadService.GetRunDetailAsync(runId, ct);
        var continuityTask = _continuityService.GetRunContinuityAsync(runId, ct);
        var fillsTask = _runReadService.GetFillsAsync(runId, ct);
        var attributionTask = _runReadService.GetAttributionAsync(runId, ct);
        var brokerageTask = ResolveBrokerageStatusAsync(fundAccountId, ct);

        await Task.WhenAll(runTask, continuityTask, fillsTask, attributionTask, brokerageTask).ConfigureAwait(false);

        var run = await runTask.ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        var continuity = await continuityTask.ConfigureAwait(false);
        var brokerageStatus = await brokerageTask.ConfigureAwait(false);
        var workItems = BuildWorkItems(run, continuity, brokerageStatus, fundAccountId);
        var warnings = workItems
            .Where(static item => item.Tone is OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical)
            .Select(static item => item.Detail)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new StrategyRunReviewPacketDto(
            RunId: runId,
            GeneratedAt: DateTimeOffset.UtcNow,
            Run: run,
            Continuity: continuity,
            Fills: await fillsTask.ConfigureAwait(false),
            Attribution: await attributionTask.ConfigureAwait(false),
            BrokerageSync: brokerageStatus,
            WorkItems: workItems,
            Warnings: warnings);
    }

    private async Task<WorkstationBrokerageSyncStatusDto?> ResolveBrokerageStatusAsync(
        Guid? fundAccountId,
        CancellationToken ct)
    {
        if (!fundAccountId.HasValue)
        {
            return null;
        }

        var syncService = _services.GetService(typeof(BrokeragePortfolioSyncService)) as BrokeragePortfolioSyncService;
        return syncService is null
            ? null
            : await syncService.GetStatusAsync(fundAccountId.Value, ct).ConfigureAwait(false);
    }

    private IReadOnlyList<OperatorWorkItemDto> BuildWorkItems(
        StrategyRunDetail run,
        StrategyRunContinuityDetail? continuity,
        WorkstationBrokerageSyncStatusDto? brokerageStatus,
        Guid? fundAccountId)
    {
        var items = new List<OperatorWorkItemDto>();
        var runId = run.Summary.RunId;

        if (run.Promotion?.RequiresReview == true || run.Summary.Promotion?.RequiresReview == true)
        {
            var promotion = run.Promotion ?? run.Summary.Promotion!;
            items.Add(NewItem(
                "promotion-review",
                OperatorWorkItemKindDto.PromotionReview,
                "Promotion review required",
                promotion.Reason,
                OperatorWorkItemToneDto.Warning,
                runId,
                auditReference: promotion.AuditReference));
        }

        var missingSecurityCount =
            (run.Portfolio?.SecurityMissingCount ?? 0)
            + (run.Ledger?.SecurityMissingCount ?? 0);
        if (missingSecurityCount > 0)
        {
            items.Add(NewItem(
                "security-master-coverage-gap",
                OperatorWorkItemKindDto.SecurityMasterCoverage,
                "Security Master coverage gap",
                $"{missingSecurityCount} run security reference(s) are missing coverage.",
                OperatorWorkItemToneDto.Warning,
                runId));
        }

        if (continuity?.ContinuityStatus.Warnings is { Count: > 0 } continuityWarnings)
        {
            foreach (var warning in continuityWarnings)
            {
                items.Add(NewItem(
                    $"continuity-{warning.Code}",
                    OperatorWorkItemKindDto.ReconciliationBreak,
                    warning.Code,
                    warning.Message,
                    warning.Code.Contains("break", StringComparison.OrdinalIgnoreCase)
                        ? OperatorWorkItemToneDto.Critical
                        : OperatorWorkItemToneDto.Warning,
                    runId));
            }
        }

        if (brokerageStatus is not null && brokerageStatus.Health is not WorkstationBrokerageSyncHealth.Healthy)
        {
            items.Add(NewItem(
                "brokerage-sync-attention",
                OperatorWorkItemKindDto.BrokerageSync,
                "Brokerage sync attention",
                brokerageStatus.Warnings.FirstOrDefault()
                    ?? brokerageStatus.LastError
                    ?? "Brokerage sync is not healthy.",
                brokerageStatus.Health is WorkstationBrokerageSyncHealth.Failed
                    ? OperatorWorkItemToneDto.Critical
                    : OperatorWorkItemToneDto.Warning,
                runId,
                fundAccountId));
        }

        if (items.Count == 0)
        {
            items.Add(NewItem(
                "review-packet-ready",
                OperatorWorkItemKindDto.PromotionReview,
                "Review packet ready",
                "Run, continuity, fill, attribution, and optional brokerage evidence are available.",
                OperatorWorkItemToneDto.Success,
                runId));
        }

        _logger.LogDebug("Built review packet work item set for run {RunId}: {Count}", runId, items.Count);
        return items
            .OrderByDescending(static item => item.Tone)
            .ThenBy(static item => item.CreatedAt)
            .ToArray();
    }

    private static OperatorWorkItemDto NewItem(
        string idPrefix,
        OperatorWorkItemKindDto kind,
        string label,
        string detail,
        OperatorWorkItemToneDto tone,
        string? runId,
        Guid? fundAccountId = null,
        string? auditReference = null)
    {
        var navigation = ResolveNavigation(kind, runId, fundAccountId);
        var scope = fundAccountId?.ToString("N") ?? runId ?? auditReference ?? label;
        return new(
            WorkItemId: BuildScopedId(idPrefix, scope),
            Kind: kind,
            Label: label,
            Detail: detail,
            Tone: tone,
            CreatedAt: DateTimeOffset.UtcNow,
            RunId: runId,
            FundAccountId: fundAccountId,
            AuditReference: auditReference,
            Workspace: navigation.Workspace,
            TargetRoute: navigation.TargetRoute,
            TargetPageTag: navigation.TargetPageTag);
    }

    private static (string Workspace, string TargetRoute, string TargetPageTag) ResolveNavigation(
        OperatorWorkItemKindDto kind,
        string? runId,
        Guid? fundAccountId)
    {
        return kind switch
        {
            OperatorWorkItemKindDto.SecurityMasterCoverage => (
                "Governance",
                UiApiRoutes.WorkstationSecurityMasterSearch,
                "SecurityMaster"),
            OperatorWorkItemKindDto.ReconciliationBreak or OperatorWorkItemKindDto.ReportPackApproval => (
                "Governance",
                UiApiRoutes.ReconciliationBreakQueue,
                "FundReconciliation"),
            OperatorWorkItemKindDto.BrokerageSync => (
                "Trading",
                fundAccountId.HasValue
                    ? UiApiRoutes.FundAccountBrokerageSyncStatus.Replace("{accountId}", fundAccountId.Value.ToString(), StringComparison.Ordinal)
                    : UiApiRoutes.FundAccountBrokerageSyncAccounts,
                "TradingShell"),
            _ => (
                "Trading",
                string.IsNullOrWhiteSpace(runId)
                    ? UiApiRoutes.WorkstationTradingReadiness
                    : UiApiRoutes.RunsReviewPacket.Replace("{runId}", runId, StringComparison.Ordinal),
                "TradingShell")
        };
    }

    private static string BuildScopedId(string prefix, string scope)
    {
        var normalizedPrefix = NormalizeIdToken(prefix);
        var normalizedScope = NormalizeIdToken(scope);
        return string.IsNullOrEmpty(normalizedScope)
            ? normalizedPrefix
            : $"{normalizedPrefix}-{normalizedScope}";
    }

    private static string NormalizeIdToken(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        var previousWasSeparator = false;

        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator && length > 0)
            {
                buffer[length++] = '-';
                previousWasSeparator = true;
            }
        }

        if (length > 0 && buffer[length - 1] == '-')
        {
            length--;
        }

        return new string(buffer[..length]);
    }
}
