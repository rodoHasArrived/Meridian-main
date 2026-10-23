using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Golden grid for <see cref="FaceValueLot.ConstantYieldAmortizedBasisAsOf"/>, captured at base
/// 7d072638 before the schedule kernel (ADR-025) takes over <see cref="FaceValueLot"/>'s coupon-date
/// generators. It is not a defect pin: it freezes governed calculation output so the delegation in
/// increment 12 can prove itself bit-identical.
/// </summary>
/// <remarks>
/// <para>The grid is payments per year {1, 2, 4, 12} x conventions {Thirty360, ActualActualIcma,
/// ThirtyE360Isda, Actual365} x three acquisition shapes x clean prices {98.5, 100, 101.25} x four
/// as-of dates (acquisition + 1 day, the first coupon date, mid-life, maturity - 1 day): 576 values.
/// Each value is stored as its invariant decimal string, so equality is exact in both value and
/// scale; a call that throws is stored as <c>throws:&lt;ExceptionType&gt;</c>.</para>
/// <para>A missing or mismatched fixture writes <c>face-value-lot-constant-yield.received.json</c>
/// beside the test binaries and fails. It never skips. Only replace the committed fixture with the
/// received file when the change in governed output is intended and approved.</para>
/// </remarks>
public sealed class FaceValueLotGoldenTests
{
    private const string FixtureFileName = "face-value-lot-constant-yield.json";
    private const string ReceivedFileName = "face-value-lot-constant-yield.received.json";
    private const int ExpectedCaseCount = 576;
    private const decimal OriginalFace = 1_000_000m;
    private const decimal CouponRatePercent = 5m;

    private static readonly Guid SecurityId = Guid.Parse("5c4ed0f0-0000-4000-8000-0000000000f0");

    private static readonly int[] PaymentsPerYear = [1, 2, 4, 12];

    private static readonly DayCountConvention[] Conventions =
    [
        DayCountConvention.Thirty360,
        DayCountConvention.ActualActualIcma,
        DayCountConvention.ThirtyE360Isda,
        DayCountConvention.Actual365,
    ];

    private static readonly decimal[] Prices = [98.5m, 100m, 101.25m];

    private static readonly AcquisitionShape[] Shapes =
    [
        // Acquired on a coupon date of every frequency's schedule (day 15 never clips).
        new("on-coupon", new DateOnly(2026, 6, 15), new DateOnly(2031, 6, 15)),
        // Acquired inside a coupon period for every frequency: an odd (short) first period.
        new("mid-period-odd-first", new DateOnly(2026, 9, 3), new DateOnly(2031, 6, 15)),
        // Month-end maturity on a leap day: the schedule runs backward on month ends.
        new("month-end-maturity", new DateOnly(2025, 10, 20), new DateOnly(2028, 2, 29)),
    ];

    [Fact]
    public void ConstantYieldAmortizedBasisAsOf_MatchesCommittedGoldenGrid()
    {
        var received = ComputeGrid();
        Assert.Equal(ExpectedCaseCount, received.Count);

        var fixturePath = LocateFixture();
        var failure = fixturePath is null
            ? $"The golden fixture '{FixtureFileName}' was not found beside the test binaries or under tests/fixtures/schedules/golden/."
            : Compare(ReadExpected(fixturePath), received);
        if (failure is null)
        {
            return;
        }

        var receivedPath = WriteReceived(received);
        Assert.Fail(
            $"{failure}{Environment.NewLine}The computed grid was written to '{receivedPath}'. " +
            "Replace tests/fixtures/schedules/golden/" + FixtureFileName + " with it only when the change in governed output is intended.");
    }

    private static List<GoldenCase> ComputeGrid()
    {
        var cases = new List<GoldenCase>(ExpectedCaseCount);
        foreach (var paymentsPerYear in PaymentsPerYear)
        {
            var monthsPerPeriod = 12 / paymentsPerYear;
            foreach (var convention in Conventions)
            {
                foreach (var shape in Shapes)
                {
                    var firstCoupon = FirstCouponAfter(shape.Acquired, shape.Maturity, monthsPerPeriod);
                    var asOfDates = new (string Label, DateOnly Date)[]
                    {
                        ("acquired+1d", shape.Acquired.AddDays(1)),
                        ("first-coupon", firstCoupon),
                        ("mid-life", shape.Acquired.AddDays((shape.Maturity.DayNumber - shape.Acquired.DayNumber) / 2)),
                        ("maturity-1d", shape.Maturity.AddDays(-1)),
                    };
                    foreach (var price in Prices)
                    {
                        var lot = new FaceValueLot(
                            $"golden-{shape.Name}", SecurityId, shape.Acquired, OriginalFace, price);
                        foreach (var (label, asOf) in asOfDates)
                        {
                            var id = string.Create(
                                CultureInfo.InvariantCulture,
                                $"ppy{paymentsPerYear}/{convention}/{shape.Name}/{price}/{label}");
                            cases.Add(new GoldenCase(
                                id,
                                paymentsPerYear,
                                convention.ToString(),
                                shape.Name,
                                shape.Acquired,
                                shape.Maturity,
                                price,
                                asOf,
                                Evaluate(lot, convention, shape.Maturity, asOf, paymentsPerYear)));
                        }
                    }
                }
            }
        }

        return cases;
    }

    private static string Evaluate(
        FaceValueLot lot,
        DayCountConvention convention,
        DateOnly maturity,
        DateOnly asOf,
        int paymentsPerYear)
    {
        try
        {
            return lot.ConstantYieldAmortizedBasisAsOf(convention, maturity, asOf, CouponRatePercent, paymentsPerYear)
                .ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            return "throws:" + ex.GetType().Name;
        }
    }

    /// <summary>
    /// The first coupon date after <paramref name="acquired"/> on the schedule run backward from
    /// <paramref name="maturity"/>, keeping month ends when maturity is a month end. It only chooses
    /// an as-of input; the golden values come from <see cref="FaceValueLot"/> itself.
    /// </summary>
    private static DateOnly FirstCouponAfter(DateOnly acquired, DateOnly maturity, int monthsPerPeriod)
    {
        var endOfMonth = maturity.Day == DateTime.DaysInMonth(maturity.Year, maturity.Month);
        var first = maturity;
        for (var k = 1; ; k++)
        {
            var candidate = maturity.AddMonths(-k * monthsPerPeriod);
            if (endOfMonth)
            {
                candidate = new DateOnly(candidate.Year, candidate.Month, DateTime.DaysInMonth(candidate.Year, candidate.Month));
            }

            if (candidate <= acquired)
            {
                return first;
            }

            first = candidate;
        }
    }

    private static string? Compare(IReadOnlyDictionary<string, string> expected, IReadOnlyList<GoldenCase> received)
    {
        var problems = new List<string>();
        foreach (var golden in received)
        {
            if (!expected.TryGetValue(golden.Id, out var value))
            {
                problems.Add($"missing case '{golden.Id}'");
            }
            else if (!string.Equals(value, golden.Basis, StringComparison.Ordinal))
            {
                problems.Add($"'{golden.Id}': expected {value}, computed {golden.Basis}");
            }
        }

        var receivedIds = received.Select(static golden => golden.Id).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(expected.Keys
            .Where(id => !receivedIds.Contains(id))
            .Select(static id => $"unexpected fixture case '{id}'"));

        if (problems.Count == 0)
        {
            return null;
        }

        var report = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"{problems.Count} golden case(s) differ from {FixtureFileName}:");
        foreach (var problem in problems.Take(20))
        {
            report.AppendLine().Append("  ").Append(problem);
        }

        return report.ToString();
    }

    private static Dictionary<string, string> ReadExpected(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"Golden fixture '{path}' is empty.");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in root["cases"]?.AsArray() ?? new JsonArray())
        {
            var id = node?["id"]?.GetValue<string>();
            var basis = node?["basis"]?.GetValue<string>();
            if (id is not null && basis is not null)
            {
                expected[id] = basis;
            }
        }

        return expected;
    }

    private static string WriteReceived(IReadOnlyList<GoldenCase> received)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "schedules", "golden");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ReceivedFileName);
        var document = new JsonObject
        {
            ["description"] = "FaceValueLot.ConstantYieldAmortizedBasisAsOf golden grid, captured at base 7d072638 "
                + "before the schedule kernel (ADR-025) takes over FaceValueLot's coupon-date generators. "
                + "Regenerate only through FaceValueLotGoldenTests, which writes the .received.json file.",
            ["originalFace"] = OriginalFace.ToString(CultureInfo.InvariantCulture),
            ["couponRatePercent"] = CouponRatePercent.ToString(CultureInfo.InvariantCulture),
            ["parBasis"] = "100",
            ["caseCount"] = received.Count,
            ["cases"] = new JsonArray(received.Select(static golden => (JsonNode)new JsonObject
            {
                ["id"] = golden.Id,
                ["paymentsPerYear"] = golden.PaymentsPerYear,
                ["convention"] = golden.Convention,
                ["shape"] = golden.Shape,
                ["acquired"] = golden.Acquired.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["maturity"] = golden.Maturity.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["price"] = golden.Price.ToString(CultureInfo.InvariantCulture),
                ["asOf"] = golden.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["basis"] = golden.Basis,
            }).ToArray()),
        };
        File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n");
        return path;
    }

    /// <summary>
    /// Prefers the fixture copied beside the test binaries (csproj None item with
    /// CopyToOutputDirectory), falling back to a repository walk for ad hoc runs.
    /// </summary>
    private static string? LocateFixture()
    {
        var copied = Path.Combine(AppContext.BaseDirectory, "fixtures", "schedules", "golden", FixtureFileName);
        if (File.Exists(copied))
        {
            return copied;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "schedules", "golden", FixtureFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed record AcquisitionShape(string Name, DateOnly Acquired, DateOnly Maturity);

    private sealed record GoldenCase(
        string Id,
        int PaymentsPerYear,
        string Convention,
        string Shape,
        DateOnly Acquired,
        DateOnly Maturity,
        decimal Price,
        DateOnly AsOf,
        string Basis);
}
