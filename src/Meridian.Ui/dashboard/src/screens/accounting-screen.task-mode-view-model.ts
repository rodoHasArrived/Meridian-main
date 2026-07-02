import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export type AccountingWorkstream =
  | "ledger"
  | "configure"
  | "journal-entries"
  | "capital-accounts"
  | "reconciliation"
  | "exceptions"
  | "security-master"
  | "approvals"
  | "reporting";
export type GovernanceWorkstream = AccountingWorkstream;

export type AccountingTaskModeId =
  | "close-cockpit"
  | "reconciliation-casework"
  | "ledger-explorer"
  | "journal-entry"
  | "capital-accounts"
  | "delivery-evidence"
  | "governance";

export interface AccountingTaskModeViewModel {
  id: AccountingTaskModeId;
  label: string;
  description: string;
  routeLabel: string;
  href: string;
  workstream: AccountingWorkstream;
  ariaLabel: string;
}

export interface AccountingTaskModeLinkViewModel {
  id: AccountingTaskModeId;
  label: string;
  description: string;
  href: string;
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
  "delivery-evidence": {
    id: "delivery-evidence",
    label: "Delivery Evidence",
    description: "Accounting evidence routes into governed report packs, retained manifests, exports, and audit-ready output support.",
    routeLabel: "Delivery Evidence",
    href: WORKSTATION_ROUTE_CATALOG.reportingEvidence
  },
  governance: {
    id: "governance",
    label: "Governance",
    description: "Books, posting controls, Security Master readiness, source coverage, and activation gates stay in governed setup paths.",
    routeLabel: "Governance",
    href: WORKSTATION_ROUTE_CATALOG.accountingConfigure
  }
};

const accountingWorkstreamTaskModes: Record<AccountingWorkstream, AccountingTaskModeId> = {
  ledger: "ledger-explorer",
  configure: "governance",
  "journal-entries": "journal-entry",
  "capital-accounts": "capital-accounts",
  reconciliation: "reconciliation-casework",
  exceptions: "reconciliation-casework",
  "security-master": "governance",
  approvals: "close-cockpit",
  reporting: "delivery-evidence"
};

const accountingTaskModeLauncherOrder: AccountingTaskModeId[] = [
  "reconciliation-casework",
  "ledger-explorer",
  "journal-entry",
  "capital-accounts",
  "delivery-evidence",
  "governance"
];

export const accountingTaskModeLauncherLinks: readonly AccountingTaskModeLinkViewModel[] =
  accountingTaskModeLauncherOrder.map((id) => {
    const definition = accountingTaskModeDefinitions[id];
    return {
      id,
      label: definition.label,
      description: definition.description,
      href: definition.href
    };
  });

export function resolveAccountingWorkstream(pathname: string): AccountingWorkstream {
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
    showWorkflowDetails: !isCloseCockpitLanding,
    showMultiAssetCoverage: !isCloseCockpitLanding,
    showExternalGl: !isCloseCockpitLanding,
    showConfiguration: taskMode.workstream === "configure",
    showJournalEntries: taskMode.workstream === "journal-entries",
    showCapitalAccounts: taskMode.workstream === "capital-accounts",
    showApprovals: taskMode.workstream === "approvals",
    showExceptionWorkbench: taskMode.workstream === "exceptions",
    showPosture: !isCloseCockpitLanding,
    showReconciliation: taskMode.workstream === "reconciliation",
    showLedgerExplorer: taskMode.workstream === "ledger" && !isCloseCockpitLanding,
    showSecurityMaster: taskMode.workstream === "security-master",
    showReconciliationActions: taskMode.workstream === "reconciliation" || taskMode.workstream === "exceptions"
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
  "security-detail-page-title": { showSecurityMaster: true }
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
