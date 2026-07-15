using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Meridian.Tests.Storage.Reporting;

[Trait("Category", "Integration")]
public sealed class ReportingArtifactCatalogAuditStoreTests : IClassFixture<ReportingArtifactDatabaseFixture>
{
    private static readonly DateTimeOffset StoredAtUtc =
        new(2026, 7, 15, 8, 30, 0, TimeSpan.Zero);

    private readonly ReportingArtifactDatabaseFixture _database;
    private readonly PostgresReportingArtifactCatalog _catalog;
    private readonly PostgresReportingArtifactAuditStore _audit;

    public ReportingArtifactCatalogAuditStoreTests(ReportingArtifactDatabaseFixture database)
    {
        _database = database;
        _catalog = new PostgresReportingArtifactCatalog(database.Options);
        _audit = new PostgresReportingArtifactAuditStore(database.Options);
    }

    [ReportingDatabaseFact]
    public async Task AddPackageAsync_IsAtomicTenantScopedAndExactlyIdempotent()
    {
        var packageId = NewId("package");
        var firstTenantPackage = CreatePackage(NewId("tenant-a"), packageId, artifactCount: 2);
        var secondTenantPackage = CreatePackage(NewId("tenant-b"), packageId, artifactCount: 1);

        var concurrentWrites = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => _catalog.AddPackageAsync(firstTenantPackage).AsTask()));
        var secondTenantWrite = await _catalog.AddPackageAsync(secondTenantPackage);

        concurrentWrites.Count(static result => !result.AlreadyExisted).Should().Be(1);
        concurrentWrites.Count(static result => result.AlreadyExisted).Should().Be(7);
        secondTenantWrite.AlreadyExisted.Should().BeFalse();

        var firstArtifact = firstTenantPackage.Artifacts[0];
        var retained = await _catalog.GetArtifactAsync(
            firstArtifact.Scope.TenantId,
            firstArtifact.PackageId,
            firstArtifact.ArtifactId);
        var crossTenant = await _catalog.GetArtifactAsync(
            NewId("tenant-missing"),
            firstArtifact.PackageId,
            firstArtifact.ArtifactId);
        var secondTenantRetained = await _catalog.GetArtifactAsync(
            secondTenantPackage.Artifacts[0].Scope.TenantId,
            packageId,
            secondTenantPackage.Artifacts[0].ArtifactId);

        retained.Should().BeEquivalentTo(firstArtifact);
        crossTenant.Should().BeNull();
        secondTenantRetained.Should().BeEquivalentTo(secondTenantPackage.Artifacts[0]);
        (await CountPackageRowsAsync(firstArtifact.Scope.TenantId, packageId)).Should().Be(1);
        (await CountArtifactRowsAsync(firstArtifact.Scope.TenantId, packageId)).Should().Be(2);

        Func<Task> unscopedLookup = async () =>
            await ((IReportingArtifactCatalog)_catalog).GetArtifactAsync(packageId, firstArtifact.ArtifactId);
        await unscopedLookup.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Tenant-scoped*");
    }

    [ReportingDatabaseFact]
    public async Task AddPackageAsync_RejectsAnyMetadataMismatchAndPreservesOriginal()
    {
        var package = CreatePackage(NewId("tenant"), NewId("package"), artifactCount: 2);
        await _catalog.AddPackageAsync(package);
        var changedArtifact = package.Artifacts[0] with { FileName = "different-name.pdf" };
        var mismatched = package with { Artifacts = package.Artifacts.SetItem(0, changedArtifact) };

        Func<Task> replace = async () => await _catalog.AddPackageAsync(mismatched);

        await replace.Should().ThrowAsync<ReportingArtifactCatalogIntegrityException>()
            .WithMessage("*replace immutable report package metadata*");
        var retained = await _catalog.GetArtifactAsync(
            package.Artifacts[0].Scope.TenantId,
            package.PackageId,
            package.Artifacts[0].ArtifactId);
        retained.Should().BeEquivalentTo(package.Artifacts[0]);
        (await CountArtifactRowsAsync(package.Artifacts[0].Scope.TenantId, package.PackageId)).Should().Be(2);
    }

    [ReportingDatabaseFact]
    public async Task AddPackageAsync_RejectsInvalidPackageBeforeAnyPartialWrite()
    {
        var package = CreatePackage(NewId("tenant"), NewId("package"), artifactCount: 2);
        var duplicateArtifact = package.Artifacts[1] with { ArtifactId = package.Artifacts[0].ArtifactId };
        var invalid = package with { Artifacts = package.Artifacts.SetItem(1, duplicateArtifact) };

        Func<Task> add = async () => await _catalog.AddPackageAsync(invalid);

        await add.Should().ThrowAsync<ArgumentException>().WithMessage("*more than once*");
        (await CountPackageRowsAsync(package.Artifacts[0].Scope.TenantId, package.PackageId)).Should().Be(0);
        (await CountArtifactRowsAsync(package.Artifacts[0].Scope.TenantId, package.PackageId)).Should().Be(0);
    }

    [ReportingDatabaseFact]
    public async Task AppendAsync_ConcurrentWritersProduceOneContiguousHashChainAndExactRetriesAreIdempotent()
    {
        var initialHead = await ReadChainHeadAsync();
        var events = Enumerable.Range(0, 12)
            .Select(index => CreateAuditEvent(NewId($"event-{index}"), reason: $"reason-{index}"))
            .ToArray();

        var receipts = (await Task.WhenAll(events.Select(item => _audit.AppendAsync(item).AsTask())))
            .OrderBy(static item => item.Sequence)
            .ToArray();

        receipts.Select(static item => item.Sequence).Should().Equal(
            Enumerable.Range(0, events.Length).Select(index => initialHead.NextSequence + index));
        receipts[0].PreviousHash.Should().Be(initialHead.LastHash);
        for (var index = 1; index < receipts.Length; index++)
        {
            receipts[index].PreviousHash.Should().Be(receipts[index - 1].Hash);
        }

        var retainedRows = await ReadAuditRowsAsync(events.Select(static item => item.EventId).ToArray());
        retainedRows.Should().HaveCount(events.Length);
        foreach (var row in retainedRows)
        {
            row.EntryHash.Should().Be(PostgresReportingArtifactAuditStore.ComputeEntryHash(
                row.Sequence,
                row.PreviousHash,
                row.EventPayload));
        }

        var firstEvent = events[0];
        var firstReceipt = receipts.Single(item => item.EventId == firstEvent.EventId);
        var retryReceipt = await _audit.AppendAsync(firstEvent);
        retryReceipt.Should().Be(firstReceipt);
        (await CountAuditRowsAsync(firstEvent.EventId)).Should().Be(1);

        Func<Task> mismatch = async () => await _audit.AppendAsync(firstEvent with { Reason = "changed" });
        await mismatch.Should().ThrowAsync<ReportingArtifactAuditIntegrityException>()
            .WithMessage("*non-identical metadata*");
        (await CountAuditRowsAsync(firstEvent.EventId)).Should().Be(1);
    }

    [ReportingDatabaseFact]
    public async Task DatabaseGuards_RejectPackageCatalogAndAuditMutation()
    {
        var package = CreatePackage(NewId("tenant"), NewId("package"), artifactCount: 1);
        await _catalog.AddPackageAsync(package);
        var auditEvent = CreateAuditEvent(NewId("event"));
        var auditReceipt = await _audit.AppendAsync(auditEvent);

        Func<Task> updatePackage = () => ExecuteAsync(
            $"update {Qualified("reporting_artifact_packages")} set artifact_count = artifact_count where tenant_id = @tenant_id and package_id = @package_id;",
            ("tenant_id", package.Artifacts[0].Scope.TenantId),
            ("package_id", package.PackageId));
        Func<Task> deleteArtifact = () => ExecuteAsync(
            $"delete from {Qualified("reporting_artifact_catalog")} where tenant_id = @tenant_id and package_id = @package_id and artifact_id = @artifact_id;",
            ("tenant_id", package.Artifacts[0].Scope.TenantId),
            ("package_id", package.PackageId),
            ("artifact_id", package.Artifacts[0].ArtifactId));
        Func<Task> updateAudit = () => ExecuteAsync(
            $"update {Qualified("reporting_artifact_audit")} set entry_hash = entry_hash where sequence = @sequence;",
            ("sequence", auditReceipt.Sequence));
        Func<Task> deleteAudit = () => ExecuteAsync(
            $"delete from {Qualified("reporting_artifact_audit")} where sequence = @sequence;",
            ("sequence", auditReceipt.Sequence));
        Func<Task> forkAuditChain = () => ExecuteAsync(
            $"""
            insert into {Qualified("reporting_artifact_audit")} (
                sequence, event_id, occurred_at_utc, action, actor_tenant_id, target_tenant_id,
                package_id, artifact_id, previous_hash, entry_hash, event_payload)
            values (
                @sequence, @event_id, @occurred_at_utc, 'ContentAccessed', 'actor-tenant', 'target-tenant',
                'package', 'artifact', @previous_hash, @entry_hash, '{{}}');
            """,
            ("sequence", auditReceipt.Sequence + 10),
            ("event_id", NewId("forged-event")),
            ("occurred_at_utc", StoredAtUtc),
            ("previous_hash", auditReceipt.Hash),
            ("entry_hash", Hash("forged-entry")));

        (await updatePackage.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
        (await deleteArtifact.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
        (await updateAudit.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
        (await deleteAudit.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
        (await forkAuditChain.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
    }

    [ReportingDatabaseFact]
    public async Task MigrationRunner_RecordsCatalogAuditMigrationInChecksummedLedger()
    {
        await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => new ReportingMigrationRunner(_database.Options).EnsureMigratedAsync()));

        (await _database.HasMigrationAsync("004_reporting_artifact_catalog_audit.sql")).Should().BeTrue();
    }

    private static ReportingRetainedArtifactPackage CreatePackage(
        string tenantId,
        string packageId,
        int artifactCount)
    {
        var scope = new ReportingOperationalScope(
            tenantId,
            NewId("organization"),
            NewId("company"),
            NewId("fund"),
            NewId("book"),
            "2026-07");
        var access = new ReportingAccessScope(
            NewId("policy"),
            "1",
            ReportingGovernanceAccessMode.Restricted,
            NewId("owner"),
            ImmutableArray.Create(NewId("principal")),
            Hash("policy"));
        var snapshot = new ReportingCertifiedSnapshotScope(
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            scope.FundId,
            scope.BookId,
            scope.PeriodId,
            NewId("snapshot"),
            Hash("snapshot"),
            NewId("reconciliation"),
            StoredAtUtc.AddMinutes(-5));
        var runId = NewId("run");
        var seriesId = NewId("series");
        var manifestId = NewId("manifest");
        var manifestHash = Hash("manifest");
        var artifacts = Enumerable.Range(0, artifactCount)
            .Select(index => new ReportingRetainedArtifactRecord(
                packageId,
                runId,
                seriesId,
                Revision: 1,
                scope,
                access,
                snapshot,
                manifestId,
                manifestHash,
                $"artifact-{index}.pdf",
                $"statement-{index}.pdf",
                "application/pdf",
                new ReportingArtifactIdentity(tenantId, Hash($"content-{index}")),
                ByteLength: 128 + index,
                StoredAtUtc.AddSeconds(index)))
            .ToImmutableArray();
        return new ReportingRetainedArtifactPackage(packageId, artifacts);
    }

    private static ReportingArtifactAuditEvent CreateAuditEvent(string eventId, string? reason = null) =>
        new(
            eventId,
            StoredAtUtc,
            ReportingArtifactAuditAction.ContentAccessed,
            NewId("actor"),
            NewId("actor-tenant"),
            NewId("target-tenant"),
            NewId("package"),
            NewId("artifact"),
            Hash("audit-content"),
            NewId("correlation"),
            reason);

    private async Task<long> CountPackageRowsAsync(string tenantId, string packageId) =>
        await ExecuteScalarInt64Async(
            $"select count(*) from {Qualified("reporting_artifact_packages")} where tenant_id = @tenant_id and package_id = @package_id;",
            ("tenant_id", tenantId),
            ("package_id", packageId));

    private async Task<long> CountArtifactRowsAsync(string tenantId, string packageId) =>
        await ExecuteScalarInt64Async(
            $"select count(*) from {Qualified("reporting_artifact_catalog")} where tenant_id = @tenant_id and package_id = @package_id;",
            ("tenant_id", tenantId),
            ("package_id", packageId));

    private async Task<long> CountAuditRowsAsync(string eventId) =>
        await ExecuteScalarInt64Async(
            $"select count(*) from {Qualified("reporting_artifact_audit")} where event_id = @event_id;",
            ("event_id", eventId));

    private async Task<AuditHeadRow> ReadChainHeadAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select next_sequence, last_hash from {Qualified("reporting_artifact_audit_chain_head")} where chain_id = 1;";
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return new AuditHeadRow(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task<IReadOnlyList<AuditRow>> ReadAuditRowsAsync(IReadOnlyCollection<string> eventIds)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select sequence, previous_hash, entry_hash, event_payload
            from {Qualified("reporting_artifact_audit")}
            where event_id = any(@event_ids)
            order by sequence;
            """;
        command.Parameters.AddWithValue("event_ids", NpgsqlDbType.Array | NpgsqlDbType.Text, eventIds.ToArray());
        var rows = new List<AuditRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AuditRow(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }

    private async Task<long> ExecuteScalarInt64Async(
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static void AddParameters(
        NpgsqlCommand command,
        IEnumerable<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
    }

    private string Qualified(string table) => $"\"{_database.Options.Schema}\".\"{table}\"";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private sealed record AuditHeadRow(long NextSequence, string? LastHash);

    private sealed record AuditRow(long Sequence, string? PreviousHash, string EntryHash, string EventPayload);
}
