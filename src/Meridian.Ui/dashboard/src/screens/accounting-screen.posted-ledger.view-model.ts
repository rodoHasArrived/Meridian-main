import { useCallback, useEffect, useMemo, useState } from "react";
import {
  getLedgerPeriodJournalEntries,
  getLedgerPeriodPnlSummary,
  getLedgerPeriods,
  getLedgerPeriodTrialBalance
} from "@/lib/ledger-reports-api";
import { describeApiError, isApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import type {
  AccountingBasisKind,
  LedgerJournalLine,
  LedgerPeriod,
  LedgerPeriodPnlSummary,
  LedgerPeriodTrialBalanceLine,
  LedgerPostedJournalEntry,
  LedgerTrialBalanceLine
} from "@/types";
import { formatCurrency, formatSignedCurrency } from "./accounting-screen.formatting";
import { DEFAULT_ACCOUNTING_BASIS } from "./accounting-screen.view-model.shared";
import {
  buildAccountingTrialBalanceViewState,
  type AccountingTrialBalanceViewState
} from "./accounting-screen.view-model";
import type { AccountingWorkstream } from "./accounting-screen.task-mode-view-model";

/**
 * View model for the Accounting workstream's posted-journal ledger panel.
 *
 * This panel reads the governed book — `/api/ledger/periods/{periodId}/trial-balance`
 * and `/api/ledger/periods/{periodId}/pnl-summary` over the immutable posted journal —
 * scoped by ledger period. It is deliberately distinct from the strategy-run ledger
 * explorer, which reads a simulation run's ledger and remains a strategy artifact.
 */

export interface AccountingPostedLedgerServices {
  getPeriods: () => Promise<LedgerPeriod[]>;
  getTrialBalance: (periodId: string) => Promise<LedgerPeriodTrialBalanceLine[]>;
  getPnlSummary: (periodId: string) => Promise<LedgerPeriodPnlSummary>;
  getJournalEntries: (periodId: string) => Promise<LedgerPostedJournalEntry[]>;
}

const defaultAccountingPostedLedgerServices: AccountingPostedLedgerServices = {
  getPeriods: () => getLedgerPeriods(),
  getTrialBalance: (periodId) => getLedgerPeriodTrialBalance(periodId),
  getPnlSummary: (periodId) => getLedgerPeriodPnlSummary(periodId),
  getJournalEntries: (periodId) => getLedgerPeriodJournalEntries(periodId)
};

/**
 * Maps a posted journal entry onto the shared ledger-journal evidence row so the
 * posted book can reuse the journal-lineage panels the run-scoped ledger built.
 */
export function toLedgerJournalLine(entry: LedgerPostedJournalEntry): LedgerJournalLine {
  return {
    journalEntryId: entry.journalEntryId,
    timestamp: entry.timestamp,
    description: entry.description,
    totalDebits: entry.totalDebits,
    totalCredits: entry.totalCredits,
    lineCount: Array.isArray(entry.lines) ? entry.lines.length : 0,
    dimensions: entry.dimensions ?? null
  };
}

export const POSTED_LEDGER_DETAIL_PANEL_ID = "posted-ledger-account-detail";

export interface PostedLedgerPeriodOptionViewModel {
  id: string;
  label: string;
  detail: string;
  statusLabel: string;
  statusTone: "default" | "outline" | "success" | "warning";
  isSelected: boolean;
  ariaLabel: string;
}

export interface PostedLedgerPeriodSelectorViewState {
  label: string;
  options: PostedLedgerPeriodOptionViewModel[];
  loading: boolean;
  loadingText: string | null;
  errorText: string | null;
  errorDetails: string[];
  emptyText: string | null;
}

export interface PostedLedgerPnlItemViewModel {
  id: string;
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "danger";
}

export interface PostedLedgerPnlViewState {
  title: string;
  description: string;
  state: "ready" | "loading" | "empty" | "error";
  items: PostedLedgerPnlItemViewModel[];
  signoffLabel: string | null;
  signoffTone: "outline" | "success" | "warning" | "danger";
  emptyText: string;
  errorText: string | null;
}

export interface AccountingPostedLedgerViewState {
  title: string;
  description: string;
  sourceBadgeLabel: string;
  periodSelector: PostedLedgerPeriodSelectorViewState;
  /** Non-error explanation shown when the selected period has no closed summary yet. */
  periodNotice: string | null;
  trialBalance: AccountingTrialBalanceViewState;
  pnl: PostedLedgerPnlViewState;
}

export interface AccountingPostedLedgerViewModel {
  view: AccountingPostedLedgerViewState;
  selectPeriod: (periodId: string) => void;
  selectBasis: (basis: AccountingBasisKind) => void;
  updateAccountFilter: (value: string) => void;
  selectTrialBalanceRow: (rowId: string | null) => void;
  /** Posted journal entries for the selected period, in the shared evidence-row shape. */
  journalLines: LedgerJournalLine[];
  journalLoading: boolean;
  journalErrorText: string | null;
  selectedPeriodId: string | null;
  selectedPeriodLabel: string | null;
}

export function sortLedgerPeriodsDescending(periods: LedgerPeriod[]): LedgerPeriod[] {
  return [...periods].sort((left, right) =>
    right.fiscalYear - left.fiscalYear ||
    right.periodNo - left.periodNo ||
    left.periodId.localeCompare(right.periodId));
}

/**
 * The latest closed period is the default subject: the trial balance and P&L read the
 * closed-period summary, so an open period would land on the "not yet closed" notice.
 */
export function resolveDefaultPostedLedgerPeriodId(periods: LedgerPeriod[]): string | null {
  const sorted = sortLedgerPeriodsDescending(periods);
  return (sorted.find((period) => period.status !== "Open") ?? sorted[0])?.periodId ?? null;
}

export function toTrialBalanceLine(line: LedgerPeriodTrialBalanceLine): LedgerTrialBalanceLine {
  return {
    accountName: line.accountName,
    accountType: line.accountType,
    symbol: line.symbol,
    financialAccountId: line.financialAccountId,
    balance: line.balance,
    entryCount: line.entryCount,
    security: null,
    dimensions: line.dimensions ?? null,
    accountingBasis: line.accountingBasis,
    ...(line.accountingPolicyId !== undefined ? { accountingPolicyId: line.accountingPolicyId } : {}),
    ...(line.accountingPolicyVersion !== undefined ? { accountingPolicyVersion: line.accountingPolicyVersion } : {}),
    ruleId: line.ruleId ?? null,
    ruleVersion: line.ruleVersion ?? null,
    sourceEventId: line.sourceEventId ?? null,
    sourceJournalEntryId: line.sourceJournalEntryId ?? null
  };
}

function periodStatusLabel(status: LedgerPeriod["status"]): string {
  switch (status) {
    case "HardClosed":
      return "Hard closed";
    case "SoftClosed":
      return "Soft closed";
    default:
      return "Open";
  }
}

function periodStatusTone(status: LedgerPeriod["status"]): PostedLedgerPeriodOptionViewModel["statusTone"] {
  switch (status) {
    case "HardClosed":
      return "success";
    case "SoftClosed":
      return "default";
    default:
      return "warning";
  }
}

export function buildPostedLedgerPeriodOptions(
  periods: LedgerPeriod[],
  selectedPeriodId: string | null
): PostedLedgerPeriodOptionViewModel[] {
  return sortLedgerPeriodsDescending(periods).map((period) => {
    const label = period.label.trim() || `FY${period.fiscalYear} P${period.periodNo}`;
    const statusLabel = periodStatusLabel(period.status);
    return {
      id: period.periodId,
      label,
      detail: `${period.startDate} to ${period.endDate}`,
      statusLabel,
      statusTone: periodStatusTone(period.status),
      isSelected: period.periodId === selectedPeriodId,
      ariaLabel: `Ledger period ${label}, ${period.startDate} to ${period.endDate}, ${statusLabel}`
    };
  });
}

export function buildPostedLedgerPnlViewState({
  pnl,
  loading,
  error,
  periodLabel
}: {
  pnl: LedgerPeriodPnlSummary | null;
  loading: boolean;
  error: ApiErrorDisplay | null;
  periodLabel: string | null;
}): PostedLedgerPnlViewState {
  const description = periodLabel
    ? `Revenue, expense, and net-income totals from the posted journal for ${periodLabel}.`
    : "Revenue, expense, and net-income totals from the posted journal for the selected period.";

  if (error) {
    return {
      title: "P&L summary",
      description,
      state: "error",
      items: [],
      signoffLabel: null,
      signoffTone: "outline",
      emptyText: "P&L summary is unavailable for the selected period.",
      errorText: error.summary
    };
  }

  if (!pnl) {
    return {
      title: "P&L summary",
      description,
      state: loading ? "loading" : "empty",
      items: [],
      signoffLabel: null,
      signoffTone: "outline",
      emptyText: loading
        ? "Loading the posted-journal P&L summary."
        : "The posted-journal P&L summary appears once the selected period has a closed-period summary.",
      errorText: null
    };
  }

  const items: PostedLedgerPnlItemViewModel[] = [
    { id: "revenue", label: "Total revenue", value: formatCurrency(pnl.totalRevenue), tone: "default" },
    { id: "expenses", label: "Total expenses", value: formatCurrency(pnl.totalExpenses), tone: "default" },
    {
      id: "net-income",
      label: "Net income",
      value: formatSignedCurrency(pnl.netIncome),
      tone: pnl.netIncome < 0 ? "danger" : pnl.netIncome > 0 ? "success" : "default"
    },
    {
      id: "variance",
      label: "Period-on-period variance",
      value: pnl.periodOnPeriodVariance === null ? "No prior period" : formatSignedCurrency(pnl.periodOnPeriodVariance),
      tone: "default"
    },
    {
      id: "open-breaks",
      label: "Open breaks",
      value: pnl.openBreakCount.toLocaleString(),
      tone: pnl.openBreakCount > 0 ? "warning" : "success"
    }
  ];

  const signoffLabel = pnl.signoffStatus === "NotRequired" ? "Sign-off not required"
    : pnl.signoffStatus === "Pending" ? "Sign-off pending"
      : pnl.signoffStatus === "SignedOff" ? "Signed off"
        : "Sign-off rejected";
  const signoffTone: PostedLedgerPnlViewState["signoffTone"] =
    pnl.signoffStatus === "SignedOff" ? "success"
      : pnl.signoffStatus === "Pending" ? "warning"
        : pnl.signoffStatus === "Rejected" ? "danger"
          : "outline";

  return {
    title: "P&L summary",
    description,
    state: "ready",
    items,
    signoffLabel,
    signoffTone,
    emptyText: "",
    errorText: null
  };
}

export function buildAccountingPostedLedgerViewState({
  periods,
  periodsLoading,
  periodsError,
  selectedPeriodId,
  periodNotice,
  trialBalanceRows,
  trialBalanceLoading,
  trialBalanceError,
  pnl,
  pnlLoading,
  pnlError,
  selectedRowId,
  selectedBasis,
  accountFilter
}: {
  periods: LedgerPeriod[];
  periodsLoading: boolean;
  periodsError: ApiErrorDisplay | null;
  selectedPeriodId: string | null;
  periodNotice: string | null;
  trialBalanceRows: LedgerTrialBalanceLine[];
  trialBalanceLoading: boolean;
  trialBalanceError: ApiErrorDisplay | null;
  pnl: LedgerPeriodPnlSummary | null;
  pnlLoading: boolean;
  pnlError: ApiErrorDisplay | null;
  selectedRowId: string | null;
  selectedBasis: AccountingBasisKind;
  accountFilter: string;
}): AccountingPostedLedgerViewState {
  const selectedPeriod = periods.find((period) => period.periodId === selectedPeriodId) ?? null;
  const periodLabel = selectedPeriod
    ? (selectedPeriod.label.trim() || `FY${selectedPeriod.fiscalYear} P${selectedPeriod.periodNo}`)
    : null;
  const scopeLabel = periodLabel
    ? `the posted journal for period ${periodLabel}`
    : "the posted journal";

  const base = buildAccountingTrialBalanceViewState({
    runId: null,
    rows: trialBalanceRows,
    selectedRowId,
    selectedBasis,
    accountFilter,
    loading: trialBalanceLoading,
    error: trialBalanceError,
    scopeLabel
  });
  const trialBalance: AccountingTrialBalanceViewState = {
    ...base,
    // Distinct copy and a distinct detail-panel id from the strategy-run explorer's
    // panel so the two surfaces never read as the same book (and both can render
    // on the ledger workstream without colliding aria ids or test queries).
    detailPanelId: POSTED_LEDGER_DETAIL_PANEL_ID,
    rows: base.rows.map((row) => ({ ...row, detailPanelId: POSTED_LEDGER_DETAIL_PANEL_ID })),
    emptyTitle: "No posted trial balance lines",
    accountFilterLabel: "Filter posted-journal GL accounts",
    detailEmptyAriaLabel: "No posted-journal trial-balance account selected"
  };

  return {
    title: "Posted-journal trial balance",
    description: "Account balances from the governed, immutable journal — the fund's book of record — scoped by ledger period. Strategy-run ledgers are simulation artifacts and live in the strategy-run explorer below.",
    sourceBadgeLabel: "Source: posted journal",
    periodSelector: {
      label: "Ledger period",
      options: buildPostedLedgerPeriodOptions(periods, selectedPeriodId),
      loading: periodsLoading,
      loadingText: periodsLoading ? "Loading ledger periods." : null,
      errorText: periodsError?.summary ?? null,
      errorDetails: periodsError?.details ?? [],
      emptyText: periodsLoading || periodsError || periods.length > 0
        ? null
        : "No ledger periods exist yet. Create a ledger book and period in Accounting → Configure to start the governed book."
    },
    periodNotice,
    trialBalance,
    pnl: buildPostedLedgerPnlViewState({ pnl, loading: pnlLoading, error: pnlError, periodLabel })
  };
}

export function useAccountingPostedLedgerViewModel(
  workstream: AccountingWorkstream,
  services: AccountingPostedLedgerServices = defaultAccountingPostedLedgerServices
): AccountingPostedLedgerViewModel {
  const [periods, setPeriods] = useState<LedgerPeriod[]>([]);
  const [periodsLoading, setPeriodsLoading] = useState(false);
  const [periodsError, setPeriodsError] = useState<ApiErrorDisplay | null>(null);
  const [selectedPeriodId, setSelectedPeriodId] = useState<string | null>(null);
  const [periodNotice, setPeriodNotice] = useState<string | null>(null);
  const [trialBalanceRows, setTrialBalanceRows] = useState<LedgerTrialBalanceLine[]>([]);
  const [trialBalanceLoading, setTrialBalanceLoading] = useState(false);
  const [trialBalanceError, setTrialBalanceError] = useState<ApiErrorDisplay | null>(null);
  const [pnl, setPnl] = useState<LedgerPeriodPnlSummary | null>(null);
  const [pnlLoading, setPnlLoading] = useState(false);
  const [pnlError, setPnlError] = useState<ApiErrorDisplay | null>(null);
  const [selectedRowId, setSelectedRowId] = useState<string | null>(null);
  const [selectedBasis, setSelectedBasis] = useState<AccountingBasisKind>(DEFAULT_ACCOUNTING_BASIS);
  const [accountFilter, setAccountFilter] = useState("");
  const [journalLines, setJournalLines] = useState<LedgerJournalLine[]>([]);
  const [journalLoading, setJournalLoading] = useState(false);
  const [journalErrorText, setJournalErrorText] = useState<string | null>(null);

  useEffect(() => {
    if (workstream !== "ledger") {
      return;
    }

    let cancelled = false;
    setPeriodsLoading(true);
    setPeriodsError(null);

    services.getPeriods()
      .then((rows) => {
        if (!cancelled) {
          setPeriods(rows);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setPeriods([]);
          setPeriodsError(describeApiError(err, "Ledger periods failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setPeriodsLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [services, workstream]);

  useEffect(() => {
    // Nothing to validate a selection against until the periods land. Resetting here would
    // clobber a caller-supplied selection (a deep link's ?periodId=) on the first render and
    // fight whoever re-applies it.
    if (periods.length === 0) {
      return;
    }

    const hasSelection = selectedPeriodId !== null &&
      periods.some((period) => period.periodId === selectedPeriodId);
    if (hasSelection) {
      return;
    }

    setSelectedPeriodId(resolveDefaultPostedLedgerPeriodId(periods));
  }, [periods, selectedPeriodId]);

  useEffect(() => {
    if (!selectedPeriodId || workstream !== "ledger") {
      setTrialBalanceRows([]);
      setTrialBalanceError(null);
      setTrialBalanceLoading(false);
      setPnl(null);
      setPnlError(null);
      setPnlLoading(false);
      setPeriodNotice(null);
      return;
    }

    let cancelled = false;
    setTrialBalanceLoading(true);
    setTrialBalanceError(null);
    setPnlLoading(true);
    setPnlError(null);
    setPeriodNotice(null);

    // A 404 on either report means the period has no closed-period summary yet —
    // an expected state for open periods, surfaced as a notice rather than a failure.
    const describeMissingSummary = () =>
      "This period has no closed-period summary yet. Trial balance and P&L publish from the posted journal when the period closes.";

    services.getTrialBalance(selectedPeriodId)
      .then((rows) => {
        if (!cancelled) {
          setTrialBalanceRows(rows.map(toTrialBalanceLine));
        }
      })
      .catch((err) => {
        if (cancelled) {
          return;
        }

        setTrialBalanceRows([]);
        if (isApiError(err) && err.status === 404) {
          setPeriodNotice(describeMissingSummary());
        } else {
          setTrialBalanceError(describeApiError(err, "Posted-journal trial balance failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setTrialBalanceLoading(false);
        }
      });

    services.getPnlSummary(selectedPeriodId)
      .then((summary) => {
        if (!cancelled) {
          setPnl(summary);
        }
      })
      .catch((err) => {
        if (cancelled) {
          return;
        }

        setPnl(null);
        if (isApiError(err) && err.status === 404) {
          setPeriodNotice(describeMissingSummary());
        } else {
          setPnlError(describeApiError(err, "Posted-journal P&L summary failed to load."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setPnlLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [selectedPeriodId, services, workstream]);

  useEffect(() => {
    if (!selectedPeriodId || workstream !== "ledger") {
      setJournalLines([]);
      setJournalErrorText(null);
      setJournalLoading(false);
      return;
    }

    let cancelled = false;
    setJournalLoading(true);
    setJournalErrorText(null);

    services.getJournalEntries(selectedPeriodId)
      .then((entries) => {
        if (!cancelled) {
          setJournalLines(entries.map(toLedgerJournalLine));
        }
      })
      .catch((err) => {
        if (cancelled) {
          return;
        }

        setJournalLines([]);
        // A period with no closed summary has no posted entries to show yet; that is a
        // state, not a failure, and the period notice already explains it.
        setJournalErrorText(isApiError(err) && err.status === 404
          ? null
          : describeApiError(err, "Posted journal entries failed to load.").summary);
      })
      .finally(() => {
        if (!cancelled) {
          setJournalLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [selectedPeriodId, services, workstream]);

  const selectPeriod = useCallback((periodId: string) => {
    setSelectedPeriodId(periodId);
    setSelectedRowId(null);
  }, []);

  const selectBasis = useCallback((basis: AccountingBasisKind) => {
    setSelectedBasis(basis);
    setSelectedRowId(null);
  }, []);

  const updateAccountFilter = useCallback((value: string) => {
    setAccountFilter(value);
    setSelectedRowId(null);
  }, []);

  const view = useMemo(
    () => buildAccountingPostedLedgerViewState({
      periods,
      periodsLoading,
      periodsError,
      selectedPeriodId,
      periodNotice,
      trialBalanceRows,
      trialBalanceLoading,
      trialBalanceError,
      pnl,
      pnlLoading,
      pnlError,
      selectedRowId,
      selectedBasis,
      accountFilter
    }),
    [
      accountFilter,
      periodNotice,
      periods,
      periodsError,
      periodsLoading,
      pnl,
      pnlError,
      pnlLoading,
      selectedBasis,
      selectedPeriodId,
      selectedRowId,
      trialBalanceError,
      trialBalanceLoading,
      trialBalanceRows
    ]
  );

  const selectedPeriodLabel = useMemo(() => {
    const period = periods.find((candidate) => candidate.periodId === selectedPeriodId);
    return period ? (period.label.trim() || `FY${period.fiscalYear} P${period.periodNo}`) : null;
  }, [periods, selectedPeriodId]);

  return {
    view,
    selectPeriod,
    selectBasis,
    updateAccountFilter,
    selectTrialBalanceRow: setSelectedRowId,
    journalLines,
    journalLoading,
    journalErrorText,
    selectedPeriodId,
    selectedPeriodLabel
  };
}
