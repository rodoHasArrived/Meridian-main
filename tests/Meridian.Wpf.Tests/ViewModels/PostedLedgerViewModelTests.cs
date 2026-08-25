using Meridian.Contracts.Api;
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
    private static LedgerPeriodDto Period(
        Guid periodId,
        int periodNo = 7,
        string label = "July 2026",
        LedgerPeriodStatusDto status = LedgerPeriodStatusDto.HardClosed)
        => new(
            PeriodId: periodId,
            LedgerBookId: Guid.NewGuid(),
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

    private sealed class FakeLedgerReportsApiClient : ILedgerReportsApiClient
    {
        public ApiResponse<List<LedgerPeriodDto>> Periods { get; set; }
            = ApiResponse<List<LedgerPeriodDto>>.Ok([]);

        public ApiResponse<List<LedgerPeriodTrialBalanceLineDto>> TrialBalance { get; set; }
            = ApiResponse<List<LedgerPeriodTrialBalanceLineDto>>.Ok([]);

        public ApiResponse<LedgerPeriodPnlSummaryDto> Pnl { get; set; }
            = ApiResponse<LedgerPeriodPnlSummaryDto>.Fail("not configured", 404);

        public List<Guid> RequestedPeriodIds { get; } = [];

        public Task<ApiResponse<List<LedgerPeriodDto>>> GetPeriodsAsync(CancellationToken ct = default)
            => Task.FromResult(Periods);

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
