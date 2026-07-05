using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Verifies the corporate-action inbox snapshot: staged proposals only, ex-date ordering,
/// and last-run context for the workbench badge.
/// </summary>
public sealed class CorporateActionInboxStateTests
{
    [Fact]
    public void GetInbox_BeforeAnyIngest_ReturnsEmptySnapshot()
    {
        var inbox = new CorporateActionInboxState().GetInbox();

        inbox.LastIngestAt.Should().BeNull();
        inbox.StagedCount.Should().Be(0);
        inbox.Staged.Should().BeEmpty();
        inbox.Errors.Should().BeEmpty();
    }

    [Fact]
    public void GetInbox_AfterIngest_ListsOnlyStagedProposalsInExDateOrder()
    {
        var state = new CorporateActionInboxState();
        var applied = MakeProposal("AAPL", new DateOnly(2026, 8, 1), autoApplied: true);
        var stagedLater = MakeProposal("MSFT", new DateOnly(2026, 9, 15), autoApplied: false);
        var stagedSooner = MakeProposal("GME", new DateOnly(2026, 8, 20), autoApplied: false);

        state.Record(new CorporateActionIngestResult(
            SecuritiesScanned: 3,
            ProvidersQueried: 4,
            Applied: 1,
            Staged: 2,
            DuplicatesSkipped: 1,
            Proposals: [applied, stagedLater, stagedSooner],
            Errors: ["nasdaq/GME: upstream unavailable"]));

        var inbox = state.GetInbox();

        inbox.LastIngestAt.Should().NotBeNull();
        inbox.StagedCount.Should().Be(2);
        inbox.AppliedLastRun.Should().Be(1);
        inbox.DuplicatesSkippedLastRun.Should().Be(1);
        inbox.Staged.Select(static proposal => proposal.Ticker).Should().ContainInOrder("GME", "MSFT");
        inbox.Staged.Should().NotContain(static proposal => proposal.AutoApplied);
        inbox.Errors.Should().ContainSingle();
    }

    [Fact]
    public void Record_SecondIngest_ReplacesTheSnapshot()
    {
        var state = new CorporateActionInboxState();
        state.Record(MakeResult(MakeProposal("GME", new DateOnly(2026, 8, 20), autoApplied: false)));

        state.Record(MakeResult());

        state.GetInbox().StagedCount.Should().Be(0);
    }

    [Fact]
    public void TryTakeStaged_RemovesTheProposalExactlyOnce()
    {
        var state = new CorporateActionInboxState();
        var staged = MakeProposal("GME", new DateOnly(2026, 8, 20), autoApplied: false);
        state.Record(MakeResult(staged));

        var taken = state.TryTakeStaged(staged.SecurityId, "dividend", staged.ExDate, out var proposal);
        var takenAgain = state.TryTakeStaged(staged.SecurityId, "Dividend", staged.ExDate, out _);

        taken.Should().BeTrue("action-type matching is case-insensitive");
        proposal.Ticker.Should().Be("GME");
        takenAgain.Should().BeFalse("an apply consumes the proposal exactly once");
        state.GetInbox().StagedCount.Should().Be(0);
    }

    [Fact]
    public void TryTakeStaged_NeverTakesAutoAppliedProposals()
    {
        var state = new CorporateActionInboxState();
        var applied = MakeProposal("AAPL", new DateOnly(2026, 8, 1), autoApplied: true);
        state.Record(MakeResult(applied));

        state.TryTakeStaged(applied.SecurityId, applied.ActionType, applied.ExDate, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void ProposalMapper_MapsDividendAndSplitFieldsOntoTheEventContract()
    {
        var dividend = MakeProposal("GME", new DateOnly(2026, 8, 20), autoApplied: false);
        var split = dividend with
        {
            ActionType = "StockSplit",
            Amount = null,
            Currency = null,
            SplitFromFactor = 1m,
            SplitToFactor = 4m
        };

        var dividendDto = CorporateActionProposalMapper.ToCorporateAction(dividend);
        var splitDto = CorporateActionProposalMapper.ToCorporateAction(split);

        dividendDto.SecurityId.Should().Be(dividend.SecurityId);
        dividendDto.EventType.Should().Be("Dividend");
        dividendDto.DividendPerShare.Should().Be(0.24m);
        dividendDto.PayDate.Should().Be(dividend.PayableDate);
        dividendDto.SplitRatio.Should().BeNull();
        dividendDto.CorpActId.Should().NotBe(Guid.Empty);

        splitDto.SplitRatio.Should().Be(4m);
        splitDto.DividendPerShare.Should().BeNull();
    }

    private static CorporateActionIngestResult MakeResult(params CorporateActionProposal[] proposals)
        => new(
            SecuritiesScanned: proposals.Length,
            ProvidersQueried: 1,
            Applied: 0,
            Staged: proposals.Length,
            DuplicatesSkipped: 0,
            Proposals: proposals,
            Errors: []);

    private static CorporateActionProposal MakeProposal(string ticker, DateOnly exDate, bool autoApplied)
        => new(
            SecurityId: Guid.NewGuid(),
            Ticker: ticker,
            ActionType: "Dividend",
            ExDate: exDate,
            RecordDate: null,
            PayableDate: exDate.AddDays(14),
            Amount: 0.24m,
            Currency: "USD",
            SplitFromFactor: null,
            SplitToFactor: null,
            WinningSource: "finnhub",
            AgreeingSources: ["finnhub"],
            DissentingSources: [],
            AutoApplied: autoApplied);
}
