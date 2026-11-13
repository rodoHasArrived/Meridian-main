using Meridian.Contracts.Ledger;
using Xunit;

namespace Meridian.Tests.Contracts.Ledger;

public sealed class LedgerAccountingArrangementSignatureTests
{
    [Fact]
    public void LegacySignatureRemainsExactWhenOperationalReferencesAreAbsent()
        => Assert.Equal("retained|intercompanyAgreementVersion=7",
            LedgerAccountingArrangementDimensions.AppendSignature("retained|intercompanyAgreementVersion=7", new()));

    [Theory]
    [InlineData("|")]
    [InlineData("\u001e")]
    public void EmbeddedFieldSeparatorsCannotMergeDifferentAccountingIdentities(string separator)
    {
        var embedded = new LedgerDimensionSetDto { ChartAccountNodeId = "node" + separator + "accountBindingRevisionId=revision" };
        var separate = new LedgerDimensionSetDto { ChartAccountNodeId = "node", AccountBindingRevisionId = "revision" };
        Assert.NotEqual(LedgerAccountingArrangementDimensions.AppendSignature("retained", embedded, separator),
            LedgerAccountingArrangementDimensions.AppendSignature("retained", separate, separator));
    }
}
