using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AdoptedIndexedPrincipal_UsesActualAtomicWriter_NoDisposalResult_AndReplays()
    {
        await using var fixture = await AmortFixture.CreateAsync(false, AccountingBasisKindDto.Gaap,
            factorPool: true, incomeIndexRatio: 1.2m);
        var command = await ScheduledIncomeCommand(fixture, 0.90m, FirstPaydown);
        var result = await fixture.Store.AppendAssetPostingAsync(command);
        var after = result.MutatedLots.Single();
        command.FactorPaydown!.PrincipalPaydown.Should().Be(60_000m);
        after.ToOpenLot().OpenTransactionCostBasis.Should().Be(1_080_000m);
        after.CurrentFactor.Should().Be(0.90m);
        result.Journal.Entry.Lines.Should().HaveCount(2);
        result.Journal.Entry.Lines.Should().NotContain(line => line.Account.AccountType == LedgerAccountType.Revenue
            || line.Account.AccountType == LedgerAccountType.Expense);
        OpenLotFactorPaydown.RealizedResult(command.FactorPaydown).Should().Be(0m);
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
        var full = await ScheduledIncomeCommand(fixture, 0m, SecondPaydown);
        var closed = await fixture.Store.AppendAssetPostingAsync(full);
        closed.MutatedLots.Single().ToOpenLot().OpenTransactionCostBasis.Should().Be(0m);
        closed.MutatedLots.Single().OpenQuantity.Should().Be(0m);
        closed.Journal.Entry.Lines.Should().HaveCount(2);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AdoptedPrincipal_ChangedAuthorityRefusesAtomicCommit()
    {
        await using var fixture = await AmortFixture.CreateAsync(false, AccountingBasisKindDto.Gaap,
            factorPool: true, incomeIndexRatio: 1.2m);
        var command = await ScheduledIncomeCommand(fixture, 0.90m, FirstPaydown);
        fixture.IncomeAuthority = null;
        await fixture.Store.Invoking(store => store.AppendAssetPostingAsync(command)).Should()
            .ThrowAsync<LedgerValidationException>().WithMessage("*authoritative bond-income recomputation*");
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lot.TaxLotRecordId])).Single().Version.Should().Be(fixture.Lot.Version);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AdoptedPrincipal_CashReceiptComponentCannotBeAllocatedTwiceAcrossEvents()
    {
        await using var fixture = await AmortFixture.CreateAsync(false, AccountingBasisKindDto.Gaap,
            factorPool: true, incomeIndexRatio: 1.2m);
        var first = await ScheduledIncomeCommand(fixture, 0.90m, FirstPaydown);
        await fixture.Store.AppendAssetPostingAsync(first);
        var receiptReference = first.RetainedEvidence.Single(e => e.SubjectType == "BondPrincipalCashReceipt").SourceReference;
        var duplicate = await ScheduledIncomeCommand(fixture, 0m, SecondPaydown, receiptReference);
        await fixture.Store.Invoking(s => s.AppendAssetPostingAsync(duplicate)).Should()
            .ThrowAsync<LedgerValidationException>().WithMessage("*already allocated*");
        (await fixture.Store.GetAtomicTaxLotPostingAsync(duplicate.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lot.TaxLotRecordId])).Single().CurrentFactor.Should().Be(0.90m);
    }

    private static async Task<AtomicTaxLotJournalCommand> ScheduledIncomeCommand(AmortFixture fixture,
        decimal newFactor, DateOnly date, string? receiptReference = null)
    {
        var legacy = await FactorPaydownCommandAsync(fixture, newFactor, date);
        var original = legacy.FactorPaydown!;
        var lot = original.Lots.Single().ExpectedLot;
        var cutoff = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var accountId = fixture.IncomeAccountId;
        var flows = new BondWorkflowCashFlow[]
        {
            new(FirstPaydown, AmortAcquired, FirstPaydown, 0m, 0m, 950_000m, 1.2m, false, []) { PeriodUnits = 1m, AccrualFraction = 1m },
            new(FirstPaydown, FirstPaydown, FirstPaydown, 0m, 60_000m, 900_000m, 1.2m, false, []),
            new(SecondPaydown, FirstPaydown, SecondPaydown, 0m, 0m, 900_000m, 1.2m, false, []) { PeriodUnits = 1m, AccrualFraction = 1m },
            new(SecondPaydown, SecondPaydown, SecondPaydown, 0m, 1_080_000m, 0m, 1.2m, false, [])
        };
        var projection = new BondWorkflowResult(lot.SecurityId, "USD", "bond-workflow-v1",
            new(cutoff, lot.AcquiredDate, cutoff), 1_000_000m, 950_000m, 0m, null, null, 0m, null,
            null, null, null, null, null, null, flows,
            [new("SecurityTerms", lot.SecurityId.ToString("D"), fixture.Security.Version, new string('a', 64))], [], []);
        var income = new BondIncomeInstructionDto(fixture.BookId, "amort-policy", "1",
            new(BondIncomeCalculator.ModelVersion, BondIncomeFloaterPolicy.AcquisitionIndex, true, true, true),
            lot.AcquiredDate, date, cutoff, projection, projection, projection, [], [], [], 0m, 0m, 1.2m, 1.2m,
            950_000m, lot.Acquisition.BondIncome!.Evidence, accountId);
        income = income with { BookedTarget = BondIncomeCalculator.Project(lot, income).BookedTarget };
        var inputs = new OpenLotAmortizationInstructionDto(lot, fixture.Security, AmortSecurityEvidence(fixture.Security),
            original.Scope.ExpectedBookPositionVersion, date, BondIncomeCalculator.ModelVersion)
        { BondIncome = income };
        var receipt = BuildEvidence("principal-cash-" + date.ToString("yyyyMMdd"), 'e') with
        { EffectiveDate = date, SubjectType = "BondPrincipalCashReceipt", SubjectId = lot.TaxLotRecordId.ToString("D") };
        if (receiptReference is not null)
            receipt = receipt with { SourceReference = receiptReference };
        var evidence = legacy.RetainedEvidence.Append(receipt).ToArray();
        var eventRef = original.EconomicEvent with { RetainedEvidence = original.EconomicEvent.RetainedEvidence.Append(receipt).ToArray() };
        var adopted = original with
        {
            EconomicEvent = eventRef,
            Lineage = original.Lineage with { TriggerEvent = eventRef, ModelVersion = BondIncomeScheduledPrincipal.ModelVersion },
            PrincipalPaydown = newFactor == 0m ? 1_080_000m : 60_000m,
            ScheduledPrincipal = new(BondIncomeScheduledPrincipal.ModelVersion, [inputs])
        };
        adopted = adopted with { Lots = BondIncomeScheduledPrincipal.Allocate(adopted, adopted.ScheduledPrincipal) };
        var oldEntry = legacy.Journal.Entry;
        var cash = oldEntry.Lines.Single(line => line.Account.AccountType == LedgerAccountType.Asset && line.Debit > 0m);
        var currentLot = (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [lot.TaxLotRecordId])).Single();
        var carrying = oldEntry.Lines.Single(line => line.Account == currentLot.Account);
        var principal = adopted.PrincipalPaydown;
        var lines = new[]
        {
            new LedgerEntry(cash.EntryId, cash.JournalEntryId, cash.Timestamp, LedgerAccounts.CashAccount(accountId.ToString("D")), principal, 0m,
                cash.Description, cash.Dimensions, new("USD", "USD", principal, 0m, 1m)),
            new LedgerEntry(carrying.EntryId, carrying.JournalEntryId, carrying.Timestamp, carrying.Account, 0m, principal,
                carrying.Description, carrying.Dimensions, new("USD", "USD", 0m, principal, 1m))
        };
        var tags = new Dictionary<string, string>(oldEntry.Metadata.Tags!)
        { [OpenLotFactorPaydown.JournalFingerprintTag] = OpenLotFactorPaydown.Fingerprint(adopted) };
        var entry = new JournalEntry(oldEntry.JournalEntryId, oldEntry.Timestamp, oldEntry.Description, lines, oldEntry.Metadata with { Tags = tags });
        var posting = legacy.Journal.PostingCommand! with
        {
            LotFactorPaydown = adopted,
            EconomicEvent = eventRef,
            ProjectionLineage = adopted.Lineage,
            Evidence = evidence.Select(e => new AccountingPostingEvidenceReferenceDto(e.EvidenceId, e.EvidenceUri,
                AccountingPostingEvidenceKindDto.Source, e.SourceSystem, e.RetainedAtUtc, e.RetainedBy, e.SubjectId,
                e.ContentHashSha256, SourceReference: e.SourceReference, Reviewer: e.ReviewedBy, ReviewedAtUtc: e.ReviewedAtUtc,
                EffectiveDate: e.EffectiveDate, EvidenceVersion: e.EvidenceVersion, ReviewStatus: e.ReviewStatus, SubjectType: e.SubjectType)).ToArray()
        };
        var authority = Substitute.For<IBondIncomeService>();
        authority.PreviewAsync(Arg.Any<BondIncomePreviewRequestDto>(), Arg.Any<CancellationToken>()).Returns(new BondIncomePreviewDto(
            fixture.BookId, lot.TaxLotRecordId, "retained", inputs, BondIncomeCalculator.Project(lot, income),
            new(AssetLotMutationIntentDto.Amortize, AssetAccountId: "Assets:Bonds", Amortization: inputs)));
        fixture.IncomeAuthority = authority;
        return (legacy with
        {
            Journal = legacy.Journal with { Entry = entry, PostingCommand = posting },
            RetainedEvidence = evidence,
            FactorPaydown = adopted
        }).WithComputedFingerprint();
    }
}
