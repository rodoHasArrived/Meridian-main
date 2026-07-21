import { WORKSTATION_ROUTE_CATALOG, workspaceForPath } from "@/lib/workspace";
import type { WorkspaceKey } from "@/types";

export type WorkflowContinuityStepStatusTone = "ready" | "review" | "blocked" | "pending";

export interface WorkflowContinuityStepStatus {
  label: string;
  tone: WorkflowContinuityStepStatusTone;
}

export interface WorkflowContinuityTrailStepDefinition {
  id: string;
  label: string;
  description: string;
  href: string;
  matchPath: string;
  matchHash?: string;
  preserveSymbol?: boolean;
}

export interface WorkflowContinuityTrailDefinition {
  id: string;
  title: string;
  summary: string;
  steps: WorkflowContinuityTrailStepDefinition[];
}

export type PrimaryOperatorWorkflowStepId = "import" | "validate" | "reconcile" | "investigate" | "approve" | "report";

export interface PrimaryOperatorWorkflowStepDefinition {
  id: PrimaryOperatorWorkflowStepId;
  label: string;
  description: string;
  href: string;
}

export const primaryOperatorWorkflowStepDefinitions: PrimaryOperatorWorkflowStepDefinition[] = [
  {
    id: "import",
    label: "Import",
    description: "Bring provider, file, and account-source data into the active operating scope.",
    href: WORKSTATION_ROUTE_CATALOG.dataProviders
  },
  {
    id: "validate",
    label: "Validate",
    description: "Check data quality, provider health, and backfill evidence before downstream use.",
    href: WORKSTATION_ROUTE_CATALOG.dataOperations
  },
  {
    id: "reconcile",
    label: "Reconcile",
    description: "Match source, ledger, cash, security, and position records with explainable breaks.",
    href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
  },
  {
    id: "investigate",
    label: "Investigate",
    description: "Review portfolio, strategy, and trading evidence behind exceptions or decisions.",
    href: WORKSTATION_ROUTE_CATALOG.portfolio
  },
  {
    id: "approve",
    label: "Approve",
    description: "Capture accounting, control, and operations-continuity approvals with evidence.",
    href: WORKSTATION_ROUTE_CATALOG.accountingApprovals
  },
  {
    id: "report",
    label: "Report",
    description: "Publish governed report packs, exports, and stakeholder-ready evidence.",
    href: WORKSTATION_ROUTE_CATALOG.reportingReportPacks
  }
];

export const workflowContinuityTrails: WorkflowContinuityTrailDefinition[] = [
  {
    id: "market-data-to-paper",
    title: "Market Data To Paper",
    summary: "Move from the Market Data desk through paper readiness and provider repair without memorizing route order.",
    steps: [
      {
        id: "market-data",
        label: "Market data",
        description: "Manage the watchlist, validate live quotes, and track price alerts from one desk.",
        href: WORKSTATION_ROUTE_CATALOG.dataQuotes,
        matchPath: WORKSTATION_ROUTE_CATALOG.dataQuotes,
        preserveSymbol: true
      },
      {
        id: "readiness",
        label: "Readiness",
        description: "Review paper-operation blockers, execution controls, replay evidence, and work items.",
        href: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        matchPath: WORKSTATION_ROUTE_CATALOG.tradingReadiness
      },
      {
        id: "provider-setup",
        label: "Provider setup",
        description: "Repair credentials, connection acknowledgement, and paper/live provider status.",
        href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
        matchPath: WORKSTATION_ROUTE_CATALOG.settings,
        matchHash: "#alpaca-provider-setup"
      }
    ]
  },
  {
    id: "daily-control-tower",
    title: "Daily Control Tower",
    summary: "Start finance users with today's exceptions, close blockers, reconciliation, ledger, reports, evidence, and data-health work before non-finance surfaces.",
    steps: [
      {
        id: "today",
        label: "Today",
        description: "Start each session from the finance decision queue and next action.",
        href: "/",
        matchPath: "/"
      },
      {
        id: "exceptions",
        label: "Exceptions",
        description: "Review breaks, approvals, and due close work that need finance action.",
        href: WORKSTATION_ROUTE_CATALOG.accountingExceptions,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingExceptions
      },
      {
        id: "close",
        label: "Close",
        description: "Review period-close blockers, approvals, and close-support tasks.",
        href: WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity
      },
      {
        id: "reconciliation",
        label: "Reconciliation",
        description: "Resolve cash, position, ledger, and security breaks before close or reporting.",
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
      },
      {
        id: "ledger",
        label: "Ledger",
        description: "Inspect trial-balance, journal, and book-impact evidence for selected cases.",
        href: WORKSTATION_ROUTE_CATALOG.accountingLedger,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingLedger
      },
      {
        id: "reports",
        label: "Reports",
        description: "Review governed report outputs affected by finance exceptions.",
        href: WORKSTATION_ROUTE_CATALOG.reportingReportPacks,
        matchPath: WORKSTATION_ROUTE_CATALOG.reportingReportPacks
      },
      {
        id: "evidence",
        label: "Evidence",
        description: "Open retained support and evidence packets for the selected finance item.",
        href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
        matchPath: WORKSTATION_ROUTE_CATALOG.reportingEvidence
      },
      {
        id: "data-health",
        label: "Data Health",
        description: "Check provider data quality and import posture after finance work is triaged.",
        href: WORKSTATION_ROUTE_CATALOG.dataProviders,
        matchPath: WORKSTATION_ROUTE_CATALOG.dataProviders
      }
    ]
  },
  {
    id: "strategy-to-paper",
    title: "Research To Paper",
    summary: "Keep Strategy comparison, strategy design, backtest evidence, paper-session readiness, portfolio impact, and audit packet review connected.",
    steps: [
      {
        id: "strategy-runs",
        label: "Run library",
        description: "Compare runs, inspect promotion history, and select the evidence candidate.",
        href: WORKSTATION_ROUTE_CATALOG.strategy,
        matchPath: WORKSTATION_ROUTE_CATALOG.strategy
      },
      {
        id: "quant-lab",
        label: "Quant Lab",
        description: "Prototype scripts, parameters, plots, and diagnostics against trusted data.",
        href: WORKSTATION_ROUTE_CATALOG.strategyQuantLab,
        matchPath: WORKSTATION_ROUTE_CATALOG.strategyQuantLab
      },
      {
        id: "covered-call",
        label: "Covered call",
        description: "Preview option chains, run covered-call scenarios, and inspect trade outcomes.",
        href: WORKSTATION_ROUTE_CATALOG.strategyCoveredCall,
        matchPath: WORKSTATION_ROUTE_CATALOG.strategyCoveredCall
      },
      {
        id: "paper-readiness",
        label: "Paper readiness",
        description: "Confirm replay consistency, acceptance gates, execution controls, and approval blockers.",
        href: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        matchPath: WORKSTATION_ROUTE_CATALOG.tradingReadiness
      },
      {
        id: "portfolio-review",
        label: "Portfolio review",
        description: "Check exposure, account sync, positions, cash, and run-to-portfolio continuity.",
        href: WORKSTATION_ROUTE_CATALOG.portfolio,
        matchPath: WORKSTATION_ROUTE_CATALOG.portfolio
      },
      {
        id: "evidence-review",
        label: "Evidence review",
        description: "Package lineage, stale evidence, and packet completeness for governed review.",
        href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
        matchPath: WORKSTATION_ROUTE_CATALOG.reportingEvidence
      }
    ]
  },
  {
    id: "trading-accounting",
    title: "Trading Controls",
    summary: "Hold execution readiness, cockpit action, portfolio exposure, reconciliation, and report-pack review in one operational path.",
    steps: [
      {
        id: "trading-readiness",
        label: "Readiness",
        description: "Review gates, replay, execution controls, trust checks, and operator work items.",
        href: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
        matchPath: WORKSTATION_ROUTE_CATALOG.tradingReadiness
      },
      {
        id: "trading-cockpit",
        label: "Trading cockpit",
        description: "Stage paper orders, inspect positions, monitor fills, and control strategy actions.",
        href: WORKSTATION_ROUTE_CATALOG.trading,
        matchPath: WORKSTATION_ROUTE_CATALOG.trading
      },
      {
        id: "portfolio-exposure",
        label: "Exposure",
        description: "Review household positions, account sync, cash, buying power, and risk posture.",
        href: WORKSTATION_ROUTE_CATALOG.portfolio,
        matchPath: WORKSTATION_ROUTE_CATALOG.portfolio
      },
      {
        id: "reconciliation",
        label: "Reconciliation",
        description: "Resolve ledger, security, cash, and position breaks before accepting readiness.",
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
      },
      {
        id: "report-packs",
        label: "Report packs",
        description: "Review governed output targets, evidence readiness, and export posture.",
        href: WORKSTATION_ROUTE_CATALOG.reportingReportPacks,
        matchPath: WORKSTATION_ROUTE_CATALOG.reportingReportPacks
      }
    ]
  },
  {
    id: "accounting-closeout",
    title: "Accounting Closeout",
    summary: "Move through received activity, record matching, exception resolution, approvals, and evidence production with the close context intact.",
    steps: [
      {
        id: "receive-activity",
        label: "Receive Activity",
        description: "Bring source activity, ledger context, security coverage, and account records into Accounting.",
        href: WORKSTATION_ROUTE_CATALOG.accountingLedger,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingLedger
      },
      {
        id: "match-records",
        label: "Match Records",
        description: "Match ledger, cash, security, and position records through reconciliation runs.",
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
      },
      {
        id: "resolve-exceptions",
        label: "Resolve Exceptions",
        description: "Review open breaks, coverage issues, comments, evidence, and sign-off blockers.",
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
      },
      {
        id: "approve-results",
        label: "Approve Results",
        description: "Review accounting approvals, close readiness, and retained audit decisions before release.",
        href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingApprovals
      },
      {
        id: "produce-evidence",
        label: "Produce Evidence",
        description: "Package approved accounting evidence into retained audit packets and report-ready outputs.",
        href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
        matchPath: WORKSTATION_ROUTE_CATALOG.reportingEvidence
      },
      {
        id: "close-support",
        label: "Close Support",
        description: "Review close checklists, period locks, governed reopen evidence, and retained close packages.",
        href: WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity,
        matchPath: WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity
      }
    ]
  }
];

export const defaultWorkflowContinuityTrail = workflowContinuityTrails[0];

export function resolvePrimaryOperatorWorkflowStepId(pathname: string): PrimaryOperatorWorkflowStepId {
  const route = pathname.toLowerCase();

  if (route.startsWith(WORKSTATION_ROUTE_CATALOG.reporting)) {
    return "report";
  }

  if (route.startsWith(WORKSTATION_ROUTE_CATALOG.accountingApprovals)
    || route.startsWith(WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity)) {
    return "approve";
  }

  if (route.startsWith(WORKSTATION_ROUTE_CATALOG.accountingReconciliation)
    || route.startsWith(WORKSTATION_ROUTE_CATALOG.accountingLedger)
    || route.startsWith(WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster)
    || route.startsWith(WORKSTATION_ROUTE_CATALOG.accounting)) {
    return "reconcile";
  }

  if (route.startsWith(WORKSTATION_ROUTE_CATALOG.dataOperations)
    || route.startsWith(WORKSTATION_ROUTE_CATALOG.dataQuotes)
    || route.startsWith(WORKSTATION_ROUTE_CATALOG.dataAlertsLegacy)) {
    return "validate";
  }

  if (route.startsWith(WORKSTATION_ROUTE_CATALOG.data)) {
    return "import";
  }

  if (route.startsWith(WORKSTATION_ROUTE_CATALOG.trading)
    || route.startsWith(WORKSTATION_ROUTE_CATALOG.portfolio)
    || route.startsWith(WORKSTATION_ROUTE_CATALOG.strategy)) {
    return "investigate";
  }

  return "import";
}

export function selectWorkflowContinuityTrail(pathname: string, hash: string): WorkflowContinuityTrailDefinition {
  const workspaceKey = workspaceForPath(pathname).key;
  const scoredTrails = workflowContinuityTrails
    .map((trail, index) => ({
      trail,
      index,
      workspaceAffinity: scoreWorkflowTrailWorkspaceAffinity(trail.id, workspaceKey),
      score: Math.max(...trail.steps.map((step) => scoreWorkflowStepRouteMatch(step, pathname, hash)))
    }))
    .filter((match) => match.score > 0)
    .sort((left, right) => right.score - left.score || right.workspaceAffinity - left.workspaceAffinity || left.index - right.index);

  if (scoredTrails.length > 0) {
    return scoredTrails[0].trail;
  }

  switch (workspaceKey) {
    case "accounting":
    case "reporting":
      return workflowContinuityTrails.find((trail) => trail.id === "accounting-closeout") ?? defaultWorkflowContinuityTrail;
    case "strategy":
      return workflowContinuityTrails.find((trail) => trail.id === "strategy-to-paper") ?? defaultWorkflowContinuityTrail;
    case "trading":
    case "portfolio":
      return workflowContinuityTrails.find((trail) => trail.id === "trading-accounting") ?? defaultWorkflowContinuityTrail;
    case "data":
    case "settings":
    default:
      return defaultWorkflowContinuityTrail;
  }
}

export function findActiveWorkflowStepIndex(
  steps: WorkflowContinuityTrailStepDefinition[],
  pathname: string,
  hash: string
) {
  const scoredSteps = steps
    .map((step, index) => ({
      index,
      score: scoreWorkflowStepRouteMatch(step, pathname, hash)
    }))
    .filter((match) => match.score > 0)
    .sort((left, right) => right.score - left.score || left.index - right.index);

  return scoredSteps[0]?.index ?? 0;
}

function scoreWorkflowTrailWorkspaceAffinity(trailId: string, workspaceKey: WorkspaceKey): number {
  if (workspaceKey === "data" || workspaceKey === "settings") {
    return trailId === "market-data-to-paper" ? 1 : 0;
  }

  if (workspaceKey === "strategy") {
    return trailId === "strategy-to-paper" ? 1 : 0;
  }

  if (workspaceKey === "accounting" || workspaceKey === "reporting") {
    return trailId === "accounting-closeout" ? 1 : 0;
  }

  if (workspaceKey === "trading" || workspaceKey === "portfolio") {
    return trailId === "trading-accounting" ? 1 : 0;
  }

  return 0;
}

function scoreWorkflowStepRouteMatch(
  step: WorkflowContinuityTrailStepDefinition,
  pathname: string,
  hash: string
) {
  const candidate = splitContinuityRoute(step.matchPath);
  const candidateHash = step.matchHash ?? candidate.hash;
  if (candidateHash && hash !== candidateHash) {
    return 0;
  }

  const matchPath = candidate.pathname;
  if (pathname === matchPath) {
    return 1000 + matchPath.length + (candidateHash ? 2000 : 0);
  }

  return pathname.startsWith(`${matchPath}/`)
    ? 100 + matchPath.length + (candidateHash ? 2000 : 0)
    : 0;
}

export function splitContinuityRoute(route: string) {
  const hashIndex = route.indexOf("#");
  const routeWithoutHash = hashIndex >= 0 ? route.slice(0, hashIndex) : route;
  const hash = hashIndex >= 0 ? route.slice(hashIndex) : "";
  const searchIndex = routeWithoutHash.indexOf("?");
  return {
    pathname: searchIndex >= 0 ? routeWithoutHash.slice(0, searchIndex) : routeWithoutHash,
    hash
  };
}
