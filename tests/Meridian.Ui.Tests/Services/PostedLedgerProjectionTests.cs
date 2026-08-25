using System.Globalization;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
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
        Guid? periodId = null,
        Guid? ledgerBookId = null)
        => new(
            PeriodId: periodId ?? Guid.NewGuid(),
            LedgerBookId: ledgerBookId ?? Guid.NewGuid(),
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

    private static LedgerBookDto Book(Guid ledgerBookId, string displayName, string baseCurrency = "USD")
        => new(
            LedgerBookId: ledgerBookId,
            FundProfileId: "fund-alpha",
            FundStructureNodeId: Guid.Parse("0000000c-0000-0000-0000-00000000000d"),
            FundStructureNodeKind: FundStructureNodeKindDto.Fund,
            DisplayName: displayName,
            BaseCurrency: baseCurrency,
            CreatedAt: DateTimeOffset.UnixEpoch,
            UpdatedAt: DateTimeOffset.UnixEpoch);

    [Fact]
    public void SortBooks_OrdersByDisplayNameWithAStableTieBreak()
    {
        var first = Guid.Parse("00000001-0000-0000-0000-000000000000");
        var second = Guid.Parse("00000002-0000-0000-0000-000000000000");
        var books = new[] { Book(second, "Feeder"), Book(first, "Master") };

        PostedLedgerProjection.SortBooks(books).Select(book => book.DisplayName)
            .Should().ContainInOrder("Feeder", "Master");
    }

    [Fact]
    public void ResolveDefaultBookId_PicksTheFirstBookInStableOrder()
    {
        var master = Guid.Parse("00000002-0000-0000-0000-000000000000");
        var feeder = Guid.Parse("00000001-0000-0000-0000-000000000000");

        PostedLedgerProjection.ResolveDefaultBookId([Book(master, "Master"), Book(feeder, "Feeder")])
            .Should().Be(feeder);
    }

    [Fact]
    public void ResolveDefaultBookId_WithNoBooks_IsNull()
        => PostedLedgerProjection.ResolveDefaultBookId([]).Should().BeNull();

    [Fact]
    public void FilterPeriodsByBook_KeepsOnlyTheRequestedBooksPeriods()
    {
        var mine = Guid.Parse("0000000a-0000-0000-0000-000000000000");
        var theirs = Guid.Parse("0000000b-0000-0000-0000-000000000000");
        var periods = new[]
        {
            Period(periodNo: 7, ledgerBookId: mine),
            Period(periodNo: 6, ledgerBookId: theirs),
            Period(periodNo: 5, ledgerBookId: mine)
        };

        PostedLedgerProjection.FilterPeriodsByBook(periods, mine)
            .Should().OnlyContain(period => period.LedgerBookId == mine)
            .And.HaveCount(2);
    }

    [Fact]
    public void FilterPeriodsByBook_WithNoBook_KeepsEveryPeriod()
        => PostedLedgerProjection.FilterPeriodsByBook([Period(periodNo: 7), Period(periodNo: 6)], null)
            .Should().HaveCount(2);

    /// <summary>
    /// The defect this replaced: ToString("C", CurrentCulture) takes the symbol from the operator's
    /// OS locale, so a USD book rendered as pounds under en-GB — the same number wearing another
    /// currency's symbol, with no conversion.
    /// </summary>
    [Fact]
    public void FormatAmount_UsesTheBooksCurrencyCodeRegardlessOfMachineCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            var formatted = PostedLedgerProjection.FormatAmount(1250.5m, "USD");

            formatted.Should().Be("USD 1,250.50");
            formatted.Should().NotContain("£");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FormatAmount_WithNoDeclaredCurrency_ShowsABareNumberRatherThanGuessingASymbol()
    {
        PostedLedgerProjection.FormatAmount(1250.5m, null).Should().Be("1,250.50");
        PostedLedgerProjection.FormatAmount(1250.5m, "  ").Should().Be("1,250.50");
    }

    [Fact]
    public void AvailableBases_ListsEachBasisPresentOnceInEnumOrder()
    {
        var lines = new[]
        {
            Line(basis: AccountingBasisKindDto.Gaap),
            Line(basis: AccountingBasisKindDto.Primary),
            Line(basis: AccountingBasisKindDto.Gaap)
        };

        PostedLedgerProjection.AvailableBases(lines)
            .Should().ContainInOrder(AccountingBasisKindDto.Primary, AccountingBasisKindDto.Gaap)
            .And.HaveCount(2);
    }

    [Fact]
    public void ResolveDefaultBasis_PrefersPrimaryWhenPresent()
        => PostedLedgerProjection.ResolveDefaultBasis([
            Line(basis: AccountingBasisKindDto.Tax),
            Line(basis: AccountingBasisKindDto.Primary)
        ]).Should().Be(AccountingBasisKindDto.Primary);

    /// <summary>
    /// Defaulting to Primary unconditionally renders a period that has no Primary projection as
    /// though it had no trial balance at all.
    /// </summary>
    [Fact]
    public void ResolveDefaultBasis_FallsBackToTheFirstAvailableBasis()
        => PostedLedgerProjection.ResolveDefaultBasis([
            Line(basis: AccountingBasisKindDto.Tax),
            Line(basis: AccountingBasisKindDto.Gaap)
        ]).Should().Be(AccountingBasisKindDto.Gaap);

    [Fact]
    public void ResolveDefaultBasis_WithNoLines_IsPrimary()
        => PostedLedgerProjection.ResolveDefaultBasis([]).Should().Be(AccountingBasisKindDto.Primary);

    /// <summary>
    /// A period's Primary and GAAP projections each tie on their own. Summing them together
    /// reports a variance that does not exist in either book.
    /// </summary>
    [Fact]
    public void FilteringByBasisBeforeTheBalanceCheckAvoidsAFalseVariance()
    {
        var lines = new[]
        {
            Line(accountName: "Cash", balance: 100m, basis: AccountingBasisKindDto.Primary),
            Line(accountName: "Payable", balance: -100m, basis: AccountingBasisKindDto.Primary),
            Line(accountName: "Cash", balance: 90m, basis: AccountingBasisKindDto.Gaap),
            Line(accountName: "Payable", balance: -90m, basis: AccountingBasisKindDto.Gaap)
        };

        PostedLedgerProjection.IsOutOfBalance(
            PostedLedgerProjection.FilterByBasis(lines, AccountingBasisKindDto.Primary)).Should().BeFalse();
        PostedLedgerProjection.IsOutOfBalance(
            PostedLedgerProjection.FilterByBasis(lines, AccountingBasisKindDto.Gaap)).Should().BeFalse();
    }
    /// <summary>
    /// The ledger service returns one row per account per dimension set, so the same account in
    /// two funds arrives as two rows with identical name, type and symbol. Dropping the scope left
    /// desktop operators unable to tell which fund's balance they were signing off.
    /// </summary>
    [Fact]
    public void DescribeDimensionScope_NamesTheDimensionsWidestFirst()
    {
        var line = Line() with
        {
            Dimensions = new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-lux",
                SleeveId: "sleeve-core",
                CostCenterId: "cc-42",
                OrganizationId: "org-1")
        };

        PostedLedgerProjection.DescribeDimensionScope(line)
            .Should().Be("Organization: org-1 · Fund: fund-alpha · Entity: entity-lux · Sleeve: sleeve-core · Cost center: cc-42");
    }

    /// <summary>
    /// Every dimension the contract declares, not a subset: two rows differing only by an omitted
    /// one rendered an identical scope, and a row scoped by an omitted one alone rendered none at
    /// all. Named rather than positional, because with eighteen possible dimensions a bare value
    /// cannot be told from the one beside it.
    /// </summary>
    [Fact]
    public void DescribeDimensionScope_CoversEveryDeclaredDimension()
    {
        var instrumentId = Guid.Parse("00000003-0000-0000-0000-000000000003");
        var positionId = Guid.Parse("00000004-0000-0000-0000-000000000004");
        var line = Line() with
        {
            Dimensions = new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-lux",
                SleeveId: "sleeve-core",
                StrategyId: "strat-1",
                InvestorId: "inv-1",
                CapitalAccountId: "cap-1",
                InstrumentId: instrumentId,
                TaxLotId: "lot-1",
                CostCenterId: "cc-42",
                CounterpartyId: "cp-1",
                OrganizationId: "org-1",
                PortfolioId: "port-1",
                BookId: "book-1",
                AccountId: "acct-1",
                CustomerId: "cust-1",
                VendorId: "vend-1",
                ProjectId: "proj-1")
            {
                PositionId = positionId
            }
        };

        PostedLedgerProjection.DescribeDimensionScope(line).Should().Be(
            "Organization: org-1 · Fund: fund-alpha · Entity: entity-lux · Portfolio: port-1 · Book: book-1 · "
            + "Sleeve: sleeve-core · Strategy: strat-1 · Investor: inv-1 · Capital account: cap-1 · "
            + "Customer: cust-1 · Vendor: vend-1 · Project: proj-1 · Account: acct-1 · "
            + $"Instrument: {instrumentId} · Position: {positionId} · Tax lot: lot-1 · "
            + "Cost center: cc-42 · Counterparty: cp-1");
    }

    /// <summary>
    /// A row scoped only by a dimension the projection used to omit must still name its scope —
    /// otherwise it reads as unscoped and is indistinguishable from every other such row.
    /// </summary>
    [Theory]
    [InlineData("book-1", null, null, null, null, "Book: book-1")]
    [InlineData(null, "acct-1", null, null, null, "Account: acct-1")]
    [InlineData(null, null, "cust-1", null, null, "Customer: cust-1")]
    [InlineData(null, null, null, "vend-1", null, "Vendor: vend-1")]
    [InlineData(null, null, null, null, "proj-1", "Project: proj-1")]
    public void DescribeDimensionScope_NamesAPreviouslyOmittedDimensionOnItsOwn(
        string? bookId,
        string? accountId,
        string? customerId,
        string? vendorId,
        string? projectId,
        string expected)
    {
        var line = Line() with
        {
            Dimensions = new LedgerDimensionSetDto(
                BookId: bookId,
                AccountId: accountId,
                CustomerId: customerId,
                VendorId: vendorId,
                ProjectId: projectId)
        };

        PostedLedgerProjection.DescribeDimensionScope(line).Should().Be(expected);
    }

    [Fact]
    public void DescribeDimensionScope_AppendsExternalGlDimensionsByName()
    {
        var line = Line() with
        {
            Dimensions = new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                ExternalGlDimensions: new Dictionary<string, string> { ["Region"] = "EMEA", ["Desk"] = "Rates" })
        };

        // Operator-defined, so the operator's own key is carried, and ordered so the label is stable.
        PostedLedgerProjection.DescribeDimensionScope(line)
            .Should().Be("Fund: fund-alpha · External Desk: Rates · External Region: EMEA");
    }

    [Fact]
    public void DescribeDimensionScope_WithNoDimensions_IsEmptySoTheColumnStaysBlank()
        => PostedLedgerProjection.DescribeDimensionScope(Line()).Should().BeEmpty();

    // ── P&L basis scoping ────────────────────────────────────────────

    private static LedgerPeriodPnlSummaryDto Pnl(
        decimal totalRevenue = 0m,
        decimal totalExpenses = 0m,
        decimal netIncome = 0m,
        decimal? variance = null,
        IReadOnlyList<LedgerPeriodTrialBalanceLineDto>? revenueLines = null,
        IReadOnlyList<LedgerPeriodTrialBalanceLineDto>? expenseLines = null)
        => new(
            PeriodId: Guid.Parse("00000001-0000-0000-0000-000000000001"),
            LedgerBookId: Guid.Parse("00000002-0000-0000-0000-000000000002"),
            FiscalYear: 2026,
            PeriodNo: 7,
            Label: "July 2026",
            TotalRevenue: totalRevenue,
            TotalExpenses: totalExpenses,
            NetIncome: netIncome,
            PeriodOnPeriodVariance: variance,
            OpenBreakCount: 0,
            SignoffStatus: LedgerPeriodSignoffStatusDto.SignedOff,
            CompletedAt: DateTimeOffset.Parse("2026-08-02T00:00:00Z", CultureInfo.InvariantCulture),
            RevenueLines: revenueLines ?? [],
            ExpenseLines: expenseLines ?? []);

    /// <summary>
    /// The endpoint's totals sum every basis the period holds, so a GAAP trial balance sat beside
    /// a P&amp;L that added Primary and GAAP revenue together.
    /// </summary>
    [Fact]
    public void ProjectPnl_SumsOnlyTheSelectedBasis()
    {
        var summary = Pnl(
            totalRevenue: 900m,
            totalExpenses: 300m,
            netIncome: 600m,
            revenueLines:
            [
                Line("Management fee", "Revenue", 500m),
                Line("Management fee", "Revenue", 400m, AccountingBasisKindDto.Gaap)
            ],
            expenseLines:
            [
                Line("Audit fee", "Expense", 200m),
                Line("Audit fee", "Expense", 100m, AccountingBasisKindDto.Gaap)
            ]);

        var projected = PostedLedgerProjection.ProjectPnl(summary, AccountingBasisKindDto.Gaap, availableBasisCount: 2);

        projected.TotalRevenue.Should().Be(400m);
        projected.TotalExpenses.Should().Be(100m);
        projected.NetIncome.Should().Be(300m);
        projected.IsBasisScoped.Should().BeTrue();
    }

    /// <summary>
    /// A single-basis period must come back with exactly the endpoint's own figures: the same sum
    /// over the same lines, so the scoping is invisible where there is nothing to scope.
    /// </summary>
    [Fact]
    public void ProjectPnl_ReproducesTheEndpointFiguresForASingleBasisPeriod()
    {
        var summary = Pnl(
            totalRevenue: 500m,
            totalExpenses: 200m,
            netIncome: 300m,
            revenueLines: [Line("Management fee", "Revenue", 500m)],
            expenseLines: [Line("Audit fee", "Expense", 200m)]);

        var projected = PostedLedgerProjection.ProjectPnl(summary, AccountingBasisKindDto.Primary, availableBasisCount: 1);

        projected.TotalRevenue.Should().Be(summary.TotalRevenue);
        projected.TotalExpenses.Should().Be(summary.TotalExpenses);
        projected.NetIncome.Should().Be(summary.NetIncome);
        projected.IsVarianceBasisScoped.Should().BeTrue();
    }

    /// <summary>
    /// No line detail leaves nothing to scope by, so the endpoint's cross-basis totals are all
    /// there is -- and the caller has to be told, rather than presenting them as one basis's own.
    /// </summary>
    [Fact]
    public void ProjectPnl_WithNoLineDetail_FallsBackToTheEndpointTotalsAndSaysSo()
    {
        var summary = Pnl(totalRevenue: 900m, totalExpenses: 300m, netIncome: 600m);

        var projected = PostedLedgerProjection.ProjectPnl(summary, AccountingBasisKindDto.Gaap, availableBasisCount: 2);

        projected.TotalRevenue.Should().Be(900m);
        projected.TotalExpenses.Should().Be(300m);
        projected.NetIncome.Should().Be(600m);
        projected.IsBasisScoped.Should().BeFalse();
    }

    /// <summary>
    /// The variance is a period-level figure derived across every basis, so it is carried through
    /// unchanged and flagged as cross-basis whenever the period holds more than one.
    /// </summary>
    [Fact]
    public void ProjectPnl_CarriesTheVarianceThroughAndFlagsItOnAMixedPeriod()
    {
        var summary = Pnl(
            variance: 150m,
            revenueLines: [Line("Management fee", "Revenue", 500m)]);

        var mixed = PostedLedgerProjection.ProjectPnl(summary, AccountingBasisKindDto.Primary, availableBasisCount: 2);
        mixed.PeriodOnPeriodVariance.Should().Be(150m);
        mixed.IsVarianceBasisScoped.Should().BeFalse();

        PostedLedgerProjection.ProjectPnl(summary, AccountingBasisKindDto.Primary, availableBasisCount: 1)
            .IsVarianceBasisScoped.Should().BeTrue();
    }
}
