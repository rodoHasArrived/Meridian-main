using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using NSubstitute;

namespace Meridian.Tests.Strategies;

public sealed class SecurityMasterAccountingEventSourceRateUnitTests
{
    [Theory]
    [InlineData("CertificateOfDeposit", 5, "Percent")]
    [InlineData("StructuredCredit", 6, "Percent")]
    [InlineData("CertificateOfDeposit", 5, "percent")]
    [InlineData("StructuredCredit", 6, "percent")]
    public async Task ImportedEconomicOnlyDefinition_PreservesRateUnitAndAccountingDisposition(string assetClass, int version, string unit)
    {
        var id = Guid.NewGuid();
        var start = new DateTimeOffset(2041, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var summary = new StrategyRunSummary("unit-run", "synthetic", "Synthetic", default, default, default,
            start, start.AddMonths(3), null, null, "synthetic", null, null, null, null, 0, start.AddMonths(3));
        var reference = new WorkstationSecurityReference(id, "Synthetic security", assetClass, "USD", SecurityStatusDto.Active, "SYNTH");
        var portfolio = new PortfolioSummary("synthetic", "unit-run", start, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            [new PortfolioPositionSummary("SYNTH", 1000000, 100m, 0, 0, false, reference)]);
        var detail = new StrategyRunDetail(summary, new Dictionary<string, string>(), portfolio, null);
        var economic = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 2,
            termsFormat = "economic",
            maturity = new { issueDate = "2041-01-01", maturityDate = "2042-01-01" },
            coupon = new { couponType = "Fixed", couponRate = 0.5m, paymentFrequency = "Quarterly", dayCount = "30/360" },
            rateConvention = new { rateUnit = unit }
        });
        var retained = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            termsFormat = "flat",
            assetClassTermsVersion = version,
            rateUnit = unit
        });
        var definition = new SecurityEconomicDefinitionRecord(id, assetClass, null, assetClass, assetClass, null, null,
            SecurityStatusDto.Active, "Synthetic security", "USD", Json("{}"), Json("""{"accountingClassification":"HeldToMaturity"}"""), economic, Json("{}"),
            1, start, null, [], assetClass, retained);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetEconomicDefinitionByIdAsync(id, Arg.Any<CancellationToken>()).Returns(definition);
        var adapter = new SecurityMasterAccountingEventSourceAdapter(query);
        var withFlat = await adapter.BuildRequestAsync(detail, new ReconciliationRunRequest("unit-run"));
        query.GetEconomicDefinitionByIdAsync(id, Arg.Any<CancellationToken>()).Returns(definition with { LegacyAssetSpecificTerms = null });
        var economicOnly = await adapter.BuildRequestAsync(detail, new ReconciliationRunRequest("unit-run"));

        withFlat.Should().NotBeNull();
        economicOnly.Should().NotBeNull();
        economicOnly!.Securities.Single().FixedIncomeTerms.Should().Be(withFlat!.Securities.Single().FixedIncomeTerms);
        economicOnly.Securities.Single().FixedIncomeTerms!.CouponRateUnit.Should().Be(
            unit == "Percent" ? RateUnit.Percent : RateUnit.Unrecognized);
        var service = new SecurityMasterAccountingEventService();
        var expected = service.Generate(withFlat);
        var actual = service.Generate(economicOnly);
        actual.Issues.Select(issue => issue.Code).Should().Equal(expected.Issues.Select(issue => issue.Code));
        actual.AccrualCalculations.Should().BeEquivalentTo(expected.AccrualCalculations);
        actual.ExpectedEvents.Should().BeEquivalentTo(expected.ExpectedEvents);
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
