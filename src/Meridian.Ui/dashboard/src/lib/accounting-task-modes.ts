import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export type AccountingWorkstream =
  | "close-cockpit"
  | "ledger"
  | "configure"
  | "journal-entries"
  | "capital-accounts"
  | "reconciliation"
  | "exceptions"
  | "security-master"
  | "approvals"
  | "evidence"
  | "reporting";

export type GovernanceWorkstream = AccountingWorkstream;

export type AccountingTaskModeId =
  | "close-cockpit"
  | "reconciliation-casework"
  | "ledger-explorer"
  | "journal-entry"
  | "capital-accounts"
  | "rules"
  | "security-master"
  | "evidence";

export type AccountingTaskModeTone = "default" | "success" | "warning" | "danger";
export type AccountingTaskModeBadgeVariant = "default" | "outline" | "success" | "warning" | "danger";

export interface AccountingTaskMode {
  id: AccountingTaskModeId;
  label: string;
  detail: string;
  href: string;
  statusLabel: string;
  countLabel: string;
  badgeVariant: AccountingTaskModeBadgeVariant;
  recommended: boolean;
  active: boolean;
}

export interface AccountingTaskModeWorkspaceData {
  breakQueue: readonly unknown[];
  reconciliationQueue: readonly unknown[];
  reporting: {
    profileCount: number;
  };
}

export interface AccountingTaskModeCloseCommandCenter {
  blockerRows: readonly unknown[];
  statusTone: AccountingTaskModeTone;
  statusLabel: string;
  metricRows: ReadonlyArray<{
    id: string;
    value: string;
    tone: AccountingTaskModeTone;
  }>;
}

export interface AccountingTaskModeWorkflowLaunch {
  steps: ReadonlyArray<{
    id: string;
    statusLabel: string;
    metricValue: string;
    tone: AccountingTaskModeTone;
  }>;
}

export interface AccountingTaskModeInput {
  pathname: string;
  data: AccountingTaskModeWorkspaceData;
  closeCommandCenter: AccountingTaskModeCloseCommandCenter | null;
  workflowLaunch: AccountingTaskModeWorkflowLaunch | null;
}

export function resolveAccountingWorkstream(pathname: string): AccountingWorkstream {
  const normalizedPath = pathname.split(/[?#]/)[0]?.replace(/\/+$/, "") || WORKSTATION_ROUTE_CATALOG.accounting;

  if (normalizedPath.startsWith(WORKSTATION_ROUTE_CATALOG.reporting)) {
    return "reporting";
  }

  if (
    normalizedPath === WORKSTATION_ROUTE_CATALOG.accounting ||
    normalizedPath === WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity
  ) {
    return "close-cockpit";
  }

  if (normalizedPath.includes("/configure")) {
    return "configure";
  }

  if (normalizedPath.includes("/journal-entries")) {
    return "journal-entries";
  }

  if (normalizedPath.includes("/capital-accounts")) {
    return "capital-accounts";
  }

  if (normalizedPath.includes("/reconciliation")) {
    return "reconciliation";
  }

  if (normalizedPath.includes("/exceptions")) {
    return "exceptions";
  }

  if (normalizedPath.includes("/security-master")) {
    return "security-master";
  }

  if (normalizedPath.includes("/approvals")) {
    return "approvals";
  }

  if (normalizedPath.includes("/evidence")) {
    return "evidence";
  }

  if (normalizedPath.includes("/ledger")) {
    return "ledger";
  }

  return "close-cockpit";
}

export function resolveGovernanceWorkstream(pathname: string): GovernanceWorkstream {
  return resolveAccountingWorkstream(pathname);
}

export function buildAccountingTaskModes({
  pathname,
  data,
  closeCommandCenter,
  workflowLaunch
}: AccountingTaskModeInput): AccountingTaskMode[] {
  const stepById = new Map(workflowLaunch?.steps.map((step) => [step.id, step]) ?? []);
  const closeBlockerCount = closeCommandCenter?.blockerRows.length ?? 0;
  const closeTone = closeCommandCenter?.statusTone ?? "default";
  const reconciliationStep = stepById.get("reconciliation");
  const exceptionsStep = stepById.get("exceptions");
  const ledgerStep = stepById.get("ledger");
  const journalStep = stepById.get("journal-entries");
  const capitalStep = stepById.get("capital-accounts");
  const rulesStep = stepById.get("configure");
  const securityStep = stepById.get("security-master");
  const sourceMetric = closeCommandCenter?.metricRows.find((item) => item.id === "source-files") ?? null;
  const reportPackMetric = closeCommandCenter?.metricRows.find((item) => item.id === "report-pack") ?? null;
  const evidenceTone = strongestAccountingTaskTone(sourceMetric?.tone, reportPackMetric?.tone);
  const normalizedPath = pathname.split(/[?#]/)[0]?.replace(/\/+$/, "") || WORKSTATION_ROUTE_CATALOG.accounting;

  return [
    {
      id: "close-cockpit",
      label: "Close Cockpit",
      detail: "Close readiness, blockers, approvals, report-pack posture, and retained proof stay first.",
      href: WORKSTATION_ROUTE_CATALOG.accounting,
      statusLabel: closeCommandCenter?.statusLabel ?? "Loading",
      countLabel: `${closeBlockerCount} blocker${closeBlockerCount === 1 ? "" : "s"}`,
      badgeVariant: accountingTaskModeBadgeVariant(closeTone),
      recommended: closeTone === "warning" || closeTone === "danger",
      active: normalizedPath === WORKSTATION_ROUTE_CATALOG.accounting ||
        normalizedPath === WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity
    },
    {
      id: "reconciliation-casework",
      label: "Reconciliation Casework",
      detail: "Statement runs, open breaks, exception cases, comments, and sign-off evidence stay together.",
      href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      statusLabel: reconciliationStep?.statusLabel ?? "Review",
      countLabel: `${reconciliationStep?.metricValue ?? String(data.breakQueue.length)} open breaks`,
      badgeVariant: accountingTaskModeBadgeVariant(strongestAccountingTaskTone(reconciliationStep?.tone, exceptionsStep?.tone)),
      recommended: accountingTaskToneNeedsAttention(reconciliationStep?.tone) || accountingTaskToneNeedsAttention(exceptionsStep?.tone),
      active: normalizedPath === WORKSTATION_ROUTE_CATALOG.accountingReconciliation ||
        normalizedPath === WORKSTATION_ROUTE_CATALOG.accountingExceptions
    },
    {
      id: "ledger-explorer",
      label: "Ledger Explorer",
      detail: "Meridian-owned trial balance, journal evidence, dimensions, and proof drill-throughs stay source-backed.",
      href: WORKSTATION_ROUTE_CATALOG.accountingLedger,
      statusLabel: ledgerStep?.statusLabel ?? "Ledger authority",
      countLabel: `${data.reconciliationQueue.length} runs`,
      badgeVariant: accountingTaskModeBadgeVariant(ledgerStep?.tone),
      recommended: accountingTaskToneNeedsAttention(ledgerStep?.tone),
      active: normalizedPath === WORKSTATION_ROUTE_CATALOG.accountingLedger
    },
    {
      id: "journal-entry",
      label: "Journal Entry",
      detail: "Manual journal drafts, line validation, Security Master attribution, and approval submission stay governed.",
      href: WORKSTATION_ROUTE_CATALOG.accountingJournalEntries,
      statusLabel: journalStep?.statusLabel ?? "Ready",
      countLabel: `${journalStep?.metricValue ?? "0"} pending`,
      badgeVariant: accountingTaskModeBadgeVariant(journalStep?.tone),
      recommended: accountingTaskToneNeedsAttention(journalStep?.tone),
      active: normalizedPath === WORKSTATION_ROUTE_CATALOG.accountingJournalEntries
    },
    {
      id: "capital-accounts",
      label: "Capital Accounts",
      detail: "Investor capital-account evidence, allocation policy traces, statements, and audit support stay connected.",
      href: WORKSTATION_ROUTE_CATALOG.accountingCapitalAccounts,
      statusLabel: capitalStep?.statusLabel ?? "No activity",
      countLabel: `${capitalStep?.metricValue ?? "0"} investor rows`,
      badgeVariant: accountingTaskModeBadgeVariant(capitalStep?.tone),
      recommended: accountingTaskToneNeedsAttention(capitalStep?.tone),
      active: normalizedPath === WORKSTATION_ROUTE_CATALOG.accountingCapitalAccounts
    },
    {
      id: "rules",
      label: "Rules",
      detail: "Books, chart, posting rules, dry-run proof, promotion approval, and setup readiness stay service-owned.",
      href: WORKSTATION_ROUTE_CATALOG.accountingConfigure,
      statusLabel: rulesStep?.statusLabel ?? "Connected",
      countLabel: rulesStep?.metricValue ?? "Shared setup",
      badgeVariant: accountingTaskModeBadgeVariant(rulesStep?.tone),
      recommended: accountingTaskToneNeedsAttention(rulesStep?.tone),
      active: normalizedPath === WORKSTATION_ROUTE_CATALOG.accountingConfigure ||
        normalizedPath === WORKSTATION_ROUTE_CATALOG.accountingEntitySetup
    },
    {
      id: "security-master",
      label: "Security Master",
      detail: "Instrument identity, provider confidence, schedules, lots, and downstream readiness stay in Accounting.",
      href: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster,
      statusLabel: securityStep?.statusLabel ?? "Coverage review",
      countLabel: `${securityStep?.metricValue ?? "0"} gaps`,
      badgeVariant: accountingTaskModeBadgeVariant(securityStep?.tone),
      recommended: accountingTaskToneNeedsAttention(securityStep?.tone),
      active: normalizedPath === WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster
    },
    {
      id: "evidence",
      label: "Evidence",
      detail: "Retained close, reconciliation, journal, report, and audit documents remain review-only support.",
      href: WORKSTATION_ROUTE_CATALOG.accountingEvidence,
      statusLabel: reportPackMetric?.tone === "success" ? "Ready" : sourceMetric?.value ? "Evidence review" : "Review",
      countLabel: sourceMetric ? `${sourceMetric.value} source gaps` : `${data.reporting.profileCount} report profiles`,
      badgeVariant: accountingTaskModeBadgeVariant(evidenceTone),
      recommended: accountingTaskToneNeedsAttention(evidenceTone),
      active: normalizedPath === WORKSTATION_ROUTE_CATALOG.accountingEvidence
    }
  ];
}

function strongestAccountingTaskTone(
  left: AccountingTaskModeTone | null | undefined,
  right?: AccountingTaskModeTone | null
): AccountingTaskModeTone {
  const tones = [left, right];
  if (tones.includes("danger")) {
    return "danger";
  }

  if (tones.includes("warning")) {
    return "warning";
  }

  if (tones.includes("success")) {
    return "success";
  }

  return left ?? right ?? "default";
}

function accountingTaskToneNeedsAttention(tone: AccountingTaskModeTone | null | undefined): boolean {
  return tone === "warning" || tone === "danger";
}

function accountingTaskModeBadgeVariant(tone: AccountingTaskModeTone | null | undefined): AccountingTaskModeBadgeVariant {
  if (tone === "success" || tone === "warning" || tone === "danger") {
    return tone;
  }

  return "outline";
}
