using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Ledger;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed class PrivateCapitalUnitRegisterViewModel : BindableBase
{
    private readonly ILedgerReportsApiClient _client;
    private IReadOnlyList<LedgerBookDto> _books = [];
    private IReadOnlyList<LedgerPeriodDto> _periods = [];
    private LedgerBookDto? _selectedBook;
    private LedgerPeriodDto? _selectedPeriod;
    private string _evidenceLink = string.Empty;
    private string _documentId = string.Empty;
    private string _statusText = string.Empty;
    private bool _busy;
    private PrivateCapitalUnitRegisterPreviewDto? _result;

    public PrivateCapitalUnitRegisterViewModel(ILedgerReportsApiClient client)
    {
        _client = client;
        LoadBooksCommand = new AsyncRelayCommand(LoadBooksAsync, () => CanEdit);
        LoadPeriodsCommand = new AsyncRelayCommand(LoadPeriodsAsync, () => CanEdit && SelectedBook is not null);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => CanEdit && SelectedBook is not null && SelectedPeriod is not null
            && !string.IsNullOrWhiteSpace(EvidenceLink) && !string.IsNullOrWhiteSpace(DocumentId));
    }

    public IAsyncRelayCommand LoadBooksCommand { get; }
    public IAsyncRelayCommand LoadPeriodsCommand { get; }
    public IAsyncRelayCommand PreviewCommand { get; }
    public bool CanEdit => !_busy;
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public IReadOnlyList<LedgerBookDto> Books { get => _books; private set => SetProperty(ref _books, value); }
    public IReadOnlyList<LedgerPeriodDto> Periods { get => _periods; private set => SetProperty(ref _periods, value); }
    public PrivateCapitalUnitRegisterPreviewDto? Result { get => _result; private set => SetProperty(ref _result, value); }

    public LedgerBookDto? SelectedBook
    {
        get => _selectedBook;
        set
        {
            if (!SetProperty(ref _selectedBook, value))
                return;
            Periods = [];
            SelectedPeriod = null;
            InvalidateReview();
        }
    }

    public LedgerPeriodDto? SelectedPeriod
    {
        get => _selectedPeriod;
        set { if (SetProperty(ref _selectedPeriod, value)) InvalidateReview(); }
    }

    public string EvidenceLink
    {
        get => _evidenceLink;
        set { if (SetProperty(ref _evidenceLink, value ?? string.Empty)) InvalidateReview(); }
    }

    public string DocumentId
    {
        get => _documentId;
        set { if (SetProperty(ref _documentId, value ?? string.Empty)) InvalidateReview(); }
    }

    private Task LoadBooksAsync(CancellationToken ct) => RunAsync(async () =>
    {
        var response = await _client.GetBooksAsync(ct);
        if (!response.Success || response.Data is null)
            throw new InvalidOperationException(response.ErrorMessage ?? "Ledger books are unavailable.");
        Books = response.Data;
        SelectedBook = Books.Count == 1 ? Books[0] : null;
        StatusText = Books.Count == 0 ? "No accessible ledger books." : "Select a ledger book and load its hard-closed periods.";
    }, ct);

    private Task LoadPeriodsAsync(CancellationToken ct) => RunAsync(async () =>
    {
        if (SelectedBook is not { } book)
            return;
        var response = await _client.GetPeriodsAsync(book.LedgerBookId, ct);
        if (SelectedBook?.LedgerBookId != book.LedgerBookId)
            return;
        if (!response.Success || response.Data is null)
            throw new InvalidOperationException(response.ErrorMessage ?? "Closed periods are unavailable.");
        Periods = response.Data.Where(period => period.LedgerBookId == book.LedgerBookId && period.Status == LedgerPeriodStatusDto.HardClosed)
            .OrderByDescending(period => period.EndDate).ToArray();
        SelectedPeriod = Periods.FirstOrDefault();
        StatusText = Periods.Count == 0 ? "This book has no hard-closed periods." : "Choose the period and accepted Evidence Vault document.";
    }, ct);

    private Task PreviewAsync(CancellationToken ct) => RunAsync(async () =>
    {
        if (SelectedBook is not { } book || SelectedPeriod is not { } period)
            return;
        Result = null;
        var request = new PreviewPrivateCapitalUnitRegisterRequest(period.PeriodId, EvidenceLink.Trim(), DocumentId.Trim());
        var response = await _client.PreviewUnitRegisterAsync(book.LedgerBookId, request, ct);
        if (SelectedBook?.LedgerBookId != book.LedgerBookId || SelectedPeriod?.PeriodId != period.PeriodId
            || EvidenceLink.Trim() != request.EvidenceLink || DocumentId.Trim() != request.DocumentId)
            return;
        if (!response.Success || response.Data is not { } result)
            throw new InvalidOperationException(response.ErrorMessage ?? "The unit register could not be reconciled.");
        if (result.Scope.LedgerBookId != book.LedgerBookId || result.PeriodId != period.PeriodId
            || result.DocumentId != request.DocumentId || result.EvidenceLink != request.EvidenceLink)
            throw new InvalidOperationException("The unit-register preview does not match the selected ledger and evidence.");
        Result = result;
        StatusText = result.IsTied ? "Unit register ties to retained journals." : "Unit register discrepancies require review.";
    }, ct);

    private async Task RunAsync(Func<Task> operation, CancellationToken ct)
    {
        _busy = true;
        StatusText = "Loading unit register evidence…";
        NotifyCommands();
        try { await operation(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { StatusText = "Unit register request cancelled."; }
        catch (Exception exception) { StatusText = exception.Message; }
        finally
        {
            _busy = false;
            NotifyCommands();
        }
    }

    private void InvalidateReview()
    {
        Result = null;
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanEdit));
        LoadBooksCommand.NotifyCanExecuteChanged();
        LoadPeriodsCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
    }
}
