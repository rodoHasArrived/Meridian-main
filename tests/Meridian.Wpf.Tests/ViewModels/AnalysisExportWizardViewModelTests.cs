using System.Windows;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class AnalysisExportWizardViewModelTests
{
    [Fact]
    public void Initialize_ProjectsScopeReadinessAndCommandState()
    {
        var viewModel = new AnalysisExportWizardViewModel();

        viewModel.Initialize();

        viewModel.CurrentStep.Should().Be(1);
        viewModel.CurrentStepTitle.Should().Be("Select export scope");
        viewModel.ActionReadinessTitle.Should().Be("Scope ready");
        viewModel.WizardScopeText.Should().Contain("2 symbols selected");
        viewModel.ValidationVisibility.Should().Be(Visibility.Collapsed);
        viewModel.PrimaryActionCommand.CanExecute(null).Should().BeTrue();
        viewModel.AddSymbolCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void EmptySymbolScope_DisablesNextAndExplainsRecovery()
    {
        var viewModel = new AnalysisExportWizardViewModel();
        viewModel.Initialize();

        viewModel.SelectedSymbols.Clear();

        viewModel.PrimaryActionCommand.CanExecute(null).Should().BeFalse();
        viewModel.ActionReadinessTitle.Should().Be("Scope setup incomplete");
        viewModel.ActionReadinessDetail.Should().Contain("Add at least one symbol");
        viewModel.ValidationVisibility.Should().Be(Visibility.Visible);
        viewModel.WizardScopeText.Should().Contain("No symbols selected");
    }

    [Fact]
    public void StepTwo_RequiresDestinationAndMetricBeforeReview()
    {
        var viewModel = new AnalysisExportWizardViewModel();
        viewModel.Initialize();

        viewModel.PrimaryActionCommand.Execute(null);

        viewModel.CurrentStep.Should().Be(2);
        viewModel.PrimaryActionCommand.CanExecute(null).Should().BeFalse();
        viewModel.ActionReadinessTitle.Should().Be("Package setup incomplete");
        viewModel.ActionReadinessDetail.Should().Contain("Destination is required");
        viewModel.ActionReadinessDetail.Should().Contain("Select at least one metric");

        viewModel.Destination = Path.Combine(Path.GetTempPath(), "analysis-export-wizard-test.csv");
        viewModel.Metrics[0].IsSelected = true;

        viewModel.PrimaryActionCommand.CanExecute(null).Should().BeTrue();
        viewModel.ActionReadinessTitle.Should().Be("Package setup ready");

        viewModel.PrimaryActionCommand.Execute(null);

        viewModel.CurrentStep.Should().Be(3);
        viewModel.PreExportReport.Should().Contain("All checks passed");
        viewModel.ActionReadinessTitle.Should().Be("Export ready to queue");
        viewModel.PrimaryActionCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void AddSymbolCommand_NormalizesDeduplicatesAndRefreshesScope()
    {
        var viewModel = new AnalysisExportWizardViewModel();
        viewModel.Initialize();

        viewModel.SymbolInput = " spy ";
        viewModel.AddSymbolCommand.Execute(null);
        viewModel.SymbolInput = "SPY";
        viewModel.AddSymbolCommand.Execute(null);

        viewModel.SelectedSymbols.Should().ContainSingle(symbol => symbol == "SPY");
        viewModel.SymbolInput.Should().BeEmpty();
        viewModel.StatusMessage.Should().Be("SPY is already selected.");
        viewModel.WizardScopeText.Should().Contain("3 symbols selected");
    }

    [Fact]
    public void CancelCommand_ResetsWizardAndShowsStatus()
    {
        var viewModel = new AnalysisExportWizardViewModel();
        viewModel.Initialize();
        viewModel.PrimaryActionCommand.Execute(null);

        viewModel.CancelCommand.Execute(null);

        viewModel.CurrentStep.Should().Be(1);
        viewModel.StatusMessage.Should().Be("Wizard reset.");
        viewModel.StatusVisibility.Should().Be(Visibility.Visible);
        viewModel.PreExportReport.Should().BeEmpty();
        viewModel.EstimatedSize.Should().BeEmpty();
    }

    [Fact]
    public void AnalysisExportWizardPageSource_BindsActionsAndReadinessThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AnalysisExportWizardPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AnalysisExportWizardPage.xaml.cs"));

        xaml.Should().Contain("AnalysisExportWizardReadinessCard");
        xaml.Should().Contain("{Binding CurrentStepTitle}");
        xaml.Should().Contain("{Binding CurrentStepDetail}");
        xaml.Should().Contain("{Binding WizardScopeText}");
        xaml.Should().Contain("{Binding ActionReadinessTitle}");
        xaml.Should().Contain("{Binding ActionReadinessDetail}");
        xaml.Should().Contain("Command=\"{Binding AddSymbolCommand}\"");
        xaml.Should().Contain("Command=\"{Binding BackCommand}\"");
        xaml.Should().Contain("Command=\"{Binding PrimaryActionCommand}\"");
        xaml.Should().Contain("Command=\"{Binding CancelCommand}\"");
        xaml.Should().Contain("{Binding ValidationVisibility}");
        xaml.Should().Contain("{Binding StatusVisibility}");
        xaml.Should().NotContain("Click=\"AddSymbol_Click\"");
        xaml.Should().NotContain("Click=\"Back_Click\"");
        xaml.Should().NotContain("Click=\"Next_Click\"");
        xaml.Should().NotContain("Click=\"Cancel_Click\"");

        codeBehind.Should().NotContain("AddSymbol_Click");
        codeBehind.Should().NotContain("Back_Click");
        codeBehind.Should().NotContain("Next_Click");
        codeBehind.Should().NotContain("Cancel_Click");
    }
}
