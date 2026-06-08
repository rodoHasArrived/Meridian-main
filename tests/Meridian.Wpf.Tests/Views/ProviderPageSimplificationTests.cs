using System.IO;
using FluentAssertions;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Views;

public sealed class ProviderPageSimplificationTests
{
    [Fact]
    public void ProviderPage_ShouldKeepOnlyBackfillSpecificProviderControls()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ProviderPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ProviderPage.xaml.cs"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\ProviderViewModel.cs"));

        xaml.Should().Contain("Backfill Provider Settings");
        xaml.Should().Contain("Fallback Chain Preview");
        xaml.Should().Contain("Dry-Run Backfill Plan");
        xaml.Should().Contain("Audit Trail");
        xaml.Should().Contain("Backfill Operator Workflow");
        xaml.Should().Contain("ProviderBackfillActionStrip");
        xaml.Should().Contain("ProviderBackfillPostureCard");
        xaml.Should().Contain("WorkstationTableInspectorControl");
        xaml.Should().Contain("ProviderBackfillSettingsGrid");
        xaml.Should().Contain("ProviderBackfillSettingsInspector");
        xaml.Should().Contain("ProviderBackfillActionInspector");
        xaml.Should().Contain("ProviderFallbackChainGrid");
        xaml.Should().Contain("ProviderDryRunPlanGrid");
        xaml.Should().Contain("ProviderAuditTrailGrid");
        xaml.Should().Contain("Command=\"{Binding SaveSelectedProviderCommand}\"");
        xaml.Should().Contain("Command=\"{Binding ResetSelectedProviderCommand}\"");
        xaml.Should().Contain("ToolTip=\"{Binding SaveSelectedProviderTooltip}\"");
        xaml.Should().Contain("ToolTip=\"{Binding ResetSelectedProviderTooltip}\"");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");

        xaml.Should().NotContain("Streaming Providers");
        xaml.Should().NotContain("Depth and tick-by-tick streaming via TWS or Gateway.");
        xaml.Should().NotContain("WebSocket trades and quotes with an accessible free tier.");
        xaml.Should().NotContain("Resilient streaming with retry and circuit-breaker patterns.");
        xaml.Should().NotContain("Hybrid streaming and historical coverage for exchange-driven workflows.");
        xaml.Should().NotContain("Active Streaming Provider");
        xaml.Should().NotContain("Provider Configuration");
        xaml.Should().NotContain("ProviderToggle_Changed");
        xaml.Should().NotContain("PriorityField_LostFocus");
        xaml.Should().NotContain("RateLimitField_LostFocus");
        xaml.Should().NotContain("ResetProvider_Click");
        xaml.Should().NotContain("Click=\"ConfigureProvider_Click\"");
        codeBehind.Should().NotContain("ProviderToggle_Changed");
        codeBehind.Should().NotContain("PriorityField_LostFocus");
        codeBehind.Should().NotContain("RateLimitField_LostFocus");
        codeBehind.Should().NotContain("ResetProvider_Click");
        codeBehind.Should().NotContain("ConfigureProvider_Click");
        viewModel.Should().Contain("public WorkstationTableModel<ProviderSettingsViewModel> ProviderSettingsTable { get; }");
        viewModel.Should().Contain("public InspectorPanelModel SelectedProviderInspector");
        viewModel.Should().Contain("public InspectorPanelModel ProviderActionInspector");
        viewModel.Should().Contain("public IAsyncRelayCommand SaveSelectedProviderCommand");
        viewModel.Should().Contain("public IAsyncRelayCommand ResetSelectedProviderCommand");
        viewModel.Should().NotContain("OnConfigureProvider");
    }
}
