using System.Text;
using FluentAssertions;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Meridian.Tests.Storage.Reporting;

[Trait("Category", "Integration")]
public sealed class StatementReconciliationReportAuthorityStoreTests :
    IClassFixture<ReportingArtifactDatabaseFixture>
{
    private const string MigrationFileName =
        "013_reporting_statement_reconciliation_authority.sql";

    private readonly ReportingArtifactDatabaseFixture _database;

    public StatementReconciliationReportAuthorityStoreTests(
        ReportingArtifactDatabaseFixture database)
    {
        _database = database;
    }

    [ReportingDatabaseFact]
    public async Task ImmutableDocument_ExactRetryIsIdempotentAndRetainsOneRevision()
    {
        var store = CreateStore();
        var scope = NewScope();
        const string documentKey = "input/statement.csv";
        var content = Encoding.UTF8.GetBytes($"statement-{Guid.NewGuid():N}");

        var first = await store.WriteDocumentAsync(
            scope,
            documentKey,
            content,
            isImmutable: true);
        var retry = await store.WriteDocumentAsync(
            scope,
            documentKey,
            content,
            isImmutable: true);

        first.Should().Be(retry);
        first.Version.Should().Be(1);
        first.IsImmutable.Should().BeTrue();
        (await store.TryReadDocumentAsync(scope, documentKey)).Should().Equal(content);
        (await ReadRevisionsAsync(scope, documentKey)).Should().ContainSingle(revision =>
            revision.Version == 1
            && revision.PreviousContentHash == null
            && revision.ContentHash == first.Identity.ContentHashSha256);

        var replace = () => store.WriteDocumentAsync(
            scope,
            documentKey,
            Encoding.UTF8.GetBytes("different-statement"),
            isImmutable: true).AsTask();

        await replace.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable retained mapping cannot be replaced*");
        (await ReadRevisionsAsync(scope, documentKey)).Should().ContainSingle();
    }

    [ReportingDatabaseFact]
    public async Task ImmutableConflict_RollsBackNewArtifactBlob()
    {
        var store = CreateStore();
        var scope = NewScope();
        const string documentKey = "input/statement.csv";
        var retainedContent = Encoding.UTF8.GetBytes($"statement-{Guid.NewGuid():N}");
        var conflictingContent = Encoding.UTF8.GetBytes($"conflict-{Guid.NewGuid():N}");
        var conflictingIdentity = new ReportingArtifactIdentity(
            scope.TenantId,
            ComputeSha256(conflictingContent));
        await store.WriteDocumentAsync(
            scope,
            documentKey,
            retainedContent,
            isImmutable: true);

        var replace = () => store.WriteDocumentAsync(
            scope,
            documentKey,
            conflictingContent,
            isImmutable: true).AsTask();

        await replace.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable retained mapping cannot be replaced*");
        (await _database.CountRowsAsync(conflictingIdentity)).Should().Be(0);
        (await store.TryReadDocumentAsync(scope, documentKey))
            .Should().Equal(retainedContent);
    }

    [ReportingDatabaseFact]
    public async Task MutableDocument_RestartAdvancesVersionAndRetainsExactRevisionChain()
    {
        var scope = NewScope();
        const string documentKey = "workflow.json";
        var firstContent = Encoding.UTF8.GetBytes($"snapshot-v1-{Guid.NewGuid():N}");
        var secondContent = Encoding.UTF8.GetBytes($"snapshot-v2-{Guid.NewGuid():N}");
        var firstStore = CreateStore();

        var first = await firstStore.WriteDocumentAsync(
            scope,
            documentKey,
            firstContent,
            isImmutable: false);

        // A new store instance proves that the mapping and bytes, not process memory, own resume.
        var restartedStore = CreateStore();
        var retry = await restartedStore.WriteDocumentAsync(
            scope,
            documentKey,
            firstContent,
            isImmutable: false);
        var retryRevisions = await ReadRevisionsAsync(scope, documentKey);
        var second = await restartedStore.WriteDocumentAsync(
            scope,
            documentKey,
            secondContent,
            isImmutable: false);
        var revisions = await ReadRevisionsAsync(scope, documentKey);

        first.Version.Should().Be(1);
        retry.Should().Be(first);
        retryRevisions.Should().ContainSingle(revision =>
            revision.Version == 1
            && revision.ContentHash == first.Identity.ContentHashSha256);
        second.Version.Should().Be(2);
        second.Identity.Should().NotBe(first.Identity);
        (await restartedStore.TryReadDocumentAsync(scope, documentKey))
            .Should().Equal(secondContent);
        revisions.Should().HaveCount(2);
        revisions[0].Should().Match<RevisionRow>(revision =>
            revision.Version == 1
            && revision.PreviousContentHash == null
            && revision.ContentHash == first.Identity.ContentHashSha256
            && revision.MappingUpdatedAtUtc == first.UpdatedAtUtc);
        revisions[1].Should().Match<RevisionRow>(revision =>
            revision.Version == 2
            && revision.PreviousContentHash == first.Identity.ContentHashSha256
            && revision.ContentHash == second.Identity.ContentHashSha256
            && revision.PreviousUpdatedAtUtc == first.UpdatedAtUtc
            && revision.MappingUpdatedAtUtc == second.UpdatedAtUtc);
    }

    [ReportingDatabaseFact]
    public async Task ReadsAndPrefixLists_RequireExactTenantCompanyWorkflowScope()
    {
        var store = CreateStore();
        var scope = NewScope();
        var otherCompany = scope with { CompanyId = $"{scope.CompanyId}-other" };
        var artifactContent = Encoding.UTF8.GetBytes("report");
        await store.WriteDocumentAsync(
            scope,
            "artifacts/report.json",
            artifactContent,
            isImmutable: false);
        await store.WriteDocumentAsync(
            scope,
            "artifacts/report.csv",
            artifactContent,
            isImmutable: false);
        await store.WriteDocumentAsync(
            scope,
            "workflow.json",
            Encoding.UTF8.GetBytes("snapshot"),
            isImmutable: false);

        (await store.ListDocumentKeysAsync(scope, "artifacts/"))
            .Should().Equal("artifacts/report.csv", "artifacts/report.json");
        (await store.ListDocumentKeysAsync(otherCompany, string.Empty)).Should().BeEmpty();
        (await store.GetDocumentAsync(otherCompany, "artifacts/report.json")).Should().BeNull();
        (await store.TryReadDocumentAsync(
            scope with { TenantId = $"{scope.TenantId}-other" },
            "artifacts/report.json")).Should().BeNull();
    }

    [ReportingDatabaseFact]
    public async Task WorkflowLease_CanceledContenderDoesNotPoisonReacquisition()
    {
        var firstStore = CreateStore();
        var contenderApplicationName = $"statement-authority-{Guid.NewGuid():N}";
        var secondStore = CreateStore(contenderApplicationName);
        var scope = NewScope();
        await using var firstLease = await firstStore.AcquireWorkflowLeaseAsync(scope);
        using var canceledWait = new CancellationTokenSource();

        var contendTask = secondStore
            .AcquireWorkflowLeaseAsync(scope, canceledWait.Token)
            .AsTask();
        await WaitForAdvisoryLockWaitAsync(contenderApplicationName);
        canceledWait.Cancel();

        var contend = async () => await contendTask;
        await contend.Should().ThrowAsync<OperationCanceledException>();
        await firstLease.DisposeAsync();

        using var reacquireTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reacquired = await secondStore.AcquireWorkflowLeaseAsync(
            scope,
            reacquireTimeout.Token);
        reacquired.Should().NotBeNull();
    }

    [ReportingDatabaseFact]
    public async Task WorkflowLease_MaximumPoolSizeOne_AllowsOperationsUnderLease()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(
            _database.Options.ConnectionString)
        {
            MaxPoolSize = 1,
            Timeout = 5,
            CommandTimeout = 5
        };
        var options = new ReportingArtifactStoreOptions
        {
            ConnectionString = connectionString.ConnectionString,
            Schema = _database.Options.Schema
        };
        var store = new PostgresStatementReconciliationReportAuthorityStore(options);
        var scope = NewScope();
        var content = Encoding.UTF8.GetBytes($"leased-{Guid.NewGuid():N}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var lease = await store.AcquireWorkflowLeaseAsync(scope, timeout.Token);
        var document = await store.WriteDocumentAsync(
            scope,
            "workflow.json",
            content,
            isImmutable: false,
            cancellationToken: timeout.Token);

        document.Version.Should().Be(1);
        (await store.TryReadDocumentAsync(scope, "workflow.json", timeout.Token))
            .Should().Equal(content);
    }

    [ReportingDatabaseFact]
    public async Task ProbeAsync_RequiresEnabledAuthorityAndRevisionGuards()
    {
        var store = CreateStore();
        (await _database.HasMigrationAsync(MigrationFileName)).Should().BeTrue();
        await store.ProbeAsync();

        await SetTriggerModeAsync(
            "trg_reporting_statement_revision_guard",
            TriggerMode.Disabled);
        try
        {
            var probe = () => store.ProbeAsync().AsTask();

            await probe.Should()
                .ThrowAsync<StatementReconciliationReportAuthorityUnavailableException>()
                .WithMessage("*missing its exact-scope keys*");
        }
        finally
        {
            await SetTriggerModeAsync(
                "trg_reporting_statement_revision_guard",
                TriggerMode.Enabled);
        }

        await store.ProbeAsync();

        await SetTriggerModeAsync(
            "trg_reporting_statement_revision_append",
            TriggerMode.ReplicaOnly);
        try
        {
            var probe = () => store.ProbeAsync().AsTask();

            await probe.Should()
                .ThrowAsync<StatementReconciliationReportAuthorityUnavailableException>()
                .WithMessage("*missing its exact-scope keys*");
        }
        finally
        {
            await SetTriggerModeAsync(
                "trg_reporting_statement_revision_append",
                TriggerMode.Enabled);
        }

        await store.ProbeAsync();

        await BindRevisionTruncateTriggerAsync(
            "reject_reporting_statement_document_truncate");
        try
        {
            var probe = () => store.ProbeAsync().AsTask();

            await probe.Should()
                .ThrowAsync<StatementReconciliationReportAuthorityUnavailableException>()
                .WithMessage("*missing its exact-scope keys*");
        }
        finally
        {
            await BindRevisionTruncateTriggerAsync(
                "reject_reporting_statement_revision_truncate");
        }

        await store.ProbeAsync();
    }

    [ReportingDatabaseFact]
    public async Task RevisionHistory_DirectInsertsUpdatesAndDeletesAreRejected()
    {
        var store = CreateStore();
        var scope = NewScope();
        const string documentKey = "workflow.json";
        await store.WriteDocumentAsync(
            scope,
            documentKey,
            Encoding.UTF8.GetBytes("snapshot"),
            isImmutable: false);

        var insert = () => ExecuteFutureRevisionInsertAsync(scope, documentKey);
        var update = () => ExecuteRevisionMutationAsync(
            mutation: "set recorded_at_utc = recorded_at_utc",
            isDelete: false,
            scope: scope,
            documentKey: documentKey);
        var delete = () => ExecuteRevisionMutationAsync(
            mutation: string.Empty,
            isDelete: true,
            scope: scope,
            documentKey: documentKey);

        (await insert.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("55000");
        (await update.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("55000");
        (await delete.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("55000");
        (await ReadRevisionsAsync(scope, documentKey)).Should().ContainSingle();

        var next = await store.WriteDocumentAsync(
            scope,
            documentKey,
            Encoding.UTF8.GetBytes("replacement"),
            isImmutable: false);
        next.Version.Should().Be(2);
        (await ReadRevisionsAsync(scope, documentKey)).Should().HaveCount(2);
    }

    [ReportingDatabaseFact]
    public async Task AuthorityTables_TruncateIsRejectedAndRetainedRowsRemainReadable()
    {
        var store = CreateStore();
        var scope = NewScope();
        const string documentKey = "workflow.json";
        var content = Encoding.UTF8.GetBytes($"snapshot-{Guid.NewGuid():N}");
        await store.WriteDocumentAsync(
            scope,
            documentKey,
            content,
            isImmutable: false);

        var truncateDocuments = () => ExecuteTruncateAsync(
            "reporting_statement_reconciliation_documents");
        var truncateRevisions = () => ExecuteTruncateAsync(
            "reporting_statement_reconciliation_document_revisions");

        (await truncateDocuments.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("55000");
        (await truncateRevisions.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("55000");
        (await store.TryReadDocumentAsync(scope, documentKey)).Should().Equal(content);
        (await ReadRevisionsAsync(scope, documentKey)).Should().ContainSingle();
    }

    [ReportingDatabaseFact]
    public async Task MappingTriggerFailure_RollsBackNewArtifactBlob()
    {
        var store = CreateStore();
        var scope = NewScope();
        var content = Encoding.UTF8.GetBytes($"trigger-failure-{Guid.NewGuid():N}");
        var identity = new ReportingArtifactIdentity(
            scope.TenantId,
            ComputeSha256(content));
        await CreateFailingDocumentInsertTriggerAsync();
        try
        {
            var write = () => store.WriteDocumentAsync(
                scope,
                "workflow.json",
                content,
                isImmutable: false).AsTask();

            (await write.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be("55000");
        }
        finally
        {
            await DropFailingDocumentInsertTriggerAsync();
        }

        (await _database.CountRowsAsync(identity)).Should().Be(0);
        (await store.GetDocumentAsync(scope, "workflow.json")).Should().BeNull();
    }

    [ReportingDatabaseFact]
    public async Task UnicodeCompositeIdentityBudget_IsEnforcedByCodeAndNamedSqlConstraint()
    {
        var store = CreateStore();
        var scope = NewScope();
        var documentKey = $"unicode/{new string('界', 650)}";
        var content = Encoding.UTF8.GetBytes($"unicode-{Guid.NewGuid():N}");
        var identity = new ReportingArtifactIdentity(
            scope.TenantId,
            ComputeSha256(content));

        var write = () => store.WriteDocumentAsync(
            scope,
            documentKey,
            content,
            isImmutable: true).AsTask();

        await write.Should().ThrowAsync<ArgumentException>()
            .WithMessage(
                $"*{PostgresStatementReconciliationReportAuthorityStore.MaximumCompositeIdentityUtf8Bytes} UTF-8 bytes*");
        (await _database.CountRowsAsync(identity)).Should().Be(0);

        var artifact = await _database.Store.StoreAsync(
            new ReportingArtifactWriteRequest(scope.TenantId, content));
        var directInsert = () => ExecuteDocumentInsertAsync(
            scope,
            documentKey,
            artifact,
            isImmutable: true);

        var sqlFailure = await directInsert.Should().ThrowAsync<PostgresException>();
        sqlFailure.Which.SqlState.Should().Be("23514");
        sqlFailure.Which.ConstraintName.Should().Be(
            "ck_reporting_statement_document_identity_utf8_bytes");
    }

    [ReportingDatabaseFact]
    public async Task ProbeAsync_MissingSchemaMapsToAuthorityUnavailable()
    {
        var options = new ReportingArtifactStoreOptions
        {
            ConnectionString = _database.Options.ConnectionString,
            Schema = $"missing_statement_{Guid.NewGuid():N}"
        };
        var store = new PostgresStatementReconciliationReportAuthorityStore(options);

        var probe = () => store.ProbeAsync().AsTask();

        var failure = await probe.Should()
            .ThrowAsync<StatementReconciliationReportAuthorityUnavailableException>();
        failure.Which.InnerException.Should().BeOfType<PostgresException>();
    }

    private PostgresStatementReconciliationReportAuthorityStore CreateStore(
        string? applicationName = null)
    {
        var options = _database.Options;
        if (applicationName is not null)
        {
            var connectionString =
                new NpgsqlConnectionStringBuilder(options.ConnectionString)
                {
                    ApplicationName = applicationName
                };
            options = new ReportingArtifactStoreOptions
            {
                ConnectionString = connectionString.ConnectionString,
                Schema = options.Schema
            };
        }

        return new PostgresStatementReconciliationReportAuthorityStore(
            options,
            new PostgresReportingArtifactStore(options));
    }

    private async Task WaitForAdvisoryLockWaitAsync(string applicationName)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                select exists (
                    select 1
                    from pg_catalog.pg_stat_activity
                    where application_name = @application_name
                      and wait_event_type = 'Lock'
                      and wait_event = 'advisory');
                """;
            command.Parameters.AddWithValue(
                "application_name",
                NpgsqlDbType.Text,
                applicationName);
            if ((bool)(await command.ExecuteScalarAsync() ?? false))
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException(
            "The contender did not reach a PostgreSQL advisory-lock wait.");
    }

    private async Task<IReadOnlyList<RevisionRow>> ReadRevisionsAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select document_version,
                   previous_content_hash_sha256,
                   previous_updated_at_utc,
                   content_hash_sha256,
                   mapping_updated_at_utc
            from "{_database.Options.Schema}"."reporting_statement_reconciliation_document_revisions"
            where tenant_id = @tenant_id
              and company_id = @company_id
              and workflow_id = @workflow_id
              and document_key = @document_key
            order by document_version;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, scope.TenantId);
        command.Parameters.AddWithValue("company_id", NpgsqlDbType.Text, scope.CompanyId);
        command.Parameters.AddWithValue("workflow_id", NpgsqlDbType.Text, scope.WorkflowId);
        command.Parameters.AddWithValue("document_key", NpgsqlDbType.Text, documentKey);

        var revisions = new List<RevisionRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            revisions.Add(new RevisionRow(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : ReadUtcTimestamp(reader, 2),
                reader.GetString(3),
                ReadUtcTimestamp(reader, 4)));
        }

        return revisions;
    }

    private async Task SetTriggerModeAsync(
        string triggerName,
        TriggerMode mode)
    {
        if (triggerName is not
            ("trg_reporting_statement_revision_append"
            or "trg_reporting_statement_revision_guard"))
        {
            throw new ArgumentOutOfRangeException(nameof(triggerName));
        }

        var modeSql = mode switch
        {
            TriggerMode.Enabled => "enable",
            TriggerMode.Disabled => "disable",
            TriggerMode.ReplicaOnly => "enable replica",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            alter table "{_database.Options.Schema}"."reporting_statement_reconciliation_document_revisions"
            {modeSql} trigger {triggerName};
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task BindRevisionTruncateTriggerAsync(string functionName)
    {
        if (functionName is not
            ("reject_reporting_statement_document_truncate"
            or "reject_reporting_statement_revision_truncate"))
        {
            throw new ArgumentOutOfRangeException(nameof(functionName));
        }

        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            drop trigger if exists trg_reporting_statement_revision_truncate_guard
                on "{_database.Options.Schema}"."reporting_statement_reconciliation_document_revisions";

            create trigger trg_reporting_statement_revision_truncate_guard
            before truncate
                on "{_database.Options.Schema}"."reporting_statement_reconciliation_document_revisions"
            for each statement
            execute function "{_database.Options.Schema}".{functionName}();
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteTruncateAsync(string tableName)
    {
        if (tableName is not
            ("reporting_statement_reconciliation_documents"
            or "reporting_statement_reconciliation_document_revisions"))
        {
            throw new ArgumentOutOfRangeException(nameof(tableName));
        }

        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"truncate table \"{_database.Options.Schema}\".\"{tableName}\";";
        await command.ExecuteNonQueryAsync();
    }

    private async Task CreateFailingDocumentInsertTriggerAsync()
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            create or replace function "{_database.Options.Schema}".fail_statement_document_insert_for_test()
            returns trigger
            language plpgsql
            as $$
            begin
                raise exception 'forced statement mapping insert failure'
                    using errcode = '55000';
                return null;
            end;
            $$;

            drop trigger if exists trg_fail_statement_document_insert_for_test
                on "{_database.Options.Schema}"."reporting_statement_reconciliation_documents";

            create trigger trg_fail_statement_document_insert_for_test
            before insert
                on "{_database.Options.Schema}"."reporting_statement_reconciliation_documents"
            for each row
            execute function "{_database.Options.Schema}".fail_statement_document_insert_for_test();
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task DropFailingDocumentInsertTriggerAsync()
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            drop trigger if exists trg_fail_statement_document_insert_for_test
                on "{_database.Options.Schema}"."reporting_statement_reconciliation_documents";
            drop function if exists
                "{_database.Options.Schema}".fail_statement_document_insert_for_test();
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteDocumentInsertAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        ReportingArtifactWriteResult artifact,
        bool isImmutable)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into "{_database.Options.Schema}"."reporting_statement_reconciliation_documents" (
                tenant_id,
                company_id,
                workflow_id,
                document_key,
                content_hash_sha256,
                byte_size,
                is_immutable,
                document_version)
            values (
                @tenant_id,
                @company_id,
                @workflow_id,
                @document_key,
                @content_hash_sha256,
                @byte_size,
                @is_immutable,
                1);
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, scope.TenantId);
        command.Parameters.AddWithValue("company_id", NpgsqlDbType.Text, scope.CompanyId);
        command.Parameters.AddWithValue("workflow_id", NpgsqlDbType.Text, scope.WorkflowId);
        command.Parameters.AddWithValue("document_key", NpgsqlDbType.Text, documentKey);
        command.Parameters.AddWithValue(
            "content_hash_sha256",
            NpgsqlDbType.Text,
            artifact.Identity.ContentHashSha256);
        command.Parameters.AddWithValue("byte_size", NpgsqlDbType.Bigint, artifact.ByteSize);
        command.Parameters.AddWithValue("is_immutable", NpgsqlDbType.Boolean, isImmutable);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteFutureRevisionInsertAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into "{_database.Options.Schema}"."reporting_statement_reconciliation_document_revisions" (
                tenant_id,
                company_id,
                workflow_id,
                document_key,
                document_version,
                previous_content_hash_sha256,
                previous_byte_size,
                previous_updated_at_utc,
                content_hash_sha256,
                byte_size,
                is_immutable,
                mapping_stored_at_utc,
                mapping_updated_at_utc)
            select tenant_id,
                   company_id,
                   workflow_id,
                   document_key,
                   document_version + 1,
                   content_hash_sha256,
                   byte_size,
                   updated_at_utc,
                   content_hash_sha256,
                   byte_size,
                   is_immutable,
                   stored_at_utc,
                   updated_at_utc
            from "{_database.Options.Schema}"."reporting_statement_reconciliation_documents"
            where tenant_id = @tenant_id
              and company_id = @company_id
              and workflow_id = @workflow_id
              and document_key = @document_key;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, scope.TenantId);
        command.Parameters.AddWithValue("company_id", NpgsqlDbType.Text, scope.CompanyId);
        command.Parameters.AddWithValue("workflow_id", NpgsqlDbType.Text, scope.WorkflowId);
        command.Parameters.AddWithValue("document_key", NpgsqlDbType.Text, documentKey);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteRevisionMutationAsync(
        string mutation,
        bool isDelete,
        StatementReconciliationReportAuthorityScope scope,
        string documentKey)
    {
        await using var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = isDelete
            ? $"""
               delete from "{_database.Options.Schema}"."reporting_statement_reconciliation_document_revisions"
               where tenant_id = @tenant_id
                 and company_id = @company_id
                 and workflow_id = @workflow_id
                 and document_key = @document_key;
               """
            : $"""
               update "{_database.Options.Schema}"."reporting_statement_reconciliation_document_revisions"
               {mutation}
               where tenant_id = @tenant_id
                 and company_id = @company_id
                 and workflow_id = @workflow_id
                 and document_key = @document_key;
               """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, scope.TenantId);
        command.Parameters.AddWithValue("company_id", NpgsqlDbType.Text, scope.CompanyId);
        command.Parameters.AddWithValue("workflow_id", NpgsqlDbType.Text, scope.WorkflowId);
        command.Parameters.AddWithValue("document_key", NpgsqlDbType.Text, documentKey);
        await command.ExecuteNonQueryAsync();
    }

    private static StatementReconciliationReportAuthorityScope NewScope() =>
        new(
            $"tenant-{Guid.NewGuid():N}",
            $"company-{Guid.NewGuid():N}",
            $"workflow-{Guid.NewGuid():N}");

    private static string ComputeSha256(byte[] content) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content))
            .ToLowerInvariant();

    private static DateTimeOffset ReadUtcTimestamp(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private sealed record RevisionRow(
        long Version,
        string? PreviousContentHash,
        DateTimeOffset? PreviousUpdatedAtUtc,
        string ContentHash,
        DateTimeOffset MappingUpdatedAtUtc);

    private enum TriggerMode
    {
        Enabled,
        Disabled,
        ReplicaOnly
    }
}

public sealed class StatementReconciliationReportAuthorityStoreValidationTests
{
    private static readonly ReportingArtifactStoreOptions Options = new()
    {
        ConnectionString =
            "Host=127.0.0.1;Database=meridian;Username=meridian;Password=meridian",
        Schema = "reporting"
    };

    [Theory]
    [InlineData("../workflow.json")]
    [InlineData("input/../workflow.json")]
    [InlineData("/input/statement.csv")]
    [InlineData("input\\statement.csv")]
    [InlineData("input//statement.csv")]
    [InlineData("input/statement.csv/")]
    public async Task WriteDocumentAsync_RejectsNonCanonicalOrTraversingKeys(
        string documentKey)
    {
        var store = new PostgresStatementReconciliationReportAuthorityStore(Options);
        var scope = new StatementReconciliationReportAuthorityScope(
            "tenant",
            "company",
            "workflow");

        var write = () => store.WriteDocumentAsync(
            scope,
            documentKey,
            Encoding.UTF8.GetBytes("statement"),
            isImmutable: true).AsTask();

        await write.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteDocumentAsync_RejectsCompositeUnicodeIdentityOverUtf8BudgetBeforeIo()
    {
        var store = new PostgresStatementReconciliationReportAuthorityStore(Options);
        var scope = new StatementReconciliationReportAuthorityScope(
            "tenant",
            "company",
            "workflow");
        var documentKey = $"unicode/{new string('界', 700)}";

        var write = () => store.WriteDocumentAsync(
            scope,
            documentKey,
            Encoding.UTF8.GetBytes("statement"),
            isImmutable: true).AsTask();

        await write.Should().ThrowAsync<ArgumentException>()
            .WithMessage(
                $"*{PostgresStatementReconciliationReportAuthorityStore.MaximumCompositeIdentityUtf8Bytes} UTF-8 bytes*");
    }

    [Fact]
    public async Task WriteDocumentAsync_NonTransactionalInjectedArtifactStoreFailsClosed()
    {
        var artifactStore = new UnsupportedArtifactStore();
        var store = new PostgresStatementReconciliationReportAuthorityStore(
            Options,
            artifactStore);
        var scope = new StatementReconciliationReportAuthorityScope(
            "tenant",
            "company",
            "workflow");

        var write = () => store.WriteDocumentAsync(
            scope,
            "workflow.json",
            Encoding.UTF8.GetBytes("statement"),
            isImmutable: false).AsTask();

        store.IsDurableAuthority.Should().BeFalse();
        await write.Should()
            .ThrowAsync<StatementReconciliationReportAuthorityUnavailableException>()
            .WithMessage("*transactionally compose immutable bytes*");
        artifactStore.StoreCallCount.Should().Be(0);
    }

    private sealed class UnsupportedArtifactStore : IReportingArtifactStore
    {
        public int StoreCallCount { get; private set; }

        public Task<ReportingArtifactWriteResult> StoreAsync(
            ReportingArtifactWriteRequest request,
            CancellationToken ct = default)
        {
            StoreCallCount++;
            throw new NotSupportedException();
        }

        public Task<ReportingArtifactReadResult> ReadAsync(
            ReportingArtifactIdentity identity,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
