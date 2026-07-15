using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.TestSupport;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Meridian.Tests.Storage.Reporting;

[Trait("Category", "Integration")]
public sealed class ReportingGovernanceRepositoryTests : IClassFixture<ReportingGovernanceDatabaseFixture>
{
    private readonly ReportingGovernanceDatabaseFixture _database;

    public ReportingGovernanceRepositoryTests(ReportingGovernanceDatabaseFixture database)
    {
        _database = database;
    }

    [ReportingDatabaseFact]
    public async Task Lifecycle_PersistsTenantBoundStateAndImmutableAuditAcrossRepositoryInstances()
    {
        var scenario = NewScenario();
        var released = await CreateReleasedRunAsync(_database.Repository, scenario);

        var restarted = new PostgresReportingGovernanceRepository(_database.Options);
        var retained = await LoadRunAsync(restarted, scenario.TenantId, released.RunId);
        var crossTenant = await LoadRunAsync(restarted, $"other-{scenario.TenantId}", released.RunId);

        retained.Should().BeEquivalentTo(released);
        retained!.GovernanceState.Should().Be(GovernedReportingState.Released);
        retained.Release.Should().NotBeNull();
        ReportingGovernanceAuditChain.Verify(retained.AuditTrail).Should().BeTrue();
        (await _database.CountAuditRowsAsync(
            scenario.TenantId,
            ReportingGovernanceAuditAggregateKind.Run,
            released.RunId)).Should().Be(released.Version);
        crossTenant.Should().BeNull();
    }

    [ReportingDatabaseFact]
    public async Task ReplaceRunAsync_RejectsAStaleDurableVersionWithoutAppendingAudit()
    {
        var scenario = NewScenario();
        var service = NewService(_database.Repository);
        var created = await service.CreateRunAsync(scenario.CreationRequest, scenario.Creator);
        var started = await service.BeginExecutionAsync(created.RunId, created.Version, scenario.Creator);

        Func<Task> staleWrite = async () =>
            await _database.Repository.ExecuteTransactionAsync(async (transaction, ct) =>
            {
                await transaction.ReplaceRunAsync(started, expectedVersion: created.Version, ct);
                return true;
            });

        await staleWrite.Should().ThrowAsync<ReportingGovernanceConcurrencyException>()
            .WithMessage("*version conflict*");

        var retained = await LoadRunAsync(_database.Repository, scenario.TenantId, created.RunId);
        retained!.Version.Should().Be(started.Version);
        retained.AuditTrail.Should().HaveCount(2);
        ReportingGovernanceAuditChain.Verify(retained.AuditTrail).Should().BeTrue();
    }

    [ReportingDatabaseFact]
    public async Task ApproveRestatementAsync_AtomicallyCreatesTheNextRevisionAndUpdatesTheRequest()
    {
        var scenario = NewScenario();
        var service = NewService(_database.Repository);
        var predecessor = await CreateReleasedRunAsync(service, scenario);
        var request = await service.RequestRestatementAsync(
            new ReportingRestatementRequestCommand(
                predecessor.RunId,
                predecessor.Version,
                "Late administrator correction",
                [new ReportingRestatementChangedLine("nav.total", "100", "101", ["evidence-change-1"])]),
            scenario.Creator);

        var replacementSnapshot = scenario.CreationRequest.Snapshot with
        {
            SnapshotId = $"snapshot-{Guid.NewGuid():N}",
            SnapshotHash = $"snapshot-hash-{Guid.NewGuid():N}",
            CapturedAtUtc = DateTimeOffset.UtcNow
        };
        var approved = await service.ApproveRestatementAsync(
            new ReportingRestatementApprovalCommand(request.RequestId, request.Version, replacementSnapshot),
            scenario.RestatementApprover);

        var retainedRequest = await LoadRestatementAsync(
            _database.Repository,
            scenario.TenantId,
            request.RequestId);
        var revisions = await ListSeriesAsync(
            _database.Repository,
            scenario.TenantId,
            predecessor.SeriesId);

        retainedRequest.Should().BeEquivalentTo(approved.Request);
        retainedRequest!.State.Should().Be(ReportingRestatementRequestState.Approved);
        retainedRequest.DraftRunId.Should().Be(approved.DraftRun.RunId);
        revisions.Select(static run => run.Revision).Should().Equal(1, 2);
        revisions[1].RestatementOfRunId.Should().Be(predecessor.RunId);
        revisions[1].GovernanceState.Should().Be(GovernedReportingState.Draft);
        ReportingGovernanceAuditChain.Verify(retainedRequest.AuditTrail).Should().BeTrue();
        ReportingGovernanceAuditChain.Verify(revisions[1].AuditTrail).Should().BeTrue();
    }

    [ReportingDatabaseFact]
    public async Task ApproveRestatementAsync_RollsBackRequestAndAuditWhenDraftRevisionInsertFails()
    {
        var scenario = NewScenario();
        var initialService = NewService(_database.Repository);
        var predecessor = await CreateReleasedRunAsync(initialService, scenario);
        var requestId = $"restatement-{Guid.NewGuid():N}";
        var conflictingService = NewService(
            _database.Repository,
            prefix => prefix == "restatement" ? requestId : predecessor.RunId);
        var request = await conflictingService.RequestRestatementAsync(
            new ReportingRestatementRequestCommand(
                predecessor.RunId,
                predecessor.Version,
                "Late correction requiring rollback proof",
                [new ReportingRestatementChangedLine("expense.total", "4", "5", ["evidence-change-2"])]),
            scenario.Creator);

        var replacementSnapshot = scenario.CreationRequest.Snapshot with
        {
            SnapshotId = $"snapshot-{Guid.NewGuid():N}",
            SnapshotHash = $"snapshot-hash-{Guid.NewGuid():N}",
            CapturedAtUtc = DateTimeOffset.UtcNow
        };

        Func<Task> approve = async () => await conflictingService.ApproveRestatementAsync(
            new ReportingRestatementApprovalCommand(request.RequestId, request.Version, replacementSnapshot),
            scenario.RestatementApprover);

        await approve.Should().ThrowAsync<ReportingGovernanceConcurrencyException>();

        var retainedRequest = await LoadRestatementAsync(
            _database.Repository,
            scenario.TenantId,
            request.RequestId);
        var revisions = await ListSeriesAsync(
            _database.Repository,
            scenario.TenantId,
            predecessor.SeriesId);

        retainedRequest!.State.Should().Be(ReportingRestatementRequestState.PendingApproval);
        retainedRequest.Version.Should().Be(1);
        retainedRequest.AuditTrail.Should().HaveCount(1);
        retainedRequest.DraftRunId.Should().BeNull();
        revisions.Should().ContainSingle();
    }

    [ReportingDatabaseFact]
    public async Task AuditTable_RejectsUpdateAndDelete()
    {
        var scenario = NewScenario();
        var service = NewService(_database.Repository);
        var created = await service.CreateRunAsync(scenario.CreationRequest, scenario.Creator);

        Func<Task> update = () => _database.ExecuteAuditMutationAsync(
            "set event_payload = event_payload",
            scenario.TenantId,
            ReportingGovernanceAuditAggregateKind.Run,
            created.RunId,
            delete: false);
        Func<Task> delete = () => _database.ExecuteAuditMutationAsync(
            string.Empty,
            scenario.TenantId,
            ReportingGovernanceAuditAggregateKind.Run,
            created.RunId,
            delete: true);

        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
        (await LoadRunAsync(_database.Repository, scenario.TenantId, created.RunId))!
            .AuditTrail.Should().ContainSingle();
    }

    [ReportingDatabaseFact]
    public async Task Read_FailsClosedWhenStatePayloadOrAuditChainIsCorrupt()
    {
        var stateScenario = NewScenario();
        var service = NewService(_database.Repository);
        var stateRun = await service.CreateRunAsync(stateScenario.CreationRequest, stateScenario.Creator);
        await _database.CorruptRunPayloadAsync(stateScenario.TenantId, stateRun.RunId);

        Func<Task> corruptStateRead = async () =>
            await LoadRunAsync(_database.Repository, stateScenario.TenantId, stateRun.RunId);
        await corruptStateRead.Should().ThrowAsync<ReportingGovernancePersistenceException>()
            .WithMessage("*SHA-256*");

        var auditScenario = NewScenario();
        var auditRun = await service.CreateRunAsync(auditScenario.CreationRequest, auditScenario.Creator);
        await _database.RemoveAuditChainAsync(
            auditScenario.TenantId,
            ReportingGovernanceAuditAggregateKind.Run,
            auditRun.RunId);

        Func<Task> corruptAuditRead = async () =>
            await LoadRunAsync(_database.Repository, auditScenario.TenantId, auditRun.RunId);
        await corruptAuditRead.Should().ThrowAsync<ReportingGovernancePersistenceException>()
            .WithMessage("*chain*");
    }

    [ReportingDatabaseFact]
    public async Task MigrationRunner_RecordsTheChecksummedGovernanceMigration()
    {
        await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => new ReportingMigrationRunner(_database.Options).EnsureMigratedAsync()));

        (await _database.ListMigrationFilesAsync()).Should().Contain("002_reporting_governance.sql");
    }

    private static async Task<GovernedReportingRun> CreateReleasedRunAsync(
        PostgresReportingGovernanceRepository repository,
        GovernanceScenario scenario) =>
        await CreateReleasedRunAsync(NewService(repository), scenario);

    private static async Task<GovernedReportingRun> CreateReleasedRunAsync(
        ReportingGovernanceService service,
        GovernanceScenario scenario)
    {
        var run = await service.CreateRunAsync(scenario.CreationRequest, scenario.Creator);
        run = await service.BeginExecutionAsync(run.RunId, run.Version, scenario.Creator);
        run = await service.CompleteExecutionAsync(run.RunId, run.Version, scenario.Creator);
        run = await service.ValidateAsync(
            run.RunId,
            run.Version,
            new ReportingReadinessReceipt(
                $"readiness-{Guid.NewGuid():N}",
                $"readiness-hash-{Guid.NewGuid():N}",
                run.RunId,
                scenario.TenantId,
                run.Snapshot.SnapshotId,
                run.Snapshot.SnapshotHash,
                DateTimeOffset.UtcNow,
                [new ReportingReadinessCheck("ledger-reconciled", true, ["evidence-ready-1"])]),
            scenario.Creator);
        run = await service.SubmitAsync(run.RunId, run.Version, scenario.Creator);
        run = await service.ApproveAsync(run.RunId, run.Version, "Reviewed and approved", scenario.Approver);
        return await service.ReleaseAsync(
            run.RunId,
            run.Version,
            new ReportingReleaseEvidence(
                $"manifest-{Guid.NewGuid():N}",
                $"manifest-hash-{Guid.NewGuid():N}",
                [new ReportingArtifactReference("artifact-1", new string('a', 64), 128)],
                ["evidence-release-1"]),
            scenario.Releaser);
    }

    private static ReportingGovernanceService NewService(
        PostgresReportingGovernanceRepository repository,
        Func<string, string>? idFactory = null) =>
        new(repository, idFactory: idFactory);

    private static async Task<GovernedReportingRun?> LoadRunAsync(
        PostgresReportingGovernanceRepository repository,
        string tenantId,
        string runId) =>
        await repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.GetRunAsync(tenantId, runId, ct));

    private static async Task<ReportingRestatementRequest?> LoadRestatementAsync(
        PostgresReportingGovernanceRepository repository,
        string tenantId,
        string requestId) =>
        await repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.GetRestatementRequestAsync(tenantId, requestId, ct));

    private static async Task<IReadOnlyList<GovernedReportingRun>> ListSeriesAsync(
        PostgresReportingGovernanceRepository repository,
        string tenantId,
        string seriesId) =>
        await repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.ListRunsBySeriesAsync(tenantId, seriesId, ct));

    private static GovernanceScenario NewScenario()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var tenantId = $"tenant-{suffix}";
        var companyId = $"company-{suffix}";
        var scope = new ReportingOperationalScope(
            tenantId,
            $"organization-{suffix}",
            companyId,
            $"fund-{suffix}",
            $"book-{suffix}",
            $"period-{suffix}");
        var request = new ReportingRunCreationRequest(
            $"run-{suffix}",
            $"series-{suffix}",
            "template-monthly-financials",
            "1.0.0",
            scope,
            new ReportingAccessScope(
                $"policy-{suffix}",
                "1",
                ReportingGovernanceAccessMode.CompanyWide,
                OwnerPrincipalId: null,
                PrincipalIds: [],
                PolicyHash: new string('b', 64)),
            new ReportingCertifiedSnapshotScope(
                tenantId,
                scope.OrganizationId,
                companyId,
                scope.FundId,
                scope.BookId,
                scope.PeriodId,
                $"snapshot-{suffix}",
                new string('c', 64),
                $"reconciliation-{suffix}",
                DateTimeOffset.UtcNow));

        return new GovernanceScenario(
            tenantId,
            request,
            Authority("creator", scope),
            Authority("approver", scope),
            Authority("releaser", scope),
            Authority("restatement-approver", scope));
    }

    private static ReportingAuthorityScope Authority(string actorPrefix, ReportingOperationalScope scope) =>
        new(
            $"{actorPrefix}-{Guid.NewGuid():N}",
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            Enum.GetValues<ReportingGovernancePermission>().ToImmutableArray(),
            ReportingCommandOrigin.HumanOperator,
            $"correlation-{Guid.NewGuid():N}");

    private sealed record GovernanceScenario(
        string TenantId,
        ReportingRunCreationRequest CreationRequest,
        ReportingAuthorityScope Creator,
        ReportingAuthorityScope Approver,
        ReportingAuthorityScope Releaser,
        ReportingAuthorityScope RestatementApprover);
}

public sealed class ReportingGovernanceDatabaseFixture : IAsyncLifetime
{
    private const string ConnectionStringVariable = "MERIDIAN_REPORTING_CONNECTION_STRING";
    private PostgresTestServer? _server;

    public ReportingArtifactStoreOptions Options { get; private set; } = null!;

    public PostgresReportingGovernanceRepository Repository { get; private set; } = null!;

    public string QualifiedRunsTable => $"\"{Options.Schema}\".\"reporting_governed_runs\"";

    public string QualifiedAuditTable => $"\"{Options.Schema}\".\"reporting_governance_audit\"";

    public async Task InitializeAsync()
    {
        _server = await PostgresTestServer.CreateAsync(ConnectionStringVariable).ConfigureAwait(false);
        Options = new ReportingArtifactStoreOptions
        {
            ConnectionString = _server.ConnectionString,
            Schema = PostgresTestSchema.NewSchemaName("reporting_governance")
        };

        try
        {
            await new ReportingMigrationRunner(Options).EnsureMigratedAsync().ConfigureAwait(false);
            Repository = new PostgresReportingGovernanceRepository(Options);
        }
        catch
        {
            await _server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_server is null)
        {
            return;
        }

        if (_server.UsesExternalConnection)
        {
            await new ReportingMigrationRunner(Options).ResetSchemaAsync().ConfigureAwait(false);
        }

        await _server.DisposeAsync().ConfigureAwait(false);
    }

    public async Task<long> CountAuditRowsAsync(
        string tenantId,
        ReportingGovernanceAuditAggregateKind kind,
        string aggregateId)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select count(*) from {QualifiedAuditTable} where tenant_id = @tenant_id and aggregate_kind = @kind and aggregate_id = @aggregate_id;";
        AddAuditIdentity(command, tenantId, kind, aggregateId);
        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
    }

    public async Task<IReadOnlyList<string>> ListMigrationFilesAsync()
    {
        var names = new List<string>();
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select filename from \"{Options.Schema}\".\"reporting_schema_migrations\" order by filename;";
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public async Task ExecuteAuditMutationAsync(
        string updateClause,
        string tenantId,
        ReportingGovernanceAuditAggregateKind kind,
        string aggregateId,
        bool delete)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = delete
            ? $"delete from {QualifiedAuditTable} where tenant_id = @tenant_id and aggregate_kind = @kind and aggregate_id = @aggregate_id;"
            : $"update {QualifiedAuditTable} {updateClause} where tenant_id = @tenant_id and aggregate_kind = @kind and aggregate_id = @aggregate_id;";
        AddAuditIdentity(command, tenantId, kind, aggregateId);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task CorruptRunPayloadAsync(string tenantId, string runId)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await SetUserTriggersAsync(connection, QualifiedRunsTable, enabled: false).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"update {QualifiedRunsTable} set state_payload = state_payload || ' ' where tenant_id = @tenant_id and run_id = @run_id;";
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            command.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, runId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetUserTriggersAsync(connection, QualifiedRunsTable, enabled: true).ConfigureAwait(false);
        }
    }

    public async Task RemoveAuditChainAsync(
        string tenantId,
        ReportingGovernanceAuditAggregateKind kind,
        string aggregateId)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await SetUserTriggersAsync(connection, QualifiedAuditTable, enabled: false).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"delete from {QualifiedAuditTable} where tenant_id = @tenant_id and aggregate_kind = @kind and aggregate_id = @aggregate_id;";
            AddAuditIdentity(command, tenantId, kind, aggregateId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            await SetUserTriggersAsync(connection, QualifiedAuditTable, enabled: true).ConfigureAwait(false);
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(Options.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    private static async Task SetUserTriggersAsync(
        NpgsqlConnection connection,
        string table,
        bool enabled)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"alter table {table} {(enabled ? "enable" : "disable")} trigger user;";
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void AddAuditIdentity(
        NpgsqlCommand command,
        string tenantId,
        ReportingGovernanceAuditAggregateKind kind,
        string aggregateId)
    {
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Smallint, (short)kind);
        command.Parameters.AddWithValue("aggregate_id", NpgsqlDbType.Text, aggregateId);
    }
}
