using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The generic registry read against a real database: the descriptor selects have to agree with
/// the migrated DDL, SQL <c>date</c> columns read as ISO dates and numerics keep their exact text,
/// and the security's current asset class gates the read.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RegistryProjectionReadPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public RegistryProjectionReadPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task CommercialPaper_ReadsTheRegistryRow_AndIsGatedOnTheCurrentClass()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var service = new RegistryProjectionReadService(_fixture.Options, store);
        var securityId = Guid.NewGuid();

        await store.UpsertProjectionAsync(Record(securityId, "CommercialPaper", version: 1, new
        {
            schemaVersion = 1,
            issuerName = "Meridian Funding Corp",
            maturity = "2027-02-15",
            discountRate = 5.2500m,
            dayCount = "ACT/360",
            isAssetBacked = true
        }));

        var projection = await service.GetAsync("commercialpaper", securityId);

        projection.Should().NotBeNull();
        projection!.AssetClass.Should().Be("CommercialPaper");
        projection.TableName.Should().Be("commercial_paper_projection");
        projection.Columns.Keys.Should().Equal(
            SecurityTermsProjectionRegistry.ParentColumnNames(SecurityTermsProjectionRegistry.Require("CommercialPaper")));
        projection.Columns["security_id"].GetString().Should().Be(securityId.ToString("D"));
        projection.Columns["issuer_name"].GetString().Should().Be("Meridian Funding Corp");
        projection.Columns["maturity_date"].GetString().Should().Be("2027-02-15");
        projection.Columns["discount_rate"].GetDecimal().Should().Be(5.25m);
        projection.Columns["is_asset_backed"].GetBoolean().Should().BeTrue();
        projection.Columns["version"].GetInt64().Should().Be(1L);

        (await service.GetAsync("Repo", securityId)).Should().BeNull("the security is not a Repo");
        (await service.GetAsync("CommercialPaper", Guid.NewGuid())).Should().BeNull("no such security");
    }

    [SecurityMasterDatabaseFact]
    public async Task DirectLoan_ReadsTheParentAndBothChildTablesInOrdinalOrder()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var service = new RegistryProjectionReadService(_fixture.Options, store);
        var securityId = Guid.NewGuid();

        await store.UpsertProjectionAsync(Record(securityId, "DirectLoan", version: 1, new
        {
            schemaVersion = 1,
            borrower = "Meridian Industrials LLC",
            maturity = "2030-03-31",
            spreadBps = 425m,
            covenants = new[]
            {
                new { covenantType = "MaxLeverage", threshold = "4.5x", notes = "Tested quarterly" },
                new { covenantType = "MinFixedCharge", threshold = "1.10x", notes = (string?)null }
            },
            principalSchedule = new[]
            {
                new { paymentDate = "2027-03-31", amount = 1_250_000m },
                new { paymentDate = "2028-03-31", amount = 1_500_000m }
            }
        }));

        var projection = await service.GetAsync("DirectLoan", securityId);

        projection!.Columns["borrower"].GetString().Should().Be("Meridian Industrials LLC");
        projection.Columns["maturity_date"].GetString().Should().Be("2030-03-31");
        projection.Columns["reference_index"].ValueKind.Should().Be(JsonValueKind.Null);

        var covenants = projection.ChildTables.Single(table => table.TableName == "direct_loan_covenant_projection").Rows;
        covenants.Select(row => row["covenant_type"].GetString()).Should().Equal("MaxLeverage", "MinFixedCharge");
        covenants.Select(row => row["ordinal"].GetInt32()).Should().Equal(0, 1);
        covenants[1]["notes"].ValueKind.Should().Be(JsonValueKind.Null);

        var schedule = projection.ChildTables.Single(table => table.TableName == "direct_loan_principal_schedule_projection").Rows;
        schedule.Select(row => row["payment_date"].GetString()).Should().Equal("2027-03-31", "2028-03-31");
        schedule.Select(row => row["amount"].GetDecimal()).Should().Equal(1_250_000m, 1_500_000m);
    }

    private static SecurityProjectionRecord Record(Guid securityId, string assetClass, long version, object assetSpecificTerms)
    {
        var identifierValue = $"REGREAD-{securityId:N}";
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        var identifier = new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, identifierValue, true, effectiveFrom);

        return new(
            securityId,
            assetClass,
            SecurityStatusDto.Active,
            $"Registry read fixture {securityId:N}",
            "USD",
            SecurityIdentifierKind.InternalCode.ToString(),
            identifierValue,
            JsonSerializer.SerializeToElement(new { displayName = $"Registry read fixture {securityId:N}", currency = "USD" }),
            JsonSerializer.SerializeToElement(assetSpecificTerms),
            JsonSerializer.SerializeToElement(new { sourceSystem = "integration-test", updatedBy = "registry-read-fixture" }),
            version,
            effectiveFrom,
            null,
            [identifier],
            []);
    }
}
