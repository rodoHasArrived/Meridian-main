using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class FundStructureSetupViewModelTests
{
    [Fact]
    public void Constructor_LoadsValidPreviewAndEnablesCreateCommand()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.HasBlockingIssues);
        Assert.True(viewModel.CreateStructureCommand.CanExecute(null));
        Assert.Contains(viewModel.PreviewNodes, node => node.Kind == FundStructureNodeKindDto.InvestmentPortfolio);
    }

    [Fact]
    public void RequiredFieldChange_UpdatesValidationAndDisablesCreateCommand()
    {
        var viewModel = CreateViewModel();

        viewModel.OrganizationCode = string.Empty;

        Assert.True(viewModel.HasBlockingIssues);
        Assert.False(viewModel.CreateStructureCommand.CanExecute(null));
        Assert.Contains(viewModel.ValidationIssues, issue => issue.FieldPath == "organization.code");
    }

    [Fact]
    public async Task CreateStructureCommand_ValidDraft_CreatesResultSummary()
    {
        var viewModel = CreateViewModel();

        await viewModel.CreateStructureCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasResult);
        Assert.Contains("Core Portfolio", viewModel.ResultSummary, StringComparison.Ordinal);
    }

    private static FundStructureSetupViewModel CreateViewModel()
    {
        var accountService = new InMemoryFundAccountService();
        var structureService = new InMemoryFundStructureService(accountService);
        return new FundStructureSetupViewModel(new FundStructureSetupWorkflowService(structureService));
    }
}
