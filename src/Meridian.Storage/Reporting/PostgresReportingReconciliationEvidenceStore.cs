using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Reporting;
using Npgsql;
using NpgsqlTypes;
using Meridian.Contracts.Integrity;

namespace Meridian.Storage.Reporting;

/// <summary>
/// Production append-only PostgreSQL receipt store. Every read verifies the database key, payload
/// hash, canonical receipt hash, and exact source scope before returning evidence to certification.
/// </summary>
public sealed class PostgresReportingReconciliationEvidenceStore :
    IReportingReconciliationEvidenceRetentionStore
{
    private readonly ReportingArtifactStoreOptions _options;
    private readonly string _legacyTable;
    private readonly string _currentTable;

    public PostgresReportingReconciliationEvidenceStore(ReportingArtifactStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ValidateDatabaseIdentifier(_options.Schema);
        _legacyTable = $"\"{_options.Schema}\".\"reporting_reconciliation_evidence\"";
        _currentTable = $"\"{_options.Schema}\".\"reporting_reconciliation_evidence_v2\"";
    }

    public async ValueTask<ReportingReconciliationEvidenceReceipt?> GetExactAsync(
        string tenantId,
        string organizationId,
        string? companyId,
        string fundId,
        string ledgerBookId,
        string accountingPeriodId,
        string accountingBasis,
        DateOnly asOfDate,
        string sourceCheckpointId,
        string sourceCheckpointHash,
        CancellationToken cancellationToken = default)
    {
        var keyHash = ComputeKeyHash(
            tenantId,
            organizationId,
            companyId,
            fundId,
            ledgerBookId,
            accountingPeriodId,
            accountingBasis,
            asOfDate,
            sourceCheckpointId,
            sourceCheckpointHash);
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var current = await ReadCurrentReceiptAsync(connection, keyHash, tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (current is not null)
        {
            ValidateCurrentReceiptKey(
                current,
                tenantId,
                organizationId,
                companyId,
                fundId,
                ledgerBookId,
                accountingPeriodId,
                accountingBasis,
                asOfDate,
                sourceCheckpointId,
                sourceCheckpointHash,
                keyHash);
            return current;
        }

        var legacy = await ReadLegacyReceiptAsync(connection, keyHash, tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (legacy is null)
        {
            return null;
        }

        ValidateLegacyReceiptKey(
            legacy,
            tenantId,
            organizationId,
            companyId,
            fundId,
            ledgerBookId,
            accountingPeriodId,
            accountingBasis,
            asOfDate,
            sourceCheckpointId,
            sourceCheckpointHash,
            keyHash);
        throw new ReportingReconciliationEvidenceLegacyMigrationRequiredException(
            "The retained reconciliation evidence is a verified legacy v1 receipt without item-level break evidence. " +
            "Final certification is blocked: preserve the legacy row, re-run the governed reconciliation and close workflow against the authoritative source, and retain its new v2 receipt. " +
            "Do not update, delete, or synthesize break evidence for the legacy receipt.");
    }

    public async ValueTask<bool> RetainAsync(
        ReportingReconciliationEvidenceReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ReportingReconciliationEvidenceValidation.Validate(receipt);
        var keyHash = ComputeKeyHash(receipt);
        var payload = JsonSerializer.Serialize(
            receipt,
            ReportingReconciliationEvidenceJsonContext.Default.ReportingReconciliationEvidenceReceipt);
        var payloadHash = Sha256Digest.ComputeUtf8(payload);
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {_currentTable} (
                tenant_id,
                receipt_key_sha256,
                organization_id,
                company_id,
                fund_id,
                ledger_book_id,
                accounting_period_id,
                accounting_basis,
                as_of_date,
                source_checkpoint_id,
                source_checkpoint_hash,
                reconciliation_checkpoint_id,
                reconciliation_checkpoint_hash,
                receipt_payload,
                receipt_hash_sha256,
                receipt_format_version,
                supersedes_legacy_receipt_key_sha256)
            values (
                @tenant_id,
                @receipt_key_sha256,
                @organization_id,
                @company_id,
                @fund_id,
                @ledger_book_id,
                @accounting_period_id,
                @accounting_basis,
                @as_of_date,
                @source_checkpoint_id,
                @source_checkpoint_hash,
                @reconciliation_checkpoint_id,
                @reconciliation_checkpoint_hash,
                @receipt_payload,
                @receipt_hash_sha256,
                2,
                @supersedes_legacy_receipt_key_sha256)
            on conflict (tenant_id, receipt_key_sha256) do nothing;
            """;
        AddParameters(command, receipt, keyHash, payload, payloadHash);
        command.Parameters.AddWithValue(
            "supersedes_legacy_receipt_key_sha256",
            NpgsqlDbType.Text,
            await VerifyLegacyReceiptForRecoveryAsync(connection, transaction, receipt, keyHash, cancellationToken)
                .ConfigureAwait(false) is null
                ? DBNull.Value
                : keyHash);
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        if (!inserted)
        {
            await using var verify = connection.CreateCommand();
            verify.Transaction = transaction;
            verify.CommandText =
                $"select receipt_payload, receipt_hash_sha256 from {_currentTable} where tenant_id = @tenant_id and receipt_key_sha256 = @key_hash for share;";
            verify.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, receipt.TenantId);
            verify.Parameters.AddWithValue("key_hash", NpgsqlDbType.Text, keyHash);
            await using var reader = await verify.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    "A reconciliation evidence insert conflicted but the immutable row could not be read.");
            }

            var retainedPayload = reader.GetString(0);
            var retainedHash = reader.GetString(1);
            VerifyPayloadHash(retainedPayload, retainedHash);
            if (!string.Equals(retainedHash, payloadHash, StringComparison.Ordinal)
                || !string.Equals(retainedPayload, payload, StringComparison.Ordinal))
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    "Attempted to replace an immutable reconciliation evidence receipt with a non-identical payload.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return !inserted;
    }

    private static void AddParameters(
        NpgsqlCommand command,
        ReportingReconciliationEvidenceReceipt receipt,
        string keyHash,
        string payload,
        string payloadHash)
    {
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, receipt.TenantId);
        command.Parameters.AddWithValue("receipt_key_sha256", NpgsqlDbType.Text, keyHash);
        command.Parameters.AddWithValue("organization_id", NpgsqlDbType.Text, receipt.OrganizationId);
        command.Parameters.AddWithValue("company_id", NpgsqlDbType.Text, receipt.CompanyId!);
        command.Parameters.AddWithValue("fund_id", NpgsqlDbType.Text, receipt.FundId);
        command.Parameters.AddWithValue("ledger_book_id", NpgsqlDbType.Text, receipt.LedgerBookId);
        command.Parameters.AddWithValue("accounting_period_id", NpgsqlDbType.Text, receipt.AccountingPeriodId);
        command.Parameters.AddWithValue("accounting_basis", NpgsqlDbType.Text, receipt.AccountingBasis);
        command.Parameters.AddWithValue("as_of_date", NpgsqlDbType.Date, receipt.AsOfDate);
        command.Parameters.AddWithValue("source_checkpoint_id", NpgsqlDbType.Text, receipt.SourceCheckpointId);
        command.Parameters.AddWithValue("source_checkpoint_hash", NpgsqlDbType.Text, receipt.SourceCheckpointHash);
        command.Parameters.AddWithValue("reconciliation_checkpoint_id", NpgsqlDbType.Text, receipt.ReconciliationCheckpointId);
        command.Parameters.AddWithValue("reconciliation_checkpoint_hash", NpgsqlDbType.Text, receipt.ReconciliationCheckpointHash);
        command.Parameters.AddWithValue("receipt_payload", NpgsqlDbType.Text, payload);
        command.Parameters.AddWithValue("receipt_hash_sha256", NpgsqlDbType.Text, payloadHash);
    }

    private async Task<ReportingReconciliationEvidenceReceipt?> ReadCurrentReceiptAsync(
        NpgsqlConnection connection,
        string keyHash,
        string tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select receipt_payload, receipt_hash_sha256 from {_currentTable} where tenant_id = @tenant_id and receipt_key_sha256 = @key_hash;";
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId.Trim());
        command.Parameters.AddWithValue("key_hash", NpgsqlDbType.Text, keyHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var payload = reader.GetString(0);
        VerifyPayloadHash(payload, reader.GetString(1));
        var receipt = DeserializeCurrent(payload);
        ValidatePersistedReceipt(receipt);
        return receipt;
    }

    private async Task<LegacyReportingReconciliationEvidenceReceipt?> ReadLegacyReceiptAsync(
        NpgsqlConnection connection,
        string keyHash,
        string tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select receipt_payload, receipt_hash_sha256 from {_legacyTable} where tenant_id = @tenant_id and receipt_key_sha256 = @key_hash;";
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId.Trim());
        command.Parameters.AddWithValue("key_hash", NpgsqlDbType.Text, keyHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var payload = reader.GetString(0);
        VerifyPayloadHash(payload, reader.GetString(1));
        var receipt = DeserializeLegacy(payload);
        ValidatePersistedLegacyReceipt(receipt);
        return receipt;
    }

    private async Task<string?> VerifyLegacyReceiptForRecoveryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReportingReconciliationEvidenceReceipt recoveryReceipt,
        string keyHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"select receipt_key_sha256, receipt_payload, receipt_hash_sha256 from {_legacyTable} where tenant_id = @tenant_id and receipt_key_sha256 = @key_hash for key share;";
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, recoveryReceipt.TenantId);
        command.Parameters.AddWithValue("key_hash", NpgsqlDbType.Text, keyHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var legacyKey = reader.GetString(0);
        var payload = reader.GetString(1);
        VerifyPayloadHash(payload, reader.GetString(2));
        var legacyReceipt = DeserializeLegacy(payload);
        ValidatePersistedLegacyReceipt(legacyReceipt);
        ValidateLegacyReceiptKey(
            legacyReceipt,
            recoveryReceipt.TenantId,
            recoveryReceipt.OrganizationId,
            recoveryReceipt.CompanyId,
            recoveryReceipt.FundId,
            recoveryReceipt.LedgerBookId,
            recoveryReceipt.AccountingPeriodId,
            recoveryReceipt.AccountingBasis,
            recoveryReceipt.AsOfDate,
            recoveryReceipt.SourceCheckpointId,
            recoveryReceipt.SourceCheckpointHash,
            keyHash);
        return legacyKey;
    }

    private static ReportingReconciliationEvidenceReceipt DeserializeCurrent(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize(
                    payload,
                    ReportingReconciliationEvidenceJsonContext.Default.ReportingReconciliationEvidenceReceipt)
                ?? throw new ReportingArtifactCatalogIntegrityException(
                    "Retained reconciliation evidence deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained reconciliation evidence is not valid JSON: {exception.Message}");
        }
    }

    private static LegacyReportingReconciliationEvidenceReceipt DeserializeLegacy(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize(
                    payload,
                    ReportingReconciliationEvidenceJsonContext.Default.LegacyReportingReconciliationEvidenceReceipt)
                ?? throw new ReportingArtifactCatalogIntegrityException(
                    "Retained legacy reconciliation evidence deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained legacy reconciliation evidence is not valid JSON: {exception.Message}");
        }
    }

    private static void ValidatePersistedReceipt(ReportingReconciliationEvidenceReceipt receipt)
    {
        try
        {
            ReportingReconciliationEvidenceValidation.Validate(receipt);
        }
        catch (ArgumentException exception)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained reconciliation evidence failed canonical validation: {exception.Message}");
        }
    }

    private static void ValidatePersistedLegacyReceipt(LegacyReportingReconciliationEvidenceReceipt receipt)
    {
        try
        {
            ValidateLegacyReceipt(receipt);
        }
        catch (ArgumentException exception)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained legacy reconciliation evidence failed v1 canonical validation: {exception.Message}");
        }
    }

    private static void ValidateCurrentReceiptKey(
        ReportingReconciliationEvidenceReceipt receipt,
        string tenantId,
        string organizationId,
        string? companyId,
        string fundId,
        string ledgerBookId,
        string accountingPeriodId,
        string accountingBasis,
        DateOnly asOfDate,
        string sourceCheckpointId,
        string sourceCheckpointHash,
        string keyHash)
    {
        if (!ReportingReconciliationEvidenceValidation.MatchesKey(
                receipt,
                tenantId,
                organizationId,
                companyId,
                fundId,
                ledgerBookId,
                accountingPeriodId,
                accountingBasis,
                asOfDate,
                sourceCheckpointId,
                sourceCheckpointHash)
            || !string.Equals(keyHash, ComputeKeyHash(receipt), StringComparison.Ordinal))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                "Retained reconciliation evidence does not match its immutable database key.");
        }
    }

    private static void ValidateLegacyReceiptKey(
        LegacyReportingReconciliationEvidenceReceipt receipt,
        string tenantId,
        string organizationId,
        string? companyId,
        string fundId,
        string ledgerBookId,
        string accountingPeriodId,
        string accountingBasis,
        DateOnly asOfDate,
        string sourceCheckpointId,
        string sourceCheckpointHash,
        string keyHash)
    {
        if (!string.Equals(receipt.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(receipt.OrganizationId, organizationId, StringComparison.Ordinal)
            || !string.Equals(receipt.CompanyId, companyId, StringComparison.Ordinal)
            || !string.Equals(receipt.FundId, fundId, StringComparison.Ordinal)
            || !string.Equals(receipt.LedgerBookId, ledgerBookId, StringComparison.Ordinal)
            || !string.Equals(receipt.AccountingPeriodId, accountingPeriodId, StringComparison.Ordinal)
            || !string.Equals(receipt.AccountingBasis, accountingBasis, StringComparison.Ordinal)
            || receipt.AsOfDate != asOfDate
            || !string.Equals(receipt.SourceCheckpointId, sourceCheckpointId, StringComparison.Ordinal)
            || !string.Equals(receipt.SourceCheckpointHash, sourceCheckpointHash, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                keyHash,
                ComputeKeyHash(
                    receipt.TenantId,
                    receipt.OrganizationId,
                    receipt.CompanyId,
                    receipt.FundId,
                    receipt.LedgerBookId,
                    receipt.AccountingPeriodId,
                    receipt.AccountingBasis,
                    receipt.AsOfDate,
                    receipt.SourceCheckpointId,
                    receipt.SourceCheckpointHash),
                StringComparison.Ordinal))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                "Retained legacy reconciliation evidence does not match its immutable database key.");
        }
    }

    private static void ValidateLegacyReceipt(LegacyReportingReconciliationEvidenceReceipt receipt)
    {
        RequireLegacyText(receipt.TenantId, nameof(receipt.TenantId));
        RequireLegacyText(receipt.OrganizationId, nameof(receipt.OrganizationId));
        RequireLegacyText(receipt.CompanyId, nameof(receipt.CompanyId));
        RequireLegacyText(receipt.FundId, nameof(receipt.FundId));
        RequireLegacyText(receipt.LedgerBookId, nameof(receipt.LedgerBookId));
        RequireLegacyText(receipt.AccountingPeriodId, nameof(receipt.AccountingPeriodId));
        RequireLegacyText(receipt.AccountingBasis, nameof(receipt.AccountingBasis));
        RequireLegacyText(receipt.SourceCheckpointId, nameof(receipt.SourceCheckpointId));
        RequireLegacyText(receipt.ReconciliationCheckpointId, nameof(receipt.ReconciliationCheckpointId));
        RequireLegacyText(receipt.CompletionCheckpointId, nameof(receipt.CompletionCheckpointId));
        RequireLegacyHash(receipt.SourceCheckpointHash, nameof(receipt.SourceCheckpointHash));
        RequireLegacyHash(receipt.ReconciliationCheckpointHash, nameof(receipt.ReconciliationCheckpointHash));
        RequireLegacyHash(receipt.CompletionCheckpointHash, nameof(receipt.CompletionCheckpointHash));
        if (receipt.EvidenceIds.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A legacy retained reconciliation receipt requires a UTC timestamp, distinct source/reconciliation identities, and unique exact evidence.",
                nameof(receipt));
        }

        var evidenceWithoutReceipt = receipt.EvidenceIds
            .Where(item => !string.Equals(
                item,
                $"reconciliation-checkpoint:{receipt.ReconciliationCheckpointId}:{receipt.ReconciliationCheckpointHash}",
                StringComparison.Ordinal))
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToImmutableArray();
        var expectedHash = ComputeLegacyReceiptHash(
            receipt.TenantId,
            receipt.OrganizationId,
            receipt.CompanyId,
            receipt.FundId,
            receipt.LedgerBookId,
            receipt.AccountingPeriodId,
            receipt.AccountingBasis,
            receipt.AsOfDate,
            receipt.SourceCheckpointId,
            receipt.SourceCheckpointHash,
            receipt.CompletionCheckpointId!,
            receipt.CompletionCheckpointHash!,
            receipt.ReconciledAtUtc,
            receipt.HasOpenBreaks,
            evidenceWithoutReceipt);
        if (receipt.AsOfDate == default
            || receipt.ReconciledAtUtc == default
            || receipt.ReconciledAtUtc.Offset != TimeSpan.Zero
            || receipt.EvidenceIds.Any(string.IsNullOrWhiteSpace)
            || receipt.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != receipt.EvidenceIds.Length
            || string.Equals(receipt.SourceCheckpointId, receipt.ReconciliationCheckpointId, StringComparison.Ordinal)
            || !string.Equals(
                receipt.ReconciliationCheckpointId,
                $"report-reconciliation-{expectedHash[..32]}",
                StringComparison.Ordinal)
            || !string.Equals(receipt.ReconciliationCheckpointHash, expectedHash, StringComparison.Ordinal)
            || !receipt.EvidenceIds.Contains(
                $"reconciliation-completion:{receipt.CompletionCheckpointId}:{receipt.CompletionCheckpointHash}",
                StringComparer.Ordinal)
            || !receipt.EvidenceIds.Contains(
                $"reconciliation-checkpoint:{receipt.ReconciliationCheckpointId}:{receipt.ReconciliationCheckpointHash}",
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "A legacy retained reconciliation receipt requires a UTC timestamp, distinct source/reconciliation identities, and unique exact evidence.",
                nameof(receipt));
        }
    }

    private static string ComputeLegacyReceiptHash(
        string tenantId,
        string organizationId,
        string? companyId,
        string fundId,
        string ledgerBookId,
        string accountingPeriodId,
        string accountingBasis,
        DateOnly asOfDate,
        string sourceCheckpointId,
        string sourceCheckpointHash,
        string completionCheckpointId,
        string completionCheckpointHash,
        DateTimeOffset reconciledAtUtc,
        bool hasOpenBreaks,
        ImmutableArray<string> evidenceIds)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("tenantId", tenantId);
            writer.WriteString("organizationId", organizationId);
            writer.WriteString("companyId", companyId);
            writer.WriteString("fundId", fundId);
            writer.WriteString("ledgerBookId", ledgerBookId);
            writer.WriteString("accountingPeriodId", accountingPeriodId);
            writer.WriteString("accountingBasis", accountingBasis);
            writer.WriteString("asOfDate", asOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("sourceCheckpointId", sourceCheckpointId);
            writer.WriteString("sourceCheckpointHash", sourceCheckpointHash);
            writer.WriteString("completionCheckpointId", completionCheckpointId);
            writer.WriteString("completionCheckpointHash", completionCheckpointHash);
            writer.WriteString("reconciledAtUtc", reconciledAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteBoolean("hasOpenBreaks", hasOpenBreaks);
            writer.WriteStartArray("evidenceIds");
            foreach (var evidence in evidenceIds.OrderBy(static item => item, StringComparer.Ordinal))
            {
                writer.WriteStringValue(evidence);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void RequireLegacyText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Legacy retained reconciliation identifiers must be present and trimmed.", parameterName);
        }
    }

    private static void RequireLegacyHash(string? value, string parameterName)
    {
        if (!ReportingReconciliationEvidenceValidation.IsLowercaseSha256(value))
        {
            throw new ArgumentException("Legacy retained reconciliation hashes must be lowercase SHA-256 values.", parameterName);
        }
    }

    private static string ComputeKeyHash(ReportingReconciliationEvidenceReceipt receipt) =>
        ComputeKeyHash(
            receipt.TenantId,
            receipt.OrganizationId,
            receipt.CompanyId,
            receipt.FundId,
            receipt.LedgerBookId,
            receipt.AccountingPeriodId,
            receipt.AccountingBasis,
            receipt.AsOfDate,
            receipt.SourceCheckpointId,
            receipt.SourceCheckpointHash);

    private static string ComputeKeyHash(
        string tenantId,
        string organizationId,
        string? companyId,
        string fundId,
        string ledgerBookId,
        string accountingPeriodId,
        string accountingBasis,
        DateOnly asOfDate,
        string sourceCheckpointId,
        string sourceCheckpointHash) =>
        Sha256Digest.ComputeUtf8(string.Join('\n',
            NormalizeKey(tenantId, nameof(tenantId)),
            NormalizeKey(organizationId, nameof(organizationId)),
            NormalizeKey(companyId, nameof(companyId)),
            NormalizeKey(fundId, nameof(fundId)),
            NormalizeKey(ledgerBookId, nameof(ledgerBookId)),
            NormalizeKey(accountingPeriodId, nameof(accountingPeriodId)),
            NormalizeKey(accountingBasis, nameof(accountingBasis)),
            asOfDate == default
                ? throw new ArgumentException("A valid as-of date is required.", nameof(asOfDate))
                : asOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            NormalizeKey(sourceCheckpointId, nameof(sourceCheckpointId)),
            NormalizeKey(sourceCheckpointHash, nameof(sourceCheckpointHash)).ToLowerInvariant()));

    private static void VerifyPayloadHash(string payload, string declaredHash)
    {
        if (!ReportingReconciliationEvidenceValidation.IsLowercaseSha256(declaredHash)
            || !string.Equals(declaredHash, Sha256Digest.ComputeUtf8(payload), StringComparison.Ordinal))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                "Retained reconciliation evidence payload hash verification failed.");
        }
    }

    private static string NormalizeKey(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || !string.Equals(normalized, value, StringComparison.Ordinal))
        {
            throw new ArgumentException("Reporting reconciliation keys must be present and trimmed.", parameterName);
        }

        return normalized;
    }

    private static void ValidateDatabaseIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !(char.IsAsciiLetter(value[0]) || value[0] == '_')
            || !value.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            throw new ArgumentException(
                $"PostgreSQL identifier '{value}' is not supported.",
                nameof(value));
        }
    }
}

/// <summary>
/// Raised after a legacy PostgreSQL receipt has passed its original payload, key, and receipt-hash
/// checks but cannot prove the current item-level break-evidence requirement.
/// </summary>
public sealed class ReportingReconciliationEvidenceLegacyMigrationRequiredException :
    ReportingReconciliationEvidenceRecoveryRequiredException
{
    public ReportingReconciliationEvidenceLegacyMigrationRequiredException(string message)
        : base(message)
    {
    }
}
