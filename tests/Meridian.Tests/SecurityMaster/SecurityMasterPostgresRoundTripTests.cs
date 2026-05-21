using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
[Collection(nameof(SecurityMasterDatabaseCollection))]
public sealed class SecurityMasterPostgresRoundTripTests
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterPostgresRoundTripTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task CreateAmendDeactivate_RoundTripsAgainstPostgres()
    {
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            _fixture.Options,
            NullLogger<SecurityMasterService>.Instance);
        var securityId = Guid.NewGuid();

        var created = await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "Equity",
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Acme Common",
                currency = "USD",
                countryOfRisk = "US",
                issuerName = "Acme Corp",
                exchange = "XNYS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            JsonSerializer.SerializeToElement(new
            {
                shareClass = "Common"
            }),
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "ACME", true, DateTimeOffset.UtcNow.AddDays(-1), null, null),
                new SecurityIdentifierDto(SecurityIdentifierKind.Isin, "US0000000001", false, DateTimeOffset.UtcNow.AddDays(-1), null, null)
            },
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "initial create"));

        created.Version.Should().Be(1);
        created.Status.Should().Be(SecurityStatusDto.Active);

        var amended = await service.AmendTermsAsync(new AmendSecurityTermsRequest(
            securityId,
            1,
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Acme Common Updated",
                currency = "USD",
                countryOfRisk = "US",
                issuerName = "Acme Corp",
                exchange = "XNAS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            null,
            Array.Empty<SecurityIdentifierDto>(),
            Array.Empty<SecurityIdentifierDto>(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            "test",
            "codex",
            null,
            "rename"));

        amended.Version.Should().Be(2);
        amended.DisplayName.Should().Be("Acme Common Updated");

        await service.DeactivateAsync(new DeactivateSecurityRequest(
            securityId,
            2,
            DateTimeOffset.UtcNow.AddMinutes(2),
            "test",
            "codex",
            null,
            "deactivate"));

        var detail = await store.GetDetailAsync(securityId);
        detail.Should().NotBeNull();
        detail!.Status.Should().Be(SecurityStatusDto.Inactive);
        detail.Version.Should().Be(3);

        var history = await eventStore.LoadAsync(securityId);
        history.Select(evt => evt.EventType).Should().ContainInOrder("SecurityCreated", "TermsAmended", "SecurityDeactivated");

        var resolved = await store.GetByIdentifierAsync(
            SecurityIdentifierKind.Ticker,
            "ACME",
            null,
            DateTimeOffset.UtcNow,
            includeInactive: true);

        resolved.Should().NotBeNull();
        resolved!.SecurityId.Should().Be(securityId);
    }

    [SecurityMasterDatabaseFact]
    public async Task GetByIdentifierAsync_ResolvesNormalizedIdentifierAndProviderValues()
    {
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            _fixture.Options,
            NullLogger<SecurityMasterService>.Instance);
        var securityId = Guid.NewGuid();
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);

        await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "Equity",
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Apple Inc.",
                currency = "USD",
                countryOfRisk = "US",
                issuerName = "Apple Inc.",
                exchange = "XNAS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            JsonSerializer.SerializeToElement(new
            {
                shareClass = "Common"
            }),
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Isin, "us-0378331005", true, effectiveFrom, null, "xnas")
            },
            effectiveFrom,
            "test",
            "codex",
            null,
            "create with raw vendor identifier"));

        await store.UpsertAliasAsync(new SecurityAliasDto(
            Guid.NewGuid(),
            securityId,
            SecurityIdentifierKind.Ric.ToString(),
            " aapl.o ",
            "refinitiv",
            SecurityAliasScope.Collector,
            "vendor alias",
            "codex",
            DateTimeOffset.UtcNow,
            effectiveFrom,
            null,
            true));

        var resolvedByIdentifier = await store.GetByIdentifierAsync(
            SecurityIdentifierKind.Isin,
            "US0378331005",
            "XNAS",
            DateTimeOffset.UtcNow,
            includeInactive: false);
        var resolvedByAlias = await store.GetByIdentifierAsync(
            SecurityIdentifierKind.Ric,
            "AAPL.O",
            "REFINITIV",
            DateTimeOffset.UtcNow,
            includeInactive: false);
        var resolvedWithoutProvider = await store.GetByIdentifierAsync(
            SecurityIdentifierKind.Isin,
            "US0378331005",
            null,
            DateTimeOffset.UtcNow,
            includeInactive: false);

        resolvedByIdentifier.Should().NotBeNull();
        resolvedByIdentifier!.SecurityId.Should().Be(securityId);
        resolvedByIdentifier.Identifiers.Should().ContainSingle(identifier =>
            identifier.NormalizedValue == "US0378331005" &&
            identifier.NormalizedProvider == "XNAS");

        resolvedByAlias.Should().NotBeNull();
        resolvedByAlias!.SecurityId.Should().Be(securityId);

        resolvedWithoutProvider.Should().NotBeNull();
        resolvedWithoutProvider!.SecurityId.Should().Be(securityId);
    }
}
