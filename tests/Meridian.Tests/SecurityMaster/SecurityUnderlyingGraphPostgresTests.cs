using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The underlying graph in PostgreSQL: derived edges are written with the projection and rewritten
/// when the terms change, declared edges live beside them with their own window, and the reverse
/// walk finds every instrument that names a security.
/// </summary>
public sealed class SecurityUnderlyingGraphPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityUnderlyingGraphPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task DerivedLinks_FollowTheTerms_AndDependentsWalkBack()
    {
        var (service, store) = Build();
        var graph = new PostgresSecurityUnderlyingGraphStore(_fixture.Options);
        var underlyingId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        var otherUnderlyingId = Guid.NewGuid();

        await CreateAsync(service, underlyingId, "Equity", new { shareClass = "Common" });
        await CreateAsync(service, otherUnderlyingId, "Equity", new { shareClass = "Common" });
        await CreateAsync(service, optionId, "Option", new
        {
            underlyingId,
            putCall = "Call",
            strike = 100m,
            expiry = "2027-01-15",
            multiplier = 100m
        });

        var underlyings = await graph.GetUnderlyingsAsync(optionId);
        underlyings.Should().ContainSingle();
        underlyings[0].Role.Should().Be(SecurityUnderlyingRole.Underlying);
        underlyings[0].UnderlyingSecurityId.Should().Be(underlyingId);
        underlyings[0].Source.Should().Be(SecurityUnderlyingLinkSource.Derived);

        (await graph.GetDependentsAsync(underlyingId)).Should().ContainSingle().Which.SecurityId.Should().Be(optionId);

        // Re-pointing the option rewrites its derived edge: the old target loses the dependent.
        var current = await store.GetProjectionAsync(optionId);
        current.Should().NotBeNull();
        await store.UpsertProjectionAsync(current! with
        {
            Version = current.Version + 1,
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                underlyingId = otherUnderlyingId,
                putCall = "Call",
                strike = 100m,
                expiry = "2027-01-15",
                multiplier = 100m
            })
        });

        (await graph.GetUnderlyingsAsync(optionId)).Should().ContainSingle().Which.UnderlyingSecurityId.Should().Be(otherUnderlyingId);
        (await graph.GetDependentsAsync(underlyingId)).Should().BeEmpty();
        (await graph.GetDependentsAsync(otherUnderlyingId)).Should().ContainSingle();
    }

    [SecurityMasterDatabaseFact]
    public async Task DeclaredLinks_LiveBesideDerivedOnes_AndOnlyDeclaredOnesCanBeRemoved()
    {
        var (service, _) = Build();
        var graph = new PostgresSecurityUnderlyingGraphStore(_fixture.Options);
        var noteId = Guid.NewGuid();
        var constituentA = Guid.NewGuid();
        var constituentB = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await CreateAsync(service, noteId, "StructuredNote", new
        {
            issuerName = "Bank",
            underlyingDescription = "Custom basket",
            maturity = "2030-01-01"
        });

        var a = await graph.DeclareAsync(new DeclareSecurityUnderlyingLinkRequest(noteId, SecurityUnderlyingRole.BasketConstituent, constituentA, null, 0.6m, from, null, "60% weight", "casey"));
        var b = await graph.DeclareAsync(new DeclareSecurityUnderlyingLinkRequest(noteId, SecurityUnderlyingRole.BasketConstituent, constituentB, null, 0.4m, from, from.AddYears(1), null, "casey"));
        // Re-declaring the same edge updates it in place.
        var aAgain = await graph.DeclareAsync(new DeclareSecurityUnderlyingLinkRequest(noteId, SecurityUnderlyingRole.BasketConstituent, constituentA, null, 0.65m, from, null, "rebalanced", "casey"));
        aAgain.LinkId.Should().Be(a.LinkId);

        var links = await graph.GetUnderlyingsAsync(noteId);
        links.Should().HaveCount(3, "the derived free-text underlying plus two declared constituents");
        links.Where(static l => l.Source == SecurityUnderlyingLinkSource.Declared).Should().HaveCount(2);
        links.Single(l => l.LinkId == a.LinkId).Weight.Should().Be(0.65m);
        links.Single(l => l.LinkId == b.LinkId).ValidTo.Should().Be(from.AddYears(1));
        links.Single(static l => l.Source == SecurityUnderlyingLinkSource.Derived).Reference.Should().Be("Custom basket");

        (await graph.GetDependentsAsync(constituentA)).Should().ContainSingle();

        (await graph.RemoveDeclaredAsync(noteId, a.LinkId)).Should().BeTrue();
        (await graph.RemoveDeclaredAsync(noteId, a.LinkId)).Should().BeFalse("already removed");
        var derived = links.Single(static l => l.Source == SecurityUnderlyingLinkSource.Derived);
        (await graph.RemoveDeclaredAsync(noteId, derived.LinkId)).Should().BeFalse("derived links follow the terms");
        (await graph.GetUnderlyingsAsync(noteId)).Should().HaveCount(2);
    }

    [SecurityMasterDatabaseFact]
    public async Task Declare_RefusesInvalidLinks()
    {
        var graph = new PostgresSecurityUnderlyingGraphStore(_fixture.Options);
        var id = Guid.NewGuid();
        var from = DateTimeOffset.UtcNow;

        await graph.Invoking(g => g.DeclareAsync(new DeclareSecurityUnderlyingLinkRequest(id, SecurityUnderlyingRole.Underlying, null, " ", null, from, null, null)))
            .Should().ThrowAsync<ArgumentException>();
        await graph.Invoking(g => g.DeclareAsync(new DeclareSecurityUnderlyingLinkRequest(id, SecurityUnderlyingRole.Underlying, id, null, null, from, null, null)))
            .Should().ThrowAsync<ArgumentException>();
        await graph.Invoking(g => g.DeclareAsync(new DeclareSecurityUnderlyingLinkRequest(id, SecurityUnderlyingRole.Underlying, Guid.NewGuid(), null, null, from, from, null)))
            .Should().ThrowAsync<ArgumentException>();
        await graph.Invoking(g => g.DeclareAsync(new DeclareSecurityUnderlyingLinkRequest(id, SecurityUnderlyingRole.BasketConstituent, Guid.NewGuid(), null, -0.1m, from, null, null)))
            .Should().ThrowAsync<ArgumentException>();
    }

    private (SecurityMasterService Service, PostgresSecurityMasterStore Store) Build()
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
        return (service, store);
    }

    private static async Task CreateAsync(SecurityMasterService service, Guid securityId, string assetClass, object terms)
    {
        var validFrom = DateTimeOffset.UtcNow.AddDays(-30);
        var ticker = "UG" + securityId.ToString("N")[..8].ToUpperInvariant();
        await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            assetClass,
            JsonSerializer.SerializeToElement(new
            {
                displayName = ticker,
                currency = "USD",
                countryOfRisk = "US",
                issuerName = ticker,
                exchange = "XNAS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            JsonSerializer.SerializeToElement(terms),
            new[] { new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, validFrom, null, null) },
            validFrom,
            "test",
            "codex",
            null,
            "create for the underlying graph"));
    }
}
