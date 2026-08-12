using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Integrity;
using Meridian.Reporting;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Reporting;

/// <summary>
/// PostgreSQL authority for statement-reconciliation workflow documents. Logical document
/// mappings are keyed by the complete tenant/company/workflow identity and refer only to verified,
/// immutable bytes in the reporting artifact store.
/// </summary>
public sealed class PostgresStatementReconciliationReportAuthorityStore :
    IStatementReconciliationReportAuthorityStore
{
    private const int MaximumScopeIdentityLength = 256;
    private const int MaximumDocumentKeyLength = 1024;
    internal const int MaximumCompositeIdentityUtf8Bytes = 2048;
    private const string WorkflowLockNamespace =
        "meridian:reporting:statement-reconciliation:workflow:";
    private const string DocumentLockNamespace =
        "meridian:reporting:statement-reconciliation:document:";

    private readonly ReportingArtifactStoreOptions _options;
    private readonly IReportingArtifactStore _artifactStore;
    private readonly PostgresReportingArtifactStore? _transactionalArtifactStore;
    private readonly string _workflowLeaseConnectionString;
    private readonly string _documentTable;
    private readonly string _revisionTable;

    public PostgresStatementReconciliationReportAuthorityStore(
        ReportingArtifactStoreOptions options)
        : this(options, new PostgresReportingArtifactStore(options))
    {
    }

    public PostgresStatementReconciliationReportAuthorityStore(
        ReportingArtifactStoreOptions options,
        IReportingArtifactStore artifactStore)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ValidateIdentifier(_options.Schema, nameof(options.Schema));
        _transactionalArtifactStore = artifactStore as PostgresReportingArtifactStore;
        if (_transactionalArtifactStore is not null
            && !_transactionalArtifactStore.SupportsTransactionalComposition(_options))
        {
            _transactionalArtifactStore = null;
        }

        var workflowLeaseConnection = new NpgsqlConnectionStringBuilder(
            _options.ConnectionString)
        {
            Pooling = false,
            Multiplexing = false
        };
        _workflowLeaseConnectionString = workflowLeaseConnection.ConnectionString;
        _documentTable =
            $"\"{_options.Schema}\".\"reporting_statement_reconciliation_documents\"";
        _revisionTable =
            $"\"{_options.Schema}\".\"reporting_statement_reconciliation_document_revisions\"";
    }

    public bool IsDurableAuthority => _transactionalArtifactStore is not null;

    public string StorageKind => "postgres-reporting-statement-reconciliation-authority";

    public ValueTask<IAsyncDisposable> AcquireWorkflowLeaseAsync(
        StatementReconciliationReportAuthorityScope scope,
        CancellationToken cancellationToken = default) =>
        ExecuteAuthorityOperationAsync(
            "acquire a workflow lease",
            () => AcquireWorkflowLeaseCoreAsync(scope, cancellationToken));

    private async ValueTask<IAsyncDisposable> AcquireWorkflowLeaseCoreAsync(
        StatementReconciliationReportAuthorityScope scope,
        CancellationToken cancellationToken)
    {
        var normalizedScope = NormalizeScope(scope);
        var lockKey = ComputeLockKey(
            WorkflowLockNamespace,
            _options.Schema,
            normalizedScope,
            documentKey: null);
        // A workflow lease is intentionally held on an unpooled, non-multiplexed physical session.
        // It must not consume the only pooled slot while operations under the lease open their
        // ordinary authority transaction (for example when Maximum Pool Size is one).
        var connection = new NpgsqlConnection(_workflowLeaseConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "select pg_advisory_lock(@lock_key);";
            command.Parameters.AddWithValue("lock_key", NpgsqlDbType.Bigint, lockKey);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new WorkflowLease(connection, lockKey);
        }
        catch
        {
            // Lock acquisition can succeed on the server while cancellation or a network break
            // prevents the acknowledgement reaching this process. Do not return that physical
            // session to the pool with an outcome-uncertain session advisory lock.
            NpgsqlConnection.ClearPool(connection);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask<bool> DocumentExistsAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAuthorityOperationAsync(
            "check a document mapping",
            () => DocumentExistsCoreAsync(scope, documentKey, cancellationToken));

    private async ValueTask<bool> DocumentExistsCoreAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken cancellationToken)
    {
        var normalizedScope = NormalizeScope(scope);
        var normalizedDocumentKey = NormalizeDocumentKey(documentKey);
        ValidateCompositeIdentityUtf8Budget(normalizedScope, normalizedDocumentKey);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select exists (
                select 1
                from {_documentTable}
                where tenant_id = @tenant_id
                  and company_id = @company_id
                  and workflow_id = @workflow_id
                  and document_key = @document_key);
            """;
        AddScopeParameters(command, normalizedScope);
        command.Parameters.AddWithValue(
            "document_key",
            NpgsqlDbType.Text,
            normalizedDocumentKey);
        return (bool)(await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false) ?? false);
    }

    public ValueTask<StatementReconciliationReportAuthorityDocument?> GetDocumentAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAuthorityOperationAsync(
            "read a document mapping",
            () => GetDocumentCoreAsync(scope, documentKey, cancellationToken));

    private async ValueTask<StatementReconciliationReportAuthorityDocument?> GetDocumentCoreAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken cancellationToken)
    {
        var normalizedScope = NormalizeScope(scope);
        var normalizedDocumentKey = NormalizeDocumentKey(documentKey);
        ValidateCompositeIdentityUtf8Budget(normalizedScope, normalizedDocumentKey);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadDocumentAsync(
                connection,
                transaction: null,
                normalizedScope,
                normalizedDocumentKey,
                forUpdate: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask<byte[]?> TryReadDocumentAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAuthorityOperationAsync(
            "read verified document bytes",
            () => TryReadDocumentCoreAsync(scope, documentKey, cancellationToken));

    private async ValueTask<byte[]?> TryReadDocumentCoreAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(scope, documentKey, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var retained = await _artifactStore
            .ReadAsync(document.Identity, cancellationToken)
            .ConfigureAwait(false);
        if (retained.Identity != document.Identity
            || retained.ByteSize != document.ByteSize
            || retained.Content.LongLength != document.ByteSize)
        {
            throw new ReportingArtifactIntegrityException(
                document.Identity,
                "the statement-reconciliation mapping does not match the verified retained artifact");
        }

        return retained.Content;
    }

    public ValueTask<StatementReconciliationReportAuthorityDocument> WriteDocumentAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        ReadOnlyMemory<byte> content,
        bool isImmutable,
        CancellationToken cancellationToken = default) =>
        ExecuteAuthorityOperationAsync(
            "retain a document mapping",
            () => WriteDocumentCoreAsync(
                scope,
                documentKey,
                content,
                isImmutable,
                cancellationToken));

    private async ValueTask<StatementReconciliationReportAuthorityDocument> WriteDocumentCoreAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        ReadOnlyMemory<byte> content,
        bool isImmutable,
        CancellationToken cancellationToken)
    {
        var normalizedScope = NormalizeScope(scope);
        var normalizedDocumentKey = NormalizeDocumentKey(documentKey);
        ValidateCompositeIdentityUtf8Budget(normalizedScope, normalizedDocumentKey);
        if (content.IsEmpty)
        {
            throw new ArgumentException(
                "Statement-reconciliation authority documents cannot be empty.",
                nameof(content));
        }

        // Caller-owned memory may be mutated while persistence is awaiting I/O. Retain one exact
        // byte sequence for both the independently computed identity and the artifact-store write.
        var retainedContent = content.ToArray();
        var expectedHash = ComputeSha256(retainedContent);
        var transactionalArtifactStore = RequireTransactionalArtifactStore();

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        await AcquireDocumentLockAsync(
                connection,
                transaction,
                normalizedScope,
                normalizedDocumentKey,
                cancellationToken)
            .ConfigureAwait(false);

        var existing = await ReadDocumentAsync(
                connection,
                transaction,
                normalizedScope,
                normalizedDocumentKey,
                forUpdate: true,
                cancellationToken)
            .ConfigureAwait(false);
        var artifact = await transactionalArtifactStore
            .StoreWithinTransactionAsync(
                new ReportingArtifactWriteRequest(normalizedScope.TenantId, retainedContent),
                connection,
                transaction,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                artifact.Identity.TenantId,
                normalizedScope.TenantId,
                StringComparison.Ordinal)
            || !string.Equals(
                artifact.Identity.ContentHashSha256,
                expectedHash,
                StringComparison.Ordinal)
            || artifact.ByteSize != retainedContent.LongLength)
        {
            throw new ReportingArtifactIntegrityException(
                artifact.Identity,
                "the artifact-store write receipt does not match the submitted tenant, bytes, and SHA-256 identity");
        }

        StatementReconciliationReportAuthorityDocument retainedDocument;
        if (existing is null)
        {
            retainedDocument = await InsertDocumentAsync(
                    connection,
                    transaction,
                    normalizedScope,
                    normalizedDocumentKey,
                    artifact,
                    isImmutable,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (existing.IsImmutable)
        {
            if (!isImmutable
                || existing.Identity != artifact.Identity
                || existing.ByteSize != artifact.ByteSize)
            {
                throw CreateMappingConflict(
                    normalizedScope,
                    normalizedDocumentKey,
                    "an immutable retained mapping cannot be replaced or made mutable");
            }

            retainedDocument = existing;
        }
        else
        {
            if (isImmutable)
            {
                throw CreateMappingConflict(
                    normalizedScope,
                    normalizedDocumentKey,
                    "a mutable retained mapping cannot change its retention policy");
            }

            // A retry after an acknowledgement-lost commit must not manufacture another
            // authoritative version for the same retained bytes.
            retainedDocument = existing.Identity == artifact.Identity
                && existing.ByteSize == artifact.ByteSize
                    ? existing
                    : await UpdateMutableDocumentAsync(
                            connection,
                            transaction,
                            normalizedScope,
                            normalizedDocumentKey,
                            artifact,
                            cancellationToken)
                        .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return retainedDocument;
    }

    public ValueTask<IReadOnlyList<string>> ListDocumentKeysAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKeyPrefix,
        CancellationToken cancellationToken = default) =>
        ExecuteAuthorityOperationAsync(
            "list document mappings",
            () => ListDocumentKeysCoreAsync(
                scope,
                documentKeyPrefix,
                cancellationToken));

    private async ValueTask<IReadOnlyList<string>> ListDocumentKeysCoreAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKeyPrefix,
        CancellationToken cancellationToken)
    {
        var normalizedScope = NormalizeScope(scope);
        var normalizedPrefix = NormalizeDocumentKeyPrefix(documentKeyPrefix);
        ValidateCompositeIdentityUtf8Budget(normalizedScope, normalizedPrefix);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select document_key
            from {_documentTable}
            where tenant_id = @tenant_id
              and company_id = @company_id
              and workflow_id = @workflow_id
              and left(document_key, char_length(@document_key_prefix)) = @document_key_prefix
            order by document_key;
            """;
        AddScopeParameters(command, normalizedScope);
        command.Parameters.AddWithValue(
            "document_key_prefix",
            NpgsqlDbType.Text,
            normalizedPrefix);

        var keys = new List<string>();
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    public ValueTask ProbeAsync(CancellationToken cancellationToken = default) =>
        ExecuteAuthorityOperationAsync(
            "probe the retained authority schema",
            () => ProbeCoreAsync(cancellationToken));

    private async ValueTask ProbeCoreAsync(CancellationToken cancellationToken)
    {
        _ = RequireTransactionalArtifactStore();
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (var shapeCommand = connection.CreateCommand())
        {
            shapeCommand.CommandText =
            $"""
            select documents.tenant_id,
                   documents.company_id,
                   documents.workflow_id,
                   documents.document_key,
                   documents.content_hash_sha256,
                   documents.byte_size,
                   documents.is_immutable,
                   documents.document_version,
                   documents.stored_at_utc,
                   documents.updated_at_utc,
                   revisions.previous_content_hash_sha256,
                   revisions.previous_byte_size,
                   revisions.previous_updated_at_utc,
                   revisions.recorded_at_utc
            from {_documentTable} documents
            cross join {_revisionTable} revisions
            where false;
            """;
            await shapeCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var authorityCommand = connection.CreateCommand();
        authorityCommand.CommandText =
            """
            select
                exists (
                    select 1
                    from pg_catalog.pg_constraint
                    where conrelid = to_regclass(@document_table)
                      and contype = 'p'
                      and pg_get_constraintdef(oid) =
                          'PRIMARY KEY (tenant_id, company_id, workflow_id, document_key)')
                and exists (
                    select 1
                    from pg_catalog.pg_constraint
                    where conrelid = to_regclass(@document_table)
                      and conname = 'fk_reporting_statement_document_blob'
                      and contype = 'f')
                and exists (
                    select 1
                    from pg_catalog.pg_constraint
                    where conrelid = to_regclass(@document_table)
                      and conname = 'ck_reporting_statement_document_key'
                      and contype = 'c')
                and exists (
                    select 1
                    from pg_catalog.pg_constraint
                    where conrelid = to_regclass(@document_table)
                      and conname = 'ck_reporting_statement_document_identity_utf8_bytes'
                      and contype = 'c'
                      and convalidated
                      and position(
                          'octet_lengthtenant_id+octet_lengthcompany_id+octet_lengthworkflow_id+octet_lengthdocument_key<=2048'
                          in regexp_replace(
                              lower(pg_get_constraintdef(oid)),
                              '[[:space:]()]',
                              '',
                              'g')) > 0)
                and exists (
                    select 1
                    from pg_catalog.pg_trigger
                    where tgrelid = to_regclass(@document_table)
                      and tgname = 'trg_reporting_statement_document_guard'
                      and not tgisinternal
                      and tgenabled in ('O', 'A')
                      and tgtype = 27
                      and tgfoid = to_regprocedure(format(
                          '%I.guard_reporting_statement_document_mutation()',
                          @schema)))
                and exists (
                    select 1
                    from pg_catalog.pg_trigger
                    where tgrelid = to_regclass(@document_table)
                      and tgname = 'trg_reporting_statement_document_truncate_guard'
                      and not tgisinternal
                      and tgenabled in ('O', 'A')
                      and tgtype = 34
                      and tgfoid = to_regprocedure(format(
                          '%I.reject_reporting_statement_document_truncate()',
                          @schema)))
                and exists (
                    select 1
                    from pg_catalog.pg_trigger
                    where tgrelid = to_regclass(@document_table)
                      and tgname = 'trg_reporting_statement_document_revision'
                      and not tgisinternal
                      and tgenabled in ('O', 'A')
                      and tgtype = 21
                      and tgfoid = to_regprocedure(format(
                          '%I.retain_reporting_statement_document_revision()',
                          @schema)))
                and exists (
                    select 1
                    from pg_catalog.pg_constraint
                    where conrelid = to_regclass(@revision_table)
                      and contype = 'p'
                      and pg_get_constraintdef(oid) =
                          'PRIMARY KEY (tenant_id, company_id, workflow_id, document_key, document_version)')
                and exists (
                    select 1
                    from pg_catalog.pg_constraint
                    where conrelid = to_regclass(@revision_table)
                      and conname = 'fk_reporting_statement_revision_blob'
                      and contype = 'f')
                and exists (
                    select 1
                    from pg_catalog.pg_constraint
                    where conrelid = to_regclass(@revision_table)
                      and conname = 'fk_reporting_statement_revision_previous_blob'
                      and contype = 'f')
                and exists (
                    select 1
                    from pg_catalog.pg_constraint
                    where conrelid = to_regclass(@revision_table)
                      and conname = 'ck_reporting_statement_revision_chain'
                      and contype = 'c')
                and exists (
                    select 1
                    from pg_catalog.pg_constraint
                    where conrelid = to_regclass(@revision_table)
                      and conname = 'ck_reporting_statement_revision_identity_utf8_bytes'
                      and contype = 'c'
                      and convalidated
                      and position(
                          'octet_lengthtenant_id+octet_lengthcompany_id+octet_lengthworkflow_id+octet_lengthdocument_key<=2048'
                          in regexp_replace(
                              lower(pg_get_constraintdef(oid)),
                              '[[:space:]()]',
                              '',
                              'g')) > 0)
                and exists (
                    select 1
                    from pg_catalog.pg_trigger
                    where tgrelid = to_regclass(@revision_table)
                      and tgname = 'trg_reporting_statement_revision_append'
                      and not tgisinternal
                      and tgenabled in ('O', 'A')
                      and tgtype = 7
                      and tgfoid = to_regprocedure(format(
                          '%I.validate_reporting_statement_revision_append()',
                          @schema)))
                and exists (
                    select 1
                    from pg_catalog.pg_trigger
                    where tgrelid = to_regclass(@revision_table)
                      and tgname = 'trg_reporting_statement_revision_guard'
                      and not tgisinternal
                      and tgenabled in ('O', 'A')
                      and tgtype = 27
                      and tgfoid = to_regprocedure(format(
                          '%I.guard_reporting_statement_revision_mutation()',
                          @schema)))
                and exists (
                    select 1
                    from pg_catalog.pg_trigger
                    where tgrelid = to_regclass(@revision_table)
                      and tgname = 'trg_reporting_statement_revision_truncate_guard'
                      and not tgisinternal
                      and tgenabled in ('O', 'A')
                      and tgtype = 34
                      and tgfoid = to_regprocedure(format(
                          '%I.reject_reporting_statement_revision_truncate()',
                          @schema)));
            """;
        authorityCommand.Parameters.AddWithValue(
            "schema",
            NpgsqlDbType.Text,
            _options.Schema);
        authorityCommand.Parameters.AddWithValue(
            "document_table",
            NpgsqlDbType.Text,
            _documentTable);
        authorityCommand.Parameters.AddWithValue(
            "revision_table",
            NpgsqlDbType.Text,
            _revisionTable);
        var authorityReady = (bool)(await authorityCommand
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false) ?? false);
        if (!authorityReady)
        {
            throw new StatementReconciliationReportAuthorityUnavailableException(
                "The PostgreSQL statement-reconciliation authority is missing its exact-scope keys, UTF-8 identity budget, blob references, or mutation guards.");
        }
    }

    internal static string NormalizeDocumentKey(string documentKey)
    {
        ArgumentNullException.ThrowIfNull(documentKey);
        if (documentKey.Length == 0
            || documentKey.Length > MaximumDocumentKeyLength
            || !string.Equals(documentKey, documentKey.Trim(), StringComparison.Ordinal)
            || documentKey[0] == '/'
            || documentKey[^1] == '/'
            || documentKey.Contains('\\')
            || documentKey.Contains("//", StringComparison.Ordinal)
            || documentKey.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Statement-reconciliation document keys must be canonical relative paths.",
                nameof(documentKey));
        }

        var segments = documentKey.Split('/');
        if (segments.Any(static segment =>
                segment.Length == 0
                || segment is "." or ".."))
        {
            throw new ArgumentException(
                "Statement-reconciliation document keys cannot contain empty or traversal segments.",
                nameof(documentKey));
        }

        return documentKey;
    }

    internal static string NormalizeDocumentKeyPrefix(string documentKeyPrefix)
    {
        ArgumentNullException.ThrowIfNull(documentKeyPrefix);
        if (documentKeyPrefix.Length == 0)
        {
            return string.Empty;
        }

        if (documentKeyPrefix.Length > MaximumDocumentKeyLength
            || !string.Equals(
                documentKeyPrefix,
                documentKeyPrefix.Trim(),
                StringComparison.Ordinal)
            || documentKeyPrefix[0] == '/'
            || documentKeyPrefix.Contains('\\')
            || documentKeyPrefix.Contains("//", StringComparison.Ordinal)
            || documentKeyPrefix.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Statement-reconciliation document-key prefixes must be canonical relative paths.",
                nameof(documentKeyPrefix));
        }

        var path = documentKeyPrefix[^1] == '/'
            ? documentKeyPrefix[..^1]
            : documentKeyPrefix;
        if (path.Length == 0
            || path.Split('/').Any(static segment =>
                segment.Length == 0
                || segment is "." or ".."))
        {
            throw new ArgumentException(
                "Statement-reconciliation document-key prefixes cannot contain empty or traversal segments.",
                nameof(documentKeyPrefix));
        }

        return documentKeyPrefix;
    }

    private async Task<StatementReconciliationReportAuthorityDocument> InsertDocumentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        ReportingArtifactWriteResult artifact,
        bool isImmutable,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {_documentTable} (
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
                1)
            returning tenant_id,
                      company_id,
                      workflow_id,
                      document_key,
                      content_hash_sha256,
                      byte_size,
                      is_immutable,
                      document_version,
                      stored_at_utc,
                      updated_at_utc;
            """;
        AddScopeParameters(command, scope);
        AddDocumentWriteParameters(command, documentKey, artifact, isImmutable);
        return await ReadRequiredReturnedDocumentAsync(
                command,
                scope,
                documentKey,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<StatementReconciliationReportAuthorityDocument> UpdateMutableDocumentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        ReportingArtifactWriteResult artifact,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            update {_documentTable}
            set content_hash_sha256 = @content_hash_sha256,
                byte_size = @byte_size,
                document_version = document_version + 1,
                updated_at_utc = clock_timestamp()
            where tenant_id = @tenant_id
              and company_id = @company_id
              and workflow_id = @workflow_id
              and document_key = @document_key
              and not is_immutable
            returning tenant_id,
                      company_id,
                      workflow_id,
                      document_key,
                      content_hash_sha256,
                      byte_size,
                      is_immutable,
                      document_version,
                      stored_at_utc,
                      updated_at_utc;
            """;
        AddScopeParameters(command, scope);
        command.Parameters.AddWithValue("document_key", NpgsqlDbType.Text, documentKey);
        command.Parameters.AddWithValue(
            "content_hash_sha256",
            NpgsqlDbType.Text,
            artifact.Identity.ContentHashSha256);
        command.Parameters.AddWithValue("byte_size", NpgsqlDbType.Bigint, artifact.ByteSize);
        return await ReadRequiredReturnedDocumentAsync(
                command,
                scope,
                documentKey,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<StatementReconciliationReportAuthorityDocument?>
        ReadDocumentAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction? transaction,
            StatementReconciliationReportAuthorityScope scope,
            string documentKey,
            bool forUpdate,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select tenant_id,
                   company_id,
                   workflow_id,
                   document_key,
                   content_hash_sha256,
                   byte_size,
                   is_immutable,
                   document_version,
                   stored_at_utc,
                   updated_at_utc
            from {_documentTable}
            where tenant_id = @tenant_id
              and company_id = @company_id
              and workflow_id = @workflow_id
              and document_key = @document_key
            {(forUpdate ? "for update" : string.Empty)};
            """;
        AddScopeParameters(command, scope);
        command.Parameters.AddWithValue("document_key", NpgsqlDbType.Text, documentKey);

        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadDocument(reader, scope, documentKey);
    }

    private static async Task<StatementReconciliationReportAuthorityDocument>
        ReadRequiredReturnedDocumentAsync(
            NpgsqlCommand command,
            StatementReconciliationReportAuthorityScope scope,
            string documentKey,
            CancellationToken cancellationToken)
    {
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw CreateMappingConflict(
                scope,
                documentKey,
                "the retained mapping changed before the write completed");
        }

        return ReadDocument(reader, scope, documentKey);
    }

    private static StatementReconciliationReportAuthorityDocument ReadDocument(
        NpgsqlDataReader reader,
        StatementReconciliationReportAuthorityScope expectedScope,
        string expectedDocumentKey)
    {
        var tenantId = reader.GetString(0);
        var companyId = reader.GetString(1);
        var workflowId = reader.GetString(2);
        var documentKey = reader.GetString(3);
        var contentHash = reader.GetString(4);
        var byteSize = reader.GetInt64(5);
        var isImmutable = reader.GetBoolean(6);
        var version = reader.GetInt64(7);
        var storedAtUtc = ReadUtcTimestamp(reader, 8);
        var updatedAtUtc = ReadUtcTimestamp(reader, 9);
        if (!string.Equals(tenantId, expectedScope.TenantId, StringComparison.Ordinal)
            || !string.Equals(companyId, expectedScope.CompanyId, StringComparison.Ordinal)
            || !string.Equals(workflowId, expectedScope.WorkflowId, StringComparison.Ordinal)
            || !string.Equals(documentKey, expectedDocumentKey, StringComparison.Ordinal)
            || !Sha256Digest.IsCanonical(contentHash)
            || byteSize <= 0
            || version <= 0
            || updatedAtUtc < storedAtUtc)
        {
            throw new InvalidDataException(
                $"Retained statement-reconciliation authority metadata for '{expectedScope.TenantId}/" +
                $"{expectedScope.CompanyId}/{expectedScope.WorkflowId}/{expectedDocumentKey}' is corrupt.");
        }

        var scope = new StatementReconciliationReportAuthorityScope(
            tenantId,
            companyId,
            workflowId);
        return new StatementReconciliationReportAuthorityDocument(
            scope,
            documentKey,
            new ReportingArtifactIdentity(tenantId, contentHash),
            byteSize,
            isImmutable,
            version,
            storedAtUtc,
            updatedAtUtc);
    }

    private async Task AcquireDocumentLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken cancellationToken)
    {
        var lockKey = ComputeLockKey(
            DocumentLockNamespace,
            _options.Schema,
            scope,
            documentKey);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(@lock_key);";
        command.Parameters.AddWithValue("lock_key", NpgsqlDbType.Bigint, lockKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static StatementReconciliationReportAuthorityScope NormalizeScope(
        StatementReconciliationReportAuthorityScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var normalized = new StatementReconciliationReportAuthorityScope(
            NormalizeScopeIdentity(scope.TenantId, nameof(scope.TenantId)),
            NormalizeScopeIdentity(scope.CompanyId, nameof(scope.CompanyId)),
            NormalizeScopeIdentity(scope.WorkflowId, nameof(scope.WorkflowId)));
        ValidateCompositeIdentityUtf8Budget(normalized, documentKey: null);
        return normalized;
    }

    private static string NormalizeScopeIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaximumScopeIdentityLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"{parameterName} must be a canonical identifier no longer than " +
                $"{MaximumScopeIdentityLength} characters.",
                parameterName);
        }

        return value;
    }

    private static void ValidateCompositeIdentityUtf8Budget(
        StatementReconciliationReportAuthorityScope scope,
        string? documentKey)
    {
        var byteCount = Encoding.UTF8.GetByteCount(scope.TenantId)
            + Encoding.UTF8.GetByteCount(scope.CompanyId)
            + Encoding.UTF8.GetByteCount(scope.WorkflowId)
            + (documentKey is null ? 0 : Encoding.UTF8.GetByteCount(documentKey));
        if (byteCount > MaximumCompositeIdentityUtf8Bytes)
        {
            throw new ArgumentException(
                "The composite statement-reconciliation tenant/company/workflow/document identity " +
                $"cannot exceed {MaximumCompositeIdentityUtf8Bytes} UTF-8 bytes.");
        }
    }

    private PostgresReportingArtifactStore RequireTransactionalArtifactStore() =>
        _transactionalArtifactStore
        ?? throw new StatementReconciliationReportAuthorityUnavailableException(
            "The PostgreSQL statement-reconciliation authority requires an artifact store that " +
            "can transactionally compose immutable bytes with the authority mapping.");

    private static void AddScopeParameters(
        NpgsqlCommand command,
        StatementReconciliationReportAuthorityScope scope)
    {
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, scope.TenantId);
        command.Parameters.AddWithValue("company_id", NpgsqlDbType.Text, scope.CompanyId);
        command.Parameters.AddWithValue("workflow_id", NpgsqlDbType.Text, scope.WorkflowId);
    }

    private static void AddDocumentWriteParameters(
        NpgsqlCommand command,
        string documentKey,
        ReportingArtifactWriteResult artifact,
        bool isImmutable)
    {
        command.Parameters.AddWithValue("document_key", NpgsqlDbType.Text, documentKey);
        command.Parameters.AddWithValue(
            "content_hash_sha256",
            NpgsqlDbType.Text,
            artifact.Identity.ContentHashSha256);
        command.Parameters.AddWithValue("byte_size", NpgsqlDbType.Bigint, artifact.ByteSize);
        command.Parameters.AddWithValue("is_immutable", NpgsqlDbType.Boolean, isImmutable);
    }

    private static InvalidOperationException CreateMappingConflict(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        string reason) =>
        new(
            $"Statement-reconciliation authority mapping '{scope.TenantId}/{scope.CompanyId}/" +
            $"{scope.WorkflowId}/{documentKey}' conflicts with retained state: {reason}.");

    private static long ComputeLockKey(
        string lockNamespace,
        string schema,
        StatementReconciliationReportAuthorityScope scope,
        string? documentKey)
    {
        var canonicalIdentity =
            $"{lockNamespace}{schema}\n{scope.TenantId}\n{scope.CompanyId}\n{scope.WorkflowId}";
        if (documentKey is not null)
        {
            canonicalIdentity += $"\n{documentKey}";
        }

        return BinaryPrimitives.ReadInt64BigEndian(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdentity)));
    }

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static DateTimeOffset ReadUtcTimestamp(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !(char.IsAsciiLetter(value[0]) || value[0] == '_')
            || !value.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            throw new ArgumentException(
                $"PostgreSQL identifier '{value}' is not supported. Use letters, digits, and " +
                "underscores, and start with a letter or underscore.",
                parameterName);
        }
    }

    private static async ValueTask<T> ExecuteAuthorityOperationAsync<T>(
        string operation,
        Func<ValueTask<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (StatementReconciliationReportAuthorityUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (IsAuthorityAvailabilityFailure(ex))
        {
            throw new StatementReconciliationReportAuthorityUnavailableException(
                $"The PostgreSQL statement-reconciliation authority is unavailable while attempting to {operation}.",
                ex);
        }
    }

    private static async ValueTask ExecuteAuthorityOperationAsync(
        string operation,
        Func<ValueTask> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (StatementReconciliationReportAuthorityUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (IsAuthorityAvailabilityFailure(ex))
        {
            throw new StatementReconciliationReportAuthorityUnavailableException(
                $"The PostgreSQL statement-reconciliation authority is unavailable while attempting to {operation}.",
                ex);
        }
    }

    private static bool IsAuthorityAvailabilityFailure(Exception exception)
    {
        if (exception is TimeoutException)
        {
            return true;
        }

        if (exception is not NpgsqlException npgsqlException)
        {
            return false;
        }

        if (npgsqlException is not PostgresException postgresException)
        {
            return true;
        }

        var sqlState = postgresException.SqlState;
        return sqlState.StartsWith("08", StringComparison.Ordinal)
               || sqlState.StartsWith("53", StringComparison.Ordinal)
               || sqlState.StartsWith("58", StringComparison.Ordinal)
               || sqlState.StartsWith("XX", StringComparison.Ordinal)
               || sqlState is
                   "3F000" // invalid_schema_name
                   or "40001" // serialization_failure
                   or "40P01" // deadlock_detected
                   or "42501" // insufficient_privilege
                   or "42703" // undefined_column
                   or "42704" // undefined_object
                   or "42P01" // undefined_table
                   or "55P03" // lock_not_available
                   or "57014" // query_canceled / statement timeout
                   or "57P01" // admin_shutdown
                   or "57P02" // crash_shutdown
                   or "57P03" // cannot_connect_now
                   or "57P05"; // idle_session_timeout
    }

    private sealed class WorkflowLease(
        NpgsqlConnection connection,
        long lockKey) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (connection.State == ConnectionState.Open)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "select pg_advisory_unlock(@lock_key);";
                    command.Parameters.AddWithValue("lock_key", NpgsqlDbType.Bigint, lockKey);
                    var released = (bool)(await command
                        .ExecuteScalarAsync(CancellationToken.None)
                        .ConfigureAwait(false) ?? false);
                    if (!released)
                    {
                        throw new InvalidOperationException(
                            "The PostgreSQL statement-reconciliation workflow lease was not held by its dedicated session.");
                    }
                }
            }
            catch
            {
                // Never return a possibly still-locked physical session to the pool.
                NpgsqlConnection.ClearPool(connection);
                throw;
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
