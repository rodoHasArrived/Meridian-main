using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The classification drift report and the taxonomy comparison behind it. The scenario is the
/// 2026-11 taxonomy revision: Swaps written while the table filed them as listed derivatives, FX
/// spot written under the "Other" asset class with no family, and investment funds written with no
/// family.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterClassificationDriftReportServiceTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Swap", "Derivative", "OtcDerivative")]
    [InlineData("Cfd", "Derivative", "OtcDerivative")]
    [InlineData("FxSpot", "CashEquivalent", "ForeignExchange")]
    [InlineData("InvestmentFund", "Fund", "PooledFund")]
    [InlineData("Warrant", "Derivative", "ListedDerivative")]
    [InlineData("Option", "Derivative", "ListedDerivative")]
    public void Current_ReturnsTheRevisedTaxonomy(string typeName, string assetClass, string family)
    {
        SecurityClassificationTaxonomy.Current(typeName)
            .Should().Be(new SecurityClassificationTaxonomy.Labels(assetClass, family));
    }

    [Fact]
    public void Current_ReturnsNull_ForAnUnmodelledClass()
    {
        SecurityClassificationTaxonomy.Current("TokenizedCarbonCredit").Should().BeNull();
        SecurityClassificationTaxonomy.Current(null).Should().BeNull();
    }

    [Fact]
    public void MatchesCurrent_DecodesTheRetiredFSharpRendering()
    {
        SecurityClassificationTaxonomy.MatchesCurrent("Swap", "Derivative", "Some(OtcDerivative)").Should().BeTrue();
        SecurityClassificationTaxonomy.MatchesCurrent("Swap", "Derivative", "ListedDerivative").Should().BeFalse();
    }

    [Fact]
    public void IsTaxonomyRevision_IsTrue_WhenOnlyTheTaxonomyMovedTheLabels()
    {
        var before = Definition("Swap", "Derivative", "ListedDerivative");
        var after = Definition("Swap", "Derivative", "OtcDerivative");

        SecurityClassificationTaxonomy.IsTaxonomyRevision(before, after).Should().BeTrue();
    }

    [Fact]
    public void IsTaxonomyRevision_IsFalse_WhenTheAssetClassItselfChanged()
    {
        // A Bond re-modelled as StructuredCredit is an amendment of the record, not a taxonomy revision.
        var before = Definition("Bond", "FixedIncome", "CorporateDebt");
        var after = Definition("StructuredCredit", "FixedIncome", "SecuritizedCredit");

        SecurityClassificationTaxonomy.IsTaxonomyRevision(before, after).Should().BeFalse();
    }

    [Fact]
    public void IsTaxonomyRevision_IsFalse_WhenTheEarlierLabelsAlreadyMatched()
    {
        var before = Definition("Swap", "Derivative", "OtcDerivative");
        var after = Definition("Swap", "Derivative", "OtcDerivative");

        SecurityClassificationTaxonomy.IsTaxonomyRevision(before, after).Should().BeFalse();
    }

    [Fact]
    public async Task BuildReportAsync_GroupsDriftedSecurities_AndSkipsCurrentOnes()
    {
        var swapA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        var swapB = Guid.Parse("00000000-0000-0000-0000-00000000000b");
        var fx = Guid.NewGuid();
        var currentSwap = Guid.NewGuid();
        var equity = Guid.NewGuid();

        var (sut, store, events) = BuildSut();
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Projection(swapB, "Swap"),
            Projection(swapA, "Swap"),
            Projection(fx, "FxSpot"),
            Projection(currentSwap, "Swap"),
            Projection(equity, "Equity"),
        });
        events.LoadAsync(swapA, Arg.Any<CancellationToken>()).Returns(Stream(swapA, Definition("Swap", "Derivative", "ListedDerivative", swapA)));
        events.LoadAsync(swapB, Arg.Any<CancellationToken>()).Returns(Stream(swapB, Definition("Swap", "Derivative", "Some(ListedDerivative)", swapB)));
        events.LoadAsync(fx, Arg.Any<CancellationToken>()).Returns(Stream(fx, Definition("FxSpot", "Other", null, fx)));
        events.LoadAsync(currentSwap, Arg.Any<CancellationToken>()).Returns(Stream(currentSwap, Definition("Swap", "Derivative", "OtcDerivative", currentSwap)));
        events.LoadAsync(equity, Arg.Any<CancellationToken>()).Returns(Stream(equity, Definition("Equity", "Equity", "CommonEquity", equity)));

        var report = await sut.BuildReportAsync();

        report.SecuritiesScanned.Should().Be(5);
        report.UnreadableCount.Should().Be(0);
        report.WithoutRetainedClassificationCount.Should().Be(0);
        report.DriftedSecurityCount.Should().Be(3);
        report.Entries.Should().HaveCount(2);

        var fxEntry = report.Entries[0];
        fxEntry.TypeName.Should().Be("FxSpot");
        fxEntry.StoredAssetClass.Should().Be("Other");
        fxEntry.StoredAssetFamily.Should().BeNull();
        fxEntry.CurrentAssetClass.Should().Be("CashEquivalent");
        fxEntry.CurrentAssetFamily.Should().Be("ForeignExchange");
        fxEntry.SecurityIds.Should().Equal(fx);

        var swapEntry = report.Entries[1];
        swapEntry.TypeName.Should().Be("Swap");
        swapEntry.StoredAssetFamily.Should().Be("ListedDerivative",
            "the retired Some(...) rendering decodes to the same label and joins its group");
        swapEntry.CurrentAssetFamily.Should().Be("OtcDerivative");
        swapEntry.SecurityCount.Should().Be(2);
        swapEntry.SecurityIds.Should().Equal(swapA, swapB);
    }

    [Fact]
    public async Task BuildReportAsync_UsesTheNewestDefinitionEvent_AndCountsStreamsWithoutOne()
    {
        var amended = Guid.NewGuid();
        var bare = Guid.NewGuid();

        var (sut, store, events) = BuildSut();
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Projection(amended, "Swap"),
            Projection(bare, "Swap"),
        });
        events.LoadAsync(amended, Arg.Any<CancellationToken>()).Returns(new[]
        {
            Envelope(amended, 1, Payload(Definition("Swap", "Derivative", "ListedDerivative", amended))),
            Envelope(amended, 2, Payload(Definition("Swap", "Derivative", "OtcDerivative", amended))),
            Envelope(amended, 3, JsonSerializer.SerializeToElement(new { alias = "SWAP-1" })),
        });
        events.LoadAsync(bare, Arg.Any<CancellationToken>()).Returns(Array.Empty<SecurityMasterEventEnvelope>());

        var report = await sut.BuildReportAsync();

        report.DriftedSecurityCount.Should().Be(0, "the newest definition event already carries the current labels");
        report.WithoutRetainedClassificationCount.Should().Be(1);
        report.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildReportAsync_SkipsInactiveSecurities()
    {
        var inactive = Guid.NewGuid();
        var (sut, store, events) = BuildSut();
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Projection(inactive, "Swap", SecurityStatusDto.Inactive),
        });

        var report = await sut.BuildReportAsync();

        report.SecuritiesScanned.Should().Be(0);
        await events.DidNotReceiveWithAnyArgs().LoadAsync(default, default);
    }

    private static (SecurityMasterClassificationDriftReportService Service, ISecurityMasterStore Store, ISecurityMasterEventStore Events) BuildSut()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        var events = Substitute.For<ISecurityMasterEventStore>();
        var service = new SecurityMasterClassificationDriftReportService(
            store, events, NullLogger<SecurityMasterClassificationDriftReportService>.Instance);
        return (service, store, events);
    }

    private static SecurityMasterEventEnvelope[] Stream(Guid securityId, SecurityEconomicDefinitionRecord definition)
        => [Envelope(securityId, 1, Payload(definition))];

    private static SecurityMasterEventEnvelope Envelope(Guid securityId, long version, JsonElement payload)
        => new(
            GlobalSequence: version,
            SecurityId: securityId,
            StreamVersion: version,
            EventType: "SecurityAmended",
            EventTimestamp: AsOf.AddDays(version),
            Actor: "test",
            CorrelationId: null,
            CausationId: null,
            Payload: payload,
            Metadata: JsonSerializer.SerializeToElement(new { }));

    private static JsonElement Payload(SecurityEconomicDefinitionRecord definition)
        => JsonSerializer.SerializeToElement(definition, SecurityMasterJsonContext.Default.SecurityEconomicDefinitionRecord);

    private static SecurityEconomicDefinitionRecord Definition(
        string typeName,
        string assetClass,
        string? assetFamily,
        Guid? securityId = null)
        => new(
            SecurityId: securityId ?? Guid.NewGuid(),
            AssetClass: assetClass,
            AssetFamily: assetFamily,
            SubType: typeName,
            TypeName: typeName,
            IssuerType: null,
            RiskCountry: null,
            Status: SecurityStatusDto.Active,
            DisplayName: $"Test {typeName}",
            Currency: "USD",
            Classification: JsonSerializer.SerializeToElement(new { assetClass, assetFamily, typeName }),
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = $"Test {typeName}", currency = "USD" }),
            EconomicTerms: JsonSerializer.SerializeToElement(new { }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "test", updatedBy = "test", asOf = "2026-06-01T00:00:00+00:00" }),
            Version: 1,
            EffectiveFrom: AsOf,
            EffectiveTo: null,
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, $"TEST-{typeName}", true, AsOf)],
            LegacyAssetClass: null,
            LegacyAssetSpecificTerms: null);

    private static SecurityProjectionRecord Projection(
        Guid securityId,
        string assetClass,
        SecurityStatusDto status = SecurityStatusDto.Active)
        => new(
            SecurityId: securityId,
            AssetClass: assetClass,
            Status: status,
            DisplayName: $"Test {assetClass}",
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: $"TEST-{assetClass}",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = $"Test {assetClass}", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "test", updatedBy = "test", asOf = "2026-06-01T00:00:00+00:00" }),
            Version: 1,
            EffectiveFrom: AsOf,
            EffectiveTo: null,
            Identifiers: Array.Empty<SecurityIdentifierDto>(),
            Aliases: Array.Empty<SecurityAliasDto>());
}
