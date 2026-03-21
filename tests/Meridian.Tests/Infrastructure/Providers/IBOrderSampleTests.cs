using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Sample-data fixture tests for Interactive Brokers order messages.
///
/// Each JSON file under Fixtures/InteractiveBrokers/ represents a single IB order
/// as it would be submitted to TWS/Gateway. The test loads every fixture and asserts
/// that the four mandatory fields are present and hold valid values:
/// <list type="bullet">
///   <item><term>orderType</term><description>IB order-type code (e.g. "LMT", "MKT", "STP", "MOC").</description></item>
///   <item><term>side</term><description>Direction of the order: "BUY" or "SELL".</description></item>
///   <item><term>quantity</term><description>Number of shares/contracts — must be a positive integer.</description></item>
///   <item><term>tif</term><description>Time-in-force code (e.g. "DAY", "GTC", "IOC", "FOK").</description></item>
/// </list>
/// </summary>
public sealed class IBOrderSampleTests
{
    // -----------------------------------------------------------------------
    // Valid value sets (mirrors IB TWS API documentation)
    // -----------------------------------------------------------------------

    private static readonly HashSet<string> ValidOrderTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "LMT", "MKT", "STP", "STP LMT", "MOC", "LOC", "MIT",
        "LIT", "REL", "TRAIL", "TRAIL LIMIT", "OCA", "MIDPRICE"
    };

    private static readonly HashSet<string> ValidSides = new(StringComparer.OrdinalIgnoreCase)
    {
        "BUY", "SELL"
    };

    private static readonly HashSet<string> ValidTifs = new(StringComparer.OrdinalIgnoreCase)
    {
        "DAY", "GTC", "IOC", "FOK", "OPG", "GTD", "DTC"
    };

    // -----------------------------------------------------------------------
    // Fixture discovery
    // -----------------------------------------------------------------------

    private static readonly string FixturesDir = ResolveFixturesDir();

    private static string ResolveFixturesDir()
    {
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var candidate = new DirectoryInfo(assemblyDir);

        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "Meridian.sln")))
            candidate = candidate.Parent;

        return Path.Combine(
            candidate?.FullName ?? assemblyDir,
            "tests", "Meridian.Tests",
            "Infrastructure", "Providers", "Fixtures", "InteractiveBrokers");
    }

    public static IEnumerable<object[]> FixtureFiles()
    {
        if (!Directory.Exists(FixturesDir))
            yield break;

        foreach (var file in Directory.GetFiles(FixturesDir, "*.json").OrderBy(f => f))
            yield return new object[] { Path.GetFileName(file) };
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void IBOrderFixture_RequiredFieldsArePresent(string fixtureFileName)
    {
        var root = LoadFixture(fixtureFileName);

        root.TryGetProperty("orderType", out _).Should().BeTrue(
            because: $"[{fixtureFileName}] must contain 'orderType'");
        root.TryGetProperty("side", out _).Should().BeTrue(
            because: $"[{fixtureFileName}] must contain 'side'");
        root.TryGetProperty("quantity", out _).Should().BeTrue(
            because: $"[{fixtureFileName}] must contain 'quantity'");
        root.TryGetProperty("tif", out _).Should().BeTrue(
            because: $"[{fixtureFileName}] must contain 'tif'");
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void IBOrderFixture_OrderTypeIsValid(string fixtureFileName)
    {
        var root = LoadFixture(fixtureFileName);
        var orderType = root.GetProperty("orderType").GetString();

        orderType.Should().NotBeNullOrWhiteSpace(
            because: $"[{fixtureFileName}] orderType must be a non-empty string");
        ValidOrderTypes.Should().Contain(orderType!,
            because: $"[{fixtureFileName}] orderType '{orderType}' is not a recognised IB order type");
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void IBOrderFixture_SideIsValid(string fixtureFileName)
    {
        var root = LoadFixture(fixtureFileName);
        var side = root.GetProperty("side").GetString();

        side.Should().NotBeNullOrWhiteSpace(
            because: $"[{fixtureFileName}] side must be a non-empty string");
        ValidSides.Should().Contain(side!,
            because: $"[{fixtureFileName}] side '{side}' must be 'BUY' or 'SELL'");
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void IBOrderFixture_QuantityIsPositive(string fixtureFileName)
    {
        var root = LoadFixture(fixtureFileName);
        var quantityEl = root.GetProperty("quantity");

        quantityEl.ValueKind.Should().Be(JsonValueKind.Number,
            because: $"[{fixtureFileName}] quantity must be a numeric value");
        quantityEl.GetInt64().Should().BeGreaterThan(0,
            because: $"[{fixtureFileName}] quantity must be a positive integer");
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void IBOrderFixture_TifIsValid(string fixtureFileName)
    {
        var root = LoadFixture(fixtureFileName);
        var tif = root.GetProperty("tif").GetString();

        tif.Should().NotBeNullOrWhiteSpace(
            because: $"[{fixtureFileName}] tif must be a non-empty string");
        ValidTifs.Should().Contain(tif!,
            because: $"[{fixtureFileName}] tif '{tif}' is not a recognised IB time-in-force code");
    }

    [Fact]
    public void FixturesDirectory_ContainsAtLeastOneFile()
    {
        Directory.Exists(FixturesDir).Should().BeTrue(
            because: "the Fixtures/InteractiveBrokers directory must exist");
        Directory.GetFiles(FixturesDir, "*.json").Should().NotBeEmpty(
            because: "at least one IB order fixture must be present");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Parses a fixture file and returns its root <see cref="JsonElement"/>.
    /// The caller must not hold the element beyond the scope of a single test;
    /// the underlying <see cref="JsonDocument"/> is intentionally not disposed here
    /// so callers can read properties inline — xUnit disposes each test instance
    /// after the test completes.
    /// </summary>
    private static JsonElement LoadFixture(string fixtureFileName)
    {
        var json = File.ReadAllText(Path.Combine(FixturesDir, fixtureFileName));
        // JsonDocument.Parse with a string overload copies the data, so the
        // document is independent of the original string.  We parse once per
        // test call; given the tiny fixture size this is negligible.
        return JsonDocument.Parse(json).RootElement;
    }
}
