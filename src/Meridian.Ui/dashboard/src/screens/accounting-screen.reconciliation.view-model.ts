import { getRunReviewPacketPath } from "@/lib/api";
import type { ApiErrorDisplay } from "@/lib/api-errors";
import {
  evidenceWorkbenchPath,
  normalizeLocalWorkstationRoute,
  WORKSTATION_ROUTE_CATALOG,
  workflowTargetPath,
} from "@/lib/workspace";
import {
  formatCurrency,
  formatCurrencyForCode,
  formatDateTimeLabel,
  formatSignedCurrency,
} from "./accounting-screen.formatting";
import { normalizeApiErrorDisplay } from "./accounting-screen.view-model.shared";
import type {
  CashFlowEvidenceTone,
  OperationalExceptionWorkbenchViewState,
  ReconciliationBreakAction,
  ReconciliationBreakDetailViewModel,
  ReconciliationBreakQueueState,
  ReconciliationBreakResolutionStatus,
  ReconciliationBreakRowViewModel,
  ReconciliationComparisonRowViewModel,
  ReconciliationComparisonViewState,
  ReconciliationDetailActionsViewModel,
  ReconciliationDetailFieldViewModel,
  ReconciliationDetailViewState,
  ReconciliationLineItemViewModel,
  ReconciliationQueuePanelViewState,
  ReconciliationQueueRunTone,
  ReconciliationResolveDialogState,
  ReconciliationRunDetailTabId,
  ReconciliationRunDetailTabViewModel,
  ReconciliationStatementRunRowViewModel,
  ReconciliationStatementRunsViewState,
} from "./accounting-screen.view-model";
import type {
  AccountingCashFlowSummary,
  AccountingSystemReconciliationRow,
  AccountingSystemReconciliationStatus,
  AccountingSystemReconciliationSummary,
  AccountingWorkspaceResponse,
  ReconciliationBreakQueueItem,
  StatementRunSummary,
} from "@/types";

type StatementRunSummaryWithMetadata = StatementRunSummary & {
  brokerCustodian?: string | null;
  account?: string | null;
  period?: string | null;
  status?: string | null;
  validationIssueCount?: number | null;
  matchCount?: number | null;
  breakCount?: number | null;
  caseCount?: number | null;
  importedAtUtc?: string | null;
  /**
   * False when the row was derived from the reconciliation queue rather than reported by the
   * statement-run service. Derived rows carry no match totals, and zero is a different fact from
   * "not reported" in a reconciliation: one says the statement matched nothing, the other says
   * Meridian does not know.
   */
  matchDataReported?: boolean;
};

export function resolveSelectedReconciliation(
  queue: AccountingWorkspaceResponse["reconciliationQueue"],
  selectedRunId: string | null
) {
  if (!selectedRunId) {
    return queue[0] ?? null;
  }

  return queue.find((item) => item.runId === selectedRunId) ?? null;
}

export function buildReconciliationDetailActions(
  item: AccountingWorkspaceResponse["reconciliationQueue"][number]
): ReconciliationDetailActionsViewModel {
  const openBreakLabel = `${item.openBreakCount} open break${item.openBreakCount === 1 ? "" : "s"}`;

  return {
    breakChecklistTargetId: "reconciliation-break-queue",
    breakChecklistHref: "#reconciliation-break-queue",
    breakChecklistLabel: "Open break checklist",
    breakChecklistAriaLabel: `Open break checklist for ${item.strategyName}; ${openBreakLabel}`,
    evidencePacketHref: evidenceWorkbenchPath("reconciliation-review", item.runId),
    evidencePacketLabel: "Evidence packet",
    evidencePacketAriaLabel: `Open reconciliation evidence packet for ${item.strategyName}`,
    auditPacketHref: getRunReviewPacketPath(item.runId),
    auditPacketLabel: "Review audit packet",
    auditPacketAriaLabel: `Review audit packet for ${item.strategyName}`
  };
}

export function buildReconciliationDetailViewState(
  item: AccountingWorkspaceResponse["reconciliationQueue"][number]
): ReconciliationDetailViewState {
  const openBreakTone: CashFlowEvidenceTone = item.openBreakCount === 0 ? "success" : "warning";
  const fields: ReconciliationDetailFieldViewModel[] = [
    buildReconciliationDetailField("Mode", item.mode.toUpperCase(), "default"),
    buildReconciliationDetailField("Run status", item.status, "default"),
    buildReconciliationDetailField("Break count", String(item.breakCount), "default"),
    buildReconciliationDetailField("Open breaks", String(item.openBreakCount), openBreakTone),
    buildReconciliationDetailField("Last updated", item.lastUpdated, "default")
  ];

  return {
    eyebrow: "Reconciliation detail",
    title: item.strategyName,
    description: `Current reconciliation status: ${formatReconciliationState(item.reconciliationStatus)}.`,
    ariaLabel: `Reconciliation detail for ${item.strategyName}`,
    narrative: buildReconciliationNarrative(item),
    narrativeLabel: `Reconciliation narrative for ${item.strategyName}`,
    fields
  };
}

interface ReconciliationStatementRunsBuildInput {
  statementRuns: StatementRunSummary[];
  fallbackQueue: AccountingWorkspaceResponse["reconciliationQueue"];
  selectedRunId: string | null;
  loading: boolean;
  error: ApiErrorDisplay | null;
}

interface ReconciliationComparisonBuildInput {
  statementRuns: StatementRunSummary[];
  fallbackQueue: AccountingWorkspaceResponse["reconciliationQueue"];
  selectedRunId: string | null;
  cashFlow: AccountingCashFlowSummary | null;
  /** Latest external GL reconciliation. When present, drives the panes with transaction-level line detail. */
  systemReconciliation?: AccountingSystemReconciliationSummary | null;
}

const SYSTEM_RECONCILIATION_TONE: Record<AccountingSystemReconciliationStatus, "success" | "warning" | "danger"> = {
  Matched: "success",
  ReviewRequired: "warning",
  Variance: "danger",
  MissingExternal: "danger",
  MissingMeridian: "danger"
};

function describeSystemReconciliationStatus(status: AccountingSystemReconciliationStatus): string {
  switch (status) {
    case "Matched":
      return "Matched";
    case "ReviewRequired":
      return "Review";
    case "Variance":
      return "Variance";
    case "MissingExternal":
      return "Missing in statement";
    case "MissingMeridian":
      return "Missing in ledger";
  }
}

function buildSystemReconciliationLine(
  row: AccountingSystemReconciliationRow,
  side: "statement" | "ledger"
): ReconciliationLineItemViewModel {
  const amount = side === "statement"
    ? row.externalDebit - row.externalCredit
    : row.meridianDebit - row.meridianCredit;
  return {
    id: `${row.rowId}:${side}`,
    matchKey: row.rowId,
    title: row.accountName || row.accountCode,
    meta: (row.accountName ? [row.accountCode, row.currency, row.detail] : [row.currency, row.detail])
      .map((part) => part?.trim())
      .filter(Boolean)
      .join(" · "),
    amountLabel: formatCurrencyForCode(amount, row.currency),
    statusLabel: describeSystemReconciliationStatus(row.status),
    statusTone: SYSTEM_RECONCILIATION_TONE[row.status]
  };
}

export function sortStatementRunsNewestFirst(
  statementRuns: readonly StatementRunSummaryWithMetadata[]
): StatementRunSummaryWithMetadata[] {
  return [...statementRuns].sort((left, right) => {
    const leftTimestamp = statementRunTimestamp(left);
    const rightTimestamp = statementRunTimestamp(right);
    if (leftTimestamp !== rightTimestamp) {
      return rightTimestamp - leftTimestamp;
    }

    return left.runId.localeCompare(right.runId);
  });
}

function statementRunTimestamp(run: StatementRunSummaryWithMetadata): number {
  for (const value of [run.importedAtUtc, run.completedAtUtc, run.startedAtUtc]) {
    if (!value) {
      continue;
    }

    const timestamp = Date.parse(value);
    if (Number.isFinite(timestamp)) {
      return timestamp;
    }
  }

  return Number.NEGATIVE_INFINITY;
}

export function buildReconciliationStatementRunsViewState({
  statementRuns,
  fallbackQueue,
  selectedRunId,
  loading,
  error
}: ReconciliationStatementRunsBuildInput): ReconciliationStatementRunsViewState {
  const detailPanelId = "statement-run-detail-tabs";
  const fallbackRows: StatementRunSummaryWithMetadata[] = statementRuns.length > 0
    ? []
    : fallbackQueue.map((item): StatementRunSummaryWithMetadata => ({
      runId: item.runId,
      importId: item.runId,
      startedAtUtc: item.lastUpdated,
      completedAtUtc: item.lastUpdated,
      // The queue carries no match totals. These zeros satisfy the shared summary shape only;
      // matchDataReported keeps them from being presented as reported results.
      positionMatches: 0,
      cashMatches: 0,
      transactionMatches: 0,
      matchDataReported: false,
      openExceptionCount: item.openBreakCount,
      status: item.reconciliationStatus,
      breakCount: item.breakCount,
      caseCount: item.openBreakCount,
      importedAtUtc: item.lastUpdated
    }));
  const sourceRows = sortStatementRunsNewestFirst(statementRuns.length > 0 ? statementRuns : fallbackRows);
  const effectiveSelectedRunId = resolveStatementRunSelection(sourceRows, selectedRunId);
  const rows = sourceRows.map((run) => buildStatementRunRow(run, effectiveSelectedRunId, detailPanelId));
  const selected = sourceRows.find((run) => run.runId === effectiveSelectedRunId) ?? null;

  return {
    title: "Statement runs",
    description: "Broker and custodian statement imports stay anchored to Meridian reconciliation data; the screen presents service-supplied match, break, and case counts.",
    tableLabel: "Accounting statement runs",
    tableCaption: "Statement run list with broker or custodian, account, period, status, validation issue count, match count, break count, case count, and imported timestamp.",
    detailPanelId,
    emptyText: "No broker or custodian statement runs are available for this accounting period.",
    loadingText: loading ? "Loading statement runs from Meridian reconciliation data." : null,
    errorText: error?.summary ?? null,
    errorDetails: error?.details ?? [],
    recoveryActionLabel: "Retry statement runs",
    recoveryActionAriaLabel: "Retry loading Accounting statement runs",
    statusAnnouncement: loading
      ? "Statement runs loading."
      : error
        ? "Statement runs failed to load."
        : `${rows.length} statement run${rows.length === 1 ? "" : "s"} available.`,
    hasRows: rows.length > 0,
    rows,
    tabs: buildReconciliationRunDetailTabs(selected)
  };
}

export function buildReconciliationComparisonViewState({
  statementRuns,
  fallbackQueue,
  selectedRunId,
  cashFlow,
  systemReconciliation = null
}: ReconciliationComparisonBuildInput): ReconciliationComparisonViewState {
  const fallbackRows: StatementRunSummaryWithMetadata[] = statementRuns.length > 0
    ? []
    : fallbackQueue.map((item): StatementRunSummaryWithMetadata => ({
      runId: item.runId,
      importId: item.runId,
      startedAtUtc: item.lastUpdated,
      completedAtUtc: item.lastUpdated,
      positionMatches: 0,
      cashMatches: Math.max(item.breakCount - item.openBreakCount, 0),
      transactionMatches: 0,
      openExceptionCount: item.openBreakCount,
      brokerCustodian: item.strategyName,
      account: item.mode.toUpperCase(),
      period: item.lastUpdated,
      status: item.reconciliationStatus,
      breakCount: item.breakCount,
      caseCount: item.openBreakCount,
      importedAtUtc: item.lastUpdated
    }));
  const sourceRows = sortStatementRunsNewestFirst(statementRuns.length > 0 ? statementRuns : fallbackRows);
  const effectiveSelectedRunId = resolveStatementRunSelection(sourceRows, selectedRunId);
  const sortedRows = [
    ...sourceRows.filter((row) => row.runId === effectiveSelectedRunId),
    ...sourceRows.filter((row) => row.runId !== effectiveSelectedRunId)
  ].slice(0, 4);
  const rows = sortedRows.map((run, index): ReconciliationComparisonRowViewModel => {
    const matchCount = run.matchCount ?? run.positionMatches + run.cashMatches + run.transactionMatches;
    const openCount = run.openExceptionCount;
    const rawStatus = run.status ?? (openCount > 0 ? "Open" : "Matched");
    const statusLabel = formatReconciliationState(rawStatus);
    const brokerCustodian = run.brokerCustodian?.trim() || `Statement ${index + 1}`;
    const account = run.account?.trim() || run.importId;
    const period = run.period?.trim() || run.completedAtUtc || run.startedAtUtc;
    const queueMatch = fallbackQueue.find((item) => item.runId === run.runId);
    const ledgerTitle = queueMatch?.strategyName ?? "Meridian ledger";
    const ledgerMeta = [
      matchCount.toLocaleString() + " matched",
      openCount > 0 ? openCount.toLocaleString() + " open" : "no open breaks"
    ].join(" · ");

    return {
      id: run.runId,
      statementTitle: brokerCustodian,
      statementMeta: `${period} · ${account}`,
      statementValue: index === 0 && cashFlow ? formatCurrency(cashFlow.totalCash) : `${matchCount.toLocaleString()} matched`,
      ledgerTitle,
      ledgerMeta,
      ledgerValue: index === 0 && cashFlow ? formatCurrency(cashFlow.totalLedgerCash) : (openCount > 0 ? `${openCount.toLocaleString()} open` : "Matched"),
      statusLabel,
      statusTone: rawStatus === "SecurityCoverageOpen"
        ? "danger"
        : openCount > 0 || rawStatus === "BreaksOpen"
          ? "warning"
          : "success"
    };
  });
  // Transaction-level line detail comes from the latest external GL reconciliation when loaded.
  // Each row carries both a statement (external) and ledger (Meridian) side keyed by rowId, so the
  // two panes cross-highlight by matchKey. MissingExternal / MissingMeridian rows are one-sided.
  const systemRows = systemReconciliation?.rows ?? [];
  const hasTransactionLines = systemRows.length > 0;

  const statementLines: ReconciliationLineItemViewModel[] = hasTransactionLines
    ? systemRows
      .filter((row) => row.status !== "MissingExternal")
      .map((row) => buildSystemReconciliationLine(row, "statement"))
    : rows.map((row) => ({
      id: `${row.id}:statement`,
      matchKey: row.id,
      title: row.statementTitle,
      meta: row.statementMeta,
      amountLabel: row.statementValue,
      statusLabel: row.statusLabel,
      statusTone: row.statusTone
    }));

  const ledgerLines: ReconciliationLineItemViewModel[] = hasTransactionLines
    ? systemRows
      .filter((row) => row.status !== "MissingMeridian")
      .map((row) => buildSystemReconciliationLine(row, "ledger"))
    : rows.map((row) => ({
      id: `${row.id}:ledger`,
      matchKey: row.id,
      title: row.ledgerTitle,
      meta: row.ledgerMeta,
      amountLabel: row.ledgerValue,
      statusLabel: row.statusLabel,
      statusTone: row.statusTone
    }));

  // Summary + badges follow the active line source so the panes and totals agree.
  let matchedCount: number;
  let openCount: number;
  let statementBalanceLabel: string;
  let ledgerBalanceLabel: string;
  let varianceLabel: string;
  let varianceTone: "success" | "warning";

  if (hasTransactionLines && systemReconciliation) {
    const summaryCurrency = systemReconciliation.rows[0]?.currency || "USD";
    const statementBalance = systemReconciliation.totalExternalDebits - systemReconciliation.totalExternalCredits;
    const ledgerBalance = systemReconciliation.totalMeridianDebits - systemReconciliation.totalMeridianCredits;
    const netVariance = statementBalance - ledgerBalance;
    // Open breaks keep the reconciliation out of balance even when debit/credit totals tie out —
    // e.g. a trial balance with offsetting row-level breaks nets to zero but is not reconciled.
    const balanced = systemReconciliation.breakCount === 0 && Math.abs(netVariance) < 0.005;
    matchedCount = systemReconciliation.matchedCount;
    openCount = systemReconciliation.breakCount;
    statementBalanceLabel = formatCurrencyForCode(statementBalance, summaryCurrency);
    ledgerBalanceLabel = formatCurrencyForCode(ledgerBalance, summaryCurrency);
    varianceTone = balanced ? "success" : "warning";
    varianceLabel = balanced
      ? "Balanced"
      : Math.abs(netVariance) >= 0.005
        ? `Out by ${formatCurrencyForCode(Math.abs(netVariance), summaryCurrency)}`
        : `${openCount.toLocaleString()} open break${openCount === 1 ? "" : "s"}`;
  } else {
    matchedCount = sourceRows.reduce((total, run) => total + (run.matchCount ?? run.positionMatches + run.cashMatches + run.transactionMatches), 0);
    openCount = sourceRows.reduce((total, run) => total + run.openExceptionCount, 0);
    statementBalanceLabel = cashFlow ? formatCurrency(cashFlow.totalCash) : "Not loaded";
    ledgerBalanceLabel = cashFlow ? formatCurrency(cashFlow.totalLedgerCash) : "Not loaded";
    const variance = cashFlow?.netVariance ?? 0;
    varianceTone = Math.abs(variance) < 0.005 ? "success" : "warning";
    varianceLabel = Math.abs(variance) < 0.005 ? "Balanced" : `Out by ${formatCurrency(Math.abs(variance))}`;
  }

  return {
    title: "Cash reconciliation - broker statement vs. ledger",
    subtitle: cashFlow?.summary ?? "Broker and custodian statements are compared with Meridian ledger balances from shared reconciliation read models.",
    statementHeading: hasTransactionLines ? "Custodian statement" : "Statement",
    ledgerHeading: hasTransactionLines ? "Internal ledger" : "Ledger",
    matchedBadgeLabel: `${matchedCount.toLocaleString()} matched`,
    openBadgeLabel: `${openCount.toLocaleString()} open`,
    statementBalanceLabel,
    ledgerBalanceLabel,
    varianceLabel,
    varianceTone,
    rows,
    statementLines,
    ledgerLines,
    lineSource: hasTransactionLines ? "transactions" : "runs",
    ariaLabel: "Cash reconciliation broker statement versus ledger comparison"
  };
}

function resolveStatementRunSelection(
  sourceRows: readonly StatementRunSummaryWithMetadata[],
  selectedRunId: string | null
): string | null {
  return selectedRunId && sourceRows.some((run) => run.runId === selectedRunId)
    ? selectedRunId
    : sourceRows[0]?.runId ?? null;
}

function buildStatementRunRow(
  run: StatementRunSummaryWithMetadata,
  selectedRunId: string | null,
  detailPanelId: string
): ReconciliationStatementRunRowViewModel {
  const matchDataReported = run.matchDataReported !== false;
  const matchCount = run.matchCount ?? run.positionMatches + run.cashMatches + run.transactionMatches;
  const status = run.status ?? (run.openExceptionCount > 0 ? "ReviewRequired" : "Matched");
  const missing: string[] = [];
  if (!matchDataReported) {
    missing.push("Match counts");
  }
  const brokerCustodianLabel = valueOrMissing(run.brokerCustodian, "Broker/custodian", missing);
  const accountLabel = valueOrMissing(run.account, "Account", missing);
  const periodLabel = valueOrMissing(run.period, "Period", missing);
  const validationIssueCount = run.validationIssueCount ?? run.openExceptionCount;
  const breakCount = run.breakCount ?? run.openExceptionCount;
  const caseCount = run.caseCount ?? run.openExceptionCount;
  const importedAtLabel = formatDateTimeLabel(run.importedAtUtc ?? run.completedAtUtc ?? run.startedAtUtc);

  return {
    runId: run.runId,
    brokerCustodianLabel,
    accountLabel,
    periodLabel,
    statusLabel: formatReconciliationState(status),
    validationIssueCountLabel: String(validationIssueCount),
    matchCountLabel: matchDataReported ? String(matchCount) : "—",
    breakCountLabel: String(breakCount),
    caseCountLabel: String(caseCount),
    importedAtLabel,
    isSelected: run.runId === selectedRunId,
    controlsId: detailPanelId,
    ariaLabel: `Statement run ${run.runId}. ${status}. ${validationIssueCount} validation issues, ${matchDataReported ? `${matchCount} matches` : "match counts not reported"}, ${breakCount} breaks, ${caseCount} cases. Imported ${importedAtLabel}.`,
    selectAriaLabel: `Inspect statement run ${run.runId}`,
    unavailableReason: missing.length > 0 ? `${missing.join(", ")} not provided by statement run data.` : null
  };
}

function valueOrMissing(value: string | null | undefined, label: string, missing: string[]): string {
  const trimmed = value?.trim();
  if (trimmed) {
    return trimmed;
  }

  missing.push(label);
  return "—";
}

function buildReconciliationRunDetailTabs(run: StatementRunSummaryWithMetadata | null): ReconciliationRunDetailTabViewModel[] {
  const disabledReason = run ? null : "Select a statement run before opening this detail tab.";
  const matchCount = run ? run.matchCount ?? run.positionMatches + run.cashMatches + run.transactionMatches : 0;
  const openExceptionCount = run?.openExceptionCount ?? 0;
  // A derived run has no match totals. Badging a fabricated zero under a description that credits
  // the reconciliation service would state the opposite of the truth, so those tabs carry no badge
  // and say plainly that the totals were not reported.
  const matchDataReported = run !== null && run.matchDataReported !== false;
  const matchTotal = (value: number): string | null => (matchDataReported ? String(value) : null);
  const matchDescription = (reported: string, subject: string): string =>
    matchDataReported ? reported : `${subject} totals were not reported for this statement run.`;
  const tabs: Array<{ id: ReconciliationRunDetailTabId; label: string; badgeLabel: string | null; description: string }> = [
    { id: "overview", label: "Overview", badgeLabel: run ? formatReconciliationState(run.status) : null, description: "Statement source, account coverage, import timing, and reconciliation status." },
    { id: "validation", label: "Validation", badgeLabel: run ? String(run.validationIssueCount ?? openExceptionCount) : null, description: "Validation issues reported by the shared statement reconciliation run." },
    { id: "positions", label: "Positions", badgeLabel: run ? matchTotal(run.positionMatches) : null, description: matchDescription("Position match totals supplied by the reconciliation service.", "Position match") },
    { id: "cash", label: "Cash", badgeLabel: run ? matchTotal(run.cashMatches) : null, description: matchDescription("Cash match totals supplied by the reconciliation service.", "Cash match") },
    { id: "transactions", label: "Transactions", badgeLabel: run ? matchTotal(run.transactionMatches) : null, description: matchDescription("Transaction match totals supplied by the reconciliation service.", "Transaction match") },
    { id: "breaks-cases", label: "Breaks & Cases", badgeLabel: run ? String(run.breakCount ?? openExceptionCount) : null, description: "Break and case counts from reconciliation/casework read models; no case-state logic runs in React." },
    { id: "evidence", label: "Evidence", badgeLabel: run ? matchTotal(matchCount) : null, description: "Evidence packet and imported statement references available for review." }
  ];

  return tabs.map((tab) => ({
    ...tab,
    disabled: !run,
    disabledReason,
    ariaLabel: run
      ? `${tab.label} tab for statement run ${run.runId}. ${tab.description}`
      : `${tab.label} tab unavailable. ${disabledReason}`
  }));
}

export function buildReconciliationQueuePanelViewState(
  queue: AccountingWorkspaceResponse["reconciliationQueue"],
  selectedRunId: string | null
): ReconciliationQueuePanelViewState {
  const detailPanelId = "reconciliation-run-detail-panel";
  const effectiveSelectedRunId = selectedRunId ?? queue[0]?.runId ?? null;

  return {
    title: "Reconciliation detail queue",
    description: "Select a run to inspect its active reconciliation detail panel.",
    overviewTitle: "Reconciliation queue",
    overviewDescription: "Open breaks, timing drift, and balanced runs stay visible without leaving Accounting.",
    overviewCaption: "Read-only reconciliation queue summary. Open the reconciliation workstream to inspect selected run detail.",
    overviewActionHref: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
    overviewActionLabel: "Open reconciliation",
    overviewActionAriaLabel: "Open Accounting reconciliation workstream",
    listLabel: "Reconciliation runs",
    emptyText: "No reconciliation runs are available for this accounting scope.",
    detailPanelId,
    detailEmptyTitle: "No reconciliation run selected",
    detailEmptyText: "Reconciliation evidence is unavailable until workspace data includes at least one run.",
    detailEmptyAriaLabel: "No reconciliation run selected",
    hasRows: queue.length > 0,
    rows: queue.map((item) => {
      const isSelected = item.runId === effectiveSelectedRunId;
      return {
        runId: item.runId,
        strategyName: item.strategyName,
        modeLabel: formatReconciliationState(item.mode),
        runStatusLabel: item.status,
        reconciliationStatusLabel: formatReconciliationState(item.reconciliationStatus),
        reconciliationTone: reconciliationStatusTone(item.reconciliationStatus),
        breakCountLabel: `${item.breakCount} break${item.breakCount === 1 ? "" : "s"}`,
        openBreakLabel: `${item.openBreakCount} open`,
        lastUpdatedLabel: item.lastUpdated,
        isSelected,
        isExpanded: isSelected,
        controlsId: detailPanelId,
        ariaLabel: `${item.strategyName}. ${item.reconciliationStatus}. ${item.openBreakCount} open breaks. Updated ${item.lastUpdated}.`,
        selectAriaLabel: `Inspect reconciliation run ${item.strategyName}`
      };
    })
  };
}

function formatReconciliationState(value: string | null | undefined): string {
  const normalized = (value ?? "")
    .trim()
    .replace(/[_-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .replace(/\s+/g, " ")
    .toLowerCase();

  return normalized ? normalized.replace(/^\w/, (character) => character.toUpperCase()) : "Unavailable";
}

function reconciliationStatusTone(
  status: AccountingWorkspaceResponse["reconciliationQueue"][number]["reconciliationStatus"]
): ReconciliationQueueRunTone {
  if (status === "Balanced") {
    return "success";
  }

  if (status === "Resolved") {
    return "primary";
  }

  if (status === "NotStarted") {
    return "muted";
  }

  return "warning";
}

function buildReconciliationDetailField(
  label: string,
  value: string,
  tone: CashFlowEvidenceTone
): ReconciliationDetailFieldViewModel {
  return {
    label,
    value,
    tone,
    ariaLabel: `${label}: ${value}`
  };
}

export function buildReconciliationBreakQueueState({
  breakQueue,
  selectedBreakId,
  loading,
  loadError,
  action,
  actionError
}: {
  breakQueue: ReconciliationBreakQueueItem[];
  selectedBreakId?: string | null;
  loading: boolean;
  loadError: string | ApiErrorDisplay | null;
  action: ReconciliationBreakAction | null;
  actionError: string | ApiErrorDisplay | null;
}): ReconciliationBreakQueueState {
  const effectiveSelectedBreakId = selectedBreakId && breakQueue.some((item) => item.breakId === selectedBreakId)
    ? selectedBreakId
    : breakQueue[0]?.breakId ?? null;
  const rows = buildReconciliationBreakRows(breakQueue, action, effectiveSelectedBreakId);
  const selectedRow = rows.find((row) => row.breakId === effectiveSelectedBreakId) ?? null;
  const loadingText = loading ? "Loading reconciliation break queue..." : null;
  const normalizedLoadError = normalizeApiErrorDisplay(loadError);
  const normalizedActionError = normalizeApiErrorDisplay(actionError);
  const errorText = normalizedLoadError
    ? normalizedLoadError.summary.startsWith("Reconciliation break queue failed")
      ? normalizedLoadError.summary
      : `Reconciliation break queue failed: ${normalizedLoadError.summary}`
    : null;
  const actionErrorText = normalizedActionError
    ? normalizedActionError.summary.startsWith("Break ")
      ? normalizedActionError.summary
      : `Break action failed: ${normalizedActionError.summary}`
    : null;

  return {
    rows,
    hasBreaks: rows.length > 0,
    tableLabel: "Reconciliation break queue",
    tableCaption: "Selectable reconciliation break queue. Select a break row to inspect reason, ownership, audit timestamps, and routing detail.",
    detailPanelId: "reconciliation-break-detail-panel",
    selectedBreakId: effectiveSelectedBreakId,
    selectedDetail: selectedRow ? buildReconciliationBreakDetail(selectedRow) : null,
    detailEmptyTitle: "No reconciliation break selected",
    detailEmptyText: "Break detail is unavailable until the queue includes at least one active or historical break.",
    detailEmptyAriaLabel: "No reconciliation break selected",
    loadingText,
    emptyText: "No reconciliation breaks in the current queue.",
    errorText,
    errorDetails: normalizedLoadError?.details ?? [],
    actionErrorText,
    actionErrorDetails: normalizedActionError?.details ?? [],
    statusAnnouncement: buildReconciliationBreakStatusAnnouncement({
      loading,
      action,
      loadError: errorText,
      actionError: actionErrorText,
      breakCount: rows.length
    })
  };
}

export function buildOperationalExceptionWorkbenchState({
  reconciliationQueue,
  breakRows
}: {
  reconciliationQueue: AccountingWorkspaceResponse["reconciliationQueue"];
  breakRows: ReconciliationBreakRowViewModel[];
}): OperationalExceptionWorkbenchViewState {
  const activeBreaks = breakRows.filter((row) => row.status === "Open" || row.status === "InReview");
  const openRunBreakCount = reconciliationQueue.reduce((total, run) => total + run.openBreakCount, 0);
  const commentCount = breakRows.reduce((total, row) => total + (row.commentCount ?? 0), 0);
  const evidenceCount = breakRows.reduce((total, row) => total + (row.evidenceCount ?? 0), 0);
  const signoffCount = breakRows.filter((row) => (row.signoffStatus ?? "").trim() || row.status === "Resolved" || row.status === "SignedOff").length;
  const cases = activeBreaks.length > 0 ? activeBreaks : breakRows.slice(0, 5);

  return {
    title: "Operational exception workbench",
    description: "Unified review for reconciliation breaks, workflow state, comments, audit evidence, and approval handoffs.",
    metricRows: [
      {
        id: "active-breaks",
        label: "Active exceptions",
        value: String(activeBreaks.length),
        detail: `${openRunBreakCount} open break${openRunBreakCount === 1 ? "" : "s"} across reconciliation runs.`,
        tone: activeBreaks.length > 0 ? "warning" : "success"
      },
      {
        id: "comments",
        label: "Comments",
        value: String(commentCount),
        detail: "Comment counts come from shared reconciliation casework metadata.",
        tone: commentCount > 0 ? "default" : "warning"
      },
      {
        id: "audit-evidence",
        label: "Audit evidence",
        value: String(evidenceCount),
        detail: "Evidence links stay attached to the originating break and reporting packet.",
        tone: evidenceCount > 0 ? "success" : "warning"
      },
      {
        id: "signoff",
        label: "Sign-off states",
        value: String(signoffCount),
        detail: "Resolved, signed-off, or role-gated cases are routed back to Accounting approvals.",
        tone: signoffCount > 0 ? "default" : "warning"
      }
    ],
    cases: cases.map((row) => ({
      id: row.breakId,
      title: row.financeLabel,
      subtitle: `${row.strategyName} - ${row.reason}`,
      rawCategoryLabel: row.category,
      statusLabel: row.status,
      statusTone: row.statusBadgeVariant,
      ownerLabel: row.ownerLabel,
      slaLabel: buildReconciliationExceptionUrgency(row),
      commentLabel: `${row.commentCount ?? 0} comment${(row.commentCount ?? 0) === 1 ? "" : "s"}`,
      auditLabel: (row.evidenceCount ?? 0) > 0
        ? `${row.evidenceCount} evidence link${row.evidenceCount === 1 ? "" : "s"}`
        : "Evidence required",
      routeHref: buildReconciliationBreakRoutingHref(row.routingTarget) ?? WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
      routeLabel: row.routingTarget ? "Open routed workflow" : "Open reconciliation",
      ariaLabel: `${row.financeLabel}. ${row.status}. ${row.reason}`
    })),
    emptyText: "No reconciliation exceptions are available for this accounting scope.",
    reconciliationHref: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
    approvalsHref: WORKSTATION_ROUTE_CATALOG.accountingApprovals,
    evidenceHref: evidenceWorkbenchPath("accounting-exceptions", "active"),
    auditHref: WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity
  };
}

export function buildReconciliationResolveDialogState(
  breakId: string,
  status: ReconciliationBreakResolutionStatus,
  rationale: string
): ReconciliationResolveDialogState {
  const command = status === "Resolved" ? "resolve" : "dismiss";
  const commandLabel = status === "Resolved" ? "Resolve" : "Dismiss";
  const inputId = `rationale-${breakId}`;
  const helpId = `rationale-help-${breakId}`;

  return {
    breakId,
    status,
    rationale,
    inputId,
    helpId,
    formAriaLabel: `${commandLabel} reconciliation break ${breakId}`,
    label: `${commandLabel} rationale`,
    placeholder: `Describe why this break is being ${command === "resolve" ? "resolved" : "dismissed"}...`,
    helpText: "A rationale is required before this queue action can be submitted.",
    submitLabel: `Confirm ${command}`,
    submitAriaLabel: `Confirm ${command} for reconciliation break ${breakId}`,
    submitDisabledReason: rationale.trim()
      ? null
      : "Enter an operator rationale before confirming this queue action.",
    cancelLabel: "Cancel",
    cancelAriaLabel: `Cancel ${command} for reconciliation break ${breakId}`,
    isSubmitDisabled: !rationale.trim()
  };
}

export function buildReconciliationBreakRows(
  breakQueue: ReconciliationBreakQueueItem[],
  action: ReconciliationBreakAction | null,
  selectedBreakId: string | null = null
): ReconciliationBreakRowViewModel[] {
  return breakQueue.map((item) => {
    const actionBusy = action?.breakId === item.breakId;
    const assignBusy = actionBusy && action?.command === "assign";
    const resolveBusy = actionBusy && action?.command === "resolve";
    const dismissBusy = actionBusy && action?.command === "dismiss";
    const canAssign = !action && item.status === "Open";
    const canResolve = !action && item.status !== "Resolved";
    const canDismiss = !action && item.status !== "Dismissed";
    const isSelected = item.breakId === selectedBreakId;
    const financeLabel = financeBreakLabel(item.category);

    return {
      ...item,
      actionBusy,
      financeLabel,
      varianceLabel: formatSignedCurrency(item.variance),
      varianceTone: item.variance > 0 ? "success" : item.variance < 0 ? "danger" : "default",
      statusBadgeVariant: reconciliationBreakStatusBadgeVariant(item.status),
      detectedAtLabel: formatDateTimeLabel(item.detectedAt),
      lastUpdatedAtLabel: formatDateTimeLabel(item.lastUpdatedAt),
      ownerLabel: item.assignedTo ?? "Unassigned",
      rowAriaLabel: `${financeLabel} ${item.breakId}. ${item.status}. Variance ${formatSignedCurrency(item.variance)}. ${item.reason}`,
      rowSelectAriaLabel: `Inspect reconciliation break ${item.breakId}`,
      detailPanelId: "reconciliation-break-detail-panel",
      isSelected,
      isExpanded: isSelected,
      assignLabel: assignBusy ? "Assigning..." : "Assign",
      resolveLabel: resolveBusy ? "Resolving..." : "Resolve",
      dismissLabel: dismissBusy ? "Dismissing..." : "Dismiss",
      assignAriaLabel: `Assign reconciliation break ${item.breakId}`,
      resolveAriaLabel: `Resolve reconciliation break ${item.breakId}`,
      dismissAriaLabel: `Dismiss reconciliation break ${item.breakId}`,
      canAssign,
      canResolve,
      canDismiss,
      assignDisabledReason: buildBreakActionDisabledReason({
        item,
        action,
        busy: assignBusy,
        alreadyComplete: item.status !== "Open",
        busyReason: "Assignment is already in progress for this break.",
        completeReason: `Only open breaks can be assigned; this break is ${item.status}.`
      }),
      resolveDisabledReason: buildBreakActionDisabledReason({
        item,
        action,
        busy: resolveBusy,
        alreadyComplete: item.status === "Resolved",
        busyReason: "Resolution is already in progress for this break.",
        completeReason: "This break is already resolved."
      }),
      dismissDisabledReason: buildBreakActionDisabledReason({
        item,
        action,
        busy: dismissBusy,
        alreadyComplete: item.status === "Dismissed",
        busyReason: "Dismissal is already in progress for this break.",
        completeReason: "This break is already dismissed."
      })
    };
  });
}

function buildReconciliationBreakDetail(row: ReconciliationBreakRowViewModel): ReconciliationBreakDetailViewModel {
  const routingActionHref = buildReconciliationBreakRoutingHref(row.routingTarget);
  const explanation = row.breakExplanation;

  return {
    id: row.detailPanelId,
    eyebrow: "Break detail",
    title: `${row.strategyName} - ${row.financeLabel}`,
    subtitle: `Reconciliation exception - ${row.status}`,
    rawCategoryLabel: row.category,
    description: row.reason,
    ariaLabel: `Reconciliation break detail for ${row.breakId}`,
    statusLabel: row.status,
    statusBadgeVariant: row.statusBadgeVariant,
    fields: [
      { label: "Run", value: row.runId },
      { label: "Variance", value: row.varianceLabel },
      { label: "Owner", value: row.ownerLabel },
      { label: "Detected", value: row.detectedAtLabel },
      { label: "Updated", value: row.lastUpdatedAtLabel },
      { label: "Exception route", value: formatReconciliationMetadata(row.exceptionRoute, "Unrouted") },
      { label: "Tolerance profile", value: formatReconciliationMetadata(row.toleranceProfileId, "Unassigned") },
      { label: "Tolerance band", value: row.toleranceBand == null ? "Policy default" : formatCurrency(row.toleranceBand) },
      { label: "Priority", value: formatReconciliationMetadata(row.priority, "Normal") },
      { label: "Urgency", value: buildReconciliationExceptionUrgency(row) },
      { label: "SLA tone", value: formatReconciliationMetadata(row.slaBadgeTone, "info") },
      { label: "Age band", value: formatReconciliationMetadata(row.ageBand, "0-4h") },
      { label: "Root cause", value: formatReconciliationMetadata(row.rootCauseCode, "Unset") },
      { label: "Resolution code", value: formatReconciliationMetadata(row.resolutionCode, "Unset") },
      { label: "Comments", value: `${row.commentCount ?? 0} comment(s); latest: ${formatReconciliationMetadata(row.lastCommentExcerpt, "No visible comment")}` },
      { label: "Evidence links", value: `${row.evidenceCount ?? 0} evidence link(s)` },
      { label: "Related cases", value: `${row.relatedCaseCount ?? 0}` },
      { label: "Required sign-off", value: buildReconciliationBreakSignoffText(row) },
      { label: "Decision note", value: formatReconciliationMetadata(row.resolutionNote, "No decision captured") },
      { label: "Routing", value: row.routingTarget ?? "No routing target" },
      { label: "Fund account", value: row.fundAccountId ?? "Not scoped" },
      { label: "Explanation summary", value: formatReconciliationMetadata(explanation?.summary, "No shared explanation") },
      { label: "Source systems", value: formatReconciliationList(explanation?.sourceSystems, "No source systems") },
      { label: "Probable cause", value: formatReconciliationMetadata(explanation?.probableCause, "No probable cause") },
      { label: "Ledger impact", value: formatReconciliationMetadata(explanation?.ledgerImpact, "No ledger impact") },
      { label: "Suggested next action", value: formatReconciliationMetadata(explanation?.suggestedNextAction, "No suggested action") },
      { label: "Explanation evidence", value: formatReconciliationList(explanation?.evidenceLinks, "No explanation evidence") }
    ],
    analysisText: explanation?.summary ?? row.explainabilitySummary ?? null,
    recommendedActionText: explanation?.suggestedNextAction ?? row.recommendedAction ?? null,
    routingActionLabel: routingActionHref ? "Open routing target" : null,
    routingActionHref,
    routingActionAriaLabel: routingActionHref ? `Open routing target for reconciliation break ${row.breakId}` : null
  };
}

export function financeBreakLabel(category: string | null | undefined): string {
  const normalized = (category ?? "").trim().toLowerCase();
  if (normalized.includes("amount") || normalized.includes("cash") || normalized.includes("fee")) {
    return "Cash variance needs review";
  }
  if (normalized.includes("quantity") || normalized.includes("position")) {
    return "Position variance needs review";
  }
  if (normalized.includes("timing")) {
    return "Timing variance needs review";
  }
  return "Accounting exception needs review";
}

function formatReconciliationList(values: string[] | null | undefined, fallback: string): string {
  const normalized = values?.map((value) => value.trim()).filter(Boolean) ?? [];
  return normalized.length > 0 ? normalized.join(", ") : fallback;
}


function buildReconciliationSlaText(row: Pick<ReconciliationBreakQueueItem, "slaState" | "slaDueAt" | "slaWarningAt" | "slaBreachedAt">): string {
  const state = reconciliationSlaStateLabel(row.slaState);
  if (row.slaBreachedAt) {
    return `${state}; breached ${formatDateTimeLabel(row.slaBreachedAt)}`;
  }
  if (row.slaDueAt) {
    return `${state}; due ${formatDateTimeLabel(row.slaDueAt)}`;
  }
  if (row.slaWarningAt) {
    return `${state}; warning ${formatDateTimeLabel(row.slaWarningAt)}`;
  }
  return state;
}

function buildReconciliationExceptionUrgency(row: ReconciliationBreakRowViewModel): string {
  const active = row.status === "Open" || row.status === "InReview";
  const slaLabel = row.slaBadgeLabel?.trim() || buildReconciliationSlaText(row);

  if (row.slaBreachedAt || row.slaState === "Breached" || row.slaBadgeTone === "danger") {
    return slaLabel.startsWith("Breached") ? slaLabel : `SLA breached · ${slaLabel}`;
  }

  if (active) {
    const trustGaps: string[] = [];
    if (!row.assignedTo?.trim()) {
      trustGaps.push("assign owner");
    }
    if ((row.evidenceCount ?? 0) === 0) {
      trustGaps.push("attach evidence");
    }
    if (!row.requiredSignoffRole?.trim()) {
      trustGaps.push("set sign-off role");
    }
    if (trustGaps.length > 0) {
      return `Review required · ${trustGaps.join(" · ")}`;
    }
  }

  return slaLabel;
}

function reconciliationSlaStateLabel(state: ReconciliationBreakQueueItem["slaState"]): string {
  switch (state) {
    case "OnTrack":
      return "On track";
    case "NotStarted":
      return "Not started";
    case "Warning":
      return "Warning";
    case "Breached":
      return "Breached";
    case "Paused":
      return "Paused";
    case "Stopped":
      return "Stopped";
    default:
      return "SLA not assessed";
  }
}

function buildReconciliationBreakRoutingHref(routingTarget: string | null | undefined): string | null {
  const trimmedTarget = routingTarget?.trim();
  if (!trimmedTarget) {
    return null;
  }

  if (trimmedTarget.startsWith("/")) {
    return normalizeLocalWorkstationRoute(trimmedTarget) ?? trimmedTarget;
  }

  return workflowTargetPath(trimmedTarget, "accounting");
}

function reconciliationBreakStatusBadgeVariant(
  status: ReconciliationBreakQueueItem["status"]
): ReconciliationBreakRowViewModel["statusBadgeVariant"] {
  if (status === "Resolved") return "success";
  if (status === "InReview") return "warning";
  if (status === "Dismissed") return "outline";
  return "danger";
}

function buildReconciliationBreakSignoffText(row: Pick<ReconciliationBreakQueueItem, "requiredSignoffRole" | "signoffStatus" | "status" | "resolvedAt">): string {
  const role = formatReconciliationMetadata(row.requiredSignoffRole, "Not configured");
  const status = formatReconciliationMetadata(row.signoffStatus, "Pending");

  if (role === "Not configured") {
    return `Sign-off: ${status}. Required role is not configured.`;
  }

  if (row.resolvedAt && !status.toLowerCase().includes("signed")) {
    return `Decision captured; sign-off: ${status} by ${role}. Close approval remains blocked.`;
  }

  return `Sign-off: ${status} by ${role}.`;
}

function formatReconciliationMetadata(value: string | null | undefined, fallback: string): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : fallback;
}

function buildBreakActionDisabledReason({
  item,
  action,
  busy,
  alreadyComplete,
  busyReason,
  completeReason
}: {
  item: ReconciliationBreakQueueItem;
  action: ReconciliationBreakAction | null;
  busy: boolean;
  alreadyComplete: boolean;
  busyReason: string;
  completeReason: string;
}): string | null {
  if (busy) {
    return busyReason;
  }

  if (action) {
    return action.breakId === item.breakId
      ? "Another action is already running for this break."
      : "Another reconciliation break action is in progress.";
  }

  if (alreadyComplete) {
    return completeReason;
  }

  return null;
}

export function buildReconciliationNarrative(item: AccountingWorkspaceResponse["reconciliationQueue"][number]) {
  if (item.reconciliationStatus === "Balanced") {
    return "This run is currently balanced. Audit review should focus on evidence completeness and timing freshness rather than open break remediation.";
  }

  if (item.reconciliationStatus === "SecurityCoverageOpen") {
    return "Break counts are secondary here. The main task is resolving Security Master coverage so downstream ledger and reporting workflows are trustworthy.";
  }

  if (item.reconciliationStatus === "Resolved") {
    return "Historical breaks have been worked through, but the run still needs operator review before it can be treated as fully balanced.";
  }

  if (item.reconciliationStatus === "NotStarted") {
    return "No reconciliation pass has been recorded yet. This run should be queued behind currently active Accounting review work.";
  }

  return "Open reconciliation breaks remain on this run. Prioritize amount mismatches, timing drift, and unresolved references before moving on.";
}

function buildReconciliationBreakStatusAnnouncement({
  loading,
  action,
  loadError,
  actionError,
  breakCount
}: {
  loading: boolean;
  action: ReconciliationBreakAction | null;
  loadError: string | null;
  actionError: string | null;
  breakCount: number;
}): string {
  if (loading) {
    return "Loading reconciliation break queue.";
  }

  if (action?.command === "assign") {
    return `Assigning reconciliation break ${action.breakId}.`;
  }

  if (action?.command === "resolve") {
    return `Resolving reconciliation break ${action.breakId}.`;
  }

  if (action?.command === "dismiss") {
    return `Dismissing reconciliation break ${action.breakId}.`;
  }

  if (actionError) {
    return actionError;
  }

  if (loadError) {
    return loadError;
  }

  if (breakCount === 0) {
    return "No reconciliation breaks in the current queue.";
  }

  return `${breakCount} reconciliation ${breakCount === 1 ? "break" : "breaks"} loaded.`;
}
