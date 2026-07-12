import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ApiRequestOptions } from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import { useQuotesStream } from "@/hooks/use-quotes-stream";
import { formatRelativeAge as formatRelative } from "@/lib/time";
import { WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import type { MetricSnapshot, QuotesSnapshotItem, QuotesSnapshotResponse, SymbolRecord, SymbolStatistics } from "@/types";

export type WatchlistBadgeVariant = "default" | "outline" | "success" | "warning" | "danger" | "paper" | "live" | "research";
export type WatchlistListState = "loading" | "error" | "empty" | "ready";
export type WatchlistPriceTone = "default" | "success" | "danger";
export type WatchlistQuoteStatusTone = "default" | "warning" | "danger";
export type WatchlistDetailFieldTone = "default" | "success" | "warning" | "danger" | "muted";
export type WatchlistSortColumn =
  | "symbol"
  | "status"
  | "last"
  | "change-percent"
  | "spread"
  | "quote-age";
export type WatchlistSortDirection = "asc" | "desc";

export interface WatchlistSortState {
  columnId: WatchlistSortColumn;
  direction: WatchlistSortDirection;
}

const DEFAULT_SORT: WatchlistSortState = { columnId: "symbol", direction: "asc" };
const STATUS_RANK: Record<SymbolRecord["status"], number> = {
  Error: 0,
  Active: 1,
  Monitored: 2,
  Archived: 3
};

const QUOTE_POLL_INTERVAL_MS = 2000;
export const WATCHLIST_QUOTE_FRESHNESS_BUDGET_MS = 2 * QUOTE_POLL_INTERVAL_MS;
const QUOTE_STALE_THRESHOLD_MS = 15_000;
export const WATCHLIST_EMPTY_VALUE = "—";
export const WATCHLIST_NO_QUOTE_LABEL = "No quote";

export interface WatchlistApi {
  getSymbols: (options?: ApiRequestOptions) => Promise<SymbolRecord[]>;
  getSymbolsStatistics: (options?: ApiRequestOptions) => Promise<SymbolStatistics>;
  getLiveQuotesSnapshot: (symbols?: readonly string[], options?: ApiRequestOptions) => Promise<{ quotes: QuotesSnapshotItem[] }>;
  addSymbol: (symbol: string) => Promise<{ success: boolean; symbol: string }>;
  bulkAddSymbols: (symbols: string[]) => Promise<{ added: number; skipped: number; errors: string[] }>;
  removeSymbol: (symbol: string) => Promise<{ success: boolean; symbol: string }>;
}

export interface WatchlistSubmitFeedback {
  tone: "success" | "warning" | "danger";
  message: string;
  details?: string[];
  providerSetupHandoff?: WatchlistProviderSetupHandoff;
  nextActionHandoff?: WatchlistRouteHandoff;
}

export interface WatchlistRouteHandoff {
  href: string;
  label: string;
  ariaLabel: string;
  detail: string;
}

export type WatchlistProviderSetupHandoff = WatchlistRouteHandoff;

export interface WatchlistRowViewModel {
  symbol: string;
  status: SymbolRecord["status"];
  statusVariant: WatchlistBadgeVariant;
  providerLabel: string;
  lastEventLabel: string;
  eventCountLabel: string;
  historyLabel: string;
  hasHistoricalData: boolean;
  bidLabel: string;
  askLabel: string;
  lastPriceLabel: string;
  changeLabel: string;
  changePercentLabel: string;
  dayRangeLabel: string;
  spreadLabel: string;
  quoteAgeLabel: string;
  hasQuote: boolean;
  quoteStale: boolean;
  lastTone: WatchlistPriceTone;
  changeTone: WatchlistDetailFieldTone;
  isRemoving: boolean;
  quoteHref: string;
  quoteAriaLabel: string;
  inspectLabel: string;
  inspectAriaLabel: string;
  removeLabel: string;
  removeAriaLabel: string;
  removeButtonVariant: "outline" | "destructive";
  removeStatusId: string | null;
  removeStatusLabel: string | null;
  removeStatusTone: "warning" | "danger";
  removeDisabledReason: string | null;
  rowSelectAriaLabel: string;
  ariaLabel: string;
  /** Numeric sort key for last price; null when the row has no live quote. */
  lastPriceValue: number | null;
  /** Numeric sort key for absolute day change; null when no session is available. */
  changeValue: number | null;
  /** Numeric sort key for day change percent; null when no session is available. */
  changePercentValue: number | null;
  /** Numeric sort key for bid/ask spread; null when no live quote. */
  spreadValue: number | null;
  /** Numeric sort key for quote age in milliseconds; null when no live quote. */
  quoteAgeMs: number | null;
}

export interface WatchlistQuoteRefreshCommandState {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
}

export interface WatchlistStarterPackCommandState {
  id: string;
  label: string;
  symbols: string[];
  symbolsLabel: string;
  ariaLabel: string;
  busyLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
}

export interface WatchlistAddSymbolFieldViewModel {
  id: string;
  label: string;
  placeholder: string;
  helperId: string;
  helperText: string;
  feedbackId: string;
  feedbackRole: "status" | "alert";
  describedBy: string;
  invalid: boolean;
  errorMessageId: string | undefined;
  disabled: boolean;
}

export interface WatchlistSelectedDetailField {
  label: string;
  value: string;
  tone: WatchlistDetailFieldTone;
}

export interface WatchlistSelectedDetail {
  symbol: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusVariant: WatchlistBadgeVariant;
  statusAriaLabel: string;
  regionLabel: string;
  quoteActionLabel: string;
  quoteActionHref: string;
  quoteActionAriaLabel: string;
  fields: WatchlistSelectedDetailField[];
}

export interface WatchlistFilterCommandState {
  label: string;
  ariaLabel: string;
  pressed: boolean;
  disabled: boolean;
  disabledReason: string | null;
  hiddenCount: number;
}

export interface WatchlistListRetryCommandState {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
}

export interface WatchlistScreenViewModel {
  pendingSymbol: string;
  setPendingSymbol: (value: string) => void;
  refreshing: boolean;
  submitting: boolean;
  submitError: string | null;
  submitFeedback: WatchlistSubmitFeedback | null;
  loadError: ApiErrorDisplay | null;
  stats: MetricSnapshot[];
  rows: WatchlistRowViewModel[];
  sort: WatchlistSortState;
  toggleSort: (columnId: WatchlistSortColumn) => void;
  hideStale: boolean;
  setHideStale: (value: boolean) => void;
  staleFilterCommand: WatchlistFilterCommandState;
  listRetryCommand: WatchlistListRetryCommandState;
  listState: WatchlistListState;
  listDescription: string;
  tableLabel: string;
  tableCaption: string;
  formLabel: string;
  addSymbolField: WatchlistAddSymbolFieldViewModel;
  inputId: string;
  inputPlaceholder: string;
  inputHelpId: string;
  inputHelpText: string;
  addButtonLabel: string;
  addButtonAriaLabel: string;
  addDisabled: boolean;
  addDisabledReason: string | null;
  refreshButtonLabel: string;
  refreshButtonAriaLabel: string;
  refreshDisabled: boolean;
  toolbarItems: Array<{ id: string; label: string; value: string; active?: boolean }>;
  quoteStatusLabel: string;
  quoteStatusTone: WatchlistQuoteStatusTone;
  quoteStatusDetails: string[];
  quoteFreshnessTimestamp: string | null;
  quoteFreshnessError: string | null;
  quoteStreamHealthy: boolean;
  quoteProviderSetupHandoff: WatchlistProviderSetupHandoff | null;
  quoteRefreshCommand: WatchlistQuoteRefreshCommandState;
  starterPackGroupLabel: string;
  starterPackEyebrow: string;
  starterPacks: WatchlistStarterPackCommandState[];
  selectedSymbol: string | null;
  selectedRowId: string | null;
  selectedDetail: WatchlistSelectedDetail | null;
  detailPanelId: string;
  detailPanelTitle: string;
  detailPanelDescription: string;
  detailPanelEmptyText: string;
  detailPanelAriaLabel: string;
  selectSymbol: (symbol: string) => void;
  refresh: () => Promise<void>;
  refreshQuotes: () => Promise<void>;
  addPendingSymbol: (event?: { preventDefault: () => void }) => Promise<void>;
  applyStarterPack: (id: string) => Promise<void>;
  removeSymbol: (symbol: string) => Promise<void>;
}

export const WATCHLIST_STARTER_PACKS: Array<{ id: string; label: string; symbols: string[] }> = [
  { id: "us-core", label: "US core", symbols: ["SPY", "QQQ", "AAPL", "MSFT"] },
  { id: "risk-pulse", label: "Risk pulse", symbols: ["TLT", "GLD", "USO", "VIXY"] },
  { id: "income", label: "Income", symbols: ["HYG", "LQD", "VNQ", "SCHD"] }
];

export function useWatchlistScreenViewModel(api: WatchlistApi): WatchlistScreenViewModel {
  const [symbols, setSymbols] = useState<SymbolRecord[] | null>(null);
  const [stats, setStats] = useState<SymbolStatistics | null>(null);
  const [loadError, setLoadError] = useState<ApiErrorDisplay | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [pendingSymbol, setPendingSymbol] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [submitFeedback, setSubmitFeedback] = useState<WatchlistSubmitFeedback | null>(null);
  const [removing, setRemoving] = useState<Record<string, boolean>>({});
  const [quotes, setQuotes] = useState<Record<string, QuotesSnapshotItem>>({});
  const [quoteError, setQuoteError] = useState<ApiErrorDisplay | null>(null);
  const [quoteFetchedAt, setQuoteFetchedAt] = useState<number | null>(null);
  const [quoteRefreshing, setQuoteRefreshing] = useState(false);
  const [activeStarterPackId, setActiveStarterPackId] = useState<string | null>(null);
  const [selectedSymbol, setSelectedSymbol] = useState<string | null>(null);
  const [pendingRemoveSymbol, setPendingRemoveSymbol] = useState<string | null>(null);
  const [sort, setSort] = useState<WatchlistSortState>(DEFAULT_SORT);
  const [hideStale, setHideStale] = useState(false);
  const mountedRef = useRef(true);
  const refreshRevisionRef = useRef(0);
  const refreshAbortRef = useRef<AbortController | null>(null);
  const currentQuoteSymbolsKeyRef = useRef("");
  const quoteAbortRef = useRef<AbortController | null>(null);
  const previousMidRef = useRef<Record<string, number>>({});
  const quoteInFlightRef = useRef(false);
  const pendingQuoteSymbolsRef = useRef<readonly string[] | null>(null);

  useEffect(() => () => {
    mountedRef.current = false;
    refreshRevisionRef.current += 1;
    refreshAbortRef.current?.abort();
    quoteAbortRef.current?.abort();
    currentQuoteSymbolsKeyRef.current = "";
    pendingQuoteSymbolsRef.current = null;
  }, []);

  const refresh = useCallback(async () => {
    refreshAbortRef.current?.abort();
    const revision = refreshRevisionRef.current + 1;
    refreshRevisionRef.current = revision;
    const controller = new AbortController();
    refreshAbortRef.current = controller;
    setRefreshing(true);
    try {
      const [symbolResult, statsResult] = await Promise.allSettled([
        api.getSymbols({ signal: controller.signal }),
        api.getSymbolsStatistics({ signal: controller.signal })
      ]);

      if (!mountedRef.current || refreshRevisionRef.current !== revision) {
        return;
      }

      if (symbolResult.status === "fulfilled") {
        setSymbols(symbolResult.value);
        setLoadError(null);
      } else if (!isAbortError(symbolResult.reason)) {
        setLoadError(buildWatchlistErrorDisplay(symbolResult.reason, "Failed to load symbols"));
      }

      if (statsResult.status === "fulfilled") {
        setStats(statsResult.value);
      }
    } finally {
      if (refreshAbortRef.current === controller) {
        refreshAbortRef.current = null;
      }
      if (mountedRef.current && refreshRevisionRef.current === revision) {
        setRefreshing(false);
      }
    }
  }, [api.getSymbols, api.getSymbolsStatistics]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const addPendingSymbol = useCallback(async (event?: { preventDefault: () => void }) => {
    event?.preventDefault();
    const nextSymbols = parseWatchlistSymbols(pendingSymbol);

    if (nextSymbols.length === 0 || submitting) {
      return;
    }

    setPendingRemoveSymbol(null);
    setSubmitting(true);
    setSubmitFeedback(null);
    try {
      if (nextSymbols.length === 1) {
        const [next] = nextSymbols;
        const result = await api.addSymbol(next);
        if (!mountedRef.current) {
          return;
        }

        if (!result.success) {
          setSubmitFeedback({
            tone: "danger",
            message: `Could not add ${next}.`,
            providerSetupHandoff: buildProviderSetupHandoff("single-symbol-add")
          });
          return;
        }

        setSubmitFeedback({
          tone: "success",
          message: `Added ${next} to the watchlist.`,
          nextActionHandoff: buildLiveQuoteHandoff([next], "single-symbol-add")
        });
        setPendingSymbol("");
        await refresh();
      } else {
        const result = await api.bulkAddSymbols(nextSymbols);
        if (!mountedRef.current) {
          return;
        }

        setSubmitFeedback(buildBulkAddFeedback(result, nextSymbols.length, nextSymbols));
        if (result.added > 0 || result.errors.length === 0) {
          setPendingSymbol("");
        }

        if (result.added > 0) {
          await refresh();
        }
      }
    } catch (error) {
      if (!mountedRef.current) {
        return;
      }

      setSubmitFeedback({
        tone: "danger",
        ...buildWatchlistFeedbackDisplay(error, "Failed to add symbol"),
        providerSetupHandoff: buildProviderSetupHandoff("symbol-add-exception")
      });
    } finally {
      if (mountedRef.current) {
        setSubmitting(false);
      }
    }
  }, [api.addSymbol, api.bulkAddSymbols, pendingSymbol, refresh, submitting]);

  const applyStarterPack = useCallback(async (id: string) => {
    const pack = WATCHLIST_STARTER_PACKS.find((candidate) => candidate.id === id);
    if (!pack || submitting) {
      return;
    }

    setPendingRemoveSymbol(null);
    setSubmitting(true);
    setActiveStarterPackId(id);
    setSubmitFeedback(null);
    setPendingSymbol(pack.symbols.join(", "));
    try {
      const result = await api.bulkAddSymbols(pack.symbols);
      if (!mountedRef.current) {
        return;
      }

      setSubmitFeedback(buildStarterPackFeedback(pack.label, result, pack.symbols.length, pack.symbols));
      if (result.added > 0 || result.errors.length === 0) {
        setPendingSymbol("");
      }

      if (result.added > 0) {
        await refresh();
      }
    } catch (error) {
      if (!mountedRef.current) {
        return;
      }

      setSubmitFeedback({
        tone: "danger",
        ...buildWatchlistFeedbackDisplay(error, `Failed to add ${pack.label} starter pack.`),
        providerSetupHandoff: buildProviderSetupHandoff("starter-pack-exception")
      });
    } finally {
      if (mountedRef.current) {
        setActiveStarterPackId(null);
        setSubmitting(false);
      }
    }
  }, [api.bulkAddSymbols, refresh, submitting]);

  const removeSymbol = useCallback(async (symbol: string) => {
    const normalizedSymbol = normalizeSymbol(symbol);
    if (pendingRemoveSymbol !== normalizedSymbol) {
      setPendingRemoveSymbol(normalizedSymbol);
      setSelectedSymbol(normalizedSymbol);
      setSubmitFeedback(null);
      return;
    }

    setPendingRemoveSymbol(null);
    setRemoving((current) => ({ ...current, [symbol]: true }));
    try {
      const result = await api.removeSymbol(symbol);
      if (!result.success) {
        throw new Error(`Could not remove ${symbol}.`);
      }

      await refresh();
    } catch (error) {
      if (!mountedRef.current) {
        return;
      }

      setLoadError(buildWatchlistErrorDisplay(error, `Failed to remove ${symbol}`));
    } finally {
      if (mountedRef.current) {
        setRemoving((current) => {
          const { [symbol]: _removed, ...rest } = current;
          return rest;
        });
      }
    }
  }, [api.removeSymbol, pendingRemoveSymbol, refresh]);

  const subscribedSymbols = useMemo(() => symbols?.map((symbol) => symbol.symbol) ?? [], [symbols]);
  const quotesStream = useQuotesStream(subscribedSymbols);

  const applyQuotesSnapshot = useCallback((response: Pick<QuotesSnapshotResponse, "quotes">) => {
    const next: Record<string, QuotesSnapshotItem> = {};
    for (const quote of response.quotes) {
      next[quote.symbol.toUpperCase()] = quote;
    }

    setQuotes((current) => {
      const previous: Record<string, number> = {};
      for (const [symbol, quote] of Object.entries(current)) {
        if (quote.midPrice !== null && quote.midPrice !== undefined) {
          previous[symbol] = quote.midPrice;
        }
      }
      previousMidRef.current = previous;
      return next;
    });
    setQuoteFetchedAt(Date.now());
    setQuoteError(null);
  }, []);

  useEffect(() => {
    if (quotesStream.snapshot) {
      applyQuotesSnapshot(quotesStream.snapshot);
    }
  }, [applyQuotesSnapshot, quotesStream.snapshot]);

  const fetchQuotes = useCallback(async (currentSymbols: readonly string[]) => {
    if (currentSymbols.length === 0) {
      pendingQuoteSymbolsRef.current = null;
      return;
    }

    if (quoteInFlightRef.current) {
      pendingQuoteSymbolsRef.current = currentSymbols.slice();
      return;
    }

    const requestKey = buildQuoteSymbolsKey(currentSymbols);
    const controller = new AbortController();
    quoteAbortRef.current?.abort();
    quoteAbortRef.current = controller;
    quoteInFlightRef.current = true;
    try {
      const response = await api.getLiveQuotesSnapshot(currentSymbols, { signal: controller.signal });
      if (!mountedRef.current || currentQuoteSymbolsKeyRef.current !== requestKey) {
        return;
      }

      applyQuotesSnapshot(response);
    } catch (error) {
      if (mountedRef.current && currentQuoteSymbolsKeyRef.current === requestKey) {
        if (!isAbortError(error)) {
          setQuoteError(describeApiError(error, "Failed to load live quotes"));
        }
      }
    } finally {
      if (quoteAbortRef.current === controller) {
        quoteAbortRef.current = null;
      }
      quoteInFlightRef.current = false;
      const pendingSymbols = pendingQuoteSymbolsRef.current;
      pendingQuoteSymbolsRef.current = null;
      if (mountedRef.current && pendingSymbols && buildQuoteSymbolsKey(pendingSymbols) === currentQuoteSymbolsKeyRef.current) {
        void fetchQuotes(pendingSymbols);
      }
    }
  }, [api.getLiveQuotesSnapshot, applyQuotesSnapshot]);

  useEffect(() => {
    currentQuoteSymbolsKeyRef.current = buildQuoteSymbolsKey(subscribedSymbols);
    if (subscribedSymbols.length === 0) {
      quoteAbortRef.current?.abort();
      pendingQuoteSymbolsRef.current = null;
      setQuotes({});
      setQuoteError(null);
      setQuoteFetchedAt(null);
      previousMidRef.current = {};
      return;
    }

    void fetchQuotes(subscribedSymbols);
    if (quotesStream.healthy) {
      // The SSE stream is delivering; polling stays suspended until it degrades.
      return () => {
        quoteAbortRef.current?.abort();
      };
    }

    const interval = window.setInterval(() => void fetchQuotes(subscribedSymbols), QUOTE_POLL_INTERVAL_MS);
    return () => {
      quoteAbortRef.current?.abort();
      window.clearInterval(interval);
    };
  }, [fetchQuotes, quotesStream.healthy, subscribedSymbols]);

  const refreshQuotes = useCallback(async () => {
    if (subscribedSymbols.length === 0 || quoteRefreshing) {
      return;
    }

    setQuoteRefreshing(true);
    try {
      await fetchQuotes(subscribedSymbols);
    } finally {
      if (mountedRef.current) {
        setQuoteRefreshing(false);
      }
    }
  }, [fetchQuotes, quoteRefreshing, subscribedSymbols]);

  const addValidation = validatePendingSymbol(pendingSymbol);
  const pendingSymbols = parseWatchlistSymbols(pendingSymbol);
  const submitError = submitFeedback?.tone === "danger" ? submitFeedback.message : null;
  const allRows = useMemo(
    () => buildWatchlistRows(symbols ?? [], removing, quotes, previousMidRef.current, Date.now(), pendingRemoveSymbol),
    [pendingRemoveSymbol, quotes, removing, symbols]
  );
  const rows = useMemo(
    () => sortAndFilterWatchlistRows(allRows, sort, hideStale),
    [allRows, sort, hideStale]
  );
  const toggleSort = useCallback((columnId: WatchlistSortColumn) => {
    setSort((current) => toggleWatchlistSort(current, columnId));
  }, []);

  useEffect(() => {
    if (rows.length === 0) {
      if (selectedSymbol !== null) {
        setSelectedSymbol(null);
      }
      if (pendingRemoveSymbol !== null) {
        setPendingRemoveSymbol(null);
      }
      return;
    }

    if (!selectedSymbol || !rows.some((row) => row.symbol === selectedSymbol)) {
      setSelectedSymbol(rows[0].symbol);
    }

    if (pendingRemoveSymbol && !rows.some((row) => row.symbol === pendingRemoveSymbol)) {
      setPendingRemoveSymbol(null);
    }
  }, [pendingRemoveSymbol, rows, selectedSymbol]);

  const selectSymbol = useCallback((symbol: string) => {
    const normalizedSymbol = normalizeSymbol(symbol);
    setSelectedSymbol(normalizedSymbol);
    if (pendingRemoveSymbol && pendingRemoveSymbol !== normalizedSymbol) {
      setPendingRemoveSymbol(null);
    }
  }, [pendingRemoveSymbol]);

  const selectedRow = rows.find((row) => row.symbol === selectedSymbol) ?? rows[0] ?? null;
  const listState = buildListState(symbols, loadError);
  const totalQuoteCount = allRows.filter((row) => row.hasQuote).length;
  const totalStaleCount = allRows.filter((row) => row.quoteStale).length;
  const quoteStatus = buildQuoteStatus({
    listState,
    rowCount: allRows.length,
    quoteCount: totalQuoteCount,
    staleCount: totalStaleCount,
    quoteError,
    quoteFetchedAt
  });
  const staleFilterCommand = buildStaleFilterCommand(allRows.length, totalStaleCount, hideStale);
  const addSymbolField = buildWatchlistAddSymbolField(submitFeedback, submitting);

  return {
    pendingSymbol,
    setPendingSymbol,
    refreshing,
    submitting,
    submitError,
    submitFeedback,
    loadError,
    stats: buildWatchlistStats(stats),
    rows,
    sort,
    toggleSort,
    hideStale,
    setHideStale: (value) => {
      setPendingRemoveSymbol(null);
      setHideStale(value);
    },
    staleFilterCommand,
    listRetryCommand: buildListRetryCommand(refreshing),
    listState,
    listDescription: buildListDescription(listState, rows.length, allRows.length, hideStale, loadError),
    tableLabel: "Subscribed symbol watchlist",
    tableCaption: buildTableCaption(sort, hideStale),
    formLabel: "Add symbols to the watchlist",
    addSymbolField,
    inputId: addSymbolField.id,
    inputPlaceholder: addSymbolField.placeholder,
    inputHelpId: addSymbolField.describedBy,
    inputHelpText: addSymbolField.helperText,
    addButtonLabel: submitting ? "Adding…" : pendingSymbols.length > 1 ? `Add ${pendingSymbols.length}` : "Add",
    addButtonAriaLabel: addValidation
      ? `Add symbol unavailable: ${addValidation}`
      : pendingSymbols.length > 1
        ? `Add ${pendingSymbols.length} symbols to watchlist: ${pendingSymbols.join(", ")}`
        : `Add ${pendingSymbols[0]} to watchlist`,
    addDisabled: submitting || addValidation !== null,
    addDisabledReason: submitting ? "Symbol add request is already running." : addValidation,
    refreshButtonLabel: refreshing ? "Refreshing…" : "Refresh",
    refreshButtonAriaLabel: refreshing ? "Refreshing watchlist" : "Refresh watchlist",
    refreshDisabled: refreshing,
    toolbarItems: buildToolbarItems(stats, rows.length, listState),
    quoteStatusLabel: quoteStatus.label,
    quoteStatusTone: quoteStatus.tone,
    quoteStatusDetails: quoteStatus.details,
    quoteFreshnessTimestamp: quoteFetchedAt ? new Date(quoteFetchedAt).toISOString() : null,
    quoteFreshnessError: quoteError?.summary ?? null,
    quoteStreamHealthy: quotesStream.healthy,
    quoteProviderSetupHandoff: quoteError ? buildProviderSetupHandoff("live-quotes") : null,
    quoteRefreshCommand: buildQuoteRefreshCommand(listState, rows.length, quoteRefreshing),
    starterPackGroupLabel: "Watchlist starter packs",
    starterPackEyebrow: "Quick add",
    starterPacks: buildStarterPackCommands(submitting, activeStarterPackId),
    selectedSymbol: selectedRow?.symbol ?? null,
    selectedRowId: selectedRow?.symbol ?? null,
    selectedDetail: buildWatchlistSelectedDetail(selectedRow),
    detailPanelId: "watchlist-selected-symbol-detail",
    detailPanelTitle: "Selected symbol inspector",
    detailPanelDescription: "Inspect the active watchlist row without leaving the Data lane.",
    detailPanelEmptyText: "No symbol is selected. Add a symbol or wait for the watchlist to load.",
    detailPanelAriaLabel: "Selected watchlist symbol detail",
    selectSymbol,
    refresh,
    refreshQuotes,
    addPendingSymbol,
    applyStarterPack,
    removeSymbol
  };
}

export function buildWatchlistAddSymbolField(
  submitFeedback: WatchlistSubmitFeedback | null,
  submitting: boolean
): WatchlistAddSymbolFieldViewModel {
  const helperId = "add-symbol-help";
  const feedbackId = "add-symbol-feedback";
  const invalid = submitFeedback?.tone === "danger";

  return {
    id: "add-symbol-input",
    label: "Add symbol",
    placeholder: "Add symbols (e.g. MSFT, SPY)",
    helperId,
    helperText: "Paste one or more symbols separated by spaces or commas. Meridian normalizes them to uppercase.",
    feedbackId,
    feedbackRole: submitFeedback?.tone === "success" ? "status" : "alert",
    describedBy: submitFeedback ? `${feedbackId} ${helperId}` : helperId,
    invalid,
    errorMessageId: invalid ? feedbackId : undefined,
    disabled: submitting
  };
}

export function buildWatchlistRows(
  symbols: SymbolRecord[],
  removing: Record<string, boolean> = {},
  quotes: Record<string, QuotesSnapshotItem> = {},
  previousMid: Record<string, number> = {},
  now = Date.now(),
  pendingRemoveSymbol: string | null = null
): WatchlistRowViewModel[] {
  const pendingRemoveKey = pendingRemoveSymbol ? normalizeSymbol(pendingRemoveSymbol) : null;
  return [...symbols]
    .sort((left, right) => left.symbol.localeCompare(right.symbol))
    .map((record) => {
      const isRemoving = removing[record.symbol] === true;
      const removeConfirmationPending = pendingRemoveKey === normalizeSymbol(record.symbol);
      const removeStatusId = (isRemoving || removeConfirmationPending)
        ? `watchlist-remove-${stableSymbolId(record.symbol)}-status`
        : null;
      const providerLabel = record.provider ?? "No provider";
      const lastEventLabel = formatRelative(record.lastEventAt);
      const eventCountLabel = formatCount(record.eventCount);
      const historyLabel = record.hasHistoricalData ? "Available" : "Missing";
      const quote = quotes[record.symbol.toUpperCase()];
      const priorMid = previousMid[record.symbol.toUpperCase()];
      const quoteAgeMs = quote ? now - new Date(quote.timestamp).getTime() : null;
      const quoteStale = quoteAgeMs !== null && quoteAgeMs > QUOTE_STALE_THRESHOLD_MS;
      const lastTone = resolveLastTone(quote, priorMid);
      const session = quote?.session ?? null;
      const changeLabel = session ? formatChange(session.change) : WATCHLIST_EMPTY_VALUE;
      const changePercentLabel = session ? formatChangePercent(session.changePercent) : WATCHLIST_EMPTY_VALUE;
      const dayRangeLabel = session ? formatDayRange(session.high, session.low) : WATCHLIST_EMPTY_VALUE;
      const changeTone = resolveChangeTone(session?.change);

      return {
        symbol: record.symbol,
        status: record.status,
        statusVariant: statusVariant(record.status),
        providerLabel,
        lastEventLabel,
        eventCountLabel,
        historyLabel,
        hasHistoricalData: record.hasHistoricalData,
        bidLabel: quote ? formatPriceSize(quote.bidPrice, quote.bidSize) : WATCHLIST_EMPTY_VALUE,
        askLabel: quote ? formatPriceSize(quote.askPrice, quote.askSize) : WATCHLIST_EMPTY_VALUE,
        lastPriceLabel: quote ? formatPrice(quote.lastPrice) : WATCHLIST_EMPTY_VALUE,
        changeLabel,
        changePercentLabel,
        dayRangeLabel,
        spreadLabel: quote ? formatSpread(quote.spread, quote.midPrice) : WATCHLIST_EMPTY_VALUE,
        quoteAgeLabel: quote ? formatRelative(quote.timestamp, now) : WATCHLIST_NO_QUOTE_LABEL,
        hasQuote: quote !== undefined,
        quoteStale,
        lastTone,
        changeTone,
        isRemoving,
        quoteHref: workstationRouteWithQuery("dataQuotes", { symbol: record.symbol }),
        quoteAriaLabel: `View live quotes for ${record.symbol}`,
        inspectLabel: "Inspect",
        inspectAriaLabel: `Inspect ${record.symbol} watchlist detail`,
        removeLabel: isRemoving ? "Removing…" : removeConfirmationPending ? "Confirm remove" : "Remove",
        removeButtonVariant: removeConfirmationPending ? "destructive" : "outline",
        removeStatusId,
        removeStatusLabel: isRemoving ? "Removing" : removeConfirmationPending ? "Pending confirmation" : null,
        removeStatusTone: isRemoving ? "danger" : "warning",
        removeAriaLabel: isRemoving
          ? `Removing ${record.symbol} from watchlist`
          : removeConfirmationPending
            ? `Confirm remove ${record.symbol} from watchlist. This stops watchlist tracking for this row.`
            : `Remove ${record.symbol} from watchlist`,
        removeDisabledReason: isRemoving ? `${record.symbol} removal is already running.` : null,
        rowSelectAriaLabel: `Select ${record.symbol} watchlist row. ${record.symbol}. Status ${record.status}.${removeConfirmationPending ? " Remove confirmation pending." : ""}`,
        ariaLabel: `${record.symbol}. Status ${record.status}. Bid ${quote ? formatPriceSize(quote.bidPrice, quote.bidSize) : "not available"}. Ask ${quote ? formatPriceSize(quote.askPrice, quote.askSize) : "not available"}. Last ${quote ? formatPrice(quote.lastPrice) : "not available"}. Change ${changeLabel}. Change percent ${changePercentLabel}. Day high low ${dayRangeLabel}. Provider ${providerLabel}. Last event ${lastEventLabel}. ${eventCountLabel} events. History ${historyLabel}.${removeConfirmationPending ? " Remove confirmation pending." : ""}`,
        lastPriceValue: toFiniteNumber(quote?.lastPrice),
        changeValue: toFiniteNumber(session?.change),
        changePercentValue: toFiniteNumber(session?.changePercent),
        spreadValue: toFiniteNumber(quote?.spread),
        quoteAgeMs: quoteAgeMs ?? null
      };
    });
}

export function sortAndFilterWatchlistRows(
  rows: WatchlistRowViewModel[],
  sort: WatchlistSortState,
  hideStale: boolean
): WatchlistRowViewModel[] {
  const filtered = hideStale ? rows.filter((row) => !row.quoteStale) : rows;
  const direction = sort.direction === "asc" ? 1 : -1;
  return [...filtered].sort((left, right) => {
    const leftKey = sortKeyFor(left, sort.columnId);
    const rightKey = sortKeyFor(right, sort.columnId);
    const leftMissing = leftKey === null;
    const rightMissing = rightKey === null;
    if (leftMissing && rightMissing) {
      return left.symbol.localeCompare(right.symbol);
    }
    if (leftMissing) return 1;
    if (rightMissing) return -1;

    const primary = compareSortKeys(leftKey as number | string, rightKey as number | string);
    if (primary !== 0) {
      return primary * direction;
    }
    return left.symbol.localeCompare(right.symbol);
  });
}

function sortKeyFor(row: WatchlistRowViewModel, columnId: WatchlistSortColumn): number | string | null {
  switch (columnId) {
    case "symbol":
      return row.symbol;
    case "status":
      return STATUS_RANK[row.status];
    case "last":
      return row.lastPriceValue;
    case "change-percent":
      return row.changePercentValue;
    case "spread":
      return row.spreadValue;
    case "quote-age":
      return row.quoteAgeMs;
  }
}

function compareSortKeys(left: number | string, right: number | string): number {
  if (typeof left === "string" && typeof right === "string") {
    return left.localeCompare(right);
  }
  if (typeof left === "number" && typeof right === "number") {
    return left - right;
  }
  return String(left).localeCompare(String(right));
}

function toFiniteNumber(value: number | null | undefined): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

export function toggleWatchlistSort(
  current: WatchlistSortState,
  columnId: WatchlistSortColumn,
  defaultSort: WatchlistSortState = DEFAULT_SORT
): WatchlistSortState {
  if (current.columnId !== columnId) {
    return { columnId, direction: columnId === "symbol" ? "asc" : "desc" };
  }
  if (current.direction === "desc") {
    return { columnId, direction: "asc" };
  }
  return defaultSort;
}

export function buildWatchlistSelectedDetail(
  row: WatchlistRowViewModel | null
): WatchlistSelectedDetail | null {
  if (!row) {
    return null;
  }

  const description = !row.hasQuote
    ? "No live quote has been returned for this symbol yet. Check provider setup or refresh prices after the subscription settles."
    : row.quoteStale
      ? "The latest quote is stale. Refresh prices or verify provider connectivity before using this row as current evidence."
      : "Live quote, provider posture, and collection evidence are ready for operator review.";

  return {
    symbol: row.symbol,
    title: row.symbol,
    subtitle: `${row.providerLabel} - ${row.eventCountLabel} events - history ${row.historyLabel.toLowerCase()}`,
    description,
    statusLabel: row.status,
    statusVariant: row.statusVariant,
    statusAriaLabel: `${row.symbol} status ${row.status}`,
    regionLabel: `${row.symbol} watchlist detail`,
    quoteActionLabel: "Open live quote",
    quoteActionHref: row.quoteHref,
    quoteActionAriaLabel: row.quoteAriaLabel,
    fields: [
      buildSelectedDetailField("Bid x size", row.bidLabel, row.hasQuote ? "default" : "muted"),
      buildSelectedDetailField("Ask x size", row.askLabel, row.hasQuote ? "default" : "muted"),
      buildSelectedDetailField("Last", row.lastPriceLabel, row.lastTone),
      buildSelectedDetailField("Chg", row.changeLabel, row.changeTone),
      buildSelectedDetailField("Chg%", row.changePercentLabel, row.changeTone),
      buildSelectedDetailField("Day H/L", row.dayRangeLabel, row.hasQuote ? "default" : "muted"),
      buildSelectedDetailField("Spread", row.spreadLabel, row.hasQuote ? "default" : "muted"),
      buildSelectedDetailField("Quote age", row.quoteAgeLabel, row.quoteStale ? "warning" : row.hasQuote ? "success" : "muted"),
      buildSelectedDetailField("Provider", row.providerLabel, row.providerLabel === "No provider" ? "warning" : "default"),
      buildSelectedDetailField("History", row.historyLabel, row.hasHistoricalData ? "success" : "warning"),
      buildSelectedDetailField("Last event", row.lastEventLabel, row.lastEventLabel === "Never" ? "muted" : "default")
    ]
  };
}

function buildSelectedDetailField(
  label: string,
  value: string,
  tone: WatchlistDetailFieldTone
): WatchlistSelectedDetailField {
  return { label, value, tone };
}

export function buildWatchlistStats(stats: SymbolStatistics | null): MetricSnapshot[] {
  return [
    buildStat("total", "Total", stats?.totalSymbols),
    buildStat("monitored", "Monitored", stats?.monitoredSymbols),
    buildStat("archived", "Archived", stats?.archivedSymbols),
    buildStat("errors", "Errors", stats?.symbolsWithErrors, stats && stats.symbolsWithErrors > 0 ? "danger" : "default")
  ];
}

export { formatRelativeAge as formatRelative } from "@/lib/time";

export function validatePendingSymbol(value: string): string | null {
  return parseWatchlistSymbols(value).length === 0 ? "Enter at least one symbol before adding it." : null;
}

export function parseWatchlistSymbols(value: string): string[] {
  const symbols = value
    .split(/[\s,]+/)
    .map((symbol) => normalizeSymbol(symbol))
    .filter(Boolean);

  return Array.from(new Set(symbols));
}

function buildQuoteSymbolsKey(symbols: readonly string[]): string {
  return symbols.map((symbol) => normalizeSymbol(symbol)).sort().join("|");
}

export function buildBulkAddFeedback(
  result: { added: number; skipped: number; errors: string[] },
  requestedCount: number,
  requestedSymbols: readonly string[] = []
): WatchlistSubmitFeedback {
  const base = `Added ${formatCount(result.added)} of ${formatCount(requestedCount)} symbol${requestedCount === 1 ? "" : "s"}`;
  const skipped = result.skipped > 0 ? `; ${formatCount(result.skipped)} skipped` : "";
  const errors = result.errors.length > 0 ? `; ${result.errors.join("; ")}` : "";
  const nextActionHandoff = result.added > 0 ? buildLiveQuoteHandoff(requestedSymbols, "bulk-add") : undefined;

  if (result.errors.length > 0 && result.added === 0) {
    return {
      tone: "danger",
      message: `${base}${skipped}${errors}.`,
      providerSetupHandoff: buildProviderSetupHandoff("bulk-add-errors")
    };
  }

  if (result.errors.length > 0 || result.skipped > 0) {
    return {
      tone: "warning",
      message: `${base}${skipped}${errors}.`,
      ...(result.errors.length > 0 ? { providerSetupHandoff: buildProviderSetupHandoff("bulk-add-partial") } : {}),
      ...(nextActionHandoff ? { nextActionHandoff } : {})
    };
  }

  return {
    tone: "success",
    message: `${base}.`,
    ...(nextActionHandoff ? { nextActionHandoff } : {})
  };
}

export function buildStarterPackFeedback(
  label: string,
  result: { added: number; skipped: number; errors: string[] },
  requestedCount: number,
  requestedSymbols: readonly string[] = []
): WatchlistSubmitFeedback {
  const base = `${label}: added ${formatCount(result.added)} of ${formatCount(requestedCount)} symbols`;
  const skipped = result.skipped > 0 ? `; ${formatCount(result.skipped)} skipped` : "";
  const errors = result.errors.length > 0 ? `; ${result.errors.join("; ")}` : "";
  const nextActionHandoff = result.added > 0 ? buildLiveQuoteHandoff(requestedSymbols, "starter-pack") : undefined;

  if (result.errors.length > 0 && result.added === 0) {
    return {
      tone: "danger",
      message: `${base}${skipped}${errors}.`,
      providerSetupHandoff: buildProviderSetupHandoff("starter-pack-errors")
    };
  }

  if (result.errors.length > 0 || result.skipped > 0) {
    return {
      tone: "warning",
      message: `${base}${skipped}${errors}.`,
      ...(result.errors.length > 0 ? { providerSetupHandoff: buildProviderSetupHandoff("starter-pack-partial") } : {}),
      ...(nextActionHandoff ? { nextActionHandoff } : {})
    };
  }

  return {
    tone: "success",
    message: `${base}.`,
    ...(nextActionHandoff ? { nextActionHandoff } : {})
  };
}

export function buildProviderSetupHandoff(reason: string): WatchlistProviderSetupHandoff {
  return {
    href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
    label: "Fix provider setup",
    ariaLabel: `Open provider setup from watchlist ${reason}`,
    detail: "Review provider credentials and connection status in Settings."
  };
}

export function buildLiveQuoteHandoff(symbols: readonly string[], reason: string): WatchlistRouteHandoff | undefined {
  const symbol = symbols.map((candidate) => normalizeSymbol(candidate)).find(Boolean);
  if (!symbol) {
    return undefined;
  }

  return {
    href: workstationRouteWithQuery("dataQuotes", { symbol }),
    label: "Review live quote",
    ariaLabel: `Open live quotes for ${symbol} from watchlist ${reason}`,
    detail: `Review the ${symbol} live quote, chart, and quick-trade ticket.`
  };
}

export function buildStarterPackCommands(
  submitting: boolean,
  activeStarterPackId: string | null
): WatchlistStarterPackCommandState[] {
  return WATCHLIST_STARTER_PACKS.map((pack) => {
    const busy = submitting && activeStarterPackId === pack.id;
    return {
      id: pack.id,
      label: pack.label,
      symbols: pack.symbols,
      symbolsLabel: pack.symbols.join(", "),
      ariaLabel: busy
        ? `Adding ${pack.label} starter pack`
        : `Add ${pack.label} starter pack: ${pack.symbols.join(", ")}`,
      busyLabel: `Adding ${pack.label}…`,
      disabled: submitting,
      disabledReason: submitting ? "Wait for the current symbol add request to finish." : null,
      busy
    };
  });
}

function buildStat(
  id: string,
  label: string,
  value: number | undefined,
  tone: MetricSnapshot["tone"] = "default"
): MetricSnapshot {
  const displayValue = value === undefined ? WATCHLIST_EMPTY_VALUE : formatCount(value);
  return {
    id,
    label,
    value: displayValue,
    delta: "",
    tone
  };
}

export function buildListRetryCommand(refreshing: boolean): WatchlistListRetryCommandState {
  const label = refreshing ? "Retrying…" : "Retry watchlist";

  return {
    label,
    ariaLabel: refreshing ? "Retrying symbol watchlist load" : "Retry symbol watchlist load",
    disabled: refreshing,
    disabledReason: refreshing ? "Watchlist refresh is already running." : null,
    busy: refreshing
  };
}

function buildListState(symbols: SymbolRecord[] | null, loadError: ApiErrorDisplay | null): WatchlistListState {
  if (loadError && symbols === null) {
    return "error";
  }

  if (symbols === null) {
    return "loading";
  }

  if (symbols.length === 0) {
    return "empty";
  }

  return "ready";
}

function buildListDescription(
  state: WatchlistListState,
  visibleCount: number,
  totalCount: number,
  hideStale: boolean,
  loadError: ApiErrorDisplay | null
): string {
  switch (state) {
    case "loading":
      return "Loading symbols…";
    case "error":
      return loadError?.summary ?? "Symbol watchlist failed to load.";
    case "empty":
      return "No symbols configured. Add one above to start collecting live data.";
    case "ready": {
      const hidden = totalCount - visibleCount;
      if (hideStale && hidden > 0) {
        return `${visibleCount} of ${totalCount} symbol${totalCount === 1 ? "" : "s"} shown; ${hidden} stale hidden.`;
      }
      return `${totalCount} symbol${totalCount === 1 ? "" : "s"} configured.`;
    }
  }
}

const SORT_COLUMN_LABELS: Record<WatchlistSortColumn, string> = {
  "symbol": "symbol",
  "status": "status",
  "last": "last price",
  "change-percent": "day change percent",
  "spread": "spread",
  "quote-age": "quote age"
};

function buildTableCaption(sort: WatchlistSortState, hideStale: boolean): string {
  const directionLabel = sort.direction === "asc" ? "ascending" : "descending";
  const sortLabel = SORT_COLUMN_LABELS[sort.columnId];
  const filterLabel = hideStale ? " Stale rows are hidden." : "";
  return `Subscribed symbols sorted by ${sortLabel} ${directionLabel}.${filterLabel} Columns show status, live bid and ask, last price, day change, day high/low, spread, quote age, provider, and actions.`;
}

export function buildStaleFilterCommand(
  rowCount: number,
  staleCount: number,
  hideStale: boolean
): WatchlistFilterCommandState {
  const hiddenCount = hideStale ? staleCount : 0;
  if (rowCount === 0) {
    return {
      label: "Hide stale",
      ariaLabel: "Hide stale quotes. Disabled because there are no symbols yet.",
      pressed: false,
      disabled: true,
      disabledReason: "Add a symbol before filtering stale quotes.",
      hiddenCount: 0
    };
  }

  if (staleCount === 0 && !hideStale) {
    return {
      label: "Hide stale",
      ariaLabel: "Hide stale quotes. No stale quotes detected.",
      pressed: false,
      disabled: true,
      disabledReason: "No stale quotes to hide.",
      hiddenCount: 0
    };
  }

  return {
    label: hideStale ? `Showing fresh only (${staleCount} hidden)` : `Hide stale (${staleCount})`,
    ariaLabel: hideStale
      ? `Showing fresh quotes only. ${staleCount} stale row${staleCount === 1 ? "" : "s"} hidden. Click to show all rows.`
      : `Hide ${staleCount} stale quote${staleCount === 1 ? "" : "s"}.`,
    pressed: hideStale,
    disabled: false,
    disabledReason: null,
    hiddenCount
  };
}

function buildToolbarItems(
  stats: SymbolStatistics | null,
  rowCount: number,
  listState: WatchlistListState
): WatchlistScreenViewModel["toolbarItems"] {
  return [
    { id: "visible", label: "Visible", value: listState === "ready" ? formatCount(rowCount) : WATCHLIST_EMPTY_VALUE },
    { id: "monitored", label: "Monitored", value: stats ? formatCount(stats.monitoredSymbols) : WATCHLIST_EMPTY_VALUE, active: Boolean(stats && stats.monitoredSymbols > 0) },
    { id: "errors", label: "Errors", value: stats ? formatCount(stats.symbolsWithErrors) : WATCHLIST_EMPTY_VALUE, active: Boolean(stats && stats.symbolsWithErrors > 0) },
    { id: "events", label: "24h events", value: stats ? formatCount(stats.totalEventsLast24h) : WATCHLIST_EMPTY_VALUE }
  ];
}

export function buildQuoteStatus({
  listState,
  rowCount,
  quoteCount,
  staleCount,
  quoteError,
  quoteFetchedAt,
  now = Date.now()
}: {
  listState: WatchlistListState;
  rowCount: number;
  quoteCount: number;
  staleCount: number;
  quoteError: ApiErrorDisplay | null;
  quoteFetchedAt: number | null;
  now?: number;
}): { label: string; tone: WatchlistQuoteStatusTone; details: string[] } {
  if (listState === "empty") {
    return { label: "Live prices are idle until symbols are added.", tone: "default", details: [] };
  }

  if (quoteError) {
    return { label: `Live prices unavailable: ${quoteError.summary}`, tone: "danger", details: quoteError.details };
  }

  if (quoteFetchedAt) {
    const updatedLabel = formatRelative(new Date(quoteFetchedAt).toISOString(), now);
    const coverageLabel = quoteCount === rowCount
      ? `Live prices for ${rowCount} symbol${rowCount === 1 ? "" : "s"}`
      : `Live prices for ${quoteCount} of ${rowCount} symbols`;
    const staleLabel = staleCount > 0 ? `; ${staleCount} stale` : "";

    return {
      label: `${coverageLabel}${staleLabel}; updated ${updatedLabel}.`,
      tone: quoteCount === rowCount && staleCount === 0 ? "default" : "warning",
      details: []
    };
  }

  return { label: "Live prices waiting for first tick.", tone: "warning", details: [] };
}

export function buildQuoteRefreshCommand(
  listState: WatchlistListState,
  rowCount: number,
  refreshing: boolean
): WatchlistQuoteRefreshCommandState {
  const label = refreshing ? "Refreshing prices…" : "Refresh prices";

  if (refreshing) {
    return {
      label,
      ariaLabel: "Refreshing live prices",
      disabled: true,
      disabledReason: "Live price refresh is already running.",
      busy: true
    };
  }

  if (listState === "loading") {
    return {
      label,
      ariaLabel: "Refresh live prices",
      disabled: true,
      disabledReason: "Wait for symbols to load before refreshing live prices.",
      busy: false
    };
  }

  if (listState === "error") {
    return {
      label,
      ariaLabel: "Refresh live prices",
      disabled: true,
      disabledReason: "Resolve the symbol list error before refreshing live prices.",
      busy: false
    };
  }

  if (listState === "empty" || rowCount === 0) {
    return {
      label,
      ariaLabel: "Refresh live prices",
      disabled: true,
      disabledReason: "Add a symbol before refreshing live prices.",
      busy: false
    };
  }

  return {
    label,
    ariaLabel: "Refresh live prices",
    disabled: false,
    disabledReason: null,
    busy: false
  };
}

function buildWatchlistErrorDisplay(error: unknown, fallback: string): ApiErrorDisplay {
  return describeApiError(error, fallback);
}

function buildWatchlistFeedbackDisplay(
  error: unknown,
  fallback: string
): Pick<WatchlistSubmitFeedback, "message" | "details"> {
  const display = describeApiError(error, fallback);
  return {
    message: display.summary,
    details: display.details
  };
}

function statusVariant(status: SymbolRecord["status"]): WatchlistBadgeVariant {
  switch (status) {
    case "Active":
      return "success";
    case "Monitored":
      return "default";
    case "Archived":
      return "outline";
    case "Error":
      return "danger";
  }
}

function normalizeSymbol(value: string): string {
  return value.trim().toUpperCase();
}

function stableSymbolId(value: string): string {
  const stable = normalizeSymbol(value).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
  return stable || "symbol";
}

function formatCount(value: number): string {
  return value.toLocaleString();
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException
    ? error.name === "AbortError"
    : error instanceof Error && error.name === "AbortError";
}

function formatPrice(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return WATCHLIST_EMPTY_VALUE;
  }

  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 4
  });
}

function formatSize(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return WATCHLIST_EMPTY_VALUE;
  }

  return value.toLocaleString();
}

function formatPriceSize(price: number | null | undefined, size: number | null | undefined): string {
  return `${formatPrice(price)} x ${formatSize(size)}`;
}

function formatSpread(spread: number | null | undefined, mid: number | null | undefined): string {
  if (spread === null || spread === undefined || Number.isNaN(spread)) {
    return WATCHLIST_EMPTY_VALUE;
  }

  if (mid && mid > 0) {
    const basisPoints = (spread / mid) * 10_000;
    return `${spread.toFixed(2)} (${basisPoints.toFixed(1)} bps)`;
  }

  return spread.toFixed(2);
}

function formatChange(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return WATCHLIST_EMPTY_VALUE;
  }

  const sign = value > 0 ? "+" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 })}`;
}

function formatChangePercent(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return WATCHLIST_EMPTY_VALUE;
  }

  const sign = value > 0 ? "+" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%`;
}

function formatDayRange(high: number | null | undefined, low: number | null | undefined): string {
  return `${formatPrice(high)} / ${formatPrice(low)}`;
}

function resolveLastTone(quote: QuotesSnapshotItem | undefined, previousMid: number | undefined): WatchlistPriceTone {
  if (!quote || quote.lastPrice === null || quote.lastPrice === undefined || previousMid === undefined) {
    return "default";
  }

  if (quote.lastPrice > previousMid) {
    return "success";
  }

  if (quote.lastPrice < previousMid) {
    return "danger";
  }

  return "default";
}

function resolveChangeTone(value: number | null | undefined): WatchlistDetailFieldTone {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return "muted";
  }

  if (value > 0) {
    return "success";
  }

  if (value < 0) {
    return "danger";
  }

  return "default";
}
