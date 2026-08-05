using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class OperatorReadinessConsoleViewModelTests
{
    private static readonly string[] RegisteredTags =
    [
        "HomeWorkspace", "TradingShell", "PortfolioShell", "AccountingShell", "ReportingShell",
        "StrategyShell", "DataShell", "SettingsShell", "StrategyRuns", "SecurityMaster",
        "FundReconciliation", "FundReportPack", "FundAccountingClose", "Provider", "FundAuditTrail",
        "AccountPortfolio", "RunRisk"
    ];

    private static bool IsRegistered(string tag)
        => RegisteredTags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task RefreshAsync_ProjectsGatesPanelsFactsAndWorkItems()
    {
        var readiness = CreateReadiness();
        var viewModel = CreateViewModel(
            new FakeReadinessProvider { Readiness = readiness },
            new FakeInboxClient { Inbox = CreateInbox() },
            new FakeReconciliationClient { Breaks = CreateBreaks() },
            runWorkspaceService: null);
        using var scope = viewModel;

        await viewModel.RefreshAsync();

        viewModel.OverallStatusText.Should().Be(
            "Blocked", "a critical inbox item and a blocked acceptance gate escalate the headline past the server's Review required status");
        viewModel.OverallTone.Should().Be(WorkstationReadinessTone.Blocked);
        viewModel.AsOfText.Should().Be("2026-08-05 06:00 UTC");
        viewModel.GateRows.Should().HaveCount(3);
        viewModel.GateRows[0].Label.Should().Be("Overall readiness");
        viewModel.GateRows[1].StatusText.Should().Be("Ready");
        viewModel.GateRows[2].StatusText.Should().Be("Blocked");
        viewModel.SessionRows.Should().HaveCount(3, "active session, paper equity, and replay coverage rows");
        viewModel.SessionRows[0].Value.Should().Be("paper-42");
        viewModel.TrustRows.Should().HaveCount(2, "trust gate and brokerage sync rows");
        viewModel.TrustRows[0].ReadinessTone.Should().Be(WorkstationReadinessTone.EvidenceLinked, "signoff is signed and no blockers exist");
        viewModel.PromotionRows.Should().ContainSingle()
            .Which.ReadinessTone.Should().Be(WorkstationReadinessTone.SignoffRequired);
        viewModel.BreakRows.Should().HaveCount(2, "resolved break-queue items are excluded");
        viewModel.WorkItemRows.Should().HaveCount(3, "inbox and readiness feeds merge with duplicate ids deduplicated");
        viewModel.WorkItemRows[0].WorkItemId.Should().Be("wi-critical", "the most severe tone sorts first");
        viewModel.WorkItemRows[0].TargetPageTag.Should().Be("FundReconciliation");
        viewModel.Warnings.Should().Equal("One readiness warning was retained.");
        viewModel.SummaryFacts.Should().HaveCount(6);
        viewModel.SummaryFacts[1].Value.Should().Be("1/2 ready");
        viewModel.HasRunsError.Should().BeTrue("no run workspace service is present in this composition");
        viewModel.HasReadinessError.Should().BeFalse();
        viewModel.HasInboxError.Should().BeFalse();
        viewModel.HasBreaksError.Should().BeFalse();
    }

    [Theory]
    [InlineData("FundReconciliation", OperatorWorkItemKindDto.ReportPackApproval, null, "FundReconciliation")]
    [InlineData("NotARegisteredTag", OperatorWorkItemKindDto.ReportPackApproval, null, "FundReportPack")]
    [InlineData("AccountingShell", OperatorWorkItemKindDto.ReconciliationBreak, null, "FundReconciliation")]
    [InlineData("ReportingShell", OperatorWorkItemKindDto.ReportPackApproval, null, "FundReportPack")]
    [InlineData("AccountingShell", (OperatorWorkItemKindDto)999, null, "AccountingShell")]
    [InlineData("ProviderConnectionCenter", OperatorWorkItemKindDto.BrokerageSync, "settings", "AccountPortfolio")]
    [InlineData("TradingShell", OperatorWorkItemKindDto.PaperReplay, "trading", "FundAuditTrail")]
    [InlineData(null, OperatorWorkItemKindDto.ExecutionControl, "trading", "RunRisk")]
    [InlineData(null, OperatorWorkItemKindDto.LedgerPeriodClose, null, "FundReconciliation")]
    [InlineData(null, (OperatorWorkItemKindDto)999, "reporting", "ReportingShell")]
    [InlineData(null, (OperatorWorkItemKindDto)999, "unknown-lane", "HomeWorkspace")]
    public void ResolveWorkItemPageTag_PrefersRegisteredTargetThenKindThenWorkspace(
        string? targetPageTag,
        OperatorWorkItemKindDto kind,
        string? workspace,
        string expectedTag)
    {
        var item = new OperatorWorkItemDto(
            WorkItemId: "wi-1",
            Kind: kind,
            Label: "Item",
            Detail: "Detail",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
            Workspace: workspace,
            TargetPageTag: targetPageTag);

        OperatorReadinessConsoleMapper.ResolveWorkItemPageTag(item, IsRegistered).Should().Be(expectedTag);
    }

    [Fact]
    public async Task RefreshAsync_MissingSources_DegradePerPanelWithoutThrowing()
    {
        using var viewModel = new OperatorReadinessConsoleViewModel(isRegisteredPageTag: IsRegistered);

        await viewModel.RefreshAsync();

        viewModel.HasReadinessError.Should().BeTrue();
        viewModel.HasInboxError.Should().BeTrue();
        viewModel.HasBreaksError.Should().BeTrue();
        viewModel.HasRunsError.Should().BeTrue();
        viewModel.OverallStatusText.Should().Be("Unavailable");
        viewModel.OverallTone.Should().Be(
            WorkstationReadinessTone.SignoffRequired,
            "a missing readiness payload is a review state the operator must act on, not a neutral one");
        viewModel.GateRows.Should().BeEmpty();
        viewModel.WorkItemRows.Should().BeEmpty();
        viewModel.SummaryFacts.Should().HaveCount(6, "the summary strip stays populated with unavailability facts");
        viewModel.SummaryFacts[0].Value.Should().Be("Unavailable");
    }

    [Fact]
    public async Task RefreshAsync_ReadinessProviderThrow_SurfacesReadinessErrorOnly()
    {
        var viewModel = CreateViewModel(
            new FakeReadinessProvider { Exception = new InvalidOperationException("Readiness projection failed.") },
            new FakeInboxClient { Inbox = CreateInbox() },
            new FakeReconciliationClient { Breaks = CreateBreaks() },
            runWorkspaceService: null);
        using var scope = viewModel;

        await viewModel.RefreshAsync();

        viewModel.ReadinessErrorText.Should().Be("Readiness projection failed.");
        viewModel.OverallStatusText.Should().Be(
            "Blocked", "a critical inbox item escalates the headline even when the readiness payload is missing");
        viewModel.HasInboxError.Should().BeFalse("the inbox loaded independently of the readiness failure");
        viewModel.WorkItemRows.Should().NotBeEmpty("inbox work items still project when readiness fails");
        viewModel.BreakRows.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RefreshAsync_NewerRefreshSupersedesInFlightOne()
    {
        var provider = new FakeReadinessProvider { Readiness = CreateReadiness() };
        var gate = new TaskCompletionSource<TradingOperatorReadinessDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        provider.PendingReadiness = gate.Task;
        var viewModel = CreateViewModel(
            provider,
            new FakeInboxClient { Inbox = CreateInbox() },
            new FakeReconciliationClient { Breaks = CreateBreaks() },
            runWorkspaceService: null);
        using var scope = viewModel;

        var firstRefresh = viewModel.RefreshAsync();
        viewModel.IsRefreshing.Should().BeTrue("the first readiness load is still pending");

        provider.PendingReadiness = null;
        provider.Readiness = CreateReadiness() with { Warnings = ["Second-round warning."] };
        var secondRefresh = viewModel.RefreshAsync();
        await secondRefresh;
        gate.SetResult(CreateReadiness());
        await firstRefresh;

        viewModel.Warnings.Should().Equal(
            new[] { "Second-round warning." },
            "the superseded first refresh must not overwrite the newer projection");
        viewModel.IsRefreshing.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_ReadyReadinessWithCriticalInbox_EscalatesHeadlineToBlocked()
    {
        var readiness = CreateReadiness() with
        {
            OverallStatus = TradingAcceptanceGateStatusDto.Ready,
            AcceptanceGates =
            [
                new TradingAcceptanceGateDto("gate-replay", "Replay parity", TradingAcceptanceGateStatusDto.Ready, "Replay matches persisted state."),
                new TradingAcceptanceGateDto("gate-signoff", "Operator sign-off", TradingAcceptanceGateStatusDto.Ready, "Sign-off is recorded.")
            ]
        };
        using var viewModel = CreateViewModel(
            new FakeReadinessProvider { Readiness = readiness },
            new FakeInboxClient { Inbox = CreateInbox() },
            new FakeReconciliationClient { Breaks = CreateBreaks() },
            runWorkspaceService: null);

        await viewModel.RefreshAsync();

        viewModel.OverallStatusText.Should().Be("Blocked", "a critical inbox item overrides a Ready server status");
        viewModel.OverallTone.Should().Be(WorkstationReadinessTone.Blocked);
        viewModel.GateRows[0].StatusText.Should().Be("Ready", "the gates panel keeps showing the server's raw overall status");
    }

    [Fact]
    public async Task RefreshAsync_ReadyReadinessWithoutInbox_DemotesHeadlineToReviewPending()
    {
        var readiness = CreateReadiness() with
        {
            OverallStatus = TradingAcceptanceGateStatusDto.Ready,
            AcceptanceGates =
            [
                new TradingAcceptanceGateDto("gate-replay", "Replay parity", TradingAcceptanceGateStatusDto.Ready, "Replay matches persisted state.")
            ]
        };
        using var viewModel = CreateViewModel(
            new FakeReadinessProvider { Readiness = readiness },
            new FakeInboxClient { Inbox = null },
            new FakeReconciliationClient { Breaks = [] },
            runWorkspaceService: null);

        await viewModel.RefreshAsync();

        viewModel.OverallStatusText.Should().Be(
            "Review pending", "a Ready headline is demoted while the operator inbox is unavailable");
        viewModel.OverallTone.Should().Be(WorkstationReadinessTone.SignoffRequired);
    }

    [Fact]
    public async Task RefreshAsync_OpenReconciliationBreak_EscalatesHeadlineToBlocked()
    {
        var readiness = CreateReadiness() with
        {
            OverallStatus = TradingAcceptanceGateStatusDto.Ready,
            AcceptanceGates =
            [
                new TradingAcceptanceGateDto("gate-replay", "Replay parity", TradingAcceptanceGateStatusDto.Ready, "Replay matches persisted state.")
            ]
        };
        var quietInbox = new OperatorInboxDto(
            AsOf: DateTimeOffset.Parse("2026-08-05T06:00:00Z"),
            Items: [],
            CriticalCount: 0,
            WarningCount: 0,
            ReviewCount: 0,
            Summary: "No operator work items need attention.");
        using var viewModel = CreateViewModel(
            new FakeReadinessProvider { Readiness = readiness },
            new FakeInboxClient { Inbox = quietInbox },
            new FakeReconciliationClient
            {
                Breaks = [CreateBreak("brk-open", ReconciliationBreakQueueStatus.Open, DateTimeOffset.Parse("2026-08-05T05:00:00Z"))]
            },
            runWorkspaceService: null);

        await viewModel.RefreshAsync();

        viewModel.OverallStatusText.Should().Be(
            "Blocked", "an open reconciliation break folds into the headline like the browser's reconciliation-clear checkpoint");
        viewModel.OverallTone.Should().Be(WorkstationReadinessTone.Blocked);
    }

    [Fact]
    public async Task RefreshAsync_InReviewBreakOnly_DemotesReadyHeadlineToReviewPending()
    {
        var readiness = CreateReadiness() with
        {
            OverallStatus = TradingAcceptanceGateStatusDto.Ready,
            AcceptanceGates =
            [
                new TradingAcceptanceGateDto("gate-replay", "Replay parity", TradingAcceptanceGateStatusDto.Ready, "Replay matches persisted state.")
            ]
        };
        var quietInbox = new OperatorInboxDto(
            AsOf: DateTimeOffset.Parse("2026-08-05T06:00:00Z"),
            Items: [],
            CriticalCount: 0,
            WarningCount: 0,
            ReviewCount: 0,
            Summary: "No operator work items need attention.");
        using var viewModel = CreateViewModel(
            new FakeReadinessProvider { Readiness = readiness },
            new FakeInboxClient { Inbox = quietInbox },
            new FakeReconciliationClient
            {
                Breaks = [CreateBreak("brk-review", ReconciliationBreakQueueStatus.InReview, DateTimeOffset.Parse("2026-08-05T05:00:00Z"))]
            },
            runWorkspaceService: null);

        await viewModel.RefreshAsync();

        viewModel.OverallStatusText.Should().Be(
            "Review pending", "in-review breaks keep the console out of the Ready state without blocking it");
        viewModel.OverallTone.Should().Be(WorkstationReadinessTone.SignoffRequired);
    }

    [Fact]
    public async Task RefreshAsync_BreakQueueOutage_SurfacesBreaksErrorInsteadOfEmptyQueue()
    {
        using var viewModel = CreateViewModel(
            new FakeReadinessProvider { Readiness = CreateReadiness() },
            new FakeInboxClient { Inbox = CreateInbox() },
            new FakeReconciliationClient { Breaks = null },
            runWorkspaceService: null);

        await viewModel.RefreshAsync();

        viewModel.HasBreaksError.Should().BeTrue("a failed break-queue call must not render as an empty queue");
        viewModel.BreakRows.Should().BeEmpty();
        viewModel.SummaryFacts[3].Value.Should().Be("Unavailable");
    }

    [Theory]
    [InlineData("Signed", WorkstationReadinessTone.EvidenceLinked)]
    [InlineData("approved", WorkstationReadinessTone.EvidenceLinked)]
    [InlineData("Completed", WorkstationReadinessTone.EvidenceLinked)]
    [InlineData("Unsigned", WorkstationReadinessTone.SignoffRequired)]
    [InlineData("not-signed", WorkstationReadinessTone.SignoffRequired)]
    [InlineData("Pending", WorkstationReadinessTone.SignoffRequired)]
    public void BuildTrustRows_MapsSignoffStatusWithExactCompletionSet(string signoffStatus, WorkstationReadinessTone expectedTone)
    {
        var readiness = CreateReadiness();
        readiness = readiness with
        {
            TrustGate = readiness.TrustGate with { OperatorSignoffStatus = signoffStatus }
        };

        var rows = OperatorReadinessConsoleMapper.BuildTrustRows(readiness);

        rows[0].ReadinessTone.Should().Be(
            expectedTone, "the desktop must reuse the server's exact sign-off completion set, not substring matching");
    }

    [Fact]
    public void BuildWorkItemRows_DuplicateIds_KeepMoreSevereToneThenNewerTimestamp()
    {
        var readinessItems = new[]
        {
            CreateWorkItem("wi-dupe", OperatorWorkItemToneDto.Critical, DateTimeOffset.Parse("2026-08-05T05:45:00Z"))
        };
        var inboxItems = new[]
        {
            CreateWorkItem("wi-dupe", OperatorWorkItemToneDto.Info, DateTimeOffset.Parse("2026-08-05T02:00:00Z"), priorityScore: 250)
        };

        var rows = OperatorReadinessConsoleMapper.BuildWorkItemRows(inboxItems, readinessItems, IsRegistered);

        rows.Should().ContainSingle()
            .Which.ReadinessTone.Should().Be(
                WorkstationReadinessTone.Blocked,
                "a stale low-severity inbox copy must not mask the critical readiness copy of the same work item");
    }

    [Fact]
    public void BuildWorkItemRows_OrdersSeverityBeforePriorityScore()
    {
        var readinessItems = new[]
        {
            CreateWorkItem("wi-critical-unscored", OperatorWorkItemToneDto.Critical, DateTimeOffset.Parse("2026-08-04T01:00:00Z"))
        };
        var inboxItems = new[]
        {
            CreateWorkItem("wi-success-scored", OperatorWorkItemToneDto.Success, DateTimeOffset.Parse("2026-08-05T05:55:00Z"), priorityScore: 300)
        };

        var rows = OperatorReadinessConsoleMapper.BuildWorkItemRows(inboxItems, readinessItems, IsRegistered);

        rows.Select(static row => row.WorkItemId).Should().Equal(
            new[] { "wi-critical-unscored", "wi-success-scored" },
            "readiness-feed items carry no priority score, so severity must order the merged queue");
    }

    [Fact]
    public void BuildBreakRows_PreservesServerOrderAndCapsRows()
    {
        var breaks = new[]
        {
            CreateBreak("brk-a", ReconciliationBreakQueueStatus.Open, DateTimeOffset.Parse("2026-08-01T00:00:00Z")),
            CreateBreak("brk-b", ReconciliationBreakQueueStatus.Resolved, DateTimeOffset.Parse("2026-08-05T00:00:00Z")),
            CreateBreak("brk-c", ReconciliationBreakQueueStatus.InReview, DateTimeOffset.Parse("2026-08-05T02:00:00Z")),
            CreateBreak("brk-d", ReconciliationBreakQueueStatus.Open, DateTimeOffset.Parse("2026-08-02T00:00:00Z")),
            CreateBreak("brk-e", ReconciliationBreakQueueStatus.Open, DateTimeOffset.Parse("2026-08-04T00:00:00Z")),
            CreateBreak("brk-f", ReconciliationBreakQueueStatus.Open, DateTimeOffset.Parse("2026-08-03T00:00:00Z")),
            CreateBreak("brk-g", ReconciliationBreakQueueStatus.Open, DateTimeOffset.Parse("2026-08-05T03:00:00Z"))
        };

        var rows = OperatorReadinessConsoleMapper.BuildBreakRows(breaks);

        rows.Select(static row => row.Id).Should().Equal(
            new[] { "brk-a", "brk-c", "brk-d", "brk-e", "brk-f" },
            "the server-provided queue order already encodes priority and must survive filtering and the row cap");
    }

    [Fact]
    public void BuildSummaryFacts_CountsFullBreakQueueAndMarksMissingQueueUnavailable()
    {
        var breaks = Enumerable.Range(1, 7)
            .Select(static index => CreateBreak(
                $"brk-{index}",
                ReconciliationBreakQueueStatus.Open,
                DateTimeOffset.Parse("2026-08-05T00:00:00Z")))
            .ToArray();

        var facts = OperatorReadinessConsoleMapper.BuildSummaryFacts(null, null, breaks, null);
        facts[3].Value.Should().Be("7 open breaks", "the fact counts the full filtered queue, not the display-capped rows");

        var unavailable = OperatorReadinessConsoleMapper.BuildSummaryFacts(null, null, breaks: null, null);
        unavailable[3].Value.Should().Be("Unavailable", "a failed reconciliation load must not read as zero breaks");
    }

    [Fact]
    public void BuildRunRows_OnlyCompletedRunsEarnSuccessTone()
    {
        var summary = new StrategyWorkspaceSummary
        {
            RecentRuns =
            [
                new StrategyRunSummaryItem { RunId = "run-1", StrategyName = "S1", StatusLabel = "Completed" },
                new StrategyRunSummaryItem { RunId = "run-2", StrategyName = "S2", StatusLabel = "Failed" },
                new StrategyRunSummaryItem { RunId = "run-3", StrategyName = "S3", StatusLabel = "Needs Review" }
            ]
        };

        var rows = OperatorReadinessConsoleMapper.BuildRunRows(summary);

        rows.Select(static row => row.ReadinessTone).Should().Equal(
            new[]
            {
                WorkstationReadinessTone.EvidenceLinked,
                WorkstationReadinessTone.Neutral,
                WorkstationReadinessTone.SignoffRequired
            },
            "a failed run must not render with the success tone");
    }

    [Fact]
    public void BuildWorkItemRows_SameTone_OrdersByServerPriorityScoreBeforeRecency()
    {
        var inboxItems = new[]
        {
            CreateWorkItem("wi-newer-low", OperatorWorkItemToneDto.Warning, DateTimeOffset.Parse("2026-08-05T06:00:00Z"), priorityScore: 10),
            CreateWorkItem("wi-older-high", OperatorWorkItemToneDto.Warning, DateTimeOffset.Parse("2026-08-04T06:00:00Z"), priorityScore: 300)
        };

        var rows = OperatorReadinessConsoleMapper.BuildWorkItemRows(inboxItems, [], IsRegistered);

        rows.Select(static row => row.WorkItemId).Should().Equal(
            new[] { "wi-older-high", "wi-newer-low" },
            "within a tone the server's priority score outranks recency so top-triage items are never pushed out");
    }

    [Fact]
    public void ResolveWorkItemPageTag_PrefersSharedWorkflowCatalogResolution()
    {
        var item = new OperatorWorkItemDto(
            WorkItemId: "wi-trust",
            Kind: OperatorWorkItemKindDto.ProviderTrustGate,
            Label: "DK1 operator sign-off pending",
            Detail: "Trust gate needs sign-off.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
            Workspace: "Trading",
            TargetRoute: Meridian.Contracts.Api.UiApiRoutes.WorkstationTradingReadiness,
            TargetPageTag: "TradingShell");

        OperatorReadinessConsoleMapper.ResolveWorkItemPageTag(item, IsRegistered, new FakeWorkflowActionCatalog())
            .Should().Be(
                "TradingShell",
                "the shared workflow catalog's answer is the workflow's deliberate entry surface and outranks the kind fallback");

        OperatorReadinessConsoleMapper.ResolveWorkItemPageTag(item, IsRegistered)
            .Should().Be(
                "TradingShell",
                "without a catalog the shared route map resolves the trading-readiness route the same way the main shell does");

        var routelessItem = item with { TargetRoute = null, TargetPageTag = null };
        OperatorReadinessConsoleMapper.ResolveWorkItemPageTag(routelessItem, IsRegistered)
            .Should().Be(
                "FundAuditTrail",
                "a genuinely routeless trust item falls back to the audit-history kind mapping");
    }

    [Fact]
    public void ResolveWorkItemPageTag_ReplayItemsIgnoreSharedTargetsAndRoutes()
    {
        var item = new OperatorWorkItemDto(
            WorkItemId: "wi-replay",
            Kind: OperatorWorkItemKindDto.PaperReplay,
            Label: "Verify replay",
            Detail: "Replay verification is stale.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
            Workspace: "Trading",
            TargetRoute: Meridian.Contracts.Api.UiApiRoutes.WorkstationTradingReadiness,
            TargetPageTag: "TradingShell");

        OperatorReadinessConsoleMapper.ResolveWorkItemPageTag(item, IsRegistered, new FakeWorkflowActionCatalog())
            .Should().Be(
                "FundAuditTrail",
                "replay items ignore shared targets and routes (the browser makes the same exception) and keep the replay-evidence surface");
    }

    [Theory]
    [InlineData("/settings#alpaca-provider-setup")]
    [InlineData("/settings#provider-connection-center")]
    public void ResolveWorkItemPageTag_SettingsProviderRoutes_OpenProviderPage(string targetRoute)
    {
        var item = CreateWorkItem(
            "wi-credentials",
            OperatorWorkItemToneDto.Critical,
            DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
            targetRoute: targetRoute);

        OperatorReadinessConsoleMapper.ResolveWorkItemPageTag(item, IsRegistered).Should().Be(
            "Provider",
            "a settings provider link names the credential workflow, not account holdings");
    }

    [Theory]
    [InlineData("/settings#alpaca-provider-setup")]
    [InlineData("/settings#provider-connection-center")]
    public void ResolveWorkItemPageTag_SettingsProviderRoutes_BeatTheCatalogKindFallback(string targetRoute)
    {
        var item = new OperatorWorkItemDto(
            WorkItemId: "wi-credentials",
            Kind: OperatorWorkItemKindDto.BrokerageSync,
            Label: "Link provider credentials",
            Detail: "Provider credentials are missing.",
            Tone: OperatorWorkItemToneDto.Critical,
            CreatedAt: DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
            Workspace: "Settings",
            TargetRoute: targetRoute,
            TargetPageTag: "ProviderConnectionCenter");

        OperatorReadinessConsoleMapper.ResolveWorkItemPageTag(item, IsRegistered, new FakeWorkflowActionCatalog())
            .Should().Be(
                "Provider",
                "the explicit settings provider route outranks the catalog's brokerage-sync kind binding to account holdings");
    }

    [Fact]
    public void ResolveWorkItemPageTag_LedgerPeriodClose_OpensReconciliationLikeTheMainShell()
    {
        var item = new OperatorWorkItemDto(
            WorkItemId: "wi-period-close",
            Kind: OperatorWorkItemKindDto.LedgerPeriodClose,
            Label: "Sign off period close",
            Detail: "The ledger period is ready for close sign-off.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
            Workspace: "Accounting",
            TargetRoute: Meridian.Contracts.Api.UiApiRoutes.LedgerPeriods,
            TargetPageTag: "AccountingShell");

        OperatorReadinessConsoleMapper.ResolveWorkItemPageTag(item, IsRegistered, new FakeWorkflowActionCatalog())
            .Should().Be(
                "FundReconciliation",
                "period-close sign-off opens the reconciliation queue ahead of the generic ledger route mapping, matching the main shell");

        OperatorReadinessConsoleMapper.ResolveWorkItemPageTag(item, IsRegistered)
            .Should().Be(
                "FundReconciliation",
                "the close special does not depend on the catalog being present");
    }

    [Fact]
    public void BuildWorkItemRows_SameToneDuplicate_KeepsTheScoredInboxCopy()
    {
        var readinessItems = new[]
        {
            CreateWorkItem("wi-dupe", OperatorWorkItemToneDto.Warning, DateTimeOffset.Parse("2026-08-05T05:45:00Z"))
        };
        var inboxItems = new[]
        {
            CreateWorkItem("wi-dupe", OperatorWorkItemToneDto.Warning, DateTimeOffset.Parse("2026-08-05T05:00:00Z"), priorityScore: 90)
        };

        var rows = OperatorReadinessConsoleMapper.BuildWorkItemRows(inboxItems, readinessItems, IsRegistered);

        rows.Should().ContainSingle()
            .Which.PriorityScore.Should().Be(
                90, "a newer unscored readiness copy must not strip the server triage score from the merged row");
    }

    [Fact]
    public void ResolveWorkItemPageTag_HonorsTargetRouteBeforeKindFallback()
    {
        var item = CreateWorkItem(
            "wi-brokerage",
            OperatorWorkItemToneDto.Warning,
            DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
            targetRoute: "/api/fund-accounts/brokerage-sync/accounts?fundAccountId=0e2a1c94-3f5b-4f6c-9a51-1c2d3e4f5a6b");

        OperatorReadinessConsoleMapper.ResolveWorkItemPageTag(item, IsRegistered).Should().Be(
            "AccountPortfolio",
            "an account-scoped TargetRoute names the recovery workflow more precisely than the kind fallback");
    }

    [Fact]
    public void BuildWorkItemRows_DuplicateIdsDifferingByCase_MergeToOneRow()
    {
        var readinessItems = new[]
        {
            CreateWorkItem("WI-DUPE", OperatorWorkItemToneDto.Critical, DateTimeOffset.Parse("2026-08-05T05:45:00Z"))
        };
        var inboxItems = new[]
        {
            CreateWorkItem("wi-dupe", OperatorWorkItemToneDto.Info, DateTimeOffset.Parse("2026-08-05T02:00:00Z"))
        };

        var rows = OperatorReadinessConsoleMapper.BuildWorkItemRows(inboxItems, readinessItems, IsRegistered);

        rows.Should().ContainSingle(
                "case-variant ids are the same work item under the server's case-insensitive dedup and must not burn two capped rows")
            .Which.ReadinessTone.Should().Be(WorkstationReadinessTone.Blocked);
    }

    [Fact]
    public async Task RefreshAsync_BreaksOutage_DemotesReadyHeadlineToReviewPending()
    {
        var readiness = CreateReadiness() with
        {
            OverallStatus = TradingAcceptanceGateStatusDto.Ready,
            AcceptanceGates =
            [
                new TradingAcceptanceGateDto("gate-replay", "Replay parity", TradingAcceptanceGateStatusDto.Ready, "Replay matches persisted state.")
            ]
        };
        var quietInbox = new OperatorInboxDto(
            AsOf: DateTimeOffset.Parse("2026-08-05T06:00:00Z"),
            Items: [],
            CriticalCount: 0,
            WarningCount: 0,
            ReviewCount: 0,
            Summary: "No operator work items need attention.");
        using var viewModel = CreateViewModel(
            new FakeReadinessProvider { Readiness = readiness },
            new FakeInboxClient { Inbox = quietInbox },
            new FakeReconciliationClient { Breaks = null },
            runWorkspaceService: null);

        await viewModel.RefreshAsync();

        viewModel.OverallStatusText.Should().Be(
            "Review pending", "the console must not headline Ready while the reconciliation queue state is unknown");
        viewModel.OverallTone.Should().Be(WorkstationReadinessTone.SignoffRequired);
        viewModel.HasBreaksError.Should().BeTrue();
    }

    [Fact]
    public void BuildTrustRows_UsesServerTrustGateAsToneAuthority()
    {
        var readiness = CreateReadiness();
        readiness = readiness with
        {
            AcceptanceGates =
            [
                new TradingAcceptanceGateDto(
                    "dk1-trust",
                    "Provider trust",
                    TradingAcceptanceGateStatusDto.Blocked,
                    "DK1 packet is not ready for operator review.")
            ]
        };

        var rows = OperatorReadinessConsoleMapper.BuildTrustRows(readiness);

        rows[0].ReadinessTone.Should().Be(
            WorkstationReadinessTone.Blocked,
            "the shared dk1-trust acceptance gate outranks the local blockers/sign-off fallback rule");
    }

    [Fact]
    public void BuildPromotionRows_UsesServerPromotionGateAsToneAuthority()
    {
        var readiness = CreateReadiness();
        readiness = readiness with
        {
            Promotion = readiness.Promotion! with { RequiresReview = false },
            AcceptanceGates =
            [
                new TradingAcceptanceGateDto(
                    "promotion",
                    "Promotion trace",
                    TradingAcceptanceGateStatusDto.Blocked,
                    "Promotion evidence is incomplete.")
            ]
        };

        var rows = OperatorReadinessConsoleMapper.BuildPromotionRows(readiness);

        rows[0].ReadinessTone.Should().Be(
            WorkstationReadinessTone.Blocked,
            "a promotion the server's gate marks blocked must not render green just because RequiresReview is false");
    }

    [Fact]
    public void BuildSessionRows_MissingReplay_EmitsVerifyRow()
    {
        var readiness = CreateReadiness() with { Replay = null };

        var rows = OperatorReadinessConsoleMapper.BuildSessionRows(readiness);

        rows.Should().HaveCount(3, "the replay row stays visible as an explicit review state when verification is missing");
        rows[2].Value.Should().Be("Verify");
        rows[2].ReadinessTone.Should().Be(WorkstationReadinessTone.SignoffRequired);
        rows[2].Detail.Should().Be("No replay verification is attached to the active readiness snapshot.");
    }

    private static OperatorReadinessConsoleViewModel CreateViewModel(
        ITradingOperatorReadinessProvider? readinessProvider,
        IWorkstationOperatorInboxApiClient? inboxClient,
        IWorkstationReconciliationApiClient? reconciliationClient,
        StrategyRunWorkspaceService? runWorkspaceService)
        => new(readinessProvider, inboxClient, reconciliationClient, runWorkspaceService, IsRegistered);

    private static TradingOperatorReadinessDto CreateReadiness()
        => new(
            AsOf: DateTimeOffset.Parse("2026-08-05T06:00:00Z"),
            ActiveSession: new TradingPaperSessionReadinessDto(
                SessionId: "paper-42",
                StrategyId: "strategy-1",
                StrategyName: "Covered Call Income",
                IsActive: true,
                InitialCash: 100_000m,
                CreatedAt: DateTimeOffset.Parse("2026-08-04T12:00:00Z"),
                ClosedAt: null,
                SymbolCount: 3,
                OrderCount: 12,
                PositionCount: 4,
                PortfolioValue: 101_250m),
            Sessions: [],
            Replay: new TradingReplayReadinessDto(
                SessionId: "paper-42",
                ReplaySource: "wal",
                IsConsistent: true,
                ComparedFillCount: 8,
                ComparedOrderCount: 12,
                ComparedLedgerEntryCount: 20,
                VerifiedAt: DateTimeOffset.Parse("2026-08-05T05:30:00Z"),
                LastPersistedFillAt: null,
                LastPersistedOrderUpdateAt: null,
                VerificationAuditId: "audit-77",
                MismatchReasons: [],
                DriftStatus: "None",
                RequiredNextAction: "None"),
            Controls: new TradingControlReadinessDto(
                CircuitBreakerOpen: false,
                CircuitBreakerReason: null,
                CircuitBreakerChangedBy: null,
                CircuitBreakerChangedAt: null,
                ManualOverrideCount: 0,
                SymbolLimitCount: 2,
                DefaultMaxPositionSize: 100m),
            Promotion: new TradingPromotionReadinessDto(
                State: "AwaitingReview",
                Reason: "Backtest completed with review-required promotion gate.",
                RequiresReview: true,
                SourceRunId: "run-9",
                TargetRunId: null,
                SuggestedNextMode: "Paper",
                AuditReference: null,
                ApprovalStatus: "Pending",
                ManualOverrideId: null,
                ApprovedBy: null),
            TrustGate: new TradingTrustGateReadinessDto(
                GateId: "dk1",
                Status: "Ready",
                ReadyForOperatorReview: true,
                OperatorSignoffRequired: true,
                OperatorSignoffStatus: "Signed",
                GeneratedAt: DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
                PacketPath: null,
                SourceSummary: null,
                RequiredSampleCount: 4,
                ReadySampleCount: 4,
                ValidatedEvidenceDocumentCount: 2,
                RequiredOwners: [],
                Blockers: [],
                Detail: "Trust gate evidence is complete."),
            BrokerageSync: new WorkstationBrokerageSyncStatusDto(
                FundAccountId: Guid.Parse("0e2a1c94-3f5b-4f6c-9a51-1c2d3e4f5a6b"),
                ProviderId: "alpaca",
                ExternalAccountId: "acct-1",
                Health: WorkstationBrokerageSyncHealth.Healthy,
                IsLinked: true,
                IsStale: false,
                LastAttemptedSyncAt: null,
                LastSuccessfulSyncAt: null,
                LastError: null,
                PositionCount: 4,
                OpenOrderCount: 1,
                FillCount: 8,
                CashTransactionCount: 2,
                SecurityMissingCount: 0,
                Warnings: []),
            WorkItems:
            [
                new OperatorWorkItemDto(
                    WorkItemId: "wi-readiness",
                    Kind: OperatorWorkItemKindDto.PromotionReview,
                    Label: "Review promotion",
                    Detail: "Promotion gate requires review.",
                    Tone: OperatorWorkItemToneDto.Warning,
                    CreatedAt: DateTimeOffset.Parse("2026-08-05T04:00:00Z"))
                {
                    PriorityScore = 40
                }
            ],
            Warnings: ["One readiness warning was retained."])
        {
            OverallStatus = TradingAcceptanceGateStatusDto.ReviewRequired,
            ReadyForPaperOperation = true,
            AcceptanceGates =
            [
                new TradingAcceptanceGateDto("gate-replay", "Replay parity", TradingAcceptanceGateStatusDto.Ready, "Replay matches persisted state."),
                new TradingAcceptanceGateDto("gate-signoff", "Operator sign-off", TradingAcceptanceGateStatusDto.Blocked, "Sign-off is missing.", RequiredNextAction: "Collect the operator sign-off.")
            ]
        };

    private static OperatorInboxDto CreateInbox()
        => new(
            AsOf: DateTimeOffset.Parse("2026-08-05T06:00:00Z"),
            Items:
            [
                new OperatorWorkItemDto(
                    WorkItemId: "wi-critical",
                    Kind: OperatorWorkItemKindDto.ReconciliationBreak,
                    Label: "Resolve reconciliation break",
                    Detail: "Cash variance breached tolerance.",
                    Tone: OperatorWorkItemToneDto.Critical,
                    CreatedAt: DateTimeOffset.Parse("2026-08-05T05:45:00Z"),
                    Workspace: "Accounting",
                    TargetPageTag: "FundReconciliation")
                {
                    PriorityScore = 90
                },
                new OperatorWorkItemDto(
                    WorkItemId: "wi-report",
                    Kind: OperatorWorkItemKindDto.ReportPackApproval,
                    Label: "Approve report pack",
                    Detail: "Report pack awaits approval.",
                    Tone: OperatorWorkItemToneDto.Warning,
                    CreatedAt: DateTimeOffset.Parse("2026-08-05T05:30:00Z"),
                    Workspace: "Reporting",
                    TargetPageTag: "NotARegisteredTag")
                {
                    PriorityScore = 60
                }
            ],
            CriticalCount: 1,
            WarningCount: 1,
            ReviewCount: 0,
            Summary: "Two operator work items need attention.");

    private static OperatorWorkItemDto CreateWorkItem(
        string id,
        OperatorWorkItemToneDto tone,
        DateTimeOffset createdAt,
        int priorityScore = 0,
        string? targetRoute = null)
        => new(
            WorkItemId: id,
            Kind: OperatorWorkItemKindDto.ReconciliationBreak,
            Label: $"Item {id}",
            Detail: "Detail",
            Tone: tone,
            CreatedAt: createdAt,
            TargetRoute: targetRoute)
        {
            PriorityScore = priorityScore
        };

    private static ReconciliationBreakQueueItem CreateBreak(
        string breakId,
        ReconciliationBreakQueueStatus status,
        DateTimeOffset detectedAt)
        => new(
            BreakId: breakId,
            RunId: "recon-run-x",
            StrategyName: "Covered Call Income",
            Category: ReconciliationBreakCategory.CashMismatch,
            Status: status,
            Variance: 1m,
            Reason: "Variance.",
            AssignedTo: null,
            DetectedAt: detectedAt,
            LastUpdatedAt: detectedAt);

    private static IReadOnlyList<ReconciliationBreakQueueItem> CreateBreaks()
        =>
        [
            new ReconciliationBreakQueueItem(
                BreakId: "brk-1",
                RunId: "recon-run-1",
                StrategyName: "Covered Call Income",
                Category: ReconciliationBreakCategory.CashMismatch,
                Status: ReconciliationBreakQueueStatus.Open,
                Variance: 125.50m,
                Reason: "Cash balance variance.",
                AssignedTo: null,
                DetectedAt: DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
                LastUpdatedAt: DateTimeOffset.Parse("2026-08-05T05:10:00Z")),
            new ReconciliationBreakQueueItem(
                BreakId: "brk-2",
                RunId: "recon-run-1",
                StrategyName: "Covered Call Income",
                Category: ReconciliationBreakCategory.AmountMismatch,
                Status: ReconciliationBreakQueueStatus.InReview,
                Variance: 3m,
                Reason: "Position quantity variance.",
                AssignedTo: "ops",
                DetectedAt: DateTimeOffset.Parse("2026-08-05T04:30:00Z"),
                LastUpdatedAt: DateTimeOffset.Parse("2026-08-05T04:40:00Z")),
            new ReconciliationBreakQueueItem(
                BreakId: "brk-3",
                RunId: "recon-run-0",
                StrategyName: "Covered Call Income",
                Category: ReconciliationBreakCategory.CashMismatch,
                Status: ReconciliationBreakQueueStatus.Resolved,
                Variance: 0m,
                Reason: "Resolved variance.",
                AssignedTo: "ops",
                DetectedAt: DateTimeOffset.Parse("2026-08-04T04:30:00Z"),
                LastUpdatedAt: DateTimeOffset.Parse("2026-08-04T05:40:00Z"))
        ];

    private sealed class FakeWorkflowActionCatalog : Meridian.Ui.Shared.Workflows.IWorkflowActionCatalog
    {
        private static readonly WorkflowActionDto ReadinessAction = new(
            ActionId: "trading.review-paper-candidate",
            Label: "Review Candidate for Paper",
            Detail: "Continue the Strategy to Trading handoff.",
            TargetPageTag: "TradingShell",
            Tone: "Primary",
            WorkItemKind: null,
            RoutePrefixes: [Meridian.Contracts.Api.UiApiRoutes.WorkstationTradingReadiness],
            RouteContains: [],
            Aliases: []);

        private static readonly WorkflowActionDto ReplayEvidenceAction = new(
            ActionId: "accounting.review-audit-trail",
            Label: "Review Audit Trail",
            Detail: "Inspect approvals, replay evidence, and trust-gate audit history.",
            TargetPageTag: "FundAuditTrail",
            Tone: "Primary",
            WorkItemKind: OperatorWorkItemKindDto.PaperReplay,
            RoutePrefixes: [],
            RouteContains: [],
            Aliases: []);

        private static readonly WorkflowActionDto BrokerageSyncAction = new(
            ActionId: "portfolio.review-brokerage-sync",
            Label: "Review Brokerage Sync",
            Detail: "Open account portfolio sync status and exception detail.",
            TargetPageTag: "AccountPortfolio",
            Tone: "Warning",
            WorkItemKind: OperatorWorkItemKindDto.BrokerageSync,
            RoutePrefixes: [],
            RouteContains: ["/brokerage-sync"],
            Aliases: []);

        public IReadOnlyList<WorkflowDefinitionDto> GetWorkflowDefinitions() => [];

        public IReadOnlyList<WorkflowActionDto> GetActions()
            => [ReadinessAction, ReplayEvidenceAction, BrokerageSyncAction];

        public WorkflowActionDto? ResolveAction(string? actionId)
            => actionId == ReadinessAction.ActionId ? ReadinessAction : null;

        public WorkflowActionDto? ResolveOperatorWorkItem(OperatorWorkItemDto? workItem)
        {
            if (workItem is null)
            {
                return null;
            }

            var routeMatch = ResolveRoute(workItem.TargetRoute);
            if (routeMatch is not null)
            {
                return routeMatch;
            }

            return workItem.Kind switch
            {
                OperatorWorkItemKindDto.PaperReplay => ReplayEvidenceAction,
                OperatorWorkItemKindDto.BrokerageSync => BrokerageSyncAction,
                _ => null
            };
        }

        public WorkflowActionDto? ResolveRoute(string? targetRoute)
            => targetRoute is not null
                && targetRoute.StartsWith(
                    Meridian.Contracts.Api.UiApiRoutes.WorkstationTradingReadiness,
                    StringComparison.OrdinalIgnoreCase)
                ? ReadinessAction
                : null;

        public string ResolveTargetPageTag(string? actionId, string fallbackPageTag)
            => ResolveAction(actionId)?.TargetPageTag ?? fallbackPageTag;
    }

    private sealed class FakeReadinessProvider : ITradingOperatorReadinessProvider
    {
        public TradingOperatorReadinessDto Readiness { get; set; } = CreateReadiness();

        public Task<TradingOperatorReadinessDto>? PendingReadiness { get; set; }

        public Exception? Exception { get; set; }

        public Task<TradingOperatorReadinessDto> GetAsync(Guid? fundAccountId = null, CancellationToken ct = default)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            return PendingReadiness ?? Task.FromResult(Readiness);
        }
    }

    private sealed class FakeInboxClient : IWorkstationOperatorInboxApiClient
    {
        public OperatorInboxDto? Inbox { get; set; }

        public Task<OperatorInboxDto?> GetInboxAsync(Guid? fundAccountId = null, CancellationToken ct = default)
            => Task.FromResult(Inbox);
    }

    private sealed class FakeReconciliationClient : IWorkstationReconciliationApiClient
    {
        public IReadOnlyList<ReconciliationBreakQueueItem>? Breaks { get; set; } = [];

        public Task<IReadOnlyList<ReconciliationBreakQueueItem>?> GetBreakQueueAsync(CancellationToken ct = default)
            => Task.FromResult(Breaks);

        public Task<ReconciliationCalibrationSummaryDto?> GetCalibrationSummaryAsync(CancellationToken ct = default)
            => Task.FromResult<ReconciliationCalibrationSummaryDto?>(null);

        public Task<IReadOnlyList<StatementRunSummaryDto>> GetStatementRunsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StatementRunSummaryDto>>([]);

        public Task<StatementRunSummaryDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
            => Task.FromResult<StatementRunSummaryDto?>(null);

        public Task<IReadOnlyList<StatementRunExceptionDto>> GetStatementExceptionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StatementRunExceptionDto>>([]);

        public Task<IReadOnlyList<StatementBreakDto>> GetOpenStatementBreaksAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StatementBreakDto>>([]);

        public Task<IReadOnlyList<ReconciliationCaseSummaryDto>> GetOpenReconciliationCasesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationCaseSummaryDto>>([]);

        public Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> GetReconciliationQueueStatusAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationQueueAccountStatusDto>>([]);

        public Task<ReconciliationRunDetail?> GetLatestRunDetailAsync(string runId, CancellationToken ct = default)
            => Task.FromResult<ReconciliationRunDetail?>(null);

        public Task<ReconciliationRunDetail?> GetRunDetailAsync(string reconciliationRunId, CancellationToken ct = default)
            => Task.FromResult<ReconciliationRunDetail?>(null);

        public Task<WorkstationReconciliationActionResult> ReviewBreakAsync(
            string breakId,
            ReviewReconciliationBreakRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("The readiness console never mutates break state.");

        public Task<WorkstationReconciliationActionResult> ResolveBreakAsync(
            string breakId,
            ResolveReconciliationBreakRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("The readiness console never mutates break state.");
    }
}
