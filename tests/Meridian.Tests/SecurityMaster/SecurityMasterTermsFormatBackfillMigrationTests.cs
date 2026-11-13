using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Rebuild;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.Migrations;
using Meridian.Storage.SecurityMaster;
using Meridian.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Migration 068 (ADR-024 amendment 2026-11-13, step 3) applied to a populated through-067 schema.
/// The backfill is the migration's own statement, run by the runner: it declares the column's family
/// on every untagged flat and profile row and changes nothing else, refuses before writing when an
/// untagged row classifies as economic (or cannot be stamped as readers infer it), rolls back whole
/// on failure, is stable across a retry and a restart, and leaves a constraint that refuses an
/// untagged row while a rebuild still persists.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityMasterTermsFormatBackfillMigrationTests
{
    private const string BackfillMigration = "068_security_master_terms_format_backfill.sql";

    private static readonly Guid UntaggedFlat = Guid.Parse("68000000-0000-4000-8000-000000000001");
    private static readonly Guid UnstampedFlat = Guid.Parse("68000000-0000-4000-8000-000000000002");
    private static readonly Guid UntaggedProfile = Guid.Parse("68000000-0000-4000-8000-000000000003");
    private static readonly Guid NullTagFlat = Guid.Parse("68000000-0000-4000-8000-000000000004");
    private static readonly Guid TaggedFlat = Guid.Parse("68000000-0000-4000-8000-000000000005");
    private static readonly Guid TaggedEconomic = Guid.Parse("68000000-0000-4000-8000-000000000006");
    private static readonly Guid UntaggedReservedTwo = Guid.Parse("68000000-0000-4000-8000-000000000007");

    [SecurityMasterDatabaseFact]
    public async Task PopulatedUpgrade_DeclaresTheColumnFamily_AndOnlyThat_AcrossFailureRetryAndRestart()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        var before = await fixture.RowsAsync();
        var receipts = await fixture.ReceiptsAsync();
        (await fixture.UntaggedCountAsync()).Should().Be(5);

        // Fail after the backfill and the constraint exist: the whole batch rolls back.
        await fixture.WriteMigrationAsync(fixture.MigrationSql + "\ndo $$ begin raise exception 'injected terms format backfill failure'; end $$;\n");
        var migrate = () => fixture.Runner().EnsureMigratedAsync();
        await migrate.Should().ThrowAsync<PostgresException>().WithMessage("*injected terms format backfill failure*");
        (await fixture.RowsAsync()).Should().Be(before);
        (await fixture.ReceiptsAsync()).Should().Be(receipts);
        (await fixture.ConstraintExistsAsync()).Should().BeFalse();

        await fixture.WriteMigrationAsync(fixture.MigrationSql);
        await fixture.Runner().EnsureMigratedAsync();

        (await fixture.ScalarAsync($"select checksum from {fixture.Schema}.schema_migrations where filename = '{BackfillMigration}'"))
            .Should().Be(Sha256Digest.ComputeUtf8(fixture.MigrationSql));
        (await fixture.UntaggedCountAsync()).Should().Be(0);
        (await fixture.ConstraintExistsAsync()).Should().BeTrue();
        (await fixture.ScalarAsync($"select convalidated from pg_constraint where conname = 'ck_securities_terms_format_declared' and conrelid = '{fixture.Schema}.securities'::regclass"))
            .Should().Be("True");

        // Row for row: the declared family equals the column, the document minus termsFormat is
        // unchanged, and no other column moved. No assetClassTermsVersion is added.
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.securities where asset_specific_terms->>'termsFormat' = terms_format")).Should().Be("7");
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.securities where asset_specific_terms ? 'assetClassTermsVersion'")).Should().Be("1",
            "only the already-tagged flat row carried the version key");
        (await fixture.TermsAsync(UntaggedFlat)).Should().Be("""{"shareClass": "Common", "termsFormat": "flat", "schemaVersion": 1}""");
        (await fixture.TermsAsync(UnstampedFlat)).Should().Be("""{"shareClass": "Preferred", "termsFormat": "flat"}""");
        (await fixture.TermsAsync(UntaggedProfile)).Should().Be(
            """{"termsFormat": "customProfile", "profileFields": {"vehicleName": "SPV I"}, "profileVersion": 2, "customProfileId": "co-invest-spv"}""");
        (await fixture.TermsAsync(NullTagFlat)).Should().Be("""{"shareClass": "Common", "termsFormat": "flat", "schemaVersion": 1}""",
            "a non-string termsFormat declares nothing and is replaced");
        (await fixture.TermsAsync(UntaggedReservedTwo)).Should().Be("""{"shareClass": "Common", "termsFormat": "flat", "schemaVersion": 2}""",
            "an untagged version-2 document without economic modules already reads as flat");
        (await fixture.ScalarAsync($"""
            select count(*)
            from {fixture.Schema}.securities s
            join jsonb_to_recordset('{before.Replace("'", "''", StringComparison.Ordinal)}'::jsonb)
                 as b(security_id uuid, asset_specific_terms jsonb, version bigint, schema_version integer, terms_format text, common_terms jsonb)
              on b.security_id = s.security_id
            where (s.asset_specific_terms - 'termsFormat') = (b.asset_specific_terms - 'termsFormat')
              and s.version = b.version and s.schema_version = b.schema_version
              and s.terms_format = b.terms_format and s.common_terms = b.common_terms
            """)).Should().Be("7");
        var tagged = await fixture.RowsAsync();
        tagged.Should().Contain("\"economic\"");

        // Every stamped row reads as the family readers already inferred from its shape.
        foreach (var (securityId, family) in new[]
                 {
                     (UntaggedFlat, SecurityTermsFormats.Flat), (UnstampedFlat, SecurityTermsFormats.Flat),
                     (UntaggedProfile, SecurityTermsFormats.CustomProfile), (NullTagFlat, SecurityTermsFormats.Flat),
                     (UntaggedReservedTwo, SecurityTermsFormats.Flat)
                 })
        {
            using var document = JsonDocument.Parse(await fixture.TermsAsync(securityId));
            SecurityTermsEnvelope.GetDeclaredFormat(document.RootElement).Should().Be(family);
        }

        // A restart finds nothing pending and changes nothing.
        var upgradedReceipts = await fixture.ReceiptsAsync();
        await fixture.Runner(legacyWritersRetired: false).EnsureMigratedAsync();
        (await fixture.ReceiptsAsync()).Should().Be(upgradedReceipts);
        (await fixture.RowsAsync()).Should().Be(tagged);
    }

    [SecurityMasterDatabaseFact]
    public async Task Rerun_OfTheBackfillStatements_IsIdempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        await fixture.WriteMigrationAsync(fixture.MigrationSql);
        await fixture.Runner().EnsureMigratedAsync();
        var tagged = await fixture.RowsAsync();

        // Executing the shipped script a second time (the duplicate_object guard, the no-op stamp,
        // the verification and a repeated VALIDATE) changes nothing and does not fail.
        await fixture.ScalarAsync(fixture.MigrationSql.Replace("__SCHEMA__", fixture.Schema, StringComparison.Ordinal));
        (await fixture.RowsAsync()).Should().Be(tagged);
        (await fixture.ScalarAsync($"select count(*) from pg_constraint where conname = 'ck_securities_terms_format_declared' and conrelid = '{fixture.Schema}.securities'::regclass"))
            .Should().Be("1");
    }

    [SecurityMasterDatabaseTheory]
    [InlineData("""{"schemaVersion":1,"shareClass":"Common"}""", "flat")]
    [InlineData("""{"schemaVersion":1,"termsFormat":null,"shareClass":"Common"}""", "flat")]
    [InlineData("""{"schemaVersion":1,"termsFormat":"customProfile","shareClass":"Common"}""", "flat")]
    [InlineData("""[1,2]""", "flat")]
    public async Task Constraint_RefusesAnUntaggedOrDisagreeingRow_AfterTheUpgrade(string terms, string column)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        await fixture.WriteMigrationAsync(fixture.MigrationSql);
        await fixture.Runner().EnsureMigratedAsync();

        var insert = () => fixture.ScalarAsync($"""
            insert into {fixture.Schema}.securities (security_id, asset_class, status, display_name, currency,
                primary_identifier_kind, primary_identifier_value, normalized_primary_identifier_value,
                common_terms, asset_specific_terms, provenance, version, effective_from, terms_format)
            values ('{Guid.NewGuid()}', 'Equity', 'Active', 'Refused', 'USD', 'Ticker', 'REFUSED', 'REFUSED',
                '{"{}"}', '{terms}', '{"{}"}', 1, '2026-01-01T00:00:00Z', '{column}')
            """);
        await insert.Should().ThrowAsync<PostgresException>().WithMessage("*ck_securities_terms_format_declared*");

        var strip = () => fixture.ScalarAsync(
            $"update {fixture.Schema}.securities set asset_specific_terms = asset_specific_terms - 'termsFormat' where security_id = '{UntaggedFlat}'");
        await strip.Should().ThrowAsync<PostgresException>().WithMessage("*ck_securities_terms_format_declared*");
    }

    [SecurityMasterDatabaseTheory]
    [InlineData("""{"schemaVersion":2,"maturity":{"maturityDate":"2030-01-01"},"coupon":null}""", "economic", "*classify as the economic terms family*")]
    [InlineData("""{"schemaVersion":2,"maturity":{"maturityDate":"2030-01-01"}}""", "flat", "*classify as the economic terms family*")]
    [InlineData("""{"customProfileId":"co-invest-spv","profileFields":{}}""", "flat", "*differs from the family their payload shape resolves to*")]
    [InlineData("""{"schemaVersion":1,"shareClass":"Common"}""", "customProfile", "*differs from the family their payload shape resolves to*")]
    [InlineData("""[1,2]""", "flat", "*not a JSON object*")]
    [InlineData("""null""", "flat", "*not a JSON object*")]
    public async Task Upgrade_RefusesARowItCannotStampAsReadersInferIt_BeforeWritingAnything(string terms, string column, string reason)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        await fixture.ScalarAsync($"""
            insert into {fixture.Schema}.securities (security_id, asset_class, status, display_name, currency,
                primary_identifier_kind, primary_identifier_value, normalized_primary_identifier_value,
                common_terms, asset_specific_terms, provenance, version, effective_from, terms_format)
            values ('68000000-0000-4000-8000-0000000000ff', 'Bond', 'Active', 'Refusal', 'USD', 'Ticker', 'REFUSE', 'REFUSE',
                '{"{}"}', '{terms}', '{"{}"}', 1, '2026-01-01T00:00:00Z', '{column}')
            """);
        await fixture.WriteMigrationAsync(fixture.MigrationSql);
        var before = await fixture.RowsAsync();
        var receipts = await fixture.ReceiptsAsync();

        var migrate = () => fixture.Runner().EnsureMigratedAsync();
        (await migrate.Should().ThrowAsync<PostgresException>().WithMessage(reason))
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        (await fixture.RowsAsync()).Should().Be(before, "the refusal runs before the backfill writes anything");
        (await fixture.ReceiptsAsync()).Should().Be(receipts);
        (await fixture.ConstraintExistsAsync()).Should().BeFalse();
    }

    [SecurityMasterDatabaseFact]
    public async Task Upgrade_RefusesMissingLegacyWriterAttestation_WithoutPublishingMigration()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        await fixture.WriteMigrationAsync(fixture.MigrationSql);
        var before = await fixture.RowsAsync();
        var receipts = await fixture.ReceiptsAsync();

        var migrate = () => fixture.Runner(legacyWritersRetired: false).EnsureMigratedAsync();
        await migrate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*migration 068 requires retirement of every writer that persists untagged terms*");
        (await fixture.ReceiptsAsync()).Should().Be(receipts);
        (await fixture.RowsAsync()).Should().Be(before);
    }

    [SecurityMasterDatabaseFact]
    public async Task Upgrade_RefusesConcurrentWriter_AndReleasesTheLockForRetry()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        await fixture.WriteMigrationAsync(fixture.MigrationSql);
        await using var writer = new NpgsqlConnection(fixture.ConnectionString);
        await writer.OpenAsync();
        await using (var writerTransaction = await writer.BeginTransactionAsync())
        {
            await using (var command = writer.CreateCommand())
            {
                command.Transaction = writerTransaction;
                command.CommandText = $"select * from {fixture.Schema}.securities for update;";
                await command.ExecuteNonQueryAsync();
            }

            var migrate = () => fixture.Runner().EnsureMigratedAsync();
            await migrate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*migration 068 cannot lock the securities table*");
            await writerTransaction.RollbackAsync();
        }

        await fixture.Runner().EnsureMigratedAsync();
        (await fixture.UntaggedCountAsync()).Should().Be(0);
    }

    [SecurityMasterDatabaseFact]
    public async Task RebuildAfterTheUpgrade_OfAnUntaggedEvent_PersistsATaggedRow_AndLeavesTheEventUntouched()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.WriteMigrationAsync(fixture.MigrationSql);
        var options = fixture.Options;
        var store = new PostgresSecurityMasterStore(options);
        var eventStore = new PostgresSecurityMasterEventStore(options);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(options);

        // Populated through 067 with an untagged row, as a pre-stamping writer left it.
        var securityId = Guid.NewGuid();
        const string untaggedTerms = """{"schemaVersion":1,"shareClass":"Common"}""";
        var stored = Projection(securityId, untaggedTerms, version: 1);
        await store.UpsertProjectionAsync(stored);
        await fixture.Runner().EnsureMigratedAsync();
        (await fixture.TermsAsync(securityId)).Should().Be("""{"shareClass": "Common", "termsFormat": "flat", "schemaVersion": 1}""");

        // A retained pre-tag event at a newer version than the row, carrying the untagged terms.
        var projected = SecurityEconomicDefinitionAdapter.ToEconomicRecord(Projection(securityId, untaggedTerms, version: 2));
        var economic = projected with { LegacyAssetSpecificTerms = Parse(untaggedTerms) };
        var @event = SecurityMasterMapping.ToEventEnvelope(economic, "TermsAmended", "codex", "test", null, 1);
        await eventStore.AppendAsync(securityId, expectedVersion: 0, [@event]);
        var eventPayloadBefore = await fixture.ScalarAsync(
            $"select payload::text from {fixture.Schema}.security_events where security_id = '{securityId}'");
        (await fixture.ScalarAsync(
                $"select payload->'legacyAssetSpecificTerms' ? 'termsFormat' from {fixture.Schema}.security_events where security_id = '{securityId}'"))
            .Should().Be("False", "the retained event carries the untagged pre-tag terms");

        var cache = new SecurityMasterProjectionCache();
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var orchestrator = new SecurityMasterRebuildOrchestrator(
            eventStore,
            store,
            cache,
            rebuilder,
            new SecurityMasterProjectionService(store, cache, rebuilder, NullLogger<SecurityMasterProjectionService>.Instance),
            options,
            NullLogger<SecurityMasterRebuildOrchestrator>.Instance,
            termsCapabilityVerifier: Substitute.For<ISecurityMasterTermsCapabilityVerifier>());

        (await orchestrator.RebuildAssetClassAsync("Equity")).Should().Be(1);

        (await fixture.ScalarAsync($"select version from {fixture.Schema}.securities where security_id = '{securityId}'")).Should().Be("2");
        (await fixture.TermsAsync(securityId)).Should().Be("""{"shareClass": "Common", "termsFormat": "flat", "schemaVersion": 1}""");
        (await fixture.ScalarAsync($"select payload::text from {fixture.Schema}.security_events where security_id = '{securityId}'"))
            .Should().Be(eventPayloadBefore, "security_events is append-only and is never rewritten");
        (await fixture.UntaggedCountAsync()).Should().Be(0);
    }

    [SecurityMasterDatabaseFact]
    public async Task SnapshotRegeneration_ReserializesAnUntaggedSnapshot_ThroughTheReplayDecoder_AndIsIdempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.WriteMigrationAsync(fixture.MigrationSql);
        await fixture.Runner().EnsureMigratedAsync();
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(fixture.Options);

        var securityId = Guid.NewGuid();
        const string untaggedTerms = """{"schemaVersion":1,"shareClass":"Common"}""";
        var projected = SecurityEconomicDefinitionAdapter.ToEconomicRecord(Projection(securityId, untaggedTerms, version: 3));
        var economic = projected with { LegacyAssetSpecificTerms = Parse(untaggedTerms) };
        var timestamp = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        await snapshotStore.SaveAsync(SecurityMasterMapping.ToSnapshot(economic, timestamp));

        var service = new SecurityMasterSnapshotRegenerationService(snapshotStore, NullLogger<SecurityMasterSnapshotRegenerationService>.Instance);
        (await service.RegenerateAsync()).Should().Be(new SecurityMasterSnapshotRegenerationResult(1, 1, 0, 0));

        var regenerated = await snapshotStore.LoadAsync(securityId);
        regenerated!.Version.Should().Be(3);
        regenerated.SnapshotTimestamp.Should().Be(timestamp);
        var legacyTerms = regenerated.Payload.GetProperty("legacyAssetSpecificTerms");
        SecurityTermsEnvelope.GetDeclaredFormat(legacyTerms).Should().Be(SecurityTermsFormats.Flat);
        legacyTerms.TryGetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey, out _).Should().BeFalse();
        var expected = SecurityMasterMapping.ToSnapshot(SecurityMasterMapping.FromEconomicPayload(
            SecurityMasterMapping.ToSnapshot(economic, timestamp).Payload), timestamp);
        (await fixture.ScalarAsync($"select payload = '{expected.Payload.GetRawText().Replace("'", "''", StringComparison.Ordinal)}'::jsonb from {fixture.Schema}.security_snapshots where security_id = '{securityId}'"))
            .Should().Be("True", "the snapshot is the replayed record re-serialized through ToSnapshot");

        (await service.RegenerateAsync()).Should().Be(new SecurityMasterSnapshotRegenerationResult(1, 0, 1, 0));

        // A snapshot a concurrent write moved to another version is never overwritten.
        var moved = SecurityMasterMapping.ToSnapshot(economic with { Version = 4 }, timestamp) with { Version = 3 };
        await fixture.ScalarAsync($"update {fixture.Schema}.security_snapshots set version = 4 where security_id = '{securityId}'");
        (await snapshotStore.ReplaceAsync(moved)).Should().Be(SecuritySnapshotReplaceOutcome.VersionChanged);
        (await snapshotStore.ReplaceAsync(moved with { SecurityId = Guid.NewGuid() })).Should().Be(SecuritySnapshotReplaceOutcome.VersionChanged);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static SecurityProjectionRecord Projection(Guid securityId, string assetSpecificTerms, long version)
    {
        var effectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var identifier = $"TF068-{securityId:N}"[..20];
        return new SecurityProjectionRecord(
            securityId,
            "Equity",
            SecurityStatusDto.Active,
            "Terms format backfill",
            "USD",
            SecurityIdentifierKind.Ticker.ToString(),
            identifier,
            Parse("""{"displayName":"Terms format backfill","currency":"USD"}"""),
            Parse(assetSpecificTerms),
            Parse("""{"sourceSystem":"tests","asOf":"2026-01-01T00:00:00+00:00","updatedBy":"terms-format-backfill"}"""),
            version,
            effectiveFrom,
            null,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, identifier, true, effectiveFrom)],
            []);
    }

    private sealed class Fixture(PostgresTestServer server, string scripts, string schema, string migrationSql) : IAsyncDisposable
    {
        public string Schema { get; } = schema;
        public string MigrationSql { get; } = migrationSql;
        public string ConnectionString => server.ConnectionString;

        public SecurityMasterOptions Options => new()
        {
            ConnectionString = server.ConnectionString,
            Schema = Schema,
            PreloadProjectionCache = false
        };

        /// <summary>A schema migrated through the last migration before 068.</summary>
        public static async Task<Fixture> CreateAsync()
        {
            var server = await PostgresTestServer.CreateAsync("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING");
            var scripts = Path.Combine(Path.GetTempPath(), $"meridian-terms-format-backfill-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(scripts);
                var source = Path.Combine(AppContext.BaseDirectory, "SecurityMaster", "Migrations");
                File.Exists(Path.Combine(source, BackfillMigration)).Should().BeTrue();
                foreach (var file in Directory.GetFiles(source, "*.sql"))
                {
                    if (StringComparer.Ordinal.Compare(Path.GetFileName(file), BackfillMigration) < 0)
                        File.Copy(file, Path.Combine(scripts, Path.GetFileName(file)));
                }

                var fixture = new Fixture(server, scripts, server.CreateSchemaName("terms_format_backfill"),
                    await File.ReadAllTextAsync(Path.Combine(source, BackfillMigration)));
                await fixture.Runner().EnsureMigratedAsync();
                return fixture;
            }
            catch
            {
                await server.DisposeAsync();
                if (Directory.Exists(scripts))
                    Directory.Delete(scripts, recursive: true);
                throw;
            }
        }

        public PostgresMigrationRunner Runner(bool legacyWritersRetired = true) => new(new PostgresMigrationRunnerOptions
        {
            ConnectionString = new NpgsqlConnectionStringBuilder(server.ConnectionString) { CommandTimeout = 30 }.ConnectionString,
            Schema = Schema,
            ScriptsSubdirectory = scripts,
            DisplayName = "Security Master",
            LockScopeName = "security_master",
            ConnectionStringSettingName = "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING",
            TrackOrdinals = true,
            BeforeScriptAsync = (fileName, connection, transaction, isUpgrade, ct) =>
                SecurityMasterMigrationRunner.CheckApplyConditionsAsync(new SecurityMasterOptions
                {
                    Schema = Schema,
                    IdentifierHistoryMigrationWritersStopped = true,
                    IdentifierHistoryMigrationLegacyWritersRetired = true,
                    TermsFormatBackfillLegacyWritersRetired = legacyWritersRetired
                }, fileName, connection, transaction, isUpgrade, ct)
        });

        public Task WriteMigrationAsync(string sql) => File.WriteAllTextAsync(Path.Combine(scripts, BackfillMigration), sql);

        public Task<string> ReceiptsAsync() => ScalarAsync(
            $"select coalesce(jsonb_agg(to_jsonb(r) order by filename), '[]'::jsonb)::text from {Schema}.schema_migrations r");

        public Task<string> RowsAsync() => ScalarAsync($"""
            select coalesce(jsonb_agg(jsonb_build_object(
                'security_id', security_id, 'asset_specific_terms', asset_specific_terms, 'version', version,
                'schema_version', schema_version, 'terms_format', terms_format, 'common_terms', common_terms)
                order by security_id), '[]'::jsonb)::text
            from {Schema}.securities
            """);

        public Task<string> TermsAsync(Guid securityId) => ScalarAsync(
            $"select asset_specific_terms::text from {Schema}.securities where security_id = '{securityId}'");

        /// <summary>The ADR-024 decision-5 count, exactly as the store measures it.</summary>
        public async Task<int> UntaggedCountAsync()
            => await new PostgresSecurityMasterStore(Options).CountUntaggedAssetSpecificTermsAsync();

        public async Task<bool> ConstraintExistsAsync()
            => await ScalarAsync($"select count(*) from pg_constraint where conname = 'ck_securities_terms_format_declared' and conrelid = '{Schema}.securities'::regclass") == "1";

        public async Task<string> ScalarAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(server.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
        }

        /// <summary>
        /// Seven securities covering the shapes the backfill meets: untagged flat rows with and
        /// without schemaVersion, an untagged profile envelope, a non-string termsFormat, an
        /// untagged version-2 document without economic modules, and already-tagged flat and
        /// economic rows the backfill must leave alone. Columns are what the store derived.
        /// </summary>
        public Task SeedAsync() => ScalarAsync($$$"""
            insert into {{{Schema}}}.securities (security_id, asset_class, status, display_name, currency,
                primary_identifier_kind, primary_identifier_value, normalized_primary_identifier_value,
                common_terms, asset_specific_terms, provenance, version, schema_version, terms_format, effective_from)
            values
                ('{{{UntaggedFlat}}}', 'Equity', 'Active', 'Untagged flat', 'USD', 'Ticker', 'TF1', 'TF1', '{"displayName":"Untagged flat"}', '{"schemaVersion":1,"shareClass":"Common"}', '{}', 3, 1, 'flat', '2026-01-01T00:00:00Z'),
                ('{{{UnstampedFlat}}}', 'Equity', 'Active', 'Unstamped flat', 'USD', 'Ticker', 'TF2', 'TF2', '{}', '{"shareClass":"Preferred"}', '{}', 1, 1, 'flat', '2026-01-01T00:00:00Z'),
                ('{{{UntaggedProfile}}}', 'CustomAsset', 'Active', 'Untagged profile', 'USD', 'Ticker', 'TF3', 'TF3', '{}', '{"customProfileId":"co-invest-spv","profileVersion":2,"profileFields":{"vehicleName":"SPV I"}}', '{}', 2, 1, 'customProfile', '2026-01-01T00:00:00Z'),
                ('{{{NullTagFlat}}}', 'Equity', 'Inactive', 'Null tag flat', 'USD', 'Ticker', 'TF4', 'TF4', '{}', '{"schemaVersion":1,"termsFormat":null,"shareClass":"Common"}', '{}', 5, 1, 'flat', '2026-01-01T00:00:00Z'),
                ('{{{TaggedFlat}}}', 'Equity', 'Active', 'Tagged flat', 'USD', 'Ticker', 'TF5', 'TF5', '{}', '{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1,"shareClass":"Common"}', '{}', 1, 1, 'flat', '2026-01-01T00:00:00Z'),
                ('{{{TaggedEconomic}}}', 'Bond', 'Active', 'Tagged economic', 'USD', 'Ticker', 'TF6', 'TF6', '{}', '{"schemaVersion":2,"termsFormat":"economic","maturity":{"maturityDate":"2030-01-01"}}', '{}', 1, 2, 'economic', '2026-01-01T00:00:00Z'),
                ('{{{UntaggedReservedTwo}}}', 'Equity', 'Active', 'Untagged reserved two', 'USD', 'Ticker', 'TF7', 'TF7', '{}', '{"schemaVersion":2,"shareClass":"Common"}', '{}', 1, 2, 'flat', '2026-01-01T00:00:00Z');
            """);

        public async ValueTask DisposeAsync()
        {
            try
            { await server.DisposeAsync(); }
            finally
            {
                if (Directory.Exists(scripts))
                    Directory.Delete(scripts, recursive: true);
            }
        }
    }
}

/// <summary>
/// Script-level guards for migration 068 that need no database: the M1 shape rule is the
/// shipped 042 rule (the same vocabulary <see cref="SecurityTermsEnvelope.ResolveFormat"/> uses),
/// and the apply gate's timeout bound.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterTermsFormatBackfillMigrationScriptTests
{
    [Fact]
    public void BackfillRefusal_UsesTheFrozenShapeRuleVocabulary_AndStampsOnlyFlatAndProfileRows()
    {
        // The statements only; the comments explain what the script never does.
        var sql = string.Join('\n', File.ReadAllLines(Path.Combine(FindRepoRoot(),
                "src", "Meridian.Storage", "SecurityMaster", "Migrations", "068_security_master_terms_format_backfill.sql"))
            .Where(static line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

        foreach (var module in SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.ShapeRuleModules)
        {
            sql.Should().Contain($"'{module}'", $"an untagged v2 document carrying '{module}' is classified economic by ResolveFormat");
        }

        sql.Should().Contain($"asset_specific_terms->>'schemaVersion' = '{EconomicTermsSchema.Current}'");
        sql.Should().Contain($"'{SecurityTermsEnvelope.FormatKey}'");
        sql.Should().Contain($"terms_format in ('{SecurityTermsFormats.Flat}', '{SecurityTermsFormats.CustomProfile}')",
            "the backfill never declares the economic family on a legacy row");
        sql.Should().NotContain(SecurityTermsEnvelope.AssetClassTermsVersionKey, "the backfill never adds a terms version");
        sql.Should().Contain("ck_securities_terms_format_declared");
        sql.Should().NotMatchRegex(@"(?i)update\s+__SCHEMA__\.security_(events|snapshots)", "events are append-only and snapshots are regenerated by the application");
    }

    [Theory]
    [InlineData(1_000_000, 30, false)]
    [InlineData(1_000_001, 0, false)]
    [InlineData(1_000_001, 31, false)]
    [InlineData(1_000_001, 30, true)]
    [InlineData(1_000_001, 1, true)]
    public void TermsFormatBackfillTimeout_RequiresARaisedTimeoutAboveAMillionRows(long rows, int timeout, bool refused)
    {
        var act = () => SecurityMasterMigrationRunner.CheckTermsFormatBackfillTimeout(rows, timeout);
        if (refused)
            act.Should().Throw<InvalidOperationException>().WithMessage("*migration 068*Raise Command Timeout above 30 seconds*");
        else
            act.Should().NotThrow();
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }
}

// Keep the same environment and Docker availability policy as the surrounding database facts.
file sealed class SecurityMasterDatabaseTheoryAttribute : TheoryAttribute
{
    public SecurityMasterDatabaseTheoryAttribute() => Skip = new SecurityMasterDatabaseFactAttribute().Skip;
}
