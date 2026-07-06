using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.TestSupport.CorporateActions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using DomainLedger = Meridian.Ledger.Ledger;
using ISecurityMasterQueryService = Meridian.Application.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Golden-scenario regression sweep for <see cref="SecurityMasterLedgerBridge"/>: every
/// fixture tagged with a ledger invariant is posted through the real bridge and checked with
/// <see cref="CorporateActionLedgerInvariants"/>. Fixtures and the known-defect registry live
/// under tests/fixtures/corporate-actions/golden/.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CorporateActionGoldenLedgerTests
{
    public static TheoryData<string> BalancedScenarios()
        => GoldenCorporateActionScenarioLoader.ScenarioIds(GoldenInvariantTags.JournalBalanced);

    public static TheoryData<string> ReceivableScenarios()
        => GoldenCorporateActionScenarioLoader.ScenarioIds(GoldenInvariantTags.ReceivableSettled);

    public static TheoryData<string> IdempotencyScenarios()
        => GoldenCorporateActionScenarioLoader.ScenarioIds(GoldenInvariantTags.IdempotentPosting);

    [Theory]
    [MemberData(nameof(BalancedScenarios))]
    public async Task PostCorporateActionsAsync_GoldenScenario_AllJournalsBalance(string scenarioId)
    {
        var (_, _, ledger) = await PostScenarioAsync(scenarioId);

        CorporateActionLedgerInvariants.AssertAllJournalsBalanced(ledger);
        CorporateActionLedgerInvariants.AssertNoDuplicateJournalIds(ledger);
    }

    [Theory]
    [MemberData(nameof(ReceivableScenarios))]
    public async Task PostCorporateActionsAsync_GoldenScenario_PayDateSettlementZeroesReceivable(string scenarioId)
    {
        var (scenario, _, ledger) = await PostScenarioAsync(scenarioId);

        ledger.JournalEntryCount.Should().BeGreaterThan(0, "the scenario declares postable dividends");
        CorporateActionLedgerInvariants.AssertReceivablesSettled(ledger, scenario.Ticker);
    }

    [Theory]
    [MemberData(nameof(IdempotencyScenarios))]
    public async Task PostCorporateActionsAsync_GoldenScenario_RepostAddsNothing(string scenarioId)
    {
        var (scenario, bridge, ledger) = await PostScenarioAsync(scenarioId);
        var firstRunCount = ledger.JournalEntryCount;

        await bridge.PostCorporateActionsAsync(
            scenario.SecurityId, scenario.Ticker, ledger, ToPostingContext(scenario));

        ledger.JournalEntryCount.Should().Be(firstRunCount,
            "posting the same corporate actions twice must be idempotent");
    }

    // ── Supersede-fold semantics ──────────────────────────────────────────────

    [Fact]
    public async Task PostCorporateActionsAsync_AmendedDividend_PostsOnlySupersedingTerms()
    {
        var (scenario, _, ledger) = await PostScenarioAsync("dividend-amended-supersede");

        var originalId = Guid.Parse("f0000000-ffff-4000-8000-0000000000c1");
        var supersedingId = Guid.Parse("f0000000-ffff-4000-8000-0000000000c2");

        ledger.Journal.Should().NotContain(entry => entry.JournalEntryId == originalId,
            "the announced 0.85 event was amended and must never post");

        var declaration = ledger.Journal.Single(entry => entry.JournalEntryId == supersedingId);
        var receivable = LedgerAccounts.DividendReceivable(scenario.Ticker);
        declaration.Lines.Single(line => line.Account == receivable).Debit.Should().Be(880m,
            "gross = amended 0.88/share x 1000 shares");
    }

    [Fact]
    public async Task PostCorporateActionsAsync_CancelledDividend_PostsNothing()
    {
        var (_, _, ledger) = await PostScenarioAsync("dividend-cancelled-after-announce");

        ledger.JournalEntryCount.Should().Be(0,
            "a chain cancelled before any posting must produce no entries");
    }

    [Fact]
    public async Task PostCorporateActionsAsync_CancellationAfterOriginalPosted_DoesNotSilentlyReverse()
    {
        var scenario = GoldenCorporateActionScenarioLoader.Load("dividend-cancelled-after-announce");
        var original = scenario.Actions.Single(static action => action.SupersedesCorpActId is null);
        var ledger = new DomainLedger();

        // First sync: only the announced dividend exists; it posts normally.
        var (bridge, queryService) = CreateBridge();
        queryService.GetCorporateActionsAsync(scenario.SecurityId, Arg.Any<CancellationToken>())
            .Returns([original]);
        await bridge.PostCorporateActionsAsync(scenario.SecurityId, scenario.Ticker, ledger, ToPostingContext(scenario));
        var postedCount = ledger.JournalEntryCount;
        postedCount.Should().BeGreaterThan(0);

        // Second sync: the cancellation arrives. The bridge must not silently reverse —
        // corrections flow through the governed restatement path.
        queryService.GetCorporateActionsAsync(scenario.SecurityId, Arg.Any<CancellationToken>())
            .Returns(scenario.Actions);
        await bridge.PostCorporateActionsAsync(scenario.SecurityId, scenario.Ticker, ledger, ToPostingContext(scenario));

        ledger.JournalEntryCount.Should().Be(postedCount,
            "cancellation after posting defers to the governed restatement path");
    }

    // ── Wiring ────────────────────────────────────────────────────────────────

    private static async Task<(GoldenCorporateActionScenario Scenario, SecurityMasterLedgerBridge Bridge, DomainLedger Ledger)>
        PostScenarioAsync(string scenarioId)
    {
        var scenario = GoldenCorporateActionScenarioLoader.Load(scenarioId);
        var (bridge, queryService) = CreateBridge();
        queryService.GetCorporateActionsAsync(scenario.SecurityId, Arg.Any<CancellationToken>())
            .Returns(scenario.Actions);

        var ledger = new DomainLedger();
        await bridge.PostCorporateActionsAsync(scenario.SecurityId, scenario.Ticker, ledger, ToPostingContext(scenario));
        return (scenario, bridge, ledger);
    }

    private static (SecurityMasterLedgerBridge Bridge, ISecurityMasterQueryService QueryService) CreateBridge()
    {
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        var bridge = new SecurityMasterLedgerBridge(queryService, NullLogger<SecurityMasterLedgerBridge>.Instance);
        return (bridge, queryService);
    }

    internal static CorporateActionLedgerPostingContext? ToPostingContext(GoldenCorporateActionScenario scenario)
        => scenario.PostingContext is { } context
            ? new CorporateActionLedgerPostingContext(
                context.PositionQuantity, context.WithholdingTaxRate, context.FinancialAccountId)
            : null;
}
