using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class EvidenceWorkbenchViewModelTests
{
    [Theory]
    [InlineData("accounting-record/accounting-record-2026-05", "accounting-record", "accounting-record-2026-05")]
    [InlineData("EvidenceWorkbench:accounting-record/accounting-record-2026-05", "accounting-record", "accounting-record-2026-05")]
    [InlineData("evidenceworkbench:strategy-run/run-42", "strategy-run", "run-42")]
    [InlineData("/accounting-record/rec-1", "accounting-record", "rec-1")]
    [InlineData("operations-evidence-package/pkg/2026-06", "operations-evidence-package", "pkg/2026-06")]
    public void TryParseSubject_SubjectStrings_ResolveKindAndId(string parameter, string expectedKind, string expectedId)
    {
        var parsed = EvidenceWorkbenchViewModel.TryParseSubject(parameter);

        parsed.Should().NotBeNull();
        parsed!.Value.SubjectKind.Should().Be(expectedKind);
        parsed.Value.SubjectId.Should().Be(expectedId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("accounting-record")]
    [InlineData("accounting-record/")]
    [InlineData("EvidenceWorkbench:")]
    public void TryParseSubject_NonSubjectParameters_ReturnNull(string? parameter)
    {
        EvidenceWorkbenchViewModel.TryParseSubject(parameter).Should().BeNull();
        EvidenceWorkbenchViewModel.TryParseSubject(42).Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_ProjectsPacketVaultQueuesAndSummary()
    {
        var client = CreateLoadedClient();
        using var viewModel = new EvidenceWorkbenchViewModel(client);

        await viewModel.RefreshAsync();

        viewModel.HasPacket.Should().BeTrue();
        viewModel.SelectedSubject.Should().NotBeNull();
        viewModel.SelectedSubject!.SubjectKey.Should().Be("strategy-run/run-42");
        viewModel.Title.Should().Be("Strategy run 42");
        viewModel.Subtitle.Should().Be("Strategy evidence packet");
        viewModel.ScoreText.Should().Be("82% complete");
        viewModel.CompletenessStatusText.Should().Be("Review Required");
        viewModel.GeneratedText.Should().Be("2026-08-01 12:30 UTC");
        viewModel.ProofChainCoverageText.Should().Be("1/2 layers, 50% coverage");
        viewModel.ProofChainRows.Should().ContainSingle()
            .Which.CountsText.Should().Be("1 ready node; 1 review node; 1 missing node");
        viewModel.NodeRows.Should().ContainSingle();
        viewModel.NodeRows[0].EvidenceId.Should().Be("evidence-price");
        viewModel.NodeRows[0].StatusText.Should().Be("Ready");
        viewModel.NodeRows[0].ArtifactCountText.Should().Be("1 artifact");
        viewModel.LineageRows.Should().ContainSingle()
            .Which.RelationshipText.Should().Be("Supports");
        viewModel.MissingEvidence.Should().Equal("evidence-recon");
        viewModel.StaleEvidence.Should().Equal("evidence-nav");
        viewModel.OrphanEvidence.Should().Equal("evidence-orphan");
        viewModel.SlaBreaches.Should().Equal("Price capture exceeded its freshness SLA.");
        viewModel.Warnings.Should().Equal("One warning was retained with the packet.");
        viewModel.PacketActions.Should().ContainSingle()
            .Which.TargetText.Should().Be("Open StrategyRuns");
        viewModel.RequestListRows.Should().ContainSingle()
            .Which.OpenRequestCountText.Should().Be("2 open requests");
        viewModel.DocumentQueue.SummaryText.Should().Be("1 document retained");
        client.LastPacketSubject.Should().Be(("strategy-run", "run-42"));
        client.LastRequestListQuery.Should().Be(("strategy-run", "run-42", 25));
        client.LastDocumentQuery.Should().Be(("strategy-run", "run-42", 25));
    }

    [Fact]
    public async Task Parameter_DeepLink_FocusesRequestedSubjectEvenWhenNotListed()
    {
        var client = CreateLoadedClient();
        client.PacketResponse = ApiResponse<EvidencePacketDto>.Fail("Evidence subject was not found.", 404);
        using var viewModel = new EvidenceWorkbenchViewModel(client);

        viewModel.Parameter = "EvidenceWorkbench:accounting-record/accounting-record-2026-05";
        await viewModel.RefreshAsync();

        client.LastPacketSubject.Should().Be(("accounting-record", "accounting-record-2026-05"));
        viewModel.SelectedSubject.Should().NotBeNull();
        viewModel.SelectedSubject!.SubjectKey.Should().Be("accounting-record/accounting-record-2026-05");
        viewModel.Subjects.Select(subject => subject.SubjectKey)
            .Should().Contain("accounting-record/accounting-record-2026-05");
        viewModel.HasPacket.Should().BeFalse();
        viewModel.StatusText.Should().Be("Evidence subject was not found.");
    }

    [Fact]
    public async Task RefreshAsync_SubjectListFailure_SurfacesError()
    {
        var client = CreateLoadedClient();
        client.SubjectsResponse = ApiResponse<EvidenceSubjectDto[]>.Fail("Evidence subjects endpoint is unavailable.");
        using var viewModel = new EvidenceWorkbenchViewModel(client);

        await viewModel.RefreshAsync();

        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorText.Should().Be("Evidence subjects endpoint is unavailable.");
        viewModel.StatusText.Should().Be("Evidence workbench failed to load.");
        viewModel.HasPacket.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_ReplacesCompletenessProjectionWithServerResult()
    {
        var client = CreateLoadedClient();
        client.ValidateResponse = ApiResponse<EvidenceCompletenessDto>.Ok(new EvidenceCompletenessDto(
            Score: 100,
            Status: EvidenceStatusDto.Ready,
            RequiredIds: ["evidence-price"],
            ReadyIds: ["evidence-price"],
            MissingIds: [],
            StaleIds: [],
            BlockingWorkItemIds: []));
        using var viewModel = new EvidenceWorkbenchViewModel(client);
        await viewModel.RefreshAsync();

        await viewModel.ValidateAsync();

        client.LastValidateSubject.Should().Be(("strategy-run", "run-42"));
        viewModel.ScoreText.Should().Be("100% complete");
        viewModel.CompletenessStatusText.Should().Be("Ready");
        viewModel.MissingEvidence.Should().BeEmpty();
        viewModel.StaleEvidence.Should().BeEmpty();
        viewModel.StatusText.Should().Be("Validation refreshed for Strategy run 42.");
    }

    [Fact]
    public async Task ExportManifestAsync_SurfacesRetainedManifestIdentity()
    {
        var client = CreateLoadedClient();
        client.ExportResponse = ApiResponse<EvidencePacketExportResponse>.Ok(new EvidencePacketExportResponse(
            SubjectKind: "strategy-run",
            SubjectId: "run-42",
            GeneratedAt: DateTimeOffset.Parse("2026-08-01T12:45:00Z"),
            ManifestPath: "evidence/strategy-run/run-42/manifest.json",
            ManifestRoute: "/workstation/evidence/strategy-run/run-42/manifest.json",
            EvidenceCount: 3,
            WarningCount: 1,
            Retained: true)
        {
            VaultIdentity = new EvidenceVaultIdentityDto(
                VaultId: "vault-77",
                SubjectKind: "strategy-run",
                SubjectId: "run-42",
                ManifestPath: "evidence/strategy-run/run-42/manifest.json",
                ManifestRoute: "/workstation/evidence/vault/vault-77",
                RetainedAt: DateTimeOffset.Parse("2026-08-01T12:45:05Z"),
                ContentHashSha256: "abc123",
                SchemaVersion: 1,
                StorageKind: "file")
        });
        using var viewModel = new EvidenceWorkbenchViewModel(client);
        await viewModel.RefreshAsync();

        await viewModel.ExportManifestAsync();

        client.LastExportSubject.Should().Be(("strategy-run", "run-42"));
        client.LastExportRequest.Should().NotBeNull();
        client.LastExportRequest!.IncludeWarnings.Should().BeTrue();
        client.LastExportRequest.RequestedBy.Should().BeNull("the server resolves the export actor");
        viewModel.HasExportResult.Should().BeTrue();
        viewModel.ExportResultText.Should().Be(
            "Manifest retained at evidence/strategy-run/run-42/manifest.json (3 evidence, 1 warnings). Vault vault-77.");
        viewModel.StatusText.Should().Be("Evidence manifest retained for Strategy run 42.");
    }

    [Fact]
    public async Task Commands_WithoutPacket_StayDisabledAndDoNothing()
    {
        var client = CreateLoadedClient();
        client.SubjectsResponse = ApiResponse<EvidenceSubjectDto[]>.Ok([]);
        client.PacketResponse = ApiResponse<EvidencePacketDto>.Fail("no packet");
        using var viewModel = new EvidenceWorkbenchViewModel(client);

        await viewModel.RefreshAsync();
        await viewModel.ValidateAsync();
        await viewModel.ExportManifestAsync();

        viewModel.HasPacket.Should().BeFalse();
        viewModel.ValidateCommand.CanExecute(null).Should().BeFalse();
        viewModel.ExportManifestCommand.CanExecute(null).Should().BeFalse();
        client.LastValidateSubject.Should().BeNull();
        client.LastExportSubject.Should().BeNull();
    }

    private static FakeEvidenceWorkbenchApiClient CreateLoadedClient()
    {
        var subject = new EvidenceSubjectDto(
            SubjectId: "run-42",
            SubjectKind: "strategy-run",
            Label: "Strategy run 42",
            Workspace: "Strategy",
            Route: null,
            PageTag: "StrategyRuns");
        var completeness = new EvidenceCompletenessDto(
            Score: 82,
            Status: EvidenceStatusDto.ReviewRequired,
            RequiredIds: ["evidence-price", "evidence-recon"],
            ReadyIds: ["evidence-price"],
            MissingIds: ["evidence-recon"],
            StaleIds: ["evidence-nav"],
            BlockingWorkItemIds: ["WI-9"])
        {
            OrphanEvidenceIds = ["evidence-orphan"],
            SlaAssessments =
            [
                new EvidenceSlaAssessmentDto(
                    PolicyId: "sla-price",
                    EvidenceId: "evidence-price",
                    EvidenceKind: "price-capture",
                    SourceSystem: "polygon",
                    AgeMinutes: 95,
                    FreshnessMinutes: 60,
                    IsBreached: true,
                    Severity: EvidenceValidationSeverityDto.Warning,
                    Message: "Price capture exceeded its freshness SLA.")
            ]
        };
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.Parse("2026-08-01T12:30:00Z"),
            Nodes:
            [
                new EvidenceNodeDto(
                    EvidenceId: "evidence-price",
                    Subject: subject,
                    Kind: "price-capture",
                    Status: EvidenceStatusDto.Ready,
                    Freshness: new EvidenceFreshnessDto(DateTimeOffset.Parse("2026-08-01T12:00:00Z"), false, null),
                    SourceSystem: "polygon",
                    Summary: "Prices captured for the run window.",
                    ArtifactRefs:
                    [
                        new EvidenceArtifactRefDto(
                            ArtifactId: "artifact-1",
                            Kind: "price-snapshot",
                            Path: "prices.json",
                            Route: null,
                            GeneratedAt: DateTimeOffset.Parse("2026-08-01T12:01:00Z"),
                            Hash: "hash-1",
                            Retained: true)
                    ],
                    RelatedWorkItemIds: ["WI-9"])
            ],
            Edges:
            [
                new EvidenceEdgeDto(
                    FromId: "evidence-price",
                    ToId: "evidence-recon",
                    Relationship: "supports",
                    Reason: "Price capture feeds reconciliation.")
            ],
            Completeness: completeness,
            Actions:
            [
                new WorkflowActionDto(
                    ActionId: "open-run",
                    Label: "Open strategy run",
                    Detail: "Review the run this packet supports.",
                    TargetPageTag: "StrategyRuns",
                    Tone: "primary",
                    WorkItemKind: null,
                    RoutePrefixes: [],
                    RouteContains: [],
                    Aliases: [])
            ],
            Warnings: ["One warning was retained with the packet."])
        {
            ProofChain = new EvidenceProofChainDto(
                CoveragePercent: 50,
                Status: EvidenceStatusDto.ReviewRequired,
                CoveredLayerCount: 1,
                TotalLayerCount: 2,
                Layers:
                [
                    new EvidenceProofChainLayerDto(
                        Layer: EvidenceProofChainLayerKindDto.Source,
                        Label: "Source capture",
                        Status: EvidenceStatusDto.ReviewRequired,
                        CoveragePercent: 50,
                        RequiredEvidenceIds: ["evidence-price", "evidence-recon"],
                        EvidenceIds: ["evidence-price"],
                        ReadyEvidenceIds: ["evidence-price"],
                        ReviewEvidenceIds: ["evidence-nav"],
                        MissingEvidenceIds: ["evidence-recon"],
                        EvidenceKinds: ["price-capture"],
                        Summary: "Source capture layer needs reconciliation evidence.")
                ],
                Summary: "Proof chain covers source capture only.")
        };
        var document = new EvidenceDocumentDto(
            DocumentId: "doc-1",
            FileName: "statement.pdf",
            Classification: EvidenceDocumentClassificationDto.Statement,
            SourceHashSha256: "hash-doc-1",
            ReceivedAt: DateTimeOffset.Parse("2026-07-30T09:00:00Z"),
            SourceChannel: "Upload",
            Actor: "ops-user",
            TenantId: "tenant-1",
            Scope: "company-1",
            ExtractionStatus: EvidenceExtractionStatusDto.Accepted,
            ObjectLinks: [],
            ReviewerState: new EvidenceDocumentReviewStateDto(EvidenceDocumentReviewStatusDto.Accepted, "reviewer"),
            AuditTrail: []);

        return new FakeEvidenceWorkbenchApiClient
        {
            SubjectsResponse = ApiResponse<EvidenceSubjectDto[]>.Ok([subject]),
            PacketResponse = ApiResponse<EvidencePacketDto>.Ok(packet),
            RequestListsResponse = ApiResponse<EvidenceVaultRequestListEntryDto[]>.Ok(
            [
                new EvidenceVaultRequestListEntryDto(
                    RequestListId: "rl-1",
                    RequestListKind: "Close",
                    TargetKind: "period",
                    TargetId: "2026-07",
                    HighestSeverity: EvidenceValidationSeverityDto.Warning,
                    Status: "Open",
                    RequestCount: 2,
                    OpenRequestCount: 2,
                    RequestIds: ["req-1", "req-2"],
                    EvidenceKinds: ["bank-statement"],
                    BlockedOutputs: ["close"],
                    Summary: "Two support requests block the close.",
                    VaultId: "vault-77",
                    SubjectKind: "strategy-run",
                    SubjectId: "run-42",
                    ManifestRoute: "/workstation/evidence/vault/vault-77",
                    RetainedAt: DateTimeOffset.Parse("2026-07-31T18:00:00Z"),
                    SupportRequests: [])
            ]),
            DocumentsResponse = ApiResponse<EvidenceVaultDocumentEntryDto[]>.Ok(
            [
                new EvidenceVaultDocumentEntryDto(
                    Document: document,
                    VaultId: "vault-77",
                    SubjectKind: "strategy-run",
                    SubjectId: "run-42",
                    ManifestRoute: "/workstation/evidence/vault/vault-77",
                    RetainedAt: DateTimeOffset.Parse("2026-07-31T18:00:00Z"),
                    StorageKind: "file",
                    OpenRequestCount: 0,
                    SupportRequests: [])
            ])
        };
    }

    private sealed class FakeEvidenceWorkbenchApiClient : IEvidenceWorkbenchApiClient
    {
        public ApiResponse<EvidenceSubjectDto[]> SubjectsResponse { get; set; } =
            ApiResponse<EvidenceSubjectDto[]>.Ok([]);

        public ApiResponse<EvidencePacketDto> PacketResponse { get; set; } =
            ApiResponse<EvidencePacketDto>.Fail("no packet configured");

        public ApiResponse<EvidenceCompletenessDto> ValidateResponse { get; set; } =
            ApiResponse<EvidenceCompletenessDto>.Fail("no validation configured");

        public ApiResponse<EvidencePacketExportResponse> ExportResponse { get; set; } =
            ApiResponse<EvidencePacketExportResponse>.Fail("no export configured");

        public ApiResponse<EvidenceVaultRequestListEntryDto[]> RequestListsResponse { get; set; } =
            ApiResponse<EvidenceVaultRequestListEntryDto[]>.Ok([]);

        public ApiResponse<EvidenceVaultDocumentEntryDto[]> DocumentsResponse { get; set; } =
            ApiResponse<EvidenceVaultDocumentEntryDto[]>.Ok([]);

        public (string SubjectKind, string SubjectId)? LastPacketSubject { get; private set; }

        public (string SubjectKind, string SubjectId)? LastValidateSubject { get; private set; }

        public (string SubjectKind, string SubjectId)? LastExportSubject { get; private set; }

        public EvidencePacketExportRequest? LastExportRequest { get; private set; }

        public (string? SubjectKind, string? SubjectId, int MaxResults)? LastRequestListQuery { get; private set; }

        public (string? SubjectKind, string? SubjectId, int MaxResults)? LastDocumentQuery { get; private set; }

        public Task<ApiResponse<EvidenceSubjectDto[]>> ListSubjectsAsync(CancellationToken ct = default)
            => Task.FromResult(SubjectsResponse);

        public Task<ApiResponse<EvidencePacketDto>> GetPacketAsync(
            string subjectKind,
            string subjectId,
            CancellationToken ct = default)
        {
            LastPacketSubject = (subjectKind, subjectId);
            return Task.FromResult(PacketResponse);
        }

        public Task<ApiResponse<EvidenceCompletenessDto>> ValidatePacketAsync(
            string subjectKind,
            string subjectId,
            CancellationToken ct = default)
        {
            LastValidateSubject = (subjectKind, subjectId);
            return Task.FromResult(ValidateResponse);
        }

        public Task<ApiResponse<EvidencePacketExportResponse>> ExportManifestAsync(
            string subjectKind,
            string subjectId,
            EvidencePacketExportRequest request,
            CancellationToken ct = default)
        {
            LastExportSubject = (subjectKind, subjectId);
            LastExportRequest = request;
            return Task.FromResult(ExportResponse);
        }

        public Task<ApiResponse<EvidenceVaultRequestListEntryDto[]>> ListOpenRequestListsAsync(
            string? subjectKind,
            string? subjectId,
            int maxResults,
            CancellationToken ct = default)
        {
            LastRequestListQuery = (subjectKind, subjectId, maxResults);
            return Task.FromResult(RequestListsResponse);
        }

        public Task<ApiResponse<EvidenceVaultDocumentEntryDto[]>> ListDocumentsAsync(
            string? subjectKind,
            string? subjectId,
            int maxResults,
            CancellationToken ct = default)
        {
            LastDocumentQuery = (subjectKind, subjectId, maxResults);
            return Task.FromResult(DocumentsResponse);
        }
    }
}
