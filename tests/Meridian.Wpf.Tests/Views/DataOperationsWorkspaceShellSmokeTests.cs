using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Features.Data.Shell;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Views;

public sealed class DataOperationsWorkspaceShellSmokeTests
{
    [Fact]
    public void DataOperationsWorkspaceShell_ShouldConstructFromDi()
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
    public void DataOperationsWorkspaceShellSource_ShouldExposeBriefingHeaderAheadOfOperationalQueues()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Features\Data\Shell\DataWorkspaceShellPage.xaml"));
        var code = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Features\Data\Shell\DataWorkspaceShellPage.xaml.cs"));
        var viewModel = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Features\Data\Shell\DataWorkspaceShellViewModel.cs"));
        var snapshotService = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Features\Data\Shell\DataWorkspaceShellSnapshotService.cs"));

        xaml.Should().NotContain("WorkspaceShellContextStripControl");
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
        xaml.IndexOf("OperationsHeroSummaryText", StringComparison.Ordinal).Should().BeLessThan(xaml.IndexOf("Operational Queues", StringComparison.Ordinal));

        code.Should().NotContain("ContextStrip.ShellContext");
        code.Should().Contain("OperationsHeroScopeText.Text = viewModel.HeroScopeText;");
        code.Should().Contain("OperationsHeroSummaryText.Text = viewModel.HeroSummaryText;");
        code.Should().Contain("OperationsHeroFocusText.Text = heroState.FocusText;");
        code.Should().Contain("OperationsHeroActionSummaryText.Text = heroState.SummaryText;");
        code.Should().Contain("OperationsHeroMetricsList.ItemsSource = viewModel.HeroMetrics;");
        code.Should().NotContain("LoadWorkspaceDataAsync");
        code.Should().NotContain("DataOperationsWorkspacePresentationBuilder.Build");
        viewModel.Should().Contain("HeroState = DataOperationsHeroState.Loading();");
        viewModel.Should().Contain("HeroMetrics = DataOperationsHeroMetric.LoadingMetrics();");
        viewModel.Should().Contain("HeroState = presentation.HeroState;");
        viewModel.Should().Contain("HeroState = DataOperationsHeroState.Error();");
        viewModel.Should().Contain("HeroMetrics = DataOperationsHeroMetric.ErrorMetrics();");
        snapshotService.Should().Contain("LoadAsync(CancellationToken cancellationToken = default)");
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
