using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="PostedLedgerViewModel"/> — the desktop posted-journal surface.
/// <para>
/// These run only on Windows: the WPF test assembly needs <c>Microsoft.WindowsDesktop.App</c>,
/// which has no Linux build. The projection decisions this view model delegates to
/// (<c>PostedLedgerProjection</c>) are covered on every platform by
/// <c>Meridian.Ui.Tests.Services.PostedLedgerProjectionTests</c>; what is asserted here is the
/// view model's own wiring — degradation, notice-versus-error, and selection.
/// </para>
/// </summary>
public sealed class PostedLedgerViewModelTests
{
    private static readonly Guid DefaultBookId = Guid.Parse("0000000a-0000-0000-0000-00000000000b");
    private static readonly Guid FeederBookId = Guid.Parse("0000000e-0000-0000-0000-00000000000f");

    private static LedgerBookDto Book(
        Guid ledgerBookId,
        string displayName = "Master Fund",
        string baseCurrency = "USD")
        => new(
            LedgerBookId: ledgerBookId,
            FundProfileId: "fund-alpha",
            FundStructureNodeId: Guid.Parse("0000000c-0000-0000-0000-00000000000d"),
            FundStructureNodeKind: FundStructureNodeKindDto.Fund,
            DisplayName: displayName,
            BaseCurrency: baseCurrency,
            CreatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    private static LedgerPeriodDto Period(
        Guid periodId,
        int periodNo = 7,
        string label = "July 2026",
        LedgerPeriodStatusDto status = LedgerPeriodStatusDto.HardClosed,
        Guid? ledgerBookId = null)
        => new(
            PeriodId: periodId,
            LedgerBookId: ledgerBookId ?? DefaultBookId,
            FiscalYear: 2026,
            PeriodNo: periodNo,
            Label: label,
            StartDate: new DateOnly(2026, 7, 1),
            EndDate: new DateOnly(2026, 7, 31),
            Status: status,
            OpenedAt: DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            ClosedAt: DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            Version: 1);

    private static LedgerPeriodTrialBalanceLineDto Line(string accountName, decimal balance)
        => new(
            AccountName: accountName,
            AccountType: "Asset",
            Symbol: null,
            FinancialAccountId: "1000",
            DebitTotal: balance > 0 ? balance : 0m,
            CreditTotal: balance < 0 ? -balance : 0m,
            Balance: balance,
            EntryCount: 3);

    private static LedgerPeriodPnlSummaryDto Pnl(Guid periodId)
        => new(
            PeriodId: periodId,
            LedgerBookId: Guid.NewGuid(),
            FiscalYear: 2026,
            PeriodNo: 7,
            Label: "July 2026",
            TotalRevenue: 5000m,
            TotalExpenses: 1800m,
            NetIncome: 3200m,
            PeriodOnPeriodVariance: null,
            OpenBreakCount: 0,
            SignoffStatus: LedgerPeriodSignoffStatusDto.SignedOff,
            CompletedAt: DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            RevenueLines: [],
            ExpenseLines: []);

    [Fact]
    public async Task RefreshAsync_LoadsTheLatestClosedPeriodAndItsPostedBook()
    {
        var periodId = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(periodId)]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok(
                [Line("Cash", 120500m), Line("Financing payable", -120500m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(periodId))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();

        viewModel.SelectedPeriodId.Should().Be(periodId);
        viewModel.SelectedPeriodLabel.Should().Be("July 2026");
        viewModel.TrialBalance.Should().HaveCount(2);
        viewModel.PnlMetrics.Should().NotBeEmpty();
        viewModel.SignoffText.Should().Be("Signed off");
        viewModel.IsOutOfBalance.Should().BeFalse();
        viewModel.HasPeriodsError.Should().BeFalse();
        viewModel.HasTrialBalanceError.Should().BeFalse();
        viewModel.HasPnlError.Should().BeFalse();
        viewModel.HasPeriodNotice.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_KeepsTheOperatorsSelectedBook_RatherThanReturningToTheDefault()
    {
        var periodId = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Books = ApiResponse<List<LedgerBookDto>>.Ok(
                [Book(DefaultBookId), Book(FeederBookId, "Feeder Fund", "EUR")]),
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(periodId, ledgerBookId: FeederBookId)]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([Line("Cash", 100m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(periodId))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();
        await viewModel.SelectBookAsync(FeederBookId);
        viewModel.SelectedBookId.Should().Be(FeederBookId);

        // Refresh is a reload of what is on screen, not a reset of the subject under review.
        await viewModel.RefreshAsync();

        viewModel.SelectedBookId.Should().Be(FeederBookId);
        viewModel.SelectedBookLabel.Should().Be("Feeder Fund");
    }

    [Fact]
    public async Task SelectBookAsync_ClearsTheOutgoingBooksCachedBasisLines()
    {
        var periodId = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Books = ApiResponse<List<LedgerBookDto>>.Ok(
                [Book(DefaultBookId), Book(FeederBookId, "Feeder Fund", "EUR")]),
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(periodId)]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([Line("Cash", 100m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(periodId))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();
        viewModel.Bases.Should().NotBeEmpty("the first book must load for this to test the switch");

        // No periods for the incoming book: the picker and its cached lines must not survive the
        // switch, or choosing a basis re-projects book A's balances under book B.
        client.Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([]);
        await viewModel.SelectBookAsync(FeederBookId);

        viewModel.Bases.Should().BeEmpty();
        viewModel.TrialBalance.Should().BeEmpty();
    }

    [Fact]
    public async Task Deactivate_LeavesThePageAbleToLoadAgainOnBackNavigation()
    {
        var periodId = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(periodId)]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([Line("Cash", 100m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(periodId))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();

        // Navigating away used to Dispose, which made the same instance permanently inert when the
        // Frame restored it from history.
        viewModel.Deactivate();
        await viewModel.RefreshAsync();

        viewModel.TrialBalance.Should().NotBeEmpty("a deactivated page must still load when navigated back to");
        viewModel.SelectedPeriodId.Should().Be(periodId);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheBooksRequestFailsAfterALoad_DropsThePreviousBooksFigures()
    {
        var periodId = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(periodId)]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok(
                [Line("Cash", 120500m), Line("Financing payable", -120500m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(periodId))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();
        viewModel.TrialBalance.Should().HaveCount(2, "the first load must succeed for this to test a refresh");

        // The operator refreshes and the books request fails. The pickers and the book label go
        // away, so leaving the balances behind would render them unlabelled -- and the next book
        // the operator selects would appear to own them.
        client.Books = ApiResponse<List<LedgerBookDto>>.Fail("ledger service unavailable", 503);
        await viewModel.RefreshAsync();

        viewModel.HasPeriodsError.Should().BeTrue();
        viewModel.TrialBalance.Should().BeEmpty();
        viewModel.PnlMetrics.Should().BeEmpty();
        viewModel.Periods.Should().BeEmpty();
        viewModel.SelectedPeriodId.Should().BeNull();
        viewModel.SelectedBookId.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_WithoutAClient_ReportsUnavailableRatherThanAnEmptyBook()
    {
        using var viewModel = new PostedLedgerViewModel();

        await viewModel.RefreshAsync();

        viewModel.HasPeriodsError.Should().BeTrue("an absent client must not render as a book with no accounts");
        viewModel.TrialBalance.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshAsync_WhenThePeriodHasNoClosedSummary_ShowsANoticeNotAnError()
    {
        var periodId = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(periodId, status: LedgerPeriodStatusDto.Open)]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Fail("not found", 404),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Fail("not found", 404)
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();

        viewModel.HasPeriodNotice.Should().BeTrue();
        viewModel.HasTrialBalanceError.Should().BeFalse("an open period is a state, not an outage");
        viewModel.HasPnlError.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_WhenTheTrialBalanceFails_ReportsAnError()
    {
        var periodId = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(periodId)]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Fail("ledger service unavailable", 503),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(periodId))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();

        viewModel.HasTrialBalanceError.Should().BeTrue();
        viewModel.HasPeriodNotice.Should().BeFalse();
        viewModel.TrialBalance.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshAsync_FlagsAPostedBookThatDoesNotTie()
    {
        var periodId = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(periodId)]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok(
                [Line("Cash", 120500m), Line("Financing payable", -120000m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(periodId))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();

        viewModel.IsOutOfBalance.Should().BeTrue();
        viewModel.BalanceSummaryText.Should().Contain("out by");
    }

    [Fact]
    public async Task RefreshAsync_WithNoPeriods_ExplainsHowToStartTheBook()
    {
        var client = new FakeLedgerReportsApiClient
        {
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([])
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();

        viewModel.Periods.Should().BeEmpty();
        viewModel.StatusText.Should().Contain("No ledger periods");
        viewModel.HasPeriodsError.Should().BeFalse();
    }

    [Fact]
    public async Task SelectPeriodAsync_SwitchesTheSubjectPeriod()
    {
        var latest = Guid.NewGuid();
        var prior = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok(
                [Period(latest), Period(prior, periodNo: 6, label: "June 2026")]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([Line("Cash", 10m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(latest))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();
        await viewModel.SelectPeriodAsync(prior);

        viewModel.SelectedPeriodId.Should().Be(prior);
        viewModel.SelectedPeriodLabel.Should().Be("June 2026");
        client.RequestedPeriodIds.Should().Contain(prior);
    }

    [Fact]
    public async Task RefreshScopesThePeriodRequestToTheSelectedBook()
    {
        var latest = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(latest)]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([Line("Cash", 10m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(latest))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();

        // An unscoped request returns every book's periods, and nothing in a period names its
        // book, so the surface could present another fund's closed period as this fund's.
        client.RequestedBookScopes.Should().NotContain((Guid?)null);
        client.RequestedBookScopes.Should().Contain(DefaultBookId);
        viewModel.SelectedBookId.Should().Be(DefaultBookId);
        viewModel.SelectedBookLabel.Should().Be("Master Fund");
    }

    [Fact]
    public async Task PeriodsBelongingToAnotherBookAreNotShown()
    {
        var mine = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            // A server that ignores the ledgerBookId query hands back both books' periods.
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([
                Period(mine),
                Period(foreign, periodNo: 6, label: "June 2026", ledgerBookId: Guid.NewGuid())
            ]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([Line("Cash", 10m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(mine))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();

        viewModel.Periods.Select(row => row.PeriodId).Should().ContainSingle().Which.Should().Be(mine);
    }

    [Fact]
    public async Task AmountsAreFormattedInTheBooksCurrencyNotTheMachines()
    {
        var latest = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Books = ApiResponse<List<LedgerBookDto>>.Ok([Book(DefaultBookId, baseCurrency: "USD")]),
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(latest)]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([Line("Cash", 1250m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(latest))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();

        viewModel.BaseCurrency.Should().Be("USD");
        viewModel.TrialBalance.Should().ContainSingle()
            .Which.BalanceLabel.Should().Contain("USD");
    }

    [Fact]
    public async Task SettingTheSelectedPeriodRowLoadsThatPeriod()
    {
        var latest = Guid.NewGuid();
        var prior = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok(
                [Period(latest), Period(prior, periodNo: 6, label: "June 2026")]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([Line("Cash", 10m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(latest))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();

        // This is what the list's SelectedItem binding does. Before it was wired, moving the
        // highlight loaded nothing and every period but the default was unreachable.
        viewModel.SelectedPeriodRow = viewModel.Periods.Single(row => row.PeriodId == prior);
        // The setter fires the command rather than awaiting it; await the command's own task so
        // the assertion cannot race a continuation.
        await (viewModel.SelectPeriodCommand.ExecutionTask ?? Task.CompletedTask);

        viewModel.SelectedPeriodId.Should().Be(prior);
        client.RequestedPeriodIds.Should().Contain(prior);
    }

    private sealed class FakeLedgerReportsApiClient : ILedgerReportsApiClient
    {
        public ApiResponse<List<LedgerPeriodDto>> Periods { get; set; }
            = ApiResponse<List<LedgerPeriodDto>>.Ok([]);

        public ApiResponse<List<LedgerPeriodTrialBalanceLineDto>> TrialBalance { get; set; }
            = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([]);

        public ApiResponse<LedgerPeriodPnlSummaryDto> Pnl { get; set; }
            = ApiResponse<LedgerPeriodPnlSummaryDto>.Fail("not configured", 404);

        public ApiResponse<List<LedgerBookDto>> Books { get; set; }
            = ApiResponse<List<LedgerBookDto>>.Ok([Book(DefaultBookId)]);

        public List<Guid> RequestedPeriodIds { get; } = [];

        /// <summary>Records the book scope each periods request carried, or null for an unscoped one.</summary>
        public List<Guid?> RequestedBookScopes { get; } = [];

        public Task<ApiResponse<List<LedgerBookDto>>> GetBooksAsync(CancellationToken ct = default)
            => Task.FromResult(Books);

        public Task<ApiResponse<List<LedgerPeriodDto>>> GetPeriodsAsync(
            Guid? ledgerBookId,
            CancellationToken ct = default)
        {
            RequestedBookScopes.Add(ledgerBookId);
            return Task.FromResult(Periods);
        }

        public Task<ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>> GetTrialBalanceAsync(
            Guid periodId,
            CancellationToken ct = default)
        {
            RequestedPeriodIds.Add(periodId);
            return Task.FromResult(TrialBalance);
        }

        public Task<ApiResponse<LedgerPeriodPnlSummaryDto>> GetPnlSummaryAsync(
            Guid periodId,
            CancellationToken ct = default)
            => Task.FromResult(Pnl);
    }
}
