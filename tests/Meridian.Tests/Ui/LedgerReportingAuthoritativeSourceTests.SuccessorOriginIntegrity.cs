using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed partial class LedgerReportingAuthoritativeSourceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureAsync_ExplicitSourceIdentityRequiresRetainedSuccessorOrigin(bool advanceRefunding)
    {
        var fixture = CreateFixture(successors: true,
            accountingBasis: advanceRefunding ? AccountingBasisKindDto.Statutory : AccountingBasisKindDto.Gaap);
        var batch = SuccessorBatch(fixture, advanceRefunding);
        batch.CorporateAction!.Projection.SourceCorporateActionId.Should().NotBeNull();
        var mutations = batch.Mutations.Select(item => item.LotBefore is not null ? item : item with
        {
            LotAfter = item.LotAfter with
            { Acquisition = item.LotAfter.Acquisition! with { CorporateActionLineage = null } }
        }).ToArray();
        fixture.JournalStore.Records.Add(batch.Journal);
        fixture.JournalStore.Successors.Add(batch with
        { Mutations = mutations, MutatedLots = mutations.Select(item => item.LotAfter).ToArray() });

        var capture = () => fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access).AsTask();

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>();
    }
}
