import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export type AccountingWorkstream =
  | "ledger"
  | "configure"
  | "journal-entries"
  | "capital-accounts"
  | "reconciliation"
  | "external-gl"
  | "exceptions"
  | "security-master"
  | "approvals"
  | "reporting";
export type GovernanceWorkstream = AccountingWorkstream;

export type AccountingTaskModeId =
  | "close-cockpit"
  | "reconciliation-casework"
  | "external-gl-reconciliation"
  | "ledger-explorer"
  | "journal-entry"
  | "capital-accounts"
  | "exceptions"
  | "security-master"
  | "approvals"
  | "configure"
  | "delivery-evidence";

export interface AccountingTaskModeViewModel {
  id: AccountingTaskModeId;
  label: string;
  description: string;
  routeLabel: string;
  href: string;
  workstream: AccountingWorkstream;
  ariaLabel: string;
}

export interface AccountingSectionVisibilityViewModel {
  showCloseCockpitLanding: boolean;
  showWorkflowDetails: boolean;
  showMultiAssetCoverage: boolean;
  showExternalGl: boolean;
  showConfiguration: boolean;
  showJournalEntries: boolean;
  showCapitalAccounts: boolean;
  showApprovals: boolean;
  showExceptionWorkbench: boolean;
  showPosture: boolean;
  showReconciliation: boolean;
  showLedgerExplorer: boolean;
  showSecurityMaster: boolean;
  showReconciliationActions: boolean;
  showReporting: boolean;
}

type AccountingTaskModeDefinition = Omit<AccountingTaskModeViewModel, "workstream" | "ariaLabel">;

const accountingTaskModeDefinitions: Record<AccountingTaskModeId, AccountingTaskModeDefinition> = {
  "close-cockpit": {
    id: "close-cockpit",
    label: "Close Cockpit",
    description: "Daily close posture, blockers, owners, affected outputs, next actions, and retained proof stay first for Accounting operators.",
    routeLabel: "Close Cockpit",
    href: WORKSTATION_ROUTE_CATALOG.accounting
  },
  "reconciliation-casework": {
    id: "reconciliation-casework",
    label: "Reconciliation Casework",
    description: "Statement runs, open breaks, owners, evidence, comments, and resolution actions stay grouped for case handling.",
    routeLabel: "Reconciliation Casework",
    href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation
  },
  "external-gl-reconciliation": {
    id: "external-gl-reconciliation",
    label: "External GL Reconciliation",
    description: "Provider imports, account mappings, tie-out evidence, and guarded export packages stay in one focused external-ledger workflow.",
    routeLabel: "External GL Reconciliation",
    href: WORKSTATION_ROUTE_CATALOG.accountingExternalGlReconciliation
  },
  "ledger-explorer": {
    id: "ledger-explorer",
    label: "Ledger Explorer",
    description: "Meridian-owned trial balance, journal support, reconciliation posture, report usage, and proof drill-through stay together.",
    routeLabel: "Ledger Explorer",
    href: WORKSTATION_ROUTE_CATALOG.accountingLedger
  },
  "journal-entry": {
    id: "journal-entry",
    label: "Journal Entry",
    description: "Manual journal drafting, validation, evidence attachment, and approval submission stay in one governed task mode.",
    routeLabel: "Journal Entry",
    href: WORKSTATION_ROUTE_CATALOG.accountingJournalEntries
  },
  "capital-accounts": {
    id: "capital-accounts",
    label: "Capital Accounts",
    description: "Investor capital activity, allocation proof, statement lineage, and report-output readiness stay connected.",
    routeLabel: "Capital Accounts",
    href: WORKSTATION_ROUTE_CATALOG.accountingCapitalAccounts
  },
  exceptions: {
    id: "exceptions",
    label: "Exceptions",
    description: "Material accounting exceptions, ownership, evidence, and resolution actions stay in one focused review queue.",
    routeLabel: "Exceptions",
    href: WORKSTATION_ROUTE_CATALOG.accountingExceptions
  },
  "security-master": {
    id: "security-master",
    label: "Security Master",
    description: "Search governed instrument identity, inspect conflicts, and review the evidence supporting each security record.",
    routeLabel: "Security Master",
    href: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster
  },
  approvals: {
    id: "approvals",
    label: "Approvals",
    description: "Review exactly what is awaiting approval, why it is ready, its retained evidence, and the effect of each decision.",
    routeLabel: "Approvals",
    href: WORKSTATION_ROUTE_CATALOG.accountingApprovals
  },
  configure: {
    id: "configure",
    label: "Configure",
    description: "Set up ledger books and posting controls, verify readiness, and activate only configurations that pass governed checks.",
    routeLabel: "Configure",
    href: WORKSTATION_ROUTE_CATALOG.accountingConfigure
  },
  "delivery-evidence": {
    id: "delivery-evidence",
    label: "Delivery Evidence",
    description: "Accounting evidence routes into governed report packs, retained manifests, exports, and audit-ready output support.",
    routeLabel: "Delivery Evidence",
    href: WORKSTATION_ROUTE_CATALOG.reportingEvidence
  }
};

const accountingWorkstreamTaskModes: Record<AccountingWorkstream, AccountingTaskModeId> = {
  ledger: "ledger-explorer",
  configure: "configure",
  "journal-entries": "journal-entry",
  "capital-accounts": "capital-accounts",
  reconciliation: "reconciliation-casework",
  "external-gl": "external-gl-reconciliation",
  exceptions: "exceptions",
  "security-master": "security-master",
  approvals: "approvals",
  reporting: "delivery-evidence"
};

export function resolveAccountingWorkstream(pathname: string): AccountingWorkstream {
  if (pathname.includes("/reconciliation/external-gl")) {
    return "external-gl";
  }

  if (pathname.startsWith(`${WORKSTATION_ROUTE_CATALOG.accounting}/reporting`)) {
    return "reporting";
  }

  if (pathname.includes("/configure")) {
    return "configure";
  }

  if (pathname.includes("/journal-entries")) {
    return "journal-entries";
  }

  if (pathname.includes("/capital-accounts")) {
    return "capital-accounts";
  }

  if (pathname.includes("/reconciliation")) {
    return "reconciliation";
  }

  if (pathname.includes("/exceptions")) {
    return "exceptions";
  }

  if (pathname.includes("/security-master")) {
    return "security-master";
  }

  if (pathname.includes("/approvals")) {
    return "approvals";
  }

  return "ledger";
}

export function buildAccountingTaskMode(pathname: string): AccountingTaskModeViewModel {
  const normalizedPath = normalizeAccountingTaskModePath(pathname);
  const workstream = resolveAccountingWorkstream(normalizedPath);
  const taskModeId = normalizedPath === WORKSTATION_ROUTE_CATALOG.accounting || normalizedPath === "/governance"
    ? "close-cockpit"
    : accountingWorkstreamTaskModes[workstream];

  return buildAccountingTaskModeViewModel(accountingTaskModeDefinitions[taskModeId], workstream);
}

export function buildAccountingSectionVisibility(
  taskMode: AccountingTaskModeViewModel,
  hash: string = ""
): AccountingSectionVisibilityViewModel {
  const isCloseCockpitLanding = taskMode.id === "close-cockpit" && taskMode.workstream === "ledger";
  const visibility: AccountingSectionVisibilityViewModel = {
    showCloseCockpitLanding: isCloseCockpitLanding,
    showWorkflowDetails: false,
    showMultiAssetCoverage: false,
    showExternalGl: taskMode.workstream === "external-gl",
    showConfiguration: taskMode.workstream === "configure",
    showJournalEntries: taskMode.workstream === "journal-entries",
    showCapitalAccounts: taskMode.workstream === "capital-accounts",
    showApprovals: taskMode.workstream === "approvals",
    showExceptionWorkbench: taskMode.workstream === "exceptions",
    showPosture: false,
    showReconciliation: taskMode.workstream === "reconciliation",
    showLedgerExplorer: taskMode.workstream === "ledger" && !isCloseCockpitLanding,
    showSecurityMaster: taskMode.workstream === "security-master",
    showReconciliationActions: taskMode.workstream === "reconciliation" || taskMode.workstream === "exceptions",
    showReporting: taskMode.workstream === "reporting"
  };

  const targetId = normalizeAccountingHashTarget(hash);
  if (!targetId) {
    return visibility;
  }

  const forcedVisibility = accountingSectionHashVisibility[targetId];
  if (!forcedVisibility) {
    return visibility;
  }

  return {
    ...visibility,
    showCloseCockpitLanding: false,
    showWorkflowDetails: true,
    ...forcedVisibility
  };
}

export function resolveGovernanceWorkstream(pathname: string): AccountingWorkstream {
  return resolveAccountingWorkstream(pathname);
}

export function accountingWorkstreamHref(workstream: AccountingWorkstream): string {
  switch (workstream) {
    case "configure":
      return WORKSTATION_ROUTE_CATALOG.accountingConfigure;
    case "journal-entries":
      return WORKSTATION_ROUTE_CATALOG.accountingJournalEntries;
    case "capital-accounts":
      return WORKSTATION_ROUTE_CATALOG.accountingCapitalAccounts;
    case "reconciliation":
      return WORKSTATION_ROUTE_CATALOG.accountingReconciliation;
    case "external-gl":
      return WORKSTATION_ROUTE_CATALOG.accountingExternalGlReconciliation;
    case "exceptions":
      return WORKSTATION_ROUTE_CATALOG.accountingExceptions;
    case "security-master":
      return WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster;
    case "approvals":
      return WORKSTATION_ROUTE_CATALOG.accountingApprovals;
    case "reporting":
      return `${WORKSTATION_ROUTE_CATALOG.accounting}/reporting`;
    case "ledger":
    default:
      return WORKSTATION_ROUTE_CATALOG.accountingLedger;
  }
}

function normalizeAccountingTaskModePath(pathname: string): string {
  const path = pathname.split("?")[0]?.split("#")[0]?.trim() || WORKSTATION_ROUTE_CATALOG.accounting;
  if (path.length > 1 && path.endsWith("/")) {
    return path.slice(0, -1).toLowerCase();
  }

  return path.toLowerCase();
}

function normalizeAccountingHashTarget(hash: string): string | null {
  const target = hash.replace(/^#/, "").trim();
  return target.length > 0 ? target : null;
}

const accountingSectionHashVisibility: Record<string, Partial<AccountingSectionVisibilityViewModel>> = {
  "accounting-posture": { showPosture: true },
  "accounting-exceptions": { showReconciliation: true },
  "accounting-actions": { showReconciliationActions: true },
  "accounting-history": { showReconciliationActions: true },
  "reconciliation-break-queue": { showReconciliationActions: true },
  "manual-je-heading": { showJournalEntries: true },
  "manual-je-balance-impact-heading": { showJournalEntries: true },
  "accounting-configure-heading": { showConfiguration: true },
  "security-master-search": { showSecurityMaster: true },
  "security-detail-page-title": { showSecurityMaster: true },
  "accounting-reporting": { showReporting: true }
};

function buildAccountingTaskModeViewModel(
  definition: AccountingTaskModeDefinition,
  workstream: AccountingWorkstream
): AccountingTaskModeViewModel {
  return {
    ...definition,
    workstream,
    ariaLabel: `Accounting task mode ${definition.label}`
  };
}
