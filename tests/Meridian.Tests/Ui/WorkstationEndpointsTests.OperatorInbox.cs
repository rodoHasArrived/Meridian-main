using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Monitoring;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using IReconciliationApiService = Meridian.Contracts.Reconciliation.IReconciliationApiService;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ReconciliationCaseSummaryDto = Meridian.Contracts.Reconciliation.ReconciliationCaseSummaryDto;
using ReconciliationQueueAccountStatusDto = Meridian.Contracts.Reconciliation.ReconciliationQueueAccountStatusDto;
using StatementImportSummaryDto = Meridian.Contracts.Reconciliation.StatementImportSummaryDto;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldKeepHighSeverityRoutesResolvable()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        }, currentUserPermissions: UserPermission.ViewTrades | UserPermission.ViewStrategies);

        var runId = $"run-inbox-route-resolution-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));
        var reconciliation = await app.Services
            .GetRequiredService<IReconciliationRunService>()
            .RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();
        await RetainScopedReconciliationBreaksAsync(app, runId, reconciliation!);

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);

        inbox.Should().NotBeNull();
        var routeCriticalItems = inbox!.Items
            .Where(item => item.Tone is OperatorWorkItemToneDto.Critical or OperatorWorkItemToneDto.Warning)
            .ToArray();

        routeCriticalItems.Should().NotBeEmpty();
        routeCriticalItems.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.Workspace) &&
            !string.IsNullOrWhiteSpace(item.TargetPageTag) &&
            !string.IsNullOrWhiteSpace(item.TargetRoute) &&
            item.TargetRoute.StartsWith("/", StringComparison.Ordinal));

        routeCriticalItems.Should().Contain(item =>
            item.Kind == OperatorWorkItemKindDto.PaperReplay &&
            item.TargetRoute == UiApiRoutes.WorkstationTradingReadiness &&
            item.TargetPageTag == "TradingShell");
        routeCriticalItems.Should().Contain(item =>
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
            item.TargetRoute == UiApiRoutes.ReconciliationBreakQueue &&
            item.TargetPageTag == "AccountingShell");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldReturnDeterministicOrderingAcrossPollingRequests()
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ViewTrades | UserPermission.ViewStrategies);
        var client = app.GetTestClient();

        var first = await client.GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);
        var second = await client.GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.Items.Select(item => item.WorkItemId)
            .Should()
            .Equal(second!.Items.Select(item => item.WorkItemId));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldMatchReadinessSeverityAcrossWave2Lanes()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
            services.AddSingleton<ReconciliationGovernanceService>();
        }, currentUserPermissions: UserPermission.ViewTrades);

        var runId = $"run-severity-parity-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));
        var reconciliation = await app.Services
            .GetRequiredService<IReconciliationRunService>()
            .RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();

        var client = app.GetTestClient();
        var readiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
            "/api/workstation/trading/readiness",
            ServerJsonOptions);
        var inbox = await client.GetFromJsonAsync<OperatorInboxDto>(
            "/api/workstation/operator/inbox",
            ServerJsonOptions);

        readiness.Should().NotBeNull();
        inbox.Should().NotBeNull();

        var readinessSeverityByCategory = NormalizeReadinessSeverity(readiness!.WorkItems);
        var inboxSeverityByCategory = NormalizeInboxSeverity(inbox!.Items);

        AssertCategoryParity("replay", readinessSeverityByCategory, inboxSeverityByCategory, "paper-session-missing");
        AssertCategoryParity("controls", readinessSeverityByCategory, inboxSeverityByCategory, "execution-evidence-incomplete");
        AssertCategoryParity("promotion-traceability", readinessSeverityByCategory, inboxSeverityByCategory, "promotion-decision-missing");
        AssertCategoryParity("reconciliation", readinessSeverityByCategory, inboxSeverityByCategory, "reconciliation-policy-");
        AssertCategoryParity("dk1-trust-gate", readinessSeverityByCategory, inboxSeverityByCategory, "dk1-trust-packet-unavailable");

        static Dictionary<string, int> NormalizeReadinessSeverity(IEnumerable<OperatorWorkItemDto> items)
            => items.GroupBy(static item => GetCategory(item.WorkItemId))
                .ToDictionary(static group => group.Key, static group => group.Max(item => ToSeverityRank(item.Tone)), StringComparer.OrdinalIgnoreCase);

        static Dictionary<string, int> NormalizeInboxSeverity(IEnumerable<OperatorWorkItemDto> items)
            => items.GroupBy(static item => GetCategory(item.WorkItemId))
                .ToDictionary(static group => group.Key, static group => group.Max(item => ToSeverityRank(item.Tone)), StringComparer.OrdinalIgnoreCase);

        static string GetCategory(string workItemId)
        {
            if (workItemId.StartsWith("paper-session-", StringComparison.OrdinalIgnoreCase) ||
                workItemId.StartsWith("paper-replay-", StringComparison.OrdinalIgnoreCase))
            {
                return "replay";
            }

            if (workItemId.StartsWith("execution-", StringComparison.OrdinalIgnoreCase))
            {
                return "controls";
            }

            if (workItemId.StartsWith("promotion-", StringComparison.OrdinalIgnoreCase))
            {
                return "promotion-traceability";
            }

            if (workItemId.StartsWith("reconciliation-policy-", StringComparison.OrdinalIgnoreCase) ||
                workItemId.StartsWith("reconciliation-break-", StringComparison.OrdinalIgnoreCase))
            {
                return "reconciliation";
            }

            if (workItemId.StartsWith("dk1-trust-", StringComparison.OrdinalIgnoreCase))
            {
                return "dk1-trust-gate";
            }

            return $"other:{workItemId}";
        }

        static int ToSeverityRank(OperatorWorkItemToneDto tone) =>
            tone switch
            {
                OperatorWorkItemToneDto.Critical => 2,
                OperatorWorkItemToneDto.Warning => 1,
                _ => 0
            };

        static void AssertCategoryParity(
            string category,
            IReadOnlyDictionary<string, int> readiness,
            IReadOnlyDictionary<string, int> inbox,
            string expectedAnchorId)
        {
            readiness.ContainsKey(category).Should().BeTrue(
                $"readiness lane should expose {category} via anchor '{expectedAnchorId}'");
            inbox.ContainsKey(category).Should().BeTrue(
                $"inbox lane should expose {category} via anchor '{expectedAnchorId}'");

            var readinessSeverity = readiness[category];
            var inboxSeverity = inbox[category];
            inboxSeverity.Should().Be(readinessSeverity,
                $"severity drift detected for {category} (anchor '{expectedAnchorId}')");
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldEmitOnlyLatestRunActionableReviewPacketBlockers()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies | UserPermission.ViewTrades);

        var olderRunId = $"run-inbox-review-packet-older-{Guid.NewGuid():N}";
        var newestRunId = $"run-inbox-review-packet-newest-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();

        await store.RecordRunAsync(BuildContinuityRun(olderRunId) with
        {
            StartedAt = new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero)
        });

        await store.RecordRunAsync(BuildReconciliationReadyRun(newestRunId) with
        {
            StartedAt = new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 3, 21, 18, 30, 0, TimeSpan.Zero)
        });

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>(
                "/api/workstation/operator/inbox",
                ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item =>
            item.WorkItemId == $"promotion-review-{newestRunId.ToLowerInvariant()}" &&
            item.Tone == OperatorWorkItemToneDto.Warning);
        inbox.Items.Should().NotContain(item =>
            item.WorkItemId == $"promotion-review-{olderRunId.ToLowerInvariant()}");

        var newestReviewPacket = await app
            .GetTestClient()
            .GetFromJsonAsync<StrategyRunReviewPacketDto>(
                $"/api/workstation/runs/{newestRunId}/review-packet",
                ServerJsonOptions);
        var olderReviewPacket = await app
            .GetTestClient()
            .GetFromJsonAsync<StrategyRunReviewPacketDto>(
                $"/api/workstation/runs/{olderRunId}/review-packet",
                ServerJsonOptions);

        newestReviewPacket.Should().NotBeNull();
        olderReviewPacket.Should().NotBeNull();
        newestReviewPacket!.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"promotion-review-{newestRunId.ToLowerInvariant()}" &&
            item.Tone == OperatorWorkItemToneDto.Warning);
        olderReviewPacket!.WorkItems.Should().ContainSingle(item =>
            item.WorkItemId == $"promotion-review-{olderRunId.ToLowerInvariant()}" &&
            item.Tone == OperatorWorkItemToneDto.Warning);
    }

    /// <summary>
    /// The inbox aggregates families whose own routes carry different permissions. A caller admitted
    /// by the route's own permission must still not receive a family they are gated out of, so this
    /// pins the run-review contribution to ViewStrategies: the same fixture that produces review-packet
    /// blockers for a strategy-permitted caller must produce none for a trading-only one, while the
    /// readiness items the route does grant stay present.
    /// </summary>
    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_WithoutStrategyPermission_ShouldOmitReviewPacketBlockers()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewTrades);

        var newestRunId = $"run-inbox-review-gate-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildContinuityRun(newestRunId) with
        {
            StartedAt = new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 3, 21, 18, 30, 0, TimeSpan.Zero)
        });

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>(
                "/api/workstation/operator/inbox",
                ServerJsonOptions);

        inbox.Should().NotBeNull();
        // The run-scoped work-item id is the discriminator, not the kind: trading readiness emits
        // PromotionReview items of its own, so asserting on kind alone would fail even with the
        // contribution correctly withheld.
        inbox!.Items.Should().NotContain(
            item => item.WorkItemId == $"promotion-review-{newestRunId.ToLowerInvariant()}",
            "run-review packets restate strategy-run detail and require ViewStrategies");
        inbox.Items.Should().NotContain(
            item => item.RunId == newestRunId && item.TargetRoute != null && item.TargetRoute.Contains("review-packet"),
            "no run-review contribution may reach a caller without the strategy permission");
        inbox.Items.Should().Contain(
            item => item.WorkItemId == "paper-session-missing",
            "the trading-readiness items the route's own permission grants must still be present");
    }

    /// <summary>
    /// The ledger and portfolio explorers project strategy-run detail that the run-ledger routes
    /// already serve under ViewStrategies, so a strategy reader must reach them or the drill-in links
    /// between the two break. The other two explorers have no such second door, so the same caller is
    /// refused those rather than admitted to all four by the widened route declaration.
    /// </summary>
    [Theory]
    [InlineData("ledger", true)]
    [InlineData("portfolio", true)]
    [InlineData("security-instrument", true)]
    [InlineData("report-line-provenance", false)]
    public async Task MapWorkstationEndpoints_FinancialRecordExplorer_AdmitsStrategyReadersToRunBackedExplorersOnly(
        string explorerId,
        bool runBacked)
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IFinancialRecordExplorerSavedViewStore>(_ =>
                new FileFinancialRecordExplorerSavedViewStore(
                    Path.Combine(Path.GetTempPath(), "meridian-tests", "explorer-views", Guid.NewGuid().ToString("N")),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<FileFinancialRecordExplorerSavedViewStore>.Instance));
            services.AddSingleton(sp => new FinancialRecordExplorerReadService(
                sp.GetRequiredService<IFinancialRecordExplorerSavedViewStore>(),
                sp.GetService<StrategyRunReadService>()));
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var response = await app
            .GetTestClient()
            .GetAsync($"/api/workstation/financial-record-explorers/{explorerId}");

        // Asserted on the authorization outcome rather than a success status: this fixture does not
        // register every service the explorers read, so an admitted request can still fail inside the
        // handler. What matters here is which side of the gate the caller lands on.
        if (runBacked)
        {
            response.StatusCode.Should().NotBe(
                HttpStatusCode.Forbidden,
                "the run-ledger routes already serve this data under ViewStrategies");
        }
        else
        {
            response.StatusCode.Should().Be(
                HttpStatusCode.Forbidden,
                "report-line provenance is built from the report-pack workflow, so it answers to the "
                + "reporting permissions rather than to a strategy permission");
        }
    }

    /// <summary>
    /// The inbox aggregates four families, so gating the route on one family's permission shuts the
    /// other three out of items they may read. Analysis, ReportingAnalyst and ReadOnly all hold
    /// ViewStrategies without ViewTrades; before the route admitted the union they received 403 and
    /// lost their run-review notifications entirely, in both workstation clients.
    /// </summary>
    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_WithStrategyPermissionOnly_ShouldContributeReviewPacketsWithoutTradingReadiness()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var newestRunId = $"run-inbox-strategy-only-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildContinuityRun(newestRunId) with
        {
            StartedAt = new DateTimeOffset(2026, 3, 21, 20, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 3, 21, 20, 30, 0, TimeSpan.Zero)
        });

        var response = await app.GetTestClient().GetAsync("/api/workstation/operator/inbox");

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "a caller holding a permission for one of the inbox's families must reach the route");

        var inbox = await response.Content.ReadFromJsonAsync<OperatorInboxDto>(ServerJsonOptions);
        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(
            item => item.WorkItemId == $"promotion-review-{newestRunId.ToLowerInvariant()}",
            "run-review packets are exactly what the strategy permission grants");
        // Trading readiness is a contribution like any other now, not a floor the route guarantees.
        inbox.Items.Should().NotContain(
            item => item.WorkItemId == "paper-session-missing",
            "trading readiness requires ViewTrades, which this caller does not hold");
        // The work-item id is the discriminator, not the kind: run-review continuity items carry the
        // ReconciliationBreak kind and point at the break-queue route while originating from the
        // strategy contribution this caller is entitled to. Only the canonical
        // "reconciliation-break-" ids come from the break-queue repository.
        inbox.Items.Should().NotContain(
            item => item.WorkItemId.StartsWith("reconciliation-break-", StringComparison.OrdinalIgnoreCase),
            "ViewStrategies is in none of the reconciliation read permissions");
    }

    /// <summary>
    /// The workflow summary composes one card per canonical workspace, so its route admits the union
    /// of the per-workspace read permissions -- otherwise a reporting or strategy reader loses the
    /// whole shell strip. Admission is not authorization: the strategy card carries candidate names,
    /// promotion state and promotion reasons, and FundAccountant reaches the route through ViewTrades
    /// while holding no strategy permission at all.
    /// </summary>
    [Fact]
    public async Task MapWorkstationEndpoints_WorkflowSummary_WithoutStrategyPermission_ShouldOmitTheStrategyWorkspace()
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserPermissions: RolePermissions.For(UserRole.FundAccountant),
            currentUserRole: UserRole.FundAccountant);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildContinuityRun($"run-workflow-scope-{Guid.NewGuid():N}"));

        var response = await app.GetTestClient().GetAsync("/api/workstation/workflow-summary?hasOperatingContext=true");

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "FundAccountant holds ViewTrades, which this route declares");

        var summary = await response.Content.ReadFromJsonAsync<OperatorWorkflowHomeSummary>(ServerJsonOptions);
        summary.Should().NotBeNull();

        var workspaceIds = summary!.Workspaces.Select(static workspace => workspace.WorkspaceId).ToArray();
        workspaceIds.Should().NotContain(
            "strategy",
            "the strategy card is built from the backtest queue, which ViewStrategies governs");
        workspaceIds.Should().NotContain(
            "data",
            "the data card reports provider connectivity and backfill failures, which the provider-metrics permissions govern");
        workspaceIds.Should().Contain(
            "trading",
            "ViewTrades is what admitted this caller and is exactly the trading card's own permission");
        workspaceIds.Should().Contain(
            "accounting",
            "FundAccountant holds the direct-lending permissions the accounting card is drawn from");
        // Portfolio, Reporting and Settings vary only on whether a context is selected -- they carry
        // no record content, so there is nothing in them to withhold from an admitted caller.
        workspaceIds.Should().Contain(new[] { "portfolio", "reporting", "settings" });
    }

    [Fact]
    public async Task MapWorkstationEndpoints_Session_WithoutRunService_ShouldUseAuthenticatedRole()
    {
        await using var app = await CreateAppAsync(
            currentUserPermissions: RolePermissions.For(UserRole.Controller),
            currentUserRole: UserRole.Controller);

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.WorkstationSession);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await response.Content.ReadFromJsonAsync<WorkstationSessionPayload>(ServerJsonOptions);
        session.Should().NotBeNull();
        session!.Role.Should().Be(
            nameof(UserRole.Controller),
            "service availability must not replace authenticated identity with a strategy-derived label");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldIncludeReviewPacketBlockersFromRecentRuns()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewTrades | UserPermission.ViewStrategies);

        var olderRunId = $"run-inbox-review-packet-older-{Guid.NewGuid():N}";
        var newestRunId = $"run-inbox-review-packet-newest-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();

        await store.RecordRunAsync(BuildReconciliationReadyRun(olderRunId) with
        {
            StartedAt = new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero)
        });

        await store.RecordRunAsync(BuildContinuityRun(newestRunId) with
        {
            StartedAt = new DateTimeOffset(2026, 3, 21, 18, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 3, 21, 18, 30, 0, TimeSpan.Zero)
        });

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>(
                "/api/workstation/operator/inbox",
                ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item => item.WorkItemId == "paper-session-missing");

        var newestReviewItem = inbox.Items.Should().ContainSingle(item =>
            item.WorkItemId == $"promotion-review-{newestRunId.ToLowerInvariant()}" &&
            item.Kind == OperatorWorkItemKindDto.PromotionReview).Which;
        var olderReviewItem = inbox.Items.Should().ContainSingle(item =>
            item.WorkItemId == $"promotion-review-{olderRunId.ToLowerInvariant()}" &&
            item.Kind == OperatorWorkItemKindDto.PromotionReview).Which;

        newestReviewItem.Tone.Should().Be(OperatorWorkItemToneDto.Warning);
        newestReviewItem.Workspace.Should().Be("Trading");
        newestReviewItem.RunId.Should().Be(newestRunId);
        newestReviewItem.TargetRoute.Should().Be(UiApiRoutes.WithParam(UiApiRoutes.RunsReviewPacket, "runId", newestRunId));
        newestReviewItem.TargetPageTag.Should().Be("TradingShell");
        olderReviewItem.Tone.Should().Be(OperatorWorkItemToneDto.Warning);
        olderReviewItem.Workspace.Should().Be("Trading");
        olderReviewItem.RunId.Should().Be(olderRunId);
        olderReviewItem.TargetRoute.Should().Be(UiApiRoutes.WithParam(UiApiRoutes.RunsReviewPacket, "runId", olderRunId));
        olderReviewItem.TargetPageTag.Should().Be("TradingShell");
        inbox.Items.Should().OnlyContain(item =>
            !item.WorkItemId.StartsWith("promotion-review-", StringComparison.OrdinalIgnoreCase) ||
            item.Tone == OperatorWorkItemToneDto.Warning ||
            item.Tone == OperatorWorkItemToneDto.Critical);
        inbox.WarningCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_WithFundAccountId_ShouldProjectBrokerageSyncWorkItem()
    {
        var fundAccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-inbox", Guid.NewGuid().ToString("N"));
        try
        {
            var brokerageSync = CreateFailedBrokerageSyncService(root);
            var status = await brokerageSync.RunSyncAsync(
                fundAccountId,
                new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-404", "ops-review"));
            status.Health.Should().Be(WorkstationBrokerageSyncHealth.Failed);

            await using var app = await CreateAppAsync(services =>
            {
                services.AddSingleton(brokerageSync);
            }, currentUserPermissions: UserPermission.ViewTrades);

            var inbox = await app
                .GetTestClient()
                .GetFromJsonAsync<OperatorInboxDto>(
                    $"/api/workstation/operator/inbox?fundAccountId={fundAccountId:D}",
                    ServerJsonOptions);

            inbox.Should().NotBeNull();
            var syncItem = inbox!.Items.Should()
                .ContainSingle(item =>
                    item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                    item.FundAccountId == fundAccountId)
                .Which;
            syncItem.Tone.Should().Be(OperatorWorkItemToneDto.Critical);
            syncItem.Workspace.Should().Be("Settings");
            syncItem.TargetRoute.Should().Be("/settings#alpaca-provider-setup");
            syncItem.TargetPageTag.Should().Be("ProviderConnectionCenter");
            syncItem.Detail.Should().Contain("Alpaca credentials are missing.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_WithFundAccountId_ShouldProjectDegradedBrokerageSyncForIbkrAndRobinhoodAsWarning()
    {
        var ibkrAccountId = Guid.Parse("c7774ca1-08f7-4f89-a4c3-89387fb7cd31");
        var robinhoodAccountId = Guid.Parse("ec705f26-f620-4c71-b13a-3e8cce9018f9");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-inbox-degraded", Guid.NewGuid().ToString("N"));
        try
        {
            var brokerageSync = CreateFailedBrokerageSyncService(root);
            await brokerageSync.RunSyncAsync(ibkrAccountId, new WorkstationBrokerageSyncRunRequestDto("ibkr", "DU-7788", "ops-review"));
            await brokerageSync.RunSyncAsync(robinhoodAccountId, new WorkstationBrokerageSyncRunRequestDto("robinhood", "RH-404", "ops-review"));

            await using var app = await CreateAppAsync(services => services.AddSingleton(brokerageSync), currentUserPermissions: UserPermission.ViewTrades);
            var client = app.GetTestClient();

            foreach (var accountId in new[] { ibkrAccountId, robinhoodAccountId })
            {
                var inbox = await client.GetFromJsonAsync<OperatorInboxDto>($"/api/workstation/operator/inbox?fundAccountId={accountId:D}", ServerJsonOptions);
                inbox.Should().NotBeNull();
                var syncItem = inbox!.Items.Should().ContainSingle(item =>
                    item.Kind == OperatorWorkItemKindDto.BrokerageSync && item.FundAccountId == accountId).Which;

                syncItem.Tone.Should().Be(OperatorWorkItemToneDto.Critical);
                syncItem.TargetPageTag.Should().Be("ProviderConnectionCenter");
                syncItem.TargetRoute.Should().Contain("-provider-setup");
                syncItem.Detail.Should().ContainEquivalentOf("credentials are missing");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FundAccountScope_ShouldReturnOnlyRequestedAccountWorkItemsAndPreserveRoutingMetadata()
    {
        var accountA = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var accountB = Guid.Parse("84fb987e-5323-4630-9a0c-d1c30c95fa47");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-scope", Guid.NewGuid().ToString("N"));
        try
        {
            var brokerageSync = CreateFailedBrokerageSyncService(root);
            await brokerageSync.RunSyncAsync(accountA, new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-404", "ops-review"));
            await brokerageSync.RunSyncAsync(accountB, new WorkstationBrokerageSyncRunRequestDto("ibkr", "DU-7788", "ops-review"));

            await using var app = await CreateAppAsync(services => services.AddSingleton(brokerageSync), currentUserPermissions: UserPermission.ViewTrades);
            var client = app.GetTestClient();

            var readiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
                $"/api/workstation/trading/readiness?fundAccountId={accountA:D}",
                ServerJsonOptions);
            var inbox = await client.GetFromJsonAsync<OperatorInboxDto>(
                $"/api/workstation/operator/inbox?fundAccountId={accountA:D}",
                ServerJsonOptions);

            readiness.Should().NotBeNull();
            readiness!.BrokerageSync.Should().NotBeNull();
            readiness.BrokerageSync!.FundAccountId.Should().Be(accountA);
            readiness.WorkItems.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId == accountA &&
                item.Workspace == "Settings" &&
                item.TargetRoute == "/settings#alpaca-provider-setup" &&
                item.TargetPageTag == "ProviderConnectionCenter");
            readiness.WorkItems.Should().NotContain(item => item.FundAccountId == accountB);

            inbox.Should().NotBeNull();
            inbox!.Items.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId == accountA &&
                item.Workspace == "Settings" &&
                item.TargetRoute == "/settings#alpaca-provider-setup" &&
                item.TargetPageTag == "ProviderConnectionCenter");
            inbox.Items.Should().NotContain(item => item.FundAccountId == accountB);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FundAccountScope_WithUnknownAccount_ShouldNotFallBackToOtherScopedAccounts()
    {
        var knownAccount = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var unknownAccount = Guid.Parse("d8a69792-3313-4067-a311-a4de2133fe81");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-scope-unknown", Guid.NewGuid().ToString("N"));
        try
        {
            var brokerageSync = CreateFailedBrokerageSyncService(root);
            await brokerageSync.RunSyncAsync(knownAccount, new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-404", "ops-review"));

            await using var app = await CreateAppAsync(services => services.AddSingleton(brokerageSync), currentUserPermissions: UserPermission.ViewTrades);
            var client = app.GetTestClient();

            var readiness = await client.GetFromJsonAsync<TradingOperatorReadinessDto>(
                $"/api/workstation/trading/readiness?fundAccountId={unknownAccount:D}",
                ServerJsonOptions);
            var inbox = await client.GetFromJsonAsync<OperatorInboxDto>(
                $"/api/workstation/operator/inbox?fundAccountId={unknownAccount:D}",
                ServerJsonOptions);

            readiness.Should().NotBeNull();
            readiness!.BrokerageSync.Should().NotBeNull("unknown scoped account should report an unlinked brokerage sync posture instead of inheriting another account's sync");
            readiness.BrokerageSync!.Health.ToString().Should().Be("Unlinked");
            readiness.BrokerageSync.ProviderId.Should().BeNull("unknown scoped account should not inherit another account's brokerage provider linkage");
            readiness.WorkItems.Should().NotContain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId == knownAccount);

            inbox.Should().NotBeNull();
            inbox!.Items.Should().NotContain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId == knownAccount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_FundAccountScope_OperatorInboxScopedQueries_ShouldReturnPerAccountBrokerageSyncItemsWithoutCrossAccountLeakage()
    {
        var accountA = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var accountB = Guid.Parse("84fb987e-5323-4630-9a0c-d1c30c95fa47");
        var accountWithoutPendingItems = Guid.Parse("04ef2646-cad4-4775-8235-d63ef5a25d7f");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-scope-global", Guid.NewGuid().ToString("N"));
        try
        {
            var brokerageSync = CreateFailedBrokerageSyncService(root);
            await brokerageSync.RunSyncAsync(accountA, new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-404", "ops-review"));
            await brokerageSync.RunSyncAsync(accountB, new WorkstationBrokerageSyncRunRequestDto("ibkr", "DU-7788", "ops-review"));

            await using var app = await CreateAppAsync(services => services.AddSingleton(brokerageSync), currentUserPermissions: UserPermission.ViewTrades);
            var client = app.GetTestClient();

            var accountAInbox = await client.GetFromJsonAsync<OperatorInboxDto>(
                $"/api/workstation/operator/inbox?fundAccountId={accountA:D}",
                ServerJsonOptions);
            var accountBInbox = await client.GetFromJsonAsync<OperatorInboxDto>(
                $"/api/workstation/operator/inbox?fundAccountId={accountB:D}",
                ServerJsonOptions);
            var emptyScopedInbox = await client.GetFromJsonAsync<OperatorInboxDto>(
                $"/api/workstation/operator/inbox?fundAccountId={accountWithoutPendingItems:D}",
                ServerJsonOptions);

            accountAInbox.Should().NotBeNull();
            accountAInbox!.Items.Should().Contain(item => item.Kind == OperatorWorkItemKindDto.BrokerageSync && item.FundAccountId == accountA);

            accountBInbox.Should().NotBeNull();
            accountBInbox!.Items.Should().Contain(item => item.Kind == OperatorWorkItemKindDto.BrokerageSync && item.FundAccountId == accountB);

            emptyScopedInbox.Should().NotBeNull();
            emptyScopedInbox!.Items.Should().Contain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId == accountWithoutPendingItems &&
                item.Workspace == "Settings" &&
                item.TargetRoute == "/settings#provider-connection-center" &&
                item.TargetPageTag == "ProviderConnectionCenter");
            emptyScopedInbox.Items.Should().NotContain(item =>
                item.Kind == OperatorWorkItemKindDto.BrokerageSync &&
                item.FundAccountId != accountWithoutPendingItems);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldIncludeOpenReconciliationBreaks()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        }, currentUserPermissions: UserPermission.ViewTrades | UserPermission.ModifySecurityMaster);

        var runId = $"run-inbox-break-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));
        var client = app.GetTestClient();
        var createResponse = await client.PostAsJsonAsync(UiApiRoutes.ReconciliationRuns, new ReconciliationRunRequest(runId));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reconciliation = await createResponse.Content.ReadFromJsonAsync<ReconciliationRunDetail>(ServerJsonOptions);
        reconciliation.Should().NotBeNull();
        await RetainScopedReconciliationBreaksAsync(app, runId, reconciliation!);

        var inbox = await client
            .GetFromJsonAsync<OperatorInboxDto>(
                "/api/workstation/operator/inbox",
                ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item =>
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
            item.RunId == runId &&
            item.Workspace == "Accounting");
        var breakItem = inbox.Items.First(item =>
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
            item.RunId == runId &&
            item.Workspace == "Accounting");
        breakItem.WorkItemId.Should().StartWith("reconciliation-break-");
        breakItem.TargetRoute.Should().Be(UiApiRoutes.ReconciliationBreakQueue);
        breakItem.TargetPageTag.Should().Be("AccountingShell");
        breakItem.Detail.Should().Contain("The break is open");
        breakItem.Detail.Should().Contain("Exception route:");
        breakItem.Detail.Should().Contain("sign-off");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ShouldIncludeInReviewReconciliationBreaks()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        }, currentUserPermissions: UserPermission.ViewTrades | UserPermission.ModifySecurityMaster);

        var runId = $"run-inbox-review-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));
        var reconciliation = await app.Services
            .GetRequiredService<IReconciliationRunService>()
            .RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();
        await RetainScopedReconciliationBreaksAsync(app, runId, reconciliation!);

        var breakId = $"{runId}:{reconciliation!.Breaks[0].CheckId}";
        var client = app.GetTestClient();
        var review = await client.PostAsJsonAsync(
            $"/api/workstation/reconciliation/break-queue/{breakId}/review",
            new ReviewReconciliationBreakRequest(
                BreakId: breakId,
                AssignedTo: "ops-review",
                ReviewedBy: "qa-review",
                ReviewNote: "Investigating the mismatch."));
        review.StatusCode.Should().Be(HttpStatusCode.OK);

        var inbox = await client.GetFromJsonAsync<OperatorInboxDto>(
            "/api/workstation/operator/inbox",
            ServerJsonOptions);

        inbox.Should().NotBeNull();
        var breakItem = inbox!.Items.First(item =>
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
            item.AuditReference == breakId);
        breakItem.Label.Should().Be("Reconciliation break in review");
        breakItem.TargetRoute.Should().Be(UiApiRoutes.ReconciliationBreakQueue);
        breakItem.TargetPageTag.Should().Be("AccountingShell");
        breakItem.Detail.Should().Contain("The break is in review");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ReadinessOnlyItems_ShouldExposeExpectedClassificationAndMetadata()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationBreakQueueRepository>(
                new FileReconciliationBreakQueueRepository(
                    Path.Combine(Path.GetTempPath(), "meridian-tests", "break-queue", Guid.NewGuid().ToString("N")),
                    NullLogger<FileReconciliationBreakQueueRepository>.Instance));
        }, currentUserPermissions: UserPermission.ViewTrades);

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>(
                "/api/workstation/operator/inbox",
                ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().NotBeEmpty();
        inbox.Items.Should().Contain(item => string.Equals(item.WorkItemId, "reconciliation-policy", StringComparison.OrdinalIgnoreCase));
        inbox.Items.Should().NotContain(item => item.WorkItemId.StartsWith("reconciliation-break-", StringComparison.OrdinalIgnoreCase));
        inbox.Items.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.Title) &&
            !string.IsNullOrWhiteSpace(item.Detail) &&
            !string.IsNullOrWhiteSpace(item.TargetRoute));
        inbox.Items.Should().Contain(item => item.WorkItemId == "paper-session-missing");
        inbox.CriticalCount.Should().Be(inbox.Items.Count(item => item.Tone == OperatorWorkItemToneDto.Critical));
        inbox.WarningCount.Should().Be(inbox.Items.Count(item => item.Tone == OperatorWorkItemToneDto.Warning));
        inbox.ReviewCount.Should().Be(inbox.CriticalCount + inbox.WarningCount);
        inbox.Items.Should().OnlyContain(item => item.PriorityScore > 0 && !string.IsNullOrWhiteSpace(item.PriorityExplanation));
        inbox.Items.Should().BeInDescendingOrder(item => item.PriorityScore);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_ReconciliationBreaks_ShouldExposeExpectedClassificationAndMetadata()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        }, currentUserPermissions: UserPermission.ViewTrades);

        var runId = $"run-inbox-break-only-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));
        var reconciliation = await app.Services
            .GetRequiredService<IReconciliationRunService>()
            .RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();
        await RetainScopedReconciliationBreaksAsync(app, runId, reconciliation!);

        var inbox = await app.GetTestClient().GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().NotBeEmpty();
        inbox.Items.Should().Contain(item => item.Kind == OperatorWorkItemKindDto.ReconciliationBreak);
        inbox.Items.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.Title) &&
            !string.IsNullOrWhiteSpace(item.Detail) &&
            !string.IsNullOrWhiteSpace(item.TargetRoute));
        inbox.Items.Where(item => item.Kind == OperatorWorkItemKindDto.ReconciliationBreak)
            .Should().OnlyContain(item => item.TargetRoute == UiApiRoutes.ReconciliationBreakQueue);
        inbox.CriticalCount.Should().Be(inbox.Items.Count(item => item.Tone == OperatorWorkItemToneDto.Critical));
        inbox.WarningCount.Should().Be(inbox.Items.Count(item => item.Tone == OperatorWorkItemToneDto.Warning));
        inbox.ReviewCount.Should().Be(inbox.CriticalCount + inbox.WarningCount);
        inbox.Items.Should().BeInDescendingOrder(item => item.PriorityScore);
        inbox.Items.Should().OnlyContain(item => item.PriorityScore > 0 && !string.IsNullOrWhiteSpace(item.PriorityExplanation));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_MixedQueue_ShouldRetainBothCategoriesWithoutDropsAndOrderByPriority()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
            services.AddSingleton<ReconciliationProjectionService>();
            services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        }, currentUserPermissions: UserPermission.ViewTrades | UserPermission.ViewStrategies);

        var runId = $"run-inbox-mixed-{Guid.NewGuid():N}";
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildReconciliationMismatchRun(runId));
        var reconciliation = await app.Services
            .GetRequiredService<IReconciliationRunService>()
            .RunAsync(new ReconciliationRunRequest(runId));
        reconciliation.Should().NotBeNull();
        await RetainScopedReconciliationBreaksAsync(app, runId, reconciliation!);

        var inbox = await app.GetTestClient().GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item => item.WorkItemId == "paper-session-missing");
        inbox.Items.Should().Contain(item => item.Kind == OperatorWorkItemKindDto.ReconciliationBreak);
        inbox.Items.Should().BeInDescendingOrder(item => item.PriorityScore);
        inbox.Items.Should().OnlyContain(item => item.PriorityScore > 0 && !string.IsNullOrWhiteSpace(item.PriorityExplanation));
        inbox.Items.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.Title) &&
            !string.IsNullOrWhiteSpace(item.Detail) &&
            !string.IsNullOrWhiteSpace(item.TargetRoute));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_WhenNoReconciliationBreaks_ShouldNotEmitFalseBreakItems()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationBreakQueueRepository>(
                new FileReconciliationBreakQueueRepository(
                    Path.Combine(Path.GetTempPath(), "meridian-tests", "break-queue", Guid.NewGuid().ToString("N")),
                    NullLogger<FileReconciliationBreakQueueRepository>.Instance));
        }, currentUserPermissions: UserPermission.ViewTrades);

        var inbox = await app.GetTestClient().GetFromJsonAsync<OperatorInboxDto>("/api/workstation/operator/inbox", ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item => string.Equals(item.WorkItemId, "reconciliation-policy", StringComparison.OrdinalIgnoreCase));
        inbox.Items.Should().NotContain(item => item.WorkItemId.StartsWith("reconciliation-break-", StringComparison.OrdinalIgnoreCase));
        inbox.CriticalCount.Should().Be(inbox.Items.Count(item => item.Tone == OperatorWorkItemToneDto.Critical));
        inbox.WarningCount.Should().Be(inbox.Items.Count(item => item.Tone == OperatorWorkItemToneDto.Warning));
        inbox.ReviewCount.Should().Be(inbox.CriticalCount + inbox.WarningCount);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperatorInbox_WhenBreakQueueUnavailable_ShouldReturnTradingReadinessWithWarning()
    {
        // Two families in one assertion set: trading readiness needs ViewTrades, and the break-queue
        // unavailability warning is only contributed to a caller the break-queue reads admit -- which
        // ViewTrades alone is not, since it cannot act on reconciliation casework.
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationBreakQueueRepository>(
                new ThrowingReconciliationBreakQueueRepository());
        }, currentUserPermissions: UserPermission.ViewTrades | UserPermission.ViewDirectLending);

        var inbox = await app
            .GetTestClient()
            .GetFromJsonAsync<OperatorInboxDto>(
                "/api/workstation/operator/inbox",
                ServerJsonOptions);

        inbox.Should().NotBeNull();
        inbox!.Items.Should().Contain(item =>
            item.WorkItemId == "paper-session-missing" &&
            item.TargetPageTag == "TradingShell");
        inbox.Items.Should().ContainSingle(item =>
            item.WorkItemId == "reconciliation-break-queue-unavailable" &&
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak &&
            item.Tone == OperatorWorkItemToneDto.Warning &&
            item.Workspace == "Accounting" &&
            item.TargetRoute == UiApiRoutes.ReconciliationBreakQueue &&
            item.TargetPageTag == "AccountingShell");
        inbox.WarningCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Dk1TrustGateReadinessService_WithLegacySignedOperatorPacket_ShouldNormalizeOwnerEvidence()
    {
        var automationRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "dk1-signed-packet",
            Guid.NewGuid().ToString("N"));
        WriteReadyDk1Packet(
            automationRoot,
            """
            {
              "requiredOwners": [ "Data Operations", "Provider Reliability", "Trading" ],
              "status": "signed",
              "requiredBeforeDk1Exit": true,
              "signedOwners": [ "Data Operations", "Provider Reliability", "Trading" ],
              "missingOwners": [],
              "completedAtUtc": "2026-04-26T16:02:00Z",
              "sourcePath": "artifacts/provider-validation/_automation/unit-ready/dk1-operator-signoff.json",
              "approvals": [
                {
                  "owner": "Data Operations",
                  "signedBy": "data.ops",
                  "signedAtUtc": "2026-04-26T15:58:00Z",
                  "decision": "approved",
                  "rationale": "Provider packet reviewed."
                },
                {
                  "owner": "Provider Reliability",
                  "signedBy": "provider.reliability",
                  "signedAtUtc": "2026-04-26T16:00:00Z",
                  "decision": "approved",
                  "rationale": "Threshold and evidence checks accepted."
                },
                {
                  "owner": "Trading",
                  "signedBy": "trading.owner",
                  "signedAtUtc": "2026-04-26T16:02:00Z",
                  "decision": "approved",
                  "rationale": "Cockpit readiness gate accepted."
                }
              ]
            }
            """);

        var service = new Dk1TrustGateReadinessService(
            new Dk1TrustGateReadinessOptions(automationRoot),
            NullLogger<Dk1TrustGateReadinessService>.Instance);

        var readiness = await service.GetCurrentAsync();

        readiness.OperatorSignoffStatus.Should().Be("signed");
        readiness.RequiredOwners.Should().BeEquivalentTo(
            ["Data", "Provider Reliability", "Trading"]);
        readiness.OperatorSignoff.Should().NotBeNull();
        readiness.OperatorSignoff!.RequiredOwners.Should().BeEquivalentTo(
            ["Data", "Provider Reliability", "Trading"]);
        readiness.OperatorSignoff!.SignedOwners.Should().BeEquivalentTo(
            ["Data", "Provider Reliability", "Trading"]);
        readiness.OperatorSignoff.MissingOwners.Should().BeEmpty();
        readiness.OperatorSignoff.CompletedAt.Should().Be(new DateTimeOffset(2026, 4, 26, 16, 2, 0, TimeSpan.Zero));
        readiness.Detail.Should().Contain("operator sign-off is complete");
        readiness.Detail.Should().NotContain("Data Operations");
        readiness.Detail.Should().Contain("explainability validated").And.Contain("calibration validated");
        readiness.Blockers.Should().BeEmpty();
        readiness.TrustRationaleContract.Should().NotBeNull();
        readiness.TrustRationaleContract!.Status.Should().Be("validated");
        readiness.CalibrationVersion.Should().BeNull();
        readiness.CalibrationValidatedAt.Should().BeNull();
        readiness.PromotionPosture.Should().BeNull();
        readiness.PacketPath.Should().NotBeNull();
        Path.IsPathRooted(readiness.PacketPath!).Should().BeFalse();
        readiness.PacketPath.Should().Contain("dk1-pilot-parity-packet.json");
    }
}
