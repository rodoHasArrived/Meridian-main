using System.Text.Json;
using System.Transactions;
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

    // ── Organizations ─────────────────────────────────────────────────────────

    public async Task UpsertOrganizationAsync(OrganizationSummaryDto dto, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
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
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return ReadOrganization(r);
    }

    public async Task<IReadOnlyList<OrganizationSummaryDto>> GetAllOrganizationsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("organization")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<OrganizationSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false)) result.Add(ReadOrganization(r));
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
        await using var cmd = conn.CreateCommand();
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
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return ReadBusiness(r);
    }

    public async Task<IReadOnlyList<BusinessSummaryDto>> GetAllBusinessesAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("business")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<BusinessSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false)) result.Add(ReadBusiness(r));
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
        await using var cmd = conn.CreateCommand();
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
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return ReadClient(r);
    }

    public async Task<IReadOnlyList<ClientSummaryDto>> GetAllClientsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("client")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<ClientSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false)) result.Add(ReadClient(r));
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
        await using var cmd = conn.CreateCommand();
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
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return ReadFund(r);
    }

    public async Task<IReadOnlyList<FundSummaryDto>> GetAllFundsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("fund")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<FundSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false)) result.Add(ReadFund(r));
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
        await using var cmd = conn.CreateCommand();
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
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return ReadSleeve(r);
    }

    public async Task<IReadOnlyList<SleeveSummaryDto>> GetAllSleevesAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("sleeve")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<SleeveSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false)) result.Add(ReadSleeve(r));
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
        await using var cmd = conn.CreateCommand();
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
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return ReadVehicle(r);
    }

    public async Task<IReadOnlyList<VehicleSummaryDto>> GetAllVehiclesAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("vehicle")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<VehicleSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false)) result.Add(ReadVehicle(r));
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
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"INSERT INTO {Q("legal_entity")}
                (entity_id, entity_type, code, name, jurisdiction, base_currency, is_active,
                 effective_from, effective_to, description, updated_at)
            VALUES (@id, @type, @code, @name, @jurisdiction, @currency, @active, @eff_from, @eff_to, @desc, now())
            ON CONFLICT (entity_id) DO UPDATE SET
                entity_type = EXCLUDED.entity_type, code = EXCLUDED.code, name = EXCLUDED.name,
                jurisdiction = EXCLUDED.jurisdiction, base_currency = EXCLUDED.base_currency,
                is_active = EXCLUDED.is_active, effective_from = EXCLUDED.effective_from,
                effective_to = EXCLUDED.effective_to, description = EXCLUDED.description, updated_at = now()";
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
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("legal_entity")} WHERE entity_id = @id";
        cmd.Parameters.AddWithValue("id", entityId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return ReadLegalEntity(r);
    }

    public async Task<IReadOnlyList<LegalEntitySummaryDto>> GetAllLegalEntitiesAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("legal_entity")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<LegalEntitySummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false)) result.Add(ReadLegalEntity(r));
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
            r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")));

    // ── Investment Portfolios ─────────────────────────────────────────────────

    public async Task UpsertInvestmentPortfolioAsync(InvestmentPortfolioSummaryDto dto, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
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
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return ReadInvestmentPortfolio(r);
    }

    public async Task<IReadOnlyList<InvestmentPortfolioSummaryDto>> GetAllInvestmentPortfoliosAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Q("investment_portfolio")} ORDER BY name";
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<InvestmentPortfolioSummaryDto>();
        while (await r.ReadAsync(ct).ConfigureAwait(false)) result.Add(ReadInvestmentPortfolio(r));
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
        await using var cmd = conn.CreateCommand();
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
        await using var cmd = conn.CreateCommand();
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


    public async Task CommitSetupBatchAsync(FundStructureSetupBatch batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
            TransactionScopeAsyncFlowOption.Enabled);

        foreach (var dto in batch.Organizations)
            await UpsertOrganizationAsync(dto, ct).ConfigureAwait(false);
        foreach (var dto in batch.Businesses)
            await UpsertBusinessAsync(dto, ct).ConfigureAwait(false);
        foreach (var dto in batch.Clients)
            await UpsertClientAsync(dto, ct).ConfigureAwait(false);
        foreach (var dto in batch.Funds)
            await UpsertFundAsync(dto, ct).ConfigureAwait(false);
        foreach (var dto in batch.Vehicles)
            await UpsertVehicleAsync(dto, ct).ConfigureAwait(false);
        foreach (var dto in batch.LegalEntities)
            await UpsertLegalEntityAsync(dto, ct).ConfigureAwait(false);
        foreach (var dto in batch.InvestmentPortfolios)
            await UpsertInvestmentPortfolioAsync(dto, ct).ConfigureAwait(false);
        foreach (var dto in batch.OwnershipLinks)
            await UpsertOwnershipLinkAsync(dto, ct).ConfigureAwait(false);
        foreach (var dto in batch.Assignments)
            await UpsertAssignmentAsync(dto, ct).ConfigureAwait(false);

        scope.Complete();
    }

    // ── Emptiness check ───────────────────────────────────────────────────────

    public async Task<bool> IsEmptyAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) = 0 FROM {Q("organization")}";
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is true;
    }
}
