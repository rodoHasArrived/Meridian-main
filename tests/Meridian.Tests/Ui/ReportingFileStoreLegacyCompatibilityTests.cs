using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class ReportingFileStoreLegacyCompatibilityTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 15, 16, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public async Task RunStore_ValidV1IsInventoriedArchivedAndNeverPromotedToCurrent()
    {
        using var fixture = new TempDirectory();
        var root = Path.Combine(fixture.Path, "runs");
        Directory.CreateDirectory(root);
        var snapshotPath = Path.Combine(root, "reporting-runs.json");
        var legacy = new ReportingRunSnapshot(
            BasicManifest("legacy-run") with
            {
                OperationalScope = new ReportingOperationalScope(
                    "tenant-a",
                    "organization-a",
                    "company-a",
                    "fund-a",
                    "book-a",
                    "2026-07"),
                AccessPolicy = new ReportAccessPolicyDto(
                    ReportAccessModeDto.CompanyWide,
                    CompanyId: "company-a")
            },
            [new ReportingRunAuditEntry("legacy-run", FixedNow, "created", "operator-a", "Legacy run")],
            FixedNow);
        var legacyJson = SerializeCommittedV1RunSnapshot(legacy);
        legacyJson.Should().NotContain("\"authoritativeSource\"");
        legacyJson.Should().NotContain("\"certifiedDatasetRows\"");
        legacyJson.Should().NotContain("\"certifiedDatasetHashSha256\"");
        legacyJson.Should().NotContain("\"manifestHashSha256\"");
        File.WriteAllText(snapshotPath, legacyJson);
        using var legacyDocument = JsonDocument.Parse(legacyJson);
        var exactRawEntry = legacyDocument.RootElement.GetProperty("runs")[0].GetRawText();
        var store = new FileReportingRunStore(
            new ReportingRunStoreOptions(root),
            NullLogger<FileReportingRunStore>.Instance);
        var tenantB = BoundAuthority("operator-b", "tenant-b", "company-b");
        var recovery = BoundAuthority(
            "local-recovery",
            "tenant-a",
            "company-a",
            hasGlobalOverride: true);

        store.HasLegacyRuns(tenantB).Should().BeFalse();
        store.ListLegacyRunInventory(tenantB).Should().BeEmpty();
        ((Action)(() => store.ExportLegacyRun("legacy-run", tenantB)))
            .Should().Throw<UnauthorizedAccessException>();
        store.HasLegacyRuns(recovery).Should().BeTrue();
        store.ListLegacyRunInventory(recovery).Should().ContainSingle(entry =>
            entry.RecordId == "legacy-run"
            && entry.TenantId == "tenant-a"
            && entry.OrganizationId == "organization-a"
            && entry.CompanyId == "company-a"
            && !entry.IsArchived
            && entry.Remediation.Contains("freshly recertify", StringComparison.Ordinal));
        store.ExportLegacyRun("legacy-run", recovery).Should().Be(exactRawEntry);
        ((Action)(() => store.ListRuns())).Should().Throw<ReportingLegacyStateRequiresArchiveException>();
        var blockedSave = () => store.SaveAsync(BasicManifest("blocked-current"), []);
        await blockedSave.Should().ThrowAsync<ReportingLegacyStateRequiresArchiveException>();

        var receipt = await store.ArchiveLegacySnapshotAsync(
            recovery,
            "Preserve pre-governance history before authoritative recertification.");

        receipt.ActorPrincipalId.Should().Be("local-recovery");
        receipt.RecordCount.Should().Be(1);
        receipt.ArchiveId.Should().MatchRegex("^[a-f0-9]{64}$");
        store.ListRuns().Should().BeEmpty("archiving v1 must not promote it to current authority");
        store.ExportLegacyRun("legacy-run", recovery).Should().Be(exactRawEntry);
        store.ListLegacyRunInventory(recovery).Should().ContainSingle(entry => entry.IsArchived);

        await store.SaveAsync(BasicManifest("current-run"), []);
        store.ListRuns().Should().ContainSingle(run => run.Manifest.RunId == "current-run");
        var mixedBytes = File.ReadAllText(snapshotPath);

        var repeated = await store.ArchiveLegacySnapshotAsync(recovery, "A different retry reason is ignored.");

        repeated.Should().Be(receipt);
        File.ReadAllText(snapshotPath).Should().Be(
            mixedBytes,
            "idempotent archive must preserve every current v2 and archived-v1 byte");

        var envelope = JsonNode.Parse(mixedBytes)!.AsObject();
        envelope["legacyRuns"]!.AsArray()[0]!.AsObject()["tenantId"] = "tenant-b";
        RehashRunLegacyEnvelope(envelope);
        File.WriteAllText(snapshotPath, envelope.ToJsonString(JsonOptions));

        var scopeSwap = () => store.ListRuns();

        scopeSwap.Should().Throw<ReportingStateCorruptionException>()
            .Which.InnerException!.Message.Should().Contain("scope index");
    }

    [Fact]
    public async Task ScheduleStore_MixedArchiveLoadsOnlyCurrentAndWorkerNeverRunsLegacy()
    {
        using var fixture = new TempDirectory();
        var snapshotPath = Path.Combine(fixture.Path, "reporting-schedules.json");
        var legacyScoped = LegacySchedule(
            "legacy-scoped",
            tenantId: "tenant-a",
            companyId: "company-a");
        var legacyUnscoped = LegacySchedule("legacy-unscoped", tenantId: null, companyId: null);
        var legacyJson = SerializeCommittedV1ScheduleSnapshot(legacyScoped, legacyUnscoped);
        legacyJson.Should().NotContain("\"releaseDeliveryHandoffs\"");
        legacyJson.Should().NotContain("\"accessPolicySnapshotHash\"");
        legacyJson.Should().NotContain("\"deliveryTargetsSnapshotHash\"");
        legacyJson.Should().NotContain("\"recipientPrincipalId\"");
        legacyJson.Should().NotContain("\"recipientPrincipalKind\"");
        File.WriteAllText(snapshotPath, legacyJson);
        using var legacyDocument = JsonDocument.Parse(legacyJson);
        var exactUnscopedRaw = legacyDocument.RootElement.GetProperty("schedules")[1].GetRawText();
        var store = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(snapshotPath),
            NullLogger<FileReportingScheduleStore>.Instance);
        var tenantA = BoundAuthority("operator-a", "tenant-a", "company-a");
        var tenantB = BoundAuthority("operator-b", "tenant-b", "company-b");
        var recovery = BoundAuthority(
            "local-recovery",
            "tenant-a",
            "company-a",
            hasGlobalOverride: true);

        store.ListLegacyScheduleInventory(tenantA).Should().BeEmpty(
            "a null-policy v1 schedule is recovery-only even for the matching tenant");
        store.ListLegacyScheduleInventory(tenantB).Should().BeEmpty();
        ((Action)(() => store.ExportLegacySchedule("legacy-scoped", tenantB)))
            .Should().Throw<UnauthorizedAccessException>();
        store.ListLegacyScheduleInventory(recovery).Should().HaveCount(2);
        store.ExportLegacySchedule("legacy-unscoped", recovery).Should().Be(exactUnscopedRaw);
        ((Action)(() => store.Load())).Should().Throw<ReportingLegacyStateRequiresArchiveException>();

        var receipt = store.ArchiveLegacySnapshot(
            recovery,
            "Freeze schedules that lack current tenant, access, parameter, and recipient authority.");

        receipt.RecordCount.Should().Be(2);
        store.Load().Should().BeEmpty();
        store.Save([CurrentSchedule("current-disabled", ReportingScheduleStateDto.Disabled)]);
        var mixedBytes = File.ReadAllText(snapshotPath);
        var service = new ReportingScheduleService(
            new ReportingOrchestrationService(
                new DefaultReportingTemplateCatalog(),
                new DeterministicReportingSectionRenderer(),
                () => FixedNow),
            store);

        service.ListSchedules(tenantA).Should().ContainSingle(schedule =>
            schedule.ScheduleId == "current-disabled");
        (await service.RunDueForWorkerAsync(FixedNow.AddDays(1), CancellationToken.None))
            .Result.Runs.Should().BeEmpty("archived v1 schedules are not hydrated into the worker");
        service.ListSchedules(tenantA).Should().NotContain(schedule =>
            schedule.ScheduleId.StartsWith("legacy-", StringComparison.Ordinal));

        store.ArchiveLegacySnapshot(recovery, "A repeated request must be idempotent.")
            .Should().Be(receipt);
        File.ReadAllText(snapshotPath).Should().Be(mixedBytes);
    }

    [Fact]
    public void ScheduleStore_CurrentActiveTargetsRequireTypedRecipientAndSnapshotHash()
    {
        using var fixture = new TempDirectory();
        var store = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(Path.Combine(fixture.Path, "reporting-schedules.json")),
            NullLogger<FileReportingScheduleStore>.Instance);
        var untypedTarget = new ReportingScheduleDeliveryTargetDto(
            "fund-operations",
            [GovernanceReportArtifactFormatDto.Pdf]);
        var untyped = CurrentSchedule("untyped", ReportingScheduleStateDto.Active) with
        {
            DeliveryTargets = [untypedTarget],
            DeliveryTargetsSnapshotHash = ReportingScheduleService.ComputeDeliveryTargetsSnapshotHash(
                [untypedTarget])
        };
        var typedTarget = untypedTarget with
        {
            RecipientPrincipalId = "operator-a",
            RecipientPrincipalKind = ReportAccessPrincipalKindDto.User
        };
        var missingHash = CurrentSchedule("missing-hash", ReportingScheduleStateDto.Active) with
        {
            DeliveryTargets = [typedTarget],
            DeliveryTargetsSnapshotHash = null
        };
        var valid = CurrentSchedule("valid", ReportingScheduleStateDto.Active) with
        {
            DeliveryTargets = [typedTarget],
            DeliveryTargetsSnapshotHash = ReportingScheduleService.ComputeDeliveryTargetsSnapshotHash(
                [typedTarget])
        };

        ((Action)(() => store.Save([untyped]))).Should().Throw<InvalidDataException>();
        ((Action)(() => store.Save([missingHash]))).Should().Throw<InvalidDataException>();
        store.Save([valid]);

        store.Load().Should().ContainSingle(schedule => schedule.ScheduleId == "valid");
    }

    [Fact]
    public void ScheduleStore_ArchivedLegacyTamperIsCorruptionNotARecoverableV1Record()
    {
        using var fixture = new TempDirectory();
        var snapshotPath = Path.Combine(fixture.Path, "reporting-schedules.json");
        File.WriteAllText(
            snapshotPath,
            SerializeCommittedV1ScheduleSnapshot(LegacySchedule("legacy", null, null)));
        var store = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(snapshotPath),
            NullLogger<FileReportingScheduleStore>.Instance);
        var recovery = new ReportAccessQueryContext(
            ActorPrincipalId: "local-recovery",
            HasGlobalOverride: true);
        store.ArchiveLegacySnapshot(recovery, "Retain exact v1 schedule for recovery.");
        var root = JsonNode.Parse(File.ReadAllText(snapshotPath))!.AsObject();
        root["legacySchedules"]!.AsArray()[0]!.AsObject()["rawPayloadJson"] = "{}";
        File.WriteAllText(snapshotPath, root.ToJsonString(JsonOptions));

        var read = () => store.Load();

        read.Should().Throw<ReportingStateCorruptionException>();
    }

    private static ReportingOutputManifest BasicManifest(string runId) =>
        new(
            runId,
            "shadow-nav-daily-pack",
            new DateOnly(2026, 7, 15),
            ReportingRunStatus.Draft,
            ImmutableArray<ReportingSectionManifest>.Empty,
            ImmutableArray<string>.Empty,
            1,
            ReportingRunTrigger.AdHoc,
            ReportWriterGrids: ImmutableArray<ReportingRunReportWriterGridArtifact>.Empty,
            RenderedReportWriterGrids: ImmutableArray<ReportWriterGridRenderDto>.Empty,
            ReportWriterGridDiffs: ImmutableArray<ReportWriterGridDiffDto>.Empty,
            CertifiedDatasetRows: ImmutableArray<IReadOnlyDictionary<string, string>>.Empty);

    private static string SerializeCommittedV1RunSnapshot(ReportingRunSnapshot run)
    {
        var root = JsonNode.Parse(JsonSerializer.Serialize(new { runs = new[] { run } }, JsonOptions))!
            .AsObject();
        var runNode = root["runs"]!.AsArray()[0]!.AsObject();
        runNode.Remove("certifiedDatasetHashSha256");
        runNode.Remove("manifestHashSha256");
        var manifestNode = runNode["manifest"]!.AsObject();
        manifestNode.Remove("authoritativeSource");
        manifestNode.Remove("certifiedDatasetRows");
        return root.ToJsonString(JsonOptions);
    }

    private static string SerializeCommittedV1ScheduleSnapshot(
        params ReportingScheduleRecordDto[] schedules)
    {
        var root = JsonNode.Parse(JsonSerializer.Serialize(new { schedules }, JsonOptions))!.AsObject();
        foreach (var scheduleNode in root["schedules"]!.AsArray()
                     .Select(static node => node!.AsObject()))
        {
            scheduleNode.Remove("releaseDeliveryHandoffs");
            scheduleNode.Remove("accessPolicySnapshotHash");
            scheduleNode.Remove("deliveryTargetsSnapshotHash");
            if (scheduleNode["deliveryTargets"] is not JsonArray targets)
            {
                continue;
            }

            foreach (var targetNode in targets)
            {
                var target = targetNode!.AsObject();
                target.Remove("recipientPrincipalId");
                target.Remove("recipientPrincipalKind");
            }
        }

        return root.ToJsonString(JsonOptions);
    }

    private static ReportingScheduleRecordDto LegacySchedule(
        string scheduleId,
        string? tenantId,
        string? companyId) =>
        new(
            scheduleId,
            "shadow-nav-daily-pack",
            "0 8 * * *",
            new DateOnly(2026, 7, 16),
            FixedNow.AddHours(-1),
            1,
            "operator-a",
            ReportingScheduleStateDto.Active,
            FixedNow.AddDays(-1),
            FixedNow.AddDays(-1),
            TenantId: tenantId,
            CompanyId: companyId,
            AccessPolicySnapshot: null,
            AccessPolicySnapshotHash: null,
            DeliveryTargetsSnapshotHash: null);

    private static ReportingScheduleRecordDto CurrentSchedule(
        string scheduleId,
        ReportingScheduleStateDto state)
    {
        var access = new ReportAccessPolicyDto(
            ReportAccessModeDto.Restricted,
            OwnerPrincipalId: "operator-a",
            Principals:
            [
                new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.User, "operator-a")
            ],
            CompanyId: "company-a",
            AllowOwnerAccess: true);
        return new ReportingScheduleRecordDto(
            scheduleId,
            "shadow-nav-daily-pack",
            "0 8 * * *",
            new DateOnly(2026, 7, 16),
            FixedNow.AddDays(1),
            1,
            "operator-a",
            state,
            FixedNow,
            FixedNow,
            Template: new VersionedReportTemplateIdDto("shadow-nav-daily-pack", 1),
            RunParameters: new ReportingRunParametersDto(
                new ReportingRunScopeDto("fund-a"),
                "2026-07",
                new DateOnly(2026, 7, 15),
                new ReportingLedgerBookSelectionDto(LedgerBookCode: "book-a"),
                ReportingAccountingBasisDto.Gaap,
                "USD",
                ReportingConsolidationLevelDto.Fund,
                ReportingOutputFormatDto.Pdf,
                ReportingFinalityDto.Draft,
                IncludeSupportingSchedules: true,
                IncludeEvidenceAppendix: true),
            TenantId: "tenant-a",
            CompanyId: "company-a",
            AccessPolicySnapshot: access,
            AccessPolicySnapshotHash: ReportingScheduleService.ComputeAccessPolicySnapshotHash(access),
            DeliveryTargetsSnapshotHash: null);
    }

    private static ReportAccessQueryContext BoundAuthority(
        string actor,
        string tenantId,
        string companyId,
        bool hasGlobalOverride = false) =>
        new(
            ActorPrincipalId: actor,
            CompanyId: companyId,
            HasGlobalOverride: hasGlobalOverride,
            TenantId: tenantId,
            RequireBoundScope: true);

    private static void RehashRunLegacyEnvelope(JsonObject envelope)
    {
        var legacyRuns = envelope["legacyRuns"]!.AsArray();
        var legacyEntries = legacyRuns.Deserialize<IReadOnlyList<JsonElement>>(JsonOptions)!;
        var receiptNode = envelope["legacyArchiveReceipt"]!.AsObject();
        var archivedPayloadHash = ComputeSha256(JsonSerializer.Serialize(legacyEntries, JsonOptions));
        receiptNode["archivedPayloadHashSha256"] = archivedPayloadHash;
        var actor = receiptNode["actorPrincipalId"]!.GetValue<string>();
        var reason = receiptNode["reason"]!.GetValue<string>();
        var archivedAtUtc = receiptNode["archivedAtUtc"]!.GetValue<DateTimeOffset>();
        receiptNode["archiveId"] = ComputeSha256(JsonSerializer.Serialize(new
        {
            kind = "run",
            actor,
            reason,
            archivedAtUtc,
            archivedPayloadHash
        }));
        var receipt = receiptNode.Deserialize<ReportingLegacyArchiveReceipt>(JsonOptions)!;
        envelope["legacyPayloadHashSha256"] = ComputeSha256(JsonSerializer.Serialize(new
        {
            entries = legacyEntries,
            archiveReceipt = receipt
        }, JsonOptions));
    }

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

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
