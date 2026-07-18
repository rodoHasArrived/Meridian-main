using System.Data;
using Meridian.Reporting;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Reporting;

/// <summary>
/// Durable PostgreSQL access-grant store. Only SHA-256 token digests are accepted or retained;
/// plaintext recipient bearer tokens never cross this persistence boundary.
/// </summary>
public sealed class PostgresReportingAccessGrantStore : IReportingAccessGrantStore
{
    private readonly ReportingArtifactStoreOptions _options;
    private readonly string _grantTable;

    public PostgresReportingAccessGrantStore(ReportingArtifactStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ReportingDistributionStoreGuard.ValidateIdentifier(_options.Schema, nameof(options.Schema));
        _grantTable = $"\"{_options.Schema}\".\"reporting_access_grants\"";
    }

    public async Task<ReportingAccessGrantRecord?> GetAsync(
        string grantId,
        CancellationToken ct = default)
    {
        var normalizedGrantId = ReportingDistributionStoreGuard.NormalizeRequired(
            grantId,
            nameof(grantId),
            256);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = CreateSelectCommand(connection, transaction: null, forUpdate: false);
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Text, normalizedGrantId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? ReadGrant(reader)
            : null;
    }

    public async Task<IReadOnlyList<ReportingAccessGrantRecord>> ListByPackageAsync(
        string tenantId,
        string packageId,
        CancellationToken ct = default)
    {
        var normalizedTenantId = ReportingDistributionStoreGuard.NormalizeRequired(
            tenantId,
            nameof(tenantId),
            256);
        var normalizedPackageId = ReportingDistributionStoreGuard.NormalizeRequired(
            packageId,
            nameof(packageId),
            256);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select {GrantSelectList()}
            from {_grantTable}
            where tenant_id = @tenant_id
              and package_id = @package_id
            order by created_at_utc desc, grant_id;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, normalizedTenantId);
        command.Parameters.AddWithValue("package_id", NpgsqlDbType.Text, normalizedPackageId);
        var grants = new List<ReportingAccessGrantRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            grants.Add(ReadGrant(reader));
        }

        return grants;
    }

    public async Task<bool> TryCreateAsync(
        ReportingAccessGrantRecord grant,
        CancellationToken ct = default)
    {
        ValidateGrant(grant, expectedVersion: 0, requireExactVersion: true);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {_grantTable} (
                grant_id,
                token_hash_sha256,
                tenant_id,
                audience,
                audience_kind,
                run_id,
                package_id,
                allow_package_read,
                artifact_ids,
                created_at_utc,
                expires_at_utc,
                max_uses,
                use_count,
                last_used_at_utc,
                revoked_at_utc,
                revoked_by,
                revocation_reason,
                version)
            values (
                @grant_id,
                @token_hash_sha256,
                @tenant_id,
                @audience,
                @audience_kind,
                @run_id,
                @package_id,
                @allow_package_read,
                @artifact_ids,
                @created_at_utc,
                @expires_at_utc,
                @max_uses,
                @use_count,
                @last_used_at_utc,
                @revoked_at_utc,
                @revoked_by,
                @revocation_reason,
                @version)
            on conflict do nothing
            returning 1;
            """;
        AddGrantParameters(command, grant);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    public async Task<bool> TryUpdateAsync(
        string grantId,
        long expectedVersion,
        ReportingAccessGrantRecord updatedGrant,
        CancellationToken ct = default)
    {
        if (expectedVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        var normalizedGrantId = ReportingDistributionStoreGuard.NormalizeRequired(
            grantId,
            nameof(grantId),
            256);
        ValidateGrant(updatedGrant, checked(expectedVersion + 1), requireExactVersion: true);
        if (!string.Equals(normalizedGrantId, updatedGrant.GrantId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Updated reporting access grant id does not match the requested grant.", nameof(updatedGrant));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);
        var current = await ReadForUpdateAsync(connection, transaction, normalizedGrantId, ct).ConfigureAwait(false);
        if (current is null || current.Version != expectedVersion)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        ValidateTransition(current, updatedGrant);

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText =
            $"""
            update {_grantTable}
            set use_count = @use_count,
                last_used_at_utc = @last_used_at_utc,
                revoked_at_utc = @revoked_at_utc,
                revoked_by = @revoked_by,
                revocation_reason = @revocation_reason,
                version = @next_version
            where grant_id = @grant_id
              and version = @expected_version;
            """;
        update.Parameters.AddWithValue("grant_id", NpgsqlDbType.Text, normalizedGrantId);
        update.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, expectedVersion);
        update.Parameters.AddWithValue("next_version", NpgsqlDbType.Bigint, updatedGrant.Version);
        update.Parameters.AddWithValue("use_count", NpgsqlDbType.Integer, updatedGrant.UseCount);
        AddNullableTimestamp(update, "last_used_at_utc", updatedGrant.LastUsedAtUtc);
        AddNullableTimestamp(update, "revoked_at_utc", updatedGrant.RevokedAtUtc);
        AddNullableText(update, "revoked_by", updatedGrant.RevokedBy);
        AddNullableText(update, "revocation_reason", updatedGrant.RevocationReason);

        if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
        {
            throw new ReportingDistributionStateCorruptionException(
                "access grant",
                normalizedGrantId,
                "its locked version disappeared before the atomic state update");
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async Task<ReportingAccessGrantRecord?> ReadForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string grantId,
        CancellationToken ct)
    {
        await using var command = CreateSelectCommand(connection, transaction, forUpdate: true);
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Text, grantId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? ReadGrant(reader)
            : null;
    }

    private NpgsqlCommand CreateSelectCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        bool forUpdate)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select {GrantSelectList()}
            from {_grantTable}
            where grant_id = @grant_id
            {(forUpdate ? "for update" : string.Empty)};
            """;
        return command;
    }

    private static string GrantSelectList() =>
        "grant_id, token_hash_sha256, tenant_id, audience, audience_kind, run_id, package_id, "
        + "allow_package_read, artifact_ids, created_at_utc, expires_at_utc, max_uses, "
        + "use_count, last_used_at_utc, revoked_at_utc, revoked_by, revocation_reason, version";

    private static ReportingAccessGrantRecord ReadGrant(NpgsqlDataReader reader)
    {
        var grantId = reader.GetString(0);
        try
        {
            var grant = new ReportingAccessGrantRecord(
                grantId,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetBoolean(7),
                reader.GetFieldValue<string[]>(8),
                ReportingDistributionStoreGuard.ReadUtcTimestamp(reader, 9),
                ReportingDistributionStoreGuard.ReadUtcTimestamp(reader, 10),
                reader.GetInt32(11),
                reader.GetInt32(12),
                ReportingDistributionStoreGuard.ReadNullableUtcTimestamp(reader, 13),
                ReportingDistributionStoreGuard.ReadNullableUtcTimestamp(reader, 14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.GetInt64(17),
                (ReportingAccessPrincipalKind)reader.GetInt32(4));
            ValidateGrant(grant, expectedVersion: null, requireExactVersion: false);
            return grant;
        }
        catch (ReportingDistributionStateCorruptionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException or ArgumentException or OverflowException)
        {
            throw new ReportingDistributionStateCorruptionException(
                "access grant",
                grantId,
                ex.Message,
                ex);
        }
    }

    private static void ValidateGrant(
        ReportingAccessGrantRecord grant,
        long? expectedVersion,
        bool requireExactVersion)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ReportingDistributionStoreGuard.NormalizeRequired(grant.GrantId, nameof(grant.GrantId), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.ValidateSha256(grant.TokenHashSha256, nameof(grant.TokenHashSha256));
        ReportingDistributionStoreGuard.NormalizeRequired(grant.TenantId, nameof(grant.TenantId), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(grant.Audience, nameof(grant.Audience), 512, requireCanonical: true);
        if (!Enum.IsDefined(grant.AudienceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(grant), "Reporting access grant audience kind is invalid.");
        }
        ReportingDistributionStoreGuard.NormalizeRequired(grant.RunId, nameof(grant.RunId), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(grant.PackageId, nameof(grant.PackageId), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.ValidateStringSet(grant.ArtifactIds, nameof(grant.ArtifactIds), 512);
        ReportingDistributionStoreGuard.RequireUtc(grant.CreatedAtUtc, nameof(grant.CreatedAtUtc));
        ReportingDistributionStoreGuard.RequireUtc(grant.ExpiresAtUtc, nameof(grant.ExpiresAtUtc));
        if (grant.ExpiresAtUtc <= grant.CreatedAtUtc)
        {
            throw new ArgumentException("Reporting access grant expiration must be after creation.", nameof(grant));
        }

        if (grant.MaxUses <= 0 || grant.UseCount < 0 || grant.UseCount > grant.MaxUses)
        {
            throw new ArgumentException("Reporting access grant use counters are invalid.", nameof(grant));
        }

        if (grant.LastUsedAtUtc is { } lastUsed
            && (lastUsed.Offset != TimeSpan.Zero
                || lastUsed < grant.CreatedAtUtc
                || lastUsed >= grant.ExpiresAtUtc))
        {
            throw new ArgumentException(
                "Reporting access grant last-use time must be UTC and fall within its active grant window.",
                nameof(grant));
        }

        if (grant.RevokedAtUtc is null)
        {
            if (grant.RevokedBy is not null || grant.RevocationReason is not null)
            {
                throw new ArgumentException("Unrevoked reporting access grants cannot retain revocation metadata.", nameof(grant));
            }
        }
        else
        {
            ReportingDistributionStoreGuard.RequireUtc(grant.RevokedAtUtc.Value, nameof(grant.RevokedAtUtc));
            if (grant.RevokedAtUtc < grant.CreatedAtUtc)
            {
                throw new ArgumentException("Reporting access grant revocation predates creation.", nameof(grant));
            }

            ReportingDistributionStoreGuard.NormalizeRequired(grant.RevokedBy!, nameof(grant.RevokedBy), 256, requireCanonical: true);
            ReportingDistributionStoreGuard.NormalizeRequired(grant.RevocationReason!, nameof(grant.RevocationReason), 2048, requireCanonical: true);
        }

        if (grant.Version < 0 || (requireExactVersion && grant.Version != expectedVersion))
        {
            throw new ArgumentException("Reporting access grant version is invalid.", nameof(grant));
        }
    }

    private static void ValidateTransition(
        ReportingAccessGrantRecord current,
        ReportingAccessGrantRecord updated)
    {
        if (!string.Equals(current.GrantId, updated.GrantId, StringComparison.Ordinal)
            || !string.Equals(current.TokenHashSha256, updated.TokenHashSha256, StringComparison.Ordinal)
            || !string.Equals(current.TenantId, updated.TenantId, StringComparison.Ordinal)
            || !string.Equals(current.Audience, updated.Audience, StringComparison.Ordinal)
            || current.AudienceKind != updated.AudienceKind
            || !string.Equals(current.RunId, updated.RunId, StringComparison.Ordinal)
            || !string.Equals(current.PackageId, updated.PackageId, StringComparison.Ordinal)
            || current.AllowPackageRead != updated.AllowPackageRead
            || !current.ArtifactIds.SequenceEqual(updated.ArtifactIds, StringComparer.Ordinal)
            || current.CreatedAtUtc != updated.CreatedAtUtc
            || current.ExpiresAtUtc != updated.ExpiresAtUtc
            || current.MaxUses != updated.MaxUses)
        {
            throw new InvalidOperationException("Reporting access grant authority scope is immutable.");
        }

        if (updated.UseCount < current.UseCount || updated.UseCount > current.UseCount + 1)
        {
            throw new InvalidOperationException("Reporting access grant use count can only advance by one atomically.");
        }

        if (updated.UseCount == current.UseCount)
        {
            if (updated.LastUsedAtUtc != current.LastUsedAtUtc)
            {
                throw new InvalidOperationException("Reporting access grant last-use time cannot change without consuming a use.");
            }
        }
        else if (updated.LastUsedAtUtc is null
                 || (current.LastUsedAtUtc is not null && updated.LastUsedAtUtc < current.LastUsedAtUtc))
        {
            throw new InvalidOperationException("Reporting access grant consumption requires a monotonic last-use time.");
        }

        var consumedUse = updated.UseCount != current.UseCount;
        var newlyRevoked = current.RevokedAtUtc is null && updated.RevokedAtUtc is not null;
        if (consumedUse && (current.RevokedAtUtc is not null || newlyRevoked))
        {
            throw new InvalidOperationException(
                "A reporting access grant cannot be consumed after or atomically with revocation.");
        }

        if (current.RevokedAtUtc is not null
            && (updated.RevokedAtUtc != current.RevokedAtUtc
                || !string.Equals(updated.RevokedBy, current.RevokedBy, StringComparison.Ordinal)
                || !string.Equals(updated.RevocationReason, current.RevocationReason, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Reporting access grant revocation is immutable.");
        }
    }

    private static void AddGrantParameters(NpgsqlCommand command, ReportingAccessGrantRecord grant)
    {
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Text, grant.GrantId);
        command.Parameters.AddWithValue("token_hash_sha256", NpgsqlDbType.Text, grant.TokenHashSha256);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, grant.TenantId);
        command.Parameters.AddWithValue("audience", NpgsqlDbType.Text, grant.Audience);
        command.Parameters.AddWithValue("audience_kind", NpgsqlDbType.Integer, (int)grant.AudienceKind);
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, grant.RunId);
        command.Parameters.AddWithValue("package_id", NpgsqlDbType.Text, grant.PackageId);
        command.Parameters.AddWithValue("allow_package_read", NpgsqlDbType.Boolean, grant.AllowPackageRead);
        command.Parameters.AddWithValue("artifact_ids", NpgsqlDbType.Array | NpgsqlDbType.Text, grant.ArtifactIds.ToArray());
        command.Parameters.AddWithValue("created_at_utc", NpgsqlDbType.TimestampTz, grant.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("expires_at_utc", NpgsqlDbType.TimestampTz, grant.ExpiresAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("max_uses", NpgsqlDbType.Integer, grant.MaxUses);
        command.Parameters.AddWithValue("use_count", NpgsqlDbType.Integer, grant.UseCount);
        AddNullableTimestamp(command, "last_used_at_utc", grant.LastUsedAtUtc);
        AddNullableTimestamp(command, "revoked_at_utc", grant.RevokedAtUtc);
        AddNullableText(command, "revoked_by", grant.RevokedBy);
        AddNullableText(command, "revocation_reason", grant.RevocationReason);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, grant.Version);
    }

    private static void AddNullableTimestamp(NpgsqlCommand command, string name, DateTimeOffset? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.TimestampTz);
        parameter.Value = value is null ? DBNull.Value : value.Value.UtcDateTime;
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = value is null ? DBNull.Value : value;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
