using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using Meridian.Application.EnvironmentDesign;
using Meridian.Contracts.EnvironmentDesign;
using Meridian.Contracts.FundStructure;

namespace Meridian.Wpf.Views;

public partial class EnvironmentDesignerPage : Page, INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly IEnvironmentDesignService _designService;
    private readonly IEnvironmentValidationService _validationService;
    private readonly IEnvironmentPublishService _publishService;
    private bool _isLoaded;
    private string _statusText = "Load or create a draft to design a company umbrella, lanes, and supported operating scopes.";
    private string _draftJson = string.Empty;
    private string _draftSummaryText = "No draft selected.";
    private string _validationAndDiffText = "Validation and publish preview output will appear here.";
    private EnvironmentDraftDto? _selectedDraft;
    private PublishedEnvironmentVersionDto? _selectedVersion;

    public EnvironmentDesignerPage(
        IEnvironmentDesignService designService,
        IEnvironmentValidationService validationService,
        IEnvironmentPublishService publishService)
    {
        _designService = designService ?? throw new ArgumentNullException(nameof(designService));
        _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        _publishService = publishService ?? throw new ArgumentNullException(nameof(publishService));
        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<EnvironmentDraftDto> Drafts { get; } = [];

    public ObservableCollection<PublishedEnvironmentVersionDto> Versions { get; } = [];

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string DraftJson
    {
        get => _draftJson;
        set => SetProperty(ref _draftJson, value);
    }

    public string DraftSummaryText
    {
        get => _draftSummaryText;
        private set => SetProperty(ref _draftSummaryText, value);
    }

    public string ValidationAndDiffText
    {
        get => _validationAndDiffText;
        private set => SetProperty(ref _validationAndDiffText, value);
    }

    public EnvironmentDraftDto? SelectedDraft
    {
        get => _selectedDraft;
        set => SetProperty(ref _selectedDraft, value);
    }

    public PublishedEnvironmentVersionDto? SelectedVersion
    {
        get => _selectedVersion;
        set => SetProperty(ref _selectedVersion, value);
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        await RefreshAsync().ConfigureAwait(true);
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
        => await RefreshAsync().ConfigureAwait(true);

    private async void OnCreateBlankDraftClick(object sender, RoutedEventArgs e)
        => await CreateDraftAsync(BuildBlankDraftRequest()).ConfigureAwait(true);

    private async void OnCreateAdvisoryStarterClick(object sender, RoutedEventArgs e)
        => await CreateDraftAsync(BuildAdvisoryStarterDraftRequest()).ConfigureAwait(true);

    private async void OnSaveDraftClick(object sender, RoutedEventArgs e)
    {
        var saved = await SaveEditorDraftAsync().ConfigureAwait(true);
        if (saved is not null)
        {
            ValidationAndDiffText = "Draft saved.";
        }
    }

    private async void OnValidateDraftClick(object sender, RoutedEventArgs e)
    {
        var draft = await SaveEditorDraftAsync().ConfigureAwait(true);
        if (draft is null)
        {
            return;
        }

        var validation = await _validationService.ValidateAsync(draft).ConfigureAwait(true);
        ValidationAndDiffText = FormatValidation(validation);
        StatusText = validation.IsValid
            ? $"Draft '{draft.Name}' is valid."
            : $"Draft '{draft.Name}' has validation errors.";
    }

    private async void OnPreviewPublishClick(object sender, RoutedEventArgs e)
    {
        var draft = await SaveEditorDraftAsync().ConfigureAwait(true);
        if (draft is null)
        {
            return;
        }

        var preview = await _publishService
            .PreviewPublishAsync(CreatePublishPlan(draft.DraftId))
            .ConfigureAwait(true);
        ValidationAndDiffText = $"{FormatValidation(preview.Validation)}{Environment.NewLine}{Environment.NewLine}{FormatPublishChanges(preview.Changes)}";
        StatusText = preview.HasDestructiveChanges
            ? "Preview contains destructive changes that require remaps or approval."
            : $"Preview built for '{draft.Name}'.";
    }

    private async void OnPublishClick(object sender, RoutedEventArgs e)
    {
        var draft = await SaveEditorDraftAsync().ConfigureAwait(true);
        if (draft is null)
        {
            return;
        }

        try
        {
            var version = await _publishService
                .PublishAsync(CreatePublishPlan(draft.DraftId))
                .ConfigureAwait(true);
            StatusText = $"Published {version.VersionLabel} for {version.OrganizationName}.";
            ValidationAndDiffText = $"Published version {version.VersionLabel} at {version.PublishedAt:u}.";
            await RefreshAsync(version.DraftId, version.VersionId).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            StatusText = "Publish blocked by validation.";
            ValidationAndDiffText = ex.Message;
        }
    }

    private async void OnRollbackClick(object sender, RoutedEventArgs e)
    {
        if (SelectedVersion is null)
        {
            StatusText = "Select a published version before rolling back.";
            return;
        }

        var rolledBack = await _publishService.RollbackAsync(
            new RollbackEnvironmentVersionRequest(
                SelectedVersion.VersionId,
                Environment.UserName,
                "Rollback requested from the WPF environment designer.")).ConfigureAwait(true);

        StatusText = $"Rolled back to {rolledBack.VersionLabel}.";
        ValidationAndDiffText = $"Current version reset to {rolledBack.VersionLabel} ({rolledBack.OrganizationName}).";
        await RefreshAsync(SelectedDraft?.DraftId, rolledBack.VersionId).ConfigureAwait(true);
    }

    private void OnDraftSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedDraft is null)
        {
            return;
        }

        DraftJson = JsonSerializer.Serialize(SelectedDraft, JsonOptions);
        DraftSummaryText = BuildDraftSummary(SelectedDraft);
        StatusText = $"Editing draft '{SelectedDraft.Name}'.";
    }

    private void OnVersionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedVersion is null)
        {
            return;
        }

        ValidationAndDiffText = $"Version: {SelectedVersion.VersionLabel}{Environment.NewLine}" +
                                $"Organization: {SelectedVersion.OrganizationName}{Environment.NewLine}" +
                                $"Published: {SelectedVersion.PublishedAt:u}{Environment.NewLine}" +
                                $"Current: {SelectedVersion.IsCurrent}{Environment.NewLine}" +
                                $"{Environment.NewLine}" +
                                FormatValidation(SelectedVersion.Validation);
    }

    private async Task RefreshAsync(Guid? preferredDraftId = null, Guid? preferredVersionId = null)
    {
        var drafts = await _designService.ListDraftsAsync().ConfigureAwait(true);
        var versions = await _designService.ListPublishedVersionsAsync().ConfigureAwait(true);

        ReplaceCollection(Drafts, drafts);
        ReplaceCollection(Versions, versions);

        SelectedDraft = Drafts.FirstOrDefault(draft => draft.DraftId == preferredDraftId)
            ?? Drafts.FirstOrDefault();
        SelectedVersion = Versions.FirstOrDefault(version => version.VersionId == preferredVersionId)
            ?? Versions.FirstOrDefault(static version => version.IsCurrent)
            ?? Versions.FirstOrDefault();

        if (SelectedDraft is not null)
        {
            DraftJson = JsonSerializer.Serialize(SelectedDraft, JsonOptions);
            DraftSummaryText = BuildDraftSummary(SelectedDraft);
        }
        else
        {
            DraftJson = string.Empty;
            DraftSummaryText = "No draft selected.";
        }

        StatusText = $"Loaded {Drafts.Count} draft(s) and {Versions.Count} published version(s).";
    }

    private async Task CreateDraftAsync(CreateEnvironmentDraftRequest request)
    {
        var created = await _designService.CreateDraftAsync(request).ConfigureAwait(true);
        await RefreshAsync(created.DraftId).ConfigureAwait(true);
        ValidationAndDiffText = $"Created draft '{created.Name}'.";
    }

    private async Task<EnvironmentDraftDto?> SaveEditorDraftAsync()
    {
        if (!TryReadDraftFromEditor(out var draft, out var error))
        {
            StatusText = "Draft JSON could not be parsed.";
            ValidationAndDiffText = error ?? "Draft JSON is invalid.";
            return null;
        }

        var saved = await _designService.SaveDraftAsync(
            draft with
            {
                UpdatedBy = Environment.UserName,
                Definition = draft.Definition with
                {
                    OrganizationName = string.IsNullOrWhiteSpace(draft.Definition.OrganizationName)
                        ? "Untitled Organization"
                        : draft.Definition.OrganizationName.Trim()
                }
            }).ConfigureAwait(true);

        await RefreshAsync(saved.DraftId, SelectedVersion?.VersionId).ConfigureAwait(true);
        return saved;
    }

    private bool TryReadDraftFromEditor(out EnvironmentDraftDto draft, out string? error)
    {
        draft = default!;
        error = null;

        try
        {
            draft = JsonSerializer.Deserialize<EnvironmentDraftDto>(DraftJson, JsonOptions)
                ?? throw new JsonException("Editor content did not deserialize into an environment draft.");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static CreateEnvironmentDraftRequest BuildBlankDraftRequest()
    {
        var organizationId = Guid.NewGuid();
        return new CreateEnvironmentDraftRequest(
            DraftId: Guid.NewGuid(),
            Name: "Blank Company Umbrella",
            CreatedBy: Environment.UserName,
            Definition: new OrganizationEnvironmentDefinitionDto(
                OrganizationId: organizationId,
                OrganizationNodeDefinitionId: "org-root",
                OrganizationCode: "ORG",
                OrganizationName: "New Meridian Company",
                BaseCurrency: "USD",
                Lanes: [],
                Nodes:
                [
                    new EnvironmentNodeDefinitionDto(
                        "org-root",
                        EnvironmentNodeKind.Organization,
                        "ORG",
                        "New Meridian Company",
                        "USD",
                        Description: "Top-level company umbrella for environment design.")
                ],
                Relationships: []),
            Notes: "Blank company umbrella draft created from the WPF environment designer.");
    }

    private static CreateEnvironmentDraftRequest BuildAdvisoryStarterDraftRequest()
    {
        var organizationId = Guid.NewGuid();
        return new CreateEnvironmentDraftRequest(
            DraftId: Guid.NewGuid(),
            Name: "Advisory Practice Starter",
            CreatedBy: Environment.UserName,
            Definition: new OrganizationEnvironmentDefinitionDto(
                OrganizationId: organizationId,
                OrganizationNodeDefinitionId: "org-root",
                OrganizationCode: "ADV-ORG",
                OrganizationName: "Northwind Advisory Group",
                BaseCurrency: "USD",
                Lanes:
                [
                    new EnvironmentLaneDefinitionDto(
                        LaneId: "advisory-lane",
                        Name: "Advisory Practice",
                        Archetype: EnvironmentLaneArchetype.AdvisoryPractice,
                        DefaultWorkspaceId: "governance",
                        DefaultLandingPageTag: "GovernanceShell",
                        RootNodeDefinitionId: "advisory-business",
                        DefaultContextNodeDefinitionId: "advisory-business",
                        AllowedManagedScopeKinds:
                        [
                            EnvironmentManagedScopeKind.IndividualInvestor,
                            EnvironmentManagedScopeKind.FamilyOffice,
                            EnvironmentManagedScopeKind.Fund
                        ],
                        Description: "Hybrid advisory umbrella serving individuals, family office structures, and funds.")
                ],
                Nodes:
                [
                    new EnvironmentNodeDefinitionDto("org-root", EnvironmentNodeKind.Organization, "ADV-ORG", "Northwind Advisory Group", "USD"),
                    new EnvironmentNodeDefinitionDto("advisory-business", EnvironmentNodeKind.Business, "ADV-HYB", "Advisory Operating Shell", "USD", ParentNodeDefinitionId: "org-root", ParentRelationshipType: OwnershipRelationshipTypeDto.Owns, BusinessKind: BusinessKindDto.Hybrid, LaneId: "advisory-lane"),
                    new EnvironmentNodeDefinitionDto("investor-client", EnvironmentNodeKind.Client, "CLI-IND-001", "Individual Investor Mandate", "USD", ParentNodeDefinitionId: "advisory-business", ParentRelationshipType: OwnershipRelationshipTypeDto.Advises, ClientSegmentKind: ClientSegmentKind.IndividualInvestor, LaneId: "advisory-lane"),
                    new EnvironmentNodeDefinitionDto("family-office-client", EnvironmentNodeKind.Client, "CLI-FO-001", "Canyon Family Office", "USD", ParentNodeDefinitionId: "advisory-business", ParentRelationshipType: OwnershipRelationshipTypeDto.Advises, ClientSegmentKind: ClientSegmentKind.FamilyOffice, LaneId: "advisory-lane"),
                    new EnvironmentNodeDefinitionDto("flagship-fund", EnvironmentNodeKind.Fund, "FUND-001", "Northwind Flagship Fund", "USD", ParentNodeDefinitionId: "advisory-business", ParentRelationshipType: OwnershipRelationshipTypeDto.Operates, LaneId: "advisory-lane"),
                    new EnvironmentNodeDefinitionDto("fo-portfolio", EnvironmentNodeKind.InvestmentPortfolio, "PORT-FO-001", "Family Office Oversight", "USD", ParentNodeDefinitionId: "family-office-client", ParentRelationshipType: OwnershipRelationshipTypeDto.Owns, LaneId: "advisory-lane"),
                    new EnvironmentNodeDefinitionDto("fund-account", EnvironmentNodeKind.Account, "ACCT-FUND-001", "Prime Brokerage Main", "USD", ParentNodeDefinitionId: "flagship-fund", ParentRelationshipType: OwnershipRelationshipTypeDto.CustodiesFor, LaneId: "advisory-lane", AccountType: AccountTypeDto.PrimeBroker, Institution: "Northwind Prime", LedgerReference: "FUND-TB")
                ],
                Relationships: []),
            Notes: "Hybrid advisory starter draft created from the WPF environment designer.");
    }

    private static string BuildDraftSummary(EnvironmentDraftDto draft)
    {
        var nodeGroups = draft.Definition.Nodes
            .GroupBy(static node => node.NodeKind)
            .OrderBy(static group => group.Key)
            .Select(group => $"{group.Key}: {group.Count()}")
            .ToArray();
        var laneLines = draft.Definition.Lanes.Select(lane =>
            $"{lane.Name} [{lane.Archetype}] -> {lane.DefaultWorkspaceId} / {lane.DefaultLandingPageTag}").ToArray();

        return $"Organization: {draft.Definition.OrganizationName}{Environment.NewLine}" +
               $"Base currency: {draft.Definition.BaseCurrency}{Environment.NewLine}" +
               $"Nodes: {draft.Definition.Nodes.Count}{Environment.NewLine}" +
               $"Relationships: {draft.Definition.Relationships.Count}{Environment.NewLine}" +
               $"Lanes: {draft.Definition.Lanes.Count}{Environment.NewLine}" +
               $"{string.Join(Environment.NewLine, laneLines.DefaultIfEmpty("No lanes configured."))}{Environment.NewLine}{Environment.NewLine}" +
               $"{string.Join(Environment.NewLine, nodeGroups.DefaultIfEmpty("No nodes configured."))}";
    }

    private static string FormatValidation(EnvironmentValidationResultDto validation)
    {
        if (validation.Issues.Count == 0)
        {
            return "Validation passed with no issues.";
        }

        return string.Join(
            Environment.NewLine,
            validation.Issues.Select(issue =>
                $"[{issue.Severity}] {issue.Code}: {issue.Message}"));
    }

    private static string FormatPublishChanges(IReadOnlyList<EnvironmentPublishChangeDto> changes)
    {
        if (changes.Count == 0)
        {
            return "No publish changes detected.";
        }

        return string.Join(
            Environment.NewLine,
            changes.Select(change =>
                $"{change.ChangeType}: {change.Summary}{(change.IsBreaking ? " [breaking]" : string.Empty)}"));
    }

    private static EnvironmentPublishPlanDto CreatePublishPlan(Guid draftId)
        => new(
            DraftId: draftId,
            PublishedBy: Environment.UserName,
            AllowDestructiveDeletes: false,
            VersionLabel: null,
            Notes: "Published from the WPF environment designer.",
            NodeRemaps: null);

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
