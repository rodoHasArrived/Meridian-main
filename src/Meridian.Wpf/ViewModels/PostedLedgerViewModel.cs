using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Desktop view model for the posted-journal ledger — the fund's book of record, scoped by
/// ledger period. Mirrors the browser workstation's posted-ledger panel over the same shared API
/// seam, so the two operator lanes cannot disagree about the book (W8-WPF-PARITY-001).
/// <para>
/// Deliberately holds no state of its own beyond what it fetched: the accounting lane's
/// desktop-local JSON stores are not a source of ledger truth, and this surface must never
/// become one.
/// </para>
/// </summary>
public sealed class PostedLedgerViewModel : BindableBase, IDisposable
{
    private readonly ILedgerReportsApiClient? _client;
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;
    private bool _hasLoaded;
    private int _loadRevision;
    private bool _isRefreshing;
    private Guid? _selectedPeriodId;
    private string _selectedPeriodLabel = "No period selected";
    private string _statusText = "Waiting for the posted journal.";
    private string _periodsErrorText = string.Empty;
    private string _trialBalanceErrorText = string.Empty;
    private string _pnlErrorText = string.Empty;
    private string _periodNotice = string.Empty;
    private string _signoffText = string.Empty;
    private string _balanceSummaryText = "Trial balance not loaded.";
    private bool _isOutOfBalance;
    private PostedLedgerPeriodRow? _selectedPeriodRow;
    private PostedLedgerBookRow? _selectedBookRow;
    private Guid? _selectedBookId;
    private string _selectedBookLabel = "No ledger book selected";
    // The book's declared base currency, not the operator's locale. Empty until a book loads,
    // which formats amounts as bare numbers rather than guessing a symbol.
    private string _baseCurrency = string.Empty;
    // The period's posted lines as returned, retained so switching basis re-projects without a
    // refetch. All bases arrive together; they are different projections of the same accounts.
    private IReadOnlyList<LedgerPeriodTrialBalanceLineDto> _postedLines = [];
    private AccountingBasisKindDto _selectedBasis = AccountingBasisKindDto.Primary;
    private PostedLedgerBasisRow? _selectedBasisRow;

    public PostedLedgerViewModel(ILedgerReportsApiClient? client = null)
    {
        _client = client;
        RefreshCommand = new AsyncRelayCommand(
            () => _isDisposed ? Task.CompletedTask : RefreshAsync(_cts.Token),
            () => !IsRefreshing);
        SelectPeriodCommand = new AsyncRelayCommand<PostedLedgerPeriodRow>(
            row => row is null || _isDisposed
                ? Task.CompletedTask
                : SelectPeriodAsync(row.PeriodId, _cts.Token));
        SelectBookCommand = new AsyncRelayCommand<PostedLedgerBookRow>(
            row => row is null || _isDisposed
                ? Task.CompletedTask
                : SelectBookAsync(row.LedgerBookId, _cts.Token));
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand<PostedLedgerPeriodRow> SelectPeriodCommand { get; }

    public IAsyncRelayCommand<PostedLedgerBookRow> SelectBookCommand { get; }

    public ObservableCollection<PostedLedgerBookRow> Books { get; } = [];

    /// <summary>The accounting bases this period actually carries; empty until a period loads.</summary>
    public ObservableCollection<PostedLedgerBasisRow> Bases { get; } = [];

    public ObservableCollection<PostedLedgerPeriodRow> Periods { get; } = [];

    public ObservableCollection<PostedLedgerTrialBalanceRow> TrialBalance { get; } = [];

    public ObservableCollection<PostedLedgerMetricRow> PnlMetrics { get; } = [];

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Guid? SelectedPeriodId
    {
        get => _selectedPeriodId;
        private set => SetProperty(ref _selectedPeriodId, value);
    }

    public string SelectedPeriodLabel
    {
        get => _selectedPeriodLabel;
        private set => SetProperty(ref _selectedPeriodLabel, value);
    }

    public Guid? SelectedBookId
    {
        get => _selectedBookId;
        private set => SetProperty(ref _selectedBookId, value);
    }

    /// <summary>
    /// Two-way bound to the period list. WPF list selection only moves a highlight; loading the
    /// period it names is this setter's job, and without it the picker changed nothing and every
    /// period but the default was unreachable.
    /// </summary>
    public PostedLedgerPeriodRow? SelectedPeriodRow
    {
        get => _selectedPeriodRow;
        set
        {
            if (!SetProperty(ref _selectedPeriodRow, value))
            {
                return;
            }

            // Null arrives when the list is repopulated, and an unchanged id when the view model
            // drove the selection itself; neither is an operator asking for a different period.
            if (value is null || _isDisposed || value.PeriodId == SelectedPeriodId)
            {
                return;
            }

            SelectPeriodCommand.Execute(value);
        }
    }

    /// <summary>Two-way bound to the book list; see <see cref="SelectedPeriodRow"/>.</summary>
    public PostedLedgerBookRow? SelectedBookRow
    {
        get => _selectedBookRow;
        set
        {
            if (!SetProperty(ref _selectedBookRow, value))
            {
                return;
            }

            if (value is null || _isDisposed || value.LedgerBookId == SelectedBookId)
            {
                return;
            }

            SelectBookCommand.Execute(value);
        }
    }

    /// <summary>Names the book on screen, so a multi-book deployment cannot mistake whose journal this is.</summary>
    public string SelectedBookLabel
    {
        get => _selectedBookLabel;
        private set => SetProperty(ref _selectedBookLabel, value);
    }

    public string BaseCurrency
    {
        get => _baseCurrency;
        private set => SetProperty(ref _baseCurrency, value);
    }

    public AccountingBasisKindDto SelectedBasis
    {
        get => _selectedBasis;
        private set => SetProperty(ref _selectedBasis, value);
    }

    /// <summary>
    /// Two-way bound to the basis picker. Stacking Primary alongside a GAAP or tax projection
    /// puts the same account on screen twice and sums both into the balance check, so exactly one
    /// basis is shown at a time.
    /// </summary>
    public PostedLedgerBasisRow? SelectedBasisRow
    {
        get => _selectedBasisRow;
        set
        {
            if (!SetProperty(ref _selectedBasisRow, value))
            {
                return;
            }

            if (value is null || _isDisposed || value.Basis == SelectedBasis)
            {
                return;
            }

            SelectedBasis = value.Basis;
            ProjectTrialBalance();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SignoffText
    {
        get => _signoffText;
        private set => SetProperty(ref _signoffText, value);
    }

    public string BalanceSummaryText
    {
        get => _balanceSummaryText;
        private set => SetProperty(ref _balanceSummaryText, value);
    }

    /// <summary>True when the posted trial balance does not tie — a blocker, not a warning.</summary>
    public bool IsOutOfBalance
    {
        get => _isOutOfBalance;
        private set => SetProperty(ref _isOutOfBalance, value);
    }

    /// <summary>
    /// Explains an expected empty state (an open period with no closed-period summary). Distinct
    /// from the error properties so a period that simply has not closed never reads as an outage.
    /// </summary>
    public string PeriodNotice
    {
        get => _periodNotice;
        private set
        {
            if (SetProperty(ref _periodNotice, value))
            {
                OnPropertyChanged(nameof(HasPeriodNotice));
            }
        }
    }

    public bool HasPeriodNotice => !string.IsNullOrEmpty(PeriodNotice);

    public string PeriodsErrorText
    {
        get => _periodsErrorText;
        private set
        {
            if (SetProperty(ref _periodsErrorText, value))
            {
                OnPropertyChanged(nameof(HasPeriodsError));
            }
        }
    }

    public bool HasPeriodsError => !string.IsNullOrEmpty(PeriodsErrorText);

    public string TrialBalanceErrorText
    {
        get => _trialBalanceErrorText;
        private set
        {
            if (SetProperty(ref _trialBalanceErrorText, value))
            {
                OnPropertyChanged(nameof(HasTrialBalanceError));
            }
        }
    }

    public bool HasTrialBalanceError => !string.IsNullOrEmpty(TrialBalanceErrorText);

    public string PnlErrorText
    {
        get => _pnlErrorText;
        private set
        {
            if (SetProperty(ref _pnlErrorText, value))
            {
                OnPropertyChanged(nameof(HasPnlError));
            }
        }
    }

    public bool HasPnlError => !string.IsNullOrEmpty(PnlErrorText);

    /// <summary>Loads once, when the page is first shown.</summary>
    public void Activate()
    {
        if (_isDisposed || _hasLoaded)
        {
            return;
        }

        _hasLoaded = true;
        _ = RefreshAsync(_cts.Token);
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
            _cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down; nothing to cancel.
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var revision = ++_loadRevision;
        IsRefreshing = true;
        StatusText = "Loading the posted journal.";
        PeriodsErrorText = string.Empty;

        try
        {
            if (_client is null)
            {
                PeriodsErrorText = "The ledger reporting client is not available in this session.";
                StatusText = "Posted journal unavailable.";
                return;
            }

            // Books first: a period means nothing without the book it belongs to, and the
            // period route returns every book's periods unless it is told which book to answer for.
            var booksResponse = await _client.GetBooksAsync(ct).ConfigureAwait(true);
            if (revision != _loadRevision)
            {
                return;
            }

            if (!booksResponse.Success || booksResponse.Data is null)
            {
                Books.Clear();
                Periods.Clear();
                PeriodsErrorText = string.IsNullOrWhiteSpace(booksResponse.ErrorMessage)
                    ? "Ledger books could not be loaded."
                    : booksResponse.ErrorMessage;
                StatusText = "Posted journal unavailable.";
                return;
            }

            ApplyBooks(booksResponse.Data);

            var bookId = PostedLedgerProjection.ResolveDefaultBookId(booksResponse.Data);
            if (bookId is null)
            {
                Periods.Clear();
                TrialBalance.Clear();
                PnlMetrics.Clear();
                StatusText = "No ledger books exist yet. Create a ledger book and period to start the governed book.";
                return;
            }

            await SelectBookAsync(bookId.Value, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Page navigated away; nothing to report.
        }
        catch (Exception ex)
        {
            if (revision == _loadRevision)
            {
                PeriodsErrorText = ex.Message;
                StatusText = "Posted journal unavailable.";
            }
        }
        finally
        {
            if (revision == _loadRevision)
            {
                IsRefreshing = false;
            }
        }
    }

    public async Task SelectBookAsync(Guid ledgerBookId, CancellationToken ct = default)
    {
        SelectedBookId = ledgerBookId;
        var book = Books.FirstOrDefault(row => row.LedgerBookId == ledgerBookId);
        SelectedBookLabel = book?.Label ?? "Selected ledger book";
        if (!ReferenceEquals(_selectedBookRow, book))
        {
            _selectedBookRow = book;
            OnPropertyChanged(nameof(SelectedBookRow));
        }

        BaseCurrency = book?.BaseCurrency ?? string.Empty;
        foreach (var row in Books)
        {
            row.IsSelected = row.LedgerBookId == ledgerBookId;
        }

        PeriodsErrorText = string.Empty;
        // The outgoing book's periods and figures are a different book entirely.
        Periods.Clear();
        TrialBalance.Clear();
        PnlMetrics.Clear();
        SelectedPeriodId = null;
        SelectedPeriodLabel = "No period selected";
        BalanceSummaryText = "Trial balance not loaded.";

        if (_client is null)
        {
            PeriodsErrorText = "The ledger reporting client is not available in this session.";
            return;
        }

        var response = await _client.GetPeriodsAsync(ledgerBookId, ct).ConfigureAwait(true);
        if (!response.Success || response.Data is null)
        {
            PeriodsErrorText = string.IsNullOrWhiteSpace(response.ErrorMessage)
                ? "Ledger periods could not be loaded."
                : response.ErrorMessage;
            StatusText = "Posted journal unavailable.";
            return;
        }

        // Filter defensively as well as scoping the request: an older server that ignores the
        // ledgerBookId query would otherwise hand back every book's periods.
        var periods = PostedLedgerProjection.FilterPeriodsByBook(response.Data, ledgerBookId);
        ApplyPeriods(periods);

        var defaultPeriodId = PostedLedgerProjection.ResolveDefaultPeriodId(periods);
        if (defaultPeriodId is null)
        {
            StatusText = $"{SelectedBookLabel} has no ledger periods yet.";
            return;
        }

        await SelectPeriodAsync(defaultPeriodId.Value, ct).ConfigureAwait(true);
    }

    private void ApplyBooks(IReadOnlyList<LedgerBookDto> books)
    {
        Books.Clear();
        foreach (var book in PostedLedgerProjection.SortBooks(books))
        {
            Books.Add(new PostedLedgerBookRow(
                book.LedgerBookId,
                string.IsNullOrWhiteSpace(book.DisplayName) ? book.LedgerBookId.ToString() : book.DisplayName,
                book.BaseCurrency));
        }
    }

    public async Task SelectPeriodAsync(Guid periodId, CancellationToken ct = default)
    {
        SelectedPeriodId = periodId;
        var selectedRow = Periods.FirstOrDefault(row => row.PeriodId == periodId);
        SelectedPeriodLabel = selectedRow?.Label ?? "Selected period";
        // Assign the backing field: routing through the property would re-enter the setter above.
        if (!ReferenceEquals(_selectedPeriodRow, selectedRow))
        {
            _selectedPeriodRow = selectedRow;
            OnPropertyChanged(nameof(SelectedPeriodRow));
        }

        foreach (var row in Periods)
        {
            row.IsSelected = row.PeriodId == periodId;
        }

        PeriodNotice = string.Empty;
        TrialBalanceErrorText = string.Empty;
        PnlErrorText = string.Empty;

        if (_client is null)
        {
            TrialBalanceErrorText = "The ledger reporting client is not available in this session.";
            return;
        }

        try
        {
            var trialBalanceTask = _client.GetTrialBalanceAsync(periodId, ct);
            var pnlTask = _client.GetPnlSummaryAsync(periodId, ct);
            await Task.WhenAll(trialBalanceTask, pnlTask).ConfigureAwait(true);

            ApplyTrialBalance(await trialBalanceTask.ConfigureAwait(true));
            ApplyPnl(await pnlTask.ConfigureAwait(true));
            StatusText = $"Posted journal for {SelectedBookLabel} · {SelectedPeriodLabel}.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Page navigated away; nothing to report.
        }
        catch (Exception ex)
        {
            TrialBalance.Clear();
            PnlMetrics.Clear();
            TrialBalanceErrorText = ex.Message;
        }
    }

    private void ApplyPeriods(IReadOnlyList<LedgerPeriodDto> periods)
    {
        Periods.Clear();
        foreach (var period in PostedLedgerProjection.SortPeriodsDescending(periods))
        {
            Periods.Add(new PostedLedgerPeriodRow(
                period.PeriodId,
                PostedLedgerProjection.DescribePeriod(period),
                PostedLedgerProjection.DescribePeriodStatus(period.Status),
                $"{period.StartDate:yyyy-MM-dd} to {period.EndDate:yyyy-MM-dd}"));
        }
    }

    private void ApplyTrialBalance(ApiResponse<List<LedgerPeriodTrialBalanceLineDto>> response)
    {
        TrialBalance.Clear();
        _postedLines = [];
        Bases.Clear();

        if (!response.Success || response.Data is null)
        {
            if (PostedLedgerProjection.IsMissingSummary(response.StatusCode))
            {
                PeriodNotice = PostedLedgerProjection.MissingSummaryNotice;
            }
            else
            {
                TrialBalanceErrorText = string.IsNullOrWhiteSpace(response.ErrorMessage)
                    ? "The posted trial balance could not be loaded."
                    : response.ErrorMessage;
            }

            BalanceSummaryText = "Trial balance not loaded.";
            IsOutOfBalance = false;
            return;
        }

        _postedLines = response.Data;
        ApplyBases(response.Data);
        SelectedBasis = PostedLedgerProjection.ResolveDefaultBasis(response.Data);
        SyncSelectedBasisRow();
        ProjectTrialBalance();
    }

    private void ApplyBases(IReadOnlyList<LedgerPeriodTrialBalanceLineDto> lines)
    {
        Bases.Clear();
        foreach (var basis in PostedLedgerProjection.AvailableBases(lines))
        {
            Bases.Add(new PostedLedgerBasisRow(basis, PostedLedgerProjection.DescribeBasis(basis)));
        }
    }

    private void SyncSelectedBasisRow()
    {
        var row = Bases.FirstOrDefault(candidate => candidate.Basis == SelectedBasis);
        foreach (var candidate in Bases)
        {
            candidate.IsSelected = candidate.Basis == SelectedBasis;
        }

        // Backing field: the property setter would re-enter and re-project.
        if (!ReferenceEquals(_selectedBasisRow, row))
        {
            _selectedBasisRow = row;
            OnPropertyChanged(nameof(SelectedBasisRow));
        }
    }

    /// <summary>
    /// Renders exactly the selected basis. The balance check sums the same filtered set: summing
    /// every basis together would add one account's Primary and GAAP projections and report a
    /// variance that does not exist.
    /// </summary>
    private void ProjectTrialBalance()
    {
        var lines = PostedLedgerProjection.FilterByBasis(_postedLines, SelectedBasis);

        TrialBalance.Clear();
        foreach (var line in lines)
        {
            TrialBalance.Add(new PostedLedgerTrialBalanceRow(
                line.AccountName,
                line.AccountType,
                line.Symbol,
                PostedLedgerProjection.FormatAmount(line.Balance, BaseCurrency),
                line.EntryCount));
        }

        IsOutOfBalance = PostedLedgerProjection.IsOutOfBalance(lines);
        var variance = PostedLedgerProjection.SumBalances(lines);
        var basisLabel = PostedLedgerProjection.DescribeBasis(SelectedBasis);
        BalanceSummaryText = IsOutOfBalance
            ? $"{basisLabel} · {TrialBalance.Count} accounts · out by {PostedLedgerProjection.FormatAmount(Math.Abs(variance), BaseCurrency)}"
            : $"{basisLabel} · {TrialBalance.Count} accounts · in balance";
    }

    private void ApplyPnl(ApiResponse<LedgerPeriodPnlSummaryDto> response)
    {
        PnlMetrics.Clear();

        if (!response.Success || response.Data is null)
        {
            if (PostedLedgerProjection.IsMissingSummary(response.StatusCode))
            {
                PeriodNotice = PostedLedgerProjection.MissingSummaryNotice;
            }
            else
            {
                PnlErrorText = string.IsNullOrWhiteSpace(response.ErrorMessage)
                    ? "The posted P&L summary could not be loaded."
                    : response.ErrorMessage;
            }

            SignoffText = string.Empty;
            return;
        }

        var pnl = response.Data;
        PnlMetrics.Add(new PostedLedgerMetricRow("Total revenue", PostedLedgerProjection.FormatAmount(pnl.TotalRevenue, BaseCurrency)));
        PnlMetrics.Add(new PostedLedgerMetricRow("Total expenses", PostedLedgerProjection.FormatAmount(pnl.TotalExpenses, BaseCurrency)));
        PnlMetrics.Add(new PostedLedgerMetricRow("Net income", PostedLedgerProjection.FormatAmount(pnl.NetIncome, BaseCurrency)));
        PnlMetrics.Add(new PostedLedgerMetricRow(
            "Period-on-period variance",
            pnl.PeriodOnPeriodVariance is { } variance
                ? PostedLedgerProjection.FormatAmount(variance, BaseCurrency)
                : "No prior period"));
        PnlMetrics.Add(new PostedLedgerMetricRow(
            "Open breaks",
            pnl.OpenBreakCount.ToString(CultureInfo.CurrentCulture)));

        SignoffText = PostedLedgerProjection.DescribeSignoffStatus(pnl.SignoffStatus);
    }
}

/// <summary>A selectable ledger period in the desktop posted-journal surface.</summary>
/// <summary>
/// One ledger book the operator can scope the posted journal to. Carries the book's declared
/// base currency so amounts are formatted in the book's own currency rather than the machine's.
/// </summary>
public sealed class PostedLedgerBookRow : BindableBase
{
    private bool _isSelected;

    public PostedLedgerBookRow(Guid ledgerBookId, string label, string baseCurrency)
    {
        LedgerBookId = ledgerBookId;
        Label = label;
        BaseCurrency = baseCurrency;
    }

    public Guid LedgerBookId { get; }

    public string Label { get; }

    public string BaseCurrency { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>One accounting basis a period carries, for the basis picker.</summary>
public sealed class PostedLedgerBasisRow : BindableBase
{
    private bool _isSelected;

    public PostedLedgerBasisRow(AccountingBasisKindDto basis, string label)
    {
        Basis = basis;
        Label = label;
    }

    public AccountingBasisKindDto Basis { get; }

    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class PostedLedgerPeriodRow : BindableBase
{
    private bool _isSelected;

    public PostedLedgerPeriodRow(Guid periodId, string label, string statusLabel, string rangeLabel)
    {
        PeriodId = periodId;
        Label = label;
        StatusLabel = statusLabel;
        RangeLabel = rangeLabel;
    }

    public Guid PeriodId { get; }

    public string Label { get; }

    public string StatusLabel { get; }

    public string RangeLabel { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record PostedLedgerTrialBalanceRow(
    string AccountName,
    string AccountType,
    string? Symbol,
    string BalanceLabel,
    int EntryCount);

public sealed record PostedLedgerMetricRow(string Label, string Value);
