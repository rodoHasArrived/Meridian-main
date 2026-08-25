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
    private CancellationTokenSource _cts = new();
    private bool _isDisposed;
    private bool _hasLoaded;
    private int _loadRevision;
    private int _periodRevision;
    private int _bookRevision;
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
    // Retained so a basis change can re-project the P&L, exactly as it re-projects the grid.
    private LedgerPeriodPnlSummaryDto? _postedPnl;
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
            // The P&L is basis-scoped too, so it re-projects with the grid. Leaving it meant
            // switching to GAAP showed GAAP balances beside the previous basis's net income.
            ProjectPnlMetrics();
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

    /// <summary>
    /// Stands the page down without ending its life: cancels whatever load is in flight and arms a
    /// fresh token so a later <see cref="Activate"/> reloads.
    /// <para>
    /// This is what navigating away means. Disposing there instead looked equivalent but was not —
    /// the shell's <c>Frame</c> can restore this same instance from navigation history, and a
    /// disposed view model comes back permanently inert: <see cref="Activate"/> returns at the
    /// <c>_isDisposed</c> guard and refresh, period and book commands all no-op, so Back landed the
    /// operator on a dead page with stale figures on it.
    /// </para>
    /// </summary>
    public void Deactivate()
    {
        if (_isDisposed)
        {
            return;
        }

        var previous = _cts;
        _cts = new CancellationTokenSource();
        _hasLoaded = false;

        try
        {
            previous.Cancel();
            previous.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down; nothing to cancel.
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
                // A refresh that fails after a good load must not leave the previous book's
                // balances on screen: the pickers and the book label are gone, so the figures
                // would render unlabelled and read as though they belonged to whatever book the
                // operator selects next.
                Books.Clear();
                ClearBookScopedFigures();
                SelectedBookRow = null;
                SelectedBookId = null;
                SelectedBookLabel = "No ledger book selected";
                BaseCurrency = string.Empty;
                PeriodsErrorText = string.IsNullOrWhiteSpace(booksResponse.ErrorMessage)
                    ? "Ledger books could not be loaded."
                    : booksResponse.ErrorMessage;
                StatusText = "Posted journal unavailable.";
                return;
            }

            ApplyBooks(booksResponse.Data);

            // Refresh must not silently change the subject under review. An operator who selected a
            // non-first book and pressed Refresh was returned to the alphabetically first one, and
            // lost the selected period with it. The default is for an initial load, or when the
            // previous selection is no longer in the book list at all.
            var bookId = booksResponse.Data.Any(book => book.LedgerBookId == SelectedBookId)
                ? SelectedBookId
                : PostedLedgerProjection.ResolveDefaultBookId(booksResponse.Data);
            if (bookId is null)
            {
                ClearBookScopedFigures();
                StatusText = "No ledger books exist yet. Create a ledger book and period to start the governed book.";
                return;
            }

            // The same rule as the book above, one level down: keeping the book but resetting to
            // the latest closed period moved an operator reviewing June onto July without saying
            // so. Only carried across a refresh of the same book -- switching books makes the
            // outgoing period meaningless, and the default is right there.
            var preferredPeriodId = bookId == SelectedBookId ? SelectedPeriodId : null;
            await SelectBookAsync(bookId.Value, preferredPeriodId, ct).ConfigureAwait(true);
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

    /// <summary>
    /// Drops every figure that only means something in the context of a particular book and
    /// period. Callers own <see cref="PeriodsErrorText"/> and <see cref="StatusText"/>, because
    /// what to say about the empty state differs between switching books and failing to load them.
    /// </summary>
    private void ClearBookScopedFigures()
    {
        // Invalidate any period load still in flight for the outgoing scope. Clearing the rows is
        // not enough on its own: SelectPeriodAsync stamps its own revision and re-checks it before
        // publishing, so book A's trial balance and P&L could return while book B's period request
        // was still pending, pass that check, and repopulate A's figures under B's label and base
        // currency -- and stay there indefinitely if B's request then failed or hung.
        _periodRevision++;

        Periods.Clear();
        TrialBalance.Clear();
        PnlMetrics.Clear();
        // The basis picker and the lines behind it are book-scoped too. Leaving them meant the
        // picker stayed interactive over the outgoing book's cached lines while the incoming book
        // was still loading, and choosing a basis re-ran ProjectTrialBalance to put book A's
        // balances back on screen under book B's label and currency.
        Bases.Clear();
        _postedLines = [];
        _postedPnl = null;
        SelectedPeriodId = null;
        SelectedPeriodLabel = "No period selected";
        BalanceSummaryText = "Trial balance not loaded.";
    }

    public Task SelectBookAsync(Guid ledgerBookId, CancellationToken ct = default)
        => SelectBookAsync(ledgerBookId, preferredPeriodId: null, ct);

    /// <summary>
    /// Scopes the surface to one ledger book, landing on <paramref name="preferredPeriodId"/> when
    /// that period belongs to the book and on the book's default period otherwise.
    /// </summary>
    private async Task SelectBookAsync(Guid ledgerBookId, Guid? preferredPeriodId, CancellationToken ct)
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
        ClearBookScopedFigures();

        if (_client is null)
        {
            PeriodsErrorText = "The ledger reporting client is not available in this session.";
            return;
        }

        // Switching books quickly leaves both period requests in flight on the shared page token;
        // without a stamp the slower one wins and shows book A's periods under book B's header
        // and currency.
        var revision = ++_bookRevision;
        var response = await _client.GetPeriodsAsync(ledgerBookId, ct).ConfigureAwait(true);
        if (revision != _bookRevision)
        {
            return;
        }

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

        // Checked against the book's own periods, not merely carried: a period that is no longer
        // there -- reopened, deleted, or never this book's -- falls back to the default rather
        // than leaving the surface pointed at nothing.
        var periodId = preferredPeriodId is { } preferred && periods.Any(period => period.PeriodId == preferred)
            ? preferred
            : PostedLedgerProjection.ResolveDefaultPeriodId(periods);
        if (periodId is null)
        {
            StatusText = $"No ledger periods exist yet for {SelectedBookLabel}. Create a period to start the governed book.";
            return;
        }

        await SelectPeriodAsync(periodId.Value, ct).ConfigureAwait(true);
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
        // The label above already reads as the new period, and the grid and metrics are always
        // bound, so the outgoing period's figures must go before the requests start — otherwise
        // they are presented as the new period for the whole load, and forever if it hangs.
        TrialBalance.Clear();
        PnlMetrics.Clear();
        Bases.Clear();
        _postedLines = [];
        _postedPnl = null;
        BalanceSummaryText = "Trial balance not loaded.";
        IsOutOfBalance = false;

        if (_client is null)
        {
            TrialBalanceErrorText = "The ledger reporting client is not available in this session.";
            return;
        }

        // A refresh and a selection (or two selections) share one page-level token, so an older
        // request can complete last and overwrite the period the operator is actually looking at.
        // Stamp each selection and drop any result whose selection has been superseded.
        var revision = ++_periodRevision;

        try
        {
            // Started together, applied separately. Awaiting both before applying either meant a
            // slow or hung P&L request held back a trial balance that had already arrived, leaving
            // both panels blank; a healthy trial balance stays usable during a P&L outage and the
            // other way round. The revision guard is still checked before each apply, so a response
            // for a superseded selection is dropped exactly as before.
            var trialBalanceTask = _client.GetTrialBalanceAsync(periodId, ct);
            var pnlTask = _client.GetPnlSummaryAsync(periodId, ct);

            var trialBalance = await trialBalanceTask.ConfigureAwait(true);
            if (revision != _periodRevision)
            {
                return;
            }

            ApplyTrialBalance(trialBalance);

            var pnl = await pnlTask.ConfigureAwait(true);
            if (revision != _periodRevision)
            {
                return;
            }

            ApplyPnl(pnl);
            StatusText = $"Posted journal for {SelectedBookLabel} · {SelectedPeriodLabel}.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Page navigated away; nothing to report.
        }
        catch (Exception ex)
        {
            if (revision != _periodRevision)
            {
                return;
            }

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
            // The account id and dimensional scope come with the line for a reason: the service
            // returns one row per account per dimension set, so without them a fund-A and a fund-B
            // balance on the same account render as two identical rows.
            TrialBalance.Add(new PostedLedgerTrialBalanceRow(
                line.AccountName,
                line.AccountType,
                line.Symbol,
                PostedLedgerProjection.FormatAmount(line.Balance, BaseCurrency),
                line.EntryCount,
                line.FinancialAccountId,
                PostedLedgerProjection.DescribeDimensionScope(line)));
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
        _postedPnl = null;

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

        _postedPnl = response.Data;
        ProjectPnlMetrics();
        SignoffText = PostedLedgerProjection.DescribeSignoffStatus(response.Data.SignoffStatus);
    }

    /// <summary>
    /// Renders the P&amp;L for the selected basis, through the same projection the browser
    /// workstation uses. The endpoint's totals sum every basis the period holds, so a GAAP trial
    /// balance used to sit beside a P&amp;L that added Primary and GAAP together — and the two
    /// clients disagreed about the same period's revenue.
    /// </summary>
    private void ProjectPnlMetrics()
    {
        PnlMetrics.Clear();
        if (_postedPnl is not { } pnl)
        {
            return;
        }

        var projected = PostedLedgerProjection.ProjectPnl(pnl, SelectedBasis, Bases.Count);
        PnlMetrics.Add(new PostedLedgerMetricRow("Total revenue", PostedLedgerProjection.FormatAmount(projected.TotalRevenue, BaseCurrency)));
        PnlMetrics.Add(new PostedLedgerMetricRow("Total expenses", PostedLedgerProjection.FormatAmount(projected.TotalExpenses, BaseCurrency)));
        PnlMetrics.Add(new PostedLedgerMetricRow("Net income", PostedLedgerProjection.FormatAmount(projected.NetIncome, BaseCurrency)));
        PnlMetrics.Add(new PostedLedgerMetricRow(
            "Period-on-period variance",
            projected.PeriodOnPeriodVariance is { } variance
                ? PostedLedgerProjection.FormatAmount(variance, BaseCurrency)
                    + (projected.IsVarianceBasisScoped ? string.Empty : " (all bases)")
                : "No prior period"));
        PnlMetrics.Add(new PostedLedgerMetricRow(
            "Open breaks",
            pnl.OpenBreakCount.ToString(CultureInfo.CurrentCulture)));

        // A period whose summary carried no revenue or expense line detail leaves nothing to scope
        // by, so the endpoint's cross-basis totals are all there is. Say so rather than presenting
        // them as the selected basis's own.
        if (Bases.Count > 1 && !projected.IsBasisScoped)
        {
            PnlMetrics.Add(new PostedLedgerMetricRow(
                "Basis scope",
                $"Period total across all {Bases.Count} bases, not {PostedLedgerProjection.DescribeBasis(SelectedBasis)} alone"));
        }
    }
}

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

/// <summary>A selectable ledger period in the desktop posted-journal surface.</summary>
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
    int EntryCount,
    string? FinancialAccountId = null,
    string ScopeLabel = "")
{
    /// <summary>Whether this row has a dimensional scope worth showing beside the account.</summary>
    public bool HasScope => !string.IsNullOrEmpty(ScopeLabel);
}

public sealed record PostedLedgerMetricRow(string Label, string Value);
