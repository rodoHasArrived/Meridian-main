using System.Text;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.FundAccounts;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IFundAccountStore"/>.
/// Uses raw Npgsql for all persistence; complex nested objects are stored as JSONB.
/// </summary>
public sealed class PostgresFundAccountStore : IFundAccountStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly FundAccountStoreOptions _options;

    // SEC-005 slice 4c: ambient caller-tenant resolver. Optional so existing construction (tests,
    // non-web hosts) keeps compiling and stays fail-open; the workstation host injects an
    // IHttpContextAccessor-backed implementation. Null (no tenant in scope) => reads are not scoped and
    // writes stamp no tenant. Trust-on-first-use: fund accounts have no in-DB registry linkage in this
    // separate database, so the owning tenant is the operator who creates the account.
    private readonly IFundScopeTenantAccessor? _tenantAccessor;

    public PostgresFundAccountStore(FundAccountStoreOptions options, IFundScopeTenantAccessor? tenantAccessor = null)
    {
        _options = options;
        _tenantAccessor = tenantAccessor;
    }

    // SEC-005 slice 4c: the caller's tenant for the current ambient scope, or null (fail-open).
    private string? ResolveCallerTenant() => _tenantAccessor?.ResolveCallerTenant();

    private string Qualified(string table) => $"{_options.Schema}.{table}";

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    // ── Account definition ────────────────────────────────────────────────────

    public async Task UpsertAccountAsync(AccountSummaryDto account, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {Qualified("account_definition")}
                (account_id, account_type, entity_id, fund_id, sleeve_id, vehicle_id,
                 account_code, display_name, base_currency, institution, is_active,
                 effective_from, effective_to, portfolio_id, ledger_reference,
                 strategy_id, run_id, operational_status, custodian_details, bank_details, tenant_id, updated_at)
            VALUES
                (@account_id, @account_type, @entity_id, @fund_id, @sleeve_id, @vehicle_id,
                 @account_code, @display_name, @base_currency, @institution, @is_active,
                 @effective_from, @effective_to, @portfolio_id, @ledger_reference,
                 @strategy_id, @run_id, @operational_status, @custodian_details::jsonb, @bank_details::jsonb, @tenant_id, now())
            ON CONFLICT (account_id) DO UPDATE SET
                account_type        = EXCLUDED.account_type,
                entity_id           = EXCLUDED.entity_id,
                fund_id             = EXCLUDED.fund_id,
                sleeve_id           = EXCLUDED.sleeve_id,
                vehicle_id          = EXCLUDED.vehicle_id,
                account_code        = EXCLUDED.account_code,
                display_name        = EXCLUDED.display_name,
                base_currency       = EXCLUDED.base_currency,
                institution         = EXCLUDED.institution,
                is_active           = EXCLUDED.is_active,
                effective_from      = EXCLUDED.effective_from,
                effective_to        = EXCLUDED.effective_to,
                portfolio_id        = EXCLUDED.portfolio_id,
                ledger_reference    = EXCLUDED.ledger_reference,
                strategy_id         = EXCLUDED.strategy_id,
                run_id              = EXCLUDED.run_id,
                operational_status  = EXCLUDED.operational_status,
                custodian_details   = EXCLUDED.custodian_details,
                bank_details        = EXCLUDED.bank_details,
                -- SEC-005 slice 4c: first-owner-wins — keep an already-stamped tenant, fill a null from the
                -- writing caller's tenant, so an account first written without a tenant in scope is stamped
                -- on a later write by a tenant-scoped operator.
                tenant_id           = coalesce(account_definition.tenant_id, EXCLUDED.tenant_id),
                updated_at          = now()
            -- SEC-005 slice 4c: refuse a cross-tenant overwrite. The read predicate hides a foreign account
            -- from GetAccount, but without this guard a tenant-scoped caller who knows a foreign account UUID
            -- could still clobber that row's fields via the conflict path (tenant_id itself is first-owner
            -- preserved, but the other columns are not). Only update when the existing row is unbound, the
            -- caller is tenantless (single-company fail-open), or the tenants match; otherwise 0 rows change
            -- and the caller gets a conflict error below. Behavior-preserving under one-company-per-deployment.
            WHERE account_definition.tenant_id IS NULL
               OR @tenant_id IS NULL
               OR lower(trim(account_definition.tenant_id)) = lower(trim(@tenant_id))
            """;
        cmd.Parameters.AddWithValue("account_id", account.AccountId);
        cmd.Parameters.AddWithValue("account_type", account.AccountType.ToString());
        cmd.Parameters.AddWithValue("entity_id", account.EntityId.HasValue ? (object)account.EntityId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("fund_id", account.FundId.HasValue ? (object)account.FundId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("sleeve_id", account.SleeveId.HasValue ? (object)account.SleeveId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("vehicle_id", account.VehicleId.HasValue ? (object)account.VehicleId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("account_code", account.AccountCode);
        cmd.Parameters.AddWithValue("display_name", account.DisplayName);
        cmd.Parameters.AddWithValue("base_currency", account.BaseCurrency);
        cmd.Parameters.AddWithValue("institution", (object?)account.Institution ?? DBNull.Value);
        cmd.Parameters.AddWithValue("is_active", account.IsActive);
        cmd.Parameters.AddWithValue("effective_from", account.EffectiveFrom);
        cmd.Parameters.AddWithValue("effective_to", account.EffectiveTo.HasValue ? (object)account.EffectiveTo.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("portfolio_id", (object?)account.PortfolioId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ledger_reference", (object?)account.LedgerReference ?? DBNull.Value);
        cmd.Parameters.AddWithValue("strategy_id", (object?)account.StrategyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("run_id", (object?)account.RunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("operational_status", account.OperationalStatus.ToString());
        cmd.Parameters.AddWithValue("custodian_details",
            account.CustodianDetails is not null ? JsonSerializer.Serialize(account.CustodianDetails, JsonOpts) : DBNull.Value);
        cmd.Parameters.AddWithValue("bank_details",
            account.BankDetails is not null ? JsonSerializer.Serialize(account.BankDetails, JsonOpts) : DBNull.Value);
        // SEC-005 slice 4c: trust-on-first-use tenant stamp from the writing operator (null => fail-open).
        var callerTenantStamp = ResolveCallerTenant();
        cmd.Parameters.AddWithValue(
            "tenant_id",
            string.IsNullOrWhiteSpace(callerTenantStamp) ? DBNull.Value : callerTenantStamp.Trim());

        var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (affected == 0)
        {
            // The row exists but the conflict update was blocked by the tenant guard above: a tenant-scoped
            // caller attempted to modify an account owned by a different tenant. Fail instead of silently
            // succeeding. Unreachable under one-company-per-deployment (the guard always matches there).
            throw new InvalidOperationException(
                $"Fund account '{account.AccountId}' is owned by a different tenant and cannot be modified.");
        }
    }

    public async Task<AccountSummaryDto?> GetAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT account_id, account_type, entity_id, fund_id, sleeve_id, vehicle_id,
                   account_code, display_name, base_currency, institution, is_active,
                   effective_from, effective_to, portfolio_id, ledger_reference,
                   strategy_id, run_id, operational_status, custodian_details, bank_details
            FROM {Qualified("account_definition")}
            WHERE account_id = @account_id
            """;
        cmd.Parameters.AddWithValue("account_id", accountId);
        // SEC-005 slice 4c: scope by the account's stamped tenant_id so a foreign account GUID resolves to
        // not-found. Fail-open for a tenantless caller or an unstamped (legacy) account.
        var callerTenant = ResolveCallerTenant();
        if (TenantReadPredicate.ShouldFilter(callerTenant))
        {
            cmd.CommandText += TenantReadPredicate.FilterClause("tenant_id");
            cmd.Parameters.AddWithValue(
                TenantReadPredicate.ParameterName,
                TenantReadPredicate.NormalizeParameter(callerTenant!));
        }
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return ReadAccountSummary(reader);
    }

    public async Task<IReadOnlyList<AccountSummaryDto>> QueryAccountsAsync(AccountStructureQuery query, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        var sb = new StringBuilder();
        sb.AppendLine($"""
            SELECT account_id, account_type, entity_id, fund_id, sleeve_id, vehicle_id,
                   account_code, display_name, base_currency, institution, is_active,
                   effective_from, effective_to, portfolio_id, ledger_reference,
                   strategy_id, run_id, operational_status, custodian_details, bank_details
            FROM {Qualified("account_definition")}
            WHERE 1=1
            """);

        if (query.ActiveOnly)
        {
            sb.AppendLine("  AND is_active = true");
        }
        if (query.AccountId.HasValue)
        {
            sb.AppendLine("  AND account_id = @account_id");
            cmd.Parameters.AddWithValue("account_id", query.AccountId.Value);
        }
        if (query.EntityId.HasValue)
        {
            sb.AppendLine("  AND entity_id = @entity_id");
            cmd.Parameters.AddWithValue("entity_id", query.EntityId.Value);
        }
        if (query.FundId.HasValue)
        {
            sb.AppendLine("  AND fund_id = @fund_id");
            cmd.Parameters.AddWithValue("fund_id", query.FundId.Value);
        }
        if (query.SleeveId.HasValue)
        {
            sb.AppendLine("  AND sleeve_id = @sleeve_id");
            cmd.Parameters.AddWithValue("sleeve_id", query.SleeveId.Value);
        }
        if (query.VehicleId.HasValue)
        {
            sb.AppendLine("  AND vehicle_id = @vehicle_id");
            cmd.Parameters.AddWithValue("vehicle_id", query.VehicleId.Value);
        }
        if (query.PortfolioId is not null)
        {
            sb.AppendLine("  AND portfolio_id = @portfolio_id");
            cmd.Parameters.AddWithValue("portfolio_id", query.PortfolioId);
        }
        if (query.LedgerReference is not null)
        {
            sb.AppendLine("  AND ledger_reference = @ledger_reference");
            cmd.Parameters.AddWithValue("ledger_reference", query.LedgerReference);
        }
        if (query.StrategyId is not null)
        {
            sb.AppendLine("  AND strategy_id = @strategy_id");
            cmd.Parameters.AddWithValue("strategy_id", query.StrategyId);
        }
        if (query.RunId is not null)
        {
            sb.AppendLine("  AND run_id = @run_id");
            cmd.Parameters.AddWithValue("run_id", query.RunId);
        }

        // SEC-005 slice 4c: scope by the account's stamped tenant_id (closes the fund_account_id
        // alternate-identifier residual). Fail-open for a tenantless caller or unstamped legacy rows.
        var callerTenant = ResolveCallerTenant();
        if (TenantReadPredicate.ShouldFilter(callerTenant))
        {
            sb.AppendLine(TenantReadPredicate.FilterClause("tenant_id"));
            cmd.Parameters.AddWithValue(
                TenantReadPredicate.ParameterName,
                TenantReadPredicate.NormalizeParameter(callerTenant!));
        }

        cmd.CommandText = sb.ToString();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<AccountSummaryDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(ReadAccountSummary(reader));
        }
        return results;
    }

    private static AccountSummaryDto ReadAccountSummary(NpgsqlDataReader reader)
    {
        var custodianJson = reader.IsDBNull(reader.GetOrdinal("custodian_details")) ? null : reader.GetString(reader.GetOrdinal("custodian_details"));
        var bankJson = reader.IsDBNull(reader.GetOrdinal("bank_details")) ? null : reader.GetString(reader.GetOrdinal("bank_details"));

        return new AccountSummaryDto(
            AccountId: reader.GetGuid(reader.GetOrdinal("account_id")),
            AccountType: Enum.Parse<AccountTypeDto>(reader.GetString(reader.GetOrdinal("account_type"))),
            EntityId: reader.IsDBNull(reader.GetOrdinal("entity_id")) ? null : reader.GetGuid(reader.GetOrdinal("entity_id")),
            FundId: reader.IsDBNull(reader.GetOrdinal("fund_id")) ? null : reader.GetGuid(reader.GetOrdinal("fund_id")),
            SleeveId: reader.IsDBNull(reader.GetOrdinal("sleeve_id")) ? null : reader.GetGuid(reader.GetOrdinal("sleeve_id")),
            VehicleId: reader.IsDBNull(reader.GetOrdinal("vehicle_id")) ? null : reader.GetGuid(reader.GetOrdinal("vehicle_id")),
            AccountCode: reader.GetString(reader.GetOrdinal("account_code")),
            DisplayName: reader.GetString(reader.GetOrdinal("display_name")),
            BaseCurrency: reader.GetString(reader.GetOrdinal("base_currency")),
            Institution: reader.IsDBNull(reader.GetOrdinal("institution")) ? null : reader.GetString(reader.GetOrdinal("institution")),
            IsActive: reader.GetBoolean(reader.GetOrdinal("is_active")),
            EffectiveFrom: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("effective_from")),
            EffectiveTo: reader.IsDBNull(reader.GetOrdinal("effective_to")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("effective_to")),
            PortfolioId: reader.IsDBNull(reader.GetOrdinal("portfolio_id")) ? null : reader.GetString(reader.GetOrdinal("portfolio_id")),
            LedgerReference: reader.IsDBNull(reader.GetOrdinal("ledger_reference")) ? null : reader.GetString(reader.GetOrdinal("ledger_reference")),
            StrategyId: reader.IsDBNull(reader.GetOrdinal("strategy_id")) ? null : reader.GetString(reader.GetOrdinal("strategy_id")),
            RunId: reader.IsDBNull(reader.GetOrdinal("run_id")) ? null : reader.GetString(reader.GetOrdinal("run_id")),
            OperationalStatus: Enum.Parse<AccountOperationalStatusDto>(reader.GetString(reader.GetOrdinal("operational_status"))),
            CustodianDetails: custodianJson is not null ? JsonSerializer.Deserialize<CustodianAccountDetailsDto>(custodianJson, JsonOpts) : null,
            BankDetails: bankJson is not null ? JsonSerializer.Deserialize<BankAccountDetailsDto>(bankJson, JsonOpts) : null);
    }

    // ── Balance snapshots ─────────────────────────────────────────────────────

    public async Task InsertBalanceSnapshotAsync(AccountBalanceSnapshotDto snapshot, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {Qualified("account_balance_snapshot")}
                (snapshot_id, account_id, fund_id, as_of_date, currency,
                 cash_balance, securities_market_value, accrued_interest,
                 pending_settlement, source, recorded_at, external_reference,
                 unrealized_pnl, realized_pnl)
            VALUES
                (@snapshot_id, @account_id, @fund_id, @as_of_date, @currency,
                 @cash_balance, @securities_market_value, @accrued_interest,
                 @pending_settlement, @source, @recorded_at, @external_reference,
                 @unrealized_pnl, @realized_pnl)
            ON CONFLICT (snapshot_id) DO NOTHING
            """;
        cmd.Parameters.AddWithValue("snapshot_id", snapshot.SnapshotId);
        cmd.Parameters.AddWithValue("account_id", snapshot.AccountId);
        cmd.Parameters.AddWithValue("fund_id", snapshot.FundId.HasValue ? (object)snapshot.FundId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("as_of_date", snapshot.AsOfDate);
        cmd.Parameters.AddWithValue("currency", snapshot.Currency);
        cmd.Parameters.AddWithValue("cash_balance", snapshot.CashBalance);
        cmd.Parameters.AddWithValue("securities_market_value", snapshot.SecuritiesMarketValue.HasValue ? (object)snapshot.SecuritiesMarketValue.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("accrued_interest", snapshot.AccruedInterest.HasValue ? (object)snapshot.AccruedInterest.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("pending_settlement", snapshot.PendingSettlement.HasValue ? (object)snapshot.PendingSettlement.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("source", snapshot.Source);
        cmd.Parameters.AddWithValue("recorded_at", snapshot.RecordedAt);
        cmd.Parameters.AddWithValue("external_reference", (object?)snapshot.ExternalReference ?? DBNull.Value);
        cmd.Parameters.AddWithValue("unrealized_pnl", snapshot.UnrealizedPnl.HasValue ? (object)snapshot.UnrealizedPnl.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("realized_pnl", snapshot.RealizedPnl.HasValue ? (object)snapshot.RealizedPnl.Value : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountBalanceSnapshotDto>> GetBalanceHistoryAsync(
        Guid accountId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        var sb = new StringBuilder();
        sb.AppendLine($"""
            SELECT snapshot_id, account_id, fund_id, as_of_date, currency,
                   cash_balance, securities_market_value, accrued_interest,
                   pending_settlement, source, recorded_at, external_reference,
                   unrealized_pnl, realized_pnl
            FROM {Qualified("account_balance_snapshot")}
            WHERE account_id = @account_id
            """);
        cmd.Parameters.AddWithValue("account_id", accountId);
        if (fromDate.HasValue)
        {
            sb.AppendLine("  AND as_of_date >= @from_date");
            cmd.Parameters.AddWithValue("from_date", fromDate.Value);
        }
        if (toDate.HasValue)
        {
            sb.AppendLine("  AND as_of_date <= @to_date");
            cmd.Parameters.AddWithValue("to_date", toDate.Value);
        }
        sb.AppendLine("  ORDER BY as_of_date DESC, recorded_at DESC");
        cmd.CommandText = sb.ToString();

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<AccountBalanceSnapshotDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new AccountBalanceSnapshotDto(
                SnapshotId: reader.GetGuid(reader.GetOrdinal("snapshot_id")),
                AccountId: reader.GetGuid(reader.GetOrdinal("account_id")),
                FundId: reader.IsDBNull(reader.GetOrdinal("fund_id")) ? null : reader.GetGuid(reader.GetOrdinal("fund_id")),
                AsOfDate: reader.GetFieldValue<DateOnly>(reader.GetOrdinal("as_of_date")),
                Currency: reader.GetString(reader.GetOrdinal("currency")),
                CashBalance: reader.GetDecimal(reader.GetOrdinal("cash_balance")),
                SecuritiesMarketValue: reader.IsDBNull(reader.GetOrdinal("securities_market_value")) ? null : reader.GetDecimal(reader.GetOrdinal("securities_market_value")),
                AccruedInterest: reader.IsDBNull(reader.GetOrdinal("accrued_interest")) ? null : reader.GetDecimal(reader.GetOrdinal("accrued_interest")),
                PendingSettlement: reader.IsDBNull(reader.GetOrdinal("pending_settlement")) ? null : reader.GetDecimal(reader.GetOrdinal("pending_settlement")),
                Source: reader.GetString(reader.GetOrdinal("source")),
                RecordedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("recorded_at")),
                ExternalReference: reader.IsDBNull(reader.GetOrdinal("external_reference")) ? null : reader.GetString(reader.GetOrdinal("external_reference")),
                UnrealizedPnl: reader.IsDBNull(reader.GetOrdinal("unrealized_pnl")) ? null : reader.GetDecimal(reader.GetOrdinal("unrealized_pnl")),
                RealizedPnl: reader.IsDBNull(reader.GetOrdinal("realized_pnl")) ? null : reader.GetDecimal(reader.GetOrdinal("realized_pnl"))));
        }
        return results;
    }

    // ── Statement ingestion ───────────────────────────────────────────────────

    public async Task InsertCustodianStatementBatchAsync(
        CustodianStatementBatchDto batch,
        IReadOnlyList<CustodianPositionLineDto> lines,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"""
                INSERT INTO {Qualified("custodian_statement_batch")}
                    (batch_id, account_id, as_of_date, custodian_name, source_format, file_name, line_count, ingested_at, loaded_by)
                VALUES
                    (@batch_id, @account_id, @as_of_date, @custodian_name, @source_format, null, @line_count, @ingested_at, @loaded_by)
                ON CONFLICT (batch_id) DO NOTHING
                """;
            cmd.Parameters.AddWithValue("batch_id", batch.BatchId);
            cmd.Parameters.AddWithValue("account_id", batch.AccountId);
            cmd.Parameters.AddWithValue("as_of_date", batch.AsOfDate);
            cmd.Parameters.AddWithValue("custodian_name", batch.CustodianName);
            cmd.Parameters.AddWithValue("source_format", batch.SourceFormat);
            cmd.Parameters.AddWithValue("line_count", batch.LineCount);
            cmd.Parameters.AddWithValue("ingested_at", batch.IngestedAt);
            cmd.Parameters.AddWithValue("loaded_by", batch.LoadedBy);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var line in lines)
        {
            var rawPayload = JsonSerializer.Serialize(new
            {
                securityName = line.SecurityName,
                assetClass = line.AssetClass,
                isShort = line.IsShort
            }, JsonOpts);

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"""
                INSERT INTO {Qualified("custodian_position_line")}
                    (position_line_id, batch_id, account_id, as_of_date,
                     security_identifier, identifier_type, quantity, market_value,
                     market_value_currency, cost_basis, accrued_income, settlement_pending, raw_payload)
                VALUES
                    (@position_line_id, @batch_id, @account_id, @as_of_date,
                     @security_identifier, @identifier_type, @quantity, @market_value,
                     @market_value_currency, null, null, false, @raw_payload::jsonb)
                ON CONFLICT (position_line_id) DO NOTHING
                """;
            cmd.Parameters.AddWithValue("position_line_id", line.LineId);
            cmd.Parameters.AddWithValue("batch_id", line.BatchId);
            cmd.Parameters.AddWithValue("account_id", line.AccountId);
            cmd.Parameters.AddWithValue("as_of_date", line.AsOfDate);
            cmd.Parameters.AddWithValue("security_identifier", line.Identifier);
            cmd.Parameters.AddWithValue("identifier_type", line.IdentifierType);
            cmd.Parameters.AddWithValue("quantity", line.Quantity);
            cmd.Parameters.AddWithValue("market_value", line.MarketValue);
            cmd.Parameters.AddWithValue("market_value_currency", line.Currency);
            cmd.Parameters.AddWithValue("raw_payload", rawPayload);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertBankStatementBatchAsync(
        BankStatementBatchDto batch,
        IReadOnlyList<BankStatementLineDto> lines,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"""
                INSERT INTO {Qualified("bank_statement_batch")}
                    (batch_id, account_id, statement_date, bank_name, file_name, line_count, ingested_at, loaded_by)
                VALUES
                    (@batch_id, @account_id, @statement_date, @bank_name, null, @line_count, @ingested_at, @loaded_by)
                ON CONFLICT (batch_id) DO NOTHING
                """;
            cmd.Parameters.AddWithValue("batch_id", batch.BatchId);
            cmd.Parameters.AddWithValue("account_id", batch.AccountId);
            cmd.Parameters.AddWithValue("statement_date", batch.StatementDate);
            cmd.Parameters.AddWithValue("bank_name", batch.BankName);
            cmd.Parameters.AddWithValue("line_count", batch.LineCount);
            cmd.Parameters.AddWithValue("ingested_at", batch.IngestedAt);
            cmd.Parameters.AddWithValue("loaded_by", batch.LoadedBy);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var line in lines)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"""
                INSERT INTO {Qualified("bank_statement_line")}
                    (statement_line_id, batch_id, account_id, statement_date, value_date,
                     amount, currency, transaction_type, description, external_reference, running_balance)
                VALUES
                    (@statement_line_id, @batch_id, @account_id, @statement_date, @value_date,
                     @amount, @currency, @transaction_type, @description, @external_reference, @running_balance)
                ON CONFLICT (statement_line_id) DO NOTHING
                """;
            cmd.Parameters.AddWithValue("statement_line_id", line.LineId);
            cmd.Parameters.AddWithValue("batch_id", line.BatchId);
            cmd.Parameters.AddWithValue("account_id", line.AccountId);
            cmd.Parameters.AddWithValue("statement_date", line.TransactionDate);
            cmd.Parameters.AddWithValue("value_date", line.ValueDate);
            cmd.Parameters.AddWithValue("amount", line.Amount);
            cmd.Parameters.AddWithValue("currency", line.Currency);
            cmd.Parameters.AddWithValue("transaction_type", line.TransactionType);
            cmd.Parameters.AddWithValue("description", line.Description);
            cmd.Parameters.AddWithValue("external_reference", (object?)line.Reference ?? DBNull.Value);
            cmd.Parameters.AddWithValue("running_balance", line.ClosingBalance.HasValue ? (object)line.ClosingBalance.Value : DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CustodianPositionLineDto>> GetCustodianPositionsAsync(
        Guid accountId, DateOnly asOfDate, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT position_line_id, batch_id, account_id, as_of_date,
                   security_identifier, identifier_type, quantity, market_value,
                   market_value_currency, raw_payload
            FROM {Qualified("custodian_position_line")}
            WHERE account_id = @account_id AND as_of_date = @as_of_date
            """;
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("as_of_date", asOfDate);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<CustodianPositionLineDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var rawJson = reader.IsDBNull(reader.GetOrdinal("raw_payload")) ? null : reader.GetString(reader.GetOrdinal("raw_payload"));
            string? securityName = null;
            string? assetClass = null;
            bool isShort = false;
            if (rawJson is not null)
            {
                using var doc = JsonDocument.Parse(rawJson);
                if (doc.RootElement.TryGetProperty("securityName", out var snProp))
                    securityName = snProp.GetString();
                if (doc.RootElement.TryGetProperty("assetClass", out var acProp))
                    assetClass = acProp.GetString();
                if (doc.RootElement.TryGetProperty("isShort", out var isProp))
                    isShort = isProp.GetBoolean();
            }
            results.Add(new CustodianPositionLineDto(
                LineId: reader.GetGuid(reader.GetOrdinal("position_line_id")),
                BatchId: reader.GetGuid(reader.GetOrdinal("batch_id")),
                AccountId: reader.GetGuid(reader.GetOrdinal("account_id")),
                AsOfDate: reader.GetFieldValue<DateOnly>(reader.GetOrdinal("as_of_date")),
                Identifier: reader.GetString(reader.GetOrdinal("security_identifier")),
                IdentifierType: reader.GetString(reader.GetOrdinal("identifier_type")),
                Quantity: reader.GetDecimal(reader.GetOrdinal("quantity")),
                MarketValue: reader.GetDecimal(reader.GetOrdinal("market_value")),
                Currency: reader.GetString(reader.GetOrdinal("market_value_currency")),
                SecurityName: securityName,
                AssetClass: assetClass,
                IsShort: isShort));
        }
        return results;
    }

    public async Task<IReadOnlyList<BankStatementLineDto>> GetBankStatementLinesAsync(
        Guid accountId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        var sb = new StringBuilder();
        sb.AppendLine($"""
            SELECT statement_line_id, batch_id, account_id, statement_date, value_date,
                   amount, currency, transaction_type, description, external_reference, running_balance
            FROM {Qualified("bank_statement_line")}
            WHERE account_id = @account_id
            """);
        cmd.Parameters.AddWithValue("account_id", accountId);
        if (fromDate.HasValue)
        {
            sb.AppendLine("  AND statement_date >= @from_date");
            cmd.Parameters.AddWithValue("from_date", fromDate.Value);
        }
        if (toDate.HasValue)
        {
            sb.AppendLine("  AND statement_date <= @to_date");
            cmd.Parameters.AddWithValue("to_date", toDate.Value);
        }
        sb.AppendLine("  ORDER BY statement_date DESC");
        cmd.CommandText = sb.ToString();

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<BankStatementLineDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new BankStatementLineDto(
                LineId: reader.GetGuid(reader.GetOrdinal("statement_line_id")),
                BatchId: reader.GetGuid(reader.GetOrdinal("batch_id")),
                AccountId: reader.GetGuid(reader.GetOrdinal("account_id")),
                TransactionDate: reader.GetFieldValue<DateOnly>(reader.GetOrdinal("statement_date")),
                ValueDate: reader.GetFieldValue<DateOnly>(reader.GetOrdinal("value_date")),
                Amount: reader.GetDecimal(reader.GetOrdinal("amount")),
                Currency: reader.GetString(reader.GetOrdinal("currency")),
                TransactionType: reader.GetString(reader.GetOrdinal("transaction_type")),
                Description: reader.GetString(reader.GetOrdinal("description")),
                Reference: reader.IsDBNull(reader.GetOrdinal("external_reference")) ? null : reader.GetString(reader.GetOrdinal("external_reference")),
                ClosingBalance: reader.IsDBNull(reader.GetOrdinal("running_balance")) ? null : reader.GetDecimal(reader.GetOrdinal("running_balance"))));
        }
        return results;
    }

    // ── Reconciliation ─────────────────────────────────────────────────────────

    public async Task InsertReconciliationRunAsync(
        AccountReconciliationRunDto run,
        IReadOnlyList<AccountReconciliationResultDto> results,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"""
                INSERT INTO {Qualified("account_reconciliation_run")}
                    (reconciliation_run_id, account_id, as_of_date, status,
                     total_checks, total_matched, total_breaks, break_amount_total,
                     requested_at, completed_at, requested_by)
                VALUES
                    (@reconciliation_run_id, @account_id, @as_of_date, @status,
                     @total_checks, @total_matched, @total_breaks, @break_amount_total,
                     @requested_at, @completed_at, @requested_by)
                ON CONFLICT (reconciliation_run_id) DO NOTHING
                """;
            cmd.Parameters.AddWithValue("reconciliation_run_id", run.ReconciliationRunId);
            cmd.Parameters.AddWithValue("account_id", run.AccountId);
            cmd.Parameters.AddWithValue("as_of_date", run.AsOfDate);
            cmd.Parameters.AddWithValue("status", run.Status);
            cmd.Parameters.AddWithValue("total_checks", run.TotalChecks);
            cmd.Parameters.AddWithValue("total_matched", run.TotalMatched);
            cmd.Parameters.AddWithValue("total_breaks", run.TotalBreaks);
            cmd.Parameters.AddWithValue("break_amount_total", run.BreakAmountTotal);
            cmd.Parameters.AddWithValue("requested_at", run.RequestedAt);
            cmd.Parameters.AddWithValue("completed_at", run.CompletedAt.HasValue ? (object)run.CompletedAt.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("requested_by", run.RequestedBy);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var result in results)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"""
                INSERT INTO {Qualified("account_reconciliation_breaks")}
                    (result_id, reconciliation_run_id, break_type, check_label,
                     is_match, category, status, expected_amount, actual_amount,
                     variance, reason)
                VALUES
                    (@result_id, @reconciliation_run_id, @break_type, @check_label,
                     @is_match, @category, @status, @expected_amount, @actual_amount,
                     @variance, @reason)
                ON CONFLICT (result_id) DO NOTHING
                """;
            cmd.Parameters.AddWithValue("result_id", result.ResultId);
            cmd.Parameters.AddWithValue("reconciliation_run_id", result.ReconciliationRunId);
            cmd.Parameters.AddWithValue("break_type", result.Category);
            cmd.Parameters.AddWithValue("check_label", result.CheckLabel);
            cmd.Parameters.AddWithValue("is_match", result.IsMatch);
            cmd.Parameters.AddWithValue("category", result.Category);
            cmd.Parameters.AddWithValue("status", result.Status);
            cmd.Parameters.AddWithValue("expected_amount", result.ExpectedAmount.HasValue ? (object)result.ExpectedAmount.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("actual_amount", result.ActualAmount.HasValue ? (object)result.ActualAmount.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("variance", result.Variance.HasValue ? (object)result.Variance.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("reason", result.Reason);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountReconciliationRunDto>> GetReconciliationRunsAsync(
        Guid accountId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT reconciliation_run_id, account_id, as_of_date, status,
                   total_checks, total_matched, total_breaks, break_amount_total,
                   requested_at, completed_at, requested_by
            FROM {Qualified("account_reconciliation_run")}
            WHERE account_id = @account_id
            ORDER BY as_of_date DESC, requested_at DESC
            """;
        cmd.Parameters.AddWithValue("account_id", accountId);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<AccountReconciliationRunDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new AccountReconciliationRunDto(
                ReconciliationRunId: reader.GetGuid(reader.GetOrdinal("reconciliation_run_id")),
                AccountId: reader.GetGuid(reader.GetOrdinal("account_id")),
                AsOfDate: reader.GetFieldValue<DateOnly>(reader.GetOrdinal("as_of_date")),
                Status: reader.GetString(reader.GetOrdinal("status")),
                TotalChecks: reader.GetInt32(reader.GetOrdinal("total_checks")),
                TotalMatched: reader.GetInt32(reader.GetOrdinal("total_matched")),
                TotalBreaks: reader.GetInt32(reader.GetOrdinal("total_breaks")),
                BreakAmountTotal: reader.GetDecimal(reader.GetOrdinal("break_amount_total")),
                RequestedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("requested_at")),
                CompletedAt: reader.IsDBNull(reader.GetOrdinal("completed_at")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("completed_at")),
                RequestedBy: reader.GetString(reader.GetOrdinal("requested_by"))));
        }
        return results;
    }

    public async Task<IReadOnlyList<AccountReconciliationResultDto>> GetReconciliationResultsAsync(
        Guid reconciliationRunId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT result_id, reconciliation_run_id, check_label,
                   is_match, category, status, expected_amount, actual_amount,
                   variance, reason
            FROM {Qualified("account_reconciliation_breaks")}
            WHERE reconciliation_run_id = @reconciliation_run_id
            """;
        cmd.Parameters.AddWithValue("reconciliation_run_id", reconciliationRunId);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<AccountReconciliationResultDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new AccountReconciliationResultDto(
                ResultId: reader.GetGuid(reader.GetOrdinal("result_id")),
                ReconciliationRunId: reader.GetGuid(reader.GetOrdinal("reconciliation_run_id")),
                CheckLabel: reader.GetString(reader.GetOrdinal("check_label")),
                IsMatch: reader.GetBoolean(reader.GetOrdinal("is_match")),
                Category: reader.GetString(reader.GetOrdinal("category")),
                Status: reader.GetString(reader.GetOrdinal("status")),
                ExpectedAmount: reader.IsDBNull(reader.GetOrdinal("expected_amount")) ? null : reader.GetDecimal(reader.GetOrdinal("expected_amount")),
                ActualAmount: reader.IsDBNull(reader.GetOrdinal("actual_amount")) ? null : reader.GetDecimal(reader.GetOrdinal("actual_amount")),
                Variance: reader.IsDBNull(reader.GetOrdinal("variance")) ? null : reader.GetDecimal(reader.GetOrdinal("variance")),
                Reason: reader.GetString(reader.GetOrdinal("reason"))));
        }
        return results;
    }

    // ── Sync history ──────────────────────────────────────────────────────────

    public async Task InsertSyncHistoryAsync(AccountSyncHistoryEntryDto entry, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {Qualified("account_sync_history")}
                (sync_history_id, account_id, capability, status, provider_link_status,
                 provider_id, external_account_id, attempted_at, completed_at, fresh_until,
                 failure_kind, failure_message, correlation_id, requested_by,
                 raw_evidence_path, projection_evidence_path, security_missing_count, warnings)
            VALUES
                (@sync_history_id, @account_id, @capability, @status, @provider_link_status,
                 @provider_id, @external_account_id, @attempted_at, @completed_at, @fresh_until,
                 @failure_kind, @failure_message, @correlation_id, @requested_by,
                 @raw_evidence_path, @projection_evidence_path, @security_missing_count, @warnings::jsonb)
            ON CONFLICT (sync_history_id) DO UPDATE SET
                status                  = EXCLUDED.status,
                provider_link_status    = EXCLUDED.provider_link_status,
                provider_id             = EXCLUDED.provider_id,
                external_account_id     = EXCLUDED.external_account_id,
                attempted_at            = EXCLUDED.attempted_at,
                completed_at            = EXCLUDED.completed_at,
                fresh_until             = EXCLUDED.fresh_until,
                failure_kind            = EXCLUDED.failure_kind,
                failure_message         = EXCLUDED.failure_message,
                security_missing_count  = EXCLUDED.security_missing_count,
                warnings                = EXCLUDED.warnings
            """;
        cmd.Parameters.AddWithValue("sync_history_id", entry.SyncHistoryId);
        cmd.Parameters.AddWithValue("account_id", entry.AccountId);
        cmd.Parameters.AddWithValue("capability", entry.Capability);
        cmd.Parameters.AddWithValue("status", entry.Status.ToString());
        cmd.Parameters.AddWithValue("provider_link_status", entry.ProviderLinkStatus.ToString());
        cmd.Parameters.AddWithValue("provider_id", (object?)entry.ProviderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("external_account_id", (object?)entry.ExternalAccountId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("attempted_at", entry.AttemptedAt);
        cmd.Parameters.AddWithValue("completed_at", entry.CompletedAt.HasValue ? (object)entry.CompletedAt.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("fresh_until", entry.FreshUntil.HasValue ? (object)entry.FreshUntil.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("failure_kind", entry.FailureKind.ToString());
        cmd.Parameters.AddWithValue("failure_message", (object?)entry.FailureMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("correlation_id", (object?)entry.CorrelationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("requested_by", (object?)entry.RequestedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("raw_evidence_path", (object?)entry.RawEvidencePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("projection_evidence_path", (object?)entry.ProjectionEvidencePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("security_missing_count", entry.SecurityMissingCount);
        cmd.Parameters.AddWithValue("warnings", JsonSerializer.Serialize(entry.Warnings, JsonOpts));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountSyncHistoryEntryDto>> GetSyncHistoryAsync(
        Guid accountId, string? capability, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        var sb = new StringBuilder();
        sb.AppendLine($"""
            SELECT sync_history_id, account_id, capability, status, provider_link_status,
                   provider_id, external_account_id, attempted_at, completed_at, fresh_until,
                   failure_kind, failure_message, correlation_id, requested_by,
                   raw_evidence_path, projection_evidence_path, security_missing_count, warnings
            FROM {Qualified("account_sync_history")}
            WHERE account_id = @account_id
            """);
        cmd.Parameters.AddWithValue("account_id", accountId);
        if (!string.IsNullOrWhiteSpace(capability))
        {
            sb.AppendLine("  AND lower(capability) = lower(@capability)");
            cmd.Parameters.AddWithValue("capability", capability);
        }
        sb.AppendLine("  ORDER BY attempted_at DESC, completed_at DESC NULLS LAST");
        cmd.CommandText = sb.ToString();

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<AccountSyncHistoryEntryDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(ReadSyncHistoryEntry(reader));
        }
        return results;
    }

    private static AccountSyncHistoryEntryDto ReadSyncHistoryEntry(NpgsqlDataReader reader)
    {
        var warningsJson = reader.IsDBNull(reader.GetOrdinal("warnings")) ? "[]" : reader.GetString(reader.GetOrdinal("warnings"));
        var warnings = JsonSerializer.Deserialize<List<string>>(warningsJson, JsonOpts) ?? [];
        return new AccountSyncHistoryEntryDto(
            SyncHistoryId: reader.GetGuid(reader.GetOrdinal("sync_history_id")),
            AccountId: reader.GetGuid(reader.GetOrdinal("account_id")),
            Capability: reader.GetString(reader.GetOrdinal("capability")),
            Status: Enum.Parse<AccountSyncStatusDto>(reader.GetString(reader.GetOrdinal("status"))),
            ProviderLinkStatus: Enum.Parse<AccountProviderLinkStatusDto>(reader.GetString(reader.GetOrdinal("provider_link_status"))),
            ProviderId: reader.IsDBNull(reader.GetOrdinal("provider_id")) ? null : reader.GetString(reader.GetOrdinal("provider_id")),
            ExternalAccountId: reader.IsDBNull(reader.GetOrdinal("external_account_id")) ? null : reader.GetString(reader.GetOrdinal("external_account_id")),
            AttemptedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("attempted_at")),
            CompletedAt: reader.IsDBNull(reader.GetOrdinal("completed_at")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("completed_at")),
            FreshUntil: reader.IsDBNull(reader.GetOrdinal("fresh_until")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("fresh_until")),
            FailureKind: Enum.Parse<AccountSyncFailureKindDto>(reader.GetString(reader.GetOrdinal("failure_kind"))),
            FailureMessage: reader.IsDBNull(reader.GetOrdinal("failure_message")) ? null : reader.GetString(reader.GetOrdinal("failure_message")),
            CorrelationId: reader.IsDBNull(reader.GetOrdinal("correlation_id")) ? null : reader.GetString(reader.GetOrdinal("correlation_id")),
            RequestedBy: reader.IsDBNull(reader.GetOrdinal("requested_by")) ? null : reader.GetString(reader.GetOrdinal("requested_by")),
            RawEvidencePath: reader.IsDBNull(reader.GetOrdinal("raw_evidence_path")) ? null : reader.GetString(reader.GetOrdinal("raw_evidence_path")),
            ProjectionEvidencePath: reader.IsDBNull(reader.GetOrdinal("projection_evidence_path")) ? null : reader.GetString(reader.GetOrdinal("projection_evidence_path")),
            SecurityMissingCount: reader.GetInt32(reader.GetOrdinal("security_missing_count")),
            Warnings: warnings);
    }

    // ── Margin snapshots ──────────────────────────────────────────────────────

    public async Task UpsertMarginSnapshotAsync(MarginSnapshotDto snapshot, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {Qualified("account_margin_snapshot")}
                (margin_snapshot_id, account_id, effective_at, recorded_at, currency,
                 margin_type, margin_call_status, initial_margin, maintenance_margin,
                 excess_liquidity, buying_power, sma, loan_balance, debit_balance,
                 credit_balance, collateral_value, marginable_securities_value,
                 non_marginable_securities_value, margin_utilization,
                 missing_requirement_count, missing_collateral_classification_count,
                 concentration_limit_breach_count, is_live_account, approved_for_live_margin,
                 requirements_json, warnings_json, provider_id, external_account_id,
                 fresh_until, agreement_evidence_path, snapshot_evidence_path, correlation_id)
            VALUES
                (@margin_snapshot_id, @account_id, @effective_at, @recorded_at, @currency,
                 @margin_type, @margin_call_status, @initial_margin, @maintenance_margin,
                 @excess_liquidity, @buying_power, @sma, @loan_balance, @debit_balance,
                 @credit_balance, @collateral_value, @marginable_securities_value,
                 @non_marginable_securities_value, @margin_utilization,
                 @missing_requirement_count, @missing_collateral_classification_count,
                 @concentration_limit_breach_count, @is_live_account, @approved_for_live_margin,
                 @requirements_json::jsonb, @warnings_json::jsonb, @provider_id, @external_account_id,
                 @fresh_until, @agreement_evidence_path, @snapshot_evidence_path, @correlation_id)
            ON CONFLICT (account_id, effective_at) DO UPDATE SET
                margin_snapshot_id                      = EXCLUDED.margin_snapshot_id,
                recorded_at                             = EXCLUDED.recorded_at,
                currency                                = EXCLUDED.currency,
                margin_type                             = EXCLUDED.margin_type,
                margin_call_status                      = EXCLUDED.margin_call_status,
                initial_margin                          = EXCLUDED.initial_margin,
                maintenance_margin                      = EXCLUDED.maintenance_margin,
                excess_liquidity                        = EXCLUDED.excess_liquidity,
                buying_power                            = EXCLUDED.buying_power,
                sma                                     = EXCLUDED.sma,
                loan_balance                            = EXCLUDED.loan_balance,
                debit_balance                           = EXCLUDED.debit_balance,
                credit_balance                          = EXCLUDED.credit_balance,
                collateral_value                        = EXCLUDED.collateral_value,
                marginable_securities_value             = EXCLUDED.marginable_securities_value,
                non_marginable_securities_value         = EXCLUDED.non_marginable_securities_value,
                margin_utilization                      = EXCLUDED.margin_utilization,
                missing_requirement_count               = EXCLUDED.missing_requirement_count,
                missing_collateral_classification_count = EXCLUDED.missing_collateral_classification_count,
                concentration_limit_breach_count        = EXCLUDED.concentration_limit_breach_count,
                is_live_account                         = EXCLUDED.is_live_account,
                approved_for_live_margin                = EXCLUDED.approved_for_live_margin,
                requirements_json                       = EXCLUDED.requirements_json,
                warnings_json                           = EXCLUDED.warnings_json,
                provider_id                             = EXCLUDED.provider_id,
                external_account_id                     = EXCLUDED.external_account_id,
                fresh_until                             = EXCLUDED.fresh_until,
                agreement_evidence_path                 = EXCLUDED.agreement_evidence_path,
                snapshot_evidence_path                  = EXCLUDED.snapshot_evidence_path,
                correlation_id                          = EXCLUDED.correlation_id
            """;
        cmd.Parameters.AddWithValue("margin_snapshot_id", snapshot.MarginSnapshotId);
        cmd.Parameters.AddWithValue("account_id", snapshot.AccountId);
        cmd.Parameters.AddWithValue("effective_at", snapshot.EffectiveAt);
        cmd.Parameters.AddWithValue("recorded_at", snapshot.RecordedAt);
        cmd.Parameters.AddWithValue("currency", snapshot.Currency);
        cmd.Parameters.AddWithValue("margin_type", snapshot.MarginType.ToString());
        cmd.Parameters.AddWithValue("margin_call_status", snapshot.MarginCallStatus.ToString());
        cmd.Parameters.AddWithValue("initial_margin", snapshot.InitialMargin.HasValue ? (object)snapshot.InitialMargin.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("maintenance_margin", snapshot.MaintenanceMargin.HasValue ? (object)snapshot.MaintenanceMargin.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("excess_liquidity", snapshot.ExcessLiquidity.HasValue ? (object)snapshot.ExcessLiquidity.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("buying_power", snapshot.BuyingPower.HasValue ? (object)snapshot.BuyingPower.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("sma", snapshot.SpecialMemorandumAccount.HasValue ? (object)snapshot.SpecialMemorandumAccount.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("loan_balance", snapshot.LoanBalance.HasValue ? (object)snapshot.LoanBalance.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("debit_balance", snapshot.DebitBalance.HasValue ? (object)snapshot.DebitBalance.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("credit_balance", snapshot.CreditBalance.HasValue ? (object)snapshot.CreditBalance.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("collateral_value", snapshot.CollateralValue.HasValue ? (object)snapshot.CollateralValue.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("marginable_securities_value", snapshot.MarginableSecuritiesValue.HasValue ? (object)snapshot.MarginableSecuritiesValue.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("non_marginable_securities_value", snapshot.NonMarginableSecuritiesValue.HasValue ? (object)snapshot.NonMarginableSecuritiesValue.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("margin_utilization", snapshot.MarginUtilization.HasValue ? (object)snapshot.MarginUtilization.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("missing_requirement_count", snapshot.MissingRequirementCount);
        cmd.Parameters.AddWithValue("missing_collateral_classification_count", snapshot.MissingCollateralClassificationCount);
        cmd.Parameters.AddWithValue("concentration_limit_breach_count", snapshot.ConcentrationLimitBreachCount);
        cmd.Parameters.AddWithValue("is_live_account", snapshot.IsLiveAccount);
        cmd.Parameters.AddWithValue("approved_for_live_margin", snapshot.ApprovedForLiveMargin);
        cmd.Parameters.AddWithValue("requirements_json", JsonSerializer.Serialize(snapshot.Requirements, JsonOpts));
        cmd.Parameters.AddWithValue("warnings_json", JsonSerializer.Serialize(snapshot.Warnings, JsonOpts));
        cmd.Parameters.AddWithValue("provider_id", (object?)snapshot.ProviderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("external_account_id", (object?)snapshot.ExternalAccountId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("fresh_until", snapshot.FreshUntil.HasValue ? (object)snapshot.FreshUntil.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("agreement_evidence_path", (object?)snapshot.AgreementEvidencePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("snapshot_evidence_path", (object?)snapshot.SnapshotEvidencePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("correlation_id", (object?)snapshot.CorrelationId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MarginSnapshotDto>> GetMarginSnapshotsAsync(
        Guid accountId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT margin_snapshot_id, account_id, effective_at, recorded_at, currency,
                   margin_type, margin_call_status, initial_margin, maintenance_margin,
                   excess_liquidity, buying_power, sma, loan_balance, debit_balance,
                   credit_balance, collateral_value, marginable_securities_value,
                   non_marginable_securities_value, margin_utilization,
                   missing_requirement_count, missing_collateral_classification_count,
                   concentration_limit_breach_count, is_live_account, approved_for_live_margin,
                   requirements_json, warnings_json, provider_id, external_account_id,
                   fresh_until, agreement_evidence_path, snapshot_evidence_path, correlation_id
            FROM {Qualified("account_margin_snapshot")}
            WHERE account_id = @account_id
            ORDER BY effective_at DESC, recorded_at DESC
            """;
        cmd.Parameters.AddWithValue("account_id", accountId);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<MarginSnapshotDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(ReadMarginSnapshot(reader));
        }
        return results;
    }

    private static MarginSnapshotDto ReadMarginSnapshot(NpgsqlDataReader reader)
    {
        var reqJson = reader.IsDBNull(reader.GetOrdinal("requirements_json")) ? "[]" : reader.GetString(reader.GetOrdinal("requirements_json"));
        var warnJson = reader.IsDBNull(reader.GetOrdinal("warnings_json")) ? "[]" : reader.GetString(reader.GetOrdinal("warnings_json"));
        var requirements = JsonSerializer.Deserialize<List<MarginRequirementDto>>(reqJson, JsonOpts) ?? [];
        var warnings = JsonSerializer.Deserialize<List<string>>(warnJson, JsonOpts) ?? [];

        return new MarginSnapshotDto(
            MarginSnapshotId: reader.GetGuid(reader.GetOrdinal("margin_snapshot_id")),
            AccountId: reader.GetGuid(reader.GetOrdinal("account_id")),
            EffectiveAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("effective_at")),
            RecordedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("recorded_at")),
            Currency: reader.GetString(reader.GetOrdinal("currency")),
            MarginType: Enum.Parse<MarginModelTypeDto>(reader.GetString(reader.GetOrdinal("margin_type"))),
            MarginCallStatus: Enum.Parse<MarginCallStatusDto>(reader.GetString(reader.GetOrdinal("margin_call_status"))),
            InitialMargin: reader.IsDBNull(reader.GetOrdinal("initial_margin")) ? null : reader.GetDecimal(reader.GetOrdinal("initial_margin")),
            MaintenanceMargin: reader.IsDBNull(reader.GetOrdinal("maintenance_margin")) ? null : reader.GetDecimal(reader.GetOrdinal("maintenance_margin")),
            ExcessLiquidity: reader.IsDBNull(reader.GetOrdinal("excess_liquidity")) ? null : reader.GetDecimal(reader.GetOrdinal("excess_liquidity")),
            BuyingPower: reader.IsDBNull(reader.GetOrdinal("buying_power")) ? null : reader.GetDecimal(reader.GetOrdinal("buying_power")),
            SpecialMemorandumAccount: reader.IsDBNull(reader.GetOrdinal("sma")) ? null : reader.GetDecimal(reader.GetOrdinal("sma")),
            LoanBalance: reader.IsDBNull(reader.GetOrdinal("loan_balance")) ? null : reader.GetDecimal(reader.GetOrdinal("loan_balance")),
            DebitBalance: reader.IsDBNull(reader.GetOrdinal("debit_balance")) ? null : reader.GetDecimal(reader.GetOrdinal("debit_balance")),
            CreditBalance: reader.IsDBNull(reader.GetOrdinal("credit_balance")) ? null : reader.GetDecimal(reader.GetOrdinal("credit_balance")),
            CollateralValue: reader.IsDBNull(reader.GetOrdinal("collateral_value")) ? null : reader.GetDecimal(reader.GetOrdinal("collateral_value")),
            MarginableSecuritiesValue: reader.IsDBNull(reader.GetOrdinal("marginable_securities_value")) ? null : reader.GetDecimal(reader.GetOrdinal("marginable_securities_value")),
            NonMarginableSecuritiesValue: reader.IsDBNull(reader.GetOrdinal("non_marginable_securities_value")) ? null : reader.GetDecimal(reader.GetOrdinal("non_marginable_securities_value")),
            MarginUtilization: reader.IsDBNull(reader.GetOrdinal("margin_utilization")) ? null : reader.GetDecimal(reader.GetOrdinal("margin_utilization")),
            MissingRequirementCount: reader.GetInt32(reader.GetOrdinal("missing_requirement_count")),
            MissingCollateralClassificationCount: reader.GetInt32(reader.GetOrdinal("missing_collateral_classification_count")),
            ConcentrationLimitBreachCount: reader.GetInt32(reader.GetOrdinal("concentration_limit_breach_count")),
            IsLiveAccount: reader.GetBoolean(reader.GetOrdinal("is_live_account")),
            ApprovedForLiveMargin: reader.GetBoolean(reader.GetOrdinal("approved_for_live_margin")),
            Requirements: requirements,
            Warnings: warnings,
            ProviderId: reader.IsDBNull(reader.GetOrdinal("provider_id")) ? null : reader.GetString(reader.GetOrdinal("provider_id")),
            ExternalAccountId: reader.IsDBNull(reader.GetOrdinal("external_account_id")) ? null : reader.GetString(reader.GetOrdinal("external_account_id")),
            FreshUntil: reader.IsDBNull(reader.GetOrdinal("fresh_until")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("fresh_until")),
            AgreementEvidencePath: reader.IsDBNull(reader.GetOrdinal("agreement_evidence_path")) ? null : reader.GetString(reader.GetOrdinal("agreement_evidence_path")),
            SnapshotEvidencePath: reader.IsDBNull(reader.GetOrdinal("snapshot_evidence_path")) ? null : reader.GetString(reader.GetOrdinal("snapshot_evidence_path")),
            CorrelationId: reader.IsDBNull(reader.GetOrdinal("correlation_id")) ? null : reader.GetString(reader.GetOrdinal("correlation_id")));
    }

    // ── Emptiness check ───────────────────────────────────────────────────────

    public async Task<bool> IsEmptyAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) = 0 FROM {Qualified("account_definition")}";
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is true;
    }
}
