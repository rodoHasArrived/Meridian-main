using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Tests.AssetOperations;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidateServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorporateActionLines_EqualBasisSuccessorsRetainReviewedIdentityRegardlessOfRuleOrder(bool reversed)
    {
        var instruction = OpenLotCorporateActionTests.Instruction(CorporateActionAccountingTypeDto.AdvanceRefunding, 50m);
        var fixture = CorporateActionRuleLines(instruction, aggregate: false);
        var original = reversed
            ? new JournalEntry(fixture.Entry.JournalEntryId, fixture.Entry.Timestamp, fixture.Entry.Description,
                fixture.Entry.Lines.Reverse().ToArray(), fixture.Entry.Metadata)
            : fixture.Entry;
        var generated = reversed ? fixture.Generated.Reverse().ToArray() : fixture.Generated;

        var result = AccountingPostingCandidateService.WithCorporateActionLineage(original, fixture.Mutation, generated);
        var repeated = AccountingPostingCandidateService.WithCorporateActionLineage(original, fixture.Mutation, generated);

        result.Lines.Should().HaveCount(3);
        result.Lines.Select(line => line.EntryId).Should().Equal(repeated.Lines.Select(line => line.EntryId));
        foreach (var successor in instruction.Successors)
        {
            var debit = result.Lines.Single(line => line.Dimensions!.TaxLotId == successor.TaxLotRecordId.ToString("D"));
            debit.Debit.Should().Be(instruction.ExpectedLot.OpenFunctionalCostBasis / 2m);
            debit.Dimensions!.InstrumentId.Should().Be(successor.Security.SecurityId);
            debit.Dimensions.PositionId.Should().Be(successor.BookPositionId);
            debit.Currency!.TransactionDebit.Should().Be(instruction.ExpectedLot.OpenTransactionCostBasis / 2m);
        }
        result.Lines.Single(line => line.Credit > 0m).Dimensions!.TaxLotId.Should().Be(instruction.ExpectedLot.TaxLotRecordId.ToString("D"));
    }

    [Fact]
    public void CorporateActionLines_AggregatedRuleEffectExpandsEveryPredecessorAndSuccessorExactlyOnce()
    {
        var instruction = OpenLotCorporateActionTests.AddPredecessor(
            OpenLotCorporateActionTests.Instruction(CorporateActionAccountingTypeDto.AdvanceRefunding, 50m));
        var fixture = CorporateActionRuleLines(instruction, aggregate: true);

        var result = AccountingPostingCandidateService.WithCorporateActionLineage(fixture.Entry, fixture.Mutation, fixture.Generated);

        fixture.Generated.Should().HaveCount(2);
        result.Lines.Should().HaveCount(6);
        result.Lines.Select(line => line.Dimensions!.TaxLotId).Distinct().Should().HaveCount(6);
        result.Lines.Where(line => line.Credit > 0m).Should().HaveCount(2);
        result.Lines.Sum(line => line.Debit).Should().Be(210000m);
        result.Lines.Sum(line => line.Debit - line.Credit).Should().Be(0m);
        result.Lines.Should().OnlyContain(line => line.Dimensions!.EntityId == "same-reviewed-entity");
        foreach (var group in OpenLotCorporateAction.Groups(instruction))
        {
            result.Lines.Should().ContainSingle(line => line.Dimensions!.TaxLotId == group.ExpectedLot.TaxLotRecordId.ToString("D") && line.Credit == group.ExpectedLot.OpenFunctionalCostBasis);
            foreach (var successor in group.Successors)
                result.Lines.Should().ContainSingle(line => line.Dimensions!.TaxLotId == successor.TaxLotRecordId.ToString("D") && line.Debit > 0m);
        }
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("account-path")]
    [InlineData("dimension")]
    [InlineData("currency")]
    public void CorporateActionLines_RejectsRuleEffectThatDoesNotReconcileReviewedAccountsAndScope(string changed)
    {
        var instruction = OpenLotCorporateActionTests.Instruction(CorporateActionAccountingTypeDto.AdvanceRefunding, 50m);
        var fixture = CorporateActionRuleLines(instruction, aggregate: true);
        var generated = fixture.Generated.ToArray();
        var lines = fixture.Entry.Lines.ToArray();
        var debit = lines[1];
        if (changed == "account-path")
            generated[1] = generated[1] with { AccountPath = "assets/unreviewed" };
        if (changed == "currency")
            generated[1] = generated[1] with { Currency = "EUR" };
        if (changed is "dimension" or "amount")
        {
            lines[1] = new LedgerEntry(debit.EntryId, debit.JournalEntryId, debit.Timestamp, debit.Account,
                changed == "amount" ? debit.Debit + 1m : debit.Debit, debit.Credit, debit.Description,
                changed == "dimension" ? debit.Dimensions! with { EntityId = "different-entity" } : debit.Dimensions);
            if (changed == "amount")
            {
                var credit = lines[0];
                lines[0] = new LedgerEntry(credit.EntryId, credit.JournalEntryId, credit.Timestamp, credit.Account,
                    credit.Debit, credit.Credit + 1m, credit.Description, credit.Dimensions);
            }
        }
        var entry = new JournalEntry(fixture.Entry.JournalEntryId, fixture.Entry.Timestamp, fixture.Entry.Description, lines, fixture.Entry.Metadata);

        var draft = () => AccountingPostingCandidateService.WithCorporateActionLineage(entry, fixture.Mutation, generated);

        draft.Should().Throw<InvalidOperationException>();
    }

    private static (JournalEntry Entry, AssetLotMutationInstructionDto Mutation, GeneratedPostingLineDto[] Generated)
        CorporateActionRuleLines(OpenLotCorporateActionInstructionDto instruction, bool aggregate)
    {
        const string path = "assets/investments";
        instruction = instruction with
        {
            Successors = instruction.Successors.Select(successor => successor with { PostingAccountPath = path }).ToArray(),
            AdditionalPredecessors = instruction.AdditionalPredecessors?.Select(group => group with
            { Successors = group.Successors.Select(successor => successor with { PostingAccountPath = path }).ToArray() }).ToArray()
        };
        var projections = OpenLotCorporateAction.Project(instruction);
        var source = instruction.ExpectedLot;
        var journalId = Guid.NewGuid();
        var at = new DateTimeOffset(instruction.EffectiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dimensions = new LedgerLineDimensionSet(EntityId: "same-reviewed-entity", InstrumentId: source.SecurityId) { PositionId = source.BookPositionId };
        var account = new LedgerAccount(instruction.SourceAssetAccountId, LedgerAccountType.Asset);
        var credit = OpenLotCorporateAction.Groups(instruction).Sum(group => group.ExpectedLot.OpenFunctionalCostBasis);
        var debits = aggregate ? new[] { credit } : projections.Select(target => target.OpenFunctionalCostBasis).ToArray();
        var lines = new[] { new LedgerEntry(Guid.NewGuid(), journalId, at, account, 0m, credit, "Reviewed corporate action", dimensions) }
            .Concat(debits.Select(amount => new LedgerEntry(Guid.NewGuid(), journalId, at, account, amount, 0m, "Reviewed corporate action", dimensions))).ToArray();
        var generated = lines.Select((line, index) => new GeneratedPostingLineDto("rule-line-" + index, path,
            line.Debit > 0m ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit,
            "reviewed-basis", line.Debit + line.Credit, "USD")).ToArray();
        return (new JournalEntry(journalId, at, "Reviewed corporate action", lines, new JournalEntryMetadata(EffectiveDate: instruction.EffectiveDate)),
            new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.CorporateAction, AssetAccountId: path, CorporateAction: instruction), generated);
    }
}
