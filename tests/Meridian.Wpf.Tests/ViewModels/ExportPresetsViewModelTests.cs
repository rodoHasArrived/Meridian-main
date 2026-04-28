using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ExportPresetsViewModelTests
{
    [Fact]
    public void NewViewModel_DisablesSaveAndExplainsMissingPresetName()
    {
        var viewModel = new ExportPresetsViewModel();

        viewModel.SavePresetCommand.CanExecute(null).Should().BeFalse();
        viewModel.CanSavePreset.Should().BeFalse();
        viewModel.PresetReadinessTitle.Should().Be("Preset setup incomplete");
        viewModel.PresetReadinessDetail.Should().Contain("Enter a preset name");
        viewModel.IsPresetLibraryEmpty.Should().BeTrue();
        viewModel.PresetLibraryStateText.Should().Be("No export presets loaded yet.");
    }

    [Fact]
    public void SavePresetCommand_WithDraftName_AddsCustomPresetAndUpdatesLibraryState()
    {
        var viewModel = new ExportPresetsViewModel
        {
            DraftName = "Daily report pack",
            DraftFormat = "Parquet",
            DraftNotes = "Daily desk delivery"
        };

        viewModel.SavePresetCommand.CanExecute(null).Should().BeTrue();

        viewModel.SavePresetCommand.Execute(null);

        viewModel.Presets.Should().ContainSingle();
        viewModel.SelectedPreset.Should().NotBeNull();
        viewModel.SelectedPreset!.Name.Should().Be("Daily report pack");
        viewModel.SelectedPreset.Format.Should().Be("Parquet");
        viewModel.IsPresetLibraryEmpty.Should().BeFalse();
        viewModel.PresetLibraryStateText.Should().Be("1 preset available for reporting handoffs.");
        viewModel.StatusMessage.Should().Be("Preset \"Daily report pack\" added.");
        viewModel.DeletePresetCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void BuiltInPreset_DisablesDeleteAndSavingCreatesCustomPreset()
    {
        var viewModel = new ExportPresetsViewModel();
        var builtIn = new ExportPresetItem(
            "Lean handoff",
            "Lean",
            "Built-in Lean export",
            isBuiltIn: true,
            updatedAt: "Apr 28, 2026 09:00");

        viewModel.Presets.Add(builtIn);
        viewModel.SelectedPreset = builtIn;

        viewModel.CanDelete.Should().BeFalse();
        viewModel.DeletePresetCommand.CanExecute(null).Should().BeFalse();
        viewModel.PresetReadinessDetail.Should().Contain("built-in template");

        viewModel.DraftName = "Lean handoff custom";
        viewModel.SavePresetCommand.Execute(null);

        viewModel.Presets.Should().HaveCount(2);
        viewModel.SelectedPreset.Should().NotBeNull();
        viewModel.SelectedPreset!.IsBuiltIn.Should().BeFalse();
        viewModel.SelectedPreset.Name.Should().Be("Lean handoff custom");
        viewModel.DeletePresetCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ExportPresetsPageSource_BindsActionsAndReadinessThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ExportPresetsPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ExportPresetsPage.xaml.cs"));

        xaml.Should().Contain("ExportPresetReadinessCard");
        xaml.Should().Contain("{Binding PresetReadinessTitle}");
        xaml.Should().Contain("{Binding PresetReadinessDetail}");
        xaml.Should().Contain("{Binding PresetLibraryStateText}");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"ExportPresetSaveButton\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"ExportPresetDeleteButton\"");
        xaml.Should().Contain("Command=\"{Binding SavePresetCommand}\"");
        xaml.Should().Contain("Command=\"{Binding DeletePresetCommand}\"");
        xaml.Should().NotContain("Click=\"SavePreset_Click\"");
        xaml.Should().NotContain("Click=\"DeletePreset_Click\"");

        codeBehind.Should().NotContain("SavePreset_Click");
        codeBehind.Should().NotContain("DeletePreset_Click");
    }
}
