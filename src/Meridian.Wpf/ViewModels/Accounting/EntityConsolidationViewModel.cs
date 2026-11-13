using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Wpf.ViewModels.Accounting;

/// <summary>Desktop inspection over the shared read model. All financial amounts come from the host.</summary>
public sealed class EntityConsolidationViewModel : BindableBase
{
    private readonly IWorkstationEntityConsolidationApiClient? _client;
    private OrganizationStructureGraphDto? _structure;
    private CancellationTokenSource? _inFlight;
    private long _generation;
    private bool _isBusy;
    private string _status = "Load authorized organizations to inspect entities and prepare consolidation drafts.";
    private OrganizationSummaryDto? _organization;
    private LegalEntitySummaryDto? _entity;
    private FundSummaryDto? _root;
    private LedgerBookDto? _book;
    private LedgerPeriodDto? _period;
    private DateTime? _asOf = DateTime.UtcNow.Date;
    private ConsolidationViewDto? _preview;
    private EntityTimelineDto? _timeline;
    private string _scopeDetail = "Gross, proposed, posted, actual and preview balances are separate server projections.";
    private string _entityDetail = "Choose an entity and an as-of date to load retained history.";
    private ConsolidationSourceDto? _selectedSource;

    public EntityConsolidationViewModel(IWorkstationEntityConsolidationApiClient? client)
    {
        _client = client;
        LoadChoicesCommand = new AsyncRelayCommand(LoadChoicesAsync, () => _client is not null && !IsBusy);
        LoadTimelineCommand = new AsyncRelayCommand(LoadTimelineAsync, () => _client is not null && !IsBusy && SelectedOrganization is not null && SelectedEntity is not null && AsOf.HasValue);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => _client is not null && !IsBusy && Request is not null);
        CreateDraftsCommand = new AsyncRelayCommand(CreateDraftsAsync, () => !IsBusy && _preview is { CanCreateDrafts: true } && _preview.Blockers.Count == 0 && _preview.Request == Request);
        if (client is null) _status = "Entity and consolidation inspection is unavailable: the workstation API client is not registered.";
    }

    public IAsyncRelayCommand LoadChoicesCommand { get; }
    public IAsyncRelayCommand LoadTimelineCommand { get; }
    public IAsyncRelayCommand PreviewCommand { get; }
    public IAsyncRelayCommand CreateDraftsCommand { get; }
    public ObservableCollection<OrganizationSummaryDto> Organizations { get; } = [];
    public ObservableCollection<LegalEntitySummaryDto> Entities { get; } = [];
    public ObservableCollection<FundSummaryDto> OwnershipRoots { get; } = [];
    public ObservableCollection<LedgerBookDto> EliminationBooks { get; } = [];
    public ObservableCollection<LedgerPeriodDto> Periods { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> TimelineRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> OwnershipRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> AgreementRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> BookRows { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<ConsolidationBalanceDto> Balances { get; } = [];
    public ObservableCollection<ConsolidationMatchDto> Matches { get; } = [];
    public ObservableCollection<ConsolidationDraftSummaryDto> Drafts { get; } = [];
    public ObservableCollection<ConsolidationSourceDto> Sources { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> TranslationReserveRows { get; } = [];
    public ObservableCollection<string> Blockers { get; } = [];
    public ConsolidationViewDto? Preview => _preview;
    public bool HasDrafts => Drafts.Count > 0;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ScopeDetail { get => _scopeDetail; private set => SetProperty(ref _scopeDetail, value); }
    public string EntityDetail { get => _entityDetail; private set => SetProperty(ref _entityDetail, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) NotifyCommands(); } }
    public OrganizationSummaryDto? SelectedOrganization
    {
        get => _organization;
        set
        {
            if (!SetProperty(ref _organization, value)) return;
            Invalidate();
            SelectedEntity = null;
            SelectedOwnershipRoot = null;
            var businessIds = _structure?.Businesses.Where(item => item.OrganizationId == value?.OrganizationId).Select(item => item.BusinessId).ToHashSet() ?? [];
            Replace(OwnershipRoots, _structure?.Funds.Where(item => item.BusinessId.HasValue && businessIds.Contains(item.BusinessId.Value)) ?? []);
            var reachable = new HashSet<Guid>();
            if (value is not null) reachable.Add(value.OrganizationId);
            if (_structure is not null)
            {
                bool changed;
                do
                {
                    changed = false;
                    foreach (var link in _structure.OwnershipLinks)
                        if (reachable.Contains(link.ParentNodeId)) changed |= reachable.Add(link.ChildNodeId);
                } while (changed);
            }
            Replace(Entities, _structure?.Entities.Where(item => reachable.Contains(item.EntityId)) ?? []);
        }
    }
    public LegalEntitySummaryDto? SelectedEntity { get => _entity; set { if (SetProperty(ref _entity, value)) Invalidate(); } }
    public FundSummaryDto? SelectedOwnershipRoot { get => _root; set { if (SetProperty(ref _root, value)) Invalidate(); } }
    public LedgerBookDto? SelectedEliminationBook
    {
        get => _book;
        set
        {
            if (!SetProperty(ref _book, value)) return;
            Invalidate();
            SelectedPeriod = null;
            Periods.Clear();
            if (value is not null && _client is not null) _ = LoadPeriodsAsync(value.LedgerBookId);
        }
    }
    public LedgerPeriodDto? SelectedPeriod { get => _period; set { if (SetProperty(ref _period, value)) Invalidate(); } }
    public DateTime? AsOf { get => _asOf; set { if (SetProperty(ref _asOf, value)) Invalidate(); } }
    public ConsolidationSourceDto? SelectedSource
    {
        get => _selectedSource;
        set { if (SetProperty(ref _selectedSource, value)) RaisePropertyChanged(nameof(SourceDetail)); }
    }
    public string SourceDetail => SelectedSource is { } source
        ? $"Book {source.LedgerBookId:D} · journal {source.JournalEntryId:D} · line {source.LineId:D}\nEntity {source.EntityId} · counterparty {source.CounterpartyId ?? "unavailable"}\nAccounting profile {source.AccountingArrangementId ?? "unavailable"} / revision {source.AccountingArrangementRevisionId ?? "unavailable"}\nLegal agreement {source.IntercompanyAgreementId} / version {source.IntercompanyAgreementVersion}\nLegal hash {source.IntercompanyAgreementPayloadHash ?? "unavailable"}\nAccount binding {source.AccountBindingRevisionId ?? "unavailable"}\n{source.DrillThrough}"
        : "Select a source row to inspect retained journal, agreement and account-binding identities.";

    private ConsolidationRequestDto? Request => SelectedOrganization is { } organization && SelectedOwnershipRoot is { } root
        && SelectedEliminationBook is { } book && SelectedPeriod is { } period && AsOf is { } date
        ? new(organization.OrganizationId, root.FundId, book.LedgerBookId, period.PeriodId, DateOnly.FromDateTime(date)) : null;

    public void SetBooks(IEnumerable<LedgerBookDto> books)
    {
        var selected = SelectedEliminationBook?.LedgerBookId;
        Invalidate();
        Replace(EliminationBooks, books.Where(book => book.FundStructureNodeKind == FundStructureNodeKindDto.Fund && book.AccountingBasis == AccountingBasisKindDto.Primary));
        SelectedEliminationBook = EliminationBooks.FirstOrDefault(book => book.LedgerBookId == selected);
    }

    public void Cancel()
    {
        ++_generation;
        _inFlight?.Cancel();
        IsBusy = false;
    }

    public void ReportReviewFailure(string message) => Status = $"Draft review is unavailable: {message}";

    private void Invalidate()
    {
        Cancel();
        _preview = null;
        _timeline = null;
        Balances.Clear(); Matches.Clear(); Drafts.Clear(); Sources.Clear(); TranslationReserveRows.Clear(); Blockers.Clear();
        TimelineRows.Clear(); OwnershipRows.Clear(); AgreementRows.Clear(); BookRows.Clear(); Warnings.Clear();
        SelectedSource = null;
        ScopeDetail = "Scope changed. Preview again to inspect current server evidence.";
        EntityDetail = "Scope changed. Load the selected entity timeline again.";
        Status = _client is null ? "Entity and consolidation inspection is unavailable: the workstation API client is not registered."
            : "Scope changed. Load evidence for the selected scope.";
        RaisePropertyChanged(nameof(Preview));
        RaisePropertyChanged(nameof(HasDrafts));
        NotifyCommands();
    }

    public Task LoadChoicesAsync()
    {
        Invalidate();
        return RunAsync(async ct =>
        {
            var structure = await _client!.GetStructureAsync(ct);
            if (ct.IsCancellationRequested) return;
            _structure = structure;
            Replace(Organizations, _structure.Organizations);
            Status = Organizations.Count == 0 ? "No organizations are available in this authenticated scope." : "Choose an organization, entity and consolidation scope.";
        });
    }

    private Task LoadPeriodsAsync(Guid bookId) => RunAsync(async ct =>
    {
        var periods = await _client!.GetPeriodsAsync(bookId, ct);
        if (ct.IsCancellationRequested || SelectedEliminationBook?.LedgerBookId != bookId) return;
        Replace(Periods, periods.OrderByDescending(item => item.StartDate));
        Status = Periods.Count == 0 ? "No periods are available for the selected elimination book." : "Select the elimination period and preview the consolidation.";
    });

    public Task LoadTimelineAsync()
    {
        if (SelectedOrganization is not { } organization || SelectedEntity is not { } entity || AsOf is not { } date) return Task.CompletedTask;
        _timeline = null;
        TimelineRows.Clear(); OwnershipRows.Clear(); AgreementRows.Clear(); BookRows.Clear(); Warnings.Clear();
        return RunAsync(async ct =>
        {
            var timeline = await _client!.GetTimelineAsync(organization.OrganizationId, entity.EntityId,
                new DateTimeOffset(DateTime.SpecifyKind(date.Date, DateTimeKind.Utc)), ct);
            if (ct.IsCancellationRequested) return;
            _timeline = timeline;
            EntityDetail = $"{timeline.Entity.Name} · {timeline.Entity.LifecycleStatus} · {timeline.Entity.BaseCurrency} · as of {timeline.AsOf:yyyy-MM-dd HH:mm} UTC";
            Replace(Warnings, timeline.Warnings);
            Replace(TimelineRows, timeline.LifecycleRevisions.OrderByDescending(item => item.Revision).SelectMany(revision => revision.Timeline.Select(state =>
                new AccountingWorkbenchRow($"Revision {revision.Revision} · {state.Status}", state.EventKind.ToString(),
                    $"[{state.EffectiveFrom:O}, {state.EffectiveTo?.ToString("O", CultureInfo.InvariantCulture) ?? "open"})",
                    $"{revision.RecordedBy} · {revision.Reason} · {revision.EvidenceReference} · hash {revision.ContentHash}"))));
            foreach (var item in timeline.Entity.LifecycleEvents ?? [])
                TimelineRows.Add(new($"Legal event · {item.EventKind}", "Recorded event", item.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
                    $"{item.RecordedBy} · {item.Summary} · {item.EvidenceReference ?? "Evidence unavailable"}"));
            Replace(OwnershipRows, timeline.OwnershipLinks.Select(link => new AccountingWorkbenchRow(link.RelationshipType.ToString(),
                link.OwnershipPercent?.ToString("G29", CultureInfo.InvariantCulture) ?? "Unspecified", $"{link.ParentNodeId:D} → {link.ChildNodeId:D}",
                $"[{link.EffectiveFrom:O}, {link.EffectiveTo?.ToString("O", CultureInfo.InvariantCulture) ?? "open"}) · {link.OwnershipLinkId:D}")));
            Replace(AgreementRows, timeline.Agreements.Select(agreement => new AccountingWorkbenchRow(
                "Legal agreement · " + agreement.AgreementType, agreement.EffectiveStatus.ToString(),
                $"{agreement.Code} · {agreement.Currency}",
                $"{agreement.AgreementId:D} · applicable version {agreement.ApplicableRevision?.AgreementVersion} · hash {agreement.ApplicableRevision?.PayloadHash ?? "unavailable"}"))
                .Concat(timeline.AccountingArrangements.Select(profile => new AccountingWorkbenchRow(
                    "Accounting profile · " + profile.TemplateKind, profile.Status.ToString(),
                    $"{profile.ArrangementReference} · {profile.Currency}",
                    $"{profile.ArrangementId:D} · revision {profile.RevisionId:D} · legal {profile.CanonicalAgreementId:D}/{profile.CanonicalAgreementVersion} · hash {profile.CanonicalPayloadHash} · [{profile.EffectiveFrom:O}, {profile.EffectiveTo?.ToString("O", CultureInfo.InvariantCulture) ?? "open"})"))));
            Replace(BookRows, timeline.Books.Select(book => new AccountingWorkbenchRow(book.DisplayName, book.AccountingBasis.ToString(),
                book.BaseCurrency, $"{book.LedgerBookId:D} · {book.AccountingPolicyId}/{book.AccountingPolicyVersion}")));
            Status = "Retained lifecycle, ownership, agreements and books loaded from the shared entity projection.";
        });
    }

    public Task PreviewAsync()
    {
        var request = Request;
        if (request is null) return Task.CompletedTask;
        _preview = null;
        Balances.Clear(); Matches.Clear(); Drafts.Clear(); Sources.Clear(); TranslationReserveRows.Clear(); Blockers.Clear();
        ScopeDetail = "Loading fresh server consolidation evidence.";
        RaisePropertyChanged(nameof(Preview));
        RaisePropertyChanged(nameof(HasDrafts));
        NotifyCommands();
        return RunAsync(async ct => { var result = await _client!.PreviewAsync(request, ct); if (!ct.IsCancellationRequested) Apply(result); });
    }

    public Task CreateDraftsAsync()
    {
        if (IsBusy || _preview is not { CanCreateDrafts: true } || _preview.Blockers.Count != 0
            || Request is not { } request || _preview.Request != request) return Task.CompletedTask;
        return RunAsync(async ct =>
        {
            try { var result = await _client!.CreateDraftsAsync(request, ct); if (!ct.IsCancellationRequested) Apply(result); }
            catch
            {
                if (!ct.IsCancellationRequested)
                {
                    _preview = null;
                    Balances.Clear(); Matches.Clear(); Drafts.Clear(); Sources.Clear(); TranslationReserveRows.Clear(); Blockers.Clear();
                    ScopeDetail = "The draft command did not return current evidence. Preview again before continuing.";
                    RaisePropertyChanged(nameof(Preview));
                    RaisePropertyChanged(nameof(HasDrafts));
                    NotifyCommands();
                }
                throw;
            }
        });
    }

    private void Apply(ConsolidationViewDto result)
    {
        _preview = result;
        Replace(Balances, result.Balances);
        Replace(Matches, result.Matches);
        Replace(Drafts, result.Drafts);
        Replace(Blockers, result.Blockers);
        Replace(Sources, result.Balances.SelectMany(balance => balance.Sources).DistinctBy(source => (source.LedgerBookId, source.JournalEntryId, source.LineId)));
        Replace(TranslationReserveRows, (result.TranslationReserve ?? new Dictionary<string, decimal>()).Select(pair =>
            new AccountingWorkbenchRow(pair.Key, result.Currency, pair.Value.ToString("G29", CultureInfo.InvariantCulture), result.TranslationFingerprint ?? "No translation fingerprint")));
        ScopeDetail = $"{result.Currency} · {result.RuleVersion ?? "Rule version unavailable"} · {result.EntityIds.Count} entities\n{result.ScopeLimitation}\nSource {result.SourceFingerprint}\nTranslation {result.TranslationFingerprint ?? "Not applied"}";
        Status = result.Blockers.Count > 0 ? $"{result.Blockers.Count} blocker(s); draft creation is unavailable."
            : result.Drafts.Count > 0 ? "Retained drafts are available for review in Manual Journals. Posting requires the ordinary review workflow."
            : "Preview loaded. Proposed eliminations have not been posted.";
        RaisePropertyChanged(nameof(Preview));
        RaisePropertyChanged(nameof(HasDrafts));
        NotifyCommands();
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_client is null) return;
        Cancel();
        var generation = _generation;
        using var cts = new CancellationTokenSource();
        _inFlight = cts;
        IsBusy = true;
        Status = "Loading governed evidence…";
        try { await action(cts.Token); }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception exception) { if (generation == _generation) Status = $"Unavailable: {exception.Message}"; }
        finally
        {
            if (generation == _generation) IsBusy = false;
            if (ReferenceEquals(_inFlight, cts)) _inFlight = null;
        }
    }

    private void NotifyCommands()
    {
        LoadChoicesCommand.NotifyCanExecuteChanged(); LoadTimelineCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged(); CreateDraftsCommand.NotifyCanExecuteChanged();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }
}
