import {
  appendOperatingScopeToRoute,
  type AppShellOperatingScopeState
} from "@/lib/app-shell-operating-scope";
import {
  WORKSTATION_ROUTE_CATALOG,
  workstationRouteWithQuery
} from "@/lib/workspace";

export type AppShellPrimaryOperatorWorkflowStepId =
  | "import"
  | "validate"
  | "reconcile"
  | "investigate"
  | "approve"
  | "report";

export type AppShellPrimaryOperatorWorkflowStatusTone = "ready" | "review" | "blocked" | "pending";

export interface AppShellPrimaryOperatorWorkflowStep {
  id: AppShellPrimaryOperatorWorkflowStepId;
  label: string;
  description: string;
  href: string;
  active: boolean;
  statusLabel: string;
  statusTone: AppShellPrimaryOperatorWorkflowStatusTone;
  ariaLabel: string;
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

export type DevelopmentFixtureDemoStepId = "watchlist" | "quotes" | "readiness" | "connect";

export interface DevelopmentFixtureDemoStep {
  id: DevelopmentFixtureDemoStepId;
  step: string;
  href: string;
  matchPath: string;
  matchHash?: string;
  label: string;
  ariaLabel: string;
}

const primaryOperatorWorkflowStepDefinitions: Array<Omit<AppShellPrimaryOperatorWorkflowStep, "active" | "statusLabel" | "statusTone" | "ariaLabel">> = [
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
    href: WORKSTATION_ROUTE_CATALOG.dataBackfills
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
    href: WORKSTATION_ROUTE_CATALOG.reportingReportBuilder
  }
];

export const workflowContinuityTrails: WorkflowContinuityTrailDefinition[] = [
  {
    id: "market-data-to-paper",
    title: "Market Data To Paper",
    summary: "Move from symbol selection through quote validation, alert monitoring, paper readiness, and provider repair without memorizing route order.",
    steps: [
      {
        id: "watchlist",
        label: "Watchlist",
        description: "Choose the monitored universe and starter packs before market-data validation.",
        href: WORKSTATION_ROUTE_CATALOG.dataWatchlist,
        matchPath: WORKSTATION_ROUTE_CATALOG.dataWatchlist
      },
      {
        id: "quotes",
        label: "Live quotes",
        description: "Inspect quote, tape, depth, and historical trend evidence for the active symbol.",
        href: WORKSTATION_ROUTE_CATALOG.dataQuotes,
        matchPath: WORKSTATION_ROUTE_CATALOG.dataQuotes,
        preserveSymbol: true
      },
      {
        id: "alerts",
        label: "Price alerts",
        description: "Track threshold triggers and validate the quote feed behind watched symbols.",
        href: WORKSTATION_ROUTE_CATALOG.dataAlerts,
        matchPath: WORKSTATION_ROUTE_CATALOG.dataAlerts,
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
        label: "Report Builder",
        description: "Review governed output targets, evidence readiness, and export posture.",
        href: WORKSTATION_ROUTE_CATALOG.reportingReportBuilder,
        matchPath: WORKSTATION_ROUTE_CATALOG.reportingReportBuilder
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

export const developmentFixtureDemoSteps: DevelopmentFixtureDemoStep[] = [
  {
    id: "watchlist",
    step: "1",
    href: WORKSTATION_ROUTE_CATALOG.dataWatchlist,
    matchPath: WORKSTATION_ROUTE_CATALOG.dataWatchlist,
    label: "Watchlist",
    ariaLabel: "Open sample watchlist demo lane"
  },
  {
    id: "quotes",
    step: "2",
    href: workstationRouteWithQuery("dataQuotes", { symbol: "AAPL" }),
    matchPath: WORKSTATION_ROUTE_CATALOG.dataQuotes,
    label: "Quotes",
    ariaLabel: "Open sample live quotes for AAPL"
  },
  {
    id: "readiness",
    step: "3",
    href: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
    matchPath: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
    label: "Readiness",
    ariaLabel: "Open sample readiness console"
  },
  {
    id: "connect",
    step: "4",
    href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
    matchPath: WORKSTATION_ROUTE_CATALOG.settings,
    matchHash: "#alpaca-provider-setup",
    label: "Connect",
    ariaLabel: "Open Alpaca paper provider setup"
  }
];

export function buildPrimaryOperatorWorkflowSteps(
  pathname: string,
  operatingScope: AppShellOperatingScopeState
): AppShellPrimaryOperatorWorkflowStep[] {
  const activeStepId = resolvePrimaryOperatorWorkflowStepId(pathname);

  return primaryOperatorWorkflowStepDefinitions.map((step) => {
    const active = step.id === activeStepId;
    const href = appendOperatingScopeToRoute(step.href, operatingScope);
    const statusLabel = active ? "Current" : "Available";
    const statusTone: AppShellPrimaryOperatorWorkflowStatusTone = active ? "review" : "pending";

    return {
      ...step,
      href,
      active,
      statusLabel,
      statusTone,
      ariaLabel: active
        ? `${step.label}, current primary operator workflow step, ${statusLabel}`
        : `Open ${step.label}, primary operator workflow step, ${statusLabel}`
    };
  });
}

export function resolvePrimaryOperatorWorkflowStepId(pathname: string): AppShellPrimaryOperatorWorkflowStepId {
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

  if (route.startsWith(WORKSTATION_ROUTE_CATALOG.dataBackfills)
    || route.startsWith(WORKSTATION_ROUTE_CATALOG.dataQuotes)
    || route.startsWith(WORKSTATION_ROUTE_CATALOG.dataAlerts)) {
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
