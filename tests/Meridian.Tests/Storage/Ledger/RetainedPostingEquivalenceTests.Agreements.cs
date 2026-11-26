using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage.Ledger;

public sealed partial class RetainedPostingEquivalenceTests
{
    [Fact]
    public void AgreementDimension_RequiresTheExactRetainedApprovedVersion()
    {
        var dimensions = new LedgerLineDimensionSet(EntityId: "entity-a", CounterpartyId: "entity-b")
        {
            IntercompanyAgreementId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"),
            IntercompanyAgreementVersion = 1
        };
        RetainedPostingEquivalence.DimensionsMatch(dimensions, dimensions with { }).Should().BeTrue();
        RetainedPostingEquivalence.DimensionsMatch(dimensions, dimensions with { IntercompanyAgreementVersion = 2 }).Should().BeFalse();
        RetainedPostingEquivalence.DimensionsMatch(dimensions, dimensions with { IntercompanyAgreementId = Guid.NewGuid() }).Should().BeFalse();
        RetainedPostingEquivalence.DimensionsMatch(dimensions, dimensions with { IntercompanyAgreementId = null, IntercompanyAgreementVersion = null }).Should().BeFalse();
    }
}
