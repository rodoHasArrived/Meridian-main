using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Review C4: a dry-run classifies every row instead of the create-only import counting a corrected
/// row as "skipped, already mastered".
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityMasterImportPlannerPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterImportPlannerPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task CorrectedFile_YieldsAmendProposals_UnchangedRows_AndCreates()
    {
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var service = new SecurityMasterService(eventStore, snapshotStore, store,
            new SecurityMasterAggregateRebuilder(eventStore, snapshotStore), _fixture.Options, NullLogger<SecurityMasterService>.Instance);
        var unchangedTicker = Ticker("UC");
        var correctedTicker = Ticker("CR");
        var newTicker = Ticker("NW");
        await service.CreateAsync(Row(unchangedTicker, "US"));
        await service.CreateAsync(Row(correctedTicker, "US"));

        var file = JsonSerializer.Serialize(new[] { Row(unchangedTicker, "US"), Row(correctedTicker, "GB"), Row(newTicker, "US") });
        var plan = await new SecurityMasterImportPlanner(new SecurityMasterCsvParser(), store).PlanAsync(file, "json", "ops.analyst");

        plan.Rows.Should().HaveCount(3);
        plan.Rows.Single(row => row.PrimaryIdentifier == unchangedTicker).Disposition.Should().Be(SecurityMasterImportRowDisposition.Unchanged);
        var corrected = plan.Rows.Single(row => row.PrimaryIdentifier == correctedTicker);
        corrected.Disposition.Should().Be(SecurityMasterImportRowDisposition.AmendProposal);
        corrected.ChangedFields.Should().Equal("commonTerms.countryOfRisk");
        plan.Rows.Single(row => row.PrimaryIdentifier == newTicker).Disposition.Should().Be(SecurityMasterImportRowDisposition.Create);
    }

    [Fact]
    public void Diff_IgnoresOmittedFieldsAndNumericScale()
    {
        var stored = JsonSerializer.SerializeToElement(new { couponRate = 4.250m, maturity = "2030-01-01", issuer = "A" });
        var incoming = JsonSerializer.SerializeToElement(new { couponRate = 4.25m, maturity = "2031-01-01" });

        SecurityMasterImportPlanner.DiffTopLevel("assetSpecificTerms", stored, incoming)
            .Should().Equal("assetSpecificTerms.maturity");
    }

    private static string Ticker(string prefix) => $"{prefix}{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private static CreateSecurityRequest Row(string ticker, string countryOfRisk)
    {
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        return new CreateSecurityRequest(
            Guid.NewGuid(),
            "Equity",
            JsonSerializer.SerializeToElement(new { displayName = ticker, currency = "USD", countryOfRisk, issuerName = ticker, exchange = "XNAS", lotSize = 1, tickSize = 0.01m }),
            JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, effectiveFrom)],
            effectiveFrom,
            "test",
            "feed",
            null,
            "create");
    }
}
