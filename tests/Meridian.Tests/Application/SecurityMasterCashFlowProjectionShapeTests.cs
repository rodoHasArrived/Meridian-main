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
/// the same <c>termsUsed</c> members, the same nested members in every row it writes, and a row
/// carrying a stray or malformed block projecting exactly as its clean twin. Only a declared projection
/// may add <c>scheduleEvidence</c>.
/// </summary>
/// <remarks>
/// The service reads the wall clock for its as-of date and drops periods paid before it, so every case
/// anchors on the first day of the UTC month the run starts in and matures years later.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SecurityMasterCashFlowProjectionShapeTests
{
    private static readonly Guid SecurityId = Guid.Parse("5a5a0001-0000-4000-8000-0000000005a0");

    // One source timestamp and one issue date for every case, so twin projections compare equal
    // member for member even when the run crosses midnight or a month boundary.
    private static readonly DateTimeOffset SourceLastUpdatedUtc = DateTimeOffset.UtcNow.AddHours(-1);

    private static readonly DateOnly IssueDate = FirstDayOfCurrentUtcMonth();

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

    // The member paths of the rows a projection writes, array indices collapsed to [], so a member
    // added to a nested record fails the pin as a top-level one does.
    private static readonly string[] ScheduleRowPaths =
    [
        "schedule[].periodDate",
        "schedule[].principalAmount",
        "schedule[].interestAmount",
        "schedule[].factor",
    ];

    private static readonly string[] SwapLegPaths =
    [
        "legSchedules[].legId",
        "legSchedules[].rateKind",
        "legSchedules[].direction",
        "legSchedules[].schedule",
        "legSchedules[].schedule[].periodDate",
        "legSchedules[].schedule[].principalAmount",
        "legSchedules[].schedule[].interestAmount",
        "legSchedules[].schedule[].factor",
        "legSchedules[].rateSource",
        "termsUsed.legs[].legId",
        "termsUsed.legs[].rateKind",
        "termsUsed.legs[].direction",
        "termsUsed.legs[].fixedRate",
        "termsUsed.legs[].indexName",
        "termsUsed.legs[].spreadBps",
        "termsUsed.legs[].currentIndexRate",
        "termsUsed.legs[].notional",
        "termsUsed.legs[].paymentFrequency",
        "termsUsed.legs[].dayCountConvention",
        "termsUsed.legs[].exchangesPrincipal",
        "termsUsed.legs[].notionalSchedule",
        "termsUsed.legs[].resetSchedule",
    ];

    // The termsUsed leg members a blocked swap still reports: SwapLegPaths without the leg schedules.
    private static readonly string[] SwapTermsLegPaths = SwapLegPaths.Where(static path => path.StartsWith("termsUsed.", StringComparison.Ordinal)).ToArray();

    private static readonly string[] PrincipalScheduleRowPaths =
    [
        "termsUsed.principalSchedule[].paymentDate",
        "termsUsed.principalSchedule[].amount",
    ];

    // The rows each case writes beyond the top-level and termsUsed members. No case writes a factor
    // schedule, a step-coupon schedule, or a leg notional or reset schedule.
    // D3 (decided 2026-11-05): undeclared non-zero rates block, so every undeclared case with a
    // non-zero rate writes no rows and carries its block reason (UndeclaredRateBlocks below). Before D3
    // the bonds, the CD and the StructuredCredit wrote ScheduleRowPaths (the contractual-schedule bond
    // also PrincipalScheduleRowPaths) and the swaps also SwapLegPaths. The zero-rate cases need no unit
    // and keep those undeclared row shapes pinned.
    private static readonly IReadOnlyDictionary<string, string[][]> RowsWritten =
        new Dictionary<string, string[][]>(StringComparer.Ordinal)
        {
            ["Bond.fixedBullet"] = [],
            ["Bond.atMaturity"] = [],
            ["Bond.contractualSchedule"] = [PrincipalScheduleRowPaths],
            ["CertificateOfDeposit"] = [],
            ["StructuredCredit.flat"] = [],
            ["Swap.twoLeg"] = [SwapTermsLegPaths],
            ["Bond.blocked"] = [],
            ["Bond.stampOneStrayBlock"] = [],
            ["Bond.stampTwoBlockAsNumber"] = [],
            ["Swap.stampOneStrayLegBlocks"] = [SwapTermsLegPaths],
            ["Bond.zeroRateBullet"] = [ScheduleRowPaths],
            ["Bond.zeroRateContractualSchedule"] = [ScheduleRowPaths, PrincipalScheduleRowPaths],
            ["Swap.zeroRateTwoLeg"] = [ScheduleRowPaths, SwapLegPaths],
        };

    private const string UndeclaredUnitTail =
        " has no declared unit (Fraction, Percent or BasisPoints). A legacy value is flagged rather than converted; its unit cannot be inferred from its magnitude.";

    // D3 (decided 2026-11-05): the exact block each undeclared non-zero case now carries.
    private static readonly IReadOnlyDictionary<string, string> UndeclaredRateBlocks =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Bond.fixedBullet"] = "Rate couponRate = 5 on Bond" + UndeclaredUnitTail,
            ["Bond.atMaturity"] = "Rate couponRate = 5 on Bond" + UndeclaredUnitTail,
            ["Bond.contractualSchedule"] = "Rate couponRate = 5 on Bond" + UndeclaredUnitTail,
            ["CertificateOfDeposit"] = "Rate couponRate = 4.1 on CertificateOfDeposit" + UndeclaredUnitTail,
            ["StructuredCredit.flat"] = "Rate couponOrIndex = 5.25 on StructuredCredit" + UndeclaredUnitTail,
            ["Swap.twoLeg"] = "Rate legs.fixedRate = 4.25 on Swap" + UndeclaredUnitTail,
            ["Bond.stampOneStrayBlock"] = "Rate couponRate = 5 on Bond" + UndeclaredUnitTail,
            ["Bond.stampTwoBlockAsNumber"] = "Rate couponRate = 5 on Bond" + UndeclaredUnitTail,
            ["Swap.stampOneStrayLegBlocks"] = "Rate legs.fixedRate = 4.25 on Swap" + UndeclaredUnitTail,
        };

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
            // D3 (decided 2026-11-05): a zero rate resolves without a unit, so these undeclared cases
            // still walk the legacy projection and pin its row shapes.
            ["Bond.zeroRateBullet"] = ("Bond", StructuredCashFlowSourceKind.CalculatedBullet, static issue => ZeroRate(FixedBond(issue, "SemiAnnual"))),
            ["Bond.zeroRateContractualSchedule"] = ("Bond", StructuredCashFlowSourceKind.CalculatedSinker, static issue => ZeroRate(ContractualScheduleBond(issue))),
            ["Swap.zeroRateTwoLeg"] = ("Swap", StructuredCashFlowSourceKind.CalculatedBullet, static issue => ZeroRateSwap(TwoLegSwap(issue))),
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

    [Theory]
    [MemberData(nameof(UndeclaredCases))]
    public async Task UndeclaredProjection_SerializesTheSameNestedMembers(string caseName)
    {
        var projection = await ProjectAsync(caseName);

        var expected = TopLevelMembers
            .Concat(TermsUsedMembers.Select(static member => $"termsUsed.{member}"))
            .Concat(RowsWritten[caseName].SelectMany(static rows => rows));
        MemberPaths(projection).Should().BeEquivalentTo(
            expected,
            $"the undeclared '{caseName}' projection writes the same members in every row");
        if (UndeclaredRateBlocks.TryGetValue(caseName, out var reason))
        {
            // D3 (decided 2026-11-05): the undeclared non-zero rate blocks with its exact reason.
            projection["blockedReason"]!.GetValue<string>().Should().Be(reason);
        }
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

        // D3 (decided 2026-11-05): undeclared non-zero rates block, so both twins block with the same
        // reason; the comparison below still requires every other member to be equal.
        clean["schedule"]!.AsArray().Should().BeEmpty($"the clean twin '{cleanName}' declares no rate unit");
        clean["blockedReason"]!.GetValue<string>().Should().Be(UndeclaredRateBlocks[cleanName]);
        twin["blockedReason"]!.GetValue<string>().Should().Be(UndeclaredRateBlocks[cleanName]);

        // The as-of is the wall clock at projection time; every other member, rows included, is equal.
        clean.Remove("asOf");
        twin.Remove("asOf");
        twin.ToJsonString().Should().Be(
            clean.ToJsonString(),
            $"'{twinName}' declares nothing, so it projects exactly as '{cleanName}'");
    }

    [Fact]
    public async Task DeclaredProjection_AddsExactlyScheduleEvidence()
    {
        // A Bond stamped at its scheduleConventions version with a minimal block declares a schedule.
        // D3 (decided 2026-11-05): the block also declares the Percent its 5 coupon was written in.
        var projection = await ProjectAsync(
            "Bond.declared",
            "Bond",
            StructuredCashFlowSourceKind.CalculatedBullet,
            WithBlock(Stamped(FixedBond(IssueDate, "SemiAnnual"), 2), """{"accrualAdjusted":false,"couponRateUnit":"Percent"}"""));

        projection["blockedReason"].Should().BeNull();
        projection["schedule"]!.AsArray().Count.Should().BePositive("the declared bond projects rows");
        projection.Select(static member => member.Key).Should().Equal(
            TopLevelMembers.Append("scheduleEvidence"),
            "a declared projection keeps the twelve members and adds only scheduleEvidence");
        projection["scheduleEvidence"].Should().BeOfType<JsonObject>()
            .Which.Select(static member => member.Key).Should().Equal(
                "kernelVersion",
                "declaredMappingVersion",
                "calendars",
                "coverageGaps");
        projection["termsUsed"].Should().BeOfType<JsonObject>()
            .Which.Select(static member => member.Key).Should().BeEquivalentTo(TermsUsedMembers);
    }

    private static Task<JsonObject> ProjectAsync(string caseName)
    {
        var (assetClass, sourceKind, buildTerms) = Cases[caseName];
        return ProjectAsync(caseName, assetClass, sourceKind, buildTerms(IssueDate));
    }

    private static async Task<JsonObject> ProjectAsync(
        string caseName,
        string assetClass,
        StructuredCashFlowSourceKind sourceKind,
        JsonObject terms)
    {
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(SecurityId, sourceKind, SourceLastUpdatedUtc, false, null, null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(Security(assetClass, IssueDate, JsonSerializer.SerializeToElement(terms)));
        var service = new SecurityMasterCashFlowService(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance);

        var projection = await service.GetProjectionAsync(SecurityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull($"the '{caseName}' security has a calculated source");
        return JsonSerializer.SerializeToNode(projection, EndpointOptions)!.AsObject();
    }

    // Every member path in the serialized projection, array indices collapsed to [].
    private static IReadOnlyCollection<string> MemberPaths(JsonObject projection)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        Collect(projection, string.Empty);
        return paths;

        void Collect(JsonNode? node, string path)
        {
            switch (node)
            {
                case JsonObject members:
                    foreach (var (key, value) in members)
                    {
                        var memberPath = path.Length == 0 ? key : $"{path}.{key}";
                        paths.Add(memberPath);
                        Collect(value, memberPath);
                    }

                    break;
                case JsonArray items:
                    foreach (var item in items)
                    {
                        Collect(item, $"{path}[]");
                    }

                    break;
            }
        }
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

    private static JsonObject ZeroRate(JsonObject bond)
    {
        bond["couponRate"] = 0m;
        return bond;
    }

    private static JsonObject ZeroRateSwap(JsonObject swap)
    {
        var legs = swap["legs"]!.AsArray();
        legs[0]!["fixedRate"] = 0m;
        legs[1]!["currentIndexRate"] = 0m;
        return swap;
    }

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
    private static DateOnly FirstDayOfCurrentUtcMonth()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        return new DateOnly(today.Year, today.Month, 1);
    }
}
