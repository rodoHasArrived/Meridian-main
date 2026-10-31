using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
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
    public void Validate_RejectsTextNormalizationOnANonStringColumn()
    {
        var issues = SecurityTermsProjectionRegistry.Validate(
        [
            new(
                AssetClass: "DirectLoan",
                TableName: "direct_loan_probe_projection",
                Columns:
                [
                    SecurityTermsProjectionColumn.Optional(
                        "spread_bps", "spreadBps", SecurityAssetTermFieldType.Decimal, SecurityTermsProjectionTextNormalization.Verbatim)
                ],
                ChildTables: [])
        ]);

        issues.Should().ContainSingle().Which.Should().Contain("normalization applies only to String terms");
    }

    [Fact]
    public void Validate_RejectsADefaultOnANonBooleanColumn()
    {
        var issues = SecurityTermsProjectionRegistry.Validate(
        [
            new(
                AssetClass: "DirectLoan",
                TableName: "direct_loan_probe_projection",
                Columns: [new("spread_bps", "spreadBps", SecurityAssetTermFieldType.Decimal, DefaultWhenAbsent: 0m)],
                ChildTables: [])
        ]);

        issues.Should().ContainSingle().Which.Should().Contain("only a bool default on a Boolean column");
    }

    [Fact]
    public void Validate_RejectsAGateThatAlsoDeclaresADefault()
    {
        var issues = SecurityTermsProjectionRegistry.Validate(
        [
            new(
                AssetClass: "Deposit",
                TableName: "deposit_probe_projection",
                Columns: [new("is_callable", "isCallable", SecurityAssetTermFieldType.Boolean, Gates: true, DefaultWhenAbsent: false)],
                ChildTables: [])
        ]);

        issues.Should().ContainSingle().Which.Should().Contain("makes the gate unreachable");
    }

    [Fact]
    public void Validate_StillReservesTheCurrencyColumnForADescriptorThatDoesNotProjectIt()
    {
        var issues = SecurityTermsProjectionRegistry.Validate(
        [
            new(
                AssetClass: "CryptoCurrency",
                TableName: "crypto_probe_projection",
                Columns: [SecurityTermsProjectionColumn.Optional("currency", "network", SecurityAssetTermFieldType.String)],
                ChildTables: [],
                ProjectsCurrency: false)
        ]);

        issues.Should().ContainSingle().Which.Should().Contain("identity-spine column");
    }

    [Fact]
    public void UpsertSql_OmitsCurrencyForADescriptorThatDoesNotProjectIt()
    {
        var descriptor = Descriptor("CryptoCurrency");

        var sql = PostgresSecurityMasterStore.BuildTermsProjectionUpsertSql(descriptor, "security_master");

        sql.Should().StartWith("insert into security_master.crypto_projection (security_id, display_name, base_currency, quote_currency, network, primary_identifier_value, version)");
        sql.Should().NotContain("@currency");
        PostgresSecurityMasterStore.TryBuildTermsProjection(
                descriptor,
                Record("CryptoCurrency", new { baseCurrency = "btc", quoteCurrency = "usd" }),
                out var plan)
            .Should().BeTrue();
        plan.Columns.Select(column => column.ColumnName).Should().NotContain("currency");
    }

    [Fact]
    public void TryBuild_AppliesEachColumnsDeclaredTextNormalizationAndBooleanDefault()
    {
        var crypto = Build("CryptoCurrency", new { baseCurrency = " btc ", quoteCurrency = "usd", network = "  Bitcoin " });
        crypto.Value("base_currency").Should().Be("BTC");
        crypto.Value("quote_currency").Should().Be("USD");
        crypto.Value("network").Should().Be("  Bitcoin ", "network was always projected verbatim");

        var fund = Build("MoneyMarketFund", new { fundFamily = "   " });
        fund.Value("fund_family").Should().Be(string.Empty, "fund family is trimmed but never nulled");
        fund.Value("sweep_eligible").Should().Be(false);
        fund.Value("liquidity_fee_eligible").Should().Be(false);
        fund.Value("weighted_average_maturity_days").Should().Be(DBNull.Value);

        var flagged = Build("MoneyMarketFund", new { sweepEligible = true, liquidityFeeEligible = "yes" });
        flagged.Value("sweep_eligible").Should().Be(true);
        flagged.Value("liquidity_fee_eligible").Should().Be(false, "a wrong JSON kind falls back to the default");

        static PostgresSecurityMasterStore.SecurityTermsProjectionPlan Build(string assetClass, object terms)
        {
            PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor(assetClass), Record(assetClass, terms), out var plan)
                .Should().BeTrue();
            return plan;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryBuild_ABlankGateSuppressesTheProjectionWhateverItsNormalization(string blank)
    {
        // CryptoCurrency gates under TrimUpperInvariant; the gate must refuse blank text itself rather
        // than rely on the normalization having nulled it.
        PostgresSecurityMasterStore.TryBuildTermsProjection(
                Descriptor("CryptoCurrency"),
                Record("CryptoCurrency", new { baseCurrency = blank, quoteCurrency = "USD" }),
                out _)
            .Should().BeFalse();

        var verbatimGate = new SecurityTermsProjectionDescriptor(
            "Deposit",
            "deposit_projection",
            [
                SecurityTermsProjectionColumn.Gate(
                    "deposit_type", "depositType", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.Verbatim)
            ],
            []);

        PostgresSecurityMasterStore.TryBuildTermsProjection(
                verbatimGate,
                Record("Deposit", new { depositType = blank }),
                out _)
            .Should().BeFalse();
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
            maturity = "2031-06-15",
            structuredCreditKind = "Clo",
            seniority = "Mezzanine",
            attachmentPoint = 0.12m,
            detachmentPoint = 0.20m,
            weightedAverageCoupon = 7.85m,
            weightedAverageMaturityMonths = 58,
            weightedAverageLoanAgeMonths = 14,
            reinvestmentPeriodEnd = "2028-06-15",
            nonCallPeriodEnd = "2027-06-15"
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("StructuredCredit"), record, out var plan)
            .Should().BeTrue();

        // Typed tranche terms (version 2) project beside the version-1 columns.
        plan.Value("structured_credit_kind").Should().Be("Clo");
        plan.Value("seniority").Should().Be("Mezzanine");
        plan.Value("attachment_point").Should().Be(0.12m);
        plan.Value("detachment_point").Should().Be(0.20m);
        plan.Value("weighted_average_coupon").Should().Be(7.85m);
        plan.Value("weighted_average_maturity_months").Should().Be(58);
        plan.Value("weighted_average_loan_age_months").Should().Be(14);
        plan.Value("reinvestment_period_end").Should().Be(new DateTime(2028, 6, 15));
        plan.Value("non_call_period_end").Should().Be(new DateTime(2027, 6, 15));

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
    public void TryBuild_ProjectsACanonicalPayloadWhoseOptionalTermsAreExplicitNull()
    {
        // The canonical serializer emits EVERY declared key on every document and writes null where
        // the domain value is None, so this — not a key-omitting payload — is the shape production
        // persists. Reading it must project nulls, not throw: the shared GetOptional* readers reach
        // for TryGetDecimal/TryGetInt32, which throw on a non-number element, and a throw here would
        // abort the whole security's projection transaction.
        var record = Record("DirectLoan", new
        {
            schemaVersion = 1,
            borrower = "Sparse Borrower LLC",
            covenants = Array.Empty<object>(),
            currentCouponRate = (decimal?)null,
            maturity = (string?)null,
            pricingSource = (string?)null,
            principalSchedule = Array.Empty<object>(),
            referenceIndex = (string?)null,
            resetFrequency = (string?)null,
            spreadBps = (decimal?)null
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("DirectLoan"), record, out var plan)
            .Should().BeTrue();

        plan.Value("borrower").Should().Be("Sparse Borrower LLC");
        plan.Value("spread_bps").Should().Be(DBNull.Value);
        plan.Value("current_coupon_rate").Should().Be(DBNull.Value);
        plan.Value("maturity_date").Should().Be(DBNull.Value);
        plan.Value("reference_index").Should().Be(DBNull.Value);
    }

    [Fact]
    public void TryBuild_ReadsAProfileBackedStructuredCreditThroughItsProfileFields()
    {
        // A profile-backed record's asset terms ARE the profile envelope, so its economics sit one
        // level down. Reading only the root would fail every gate and unproject the tranche —
        // deleting any row it already had — while every other codec for the class reads it fine.
        var record = Record("StructuredCredit", new
        {
            schemaVersion = 3,
            customProfileId = "structured-credit-io-po",
            profileVersion = 1,
            profileFields = new
            {
                tranche = "A-1",
                poolId = "POOL-1",
                collateralType = "CLO",
                originalFace = 1_000_000m,
                currentFactor = 0.5m,
                couponOrIndex = "SOFR+250",
                factorSchedule = "trustee report",
                factorScheduleEntries = new[]
                {
                    new { asOfDate = "2026-01-01", factor = 0.8m }
                },
                maturity = "2032-01-01"
            }
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("StructuredCredit"), record, out var plan)
            .Should().BeTrue();

        plan.Value("tranche").Should().Be("A-1");
        plan.Value("pool_id").Should().Be("POOL-1");
        plan.Value("collateral_type").Should().Be("CLO");
        plan.Value("original_face").Should().Be(1_000_000m);
        plan.Value("coupon_or_index").Should().Be("SOFR+250");
        plan.Value("maturity_date").Should().Be(new DateTime(2032, 1, 1));

        var factors = plan.ChildRows("structured_credit_factor_schedule_projection");
        factors.Should().ContainSingle();
        Column(factors[0], "as_of_date").Should().Be(new DateTime(2026, 1, 1));
        Column(factors[0], "factor").Should().Be(0.8m);
    }

    [Fact]
    public void TryBuild_PrefersTheGovernedProfileTermOverAnOuterDuplicate()
    {
        // Governed profile fields outrank outer duplicates, as StructuredCashFlowTermsResolver
        // already does: profileFields values are validated on write, outer keys on an envelope are
        // ungoverned pass-through. A projection that preferred the root would disagree with every
        // other reader of the same record.
        var record = Record("StructuredCredit", new
        {
            schemaVersion = 3,
            customProfileId = "structured-credit-io-po",
            profileVersion = 1,
            tranche = "UNGOVERNED-OUTER",
            collateralType = "RMBS",
            profileFields = new
            {
                tranche = "B",
                collateralType = "CLO",
                originalFace = 10_000_000m,
                couponOrIndex = "SOFR+250"
            }
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("StructuredCredit"), record, out var plan)
            .Should().BeTrue();

        plan.Value("tranche").Should().Be("B");
        plan.Value("collateral_type").Should().Be("CLO");
    }

    [Fact]
    public void TryBuild_FallsBackToTheRootForATermTheProfileDoesNotDeclare()
    {
        // The seeded structured-credit profile declares no factorScheduleEntries, so a record can
        // carry the governed scalars in the envelope and the typed schedule at the root.
        var record = Record("StructuredCredit", new
        {
            schemaVersion = 3,
            customProfileId = "structured-credit-io-po",
            profileVersion = 1,
            factorScheduleEntries = new[] { new { asOfDate = "2026-01-01", factor = 0.8m } },
            profileFields = new
            {
                tranche = "B",
                collateralType = "CLO",
                originalFace = 10_000_000m,
                couponOrIndex = "SOFR+250"
            }
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("StructuredCredit"), record, out var plan)
            .Should().BeTrue();

        plan.Value("tranche").Should().Be("B");
        plan.ChildRows("structured_credit_factor_schedule_projection").Should().ContainSingle();
    }

    [Fact]
    public void TryBuild_DoesNotReachIntoProfileFieldsForAClassThatCannotCarryAProfile()
    {
        // DirectLoan is not profile-backed, so a stray profileFields object is opaque payload, not a
        // term source. Reading it would invent economics the class never declared.
        var record = Record("DirectLoan", new
        {
            schemaVersion = 1,
            borrower = "Root Borrower LLC",
            covenants = Array.Empty<object>(),
            principalSchedule = Array.Empty<object>(),
            profileFields = new { referenceIndex = "SHOULD-NOT-BE-READ" }
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("DirectLoan"), record, out var plan)
            .Should().BeTrue();

        plan.Value("reference_index").Should().Be(DBNull.Value);
    }

    [Fact]
    public void TryBuild_RefusesNonpositivePrincipalWithoutPublishingAPartialSchedule()
    {
        // Every retained row must be valid. Removing nonpositive instalments would turn rejected
        // source evidence into an apparently complete schedule in both cash-flow and SQL readers.
        var record = Record("DirectLoan", new
        {
            schemaVersion = 1,
            borrower = "Meridian Industrials LLC",
            covenants = Array.Empty<object>(),
            principalSchedule = new[]
            {
                new { paymentDate = "2027-03-31", amount = 1_250_000m },
                new { paymentDate = "2027-09-30", amount = 0m },
                new { paymentDate = "2028-03-31", amount = -500m },
                new { paymentDate = "2028-09-30", amount = 750_000m }
            }
        });

        Action build = () => PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("DirectLoan"), record, out _);
        var diagnostics = build.Should().Throw<SecurityScheduleValidationException>().Which.Diagnostics;
        diagnostics.Should().HaveCount(2).And.OnlyContain(diagnostic => diagnostic.Code == "SM_SCHEDULE_RANGE_INVALID");
        diagnostics.Select(diagnostic => diagnostic.Path).Should().Equal(
            "assetSpecificTerms.principalSchedule[1].amount", "assetSpecificTerms.principalSchedule[2].amount");
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
        // Invalid retained evidence is distinguished from an inapplicable or absent projection,
        // and cannot erase the prior valid rows through the ordinary no-projection delete path.
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

        Action build = () => PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("StructuredCredit"), record, out _);
        var diagnostic = build.Should().Throw<SecurityScheduleValidationException>().Which.Diagnostics.Should().ContainSingle().Which;
        diagnostic.Code.Should().Be("SM_SCHEDULE_VALUE_REQUIRED");
        diagnostic.Path.Should().Be("assetSpecificTerms.factorScheduleEntries[1].factor");
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

    [Fact]
    public void EveryRegisteredTableAndColumn_ExistsInTheMigrationDdl()
    {
        // The descriptor names the table and columns the writer emits SQL against; the migration
        // creates them. Nothing else binds the two, and a mismatch fails only once a statement
        // reaches a real database — which the pull-request gate never does, because it excludes
        // Category=Integration. This reads the shipped DDL so the binding is checked at unit speed.
        var ddl = ReadShippedMigrationDdl();

        foreach (var descriptor in SecurityTermsProjectionRegistry.Descriptors)
        {
            AssertTableDeclares(ddl, descriptor.TableName, SecurityTermsProjectionRegistry.ParentColumnNames(descriptor));

            foreach (var child in descriptor.ChildTables)
            {
                AssertTableDeclares(ddl, child.TableName, SecurityTermsProjectionRegistry.ChildColumnNames(child));
            }

            foreach (var satellite in descriptor.Satellites)
            {
                AssertTableDeclares(ddl, satellite.TableName, SecurityTermsProjectionRegistry.SatelliteColumnNames(satellite));
            }
        }
    }

    [Fact]
    public void EveryTableWithoutASpineColumn_DeclaresThatInTheRegistry_NotTheOtherWayRound()
    {
        // A table that opts out of display_name, currency or the primary identifier must really lack
        // the column: the upsert would otherwise leave a NOT NULL column unbound. Bond's parent and
        // two of its satellites predate the spine and are the only such tables today.
        var ddl = ReadShippedMigrationDdl();

        foreach (var descriptor in SecurityTermsProjectionRegistry.Descriptors)
        {
            AssertSpineMatchesDdl(ddl, descriptor.TableName, descriptor.ProjectsDisplayName, descriptor.ProjectsCurrency, descriptor.ProjectsPrimaryIdentifier);
            foreach (var satellite in descriptor.Satellites)
            {
                AssertSpineMatchesDdl(ddl, satellite.TableName, satellite.ProjectsDisplayName, satellite.ProjectsCurrency, satellite.ProjectsPrimaryIdentifier);
            }
        }
    }

    private static void AssertSpineMatchesDdl(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> ddl,
        string tableName,
        bool projectsDisplayName,
        bool projectsCurrency,
        bool projectsPrimaryIdentifier)
    {
        var columns = ddl[tableName];
        columns.Contains("display_name").Should().Be(projectsDisplayName, $"'{tableName}' declares ProjectsDisplayName={projectsDisplayName}");
        columns.Contains("currency").Should().Be(projectsCurrency, $"'{tableName}' declares ProjectsCurrency={projectsCurrency}");
        columns.Contains("primary_identifier_value").Should().Be(projectsPrimaryIdentifier, $"'{tableName}' declares ProjectsPrimaryIdentifier={projectsPrimaryIdentifier}");
        columns.Should().Contain("version", $"every registry table carries version");
    }

    [Fact]
    public void EveryChildTableClaimingACascade_DeclaresOneInTheMigrationDdl()
    {
        // The writer clears a projection with a single parent delete wherever a child declares the
        // cascade. If the DDL ever loses it, that shortcut silently leaves orphan child rows behind
        // for every record that stops qualifying — so the claim is checked against the shipped SQL.
        var sql = ReadShippedMigrationSql();

        foreach (var descriptor in SecurityTermsProjectionRegistry.Descriptors)
        {
            foreach (var child in descriptor.ChildTables.Where(static child => child.CascadesFromParent))
            {
                CascadingForeignKey(child.TableName, descriptor.TableName)
                    .IsMatch(sql)
                    .Should().BeTrue(
                        $"'{child.TableName}' claims it cascades from '{descriptor.TableName}', so its "
                        + "security_id foreign key must declare 'on delete cascade'");
            }

            foreach (var satellite in descriptor.Satellites.Where(static satellite => satellite.CascadesFromParent))
            {
                CascadingForeignKey(satellite.TableName, descriptor.TableName)
                    .IsMatch(sql)
                    .Should().BeTrue(
                        $"'{satellite.TableName}' claims it cascades from '{descriptor.TableName}', so its "
                        + "security_id foreign key must declare 'on delete cascade'");
            }
        }
    }

    [Fact]
    public void Validate_RejectsAGatingColumnOnASatelliteTable()
    {
        var issues = SecurityTermsProjectionRegistry.Validate(
        [
            new(
                AssetClass: "Deposit",
                TableName: "deposit_probe_projection",
                Columns: [SecurityTermsProjectionColumn.Optional("institution_name", "institutionName", SecurityAssetTermFieldType.String)],
                ChildTables: [],
                SatelliteTables:
                [
                    new(
                        TableName: "deposit_probe_lifecycle_projection",
                        // depositType is Required, so the only objection is the gate's placement.
                        Columns: [SecurityTermsProjectionColumn.Gate("deposit_type", "depositType", SecurityAssetTermFieldType.String)])
                ])
        ]);

        issues.Should().ContainSingle().Which.Should().Contain("satellite").And.Contain("Condition");
    }

    [Fact]
    public void Validate_HoldsSatelliteColumnsToTheTermsSchemaLikeParentColumns()
    {
        var issues = SecurityTermsProjectionRegistry.Validate(
        [
            new(
                AssetClass: "Deposit",
                TableName: "deposit_probe_projection",
                Columns: [SecurityTermsProjectionColumn.Optional("institution_name", "institutionName", SecurityAssetTermFieldType.String)],
                ChildTables: [],
                SatelliteTables:
                [
                    new(
                        TableName: "deposit_probe_lifecycle_projection",
                        Columns: [SecurityTermsProjectionColumn.Optional("maturity_date", "maturityDate", SecurityAssetTermFieldType.Date)])
                ])
        ]);

        issues.Should().ContainSingle().Which.Should().Contain("deposit_probe_lifecycle_projection.maturity_date").And.Contain("does not declare");
    }

    [Fact]
    public void SatelliteUpsertSql_UsesTheSpineTheSatelliteDeclares()
    {
        var bond = Descriptor("Bond");
        var issuer = bond.Satellites.Single(static satellite => satellite.TableName == "bond_issuer_projection");
        var lifecycle = bond.Satellites.Single(static satellite => satellite.TableName == "bond_lifecycle_projection");

        PostgresSecurityMasterStore.BuildTermsProjectionUpsertSql(bond, "security_master")
            .Should().StartWith("insert into security_master.bond_projection (security_id, issuer_name, ")
            .And.Contain(", floating_spread_bps, version) values (")
            .And.NotContain("display_name");
        PostgresSecurityMasterStore.BuildTermsProjectionSatelliteUpsertSql(issuer, "security_master")
            .Should().StartWith("insert into security_master.bond_issuer_projection (security_id, display_name, currency, issuer_name, maturity_date, seniority, primary_identifier_value, version)")
            .And.Contain("on conflict (security_id) do update set display_name = excluded.display_name");
        PostgresSecurityMasterStore.BuildTermsProjectionSatelliteUpsertSql(lifecycle, "security_master")
            .Should().StartWith("insert into security_master.bond_lifecycle_projection (security_id, lifecycle_stat, ")
            .And.NotContain("display_name")
            .And.NotContain("primary_identifier_value");
    }

    [Fact]
    public void TryBuild_DecodesACanonicalBondIntoItsParentAndThreeSatellites()
    {
        var record = Record("Bond", new
        {
            schemaVersion = 1,
            // Far enough out that the lifecycle state does not flip to Matured as the real date advances.
            maturity = "2091-06-15",
            issueDate = "2021-06-15",
            couponType = "Fixed",
            couponRate = 5.25m,
            dayCount = "30/360",
            isCallable = true,
            callDate = "2026-06-15",
            subclass = "Corporate",
            seniority = "Senior",
            issuerName = "Acme Corp",
            par = 1000m,
            paymentFrequency = "SemiAnnual"
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("Bond"), record, out var plan).Should().BeTrue();

        plan.Columns.Select(static column => column.ColumnName).Should().Equal(
            "security_id", "issuer_name", "seniority", "subclass", "issue_date", "maturity_date",
            "coupon_kind", "fixed_coupon_rate", "floating_rate_index", "floating_spread_bps", "version");
        plan.Value("issuer_name").Should().Be("Acme Corp");
        plan.SatelliteValue("bond_lifecycle_projection", "lifecycle_stat").Should().Be("Callable",
            "the call date has passed and the bond is callable");
        plan.SatelliteValue("bond_lifecycle_projection", "par").Should().Be(1000m);
        plan.SatelliteValue("bond_lifecycle_projection", "is_callable").Should().Be(true);
        plan.SatelliteValue("bond_accrual_convention_projection", "settlement_cycle_days").Should().Be(DBNull.Value,
            "the record's common terms carry no settlement cycle");
        plan.SatelliteColumns("bond_issuer_projection")!.Select(static column => column.ColumnName).Should().Equal(
            "security_id", "display_name", "currency", "issuer_name", "maturity_date", "seniority", "primary_identifier_value", "version");
        plan.SatelliteValue("bond_issuer_projection", "display_name").Should().Be(record.DisplayName);
    }

    private static void AssertTableDeclares(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> ddl,
        string tableName,
        IEnumerable<string> columnNames)
    {
        ddl.Should().ContainKey(tableName,
            $"the registry writes into '{tableName}', so a migration must create it");

        foreach (var columnName in columnNames)
        {
            ddl[tableName].Should().Contain(columnName,
                $"the registry writes '{tableName}.{columnName}', so the migration must declare it");
        }
    }

    /// <summary>
    /// The column names each Security Master migration declares per created table, read from the
    /// scripts as they ship in the build output — the same files the migration runner applies.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyCollection<string>> ReadShippedMigrationDdl()
    {
        var tables = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

        var sql = ReadShippedMigrationSql();
        foreach (Match table in CreateTablePattern.Matches(sql))
        {
            tables[table.Groups["table"].Value] = ParseColumnNames(table.Groups["body"].Value);
        }

        // A later migration may widen a projection table (045 added the typed structured-credit
        // tranche columns), so columns an `alter table … add column` declares count as well.
        foreach (Match alter in AlterTableAddColumnPattern.Matches(sql))
        {
            var tableName = alter.Groups["table"].Value;
            var columns = new HashSet<string>(
                tables.TryGetValue(tableName, out var existing) ? existing : [],
                StringComparer.OrdinalIgnoreCase);
            foreach (Capture column in alter.Groups["column"].Captures)
            {
                columns.Add(column.Value);
            }

            tables[tableName] = columns;
        }

        return tables;
    }

    /// <summary>Every shipped Security Master migration script, comments stripped, concatenated.</summary>
    private static string ReadShippedMigrationSql()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "SecurityMaster", "Migrations");
        Directory.Exists(directory).Should().BeTrue($"migration scripts must ship to '{directory}'");

        return string.Join(
            '\n',
            Directory.GetFiles(directory, "*.sql").Select(path => StripLineComments(File.ReadAllText(path))));
    }

    /// <summary>Matches a child table's cascading <c>security_id</c> foreign key to its parent.</summary>
    private static Regex CascadingForeignKey(string childTable, string parentTable)
        => new(
            $@"create\s+table[^;]*?__SCHEMA__\.{Regex.Escape(childTable)}\s*\([^;]*?security_id[^,]*?references\s+__SCHEMA__\.{Regex.Escape(parentTable)}\s*\(\s*security_id\s*\)\s+on\s+delete\s+cascade",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>Drops <c>--</c> comments so their prose cannot be mistaken for a column definition.</summary>
    private static string StripLineComments(string sql)
        => string.Join(
            '\n',
            sql.Split('\n').Select(static line =>
            {
                var comment = line.IndexOf("--", StringComparison.Ordinal);
                return comment < 0 ? line : line[..comment];
            }));

    /// <summary>
    /// The leading identifier of each top-level definition in a <c>create table</c> body. Splitting
    /// is depth-aware so a type's own comma — <c>numeric(28, 10)</c> — does not read as a column
    /// boundary, and definitions that start with a keyword (<c>primary key</c>, <c>constraint</c>)
    /// are dropped rather than recorded as columns.
    /// </summary>
    private static IReadOnlyCollection<string> ParseColumnNames(string body)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new StringBuilder();
        var depth = 0;

        void Flush()
        {
            var definition = current.ToString().Trim();
            current.Clear();

            var name = definition
                .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (name is not null && !TableConstraintKeywords.Contains(name))
            {
                columns.Add(name);
            }
        }

        foreach (var character in body)
        {
            switch (character)
            {
                case '(':
                    depth++;
                    current.Append(character);
                    break;
                case ')':
                    depth--;
                    current.Append(character);
                    break;
                case ',' when depth == 0:
                    Flush();
                    break;
                default:
                    current.Append(character);
                    break;
            }
        }

        Flush();
        return columns;
    }

    private static readonly HashSet<string> TableConstraintKeywords =
        new(StringComparer.OrdinalIgnoreCase) { "primary", "unique", "constraint", "foreign", "check", "exclude" };

    private static readonly Regex CreateTablePattern = new(
        @"create\s+table\s+(?:if\s+not\s+exists\s+)?__SCHEMA__\.(?<table>\w+)\s*\((?<body>.*?)\)\s*;",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>
    /// <c>alter table __SCHEMA__.t add column [if not exists] c …[, add column …];</c> — every
    /// <c>column</c> capture is one added column name. A type's own parenthesised arguments
    /// (<c>numeric(18, 10)</c>) are skipped as a unit so their comma does not end the clause.
    /// </summary>
    private static readonly Regex AlterTableAddColumnPattern = new(
        @"alter\s+table\s+(?:if\s+exists\s+)?__SCHEMA__\.(?<table>\w+)\s+(?:add\s+column\s+(?:if\s+not\s+exists\s+)?(?<column>\w+)(?:\([^)]*\)|[^,;(])*,?\s*)+;",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static SecurityTermsProjectionDescriptor Descriptor(string assetClass)
        => SecurityTermsProjectionRegistry.Descriptors.Single(descriptor =>
            string.Equals(descriptor.AssetClass, assetClass, StringComparison.Ordinal));

    /// <summary>The classes migration 044 projected for the first time (every catalog class but CustomAsset is now projected).</summary>
    private static readonly string[] Migration044Classes =
    [
        "CommercialPaper", "TreasuryBill", "Repo", "CashSweep", "OtherSecurity", "Cfd", "Warrant",
        "InvestmentFund", "PrivateFundInterest", "PrivateCompanyEquity", "RealEstateHolding", "CommitmentGuarantee"
    ];

    /// <summary>The OTC derivative classes migration 046 projects from the day they are modeled.</summary>
    private static readonly string[] Migration046Classes = ["FxForward", "NonDeliverableForward", "CreditDefaultSwap"];

    /// <summary>The rate options and linked notes migration 047 projects from the day they are modeled.</summary>
    private static readonly string[] Migration047Classes = ["Swaption", "CapFloor", "StructuredNote"];

    [Fact]
    public void Registry_ProjectsEveryCatalogClassExceptCustomAsset()
    {
        var expected = SecurityAssetClassCatalog.AssetClasses
            .Except(PostgresSecurityMasterStore.SpecializedProjectionAssetClasses, StringComparer.Ordinal)
            .Except(new[] { "CustomAsset" }, StringComparer.Ordinal);

        SecurityTermsProjectionRegistry.AssetClasses.Should().BeEquivalentTo(expected,
            "CustomAsset is the one deliberate gap: its fields are governed per profile and have no fixed column set");
        Migration044Classes.Should().BeSubsetOf(SecurityTermsProjectionRegistry.AssetClasses);
        Migration046Classes.Should().BeSubsetOf(SecurityTermsProjectionRegistry.AssetClasses);
        Migration047Classes.Should().BeSubsetOf(SecurityTermsProjectionRegistry.AssetClasses);
    }

    [Fact]
    public void EveryMigration044Class_ProjectsItsCanonicalFullPayload()
    {
        // The descriptors gate on the terms the schema marks Required, so the canonical full payload
        // of each class — the one the domain serializer writes — must satisfy every gate. A gate that
        // the canonical document could not satisfy would unproject every valid record of the class.
        foreach (var assetClass in Migration044Classes.Concat(Migration046Classes).Concat(Migration047Classes))
        {
            var descriptor = SecurityTermsProjectionRegistry.Descriptors.Single(candidate => candidate.AssetClass == assetClass);
            var record = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(
                assetClass,
                SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
                    assetClass,
                    SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]));

            PostgresSecurityMasterStore.TryBuildTermsProjection(descriptor, record, out var plan)
                .Should().BeTrue($"the canonical {assetClass} document must satisfy every gate of its own projection");

            foreach (var column in descriptor.Columns.Where(static column => column.Gates))
            {
                plan.Value(column.ColumnName).Should().NotBe(DBNull.Value, $"{assetClass}.{column.ColumnName} gates the projection");
            }
        }
    }

    [Fact]
    public void TryBuild_ProjectsAGuidTermAsAUuidValue_AndGatesOnAMalformedOne()
    {
        var warrant = SecurityTermsProjectionRegistry.Descriptors.Single(static descriptor => descriptor.AssetClass == "Warrant");
        var underlyingId = Guid.NewGuid();

        PostgresSecurityMasterStore.TryBuildTermsProjection(
                warrant,
                Record("Warrant", new { underlyingId, warrantType = "Call", strike = 12.5m }),
                out var plan)
            .Should().BeTrue();
        plan.Value("underlying_id").Should().Be(underlyingId, "a Guid term binds as a uuid, not as its text");
        plan.Value("expiry_date").Should().Be(DBNull.Value);

        PostgresSecurityMasterStore.TryBuildTermsProjection(
                warrant,
                Record("Warrant", new { underlyingId = "not-a-guid", warrantType = "Call" }),
                out _)
            .Should().BeFalse("a malformed Guid on a gating column suppresses the projection rather than writing a partial row");
    }

    [Fact]
    public void TryBuild_FansOutCommitmentGuaranteeCovenants_AndTreatsAnAbsentListAsEmpty()
    {
        var guarantee = SecurityTermsProjectionRegistry.Descriptors.Single(static descriptor => descriptor.AssetClass == "CommitmentGuarantee");

        PostgresSecurityMasterStore.TryBuildTermsProjection(
                guarantee,
                Record("CommitmentGuarantee", new
                {
                    counterparty = "Meridian Credit Bank",
                    committedAmount = 12_000_000m,
                    effectiveDate = "2026-01-01",
                    covenants = new object[]
                    {
                        new { covenantType = "MinLiquidity", threshold = "$5m", notes = (string?)null },
                        new { covenantType = "MaxLeverage", threshold = "4.5x", notes = "tested quarterly" }
                    }
                }),
                out var plan)
            .Should().BeTrue();

        var rows = plan.ChildRows("commitment_guarantee_covenant_projection");
        rows.Should().HaveCount(2);
        rows.Select(row => Column(row, "ordinal")).Should().Equal(0, 1);
        Column(rows[1], "threshold").Should().Be("4.5x", "the covenant threshold stays prose");
        Column(rows[0], "notes").Should().Be(DBNull.Value);

        PostgresSecurityMasterStore.TryBuildTermsProjection(
                guarantee,
                Record("CommitmentGuarantee", new { counterparty = "Bank", committedAmount = 1m, effectiveDate = "2026-01-01" }),
                out var bare)
            .Should().BeTrue();
        bare.ChildRows("commitment_guarantee_covenant_projection").Should().BeEmpty();
    }

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
