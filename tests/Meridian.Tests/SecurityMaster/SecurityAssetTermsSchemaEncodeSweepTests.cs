using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// ADR-027 stage 3 (S3-C): the encode-arm form rules the S3-A audit found proven only
/// transitively. Asserted from schema metadata per class: every value the writer emits carries
/// its field's declared JSON kind (dates as ISO calendar dates, guids parseable, numbers for
/// decimal and integer fields); a minimal submission (required fields only) writes every
/// required key and invents no optional value; and the written form is idempotent through the
/// codec — re-decoding and re-writing any written document reproduces it byte-for-byte, which is
/// the property S3-D's table-driven extraction must preserve per class. The envelope rules
/// (schemaVersion, termsFormat, assetClassTermsVersion) are swept for minimal payloads too, so
/// they hold for every written row and not only the fully-populated twins.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetTermsSchemaEncodeSweepTests
{
    public static TheoryData<string> SweptAssetClasses()
    {
        var data = new TheoryData<string>();
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            if (assetClass is not "CustomAsset")
            {
                data.Add(assetClass);
            }
        }

        return data;
    }

    private static string WriteFullTwin(string assetClass)
        => SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            assetClass, SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]).GetRawText();

    /// <summary>
    /// Optional fields a minimal submission must keep because a domain rule requires them
    /// conditionally on another field's value — the codec-only rules the S2-B1 ledger names.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> ConditionallyRequiredOptionals =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // The twin is a Collar: cap_floor_cap_strike_required / cap_floor_floor_strike_required.
            ["CapFloor"] = ["capStrike", "floorStrike"]
        };

    /// <summary>
    /// Optional fields the writer fills with a documented default when the submission omits
    /// them, so the written form carries a value the writer did not receive — by declaration,
    /// not invention (e.g. ADR-025's Bond coupon default).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> WrittenOptionalDefaults =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Bond.couponType"] = "\"Fixed\"",
            // The defaulted Fixed structure carries its rate explicitly; an absent coupon is a
            // zero-coupon assertion under ADR-025's documented default, not an unknown.
            ["Bond.couponRate"] = "0"
        };

    private static JsonObject MinimalSubmission(string assetClass)
    {
        // The written full twin minus every optional root field: what a writer that knows only
        // the required contract submits. Container keys declared optional go too; required
        // containers (a swap's legs) stay, as do the ledgered conditionally-required optionals.
        var keep = ConditionallyRequiredOptionals.TryGetValue(assetClass, out var conditional)
            ? conditional
            : [];
        var terms = JsonNode.Parse(WriteFullTwin(assetClass))!.AsObject();
        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
        {
            if (!field.Required && !keep.Contains(field.Key, StringComparer.Ordinal))
            {
                terms.Remove(field.Key);
            }
        }

        return terms;
    }

    private static string WriteBytes(string assetClass, JsonObject terms)
    {
        var effectiveFrom = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var result = SecurityMasterCommandFacade.Create(SecurityMasterMapping.ToCreateCommand(new CreateSecurityRequest(
            SecurityId: Guid.NewGuid(),
            AssetClass: assetClass,
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = $"{assetClass} encode sweep", currency = "USD" }),
            AssetSpecificTerms: JsonDocument.Parse(terms.ToJsonString()).RootElement.Clone(),
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, $"ENCODE-{assetClass}", true, effectiveFrom)],
            EffectiveFrom: effectiveFrom,
            SourceSystem: "encode-sweep-tests",
            UpdatedBy: "encode-sweep-tests",
            SourceRecordId: $"{assetClass}-encode-sweep",
            Reason: "SecurityAssetTermsSchema encode sweep")));
        result.IsSuccess.Should().BeTrue($"the {assetClass} submission must pass the domain commands for its written form to exist");
        return result.Snapshot!.AssetSpecificTermsJson;
    }

    private static bool MatchesDeclaredKind(SecurityAssetTermFieldType type, JsonElement value) => type switch
    {
        SecurityAssetTermFieldType.String => value.ValueKind == JsonValueKind.String,
        SecurityAssetTermFieldType.Decimal or SecurityAssetTermFieldType.Integer => value.ValueKind == JsonValueKind.Number,
        SecurityAssetTermFieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        SecurityAssetTermFieldType.Date => value.ValueKind == JsonValueKind.String
            && (DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _)),
        SecurityAssetTermFieldType.Guid => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _),
        SecurityAssetTermFieldType.Array => value.ValueKind == JsonValueKind.Array,
        SecurityAssetTermFieldType.Object => value.ValueKind == JsonValueKind.Object,
        _ => false
    };

    [Theory]
    [MemberData(nameof(SweptAssetClasses))]
    public void WrittenValues_CarryTheirDeclaredJsonKinds(string assetClass)
    {
        var written = JsonDocument.Parse(WriteFullTwin(assetClass)).RootElement;
        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
        {
            if (!written.TryGetProperty(field.Key, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            MatchesDeclaredKind(field.Type, value).Should().BeTrue(
                $"{assetClass}.{field.Key} declares {field.Type} and the writer emitted {value.GetRawText()}");
        }
    }

    [Theory]
    [MemberData(nameof(SweptAssetClasses))]
    public void MinimalSubmission_WritesEveryRequiredKey_AndInventsNoOptionalValue(string assetClass)
    {
        var written = JsonDocument.Parse(WriteBytes(assetClass, MinimalSubmission(assetClass))).RootElement;

        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
        {
            if (field.Required)
            {
                written.TryGetProperty(field.Key, out var value).Should().BeTrue(
                    $"{assetClass}.{field.Key} is required, so every written row carries the key");
                value.ValueKind.Should().NotBe(JsonValueKind.Undefined);
            }
            else if (written.TryGetProperty(field.Key, out var optional) && optional.ValueKind != JsonValueKind.Null)
            {
                if (ConditionallyRequiredOptionals.TryGetValue(assetClass, out var kept)
                    && kept.Contains(field.Key, StringComparer.Ordinal))
                {
                    continue; // Submitted deliberately: a domain rule conditionally requires it.
                }

                if (WrittenOptionalDefaults.TryGetValue($"{assetClass}.{field.Key}", out var declaredDefault))
                {
                    optional.GetRawText().Should().Be(declaredDefault,
                        $"{assetClass}.{field.Key}'s written default is declared, so it must be exactly that value");
                    continue;
                }

                // A written value for a field the submission never carried would be an invented
                // term — the one exception is a container the frozen fill writes empty.
                (optional.ValueKind == JsonValueKind.Array && optional.GetArrayLength() == 0).Should().BeTrue(
                    $"{assetClass}.{field.Key} was not submitted, so the writer may at most emit an empty frozen container, not {optional.GetRawText()}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(SweptAssetClasses))]
    public void WrittenForm_IsIdempotentThroughTheCodec(string assetClass)
    {
        // The property S3-D must preserve per class: decode-then-rewrite of any written document
        // reproduces it exactly. The round-trip suite pins this for the full twins; the sweep
        // extends it to minimal rows, whose absent and defaulted fields exercise the other half
        // of the writer.
        var firstPass = WriteBytes(assetClass, MinimalSubmission(assetClass));

        var reDecoded = SecurityMasterMapping.ToRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(
                assetClass, JsonDocument.Parse(firstPass).RootElement.Clone()));
        var secondPass = new SecurityMasterSnapshotWrapper(reDecoded).AssetSpecificTermsJson;

        secondPass.Should().Be(firstPass, $"the {assetClass} written form must be a fixed point of the codec");
    }

    [Theory]
    [MemberData(nameof(SweptAssetClasses))]
    public void MinimalWrittenForm_CarriesTheEnvelope(string assetClass)
    {
        var written = JsonDocument.Parse(WriteBytes(assetClass, MinimalSubmission(assetClass))).RootElement;

        written.GetProperty(SecurityTermsEnvelope.FormatKey).GetString().Should().Be(SecurityTermsFormats.Flat,
            "every written row declares its payload family");
        written.TryGetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey, out var stamp).Should().BeTrue(
            "every written row declares the terms version it satisfies");
        stamp.GetInt32().Should().Be(SecurityTermsEnvelope.ResolveAssetClassTermsVersion(written),
            "the stamp must describe the document it sits on");
    }
}
