using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.Contracts.UnitTests.Contracts;

public sealed class IntercompanyAgreementRatePrecisionTests
{
    [Fact]
    public void RateRoundTripsAsExactDecimalTextThroughGeneratedContracts()
    {
        const string exact = "0.1234567890123456789012345678";
        var terms = JsonSerializer.Deserialize("{\"fixedAnnualRate\":\"" + exact + "\"}",
            FundStructureContractsJsonContext.Default.IntercompanyAgreementTermsDto)!;
        var json = JsonSerializer.Serialize(terms, FundStructureContractsJsonContext.Default.IntercompanyAgreementTermsDto);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(exact, document.RootElement.GetProperty("fixedAnnualRate").GetString());
        Assert.Equal(0.1234567890123456789012345678m, terms.FixedAnnualRate);
    }

    [Fact]
    public void NumericRateRequestsRemainCompatible()
    {
        var terms = JsonSerializer.Deserialize("{\"fixedAnnualRate\":0.05}",
            FundStructureContractsJsonContext.Default.IntercompanyAgreementTermsDto)!;
        Assert.Equal(0.05m, terms.FixedAnnualRate);
    }
}
