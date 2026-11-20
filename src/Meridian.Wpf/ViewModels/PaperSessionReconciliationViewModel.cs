using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Execution.Services;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class PaperSessionReconciliationViewModel : BindableBase
{
    private readonly ApiClientService _api;
    private IReadOnlyList<PaperSessionSummaryDto> _sessions = [];
    private PaperSessionSummaryDto? _session;
    private PaperSessionCutoffEvidence? _evidence;
    private string _status = string.Empty;

    public PaperSessionReconciliationViewModel(ApiClientService api)
    {
        _api = api;
        LoadSessionsCommand = new AsyncRelayCommand(LoadSessionsAsync);
        ReviewCommand = new AsyncRelayCommand(ReviewAsync, () => Session is not null);
    }

    public IAsyncRelayCommand LoadSessionsCommand { get; }
    public IAsyncRelayCommand ReviewCommand { get; }

    public IReadOnlyList<PaperSessionSummaryDto> Sessions
    {
        get => _sessions;
        private set => SetProperty(ref _sessions, value);
    }

    public PaperSessionSummaryDto? Session
    {
        get => _session;
        set
        {
            if (!SetProperty(ref _session, value))
                return;
            ReviewCommand.Cancel();
            ReviewCommand.NotifyCanExecuteChanged();
            Evidence = null;
            Status = string.Empty;
        }
    }

    public PaperSessionCutoffEvidence? Evidence
    {
        get => _evidence;
        private set
        {
            if (SetProperty(ref _evidence, value))
                RaisePropertyChanged(nameof(EvidenceVisibility));
        }
    }

    public Visibility EvidenceVisibility => Evidence is null ? Visibility.Collapsed : Visibility.Visible;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    internal void Clear()
    {
        LoadSessionsCommand.Cancel();
        Session = null;
        Sessions = [];
        Status = string.Empty;
    }

    private async Task LoadSessionsAsync(CancellationToken ct)
    {
        Session = null;
        Sessions = [];
        Status = "Loading paper sessions…";
        try
        {
            var response = await _api.GetWithResponseAsync<PaperSessionSummaryDto[]>(UiApiRoutes.ExecutionSessions, ct);
            if (ct.IsCancellationRequested)
                return;
            if (!response.Success || response.Data is not { } rows)
            {
                Status = "Paper sessions are unavailable.";
                return;
            }

            Sessions = rows.OrderByDescending(row => row.CreatedAt).ToArray();
            if (Sessions.Count == 1)
                Session = Sessions[0];
            Status = Sessions.Count == 0 ? "No paper sessions are available in this account scope." : string.Empty;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task ReviewAsync(CancellationToken ct)
    {
        if (Session is not { } session)
            return;
        Evidence = null;
        Status = "Loading daily reconciliation…";
        try
        {
            var route = UiApiRoutes.WithParam(UiApiRoutes.ExecutionSessionReconciliation, "sessionId", session.SessionId);
            var response = await _api.GetWithResponseAsync<PaperSessionCutoffEvidence>(route, ct);
            if (ct.IsCancellationRequested || Session != session)
                return;
            if (response.Success && response.Data is { } evidence && evidence.SessionId == session.SessionId
                && string.Equals(evidence.AccountId, session.FundAccountId?.ToString("D") ?? PaperTradingPortfolio.DefaultAccountId,
                    StringComparison.OrdinalIgnoreCase))
            {
                Evidence = evidence;
                Status = evidence.CoverageBlocker ?? (evidence.Breaks.Count == 0
                    ? "Session cash and lots tie at the retained cutoff."
                    : "Ledger breaks require review.");
                return;
            }

            Status = response.StatusCode switch
            {
                404 => "This session has no retained daily reconciliation.",
                409 => "Retained paper evidence could not verify this cutoff.",
                _ => "Reconciliation evidence is unavailable for this session."
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
