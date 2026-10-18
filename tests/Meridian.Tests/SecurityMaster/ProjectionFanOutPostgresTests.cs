using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The class-keyed fan-out (<see cref="ProjectionFanOutTests"/>) rests on one invariant: a
/// projection row for class X exists only while the security's stored class is X. This proves the
/// invariant end to end against PostgreSQL by walking one security through three classes and
/// checking the projection tables after each write.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProjectionFanOutPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public ProjectionFanOutPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task AClassChange_ClearsThePriorProjection_AndPublishesTheNewOne()
    {
        var securityId = Guid.NewGuid();
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var equityStore = new PostgresEquityReferenceProjectionStore(_fixture.Options);
        var depositStore = new PostgresDepositReferenceProjectionStore(_fixture.Options);

        await store.UpsertProjectionAsync(Record(securityId, "Equity", version: 1, new
        {
            schemaVersion = 1,
            shareClass = "Common"
        }));
        (await equityStore.GetEquityAsync(securityId)).Should().NotBeNull("the first write publishes the equity projection");

        await store.UpsertProjectionAsync(Record(securityId, "Deposit", version: 2, new
        {
            schemaVersion = 1,
            depositType = "Term",
            institutionName = "Fan-out Bank"
        }));
        (await equityStore.GetEquityAsync(securityId)).Should().BeNull("the prior class's writer clears the equity row");
        (await depositStore.GetDepositAsync(securityId)).Should().NotBeNull("the current class's writer publishes the deposit row");

        await store.UpsertProjectionAsync(Record(securityId, "CustomAsset", version: 3, new
        {
            schemaVersion = 1,
            profileId = "none"
        }));
        (await depositStore.GetDepositAsync(securityId)).Should().BeNull("moving to an unprojected class still clears the prior projection");
        (await equityStore.GetEquityAsync(securityId)).Should().BeNull();
    }

    [SecurityMasterDatabaseFact]
    public async Task ARewriteThatLosesAGateTerm_ClearsItsOwnProjection()
    {
        var securityId = Guid.NewGuid();
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var depositStore = new PostgresDepositReferenceProjectionStore(_fixture.Options);

        await store.UpsertProjectionAsync(Record(securityId, "Deposit", version: 1, new
        {
            schemaVersion = 1,
            depositType = "Term",
            institutionName = "Fan-out Bank"
        }));
        (await depositStore.GetDepositAsync(securityId)).Should().NotBeNull();

        // Same class, but the institutionName gate is gone: the current class's writer takes the
        // delete branch itself, which is why a same-class rewrite needs no second writer.
        await store.UpsertProjectionAsync(Record(securityId, "Deposit", version: 2, new
        {
            schemaVersion = 1,
            depositType = "Term"
        }));
        (await depositStore.GetDepositAsync(securityId)).Should().BeNull();
    }

    private static SecurityProjectionRecord Record(Guid securityId, string assetClass, long version, object assetSpecificTerms)
    {
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        var ticker = $"FAN{securityId:N}"[..12];
        return new SecurityProjectionRecord(
            SecurityId: securityId,
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: "Fan-out fixture",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: ticker,
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Fan-out fixture", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(assetSpecificTerms),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "test" }),
            Version: version,
            EffectiveFrom: effectiveFrom,
            EffectiveTo: null,
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, effectiveFrom)],
            Aliases: []);
    }
}
