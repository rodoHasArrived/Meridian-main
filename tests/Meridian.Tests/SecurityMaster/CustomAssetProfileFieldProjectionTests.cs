using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The decode half of the profile-keyed CustomAsset projection (migration 064). Building the plan
/// is pure, so the golden rows are pinned without a database; the Postgres fan-out tests prove the
/// same rows land in the table.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CustomAssetProfileFieldProjectionTests
{
    /// <summary>A governed envelope carrying every JSON value kind a profile field can hold.</summary>
    internal const string EnvelopeJson =
        """
        {
          "customProfileId": "specialized-holding",
          "profileVersion": 3,
          "profileFields": {
            "vintage": "2019",
            "appraisalDate": "2026-06-30",
            "appraisedValue": 1250000.50,
            "ownershipPct": 0.125000000000000000000000000001,
            "isInsured": true,
            "custodian": null,
            "location": { "city": "Geneva", "vault": 7 },
            "tags": ["art", "fine-art"]
          },
          "profileApproval": { "approvedBy": "checker", "approvedAtUtc": "2026-07-01T00:00:00Z", "approvalReference": "APR-1" },
          "evidenceLinks": [],
          "termsFormat": "customProfile"
        }
        """;

    [Fact]
    public void AGovernedEnvelope_ProjectsOneRowPerProfileField_InStoredOrder_WithItsStoredValue()
    {
        PostgresSecurityMasterStore.TryBuildCustomAssetProfileFieldProjection(Record("CustomAsset", EnvelopeJson), out var plan)
            .Should().BeTrue();

        plan.CustomProfileId.Should().Be("specialized-holding");
        plan.ProfileVersion.Should().Be(3);
        plan.Fields.Select(static field => (field.FieldKey, field.ValueKind, field.RawJson)).Should().Equal(
            ("vintage", "string", "\"2019\""),
            ("appraisalDate", "string", "\"2026-06-30\""),
            ("appraisedValue", "number", "1250000.50"),
            ("ownershipPct", "number", "0.125000000000000000000000000001"),
            ("isInsured", "boolean", "true"),
            ("custodian", "null", "null"),
            ("location", "object", "{ \"city\": \"Geneva\", \"vault\": 7 }"),
            ("tags", "array", "[\"art\", \"fine-art\"]"));
    }

    [Fact]
    public void EnvelopeMetadataOutsideProfileFields_IsNotProjectedAsAField()
    {
        PostgresSecurityMasterStore.TryBuildCustomAssetProfileFieldProjection(Record("CustomAsset", EnvelopeJson), out var plan)
            .Should().BeTrue();

        plan.Fields.Select(static field => field.FieldKey)
            .Should().NotContain(["customProfileId", "profileVersion", "profileApproval", "evidenceLinks", "termsFormat"]);
    }

    [Fact]
    public void ARepeatedKey_ProjectsOnce_WithTheValueEveryOtherReaderSees()
    {
        const string json = """{"customProfileId":"p","profileVersion":1,"profileFields":{"a":1,"b":"x","a":2}}""";
        var terms = JsonSerializer.Deserialize<JsonElement>(json);
        var expected = terms.GetProperty("profileFields").GetProperty("a").GetRawText();

        PostgresSecurityMasterStore.TryBuildCustomAssetProfileFieldProjection(Record("CustomAsset", json), out var plan)
            .Should().BeTrue();

        plan.Fields.Select(static field => field.FieldKey).Should().Equal("a", "b");
        plan.Fields[0].RawJson.Should().Be(expected, "the projection must agree with TryGetProperty, the lookup every other reader uses");
    }

    [Fact]
    public void AnEmptyProfileFieldsObject_IsAnEnvelopeWithNoRows()
    {
        PostgresSecurityMasterStore.TryBuildCustomAssetProfileFieldProjection(
                Record("CustomAsset", """{"customProfileId":"p","profileVersion":1,"profileFields":{}}"""), out var plan)
            .Should().BeTrue();
        plan.Fields.Should().BeEmpty();
    }

    public static TheoryData<string, string> RecordsWithoutAProjection => new()
    {
        { "legacy flat terms, no envelope", """{"schemaVersion":1,"profileId":"none"}""" },
        { "profile fields without a profile id", """{"profileVersion":1,"profileFields":{"a":1}}""" },
        { "blank profile id", """{"customProfileId":"  ","profileVersion":1,"profileFields":{"a":1}}""" },
        { "non-string profile id", """{"customProfileId":7,"profileVersion":1,"profileFields":{"a":1}}""" },
        { "missing profile version", """{"customProfileId":"p","profileFields":{"a":1}}""" },
        { "string profile version", """{"customProfileId":"p","profileVersion":"1","profileFields":{"a":1}}""" },
        { "fractional profile version", """{"customProfileId":"p","profileVersion":1.5,"profileFields":{"a":1}}""" },
        { "profile fields not an object", """{"customProfileId":"p","profileVersion":1,"profileFields":[1]}""" },
        { "terms not an object", """[]""" },
    };

    [Theory]
    [MemberData(nameof(RecordsWithoutAProjection))]
    public void ARecordWithoutACompleteEnvelope_HasNoProjection(string because, string termsJson)
    {
        PostgresSecurityMasterStore.TryBuildCustomAssetProfileFieldProjection(Record("CustomAsset", termsJson), out _)
            .Should().BeFalse(because);
    }

    [Fact]
    public void AnotherClassCarryingAnEnvelope_HasNoCustomAssetProjection()
    {
        // OtherSecurity also supports profile-backed terms; only CustomAsset owns this table, and
        // after a class change the CustomAsset writer runs against the new class to clear its rows.
        PostgresSecurityMasterStore.TryBuildCustomAssetProfileFieldProjection(Record("OtherSecurity", EnvelopeJson), out _)
            .Should().BeFalse();
        PostgresSecurityMasterStore.TryBuildCustomAssetProfileFieldProjection(Record("customasset", EnvelopeJson), out _)
            .Should().BeTrue("the class is compared case-insensitively, like the registry writers' own gates");
    }

    [Fact]
    public void TheReaderSelect_NamesTheTableAndReadsNumbersAsText()
    {
        var store = new PostgresCustomAssetProfileFieldProjectionStore(new SecurityMasterOptions { Schema = "sm_test", ConnectionString = "Host=unused" });

        store.SelectSql("where security_id = @security_id").Should().Be(
            "select security_id, custom_profile_id, profile_version, field_key, value_kind, text_value, "
            + "numeric_value::text as numeric_text, boolean_value, raw_value::text as raw_value_json, version "
            + "from sm_test.custom_asset_profile_field_projection where security_id = @security_id;");
        PostgresCustomAssetProfileFieldProjectionStore.ParseNumeric("1250000.50").Should().Be(1250000.50m);
        PostgresCustomAssetProfileFieldProjectionStore.ParseNumeric("1" + new string('0', 40)).Should().BeNull(
            "a number beyond decimal range reads as null rather than failing the query; raw_value keeps it");
        PostgresCustomAssetProfileFieldProjectionStore.ParseNumeric(null).Should().BeNull();
    }

    internal static SecurityProjectionRecord Record(string assetClass, string assetSpecificTermsJson, Guid? securityId = null, long version = 1)
    {
        var id = securityId ?? Guid.NewGuid();
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        var ticker = $"CA{id:N}"[..12];
        return new SecurityProjectionRecord(
            SecurityId: id,
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: "Custom asset fixture",
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: ticker,
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Custom asset fixture", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.Deserialize<JsonElement>(assetSpecificTermsJson),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "test" }),
            Version: version,
            EffectiveFrom: effectiveFrom,
            EffectiveTo: null,
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, ticker, true, effectiveFrom)],
            Aliases: []);
    }
}
