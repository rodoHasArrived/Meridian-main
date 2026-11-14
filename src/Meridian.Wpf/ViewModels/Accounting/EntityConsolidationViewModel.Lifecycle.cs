using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.FundStructure;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class EntityConsolidationViewModel
{
    private bool _lifecycleEditing, _lifecycleDirty;
    private Guid _lifecycleEntityId;
    private long _lifecycleExpectedRevision;
    private string _lifecycleReason = "", _lifecycleEvidence = "", _lifecycleSubjectHash = "";
    private string _lifecycleExternalReviewer = "", _lifecycleReviewerReference = "", _lifecycleReviewedOn = "", _lifecycleReviewEvidence = "", _lifecycleReviewArtifact = "", _lifecycleReviewEvidenceHash = "", _lifecycleReviewRationale = "";
    private LifecycleWindowEditor? _selectedLifecycleWindow;
    public ObservableCollection<LifecycleWindowEditor> LifecycleDraftWindows { get; } = [];
    public IReadOnlyList<LegalEntityLifecycleStatusDto> LifecycleStatuses { get; } = Enum.GetValues<LegalEntityLifecycleStatusDto>();
    public IReadOnlyList<LegalEntityLifecycleEventKindDto> LifecycleEventKinds { get; } = Enum.GetValues<LegalEntityLifecycleEventKindDto>();
    public bool IsLifecycleEditing { get => _lifecycleEditing; private set { SetProperty(ref _lifecycleEditing, value); NotifyLifecycleEditorCommands(); } }
    public bool IsLifecycleDirty { get => _lifecycleDirty; private set { SetProperty(ref _lifecycleDirty, value); NotifyCommands(); } }
    public string LifecycleReason { get => _lifecycleReason; set => EditLifecycle(ref _lifecycleReason, value); }
    public string LifecycleEvidenceReference { get => _lifecycleEvidence; set => EditLifecycle(ref _lifecycleEvidence, value); }
    public string LifecycleSubjectHash { get => _lifecycleSubjectHash; private set { SetProperty(ref _lifecycleSubjectHash, value); NotifyLifecycleEditorCommands(); } }
    public string LifecycleExternalReviewer { get => _lifecycleExternalReviewer; set => SetProperty(ref _lifecycleExternalReviewer, value); }
    public string LifecycleReviewerReference { get => _lifecycleReviewerReference; set => SetProperty(ref _lifecycleReviewerReference, value); }
    public string LifecycleReviewedOn { get => _lifecycleReviewedOn; set => SetProperty(ref _lifecycleReviewedOn, value); }
    public string LifecycleReviewEvidence { get => _lifecycleReviewEvidence; set => SetProperty(ref _lifecycleReviewEvidence, value); }
    public string LifecycleReviewArtifact { get => _lifecycleReviewArtifact; set => SetProperty(ref _lifecycleReviewArtifact, value); }
    public string LifecycleReviewEvidenceHash { get => _lifecycleReviewEvidenceHash; set => SetProperty(ref _lifecycleReviewEvidenceHash, value); }
    public string LifecycleReviewRationale { get => _lifecycleReviewRationale; set => SetProperty(ref _lifecycleReviewRationale, value); }
    public LifecycleWindowEditor? SelectedLifecycleWindow { get => _selectedLifecycleWindow; set => SetProperty(ref _selectedLifecycleWindow, value); }
    public IRelayCommand BeginLifecycleRevisionCommand { get; private set; } = null!;
    public IRelayCommand AddLifecycleWindowCommand { get; private set; } = null!;
    public IRelayCommand RemoveLifecycleWindowCommand { get; private set; } = null!;
    public IRelayCommand DiscardLifecycleRevisionCommand { get; private set; } = null!;
    public IAsyncRelayCommand PreviewLifecycleReviewCommand { get; private set; } = null!;
    public IAsyncRelayCommand RetainLifecycleRevisionCommand { get; private set; } = null!;
    private void InitializeLifecycleEditorCommands()
    {
        BeginLifecycleRevisionCommand = new RelayCommand(BeginLifecycleRevision, () => !IsBusy && !IsLifecycleEditing && _timeline?.CanAttestExternalLifecycleReview == true);
        AddLifecycleWindowCommand = new RelayCommand(() =>
        {
            var row = new LifecycleWindowEditor { EffectiveFrom = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
            row.PropertyChanged += LifecycleWindowChanged;
            LifecycleDraftWindows.Add(row);
            ChangedLifecycleContent();
        }, () => !IsBusy && IsLifecycleEditing);
        RemoveLifecycleWindowCommand = new RelayCommand(() => { if (SelectedLifecycleWindow is not null) { SelectedLifecycleWindow.PropertyChanged -= LifecycleWindowChanged; LifecycleDraftWindows.Remove(SelectedLifecycleWindow); ChangedLifecycleContent(); } }, () => !IsBusy && IsLifecycleEditing);
        DiscardLifecycleRevisionCommand = new RelayCommand(ResetLifecycleEditor, () => !IsBusy && IsLifecycleEditing);
        PreviewLifecycleReviewCommand = new AsyncRelayCommand(PreviewLifecycleReviewAsync, () => !IsBusy && IsLifecycleEditing);
        RetainLifecycleRevisionCommand = new AsyncRelayCommand(RetainLifecycleRevisionAsync, () => !IsBusy && IsLifecycleEditing && !string.IsNullOrWhiteSpace(LifecycleSubjectHash));
    }
    private void BeginLifecycleRevision()
    {
        if (_timeline is not { } timeline)
            return;
        ResetLifecycleEditor();
        _lifecycleEntityId = timeline.Entity.EntityId;
        var latest = timeline.LifecycleRevisions.OrderBy(x => x.Revision).LastOrDefault();
        _lifecycleExpectedRevision = latest?.Revision ?? 0;
        foreach (var state in latest?.Timeline ?? [])
        {
            var row = new LifecycleWindowEditor { TransitionId = state.TransitionId, EffectiveFrom = state.EffectiveFrom.ToString("O", CultureInfo.InvariantCulture), EffectiveTo = state.EffectiveTo?.ToString("O", CultureInfo.InvariantCulture) ?? "", Status = state.Status, EventKind = state.EventKind, SuccessorEntityId = state.SuccessorEntityId?.ToString("D") ?? "" };
            row.PropertyChanged += LifecycleWindowChanged;
            LifecycleDraftWindows.Add(row);
        }
        IsLifecycleEditing = IsLifecycleDirty = true;
        Status = "Prepare the complete effective-dated timeline. Historical legacy observations remain unverified until supported by external review evidence. Dates must include UTC offset.";
    }
    private void EditLifecycle(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    { if (SetProperty(ref field, value, property) && IsLifecycleEditing) ChangedLifecycleContent(); }
    private void LifecycleWindowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => ChangedLifecycleContent();
    private void ChangedLifecycleContent() { LifecycleSubjectHash = ""; IsLifecycleDirty = true; }
    private AppendEntityLifecycleRevisionRequestDto BuildLifecycleRevision()
    {
        if (_lifecycleEntityId == Guid.Empty || SelectedEntity?.EntityId != _lifecycleEntityId)
            throw new InvalidOperationException("The lifecycle draft does not belong to the selected entity.");
        return new(_lifecycleEntityId, _lifecycleExpectedRevision, LifecycleDraftWindows.Select(row => new EntityLifecycleStateDto(row.TransitionId,
            ParseOffset(row.EffectiveFrom), string.IsNullOrWhiteSpace(row.EffectiveTo) ? null : ParseOffset(row.EffectiveTo), row.Status, row.EventKind,
            string.IsNullOrWhiteSpace(row.SuccessorEntityId) ? null : Guid.Parse(row.SuccessorEntityId))).ToArray(), LifecycleReason, LifecycleEvidenceReference);
    }
    private Task PreviewLifecycleReviewAsync() => RunAsync(async ct =>
    {
        var request = BuildLifecycleRevision();
        var result = await _client!.PreviewLifecycleReviewAsync(request, ct);
        if (ct.IsCancellationRequested)
            return;
        var expected = EntityLifecycleExternalReviewIntegrity.SubmittedHash(request);
        if (result.SubmittedHash != expected)
            throw new InvalidOperationException("Server review subject differs from this exact timeline. Reload the source before attesting.");
        LifecycleSubjectHash = result.SubmittedHash;
        Status = "Exact lifecycle subject retained for review. Any content edit clears this preview. Attestation records the external reviewer separately from your authenticated account.";
    });
    private Task RetainLifecycleRevisionAsync() => RunAsync(async ct =>
    {
        var request = BuildLifecycleRevision();
        if (EntityLifecycleExternalReviewIntegrity.SubmittedHash(request) != LifecycleSubjectHash)
            throw new InvalidOperationException("Timeline content changed; preview the exact subject again.");
        if (!DateOnly.TryParseExact(LifecycleReviewedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var reviewedOn))
            throw new InvalidOperationException("Review date must use yyyy-MM-dd.");
        request = request with
        {
            ExternalReview = new(LifecycleExternalReviewer, LifecycleReviewerReference, reviewedOn,
            new(LifecycleReviewEvidence, string.IsNullOrWhiteSpace(LifecycleReviewArtifact) ? null : LifecycleReviewArtifact, string.IsNullOrWhiteSpace(LifecycleReviewEvidenceHash) ? null : LifecycleReviewEvidenceHash), LifecycleReviewRationale, LifecycleSubjectHash)
        };
        var retained = await _client!.AppendLifecycleRevisionAsync(request, ct);
        if (ct.IsCancellationRequested)
            return;
        ResetLifecycleEditor();
        _timeline = null;
        NotifyCommands();
        Status = $"Externally reviewed lifecycle revision {retained.Revision} retained; attested by {retained.RecordedBy}. Reload the timeline to inspect its effective dates. This does not post journals.";
    });
    private static string LifecycleExternalReviewSummary(EntityLifecycleRevisionDto revision)
        => revision.ExternalReview is { } external ? $" · Externally reviewed by {external.ExternalReviewer} ({external.ReviewerReference}) on {external.ReviewedOn:yyyy-MM-dd}; attested by {external.Operator}; evidence {external.Evidence.Reference}/{external.Evidence.ArtifactId}" : "";
    private static DateTimeOffset ParseOffset(string text)
        => DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value
            : throw new InvalidOperationException("Lifecycle dates must use an ISO timestamp with offset, for example 2026-11-13T00:00:00.0000000+00:00.");
    public void ResetSession()
    {
        ResetLifecycleEditor();
        Invalidate();
        _structure = null;
        SelectedOrganization = null;
        SelectedEntity = null;
        SelectedOwnershipRoot = null;
        SelectedEliminationBook = null;
        SelectedPeriod = null;
        Organizations.Clear();
        Entities.Clear();
        OwnershipRoots.Clear();
        EliminationBooks.Clear();
        Periods.Clear();
        Status = "Session changed. Reload authorized organizations before inspecting or reviewing entity evidence.";
    }
    private void ResetLifecycleEditor()
    {
        foreach (var row in LifecycleDraftWindows)
            row.PropertyChanged -= LifecycleWindowChanged;
        LifecycleDraftWindows.Clear();
        IsLifecycleEditing = false;
        IsLifecycleDirty = false;
        _lifecycleEntityId = Guid.Empty;
        LifecycleSubjectHash = "";
        LifecycleReason = LifecycleEvidenceReference = LifecycleExternalReviewer = LifecycleReviewerReference = LifecycleReviewedOn = LifecycleReviewEvidence = LifecycleReviewArtifact = LifecycleReviewEvidenceHash = LifecycleReviewRationale = "";
    }
    private void NotifyLifecycleEditorCommands()
    {
        BeginLifecycleRevisionCommand?.NotifyCanExecuteChanged();
        AddLifecycleWindowCommand?.NotifyCanExecuteChanged();
        RemoveLifecycleWindowCommand?.NotifyCanExecuteChanged();
        DiscardLifecycleRevisionCommand?.NotifyCanExecuteChanged();
        PreviewLifecycleReviewCommand?.NotifyCanExecuteChanged();
        RetainLifecycleRevisionCommand?.NotifyCanExecuteChanged();
    }
}

public sealed class LifecycleWindowEditor : ObservableObject
{
    private string _from = "", _to = "", _successor = "";
    private LegalEntityLifecycleStatusDto _status;
    private LegalEntityLifecycleEventKindDto _eventKind;
    public Guid TransitionId { get; init; } = Guid.NewGuid();
    public string EffectiveFrom { get => _from; set => SetProperty(ref _from, value); }
    public string EffectiveTo { get => _to; set => SetProperty(ref _to, value); }
    public LegalEntityLifecycleStatusDto Status { get => _status; set => SetProperty(ref _status, value); }
    public LegalEntityLifecycleEventKindDto EventKind { get => _eventKind; set => SetProperty(ref _eventKind, value); }
    public string SuccessorEntityId { get => _successor; set => SetProperty(ref _successor, value); }
}
