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
        Assert.Equal(LegalEntityFormDto.LimitedPartnership, viewModel.BuildDraft().LegalEntity.LegalForm);
        Assert.Contains(viewModel.BuildDraft().LegalEntity.BeneficialOwners ?? [], owner => owner.OwnerName == "Flagship GP");
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

    [Fact]
    public void LegalEntityProfileChanges_UpdateDraftAndSummary()
    {
        var viewModel = CreateViewModel();

        viewModel.LegalEntityForm = LegalEntityFormDto.LimitedLiabilityCompany;
        viewModel.LegalEntityLifecycleStatus = LegalEntityLifecycleStatusDto.Restructuring;
        viewModel.RegistrationNumber = "DE-SPV-123";
        viewModel.BeneficialOwnerName = "Co-investor LLC";
        viewModel.BeneficialOwnerPercent = "25";
        viewModel.InitialLifecycleEventKind = LegalEntityLifecycleEventKindDto.SpvRollup;

        var draft = viewModel.BuildDraft();

        Assert.Equal(LegalEntityFormDto.LimitedLiabilityCompany, draft.LegalEntity.LegalForm);
        Assert.Equal(LegalEntityLifecycleStatusDto.Restructuring, draft.LegalEntity.LifecycleStatus);
        Assert.Equal("DE-SPV-123", draft.LegalEntity.RegistrationNumber);
        Assert.Contains(draft.LegalEntity.BeneficialOwners ?? [], owner => owner.OwnerName == "Co-investor LLC" && owner.OwnershipPercent == 25m);
        Assert.Equal(LegalEntityLifecycleEventKindDto.SpvRollup, draft.LegalEntity.InitialLifecycleEventKind);
        Assert.Contains("LimitedLiabilityCompany", viewModel.LegalEntityProfileSummary, StringComparison.Ordinal);
    }

    private static FundStructureSetupViewModel CreateViewModel()
    {
        var accountService = new InMemoryFundAccountService();
        var structureService = new InMemoryFundStructureService(accountService);
        return new FundStructureSetupViewModel(new FundStructureSetupWorkflowService(structureService));
    }
}
