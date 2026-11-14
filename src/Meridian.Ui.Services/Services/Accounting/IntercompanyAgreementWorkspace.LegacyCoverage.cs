using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

public sealed partial class IntercompanyAgreementWorkspace
{
    private LegacyIntercompanyAttributionDto? _selectedLegacyAttribution;
    private IntercompanyLegacyCoverageCandidate? _legacyCandidate;
    private ResolvedIntercompanyAccountingArrangementDto? _legacyProfile;
    private IntercompanyAccountBindingDto? _legacyBinding;
    private LegacyIntercompanyReviewModeDto _legacyReviewMode = LegacyIntercompanyReviewModeDto.OperatorReviewed;
    private string _legacyPreparedBy = "", _legacyTransaction = "", _legacyEvidence = "", _legacyRationale = "";
    private string _legacyStatus = "Load authoritative source candidates, approved profiles and binding history for the selected ledger book.";
    private string? _legacyRetryPayload;
    private readonly List<IntercompanyAccountBindingDto> _legacyBindingHistory = [];
    private Guid _legacyRequestId;
    private DateTimeOffset _legacyRequestedAt;
    public ObservableCollection<IntercompanyLegacyCoverageCandidate> LegacyCandidates { get; } = [];
    public ObservableCollection<ResolvedIntercompanyAccountingArrangementDto> LegacyProfiles { get; } = [];
    public ObservableCollection<IntercompanyAccountBindingDto> LegacyBindings { get; } = [];
    public ObservableCollection<LegacyIntercompanyAttributionDto> LegacyCoverageHistory { get; } = [];
    public IReadOnlyList<LegacyIntercompanyReviewModeDto> LegacyReviewModes { get; } = Enum.GetValues<LegacyIntercompanyReviewModeDto>();
    public IntercompanyLegacyCoverageCandidate? SelectedLegacyCandidate
    {
        get => _legacyCandidate;
        set
        {
            if (!SetProperty(ref _legacyCandidate, value))
                return;
            LegacyProfiles.Clear();
            SelectedLegacyProfile = null;
            LegacyBindings.Clear();
            SelectedLegacyBinding = null;
            if (value is not null)
                foreach (var binding in _legacyBindingHistory.Where(b => !b.IsArchived && b.LedgerBookId == value.LedgerBookId
                    && b.EffectiveFrom <= value.EffectiveDate && (!b.EffectiveTo.HasValue || value.EffectiveDate < b.EffectiveTo.Value)
                    && (b.AccountName == value.AccountName || b.HistoricalNames.Contains(value.AccountName, StringComparer.Ordinal))))
                    LegacyBindings.Add(binding);
            NotifyLegacyCommands();
        }
    }
    public ResolvedIntercompanyAccountingArrangementDto? SelectedLegacyProfile { get => _legacyProfile; set { SetProperty(ref _legacyProfile, value); NotifyLegacyCommands(); } }
    public IntercompanyAccountBindingDto? SelectedLegacyBinding { get => _legacyBinding; set { SetProperty(ref _legacyBinding, value); NotifyLegacyCommands(); } }
    public LegacyIntercompanyReviewModeDto LegacyReviewMode { get => _legacyReviewMode; set => SetProperty(ref _legacyReviewMode, value); }
    public string LegacyPreparedBy { get => _legacyPreparedBy; set => SetProperty(ref _legacyPreparedBy, value); }
    public string LegacyTransactionId { get => _legacyTransaction; set => SetProperty(ref _legacyTransaction, value); }
    public string LegacyEvidenceReference { get => _legacyEvidence; set => SetProperty(ref _legacyEvidence, value); }
    public string LegacyReviewRationale { get => _legacyRationale; set => SetProperty(ref _legacyRationale, value); }
    public string LegacyCoverageStatus { get => _legacyStatus; private set => SetProperty(ref _legacyStatus, value); }
    public LegacyIntercompanyAttributionDto? SelectedLegacyAttribution { get => _selectedLegacyAttribution; set { SetProperty(ref _selectedLegacyAttribution, value); NotifyLegacyCommands(); } }
    public IAsyncRelayCommand LoadLegacySourceMappingsCommand { get; private set; } = null!;
    public IAsyncRelayCommand RevokeLegacyCoverageCommand { get; private set; } = null!;
    public IAsyncRelayCommand LoadLegacyCoverageCommand { get; private set; } = null!;
    public IAsyncRelayCommand ReviewLegacyCoverageCommand { get; private set; } = null!;
    private void InitializeLegacyCoverageCommands()
    {
        LoadLegacySourceMappingsCommand = new AsyncRelayCommand(LoadLegacySourceMappingsAsync, () => !IsBusy && !IsDirty && SelectedLegacyCandidate is not null);
        RevokeLegacyCoverageCommand = new AsyncRelayCommand(RevokeLegacyCoverageAsync, () => !IsBusy && !IsDirty && SelectedLegacyAttribution is { Revoked: false });
        LoadLegacyCoverageCommand = new AsyncRelayCommand(LoadLegacyCoverageAsync, () => !IsBusy && !IsDirty && !string.IsNullOrWhiteSpace(LedgerBook));
        ReviewLegacyCoverageCommand = new AsyncRelayCommand(ReviewLegacyCoverageAsync, () => !IsBusy && !IsDirty && SelectedLegacyCandidate is not null && SelectedLegacyProfile is not null && SelectedLegacyBinding is not null);
    }
    private Task LoadLegacyCoverageAsync() => RunAsync(async token =>
    {
        var bookId = ParseId(LedgerBook, "Ledger book");
        var date = Date(AsOf, "As-of date");
        var sourcesTask = _client.GetLegacyCoverageCandidatesAsync(bookId, token);
        var historyTask = _client.GetLegacyCoverageHistoryAsync(bookId, token);
        var bindingsTask = _client.GetCoverageBindingsAsync(bookId, token);
        await Task.WhenAll(sourcesTask, historyTask, bindingsTask);
        token.ThrowIfCancellationRequested();
        var sources = Require(sourcesTask.Result);
        var history = Require(historyTask.Result);
        var bindings = Require(bindingsTask.Result);
        ResetLegacyCoverage();
        _legacyBindingHistory.AddRange(bindings);
        foreach (var source in sources.Where(x => x.EffectiveDate <= date))
            LegacyCandidates.Add(source);
        foreach (var attribution in history)
            LegacyCoverageHistory.Add(attribution);
        LegacyCoverageStatus = $"{LegacyCandidates.Count} immutable source candidate(s). Select a source and load approved mappings at its own effective date. Review mode is explicit; existing journals remain unchanged.";
    });
    private Task LoadLegacySourceMappingsAsync() => RunAsync(async token =>
    {
        var source = SelectedLegacyCandidate ?? throw new InvalidOperationException("Select a retained source line.");
        var profiles = Require(await _client.GetCoverageProfilesAsync(source.LedgerBookId, source.EffectiveDate, token));
        token.ThrowIfCancellationRequested();
        if (SelectedLegacyCandidate != source)
            return;
        LegacyProfiles.Clear();
        foreach (var profile in profiles.Where(p => p.LegalRevision.Terms.AgreementType != IntercompanyAgreementTypeDto.Guarantee))
            LegacyProfiles.Add(profile);
        LegacyCoverageStatus = $"Approved profiles and historical account bindings for source date {source.EffectiveDate:yyyy-MM-dd}. Guarantees cannot cover ordinary receivable/payable source lines.";
    });
    private Task ReviewLegacyCoverageAsync() => RunAsync(async token =>
    {
        var source = SelectedLegacyCandidate ?? throw new InvalidOperationException("Select an authoritative source line.");
        var profile = SelectedLegacyProfile ?? throw new InvalidOperationException("Select an approved accounting profile.");
        var binding = SelectedLegacyBinding ?? throw new InvalidOperationException("Select the account binding applicable to that source line.");
        if (source.LedgerBookId != binding.LedgerBookId || !profile.PartyMappings.Any(x => x.LedgerBookId == source.LedgerBookId))
            throw new InvalidOperationException("Source, accounting profile and account binding must belong to the selected book.");
        var previous = LegacyCoverageHistory.Where(x => x.JournalEntryId == source.JournalEntryId && x.LineId == source.EntryId).OrderBy(x => x.Revision).LastOrDefault();
        var row = new LegacyIntercompanyAttributionDto(Guid.Empty, (previous?.Revision ?? 0) + 1, source.LedgerBookId, source.JournalEntryId,
            source.EntryId, source.SourceLineDigest, profile.ArrangementId, profile.RevisionId, binding.RevisionId, binding.ChartAccountNodeId,
            LegacyTransactionId, LegacyPreparedBy, "", default, LegacyEvidenceReference, "", null, previous?.AttributionId)
        {
            IntercompanyAgreementId = profile.CanonicalAgreementId,
            IntercompanyAgreementVersion = profile.CanonicalAgreementVersion,
            IntercompanyAgreementPayloadHash = profile.CanonicalPayloadHash,
            ReviewMode = LegacyReviewMode,
            ReviewRationale = LegacyReviewRationale
        };
        var payload = JsonSerializer.Serialize(row);
        if (_legacyRetryPayload != payload)
        { _legacyRetryPayload = payload; _legacyRequestId = Guid.NewGuid(); _legacyRequestedAt = DateTimeOffset.UtcNow; }
        var retained = Require(await _client.AppendLegacyCoverageAsync(new(row with { AttributionId = _legacyRequestId, ApprovedAt = _legacyRequestedAt }, previous?.AttributionId), token));
        token.ThrowIfCancellationRequested();
        LegacyCoverageHistory.Add(retained);
        LegacyCoverageStatus = retained.ReviewMode == LegacyIntercompanyReviewModeDto.OperatorReviewed
            ? $"Operator-reviewed attribution retained by {retained.ApprovedBy}. This is not independent review, settlement, or journal posting."
            : $"Independently reviewed attribution retained by {retained.ApprovedBy}. Source journals remain unchanged.";
    });
    private Task RevokeLegacyCoverageAsync() => RunAsync(async token =>
    {
        var previous = SelectedLegacyAttribution ?? throw new InvalidOperationException("Select retained coverage to revoke.");
        if (previous.Revoked || LegacyCoverageHistory.Any(x => x.JournalEntryId == previous.JournalEntryId && x.LineId == previous.LineId && x.Revision > previous.Revision))
            throw new InvalidOperationException("Select the latest active attribution for this source line.");
        var row = previous with
        {
            AttributionId = Guid.Empty,
            Revision = previous.Revision + 1,
            PreviousAttributionId = previous.AttributionId,
            ApprovedBy = "",
            ApprovedAt = default,
            PreparedBy = LegacyPreparedBy,
            TenantId = "",
            CompanyId = null,
            Revoked = true,
            ReviewMode = LegacyReviewMode,
            ReviewRationale = LegacyReviewRationale,
            EvidenceReference = LegacyEvidenceReference
        };
        var payload = "revoke:" + JsonSerializer.Serialize(row);
        if (_legacyRetryPayload != payload)
        { _legacyRetryPayload = payload; _legacyRequestId = Guid.NewGuid(); _legacyRequestedAt = DateTimeOffset.UtcNow; }
        var retained = Require(await _client.AppendLegacyCoverageAsync(new(row with { AttributionId = _legacyRequestId, ApprovedAt = _legacyRequestedAt }, previous.AttributionId), token));
        token.ThrowIfCancellationRequested();
        LegacyCoverageHistory.Add(retained);
        SelectedLegacyAttribution = retained;
        LegacyCoverageStatus = "Coverage revoked through a reviewed successor. Original source journals and earlier attribution evidence remain immutable.";
    });
    private void ResetLegacyCoverage()
    {
        _legacyBindingHistory.Clear();
        LegacyCandidates.Clear();
        LegacyProfiles.Clear();
        LegacyBindings.Clear();
        LegacyCoverageHistory.Clear();
        SelectedLegacyAttribution = null;
        SelectedLegacyCandidate = null;
        SelectedLegacyProfile = null;
        SelectedLegacyBinding = null;
        LegacyCoverageStatus = "Coverage sources are not loaded for this book/date.";
    }
    private void NotifyLegacyCommands() { LoadLegacySourceMappingsCommand?.NotifyCanExecuteChanged(); RevokeLegacyCoverageCommand?.NotifyCanExecuteChanged(); LoadLegacyCoverageCommand?.NotifyCanExecuteChanged(); ReviewLegacyCoverageCommand?.NotifyCanExecuteChanged(); }
}
