using System.Text.Json;
using Meridian.Contracts.Schema;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresSecurityMasterStore : ISecurityMasterStore
{
    private const string BondProjectionTable = "bond_projection";
    private const string BondLifecycleProjectionTable = "bond_lifecycle_projection";
    private const string BondAccrualConventionProjectionTable = "bond_accrual_convention_projection";
    private const string BondIssuerProjectionTable = "bond_issuer_projection";
    private const string OptionContractProjectionTable = "option_contract_projection";
    private const string OptionSeriesProjectionTable = "option_series_projection";
    private const string OptionLifecycleProjectionTable = "option_lifecycle_projection";
    private const string OptionAliasProjectionTable = "option_alias_projection";
    private const string EquityProjectionTable = "equity_projection";
    private const string FutureProjectionTable = "future_projection";
    private const string FxSpotProjectionTable = "fxspot_projection";
    private const string SwapProjectionTable = "swap_projection";
    private const string CommodityProjectionTable = "commodity_projection";
    private const string CryptoProjectionTable = "crypto_projection";
    private const string DepositProjectionTable = "deposit_projection";
    private const string MoneyMarketFundProjectionTable = "money_market_fund_projection";
    private const string CertificateOfDepositProjectionTable = "certificate_of_deposit_projection";

    /// <summary>
    /// A registered per-asset-class relational projection writer: the asset class it owns and the
    /// upsert-or-delete operation the store fans out to for every persisted record.
    /// </summary>
    private sealed record AssetProjectionWriter(
        string AssetClass,
        Func<PostgresSecurityMasterStore, NpgsqlConnection, NpgsqlTransaction, SecurityProjectionRecord, CancellationToken, Task> WriteAsync);

    /// <summary>
    /// The ordered registry of per-asset-class projection writers. Replaces the previous hard-coded
    /// fan-out block so adding a projected asset class is a single additive registration here, and the
    /// projected set is enumerable for the catalog-vs-projection coverage guard.
    /// </summary>
    private static readonly IReadOnlyList<AssetProjectionWriter> ProjectionWriters =
    [
        new("Bond", static (store, c, t, r, ct) => store.UpsertBondProjectionTablesAsync(c, t, r, ct)),
        new("Option", static (store, c, t, r, ct) => store.UpsertOptionProjectionTablesAsync(c, t, r, ct)),
        new("Equity", static (store, c, t, r, ct) => store.UpsertEquityProjectionAsync(c, t, r, ct)),
        new("Future", static (store, c, t, r, ct) => store.UpsertFutureProjectionAsync(c, t, r, ct)),
        new("FxSpot", static (store, c, t, r, ct) => store.UpsertFxSpotProjectionAsync(c, t, r, ct)),
        new("Swap", static (store, c, t, r, ct) => store.UpsertSwapProjectionAsync(c, t, r, ct)),
        new("Commodity", static (store, c, t, r, ct) => store.UpsertCommodityProjectionAsync(c, t, r, ct)),
        new("CryptoCurrency", static (store, c, t, r, ct) => store.UpsertCryptoProjectionAsync(c, t, r, ct)),
        new("Deposit", static (store, c, t, r, ct) => store.UpsertDepositProjectionAsync(c, t, r, ct)),
        new("MoneyMarketFund", static (store, c, t, r, ct) => store.UpsertMoneyMarketFundProjectionAsync(c, t, r, ct)),
        new("CertificateOfDeposit", static (store, c, t, r, ct) => store.UpsertCertificateOfDepositProjectionAsync(c, t, r, ct)),
    ];

    /// <summary>The asset classes that have a dedicated relational projection, in fan-out order.</summary>
    internal static IReadOnlyList<string> ProjectedAssetClasses { get; } =
        ProjectionWriters.Select(static writer => writer.AssetClass).ToArray();

    private readonly SecurityMasterOptions _options;
    private readonly ISchemaUpcaster<SecurityAssetSpecificTerms> _assetSpecificTermsUpcaster;

    public PostgresSecurityMasterStore(
        SecurityMasterOptions options,
        ISchemaUpcaster<SecurityAssetSpecificTerms>? assetSpecificTermsUpcaster = null)
    {
        _options = options;
        _assetSpecificTermsUpcaster = assetSpecificTermsUpcaster
            ?? SecurityAssetSpecificTermsUpcasterPipeline.Instance;
    }

    public async Task UpsertProjectionAsync(SecurityProjectionRecord record, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await UpsertProjectionCoreAsync(connection, transaction, record, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task PersistProjectionBatchAsync(
        string projectionName,
        long lastGlobalSequence,
        IReadOnlyList<SecurityProjectionRecord> records,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var record in records)
        {
            await UpsertProjectionCoreAsync(connection, transaction, record, ct).ConfigureAwait(false);
        }

        await SaveCheckpointCoreAsync(connection, transaction, projectionName, lastGlobalSequence, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertAliasAsync(SecurityAliasDto alias, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("security_aliases")} (
                alias_id, security_id, alias_kind, alias_value, normalized_alias_value, provider, normalized_provider, scope, reason,
                created_by, created_at, valid_from, valid_to, is_enabled)
            values (
                @alias_id, @security_id, @alias_kind, @alias_value, @normalized_alias_value, @provider, @normalized_provider, @scope, @reason,
                @created_by, @created_at, @valid_from, @valid_to, @is_enabled)
            on conflict (alias_id) do update set
                security_id = excluded.security_id,
                alias_kind = excluded.alias_kind,
                alias_value = excluded.alias_value,
                normalized_alias_value = excluded.normalized_alias_value,
                provider = excluded.provider,
                normalized_provider = excluded.normalized_provider,
                scope = excluded.scope,
                reason = excluded.reason,
                created_by = excluded.created_by,
                created_at = excluded.created_at,
                valid_from = excluded.valid_from,
                valid_to = excluded.valid_to,
                is_enabled = excluded.is_enabled;
            """;

        command.Parameters.AddWithValue("alias_id", alias.AliasId);
        command.Parameters.AddWithValue("security_id", alias.SecurityId);
        command.Parameters.AddWithValue("alias_kind", alias.AliasKind);
        command.Parameters.AddWithValue("alias_value", alias.AliasValue);
        command.Parameters.AddWithValue("normalized_alias_value", SecurityIdentifierNormalizer.NormalizeAliasValue(alias.AliasKind, alias.AliasValue));
        command.Parameters.AddWithValue("provider", (object?)alias.Provider ?? DBNull.Value);
        command.Parameters.AddWithValue("normalized_provider", ToDbNullable(SecurityIdentifierNormalizer.NormalizeProvider(alias.Provider)));
        command.Parameters.AddWithValue("scope", alias.Scope.ToString());
        command.Parameters.AddWithValue("reason", (object?)alias.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("created_by", alias.CreatedBy);
        command.Parameters.AddWithValue("created_at", alias.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("valid_from", alias.ValidFrom.UtcDateTime);
        command.Parameters.AddWithValue("valid_to", (object?)alias.ValidTo?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("is_enabled", alias.IsEnabled);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeactivateProjectionAsync(Guid securityId, DateTimeOffset effectiveTo, long version, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            update {Qualified("securities")}
            set status = 'Inactive',
                effective_to = @effective_to,
                version = @version
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("effective_to", effectiveTo.UtcDateTime);
        command.Parameters.AddWithValue("version", version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<SecurityDetailDto?> GetDetailAsync(Guid securityId, CancellationToken ct = default)
    {
        var projection = await GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        return projection is null ? null : SecurityMasterDbMapper.ToDetail(projection);
    }

    public async Task<SecurityProjectionRecord?> GetProjectionAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await GetProjectionCoreAsync(connection, securityId, ct).ConfigureAwait(false);
    }

    public async Task<SecurityProjectionRecord?> GetByIdentifierAsync(SecurityIdentifierKind kind, string value, string? provider, DateTimeOffset asOfUtc, bool includeInactive, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        var securityId = await ResolveSecurityIdAsync(connection, kind, value, provider, asOfUtc, includeInactive, ct).ConfigureAwait(false);
        return securityId is null ? null : await GetProjectionCoreAsync(connection, securityId.Value, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var trimmedQuery = request.Query.Trim();
        var useFts = trimmedQuery.Length >= 2;

        // Use full-text search when the query is long enough for meaningful tokenisation.
        // The ILIKE fallback catches short identifiers (e.g. "GE", "T") that tsvector skips.
        command.CommandText = useFts
            ? $"""
               select security_id,
                      ts_rank(search_vector, plainto_tsquery('simple', @query)) as rank
               from {Qualified("securities")}
               where (
                   search_vector @@ plainto_tsquery('simple', @query)
                   or display_name ilike @pattern
                   or primary_identifier_value ilike @pattern)
                 and (@active_only = false or status = 'Active')
               order by rank desc, display_name
               limit @take offset @skip;
               """
            : $"""
               select security_id, 0::float4 as rank
               from {Qualified("securities")}
               where (
                   display_name ilike @pattern
                   or primary_identifier_value ilike @pattern)
                 and (@active_only = false or status = 'Active')
               order by display_name
               limit @take offset @skip;
               """;

        command.Parameters.AddWithValue("query", trimmedQuery);
        command.Parameters.AddWithValue("pattern", $"%{trimmedQuery}%");
        command.Parameters.AddWithValue("active_only", request.ActiveOnly);
        command.Parameters.AddWithValue("take", request.Take);
        command.Parameters.AddWithValue("skip", request.Skip);

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ids.Add(reader.GetGuid(0));
        }

        var results = new List<SecuritySummaryDto>(ids.Count);
        foreach (var id in ids)
        {
            var projection = await GetProjectionCoreAsync(connection, id, ct).ConfigureAwait(false);
            if (projection is not null)
            {
                results.Add(SecurityMasterDbMapper.ToSummary(projection));
            }
        }

        return results;
    }

    public Task<IReadOnlyList<SecurityProjectionRecord>> LoadAllAsync(CancellationToken ct = default)
        => LoadByStatusAsync(activeOnly: false, ct);

    public Task<IReadOnlyList<SecurityProjectionRecord>> LoadActiveAsync(CancellationToken ct = default)
        => LoadByStatusAsync(activeOnly: true, ct);

    private async Task<IReadOnlyList<SecurityProjectionRecord>> LoadByStatusAsync(
        bool activeOnly, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id from {Qualified("securities")}
            where (@active_only = false or status = 'Active')
            order by display_name;
            """;
        command.Parameters.AddWithValue("active_only", activeOnly);

        var ids = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        var results = new List<SecurityProjectionRecord>(ids.Count);
        foreach (var id in ids)
        {
            var projection = await GetProjectionCoreAsync(connection, id, ct).ConfigureAwait(false);
            if (projection is not null)
            {
                results.Add(projection);
            }
        }

        return results;
    }

    public async Task<long?> GetCheckpointAsync(string projectionName, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select last_global_sequence
            from {Qualified("projection_checkpoint")}
            where projection_name = @projection_name;
            """;
        command.Parameters.AddWithValue("projection_name", projectionName);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is null or DBNull ? null : (long)result;
    }

    public async Task SaveCheckpointAsync(string projectionName, long lastGlobalSequence, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await SaveCheckpointCoreAsync(connection, transaction: null, projectionName, lastGlobalSequence, ct).ConfigureAwait(false);
    }

    private async Task UpsertProjectionCoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("securities")} (
                    security_id, asset_class, status, display_name, currency, country_of_risk, issuer_name,
                    exchange_code, lot_size, tick_size, primary_identifier_kind, primary_identifier_value,
                    normalized_primary_identifier_value,
                    common_terms, asset_specific_terms, provenance, version, schema_version, effective_from, effective_to)
                values (
                    @security_id, @asset_class, @status, @display_name, @currency, @country_of_risk, @issuer_name,
                    @exchange_code, @lot_size, @tick_size, @primary_identifier_kind, @primary_identifier_value,
                    @normalized_primary_identifier_value,
                    @common_terms::jsonb, @asset_specific_terms::jsonb, @provenance::jsonb, @version, @schema_version,
                    @effective_from, @effective_to)
                on conflict (security_id) do update set
                    asset_class = excluded.asset_class,
                    status = excluded.status,
                    display_name = excluded.display_name,
                    currency = excluded.currency,
                    country_of_risk = excluded.country_of_risk,
                    issuer_name = excluded.issuer_name,
                    exchange_code = excluded.exchange_code,
                    lot_size = excluded.lot_size,
                    tick_size = excluded.tick_size,
                    primary_identifier_kind = excluded.primary_identifier_kind,
                    primary_identifier_value = excluded.primary_identifier_value,
                    normalized_primary_identifier_value = excluded.normalized_primary_identifier_value,
                    common_terms = excluded.common_terms,
                    asset_specific_terms = excluded.asset_specific_terms,
                    provenance = excluded.provenance,
                    version = excluded.version,
                    schema_version = excluded.schema_version,
                    effective_from = excluded.effective_from,
                    effective_to = excluded.effective_to;
                """;

            // Promote the asset-specific-terms schema version into the queryable schema_version column.
            // The upcaster is the single authority that resolves the effective version (a payload with
            // no explicit schemaVersion resolves to the legacy default). The stored payload itself is
            // written unchanged so no existing read path observes an altered blob.
            var schemaVersion = _assetSpecificTermsUpcaster.Upcast(record.AssetSpecificTerms.GetRawText())?.SchemaVersion
                ?? SecurityMasterSchemaVersions.DefaultAssetSpecificTerms;

            command.Parameters.AddWithValue("security_id", record.SecurityId);
            command.Parameters.AddWithValue("asset_class", record.AssetClass);
            command.Parameters.AddWithValue("status", record.Status.ToString());
            command.Parameters.AddWithValue("display_name", record.DisplayName);
            command.Parameters.AddWithValue("currency", record.Currency);
            command.Parameters.AddWithValue("country_of_risk", (object?)GetOptionalString(record.CommonTerms, "countryOfRisk") ?? DBNull.Value);
            command.Parameters.AddWithValue("issuer_name", (object?)GetOptionalString(record.CommonTerms, "issuerName") ?? DBNull.Value);
            command.Parameters.AddWithValue("exchange_code", (object?)GetOptionalString(record.CommonTerms, "exchange") ?? DBNull.Value);
            command.Parameters.AddWithValue("lot_size", (object?)GetOptionalDecimal(record.CommonTerms, "lotSize") ?? DBNull.Value);
            command.Parameters.AddWithValue("tick_size", (object?)GetOptionalDecimal(record.CommonTerms, "tickSize") ?? DBNull.Value);
            command.Parameters.AddWithValue("primary_identifier_kind", record.PrimaryIdentifierKind);
            command.Parameters.AddWithValue("primary_identifier_value", record.PrimaryIdentifierValue);
            command.Parameters.AddWithValue("normalized_primary_identifier_value", NormalizePrimaryIdentifierValue(record));
            command.Parameters.AddWithValue("common_terms", record.CommonTerms.GetRawText());
            command.Parameters.AddWithValue("asset_specific_terms", record.AssetSpecificTerms.GetRawText());
            command.Parameters.AddWithValue("provenance", record.Provenance.GetRawText());
            command.Parameters.AddWithValue("version", record.Version);
            command.Parameters.AddWithValue("schema_version", schemaVersion);
            command.Parameters.AddWithValue("effective_from", record.EffectiveFrom.UtcDateTime);
            command.Parameters.AddWithValue("effective_to", (object?)record.EffectiveTo?.UtcDateTime ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await ReplaceIdentifiersAsync(connection, transaction, record.SecurityId, record.Identifiers, ct).ConfigureAwait(false);
        await ReplaceAliasesAsync(connection, transaction, record.SecurityId, record.Aliases, ct).ConfigureAwait(false);

        // Fan out to every registered per-asset-class projection writer. Each writer upserts its own
        // projection when the record matches its asset class and deletes any stale row otherwise, so
        // every writer runs for every record (class changes clean up the previous projection). Adding a
        // projected asset class is a single additive registration in ProjectionWriters plus its writer.
        foreach (var writer in ProjectionWriters)
        {
            await writer.WriteAsync(this, connection, transaction, record, ct).ConfigureAwait(false);
        }
    }

    private async Task UpsertBondProjectionTablesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        if (!string.Equals(record.AssetClass, "Bond", StringComparison.OrdinalIgnoreCase) ||
            !TryBuildBondProjection(record, out var projection))
        {
            await DeleteBondProjectionTablesAsync(connection, transaction, record.SecurityId, ct).ConfigureAwait(false);
            return;
        }

        await UpsertBondProjectionAsync(connection, transaction, projection, ct).ConfigureAwait(false);
        await UpsertBondLifecycleProjectionAsync(connection, transaction, projection, ct).ConfigureAwait(false);
        await UpsertBondAccrualProjectionAsync(connection, transaction, projection, ct).ConfigureAwait(false);
        await UpsertBondIssuerProjectionAsync(connection, transaction, projection, record, ct).ConfigureAwait(false);
    }

    private async Task UpsertOptionProjectionTablesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        if (!string.Equals(record.AssetClass, "Option", StringComparison.OrdinalIgnoreCase) ||
            !TryBuildOptionProjection(record, out var projection))
        {
            await DeleteOptionProjectionTablesAsync(connection, transaction, record.SecurityId, ct).ConfigureAwait(false);
            return;
        }

        await UpsertOptionContractProjectionAsync(connection, transaction, projection, ct).ConfigureAwait(false);
        await UpsertOptionSeriesProjectionAsync(connection, transaction, projection, ct).ConfigureAwait(false);
        await UpsertOptionLifecycleProjectionAsync(connection, transaction, projection, ct).ConfigureAwait(false);
        await UpsertOptionAliasProjectionAsync(connection, transaction, projection, record.Aliases, ct).ConfigureAwait(false);
    }

    private async Task ReplaceIdentifiersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        IReadOnlyList<SecurityIdentifierDto> identifiers,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified("security_identifiers")} where security_id = @security_id;";
            delete.Parameters.AddWithValue("security_id", securityId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var identifier in identifiers)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("security_identifiers")} (
                    security_id, identifier_kind, identifier_value, normalized_identifier_value,
                    provider, normalized_provider, is_primary,
                    valid_from, valid_to, source, confidence, manual_override)
                values (
                    @security_id, @identifier_kind, @identifier_value, @normalized_identifier_value,
                    @provider, @normalized_provider, @is_primary,
                    @valid_from, @valid_to, @source, @confidence, @manual_override);
                """;
            insert.Parameters.AddWithValue("security_id", securityId);
            insert.Parameters.AddWithValue("identifier_kind", identifier.Kind.ToString());
            insert.Parameters.AddWithValue("identifier_value", identifier.Value);
            insert.Parameters.AddWithValue("normalized_identifier_value", SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier));
            insert.Parameters.AddWithValue("provider", (object?)identifier.Provider ?? DBNull.Value);
            insert.Parameters.AddWithValue("normalized_provider", ToDbNullable(SecurityIdentifierNormalizer.GetOrComputeNormalizedProvider(identifier)));
            insert.Parameters.AddWithValue("is_primary", identifier.IsPrimary);
            insert.Parameters.AddWithValue("valid_from", identifier.ValidFrom.UtcDateTime);
            insert.Parameters.AddWithValue("valid_to", (object?)identifier.ValidTo?.UtcDateTime ?? DBNull.Value);
            insert.Parameters.AddWithValue("source", "SecurityMaster");
            insert.Parameters.AddWithValue("confidence", DBNull.Value);
            insert.Parameters.AddWithValue("manual_override", false);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ReplaceAliasesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        IReadOnlyList<SecurityAliasDto> aliases,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified("security_aliases")} where security_id = @security_id;";
            delete.Parameters.AddWithValue("security_id", securityId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var alias in aliases)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("security_aliases")} (
                    alias_id, security_id, alias_kind, alias_value, normalized_alias_value,
                    provider, normalized_provider, scope, reason,
                    created_by, created_at, valid_from, valid_to, is_enabled)
                values (
                    @alias_id, @security_id, @alias_kind, @alias_value, @normalized_alias_value,
                    @provider, @normalized_provider, @scope, @reason,
                    @created_by, @created_at, @valid_from, @valid_to, @is_enabled);
                """;
            insert.Parameters.AddWithValue("alias_id", alias.AliasId);
            insert.Parameters.AddWithValue("security_id", securityId);
            insert.Parameters.AddWithValue("alias_kind", alias.AliasKind);
            insert.Parameters.AddWithValue("alias_value", alias.AliasValue);
            insert.Parameters.AddWithValue("normalized_alias_value", SecurityIdentifierNormalizer.NormalizeAliasValue(alias.AliasKind, alias.AliasValue));
            insert.Parameters.AddWithValue("provider", (object?)alias.Provider ?? DBNull.Value);
            insert.Parameters.AddWithValue("normalized_provider", ToDbNullable(SecurityIdentifierNormalizer.NormalizeProvider(alias.Provider)));
            insert.Parameters.AddWithValue("scope", alias.Scope.ToString());
            insert.Parameters.AddWithValue("reason", (object?)alias.Reason ?? DBNull.Value);
            insert.Parameters.AddWithValue("created_by", alias.CreatedBy);
            insert.Parameters.AddWithValue("created_at", alias.CreatedAt.UtcDateTime);
            insert.Parameters.AddWithValue("valid_from", alias.ValidFrom.UtcDateTime);
            insert.Parameters.AddWithValue("valid_to", (object?)alias.ValidTo?.UtcDateTime ?? DBNull.Value);
            insert.Parameters.AddWithValue("is_enabled", alias.IsEnabled);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task SaveCheckpointCoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string projectionName,
        long lastGlobalSequence,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("projection_checkpoint")} (
                projection_name, last_global_sequence, updated_at)
            values (
                @projection_name, @last_global_sequence, @updated_at)
            on conflict (projection_name) do update set
                last_global_sequence = excluded.last_global_sequence,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("projection_name", projectionName);
        command.Parameters.AddWithValue("last_global_sequence", lastGlobalSequence);
        command.Parameters.AddWithValue("updated_at", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task DeleteBondProjectionTablesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        CancellationToken ct)
    {
        foreach (var table in new[]
                 {
                     BondProjectionTable,
                     BondLifecycleProjectionTable,
                     BondAccrualConventionProjectionTable,
                     BondIssuerProjectionTable
                 })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"delete from {Qualified(table)} where security_id = @security_id;";
            command.Parameters.AddWithValue("security_id", securityId);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task DeleteOptionProjectionTablesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"delete from {Qualified(OptionContractProjectionTable)} where security_id = @security_id;";
        command.Parameters.AddWithValue("security_id", securityId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<SecurityProjectionRecord?> GetProjectionCoreAsync(NpgsqlConnection connection, Guid securityId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id,
                   asset_class,
                   status,
                   display_name,
                   currency,
                   primary_identifier_kind,
                   primary_identifier_value,
                   common_terms::text,
                   asset_specific_terms::text,
                   provenance::text,
                   version,
                   effective_from,
                   effective_to
            from {Qualified("securities")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var projection = new SecurityProjectionRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            SecurityMasterEnumReads.ParseOrFallback(reader.GetString(2), SecurityStatusDto.Unknown),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            JsonDocument.Parse(reader.GetString(7)).RootElement.Clone(),
            JsonDocument.Parse(reader.GetString(8)).RootElement.Clone(),
            JsonDocument.Parse(reader.GetString(9)).RootElement.Clone(),
            reader.GetInt64(10),
            new DateTimeOffset(reader.GetDateTime(11), TimeSpan.Zero),
            reader.IsDBNull(12) ? null : new DateTimeOffset(reader.GetDateTime(12), TimeSpan.Zero),
            Array.Empty<SecurityIdentifierDto>(),
            Array.Empty<SecurityAliasDto>());

        await reader.CloseAsync().ConfigureAwait(false);

        var identifiers = await LoadIdentifiersAsync(connection, securityId, ct).ConfigureAwait(false);
        var aliases = await LoadAliasesAsync(connection, securityId, ct).ConfigureAwait(false);
        return projection with { Identifiers = identifiers, Aliases = aliases };
    }

    private async Task<IReadOnlyList<SecurityIdentifierDto>> LoadIdentifiersAsync(NpgsqlConnection connection, Guid securityId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select identifier_kind, identifier_value, is_primary, valid_from, valid_to, provider,
                   normalized_identifier_value, normalized_provider
            from {Qualified("security_identifiers")}
            where security_id = @security_id
            order by is_primary desc, identifier_kind, identifier_value;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var results = new List<SecurityIdentifierDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // Read-tolerant: an identifier kind written by a newer node degrades to Unknown so the
            // row (and every other identifier on the security) stays readable.
            var kind = SecurityMasterEnumReads.ParseOrFallback(reader.GetString(0), SecurityIdentifierKind.Unknown);
            var value = reader.GetString(1);
            var provider = reader.IsDBNull(5) ? null : reader.GetString(5);
            var normalizedValue = reader.IsDBNull(6)
                ? SecurityIdentifierNormalizer.NormalizeValue(kind, value)
                : reader.GetString(6);
            var normalizedProvider = reader.IsDBNull(7)
                ? SecurityIdentifierNormalizer.NormalizeProvider(provider)
                : reader.GetString(7);
            results.Add(new SecurityIdentifierDto(
                kind,
                value,
                reader.GetBoolean(2),
                new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero),
                reader.IsDBNull(4) ? null : new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero),
                provider,
                normalizedValue,
                normalizedProvider));
        }

        return results;
    }

    private async Task<IReadOnlyList<SecurityAliasDto>> LoadAliasesAsync(NpgsqlConnection connection, Guid securityId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select alias_id, alias_kind, alias_value, provider, scope, reason,
                   created_by, created_at, valid_from, valid_to, is_enabled
            from {Qualified("security_aliases")}
            where security_id = @security_id
            order by alias_kind, alias_value;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var results = new List<SecurityAliasDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new SecurityAliasDto(
                reader.GetGuid(0),
                securityId,
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                SecurityMasterEnumReads.ParseOrFallback(reader.GetString(4), SecurityAliasScope.Unknown),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero),
                new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero),
                reader.IsDBNull(9) ? null : new DateTimeOffset(reader.GetDateTime(9), TimeSpan.Zero),
                reader.GetBoolean(10)));
        }

        return results;
    }

    private async Task<Guid?> ResolveSecurityIdAsync(
        NpgsqlConnection connection,
        SecurityIdentifierKind kind,
        string value,
        string? provider,
        DateTimeOffset asOfUtc,
        bool includeInactive,
        CancellationToken ct)
    {
        var identifierKind = kind.ToString();
        var normalizedIdentifierValue = SecurityIdentifierNormalizer.NormalizeValue(kind, value);
        var normalizedProvider = ToNullableString(SecurityIdentifierNormalizer.NormalizeProvider(provider));
        if (normalizedIdentifierValue.Length == 0)
        {
            return null;
        }

        foreach (var sql in new[]
        {
            $"""
            select s.security_id
            from {Qualified("security_identifiers")} i
            join {Qualified("securities")} s on s.security_id = i.security_id
            where i.identifier_kind = @identifier_kind
              and i.normalized_identifier_value = @normalized_identifier_value
              and ((@normalized_provider is null and i.normalized_provider is null) or i.normalized_provider = @normalized_provider)
              and i.valid_from <= @as_of
              and (i.valid_to is null or i.valid_to > @as_of)
              and (@include_inactive = true or s.status = 'Active')
            order by i.is_primary desc
            limit 1;
            """,
            $"""
            select s.security_id
            from {Qualified("security_aliases")} a
            join {Qualified("securities")} s on s.security_id = a.security_id
            where a.alias_kind = @identifier_kind
              and a.normalized_alias_value = @normalized_identifier_value
              and ((@normalized_provider is null and a.normalized_provider is null) or a.normalized_provider = @normalized_provider)
              and a.valid_from <= @as_of
              and (a.valid_to is null or a.valid_to > @as_of)
              and a.is_enabled = true
              and (@include_inactive = true or s.status = 'Active')
            limit 1;
            """,
            $"""
            select s.security_id
            from {Qualified("securities")} s
            where s.primary_identifier_kind = @identifier_kind
              and s.normalized_primary_identifier_value = @normalized_identifier_value
              and @normalized_provider is null
              and not exists (
                  select 1
                  from {Qualified("security_identifiers")} i
                  where i.security_id = s.security_id
                    and i.identifier_kind = @identifier_kind
                    and i.normalized_identifier_value = @normalized_identifier_value)
              and (@include_inactive = true or s.status = 'Active')
            limit 1;
            """
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("identifier_kind", identifierKind);
            command.Parameters.AddWithValue("normalized_identifier_value", normalizedIdentifierValue);
            command.Parameters.Add(new NpgsqlParameter("normalized_provider", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)normalizedProvider ?? DBNull.Value });
            command.Parameters.AddWithValue("as_of", asOfUtc.UtcDateTime);
            command.Parameters.AddWithValue("include_inactive", includeInactive);

            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (result is Guid guid)
            {
                return guid;
            }
        }

        return null;
    }

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

    private string Qualified(string table) => $"{_options.Schema}.{table}";

    private async Task UpsertBondProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BondProjectionWriteModel projection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(BondProjectionTable)} (
                security_id, issuer_name, seniority, subclass, issue_date, maturity_date,
                coupon_kind, fixed_coupon_rate, floating_rate_index, floating_spread_bps, version)
            values (
                @security_id, @issuer_name, @seniority, @subclass, @issue_date, @maturity_date,
                @coupon_kind, @fixed_coupon_rate, @floating_rate_index, @floating_spread_bps, @version)
            on conflict (security_id) do update set
                issuer_name = excluded.issuer_name,
                seniority = excluded.seniority,
                subclass = excluded.subclass,
                issue_date = excluded.issue_date,
                maturity_date = excluded.maturity_date,
                coupon_kind = excluded.coupon_kind,
                fixed_coupon_rate = excluded.fixed_coupon_rate,
                floating_rate_index = excluded.floating_rate_index,
                floating_spread_bps = excluded.floating_spread_bps,
                version = excluded.version;
            """;
        AddBondProjectionParameters(command, projection);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertBondLifecycleProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BondProjectionWriteModel projection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(BondLifecycleProjectionTable)} (
                security_id, lifecycle_stat, issue_date, call_date, maturity_date, is_callable, version,
                subclass, par, payment_frequency, legal_final_maturity, pre_refund_date, mandatory_put_date)
            values (
                @security_id, @lifecycle_stat, @issue_date, @call_date, @maturity_date, @is_callable, @version,
                @subclass, @par, @payment_frequency, @legal_final_maturity, @pre_refund_date, @mandatory_put_date)
            on conflict (security_id) do update set
                lifecycle_stat = excluded.lifecycle_stat,
                issue_date = excluded.issue_date,
                call_date = excluded.call_date,
                maturity_date = excluded.maturity_date,
                is_callable = excluded.is_callable,
                version = excluded.version,
                subclass = excluded.subclass,
                par = excluded.par,
                payment_frequency = excluded.payment_frequency,
                legal_final_maturity = excluded.legal_final_maturity,
                pre_refund_date = excluded.pre_refund_date,
                mandatory_put_date = excluded.mandatory_put_date;
            """;
        command.Parameters.AddWithValue("security_id", projection.SecurityId);
        command.Parameters.AddWithValue("lifecycle_stat", projection.LifecycleStat);
        command.Parameters.AddWithValue("issue_date", (object?)projection.IssueDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("call_date", (object?)projection.CallDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("maturity_date", projection.MaturityDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("is_callable", projection.IsCallable);
        command.Parameters.AddWithValue("version", projection.Version);
        command.Parameters.AddWithValue("subclass", (object?)projection.Subclass ?? DBNull.Value);
        command.Parameters.AddWithValue("par", (object?)projection.Par ?? DBNull.Value);
        command.Parameters.AddWithValue("payment_frequency", (object?)projection.PaymentFrequency ?? DBNull.Value);
        command.Parameters.AddWithValue("legal_final_maturity", (object?)projection.LegalFinalMaturity?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("pre_refund_date", (object?)projection.PreRefundDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("mandatory_put_date", (object?)projection.MandatoryPutDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertBondAccrualProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BondProjectionWriteModel projection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(BondAccrualConventionProjectionTable)} (
                security_id, day_count_convention, settlement_cycle_days, holiday_calendar_id,
                coupon_kind, fixed_coupon_rate, floating_rate_index, floating_spread_bps, version)
            values (
                @security_id, @day_count_convention, @settlement_cycle_days, @holiday_calendar_id,
                @coupon_kind, @fixed_coupon_rate, @floating_rate_index, @floating_spread_bps, @version)
            on conflict (security_id) do update set
                day_count_convention = excluded.day_count_convention,
                settlement_cycle_days = excluded.settlement_cycle_days,
                holiday_calendar_id = excluded.holiday_calendar_id,
                coupon_kind = excluded.coupon_kind,
                fixed_coupon_rate = excluded.fixed_coupon_rate,
                floating_rate_index = excluded.floating_rate_index,
                floating_spread_bps = excluded.floating_spread_bps,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("security_id", projection.SecurityId);
        command.Parameters.AddWithValue("day_count_convention", (object?)projection.DayCountConvention ?? DBNull.Value);
        command.Parameters.AddWithValue("settlement_cycle_days", (object?)projection.SettlementCycleDays ?? DBNull.Value);
        command.Parameters.AddWithValue("holiday_calendar_id", (object?)projection.HolidayCalendarId ?? DBNull.Value);
        command.Parameters.AddWithValue("coupon_kind", (object?)projection.CouponKind ?? DBNull.Value);
        command.Parameters.AddWithValue("fixed_coupon_rate", (object?)projection.FixedCouponRate ?? DBNull.Value);
        command.Parameters.AddWithValue("floating_rate_index", (object?)projection.FloatingRateIndex ?? DBNull.Value);
        command.Parameters.AddWithValue("floating_spread_bps", (object?)projection.FloatingSpreadBps ?? DBNull.Value);
        command.Parameters.AddWithValue("version", projection.Version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertBondIssuerProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BondProjectionWriteModel projection,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projection.IssuerName))
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified(BondIssuerProjectionTable)} where security_id = @security_id;";
            delete.Parameters.AddWithValue("security_id", projection.SecurityId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(BondIssuerProjectionTable)} (
                security_id, issuer_name, display_name, primary_identifier_value, currency, maturity_date, seniority, version)
            values (
                @security_id, @issuer_name, @display_name, @primary_identifier_value, @currency, @maturity_date, @seniority, @version)
            on conflict (security_id) do update set
                issuer_name = excluded.issuer_name,
                display_name = excluded.display_name,
                primary_identifier_value = excluded.primary_identifier_value,
                currency = excluded.currency,
                maturity_date = excluded.maturity_date,
                seniority = excluded.seniority,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("security_id", projection.SecurityId);
        command.Parameters.AddWithValue("issuer_name", projection.IssuerName!);
        command.Parameters.AddWithValue("display_name", record.DisplayName);
        command.Parameters.AddWithValue("primary_identifier_value", record.PrimaryIdentifierValue);
        command.Parameters.AddWithValue("currency", record.Currency);
        command.Parameters.AddWithValue("maturity_date", projection.MaturityDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("seniority", (object?)projection.Seniority ?? DBNull.Value);
        command.Parameters.AddWithValue("version", projection.Version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddBondProjectionParameters(NpgsqlCommand command, BondProjectionWriteModel projection)
    {
        command.Parameters.AddWithValue("security_id", projection.SecurityId);
        command.Parameters.AddWithValue("issuer_name", (object?)projection.IssuerName ?? DBNull.Value);
        command.Parameters.AddWithValue("seniority", (object?)projection.Seniority ?? DBNull.Value);
        command.Parameters.AddWithValue("subclass", (object?)projection.Subclass ?? DBNull.Value);
        command.Parameters.AddWithValue("issue_date", (object?)projection.IssueDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("maturity_date", projection.MaturityDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("coupon_kind", (object?)projection.CouponKind ?? DBNull.Value);
        command.Parameters.AddWithValue("fixed_coupon_rate", (object?)projection.FixedCouponRate ?? DBNull.Value);
        command.Parameters.AddWithValue("floating_rate_index", (object?)projection.FloatingRateIndex ?? DBNull.Value);
        command.Parameters.AddWithValue("floating_spread_bps", (object?)projection.FloatingSpreadBps ?? DBNull.Value);
        command.Parameters.AddWithValue("version", projection.Version);
    }

    internal static bool TryBuildBondProjection(SecurityProjectionRecord record, out BondProjectionWriteModel projection)
    {
        if (!TryGetOptionalDateOnly(record.AssetSpecificTerms, "maturity", out var maturityDate) || maturityDate is null)
        {
            projection = default!;
            return false;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var issueDate = GetOptionalDateOnly(record.AssetSpecificTerms, "issueDate");
        var callDate = GetOptionalDateOnly(record.AssetSpecificTerms, "callDate");
        var isCallable = GetOptionalBool(record.AssetSpecificTerms, "isCallable") ?? false;

        // The serializer (Interop.SecurityMaster.assetSpecificTermsJson) writes the coupon flat, per the
        // Bond entry in SecurityAssetTermsSchema: couponType/couponRate/floatingIndex/spreadBps/dayCount.
        // Reading a nested "coupon" object here previously left every coupon column null for canonically
        // serialized bonds (silent data loss); read the flat shape first and fall back to the legacy
        // nested object only for externally-authored payloads that still use it.
        var coupon = GetOptionalObject(record.AssetSpecificTerms, "coupon");
        var couponKind = GetOptionalString(record.AssetSpecificTerms, "couponType")
            ?? (coupon.HasValue ? GetOptionalString(coupon.Value, "kind") : null);
        var fixedCouponRate = GetOptionalDecimal(record.AssetSpecificTerms, "couponRate")
            ?? (coupon.HasValue ? GetOptionalDecimal(coupon.Value, "rate") : null);
        var floatingRateIndex = GetOptionalString(record.AssetSpecificTerms, "floatingIndex")
            ?? (coupon.HasValue ? GetOptionalString(coupon.Value, "index") : null);
        var floatingSpreadBps = GetOptionalDecimal(record.AssetSpecificTerms, "spreadBps")
            ?? (coupon.HasValue ? GetOptionalDecimal(coupon.Value, "spreadBps") : null);
        var dayCountConvention = GetOptionalString(record.AssetSpecificTerms, "dayCount")
            ?? (coupon.HasValue ? GetOptionalString(coupon.Value, "dayCountConvention") : null);

        var lifecycleStat =
            record.EffectiveTo.HasValue ? "Retired" :
            maturityDate.Value < today ? "Matured" :
            isCallable && callDate.HasValue && callDate.Value <= today ? "Callable" :
            issueDate.HasValue && issueDate.Value > today ? "WhenIssued" :
            "Active";

        projection = new BondProjectionWriteModel(
            record.SecurityId,
            GetOptionalString(record.AssetSpecificTerms, "issuerName") ?? GetOptionalString(record.CommonTerms, "issuerName"),
            GetOptionalString(record.AssetSpecificTerms, "seniority"),
            GetOptionalString(record.AssetSpecificTerms, "subclass"),
            issueDate,
            maturityDate.Value,
            callDate,
            isCallable,
            lifecycleStat,
            couponKind,
            fixedCouponRate,
            floatingRateIndex,
            floatingSpreadBps,
            dayCountConvention,
            GetOptionalInt(record.CommonTerms, "settlementCycleDays"),
            GetOptionalString(record.CommonTerms, "holidayCalendarId"),
            record.Version,
            GetOptionalDecimal(record.AssetSpecificTerms, "par"),
            GetOptionalString(record.AssetSpecificTerms, "paymentFrequency"),
            GetOptionalDateOnly(record.AssetSpecificTerms, "legalFinalMaturity"),
            GetOptionalDateOnly(record.AssetSpecificTerms, "preRefundDate"),
            GetOptionalDateOnly(record.AssetSpecificTerms, "mandatoryPutDate"));
        return true;
    }

    private async Task UpsertOptionContractProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OptionProjectionWriteModel projection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(OptionContractProjectionTable)} (
                contract_symbol, security_id, option_chain_id, underlying_symbol, underlying_security_id,
                put_call, strike, expiry_date, multiplier, is_adjusted, last_trading_dt, lifecycle_stat, updated_at, version)
            values (
                @contract_symbol, @security_id, @option_chain_id, @underlying_symbol, @underlying_security_id,
                @put_call, @strike, @expiry_date, @multiplier, @is_adjusted, @last_trading_dt, @lifecycle_stat, @updated_at, @version)
            on conflict (contract_symbol) do update set
                security_id = excluded.security_id,
                option_chain_id = excluded.option_chain_id,
                underlying_symbol = excluded.underlying_symbol,
                underlying_security_id = excluded.underlying_security_id,
                put_call = excluded.put_call,
                strike = excluded.strike,
                expiry_date = excluded.expiry_date,
                multiplier = excluded.multiplier,
                is_adjusted = excluded.is_adjusted,
                last_trading_dt = excluded.last_trading_dt,
                lifecycle_stat = excluded.lifecycle_stat,
                updated_at = excluded.updated_at,
                version = excluded.version;
            """;
        AddOptionProjectionParameters(command, projection);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertOptionSeriesProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OptionProjectionWriteModel projection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(OptionSeriesProjectionTable)} (
                option_chain_id, contract_symbol, underlying_symbol, expiry_date, strike, put_call)
            values (
                @option_chain_id, @contract_symbol, @underlying_symbol, @expiry_date, @strike, @put_call)
            on conflict (option_chain_id, contract_symbol) do update set
                underlying_symbol = excluded.underlying_symbol,
                expiry_date = excluded.expiry_date,
                strike = excluded.strike,
                put_call = excluded.put_call;
            """;
        command.Parameters.AddWithValue("option_chain_id", projection.OptionChainId);
        command.Parameters.AddWithValue("contract_symbol", projection.ContractSymbol);
        command.Parameters.AddWithValue("underlying_symbol", projection.UnderlyingSymbol);
        command.Parameters.AddWithValue("expiry_date", projection.ExpiryDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("strike", projection.Strike);
        command.Parameters.AddWithValue("put_call", projection.PutCall);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertOptionLifecycleProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OptionProjectionWriteModel projection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(OptionLifecycleProjectionTable)} (
                contract_symbol, lifecycle_stat, listed_at, last_trading_dt, expiry_date, version)
            values (
                @contract_symbol, @lifecycle_stat, @listed_at, @last_trading_dt, @expiry_date, @version)
            on conflict (contract_symbol) do update set
                lifecycle_stat = excluded.lifecycle_stat,
                last_trading_dt = excluded.last_trading_dt,
                expiry_date = excluded.expiry_date,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("contract_symbol", projection.ContractSymbol);
        command.Parameters.AddWithValue("lifecycle_stat", projection.LifecycleStat);
        command.Parameters.AddWithValue("listed_at", projection.ListedAt.UtcDateTime);
        command.Parameters.AddWithValue("last_trading_dt", (object?)projection.LastTradingDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("expiry_date", projection.ExpiryDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("version", projection.Version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertOptionAliasProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OptionProjectionWriteModel projection,
        IReadOnlyList<SecurityAliasDto> aliases,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified(OptionAliasProjectionTable)} where contract_symbol = @contract_symbol;";
            delete.Parameters.AddWithValue("contract_symbol", projection.ContractSymbol);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var alias in aliases)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified(OptionAliasProjectionTable)} (
                    contract_symbol, alias_kind, alias_value, provider, normalized_alias)
                values (
                    @contract_symbol, @alias_kind, @alias_value, @provider, @normalized_alias);
                """;
            insert.Parameters.AddWithValue("contract_symbol", projection.ContractSymbol);
            insert.Parameters.AddWithValue("alias_kind", alias.AliasKind);
            insert.Parameters.AddWithValue("alias_value", alias.AliasValue);
            insert.Parameters.AddWithValue("provider", alias.Provider ?? string.Empty);
            insert.Parameters.AddWithValue("normalized_alias", alias.AliasValue.Trim().ToUpperInvariant());
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static void AddOptionProjectionParameters(NpgsqlCommand command, OptionProjectionWriteModel projection)
    {
        command.Parameters.AddWithValue("contract_symbol", projection.ContractSymbol);
        command.Parameters.AddWithValue("security_id", projection.SecurityId);
        command.Parameters.AddWithValue("option_chain_id", projection.OptionChainId);
        command.Parameters.AddWithValue("underlying_symbol", projection.UnderlyingSymbol);
        command.Parameters.AddWithValue("underlying_security_id", (object?)projection.UnderlyingSecurityId ?? DBNull.Value);
        command.Parameters.AddWithValue("put_call", projection.PutCall);
        command.Parameters.AddWithValue("strike", projection.Strike);
        command.Parameters.AddWithValue("expiry_date", projection.ExpiryDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("multiplier", projection.Multiplier);
        command.Parameters.AddWithValue("is_adjusted", projection.IsAdjusted);
        command.Parameters.AddWithValue("last_trading_dt", (object?)projection.LastTradingDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("lifecycle_stat", projection.LifecycleStat);
        command.Parameters.AddWithValue("updated_at", projection.ListedAt.UtcDateTime);
        command.Parameters.AddWithValue("version", projection.Version);
    }

    private static bool TryBuildOptionProjection(SecurityProjectionRecord record, out OptionProjectionWriteModel projection)
    {
        if (!TryGetOptionalDateOnly(record.AssetSpecificTerms, "expiry", out var expiryDate) ||
            expiryDate is null)
        {
            projection = default!;
            return false;
        }

        var contractSymbol = record.PrimaryIdentifierValue.Trim().ToUpperInvariant();
        var underlyingSymbol = GetOptionalString(record.AssetSpecificTerms, "underlyingSymbol")
            ?? GetOptionalString(record.CommonTerms, "underlyingSymbol")
            ?? "UNKNOWN";

        var putCall = GetOptionalString(record.AssetSpecificTerms, "putCall") ?? "Call";
        var strike = GetOptionalDecimal(record.AssetSpecificTerms, "strike");
        var multiplier = GetOptionalDecimal(record.AssetSpecificTerms, "multiplier");

        if (strike is null || multiplier is null)
        {
            projection = default!;
            return false;
        }

        Guid? underlyingSecurityId = null;
        if (record.AssetSpecificTerms.TryGetProperty("underlyingId", out var underlyingIdEl) &&
            underlyingIdEl.ValueKind == JsonValueKind.String &&
            Guid.TryParse(underlyingIdEl.GetString(), out var parsed))
        {
            underlyingSecurityId = parsed;
        }

        var optionChainId = GetOptionalString(record.AssetSpecificTerms, "optChainId");
        if (string.IsNullOrWhiteSpace(optionChainId))
        {
            optionChainId = BuildOptionChainId(underlyingSymbol, expiryDate.Value);
        }

        var lastTradingDate = GetOptionalDateOnly(record.AssetSpecificTerms, "lastTradingDt") ?? expiryDate;
        var lifecycleStat =
            record.EffectiveTo.HasValue ? "Retired" :
            expiryDate.Value < DateOnly.FromDateTime(DateTime.UtcNow) ? "Expired" :
            "Active";

        projection = new OptionProjectionWriteModel(
            contractSymbol,
            record.SecurityId,
            optionChainId!,
            underlyingSymbol.Trim().ToUpperInvariant(),
            underlyingSecurityId,
            putCall,
            strike.Value,
            expiryDate.Value,
            multiplier.Value,
            GetOptionalBool(record.AssetSpecificTerms, "isAdjusted") ?? false,
            lastTradingDate,
            lifecycleStat,
            record.EffectiveFrom,
            record.Version);
        return true;
    }

    private static string BuildOptionChainId(string underlyingSymbol, DateOnly expiryDate)
        => $"{underlyingSymbol.Trim().ToUpperInvariant()}-{expiryDate:yyyyMMdd}";

    private async Task UpsertEquityProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        if (!string.Equals(record.AssetClass, "Equity", StringComparison.OrdinalIgnoreCase))
        {
            await using var del = connection.CreateCommand();
            del.Transaction = transaction;
            del.CommandText = $"delete from {Qualified(EquityProjectionTable)} where security_id = @security_id;";
            del.Parameters.AddWithValue("security_id", record.SecurityId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(EquityProjectionTable)} (
                security_id, display_name, currency, share_class, voting_rights_cat,
                classification, exchange_code, country_of_risk, issuer_name,
                primary_identifier_value, version)
            values (
                @security_id, @display_name, @currency, @share_class, @voting_rights_cat,
                @classification, @exchange_code, @country_of_risk, @issuer_name,
                @primary_identifier_value, @version)
            on conflict (security_id) do update set
                display_name = excluded.display_name,
                currency = excluded.currency,
                share_class = excluded.share_class,
                voting_rights_cat = excluded.voting_rights_cat,
                classification = excluded.classification,
                exchange_code = excluded.exchange_code,
                country_of_risk = excluded.country_of_risk,
                issuer_name = excluded.issuer_name,
                primary_identifier_value = excluded.primary_identifier_value,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("security_id", record.SecurityId);
        command.Parameters.AddWithValue("display_name", record.DisplayName);
        command.Parameters.AddWithValue("currency", record.Currency ?? string.Empty);
        command.Parameters.AddWithValue("share_class", (object?)GetOptionalString(record.AssetSpecificTerms, "shareClass") ?? DBNull.Value);
        command.Parameters.AddWithValue("voting_rights_cat", (object?)GetOptionalString(record.AssetSpecificTerms, "votingRightsCat") ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "classification",
            (object?)SecurityTermReader.ReadEquityClassification(record.AssetSpecificTerms) ?? DBNull.Value);
        command.Parameters.AddWithValue("exchange_code", (object?)GetOptionalString(record.CommonTerms, "exchange") ?? DBNull.Value);
        command.Parameters.AddWithValue("country_of_risk", (object?)GetOptionalString(record.CommonTerms, "countryOfRisk") ?? DBNull.Value);
        command.Parameters.AddWithValue("issuer_name", (object?)GetOptionalString(record.CommonTerms, "issuerName") ?? DBNull.Value);
        command.Parameters.AddWithValue("primary_identifier_value", record.PrimaryIdentifierValue);
        command.Parameters.AddWithValue("version", record.Version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertFutureProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        if (!string.Equals(record.AssetClass, "Future", StringComparison.OrdinalIgnoreCase) ||
            !TryGetOptionalDateOnly(record.AssetSpecificTerms, "expiry", out var expiryDate) ||
            expiryDate is null)
        {
            await using var del = connection.CreateCommand();
            del.Transaction = transaction;
            del.CommandText = $"delete from {Qualified(FutureProjectionTable)} where security_id = @security_id;";
            del.Parameters.AddWithValue("security_id", record.SecurityId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        var rootSymbol = (GetOptionalString(record.AssetSpecificTerms, "rootSymbol")
            ?? record.PrimaryIdentifierValue.Trim().ToUpperInvariant());
        var contractMonth = GetOptionalString(record.AssetSpecificTerms, "contractMonth") ?? string.Empty;
        var multiplier = GetOptionalDecimal(record.AssetSpecificTerms, "multiplier") ?? 1m;
        var isRollTarget = GetOptionalBool(record.AssetSpecificTerms, "isRollTarget") ?? false;
        var lastTradingDate = GetOptionalDateOnly(record.AssetSpecificTerms, "lastTradingDt") ?? expiryDate;
        var lifecycleStat =
            record.EffectiveTo.HasValue ? "Retired" :
            expiryDate.Value < DateOnly.FromDateTime(DateTime.UtcNow) ? "Expired" :
            isRollTarget ? "RollTarget" :
            "Active";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(FutureProjectionTable)} (
                security_id, display_name, currency, root_symbol, contract_month, expiry_date,
                multiplier, settlement_type, is_roll_target, roll_window_days, last_trading_dt,
                first_notice_dt, delivery_month_dt, delivery_location_code, lifecycle_stat,
                primary_identifier_value, version)
            values (
                @security_id, @display_name, @currency, @root_symbol, @contract_month, @expiry_date,
                @multiplier, @settlement_type, @is_roll_target, @roll_window_days, @last_trading_dt,
                @first_notice_dt, @delivery_month_dt, @delivery_location_code, @lifecycle_stat,
                @primary_identifier_value, @version)
            on conflict (security_id) do update set
                display_name = excluded.display_name,
                currency = excluded.currency,
                root_symbol = excluded.root_symbol,
                contract_month = excluded.contract_month,
                expiry_date = excluded.expiry_date,
                multiplier = excluded.multiplier,
                settlement_type = excluded.settlement_type,
                is_roll_target = excluded.is_roll_target,
                roll_window_days = excluded.roll_window_days,
                last_trading_dt = excluded.last_trading_dt,
                first_notice_dt = excluded.first_notice_dt,
                delivery_month_dt = excluded.delivery_month_dt,
                delivery_location_code = excluded.delivery_location_code,
                lifecycle_stat = excluded.lifecycle_stat,
                primary_identifier_value = excluded.primary_identifier_value,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("security_id", record.SecurityId);
        command.Parameters.AddWithValue("display_name", record.DisplayName);
        command.Parameters.AddWithValue("currency", record.Currency ?? string.Empty);
        command.Parameters.AddWithValue("root_symbol", rootSymbol.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("contract_month", contractMonth);
        command.Parameters.AddWithValue("expiry_date", expiryDate.Value.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("multiplier", multiplier);
        command.Parameters.AddWithValue("settlement_type", (object?)GetOptionalString(record.AssetSpecificTerms, "settlementType") ?? DBNull.Value);
        command.Parameters.AddWithValue("is_roll_target", isRollTarget);
        command.Parameters.AddWithValue("roll_window_days", (object?)GetOptionalInt(record.AssetSpecificTerms, "rollWindowDays") ?? DBNull.Value);
        command.Parameters.AddWithValue("last_trading_dt", (object?)lastTradingDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("first_notice_dt", (object?)GetOptionalDateOnly(record.AssetSpecificTerms, "firstNoticeDt")?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("delivery_month_dt", (object?)GetOptionalDateOnly(record.AssetSpecificTerms, "deliveryMonthDt")?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("delivery_location_code", (object?)GetOptionalString(record.AssetSpecificTerms, "deliveryLocationCode") ?? DBNull.Value);
        command.Parameters.AddWithValue("lifecycle_stat", lifecycleStat);
        command.Parameters.AddWithValue("primary_identifier_value", record.PrimaryIdentifierValue);
        command.Parameters.AddWithValue("version", record.Version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertFxSpotProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        var baseCurrency = GetOptionalString(record.AssetSpecificTerms, "baseCurrency");
        var quoteCurrency = GetOptionalString(record.AssetSpecificTerms, "quoteCurrency");

        if (!string.Equals(record.AssetClass, "FxSpot", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(baseCurrency) ||
            string.IsNullOrWhiteSpace(quoteCurrency))
        {
            await using var del = connection.CreateCommand();
            del.Transaction = transaction;
            del.CommandText = $"delete from {Qualified(FxSpotProjectionTable)} where security_id = @security_id;";
            del.Parameters.AddWithValue("security_id", record.SecurityId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        var base_ = baseCurrency.Trim().ToUpperInvariant();
        var quote = quoteCurrency.Trim().ToUpperInvariant();
        var pairCode = $"{base_}{quote}";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(FxSpotProjectionTable)} (
                security_id, display_name, base_currency, quote_currency, pair_code,
                primary_identifier_value, version)
            values (
                @security_id, @display_name, @base_currency, @quote_currency, @pair_code,
                @primary_identifier_value, @version)
            on conflict (security_id) do update set
                display_name = excluded.display_name,
                base_currency = excluded.base_currency,
                quote_currency = excluded.quote_currency,
                pair_code = excluded.pair_code,
                primary_identifier_value = excluded.primary_identifier_value,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("security_id", record.SecurityId);
        command.Parameters.AddWithValue("display_name", record.DisplayName);
        command.Parameters.AddWithValue("base_currency", base_);
        command.Parameters.AddWithValue("quote_currency", quote);
        command.Parameters.AddWithValue("pair_code", pairCode);
        command.Parameters.AddWithValue("primary_identifier_value", record.PrimaryIdentifierValue);
        command.Parameters.AddWithValue("version", record.Version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }


    private async Task UpsertSwapProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        if (!string.Equals(record.AssetClass, "Swap", StringComparison.OrdinalIgnoreCase) ||
            !TryBuildSwapProjection(record, out var projection))
        {
            await using var del = connection.CreateCommand();
            del.Transaction = transaction;
            del.CommandText = $"delete from {Qualified(SwapProjectionTable)} where security_id = @security_id;";
            del.Parameters.AddWithValue("security_id", record.SecurityId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(SwapProjectionTable)} (
                security_id, display_name, currency, swap_type,
                effective_date, maturity_date, lifecycle_stat,
                primary_identifier_value, version)
            values (
                @security_id, @display_name, @currency, @swap_type,
                @effective_date, @maturity_date, @lifecycle_stat,
                @primary_identifier_value, @version)
            on conflict (security_id) do update set
                display_name = excluded.display_name,
                currency = excluded.currency,
                swap_type = excluded.swap_type,
                effective_date = excluded.effective_date,
                maturity_date = excluded.maturity_date,
                lifecycle_stat = excluded.lifecycle_stat,
                primary_identifier_value = excluded.primary_identifier_value,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("security_id", projection.SecurityId);
        command.Parameters.AddWithValue("display_name", projection.DisplayName);
        command.Parameters.AddWithValue("currency", projection.Currency);
        command.Parameters.AddWithValue("swap_type", (object?)projection.SwapType ?? DBNull.Value);
        command.Parameters.AddWithValue("effective_date", projection.EffectiveDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("maturity_date", projection.MaturityDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("lifecycle_stat", projection.LifecycleStat);
        command.Parameters.AddWithValue("primary_identifier_value", projection.PrimaryIdentifierValue);
        command.Parameters.AddWithValue("version", projection.Version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    internal static bool TryBuildSwapProjection(SecurityProjectionRecord record, out SwapProjectionWriteModel projection)
    {
        var effectiveDate = GetOptionalDateOnly(record.AssetSpecificTerms, "effectiveDate");
        var maturityDate = GetOptionalDateOnly(record.AssetSpecificTerms, "maturityDate");
        if (effectiveDate is null || maturityDate is null)
        {
            projection = default!;
            return false;
        }

        projection = new SwapProjectionWriteModel(
            record.SecurityId,
            record.DisplayName,
            record.Currency,
            DeriveSwapType(record.AssetSpecificTerms),
            effectiveDate.Value,
            maturityDate.Value,
            "Active",
            record.PrimaryIdentifierValue,
            record.Version);
        return true;
    }

    /// <summary>
    /// Resolves the queryable <c>swap_type</c> from the swap terms. The serializer emits swap economics
    /// as <c>legs</c> (per the Swap entry in <see cref="SecurityAssetTermsSchema"/>) and never a
    /// <c>swapType</c> field, so reading only <c>swapType</c> left the column null for every canonically
    /// serialized swap; derive it from the leg types instead, honouring an explicit <c>swapType</c> when a
    /// non-canonical payload supplies one.
    /// </summary>
    internal static string? DeriveSwapType(JsonElement assetSpecificTerms)
    {
        var explicitType = GetOptionalString(assetSpecificTerms, "swapType");
        if (!string.IsNullOrWhiteSpace(explicitType))
        {
            return explicitType;
        }

        if (!assetSpecificTerms.TryGetProperty("legs", out var legs) || legs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var legTypes = new List<string>();
        var hasFixed = false;
        var hasFloating = false;
        foreach (var leg in legs.EnumerateArray())
        {
            var legType = GetOptionalString(leg, "legType");
            if (string.IsNullOrWhiteSpace(legType))
            {
                continue;
            }

            legType = legType.Trim();
            var alreadySeen = false;
            foreach (var seen in legTypes)
            {
                if (string.Equals(seen, legType, StringComparison.OrdinalIgnoreCase))
                {
                    alreadySeen = true;
                    break;
                }
            }

            if (!alreadySeen)
            {
                legTypes.Add(legType);
            }

            if (string.Equals(legType, "Fixed", StringComparison.OrdinalIgnoreCase))
            {
                hasFixed = true;
            }
            else if (string.Equals(legType, "Floating", StringComparison.OrdinalIgnoreCase))
            {
                hasFloating = true;
            }
        }

        if (legTypes.Count == 0)
        {
            return null;
        }

        if (hasFixed && hasFloating)
        {
            return "FixedFloat";
        }

        if (hasFloating && !hasFixed && legTypes.Count == 1)
        {
            return "BasisFloat";
        }

        return string.Join("/", legTypes);
    }

    private async Task UpsertCommodityProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        var commodityType = GetOptionalString(record.AssetSpecificTerms, "commodityType");

        if (!string.Equals(record.AssetClass, "Commodity", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(commodityType))
        {
            await using var del = connection.CreateCommand();
            del.Transaction = transaction;
            del.CommandText = $"delete from {Qualified(CommodityProjectionTable)} where security_id = @security_id;";
            del.Parameters.AddWithValue("security_id", record.SecurityId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        var denomination = GetOptionalString(record.AssetSpecificTerms, "denomination");
        var contractSize = GetOptionalDecimal(record.AssetSpecificTerms, "contractSize");
        var exchangeCode = GetOptionalString(record.AssetSpecificTerms, "exchangeCode");
        var deliveryCountry = GetOptionalString(record.AssetSpecificTerms, "deliveryCountry");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(CommodityProjectionTable)} (
                security_id, display_name, currency, commodity_type, denomination,
                contract_size, exchange_code, delivery_country,
                primary_identifier_value, version)
            values (
                @security_id, @display_name, @currency, @commodity_type, @denomination,
                @contract_size, @exchange_code, @delivery_country,
                @primary_identifier_value, @version)
            on conflict (security_id) do update set
                display_name = excluded.display_name,
                currency = excluded.currency,
                commodity_type = excluded.commodity_type,
                denomination = excluded.denomination,
                contract_size = excluded.contract_size,
                exchange_code = excluded.exchange_code,
                delivery_country = excluded.delivery_country,
                primary_identifier_value = excluded.primary_identifier_value,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("security_id", record.SecurityId);
        command.Parameters.AddWithValue("display_name", record.DisplayName);
        command.Parameters.AddWithValue("currency", record.Currency);
        command.Parameters.AddWithValue("commodity_type", commodityType.Trim());
        command.Parameters.AddWithValue("denomination", (object?)denomination ?? DBNull.Value);
        command.Parameters.AddWithValue("contract_size", (object?)contractSize ?? DBNull.Value);
        command.Parameters.AddWithValue("exchange_code", (object?)exchangeCode ?? DBNull.Value);
        command.Parameters.AddWithValue("delivery_country", (object?)deliveryCountry ?? DBNull.Value);
        command.Parameters.AddWithValue("primary_identifier_value", record.PrimaryIdentifierValue);
        command.Parameters.AddWithValue("version", record.Version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertCryptoProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        var baseCurrency = GetOptionalString(record.AssetSpecificTerms, "baseCurrency");
        var quoteCurrency = GetOptionalString(record.AssetSpecificTerms, "quoteCurrency");

        if (!string.Equals(record.AssetClass, "CryptoCurrency", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(baseCurrency) ||
            string.IsNullOrWhiteSpace(quoteCurrency))
        {
            await using var del = connection.CreateCommand();
            del.Transaction = transaction;
            del.CommandText = $"delete from {Qualified(CryptoProjectionTable)} where security_id = @security_id;";
            del.Parameters.AddWithValue("security_id", record.SecurityId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        var network = GetOptionalString(record.AssetSpecificTerms, "network");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(CryptoProjectionTable)} (
                security_id, display_name, base_currency, quote_currency, network,
                primary_identifier_value, version)
            values (
                @security_id, @display_name, @base_currency, @quote_currency, @network,
                @primary_identifier_value, @version)
            on conflict (security_id) do update set
                display_name = excluded.display_name,
                base_currency = excluded.base_currency,
                quote_currency = excluded.quote_currency,
                network = excluded.network,
                primary_identifier_value = excluded.primary_identifier_value,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("security_id", record.SecurityId);
        command.Parameters.AddWithValue("display_name", record.DisplayName);
        command.Parameters.AddWithValue("base_currency", baseCurrency.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("quote_currency", quoteCurrency.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("network", (object?)network ?? DBNull.Value);
        command.Parameters.AddWithValue("primary_identifier_value", record.PrimaryIdentifierValue);
        command.Parameters.AddWithValue("version", record.Version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertDepositProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        var depositType = GetOptionalString(record.AssetSpecificTerms, "depositType");
        var institutionName = GetOptionalString(record.AssetSpecificTerms, "institutionName");

        if (!string.Equals(record.AssetClass, "Deposit", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(depositType) ||
            string.IsNullOrWhiteSpace(institutionName))
        {
            await using var del = connection.CreateCommand();
            del.Transaction = transaction;
            del.CommandText = $"delete from {Qualified(DepositProjectionTable)} where security_id = @security_id;";
            del.Parameters.AddWithValue("security_id", record.SecurityId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        var maturity = GetOptionalDateOnly(record.AssetSpecificTerms, "maturity");
        var interestRate = GetOptionalDecimal(record.AssetSpecificTerms, "interestRate");
        var dayCount = GetOptionalString(record.AssetSpecificTerms, "dayCount");
        var isCallable = GetOptionalBool(record.AssetSpecificTerms, "isCallable") ?? false;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(DepositProjectionTable)} (
                security_id, display_name, currency, deposit_type, institution_name,
                maturity_date, interest_rate, day_count, is_callable,
                primary_identifier_value, version)
            values (
                @security_id, @display_name, @currency, @deposit_type, @institution_name,
                @maturity_date, @interest_rate, @day_count, @is_callable,
                @primary_identifier_value, @version)
            on conflict (security_id) do update set
                display_name = excluded.display_name,
                currency = excluded.currency,
                deposit_type = excluded.deposit_type,
                institution_name = excluded.institution_name,
                maturity_date = excluded.maturity_date,
                interest_rate = excluded.interest_rate,
                day_count = excluded.day_count,
                is_callable = excluded.is_callable,
                primary_identifier_value = excluded.primary_identifier_value,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("security_id", record.SecurityId);
        command.Parameters.AddWithValue("display_name", record.DisplayName);
        command.Parameters.AddWithValue("currency", record.Currency);
        command.Parameters.AddWithValue("deposit_type", depositType.Trim());
        command.Parameters.AddWithValue("institution_name", institutionName.Trim());
        command.Parameters.AddWithValue("maturity_date", maturity.HasValue ? (object)maturity.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        command.Parameters.AddWithValue("interest_rate", (object?)interestRate ?? DBNull.Value);
        command.Parameters.AddWithValue("day_count", (object?)dayCount ?? DBNull.Value);
        command.Parameters.AddWithValue("is_callable", isCallable);
        command.Parameters.AddWithValue("primary_identifier_value", record.PrimaryIdentifierValue);
        command.Parameters.AddWithValue("version", record.Version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertMoneyMarketFundProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        if (!string.Equals(record.AssetClass, "MoneyMarketFund", StringComparison.OrdinalIgnoreCase))
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = $"""delete from {Qualified(MoneyMarketFundProjectionTable)} where security_id = @security_id;""";
            delete.Parameters.AddWithValue("security_id", record.SecurityId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        var fundFamily = GetOptionalString(record.AssetSpecificTerms, "fundFamily");
        var sweepEligible = GetOptionalBool(record.AssetSpecificTerms, "sweepEligible") ?? false;
        var weightedAverageMaturityDays = GetOptionalInt(record.AssetSpecificTerms, "weightedAverageMaturityDays");
        var liquidityFeeEligible = GetOptionalBool(record.AssetSpecificTerms, "liquidityFeeEligible") ?? false;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(MoneyMarketFundProjectionTable)} (
                security_id, display_name, currency, fund_family, sweep_eligible,
                weighted_average_maturity_days, liquidity_fee_eligible, primary_identifier_value, version)
            values (
                @security_id, @display_name, @currency, @fund_family, @sweep_eligible,
                @weighted_average_maturity_days, @liquidity_fee_eligible, @primary_identifier_value, @version)
            on conflict (security_id) do update set
                display_name = excluded.display_name,
                currency = excluded.currency,
                fund_family = excluded.fund_family,
                sweep_eligible = excluded.sweep_eligible,
                weighted_average_maturity_days = excluded.weighted_average_maturity_days,
                liquidity_fee_eligible = excluded.liquidity_fee_eligible,
                primary_identifier_value = excluded.primary_identifier_value,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("security_id", record.SecurityId);
        command.Parameters.AddWithValue("display_name", record.DisplayName);
        command.Parameters.AddWithValue("currency", record.Currency);
        command.Parameters.AddWithValue("fund_family", (object?)fundFamily?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("sweep_eligible", sweepEligible);
        command.Parameters.AddWithValue("weighted_average_maturity_days", (object?)weightedAverageMaturityDays ?? DBNull.Value);
        command.Parameters.AddWithValue("liquidity_fee_eligible", liquidityFeeEligible);
        command.Parameters.AddWithValue("primary_identifier_value", record.PrimaryIdentifierValue);
        command.Parameters.AddWithValue("version", record.Version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertCertificateOfDepositProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        var issuerName = GetOptionalString(record.AssetSpecificTerms, "issuerName");
        var maturity = GetOptionalDateOnly(record.AssetSpecificTerms, "maturity");

        if (!string.Equals(record.AssetClass, "CertificateOfDeposit", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(issuerName) ||
            !maturity.HasValue)
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = $"""delete from {Qualified(CertificateOfDepositProjectionTable)} where security_id = @security_id;""";
            delete.Parameters.AddWithValue("security_id", record.SecurityId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        var couponRate = GetOptionalDecimal(record.AssetSpecificTerms, "couponRate");
        var callableDate = GetOptionalDateOnly(record.AssetSpecificTerms, "callableDate");
        var dayCount = GetOptionalString(record.AssetSpecificTerms, "dayCount");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(CertificateOfDepositProjectionTable)} (
                security_id, display_name, currency, issuer_name, maturity_date,
                coupon_rate, callable_date, day_count, primary_identifier_value, version)
            values (
                @security_id, @display_name, @currency, @issuer_name, @maturity_date,
                @coupon_rate, @callable_date, @day_count, @primary_identifier_value, @version)
            on conflict (security_id) do update set
                display_name = excluded.display_name,
                currency = excluded.currency,
                issuer_name = excluded.issuer_name,
                maturity_date = excluded.maturity_date,
                coupon_rate = excluded.coupon_rate,
                callable_date = excluded.callable_date,
                day_count = excluded.day_count,
                primary_identifier_value = excluded.primary_identifier_value,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("security_id", record.SecurityId);
        command.Parameters.AddWithValue("display_name", record.DisplayName);
        command.Parameters.AddWithValue("currency", record.Currency);
        command.Parameters.AddWithValue("issuer_name", issuerName.Trim());
        command.Parameters.AddWithValue("maturity_date", maturity.Value.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("coupon_rate", (object?)couponRate ?? DBNull.Value);
        command.Parameters.AddWithValue("callable_date", callableDate.HasValue ? (object)callableDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        command.Parameters.AddWithValue("day_count", (object?)dayCount ?? DBNull.Value);
        command.Parameters.AddWithValue("primary_identifier_value", record.PrimaryIdentifierValue);
        command.Parameters.AddWithValue("version", record.Version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string? GetOptionalString(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string NormalizePrimaryIdentifierValue(SecurityProjectionRecord record)
        => Enum.TryParse<SecurityIdentifierKind>(record.PrimaryIdentifierKind, ignoreCase: true, out var kind)
            ? SecurityIdentifierNormalizer.NormalizeValue(kind, record.PrimaryIdentifierValue)
            : SecurityIdentifierNormalizer.NormalizeAliasValue(record.PrimaryIdentifierKind, record.PrimaryIdentifierValue);

    private static object ToDbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static string? ToNullableString(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static decimal? GetOptionalDecimal(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.TryGetDecimal(out var decimalValue)
            ? decimalValue
            : null;

    private static int? GetOptionalInt(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var intValue)
            ? intValue
            : null;

    private static bool? GetOptionalBool(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static JsonElement? GetOptionalObject(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static DateOnly? GetOptionalDateOnly(JsonElement json, string propertyName)
        => TryGetOptionalDateOnly(json, propertyName, out var value) ? value : null;

    private static bool TryGetOptionalDateOnly(JsonElement json, string propertyName, out DateOnly? value)
    {
        if (json.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.String &&
            DateOnly.TryParse(element.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    internal sealed record BondProjectionWriteModel(
        Guid SecurityId,
        string? IssuerName,
        string? Seniority,
        string? Subclass,
        DateOnly? IssueDate,
        DateOnly MaturityDate,
        DateOnly? CallDate,
        bool IsCallable,
        string LifecycleStat,
        string? CouponKind,
        decimal? FixedCouponRate,
        string? FloatingRateIndex,
        decimal? FloatingSpreadBps,
        string? DayCountConvention,
        int? SettlementCycleDays,
        string? HolidayCalendarId,
        long Version,
        // Clearwater extended lifecycle fields.
        decimal? Par,
        string? PaymentFrequency,
        DateOnly? LegalFinalMaturity,
        DateOnly? PreRefundDate,
        DateOnly? MandatoryPutDate);

    private sealed record OptionProjectionWriteModel(
        string ContractSymbol,
        Guid SecurityId,
        string OptionChainId,
        string UnderlyingSymbol,
        Guid? UnderlyingSecurityId,
        string PutCall,
        decimal Strike,
        DateOnly ExpiryDate,
        decimal Multiplier,
        bool IsAdjusted,
        DateOnly? LastTradingDate,
        string LifecycleStat,
        DateTimeOffset ListedAt,
        long Version);

    internal sealed record SwapProjectionWriteModel(
        Guid SecurityId,
        string DisplayName,
        string Currency,
        string? SwapType,
        DateOnly EffectiveDate,
        DateOnly MaturityDate,
        string LifecycleStat,
        string PrimaryIdentifierValue,
        long Version);
}
