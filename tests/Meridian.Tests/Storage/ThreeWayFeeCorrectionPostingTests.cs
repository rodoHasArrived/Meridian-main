using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Storage;

public sealed partial class ThreeWayFeeCorrectionPostingTests
{
    private const string Tenant = "synthetic-tenant";
    private const string Company = "synthetic-company";
    private static readonly DateTimeOffset FeeAt = new(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EconomicIdentity_NormalizesOwnerAndProviderButPreservesCaseSensitiveSourceIds()
    {
        var account = Guid.NewGuid();
        var book = Guid.NewGuid();
        var original = ThreeWayFeeCorrectionIdentity.Compute(Tenant, Company, "SyntheticBroker", "External-A", account, book, "Fee-A");
        ThreeWayFeeCorrectionIdentity.Compute(" SYNTHETIC-TENANT ", " SYNTHETIC-COMPANY ", " syntheticbroker ",
            "External-A", account, book, "Fee-A").Should().Be(original);
        ThreeWayFeeCorrectionIdentity.Compute(Tenant, Company, "SyntheticBroker", "external-a", account, book, "Fee-A")
            .Should().NotBe(original);
        ThreeWayFeeCorrectionIdentity.Compute(Tenant, Company, "SyntheticBroker", "External-A", account, book, "fee-a")
            .Should().NotBe(original);
    }

    [Fact]
    public void CorrectionRefusesCurrencyBlindLegsAndChangedEvidenceAmount()
    {
        var book = Book(Guid.NewGuid(), Guid.NewGuid());
        var period = Period(book.LedgerBookId);
        var write = FeeWrite(book, period, "run-1");
        var first = write.Entry.Lines[0];
        var lines = write.Entry.Lines.ToArray();
        lines[0] = new(first.EntryId, first.JournalEntryId, first.Timestamp, first.Account,
            first.Debit, first.Credit, first.Description);
        var blind = write with
        {
            Entry = new(write.Entry.JournalEntryId, write.Entry.Timestamp,
            write.Entry.Description, lines, write.Entry.Metadata)
        };
        Action postBlind = () => AccountingPostingCommandValidator.NormalizeAndValidate(blind, true, true);
        postBlind.Should().Throw<LedgerValidationException>().WithMessage("*typed USD expense/cash*");
        var altered = write with
        {
            PostingCommand = write.PostingCommand! with
            {
                ThreeWayFeeCorrection = write.PostingCommand.ThreeWayFeeCorrection! with { Amount = 2m }
            }
        };
        Action postChanged = () => AccountingPostingCommandValidator.NormalizeAndValidate(altered, true, true);
        postChanged.Should().Throw<LedgerValidationException>().WithMessage("*typed USD expense/cash*");
    }

    [Fact]
    public void LinkedReversal_KeepsSourceEvidenceWithoutClaimingOriginalFeeUniqueness()
    {
        var book = Book(Guid.NewGuid(), Guid.NewGuid());
        var original = AccountingPostingCommandValidator.NormalizeAndValidate(FeeWrite(book, Period(book.LedgerBookId), "run-1"), true, true);
        var reversalId = Guid.NewGuid();
        var key = "reviewed-fee-reversal:" + reversalId.ToString("D");
        var reversed = new JournalEntry(reversalId, original.Entry.Timestamp, original.Entry.Description,
            original.Entry.Lines.Select(line => new LedgerEntry(Guid.NewGuid(), reversalId, line.Timestamp,
                line.Account, line.Credit, line.Debit, line.Description, line.Dimensions,
                new LedgerEntryCurrency("USD", "USD", line.Credit, line.Debit, 1m))).ToArray(),
            original.Entry.Metadata with
            {
                IdempotencyKey = key,
                ActivityType = "Reversal",
                Tags = original.Entry.Metadata.Tags!.Where(pair => pair.Key.StartsWith("fee.", StringComparison.Ordinal)
                    || pair.Key == "externalTransactionId").ToDictionary(pair => pair.Key, pair => pair.Value)
            });
        var write = original with
        {
            Entry = reversed,
            CommandId = reversalId,
            SourceEventId = reversalId,
            SourceJournalEntryId = original.Entry.JournalEntryId,
            PostingCommand = original.PostingCommand! with
            {
                CommandId = reversalId,
                SourceEventId = reversalId,
                IdempotencyKey = key,
                SourceEventType = "manual.reversal",
                Intent = AccountingPostingIntentDto.Reversal,
                SourceJournalEntryId = original.Entry.JournalEntryId,
                ThreeWayFeeCorrection = null,
                TreasuryContext = null
            }
        };

        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(write, true, true);

        normalized.Entry.Metadata.Tags.Should().NotContainKey("fee.economicBookId");
        normalized.Entry.Metadata.Tags.Should().NotContainKey("fee.economicAccountId");
        normalized.Entry.Metadata.Tags.Should().NotContainKey("fee.externalTransactionId");
        normalized.Entry.Metadata.Tags!["externalTransactionId"].Should().Be("Fee-A");
        normalized.SourceJournalEntryId.Should().Be(original.Entry.JournalEntryId);
        normalized.Entry.IsBalanced.Should().BeTrue();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task PostedCorrection_IsTypedAndReconcilesCash_WhileAnotherRunCannotPostTheFeeAgain()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var accountId = Guid.NewGuid();
        var book = Book(Guid.NewGuid(), accountId);
        await new PostgresFundProfileTenancyRegistry(database.Options).BindAsync(book.FundProfileId, Tenant, Company);
        await database.JournalStore.SaveLedgerBookAsync(book);
        var period = await database.JournalStore.SavePeriodAsync(Period(book.LedgerBookId), 0);
        await database.JournalStore.AppendAsync(Opening(book, period));
        var correction = FeeWrite(book, period, "comparison-run-1");
        using var target = new DurableLedgerPostingTarget(database.JournalStore);

        (await target.PostAsync(correction)).WasAppended.Should().BeTrue();
        (await target.PostAsync(correction)).WasAppended.Should().BeFalse();
        var anotherRunCopy = FeeWrite(book, period, "comparison-run-2");
        anotherRunCopy.Entry.JournalEntryId.Should().NotBe(correction.Entry.JournalEntryId);
        anotherRunCopy.SourceEventId.Should().Be(correction.SourceEventId);
        anotherRunCopy.Entry.Metadata.IdempotencyKey.Should().Be(correction.Entry.Metadata.IdempotencyKey);
        Func<Task> duplicate = () => target.PostAsync(anotherRunCopy);
        await duplicate.Should().ThrowAsync<LedgerValidationException>();

        var entries = await database.JournalStore.GetByPeriodAsync(period.PeriodId);
        entries.Should().HaveCount(2, "one opening plus one reviewed correction is the complete posted population");
        var posted = entries.Single(record => record.SourceEventId == correction.SourceEventId);
        posted.PostingKind.Should().Be(LedgerPostingKindDto.Adjustment);
        posted.Entry.Lines.Should().OnlyContain(line => line.Currency != null &&
            line.Currency.TransactionCurrency == "USD" && line.Currency.FunctionalCurrency == "USD" && line.Currency.FxRateToFunctional == 1m);
        var cash = entries.SelectMany(record => record.Entry.Lines).Where(line => line.Account.Name == "Cash")
            .Sum(line => line.Debit - line.Credit);
        cash.Should().Be(99m);
        var common = ThreeWayReconciliationOptions.SupportedBasisConvention;
        var sources = new ThreeWaySourceSnapshotDto[]
        {
            new("ledger", FeeAt, "synthetic:posted-ledger", cash, [], common, [], "simulated"),
            new("oms", FeeAt, "synthetic:independent-oms", 99m, [], common, [], "simulated"),
            new("broker", FeeAt, "synthetic:independent-broker-fee", 99m, [], common, [], "simulated")
        };
        var checks = ThreeWayReconciliationComparer.Compare(sources, FeeAt,
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5));
        checks.Should().OnlyContain(check => check.Status == "Matched");
        ThreeWayAdjustmentProposalBuilder.Build(sources, checks, FeeAt).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AutomaticAndReviewedFeePaths_ShareEconomicUniquenessInBothPostingOrders()
    {
        foreach (var automaticFirst in new[] { true, false })
        {
            await using var database = await LedgerPostgresTestDatabase.CreateAsync();
            var book = Book(Guid.NewGuid(), Guid.NewGuid());
            var period = await SeedBookAsync(database, book);
            var correction = FeeWrite(book, period, "comparison-run-1");
            var automated = await AutomaticFeeAsync(database, book, period);
            using var target = new DurableLedgerPostingTarget(database.JournalStore);
            var first = automaticFirst ? automated : correction;
            var second = automaticFirst ? correction : automated;

            (await target.PostAsync(first)).WasAppended.Should().BeTrue();
            Func<Task> duplicateEconomicFee = () => target.PostAsync(second);
            await duplicateEconomicFee.Should().ThrowAsync<LedgerValidationException>();

            var retained = await database.JournalStore.GetByPeriodAsync(period.PeriodId);
            retained.Should().ContainSingle();
            retained.Sum(record => record.Entry.Lines.Where(line => line.Account.Name == "Fees").Sum(line => line.Debit))
                .Should().Be(1m);
            (await target.PostAsync(first)).WasAppended.Should().BeFalse("the winner's exact receipt remains replayable");
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentAutomaticAndReviewedFee_PostOnlyOneEconomicEffect()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var book = Book(Guid.NewGuid(), Guid.NewGuid());
        var period = await SeedBookAsync(database, book);
        var correction = FeeWrite(book, period, "comparison-run-1");
        var automated = await AutomaticFeeAsync(database, book, period);
        var attempts = await Task.WhenAll(TryAppend(correction), TryAppend(automated));

        attempts.Count(success => success).Should().Be(1);
        var retained = await database.JournalStore.GetByPeriodAsync(period.PeriodId);
        retained.Should().ContainSingle();
        retained.Single().Entry.Lines.Single(line => line.Account.Name == "Cash").Credit.Should().Be(1m);
        using var target = new DurableLedgerPostingTarget(database.JournalStore);
        Func<Task> retryLoser = () => target.PostAsync(attempts[0] ? automated : correction);
        await retryLoser.Should().ThrowAsync<LedgerValidationException>();

        async Task<bool> TryAppend(LedgerJournalEntryWrite write)
        {
            try
            { await database.JournalStore.AppendAsync(write); return true; }
            catch (Exception exception) when (exception is LedgerValidationException or Npgsql.PostgresException) { return false; }
        }
    }

    private static async Task<LedgerAccountingPeriod> SeedBookAsync(LedgerPostgresTestDatabase database, LedgerBookRecord book)
    {
        await new PostgresFundProfileTenancyRegistry(database.Options).BindAsync(book.FundProfileId, Tenant, Company);
        await database.JournalStore.SaveLedgerBookAsync(book);
        return await database.JournalStore.SavePeriodAsync(Period(book.LedgerBookId), 0);
    }

    private static async Task<LedgerJournalEntryWrite> AutomaticFeeAsync(LedgerPostgresTestDatabase database,
        LedgerBookRecord book, LedgerAccountingPeriod period)
    {
        const string mapping = "synthetic-fee-mapping-v1";
        var accountId = book.FundStructureNodeId.ToString("D");
        await database.AccountingConfigurationStore.SaveAsync(new AccountingConfigurationWorkspaceDto(book.FundProfileId,
            book.LedgerBookId, AccountingConfigurationStatusDto.Active, mapping, FeeAt, [],
            [new("cash", "Assets:Cash", "Cash", "Asset"), new("fee", "Expenses:Fees", "Fees", "Expense")],
            [], [], [], [], TenantId: Tenant, CompanyId: Company));
        var policy = await new AccountingPolicyService().CreatePolicyAsync(new CreateAccountingPolicyRequest(
            book.AccountingBasis, book.AccountingPolicyId, book.AccountingPolicyVersion, "Synthetic fee policy", period.StartDate,
            RulePack: new("synthetic-fees", "v1",
            [
                new("execution.trade-fill", AccountingTreatmentKindDto.General, SourceEventType: "execution.trade-fill", RequiresApproval: false, AllowsAutoPosting: true),
                new("execution.trade-commission", AccountingTreatmentKindDto.Expense, SourceEventType: "execution.trade-commission", RequiresApproval: false, AllowsAutoPosting: true),
                new("execution.trade-fee", AccountingTreatmentKindDto.Expense, SourceEventType: "execution.trade-fee", RequiresApproval: false, AllowsAutoPosting: true)
            ])));
        var grant = await database.JournalStore.ActivateWithPolicyAsync(new ActivateAutomatedPostingGrantRequest(Guid.NewGuid(),
            accountId, book.LedgerBookId, period.PeriodId, book.AccountingPolicyId, book.AccountingPolicyVersion,
            mapping, CompanyId: Company), policy, "grant-operator", Tenant);
        var original = FeeWrite(book, period, "not-a-comparison");
        var id = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var key = "automatic-fill-fee:" + id.ToString("D");
        var journal = new JournalEntry(id, original.Entry.Timestamp, original.Entry.Description,
            original.Entry.Lines.Select(line => new LedgerEntry(Guid.NewGuid(), id, line.Timestamp, line.Account,
                line.Debit, line.Credit, line.Description, line.Dimensions, line.Currency)).ToArray(),
            original.Entry.Metadata with { ActivityType = "execution.trade-fee", IdempotencyKey = key });
        return original with
        {
            Entry = journal,
            CommandId = id,
            SourceEventId = sourceId,
            PostingKind = LedgerPostingKindDto.Originating,
            RuleId = "execution.trade-fee",
            RuleVersion = "v1",
            AdjustmentApproval = null,
            PostingCommand = original.PostingCommand! with
            {
                CommandId = id,
                SourceEventId = sourceId,
                IdempotencyKey = key,
                Intent = AccountingPostingIntentDto.Originating,
                SourceEventType = "execution.trade-fee",
                ActionOrigin = OperationsActionOriginDto.AuthorizedSystem,
                ApprovalState = AccountingPostingApprovalStateDto.NotRequired,
                ApprovalId = null,
                ThreeWayFeeCorrection = null,
                AutomationGrant = grant.Reference,
                Actor = "authorized-synthetic-fill-accounting",
                TreasuryContext = null
            }
        };
    }

    private static LedgerBookRecord Book(Guid bookId, Guid accountId)
        => new(bookId, "synthetic-fee-correction", accountId, FundStructureNodeKindDto.Account,
            "Synthetic fee correction", "USD", FeeAt, FeeAt);

    private static LedgerAccountingPeriod Period(Guid bookId)
        => new(Guid.NewGuid(), bookId, 2026, 5, "Synthetic May", new(2026, 5, 1), new(2026, 5, 31),
            "Open", FeeAt.AddDays(-14), null, 0);

    private static LedgerJournalEntryWrite Opening(LedgerBookRecord book, LedgerAccountingPeriod period)
    {
        var id = Guid.NewGuid();
        var at = FeeAt.AddMinutes(-5);
        var account = book.FundStructureNodeId.ToString("D");
        const string description = "Reviewed synthetic opening";
        var entry = new JournalEntry(id, at, description,
        [
            new(Guid.NewGuid(), id, at, new("Cash", LedgerAccountType.Asset, FinancialAccountId: account),
                100m, 0m, description, currency: new("USD", "USD", 100m, 0m, 1m)),
            new(Guid.NewGuid(), id, at, new("Capital", LedgerAccountType.Equity, FinancialAccountId: account),
                0m, 100m, description, currency: new("USD", "USD", 0m, 100m, 1m))
        ], new(ActivityType: "opening-balance", FinancialAccountId: account,
            EffectiveDate: DateOnly.FromDateTime(at.UtcDateTime), IdempotencyKey: id.ToString("D")));
        var command = new AccountingPostingCommandDto(id, book.LedgerBookId, period.PeriodId,
            DateOnly.FromDateTime(at.UtcDateTime), at, id.ToString("D"), ExpectedVersion: period.Version,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "opening-review",
            OperatorRationale: "Reviewed synthetic opening", LedgerBookId: book.LedgerBookId)
        { Actor = "opening-poster", Provenance = DataProvenance.Simulated };
        return new(entry, book.LedgerBookId, period.PeriodId, CommandId: id, PostingCommand: command,
            LedgerBookId: book.LedgerBookId);
    }

    private static LedgerJournalEntryWrite FeeWrite(LedgerBookRecord book, LedgerAccountingPeriod period, string run)
    {
        var journalId = Guid.NewGuid();
        var account = book.FundStructureNodeId.ToString("D");
        var correction = new ThreeWayFeeCorrectionEvidenceDto("", Tenant, Company, "SyntheticBroker", "External-A",
            "Fee-A", book.FundStructureNodeId, book.LedgerBookId, run, new string('a', 64), 1m, "USD", FeeAt);
        correction = correction with { EconomicKey = ThreeWayFeeCorrectionIdentity.Compute(correction) };
        var key = "three-way-correction:" + correction.EconomicKey;
        var sourceId = ThreeWayFeeCorrectionIdentity.SourceEventId(correction);
        const string description = "Reviewed missing synthetic broker fee";
        var entry = new JournalEntry(journalId, FeeAt, description,
        [
            new(Guid.NewGuid(), journalId, FeeAt, new("Fees", LedgerAccountType.Expense, FinancialAccountId: account),
                1m, 0m, description, currency: new("USD", "USD", 1m, 0m, 1m)),
            new(Guid.NewGuid(), journalId, FeeAt, new("Cash", LedgerAccountType.Asset, FinancialAccountId: account),
                0m, 1m, description, currency: new("USD", "USD", 0m, 1m, 1m))
        ], new(ActivityType: "ManualJournalEntry", FinancialAccountId: account,
            EffectiveDate: DateOnly.FromDateTime(FeeAt.UtcDateTime), IdempotencyKey: key,
            Tags: new Dictionary<string, string> { ["externalTransactionId"] = correction.FeeTransactionId }));
        var command = new AccountingPostingCommandDto(journalId, book.LedgerBookId, period.PeriodId,
            DateOnly.FromDateTime(FeeAt.UtcDateTime), FeeAt.AddHours(2), key, AccountingPostingIntentDto.Adjustment,
            SourceEventId: sourceId, ExpectedVersion: period.Version, SourceEventType: "reconciliation.missing-broker-fee",
            TreasuryContext: new(EffectiveDate: DateOnly.FromDateTime(FeeAt.UtcDateTime), IdempotencyKey: key,
                SettlementReference: "three-way-fee:" + correction.FeeTransactionId),
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-fee-review",
            Evidence: [new("source-fee", "synthetic://broker/fees/Fee-A", AccountingPostingEvidenceKindDto.Source,
                "Synthetic", FeeAt, "fee-preparer", ContentHash: correction.SourceEvidenceHash)], LedgerBookId: book.LedgerBookId)
        { Actor = "fee-poster", Provenance = DataProvenance.Simulated, ThreeWayFeeCorrection = correction };
        return new(entry, book.LedgerBookId, period.PeriodId, CommandId: journalId, SourceEventId: sourceId,
            PostingKind: LedgerPostingKindDto.Adjustment,
            AdjustmentApproval: new("independent-fee-review", LedgerAdjustmentApprovalStatusDto.Approved,
                "independent-fee-reviewer", FeeAt.AddHours(1), "reviewed-adjusting-entry",
                EvidenceLink: "synthetic://broker/fees/Fee-A"),
            PostingCommand: command, LedgerBookId: book.LedgerBookId);
    }
}
