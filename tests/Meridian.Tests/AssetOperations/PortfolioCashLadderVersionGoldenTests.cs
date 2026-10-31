using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Curves;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.FixedIncome;
using Meridian.Tests.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

/// <summary>
/// Golden grid for <c>portfolio-cash-ladder-v1</c> and <c>portfolio-cash-ladder-v2</c> output. It is not
/// a defect pin: it freezes both retained engines so that explicit v1 replay through the shared
/// cash-ladder query stays bit-identical, and so a later v1 retirement can prove what it removes.
/// </summary>
/// <remarks>
/// <para>The grid is two engines x three bond shapes (fixed bullet, European call and put, SOFR
/// floater with an observed reset) x six scenarios (base, early call, holder put, rates up,
/// redemption wave, FX adverse): 36 cases. Decimals are stored as invariant strings; generation
/// timestamps are excluded.</para>
/// <para>A missing or mismatched fixture writes <c>portfolio-cash-ladder-versions.received.json</c>
/// beside the test binaries and fails. It never skips. Replace the committed fixture under
/// tests/fixtures/fixed-income/golden/ only when the change in governed output is intended.</para>
/// </remarks>
public sealed class PortfolioCashLadderVersionGoldenTests
{
    private const string FixtureFileName = "portfolio-cash-ladder-versions.json";
    private const string ReceivedFileName = "portfolio-cash-ladder-versions.received.json";
    private const int ExpectedCaseCount = 36;
    private static readonly DateTimeOffset AsOf = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid FixedId = Guid.Parse("7a1c0d2e-0000-4000-8000-00000000b1b1");
    private static readonly Guid CallableId = Guid.Parse("7a1c0d2e-0000-4000-8000-00000000b2b2");
    private static readonly Guid FloatingId = Guid.Parse("7a1c0d2e-0000-4000-8000-00000000b3b3");

    private static readonly string[] Scenarios =
    [
        PortfolioCashLadderEngine.BaseScenarioId,
        PortfolioCashLadderEngine.EarlyCallScenarioId,
        PortfolioCashLadderEngine.HolderPutScenarioId,
        PortfolioCashLadderEngine.RatesUpScenarioId,
        PortfolioCashLadderEngine.RedemptionWaveScenarioId,
        PortfolioCashLadderEngine.FxAdverseScenarioId,
    ];

    [Fact]
    public async Task LadderEngines_MatchCommittedGoldenGrid()
    {
        var received = await ComputeGridAsync();
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

    private static async Task<List<GoldenCase>> ComputeGridAsync()
    {
        var cases = new List<GoldenCase>();
        foreach (var (name, security) in Shapes())
        {
            var projection = (await CashFlows(security).GetProjectionAsync(security.SecurityId,
                StructuredCashFlowScenario.Base, new BondCalculationContext(AsOf)))!;
            var inputs = BondConsistencyTests.Inputs(security, projection);
            foreach (var scenario in Scenarios)
            {
                cases.Add(new($"portfolio-cash-ladder-v1|{name}|{scenario}", Render(LegacyPortfolioCashLadderEngine.Build(inputs, scenario))));
                cases.Add(new($"portfolio-cash-ladder-v2|{name}|{scenario}", Render(PortfolioCashLadderEngine.Build(inputs, scenario))));
            }
        }
        return cases;
    }

    private static IEnumerable<(string Name, SecurityDetailDto Security)> Shapes()
    {
        yield return ("fixed-bullet", Bond(FixedId, new()));
        yield return ("european-call-put", Bond(CallableId, new()
        {
            ["isCallable"] = true,
            ["callSchedule"] = new[] { new { callDate = "2026-07-01", exerciseStyle = "European", callPrice = 101m, priceBasis = "PercentOfPar", priceType = "Fixed" } },
            ["putSchedule"] = new[] { new { putDate = "2026-07-01", exerciseStyle = "European", putPrice = 105m, priceBasis = "PercentOfPar" } }
        }));
        yield return ("sofr-floater-observed-reset", Bond(FloatingId, new()
        {
            ["couponType"] = "Floating",
            ["floatingIndex"] = "SOFR",
            ["spreadBps"] = 0m,
            ["currentIndexRate"] = 4m,
            ["currentIndexResetDate"] = "2026-01-01",
            ["currentIndexRateSource"] = "fixing:SOFR:2026-01-01"
        }));
    }

    private static SecurityDetailDto Bond(Guid securityId, Dictionary<string, object?> overrides)
    {
        var payload = new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = 3,
            ["issueDate"] = "2026-01-01",
            ["maturity"] = "2027-01-01",
            ["couponType"] = "Fixed",
            ["couponRate"] = 5m,
            ["dayCount"] = "30/360",
            ["par"] = 100m,
            ["paymentFrequency"] = "SemiAnnual",
            ["isCallable"] = false,
            ["scheduleConventions"] = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level" }
        };
        foreach (var item in overrides)
            payload[item.Key] = item.Value;
        if (payload.TryGetValue("callSchedule", out var calls))
        {
            var rows = JsonSerializer.SerializeToElement(calls);
            if (rows.GetArrayLength() > 0 && !payload.ContainsKey("callDate"))
                payload["callDate"] = rows[0].GetProperty("callDate").GetString();
        }
        return new(securityId, "Bond", SecurityStatusDto.Active, "Golden ladder bond", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(payload), [], [], 1, AsOf, null);
    }

    private static SecurityMasterCashFlowService CashFlows(SecurityDetailDto security)
    {
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityCashFlowSourceDto(security.SecurityId,
            StructuredCashFlowSourceKind.CalculatedBullet, security.EffectiveFrom, false, null, null));
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var curves = new InMemorySecurityForwardCurveProvider();
        curves.Publish(new("SOFR", new(2026, 1, 1), [new(new(2026, 1, 1), 0.04m), new(new(2027, 1, 1), 0.04m)]));
        return new(store, [], query, NullLogger<SecurityMasterCashFlowService>.Instance, forwardCurves: curves);
    }

    private static JsonObject Render(PortfolioCashLadderDto ladder) => new()
    {
        ["engineVersion"] = ladder.EngineVersion,
        ["scenarioId"] = ladder.ScenarioId,
        ["asOfDate"] = Day(ladder.AsOfDate),
        ["horizonDays"] = ladder.HorizonDays,
        ["baseCurrency"] = ladder.BaseCurrency,
        ["openingCash"] = Dec(ladder.OpeningCash),
        ["minimumCashThreshold"] = Dec(ladder.MinimumCashThreshold),
        ["securitiesEvaluated"] = ladder.SecuritiesEvaluated,
        ["securitiesWithFlows"] = ladder.SecuritiesWithFlows,
        ["isDecisionReady"] = ladder.IsDecisionReady,
        ["blockingReasons"] = Strings(ladder.BlockingReasons),
        ["assumptions"] = Strings(ladder.Assumptions),
        ["warnings"] = Strings(ladder.Warnings),
        ["availableScenarios"] = Strings(ladder.AvailableScenarios.Select(static item => item.ScenarioId)),
        ["buckets"] = new JsonArray(ladder.Buckets.Select(static bucket => (JsonNode?)new JsonObject
        {
            ["start"] = Day(bucket.BucketStart),
            ["end"] = Day(bucket.BucketEnd),
            ["label"] = bucket.Label,
            ["inflows"] = Dec(bucket.Inflows),
            ["outflows"] = Dec(bucket.Outflows),
            ["netFlow"] = Dec(bucket.NetFlow),
            ["cumulativeCash"] = Dec(bucket.CumulativeCash),
            ["breaches"] = bucket.BreachesMinimumBalance,
            ["sources"] = new JsonArray(bucket.Sources.Select(static slice => (JsonNode?)new JsonObject
            {
                ["source"] = slice.Source, ["amount"] = Dec(slice.Amount), ["flowCount"] = slice.FlowCount
            }).ToArray())
        }).ToArray()),
        ["contributions"] = new JsonArray(ladder.Contributions.Select(static row => (JsonNode?)new JsonObject
        {
            ["securityId"] = row.SecurityId.ToString("D"),
            ["assetClass"] = row.AssetClass,
            ["projectionEngineVersion"] = row.ProjectionEngineVersion,
            ["sourceDomain"] = row.SourceDomain,
            ["flowType"] = row.FlowType,
            ["sourceLane"] = row.SourceLane,
            ["dueDate"] = Day(row.DueDate),
            ["amount"] = Dec(row.Amount),
            ["currency"] = row.Currency,
            ["baseAmount"] = Dec(row.BaseAmount),
            ["scenarioAdjustment"] = row.ScenarioAdjustment
        }).ToArray())
    };

    private static JsonArray Strings(IEnumerable<string> values)
        => new(values.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray());
    private static string Dec(decimal value) => value.ToString(CultureInfo.InvariantCulture);
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
            ["description"] = "Golden portfolio-cash-ladder-v1 and portfolio-cash-ladder-v2 ladders captured when explicit v1 replay dispatch was added to the shared cash-ladder query.",
            ["caseCount"] = received.Count,
            ["cases"] = cases
        };
        var path = Path.Combine(AppContext.BaseDirectory, ReceivedFileName);
        File.WriteAllText(path, root.ToJsonString(Pretty) + Environment.NewLine);
        return path;
    }

    private static readonly JsonSerializerOptions Canonical = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private sealed record GoldenCase(string Key, JsonObject Value);
}
