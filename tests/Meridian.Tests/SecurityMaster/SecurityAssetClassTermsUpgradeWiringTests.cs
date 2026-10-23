using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.SecurityMaster;
using Xunit;
using static Meridian.Tests.SecurityMaster.SecurityAssetClassTermsUpgraderTests;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Every read path the per-class terms upgrade hook (<see cref="SecurityAssetClassTermsUpgrader"/>) is
/// wired into reads a version-1 StructuredCredit payload exactly as it reads its version-2 twin, and
/// the identity step leaves each path's input untouched: the mapping decoder, the economic-definition
/// adapter, the structured cash-flow resolver, the obligation projection and the relational
/// projection decoder. The stored blob and the columns derived from it are never changed.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetClassTermsUpgradeWiringTests
{
    private static readonly Guid SecurityId = Guid.Parse("7d0a1c52-0000-4000-8000-0000000000c1");

    private const string ProfileBackedText = """
        {"schemaVersion":3,"termsFormat":"customProfile","customProfileId":"structured-credit-v1","profileVersion":1,"profileFields":{"tranche":"A-1","collateralType":"CLO","originalFace":10000000,"couponOrIndex":"SOFR+250","maturity":"2031-06-15"}}
        """;

    private static SecurityProjectionRecord Record(string terms)
        => SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("StructuredCredit", Json(terms)) with { SecurityId = SecurityId };

    private static SecurityDetailDto Detail(string terms)
        => new(
            SecurityId,
            "StructuredCredit",
            SecurityStatusDto.Active,
            "MRDN 2024-1 A-1",
            "USD",
            JsonSerializer.SerializeToElement(new { currency = "USD" }),
            Json(terms),
            [],
            [],
            3,
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null);

    [Fact]
    public void Mapping_ReadsVersionOneAsVersionTwo_AndStillRefusesANewerStamp()
    {
        var v2 = SecurityMasterMapping.ToRecord(Record(V2Text)).Kind;
        SecurityMasterMapping.ToRecord(Record(V1Text)).Kind.Should().Be(v2);
        SecurityMasterMapping.ToRecord(Record(UnstampedText)).Kind.Should().Be(v2);

        // The guard still sees the stored payload before the hook, so a newer stamp is refused as before.
        var newer = () => SecurityMasterMapping.ToRecord(
            Record(V1Text.Replace("\"assetClassTermsVersion\":1", "\"assetClassTermsVersion\":3", StringComparison.Ordinal)));
        newer.Should().Throw<InvalidOperationException>().WithMessage($"*{SecurityTermsEnvelope.AssetClassTermsVersionKey}*");
    }

    [Fact]
    public void EconomicAdapter_CarriesRetainedVersionOneTermsVerbatim_AndStampsTheFlattenedBranchAtTheCurrentVersion()
    {
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(Record(V1Text));

        var projected = SecurityEconomicDefinitionAdapter.ToProjection(economic).AssetSpecificTerms;
        projected.GetRawText().Should().Be(Json(V1Text).GetRawText(), "the retained version-1 bytes are carried, not restamped");
        JsonElement.DeepEquals(projected, Json(V1Text)).Should().BeTrue();

        var flattened = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null }).AssetSpecificTerms;
        flattened.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32()
            .Should().Be(SecurityAssetClassCatalog.GetTermsVersion("StructuredCredit"));
    }

    [Fact]
    public void Resolver_YieldsTheStoredElements_AndResolvesVersionOneAsVersionTwo()
    {
        var v1 = Detail(V1Text);
        var sources = StructuredCashFlowTermsResolver.EnumerateTermSources(v1).ToArray();
        sources[0].Equals((object)v1.AssetSpecificTerms).Should().BeTrue("the identity step yields the stored element, not a copy");
        sources[0].Equals((object)Copy(v1.AssetSpecificTerms)).Should().BeFalse();

        var profileBacked = Detail(ProfileBackedText);
        var profileSources = StructuredCashFlowTermsResolver.EnumerateTermSources(profileBacked).ToArray();
        profileSources[0].Equals((object)profileBacked.AssetSpecificTerms.GetProperty("profileFields")).Should().BeTrue();
        profileSources[1].Equals((object)profileBacked.AssetSpecificTerms).Should().BeTrue();

        StructuredCashFlowTermsResolver.Resolve(v1).Should().BeEquivalentTo(StructuredCashFlowTermsResolver.Resolve(Detail(V2Text)));
    }

    [Fact]
    public void ObligationProjection_ProjectsVersionOneAsVersionTwo_AndWrapsTheStoredTerms()
    {
        var service = new AssetObligationProjectionService();
        var v1 = service.ProjectFromSecurityMaster(Detail(V1Text));
        var v2 = service.ProjectFromSecurityMaster(Detail(V2Text));

        v1.ProjectedCashFlows.Should().NotBeEmpty();
        v1.ProjectedCashFlows.Select(static flow => (flow.FlowType, flow.DueDate, flow.AccrualStartDate, flow.AccrualEndDate, flow.Amount))
            .Should().Equal(v2.ProjectedCashFlows.Select(static flow => (flow.FlowType, flow.DueDate, flow.AccrualStartDate, flow.AccrualEndDate, flow.Amount)));
        (v1.TermsObligationsTimeline?.Events ?? []).Select(static item => (item.EventKind, item.EventLane, item.EffectiveDate, item.ExpectedAmount))
            .Should().Equal((v2.TermsObligationsTimeline?.Events ?? []).Select(static item => (item.EventKind, item.EventLane, item.EffectiveDate, item.ExpectedAmount)));

        // The wrapper is re-serialized (its encoder escapes '+'), so the comparison is by value.
        JsonElement.DeepEquals(
                v1.TermsHistory.Should().ContainSingle().Which.ExtensionPayload!.Value.GetProperty("AssetSpecificTerms"),
                Json(V1Text))
            .Should().BeTrue("the identity step wraps the stored terms unchanged");
    }

    [Fact]
    public void RelationalProjection_DecodesVersionOneAsVersionTwo_AndTheStoredColumnsStayDerivedFromTheBlob()
    {
        var descriptor = SecurityTermsProjectionRegistry.Require("StructuredCredit");
        var v1Record = Record(V1Text);
        var v1Before = v1Record.AssetSpecificTerms.GetRawText();

        PostgresSecurityMasterStore.TryBuildTermsProjection(descriptor, v1Record, out var v1).Should().BeTrue();
        PostgresSecurityMasterStore.TryBuildTermsProjection(descriptor, Record(V2Text), out var v2).Should().BeTrue();

        Columns(v1.Columns).Should().Equal(Columns(v2.Columns));
        v1.Children.Select(static child => child.TableName).Should().Equal(v2.Children.Select(static child => child.TableName));
        v1.Children.Should().Contain(static child => child.TableName == "structured_credit_factor_schedule_projection");
        for (var index = 0; index < v1.Children.Count; index++)
        {
            v1.Children[index].Rows.SelectMany(Columns).Should().Equal(v2.Children[index].Rows.SelectMany(Columns));
        }

        v1.Satellites.Select(static satellite => (satellite.TableName, satellite.Columns is null))
            .Should().Equal(v2.Satellites.Select(static satellite => (satellite.TableName, satellite.Columns is null)));
        for (var index = 0; index < v1.Satellites.Count; index++)
        {
            Columns(v1.Satellites[index].Columns ?? []).Should().Equal(Columns(v2.Satellites[index].Columns ?? []));
        }

        v1Record.AssetSpecificTerms.GetRawText().Should().Be(v1Before, "decoding never rewrites the stored blob");
        PostgresSecurityMasterStore.ResolveStorableTermsFormat(v1Record).Should().Be(SecurityTermsFormats.Flat);
    }

    [Fact]
    public void ClassValidators_ReadVersionOneAsVersionTwo()
    {
        var service = new Meridian.Application.SecurityMaster.Validation.SecurityValidationService(
            NSubstitute.Substitute.For<ISecurityMasterStore>(),
            Meridian.Application.SecurityMaster.Validation.AssetClassValidatorRegistry.CreateDefault());
        var evaluatedAt = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

        var v1 = service.ValidateRecord(Record(V1Text), [Record(V1Text)], evaluatedAt);
        var v2 = service.ValidateRecord(Record(V2Text), [Record(V2Text)], evaluatedAt);

        v1.Issues.Select(static issue => issue.Code).Where(static code => code != "SM_ASSET_CLASS_TERMS_VERSION_BEHIND")
            .Should().Equal(v2.Issues.Select(static issue => issue.Code));
        v1.HasBlockingIssues.Should().Be(v2.HasBlockingIssues);
    }

    [Theory]
    [InlineData("src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs")]
    [InlineData("src/Meridian.Application/SecurityMaster/SecurityEconomicDefinitionAdapter.cs")]
    [InlineData("src/Meridian.Application/SecurityMaster/SecurityMasterCashFlowService.cs")]
    [InlineData("src/Meridian.Application/SecurityMaster/Validation/SecurityValidationService.cs")]
    [InlineData("src/Meridian.Contracts/SecurityMaster/StructuredCashFlowTermsResolver.cs")]
    [InlineData("src/Meridian.Instruments/AssetOperations/AssetObligationProjectionService.cs")]
    [InlineData("src/Meridian.Storage/SecurityMaster/PostgresSecurityMasterStore.TermsProjection.cs")]
    public void WiredReadPaths_CallTheUpgradeHook(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));

        (source.Contains("SecurityAssetClassTermsUpgrader.UpgradeForRead(", StringComparison.Ordinal)
         || source.Contains("SecurityAssetClassTermsUpgrader.TryUpgradeForRead(", StringComparison.Ordinal))
            .Should().BeTrue($"{relativePath} reads stored flat terms and must read them through the per-class upgrade hook");
    }

    private static IEnumerable<(string ColumnName, object Value)> Columns(IReadOnlyList<PostgresSecurityMasterStore.SecurityTermsProjectionValue> values)
        => values.Select(static value => (value.ColumnName, value.Value));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Unable to locate Meridian repository root from test output directory.");
    }
}
