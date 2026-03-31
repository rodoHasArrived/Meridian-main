using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ISecurityMasterQueryService = Meridian.Application.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Unit tests for <see cref="SecurityMasterLedgerBridge"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterLedgerBridgeTests
{
    private static readonly Guid SecurityId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private const string Ticker = "AAPL";

    private static SecurityMasterLedgerBridge BuildBridge(ISecurityMasterQueryService queryService)
        => new(queryService, NullLogger<SecurityMasterLedgerBridge>.Instance);

    private static CorporateActionDto MakeDividend(decimal dividendPerShare, DateOnly exDate)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: SecurityId,
            EventType: "Dividend",
            ExDate: exDate,
            PayDate: exDate.AddDays(14),
            DividendPerShare: dividendPerShare,
            Currency: "USD",
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);

    private static CorporateActionDto MakeStockSplit(decimal splitRatio, DateOnly exDate)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: SecurityId,
            EventType: "StockSplit",
            ExDate: exDate,
            PayDate: null,
            DividendPerShare: null,
            Currency: null,
            SplitRatio: splitRatio,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);

    private static CorporateActionDto MakeSpinOff(Guid newSecurityId, decimal distributionRatio, DateOnly exDate)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: SecurityId,
            EventType: "SpinOff",
            ExDate: exDate,
            PayDate: null,
            DividendPerShare: null,
            Currency: null,
            SplitRatio: null,
            NewSecurityId: newSecurityId,
            DistributionRatio: distributionRatio,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);

    [Fact]
    public async Task PostCorporateActionsAsync_Dividend_PostsBalancedDrDividendReceivableCrDividendIncome()
    {
        var dividend = MakeDividend(0.25m, new DateOnly(2025, 3, 15));
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { dividend });

        var bridge = BuildBridge(queryService);
        var ledger = new Meridian.Ledger.Ledger();

        await bridge.PostCorporateActionsAsync(SecurityId, Ticker, ledger);

        ledger.Journal.Should().HaveCount(1);
        var entry = ledger.Journal[0];
        entry.JournalEntryId.Should().Be(dividend.CorpActId);
        entry.IsBalanced.Should().BeTrue();
        entry.Metadata.ActivityType.Should().Be("Dividend");
        entry.Metadata.Symbol.Should().Be("AAPL");
        entry.Metadata.SecurityId.Should().Be(SecurityId);
        entry.Metadata.LedgerView.Should().Be(LedgerViewKind.SecurityMaster);

        var receivable = LedgerAccounts.DividendReceivable(Ticker);
        var income = LedgerAccounts.DividendIncome;

        ledger.GetBalance(receivable).Should().Be(0.25m);
        ledger.GetBalance(income).Should().Be(0.25m);
    }

    [Fact]
    public async Task PostCorporateActionsAsync_IsIdempotent_WhenCalledTwice()
    {
        var dividend = MakeDividend(1.00m, new DateOnly(2025, 6, 10));
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { dividend });

        var bridge = BuildBridge(queryService);
        var ledger = new Meridian.Ledger.Ledger();

        await bridge.PostCorporateActionsAsync(SecurityId, Ticker, ledger);
        await bridge.PostCorporateActionsAsync(SecurityId, Ticker, ledger);

        // Second call must skip the already-posted entry.
        ledger.Journal.Should().HaveCount(1);
    }

    [Fact]
    public async Task PostCorporateActionsAsync_StockSplit_PostsZeroNetMemoEntry()
    {
        var split = MakeStockSplit(2.0m, new DateOnly(2025, 9, 1));
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { split });

        var bridge = BuildBridge(queryService);
        var ledger = new Meridian.Ledger.Ledger();

        await bridge.PostCorporateActionsAsync(SecurityId, Ticker, ledger);

        ledger.Journal.Should().HaveCount(1);
        var entry = ledger.Journal[0];
        entry.JournalEntryId.Should().Be(split.CorpActId);
        entry.IsBalanced.Should().BeTrue();
        entry.Metadata.ActivityType.Should().Be("StockSplit");
        entry.Metadata.LedgerView.Should().Be(LedgerViewKind.SecurityMaster);
    }

    [Fact]
    public async Task PostCorporateActionsAsync_SpinOff_PostsBalancedDrCashCrCorpActionDistribution()
    {
        var newSecurity = Guid.NewGuid();
        var spinOff = MakeSpinOff(newSecurity, 0.5m, new DateOnly(2025, 11, 20));
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { spinOff });

        var bridge = BuildBridge(queryService);
        var ledger = new Meridian.Ledger.Ledger();

        await bridge.PostCorporateActionsAsync(SecurityId, Ticker, ledger);

        ledger.Journal.Should().HaveCount(1);
        var entry = ledger.Journal[0];
        entry.IsBalanced.Should().BeTrue();
        entry.Metadata.ActivityType.Should().Be("SpinOff");

        var distribution = LedgerAccounts.CorpActionDistribution(Ticker);
        ledger.GetBalance(LedgerAccounts.Cash).Should().Be(0.5m);
        ledger.GetBalance(distribution).Should().Be(0.5m);
    }

    [Fact]
    public async Task PostCorporateActionsAsync_EmptyActions_DoesNotPostAnything()
    {
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CorporateActionDto>());

        var bridge = BuildBridge(queryService);
        var ledger = new Meridian.Ledger.Ledger();

        await bridge.PostCorporateActionsAsync(SecurityId, Ticker, ledger);

        ledger.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task PostCorporateActionsAsync_TickerNormalized_UpperCaseInMetadata()
    {
        var dividend = MakeDividend(0.10m, new DateOnly(2025, 1, 10));
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { dividend });

        var bridge = BuildBridge(queryService);
        var ledger = new Meridian.Ledger.Ledger();

        await bridge.PostCorporateActionsAsync(SecurityId, "aapl", ledger);

        ledger.Journal[0].Metadata.Symbol.Should().Be("AAPL");
    }
}
