using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// PostgreSQL coverage for the governance audit work: operator overrides, aliases and rebuilds
/// append to the append-only governance log in the same transaction as the change; a reviewer
/// cannot approve values they staged; and a canonical write (event, projection, field
/// attribution) commits atomically under the canonical write scope.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityMasterGovernanceEventsPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterGovernanceEventsPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task OverridePatchAndDecision_AppendGovernanceEvents_AndSelfApprovalIsRefused()
    {
        var securityId = await CreateEquityAsync(new PostgresSecurityFieldProvenanceStore(_fixture.Options));
        var overrides = new PostgresOperatorOverridesStore(_fixture.Options);

        await overrides.PatchAsync(
            securityId,
            new OperatorOverridesPatchRequest(new Dictionary<string, string> { ["sector"] = "Technology" }, null),
            "editor");

        var selfApproval = () => overrides.RecordApprovalDecisionAsync(
            securityId, new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, "EDITOR "));
        await selfApproval.Should().ThrowAsync<OperatorOverrideSelfApprovalException>();

        var decided = await overrides.RecordApprovalDecisionAsync(
            securityId, new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, "reviewer", "checked"));
        decided.ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.Approved);

        var events = (await new PostgresSecurityGovernanceEventLog(_fixture.Options).GetForSecurityAsync(securityId))
            .Where(static e => e.Stream == SecurityGovernanceStreams.OperatorOverrides)
            .ToArray();
        events.Select(static e => e.EventType).Should().Equal("Patched", "Approved");
        events[0].Actor.Should().Be("editor");
        events[0].Payload.GetProperty("values").GetProperty("sector").GetString().Should().Be("Technology");
        events[1].Actor.Should().Be("reviewer");
        events[1].Payload.GetProperty("stagedBy").GetString().Should().Be("editor");
    }

    [SecurityMasterDatabaseFact]
    public async Task OverrideSelfRejection_IsAllowed()
    {
        var securityId = await CreateEquityAsync(new PostgresSecurityFieldProvenanceStore(_fixture.Options));
        var overrides = new PostgresOperatorOverridesStore(_fixture.Options);
        await overrides.PatchAsync(
            securityId,
            new OperatorOverridesPatchRequest(new Dictionary<string, string> { ["sector"] = "Energy" }, null),
            "editor");

        var withdrawn = await overrides.RecordApprovalDecisionAsync(
            securityId, new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Rejected, "editor", "withdrawn"));

        withdrawn.ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.Rejected);
    }

    [SecurityMasterDatabaseFact]
    public async Task AliasUpsert_AppendsAGovernanceEvent()
    {
        var securityId = await CreateEquityAsync(new PostgresSecurityFieldProvenanceStore(_fixture.Options));
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var now = DateTimeOffset.UtcNow;

        await store.UpsertAliasAsync(new SecurityAliasDto(
            Guid.NewGuid(), securityId, "VendorCode", "VND-" + securityId.ToString("N")[..8], "vendor",
            SecurityAliasScope.Operations, "vendor mapping", "alias-steward", now, now, null, true));

        var events = await new PostgresSecurityGovernanceEventLog(_fixture.Options).GetForSecurityAsync(securityId);
        events.Should().Contain(e => e.Stream == SecurityGovernanceStreams.Aliases
                                     && e.EventType == "AliasRevisionRecorded"
                                     && e.Actor == "alias-steward");
    }

    [SecurityMasterDatabaseFact]
    public async Task GovernanceEvents_AreAppendOnly()
    {
        var securityId = await CreateEquityAsync(new PostgresSecurityFieldProvenanceStore(_fixture.Options));
        await new PostgresOperatorOverridesStore(_fixture.Options).PatchAsync(
            securityId,
            new OperatorOverridesPatchRequest(new Dictionary<string, string> { ["sector"] = "Utilities" }, null),
            "editor");

        foreach (var mutation in new[]
                 {
                     $"update {_fixture.Options.Schema}.security_governance_events set actor = 'rewrite' where security_id = @id",
                     $"delete from {_fixture.Options.Schema}.security_governance_events where security_id = @id",
                     $"truncate {_fixture.Options.Schema}.security_governance_events"
                 })
        {
            await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(mutation, connection);
            command.Parameters.AddWithValue("id", securityId);
            var act = () => command.ExecuteNonQueryAsync();
            await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514", mutation);
        }

        (await new PostgresSecurityGovernanceEventLog(_fixture.Options).GetForSecurityAsync(securityId))
            .Should().NotBeEmpty();
    }

    [SecurityMasterDatabaseFact]
    public async Task CanonicalCreate_WithAtomicScope_RecordsAttributionInTheSameCommit()
    {
        var securityId = await CreateEquityAsync(new PostgresSecurityFieldProvenanceStore(_fixture.Options));

        var attribution = await new PostgresSecurityFieldProvenanceStore(_fixture.Options).GetAsync(securityId);
        attribution.Should().Contain(record => record.Origin == SecurityFieldProvenanceOrigins.CanonicalWrite);
        (await new PostgresSecurityMasterEventStore(_fixture.Options).LoadAsync(securityId)).Should().ContainSingle();
    }

    [SecurityMasterDatabaseFact]
    public async Task CanonicalCreate_WhenAttributionFails_RollsBackTheEventAndProjection()
    {
        var securityId = Guid.NewGuid();
        var create = () => CreateEquityAsync(new FailingProvenanceStore(), securityId);

        await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("*attribution unavailable*");

        (await new PostgresSecurityMasterEventStore(_fixture.Options).LoadAsync(securityId))
            .Should().BeEmpty("the event append must roll back with the failed attribution");
        (await new PostgresSecurityMasterStore(_fixture.Options).GetProjectionAsync(securityId))
            .Should().BeNull("the projection upsert must roll back with the failed attribution");
    }

    private async Task<Guid> CreateEquityAsync(ISecurityFieldProvenanceStore provenance, Guid? id = null)
    {
        var securityId = id ?? Guid.NewGuid();
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            new SecurityMasterAggregateRebuilder(eventStore, snapshotStore),
            _fixture.Options,
            NullLogger<SecurityMasterService>.Instance,
            fieldProvenance: provenance,
            canonicalWriteScope: new PostgresSecurityMasterCanonicalWriteScope(_fixture.Options));

        var ticker = "GOV" + securityId.ToString("N")[..8].ToUpperInvariant();
        await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "Equity",
            JsonSerializer.SerializeToElement(new { displayName = "Governance Equity", currency = "USD", issuerName = "Governance Corp" }),
            JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, DateTimeOffset.UtcNow.AddDays(-1))],
            DateTimeOffset.UtcNow,
            "tests",
            "governance-tests",
            null,
            "governance coverage"));
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
