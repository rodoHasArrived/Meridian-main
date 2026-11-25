using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class SecurityRateRepairPersistenceTests(SecurityMasterDatabaseFixture fixture)
    : IClassFixture<SecurityMasterDatabaseFixture>
{
    private PostgresSecurityRateRepairStore Store() => new(fixture.Options);
    private static SecurityRateRepairManifest Manifest(Guid? batchId = null)
    {
        var security = Guid.NewGuid();
        var request = new CreateSecurityRateRepairBatchRequest(batchId ?? Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), "synthetic-fund", Guid.NewGuid().ToString("D")),
            new("synthetic-vendor", "assetSpecificTerms.couponRate", "v1", "AnnualRate"),
            [new(security, 7)], "Percent", new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            "ev-0123456789abcdef0123456789abcdef", "Retained vendor definition.", true);
        return new("repair-test-tenant", "repair-test-company", "maker", DateTimeOffset.UtcNow,
            new string('a', 64), new string('b', 64), request,
            [new(security, 7, "Synthetic CD", "CertificateOfDeposit", "5.25")]);
    }

    [SecurityMasterDatabaseFact]
    public async Task Create_RetainsExactRosterAndPreparedWorkflowAcrossRetriesAndInstances()
    {
        var manifest = Manifest();
        var first = await Store().CreateAsync(manifest);
        var retry = await Store().CreateAsync(manifest);
        retry.Should().BeEquivalentTo(first);
        retry.Items.Single().WorkflowId.Should().NotBeEmpty();
        retry.Manifest.Items.Single().RawValue.Should().Be("5.25");
        await Store().Invoking(store => store.CreateAsync(manifest with { CommandSha256 = new string('c', 64) }))
            .Should().ThrowAsync<SecurityRateRepairConflictException>();
        (await Store().GetAsync(manifest.Request.RequestId, "another-tenant")).Should().BeNull();
    }

    [SecurityMasterDatabaseFact]
    public async Task ItemTransaction_RollsBackBothNestedDraftAndProgressOnFailure()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var manifest = Manifest();
        await Store().CreateAsync(manifest, timeout.Token);
        var frozen = manifest.Items.Single();
        var revisions = new PostgresSecurityMasterRevisionStore(fixture.Options);
        Func<Task> action = () => Store().ExecuteItemAsync(manifest.Request.RequestId, manifest.TenantId, frozen.SecurityId, async progress =>
        {
            await revisions.CreateDraftAsync(frozen.SecurityId, manifest.Maker, "assetSpecificTerms.rateUnit",
                manifest.Request.EffectiveFrom, "Governed repair", null, new("Percent"), new(7, "{}"),
                new(manifest.Request.RequestId, manifest.Request.EvidenceId, manifest.EvidenceSha256), timeout.Token);
            throw new InvalidOperationException("synthetic transaction failure");
        }, timeout.Token);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("synthetic transaction failure");
        (await revisions.ListBySecurityAsync(frozen.SecurityId, timeout.Token)).Should().BeEmpty();
        var retained = await Store().GetAsync(manifest.Request.RequestId, manifest.TenantId, timeout.Token);
        retained!.Version.Should().Be(1);
        retained.Items.Single().RevisionId.Should().BeNull();
    }

    [SecurityMasterDatabaseFact]
    public async Task CommandLease_ExcludesOtherHostsAndReleasesBeforePoolReturn()
    {
        var manifest = Manifest();
        await Store().CreateAsync(manifest);
        await using (await Store().AcquireCommandLeaseAsync(manifest.Request.RequestId, manifest.TenantId, 1))
        {
            await Store().Invoking(store => store.AcquireCommandLeaseAsync(manifest.Request.RequestId, manifest.TenantId, 1))
                .Should().ThrowAsync<SecurityRateRepairConflictException>();
        }
        await using var recovered = await Store().AcquireCommandLeaseAsync(manifest.Request.RequestId, manifest.TenantId, 1);
        await Store().ExecuteItemAsync(manifest.Request.RequestId, manifest.TenantId, manifest.Items.Single().SecurityId,
            progress => Task.FromResult(progress with { ErrorCode = "Synthetic", Error = "Visible retryable outcome" }));
        (await Store().GetAsync(manifest.Request.RequestId, manifest.TenantId))!.Version.Should().Be(2);
    }

    [SecurityMasterDatabaseFact]
    public async Task DraftMakerAndSubmitter_SurviveActorTransitionsAndCannotBeRewritten()
    {
        var revisions = new PostgresSecurityMasterRevisionStore(fixture.Options);
        var draft = await revisions.CreateDraftAsync(Guid.NewGuid(), "original-maker");
        await revisions.TransitionAsync(draft.RevisionId, SecurityMasterRevisionStateDto.Draft,
            SecurityMasterRevisionStateDto.Submitted, "delegated-submitter");
        await revisions.TransitionAsync(draft.RevisionId, SecurityMasterRevisionStateDto.Submitted,
            SecurityMasterRevisionStateDto.Approved, "independent-reviewer");
        var retained = (await new PostgresSecurityMasterRevisionStore(fixture.Options).GetAsync(draft.RevisionId))!;
        retained.Actor.Should().Be("independent-reviewer");
        retained.DraftedBy.Should().Be("original-maker");
        retained.SubmittedBy.Should().Be("delegated-submitter");
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"update {fixture.Options.Schema}.security_master_revisions set drafted_by = 'forged' where revision_id = @id";
        command.Parameters.AddWithValue("id", draft.RevisionId);
        await command.Invoking(command => command.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>();
    }

    [SecurityMasterDatabaseFact]
    public async Task FrozenManifest_RejectsMutation()
    {
        var manifest = Manifest();
        await Store().CreateAsync(manifest);
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"update {fixture.Options.Schema}.security_rate_repair_batches set created_by = 'forged' where batch_id = @id";
        command.Parameters.AddWithValue("id", manifest.Request.RequestId);
        await command.Invoking(command => command.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>();
    }

    [SecurityMasterDatabaseFact]
    public async Task RetainedManifestAndProgress_RejectRemovalWhileProgressVersionCannotRegressBelowOne()
    {
        var manifest = Manifest();
        await Store().CreateAsync(manifest);
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[]
        {
            $"delete from {fixture.Options.Schema}.security_rate_repair_batches where batch_id = @id",
            $"delete from {fixture.Options.Schema}.security_rate_repair_state where batch_id = @id",
            $"truncate {fixture.Options.Schema}.security_rate_repair_batches cascade",
            $"truncate {fixture.Options.Schema}.security_rate_repair_state"
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (sql.Contains("@id", StringComparison.Ordinal))
                command.Parameters.AddWithValue("id", manifest.Request.RequestId);
            (await command.Invoking(command => command.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }
        await using var zero = connection.CreateCommand();
        zero.CommandText = $"update {fixture.Options.Schema}.security_rate_repair_state set version = 0 where batch_id = @id";
        zero.Parameters.AddWithValue("id", manifest.Request.RequestId);
        (await zero.Invoking(command => command.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        (await Store().GetAsync(manifest.Request.RequestId, manifest.TenantId))!.Items.Should().ContainSingle();
    }

    [SecurityMasterDatabaseFact]
    public async Task RepairContext_RejectsNullHashOrMissingCanonicalBaseAtTheDatabaseBoundary()
    {
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        foreach (var missing in new[] { "hash", "version", "snapshot", "negative-version" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                insert into {fixture.Options.Schema}.security_master_revisions
                  (revision_id, security_id, state, actor, created_at, updated_at, drafted_by,
                   repair_batch_id, repair_evidence_id, repair_evidence_sha256,
                   base_version, field_base_value, field_base_recorded, field_value_recorded)
                values (@id, @security, 'Draft', 'maker', now(), now(), 'maker', @batch, 'evidence', @hash,
                        @version, @snapshot, true, true);
                """;
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("security", Guid.NewGuid());
            command.Parameters.AddWithValue("batch", Guid.NewGuid());
            command.Parameters.AddWithValue("hash", missing == "hash" ? DBNull.Value : new string('a', 64));
            command.Parameters.AddWithValue("version", missing == "version" ? DBNull.Value : missing == "negative-version" ? -1L : 7L);
            command.Parameters.AddWithValue("snapshot", missing == "snapshot" ? DBNull.Value : "{}");
            (await command.Invoking(command => command.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }
    }

    [SecurityMasterDatabaseFact]
    public async Task ReviewedImpactBinding_RejectsMalformedReceiptAndNonpositiveCanonicalVersion()
    {
        var revision = await new PostgresSecurityMasterRevisionStore(fixture.Options).CreateDraftAsync(Guid.NewGuid(), "maker");
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        foreach (var invalid in new[] { (Receipt: "not-a-receipt", Version: 7L), (Receipt: new string('a', 64), Version: 0L) })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"update {fixture.Options.Schema}.security_master_revisions set reviewed_impact_receipt_id = @receipt, reviewed_canonical_version = @version where revision_id = @id";
            command.Parameters.AddWithValue("receipt", invalid.Receipt);
            command.Parameters.AddWithValue("version", invalid.Version);
            command.Parameters.AddWithValue("id", revision.RevisionId);
            (await command.Invoking(command => command.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }
    }
}
