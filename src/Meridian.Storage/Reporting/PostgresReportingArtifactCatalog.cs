using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Reporting;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Reporting;

/// <summary>
/// PostgreSQL-backed immutable catalog for tenant-scoped rendered report package metadata.
/// </summary>
public sealed class PostgresReportingArtifactCatalog : IReportingArtifactCatalog
{
    private const int MaximumIdentifierLength = 256;
    private const int MaximumArtifactCount = 10_000;

    private readonly ReportingArtifactStoreOptions _options;
    private readonly string _packageTable;
    private readonly string _artifactTable;

    public PostgresReportingArtifactCatalog(ReportingArtifactStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ValidateDatabaseIdentifier(_options.Schema, nameof(options.Schema));
        _packageTable = $"\"{_options.Schema}\".\"reporting_artifact_packages\"";
        _artifactTable = $"\"{_options.Schema}\".\"reporting_artifact_catalog\"";
    }

    public async ValueTask<ReportingArtifactCatalogWriteResult> AddPackageAsync(
        ReportingRetainedArtifactPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        var tenantId = ValidatePackage(package);
        var packagePayload = SerializePackage(package);
        var packageHash = ComputeSha256(packagePayload);
        var artifactPayloads = package.Artifacts
            .Select(static artifact =>
            {
                var payload = SerializeArtifact(artifact);
                return new SerializedArtifact(artifact.ArtifactId, payload, ComputeSha256(payload));
            })
            .ToArray();

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        var inserted = await TryInsertPackageAsync(
                connection,
                transaction,
                tenantId,
                package,
                packagePayload,
                packageHash,
                cancellationToken)
            .ConfigureAwait(false);

        if (inserted)
        {
            foreach (var artifact in artifactPayloads)
            {
                await InsertArtifactAsync(
                        connection,
                        transaction,
                        tenantId,
                        package.PackageId,
                        artifact,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new ReportingArtifactCatalogWriteResult(AlreadyExisted: false);
        }

        await VerifyExactPackageRetryAsync(
                connection,
                transaction,
                tenantId,
                package.PackageId,
                packagePayload,
                packageHash,
                artifactPayloads,
                cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ReportingArtifactCatalogWriteResult(AlreadyExisted: true);
    }

    public async ValueTask<ReportingRetainedArtifactRecord?> GetArtifactAsync(
        string tenantId,
        string packageId,
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        var normalizedTenantId = ValidateAndNormalizeKey(tenantId, nameof(tenantId));
        var normalizedPackageId = ValidateAndNormalizeKey(packageId, nameof(packageId));
        var normalizedArtifactId = ValidateAndNormalizeKey(artifactId, nameof(artifactId));

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select artifact_payload,
                   artifact_hash_sha256
            from {_artifactTable}
            where tenant_id = @tenant_id
              and package_id = @package_id
              and artifact_id = @artifact_id;
            """;
        AddArtifactKeyParameters(command, normalizedTenantId, normalizedPackageId, normalizedArtifactId);

        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var payload = reader.GetString(0);
        var declaredHash = reader.GetString(1);
        VerifyPayloadHash("artifact catalog record", declaredHash, payload);
        var artifact = DeserializeArtifact(payload);
        try
        {
            ValidateArtifact(artifact);
        }
        catch (ArgumentException exception)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained artifact metadata is invalid: {exception.Message}");
        }
        if (!string.Equals(artifact.Scope.TenantId, normalizedTenantId, StringComparison.Ordinal)
            || !string.Equals(artifact.PackageId, normalizedPackageId, StringComparison.Ordinal)
            || !string.Equals(artifact.ArtifactId, normalizedArtifactId, StringComparison.Ordinal))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                "Retained artifact metadata does not match its tenant/package/artifact database key.");
        }

        return artifact;
    }

    public async ValueTask<ReportingRetainedArtifactPackage?> GetPackageAsync(
        string tenantId,
        string packageId,
        CancellationToken cancellationToken = default)
    {
        var normalizedTenantId = ValidateAndNormalizeKey(tenantId, nameof(tenantId));
        var normalizedPackageId = ValidateAndNormalizeKey(packageId, nameof(packageId));
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        string packagePayload;
        int declaredArtifactCount;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                $"""
                select package_payload,
                       package_hash_sha256,
                       artifact_count
                from {_packageTable}
                where tenant_id = @tenant_id
                  and package_id = @package_id;
                """;
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, normalizedTenantId);
            command.Parameters.AddWithValue("package_id", NpgsqlDbType.Text, normalizedPackageId);
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            packagePayload = reader.GetString(0);
            VerifyPayloadHash("artifact package", reader.GetString(1), packagePayload);
            declaredArtifactCount = reader.GetInt32(2);
        }

        var package = DeserializePackage(packagePayload);
        string packageTenantId;
        try
        {
            packageTenantId = ValidatePackage(package);
        }
        catch (ArgumentException exception)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained artifact package metadata is invalid: {exception.Message}");
        }

        if (!string.Equals(package.PackageId, normalizedPackageId, StringComparison.Ordinal)
            || !string.Equals(packageTenantId, normalizedTenantId, StringComparison.Ordinal)
            || declaredArtifactCount != package.Artifacts.Length)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                "Retained artifact package metadata does not match its tenant/package database key or declared artifact count.");
        }

        var expected = package.Artifacts.ToDictionary(
            static artifact => artifact.ArtifactId,
            static artifact => SerializeArtifact(artifact),
            StringComparer.Ordinal);
        var retained = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                $"""
                select artifact_id,
                       artifact_payload,
                       artifact_hash_sha256
                from {_artifactTable}
                where tenant_id = @tenant_id
                  and package_id = @package_id;
                """;
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, normalizedTenantId);
            command.Parameters.AddWithValue("package_id", NpgsqlDbType.Text, normalizedPackageId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var artifactId = reader.GetString(0);
                var artifactPayload = reader.GetString(1);
                VerifyPayloadHash("artifact catalog record", reader.GetString(2), artifactPayload);
                var artifact = DeserializeArtifact(artifactPayload);
                try
                {
                    ValidateArtifact(artifact);
                }
                catch (ArgumentException exception)
                {
                    throw new ReportingArtifactCatalogIntegrityException(
                        $"Retained artifact metadata is invalid: {exception.Message}");
                }

                if (!string.Equals(artifact.Scope.TenantId, normalizedTenantId, StringComparison.Ordinal)
                    || !string.Equals(artifact.PackageId, normalizedPackageId, StringComparison.Ordinal)
                    || !string.Equals(artifact.ArtifactId, artifactId, StringComparison.Ordinal)
                    || !retained.TryAdd(artifactId, artifactPayload))
                {
                    throw new ReportingArtifactCatalogIntegrityException(
                        "Retained artifact metadata does not match its tenant/package/artifact database key or is duplicated.");
                }
            }
        }

        if (retained.Count != declaredArtifactCount
            || expected.Count != declaredArtifactCount
            || expected.Any(pair =>
                !retained.TryGetValue(pair.Key, out var payload)
                || !string.Equals(pair.Value, payload, StringComparison.Ordinal)))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Immutable report package '{normalizedPackageId}' is incomplete, augmented, or inconsistent with its retained package payload.");
        }

        return package;
    }

    private async Task<bool> TryInsertPackageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ReportingRetainedArtifactPackage package,
        string packagePayload,
        string packageHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {_packageTable} (
                tenant_id,
                package_id,
                package_hash_sha256,
                package_payload,
                artifact_count)
            values (
                @tenant_id,
                @package_id,
                @package_hash_sha256,
                @package_payload,
                @artifact_count)
            on conflict (tenant_id, package_id) do nothing;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        command.Parameters.AddWithValue("package_id", NpgsqlDbType.Text, package.PackageId);
        command.Parameters.AddWithValue("package_hash_sha256", NpgsqlDbType.Text, packageHash);
        command.Parameters.AddWithValue("package_payload", NpgsqlDbType.Text, packagePayload);
        command.Parameters.AddWithValue("artifact_count", NpgsqlDbType.Integer, package.Artifacts.Length);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private async Task InsertArtifactAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        string packageId,
        SerializedArtifact artifact,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {_artifactTable} (
                tenant_id,
                package_id,
                artifact_id,
                artifact_hash_sha256,
                artifact_payload)
            values (
                @tenant_id,
                @package_id,
                @artifact_id,
                @artifact_hash_sha256,
                @artifact_payload);
            """;
        AddArtifactKeyParameters(command, tenantId, packageId, artifact.ArtifactId);
        command.Parameters.AddWithValue("artifact_hash_sha256", NpgsqlDbType.Text, artifact.Hash);
        command.Parameters.AddWithValue("artifact_payload", NpgsqlDbType.Text, artifact.Payload);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task VerifyExactPackageRetryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        string packageId,
        string expectedPackagePayload,
        string expectedPackageHash,
        IReadOnlyList<SerializedArtifact> expectedArtifacts,
        CancellationToken cancellationToken)
    {
        await using (var packageCommand = connection.CreateCommand())
        {
            packageCommand.Transaction = transaction;
            packageCommand.CommandText =
                $"""
                select package_hash_sha256,
                       package_payload,
                       artifact_count
                from {_packageTable}
                where tenant_id = @tenant_id
                  and package_id = @package_id
                for share;
                """;
            packageCommand.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            packageCommand.Parameters.AddWithValue("package_id", NpgsqlDbType.Text, packageId);

            await using var reader = await packageCommand
                .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    "A package insert conflicted, but the immutable package row could not be read.");
            }

            var retainedHash = reader.GetString(0);
            var retainedPayload = reader.GetString(1);
            var retainedArtifactCount = reader.GetInt32(2);
            VerifyPayloadHash("artifact package", retainedHash, retainedPayload);
            if (!string.Equals(retainedHash, expectedPackageHash, StringComparison.Ordinal)
                || !string.Equals(retainedPayload, expectedPackagePayload, StringComparison.Ordinal)
                || retainedArtifactCount != expectedArtifacts.Count)
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    "Attempted to replace immutable report package metadata with a non-identical payload.");
            }
        }

        var retainedArtifacts = new Dictionary<string, SerializedArtifact>(StringComparer.Ordinal);
        await using (var artifactCommand = connection.CreateCommand())
        {
            artifactCommand.Transaction = transaction;
            artifactCommand.CommandText =
                $"""
                select artifact_id,
                       artifact_payload,
                       artifact_hash_sha256
                from {_artifactTable}
                where tenant_id = @tenant_id
                  and package_id = @package_id;
                """;
            artifactCommand.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            artifactCommand.Parameters.AddWithValue("package_id", NpgsqlDbType.Text, packageId);
            await using var reader = await artifactCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var retained = new SerializedArtifact(reader.GetString(0), reader.GetString(1), reader.GetString(2));
                VerifyPayloadHash("artifact catalog record", retained.Hash, retained.Payload);
                if (!retainedArtifacts.TryAdd(retained.ArtifactId, retained))
                {
                    throw new ReportingArtifactCatalogIntegrityException(
                        $"Immutable package '{packageId}' contains duplicate artifact metadata '{retained.ArtifactId}'.");
                }
            }
        }

        if (retainedArtifacts.Count != expectedArtifacts.Count)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                "Immutable report package metadata is incomplete or contains unexpected artifacts.");
        }

        foreach (var expected in expectedArtifacts)
        {
            if (!retainedArtifacts.TryGetValue(expected.ArtifactId, out var retained)
                || !string.Equals(retained.Hash, expected.Hash, StringComparison.Ordinal)
                || !string.Equals(retained.Payload, expected.Payload, StringComparison.Ordinal))
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    $"Attempted to replace immutable artifact metadata '{expected.ArtifactId}' with a non-identical payload.");
            }
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static string ValidatePackage(ReportingRetainedArtifactPackage package)
    {
        ValidateRequiredIdentifier(package.PackageId, nameof(package.PackageId));
        if (package.Artifacts.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A reporting artifact package must contain at least one artifact.", nameof(package));
        }

        if (package.Artifacts.Length > MaximumArtifactCount)
        {
            throw new ArgumentException(
                $"A reporting artifact package cannot contain more than {MaximumArtifactCount} artifacts.",
                nameof(package));
        }

        var artifactIds = new HashSet<string>(StringComparer.Ordinal);
        var first = package.Artifacts[0];
        ValidateArtifact(first);
        foreach (var artifact in package.Artifacts)
        {
            ValidateArtifact(artifact);
            if (!string.Equals(package.PackageId, artifact.PackageId, StringComparison.Ordinal))
            {
                throw new ArgumentException("Every artifact must carry the enclosing package id.", nameof(package));
            }

            if (!HasSamePackageMetadata(first, artifact))
            {
                throw new ArgumentException(
                    "Every artifact in a package must carry the same run, scope, access, snapshot, and manifest metadata.",
                    nameof(package));
            }

            if (!artifactIds.Add(artifact.ArtifactId))
            {
                throw new ArgumentException(
                    $"Artifact id '{artifact.ArtifactId}' appears more than once in the package.",
                    nameof(package));
            }
        }

        return first.Scope.TenantId;
    }

    private static void ValidateArtifact(ReportingRetainedArtifactRecord artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(artifact.Scope);
        ArgumentNullException.ThrowIfNull(artifact.Access);
        ArgumentNullException.ThrowIfNull(artifact.Snapshot);
        ArgumentNullException.ThrowIfNull(artifact.Identity);

        ValidateRequiredIdentifier(artifact.PackageId, nameof(artifact.PackageId));
        ValidateRequiredIdentifier(artifact.RunId, nameof(artifact.RunId));
        ValidateRequiredIdentifier(artifact.SeriesId, nameof(artifact.SeriesId));
        ValidateRequiredIdentifier(artifact.ArtifactId, nameof(artifact.ArtifactId));
        ValidateRequiredIdentifier(artifact.ManifestId, nameof(artifact.ManifestId));
        ValidateRequiredIdentifier(artifact.FileName, nameof(artifact.FileName));
        ValidateRequiredIdentifier(artifact.ContentType, nameof(artifact.ContentType));
        ValidateRequiredIdentifier(artifact.Scope.TenantId, nameof(artifact.Scope.TenantId));
        ValidateRequiredIdentifier(artifact.Scope.OrganizationId, nameof(artifact.Scope.OrganizationId));
        ValidateOptionalIdentifier(artifact.Scope.CompanyId, nameof(artifact.Scope.CompanyId));
        ValidateOptionalIdentifier(artifact.Scope.FundId, nameof(artifact.Scope.FundId));
        ValidateRequiredIdentifier(artifact.Scope.BookId, nameof(artifact.Scope.BookId));
        ValidateRequiredIdentifier(artifact.Scope.PeriodId, nameof(artifact.Scope.PeriodId));
        ValidateRequiredIdentifier(artifact.Access.PolicyId, nameof(artifact.Access.PolicyId));
        ValidateRequiredIdentifier(artifact.Access.PolicyVersion, nameof(artifact.Access.PolicyVersion));
        ValidateOptionalIdentifier(artifact.Access.OwnerPrincipalId, nameof(artifact.Access.OwnerPrincipalId));
        ValidateRequiredIdentifier(artifact.Snapshot.SnapshotId, nameof(artifact.Snapshot.SnapshotId));
        ValidateRequiredIdentifier(
            artifact.Snapshot.ReconciliationCheckpointId,
            nameof(artifact.Snapshot.ReconciliationCheckpointId));

        if (artifact.Revision <= 0 || artifact.ByteLength <= 0)
        {
            throw new ArgumentException("Artifact revision and byte length must both be positive.", nameof(artifact));
        }

        ValidateHash(artifact.ManifestHash, nameof(artifact.ManifestHash));
        ValidateHash(artifact.Access.PolicyHash, nameof(artifact.Access.PolicyHash));
        ValidateHash(artifact.Snapshot.SnapshotHash, nameof(artifact.Snapshot.SnapshotHash));
        ValidateHash(artifact.Identity.ContentHashSha256, nameof(artifact.Identity.ContentHashSha256));

        if (!Enum.IsDefined(artifact.Access.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(artifact), "Reporting artifact access mode is invalid.");
        }

        if (artifact.Access.Mode == ReportingGovernanceAccessMode.Private
            && (!artifact.Access.AllowOwnerAccess
                || string.IsNullOrWhiteSpace(artifact.Access.OwnerPrincipalId))
            && artifact.Access.Principals.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Private reporting artifact access requires an enabled owner or named user principal.",
                nameof(artifact));
        }

        if (artifact.Access.Mode == ReportingGovernanceAccessMode.Restricted
            && (!artifact.Access.AllowOwnerAccess
                || string.IsNullOrWhiteSpace(artifact.Access.OwnerPrincipalId))
            && artifact.Access.Principals.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Restricted reporting artifact access requires an owner or at least one principal.",
                nameof(artifact));
        }

        if (artifact.Access.Mode == ReportingGovernanceAccessMode.CompanyWide
            && string.IsNullOrWhiteSpace(artifact.Scope.CompanyId))
        {
            throw new ArgumentException(
                "Company-wide reporting artifact access requires an immutable company scope.",
                nameof(artifact));
        }

        if (!string.Equals(artifact.Scope.TenantId, artifact.Identity.TenantId, StringComparison.Ordinal)
            || !string.Equals(artifact.Scope.TenantId, artifact.Snapshot.TenantId, StringComparison.Ordinal)
            || !string.Equals(artifact.Scope.OrganizationId, artifact.Snapshot.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(artifact.Scope.CompanyId, artifact.Snapshot.CompanyId, StringComparison.Ordinal)
            || !string.Equals(artifact.Scope.FundId, artifact.Snapshot.FundId, StringComparison.Ordinal)
            || !string.Equals(artifact.Scope.BookId, artifact.Snapshot.BookId, StringComparison.Ordinal)
            || !string.Equals(artifact.Scope.PeriodId, artifact.Snapshot.PeriodId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Artifact identity and certified snapshot must match the artifact operational scope.",
                nameof(artifact));
        }

        if (artifact.StoredAtUtc.Offset != TimeSpan.Zero || artifact.Snapshot.CapturedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Artifact and snapshot timestamps must be expressed in UTC.", nameof(artifact));
        }

        if (!artifact.Access.Principals.IsDefault)
        {
            foreach (var principal in artifact.Access.Principals)
            {
                if (principal is null || !Enum.IsDefined(principal.Kind))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(artifact),
                        "Reporting artifact access principal kind is invalid.");
                }

                ValidateRequiredIdentifier(
                    principal.PrincipalId,
                    nameof(artifact.Access.Principals));
            }

            if (artifact.Access.Principals.Any(principal =>
                    artifact.Access.Principals.Count(candidate =>
                        candidate.Kind == principal.Kind
                        && string.Equals(
                            candidate.PrincipalId,
                            principal.PrincipalId,
                            StringComparison.OrdinalIgnoreCase)) > 1))
            {
                throw new ArgumentException(
                    "Reporting artifact access principals must be unique by kind and identity.",
                    nameof(artifact));
            }
        }

        if (artifact.Access.Mode == ReportingGovernanceAccessMode.Private
            && !artifact.Access.Principals.IsDefaultOrEmpty
            && artifact.Access.Principals.Any(static principal =>
                principal.Kind != ReportingAccessPrincipalKind.User))
        {
            throw new ArgumentException(
                "Private reporting artifact access can retain only user principals.",
                nameof(artifact));
        }
    }

    private static bool HasSamePackageMetadata(
        ReportingRetainedArtifactRecord expected,
        ReportingRetainedArtifactRecord candidate) =>
        string.Equals(expected.RunId, candidate.RunId, StringComparison.Ordinal)
        && string.Equals(expected.SeriesId, candidate.SeriesId, StringComparison.Ordinal)
        && expected.Revision == candidate.Revision
        && Equals(expected.Scope, candidate.Scope)
        && HasSameAccess(expected.Access, candidate.Access)
        && Equals(expected.Snapshot, candidate.Snapshot)
        && string.Equals(expected.ManifestId, candidate.ManifestId, StringComparison.Ordinal)
        && string.Equals(expected.ManifestHash, candidate.ManifestHash, StringComparison.Ordinal);

    private static bool HasSameAccess(ReportingAccessScope expected, ReportingAccessScope candidate) =>
        string.Equals(expected.PolicyId, candidate.PolicyId, StringComparison.Ordinal)
        && string.Equals(expected.PolicyVersion, candidate.PolicyVersion, StringComparison.Ordinal)
        && expected.Mode == candidate.Mode
        && string.Equals(expected.OwnerPrincipalId, candidate.OwnerPrincipalId, StringComparison.Ordinal)
        && expected.AllowOwnerAccess == candidate.AllowOwnerAccess
        && ((expected.Principals.IsDefault && candidate.Principals.IsDefault)
            || (!expected.Principals.IsDefault
                && !candidate.Principals.IsDefault
                && expected.Principals.SequenceEqual(candidate.Principals)))
        && string.Equals(expected.PolicyHash, candidate.PolicyHash, StringComparison.Ordinal);

    private static string SerializePackage(ReportingRetainedArtifactPackage package) =>
        JsonSerializer.Serialize(package, ReportingArtifactCatalogJsonContext.Default.ReportingRetainedArtifactPackage);

    private static string SerializeArtifact(ReportingRetainedArtifactRecord artifact) =>
        JsonSerializer.Serialize(artifact, ReportingArtifactCatalogJsonContext.Default.ReportingRetainedArtifactRecord);

    private static ReportingRetainedArtifactRecord DeserializeArtifact(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize(
                       payload,
                       ReportingArtifactCatalogJsonContext.Default.ReportingRetainedArtifactRecord)
                   ?? throw new ReportingArtifactCatalogIntegrityException(
                       "Retained artifact metadata deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained artifact metadata is not valid canonical JSON: {exception.Message}");
        }
    }

    private static ReportingRetainedArtifactPackage DeserializePackage(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize(
                       payload,
                       ReportingArtifactCatalogJsonContext.Default.ReportingRetainedArtifactPackage)
                   ?? throw new ReportingArtifactCatalogIntegrityException(
                       "Retained artifact package metadata deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained artifact package metadata is not valid canonical JSON: {exception.Message}");
        }
    }

    private static void VerifyPayloadHash(string label, string declaredHash, string payload)
    {
        if (!IsLowercaseSha256(declaredHash))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained {label} carries an invalid SHA-256 hash.");
        }

        var actualHash = ComputeSha256(payload);
        if (!string.Equals(declaredHash, actualHash, StringComparison.Ordinal))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained {label} hash '{declaredHash}' does not match payload hash '{actualHash}'.");
        }
    }

    private static string ComputeSha256(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    private static void AddArtifactKeyParameters(
        NpgsqlCommand command,
        string tenantId,
        string packageId,
        string artifactId)
    {
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        command.Parameters.AddWithValue("package_id", NpgsqlDbType.Text, packageId);
        command.Parameters.AddWithValue("artifact_id", NpgsqlDbType.Text, artifactId);
    }

    private static string ValidateAndNormalizeKey(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > MaximumIdentifierLength)
        {
            throw new ArgumentException(
                $"Reporting catalog identifiers cannot exceed {MaximumIdentifierLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static void ValidateRequiredIdentifier(string value, string parameterName)
    {
        var normalized = ValidateAndNormalizeKey(value, parameterName);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            throw new ArgumentException("Reporting catalog identifiers must already be trimmed.", parameterName);
        }
    }

    private static void ValidateOptionalIdentifier(string? value, string parameterName)
    {
        if (value is not null)
        {
            ValidateRequiredIdentifier(value, parameterName);
        }
    }

    private static void ValidateHash(string value, string parameterName)
    {
        if (!IsLowercaseSha256(value))
        {
            throw new ArgumentException(
                "Reporting catalog hashes must contain exactly 64 lowercase hexadecimal characters.",
                parameterName);
        }
    }

    private static bool IsLowercaseSha256(string? value) =>
        value is { Length: 64 }
        && value.All(Uri.IsHexDigit)
        && string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal);

    private static void ValidateDatabaseIdentifier(string value, string parameterName)
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

    private sealed record SerializedArtifact(string ArtifactId, string Payload, string Hash);
}
