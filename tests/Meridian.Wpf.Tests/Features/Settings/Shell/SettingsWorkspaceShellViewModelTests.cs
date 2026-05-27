using Meridian.Ui.Services;
using Meridian.Wpf.Features.Settings.Shell;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Tests.Features.Settings.Shell;

public sealed class SettingsWorkspaceShellViewModelTests
{
    [Fact]
    public async Task RefreshAsync_ShouldExposeSettingsPresentationState()
    {
        var operationsItem = new WorkspaceQueueItem
        {
            Title = "Credential readiness",
            PrimaryActionId = "CredentialManagement",
            PrimaryActionLabel = "Review"
        };
        var presentation = new SettingsWorkspaceShellPresentation
        {
            Context = new WorkspaceShellContextInput
            {
                WorkspaceTitle = "Settings",
                PrimaryScopeValue = "Compact"
            },
            CommandGroup = new WorkspaceCommandGroup
            {
                PrimaryCommands =
                [
                    new WorkspaceCommandItem { Id = "Settings", Label = "Preferences" }
                ]
            },
            HeroBadgeText = "Ready",
            HeroBadgeTone = WorkspaceTone.Success,
            HeroFocusText = "Support ready",
            HeroSummaryText = "Settings posture is ready.",
            HeroDetailText = "Open diagnostics and credentials.",
            OperationsItems = [operationsItem],
            SupportItems =
            [
                new WorkspaceQueueItem { Title = "Notifications", PrimaryActionId = "NotificationCenter" }
            ],
            QuickLinks =
            [
                new WorkspaceRecentItem { Title = "Workflow library", ActionId = "WorkflowLibrary" }
            ]
        };
        var snapshotService = new StubSnapshotService(new SettingsWorkspaceShellSnapshot
        {
            ProviderCount = 2,
            ConfiguredCredentialCount = 2,
            ShellDensityLabel = "Compact"
        });
        var presentationService = new StubPresentationService(presentation);
        var viewModel = new SettingsWorkspaceShellViewModel(snapshotService, presentationService);

        await viewModel.RefreshAsync();

        snapshotService.LoadCount.Should().Be(1);
        presentationService.LastSnapshot.Should().NotBeNull();
        viewModel.CommandGroup.PrimaryCommands.Should().ContainSingle().Which.Id.Should().Be("Settings");
        viewModel.HeroBadgeText.Should().Be("Ready");
        viewModel.HeroBadgeTone.Should().Be(WorkspaceTone.Success);
        viewModel.HeroFocusText.Should().Be("Support ready");
        viewModel.HeroSummaryText.Should().Be("Settings posture is ready.");
        viewModel.OperationsItems.Should().ContainSingle().Which.Should().BeSameAs(operationsItem);
        viewModel.SupportItems.Should().ContainSingle();
        viewModel.QuickLinks.Should().ContainSingle();
    }

    [Theory]
    [InlineData("Settings", false, SettingsWorkspaceShellActionKind.OpenPane, PaneDropAction.Replace)]
    [InlineData("CredentialManagement", false, SettingsWorkspaceShellActionKind.OpenPane, PaneDropAction.SplitRight)]
    [InlineData("SystemHealth", false, SettingsWorkspaceShellActionKind.OpenPane, PaneDropAction.SplitBelow)]
    [InlineData("NotificationCenter", true, SettingsWorkspaceShellActionKind.Navigate, PaneDropAction.OpenTab)]
    public async Task ExecuteActionAsync_ShouldKeepActionResolutionInViewModel(
        string actionId,
        bool navigate,
        SettingsWorkspaceShellActionKind expectedKind,
        PaneDropAction expectedDockAction)
    {
        var viewModel = new SettingsWorkspaceShellViewModel(new StubSnapshotService(new SettingsWorkspaceShellSnapshot()));
        SettingsWorkspaceShellActionRequestedEventArgs? request = null;
        viewModel.ActionRequested += (_, args) => request = args;

        await viewModel.ExecuteActionAsync(actionId, navigate);

        request.Should().NotBeNull();
        request!.Kind.Should().Be(expectedKind);
        request.ActionId.Should().Be(actionId);
        request.DockAction.Should().Be(expectedDockAction);
    }

    [Fact]
    public async Task ExecuteActionAsync_WithRefresh_ShouldRefreshWithoutNavigationRequest()
    {
        var snapshotService = new StubSnapshotService(new SettingsWorkspaceShellSnapshot());
        var viewModel = new SettingsWorkspaceShellViewModel(snapshotService);
        SettingsWorkspaceShellActionRequestedEventArgs? request = null;
        viewModel.ActionRequested += (_, args) => request = args;

        await viewModel.ExecuteActionAsync("Refresh", navigate: true);

        snapshotService.LoadCount.Should().Be(1);
        request.Should().NotBeNull();
        request!.Kind.Should().Be(SettingsWorkspaceShellActionKind.Refresh);
        request.ActionId.Should().Be("Refresh");
    }

    private sealed class StubSnapshotService : ISettingsWorkspaceShellSnapshotService
    {
        private readonly SettingsWorkspaceShellSnapshot _snapshot;

        public StubSnapshotService(SettingsWorkspaceShellSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public int LoadCount { get; private set; }

        public Task<SettingsWorkspaceShellSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(_snapshot);
        }
    }

    private sealed class StubPresentationService : ISettingsWorkspaceShellPresentationService
    {
        private readonly SettingsWorkspaceShellPresentation _presentation;

        public StubPresentationService(SettingsWorkspaceShellPresentation presentation)
        {
            _presentation = presentation;
        }

        public SettingsWorkspaceShellSnapshot? LastSnapshot { get; private set; }

        public SettingsWorkspaceShellPresentation Build(SettingsWorkspaceShellSnapshot snapshot)
        {
            LastSnapshot = snapshot;
            return _presentation;
        }
    }
}
