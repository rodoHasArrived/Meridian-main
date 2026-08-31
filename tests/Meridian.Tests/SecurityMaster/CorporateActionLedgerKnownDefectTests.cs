using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Ledger;
using Meridian.TestSupport.CorporateActions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using DomainLedger = Meridian.Ledger.Ledger;
using ISecurityMasterQueryService = Meridian.Application.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Characterization tests pinning the ledger-side entries of the known-defect registry
/// (tests/fixtures/corporate-actions/golden/KNOWN-DEFECTS.md). Each test asserts TODAY'S
/// defective behavior so CI stays green and any accidental behavior change — fix or
/// regression — fails loudly. When the fixing lane lands, flip the assertion to the TARGET
/// and update the registry in the same PR.
/// </summary>
[Trait("Category", "KnownDefect")]
public sealed class CorporateActionLedgerKnownDefectTests
{
    [Fact]
    public async Task SpinOff_CurrentBehavior_SymbolicOneUnitMemoOnly_CA_DEF_002()
    {
        // TARGET (Idea #3): a spinoff allocates cost basis across parent and child by
        // fair-value ratio, opens the child position, and books cash-in-lieu for fractionals.
        var (scenario, ledger) = await PostAsync("t-wbd-spinoff-2022");

        var entry = ledger.Journal.Should().ContainSingle().Subject;
        var securities = LedgerAccounts.Securities(scenario.Ticker);
        entry.Lines.Should().OnlyContain(line => line.Account == securities,
            "CURRENT defective behavior: both memo legs land on the parent's securities account");
        entry.Lines.Sum(static line => line.Debit).Should().Be(1m,
            "CURRENT defective behavior: symbolic 1-unit memo, no basis allocation");
    }

    [Fact]
    public async Task Merger_CurrentBehavior_SymbolicOneUnitMemoOnly_CA_DEF_002()
    {
        // TARGET (Idea #3): a merger converts lots at the exchange ratio plus cash
        // consideration and realizes P&L on absorption.
        var (scenario, ledger) = await PostAsync("cash-stock-merger-2018");
        var mergerCorpActId = Guid.Parse("d0000000-dddd-4000-8000-0000000000c1");

        var mergerEntry = ledger.Journal.Single(entry => entry.JournalEntryId == mergerCorpActId);
        var securities = LedgerAccounts.Securities(scenario.Ticker);
        mergerEntry.Lines.Should().OnlyContain(line => line.Account == securities);
        mergerEntry.Lines.Sum(static line => line.Debit).Should().Be(1m,
            "CURRENT defective behavior: the exchange ratio and acquirer position are ignored");
    }

    [Fact]
    public async Task Dividend_NoPositionContext_CurrentBehavior_SkippedEntirely_CA_DEF_006()
    {
        // TARGET (Idea #3): the record-date holdings seam supplies the position quantity;
        // a declared dividend on held stock must post without caller-side plumbing.
        var (_, ledger) = await PostAsync("dividend-no-position-context");

        ledger.JournalEntryCount.Should().Be(0,
            "CURRENT defective behavior: PositionQuantity defaults to 0 and every dividend is skipped");
    }

    private static async Task<(GoldenCorporateActionScenario Scenario, DomainLedger Ledger)> PostAsync(string scenarioId)
    {
        var scenario = GoldenCorporateActionScenarioLoader.Load(scenarioId);
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetCorporateActionsAsync(scenario.SecurityId, Arg.Any<CancellationToken>())
            .Returns(scenario.Actions);

        var bridge = new SecurityMasterLedgerBridge(queryService, NullLogger<SecurityMasterLedgerBridge>.Instance);
        var ledger = new DomainLedger();
        await bridge.PostCorporateActionsAsync(
            scenario.SecurityId,
            scenario.Ticker,
            ledger,
            CorporateActionGoldenLedgerTests.ToPostingContext(scenario));

        return (scenario, ledger);
    }
}
