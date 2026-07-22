using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Golden-file coverage for the live-call path: the fund-economics kernels reached through their
/// real caller (<see cref="FundEconomicsJournalFactory"/>) rather than only through the pure
/// calculators. Also proves the produced fee postings balance per currency when round-tripped
/// through the ledger and are version-checked at the governed append boundary.
/// </summary>
public sealed class FundEconomicsJournalFactoryTests
{
    private const string FundId = "fund-alpha";
    private const string PeriodId = "2026-Q1";
    private static readonly DateOnly EffectiveDate = new(2026, 3, 31);
    private static readonly DateTimeOffset OccurredAt = new(2026, 3, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ManagementFee_LiveCaller_ProducesGoldenBalancedDraft()
    {
        // 10,000,000 net assets × 2% × 365/365 = 200,000.00 — a full-year accrual.
        var draft = FundEconomicsJournalFactory.BuildManagementFeeAccrualDraft(
            FundId, 10_000_000m, 0.02m, 365, 365, EffectiveDate, OccurredAt, PeriodId);

        draft.IsBalanced.Should().BeTrue();
        draft.TotalDebits.Should().Be(200_000.00m);
        draft.Lines.Should().ContainSingle(line =>
            line.account == LedgerAccounts.ManagementFeeExpenseFor(FundId) && line.debit == 200_000.00m);
        draft.Lines.Should().ContainSingle(line =>
            line.account == LedgerAccounts.ManagementFeePayableFor(FundId) && line.credit == 200_000.00m);
        draft.Event.Kind.Should().Be(AutomatedJournalEventKind.ManagementFeeAccrued);
    }

    [Fact]
    public void PerformanceFee_LiveCaller_CrystallizesAndAdvancesHighWaterMark()
    {
        // Gross profit 2,000,000 above the 10,000,000 high-water mark, less a 200,000 hurdle,
        // charged at 20% => 360,000.00. Crystallizing advances the mark to 12,000,000 - 360,000.
        var result = FundEconomicsJournalFactory.BuildPerformanceFeeAccrualDraft(
            FundId, 12_000_000m, 10_000_000m, 200_000m, 0.20m, crystallize: true, EffectiveDate, OccurredAt, PeriodId);

        result.HasFee.Should().BeTrue();
        result.PerformanceFee.Should().Be(360_000.00m);
        result.NewHighWaterMark.Should().Be(11_640_000.00m);
        result.Draft!.IsBalanced.Should().BeTrue();
        result.Draft.Lines.Should().ContainSingle(line =>
            line.account == LedgerAccounts.PerformanceFeeExpenseFor(FundId) && line.debit == 360_000.00m);
        result.Draft.Lines.Should().ContainSingle(line =>
            line.account == LedgerAccounts.PerformanceFeePayableFor(FundId) && line.credit == 360_000.00m);
    }

    [Fact]
    public void PerformanceFee_LiveCaller_BelowHighWaterMark_ProducesNoDraft()
    {
        var result = FundEconomicsJournalFactory.BuildPerformanceFeeAccrualDraft(
            FundId, 9_000_000m, 10_000_000m, 0m, 0.20m, crystallize: true, EffectiveDate, OccurredAt, PeriodId);

        result.HasFee.Should().BeFalse();
        result.Draft.Should().BeNull();
        result.PerformanceFee.Should().Be(0m);
        result.NewHighWaterMark.Should().Be(10_000_000m);
    }

    [Fact]
    public void ExpenseAccrual_LiveCaller_ProducesGoldenBalancedDraft()
    {
        // 120,000 annual admin expense × 30/360 = 10,000.00.
        var draft = FundEconomicsJournalFactory.BuildExpenseAccrualDraft(
            FundId, "admin-fee", 120_000m, 30, 360, EffectiveDate, OccurredAt, PeriodId);

        draft.IsBalanced.Should().BeTrue();
        draft.TotalDebits.Should().Be(10_000.00m);
        draft.Lines.Should().ContainSingle(line =>
            line.account == LedgerAccounts.FundOperatingExpenseFor(FundId) && line.debit == 10_000.00m);
        draft.Lines.Should().ContainSingle(line =>
            line.account == LedgerAccounts.AccruedExpensesPayableFor(FundId) && line.credit == 10_000.00m);
    }

    [Fact]
    public void Distribution_LiveCaller_SplitsLpAndGpAndBalances()
    {
        // A whole-fund waterfall over a 10,000,000 contribution, 500,000 preferred, distributing
        // 13,000,000 at 20% carry — the waterfall kernel is the live source of the split.
        var waterfall = EuropeanDistributionWaterfall.Distribute(new EuropeanWaterfallInput(
            contributedCapital: 10_000_000m,
            preferredReturnAccrued: 500_000m,
            amountToDistribute: 13_000_000m,
            carryRate: 0.20m));

        var draft = FundEconomicsJournalFactory.BuildDistributionWaterfallDraft(
            FundId, "investor-1", waterfall, EffectiveDate, OccurredAt, PeriodId, "dist-1");

        draft.IsBalanced.Should().BeTrue();
        draft.TotalCredits.Should().Be(waterfall.Distributed);
        draft.Lines.Should().Contain(line =>
            line.account == LedgerAccounts.FundDistributionPayableFor("investor-1") && line.credit == waterfall.Distributed);
        draft.Lines.Sum(line => line.debit).Should().Be(waterfall.LpTotal + waterfall.GpTotal);
    }

    [Fact]
    public void FeePostings_RoundTripThroughLedger_BalancePerCurrency()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var feeDraft = FundEconomicsJournalFactory.BuildManagementFeeAccrualDraft(
            FundId, 10_000_000m, 0.02m, 365, 365, EffectiveDate, OccurredAt, PeriodId);
        var entry = ToJournalEntry(feeDraft, "USD");

        ledger.Post(entry);

        // Balanced overall and balanced per transaction currency (USD debits == USD credits).
        entry.IsBalanced.Should().BeTrue();
        var byCurrency = entry.Lines
            .GroupBy(line => line.Currency!.TransactionCurrency)
            .Select(group => new
            {
                Currency = group.Key,
                Debit = group.Sum(line => line.Currency!.TransactionDebit),
                Credit = group.Sum(line => line.Currency!.TransactionCredit),
            })
            .ToArray();
        byCurrency.Should().ContainSingle();
        byCurrency[0].Currency.Should().Be("USD");
        byCurrency[0].Debit.Should().Be(byCurrency[0].Credit);

        // Round-trip: the posted balances are readable back off the ledger.
        ledger.GetBalance(LedgerAccounts.ManagementFeeExpenseFor(FundId)).Should().Be(200_000.00m);
        ledger.GetBalance(LedgerAccounts.ManagementFeePayableFor(FundId)).Should().Be(200_000.00m);
    }

    [Fact]
    public void FeePosting_AtAppendBoundary_IsVersionChecked()
    {
        var feeDraft = FundEconomicsJournalFactory.BuildManagementFeeAccrualDraft(
            FundId, 10_000_000m, 0.02m, 365, 365, EffectiveDate, OccurredAt, PeriodId);
        var entry = ToJournalEntry(feeDraft, "USD");
        var commandId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var aggregateId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var periodId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var ledgerBookId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        AccountingPostingCommandDto BuildCommand(long? expectedVersion) => new(
            commandId,
            aggregateId,
            periodId,
            EffectiveDate,
            OccurredAt,
            entry.Metadata.IdempotencyKey!,
            Intent: AccountingPostingIntentDto.Originating,
            CorrelationId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            CausationId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            ExpectedVersion: expectedVersion,
            SourceEventType: "ManagementFee",
            ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: "approval-1",
            OperatorRationale: "Controller approved the quarterly management fee accrual.",
            Evidence: [],
            LedgerBookId: ledgerBookId);

        LedgerJournalEntryWrite BuildWrite(long? expectedVersion) => new(
            entry,
            aggregateId,
            periodId,
            CommandId: commandId,
            PostingCommand: BuildCommand(expectedVersion));

        // With an expected version the governed boundary accepts the write and stamps the version.
        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(
            BuildWrite(expectedVersion: 3),
            requirePostingCommand: true,
            requireExpectedVersion: true);
        normalized.PostingCommand!.ExpectedVersion.Should().Be(3);

        // Without one, the boundary refuses the append — the version check is mandatory.
        var post = () => AccountingPostingCommandValidator.NormalizeAndValidate(
            BuildWrite(expectedVersion: null),
            requirePostingCommand: true,
            requireExpectedVersion: true);
        post.Should().Throw<LedgerValidationException>()
            .WithMessage("*expected version is required*");
    }

    private static JournalEntry ToJournalEntry(AutomatedJournalDraft draft, string currency)
    {
        var lines = draft.Lines
            .Select(line => new LedgerEntry(
                Guid.NewGuid(),
                JournalEntryId,
                OccurredAt,
                line.account,
                line.debit,
                line.credit,
                draft.Description,
                line.dimensions,
                new LedgerEntryCurrency(currency, currency, line.debit, line.credit, 1m)))
            .ToArray();

        return new JournalEntry(JournalEntryId, OccurredAt, draft.Description, lines, draft.Metadata);
    }

    private static readonly Guid JournalEntryId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
}
