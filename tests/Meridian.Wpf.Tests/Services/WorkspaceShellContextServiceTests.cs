using System.IO;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class WorkspaceShellContextServiceTests
{
    [Fact]
    public async Task CreateAsync_WhenFundSelected_ComposesFundAndEnvironmentBadges()
    {
        var detector = FixtureModeDetector.Instance;
        detector.SetFixtureMode(false);
        detector.UpdateBackendReachability(true);
        NotificationService.Instance.ClearHistory();

        var statusService = Substitute.For<IStatusService>();
        statusService.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new StatusResponse { IsConnected = true });

        var fundContext = await CreateFundContextAsync();
        var service = new WorkspaceShellContextService(
            fundContext,
            detector,
            NotificationService.Instance,
            statusService);

        var context = await service.CreateAsync(new WorkspaceShellContextInput
        {
            WorkspaceTitle = "Governance",
            WorkspaceSubtitle = "Shell",
            PrimaryScopeLabel = "Fund",
            PrimaryScopeValue = string.Empty,
            AsOfValue = "Apr 07 2026 09:30",
            FreshnessValue = "Backend connected",
            ReviewStateValue = "Unlocked",
            ReviewStateTone = WorkspaceTone.Success,
            CriticalValue = "Queue stable",
            CriticalTone = WorkspaceTone.Info
        });

        context.WorkspaceTitle.Should().Be("Governance");
        context.Badges.Should().ContainSingle(b => b.Label == "Fund" && b.Value.Contains("Alpha Credit"));
        context.Badges.Should().ContainSingle(b => b.Label == "Environment" && b.Value == "Live" && b.Tone == WorkspaceTone.Success);
        context.Badges.Should().ContainSingle(b => b.Label == "Alerts" && b.Value == "No recent alerts");
    }

    [Fact]
    public async Task CreateAsync_WhenFixtureModeAndUnreadAlerts_SurfaceWarningBadges()
    {
        var detector = FixtureModeDetector.Instance;
        detector.SetFixtureMode(true);
        detector.UpdateBackendReachability(true);
        NotificationService.Instance.ClearHistory();
        NotificationService.Instance.ShowNotification("Backfill warning", "Provider queue is stale", Meridian.Ui.Services.NotificationType.Warning);

        var statusService = Substitute.For<IStatusService>();
        statusService.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new StatusResponse { IsConnected = true });

        var fundContext = await CreateFundContextAsync();
        var service = new WorkspaceShellContextService(
            fundContext,
            detector,
            NotificationService.Instance,
            statusService);

        var context = await service.CreateAsync(new WorkspaceShellContextInput
        {
            WorkspaceTitle = "Data Operations",
            WorkspaceSubtitle = "Shell",
            PrimaryScopeLabel = "Queue",
            PrimaryScopeValue = "Provider posture",
            AsOfValue = "Apr 07 2026 09:30",
            FreshnessValue = "stale provider feed",
            ReviewStateValue = "Queue staged",
            ReviewStateTone = WorkspaceTone.Warning
        });

        context.Badges.Should().ContainSingle(b => b.Label == "Environment" && b.Value == "Fixture" && b.Tone == WorkspaceTone.Warning);
        context.Badges.Should().ContainSingle(b => b.Label == "Freshness" && b.Tone == WorkspaceTone.Warning);
        context.Badges.Should().ContainSingle(b => b.Label == "Alerts" && b.Value.Contains("1 unread") && b.Tone == WorkspaceTone.Warning);

        NotificationService.Instance.ClearHistory();
        detector.SetFixtureMode(false);
    }

    [Fact]
    public async Task CreateAsync_WhenOperatingContextSelected_AddsScopeAndCurrencyBadges()
    {
        var detector = FixtureModeDetector.Instance;
        detector.SetFixtureMode(false);
        detector.UpdateBackendReachability(true);
        NotificationService.Instance.ClearHistory();

        var statusService = Substitute.For<IStatusService>();
        statusService.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new StatusResponse { IsConnected = true });

        var fundContext = await CreateFundContextAsync();
        var operatingContextService = await CreateOperatingContextServiceAsync(fundContext);
        var service = new WorkspaceShellContextService(
            fundContext,
            detector,
            NotificationService.Instance,
            statusService,
            operatingContextService);

        var context = await service.CreateAsync(new WorkspaceShellContextInput
        {
            WorkspaceTitle = "Research",
            WorkspaceSubtitle = "Shell",
            PrimaryScopeLabel = "Scope",
            PrimaryScopeValue = string.Empty,
            AsOfValue = "Apr 08 2026 09:30",
            FreshnessValue = "Backend connected",
            ReviewStateValue = "Ready",
            ReviewStateTone = WorkspaceTone.Success
        });

        context.Badges.Should().ContainSingle(b => b.Label == "Scope" && b.Value.Contains("Alpha Credit"));
        context.Badges.Should().ContainSingle(b => b.Label == "Scope" && b.Value == "Fund");
        context.Badges.Should().ContainSingle(b => b.Label == "Currency" && b.Value == "USD");
    }

    private static async Task<FundContextService> CreateFundContextAsync()
    {
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            "meridian-shell-context-tests",
            $"{Guid.NewGuid():N}.json");

        var service = new FundContextService(storagePath);
        await service.UpsertProfileAsync(new FundProfileDetail(
            FundProfileId: "alpha-credit",
            DisplayName: "Alpha Credit",
            LegalEntityName: "Alpha Credit Master Fund LP",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "governance",
            DefaultLandingPageTag: "GovernanceShell",
            DefaultLedgerScope: FundLedgerScope.Consolidated,
            IsDefault: true));
        await service.SelectFundProfileAsync("alpha-credit");
        return service;
    }

    private static async Task<WorkstationOperatingContextService> CreateOperatingContextServiceAsync(FundContextService fundContext)
    {
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            "meridian-shell-context-tests",
            $"{Guid.NewGuid():N}.operating-context.json");

        var service = new WorkstationOperatingContextService(fundContext, storagePath: storagePath);
        await service.LoadAsync();
        await service.SelectContextAsync(service.Contexts[0].ContextKey);
        return service;
    }
}
