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
[Trait("Category", "Integration")]
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

        (await graph.RemoveDeclaredAsync(noteId, a.LinkId, "casey")).Should().BeTrue();
        (await graph.RemoveDeclaredAsync(noteId, a.LinkId, "casey")).Should().BeFalse("already removed");
        var derived = links.Single(static l => l.Source == SecurityUnderlyingLinkSource.Derived);
        (await graph.RemoveDeclaredAsync(noteId, derived.LinkId, "casey")).Should().BeFalse("derived links follow the terms");
        (await graph.GetUnderlyingsAsync(noteId)).Should().HaveCount(2);
    }

    [SecurityMasterDatabaseFact]
    public async Task DeclaredLinkChanges_AreRecordedAsGovernanceEvents_WithTheActorAndTheReplacedState()
    {
        var (service, _) = Build();
        var graph = new PostgresSecurityUnderlyingGraphStore(_fixture.Options);
        var governance = new PostgresSecurityGovernanceEventLog(_fixture.Options);
        var noteId = Guid.NewGuid();
        var constituent = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await CreateAsync(service, noteId, "StructuredNote", new
        {
            issuerName = "Bank",
            underlyingDescription = "Custom basket",
            maturity = "2030-01-01"
        });
        (await GraphEventsAsync(governance, noteId)).Should().BeEmpty("derived links follow the terms, which the event stream already records");

        var declared = await graph.DeclareAsync(new DeclareSecurityUnderlyingLinkRequest(noteId, SecurityUnderlyingRole.BasketConstituent, constituent, null, 0.6m, from, null, "initial weight", "casey"));
        var amended = await graph.DeclareAsync(new DeclareSecurityUnderlyingLinkRequest(noteId, SecurityUnderlyingRole.BasketConstituent, constituent, null, 0.65m, from, null, "rebalanced", "jordan"));

        // Re-entering the edge exactly as stored is not a change: nothing is written or recorded,
        // and the stored declaration (with its actor) comes back.
        var unchanged = await graph.DeclareAsync(new DeclareSecurityUnderlyingLinkRequest(noteId, SecurityUnderlyingRole.BasketConstituent, constituent, null, 0.65m, from, null, "rebalanced", "morgan"));
        unchanged.CreatedBy.Should().Be("jordan");
        unchanged.Weight.Should().Be(0.65m);

        (await graph.RemoveDeclaredAsync(noteId, declared.LinkId, "riley")).Should().BeTrue();
        (await graph.RemoveDeclaredAsync(noteId, declared.LinkId, "riley")).Should().BeFalse();

        var events = await GraphEventsAsync(governance, noteId);
        events.Select(static e => e.EventType).Should().Equal("UnderlyingLinkDeclared", "UnderlyingLinkAmended", "UnderlyingLinkRemoved");
        events.Select(static e => e.Actor).Should().Equal("casey", "jordan", "riley");

        var first = events[0].Payload;
        first.GetProperty("link").GetProperty("linkId").GetGuid().Should().Be(declared.LinkId);
        first.GetProperty("link").GetProperty("role").GetString().Should().Be("BasketConstituent");
        first.GetProperty("link").GetProperty("underlyingSecurityId").GetGuid().Should().Be(constituent);
        first.GetProperty("link").GetProperty("weight").GetDecimal().Should().Be(0.6m);
        first.GetProperty("previous").ValueKind.Should().Be(JsonValueKind.Null);

        var change = events[1].Payload;
        change.GetProperty("previous").GetProperty("weight").GetDecimal().Should().Be(0.6m);
        change.GetProperty("previous").GetProperty("notes").GetString().Should().Be("initial weight");
        change.GetProperty("previous").GetProperty("declaredBy").GetString().Should().Be("casey");
        change.GetProperty("link").GetProperty("weight").GetDecimal().Should().Be(0.65m);
        change.GetProperty("changes").EnumerateArray().Select(static c => c.GetString()).Should().Equal("weight", "notes");
        amended.LinkId.Should().Be(declared.LinkId);

        var removal = events[2].Payload.GetProperty("link");
        removal.GetProperty("linkId").GetGuid().Should().Be(declared.LinkId);
        removal.GetProperty("weight").GetDecimal().Should().Be(0.65m, "the removal keeps the state that was removed");
        removal.GetProperty("declaredBy").GetString().Should().Be("jordan");
    }

    [SecurityMasterDatabaseFact]
    public async Task DeclaredLinkEvents_RecordTheRowAsStored()
    {
        var (service, _) = Build();
        var graph = new PostgresSecurityUnderlyingGraphStore(_fixture.Options);
        var governance = new PostgresSecurityGovernanceEventLog(_fixture.Options);
        var commitmentId = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await CreateAsync(service, commitmentId, "CommitmentGuarantee", new
        {
            counterparty = "Bank",
            committedAmount = 1_000_000m,
            effectiveDate = "2026-01-01",
            covenants = Array.Empty<object>()
        });

        // A reference is one edge whatever its case, so the second spelling amends the first; the
        // table keeps the first spelling and rounds the weight to ten decimals.
        var declared = await graph.DeclareAsync(new DeclareSecurityUnderlyingLinkRequest(commitmentId, SecurityUnderlyingRole.Collateral, null, "Pledged Receivables", 0.123456789012345m, from, null, null, "casey"));
        var amended = await graph.DeclareAsync(new DeclareSecurityUnderlyingLinkRequest(commitmentId, SecurityUnderlyingRole.Collateral, null, "PLEDGED RECEIVABLES", 0.123456789012345m, from, from.AddYears(1), null, "jordan"));

        declared.Reference.Should().Be("Pledged Receivables");
        declared.Weight.Should().Be(0.1234567890m);
        amended.LinkId.Should().Be(declared.LinkId);
        amended.Reference.Should().Be("Pledged Receivables", "the stored row keeps its first spelling");

        var stored = (await graph.GetUnderlyingsAsync(commitmentId)).Single(l => l.LinkId == declared.LinkId);
        amended.Should().BeEquivalentTo(stored);

        var events = await GraphEventsAsync(governance, commitmentId);
        events.Select(static e => e.EventType).Should().Equal("UnderlyingLinkDeclared", "UnderlyingLinkAmended");
        events[1].Payload.GetProperty("link").GetProperty("reference").GetString().Should().Be("Pledged Receivables");
        events[1].Payload.GetProperty("link").GetProperty("weight").GetDecimal().Should().Be(0.1234567890m);
        events[1].Payload.GetProperty("changes").EnumerateArray().Select(static c => c.GetString()).Should().Equal("validTo");
    }

    [SecurityMasterDatabaseFact]
    public async Task RemoveDeclared_RequiresAnActor()
    {
        var graph = new PostgresSecurityUnderlyingGraphStore(_fixture.Options);

        await graph.Invoking(g => g.RemoveDeclaredAsync(Guid.NewGuid(), Guid.NewGuid(), " "))
            .Should().ThrowAsync<ArgumentException>();
    }

    private static async Task<SecurityGovernanceEvent[]> GraphEventsAsync(PostgresSecurityGovernanceEventLog governance, Guid securityId)
        => (await governance.GetForSecurityAsync(securityId))
            .Where(static e => e.Stream == SecurityGovernanceStreams.UnderlyingGraph)
            .ToArray();

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

/// <summary>The declared-link amendment check, at the precision the graph table stores.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityUnderlyingLinkWriterTests
{
    [Fact]
    public void ChangedFields_ComparesAtTheStoredPrecision_OnBothSidesOfThePostgresEpoch()
    {
        var link = new SecurityUnderlyingLinkDto(Guid.NewGuid(), Guid.NewGuid(), SecurityUnderlyingRole.Collateral, Guid.NewGuid(), null, 0.5m,
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), null, SecurityUnderlyingLinkSource.Declared, null, "casey", DateTimeOffset.UnixEpoch);

        // Npgsql truncates toward 2000-01-01: an instant five ticks before it is stored as the epoch,
        // and one five ticks after it as well.
        SecurityUnderlyingLinkWriter.ChangedFields(link, link with { ValidFrom = link.ValidFrom.AddTicks(-5) }).Should().BeEmpty();
        SecurityUnderlyingLinkWriter.ChangedFields(link, link with { ValidFrom = link.ValidFrom.AddTicks(5) }).Should().BeEmpty();
        SecurityUnderlyingLinkWriter.ChangedFields(link, link with { Weight = 0.50000000004m }).Should().BeEmpty();
        SecurityUnderlyingLinkWriter.ChangedFields(link, link with { ValidFrom = link.ValidFrom.AddTicks(-10) }).Should().Equal("validFrom");
        SecurityUnderlyingLinkWriter.ChangedFields(link, link with { Weight = 0.6m, Notes = "rebalanced" }).Should().Equal("weight", "notes");
    }
}
