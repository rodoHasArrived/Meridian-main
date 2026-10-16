using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Golden rows for the Equity, FxSpot, Future, Swap and Commodity relational projections.
/// <para>
/// These expectations were captured against the hand-written <c>Upsert*ProjectionAsync</c> writers
/// (the matrix passed against them first) before those writers moved onto
/// <see cref="SecurityTermsProjectionRegistry"/> descriptors with derived columns; the same matrix
/// then had to pass against the descriptors. Cases cover gate hits and misses, stale rows cleared
/// on a gate miss or class change, verbatim and normalized strings, common-terms columns, derived
/// columns (equity classification, FX pair code, futures root-symbol fallback and lifecycle state,
/// swap type from legs) and wrong JSON kinds.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class SpecializedProjectionGoldenTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private const string DisplayName = "Golden specialized fixture";
    private const string Currency = "USD";
    private const long Version = 11;

    /// <summary>Replaced in expectations by the record's primary identifier, upper-cased.</summary>
    private const string PrimaryUpper = "{PRIMARY_UPPER}";

    private readonly SecurityMasterDatabaseFixture _fixture;

    public SpecializedProjectionGoldenTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <param name="CommonTerms">Raw common-terms JSON; defaults to display name and currency only.</param>
    /// <param name="Retired">Whether the record carries an effective-to date.</param>
    public sealed record GoldenCase(
        string Name,
        string AssetClass,
        string Terms,
        string? Expected,
        GoldenCase? Prior = null,
        string? CommonTerms = null,
        bool Retired = false);

    private static readonly GoldenCase EquityFull = new(
        "full",
        "Equity",
        """{"shareClass":" A ","votingRightsCat":"FullVoting","classification":"Other","otherClassification":"Tracking"}""",
        """{"share_class":" A ","voting_rights_cat":"FullVoting","classification":"Tracking","exchange_code":"XNYS","country_of_risk":"US","issuer_name":"Acme Corp"}""",
        CommonTerms: """{"displayName":"x","currency":"USD","exchange":"XNYS","countryOfRisk":"US","issuerName":"Acme Corp"}""");

    public static IReadOnlyList<GoldenCase> EquityCases { get; } =
    [
        EquityFull,
        new("minimal", "Equity", "{}",
            """{"share_class":null,"voting_rights_cat":null,"classification":null,"exchange_code":null,"country_of_risk":null,"issuer_name":null}"""),
        new("plain classification", "Equity", """{"classification":"Common"}""",
            """{"share_class":null,"voting_rights_cat":null,"classification":"Common","exchange_code":null,"country_of_risk":null,"issuer_name":null}"""),
        new("blank classification reads as absent", "Equity", """{"classification":"  "}""",
            """{"share_class":null,"voting_rights_cat":null,"classification":null,"exchange_code":null,"country_of_risk":null,"issuer_name":null}"""),
        new("wrong JSON kinds", "Equity", """{"shareClass":5,"votingRightsCat":true,"classification":7}""",
            """{"share_class":null,"voting_rights_cat":null,"classification":null,"exchange_code":null,"country_of_risk":null,"issuer_name":null}"""),
        new("asset class matched case-insensitively", "equity", """{"shareClass":"B"}""",
            """{"share_class":"B","voting_rights_cat":null,"classification":null,"exchange_code":null,"country_of_risk":null,"issuer_name":null}"""),
        new("class change clears the row", "FxSpot", """{"baseCurrency":"CHF","quoteCurrency":"JPY"}""", null, Prior: EquityFull),
    ];

    private static readonly GoldenCase FxSpotFull = new(
        "full",
        "FxSpot",
        """{"baseCurrency":" eur ","quoteCurrency":"usd"}""",
        """{"base_currency":"EUR","quote_currency":"USD","pair_code":"EURUSD"}""");

    public static IReadOnlyList<GoldenCase> FxSpotCases { get; } =
    [
        FxSpotFull,
        new("missing quote", "FxSpot", """{"baseCurrency":"EUR"}""", null),
        new("whitespace base", "FxSpot", """{"baseCurrency":"   ","quoteCurrency":"USD"}""", null),
        new("numeric base", "FxSpot", """{"baseCurrency":1,"quoteCurrency":"USD"}""", null),
        new("null quote", "FxSpot", """{"baseCurrency":"GBP","quoteCurrency":null}""", null),
        new("gate miss after a projected row clears it", "FxSpot", """{"baseCurrency":"GBP","quoteCurrency":" "}""", null,
            Prior: FxSpotFull with { Terms = """{"baseCurrency":"GBP","quoteCurrency":"USD"}""" }),
        new("class change clears the row", "Equity", "{}", null,
            Prior: FxSpotFull with { Terms = """{"baseCurrency":"AUD","quoteCurrency":"USD"}""" }),
    ];

    private static readonly GoldenCase FutureFull = new(
        "full",
        "Future",
        """{"rootSymbol":" es ","contractMonth":"2099-12","expiry":"2099-12-18","multiplier":50,"settlementType":"Cash","isRollTarget":true,"rollWindowDays":5,"lastTradingDt":"2099-12-17","firstNoticeDt":"2099-12-01","deliveryMonthDt":"2099-12-01","deliveryLocationCode":"CME"}""",
        """{"root_symbol":"ES","contract_month":"2099-12","expiry_date":"2099-12-18","multiplier":50,"settlement_type":"Cash","is_roll_target":true,"roll_window_days":5,"last_trading_dt":"2099-12-17","first_notice_dt":"2099-12-01","delivery_month_dt":"2099-12-01","delivery_location_code":"CME","lifecycle_stat":"RollTarget"}""");

    public static IReadOnlyList<GoldenCase> FutureCases { get; } =
    [
        FutureFull,
        new("minimal falls back to the primary identifier and defaults", "Future", """{"expiry":"2099-12-18"}""",
            $$"""{"root_symbol":"{{PrimaryUpper}}","contract_month":"","expiry_date":"2099-12-18","multiplier":1,"settlement_type":null,"is_roll_target":false,"roll_window_days":null,"last_trading_dt":"2099-12-18","first_notice_dt":null,"delivery_month_dt":null,"delivery_location_code":null,"lifecycle_stat":"Active"}"""),
        new("expired", "Future", """{"expiry":"2001-01-19","rootSymbol":"CL"}""",
            """{"root_symbol":"CL","contract_month":"","expiry_date":"2001-01-19","multiplier":1,"settlement_type":null,"is_roll_target":false,"roll_window_days":null,"last_trading_dt":"2001-01-19","first_notice_dt":null,"delivery_month_dt":null,"delivery_location_code":null,"lifecycle_stat":"Expired"}"""),
        new("retired", "Future", """{"expiry":"2099-12-18","rootSymbol":"NQ"}""",
            """{"root_symbol":"NQ","contract_month":"","expiry_date":"2099-12-18","multiplier":1,"settlement_type":null,"is_roll_target":false,"roll_window_days":null,"last_trading_dt":"2099-12-18","first_notice_dt":null,"delivery_month_dt":null,"delivery_location_code":null,"lifecycle_stat":"Retired"}""",
            Retired: true),
        new("wrong JSON kinds", "Future", """{"expiry":"2099-12-18","rootSymbol":"ZN","multiplier":"50","isRollTarget":"true","rollWindowDays":"5","lastTradingDt":7}""",
            """{"root_symbol":"ZN","contract_month":"","expiry_date":"2099-12-18","multiplier":1,"settlement_type":null,"is_roll_target":false,"roll_window_days":null,"last_trading_dt":"2099-12-18","first_notice_dt":null,"delivery_month_dt":null,"delivery_location_code":null,"lifecycle_stat":"Active"}"""),
        new("missing expiry", "Future", """{"rootSymbol":"ES"}""", null),
        new("unparseable expiry", "Future", """{"rootSymbol":"ES","expiry":"december"}""", null),
        new("gate miss after a projected row clears it", "Future", """{"rootSymbol":"ES"}""", null, Prior: FutureFull),
        new("class change clears the row", "Equity", "{}", null, Prior: FutureFull),
    ];

    private static readonly GoldenCase SwapFull = new(
        "fixed against floating",
        "Swap",
        """{"effectiveDate":"2025-01-01","maturityDate":"2030-01-01","legs":[{"legType":"Fixed"},{"legType":"Floating"}]}""",
        """{"swap_type":"FixedFloat","effective_date":"2025-01-01","maturity_date":"2030-01-01","lifecycle_stat":"Active"}""");

    public static IReadOnlyList<GoldenCase> SwapCases { get; } =
    [
        SwapFull,
        new("explicit swap type wins verbatim", "Swap", """{"swapType":" Basis ","effectiveDate":"2025-01-01","maturityDate":"2030-01-01","legs":[{"legType":"Fixed"}]}""",
            """{"swap_type":" Basis ","effective_date":"2025-01-01","maturity_date":"2030-01-01","lifecycle_stat":"Active"}"""),
        new("floating legs of one type", "Swap", """{"effectiveDate":"2025-01-01","maturityDate":"2030-01-01","legs":[{"legType":"Floating"},{"legType":" floating "}]}""",
            """{"swap_type":"BasisFloat","effective_date":"2025-01-01","maturity_date":"2030-01-01","lifecycle_stat":"Active"}"""),
        new("other leg types join", "Swap", """{"effectiveDate":"2025-01-01","maturityDate":"2030-01-01","legs":[{"legType":"Inflation"},{"legType":"Fixed"}]}""",
            """{"swap_type":"Inflation/Fixed","effective_date":"2025-01-01","maturity_date":"2030-01-01","lifecycle_stat":"Active"}"""),
        new("no legs", "Swap", """{"effectiveDate":"2025-01-01","maturityDate":"2030-01-01"}""",
            """{"swap_type":null,"effective_date":"2025-01-01","maturity_date":"2030-01-01","lifecycle_stat":"Active"}"""),
        new("missing maturity", "Swap", """{"effectiveDate":"2025-01-01"}""", null),
        new("gate miss after a projected row clears it", "Swap", """{"effectiveDate":"2025-01-01"}""", null, Prior: SwapFull),
        new("class change clears the row", "Equity", "{}", null, Prior: SwapFull),
    ];

    private static readonly GoldenCase CommodityFull = new(
        "full",
        "Commodity",
        """{"commodityType":" Energy ","denomination":"bbl","contractSize":1000,"exchangeCode":"NYMEX","deliveryCountry":"US"}""",
        """{"commodity_type":"Energy","denomination":"bbl","contract_size":1000,"exchange_code":"NYMEX","delivery_country":"US"}""");

    public static IReadOnlyList<GoldenCase> CommodityCases { get; } =
    [
        CommodityFull,
        new("minimal", "Commodity", """{"commodityType":"Metal"}""",
            """{"commodity_type":"Metal","denomination":null,"contract_size":null,"exchange_code":null,"delivery_country":null}"""),
        new("wrong JSON kinds", "Commodity", """{"commodityType":"Metal","denomination":4,"contractSize":"100"}""",
            """{"commodity_type":"Metal","denomination":null,"contract_size":null,"exchange_code":null,"delivery_country":null}"""),
        new("missing type", "Commodity", """{"denomination":"oz"}""", null),
        new("whitespace type", "Commodity", """{"commodityType":"  "}""", null),
        new("gate miss after a projected row clears it", "Commodity", """{"commodityType":""}""", null, Prior: CommodityFull),
        new("class change clears the row", "Equity", "{}", null, Prior: CommodityFull),
    ];

    [SecurityMasterDatabaseFact]
    public Task Equity_LeavesTheGoldenRows() => AssertGoldenRowsAsync("equity_projection", EquityCases);

    [SecurityMasterDatabaseFact]
    public Task FxSpot_LeavesTheGoldenRows() => AssertGoldenRowsAsync("fxspot_projection", FxSpotCases, projectsCurrency: false);

    [SecurityMasterDatabaseFact]
    public Task Future_LeavesTheGoldenRows() => AssertGoldenRowsAsync("future_projection", FutureCases);

    [SecurityMasterDatabaseFact]
    public Task Swap_LeavesTheGoldenRows() => AssertGoldenRowsAsync("swap_projection", SwapCases);

    [SecurityMasterDatabaseFact]
    public Task Commodity_LeavesTheGoldenRows() => AssertGoldenRowsAsync("commodity_projection", CommodityCases);

    private async Task AssertGoldenRowsAsync(string table, IReadOnlyList<GoldenCase> cases, bool projectsCurrency = true)
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var failures = new List<string>();
        foreach (var golden in cases)
        {
            var securityId = Guid.NewGuid();
            if (golden.Prior is { } prior)
            {
                await store.UpsertProjectionAsync(Record(securityId, prior));
                (await ReadRowAsync(table, securityId))
                    .Should().NotBeNull($"the prior record of [{golden.Name}] must project a row for the clear to be meaningful");
            }

            await store.UpsertProjectionAsync(Record(securityId, golden));
            var row = await ReadRowAsync(table, securityId);
            var expected = ExpectedRow(golden.Expected, projectsCurrency, securityId);
            if (!await JsonbEqualAsync(expected, row))
            {
                failures.Add($"[{golden.Name}] expected {expected ?? "<no row>"} but the store wrote {row ?? "<no row>"}");
            }
        }

        cases.Should().Contain(golden => golden.Expected != null).And.Contain(golden => golden.Expected == null);
        failures.Should().BeEmpty();
    }

    private async Task<string?> ReadRowAsync(string table, Guid securityId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"select (to_jsonb(t) - 'security_id')::text from {_fixture.Options.Schema}.{table} t where security_id = @security_id;";
        command.Parameters.AddWithValue("security_id", securityId);
        return (string?)await command.ExecuteScalarAsync();
    }

    private async Task<bool> JsonbEqualAsync(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select @left::jsonb = @right::jsonb;";
        command.Parameters.AddWithValue("left", left);
        command.Parameters.AddWithValue("right", right);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static string? ExpectedRow(string? classColumns, bool projectsCurrency, Guid securityId)
    {
        if (classColumns is null)
        {
            return null;
        }

        var row = JsonNode.Parse(classColumns.Replace(PrimaryUpper, IdentifierValue(securityId).ToUpperInvariant(), StringComparison.Ordinal))!.AsObject();
        row["display_name"] = DisplayName;
        if (projectsCurrency)
        {
            row["currency"] = Currency;
        }

        row["primary_identifier_value"] = IdentifierValue(securityId);
        row["version"] = Version;
        return row.ToJsonString();
    }

    private static string IdentifierValue(Guid securityId) => $"Golden-{securityId:N}";

    private static SecurityProjectionRecord Record(Guid securityId, GoldenCase golden)
    {
        using var terms = JsonDocument.Parse(golden.Terms);
        using var common = JsonDocument.Parse(golden.CommonTerms ?? $$"""{"displayName":"{{DisplayName}}","currency":"{{Currency}}"}""");
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        return new(
            securityId,
            golden.AssetClass,
            golden.Retired ? SecurityStatusDto.Inactive : SecurityStatusDto.Active,
            DisplayName,
            Currency,
            SecurityIdentifierKind.InternalCode.ToString(),
            IdentifierValue(securityId),
            common.RootElement.Clone(),
            terms.RootElement.Clone(),
            JsonSerializer.SerializeToElement(new { sourceSystem = "integration-test", updatedBy = "specialized-golden-fixture" }),
            Version,
            effectiveFrom,
            golden.Retired ? effectiveFrom.AddHours(1) : null,
            [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, IdentifierValue(securityId), true, effectiveFrom)],
            []);
    }
}
