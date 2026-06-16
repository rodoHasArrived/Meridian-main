using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrations;

namespace Meridian.Tests.Contracts;

public sealed class ProviderIntegrationContractsTests
{
    [Fact]
    public void ProviderIntegrationManifest_RoundTripsWithCamelCaseAndStringEnums()
    {
        var manifest = CreateManifest();

        var json = JsonSerializer.Serialize(
            manifest,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto);
        var roundTrip = JsonSerializer.Deserialize(
            json,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto);

        json.Should().Contain("\"manifestId\"");
        json.Should().Contain("\"integrationType\": \"Rest\"");
        json.Should().Contain("\"capability\": \"Positions\"");
        json.Should().Contain("\"targetField\": \"security.cusip\"");
        json.Should().NotContain("\"ManifestId\"");
        roundTrip.Should().BeEquivalentTo(manifest);
    }

    [Fact]
    public void ProviderConnection_DoesNotRequireCredentialSecretMaterial()
    {
        var connection = new ProviderConnectionDto(
            "connection-alpha",
            "custodian-abc",
            "manifest-custodian-abc-v1",
            "General Account",
            "production",
            ProviderIntegrationActivationStateDto.PendingApproval,
            "vault://provider-credentials/custodian-abc/general-account",
            [ProviderCapabilityKindDto.Positions],
            "ops@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:05:00Z"),
            "approval-evidence-1");

        var json = JsonSerializer.Serialize(
            connection,
            ProviderIntegrationContractsJsonContext.Default.ProviderConnectionDto);

        json.Should().Contain("\"credentialSecretRef\"");
        json.Should().NotContain("clientSecret");
        json.Should().NotContain("apiKey");
        json.Should().NotContain("password");
    }

    [Fact]
    public void ProviderIntegrationActivationReadiness_RoundTripsWithOperatorSafeIssues()
    {
        var readiness = new ProviderIntegrationActivationReadinessDto(
            IsReady: false,
            Issues:
            [
                new ProviderIntegrationActivationIssueDto(
                    "provider-manifest.required-mapping-missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Required canonical field 'quantity' is not mapped for Positions.",
                    ProviderCapabilityKindDto.Positions,
                    "Map the required canonical field before activation.")
            ],
            RequiredEvidence: ["dry-run-result", "activation-approval"]);

        var json = JsonSerializer.Serialize(
            readiness,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationActivationReadinessDto);
        var roundTrip = JsonSerializer.Deserialize(
            json,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationActivationReadinessDto);

        json.Should().Contain("\"isReady\": false");
        json.Should().Contain("\"severity\": \"Critical\"");
        json.Should().Contain("\"capability\": \"Positions\"");
        json.Should().NotContain("\"IsReady\"");
        roundTrip.Should().BeEquivalentTo(readiness);
    }

    [Fact]
    public void ProviderIntegrationSyncRun_RoundTripsWithDryRunEvidence()
    {
        var syncRun = new ProviderIntegrationSyncRunDto(
            "sync-run-1",
            "manifest-custodian-abc-v1",
            "connection-alpha",
            "custodian-abc",
            ProviderCapabilityKindDto.Positions,
            "positions",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:01:00Z"),
            ProviderIntegrationProcessingStatusDto.Validated,
            RecordsReceived: 50,
            RecordsAccepted: 48,
            RecordsQuarantined: 2,
            RawPayloadId: "payload-1",
            Issues:
            [
                new ValidationIssueDto(
                    "required.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Required field 'security.cusip' is missing.",
                    "security.cusip",
                    "Map CUSIP, ISIN, ticker, or provider security id.")
            ]);

        var json = JsonSerializer.Serialize(
            syncRun,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationSyncRunDto);
        var roundTrip = JsonSerializer.Deserialize(
            json,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationSyncRunDto);

        json.Should().Contain("\"syncRunId\": \"sync-run-1\"");
        json.Should().Contain("\"recordsAccepted\": 48");
        json.Should().Contain("\"status\": \"Validated\"");
        roundTrip.Should().BeEquivalentTo(syncRun);
    }

    [Fact]
    public void ProviderIntegrationConnectionMonitor_RoundTripsWithOperatorEvidence()
    {
        var evidence = new ProviderIntegrationSyncRunEvidenceDto(
            "sync-run-1",
            ProviderCapabilityKindDto.Positions,
            "positions",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:01:00Z"),
            ProviderIntegrationProcessingStatusDto.Quarantined,
            RecordsReceived: 50,
            RecordsAccepted: 48,
            RecordsQuarantined: 2,
            DurableStagingRecordCount: 48,
            DurableQuarantinedRecordCount: 2,
            CriticalIssueCount: 1,
            WarningIssueCount: 1,
            RawPayloadId: "payload-1",
            Issues:
            [
                new ValidationIssueDto(
                    "required.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Required field 'security.cusip' is missing.",
                    "security.cusip",
                    "Map CUSIP, ISIN, ticker, or provider security id.")
            ]);
        var monitor = new ProviderIntegrationConnectionMonitorDto(
            "connection-alpha",
            "manifest-custodian-abc-v1",
            "custodian-abc",
            "Custodian ABC",
            "General Account",
            "production",
            ProviderIntegrationActivationStateDto.DryRunPassed,
            [ProviderCapabilityKindDto.Positions],
            evidence,
            [evidence],
            RecentRecordsReceived: 50,
            RecentRecordsAccepted: 48,
            RecentRecordsQuarantined: 2,
            DurableStagingRecordCount: 48,
            DurableQuarantinedRecordCount: 2,
            HasCriticalIssues: true);

        var json = JsonSerializer.Serialize(
            monitor,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationConnectionMonitorDto);
        var roundTrip = JsonSerializer.Deserialize(
            json,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationConnectionMonitorDto);

        json.Should().Contain("\"lastSyncRun\"");
        json.Should().Contain("\"durableQuarantinedRecordCount\": 2");
        json.Should().Contain("\"hasCriticalIssues\": true");
        json.Should().NotContain("\"LastSyncRun\"");
        roundTrip.Should().BeEquivalentTo(monitor);
    }

    [Fact]
    public void ProviderIntegrationActivation_RoundTripsWithApprovalEvidence()
    {
        var request = new ProviderIntegrationActivationRequestDto(
            "manifest-custodian-abc-v1",
            "connection-alpha",
            "approver@example.com",
            DateTimeOffset.Parse("2026-06-16T14:00:00Z"),
            "approval-evidence-1",
            "Approved after dry-run evidence review.");
        var result = new ProviderIntegrationActivationResultDto(
            Activated: true,
            request.ManifestId,
            request.ConnectionId,
            ProviderIntegrationActivationStateDto.Active,
            ProviderIntegrationActivationStateDto.Active,
            new ProviderIntegrationActivationReadinessDto(
                IsReady: true,
                Issues: [],
                RequiredEvidence: ["activation-approval", "dry-run-result"]),
            request.ApprovalEvidenceId,
            "Provider integration connection activated.");

        var requestJson = JsonSerializer.Serialize(
            request,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationActivationRequestDto);
        var resultJson = JsonSerializer.Serialize(
            result,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationActivationResultDto);
        var requestRoundTrip = JsonSerializer.Deserialize(
            requestJson,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationActivationRequestDto);
        var resultRoundTrip = JsonSerializer.Deserialize(
            resultJson,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationActivationResultDto);

        requestJson.Should().Contain("\"approvalEvidenceId\"");
        requestJson.Should().NotContain("\"ApprovalEvidenceId\"");
        resultJson.Should().Contain("\"activated\": true");
        resultJson.Should().Contain("\"manifestState\": \"Active\"");
        requestRoundTrip.Should().BeEquivalentTo(request);
        resultRoundTrip.Should().BeEquivalentTo(result);
    }

    [Fact]
    public void ProviderIntegrationSetupSave_RoundTripsWithManifestAndConnection()
    {
        var manifest = CreateManifest();
        var connection = new ProviderConnectionDto(
            "connection-alpha",
            manifest.ProviderId,
            manifest.ManifestId,
            "General Account",
            manifest.Environment,
            ProviderIntegrationActivationStateDto.Draft,
            "vault://provider-credentials/custodian-abc/general-account",
            [ProviderCapabilityKindDto.Positions],
            "ops@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-16T12:05:00Z"),
            ApprovalEvidenceId: null);
        var request = new ProviderIntegrationSetupSaveRequestDto(
            manifest,
            connection,
            "ops@example.com",
            DateTimeOffset.Parse("2026-06-16T12:10:00Z"),
            "Saved from setup wizard.");
        var result = new ProviderIntegrationSetupSaveResultDto(
            Saved: true,
            manifest.ManifestId,
            connection.ConnectionId,
            ProviderIntegrationActivationStateDto.Draft,
            ProviderIntegrationActivationStateDto.Draft,
            new ProviderIntegrationActivationReadinessDto(
                IsReady: false,
                Issues: [],
                RequiredEvidence: ["endpoint-test", "dry-run-result"]),
            "Provider integration setup draft saved.");

        var requestJson = JsonSerializer.Serialize(
            request,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationSetupSaveRequestDto);
        var resultJson = JsonSerializer.Serialize(
            result,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationSetupSaveResultDto);
        var requestRoundTrip = JsonSerializer.Deserialize(
            requestJson,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationSetupSaveRequestDto);
        var resultRoundTrip = JsonSerializer.Deserialize(
            resultJson,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationSetupSaveResultDto);

        requestJson.Should().Contain("\"manifest\"");
        requestJson.Should().Contain("\"connection\"");
        requestJson.Should().Contain("\"savedBy\"");
        requestJson.Should().NotContain("\"SavedBy\"");
        resultJson.Should().Contain("\"saved\": true");
        resultJson.Should().Contain("\"manifestState\": \"Draft\"");
        requestRoundTrip.Should().BeEquivalentTo(request);
        resultRoundTrip.Should().BeEquivalentTo(result);
    }

    [Fact]
    public void ProviderIntegrationQuarantineReview_RoundTripsWithDecisionEvidence()
    {
        var record = new QuarantinedRecordDto(
            "quarantine-1",
            "sync-run-1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            JsonDocument.Parse("""{"account_id":"A1"}""").RootElement.Clone(),
            MappedRecord: null,
            [
                new ValidationIssueDto(
                    "position.security-id.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Security identifier is required.",
                    "security",
                    "Map CUSIP, ISIN, ticker, or provider security id.")
            ],
            ProviderIntegrationProcessingStatusDto.Quarantined,
            DateTimeOffset.Parse("2026-06-16T12:01:00Z"));
        var decision = new ProviderIntegrationQuarantineDecisionDto(
            "decision-1",
            record.SyncRunId,
            record.QuarantineRecordId,
            record.ConnectionId,
            ProviderIntegrationQuarantineResolutionActionDto.ReplayAfterMappingChange,
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:05:00Z"),
            "Mapping updated.");
        var review = new ProviderIntegrationQuarantineReviewDto(
            record.ConnectionId,
            [record.SyncRunId],
            [record],
            [
                new ProviderIntegrationQuarantineIssueGroupDto(
                    "position.security-id.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "security",
                    "Security identifier is required.",
                    "Map CUSIP, ISIN, ticker, or provider security id.",
                    RecordCount: 1)
            ],
            [decision],
            TotalQuarantinedRecords: 1,
            CriticalIssueCount: 1,
            WarningIssueCount: 0);
        var result = new ProviderIntegrationQuarantineResolutionResultDto(
            Resolved: true,
            record,
            decision,
            "Provider integration quarantine review decision recorded.");

        var reviewJson = JsonSerializer.Serialize(
            review,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationQuarantineReviewDto);
        var resultJson = JsonSerializer.Serialize(
            result,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationQuarantineResolutionResultDto);
        var reviewRoundTrip = JsonSerializer.Deserialize(
            reviewJson,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationQuarantineReviewDto);
        var resultRoundTrip = JsonSerializer.Deserialize(
            resultJson,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationQuarantineResolutionResultDto);

        reviewJson.Should().Contain("\"issueGroups\"");
        reviewJson.Should().Contain("\"action\": \"ReplayAfterMappingChange\"");
        reviewJson.Should().NotContain("\"IssueGroups\"");
        resultJson.Should().Contain("\"resolved\": true");
        reviewRoundTrip.Should().NotBeNull();
        reviewRoundTrip!.ConnectionId.Should().Be(review.ConnectionId);
        reviewRoundTrip.Records.Should().ContainSingle()
            .Which.RawRecord.GetProperty("account_id").GetString().Should().Be("A1");
        reviewRoundTrip.IssueGroups.Should().ContainSingle()
            .Which.IssueCode.Should().Be("position.security-id.missing");
        reviewRoundTrip.Decisions.Should().ContainSingle()
            .Which.Action.Should().Be(ProviderIntegrationQuarantineResolutionActionDto.ReplayAfterMappingChange);
        resultRoundTrip.Should().NotBeNull();
        resultRoundTrip!.Resolved.Should().BeTrue();
        resultRoundTrip.Record.RawRecord.GetProperty("account_id").GetString().Should().Be("A1");
        resultRoundTrip.Decision.Should().BeEquivalentTo(decision);
    }

    [Fact]
    public void ProviderIntegrationQuarantineReplay_RoundTripsWithReplaySummary()
    {
        var request = new ProviderIntegrationQuarantineReplayRequestDto(
            "sync-run-replay-1",
            "sync-run-source-1",
            "manifest-custodian-abc-v1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            ["quarantine-1"],
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:20:00Z"));
        var result = new ProviderIntegrationQuarantineReplayResultDto(
            request.ReplaySyncRunId,
            "payload-replay-1",
            request.Capability,
            RecordsReplayed: 1,
            RecordsAccepted: 1,
            RecordsRequarantined: 0,
            ProviderIntegrationProcessingStatusDto.Validated,
            Issues: []);

        var requestJson = JsonSerializer.Serialize(
            request,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationQuarantineReplayRequestDto);
        var resultJson = JsonSerializer.Serialize(
            result,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationQuarantineReplayResultDto);
        var requestRoundTrip = JsonSerializer.Deserialize(
            requestJson,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationQuarantineReplayRequestDto);
        var resultRoundTrip = JsonSerializer.Deserialize(
            resultJson,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationQuarantineReplayResultDto);

        requestJson.Should().Contain("\"replaySyncRunId\"");
        requestJson.Should().Contain("\"quarantineRecordIds\"");
        requestJson.Should().NotContain("\"ReplaySyncRunId\"");
        resultJson.Should().Contain("\"recordsRequarantined\": 0");
        resultRoundTrip.Should().BeEquivalentTo(result);
        requestRoundTrip.Should().BeEquivalentTo(request);
    }

    [Fact]
    public void ProviderIntegrationOpenApiImport_RoundTripsWithGeneratedManifest()
    {
        var manifest = CreateManifest() with
        {
            IntegrationType = IntegrationTypeDto.OpenApiRest,
            ChangeReason = "Imported from OpenAPI specification."
        };
        var request = new ProviderIntegrationOpenApiImportRequestDto(
            "manifest-openapi-custodian-v1",
            "openapi-custodian",
            "OpenAPI Custodian",
            "test",
            ProviderIntegrationAuthTypeDto.OAuth2,
            "https://api.example.com/oauth/token",
            ["positions.read"],
            [ProviderCapabilityKindDto.Positions],
            """{"openapi":"3.0.0","paths":{}}""",
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            "Imported from provider OpenAPI spec.");
        var result = new ProviderIntegrationOpenApiImportResultDto(
            Imported: true,
            manifest,
            new ProviderIntegrationActivationReadinessDto(
                IsReady: false,
                Issues: [],
                RequiredEvidence: ["endpoint-test", "dry-run-result"]),
            Issues: [],
            "OpenAPI import draft manifest saved.");

        var requestJson = JsonSerializer.Serialize(
            request,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationOpenApiImportRequestDto);
        var resultJson = JsonSerializer.Serialize(
            result,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationOpenApiImportResultDto);
        var requestRoundTrip = JsonSerializer.Deserialize(
            requestJson,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationOpenApiImportRequestDto);
        var resultRoundTrip = JsonSerializer.Deserialize(
            resultJson,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationOpenApiImportResultDto);

        requestJson.Should().Contain("\"openApiDocumentJson\"");
        requestJson.Should().Contain("\"authType\": \"OAuth2\"");
        requestJson.Should().NotContain("\"OpenApiDocumentJson\"");
        resultJson.Should().Contain("\"imported\": true");
        resultJson.Should().Contain("\"integrationType\": \"OpenApiRest\"");
        requestRoundTrip.Should().BeEquivalentTo(request);
        resultRoundTrip.Should().BeEquivalentTo(result);
    }

    [Fact]
    public void ProviderIntegrationSchemaDriftCheck_RoundTripsWithPauseRecommendation()
    {
        var request = new ProviderIntegrationSchemaDriftCheckRequestDto(
            "manifest-custodian-abc-v1",
            "connection-alpha",
            ProviderCapabilityKindDto.Positions,
            "positions",
            "sync-run-1",
            "payload-1",
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:05:00Z"));
        var result = new ProviderIntegrationSchemaDriftCheckResultDto(
            request.ManifestId,
            request.ConnectionId,
            request.Capability,
            request.EndpointKey,
            request.SyncRunId,
            request.RawPayloadId,
            DriftDetected: true,
            ShouldPauseCapability: true,
            RecordsInspected: 0,
            Issues:
            [
                new ProviderIntegrationSchemaDriftIssueDto(
                    "schema.records-path.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    request.Capability,
                    request.EndpointKey,
                    "$.positions",
                    "No records were found at configured records path '$.positions'.",
                    "Pause this capability until the records path is remapped.")
            ]);

        var requestJson = JsonSerializer.Serialize(
            request,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationSchemaDriftCheckRequestDto);
        var resultJson = JsonSerializer.Serialize(
            result,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationSchemaDriftCheckResultDto);
        var requestRoundTrip = JsonSerializer.Deserialize(
            requestJson,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationSchemaDriftCheckRequestDto);
        var resultRoundTrip = JsonSerializer.Deserialize(
            resultJson,
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationSchemaDriftCheckResultDto);

        requestJson.Should().Contain("\"rawPayloadId\"");
        requestJson.Should().Contain("\"capability\": \"Positions\"");
        requestJson.Should().NotContain("\"RawPayloadId\"");
        resultJson.Should().Contain("\"shouldPauseCapability\": true");
        resultJson.Should().Contain("\"code\": \"schema.records-path.missing\"");
        requestRoundTrip.Should().BeEquivalentTo(request);
        resultRoundTrip.Should().BeEquivalentTo(result);
    }

    private static ProviderIntegrationManifestDto CreateManifest()
        => new(
            "manifest-custodian-abc-v1",
            1,
            "custodian-abc",
            "Custodian ABC",
            IntegrationTypeDto.Rest,
            "production",
            new ProviderIntegrationAuthConfigDto(
                ProviderIntegrationAuthTypeDto.OAuth2,
                "https://api.example.com/oauth/token",
                ["positions.read"],
                new Dictionary<string, string> { ["credentialField"] = "clientCredentials" }),
            [
                new ProviderCapabilityDto(
                    ProviderCapabilityKindDto.Positions,
                    Enabled: true,
                    RequiresCertifiedAdapter: false,
                    RequiredCanonicalFields: ["providerAccountId", "quantity", "asOf"])
            ],
            [
                new EndpointDefinitionDto(
                    "positions",
                    ProviderCapabilityKindDto.Positions,
                    ProviderIntegrationHttpMethodDto.Get,
                    "/v1/accounts/{accountId}/positions",
                    new Dictionary<string, string> { ["Accept"] = "application/json" },
                    new Dictionary<string, string> { ["asOf"] = "{asOfDate}" },
                    RequestBodyTemplate: null,
                    DependsOn: new EndpointDependencyDto("accounts", "$.id", "accountId"),
                    Pagination: new EndpointPaginationDto(
                        ProviderIntegrationPaginationTypeDto.Cursor,
                        "$.nextCursor",
                        "cursor",
                        NextUrlPath: null,
                        PageSize: 100),
                    Response: new EndpointResponseShapeDto(
                        "$.positions",
                        "sha256:shape",
                        ["$.positions[*].account_id", "$.positions[*].quantity"]))
            ],
            [
                new FieldMappingDto(
                    ProviderCapabilityKindDto.Positions,
                    "$.cusip",
                    "security.cusip",
                    new TransformRuleDto("trimUppercase", new Dictionary<string, string>()),
                    Required: false,
                    ProviderMappingConfidenceDto.High,
                    DefaultValue: null,
                    ConstantValue: null)
            ],
            new SyncScheduleDto(
                "incremental",
                "daily",
                "06:00",
                "America/New_York",
                ProviderIntegrationCursorTypeDto.Timestamp,
                "updated_at",
                "monthly"),
            [
                new ValidationRuleDto(
                    ProviderCapabilityKindDto.Positions,
                    "position.required-provider-account",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    "Provider account id is required.",
                    ["providerAccountId"])
            ],
            new ProviderIntegrationActivationPolicyDto(
                RequiresAuthenticationTest: true,
                RequiresEndpointTest: true,
                RequiresDryRun: true,
                RequiresApproval: true,
                ProductionWriteCapabilitiesAllowed: false,
                RequiredIssueCodes:
                [
                    "integration.connection-alpha.credential-state",
                    "integration.connection-alpha.required-mapping"
                ]),
            ProviderIntegrationActivationStateDto.Draft,
            "ops@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            ApprovedBy: null,
            ApprovedAt: null,
            ChangeReason: "Initial custodian positions setup");
}
