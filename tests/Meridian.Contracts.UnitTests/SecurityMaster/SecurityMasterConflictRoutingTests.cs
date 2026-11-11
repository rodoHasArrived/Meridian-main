using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Contracts.UnitTests.SecurityMaster;

/// <summary>
/// Conflict routing by declared facts: declared kinds, whole path segments, and the term keys each
/// asset class's own schema declares. No route depends on a substring of the path.
/// </summary>
public sealed class SecurityMasterConflictRoutingTests
{
    // Every path SecurityMasterConflictDetection writes today, with the route it takes.
    [Theory]
    [InlineData("Identifiers.Isin", SecurityMasterConflictKinds.IdentifierAmbiguity, true, false)]
    [InlineData("Identifiers.Ric", SecurityMasterConflictKinds.IdentifierAmbiguity, true, false)]
    [InlineData("Identifiers.Primary", SecurityMasterConflictKinds.IdentifierAmbiguity, true, false)]
    [InlineData("EconomicTerms.maturityDate", SecurityMasterConflictKinds.EconomicTermMismatch, false, true)]
    [InlineData("EconomicTerms.issueDate", SecurityMasterConflictKinds.EconomicTermMismatch, false, true)]
    [InlineData("EconomicTerms.couponRate", SecurityMasterConflictKinds.EconomicTermMismatch, false, true)]
    [InlineData("EconomicTerms.principalFace", SecurityMasterConflictKinds.EconomicTermMismatch, false, true)]
    [InlineData("EconomicTerms.paymentFrequency", SecurityMasterConflictKinds.EconomicTermMismatch, false, true)]
    [InlineData("EconomicTerms.dayCountConvention", SecurityMasterConflictKinds.EconomicTermMismatch, false, true)]
    [InlineData("EconomicTerms.principalSchedule", SecurityMasterConflictKinds.EconomicTermMismatch, false, true)]
    [InlineData("CommonTerms.currency", SecurityMasterConflictKinds.CommonTermMismatch, false, true)]
    [InlineData("CommonTerms.countryOfRisk", SecurityMasterConflictKinds.CommonTermMismatch, false, true)]
    [InlineData("ProfileFields.tranche", SecurityMasterConflictKinds.EconomicTermMismatch, false, true)]
    [InlineData("ProfileFields.collateralType", SecurityMasterConflictKinds.EconomicTermMismatch, false, true)]
    public void EmittedFieldPaths_RouteToIdentityOrTerms(string fieldPath, string kind, bool identity, bool terms)
    {
        var conflict = Conflict(fieldPath, kind);

        SecurityMasterConflictRouting.IsIdentityConflict(conflict).Should().Be(identity);
        SecurityMasterConflictRouting.IsTermsConflict(conflict, "Bond").Should().Be(terms);
    }

    // Paths the word lists routed only because of a substring. With no declared kind to lean on,
    // each is routed by its whole segments.
    [Theory]
    [InlineData("ProfileFields.providerName", false)]
    [InlineData("ProfileFields.transferRestrictions", false)]
    [InlineData("ProfileFields.aliasPolicy", false)]
    [InlineData("Notes.isinLookalike", false)]
    [InlineData("ProviderMappings.alpaca", true)]
    [InlineData("Aliases.AAPL", true)]
    [InlineData("Identifiers.Cusip", true)]
    [InlineData("PrimaryIdentifier", true)]
    public void IdentityRoute_ComparesWholeSegments(string fieldPath, bool expected)
        => SecurityMasterConflictRouting.IsIdentityConflict(Conflict(fieldPath, "Unclassified")).Should().Be(expected);

    // A bare field name is a terms conflict only for a class whose schema declares that key or alias.
    [Theory]
    [InlineData("maturity", "Bond", true)]
    [InlineData("maturityDate", "Swap", true)]
    [InlineData("maturityDate", "Bond", false)]
    [InlineData("maturity", "Equity", false)]
    [InlineData("strike", "Option", true)]
    [InlineData("strike", "Bond", false)]
    [InlineData("couponRate", "CertificateOfDeposit", true)]
    [InlineData("couponRate", "FxSpot", false)]
    [InlineData("tranche", "StructuredCredit", true)]
    [InlineData("tranche", "MBS", true)]
    [InlineData("Notes.maturityNote", "Bond", false)]
    [InlineData("Remarks.couponish", "Bond", false)]
    public void TermsRoute_UsesTheKeysTheRecordsOwnClassDeclares(string fieldPath, string assetClass, bool expected)
        => SecurityMasterConflictRouting.IsTermsConflict(Conflict(fieldPath, "Unclassified"), assetClass).Should().Be(expected);

    [Theory]
    [InlineData("EconomicDefinition.MaturityDate", "EconomicTermsMismatch")]
    [InlineData("AssetSpecificTerms.anything", "Unclassified")]
    [InlineData("Classification.AssetClass", "Unclassified")]
    [InlineData("Currency", "Unclassified")]
    [InlineData("Free.Text", SecurityMasterConflictKinds.EconomicTermMismatch)]
    public void TermsRoute_TakesContainersClassificationNamesAndDeclaredKinds_ForAnyClass(string fieldPath, string kind)
        => SecurityMasterConflictRouting.IsTermsConflict(Conflict(fieldPath, kind), assetClass: null).Should().BeTrue();

    [Fact]
    public void TermRoutingKeys_AreEachClassesDeclaredKeysAndAliases_AndNothingElse()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            var expected = SecurityAssetTermsSchema.Fields(assetClass)
                .SelectMany(static field => field.Aliases.Prepend(field.Key))
                .Select(static key => key.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);

            SecurityMasterConflictRouting.TermRoutingKeys(assetClass).Should().BeEquivalentTo(expected, assetClass);
        }

        SecurityMasterConflictRouting.TermRoutingKeys("NotAClass").Should().BeEmpty();
        SecurityMasterConflictRouting.TermRoutingKeys(null).Should().BeEmpty();
        SecurityMasterConflictRouting.TermRoutingKeys("CorporateBond").Should()
            .BeEquivalentTo(SecurityMasterConflictRouting.TermRoutingKeys("Bond"), "an alias is the class it names");
    }

    [Theory]
    [InlineData("Identifiers.Ric", "Ric", true)]
    [InlineData("Identifiers.ric", "RIC", true)]
    [InlineData("ProfileFields.transferRestrictions", "Ric", false)]
    [InlineData("Identifiers.Ric", null, false)]
    [InlineData(null, "Ric", false)]
    public void HasSegment_MatchesAWholeSegmentOnly(string? fieldPath, string? name, bool expected)
        => SecurityMasterConflictRouting.HasSegment(fieldPath, name).Should().Be(expected);

    [Theory]
    [InlineData("ProviderMappings.AAPL.O", "AAPL.O", true)]
    [InlineData("AAPL.O", "aapl.o", true)]
    [InlineData("ProviderMappings.XAAPL.O", "AAPL.O", false)]
    [InlineData("ProviderMappings.AAPL.OQ", "AAPL.O", false)]
    [InlineData("Identifiers.Ric", "", false)]
    public void EndsWithValue_MatchesTheWholeTrailingValue(string fieldPath, string value, bool expected)
        => SecurityMasterConflictRouting.EndsWithValue(fieldPath, value).Should().Be(expected);

    private static SecurityMasterConflict Conflict(string fieldPath, string kind)
        => new(Guid.NewGuid(), Guid.NewGuid(), kind, fieldPath, "provider-a", "a", "provider-b", "b", DateTimeOffset.UnixEpoch, "Open");
}
