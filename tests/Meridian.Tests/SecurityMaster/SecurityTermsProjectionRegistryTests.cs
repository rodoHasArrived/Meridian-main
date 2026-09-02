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
    public void TryBuild_PrefersARootTermOverTheProfileEnvelopeCopy()
    {
        // A first-class payload that also carries an envelope must read as first-class: the root is
        // the canonical form, the envelope is the fallback.
        var record = Record("StructuredCredit", new
        {
            schemaVersion = 1,
            tranche = "B",
            collateralType = "CLO",
            originalFace = 10_000_000m,
            couponOrIndex = "SOFR+250",
            profileFields = new { tranche = "SHOULD-NOT-WIN", collateralType = "RMBS" }
        });

        PostgresSecurityMasterStore.TryBuildTermsProjection(Descriptor("StructuredCredit"), record, out var plan)
            .Should().BeTrue();

        plan.Value("tranche").Should().Be("B");
        plan.Value("collateral_type").Should().Be("CLO");
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
            AssertTableDeclares(
                ddl,
                descriptor.TableName,
                SecurityTermsProjectionRegistry.LeadingIdentityColumns
                    .Concat(descriptor.Columns.Select(column => column.ColumnName))
                    .Concat(SecurityTermsProjectionRegistry.TrailingIdentityColumns));

            foreach (var child in descriptor.ChildTables)
            {
                AssertTableDeclares(
                    ddl,
                    child.TableName,
                    SecurityTermsProjectionRegistry.ChildKeyColumns
                        .Concat(child.Columns.Select(column => column.ColumnName)));
            }
        }
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
        var directory = Path.Combine(AppContext.BaseDirectory, "SecurityMaster", "Migrations");
        Directory.Exists(directory).Should().BeTrue($"migration scripts must ship to '{directory}'");

        var tables = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.GetFiles(directory, "*.sql"))
        {
            var sql = StripLineComments(File.ReadAllText(path));
            foreach (Match table in CreateTablePattern.Matches(sql))
            {
                tables[table.Groups["table"].Value] = ParseColumnNames(table.Groups["body"].Value);
            }
        }

        return tables;
    }

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
