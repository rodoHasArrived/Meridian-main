import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ApiRequestOptions } from "@/lib/api";
import type { MetricSnapshot, QuotesSnapshotItem, SymbolRecord, SymbolStatistics } from "@/types";

export type WatchlistBadgeVariant = "default" | "outline" | "success" | "warning" | "danger" | "paper" | "live" | "research";
export type WatchlistListState = "loading" | "error" | "empty" | "ready";
export type WatchlistPriceTone = "default" | "success" | "danger";
export type WatchlistQuoteStatusTone = "default" | "warning" | "danger";
export type WatchlistDetailFieldTone = "default" | "success" | "warning" | "danger" | "muted";

const QUOTE_POLL_INTERVAL_MS = 2000;
const QUOTE_STALE_THRESHOLD_MS = 15_000;

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
  providerSetupHandoff?: WatchlistProviderSetupHandoff;
}

export interface WatchlistProviderSetupHandoff {
  href: string;
  label: string;
  ariaLabel: string;
  detail: string;
}

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
  spreadLabel: string;
  quoteAgeLabel: string;
  hasQuote: boolean;
  quoteStale: boolean;
  lastTone: WatchlistPriceTone;
  isRemoving: boolean;
  quoteHref: string;
  quoteAriaLabel: string;
  inspectLabel: string;
  inspectAriaLabel: string;
  removeLabel: string;
  removeAriaLabel: string;
  removeDisabledReason: string | null;
  rowSelectAriaLabel: string;
  ariaLabel: string;
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
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
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

export interface WatchlistScreenViewModel {
  pendingSymbol: string;
  setPendingSymbol: (value: string) => void;
  refreshing: boolean;
  submitting: boolean;
  submitError: string | null;
  submitFeedback: WatchlistSubmitFeedback | null;
  loadError: string | null;
  stats: MetricSnapshot[];
  rows: WatchlistRowViewModel[];
  listState: WatchlistListState;
  listDescription: string;
  tableLabel: string;
  tableCaption: string;
  formLabel: string;
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
  const [loadError, setLoadError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [pendingSymbol, setPendingSymbol] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [submitFeedback, setSubmitFeedback] = useState<WatchlistSubmitFeedback | null>(null);
  const [removing, setRemoving] = useState<Record<string, boolean>>({});
  const [quotes, setQuotes] = useState<Record<string, QuotesSnapshotItem>>({});
  const [quoteError, setQuoteError] = useState<string | null>(null);
  const [quoteFetchedAt, setQuoteFetchedAt] = useState<number | null>(null);
  const [quoteRefreshing, setQuoteRefreshing] = useState(false);
  const [activeStarterPackId, setActiveStarterPackId] = useState<string | null>(null);
  const [selectedSymbol, setSelectedSymbol] = useState<string | null>(null);
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
        setLoadError(messageFromError(symbolResult.reason, "Failed to load symbols"));
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

        setSubmitFeedback({ tone: "success", message: `Added ${next} to the watchlist.` });
        setPendingSymbol("");
        await refresh();
      } else {
        const result = await api.bulkAddSymbols(nextSymbols);
        if (!mountedRef.current) {
          return;
        }

        setSubmitFeedback(buildBulkAddFeedback(result, nextSymbols.length));
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
        message: messageFromError(error, "Failed to add symbol"),
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

    setSubmitting(true);
    setActiveStarterPackId(id);
    setSubmitFeedback(null);
    setPendingSymbol(pack.symbols.join(", "));
    try {
      const result = await api.bulkAddSymbols(pack.symbols);
      if (!mountedRef.current) {
        return;
      }

      setSubmitFeedback(buildStarterPackFeedback(pack.label, result, pack.symbols.length));
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
        message: messageFromError(error, `Failed to add ${pack.label} starter pack.`),
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

      setLoadError(messageFromError(error, `Failed to remove ${symbol}`));
    } finally {
      if (mountedRef.current) {
        setRemoving((current) => {
          const { [symbol]: _removed, ...rest } = current;
          return rest;
        });
      }
    }
  }, [api.removeSymbol, refresh]);

  const subscribedSymbols = useMemo(() => symbols?.map((symbol) => symbol.symbol) ?? [], [symbols]);

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
    } catch (error) {
      if (mountedRef.current && currentQuoteSymbolsKeyRef.current === requestKey) {
        if (!isAbortError(error)) {
          setQuoteError(messageFromError(error, "Failed to load live quotes"));
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
  }, [api.getLiveQuotesSnapshot]);

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
    const interval = window.setInterval(() => void fetchQuotes(subscribedSymbols), QUOTE_POLL_INTERVAL_MS);
    return () => {
      quoteAbortRef.current?.abort();
      window.clearInterval(interval);
    };
  }, [fetchQuotes, subscribedSymbols]);

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
  const rows = useMemo(
    () => buildWatchlistRows(symbols ?? [], removing, quotes, previousMidRef.current),
    [symbols, removing, quotes]
  );
  useEffect(() => {
    if (rows.length === 0) {
      if (selectedSymbol !== null) {
        setSelectedSymbol(null);
      }
      return;
    }

    if (!selectedSymbol || !rows.some((row) => row.symbol === selectedSymbol)) {
      setSelectedSymbol(rows[0].symbol);
    }
  }, [rows, selectedSymbol]);

  const selectedRow = rows.find((row) => row.symbol === selectedSymbol) ?? rows[0] ?? null;
  const listState = buildListState(symbols, loadError);
  const quoteStatus = buildQuoteStatus({
    listState,
    rowCount: rows.length,
    quoteCount: rows.filter((row) => row.hasQuote).length,
    staleCount: rows.filter((row) => row.quoteStale).length,
    quoteError,
    quoteFetchedAt
  });

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
    listState,
    listDescription: buildListDescription(listState, rows.length, loadError),
    tableLabel: "Subscribed symbol watchlist",
    tableCaption: "Subscribed symbols sorted alphabetically with status, live bid and ask, last price, spread, quote age, provider, and actions.",
    formLabel: "Add symbols to the watchlist",
    inputId: "add-symbol-input",
    inputPlaceholder: "Add symbols (e.g. MSFT, SPY)",
    inputHelpId: submitFeedback ? "add-symbol-feedback add-symbol-help" : "add-symbol-help",
    inputHelpText: "Paste one or more symbols separated by spaces or commas. Meridian normalizes them to uppercase.",
    addButtonLabel: submitting ? "Adding..." : pendingSymbols.length > 1 ? `Add ${pendingSymbols.length}` : "Add",
    addButtonAriaLabel: addValidation
      ? `Add symbol unavailable: ${addValidation}`
      : pendingSymbols.length > 1
        ? `Add ${pendingSymbols.length} symbols to watchlist: ${pendingSymbols.join(", ")}`
        : `Add ${pendingSymbols[0]} to watchlist`,
    addDisabled: submitting || addValidation !== null,
    addDisabledReason: submitting ? "Symbol add request is already running." : addValidation,
    refreshButtonLabel: refreshing ? "Refreshing..." : "Refresh",
    refreshButtonAriaLabel: refreshing ? "Refreshing watchlist" : "Refresh watchlist",
    refreshDisabled: refreshing,
    toolbarItems: buildToolbarItems(stats, rows.length, listState),
    quoteStatusLabel: quoteStatus.label,
    quoteStatusTone: quoteStatus.tone,
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
    selectSymbol: setSelectedSymbol,
    refresh,
    refreshQuotes,
    addPendingSymbol,
    applyStarterPack,
    removeSymbol
  };
}

export function buildWatchlistRows(
  symbols: SymbolRecord[],
  removing: Record<string, boolean> = {},
  quotes: Record<string, QuotesSnapshotItem> = {},
  previousMid: Record<string, number> = {},
  now = Date.now()
): WatchlistRowViewModel[] {
  return [...symbols]
    .sort((left, right) => left.symbol.localeCompare(right.symbol))
    .map((record) => {
      const isRemoving = removing[record.symbol] === true;
      const providerLabel = record.provider ?? "No provider";
      const lastEventLabel = formatRelative(record.lastEventAt);
      const eventCountLabel = formatCount(record.eventCount);
      const historyLabel = record.hasHistoricalData ? "Available" : "Missing";
      const quote = quotes[record.symbol.toUpperCase()];
      const priorMid = previousMid[record.symbol.toUpperCase()];
      const quoteAgeMs = quote ? now - new Date(quote.timestamp).getTime() : null;
      const quoteStale = quoteAgeMs !== null && quoteAgeMs > QUOTE_STALE_THRESHOLD_MS;
      const lastTone = resolveLastTone(quote, priorMid);

      return {
        symbol: record.symbol,
        status: record.status,
        statusVariant: statusVariant(record.status),
        providerLabel,
        lastEventLabel,
        eventCountLabel,
        historyLabel,
        hasHistoricalData: record.hasHistoricalData,
        bidLabel: quote ? formatPriceSize(quote.bidPrice, quote.bidSize) : "-",
        askLabel: quote ? formatPriceSize(quote.askPrice, quote.askSize) : "-",
        lastPriceLabel: quote ? formatPrice(quote.lastPrice) : "-",
        spreadLabel: quote ? formatSpread(quote.spread, quote.midPrice) : "-",
        quoteAgeLabel: quote ? formatRelative(quote.timestamp, now) : "Never",
        hasQuote: quote !== undefined,
        quoteStale,
        lastTone,
        isRemoving,
        quoteHref: `/data/quotes?symbol=${encodeURIComponent(record.symbol)}`,
        quoteAriaLabel: `View live quotes for ${record.symbol}`,
        inspectLabel: "Inspect",
        inspectAriaLabel: `Inspect ${record.symbol} watchlist detail`,
        removeLabel: isRemoving ? "Removing..." : "Remove",
        removeAriaLabel: isRemoving ? `Removing ${record.symbol} from watchlist` : `Remove ${record.symbol} from watchlist`,
        removeDisabledReason: isRemoving ? `${record.symbol} removal is already running.` : null,
        rowSelectAriaLabel: `Select ${record.symbol} watchlist row. ${record.symbol}. Status ${record.status}.`,
        ariaLabel: `${record.symbol}. Status ${record.status}. Bid ${quote ? formatPriceSize(quote.bidPrice, quote.bidSize) : "not available"}. Ask ${quote ? formatPriceSize(quote.askPrice, quote.askSize) : "not available"}. Last ${quote ? formatPrice(quote.lastPrice) : "not available"}. Provider ${providerLabel}. Last event ${lastEventLabel}. ${eventCountLabel} events. History ${historyLabel}.`
      };
    });
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

export function formatRelative(iso: string | null, now = Date.now()): string {
  if (!iso) {
    return "Never";
  }

  const timestamp = new Date(iso).getTime();
  if (Number.isNaN(timestamp)) {
    return "Never";
  }

  const diff = now - timestamp;
  if (diff < 0) {
    return new Date(iso).toLocaleString();
  }

  const seconds = Math.round(diff / 1000);
  if (seconds < 60) {
    return `${seconds}s ago`;
  }

  const minutes = Math.round(seconds / 60);
  if (minutes < 60) {
    return `${minutes}m ago`;
  }

  const hours = Math.round(minutes / 60);
  if (hours < 24) {
    return `${hours}h ago`;
  }

  const days = Math.round(hours / 24);
  return `${days}d ago`;
}

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
  requestedCount: number
): WatchlistSubmitFeedback {
  const base = `Added ${formatCount(result.added)} of ${formatCount(requestedCount)} symbol${requestedCount === 1 ? "" : "s"}`;
  const skipped = result.skipped > 0 ? `; ${formatCount(result.skipped)} skipped` : "";
  const errors = result.errors.length > 0 ? `; ${result.errors.join("; ")}` : "";

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
      providerSetupHandoff: result.errors.length > 0 ? buildProviderSetupHandoff("bulk-add-partial") : undefined
    };
  }

  return { tone: "success", message: `${base}.` };
}

export function buildStarterPackFeedback(
  label: string,
  result: { added: number; skipped: number; errors: string[] },
  requestedCount: number
): WatchlistSubmitFeedback {
  const base = `${label}: added ${formatCount(result.added)} of ${formatCount(requestedCount)} symbols`;
  const skipped = result.skipped > 0 ? `; ${formatCount(result.skipped)} skipped` : "";
  const errors = result.errors.length > 0 ? `; ${result.errors.join("; ")}` : "";

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
      providerSetupHandoff: result.errors.length > 0 ? buildProviderSetupHandoff("starter-pack-partial") : undefined
    };
  }

  return { tone: "success", message: `${base}.` };
}

export function buildProviderSetupHandoff(reason: string): WatchlistProviderSetupHandoff {
  return {
    href: "/settings#alpaca-provider-setup",
    label: "Fix provider setup",
    ariaLabel: `Open provider setup from watchlist ${reason}`,
    detail: "Review provider credentials and connection status in Settings."
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
  const displayValue = value === undefined ? "-" : formatCount(value);
  return {
    id,
    label,
    value: displayValue,
    delta: "",
    tone
  };
}

function buildListState(symbols: SymbolRecord[] | null, loadError: string | null): WatchlistListState {
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

function buildListDescription(state: WatchlistListState, rowCount: number, loadError: string | null): string {
  switch (state) {
    case "loading":
      return "Loading symbols...";
    case "error":
      return loadError ?? "Symbol watchlist failed to load.";
    case "empty":
      return "No symbols configured. Add one above to start collecting live data.";
    case "ready":
      return `${rowCount} symbol${rowCount === 1 ? "" : "s"} configured.`;
  }
}

function buildToolbarItems(
  stats: SymbolStatistics | null,
  rowCount: number,
  listState: WatchlistListState
): WatchlistScreenViewModel["toolbarItems"] {
  return [
    { id: "visible", label: "Visible", value: listState === "ready" ? formatCount(rowCount) : "-" },
    { id: "monitored", label: "Monitored", value: stats ? formatCount(stats.monitoredSymbols) : "-", active: Boolean(stats && stats.monitoredSymbols > 0) },
    { id: "errors", label: "Errors", value: stats ? formatCount(stats.symbolsWithErrors) : "-", active: Boolean(stats && stats.symbolsWithErrors > 0) },
    { id: "events", label: "24h events", value: stats ? formatCount(stats.totalEventsLast24h) : "-" }
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
  quoteError: string | null;
  quoteFetchedAt: number | null;
  now?: number;
}): { label: string; tone: WatchlistQuoteStatusTone } {
  if (listState === "empty") {
    return { label: "Live prices are idle until symbols are added.", tone: "default" };
  }

  if (quoteError) {
    return { label: `Live prices unavailable: ${quoteError}`, tone: "danger" };
  }

  if (quoteFetchedAt) {
    const updatedLabel = formatRelative(new Date(quoteFetchedAt).toISOString(), now);
    const coverageLabel = quoteCount === rowCount
      ? `Live prices for ${rowCount} symbol${rowCount === 1 ? "" : "s"}`
      : `Live prices for ${quoteCount} of ${rowCount} symbols`;
    const staleLabel = staleCount > 0 ? `; ${staleCount} stale` : "";

    return {
      label: `${coverageLabel}${staleLabel}; updated ${updatedLabel}.`,
      tone: quoteCount === rowCount && staleCount === 0 ? "default" : "warning"
    };
  }

  return { label: "Live prices waiting for first tick.", tone: "warning" };
}

export function buildQuoteRefreshCommand(
  listState: WatchlistListState,
  rowCount: number,
  refreshing: boolean
): WatchlistQuoteRefreshCommandState {
  const label = refreshing ? "Refreshing prices..." : "Refresh prices";

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

function formatCount(value: number): string {
  return value.toLocaleString();
}

function messageFromError(error: unknown, fallback: string): string {
  return error instanceof Error && error.message ? error.message : fallback;
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException
    ? error.name === "AbortError"
    : error instanceof Error && error.name === "AbortError";
}

function formatPrice(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return "-";
  }

  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 4
  });
}

function formatSize(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return "-";
  }

  return value.toLocaleString();
}

function formatPriceSize(price: number | null | undefined, size: number | null | undefined): string {
  return `${formatPrice(price)} x ${formatSize(size)}`;
}

function formatSpread(spread: number | null | undefined, mid: number | null | undefined): string {
  if (spread === null || spread === undefined || Number.isNaN(spread)) {
    return "-";
  }

  if (mid && mid > 0) {
    const basisPoints = (spread / mid) * 10_000;
    return `${spread.toFixed(2)} (${basisPoints.toFixed(1)} bps)`;
  }

  return spread.toFixed(2);
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
