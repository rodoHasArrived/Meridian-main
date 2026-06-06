using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Ui.Shared.Services;
using Xunit;

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
        Assert.Equal(result.InvestmentPortfolio.InvestmentPortfolioId, result.AccountHandoffAssignment.NodeId);
        Assert.Contains(result.Graph.Nodes, node => node.Kind == FundStructureNodeKindDto.Fund && node.Code == "FUND");
        Assert.DoesNotContain(result.Graph.Nodes, node => node.Kind == FundStructureNodeKindDto.InvestmentPortfolio);
    }


    [Fact]
    public async Task CreateAsync_WithAuthenticatedActor_OverridesClientSuppliedRequestedBy()
    {
        var accountService = new InMemoryFundAccountService();
        var recordingService = new RecordingFundStructureService(new InMemoryFundStructureService(accountService));
        var service = new FundStructureSetupWorkflowService(recordingService);
        var draft = CreateDraft() with { RequestedBy = "spoofed-operator" };

        await service.CreateAsync(draft, "fund-ops", CancellationToken.None);

        Assert.NotEmpty(recordingService.CreatedByValues);
        Assert.All(recordingService.CreatedByValues, actor => Assert.Equal("fund-ops", actor));
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

    private sealed class RecordingFundStructureService(IFundStructureService inner) : IFundStructureService
    {
        private readonly List<string> _createdByValues = [];

        public IReadOnlyList<string> CreatedByValues => _createdByValues;

        public Task<OrganizationSummaryDto> CreateOrganizationAsync(CreateOrganizationRequest request, CancellationToken ct = default)
        {
            _createdByValues.Add(request.CreatedBy);
            return inner.CreateOrganizationAsync(request, ct);
        }

        public Task<BusinessSummaryDto> CreateBusinessAsync(CreateBusinessRequest request, CancellationToken ct = default)
        {
            _createdByValues.Add(request.CreatedBy);
            return inner.CreateBusinessAsync(request, ct);
        }

        public Task<ClientSummaryDto> CreateClientAsync(CreateClientRequest request, CancellationToken ct = default)
        {
            _createdByValues.Add(request.CreatedBy);
            return inner.CreateClientAsync(request, ct);
        }

        public Task<FundSummaryDto> CreateFundAsync(CreateFundRequest request, CancellationToken ct = default)
        {
            _createdByValues.Add(request.CreatedBy);
            return inner.CreateFundAsync(request, ct);
        }

        public Task<SleeveSummaryDto> CreateSleeveAsync(CreateSleeveRequest request, CancellationToken ct = default)
        {
            _createdByValues.Add(request.CreatedBy);
            return inner.CreateSleeveAsync(request, ct);
        }

        public Task<VehicleSummaryDto> CreateVehicleAsync(CreateVehicleRequest request, CancellationToken ct = default)
        {
            _createdByValues.Add(request.CreatedBy);
            return inner.CreateVehicleAsync(request, ct);
        }

        public Task<LegalEntitySummaryDto> CreateLegalEntityAsync(CreateLegalEntityRequest request, CancellationToken ct = default)
        {
            _createdByValues.Add(request.CreatedBy);
            return inner.CreateLegalEntityAsync(request, ct);
        }

        public Task<InvestmentPortfolioSummaryDto> CreateInvestmentPortfolioAsync(CreateInvestmentPortfolioRequest request, CancellationToken ct = default)
        {
            _createdByValues.Add(request.CreatedBy);
            return inner.CreateInvestmentPortfolioAsync(request, ct);
        }

        public Task<OwnershipLinkDto> LinkNodesAsync(LinkFundStructureNodesRequest request, CancellationToken ct = default)
        {
            _createdByValues.Add(request.CreatedBy);
            return inner.LinkNodesAsync(request, ct);
        }

        public Task<OwnershipLinkDto> UpdateOwnershipLinkAsync(UpdateOwnershipLinkRequest request, CancellationToken ct = default)
            => inner.UpdateOwnershipLinkAsync(request, ct);

        public Task<OwnershipLinkDto> ExpireOwnershipLinkAsync(ExpireOwnershipLinkRequest request, CancellationToken ct = default)
            => inner.ExpireOwnershipLinkAsync(request, ct);

        public Task<OwnershipLinkDto> ReplaceOwnershipLinkAsync(ReplaceOwnershipLinkRequest request, CancellationToken ct = default)
            => inner.ReplaceOwnershipLinkAsync(request, ct);

        public Task<OwnershipGraphValidationResultDto> ValidateOwnershipGraphAsync(ValidateOwnershipGraphRequest request, CancellationToken ct = default)
            => inner.ValidateOwnershipGraphAsync(request, ct);

        public Task<FundStructureAssignmentDto> AssignNodeAsync(AssignFundStructureNodeRequest request, CancellationToken ct = default)
        {
            _createdByValues.Add(request.CreatedBy);
            return inner.AssignNodeAsync(request, ct);
        }

        public Task<OrganizationStructureGraphDto> GetOrganizationStructureAsync(OrganizationStructureQuery query, CancellationToken ct = default)
            => inner.GetOrganizationStructureAsync(query, ct);

        public Task<FundStructureGraphDto> GetFundStructureGraphAsync(FundStructureQuery query, CancellationToken ct = default)
            => inner.GetFundStructureGraphAsync(query, ct);

        public Task<AdvisoryStructureViewDto?> GetAdvisoryViewAsync(AdvisoryStructureQuery query, CancellationToken ct = default)
            => inner.GetAdvisoryViewAsync(query, ct);

        public Task<FundOperatingViewDto?> GetFundOperatingViewAsync(FundOperatingStructureQuery query, CancellationToken ct = default)
            => inner.GetFundOperatingViewAsync(query, ct);

        public Task<AccountingStructureViewDto> GetAccountingViewAsync(AccountingStructureQuery query, CancellationToken ct = default)
            => inner.GetAccountingViewAsync(query, ct);

        public Task<GovernanceCashFlowViewDto?> GetCashFlowViewAsync(GovernanceCashFlowQuery query, CancellationToken ct = default)
            => inner.GetCashFlowViewAsync(query, ct);
    }

}
