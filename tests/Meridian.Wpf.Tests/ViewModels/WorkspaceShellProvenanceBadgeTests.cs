using System.IO;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

// W9-TRUTH-001: the Portfolio and Reporting workspace shells previously rendered an empty
// context strip, so a backend serving seeded data carried no per-screen simulation label
// there. These tests pin that both shells now compose the shared context strip and that the
// strip's Environment badge surfaces the server-reported provenance fail-closed.
public sealed class WorkspaceShellProvenanceBadgeTests
{
    [Fact]
    public async Task PortfolioShell_RefreshShellContext_SurfacesSeededProvenanceBadge()
    {
        var service = await CreateShellContextServiceAsync("seeded");
        var viewModel = new PortfolioWorkspaceShellViewModel(service);

        await viewModel.RefreshShellContextAsync();

        viewModel.ShellContext.WorkspaceTitle.Should().Be("Portfolio");
        viewModel.ShellContext.Badges.Should().ContainSingle(
            b => b.Label == "Environment" && b.Value == "SEEDED data" && b.Tone == WorkspaceTone.Warning);
    }

    [Fact]
    public async Task PortfolioShell_WithoutShellContextService_KeepsEmptyContextWithoutFailing()
    {
        var viewModel = new PortfolioWorkspaceShellViewModel();

        await viewModel.RefreshShellContextAsync();

        viewModel.ShellContext.Badges.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportingShell_Refresh_SurfacesSeededProvenanceBadgeWithoutFundContext()
    {
        var service = await CreateShellContextServiceAsync("seeded");
        var viewModel = new ReportingWorkspaceShellViewModel(shellContextService: service);

        await viewModel.RefreshAsync();

        viewModel.ShellContext.WorkspaceTitle.Should().Be("Reporting");
        viewModel.ShellContext.Badges.Should().ContainSingle(
            b => b.Label == "Environment" && b.Value == "SEEDED data" && b.Tone == WorkspaceTone.Warning);
    }

    [Fact]
    public async Task ReportingShell_Refresh_WhenBackendServesRealData_ReportsLiveEnvironment()
    {
        var service = await CreateShellContextServiceAsync(serverProvenanceToken: null);
        var viewModel = new ReportingWorkspaceShellViewModel(shellContextService: service);

        await viewModel.RefreshAsync();

        viewModel.ShellContext.Badges.Should().ContainSingle(
            b => b.Label == "Environment" && b.Value == "Live" && b.Tone == WorkspaceTone.Success);
    }

    private static async Task<WorkspaceShellContextService> CreateShellContextServiceAsync(string? serverProvenanceToken)
    {
        var detector = (FixtureModeDetector)Activator.CreateInstance(typeof(FixtureModeDetector), nonPublic: true)!;
        detector.SetFixtureMode(false);
        detector.UpdateBackendReachability(true);
        detector.ReportServerDataProvenance(serverProvenanceToken);

        var notificationService =
            (NotificationService)Activator.CreateInstance(typeof(NotificationService), nonPublic: true)!;

        var statusService = Substitute.For<IStatusService>();
        statusService.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new StatusResponse { IsConnected = true });

        var storagePath = Path.Combine(
            Path.GetTempPath(),
            "meridian-shell-provenance-tests",
            $"{Guid.NewGuid():N}.json");

        return new WorkspaceShellContextService(
            new FundContextService(storagePath),
            detector,
            notificationService,
            statusService);
    }
}
