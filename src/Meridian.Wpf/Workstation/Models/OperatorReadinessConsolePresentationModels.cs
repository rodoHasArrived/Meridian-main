using System.Globalization;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Workstation.Models;

public sealed record OperatorReadinessFactModel(
    string Label,
    string Value,
    string Detail);

public sealed record OperatorReadinessGateRowModel(
    string GateId,
    string Label,
    string StatusText,
    WorkstationReadinessTone ReadinessTone,
    string Tone,
    string Detail,
    string? RequiredNextAction);

public sealed record OperatorReadinessPanelRowModel(
    string Id,
    string Label,
    string Value,
    string Detail,
    string Meta,
    WorkstationReadinessTone ReadinessTone,
    string Tone);

public sealed record OperatorReadinessWorkItemRowModel(
    string WorkItemId,
    string Label,
    string Detail,
    string ToneText,
    WorkstationReadinessTone ReadinessTone,
    string Tone,
    string WorkspaceText,
    string CreatedText,
    string TargetPageTag,
    int PriorityScore,
    string? PriorityExplanation);

/// <summary>
/// Projects the server-owned readiness contracts (trading operator readiness, operator inbox,
/// reconciliation break queue, strategy run summaries) into desktop console rows. All gate and
/// severity truth stays server-side; this mapper only restyles statuses and resolves navigation
/// targets against the registered shell catalog.
/// </summary>
public static class OperatorReadinessConsoleMapper
{
    private const int MaxWorkItemRows = 6;
    private const int MaxRunRows = 5;
    private const int MaxBreakRows = 5;

    public static WorkstationReadinessTone ToReadinessTone(TradingAcceptanceGateStatusDto status)
        => status switch
        {
            TradingAcceptanceGateStatusDto.Ready => WorkstationReadinessTone.EvidenceLinked,
            TradingAcceptanceGateStatusDto.ReviewRequired => WorkstationReadinessTone.SignoffRequired,
            TradingAcceptanceGateStatusDto.Blocked => WorkstationReadinessTone.Blocked,
            _ => WorkstationReadinessTone.Neutral
        };

    public static WorkstationReadinessTone ToReadinessTone(OperatorWorkItemToneDto tone)
        => tone switch
        {
            OperatorWorkItemToneDto.Critical => WorkstationReadinessTone.Blocked,
            OperatorWorkItemToneDto.Warning => WorkstationReadinessTone.SignoffRequired,
            OperatorWorkItemToneDto.Success => WorkstationReadinessTone.EvidenceLinked,
            _ => WorkstationReadinessTone.Neutral
        };

    public static IReadOnlyList<OperatorReadinessGateRowModel> BuildGateRows(TradingOperatorReadinessDto readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        var rows = new List<OperatorReadinessGateRowModel>(readiness.AcceptanceGates.Count + 1);
        var overallTone = ToReadinessTone(readiness.OverallStatus);
        var overallDetail = readiness.ReadyForPaperOperation
            ? readiness.ReadyForLiveOperation
                ? "Paper and live operation gates are satisfied."
                : $"Paper operation is ready; live operation has {Pluralize(readiness.LiveOperationBlockers.Count, "blocker")}."
            : "Paper operation gates are not satisfied yet.";
        rows.Add(new OperatorReadinessGateRowModel(
            "overall",
            "Overall readiness",
            FormatStatus(readiness.OverallStatus),
            overallTone,
            ToWorkspaceTone(overallTone),
            overallDetail,
            RequiredNextAction: null));

        foreach (var gate in readiness.AcceptanceGates)
        {
            var tone = ToReadinessTone(gate.Status);
            rows.Add(new OperatorReadinessGateRowModel(
                gate.GateId,
                gate.Label,
                FormatStatus(gate.Status),
                tone,
                ToWorkspaceTone(tone),
                gate.Detail,
                gate.RequiredNextAction));
        }

        return rows;
    }

    public static IReadOnlyList<OperatorReadinessPanelRowModel> BuildSessionRows(TradingOperatorReadinessDto readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        if (readiness.ActiveSession is null)
        {
            return
            [
                new OperatorReadinessPanelRowModel(
                    "active-session",
                    "Active paper session",
                    "None",
                    "No paper session is currently active.",
                    Pluralize(readiness.Sessions.Count, "retained session"),
                    WorkstationReadinessTone.SignoffRequired,
                    WorkspaceTone.Warning)
            ];
        }

        var session = readiness.ActiveSession;
        var sessionTone = session.IsActive
            ? WorkstationReadinessTone.EvidenceLinked
            : WorkstationReadinessTone.SignoffRequired;
        var rows = new List<OperatorReadinessPanelRowModel>
        {
            new(
                "active-session",
                "Active paper session",
                session.SessionId,
                session.StrategyName ?? session.StrategyId,
                $"{Pluralize(session.OrderCount, "order")} · {Pluralize(session.PositionCount, "position")} · {Pluralize(session.SymbolCount, "symbol")}",
                sessionTone,
                ToWorkspaceTone(sessionTone)),
            new(
                "paper-equity",
                "Paper equity",
                session.PortfolioValue is null
                    ? "Unavailable"
                    : session.PortfolioValue.Value.ToString("N0", CultureInfo.InvariantCulture),
                $"Initial cash {session.InitialCash.ToString("N0", CultureInfo.InvariantCulture)}",
                $"Created {FormatTimestamp(session.CreatedAt)}",
                session.PortfolioValue is null
                    ? WorkstationReadinessTone.SignoffRequired
                    : WorkstationReadinessTone.EvidenceLinked,
                session.PortfolioValue is null ? WorkspaceTone.Warning : WorkspaceTone.Success)
        };

        // The replay row is always present so a missing verification reads as an explicit
        // review state instead of silently disappearing (matches the browser session facts).
        var replayConsistent = readiness.Replay?.IsConsistent == true;
        var replayTone = replayConsistent
            ? WorkstationReadinessTone.EvidenceLinked
            : WorkstationReadinessTone.SignoffRequired;
        rows.Add(new OperatorReadinessPanelRowModel(
            "replay-coverage",
            "Replay coverage",
            replayConsistent ? "Consistent" : "Verify",
            readiness.Replay is null
                ? "No replay verification is attached to the active readiness snapshot."
                : $"{readiness.Replay.ComparedFillCount} fills · {readiness.Replay.ComparedOrderCount} orders · {readiness.Replay.ComparedLedgerEntryCount} ledger entries compared",
            readiness.Replay?.VerificationAuditId ?? "No verification audit",
            replayTone,
            ToWorkspaceTone(replayTone)));

        return rows;
    }

    public static IReadOnlyList<OperatorReadinessPanelRowModel> BuildTrustRows(TradingOperatorReadinessDto readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        var trustGate = readiness.TrustGate;
        var trustTone = trustGate.Blockers.Count > 0
            ? WorkstationReadinessTone.Blocked
            : !trustGate.OperatorSignoffRequired || IsOperatorSignoffComplete(trustGate.OperatorSignoffStatus)
                ? WorkstationReadinessTone.EvidenceLinked
                : WorkstationReadinessTone.SignoffRequired;
        var rows = new List<OperatorReadinessPanelRowModel>
        {
            new(
                "trust-gate",
                "Provider trust gate",
                trustGate.Status,
                trustGate.Detail,
                $"{trustGate.ReadySampleCount}/{trustGate.RequiredSampleCount} samples ready · {Pluralize(trustGate.ValidatedEvidenceDocumentCount, "evidence document")}",
                trustTone,
                ToWorkspaceTone(trustTone))
        };

        if (readiness.BrokerageSync is not null)
        {
            var sync = readiness.BrokerageSync;
            var healthy = sync.Health == WorkstationBrokerageSyncHealth.Healthy && !sync.IsStale;
            var syncTone = healthy
                ? WorkstationReadinessTone.EvidenceLinked
                : sync.Health == WorkstationBrokerageSyncHealth.Failed
                    ? WorkstationReadinessTone.Blocked
                    : WorkstationReadinessTone.SignoffRequired;
            rows.Add(new OperatorReadinessPanelRowModel(
                "brokerage-sync",
                "Brokerage sync",
                sync.Health.ToString(),
                sync.LastError ?? $"{Pluralize(sync.PositionCount, "position")} · {Pluralize(sync.OpenOrderCount, "open order")} · {Pluralize(sync.FillCount, "fill")}",
                sync.ProviderId ?? "No provider linked",
                syncTone,
                ToWorkspaceTone(syncTone)));
        }

        return rows;
    }

    public static IReadOnlyList<OperatorReadinessPanelRowModel> BuildPromotionRows(TradingOperatorReadinessDto readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        if (readiness.Promotion is null)
        {
            return
            [
                new OperatorReadinessPanelRowModel(
                    "promotion",
                    "Promotion",
                    "None pending",
                    "No promotion decision is currently awaiting review.",
                    string.Empty,
                    WorkstationReadinessTone.Neutral,
                    WorkspaceTone.Neutral)
            ];
        }

        var promotion = readiness.Promotion;
        // The server's own promotion acceptance gate is the tone authority when present — a
        // promotion with RequiresReview == false can still have an incomplete trace, and the gate
        // is where the server expresses that. RequiresReview is only the fallback signal.
        var promotionGate = readiness.AcceptanceGates.FirstOrDefault(
            static gate => string.Equals(gate.GateId, "promotion", StringComparison.OrdinalIgnoreCase));
        var tone = promotionGate is not null
            ? ToReadinessTone(promotionGate.Status)
            : promotion.RequiresReview
                ? WorkstationReadinessTone.SignoffRequired
                : WorkstationReadinessTone.EvidenceLinked;
        return
        [
            new OperatorReadinessPanelRowModel(
                "promotion",
                "Promotion",
                promotion.State,
                promotion.Reason,
                promotion.ApprovalStatus is null
                    ? promotion.SourceRunId ?? string.Empty
                    : $"Approval {promotion.ApprovalStatus} · {promotion.SourceRunId}",
                tone,
                ToWorkspaceTone(tone))
        ];
    }

    public static IReadOnlyList<OperatorReadinessPanelRowModel> BuildRunRows(StrategyWorkspaceSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return summary.RecentRuns
            .Take(MaxRunRows)
            .Select(run =>
            {
                // Only a completed run earns the success tone; everything else (running, failed,
                // cancelled) stays neutral like the browser's latest-runs panel, with review
                // states escalated to a sign-off tone.
                var tone = run.StatusLabel.Contains("review", StringComparison.OrdinalIgnoreCase)
                    ? WorkstationReadinessTone.SignoffRequired
                    : run.StatusLabel.Equals("Completed", StringComparison.OrdinalIgnoreCase)
                        ? WorkstationReadinessTone.EvidenceLinked
                        : WorkstationReadinessTone.Neutral;
                return new OperatorReadinessPanelRowModel(
                    run.RunId,
                    run.StrategyName,
                    run.StatusLabel,
                    $"P&L {run.NetPnlFormatted} · Return {run.TotalReturnFormatted}",
                    run.RunId,
                    tone,
                    ToWorkspaceTone(tone));
            })
            .ToArray();
    }

    public static IReadOnlyList<OperatorReadinessPanelRowModel> BuildBreakRows(
        IReadOnlyList<ReconciliationBreakQueueItem> breaks)
    {
        ArgumentNullException.ThrowIfNull(breaks);

        // Server break-queue order is preserved (it already encodes severity/priority); re-sorting
        // by detection time here would surface different rows than the browser console shows.
        return breaks
            .Where(static item => item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview)
            .Take(MaxBreakRows)
            .Select(static item =>
            {
                var tone = item.Status == ReconciliationBreakQueueStatus.Open
                    ? WorkstationReadinessTone.Blocked
                    : WorkstationReadinessTone.SignoffRequired;
                return new OperatorReadinessPanelRowModel(
                    item.BreakId,
                    string.IsNullOrWhiteSpace(item.StrategyName) ? item.BreakId : item.StrategyName,
                    $"{item.Category} · {item.Status}",
                    item.Reason,
                    $"Variance {item.Variance.ToString("N2", CultureInfo.InvariantCulture)} · Detected {FormatTimestamp(item.DetectedAt)}",
                    tone,
                    ToWorkspaceTone(tone));
            })
            .ToArray();
    }

    /// <summary>
    /// Merges the readiness and inbox work-item feeds and resolves each row to a registered shell
    /// page tag. Duplicate ids keep the more severe tone (then the newer timestamp) and the queue
    /// orders severity-first, matching the browser console and the server-side inbox dedup —
    /// PriorityScore alone cannot order the merge because only inbox items carry a score.
    /// </summary>
    public static IReadOnlyList<OperatorReadinessWorkItemRowModel> BuildWorkItemRows(
        IReadOnlyList<OperatorWorkItemDto> inboxItems,
        IReadOnlyList<OperatorWorkItemDto> readinessItems,
        Func<string, bool> isRegisteredPageTag)
    {
        ArgumentNullException.ThrowIfNull(inboxItems);
        ArgumentNullException.ThrowIfNull(readinessItems);
        ArgumentNullException.ThrowIfNull(isRegisteredPageTag);

        var merged = new Dictionary<string, OperatorWorkItemDto>(StringComparer.Ordinal);
        foreach (var item in readinessItems.Concat(inboxItems))
        {
            if (!merged.TryGetValue(item.WorkItemId, out var existing) || ShouldReplaceWorkItem(existing, item))
            {
                merged[item.WorkItemId] = item;
            }
        }

        return merged.Values
            .OrderBy(static item => TonePriority(item.Tone))
            .ThenByDescending(static item => item.CreatedAt)
            .Take(MaxWorkItemRows)
            .Select(item =>
            {
                var tone = ToReadinessTone(item.Tone);
                return new OperatorReadinessWorkItemRowModel(
                    item.WorkItemId,
                    item.Label,
                    item.Detail,
                    item.Tone.ToString(),
                    tone,
                    ToWorkspaceTone(tone),
                    item.Workspace ?? "Workstation",
                    FormatTimestamp(item.CreatedAt),
                    ResolveWorkItemPageTag(item, isRegisteredPageTag),
                    item.PriorityScore,
                    item.PriorityExplanation);
            })
            .ToArray();
    }

    /// <summary>
    /// Workspace-shell landing tags are coarse routing targets: an inbox item that carries one
    /// (the shared endpoint labels many kinds with their owning shell) still deserves the deeper
    /// kind-specific page when the desktop has one registered.
    /// </summary>
    private static readonly HashSet<string> CoarseWorkspaceShellTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "TradingShell", "PortfolioShell", "AccountingShell", "ReportingShell",
        "StrategyShell", "DataShell", "SettingsShell"
    };

    public static string ResolveWorkItemPageTag(OperatorWorkItemDto item, Func<string, bool> isRegisteredPageTag)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(isRegisteredPageTag);

        var hasRegisteredTarget = !string.IsNullOrWhiteSpace(item.TargetPageTag) && isRegisteredPageTag(item.TargetPageTag);
        if (hasRegisteredTarget && !CoarseWorkspaceShellTags.Contains(item.TargetPageTag!))
        {
            return item.TargetPageTag!;
        }

        var kindTag = item.Kind switch
        {
            OperatorWorkItemKindDto.PaperReplay => "StrategyRuns",
            OperatorWorkItemKindDto.PromotionReview => "StrategyRuns",
            OperatorWorkItemKindDto.BrokerageSync => "Provider",
            OperatorWorkItemKindDto.SecurityMasterCoverage => "SecurityMaster",
            OperatorWorkItemKindDto.ReconciliationBreak => "FundReconciliation",
            OperatorWorkItemKindDto.ReportPackApproval => "FundReportPack",
            OperatorWorkItemKindDto.ProviderTrustGate => "DataShell",
            OperatorWorkItemKindDto.ExecutionControl => "TradingShell",
            OperatorWorkItemKindDto.LedgerPeriodClose => "FundAccountingClose",
            OperatorWorkItemKindDto.BrokerExecutionReconciliation => "TradingShell",
            _ => null
        };
        if (kindTag is not null && isRegisteredPageTag(kindTag))
        {
            return kindTag;
        }

        if (hasRegisteredTarget)
        {
            return item.TargetPageTag!;
        }

        var workspaceTag = item.Workspace?.Trim().ToLowerInvariant() switch
        {
            "trading" => "TradingShell",
            "portfolio" => "PortfolioShell",
            "accounting" => "AccountingShell",
            "reporting" => "ReportingShell",
            "strategy" => "StrategyShell",
            "data" => "DataShell",
            "settings" => "SettingsShell",
            _ => null
        };
        if (workspaceTag is not null && isRegisteredPageTag(workspaceTag))
        {
            return workspaceTag;
        }

        return "HomeWorkspace";
    }

    public static IReadOnlyList<OperatorReadinessFactModel> BuildSummaryFacts(
        TradingOperatorReadinessDto? readiness,
        OperatorInboxDto? inbox,
        IReadOnlyList<ReconciliationBreakQueueItem>? breaks,
        StrategyWorkspaceSummary? runSummary)
    {
        var readyGateCount = readiness?.AcceptanceGates.Count(static gate => gate.Status == TradingAcceptanceGateStatusDto.Ready) ?? 0;
        var totalGateCount = readiness?.AcceptanceGates.Count ?? 0;
        // The fact counts the full filtered queue, not the display-capped break rows, so the
        // summary stays truthful when more breaks exist than the panel shows.
        var openBreakCount = breaks?.Count(static item =>
            item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview);

        return
        [
            new OperatorReadinessFactModel(
                "Overall",
                readiness is null ? "Unavailable" : FormatStatus(readiness.OverallStatus),
                readiness is null ? "Trading readiness did not load" : $"As of {FormatTimestamp(readiness.AsOf)}"),
            new OperatorReadinessFactModel(
                "Acceptance gates",
                readiness is null
                    ? "Unavailable"
                    : totalGateCount == 0 ? "None" : $"{readyGateCount}/{totalGateCount} ready",
                readiness is null
                    ? "Trading readiness did not load"
                    : totalGateCount == 0 ? "No acceptance gates were returned" : "Server-evaluated readiness gates"),
            new OperatorReadinessFactModel(
                "Inbox",
                inbox is null ? "Unavailable" : Pluralize(inbox.Items.Count, "work item"),
                inbox is null
                    ? "Operator inbox did not load"
                    : $"{inbox.CriticalCount} critical · {inbox.WarningCount} warning · {inbox.ReviewCount} review"),
            new OperatorReadinessFactModel(
                "Reconciliation",
                openBreakCount is null ? "Unavailable" : Pluralize(openBreakCount.Value, "open break"),
                openBreakCount is null
                    ? "Reconciliation break queue did not load"
                    : "Open and in-review break-queue items"),
            new OperatorReadinessFactModel(
                "Strategy runs",
                runSummary is null ? "Unavailable" : Pluralize(runSummary.PendingReviewCount, "pending review"),
                runSummary is null
                    ? "Run summaries did not load"
                    : $"{runSummary.TotalRuns} runs retained · {runSummary.PromotedCount} promoted"),
            new OperatorReadinessFactModel(
                "Warnings",
                Pluralize(readiness?.Warnings.Count ?? 0, "warning"),
                "Warnings retained with the readiness snapshot")
        ];
    }

    public static string FormatStatus(TradingAcceptanceGateStatusDto status)
        => status switch
        {
            TradingAcceptanceGateStatusDto.ReviewRequired => "Review required",
            _ => status.ToString()
        };

    public static string FormatTimestamp(DateTimeOffset value)
        => $"{value.UtcDateTime:yyyy-MM-dd HH:mm} UTC";

    /// <summary>
    /// Replaces a duplicate work item only when the candidate carries a more severe tone, or the
    /// same tone with an equal-or-newer timestamp — the same precedence the browser console and
    /// the server-side inbox dedup use, so a stale low-severity copy never masks a critical one.
    /// </summary>
    private static bool ShouldReplaceWorkItem(OperatorWorkItemDto existing, OperatorWorkItemDto candidate)
    {
        var toneDelta = TonePriority(candidate.Tone) - TonePriority(existing.Tone);
        if (toneDelta != 0)
        {
            return toneDelta < 0;
        }

        return candidate.CreatedAt >= existing.CreatedAt;
    }

    private static int TonePriority(OperatorWorkItemToneDto tone)
        => tone switch
        {
            OperatorWorkItemToneDto.Critical => 0,
            OperatorWorkItemToneDto.Warning => 1,
            OperatorWorkItemToneDto.Info => 2,
            _ => 3
        };

    /// <summary>
    /// Mirrors the exact case-insensitive completion set the readiness server uses
    /// (<c>TradingOperatorReadinessService.IsOperatorSignoffComplete</c>) so substring states like
    /// "unsigned" or "not-signed" never restyle as complete on the desktop.
    /// </summary>
    private static bool IsOperatorSignoffComplete(string status) =>
        string.Equals(status, "signed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

    private static string Pluralize(int count, string singular)
        => count == 1 ? $"1 {singular}" : $"{count} {singular}s";

    private static string ToWorkspaceTone(WorkstationReadinessTone readinessTone)
        => readinessTone switch
        {
            WorkstationReadinessTone.Blocked => WorkspaceTone.Danger,
            WorkstationReadinessTone.SignoffRequired => WorkspaceTone.Warning,
            WorkstationReadinessTone.EvidenceLinked or WorkstationReadinessTone.Ready => WorkspaceTone.Success,
            WorkstationReadinessTone.Recovery or WorkstationReadinessTone.Stale => WorkspaceTone.Warning,
            _ => WorkspaceTone.Neutral
        };
}
