using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// CustomAsset promotion end to end against PostgreSQL: the co-invest-spv record's profile-field
/// rows (migration 064) are cleared and the PrivateFundInterest registry projection is written when
/// the envelope amendment promotes it, and the stored row and events say the same.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CustomAssetPromotionRoundTripPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public CustomAssetPromotionRoundTripPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task CoInvestSpv_PromotedToPrivateFundInterest_MovesItsProjectionRows()
    {
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            new SecurityMasterAggregateRebuilder(eventStore, snapshotStore),
            _fixture.Options,
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: StaticSecurityAssetProfileCatalog.CreateDefault());
        var profileFields = new PostgresCustomAssetProfileFieldProjectionStore(_fixture.Options);
        var securityId = Guid.NewGuid();
        var identifierValue = $"SPV-{securityId:N}";

        var created = await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "CustomAsset",
            JsonSerializer.SerializeToElement(new
            {
                displayName = "SPV becoming fund interest",
                currency = "USD",
                exchange = "XNYS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            CustomAssetPromotionRoundTripTests.CoInvestSpvEnvelope(),
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, identifierValue, true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
            },
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "create spv"));
        created.AssetClass.Should().Be("CustomAsset");
        (await profileFields.GetProfileFieldsAsync(securityId)).Should().NotBeEmpty("a CustomAsset envelope projects its profile fields");
        (await CountAsync("private_fund_interest_projection", securityId)).Should().Be(0);

        // A common-terms amendment of a co-invest-spv record keeps it CustomAsset. co-invest-spv does
        // not reclassify, so this is not the R1 guard (CustomAssetPromotionRoundTripTests pins that on
        // a record pinned to private-fund-interest); it gives the history a no-patch event to replay.
        var renamed = await service.AmendTermsAsync(new AmendSecurityTermsRequest(
            securityId,
            1,
            JsonSerializer.SerializeToElement(new
            {
                displayName = "SPV renamed",
                currency = "USD",
                exchange = "XNYS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            null,
            [],
            [],
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "rename"));
        renamed.AssetClass.Should().Be("CustomAsset");

        var promoted = await service.AmendTermsAsync(CustomAssetPromotionRoundTripTests.EnvelopeAmend(
            securityId, expectedVersion: 2, CustomAssetPromotionRoundTripTests.PrivateFundInterestEnvelope()));

        promoted.AssetClass.Should().Be("PrivateFundInterest");
        promoted.Version.Should().Be(3);
        promoted.Identifiers.Should().ContainSingle(static identifier => identifier.Kind == SecurityIdentifierKind.InternalCode)
            .Which.Value.Should().Be(identifierValue);

        (await profileFields.GetProfileFieldsAsync(securityId)).Should().BeEmpty("leaving CustomAsset clears its profile-field rows");
        (await CountAsync("private_fund_interest_projection", securityId)).Should().Be(1, "the promoted class's registry projection is written");

        var persisted = await store.GetProjectionAsync(securityId);
        persisted!.AssetClass.Should().Be("PrivateFundInterest");
        SecurityTermsEnvelope.GetDeclaredFormat(persisted.AssetSpecificTerms).Should().Be(SecurityTermsFormats.CustomProfile);
        (await ScalarAsync("select terms_format from {schema}.securities where security_id = @id", securityId))
            .Should().Be(SecurityTermsFormats.CustomProfile);

        var history = await eventStore.LoadAsync(securityId);
        history.Select(static evt => evt.EventType).Should().Equal("SecurityCreated", "TermsAmended", "TermsAmended");
        history.Select(static evt => SecurityMasterMapping.FromEventEnvelope(evt).LegacyAssetClass)
            .Should().Equal("CustomAsset", "CustomAsset", "PrivateFundInterest");
    }

    private async Task<long> CountAsync(string table, Guid securityId)
        => Convert.ToInt64(await ScalarObjectAsync($"select count(*) from {{schema}}.{table} where security_id = @id", securityId));

    private async Task<string?> ScalarAsync(string sql, Guid securityId)
        => (string?)await ScalarObjectAsync(sql, securityId);

    private async Task<object?> ScalarObjectAsync(string sql, Guid securityId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql.Replace("{schema}", _fixture.Options.Schema, StringComparison.Ordinal), connection);
        command.Parameters.AddWithValue("id", securityId);
        return await command.ExecuteScalarAsync();
    }
}
