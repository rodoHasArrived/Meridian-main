using System.Text.Json;
using Meridian.FinancialOperations.Consolidation;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed partial class ConsolidationServiceTests
{
    [Fact]
    public async Task RetainedEvidenceReadsLegacyAndWebCasingWithoutChangingCanonicalSourceOperands()
    {
        var fixture = new Fixture();
        var expected = (await fixture.Calculate()).Evidence;
        foreach (var json in new[] { JsonSerializer.Serialize(expected), JsonSerializer.Serialize(expected, new JsonSerializerOptions(JsonSerializerDefaults.Web)) })
        {
            var actual = Assert.IsType<Meridian.Contracts.Ledger.ConsolidationEvidenceDto>(ConsolidationService.ReadEvidence(json));
            Assert.Equal(expected.Request, actual.Request);
            Assert.Equal(expected.RuleVersion, actual.RuleVersion);
            Assert.Equal(ConsolidationService.Hash(expected), ConsolidationService.Hash(actual));
        }
    }
}
