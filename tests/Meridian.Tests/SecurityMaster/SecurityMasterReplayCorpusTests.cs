using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// ADR-027 stage-2 replay-compatibility corpus (the precondition the decision item names): a
/// committed set of stored-event-shaped economic definition records, each replayed through the
/// three paths stage 2 must not disturb, with the resulting bytes pinned in a committed golden —
/// <list type="number">
/// <item><b>rebuild</b> — <see cref="SecurityEconomicDefinitionAdapter.ToProjection"/>, the
/// event-replay projection rebuild;</item>
/// <item><b>nextEconomic</b> — <see cref="SecurityEconomicDefinitionAdapter.ToEconomicRecord"/>
/// of the rebuilt projection, the next event the write path would emit (retained fold
/// included);</item>
/// <item><b>amendCycle</b> — the domain-codec round trip an amend takes today
/// (<c>ToRecord</c> → snapshot → <c>ToProjection</c>) with the retained carry re-applied, which
/// is exactly the seam stage 2 deletes.</item>
/// </list>
/// A path that refuses pins its refusal message instead, so narrowing or widening a refusal is a
/// golden change too. The corpus inputs are committed literals and never regenerate from the
/// serializer: any serializer or bridge change surfaces as a golden diff that must be reviewed
/// and approved, which is the ADR's "byte-identical before and after the command-path move"
/// proof obligation made mechanical. Extend the corpus with new cases as stage-2 moves land;
/// never edit a committed case in place.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterReplayCorpusTests
{
    private const string CorpusFileName = "replay-corpus.json";
    private const string GoldenFileName = "replay-corpus-golden.json";
    private const string ReceivedFileName = "replay-corpus-golden.received.json";

    private static readonly JsonSerializerOptions FixtureOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [Fact]
    public void EveryCorpusCase_ReplaysToItsCommittedBytes()
    {
        var corpus = LoadCorpus();
        var received = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var replayCase in corpus)
        {
            ComputeCase(replayCase, received);
        }

        var goldenPath = LocateFixture(GoldenFileName);
        string? failure;
        if (goldenPath is null)
        {
            failure = $"The golden fixture '{GoldenFileName}' was not found beside the test binaries or under tests/fixtures/security-master/replay-corpus/.";
        }
        else
        {
            var expected = JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(goldenPath))!;
            failure = Compare(expected, received);
        }

        if (failure is null)
        {
            return;
        }

        var receivedPath = Path.Combine(AppContext.BaseDirectory, ReceivedFileName);
        File.WriteAllText(receivedPath, JsonSerializer.Serialize(received, FixtureOptions) + "\n", new UTF8Encoding(false));
        Assert.Fail(
            $"{failure}{Environment.NewLine}The computed output was written to '{receivedPath}'. Replace " +
            "tests/fixtures/security-master/replay-corpus/" + GoldenFileName + " with it only when the replay change is intended and approved " +
            "(ADR-027 stage 2 requires rebuilt state to stay byte-identical across command-path moves).");
    }

    private static void ComputeCase(JsonElement replayCase, SortedDictionary<string, string> received)
    {
        var name = replayCase.GetProperty("name").GetString()!;
        var record = BuildRecord(replayCase);
        var expectRefusal = replayCase.TryGetProperty("expectRefusal", out var refusal) && refusal.ValueKind == JsonValueKind.True;

        SecurityProjectionRecord? rebuilt = null;
        try
        {
            rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(record);
            received[$"{name}.rebuild"] = rebuilt.AssetSpecificTerms.GetRawText();
        }
        catch (InvalidOperationException exception)
        {
            received[$"{name}.rebuild"] = Refused(exception);
        }

        if (expectRefusal || rebuilt is null)
        {
            return;
        }

        try
        {
            received[$"{name}.nextEconomic"] = SecurityEconomicDefinitionAdapter.ToEconomicRecord(rebuilt).EconomicTerms.GetRawText();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            received[$"{name}.nextEconomic"] = Refused(exception);
        }

        try
        {
            // The per-write round trip the commands take today, and the seam stage 2 deletes: the
            // domain codec re-serializes only declared flat keys, then the retained carry restores
            // what the codec cannot hold. Pinning its bytes proves the move changed nothing.
            var amended = SecurityMasterMapping.ToProjection(
                new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(rebuilt)));
            var carried = SecurityEconomicDefinitionAdapter.CarryRetainedEconomicTerms(rebuilt, amended);
            received[$"{name}.amendCycle"] = carried.AssetSpecificTerms.GetRawText();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            received[$"{name}.amendCycle"] = Refused(exception);
        }
    }

    private static string Refused(Exception exception)
        => $"<refused: {exception.Message}>";

    private static SecurityEconomicDefinitionRecord BuildRecord(JsonElement replayCase)
    {
        var name = replayCase.GetProperty("name").GetString()!;
        var assetClass = replayCase.GetProperty("assetClass").GetString()!;
        var typeName = replayCase.GetProperty("typeName").GetString()!;
        var legacyAssetClass = replayCase.GetProperty("legacyAssetClass").GetString()!;
        var economicTerms = replayCase.GetProperty("economicTerms").Clone();
        JsonElement? legacyTerms = replayCase.TryGetProperty("legacyAssetSpecificTerms", out var legacy)
            && legacy.ValueKind != JsonValueKind.Null
                ? legacy.Clone()
                : null;

        var asOf = DateTimeOffset.UnixEpoch;
        return new SecurityEconomicDefinitionRecord(
            SecurityId: DeterministicGuid(name),
            AssetClass: assetClass,
            AssetFamily: null,
            SubType: typeName,
            TypeName: typeName,
            IssuerType: null,
            RiskCountry: null,
            Status: SecurityStatusDto.Active,
            DisplayName: $"Corpus {name}",
            Currency: "USD",
            Classification: Json("{}"),
            CommonTerms: Json($$"""{"displayName":"Corpus {{name}}","currency":"USD"}"""),
            EconomicTerms: economicTerms,
            Provenance: Json("""{"sourceSystem":"replay-corpus","updatedBy":"replay-corpus","asOf":"1970-01-01T00:00:00+00:00"}"""),
            Version: 1,
            EffectiveFrom: asOf,
            EffectiveTo: null,
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, $"CORPUS-{name}", true, asOf)],
            LegacyAssetClass: legacyAssetClass,
            LegacyAssetSpecificTerms: legacyTerms);
    }

    private static Guid DeterministicGuid(string name)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string? Compare(SortedDictionary<string, string> expected, SortedDictionary<string, string> received)
    {
        var missing = expected.Keys.Except(received.Keys, StringComparer.Ordinal).ToArray();
        var surplus = received.Keys.Except(expected.Keys, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || surplus.Length > 0)
        {
            return $"Corpus entry set changed. Missing from this run: [{string.Join(", ", missing)}]. New in this run: [{string.Join(", ", surplus)}].";
        }

        var differing = expected
            .Where(entry => !string.Equals(entry.Value, received[entry.Key], StringComparison.Ordinal))
            .Select(static entry => entry.Key)
            .ToArray();
        return differing.Length > 0
            ? $"Replay bytes changed for: [{string.Join(", ", differing)}]."
            : null;
    }

    public static TheoryData<string> DeclaredAssetClasses() => SecurityAssetTermsSchemaRoundTripTests.DeclaredAssetClasses();

    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void IdentityGate_StripsRetainedExactlyWhereTheCandidateRebuildsTheSource(string assetClass)
    {
        // ADR-027 stage 1 increment 6: economic-only replay sheds retainedEconomicTerms when the
        // flattened candidate rebuilds the source document byte-for-byte, and keeps it (with the
        // fold-on-read) everywhere else — including every class whose replay refuses. The gate's
        // verdict is derived here rather than hard-coded, so a class whose codec gains or loses
        // losslessness shows up as a corpus/golden diff, never as a silent flip.
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            assetClass, SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, original));

        SecurityProjectionRecord recovered;
        try
        {
            recovered = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
        }
        catch (InvalidOperationException)
        {
            return; // Refusing classes (Equity) keep their refusal; the corpus pins the message.
        }

        var carriesRetained = SecurityTermsEnvelope.TryGetRetainedEconomicTerms(recovered.AssetSpecificTerms, out var retained);
        if (!carriesRetained)
        {
            // Stripped: the rebuilt next event must reproduce the source exactly — the flat row
            // holds the whole document, which is what ending the carry means.
            SecurityEconomicDefinitionAdapter.ToEconomicRecord(recovered).EconomicTerms.GetRawText()
                .Should().Be(economic.EconomicTerms.GetRawText(),
                    $"a {assetClass} replay without the carry must lose nothing");
        }
        else
        {
            retained.GetRawText().Should().Be(economic.EconomicTerms.GetRawText(),
                $"a {assetClass} replay that keeps the carry must keep the source verbatim");
        }
    }

    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void RecordAwareDerivation_IsByteIdenticalToTheProjectionDecode(string assetClass)
    {
        // ADR-027 stage 2 (S2-B3): the command services derive the economic record from the typed
        // domain record when no profile-backed override applied, skipping the encode → re-decode
        // seam. The two derivations must agree byte-for-byte on every declared class, or the seam
        // removal changed an event payload.
        var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(
            assetClass,
            SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
                assetClass,
                SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]));
        var record = SecurityMasterMapping.ToRecord(projection);

        SecurityEconomicDefinitionAdapter.ToEconomicRecord(record, projection).EconomicTerms.GetRawText()
            .Should().Be(
                SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection).EconomicTerms.GetRawText(),
                $"the record-aware route must emit the same {assetClass} economic bytes as the projection decode");
    }

    private static IReadOnlyList<JsonElement> LoadCorpus()
    {
        var corpusPath = LocateFixture(CorpusFileName);
        corpusPath.Should().NotBeNull($"the corpus fixture '{CorpusFileName}' must be committed under tests/fixtures/security-master/replay-corpus/");
        using var document = JsonDocument.Parse(File.ReadAllText(corpusPath!));
        var cases = document.RootElement.GetProperty("cases");
        cases.GetArrayLength().Should().BeGreaterThan(0, "an empty corpus proves nothing");
        return cases.EnumerateArray().Select(static replayCase => replayCase.Clone()).ToArray();
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static string? LocateFixture(string fileName)
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "fixtures", "security-master", "replay-corpus", fileName);
        if (File.Exists(beside))
        {
            return beside;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "security-master", "replay-corpus", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
