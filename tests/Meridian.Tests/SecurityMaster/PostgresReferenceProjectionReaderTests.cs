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

        // Option is the one remaining specialized writer; CustomAsset is profile-governed. Bond now
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
    }

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
