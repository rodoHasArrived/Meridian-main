import { resolveAccountingWorkstream, type AccountingWorkstream } from "@/lib/accounting-task-modes";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export interface AccountingLoadingViewState {
  role: "status";
  ariaBusy: true;
  ariaLive: "polite";
  titleId: string;
  detailId: string;
  eyebrow: string;
  title: string;
  detail: string;
  routeLabel: string;
  workstreamLabel: string;
  statusItemsLabel: string;
  statusItems: AccountingLoadingStatusItemViewModel[];
  actionsLabel: string;
  actions: AccountingLoadingActionViewModel[];
}

export interface AccountingLoadingStatusItemViewModel {
  id: string;
  label: string;
  detail: string;
}

export interface AccountingLoadingActionViewModel {
  id: string;
  label: string;
  detail: string;
  href: string;
  ariaLabel: string;
}

export function buildAccountingLoadingViewState(pathname: string): AccountingLoadingViewState {
  const workspaceLabel = pathname.startsWith(WORKSTATION_ROUTE_CATALOG.reporting) ? "Reporting" : "Accounting";
  const slug = workspaceLabel.toLowerCase();
  const workstream = workspaceLabel === "Accounting" ? resolveAccountingWorkstream(pathname) : "reporting";
  const workstreamLabel = formatAccountingLoadingWorkstreamLabel(workstream);
  const accountingStatusItems: AccountingLoadingStatusItemViewModel[] = [
    {
      id: "ledger-reconciliation",
      label: "Ledger and reconciliation",
      detail: "Loading close metrics, reconciliation runs, open breaks, cash-flow evidence, and trial-balance rows."
    },
    {
      id: "approvals-exceptions",
      label: "Approvals and exceptions",
      detail: "Preparing dedicated approval and exception workstreams from close-control data."
    },
    {
      id: "security-reporting",
      label: "Security and reporting evidence",
      detail: "Loading Security Master coverage, report profiles, external GL evidence, and retained report-pack context."
    }
  ];
  const reportingStatusItems: AccountingLoadingStatusItemViewModel[] = [
    {
      id: "report-packs",
      label: "Report packs",
      detail: "Loading governed report-pack runs, retained manifests, and evidence-bundle readiness."
    },
    {
      id: "approvals",
      label: "Approval context",
      detail: "Preparing accounting approval and exception handoffs for report evidence review."
    },
    {
      id: "exports",
      label: "Export setup",
      detail: "Loading profile, recipient, dictionary, and loader-script state."
    }
  ];
  const accountingActions: AccountingLoadingActionViewModel[] = [
    {
      id: "continuity",
      label: "Open continuity",
      detail: "Review close workflow gates while workspace data finishes loading.",
      href: WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity,
      ariaLabel: "Open Accounting operations continuity while Accounting loads"
    },
    {
      id: "entity-setup",
      label: "Entity setup",
      detail: "Configure fund structure, account context, and setup evidence.",
      href: WORKSTATION_ROUTE_CATALOG.accountingEntitySetup,
      ariaLabel: "Open Accounting entity setup while Accounting loads"
    },
    {
      id: "provider-posture",
      label: "Provider posture",
      detail: "Check source and provider diagnostics before relying on fresh close data.",
      href: WORKSTATION_ROUTE_CATALOG.dataProviders,
      ariaLabel: "Open Data provider posture while Accounting loads"
    },
    {
      id: "report-evidence",
      label: "Report evidence",
      detail: "Open retained report-pack evidence for close and audit review.",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
      ariaLabel: "Open Reporting evidence while Accounting loads"
    }
  ];
  const reportingActions: AccountingLoadingActionViewModel[] = [
    {
      id: "report-evidence",
      label: "Report evidence",
      detail: "Open retained report-pack evidence and manifests.",
      href: WORKSTATION_ROUTE_CATALOG.reportingEvidence,
      ariaLabel: "Open Reporting evidence while Reporting loads"
    },
    {
      id: "approvals",
      label: "Accounting approvals",
      detail: "Review close approvals linked to reporting release.",
      href: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
      ariaLabel: "Open Accounting approvals while Reporting loads"
    },
    {
      id: "exceptions",
      label: "Exceptions",
      detail: "Review exception evidence that may block report release.",
      href: WORKSTATION_ROUTE_CATALOG.accountingExceptions,
      ariaLabel: "Open Accounting exceptions while Reporting loads"
    }
  ];

  return {
    role: "status",
    ariaBusy: true,
    ariaLive: "polite",
    titleId: `${slug}-workspace-loading-title`,
    detailId: `${slug}-workspace-loading-detail`,
    eyebrow: `${workspaceLabel} workspace data`,
    title: `Loading ${workspaceLabel}`,
    detail: workspaceLabel === "Reporting"
      ? "Waiting for report-pack, governed export, and approval summaries from workspace data."
      : "Waiting for ledger, reconciliation, cash-flow, and Security Master summaries from workspace data.",
    routeLabel: pathname,
    workstreamLabel,
    statusItemsLabel: `${workspaceLabel} workspace data loading`,
    statusItems: workspaceLabel === "Reporting" ? reportingStatusItems : accountingStatusItems,
    actionsLabel: `${workspaceLabel} actions available while loading`,
    actions: workspaceLabel === "Reporting" ? reportingActions : accountingActions
  };
}

export function buildGovernanceLoadingViewState(pathname: string): AccountingLoadingViewState {
  return buildAccountingLoadingViewState(pathname);
}

function formatAccountingLoadingWorkstreamLabel(workstream: AccountingWorkstream): string {
  if (workstream === "close-cockpit") {
    return "Close Cockpit";
  }

  if (workstream === "security-master") {
    return "Security Master";
  }

  return workstream.charAt(0).toUpperCase() + workstream.slice(1).replace("-", " ");
}
