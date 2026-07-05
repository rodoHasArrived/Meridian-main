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
            RightsPerShare: null,
            RecordDate: exDate.AddDays(1));

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

    private static CorporateActionDto MakePrincipalPaydown(decimal factorDelta, DateOnly exDate)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: SecurityId,
            EventType: "PrincipalPaydown",
            ExDate: exDate,
            PayDate: exDate.AddDays(2),
            DividendPerShare: null,
            Currency: "USD",
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: factorDelta,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);

    [Fact]
    public async Task PostCorporateActionsAsync_Dividend_PostsScaledReceivableAndPayDateRelief()
    {
        var dividend = MakeDividend(0.25m, new DateOnly(2025, 3, 15));
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { dividend });

        var bridge = BuildBridge(queryService);
        var ledger = new Meridian.Ledger.Ledger();

        await bridge.PostCorporateActionsAsync(
            SecurityId,
            Ticker,
            ledger,
            new CorporateActionLedgerPostingContext(PositionQuantity: 100m));

        ledger.Journal.Should().HaveCount(2);
        var entry = ledger.Journal[0];
        entry.JournalEntryId.Should().Be(dividend.CorpActId);
        entry.IsBalanced.Should().BeTrue();
        entry.Metadata.ActivityType.Should().Be("Dividend");
        entry.Metadata.Symbol.Should().Be("AAPL");
        entry.Metadata.SecurityId.Should().Be(SecurityId);
        entry.Metadata.LedgerView.Should().Be(LedgerViewKind.SecurityMaster);

        var receivable = LedgerAccounts.DividendReceivable(Ticker);
        var income = LedgerAccounts.DividendIncome;

        ledger.GetBalance(receivable).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.Cash).Should().Be(25m);
        ledger.GetBalance(income).Should().Be(25m);
        ledger.Journal[1].Metadata.ActivityType.Should().Be(AutomatedJournalEventKind.DividendReceived.ToString());
    }

    [Fact]
    public async Task PostCorporateActionsAsync_DividendWithWithholding_PostsTaxAccrualAndNetReceipt()
    {
        var dividend = MakeDividend(0.25m, new DateOnly(2025, 3, 15));
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { dividend });

        var bridge = BuildBridge(queryService);
        var ledger = new Meridian.Ledger.Ledger();

        await bridge.PostCorporateActionsAsync(
            SecurityId,
            Ticker,
            ledger,
            new CorporateActionLedgerPostingContext(PositionQuantity: 100m, WithholdingTaxRate: 0.2m));

        ledger.Journal.Should().HaveCount(3);
        ledger.GetBalance(LedgerAccounts.DividendReceivable(Ticker)).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.Cash).Should().Be(20m);
        ledger.GetBalance(LedgerAccounts.DividendIncome).Should().Be(25m);
        ledger.GetBalance(LedgerAccounts.WithholdingTaxExpenseFor(Ticker)).Should().Be(5m);
        ledger.GetBalance(LedgerAccounts.WithholdingTaxPayableFor(Ticker)).Should().Be(0m);
        ledger.Journal.Should().Contain(entry =>
            entry.Metadata.ActivityType == AutomatedJournalEventKind.WithholdingTaxAccrued.ToString());
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

        await bridge.PostCorporateActionsAsync(
            SecurityId,
            Ticker,
            ledger,
            new CorporateActionLedgerPostingContext(PositionQuantity: 10m));
        await bridge.PostCorporateActionsAsync(
            SecurityId,
            Ticker,
            ledger,
            new CorporateActionLedgerPostingContext(PositionQuantity: 10m));

        // Second call must skip the already-posted declaration and pay-date receipt.
        ledger.Journal.Should().HaveCount(2);
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
    public async Task PostCorporateActionsAsync_SpinOff_PostsNonCashMemoEntry()
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

        ledger.GetBalance(LedgerAccounts.Cash).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.CorpActionDistribution(Ticker)).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.Securities(Ticker)).Should().Be(0m);
    }

    [Fact]
    public async Task PostCorporateActionsAsync_PrincipalPaydown_PostsCashAndReducesSecurityAsset()
    {
        var paydown = MakePrincipalPaydown(0.0085m, new DateOnly(2026, 5, 25));
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { paydown });

        var bridge = BuildBridge(queryService);
        var ledger = new Meridian.Ledger.Ledger();

        await bridge.PostCorporateActionsAsync(SecurityId, Ticker, ledger);

        ledger.Journal.Should().ContainSingle();
        var entry = ledger.Journal[0];
        entry.JournalEntryId.Should().Be(paydown.CorpActId);
        entry.IsBalanced.Should().BeTrue();
        entry.Metadata.ActivityType.Should().Be("PrincipalPaydown");
        entry.Metadata.SecurityId.Should().Be(SecurityId);
        entry.Metadata.LedgerView.Should().Be(LedgerViewKind.SecurityMaster);

        ledger.GetBalance(LedgerAccounts.Cash).Should().Be(0.0085m);
        ledger.GetBalance(LedgerAccounts.Securities(Ticker)).Should().Be(-0.0085m);
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
    public async Task PostCorporateActionsAsync_DividendWithoutPositionQuantity_DoesNotPostEconomics()
    {
        var dividend = MakeDividend(0.10m, new DateOnly(2025, 1, 10));
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { dividend });

        var bridge = BuildBridge(queryService);
        var ledger = new Meridian.Ledger.Ledger();

        await bridge.PostCorporateActionsAsync(SecurityId, "aapl", ledger);

        ledger.Journal.Should().BeEmpty("dividend economics require a record-date position quantity");
    }

    [Fact]
    public async Task PostCorporateActionsAsync_TickerNormalized_UpperCaseInMetadata()
    {
        var split = MakeStockSplit(2m, new DateOnly(2025, 1, 10));
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new[] { split });

        var bridge = BuildBridge(queryService);
        var ledger = new Meridian.Ledger.Ledger();

        await bridge.PostCorporateActionsAsync(SecurityId, "aapl", ledger);

        ledger.Journal[0].Metadata.Symbol.Should().Be("AAPL");
    }
}
