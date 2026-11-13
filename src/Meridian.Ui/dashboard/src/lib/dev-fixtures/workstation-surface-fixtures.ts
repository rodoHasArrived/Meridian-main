import type {
  CorporateActionInboxResponse,
  DailyValuationScheduleWorkItem,
  OperatorScopePreference,
  OperatorSurfaceVisit,
  ProviderCapabilityMatrixResponse,
  ProviderInstrumentCapabilityCell,
  QualityDashboardResponse,
  RiskEscalation,
  SecurityMasterQualityReport
} from "@/types";
import type {
  ReconciliationCaseSummary,
  ReconciliationQueueAccountStatus,
  ReconciliationTaxonomySnapshot
} from "@/types/reconciliation-readiness.types";
import type { StatementRunBreak, StatementRunValidation } from "@/types/statement-run-detail.types";
import { UI_API_ROUTES } from "../ui-api-routes.generated";
import {
  PROVIDER_API_ENDPOINTS,
  QUALITY_API_ENDPOINTS,
  RECONCILIATION_API_ENDPOINTS,
  RISK_API_ENDPOINTS,
  SECURITY_MASTER_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINTS
} from "../workstation-endpoints";
import { apiRoutePattern, readDecodedPathSegment, type DynamicFixturePattern } from "./fixture-resolver";

// Synthetic no-host answers for workstation read surfaces that used to return 501 in
// fixture-only mode. Every shape is the shared contract type the screen already reads.
// These are sample values for layout and state review; none is evidence of a backend result,
// and no write route is answered here.

/** The one statement run the rest of the fixture desk already refers to. */
const fixtureStatementRunId = "run-42";
const fixtureStatementImportId = "stmt-import-run-42";

// ── Reconciliation readiness ───────────────────────────────────────────────────────────────
// Values agree with the run-42 cash break the Accounting sample workspace already shows
// (500.00 over tolerance, detected 2026-01-01). State words are ones the server emits.
const fixtureReconciliationQueueStatus: ReconciliationQueueAccountStatus[] = [
  {
    accountId: "acct-operating-cash",
    accountCode: "Assets:Cash:Operating",
    queueState: "Open",
    unresolvedBreakCount: 1,
    signOffReady: false,
    nextBestAction: "Review the open cash variance on run-42 before sign-off.",
    blockerReason: "One cash break is over tolerance.",
    evidenceLinks: []
  }
];

const fixtureReconciliationOpenCases: ReconciliationCaseSummary[] = [
  {
    caseId: "case-run-42-cash",
    importId: fixtureStatementImportId,
    status: "Open",
    reason: "Cash variance over tolerance.",
    confidence: 0.82,
    rationale: "Sample case: a 500.00 USD cash variance on run-42 exceeds the 100.00 USD tolerance.",
    createdAtUtc: "2026-01-01T00:00:00Z",
    assignee: null,
    priority: "High",
    slaPolicyId: null,
    slaDueAtUtc: null,
    slaWarningAtUtc: null,
    slaBreachedAtUtc: null,
    slaState: "Deferred",
    ageBand: "0-4h",
    businessAgeHours: 2,
    rootCauseCode: null,
    resolutionCode: null,
    resolutionNote: null,
    signedOffBy: null,
    signedOffAtUtc: null,
    reopenedBy: null,
    reopenedAtUtc: null,
    reopenReason: null,
    version: 1
  }
];

const fixtureReconciliationTaxonomy: ReconciliationTaxonomySnapshot = {
  version: 1,
  rootCauses: [
    { code: "TIMING", displayName: "Timing difference", version: 1, isActive: true, requiredEvidencePrefixes: ["statement:"] },
    { code: "MISSING_BOOK_ENTRY", displayName: "Missing book entry", version: 1, isActive: true, requiredEvidencePrefixes: ["journal:"] },
    { code: "DATA_ERROR", displayName: "Source data error", version: 1, isActive: true, requiredEvidencePrefixes: null }
  ],
  resolutionCodes: [
    { code: "CLEARED_NEXT_DAY", displayName: "Cleared on next statement", version: 1, isActive: true, requiredEvidencePrefixes: ["statement:"] },
    { code: "ADJUSTING_ENTRY", displayName: "Adjusting journal posted", version: 1, isActive: true, requiredEvidencePrefixes: ["journal:"] },
    { code: "NO_ACTION", displayName: "No action required", version: 1, isActive: true, requiredEvidencePrefixes: null }
  ]
};

// Enums on the statement-run contracts cross the wire as ordinals (see statement-run-detail.types).
const fixtureStatementRunValidation: StatementRunValidation = {
  runId: fixtureStatementRunId,
  issues: [
    {
      issueId: "run-42:issue-1",
      severity: 1,
      code: "STALE_PRICE",
      message: "One position was priced from the prior business day.",
      sourceRowNumber: 14,
      sourceColumn: "MarketValue",
      rawValue: "18250.00",
      recommendedAction: "Confirm the statement price date before accepting the market value.",
      evidenceLink: null
    }
  ],
  isBlocked: false
};

const fixtureStatementRunBreaks: StatementRunBreak[] = [
  {
    breakId: "run-42:cash",
    runId: fixtureStatementRunId,
    importId: fixtureStatementImportId,
    sourceReference: "Assets:Cash:Operating",
    breakType: 7,
    category: "AmountMismatch",
    delta: 500,
    tolerance: 100,
    toleranceBreached: true,
    createdAtUtc: "2026-01-01T00:00:00Z",
    status: "Open"
  }
];

// ── Operator preferences ───────────────────────────────────────────────────────────────────
// No saved default scope and no earlier visit: the first-session answer the server gives.
const fixtureOperatorScopePreference: OperatorScopePreference = { defaultScope: null, updatedAt: null };

function fixtureOperatorSurfaceVisit(surface: string): OperatorSurfaceVisit {
  return { surface, previousVisitAt: null, visitedAt: null };
}

// ── Trading ────────────────────────────────────────────────────────────────────────────────
/** Nothing is parked for approval on the sample desk. */
const fixtureRiskEscalations: RiskEscalation[] = [];

// ── Accounting ─────────────────────────────────────────────────────────────────────────────
/** No daily mark-to-market schedule is configured on the sample desk. */
const fixtureDailyValuationSchedules: DailyValuationScheduleWorkItem[] = [];

// ── Data ───────────────────────────────────────────────────────────────────────────────────
const fixtureInstrumentTypes = ["Equity", "Etf", "Option", "Future"];

function capabilityCells(supported: Record<string, Partial<ProviderInstrumentCapabilityCell>>): ProviderInstrumentCapabilityCell[] {
  return fixtureInstrumentTypes.map((instrumentType) => {
    const declared = supported[instrumentType];
    return {
      instrumentType,
      supported: declared !== undefined,
      stream: false,
      backfill: false,
      corporateActions: false,
      symbolSearch: false,
      optionsChain: false,
      ...declared
    };
  });
}

const fixtureProviderCapabilityMatrix: ProviderCapabilityMatrixResponse = {
  generatedAt: "2026-04-28T18:15:00Z",
  instrumentTypes: fixtureInstrumentTypes,
  providers: [
    {
      providerId: "alpaca",
      declaredInstrumentTypes: ["Equity", "Etf", "Option"],
      surfaces: { streaming: true, historical: true, symbolSearch: true, corporateActions: true, optionsChain: true, brokerage: true },
      cells: capabilityCells({
        Equity: { stream: true, backfill: true, corporateActions: true, symbolSearch: true },
        Etf: { stream: true, backfill: true, corporateActions: true, symbolSearch: true },
        Option: { stream: true, optionsChain: true }
      })
    },
    {
      providerId: "polygon",
      declaredInstrumentTypes: ["Equity", "Etf", "Option"],
      surfaces: { streaming: true, historical: true, symbolSearch: true, corporateActions: true, optionsChain: true, brokerage: false },
      cells: capabilityCells({
        Equity: { stream: true, backfill: true, corporateActions: true, symbolSearch: true },
        Etf: { stream: true, backfill: true, symbolSearch: true },
        Option: { backfill: true, optionsChain: true }
      })
    },
    {
      providerId: "databento",
      declaredInstrumentTypes: ["Equity", "Future"],
      surfaces: { streaming: true, historical: true, symbolSearch: false, corporateActions: false, optionsChain: false, brokerage: false },
      cells: capabilityCells({
        Equity: { stream: true, backfill: true },
        Future: { stream: true, backfill: true }
      })
    }
  ],
  discoveryFailures: []
};

/** No composite has been computed and nothing is recent: the dashboard's "not yet scored" answer. */
const fixtureQualityDashboard: QualityDashboardResponse = {
  timestamp: "2026-04-28T18:15:00Z",
  composite: null,
  recentGaps: [],
  recentAnomalies: []
};

// ── Security Master ────────────────────────────────────────────────────────────────────────
/** An ingest ran and staged nothing. */
const fixtureCorporateActionInbox: CorporateActionInboxResponse = {
  lastIngestAt: "2026-04-28T06:00:00Z",
  stagedCount: 0,
  appliedLastRun: 0,
  duplicatesSkippedLastRun: 0,
  staged: [],
  errors: [],
  cases: []
};

const fixtureSecurityMasterQualityReport: SecurityMasterQualityReport = {
  runAt: "2026-04-28T06:05:00Z",
  securitiesScanned: 16,
  violationCount: 1,
  violations: [
    {
      ruleId: "SM-IDENT-001",
      ruleName: "Primary identifier present",
      category: "Identifiers",
      securityId: "sec-dev-001",
      fieldPath: "identifiers.primary",
      message: "Sample finding: the primary identifier has no validity start date.",
      severity: "Warning",
      detectedAt: "2026-04-28T06:05:00Z"
    }
  ],
  untaggedTermsPayloadCount: 0
};

export const workstationSurfaceFixtureRoutes = {
  [RECONCILIATION_API_ENDPOINTS.queueStatus]: fixtureReconciliationQueueStatus,
  [RECONCILIATION_API_ENDPOINTS.openCases]: fixtureReconciliationOpenCases,
  [RECONCILIATION_API_ENDPOINTS.breakQueueTaxonomy]: fixtureReconciliationTaxonomy,
  [UI_API_ROUTES.WorkstationOperatorScopePreference]: fixtureOperatorScopePreference,
  [RISK_API_ENDPOINTS.escalations]: fixtureRiskEscalations,
  [WORKSTATION_API_ENDPOINTS.dailyValuationSchedules]: fixtureDailyValuationSchedules,
  [PROVIDER_API_ENDPOINTS.capabilityMatrix]: fixtureProviderCapabilityMatrix,
  [QUALITY_API_ENDPOINTS.dashboard]: fixtureQualityDashboard,
  [SECURITY_MASTER_API_ENDPOINTS.corporateActionInbox]: fixtureCorporateActionInbox,
  [SECURITY_MASTER_API_ENDPOINTS.qualityReportLatest]: fixtureSecurityMasterQualityReport
} satisfies Record<string, unknown>;

const statementRunBase = RECONCILIATION_API_ENDPOINTS.statementRuns;
const operatorVisitBase = UI_API_ROUTES.WorkstationOperatorSurfaceVisit.replace("/{surface}", "");

export const workstationSurfaceFixturePatterns: DynamicFixturePattern[] = [
  {
    // Only the run the sample desk refers to has retained detail; any other id stays unsupported.
    pattern: apiRoutePattern(statementRunBase, "/[^/]+/validation"),
    resolve: (cleanPath) => (readDecodedPathSegment(cleanPath, 1) === fixtureStatementRunId ? fixtureStatementRunValidation : undefined)
  },
  {
    pattern: apiRoutePattern(statementRunBase, "/[^/]+/breaks"),
    resolve: (cleanPath) => (readDecodedPathSegment(cleanPath, 1) === fixtureStatementRunId ? fixtureStatementRunBreaks : undefined)
  },
  {
    pattern: apiRoutePattern(operatorVisitBase, "/[^/]+"),
    resolve: (cleanPath) => fixtureOperatorSurfaceVisit(readDecodedPathSegment(cleanPath))
  }
];
