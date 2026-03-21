using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Canonicalization;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Application.Canonicalization;

/// <summary>
/// Golden fixture tests for <see cref="EventCanonicalizer"/> using curated
/// .json fixture files from the Fixtures/ directory.
///
/// Each fixture file encodes a raw provider event (input) and the expected
/// canonicalized output fields, making regressions immediately visible when
/// condition-codes.json or venue-mapping.json mappings change.
///
/// Related: J8 — Golden Fixture Test Suite (docs/status/IMPROVEMENTS.md).
/// </summary>
public sealed class CanonicalizationGoldenFixtureTests
{
    private readonly EventCanonicalizer _canonicalizer;
    private static readonly string FixturesDir;

    static CanonicalizationGoldenFixtureTests()
    {
        // Resolve the Fixtures directory relative to this test assembly, walking up to the repo root
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var candidate = new DirectoryInfo(assemblyDir);

        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "Meridian.sln")))
            candidate = candidate.Parent;

        FixturesDir = Path.Combine(
            candidate?.FullName ?? assemblyDir,
            "tests", "Meridian.Tests", "Application", "Canonicalization", "Fixtures");
    }

    public CanonicalizationGoldenFixtureTests()
    {
        // Build canonicalizer with production condition-code and venue mappings
        var repoRoot = ResolveRepoRoot();
        var conditionMapper = ConditionCodeMapper.LoadFromFile(
            Path.Combine(repoRoot, "config", "condition-codes.json"));
        var venueMapper = VenueMicMapper.LoadFromFile(
            Path.Combine(repoRoot, "config", "venue-mapping.json"));

        // Registry: resolve known test symbols to themselves; unknown → null
        var registry = Substitute.For<ICanonicalSymbolRegistry>();
        registry.ResolveToCanonical(Arg.Is<string>(s => s == "AAPL" || s == "MSFT" || s == "SPY"))
                .Returns(callInfo => callInfo.Arg<string>());
        registry.ResolveToCanonical(Arg.Is<string>(s => s != "AAPL" && s != "MSFT" && s != "SPY"))
                .Returns((string?)null);

        _canonicalizer = new EventCanonicalizer(registry, conditionMapper, venueMapper);
    }

    public static IEnumerable<object[]> FixtureFiles()
    {
        // Yield all *.json files in the Fixtures directory
        if (!Directory.Exists(FixturesDir))
            yield break;

        foreach (var file in Directory.GetFiles(FixturesDir, "*.json").OrderBy(f => f))
            yield return new object[] { Path.GetFileName(file) };
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void GoldenFixture_CanonicalizeMatchesExpected(string fixtureFileName)
    {
        var fixturePath = Path.Combine(FixturesDir, fixtureFileName);
        var json = File.ReadAllText(fixturePath);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var description = root.TryGetProperty("description", out var desc) ? desc.GetString() : fixtureFileName;
        var rawEl = root.GetProperty("raw");
        var expectedEl = root.GetProperty("expected");

        // Build input MarketEvent from raw fixture fields
        var raw = BuildMarketEventFromFixture(rawEl);

        // Act: canonicalize
        var result = _canonicalizer.Canonicalize(raw);

        // Assert: check each expected field
        if (expectedEl.TryGetProperty("canonicalSymbol", out var expSymbol))
        {
            result.CanonicalSymbol.Should().Be(expSymbol.GetString(),
                because: $"[{fixtureFileName}] {description}: canonicalSymbol mismatch");
        }

        if (expectedEl.TryGetProperty("canonicalVenue", out var expVenue))
        {
            result.CanonicalVenue.Should().Be(expVenue.GetString(),
                because: $"[{fixtureFileName}] {description}: canonicalVenue mismatch");
        }

        if (expectedEl.TryGetProperty("canonicalizationVersion", out var expVersion))
        {
            result.CanonicalizationVersion.Should().Be((byte)expVersion.GetInt32(),
                because: $"[{fixtureFileName}] {description}: canonicalizationVersion mismatch");
        }

        if (expectedEl.TryGetProperty("tier", out var expTier) &&
            Enum.TryParse<MarketEventTier>(expTier.GetString(), out var tier))
        {
            result.Tier.Should().Be(tier,
                because: $"[{fixtureFileName}] {description}: tier mismatch");
        }

        if (expectedEl.TryGetProperty("rawSymbolUnchanged", out var rawSymbolProp) &&
            rawSymbolProp.GetBoolean())
        {
            result.Symbol.Should().Be(raw.Symbol,
                because: $"[{fixtureFileName}] {description}: raw Symbol must never be mutated during canonicalization");
        }

        if (expectedEl.TryGetProperty("canonicalConditions", out var expConditionsEl) &&
            expConditionsEl.ValueKind == JsonValueKind.Array)
        {
            var expectedConditions = expConditionsEl.EnumerateArray()
                .Select(c => Enum.Parse<CanonicalTradeCondition>(c.GetString()!))
                .ToArray();
            var tradePaylod = result.Payload as Trade;
            tradePaylod.Should().NotBeNull(
                because: $"[{fixtureFileName}] {description}: expected Trade payload for canonicalConditions assertion");
            tradePaylod!.CanonicalConditions.Should().BeEquivalentTo(expectedConditions,
                because: $"[{fixtureFileName}] {description}: canonicalConditions mismatch");
        }
    }

    /// <summary>
    /// Builds a raw (pre-canonicalization) <see cref="MarketEvent"/> from the "raw" object in a fixture file.
    /// Supports minimal required fields (symbol, source, venue, price, size, sequenceNumber, conditions).
    /// </summary>
    private static MarketEvent BuildMarketEventFromFixture(JsonElement raw)
    {
        var symbol = raw.GetProperty("symbol").GetString()!;
        var source = raw.GetProperty("source").GetString()!;
        var venue = raw.TryGetProperty("venue", out var v) ? v.GetString() : null;
        var price = raw.TryGetProperty("price", out var p) ? p.GetDecimal() : 100m;
        var size = raw.TryGetProperty("size", out var s) ? s.GetInt64() : 100L;
        var seq = raw.TryGetProperty("sequenceNumber", out var sn) ? sn.GetInt64() : 1L;

        string[]? rawConditions = null;
        if (raw.TryGetProperty("conditions", out var condEl) && condEl.ValueKind == JsonValueKind.Array)
        {
            rawConditions = condEl.EnumerateArray()
                .Select(c => c.GetString())
                .Where(c => c is not null)
                .Select(c => c!)
                .ToArray();
            if (rawConditions.Length == 0)
                rawConditions = null;
        }

        var trade = new Trade(
            Timestamp: new DateTimeOffset(2026, 2, 25, 14, 30, 0, TimeSpan.Zero),
            Symbol: symbol,
            Price: price,
            Size: size,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: seq,
            StreamId: source,
            Venue: venue,
            RawConditions: rawConditions);

        return new MarketEvent(
            Timestamp: new DateTimeOffset(2026, 2, 25, 14, 30, 0, TimeSpan.Zero),
            Symbol: symbol,
            Type: MarketEventType.Trade,
            Payload: trade,
            Sequence: seq,
            Source: source,
            Tier: MarketEventTier.Raw,
            CanonicalizationVersion: 0);
    }

    private static string ResolveRepoRoot()
    {
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var candidate = new DirectoryInfo(assemblyDir);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "Meridian.sln")))
            candidate = candidate.Parent;
        return candidate?.FullName ?? assemblyDir;
    }
}
