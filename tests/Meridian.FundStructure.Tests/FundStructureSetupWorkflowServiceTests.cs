using Meridian.Application.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Ui.Shared.Services;

namespace Meridian.FundStructure.Tests;

/// <summary>Guards the operator onboarding scenario where a new fund structure is created before broker handoff.</summary>
public sealed class FundStructureSetupWorkflowServiceTests
{
    [Fact]
    public async Task CreateAsync_ValidOperatorSetup_CreatesGraphAndAccountHandoffAssignment()
    {
        var service = CreateWorkflowService();
        var draft = CreateDraft();

        var result = await service.CreateAsync(draft);

        Assert.Equal("ORG", result.Organization.Code);
        Assert.Equal("FUND", result.Fund?.Code);
        Assert.Equal("PORT", result.InvestmentPortfolio.Code);
        Assert.Equal(FundStructureSetupWorkflowService.AccountHandoffAssignmentType, result.AccountHandoffAssignment.AssignmentType);
        Assert.Contains(result.Graph.Nodes, node => node.Kind == FundStructureNodeKindDto.InvestmentPortfolio && node.Code == "PORT");
    }

    [Fact]
    public void Validate_MissingRequiredFields_DisablesCreateWithBlockingIssues()
    {
        var service = CreateWorkflowService();
        var draft = CreateDraft() with
        {
            Organization = new FundStructureSetupOrganizationDraftDto(null, string.Empty, string.Empty, "US")
        };

        var validation = service.Validate(draft);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.FieldPath == "organization.code" && issue.IsBlocking);
        Assert.Contains(validation.Issues, issue => issue.Code == "currency.format" && issue.IsBlocking);
    }

    [Fact]
    public async Task CreateAsync_CancelledToken_PropagatesCancellationBeforeMutatingGraph()
    {
        var service = CreateWorkflowService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.CreateAsync(CreateDraft(), cts.Token));
    }

    internal static FundStructureSetupDraftDto CreateDraft()
        => new(
            new FundStructureSetupOrganizationDraftDto(null, "ORG", "Organization", "USD"),
            new FundStructureSetupBusinessDraftDto(null, BusinessKindDto.FundManager, "INV", "Investment Management", "USD"),
            new FundStructureSetupClientOrFundDraftDto(null, null, CreateClient: false, "FUND", "Flagship Fund", "USD"),
            new FundStructureSetupLegalEntityDraftDto(null, LegalEntityTypeDto.Fund, "LE", "Flagship LP", "US-DE", "USD"),
            new FundStructureSetupVehicleDraftDto(null, "VEH", "Flagship Vehicle", "USD"),
            new FundStructureSetupInvestmentPortfolioDraftDto(null, "PORT", "Core Portfolio", "USD"),
            new FundStructureSetupAccountHandoffDraftDto("ACCT", "Primary brokerage handoff", AccountTypeDto.Brokerage, "USD", "Broker", "4000"),
            InitialOwnershipLinks: Array.Empty<FundStructureSetupOwnershipLinkDraftDto>(),
            EffectiveFrom: new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero),
            RequestedBy: "test-operator");

    private static FundStructureSetupWorkflowService CreateWorkflowService()
    {
        var accountService = new InMemoryFundAccountService();
        var fundStructureService = new InMemoryFundStructureService(accountService);
        return new FundStructureSetupWorkflowService(fundStructureService);
    }
}
