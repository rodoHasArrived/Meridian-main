using System.Collections.Generic;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresDataVendorEntitlementStore : IDataVendorEntitlementStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresDataVendorEntitlementStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<IReadOnlyList<DataVendorEntitlementDto>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select entitlement_id, vendor_name, data_type, contract_reference,
                   effective_from, effective_to, aum_threshold_usd, requires_direct_client_contract,
                   contact_email, renewal_reminder_days, is_active, created_by, created_at
            from {Qualified("data_vendor_entitlements")}
            order by vendor_name, data_type;
            """;

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<DataVendorEntitlementDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(ReadDto(reader));

        return results;
    }

    public async Task<IReadOnlyList<DataVendorEntitlementDto>> GetExpiringAsync(
        DateTimeOffset cutoffUtc, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select entitlement_id, vendor_name, data_type, contract_reference,
                   effective_from, effective_to, aum_threshold_usd, requires_direct_client_contract,
                   contact_email, renewal_reminder_days, is_active, created_by, created_at
            from {Qualified("data_vendor_entitlements")}
            where is_active = true
              and effective_to is not null
              and effective_to >= now()
              and effective_to <= @cutoff
            order by effective_to;
            """;
        command.Parameters.AddWithValue("cutoff", cutoffUtc);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<DataVendorEntitlementDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(ReadDto(reader));

        return results;
    }

    public async Task<DataVendorEntitlementDto?> GetByIdAsync(Guid entitlementId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select entitlement_id, vendor_name, data_type, contract_reference,
                   effective_from, effective_to, aum_threshold_usd, requires_direct_client_contract,
                   contact_email, renewal_reminder_days, is_active, created_by, created_at
            from {Qualified("data_vendor_entitlements")}
            where entitlement_id = @entitlement_id;
            """;
        command.Parameters.AddWithValue("entitlement_id", entitlementId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return ReadDto(reader);
    }

    public async Task<DataVendorEntitlementDto> UpsertAsync(
        DataVendorEntitlementDto entitlement, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("data_vendor_entitlements")}
                (entitlement_id, vendor_name, data_type, contract_reference,
                 effective_from, effective_to, aum_threshold_usd, requires_direct_client_contract,
                 contact_email, renewal_reminder_days, is_active, created_by, created_at)
            values (@entitlement_id, @vendor_name, @data_type, @contract_reference,
                    @effective_from, @effective_to, @aum_threshold_usd, @requires_direct_client_contract,
                    @contact_email, @renewal_reminder_days, @is_active, @created_by, @created_at)
            on conflict (entitlement_id) do update
                set vendor_name = excluded.vendor_name,
                    data_type = excluded.data_type,
                    contract_reference = excluded.contract_reference,
                    effective_from = excluded.effective_from,
                    effective_to = excluded.effective_to,
                    aum_threshold_usd = excluded.aum_threshold_usd,
                    requires_direct_client_contract = excluded.requires_direct_client_contract,
                    contact_email = excluded.contact_email,
                    renewal_reminder_days = excluded.renewal_reminder_days,
                    is_active = excluded.is_active;
            """;
        command.Parameters.AddWithValue("entitlement_id", entitlement.EntitlementId);
        command.Parameters.AddWithValue("vendor_name", entitlement.VendorName);
        command.Parameters.AddWithValue("data_type", entitlement.DataType.ToString());
        command.Parameters.AddWithValue("contract_reference", (object?)entitlement.ContractReference ?? DBNull.Value);
        command.Parameters.AddWithValue("effective_from", entitlement.EffectiveFrom.UtcDateTime);
        command.Parameters.AddWithValue("effective_to", (object?)entitlement.EffectiveTo?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("aum_threshold_usd", (object?)entitlement.AumThresholdUsd ?? DBNull.Value);
        command.Parameters.AddWithValue("requires_direct_client_contract", entitlement.RequiresDirectClientContract);
        command.Parameters.AddWithValue("contact_email", (object?)entitlement.ContactEmail ?? DBNull.Value);
        command.Parameters.AddWithValue("renewal_reminder_days", entitlement.RenewalReminderDays);
        command.Parameters.AddWithValue("is_active", entitlement.Status != DataVendorEntitlementStatus.Expired);
        command.Parameters.AddWithValue("created_by", entitlement.CreatedBy);
        command.Parameters.AddWithValue("created_at", entitlement.CreatedAt.UtcDateTime);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return entitlement;
    }

    public async Task DeactivateAsync(Guid entitlementId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            update {Qualified("data_vendor_entitlements")}
            set is_active = false, effective_to = now()
            where entitlement_id = @entitlement_id;
            """;
        command.Parameters.AddWithValue("entitlement_id", entitlementId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static DataVendorEntitlementDto ReadDto(NpgsqlDataReader reader)
    {
        var entitlementId = reader.GetGuid(0);
        var vendorName = reader.GetString(1);
        var dataType = Enum.Parse<DataVendorDataType>(reader.GetString(2));
        var contractRef = reader.IsDBNull(3) ? null : reader.GetString(3);
        var effectiveFrom = reader.GetFieldValue<DateTimeOffset>(4);
        var effectiveTo = reader.IsDBNull(5)
            ? (DateTimeOffset?)null
            : reader.GetFieldValue<DateTimeOffset>(5);
        var aumThreshold = reader.IsDBNull(6) ? (decimal?)null : reader.GetDecimal(6);
        var requiresDirect = reader.GetBoolean(7);
        var contactEmail = reader.IsDBNull(8) ? null : reader.GetString(8);
        var renewalDays = reader.GetInt32(9);
        var isActive = reader.GetBoolean(10);
        var createdBy = reader.GetString(11);
        var createdAt = reader.GetFieldValue<DateTimeOffset>(12);

        var status = DeriveStatus(isActive, effectiveTo, renewalDays);

        return new DataVendorEntitlementDto(
            entitlementId, vendorName, dataType, contractRef,
            effectiveFrom, effectiveTo, aumThreshold, requiresDirect,
            contactEmail, renewalDays, status, createdBy, createdAt);
    }

    private static DataVendorEntitlementStatus DeriveStatus(bool isActive, DateTimeOffset? effectiveTo, int renewalReminderDays)
    {
        if (!isActive || (effectiveTo.HasValue && effectiveTo.Value < DateTimeOffset.UtcNow))
            return DataVendorEntitlementStatus.Expired;

        if (effectiveTo.HasValue && effectiveTo.Value <= DateTimeOffset.UtcNow.AddDays(renewalReminderDays))
            return DataVendorEntitlementStatus.ExpiringSoon;

        return DataVendorEntitlementStatus.Active;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table) => $"{_options.Schema}.{table}";
}
