using System.Data;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The reference-projection stores read their rows by column name from the select the registry
/// descriptor defines, so a store can only break in two ways: by naming a column the descriptor
/// does not carry, or by reading a column as the wrong type. Both are caught here without a
/// database. Each registry-backed store's mapper runs over a <see cref="DataTable"/> whose columns
/// are exactly the descriptor's select list, filled from a sample row that itself may only name
/// descriptor columns; a missing name fails at <c>GetOrdinal</c> and a wrong type fails at the
/// typed read. Bond and Option stay specialized, so their mappers run over their own select lists.
/// </summary>
public sealed class PostgresReferenceProjectionReaderTests
{
    private static readonly Guid SecurityId = Guid.Parse("7f1d4a6e-3b2c-4d5e-8f90-1a2b3c4d5e6f");
    private static readonly DateTime Maturity = new(2031, 6, 15);
    private static readonly SecurityMasterOptions Options = new() { Schema = "sm_test", ConnectionString = "Host=unused" };

    [Fact]
    public void SelectSql_CarriesTheDescriptorColumnsInWriteOrder_AndQualifiesTheTable()
    {
        var reader = new PostgresReferenceProjectionReader(Options);

        reader.SelectSql("Deposit", "where security_id = @security_id").Should().Be(
            "select security_id, display_name, currency, deposit_type, institution_name, maturity_date, "
            + "interest_rate, day_count, is_callable, primary_identifier_value, version "
            + "from sm_test.deposit_projection where security_id = @security_id;");
    }

    [Fact]
    public void SelectSql_DropsCurrencyForADescriptorThatDoesNotProjectIt()
    {
        var reader = new PostgresReferenceProjectionReader(Options);

        reader.SelectSql("CryptoCurrency", "where security_id = @security_id").Should().Be(
            "select security_id, display_name, base_currency, quote_currency, network, primary_identifier_value, version "
            + "from sm_test.crypto_projection where security_id = @security_id;");
    }

    [Fact]
    public void SelectChildSql_CarriesTheKeyColumnsThenTheElementColumns()
    {
        var reader = new PostgresReferenceProjectionReader(Options);

        reader.SelectChildSql("DirectLoan", "direct_loan_covenant_projection", "where security_id = @security_id order by ordinal")
            .Should().Be(
                "select security_id, ordinal, covenant_type, threshold, notes "
                + "from sm_test.direct_loan_covenant_projection where security_id = @security_id order by ordinal;");
    }

    [Fact]
    public void SelectSql_RefusesAClassWithoutARegistryDescriptor_AndAnUnknownChildTable()
    {
        var reader = new PostgresReferenceProjectionReader(Options);

        // Option and CustomAsset (profile-keyed rows) are specialized writers, not descriptors. Bond now
        // has a descriptor whose parent table carries no display name or currency in its spine.
        reader.Invoking(r => r.SelectSql("Option", "")).Should().Throw<ArgumentException>().WithMessage("*Option*");
        reader.Invoking(r => r.SelectSql("CustomAsset", "")).Should().Throw<ArgumentException>();
        reader.SelectSql("Bond", "").Should().StartWith("select security_id, issuer_name, ").And.NotContain("display_name");
        reader.Invoking(r => r.SelectChildSql("DirectLoan", "direct_loan_waterfall_projection", ""))
            .Should().Throw<ArgumentException>().WithMessage("*direct_loan_waterfall_projection*");
    }

    [Fact]
    public void ProjectionRow_NamesTheMissingColumn()
    {
        using var reader = TableReader(["security_id"], new Dictionary<string, object> { ["security_id"] = SecurityId });

        var row = new ProjectionRow(reader);

        row.Invoking(r => r.String("display_name")).Should().Throw<InvalidOperationException>().WithMessage("*display_name*");
    }

    [Fact]
    public void EveryStoreOnTheRegistry_NamesItsDescriptor()
    {
        // The store constants are what SelectSql resolves; a renamed class must be renamed here too.
        string[] storeClasses =
        [
            PostgresEquityReferenceProjectionStore.AssetClass,
            PostgresFutureReferenceProjectionStore.AssetClass,
            PostgresFxSpotReferenceProjectionStore.AssetClass,
            PostgresSwapReferenceProjectionStore.AssetClass,
            PostgresCommodityReferenceProjectionStore.AssetClass,
            PostgresCryptoReferenceProjectionStore.AssetClass,
            PostgresDepositReferenceProjectionStore.AssetClass,
            PostgresMoneyMarketFundReferenceProjectionStore.AssetClass,
            PostgresCertificateOfDepositReferenceProjectionStore.AssetClass,
            PostgresDirectLoanReferenceProjectionStore.AssetClass,
            PostgresStructuredCreditReferenceProjectionStore.AssetClass
        ];

        foreach (var assetClass in storeClasses)
        {
            SecurityTermsProjectionRegistry.Require(assetClass).AssetClass.Should().Be(assetClass);
            SecurityAssetClassCatalog.GetOrDefault(assetClass).ProjectionKind
                .Should().Be(SecurityTermsProjectionKind.Declarative, $"{assetClass} reads a registry-written table");
        }

        SecurityTermsProjectionRegistry.Require(PostgresDirectLoanReferenceProjectionStore.AssetClass).ChildTables
            .Select(child => child.TableName).Should().BeEquivalentTo(new[]
            {
                PostgresDirectLoanReferenceProjectionStore.CovenantTable,
                PostgresDirectLoanReferenceProjectionStore.PrincipalScheduleTable
            });
        SecurityTermsProjectionRegistry.Require(PostgresStructuredCreditReferenceProjectionStore.AssetClass).ChildTables
            .Select(child => child.TableName).Should().BeEquivalentTo(new[]
            {
                PostgresStructuredCreditReferenceProjectionStore.FactorScheduleTable
            });

        // Every typed store reads through the generic registry store, one per table it reads, and
        // the generic store's class and child table are the ones the constants name.
        var tables = RegistryStoreQueries().Select(entry => (entry.AssetClass, entry.ChildTable)).Distinct().ToArray();
        tables.Select(table => table.AssetClass).Distinct().Should().BeEquivalentTo(storeClasses);
        tables.Should().Contain((PostgresDirectLoanReferenceProjectionStore.AssetClass, PostgresDirectLoanReferenceProjectionStore.CovenantTable));
        tables.Should().Contain((PostgresDirectLoanReferenceProjectionStore.AssetClass, PostgresDirectLoanReferenceProjectionStore.PrincipalScheduleTable));
        tables.Should().Contain((PostgresStructuredCreditReferenceProjectionStore.AssetClass, PostgresStructuredCreditReferenceProjectionStore.FactorScheduleTable));
    }

    /// <summary>
    /// The predicate text each hand-written store appended to its select before the stores moved
    /// onto <see cref="RegistryReferenceProjectionStore{TRow}"/>, copied literally. Each generic
    /// store must send exactly <c>SelectSql</c>/<c>SelectChildSql</c> over that text, and must
    /// declare nothing else, so the conversion is pinned to the SQL the old stores sent.
    /// </summary>
    private static readonly (string AssetClass, string? ChildTable, string Query, string Predicate)[] HandWrittenPredicates =
    [
        ("CryptoCurrency", null, "BySecurityId", "where security_id = @security_id"),
        ("CryptoCurrency", null, "ByNetwork", "where lower(network) = lower(@network)\norder by display_name"),
        ("CryptoCurrency", null, "ByBaseCurrency", "where base_currency = @base_currency\norder by display_name"),
        ("Deposit", null, "BySecurityId", "where security_id = @security_id"),
        ("Deposit", null, "ByInstitution", "where lower(institution_name) = lower(@institution_name)\norder by maturity_date"),
        ("Deposit", null, "MaturingBefore", "where maturity_date is not null\n  and maturity_date < @before_date\norder by maturity_date"),
        ("MoneyMarketFund", null, "BySecurityId", "where security_id = @security_id"),
        ("MoneyMarketFund", null, "ByFundFamily", "where lower(fund_family) = lower(@fund_family)\norder by display_name"),
        ("MoneyMarketFund", null, "BySweepEligibility", "where sweep_eligible = @sweep_eligible\norder by display_name"),
        ("CertificateOfDeposit", null, "BySecurityId", "where security_id = @security_id"),
        ("CertificateOfDeposit", null, "ByIssuer", "where lower(issuer_name) = lower(@issuer_name)\norder by maturity_date"),
        ("CertificateOfDeposit", null, "MaturingBefore", "where maturity_date < @before_date\norder by maturity_date"),
        ("Equity", null, "BySecurityId", "where security_id = @security_id"),
        ("Equity", null, "ByExchange", "where exchange_code = @exchange_code\norder by display_name"),
        ("Equity", null, "ByIssuer", "where lower(issuer_name) = lower(@issuer_name)\norder by display_name"),
        ("FxSpot", null, "BySecurityId", "where security_id = @security_id"),
        ("FxSpot", null, "ByPairCode", "where pair_code = @pair_code"),
        ("FxSpot", null, "ByCurrency", "where base_currency = @currency or quote_currency = @currency\norder by pair_code"),
        ("Swap", null, "BySecurityId", "where security_id = @security_id"),
        ("Swap", null, "BySwapType", "where lower(swap_type) = lower(@swap_type)\norder by maturity_date"),
        ("Swap", null, "MaturingBefore", "where maturity_date < @before_date\n  and lifecycle_stat not in ('Matured', 'Terminated')\norder by maturity_date"),
        ("Commodity", null, "BySecurityId", "where security_id = @security_id"),
        ("Commodity", null, "ByCommodityType", "where lower(commodity_type) = lower(@commodity_type)\norder by display_name"),
        ("Commodity", null, "ByExchange", "where exchange_code = @exchange_code\norder by display_name"),
        ("Future", null, "BySecurityId", "where security_id = @security_id"),
        ("Future", null, "ByRootSymbol", "where root_symbol = @root_symbol\norder by expiry_date"),
        ("DirectLoan", null, "BySecurityId", "where security_id = @security_id"),
        ("DirectLoan", null, "ByBorrower", "where lower(borrower) = lower(@borrower)\norder by maturity_date nulls last, display_name"),
        ("DirectLoan", null, "ByReferenceIndex", "where lower(reference_index) = lower(@reference_index)\norder by maturity_date nulls last, display_name"),
        ("DirectLoan", null, "MaturityLadder", "where maturity_date between @from_date and @to_date\norder by maturity_date, display_name"),
        ("DirectLoan", "direct_loan_covenant_projection", "ChildrenBySecurity", "where security_id = @security_id\norder by ordinal"),
        ("DirectLoan", "direct_loan_principal_schedule_projection", "ChildrenBySecurity", "where security_id = @security_id\norder by ordinal"),
        ("DirectLoan", "direct_loan_principal_schedule_projection", "PrincipalPaymentsDue", "where payment_date between @from_date and @to_date\norder by payment_date, security_id, ordinal"),
        ("StructuredCredit", null, "BySecurityId", "where security_id = @security_id"),
        ("StructuredCredit", null, "ByPool", "where lower(pool_id) = lower(@pool_id)\norder by tranche, display_name"),
        ("StructuredCredit", null, "ByCollateralType", "where lower(collateral_type) = lower(@collateral_type)\norder by maturity_date nulls last, display_name"),
        ("StructuredCredit", "structured_credit_factor_schedule_projection", "FactorScheduleBySecurity", "where security_id = @security_id\norder by ordinal"),
        ("StructuredCredit", "structured_credit_factor_schedule_projection", "FactorAsOf", "where security_id = @security_id and as_of_date <= @as_of_date\norder by as_of_date desc, ordinal desc\nlimit 1"),
    ];

    [Fact]
    public void RegistryStores_SendExactlyTheSqlTheHandWrittenStoresSent()
    {
        var reader = new PostgresReferenceProjectionReader(Options);
        var actual = RegistryStoreQueries().ToArray();

        actual.Select(entry => (entry.AssetClass, entry.ChildTable, entry.Query))
            .Should().BeEquivalentTo(HandWrittenPredicates.Select(entry => (entry.AssetClass, entry.ChildTable, entry.Query)),
                "a store declares exactly the reads it had before the move");

        foreach (var expected in HandWrittenPredicates)
        {
            var expectedSql = expected.ChildTable is null
                ? reader.SelectSql(expected.AssetClass, expected.Predicate)
                : reader.SelectChildSql(expected.AssetClass, expected.ChildTable, expected.Predicate);
            actual.Single(entry => entry.AssetClass == expected.AssetClass
                    && entry.ChildTable == expected.ChildTable
                    && entry.Query == expected.Query)
                .Sql.Should().Be(expectedSql, $"{expected.AssetClass}/{expected.ChildTable}/{expected.Query}");
        }
    }

    [Fact]
    public void RegistryStore_RefusesAnUndeclaredQuery_ADuplicateName_AndAnUnknownTable()
    {
        var store = PostgresCryptoReferenceProjectionStore.CreateStore(Options);
        var undeclared = new RegistryProjectionQuery("ByIssuer", "where issuer_name = @issuer_name");
        store.Invoking(s => s.SqlFor(undeclared)).Should().Throw<InvalidOperationException>().WithMessage("*CryptoCurrency*ByIssuer*");

        // Same name, different text: a query object the store did not declare is refused too.
        var impostor = PostgresCryptoReferenceProjectionStore.ByNetwork with { PredicateSql = "where true" };
        store.Invoking(s => s.SqlFor(impostor)).Should().Throw<InvalidOperationException>();

        var duplicate = () => new RegistryReferenceProjectionStore<CryptoProjectionRow>(
            Options, "CryptoCurrency", PostgresCryptoReferenceProjectionStore.MapRow,
            [PostgresCryptoReferenceProjectionStore.ByNetwork, PostgresCryptoReferenceProjectionStore.ByNetwork]);
        duplicate.Should().Throw<ArgumentException>().WithMessage("*ByNetwork*");

        // A parent store declares the by-security read itself; restating it is a duplicate.
        var restated = () => new RegistryReferenceProjectionStore<CryptoProjectionRow>(
            Options, "CryptoCurrency", PostgresCryptoReferenceProjectionStore.MapRow, [RegistryProjectionQuery.BySecurityId]);
        restated.Should().Throw<ArgumentException>();

        var unknownClass = () => new RegistryReferenceProjectionStore<CryptoProjectionRow>(
            Options, "Option", PostgresCryptoReferenceProjectionStore.MapRow, []);
        unknownClass.Should().Throw<ArgumentException>().WithMessage("*Option*");

        var unknownChild = () => new RegistryReferenceProjectionStore<DirectLoanCovenantRow>(
            Options, "DirectLoan", PostgresDirectLoanReferenceProjectionStore.MapCovenantRow,
            [PostgresDirectLoanReferenceProjectionStore.ChildrenBySecurity], "direct_loan_waterfall_projection");
        unknownChild.Should().Throw<ArgumentException>().WithMessage("*direct_loan_waterfall_projection*");

        // A child store answers only what it declares: there is no implicit by-security read.
        PostgresDirectLoanReferenceProjectionStore.CreateCovenantStore(Options).Queries
            .Select(query => query.Name).Should().Equal("ChildrenBySecurity");
    }

    [Fact]
    public void SelectSatelliteSql_CarriesTheSatelliteSpine_AndRefusesAnUnknownSatellite()
    {
        var reader = new PostgresReferenceProjectionReader(Options);
        var bond = SecurityTermsProjectionRegistry.Require("Bond");
        bond.Satellites.Should().NotBeEmpty();

        foreach (var satellite in bond.Satellites)
        {
            reader.SelectSatelliteSql("Bond", satellite.TableName, "where security_id = @security_id").Should().Be(
                $"select {string.Join(", ", SecurityTermsProjectionRegistry.SatelliteColumnNames(satellite))} "
                + $"from sm_test.{satellite.TableName} where security_id = @security_id;");
        }

        reader.Invoking(r => r.SelectSatelliteSql("Bond", "bond_waterfall_projection", ""))
            .Should().Throw<ArgumentException>().WithMessage("*bond_waterfall_projection*");
    }

    [Fact]
    public void ToJsonColumns_WritesEveryNamedColumnInOrder_WithNullsAndTypedValues()
    {
        using var reader = TableReader(
            ["security_id", "display_name", "amount", "version", "is_callable", "missing_value"],
            new Dictionary<string, object>
            {
                ["security_id"] = SecurityId,
                ["display_name"] = "Display",
                ["amount"] = 1234.5600m,
                ["version"] = 3L,
                ["is_callable"] = true
            });
        reader.Read().Should().BeTrue();

        var json = new ProjectionRow(reader).ToJsonColumns(["version", "security_id", "display_name", "amount", "is_callable", "missing_value"]);

        json.Keys.Should().Equal("version", "security_id", "display_name", "amount", "is_callable", "missing_value");
        json["security_id"].GetString().Should().Be(SecurityId.ToString("D"));
        json["display_name"].GetString().Should().Be("Display");
        json["amount"].GetRawText().Should().Be("1234.5600", "decimal text is kept exactly");
        json["version"].GetInt64().Should().Be(3L);
        json["is_callable"].GetBoolean().Should().BeTrue();
        json["missing_value"].ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        new ProjectionRow(reader).Invoking(row => row.ToJsonColumns(["not_selected"]))
            .Should().Throw<InvalidOperationException>().WithMessage("*not_selected*");
    }

    [Fact]
    public void ProjectionJsonValue_WritesDatesAsDates_AndTimestampsAsUtc()
    {
        ProjectionJsonValue.From(new DateTime(2031, 6, 15), "date").GetString().Should().Be("2031-06-15");
        ProjectionJsonValue.From(new DateTime(2026, 10, 18, 1, 2, 3, DateTimeKind.Unspecified), "timestamp without time zone")
            .GetString().Should().Be("2026-10-18T01:02:03.0000000+00:00");
        ProjectionJsonValue.From(new DateOnly(2030, 1, 2), "date").GetString().Should().Be("2030-01-02");
        ProjectionJsonValue.From(7, "integer").GetInt32().Should().Be(7);
        ProjectionJsonValue.From(DBNull.Value, "text").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        ProjectionJsonValue.From(null, "text").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    /// <summary>Every query every converted store declares, with the SQL it sends.</summary>
    private static IEnumerable<(string AssetClass, string? ChildTable, string Query, string Sql)> RegistryStoreQueries()
    {
        foreach (var entry in Describe(PostgresCryptoReferenceProjectionStore.CreateStore(Options))) yield return entry;
        foreach (var entry in Describe(PostgresDepositReferenceProjectionStore.CreateStore(Options))) yield return entry;
        foreach (var entry in Describe(PostgresMoneyMarketFundReferenceProjectionStore.CreateStore(Options))) yield return entry;
        foreach (var entry in Describe(PostgresCertificateOfDepositReferenceProjectionStore.CreateStore(Options))) yield return entry;
        foreach (var entry in Describe(PostgresEquityReferenceProjectionStore.CreateStore(Options))) yield return entry;
        foreach (var entry in Describe(PostgresFxSpotReferenceProjectionStore.CreateStore(Options))) yield return entry;
        foreach (var entry in Describe(PostgresSwapReferenceProjectionStore.CreateStore(Options))) yield return entry;
        foreach (var entry in Describe(PostgresCommodityReferenceProjectionStore.CreateStore(Options))) yield return entry;
        foreach (var entry in Describe(PostgresFutureReferenceProjectionStore.CreateStore(Options))) yield return entry;
        foreach (var entry in Describe(PostgresDirectLoanReferenceProjectionStore.CreateLoanStore(Options))) yield return entry;
        foreach (var entry in Describe(PostgresDirectLoanReferenceProjectionStore.CreateCovenantStore(Options))) yield return entry;
        foreach (var entry in Describe(PostgresDirectLoanReferenceProjectionStore.CreatePrincipalScheduleStore(Options))) yield return entry;
        foreach (var entry in Describe(PostgresStructuredCreditReferenceProjectionStore.CreateTrancheStore(Options))) yield return entry;
        foreach (var entry in Describe(PostgresStructuredCreditReferenceProjectionStore.CreateFactorScheduleStore(Options))) yield return entry;
    }

    private static IEnumerable<(string AssetClass, string? ChildTable, string Query, string Sql)> Describe<TRow>(
        RegistryReferenceProjectionStore<TRow> store)
        where TRow : class
        => store.Queries.Select(query => (store.AssetClass, store.ChildTableName, query.Name, store.SqlFor(query)));

    [Fact]
    public void Equity_MapsTheDescriptorSelect()
    {
        var row = MapParent("Equity", PostgresEquityReferenceProjectionStore.MapRow, Spine("USD")
            .With("share_class", "A").With("voting_rights_cat", "OneVote").With("classification", "Common")
            .With("exchange_code", "XNAS").With("country_of_risk", "US").With("issuer_name", "Apple Inc."));

        row.Should().Be(new EquityProjectionRow(SecurityId, "Display", "USD", "A", "OneVote", "Common", "XNAS", "US", "Apple Inc.", "PRIMARY", 3L));

        var sparse = MapParent("Equity", PostgresEquityReferenceProjectionStore.MapRow, Spine("USD"));
        sparse.ShareClass.Should().BeNull();
        sparse.IssuerName.Should().BeNull();
    }

    [Fact]
    public void Future_MapsTheDescriptorSelect()
    {
        var row = MapParent("Future", PostgresFutureReferenceProjectionStore.MapRow, Spine("USD")
            .With("root_symbol", "ES").With("contract_month", "202512").With("expiry_date", Maturity)
            .With("multiplier", 50m).With("settlement_type", "Cash").With("is_roll_target", true)
            .With("roll_window_days", 5).With("last_trading_dt", Maturity).With("first_notice_dt", Maturity)
            .With("delivery_month_dt", Maturity).With("delivery_location_code", "CME").With("lifecycle_stat", "Active"));

        row.Should().Be(new FutureProjectionRow(
            SecurityId, "Display", "USD", "ES", "202512", DateOnly.FromDateTime(Maturity), 50m, "Cash", true, 5,
            DateOnly.FromDateTime(Maturity), DateOnly.FromDateTime(Maturity), DateOnly.FromDateTime(Maturity),
            "CME", "Active", "PRIMARY", 3L));
    }

    [Fact]
    public void FxSpot_MapsTheDescriptorSelect_WithoutACurrencyColumn()
    {
        var row = MapParent("FxSpot", PostgresFxSpotReferenceProjectionStore.MapRow, Spine(currency: null)
            .With("base_currency", "EUR").With("quote_currency", "USD").With("pair_code", "EURUSD"));

        row.Should().Be(new FxSpotProjectionRow(SecurityId, "Display", "EUR", "USD", "EURUSD", "PRIMARY", 3L));
    }

    [Fact]
    public void Swap_MapsTheDescriptorSelect()
    {
        var row = MapParent("Swap", PostgresSwapReferenceProjectionStore.MapRow, Spine("USD")
            .With("swap_type", "IRS").With("effective_date", new DateTime(2025, 1, 2)).With("maturity_date", Maturity)
            .With("lifecycle_stat", "Active"));

        row.Should().Be(new SwapProjectionRow(SecurityId, "Display", "USD", "IRS", new DateOnly(2025, 1, 2), DateOnly.FromDateTime(Maturity), "Active", "PRIMARY", 3L));
    }

    [Fact]
    public void Commodity_MapsTheDescriptorSelect()
    {
        var row = MapParent("Commodity", PostgresCommodityReferenceProjectionStore.MapRow, Spine("USD")
            .With("commodity_type", "Energy").With("denomination", "bbl").With("contract_size", 1000m)
            .With("exchange_code", "XNYM").With("delivery_country", "US"));

        row.Should().Be(new CommodityProjectionRow(SecurityId, "Display", "USD", "Energy", "bbl", 1000m, "XNYM", "US", "PRIMARY", 3L));
    }

    [Fact]
    public void Crypto_MapsTheDescriptorSelect_WithoutACurrencyColumn()
    {
        var row = MapParent("CryptoCurrency", PostgresCryptoReferenceProjectionStore.MapRow, Spine(currency: null)
            .With("base_currency", "BTC").With("quote_currency", "USD").With("network", "Bitcoin"));

        row.Should().Be(new CryptoProjectionRow(SecurityId, "Display", "BTC", "USD", "Bitcoin", "PRIMARY", 3L));
    }

    [Fact]
    public void Deposit_MapsTheDescriptorSelect()
    {
        var row = MapParent("Deposit", PostgresDepositReferenceProjectionStore.MapRow, Spine("USD")
            .With("deposit_type", "Term").With("institution_name", "Bank").With("maturity_date", Maturity)
            .With("interest_rate", 0.045m).With("day_count", "ACT/360").With("is_callable", false));

        row.Should().Be(new DepositProjectionRow(SecurityId, "Display", "USD", "Term", "Bank", DateOnly.FromDateTime(Maturity), 0.045m, "ACT/360", false, "PRIMARY", 3L));
    }

    [Fact]
    public void MoneyMarketFund_MapsTheDescriptorSelect()
    {
        var row = MapParent("MoneyMarketFund", PostgresMoneyMarketFundReferenceProjectionStore.MapRow, Spine("USD")
            .With("fund_family", "Fidelity").With("sweep_eligible", true).With("weighted_average_maturity_days", 32)
            .With("liquidity_fee_eligible", false));

        row.Should().Be(new MoneyMarketFundProjectionRow(SecurityId, "Display", "USD", "Fidelity", true, 32, false, "PRIMARY", 3L));
    }

    [Fact]
    public void CertificateOfDeposit_MapsTheDescriptorSelect()
    {
        var row = MapParent("CertificateOfDeposit", PostgresCertificateOfDepositReferenceProjectionStore.MapRow, Spine("USD")
            .With("issuer_name", "Bank").With("maturity_date", Maturity).With("coupon_rate", 0.05m)
            .With("callable_date", Maturity).With("day_count", "30/360"));

        row.Should().Be(new CertificateOfDepositProjectionRow(SecurityId, "Display", "USD", "Bank", DateOnly.FromDateTime(Maturity), 0.05m, DateOnly.FromDateTime(Maturity), "30/360", "PRIMARY", 3L));
    }

    [Fact]
    public void DirectLoan_MapsTheDescriptorSelect_AndBothChildTables()
    {
        var loan = MapParent("DirectLoan", PostgresDirectLoanReferenceProjectionStore.MapLoanRow, Spine("USD")
            .With("borrower", "Acme").With("maturity_date", Maturity).With("reference_index", "SOFR")
            .With("spread_bps", 450m).With("current_coupon_rate", 0.0875m).With("reset_frequency", "Quarterly")
            .With("pricing_source", "Agent"));
        loan.Should().Be(new DirectLoanProjectionRow(SecurityId, "Display", "USD", "Acme", DateOnly.FromDateTime(Maturity), "SOFR", 450m, 0.0875m, "Quarterly", "Agent", "PRIMARY", 3L));

        var covenant = MapChild("DirectLoan", PostgresDirectLoanReferenceProjectionStore.CovenantTable,
            PostgresDirectLoanReferenceProjectionStore.MapCovenantRow,
            Child(2).With("covenant_type", "Leverage").With("threshold", "4.5x").With("notes", "Tested quarterly"));
        covenant.Should().Be(new DirectLoanCovenantRow(SecurityId, 2, "Leverage", "4.5x", "Tested quarterly"));

        var payment = MapChild("DirectLoan", PostgresDirectLoanReferenceProjectionStore.PrincipalScheduleTable,
            PostgresDirectLoanReferenceProjectionStore.MapPrincipalPaymentRow,
            Child(1).With("payment_date", Maturity).With("amount", 250000m));
        payment.Should().Be(new DirectLoanPrincipalPaymentRow(SecurityId, 1, DateOnly.FromDateTime(Maturity), 250000m));
    }

    [Fact]
    public void StructuredCredit_MapsTheDescriptorSelect_AndTheFactorSchedule()
    {
        var tranche = MapParent("StructuredCredit", PostgresStructuredCreditReferenceProjectionStore.MapTrancheRow, Spine("USD")
            .With("tranche", "A1").With("pool_id", "POOL-1").With("collateral_type", "CLO")
            .With("original_face", 1_000_000m).With("current_factor", 0.85m).With("coupon_or_index", "SOFR+150")
            .With("factor_schedule_reference", "Trustee report").With("maturity_date", Maturity)
            .With("structured_credit_kind", "Clo").With("seniority", "Mezzanine").With("attachment_point", 0.12m)
            .With("detachment_point", 0.20m).With("weighted_average_coupon", 7.85m).With("weighted_average_maturity_months", 58)
            .With("weighted_average_loan_age_months", 14).With("reinvestment_period_end", Maturity).With("non_call_period_end", Maturity));
        tranche.Should().Be(new StructuredCreditProjectionRow(
            SecurityId, "Display", "USD", "A1", "POOL-1", "CLO", 1_000_000m, 0.85m, "SOFR+150", "Trustee report", DateOnly.FromDateTime(Maturity), "PRIMARY", 3L,
            "Clo", "Mezzanine", 0.12m, 0.20m, 7.85m, 58, 14, DateOnly.FromDateTime(Maturity), DateOnly.FromDateTime(Maturity)));

        // A version-1 row (written before migration 045) reads with the typed terms unset.
        var legacy = MapParent("StructuredCredit", PostgresStructuredCreditReferenceProjectionStore.MapTrancheRow, Spine("USD")
            .With("tranche", "A1").With("collateral_type", "CLO").With("original_face", 1_000_000m).With("coupon_or_index", "SOFR+150"));
        legacy.StructuredCreditKind.Should().BeNull();
        legacy.AttachmentPoint.Should().BeNull();
        legacy.NonCallPeriodEnd.Should().BeNull();

        var factor = MapChild("StructuredCredit", PostgresStructuredCreditReferenceProjectionStore.FactorScheduleTable,
            PostgresStructuredCreditReferenceProjectionStore.MapFactorScheduleRow,
            Child(0).With("as_of_date", Maturity).With("factor", 0.85m));
        factor.Should().Be(new StructuredCreditFactorScheduleRow(SecurityId, 0, DateOnly.FromDateTime(Maturity), 0.85m));
    }

    [Fact]
    public void Bond_MapsItsJoinedSelect_AndItsLifecycleAndAccrualChildren()
    {
        string[] joined =
        [
            "security_id", "display_name", "currency", "primary_identifier_value", "issuer_name", "seniority",
            "maturity_date", "lifecycle_stat", "issue_date", "call_date", "is_callable", "day_count_convention",
            "settlement_cycle_days", "holiday_calendar_id", "coupon_kind", "fixed_coupon_rate", "floating_rate_index",
            "floating_spread_bps", "version", "subclass", "par", "payment_frequency", "legal_final_maturity",
            "pre_refund_date", "mandatory_put_date", "asset_specific_terms", "effective_to_date"
        ];
        var bond = Map(joined, PostgresBondReferenceProjectionStore.MapBondRow, Spine("USD")
            .With("issuer_name", "Issuer").With("seniority", "Senior").With("maturity_date", Maturity)
            .With("lifecycle_stat", "Active").With("is_callable", true).With("settlement_cycle_days", 2)
            .With("coupon_kind", "Fixed").With("fixed_coupon_rate", 0.05m).With("par", 1000m));
        bond.Should().Be(new BondProjectionRow(
            SecurityId, "Display", "USD", "PRIMARY", "Issuer", "Senior", DateOnly.FromDateTime(Maturity), "Active",
            null, null, true, null, 2, null, "Fixed", 0.05m, null, null, 3L, null, 1000m, null, null, null, null));

        string[] lifecycle =
        [
            "security_id", "lifecycle_stat", "issue_date", "call_date", "maturity_date", "is_callable", "version",
            "subclass", "par", "payment_frequency", "legal_final_maturity", "pre_refund_date", "mandatory_put_date", "effective_to_date"
        ];
        var lifecycleRow = Map(lifecycle, PostgresBondReferenceProjectionStore.MapLifecycleRow, new Sample()
            .With("security_id", SecurityId).With("lifecycle_stat", "Active").With("maturity_date", Maturity)
            .With("is_callable", false).With("version", 3L).With("payment_frequency", "SemiAnnual"));
        lifecycleRow.Should().Be(new BondLifecycleProjectionRow(SecurityId, "Active", null, null, DateOnly.FromDateTime(Maturity), false, 3L, null, null, "SemiAnnual"));

        string[] accrual =
        [
            "security_id", "day_count_convention", "settlement_cycle_days", "holiday_calendar_id", "coupon_kind",
            "fixed_coupon_rate", "floating_rate_index", "floating_spread_bps", "version"
        ];
        var accrualRow = Map(accrual, PostgresBondReferenceProjectionStore.MapAccrualConventionRow, new Sample()
            .With("security_id", SecurityId).With("day_count_convention", "30/360").With("floating_rate_index", "SOFR")
            .With("floating_spread_bps", 125m).With("version", 3L));
        accrualRow.Should().Be(new BondAccrualConventionProjectionRow(SecurityId, "30/360", null, null, null, null, "SOFR", 125m, 3L));
    }

    [Fact]
    public void Option_MapsItsContractSelect()
    {
        string[] contract =
        [
            "contract_symbol", "security_id", "option_chain_id", "underlying_symbol", "underlying_security_id",
            "put_call", "strike", "expiry_date", "multiplier", "is_adjusted", "last_trading_dt", "lifecycle_stat",
            "updated_at", "version"
        ];
        var updated = new DateTime(2026, 10, 18, 1, 2, 3, DateTimeKind.Unspecified);

        var row = Map(contract, PostgresOptionReferenceProjectionStore.MapRow, new Sample()
            .With("contract_symbol", "AAPL241220C00150000").With("option_chain_id", "AAPL-20241220")
            .With("underlying_symbol", "AAPL").With("underlying_security_id", SecurityId).With("put_call", "Call")
            .With("strike", 150m).With("expiry_date", Maturity).With("multiplier", 100m).With("is_adjusted", false)
            .With("lifecycle_stat", "Active").With("updated_at", updated).With("version", 7L));

        row.Should().Be(new OptionContractProjectionRow(
            "AAPL241220C00150000", null, "AAPL-20241220", "AAPL", SecurityId, "Call", 150m, DateOnly.FromDateTime(Maturity),
            100m, false, null, "Active", new DateTimeOffset(updated, TimeSpan.Zero), 7L));
    }

    private static TRow MapParent<TRow>(string assetClass, Func<ProjectionRow, TRow> map, Sample sample)
    {
        var descriptor = SecurityTermsProjectionRegistry.Require(assetClass);
        return Map(SecurityTermsProjectionRegistry.ParentColumnNames(descriptor), map, sample);
    }

    private static TRow MapChild<TRow>(string assetClass, string childTable, Func<ProjectionRow, TRow> map, Sample sample)
    {
        var child = SecurityTermsProjectionRegistry.Require(assetClass).ChildTables
            .Single(table => table.TableName == childTable);
        return Map(SecurityTermsProjectionRegistry.ChildColumnNames(child), map, sample);
    }

    private static TRow Map<TRow>(IReadOnlyList<string> columns, Func<ProjectionRow, TRow> map, Sample sample)
    {
        sample.Values.Keys.Should().BeSubsetOf(columns, "the sample row may only name columns the select carries");

        using var reader = TableReader(columns, sample.Values);
        reader.Read().Should().BeTrue();
        return map(new ProjectionRow(reader));
    }

    private static DataTableReader TableReader(IReadOnlyList<string> columns, IReadOnlyDictionary<string, object> values)
    {
        var table = new DataTable();
        foreach (var column in columns)
        {
            table.Columns.Add(column, typeof(object));
        }

        var row = table.NewRow();
        foreach (var (column, value) in values)
        {
            row[column] = value;
        }

        table.Rows.Add(row);
        return table.CreateDataReader();
    }

    /// <summary>The identity spine every parent projection carries; <paramref name="currency"/> null for a class that does not project it.</summary>
    private static Sample Spine(string? currency)
    {
        var sample = new Sample()
            .With("security_id", SecurityId)
            .With("display_name", "Display")
            .With("primary_identifier_value", "PRIMARY")
            .With("version", 3L);
        return currency is null ? sample : sample.With("currency", currency);
    }

    private static Sample Child(int ordinal)
        => new Sample().With("security_id", SecurityId).With("ordinal", ordinal);

    private sealed class Sample
    {
        public Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);

        public Sample With(string column, object value)
        {
            Values[column] = value;
            return this;
        }
    }
}
