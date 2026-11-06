using System.Text.Json;
using FluentAssertions;
using Meridian.Storage.SecurityMaster;
using Npgsql;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The profile-keyed CustomAsset projection (migration 064) end to end through the store's fan-out:
/// rows follow the envelope, values keep their stored meaning in the typed columns, and a legacy or
/// class-changed record clears them.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CustomAssetProfileFieldProjectionPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public CustomAssetProfileFieldProjectionPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task AGovernedEnvelope_ProjectsTypedRows_ThatKeepTheirStoredMeaning()
    {
        var securityId = Guid.NewGuid();
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var reader = new PostgresCustomAssetProfileFieldProjectionStore(_fixture.Options);

        await store.UpsertProjectionAsync(CustomAssetProfileFieldProjectionTests.Record(
            "CustomAsset", CustomAssetProfileFieldProjectionTests.EnvelopeJson, securityId, version: 4));

        var rows = (await reader.GetProfileFieldsAsync(securityId)).ToDictionary(static row => row.FieldKey);
        rows.Keys.Should().BeEquivalentTo(
            ["vintage", "appraisalDate", "appraisedValue", "ownershipPct", "isInsured", "custodian", "location", "tags"]);
        rows.Values.Should().OnlyContain(row =>
            row.SecurityId == securityId && row.CustomProfileId == "specialized-holding" && row.ProfileVersion == 3 && row.Version == 4);

        rows["vintage"].Should().BeEquivalentTo(new { ValueKind = "string", TextValue = "2019", NumericValue = (decimal?)null, BooleanValue = (bool?)null },
            "a numeric-looking string stays a string");
        rows["appraisalDate"].TextValue.Should().Be("2026-06-30", "a date-looking string is not reinterpreted");
        rows["appraisedValue"].Should().BeEquivalentTo(new { ValueKind = "number", TextValue = (string?)null, NumericValue = 1250000.50m });
        rows["isInsured"].Should().BeEquivalentTo(new { ValueKind = "boolean", BooleanValue = (bool?)true, TextValue = (string?)null });
        rows["custodian"].Should().BeEquivalentTo(new { ValueKind = "null", TextValue = (string?)null, NumericValue = (decimal?)null, BooleanValue = (bool?)null, RawValueJson = "null" });
        rows["location"].ValueKind.Should().Be("object");
        JsonSerializer.Deserialize<JsonElement>(rows["location"].RawValueJson).GetProperty("vault").GetInt32().Should().Be(7);
        rows["tags"].RawValueJson.Should().Be("[\"art\", \"fine-art\"]");

        // The unbounded numeric column keeps a precision decimal cannot carry.
        (await ScalarAsync(
                $"select numeric_value::text from {_fixture.Options.Schema}.custom_asset_profile_field_projection where security_id = @id and field_key = 'ownershipPct'",
                securityId))
            .Should().Be("0.125000000000000000000000000001");

        (await reader.GetByProfileAsync("specialized-holding", 3)).Should().Contain(row => row.SecurityId == securityId);
        (await reader.GetByProfileAsync("specialized-holding", 2)).Should().NotContain(row => row.SecurityId == securityId);
        (await reader.FindByTextValueAsync("specialized-holding", "vintage", "2019")).Should().ContainSingle(row => row.SecurityId == securityId);
    }

    [SecurityMasterDatabaseFact]
    public async Task ALongIncompressibleTextValue_Projects_AndIsFoundByItsExactValue()
    {
        // Profile Text fields have no length limit. An index over the raw value would exceed the
        // B-tree entry size (~2.7 KB) for long, poorly compressible text and fail the canonical write.
        var securityId = Guid.NewGuid();
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var reader = new PostgresCustomAssetProfileFieldProjectionStore(_fixture.Options);
        var longText = string.Concat(Enumerable.Range(0, 400).Select(static _ => Guid.NewGuid().ToString("N")));
        var terms = JsonSerializer.Serialize(new
        {
            customProfileId = "p-long-text",
            profileVersion = 1,
            profileFields = new { memo = longText, other = longText[..^1] + "x" },
        });

        await store.UpsertProjectionAsync(CustomAssetProfileFieldProjectionTests.Record("CustomAsset", terms, securityId, version: 1));

        (await reader.GetProfileFieldsAsync(securityId)).Single(static row => row.FieldKey == "memo").TextValue.Should().Be(longText);
        (await reader.FindByTextValueAsync("p-long-text", "memo", longText)).Should().ContainSingle(row => row.SecurityId == securityId);
        (await reader.FindByTextValueAsync("p-long-text", "memo", longText[..^1] + "x")).Should().BeEmpty(
            "the hash narrows the lookup and the exact value still decides it");
    }

    [SecurityMasterDatabaseFact]
    public async Task ARewrite_ReplacesTheFieldRows_AndALegacyRowWithoutAnEnvelopeClearsThem()
    {
        var securityId = Guid.NewGuid();
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var reader = new PostgresCustomAssetProfileFieldProjectionStore(_fixture.Options);

        await store.UpsertProjectionAsync(CustomAssetProfileFieldProjectionTests.Record(
            "CustomAsset", """{"customProfileId":"p-rewrite","profileVersion":1,"profileFields":{"a":"one","b":2}}""", securityId, version: 1));
        (await reader.GetProfileFieldsAsync(securityId)).Select(static row => row.FieldKey).Should().Equal("a", "b");

        await store.UpsertProjectionAsync(CustomAssetProfileFieldProjectionTests.Record(
            "CustomAsset", """{"customProfileId":"p-rewrite","profileVersion":2,"profileFields":{"b":3}}""", securityId, version: 2));
        var rewritten = await reader.GetProfileFieldsAsync(securityId);
        rewritten.Should().ContainSingle().Which.Should().BeEquivalentTo(new { FieldKey = "b", NumericValue = (decimal?)3m, ProfileVersion = 2, Version = 2L },
            "a field the envelope no longer declares is removed rather than left stale");

        // A legacy/unmapped CustomAsset row without a profile envelope projects nothing; its terms
        // JSON is still persisted as the only copy.
        await store.UpsertProjectionAsync(CustomAssetProfileFieldProjectionTests.Record(
            "CustomAsset", """{"schemaVersion":1,"profileId":"none"}""", securityId, version: 3));
        (await reader.GetProfileFieldsAsync(securityId)).Should().BeEmpty();
        var persisted = await store.GetProjectionAsync(securityId);
        persisted.Should().NotBeNull();
        persisted!.AssetSpecificTerms.GetProperty("profileId").GetString().Should().Be("none");
    }

    [SecurityMasterDatabaseFact]
    public async Task AClassChange_FromAndToCustomAsset_MovesTheProjection()
    {
        var securityId = Guid.NewGuid();
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var reader = new PostgresCustomAssetProfileFieldProjectionStore(_fixture.Options);
        var depositStore = new PostgresDepositReferenceProjectionStore(_fixture.Options);

        await store.UpsertProjectionAsync(CustomAssetProfileFieldProjectionTests.Record(
            "Deposit", """{"schemaVersion":1,"depositType":"Term","institutionName":"Fan-out Bank","isCallable":false}""", securityId, version: 1));
        (await depositStore.GetDepositAsync(securityId)).Should().NotBeNull();

        await store.UpsertProjectionAsync(CustomAssetProfileFieldProjectionTests.Record(
            "CustomAsset", CustomAssetProfileFieldProjectionTests.EnvelopeJson, securityId, version: 2));
        (await depositStore.GetDepositAsync(securityId)).Should().BeNull("the prior class's writer clears the deposit row");
        (await reader.GetProfileFieldsAsync(securityId)).Should().HaveCount(8);

        await store.UpsertProjectionAsync(CustomAssetProfileFieldProjectionTests.Record(
            "Deposit", """{"schemaVersion":1,"depositType":"Term","institutionName":"Fan-out Bank","isCallable":false}""", securityId, version: 3));
        (await reader.GetProfileFieldsAsync(securityId)).Should().BeEmpty("leaving CustomAsset clears its profile-field rows");
        (await depositStore.GetDepositAsync(securityId)).Should().NotBeNull();
    }

    private async Task<string?> ScalarAsync(string sql, Guid securityId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", securityId);
        return (string?)await command.ExecuteScalarAsync();
    }
}
