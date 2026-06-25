using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Tests.Services;

public sealed class OperationsContinuityDtoContractTests
{
    [Fact]
    public void OperationsContinuityWorkflowDto_ShouldRoundTripSharedContractWithoutClientSideStatusDerivation()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var dto = new OperationsContinuityWorkflowDto(
            WorkflowId: Guid.NewGuid(),
            FundAccountId: Guid.NewGuid(),
            PeriodId: "2026-05",
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "custodian",
            CreatedAtUtc: new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc: new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero),
            Version: 7,
            Status: OperationsWorkflowStatusDto.ReconciliationActive,
            BrokerIntakeState: OperationsBrokerIntakeStateDto.Complete,
            SecurityMasterState: OperationsSecurityMasterStateDto.Complete,
            LedgerPostingState: OperationsLedgerPostingStateDto.Complete,
            ReconciliationState: OperationsReconciliationStateDto.InReview,
            ApprovalState: OperationsApprovalStateDto.Pending,
            Gates:
            [
                new OperationsGateDto(
                    GateKey: OperationsGateKeyDto.Reconciliation,
                    DisplayName: "Reconciliation",
                    Status: OperationsGateStatusDto.ReviewRequired,
                    IsRequired: true,
                    Description: "Breaks require operator review.",
                    Blockers:
                    [
                        new OperationsWorkflowBlockerDto(
                            Code: "reconciliation-break",
                            Message: "Open critical break must be resolved.",
                            Gate: OperationsGateKeyDto.Reconciliation,
                            Severity: "critical",
                            EvidenceLinks:
                            [
                                new OperationsEvidenceLinkDto("ev-break-1", "Break packet", "/evidence/break-1", "reconciliation", DateTimeOffset.UtcNow)
                            ])
                    ],
                    NextActions:
                    [
                        new OperationsNextActionDto("open-break", "Open Break Review", "/wpf/fund-reconciliation", OperationsGateKeyDto.Reconciliation)
                    ],
                    CompletedAtUtc: null,
                    CompletedBy: null)
            ],
            Timeline: [],
            BreakCases:
            [
                new OperationsBreakCaseDto(
                    BreakId: "break-cash-1",
                    CheckId: "cash-reconciliation",
                    Category: "Cash",
                    Severity: "Critical",
                    Status: "Open",
                    Owner: "fund-controller",
                    DueDate: new DateOnly(2026, 5, 21),
                    ExpectedSource: "Custodian statement",
                    ActualSource: "Meridian ledger",
                    ExpectedAmount: 1000m,
                    ActualAmount: 975m,
                    Variance: -25m,
                    SecurityId: null,
                    Symbol: null,
                    SuggestedAction: "Resolve cash variance before close package approval.",
                    EvidenceLinks:
                    [
                        new OperationsEvidenceLinkDto(
                            "ev-break-1",
                            "Break packet",
                            "/evidence/break-1",
                            "reconciliation",
                            DateTimeOffset.UtcNow)
                    ],
                    CorrelationKeys: new OperationsContinuityCorrelationKeysDto(
                        RunId: "recon-run-2026-05",
                        FundAccountId: null,
                        PortfolioSnapshotId: null,
                        LedgerBatchId: "ledger-batch-2026-05",
                        LedgerPostingGroupId: null,
                        ReconciliationCaseId: "case-cash-1"),
                    EscalationLevel: "Controller",
                    EscalationReason: "Aged past SLA warning",
                    EscalatedAtUtc: new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
                    SlaState: "Breached",
                    SlaDueAtUtc: new DateTimeOffset(2026, 5, 20, 11, 0, 0, TimeSpan.Zero),
                    Materiality: 25m,
                    RootCauseCode: "BANK_CASH_TIMING",
                    ApprovalState: "PendingControllerReview",
                    BlockedOutputs:
                    [
                        "close-package",
                        "investor-statement"
                    ])
            ],
            LedgerPreview: null,
            Approvals: [],
            ReportPackReadiness: new OperationsReportPackReadinessDto(
                IsReady: false,
                ReportPackId: "report-pack-2026-05",
                BlockingReason: "Awaiting reconciliation sign-off.",
                EvidenceLinks:
                [
                    new OperationsEvidenceLinkDto("ev-pack-1", "Draft report pack", "/evidence/report-pack", "reporting", DateTimeOffset.UtcNow)
                ]),
            CloseChecklist: [],
            EvidenceLinks:
            [
                new OperationsEvidenceLinkDto("ev-close-1", "Close checklist", "/evidence/close-checklist", "ops-runbook", DateTimeOffset.UtcNow)
            ],
            Blockers:
            [
                new OperationsWorkflowBlockerDto(
                    Code: "report-pack-blocked",
                    Message: "Report pack cannot be promoted.",
                    Gate: OperationsGateKeyDto.Approval,
                    Severity: "warning",
                    EvidenceLinks:
                    [
                        new OperationsEvidenceLinkDto("ev-pack-1", "Draft report pack", "/evidence/report-pack", "reporting", DateTimeOffset.UtcNow)
                    ])
            ],
            NextActions:
            [
                new OperationsNextActionDto(
                    Code: "prepare-report-pack",
                    Label: "Prepare Report Pack",
                    Route: "/wpf/fund-report-pack",
                    Gate: OperationsGateKeyDto.Approval)
            ],
            CloseReadiness: new OperationsCloseReadinessDto(
                IsReadyToClose: false,
                Severity: "Critical",
                Score: 70,
                Components:
                [
                    new OperationsCloseReadinessComponentDto(
                        Key: "reconciliation",
                        Label: "Reconciliation",
                        Score: 0,
                        Weight: 15,
                        IsReady: false,
                        Severity: "Critical",
                        BlockingReason: "Unresolved reconciliation breaks still require disposition.",
                        Gate: OperationsGateKeyDto.Reconciliation,
                        RouteHint: "/workstation/accounting")
                ],
                Blockers:
                [
                    new OperationsCloseReadinessBlockerDto(
                        Code: "RECONCILIATION_BREAKS_OPEN",
                        Category: "Reconciliation",
                        Severity: "Critical",
                        Message: "Unresolved reconciliation breaks still require disposition.",
                        Gate: OperationsGateKeyDto.Reconciliation,
                        RouteHint: "/workstation/accounting")
                ],
                NextActions:
                [
                    new OperationsNextActionDto(
                        Code: "RECONCILIATION_BREAKS_OPEN",
                        Label: "Unresolved reconciliation breaks still require disposition.",
                        Route: "/workstation/accounting",
                        Gate: OperationsGateKeyDto.Reconciliation)
                ]),
            AccountingRecordSummary: new OperationsAccountingRecordSummaryDto(
                RecordId: "accounting-record-2026-05",
                IsAuditReady: false,
                CompleteCategoryCount: 4,
                RequiredCategoryCount: 8,
                Summary: "Accounting record has 4 of 8 required evidence categories complete.",
                EvidenceCategories:
                [
                    new OperationsAccountingRecordEvidenceCategoryDto(
                        Key: "reconciliation-case-history",
                        Label: "Reconciliation case history",
                        IsComplete: false,
                        Status: "Open critical break must be resolved.",
                        RouteHint: "/workstation/accounting",
                        EvidenceLinks: [],
                        RequiredEvidence: ["reconciliation run", "break-case decision history", "resolved exception evidence"])
                ],
                EvidenceLinks: []),
            ReviewedAutomation: new OperationsReviewedAutomationSummaryDto(
                SummaryId: "reviewed-automation",
                Stage: "Suggested matches require review",
                Status: EvidenceStatusDto.ReviewRequired,
                RequiresHumanReview: true,
                Summary: "Automation output is retained as suggested support only.",
                AllowedUseCases:
                [
                    "Suggest reconciliation matches",
                    "Summarize retained evidence"
                ],
                ProhibitedActions:
                [
                    "Approve its own work",
                    "Post material journals without approval",
                    "Erase evidence"
                ],
                EvidenceLinks:
                [
                    new OperationsEvidenceLinkDto(
                        "ev-automation-1",
                        "Suggested match review",
                        "/evidence/automation-review",
                        "reviewed-automation",
                        DateTimeOffset.UtcNow)
                ],
                RequiredActions:
                [
                    "Route the suggestion to a human reviewer."
                ]));

        var json = JsonSerializer.Serialize(dto, options);
        var actual = JsonSerializer.Deserialize<OperationsContinuityWorkflowDto>(json, options);

        actual.Should().NotBeNull();
        actual!.Status.Should().Be(OperationsWorkflowStatusDto.ReconciliationActive);
        actual.EvidenceLinks.Should().ContainSingle(link => link.EvidenceId == "ev-close-1");
        actual.BreakCases.Should().ContainSingle();
        var breakCase = actual.BreakCases.Single();
        breakCase.Owner.Should().Be("fund-controller");
        breakCase.SlaState.Should().Be("Breached");
        breakCase.SlaDueAtUtc.Should().Be(new DateTimeOffset(2026, 5, 20, 11, 0, 0, TimeSpan.Zero));
        breakCase.Materiality.Should().Be(25m);
        breakCase.RootCauseCode.Should().Be("BANK_CASH_TIMING");
        breakCase.ApprovalState.Should().Be("PendingControllerReview");
        breakCase.BlockedOutputs.Should().Contain("close-package");
        breakCase.BlockedOutputs.Should().Contain("investor-statement");
        breakCase.CorrelationKeys!.ReconciliationCaseId.Should().Be("case-cash-1");
        actual.ReportPackReadiness.IsReady.Should().BeFalse();
        actual.ReportPackReadiness.ReportPackId.Should().Be("report-pack-2026-05");
        actual.CloseReadiness.Should().NotBeNull();
        actual.CloseReadiness!.Score.Should().Be(70);
        actual.CloseReadiness.Components.Should().ContainSingle(component => component.Key == "reconciliation");
        actual.AccountingRecordSummary.Should().NotBeNull();
        actual.AccountingRecordSummary!.RequiredCategoryCount.Should().Be(8);
        actual.AccountingRecordSummary.EvidenceCategories.Should().ContainSingle(category => category.Key == "reconciliation-case-history");
        actual.AccountingRecordSummary.EvidenceCategories.Single().RequiredEvidence.Should().Contain("break-case decision history");
        actual.ReviewedAutomation.Should().NotBeNull();
        actual.ReviewedAutomation!.Stage.Should().Be("Suggested matches require review");
        actual.ReviewedAutomation.RequiresHumanReview.Should().BeTrue();
        actual.ReviewedAutomation.AllowedUseCases.Should().Contain("Summarize retained evidence");
        actual.ReviewedAutomation.ProhibitedActions.Should().Contain("Erase evidence");
        actual.ReviewedAutomation.EvidenceLinks.Should().ContainSingle(link => link.EvidenceId == "ev-automation-1");
        actual.Blockers.Should().ContainSingle(blocker => blocker.EvidenceLinks.Any(link => link.EvidenceId == "ev-pack-1"));
        actual.NextActions.Should().ContainSingle(action => action.Code == "prepare-report-pack");
    }

    [Fact]
    public void GovernanceLifecycleProjectionDto_FinancialOperationsQueueInputs_ShouldDefaultAndRoundTrip()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var legacyProjection = new GovernanceLifecycleProjectionDto(
            DecisionPosture: "Pending review",
            SignoffPosture: "Approval pending",
            CloseReadiness: "Blocked",
            AuditTraceability: "Evidence pending");

        legacyProjection.EvidenceReferences.Should().BeEmpty();
        legacyProjection.AuditReferences.Should().BeEmpty();
        legacyProjection.EvidencePackages.Should().BeEmpty();
        legacyProjection.ReconciliationLanes.Should().BeEmpty();
        legacyProjection.BreakCases.Should().BeEmpty();
        legacyProjection.CloseChecklist.Should().BeEmpty();
        legacyProjection.Approvals.Should().BeEmpty();

        var evidence = new OperationsEvidenceLinkDto(
            "ev-finops-queue",
            "Financial Operations queue packet",
            "/api/workstation/operations/continuity/evidence/ev-finops-queue",
            "operations-continuity",
            new DateTimeOffset(2026, 5, 8, 14, 20, 0, TimeSpan.Zero));
        var projection = legacyProjection with
        {
            EvidencePackages =
            [
                new OperationsEvidencePackageSummaryDto(
                    "period-lock-reopen:2026-05",
                    "Period lock and reopen evidence",
                    EvidenceStatusDto.Missing,
                    false,
                    "Period lock evidence is not complete.",
                    "OperationsContinuity",
                    1,
                    2,
                    1,
                    [evidence],
                    ["Close the workflow"])
            ],
            ReconciliationLanes =
            [
                new OperationsReconciliationLaneSummaryDto(
                    "mbs-factor-reconciliation",
                    "MBS factor reconciliation",
                    OperationsReconciliationLaneStatusDto.ReviewRequired,
                    false,
                    1,
                    "MBS factor reconciliation has one open break.",
                    "FundReconciliation",
                    [evidence],
                    ["Resolve or assign MBS factor breaks"])
            ],
            BreakCases =
            [
                new OperationsBreakCaseDto(
                    "break-finops-factor",
                    "mbs-factor-check",
                    "MbsFactor",
                    "Warning",
                    "InReview",
                    "fund-controller",
                    new DateOnly(2026, 5, 9),
                    "custodian",
                    "ledger",
                    125_000m,
                    124_500m,
                    -500m,
                    null,
                    "MBS",
                    "Resolve factor variance and retain custodian factor evidence.",
                    [evidence],
                    new OperationsContinuityCorrelationKeysDto(
                        RunId: "run-finops-queue",
                        ReconciliationCaseId: "case-finops-factor"),
                    EscalationLevel: "Level 2",
                    SlaState: "Warning",
                    BlockedOutputs: ["Report-pack release"])
            ],
            CloseChecklist =
            [
                new OperationsCloseChecklistTaskDto(
                    "close-gate-reconciliation",
                    OperationsGateKeyDto.Reconciliation,
                    "Reconciliation close gate",
                    "accounting-operator",
                    "Evidence link and gate completion audit",
                    1,
                    new DateOnly(2026, 5, 15),
                    new DateOnly(2026, 5, 10),
                    "Pending",
                    null,
                    "ev-finops-queue",
                    "OperationsContinuity",
                    false,
                    null,
                    null)
            ],
            Approvals =
            [
                new OperationsApprovalDto(
                    "approval-finops-queue",
                    OperationsApprovalStateDto.ReviewerAssigned,
                    "fund-ops",
                    "controller",
                    "Controller review is pending.",
                    new DateTimeOffset(2026, 5, 9, 16, 0, 0, TimeSpan.Zero),
                    null,
                    [evidence])
            ]
        };

        var json = JsonSerializer.Serialize(projection, options);
        var actual = JsonSerializer.Deserialize<GovernanceLifecycleProjectionDto>(json, options);

        actual.Should().NotBeNull();
        actual!.EvidencePackages.Should().ContainSingle(package => package.PackageId == "period-lock-reopen:2026-05");
        actual.ReconciliationLanes.Should().ContainSingle(lane => lane.LaneId == "mbs-factor-reconciliation" && lane.BreakCount == 1);
        actual.BreakCases.Should().ContainSingle(breakCase => breakCase.BlockedOutputs!.Contains("Report-pack release"));
        actual.CloseChecklist.Should().ContainSingle(task => task.TaskId == "close-gate-reconciliation");
        actual.Approvals.Should().ContainSingle(approval => approval.Status == OperationsApprovalStateDto.ReviewerAssigned);
    }

    [Fact]
    public void EvidenceVaultLookupContracts_ShouldRoundTripAccountingRecordLinkage()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var linkage = new EvidenceSubjectLinkageDto(
            EvidenceSubject: "accounting-record/workflow-2026-05",
            RunId: null,
            PeriodId: "2026-05",
            ReportPackId: "report-pack-2026-05",
            ReconciliationCaseId: "case-77",
            AccountingRecordId: "workflow-2026-05");
        var lookup = new EvidenceVaultLookupRequestDto(
            EvidenceSubject: null,
            RunId: null,
            PeriodId: null,
            ReportPackId: null,
            ReconciliationCaseId: null,
            AccountingRecordId: "workflow-2026-05");

        var linkageJson = JsonSerializer.Serialize(linkage, options);
        var lookupJson = JsonSerializer.Serialize(lookup, options);
        var actualLinkage = JsonSerializer.Deserialize<EvidenceSubjectLinkageDto>(linkageJson, options);
        var actualLookup = JsonSerializer.Deserialize<EvidenceVaultLookupRequestDto>(lookupJson, options);

        linkageJson.Should().Contain("\"accountingRecordId\":\"workflow-2026-05\"");
        lookupJson.Should().Contain("\"accountingRecordId\":\"workflow-2026-05\"");
        actualLinkage.Should().NotBeNull();
        actualLinkage!.AccountingRecordId.Should().Be("workflow-2026-05");
        actualLinkage.EvidenceSubject.Should().Be("accounting-record/workflow-2026-05");
        actualLookup.Should().NotBeNull();
        actualLookup!.AccountingRecordId.Should().Be("workflow-2026-05");
    }

    [Fact]
    public void EvidenceVaultDocumentQueueContracts_ShouldRoundTripForDesktopReadModels()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var document = new EvidenceDocumentDto(
            DocumentId: "doc:ev-close-2026-05",
            FileName: "operating-bank-statement.csv",
            Classification: EvidenceDocumentClassificationDto.BankEvidence,
            SourceHashSha256: new string('a', 64),
            ReceivedAt: DateTimeOffset.Parse("2026-05-09T13:00:00Z"),
            SourceChannel: "upload",
            Actor: "fund-controller",
            TenantId: "tenant-alpha",
            Scope: "fund-alpha",
            ExtractionStatus: EvidenceExtractionStatusDto.NeedsReview,
            ObjectLinks:
            [
                new EvidenceDocumentLinkDto(
                    EvidenceDocumentLinkKindDto.CloseTask,
                    "close-task:cash-support",
                    "Cash support",
                    "/workstation/accounting/close/tasks/cash-support",
                    "blocks-close-readiness")
            ],
            ReviewerState: new EvidenceDocumentReviewStateDto(
                EvidenceDocumentReviewStatusDto.NeedsReview,
                "fund-controller",
                null,
                "Statement amount needs review."),
            AuditTrail:
            [
                new EvidenceDocumentAuditEventDto(
                    DateTimeOffset.Parse("2026-05-09T13:00:00Z"),
                    "fund-controller",
                    "DocumentIntakeRetained",
                    "Retained BankEvidence document.",
                    "ev-close-2026-05")
            ])
        {
            ManifestRoute = "/workstation/evidence/_vault/ev-close-2026-05/manifest.json",
            ExtractorId = "manual-metadata-v1"
        };
        var entry = new EvidenceVaultDocumentEntryDto(
            Document: document,
            VaultId: "ev-close-2026-05",
            SubjectKind: "accounting-record",
            SubjectId: "workflow-2026-05",
            ManifestRoute: "/workstation/evidence/_vault/ev-close-2026-05/manifest.json",
            RetainedAt: DateTimeOffset.Parse("2026-05-09T13:00:00Z"),
            StorageKind: "file-bundle",
            OpenRequestCount: 1,
            SupportRequests: []);
        var query = new EvidenceVaultDocumentQueryDto(
            Classification: EvidenceDocumentClassificationDto.BankEvidence,
            ReviewStatus: EvidenceDocumentReviewStatusDto.NeedsReview,
            LinkKind: EvidenceDocumentLinkKindDto.CloseTask,
            ObjectId: "close-task:cash-support",
            SubjectKind: "accounting-record",
            SubjectId: "workflow-2026-05",
            TenantId: "tenant-alpha",
            Scope: "fund-alpha");
        var intakeSource = new EvidenceDocumentIntakeSourceDto(
            EvidenceDocumentIntakeSourceKindDto.LocalFile,
            Path: @"D:\imports\operating-bank-statement.csv",
            Uri: "file://imports/operating-bank-statement.csv",
            DisplayName: "Operating bank statement",
            ExpectedContentHashSha256: new string('a', 64));
        var manifestSnapshot = new EvidenceManifestDto(
            ManifestId: "ev-close-2026-05",
            FrozenAt: DateTimeOffset.Parse("2026-05-09T13:01:00Z"),
            PackageKind: "accounting-record",
            PackageId: "workflow-2026-05",
            ContentHashSha256: new string('c', 64),
            Documents: [document],
            Requests:
            [
                new EvidenceRequestDto(
                    RequestId: "support-request:validationissue:operating-bank-statement",
                    RequestKind: "ValidationIssue",
                    Severity: EvidenceValidationSeverityDto.Warning,
                    Status: "Open",
                    Summary: "Statement amount needs review.",
                    TargetKind: "close",
                    TargetId: "workflow-2026-05",
                    BlockedOutput: null)
            ],
            ObjectLinks: document.ObjectLinks);
        var vaultIdentity = new EvidenceVaultIdentityDto(
            VaultId: "ev-close-2026-05",
            SubjectKind: "accounting-record",
            SubjectId: "workflow-2026-05",
            ManifestPath: "workstation/evidence/_vault/ev-close-2026-05/manifest.json",
            ManifestRoute: "/workstation/evidence/_vault/ev-close-2026-05/manifest.json",
            RetainedAt: DateTimeOffset.Parse("2026-05-09T13:01:00Z"),
            ContentHashSha256: manifestSnapshot.ContentHashSha256,
            SchemaVersion: 1,
            StorageKind: "file-bundle")
        {
            Documents = [document],
            ManifestSnapshot = manifestSnapshot
        };
        var closePackage = new OperationsClosePackagePublicationDto(
            ClosePackageId: "close-package-2026-05",
            ReportPackId: "report-pack-2026-05",
            RetainedManifestId: "manifest-2026-05",
            RetainedManifestRoute: "/workstation/accounting/operations-continuity/workflow-2026-05/close-package/manifest",
            EvidenceHash: new string('b', 64),
            PublishedAtUtc: DateTimeOffset.Parse("2026-05-09T16:00:00Z"),
            PublishedBy: "fund-controller",
            SignOffRationale: "Close package retained with vault evidence.",
            EvidenceLinks:
            [
                new OperationsEvidenceLinkDto(
                    document.DocumentId,
                    "Vault document: operating-bank-statement.csv",
                    document.ManifestRoute,
                    document.SourceChannel,
                    document.ReceivedAt)
            ],
            ChecklistControlApprovals: [],
            DocumentSnapshots: [document],
            ManifestSnapshot: manifestSnapshot);

        var entryJson = JsonSerializer.Serialize(entry, options);
        var queryJson = JsonSerializer.Serialize(query, options);
        var intakeSourceJson = JsonSerializer.Serialize(intakeSource, options);
        var vaultIdentityJson = JsonSerializer.Serialize(vaultIdentity, options);
        var closePackageJson = JsonSerializer.Serialize(closePackage, options);
        var actualEntry = JsonSerializer.Deserialize<EvidenceVaultDocumentEntryDto>(entryJson, options);
        var actualQuery = JsonSerializer.Deserialize<EvidenceVaultDocumentQueryDto>(queryJson, options);
        var actualIntakeSource = JsonSerializer.Deserialize<EvidenceDocumentIntakeSourceDto>(intakeSourceJson, options);
        var actualVaultIdentity = JsonSerializer.Deserialize<EvidenceVaultIdentityDto>(vaultIdentityJson, options);
        var actualClosePackage = JsonSerializer.Deserialize<OperationsClosePackagePublicationDto>(closePackageJson, options);

        entryJson.Should().Contain("\"classification\":\"BankEvidence\"");
        entryJson.Should().Contain("\"linkKind\":\"CloseTask\"");
        queryJson.Should().Contain("\"reviewStatus\":\"NeedsReview\"");
        intakeSourceJson.Should().Contain("\"sourceKind\":\"LocalFile\"");
        vaultIdentityJson.Should().Contain("\"manifestSnapshot\"");
        vaultIdentityJson.Should().Contain("\"packageKind\":\"accounting-record\"");
        closePackageJson.Should().Contain("\"documentSnapshots\"");
        closePackageJson.Should().Contain("\"manifestSnapshot\"");
        actualEntry.Should().NotBeNull();
        actualEntry!.Document.ObjectLinks.Should().ContainSingle(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.CloseTask &&
            link.ObjectId == "close-task:cash-support");
        actualEntry.Document.ReviewerState.Status.Should().Be(EvidenceDocumentReviewStatusDto.NeedsReview);
        actualQuery.Should().NotBeNull();
        actualQuery!.TenantId.Should().Be("tenant-alpha");
        actualQuery.LinkKind.Should().Be(EvidenceDocumentLinkKindDto.CloseTask);
        actualIntakeSource.Should().NotBeNull();
        actualIntakeSource!.SourceKind.Should().Be(EvidenceDocumentIntakeSourceKindDto.LocalFile);
        actualIntakeSource.Uri.Should().Be("file://imports/operating-bank-statement.csv");
        actualVaultIdentity.Should().NotBeNull();
        actualVaultIdentity!.ManifestSnapshot.Should().NotBeNull();
        actualVaultIdentity.ManifestSnapshot!.Documents.Should().ContainSingle(snapshot =>
            snapshot.DocumentId == document.DocumentId);
        actualVaultIdentity.ManifestSnapshot.Requests.Should().ContainSingle(request =>
            request.TargetKind == "close" &&
            request.TargetId == "workflow-2026-05");
        actualClosePackage.Should().NotBeNull();
        actualClosePackage!.DocumentSnapshots.Should().ContainSingle(snapshot =>
            snapshot.DocumentId == document.DocumentId &&
            snapshot.SourceHashSha256 == document.SourceHashSha256);
        actualClosePackage.ManifestSnapshot.Should().NotBeNull();
        actualClosePackage.ManifestSnapshot!.ManifestId.Should().Be(manifestSnapshot.ManifestId);
        actualClosePackage.ManifestSnapshot.Documents.Should().ContainSingle(snapshot =>
            snapshot.DocumentId == document.DocumentId);
    }
}
