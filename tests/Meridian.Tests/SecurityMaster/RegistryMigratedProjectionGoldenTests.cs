using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Parity guard for the flat projection writers migrated onto
/// <see cref="SecurityTermsProjectionRegistry"/>: for every record in a matrix per class, the
/// hand-written writer and the registry writer run into the same table (each in its own rolled-back
/// transaction) and must leave identical rows — or identically no row. Every case also states the
/// row it expects, so the matrix stays a golden test once the hand-written writers are gone.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RegistryMigratedProjectionGoldenTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private const string DisplayName = "Golden fixture";
    private const string Currency = "USD";
    private const string PrimaryIdentifierValue = "GOLDEN-1";
    private const long Version = 7;

    private readonly SecurityMasterDatabaseFixture _fixture;

    public RegistryMigratedProjectionGoldenTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>One matrix row: the record to write, an optional prior write, and the expected row.</summary>
    /// <param name="Name">Case label for failure messages.</param>
    /// <param name="AssetClass">The record's asset class.</param>
    /// <param name="Terms">Raw asset-specific-terms JSON, so explicit nulls and wrong kinds survive.</param>
    /// <param name="Expected">
    /// The class-specific columns expected on the row as a JSON object, or null when the record must
    /// leave no row behind. Identity columns are added by the harness.
    /// </param>
    /// <param name="Prior">A record written first through the same path, to prove a stale row is cleared.</param>
    public sealed record GoldenCase(
        string Name,
        string AssetClass,
        string Terms,
        string? Expected,
        GoldenCase? Prior = null);

    private static readonly GoldenCase MoneyMarketFundFull = new(
        "full",
        "MoneyMarketFund",
        """{"fundFamily":"  Vanguard Treasury ","sweepEligible":true,"weightedAverageMaturityDays":42,"liquidityFeeEligible":true}""",
        """{"fund_family":"Vanguard Treasury","sweep_eligible":true,"weighted_average_maturity_days":42,"liquidity_fee_eligible":true}""");

    public static IReadOnlyList<GoldenCase> MoneyMarketFundCases { get; } =
    [
        MoneyMarketFundFull,
        new("minimal", "MoneyMarketFund", "{}",
            """{"fund_family":null,"sweep_eligible":false,"weighted_average_maturity_days":null,"liquidity_fee_eligible":false}"""),
        new("asset class matched case-insensitively", "moneymarketfund", """{"sweepEligible":true}""",
            """{"fund_family":null,"sweep_eligible":true,"weighted_average_maturity_days":null,"liquidity_fee_eligible":false}"""),
        new("whitespace family is trimmed to empty, not nulled", "MoneyMarketFund", """{"fundFamily":"   "}""",
            """{"fund_family":"","sweep_eligible":false,"weighted_average_maturity_days":null,"liquidity_fee_eligible":false}"""),
        new("empty family stays empty", "MoneyMarketFund", """{"fundFamily":""}""",
            """{"fund_family":"","sweep_eligible":false,"weighted_average_maturity_days":null,"liquidity_fee_eligible":false}"""),
        new("explicit nulls", "MoneyMarketFund",
            """{"fundFamily":null,"sweepEligible":null,"weightedAverageMaturityDays":null,"liquidityFeeEligible":null}""",
            """{"fund_family":null,"sweep_eligible":false,"weighted_average_maturity_days":null,"liquidity_fee_eligible":false}"""),
        new("wrong JSON kinds", "MoneyMarketFund",
            """{"fundFamily":5,"sweepEligible":"true","weightedAverageMaturityDays":"42","liquidityFeeEligible":1}""",
            """{"fund_family":null,"sweep_eligible":false,"weighted_average_maturity_days":null,"liquidity_fee_eligible":false}"""),
        new("non-integral maturity days", "MoneyMarketFund", """{"weightedAverageMaturityDays":42.5}""",
            """{"fund_family":null,"sweep_eligible":false,"weighted_average_maturity_days":null,"liquidity_fee_eligible":false}"""),
        new("class change clears the row", "Equity", """{"shareClass":"Common"}""", null, Prior: MoneyMarketFundFull),
    ];

    private static readonly GoldenCase CertificateOfDepositFull = new(
        "full",
        "CertificateOfDeposit",
        """{"issuerName":"  First Meridian Bank ","maturity":"2027-06-30","couponRate":4.25,"callableDate":"2026-12-31","dayCount":" ACT/360 "}""",
        """{"issuer_name":"First Meridian Bank","maturity_date":"2027-06-30","coupon_rate":4.25,"callable_date":"2026-12-31","day_count":" ACT/360 "}""");

    public static IReadOnlyList<GoldenCase> CertificateOfDepositCases { get; } =
    [
        CertificateOfDepositFull,
        new("minimal", "CertificateOfDeposit", """{"issuerName":"Bank","maturity":"2027-06-30"}""",
            """{"issuer_name":"Bank","maturity_date":"2027-06-30","coupon_rate":null,"callable_date":null,"day_count":null}"""),
        new("missing issuer", "CertificateOfDeposit", """{"maturity":"2027-06-30"}""", null),
        new("whitespace issuer", "CertificateOfDeposit", """{"issuerName":"   ","maturity":"2027-06-30"}""", null),
        new("empty issuer", "CertificateOfDeposit", """{"issuerName":"","maturity":"2027-06-30"}""", null),
        new("null issuer", "CertificateOfDeposit", """{"issuerName":null,"maturity":"2027-06-30"}""", null),
        new("numeric issuer", "CertificateOfDeposit", """{"issuerName":12,"maturity":"2027-06-30"}""", null),
        new("missing maturity", "CertificateOfDeposit", """{"issuerName":"Bank"}""", null),
        new("unparseable maturity", "CertificateOfDeposit", """{"issuerName":"Bank","maturity":"not-a-date"}""", null),
        new("numeric maturity", "CertificateOfDeposit", """{"issuerName":"Bank","maturity":20270630}""", null),
        new("explicit nulls", "CertificateOfDeposit",
            """{"issuerName":"Bank","maturity":"2027-06-30","couponRate":null,"callableDate":null,"dayCount":null}""",
            """{"issuer_name":"Bank","maturity_date":"2027-06-30","coupon_rate":null,"callable_date":null,"day_count":null}"""),
        new("wrong JSON kinds", "CertificateOfDeposit",
            """{"issuerName":"Bank","maturity":"2027-06-30","couponRate":"4.25","callableDate":20261231,"dayCount":360}""",
            """{"issuer_name":"Bank","maturity_date":"2027-06-30","coupon_rate":null,"callable_date":null,"day_count":null}"""),
        new("blank day count is kept verbatim", "CertificateOfDeposit",
            """{"issuerName":"Bank","maturity":"2027-06-30","dayCount":"  "}""",
            """{"issuer_name":"Bank","maturity_date":"2027-06-30","coupon_rate":null,"callable_date":null,"day_count":"  "}"""),
        new("gate miss after a projected row clears it", "CertificateOfDeposit", """{"issuerName":"  ","maturity":"2027-06-30"}""", null,
            Prior: CertificateOfDepositFull),
        new("class change clears the row", "Equity", """{"shareClass":"Common"}""", null, Prior: CertificateOfDepositFull),
    ];

    private static readonly GoldenCase DepositFull = new(
        "full",
        "Deposit",
        """{"depositType":" Term ","institutionName":" JPMorgan Chase ","maturity":"2026-03-01","interestRate":5.125,"dayCount":" ACT/365 ","isCallable":true}""",
        """{"deposit_type":"Term","institution_name":"JPMorgan Chase","maturity_date":"2026-03-01","interest_rate":5.125,"day_count":" ACT/365 ","is_callable":true}""");

    public static IReadOnlyList<GoldenCase> DepositCases { get; } =
    [
        DepositFull,
        new("minimal", "Deposit", """{"depositType":"Demand","institutionName":"Bank"}""",
            """{"deposit_type":"Demand","institution_name":"Bank","maturity_date":null,"interest_rate":null,"day_count":null,"is_callable":false}"""),
        new("missing deposit type", "Deposit", """{"institutionName":"Bank"}""", null),
        new("empty deposit type", "Deposit", """{"depositType":"","institutionName":"Bank"}""", null),
        new("whitespace institution", "Deposit", """{"depositType":"Demand","institutionName":" \t "}""", null),
        new("numeric institution", "Deposit", """{"depositType":"Demand","institutionName":7}""", null),
        new("explicit nulls", "Deposit",
            """{"depositType":"Demand","institutionName":"Bank","maturity":null,"interestRate":null,"dayCount":null,"isCallable":null}""",
            """{"deposit_type":"Demand","institution_name":"Bank","maturity_date":null,"interest_rate":null,"day_count":null,"is_callable":false}"""),
        new("wrong JSON kinds", "Deposit",
            """{"depositType":"Demand","institutionName":"Bank","maturity":"bad","interestRate":"5","dayCount":365,"isCallable":"true"}""",
            """{"deposit_type":"Demand","institution_name":"Bank","maturity_date":null,"interest_rate":null,"day_count":null,"is_callable":false}"""),
        new("empty day count is kept verbatim", "Deposit", """{"depositType":"Demand","institutionName":"Bank","dayCount":""}""",
            """{"deposit_type":"Demand","institution_name":"Bank","maturity_date":null,"interest_rate":null,"day_count":"","is_callable":false}"""),
        new("gate miss after a projected row clears it", "Deposit", """{"depositType":"Term"}""", null, Prior: DepositFull),
        new("class change clears the row", "CertificateOfDeposit", """{"issuerName":"Bank","maturity":"2027-06-30"}""", null,
            Prior: DepositFull),
    ];

    private static readonly GoldenCase CryptoFull = new(
        "full",
        "CryptoCurrency",
        """{"baseCurrency":" btc ","quoteCurrency":"usdT","network":" Bitcoin Mainnet "}""",
        """{"base_currency":"BTC","quote_currency":"USDT","network":" Bitcoin Mainnet "}""");

    public static IReadOnlyList<GoldenCase> CryptoCases { get; } =
    [
        CryptoFull,
        new("minimal", "CryptoCurrency", """{"baseCurrency":"ETH","quoteCurrency":"USD"}""",
            """{"base_currency":"ETH","quote_currency":"USD","network":null}"""),
        new("missing quote", "CryptoCurrency", """{"baseCurrency":"ETH"}""", null),
        new("whitespace base", "CryptoCurrency", """{"baseCurrency":"   ","quoteCurrency":"USD"}""", null),
        new("empty quote", "CryptoCurrency", """{"baseCurrency":"ETH","quoteCurrency":""}""", null),
        new("null base", "CryptoCurrency", """{"baseCurrency":null,"quoteCurrency":"USD"}""", null),
        new("numeric base", "CryptoCurrency", """{"baseCurrency":1,"quoteCurrency":"USD"}""", null),
        new("explicit null network", "CryptoCurrency", """{"baseCurrency":"sol","quoteCurrency":"usdc","network":null}""",
            """{"base_currency":"SOL","quote_currency":"USDC","network":null}"""),
        new("empty network is kept verbatim", "CryptoCurrency", """{"baseCurrency":"sol","quoteCurrency":"usdc","network":""}""",
            """{"base_currency":"SOL","quote_currency":"USDC","network":""}"""),
        new("numeric network", "CryptoCurrency", """{"baseCurrency":"sol","quoteCurrency":"usdc","network":5}""",
            """{"base_currency":"SOL","quote_currency":"USDC","network":null}"""),
        new("gate miss after a projected row clears it", "CryptoCurrency", """{"baseCurrency":"btc","quoteCurrency":" "}""", null,
            Prior: CryptoFull),
        new("class change clears the row", "FxSpot", """{"baseCurrency":"EUR","quoteCurrency":"USD"}""", null, Prior: CryptoFull),
    ];

    [SecurityMasterDatabaseFact]
    public Task MoneyMarketFund_HandWrittenAndRegistryWritersLeaveIdenticalRows()
        => AssertParityAsync("MoneyMarketFund", "money_market_fund_projection", "UpsertMoneyMarketFundProjectionAsync", MoneyMarketFundCases);

    [SecurityMasterDatabaseFact]
    public Task CertificateOfDeposit_HandWrittenAndRegistryWritersLeaveIdenticalRows()
        => AssertParityAsync("CertificateOfDeposit", "certificate_of_deposit_projection", "UpsertCertificateOfDepositProjectionAsync", CertificateOfDepositCases);

    [SecurityMasterDatabaseFact]
    public Task Deposit_HandWrittenAndRegistryWritersLeaveIdenticalRows()
        => AssertParityAsync("Deposit", "deposit_projection", "UpsertDepositProjectionAsync", DepositCases);

    [SecurityMasterDatabaseFact]
    public Task CryptoCurrency_HandWrittenAndRegistryWritersLeaveIdenticalRows()
        => AssertParityAsync("CryptoCurrency", "crypto_projection", "UpsertCryptoProjectionAsync", CryptoCases, projectsCurrency: false);

    private async Task AssertParityAsync(
        string assetClass,
        string table,
        string handWrittenMethod,
        IReadOnlyList<GoldenCase> cases,
        bool projectsCurrency = true)
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var descriptor = SecurityTermsProjectionRegistry.Descriptors.Single(d => d.AssetClass == assetClass);
        descriptor.TableName.Should().Be(table);

        var legacy = typeof(PostgresSecurityMasterStore).GetMethod(handWrittenMethod, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{handWrittenMethod} not found.");
        var registry = typeof(PostgresSecurityMasterStore).GetMethod("WriteTermsProjectionAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WriteTermsProjectionAsync not found.");

        Task Legacy(NpgsqlConnection c, NpgsqlTransaction t, SecurityProjectionRecord r)
            => (Task)legacy.Invoke(store, [c, t, r, CancellationToken.None])!;

        Task Registry(NpgsqlConnection c, NpgsqlTransaction t, SecurityProjectionRecord r)
            => (Task)registry.Invoke(store, [descriptor, c, t, r, CancellationToken.None])!;

        var failures = new List<string>();
        foreach (var golden in cases)
        {
            var securityId = Guid.NewGuid();
            var legacyRow = await RunAsync(Legacy, table, securityId, golden);
            var registryRow = await RunAsync(Registry, table, securityId, golden);

            if (!await JsonbEqualAsync(legacyRow, registryRow))
            {
                failures.Add($"[{golden.Name}] hand-written {legacyRow ?? "<no row>"} vs registry {registryRow ?? "<no row>"}");
            }

            var expected = ExpectedRow(golden.Expected, projectsCurrency);
            if (!await JsonbEqualAsync(expected, registryRow))
            {
                failures.Add($"[{golden.Name}] expected {expected ?? "<no row>"} but registry wrote {registryRow ?? "<no row>"}");
            }
        }

        cases.Should().Contain(golden => golden.Expected != null).And.Contain(golden => golden.Expected == null);
        failures.Should().BeEmpty();
    }

    /// <summary>
    /// Writes the case (and its prior record, if any) through <paramref name="writer"/> inside a
    /// transaction, reads the resulting row as jsonb text, then rolls back.
    /// </summary>
    private async Task<string?> RunAsync(
        Func<NpgsqlConnection, NpgsqlTransaction, SecurityProjectionRecord, Task> writer,
        string table,
        Guid securityId,
        GoldenCase golden)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        if (golden.Prior is { } prior)
        {
            await writer(connection, transaction, Record(securityId, prior));
            (await ReadRowAsync(connection, transaction, table, securityId))
                .Should().NotBeNull($"the prior record of [{golden.Name}] must project a row for the clear to be meaningful");
        }

        await writer(connection, transaction, Record(securityId, golden));
        var row = await ReadRowAsync(connection, transaction, table, securityId);
        await transaction.RollbackAsync();
        return row;
    }

    private async Task<string?> ReadRowAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, Guid securityId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select (to_jsonb(t) - 'security_id')::text from {_fixture.Options.Schema}.{table} t where security_id = @security_id;";
        command.Parameters.AddWithValue("security_id", securityId);
        return (string?)await command.ExecuteScalarAsync();
    }

    /// <summary>Compares two rows as jsonb, so numeric scale and key order do not matter.</summary>
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

    private static string? ExpectedRow(string? classColumns, bool projectsCurrency)
    {
        if (classColumns is null)
        {
            return null;
        }

        var row = JsonNode.Parse(classColumns)!.AsObject();
        row["display_name"] = DisplayName;
        if (projectsCurrency)
        {
            row["currency"] = Currency;
        }

        row["primary_identifier_value"] = PrimaryIdentifierValue;
        row["version"] = Version;
        return row.ToJsonString();
    }

    private static SecurityProjectionRecord Record(Guid securityId, GoldenCase golden)
    {
        using var terms = JsonDocument.Parse(golden.Terms);
        return new(
            securityId,
            golden.AssetClass,
            SecurityStatusDto.Active,
            DisplayName,
            Currency,
            "InternalCode",
            PrimaryIdentifierValue,
            JsonSerializer.SerializeToElement(new { }),
            terms.RootElement.Clone(),
            JsonSerializer.SerializeToElement(new { }),
            Version,
            DateTimeOffset.UtcNow,
            null,
            [],
            []);
    }
}
