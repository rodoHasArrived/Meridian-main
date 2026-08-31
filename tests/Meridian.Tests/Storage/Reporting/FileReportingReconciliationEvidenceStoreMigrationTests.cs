using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Storage.Reporting;

/// <summary>
/// Guards the operator recovery path when an integrity-verified, pre-break-evidence receipt is
/// opened after the reporting evidence schema upgrade.
/// </summary>
public sealed class FileReportingReconciliationEvidenceStoreMigrationTests
{
    private const string LegacySchemaVersion = "meridian.reporting.reconciliation-evidence.v1";
    private static readonly JsonSerializerOptions LegacyJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [Fact]
    public void ReconciliationEvidenceJsonContext_RegistersCurrentAndLegacyReceiptShapes()
    {
        ReportingReconciliationEvidenceJsonContext.Default.ReportingReconciliationEvidenceReceipt
            .Should().NotBeNull();
        ReportingReconciliationEvidenceJsonContext.Default.LegacyReportingReconciliationEvidenceReceipt
            .Should().NotBeNull();
    }

    [Fact]
    public async Task Scenario_PreBreakEvidenceReceipt_VerifiedV1SnapshotRequiresGovernedRecovery()
    {
        var receipt = NewLegacyReceipt();
        var fixture = SerializeLegacySnapshot(receipt);
        fixture.Should().NotContain("breakEvidence");

        await using var fixtureFile = await TemporarySnapshot.CreateAsync(fixture);
        var store = new FileReportingReconciliationEvidenceStore(fixtureFile.Path);

        Func<Task> act = async () => await store.GetExactAsync(
            receipt.TenantId,
            receipt.OrganizationId,
            receipt.CompanyId,
            receipt.FundId,
            receipt.LedgerBookId,
            receipt.AccountingPeriodId,
            receipt.AccountingBasis,
            receipt.AsOfDate,
            receipt.SourceCheckpointId,
            receipt.SourceCheckpointHash);

        var exception = (await act.Should()
            .ThrowAsync<ReportingReconciliationEvidenceMigrationRequiredException>()).Which;
        exception.Message.Should().Contain("Preserve the snapshot")
            .And.Contain("Do not edit the legacy file")
            .And.Contain("synthesize break evidence");
        (await File.ReadAllTextAsync(fixtureFile.Path)).Should().Be(fixture);
    }

    [Fact]
    public async Task Scenario_GovernedV2RetentionPreservesVerifiedV1AndRecoversConfiguredPathInPlace()
    {
        var legacy = NewLegacyReceipt();
        var legacyJson = SerializeLegacySnapshot(legacy);
        var current = NewCurrentReceipt();
        await using var fixtureFile = await TemporarySnapshot.CreateAsync(legacyJson);
        var store = new FileReportingReconciliationEvidenceStore(fixtureFile.Path);

        var alreadyExisted = await store.RetainAsync(current);

        alreadyExisted.Should().BeFalse();
        var backup = Directory.GetFiles(
            Path.GetDirectoryName(fixtureFile.Path)!,
            $"{Path.GetFileName(fixtureFile.Path)}.legacy-v1.*.json")
            .Should().ContainSingle().Subject;
        (await File.ReadAllTextAsync(backup)).Should().Be(legacyJson);
        (await File.ReadAllTextAsync(fixtureFile.Path)).Should()
            .Contain("meridian.reporting.reconciliation-evidence.v2")
            .And.Contain("breakEvidence");

        var restarted = new FileReportingReconciliationEvidenceStore(fixtureFile.Path);
        var retained = await restarted.GetExactAsync(
            current.TenantId,
            current.OrganizationId,
            current.CompanyId,
            current.FundId,
            current.LedgerBookId,
            current.AccountingPeriodId,
            current.AccountingBasis,
            current.AsOfDate,
            current.SourceCheckpointId,
            current.SourceCheckpointHash);
        retained.Should().BeEquivalentTo(current, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Scenario_PreBreakEvidenceReceiptWithAlteredReceiptHash_IntegrityFailurePrecedesRecoveryGuidance()
    {
        var original = NewLegacyReceipt();
        var tampered = original with { HasOpenBreaks = true };
        await using var fixtureFile = await TemporarySnapshot.CreateAsync(SerializeLegacySnapshot(tampered));
        var store = new FileReportingReconciliationEvidenceStore(fixtureFile.Path);

        Func<Task> act = async () => await store.GetExactAsync(
            tampered.TenantId,
            tampered.OrganizationId,
            tampered.CompanyId,
            tampered.FundId,
            tampered.LedgerBookId,
            tampered.AccountingPeriodId,
            tampered.AccountingBasis,
            tampered.AsOfDate,
            tampered.SourceCheckpointId,
            tampered.SourceCheckpointHash);

        (await act.Should().ThrowAsync<ReportingArtifactCatalogIntegrityException>()).Which.Message
            .Should().Contain("invalid legacy receipt");
    }

    [Fact]
    public async Task Scenario_PreBreakEvidenceSnapshot_CertificationReturnsStructuredRecloseBlocker()
    {
        var receipt = NewLegacyReceipt();
        await using var fixtureFile = await TemporarySnapshot.CreateAsync(SerializeLegacySnapshot(receipt));
        var authoritativeSource = new LegacyAuthoritativeSource(receipt);
        var certification = new ReportingRunCertificationService(
            authoritativeSource,
            new ReportingReconciliationEvidenceSource(
                new FileReportingReconciliationEvidenceStore(fixtureFile.Path)));

        Func<Task> certify = async () => await certification.CertifyAsync(
            Template(receipt.CompanyId),
            Readiness(receipt),
            Access(receipt));

        var exception = (await certify.Should().ThrowAsync<ReportingRunReadinessBlockedException>()).Which;
        exception.Readiness.Status.Should().Be(ReportingRunReadinessStatusDto.Blocked);
        exception.Readiness.CanGenerateDraft.Should().BeFalse();
        exception.Readiness.CanGenerateFinal.Should().BeFalse();
        exception.Readiness.Checks.Should().ContainSingle(check =>
            check.CheckId == "exact-reconciliation-evidence"
            && check.Status == ReportingRunReadinessStatusDto.Blocked
            && check.BlocksDraft
            && check.BlocksFinal
            && check.EvidenceReferences.Contains("reconciliation-evidence:migration-required"));
        exception.Readiness.BlockingReasons.Should().ContainSingle(reason =>
            reason.Contains("Re-run reconciliation and close", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scenario_CorruptedPreBreakEvidenceSnapshot_CertificationFailsAsIntegrityError()
    {
        var corrupted = NewLegacyReceipt() with { HasOpenBreaks = true };
        await using var fixtureFile = await TemporarySnapshot.CreateAsync(SerializeLegacySnapshot(corrupted));
        var certification = new ReportingRunCertificationService(
            new LegacyAuthoritativeSource(corrupted),
            new ReportingReconciliationEvidenceSource(
                new FileReportingReconciliationEvidenceStore(fixtureFile.Path)));

        Func<Task> certify = async () => await certification.CertifyAsync(
            Template(corrupted.CompanyId),
            Readiness(corrupted),
            Access(corrupted));

        (await certify.Should().ThrowAsync<ReportingArtifactCatalogIntegrityException>()).Which.Message
            .Should().Contain("invalid legacy receipt");
    }

    [Fact]
    public async Task Scenario_CurrentBreakEvidenceSchema_ReceiptRoundTripsThroughFileStore()
    {
        var receipt = NewCurrentReceipt();
        await using var fixtureFile = await TemporarySnapshot.CreateAsync();
        var store = new FileReportingReconciliationEvidenceStore(fixtureFile.Path);

        var alreadyExisted = await store.RetainAsync(receipt);
        var restarted = new FileReportingReconciliationEvidenceStore(fixtureFile.Path);
        var retained = await restarted.GetExactAsync(
            receipt.TenantId,
            receipt.OrganizationId,
            receipt.CompanyId,
            receipt.FundId,
            receipt.LedgerBookId,
            receipt.AccountingPeriodId,
            receipt.AccountingBasis,
            receipt.AsOfDate,
            receipt.SourceCheckpointId,
            receipt.SourceCheckpointHash);

        alreadyExisted.Should().BeFalse();
        retained.Should().BeEquivalentTo(receipt, options => options.WithStrictOrdering());
        retained!.CloseWorkflowCompletion.Should().BeEquivalentTo(receipt.CloseWorkflowCompletion);
        (await File.ReadAllTextAsync(fixtureFile.Path)).Should()
            .Contain("meridian.reporting.reconciliation-evidence.v2")
            .And.Contain("breakEvidence")
            .And.Contain("closeWorkflowCompletion");
    }

    private static string SerializeLegacySnapshot(LegacyReceipt receipt)
    {
        var receipts = new[] { receipt };
        var contentHash = ComputeSha256(JsonSerializer.Serialize(receipts, LegacyJsonOptions));
        return JsonSerializer.Serialize(
            new LegacySnapshot(LegacySchemaVersion, receipts, contentHash),
            LegacyJsonOptions);
    }

    private static LegacyReceipt NewLegacyReceipt()
    {
        const string sourceHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string completionHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string tenantId = "tenant-v1";
        const string organizationId = "organization-v1";
        const string companyId = "company-v1";
        const string fundId = "fund-v1";
        const string ledgerBookId = "2f0a909f-0d18-4b0a-8070-a239451de115";
        const string accountingPeriodId = "period-v1";
        const string accountingBasis = "Gaap";
        const string sourceCheckpointId = "source-v1";
        const string completionCheckpointId = "completion-v1";
        var reconciledAtUtc = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var asOfDate = new DateOnly(2026, 6, 30);
        var evidenceWithoutReceipt = ImmutableArray.Create(
            "source-evidence:v1",
            $"reconciliation-completion:{completionCheckpointId}:{completionHash}");
        var reconciliationHash = ComputeLegacyReceiptHash(
            tenantId,
            organizationId,
            companyId,
            fundId,
            ledgerBookId,
            accountingPeriodId,
            accountingBasis,
            asOfDate,
            sourceCheckpointId,
            sourceHash,
            completionCheckpointId,
            completionHash,
            reconciledAtUtc,
            hasOpenBreaks: false,
            evidenceWithoutReceipt);
        var reconciliationCheckpointId = $"report-reconciliation-{reconciliationHash[..32]}";
        return new LegacyReceipt(
            tenantId,
            organizationId,
            companyId,
            fundId,
            ledgerBookId,
            accountingPeriodId,
            accountingBasis,
            asOfDate,
            sourceCheckpointId,
            sourceHash,
            reconciliationCheckpointId,
            reconciliationHash,
            reconciledAtUtc,
            HasOpenBreaks: false,
            EvidenceIds: evidenceWithoutReceipt.Add($"reconciliation-checkpoint:{reconciliationCheckpointId}:{reconciliationHash}"),
            CompletionCheckpointId: completionCheckpointId,
            CompletionCheckpointHash: completionHash);
    }

    private static ReportingReconciliationEvidenceReceipt NewCurrentReceipt()
    {
        const string ledgerBookId = "2f0a909f-0d18-4b0a-8070-a239451de115";
        const string accountingPeriodId = "94a253d0-a892-40fd-a419-15051be09125";
        var completedAt = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var source = new ReportingAuthoritativeSourceCheckpoint(
            "ledger-journal",
            "ledger-source-v2",
            "tenant-v2",
            "organization-v2",
            "company-v2",
            "fund-v2",
            ledgerBookId,
            accountingPeriodId,
            "Gaap",
            new DateOnly(2026, 6, 30),
            completedAt,
            42,
            2,
            4,
            "ledger-checkpoint-v2",
            new string('a', 64),
            completedAt,
            ImmutableArray.Create("ledger-sequence:v2:42"));
        var completion = new ReportingReconciliationCompletionEvidence(
            "completion-v2",
            new string('b', 64),
            completedAt,
            HasOpenBreaks: false,
            ImmutableArray.Create("period-close:v2:hard-closed"),
            ImmutableArray<ReportingReconciliationBreakEvidence>.Empty,
            new ReportingCloseWorkflowCompletionEvidence(
                "0acdf53a-6311-4448-9691-d10a1676c089",
                WorkflowVersion: 7,
                "ea4be56c-a534-458b-bb32-c92fb55e2d03",
                ledgerBookId,
                accountingPeriodId,
                "approval-v2",
                new string('c', 64),
                new string('d', 64),
                "close-package-v2",
                new string('e', 64),
                "b93d4291-ea1e-4cb5-865a-4f039a92f8ca",
                new string('f', 64)));
        return ReportingReconciliationEvidenceValidation.CreateReceipt(source, completion);
    }

    private static ReportingTemplateMetadata Template(string companyId) => new(
        "legacy-recovery-report",
        ReportingTemplateFamily.CustomReport,
        "Legacy recovery report",
        "1.0.0",
        ["summary"],
        ImmutableDictionary<string, string>.Empty,
        ReportWriterGrids: [],
        AccessPolicy: new ReportAccessPolicyDto(
            ReportAccessModeDto.CompanyWide,
            CompanyId: companyId));

    private static ReportingRunReadinessDto Readiness(LegacyReceipt receipt) => new(
        "legacy-recovery-evaluation",
        receipt.ReconciledAtUtc,
        new VersionedReportTemplateIdDto("legacy-recovery-report", 1),
        new ReportingRunParametersDto(
            new ReportingRunScopeDto(receipt.FundId),
            receipt.AccountingPeriodId,
            receipt.AsOfDate,
            new ReportingLedgerBookSelectionDto(LedgerBookCode: receipt.LedgerBookId),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            ReportingFinalityDto.Final,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: true),
        ReportingRunReadinessStatusDto.Ready,
        CanGenerateDraft: true,
        CanGenerateFinal: true,
        [new ReportingRunReadinessCheckDto(
            "source",
            "Source",
            ReportingRunReadinessStatusDto.Ready,
            "The authoritative source is available.",
            0,
            BlocksDraft: false,
            BlocksFinal: false)],
        [],
        new string('a', 64));

    private static ReportAccessQueryContext Access(LegacyReceipt receipt) => new(
        "legacy-recovery-operator",
        ["report-reviewers"],
        receipt.CompanyId,
        TenantId: receipt.TenantId,
        RequireBoundScope: true);

    private static string ComputeLegacyReceiptHash(
        string tenantId,
        string organizationId,
        string? companyId,
        string fundId,
        string ledgerBookId,
        string accountingPeriodId,
        string accountingBasis,
        DateOnly asOfDate,
        string sourceCheckpointId,
        string sourceCheckpointHash,
        string completionCheckpointId,
        string completionCheckpointHash,
        DateTimeOffset reconciledAtUtc,
        bool hasOpenBreaks,
        ImmutableArray<string> evidenceIds)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("tenantId", tenantId);
            writer.WriteString("organizationId", organizationId);
            writer.WriteString("companyId", companyId);
            writer.WriteString("fundId", fundId);
            writer.WriteString("ledgerBookId", ledgerBookId);
            writer.WriteString("accountingPeriodId", accountingPeriodId);
            writer.WriteString("accountingBasis", accountingBasis);
            writer.WriteString("asOfDate", asOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("sourceCheckpointId", sourceCheckpointId);
            writer.WriteString("sourceCheckpointHash", sourceCheckpointHash);
            writer.WriteString("completionCheckpointId", completionCheckpointId);
            writer.WriteString("completionCheckpointHash", completionCheckpointHash);
            writer.WriteString("reconciledAtUtc", reconciledAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteBoolean("hasOpenBreaks", hasOpenBreaks);
            writer.WriteStartArray("evidenceIds");
            foreach (var evidence in evidenceIds.OrderBy(static item => item, StringComparer.Ordinal))
            {
                writer.WriteStringValue(evidence);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record LegacySnapshot(
        string SchemaVersion,
        IReadOnlyList<LegacyReceipt> Receipts,
        string ContentHashSha256);

    private sealed record LegacyReceipt(
        string TenantId,
        string OrganizationId,
        string? CompanyId,
        string FundId,
        string LedgerBookId,
        string AccountingPeriodId,
        string AccountingBasis,
        DateOnly AsOfDate,
        string SourceCheckpointId,
        string SourceCheckpointHash,
        string ReconciliationCheckpointId,
        string ReconciliationCheckpointHash,
        DateTimeOffset ReconciledAtUtc,
        bool HasOpenBreaks,
        ImmutableArray<string> EvidenceIds,
        string? CompletionCheckpointId = null,
        string? CompletionCheckpointHash = null);

    private sealed class LegacyAuthoritativeSource(LegacyReceipt receipt) : IReportingAuthoritativeSource
    {
        public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cutoff = new DateTimeOffset(
                receipt.AsOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                TimeSpan.Zero);
            var checkpoint = new ReportingAuthoritativeSourceCheckpoint(
                "legacy-file-ledger",
                "legacy-file-source",
                receipt.TenantId,
                receipt.OrganizationId,
                receipt.CompanyId,
                receipt.FundId,
                receipt.LedgerBookId,
                receipt.AccountingPeriodId,
                receipt.AccountingBasis,
                receipt.AsOfDate,
                cutoff,
                HighestGlobalSequence: 1,
                JournalEntryCount: 0,
                LedgerLineCount: 0,
                CheckpointId: receipt.SourceCheckpointId,
                CheckpointHash: receipt.SourceCheckpointHash,
                CapturedAtUtc: receipt.ReconciledAtUtc,
                EvidenceIds: [$"reporting-source-checkpoint:{receipt.SourceCheckpointId}:{receipt.SourceCheckpointHash}"]);
            return ValueTask.FromResult(new ReportingAuthoritativeSourceCapture(checkpoint, []));
        }
    }

    private sealed class TemporarySnapshot : IAsyncDisposable
    {
        private TemporarySnapshot(string path) => Path = path;

        public string Path { get; }

        public static async ValueTask<TemporarySnapshot> CreateAsync(string? contents = null)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"meridian-reconciliation-evidence-{Guid.NewGuid():N}.json");
            if (contents is not null)
            {
                await File.WriteAllTextAsync(path, contents);
            }

            return new TemporarySnapshot(path);
        }

        public ValueTask DisposeAsync()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }

            foreach (var backup in Directory.GetFiles(
                         System.IO.Path.GetDirectoryName(Path)!,
                         $"{System.IO.Path.GetFileName(Path)}.legacy-v1.*.json"))
            {
                File.Delete(backup);
            }

            return ValueTask.CompletedTask;
        }
    }
}
