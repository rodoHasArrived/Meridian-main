using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecurityMasterQueryContract = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Application;

/// <summary>
/// The wire shape of an undeclared structured cash-flow projection, pinned at f79ee152 (main 838a74d5
/// plus increment 5a-0) before increment 5a adds a declared mode to the Security Master engine. Each
/// case goes through the public <see cref="SecurityMasterCashFlowService.GetProjectionAsync"/> seam and
/// is serialized with the UI endpoints' options (<see cref="UiEndpoints.CreateEndpointJsonOptions"/>).
/// Increment 5a must leave every undeclared projection as it is: the same twelve top-level members,
/// the same <c>termsUsed</c> members, and a row carrying a stray or malformed block projecting exactly
/// as its clean twin. Only a declared projection may add <c>scheduleEvidence</c>.
/// </summary>
/// <remarks>
/// The service reads the wall clock for its as-of date and drops periods paid before it, so every case
/// anchors on the first day of the current UTC month and matures years later.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SecurityMasterCashFlowProjectionShapeTests
{
    private static readonly Guid SecurityId = Guid.Parse("5a5a0001-0000-4000-8000-0000000005a0");

    // One source timestamp for every case, so twin projections compare equal member for member.
    private static readonly DateTimeOffset SourceLastUpdatedUtc = DateTimeOffset.UtcNow.AddHours(-1);

    private static readonly JsonSerializerOptions EndpointOptions = UiEndpoints.CreateEndpointJsonOptions();

    private static readonly string[] TopLevelMembers =
    [
        "securityId",
        "sourceKind",
        "scenario",
        "asOf",
        "schedule",
        "staleness",
        "sourceLastUpdatedUtc",
        "factorSchedule",
        "termsUsed",
        "legSchedules",
        "blockedReason",
        "isNormalizedPer100",
    ];

    private static readonly string[] TermsUsedMembers =
    [
        "maturityDate",
        "issueDate",
        "principalFace",
        "currentFactor",
        "couponRate",
        "paymentFrequency",
        "dayCountConvention",
        "factorSchedule",
        "legs",
        "principalSchedule",
        "stepCouponSchedule",
        "inflationIndex",
        "inflationBaseIndexValue",
        "inflationIndexRatio",
        "hasFactorSchedule",
        "hasLegs",
        "hasPrincipalSchedule",
        "hasStepCouponSchedule",
    ];

    // A fully-populated scheduleConventions block, as a node that writes it would store it.
    private const string PopulatedBlock =
        """{"accrualAdjusted":true,"businessDayConvention":"ModifiedFollowing","couponCashBasis":"DayCount","couponRateUnit":"Percent","direction":"Backward","endOfMonthRule":false,"firstRegularDate":"2025-09-15","lastRegularDate":"2035-03-15","paymentCalendars":["USNY","GBLO"],"paymentLagBusinessDays":2,"rollConvention":"15","stubPolicy":"Short"}""";

    // The undeclared cases: the class, the assigned source and the stored terms for an issue date.
    private static readonly IReadOnlyDictionary<string, (string AssetClass, StructuredCashFlowSourceKind SourceKind, Func<DateOnly, JsonObject> Terms)> Cases =
        new Dictionary<string, (string, StructuredCashFlowSourceKind, Func<DateOnly, JsonObject>)>(StringComparer.Ordinal)
        {
            ["Bond.fixedBullet"] = ("Bond", StructuredCashFlowSourceKind.CalculatedBullet, static issue => FixedBond(issue, "SemiAnnual")),
            ["Bond.atMaturity"] = ("Bond", StructuredCashFlowSourceKind.CalculatedBullet, static issue => FixedBond(issue, "AtMaturity")),
            ["Bond.contractualSchedule"] = ("Bond", StructuredCashFlowSourceKind.CalculatedSinker, static issue => ContractualScheduleBond(issue)),
            ["CertificateOfDeposit"] = ("CertificateOfDeposit", StructuredCashFlowSourceKind.CalculatedBullet, static issue => new JsonObject
            {
                ["issuerName"] = "First Meridian Bank",
                ["maturity"] = Date(issue.AddYears(1)),
                ["couponRate"] = 4.1m,
                ["dayCount"] = "ACT/360",
            }),
            ["StructuredCredit.flat"] = ("StructuredCredit", StructuredCashFlowSourceKind.CalculatedBullet, static issue => new JsonObject
            {
                ["termsFormat"] = "flat",
                ["assetClassTermsVersion"] = 2,
                ["tranche"] = "A-1",
                ["collateralType"] = "CLO",
                ["originalFace"] = 1_000_000m,
                ["couponOrIndex"] = "5.25",
                ["maturity"] = Date(issue.AddYears(3)),
            }),
            ["Swap.twoLeg"] = ("Swap", StructuredCashFlowSourceKind.CalculatedBullet, static issue => TwoLegSwap(issue)),
            ["Bond.blocked"] = ("Bond", StructuredCashFlowSourceKind.CalculatedBullet, static issue => CouponlessBond(issue)),
            // Stray and malformed twins: a block below its version, a block that is not an object, and
            // leg blocks below their version. None of them declares anything.
            ["Bond.stampOneStrayBlock"] = ("Bond", StructuredCashFlowSourceKind.CalculatedBullet, static issue =>
                WithBlock(Stamped(FixedBond(issue, "SemiAnnual"), 1), PopulatedBlock)),
            ["Bond.stampTwoBlockAsNumber"] = ("Bond", StructuredCashFlowSourceKind.CalculatedBullet, static issue =>
                WithBlock(Stamped(FixedBond(issue, "SemiAnnual"), 2), "7")),
            ["Swap.stampOneStrayLegBlocks"] = ("Swap", StructuredCashFlowSourceKind.CalculatedBullet, static issue => StampOneSwapWithLegBlocks(issue)),
        };

    public static TheoryData<string> UndeclaredCases()
    {
        var data = new TheoryData<string>();
        foreach (var name in Cases.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    public static TheoryData<string, string> StrayAndMalformedTwins()
        => new()
        {
            { "Bond.stampOneStrayBlock", "Bond.fixedBullet" },
            { "Bond.stampTwoBlockAsNumber", "Bond.fixedBullet" },
            { "Swap.stampOneStrayLegBlocks", "Swap.twoLeg" },
        };

    [Theory]
    [MemberData(nameof(UndeclaredCases))]
    public async Task UndeclaredProjection_SerializesTheTwelveTopLevelMembersAndTheTermsUsedMembers(string caseName)
    {
        var projection = await ProjectAsync(caseName);

        projection.Select(static member => member.Key).Should().Equal(
            TopLevelMembers,
            $"the undeclared '{caseName}' projection keeps its wire shape; only a declared projection adds scheduleEvidence");
        projection["termsUsed"].Should().BeOfType<JsonObject>()
            .Which.Select(static member => member.Key).Should().BeEquivalentTo(
                TermsUsedMembers,
                $"the undeclared '{caseName}' projection reports the same resolved terms");
    }

    [Fact]
    public async Task BlockedCase_KeepsTheShapeAndCarriesItsReason()
    {
        var projection = await ProjectAsync("Bond.blocked");

        projection["blockedReason"]!.GetValue<string>().Should().Contain("coupon rate is missing");
        projection["schedule"]!.AsArray().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(StrayAndMalformedTwins))]
    public async Task StrayOrMalformedBlock_ProjectsExactlyAsItsCleanTwin(string twinName, string cleanName)
    {
        var clean = await ProjectAsync(cleanName);
        var twin = await ProjectAsync(twinName);

        clean["schedule"]!.AsArray().Count.Should().BePositive($"the clean twin '{cleanName}' projects rows to compare");
        clean["blockedReason"].Should().BeNull();

        // The as-of is the wall clock at projection time; every other member, rows included, is equal.
        clean.Remove("asOf");
        twin.Remove("asOf");
        twin.ToJsonString().Should().Be(
            clean.ToJsonString(),
            $"'{twinName}' declares nothing, so it projects exactly as '{cleanName}'");
    }

    private static async Task<JsonObject> ProjectAsync(string caseName)
    {
        var (assetClass, sourceKind, buildTerms) = Cases[caseName];
        var issue = IssueDate();
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(SecurityId, sourceKind, SourceLastUpdatedUtc, false, null, null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(Security(assetClass, issue, JsonSerializer.SerializeToElement(buildTerms(issue))));
        var service = new SecurityMasterCashFlowService(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

        var projection = await service.GetProjectionAsync(SecurityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull($"the '{caseName}' security has a calculated source");
        return JsonSerializer.SerializeToNode(projection, EndpointOptions)!.AsObject();
    }

    private static JsonObject FixedBond(DateOnly issue, string paymentFrequency)
        => new()
        {
            ["maturity"] = Date(issue.AddYears(2)),
            ["issueDate"] = Date(issue),
            ["couponType"] = "Fixed",
            ["couponRate"] = 5m,
            ["dayCount"] = "30/360",
            ["isCallable"] = false,
            ["issuerName"] = "ACME Corp",
            ["par"] = 1000m,
            ["paymentFrequency"] = paymentFrequency,
        };

    private static JsonObject ContractualScheduleBond(DateOnly issue)
    {
        var terms = FixedBond(issue, "SemiAnnual");
        terms["principalSchedule"] = new JsonArray(
            new JsonObject { ["paymentDate"] = Date(issue.AddMonths(12)), ["amount"] = 400m },
            new JsonObject { ["paymentDate"] = Date(issue.AddMonths(18)), ["amount"] = 300m });
        return terms;
    }

    // Without a coupon rate the calculated walk refuses to project and says why.
    private static JsonObject CouponlessBond(DateOnly issue)
    {
        var terms = FixedBond(issue, "SemiAnnual");
        terms.Remove("couponRate");
        return terms;
    }

    private static JsonObject TwoLegSwap(DateOnly issue)
        => new()
        {
            ["effectiveDate"] = Date(issue),
            ["maturityDate"] = Date(issue.AddYears(2)),
            ["legs"] = new JsonArray(
                new JsonObject
                {
                    ["legId"] = "fixed",
                    ["legType"] = "Fixed",
                    ["currency"] = "USD",
                    ["direction"] = "Pay",
                    ["fixedRate"] = 4.25m,
                    ["notional"] = 1_000_000m,
                    ["paymentFrequency"] = "SemiAnnual",
                    ["dayCount"] = "30/360",
                    ["exchangesPrincipal"] = false,
                },
                new JsonObject
                {
                    ["legId"] = "float",
                    ["legType"] = "Floating",
                    ["currency"] = "USD",
                    ["direction"] = "Receive",
                    ["index"] = "SOFR",
                    ["spreadBps"] = 15m,
                    ["currentIndexRate"] = 4m,
                    ["notional"] = 1_000_000m,
                    ["paymentFrequency"] = "Quarterly",
                    ["dayCount"] = "ACT/360",
                    ["exchangesPrincipal"] = false,
                }),
        };

    private static JsonObject StampOneSwapWithLegBlocks(DateOnly issue)
    {
        var terms = Stamped(TwoLegSwap(issue), 1);
        foreach (var leg in terms["legs"]!.AsArray())
        {
            WithBlock(leg!.AsObject(), PopulatedBlock);
        }

        return terms;
    }

    // A flat payload stamped as a writer stamps it: the envelope keys first, then the terms.
    private static JsonObject Stamped(JsonObject terms, int stamp)
    {
        var stamped = new JsonObject { ["termsFormat"] = "flat", ["assetClassTermsVersion"] = stamp };
        foreach (var (key, value) in terms)
        {
            stamped[key] = value?.DeepClone();
        }

        return stamped;
    }

    private static JsonObject WithBlock(JsonObject container, string block)
    {
        container["scheduleConventions"] = JsonNode.Parse(block);
        return container;
    }

    private static SecurityDetailDto Security(string assetClass, DateOnly issue, JsonElement assetSpecificTerms)
        => new(
            SecurityId,
            assetClass,
            SecurityStatusDto.Active,
            $"Projection shape {assetClass}",
            "USD",
            JsonSerializer.SerializeToElement(new { displayName = $"Projection shape {assetClass}", currency = "USD" }),
            assetSpecificTerms,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "5A5A00001", true, SourceLastUpdatedUtc)],
            [],
            1,
            new DateTimeOffset(issue.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            null);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // The first day of the current UTC month: whole 30/360 periods, all ahead of the service's as-of.
    private static DateOnly IssueDate()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        return new DateOnly(today.Year, today.Month, 1);
    }
}
