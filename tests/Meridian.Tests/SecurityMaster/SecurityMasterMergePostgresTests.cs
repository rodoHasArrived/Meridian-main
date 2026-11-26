using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Review C1 (owner decision 2026-11-25: redirect). Merging a duplicate retires it and records a
/// redirect; resolution of an identifier only the duplicate claimed lands on the survivor.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityMasterMergePostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterMergePostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task Merge_RetiresTheDuplicate_AndResolutionFollowsTheRedirect()
    {
        var (service, store, redirects) = Compose();
        var survivorTicker = Ticker("SV");
        var duplicateTicker = Ticker("DP");
        var survivor = await CreateEquityAsync(service, survivorTicker);
        var duplicate = await CreateEquityAsync(service, duplicateTicker);
        var merges = new SecurityMasterMergeService(service, store, redirects, NullLogger<SecurityMasterMergeService>.Instance);

        var result = await merges.MergeAsync(new MergeSecuritiesRequest(duplicate, survivor, 1, "ops.analyst", "Duplicate of the same listing."));

        result.RetiredWasDeactivated.Should().BeTrue();
        (await store.GetProjectionAsync(duplicate))!.Status.Should().Be(SecurityStatusDto.Inactive);
        var resolver = new SecurityResolver(store, redirects);
        var resolution = await resolver.ResolveCanonicalAsync(
            new ResolveSecurityRequest(SecurityIdentifierKind.Ticker, duplicateTicker, null, DateTimeOffset.UtcNow.AddMinutes(-1), ActiveOnly: true));
        resolution.Status.Should().Be(SecurityResolutionStatus.Resolved);
        resolution.SecurityId.Should().Be(survivor);
    }

    [SecurityMasterDatabaseFact]
    public async Task Merge_IsIdempotent_AndRefusesASecondSurvivor()
    {
        var (service, store, redirects) = Compose();
        var survivor = await CreateEquityAsync(service, Ticker("SA"));
        var other = await CreateEquityAsync(service, Ticker("SB"));
        var duplicate = await CreateEquityAsync(service, Ticker("DX"));
        var merges = new SecurityMasterMergeService(service, store, redirects, NullLogger<SecurityMasterMergeService>.Instance);

        await merges.MergeAsync(new MergeSecuritiesRequest(duplicate, survivor, 1, "ops", "dup"));
        var retry = await merges.MergeAsync(new MergeSecuritiesRequest(duplicate, survivor, 2, "ops", "dup"));
        var conflicting = () => merges.MergeAsync(new MergeSecuritiesRequest(duplicate, other, 2, "ops", "dup"));

        retry.RetiredWasDeactivated.Should().BeFalse("the retry only records the redirect");
        await conflicting.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already merged*");
    }

    private (SecurityMasterService, PostgresSecurityMasterStore, PostgresSecurityMasterRedirectStore) Compose()
    {
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var service = new SecurityMasterService(
            eventStore, snapshotStore, store, new SecurityMasterAggregateRebuilder(eventStore, snapshotStore),
            _fixture.Options, NullLogger<SecurityMasterService>.Instance);
        return (service, store, new PostgresSecurityMasterRedirectStore(_fixture.Options));
    }

    private static string Ticker(string prefix) => $"{prefix}{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private static async Task<Guid> CreateEquityAsync(SecurityMasterService service, string ticker)
    {
        var securityId = Guid.NewGuid();
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "Equity",
            JsonSerializer.SerializeToElement(new { displayName = ticker, currency = "USD", countryOfRisk = "US", issuerName = ticker, exchange = "XNAS", lotSize = 1, tickSize = 0.01m }),
            JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, effectiveFrom)],
            effectiveFrom,
            "test",
            "feed",
            null,
            "create"));
        return securityId;
    }
}
