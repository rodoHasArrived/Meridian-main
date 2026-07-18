using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Reporting;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Reporting;

/// <summary>
/// Production append-only PostgreSQL receipt store. Every read verifies the database key, payload
/// hash, canonical receipt hash, and exact source scope before returning evidence to certification.
/// </summary>
public sealed class PostgresReportingReconciliationEvidenceStore :
    IReportingReconciliationEvidenceRetentionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ReportingArtifactStoreOptions _options;
    private readonly string _table;

    public PostgresReportingReconciliationEvidenceStore(ReportingArtifactStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ValidateDatabaseIdentifier(_options.Schema);
        _table = $"\"{_options.Schema}\".\"reporting_reconciliation_evidence\"";
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
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select receipt_payload, receipt_hash_sha256 from {_table} where tenant_id = @tenant_id and receipt_key_sha256 = @key_hash;";
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId.Trim());
        command.Parameters.AddWithValue("key_hash", NpgsqlDbType.Text, keyHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var payload = reader.GetString(0);
        VerifyPayloadHash(payload, reader.GetString(1));
        var receipt = Deserialize(payload);
        ValidatePersistedReceipt(receipt);
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

        return receipt;
    }

    public async ValueTask<bool> RetainAsync(
        ReportingReconciliationEvidenceReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ReportingReconciliationEvidenceValidation.Validate(receipt);
        var keyHash = ComputeKeyHash(receipt);
        var payload = JsonSerializer.Serialize(receipt, JsonOptions);
        var payloadHash = ComputeSha256(payload);
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {_table} (
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
                receipt_hash_sha256)
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
                @receipt_hash_sha256)
            on conflict (tenant_id, receipt_key_sha256) do nothing;
            """;
        AddParameters(command, receipt, keyHash, payload, payloadHash);
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        if (!inserted)
        {
            await using var verify = connection.CreateCommand();
            verify.Transaction = transaction;
            verify.CommandText =
                $"select receipt_payload, receipt_hash_sha256 from {_table} where tenant_id = @tenant_id and receipt_key_sha256 = @key_hash for share;";
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

    private static ReportingReconciliationEvidenceReceipt Deserialize(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<ReportingReconciliationEvidenceReceipt>(payload, JsonOptions)
                ?? throw new ReportingArtifactCatalogIntegrityException(
                    "Retained reconciliation evidence deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained reconciliation evidence is not valid JSON: {exception.Message}");
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
        ComputeSha256(string.Join('\n',
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
            || !string.Equals(declaredHash, ComputeSha256(payload), StringComparison.Ordinal))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                "Retained reconciliation evidence payload hash verification failed.");
        }
    }

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

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
