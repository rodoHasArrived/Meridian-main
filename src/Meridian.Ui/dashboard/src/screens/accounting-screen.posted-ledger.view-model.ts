import { useCallback, useEffect, useMemo, useState } from "react";
import {
  getLedgerPeriodJournalEntries,
  getLedgerPeriodPnlSummary,
  getLedgerBooks,
  getLedgerPeriods,
  getLedgerPeriodTrialBalance
} from "@/lib/ledger-reports-api";
import { describeApiError, isApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import type {
  AccountingBasisKind,
  LedgerBook,
  LedgerDimensionSet,
  LedgerJournalLine,
  LedgerPeriod,
  LedgerPeriodPnlSummary,
  LedgerPeriodTrialBalanceLine,
  LedgerPostedJournalEntry,
  LedgerTrialBalanceLine
} from "@/types";
import { formatCurrency, formatCurrencyForCode, formatSignedCurrency } from "./accounting-screen.formatting";
import {
  countAvailableAccountingBases,
  DEFAULT_ACCOUNTING_BASIS,
  resolveAvailableAccountingBasis
} from "./accounting-screen.view-model.shared";
import { accountingBasisDisplayName } from "./accounting-screen.basis-bridge.view-model";
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
  getBooks: () => Promise<LedgerBook[]>;
  getPeriods: (query?: { ledgerBookId?: string | null }) => Promise<LedgerPeriod[]>;
  getTrialBalance: (periodId: string) => Promise<LedgerPeriodTrialBalanceLine[]>;
  getPnlSummary: (periodId: string) => Promise<LedgerPeriodPnlSummary>;
  getJournalEntries: (periodId: string) => Promise<LedgerPostedJournalEntry[]>;
}

const defaultAccountingPostedLedgerServices: AccountingPostedLedgerServices = {
  getBooks: () => getLedgerBooks(),
  getPeriods: (query) => getLedgerPeriods(query ?? {}),
  getTrialBalance: (periodId) => getLedgerPeriodTrialBalance(periodId),
  getPnlSummary: (periodId) => getLedgerPeriodPnlSummary(periodId),
  getJournalEntries: (periodId) => getLedgerPeriodJournalEntries(periodId)
};

/** Stable key for a dimension set, used only to test two sets for equality. */
function ledgerDimensionSetKey(dimensions: LedgerDimensionSet): string {
  const entries = Object.entries(dimensions as Record<string, unknown>)
    .filter(([, value]) => value !== null && value !== undefined)
    .map(([key, value]) => [
      key,
      typeof value === "object"
        ? JSON.stringify(Object.entries(value as Record<string, string>).sort(([a], [b]) => a.localeCompare(b)))
        : String(value)
    ] as const)
    .sort(([a], [b]) => a.localeCompare(b));
  return JSON.stringify(entries);
}

/**
 * The dimension scope of a journal entry as a whole, which is only meaningful when every one of
 * its lines was posted to the same scope. `LedgerJournalEntryDto` carries no dimensions of its
 * own — they live on `LedgerJournalEntryLineDto` — so an entry that spans scopes has no single
 * one to name, and saying otherwise would attribute a mixed entry to whichever line came first.
 */
export function resolvePostedEntryDimensions(entry: LedgerPostedJournalEntry): LedgerDimensionSet | null {
  const lines = Array.isArray(entry.lines) ? entry.lines : [];
  const scoped = lines.map((line) => line.dimensions ?? null);
  if (scoped.length === 0 || scoped.some((dimensions) => dimensions === null)) {
    return null;
  }

  const first = scoped[0] as LedgerDimensionSet;
  const key = ledgerDimensionSetKey(first);
  return scoped.every((dimensions) => ledgerDimensionSetKey(dimensions as LedgerDimensionSet) === key)
    ? first
    : null;
}

/**
 * Maps a posted journal entry onto the shared ledger-journal evidence row so the
 * posted book can reuse the journal-lineage panels the run-scoped ledger built.
 */
export function toLedgerJournalLine(entry: LedgerPostedJournalEntry): LedgerJournalLine {
  const dimensions = resolvePostedEntryDimensions(entry);
  return {
    journalEntryId: entry.journalEntryId,
    timestamp: entry.timestamp,
    description: entry.description,
    totalDebits: entry.totalDebits,
    totalCredits: entry.totalCredits,
    lineCount: Array.isArray(entry.lines) ? entry.lines.length : 0,
    dimensions,
    // Derived, not left null: consumers fall back to "all entities" on a null scope, which is an
    // affirmative claim about an entry that may be scoped to exactly one. Null here means the
    // entry's lines do not agree on an entity, which is not the same as spanning all of them.
    entityScopeId: dimensions?.entityId ?? null,
    entityScopeDisplayName: dimensions?.entityId ?? null
  };
}

/** One instrument reachable from a posted trial balance, for the related-securities drill-through. */
export interface PostedLedgerRelatedSecurity {
  securityId: string;
  label: string;
}

/**
 * The instruments a posted trial balance touches.
 * <p>
 * `LedgerPeriodTrialBalanceLineDto` carries no security reference — the posted book identifies an
 * instrument through `Dimensions.InstrumentId`, which the posting spine asserts is the Security
 * Master id — so keying strictly on `security.securityId` found nothing on a posted period and the
 * drill-through was permanently empty. Both are read here, in that order.
 * </p>
 */
export function collectPostedLedgerRelatedSecurities(
  rows: readonly Pick<LedgerTrialBalanceLine, "security" | "symbol" | "dimensions">[]
): PostedLedgerRelatedSecurity[] {
  const seen = new Map<string, string>();
  for (const row of rows) {
    const securityId = row.security?.securityId?.trim() || row.dimensions?.instrumentId?.trim();
    if (!securityId || seen.has(securityId)) {
      continue;
    }

    seen.set(securityId, row.security?.displayName?.trim() || row.symbol?.trim() || securityId);
  }

  return Array.from(seen.entries()).map(([securityId, label]) => ({ securityId, label }));
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

export interface PostedLedgerBookOption {
  id: string;
  label: string;
  baseCurrency: string;
  isSelected: boolean;
}

export interface AccountingPostedLedgerViewState {
  title: string;
  description: string;
  sourceBadgeLabel: string;
  periodSelector: PostedLedgerPeriodSelectorViewState;
  /** Non-error explanation shown when the selected period has no closed summary yet. */
  periodNotice: string | null;
  /** The ledger book these periods belong to, so a multi-book deployment names its subject. */
  selectedBookLabel: string | null;
  bookOptions: PostedLedgerBookOption[];
  /** The selected book's base currency; posted amounts are in book units, not USD. */
  baseCurrency: string | null;
  /**
   * The fund-structure node the selected book is attached to. Dropping it left surfaces labelling
   * an entity-scoped governed balance as an all-entity one.
   */
  bookScopeLabel: string | null;
  /**
   * When the selected period's closed summary completed. Retained on the P&L response, so a
   * surface that claims no as-of timestamp was kept is asserting an evidence gap that is not
   * there.
   */
  periodCompletedAt: string | null;
  trialBalance: AccountingTrialBalanceViewState;
  pnl: PostedLedgerPnlViewState;
}

export interface AccountingPostedLedgerViewModel {
  view: AccountingPostedLedgerViewState;
  selectBook: (ledgerBookId: string) => void;
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

/**
 * Ledger books in the order both workstations present them: by display name, with a stable id
 * tie-break.
 *
 * Mirrors <c>PostedLedgerProjection.SortBooks</c> exactly. Taking the store's own order here
 * instead meant the browser's default book followed `fund_profile_id, display_name,
 * ledger_book_id` while the desktop's followed display name alone, so in a multi-fund deployment
 * the two co-equal views of the same governed ledger opened on different books — and therefore
 * different periods and figures — for the same operator in the same session.
 */
export function sortLedgerBooks(books: readonly LedgerBook[]): LedgerBook[] {
  return [...books].sort((left, right) =>
    (left.displayName.trim() || left.ledgerBookId)
      .localeCompare(right.displayName.trim() || right.ledgerBookId, undefined, { sensitivity: "accent" })
    || left.ledgerBookId.localeCompare(right.ledgerBookId));
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
  periodLabel,
  selectedBasis = DEFAULT_ACCOUNTING_BASIS,
  baseCurrency = null,
  availableBasisCount = 1
}: {
  pnl: LedgerPeriodPnlSummary | null;
  loading: boolean;
  error: ApiErrorDisplay | null;
  periodLabel: string | null;
  /**
   * The basis the trial balance beside this panel is showing. The endpoint aggregates revenue and
   * expense across every basis the period holds, so without this a GAAP trial balance sat next to
   * a P&L that double-counted Primary and GAAP together.
   */
  selectedBasis?: AccountingBasisKind;
  /** The book's base currency; posted amounts are in book units, not dollars. */
  baseCurrency?: string | null;
  /**
   * How many accounting bases the selected period holds. The variance below is a period-level
   * figure derived across all of them and cannot be split, so on a mixed period it has to be
   * labelled rather than left to read as the selected basis's own.
   */
  availableBasisCount?: number;
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

  // The endpoint's totalRevenue and totalExpenses are plain sums of the lines it returns, so the
  // same sums over the basis-filtered lines reproduce them exactly for a single-basis period and
  // scope them correctly for a mixed one. Net income is derived the way the server derives its own
  // realized figures -- revenue less expenses -- because the endpoint's netIncome is a period-level
  // value that cannot be attributed to one basis.
  const inBasis = (line: LedgerPeriodTrialBalanceLine) =>
    (line.accountingBasis ?? DEFAULT_ACCOUNTING_BASIS) === selectedBasis;
  const sumBalances = (lines: LedgerPeriodTrialBalanceLine[]) =>
    lines.filter(inBasis).reduce((total, line) => total + line.balance, 0);

  const hasLineDetail = pnl.revenueLines.length > 0 || pnl.expenseLines.length > 0;
  const totalRevenue = hasLineDetail ? sumBalances(pnl.revenueLines) : pnl.totalRevenue;
  const totalExpenses = hasLineDetail ? sumBalances(pnl.expenseLines) : pnl.totalExpenses;
  const netIncome = hasLineDetail ? totalRevenue - totalExpenses : pnl.netIncome;

  const money = (value: number) =>
    baseCurrency ? formatCurrencyForCode(value, baseCurrency) : formatCurrency(value);
  const signedMoney = (value: number) =>
    baseCurrency ? `${value > 0 ? "+" : ""}${formatCurrencyForCode(value, baseCurrency)}` : formatSignedCurrency(value);

  const items: PostedLedgerPnlItemViewModel[] = [
    { id: "revenue", label: "Total revenue", value: money(totalRevenue), tone: "default" },
    { id: "expenses", label: "Total expenses", value: money(totalExpenses), tone: "default" },
    {
      id: "net-income",
      label: "Net income",
      value: signedMoney(netIncome),
      tone: netIncome < 0 ? "danger" : netIncome > 0 ? "success" : "default"
    },
    {
      id: "variance",
      label: "Period-on-period variance",
      // Carried through, not recomputed: the endpoint derives it across every basis the period
      // holds. Scoping the totals above without saying so left a basis-scoped net income sitting
      // beside a cross-basis variance as though they were one set of figures.
      value: pnl.periodOnPeriodVariance === null
        ? "No prior period"
        : availableBasisCount > 1
          ? `${signedMoney(pnl.periodOnPeriodVariance)} (all bases)`
          : signedMoney(pnl.periodOnPeriodVariance),
      tone: "default"
    },
    {
      id: "open-breaks",
      label: "Open breaks",
      value: pnl.openBreakCount.toLocaleString(),
      tone: pnl.openBreakCount > 0 ? "warning" : "success"
    }
  ];

  // A period whose summary carried no revenue or expense line detail leaves nothing to scope by,
  // so the endpoint's cross-basis totals are all there is. Say so rather than presenting them as
  // the selected basis's own.
  if (availableBasisCount > 1 && !hasLineDetail) {
    items.push({
      id: "basis-scope",
      label: "Basis scope",
      value: `Period total across all ${availableBasisCount} bases, not ${accountingBasisDisplayName(selectedBasis)} alone`,
      tone: "warning"
    });
  }

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
  booksErrorText,
  booksLoading = false,
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
  accountFilter,
  selectedBookLabel,
  baseCurrency,
  bookScopeLabel = null,
  bookOptions
}: {
  periods: LedgerPeriod[];
  periodsLoading: boolean;
  periodsError: ApiErrorDisplay | null;
  booksErrorText: string | null;
  /** Book discovery gates every request below it, so it reads as loading on the period selector. */
  booksLoading?: boolean;
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
  /** Names the book the periods below belong to; null until the books land. */
  selectedBookLabel: string | null;
  baseCurrency: string | null;
  bookOptions: PostedLedgerBookOption[];
  bookScopeLabel?: string | null;
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
    scopeLabel,
    // These rows are a posted book's, so their balances carry the book's base currency.
    currency: baseCurrency,
    // And their journal evidence resolves by period, not by run.
    periodId: selectedPeriodId
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
      loading: periodsLoading || booksLoading,
      loadingText: booksLoading
        ? "Loading ledger books."
        : periodsLoading ? "Loading ledger periods." : null,
      errorText: booksErrorText ?? periodsError?.summary ?? null,
      errorDetails: periodsError?.details ?? [],
      // "Create a ledger book" is an instruction, and it must not appear while the request that
      // would have found one is still in flight or has failed.
      emptyText: periodsLoading || booksLoading || periodsError || booksErrorText || periods.length > 0
        ? null
        : "No ledger periods exist yet. Create a ledger book and period in Accounting → Configure to start the governed book."
    },
    periodNotice,
    selectedBookLabel,
    bookOptions,
    baseCurrency,
    bookScopeLabel,
    periodCompletedAt: pnl?.completedAt ?? null,
    trialBalance,
    pnl: buildPostedLedgerPnlViewState({
      pnl,
      loading: pnlLoading,
      error: pnlError,
      periodLabel,
      selectedBasis,
      baseCurrency,
      availableBasisCount: countAvailableAccountingBases(trialBalanceRows)
    })
  };
}

export function useAccountingPostedLedgerViewModel(
  workstream: AccountingWorkstream,
  services: AccountingPostedLedgerServices = defaultAccountingPostedLedgerServices,
  // Opt-in because the journal is the one request here whose cost scales with the size of the
  // book: a production month's posted entries are returned in full. Only a consumer that renders
  // them should pay for them. AccountingPostedLedgerSection does not, so it does not ask.
  { includeJournal = false }: { includeJournal?: boolean } = {}
): AccountingPostedLedgerViewModel {
  const [books, setBooks] = useState<LedgerBook[]>([]);
  const [selectedBookId, setSelectedBookId] = useState<string | null>(null);
  const [booksErrorText, setBooksErrorText] = useState<string | null>(null);
  // Book discovery gates every request below it, so an untracked one left the period
  // selector reporting "no ledger periods exist yet -- create a ledger book" while the books
  // request was still in flight: an instruction to create accounting data, during a load.
  const [booksLoading, setBooksLoading] = useState(false);
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

  /**
   * Drops everything that only means something within one ledger book. The book label and base
   * currency come off the selected book, so figures left behind after it goes render unlabelled
   * and read as belonging to whatever book is chosen next.
   */
  const clearBookScopedState = useCallback(() => {
    setSelectedPeriodId(null);
    // Clearing the id alone was not enough: the periods array still held book A's, so the
    // selection-validation effect immediately re-picked A's default and loaded its figures under
    // B's label and currency -- and left them there indefinitely if B's period request hung.
    setPeriods([]);
    setPeriodNotice(null);
    setTrialBalanceRows([]);
    setPnl(null);
    setJournalLines([]);
    setJournalErrorText(null);
    setSelectedRowId(null);
  }, []);

  useEffect(() => {
    if (workstream !== "ledger") {
      return;
    }

    let cancelled = false;
    setBooksErrorText(null);
    setBooksLoading(true);
    services.getBooks()
      .then((unsorted) => {
        if (cancelled) return;
        const rows = sortLedgerBooks(unsorted);
        setBooks(rows);
        if (rows.length === 0) {
          // No book means no scope for anything below, and the period effect will not run to
          // clear it.
          clearBookScopedState();
        }
        // Scope to one book before any period is chosen. Unscoped, the period list spans every
        // book and the default lands on whichever book owns the globally latest closed period —
        // presented under this panel's fixed scope label as though it were the only book.
        setSelectedBookId((current) =>
          current !== null && rows.some((book) => book.ledgerBookId === current)
            ? current
            : rows[0]?.ledgerBookId ?? null);
      })
      .catch((err) => {
        if (cancelled) return;
        setBooks([]);
        setSelectedBookId(null);
        // The previous book's periods, balances and P&L are book-scoped, and the label and base
        // currency that named them come off the selected book -- so leaving them rendered a book's
        // figures with nothing saying whose they were, indefinitely, since the period effect does
        // not run without a selected book. The desktop workstation drops them for the same reason.
        clearBookScopedState();
        // Without this the period effect simply never runs and the screen renders its ordinary
        // "no ledger periods exist yet" empty state, telling operators to create accounting data
        // during what is actually an API outage.
        setBooksErrorText(describeApiError(err, "Ledger books failed to load.").summary);
      })
      .finally(() => {
        if (!cancelled) {
          setBooksLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [clearBookScopedState, services, workstream]);

  useEffect(() => {
    if (workstream !== "ledger" || selectedBookId === null) {
      return;
    }

    let cancelled = false;
    setPeriodsLoading(true);
    setPeriodsError(null);

    services.getPeriods({ ledgerBookId: selectedBookId })
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
  }, [selectedBookId, services, workstream]);

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
    // Drop the outgoing period's figures before requesting the new one. The period label and
    // scope re-render from selectedPeriodId immediately, and the trial-balance view state counts
    // retained rows as "ready" even while loading, so keeping them would present one period's
    // balances under another period's name — indefinitely if the request never settles.
    setTrialBalanceRows([]);
    setPnl(null);
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
          const lines = rows.map(toTrialBalanceLine);
          setTrialBalanceRows(lines);
          // Resolved from the rows themselves. Carrying GAAP or Tax across a period change filters
          // every row out of a period that only holds Primary, and the period reads as having no
          // trial balance even though it loaded successfully.
          setSelectedBasis(resolveAvailableAccountingBasis(lines));
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
    if (!includeJournal || !selectedPeriodId || workstream !== "ledger") {
      setJournalLines([]);
      setJournalErrorText(null);
      setJournalLoading(false);
      return;
    }

    let cancelled = false;
    // Same reason as the trial balance above: these entries are the outgoing period's evidence.
    setJournalLines([]);
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
  }, [selectedPeriodId, services, workstream, includeJournal]);

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

  const selectedBook = useMemo(
    () => books.find((candidate) => candidate.ledgerBookId === selectedBookId) ?? null,
    [books, selectedBookId]
  );
  const selectedBookLabel = selectedBook ? (selectedBook.displayName.trim() || selectedBook.ledgerBookId) : null;
  const baseCurrency = selectedBook?.baseCurrency?.trim() || null;
  // The book names the fund-structure node it belongs to. Surfaces that dropped it fell back to
  // "All entities", which is an affirmative claim about a book scoped to exactly one.
  const bookScopeLabel = selectedBook
    ? [selectedBook.fundStructureNodeKind?.trim(), selectedBook.fundProfileId?.trim()]
      .filter(Boolean).join(" · ") || null
    : null;
  const bookOptions = useMemo<PostedLedgerBookOption[]>(
    () => books.map((book) => ({
      id: book.ledgerBookId,
      label: book.displayName.trim() || book.ledgerBookId,
      baseCurrency: book.baseCurrency,
      isSelected: book.ledgerBookId === selectedBookId
    })),
    [books, selectedBookId]
  );

  const selectBook = useCallback((ledgerBookId: string) => {
    // Re-selecting the book already on screen is not a scope change. setSelectedBookId would be a
    // no-op for an unchanged id, so the period effect would never re-run, while the clearing below
    // had already emptied the ledger -- leaving the panel blank until another book was chosen.
    if (ledgerBookId === selectedBookId) {
      return;
    }

    setSelectedBookId(ledgerBookId);
    // The incoming book's periods are a different set entirely; keeping the outgoing selection
    // would request a period that does not belong to it.
    clearBookScopedState();
  }, [clearBookScopedState, selectedBookId]);

  const view = useMemo(
    () => buildAccountingPostedLedgerViewState({
      periods,
      periodsLoading,
      periodsError,
      booksErrorText,
      booksLoading,
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
      accountFilter,
      selectedBookLabel,
      baseCurrency,
      bookScopeLabel,
      bookOptions
    }),
    [
      accountFilter,
      baseCurrency,
      bookOptions,
      bookScopeLabel,
      booksErrorText,
      booksLoading,
      selectedBookLabel,
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
    selectBook,
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
