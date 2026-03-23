using System.Data;
using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Npgsql;

namespace Meridian.Storage.DirectLending;

public sealed partial class PostgresDirectLendingStateStore : IDirectLendingStateStore, IDirectLendingOperationsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DirectLendingOptions _options;

    public PostgresDirectLendingStateStore(DirectLendingOptions options)
    {
        _options = options;
    }

    public async Task<LoanContractDetailDto?> LoadContractProjectionAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select facility_name,
                   borrower_id,
                   borrower_name,
                   legal_entity_id,
                   status,
                   effective_date,
                   activation_date,
                   close_date,
                   current_terms_version
            from {Qualified("loan_contract")}
            where loan_id = @loan_id;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var facilityName = reader.GetString(0);
        var borrower = new BorrowerInfoDto(
            reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3));
        var status = ParseEnum<LoanStatus>(reader.GetString(4));
        var effectiveDate = DateOnly.FromDateTime(reader.GetDateTime(5));
        DateOnly? activationDate = reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6));
        DateOnly? closeDate = reader.IsDBNull(7) ? null : DateOnly.FromDateTime(reader.GetDateTime(7));
        var currentTermsVersion = reader.GetInt32(8);
        await reader.CloseAsync().ConfigureAwait(false);

        var termsVersions = await LoadTermsVersionProjectionsCoreAsync(connection, loanId, ct).ConfigureAwait(false);
        var currentTerms = termsVersions.FirstOrDefault(item => item.VersionNumber == currentTermsVersion)?.Terms;
        if (currentTerms is null)
        {
            return null;
        }

        return new LoanContractDetailDto(
            loanId,
            facilityName,
            borrower,
            status,
            effectiveDate,
            activationDate,
            closeDate,
            currentTermsVersion,
            currentTerms,
            termsVersions);
    }

    public async Task<IReadOnlyList<LoanTermsVersionDto>> LoadTermsVersionProjectionsAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await LoadTermsVersionProjectionsCoreAsync(connection, loanId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DrawdownLotDto>> LoadDrawdownLotProjectionsAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await LoadDrawdownLotsCoreAsync(connection, loanId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ServicingRevisionDto>> LoadServicingRevisionProjectionsAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await LoadServicingRevisionsCoreAsync(connection, loanId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DailyAccrualEntryDto>> LoadAccrualEntryProjectionsAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await LoadAccrualEntriesCoreAsync(connection, loanId, ct).ConfigureAwait(false);
    }

    public async Task<LoanServicingStateDto?> LoadServicingProjectionAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select status,
                   current_commitment,
                   total_drawn,
                   available_to_draw,
                   principal_outstanding,
                   interest_accrued_unpaid,
                   commitment_fee_accrued_unpaid,
                   fees_accrued_unpaid,
                   penalty_accrued_unpaid,
                   current_rate_reset_json::text,
                   last_accrual_date,
                   last_payment_date,
                   servicing_revision
            from {Qualified("loan_servicing_projection")}
            where loan_id = @loan_id;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var status = ParseEnum<LoanStatus>(reader.GetString(0));
        var currentCommitment = reader.GetDecimal(1);
        var totalDrawn = reader.GetDecimal(2);
        var availableToDraw = reader.GetDecimal(3);
        var balances = new OutstandingBalancesDto(
            reader.GetDecimal(4),
            reader.GetDecimal(5),
            reader.GetDecimal(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8));
        var currentRateReset = ParseNullableJson<RateResetDto>(reader.IsDBNull(9) ? null : reader.GetString(9));
        DateOnly? lastAccrualDate = reader.IsDBNull(10) ? null : DateOnly.FromDateTime(reader.GetDateTime(10));
        DateOnly? lastPaymentDate = reader.IsDBNull(11) ? null : DateOnly.FromDateTime(reader.GetDateTime(11));
        var servicingRevision = reader.GetInt64(12);
        await reader.CloseAsync().ConfigureAwait(false);

        var drawdownLots = await LoadDrawdownLotsCoreAsync(connection, loanId, ct).ConfigureAwait(false);
        var revisions = await LoadServicingRevisionsCoreAsync(connection, loanId, ct).ConfigureAwait(false);
        var accrualEntries = await LoadAccrualEntriesCoreAsync(connection, loanId, ct).ConfigureAwait(false);

        return new LoanServicingStateDto(
            loanId,
            status,
            currentCommitment,
            totalDrawn,
            availableToDraw,
            balances,
            drawdownLots,
            currentRateReset,
            lastAccrualDate,
            lastPaymentDate,
            servicingRevision,
            revisions,
            accrualEntries);
    }

    public async Task<PersistedDirectLendingState?> LoadAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select aggregate_version,
                   contract_json::text,
                   servicing_json::text
            from {Qualified("loan_state")}
            where loan_id = @loan_id;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var aggregateVersion = reader.GetInt64(0);
        var contract = JsonSerializer.Deserialize<LoanContractDetailDto>(reader.GetString(1), JsonOptions)
            ?? throw new InvalidOperationException($"Unable to deserialize direct-lending contract state for {loanId}.");
        var servicing = JsonSerializer.Deserialize<LoanServicingStateDto>(reader.GetString(2), JsonOptions)
            ?? throw new InvalidOperationException($"Unable to deserialize direct-lending servicing state for {loanId}.");

        return new PersistedDirectLendingState(loanId, aggregateVersion, contract, servicing);
    }

    public async Task<IReadOnlyList<LoanEventLineageDto>> GetHistoryAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select event_id,
                   aggregate_version,
                   event_type,
                   event_schema_version,
                   effective_date,
                   recorded_at,
                   payload::text,
                   causation_id,
                   correlation_id,
                   command_id,
                   source_system,
                   replay_flag
            from {Qualified("loan_event")}
            where loan_id = @loan_id
            order by aggregate_version;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        var results = new List<LoanEventLineageDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new LoanEventLineageDto(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4)),
                new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetBoolean(11)));
        }

        return results;
    }

    public async Task SaveStateAsync(
        Guid loanId,
        long aggregateVersion,
        LoanContractDetailDto contract,
        LoanServicingStateDto servicing,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
        await UpsertStateAsync(connection, transaction, loanId, aggregateVersion, contract, servicing, ct).ConfigureAwait(false);
        await UpsertNormalizedProjectionAsync(connection, transaction, loanId, aggregateVersion, contract, servicing, ct).ConfigureAwait(false);
        await MaybeInsertSnapshotAsync(connection, transaction, loanId, aggregateVersion, contract, servicing, forceSnapshot: true, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveAsync(
        Guid loanId,
        long expectedVersion,
        long nextVersion,
        LoanContractDetailDto contract,
        LoanServicingStateDto servicing,
        string eventType,
        int eventSchemaVersion,
        DateOnly? effectiveDate,
        JsonDocument payload,
        DirectLendingEventWriteMetadata metadata,
        DirectLendingPersistenceBatch? persistenceBatch,
        Guid eventId,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

        var currentVersion = await LoadCurrentVersionAsync(connection, transaction, loanId, ct).ConfigureAwait(false);
        if (currentVersion != expectedVersion)
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.ConcurrencyConflict,
                    $"Direct lending aggregate version conflict for {loanId}. Expected {expectedVersion}, actual {currentVersion}."));
        }

        await UpsertStateAsync(connection, transaction, loanId, nextVersion, contract, servicing, ct).ConfigureAwait(false);
        await UpsertNormalizedProjectionAsync(connection, transaction, loanId, nextVersion, contract, servicing, ct).ConfigureAwait(false);
        await AppendEventAsync(connection, transaction, loanId, nextVersion, eventType, eventSchemaVersion, effectiveDate, payload, metadata, eventId, ct).ConfigureAwait(false);
        await PersistBatchArtifactsAsync(connection, transaction, loanId, eventId, persistenceBatch, ct).ConfigureAwait(false);
        await MaybeInsertSnapshotAsync(connection, transaction, loanId, nextVersion, contract, servicing, forceSnapshot: false, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        long nextVersion,
        LoanContractDetailDto contract,
        LoanServicingStateDto servicing,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("loan_state")} (
                loan_id,
                aggregate_version,
                contract_json,
                servicing_json,
                updated_at)
            values (
                @loan_id,
                @aggregate_version,
                cast(@contract_json as jsonb),
                cast(@servicing_json as jsonb),
                now())
            on conflict (loan_id) do update
            set aggregate_version = excluded.aggregate_version,
                contract_json = excluded.contract_json,
                servicing_json = excluded.servicing_json,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);
        command.Parameters.AddWithValue("aggregate_version", nextVersion);
        command.Parameters.AddWithValue("contract_json", JsonSerializer.Serialize(contract, JsonOptions));
        command.Parameters.AddWithValue("servicing_json", JsonSerializer.Serialize(servicing, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<Guid> AppendEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        long nextVersion,
        string eventType,
        int eventSchemaVersion,
        DateOnly? effectiveDate,
        JsonDocument payload,
        DirectLendingEventWriteMetadata metadata,
        Guid eventId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("loan_event")} (
                event_id,
                loan_id,
                aggregate_version,
                event_type,
                event_schema_version,
                effective_date,
                recorded_at,
                payload,
                causation_id,
                correlation_id,
                command_id,
                source_system,
                replay_flag)
            values (
                @event_id,
                @loan_id,
                @aggregate_version,
                @event_type,
                @event_schema_version,
                @effective_date,
                @recorded_at,
                cast(@payload as jsonb),
                @causation_id,
                @correlation_id,
                @command_id,
                @source_system,
                @replay_flag);
            """;
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("loan_id", loanId);
        command.Parameters.AddWithValue("aggregate_version", nextVersion);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("event_schema_version", eventSchemaVersion);
        command.Parameters.AddWithValue("effective_date", (object?)effectiveDate ?? DBNull.Value);
        command.Parameters.AddWithValue("recorded_at", DateTimeOffset.UtcNow.UtcDateTime);
        command.Parameters.AddWithValue("payload", payload.RootElement.GetRawText());
        command.Parameters.AddWithValue("causation_id", (object?)metadata.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", (object?)metadata.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("command_id", (object?)metadata.CommandId ?? DBNull.Value);
        command.Parameters.AddWithValue("source_system", (object?)metadata.SourceSystem ?? DBNull.Value);
        command.Parameters.AddWithValue("replay_flag", metadata.ReplayFlag);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return eventId;
    }

    private async Task MaybeInsertSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        long aggregateVersion,
        LoanContractDetailDto contract,
        LoanServicingStateDto servicing,
        bool forceSnapshot,
        CancellationToken ct)
    {
        if (!forceSnapshot && !ShouldCreateSnapshot(aggregateVersion))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("loan_snapshot")} (
                loan_id,
                aggregate_version,
                contract_json,
                servicing_json,
                created_at)
            values (
                @loan_id,
                @aggregate_version,
                cast(@contract_json as jsonb),
                cast(@servicing_json as jsonb),
                now())
            on conflict (loan_id, aggregate_version) do nothing;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);
        command.Parameters.AddWithValue("aggregate_version", aggregateVersion);
        command.Parameters.AddWithValue("contract_json", JsonSerializer.Serialize(contract, JsonOptions));
        command.Parameters.AddWithValue("servicing_json", JsonSerializer.Serialize(servicing, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertNormalizedProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        long aggregateVersion,
        LoanContractDetailDto contract,
        LoanServicingStateDto servicing,
        CancellationToken ct)
    {
        await UpsertContractProjectionAsync(connection, transaction, loanId, aggregateVersion, contract, ct).ConfigureAwait(false);
        await ReplaceTermsVersionsAsync(connection, transaction, loanId, contract.TermsVersions, ct).ConfigureAwait(false);
        await UpsertServicingProjectionAsync(connection, transaction, loanId, aggregateVersion, servicing, ct).ConfigureAwait(false);
        await ReplaceDrawdownLotsAsync(connection, transaction, loanId, servicing.DrawdownLots, ct).ConfigureAwait(false);
        await ReplaceServicingRevisionsAsync(connection, transaction, loanId, servicing.RevisionHistory, ct).ConfigureAwait(false);
        await ReplaceAccrualEntriesAsync(connection, transaction, loanId, servicing.AccrualEntries, ct).ConfigureAwait(false);
    }

    private async Task UpsertContractProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        long aggregateVersion,
        LoanContractDetailDto contract,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("loan_contract")} (
                loan_id,
                aggregate_version,
                facility_name,
                borrower_id,
                borrower_name,
                legal_entity_id,
                status,
                effective_date,
                activation_date,
                close_date,
                current_terms_version,
                base_currency,
                updated_at)
            values (
                @loan_id,
                @aggregate_version,
                @facility_name,
                @borrower_id,
                @borrower_name,
                @legal_entity_id,
                @status,
                @effective_date,
                @activation_date,
                @close_date,
                @current_terms_version,
                @base_currency,
                now())
            on conflict (loan_id) do update
            set aggregate_version = excluded.aggregate_version,
                facility_name = excluded.facility_name,
                borrower_id = excluded.borrower_id,
                borrower_name = excluded.borrower_name,
                legal_entity_id = excluded.legal_entity_id,
                status = excluded.status,
                effective_date = excluded.effective_date,
                activation_date = excluded.activation_date,
                close_date = excluded.close_date,
                current_terms_version = excluded.current_terms_version,
                base_currency = excluded.base_currency,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);
        command.Parameters.AddWithValue("aggregate_version", aggregateVersion);
        command.Parameters.AddWithValue("facility_name", contract.FacilityName);
        command.Parameters.AddWithValue("borrower_id", contract.Borrower.BorrowerId);
        command.Parameters.AddWithValue("borrower_name", contract.Borrower.BorrowerName);
        command.Parameters.AddWithValue("legal_entity_id", (object?)contract.Borrower.LegalEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue("status", contract.Status.ToString());
        command.Parameters.AddWithValue("effective_date", contract.EffectiveDate);
        command.Parameters.AddWithValue("activation_date", (object?)contract.ActivationDate ?? DBNull.Value);
        command.Parameters.AddWithValue("close_date", (object?)contract.CloseDate ?? DBNull.Value);
        command.Parameters.AddWithValue("current_terms_version", contract.CurrentTermsVersion);
        command.Parameters.AddWithValue("base_currency", contract.CurrentTerms.BaseCurrency.ToString());
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task ReplaceTermsVersionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        IReadOnlyList<LoanTermsVersionDto> termsVersions,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified("loan_terms_version")} where loan_id = @loan_id;";
            delete.Parameters.AddWithValue("loan_id", loanId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var termsVersion in termsVersions)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("loan_terms_version")} (
                    loan_id,
                    terms_version,
                    terms_hash,
                    source_action,
                    amendment_reason,
                    recorded_at,
                    origination_date,
                    maturity_date,
                    commitment_amount,
                    base_currency,
                    rate_type_kind,
                    fixed_annual_rate,
                    interest_index_name,
                    spread_bps,
                    floor_rate,
                    cap_rate,
                    day_count_basis,
                    payment_frequency,
                    amortization_type,
                    commitment_fee_rate,
                    default_rate_spread_bps,
                    prepayment_allowed,
                    covenants_json)
                values (
                    @loan_id,
                    @terms_version,
                    @terms_hash,
                    @source_action,
                    @amendment_reason,
                    @recorded_at,
                    @origination_date,
                    @maturity_date,
                    @commitment_amount,
                    @base_currency,
                    @rate_type_kind,
                    @fixed_annual_rate,
                    @interest_index_name,
                    @spread_bps,
                    @floor_rate,
                    @cap_rate,
                    @day_count_basis,
                    @payment_frequency,
                    @amortization_type,
                    @commitment_fee_rate,
                    @default_rate_spread_bps,
                    @prepayment_allowed,
                    cast(@covenants_json as jsonb));
                """;
            insert.Parameters.AddWithValue("loan_id", loanId);
            insert.Parameters.AddWithValue("terms_version", termsVersion.VersionNumber);
            insert.Parameters.AddWithValue("terms_hash", termsVersion.TermsHash);
            insert.Parameters.AddWithValue("source_action", termsVersion.SourceAction);
            insert.Parameters.AddWithValue("amendment_reason", (object?)termsVersion.AmendmentReason ?? DBNull.Value);
            insert.Parameters.AddWithValue("recorded_at", termsVersion.RecordedAt.UtcDateTime);
            insert.Parameters.AddWithValue("origination_date", termsVersion.Terms.OriginationDate);
            insert.Parameters.AddWithValue("maturity_date", termsVersion.Terms.MaturityDate);
            insert.Parameters.AddWithValue("commitment_amount", termsVersion.Terms.CommitmentAmount);
            insert.Parameters.AddWithValue("base_currency", termsVersion.Terms.BaseCurrency.ToString());
            insert.Parameters.AddWithValue("rate_type_kind", termsVersion.Terms.RateTypeKind.ToString());
            insert.Parameters.AddWithValue("fixed_annual_rate", (object?)termsVersion.Terms.FixedAnnualRate ?? DBNull.Value);
            insert.Parameters.AddWithValue("interest_index_name", (object?)termsVersion.Terms.InterestIndexName ?? DBNull.Value);
            insert.Parameters.AddWithValue("spread_bps", (object?)termsVersion.Terms.SpreadBps ?? DBNull.Value);
            insert.Parameters.AddWithValue("floor_rate", (object?)termsVersion.Terms.FloorRate ?? DBNull.Value);
            insert.Parameters.AddWithValue("cap_rate", (object?)termsVersion.Terms.CapRate ?? DBNull.Value);
            insert.Parameters.AddWithValue("day_count_basis", termsVersion.Terms.DayCountBasis.ToString());
            insert.Parameters.AddWithValue("payment_frequency", termsVersion.Terms.PaymentFrequency.ToString());
            insert.Parameters.AddWithValue("amortization_type", termsVersion.Terms.AmortizationType.ToString());
            insert.Parameters.AddWithValue("commitment_fee_rate", (object?)termsVersion.Terms.CommitmentFeeRate ?? DBNull.Value);
            insert.Parameters.AddWithValue("default_rate_spread_bps", (object?)termsVersion.Terms.DefaultRateSpreadBps ?? DBNull.Value);
            insert.Parameters.AddWithValue("prepayment_allowed", termsVersion.Terms.PrepaymentAllowed);
            insert.Parameters.AddWithValue("covenants_json", (object?)termsVersion.Terms.CovenantsJson ?? "null");
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task UpsertServicingProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        long aggregateVersion,
        LoanServicingStateDto servicing,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("loan_servicing_projection")} (
                loan_id,
                aggregate_version,
                status,
                current_commitment,
                total_drawn,
                available_to_draw,
                principal_outstanding,
                interest_accrued_unpaid,
                commitment_fee_accrued_unpaid,
                fees_accrued_unpaid,
                penalty_accrued_unpaid,
                current_rate_reset_json,
                last_accrual_date,
                last_payment_date,
                servicing_revision,
                updated_at)
            values (
                @loan_id,
                @aggregate_version,
                @status,
                @current_commitment,
                @total_drawn,
                @available_to_draw,
                @principal_outstanding,
                @interest_accrued_unpaid,
                @commitment_fee_accrued_unpaid,
                @fees_accrued_unpaid,
                @penalty_accrued_unpaid,
                cast(@current_rate_reset_json as jsonb),
                @last_accrual_date,
                @last_payment_date,
                @servicing_revision,
                now())
            on conflict (loan_id) do update
            set aggregate_version = excluded.aggregate_version,
                status = excluded.status,
                current_commitment = excluded.current_commitment,
                total_drawn = excluded.total_drawn,
                available_to_draw = excluded.available_to_draw,
                principal_outstanding = excluded.principal_outstanding,
                interest_accrued_unpaid = excluded.interest_accrued_unpaid,
                commitment_fee_accrued_unpaid = excluded.commitment_fee_accrued_unpaid,
                fees_accrued_unpaid = excluded.fees_accrued_unpaid,
                penalty_accrued_unpaid = excluded.penalty_accrued_unpaid,
                current_rate_reset_json = excluded.current_rate_reset_json,
                last_accrual_date = excluded.last_accrual_date,
                last_payment_date = excluded.last_payment_date,
                servicing_revision = excluded.servicing_revision,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);
        command.Parameters.AddWithValue("aggregate_version", aggregateVersion);
        command.Parameters.AddWithValue("status", servicing.Status.ToString());
        command.Parameters.AddWithValue("current_commitment", servicing.CurrentCommitment);
        command.Parameters.AddWithValue("total_drawn", servicing.TotalDrawn);
        command.Parameters.AddWithValue("available_to_draw", servicing.AvailableToDraw);
        command.Parameters.AddWithValue("principal_outstanding", servicing.Balances.PrincipalOutstanding);
        command.Parameters.AddWithValue("interest_accrued_unpaid", servicing.Balances.InterestAccruedUnpaid);
        command.Parameters.AddWithValue("commitment_fee_accrued_unpaid", servicing.Balances.CommitmentFeeAccruedUnpaid);
        command.Parameters.AddWithValue("fees_accrued_unpaid", servicing.Balances.FeesAccruedUnpaid);
        command.Parameters.AddWithValue("penalty_accrued_unpaid", servicing.Balances.PenaltyAccruedUnpaid);
        command.Parameters.AddWithValue(
            "current_rate_reset_json",
            servicing.CurrentRateReset is null ? "null" : JsonSerializer.Serialize(servicing.CurrentRateReset, JsonOptions));
        command.Parameters.AddWithValue("last_accrual_date", (object?)servicing.LastAccrualDate ?? DBNull.Value);
        command.Parameters.AddWithValue("last_payment_date", (object?)servicing.LastPaymentDate ?? DBNull.Value);
        command.Parameters.AddWithValue("servicing_revision", servicing.ServicingRevision);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task ReplaceDrawdownLotsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        IReadOnlyList<DrawdownLotDto> drawdownLots,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified("drawdown_lot_projection")} where loan_id = @loan_id;";
            delete.Parameters.AddWithValue("loan_id", loanId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var lot in drawdownLots)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("drawdown_lot_projection")} (
                    lot_id,
                    loan_id,
                    drawdown_date,
                    settle_date,
                    original_principal,
                    remaining_principal,
                    external_ref)
                values (
                    @lot_id,
                    @loan_id,
                    @drawdown_date,
                    @settle_date,
                    @original_principal,
                    @remaining_principal,
                    @external_ref);
                """;
            insert.Parameters.AddWithValue("lot_id", lot.LotId);
            insert.Parameters.AddWithValue("loan_id", loanId);
            insert.Parameters.AddWithValue("drawdown_date", lot.DrawdownDate);
            insert.Parameters.AddWithValue("settle_date", lot.SettleDate);
            insert.Parameters.AddWithValue("original_principal", lot.OriginalPrincipal);
            insert.Parameters.AddWithValue("remaining_principal", lot.RemainingPrincipal);
            insert.Parameters.AddWithValue("external_ref", (object?)lot.ExternalRef ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ReplaceServicingRevisionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        IReadOnlyList<ServicingRevisionDto> revisions,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified("servicing_revision_projection")} where loan_id = @loan_id;";
            delete.Parameters.AddWithValue("loan_id", loanId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var revision in revisions)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("servicing_revision_projection")} (
                    loan_id,
                    revision_number,
                    revision_source_type,
                    effective_as_of_date,
                    created_at,
                    notes)
                values (
                    @loan_id,
                    @revision_number,
                    @revision_source_type,
                    @effective_as_of_date,
                    @created_at,
                    @notes);
                """;
            insert.Parameters.AddWithValue("loan_id", loanId);
            insert.Parameters.AddWithValue("revision_number", revision.RevisionNumber);
            insert.Parameters.AddWithValue("revision_source_type", revision.RevisionSourceType);
            insert.Parameters.AddWithValue("effective_as_of_date", revision.EffectiveAsOfDate);
            insert.Parameters.AddWithValue("created_at", revision.CreatedAt.UtcDateTime);
            insert.Parameters.AddWithValue("notes", revision.Notes);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ReplaceAccrualEntriesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        IReadOnlyList<DailyAccrualEntryDto> accrualEntries,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified("accrual_entry_projection")} where loan_id = @loan_id;";
            delete.Parameters.AddWithValue("loan_id", loanId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var entry in accrualEntries)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("accrual_entry_projection")} (
                    accrual_entry_id,
                    loan_id,
                    accrual_date,
                    interest_amount,
                    commitment_fee_amount,
                    penalty_amount,
                    annual_rate_applied,
                    recorded_at)
                values (
                    @accrual_entry_id,
                    @loan_id,
                    @accrual_date,
                    @interest_amount,
                    @commitment_fee_amount,
                    @penalty_amount,
                    @annual_rate_applied,
                    @recorded_at);
                """;
            insert.Parameters.AddWithValue("accrual_entry_id", entry.AccrualEntryId);
            insert.Parameters.AddWithValue("loan_id", loanId);
            insert.Parameters.AddWithValue("accrual_date", entry.AccrualDate);
            insert.Parameters.AddWithValue("interest_amount", entry.InterestAmount);
            insert.Parameters.AddWithValue("commitment_fee_amount", entry.CommitmentFeeAmount);
            insert.Parameters.AddWithValue("penalty_amount", entry.PenaltyAmount);
            insert.Parameters.AddWithValue("annual_rate_applied", entry.AnnualRateApplied);
            insert.Parameters.AddWithValue("recorded_at", entry.RecordedAt.UtcDateTime);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task PersistBatchArtifactsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        Guid sourceEventId,
        DirectLendingPersistenceBatch? batch,
        CancellationToken ct)
    {
        if (batch is null)
        {
            return;
        }

        if (batch.CashTransactions is not null)
        {
            foreach (var row in batch.CashTransactions)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    $"""
                    insert into {Qualified("cash_transaction")} (
                        cash_transaction_id,
                        loan_id,
                        transaction_type,
                        effective_date,
                        transaction_date,
                        settlement_date,
                        amount,
                        currency,
                        counterparty,
                        external_ref,
                        source_event_id,
                        recorded_at)
                    values (
                        @cash_transaction_id,
                        @loan_id,
                        @transaction_type,
                        @effective_date,
                        @transaction_date,
                        @settlement_date,
                        @amount,
                        @currency,
                        @counterparty,
                        @external_ref,
                        @source_event_id,
                        now())
                    on conflict do nothing;
                    """;
                insert.Parameters.AddWithValue("cash_transaction_id", row.CashTransactionId);
                insert.Parameters.AddWithValue("loan_id", loanId);
                insert.Parameters.AddWithValue("transaction_type", row.TransactionType);
                insert.Parameters.AddWithValue("effective_date", row.EffectiveDate);
                insert.Parameters.AddWithValue("transaction_date", row.TransactionDate);
                insert.Parameters.AddWithValue("settlement_date", row.SettlementDate);
                insert.Parameters.AddWithValue("amount", row.Amount);
                insert.Parameters.AddWithValue("currency", row.Currency);
                insert.Parameters.AddWithValue("counterparty", (object?)row.Counterparty ?? DBNull.Value);
                insert.Parameters.AddWithValue("external_ref", (object?)row.ExternalRef ?? DBNull.Value);
                insert.Parameters.AddWithValue("source_event_id", sourceEventId);
                await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        if (batch.PaymentAllocations is not null)
        {
            foreach (var row in batch.PaymentAllocations)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    $"""
                    insert into {Qualified("payment_allocation")} (
                        allocation_id,
                        loan_id,
                        cash_transaction_id,
                        allocation_seq_no,
                        target_type,
                        target_id,
                        allocated_amount,
                        allocation_rule,
                        source_event_id,
                        created_at)
                    values (
                        @allocation_id,
                        @loan_id,
                        @cash_transaction_id,
                        @allocation_seq_no,
                        @target_type,
                        @target_id,
                        @allocated_amount,
                        @allocation_rule,
                        @source_event_id,
                        now());
                    """;
                insert.Parameters.AddWithValue("allocation_id", Guid.NewGuid());
                insert.Parameters.AddWithValue("loan_id", loanId);
                insert.Parameters.AddWithValue("cash_transaction_id", row.CashTransactionId);
                insert.Parameters.AddWithValue("allocation_seq_no", row.AllocationSequenceNumber);
                insert.Parameters.AddWithValue("target_type", row.TargetType);
                insert.Parameters.AddWithValue("target_id", row.TargetId);
                insert.Parameters.AddWithValue("allocated_amount", row.AllocatedAmount);
                insert.Parameters.AddWithValue("allocation_rule", row.AllocationRule);
                insert.Parameters.AddWithValue("source_event_id", sourceEventId);
                await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        if (batch.FeeBalances is not null)
        {
            foreach (var row in batch.FeeBalances)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    $"""
                    insert into {Qualified("fee_balance")} (
                        fee_balance_id,
                        loan_id,
                        fee_type,
                        effective_date,
                        original_amount,
                        unpaid_amount,
                        source_event_id,
                        note,
                        created_at)
                    values (
                        @fee_balance_id,
                        @loan_id,
                        @fee_type,
                        @effective_date,
                        @original_amount,
                        @unpaid_amount,
                        @source_event_id,
                        @note,
                        now());
                    """;
                insert.Parameters.AddWithValue("fee_balance_id", row.FeeBalanceId);
                insert.Parameters.AddWithValue("loan_id", loanId);
                insert.Parameters.AddWithValue("fee_type", row.FeeType);
                insert.Parameters.AddWithValue("effective_date", row.EffectiveDate);
                insert.Parameters.AddWithValue("original_amount", row.OriginalAmount);
                insert.Parameters.AddWithValue("unpaid_amount", row.UnpaidAmount);
                insert.Parameters.AddWithValue("source_event_id", sourceEventId);
                insert.Parameters.AddWithValue("note", (object?)row.Note ?? DBNull.Value);
                await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        if (batch.OutboxMessages is not null)
        {
            foreach (var row in batch.OutboxMessages)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    $"""
                    insert into {Qualified("outbox_message")} (
                        outbox_message_id,
                        topic,
                        message_key,
                        payload,
                        headers,
                        occurred_at,
                        visible_after,
                        processed_at,
                        error_count,
                        last_error)
                    values (
                        @outbox_message_id,
                        @topic,
                        @message_key,
                        cast(@payload as jsonb),
                        cast(@headers as jsonb),
                        @occurred_at,
                        @visible_after,
                        null,
                        0,
                        null);
                    """;
                insert.Parameters.AddWithValue("outbox_message_id", Guid.NewGuid());
                insert.Parameters.AddWithValue("topic", row.Topic);
                insert.Parameters.AddWithValue("message_key", row.MessageKey);
                insert.Parameters.AddWithValue("payload", row.PayloadJson);
                insert.Parameters.AddWithValue("headers", row.HeadersJson ?? "null");
                insert.Parameters.AddWithValue("occurred_at", row.OccurredAt.UtcDateTime);
                insert.Parameters.AddWithValue("visible_after", (row.VisibleAfter ?? row.OccurredAt).UtcDateTime);
                await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<IReadOnlyList<LoanTermsVersionDto>> LoadTermsVersionProjectionsCoreAsync(
        NpgsqlConnection connection,
        Guid loanId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select terms_version,
                   terms_hash,
                   source_action,
                   amendment_reason,
                   recorded_at,
                   origination_date,
                   maturity_date,
                   commitment_amount,
                   base_currency,
                   rate_type_kind,
                   fixed_annual_rate,
                   interest_index_name,
                   spread_bps,
                   floor_rate,
                   cap_rate,
                   day_count_basis,
                   payment_frequency,
                   amortization_type,
                   commitment_fee_rate,
                   default_rate_spread_bps,
                   prepayment_allowed,
                   covenants_json::text
            from {Qualified("loan_terms_version")}
            where loan_id = @loan_id
            order by terms_version desc;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        var results = new List<LoanTermsVersionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var terms = new DirectLendingTermsDto(
                DateOnly.FromDateTime(reader.GetDateTime(5)),
                DateOnly.FromDateTime(reader.GetDateTime(6)),
                reader.GetDecimal(7),
                ParseEnum<CurrencyCode>(reader.GetString(8)),
                ParseEnum<RateTypeKind>(reader.GetString(9)),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                reader.IsDBNull(13) ? null : reader.GetDecimal(13),
                reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                ParseEnum<DayCountBasis>(reader.GetString(15)),
                ParseEnum<PaymentFrequency>(reader.GetString(16)),
                ParseEnum<AmortizationType>(reader.GetString(17)),
                reader.IsDBNull(18) ? null : reader.GetDecimal(18),
                reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                reader.GetBoolean(20),
                NormalizeJsonText(reader.IsDBNull(21) ? null : reader.GetString(21)));

            results.Add(new LoanTermsVersionDto(
                reader.GetInt32(0),
                reader.GetString(1),
                terms,
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero)));
        }

        return results;
    }

    private async Task<IReadOnlyList<DrawdownLotDto>> LoadDrawdownLotsCoreAsync(
        NpgsqlConnection connection,
        Guid loanId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select lot_id,
                   drawdown_date,
                   settle_date,
                   original_principal,
                   remaining_principal,
                   external_ref
            from {Qualified("drawdown_lot_projection")}
            where loan_id = @loan_id
            order by drawdown_date, lot_id;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        var results = new List<DrawdownLotDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new DrawdownLotDto(
                reader.GetGuid(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return results;
    }

    private async Task<IReadOnlyList<ServicingRevisionDto>> LoadServicingRevisionsCoreAsync(
        NpgsqlConnection connection,
        Guid loanId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select revision_number,
                   revision_source_type,
                   effective_as_of_date,
                   created_at,
                   notes
            from {Qualified("servicing_revision_projection")}
            where loan_id = @loan_id
            order by revision_number desc;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        var results = new List<ServicingRevisionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new ServicingRevisionDto(
                reader.GetInt64(0),
                reader.GetString(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero),
                reader.GetString(4)));
        }

        return results;
    }

    private async Task<IReadOnlyList<DailyAccrualEntryDto>> LoadAccrualEntriesCoreAsync(
        NpgsqlConnection connection,
        Guid loanId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select accrual_entry_id,
                   accrual_date,
                   interest_amount,
                   commitment_fee_amount,
                   penalty_amount,
                   annual_rate_applied,
                   recorded_at
            from {Qualified("accrual_entry_projection")}
            where loan_id = @loan_id
            order by accrual_date desc, accrual_entry_id;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        var results = new List<DailyAccrualEntryDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new DailyAccrualEntryDto(
                reader.GetGuid(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                new DateTimeOffset(reader.GetDateTime(6), TimeSpan.Zero)));
        }

        return results;
    }

    private async Task<long> LoadCurrentVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loanId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select coalesce((
                select aggregate_version
                from {Qualified("loan_state")}
                where loan_id = @loan_id
                for update), 0);
            """;
        command.Parameters.AddWithValue("loan_id", loanId);
        return (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("DirectLendingOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table) => $"{_options.Schema}.{table}";

    private bool ShouldCreateSnapshot(long aggregateVersion)
        => aggregateVersion == 1
            || (_options.SnapshotIntervalVersions > 0 && aggregateVersion % _options.SnapshotIntervalVersions == 0);

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum
        => Enum.Parse<TEnum>(value, ignoreCase: true);

    private static string? NormalizeJsonText(string? value)
        => string.Equals(value, "null", StringComparison.OrdinalIgnoreCase) ? null : value;

    private static T? ParseNullableJson<T>(string? json)
    {
        var normalized = NormalizeJsonText(json);
        return normalized is null
            ? default
            : JsonSerializer.Deserialize<T>(normalized, JsonOptions);
    }
}
