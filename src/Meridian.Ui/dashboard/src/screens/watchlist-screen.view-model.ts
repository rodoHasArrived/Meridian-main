import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { MetricSnapshot, QuotesSnapshotItem, SymbolRecord, SymbolStatistics } from "@/types";

export type WatchlistBadgeVariant = "default" | "outline" | "success" | "warning" | "danger" | "paper" | "live" | "research";
export type WatchlistListState = "loading" | "error" | "empty" | "ready";
export type WatchlistPriceTone = "default" | "success" | "danger";
export type WatchlistQuoteStatusTone = "default" | "warning" | "danger";

const QUOTE_POLL_INTERVAL_MS = 2000;
const QUOTE_STALE_THRESHOLD_MS = 15_000;

export interface WatchlistApi {
  getSymbols: () => Promise<SymbolRecord[]>;
  getSymbolsStatistics: () => Promise<SymbolStatistics>;
  getLiveQuotesSnapshot: (symbols?: readonly string[]) => Promise<{ quotes: QuotesSnapshotItem[] }>;
  addSymbol: (symbol: string) => Promise<{ success: boolean; symbol: string }>;
  bulkAddSymbols: (symbols: string[]) => Promise<{ added: number; skipped: number; errors: string[] }>;
  removeSymbol: (symbol: string) => Promise<{ success: boolean; symbol: string }>;
}

export interface WatchlistSubmitFeedback {
  tone: "success" | "warning" | "danger";
  message: string;
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
  removeLabel: string;
  removeAriaLabel: string;
  removeDisabledReason: string | null;
  ariaLabel: string;
}

export interface WatchlistQuoteRefreshCommandState {
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
  loadError: string | null;
  stats: MetricSnapshot[];
  rows: WatchlistRowViewModel[];
  listState: WatchlistListState;
  listDescription: string;
  tableLabel: string;
  tableCaption: string;
  formLabel: string;
  inputId: string;
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
  quoteRefreshCommand: WatchlistQuoteRefreshCommandState;
  refresh: () => Promise<void>;
  refreshQuotes: () => Promise<void>;
  addPendingSymbol: (event?: { preventDefault: () => void }) => Promise<void>;
  removeSymbol: (symbol: string) => Promise<void>;
}

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
  const previousMidRef = useRef<Record<string, number>>({});
  const quoteInFlightRef = useRef(false);

  const refresh = useCallback(async () => {
    setRefreshing(true);
    try {
      const [symbolResult, statsResult] = await Promise.allSettled([
        api.getSymbols(),
        api.getSymbolsStatistics()
      ]);

      if (symbolResult.status === "fulfilled") {
        setSymbols(symbolResult.value);
        setLoadError(null);
      } else {
        setLoadError(messageFromError(symbolResult.reason, "Failed to load symbols"));
      }

      if (statsResult.status === "fulfilled") {
        setStats(statsResult.value);
      }
    } finally {
      setRefreshing(false);
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
        if (!result.success) {
          setSubmitFeedback({ tone: "danger", message: `Could not add ${next}.` });
          return;
        }

        setSubmitFeedback({ tone: "success", message: `Added ${next} to the watchlist.` });
        setPendingSymbol("");
        await refresh();
      } else {
        const result = await api.bulkAddSymbols(nextSymbols);
        setSubmitFeedback(buildBulkAddFeedback(result, nextSymbols.length));
        if (result.added > 0 || result.errors.length === 0) {
          setPendingSymbol("");
        }

        if (result.added > 0) {
          await refresh();
        }
      }
    } catch (error) {
      setSubmitFeedback({ tone: "danger", message: messageFromError(error, "Failed to add symbol") });
    } finally {
      setSubmitting(false);
    }
  }, [api.addSymbol, api.bulkAddSymbols, pendingSymbol, refresh, submitting]);

  const removeSymbol = useCallback(async (symbol: string) => {
    setRemoving((current) => ({ ...current, [symbol]: true }));
    try {
      await api.removeSymbol(symbol);
      await refresh();
    } catch (error) {
      setLoadError(messageFromError(error, `Failed to remove ${symbol}`));
    } finally {
      setRemoving((current) => {
        const { [symbol]: _removed, ...rest } = current;
        return rest;
      });
    }
  }, [api.removeSymbol, refresh]);

  const subscribedSymbols = useMemo(() => symbols?.map((symbol) => symbol.symbol) ?? [], [symbols]);

  const fetchQuotes = useCallback(async (currentSymbols: readonly string[]) => {
    if (quoteInFlightRef.current || currentSymbols.length === 0) {
      return;
    }

    quoteInFlightRef.current = true;
    try {
      const response = await api.getLiveQuotesSnapshot(currentSymbols);
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
      setQuoteError(messageFromError(error, "Failed to load live quotes"));
    } finally {
      quoteInFlightRef.current = false;
    }
  }, [api.getLiveQuotesSnapshot]);

  useEffect(() => {
    if (subscribedSymbols.length === 0) {
      setQuotes({});
      setQuoteError(null);
      setQuoteFetchedAt(null);
      previousMidRef.current = {};
      return;
    }

    void fetchQuotes(subscribedSymbols);
    const interval = window.setInterval(() => void fetchQuotes(subscribedSymbols), QUOTE_POLL_INTERVAL_MS);
    return () => window.clearInterval(interval);
  }, [fetchQuotes, subscribedSymbols]);

  const refreshQuotes = useCallback(async () => {
    if (subscribedSymbols.length === 0 || quoteRefreshing) {
      return;
    }

    setQuoteRefreshing(true);
    try {
      await fetchQuotes(subscribedSymbols);
    } finally {
      setQuoteRefreshing(false);
    }
  }, [fetchQuotes, quoteRefreshing, subscribedSymbols]);

  const addValidation = validatePendingSymbol(pendingSymbol);
  const pendingSymbols = parseWatchlistSymbols(pendingSymbol);
  const submitError = submitFeedback?.tone === "danger" ? submitFeedback.message : null;
  const rows = useMemo(
    () => buildWatchlistRows(symbols ?? [], removing, quotes, previousMidRef.current),
    [symbols, removing, quotes]
  );
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
    quoteRefreshCommand: buildQuoteRefreshCommand(listState, rows.length, quoteRefreshing),
    refresh,
    refreshQuotes,
    addPendingSymbol,
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
        removeLabel: isRemoving ? "Removing..." : "Remove",
        removeAriaLabel: isRemoving ? `Removing ${record.symbol} from watchlist` : `Remove ${record.symbol} from watchlist`,
        removeDisabledReason: isRemoving ? `${record.symbol} removal is already running.` : null,
        ariaLabel: `${record.symbol}. Status ${record.status}. Bid ${quote ? formatPriceSize(quote.bidPrice, quote.bidSize) : "not available"}. Ask ${quote ? formatPriceSize(quote.askPrice, quote.askSize) : "not available"}. Last ${quote ? formatPrice(quote.lastPrice) : "not available"}. Provider ${providerLabel}. Last event ${lastEventLabel}. ${eventCountLabel} events. History ${historyLabel}.`
      };
    });
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

export function buildBulkAddFeedback(
  result: { added: number; skipped: number; errors: string[] },
  requestedCount: number
): WatchlistSubmitFeedback {
  const base = `Added ${formatCount(result.added)} of ${formatCount(requestedCount)} symbol${requestedCount === 1 ? "" : "s"}`;
  const skipped = result.skipped > 0 ? `; ${formatCount(result.skipped)} skipped` : "";
  const errors = result.errors.length > 0 ? `; ${result.errors.join("; ")}` : "";

  if (result.errors.length > 0 && result.added === 0) {
    return { tone: "danger", message: `${base}${skipped}${errors}.` };
  }

  if (result.errors.length > 0 || result.skipped > 0) {
    return { tone: "warning", message: `${base}${skipped}${errors}.` };
  }

  return { tone: "success", message: `${base}.` };
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
