using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// PostgreSQL coverage for the recorded identifier history (migration 062): projection replacement
/// appends Added/Amended/Expired/Removed revisions in its own transaction, keeps row-level
/// source/confidence/manual-override for unchanged identities, and the history is append-only and
/// readable at explicit recorded and valid cutoffs.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityIdentifierHistoryPostgresTests(SecurityMasterDatabaseFixture fixture)
    : IClassFixture<SecurityMasterDatabaseFixture>
{
    private static readonly DateTimeOffset ValidFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [SecurityMasterDatabaseFact]
    public async Task Replacement_AppendsAddedRevisions_AndAnUnchangedReplayAppendsNothing()
    {
        var store = Store();
        var id = Guid.NewGuid();
        var projection = Projection(id, 1, Ticker(id), Isin(id));

        await store.UpsertProjectionAsync(projection);
        var first = await store.GetIdentifierRevisionsAsync(id, Later());
        first.Should().HaveCount(2);
        first.Should().OnlyContain(revision => revision.ChangeKind == SecurityIdentifierRevisionKinds.Added
                                               && revision.Revision == 1
                                               && revision.RecordedBy == PostgresSecurityMasterStore.ProjectionReplacementIdentifierActor
                                               && revision.Source == "SecurityMaster"
                                               && revision.HistoryAvailableFrom == null);

        // Same identifiers at a newer projection version: the rows are rewritten, the history is not.
        await store.UpsertProjectionAsync(projection with { Version = 2 });
        (await store.GetIdentifierRevisionsAsync(id, Later())).Should().HaveCount(2);
    }

    [SecurityMasterDatabaseFact]
    public async Task Replacement_KeepsSourceConfidenceAndManualOverride_ForAnUnchangedIdentity()
    {
        var store = Store();
        var id = Guid.NewGuid();
        await store.UpsertProjectionAsync(Projection(id, 1, Ticker(id)));
        await ExecuteAsync(
            $"update {Schema}.security_identifiers set source = 'OperatorDesk', confidence = 0.8750, manual_override = true where security_id = @id",
            id);

        await store.UpsertProjectionAsync(Projection(id, 2, Ticker(id), Isin(id)));

        var rows = await ReadCurrentRowsAsync(id);
        rows.Should().ContainSingle(row => row.Kind == "Ticker")
            .Which.Should().Be(("Ticker", "OperatorDesk", (decimal?)0.8750m, true),
                "the projection record carries no row metadata, so an unchanged identity keeps what was stored");
        rows.Should().ContainSingle(row => row.Kind == "Isin")
            .Which.Should().Be(("Isin", "SecurityMaster", (decimal?)null, false));

        var ticker = (await store.GetIdentifierRevisionsAsync(id, Later()))
            .Where(revision => revision.IdentifierKind == "Ticker").ToArray();
        ticker.Select(revision => revision.ChangeKind).Should().Equal(
            SecurityIdentifierRevisionKinds.Added, SecurityIdentifierRevisionKinds.Amended);
        ticker[1].Should().Match<SecurityIdentifierRevision>(revision =>
            revision.Source == "OperatorDesk" && revision.Confidence == 0.8750m && revision.ManualOverride);
    }

    [SecurityMasterDatabaseFact]
    public async Task History_RecordsExpiryRemovalAndReAddition_AndAnswersExplicitRecordedAndValidCutoffs()
    {
        var store = Store();
        var id = Guid.NewGuid();
        var expiry = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);

        await store.UpsertProjectionAsync(Projection(id, 1, Ticker(id), Isin(id)));
        var afterCreate = await DatabaseTimestampAsync();
        await store.UpsertProjectionAsync(Projection(id, 2, Ticker(id) with { ValidTo = expiry }));
        var afterAmend = await DatabaseTimestampAsync();
        await store.UpsertProjectionAsync(Projection(id, 3, Ticker(id) with { ValidTo = expiry }, Isin(id)));

        var history = await store.GetIdentifierRevisionsAsync(id, Later());
        history.Where(revision => revision.IdentifierKind == "Ticker").Select(revision => revision.ChangeKind)
            .Should().Equal(SecurityIdentifierRevisionKinds.Added, SecurityIdentifierRevisionKinds.Expired);
        var isin = history.Where(revision => revision.IdentifierKind == "Isin").ToArray();
        isin.Select(revision => revision.ChangeKind).Should().Equal(
            SecurityIdentifierRevisionKinds.Added, SecurityIdentifierRevisionKinds.Removed, SecurityIdentifierRevisionKinds.Added);
        isin.Select(revision => revision.Revision).Should().Equal(1, 2, 3);
        isin.Select(revision => revision.RecordedAt).Should().BeInAscendingOrder();

        // Recorded cutoffs select what the projection held then; a later correction cannot change them.
        var asCreated = await store.GetIdentifiersRecordedAsOfAsync(id, afterCreate);
        asCreated.IsHistoryAvailable.Should().BeTrue();
        asCreated.Identifiers.Select(revision => (revision.IdentifierKind, revision.ValidTo))
            .Should().BeEquivalentTo(new[] { ("Isin", (DateTimeOffset?)null), ("Ticker", (DateTimeOffset?)null) });
        var asAmended = await store.GetIdentifiersRecordedAsOfAsync(id, afterAmend);
        asAmended.Identifiers.Should().ContainSingle().Which.ValidTo.Should().Be(expiry);
        (await store.GetIdentifierRevisionsAsync(id, afterCreate)).Should().HaveCount(2, "the recorded cutoff bounds the audit trail too");
        (await store.GetIdentifiersRecordedAsOfAsync(id, afterCreate.AddDays(-1))).Identifiers.Should().BeEmpty();

        // Valid time is a separate filter over the same recorded view.
        var current = Later();
        (await store.GetIdentifiersRecordedAsOfAsync(id, current, validAsOfUtc: expiry.AddDays(-1))).Identifiers
            .Select(revision => revision.IdentifierKind).Should().BeEquivalentTo("Isin", "Ticker");
        (await store.GetIdentifiersRecordedAsOfAsync(id, current, validAsOfUtc: expiry)).Identifiers
            .Select(revision => revision.IdentifierKind).Should().Equal("Isin");
        (await store.GetIdentifiersRecordedAsOfAsync(id, afterAmend, validAsOfUtc: expiry.AddDays(-1))).Identifiers
            .Select(revision => revision.IdentifierKind).Should().Equal("Ticker");
    }

    [SecurityMasterDatabaseFact]
    public async Task IdentifierRevisions_RefuseUpdateDeleteAndTruncate()
    {
        var store = Store();
        var id = Guid.NewGuid();
        await store.UpsertProjectionAsync(Projection(id, 1, Ticker(id)));

        foreach (var mutation in new[]
                 {
                     $"update {Schema}.security_identifier_revisions set recorded_by = 'rewrite' where security_id = @id",
                     $"delete from {Schema}.security_identifier_revisions where security_id = @id",
                     $"truncate {Schema}.security_identifier_revisions"
                 })
        {
            var act = () => ExecuteAsync(mutation, id);
            await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514", mutation);
        }

        (await store.GetIdentifierRevisionsAsync(id, Later())).Should().ContainSingle()
            .Which.RecordedBy.Should().Be(PostgresSecurityMasterStore.ProjectionReplacementIdentifierActor);
    }

    [SecurityMasterDatabaseFact]
    public async Task RolledBackReplacement_LeavesNeitherRowsNorHistory()
    {
        var store = Store();
        var id = Guid.NewGuid();
        await store.UpsertProjectionAsync(Projection(id, 1, Ticker(id)));

        var act = () => PostgresSecurityMasterMutation.ExecuteCanonicalAsync<int>(fixture.Options, id, async () =>
        {
            await store.UpsertProjectionAsync(Projection(id, 2, Isin(id)));
            throw new InvalidOperationException("abort after the replacement");
        });
        await act.Should().ThrowAsync<InvalidOperationException>();

        (await ReadCurrentRowsAsync(id)).Select(row => row.Kind).Should().Equal("Ticker");
        (await store.GetIdentifierRevisionsAsync(id, Later())).Should().ContainSingle()
            .Which.IdentifierKind.Should().Be("Ticker");
    }

    [SecurityMasterDatabaseFact]
    public async Task ConcurrentReplacements_SerializeOnTheSecurity_AndHistoryEndsAtTheCurrentRows()
    {
        var id = Guid.NewGuid();
        await Store().UpsertProjectionAsync(Projection(id, 1, Ticker(id)));

        var writes = Enumerable.Range(2, 8).Select(version => Task.Run(() => Store().UpsertProjectionAsync(
            Projection(id, version, version % 2 == 0 ? new[] { Ticker(id), Isin(id) } : new[] { Ticker(id) with { Provider = $"P{version % 3}" } }))));
        await Task.WhenAll(writes);

        var store = Store();
        var current = await ReadCurrentRowsAsync(id);
        var recorded = await store.GetIdentifiersRecordedAsOfAsync(id, Later());
        recorded.Identifiers.Select(revision => revision.IdentifierKind).Should().BeEquivalentTo(current.Select(row => row.Kind));

        var history = await store.GetIdentifierRevisionsAsync(id, Later());
        foreach (var identity in history.GroupBy(revision => (revision.IdentifierKind, revision.IdentifierValue, revision.ValidFrom)))
        {
            identity.Select(revision => revision.Revision).Should().Equal(Enumerable.Range(1, identity.Count()),
                "serialized replacements number each identity's revisions without gaps or duplicates");
            identity.Select(revision => revision.RecordedAt).Should().BeInAscendingOrder();
        }
    }

    [SecurityMasterDatabaseFact]
    public async Task MigratedBaseline_IsUnavailableBeforeItsBoundary_AndDoesNotBackdateHistory()
    {
        var store = Store();
        var id = Guid.NewGuid();
        await store.UpsertProjectionAsync(Projection(id, 1, Ticker(id)));
        // Reproduce migration 062's baseline for a row that predates history: its earlier history is unknown.
        var boundary = await DatabaseTimestampAsync();
        await ExecuteAsync(
            $"""
            insert into {Schema}.security_identifier_revisions (
                security_id, identifier_kind, identifier_value, valid_from, revision, change_kind,
                normalized_identifier_value, provider, normalized_provider, is_primary, valid_to,
                source, confidence, manual_override, recorded_by, recorded_at, history_available_from)
            values (@id, 'Cusip', 'BASELINE01', '2020-01-01T00:00:00Z', 1, 'Baseline', 'BASELINE01', null, null,
                    false, null, 'LegacyFeed', null, false, 'security-master.migration-062', @boundary, @boundary);
            """,
            id,
            ("boundary", boundary.UtcDateTime));

        var before = await store.GetIdentifiersRecordedAsOfAsync(id, boundary.AddTicks(-10));
        before.IsHistoryAvailable.Should().BeFalse();
        before.Identifiers.Select(revision => revision.IdentifierKind).Should().Equal("Ticker");
        var after = await store.GetIdentifiersRecordedAsOfAsync(id, Later());
        after.IsHistoryAvailable.Should().BeTrue();
        after.Identifiers.Select(revision => revision.IdentifierKind).Should().BeEquivalentTo("Cusip", "Ticker");
    }

    [SecurityMasterDatabaseFact]
    public async Task CanonicalCreate_CommitsEventProjectionAndIdentifierHistoryTogether()
    {
        var created = await CreateEquityAsync(new PostgresSecurityFieldProvenanceStore(fixture.Options));
        (await new PostgresSecurityMasterEventStore(fixture.Options).LoadAsync(created)).Should().ContainSingle();
        (await Store().GetProjectionAsync(created)).Should().NotBeNull();
        (await Store().GetIdentifierRevisionsAsync(created, Later())).Should().ContainSingle()
            .Which.ChangeKind.Should().Be(SecurityIdentifierRevisionKinds.Added);

        var failed = Guid.NewGuid();
        var act = () => CreateEquityAsync(new FailingProvenanceStore(), failed);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*attribution unavailable*");
        (await new PostgresSecurityMasterEventStore(fixture.Options).LoadAsync(failed)).Should().BeEmpty();
        (await Store().GetProjectionAsync(failed)).Should().BeNull();
        (await Store().GetIdentifierRevisionsAsync(failed, Later())).Should().BeEmpty(
            "the identifier history rolls back with the event and the projection");
    }

    private string Schema => fixture.Options.Schema;

    private PostgresSecurityMasterStore Store() => new(fixture.Options);

    private static DateTimeOffset Later() => DateTimeOffset.UtcNow.AddMinutes(5);

    private static SecurityIdentifierDto Ticker(Guid id)
        => new(SecurityIdentifierKind.Ticker, $"HIST-{id:N}", true, ValidFrom);

    private static SecurityIdentifierDto Isin(Guid id)
        => new(SecurityIdentifierKind.Isin, $"US{id.ToString("N")[..10].ToUpperInvariant()}", false, ValidFrom);

    private static SecurityProjectionRecord Projection(Guid id, long version, params SecurityIdentifierDto[] identifiers)
        => SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Equity",
            JsonSerializer.SerializeToElement(new { schemaVersion = 1, classification = "Common", shareClass = "A" })) with
        {
            SecurityId = id,
            PrimaryIdentifierKind = "Ticker",
            PrimaryIdentifierValue = $"HIST-{id:N}",
            Identifiers = identifiers,
            Version = version
        };

    private async Task ExecuteAsync(string sql, Guid id, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyList<(string Kind, string Source, decimal? Confidence, bool ManualOverride)>> ReadCurrentRowsAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"select identifier_kind, source, confidence, manual_override from {Schema}.security_identifiers where security_id = @id order by identifier_kind",
            connection);
        command.Parameters.AddWithValue("id", id);
        var rows = new List<(string, string, decimal?, bool)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetDecimal(2), reader.GetBoolean(3)));
        return rows;
    }

    private async Task<DateTimeOffset> DatabaseTimestampAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select clock_timestamp();", connection);
        return new DateTimeOffset((DateTime)(await command.ExecuteScalarAsync())!);
    }

    private async Task<Guid> CreateEquityAsync(ISecurityFieldProvenanceStore provenance, Guid? id = null)
    {
        var securityId = id ?? Guid.NewGuid();
        var eventStore = new PostgresSecurityMasterEventStore(fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(fixture.Options);
        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            Store(),
            new SecurityMasterAggregateRebuilder(eventStore, snapshotStore),
            fixture.Options,
            NullLogger<SecurityMasterService>.Instance,
            fieldProvenance: provenance,
            canonicalWriteScope: new PostgresSecurityMasterCanonicalWriteScope(fixture.Options));

        await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "Equity",
            JsonSerializer.SerializeToElement(new { displayName = "History Equity", currency = "USD", issuerName = "History Corp" }),
            JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "IDH" + securityId.ToString("N")[..8].ToUpperInvariant(), true, DateTimeOffset.UtcNow.AddDays(-1))],
            DateTimeOffset.UtcNow,
            "tests",
            "identifier-history-tests",
            null,
            "identifier history coverage"));
        return securityId;
    }

    private sealed class FailingProvenanceStore : ISecurityFieldProvenanceStore
    {
        public Task UpsertAsync(SecurityFieldProvenanceRecord record, CancellationToken ct = default)
            => throw new InvalidOperationException("attribution unavailable");

        public Task RemoveAsync(Guid securityId, string fieldPath, string origin, DateTimeOffset clearedAt, long? maxSourceVersion = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<SecurityFieldProvenanceRecord>> GetAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityFieldProvenanceRecord>>([]);
    }
}
