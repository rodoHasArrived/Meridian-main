using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Npgsql;

namespace Meridian.Storage.FundStructure;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IFundStructureStore"/>.
/// Array/list fields are stored as JSONB. All writes use INSERT … ON CONFLICT DO UPDATE.
/// </summary>
public sealed class PostgresFundStructureStore : IFundStructureStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly FundStructureStoreOptions _options;

    public PostgresFundStructureStore(FundStructureStoreOptions options) => _options = options;

    private string Q(string table) => $"{_options.Schema}.{table}";

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    private static List<Guid> DeserializeGuids(string json) =>
        JsonSerializer.Deserialize<List<Guid>>(json, JsonOpts) ?? [];

    private static List<T> DeserializeList<T>(string json) =>
        JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? [];

    // ── Organizations ─────────────────────────────────────────────────────────

    public async Task UpsertOrganizationAsync(OrganizationSummaryDto dto, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await UpsertOrganizationAsync(conn, transaction: null, dto, ct).ConfigureAwait(false);
    }

    private async Task UpsertOrganizationAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? transaction,
        OrganizationSummaryDto dto,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"INSERT INTO {Q("organization")}
                (organization_id, code, name, base_currency, is_active, effective_from, effective_to, business_ids, description, updated_at)
            VALUES (@id, @code, @name, @currency, @active, @eff_from, @eff_to, @biz_ids::jsonb, @desc, now())
            ON CONFLICT (organization_id) DO UPDATE SET
                code = EXCLUDED.code, name = EXCLUDED.name, base_currency = EXCLUDED.base_currency,
                is_active = EXCLUDED.is_active, effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to, business_ids = EXCLUDED.business_ids,
                description = EXCLUDED.description, updated_at = now()";
        cmd.Parameters.AddWithValue("id", dto.OrganizationId);
        cmd.Parameters.AddWithValue("code", dto.Code);
        cmd.Parameters.AddWithValue("name", dto.Name);
        cmd.Parameters.AddWithValue("currency", dto.BaseCurrency);
        cmd.Parameters.AddWithValue("active", dto.IsActive);
        cmd.Parameters.AddWithValue("eff_from", dto.EffectiveFrom);
        cmd.Parameters.AddWithValue("eff_to", dto.EffectiveTo.HasValue ? (object)dto.EffectiveTo.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("biz_ids", JsonSerializer.Serialize(dto.BusinessIds, JsonOpts));
        cmd.Parameters.AddWithValue("desc", (object?)dto.Description ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<OrganizationSummaryDto?> GetOrganizationAsync(Guid organizationId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("organization")} WHERE organization_id = @id";
        cmd.Parameters.AddWithValue("id", organizationId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return ReadOrganization(r);
    }

    public async Task<IReadOnlyList<OrganizationSummaryDto>> GetAllOrganizationsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("organization")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<OrganizationSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadOrganization(r));
        return result;
    }

    private static OrganizationSummaryDto ReadOrganization(NpgsqlDataReader r) =>
        new(r.GetGuid(r.GetOrdinal("organization_id")), r.GetString(r.GetOrdinal("code")),
            r.GetString(r.GetOrdinal("name")), r.GetString(r.GetOrdinal("base_currency")),
            r.GetBoolean(r.GetOrdinal("is_active")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_from")),
            r.IsDBNull(r.GetOrdinal("effective_to")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_to")),
            DeserializeGuids(r.GetString(r.GetOrdinal("business_ids"))),
            r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")));

    // ── Businesses ────────────────────────────────────────────────────────────

    public async Task UpsertBusinessAsync(BusinessSummaryDto dto, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await UpsertBusinessAsync(conn, transaction: null, dto, ct).ConfigureAwait(false);
    }

    private async Task UpsertBusinessAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? transaction,
        BusinessSummaryDto dto,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"INSERT INTO {Q("business")}
                (business_id, organization_id, business_kind, code, name, base_currency, is_active,
                 effective_from, effective_to, client_ids, fund_ids, investment_portfolio_ids, description, updated_at)
            VALUES (@id, @org_id, @kind, @code, @name, @currency, @active, @eff_from, @eff_to,
                    @client_ids::jsonb, @fund_ids::jsonb, @portfolio_ids::jsonb, @desc, now())
            ON CONFLICT (business_id) DO UPDATE SET
                organization_id = EXCLUDED.organization_id, business_kind = EXCLUDED.business_kind,
                code = EXCLUDED.code, name = EXCLUDED.name, base_currency = EXCLUDED.base_currency,
                is_active = EXCLUDED.is_active, effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to, client_ids = EXCLUDED.client_ids,
                fund_ids = EXCLUDED.fund_ids, investment_portfolio_ids = EXCLUDED.investment_portfolio_ids,
                description = EXCLUDED.description, updated_at = now()";
        cmd.Parameters.AddWithValue("id", dto.BusinessId);
        cmd.Parameters.AddWithValue("org_id", dto.OrganizationId);
        cmd.Parameters.AddWithValue("kind", dto.BusinessKind.ToString());
        cmd.Parameters.AddWithValue("code", dto.Code);
        cmd.Parameters.AddWithValue("name", dto.Name);
        cmd.Parameters.AddWithValue("currency", dto.BaseCurrency);
        cmd.Parameters.AddWithValue("active", dto.IsActive);
        cmd.Parameters.AddWithValue("eff_from", dto.EffectiveFrom);
        cmd.Parameters.AddWithValue("eff_to", dto.EffectiveTo.HasValue ? (object)dto.EffectiveTo.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("client_ids", JsonSerializer.Serialize(dto.ClientIds, JsonOpts));
        cmd.Parameters.AddWithValue("fund_ids", JsonSerializer.Serialize(dto.FundIds, JsonOpts));
        cmd.Parameters.AddWithValue("portfolio_ids", JsonSerializer.Serialize(dto.InvestmentPortfolioIds, JsonOpts));
        cmd.Parameters.AddWithValue("desc", (object?)dto.Description ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<BusinessSummaryDto?> GetBusinessAsync(Guid businessId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("business")} WHERE business_id = @id";
        cmd.Parameters.AddWithValue("id", businessId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return ReadBusiness(r);
    }

    public async Task<IReadOnlyList<BusinessSummaryDto>> GetAllBusinessesAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("business")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<BusinessSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadBusiness(r));
        return result;
    }

    private static BusinessSummaryDto ReadBusiness(NpgsqlDataReader r) =>
        new(r.GetGuid(r.GetOrdinal("business_id")), r.GetGuid(r.GetOrdinal("organization_id")),
            Enum.Parse<BusinessKindDto>(r.GetString(r.GetOrdinal("business_kind"))),
            r.GetString(r.GetOrdinal("code")), r.GetString(r.GetOrdinal("name")),
            r.GetString(r.GetOrdinal("base_currency")), r.GetBoolean(r.GetOrdinal("is_active")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_from")),
            r.IsDBNull(r.GetOrdinal("effective_to")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_to")),
            DeserializeGuids(r.GetString(r.GetOrdinal("client_ids"))),
            DeserializeGuids(r.GetString(r.GetOrdinal("fund_ids"))),
            DeserializeGuids(r.GetString(r.GetOrdinal("investment_portfolio_ids"))),
            r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")));

    // ── Clients ───────────────────────────────────────────────────────────────

    public async Task UpsertClientAsync(ClientSummaryDto dto, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await UpsertClientAsync(conn, transaction: null, dto, ct).ConfigureAwait(false);
    }

    private async Task UpsertClientAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? transaction,
        ClientSummaryDto dto,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"INSERT INTO {Q("client")}
                (client_id, business_id, code, name, base_currency, is_active,
                 effective_from, effective_to, investment_portfolio_ids, description, client_segment_kind, updated_at)
            VALUES (@id, @biz_id, @code, @name, @currency, @active, @eff_from, @eff_to,
                    @portfolio_ids::jsonb, @desc, @segment, now())
            ON CONFLICT (client_id) DO UPDATE SET
                business_id = EXCLUDED.business_id, code = EXCLUDED.code, name = EXCLUDED.name,
                base_currency = EXCLUDED.base_currency, is_active = EXCLUDED.is_active,
                effective_from = EXCLUDED.effective_from, effective_to = EXCLUDED.effective_to,
                investment_portfolio_ids = EXCLUDED.investment_portfolio_ids,
                description = EXCLUDED.description, client_segment_kind = EXCLUDED.client_segment_kind,
                updated_at = now()";
        cmd.Parameters.AddWithValue("id", dto.ClientId);
        cmd.Parameters.AddWithValue("biz_id", dto.BusinessId);
        cmd.Parameters.AddWithValue("code", dto.Code);
        cmd.Parameters.AddWithValue("name", dto.Name);
        cmd.Parameters.AddWithValue("currency", dto.BaseCurrency);
        cmd.Parameters.AddWithValue("active", dto.IsActive);
        cmd.Parameters.AddWithValue("eff_from", dto.EffectiveFrom);
        cmd.Parameters.AddWithValue("eff_to", dto.EffectiveTo.HasValue ? (object)dto.EffectiveTo.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("portfolio_ids", JsonSerializer.Serialize(dto.InvestmentPortfolioIds, JsonOpts));
        cmd.Parameters.AddWithValue("desc", (object?)dto.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("segment", dto.ClientSegmentKind.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<ClientSummaryDto?> GetClientAsync(Guid clientId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("client")} WHERE client_id = @id";
        cmd.Parameters.AddWithValue("id", clientId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return ReadClient(r);
    }

    public async Task<IReadOnlyList<ClientSummaryDto>> GetAllClientsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("client")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<ClientSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadClient(r));
        return result;
    }

    private static ClientSummaryDto ReadClient(NpgsqlDataReader r) =>
        new(r.GetGuid(r.GetOrdinal("client_id")), r.GetGuid(r.GetOrdinal("business_id")),
            r.GetString(r.GetOrdinal("code")), r.GetString(r.GetOrdinal("name")),
            r.GetString(r.GetOrdinal("base_currency")), r.GetBoolean(r.GetOrdinal("is_active")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_from")),
            r.IsDBNull(r.GetOrdinal("effective_to")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_to")),
            DeserializeGuids(r.GetString(r.GetOrdinal("investment_portfolio_ids"))),
            r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
            Enum.Parse<ClientSegmentKind>(r.GetString(r.GetOrdinal("client_segment_kind"))));

    // ── Funds ─────────────────────────────────────────────────────────────────

    public async Task UpsertFundAsync(FundSummaryDto dto, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await UpsertFundAsync(conn, transaction: null, dto, ct).ConfigureAwait(false);
    }

    private async Task UpsertFundAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? transaction,
        FundSummaryDto dto,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"INSERT INTO {Q("fund")}
                (fund_id, business_id, code, name, base_currency, is_active,
                 effective_from, effective_to, sleeve_ids, vehicle_ids, entity_ids,
                 investment_portfolio_ids, account_ids, description, updated_at)
            VALUES (@id, @biz_id, @code, @name, @currency, @active, @eff_from, @eff_to,
                    @sleeve_ids::jsonb, @vehicle_ids::jsonb, @entity_ids::jsonb,
                    @portfolio_ids::jsonb, @account_ids::jsonb, @desc, now())
            ON CONFLICT (fund_id) DO UPDATE SET
                business_id = EXCLUDED.business_id, code = EXCLUDED.code, name = EXCLUDED.name,
                base_currency = EXCLUDED.base_currency, is_active = EXCLUDED.is_active,
                effective_from = EXCLUDED.effective_from, effective_to = EXCLUDED.effective_to,
                sleeve_ids = EXCLUDED.sleeve_ids, vehicle_ids = EXCLUDED.vehicle_ids,
                entity_ids = EXCLUDED.entity_ids, investment_portfolio_ids = EXCLUDED.investment_portfolio_ids,
                account_ids = EXCLUDED.account_ids, description = EXCLUDED.description, updated_at = now()";
        cmd.Parameters.AddWithValue("id", dto.FundId);
        cmd.Parameters.AddWithValue("biz_id", dto.BusinessId.HasValue ? (object)dto.BusinessId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("code", dto.Code);
        cmd.Parameters.AddWithValue("name", dto.Name);
        cmd.Parameters.AddWithValue("currency", dto.BaseCurrency);
        cmd.Parameters.AddWithValue("active", dto.IsActive);
        cmd.Parameters.AddWithValue("eff_from", dto.EffectiveFrom);
        cmd.Parameters.AddWithValue("eff_to", dto.EffectiveTo.HasValue ? (object)dto.EffectiveTo.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("sleeve_ids", JsonSerializer.Serialize(dto.SleeveIds, JsonOpts));
        cmd.Parameters.AddWithValue("vehicle_ids", JsonSerializer.Serialize(dto.VehicleIds, JsonOpts));
        cmd.Parameters.AddWithValue("entity_ids", JsonSerializer.Serialize(dto.EntityIds, JsonOpts));
        cmd.Parameters.AddWithValue("portfolio_ids", JsonSerializer.Serialize(dto.InvestmentPortfolioIds, JsonOpts));
        cmd.Parameters.AddWithValue("account_ids", JsonSerializer.Serialize(dto.AccountIds, JsonOpts));
        cmd.Parameters.AddWithValue("desc", (object?)dto.Description ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<FundSummaryDto?> GetFundAsync(Guid fundId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("fund")} WHERE fund_id = @id";
        cmd.Parameters.AddWithValue("id", fundId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return ReadFund(r);
    }

    public async Task<IReadOnlyList<FundSummaryDto>> GetAllFundsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("fund")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<FundSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadFund(r));
        return result;
    }

    private static FundSummaryDto ReadFund(NpgsqlDataReader r) =>
        new(r.GetGuid(r.GetOrdinal("fund_id")),
            r.IsDBNull(r.GetOrdinal("business_id")) ? null : r.GetGuid(r.GetOrdinal("business_id")),
            r.GetString(r.GetOrdinal("code")), r.GetString(r.GetOrdinal("name")),
            r.GetString(r.GetOrdinal("base_currency")), r.GetBoolean(r.GetOrdinal("is_active")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_from")),
            r.IsDBNull(r.GetOrdinal("effective_to")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_to")),
            DeserializeGuids(r.GetString(r.GetOrdinal("sleeve_ids"))),
            DeserializeGuids(r.GetString(r.GetOrdinal("vehicle_ids"))),
            DeserializeGuids(r.GetString(r.GetOrdinal("entity_ids"))),
            DeserializeGuids(r.GetString(r.GetOrdinal("investment_portfolio_ids"))),
            DeserializeGuids(r.GetString(r.GetOrdinal("account_ids"))),
            r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")));

    // ── Sleeves ───────────────────────────────────────────────────────────────

    public async Task UpsertSleeveAsync(SleeveSummaryDto dto, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await UpsertSleeveAsync(conn, transaction: null, dto, ct).ConfigureAwait(false);
    }

    private async Task UpsertSleeveAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? transaction,
        SleeveSummaryDto dto,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"INSERT INTO {Q("sleeve")}
                (sleeve_id, fund_id, code, name, mandate, is_active,
                 effective_from, effective_to, strategy_ids, investment_portfolio_ids, account_ids, updated_at)
            VALUES (@id, @fund_id, @code, @name, @mandate, @active, @eff_from, @eff_to,
                    @strategy_ids::jsonb, @portfolio_ids::jsonb, @account_ids::jsonb, now())
            ON CONFLICT (sleeve_id) DO UPDATE SET
                fund_id = EXCLUDED.fund_id, code = EXCLUDED.code, name = EXCLUDED.name,
                mandate = EXCLUDED.mandate, is_active = EXCLUDED.is_active,
                effective_from = EXCLUDED.effective_from, effective_to = EXCLUDED.effective_to,
                strategy_ids = EXCLUDED.strategy_ids, investment_portfolio_ids = EXCLUDED.investment_portfolio_ids,
                account_ids = EXCLUDED.account_ids, updated_at = now()";
        cmd.Parameters.AddWithValue("id", dto.SleeveId);
        cmd.Parameters.AddWithValue("fund_id", dto.FundId);
        cmd.Parameters.AddWithValue("code", dto.Code);
        cmd.Parameters.AddWithValue("name", dto.Name);
        cmd.Parameters.AddWithValue("mandate", (object?)dto.Mandate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("active", dto.IsActive);
        cmd.Parameters.AddWithValue("eff_from", dto.EffectiveFrom);
        cmd.Parameters.AddWithValue("eff_to", dto.EffectiveTo.HasValue ? (object)dto.EffectiveTo.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("strategy_ids", JsonSerializer.Serialize(dto.StrategyIds, JsonOpts));
        cmd.Parameters.AddWithValue("portfolio_ids", JsonSerializer.Serialize(dto.InvestmentPortfolioIds, JsonOpts));
        cmd.Parameters.AddWithValue("account_ids", JsonSerializer.Serialize(dto.AccountIds, JsonOpts));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<SleeveSummaryDto?> GetSleeveAsync(Guid sleeveId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("sleeve")} WHERE sleeve_id = @id";
        cmd.Parameters.AddWithValue("id", sleeveId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return ReadSleeve(r);
    }

    public async Task<IReadOnlyList<SleeveSummaryDto>> GetAllSleevesAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("sleeve")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<SleeveSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadSleeve(r));
        return result;
    }

    private static SleeveSummaryDto ReadSleeve(NpgsqlDataReader r) =>
        new(r.GetGuid(r.GetOrdinal("sleeve_id")), r.GetGuid(r.GetOrdinal("fund_id")),
            r.GetString(r.GetOrdinal("code")), r.GetString(r.GetOrdinal("name")),
            r.IsDBNull(r.GetOrdinal("mandate")) ? null : r.GetString(r.GetOrdinal("mandate")),
            r.GetBoolean(r.GetOrdinal("is_active")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_from")),
            r.IsDBNull(r.GetOrdinal("effective_to")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_to")),
            DeserializeGuids(r.GetString(r.GetOrdinal("strategy_ids"))),
            DeserializeGuids(r.GetString(r.GetOrdinal("investment_portfolio_ids"))),
            DeserializeGuids(r.GetString(r.GetOrdinal("account_ids"))));

    // ── Vehicles ──────────────────────────────────────────────────────────────

    public async Task UpsertVehicleAsync(VehicleSummaryDto dto, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await UpsertVehicleAsync(conn, transaction: null, dto, ct).ConfigureAwait(false);
    }

    private async Task UpsertVehicleAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? transaction,
        VehicleSummaryDto dto,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"INSERT INTO {Q("vehicle")}
                (vehicle_id, fund_id, legal_entity_id, code, name, base_currency, is_active,
                 effective_from, effective_to, investment_portfolio_ids, account_ids, description, updated_at)
            VALUES (@id, @fund_id, @entity_id, @code, @name, @currency, @active, @eff_from, @eff_to,
                    @portfolio_ids::jsonb, @account_ids::jsonb, @desc, now())
            ON CONFLICT (vehicle_id) DO UPDATE SET
                fund_id = EXCLUDED.fund_id, legal_entity_id = EXCLUDED.legal_entity_id,
                code = EXCLUDED.code, name = EXCLUDED.name, base_currency = EXCLUDED.base_currency,
                is_active = EXCLUDED.is_active, effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to, investment_portfolio_ids = EXCLUDED.investment_portfolio_ids,
                account_ids = EXCLUDED.account_ids, description = EXCLUDED.description, updated_at = now()";
        cmd.Parameters.AddWithValue("id", dto.VehicleId);
        cmd.Parameters.AddWithValue("fund_id", dto.FundId);
        cmd.Parameters.AddWithValue("entity_id", dto.LegalEntityId);
        cmd.Parameters.AddWithValue("code", dto.Code);
        cmd.Parameters.AddWithValue("name", dto.Name);
        cmd.Parameters.AddWithValue("currency", dto.BaseCurrency);
        cmd.Parameters.AddWithValue("active", dto.IsActive);
        cmd.Parameters.AddWithValue("eff_from", dto.EffectiveFrom);
        cmd.Parameters.AddWithValue("eff_to", dto.EffectiveTo.HasValue ? (object)dto.EffectiveTo.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("portfolio_ids", JsonSerializer.Serialize(dto.InvestmentPortfolioIds, JsonOpts));
        cmd.Parameters.AddWithValue("account_ids", JsonSerializer.Serialize(dto.AccountIds, JsonOpts));
        cmd.Parameters.AddWithValue("desc", (object?)dto.Description ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<VehicleSummaryDto?> GetVehicleAsync(Guid vehicleId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("vehicle")} WHERE vehicle_id = @id";
        cmd.Parameters.AddWithValue("id", vehicleId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return ReadVehicle(r);
    }

    public async Task<IReadOnlyList<VehicleSummaryDto>> GetAllVehiclesAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("vehicle")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<VehicleSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadVehicle(r));
        return result;
    }

    private static VehicleSummaryDto ReadVehicle(NpgsqlDataReader r) =>
        new(r.GetGuid(r.GetOrdinal("vehicle_id")), r.GetGuid(r.GetOrdinal("fund_id")),
            r.GetGuid(r.GetOrdinal("legal_entity_id")),
            r.GetString(r.GetOrdinal("code")), r.GetString(r.GetOrdinal("name")),
            r.GetString(r.GetOrdinal("base_currency")), r.GetBoolean(r.GetOrdinal("is_active")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_from")),
            r.IsDBNull(r.GetOrdinal("effective_to")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_to")),
            DeserializeGuids(r.GetString(r.GetOrdinal("investment_portfolio_ids"))),
            DeserializeGuids(r.GetString(r.GetOrdinal("account_ids"))),
            r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")));

    // ── Legal Entities ────────────────────────────────────────────────────────

    public async Task UpsertLegalEntityAsync(LegalEntitySummaryDto dto, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await UpsertLegalEntityAsync(conn, transaction: null, dto, ct).ConfigureAwait(false);
    }

    private async Task UpsertLegalEntityAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? transaction,
        LegalEntitySummaryDto dto,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"INSERT INTO {Q("legal_entity")}
                (entity_id, entity_type, code, name, jurisdiction, base_currency, is_active,
                 effective_from, effective_to, description, legal_form, lifecycle_status,
                 registration_number, beneficial_owners, lifecycle_events, updated_at)
            VALUES (@id, @type, @code, @name, @jurisdiction, @currency, @active, @eff_from, @eff_to,
                    @desc, @legal_form, @lifecycle_status, @registration_number,
                    @beneficial_owners::jsonb, @lifecycle_events::jsonb, now())
            ON CONFLICT (entity_id) DO UPDATE SET
                entity_type = EXCLUDED.entity_type, code = EXCLUDED.code, name = EXCLUDED.name,
                jurisdiction = EXCLUDED.jurisdiction, base_currency = EXCLUDED.base_currency,
                is_active = EXCLUDED.is_active, effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to, description = EXCLUDED.description,
                legal_form = EXCLUDED.legal_form, lifecycle_status = EXCLUDED.lifecycle_status,
                registration_number = EXCLUDED.registration_number,
                beneficial_owners = EXCLUDED.beneficial_owners,
                lifecycle_events = EXCLUDED.lifecycle_events, updated_at = now()";
        cmd.Parameters.AddWithValue("id", dto.EntityId);
        cmd.Parameters.AddWithValue("type", dto.EntityType.ToString());
        cmd.Parameters.AddWithValue("code", dto.Code);
        cmd.Parameters.AddWithValue("name", dto.Name);
        cmd.Parameters.AddWithValue("jurisdiction", dto.Jurisdiction);
        cmd.Parameters.AddWithValue("currency", dto.BaseCurrency);
        cmd.Parameters.AddWithValue("active", dto.IsActive);
        cmd.Parameters.AddWithValue("eff_from", dto.EffectiveFrom);
        cmd.Parameters.AddWithValue("eff_to", dto.EffectiveTo.HasValue ? (object)dto.EffectiveTo.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("desc", (object?)dto.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("legal_form", dto.LegalForm.ToString());
        cmd.Parameters.AddWithValue("lifecycle_status", dto.LifecycleStatus.ToString());
        cmd.Parameters.AddWithValue("registration_number", (object?)dto.RegistrationNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("beneficial_owners", JsonSerializer.Serialize(dto.BeneficialOwners ?? [], JsonOpts));
        cmd.Parameters.AddWithValue("lifecycle_events", JsonSerializer.Serialize(dto.LifecycleEvents ?? [], JsonOpts));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("legal_entity")} WHERE entity_id = @id";
        cmd.Parameters.AddWithValue("id", entityId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return ReadLegalEntity(r);
    }

    public async Task<IReadOnlyList<LegalEntitySummaryDto>> GetAllLegalEntitiesAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("legal_entity")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<LegalEntitySummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadLegalEntity(r));
        return result;
    }

    private static LegalEntitySummaryDto ReadLegalEntity(NpgsqlDataReader r) =>
        new(r.GetGuid(r.GetOrdinal("entity_id")),
            Enum.Parse<LegalEntityTypeDto>(r.GetString(r.GetOrdinal("entity_type"))),
            r.GetString(r.GetOrdinal("code")), r.GetString(r.GetOrdinal("name")),
            r.GetString(r.GetOrdinal("jurisdiction")), r.GetString(r.GetOrdinal("base_currency")),
            r.GetBoolean(r.GetOrdinal("is_active")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_from")),
            r.IsDBNull(r.GetOrdinal("effective_to")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_to")),
            r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
            Enum.Parse<LegalEntityFormDto>(r.GetString(r.GetOrdinal("legal_form"))),
            Enum.Parse<LegalEntityLifecycleStatusDto>(r.GetString(r.GetOrdinal("lifecycle_status"))),
            r.IsDBNull(r.GetOrdinal("registration_number")) ? null : r.GetString(r.GetOrdinal("registration_number")),
            DeserializeList<BeneficialOwnerSummaryDto>(r.GetString(r.GetOrdinal("beneficial_owners"))),
            DeserializeList<LegalEntityLifecycleEventDto>(r.GetString(r.GetOrdinal("lifecycle_events"))));

    // ── Investment Portfolios ─────────────────────────────────────────────────

    public async Task UpsertInvestmentPortfolioAsync(InvestmentPortfolioSummaryDto dto, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await UpsertInvestmentPortfolioAsync(conn, transaction: null, dto, ct).ConfigureAwait(false);
    }

    private async Task UpsertInvestmentPortfolioAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? transaction,
        InvestmentPortfolioSummaryDto dto,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"INSERT INTO {Q("investment_portfolio")}
                (investment_portfolio_id, business_id, code, name, base_currency, is_active,
                 effective_from, effective_to, client_id, fund_id, sleeve_id, vehicle_id, entity_id,
                 account_ids, description, updated_at)
            VALUES (@id, @biz_id, @code, @name, @currency, @active, @eff_from, @eff_to,
                    @client_id, @fund_id, @sleeve_id, @vehicle_id, @entity_id,
                    @account_ids::jsonb, @desc, now())
            ON CONFLICT (investment_portfolio_id) DO UPDATE SET
                business_id = EXCLUDED.business_id, code = EXCLUDED.code, name = EXCLUDED.name,
                base_currency = EXCLUDED.base_currency, is_active = EXCLUDED.is_active,
                effective_from = EXCLUDED.effective_from, effective_to = EXCLUDED.effective_to,
                client_id = EXCLUDED.client_id, fund_id = EXCLUDED.fund_id,
                sleeve_id = EXCLUDED.sleeve_id, vehicle_id = EXCLUDED.vehicle_id,
                entity_id = EXCLUDED.entity_id, account_ids = EXCLUDED.account_ids,
                description = EXCLUDED.description, updated_at = now()";
        cmd.Parameters.AddWithValue("id", dto.InvestmentPortfolioId);
        cmd.Parameters.AddWithValue("biz_id", dto.BusinessId);
        cmd.Parameters.AddWithValue("code", dto.Code);
        cmd.Parameters.AddWithValue("name", dto.Name);
        cmd.Parameters.AddWithValue("currency", dto.BaseCurrency);
        cmd.Parameters.AddWithValue("active", dto.IsActive);
        cmd.Parameters.AddWithValue("eff_from", dto.EffectiveFrom);
        cmd.Parameters.AddWithValue("eff_to", dto.EffectiveTo.HasValue ? (object)dto.EffectiveTo.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("client_id", dto.ClientId.HasValue ? (object)dto.ClientId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("fund_id", dto.FundId.HasValue ? (object)dto.FundId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("sleeve_id", dto.SleeveId.HasValue ? (object)dto.SleeveId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("vehicle_id", dto.VehicleId.HasValue ? (object)dto.VehicleId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("entity_id", dto.EntityId.HasValue ? (object)dto.EntityId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("account_ids", JsonSerializer.Serialize(dto.AccountIds, JsonOpts));
        cmd.Parameters.AddWithValue("desc", (object?)dto.Description ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<InvestmentPortfolioSummaryDto?> GetInvestmentPortfolioAsync(Guid portfolioId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("investment_portfolio")} WHERE investment_portfolio_id = @id";
        cmd.Parameters.AddWithValue("id", portfolioId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return ReadInvestmentPortfolio(r);
    }

    public async Task<IReadOnlyList<InvestmentPortfolioSummaryDto>> GetAllInvestmentPortfoliosAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("investment_portfolio")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<InvestmentPortfolioSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadInvestmentPortfolio(r));
        return result;
    }

    private static InvestmentPortfolioSummaryDto ReadInvestmentPortfolio(NpgsqlDataReader r) =>
        new(r.GetGuid(r.GetOrdinal("investment_portfolio_id")),
            r.GetGuid(r.GetOrdinal("business_id")),
            r.GetString(r.GetOrdinal("code")), r.GetString(r.GetOrdinal("name")),
            r.GetString(r.GetOrdinal("base_currency")), r.GetBoolean(r.GetOrdinal("is_active")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_from")),
            r.IsDBNull(r.GetOrdinal("effective_to")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_to")),
            r.IsDBNull(r.GetOrdinal("client_id")) ? null : r.GetGuid(r.GetOrdinal("client_id")),
            r.IsDBNull(r.GetOrdinal("fund_id")) ? null : r.GetGuid(r.GetOrdinal("fund_id")),
            r.IsDBNull(r.GetOrdinal("sleeve_id")) ? null : r.GetGuid(r.GetOrdinal("sleeve_id")),
            r.IsDBNull(r.GetOrdinal("vehicle_id")) ? null : r.GetGuid(r.GetOrdinal("vehicle_id")),
            r.IsDBNull(r.GetOrdinal("entity_id")) ? null : r.GetGuid(r.GetOrdinal("entity_id")),
            DeserializeGuids(r.GetString(r.GetOrdinal("account_ids"))),
            r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")));

    // ── Ownership Links ───────────────────────────────────────────────────────

    public async Task UpsertOwnershipLinkAsync(OwnershipLinkDto dto, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await UpsertOwnershipLinkAsync(conn, transaction: null, dto, ct).ConfigureAwait(false);
    }

    private async Task UpsertOwnershipLinkAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? transaction,
        OwnershipLinkDto dto,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"INSERT INTO {Q("ownership_link")}
                (ownership_link_id, parent_node_id, child_node_id, relationship_type,
                 ownership_percent, is_primary, effective_from, effective_to, notes, updated_at)
            VALUES (@id, @parent, @child, @rel_type, @pct, @primary, @eff_from, @eff_to, @notes, now())
            ON CONFLICT (ownership_link_id) DO UPDATE SET
                parent_node_id = EXCLUDED.parent_node_id, child_node_id = EXCLUDED.child_node_id,
                relationship_type = EXCLUDED.relationship_type, ownership_percent = EXCLUDED.ownership_percent,
                is_primary = EXCLUDED.is_primary, effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to, notes = EXCLUDED.notes, updated_at = now()";
        cmd.Parameters.AddWithValue("id", dto.OwnershipLinkId);
        cmd.Parameters.AddWithValue("parent", dto.ParentNodeId);
        cmd.Parameters.AddWithValue("child", dto.ChildNodeId);
        cmd.Parameters.AddWithValue("rel_type", dto.RelationshipType.ToString());
        cmd.Parameters.AddWithValue("pct", dto.OwnershipPercent.HasValue ? (object)dto.OwnershipPercent.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("primary", dto.IsPrimary);
        cmd.Parameters.AddWithValue("eff_from", dto.EffectiveFrom);
        cmd.Parameters.AddWithValue("eff_to", dto.EffectiveTo.HasValue ? (object)dto.EffectiveTo.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("notes", (object?)dto.Notes ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OwnershipLinkDto>> GetAllOwnershipLinksAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("ownership_link")} ORDER BY effective_from";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<OwnershipLinkDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result.Add(new OwnershipLinkDto(
                r.GetGuid(r.GetOrdinal("ownership_link_id")),
                r.GetGuid(r.GetOrdinal("parent_node_id")),
                r.GetGuid(r.GetOrdinal("child_node_id")),
                Enum.Parse<OwnershipRelationshipTypeDto>(r.GetString(r.GetOrdinal("relationship_type"))),
                r.IsDBNull(r.GetOrdinal("ownership_percent")) ? null : r.GetDecimal(r.GetOrdinal("ownership_percent")),
                r.GetBoolean(r.GetOrdinal("is_primary")),
                r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_from")),
                r.IsDBNull(r.GetOrdinal("effective_to")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_to")),
                r.IsDBNull(r.GetOrdinal("notes")) ? null : r.GetString(r.GetOrdinal("notes"))));
        return result;
    }

    // ── Assignments ───────────────────────────────────────────────────────────

    public async Task UpsertAssignmentAsync(FundStructureAssignmentDto dto, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await UpsertAssignmentAsync(conn, transaction: null, dto, ct).ConfigureAwait(false);
    }

    private async Task UpsertAssignmentAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? transaction,
        FundStructureAssignmentDto dto,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"INSERT INTO {Q("fund_structure_assignment")}
                (assignment_id, node_id, assignment_type, assignment_reference,
                 effective_from, effective_to, is_primary, updated_at)
            VALUES (@id, @node_id, @type, @ref, @eff_from, @eff_to, @primary, now())
            ON CONFLICT (assignment_id) DO UPDATE SET
                node_id = EXCLUDED.node_id, assignment_type = EXCLUDED.assignment_type,
                assignment_reference = EXCLUDED.assignment_reference,
                effective_from = EXCLUDED.effective_from, effective_to = EXCLUDED.effective_to,
                is_primary = EXCLUDED.is_primary, updated_at = now()";
        cmd.Parameters.AddWithValue("id", dto.AssignmentId);
        cmd.Parameters.AddWithValue("node_id", dto.NodeId);
        cmd.Parameters.AddWithValue("type", dto.AssignmentType);
        cmd.Parameters.AddWithValue("ref", dto.AssignmentReference);
        cmd.Parameters.AddWithValue("eff_from", dto.EffectiveFrom);
        cmd.Parameters.AddWithValue("eff_to", dto.EffectiveTo.HasValue ? (object)dto.EffectiveTo.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("primary", dto.IsPrimary);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FundStructureAssignmentDto>> GetAllAssignmentsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("fund_structure_assignment")} ORDER BY effective_from";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<FundStructureAssignmentDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result.Add(new FundStructureAssignmentDto(
                r.GetGuid(r.GetOrdinal("assignment_id")),
                r.GetGuid(r.GetOrdinal("node_id")),
                r.GetString(r.GetOrdinal("assignment_type")),
                r.GetString(r.GetOrdinal("assignment_reference")),
                r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_from")),
                r.IsDBNull(r.GetOrdinal("effective_to")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_to")),
                r.GetBoolean(r.GetOrdinal("is_primary"))));
        return result;
    }

    // ── Linked account node identities ────────────────────────────────────────

    public async Task UpsertLinkedAccountIdAsync(Guid accountId, CancellationToken ct = default)
    {
        ValidateLinkedAccountId(accountId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await UpsertLinkedAccountIdAsync(connection, transaction: null, accountId, ct).ConfigureAwait(false);
    }

    private async Task UpsertLinkedAccountIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid accountId,
        CancellationToken ct)
    {
        ValidateLinkedAccountId(accountId);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {Q("fund_structure_linked_account")} (account_id, updated_at)
            VALUES (@account_id, now())
            ON CONFLICT (account_id) DO UPDATE SET updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> GetAllLinkedAccountIdsAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT account_id
            FROM {Q("fund_structure_linked_account")}
            ORDER BY account_id
            """;

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<Guid>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(reader.GetGuid(0));

        return result;
    }

    private static void ValidateLinkedAccountId(Guid accountId)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Linked fund-structure account identifier cannot be empty.",
                nameof(accountId));
        }
    }

    // ── Transactional legacy import ───────────────────────────────────────────

    public async Task<FundStructureLegacyImportResult> ImportLegacySnapshotIfEmptyAsync(
        FundStructureLegacyImportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceHash = NormalizeSourceHash(request.SourceHash);
        ct.ThrowIfCancellationRequested();

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);

        await AcquireLegacyImportLockAsync(connection, transaction, ct).ConfigureAwait(false);
        if (await HasLegacyImportReceiptAsync(connection, transaction, sourceHash, ct).ConfigureAwait(false))
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return FundStructureLegacyImportResult.AlreadyImported;
        }

        if (!await IsEmptyAsync(connection, transaction, ct).ConfigureAwait(false))
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return FundStructureLegacyImportResult.StoreNotEmpty;
        }

        foreach (var organization in request.Organizations)
            await UpsertOrganizationAsync(connection, transaction, organization, ct).ConfigureAwait(false);
        foreach (var business in request.Businesses)
            await UpsertBusinessAsync(connection, transaction, business, ct).ConfigureAwait(false);
        foreach (var client in request.Clients)
            await UpsertClientAsync(connection, transaction, client, ct).ConfigureAwait(false);
        foreach (var fund in request.Funds)
            await UpsertFundAsync(connection, transaction, fund, ct).ConfigureAwait(false);
        foreach (var sleeve in request.Sleeves)
            await UpsertSleeveAsync(connection, transaction, sleeve, ct).ConfigureAwait(false);
        foreach (var vehicle in request.Vehicles)
            await UpsertVehicleAsync(connection, transaction, vehicle, ct).ConfigureAwait(false);
        foreach (var entity in request.Entities)
            await UpsertLegalEntityAsync(connection, transaction, entity, ct).ConfigureAwait(false);
        foreach (var portfolio in request.InvestmentPortfolios)
            await UpsertInvestmentPortfolioAsync(connection, transaction, portfolio, ct).ConfigureAwait(false);
        foreach (var link in request.OwnershipLinks)
            await UpsertOwnershipLinkAsync(connection, transaction, link, ct).ConfigureAwait(false);
        foreach (var assignment in request.Assignments)
            await UpsertAssignmentAsync(connection, transaction, assignment, ct).ConfigureAwait(false);
        foreach (var linkedAccountId in request.LinkedAccountIds)
            await UpsertLinkedAccountIdAsync(connection, transaction, linkedAccountId, ct).ConfigureAwait(false);

        var entityCount = request.Organizations.Count
            + request.Businesses.Count
            + request.Clients.Count
            + request.Funds.Count
            + request.Sleeves.Count
            + request.Vehicles.Count
            + request.Entities.Count
            + request.InvestmentPortfolios.Count
            + request.OwnershipLinks.Count
            + request.Assignments.Count
            + request.LinkedAccountIds.Count;

        await using (var receiptCommand = connection.CreateCommand())
        {
            receiptCommand.Transaction = transaction;
            receiptCommand.CommandText = $"""
                INSERT INTO {Q("fund_structure_legacy_import_receipt")}
                    (source_hash, entity_count)
                VALUES (@source_hash, @entity_count)
                """;
            receiptCommand.Parameters.AddWithValue("source_hash", sourceHash);
            receiptCommand.Parameters.AddWithValue("entity_count", entityCount);
            await receiptCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return FundStructureLegacyImportResult.Imported;
    }

    private async Task AcquireLegacyImportLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtext(@lock_scope))";
        command.Parameters.AddWithValue(
            "lock_scope",
            $"meridian:{_options.Schema}:fund-structure-legacy-import");
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> HasLegacyImportReceiptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sourceHash,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT EXISTS (
                SELECT 1
                FROM {Q("fund_structure_legacy_import_receipt")}
                WHERE source_hash = @source_hash)
            """;
        command.Parameters.AddWithValue("source_hash", sourceHash);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;
    }

    private static string NormalizeSourceHash(string sourceHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHash);
        var normalized = sourceHash.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Legacy import source hash must be a 64-character SHA-256 hexadecimal value.",
                nameof(sourceHash));
        }

        return normalized;
    }

    // ── Emptiness check ───────────────────────────────────────────────────────

    public async Task<bool> IsEmptyAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        return await IsEmptyAsync(conn, transaction: null, ct).ConfigureAwait(false);
    }

    private async Task<bool> IsEmptyAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? transaction,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"""
            SELECT
                NOT EXISTS (SELECT 1 FROM {Q("organization")})
                AND NOT EXISTS (SELECT 1 FROM {Q("business")})
                AND NOT EXISTS (SELECT 1 FROM {Q("client")})
                AND NOT EXISTS (SELECT 1 FROM {Q("fund")})
                AND NOT EXISTS (SELECT 1 FROM {Q("sleeve")})
                AND NOT EXISTS (SELECT 1 FROM {Q("vehicle")})
                AND NOT EXISTS (SELECT 1 FROM {Q("legal_entity")})
                AND NOT EXISTS (SELECT 1 FROM {Q("investment_portfolio")})
                AND NOT EXISTS (SELECT 1 FROM {Q("ownership_link")})
                AND NOT EXISTS (SELECT 1 FROM {Q("fund_structure_assignment")})
                AND NOT EXISTS (SELECT 1 FROM {Q("fund_structure_linked_account")})
            """;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is true;
    }
}
