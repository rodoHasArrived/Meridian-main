using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AdoptedIncome_AfterScheduledPaydown_PostsNewTargetsThenClosesWithoutLegacyElection()
    {
        await using var fixture = await AmortFixture.CreateAsync(false, AccountingBasisKindDto.Gaap,
            factorPool: true, incomeIndexRatio: 1.2m);
        var paydown = await fixture.Store.AppendAssetPostingAsync(
            await ScheduledIncomeCommand(fixture, 0.90m, FirstPaydown));
        var afterPaydown = paydown.MutatedLots.Single();
        var template = (await ScheduledIncomeCommand(fixture, 0m, SecondPaydown))
            .FactorPaydown!.ScheduledPrincipal!.IncomeInstructions.Single();

        async Task<AtomicTaxLotJournalResult> PostIndexTarget(DateOnly date, decimal ratio)
        {
            var current = (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lot.TaxLotRecordId])).Single();
            var knownAt = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
            var retained = template.BondIncome!;
            var income = retained with
            {
                ThroughDate = date,
                KnownAt = knownAt,
                CurrentIndexRatio = ratio,
                CurrentProjection = retained.CurrentProjection with
                {
                    Context = retained.CurrentProjection.Context with { AsOf = knownAt, SettlementDate = date, KnownAt = knownAt },
                    CashFlows = retained.CurrentProjection.CashFlows.Select(flow => flow.PaymentDate == SecondPaydown
                        ? flow with { IndexRatio = ratio, Principal = flow.Principal == 0m ? 0m : 900_000m * ratio }
                        : flow).ToArray()
                }
            };
            income = income with { BookedTarget = BondIncomeCalculator.Project(current.ToOpenLot(), income).BookedTarget };
            var inputs = template with { ExpectedLot = current.ToOpenLot(), AsOfDate = date, BondIncome = income };
            inputs.AmortizeRemainingFace.Should().BeFalse("the adopted policy prohibits the legacy election");
            var preview = new BondIncomePreviewDto(fixture.BookId, current.TaxLotRecordId, "retained", inputs,
                BondIncomeCalculator.Project(current.ToOpenLot(), income),
                new(AssetLotMutationIntentDto.Amortize, Amortization: inputs));
            var authority = Substitute.For<IBondIncomeService>();
            authority.PreviewAsync(Arg.Any<BondIncomePreviewRequestDto>(), Arg.Any<CancellationToken>()).Returns(preview);
            fixture.IncomeAuthority = authority;
            var command = fixture.Command(adoptedInstruction: inputs);
            var result = await fixture.Store.AppendAssetPostingAsync(command);
            (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
            return result;
        }

        // Independent indexation: 900,000 remaining underlying face × (1.201 - 1.200) = 900.
        var indexed = await PostIndexTarget(FirstPaydown.AddDays(1), 1.201m);
        indexed.MutatedLots.Single().ToOpenLot().OpenTransactionCostBasis.Should().Be(1_080_900m);
        indexed.MutatedLots.Single().CurrentFactor.Should().Be(0.90m);
        indexed.MutatedLots.Single().OpenQuantity.Should().Be(afterPaydown.OpenQuantity);
        indexed.Journal.Entry.Lines.Sum(line => line.Debit).Should().Be(900m);
        indexed.Journal.Entry.Lines.Sum(line => line.Credit).Should().Be(900m);
        var normalized = await PostIndexTarget(SecondPaydown.AddDays(-1), 1.2m);
        normalized.MutatedLots.Single().ToOpenLot().OpenTransactionCostBasis.Should().Be(1_080_000m);
        var closed = await fixture.Store.AppendAssetPostingAsync(await ScheduledIncomeCommand(fixture, 0m, SecondPaydown));
        closed.MutatedLots.Single().OpenQuantity.Should().Be(0m);
        closed.MutatedLots.Single().ToOpenLot().OpenTransactionCostBasis.Should().Be(0m);
        closed.Journal.Entry.Lines.Should().NotContain(line => line.Account.AccountType == LedgerAccountType.Revenue
            || line.Account.AccountType == LedgerAccountType.Expense);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AdoptedIncome_PaydownHistory_RequiresExactScheduledReceiptAndRejectsLegacyTreatment()
    {
        await using var fixture = await AmortFixture.CreateAsync(false, AccountingBasisKindDto.Gaap,
            factorPool: true, incomeIndexRatio: 1.2m);
        var receipt = await fixture.Store.AppendAssetPostingAsync(await ScheduledIncomeCommand(fixture, 0.90m, FirstPaydown));
        var current = receipt.MutatedLots.Single();
        foreach (var invalid in new AtomicTaxLotJournalResult?[]
        {
            null,
            receipt with { MutationBatchId = Guid.NewGuid() },
            receipt with { Mutations = [] },
            receipt with { Mutations = [receipt.Mutations.Single() with { ResultVersion = current.Version + 1 }] },
            receipt with { FactorPaydown = receipt.FactorPaydown! with { ScheduledPrincipal = null } }
        })
        {
            var validate = () => PostgresLedgerJournalStore.ValidateAmortizationBasisHistoryAsync(current,
                FirstPaydown.AddDays(1), _ => Task.FromResult(invalid), adoptedBondIncome: true);
            await validate.Should().ThrowAsync<LedgerValidationException>();
        }
        await PostgresLedgerJournalStore.ValidateAmortizationBasisHistoryAsync(current,
            FirstPaydown.AddDays(1), _ => Task.FromResult<AtomicTaxLotJournalResult?>(receipt), adoptedBondIncome: true);
        var legacy = () => PostgresLedgerJournalStore.ValidateAmortizationBasisHistoryAsync(current,
            FirstPaydown.AddDays(1), _ => Task.FromResult<AtomicTaxLotJournalResult?>(receipt));
        await legacy.Should().ThrowAsync<LedgerValidationException>().WithMessage("*another basis treatment*");
    }
}
