using System.Data;
using System.Security.Cryptography;
using Meridian.Reporting;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Reporting;

/// <summary>
/// PostgreSQL-backed, tenant-scoped content-addressed store for immutable reporting artifacts.
/// </summary>
public sealed class PostgresReportingArtifactStore : IReportingArtifactStore
{
    private const int MaximumTenantIdLength = 256;

    private readonly ReportingArtifactStoreOptions _options;
    private readonly string _artifactTable;

    public PostgresReportingArtifactStore(ReportingArtifactStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ValidateIdentifier(_options.Schema, nameof(options.Schema));
        _artifactTable = $"\"{_options.Schema}\".\"reporting_artifact_blobs\"";
    }

    public async Task<ReportingArtifactWriteResult> StoreAsync(
        ReportingArtifactWriteRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = NormalizeTenantId(request.TenantId);
        if (request.Content.IsEmpty)
        {
            throw new ArgumentException("Reporting artifact content cannot be empty.", nameof(request));
        }

        // Clone caller-owned memory before hashing or awaiting so mutations cannot change the bytes
        // between identity calculation and persistence.
        var content = request.Content.ToArray();
        var contentHash = ComputeSha256(content);
        var identity = new ReportingArtifactIdentity(tenantId, contentHash);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            $"""
            insert into {_artifactTable} (
                tenant_id,
                content_hash_sha256,
                byte_size,
                content)
            values (
                @tenant_id,
                @content_hash_sha256,
                @byte_size,
                @content)
            on conflict (tenant_id, content_hash_sha256) do nothing
            returning stored_at_utc;
            """;
        insert.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        insert.Parameters.AddWithValue("content_hash_sha256", NpgsqlDbType.Text, contentHash);
        insert.Parameters.AddWithValue("byte_size", NpgsqlDbType.Bigint, content.LongLength);
        insert.Parameters.AddWithValue("content", NpgsqlDbType.Bytea, content);

        DateTimeOffset? insertedAtUtc = null;
        await using (var reader = await insert.ExecuteReaderAsync(CommandBehavior.SingleRow, ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                insertedAtUtc = ReadUtcTimestamp(reader, 0);
            }
        }

        if (insertedAtUtc is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new ReportingArtifactWriteResult(
                identity,
                content.LongLength,
                insertedAtUtc.Value,
                AlreadyExisted: false);
        }

        var existing = await ReadRowAsync(connection, transaction, identity, ct).ConfigureAwait(false)
            ?? throw new ReportingArtifactIntegrityException(
                identity,
                "an idempotent insert conflicted, but the retained row could not be read");
        VerifyIntegrity(identity, existing);
        if (!existing.Content.AsSpan().SequenceEqual(content))
        {
            throw new ReportingArtifactIntegrityException(
                identity,
                "different bytes were retained under the same content address");
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new ReportingArtifactWriteResult(
            identity,
            existing.DeclaredByteSize,
            existing.StoredAtUtc,
            AlreadyExisted: true);
    }

    public async Task<ReportingArtifactReadResult> ReadAsync(
        ReportingArtifactIdentity identity,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var normalizedIdentity = NormalizeIdentity(identity);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        var row = await ReadRowAsync(connection, transaction: null, normalizedIdentity, ct).ConfigureAwait(false)
            ?? throw new ReportingArtifactNotFoundException(normalizedIdentity);
        VerifyIntegrity(normalizedIdentity, row);

        return new ReportingArtifactReadResult(
            normalizedIdentity,
            row.DeclaredByteSize,
            row.StoredAtUtc,
            row.Content);
    }

    private async Task<ArtifactRow?> ReadRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ReportingArtifactIdentity identity,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select byte_size,
                   octet_length(content)::bigint,
                   content,
                   stored_at_utc
            from {_artifactTable}
            where tenant_id = @tenant_id
              and content_hash_sha256 = @content_hash_sha256;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, identity.TenantId);
        command.Parameters.AddWithValue("content_hash_sha256", NpgsqlDbType.Text, identity.ContentHashSha256);

        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleRow | CommandBehavior.SequentialAccess, ct)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new ArtifactRow(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetFieldValue<byte[]>(2),
            ReadUtcTimestamp(reader, 3));
    }

    private static void VerifyIntegrity(ReportingArtifactIdentity identity, ArtifactRow row)
    {
        if (row.DeclaredByteSize != row.PhysicalByteSize || row.DeclaredByteSize != row.Content.LongLength)
        {
            throw new ReportingArtifactIntegrityException(
                identity,
                $"declared size {row.DeclaredByteSize} does not match retained size {row.PhysicalByteSize}");
        }

        var actualHash = ComputeSha256(row.Content);
        if (!string.Equals(actualHash, identity.ContentHashSha256, StringComparison.Ordinal))
        {
            throw new ReportingArtifactIntegrityException(
                identity,
                $"retained SHA-256 {actualHash} does not match its content address");
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private static ReportingArtifactIdentity NormalizeIdentity(ReportingArtifactIdentity identity)
    {
        var tenantId = NormalizeTenantId(identity.TenantId);
        var hash = identity.ContentHashSha256?.Trim().ToLowerInvariant();
        if (hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Reporting artifact SHA-256 identities must contain exactly 64 hexadecimal characters.", nameof(identity));
        }

        return new ReportingArtifactIdentity(tenantId, hash);
    }

    private static string NormalizeTenantId(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Reporting artifact tenant id is required.", nameof(tenantId));
        }

        var normalized = tenantId.Trim();
        if (normalized.Length > MaximumTenantIdLength)
        {
            throw new ArgumentException(
                $"Reporting artifact tenant ids cannot exceed {MaximumTenantIdLength} characters.",
                nameof(tenantId));
        }

        return normalized;
    }

    private static string ComputeSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static DateTimeOffset ReadUtcTimestamp(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !(char.IsAsciiLetter(value[0]) || value[0] == '_')
            || !value.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            throw new ArgumentException(
                $"PostgreSQL identifier '{value}' is not supported. Use letters, digits, and underscores, and start with a letter or underscore.",
                parameterName);
        }
    }

    private sealed record ArtifactRow(
        long DeclaredByteSize,
        long PhysicalByteSize,
        byte[] Content,
        DateTimeOffset StoredAtUtc);
}
