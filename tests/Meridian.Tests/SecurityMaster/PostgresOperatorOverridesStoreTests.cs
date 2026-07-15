using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Round-trip coverage for the durable operator-override approval/audit trail (Wave 1 / Track C).
///
/// The store must PERSIST and REHYDRATE the full approval surface that <see cref="OperatorOverridesDto"/>
/// advertises — <c>ApprovalStatus</c>, <c>ReasonCode</c>, <c>ReviewedBy</c>, <c>ReviewedAt</c>, and the
/// append-only <c>AuditTrail</c> — plus the new <c>RecordApprovalDecisionAsync</c> reviewer mutation.
///
/// STATUS: these tests are the executable specification for Track C and are RED until it is implemented
/// (the store today writes only <c>values/updated_by/updated_at</c>, drops the approval fields on read,
/// and has no decision mutation). They will compile and pass once:
///   • the <c>security_operator_overrides</c> table gains approval_status / reason_code / reviewed_by /
///     reviewed_at / audit_trail columns (SecurityMasterMigrationRunner),
///   • GetAsync/PatchAsync persist and rehydrate them, and
///   • IOperatorOverridesStore.RecordApprovalDecisionAsync is added.
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(SecurityMasterDatabaseCollection))]
public sealed class PostgresOperatorOverridesStoreTests
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public PostgresOperatorOverridesStoreTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private PostgresOperatorOverridesStore NewStore() => new(_fixture.Options);

    private static OperatorOverridesPatchRequest Patch(string key, string value, string reasonCode)
        => new(
            SetValues: new Dictionary<string, string>(StringComparer.Ordinal) { [key] = value },
            RemoveKeys: null)
        {
            ReasonCode = reasonCode
        };

    [SecurityMasterDatabaseFact]
    public async Task PatchAsync_ThenGetAsync_PersistsPendingStatusAndAuditEntry()
    {
        var store = NewStore();
        var securityId = Guid.NewGuid();

        await store.PatchAsync(securityId, Patch("rating", "AA", "annotation-correction"), "operator-1");

        var loaded = await store.GetAsync(securityId);
        loaded.Should().NotBeNull();
        loaded!.ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.Pending);
        loaded.ReasonCode.Should().Be("annotation-correction");
        loaded.ReviewedBy.Should().BeNull();
        loaded.ReviewedAt.Should().BeNull();
        loaded.Values.Should().Contain(new KeyValuePair<string, string>("rating", "AA"));

        loaded.AuditTrail.Should().ContainSingle();
        loaded.AuditTrail[0].EventType.Should().Be("Patched");
        loaded.AuditTrail[0].Actor.Should().Be("operator-1");
        loaded.AuditTrail[0].ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.Pending);
    }

    [SecurityMasterDatabaseFact]
    public async Task PatchAsync_AfterApproval_ResetsToPendingAndKeepsPriorAudit()
    {
        var store = NewStore();
        var securityId = Guid.NewGuid();

        await store.PatchAsync(securityId, Patch("rating", "AA", "initial"), "operator-1");
        await store.RecordApprovalDecisionAsync(
            securityId,
            new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, "reviewer-1", "signed off"));

        // A value change after approval must invalidate the sign-off and re-open review.
        await store.PatchAsync(securityId, Patch("rating", "BBB", "revised"), "operator-1");

        var loaded = await store.GetAsync(securityId);
        loaded!.ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.Pending);
        loaded.ReviewedBy.Should().BeNull("a re-patch clears the prior reviewer decision");
        loaded.ReviewedAt.Should().BeNull();
        loaded.Values.Should().Contain(new KeyValuePair<string, string>("rating", "BBB"));

        // The trail is append-only: prior entries are preserved in chronological order.
        loaded.AuditTrail.Select(entry => entry.EventType).Should().ContainInOrder("Patched", "Approved", "Patched");
    }

    [SecurityMasterDatabaseFact]
    public async Task RecordApprovalDecisionAsync_Approved_StampsReviewerAndReviewedAt()
    {
        var store = NewStore();
        var securityId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await store.PatchAsync(securityId, Patch("sector", "Tech", "reclassification"), "operator-1");

        var result = await store.RecordApprovalDecisionAsync(
            securityId,
            new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, "reviewer-1", "looks correct"));

        result.ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.Approved);
        result.ReviewedBy.Should().Be("reviewer-1");
        result.ReviewedAt.Should().NotBeNull();
        result.ReviewedAt!.Value.Should().BeOnOrAfter(before);

        // Durable across a fresh read.
        var loaded = await store.GetAsync(securityId);
        loaded!.ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.Approved);
        loaded.ReviewedBy.Should().Be("reviewer-1");
        var decisionEntry = loaded.AuditTrail.Last();
        decisionEntry.EventType.Should().Be("Approved");
        decisionEntry.Reviewer.Should().Be("reviewer-1");
        decisionEntry.ReviewedAt.Should().NotBeNull();
        decisionEntry.Comment.Should().Be("looks correct");
    }

    [SecurityMasterDatabaseFact]
    public async Task RecordApprovalDecisionAsync_Rejected_StampsReviewerAndAppendsRejectedAudit()
    {
        var store = NewStore();
        var securityId = Guid.NewGuid();

        await store.PatchAsync(securityId, Patch("rating", "AA", "correction"), "operator-1");

        var result = await store.RecordApprovalDecisionAsync(
            securityId,
            new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Rejected, "reviewer-2", "insufficient evidence"));

        result.ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.Rejected);
        result.ReviewedBy.Should().Be("reviewer-2");

        var loaded = await store.GetAsync(securityId);
        loaded!.ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.Rejected);
        loaded.AuditTrail.Last().EventType.Should().Be("Rejected");
        loaded.AuditTrail.Last().Reviewer.Should().Be("reviewer-2");
    }

    [SecurityMasterDatabaseFact]
    public async Task RecordApprovalDecisionAsync_MissingRow_ThrowsInvalidOperation()
    {
        var store = NewStore();

        var act = async () => await store.RecordApprovalDecisionAsync(
            Guid.NewGuid(),
            new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, "reviewer-1"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [SecurityMasterDatabaseFact]
    public async Task RecordApprovalDecisionAsync_NonPending_ThrowsInvalidOperation()
    {
        var store = NewStore();
        var securityId = Guid.NewGuid();

        await store.PatchAsync(securityId, Patch("rating", "AA", "correction"), "operator-1");
        await store.RecordApprovalDecisionAsync(
            securityId,
            new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, "reviewer-1"));

        // A second decision on an already-decided (non-Pending) overlay is rejected.
        var act = async () => await store.RecordApprovalDecisionAsync(
            securityId,
            new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Rejected, "reviewer-2"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [SecurityMasterDatabaseFact]
    public async Task RecordApprovalDecisionAsync_InvalidDecision_ThrowsArgument()
    {
        var store = NewStore();
        var securityId = Guid.NewGuid();

        await store.PatchAsync(securityId, Patch("rating", "AA", "correction"), "operator-1");

        // A decision must be Approved or Rejected — Pending / NotRequested are not decisions.
        var act = async () => await store.RecordApprovalDecisionAsync(
            securityId,
            new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Pending, "reviewer-1"));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [SecurityMasterDatabaseFact]
    public async Task GetAsync_LegacyRowMissingApprovalColumns_DefaultsToNotRequested()
    {
        var store = NewStore();
        var securityId = Guid.NewGuid();

        // Simulate a pre-Track-C row: insert only the base columns and let the migration-added
        // approval columns fall back to their defaults (NotRequested / empty trail).
        await InsertBaseOnlyRowAsync(securityId);

        var loaded = await store.GetAsync(securityId);
        loaded.Should().NotBeNull();
        loaded!.ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.NotRequested);
        loaded.ReviewedBy.Should().BeNull();
        loaded.ReviewedAt.Should().BeNull();
        loaded.AuditTrail.Should().BeEmpty();
    }

    private async Task InsertBaseOnlyRowAsync(Guid securityId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {_fixture.Options.Schema}.security_operator_overrides (security_id, values, updated_by, updated_at)
            values (@security_id, '{{}}'::jsonb, 'legacy-operator', now());
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        await command.ExecuteNonQueryAsync();
    }
}
