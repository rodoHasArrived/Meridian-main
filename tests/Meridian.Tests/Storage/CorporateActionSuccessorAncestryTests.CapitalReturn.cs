using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;

namespace Meridian.Tests.Storage;

public sealed partial class CorporateActionSuccessorAncestryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapitalReturn_ChecksEveryPredecessorBeforeAllowingAnotherReduction(bool legacy)
    {
        var chain = BuildChain(legacy);
        var instruction = CapitalReturnTestData.Build(chain.Current.ToOpenLot(),
            date: OpenLotSuccessorTestData.EffectiveDate.AddDays(3));
        var visited = new List<Guid>();
        await CorporateActionSuccessorAncestry.ValidateCapitalReturnAsync(chain.Current, instruction,
            id => Task.FromResult(chain.Receipts.GetValueOrDefault(id)),
            ancestor => { visited.Add(ancestor.TaxLotRecordId); return Task.CompletedTask; });
        visited.Should().HaveCount(3).And.OnlyHaveUniqueItems();
        visited.Last().Should().Be(chain.First.ExpectedLot.TaxLotRecordId);
        var duplicate = () => CorporateActionSuccessorAncestry.ValidateCapitalReturnAsync(chain.Current, instruction,
            id => Task.FromResult(chain.Receipts.GetValueOrDefault(id)),
            ancestor => ancestor.TaxLotRecordId == chain.First.ExpectedLot.TaxLotRecordId
                ? Task.FromException(new LedgerValidationException("Source return already reduced this ancestor."))
                : Task.CompletedTask);
        await duplicate.Should().ThrowAsync<LedgerValidationException>().WithMessage("*already reduced this ancestor*");
    }

    [Fact]
    public async Task CapitalReturn_MissingSuccessorBirthReceiptRefusesTheReduction()
    {
        var chain = BuildChain(legacy: false);
        var instruction = CapitalReturnTestData.Build(chain.Current.ToOpenLot(),
            date: OpenLotSuccessorTestData.EffectiveDate.AddDays(3));
        var validate = () => CorporateActionSuccessorAncestry.ValidateCapitalReturnAsync(chain.Current, instruction,
            _ => Task.FromResult<AtomicTaxLotJournalResult?>(null), _ => Task.CompletedTask);
        await validate.Should().ThrowAsync<LedgerValidationException>().WithMessage("*cannot be certified*");
    }
}
