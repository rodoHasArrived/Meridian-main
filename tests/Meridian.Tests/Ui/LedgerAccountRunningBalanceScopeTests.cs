using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Tests.Ui;

public sealed class LedgerAccountRunningBalanceScopeTests
{
    private static readonly Guid PeriodId = Guid.NewGuid();
    private static readonly Guid BookId = Guid.NewGuid();

    [Fact]
    public void BuildAccountRunningBalance_TreatsAnOmittedFilterAsAnyAndAnEmptyFilterAsUnscoped()
    {
        // The same account name posted once without a financial account and once with one: the
        // trial balance shows two rows, and each must drill down to its own postings.
        var entries = new[]
        {
            Record(1, "Unscoped receipt", financialAccountId: null, amount: 100m),
            Record(2, "Account 1000 receipt", financialAccountId: "1000", amount: 40m)
        };

        var any = LedgerEndpoints.BuildAccountRunningBalance(entries, PeriodId, BookId, "Cash", symbol: null, financialAccountId: null);
        var unscoped = LedgerEndpoints.BuildAccountRunningBalance(entries, PeriodId, BookId, "Cash", symbol: "", financialAccountId: "");
        var scoped = LedgerEndpoints.BuildAccountRunningBalance(entries, PeriodId, BookId, "Cash", symbol: null, financialAccountId: "1000");

        any.EndingBalance.Should().Be(140m);
        unscoped.Points.Select(point => point.Description).Should().Equal("Unscoped receipt");
        unscoped.EndingBalance.Should().Be(100m);
        unscoped.FinancialAccountId.Should().BeNull();
        scoped.Points.Select(point => point.Description).Should().Equal("Account 1000 receipt");
        scoped.EndingBalance.Should().Be(40m);
    }

    [Fact]
    public void BuildAccountRunningBalance_LimitsPostingsToTheSelectedRowsDimensionSet()
    {
        // One account posted in two sleeves and once with no dimensions: the period trial balance
        // shows three rows, each stamped with the book's fund.
        var entries = new[]
        {
            Record(1, "Sleeve 1 receipt", null, 100m, new LedgerLineDimensionSet(SleeveId: "sleeve-1")),
            Record(2, "Sleeve 2 receipt", null, 40m, new LedgerLineDimensionSet(SleeveId: "sleeve-2")),
            Record(3, "Undimensioned receipt", null, 7m)
        };

        LedgerAccountRunningBalanceDto Build(LedgerEndpoints.RunningBalanceDimensionScope? scope) =>
            LedgerEndpoints.BuildAccountRunningBalance(entries, PeriodId, BookId, "Cash", null, null, scope, "alpha-fund");

        Build(null).EndingBalance.Should().Be(147m, "no dimension scope means every dimension set of the account");

        var sleeveOne = Build(new(new LedgerDimensionSetDto(FundId: "alpha-fund", SleeveId: "sleeve-1")));
        sleeveOne.Points.Select(point => point.Description).Should().Equal("Sleeve 1 receipt");
        sleeveOne.EndingBalance.Should().Be(100m);

        // The undimensioned row reaches the client as "fund only" after the book's fund is stamped;
        // a client holding no dimensions at all sends null. Both select the same postings.
        Build(new(new LedgerDimensionSetDto(FundId: "alpha-fund"))).Points.Select(point => point.Description)
            .Should().Equal("Undimensioned receipt");
        Build(new(null)).Points.Select(point => point.Description).Should().Equal("Undimensioned receipt");

        Build(new(new LedgerDimensionSetDto(FundId: "alpha-fund", SleeveId: "sleeve-9"))).Points.Should().BeEmpty();
    }

    [Fact]
    public void TryParseRunningBalanceDimensionScope_DistinguishesAbsentNullAndInvalid()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

        LedgerEndpoints.TryParseRunningBalanceDimensionScope(null, options, out var absent).Should().BeTrue();
        absent.Should().BeNull();

        LedgerEndpoints.TryParseRunningBalanceDimensionScope("null", options, out var explicitNull).Should().BeTrue();
        explicitNull.Should().NotBeNull();
        explicitNull!.Dimensions.Should().BeNull();

        LedgerEndpoints.TryParseRunningBalanceDimensionScope(
            """{"fundId":"alpha-fund","sleeveId":"sleeve-1","externalGlDimensions":{"Department":"Ops"}}""",
            options,
            out var parsed).Should().BeTrue();
        parsed!.Dimensions!.SleeveId.Should().Be("sleeve-1");
        parsed.Dimensions.ExternalGlDimensions["Department"].Should().Be("Ops");

        LedgerEndpoints.TryParseRunningBalanceDimensionScope("{not json", options, out _).Should().BeFalse();
    }

    private static LedgerJournalEntryRecord Record(
        long sequence,
        string description,
        string? financialAccountId,
        decimal amount,
        LedgerLineDimensionSet? dimensions = null)
    {
        var journalEntryId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(sequence);
        var entry = new JournalEntry(
            journalEntryId,
            timestamp,
            description,
            [
                new LedgerEntry(Guid.NewGuid(), journalEntryId, timestamp,
                    new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: financialAccountId), amount, 0m, description, dimensions),
                new LedgerEntry(Guid.NewGuid(), journalEntryId, timestamp,
                    new LedgerAccount("Capital", LedgerAccountType.Equity), 0m, amount, description)
            ]);
        return new LedgerJournalEntryRecord(
            entry, Guid.NewGuid(), PeriodId, null, null, sequence, timestamp,
            AccountingBasisKindDto.Primary, "legacy-v1", "legacy-v1", null, null, null, null,
            LedgerPostingKindDto.Originating, null);
    }
}
