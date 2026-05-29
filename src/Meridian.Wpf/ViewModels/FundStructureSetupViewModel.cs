using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.FundStructure;
using Meridian.Ui.Shared.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class FundStructureSetupViewModel : BindableBase
{
    private readonly FundStructureSetupWorkflowService _workflowService;
    private string _organizationCode = "ORG";
    private string _organizationName = "New Organization";
    private string _businessCode = "INV";
    private string _businessName = "Investment Management";
    private string _clientOrFundCode = "FUND";
    private string _clientOrFundName = "Flagship Fund";
    private bool _createClient;
    private string _legalEntityCode = "LE";
    private string _legalEntityName = "Flagship LP";
    private string _jurisdiction = "US-DE";
    private string _vehicleCode = "VEH";
    private string _vehicleName = "Flagship Vehicle";
    private string _portfolioCode = "PORT";
    private string _portfolioName = "Core Portfolio";
    private string _accountCode = "ACCT";
    private string _accountDisplayName = "Primary brokerage handoff";
    private string _institution = "Broker handoff";
    private string _ledgerReference = "4000-BROKER";
    private string _baseCurrency = "USD";
    private string _requestedBy = Environment.UserName;
    private string _statusText = "Validate the entity setup draft before creating the governed graph.";
    private bool _isBusy;
    private FundStructureSetupResultDto? _lastResult;

    public FundStructureSetupViewModel(FundStructureSetupWorkflowService workflowService)
    {
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
        ValidationIssues = new ObservableCollection<FundStructureSetupValidationIssueDto>();
        PreviewNodes = new ObservableCollection<FundStructureNodeDto>();
        PreviewLinks = new ObservableCollection<FundStructureSetupPreviewLinkDto>();
        ValidateDraftCommand = new RelayCommand(ValidateDraft, () => !IsBusy);
        CreateStructureCommand = new AsyncRelayCommand(CreateStructureAsync, CanCreateStructure);
        ValidateDraft();
    }

    public ObservableCollection<FundStructureSetupValidationIssueDto> ValidationIssues { get; }
    public ObservableCollection<FundStructureNodeDto> PreviewNodes { get; }
    public ObservableCollection<FundStructureSetupPreviewLinkDto> PreviewLinks { get; }
    public IRelayCommand ValidateDraftCommand { get; }
    public IAsyncRelayCommand CreateStructureCommand { get; }

    public string OrganizationCode { get => _organizationCode; set => SetDraftProperty(ref _organizationCode, value); }
    public string OrganizationName { get => _organizationName; set => SetDraftProperty(ref _organizationName, value); }
    public string BusinessCode { get => _businessCode; set => SetDraftProperty(ref _businessCode, value); }
    public string BusinessName { get => _businessName; set => SetDraftProperty(ref _businessName, value); }
    public string ClientOrFundCode { get => _clientOrFundCode; set => SetDraftProperty(ref _clientOrFundCode, value); }
    public string ClientOrFundName { get => _clientOrFundName; set => SetDraftProperty(ref _clientOrFundName, value); }
    public bool CreateClient { get => _createClient; set => SetDraftProperty(ref _createClient, value); }
    public string LegalEntityCode { get => _legalEntityCode; set => SetDraftProperty(ref _legalEntityCode, value); }
    public string LegalEntityName { get => _legalEntityName; set => SetDraftProperty(ref _legalEntityName, value); }
    public string Jurisdiction { get => _jurisdiction; set => SetDraftProperty(ref _jurisdiction, value); }
    public string VehicleCode { get => _vehicleCode; set => SetDraftProperty(ref _vehicleCode, value); }
    public string VehicleName { get => _vehicleName; set => SetDraftProperty(ref _vehicleName, value); }
    public string PortfolioCode { get => _portfolioCode; set => SetDraftProperty(ref _portfolioCode, value); }
    public string PortfolioName { get => _portfolioName; set => SetDraftProperty(ref _portfolioName, value); }
    public string AccountCode { get => _accountCode; set => SetDraftProperty(ref _accountCode, value); }
    public string AccountDisplayName { get => _accountDisplayName; set => SetDraftProperty(ref _accountDisplayName, value); }
    public string Institution { get => _institution; set => SetDraftProperty(ref _institution, value); }
    public string LedgerReference { get => _ledgerReference; set => SetDraftProperty(ref _ledgerReference, value); }
    public string BaseCurrency { get => _baseCurrency; set => SetDraftProperty(ref _baseCurrency, value); }
    public string RequestedBy { get => _requestedBy; set => SetDraftProperty(ref _requestedBy, value); }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ValidateDraftCommand.NotifyCanExecuteChanged();
                CreateStructureCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasBlockingIssues => ValidationIssues.Any(static issue => issue.IsBlocking);
    public bool HasResult => _lastResult is not null;
    public string ResultSummary => _lastResult is null ? "No setup created yet." : $"Created {_lastResult.BusinessLane.Name} / {_lastResult.InvestmentPortfolio.Name} with {_lastResult.Graph.Nodes.Count} graph nodes.";

    public FundStructureSetupDraftDto BuildDraft()
        => new(
            new FundStructureSetupOrganizationDraftDto(null, OrganizationCode, OrganizationName, BaseCurrency),
            new FundStructureSetupBusinessDraftDto(null, BusinessKindDto.FundManager, BusinessCode, BusinessName, BaseCurrency),
            new FundStructureSetupClientOrFundDraftDto(null, null, CreateClient, ClientOrFundCode, ClientOrFundName, BaseCurrency),
            new FundStructureSetupLegalEntityDraftDto(null, LegalEntityTypeDto.Fund, LegalEntityCode, LegalEntityName, Jurisdiction, BaseCurrency),
            new FundStructureSetupVehicleDraftDto(null, VehicleCode, VehicleName, BaseCurrency),
            new FundStructureSetupInvestmentPortfolioDraftDto(null, PortfolioCode, PortfolioName, BaseCurrency),
            new FundStructureSetupAccountHandoffDraftDto(AccountCode, AccountDisplayName, AccountTypeDto.Brokerage, BaseCurrency, Institution, LedgerReference),
            InitialOwnershipLinks: Array.Empty<FundStructureSetupOwnershipLinkDraftDto>(),
            EffectiveFrom: DateTimeOffset.UtcNow,
            RequestedBy: RequestedBy);

    private void ValidateDraft()
    {
        var preview = _workflowService.Preview(BuildDraft());
        Replace(ValidationIssues, preview.ValidationSummary.Issues);
        Replace(PreviewNodes, preview.Nodes);
        Replace(PreviewLinks, preview.OwnershipLinks);
        StatusText = preview.ValidationSummary.IsValid
            ? "Draft is valid. Review the graph preview, then create the structure."
            : $"Resolve {preview.ValidationSummary.Issues.Count(static issue => issue.IsBlocking)} blocking setup issue(s).";
        RaisePropertyChanged(nameof(HasBlockingIssues));
        CreateStructureCommand.NotifyCanExecuteChanged();
    }

    private async Task CreateStructureAsync()
    {
        IsBusy = true;
        try
        {
            ValidateDraft();
            if (HasBlockingIssues)
            {
                return;
            }

            _lastResult = await _workflowService.CreateAsync(BuildDraft()).ConfigureAwait(true);
            StatusText = "Entity setup created through the shared fund-structure workflow.";
            RaisePropertyChanged(nameof(HasResult));
            RaisePropertyChanged(nameof(ResultSummary));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreateStructure() => !IsBusy && !HasBlockingIssues;

    private void SetDraftProperty<T>(ref T field, T value)
    {
        if (SetProperty(ref field, value))
        {
            ValidateDraft();
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
