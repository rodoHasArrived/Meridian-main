using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Features.Data.Shell;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Views;

public sealed class DataWorkspaceShellSmokeTests
{
    [Fact]
    public void DataWorkspaceShell_ShouldConstructFromDi()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            AppServiceTestHost.InvokeConfigureServices(configureServices!, services);

            using var serviceProvider = services.BuildServiceProvider();

            var exception = Record.Exception(() =>
                serviceProvider.GetRequiredService<DataWorkspaceShellPage>());

            exception.Should().BeNull();
        });
    }

    [Fact]
    public void DataWorkspaceShellSource_ShouldExposeCompactWorkstationLayoutAheadOfOperationalQueues()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Features\Data\Shell\DataWorkspaceShellPage.xaml"));
        var code = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Features\Data\Shell\DataWorkspaceShellPage.xaml.cs"));
        var viewModel = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Features\Data\Shell\DataWorkspaceShellViewModel.cs"));
        var snapshotService = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Features\Data\Shell\DataWorkspaceShellSnapshotService.cs"));
        var presentationBuilder = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Services\DataWorkspacePresentationBuilder.cs"));

        xaml.Should().Contain("WorkspaceCopyCatalog.DataShellTitle");
        xaml.Should().NotContain("WorkspaceCopyCatalog.DataOperationsShellTitle");
        xaml.Should().Contain("DataWorkspaceShellPageBase");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"DataWorkspaceShellPage\"");
        xaml.Should().NotContain("DataOperationsWorkspaceShellPageBase");
        xaml.Should().NotContain("AutomationProperties.AutomationId=\"DataOperationsWorkspaceShellPage\"");
        xaml.Should().Contain("Style=\"{StaticResource WorkstationPageBandStyle}\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"DataWorkspaceFilterBar\"");
        xaml.Should().Contain("Style=\"{StaticResource WorkstationFilterBarStyle}\"");
        xaml.Should().Contain("Style=\"{StaticResource WorkstationFilterChipStyle}\"");
        xaml.Should().Contain("Style=\"{StaticResource WorkstationTablePanelStyle}\"");
        xaml.Should().Contain("Style=\"{StaticResource WorkstationPanelHeaderStyle}\"");
        xaml.Should().Contain("Style=\"{StaticResource WorkstationPanelBodyStyle}\"");
        xaml.Should().Contain("Style=\"{StaticResource WorkstationInspectorRailStyle}\"");
        xaml.Should().Contain("Style=\"{StaticResource WorkstationDockStripStyle}\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"DataWorkspaceQueuePanel\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"DataWorkspaceDockStrip\"");
        xaml.Should().Contain("WorkspaceShellContextStripControl");
        xaml.Should().Contain("Next Handoff");
        xaml.Should().Contain("OperationsHeroScopeText");
        xaml.Should().Contain("OperationsHeroSummaryText");
        xaml.Should().Contain("OperationsHeroFocusText");
        xaml.Should().Contain("OperationsHeroActionSummaryText");
        xaml.Should().Contain("OperationsHeroMetricsList");
        xaml.Should().Contain("HeroMetricTemplate");
        xaml.Should().Contain("OperationsHeroHandoffTitleText");
        xaml.Should().Contain("OperationsHeroPrimaryActionButton");
        xaml.Should().Contain("OperationsHeroSecondaryActionButton");
        xaml.Should().Contain("OperationsHeroTargetText");
        xaml.IndexOf("DataWorkspaceFilterBar", StringComparison.Ordinal).Should().BeLessThan(xaml.IndexOf("Operational Queues", StringComparison.Ordinal));
        xaml.IndexOf("OperationsHeroSummaryText", StringComparison.Ordinal).Should().BeLessThan(xaml.IndexOf("DataWorkspaceFilterBar", StringComparison.Ordinal));
        xaml.IndexOf("DataWorkspaceQueuePanel", StringComparison.Ordinal).Should().BeLessThan(xaml.IndexOf("WorkspaceInspectorHostData", StringComparison.Ordinal));
        xaml.Should().Contain("MinHeight=\"176\"");
        xaml.Should().Contain("MaxHeight=\"240\"");
        xaml.Should().Contain("Height=\"196\"");

        xaml.Should().Contain("Text=\"{Binding HeroScopeText}\"");
        xaml.Should().Contain("Text=\"{Binding HeroSummaryText}\"");
        xaml.Should().Contain("Text=\"{Binding HeroState.FocusText}\"");
        xaml.Should().Contain("Text=\"{Binding HeroState.SummaryText}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding HeroMetrics}\"");
        code.Should().NotContain("ContextStrip.ShellContext");
        code.Should().NotContain("OperationsHeroScopeText.Text =");
        code.Should().Contain("DataViewModel.RefreshRequested += OnRefreshRequested;");
        code.Should().Contain("DataViewModel.ActionRequested += OnActionRequested;");
        code.Should().Contain("=> DataViewModel.RefreshAsync();");
        code.Should().NotContain("LoadWorkspaceDataAsync");
        code.Should().NotContain("DataWorkspacePresentationBuilder.Build");
        code.Should().NotContain("DataOperationsWorkspaceShellStateProvider");
        viewModel.Should().Contain("HeroState = DataHeroState.Loading();");
        viewModel.Should().Contain("HeroMetrics = DataHeroMetric.LoadingMetrics();");
        viewModel.Should().Contain("HeroState = presentation.HeroState;");
        viewModel.Should().Contain("HeroState = DataHeroState.Error();");
        viewModel.Should().Contain("HeroMetrics = DataHeroMetric.ErrorMetrics();");
        snapshotService.Should().Contain("LoadAsync(CancellationToken cancellationToken = default)");
        snapshotService.Should().Contain("WorkspaceCopyCatalog.Data.DefaultScopeLabel");
        snapshotService.Should().NotContain("WorkspaceCopyCatalog.DataOperations.");
        presentationBuilder.Should().Contain("WorkspaceCopyCatalog.Data.ShellTitle");
        presentationBuilder.Should().NotContain("WorkspaceCopyCatalog.DataOperations.");
        code.Should().Contain("private async void OnOperationsHeroPrimaryActionClick");
        code.Should().Contain("private async void OnOperationsHeroSecondaryActionClick");
        xaml.Should().Contain("WorkspaceDecisionQueueControl");
        xaml.Should().Contain("QueueAutomationId=\"DataProviderDecisionQueue\"");
        xaml.Should().Contain("QueueAutomationId=\"DataBackfillDecisionQueue\"");
        xaml.Should().Contain("QueueAutomationId=\"DataStorageDecisionQueue\"");
        xaml.Should().Contain("DecisionInvoked=\"OnDataDecisionInvoked\"");
        xaml.Should().NotContain("QueueItemTemplate");
        code.Should().Contain("private async void OnDataDecisionInvoked");
        code.Should().Contain("WorkspaceDecisionInvokedEventArgs e");
        code.Should().NotContain("OnQueuePrimaryActionClick");
        code.Should().NotContain("OnQueueSecondaryActionClick");
    }

    [Fact]
    public void DataFeatureModuleSource_ShouldIsolateLegacyDataShellAliases()
    {
        var source = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Features\Data\DataFeatureModule.cs"));

        source.Should().Contain("LegacyDataShellAliases");
        source.Should().Contain("ShellPageRegistryBuilder.Page<DataWorkspaceShellPage>");
        source.Should().Contain("LegacyDataShellAliases),");
        source.Should().NotContain("[\"DataOperationsShell\", \"DataOperationsWorkspace\"]");
    }

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
