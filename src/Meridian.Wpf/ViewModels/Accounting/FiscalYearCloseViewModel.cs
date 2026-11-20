using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed class FiscalYearCloseViewModel : BindableBase
{
    private readonly ILedgerReportsApiClient _ledgerApi;
    private readonly IWorkstationFiscalYearCloseApiClient _closeApi;
    private IReadOnlyList<LedgerBookDto> _books = [];
    private LedgerBookDto? _selectedBook;
    private string _fiscalYearText = string.Empty;
    private string _reason = string.Empty;
    private string _statusText = "Load accessible ledger books to review a fiscal year.";
    private bool _isBusy;
    private FiscalYearClosePlanDto? _plan;
    private FiscalYearCloseReceiptDto? _latest;
    private FiscalYearCloseReceiptDto? _retainedView;
    private FinalizeFiscalYearCloseRequestDto? _pendingCommand;
    private IReadOnlyList<FiscalCloseBalanceSection> _balanceSections = [];
    private FiscalCloseBalanceSection? _selectedSection;

    public FiscalYearCloseViewModel(ILedgerReportsApiClient ledgerApi, IWorkstationFiscalYearCloseApiClient closeApi)
    {
        _ledgerApi = ledgerApi;
        _closeApi = closeApi;
        LoadBooksCommand = new AsyncRelayCommand(LoadBooksAsync, () => !IsBusy);
        ReviewCommand = new AsyncRelayCommand(ReviewAsync, () => !IsBusy && SelectedBook is not null && TryGetFiscalYear(out _));
        FinalizeCommand = new AsyncRelayCommand(FinalizeAsync, CanFinalize);
        ViewRetainedCommand = new RelayCommand(() =>
        {
            _retainedView = _latest;
            UpdateEvidence();
        }, () => !IsBusy && _latest is not null && _retainedView is null && !MatchesRetainedPlan);
        ShowPredecessorCommand = new AsyncRelayCommand(ShowPredecessorAsync,
            () => !IsBusy && DisplayedReceipt?.SupersedesReceiptId is not null);
        ReturnToCurrentCommand = new RelayCommand(() =>
        {
            _retainedView = null;
            UpdateEvidence();
        }, () => !IsBusy && _retainedView is not null);
    }

    public IAsyncRelayCommand LoadBooksCommand { get; }
    public IAsyncRelayCommand ReviewCommand { get; }
    public IAsyncRelayCommand FinalizeCommand { get; }
    public IRelayCommand ViewRetainedCommand { get; }
    public IAsyncRelayCommand ShowPredecessorCommand { get; }
    public IRelayCommand ReturnToCurrentCommand { get; }
    public IReadOnlyList<LedgerBookDto> Books { get => _books; private set => SetProperty(ref _books, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanEditScope));
                NotifyCommands();
            }
        }
    }

    public bool CanEditScope => !IsBusy;

    public LedgerBookDto? SelectedBook
    {
        get => _selectedBook;
        set
        {
            if (SetProperty(ref _selectedBook, value))
                InvalidateScope();
        }
    }

    public string FiscalYearText
    {
        get => _fiscalYearText;
        set
        {
            if (SetProperty(ref _fiscalYearText, value ?? string.Empty))
                InvalidateScope();
        }
    }

    public string Reason
    {
        get => _reason;
        set
        {
            if (SetProperty(ref _reason, value ?? string.Empty))
                FinalizeCommand.NotifyCanExecuteChanged();
        }
    }

    public FiscalYearCloseReceiptDto? DisplayedReceipt => _retainedView ?? _latest;
    public FiscalYearClosePlanDto? DisplayedPlan => _retainedView?.Plan ?? _plan;
    public string CloseActionLabel => _latest is null ? "Retain fiscal close" : "Retain successor close";
    public string EvidenceSummary
    {
        get
        {
            if (DisplayedPlan is not { IsReady: true } plan)
                return string.Empty;

            var currency = _retainedView?.FunctionalCurrency ?? plan.Book?.FunctionalCurrency ?? SelectedBook?.BaseCurrency;
            return $"Reported net income {Format(plan.ReportedNetIncome)} {currency}  ·  " +
                $"Opening equity {Format(plan.OpeningEquity)} {currency}  ·  " +
                $"Post-close equity {Format(plan.PostClosingEquity)} {currency}";
        }
    }

    public IReadOnlyList<FiscalCloseBalanceSection> BalanceSections
    {
        get => _balanceSections;
        private set => SetProperty(ref _balanceSections, value);
    }

    public FiscalCloseBalanceSection? SelectedSection
    {
        get => _selectedSection;
        set => SetProperty(ref _selectedSection, value);
    }

    private bool MatchesRetainedPlan => _latest is not null && _plan?.PlanHashSha256 == _latest.Plan.PlanHashSha256;

    private bool CanFinalize() => !IsBusy && SelectedBook is not null && TryGetFiscalYear(out _) &&
        _retainedView is null && _plan is { IsReady: true, CanFinalizeFiscalYearClose: true, PlanHashSha256: not null }
        && !MatchesRetainedPlan && !string.IsNullOrWhiteSpace(Reason);

    private bool TryGetFiscalYear(out int year) =>
        int.TryParse(FiscalYearText, NumberStyles.None, CultureInfo.InvariantCulture, out year) && year is >= 1900 and <= 9999;

    private async Task LoadBooksAsync(CancellationToken ct)
    {
        await ReadOrWriteAsync(async () =>
        {
            var response = await _ledgerApi.GetBooksAsync(ct);
            if (!response.Success || response.Data is null)
                throw new InvalidOperationException(response.ErrorMessage ?? "Ledger books are unavailable.");
            var priorBook = SelectedBook?.LedgerBookId;
            Books = response.Data;
            SelectedBook = Books.FirstOrDefault(book => book.LedgerBookId == priorBook)
                ?? (Books.Count == 1 ? Books[0] : null);
            StatusText = Books.Count > 0
                ? "Select a ledger book and enter its fiscal year."
                : "No ledger books are available for this session.";
        }, ct);
    }

    private async Task ReviewAsync(CancellationToken ct)
    {
        if (SelectedBook is not { } book || !TryGetFiscalYear(out var year))
            return;
        _plan = null;
        _latest = null;
        _retainedView = null;
        UpdateEvidence();

        await ReadOrWriteAsync(async () =>
        {
            var planTask = _closeApi.GetClosePlanAsync(book.LedgerBookId, year, ct);
            var receiptTask = _closeApi.GetCloseReceiptAsync(book.LedgerBookId, year, null, ct);
            await Task.WhenAll(planTask, receiptTask);
            if (SelectedBook?.LedgerBookId != book.LedgerBookId || !TryGetFiscalYear(out var currentYear) || currentYear != year)
                return;
            _plan = await planTask;
            _latest = await receiptTask;
            StatusText = MatchesRetainedPlan ? "This fiscal close has been retained."
                : !_plan.IsReady ? "Resolve the fiscal close blockers before retaining a close."
                : _plan.CanFinalizeFiscalYearClose ? "Review the evidence and record a reason to retain the close."
                : _plan.ProposedClosingTransfers.Count > 0 ? "Complete the proposed transfers through governed period close."
                : "Retaining the close requires controller authorization.";
        }, ct);
    }

    private async Task FinalizeAsync(CancellationToken ct)
    {
        if (!CanFinalize() || SelectedBook is not { } book || !TryGetFiscalYear(out var year)
            || _plan?.PlanHashSha256 is not { } planHash)
            return;

        var reason = Reason.Trim();
        var predecessor = _latest?.ReceiptId;
        var request = _pendingCommand is { } pending && pending.ExpectedPlanHashSha256 == planHash
            && pending.Reason == reason && pending.SupersedesReceiptId == predecessor
            ? pending : new FinalizeFiscalYearCloseRequestDto(Guid.NewGuid(), planHash, reason, predecessor);
        _pendingCommand = request;
        await ReadOrWriteAsync(async () =>
        {
            _latest = await _closeApi.FinalizeCloseAsync(book.LedgerBookId, year, request, ct);
            _pendingCommand = null;
            Reason = string.Empty;
            StatusText = "Fiscal close receipt retained.";
        }, ct);
    }

    private async Task ShowPredecessorAsync(CancellationToken ct)
    {
        if (SelectedBook is not { } book || !TryGetFiscalYear(out var year)
            || DisplayedReceipt?.SupersedesReceiptId is not { } predecessor)
            return;

        await ReadOrWriteAsync(async () =>
        {
            _retainedView = await _closeApi.GetCloseReceiptAsync(book.LedgerBookId, year, predecessor, ct);
            StatusText = "Showing an earlier retained close receipt.";
        }, ct);
    }

    private async Task ReadOrWriteAsync(Func<Task> action, CancellationToken ct)
    {
        IsBusy = true;
        StatusText = "Loading fiscal close evidence…";
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusText = "Fiscal close request cancelled. Review the retained evidence before retrying.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateEvidence();
        }
    }

    private void InvalidateScope()
    {
        _plan = null;
        _latest = null;
        _retainedView = null;
        _pendingCommand = null;
        Reason = string.Empty;
        StatusText = "Review the selected book and fiscal year.";
        UpdateEvidence();
    }

    private void UpdateEvidence()
    {
        var priorSection = SelectedSection?.Label;
        BalanceSections = DisplayedPlan is { IsReady: true } plan
            ? [
                CreateSection("Pre-close trial balance", plan.PreClosingTrialBalance),
                CreateSection("Posted closing transfers", plan.PostedClosingTransfers),
                CreateSection("Proposed closing transfers", plan.ProposedClosingTransfers),
                CreateSection("Post-close trial balance", plan.PostClosingTrialBalance),
                CreateSection("Next-year opening balances", plan.NextYearOpeningBalances)
            ] : [];
        SelectedSection = BalanceSections.FirstOrDefault(section => section.Label == priorSection) ?? BalanceSections.FirstOrDefault();
        OnPropertyChanged(nameof(DisplayedReceipt));
        OnPropertyChanged(nameof(DisplayedPlan));
        OnPropertyChanged(nameof(CloseActionLabel));
        OnPropertyChanged(nameof(EvidenceSummary));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        LoadBooksCommand.NotifyCanExecuteChanged();
        ReviewCommand.NotifyCanExecuteChanged();
        FinalizeCommand.NotifyCanExecuteChanged();
        ViewRetainedCommand.NotifyCanExecuteChanged();
        ShowPredecessorCommand.NotifyCanExecuteChanged();
        ReturnToCurrentCommand.NotifyCanExecuteChanged();
    }

    private static FiscalCloseBalanceSection CreateSection(string label, IReadOnlyList<FiscalYearCloseBalanceDto> rows) =>
        new(label, rows.Select(row => new FiscalCloseBalanceRow(
            row.AccountName,
            string.Join(" · ", new[] { row.Symbol, row.FinancialAccountId, row.Dimensions?.EntityId, row.Dimensions?.BookId }
                .Where(value => !string.IsNullOrWhiteSpace(value))),
            Format(row.DebitTotal),
            Format(row.CreditTotal),
            Format(row.Balance))).ToArray());

    private static string Format(decimal amount) => amount.ToString("#,##0.############################", CultureInfo.CurrentCulture);
}

public sealed record FiscalCloseBalanceSection(string Label, IReadOnlyList<FiscalCloseBalanceRow> Rows);

public sealed record FiscalCloseBalanceRow(string Account, string Scope, string Debit, string Credit, string Balance);
