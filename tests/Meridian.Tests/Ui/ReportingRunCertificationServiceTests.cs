using System.Collections.Immutable;
using System.IO.Compression;
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

public sealed class ReportingRunCertificationServiceTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CertifyAsync_SameAuthoritativeCheckpointProducesStableSnapshotAndNormalizedScope()
    {
        var source = new StubAuthoritativeSource(Rows());
        var sut = new ReportingRunCertificationService(source, new StubReconciliationSource());

        var first = await sut.CertifyAsync(
            Template(),
            Readiness("evaluation-one", CapturedAt.AddHours(1)),
            Access());
        var second = await sut.CertifyAsync(
            Template(),
            Readiness("evaluation-two", CapturedAt.AddHours(2)),
            Access());

        second.Snapshot.SnapshotHash.Should().Be(first.Snapshot.SnapshotHash);
        second.Snapshot.SnapshotId.Should().Be(first.Snapshot.SnapshotId);
        second.Snapshot.SourceCheckpointId.Should().Be(first.AuthoritativeSource.CheckpointId);
        second.Snapshot.ReconciliationCheckpointId.Should().NotBe(second.Snapshot.SourceCheckpointId);
        second.DatasetRows.Should().Equal(first.DatasetRows);
        first.OperationalScope.TenantId.Should().Be("tenant-a");
        first.OperationalScope.CompanyId.Should().Be("company-a");
        first.Readiness.ResolvedParameters.LedgerBook.LedgerBookId.Should().Be(StubAuthoritativeSource.BookId);
        first.Readiness.ResolvedParameters.PeriodId.Should().Be(StubAuthoritativeSource.PeriodId.ToString("D"));
    }

    [Fact]
    public async Task CertifyAsync_MissingRetainedReconciliationEvidenceFailsClosed()
    {
        var sut = new ReportingRunCertificationService(
            new StubAuthoritativeSource(Rows()),
            reconciliationEvidenceSource: null);

        Func<Task> certify = async () => await sut.CertifyAsync(
            Template(),
            Readiness("evaluation-one", CapturedAt),
            Access());

        await certify.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*reconciliation*configured*");
    }

    [Fact]
    public async Task CertifyAsync_HealthyStoreMissingExactReceiptReturnsStructuredReadinessBlocker()
    {
        var sut = new ReportingRunCertificationService(
            new StubAuthoritativeSource(Rows()),
            new ReportingReconciliationEvidenceSource(new EmptyReconciliationStore()));

        Func<Task> certify = async () => await sut.CertifyAsync(
            Template(),
            Readiness("evaluation-one", CapturedAt),
            Access());

        var exception = (await certify.Should().ThrowAsync<ReportingRunReadinessBlockedException>()).Which;
        exception.Readiness.Status.Should().Be(ReportingRunReadinessStatusDto.Blocked);
        exception.Readiness.CanGenerateDraft.Should().BeFalse();
        exception.Readiness.CanGenerateFinal.Should().BeFalse();
        exception.Readiness.Checks.Should().ContainSingle(check =>
            check.CheckId == "exact-reconciliation-evidence"
            && check.Status == ReportingRunReadinessStatusDto.Blocked
            && check.BlocksDraft
            && check.BlocksFinal
            && check.EvidenceReferences.Contains(
                "reporting-source-checkpoint:ledger-checkpoint-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb:" + new string('b', 64)));
        exception.Readiness.BlockingReasons.Should().ContainSingle(reason =>
            reason.Contains("No retained reconciliation/close checkpoint", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("entity-scope-enum")]
    [InlineData("accounting-basis-enum")]
    [InlineData("consolidation-enum")]
    [InlineData("output-format-enum")]
    [InlineData("finality-enum")]
    [InlineData("scope-consolidation-mismatch")]
    [InlineData("missing-entity")]
    [InlineData("missing-ledger-book")]
    [InlineData("missing-period")]
    [InlineData("missing-currency")]
    [InlineData("missing-final-evidence-appendix")]
    public async Task CertifyAsync_MalformedParameterMatrixFailsBeforeAuthoritativeCapture(string scenario)
    {
        var source = new StubAuthoritativeSource(Rows());
        var parameters = Parameters(ReportingOutputFormatDto.Pdf);
        parameters = scenario switch
        {
            "entity-scope-enum" => parameters with
            {
                Scope = parameters.Scope with { EntityScopeKind = (ReportingEntityScopeKindDto)999 }
            },
            "accounting-basis-enum" => parameters with
            {
                AccountingBasis = (ReportingAccountingBasisDto)999
            },
            "consolidation-enum" => parameters with
            {
                ConsolidationLevel = (ReportingConsolidationLevelDto)999
            },
            "output-format-enum" => parameters with
            {
                OutputFormat = (ReportingOutputFormatDto)999
            },
            "finality-enum" => parameters with
            {
                Finality = (ReportingFinalityDto)999
            },
            "scope-consolidation-mismatch" => parameters with
            {
                Scope = parameters.Scope with
                {
                    EntityScopeKind = ReportingEntityScopeKindDto.Entity,
                    EntityId = "entity-a"
                },
                ConsolidationLevel = ReportingConsolidationLevelDto.Fund
            },
            "missing-entity" => parameters with
            {
                Scope = parameters.Scope with
                {
                    EntityScopeKind = ReportingEntityScopeKindDto.Entity,
                    EntityId = null
                },
                ConsolidationLevel = ReportingConsolidationLevelDto.Entity
            },
            "missing-ledger-book" => parameters with
            {
                LedgerBook = new ReportingLedgerBookSelectionDto()
            },
            "missing-period" => parameters with { PeriodId = " " },
            "missing-currency" => parameters with { PresentationCurrency = " " },
            "missing-final-evidence-appendix" => parameters with { IncludeEvidenceAppendix = false },
            _ => throw new InvalidOperationException($"Unknown scenario '{scenario}'.")
        };
        var readiness = Readiness("evaluation-one", CapturedAt) with
        {
            ResolvedParameters = parameters
        };
        var sut = new ReportingRunCertificationService(source, new StubReconciliationSource());

        Func<Task> certify = async () => await sut.CertifyAsync(Template(), readiness, Access());

        await certify.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>();
        source.CaptureCount.Should().Be(0);
    }

    [Fact]
    public void Certify_CallerSuppliedRowsCompatibilityPathFailsClosed()
    {
#pragma warning disable CS0618
        var act = () => new ReportingRunCertificationService().Certify(
            Template(),
            Readiness("evaluation-one", CapturedAt),
            Rows(),
            "custom-request-dataset",
            Access());
#pragma warning restore CS0618

        act.Should().Throw<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*durable authoritative source*");
    }

    [Fact]
    public async Task EvaluateManifest_CrossTenantAdministratorIsDeniedBeforeOverride()
    {
        var certified = await new ReportingRunCertificationService(
                new StubAuthoritativeSource(Rows()),
                new StubReconciliationSource())
            .CertifyAsync(Template(), Readiness("evaluation-one", CapturedAt), Access());
        var manifest = BuildManifest("run-access", Template(), certified);

        var result = ReportAccessPolicyEvaluator.Evaluate(
            manifest,
            new ReportAccessQueryContext(
                "admin-b",
                CompanyId: "company-b",
                HasGlobalOverride: true,
                TenantId: "tenant-b",
                RequireBoundScope: true));

        result.IsAccessible.Should().BeFalse();
        result.Reason.Should().Contain("another tenant");
    }

    [Fact]
    public async Task ProduceAsync_CsvPreservesCertifiedBusinessValuesAndNeutralizesSpreadsheetFormulas()
    {
        var production = await ProduceAsync("run-csv", ReportingOutputFormatDto.Csv);
        var csv = Encoding.UTF8.GetString(production.Artifacts
            .Single(artifact => artifact.FileName == "run-csv.csv")
            .Content.Span);

        csv.Should().Contain("account");
        csv.Should().Contain("123.45");
        csv.Should().Contain("-23.45");
        csv.Should().Contain("'=2+3");
        csv.Should().Contain("-12.5", "valid negative numeric values must remain numeric");
        csv.Should().NotContain(",=2+3");
    }

    [Fact]
    public async Task ProduceAsync_XlsxUsesInlineTextAndPreservesExactCertifiedValuesWithoutFormulaCells()
    {
        var production = await ProduceAsync("run-xlsx", ReportingOutputFormatDto.Xlsx);
        var artifact = production.Artifacts.Single(item => item.FileName == "run-xlsx.xlsx");
        using var stream = new MemoryStream(artifact.Content.ToArray());
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(
            archive.GetEntry("xl/worksheets/sheet1.xml")!.Open(),
            Encoding.UTF8);
        var sheet = await reader.ReadToEndAsync();

        sheet.Should().Contain("123.45");
        sheet.Should().Contain("-23.45");
        sheet.Should().Contain("=2+3");
        sheet.Should().Contain("t=\"inlineStr\"");
        sheet.Should().NotContain("<f>");
    }

    [Fact]
    public async Task ProduceAsync_PdfIncludesCertifiedAccountTotalsAndRowHash()
    {
        var production = await ProduceAsync("run-pdf", ReportingOutputFormatDto.Pdf);
        var pdf = Encoding.ASCII.GetString(production.Artifacts
            .Single(artifact => artifact.FileName == "run-pdf.pdf")
            .Content.Span);

        pdf.Should().StartWith("%PDF-1.4");
        pdf.Should().Contain("Certified ledger rows: 2");
        pdf.Should().Contain("Cash: 123.45 / 0 / 123.45");
        pdf.Should().Contain("Payable: 0 / 23.45 / -23.45");
        pdf.Should().Contain("Certified row hash:");
    }

    [Fact]
    public async Task FileRunStore_RestartRejectsCertifiedRowMutationEvenWhenStorageChecksumsAreRehashed()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Meridian.Tests",
            Guid.NewGuid().ToString("N"),
            "reporting-row-binding");
        Directory.CreateDirectory(root);
        try
        {
            var template = Template(reportWriterGrid: false);
            var certified = await new ReportingRunCertificationService(
                    new StubAuthoritativeSource(Rows()),
                    new StubReconciliationSource())
                .CertifyAsync(
                    template,
                    Readiness("evaluation-row-binding", CapturedAt),
                    Access());
            var manifest = BuildManifest("run-row-binding", template, certified);
            var store = new FileReportingRunStore(
                new ReportingRunStoreOptions(root),
                NullLogger<FileReportingRunStore>.Instance);
            await store.SaveAsync(manifest, []);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            var snapshotPath = Path.Combine(root, "reporting-runs.json");
            var envelope = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))!.AsObject();
            var retainedRun = envelope["runs"]!.AsArray()[0]!.AsObject();
            var retainedManifest = retainedRun["manifest"]!.AsObject();
            retainedManifest["certifiedDatasetRows"]!.AsArray()[0]!.AsObject()["netAmount"] = "999.99";

            var rehydratedManifest = retainedManifest.Deserialize<ReportingOutputManifest>(options)!;
            retainedRun["certifiedDatasetHashSha256"] =
                FileReportingRunStore.ComputeCertifiedRowsHash(rehydratedManifest.CertifiedDatasetRows);
            retainedRun["manifestHashSha256"] = ComputeSha256(
                JsonSerializer.Serialize(rehydratedManifest, options));
            var rehydratedRuns = envelope["runs"]!
                .Deserialize<IReadOnlyList<ReportingRunSnapshot>>(options)!;
            envelope["payloadHashSha256"] = ComputeSha256(
                JsonSerializer.Serialize(rehydratedRuns, options));
            await File.WriteAllTextAsync(snapshotPath, envelope.ToJsonString(options));

            var restarted = new FileReportingRunStore(
                new ReportingRunStoreOptions(root),
                NullLogger<FileReportingRunStore>.Instance);
            var load = () => restarted.ListRuns();

            var exception = load.Should().Throw<ReportingStateCorruptionException>().Which;
            exception.StatePath.Should().Be(snapshotPath);
            exception.InnerException!.Message.Should().Contain("snapshot hash");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ReportingGovernedArtifactProduction> ProduceAsync(
        string runId,
        ReportingOutputFormatDto outputFormat)
    {
        var template = Template(reportWriterGrid: false);
        var certified = await new ReportingRunCertificationService(
                new StubAuthoritativeSource(Rows()),
                new StubReconciliationSource())
            .CertifyAsync(
                template,
                Readiness("evaluation-artifact", CapturedAt, outputFormat),
                Access());
        var manifest = BuildManifest(runId, template, certified);
        return await new DeterministicReportingCertifiedArtifactProducer().ProduceAsync(manifest);
    }

    private static ReportingOutputManifest BuildManifest(
        string runId,
        ReportingTemplateMetadata template,
        CertifiedReportingRunContext certified)
    {
        var declarations = ReportingArtifactDeclaration.Build(
            runId,
            template,
            certified.Readiness.ResolvedParameters,
            includeCertifiedSourceSchedule: true);
        return new ReportingOutputManifest(
            runId,
            template.TemplateId,
            certified.Readiness.ResolvedParameters.AsOfDate,
            ReportingRunStatus.Draft,
            ImmutableArray<ReportingSectionManifest>.Empty,
            declarations.Select(static declaration => declaration.ArtifactId).ToImmutableArray(),
            AttemptCount: 1,
            Trigger: ReportingRunTrigger.AdHoc,
            RunSeriesId: runId,
            ResolvedTemplate: certified.Readiness.ResolvedTemplate,
            ResolvedParameters: certified.Readiness.ResolvedParameters,
            Readiness: certified.Readiness,
            OperationalScope: certified.OperationalScope,
            ImmutableAccessScope: certified.AccessScope,
            CertifiedSnapshot: certified.Snapshot,
            AuthoritativeSource: certified.AuthoritativeSource,
            CertifiedDatasetRows: certified.DatasetRows);
    }

    private static ReportingTemplateMetadata Template(bool reportWriterGrid = true) => new(
        "test-report",
        ReportingTemplateFamily.CustomReport,
        "Test report",
        "2.0.0",
        ["detail"],
        ImmutableDictionary<string, string>.Empty,
        ReportWriterGrids: reportWriterGrid
            ?
            [
                new ReportWriterGridDefinitionDto(
                    "detail",
                    "Detail",
                    ReportWriterGridKindDto.Detail,
                    RowFields: ["account"],
                    Metrics:
                    [
                        new ReportWriterMetricDefinitionDto(
                            "amount",
                            "amount",
                            ReportWriterAggregateFunctionDto.Sum)
                    ])
            ]
            : [],
        AccessPolicy: new ReportAccessPolicyDto(
            ReportAccessModeDto.CompanyWide,
            CompanyId: "company-a"));

    private static ReportingRunReadinessDto Readiness(
        string evaluationId,
        DateTimeOffset evaluatedAt,
        ReportingOutputFormatDto outputFormat = ReportingOutputFormatDto.Pdf) =>
        new(
            evaluationId,
            evaluatedAt,
            new VersionedReportTemplateIdDto("test-report", 2),
            Parameters(outputFormat),
            ReportingRunReadinessStatusDto.Ready,
            CanGenerateDraft: true,
            CanGenerateFinal: true,
            Checks:
            [
                new ReportingRunReadinessCheckDto(
                    "source",
                    "Source",
                    ReportingRunReadinessStatusDto.Ready,
                    "Source is ready.",
                    0,
                    BlocksDraft: false,
                    BlocksFinal: false,
                    EvidenceReferences: ["source:checkpoint-a"])
            ],
            BlockingReasons: [],
            EvidenceHash: new string('a', 64));

    private static ReportingRunParametersDto Parameters(ReportingOutputFormatDto outputFormat) =>
        new(
            new ReportingRunScopeDto("fund-a"),
            "2026-06",
            new DateOnly(2026, 6, 30),
            new ReportingLedgerBookSelectionDto(StubAuthoritativeSource.BookId),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            outputFormat,
            ReportingFinalityDto.Final,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: true);

    private static ReportAccessQueryContext Access() => new(
        "operator-a",
        ["report-reviewers"],
        "company-a",
        TenantId: "tenant-a",
        RequireBoundScope: true);

    private static ImmutableArray<IReadOnlyDictionary<string, string>> Rows() =>
    [
        Row(
            ("account", "Cash"),
            ("debit", "123.45"),
            ("credit", "0"),
            ("netAmount", "123.45"),
            ("formulaText", "=2+3"),
            ("negativeAmount", "-12.5"),
            ("entryId", "11111111-1111-1111-1111-111111111111")),
        Row(
            ("account", "Payable"),
            ("debit", "0"),
            ("credit", "23.45"),
            ("netAmount", "-23.45"),
            ("formulaText", "+SUM(A1:A2)"),
            ("negativeAmount", "-1"),
            ("entryId", "22222222-2222-2222-2222-222222222222"))
    ];

    private static IReadOnlyDictionary<string, string> Row(params (string Key, string Value)[] values) =>
        values.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class StubAuthoritativeSource : IReportingAuthoritativeSource
    {
        public static readonly Guid BookId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public static readonly Guid PeriodId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        private readonly ImmutableArray<IReadOnlyDictionary<string, string>> _rows;

        public StubAuthoritativeSource(ImmutableArray<IReadOnlyDictionary<string, string>> rows)
        {
            _rows = rows;
        }

        public int CaptureCount { get; private set; }

        public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureCount++;
            var hash = new string('b', 64);
            var checkpointId = "ledger-checkpoint-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            var checkpoint = new ReportingAuthoritativeSourceCheckpoint(
                "durable-ledger-journal",
                $"ledger:{BookId:D}:{PeriodId:D}",
                "tenant-a",
                "55555555-5555-5555-5555-555555555555",
                "company-a",
                "fund-a",
                BookId.ToString("D"),
                PeriodId.ToString("D"),
                "Gaap",
                parameters.AsOfDate,
                new DateTimeOffset(
                    parameters.AsOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                    TimeSpan.Zero),
                HighestGlobalSequence: 42,
                JournalEntryCount: 2,
                LedgerLineCount: _rows.Length,
                checkpointId,
                hash,
                CapturedAt,
                [$"reporting-source-checkpoint:{checkpointId}:{hash}", "ledger-sequence:42"]);
            return ValueTask.FromResult(new ReportingAuthoritativeSourceCapture(checkpoint, _rows));
        }
    }

    private sealed class StubReconciliationSource : IReportingReconciliationEvidenceSource
    {
        public ValueTask<ReportingReconciliationEvidenceReceipt> ResolveAsync(
            ReportingRunParametersDto parameters,
            ReportingAuthoritativeSourceCheckpoint source,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ReportingReconciliationEvidenceValidation.CreateReceipt(
                source,
                new ReportingReconciliationCompletionEvidence(
                    "hard-close-44444444444444444444444444444444-v7",
                    new string('c', 64),
                    CapturedAt,
                    HasOpenBreaks: false,
                    ["ledger-period:44444444-4444-4444-4444-444444444444:hard-closed"])));
        }
    }

    private sealed class EmptyReconciliationStore : IReportingReconciliationEvidenceStore
    {
        public ValueTask<ReportingReconciliationEvidenceReceipt?> GetExactAsync(
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
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ReportingReconciliationEvidenceReceipt?>(null);
    }
}
