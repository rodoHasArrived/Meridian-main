using System.Windows.Controls;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class WelcomePageViewModelTests : IDisposable
{
    public void Dispose()
    {
        NavigationService.Instance.ResetForTests();
    }

    private static WelcomePageViewModel CreateViewModel()
    {
        var navigationService = NavigationService.Instance;
        navigationService.ResetForTests();
        navigationService.Initialize(new Frame());

        return new WelcomePageViewModel(
            navigationService,
            NotificationService.Instance,
            StatusService.Instance,
            ConnectionService.Instance,
            ConfigService.Instance);
    }

    [Fact]
    public void WorkspaceCards_ExposeCanonicalOperatorShells()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();

            viewModel.WorkspaceCards.Select(card => card.Title)
                .Should()
                .Equal("Research", "Trading", "Data Operations", "Governance");

            viewModel.WorkspaceCards.Select(card => card.PageTag)
                .Should()
                .Equal("ResearchShell", "TradingShell", "DataOperationsShell", "GovernanceShell");
        });
    }

    [Theory]
    [InlineData("ResearchShell")]
    [InlineData("TradingShell")]
    [InlineData("DataOperationsShell")]
    [InlineData("GovernanceShell")]
    public void NavigateToWorkspaceCommand_NavigatesToRequestedShell(string pageTag)
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();

            viewModel.NavigateToWorkspaceCommand.Execute(pageTag);

            NavigationService.Instance.GetCurrentPageTag().Should().Be(pageTag);
        });
    }

    [Fact]
    public void NavigateToDataOperationsWorkspaceCommand_NavigatesToDataOperationsShell()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();

            viewModel.NavigateToDataOperationsWorkspaceCommand.Execute(null);

            NavigationService.Instance.GetCurrentPageTag().Should().Be("DataOperationsShell");
        });
    }

    [Fact]
    public void ApplyOverviewSnapshotForTests_WhenDisconnected_PrioritisesProviderRecovery()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();

            viewModel.ApplyOverviewSnapshotForTests(
                isConnected: false,
                connectionProviderText: "No provider connected",
                symbolCount: 0,
                storagePath: @"C:\Meridian\data",
                configuredDataRoot: @"C:\Meridian\data");

            viewModel.NextAction.ToneLabel.Should().Be("Connection blocker");
            viewModel.NextAction.PrimaryActionPageTag.Should().Be("Provider");
            viewModel.ReadinessItems.Should().ContainSingle(item =>
                item.Title == "Provider session" && item.StatusLabel == "Blocked");
        });
    }

    [Fact]
    public void ApplyOverviewSnapshotForTests_WhenConnectedWithoutSymbols_RecommendsSymbolSetup()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();

            viewModel.ApplyOverviewSnapshotForTests(
                isConnected: true,
                connectionProviderText: "Alpaca",
                symbolCount: 0,
                storagePath: @"C:\Meridian\data",
                configuredDataRoot: @"C:\Meridian\data");

            viewModel.NextAction.ToneLabel.Should().Be("Setup gap");
            viewModel.NextAction.PrimaryActionPageTag.Should().Be("Symbols");
            viewModel.ReadinessItems.Should().ContainSingle(item =>
                item.Title == "Symbol inventory" && item.StatusLabel == "Needs symbols");
        });
    }

    [Fact]
    public void ApplyOverviewSnapshotForTests_WhenStorageUsesDefaultPath_RecommendsStorageReview()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();

            viewModel.ApplyOverviewSnapshotForTests(
                isConnected: true,
                connectionProviderText: "Alpaca",
                symbolCount: 12,
                storagePath: @"C:\Users\Trader\AppData\Local\Meridian\data",
                configuredDataRoot: "data");

            viewModel.NextAction.ToneLabel.Should().Be("Storage review");
            viewModel.NextAction.PrimaryActionPageTag.Should().Be("Storage");
            viewModel.ReadinessItems.Should().ContainSingle(item =>
                item.Title == "Storage target" && item.StatusLabel == "Default path");
        });
    }

    [Fact]
    public void ApplyOverviewSnapshotForTests_WhenSetupIsHealthy_RoutesIntoOperatorFlow()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();

            viewModel.ApplyOverviewSnapshotForTests(
                isConnected: true,
                connectionProviderText: "Alpaca",
                symbolCount: 12,
                storagePath: @"D:\Meridian\data",
                configuredDataRoot: @"D:\Meridian\data");

            viewModel.NextAction.ToneLabel.Should().Be("Ready for operator flow");
            viewModel.NextAction.PrimaryActionPageTag.Should().Be("TradingShell");
            viewModel.NextAction.SecondaryActionPageTag.Should().Be("ResearchShell");
            viewModel.ReadinessItems.Should().ContainSingle(item =>
                item.Title == "Storage target" && item.StatusLabel == "Custom path");
        });
    }
}
