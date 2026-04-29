using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class DataSourcesViewModelTests
{
    [Fact]
    public void BuildEditReadinessState_WithoutName_DisablesSaveAndExplainsRecovery()
    {
        var state = DataSourcesViewModel.BuildEditReadinessState(
            sourceName: "",
            priorityText: "100",
            provider: "IB",
            type: "RealTime",
            symbolsText: "");

        state.CanSave.Should().BeFalse();
        state.Title.Should().Be("Data source setup incomplete");
        state.Detail.Should().Contain("Name the source");
        state.ScopeText.Should().Be("Interactive Brokers - Real-time - Global symbol list");
        state.SourceNameError.Should().Be("Name is required.");
        state.IsSourceNameErrorVisible.Should().BeTrue();
        state.IsPriorityErrorVisible.Should().BeFalse();
    }

    [Fact]
    public void BuildEditReadinessState_WithInvalidPriority_DisablesSaveAndShowsPriorityValidation()
    {
        var state = DataSourcesViewModel.BuildEditReadinessState(
            sourceName: "Polygon Stocks",
            priorityText: "0",
            provider: "Polygon",
            type: "Historical",
            symbolsText: "spy, qqq");

        state.CanSave.Should().BeFalse();
        state.Detail.Should().Contain("Set a priority between 1 and 1000");
        state.PriorityError.Should().Be("Priority must be between 1 and 1000.");
        state.IsPriorityErrorVisible.Should().BeTrue();
        state.ScopeText.Should().Be("Polygon.io - Historical - 2 symbols: SPY, QQQ");
    }

    [Fact]
    public void BuildEditReadinessState_WithValidScopedSource_EnablesSaveAndNormalizesScope()
    {
        var state = DataSourcesViewModel.BuildEditReadinessState(
            sourceName: "Alpaca Paper",
            priorityText: "25",
            provider: "Alpaca",
            type: "Both",
            symbolsText: " spy, SPY, msft ");

        state.CanSave.Should().BeTrue();
        state.Title.Should().Be("Data source ready");
        state.Detail.Should().Contain("Alpaca real-time and historical source is ready to save");
        state.ScopeText.Should().Be("Alpaca - Real-time and historical - 2 symbols: SPY, MSFT");
        state.IsSourceNameErrorVisible.Should().BeFalse();
        state.IsPriorityErrorVisible.Should().BeFalse();
    }

    [Fact]
    public void DataSourcesPageSource_BindsEditReadinessAndProviderSetupThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataSourcesPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataSourcesPage.xaml.cs"));

        xaml.Should().Contain("DataSourcesEditReadinessCard");
        xaml.Should().Contain("{Binding SourceSetupReadinessTitle}");
        xaml.Should().Contain("{Binding SourceSetupReadinessDetail}");
        xaml.Should().Contain("{Binding SourceSetupScopeText}");
        xaml.Should().Contain("SelectedValue=\"{Binding SelectedProvider, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"");
        xaml.Should().Contain("SelectedValue=\"{Binding SelectedType, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"");
        xaml.Should().Contain("SelectedValue=\"{Binding AlpacaFeed, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"");
        xaml.Should().Contain("SelectedValue=\"{Binding AlpacaEnvironmentTag, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"");
        xaml.Should().Contain("SelectedValue=\"{Binding PolygonFeed, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"");
        xaml.Should().Contain("IsChecked=\"{Binding IBPaperTrading, Mode=TwoWay}\"");
        xaml.Should().Contain("IsChecked=\"{Binding AlpacaSubscribeQuotes, Mode=TwoWay}\"");
        xaml.Should().Contain("IsChecked=\"{Binding PolygonSubscribeTrades, Mode=TwoWay}\"");
        xaml.Should().Contain("Text=\"{Binding SymbolsText, UpdateSourceTrigger=PropertyChanged}\"");
        xaml.Should().Contain("Command=\"{Binding DataContext.EditSourceCommand, RelativeSource={RelativeSource AncestorType=Page}}\"");
        xaml.Should().Contain("Command=\"{Binding DataContext.DeleteSourceCommand, RelativeSource={RelativeSource AncestorType=Page}}\"");
        xaml.Should().Contain("Command=\"{Binding DataContext.ToggleSourceEnabledCommand, RelativeSource={RelativeSource AncestorType=Page}}\"");
        xaml.Should().Contain("IsEnabled=\"{Binding CanSaveSource}\"");
        xaml.Should().NotContain("SelectionChanged=\"ProviderCombo_SelectionChanged\"");
        xaml.Should().NotContain("Click=\"EditDataSource_Click\"");
        xaml.Should().NotContain("Click=\"DeleteDataSource_Click\"");
        xaml.Should().NotContain("SourceEnabled_Changed");

        codeBehind.Should().NotContain("ProviderCombo_SelectionChanged");
        codeBehind.Should().NotContain("EditDataSource_Click");
        codeBehind.Should().NotContain("DeleteDataSource_Click");
        codeBehind.Should().NotContain("SourceEnabled_Changed");
        codeBehind.Should().Contain("PasswordBox.Password cannot be data-bound");
    }
}
