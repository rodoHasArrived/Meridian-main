using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="PostedLedgerProjection"/> — the posted-journal ledger surface's
/// presentation decisions. These live in the <c>net10.0</c> lane on purpose: the desktop
/// workstation consumes the same functions but cannot execute tests off Windows, so this is
/// where the behaviour is actually proven.
/// </summary>
public sealed class PostedLedgerProjectionTests
{
    private static LedgerPeriodDto Period(
        int fiscalYear = 2026,
        int periodNo = 7,
        string label = "July 2026",
        LedgerPeriodStatusDto status = LedgerPeriodStatusDto.HardClosed,
        Guid? periodId = null)
        => new(
            PeriodId: periodId ?? Guid.NewGuid(),
            LedgerBookId: Guid.NewGuid(),
            FiscalYear: fiscalYear,
            PeriodNo: periodNo,
            Label: label,
            StartDate: new DateOnly(fiscalYear, Math.Max(periodNo, 1), 1),
            EndDate: new DateOnly(fiscalYear, Math.Max(periodNo, 1), 28),
            Status: status,
            OpenedAt: DateTimeOffset.UnixEpoch,
            ClosedAt: null,
            Version: 1);

    private static LedgerPeriodTrialBalanceLineDto Line(
        string accountName = "Cash",
        string accountType = "Asset",
        decimal balance = 100m,
        AccountingBasisKindDto basis = AccountingBasisKindDto.Primary,
        string? symbol = null,
        string? financialAccountId = "1000")
        => new(
            AccountName: accountName,
            AccountType: accountType,
            Symbol: symbol,
            FinancialAccountId: financialAccountId,
            DebitTotal: balance > 0 ? balance : 0m,
            CreditTotal: balance < 0 ? -balance : 0m,
            Balance: balance,
            EntryCount: 1,
            AccountingBasis: basis);

    // ── Period ordering and default subject ──────────────────────────

    [Fact]
    public void SortPeriodsDescending_OrdersNewestFirst()
    {
        var older = Period(fiscalYear: 2025, periodNo: 12);
        var newest = Period(fiscalYear: 2026, periodNo: 7);
        var middle = Period(fiscalYear: 2026, periodNo: 1);

        var sorted = PostedLedgerProjection.SortPeriodsDescending([older, newest, middle]);

        sorted.Select(static period => period.PeriodId)
            .Should().Equal(newest.PeriodId, middle.PeriodId, older.PeriodId);
    }

    [Fact]
    public void SortPeriodsDescending_WithNull_ReturnsEmpty()
        => PostedLedgerProjection.SortPeriodsDescending(null).Should().BeEmpty();

    [Fact]
    public void ResolveDefaultPeriodId_PrefersTheLatestClosedPeriodOverANewerOpenOne()
    {
        var open = Period(periodNo: 8, status: LedgerPeriodStatusDto.Open);
        var closed = Period(periodNo: 7, status: LedgerPeriodStatusDto.HardClosed);

        PostedLedgerProjection.ResolveDefaultPeriodId([open, closed])
            .Should().Be(closed.PeriodId, "trial balance and P&L publish from the closed-period summary");
    }

    [Fact]
    public void ResolveDefaultPeriodId_WithOnlyOpenPeriods_FallsBackToTheLatest()
    {
        var older = Period(periodNo: 6, status: LedgerPeriodStatusDto.Open);
        var newer = Period(periodNo: 7, status: LedgerPeriodStatusDto.Open);

        PostedLedgerProjection.ResolveDefaultPeriodId([older, newer]).Should().Be(newer.PeriodId);
    }

    [Fact]
    public void ResolveDefaultPeriodId_WithNoPeriods_IsNull()
        => PostedLedgerProjection.ResolveDefaultPeriodId([]).Should().BeNull();

    [Fact]
    public void ResolveDefaultPeriodId_TreatsSoftClosedAsClosed()
    {
        var open = Period(periodNo: 9, status: LedgerPeriodStatusDto.Open);
        var soft = Period(periodNo: 8, status: LedgerPeriodStatusDto.SoftClosed);

        PostedLedgerProjection.ResolveDefaultPeriodId([open, soft]).Should().Be(soft.PeriodId);
    }

    // ── Labels ───────────────────────────────────────────────────────

    [Fact]
    public void DescribePeriod_FallsBackToFiscalCoordinatesWhenUnlabelled()
    {
        PostedLedgerProjection.DescribePeriod(Period(label: "   ")).Should().Be("FY2026 P7");
        PostedLedgerProjection.DescribePeriod(Period(label: "July 2026")).Should().Be("July 2026");
    }

    [Theory]
    [InlineData(LedgerPeriodStatusDto.HardClosed, "Hard closed")]
    [InlineData(LedgerPeriodStatusDto.SoftClosed, "Soft closed")]
    [InlineData(LedgerPeriodStatusDto.Open, "Open")]
    public void DescribePeriodStatus_NamesEachState(LedgerPeriodStatusDto status, string expected)
        => PostedLedgerProjection.DescribePeriodStatus(status).Should().Be(expected);

    [Theory]
    [InlineData(LedgerPeriodSignoffStatusDto.SignedOff, "Signed off")]
    [InlineData(LedgerPeriodSignoffStatusDto.Pending, "Sign-off pending")]
    [InlineData(LedgerPeriodSignoffStatusDto.Rejected, "Sign-off rejected")]
    [InlineData(LedgerPeriodSignoffStatusDto.NotRequired, "Sign-off not required")]
    public void DescribeSignoffStatus_NamesEachState(LedgerPeriodSignoffStatusDto status, string expected)
        => PostedLedgerProjection.DescribeSignoffStatus(status).Should().Be(expected);

    // ── Missing-summary handling ─────────────────────────────────────

    [Fact]
    public void IsMissingSummary_TreatsOnly404AsTheOpenPeriodState()
    {
        PostedLedgerProjection.IsMissingSummary(404).Should().BeTrue();
        PostedLedgerProjection.IsMissingSummary(500).Should().BeFalse();
        PostedLedgerProjection.IsMissingSummary(403).Should().BeFalse();
        PostedLedgerProjection.IsMissingSummary(200).Should().BeFalse();
    }

    // ── Balance integrity ────────────────────────────────────────────

    [Fact]
    public void IsOutOfBalance_FlagsABookThatDoesNotTie()
    {
        PostedLedgerProjection.IsOutOfBalance([Line(balance: 100m), Line(balance: -90m)])
            .Should().BeTrue();
        PostedLedgerProjection.IsOutOfBalance([Line(balance: 100m), Line(balance: -100m)])
            .Should().BeFalse();
    }

    [Fact]
    public void IsOutOfBalance_ToleratesSubHalfCentRounding()
        => PostedLedgerProjection.IsOutOfBalance([Line(balance: 100m), Line(balance: -99.998m)])
            .Should().BeFalse("a rounding tail must not read as a broken book");

    [Fact]
    public void IsOutOfBalance_WithNoLines_IsFalse()
    {
        PostedLedgerProjection.IsOutOfBalance([]).Should().BeFalse();
        PostedLedgerProjection.IsOutOfBalance(null).Should().BeFalse();
    }

    [Fact]
    public void SumBalances_AddsPostedBalances()
        => PostedLedgerProjection.SumBalances([Line(balance: 120500m), Line(balance: -500m)])
            .Should().Be(120000m);

    // ── Basis and account filtering ──────────────────────────────────

    [Fact]
    public void FilterByBasis_KeepsOnlyTheSelectedBasis()
    {
        var lines = new[]
        {
            Line(accountName: "Cash", basis: AccountingBasisKindDto.Primary),
            Line(accountName: "Accrual", basis: AccountingBasisKindDto.Gaap)
        };

        PostedLedgerProjection.FilterByBasis(lines, AccountingBasisKindDto.Primary)
            .Should().ContainSingle().Which.AccountName.Should().Be("Cash");
    }

    [Fact]
    public void MatchesAccountFilter_SearchesNameTypeAccountIdAndSymbol()
    {
        var line = Line(accountName: "Apple Inc.", accountType: "Asset", symbol: "AAPL", financialAccountId: "acct-aapl");

        PostedLedgerProjection.MatchesAccountFilter(line, "apple").Should().BeTrue();
        PostedLedgerProjection.MatchesAccountFilter(line, "AAPL").Should().BeTrue();
        PostedLedgerProjection.MatchesAccountFilter(line, "acct-").Should().BeTrue();
        PostedLedgerProjection.MatchesAccountFilter(line, "asset").Should().BeTrue();
        PostedLedgerProjection.MatchesAccountFilter(line, "financing").Should().BeFalse();
    }

    [Fact]
    public void MatchesAccountFilter_WithEmptyFilter_KeepsEveryLine()
    {
        var line = Line();
        PostedLedgerProjection.MatchesAccountFilter(line, null).Should().BeTrue();
        PostedLedgerProjection.MatchesAccountFilter(line, "   ").Should().BeTrue();
    }
}
