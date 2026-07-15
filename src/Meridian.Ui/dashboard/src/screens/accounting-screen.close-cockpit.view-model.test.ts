import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  buildAccountingWorkflowLaunchViewState,
  buildCloseCommandCenterViewState,
  useAccountingCloseReportPackageViewModel,
} from "@/screens/accounting-screen.close-cockpit.view-model";
import type { AccountingCloseReportPackageServices } from "@/screens/accounting-screen.view-model";
import type {
  AccountingReportPackageBundle,
  AccountingSystemProvider,
  AccountingSystemReconciliationSummary,
  AccountingWorkspaceResponse,
  ClosePeriodPlan,
  DailyValuationScheduleWorkItem,
  FinancialOperationsCommandCenter,
  MultiAssetCoverageSummary,
  OperationsContinuityWorkflow,
  ReconciliationBreakQueueItem,
  ReportExportArtifactManifest,
} from "@/types";

const reconciliationQueue: AccountingWorkspaceResponse["reconciliationQueue"] = [
  {
    runId: "run-42",
    strategyName: "Paper Index Mean Reversion",
    mode: "paper",
    status: "Running",
    lastUpdated: "3m ago",
    breakCount: 2,
    openBreakCount: 1,
    reconciliationStatus: "BreaksOpen"
  },
  {
    runId: "run-57",
    strategyName: "Intraday Vol Carry",
    mode: "paper",
    status: "Paused",
    lastUpdated: "7m ago",
    breakCount: 1,
    openBreakCount: 0,
    reconciliationStatus: "Resolved"
  }
];

const breakQueue: ReconciliationBreakQueueItem[] = [
  {
    breakId: "run-42:cash",
    runId: "run-42",
    strategyName: "Paper Index Mean Reversion",
    category: "AmountMismatch",
    status: "Open",
    variance: 500,
    reason: "Cash variance over tolerance.",
    assignedTo: null,
    detectedAt: "2026-01-01T00:00:00Z",
    lastUpdatedAt: "2026-01-01T00:00:00Z",
    reviewedBy: null,
    reviewedAt: null,
    resolvedBy: null,
    resolvedAt: null,
    resolutionNote: null
  },
  {
    breakId: "run-57:fees",
    runId: "run-57",
    strategyName: "Intraday Vol Carry",
    category: "FeeMismatch",
    status: "Resolved",
    variance: 0,
    reason: "Fee variance resolved.",
    assignedTo: "ops.gov",
    detectedAt: "2026-01-02T00:00:00Z",
    lastUpdatedAt: "2026-01-02T00:00:00Z",
    reviewedBy: "ops.gov",
    reviewedAt: "2026-01-02T00:05:00Z",
    resolvedBy: "ops.gov",
    resolvedAt: "2026-01-02T00:10:00Z",
    resolutionNote: "Reviewed in accounting panel.",
    exceptionRoute: "fund-ops-review",
    toleranceProfileId: "fee-variance-ops",
    toleranceBand: 100,
    requiredSignoffRole: "Fund operations lead",
    signoffStatus: "Pending Signoff",
    routingTarget: "FundTrialBalance",
    routingDetail: "Open the accounting trial balance for evidence review.",
    recommendedAction: "Review matched fee entries before closing.",
    breakExplanation: {
      summary: "Provider fees and Meridian ledger fees now match after operator review.",
      sourceSystems: ["Provider activity", "Meridian ledger"],
      probableCause: "The provider posted fees after the first reconciliation pass.",
      ledgerImpact: "No remaining ledger adjustment is required.",
      suggestedNextAction: "Attach the provider activity evidence before close sign-off.",
      evidenceLinks: ["/accounting/evidence/provider-fees"]
    }
  }
];

const accountingWorkspace: AccountingWorkspaceResponse = {
  metrics: [],
  reconciliationQueue,
  breakQueue,
  cashFlow: {
    totalCash: 120000,
    totalLedgerCash: 119500,
    netVariance: 500,
    totalFinancing: 0,
    runsWithCashSignals: 2,
    runsWithCashVariance: 1,
    tone: "warning",
    summary: "Cash variance is under controller review."
  },
  reporting: {
    profileCount: 1,
    recommendedProfiles: ["controller"],
    reportPackTargets: ["monthly-close"],
    summary: "Controller reporting profile is available.",
    profiles: []
  },
  controlCenter: {
    closeReadiness: "Blocked",
    portfolioFilterOptions: ["all-portfolios"],
    accountFilterOptions: ["fund-alpha"],
    blockerSeverityDistribution: [{ severity: "Critical", count: 1 }],
    agingCurves: [],
    ownerWorkload: [],
    slaBreachCount: 1,
    trendSnapshots: [],
    drillLinks: [],
    alerts: [{ tone: "danger", message: "Ledger validation is blocking close." }]
  }
};

const closeWorkflow: OperationsContinuityWorkflow = {
  workflowId: "workflow-close-1",
  fundAccountId: "fund-alpha",
  periodId: "2026-05",
  securityMasterSnapshotId: "snapshot-1",
  brokerSource: "broker-fixture",
  status: "Blocked",
  version: 3,
  createdAtUtc: "2026-05-31T00:00:00Z",
  updatedAtUtc: "2026-06-01T02:00:00Z",
  brokerIntakeState: "Complete",
  securityMasterState: "Complete",
  ledgerPostingState: "Drafted",
  reconciliationState: "ExceptionsOpen",
  approvalState: "Pending",
  gates: [
    {
      gateKey: "LedgerPosting",
      displayName: "Ledger posting",
      status: "Blocked",
      isRequired: true,
      description: "Ledger validation must pass before close.",
      blockers: [
        {
          code: "LEDGER_VALIDATION_REQUIRED",
          message: "Ledger posting requires a balanced journal draft.",
          gate: "LedgerPosting",
          severity: "Critical",
          evidenceLinks: []
        }
      ],
      nextActions: [],
      completedAtUtc: null,
      completedBy: null
    }
  ],
  nextActions: [
    { code: "FIX_LEDGER", label: "Resolve ledger blockers", route: "/accounting/ledger", gate: "LedgerPosting" }
  ],
  timeline: [],
  breakCases: [
    {
      breakId: "break-close-1",
      checkId: "cash-check",
      category: "Cash",
      severity: "Critical",
      status: "Open",
      owner: null,
      dueDate: "2026-06-02",
      expectedSource: "custodian",
      actualSource: null,
      expectedAmount: 100,
      actualAmount: null,
      variance: 100,
      securityId: null,
      symbol: null,
      suggestedAction: "Attach custodian source file.",
      evidenceLinks: []
    }
  ],
  ledgerPreview: {
    previewId: "ledger-preview-1",
    status: "Drafted",
    ledgerBatchId: null,
    generatedAtUtc: "2026-06-01T01:00:00Z",
    evidenceLinks: []
  },
  approvals: [
    {
      approvalId: "approval-1",
      status: "Pending",
      operator: "controller",
      reviewer: null,
      rationale: null,
      submittedAtUtc: "2026-06-01T01:30:00Z",
      decidedAtUtc: null,
      evidenceLinks: []
    }
  ],
  reportPackReadiness: {
    isReady: false,
    reportPackId: null,
    blockingReason: "Report pack is waiting on ledger validation.",
    evidenceLinks: []
  },
  closeChecklist: [
    {
      taskId: "source-file",
      gate: "BrokerIngest",
      label: "Source file retained",
      owner: "controller",
      requiredEvidence: "Custodian source file",
      dueDate: "2026-06-02",
      requiredApprovalCount: 1,
      expiresOn: null,
      status: "Open",
      blockingReason: "Source file missing.",
      evidencePointer: null,
      remediationRoute: "/accounting/reconciliation",
      canAcknowledge: false,
      acknowledgedAtUtc: null,
      acknowledgedBy: null
    }
  ],
  closeReadiness: {
    isReadyToClose: false,
    severity: "Critical",
    score: 42,
    components: [],
    blockers: [
      {
        code: "SOURCE_FILE_MISSING",
        category: "Evidence",
        severity: "Warning",
        message: "Custodian source file is missing.",
        gate: "BrokerIngest",
        routeHint: "/accounting/reconciliation"
      }
    ],
    nextActions: []
  },
  closePackage: null,
  accountingRecordSummary: {
    recordId: "acct-record-1",
    isAuditReady: false,
    completeCategoryCount: 1,
    requiredCategoryCount: 3,
    summary: "Accounting record is incomplete.",
    evidenceCategories: [
      {
        key: "source-records",
        label: "Retained source records",
        isComplete: false,
        status: "Missing",
        routeHint: "/reporting/evidence",
        evidenceLinks: [],
        requiredEvidence: ["custodian activity file"]
      },
      {
        key: "ledger-evidence",
        label: "Ledger evidence",
        isComplete: true,
        status: "Complete",
        routeHint: "/accounting/ledger",
        evidenceLinks: [],
        requiredEvidence: ["journal preview"]
      }
    ],
    evidenceLinks: []
  },
  evidenceLinks: [],
  blockers: [
    {
      code: "CLOSE_BLOCKED",
      message: "Close workflow has unresolved blockers.",
      gate: "LedgerPosting",
      severity: "Critical",
      evidenceLinks: []
    }
  ]
};

const closePeriodPlan: ClosePeriodPlan = {
  closePlanId: "close-plan-alpha-202605",
  workflowVersion: 19,
  fundProfileId: "fund-alpha",
  ledgerBookId: "book-alpha",
  periodId: "2026-05",
  periodStart: "2026-05-01",
  periodEnd: "2026-05-31",
  closeDueDate: "2026-06-05",
  isPeriodLocked: false,
  materialityPolicy: {
    policyId: "materiality-alpha",
    amountThreshold: 2500,
    percentThreshold: 0.5,
    currency: "USD",
    reviewRole: "controller",
    requiresLateAdjustmentApproval: true
  },
  tasks: [
    {
      taskId: "task-nav",
      displayName: "Finalize NAV package",
      status: "ReadyForSignOff",
      owner: "fund-accounting",
      dueDate: "2026-06-04",
      dependencies: [
        {
          dependencyId: "dependency-recon",
          dependsOnTaskId: "task-reconciliation",
          reason: "Reconciliation must clear before NAV package sign-off."
        }
      ],
      signOffs: [
        {
          signOffId: "signoff-controller",
          role: "controller",
          actor: "ops-user",
          approvalState: "Approved",
          signedAtUtc: "2026-06-02T05:00:00Z",
          evidenceLinks: ["evidence/nav-signoff"],
          notes: "Controller retained NAV package sign-off."
        }
      ],
      evidenceLinks: ["evidence/nav-package", "evidence/nav-signoff"],
      blockerReason: "Controller sign-off pending.",
      signOffRequirements: [
        {
          requirementId: "requirement-task-nav-controller",
          role: "controller",
          requiredApprovalCount: 1,
          approvedCount: 1,
          isSatisfied: true,
          evidenceRequirement: "Controller NAV sign-off evidence"
        }
      ]
    }
  ],
  lateAdjustments: [
    {
      requestId: "late-adjustment-1",
      journalEntryId: "manual-je-late-1",
      requestedBy: "controller",
      requestedAtUtc: "2026-06-02T03:00:00Z",
      amount: 1250,
      currency: "USD",
      reason: "Late custodian fee accrual.",
      approvalState: "Approved",
      materialityPolicy: {
        policyId: "materiality-alpha",
        amountThreshold: 2500,
        percentThreshold: 0.5,
        currency: "USD",
        reviewRole: "controller",
        requiresLateAdjustmentApproval: true
      },
      evidenceLinks: ["evidence/late-adjustment", "evidence/late-adjustment-approval"],
      decidedBy: "ops-user",
      decidedAtUtc: "2026-06-02T04:00:00Z",
      decisionNotes: "Controller approval retained."
    }
  ],
  validationIssues: [
    {
      code: "CLOSE_TASK_PENDING",
      severity: "Warning",
      message: "NAV package still needs controller sign-off.",
      targetId: "task-nav"
    }
  ],
  closeCalendar: [
    {
      milestoneId: "close-calendar-task-nav",
      taskId: "task-nav",
      displayName: "Finalize NAV package",
      owner: "fund-accounting",
      dueDate: "2026-06-04",
      status: "ReadyForSignOff",
      isBlocked: false,
      isSatisfied: true,
      isPeriodLocked: false,
      dependencyCount: 1,
      requiredSignOffCount: 1,
      approvedSignOffCount: 1,
      evidenceLinks: ["evidence/nav-package", "evidence/nav-signoff"],
      blockerReason: "Controller sign-off pending."
    }
  ],
  configuration: {
    workflowId: "workflow-close-1",
    materialityPolicy: {
      policyId: "materiality-alpha",
      amountThreshold: 2500,
      percentThreshold: 0.5,
      currency: "USD",
      reviewRole: "controller",
      requiresLateAdjustmentApproval: true
    },
    taskConfigurations: [
      {
        taskId: "task-nav",
        displayName: "Finalize NAV package",
        owner: "fund-accounting",
        dueDate: "2026-06-04",
        requiredApprovalCount: 1,
        requiredApprovalRole: "controller",
        requiredEvidence: "Controller NAV sign-off evidence",
        dependsOnTaskIds: ["task-reconciliation"],
        dependencyConfigurations: [
          {
            dependsOnTaskId: "task-reconciliation",
            reason: "Reconciliation must clear before NAV package sign-off."
          }
        ]
      }
    ],
    configuredBy: "controller",
    configuredAtUtc: "2026-06-02T02:30:00Z",
    evidenceLinks: ["evidence/close-plan-configuration"]
  },
  operatingCoverage: [
    {
      controlId: "close-plan-setup",
      label: "Close plan setup",
      state: "ReadyForReview",
      evidenceCount: 1,
      blockingIssueCount: 0,
      requiredAction: "Review the retained close-plan configuration before period lock.",
      evidenceLinks: ["evidence/close-plan-configuration"],
      blockingIssues: []
    },
    {
      controlId: "dependency-graph",
      label: "Dependency graph",
      state: "Blocked",
      evidenceCount: 2,
      blockingIssueCount: 1,
      requiredAction: "Complete predecessor close tasks before dependent close work advances.",
      evidenceLinks: ["evidence/nav-package", "evidence/nav-signoff"],
      blockingIssues: [
        {
          code: "CloseTaskWaitingOnDependency",
          severity: "Warning",
          message: "NAV package is waiting on reconciliation.",
          targetId: "task-nav"
        }
      ]
    },
    {
      controlId: "sign-off-matrix",
      label: "Sign-off matrix",
      state: "ReadyForReview",
      evidenceCount: 2,
      blockingIssueCount: 0,
      requiredAction: "Review retained sign-off matrix approvals before report certification.",
      evidenceLinks: ["evidence/nav-signoff"],
      blockingIssues: []
    },
    {
      controlId: "late-adjustments",
      label: "Late adjustments",
      state: "ReadyForReview",
      evidenceCount: 2,
      blockingIssueCount: 0,
      requiredAction: "Review retained late-adjustment decisions before final close.",
      evidenceLinks: ["evidence/late-adjustment", "evidence/late-adjustment-approval"],
      blockingIssues: []
    },
    {
      controlId: "blocker-evidence-review",
      label: "Blocker evidence review",
      state: "Blocked",
      evidenceCount: 0,
      blockingIssueCount: 1,
      requiredAction: "Review active blocker evidence and remediate critical close validation issues.",
      evidenceLinks: [],
      blockingIssues: [
        {
          code: "CLOSE_TASK_PENDING",
          severity: "Warning",
          message: "NAV package still needs controller sign-off.",
          targetId: "task-nav"
        }
      ]
    },
    {
      controlId: "period-lock",
      label: "Period lock",
      state: "ReadyForReview",
      evidenceCount: 0,
      blockingIssueCount: 0,
      requiredAction: "Retain close-package evidence and lock the period.",
      evidenceLinks: [],
      blockingIssues: []
    }
  ],
  closingEntriesGate: {
    gateId: "closing-entries:book-alpha:2026-05",
    label: "Post closing entries",
    state: "DraftQueued",
    isReadyForLock: false,
    netIncomeRoll: 1500,
    temporaryAccountBalanceCount: 2,
    detail: "A closing-entry draft is queued for controller approval and posting.",
    draftJournalEntryId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    draftStatus: "Draft",
    idempotencyKey: "closing-entries:book-alpha:2026-05:v1",
    balances: [
      {
        accountName: "Advisory fee revenue",
        accountType: "Revenue",
        balance: 2500,
        symbol: "ADV-FEE",
        financialAccountId: "financial-account-revenue",
        dimensions: {
          fundId: "fund-alpha",
          entityId: "entity-alpha",
          sleeveId: "sleeve-credit",
          externalGlDimensions: { class: "private-fund" }
        }
      },
      {
        accountName: "Fund administration expense",
        accountType: "Expense",
        balance: -1000,
        financialAccountId: "financial-account-expense",
        dimensions: {
          fundId: "fund-alpha",
          entityId: "entity-alpha",
          costCenterId: "fund-operations"
        }
      }
    ],
    evidenceLinks: ["evidence/closing-entry-preview"],
    closingBatchJournalEntryIds: ["11111111-2222-3333-4444-555555555555"],
    reversalDraftJournalEntryIds: ["66666666-7777-8888-9999-aaaaaaaaaaaa"]
  }
};

const postedClosePeriodPlan: ClosePeriodPlan = {
  ...closePeriodPlan,
  closingEntriesGate: {
    ...closePeriodPlan.closingEntriesGate!,
    state: "Posted",
    isReadyForLock: true,
    detail: "Closing entries are posted and retained for period lock."
  }
};

const accountingReportPackage: AccountingReportPackageBundle = {
  financialStatements: {
    packageId: "accounting-report-package-alpha-202605",
    fundProfileId: "fund-alpha",
    ledgerBookId: "book-alpha",
    periodId: "2026-05",
    certificationState: "ReadyForReview",
    statementIds: ["balance-sheet-alpha"],
    evidenceLinks: ["evidence/financial-statements"],
    certification: null,
    restatement: {
      restatementId: "restatement-alpha-1",
      priorPackageId: "accounting-report-package-alpha-202604",
      reasonCode: "late-fee-accrual",
      approvalState: "Submitted",
      requestedBy: "controller",
      requestedAtUtc: "2026-06-02T04:00:00Z",
      evidenceLinks: ["evidence/restatement"]
    }
  },
  investorCapitalStatements: [
    {
      statementId: "investor-capital-alpha-1",
      fundProfileId: "fund-alpha",
      capitalAccountId: "capital-alpha-1",
      investorId: "investor-alpha",
      periodId: "2026-05",
      beginningCapital: 100000,
      contributions: 25000,
      distributions: 10000,
      realizedGainLoss: 4500,
      endingCapital: 119500,
      currency: "USD",
      certificationState: "ReadyForReview",
      evidenceLinks: ["evidence/investor-statement"]
    }
  ],
  realizedGainLoss: {
    reportId: "rgl-alpha-202605",
    fundProfileId: "fund-alpha",
    ledgerBookId: "book-alpha",
    periodId: "2026-05",
    dimensions: {
      fundId: "fund-alpha",
      entityId: "entity-alpha",
      capitalAccountId: "capital-alpha-1",
      externalGlDimensions: { class: "private-fund" }
    },
    realizedGainLoss: 4500,
    currency: "USD",
    certificationState: "ReadyForReview",
    evidenceLinks: ["evidence/rgl"]
  },
  navPackage: {
    packageId: "nav-alpha-202605",
    fundProfileId: "fund-alpha",
    ledgerBookId: "book-alpha",
    periodId: "2026-05",
    nav: 119500,
    currency: "USD",
    certificationState: "ReadyForReview",
    evidenceLinks: ["evidence/nav"],
    certification: null,
    restatement: null
  },
  certification: {
    certificationId: "cert-alpha-202605",
    state: "ReadyForReview",
    actor: "controller",
    recordedAtUtc: "2026-06-02T04:30:00Z",
    summary: "Controller package ready for review.",
    evidenceLinks: ["evidence/certification"]
  },
  validationIssues: [
    {
      code: "PACKAGE_REVIEW_PENDING",
      severity: "Warning",
      message: "Package is not certified yet.",
      targetId: "cert-alpha-202605"
    }
  ],
  exportArtifacts: [
    {
      artifactId: "report-export-financial-statements",
      artifactKind: "financial-statements",
      displayName: "Financial statement package",
      format: "pdf",
      route: "/api/ledger/reports/accounting-packages/accounting-report-package-alpha-202605/exports/report-export-financial-statements",
      certificationState: "ReadyForReview",
      generatedAtUtc: "2026-06-02T04:30:00Z",
      contentHash: "hash-financial-statements",
      evidenceLinks: ["evidence/financial-statements"],
      sourceStatementId: "accounting-report-package-alpha-202605"
    },
    {
      artifactId: "report-export-nav",
      artifactKind: "nav-package",
      displayName: "NAV package",
      format: "pdf",
      route: "/api/ledger/reports/accounting-packages/accounting-report-package-alpha-202605/exports/report-export-nav",
      certificationState: "Certified",
      generatedAtUtc: "2026-06-02T04:30:00Z",
      contentHash: "hash-nav",
      evidenceLinks: ["evidence/nav"],
      sourceStatementId: "nav-alpha-202605"
    }
  ]
};

const accountingReportExportManifest: ReportExportArtifactManifest = {
  packageId: "accounting-report-package-alpha-202605",
  artifactId: "report-export-financial-statements",
  artifactKind: "financial-statements",
  displayName: "Financial statement package",
  format: "pdf",
  route: "/api/ledger/reports/accounting-packages/accounting-report-package-alpha-202605/exports/report-export-financial-statements",
  certificationState: "ReadyForReview",
  generatedAtUtc: "2026-06-02T04:30:00Z",
  contentHash: "hash-financial-statements",
  contentType: "application/pdf",
  fileName: "financial-statements-alpha-202605.pdf",
  externalPostingAllowed: false,
  payload: "{\"packageId\":\"accounting-report-package-alpha-202605\"}",
  evidenceLinks: ["evidence/financial-statements"],
  sourceStatementId: "accounting-report-package-alpha-202605"
};

const accountingSystemProvider: AccountingSystemProvider = {
  providerId: "quickbooks-fixture",
  displayName: "QuickBooks fixture",
  state: "Planned",
  requiresCredentials: false,
  supportsChartOfAccounts: true,
  supportsJournalEntries: true,
  supportsTrialBalance: true,
  supportsPosting: false,
  statusLabel: "Planned",
  statusDetail: "Posting is disabled for fixture evidence.",
  evidenceKinds: ["trial-balance"],
  mappingRequirements: [
    {
      requirementId: "quickbooks-fixture:trial-balance-tie-out",
      label: "Trial-balance tie-out",
      requiredEvidenceKind: "QuickBooksTrialBalance",
      requiredAction: "Reconcile provider trial-balance rows against Meridian-owned ledger totals before certification.",
      requiredForGuardedExport: true
    }
  ]
};

const accountingSystemReconciliation: AccountingSystemReconciliationSummary = {
  reconciliationId: "gl-recon-1",
  importId: "gl-import-1",
  providerId: "quickbooks-fixture",
  fundProfileId: "fund-alpha",
  periodStart: "2026-05-01",
  periodEnd: "2026-05-31",
  generatedAtUtc: "2026-06-01T03:00:00Z",
  matchedCount: 1,
  breakCount: 1,
  totalExternalDebits: 100,
  totalExternalCredits: 100,
  totalMeridianDebits: 95,
  totalMeridianCredits: 95,
  postingEnabled: false,
  postingDisabledReason: "Read-only fixture.",
  evidenceReferences: [],
  rows: [
    {
      rowId: "row-1",
      accountCode: "1000",
      accountName: "Cash",
      currency: "USD",
      status: "ReviewRequired",
      externalDebit: 100,
      externalCredit: 0,
      meridianDebit: 95,
      meridianCredit: 0,
      variance: 5,
      detail: "Cash variance requires controller review.",
      evidenceRef: null
    }
  ]
};

const multiAssetCoverage: MultiAssetCoverageSummary = {
  fundAccountId: "fund-alpha",
  entity: "Fund Alpha",
  asOfUtc: "2026-06-01T03:00:00Z",
  metrics: [],
  drillThroughRoutes: { coverage: "/api/workstation/portfolio/multi-asset-coverage" },
  assetClasses: [
    {
      assetClass: "PrivateCredit",
      displayName: "Private credit",
      status: "ReviewRequired",
      statusLabel: "Review required",
      summary: "Valuation source is stale.",
      evidenceRequirements: [],
      blockers: [
        {
          code: "VALUATION_STALE",
          severity: "Warning",
          message: "Private credit valuation is stale.",
          source: "valuation-feed",
          evidenceRoute: "/accounting/security-master"
        }
      ],
      ledgerClassification: {},
      reconciliationSignals: {}
    }
  ]
};


describe("accounting-screen close-cockpit view model", () => {
  it("derives a route-aware accounting workflow launch model", () => {
    const closeCommandCenter = buildCloseCommandCenterViewState({
      data: accountingWorkspace,
      workflow: closeWorkflow,
      workflowLoading: false,
      workflowError: null,
      accountingSystemProviders: [],
      accountingSystemImport: null,
      accountingSystemReconciliation: null,
      multiAssetCoverage: null
    });

    const state = buildAccountingWorkflowLaunchViewState({
      data: accountingWorkspace,
      workstream: "reconciliation",
      closeCommandCenter
    });

    expect(state).toMatchObject({
      title: "Accounting workflow",
      activeLabel: "Reconciliation Casework active",
      statusLabel: "Blocked",
      statusTone: "danger",
      ariaLabel: "Accounting workflow launch paths"
    });
    expect(state.taskMode).toMatchObject({
      id: "reconciliation-casework",
      label: "Reconciliation Casework",
      href: "/accounting/reconciliation"
    });
    expect(state.steps.map((step) => step.href)).toEqual([
      "/accounting/configure",
      "/accounting/journal-entries",
      "/accounting/capital-accounts",
      "/accounting/ledger",
      "/accounting/reconciliation",
      "/accounting/exceptions",
      "/accounting/security-master",
      "/accounting/approvals",
      "/reporting/evidence"
    ]);
    expect(state.steps.find((step) => step.id === "reconciliation")).toMatchObject({
      label: "Reconciliation Casework",
      metricLabel: "Open breaks",
      metricValue: "1",
      statusLabel: "Review breaks",
      tone: "warning",
      isActive: true
    });
    expect(state.steps.find((step) => step.id === "approvals")).toMatchObject({
      metricValue: "2",
      statusLabel: "Signer review",
      tone: "warning"
    });
    expect(state.actionRows.map((action) => action.href)).toEqual([
      "/accounting/reconciliation",
      "/accounting/journal-entries",
      "/accounting/approvals",
      "/reporting/evidence"
    ]);
    expect(state.actionRows.find((action) => action.id === "evidence")).toMatchObject({
      label: "Attach evidence",
      tone: "warning"
    });
  });

  it("loads shared close plans and builds certified accounting report package requests", async () => {
    const getClosePlan = vi.fn(async () => closePeriodPlan);
    const createLateAdjustment = vi.fn(async () => closePeriodPlan);
    const reviewLateAdjustment = vi.fn(async () => closePeriodPlan);
    const signOffCloseTask = vi.fn(async () => closePeriodPlan);
    const configureClosePlan = vi.fn(async () => postedClosePeriodPlan);
    const lockClosePeriod = vi.fn(async () => ({
      isLocked: true,
      plan: { ...postedClosePeriodPlan, isPeriodLocked: true },
      transition: null,
      issues: []
    }));
    const buildPackage = vi.fn(async () => accountingReportPackage);
    const certifyPackage = vi.fn(async () => ({
      ...accountingReportPackage,
      financialStatements: {
        ...accountingReportPackage.financialStatements,
        certificationState: "Certified" as const
      },
      investorCapitalStatements: accountingReportPackage.investorCapitalStatements.map((statement) => ({
        ...statement,
        certificationState: "Certified" as const
      })),
      realizedGainLoss: {
        ...accountingReportPackage.realizedGainLoss,
        certificationState: "Certified" as const
      },
      navPackage: {
        ...accountingReportPackage.navPackage,
        certificationState: "Certified" as const
      },
      certification: {
        ...accountingReportPackage.certification,
        state: "Certified" as const,
        summary: "Controller certified retained report package."
      }
    }));
    const getExportManifest = vi.fn(async () => accountingReportExportManifest);
    const listPackages = vi.fn(async () => [accountingReportPackage]);
    const services: AccountingCloseReportPackageServices = {
      getClosePlan,
      createLateAdjustment,
      reviewLateAdjustment,
      signOffCloseTask,
      configureClosePlan,
      lockClosePeriod,
      buildPackage,
      certifyPackage,
      getExportManifest,
      listPackages,
      reviewCloseEvidence: vi.fn(async () => closePeriodPlan)
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(listPackages).toHaveBeenCalledWith({
      fundProfileId: "fund-alpha",
      periodId: "2026-05",
      ledgerBookId: "book-alpha"
    }));
    await waitFor(() => expect(result.current.packageRows).toHaveLength(1));

    expect(getClosePlan).toHaveBeenCalledWith("workflow-close-1");
    expect(listPackages).toHaveBeenCalledWith({
      fundProfileId: "fund-alpha",
      periodId: "2026-05",
      ledgerBookId: "book-alpha"
    });
    expect(result.current).toMatchObject({
      statusLabel: "Ready for review package",
      statusTone: "warning",
      fundLabel: "fund-alpha",
      lockLabel: "Period open",
      certifyButtonLabel: "Certify package",
      certifyDisabledReason: null
    });
    expect(result.current.materialityLabel).toContain("controller");
    expect(result.current.configureClosePlanButtonLabel).toBe("Retain close setup");
    expect(result.current.configureClosePlanDisabledReason).toBeNull();
    expect(result.current.closeSetupDraft).toMatchObject({
      amountThreshold: "2500",
      percentThreshold: "0.5",
      currency: "USD",
      reviewRole: "controller",
      requiresLateAdjustmentApproval: true,
      taskId: "task-nav",
      taskDisplayName: "Finalize NAV package",
      taskOwner: "fund-accounting",
      taskDueDate: "2026-06-04",
      taskRequiredApprovalCount: "1",
      taskRequiredApprovalRole: "controller",
      taskRequiredEvidence: "Controller NAV sign-off evidence",
      taskDependsOnTaskIds: "task-reconciliation"
    });
    expect(result.current.closeSetupTaskOptions).toEqual([
      expect.objectContaining({
        taskId: "task-nav",
        displayName: "Finalize NAV package",
        statusLabel: "Ready for sign-off",
        statusTone: "warning",
        ownerLabel: "fund-accounting",
        dueDateLabel: "2026-06-04",
        dependencyLabel: "1 dependency: task-reconciliation",
        signOffLabel: "1/1 approvals",
        selected: true
      })
    ]);
    expect(result.current.metrics.find((metric) => metric.id === "calendar")).toMatchObject({
      label: "Calendar",
      value: "1",
      detail: "1 calendar milestone sequenced.",
      tone: "success"
    });
    expect(result.current.closeCalendar[0]).toMatchObject({
      displayName: "Finalize NAV package",
      ownerLabel: "fund-accounting",
      dueDateLabel: "2026-06-04",
      statusLabel: "Ready for sign-off",
      statusTone: "success",
      dependencyLabel: "1 dependency",
      signOffLabel: "1/1 sign-offs",
      evidenceLabel: "2 evidence links",
      lockedLabel: "Open period",
      blockerLabel: "Controller sign-off pending."
    });
    expect(result.current.tasks[0]).toMatchObject({
      displayName: "Finalize NAV package",
      statusLabel: "Ready for sign-off",
      dependencyLabel: expect.stringContaining("task-reconciliation"),
      signOffLabel: "1/1 required sign-offs approved",
      signOffDetailLabel: "Approved by ops-user on Jun 2, 05:00 UTC | Controller retained NAV package sign-off.",
      signOffRequirementLabel: "controller: 1/1",
      evidenceLabel: "2 evidence links",
      blockerLabel: "Controller sign-off pending."
    });
    expect(result.current.dependencyGraphRows).toEqual([
      expect.objectContaining({
        dependencyId: "dependency-recon",
        taskId: "task-nav",
        taskLabel: "Finalize NAV package",
        dependsOnTaskId: "task-reconciliation",
        predecessorLabel: "task-reconciliation",
        reason: "Reconciliation must clear before NAV package sign-off.",
        statusLabel: "Predecessor missing",
        statusTone: "danger",
        blockerLabel: "Controller sign-off pending."
      })
    ]);
    expect(result.current.signOffMatrixRows).toEqual([
      expect.objectContaining({
        rowId: "requirement-task-nav-controller",
        taskId: "task-nav",
        taskLabel: "Finalize NAV package",
        roleLabel: "controller",
        approvedLabel: "1/1",
        statusLabel: "Satisfied",
        statusTone: "success",
        evidenceRequirementLabel: "Controller NAV sign-off evidence",
        latestSignOffLabel: "Approved by ops-user on Jun 2, 05:00 UTC | Controller retained NAV package sign-off."
      })
    ]);
    expect(result.current.operatingCoverageRows).toHaveLength(6);
    expect(result.current.operatingCoverageRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        controlId: "close-plan-setup",
        label: "Close plan setup",
        statusLabel: "Ready for review",
        statusTone: "success",
        evidenceLabel: "1 evidence link",
        blockerLabel: "0 blocking issues",
        requiredAction: "Review the retained close-plan configuration before period lock.",
        issueLabels: []
      }),
      expect.objectContaining({
        controlId: "dependency-graph",
        label: "Dependency graph",
        statusLabel: "Blocked",
        statusTone: "danger",
        evidenceLabel: "2 evidence links",
        blockerLabel: "1 blocking issue",
        requiredAction: "Complete predecessor close tasks before dependent close work advances.",
        issueLabels: ["Warning | CloseTaskWaitingOnDependency | task-nav"]
      }),
      expect.objectContaining({
        controlId: "blocker-evidence-review",
        label: "Blocker evidence review",
        statusLabel: "Blocked",
        statusTone: "danger",
        evidenceLabel: "0 evidence links",
        blockerLabel: "1 blocking issue",
        issueLabels: ["Warning | CLOSE_TASK_PENDING | task-nav"]
      }),
      expect.objectContaining({
        controlId: "period-lock",
        label: "Period lock",
        statusLabel: "Ready for review",
        statusTone: "success",
        requiredAction: "Retain close-package evidence and lock the period."
      })
    ]));
    expect(result.current.closingEntriesGate).toMatchObject({
      gateId: "closing-entries:book-alpha:2026-05",
      label: "Post closing entries",
      statusLabel: "Draft queued",
      statusTone: "warning",
      isReadyForLock: false,
      netIncomeRollLabel: "+$1,500.00 USD",
      temporaryAccountBalanceLabel: "2 temporary-account balances",
      draftLabel: "Draft aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee | Draft",
      idempotencyLabel: "Idempotency closing-entries:book-alpha:2026-05:v1",
      closingBatchLabel: "1 closing batch journal entry: 11111111-2222-3333-4444-555555555555",
      reversalDraftLabel: "1 reversal draft journal entry: 66666666-7777-8888-9999-aaaaaaaaaaaa",
      evidenceLabel: "1 evidence link"
    });
    expect(result.current.closingEntriesGate?.balances).toEqual([
      expect.objectContaining({
        accountLabel: "Advisory fee revenue (ADV-FEE)",
        accountTypeLabel: "Revenue",
        balanceLabel: "+$2,500.00 USD",
        scopeLabel: "Fund: fund-alpha | Entity: entity-alpha | Sleeve: sleeve-credit | External class: private-fund",
        financialAccountLabel: "financial-account-revenue"
      }),
      expect.objectContaining({
        accountLabel: "Fund administration expense",
        accountTypeLabel: "Expense",
        balanceLabel: "-$1,000.00 USD",
        scopeLabel: "Fund: fund-alpha | Entity: entity-alpha | Cost center: fund-operations",
        financialAccountLabel: "financial-account-expense"
      })
    ]);
    expect(result.current.evidenceReviewRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        rowId: "task-evidence-task-nav",
        label: "Finalize NAV package",
        categoryLabel: "Checklist task",
        evidenceLabel: "2 evidence links",
        statusLabel: "Evidence retained",
        statusTone: "success",
        detailLabel: "Controller sign-off pending."
      }),
      expect.objectContaining({
        rowId: "late-adjustment-evidence-late-adjustment-1",
        label: "manual-je-late-1",
        categoryLabel: "Late adjustment",
        evidenceLabel: "2 evidence links",
        statusLabel: "Approved",
        statusTone: "success",
        detailLabel: "Late custodian fee accrual."
      }),
      expect.objectContaining({
        rowId: "report-package-evidence-accounting-report-package-alpha-202605",
        label: "accounting-report-package-alpha-202605",
        categoryLabel: "Report package",
        evidenceLabel: "7 evidence links",
        statusLabel: "Ready for review",
        statusTone: "warning",
        detailLabel: "1 investor statement; 2 export artifacts"
      }),
      expect.objectContaining({
        rowId: "validation-evidence-CLOSE_TASK_PENDING-task-nav",
        label: "Warning | CLOSE_TASK_PENDING",
        categoryLabel: "Blocker review",
        evidenceLabel: "task-nav",
        statusLabel: "Review required",
        statusTone: "warning",
        detailLabel: "NAV package still needs controller sign-off."
      })
    ]));
    expect(result.current.lateAdjustments[0]).toMatchObject({
      journalEntryId: "manual-je-late-1",
      reason: "Late custodian fee accrual.",
      decisionLabel: "Approved by ops-user on Jun 2, 04:00 UTC",
      evidenceLabel: "2 evidence links",
      materialityLabel: "Within materiality: at or below $2,500 USD; controller review policy",
      materialityTone: "success"
    });
    expect(result.current.packageRows[0]).toMatchObject({
      packageId: "accounting-report-package-alpha-202605",
      certificationLabel: "Ready for review",
      navLabel: "$119,500 USD",
      investorStatementLabel: "1 investor statement",
      realizedGainLossLabel: "+$4,500.00 USD",
      restatementLabel: "late-fee-accrual | Submitted",
      exportArtifactLabel: "1/2 exports certified",
      exportArtifactTone: "warning",
      evidenceLabel: "6 evidence links",
      validationLabel: "1 validation issue",
      selected: true
    });
    expect(result.current.exportManifestButtonLabel).toBe("Inspect Financial statement package");
    expect(result.current.exportManifestDisabledReason).toBeNull();
    expect(result.current.validationIssues.map((issue) => issue.label)).toEqual([
      "Warning | CLOSE_TASK_PENDING",
      "Warning | PACKAGE_REVIEW_PENDING"
    ]);
    expect(result.current.certificationSafeguards).toEqual([
      expect.objectContaining({
        id: "checklist-signoff",
        label: "Checklist sign-off",
        value: "0/1",
        detail: "1 close task still requires retained sign-off evidence.",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "period-lock",
        label: "Period lock",
        value: "Open for adjustments",
        detail: "Period remains open, so late adjustments can still change the package evidence set.",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "critical-validation",
        label: "Critical validation blockers",
        value: "Clear",
        detail: "No critical close or report package validation issues are surfaced.",
        tone: "success"
      }),
      expect.objectContaining({
        id: "export-certification",
        label: "Export certification",
        value: "1/2",
        detail: "1 export artifact remains ready for review.",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "restatement-workflow",
        label: "Restatement workflow",
        value: "Submitted",
        detail: "late-fee-accrual restates accounting-report-package-alpha-202604; approval evidence is retained with the package.",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "evidence-package",
        label: "Evidence package",
        value: "7 evidence links",
        detail: "Certification will submit the retained financial statement, investor capital, realized gain/loss, NAV, restatement, and package evidence links.",
        tone: "success"
      })
    ]);
    expect(result.current.closeWorkflowSteps).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "close-setup",
        label: "Close setup",
        statusLabel: "Retained",
        actionId: "configure-close-plan",
        disabledReason: null,
        tone: "success"
      }),
      expect.objectContaining({
        id: "checklist-signoff",
        label: "Checklist sign-off",
        statusLabel: "1 open",
        actionId: "sign-off-task",
        disabledReason: "Select a close checklist task before signing off.",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "late-adjustments",
        label: "Late adjustments",
        statusLabel: "Reviewed",
        actionId: "request-late-adjustment",
        disabledReason: "Enter the journal entry id for the late adjustment.",
        tone: "success"
      }),
      expect.objectContaining({
        id: "blocker-review",
        label: "Blocker review",
        statusLabel: "2 unreviewed",
        actionId: "review-evidence",
        disabledReason: null,
        tone: "warning"
      }),
      expect.objectContaining({
        id: "report-package",
        label: "Report package",
        statusLabel: "Ready for review",
        actionId: "build-package",
        disabledReason: null,
        tone: "warning"
      }),
      expect.objectContaining({
        id: "certification",
        label: "Certification",
        statusLabel: "Ready for review",
        actionId: "certify-package",
        disabledReason: null,
        tone: "warning"
      }),
      expect.objectContaining({
        id: "export-manifest",
        label: "Export manifest",
        statusLabel: "Ready for review",
        actionId: "inspect-export",
        disabledReason: null,
        tone: "warning"
      }),
      expect.objectContaining({
        id: "period-lock",
        label: "Period lock",
        statusLabel: "Open",
        actionId: "lock-period",
        disabledReason: "Closing entries must be Posted or Not required before period lock; current state is Draft queued.",
        tone: "default"
      })
    ]));
    expect(result.current.closeWorkflowSteps).toHaveLength(8);
    expect(result.current.liveRegionText).toContain("workflow step");

    await act(async () => {
      await result.current.inspectSelectedPackageExport();
    });

    expect(getExportManifest).toHaveBeenCalledWith(
      "accounting-report-package-alpha-202605",
      "report-export-financial-statements"
    );
    expect(result.current.exportManifestStatusText).toBe("Loaded export manifest report-export-financial-statements.");
    expect(result.current.exportManifestStatusTone).toBe("success");
    expect(result.current.exportManifest).toMatchObject({
      packageId: "accounting-report-package-alpha-202605",
      artifactId: "report-export-financial-statements",
      displayName: "Financial statement package",
      formatLabel: "pdf | application/pdf",
      fileName: "financial-statements-alpha-202605.pdf",
      certificationLabel: "Ready for review",
      evidenceLabel: "1 evidence link",
      postingLabel: "External posting disabled",
      routeLabel: "/api/ledger/reports/accounting-packages/accounting-report-package-alpha-202605/exports/report-export-financial-statements"
    });

    await act(async () => {
      result.current.updateCloseSetupDraft({
        amountThreshold: "7500",
        percentThreshold: "1.25",
        currency: "eur",
        reviewRole: "CFO",
        requiresLateAdjustmentApproval: false,
        taskDisplayName: "CFO NAV package review",
        taskOwner: "CFO Office",
        taskDueDate: "2026-06-06",
        taskRequiredApprovalCount: "2",
        taskRequiredApprovalRole: "CFO",
        taskRequiredEvidence: "CFO approval and retained NAV support",
        taskDependsOnTaskIds: "task-reconciliation, task-report"
      });
    });

    await act(async () => {
      await result.current.configureClosePlan();
    });

    expect(configureClosePlan).toHaveBeenCalledWith(expect.objectContaining({
      workflowId: "workflow-close-1",
      materialityPolicy: expect.objectContaining({
        amountThreshold: 7500,
        percentThreshold: 1.25,
        currency: "EUR",
        reviewRole: "CFO",
        requiresLateAdjustmentApproval: false
      }),
      actor: "browser-accounting-controller",
      correlationId: "browser-close-plan-configuration-workflow-close-1",
      actionOrigin: "HumanOperator",
      expectedConfiguredAtUtc: "2026-06-02T02:30:00Z",
      taskConfigurations: [
        expect.objectContaining({
          taskId: "task-nav",
          displayName: "CFO NAV package review",
          owner: "CFO Office",
          dueDate: "2026-06-06",
          requiredApprovalCount: 2,
          requiredApprovalRole: "CFO",
          requiredEvidence: "CFO approval and retained NAV support",
          dependsOnTaskIds: ["task-reconciliation", "task-report"]
        })
      ],
      evidenceLinks: expect.arrayContaining([
        "browser://accounting/close/setup/workflow-close-1",
        "evidence://close-plan-configuration/fund/fund-alpha/period/2026-05",
        "evidence://close-plan-configuration/ledger-book/book-alpha",
        "evidence/nav-package",
        "evidence/late-adjustment"
      ])
    }));
    expect(result.current.configureClosePlanStatusText).toBe("Retained close-plan setup for 2026-05.");
    expect(result.current.configureClosePlanStatusTone).toBe("success");

    await act(async () => {
      await result.current.lockClosePeriod();
    });

    expect(lockClosePeriod).toHaveBeenCalledWith(expect.objectContaining({
      workflowId: "workflow-close-1",
      expectedWorkflowVersion: closePeriodPlan.workflowVersion,
      actor: "browser-accounting-controller",
      rationale: "Lock close period 2026-05 after close checklist and report package review.",
      reportPackId: "accounting-report-package-alpha-202605",
      correlationId: "browser-close-period-lock-workflow-close-1",
      closePackageId: "close-package-2026-05",
      closePackageManifestId: "close-manifest-2026-05",
      closePackageRetainedManifestRoute: "/api/ledger/reports/accounting-packages/accounting-report-package-alpha-202605/exports/report-export-financial-statements",
      actionOrigin: "HumanOperator",
      prepareClosingEntriesOnly: false,
      checklistControlApprovals: [
        {
          taskId: "task-nav",
          approvedBy: "ops-user",
          approvedAtUtc: "2026-06-02T05:00:00Z"
        }
      ],
      evidenceLinks: expect.arrayContaining([
        "browser://accounting/close/period-lock/workflow-close-1",
        "evidence://close-package/workflow/workflow-close-1/period/2026-05/book/book-alpha/period-lock",
        "evidence://report-package/accounting-report-package-alpha-202605/workflow/workflow-close-1/period/2026-05/book/book-alpha",
        "evidence/nav-package",
        "evidence/certification"
      ])
    }));
    expect(result.current.lockClosePeriodStatusText).toBe("Locked close period 2026-05.");
    expect(result.current.lockClosePeriodStatusTone).toBe("success");
    expect(result.current.lockLabel).toBe("Period locked");

    await act(async () => {
      await result.current.buildReportPackage();
    });

    expect(buildPackage).toHaveBeenCalledWith(expect.objectContaining({
      actor: "browser-accounting-operator",
      closeWorkflowId: "workflow-close-1",
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-alpha",
      periodId: "2026-05",
      capitalAccountId: "capital-alpha-1",
      investorId: "investor-alpha",
      beginningCapital: 100000,
      contributions: 25000,
      distributions: 10000,
      realizedGainLoss: 4500,
      nav: 119500,
      currency: "USD",
      correlationId: "browser-close-report-workflow-close-1",
      evidenceLinks: expect.arrayContaining(["evidence/nav-package", "evidence/late-adjustment"])
    }));
    expect(result.current.buildStatusText).toContain("accounting-report-package-alpha-202605");

    await act(async () => {
      await result.current.certifyPackage();
    });

    expect(certifyPackage).toHaveBeenCalledWith(expect.objectContaining({
      packageId: "accounting-report-package-alpha-202605",
      actor: "browser-accounting-controller",
      notes: expect.stringContaining("Certified accounting report package accounting-report-package-alpha-202605"),
      correlationId: "browser-certify-report-accounting-report-package-alpha-202605",
      evidenceLinks: expect.arrayContaining([
        "evidence/certification",
        "evidence/nav",
        "evidence:report-certification:accounting-report-package-alpha-202605"
      ])
    }));
    expect(result.current.certifyStatusText).toContain("Certified report package accounting-report-package-alpha-202605");
    expect(result.current.packageRows[0]).toMatchObject({
      certificationLabel: "Certified",
      certificationTone: "success"
    });
    expect(createLateAdjustment).not.toHaveBeenCalled();
    expect(reviewLateAdjustment).not.toHaveBeenCalled();
    expect(signOffCloseTask).not.toHaveBeenCalled();
  });

  it("queues required closing entries with the close-plan workflow version and refreshes the gate", async () => {
    const requiredClosePlan: ClosePeriodPlan = {
      ...closePeriodPlan,
      workflowVersion: 27,
      closingEntriesGate: {
        ...closePeriodPlan.closingEntriesGate!,
        state: "Required",
        isReadyForLock: false,
        detail: "Non-zero temporary-account balances require closing entries before period lock.",
        draftJournalEntryId: null,
        draftStatus: null,
        closingBatchJournalEntryIds: [],
        reversalDraftJournalEntryIds: []
      }
    };
    const queuedClosePlan: ClosePeriodPlan = {
      ...requiredClosePlan,
      workflowVersion: 28,
      closingEntriesGate: {
        ...requiredClosePlan.closingEntriesGate!,
        state: "DraftQueued",
        detail: "A closing-entry draft is queued for controller approval and posting."
      }
    };
    const getClosePlan = vi.fn()
      .mockResolvedValueOnce(requiredClosePlan)
      .mockResolvedValue(queuedClosePlan);
    const lockClosePeriod = vi.fn(async () => ({
      isLocked: false,
      plan: queuedClosePlan,
      transition: null,
      issues: []
    }));
    const services: AccountingCloseReportPackageServices = {
      getClosePlan,
      createLateAdjustment: vi.fn(async () => requiredClosePlan),
      reviewLateAdjustment: vi.fn(async () => requiredClosePlan),
      signOffCloseTask: vi.fn(async () => requiredClosePlan),
      reviewCloseEvidence: vi.fn(async () => requiredClosePlan),
      configureClosePlan: vi.fn(async () => requiredClosePlan),
      lockClosePeriod,
      buildPackage: vi.fn(async () => accountingReportPackage),
      certifyPackage: vi.fn(async () => accountingReportPackage),
      getExportManifest: vi.fn(async () => accountingReportExportManifest),
      listPackages: vi.fn(async () => [accountingReportPackage])
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.closingEntriesGate?.statusLabel).toBe("Required"));
    expect(result.current.queueClosingEntriesDisabledReason).toBeNull();
    expect(result.current.lockClosePeriodDisabledReason).toBe("Queue and post closing entries before locking the period.");

    await act(async () => {
      await result.current.queueClosingEntries();
    });

    expect(lockClosePeriod).toHaveBeenCalledWith(expect.objectContaining({
      workflowId: "workflow-close-1",
      expectedWorkflowVersion: 27,
      actor: "browser-accounting-controller",
      rationale: "Prepare closing entries for close period 2026-05 before period lock.",
      reportPackId: "accounting-report-package-alpha-202605",
      correlationId: "browser-close-period-closing-entries-workflow-close-1",
      prepareClosingEntriesOnly: true,
      evidenceLinks: expect.arrayContaining([
        "browser://accounting/close/closing-entry-preparation/workflow-close-1",
        "evidence://close-package/workflow/workflow-close-1/period/2026-05/book/book-alpha/closing-entry-preparation"
      ])
    }));
    expect(getClosePlan).toHaveBeenCalledTimes(2);
    expect(result.current.queueClosingEntriesStatusText).toBe("Queued closing entries for 2026-05; state is Draft queued.");
    expect(result.current.queueClosingEntriesStatusTone).toBe("success");
    expect(result.current.closingEntriesGate?.statusLabel).toBe("Draft queued");
    expect(result.current.queueClosingEntriesDisabledReason).toContain("current state is Draft queued");
    expect(result.current.lockClosePeriodDisabledReason).toContain("current state is Draft queued");
  });

  it.each([
    { state: "Required", isReadyForLock: true },
    { state: "DraftQueued", isReadyForLock: true },
    { state: "Submitted", isReadyForLock: true },
    { state: "Approved", isReadyForLock: true },
    { state: "ReversalQueued", isReadyForLock: true },
    { state: "Blocked", isReadyForLock: true },
    { state: "Unavailable", isReadyForLock: true },
    { state: "Unexpected", isReadyForLock: true },
    { state: "Posted", isReadyForLock: false },
    { state: "NotRequired", isReadyForLock: false }
  ])(
    "keeps hard period lock disabled for gate state $state with readiness $isReadyForLock",
    async ({ state, isReadyForLock }) => {
      const intermediateClosePlan: ClosePeriodPlan = {
        ...closePeriodPlan,
        closingEntriesGate: {
          ...closePeriodPlan.closingEntriesGate!,
          state: state as NonNullable<ClosePeriodPlan["closingEntriesGate"]>["state"],
          isReadyForLock
        }
      };
      const lockClosePeriod = vi.fn();
      const services: AccountingCloseReportPackageServices = {
        getClosePlan: vi.fn(async () => intermediateClosePlan),
        createLateAdjustment: vi.fn(async () => intermediateClosePlan),
        reviewLateAdjustment: vi.fn(async () => intermediateClosePlan),
        signOffCloseTask: vi.fn(async () => intermediateClosePlan),
        reviewCloseEvidence: vi.fn(async () => intermediateClosePlan),
        configureClosePlan: vi.fn(async () => intermediateClosePlan),
        lockClosePeriod,
        buildPackage: vi.fn(async () => accountingReportPackage),
        certifyPackage: vi.fn(async () => accountingReportPackage),
        getExportManifest: vi.fn(async () => accountingReportExportManifest),
        listPackages: vi.fn(async () => [accountingReportPackage])
      };

      const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

      await waitFor(() => expect(result.current.closingEntriesGate).not.toBeNull());
      expect(result.current.closingEntriesGate?.isReadyForLock).toBe(false);
      expect(result.current.lockClosePeriodDisabledReason).toContain(
        state === "Required"
          ? "Queue and post closing entries before locking the period"
          : "Closing entries must be Posted or Not required before period lock"
      );

      await act(async () => {
        await result.current.lockClosePeriod();
      });

      expect(lockClosePeriod).not.toHaveBeenCalled();
      expect(result.current.lockClosePeriodStatusTone).toBe("danger");
    }
  );

  it("selects retained close setup tasks before retaining dependency and sign-off edits", async () => {
    const multiTaskClosePlan: ClosePeriodPlan = {
      ...closePeriodPlan,
      tasks: [
        ...closePeriodPlan.tasks,
        {
          taskId: "task-cash",
          displayName: "Tie cash controls",
          status: "SignedOff" as const,
          owner: "treasury-ops",
          dueDate: "2026-06-04",
          dependencies: [],
          signOffs: [],
          evidenceLinks: ["evidence/cash-controls"],
          blockerReason: null,
          signOffRequirements: [
            {
              requirementId: "requirement-task-cash-controller",
              role: "controller",
              requiredApprovalCount: 1,
              approvedCount: 1,
              isSatisfied: true,
              evidenceRequirement: "Controller cash control evidence"
            }
          ]
        },
        {
          taskId: "task-report",
          displayName: "Review investor statements",
          status: "InProgress" as const,
          owner: "investor-ops",
          dueDate: "2026-06-05",
          dependencies: [],
          signOffs: [],
          evidenceLinks: ["evidence/investor-statements"],
          blockerReason: null,
          signOffRequirements: [
            {
              requirementId: "requirement-task-report-cfo",
              role: "CFO",
              requiredApprovalCount: 2,
              approvedCount: 0,
              isSatisfied: false,
              evidenceRequirement: "CFO investor statement approval evidence"
            }
          ]
        }
      ]
    };
    const configureClosePlan = vi.fn(async () => multiTaskClosePlan);
    const services: AccountingCloseReportPackageServices = {
      getClosePlan: vi.fn(async () => multiTaskClosePlan),
      createLateAdjustment: vi.fn(async () => multiTaskClosePlan),
      reviewLateAdjustment: vi.fn(async () => multiTaskClosePlan),
      signOffCloseTask: vi.fn(async () => multiTaskClosePlan),
      configureClosePlan,
      lockClosePeriod: vi.fn(async () => ({ isLocked: false, plan: multiTaskClosePlan, transition: null, issues: [] })),
      buildPackage: vi.fn(async () => accountingReportPackage),
      certifyPackage: vi.fn(async () => accountingReportPackage),
      getExportManifest: vi.fn(async () => accountingReportExportManifest),
      listPackages: vi.fn(async () => [accountingReportPackage]),
      reviewCloseEvidence: vi.fn(async () => multiTaskClosePlan)
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.closeSetupTaskOptions).toHaveLength(3));

    expect(result.current.closeSetupTaskOptions.map((task) => [task.taskId, task.selected])).toEqual([
      ["task-nav", true],
      ["task-cash", false],
      ["task-report", false]
    ]);
    expect(result.current.closeSetupDependencyOptions).toEqual([
      expect.objectContaining({
        taskId: "task-cash",
        displayName: "Tie cash controls",
        checked: false,
        toggleAriaLabel: "Add Tie cash controls as a dependency"
      }),
      expect.objectContaining({
        taskId: "task-report",
        displayName: "Review investor statements",
        checked: false,
        toggleAriaLabel: "Add Review investor statements as a dependency"
      })
    ]);

    await act(async () => {
      result.current.selectCloseSetupTask("task-report");
    });

    expect(result.current.closeSetupDraft).toMatchObject({
      taskId: "task-report",
      taskDisplayName: "Review investor statements",
      taskOwner: "investor-ops",
      taskDueDate: "2026-06-05",
      taskRequiredApprovalCount: "2",
      taskRequiredApprovalRole: "CFO",
      taskRequiredEvidence: "CFO investor statement approval evidence",
      taskSignOffRequirements: "CFO | 2 | CFO investor statement approval evidence",
      taskDependsOnTaskIds: "",
      taskDependencyReason: ""
    });
    expect(result.current.closeSetupTaskOptions.map((task) => [task.taskId, task.selected])).toEqual([
      ["task-nav", false],
      ["task-cash", false],
      ["task-report", true]
    ]);
    expect(result.current.closeSetupDependencyOptions).toEqual([
      expect.objectContaining({
        taskId: "task-nav",
        displayName: "Finalize NAV package",
        checked: false,
        toggleAriaLabel: "Add Finalize NAV package as a dependency"
      }),
      expect.objectContaining({
        taskId: "task-cash",
        displayName: "Tie cash controls",
        checked: false,
        toggleAriaLabel: "Add Tie cash controls as a dependency"
      })
    ]);
    expect(result.current.closeSetupSignOffRoleOptions).toEqual([
      expect.objectContaining({
        role: "CFO",
        sourceLabel: "Required role",
        selected: true,
        selectAriaLabel: "Selected close sign-off role CFO"
      }),
      expect.objectContaining({
        role: "investor-ops",
        sourceLabel: "Task owner",
        selected: false,
        selectAriaLabel: "Select close sign-off role investor-ops"
      }),
      expect.objectContaining({
        role: "controller",
        sourceLabel: "Materiality reviewer",
        selected: false
      })
    ]);

    await act(async () => {
      result.current.updateCloseSetupDraft({ taskOwner: "CFO Office" });
      result.current.toggleCloseSetupDependency("task-nav");
      result.current.toggleCloseSetupDependency("task-cash");
      result.current.updateCloseSetupDraft({
        taskSignOffRequirements: [
          "investor-ops | 2 | Investor operations close package evidence",
          "CFO | 1 | CFO final report package evidence"
        ].join("\n"),
        taskDependencyReason: [
          "task-nav: NAV package must be signed off before investor statement review.",
          "task-cash: Cash controls must tie out before investor statement review."
        ].join("\n")
      });
      result.current.selectCloseSetupSignOffRole("investor-ops");
    });

    expect(result.current.closeSetupDraft.taskDependsOnTaskIds).toBe("task-nav, task-cash");
    expect(result.current.closeSetupDraft.taskDependencyReason).toBe([
      "task-nav: NAV package must be signed off before investor statement review.",
      "task-cash: Cash controls must tie out before investor statement review."
    ].join("\n"));
    expect(result.current.closeSetupDraft.taskRequiredApprovalRole).toBe("investor-ops");
    expect(result.current.closeSetupSignOffRoleOptions.find((role) => role.role === "investor-ops")).toMatchObject({
      selected: true,
      selectAriaLabel: "Selected close sign-off role investor-ops"
    });
    expect(result.current.closeSetupDependencyOptions.find((option) => option.taskId === "task-nav")).toMatchObject({
      taskId: "task-nav",
      checked: true,
      toggleAriaLabel: "Remove Finalize NAV package as a dependency"
    });
    expect(result.current.closeSetupDependencyOptions.find((option) => option.taskId === "task-cash")).toMatchObject({
      taskId: "task-cash",
      checked: true,
      toggleAriaLabel: "Remove Tie cash controls as a dependency"
    });

    await act(async () => {
      await result.current.configureClosePlan();
    });

    expect(configureClosePlan).toHaveBeenCalledWith(expect.objectContaining({
      taskConfigurations: expect.arrayContaining([
        expect.objectContaining({
          taskId: "task-report",
          displayName: "Review investor statements",
          owner: "CFO Office",
          dueDate: "2026-06-05",
          requiredApprovalCount: 2,
          requiredApprovalRole: "investor-ops",
          requiredEvidence: "Investor operations close package evidence",
          dependsOnTaskIds: ["task-nav", "task-cash"],
          dependencyConfigurations: [
            {
              dependsOnTaskId: "task-nav",
              reason: "NAV package must be signed off before investor statement review."
            },
            {
              dependsOnTaskId: "task-cash",
              reason: "Cash controls must tie out before investor statement review."
            }
          ],
          signOffRequirementConfigurations: [
            {
              role: "investor-ops",
              requiredApprovalCount: 2,
              evidenceRequirement: "Investor operations close package evidence"
            },
            {
              role: "CFO",
              requiredApprovalCount: 1,
              evidenceRequirement: "CFO final report package evidence"
            }
          ]
        })
      ])
    }));
  });

  it("blocks browser close setup retention when the task id is not in the loaded close plan", async () => {
    const configureClosePlan = vi.fn(async () => closePeriodPlan);
    const services: AccountingCloseReportPackageServices = {
      getClosePlan: vi.fn(async () => closePeriodPlan),
      createLateAdjustment: vi.fn(async () => closePeriodPlan),
      reviewLateAdjustment: vi.fn(async () => closePeriodPlan),
      signOffCloseTask: vi.fn(async () => closePeriodPlan),
      configureClosePlan,
      lockClosePeriod: vi.fn(async () => ({ isLocked: false, plan: closePeriodPlan, transition: null, issues: [] })),
      buildPackage: vi.fn(async () => accountingReportPackage),
      certifyPackage: vi.fn(async () => accountingReportPackage),
      getExportManifest: vi.fn(async () => accountingReportExportManifest),
      listPackages: vi.fn(async () => [accountingReportPackage]),
      reviewCloseEvidence: vi.fn(async () => closePeriodPlan)
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.closeSetupDraft.taskId).toBe("task-nav"));

    await act(async () => {
      result.current.updateCloseSetupDraft({ taskId: "task-missing" });
    });

    expect(result.current.configureClosePlanDisabledReason).toBe("Close checklist task task-missing is not loaded in this close plan.");

    await act(async () => {
      await result.current.configureClosePlan();
    });

    expect(result.current.configureClosePlanStatusText).toBe("Close checklist task task-missing is not loaded in this close plan.");
    expect(result.current.configureClosePlanStatusTone).toBe("danger");
    expect(configureClosePlan).not.toHaveBeenCalled();
  });

  it("blocks browser close setup retention when sign-off matrix fields are incomplete", async () => {
    const configureClosePlan = vi.fn(async () => closePeriodPlan);
    const services: AccountingCloseReportPackageServices = {
      getClosePlan: vi.fn(async () => closePeriodPlan),
      createLateAdjustment: vi.fn(async () => closePeriodPlan),
      reviewLateAdjustment: vi.fn(async () => closePeriodPlan),
      signOffCloseTask: vi.fn(async () => closePeriodPlan),
      configureClosePlan,
      lockClosePeriod: vi.fn(async () => ({ isLocked: false, plan: closePeriodPlan, transition: null, issues: [] })),
      buildPackage: vi.fn(async () => accountingReportPackage),
      certifyPackage: vi.fn(async () => accountingReportPackage),
      getExportManifest: vi.fn(async () => accountingReportExportManifest),
      listPackages: vi.fn(async () => [accountingReportPackage]),
      reviewCloseEvidence: vi.fn(async () => closePeriodPlan)
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.closeSetupDraft.taskId).toBe("task-nav"));

    await act(async () => {
      result.current.updateCloseSetupDraft({ taskRequiredApprovalCount: "0" });
    });

    expect(result.current.configureClosePlanDisabledReason).toBe("Enter a positive required approval count before saving close setup.");

    await act(async () => {
      await result.current.configureClosePlan();
    });

    expect(result.current.configureClosePlanStatusText).toBe("Enter a positive required approval count before saving close setup.");
    expect(result.current.configureClosePlanStatusTone).toBe("danger");
    expect(configureClosePlan).not.toHaveBeenCalled();

    await act(async () => {
      result.current.updateCloseSetupDraft({ taskRequiredApprovalCount: "1", taskRequiredApprovalRole: "" });
    });

    expect(result.current.configureClosePlanDisabledReason).toBe("Enter an approval role before saving close setup.");

    await act(async () => {
      result.current.updateCloseSetupDraft({ taskRequiredApprovalRole: "Controller", taskRequiredEvidence: "" });
    });

    expect(result.current.configureClosePlanDisabledReason).toBe("Enter required sign-off evidence before saving close setup.");
  });

  it("blocks browser close setup retention when materiality fields are incomplete", async () => {
    const configureClosePlan = vi.fn(async () => closePeriodPlan);
    const services: AccountingCloseReportPackageServices = {
      getClosePlan: vi.fn(async () => closePeriodPlan),
      createLateAdjustment: vi.fn(async () => closePeriodPlan),
      reviewLateAdjustment: vi.fn(async () => closePeriodPlan),
      signOffCloseTask: vi.fn(async () => closePeriodPlan),
      configureClosePlan,
      lockClosePeriod: vi.fn(async () => ({ isLocked: false, plan: closePeriodPlan, transition: null, issues: [] })),
      buildPackage: vi.fn(async () => accountingReportPackage),
      certifyPackage: vi.fn(async () => accountingReportPackage),
      getExportManifest: vi.fn(async () => accountingReportExportManifest),
      listPackages: vi.fn(async () => [accountingReportPackage]),
      reviewCloseEvidence: vi.fn(async () => closePeriodPlan)
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.closeSetupDraft.taskId).toBe("task-nav"));

    await act(async () => {
      result.current.updateCloseSetupDraft({ amountThreshold: "" });
    });

    expect(result.current.configureClosePlanDisabledReason).toBe("Enter a materiality amount threshold before saving close setup.");

    await act(async () => {
      await result.current.configureClosePlan();
    });

    expect(result.current.configureClosePlanStatusText).toBe("Enter a materiality amount threshold before saving close setup.");
    expect(result.current.configureClosePlanStatusTone).toBe("danger");
    expect(configureClosePlan).not.toHaveBeenCalled();

    await act(async () => {
      result.current.updateCloseSetupDraft({ amountThreshold: "-1" });
    });

    expect(result.current.configureClosePlanDisabledReason).toBe("Enter a non-negative materiality amount threshold before saving close setup.");

    await act(async () => {
      result.current.updateCloseSetupDraft({ amountThreshold: "2500", percentThreshold: "not-a-percent" });
    });

    expect(result.current.configureClosePlanDisabledReason).toBe("Enter a non-negative materiality percent threshold before saving close setup.");

    await act(async () => {
      result.current.updateCloseSetupDraft({ percentThreshold: "0.5", currency: "US" });
    });

    expect(result.current.configureClosePlanDisabledReason).toBe("Enter a three-letter materiality currency before saving close setup.");

    await act(async () => {
      result.current.updateCloseSetupDraft({ currency: "USD", reviewRole: "" });
    });

    expect(result.current.configureClosePlanDisabledReason).toBe("Enter a materiality review role before saving close setup.");
  });

  it("renders service-owned close readiness rows when report packages provide them", async () => {
    const serviceOwnedPackage: AccountingReportPackageBundle = {
      ...accountingReportPackage,
      closeReadinessItems: [
        {
          itemId: "period-lock",
          category: "PeriodLock",
          label: "Period lock",
          state: "Blocked",
          summary: "The accounting period remains open for late adjustments.",
          requiredAction: "Lock the period after close approvals before final report certification.",
          blockingIssueCount: 1,
          evidenceLinks: ["evidence/period-lock"],
          blockingIssues: [
            {
              code: "PeriodNotLocked",
              severity: "Critical",
              message: "The close period is not locked.",
              targetId: "close-plan-alpha-202605"
            }
          ],
          ledgerBookId: "book-alpha",
          dimensions: {
            fundId: "fund-alpha",
            bookId: "book-alpha",
            externalGlDimensions: {}
          }
        },
        {
          itemId: "report-evidence-package",
          category: "ReportEvidence",
          label: "Report evidence package",
          state: "ReadyForReview",
          summary: "4 retained evidence links support the package.",
          requiredAction: "Attach ledger, reconciliation, rendered report, NAV, certification, and package evidence before certification.",
          blockingIssueCount: 0,
          evidenceLinks: ["evidence/ledger", "evidence/reconciliation", "evidence/report", "evidence/nav"],
          blockingIssues: [],
          ledgerBookId: "book-alpha",
          dimensions: {
            fundId: "fund-alpha",
            bookId: "book-alpha",
            externalGlDimensions: {}
          }
        }
      ]
    };
    const services: AccountingCloseReportPackageServices = {
      getClosePlan: vi.fn(async () => closePeriodPlan),
      createLateAdjustment: vi.fn(async () => closePeriodPlan),
      reviewLateAdjustment: vi.fn(async () => closePeriodPlan),
      signOffCloseTask: vi.fn(async () => closePeriodPlan),
      configureClosePlan: vi.fn(async () => closePeriodPlan),
      lockClosePeriod: vi.fn(async () => ({ isLocked: false, plan: closePeriodPlan, transition: null, issues: [] })),
      buildPackage: vi.fn(async () => serviceOwnedPackage),
      certifyPackage: vi.fn(async () => serviceOwnedPackage),
      getExportManifest: vi.fn(async () => accountingReportExportManifest),
      listPackages: vi.fn(async () => [serviceOwnedPackage]),
      reviewCloseEvidence: vi.fn(async () => closePeriodPlan)
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.packageRows).toHaveLength(1));

    expect(result.current.certificationSafeguards).toEqual([
      expect.objectContaining({
        id: "period-lock",
        label: "Period lock",
        value: "Blocked",
        detail: expect.stringContaining("1 blocking issue attached."),
        tone: "danger"
      }),
      expect.objectContaining({
        id: "report-evidence-package",
        label: "Report evidence package",
        value: "Ready for review",
        detail: expect.stringContaining("4 evidence links retained."),
        tone: "success"
      })
    ]);
  });

  it("retains active close blocker evidence reviews without clearing the blocker", async () => {
    const reviewedClosePlan: ClosePeriodPlan = {
      ...closePeriodPlan,
      evidenceReviews: [
        {
          reviewId: "close-review-workflow-close-1-close-task-pending-task-nav",
          issueCode: "CLOSE_TASK_PENDING",
          targetId: "task-nav",
          reviewedBy: "browser-accounting-controller",
          reviewedAtUtc: "2026-06-03T13:15:00Z",
          notes: "Reviewed close blocker CLOSE_TASK_PENDING for task-nav from the Accounting close cockpit.",
          evidenceLinks: [
            "browser://accounting/close/evidence-review/workflow-close-1/CLOSE_TASK_PENDING/task-nav/book/book-alpha",
            "evidence://close-review/workflow/workflow-close-1/period/2026-05/book/book-alpha/issue/CLOSE_TASK_PENDING/target/task-nav"
          ]
        }
      ]
    };
    const reviewCloseEvidence = vi.fn(async () => reviewedClosePlan);
    const services: AccountingCloseReportPackageServices = {
      getClosePlan: vi.fn(async () => closePeriodPlan),
      createLateAdjustment: vi.fn(async () => closePeriodPlan),
      reviewLateAdjustment: vi.fn(async () => closePeriodPlan),
      signOffCloseTask: vi.fn(async () => closePeriodPlan),
      configureClosePlan: vi.fn(async () => closePeriodPlan),
      lockClosePeriod: vi.fn(async () => ({ isLocked: false, plan: closePeriodPlan, transition: null, issues: [] })),
      buildPackage: vi.fn(async () => accountingReportPackage),
      certifyPackage: vi.fn(async () => accountingReportPackage),
      getExportManifest: vi.fn(async () => accountingReportExportManifest),
      listPackages: vi.fn(async () => [accountingReportPackage]),
      reviewCloseEvidence
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.evidenceReviewRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        rowId: "validation-evidence-CLOSE_TASK_PENDING-task-nav",
        statusLabel: "Review required",
        reviewDisabledReason: null
      })
    ])));
    expect(result.current.closeWorkflowSteps).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "blocker-review",
        statusLabel: "2 unreviewed",
        actionId: "review-evidence",
        disabledReason: null
      })
    ]));

    await act(async () => {
      await result.current.reviewCloseEvidence("validation-evidence-CLOSE_TASK_PENDING-task-nav");
    });

    expect(reviewCloseEvidence).toHaveBeenCalledWith(expect.objectContaining({
      workflowId: "workflow-close-1",
      issueCode: "CLOSE_TASK_PENDING",
      targetId: "task-nav",
      actor: "browser-accounting-controller",
      notes: expect.stringContaining("Reviewed close blocker CLOSE_TASK_PENDING for task-nav"),
      correlationId: "browser-close-evidence-review-workflow-close-1-CLOSE_TASK_PENDING-task-nav",
      actionOrigin: "HumanOperator",
      evidenceLinks: expect.arrayContaining([
        "browser://accounting/close/evidence-review/workflow-close-1/CLOSE_TASK_PENDING/task-nav/book/book-alpha",
        "evidence://close-review/workflow/workflow-close-1/period/2026-05/book/book-alpha/issue/CLOSE_TASK_PENDING/target/task-nav"
      ])
    }));
    expect(result.current.reviewCloseEvidenceStatusText).toBe("Retained close evidence review for CLOSE_TASK_PENDING.");
    expect(result.current.reviewCloseEvidenceStatusTone).toBe("success");
    expect(result.current.validationIssues).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "CLOSE_TASK_PENDING-task-nav",
        label: "Warning | CLOSE_TASK_PENDING"
      })
    ]));
    expect(result.current.evidenceReviewRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        rowId: "validation-evidence-CLOSE_TASK_PENDING-task-nav",
        statusLabel: "Review retained",
        statusTone: "success",
        evidenceLabel: "2 review evidence links retained",
        latestReviewLabel: "browser-accounting-controller on Jun 3, 13:15 UTC | Reviewed close blocker CLOSE_TASK_PENDING for task-nav from the Accounting close cockpit.",
        reviewDisabledReason: "Close evidence review is already retained for this issue."
      })
    ]));
  });

  it("reviews pending late adjustments through the shared close-management endpoint", async () => {
    const pendingLateAdjustmentPlan: ClosePeriodPlan = {
      ...closePeriodPlan,
      lateAdjustments: closePeriodPlan.lateAdjustments.map((adjustment) => ({
        ...adjustment,
        amount: 4000,
        approvalState: "Submitted" as const,
        decidedBy: null,
        decidedAtUtc: null,
        decisionNotes: null
      }))
    };
    const reviewedLateAdjustmentPlan: ClosePeriodPlan = {
      ...pendingLateAdjustmentPlan,
      lateAdjustments: pendingLateAdjustmentPlan.lateAdjustments.map((adjustment) => ({
        ...adjustment,
        approvalState: "Approved" as const,
        decidedBy: "browser-accounting-controller",
        decidedAtUtc: "2026-06-03T12:45:00Z",
        decisionNotes: "Controller approved material late adjustment from browser close cockpit."
      }))
    };
    const getClosePlan = vi.fn(async () => pendingLateAdjustmentPlan);
    const createLateAdjustment = vi.fn(async () => pendingLateAdjustmentPlan);
    const reviewLateAdjustment = vi.fn(async () => reviewedLateAdjustmentPlan);
    const signOffCloseTask = vi.fn(async () => pendingLateAdjustmentPlan);
    const configureClosePlan = vi.fn(async () => pendingLateAdjustmentPlan);
    const buildPackage = vi.fn(async () => accountingReportPackage);
    const certifyPackage = vi.fn(async () => accountingReportPackage);
    const getExportManifest = vi.fn(async () => accountingReportExportManifest);
    const listPackages = vi.fn(async () => [accountingReportPackage]);
    const services: AccountingCloseReportPackageServices = {
      getClosePlan,
      createLateAdjustment,
      reviewLateAdjustment,
      signOffCloseTask,
      configureClosePlan,
      lockClosePeriod: vi.fn(async () => ({ isLocked: false, plan: pendingLateAdjustmentPlan, transition: null, issues: [] })),
      buildPackage,
      certifyPackage,
      getExportManifest,
      listPackages,
      reviewCloseEvidence: vi.fn(async () => pendingLateAdjustmentPlan)
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.lateAdjustments).toHaveLength(1));

    expect(result.current.lateAdjustments[0]).toMatchObject({
      requestId: "late-adjustment-1",
      statusLabel: "Submitted",
      decisionLabel: null,
      materialityLabel: "Material adjustment: exceeds $2,500 USD; controller review required",
      materialityTone: "warning",
      reviewDisabledReason: null
    });
    expect(result.current.closeWorkflowSteps).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "late-adjustments",
        statusLabel: "1 pending",
        actionId: null,
        disabledReason: null,
        tone: "warning"
      })
    ]));

    await act(async () => {
      await result.current.reviewLateAdjustment("late-adjustment-1", "Approved");
    });

    expect(reviewLateAdjustment).toHaveBeenCalledWith(expect.objectContaining({
      workflowId: "workflow-close-1",
      requestId: "late-adjustment-1",
      decision: "Approved",
      actor: "browser-accounting-controller",
      notes: "Approved late adjustment late-adjustment-1 from the Accounting close cockpit.",
      correlationId: "browser-late-adjustment-review-workflow-close-1-late-adjustment-1-approved",
      evidenceLinks: expect.arrayContaining([
        "browser://accounting/close/late-adjustments/review/workflow-close-1/late-adjustment-1/approved",
        "evidence/late-adjustment"
      ])
    }));
    expect(result.current.reviewLateAdjustmentStatusText).toBe("Late adjustment late-adjustment-1 approved.");
    expect(result.current.reviewLateAdjustmentStatusTone).toBe("success");
    expect(result.current.lateAdjustments[0]).toMatchObject({
      statusLabel: "Approved",
      decisionLabel: "Approved by browser-accounting-controller on Jun 3, 12:45 UTC",
      reviewDisabledReason: "Late adjustment is already approved."
    });
    expect(result.current.closeWorkflowSteps).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "late-adjustments",
        statusLabel: "Reviewed",
        actionId: "request-late-adjustment",
        disabledReason: "Enter the journal entry id for the late adjustment.",
        tone: "success"
      })
    ]));
    expect(createLateAdjustment).not.toHaveBeenCalled();
    expect(configureClosePlan).not.toHaveBeenCalled();
    expect(signOffCloseTask).not.toHaveBeenCalled();
    expect(buildPackage).not.toHaveBeenCalled();
    expect(certifyPackage).not.toHaveBeenCalled();
  });

  it("signs off the next ready close checklist task through the shared close endpoint", async () => {
    const unsignedClosePlan: ClosePeriodPlan = {
      ...closePeriodPlan,
      tasks: closePeriodPlan.tasks.map((task) => ({
        ...task,
        signOffs: [],
        signOffRequirements: task.signOffRequirements?.map((requirement) => ({
          ...requirement,
          approvedCount: 0,
          isSatisfied: false
        })) ?? null
      }))
    };
    const signedClosePlan: ClosePeriodPlan = {
      ...unsignedClosePlan,
      tasks: unsignedClosePlan.tasks.map((task) => ({
        ...task,
        status: "SignedOff" as const,
        signOffs: [
          {
            signOffId: "signoff-browser-controller",
            role: "controller",
            actor: "browser-accounting-controller",
            approvalState: "Approved" as const,
            signedAtUtc: "2026-06-03T12:30:00Z",
            evidenceLinks: ["browser://accounting/close/sign-off/workflow-close-1/task-nav"],
            notes: "Approved from browser close cockpit."
          }
        ],
        signOffRequirements: task.signOffRequirements?.map((requirement) => ({
          ...requirement,
          approvedCount: requirement.requiredApprovalCount,
          isSatisfied: true
        })) ?? null
      }))
    };
    const getClosePlan = vi.fn(async () => unsignedClosePlan);
    const createLateAdjustment = vi.fn(async () => unsignedClosePlan);
    const reviewLateAdjustment = vi.fn(async () => unsignedClosePlan);
    const signOffCloseTask = vi.fn(async () => signedClosePlan);
    const configureClosePlan = vi.fn(async () => signedClosePlan);
    const buildPackage = vi.fn(async () => accountingReportPackage);
    const certifyPackage = vi.fn(async () => accountingReportPackage);
    const getExportManifest = vi.fn(async () => accountingReportExportManifest);
    const listPackages = vi.fn(async () => [accountingReportPackage]);
    const services: AccountingCloseReportPackageServices = {
      getClosePlan,
      createLateAdjustment,
      reviewLateAdjustment,
      signOffCloseTask,
      configureClosePlan,
      lockClosePeriod: vi.fn(async () => ({ isLocked: false, plan: signedClosePlan, transition: null, issues: [] })),
      buildPackage,
      certifyPackage,
      getExportManifest,
      listPackages,
      reviewCloseEvidence: vi.fn(async () => unsignedClosePlan)
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.tasks).toHaveLength(1));

    expect(result.current.signOffButtonLabel).toBe("Approved Finalize NAV package");
    expect(result.current.signOffDisabledReason).toBeNull();
    expect(result.current.closeSignOffDraft).toMatchObject({
      taskId: "task-nav",
      role: "controller",
      decision: "Approved"
    });
    expect(result.current.closeSignOffTaskOptions).toEqual([
      expect.objectContaining({
        taskId: "task-nav",
        selected: true,
        signOffLabel: "0/1 required sign-offs approved"
      })
    ]);
    expect(result.current.closeSignOffRoleOptions).toEqual(expect.arrayContaining([
      expect.objectContaining({
        role: "controller",
        selected: true,
        sourceLabel: "Required role"
      })
    ]));
    expect(result.current.closeSignOffDecisionOptions).toEqual([
      expect.objectContaining({ decision: "Approved", selected: true }),
      expect.objectContaining({ decision: "Rejected", selected: false })
    ]);
    expect(result.current.tasks[0]).toMatchObject({
      signOffLabel: "0/1 required sign-offs approved",
      signOffRequirementLabel: "controller: 0/1"
    });

    await act(async () => {
      await result.current.signOffNextTask();
    });

    expect(signOffCloseTask).toHaveBeenCalledWith(expect.objectContaining({
      workflowId: "workflow-close-1",
      taskId: "task-nav",
      role: "controller",
      decision: "Approved",
      actor: "browser-accounting-controller",
      notes: "Approved Finalize NAV package close checklist task from the Accounting close cockpit.",
      correlationId: "browser-close-signoff-workflow-close-1-task-nav-approved",
      evidenceLinks: expect.arrayContaining([
        "browser://accounting/close/sign-off/workflow-close-1/task-nav",
        "browser://accounting/close/sign-off/workflow-close-1/task-nav/controller",
        "evidence/nav-signoff",
        "evidence/nav-package"
      ])
    }));
    expect(result.current.signOffStatusText).toBe("Approved sign-off retained for Finalize NAV package.");
    expect(result.current.signOffStatusTone).toBe("success");
    expect(result.current.tasks[0]).toMatchObject({
      statusLabel: "Signed off",
      signOffLabel: "1/1 required sign-offs approved",
      signOffRequirementLabel: "controller: 1/1"
    });
    expect(result.current.signOffDisabledReason).toBe("Select a close checklist task before signing off.");
    expect(createLateAdjustment).not.toHaveBeenCalled();
    expect(reviewLateAdjustment).not.toHaveBeenCalled();
    expect(configureClosePlan).not.toHaveBeenCalled();
    expect(buildPackage).not.toHaveBeenCalled();
    expect(certifyPackage).not.toHaveBeenCalled();
  });

  it("retains configured rejected close checklist sign-off decisions through the shared close endpoint", async () => {
    const unsignedClosePlan: ClosePeriodPlan = {
      ...closePeriodPlan,
      tasks: closePeriodPlan.tasks.map((task) => ({
        ...task,
        signOffs: [],
        signOffRequirements: task.signOffRequirements?.map((requirement) => ({
          ...requirement,
          approvedCount: 0,
          isSatisfied: false
        })) ?? null
      }))
    };
    const rejectedClosePlan: ClosePeriodPlan = {
      ...unsignedClosePlan,
      tasks: unsignedClosePlan.tasks.map((task) => ({
        ...task,
        signOffs: [
          {
            signOffId: "signoff-browser-controller-rejected",
            role: "controller",
            actor: "browser-accounting-controller",
            approvalState: "Rejected" as const,
            signedAtUtc: "2026-06-03T12:30:00Z",
            evidenceLinks: ["browser://accounting/close/sign-off/workflow-close-1/task-nav/controller"],
            notes: "Controller rejected NAV package pending retained evidence."
          }
        ]
      }))
    };
    const signOffCloseTask = vi.fn(async () => rejectedClosePlan);
    const services: AccountingCloseReportPackageServices = {
      getClosePlan: vi.fn(async () => unsignedClosePlan),
      createLateAdjustment: vi.fn(async () => unsignedClosePlan),
      reviewLateAdjustment: vi.fn(async () => unsignedClosePlan),
      signOffCloseTask,
      configureClosePlan: vi.fn(async () => unsignedClosePlan),
      lockClosePeriod: vi.fn(async () => ({ isLocked: false, plan: unsignedClosePlan, transition: null, issues: [] })),
      buildPackage: vi.fn(async () => accountingReportPackage),
      certifyPackage: vi.fn(async () => accountingReportPackage),
      getExportManifest: vi.fn(async () => accountingReportExportManifest),
      listPackages: vi.fn(async () => [accountingReportPackage]),
      reviewCloseEvidence: vi.fn(async () => unsignedClosePlan)
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.closeSignOffDraft.taskId).toBe("task-nav"));

    act(() => {
      result.current.selectCloseSignOffDecision("Rejected");
      result.current.updateCloseSignOffDraft({ notes: "Controller rejected NAV package pending retained evidence." });
    });

    expect(result.current.signOffButtonLabel).toBe("Rejected Finalize NAV package");

    await act(async () => {
      await result.current.signOffNextTask();
    });

    expect(signOffCloseTask).toHaveBeenCalledWith(expect.objectContaining({
      workflowId: "workflow-close-1",
      taskId: "task-nav",
      role: "controller",
      decision: "Rejected",
      actor: "browser-accounting-controller",
      notes: "Controller rejected NAV package pending retained evidence.",
      correlationId: "browser-close-signoff-workflow-close-1-task-nav-rejected",
      evidenceLinks: expect.arrayContaining([
        "browser://accounting/close/sign-off/workflow-close-1/task-nav",
        "browser://accounting/close/sign-off/workflow-close-1/task-nav/controller",
        "evidence/nav-package"
      ])
    }));
    expect(result.current.signOffStatusText).toBe("Rejected sign-off retained for Finalize NAV package.");
    expect(result.current.tasks[0].signOffDetailLabel).toBe("Rejected by browser-accounting-controller on Jun 3, 12:30 UTC | Controller rejected NAV package pending retained evidence.");
  });

  it("blocks browser close task sign-off when the selected role is not retained", async () => {
    const unsignedClosePlan: ClosePeriodPlan = {
      ...closePeriodPlan,
      tasks: closePeriodPlan.tasks.map((task) => ({
        ...task,
        signOffs: [],
        signOffRequirements: task.signOffRequirements?.map((requirement) => ({
          ...requirement,
          approvedCount: 0,
          isSatisfied: false
        })) ?? null
      }))
    };
    const signOffCloseTask = vi.fn(async () => unsignedClosePlan);
    const services: AccountingCloseReportPackageServices = {
      getClosePlan: vi.fn(async () => unsignedClosePlan),
      createLateAdjustment: vi.fn(async () => unsignedClosePlan),
      reviewLateAdjustment: vi.fn(async () => unsignedClosePlan),
      signOffCloseTask,
      configureClosePlan: vi.fn(async () => unsignedClosePlan),
      lockClosePeriod: vi.fn(async () => ({ isLocked: false, plan: unsignedClosePlan, transition: null, issues: [] })),
      buildPackage: vi.fn(async () => accountingReportPackage),
      certifyPackage: vi.fn(async () => accountingReportPackage),
      getExportManifest: vi.fn(async () => accountingReportExportManifest),
      listPackages: vi.fn(async () => [accountingReportPackage]),
      reviewCloseEvidence: vi.fn(async () => unsignedClosePlan)
    };

    const { result } = renderHook(() => useAccountingCloseReportPackageViewModel(closeWorkflow, services));

    await waitFor(() => expect(result.current.closeSignOffDraft.taskId).toBe("task-nav"));

    act(() => {
      result.current.updateCloseSignOffDraft({ role: "investor-relations" });
    });

    expect(result.current.signOffDisabledReason).toBe("investor-relations is not retained on the selected task sign-off matrix.");

    await act(async () => {
      await result.current.signOffNextTask();
    });

    expect(result.current.signOffStatusText).toBe("investor-relations is not retained on the selected task sign-off matrix.");
    expect(result.current.signOffStatusTone).toBe("danger");
    expect(signOffCloseTask).not.toHaveBeenCalled();
  });


  it("derives a blocked controller close command center from workflow and provider signals", () => {
    const state = buildCloseCommandCenterViewState({
      data: accountingWorkspace,
      workflow: closeWorkflow,
      workflowLoading: false,
      workflowError: null,
      accountingSystemProviders: [accountingSystemProvider],
      accountingSystemImport: null,
      accountingSystemReconciliation,
      multiAssetCoverage
    });

    expect(state).toMatchObject({
      title: "CFO / Controller close command center",
      status: "blocked",
      statusLabel: "Blocked",
      statusTone: "danger",
      periodLabel: "2026-05",
      fundAccountLabel: "fund-alpha"
    });
    expect(state.metricRows).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "breaks", value: "2", tone: "warning" }),
      expect.objectContaining({ id: "source-files", value: "2", tone: "warning" }),
      expect.objectContaining({ id: "adjustments", value: "2", tone: "warning" }),
      expect.objectContaining({ id: "valuations", value: "1", tone: "warning" }),
      expect.objectContaining({ id: "providers", value: "2", tone: "warning" }),
      expect.objectContaining({ id: "report-pack", value: "Not ready", tone: "warning" }),
      expect.objectContaining({ id: "signoff", value: "0/1 approved", tone: "warning" })
    ]));
    expect(state.blockerRows).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "CLOSE_BLOCKED", tone: "danger" }),
      expect.objectContaining({ id: "SOURCE_FILE_MISSING", href: "/accounting/reconciliation" }),
      expect.objectContaining({ id: "evidence-source-records", detail: "Missing: custodian activity file" })
    ]));
    expect(state.actionRows.map((action) => action.href)).toEqual([
      "/accounting/reconciliation",
      "/accounting/approvals",
      "/reporting/evidence"
    ]);
    expect(state.updatedAtUtc).toBe(closeWorkflow.updatedAtUtc);
  });

  it("marks the controller close command center ready only when all close signals clear", () => {
    const readyWorkflow: OperationsContinuityWorkflow = {
      ...closeWorkflow,
      status: "ReadyForClose",
      gates: [],
      nextActions: [],
      breakCases: [],
      approvals: [
        {
          ...closeWorkflow.approvals[0],
          status: "Approved",
          reviewer: "controller-reviewer",
          decidedAtUtc: "2026-06-01T04:00:00Z"
        }
      ],
      reportPackReadiness: {
        isReady: true,
        reportPackId: "report-pack-2026-05",
        blockingReason: null,
        evidenceLinks: []
      },
      closeChecklist: [
        {
          ...closeWorkflow.closeChecklist[0],
          status: "Done",
          evidencePointer: "source-file-evidence",
          acknowledgedAtUtc: "2026-06-01T03:00:00Z",
          acknowledgedBy: "controller"
        }
      ],
      closeReadiness: {
        isReadyToClose: true,
        severity: "Ready",
        score: 100,
        components: [],
        blockers: [],
        nextActions: []
      },
      closePackage: {
        closePackageId: "close-package-1",
        reportPackId: "report-pack-2026-05",
        retainedManifestId: "manifest-1",
        retainedManifestRoute: "/reporting/evidence/manifest-1",
        evidenceHash: "hash-1",
        publishedAtUtc: "2026-06-01T04:30:00Z",
        publishedBy: "controller-reviewer",
        signOffRationale: "Close evidence reviewed.",
        evidenceLinks: [],
        checklistControlApprovals: []
      },
      accountingRecordSummary: {
        ...closeWorkflow.accountingRecordSummary!,
        isAuditReady: true,
        completeCategoryCount: 2,
        evidenceCategories: closeWorkflow.accountingRecordSummary!.evidenceCategories.map((category) => ({
          ...category,
          isComplete: true,
          status: "Complete"
        }))
      },
      blockers: []
    };
    const readyData: AccountingWorkspaceResponse = {
      ...accountingWorkspace,
      breakQueue: [],
      reconciliationQueue: [],
      controlCenter: {
        ...accountingWorkspace.controlCenter!,
        closeReadiness: "Ready",
        alerts: [],
        slaBreachCount: 0
      }
    };
    const readyProvider: AccountingSystemProvider = {
      ...accountingSystemProvider,
      state: "Available",
      statusLabel: "Available"
    };
    const readyGl: AccountingSystemReconciliationSummary = {
      ...accountingSystemReconciliation,
      breakCount: 0,
      rows: accountingSystemReconciliation.rows.map((row) => ({ ...row, status: "Matched", variance: 0 }))
    };
    const readyCoverage: MultiAssetCoverageSummary = {
      ...multiAssetCoverage,
      assetClasses: multiAssetCoverage.assetClasses.map((assetClass) => ({
        ...assetClass,
        status: "Ready",
        statusLabel: "Ready",
        blockers: []
      }))
    };

    const state = buildCloseCommandCenterViewState({
      data: readyData,
      workflow: readyWorkflow,
      workflowLoading: false,
      workflowError: null,
      accountingSystemProviders: [readyProvider],
      accountingSystemImport: null,
      accountingSystemReconciliation: readyGl,
      multiAssetCoverage: readyCoverage
    });

    expect(state).toMatchObject({
      status: "ready",
      statusLabel: "Ready",
      statusTone: "success",
      summary: "The close is ready: breaks, source evidence, approvals, valuations, providers, report pack, and sign-off are clear."
    });
    expect(state.metricRows).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "breaks", value: "0", tone: "success" }),
      expect.objectContaining({ id: "source-files", value: "0", tone: "success" }),
      expect.objectContaining({ id: "adjustments", value: "0", tone: "success" }),
      expect.objectContaining({ id: "providers", value: "0", tone: "success" }),
      expect.objectContaining({ id: "report-pack", value: "report-pack-2026-05", tone: "success" }),
      expect.objectContaining({ id: "signoff", value: "Signed by controller-reviewer", tone: "success" })
    ]));
    expect(state.blockerRows).toEqual([]);
  });

  it("uses shared FINOPS close-support decisions from the command-center DTO", () => {
    const commandCenter: FinancialOperationsCommandCenter = {
      generatedAtUtc: "2026-06-01T05:00:00Z",
      fundProfileId: "fund-alpha",
      ledgerBookId: "11111111-1111-1111-1111-111111111111",
      fundAccountId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      periodId: "2026-06",
      status: "Blocked",
      isReadyToComplete: false,
      summary: "Queue summary should not replace server close-support decision.",
      activeItemCount: 2,
      blockedItemCount: 2,
      reviewItemCount: 0,
      metrics: [],
      queueRows: [],
      activeWorkflow: null,
      closeCalendar: null,
      privateCapitalCloseCockpit: null,
      closeSupportDecision: {
        decisionId: "finops-close-support:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        status: "Blocked",
        isReady: false,
        summary: "2 close-support decision(s) block completion.",
        periodState: "ReadyForClose period 2026-06; close readiness Blocked.",
        lockReopenPosture: "Prior close package is retained, but current period is reopened or not re-locked.",
        navReportDependencyPosture: "Report pack is blocked. 0/1 private-capital close lane(s) ready.",
        unresolvedExceptionCount: 0,
        pendingApprovalCount: 1,
        retainedEvidenceGapCount: 1,
        decisions: [
          {
            decisionId: "period-lock-calendar",
            category: "Lock/reopen posture",
            label: "Period lock calendar",
            status: "Blocked",
            isBlocking: true,
            detail: "Close calendar has blocked or incomplete period-lock items.",
            requiredAction: "Clear close-calendar period-lock and reopen remediation items.",
            routeHint: "/api/workstation/operations/continuity/close-calendar",
            evidenceLinks: []
          },
          {
            decisionId: "nav-report-dependencies",
            category: "NAV/report dependencies",
            label: "NAV and report dependency posture",
            status: "Blocked",
            isBlocking: true,
            detail: "1 private-capital close lane(s) block NAV/report support.",
            requiredAction: "Resolve NAV support, report output approval, delivery, and private-capital close lanes.",
            routeHint: "/workstation/accounting/capital-accounts",
            evidenceLinks: []
          }
        ]
      }
    };

    const state = buildCloseCommandCenterViewState({
      data: accountingWorkspace,
      commandCenter,
      commandCenterLoading: false,
      commandCenterError: null,
      workflow: closeWorkflow,
      workflowLoading: false,
      workflowError: null,
      accountingSystemProviders: [accountingSystemProvider],
      accountingSystemImport: null,
      accountingSystemReconciliation,
      multiAssetCoverage
    });

    expect(state.summary).toBe("2 close-support decision(s) block completion.");
    expect(state.metricRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "close-support-period-state",
        value: "Blocked",
        detail: "ReadyForClose period 2026-06; close readiness Blocked."
      }),
      expect.objectContaining({
        id: "close-support-evidence",
        value: "1",
        detail: "Report pack is blocked. 0/1 private-capital close lane(s) ready."
      }),
      expect.objectContaining({
        id: "close-support-approvals",
        value: "1",
        href: "/accounting/approvals"
      })
    ]));
    expect(state.blockerRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "period-lock-calendar",
        label: "Lock/reopen posture - Period lock calendar",
        statusLabel: "Blocked",
        actionLabel: "Clear close-calendar period-lock and reopen remediation items.",
        impactLabel: "Lock/reopen posture",
        href: "/accounting/approvals"
      }),
      expect.objectContaining({
        id: "nav-report-dependencies",
        label: "NAV/report dependencies - NAV and report dependency posture",
        impactLabel: "NAV/report dependencies",
        href: "/accounting/capital-accounts"
      })
    ]));
    expect(state.actionRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "period-lock-calendar",
        label: "Clear close-calendar period-lock and reopen remediation items.",
        href: "/accounting/approvals"
      }),
      expect.objectContaining({
        id: "nav-report-dependencies",
        label: "Resolve NAV support, report output approval, delivery, and private-capital close lanes.",
        href: "/accounting/capital-accounts"
      })
    ]));
  });

  it("gates daily valuation schedule and batch commands from retained typed state", () => {
    const currentDailyValuationSchedule: DailyValuationScheduleWorkItem = {
      scheduleId: "daily-fund-alpha",
      fundProfileId: "fund-alpha",
      currency: "USD",
      actor: "valuation-scheduler",
      ledgerBookId: "11111111-1111-1111-1111-111111111111",
      periodId: "22222222-2222-2222-2222-222222222222",
      nextRunAtUtc: "2026-06-02T01:00:00Z",
      positions: [],
      policyId: "daily-close-policy",
      policyName: "Approved daily close marks",
      valuationMethod: "ClosingPrice",
      policyApprovedBy: "controller",
      policyApprovedAtUtc: "2026-05-01T00:00:00Z",
      reason: "Retained daily close valuation schedule.",
      isEnabled: true,
      entityId: "entity-alpha",
      tenantId: "tenant-alpha",
      companyId: "company-alpha",
      state: "DraftReady"
    };
    const commandCenter: FinancialOperationsCommandCenter = {
      generatedAtUtc: "2026-06-01T05:00:00Z",
      fundProfileId: "fund-alpha",
      ledgerBookId: "11111111-1111-1111-1111-111111111111",
      fundAccountId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      periodId: "2026-06",
      status: "ReviewRequired",
      isReadyToComplete: false,
      summary: "Daily valuation drafts await controller approval.",
      activeItemCount: 1,
      blockedItemCount: 0,
      reviewItemCount: 1,
      metrics: [],
      queueRows: [],
      activeWorkflow: null,
      closeCalendar: null,
      closeSupportDecision: null,
      privateCapitalCloseCockpit: {
        fundProfileId: "fund-alpha",
        ledgerBookId: "11111111-1111-1111-1111-111111111111",
        fundAccountId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        periodId: "2026-06",
        entityId: "entity-alpha",
        projectedAtUtc: "2026-06-01T05:00:00Z",
        cockpitRoute: "/accounting/close",
        overallStatus: "ReviewRequired",
        isReadyToClose: false,
        readinessScore: 80,
        workflowCount: 1,
        fundEventCount: 0,
        capitalAccountCount: 0,
        reportOutputCount: 0,
        deliveredReportOutputCount: 0,
        readyLaneCount: 0,
        blockedLaneCount: 1,
        lanes: [],
        workflows: [],
        blockers: [],
        nextActions: [],
        liveCapabilities: [],
        plannedCapabilities: [],
        dailyValuationStatus: {
          scheduleId: "daily-fund-alpha",
          fundProfileId: "fund-alpha",
          ledgerBookId: "11111111-1111-1111-1111-111111111111",
          periodId: "2026-06",
          isConfigured: true,
          isEnabled: true,
          nextRunAtUtc: "2026-06-02T01:00:00Z",
          lastRunAtUtc: "2026-06-01T01:00:00Z",
          state: "DraftReady",
          summary: "Two retained valuation drafts are ready.",
          evidenceLinks: [],
          blockers: [],
          journalEntryIds: [
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1",
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"
          ],
          batchCorrelationId: "daily-valuation:fund-alpha:2026-06-01"
        }
      }
    };

    const state = buildCloseCommandCenterViewState({
      data: accountingWorkspace,
      commandCenter,
      commandCenterLoading: false,
      commandCenterError: null,
      workflow: closeWorkflow,
      workflowLoading: false,
      workflowError: null,
      accountingSystemProviders: [accountingSystemProvider],
      accountingSystemImport: null,
      accountingSystemReconciliation,
      multiAssetCoverage,
      currentDailyValuationSchedule
    });

    expect(state.actionRows[0]).toMatchObject({
      id: "daily-valuation-configure",
      command: "configure-daily-valuation-schedule",
      disabledReason: "Complete or correct the retained daily valuation batch before reconfiguring its schedule."
    });
    expect(state.actionRows[1]).toMatchObject({
      id: "daily-valuation-run-due",
      command: "run-due-daily-valuation-schedules",
      disabledReason: "Run due is available only from Scheduled state; current state is DraftReady."
    });
    expect(state.actionRows[2]).toMatchObject({
      id: "daily-valuation-approve-post",
      label: "Approve and post 2 valuation drafts",
      command: "approve-daily-valuation-batch",
      ariaLabel: "Approve and post the complete retained daily valuation batch"
    });

    const scheduledCommandCenter: FinancialOperationsCommandCenter = {
      ...commandCenter,
      privateCapitalCloseCockpit: {
        ...commandCenter.privateCapitalCloseCockpit!,
        dailyValuationStatus: {
          ...commandCenter.privateCapitalCloseCockpit!.dailyValuationStatus!,
          state: "Scheduled",
          summary: "Daily valuation is scheduled.",
          journalEntryId: null,
          journalEntryIds: [],
          batchCorrelationId: null
        }
      }
    };
    const scheduledState = buildCloseCommandCenterViewState({
      data: accountingWorkspace,
      commandCenter: scheduledCommandCenter,
      commandCenterLoading: false,
      commandCenterError: null,
      workflow: closeWorkflow,
      workflowLoading: false,
      workflowError: null,
      accountingSystemProviders: [accountingSystemProvider],
      accountingSystemImport: null,
      accountingSystemReconciliation,
      multiAssetCoverage,
      currentDailyValuationSchedule: { ...currentDailyValuationSchedule, state: "Scheduled" }
    });
    expect(scheduledState.actionRows.slice(0, 2)).toEqual([
      expect.objectContaining({
        command: "configure-daily-valuation-schedule",
        disabledReason: null
      }),
      expect.objectContaining({
        command: "run-due-daily-valuation-schedules",
        disabledReason: null
      })
    ]);

    const blockedCommandCenter: FinancialOperationsCommandCenter = {
      ...commandCenter,
      privateCapitalCloseCockpit: {
        ...commandCenter.privateCapitalCloseCockpit!,
        dailyValuationStatus: {
          ...commandCenter.privateCapitalCloseCockpit!.dailyValuationStatus!,
          state: "Blocked",
          summary: "One of two valuation drafts posted before a retained blocker stopped the batch.",
          blockers: ["Draft correction is required."],
          batchCorrelationId: "daily-valuation:fund-alpha:2026-06-01"
        }
      }
    };
    const blockedState = buildCloseCommandCenterViewState({
      data: accountingWorkspace,
      commandCenter: blockedCommandCenter,
      commandCenterLoading: false,
      commandCenterError: null,
      workflow: closeWorkflow,
      workflowLoading: false,
      workflowError: null,
      accountingSystemProviders: [accountingSystemProvider],
      accountingSystemImport: null,
      accountingSystemReconciliation,
      multiAssetCoverage,
      currentDailyValuationSchedule: { ...currentDailyValuationSchedule, state: "Blocked" }
    });
    expect(blockedState.actionRows[0].disabledReason).toContain("retained daily valuation batch");
    expect(blockedState.actionRows[1].disabledReason).toContain("current state is Blocked");
    expect(blockedState.actionRows[2]).toMatchObject({
      id: "daily-valuation-retry-batch",
      label: "Correct and retry 2 valuation drafts",
      command: "retry-daily-valuation-batch",
      disabledReason: null
    });
  });

  it("surfaces shared FINOPS queue owner due evidence action and impact metadata", () => {
    const commandCenter: FinancialOperationsCommandCenter = {
      generatedAtUtc: "2026-06-01T05:00:00Z",
      fundProfileId: "fund-alpha",
      ledgerBookId: "11111111-1111-1111-1111-111111111111",
      fundAccountId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      periodId: "2026-06",
      status: "Blocked",
      isReadyToComplete: false,
      summary: "Shared FINOPS queue row blocks close-report release.",
      activeItemCount: 1,
      blockedItemCount: 1,
      reviewItemCount: 0,
      metrics: [],
      queueRows: [
        {
          queueId: "break:cash-variance",
          sourceKind: "reconciliation-break",
          kindLabel: "Break",
          title: "Cash variance",
          statusLabel: "Blocked",
          detail: "Cash reconciliation is over tolerance.",
          ownerLabel: "Controller",
          dueLabel: "SLA 2026-06-02 18:00Z",
          evidenceLabel: "2 evidence links",
          actionLabel: "Resolve cash break before close report release.",
          routeHint: "/workstation/accounting/reconciliation",
          isBlocked: true,
          sortOrder: 100,
          workflowId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          evidenceLinks: [],
          severityLabel: "Critical",
          slaLabel: "SLA Warning 2026-06-02 18:00Z",
          blockerType: "ReportPack",
          closeReportImpact: "Blocks close report release"
        }
      ],
      activeWorkflow: null,
      closeCalendar: null,
      privateCapitalCloseCockpit: null,
      closeSupportDecision: null
    };

    const state = buildCloseCommandCenterViewState({
      data: accountingWorkspace,
      commandCenter,
      commandCenterLoading: false,
      commandCenterError: null,
      workflow: closeWorkflow,
      workflowLoading: false,
      workflowError: null,
      accountingSystemProviders: [accountingSystemProvider],
      accountingSystemImport: null,
      accountingSystemReconciliation,
      multiAssetCoverage
    });

    expect(state.blockerRows).toContainEqual(expect.objectContaining({
      id: "break:cash-variance",
      label: "Break - Cash variance",
      statusLabel: "Blocked",
      ownerLabel: "Controller",
      dueLabel: "SLA Warning 2026-06-02 18:00Z",
      evidenceLabel: "2 evidence links",
      actionLabel: "Resolve cash break before close report release.",
      impactLabel: "Blocks close report release",
      href: "/accounting/reconciliation"
    }));
    expect(state.blockerRows[0].detail).toContain("Severity: Critical.");
    expect(state.blockerRows[0].detail).toContain("Blocker: ReportPack.");
    expect(state.actionRows[0]).toMatchObject({
      id: "break:cash-variance",
      label: "Resolve cash break before close report release.",
      href: "/accounting/reconciliation"
    });
  });
});
