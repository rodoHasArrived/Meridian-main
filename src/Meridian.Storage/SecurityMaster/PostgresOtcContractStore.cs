using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// PostgreSQL store for the OTC contract model (migration 050): master agreements, collateral
/// agreements, netting sets and bilateral contracts. Every write passes <see cref="OtcContractRules"/>
/// first, so the database constraints are a backstop rather than the first line of validation.
/// Each write locks the row it versions before writing it; a contract write first takes the
/// contracts lock of each netting set it touches (<see cref="OtcAdvisoryLocks"/>), so it cannot
/// change a netting set while a margin call is being issued on it.
/// </summary>
public sealed partial class PostgresOtcContractStore : IOtcContractStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresOtcContractStore(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<OtcMasterAgreementDto> UpsertMasterAgreementAsync(UpsertOtcMasterAgreementRequest request, CancellationToken ct = default)
    {
        var normalized = OtcContractRules.Normalize(request);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var id = normalized.AgreementId ?? Guid.NewGuid();
        var version = await NextVersionAsync(connection, transaction, "otc_master_agreements", "agreement_id", id, normalized.AgreementId is not null, "master agreement", ct).ConfigureAwait(false);
        var dto = new OtcMasterAgreementDto(
            id,
            normalized.Counterparty,
            normalized.CounterpartyLei,
            normalized.OurEntity,
            normalized.OurEntityId,
            normalized.AgreementType,
            normalized.AgreementDate,
            normalized.GoverningLaw,
            normalized.Notes,
            version,
            normalized.UpdatedBy!,
            DateTimeOffset.UtcNow);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {_options.Schema}.otc_master_agreements (
                    agreement_id, counterparty, counterparty_lei, our_entity, our_entity_id, agreement_type,
                    agreement_date, governing_law, notes, version, updated_by, updated_at)
                values (
                    @id, @counterparty, @lei, @our_entity, @our_entity_id, @type,
                    @date, @law, @notes, @version, @updated_by, @updated_at)
                on conflict (agreement_id) do update set
                    counterparty = excluded.counterparty,
                    counterparty_lei = excluded.counterparty_lei,
                    our_entity = excluded.our_entity,
                    our_entity_id = excluded.our_entity_id,
                    agreement_type = excluded.agreement_type,
                    agreement_date = excluded.agreement_date,
                    governing_law = excluded.governing_law,
                    notes = excluded.notes,
                    version = excluded.version,
                    updated_by = excluded.updated_by,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("id", dto.AgreementId);
            command.Parameters.AddWithValue("counterparty", dto.Counterparty);
            command.Parameters.AddWithValue("lei", (object?)dto.CounterpartyLei ?? DBNull.Value);
            command.Parameters.AddWithValue("our_entity", dto.OurEntity);
            command.Parameters.AddWithValue("our_entity_id", (object?)dto.OurEntityId ?? DBNull.Value);
            command.Parameters.AddWithValue("type", dto.AgreementType.ToString());
            command.Parameters.AddWithValue("date", dto.AgreementDate);
            command.Parameters.AddWithValue("law", (object?)dto.GoverningLaw ?? DBNull.Value);
            command.Parameters.AddWithValue("notes", (object?)dto.Notes ?? DBNull.Value);
            command.Parameters.AddWithValue("version", dto.Version);
            command.Parameters.AddWithValue("updated_by", dto.UpdatedBy);
            command.Parameters.AddWithValue("updated_at", dto.UpdatedAt.UtcDateTime);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return dto;
    }

    public async Task<OtcMasterAgreementDto?> GetMasterAgreementAsync(Guid agreementId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        var rows = await ReadMasterAgreementsAsync(connection, null, "where agreement_id = @id", ("id", agreementId), ct).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<IReadOnlyList<OtcMasterAgreementDto>> ListMasterAgreementsAsync(string? counterparty = null, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(counterparty)
            ? await ReadMasterAgreementsAsync(connection, null, string.Empty, null, ct).ConfigureAwait(false)
            : await ReadMasterAgreementsAsync(connection, null, "where lower(counterparty) = lower(@counterparty)", ("counterparty", counterparty.Trim()), ct).ConfigureAwait(false);
    }

    public async Task<OtcCollateralAgreementDto> UpsertCollateralAgreementAsync(UpsertOtcCollateralAgreementRequest request, CancellationToken ct = default)
    {
        var normalized = OtcContractRules.Normalize(request);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await RequireRowAsync(connection, transaction, "otc_master_agreements", "agreement_id", normalized.MasterAgreementId, "master agreement", ct).ConfigureAwait(false);
        var id = normalized.CollateralAgreementId ?? Guid.NewGuid();
        var version = await NextVersionAsync(connection, transaction, "otc_collateral_agreements", "collateral_agreement_id", id, normalized.CollateralAgreementId is not null, "collateral agreement", ct).ConfigureAwait(false);
        var dto = new OtcCollateralAgreementDto(
            id,
            normalized.MasterAgreementId,
            normalized.Currency,
            normalized.ThresholdOurs,
            normalized.ThresholdTheirs,
            normalized.MinimumTransferAmount,
            normalized.IndependentAmount,
            normalized.RoundingAmount,
            normalized.ValuationFrequency,
            normalized.EligibleCollateral,
            normalized.EffectiveDate,
            normalized.TerminationDate,
            normalized.Notes,
            version,
            normalized.UpdatedBy!,
            DateTimeOffset.UtcNow);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {_options.Schema}.otc_collateral_agreements (
                    collateral_agreement_id, master_agreement_id, currency, threshold_ours, threshold_theirs,
                    minimum_transfer_amount, independent_amount, rounding_amount, valuation_frequency,
                    eligible_collateral, effective_date, termination_date, notes, version, updated_by, updated_at)
                values (
                    @id, @master, @currency, @threshold_ours, @threshold_theirs,
                    @mta, @independent, @rounding, @frequency,
                    @eligible, @effective, @termination, @notes, @version, @updated_by, @updated_at)
                on conflict (collateral_agreement_id) do update set
                    master_agreement_id = excluded.master_agreement_id,
                    currency = excluded.currency,
                    threshold_ours = excluded.threshold_ours,
                    threshold_theirs = excluded.threshold_theirs,
                    minimum_transfer_amount = excluded.minimum_transfer_amount,
                    independent_amount = excluded.independent_amount,
                    rounding_amount = excluded.rounding_amount,
                    valuation_frequency = excluded.valuation_frequency,
                    eligible_collateral = excluded.eligible_collateral,
                    effective_date = excluded.effective_date,
                    termination_date = excluded.termination_date,
                    notes = excluded.notes,
                    version = excluded.version,
                    updated_by = excluded.updated_by,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("id", dto.CollateralAgreementId);
            command.Parameters.AddWithValue("master", dto.MasterAgreementId);
            command.Parameters.AddWithValue("currency", dto.Currency);
            command.Parameters.AddWithValue("threshold_ours", dto.ThresholdOurs);
            command.Parameters.AddWithValue("threshold_theirs", dto.ThresholdTheirs);
            command.Parameters.AddWithValue("mta", dto.MinimumTransferAmount);
            command.Parameters.AddWithValue("independent", (object?)dto.IndependentAmount ?? DBNull.Value);
            command.Parameters.AddWithValue("rounding", (object?)dto.RoundingAmount ?? DBNull.Value);
            command.Parameters.AddWithValue("frequency", dto.ValuationFrequency);
            command.Parameters.Add(new NpgsqlParameter("eligible", NpgsqlDbType.Jsonb)
            {
                Value = JsonSerializer.Serialize(dto.EligibleCollateral.ToArray(), OtcContractsJsonContext.Default.OtcEligibleCollateralDtoArray)
            });
            command.Parameters.AddWithValue("effective", dto.EffectiveDate);
            command.Parameters.AddWithValue("termination", (object?)dto.TerminationDate ?? DBNull.Value);
            command.Parameters.AddWithValue("notes", (object?)dto.Notes ?? DBNull.Value);
            command.Parameters.AddWithValue("version", dto.Version);
            command.Parameters.AddWithValue("updated_by", dto.UpdatedBy);
            command.Parameters.AddWithValue("updated_at", dto.UpdatedAt.UtcDateTime);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return dto;
    }

    public async Task<OtcCollateralAgreementDto?> GetCollateralAgreementAsync(Guid collateralAgreementId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadCollateralAgreementAsync(connection, null, _options.Schema, collateralAgreementId, ct).ConfigureAwait(false);
    }

    public async Task<OtcNettingSetDto> UpsertNettingSetAsync(UpsertOtcNettingSetRequest request, CancellationToken ct = default)
    {
        var normalized = OtcContractRules.Normalize(request);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await RequireRowAsync(connection, transaction, "otc_master_agreements", "agreement_id", normalized.MasterAgreementId, "master agreement", ct).ConfigureAwait(false);
        if (normalized.CollateralAgreementId is { } csaId)
        {
            var csa = await ReadCollateralAgreementAsync(connection, transaction, _options.Schema, csaId, ct).ConfigureAwait(false)
                ?? throw new ArgumentException($"Collateral agreement {csaId:D} does not exist.", nameof(request));
            if (csa.MasterAgreementId != normalized.MasterAgreementId)
                throw new ArgumentException("A netting set's collateral agreement must sit under the same master agreement.", nameof(request));
        }

        var id = normalized.NettingSetId ?? Guid.NewGuid();
        var version = await NextVersionAsync(connection, transaction, "otc_netting_sets", "netting_set_id", id, normalized.NettingSetId is not null, "netting set", ct).ConfigureAwait(false);
        var dto = new OtcNettingSetDto(
            id,
            normalized.MasterAgreementId,
            normalized.CollateralAgreementId,
            normalized.Name,
            normalized.Notes,
            version,
            normalized.UpdatedBy!,
            DateTimeOffset.UtcNow);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {_options.Schema}.otc_netting_sets (
                    netting_set_id, master_agreement_id, collateral_agreement_id, name, notes, version, updated_by, updated_at)
                values (@id, @master, @csa, @name, @notes, @version, @updated_by, @updated_at)
                on conflict (netting_set_id) do update set
                    master_agreement_id = excluded.master_agreement_id,
                    collateral_agreement_id = excluded.collateral_agreement_id,
                    name = excluded.name,
                    notes = excluded.notes,
                    version = excluded.version,
                    updated_by = excluded.updated_by,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("id", dto.NettingSetId);
            command.Parameters.AddWithValue("master", dto.MasterAgreementId);
            command.Parameters.AddWithValue("csa", (object?)dto.CollateralAgreementId ?? DBNull.Value);
            command.Parameters.AddWithValue("name", dto.Name);
            command.Parameters.AddWithValue("notes", (object?)dto.Notes ?? DBNull.Value);
            command.Parameters.AddWithValue("version", dto.Version);
            command.Parameters.AddWithValue("updated_by", dto.UpdatedBy);
            command.Parameters.AddWithValue("updated_at", dto.UpdatedAt.UtcDateTime);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return dto;
    }

    public async Task<OtcNettingSetViewDto?> GetNettingSetAsync(Guid nettingSetId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        var nettingSet = await ReadNettingSetAsync(connection, null, nettingSetId, ct).ConfigureAwait(false);
        if (nettingSet is null)
            return null;

        var master = (await ReadMasterAgreementsAsync(connection, null, "where agreement_id = @id", ("id", nettingSet.MasterAgreementId), ct).ConfigureAwait(false))[0];
        var csa = nettingSet.CollateralAgreementId is { } csaId
            ? await ReadCollateralAgreementAsync(connection, null, _options.Schema, csaId, ct).ConfigureAwait(false)
            : null;
        var contracts = await ReadContractsAsync(connection, null, "where netting_set_id = @id", nettingSetId, ct).ConfigureAwait(false);
        return new OtcNettingSetViewDto(nettingSet, master, csa, contracts);
    }

    public async Task<OtcContractDto> UpsertContractAsync(UpsertOtcContractRequest request, CancellationToken ct = default)
    {
        var normalized = OtcContractRules.Normalize(request);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var assetClass = await ReadAssetClassAsync(connection, transaction, normalized.SecurityId, ct).ConfigureAwait(false)
            ?? throw new ArgumentException($"Security {normalized.SecurityId:D} is not in the Security Master.", nameof(request));
        OtcContractRules.RequireBilateralAssetClass(assetClass);
        await RequireRowAsync(connection, transaction, "otc_netting_sets", "netting_set_id", normalized.NettingSetId, "netting set", ct).ConfigureAwait(false);

        var id = normalized.ContractId ?? Guid.NewGuid();
        long version;
        if (normalized.ContractId is not null)
        {
            // A write waits for, and is waited on by, a margin call being issued on the netting set it
            // leaves or joins, so an issue cannot miss a contract amended, added or moved while it
            // checks the snapshot's inputs. The contract is read once to learn its netting set, both
            // netting sets are locked in id order before any row lock (see OtcAdvisoryLocks), and the
            // row is read again under lock: a move made in between is refused rather than overwritten.
            var probe = (await ReadContractsAsync(connection, transaction, "where contract_id = @id", id, ct).ConfigureAwait(false)).SingleOrDefault()
                ?? throw new ArgumentException($"Contract {id:D} does not exist.", nameof(request));
            await OtcAdvisoryLocks.LockNettingSetContractsAsync(connection, transaction, [probe.NettingSetId, normalized.NettingSetId], ct).ConfigureAwait(false);
            var existing = (await ReadContractsAsync(connection, transaction, "where contract_id = @id", id, ct, forUpdate: true).ConfigureAwait(false)).SingleOrDefault()
                ?? throw new ArgumentException($"Contract {id:D} does not exist.", nameof(request));
            if (existing.NettingSetId != probe.NettingSetId)
                throw new InvalidOperationException($"Contract {id:D} moved to another netting set under the write; retry.");
            if (OtcContractRules.IsClosed(existing.Status))
                throw new ArgumentException($"Contract {id:D} is {existing.Status} and can no longer be amended.", nameof(request));
            if (existing.SecurityId != normalized.SecurityId)
                throw new ArgumentException("A contract cannot be moved to another instrument; book a new contract.", nameof(request));
            if ((await ReadLifecycleEventsAsync(connection, transaction, id, ct).ConfigureAwait(false)).Count > 0)
                throw new ArgumentException("A lifecycle-managed contract cannot be amended through a snapshot upsert; use an explicit retained lifecycle workflow.", nameof(request));
            version = existing.Version + 1;
        }
        else
        {
            await OtcAdvisoryLocks.LockNettingSetContractsAsync(connection, transaction, [normalized.NettingSetId], ct).ConfigureAwait(false);
            version = 1;
        }

        var dto = new OtcContractDto(
            id,
            normalized.SecurityId,
            normalized.NettingSetId,
            normalized.ExternalTradeId,
            normalized.Direction,
            normalized.Notional,
            normalized.NotionalCurrency,
            normalized.TradeDate,
            normalized.EffectiveDate,
            normalized.TerminationDate,
            normalized.Status,
            normalized.ConfirmationReference,
            normalized.Notes,
            version,
            normalized.UpdatedBy!,
            DateTimeOffset.UtcNow);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {_options.Schema}.otc_contracts (
                    contract_id, security_id, netting_set_id, external_trade_id, direction, notional, notional_currency,
                    trade_date, effective_date, termination_date, status, confirmation_reference, notes, version, updated_by, updated_at)
                values (
                    @id, @security_id, @netting_set_id, @external_trade_id, @direction, @notional, @currency,
                    @trade_date, @effective_date, @termination_date, @status, @confirmation, @notes, @version, @updated_by, @updated_at)
                on conflict (contract_id) do update set
                    netting_set_id = excluded.netting_set_id,
                    external_trade_id = excluded.external_trade_id,
                    direction = excluded.direction,
                    notional = excluded.notional,
                    notional_currency = excluded.notional_currency,
                    trade_date = excluded.trade_date,
                    effective_date = excluded.effective_date,
                    termination_date = excluded.termination_date,
                    status = excluded.status,
                    confirmation_reference = excluded.confirmation_reference,
                    notes = excluded.notes,
                    version = excluded.version,
                    updated_by = excluded.updated_by,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("id", dto.ContractId);
            command.Parameters.AddWithValue("security_id", dto.SecurityId);
            command.Parameters.AddWithValue("netting_set_id", dto.NettingSetId);
            command.Parameters.AddWithValue("external_trade_id", (object?)dto.ExternalTradeId ?? DBNull.Value);
            command.Parameters.AddWithValue("direction", dto.Direction.ToString());
            command.Parameters.AddWithValue("notional", dto.Notional);
            command.Parameters.AddWithValue("currency", dto.NotionalCurrency);
            command.Parameters.AddWithValue("trade_date", dto.TradeDate);
            command.Parameters.AddWithValue("effective_date", dto.EffectiveDate);
            command.Parameters.AddWithValue("termination_date", (object?)dto.TerminationDate ?? DBNull.Value);
            command.Parameters.AddWithValue("status", dto.Status.ToString());
            command.Parameters.AddWithValue("confirmation", (object?)dto.ConfirmationReference ?? DBNull.Value);
            command.Parameters.AddWithValue("notes", (object?)dto.Notes ?? DBNull.Value);
            command.Parameters.AddWithValue("version", dto.Version);
            command.Parameters.AddWithValue("updated_by", dto.UpdatedBy);
            command.Parameters.AddWithValue("updated_at", dto.UpdatedAt.UtcDateTime);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return dto;
    }

    public async Task<OtcContractDto?> GetContractAsync(Guid contractId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return (await ReadContractsAsync(connection, null, "where contract_id = @id", contractId, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<OtcContractDto>> ListContractsForSecurityAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadContractsAsync(connection, null, "where security_id = @id", securityId, ct).ConfigureAwait(false);
    }

    private async Task<string?> ReadAssetClassAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid securityId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select asset_class from {_options.Schema}.securities where security_id = @id;";
        command.Parameters.AddWithValue("id", securityId);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    private async Task RequireRowAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string keyColumn, Guid id, string noun, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select 1 from {_options.Schema}.{table} where {keyColumn} = @id;";
        command.Parameters.AddWithValue("id", id);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            throw new ArgumentException($"The {noun} {id:D} does not exist.");
    }

    private async Task<long> NextVersionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string keyColumn, Guid id, bool mustExist, string noun, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select version from {_options.Schema}.{table} where {keyColumn} = @id for update;";
        command.Parameters.AddWithValue("id", id);
        var current = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (current is null)
        {
            return mustExist ? throw new ArgumentException($"The {noun} {id:D} does not exist.") : 1;
        }

        return (long)current + 1;
    }

    private async Task<IReadOnlyList<OtcMasterAgreementDto>> ReadMasterAgreementsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string whereClause, (string Name, object Value)? parameter, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select agreement_id, counterparty, counterparty_lei, our_entity, our_entity_id, agreement_type,
                   agreement_date, governing_law, notes, version, updated_by, updated_at
            from {_options.Schema}.otc_master_agreements
            {whereClause}
            order by counterparty, agreement_date, agreement_id;
            """;
        if (parameter is { } p)
            command.Parameters.AddWithValue(p.Name, p.Value);

        var rows = new List<OtcMasterAgreementDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new OtcMasterAgreementDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                Enum.Parse<OtcMasterAgreementType>(reader.GetString(5)),
                reader.GetFieldValue<DateOnly>(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetInt64(9),
                reader.GetString(10),
                Utc(reader.GetDateTime(11))));
        }

        return rows;
    }

    /// <summary>
    /// Reads one collateral agreement with the caller's connection and transaction. Shared with
    /// <see cref="PostgresOtcCollateralStore"/>, which resolves eligible collateral and currency
    /// against the agreement inside its own write transaction.
    /// </summary>
    internal static async Task<OtcCollateralAgreementDto?> ReadCollateralAgreementAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string schema, Guid id, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select collateral_agreement_id, master_agreement_id, currency, threshold_ours, threshold_theirs,
                   minimum_transfer_amount, independent_amount, rounding_amount, valuation_frequency,
                   eligible_collateral, effective_date, termination_date, notes, version, updated_by, updated_at
            from {schema}.otc_collateral_agreements
            where collateral_agreement_id = @id;
            """;
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        var eligible = JsonSerializer.Deserialize(reader.GetString(9), OtcContractsJsonContext.Default.OtcEligibleCollateralDtoArray) ?? [];
        return new OtcCollateralAgreementDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : reader.GetDecimal(6),
            reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            reader.GetString(8),
            eligible,
            reader.GetFieldValue<DateOnly>(10),
            reader.IsDBNull(11) ? null : reader.GetFieldValue<DateOnly>(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetInt64(13),
            reader.GetString(14),
            Utc(reader.GetDateTime(15)));
    }

    private async Task<OtcNettingSetDto?> ReadNettingSetAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid id, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select netting_set_id, master_agreement_id, collateral_agreement_id, name, notes, version, updated_by, updated_at
            from {_options.Schema}.otc_netting_sets
            where netting_set_id = @id;
            """;
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return new OtcNettingSetDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt64(5),
            reader.GetString(6),
            Utc(reader.GetDateTime(7)));
    }

    private async Task<IReadOnlyList<OtcContractDto>> ReadContractsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string whereClause, Guid id, CancellationToken ct, bool forUpdate = false)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select contract_id, security_id, netting_set_id, external_trade_id, direction, notional, notional_currency,
                   trade_date, effective_date, termination_date, status, confirmation_reference, notes, version, updated_by, updated_at
            from {_options.Schema}.otc_contracts
            {whereClause}
            order by trade_date, contract_id{(forUpdate ? " for update" : string.Empty)};
            """;
        command.Parameters.AddWithValue("id", id);

        var rows = new List<OtcContractDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new OtcContractDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                Enum.Parse<OtcContractDirection>(reader.GetString(4)),
                reader.GetDecimal(5),
                reader.GetString(6),
                reader.GetFieldValue<DateOnly>(7),
                reader.GetFieldValue<DateOnly>(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
                Enum.Parse<OtcContractStatus>(reader.GetString(10)),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetInt64(13),
                reader.GetString(14),
                Utc(reader.GetDateTime(15))));
        }

        return rows;
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
