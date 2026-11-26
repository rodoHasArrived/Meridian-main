# Ui.Shared Decomposition Inventory

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-11-25

Phase 0 evidence for the [Ui.Shared Decomposition Plan](ui-shared-decomposition-plan.md). This is a
reviewed snapshot, not a CI-maintained file. Refresh it before each phase with:

```bash
python3 build/scripts/ui-shared-inventory.py
```

The scan is heuristic: it matches declared type names as whole words in comment-stripped source.
Common short names (for example `Snapshot`) can inflate edge counts slightly; the compiler is the
authority when a move is made.

## Snapshot (2026-11-26)

| Cluster | Files | Lines |
| --- | ---: | ---: |
| Adapter | 293 | 82926 |
| Accounting | 94 | 41149 |
| Reporting | 63 | 35608 |
| Workstation | 132 | 28975 |
| Operations | 60 | 21745 |
| Evidence | 38 | 15762 |
| SecurityMaster | 20 | 6108 |
| Other | 27 | 3693 |

### Cross-cluster references (distinct type uses, source -> target)

| Source | Target | Type uses |
| --- | --- | ---: |
| Workstation | Adapter | 89 |
| Workstation | Accounting | 85 |
| Workstation | Reporting | 66 |
| Workstation | Operations | 57 |
| Accounting | Workstation | 44 |
| Reporting | Accounting | 35 |
| Operations | Workstation | 33 |
| Accounting | Reporting | 31 |
| Operations | Adapter | 24 |
| Reporting | Evidence | 21 |
| Workstation | Evidence | 21 |
| Reporting | Adapter | 16 |
| Operations | Evidence | 15 |
| Accounting | Adapter | 14 |
| Reporting | Workstation | 14 |
| Evidence | Reporting | 12 |
| Evidence | Adapter | 10 |
| Evidence | Accounting | 8 |
| Operations | Accounting | 8 |
| Accounting | Evidence | 7 |
| Evidence | Workstation | 5 |
| Workstation | Other | 5 |
| Workstation | SecurityMaster | 5 |
| Other | Operations | 3 |
| Accounting | Operations | 3 |
| Evidence | Operations | 2 |
| Evidence | Other | 2 |
| SecurityMaster | Accounting | 2 |
| Other | Accounting | 1 |
| SecurityMaster | Evidence | 1 |
| SecurityMaster | Adapter | 1 |
| Other | Workstation | 1 |

### Movable files that reference the adapter layer (50)

These must have the adapter dependency moved down before the file can leave Ui.Shared.

- `src/Meridian.Ui.Shared/Evidence/EvidenceContributors.cs`: ApprovalState, HttpContextWorkstationTenantContextAccessor, OperationsContinuityWorkflowDto
- `src/Meridian.Ui.Shared/Evidence/EvidenceSubjectResolver.cs`: HttpContextWorkstationTenantContextAccessor
- `src/Meridian.Ui.Shared/Evidence/FileEvidenceArtifactStore.StorageQuota.cs`: WorkstationOperationsJsonContext
- `src/Meridian.Ui.Shared/Evidence/ReconciliationEvidenceContributor.StatementRuns.cs`: IWorkstationTenantContextAccessor
- `src/Meridian.Ui.Shared/Evidence/StrategyRunEvidenceContributor.cs`: EvidenceEndpoints, HttpContextWorkstationTenantContextAccessor, IWorkstationTenantContextAccessor, WorkstationTenantContext
- `src/Meridian.Ui.Shared/Services/AccountingClosePostingWorkbenchBridge.cs`: ApprovalState, OperationsApprovalDto
- `src/Meridian.Ui.Shared/Services/AccountingConfigurationService.ActivationValidation.cs`: ApprovalState
- `src/Meridian.Ui.Shared/Services/AccountingConfigurationService.RuleValidation.cs`: ApprovalState
- `src/Meridian.Ui.Shared/Services/AmountPassportFacts.cs`: LedgerEndpoints
- `src/Meridian.Ui.Shared/Services/AuditTrailExplorerService.cs`: OperationsTimelineEntryDto
- `src/Meridian.Ui.Shared/Services/AuditTrailHttpAuthority.cs`: EndpointAuthorization, FundAccountEndpoints, HttpContextWorkstationTenantContextAccessor
- `src/Meridian.Ui.Shared/Services/AutomatedJournalDraftIntakeService.cs`: PeriodAdjustmentJsonContext
- `src/Meridian.Ui.Shared/Services/AutomatedJournalScheduleStore.cs`: AutomatedJournalScheduleJsonContext
- `src/Meridian.Ui.Shared/Services/BackfillCoordinator.cs`: ValidateRequest
- `src/Meridian.Ui.Shared/Services/BondWorkflowServiceRegistration.cs`: IWorkstationTenantContextAccessor
- `src/Meridian.Ui.Shared/Services/CapitalAccountWorkbenchService.cs`: ApprovalState
- `src/Meridian.Ui.Shared/Services/CapitalCallFundingIntake.cs`: ApprovalState
- `src/Meridian.Ui.Shared/Services/ClosePublicationReadinessGuard.cs`: IWorkstationTenantContextAccessor
- `src/Meridian.Ui.Shared/Services/CollateralExposureService.cs`: HttpContextWorkstationTenantContextAccessor
- `src/Meridian.Ui.Shared/Services/CoveredCall/CoveredCallBacktestService.cs`: CoveredCallBacktestRequest, CoveredCallChainPreview, CoveredCallChainPreviewRequest, CoveredCallChainRow, CoveredCallJsonContext, CoveredCallRunHandle, CoveredCallRunResult, CoveredCallRunScope, CoveredCallRunStatusDto, CoveredCallRunSummary, ValidateRequest
- `src/Meridian.Ui.Shared/Services/CoveredCall/CoveredCallRunProjection.cs`: CoveredCallBacktestRequest, CoveredCallChainRow, CoveredCallEquityPoint, CoveredCallMetricsDto, CoveredCallOpenPositionDto, CoveredCallRunResult, CoveredCallRunScope, CoveredCallTradeDto, OverwriteScoringModeDto
- `src/Meridian.Ui.Shared/Services/CoveredCall/ICoveredCallBacktestService.cs`: CoveredCallBacktestRequest, CoveredCallChainPreview, CoveredCallChainPreviewRequest, CoveredCallRunHandle, CoveredCallRunResult, CoveredCallRunScope, CoveredCallRunStatusDto, CoveredCallRunSummary
- `src/Meridian.Ui.Shared/Services/EntityLifecycleExternalReviewEvidenceAuthority.cs`: HttpContextWorkstationTenantContextAccessor
- `src/Meridian.Ui.Shared/Services/EntityTimelineAccessAuthority.cs`: EndpointAuthorization, HttpContextWorkstationTenantContextAccessor
- `src/Meridian.Ui.Shared/Services/ExecutionWorkflowWorker.cs`: ExecutionWorkflowJsonContext
- `src/Meridian.Ui.Shared/Services/FamilyOfficeReadService.cs`: CapitalCommitmentDto, FamilyAccountSummaryDto, FamilyAssetSummaryDto, FamilyBalanceSheetDto, FamilyEntityDto, FamilyOfficeBalanceSheetResponseDto, FamilyOfficeClientOptionDto, FamilyOfficeEndpointStateDto, FamilyOfficeEntitiesResponseDto, FamilyOfficeEvidenceLinkDto, FamilyOfficeOverviewDto, FamilyOfficeOwnershipGraphResponseDto, FamilyOfficeReadinessDto, FamilyOwnershipEdgeDto, FamilyOwnershipGraphDto, FamilyOwnershipNodeDto, PrivateAssetSummaryDto
- `src/Meridian.Ui.Shared/Services/FundOperationsWorkspaceReadService.cs`: ApprovalState, OperationsContinuityWorkflowDto, OperationsTimelineEntryDto, ReconciliationState
- `src/Meridian.Ui.Shared/Services/IntercompanyAgreementAuthorities.cs`: EndpointAuthorization, HttpContextWorkstationTenantContextAccessor
- `src/Meridian.Ui.Shared/Services/IntercompanyP1P2ServiceRegistration.cs`: EndpointAuthorization, HttpContextWorkstationTenantContextAccessor
- `src/Meridian.Ui.Shared/Services/ManualJournalEntryWorkbenchService.Intercompany.cs`: ApprovalState
- `src/Meridian.Ui.Shared/Services/ManualJournalEntryWorkbenchService.cs`: ApprovalState
- `src/Meridian.Ui.Shared/Services/OmsIntegrationService.cs`: IOmsIntegrationApiHandler, OmsAdapterBoundary, OmsAdapterDiagnostics, OmsAdapterKind, OmsInboundMessage, OmsIngestionResult, OmsIntegrationAuditEntry, OmsIntegrationDirection, OmsIntegrationScope, OmsKeyRotationRequest, OmsKeyRotationResult, OmsRequestSignatureInput, OmsRequestSignatureResult, OmsRetryPolicy, OmsSyncConflictResult, OmsSyncMode, OmsSyncRequest
- `src/Meridian.Ui.Shared/Services/OperationsContinuityReconciliationBridge.cs`: ApprovalState, OperationsBreakCaseDto, OperationsContinuityWorkflowDto, OperationsTimelineEntryDto
- `src/Meridian.Ui.Shared/Services/OperationsReportPackAuthority.cs`: OperationsContinuityWorkflowDto
- `src/Meridian.Ui.Shared/Services/PeriodAdjustmentPreparationService.cs`: PeriodAdjustmentJsonContext
- `src/Meridian.Ui.Shared/Services/PostedLedgerAmountProvenanceService.Passport.cs`: LedgerEndpoints
- `src/Meridian.Ui.Shared/Services/PostedLedgerAmountProvenanceService.cs`: LedgerEndpoints
- `src/Meridian.Ui.Shared/Services/PrivateCapitalEventIntakes.cs`: ApprovalState
- `src/Meridian.Ui.Shared/Services/PrivateCapitalFundEventCommandCenterService.cs`: ApprovalState
- `src/Meridian.Ui.Shared/Services/PrivateCapitalLifecycleSource.cs`: CapitalActivityDto, CapitalCommitmentDto, FamilyOfficeJsonContext, PrivateCapitalLifecycleDocumentDto, PrivateCapitalSnapshotDto
- `src/Meridian.Ui.Shared/Services/ReportWriterDatasetSourceService.cs`: ReconciliationState
- `src/Meridian.Ui.Shared/Services/ReportingCertifiedArtifactProducer.cs`: ReportingCertifiedArtifactJsonContext, ReportingEvidenceAppendixDocument, ReportingEvidenceVaultDocument, ReportingRetainedGridColumn, ReportingRetainedGridDocument, ReportingRetainedGridRow, ReportingRetainedPreviewDocument, ReportingRetainedPreviewPrimaryArtifact
- `src/Meridian.Ui.Shared/Services/ReportingDeliveryReadModelSecurity.cs`: EndpointAuthorization, HttpContextWorkstationTenantContextAccessor
- `src/Meridian.Ui.Shared/Services/SecurityMasterWorkbenchQueryService.DownstreamImpact.cs`: HttpContextWorkstationTenantContextAccessor
- `src/Meridian.Ui.Shared/Services/StatementReconciliationCaseworkHandoffService.cs`: OperationsContinuityWorkflowDto
- `src/Meridian.Ui.Shared/Services/StatementReconciliationIntakeAuthority.cs`: BrokerIntakeState, OperationsContinuityWorkflowDto, OperationsTransitionRequestDto
- `src/Meridian.Ui.Shared/Services/TradingOperatorReadinessService.cs`: ExecutionEndpoints
- `src/Meridian.Ui.Shared/Services/WorkstationServiceCollectionExtensions.cs`: FundScopedWriteTenantOptions, HttpContextWorkstationTenantContextAccessor, IFundProfileTenantGuard, IOmsIntegrationApiHandler, IQuoteStreamBroadcaster, IWorkstationTenantContextAccessor, QuoteStreamBroadcaster, QuoteStreamOptions, RegistryFundProfileTenantGuard, ReplayScanLifetime, ReportRunStreamBroadcaster, StreamConnectionRegistry, WorkstationFundScopeTenantAccessor
- `src/Meridian.Ui.Shared/Services/WorkstationWorkflowReadScope.cs`: EndpointAuthorization
- `src/Meridian.Ui.Shared/Services/WorkstationWorkflowSummaryService.cs`: ApprovalState, BrokerIntakeState, LedgerPostingState, OperationsContinuityWorkflowDto, ReconciliationState

### Internal types used outside their own cluster (45)

- `AccountingPreparerAttestationDisclosure` (Accounting) used by: Reporting
- `AlpacaFeedConfiguration` (Operations) used by: Workstation
- `AmountPassportFacts` (Workstation) used by: Accounting, Operations, Reporting
- `AmountPassportScopeMismatchException` (Accounting) used by: Operations, Reporting
- `ApprovalDecision` (Workstation) used by: Accounting
- `AutomatedJournalScheduleJsonContext` (Adapter) used by: Accounting
- `Basis` (Workstation) used by: Accounting, Operations
- `CapitalCallDefaultInterestDraftProducer` (Workstation) used by: Accounting
- `CapitalCallFundingDraftProducer` (Reporting) used by: Accounting
- `CapitalCallIssuanceDraftProducer` (Reporting) used by: Accounting
- `ConsolidationChartValidation` (Workstation) used by: Accounting
- `CoveredCallJsonContext` (Adapter) used by: Operations
- `CrossProcessFileLock` (Workstation) used by: Accounting
- `DistributionDeclarationDraftProducer` (Workstation) used by: Accounting
- `DurableStatementReconciliationAuthorityRegistration` (Evidence) used by: Workstation
- `Entry` (Workstation) used by: Accounting, Operations, Reporting
- `Envelope` (Evidence) used by: SecurityMaster, Workstation
- `ExecutionAuditPublicProjection` (Operations) used by: Workstation
- `ExecutionWorkflowJsonContext` (Adapter) used by: Operations
- `FamilyOfficeJsonContext` (Adapter) used by: Workstation
- `ManualJournalMutationJsonContext` (Accounting) used by: Workstation
- `Observation` (Workstation) used by: Evidence, Operations
- `PaperSessionDailyReconciliation` (Workstation) used by: Operations
- `Payload` (Accounting) used by: Evidence, Reporting, SecurityMaster, Workstation
- `PeriodAdjustmentJsonContext` (Adapter) used by: Accounting, Workstation
- `PrivateCapitalEventProduction` (Workstation) used by: Accounting
- `ProviderCredentialProbe` (Operations) used by: Workstation
- `ReconciliationBreakQueueProjection` (Accounting) used by: Operations, Reporting
- `RecurringJournalCalculationMath` (Workstation) used by: Operations
- `RegistryFundProfileTenantGuard` (Adapter) used by: Workstation
- `ReplayScanLifetime` (Adapter) used by: Workstation
- `ReportingCertifiedArtifactJsonContext` (Adapter) used by: Reporting
- `ReportingCertifiedLedgerPresentationBinding` (Reporting) used by: Accounting, Workstation
- `ReportingEvidenceAppendixDocument` (Adapter) used by: Reporting
- `ReportingEvidenceVaultDocument` (Adapter) used by: Reporting
- `ReportingRetainedGridColumn` (Adapter) used by: Reporting
- `ReportingRetainedGridDocument` (Adapter) used by: Reporting
- `ReportingRetainedGridRow` (Adapter) used by: Reporting
- `ReportingRetainedPreviewDocument` (Adapter) used by: Reporting
- `ReportingRetainedPreviewPrimaryArtifact` (Adapter) used by: Reporting
- `SeriesKey` (Workstation) used by: Operations
- `ServiceProviderReportingAuthoritativeSource` (Workstation) used by: Reporting
- `Session` (Accounting) used by: Operations, Workstation
- `Snapshot` (Evidence) used by: Accounting, Operations, Reporting, Workstation
- `WorkstationOperationsJsonContext` (Adapter) used by: Evidence

### Assemblies granting InternalsVisibleTo("Meridian.Ui.Shared")

- `src/Meridian.Application/GlobalUsings.cs`
