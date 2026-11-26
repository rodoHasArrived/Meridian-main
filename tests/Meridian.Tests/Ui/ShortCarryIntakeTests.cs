using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// The short carrying-charge posting path: a reviewed borrow fee or payment in lieu is proved against the
/// authoritative open short lot, projected by <see cref="ShortCarryProjector"/>, and queued as a governed
/// workbench draft. Nothing posts until the draft is reviewed and approved.
/// </summary>
public sealed class ShortCarryIntakeTests
{
    private const string FundProfileId = "fund-alpha";
    private static readonly Guid BookId = Guid.Parse("5c000000-0000-0000-0000-0000000000b1");
    private static readonly Guid LotRecordId = Guid.Parse("5c000000-0000-0000-0000-0000000000a1");
    private static readonly Guid SecurityId = Guid.Parse("5c000000-0000-0000-0000-0000000000c1");
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly LedgerAccount ShortCarrying = LedgerAccounts.ShortSecuritiesPayable("XYZ");

    [Fact]
    public void BorrowFeeDraft_DebitsBorrowFeeExpenseAndCreditsCash_OnTheLastAccruedDay()
    {
        var draft = ShortCarryDraftBuilder.BuildDraft(BorrowFee(), ShortCarrying, SecurityId, Now, "ops-user");

        // 100 units x 50.00 x 3.65% x 10 days / 365 = 5.00
        draft.Lines.Should().HaveCount(2);
        draft.Lines.Should().ContainSingle(line => line.account.Name == "Stock Borrow Fee Expense" && line.debit == 5m && line.credit == 0m);
        draft.Lines.Should().ContainSingle(line => line.account.Name == "Cash" && line.debit == 0m && line.credit == 5m);
        draft.Event.Kind.Should().Be(AutomatedJournalEventKind.ShortCarryCharged);
        draft.Metadata.EffectiveDate.Should().Be(new DateOnly(2026, 11, 10));
        draft.Metadata.IdempotencyKey.Should().StartWith(ShortCarryDraftBuilder.IdempotencyPrefix + LotRecordId.ToString("D"));
        draft.Metadata.Tags!["shortCarry.sourceEvidenceId"].Should().Be("lender-statement-2026-11");
    }

    [Fact]
    public void PaymentInLieuDraft_DebitsDividendExpense_OnThePayDate()
    {
        var draft = ShortCarryDraftBuilder.BuildDraft(PaymentInLieu(), ShortCarrying, SecurityId, Now, "ops-user");

        draft.Lines.Should().ContainSingle(line => line.account.Name == "Dividend Expense" && line.debit == 25m);
        draft.Metadata.EffectiveDate.Should().Be(new DateOnly(2026, 11, 11));
    }

    [Fact]
    public void ChangedInstructionInputs_AreADifferentChargeIdentity()
    {
        var first = ShortCarryDraftBuilder.BuildDraft(BorrowFee(), ShortCarrying, SecurityId, Now, "ops-user");
        var replay = ShortCarryDraftBuilder.BuildDraft(BorrowFee(), ShortCarrying, SecurityId, Now.AddHours(1), "ops-user");
        var changed = ShortCarryDraftBuilder.BuildDraft(BorrowFee() with { Quantity = 90m }, ShortCarrying, SecurityId, Now, "ops-user");

        replay.Metadata.IdempotencyKey.Should().Be(first.Metadata.IdempotencyKey);
        changed.Metadata.IdempotencyKey.Should().NotBe(first.Metadata.IdempotencyKey);
    }

    [Fact]
    public void LongCarryingAccount_IsRefused()
    {
        var build = () => ShortCarryDraftBuilder.BuildDraft(BorrowFee(), LedgerAccounts.Securities("XYZ"), SecurityId, Now, "ops-user");

        build.Should().Throw<ArgumentException>().WithMessage("*liability carrying account*");
    }

    [Fact]
    public async Task Intake_QueuesTheProjectedChargeForApproval_WithoutPosting()
    {
        var fixture = await CreateFixtureAsync(ShortLot());

        var result = await fixture.Runner.RunShortCarryIntakeAsync(Request(BorrowFee()));

        var draft = result.Intake.Created.Should().ContainSingle().Subject;
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft, "a carrying charge lands in the approval queue, never posts directly");
        draft.EntryType.Should().Be(ManualJournalEntryTypeDto.Expense);
        draft.TotalDebits.Should().Be(5m);
        draft.TotalCredits.Should().Be(5m);
        draft.Lines.Should().Contain(line => line.AccountPath == "Expenses:Stock Borrow Fee" && line.Side == AccountingTemplateLineSideDto.Debit);
        draft.Lines.Should().Contain(line => line.AccountPath == "Assets:Cash" && line.Side == AccountingTemplateLineSideDto.Credit);
        draft.ValidationIssues.Where(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            .Select(issue => $"{issue.Code}: {issue.Message}").Should().BeEmpty();
        draft.TreasuryContext!.IdempotencyKey.Should().StartWith(ShortCarryDraftBuilder.IdempotencyPrefix);
    }

    public static TheoryData<string, decimal, OpenLotDirection, string> Refusals => new()
    {
        { "fully covered lot", 0m, OpenLotDirection.Short, "*fully covered*" },
        { "charge above the open quantity", 50m, OpenLotDirection.Short, "*exceeds the lot's open quantity*" },
        { "long lot", 100m, OpenLotDirection.Long, "*Short lots only*" }
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Intake_RefusesAChargeTheLotOfRecordDoesNotSupport(
        string scenario, decimal openQuantity, OpenLotDirection direction, string reason)
    {
        _ = scenario;
        var fixture = await CreateFixtureAsync(ShortLot(openQuantity, direction));

        var run = () => fixture.Runner.RunShortCarryIntakeAsync(Request(BorrowFee()));

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage(reason);
        (await fixture.Workbench.GetWorkbenchAsync(FundProfileId, BookId)).Drafts.Should().BeEmpty();
    }

    [Fact]
    public async Task Intake_RefusesABookOwnedByAnotherFund()
    {
        var fixture = await CreateFixtureAsync(ShortLot(), bookFundProfileId: "fund-other");

        var run = () => fixture.Runner.RunShortCarryIntakeAsync(Request(BorrowFee()));

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not belong to the requested fund profile*");
    }

    [Fact]
    public async Task Intake_WithoutALedgerJournalStore_FailsLoudly()
    {
        var fixture = await CreateFixtureAsync(ShortLot(), withLedgerStore: false);

        var run = () => fixture.Runner.RunShortCarryIntakeAsync(Request(BorrowFee()));

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ledger journal store*");
    }

    private static ShortCarryInstruction BorrowFee() => new(
        LotRecordId, "short-lot-1", OpenLotDirection.Short, ShortCarryKind.BorrowFee,
        new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 11), 100m,
        ReferencePrice: 50m, AnnualRate: 0.0365m, DaysPerYear: 365, AmountPerUnit: null,
        Currency: "USD", FunctionalCurrency: "USD", SourceEvidenceId: "lender-statement-2026-11");

    private static ShortCarryInstruction PaymentInLieu() => new(
        LotRecordId, "short-lot-1", OpenLotDirection.Short, ShortCarryKind.PaymentInLieu,
        new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 11), 100m,
        ReferencePrice: null, AnnualRate: null, DaysPerYear: null, AmountPerUnit: 0.25m,
        Currency: "USD", FunctionalCurrency: "USD", SourceEvidenceId: "dividend-notice-xyz");

    private static RunShortCarryDraftIntakeRequest Request(ShortCarryInstruction instruction)
        => new(FundProfileId, BookId, "ops-user", instruction, EntityId: "entity-alpha",
            TenantId: "tenant-alpha", CompanyId: "company-alpha");

    private static LedgerTaxLotRecord ShortLot(decimal openQuantity = 100m, OpenLotDirection direction = OpenLotDirection.Short)
    {
        var evidence = new RetainedEvidenceIdentityDto("acquisition-evidence", "evidence://synthetic/short-open", new string('a', 64),
            "Synthetic", LotRecordId.ToString("D"), "Accepted", "synthetic-source-validation", Now,
            new DateOnly(2026, 10, 1), 1, Now, "synthetic-source-validation", "OpenLotAcquisition", LotRecordId.ToString("D"));
        var batch = Guid.NewGuid();
        return new LedgerTaxLotRecord(LotRecordId, BookId, ShortCarrying, "short-lot-1", new DateOnly(2026, 10, 1),
            100m, openQuantity, 50m, "USD", Now, Now, Version: 1,
            OriginatingMutationBatchId: batch, LastMutationBatchId: batch, SecurityId: SecurityId, BookPositionId: Guid.NewGuid(),
            Acquisition: new OpenLotAcquisitionDto(LotQuantityBasis.Units, "USD", "USD", 1m, 5_000m, 5_000m,
                new DateOnly(2026, 10, 1), null, [evidence])
            { Direction = direction });
    }

    private static async Task<Fixture> CreateFixtureAsync(
        LedgerTaxLotRecord lot, string bookFundProfileId = FundProfileId, bool withLedgerStore = true)
    {
        var configurationStore = new InMemoryAccountingConfigurationStore();
        await configurationStore.SaveAsync(new AccountingConfigurationWorkspaceDto(
            FundProfileId,
            LedgerBookId: null,
            AccountingConfigurationStatusDto.Draft,
            "test",
            DateTimeOffset.UtcNow,
            LedgerBooks: [],
            ChartOfAccounts:
            [
                new ChartOfAccountsNodeDto("Expenses:Stock Borrow Fee", "Expenses:Stock Borrow Fee", "Stock Borrow Fee Expense", "Expense"),
                new ChartOfAccountsNodeDto("Expenses:Dividend Expense", "Expenses:Dividend Expense", "Dividend Expense", "Expense"),
                new ChartOfAccountsNodeDto("Assets:Cash", "Assets:Cash", "Cash", "Asset")
            ],
            JournalTemplates: [],
            PostingRules: [],
            ValidationIssues: [],
            AuditTrail: [], TenantId: "tenant-alpha", CompanyId: "company-alpha"));
        var configurationService = new AccountingConfigurationService(configurationStore, new InMemoryAccountingActionAuditStore());
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(draftStore, configurationService, new InMemoryAccountingActionAuditStore());
        var intake = new AutomatedJournalDraftIntakeService(workbench, draftStore, configurationService);

        var ledger = Substitute.For<ILedgerJournalStore>();
        ledger.GetLedgerBookAsync(BookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookRecord(
            BookId, bookFundProfileId, Guid.NewGuid(), FundStructureNodeKindDto.Fund, "Primary book", "USD", Now, Now));
        ledger.GetTaxLotsByIdsAsync(BookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { lot });

        var runner = new AutomatedJournalIntakeRunner(
            intake,
            new FeeScheduleAccrualEventProducer(),
            timeProvider: new FixedTimeProvider(Now),
            ledgerJournalStore: withLedgerStore ? ledger : null);
        return new Fixture(runner, workbench);
    }

    private sealed record Fixture(AutomatedJournalIntakeRunner Runner, ManualJournalEntryWorkbenchService Workbench);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
