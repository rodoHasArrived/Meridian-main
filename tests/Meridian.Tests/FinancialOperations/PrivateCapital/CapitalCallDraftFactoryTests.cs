using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

public sealed class CapitalCallDraftFactoryTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 3, 31, 12, 0, 0, TimeSpan.Zero);

    private static InvestorCommitment Commitment()
        => new(
            "commitment:fund-a:lp-1:1",
            "fund-a",
            null,
            "ca:lp-1",
            "lp-1",
            "USD",
            10_000_000m,
            new DateOnly(2026, 1, 1),
            new DateOnly(2031, 1, 1),
            CommitmentStatus.Active);

    private static JournalEvidenceReference Evidence()
        => new("evidence-1", "vault://drawdown/1", "drawdown-notice", "commitment-engine", OccurredAt, "ops-controller");

    [Fact]
    public void CapitalCallDraft_IsBalancedWithRequiredMetadata()
    {
        var draft = CapitalCallDraftFactory.BuildCapitalCallDraft(
            Commitment(), "inst-1", 2_500_000m, new DateOnly(2026, 3, 31), OccurredAt, [Evidence()]);

        draft.IsBalanced.Should().BeTrue();
        draft.Metadata.EffectiveDate.Should().Be(new DateOnly(2026, 3, 31));
        draft.Metadata.IdempotencyKey.Should().NotBeNullOrWhiteSpace();
        draft.Metadata.FundEventType.Should().Be("CapitalCall");
        draft.Metadata.CapitalAccountId.Should().Be("ca:lp-1");
        draft.Metadata.Tags.Should().ContainKey("commitmentId");
        draft.Lines.Should().Contain(line =>
            line.account.AccountType == LedgerAccountType.Equity && line.credit == 2_500_000m);
    }

    [Fact]
    public void CapitalCallDraft_RoundTripsThroughProjectorAsPostingReady()
    {
        var commitment = Commitment();
        var draft = CapitalCallDraftFactory.BuildCapitalCallDraft(
            commitment, "inst-1", 2_500_000m, new DateOnly(2026, 3, 31), OccurredAt, [Evidence()]);

        var ledger = new Meridian.Ledger.Ledger();
        AutomatedJournalApproval.Submit(draft, "ops-controller", OccurredAt, "issue call", ["evidence-1"])
            .Approve("cfo", OccurredAt, "approve call", ["evidence-1"])
            .PostTo(ledger, "cfo", OccurredAt, "post call", ["evidence-1"]);

        var projection = PrivateCapitalFundEventLedgerProjector.Project(ledger);
        var fundEvent = projection.Events.Should().ContainSingle().Subject;
        fundEvent.IsPostingReady.Should().BeTrue();
        fundEvent.CapitalAccountImpacts.Should().NotBeEmpty();
    }

    [Fact]
    public void DefaultInterestDraft_CreditsIncomeNotEquity()
    {
        var commitment = Commitment();
        var installment = new DrawdownInstallment(
            "inst-1", commitment.CommitmentId, 1, new DateOnly(2026, 3, 17), new DateOnly(2026, 3, 31),
            callPercent: null, callAmount: 1_000_000m, DrawdownInstallmentStatus.Defaulted);
        var accrual = new DefaultInterestAccrual(
            "accrual-1", "default-1", new DateOnly(2026, 4, 10), new DateOnly(2026, 5, 1),
            1_000_000m, 0.10m, DefaultInterestConvention.Actual365Fixed, 5_753.42m);
        var capitalDefault = new CapitalCallDefault(
            "default-1", commitment, installment, 1_000_000m, new DateOnly(2026, 3, 31), null,
            DrawdownInstallmentStatus.Defaulted, [accrual]);

        var draft = CapitalCallDraftFactory.BuildDefaultInterestDraft(capitalDefault, accrual, OccurredAt, [Evidence()]);

        draft.IsBalanced.Should().BeTrue();
        draft.Lines.Should().Contain(line =>
            line.account.AccountType == LedgerAccountType.Revenue && line.credit == 5_753.42m);
        draft.Lines.Should().NotContain(line => line.account.AccountType == LedgerAccountType.Equity);
    }

    [Fact]
    public void FundingDraft_MovesCashAgainstReceivable()
    {
        var draft = CapitalCallDraftFactory.BuildCapitalCallFundingDraft(
            Commitment(), "inst-1", 2_500_000m, new DateOnly(2026, 4, 5), OccurredAt, evidenceReferences: [Evidence()]);

        draft.IsBalanced.Should().BeTrue();
        draft.Lines.Should().Contain(line =>
            line.account.Name == "Cash" && line.debit == 2_500_000m);
        draft.Lines.Should().Contain(line =>
            line.account.Name == "Capital Call Receivable" && line.credit == 2_500_000m);
    }
}
