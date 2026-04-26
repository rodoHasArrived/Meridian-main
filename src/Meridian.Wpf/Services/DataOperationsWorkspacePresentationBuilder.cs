using Meridian.Contracts.Api;
using Meridian.Contracts.Session;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Copy;
using Meridian.Wpf.Models;
using NotificationHistoryItemModel = Meridian.Ui.Services.Services.NotificationHistoryItem;
using ProviderInfoModel = Meridian.Ui.Services.Services.ProviderInfo;
using StatusProviderInfoModel = Meridian.Ui.Services.Services.StatusProviderInfo;

namespace Meridian.Wpf.Services;

public static class DataOperationsWorkspacePresentationBuilder
{
    internal const string ProvidersUnavailableSummary = "No providers";
    internal const string BackfillUnavailableSummary = "No active backfill";
    internal const string StorageUnavailableSummary = "No data";
    private const string ProviderStreamSignalSource = "Provider quote/trade stream health telemetry";
    private const string ProviderStreamReasonCode = "PROVIDER_STREAM_DEGRADED";
    private const string ProviderStreamRecommendedAction = "Verify provider connectivity and entitlements, then monitor for recovery before promotion decisions.";

    public static DataOperationsWorkspacePresentation Build(DataOperationsWorkspaceData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var providerCount = GetProviderCount(data);
        var healthyProviderCount = GetHealthyProviderCount(data, providerCount);
        var resumableCount = data.ResumableJobs.Count;
        var activeResumables = data.ResumableJobs.Count(job => job.Status == CheckpointStatus.InProgress);
        var pendingSymbols = data.ResumableJobs.Sum(job => job.PendingCount);
        var enabledSchedules = data.BackfillSchedules.Count(schedule => schedule.IsEnabled);
        var latestExecution = data.BackfillExecutions
            .OrderByDescending(execution => execution.CompletedAt ?? execution.StartedAt)
            .FirstOrDefault();
        var latestSession = data.ActiveSession ?? data.Sessions
            .OrderByDescending(GetSessionTimestamp)
            .FirstOrDefault();
        var latestExportJob = data.ExportJobs
            .OrderByDescending(GetExportTimestamp)
            .FirstOrDefault();
        var storageIssueCount = data.StorageHealth?.Issues.Count ?? 0;
        var criticalStorageIssueCount = data.StorageHealth?.Issues.Count(IsCriticalIssue) ?? 0;
        var exportRunningCount = data.ExportJobs.Count(IsActiveExport);
        var exportQueuedCount = data.ExportJobs.Count(job => job.Status is ExportJobStatus.Pending or ExportJobStatus.Queued);
        var exportFailedCount = data.ExportJobs.Count(job => job.Status == ExportJobStatus.Failed);

        var providersTone = providerCount == 0
            ? WorkspaceTone.Warning
            : healthyProviderCount >= providerCount && data.ProviderStatus?.IsConnected == true
                ? WorkspaceTone.Success
                : healthyProviderCount > 0 || data.ProviderStatus?.IsConnected == true
                    ? WorkspaceTone.Warning
                    : WorkspaceTone.Danger;
        var (backfillText, backfillTone) = resumableCount > 0
            ? ($"{pendingSymbols} pending", WorkspaceTone.Warning)
            : data.LastBackfillStatus is { Success: false } || IsFailedExecution(latestExecution)
                ? ("Needs review", WorkspaceTone.Warning)
                : enabledSchedules > 0
                    ? ($"{enabledSchedules} scheduled", WorkspaceTone.Info)
                    : data.LastBackfillStatus is not null
                        ? ($"{data.LastBackfillStatus.BarsWritten:N0} bars", WorkspaceTone.Info)
                        : latestExecution is not null
                            ? ($"{latestExecution.BarsDownloaded:N0} bars", WorkspaceTone.Info)
                            : (BackfillUnavailableSummary, WorkspaceTone.Neutral);
        var (storageText, storageTone) = criticalStorageIssueCount > 0
            ? ($"{criticalStorageIssueCount} issues", WorkspaceTone.Danger)
            : storageIssueCount > 0
                ? ($"{storageIssueCount} issues", WorkspaceTone.Warning)
                : data.StorageStats is not null
                    ? ($"{data.StorageStats.UsedPercentage:F0}% used", WorkspaceTone.Info)
                    : (StorageUnavailableSummary, WorkspaceTone.Neutral);
        var freshnessValue = data.ActiveSession is not null
            ? $"{data.ActiveSession.Name} active · {(data.ActiveSession.Provider ?? "No provider")}"
            : data.ProviderStatus?.IsConnected == true && !string.IsNullOrWhiteSpace(data.ProviderStatus.ActiveProvider)
                ? $"{data.ProviderStatus.ActiveProvider} connected"
                : data.StorageStats is { NewestData: { } newestData } && newestData != default
                    ? $"Storage current to {newestData:g}"
                    : latestExportJob is not null
                        ? exportRunningCount > 0
                            ? $"{exportRunningCount} export job(s) active"
                            : $"{DisplayExportJobName(latestExportJob)} {latestExportJob.Status.ToString().ToLowerInvariant()}"
                        : latestSession is not null
                            ? $"{latestSession.Name} {latestSession.Status.ToLowerInvariant()}"
                            : "Awaiting live telemetry";
        var reviewStateValue = resumableCount > 0
            ? $"{resumableCount} resumable job(s)"
            : latestExecution is not null && IsFailedExecution(latestExecution)
                ? "Last execution failed"
                : data.LastBackfillStatus is { Success: false }
                    ? "Last run failed"
                    : enabledSchedules > 0
                        ? $"{enabledSchedules} enabled schedule(s)"
                        : data.LastBackfillStatus is not null || latestExecution is not null
                            ? "Queue clear"
                            : "Awaiting queue";
        var criticalValue = data.UnreadAlerts > 0
            ? $"{data.UnreadAlerts} unread alert(s)"
            : criticalStorageIssueCount > 0
                ? $"{criticalStorageIssueCount} critical storage issue(s)"
                : exportFailedCount > 0
                    ? $"{exportFailedCount} failed export job(s)"
                    : data.LastBackfillStatus is { Success: false } || IsFailedExecution(latestExecution)
                        ? "Backfill recovery required"
                        : "No urgent blockers";
        var criticalTone = criticalStorageIssueCount > 0 || exportFailedCount > 0
            ? WorkspaceTone.Danger
            : data.UnreadAlerts > 0 || data.LastBackfillStatus is { Success: false } || IsFailedExecution(latestExecution)
                ? WorkspaceTone.Warning
                : WorkspaceTone.Info;
        var queueSummary = resumableCount > 0
            ? $"{resumableCount} resumable backfill job(s) are waiting across {pendingSymbols} remaining symbol(s). Keep provider health and storage headroom aligned before the next run."
            : criticalStorageIssueCount > 0
                ? $"Storage review is blocking a clean handoff. {criticalStorageIssueCount} critical storage issue(s) need attention before the next package or export."
                : exportRunningCount > 0
                    ? $"{exportRunningCount} export job(s) are active while provider, backfill, and storage review stay docked in the same shell."
                    : data.ActiveSession is not null
                        ? $"Collection session '{data.ActiveSession.Name}' is active. Monitor provider routing, backfill pressure, and storage growth together."
                        : data.ScopeSummary;
        var providerQueueState = BuildQueueRegionState(
            isLoading: false,
            hasError: providerCount == 0 && data.ProviderStatus is null && (data.BackfillHealth?.Providers?.Count ?? 0) == 0,
            isEmpty: providerCount == 0,
            loadingTitle: "Loading provider queue",
            loadingDescription: "Gathering provider catalog and health telemetry.",
            emptyTitle: "No providers configured",
            emptyDescription: "Configure at least one provider before routing collection and backfill queues.",
            emptyPrimaryLabel: "Switch Context",
            emptyPrimaryAction: "SwitchContext",
            emptySecondaryLabel: "Provider Health",
            emptySecondaryAction: "ProviderHealth",
            errorTitle: "Provider queue degraded",
            errorDescription: "Provider catalog or status telemetry is unavailable.",
            errorPrimaryLabel: "Retry",
            errorPrimaryAction: "Retry",
            errorSecondaryLabel: "Open Diagnostics",
            errorSecondaryAction: "Diagnostics");

        var backfillQueueState = BuildQueueRegionState(
            isLoading: false,
            hasError: data.LastBackfillStatus is { Success: false } || IsFailedExecution(latestExecution),
            isEmpty: resumableCount == 0 && data.LastBackfillStatus is null && latestExecution is null && enabledSchedules == 0,
            loadingTitle: "Loading backfill queue",
            loadingDescription: "Reading resumable checkpoints and schedule posture.",
            emptyTitle: "Backfill queue is empty",
            emptyDescription: "Stage a historical backfill run or enable schedules to keep coverage fresh.",
            emptyPrimaryLabel: "Open Backfill",
            emptyPrimaryAction: "Backfill",
            emptySecondaryLabel: "Switch Context",
            emptySecondaryAction: "SwitchContext",
            errorTitle: "Backfill queue degraded",
            errorDescription: "Backfill execution or checkpoint telemetry reported an error.",
            errorPrimaryLabel: "Retry",
            errorPrimaryAction: "Retry",
            errorSecondaryLabel: "Open Diagnostics",
            errorSecondaryAction: "Diagnostics");

        var storageQueueState = BuildQueueRegionState(
            isLoading: false,
            hasError: criticalStorageIssueCount > 0 || (data.StorageStats is null && data.StorageHealth is null),
            isEmpty: data.StorageStats is { TotalSymbols: 0 },
            loadingTitle: "Loading storage queue",
            loadingDescription: "Collecting storage utilization and health posture.",
            emptyTitle: "Storage queue is empty",
            emptyDescription: "No symbols are currently stored for this context.",
            emptyPrimaryLabel: "Switch Context",
            emptyPrimaryAction: "SwitchContext",
            emptySecondaryLabel: "Open Storage",
            emptySecondaryAction: "Storage",
            errorTitle: "Storage queue degraded",
            errorDescription: "Storage metrics are missing or critical issues require operator review.",
            errorPrimaryLabel: "Retry",
            errorPrimaryAction: "Retry",
            errorSecondaryLabel: "Open Diagnostics",
            errorSecondaryAction: "Diagnostics");

        var operationsDetail = criticalStorageIssueCount > 0
            ? $"{criticalStorageIssueCount} critical storage issue(s) need review before the next package or export handoff for {data.ScopeLabel}."
            : exportRunningCount > 0 && latestExportJob is not null
                ? $"Export job '{DisplayExportJobName(latestExportJob)}' is active while provider health, backfill pressure, and storage posture remain in the same operator shell."
                : latestExecution is not null
                    ? $"Latest backfill {latestExecution.Status.ToLowerInvariant()} after {latestExecution.SymbolsProcessed} symbol(s) and {latestExecution.BarsDownloaded:N0} bars. Keep providers and storage aligned before the next run."
                    : latestSession is not null
                        ? $"Latest collection session '{latestSession.Name}' keeps provider, backfill, and storage review tied to {data.ScopeLabel}."
                        : data.Notifications.FirstOrDefault() is { } latestNotification
                            ? $"Latest operational signal: {latestNotification.Title}. Keep providers, sessions, storage, and export delivery aligned to {data.ScopeLabel}."
                            : $"Provider readiness, historical coverage, storage posture, and export delivery stay visible together for {data.ScopeLabel}.";

        var providerQueueItems = new[]
        {
            BuildProviderItem(data, providerCount, healthyProviderCount, providersTone)
        };
        var backfillQueueItems = new[]
        {
            BuildBackfillItem(data, latestExecution, resumableCount, activeResumables, pendingSymbols, enabledSchedules),
            BuildSessionItem(latestSession)
        };
        var storageQueueItems = new[]
        {
            BuildStorageItem(data.StorageStats, data.StorageHealth, storageIssueCount, criticalStorageIssueCount),
            BuildExportItem(latestExportJob, exportRunningCount, exportQueuedCount, exportFailedCount, data.ExportJobs.Count)
        };
        var heroState = BuildHeroState(
            data,
            providerQueueState,
            backfillQueueState,
            storageQueueState,
            providerQueueItems,
            backfillQueueItems,
            storageQueueItems,
            providerCount,
            healthyProviderCount,
            resumableCount,
            pendingSymbols,
            enabledSchedules,
            criticalStorageIssueCount,
            exportRunningCount,
            exportFailedCount);

        return new DataOperationsWorkspacePresentation
        {
            Context = new WorkspaceShellContextInput
            {
                WorkspaceTitle = WorkspaceCopyCatalog.DataOperations.ShellTitle,
                WorkspaceSubtitle = WorkspaceCopyCatalog.DataOperations.ShellSubtitle,
                PrimaryScopeLabel = WorkspaceCopyCatalog.DataOperations.PrimaryScopeLabel,
                PrimaryScopeValue = data.ScopeLabel,
                AsOfValue = data.RetrievedAt.ToString("MMM dd yyyy HH:mm"),
                FreshnessValue = freshnessValue,
                ReviewStateLabel = "Backfill",
                ReviewStateValue = reviewStateValue,
                ReviewStateTone = backfillTone,
                CriticalLabel = "Critical",
                CriticalValue = criticalValue,
                CriticalTone = criticalTone,
                AdditionalBadges = BuildAdditionalBadges(latestSession, latestExportJob, enabledSchedules, exportRunningCount, exportFailedCount)
            },
            CommandGroup = BuildCommandGroup(),
            HeroState = heroState,
            QueueScopeBadgeText = data.UnreadAlerts > 0 ? $"{data.ScopeLabel} · {data.UnreadAlerts} alert-linked" : data.ScopeLabel,
            QueueSummaryText = queueSummary,
            ProviderQueueItems = providerQueueItems,
            BackfillQueueItems = backfillQueueItems,
            StorageQueueItems = storageQueueItems,
            OperationsSummaryTitleText = "Data Operations",
            OperationsSummaryDetailText = operationsDetail,
            SummaryProvidersText = providerCount > 0 ? $"{healthyProviderCount}/{providerCount} ready" : ProvidersUnavailableSummary,
            SummaryProvidersTone = providersTone,
            SummaryBackfillText = backfillText,
            SummaryBackfillTone = backfillTone,
            SummaryStorageText = storageText,
            SummaryStorageTone = storageTone,
            RecentOperations = BuildRecentOperations(data, latestExecution, latestSession, latestExportJob),
            ProviderQueueState = providerQueueState,
            BackfillQueueState = backfillQueueState,
            StorageQueueState = storageQueueState
        };
    }

    private static DataOperationsHeroState BuildHeroState(
        DataOperationsWorkspaceData data,
        WorkspaceQueueRegionState providerQueueState,
        WorkspaceQueueRegionState backfillQueueState,
        WorkspaceQueueRegionState storageQueueState,
        IReadOnlyList<WorkspaceQueueItem> providerQueueItems,
        IReadOnlyList<WorkspaceQueueItem> backfillQueueItems,
        IReadOnlyList<WorkspaceQueueItem> storageQueueItems,
        int providerCount,
        int healthyProviderCount,
        int resumableCount,
        int pendingSymbols,
        int enabledSchedules,
        int criticalStorageIssueCount,
        int exportRunningCount,
        int exportFailedCount)
    {
        var providerItem = providerQueueItems.FirstOrDefault() ?? new WorkspaceQueueItem();
        var backfillItem = backfillQueueItems.FirstOrDefault() ?? new WorkspaceQueueItem();
        var sessionItem = backfillQueueItems.Skip(1).FirstOrDefault() ?? new WorkspaceQueueItem();
        var storageItem = storageQueueItems.FirstOrDefault() ?? new WorkspaceQueueItem();
        var exportItem = storageQueueItems.Skip(1).FirstOrDefault() ?? new WorkspaceQueueItem();

        if (providerQueueState.HasError)
        {
            return CreateHeroState(
                focusText: "Provider routing",
                summaryText: providerQueueState.Title,
                badgeText: "Degraded",
                badgeTone: WorkspaceTone.Danger,
                handoffTitleText: "Refresh provider telemetry before routing new queue work",
                handoffDetailText: providerQueueState.Description,
                primaryActionId: providerQueueState.PrimaryActionId,
                primaryActionLabel: providerQueueState.PrimaryActionLabel,
                secondaryActionId: providerQueueState.SecondaryActionId,
                secondaryActionLabel: providerQueueState.SecondaryActionLabel);
        }

        if (providerQueueState.IsEmpty)
        {
            return CreateHeroState(
                focusText: "Provider onboarding",
                summaryText: providerQueueState.Title,
                badgeText: "Action required",
                badgeTone: WorkspaceTone.Warning,
                handoffTitleText: "Configure providers before staging backfills or exports",
                handoffDetailText: providerQueueState.Description,
                primaryActionId: providerQueueState.PrimaryActionId,
                primaryActionLabel: providerQueueState.PrimaryActionLabel,
                secondaryActionId: providerQueueState.SecondaryActionId,
                secondaryActionLabel: providerQueueState.SecondaryActionLabel);
        }

        if (providerCount > 0 && (healthyProviderCount < providerCount || data.ProviderStatus?.IsConnected != true))
        {
            return CreateHeroState(
                focusText: "Provider routing",
                summaryText: $"{healthyProviderCount}/{providerCount} providers are ready",
                badgeText: "Review",
                badgeTone: WorkspaceTone.Warning,
                handoffTitleText: "Stabilize provider health before the next historical or export handoff",
                handoffDetailText: providerItem.Detail,
                primaryActionId: providerItem.PrimaryActionId,
                primaryActionLabel: providerItem.PrimaryActionLabel,
                secondaryActionId: providerItem.SecondaryActionId,
                secondaryActionLabel: providerItem.SecondaryActionLabel);
        }

        if (criticalStorageIssueCount > 0 || storageQueueState.HasError)
        {
            var summaryText = criticalStorageIssueCount > 0
                ? $"{criticalStorageIssueCount} critical storage issue(s) need review"
                : storageQueueState.Title;
            var detailText = criticalStorageIssueCount > 0
                ? storageItem.Detail
                : storageQueueState.Description;
            var primaryActionId = criticalStorageIssueCount > 0 ? storageItem.PrimaryActionId : storageQueueState.PrimaryActionId;
            var primaryActionLabel = criticalStorageIssueCount > 0 ? storageItem.PrimaryActionLabel : storageQueueState.PrimaryActionLabel;
            var secondaryActionId = criticalStorageIssueCount > 0 ? storageItem.SecondaryActionId : storageQueueState.SecondaryActionId;
            var secondaryActionLabel = criticalStorageIssueCount > 0 ? storageItem.SecondaryActionLabel : storageQueueState.SecondaryActionLabel;

            return CreateHeroState(
                focusText: "Storage posture",
                summaryText: summaryText,
                badgeText: criticalStorageIssueCount > 0 ? "Blocked" : "Degraded",
                badgeTone: criticalStorageIssueCount > 0 ? WorkspaceTone.Danger : WorkspaceTone.Warning,
                handoffTitleText: "Resolve storage blockers before the next package handoff",
                handoffDetailText: detailText,
                primaryActionId: primaryActionId,
                primaryActionLabel: primaryActionLabel,
                secondaryActionId: secondaryActionId,
                secondaryActionLabel: secondaryActionLabel);
        }

        if (backfillQueueState.HasError)
        {
            return CreateHeroState(
                focusText: "Backfill recovery",
                summaryText: backfillQueueState.Title,
                badgeText: "Review",
                badgeTone: WorkspaceTone.Warning,
                handoffTitleText: "Investigate failed backfill telemetry before routing more coverage",
                handoffDetailText: backfillQueueState.Description,
                primaryActionId: backfillQueueState.PrimaryActionId,
                primaryActionLabel: backfillQueueState.PrimaryActionLabel,
                secondaryActionId: backfillQueueState.SecondaryActionId,
                secondaryActionLabel: backfillQueueState.SecondaryActionLabel);
        }

        if (resumableCount > 0)
        {
            return CreateHeroState(
                focusText: "Historical coverage",
                summaryText: $"{resumableCount} resumable job(s) are waiting across {pendingSymbols} symbol(s)",
                badgeText: "Attention",
                badgeTone: WorkspaceTone.Warning,
                handoffTitleText: "Resume staged backfills before the next operator handoff",
                handoffDetailText: backfillItem.Detail,
                primaryActionId: backfillItem.PrimaryActionId,
                primaryActionLabel: backfillItem.PrimaryActionLabel,
                secondaryActionId: backfillItem.SecondaryActionId,
                secondaryActionLabel: backfillItem.SecondaryActionLabel);
        }

        if (exportFailedCount > 0)
        {
            return CreateHeroState(
                focusText: "Export delivery",
                summaryText: $"{exportFailedCount} export job(s) failed",
                badgeText: "Review",
                badgeTone: WorkspaceTone.Warning,
                handoffTitleText: "Resolve failed delivery before the next package handoff",
                handoffDetailText: exportItem.Detail,
                primaryActionId: exportItem.PrimaryActionId,
                primaryActionLabel: exportItem.PrimaryActionLabel,
                secondaryActionId: exportItem.SecondaryActionId,
                secondaryActionLabel: exportItem.SecondaryActionLabel);
        }

        if (exportRunningCount > 0)
        {
            return CreateHeroState(
                focusText: "Export delivery",
                summaryText: $"{exportRunningCount} export job(s) are active",
                badgeText: "Active",
                badgeTone: WorkspaceTone.Info,
                handoffTitleText: "Monitor active delivery without leaving the shell",
                handoffDetailText: exportItem.Detail,
                primaryActionId: exportItem.PrimaryActionId,
                primaryActionLabel: exportItem.PrimaryActionLabel,
                secondaryActionId: exportItem.SecondaryActionId,
                secondaryActionLabel: exportItem.SecondaryActionLabel);
        }

        if (enabledSchedules > 0)
        {
            return CreateHeroState(
                focusText: "Recurring coverage",
                summaryText: $"{enabledSchedules} schedule(s) are enabled",
                badgeText: "Scheduled",
                badgeTone: WorkspaceTone.Info,
                handoffTitleText: "Confirm scheduled coverage is aligned with current provider posture",
                handoffDetailText: backfillItem.Detail,
                primaryActionId: "Schedules",
                primaryActionLabel: "Schedules",
                secondaryActionId: backfillItem.PrimaryActionId,
                secondaryActionLabel: backfillItem.PrimaryActionLabel);
        }

        if (data.ActiveSession is not null)
        {
            return CreateHeroState(
                focusText: "Collection lane",
                summaryText: "Collection session is active",
                badgeText: "Active",
                badgeTone: WorkspaceTone.Info,
                handoffTitleText: "Keep session, storage, and export review aligned",
                handoffDetailText: sessionItem.Detail,
                primaryActionId: sessionItem.PrimaryActionId,
                primaryActionLabel: sessionItem.PrimaryActionLabel,
                secondaryActionId: sessionItem.SecondaryActionId,
                secondaryActionLabel: sessionItem.SecondaryActionLabel);
        }

        return CreateHeroState(
            focusText: "Operational posture",
            summaryText: "Provider, storage, and export posture are aligned",
            badgeText: "Ready",
            badgeTone: WorkspaceTone.Success,
            handoffTitleText: "Open the shell area that matches the next handoff",
            handoffDetailText: data.ScopeSummary,
            primaryActionId: exportItem.PrimaryActionId,
            primaryActionLabel: exportItem.PrimaryActionLabel,
            secondaryActionId: providerItem.PrimaryActionId,
            secondaryActionLabel: providerItem.PrimaryActionLabel);
    }

    private static DataOperationsHeroState CreateHeroState(
        string focusText,
        string summaryText,
        string badgeText,
        string badgeTone,
        string handoffTitleText,
        string handoffDetailText,
        string primaryActionId,
        string primaryActionLabel,
        string secondaryActionId,
        string secondaryActionLabel)
    {
        return new DataOperationsHeroState
        {
            FocusText = focusText,
            SummaryText = summaryText,
            BadgeText = badgeText,
            BadgeTone = badgeTone,
            HandoffTitleText = handoffTitleText,
            HandoffDetailText = handoffDetailText,
            PrimaryActionId = primaryActionId,
            PrimaryActionLabel = primaryActionLabel,
            SecondaryActionId = secondaryActionId,
            SecondaryActionLabel = secondaryActionLabel,
            TargetText = $"Target: {ResolveHeroTarget(primaryActionId)}"
        };
    }

    private static string ResolveHeroTarget(string actionId) => actionId switch
    {
        "Retry" => "Refresh current shell",
        "SwitchContext" => "Context selector",
        "ProviderHealth" => "Provider Health",
        "Provider" => "Providers",
        "Backfill" => "Backfill",
        "Storage" => "Storage",
        "CollectionSessions" => "Collection Sessions",
        "DataExport" => "Data Export",
        "Schedules" => "Schedules",
        "PackageManager" => "Package Manager",
        "Diagnostics" => "Diagnostics",
        _ => string.IsNullOrWhiteSpace(actionId) ? "Current shell" : actionId
    };

    private static WorkspaceCommandGroup BuildCommandGroup() => new()
    {
        PrimaryCommands =
        [
            new WorkspaceCommandItem { Id = "ProviderHealth", Label = "Provider Health", Description = "Open provider health", ShortcutHint = "Ctrl+1", Glyph = "\uEB51", Tone = WorkspaceTone.Primary },
            new WorkspaceCommandItem { Id = "Backfill", Label = "Backfill Queue", Description = "Open backfill queue", ShortcutHint = "Ctrl+2", Glyph = "\uE896" },
            new WorkspaceCommandItem { Id = "DataExport", Label = "Data Export", Description = "Open data export", ShortcutHint = "Ctrl+3", Glyph = "\uEDE1" }
        ],
        SecondaryCommands =
        [
            new WorkspaceCommandItem { Id = "Provider", Label = "Providers", Description = "Open providers", Glyph = "\uEC05" },
            new WorkspaceCommandItem { Id = "Symbols", Label = "Symbols", Description = "Open symbols", Glyph = "\uE8AB" },
            new WorkspaceCommandItem { Id = "Storage", Label = "Storage", Description = "Open storage", Glyph = "\uEE94" },
            new WorkspaceCommandItem { Id = "CollectionSessions", Label = "Collection Sessions", Description = "Open sessions", Glyph = "\uE8EF" },
            new WorkspaceCommandItem { Id = "Schedules", Label = "Schedules", Description = "Open schedules", Glyph = "\uE916" },
            new WorkspaceCommandItem { Id = "PackageManager", Label = "Package Manager", Description = "Open package manager", Glyph = "\uE8B7" }
        ]
    };

    private static IReadOnlyList<WorkspaceShellBadge> BuildAdditionalBadges(CollectionSession? latestSession, ExportJob? latestExportJob, int enabledSchedules, int exportRunningCount, int exportFailedCount)
    {
        var badges = new List<WorkspaceShellBadge>();

        if (latestSession is not null)
        {
            badges.Add(new WorkspaceShellBadge
            {
                Label = "Session",
                Value = latestSession.Name,
                Glyph = "\uE8EF",
                Tone = latestSession.Status.Equals(SessionStatus.Active, StringComparison.OrdinalIgnoreCase) ? WorkspaceTone.Info : ResolveSessionTone(latestSession)
            });
        }

        if (latestExportJob is not null)
        {
            badges.Add(new WorkspaceShellBadge
            {
                Label = "Exports",
                Value = exportRunningCount > 0 ? $"{exportRunningCount} active" : exportFailedCount > 0 ? $"{exportFailedCount} failed" : $"{DisplayExportJobName(latestExportJob)} ready",
                Glyph = "\uEDE1",
                Tone = exportFailedCount > 0 ? WorkspaceTone.Warning : exportRunningCount > 0 ? WorkspaceTone.Info : WorkspaceTone.Neutral
            });
        }
        else if (enabledSchedules > 0)
        {
            badges.Add(new WorkspaceShellBadge
            {
                Label = "Schedules",
                Value = $"{enabledSchedules} enabled",
                Glyph = "\uE916",
                Tone = WorkspaceTone.Neutral
            });
        }

        return badges;
    }

    private static WorkspaceQueueItem BuildProviderItem(DataOperationsWorkspaceData data, int providerCount, int healthyProviderCount, string tone)
    {
        var degradedProviders = (data.BackfillHealth?.Providers ?? []).Where(entry => !entry.Value.IsAvailable).Select(entry => entry.Key).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var statusLabel = providerCount == 0 && data.ProviderStatus is null && (data.BackfillHealth?.Providers?.Count ?? 0) == 0 ? "Unavailable" : providerCount == 0 ? "Unconfigured" : healthyProviderCount >= providerCount && data.ProviderStatus?.IsConnected == true ? "Healthy" : healthyProviderCount > 0 || data.ProviderStatus?.IsConnected == true ? "Degraded" : "Offline";
        var countLabel = providerCount > 0 ? $"{healthyProviderCount}/{providerCount} ready" : (data.BackfillHealth?.Providers?.Count ?? 0) > 0 ? $"{healthyProviderCount}/{data.BackfillHealth!.Providers!.Count} healthy" : "No telemetry";
        var detail = providerCount == 0 && data.ProviderStatus is null && (data.BackfillHealth?.Providers?.Count ?? 0) == 0
            ? "Provider catalog and health telemetry are unavailable. Open Providers to configure sources or Provider Health to inspect connectivity."
            : providerCount == 0
                ? "No providers are configured yet. Use Providers to register sources, then open Provider Health to confirm readiness."
                : data.ProviderStatus?.IsConnected == true && !string.IsNullOrWhiteSpace(data.ProviderStatus.ActiveProvider)
                    ? degradedProviders.Length > 0
                        ? $"{data.ProviderStatus.ActiveProvider} is connected, but {degradedProviders.Length} provider(s) need review: {string.Join(", ", degradedProviders.Take(2))}. {BuildProviderTrustRationale()}"
                        : $"{data.ProviderStatus.ActiveProvider} is connected. {healthyProviderCount}/{providerCount} providers report healthy across the current routing chain."
                    : degradedProviders.Length > 0
                        ? $"No active provider is connected. Review degraded providers {string.Join(", ", degradedProviders.Take(2))} before resuming collection, backfill, or export work. {BuildProviderTrustRationale()}"
                        : $"Provider telemetry is present, but no active route is currently connected. Open Provider Health to restore readiness before the next operational run. {BuildProviderTrustRationale()}";

        return new WorkspaceQueueItem { Title = "Provider health", Detail = detail, StatusLabel = statusLabel, CountLabel = countLabel, Tone = tone, PrimaryActionId = "ProviderHealth", PrimaryActionLabel = "Provider Health", SecondaryActionId = "Provider", SecondaryActionLabel = "Providers" };
    }

    private static string BuildProviderTrustRationale()
        => $"Signal source: {ProviderStreamSignalSource}. Reason code: {ProviderStreamReasonCode}. Recommended action: {ProviderStreamRecommendedAction}";

    private static WorkspaceQueueItem BuildBackfillItem(DataOperationsWorkspaceData data, BackfillExecution? latestExecution, int resumableCount, int activeResumables, int pendingSymbols, int enabledSchedules)
    {
        var latestResumable = data.ResumableJobs.OrderByDescending(job => job.CompletedAt ?? job.CreatedAt).FirstOrDefault();
        var statusLabel = activeResumables > 0 ? "Active" : resumableCount > 0 ? "Resumable" : data.LastBackfillStatus is { Success: false } || IsFailedExecution(latestExecution) ? "Review" : enabledSchedules > 0 ? "Scheduled" : latestExecution is not null || data.LastBackfillStatus is not null ? "Clear" : "Idle";
        var countLabel = resumableCount > 0 ? $"{pendingSymbols} symbol(s) pending" : latestExecution is not null ? $"{latestExecution.BarsDownloaded:N0} bars last run" : data.LastBackfillStatus is not null ? $"{data.LastBackfillStatus.BarsWritten:N0} bars last run" : enabledSchedules > 0 ? $"{enabledSchedules} schedule(s) enabled" : "No queued work";
        var detail = latestResumable is not null
            ? $"{resumableCount} resumable job(s) remain across {pendingSymbols} symbol(s). Latest checkpoint uses {latestResumable.Provider} from {latestResumable.FromDate:d} to {latestResumable.ToDate:d}."
            : latestExecution is not null
                ? $"Last execution {latestExecution.Status.ToLowerInvariant()} after {latestExecution.SymbolsProcessed} symbol(s) and {latestExecution.BarsDownloaded:N0} bars. {enabledSchedules} enabled schedule(s) remain available for recurring work."
                : data.LastBackfillStatus is { } lastBackfillStatus
                    ? $"Latest backfill {(lastBackfillStatus.Success ? "completed" : "failed")} via {lastBackfillStatus.Provider ?? "the configured provider"} after writing {lastBackfillStatus.BarsWritten:N0} bars. {enabledSchedules} schedule(s) are staged for future runs."
                    : enabledSchedules > 0
                        ? $"{enabledSchedules} enabled schedule(s) are ready to stage recurring backfills. No resumable or active queue items are waiting right now."
                        : "No active or resumable backfills are waiting. Use Backfill to stage historical coverage or Schedules to automate recurring fills.";

        return new WorkspaceQueueItem { Title = "Backfill queue", Detail = detail, StatusLabel = statusLabel, CountLabel = countLabel, Tone = resumableCount > 0 || data.LastBackfillStatus is { Success: false } || IsFailedExecution(latestExecution) ? WorkspaceTone.Warning : WorkspaceTone.Info, PrimaryActionId = "Backfill", PrimaryActionLabel = "Open Backfill", SecondaryActionId = "Schedules", SecondaryActionLabel = "Schedules" };
    }

    private static WorkspaceQueueItem BuildSessionItem(CollectionSession? latestSession)
    {
        if (latestSession is null)
        {
            return new WorkspaceQueueItem { Title = "Collection sessions", Detail = "No collection sessions are on record yet. Start a session to keep ingest, provider, and storage review connected inside the same shell.", StatusLabel = "Idle", CountLabel = "No sessions", Tone = WorkspaceTone.Neutral, PrimaryActionId = "CollectionSessions", PrimaryActionLabel = "Open Sessions", SecondaryActionId = "Storage", SecondaryActionLabel = "Storage" };
        }

        var totalEvents = latestSession.Statistics?.TotalEvents ?? 0;
        var detail = latestSession.Status.Equals(SessionStatus.Active, StringComparison.OrdinalIgnoreCase)
            ? $"Session '{latestSession.Name}' is active on {latestSession.Provider ?? "No provider"} with {totalEvents:N0} events captured at {(latestSession.Statistics?.EventsPerSecond ?? 0):F1}/s."
            : $"Latest session '{latestSession.Name}' is {latestSession.Status.ToLowerInvariant()} on {latestSession.Provider ?? "No provider"} with {totalEvents:N0} events across {latestSession.Symbols.Length} symbol(s).";

        return new WorkspaceQueueItem { Title = "Collection sessions", Detail = detail, StatusLabel = latestSession.Status, CountLabel = totalEvents > 0 ? $"{totalEvents:N0} events" : $"{latestSession.Symbols.Length} symbol(s)", Tone = ResolveSessionTone(latestSession), PrimaryActionId = "CollectionSessions", PrimaryActionLabel = "Open Sessions", SecondaryActionId = "Storage", SecondaryActionLabel = "Storage" };
    }

    private static WorkspaceQueueItem BuildStorageItem(StorageStatsSummary? storageStats, StorageHealthReport? storageHealth, int storageIssueCount, int criticalStorageIssueCount)
    {
        if (storageStats is null && storageHealth is null)
        {
            return new WorkspaceQueueItem { Title = "Storage health", Detail = "Storage stats are unavailable. Open Storage to inspect persistence health and reconnect the backend before the next export or package handoff.", StatusLabel = "Unavailable", CountLabel = "No stats", Tone = WorkspaceTone.Warning, PrimaryActionId = "Storage", PrimaryActionLabel = "Open Storage", SecondaryActionId = "PackageManager", SecondaryActionLabel = "Packages" };
        }

        var statusLabel = criticalStorageIssueCount > 0 ? "Critical" : storageIssueCount > 0 ? "Review" : "Healthy";
        var countLabel = storageStats is not null ? $"{storageStats.UsedPercentage:F0}% used" : $"{storageIssueCount} issue(s)";
        var detail = storageIssueCount > 0
            ? $"{storageIssueCount} storage issue(s) detected. {(storageHealth?.Issues.FirstOrDefault()?.Description ?? storageHealth?.Recommendations.FirstOrDefault() ?? "Open Storage to review the affected paths.")}"
            : storageStats is not null
                ? $"{storageStats.TotalSymbols} symbol(s) are stored across {storageStats.TotalFiles:N0} files. Latest data landed at {storageStats.NewestData:g}."
                : "Storage metrics are partially available. Open Storage to review persistence posture.";

        return new WorkspaceQueueItem { Title = "Storage posture", Detail = detail, StatusLabel = statusLabel, CountLabel = countLabel, Tone = criticalStorageIssueCount > 0 ? WorkspaceTone.Danger : storageIssueCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info, PrimaryActionId = "Storage", PrimaryActionLabel = "Open Storage", SecondaryActionId = "PackageManager", SecondaryActionLabel = "Packages" };
    }

    private static WorkspaceQueueItem BuildExportItem(ExportJob? latestExportJob, int exportRunningCount, int exportQueuedCount, int exportFailedCount, int totalJobs)
    {
        if (latestExportJob is null)
        {
            return new WorkspaceQueueItem { Title = "Export jobs", Detail = "No export jobs are staged. Use Data Export to create new handoff packages or Schedules to automate recurring delivery.", StatusLabel = "Idle", CountLabel = "No jobs", Tone = WorkspaceTone.Neutral, PrimaryActionId = "DataExport", PrimaryActionLabel = "Data Export", SecondaryActionId = "Schedules", SecondaryActionLabel = "Schedules" };
        }

        var latestRun = latestExportJob.RunHistory.OrderByDescending(run => run.CompletedAt ?? run.StartedAt).FirstOrDefault();
        var detail = latestRun is not null
            ? $"Most recent export '{DisplayExportJobName(latestExportJob)}' is {latestExportJob.Status.ToString().ToLowerInvariant()} with {latestRun.FilesExported:N0} file(s) and {StorageServiceBase.FormatBytes(latestRun.BytesExported)} delivered."
            : $"Export job '{DisplayExportJobName(latestExportJob)}' is {latestExportJob.Status.ToString().ToLowerInvariant()} and has delivered {latestExportJob.TotalFilesExported:N0} file(s) so far.";
        var countLabel = exportRunningCount > 0 ? $"{exportRunningCount} active" : exportQueuedCount > 0 ? $"{exportQueuedCount} queued" : exportFailedCount > 0 ? $"{exportFailedCount} failed" : $"{totalJobs} saved";
        var statusLabel = exportRunningCount > 0 ? "Running" : exportQueuedCount > 0 ? "Queued" : exportFailedCount > 0 ? "Failed" : "Ready";

        return new WorkspaceQueueItem { Title = "Export jobs", Detail = detail, StatusLabel = statusLabel, CountLabel = countLabel, Tone = exportFailedCount > 0 ? WorkspaceTone.Warning : exportRunningCount > 0 || exportQueuedCount > 0 ? WorkspaceTone.Info : WorkspaceTone.Neutral, PrimaryActionId = "DataExport", PrimaryActionLabel = "Data Export", SecondaryActionId = "Schedules", SecondaryActionLabel = "Schedules" };
    }

    private static IReadOnlyList<WorkspaceRecentItem> BuildRecentOperations(DataOperationsWorkspaceData data, BackfillExecution? latestExecution, CollectionSession? latestSession, ExportJob? latestExportJob)
    {
        var items = new List<(DateTimeOffset Timestamp, WorkspaceRecentItem Item)>();

        if (latestSession is not null)
        {
            var totalEvents = latestSession.Statistics?.TotalEvents ?? 0;
            items.Add((ToOffset(GetSessionTimestamp(latestSession)), new WorkspaceRecentItem { Title = latestSession.Name, Detail = latestSession.Status.Equals(SessionStatus.Active, StringComparison.OrdinalIgnoreCase) ? $"{latestSession.Provider ?? "No provider"} session is active with {totalEvents:N0} events captured." : $"{latestSession.Status} session across {latestSession.Symbols.Length} symbol(s) with {totalEvents:N0} captured events.", Meta = $"{GetSessionTimestamp(latestSession):g} · {latestSession.Status}", Tone = ResolveSessionTone(latestSession), ActionId = "CollectionSessions", ActionLabel = "Open Sessions" }));
        }

        var latestResumable = data.ResumableJobs.OrderByDescending(job => job.CompletedAt ?? job.CreatedAt).FirstOrDefault();
        if (latestResumable is not null)
        {
            items.Add((ToOffset(latestResumable.CompletedAt ?? latestResumable.CreatedAt), new WorkspaceRecentItem { Title = $"{latestResumable.Provider} backfill", Detail = $"{latestResumable.PendingCount} symbol(s) remain pending from {latestResumable.FromDate:d} to {latestResumable.ToDate:d}.", Meta = $"{(latestResumable.CompletedAt ?? latestResumable.CreatedAt):g} · {latestResumable.Status}", Tone = latestResumable.Status == CheckpointStatus.Failed ? WorkspaceTone.Warning : WorkspaceTone.Info, ActionId = "Backfill", ActionLabel = "Open Backfill" }));
        }
        else if (latestExecution is not null)
        {
            items.Add((ToOffset(latestExecution.CompletedAt ?? latestExecution.StartedAt), new WorkspaceRecentItem { Title = "Backfill execution", Detail = $"{latestExecution.Status} after {latestExecution.SymbolsProcessed} symbol(s) and {latestExecution.BarsDownloaded:N0} downloaded bars.", Meta = $"{(latestExecution.CompletedAt ?? latestExecution.StartedAt):g} · {latestExecution.Status}", Tone = IsFailedExecution(latestExecution) ? WorkspaceTone.Warning : WorkspaceTone.Info, ActionId = "Backfill", ActionLabel = "Open Backfill" }));
        }
        else if (data.LastBackfillStatus is { } lastBackfillStatus)
        {
            var timestamp = lastBackfillStatus.CompletedUtc ?? lastBackfillStatus.StartedUtc ?? data.RetrievedAt;
            items.Add((timestamp, new WorkspaceRecentItem { Title = "Backfill result", Detail = $"{(lastBackfillStatus.Success ? "Completed" : "Failed")} via {lastBackfillStatus.Provider ?? "unknown provider"} after writing {lastBackfillStatus.BarsWritten:N0} bars.", Meta = $"{timestamp.LocalDateTime:g} · {(lastBackfillStatus.Success ? "Completed" : "Failed")}", Tone = lastBackfillStatus.Success ? WorkspaceTone.Info : WorkspaceTone.Warning, ActionId = "Backfill", ActionLabel = "Open Backfill" }));
        }

        if (latestExportJob is not null)
        {
            var latestRun = latestExportJob.RunHistory.OrderByDescending(run => run.CompletedAt ?? run.StartedAt).FirstOrDefault();
            var detail = latestRun is not null ? $"{latestExportJob.Status} with {latestRun.FilesExported:N0} file(s) and {StorageServiceBase.FormatBytes(latestRun.BytesExported)} delivered." : $"{latestExportJob.Status} export job with {latestExportJob.TotalFilesExported:N0} total file(s) written so far.";
            items.Add((ToOffset(GetExportTimestamp(latestExportJob)), new WorkspaceRecentItem { Title = DisplayExportJobName(latestExportJob), Detail = detail, Meta = $"{GetExportTimestamp(latestExportJob):g} · {latestExportJob.Format}", Tone = latestExportJob.Status == ExportJobStatus.Failed ? WorkspaceTone.Warning : WorkspaceTone.Info, ActionId = "DataExport", ActionLabel = "Open Data Export" }));
        }

        foreach (var notification in data.Notifications)
        {
            if (items.Count >= 3)
            {
                break;
            }

            var actionId = ResolveNotificationAction(notification);
            items.Add((ToOffset(notification.Timestamp), new WorkspaceRecentItem { Title = notification.Title, Detail = notification.Message, Meta = $"{notification.Timestamp:g} · {notification.Type}", Tone = notification.IsRead ? WorkspaceTone.Neutral : WorkspaceTone.Warning, ActionId = actionId, ActionLabel = ResolveNotificationActionLabel(actionId) }));
        }

        return items.Count > 0
            ? items.OrderByDescending(item => item.Timestamp).Take(3).Select(item => item.Item).ToArray()
            :
            [
                new WorkspaceRecentItem { Title = "Provider health", Detail = "Inspect provider reachability and fallback order before starting new historical work.", Meta = "No recent operations", Tone = WorkspaceTone.Info, ActionId = "ProviderHealth", ActionLabel = "Provider Health" },
                new WorkspaceRecentItem { Title = "Backfill queue", Detail = "Stage historical coverage or review resumable jobs from the shell.", Meta = "No recent operations", Tone = WorkspaceTone.Neutral, ActionId = "Backfill", ActionLabel = "Open Backfill" },
                new WorkspaceRecentItem { Title = "Export delivery", Detail = "Create or inspect export jobs without leaving Data Operations.", Meta = "No recent operations", Tone = WorkspaceTone.Neutral, ActionId = "DataExport", ActionLabel = "Open Data Export" }
            ];
    }

    private static WorkspaceQueueRegionState BuildQueueRegionState(
        bool isLoading,
        bool hasError,
        bool isEmpty,
        string loadingTitle,
        string loadingDescription,
        string emptyTitle,
        string emptyDescription,
        string emptyPrimaryLabel,
        string emptyPrimaryAction,
        string emptySecondaryLabel,
        string emptySecondaryAction,
        string errorTitle,
        string errorDescription,
        string errorPrimaryLabel,
        string errorPrimaryAction,
        string errorSecondaryLabel,
        string errorSecondaryAction)
    {
        if (isLoading)
        {
            return WorkspaceQueueRegionState.Loading(loadingTitle, loadingDescription);
        }

        if (hasError)
        {
            return WorkspaceQueueRegionState.Error(errorTitle, errorDescription, errorPrimaryLabel, errorPrimaryAction, errorSecondaryLabel, errorSecondaryAction);
        }

        if (isEmpty)
        {
            return WorkspaceQueueRegionState.Empty(emptyTitle, emptyDescription, emptyPrimaryLabel, emptyPrimaryAction, emptySecondaryLabel, emptySecondaryAction);
        }

        return WorkspaceQueueRegionState.None;
    }

    private static string ResolveNotificationAction(NotificationHistoryItem notification)
    {
        var combined = $"{notification.Title} {notification.Message}";
        if (combined.Contains("export", StringComparison.OrdinalIgnoreCase))
            return "DataExport";
        if (combined.Contains("storage", StringComparison.OrdinalIgnoreCase))
            return "Storage";
        if (combined.Contains("schedule", StringComparison.OrdinalIgnoreCase))
            return "Schedules";
        if (combined.Contains("provider", StringComparison.OrdinalIgnoreCase))
            return "ProviderHealth";
        if (combined.Contains("backfill", StringComparison.OrdinalIgnoreCase))
            return "Backfill";
        return "CollectionSessions";
    }

    private static string ResolveNotificationActionLabel(string actionId) => actionId switch
    {
        "DataExport" => "Open Data Export",
        "Storage" => "Open Storage",
        "Schedules" => "Open Schedules",
        "ProviderHealth" => "Provider Health",
        "Backfill" => "Open Backfill",
        _ => "Open Sessions"
    };

    private static int GetProviderCount(DataOperationsWorkspaceData data) => Math.Max(Math.Max(data.Providers.Count, data.ProviderStatus?.AvailableProviders.Count ?? 0), data.BackfillHealth?.Providers?.Count ?? 0);
    private static int GetHealthyProviderCount(DataOperationsWorkspaceData data, int providerCount) => providerCount == 0 ? 0 : Math.Min(providerCount, Math.Max(data.BackfillHealth?.Providers?.Count(entry => entry.Value.IsAvailable) ?? 0, data.ProviderStatus?.IsConnected == true ? 1 : 0));
    private static bool IsFailedExecution(BackfillExecution? execution) => execution?.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsActiveExport(ExportJob job) => job.Status is ExportJobStatus.Running or ExportJobStatus.Pending or ExportJobStatus.Queued;
    private static bool IsCriticalIssue(StorageIssue issue) => issue.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase) || issue.Severity.Equals("High", StringComparison.OrdinalIgnoreCase) || issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase);
    private static string ResolveSessionTone(CollectionSession session) => session.Status.Equals(SessionStatus.Completed, StringComparison.OrdinalIgnoreCase) ? WorkspaceTone.Success : session.Status.Equals(SessionStatus.Failed, StringComparison.OrdinalIgnoreCase) ? WorkspaceTone.Warning : WorkspaceTone.Info;
    private static string DisplayExportJobName(ExportJob job) => string.IsNullOrWhiteSpace(job.Name) ? "Export job" : job.Name.Trim();
    private static DateTime GetSessionTimestamp(CollectionSession session) => session.UpdatedAt != default ? session.UpdatedAt : session.EndedAt ?? session.StartedAt ?? session.CreatedAt;
    private static DateTime GetExportTimestamp(ExportJob job) => job.LastRunAt ?? job.RunHistory.OrderByDescending(run => run.CompletedAt ?? run.StartedAt).FirstOrDefault()?.CompletedAt ?? job.RunHistory.OrderByDescending(run => run.CompletedAt ?? run.StartedAt).FirstOrDefault()?.StartedAt ?? job.CreatedAt;
    private static DateTimeOffset ToOffset(DateTime value) => value.Kind == DateTimeKind.Utc ? new DateTimeOffset(value) : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local));
}

public sealed class DataOperationsWorkspaceData
{
    public string ScopeLabel { get; init; } = "Provider and storage posture";
    public string ScopeSummary { get; init; } = "Provider posture, backfill priority, storage follow-up, and export delivery stay in one fixed shell.";
    public DateTimeOffset RetrievedAt { get; init; } = DateTimeOffset.Now;
    public int UnreadAlerts { get; init; }
    public IReadOnlyList<NotificationHistoryItemModel> Notifications { get; init; } = Array.Empty<NotificationHistoryItemModel>();
    public IReadOnlyList<ProviderInfoModel> Providers { get; init; } = Array.Empty<ProviderInfoModel>();
    public StatusProviderInfoModel? ProviderStatus { get; init; }
    public BackfillHealthResponse? BackfillHealth { get; init; }
    public BackfillResultDto? LastBackfillStatus { get; init; }
    public IReadOnlyList<BackfillExecution> BackfillExecutions { get; init; } = Array.Empty<BackfillExecution>();
    public IReadOnlyList<BackfillCheckpoint> ResumableJobs { get; init; } = Array.Empty<BackfillCheckpoint>();
    public IReadOnlyList<BackfillSchedule> BackfillSchedules { get; init; } = Array.Empty<BackfillSchedule>();
    public StorageStatsSummary? StorageStats { get; init; }
    public StorageHealthReport? StorageHealth { get; init; }
    public CollectionSession? ActiveSession { get; init; }
    public IReadOnlyList<CollectionSession> Sessions { get; init; } = Array.Empty<CollectionSession>();
    public IReadOnlyList<ExportJob> ExportJobs { get; init; } = Array.Empty<ExportJob>();
}

public sealed class DataOperationsWorkspacePresentation
{
    public WorkspaceShellContextInput Context { get; init; } = new();
    public WorkspaceCommandGroup CommandGroup { get; init; } = new();
    public DataOperationsHeroState HeroState { get; init; } = DataOperationsHeroState.Loading();
    public string QueueScopeBadgeText { get; init; } = string.Empty;
    public string QueueSummaryText { get; init; } = string.Empty;
    public IReadOnlyList<WorkspaceQueueItem> ProviderQueueItems { get; init; } = Array.Empty<WorkspaceQueueItem>();
    public IReadOnlyList<WorkspaceQueueItem> BackfillQueueItems { get; init; } = Array.Empty<WorkspaceQueueItem>();
    public IReadOnlyList<WorkspaceQueueItem> StorageQueueItems { get; init; } = Array.Empty<WorkspaceQueueItem>();
    public string OperationsSummaryTitleText { get; init; } = "Data Operations";
    public string OperationsSummaryDetailText { get; init; } = string.Empty;
    public string SummaryProvidersText { get; init; } = DataOperationsWorkspacePresentationBuilder.ProvidersUnavailableSummary;
    public string SummaryProvidersTone { get; init; } = WorkspaceTone.Warning;
    public string SummaryBackfillText { get; init; } = DataOperationsWorkspacePresentationBuilder.BackfillUnavailableSummary;
    public string SummaryBackfillTone { get; init; } = WorkspaceTone.Neutral;
    public string SummaryStorageText { get; init; } = DataOperationsWorkspacePresentationBuilder.StorageUnavailableSummary;
    public string SummaryStorageTone { get; init; } = WorkspaceTone.Success;
    public IReadOnlyList<WorkspaceRecentItem> RecentOperations { get; init; } = Array.Empty<WorkspaceRecentItem>();
    public WorkspaceQueueRegionState ProviderQueueState { get; init; } = WorkspaceQueueRegionState.None;
    public WorkspaceQueueRegionState BackfillQueueState { get; init; } = WorkspaceQueueRegionState.None;
    public WorkspaceQueueRegionState StorageQueueState { get; init; } = WorkspaceQueueRegionState.None;
}

public sealed class DataOperationsHeroState
{
    public string FocusText { get; init; } = "Operational posture";
    public string SummaryText { get; init; } = "Refreshing provider, backfill, storage, and export posture.";
    public string BadgeText { get; init; } = "Loading";
    public string BadgeTone { get; init; } = WorkspaceTone.Info;
    public string HandoffTitleText { get; init; } = "Telemetry refresh in progress";
    public string HandoffDetailText { get; init; } = "The Data Operations shell is collecting current readiness signals.";
    public string PrimaryActionId { get; init; } = string.Empty;
    public string PrimaryActionLabel { get; init; } = string.Empty;
    public string SecondaryActionId { get; init; } = string.Empty;
    public string SecondaryActionLabel { get; init; } = string.Empty;
    public string TargetText { get; init; } = "Target: Current shell";

    public static DataOperationsHeroState Loading() =>
        new();

    public static DataOperationsHeroState Error() =>
        new()
        {
            FocusText = "Operational posture",
            SummaryText = "Telemetry refresh failed for the current Data Operations scope.",
            BadgeText = "Degraded",
            BadgeTone = WorkspaceTone.Warning,
            HandoffTitleText = "Retry the shell refresh, then inspect diagnostics if the shell stays degraded",
            HandoffDetailText = "Provider, backfill, storage, or export telemetry could not be refreshed from the local workstation services.",
            PrimaryActionId = "Retry",
            PrimaryActionLabel = "Retry",
            SecondaryActionId = "Diagnostics",
            SecondaryActionLabel = "Diagnostics",
            TargetText = "Target: Refresh current shell"
        };
}
