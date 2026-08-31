using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class ReportingDeliveryReleaseGateTests
{
    public static TheoryData<ReportingRunStatus> UnreleasedStatuses => new()
    {
        ReportingRunStatus.Draft,
        ReportingRunStatus.InReview,
        ReportingRunStatus.Approved,
        ReportingRunStatus.Failed
    };

    [Theory]
    [MemberData(nameof(UnreleasedStatuses))]
    public void DeliverReportingRun_UnreleasedRun_FailsWithoutPersistingAttempt(ReportingRunStatus status)
    {
        var sut = new ReportPackDeliveryService(new ReportPackWorkflowService());
        var manifest = new ReportingOutputManifest(
            "run-gated",
            "investor-monthly-statement",
            new DateOnly(2026, 6, 30),
            status,
            ImmutableArray<ReportingSectionManifest>.Empty,
            ImmutableArray<string>.Empty,
            1,
            ReportingRunTrigger.Scheduled);
        var target = new ReportingScheduleDeliveryTargetDto("investor-relations");

        var act = () => sut.DeliverReportingRun(manifest, target, "scheduler");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only Released runs may enter distribution*");
        sut.ListAttempts().Should().BeEmpty();
    }

    [Fact]
    public void DeliverReportingRun_DownloadedBytesMatchRetainedIntegrityAndNeutralizeDelimitedFormulas()
    {
        var sut = new ReportPackDeliveryService(new ReportPackWorkflowService());
        var manifest = new ReportingOutputManifest(
            "run-integrity-formula-guard",
            "investor-monthly-statement",
            new DateOnly(2026, 6, 30),
            ReportingRunStatus.Released,
            ImmutableArray<ReportingSectionManifest>.Empty,
            ImmutableArray.Create("trial-balance.csv", "=cmd.csv", "reviewed;=HYPERLINK.csv"),
            1,
            ReportingRunTrigger.Scheduled,
            BrandingTheme: new ReportBrandingThemeDto(
                "formula-guard",
                "Formula guard",
                "Meridian",
                "#123456",
                "#654321",
                "#111111",
                "#ffffff",
                Disclaimer: "approved;=HYPERLINK(\"https://example.invalid\")"));
        var target = new ReportingScheduleDeliveryTargetDto(
            "investor-relations",
            [
                GovernanceReportArtifactFormatDto.Json,
                GovernanceReportArtifactFormatDto.Csv,
                GovernanceReportArtifactFormatDto.Xlsx,
                GovernanceReportArtifactFormatDto.Html,
                GovernanceReportArtifactFormatDto.Pdf
            ],
            ReportPackDeliveryModeDto.EmailLink);

        var attempt = sut.DeliverReportingRun(manifest, target, "scheduler");

        attempt.Package.Should().NotBeNull();
        var package = attempt.Package!;
        var token = package.SecureLink.Split("token=", 2, StringSplitOptions.None)[1];
        foreach (var artifact in package.Artifacts)
        {
            var downloaded = sut.GetArtifact(
                attempt.ReportId,
                attempt.AttemptId,
                artifact.ArtifactName,
                token);
            downloaded.Content.LongLength.Should().Be(artifact.ByteSize);
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(downloaded.Content))
                .ToLowerInvariant()
                .Should()
                .Be(artifact.ChecksumSha256);
        }

        var csvArtifact = package.Artifacts.Single(
            artifact => artifact.Format == GovernanceReportArtifactFormatDto.Csv);
        var csv = System.Text.Encoding.UTF8.GetString(
            sut.GetArtifact(attempt.ReportId, attempt.AttemptId, csvArtifact.ArtifactName, token).Content);
        csv.Should().Contain("'=cmd.csv");
        csv.Should().Contain(";'=HYPERLINK.csv");
        csv.Should().Contain("approved;'=HYPERLINK");
    }

    [Fact]
    public void GetArtifact_ReloadedSnapshot_ReturnsExactRetainedXlsxBytesDespiteRendererInputDrift()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-reload", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            const string originalDisclaimer = "ORIGINAL-RENDERER-DISCLAIMER";
            const string driftedDisclaimer = "DRIFTED-RENDERER-DISCLAIMER";
            var sut = CreatePersistedService(snapshotPath);
            var attempt = sut.DeliverReportingRun(
                CreateReleasedManifest("run-reloaded-integrity", originalDisclaimer),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Xlsx),
                "scheduler");
            var package = attempt.Package!;
            var artifact = package.Artifacts.Single();
            var token = GetPackageToken(package);
            var originallyDownloaded = sut.GetArtifact(
                attempt.ReportId,
                attempt.AttemptId,
                artifact.ArtifactName,
                token).Content;
            var snapshot = File.ReadAllText(snapshotPath);
            snapshot.Should().Contain("\"artifactContents\"");
            snapshot.Should().NotContain("\"contentBase64\"");
            snapshot.Should().Contain(originalDisclaimer);
            var retainedMapping = JsonNode.Parse(snapshot)!["artifactContents"]!.AsArray()[0]!.AsObject();
            File.Exists(Path.Combine(
                $"{Path.GetFullPath(snapshotPath)}.artifacts",
                retainedMapping["blobName"]!.GetValue<string>())).Should().BeTrue();
            File.WriteAllText(
                snapshotPath,
                snapshot.Replace(originalDisclaimer, driftedDisclaimer, StringComparison.Ordinal));

            var reloaded = CreatePersistedService(snapshotPath);
            var downloaded = reloaded.GetArtifact(
                attempt.ReportId,
                attempt.AttemptId,
                artifact.ArtifactName,
                token);

            downloaded.Content.Should().Equal(originallyDownloaded);
            downloaded.Content.LongLength.Should().Be(artifact.ByteSize);
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(downloaded.Content))
                .ToLowerInvariant()
                .Should()
                .Be(artifact.ChecksumSha256);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void GetArtifact_LegacySnapshotWithoutRetainedBytes_RegeneratesVerifiedDeterministicArtifact()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-legacy-json", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var sut = CreatePersistedService(snapshotPath);
            var attempt = sut.DeliverReportingRun(
                CreateReleasedManifest("run-legacy-json", "legacy deterministic disclaimer"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Json),
                "scheduler");
            var package = attempt.Package!;
            var artifact = package.Artifacts.Single();
            var token = GetPackageToken(package);
            var expected = sut.GetArtifact(
                attempt.ReportId,
                attempt.AttemptId,
                artifact.ArtifactName,
                token).Content;
            RemoveRetainedArtifactContents(snapshotPath);

            var reloaded = CreatePersistedService(snapshotPath);
            var downloaded = reloaded.GetArtifact(
                attempt.ReportId,
                attempt.AttemptId,
                artifact.ArtifactName,
                token);

            downloaded.Content.Should().Equal(expected);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void GetArtifact_UnreproducibleLegacyXlsx_RequiresInvalidationAndRedelivery()
    {
        const string originalDisclaimer = "ORIGINAL-LEGACY-XLSX-DISCLAIMER";
        const string driftedDisclaimer = "DRIFTED-LEGACY-XLSX-DISCLAIMER";
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-legacy-xlsx", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var sut = CreatePersistedService(snapshotPath);
            var attempt = sut.DeliverReportingRun(
                CreateReleasedManifest("run-legacy-xlsx", originalDisclaimer),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Xlsx),
                "scheduler");
            var package = attempt.Package!;
            var artifact = package.Artifacts.Single();
            var token = GetPackageToken(package);
            RemoveRetainedArtifactContents(snapshotPath);
            var snapshot = File.ReadAllText(snapshotPath);
            File.WriteAllText(
                snapshotPath,
                snapshot.Replace(originalDisclaimer, driftedDisclaimer, StringComparison.Ordinal));

            var reloaded = CreatePersistedService(snapshotPath);
            var act = () => reloaded.GetArtifact(
                attempt.ReportId,
                attempt.AttemptId,
                artifact.ArtifactName,
                token);

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*Legacy delivery artifact*cannot be reproduced*Invalidate this package and re-deliver*");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void GetArtifact_TamperedRetainedBytes_FailsClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-retained-tamper", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var sut = CreatePersistedService(snapshotPath);
            var attempt = sut.DeliverReportingRun(
                CreateReleasedManifest("run-retained-tamper", "retained disclaimer"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Xlsx),
                "scheduler");
            var package = attempt.Package!;
            var artifact = package.Artifacts.Single();
            var token = GetPackageToken(package);
            var snapshot = JsonNode.Parse(File.ReadAllText(snapshotPath))!.AsObject();
            var retainedArtifact = snapshot["artifactContents"]!.AsArray()[0]!.AsObject();
            var blobPath = Path.Combine(
                $"{Path.GetFullPath(snapshotPath)}.artifacts",
                retainedArtifact["blobName"]!.GetValue<string>());
            var bytes = File.ReadAllBytes(blobPath);
            bytes[0] ^= 0x01;
            File.WriteAllBytes(blobPath, bytes);

            var reloaded = CreatePersistedService(snapshotPath);
            var act = () => reloaded.GetArtifact(
                attempt.ReportId,
                attempt.AttemptId,
                artifact.ArtifactName,
                token);

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*failed retained integrity verification*");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FileStore_CurrentSnapshotMissingExpectedRetainedEntry_FailsClosedAsCorrupt()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-current-missing", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var sut = CreatePersistedService(snapshotPath);
            sut.DeliverReportingRun(
                CreateReleasedManifest("run-current-missing", "current snapshot"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Xlsx),
                "scheduler");
            var snapshot = JsonNode.Parse(File.ReadAllText(snapshotPath))!.AsObject();
            snapshot["schemaVersion"]!.GetValue<int>().Should().BeGreaterThan(0);
            snapshot["artifactContents"]!.AsArray().Clear();
            File.WriteAllText(snapshotPath, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var act = () => CreatePersistedService(snapshotPath);

            var exception = act.Should().Throw<ReportingStateCorruptionException>().Which;
            exception.StatePath.Should().Be(snapshotPath);
            exception.InnerException.Should().BeOfType<JsonException>()
                .Which.Message.Should().Contain("missing");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FileStore_UnsupportedFutureSnapshotSchema_FailsClosedAsCorrupt()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-future-schema", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var sut = CreatePersistedService(snapshotPath);
            sut.DeliverReportingRun(
                CreateReleasedManifest("run-future-schema", "future schema"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Json),
                "scheduler");
            var snapshot = JsonNode.Parse(File.ReadAllText(snapshotPath))!.AsObject();
            snapshot["schemaVersion"] = int.MaxValue;
            File.WriteAllText(snapshotPath, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var act = () => CreatePersistedService(snapshotPath);

            var exception = act.Should().Throw<ReportingStateCorruptionException>().Which;
            exception.StatePath.Should().Be(snapshotPath);
            exception.InnerException.Should().BeOfType<JsonException>()
                .Which.Message.Should().Contain("unsupported");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FileStore_UnversionedMalformedEmbeddedContent_FailsClosedAsCorrupt()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-legacy-malformed", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var sut = CreatePersistedService(snapshotPath);
            sut.DeliverReportingRun(
                CreateReleasedManifest("run-legacy-malformed", "malformed legacy content"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Json),
                "scheduler");
            var (snapshot, mapping, _) = ConvertToLegacyEmbeddedContent(snapshotPath);
            mapping["contentBase64"] = "not-valid-base64!";
            File.WriteAllText(snapshotPath, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var act = () => CreatePersistedService(snapshotPath);

            act.Should().Throw<ReportingStateCorruptionException>();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FileStore_UnversionedChecksumMismatchedEmbeddedContent_FailsClosedAsCorrupt()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-legacy-mismatch", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var sut = CreatePersistedService(snapshotPath);
            sut.DeliverReportingRun(
                CreateReleasedManifest("run-legacy-mismatch", "mismatched legacy content"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Json),
                "scheduler");
            var (snapshot, mapping, content) = ConvertToLegacyEmbeddedContent(snapshotPath);
            content[0] ^= 0x01;
            mapping["contentBase64"] = Convert.ToBase64String(content);
            File.WriteAllText(snapshotPath, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var act = () => CreatePersistedService(snapshotPath);

            act.Should().Throw<ReportingStateCorruptionException>();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FileStore_CurrentBlobMappingWithEmbeddedContent_FailsClosedAsCorrupt()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-current-embedded", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var sut = CreatePersistedService(snapshotPath);
            sut.DeliverReportingRun(
                CreateReleasedManifest("run-current-embedded", "current embedded content"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Json),
                "scheduler");
            var snapshot = JsonNode.Parse(File.ReadAllText(snapshotPath))!.AsObject();
            snapshot["artifactContents"]!.AsArray()[0]!["contentBase64"] =
                Convert.ToBase64String([0x01]);
            File.WriteAllText(snapshotPath, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var act = () => CreatePersistedService(snapshotPath);

            act.Should().Throw<ReportingStateCorruptionException>();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FileStore_MetadataOnlySave_RejectsNewArtifactPathsButAllowsPackageNullAttempts()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-metadata-only", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var source = new ReportPackDeliveryService(new ReportPackWorkflowService());
            var delivered = source.DeliverReportingRun(
                CreateReleasedManifest("run-metadata-only", "metadata-only guard"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Json),
                "scheduler");
            var failure = delivered with
            {
                AttemptId = Guid.NewGuid(),
                State = ReportPackDeliveryStateDto.Failed,
                Package = null
            };
            var store = new FileReportPackDeliveryRecordStore(
                new ReportPackDeliveryStoreOptions(snapshotPath),
                NullLogger<FileReportPackDeliveryRecordStore>.Instance);

            store.Save([failure]);
            var act = () => store.Save([failure, delivered]);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*cannot introduce package artifact paths without exact retained bytes*");
            store.Load().Should().ContainSingle()
                .Which.AttemptId.Should().Be(failure.AttemptId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FileStore_MetadataOnlySave_RejectsLegacyArtifactPathRemovalAndSnapshotRemainsReloadable()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-metadata-removal", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var sut = CreatePersistedService(snapshotPath);
            var attempt = sut.DeliverReportingRun(
                CreateReleasedManifest("run-metadata-removal", "preserve artifact history"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Json),
                "scheduler");
            var (snapshot, _, _) = ConvertToLegacyEmbeddedContent(snapshotPath);
            File.WriteAllText(snapshotPath, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            var store = new FileReportPackDeliveryRecordStore(
                new ReportPackDeliveryStoreOptions(snapshotPath),
                NullLogger<FileReportPackDeliveryRecordStore>.Instance);
            store.Load().Should().ContainSingle();

            var act = () => store.Save([]);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*cannot remove retained package artifact paths*");
            store.Load().Should().ContainSingle()
                .Which.AttemptId.Should().Be(attempt.AttemptId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileStore_MetadataOnlySave_RejectsLegacySamePathMetadataMutationAndSnapshotRemainsReloadable(
        bool mutateByteSize)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-metadata-mutation", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var sut = CreatePersistedService(snapshotPath);
            var attempt = sut.DeliverReportingRun(
                CreateReleasedManifest("run-metadata-mutation", "authenticate artifact metadata"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Json),
                "scheduler");
            var originalPackage = attempt.Package!;
            var originalArtifact = originalPackage.Artifacts.Single();
            var (snapshot, _, _) = ConvertToLegacyEmbeddedContent(snapshotPath);
            File.WriteAllText(snapshotPath, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            var store = new FileReportPackDeliveryRecordStore(
                new ReportPackDeliveryStoreOptions(snapshotPath),
                NullLogger<FileReportPackDeliveryRecordStore>.Instance);
            store.Load().Should().ContainSingle();

            var mismatchedChecksum =
                $"{(originalArtifact.ChecksumSha256[0] == '0' ? '1' : '0')}{originalArtifact.ChecksumSha256[1..]}";
            var mutatedArtifact = mutateByteSize
                ? originalArtifact with { ByteSize = originalArtifact.ByteSize + 1 }
                : originalArtifact with { ChecksumSha256 = mismatchedChecksum };
            mutatedArtifact.RetainedPath.Should().Be(originalArtifact.RetainedPath);
            var mutatedAttempt = attempt with
            {
                Package = originalPackage with { Artifacts = [mutatedArtifact] }
            };

            var act = () => store.Save([mutatedAttempt]);

            act.Should().Throw<JsonException>()
                .WithMessage("*mismatched retained artifact content*");
            var reloadedArtifact = store.Load().Should().ContainSingle()
                .Which.Package!.Artifacts.Should().ContainSingle()
                .Which;
            reloadedArtifact.ByteSize.Should().Be(originalArtifact.ByteSize);
            reloadedArtifact.ChecksumSha256.Should().Be(originalArtifact.ChecksumSha256);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileStore_NullAttemptElement_FailsClosedAsCorrupt(bool unversioned)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-null-attempt", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var sut = CreatePersistedService(snapshotPath);
            sut.DeliverReportingRun(
                CreateReleasedManifest("run-null-attempt", "null attempt"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Json),
                "scheduler");
            var snapshot = JsonNode.Parse(File.ReadAllText(snapshotPath))!.AsObject();
            if (unversioned)
            {
                snapshot.Remove("schemaVersion").Should().BeTrue();
                snapshot.Remove("legacyArtifactPaths").Should().BeTrue();
            }

            snapshot["attempts"]!.AsArray().Add((JsonNode?)null);
            File.WriteAllText(snapshotPath, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var act = () => CreatePersistedService(snapshotPath);

            act.Should().Throw<ReportingStateCorruptionException>();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DeliverReportingRun_FailedPersistence_DoesNotPublishOrResurrectAttempt()
    {
        var store = new ThrowingReportPackDeliveryRecordStore();
        var sut = new ReportPackDeliveryService(new ReportPackWorkflowService(), store);

        var failed = () => sut.DeliverReportingRun(
            CreateReleasedManifest("run-persistence-failed", "must not publish"),
            CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Json),
            "scheduler");

        failed.Should().Throw<IOException>();
        sut.ListAttempts().Should().BeEmpty();

        store.ThrowOnSave = false;
        var persisted = sut.DeliverReportingRun(
            CreateReleasedManifest("run-persistence-success", "publish once"),
            CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Json),
            "scheduler");

        sut.ListAttempts().Should().ContainSingle()
            .Which.AttemptId.Should().Be(persisted.AttemptId);
        store.SavedAttempts.Should().ContainSingle()
            .Which.AttemptId.Should().Be(persisted.AttemptId);
    }

    [Fact]
    public void FileStore_MixedLegacyAndCurrentSnapshot_ProtectsNewBytesAndPreservesLegacyFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-mixed-schema", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var original = CreatePersistedService(snapshotPath);
            var legacyAttempt = original.DeliverReportingRun(
                CreateReleasedManifest("run-mixed-legacy", "legacy artifact"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Json),
                "scheduler");
            var legacyArtifact = legacyAttempt.Package!.Artifacts.Single();
            var legacyToken = GetPackageToken(legacyAttempt.Package!);
            var expectedLegacyBytes = original.GetArtifact(
                legacyAttempt.ReportId,
                legacyAttempt.AttemptId,
                legacyArtifact.ArtifactName,
                legacyToken).Content;
            RemoveRetainedArtifactContents(snapshotPath);

            var upgraded = CreatePersistedService(snapshotPath);
            var currentAttempt = upgraded.DeliverReportingRun(
                CreateReleasedManifest("run-mixed-current", "current artifact"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Xlsx),
                "scheduler");
            var currentArtifact = currentAttempt.Package!.Artifacts.Single();
            var snapshot = JsonNode.Parse(File.ReadAllText(snapshotPath))!.AsObject();
            snapshot["schemaVersion"]!.GetValue<int>().Should().Be(2);
            snapshot["legacyArtifactPaths"]!.AsArray()
                .Select(static node => node!.GetValue<string>())
                .Should().BeEquivalentTo([legacyArtifact.RetainedPath]);
            snapshot["artifactContents"]!.AsArray()
                .Select(static node => node!["retainedPath"]!.GetValue<string>())
                .Should().BeEquivalentTo([currentArtifact.RetainedPath]);

            var downloadedLegacy = upgraded.GetArtifact(
                legacyAttempt.ReportId,
                legacyAttempt.AttemptId,
                legacyArtifact.ArtifactName,
                legacyToken);
            downloadedLegacy.Content.Should().Equal(expectedLegacyBytes);

            var retainedContents = snapshot["artifactContents"]!.AsArray();
            retainedContents.Remove(
                retainedContents.Single(node =>
                    string.Equals(
                        node!["retainedPath"]!.GetValue<string>(),
                        currentArtifact.RetainedPath,
                        StringComparison.OrdinalIgnoreCase))).Should().BeTrue();
            File.WriteAllText(snapshotPath, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var act = () => CreatePersistedService(snapshotPath);

            act.Should().Throw<ReportingStateCorruptionException>();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FileStore_LaterSave_PreservesArtifactBlobPastAccessExpiry()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-report-pack-expired-retention", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "deliveries.json");
        try
        {
            var original = CreatePersistedService(snapshotPath);
            var expiredAttempt = original.DeliverReportingRun(
                CreateReleasedManifest("run-expired-retention", "expired artifact"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Xlsx),
                "scheduler");
            var expiredPath = expiredAttempt.Package!.Artifacts.Single().RetainedPath;
            var snapshot = JsonNode.Parse(File.ReadAllText(snapshotPath))!.AsObject();
            var expiredBlobName = snapshot["artifactContents"]!.AsArray()[0]!["blobName"]!.GetValue<string>();
            var expiredBlobPath = Path.Combine($"{Path.GetFullPath(snapshotPath)}.artifacts", expiredBlobName);
            snapshot["attempts"]!.AsArray()[0]!["package"]!.AsObject()["accessExpiresAtUtc"] =
                DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
            File.WriteAllText(snapshotPath, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var reloaded = CreatePersistedService(snapshotPath);
            var currentAttempt = reloaded.DeliverReportingRun(
                CreateReleasedManifest("run-current-retention", "current artifact"),
                CreateDeliveryTarget(GovernanceReportArtifactFormatDto.Json),
                "scheduler");
            var currentPath = currentAttempt.Package!.Artifacts.Single().RetainedPath;
            snapshot = JsonNode.Parse(File.ReadAllText(snapshotPath))!.AsObject();

            snapshot["attempts"]!.AsArray().Should().HaveCount(2);
            snapshot["artifactContents"]!.AsArray()
                .Select(static node => node!["retainedPath"]!.GetValue<string>())
                .Should().BeEquivalentTo([expiredPath, currentPath]);
            snapshot["legacyArtifactPaths"]!.AsArray()
                .Select(static node => node!.GetValue<string>())
                .Should().NotContain(expiredPath);
            File.Exists(expiredBlobPath).Should().BeTrue();
            reloaded.ListAttempts().Should().Contain(attempt => attempt.AttemptId == expiredAttempt.AttemptId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ReportPackDeliveryService CreatePersistedService(string snapshotPath)
    {
        var store = new FileReportPackDeliveryRecordStore(
            new ReportPackDeliveryStoreOptions(snapshotPath),
            NullLogger<FileReportPackDeliveryRecordStore>.Instance);
        return new ReportPackDeliveryService(new ReportPackWorkflowService(), store);
    }

    private static (JsonObject Snapshot, JsonObject Mapping, byte[] Content) ConvertToLegacyEmbeddedContent(
        string snapshotPath)
    {
        var snapshot = JsonNode.Parse(File.ReadAllText(snapshotPath))!.AsObject();
        var mapping = snapshot["artifactContents"]!.AsArray()[0]!.AsObject();
        var blobPath = Path.Combine(
            $"{Path.GetFullPath(snapshotPath)}.artifacts",
            mapping["blobName"]!.GetValue<string>());
        var content = File.ReadAllBytes(blobPath);
        snapshot.Remove("schemaVersion").Should().BeTrue();
        snapshot.Remove("legacyArtifactPaths").Should().BeTrue();
        mapping.Remove("blobName").Should().BeTrue();
        mapping.Remove("checksumSha256").Should().BeTrue();
        mapping.Remove("byteSize").Should().BeTrue();
        mapping["contentBase64"] = Convert.ToBase64String(content);
        return (snapshot, mapping, content);
    }

    private static void RemoveRetainedArtifactContents(string snapshotPath)
    {
        var snapshot = JsonNode.Parse(File.ReadAllText(snapshotPath))!.AsObject();
        snapshot.Remove("artifactContents").Should().BeTrue();
        snapshot.Remove("legacyArtifactPaths").Should().BeTrue();
        snapshot.Remove("schemaVersion").Should().BeTrue();
        File.WriteAllText(snapshotPath, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static ReportingOutputManifest CreateReleasedManifest(string runId, string disclaimer) =>
        new(
            runId,
            "investor-monthly-statement",
            new DateOnly(2026, 6, 30),
            ReportingRunStatus.Released,
            ImmutableArray<ReportingSectionManifest>.Empty,
            ImmutableArray.Create("trial-balance.csv"),
            1,
            ReportingRunTrigger.Scheduled,
            BrandingTheme: new ReportBrandingThemeDto(
                "integrity-theme",
                "Integrity theme",
                "Meridian",
                "#123456",
                "#654321",
                "#111111",
                "#ffffff",
                Disclaimer: disclaimer));

    private static ReportingScheduleDeliveryTargetDto CreateDeliveryTarget(
        GovernanceReportArtifactFormatDto format) =>
        new(
            "investor-relations",
            [format],
            ReportPackDeliveryModeDto.EmailLink);

    private static string GetPackageToken(ReportPackDeliveryPackageDto package) =>
        package.SecureLink.Split("token=", 2, StringSplitOptions.None)[1];

    private sealed class ThrowingReportPackDeliveryRecordStore : IReportPackDeliveryRecordStore
    {
        public bool ThrowOnSave { get; set; } = true;

        public IReadOnlyList<ReportPackDeliveryAttemptDto> SavedAttempts { get; private set; } = [];

        public IReadOnlyList<ReportPackDeliveryAttemptDto> Load() => [];

        public void Save(IReadOnlyList<ReportPackDeliveryAttemptDto> attempts)
        {
            if (ThrowOnSave)
            {
                throw new IOException("Injected report-pack delivery persistence failure.");
            }

            SavedAttempts = attempts.ToArray();
        }
    }
}
