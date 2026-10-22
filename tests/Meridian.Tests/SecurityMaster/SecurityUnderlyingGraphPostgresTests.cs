using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

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

        (await graph.RemoveDeclaredAsync(noteId, a.LinkId, "graph-steward")).Should().BeTrue();
        (await graph.RemoveDeclaredAsync(noteId, a.LinkId, "graph-steward")).Should().BeFalse("already removed");
        var derived = links.Single(static l => l.Source == SecurityUnderlyingLinkSource.Derived);
        (await graph.RemoveDeclaredAsync(noteId, derived.LinkId, "graph-steward")).Should().BeFalse("derived links follow the terms");
        (await graph.GetUnderlyingsAsync(noteId)).Should().HaveCount(2);
        var events = (await new PostgresSecurityGovernanceEventLog(_fixture.Options).GetForSecurityAsync(noteId))
            .Where(static e => e.Stream == SecurityGovernanceStreams.UnderlyingLinks)
            .ToArray();
        events.Should().HaveCount(4, "the missing and derived-link removals make no change and append no event");
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


    [SecurityMasterDatabaseFact]
    public async Task DeclaredLinkChanges_RetainPreviousAndNewSnapshots_WithEachActor()
    {
        var (service, _) = Build();
        var graph = new PostgresSecurityUnderlyingGraphStore(_fixture.Options);
        var securityId = Guid.NewGuid();
        await CreateAsync(service, securityId, "Equity", new { shareClass = "Common" });
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var request = new DeclareSecurityUnderlyingLinkRequest(
            securityId, SecurityUnderlyingRole.BasketConstituent, Guid.NewGuid(), null,
            0.6m, from, from.AddYears(1), "initial weight", "alice");
        var initial = await graph.DeclareAsync(request);
        var revised = await graph.DeclareAsync(request with
        {
            Weight = 0.65m,
            ValidFrom = from.AddDays(1),
            ValidTo = from.AddYears(2),
            Notes = "rebalanced",
            CreatedBy = "bob"
        });

        (await graph.RemoveDeclaredAsync(Guid.NewGuid(), initial.LinkId, "wrong-security")).Should().BeFalse();
        (await graph.RemoveDeclaredAsync(securityId, initial.LinkId, "casey")).Should().BeTrue();
        (await graph.RemoveDeclaredAsync(securityId, initial.LinkId, "missing")).Should().BeFalse();

        var events = (await new PostgresSecurityGovernanceEventLog(_fixture.Options).GetForSecurityAsync(securityId))
            .Where(static e => e.Stream == SecurityGovernanceStreams.UnderlyingLinks)
            .ToArray();
        events.Select(static e => e.EventType).Should().Equal("DeclaredLinkCreated", "DeclaredLinkRedeclared", "DeclaredLinkRemoved");
        events.Select(static e => e.Actor).Should().Equal("alice", "bob", "casey");
        events[0].Payload.GetProperty("previous").ValueKind.Should().Be(JsonValueKind.Null);
        Snapshot(events[0], "new").Should().Be(initial);
        Snapshot(events[1], "previous").Should().Be(initial);
        Snapshot(events[1], "new").Should().Be(revised);
        Snapshot(events[2], "previous").Should().Be(revised);
        events[2].Payload.GetProperty("new").ValueKind.Should().Be(JsonValueKind.Null);
        (await graph.GetUnderlyingsAsync(securityId)).Should().BeEmpty();
    }

    [SecurityMasterDatabaseFact]
    public async Task ConcurrentDeclarations_WithSchemaCaseVariants_RetainOneCommittedAuditChain()
    {
        var (service, _) = Build();
        var graph = new PostgresSecurityUnderlyingGraphStore(_fixture.Options);
        var alternateCase = new PostgresSecurityUnderlyingGraphStore(new SecurityMasterOptions
        {
            ConnectionString = _fixture.Options.ConnectionString,
            Schema = _fixture.Options.Schema.ToUpperInvariant()
        });
        var securityId = Guid.NewGuid();
        await CreateAsync(service, securityId, "Equity", new { shareClass = "Common" });
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var target = Guid.NewGuid();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = Enumerable.Range(1, 8).Select(async i =>
        {
            await start.Task;
            var writer = i % 2 == 0 ? graph : alternateCase;
            return await writer.DeclareAsync(new DeclareSecurityUnderlyingLinkRequest(
                securityId, SecurityUnderlyingRole.BasketConstituent, target, null,
                i / 10m, from.AddDays(i), from.AddYears(1), $"revision {i}", $"writer-{i}"));
        }).ToArray();
        start.SetResult();
        await Task.WhenAll(writes);

        var events = (await new PostgresSecurityGovernanceEventLog(_fixture.Options).GetForSecurityAsync(securityId))
            .Where(static e => e.Stream == SecurityGovernanceStreams.UnderlyingLinks)
            .ToArray();
        events.Should().HaveCount(writes.Length);
        events[0].EventType.Should().Be("DeclaredLinkCreated");
        events[0].Payload.GetProperty("previous").ValueKind.Should().Be(JsonValueKind.Null);
        for (var i = 1; i < events.Length; i++)
        {
            events[i].EventType.Should().Be("DeclaredLinkRedeclared");
            Snapshot(events[i], "previous").Should().Be(Snapshot(events[i - 1], "new"));
        }

        foreach (var recorded in events)
        {
            recorded.Actor.Should().Be(Snapshot(recorded, "new").CreatedBy);
        }

        (await graph.GetUnderlyingsAsync(securityId)).Should().ContainSingle()
            .Which.Should().Be(Snapshot(events[^1], "new"));
    }

    [SecurityMasterDatabaseFact]
    public async Task WhenGovernanceAppendFails_CreateRedeclareAndRemove_AllRollBack()
    {
        var (service, _) = Build();
        var graph = new PostgresSecurityUnderlyingGraphStore(_fixture.Options);
        var securityId = Guid.NewGuid();
        await CreateAsync(service, securityId, "Equity", new { shareClass = "Common" });
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var request = new DeclareSecurityUnderlyingLinkRequest(
            securityId, SecurityUnderlyingRole.BasketConstituent, Guid.NewGuid(), null,
            0.6m, from, from.AddYears(1), "retained", "alice");
        var retained = await graph.DeclareAsync(request);
        var guard = "reject_underlying_event_" + securityId.ToString("N");
        await ExecuteSqlAsync(
            $"""
            create function {_fixture.Options.Schema}.{guard}() returns trigger language plpgsql as $audit$
            begin
                if new.security_id = '{securityId:D}'::uuid and new.stream = 'UnderlyingLinks' then
                    raise exception 'underlying audit unavailable';
                end if;
                return new;
            end;
            $audit$;
            create trigger {guard} before insert on {_fixture.Options.Schema}.security_governance_events
                for each row execute function {_fixture.Options.Schema}.{guard}();
            """);
        try
        {
            var create = () => graph.DeclareAsync(request with { UnderlyingSecurityId = Guid.NewGuid(), CreatedBy = "create" });
            await create.Should().ThrowAsync<PostgresException>().WithMessage("*underlying audit unavailable*");
            var redeclare = () => graph.DeclareAsync(request with { Weight = 0.8m, Notes = "must roll back", CreatedBy = "redeclare" });
            await redeclare.Should().ThrowAsync<PostgresException>().WithMessage("*underlying audit unavailable*");
            var remove = () => graph.RemoveDeclaredAsync(securityId, retained.LinkId, "remove");
            await remove.Should().ThrowAsync<PostgresException>().WithMessage("*underlying audit unavailable*");

            (await graph.GetUnderlyingsAsync(securityId)).Should().ContainSingle().Which.Should().Be(retained);
            var events = (await new PostgresSecurityGovernanceEventLog(_fixture.Options).GetForSecurityAsync(securityId))
                .Where(static e => e.Stream == SecurityGovernanceStreams.UnderlyingLinks);
            events.Should().ContainSingle().Which.Actor.Should().Be("alice");
        }
        finally
        {
            await ExecuteSqlAsync(
                $"""
                drop trigger if exists {guard} on {_fixture.Options.Schema}.security_governance_events;
                drop function if exists {_fixture.Options.Schema}.{guard}();
                """);
        }
    }


    [SecurityMasterDatabaseFact]
    public async Task UnderlyingGovernanceMigration_PreservesPriorStreamsAndGuards_AndRollsBackFailedUpgrade()
    {
        var schema = $"sm_underlying_upgrade_{Guid.NewGuid():N}";
        async Task<string> MigrationAsync(string file)
            => (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SecurityMaster", "Migrations", file)))
                .Replace("__SCHEMA__", schema, StringComparison.Ordinal);

        await ExecuteSqlAsync($"create schema {schema};");
        try
        {
            await ExecuteSqlAsync(await MigrationAsync("040_security_master_governance_events.sql"));
            await ExecuteSqlAsync(await MigrationAsync("041_security_master_governance_events_truncate_guard.sql"));
            await ExecuteSqlAsync(
                $"""
                insert into {schema}.security_governance_events (stream, event_type, actor, occurred_at, payload)
                values ('OperatorOverrides', 'retained', 'original', '2026-01-01T00:00:00Z', jsonb_build_object('retained', 1)),
                       ('Aliases', 'retained', 'original', '2026-01-01T00:00:00Z', jsonb_build_object('retained', 1)),
                       ('ProjectionRebuild', 'retained', 'original', '2026-01-01T00:00:00Z', jsonb_build_object('retained', 1));
                """);
            var migration = await MigrationAsync("050_security_underlying_link_governance.sql");
            await using (var connection = new NpgsqlConnection(_fixture.Options.ConnectionString))
            {
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                await using var command = new NpgsqlCommand(migration + "\nselect 1 / 0;", connection, transaction);
                var fail = () => command.ExecuteNonQueryAsync();
                await fail.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "22012");
                await transaction.RollbackAsync();
            }

            var insertUnderlying =
                $"""
                insert into {schema}.security_governance_events (stream, event_type, actor, occurred_at, payload)
                values ('UnderlyingLinks', 'new', 'reviewer', now(), jsonb_build_object('new', true));
                """;
            var beforeUpgrade = () => ExecuteSqlAsync(insertUnderlying);
            await beforeUpgrade.Should().ThrowAsync<PostgresException>()
                .Where(e => e.SqlState == PostgresErrorCodes.CheckViolation
                            && e.ConstraintName == "security_governance_events_stream_check");

            await ExecuteSqlAsync(migration);
            await ExecuteSqlAsync(insertUnderlying);
            await using (var connection = new NpgsqlConnection(_fixture.Options.ConnectionString))
            {
                await connection.OpenAsync();
                await using var retained = new NpgsqlCommand(
                    $"""
                    select count(*) from {schema}.security_governance_events
                    where stream in ('OperatorOverrides', 'Aliases', 'ProjectionRebuild')
                      and event_type = 'retained' and actor = 'original'
                      and occurred_at = '2026-01-01T00:00:00Z'::timestamptz and payload = jsonb_build_object('retained', 1)::jsonb;
                    """, connection);
                ((long)(await retained.ExecuteScalarAsync())!).Should().Be(3);
            }

            var invalidStream = () => ExecuteSqlAsync(insertUnderlying.Replace("'UnderlyingLinks'", "'UnknownStream'", StringComparison.Ordinal));
            await invalidStream.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.CheckViolation);
            foreach (var mutation in new[]
                     {
                         $"update {schema}.security_governance_events set actor = 'rewrite' where stream = 'UnderlyingLinks';",
                         $"delete from {schema}.security_governance_events where stream = 'UnderlyingLinks';",
                         $"truncate {schema}.security_governance_events;"
                     })
            {
                var mutate = () => ExecuteSqlAsync(mutation);
                await mutate.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.CheckViolation);
            }
        }
        finally
        {
            await ExecuteSqlAsync($"drop schema if exists {schema} cascade;");
        }
    }

    private static SecurityUnderlyingLinkDto Snapshot(SecurityGovernanceEvent recorded, string property)
        => recorded.Payload.GetProperty(property).Deserialize<SecurityUnderlyingLinkDto>()!;

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
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
