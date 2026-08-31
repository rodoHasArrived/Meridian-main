using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class ReportingRunStoreManifestHashTests
{
    [Fact]
    public async Task SaveAsync_NonCertifiedFailedManifestWithDefaultArrays_PersistsAndRoundTrips()
    {
        using var fixture = new TempDirectory();
        var store = new FileReportingRunStore(
            new ReportingRunStoreOptions(fixture.Path),
            NullLogger<FileReportingRunStore>.Instance);

        // A non-certified run-failure manifest reaches the store before any of the grid/diff
        // collections are populated, and with no AuthoritativeSource its CertifiedDatasetRows is
        // default too. Every optional ImmutableArray member is therefore default (uninitialized).
        // Serializing a default ImmutableArray throws InvalidOperationException, and ValidateManifest
        // rejects a default CertifiedDatasetRows outright — either would previously abort the write
        // and mask the real failure. SaveAsync must normalize the whole manifest and persist it.
        var manifest = new ReportingOutputManifest(
            "run-default-arrays",
            "shadow-nav-daily-pack",
            new DateOnly(2026, 7, 15),
            ReportingRunStatus.Failed,
            ImmutableArray<ReportingSectionManifest>.Empty,
            ImmutableArray<string>.Empty,
            1,
            ReportingRunTrigger.AdHoc,
            FailureReason: "renderer threw");

        manifest.ReportWriterGrids.IsDefault.Should().BeTrue();
        manifest.RenderedReportWriterGrids.IsDefault.Should().BeTrue();
        manifest.ReportWriterGridDiffs.IsDefault.Should().BeTrue();
        manifest.CertifiedDatasetRows.IsDefault.Should().BeTrue();

        await store.SaveAsync(manifest, []);

        // ListRuns re-verifies the persisted manifest hash on read; a clean round-trip proves that
        // the stored snapshot serialized successfully and that save-side and load-side hashing agree
        // on the normalized manifest.
        var retained = store.ListRuns().Should()
            .ContainSingle(run => run.Manifest.RunId == "run-default-arrays").Subject;
        retained.Manifest.CertifiedDatasetRows.IsDefault.Should().BeFalse();
        retained.Manifest.ReportWriterGrids.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task CertifiedManifestValidation_FileAndPostgresRejectSamePartialAuthorityBeforeIo()
    {
        using var fixture = new TempDirectory();
        var fileStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(fixture.Path),
            NullLogger<FileReportingRunStore>.Instance);
        var postgresStore = new PostgresReportingRunStore(
            new ReportingArtifactStoreOptions
            {
                ConnectionString =
                    "Host=127.0.0.1;Port=1;Database=unreachable;Username=test;Password=test;Timeout=1",
                Schema = "reporting"
            });
        var partial = new ReportingOutputManifest(
            "partial-certified-run",
            "capital-account-statement",
            new DateOnly(2026, 7, 31),
            ReportingRunStatus.Draft,
            [],
            [],
            1,
            ReportingRunTrigger.AdHoc,
            OperationalScope: new ReportingOperationalScope(
                "tenant-a",
                "organization-a",
                "company-a",
                "fund-a",
                "book-a",
                "period-a"));

        var fileWrite = () => fileStore.SaveAsync(partial, []);
        var postgresWrite = () => postgresStore.SaveAsync(partial, []);

        await fileWrite.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*certified reporting manifest*");
        await postgresWrite.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*certified reporting manifest*");
    }

    [Fact]
    public async Task CertifiedManifestValidation_FileAndPostgresRejectSameNonCanonicalPeriodBeforeIo()
    {
        using var fixture = new TempDirectory();
        var fileStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(fixture.Path),
            NullLogger<FileReportingRunStore>.Instance);
        var postgresStore = new PostgresReportingRunStore(
            new ReportingArtifactStoreOptions
            {
                ConnectionString =
                    "Host=127.0.0.1;Port=1;Database=unreachable;Username=test;Password=test;Timeout=1",
                Schema = "reporting"
            });
        var canonical = BuildCertifiedManifest("44444444-4444-4444-4444-444444444444");
        var nonCanonical = BuildCertifiedManifest("2026-07");

        Action validateCanonical = () =>
            ReportingCertifiedManifestValidation.Validate(canonical);
        var fileWrite = () => fileStore.SaveAsync(nonCanonical, []);
        var postgresWrite = () => postgresStore.SaveAsync(nonCanonical, []);

        validateCanonical.Should().NotThrow();
        await fileWrite.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*canonical non-empty GUID accounting-period identity*");
        await postgresWrite.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*canonical non-empty GUID accounting-period identity*");
    }

    private static ReportingOutputManifest BuildCertifiedManifest(string periodId)
    {
        var now = new DateTimeOffset(2026, 7, 25, 20, 0, 0, TimeSpan.Zero);
        var asOfDate = new DateOnly(2026, 7, 25);
        var scope = new ReportingOperationalScope(
            "tenant-a",
            "organization-a",
            "company-a",
            "fund-a",
            "book-a",
            periodId);
        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto(scope.FundId!),
            periodId,
            asOfDate,
            new ReportingLedgerBookSelectionDto(LedgerBookCode: scope.BookId),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            ReportingFinalityDto.Draft,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: false);
        var parametersJson = ReportingCanonicalParameterSerializer.Serialize(parameters);
        var parametersHash = ReportingCanonicalParameterSerializer.ComputeHash(parameters);
        var checkpointId = "checkpoint-a";
        var checkpointHash = new string('b', 64);
        var rows = ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["account"] = "cash",
                ["amount"] = "100.00"
            });
        var snapshot = new ReportingCertifiedSnapshotScope(
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            scope.FundId,
            scope.BookId,
            periodId,
            "snapshot-a",
            new string('0', 64),
            "reconciliation-a",
            now,
            checkpointId,
            checkpointHash,
            new string('c', 64),
            parametersJson,
            parametersHash);
        var manifest = new ReportingOutputManifest(
            "certified-run",
            "investor-monthly-statement",
            asOfDate,
            ReportingRunStatus.Draft,
            [],
            [],
            1,
            ReportingRunTrigger.AdHoc,
            ResolvedTemplate: new VersionedReportTemplateIdDto(
                "investor-monthly-statement",
                1),
            ResolvedParameters: parameters,
            Readiness: new ReportingRunReadinessDto(
                "readiness-a",
                now.AddMinutes(-1),
                new VersionedReportTemplateIdDto("investor-monthly-statement", 1),
                parameters,
                ReportingRunReadinessStatusDto.Ready,
                CanGenerateDraft: true,
                CanGenerateFinal: false,
                Checks:
                [
                    new ReportingRunReadinessCheckDto(
                        "source",
                        "Source",
                        ReportingRunReadinessStatusDto.Ready,
                        "Durable source is ready.",
                        IssueCount: 0,
                        BlocksDraft: true,
                        BlocksFinal: true,
                        EvidenceReferences: ["source:ready"])
                ],
                BlockingReasons: [],
                EvidenceHash: new string('d', 64)),
            OperationalScope: scope,
            ImmutableAccessScope: new ReportingAccessScope(
                "company-reporting",
                "1",
                ReportingGovernanceAccessMode.CompanyWide,
                OwnerPrincipalId: null,
                AllowOwnerAccess: false,
                Principals: [],
                PolicyHash: new string('a', 64)),
            CertifiedSnapshot: snapshot,
            AuthoritativeSource: new ReportingAuthoritativeSourceCheckpoint(
                "durable-ledger",
                "source-a",
                scope.TenantId,
                scope.OrganizationId,
                scope.CompanyId,
                scope.FundId!,
                scope.BookId,
                periodId,
                "Gaap",
                asOfDate,
                now.AddMinutes(-2),
                HighestGlobalSequence: 1,
                JournalEntryCount: 1,
                LedgerLineCount: rows.Length,
                checkpointId,
                checkpointHash,
                now.AddMinutes(-2),
                [$"reporting-source-checkpoint:{checkpointId}:{checkpointHash}"]),
            CertifiedDatasetRows: rows);
        return manifest with
        {
            CertifiedSnapshot = snapshot with
            {
                SnapshotHash =
                    ReportingCertifiedManifestValidation.ComputeSnapshotHash(manifest)
            }
        };
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Meridian.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
