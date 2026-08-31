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
        var masterPeriodId = Guid.NewGuid();
        var feederPeriodId = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            // Named so the master book is the one an initial load lands on: books are ordered by
            // display name, so a feeder that sorted first would make the switch below a no-op and
            // the test would pass without ever leaving the default.
            Books = ApiResponse<List<LedgerBookDto>>.Ok(
                [Book(DefaultBookId, "Alpha Master Fund"), Book(FeederBookId, "Beta Feeder Fund", "EUR")]),
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([
                Period(masterPeriodId),
                Period(feederPeriodId, periodNo: 6, label: "June 2026", ledgerBookId: FeederBookId)
            ]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([Line("Cash", 100m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(masterPeriodId))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();
        viewModel.SelectedBookId.Should().Be(DefaultBookId, "the initial load takes the first book in display order");

        await viewModel.SelectBookAsync(FeederBookId);
        viewModel.SelectedBookId.Should().Be(FeederBookId);
        viewModel.SelectedPeriodId.Should().Be(feederPeriodId);

        // Refresh is a reload of what is on screen, not a reset of the subject under review.
        await viewModel.RefreshAsync();

        viewModel.SelectedBookId.Should().Be(FeederBookId);
        viewModel.SelectedBookLabel.Should().Be("Beta Feeder Fund");
        viewModel.SelectedPeriodId.Should().Be(feederPeriodId);
    }

    [Fact]
    public async Task SelectBookAsync_ClearsTheOutgoingBooksCachedBasisLines()
    {
        var periodId = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            // The period below belongs to the master book, so the master book has to be the one
            // the initial load selects -- periods are filtered to the selected book, and a feeder
            // that sorted first would leave the first load with nothing to show.
            Books = ApiResponse<List<LedgerBookDto>>.Ok(
                [Book(DefaultBookId, "Alpha Master Fund"), Book(FeederBookId, "Beta Feeder Fund", "EUR")]),
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(periodId)]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([Line("Cash", 100m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(periodId))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();
        viewModel.SelectedBookId.Should().Be(DefaultBookId);
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

    /// <summary>
    /// A refresh reloads what is on screen; it does not change the subject. Keeping the book but
    /// resetting to the latest closed period moved an operator reviewing June onto July with no
    /// indication that the figures beneath them had changed.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_KeepsTheOperatorsSelectedPeriod_RatherThanReturningToTheLatest()
    {
        var latest = Guid.NewGuid();
        var prior = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([
                Period(latest),
                Period(prior, periodNo: 6, label: "June 2026")
            ]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([Line("Cash", 100m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(latest))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();
        viewModel.SelectedPeriodId.Should().Be(latest);

        await viewModel.SelectPeriodAsync(prior);
        await viewModel.RefreshAsync();

        viewModel.SelectedPeriodId.Should().Be(prior);
        viewModel.SelectedPeriodLabel.Should().Be("June 2026");
    }

    /// <summary>
    /// Checked against the book's own periods, not merely carried: a period that has been reopened
    /// or removed must not leave the surface pointed at nothing.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_WhenTheSelectedPeriodIsGone_FallsBackToTheDefault()
    {
        var latest = Guid.NewGuid();
        var prior = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([
                Period(latest),
                Period(prior, periodNo: 6, label: "June 2026")
            ]),
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([Line("Cash", 100m)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(latest))
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();
        await viewModel.SelectPeriodAsync(prior);

        client.Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(latest)]);
        await viewModel.RefreshAsync();

        viewModel.SelectedPeriodId.Should().Be(latest);
    }

    /// <summary>
    /// The endpoint's totals sum every basis the period holds. Rendering them beside a grid
    /// filtered to one basis put a GAAP trial balance next to a P&amp;L that had added Primary and
    /// GAAP revenue together — and made the desktop disagree with the browser workstation, which
    /// scopes the same figures through the same projection.
    /// </summary>
    [Fact]
    public async Task ThePnlIsScopedToTheSelectedBasis_AndReprojectsWhenItChanges()
    {
        var periodId = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([
                Line("Cash", 100m),
                Line("Cash", 90m) with { AccountingBasis = AccountingBasisKindDto.Gaap }
            ]),
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(periodId)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(periodId) with
            {
                RevenueLines =
                [
                    Line("Management fee", 500m) with { AccountType = "Revenue" },
                    Line("Management fee", 400m) with { AccountType = "Revenue", AccountingBasis = AccountingBasisKindDto.Gaap }
                ],
                ExpenseLines =
                [
                    Line("Audit fee", 200m) with { AccountType = "Expense" },
                    Line("Audit fee", 100m) with { AccountType = "Expense", AccountingBasis = AccountingBasisKindDto.Gaap }
                ]
            })
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();

        viewModel.SelectedBasis.Should().Be(AccountingBasisKindDto.Primary);
        Metric(viewModel, "Total revenue").Should().Contain("500");
        Metric(viewModel, "Net income").Should().Contain("300");

        // Switching basis re-projects the P&L with the grid. Leaving it behind showed GAAP
        // balances beside the previous basis's net income.
        viewModel.SelectedBasisRow = viewModel.Bases.Single(basis => basis.Basis == AccountingBasisKindDto.Gaap);

        Metric(viewModel, "Total revenue").Should().Contain("400");
        Metric(viewModel, "Total expenses").Should().Contain("100");
        Metric(viewModel, "Net income").Should().Contain("300");
    }

    /// <summary>
    /// The variance is a period-level figure the endpoint derives across every basis and cannot be
    /// split, so it is carried through unchanged — and labelled, rather than left to read as the
    /// selected basis's own beside totals that are.
    /// </summary>
    [Fact]
    public async Task TheVarianceIsLabelledCrossBasisOnAMixedPeriod()
    {
        var periodId = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([
                Line("Cash", 100m),
                Line("Cash", 90m) with { AccountingBasis = AccountingBasisKindDto.Gaap }
            ]),
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(periodId)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(periodId) with { PeriodOnPeriodVariance = 150m })
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();

        viewModel.Bases.Should().HaveCount(2);
        Metric(viewModel, "Period-on-period variance").Should().Contain("all bases");
    }

    /// <summary>
    /// A single-basis period has nothing to disclose: the endpoint's figures are the selected
    /// basis's figures, and saying otherwise is noise on the surface an operator signs off from.
    /// </summary>
    [Fact]
    public async Task ASingleBasisPeriodCarriesNoBasisCaveats()
    {
        var periodId = Guid.NewGuid();
        var client = new FakeLedgerReportsApiClient
        {
            TrialBalance = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([Line("Cash", 100m)]),
            Periods = ApiResponse<List<LedgerPeriodDto>>.Ok([Period(periodId)]),
            Pnl = ApiResponse<LedgerPeriodPnlSummaryDto>.Ok(Pnl(periodId) with { PeriodOnPeriodVariance = 150m })
        };

        using var viewModel = new PostedLedgerViewModel(client);
        await viewModel.RefreshAsync();

        Metric(viewModel, "Period-on-period variance").Should().NotContain("all bases");
        viewModel.PnlMetrics.Should().NotContain(metric => metric.Label == "Basis scope");
    }

    private static string Metric(PostedLedgerViewModel viewModel, string label)
        => viewModel.PnlMetrics.Single(metric => metric.Label == label).Value;

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
