using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;
using NSubstitute;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task BondCouponSink_TwoPhysicalLotsShareOneReceipt_AndReversalReleasesOnlyItsShare()
    {
        await using var fixture = await CouponSinkFixture.CreateAsync();
        var first = fixture.Instruction(0, "lot-one", 45m, 50m, 3m, 2m);
        var second = fixture.Instruction(1, "lot-two", 45m, 50m, 3m, 2m);
        var firstWrite = fixture.Write(first);
        await fixture.Store.AppendAsync(firstWrite);
        await fixture.Store.AppendAsync(fixture.Write(second));

        (await fixture.Store.GetConsumedCouponAllocationAsync(fixture.Scope, "shared-allocation"))
            .Should().Be(new BondCouponAllocationConsumptionDto(90m, 100m, 6m, 4m));
        (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "bank-coupon")).Should().Be(90m);
        var receipt = await fixture.Store.GetCouponRecognitionRunAsync(fixture.Scope, fixture.Lots[0].TaxLotRecordId, "lot-one");
        receipt.Should().NotBeNull();
        var generic = fixture.Write(fixture.Reverse(receipt!, "generic-reversal"));
        var genericEvent = generic.PostingCommand!.EconomicEvent! with { SourceDomain = "ManualAdjustment" };
        generic = generic with
        {
            PostingCommand = generic.PostingCommand with
            {
                BondCouponRecognition = null,
                EconomicEvent = genericEvent,
                ProjectionLineage = generic.PostingCommand.ProjectionLineage! with
                { ModelKey = "manual-adjustment", TriggerEvent = genericEvent }
            }
        };
        await fixture.Store.Invoking(store => store.AppendAsync(generic)).Should().ThrowAsync<LedgerValidationException>();
        (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "bank-coupon")).Should().Be(90m);
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(4);
        var reversed = fixture.Reverse(receipt!, "reverse-lot-one");
        await fixture.Store.AppendAsync(fixture.Write(reversed));
        (await fixture.Store.GetConsumedCouponAllocationAsync(fixture.Scope, "shared-allocation"))
            .Should().Be(new BondCouponAllocationConsumptionDto(45m, 50m, 3m, 2m));
        (await fixture.Store.GetConsumedCouponObligationAsync(fixture.Scope, fixture.Lots[0].TaxLotRecordId, "coupon-obligation")).Should().Be(0m);
        (await fixture.Store.GetConsumedCouponObligationAsync(fixture.Scope, fixture.Lots[1].TaxLotRecordId, "coupon-obligation")).Should().Be(50m);

        var reversalReceipt = await fixture.Store.GetLatestCouponRecognitionAsync(fixture.Scope, fixture.Lots[0].TaxLotRecordId);
        var rebook = fixture.Instruction(0, "rebook-lot-one", 45m, 50m, 3m, 2m) with { PreviousReceiptId = reversalReceipt!.ReceiptId };
        await fixture.Store.AppendAsync(fixture.Write(rebook));
        (await fixture.Store.GetConsumedCouponAllocationAsync(fixture.Scope, "shared-allocation"))
            .Should().Be(new BondCouponAllocationConsumptionDto(90m, 100m, 6m, 4m));
        var after = await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, fixture.Lots.Select(lot => lot.TaxLotRecordId).ToArray());
        after.Select(lot => lot.Version).Should().Equal(1, 1);
        after.Sum(lot => lot.OpenQuantity).Should().Be(200m);
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(6);
        await fixture.Authority.Received(4).VerifyPreparedSourceAsync(Arg.Any<BondCouponRecognitionInstructionDto>(),
            Arg.Any<RetainedEvidenceIdentityDto>(), Arg.Any<CancellationToken>());
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task BondCouponSink_NegativeCouponAndFeeSharesPersistSignedCash_AndCannotOverconsumeFees()
    {
        await using var fixture = await CouponSinkFixture.CreateAsync(negative: true);
        var first = fixture.Instruction(0, "negative-one", -55m, -50m, 0m, 5m);
        await fixture.Store.AppendAsync(fixture.Write(first));
        // Net cash remains within the retained -110, but the claimed fee total would be 11 > 10.
        var tooMuchFee = fixture.Instruction(1, "negative-fee-overclaim", -55m, -49m, 0m, 6m);
        await fixture.Store.Invoking(store => store.AppendAsync(fixture.Write(tooMuchFee))).Should()
            .ThrowAsync<LedgerValidationException>().WithMessage("*source net, gross, tax or fee allocation*");
        (await fixture.Store.GetCouponRecognitionRunAsync(fixture.Scope, fixture.Lots[1].TaxLotRecordId, tooMuchFee.Request.RunId)).Should().BeNull();
        (await fixture.Store.GetConsumedCouponAllocationAsync(fixture.Scope, "shared-allocation"))
            .Should().Be(new BondCouponAllocationConsumptionDto(-55m, -50m, 0m, 5m));
        var second = fixture.Instruction(1, "negative-two", -55m, -50m, 0m, 5m);
        await fixture.Store.AppendAsync(fixture.Write(second));
        (await fixture.Store.GetConsumedCouponAllocationAsync(fixture.Scope, "shared-allocation"))
            .Should().Be(new BondCouponAllocationConsumptionDto(-110m, -100m, 0m, 10m));
        var journals = (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId))
            .Where(row => row.SourceEventId == first.EventId || row.SourceEventId == second.EventId).ToArray();
        journals.Should().HaveCount(2);
        journals.Sum(row => row.Entry.Lines.Where(line => line.Account.Name == "CouponCash").Sum(line => line.Credit - line.Debit)).Should().Be(110m);
        journals.Sum(row => row.Entry.Lines.Where(line => line.Account.Name == "CouponFees").Sum(line => line.Debit - line.Credit)).Should().Be(10m);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task BondCouponSink_LateJournalInsertFailureRollsBackReceiptAndCashClaims_AndAllowsRetry()
    {
        await using var fixture = await CouponSinkFixture.CreateAsync();
        var instruction = fixture.Instruction(0, "rollback-share", 45m, 50m, 3m, 2m);
        var duplicateJournal = fixture.Write(instruction, fixture.Lots[0].SourceJournalEntryId);
        await fixture.Store.Invoking(store => store.AppendAsync(duplicateJournal)).Should().ThrowAsync<PostgresException>()
            .Where(error => error.SqlState == PostgresErrorCodes.UniqueViolation);
        (await fixture.Store.GetCouponRecognitionRunAsync(fixture.Scope, fixture.Lots[0].TaxLotRecordId, instruction.Request.RunId)).Should().BeNull();
        (await fixture.Store.GetConsumedCouponAllocationAsync(fixture.Scope, "shared-allocation"))
            .Should().Be(new BondCouponAllocationConsumptionDto(0m, 0m, 0m, 0m));
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(2);
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lots[0].TaxLotRecordId])).Single().Version.Should().Be(1);
        await fixture.Store.AppendAsync(fixture.Write(instruction));
        (await fixture.Store.GetConsumedCouponAllocationAsync(fixture.Scope, "shared-allocation"))
            .Should().Be(new BondCouponAllocationConsumptionDto(45m, 50m, 3m, 2m));
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task BondCouponSink_RetainsUnroundedTarget_AndReceiptLinesExactlyEqualTenDecimalJournal()
    {
        await using var fixture = await CouponSinkFixture.CreateAsync();
        const decimal raw = 1.234567890123m;
        var instruction = fixture.AccrualInstruction(0, "unrounded-accrual", raw);
        var write = fixture.Write(instruction);
        await fixture.Store.AppendAsync(write);
        var receipt = await fixture.Store.GetCouponRecognitionRunAsync(fixture.Scope, fixture.Lots[0].TaxLotRecordId,
            instruction.Request.RunId);
        var targets = receipt!.Instruction.BookedTargets!;
        targets.RawReceivableEarned.Should().Be(raw);
        targets.BookedReceivableEarned.Should().Be(1.2345678901m);
        targets.ReceivableRoundingResidual.Should().Be(0.000000000023m);
        (targets.BookedReceivableEarned + targets.ReceivableRoundingResidual).Should().Be(raw);
        var journal = (await fixture.Store.QueryAsync(new LedgerJournalEntryQuery(JournalEntryId: write.Entry.JournalEntryId))).Single();
        foreach (var retained in receipt.Instruction.Lines)
        {
            var name = receipt.Instruction.Accounts.Single(account => account.Path == retained.AccountId).LedgerName;
            var actual = journal.Entry.Lines.Single(line => line.Account.Name == name);
            actual.Debit.Should().Be(retained.Debit);
            actual.Credit.Should().Be(retained.Credit);
            actual.Currency!.TransactionDebit.Should().Be(retained.Debit);
            actual.Currency!.TransactionCredit.Should().Be(retained.Credit);
        }
        journal.Entry.Lines.Single(line => line.Account.Name == "CouponReceivable").Debit.Should().Be(targets.BookedReceivableEarned);
        journal.Entry.Lines.Single(line => line.Account.Name == "CouponIncome").Credit.Should().Be(targets.BookedReceivableEarned);
        (await fixture.Store.GetConsumedCouponAllocationAsync(fixture.Scope, "shared-allocation"))
            .Should().Be(new BondCouponAllocationConsumptionDto(0m, 0m, 0m, 0m));
    }

    private sealed class CouponSinkFixture : IAsyncDisposable
    {
        private static readonly DateOnly Acquired = new(2026, 5, 12), Through = new(2026, 5, 20);
        private static readonly DateTimeOffset At = new(2026, 5, 20, 14, 0, 0, TimeSpan.Zero);
        private static readonly BondCouponBookAccountsDto Paths = new("CouponReceivable", "CouponPayable", "CouponIncome", "CouponCash", "CouponTax", "CouponFees");
        private static readonly BondCouponRecognitionAccountDto[] Accounts =
        [new("CouponReceivable", "CouponReceivable", "Asset"), new("CouponPayable", "CouponPayable", "Liability"),
         new("CouponIncome", "CouponIncome", "Revenue"), new("CouponCash", "CouponCash", "Asset"),
         new("CouponTax", "CouponTax", "Asset"), new("CouponFees", "CouponFees", "Expense")];
        private readonly LedgerPostgresTestDatabase _database;
        private readonly LotReferenceAuthority _reference;
        private readonly bool _negative;
        public Guid BookId => Guid.Parse(Scope.BookId);
        public AccountCashForecastScopeDto Scope { get; }
        public LedgerAccountingPeriod Period { get; }
        public PostgresLedgerJournalStore Store { get; }
        public IBondCouponRecognitionService Authority { get; } = Substitute.For<IBondCouponRecognitionService>();
        private IBondIncomeService IncomeAuthority { get; } = Substitute.For<IBondIncomeService>();
        public LedgerTaxLotRecord[] Lots { get; private set; } = [];

        private CouponSinkFixture(LedgerPostgresTestDatabase database, LotReferenceAuthority reference,
            AccountCashForecastScopeDto scope, LedgerAccountingPeriod period, bool negative)
        {
            _database = database;
            _reference = reference;
            Scope = scope;
            Period = period;
            _negative = negative;
            Authority.VerifyPreparedSourceAsync(Arg.Any<BondCouponRecognitionInstructionDto>(),
                Arg.Any<RetainedEvidenceIdentityDto>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            IncomeAuthority.RequireAcquisitionPolicyAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(),
                Arg.Any<BondIncomeAcquisitionDto>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Store = new(database.Options, backfillSecurityMaster: () => reference.Securities,
                backfillPositions: () => reference.Positions, bondIncomeAuthority: () => IncomeAuthority,
                couponRecognitionAuthority: () => Authority);
        }

        public static async Task<CouponSinkFixture> CreateAsync(bool negative = false)
        {
            var database = await LedgerPostgresTestDatabase.CreateAsync();
            try
            {
                var bookId = Guid.NewGuid();
                var accountId = Guid.NewGuid();
                await database.JournalStore.SaveLedgerBookAsync(new(bookId, "coupon-owner", accountId, FundStructureNodeKindDto.Account,
                    "Coupon sink fixture", "USD", At.AddDays(-20), At, AccountingBasis: AccountingBasisKindDto.Gaap,
                    AccountingPolicyId: "coupon-policy", AccountingPolicyVersion: "1"));
                var period = await database.JournalStore.SavePeriodAsync(new(Guid.NewGuid(), bookId, 2026, 5, "Coupon May",
                    new(2026, 5, 1), new(2026, 5, 31), "Open", At.AddDays(-20), null, 0), 0);
                var reference = await database.SeedLotReferenceAuthorityAsync(bookId, TestSecurityId, TestBookPositionId);
                await reference.Securities.UpsertProjectionAsync(reference.Security with { AssetClass = "Bond", Version = 2 });
                reference = reference with { Security = (await reference.Securities.GetProjectionAsync(TestSecurityId))! };
                var fixture = new CouponSinkFixture(database, reference,
                    new("coupon-tenant", "coupon-company", accountId.ToString("D"), bookId.ToString("D"), "USD"), period, negative);
                fixture.Lots = [await fixture.AcquireAsync("coupon-lot-one"), await fixture.AcquireAsync("coupon-lot-two")];
                return fixture;
            }
            catch { await database.DisposeAsync(); throw; }
        }

        private async Task<LedgerTaxLotRecord> AcquireAsync(string lotName)
        {
            var command = BuildAcquisitionCommand(BookId, Period.PeriodId, Period.Version, lotName);
            var lot = command.AcquisitionLot!;
            var source = BuildEvidence(lotName, 'a') with { SubjectType = "OpenLotAcquisition", SubjectId = lot.TaxLotRecordId.ToString("D") };
            var acquisition = new OpenLotAcquisitionDto(LotQuantityBasis.Face, "USD", "USD", 1m, 10_000m, 10_000m, Acquired,
                new(100m, 1m, BondAmortizationMethod.NoAmortization, null), [source])
            { BondIncome = new(source.RetainedAtUtc, 10_000m, 0m, 1m, [], [source]) };
            lot = lot with
            {
                LotId = lotName,
                Account = new("Investment lots", LedgerAccountType.Asset, FinancialAccountId: Scope.AccountId),
                OriginalFace = 10_000m,
                BookedFactor = 1m,
                ParBasis = 100m,
                EvidenceRef = source.EvidenceId,
                Acquisition = acquisition
            };
            var lines = command.Journal.Entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId,
                line.Timestamp, line.Debit > 0m ? lot.Account : new("Cash", LedgerAccountType.Asset, FinancialAccountId: Scope.AccountId),
                line.Debit, line.Credit, line.Description, new LedgerLineDimensionSet(BookId: Scope.BookId, AccountId: Scope.AccountId,
                    InstrumentId: lot.SecurityId, TaxLotId: lot.LotId)
                { PositionId = lot.BookPositionId }, line.Currency)).ToArray();
            var entry = new JournalEntry(command.Journal.Entry.JournalEntryId, command.Journal.Entry.Timestamp,
                command.Journal.Entry.Description, lines, command.Journal.Entry.Metadata);
            command = (command with
            {
                AcquisitionLot = lot,
                RetainedEvidence = [source],
                ReferenceVersions = _reference.Versions,
                Journal = command.Journal with
                {
                    Entry = entry,
                    AccountingBasis = AccountingBasisKindDto.Gaap,
                    AccountingPolicyId = "coupon-policy",
                    AccountingPolicyVersion = "1"
                }
            }).WithComputedFingerprint();
            return (await Store.AppendAssetPostingAsync(command)).MutatedLots.Single();
        }

        public BondCouponRecognitionInstructionDto Instruction(int lotIndex, string runId, decimal net, decimal gross, decimal tax, decimal fees)
        {
            var physical = Lots[lotIndex];
            var lot = physical.ToOpenLot();
            var paymentEvidence = BuildEvidence("bank-coupon", 'b') with
            {
                EffectiveDate = Through,
                SubjectType = "AccountCashFlow",
                SubjectId = "bank-coupon",
                ReviewedAtUtc = At.AddMinutes(-30),
                RetainedAtUtc = At.AddMinutes(-15)
            };
            var provenance = new AccountCashProvenanceDto("BrokerStatement", "bank-coupon", paymentEvidence.ContentHashSha256,
                AccountCashEvidenceRole.Observation, paymentEvidence);
            var deductions = provenance with
            {
                SourceEntityId = "coupon-deductions",
                Evidence = paymentEvidence with
                { EvidenceId = "coupon-deductions", SubjectType = "AccountCashAllocation", SubjectId = "shared-allocation" }
            };
            var payment = new AccountCashFlowDto("bank-coupon", "bank-coupon-v1", Scope.AccountId, Scope.BookId,
                AccountCashFlowStatus.Actual, "Coupon", _negative ? -110m : 90m, "USD", Through, Through, Through, provenance, lot.SecurityId);
            var full = new AccountCashPaymentAllocationDto("shared-allocation", "coupon-obligation", payment.VersionId,
                payment.Amount, _negative ? -100m : 100m, _negative ? 0m : 6m, _negative ? 10m : 4m, deductions);
            var share = new BondCouponCashAllocationDto(full.AllocationId, payment.VersionId, net, gross, tax, fees, deductions);
            var request = new BondCouponRecognitionRequest(runId, physical.TaxLotRecordId, Period.PeriodId, Through, At, [share]);
            var earned = gross;
            var lines = BondCouponRecognitionCalculator.LinesBySide("USD", Paths, Math.Max(earned, 0m), Math.Max(-earned, 0m),
                Math.Max(earned, 0m), Math.Max(-earned, 0m), [share]);
            var dimensions = new LedgerDimensionSetDto(BookId: Scope.BookId, AccountId: Scope.AccountId,
                InstrumentId: lot.SecurityId, TaxLotId: lot.LotId)
            { PositionId = lot.BookPositionId };
            lines = lines.Select(line => line with { Dimensions = dimensions }).ToArray();
            var securityEvidence = BuildEvidence("coupon-reference", 'c') with
            {
                EffectiveDate = Through,
                SubjectType = "SecurityMasterProjection",
                SubjectId = lot.SecurityId.ToString("D"),
                ContentHashSha256 = OpenLotAmortization.SecurityHash(_reference.Security)
            };
            var income = new OpenLotAmortizationInstructionDto(lot, _reference.Security, securityEvidence,
                _reference.Position.Version, Through, BondIncomeCalculator.ModelVersion);
            return new(BondCouponRecognitionCalculator.ModelVersion, Scope, request, income, Guid.NewGuid(), null,
                earned, 0m, 0m, [new(share, payment.Amount, payment, full)], Accounts, lines)
            {
                CumulativeReceivableEarned = Math.Max(earned, 0m),
                CumulativePayableEarned = Math.Max(-earned, 0m),
                BookedTargets = new(BondIncomeBookedPrecision.PolicyVersion, Math.Max(earned, 0m), Math.Max(earned, 0m), 0m,
                    Math.Max(-earned, 0m), Math.Max(-earned, 0m), 0m, 0m, 0m)
            };
        }

        public BondCouponRecognitionInstructionDto AccrualInstruction(int lotIndex, string runId, decimal raw)
        {
            var instruction = Instruction(lotIndex, runId, 45m, 50m, 3m, 2m);
            var booked = BondIncomeBookedPrecision.RoundTarget(raw);
            var lines = BondCouponRecognitionCalculator.LinesBySide("USD", Paths, Math.Max(booked, 0m), Math.Max(-booked, 0m),
                Math.Max(booked, 0m), Math.Max(-booked, 0m), []);
            return instruction with
            {
                Request = instruction.Request with { CashAllocations = [] },
                Cash = [],
                CumulativeEarned = booked,
                CumulativeReceivableEarned = Math.Max(booked, 0m),
                CumulativePayableEarned = Math.Max(-booked, 0m),
                Lines = lines.Select(line => line with { Dimensions = instruction.Lines[0].Dimensions }).ToArray(),
                BookedTargets = new(BondIncomeBookedPrecision.PolicyVersion, Math.Max(raw, 0m), Math.Max(booked, 0m),
                    Math.Max(raw, 0m) - Math.Max(booked, 0m), Math.Max(-raw, 0m), Math.Max(-booked, 0m),
                    Math.Max(-raw, 0m) - Math.Max(-booked, 0m), 0m, 0m)
            };
        }

        public BondCouponRecognitionInstructionDto Reverse(BondCouponRecognitionReceiptDto target, string runId)
            => target.Instruction with
            {
                Request = target.Instruction.Request with
                {
                    RunId = runId,
                    CashAllocations = [],
                    ReversesReceiptId = target.ReceiptId,
                    CorrectionRationale = "Reverse only this lot's share"
                },
                EventId = Guid.NewGuid(),
                PreviousReceiptId = target.ReceiptId,
                ReversalTarget = target,
                CumulativeEarned = 0m,
                PreviousEarned = target.Instruction.CumulativeEarned,
                CumulativeReceivableEarned = 0m,
                CumulativePayableEarned = 0m,
                Cash = [],
                BookedTargets = new(BondIncomeBookedPrecision.PolicyVersion, 0m, 0m, 0m, 0m, 0m, 0m,
                    target.Instruction.BookedTargets!.RawReceivableEarned, target.Instruction.BookedTargets.RawPayableEarned),
                Lines = target.Instruction.Lines.Select(line => line with { Debit = line.Credit, Credit = line.Debit }).ToArray()
            };

        public LedgerJournalEntryWrite Write(BondCouponRecognitionInstructionDto instruction, Guid? journalId = null)
        {
            var id = journalId ?? Guid.NewGuid();
            var lot = instruction.IncomeInstruction.ExpectedLot;
            var fingerprint = BondCouponRecognitionCalculator.Fingerprint(instruction);
            var source = BuildEvidence("coupon-source-" + instruction.Request.RunId, 'd') with
            {
                EffectiveDate = Through,
                SubjectType = AssetAccountingEvidenceSubjects.Event,
                SubjectId = instruction.EventId.ToString("D"),
                ContentHashSha256 = fingerprint,
                ReviewedAtUtc = At.AddMinutes(-10),
                RetainedAtUtc = At.AddMinutes(-5)
            };
            // Preparation retains original acquisition observations without redating them to this
            // coupon event. The final normalizer must admit these exact older typed identities.
            var retained = lot.Acquisition.Evidence.Append(source).Distinct().ToArray();
            var economic = new EconomicEventReferenceDto(instruction.EventId, AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Income),
                1, Through, source.RetainedAtUtc, "BondCouponRecognition", source.SourceReference, SourceContentHash: fingerprint)
            { SecurityId = lot.SecurityId, BookPositionId = lot.BookPositionId, RetainedEvidence = retained };
            var lineage = new ProjectionLineageDto(instruction.EventId, instruction.EventId, BondCouponRecognitionCalculator.ModelVersion,
                BondCouponRecognitionCalculator.ModelVersion, BondCouponRecognitionCalculator.ModelVersion, "ObservedCouponRecognition",
                Through, At, "BondCouponRecognition", source.SourceReference, economic)
            { BookPositionId = lot.BookPositionId, RetainedEvidence = retained };
            var evidence = retained.Select(item => new AccountingPostingEvidenceReferenceDto(item.EvidenceId, item.EvidenceUri,
                AccountingPostingEvidenceKindDto.Source, item.SourceSystem, item.RetainedAtUtc, item.RetainedBy,
                item.SubjectId, item.ContentHashSha256, SourceReference: item.SourceReference, Reviewer: item.ReviewedBy,
                ReviewedAtUtc: item.ReviewedAtUtc, EffectiveDate: item.EffectiveDate, EvidenceVersion: item.EvidenceVersion,
                ReviewStatus: item.ReviewStatus, SubjectType: item.SubjectType)).ToArray();
            var reversal = instruction.ReversalTarget;
            var context = _reference.Position.BookContext with { PeriodId = Period.PeriodId };
            var posting = new AccountingPostingCommandDto(Guid.NewGuid(), BookId, Period.PeriodId, Through, At,
                "coupon:" + instruction.Request.RunId, reversal is null ? AccountingPostingIntentDto.Originating : AccountingPostingIntentDto.Adjustment,
                instruction.EventId, SourceJournalEntryId: reversal?.JournalEntryId, ExpectedVersion: Period.Version,
                SourceEventType: economic.EventType, ApprovalState: AccountingPostingApprovalStateDto.Approved,
                ApprovalId: "independent-coupon-approval", Evidence: evidence, LedgerBookId: BookId)
            {
                Actor = "coupon-operator",
                BookContext = context,
                BookPositionId = lot.BookPositionId,
                EconomicEvent = economic,
                ProjectionLineage = lineage,
                RulePackReference = new("coupon-policy", "1"),
                BondCouponRecognition = instruction
            };
            var lines = instruction.Lines.Select(line =>
            {
                var account = Accounts.Single(a => a.Path == line.AccountId);
                var dimensions = new LedgerLineDimensionSet(BookId: Scope.BookId, AccountId: Scope.AccountId,
                    InstrumentId: lot.SecurityId, TaxLotId: lot.LotId)
                { PositionId = lot.BookPositionId };
                return new LedgerEntry(Guid.NewGuid(), id, At, new(account.LedgerName, Enum.Parse<LedgerAccountType>(account.AccountType),
                    FinancialAccountId: Scope.AccountId), line.Debit, line.Credit, "Coupon accrual/clearing", dimensions,
                    new("USD", "USD", line.Debit, line.Credit, 1m));
            }).ToArray();
            return new(new(id, At, "Coupon accrual/clearing", lines, new(SecurityId: lot.SecurityId, EffectiveDate: Through,
                IdempotencyKey: posting.IdempotencyKey, Tags: SecurityMasterLineageTags(lot.SecurityId))), BookId, Period.PeriodId,
                SourceEventId: instruction.EventId, SourceJournalEntryId: reversal?.JournalEntryId,
                AccountingBasis: AccountingBasisKindDto.Gaap, AccountingPolicyId: "coupon-policy", AccountingPolicyVersion: "1",
                PostingKind: reversal is null ? LedgerPostingKindDto.Originating : LedgerPostingKindDto.Adjustment,
                LedgerBookId: BookId, PostingCommand: posting);
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }
}
