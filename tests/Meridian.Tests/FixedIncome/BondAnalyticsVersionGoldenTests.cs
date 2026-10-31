using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

/// <summary>
/// Golden grid for <c>bond-analytics-v1</c> and <c>bond-analytics-v2</c> results, captured before the
/// v1 schedule and exercise clones were folded into the shared <see cref="BondContractualEconomics"/>
/// and <see cref="BondExerciseEconomics"/> kernels. It is not a defect pin: it freezes governed
/// analytics output so the consolidation can prove itself bit-identical for both retained versions.
/// </summary>
/// <remarks>
/// <para>The grid is two calculation versions x nine bond shapes (regular Level and DayCount cash,
/// stub first coupons under ICMA and Actual/365, a two-business-day payment lag, European call/put
/// with calendar notice, a quarterly Bermudan call, a call that crosses a lagged coupon, and a
/// zero coupon with annual-effective yield) x two settlement dates x a yield request and a clean
/// price request: 72 cases. Decimals are stored as invariant strings, so equality is exact in both
/// value and scale. Blocked results store their blocker codes.</para>
/// <para>A missing or mismatched fixture writes <c>bond-analytics-versions.received.json</c> beside
/// the test binaries and fails. It never skips. Replace the committed fixture under
/// tests/fixtures/fixed-income/golden/ only when the change in governed output is intended.</para>
/// </remarks>
public sealed class BondAnalyticsVersionGoldenTests
{
    private const string FixtureFileName = "bond-analytics-versions.json";
    private const string ReceivedFileName = "bond-analytics-versions.received.json";
    private const int ExpectedCaseCount = 72;
    private static readonly Guid SecurityId = Guid.Parse("7a1c0d2e-0000-4000-8000-00000000a1a1");
    private static readonly string[] Versions = ["bond-analytics-v1", "bond-analytics-v2"];

    [Fact]
    public void AnalyticsVersions_MatchCommittedGoldenGrid()
    {
        var received = ComputeGrid();
        Assert.Equal(ExpectedCaseCount, received.Count);

        var fixturePath = LocateFixture();
        var failure = fixturePath is null
            ? $"The golden fixture '{FixtureFileName}' was not found beside the test binaries or under tests/fixtures/fixed-income/golden/."
            : Compare(ReadExpected(fixturePath), received);
        if (failure is null)
            return;

        var receivedPath = WriteReceived(received);
        Assert.Fail(
            $"{failure}{Environment.NewLine}The computed grid was written to '{receivedPath}'. " +
            "Replace tests/fixtures/fixed-income/golden/" + FixtureFileName + " with it only when the change in governed output is intended.");
    }

    private static List<GoldenCase> ComputeGrid()
    {
        var calendars = new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);
        var cases = new List<GoldenCase>();
        foreach (var shape in Shapes)
        {
            var query = Substitute.For<ISecurityMasterQueryService>();
            query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(shape.Security);
            var service = new BondAnalyticsService(query, calendars);
            foreach (var version in Versions)
                foreach (var (settlementName, settlement) in shape.Settlements)
                    foreach (var (requestName, price, yield) in Requests)
                    {
                        var context = new BondCalculationContext(
                            new DateTimeOffset(settlement.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
                        var request = new BondAnalyticsRequest(SecurityId, context, price, yield, shape.Convention)
                        { CalculationVersion = version };
                        var result = service.CalculateAsync(request).GetAwaiter().GetResult()!;
                        cases.Add(new($"{version}|{shape.Name}|{settlementName}|{requestName}", Render(result)));
                    }
        }
        return cases;
    }

    private static readonly (string Name, decimal? Price, decimal? Yield)[] Requests =
    [
        ("yield-5pct", null, 0.05m),
        ("clean-98.5", 98.5m, null),
    ];

    private static IReadOnlyList<BondShape> Shapes =>
    [
        new("regular-level-30-360", Bond(new()), null,
            [("on-coupon", new(2026, 1, 15)), ("mid-period", new(2026, 4, 15))]),
        new("regular-daycount-30-360", Bond(new() { ["scheduleConventions"] = Conventions("DayCount") }), null,
            [("on-coupon", new(2026, 1, 15)), ("mid-period", new(2026, 4, 15))]),
        new("stub-icma-level", Bond(new()
        {
            ["issueDate"] = "2025-12-01", ["maturity"] = "2027-01-01", ["dayCount"] = "ACT/ACT ICMA",
            ["scheduleConventions"] = Conventions("Level", firstRegular: "2026-01-01")
        }), null, [("issue", new(2025, 12, 1)), ("in-stub", new(2025, 12, 20))]),
        new("stub-act365-daycount", Bond(new()
        {
            ["issueDate"] = "2025-12-01", ["maturity"] = "2027-01-01", ["dayCount"] = "ACT/365F",
            ["scheduleConventions"] = Conventions("DayCount", firstRegular: "2026-01-01")
        }), null, [("issue", new(2025, 12, 1)), ("after-stub", new(2026, 3, 10))]),
        new("payment-lag-2bd", Bond(new()
        {
            ["issueDate"] = "2026-01-30", ["maturity"] = "2027-01-30",
            ["scheduleConventions"] = Conventions("Level", lag: 2)
        }), null, [("issue", new(2026, 1, 30)), ("mid-period", new(2026, 5, 4))]),
        new("european-call-put-notice", Bond(new()
        {
            ["assetClassTermsVersion"] = 3, ["isCallable"] = true, ["callDate"] = "2027-01-15",
            ["callSchedule"] = new[] { new { callDate = "2027-01-15", exerciseStyle = "European", callPrice = 101m,
                priceBasis = "PercentOfPar", priceType = "Fixed", minimumNoticeDays = 30, noticeDayType = "Calendar" } },
            ["putSchedule"] = new[] { new { putDate = "2027-01-15", exerciseStyle = "European", putPrice = 105m,
                priceBasis = "PercentOfPar", minimumNoticeDays = 30, noticeDayType = "Calendar" } }
        }), null, [("on-coupon", new(2026, 1, 15)), ("mid-period", new(2026, 10, 1))]),
        new("bermudan-quarterly-call", Bond(new()
        {
            ["assetClassTermsVersion"] = 3, ["isCallable"] = true, ["callDate"] = "2026-07-15",
            ["callSchedule"] = new[] { new { callDate = "2026-07-15", callEndDate = "2027-07-15", exerciseStyle = "Bermudan",
                exerciseFrequency = "Quarterly", callPrice = 102m, priceBasis = "PercentOfPar", priceType = "Fixed",
                minimumNoticeDays = 10, maximumNoticeDays = 60, noticeDayType = "Calendar" } }
        }), null, [("on-coupon", new(2026, 1, 15)), ("mid-period", new(2026, 9, 1))]),
        new("call-crossing-lagged-coupon", Bond(new()
        {
            ["assetClassTermsVersion"] = 3, ["isCallable"] = true, ["callDate"] = "2026-07-15",
            ["scheduleConventions"] = Conventions("Level", lag: 2),
            ["callSchedule"] = new[] { new { callDate = "2026-07-15", exerciseStyle = "European", callPrice = 101m,
                priceBasis = "PercentOfPar" } }
        }), null, [("on-coupon", new(2026, 1, 15)), ("mid-period", new(2026, 4, 15))]),
        new("zero-coupon-annual-effective", Bond(new()
        {
            ["couponType"] = "ZeroCoupon", ["couponRate"] = null, ["paymentFrequency"] = "AtMaturity"
        }), BondYieldConvention.AnnualEffective,
            [("issue", new(2026, 1, 15)), ("mid-life", new(2027, 3, 1))]),
    ];

    private static object Conventions(string cashBasis, string? firstRegular = null, int? lag = null)
    {
        var block = new Dictionary<string, object?>
        {
            ["accrualAdjusted"] = false,
            ["couponRateUnit"] = "Percent",
            ["couponCashBasis"] = cashBasis
        };
        if (firstRegular is not null)
            block["firstRegularDate"] = firstRegular;
        if (lag is not null)
        {
            block["paymentLagBusinessDays"] = lag;
            block["paymentCalendars"] = new[] { BusinessDayCalendars.WeekendsOnlyId };
        }
        return block;
    }

    private static SecurityDetailDto Bond(Dictionary<string, object?> overrides)
    {
        var payload = new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = 2,
            ["issueDate"] = "2026-01-15",
            ["maturity"] = "2028-01-15",
            ["couponType"] = "Fixed",
            ["couponRate"] = 5m,
            ["dayCount"] = "30/360",
            ["par"] = 100m,
            ["paymentFrequency"] = "SemiAnnual",
            ["isCallable"] = false,
            ["scheduleConventions"] = Conventions("Level")
        };
        foreach (var item in overrides)
            payload[item.Key] = item.Value;
        var issue = DateOnly.ParseExact((string)payload["issueDate"]!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new(SecurityId, "Bond", SecurityStatusDto.Active, "Golden analytics bond", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(payload), [], [],
            7, new DateTimeOffset(issue.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), null);
    }

    private static JsonObject Render(BondAnalyticsResult result)
    {
        var node = new JsonObject
        {
            ["modelVersion"] = result.ModelVersion,
            ["yieldConvention"] = result.YieldConvention?.ToString(),
            ["cleanPrice"] = Dec(result.CleanPrice),
            ["dirtyPrice"] = Dec(result.DirtyPrice),
            ["accruedInterest"] = Dec(result.AccruedInterest),
            ["yieldToMaturity"] = Dec(result.YieldToMaturity),
            ["currentYield"] = Dec(result.CurrentYield),
            ["macaulayDuration"] = Dec(result.MacaulayDuration),
            ["modifiedDuration"] = Dec(result.ModifiedDuration),
            ["convexity"] = Dec(result.Convexity),
            ["dv01"] = Dec(result.Dv01),
            ["yieldToWorst"] = Dec(result.YieldToWorst),
            ["blockers"] = new JsonArray(result.Blockers.Select(static b => (JsonNode?)JsonValue.Create(b.Code)).ToArray()),
            ["inputKinds"] = new JsonArray(result.Inputs.Select(static i => (JsonNode?)JsonValue.Create(i.Kind)).ToArray()),
            ["cashFlows"] = new JsonArray(result.CashFlows.Select(static flow => (JsonNode?)new JsonObject
            {
                ["flowId"] = flow.FlowId.ToString("D"),
                ["paymentDate"] = Day(flow.PaymentDate),
                ["accrualStart"] = Day(flow.AccrualStart),
                ["accrualEnd"] = Day(flow.AccrualEnd),
                ["interest"] = Dec(flow.Interest),
                ["principal"] = Dec(flow.Principal),
                ["timeInYears"] = Dec(flow.TimeInYears)
            }).ToArray()),
            ["exerciseYields"] = new JsonArray(result.ExerciseYields.Select(static item => (JsonNode?)new JsonObject
            {
                ["kind"] = item.Kind,
                ["exerciseDate"] = Day(item.ExerciseDate),
                ["redemptionPrice"] = Dec(item.RedemptionPrice),
                ["yield"] = Dec(item.Yield),
                ["latestNoticeDate"] = item.LatestNoticeDate is { } latest ? Day(latest) : null,
                ["earliestNoticeDate"] = item.EarliestNoticeDate is { } earliest ? Day(earliest) : null
            }).ToArray())
        };
        return node;
    }

    private static string? Dec(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
    private static string Day(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? LocateFixture()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "fixtures", "fixed-income", "golden", FixtureFileName),
            Path.Combine(FindRepositoryRoot() ?? AppContext.BaseDirectory, "tests", "fixtures", "fixed-income", "golden", FixtureFileName)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    private static Dictionary<string, string> ReadExpected(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        return root["cases"]!.AsObject().ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value!.ToJsonString(Canonical),
            StringComparer.Ordinal);
    }

    private static string? Compare(Dictionary<string, string> expected, List<GoldenCase> received)
    {
        var failures = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in received)
        {
            seen.Add(item.Key);
            var actual = item.Value.ToJsonString(Canonical);
            if (!expected.TryGetValue(item.Key, out var want))
                failures.AppendLine(CultureInfo.InvariantCulture, $"Missing expected case '{item.Key}'.");
            else if (!string.Equals(want, actual, StringComparison.Ordinal))
                failures.AppendLine(CultureInfo.InvariantCulture, $"Case '{item.Key}' differs.{Environment.NewLine}  expected: {want}{Environment.NewLine}  actual:   {actual}");
        }
        foreach (var key in expected.Keys.Where(key => !seen.Contains(key)))
            failures.AppendLine(CultureInfo.InvariantCulture, $"Fixture case '{key}' was not computed.");
        return failures.Length == 0 ? null : failures.ToString();
    }

    private static string WriteReceived(List<GoldenCase> received)
    {
        var cases = new JsonObject();
        foreach (var item in received)
            cases[item.Key] = item.Value;
        var root = new JsonObject
        {
            ["description"] = "Golden bond-analytics-v1 and bond-analytics-v2 results captured before the v1 kernel clones were folded into the shared contractual economics kernel.",
            ["caseCount"] = received.Count,
            ["cases"] = cases
        };
        var path = Path.Combine(AppContext.BaseDirectory, ReceivedFileName);
        File.WriteAllText(path, root.ToJsonString(Pretty) + Environment.NewLine);
        return path;
    }

    private static readonly JsonSerializerOptions Canonical = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed record GoldenCase(string Key, JsonObject Value);

    private sealed record BondShape(
        string Name,
        SecurityDetailDto Security,
        BondYieldConvention? Convention,
        (string Name, DateOnly Date)[] Settlements);
}
