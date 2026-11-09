using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// ADR-027 stage 3 (S3-B): the decode-arm sweeps the S3-A audit found missing, generated from
/// <see cref="SecurityAssetTermsSchema"/> itself so a field added to the schema is swept without
/// a test edit — per-alias acceptance, per-field missing-Req refusal, per-field type-mismatch
/// refusal, the systematic vocabulary sweep (<c>OneOf</c> enforcing or escaping per the field's
/// declared flags), and the convention sweep across every <c>WithConvention</c> field. Where a
/// field legitimately sits outside a sweep (a structural container, the profile envelope, a
/// tolerant-by-contract spelling), it appears in the committed exclusion table with its reason,
/// and the table itself is pinned against staleness.
/// </summary>
[Trait("Category", "Unit")]
public sealed partial class SecurityAssetTermsSchemaDecodeSweepTests
{
    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────

    private static JsonElement Twin(string assetClass)
        => SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            assetClass, SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);

    private static JsonObject MutableTwin(string assetClass)
        => JsonNode.Parse(Twin(assetClass).GetRawText())!.AsObject();

    private static string ReadBytes(string assetClass, JsonObject terms)
        => new SecurityMasterSnapshotWrapper(
                SecurityMasterMapping.ToRecord(
                    SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(
                        assetClass, JsonDocument.Parse(terms.ToJsonString()).RootElement.Clone())))
            .AssetSpecificTermsJson;

    private static void WriteDecode(string assetClass, JsonObject terms)
        => SecurityMasterMapping.ToCreateCommand(Request(assetClass, JsonDocument.Parse(terms.ToJsonString()).RootElement.Clone()));

    private static string WriteBytes(string assetClass, JsonObject terms)
    {
        var result = SecurityMasterCommandFacade.Create(
            SecurityMasterMapping.ToCreateCommand(Request(assetClass, JsonDocument.Parse(terms.ToJsonString()).RootElement.Clone())));
        result.IsSuccess.Should().BeTrue($"the {assetClass} payload must pass the domain commands for its written form to exist");
        return result.Snapshot!.AssetSpecificTermsJson;
    }

    private static CreateSecurityRequest Request(string assetClass, JsonElement assetSpecificTerms)
    {
        var effectiveFrom = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        return new CreateSecurityRequest(
            SecurityId: Guid.NewGuid(),
            AssetClass: assetClass,
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = $"{assetClass} decode sweep", currency = "USD" }),
            AssetSpecificTerms: assetSpecificTerms,
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, $"SWEEP-{assetClass}", true, effectiveFrom)],
            EffectiveFrom: effectiveFrom,
            SourceSystem: "decode-sweep-tests",
            UpdatedBy: "decode-sweep-tests",
            SourceRecordId: $"{assetClass}-decode-sweep",
            Reason: "SecurityAssetTermsSchema decode sweep");
    }

    /// <summary>Classes whose terms are not a flat field bag, so the per-field sweeps skip them whole.</summary>
    private static readonly IReadOnlyDictionary<string, string> ExcludedClasses =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CustomAsset"] = "profile envelope: fields live inside profileFields and are validated by the profile contract, not the flat schema sweep"
        };

    /// <summary>
    /// Structural container fields every sweep skips: their decode is versioned (ADR-024) or
    /// element-wise, owned by the dedicated schedule/conventions suites rather than scalar rules.
    /// </summary>
    private static readonly IReadOnlySet<string> ContainerKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "callSchedule", "putSchedule", "scheduleConventions", "factorScheduleEntries",
        "principalSchedule", "stepSchedule", "legs", "covenants", "resetSchedule", "notionalSchedule"
    };

    private static IEnumerable<(string AssetClass, SecurityAssetTermField Field)> SweepFields()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            if (ExcludedClasses.ContainsKey(assetClass))
            {
                continue;
            }

            foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
            {
                if (ContainerKeys.Contains(field.Key) || field.Type is SecurityAssetTermFieldType.Array or SecurityAssetTermFieldType.Object)
                {
                    continue;
                }

                yield return (assetClass, field);
            }
        }
    }

    // ── 1. Per-alias acceptance ─────────────────────────────────────────────────────────────────

    public static TheoryData<string, string, string> AliasCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (assetClass, field) in SweepFields())
        {
            foreach (var alias in field.Aliases)
            {
                data.Add(assetClass, field.Key, alias);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AliasCases))]
    public void DeclaredAlias_WritesIdenticallyToTheCanonicalKey(string assetClass, string key, string alias)
    {
        // Aliases are write-side tolerances: the write-mode mapping canonicalizes them, so stored
        // rows carry only canonical spellings and the read decode never needs the alias. The
        // sweep therefore proves the WRITTEN bytes identical, which is the alias's whole contract.
        var canonical = MutableTwin(assetClass);
        if (canonical[key] is null)
        {
            return; // The twin leaves this optional field unset; there is no value to alias.
        }

        var aliased = MutableTwin(assetClass);
        var value = aliased[key]!.DeepClone();
        aliased.Remove(key);
        aliased[alias] = value;

        WriteBytes(assetClass, aliased).Should().Be(WriteBytes(assetClass, canonical),
            $"the schema declares '{alias}' as an accepted spelling of {assetClass}.{key}, so the canonical written form must not differ");
    }

    // ── 2. Per-field missing-Req refusal ────────────────────────────────────────────────────────

    public static TheoryData<string, string> RequiredCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (assetClass, field) in SweepFields())
        {
            if (field.Required)
            {
                data.Add(assetClass, field.Key);
            }
        }

        return data;
    }

    /// <summary>
    /// Required fields whose ABSENCE the codec absorbs with a documented default instead of
    /// refusing: the schema's Required describes the written form (the key is always present in a
    /// stored row), and for these the writer supplies the default when the submission omits it.
    /// Every entry is a divergence the sweep makes measurable; tightening any of them to a
    /// refusal is an S3-D decision, not a silent change.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ToleratedAbsenceDefaults =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Bond.isCallable"] = "false",
            ["Bond.subclass"] = "absent-defaults-to-open-subclass",
            ["Deposit.isCallable"] = "false",
            ["MoneyMarketFund.sweepEligible"] = "false",
            ["MoneyMarketFund.liquidityFeeEligible"] = "false",
            ["CommercialPaper.isAssetBacked"] = "false",
            ["Future.isRollTarget"] = "false",
            ["Option.isAdjusted"] = "false"
        };

    [Theory]
    [MemberData(nameof(RequiredCases))]
    public void MissingRequiredField_RefusesOnWrite_OrFillsItsLedgeredDefault(string assetClass, string key)
    {
        var terms = MutableTwin(assetClass);
        terms.Remove(key);

        if (ToleratedAbsenceDefaults.ContainsKey($"{assetClass}.{key}"))
        {
            // The ledgered tolerance: the write succeeds and the written form still carries the
            // key (with its default), so Required stays true of every stored row.
            var written = WriteBytes(assetClass, terms);
            JsonDocument.Parse(written).RootElement.TryGetProperty(key, out _).Should().BeTrue(
                $"{assetClass}.{key} is required in the written form, so the codec's absence default must still write the key");
            return;
        }

        var decode = () => WriteDecode(assetClass, terms);
        decode.Should().Throw<Exception>($"{assetClass}.{key} is declared required and has no ledgered absence default")
            .Which.Message.Should().ContainEquivalentOf(key,
                "the refusal must name the missing field so the writer can fix the payload");
    }

    // ── 3. Per-field type-mismatch refusal ──────────────────────────────────────────────────────

    private static string? MismatchedValue(SecurityAssetTermFieldType type) => type switch
    {
        SecurityAssetTermFieldType.Decimal => "\"not-a-number\"",
        SecurityAssetTermFieldType.Integer => "\"not-an-integer\"",
        SecurityAssetTermFieldType.Boolean => "\"not-a-boolean\"",
        SecurityAssetTermFieldType.Date => "\"not-a-date\"",
        SecurityAssetTermFieldType.Guid => "\"not-a-guid\"",
        SecurityAssetTermFieldType.String => "12345",
        _ => null
    };

    public static TheoryData<string, string, string> TypeMismatchCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (assetClass, field) in SweepFields())
        {
            if (MismatchedValue(field.Type) is { } bad)
            {
                data.Add(assetClass, field.Key, bad);
            }
        }

        return data;
    }

    /// <summary>
    /// Fields whose mistyped value the write path currently tolerates instead of refusing, each a
    /// measured divergence: the value reads as unset and the write proceeds without it.
    /// Tightening is an S3-D decision.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ToleratedMistypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["StructuredCredit.factorSchedule"] = "legacy free-text trustee-report pointer: a mistyped submission reads as unset (written null) instead of refusing"
        };

    [Theory]
    [MemberData(nameof(TypeMismatchCases))]
    public void MistypedField_RefusesOnWrite_OrDropsPerItsLedgeredTolerance(string assetClass, string key, string badValue)
    {
        var terms = MutableTwin(assetClass);
        terms[key] = JsonNode.Parse(badValue);

        if (ToleratedMistypes.ContainsKey($"{assetClass}.{key}"))
        {
            // The ledgered tolerance, pinned exactly: the mistyped submission reads as unset and
            // the written form carries the key as null — the value is silently discarded rather
            // than refused. Tightening this is an S3-D decision.
            var written = JsonDocument.Parse(WriteBytes(assetClass, terms)).RootElement;
            written.GetProperty(key).ValueKind.Should().Be(JsonValueKind.Null,
                $"{assetClass}.{key}'s ledgered tolerance reads a mistyped value as unset, written as null");
            return;
        }

        var decode = () => WriteDecode(assetClass, terms);
        decode.Should().Throw<Exception>(
            $"{assetClass}.{key} declares a type the substituted value cannot satisfy, and a silent coercion would persist a value the reader drops");
    }

    // ── 4. The vocabulary sweep ─────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> DiscriminantCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (assetClass, field) in SweepFields())
        {
            if (field.IsDiscriminant)
            {
                data.Add(assetClass, field.Key);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DiscriminantCases))]
    public void UndeclaredVocabularyValue_AlwaysRefusesOnWrite_AndReadsPerTheFieldsOwnFlags(string assetClass, string key)
    {
        // The vocabulary contract has two halves, exactly as the schema documents it: a WRITE may
        // only assert declared values (EnsureDeclaredVocabulariesOnWrite enforces every
        // discriminant, verbatim-flagged or not), while a READ of an odd value already in a row
        // loses nothing where the field carries undeclared spellings verbatim.
        var field = SecurityAssetTermsSchema.Fields(assetClass).Single(f => f.Key == key);
        var terms = MutableTwin(assetClass);
        terms[key] = "ZzzUndeclaredSweepValue";

        var decode = () => WriteDecode(assetClass, terms);
        decode.Should().Throw<Exception>(
                $"a write may not assert an undeclared {assetClass}.{key} value whatever the field's read-side flags")
            .Which.Message.Should().ContainEquivalentOf(key, "the refusal must name the field");

        if (field.CarriesUndeclaredValueVerbatim)
        {
            ReadBytes(assetClass, terms).Should().Contain("ZzzUndeclaredSweepValue",
                $"a stored {assetClass}.{key} spelling outside the vocabulary must survive the read round trip verbatim");
        }
    }

    // ── 5. The convention sweep ─────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> ConventionCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (assetClass, field) in SweepFields())
        {
            if (field.Convention != SecurityTermConventionKind.None)
            {
                data.Add(assetClass, field.Key);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ConventionCases))]
    public void UnparseableConventionLabel_RefusesOnWrite(string assetClass, string key)
    {
        var field = SecurityAssetTermsSchema.Fields(assetClass).Single(f => f.Key == key);
        field.SatisfiesConvention("Zzz-Not-A-Convention").Should().BeFalse("the probe value must be outside the convention");

        var terms = MutableTwin(assetClass);
        terms[key] = "Zzz-Not-A-Convention";

        var decode = () => WriteDecode(assetClass, terms);
        decode.Should().Throw<Exception>(
            $"{assetClass}.{key} carries the {field.Convention} convention, and an unparseable label would persist as text the schedule kernel later rejects");
    }

    // ── The exclusion tables stay honest ────────────────────────────────────────────────────────

    [Fact]
    public void ExclusionTables_ReferToRealSchemaEntries()
    {
        foreach (var excluded in ExcludedClasses.Keys)
        {
            SecurityAssetTermsSchema.AssetClasses.Should().Contain(excluded,
                "an exclusion for a class the schema no longer declares is stale and must be removed");
        }

        var allKeys = SecurityAssetTermsSchema.AssetClasses
            .SelectMany(static assetClass => SecurityAssetTermsSchema.Fields(assetClass))
            .Select(static field => field.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var container in ContainerKeys)
        {
            // What matters is that no excluded container quietly leaves the schema while staying
            // excluded; nested-only containers (resetSchedule, notionalSchedule) live inside the
            // legs element declarations rather than as root fields.
            var nestedOnly = container is "resetSchedule" or "notionalSchedule";
            (nestedOnly || allKeys.Contains(container))
                .Should().BeTrue($"the excluded container '{container}' no longer appears in the schema's root fields");
        }
    }
}
