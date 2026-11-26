using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// ADR-034 / review E1: a back-dated correction used to become the whole-life value. Terms are now
/// readable as valid at a date and as known at a recording time.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityMasterValidTimePostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterValidTimePostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task BackDatedCorrection_AppliesFromItsValidFrom_AndIsInvisibleBeforeItWasRecorded()
    {
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var service = new SecurityMasterService(eventStore, snapshotStore, store,
            new SecurityMasterAggregateRebuilder(eventStore, snapshotStore), _fixture.Options, NullLogger<SecurityMasterService>.Instance);
        var securityId = Guid.NewGuid();
        var ticker = $"VT{securityId.ToString("N")[..6].ToUpperInvariant()}";
        var created = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var correctedFrom = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await service.CreateAsync(new CreateSecurityRequest(
            securityId, "Equity", Common("US"), JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, created)],
            created, "test", "feed", null, "create"));
        var beforeCorrection = DateTimeOffset.UtcNow;
        await Task.Delay(20);
        await service.AmendTermsAsync(new AmendSecurityTermsRequest(
            securityId, 1, Common("GB"), null, [], [], correctedFrom, "test", "ops", null, "Back-dated country correction"));

        var query = new SecurityMasterValidTimeQuery(eventStore);
        var at2021 = await query.GetAsync(securityId, new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var at2024 = await query.GetAsync(securityId, new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var at2024KnownBefore = await query.GetAsync(securityId, new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero), beforeCorrection);

        Country(at2021!).Should().Be("US", "the correction applies only from its valid-from");
        Country(at2024!).Should().Be("GB");
        at2024!.AssertedValidFrom.Should().Be(correctedFrom);
        Country(at2024KnownBefore!).Should().Be("US", "as known before the correction was recorded");
    }

    private static string? Country(SecurityTermsAsOf terms)
        => SecurityTermReader.ReadString(terms.Definition.CommonTerms, "countryOfRisk") ?? terms.Definition.RiskCountry;

    private static JsonElement Common(string countryOfRisk)
        => JsonSerializer.SerializeToElement(new
        {
            displayName = "Valid Time Co",
            currency = "USD",
            countryOfRisk,
            issuerName = "Valid Time Co",
            exchange = "XNAS",
            lotSize = 1,
            tickSize = 0.01m
        });
}
