using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

/// <summary>
/// Guards the operator review scenario where multiple workstation contributors assemble one evidence packet before paper operation.
/// </summary>
public sealed class EvidenceWorkflowFabricTests
{
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task EvidenceGraphService_DuringPaperReadinessReview_DeduplicatesNodesAndFlagsInvalidEdges()
    {
        var subject = Subject(EvidenceSubjectResolver.PaperReadinessKind, "current");
        var ready = Node(subject, "ready", "readiness-gate", EvidenceStatusDto.Ready);
        var stale = Node(subject, "stale", "paper-replay", EvidenceStatusDto.Stale, stale: true);
        var review = Node(subject, "review", "provider-trust", EvidenceStatusDto.ReviewRequired, workItemIds: ["provider-trust:sample-review"]);
        var duplicateReady = ready with { Summary = "Duplicate contributor result should not replace the first node." };
        var contributors = new IEvidenceContributor[]
        {
            new StubContributor("readiness", static _ => true, _ => new EvidenceContribution(
                Nodes: [ready, stale],
                Edges:
                [
                    new EvidenceEdgeDto("ready", "stale", "requires", "Replay evidence supports readiness."),
                    new EvidenceEdgeDto("ready", "missing", "requires", "Broken edge should be rejected.")
                ],
                Actions: [],
                RequiredEvidenceIds: ["ready", "stale", "missing"],
                Warnings: [])),
            new StubContributor("provider-trust", static _ => true, _ => new EvidenceContribution(
                Nodes: [duplicateReady, review],
                Edges: [new EvidenceEdgeDto("ready", "review", "requires", "Provider trust supports readiness.")],
                Actions: [],
                RequiredEvidenceIds: ["review"],
                Warnings: ["Optional DK1 sample review is pending."]))
        };
        var service = CreateGraphService(contributors);

        var packet = await service.GetPacketAsync(subject.SubjectKind, subject.SubjectId);

        packet.Should().NotBeNull();
        packet!.Nodes.Should().HaveCount(3);
        packet.Nodes.Single(node => node.EvidenceId == "ready").Summary.Should().Be(ready.Summary);
        packet.Edges.Should().OnlyContain(edge => edge.ToId != "missing");
        packet.Warnings.Should().Contain(warning => warning.Contains("references a missing node", StringComparison.OrdinalIgnoreCase));
        packet.Warnings.Should().Contain("Optional DK1 sample review is pending.");
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Blocked);
        packet.Completeness.Score.Should().Be(25);
        packet.Completeness.MissingIds.Should().Contain("missing");
        packet.Completeness.StaleIds.Should().Contain("stale");
        packet.Completeness.BlockingWorkItemIds.Should().Contain("provider-trust:sample-review");
        packet.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "invalid-edge" &&
            issue.EvidenceId == "ready");
        packet.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "missing-required-evidence" &&
            issue.EvidenceId == "missing" &&
            issue.Severity == EvidenceValidationSeverityDto.Critical);
        packet.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "stale-required-evidence" &&
            issue.EvidenceId == "stale");
        packet.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "review-required-evidence" &&
            issue.EvidenceId == "review" &&
            issue.RelatedWorkItemId == "provider-trust:sample-review");
    }

    [Fact]
    public void EvidencePacketValidationService_DuringGovernedReportReview_ExplainsReadyMissingStaleAndReviewStates()
    {
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "current");
        var ready = Node(subject, "ready", "analysis-export", EvidenceStatusDto.Ready);
        var stale = Node(subject, "stale", "report-pack", EvidenceStatusDto.Stale, stale: true);
        var review = Node(subject, "review", "portfolio-context", EvidenceStatusDto.ReviewRequired, workItemIds: ["report-pack-lineage:current"]);
        var service = new EvidencePacketValidationService();

        var readyResult = service.Validate(
            [ready],
            [],
            new HashSet<string>(["ready"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        readyResult.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);
        readyResult.Completeness.ValidationIssues.Should().BeEmpty();

        var missingResult = service.Validate(
            [ready],
            [],
            new HashSet<string>(["ready", "missing"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        missingResult.Completeness.Status.Should().Be(EvidenceStatusDto.Blocked);
        missingResult.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "missing-required-evidence" &&
            issue.EvidenceId == "missing" &&
            issue.Severity == EvidenceValidationSeverityDto.Critical);

        var staleResult = service.Validate(
            [stale],
            [],
            new HashSet<string>(["stale"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        staleResult.Completeness.Status.Should().Be(EvidenceStatusDto.Stale);
        staleResult.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "stale-required-evidence" &&
            issue.EvidenceId == "stale" &&
            issue.EvidenceKind == "report-pack");

        var reviewResult = service.Validate(
            [review],
            [],
            new HashSet<string>(["review"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        reviewResult.Completeness.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        reviewResult.Completeness.BlockingWorkItemIds.Should().Contain("report-pack-lineage:current");
        reviewResult.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "review-required-evidence" &&
            issue.RelatedWorkItemId == "report-pack-lineage:current");
    }

    [Fact]
    public void EvidencePacketValidationService_DuringLedgerReview_ExplainsLedgerArtifactIntegrityIssues()
    {
        var subject = Subject(EvidenceSubjectResolver.StrategyRunKind, "run-ledger-integrity");
        var ledger = Node(
            subject,
            "ledger",
            "run-ledger",
            EvidenceStatusDto.Ready,
            artifacts:
            [
                new EvidenceArtifactRefDto(
                    "ledger:journal",
                    "ledger-journal",
                    Path: null,
                    Route: null,
                    GeneratedAt: DateTimeOffset.UtcNow,
                    Hash: null,
                    Retained: false)
            ]);
        var service = new EvidencePacketValidationService();

        var result = service.Validate(
            [ledger],
            [],
            new HashSet<string>(["ledger"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        result.Completeness.Status.Should().Be(EvidenceStatusDto.Blocked);
        result.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ledger-missing-trial-balance-artifact" &&
            issue.EvidenceId == "ledger" &&
            issue.Severity == EvidenceValidationSeverityDto.Critical);
        result.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ledger-artifact-not-retained" &&
            issue.EvidenceId == "ledger" &&
            issue.Severity == EvidenceValidationSeverityDto.Warning);
        result.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ledger-artifact-not-addressable" &&
            issue.EvidenceId == "ledger" &&
            issue.Severity == EvidenceValidationSeverityDto.Critical);
    }

    [Fact]
    public void EvidencePacketValidationService_DuringLedgerReview_MarksWarningOnlyRetentionIssuesForReview()
    {
        var subject = Subject(EvidenceSubjectResolver.StrategyRunKind, "run-ledger-retention");
        var generatedAt = DateTimeOffset.UtcNow;
        var ledger = Node(
            subject,
            "ledger",
            "run-ledger",
            EvidenceStatusDto.Ready,
            artifacts:
            [
                new EvidenceArtifactRefDto(
                    "ledger:journal",
                    "ledger-journal",
                    Path: "runs/run-ledger-retention/ledger-journal.json",
                    Route: null,
                    GeneratedAt: generatedAt,
                    Hash: null,
                    Retained: false),
                new EvidenceArtifactRefDto(
                    "ledger:trial-balance",
                    "ledger-trial-balance",
                    Path: "runs/run-ledger-retention/trial-balance.json",
                    Route: null,
                    GeneratedAt: generatedAt,
                    Hash: null,
                    Retained: true)
            ]);
        var service = new EvidencePacketValidationService();

        var result = service.Validate(
            [ledger],
            [],
            new HashSet<string>(["ledger"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        result.Completeness.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        result.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ledger-artifact-not-retained" &&
            issue.Severity == EvidenceValidationSeverityDto.Warning);
        result.Completeness.ValidationIssues.Should().NotContain(issue =>
            issue.Severity == EvidenceValidationSeverityDto.Critical);
    }

    [Fact]
    public void EvidencePacketValidationService_DetectsOrphansAndCanonicalSubjectLinkageWithoutFalsePositives()
    {
        var subject = Subject(EvidenceSubjectResolver.StrategyRunKind, "run-orphan-check");
        var linkedA = Node(subject, "linked-a", "run-ledger", EvidenceStatusDto.Ready);
        var linkedB = Node(subject, "linked-b", "report-pack", EvidenceStatusDto.Ready);
        var orphan = Node(subject, "orphan", "approval", EvidenceStatusDto.Ready);
        var service = new EvidencePacketValidationService();

        var result = service.Validate(
            [linkedA, linkedB, orphan],
            [new EvidenceEdgeDto("linked-a", "linked-b", "supports", "linked evidence")],
            new HashSet<string>(["linked-a", "linked-b"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: true);

        result.Completeness.OrphanEvidenceIds.Should().Contain("orphan");
        result.Completeness.OrphanEvidenceIds.Should().NotContain("linked-a");
        result.Completeness.WarningIssueCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EvidencePacketValidationService_BlocksWhenRetainedArtifactsMissCanonicalSubject()
    {
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "current");
        var node = Node(
            subject,
            "report",
            "report-pack",
            EvidenceStatusDto.Ready,
            artifacts:
            [
                new EvidenceArtifactRefDto("a1", "report-pack", "/tmp/a1.json", null, DateTimeOffset.UtcNow, null, true)
            ]);
        var service = new EvidencePacketValidationService();

        var result = service.Validate(
            [node],
            [],
            new HashSet<string>(["report"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        result.Completeness.Status.Should().Be(EvidenceStatusDto.Blocked);
        result.Completeness.BlockingIssueCount.Should().BeGreaterThan(0);
        result.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "retained-artifact-missing-canonical-subject" &&
            issue.Severity == EvidenceValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task EvidenceGraphService_DuringCancelledReview_PreservesCancellation()
    {
        var service = CreateGraphService([new StubContributor("slow", static _ => true, _ => new EvidenceContribution([], [], [], [], []))]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetPacketAsync(EvidenceSubjectResolver.PaperReadinessKind, "current", cts.Token));
    }

    [Fact]
    public async Task MapEvidenceEndpoints_DuringReportPackReview_ReturnsPacketGraphValidationTemplatesAndManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-workflow", Guid.NewGuid().ToString("N"));
        await using var app = await CreateEvidenceAppAsync(root);
        var client = app.GetTestClient();

        var templatesResponse = await client.GetAsync("/api/workstation/evidence/templates");
        templatesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var templates = await templatesResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceTemplateDto>>(ServerJsonOptions);
        templates.Should().NotBeNull();
        templates!.Should().Contain(template =>
            template.WorkflowId == "portfolio-reporting-output" &&
            template.ExportSettings.ManifestOnly &&
            template.ExportSettings.SchemaVersion == 1);

        var packetResponse = await client.GetAsync("/api/workstation/evidence/subjects/report-pack/current/packet");
        packetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var packet = await packetResponse.Content.ReadFromJsonAsync<EvidencePacketDto>(ServerJsonOptions);
        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.ReportPackKind);
        packet.Nodes.Should().Contain(node => node.Kind == "analysis-export");
        packet.Warnings.Should().Contain(warning => warning.Contains("report-pack repository is not registered", StringComparison.OrdinalIgnoreCase));

        var graphResponse = await client.GetAsync("/api/workstation/evidence/subjects/report-pack/current/graph");
        graphResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var graph = await graphResponse.Content.ReadFromJsonAsync<EvidenceGraphDto>(ServerJsonOptions);
        graph!.Nodes.Should().Contain(node => node.EvidenceId == "report-pack:current:analysis-export");

        var validationResponse = await client.PostAsync("/api/workstation/evidence/subjects/report-pack/current/validate", content: null);
        validationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var completeness = await validationResponse.Content.ReadFromJsonAsync<EvidenceCompletenessDto>(ServerJsonOptions);
        completeness!.ReadyIds.Should().Contain("report-pack:current:analysis-export");

        var exportResponse = await client.PostAsJsonAsync(
            "/api/workstation/evidence/subjects/report-pack/current/export-manifest",
            new EvidencePacketExportRequest("operator", "report-pack review", IncludeWarnings: false),
            ServerJsonOptions);
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var export = await exportResponse.Content.ReadFromJsonAsync<EvidencePacketExportResponse>(ServerJsonOptions);
        export.Should().NotBeNull();
        export!.Retained.Should().BeTrue();
        export.VaultIdentity.Should().NotBeNull();
        export.VaultIdentity!.VaultId.Should().StartWith("ev-");
        export.VaultIdentity.ManifestPath.Should().Be(export.ManifestPath);
        export.VaultIdentity.ManifestRoute.Should().Be(export.ManifestRoute);
        export.VaultIdentity.ContentHashSha256.Should().HaveLength(64);
        export.VaultIdentity.StorageKind.Should().Be("file-manifest");
        export.WarningCount.Should().Be(0);
        File.Exists(Path.Combine(root, export.ManifestPath.Replace('/', Path.DirectorySeparatorChar))).Should().BeTrue();
        var indexPath = Path.Combine(root, "workstation", "evidence", "_vault", $"{export.VaultIdentity.VaultId}.json");
        File.Exists(indexPath).Should().BeTrue();
        var indexJson = await File.ReadAllTextAsync(indexPath);
        var indexedIdentity = JsonSerializer.Deserialize<EvidenceVaultIdentityDto>(indexJson, ServerJsonOptions);
        indexedIdentity.Should().BeEquivalentTo(export.VaultIdentity);

        var manifestResponse = await client.GetAsync(export.ManifestRoute);
        manifestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        manifestResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        var manifestJson = await manifestResponse.Content.ReadAsStringAsync();
        manifestJson.Should().Contain("\"manifestOnly\": true");
        manifestJson.Should().Contain("\"requestedBy\": \"operator\"");
        manifestJson.Should().Contain("\"vaultIdentity\": {");
        manifestJson.Should().Contain(export.VaultIdentity.VaultId);

        var vaultResponse = await client.GetAsync($"/workstation/evidence/vault/{export.VaultIdentity.VaultId}");
        vaultResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        vaultResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var missingVaultResponse = await client.GetAsync("/workstation/evidence/vault/ev-000000000000000000000000");
        missingVaultResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var missingVaultError = await missingVaultResponse.Content.ReadFromJsonAsync<EvidenceEndpointErrorDto>(ServerJsonOptions);
        missingVaultError!.Code.Should().Be("evidence-vault-manifest-not-found");
        missingVaultError.VaultId.Should().Be("ev-000000000000000000000000");

        var traversalResponse = await client.GetAsync("/workstation/evidence/report-pack/current/..%2Fsecret-manifest.json");
        traversalResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var traversalError = await traversalResponse.Content.ReadFromJsonAsync<EvidenceEndpointErrorDto>(ServerJsonOptions);
        traversalError!.Code.Should().Be("evidence-manifest-not-found");

        var malformedExportResponse = await client.PostAsync(
            "/api/workstation/evidence/subjects/report-pack/current/export-manifest",
            new StringContent("{", Encoding.UTF8, "application/json"));
        malformedExportResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var malformedExportError = await malformedExportResponse.Content.ReadFromJsonAsync<EvidenceEndpointErrorDto>(ServerJsonOptions);
        malformedExportError!.Code.Should().Be("invalid-evidence-export-request");
        malformedExportError.SubjectKind.Should().Be(EvidenceSubjectResolver.ReportPackKind);
    }

    [Fact]
    public async Task MapEvidenceEndpoints_DuringUnsupportedSubjectReview_ReturnsBadRequest()
    {
        await using var app = await CreateEvidenceAppAsync(Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-workflow", Guid.NewGuid().ToString("N")));
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/workstation/evidence/subjects/unknown/current/packet");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<EvidenceEndpointErrorDto>(ServerJsonOptions);
        error!.Code.Should().Be("unsupported-evidence-subject-kind");
        error.SubjectKind.Should().Be("unknown");
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestExport_SanitizesSubjectPathAndHonorsWarningPreference()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject("report-pack", "Review Jan/../2026");
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [Node(subject, "node-1", "analysis-export", EvidenceStatusDto.Ready)],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["node-1"], ["node-1"], [], [], [])
            {
                ValidationIssues =
                [
                    new EvidenceValidationIssueDto(
                        Code: "review-required-evidence",
                        Severity: EvidenceValidationSeverityDto.Warning,
                        Message: "Report-pack approval evidence requires review.",
                        EvidenceId: "node-1",
                        EvidenceKind: "analysis-export",
                        SourceSystem: "test")
                ]
            },
            Actions: [],
            Warnings: ["This warning should be excluded."]);

        var response = await store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "safe export", IncludeWarnings: false));
        var manifestPath = Path.Combine(root, response.ManifestPath.Replace('/', Path.DirectorySeparatorChar));
        var manifestJson = await File.ReadAllTextAsync(manifestPath);

        response.ManifestPath.Should().Contain("review jan-..-2026");
        response.ManifestRoute.Should().Contain("/workstation/evidence/report-pack/review%20jan-..-2026/");
        response.VaultIdentity.Should().NotBeNull();
        response.VaultIdentity!.SubjectId.Should().Be("Review Jan/../2026");
        response.WarningCount.Should().Be(0);
        var retainedManifest = await store.TryOpenManifestByVaultIdAsync(response.VaultIdentity.VaultId);
        retainedManifest.Should().NotBeNull();
        using (var reader = new StreamReader(retainedManifest!.Content))
        {
            var retainedJson = await reader.ReadToEndAsync();
            retainedJson.Should().Contain("\"subjectId\": \"Review Jan/../2026\"");
            retainedJson.Should().Contain(response.VaultIdentity.VaultId);
        }

        manifestJson.Should().Contain("\"schemaVersion\": 1");
        manifestJson.Should().Contain("\"validationIssues\": [");
        manifestJson.Should().Contain("\"vaultIdentity\": {");
        manifestJson.Should().Contain("\"code\": \"review-required-evidence\"");
        manifestJson.Should().NotContain("This warning should be excluded.");
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestRead_RejectsDotSegmentSubjectTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject("report-pack", "current");
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [Node(subject, "node-1", "analysis-export", EvidenceStatusDto.Ready)],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["node-1"], ["node-1"], [], [], []),
            Actions: [],
            Warnings: []);

        var response = await store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "safe export"));
        var generatedFileName = Uri.UnescapeDataString(response.ManifestRoute.Split('/')[^1]);
        var validManifest = await store.TryOpenManifestAsync("report-pack", "current", generatedFileName);
        validManifest.Should().NotBeNull();
        await validManifest!.Content.DisposeAsync();

        var evidenceRoot = Path.Combine(root, "workstation", "evidence");
        Directory.CreateDirectory(evidenceRoot);
        var escapedManifestPath = Path.Combine(evidenceRoot, "secret-manifest.json");
        await File.WriteAllTextAsync(escapedManifestPath, """{"schemaVersion":1}""");

        var subjectIdTraversal = await store.TryOpenManifestAsync("report-pack", "..", "secret-manifest.json");
        var subjectKindTraversal = await store.TryOpenManifestAsync("..", "report-pack", "secret-manifest.json");
        var encodedSeparatorTraversal = await store.TryOpenManifestAsync("report-pack", "current/..", "secret-manifest.json");

        subjectIdTraversal.Should().BeNull();
        subjectKindTraversal.Should().BeNull();
        encodedSeparatorTraversal.Should().BeNull();
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringLedgerManifestExport_PreservesRouteOnlyArtifactRefs()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject(EvidenceSubjectResolver.StrategyRunKind, "run-ledger-proof");
        var generatedAt = new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);
        var artifacts = new[]
        {
            new EvidenceArtifactRefDto(
                "strategy-run:run-ledger-proof:ledger:journal",
                "ledger-journal",
                Path: null,
                Route: "/api/workstation/runs/run-ledger-proof/ledger/journal",
                GeneratedAt: generatedAt,
                Hash: null,
                Retained: true),
            new EvidenceArtifactRefDto(
                "strategy-run:run-ledger-proof:ledger:trial-balance",
                "ledger-trial-balance",
                Path: null,
                Route: "/api/workstation/runs/run-ledger-proof/ledger/trial-balance",
                GeneratedAt: generatedAt,
                Hash: null,
                Retained: true)
        };
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: generatedAt,
            Nodes: [Node(subject, "strategy-run:run-ledger-proof:ledger", "run-ledger", EvidenceStatusDto.Ready, artifacts: artifacts)],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["strategy-run:run-ledger-proof:ledger"], ["strategy-run:run-ledger-proof:ledger"], [], [], []),
            Actions: [],
            Warnings: []);

        var response = await store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "ledger proof export"));
        var manifestPath = Path.Combine(root, response.ManifestPath.Replace('/', Path.DirectorySeparatorChar));
        await using var stream = File.OpenRead(manifestPath);
        using var manifest = await JsonDocument.ParseAsync(stream);
        var ledgerNode = manifest.RootElement.GetProperty("nodes")
            .EnumerateArray()
            .Single(node => node.GetProperty("kind").GetString() == "run-ledger");
        var artifactRefs = ledgerNode.GetProperty("artifactRefs")
            .EnumerateArray()
            .ToArray();

        artifactRefs.Should().HaveCount(2);
        artifactRefs.Should().Contain(artifact =>
            IsRouteOnlyArtifact(artifact, "ledger-journal", "/api/workstation/runs/run-ledger-proof/ledger/journal"));
        artifactRefs.Should().Contain(artifact =>
            IsRouteOnlyArtifact(artifact, "ledger-trial-balance", "/api/workstation/runs/run-ledger-proof/ledger/trial-balance"));
    }

    [Fact]
    public async Task EvidenceEndpoints_VaultSearch_FindsBundlesByRunReportPackAndReconciliationCase()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evidence-vault-search-{Guid.NewGuid():N}");
        await using var app = await CreateEvidenceAppAsync(root);
        var client = app.GetTestClient();

        await client.PostAsJsonAsync(
            "/api/workstation/evidence/subjects/report-pack/current/export-manifest",
            new EvidencePacketExportRequest("operator", "seed")
            {
                Linkage = new EvidenceSubjectLinkageDto("report-pack/current", "run-123", "period-2026-05", "rp-55", "case-77")
            });

        var response = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/search",
            new EvidenceVaultLookupRequestDto(null, "run-123", null, "rp-55", "case-77"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var matches = await response.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceVaultIdentityDto>>(ServerJsonOptions);
        matches.Should().NotBeNull();
        matches!.Should().ContainSingle();
        matches[0].SubjectKind.Should().Be("report-pack");
        matches[0].SubjectId.Should().Be("current");
    }

    private static EvidenceGraphService CreateGraphService(IReadOnlyList<IEvidenceContributor> contributors)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new EvidenceGraphService(
            new EvidenceSubjectResolver(services),
            new EvidenceTemplateRegistry(),
            contributors,
            NullLogger<EvidenceGraphService>.Instance);
    }

    private static async Task<WebApplication> CreateEvidenceAppAsync(string root)
    {
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, """{"DataRoot":"."}""");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new Meridian.Application.UI.ConfigStore(configPath));
        builder.Services.AddWorkflowLibrary();
        builder.Services.AddEvidenceWorkflowFabric();

        var app = builder.Build();
        app.MapEvidenceEndpoints(ServerJsonOptions);
        await app.StartAsync();
        return app;
    }

    private static EvidenceSubjectDto Subject(string kind, string id)
        => new(
            SubjectId: id,
            SubjectKind: kind,
            Label: $"{kind} {id}",
            Workspace: "Trading",
            Route: "/trading/readiness",
            PageTag: "TradingReadiness");

    private static EvidenceNodeDto Node(
        EvidenceSubjectDto subject,
        string id,
        string kind,
        EvidenceStatusDto status,
        bool stale = false,
        IReadOnlyList<string>? workItemIds = null,
        IReadOnlyList<EvidenceArtifactRefDto>? artifacts = null)
        => new(
            EvidenceId: id,
            Subject: subject,
            Kind: kind,
            Status: status,
            Freshness: new EvidenceFreshnessDto(
                stale ? DateTimeOffset.UtcNow.AddDays(-8) : DateTimeOffset.UtcNow,
                stale,
                stale ? "Evidence is older than seven days." : null),
            SourceSystem: "test",
            Summary: $"{kind} evidence",
            ArtifactRefs: artifacts ?? [],
            RelatedWorkItemIds: workItemIds ?? []);

    private static bool IsRouteOnlyArtifact(JsonElement artifact, string kind, string route)
        => artifact.GetProperty("kind").GetString() == kind &&
           artifact.GetProperty("route").GetString() == route &&
           artifact.GetProperty("path").ValueKind == JsonValueKind.Null &&
           artifact.GetProperty("hash").ValueKind == JsonValueKind.Null;

    private sealed class StubContributor : IEvidenceContributor
    {
        private readonly Func<EvidenceSubjectDto, bool> _supports;
        private readonly Func<EvidenceContributionContext, EvidenceContribution> _contribute;

        public StubContributor(
            string contributorId,
            Func<EvidenceSubjectDto, bool> supports,
            Func<EvidenceContributionContext, EvidenceContribution> contribute)
        {
            ContributorId = contributorId;
            _supports = supports;
            _contribute = contribute;
        }

        public string ContributorId { get; }

        public bool Supports(EvidenceSubjectDto subject) => _supports(subject);

        public Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_contribute(context));
        }
    }
}
