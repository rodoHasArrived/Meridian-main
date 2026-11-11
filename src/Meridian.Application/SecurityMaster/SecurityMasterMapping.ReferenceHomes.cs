using Meridian.FSharp.Domain;

namespace Meridian.Application.SecurityMaster;

internal static partial class SecurityMasterMapping
{
    private static InvestmentFundTerms ToInvestmentFundReferenceTerms(SchemaBoundTerms terms)
    {
        // Keep the old codec's scalar read order while grouping storage into typed modules.
        var fundType = ToOption(terms.OptionalString("fundType"));
        var fundFamily = ToOption(terms.OptionalString("fundFamily"));
        var navCurrency = ToOption(terms.OptionalString("navCurrency"));
        var distributionPolicy = ToDistributionPolicyOption(terms.OptionalString("distributionPolicy"));
        var isStableNav = ToOption(terms.OptionalBoolean("isStableNav"));
        var pricingSource = ToOption(terms.OptionalString("pricingSource"));
        return new InvestmentFundTerms(
            new FundShareTerms(fundType, navCurrency, distributionPolicy, isStableNav),
            new FundTerms(fundFamily, null, null, null),
            new ReferenceDataSourceTerms(pricingSource, null));
    }
}
