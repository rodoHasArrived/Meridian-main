using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Presentation-only desktop workbench over the shared workstation evidence endpoints. It projects
/// server-owned evidence subjects, packets, proof chains, vault request lists, and retained
/// documents; it never evaluates completeness, SLA, or retention policy locally. Deep links arrive
/// as <c>EvidenceWorkbench:{subjectKind}/{subjectId}</c> navigation parameters.
/// </summary>
public sealed class EvidenceWorkbenchViewModel : BindableBase, IDisposable
{
    private const int VaultQueueMaxResults = 25;

    private readonly IEvidenceWorkbenchApiClient _apiClient;
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;
    private bool _hasLoaded;
    private bool _suppressSelectionLoad;
    private object? _parameter;
    private string? _requestedSubjectKind;
    private string? _requestedSubjectId;
    private EvidenceWorkbenchSubjectRowModel? _selectedSubject;
    private EvidencePacketDto? _packet;
    private bool _isRefreshing;
    private bool _isValidating;
    private bool _isExporting;
    private string _title = "Evidence Workbench";
    private string _subtitle = "Choose a workflow subject to inspect packet completeness and lineage.";
    private string _statusText = "Waiting for evidence subjects from the shared workstation API.";
    private string _errorText = string.Empty;
    private string _scoreText = "No score";
    private string _completenessStatusText = "Not loaded";
    private WorkstationReadinessTone _completenessTone = WorkstationReadinessTone.Neutral;
    private string _completenessToneText = Meridian.Wpf.Models.WorkspaceTone.Neutral;
    private string _generatedText = "Not generated";
    private string _proofChainSummaryText = "Proof-chain coverage was not returned by this packet.";
    private string _proofChainCoverageText = "No proof-chain coverage";
    private string _exportResultText = string.Empty;
    private EvidenceVaultDocumentQueuePresentationModel _documentQueue =
        EvidenceVaultPresentationMapper.BuildDocumentQueue([], null);

    public EvidenceWorkbenchViewModel(IEvidenceWorkbenchApiClient apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        RefreshCommand = new AsyncRelayCommand(
            () => _isDisposed ? Task.CompletedTask : RefreshAsync(_cts.Token),
            () => !IsBusy);
        ValidateCommand = new AsyncRelayCommand(
            () => _isDisposed ? Task.CompletedTask : ValidateAsync(_cts.Token),
            () => HasPacket && !IsBusy);
        ExportManifestCommand = new AsyncRelayCommand(
            () => _isDisposed ? Task.CompletedTask : ExportManifestAsync(_cts.Token),
            () => HasPacket && !IsBusy);
        OpenPacketActionCommand = new RelayCommand<EvidencePacketActionRowModel>(
            OpenPacketAction,
            static action => action is not null && !string.IsNullOrWhiteSpace(action.TargetPageTag));
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand ValidateCommand { get; }

    public IAsyncRelayCommand ExportManifestCommand { get; }

    public IRelayCommand<EvidencePacketActionRowModel> OpenPacketActionCommand { get; }

    public ObservableCollection<EvidenceWorkbenchSubjectRowModel> Subjects { get; } = [];

    public ObservableCollection<EvidenceWorkbenchSummaryFactModel> SummaryItems { get; } = [];

    public ObservableCollection<EvidenceProofChainLayerRowModel> ProofChainRows { get; } = [];

    public ObservableCollection<EvidenceNodeRowModel> NodeRows { get; } = [];

    public ObservableCollection<EvidenceLineageRowModel> LineageRows { get; } = [];

    public ObservableCollection<EvidenceRequestListRowModel> RequestListRows { get; } = [];

    public ObservableCollection<EvidencePacketActionRowModel> PacketActions { get; } = [];

    public ObservableCollection<string> MissingEvidence { get; } = [];

    public ObservableCollection<string> StaleEvidence { get; } = [];

    public ObservableCollection<string> OrphanEvidence { get; } = [];

    public ObservableCollection<string> SlaBreaches { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>
    /// Navigation parameter applied by the shell. A string in the form
    /// <c>{subjectKind}/{subjectId}</c> (optionally still carrying the
    /// <c>EvidenceWorkbench:</c> prefix) focuses the workbench on that subject.
    /// </summary>
    public object? Parameter
    {
        get => _parameter;
        set
        {
            if (SetProperty(ref _parameter, value))
            {
                ApplyRequestedSubject(TryParseSubject(value));
            }
        }
    }

    public EvidenceWorkbenchSubjectRowModel? SelectedSubject
    {
        get => _selectedSubject;
        set
        {
            if (SetProperty(ref _selectedSubject, value) && !_suppressSelectionLoad && value is not null && !_isDisposed)
            {
                _requestedSubjectKind = value.SubjectKind;
                _requestedSubjectId = value.SubjectId;
                _ = RefreshAsync(_cts.Token);
            }
        }
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                NotifyBusyChanged();
            }
        }
    }

    public bool IsValidating
    {
        get => _isValidating;
        private set
        {
            if (SetProperty(ref _isValidating, value))
            {
                NotifyBusyChanged();
            }
        }
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetProperty(ref _isExporting, value))
            {
                NotifyBusyChanged();
            }
        }
    }

    public bool IsBusy => IsRefreshing || IsValidating || IsExporting;

    public bool HasPacket => _packet is not null;

    public bool HasSubjects => Subjects.Count > 0;

    public string ScoreText
    {
        get => _scoreText;
        private set => SetProperty(ref _scoreText, value);
    }

    public string CompletenessStatusText
    {
        get => _completenessStatusText;
        private set => SetProperty(ref _completenessStatusText, value);
    }

    public WorkstationReadinessTone CompletenessTone
    {
        get => _completenessTone;
        private set => SetProperty(ref _completenessTone, value);
    }

    public string CompletenessToneText
    {
        get => _completenessToneText;
        private set => SetProperty(ref _completenessToneText, value);
    }

    public string GeneratedText
    {
        get => _generatedText;
        private set => SetProperty(ref _generatedText, value);
    }

    public string ProofChainSummaryText
    {
        get => _proofChainSummaryText;
        private set => SetProperty(ref _proofChainSummaryText, value);
    }

    public string ProofChainCoverageText
    {
        get => _proofChainCoverageText;
        private set => SetProperty(ref _proofChainCoverageText, value);
    }

    public string ExportResultText
    {
        get => _exportResultText;
        private set
        {
            if (SetProperty(ref _exportResultText, value))
            {
                OnPropertyChanged(nameof(HasExportResult));
            }
        }
    }

    public bool HasExportResult => !string.IsNullOrWhiteSpace(ExportResultText);

    public EvidenceVaultDocumentQueuePresentationModel DocumentQueue
    {
        get => _documentQueue;
        private set => SetProperty(ref _documentQueue, value);
    }

    public void Activate()
    {
        if (!_hasLoaded && !IsRefreshing && !_isDisposed)
        {
            _ = RefreshAsync(_cts.Token);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The token source was already disposed; nothing to cancel.
            LoggingService.Instance.LogDebug(
                "Ignored cancel on already-disposed token source.",
                ("view", nameof(EvidenceWorkbenchViewModel)));
        }

        _cts.Dispose();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_isDisposed || IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        ErrorText = string.Empty;
        ExportResultText = string.Empty;
        try
        {
            var subjectsResponse = await _apiClient.ListSubjectsAsync(ct).ConfigureAwait(true);
            if (!subjectsResponse.Success)
            {
                ApplyLoadFailure(subjectsResponse.ErrorMessage ?? "Evidence subjects failed to load.");
                return;
            }

            var subjects = EvidenceWorkbenchPresentationMapper.BuildSubjectRows(subjectsResponse.Data ?? []);
            var selection = ResolveSelection(subjects);
            ApplySubjects(subjects, selection);

            var requestListsTask = _apiClient.ListOpenRequestListsAsync(
                selection?.SubjectKind, selection?.SubjectId, VaultQueueMaxResults, ct);
            var documentsTask = _apiClient.ListDocumentsAsync(
                selection?.SubjectKind, selection?.SubjectId, VaultQueueMaxResults, ct);
            var packetResponse = selection is null
                ? null
                : await _apiClient.GetPacketAsync(selection.SubjectKind, selection.SubjectId, ct).ConfigureAwait(true);
            var requestListsResponse = await requestListsTask.ConfigureAwait(true);
            var documentsResponse = await documentsTask.ConfigureAwait(true);

            ApplyVaultQueues(requestListsResponse, documentsResponse);
            ApplyPacketResponse(selection, packetResponse);
            _hasLoaded = true;
        }
        catch (OperationCanceledException)
        {
            // Navigation or disposal cancelled the in-flight refresh; leave current state as-is.
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public async Task ValidateAsync(CancellationToken ct = default)
    {
        var subject = SelectedSubject;
        if (_isDisposed || subject is null || _packet is null)
        {
            return;
        }

        IsValidating = true;
        ErrorText = string.Empty;
        try
        {
            var response = await _apiClient.ValidatePacketAsync(subject.SubjectKind, subject.SubjectId, ct).ConfigureAwait(true);
            if (!response.Success || response.Data is null)
            {
                ErrorText = response.ErrorMessage ?? "Evidence validation failed.";
                return;
            }

            ApplyPacketProjection(EvidenceWorkbenchPresentationMapper.BuildPacket(_packet, response.Data));
            StatusText = $"Validation refreshed for {subject.Label}.";
        }
        catch (OperationCanceledException)
        {
            // Cancelled during teardown; keep the retained packet projection.
        }
        finally
        {
            IsValidating = false;
        }
    }

    public async Task ExportManifestAsync(CancellationToken ct = default)
    {
        var subject = SelectedSubject;
        if (_isDisposed || subject is null || _packet is null)
        {
            return;
        }

        IsExporting = true;
        ErrorText = string.Empty;
        ExportResultText = string.Empty;
        try
        {
            var request = new EvidencePacketExportRequest(
                RequestedBy: null,
                Reason: $"Desktop evidence workbench export for {subject.SubjectKey}.",
                IncludeWarnings: true);
            var response = await _apiClient.ExportManifestAsync(subject.SubjectKind, subject.SubjectId, request, ct).ConfigureAwait(true);
            if (!response.Success || response.Data is null)
            {
                ErrorText = response.ErrorMessage ?? "Evidence manifest export failed.";
                return;
            }

            ExportResultText = BuildExportResultText(response.Data);
            StatusText = $"Evidence manifest retained for {subject.Label}.";
        }
        catch (OperationCanceledException)
        {
            // Cancelled during teardown; the server retains any manifest it already froze.
        }
        finally
        {
            IsExporting = false;
        }
    }

    internal static (string SubjectKind, string SubjectId)? TryParseSubject(object? parameter)
    {
        if (parameter is not string text || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var subject = text.Trim();
        const string prefix = "EvidenceWorkbench:";
        if (subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            subject = subject[prefix.Length..].Trim();
        }

        subject = subject.TrimStart('/');
        var separatorIndex = subject.IndexOf('/', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == subject.Length - 1)
        {
            return null;
        }

        var subjectKind = subject[..separatorIndex].Trim();
        var subjectId = subject[(separatorIndex + 1)..].Trim();
        return string.IsNullOrWhiteSpace(subjectKind) || string.IsNullOrWhiteSpace(subjectId)
            ? null
            : (subjectKind, subjectId);
    }

    internal static string BuildExportResultText(EvidencePacketExportResponse response)
    {
        var retention = response.Retained ? "retained" : "generated";
        var vault = response.VaultIdentity is null ? string.Empty : $" Vault {response.VaultIdentity.VaultId}.";
        return $"Manifest {retention} at {response.ManifestPath} " +
               $"({response.EvidenceCount} evidence, {response.WarningCount} warnings).{vault}";
    }

    private void ApplyRequestedSubject((string SubjectKind, string SubjectId)? subject)
    {
        if (subject is null || _isDisposed)
        {
            return;
        }

        _requestedSubjectKind = subject.Value.SubjectKind;
        _requestedSubjectId = subject.Value.SubjectId;
        _hasLoaded = false;
        _ = RefreshAsync(_cts.Token);
    }

    private EvidenceWorkbenchSubjectRowModel? ResolveSelection(
        IReadOnlyList<EvidenceWorkbenchSubjectRowModel> subjects)
    {
        if (_requestedSubjectKind is not null && _requestedSubjectId is not null)
        {
            var requested = subjects.FirstOrDefault(subject =>
                string.Equals(subject.SubjectKind, _requestedSubjectKind, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(subject.SubjectId, _requestedSubjectId, StringComparison.OrdinalIgnoreCase));

            // A deep-linked subject stays addressable even when the subject index does not list it.
            return requested ?? new EvidenceWorkbenchSubjectRowModel(
                _requestedSubjectKind,
                _requestedSubjectId,
                $"{_requestedSubjectKind}/{_requestedSubjectId}",
                "Workstation",
                $"{_requestedSubjectKind}/{_requestedSubjectId}");
        }

        return subjects.Count > 0 ? subjects[0] : null;
    }

    private void ApplySubjects(
        IReadOnlyList<EvidenceWorkbenchSubjectRowModel> subjects,
        EvidenceWorkbenchSubjectRowModel? selection)
    {
        _suppressSelectionLoad = true;
        try
        {
            Subjects.Clear();
            foreach (var subject in subjects)
            {
                Subjects.Add(subject);
            }

            if (selection is not null && !subjects.Any(subject =>
                    string.Equals(subject.SubjectKey, selection.SubjectKey, StringComparison.OrdinalIgnoreCase)))
            {
                Subjects.Insert(0, selection);
            }

            SelectedSubject = selection is null
                ? null
                : Subjects.First(subject =>
                    string.Equals(subject.SubjectKey, selection.SubjectKey, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _suppressSelectionLoad = false;
        }

        OnPropertyChanged(nameof(HasSubjects));
    }

    private void ApplyVaultQueues(
        ApiResponse<EvidenceVaultRequestListEntryDto[]> requestListsResponse,
        ApiResponse<EvidenceVaultDocumentEntryDto[]> documentsResponse)
    {
        RequestListRows.Clear();
        if (requestListsResponse.Success)
        {
            foreach (var row in EvidenceWorkbenchPresentationMapper.BuildRequestListRows(requestListsResponse.Data ?? []))
            {
                RequestListRows.Add(row);
            }
        }

        DocumentQueue = EvidenceVaultPresentationMapper.BuildDocumentQueue(
            documentsResponse.Success ? documentsResponse.Data ?? [] : [],
            manifestSnapshot: null);
    }

    private void ApplyPacketResponse(
        EvidenceWorkbenchSubjectRowModel? selection,
        ApiResponse<EvidencePacketDto>? packetResponse)
    {
        if (selection is null)
        {
            _packet = null;
            ApplyPacketProjection(null);
            Title = "Evidence Workbench";
            Subtitle = "Choose a workflow subject to inspect packet completeness and lineage.";
            StatusText = "No evidence subjects were returned by the shared workstation API.";
            OnPropertyChanged(nameof(HasPacket));
            return;
        }

        if (packetResponse is null || !packetResponse.Success || packetResponse.Data is null)
        {
            _packet = null;
            ApplyPacketProjection(null);
            Title = selection.Label;
            Subtitle = $"{selection.Label} is selected, but packet evidence is not available.";
            StatusText = packetResponse?.ErrorMessage ?? "Evidence packet failed to load.";
            OnPropertyChanged(nameof(HasPacket));
            return;
        }

        _packet = packetResponse.Data;
        Title = packetResponse.Data.Subject.Label;
        Subtitle = $"{packetResponse.Data.Subject.Workspace} evidence packet";
        StatusText = $"Evidence packet loaded for {selection.SubjectKey}.";
        ApplyPacketProjection(EvidenceWorkbenchPresentationMapper.BuildPacket(packetResponse.Data));
        OnPropertyChanged(nameof(HasPacket));
    }

    private void ApplyPacketProjection(EvidenceWorkbenchPacketPresentationModel? projection)
    {
        SummaryItems.Clear();
        ProofChainRows.Clear();
        NodeRows.Clear();
        LineageRows.Clear();
        PacketActions.Clear();
        MissingEvidence.Clear();
        StaleEvidence.Clear();
        OrphanEvidence.Clear();
        SlaBreaches.Clear();
        Warnings.Clear();

        if (projection is null)
        {
            ScoreText = "No score";
            CompletenessStatusText = "Not loaded";
            CompletenessTone = WorkstationReadinessTone.Neutral;
            CompletenessToneText = Meridian.Wpf.Models.WorkspaceTone.Neutral;
            GeneratedText = "Not generated";
            ProofChainSummaryText = "Proof-chain coverage was not returned by this packet.";
            ProofChainCoverageText = "No proof-chain coverage";
            ValidateCommand.NotifyCanExecuteChanged();
            ExportManifestCommand.NotifyCanExecuteChanged();
            return;
        }

        ScoreText = projection.ScoreText;
        CompletenessStatusText = projection.StatusText;
        CompletenessTone = projection.ReadinessTone;
        CompletenessToneText = projection.Tone;
        GeneratedText = projection.GeneratedText;
        ProofChainSummaryText = projection.ProofChainSummaryText;
        ProofChainCoverageText = projection.ProofChainCoverageText;

        SummaryItems.Add(new EvidenceWorkbenchSummaryFactModel("Completeness", projection.ScoreText, projection.StatusText));
        SummaryItems.Add(new EvidenceWorkbenchSummaryFactModel("Generated", projection.GeneratedText, "Server-side packet assembly time"));
        SummaryItems.Add(new EvidenceWorkbenchSummaryFactModel(
            "Evidence nodes",
            projection.Nodes.Count == 1 ? "1 node" : $"{projection.Nodes.Count} nodes",
            projection.LineageRows.Count == 1 ? "1 lineage edge" : $"{projection.LineageRows.Count} lineage edges"));
        SummaryItems.Add(new EvidenceWorkbenchSummaryFactModel(
            "Open gaps",
            $"{projection.MissingEvidenceIds.Count + projection.StaleEvidenceIds.Count} missing or stale",
            projection.SlaBreachMessages.Count == 1 ? "1 SLA breach" : $"{projection.SlaBreachMessages.Count} SLA breaches"));

        foreach (var layer in projection.ProofChainLayers)
        {
            ProofChainRows.Add(layer);
        }

        foreach (var node in projection.Nodes)
        {
            NodeRows.Add(node);
        }

        foreach (var edge in projection.LineageRows)
        {
            LineageRows.Add(edge);
        }

        foreach (var action in projection.Actions)
        {
            PacketActions.Add(action);
        }

        foreach (var id in projection.MissingEvidenceIds)
        {
            MissingEvidence.Add(id);
        }

        foreach (var id in projection.StaleEvidenceIds)
        {
            StaleEvidence.Add(id);
        }

        foreach (var id in projection.OrphanEvidenceIds)
        {
            OrphanEvidence.Add(id);
        }

        foreach (var message in projection.SlaBreachMessages)
        {
            SlaBreaches.Add(message);
        }

        foreach (var warning in projection.Warnings)
        {
            Warnings.Add(warning);
        }

        ValidateCommand.NotifyCanExecuteChanged();
        ExportManifestCommand.NotifyCanExecuteChanged();
    }

    private void ApplyLoadFailure(string message)
    {
        _packet = null;
        ApplyPacketProjection(null);
        ErrorText = message;
        StatusText = "Evidence workbench failed to load.";
        OnPropertyChanged(nameof(HasPacket));
    }

    private void OpenPacketAction(EvidencePacketActionRowModel? action)
    {
        if (action is null || string.IsNullOrWhiteSpace(action.TargetPageTag))
        {
            return;
        }

        NavigationService.Instance.NavigateTo(action.TargetPageTag);
    }

    private void NotifyBusyChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        RefreshCommand.NotifyCanExecuteChanged();
        ValidateCommand.NotifyCanExecuteChanged();
        ExportManifestCommand.NotifyCanExecuteChanged();
    }
}
