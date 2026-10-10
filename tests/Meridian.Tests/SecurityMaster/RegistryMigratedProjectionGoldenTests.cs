using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Golden rows for the flat projections migrated onto <see cref="SecurityTermsProjectionRegistry"/>
/// (CryptoCurrency, Deposit, MoneyMarketFund, CertificateOfDeposit).
/// <para>
/// This matrix began as a parity guard: each hand-written <c>Upsert*ProjectionAsync</c> writer and
/// its registry descriptor ran into the same table for every case and had to leave identical rows,
/// and only once it passed were the hand-written writers deleted. The expectations below are what
/// those writers persisted — gate hits and misses (including whitespace-only gate terms), stale rows
/// cleared on a gate miss or class change, trimming and upper-casing, verbatim optional strings,
/// explicit JSON nulls, wrong JSON kinds, and NOT NULL flags defaulting to false — so any change to
/// the registry path that would alter a persisted row fails here.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class RegistryMigratedProjectionGoldenTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private const string DisplayName = "Golden fixture";
    private const string Currency = "USD";
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
    public Task MoneyMarketFund_RegistryWriterLeavesTheGoldenRows()
        => AssertGoldenRowsAsync("MoneyMarketFund", "money_market_fund_projection", MoneyMarketFundCases);

    [SecurityMasterDatabaseFact]
    public Task CertificateOfDeposit_RegistryWriterLeavesTheGoldenRows()
        => AssertGoldenRowsAsync("CertificateOfDeposit", "certificate_of_deposit_projection", CertificateOfDepositCases);

    [SecurityMasterDatabaseFact]
    public Task Deposit_RegistryWriterLeavesTheGoldenRows()
        => AssertGoldenRowsAsync("Deposit", "deposit_projection", DepositCases);

    [SecurityMasterDatabaseFact]
    public Task CryptoCurrency_RegistryWriterLeavesTheGoldenRows()
        => AssertGoldenRowsAsync("CryptoCurrency", "crypto_projection", CryptoCases, projectsCurrency: false);

    [SecurityMasterDatabaseFact]
    public async Task MigratedProjections_StayReadableThroughTheirReferenceProjectionStores()
    {
        // The read side was not touched by the migration; this proves the rows the registry writes
        // still decode through the unchanged query stores, end to end through UpsertProjectionAsync.
        var store = new PostgresSecurityMasterStore(_fixture.Options);

        var fundId = Guid.NewGuid();
        await store.UpsertProjectionAsync(Record(fundId, MoneyMarketFundFull));
        var fund = await new PostgresMoneyMarketFundReferenceProjectionStore(_fixture.Options).GetMoneyMarketFundAsync(fundId);
        fund.Should().NotBeNull();
        fund!.FundFamily.Should().Be("Vanguard Treasury");
        fund.SweepEligible.Should().BeTrue();
        fund.WeightedAverageMaturityDays.Should().Be(42);

        var cdId = Guid.NewGuid();
        await store.UpsertProjectionAsync(Record(cdId, CertificateOfDepositFull));
        var cd = await new PostgresCertificateOfDepositReferenceProjectionStore(_fixture.Options).GetCertificateOfDepositAsync(cdId);
        cd.Should().NotBeNull();
        cd!.IssuerName.Should().Be("First Meridian Bank");
        cd.Maturity.Should().Be(new DateOnly(2027, 6, 30));

        var depositId = Guid.NewGuid();
        await store.UpsertProjectionAsync(Record(depositId, DepositFull));
        var deposit = await new PostgresDepositReferenceProjectionStore(_fixture.Options).GetDepositAsync(depositId);
        deposit.Should().NotBeNull();
        deposit!.InstitutionName.Should().Be("JPMorgan Chase");
        deposit.IsCallable.Should().BeTrue();

        var cryptoId = Guid.NewGuid();
        await store.UpsertProjectionAsync(Record(cryptoId, CryptoFull));
        var crypto = await new PostgresCryptoReferenceProjectionStore(_fixture.Options).GetCryptoAsync(cryptoId);
        crypto.Should().NotBeNull();
        crypto!.BaseCurrency.Should().Be("BTC");
        crypto.QuoteCurrency.Should().Be("USDT");
    }

    private async Task AssertGoldenRowsAsync(
        string assetClass,
        string table,
        IReadOnlyList<GoldenCase> cases,
        bool projectsCurrency = true)
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        SecurityTermsProjectionRegistry.Descriptors.Single(d => d.AssetClass == assetClass)
            .TableName.Should().Be(table);

        var failures = new List<string>();
        foreach (var golden in cases)
        {
            var securityId = Guid.NewGuid();

            // Through the public write path, so the fan-out registration is covered too: every
            // writer runs for every record, which is what clears a row on a class change.
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

    private static string? ExpectedRow(string? classColumns, bool projectsCurrency, Guid securityId)
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

        row["primary_identifier_value"] = IdentifierValue(securityId);
        row["version"] = Version;
        return row.ToJsonString();
    }

    private static string IdentifierValue(Guid securityId) => $"GOLDEN-{securityId:N}";

    private static SecurityProjectionRecord Record(Guid securityId, GoldenCase golden)
    {
        using var terms = JsonDocument.Parse(golden.Terms);
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        return new(
            securityId,
            golden.AssetClass,
            SecurityStatusDto.Active,
            DisplayName,
            Currency,
            SecurityIdentifierKind.InternalCode.ToString(),
            IdentifierValue(securityId),
            JsonSerializer.SerializeToElement(new { displayName = DisplayName, currency = Currency }),
            terms.RootElement.Clone(),
            JsonSerializer.SerializeToElement(new { sourceSystem = "integration-test", updatedBy = "registry-golden-fixture" }),
            Version,
            effectiveFrom,
            null,
            [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, IdentifierValue(securityId), true, effectiveFrom)],
            []);
    }
}
