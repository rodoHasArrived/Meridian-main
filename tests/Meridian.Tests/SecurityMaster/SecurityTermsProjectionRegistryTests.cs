using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Contract guards for the schema-driven relational terms projections.
/// <para>
/// A declarative projection trades hand-written SQL for a table of column-to-term bindings, which
/// only helps while those bindings are checked. These tests are that check: the registry must agree
/// with <see cref="SecurityAssetTermsSchema"/> on every key and type it reads, the generated SQL
/// must carry the same upsert shape the hand-written projections spell out, and the decode half must
/// refuse to publish a partial projection. The write path itself is only reachable through
/// integration tests, which the pull-request gate excludes — so everything assertable without a
/// database is asserted here.
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityTermsProjectionRegistryTests
{
    [Fact]
    public void Registry_AgreesWithTheTermsSchemaAndTheCatalog()
    {
        SecurityTermsProjectionRegistry.ValidationIssues.Should().BeEmpty(
            "a projected column that reads a key the terms contract does not declare is the exact drift "
            + "SecurityAssetTermsSchema exists to prevent");
    }

    [Fact]
    public void Registry_CoversTheAssetOperationsClassesItClaims()
    {
        SecurityTermsProjectionRegistry.AssetClasses
            .Should().BeSubsetOf(PostgresSecurityMasterStore.ProjectedAssetClasses,
                "a descriptor is only a projection once the store fans out to it");
    }

    [Fact]
    public void Validate_RejectsAColumnReadingATermTheSchemaDoesNotDeclare()
    {
        var issues = SecurityTermsProjectionRegistry.Validate(
        [
            new(
                AssetClass: "DirectLoan",
                TableName: "direct_loan_probe_projection",
                Columns: [SecurityTermsProjectionColumn.Optional("servicer", "servicer", SecurityAssetTermFieldType.String)],
                ChildTables: [])
        ]);

        issues.Should().ContainSingle().Which.Should().Contain("servicer").And.Contain("does not declare");
    }

    [Fact]
    public void Validate_RejectsAColumnReadingADeclaredTermAsTheWrongType()
    {
        var issues = SecurityTermsProjectionRegistry.Validate(
        [
            new(
                AssetClass: "DirectLoan",
                TableName: "direct_loan_probe_projection",
                // spreadBps is a Decimal in the terms contract; reading it as a date would project null.
                Columns: [SecurityTermsProjectionColumn.Optional("spread_bps", "spreadBps", SecurityAssetTermFieldType.Date)],
                ChildTables: [])
        ]);

        issues.Should().ContainSingle().Which.Should().Contain("spreadBps").And.Contain("Decimal");
    }

    [Fact]
    public void Validate_RejectsGatingOnAnOptionalTerm()
    {
        var issues = SecurityTermsProjectionRegistry.Validate(
        [
            new(
                AssetClass: "DirectLoan",
                TableName: "direct_loan_probe_projection",
                Columns: [SecurityTermsProjectionColumn.Gate("maturity_date", "maturity", SecurityAssetTermFieldType.Date)],
                ChildTables: [])
        ]);

        issues.Should().ContainSingle().Which.Should().Contain("gates the projection on optional term");
    }

    [Fact]
    public void Validate_RejectsAChildTableFannedOutFromANonArrayTerm()
    {
        var issues = SecurityTermsProjectionRegistry.Validate(
        [
            new(
                AssetClass: "DirectLoan",
                TableName: "direct_loan_probe_projection",
                Columns: [],
                ChildTables:
                [
                    new(
                        TableName: "direct_loan_probe_child_projection",
                        TermKey: "borrower",
                        Columns: [new("value", "value", SecurityAssetTermFieldType.String)])
                ])
        ]);

        issues.Should().ContainSingle().Which.Should().Contain("borrower").And.Contain("not Array");
    }

    [Fact]
    public void Validate_RejectsATableNameThatIsNotASafeIdentifier()
    {
        var issues = SecurityTermsProjectionRegistry.Validate(
        [
            new(
                AssetClass: "DirectLoan",
                TableName: "direct_loan\"; drop table securities; --",
                Columns: [],
                ChildTables: [])
        ]);

        issues.Should().ContainSingle().Which.Should().Contain("not a lower snake_case SQL identifier");
    }

    [Fact]
    public void UpsertSql_RestatesEveryNonKeyColumnFromExcluded()
    {
        var descriptor = Descriptor("StructuredCredit");

        var sql = PostgresSecurityMasterStore.BuildTermsProjectionUpsertSql(descriptor, "security_master");

        sql.Should().StartWith("insert into security_master.structured_credit_projection (security_id, display_name, currency, tranche");
        sql.Should().Contain("on conflict (security_id) do update set ");
        sql.Should().Contain("tranche = excluded.tranche");
        sql.Should().Contain("original_face = excluded.original_face");
        sql.Should().Contain("version = excluded.version");
        sql.Should().NotContain("security_id = excluded.security_id", "the conflict key is not restated");
        sql.Should().EndWith(";");
    }

    [Fact]
    public void ChildInsertSql_LeadsWithTheSecurityAndOrdinalKey()
    {
        var child = Descriptor("StructuredCredit").ChildTables.Single();

        var sql = PostgresSecurityMasterStore.BuildTermsProjectionChildInsertSql(child, "security_master");

        sql.Should().Be(
            "insert into security_master.structured_credit_factor_schedule_projection "
            + "(security_id, ordinal, as_of_date, factor) values (@security_id, @ordinal, @as_of_date, @factor);");
    }

    [Fact]
    public void TryBuild_DecodesTheCanonicalDirectLoanPayloadIntoColumnsAndChildRows()
    {
        var record = Record("DirectLoan", new
        {
            schemaVersion = 1,
            borrower = "Meridian Industrials LLC",
            maturity = "2030-03-31",
            referenceIndex = "SOFR",
            spreadBps = 425m,
            currentCouponRate = 9.55m,
            resetFrequency = "Quarterly",
            pricingSource = "IHSMarkit",
            covenants = new[]
            {
                new { covenantType = "MaxLeverage", threshold = "4.5x", notes = "Tested quarterly" }
            },
            principalSchedule = new[]
            {
                new { paymentDate = "2027-03-31", amount = 1_250_000m },
                new { paymentDate = "2028-03-31", amount = 1_250_000m }
            }
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("DirectLoan"), record, out var plan)
            .Should().BeTrue();

        plan.Value("borrower").Should().Be("Meridian Industrials LLC");
        plan.Value("maturity_date").Should().Be(new DateTime(2030, 3, 31));
        plan.Value("reference_index").Should().Be("SOFR");
        plan.Value("spread_bps").Should().Be(425m);
        plan.Value("current_coupon_rate").Should().Be(9.55m);
        plan.Value("version").Should().Be(7L);

        var covenants = plan.ChildRows("direct_loan_covenant_projection");
        covenants.Should().ContainSingle();
        Column(covenants[0], "ordinal").Should().Be(0);
        Column(covenants[0], "covenant_type").Should().Be("MaxLeverage");
        // The canonical threshold is written prose, not a number.
        Column(covenants[0], "threshold").Should().Be("4.5x");

        var schedule = plan.ChildRows("direct_loan_principal_schedule_projection");
        schedule.Should().HaveCount(2);
        Column(schedule[0], "ordinal").Should().Be(0);
        Column(schedule[0], "payment_date").Should().Be(new DateTime(2027, 3, 31));
        Column(schedule[0], "amount").Should().Be(1_250_000m);
        Column(schedule[1], "ordinal").Should().Be(1, "ordinal preserves the order the terms document declares");
    }

    [Fact]
    public void TryBuild_DecodesTheCanonicalStructuredCreditPayloadIntoColumnsAndChildRows()
    {
        var record = Record("StructuredCredit", new
        {
            schemaVersion = 1,
            tranche = "B",
            poolId = "MRDN-2026-1",
            collateralType = "CLO",
            originalFace = 10_000_000m,
            currentFactor = 0.8235m,
            couponOrIndex = "SOFR+250",
            factorSchedule = "See trustee report 2026-07",
            factorScheduleEntries = new[]
            {
                new { asOfDate = "2026-06-01", factor = 0.8412m },
                new { asOfDate = "2026-07-01", factor = 0.8235m }
            },
            maturity = "2031-06-15"
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("StructuredCredit"), record, out var plan)
            .Should().BeTrue();

        plan.Value("tranche").Should().Be("B");
        plan.Value("pool_id").Should().Be("MRDN-2026-1");
        plan.Value("collateral_type").Should().Be("CLO");
        plan.Value("original_face").Should().Be(10_000_000m);
        plan.Value("current_factor").Should().Be(0.8235m);
        plan.Value("coupon_or_index").Should().Be("SOFR+250");
        plan.Value("factor_schedule_reference").Should().Be("See trustee report 2026-07",
            "the free-text pointer is projected as prose, never as factor data");
        plan.Value("maturity_date").Should().Be(new DateTime(2031, 6, 15));

        var factors = plan.ChildRows("structured_credit_factor_schedule_projection");
        factors.Should().HaveCount(2);
        Column(factors[0], "as_of_date").Should().Be(new DateTime(2026, 6, 1));
        Column(factors[0], "factor").Should().Be(0.8412m);
        Column(factors[1], "factor").Should().Be(0.8235m);
    }

    [Fact]
    public void TryBuild_RefusesARecordOfAnotherAssetClass()
    {
        // Every writer runs for every record, so a class change has to clear the previous
        // projection rather than leave an orphan behind.
        var record = Record("Bond", new { schemaVersion = 1, borrower = "Not a loan" });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("DirectLoan"), record, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryBuild_RefusesAPayloadMissingAGatingTerm()
    {
        var record = Record("StructuredCredit", new
        {
            schemaVersion = 1,
            tranche = "B",
            collateralType = "CLO",
            couponOrIndex = "SOFR+250"
            // originalFace is absent: a NOT NULL column with no value means no projection row.
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("StructuredCredit"), record, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryBuild_RefusesTheWholeProjectionWhenAScheduleRowIsMalformed()
    {
        // A schedule that silently loses a row still reads as a complete schedule and would
        // misstate amortization; an absent projection reads as "not projected".
        var record = Record("StructuredCredit", new
        {
            schemaVersion = 1,
            tranche = "B",
            collateralType = "CLO",
            originalFace = 10_000_000m,
            couponOrIndex = "SOFR+250",
            factorScheduleEntries = new object[]
            {
                new { asOfDate = "2026-06-01", factor = 0.8412m },
                new { asOfDate = "2026-07-01" }
            }
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("StructuredCredit"), record, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryBuild_TreatsAnAbsentOrNullScheduleAsAnEmptyOne()
    {
        // The canonical serializer renders an empty F# list as [] and never omits the key, but a
        // legacy row can carry JSON null; neither is a malformed schedule.
        var record = Record("DirectLoan", new
        {
            schemaVersion = 1,
            borrower = "Bullet Term Loan Co",
            covenants = (object?)null,
            principalSchedule = Array.Empty<object>()
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("DirectLoan"), record, out var plan)
            .Should().BeTrue();

        plan.ChildRows("direct_loan_covenant_projection").Should().BeEmpty();
        plan.ChildRows("direct_loan_principal_schedule_projection").Should().BeEmpty();
        plan.Value("maturity_date").Should().Be(DBNull.Value, "an absent optional term projects null");
    }

    [Fact]
    public void TryBuild_TrimsProjectedStringsAndTreatsBlankAsAbsent()
    {
        var record = Record("DirectLoan", new
        {
            schemaVersion = 1,
            borrower = "  Meridian Industrials LLC  ",
            referenceIndex = "   ",
            covenants = Array.Empty<object>(),
            principalSchedule = Array.Empty<object>()
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("DirectLoan"), record, out var plan)
            .Should().BeTrue();

        plan.Value("borrower").Should().Be("Meridian Industrials LLC");
        plan.Value("reference_index").Should().Be(DBNull.Value);
    }

    [Fact]
    public void TryBuild_RefusesAPayloadWhoseGatingTermIsBlank()
    {
        var record = Record("DirectLoan", new
        {
            schemaVersion = 1,
            borrower = "   ",
            covenants = Array.Empty<object>(),
            principalSchedule = Array.Empty<object>()
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("DirectLoan"), record, out _)
            .Should().BeFalse();
    }

    private static SecurityTermsProjectionDescriptor Descriptor(string assetClass)
        => SecurityTermsProjectionRegistry.Descriptors.Single(descriptor =>
            string.Equals(descriptor.AssetClass, assetClass, StringComparison.Ordinal));

    private static object? Column(
        IReadOnlyList<PostgresSecurityMasterStore.SecurityTermsProjectionValue> row,
        string columnName)
        => row.Single(value => string.Equals(value.ColumnName, columnName, StringComparison.Ordinal)).Value;

    private static SecurityProjectionRecord Record(string assetClass, object assetSpecificTerms)
        => new(
            Guid.NewGuid(),
            assetClass,
            SecurityStatusDto.Active,
            "Projection fixture",
            "USD",
            "InternalCode",
            "FIXTURE-1",
            JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(assetSpecificTerms),
            JsonSerializer.SerializeToElement(new { }),
            7,
            DateTimeOffset.UtcNow,
            null,
            [],
            []);
}
