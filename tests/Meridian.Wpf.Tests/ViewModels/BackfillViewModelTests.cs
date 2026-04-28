using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class BackfillViewModelTests
{
    [Fact]
    public void BuildStartSetupState_WithoutSymbols_DisablesStartAndExplainsRecovery()
    {
        var state = BackfillViewModel.BuildStartSetupState(
            symbolsText: "",
            fromDate: new DateTime(2026, 4, 1),
            toDate: new DateTime(2026, 4, 28),
            providerDisplay: "Multi-Source (Auto-Failover)",
            provider: "composite",
            granularity: "Daily");

        state.CanStart.Should().BeFalse();
        state.ReadinessTitle.Should().Be("Backfill setup incomplete");
        state.ReadinessDetail.Should().Contain("Add at least one symbol");
        state.SymbolCountText.Should().Be("0 symbols");
        state.SymbolsValidationError.Should().Be("Please enter at least one symbol");
        state.IsSymbolsValidationErrorVisible.Should().BeTrue();
    }

    [Fact]
    public void BuildStartSetupState_WithInvalidDateRange_DisablesStartAndSurfacesBoundValidation()
    {
        var state = BackfillViewModel.BuildStartSetupState(
            symbolsText: "SPY, QQQ",
            fromDate: new DateTime(2026, 4, 28),
            toDate: new DateTime(2026, 4, 1),
            providerDisplay: "Yahoo Finance (Free)",
            provider: "yahoo",
            granularity: "5Min");

        state.CanStart.Should().BeFalse();
        state.ReadinessDetail.Should().Contain("Fix the date range");
        state.FromDateValidationError.Should().Be("From date must be earlier than To date");
        state.ToDateValidationError.Should().Be("To date must be on or after From date");
        state.IsFromDateValidationErrorVisible.Should().BeTrue();
        state.IsToDateValidationErrorVisible.Should().BeTrue();
    }

    [Fact]
    public void BuildStartSetupState_WithValidRequest_DeduplicatesSymbolsAndDescribesScope()
    {
        var state = BackfillViewModel.BuildStartSetupState(
            symbolsText: " spy, SPY, qqq ",
            fromDate: new DateTime(2026, 4, 1),
            toDate: new DateTime(2026, 4, 28),
            providerDisplay: "Multi-Source (Auto-Failover)",
            provider: "composite",
            granularity: "Hourly");

        state.CanStart.Should().BeTrue();
        state.ReadinessTitle.Should().Be("Backfill ready");
        state.ReadinessDetail.Should().Contain("hourly history");
        state.ReadinessDetail.Should().Contain("SPY, QQQ");
        state.ScopeText.Should().Contain("SPY, QQQ");
        state.ScopeText.Should().Contain("Apr 1, 2026 to Apr 28, 2026");
        state.SymbolCountText.Should().Be("2 symbols");
        state.IsSymbolsValidationErrorVisible.Should().BeFalse();
    }

    [Fact]
    public void BackfillPageSource_BindsStartReadinessThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\BackfillPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\BackfillPage.xaml.cs"));

        xaml.Should().Contain("BackfillStartReadinessCard");
        xaml.Should().Contain("{Binding StartReadinessTitle}");
        xaml.Should().Contain("{Binding StartReadinessDetail}");
        xaml.Should().Contain("{Binding StartReadinessScopeText}");
        xaml.Should().Contain("{Binding SymbolsValidationErrorText}");
        xaml.Should().Contain("{Binding FromDateValidationErrorText}");
        xaml.Should().Contain("{Binding ToDateValidationErrorText}");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"BackfillStartButton\"");
        xaml.Should().Contain("IsEnabled=\"{Binding CanStartBackfill}\"");

        codeBehind.Should().Contain("RefreshStartSetupState();");
        codeBehind.Should().NotContain("SymbolsValidationError.Text =");
        codeBehind.Should().NotContain("FromDateValidationError.Text =");
        codeBehind.Should().NotContain("ToDateValidationError.Text =");
    }
}
