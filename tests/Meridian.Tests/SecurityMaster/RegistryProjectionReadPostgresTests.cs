using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The generic registry read against a real database: the descriptor selects have to agree with
/// the migrated DDL, SQL <c>date</c> columns read as ISO dates and numerics keep their exact text,
/// and the security's current asset class gates the read.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RegistryProjectionReadPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public RegistryProjectionReadPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task CommercialPaper_ReadsTheRegistryRow_AndIsGatedOnTheCurrentClass()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var service = new RegistryProjectionReadService(_fixture.Options, store);
        var securityId = Guid.NewGuid();

        await store.UpsertProjectionAsync(Record(securityId, "CommercialPaper", version: 1, new
        {
            schemaVersion = 1,
            issuerName = "Meridian Funding Corp",
            maturity = "2027-02-15",
            discountRate = 5.2500m,
            dayCount = "ACT/360",
            isAssetBacked = true
        }));

        var projection = await service.GetAsync("commercialpaper", securityId);

        projection.Should().NotBeNull();
        projection!.AssetClass.Should().Be("CommercialPaper");
        projection.TableName.Should().Be("commercial_paper_projection");
        projection.Columns.Keys.Should().Equal(
            SecurityTermsProjectionRegistry.ParentColumnNames(SecurityTermsProjectionRegistry.Require("CommercialPaper")));
        projection.Columns["security_id"].GetString().Should().Be(securityId.ToString("D"));
        projection.Columns["issuer_name"].GetString().Should().Be("Meridian Funding Corp");
        projection.Columns["maturity_date"].GetString().Should().Be("2027-02-15");
        projection.Columns["discount_rate"].GetDecimal().Should().Be(5.25m);
        projection.Columns["is_asset_backed"].GetBoolean().Should().BeTrue();
        projection.Columns["version"].GetInt64().Should().Be(1L);

        (await service.GetAsync("Repo", securityId)).Should().BeNull("the security is not a Repo");
        (await service.GetAsync("CommercialPaper", Guid.NewGuid())).Should().BeNull("no such security");
    }

    [SecurityMasterDatabaseFact]
    public async Task DirectLoan_ReadsTheParentAndBothChildTablesInOrdinalOrder()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var service = new RegistryProjectionReadService(_fixture.Options, store);
        var securityId = Guid.NewGuid();

        await store.UpsertProjectionAsync(Record(securityId, "DirectLoan", version: 1, new
        {
            schemaVersion = 1,
            borrower = "Meridian Industrials LLC",
            maturity = "2030-03-31",
            spreadBps = 425m,
            covenants = new[]
            {
                new { covenantType = "MaxLeverage", threshold = "4.5x", notes = "Tested quarterly" },
                new { covenantType = "MinFixedCharge", threshold = "1.10x", notes = (string?)null }
            },
            principalSchedule = new[]
            {
                new { paymentDate = "2027-03-31", amount = 1_250_000m },
                new { paymentDate = "2028-03-31", amount = 1_500_000m }
            }
        }));

        var projection = await service.GetAsync("DirectLoan", securityId);

        projection!.Columns["borrower"].GetString().Should().Be("Meridian Industrials LLC");
        projection.Columns["maturity_date"].GetString().Should().Be("2030-03-31");
        projection.Columns["reference_index"].ValueKind.Should().Be(JsonValueKind.Null);

        var covenants = projection.ChildTables.Single(table => table.TableName == "direct_loan_covenant_projection").Rows;
        covenants.Select(row => row["covenant_type"].GetString()).Should().Equal("MaxLeverage", "MinFixedCharge");
        covenants.Select(row => row["ordinal"].GetInt32()).Should().Equal(0, 1);
        covenants[1]["notes"].ValueKind.Should().Be(JsonValueKind.Null);

        var schedule = projection.ChildTables.Single(table => table.TableName == "direct_loan_principal_schedule_projection").Rows;
        schedule.Select(row => row["payment_date"].GetString()).Should().Equal("2027-03-31", "2028-03-31");
        schedule.Select(row => row["amount"].GetDecimal()).Should().Equal(1_250_000m, 1_500_000m);
    }

    [SecurityMasterDatabaseFact]
    public async Task DirectLoan_AnAmendCommittedBetweenTheParentAndChildReads_CannotTearTheComposite()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var securityId = Guid.NewGuid();
        await store.UpsertProjectionAsync(Record(securityId, "DirectLoan", version: 1, DirectLoanTerms(
            "Meridian Industrials LLC",
            [("MaxLeverage", "4.5x"), ("MinFixedCharge", "1.10x")],
            [("2027-03-31", 1_250_000m), ("2028-03-31", 1_500_000m)])));

        // The amend lands from another connection after the parent row has been read and before
        // any child table is: an old parent with new child rows is exactly the torn result.
        var rows = new AmendAfterParentRowSource(
            new PostgresRegistryProjectionRowSource(_fixture.Options),
            ct => store.UpsertProjectionAsync(Record(securityId, "DirectLoan", version: 2, DirectLoanTerms(
                "Meridian Industrials Holdings LLC",
                [("MaxLeverage", "5.0x")],
                [("2029-06-30", 9_000_000m)])), ct));
        var service = new RegistryProjectionReadService(_fixture.Options, store, rows);

        var during = await service.GetAsync("DirectLoan", securityId);

        rows.AmendsCommitted.Should().Be(1, "the amend committed while the composite read was under way");
        during!.Columns["version"].GetInt64().Should().Be(1L);
        during.Columns["borrower"].GetString().Should().Be("Meridian Industrials LLC");
        Covenants(during).Should().Equal("MaxLeverage:4.5x", "MinFixedCharge:1.10x");
        Schedule(during).Should().Equal("2027-03-31:1250000", "2028-03-31:1500000");

        var after = await new RegistryProjectionReadService(_fixture.Options, store).GetAsync("DirectLoan", securityId);

        after!.Columns["version"].GetInt64().Should().Be(2L);
        after.Columns["borrower"].GetString().Should().Be("Meridian Industrials Holdings LLC");
        Covenants(after).Should().Equal("MaxLeverage:5.0x");
        Schedule(after).Should().Equal("2029-06-30:9000000");
    }

    [SecurityMasterDatabaseFact]
    public async Task DirectLoanStores_ReadThroughOneSnapshot_SeeOneCommittedVersion()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var securityId = Guid.NewGuid();
        await store.UpsertProjectionAsync(Record(securityId, "DirectLoan", version: 1, DirectLoanTerms(
            "Snapshot Borrower LLC",
            [("MaxLeverage", "4.5x"), ("MinFixedCharge", "1.10x")],
            [("2027-03-31", 1_250_000m)])));

        var loans = PostgresDirectLoanReferenceProjectionStore.CreateLoanStore(_fixture.Options);
        var covenants = PostgresDirectLoanReferenceProjectionStore.CreateCovenantStore(_fixture.Options);
        var bySecurity = new Npgsql.NpgsqlParameter[] { new("security_id", securityId) };

        await using (var snapshot = await loans.BeginSnapshotAsync(CancellationToken.None))
        {
            var loan = await loans.GetBySecurityIdAsync(snapshot, securityId, CancellationToken.None);

            await store.UpsertProjectionAsync(Record(securityId, "DirectLoan", version: 2, DirectLoanTerms(
                "Snapshot Borrower Holdings LLC",
                [("MaxLeverage", "5.0x")],
                [("2029-06-30", 9_000_000m)])));

            var inSnapshot = await covenants.ListAsync(
                snapshot,
                PostgresDirectLoanReferenceProjectionStore.ChildrenBySecurity,
                bySecurity,
                CancellationToken.None);

            loan!.Version.Should().Be(1L);
            loan.Borrower.Should().Be("Snapshot Borrower LLC");
            inSnapshot.Select(row => row.CovenantType + ":" + row.Threshold).Should().Equal("MaxLeverage:4.5x", "MinFixedCharge:1.10x");
        }

        // Outside a snapshot each single-table read sees the latest commit, as before.
        (await loans.GetBySecurityIdAsync(securityId, CancellationToken.None))!.Version.Should().Be(2L);
        (await covenants.ListAsync(
                PostgresDirectLoanReferenceProjectionStore.ChildrenBySecurity,
                [new Npgsql.NpgsqlParameter("security_id", securityId)],
                CancellationToken.None))
            .Select(row => row.CovenantType + ":" + row.Threshold).Should().Equal("MaxLeverage:5.0x");
    }

    private static object DirectLoanTerms(
        string borrower,
        IReadOnlyList<(string Type, string Threshold)> covenants,
        IReadOnlyList<(string Date, decimal Amount)> principal)
        => new
        {
            schemaVersion = 1,
            borrower,
            maturity = "2030-03-31",
            spreadBps = 425m,
            covenants = covenants.Select(c => new { covenantType = c.Type, threshold = c.Threshold, notes = (string?)null }).ToArray(),
            principalSchedule = principal.Select(p => new { paymentDate = p.Date, amount = p.Amount }).ToArray()
        };

    private static IEnumerable<string> Covenants(SecurityTermsProjectionReadDto projection)
        => projection.ChildTables.Single(table => table.TableName == "direct_loan_covenant_projection").Rows
            .Select(row => $"{row["covenant_type"].GetString()}:{row["threshold"].GetString()}");

    private static IEnumerable<string> Schedule(SecurityTermsProjectionReadDto projection)
        => projection.ChildTables.Single(table => table.TableName == "direct_loan_principal_schedule_projection").Rows
            .Select(row => $"{row["payment_date"].GetString()}:{row["amount"].GetDecimal():0.##}");

    /// <summary>
    /// Delegates to the PostgreSQL row source and, once the first projection select (the parent
    /// row) has returned, commits <c>amend</c> from a separate connection before the read continues.
    /// </summary>
    private sealed class AmendAfterParentRowSource : IRegistryProjectionRowSource
    {
        private readonly IRegistryProjectionRowSource _inner;
        private readonly Func<CancellationToken, Task> _amend;

        public AmendAfterParentRowSource(IRegistryProjectionRowSource inner, Func<CancellationToken, Task> amend)
        {
            _inner = inner;
            _amend = amend;
        }

        public int AmendsCommitted { get; private set; }

        public async Task<IRegistryProjectionRowSnapshot> OpenSnapshotAsync(CancellationToken ct)
            => new Snapshot(this, await _inner.OpenSnapshotAsync(ct));

        private sealed class Snapshot : IRegistryProjectionRowSnapshot
        {
            private readonly AmendAfterParentRowSource _source;
            private readonly IRegistryProjectionRowSnapshot _inner;

            public Snapshot(AmendAfterParentRowSource source, IRegistryProjectionRowSnapshot inner)
            {
                _source = source;
                _inner = inner;
            }

            public Task<bool> IsCurrentAssetClassAsync(Guid securityId, string assetClass, CancellationToken ct)
                => _inner.IsCurrentAssetClassAsync(securityId, assetClass, ct);

            public async Task<IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>> ReadAsync(
                string sql,
                IReadOnlyList<string> columns,
                Guid securityId,
                CancellationToken ct)
            {
                var rows = await _inner.ReadAsync(sql, columns, securityId, ct);
                if (_source.AmendsCommitted == 0)
                {
                    await _source._amend(ct);
                    _source.AmendsCommitted++;
                }

                return rows;
            }

            public ValueTask DisposeAsync() => _inner.DisposeAsync();
        }
    }

    private static SecurityProjectionRecord Record(Guid securityId, string assetClass, long version, object assetSpecificTerms)
    {
        var identifierValue = $"REGREAD-{securityId:N}";
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        var identifier = new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, identifierValue, true, effectiveFrom);

        return new(
            securityId,
            assetClass,
            SecurityStatusDto.Active,
            $"Registry read fixture {securityId:N}",
            "USD",
            SecurityIdentifierKind.InternalCode.ToString(),
            identifierValue,
            JsonSerializer.SerializeToElement(new { displayName = $"Registry read fixture {securityId:N}", currency = "USD" }),
            SecurityTermsEnvelope.StampDeclaredFormat(JsonSerializer.SerializeToElement(assetSpecificTerms)),
            JsonSerializer.SerializeToElement(new { sourceSystem = "integration-test", updatedBy = "registry-read-fixture" }),
            version,
            effectiveFrom,
            null,
            [identifier],
            []);
    }
}
