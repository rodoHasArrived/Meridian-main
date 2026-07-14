using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using DomainLedger = Meridian.Ledger.Ledger;
using ISecurityMasterQueryService = Meridian.Application.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Tests the opt-in <see cref="CorporateActionLedgerPostingContext.AutoReverseSupersededPostings"/>
/// path on <see cref="SecurityMasterLedgerBridge"/>: a cancellation/amendment of a previously-posted
/// corporate action books automated reversing entries (and rebooks amended terms), while the default
/// context continues to defer corrections to the governed restatement path.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterLedgerBridgeAutoReverseTests
{
    private static readonly Guid SecurityId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private const string Ticker = "AAPL";
    private static readonly DateOnly ExDate = new(2026, 3, 15);

    private static CorporateActionDto MakeDividend(decimal dividendPerShare)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: SecurityId,
            EventType: "Dividend",
            ExDate: ExDate,
            PayDate: ExDate.AddDays(14),
            DividendPerShare: dividendPerShare,
            Currency: "USD",
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null,
            RecordDate: ExDate.AddDays(1));

    private static (SecurityMasterLedgerBridge Bridge, ISecurityMasterQueryService QueryService) CreateBridge()
    {
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var bridge = new SecurityMasterLedgerBridge(queryService, NullLogger<SecurityMasterLedgerBridge>.Instance);
        return (bridge, queryService);
    }

    [Fact]
    public async Task AutoReverse_CancellationAfterPosting_ReversesOriginalToZero()
    {
        var (bridge, queryService) = CreateBridge();
        var ledger = new DomainLedger();
        var context = new CorporateActionLedgerPostingContext(PositionQuantity: 100m, AutoReverseSupersededPostings: true);
        var original = MakeDividend(0.25m);

        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new[] { original });
        await bridge.PostCorporateActionsAsync(SecurityId, Ticker, ledger, context);
        var afterFirstSync = ledger.JournalEntryCount;
        afterFirstSync.Should().BeGreaterThan(0);

        var cancellation = original with
        {
            CorpActId = Guid.NewGuid(),
            SupersedesCorpActId = original.CorpActId,
            LifecycleState = CorporateActionLifecycleStates.Cancelled,
        };
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new[] { original, cancellation });
        await bridge.PostCorporateActionsAsync(SecurityId, Ticker, ledger, context);

        ledger.JournalEntryCount.Should().BeGreaterThan(afterFirstSync, "reversing entries were booked");
        ledger.GetBalance(LedgerAccounts.DividendReceivable(Ticker)).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.DividendIncome).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.Cash).Should().Be(0m);
        ledger.Journal.Should().OnlyContain(entry => entry.IsBalanced);
    }

    [Fact]
    public async Task AutoReverse_AmendmentAfterPosting_ReversesOriginalAndRebooksNewTerms()
    {
        var (bridge, queryService) = CreateBridge();
        var ledger = new DomainLedger();
        var context = new CorporateActionLedgerPostingContext(PositionQuantity: 100m, AutoReverseSupersededPostings: true);
        var original = MakeDividend(0.25m);

        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new[] { original });
        await bridge.PostCorporateActionsAsync(SecurityId, Ticker, ledger, context);

        var amendment = MakeDividend(0.30m) with { SupersedesCorpActId = original.CorpActId };
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new[] { original, amendment });
        await bridge.PostCorporateActionsAsync(SecurityId, Ticker, ledger, context);

        // The amended declaration posts with the new 0.30/share terms (30 = 0.30 x 100 shares).
        var amendedDeclaration = ledger.Journal.Single(entry => entry.JournalEntryId == amendment.CorpActId);
        amendedDeclaration.Lines.Single(line => line.Account == LedgerAccounts.DividendReceivable(Ticker))
            .Debit.Should().Be(30m);

        // Original terms fully reversed, so the receivable nets flat and only the amended income remains.
        ledger.GetBalance(LedgerAccounts.DividendReceivable(Ticker)).Should().Be(0m);
        ledger.Journal.Should().OnlyContain(entry => entry.IsBalanced);
    }

    [Fact]
    public async Task DefaultContext_AmendmentAfterPosting_DefersWithoutReversing()
    {
        var (bridge, queryService) = CreateBridge();
        var ledger = new DomainLedger();
        var context = new CorporateActionLedgerPostingContext(PositionQuantity: 100m); // AutoReverse defaults off
        var original = MakeDividend(0.25m);

        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new[] { original });
        await bridge.PostCorporateActionsAsync(SecurityId, Ticker, ledger, context);
        var afterFirstSync = ledger.JournalEntryCount;

        var amendment = MakeDividend(0.30m) with { SupersedesCorpActId = original.CorpActId };
        queryService.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new[] { original, amendment });
        await bridge.PostCorporateActionsAsync(SecurityId, Ticker, ledger, context);

        ledger.JournalEntryCount.Should().Be(afterFirstSync,
            "amendment after posting defers to the governed restatement path unless auto-reverse is enabled");
    }
}
