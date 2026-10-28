using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.SecurityMaster;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Xunit;
using static Meridian.Tests.SecurityMaster.SecurityAssetClassTermsUpgraderTests;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Every read path the per-class terms upgrade hook (<see cref="SecurityAssetClassTermsUpgrader"/>) is
/// wired into reads a version-1 StructuredCredit payload exactly as it reads its version-2 twin, and
/// the identity step leaves each path's input untouched: the mapping decoder, the economic-definition
/// adapter, the structured cash-flow resolver, the obligation projection and the relational
/// projection decoder. The stored blob and the columns derived from it are never changed. The mapping
/// decoder also reads a version-3 payload, which a node reads before it writes it, as its version-2
/// twin plus the scheduleConventions block it declares, and a version-4 payload as that plus the
/// callSchedule and putSchedule arrays it declares, in stored order.
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

        // The guard still sees the stored payload before the hook, so a stamp above what the node
        // reads (versions 3 and 4 are readable ahead of write since 2026-10-25) is refused as before.
        var newer = () => SecurityMasterMapping.ToRecord(
            Record(V1Text.Replace("\"assetClassTermsVersion\":1", "\"assetClassTermsVersion\":5", StringComparison.Ordinal)));
        newer.Should().Throw<InvalidOperationException>()
            .WithMessage("Unsupported assetClassTermsVersion '5' for asset class 'StructuredCredit'; this node reads versions 1 through 4.");
    }

    // A StructuredCredit row stamped 3 by a node that writes version 3; this node reads it ahead of
    // writing it (ADR-024).
    private static string V3Text => V1Text.Replace("\"assetClassTermsVersion\":1", "\"assetClassTermsVersion\":3", StringComparison.Ordinal);

    private const string ScheduleConventionsBlock = """
        {"businessDayConvention":"ModifiedFollowing","accrualAdjusted":false,"paymentCalendars":["USNY","GBLO"],"paymentLagBusinessDays":2,"rollConvention":"15","endOfMonthRule":true,"firstRegularDate":"2024-07-15","lastRegularDate":"2031-01-15","direction":"Backward","stubPolicy":"Short","couponRateUnit":"Percent","couponCashBasis":"DayCount"}
        """;

    private static string WithScheduleConventions(string terms)
    {
        var node = JsonNode.Parse(terms)!.AsObject();
        node["scheduleConventions"] = JsonNode.Parse(ScheduleConventionsBlock);
        return node.ToJsonString();
    }

    [Fact]
    public void Mapping_ReadsVersionThreeAsVersionTwo_PlusTheScheduleConventionsItDeclares()
    {
        var v2 = SecurityMasterMapping.ToRecord(Record(V2Text)).Kind;
        SecurityMasterMapping.ToRecord(Record(V3Text)).Kind.Should().Be(v2, "version 3 adds only the optional scheduleConventions block");

        var v2Terms = ((SecurityKind.StructuredCredit)v2).Item;
        var conventions = new ScheduleConventions(
            FSharpOption<string>.Some("ModifiedFollowing"),
            FSharpOption<bool>.Some(false),
            ListModule.OfSeq(new[] { "USNY", "GBLO" }),
            FSharpOption<int>.Some(2),
            FSharpOption<string>.Some("15"),
            FSharpOption<bool>.Some(true),
            FSharpOption<DateOnly>.Some(new DateOnly(2024, 7, 15)),
            FSharpOption<DateOnly>.Some(new DateOnly(2031, 1, 15)),
            FSharpOption<string>.Some("Backward"),
            FSharpOption<string>.Some("Short"),
            FSharpOption<string>.Some("Percent"),
            FSharpOption<string>.Some("DayCount"));
        var expected = SecurityKind.NewStructuredCredit(new StructuredCreditTerms(
            v2Terms.Tranche,
            v2Terms.PoolId,
            v2Terms.CollateralType,
            v2Terms.OriginalFace,
            v2Terms.CurrentFactor,
            v2Terms.CouponOrIndex,
            v2Terms.FactorSchedule,
            v2Terms.FactorScheduleEntries,
            v2Terms.Maturity,
            v2Terms.Kind,
            v2Terms.Seniority,
            v2Terms.AttachmentPoint,
            v2Terms.DetachmentPoint,
            v2Terms.WeightedAverageCoupon,
            v2Terms.WeightedAverageMaturityMonths,
            v2Terms.WeightedAverageLoanAgeMonths,
            v2Terms.ReinvestmentPeriodEnd,
            v2Terms.NonCallPeriodEnd,
            conventions,
            CallPutSchedulesModule.Empty));
        v2Terms.ScheduleConventions.Should().Be(ScheduleConventionsModule.Empty);

        SecurityMasterMapping.ToRecord(Record(WithScheduleConventions(V3Text))).Kind.Should().Be(expected);

        // Decoding is gated on the stamp: below version 3 the same block is not read.
        SecurityMasterMapping.ToRecord(Record(WithScheduleConventions(V2Text))).Kind.Should().Be(v2);
    }

    // A StructuredCredit row stamped 4 by a node that writes version 4; this node reads it ahead of
    // writing it (ADR-024).
    private static string V4Text => V1Text.Replace("\"assetClassTermsVersion\":1", "\"assetClassTermsVersion\":4", StringComparison.Ordinal);

    // Both arrays are stored out of date order, so a decoder that sorted would fail: the flat terms
    // keep stored order. Together the entries carry every call and put member.
    private const string CallScheduleArray = """
        [{"callDate":"2028-06-15","callEndDate":"2031-06-14","exerciseStyle":"Bermudan","exerciseFrequency":"Quarterly","callPrice":101.5,"priceBasis":"PercentOfPar","priceType":"Fixed","partialRedemption":"ProRata","minimumNoticeDays":10,"maximumNoticeDays":60,"noticeDayType":"Business"},{"callDate":"2026-06-15","callEndDate":"2028-06-14","exerciseStyle":"American","priceBasis":"PercentOfPar","priceType":"MakeWhole","makeWholeSpreadBps":25,"partialRedemption":"WholeOnly","minimumNoticeDays":30,"maximumNoticeDays":60,"noticeDayType":"Calendar"}]
        """;

    private const string PutScheduleArray = """
        [{"putDate":"2029-06-15","putEndDate":"2030-06-15","exerciseStyle":"Bermudan","exerciseFrequency":"SemiAnnual","putPrice":100,"priceBasis":"PercentOfPar","minimumNoticeDays":15,"maximumNoticeDays":45,"noticeDayType":"Business"},{"putDate":"2027-06-15"}]
        """;

    private static string WithCallAndPutSchedules(string terms)
    {
        var node = JsonNode.Parse(terms)!.AsObject();
        node["callSchedule"] = JsonNode.Parse(CallScheduleArray);
        node["putSchedule"] = JsonNode.Parse(PutScheduleArray);
        return node.ToJsonString();
    }

    private static CallPutSchedules DeclaredCallAndPutSchedules()
        => new(
            ListModule.OfSeq(new[]
            {
                new CallScheduleTerm(
                    new DateOnly(2028, 6, 15),
                    FSharpOption<DateOnly>.Some(new DateOnly(2031, 6, 14)),
                    FSharpOption<string>.Some("Bermudan"),
                    FSharpOption<string>.Some("Quarterly"),
                    FSharpOption<decimal>.Some(101.5m),
                    FSharpOption<string>.Some("PercentOfPar"),
                    FSharpOption<string>.Some("Fixed"),
                    FSharpOption<decimal>.None,
                    FSharpOption<string>.Some("ProRata"),
                    FSharpOption<int>.Some(10),
                    FSharpOption<int>.Some(60),
                    FSharpOption<string>.Some("Business")),
                new CallScheduleTerm(
                    new DateOnly(2026, 6, 15),
                    FSharpOption<DateOnly>.Some(new DateOnly(2028, 6, 14)),
                    FSharpOption<string>.Some("American"),
                    FSharpOption<string>.None,
                    FSharpOption<decimal>.None,
                    FSharpOption<string>.Some("PercentOfPar"),
                    FSharpOption<string>.Some("MakeWhole"),
                    FSharpOption<decimal>.Some(25m),
                    FSharpOption<string>.Some("WholeOnly"),
                    FSharpOption<int>.Some(30),
                    FSharpOption<int>.Some(60),
                    FSharpOption<string>.Some("Calendar")),
            }),
            ListModule.OfSeq(new[]
            {
                new PutScheduleTerm(
                    new DateOnly(2029, 6, 15),
                    FSharpOption<DateOnly>.Some(new DateOnly(2030, 6, 15)),
                    FSharpOption<string>.Some("Bermudan"),
                    FSharpOption<string>.Some("SemiAnnual"),
                    FSharpOption<decimal>.Some(100m),
                    FSharpOption<string>.Some("PercentOfPar"),
                    FSharpOption<int>.Some(15),
                    FSharpOption<int>.Some(45),
                    FSharpOption<string>.Some("Business")),
                new PutScheduleTerm(
                    new DateOnly(2027, 6, 15),
                    FSharpOption<DateOnly>.None,
                    FSharpOption<string>.None,
                    FSharpOption<string>.None,
                    FSharpOption<decimal>.None,
                    FSharpOption<string>.None,
                    FSharpOption<int>.None,
                    FSharpOption<int>.None,
                    FSharpOption<string>.None),
            }));

    // The same terms with only the call and put schedules replaced.
    private static SecurityKind StructuredCreditWith(StructuredCreditTerms terms, CallPutSchedules schedules)
        => SecurityKind.NewStructuredCredit(new StructuredCreditTerms(
            terms.Tranche,
            terms.PoolId,
            terms.CollateralType,
            terms.OriginalFace,
            terms.CurrentFactor,
            terms.CouponOrIndex,
            terms.FactorSchedule,
            terms.FactorScheduleEntries,
            terms.Maturity,
            terms.Kind,
            terms.Seniority,
            terms.AttachmentPoint,
            terms.DetachmentPoint,
            terms.WeightedAverageCoupon,
            terms.WeightedAverageMaturityMonths,
            terms.WeightedAverageLoanAgeMonths,
            terms.ReinvestmentPeriodEnd,
            terms.NonCallPeriodEnd,
            terms.ScheduleConventions,
            schedules));

    [Fact]
    public void Mapping_ReadsVersionFourAsVersionTwo_PlusTheBlockAndTheCallAndPutSchedulesItDeclares()
    {
        var v2 = SecurityMasterMapping.ToRecord(Record(V2Text)).Kind;
        SecurityMasterMapping.ToRecord(Record(V4Text)).Kind
            .Should().Be(v2, "version 4 adds only the optional callSchedule and putSchedule arrays to version 3's optional block");

        // Version 3's block reads the same at stamp 4 (the version-3 test above pins its decode).
        var withBlock = SecurityMasterMapping.ToRecord(Record(WithScheduleConventions(V3Text))).Kind;
        var blockTerms = ((SecurityKind.StructuredCredit)withBlock).Item;
        blockTerms.ScheduleConventions.Should().NotBe(ScheduleConventionsModule.Empty);
        blockTerms.CallPutSchedules.Should().Be(CallPutSchedulesModule.Empty);
        SecurityMasterMapping.ToRecord(Record(WithScheduleConventions(V4Text))).Kind.Should().Be(withBlock);

        var schedules = DeclaredCallAndPutSchedules();
        var decoded = SecurityMasterMapping.ToRecord(Record(WithCallAndPutSchedules(WithScheduleConventions(V4Text)))).Kind;
        decoded.Should().Be(StructuredCreditWith(blockTerms, schedules));

        var decodedSchedules = ((SecurityKind.StructuredCredit)decoded).Item.CallPutSchedules;
        decodedSchedules.Calls.Select(static call => call.CallDate)
            .Should().Equal(new[] { new DateOnly(2028, 6, 15), new DateOnly(2026, 6, 15) }, "the flat terms keep stored order");
        decodedSchedules.Puts.Select(static put => put.PutDate)
            .Should().Equal(new[] { new DateOnly(2029, 6, 15), new DateOnly(2027, 6, 15) }, "the flat terms keep stored order");

        // The arrays decode without the block too, beside the version-2 terms.
        SecurityMasterMapping.ToRecord(Record(WithCallAndPutSchedules(V4Text))).Kind
            .Should().Be(StructuredCreditWith(((SecurityKind.StructuredCredit)v2).Item, schedules));
    }

    [Fact]
    public void Mapping_DoesNotDecodeTheCallAndPutSchedules_BelowVersionFour()
    {
        // Decoding is gated on the stamp: at version 3 the block is read and the same arrays are not.
        var withBlock = SecurityMasterMapping.ToRecord(Record(WithScheduleConventions(V3Text))).Kind;
        var stampThree = SecurityMasterMapping.ToRecord(Record(WithCallAndPutSchedules(WithScheduleConventions(V3Text)))).Kind;

        stampThree.Should().Be(withBlock);
        ((SecurityKind.StructuredCredit)stampThree).Item.CallPutSchedules.Should().Be(CallPutSchedulesModule.Empty);
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
    [InlineData("src/Meridian.Application/SecurityMaster/CashFlow/SecurityPrincipalBalance.cs")]
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
