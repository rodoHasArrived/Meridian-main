using System.Windows;
using Meridian.Wpf.Features.Data.Shell;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Features.Data.Shell;

public sealed class DataWorkspaceShellViewModelTests
{
    [Fact]
    public async Task RefreshAsync_ShouldExposePresentationState()
    {
        var commandGroup = new WorkspaceCommandGroup
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "ProviderHealth", Label = "Provider Health" }
            ]
        };
        var providerQueueItem = new WorkspaceQueueItem
        {
            Title = "Provider route",
            Detail = "Healthy provider path.",
            PrimaryActionId = "ProviderHealth",
            PrimaryActionLabel = "Open"
        };
        var presentation = new DataWorkspacePresentation
        {
            Context = new WorkspaceShellContextInput
            {
                PrimaryScopeValue = "Fund A",
                WorkspaceTitle = "Data"
            },
            CommandGroup = commandGroup,
            HeroState = new DataHeroState
            {
                FocusText = "Provider posture",
                SummaryText = "Provider state is ready.",
                BadgeText = "Ready",
                PrimaryActionId = "ProviderHealth",
                PrimaryActionLabel = "Open provider health"
            },
            HeroMetrics =
            [
                new DataHeroMetric { Label = "Providers", Value = "1", Detail = "One provider" }
            ],
            IntegrationWorkflowSteps =
            [
                new DataIntegrationWorkflowStep
                {
                    Id = "connect-source",
                    Label = "Connect Source",
                    Description = "Provider ready.",
                    Status = WorkflowStepStatus.Complete,
                    StatusLabel = "Connected",
                    Tone = WorkspaceTone.Success
                }
            ],
            QueueScopeBadgeText = "Fund A",
            QueueSummaryText = "Provider and storage queues are ready.",
            ProviderQueueItems = [providerQueueItem],
            BackfillQueueState = WorkspaceQueueRegionState.Empty("No backfill", "No work queued.", "Open Backfill", "Backfill"),
            OperationsSummaryTitleText = "Data",
            OperationsSummaryDetailText = "Current scope",
            SummaryProvidersText = "1 healthy",
            SummaryProvidersTone = WorkspaceTone.Success,
            SummaryBackfillText = "No active backfill",
            SummaryStorageText = "Stable",
            RecentOperations =
            [
                new WorkspaceRecentItem { Title = "Session", ActionId = "CollectionSessions", ActionLabel = "Open" }
            ]
        };
        var snapshotService = new StubSnapshotService(new DataWorkspaceData());
        var presentationService = new StubPresentationService(presentation);
        var viewModel = new DataWorkspaceShellViewModel(snapshotService, presentationService);

        await viewModel.RefreshAsync();

        snapshotService.LoadCount.Should().Be(1);
        presentationService.LastData.Should().NotBeNull();
        viewModel.CommandGroup.Should().BeSameAs(commandGroup);
        viewModel.HeroScopeText.Should().Be("Fund A");
        viewModel.HeroState.FocusText.Should().Be("Provider posture");
        viewModel.HeroPrimaryActionVisibility.Should().Be(Visibility.Visible);
        viewModel.HeroSecondaryActionVisibility.Should().Be(Visibility.Collapsed);
        viewModel.HeroMetrics.Should().HaveCount(1);
        viewModel.IntegrationWorkflowSteps.Should().ContainSingle()
            .Which.StatusLabel.Should().Be("Connected");
        viewModel.QueueSummaryText.Should().Be("Provider and storage queues are ready.");
        viewModel.ProviderQueueItems.Should().ContainSingle().Which.Should().BeSameAs(providerQueueItem);
        viewModel.ProviderQueueListVisibility.Should().Be(Visibility.Visible);
        viewModel.ProviderQueueStateVisibility.Should().Be(Visibility.Collapsed);
        viewModel.BackfillQueueState.IsEmpty.Should().BeTrue();
        viewModel.BackfillQueueListVisibility.Should().Be(Visibility.Collapsed);
        viewModel.BackfillQueueStateVisibility.Should().Be(Visibility.Visible);
        viewModel.SummaryProvidersText.Should().Be("1 healthy");
        viewModel.SummaryProvidersTone.Should().Be(WorkspaceTone.Success);
        viewModel.RecentOperations.Should().ContainSingle();
    }

    [Theory]
    [InlineData("Retry", null, PaneDropAction.OpenTab)]
    [InlineData("SwitchContext", DataWorkspaceShellActionKind.SwitchContext, PaneDropAction.OpenTab)]
    [InlineData("ProviderHealth", DataWorkspaceShellActionKind.OpenPane, PaneDropAction.Replace)]
    [InlineData("Backfill", DataWorkspaceShellActionKind.OpenPane, PaneDropAction.SplitRight)]
    [InlineData("Storage", DataWorkspaceShellActionKind.OpenPane, PaneDropAction.SplitBelow)]
    public async Task ExecuteActionAsync_ShouldKeepActionResolutionInViewModel(
        string actionId,
        DataWorkspaceShellActionKind? expectedKind,
        PaneDropAction expectedDockAction)
    {
        var snapshotService = new StubSnapshotService(new DataWorkspaceData());
        var viewModel = new DataWorkspaceShellViewModel(snapshotService, new StubPresentationService(new DataWorkspacePresentation()));
        DataWorkspaceShellActionRequestedEventArgs? request = null;
        viewModel.ActionRequested += (_, args) => request = args;

        await viewModel.ExecuteActionAsync(actionId, navigate: false);

        if (expectedKind is null)
        {
            request.Should().BeNull();
            snapshotService.LoadCount.Should().Be(1);
            return;
        }

        request.Should().NotBeNull();
        request!.Kind.Should().Be(expectedKind);
        request.ActionId.Should().Be(actionId);
        request.DockAction.Should().Be(expectedDockAction);
    }

    [Fact]
    public async Task ExecuteActionAsync_WithNavigationCommand_ShouldRaiseNavigateRequest()
    {
        var viewModel = new DataWorkspaceShellViewModel(new StubSnapshotService(new DataWorkspaceData()));
        DataWorkspaceShellActionRequestedEventArgs? request = null;
        viewModel.ActionRequested += (_, args) => request = args;

        await viewModel.ExecuteActionAsync("ProviderHealth", navigate: true);

        request.Should().NotBeNull();
        request!.Kind.Should().Be(DataWorkspaceShellActionKind.Navigate);
        request.ActionId.Should().Be("ProviderHealth");
    }

    private sealed class StubSnapshotService : IDataWorkspaceShellSnapshotService
    {
        private readonly DataWorkspaceData _data;

        public StubSnapshotService(DataWorkspaceData data)
        {
            _data = data;
        }

        public int LoadCount { get; private set; }

        public Task<DataWorkspaceData> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(_data);
        }
    }

    private sealed class StubPresentationService : IDataWorkspaceShellPresentationService
    {
        private readonly DataWorkspacePresentation _presentation;

        public StubPresentationService(DataWorkspacePresentation presentation)
        {
            _presentation = presentation;
        }

        public DataWorkspaceData? LastData { get; private set; }

        public DataWorkspacePresentation Build(DataWorkspaceData data)
        {
            LastData = data;
            return _presentation;
        }
    }
}
