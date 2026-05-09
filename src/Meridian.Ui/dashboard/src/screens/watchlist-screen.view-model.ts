import { useCallback, useEffect, useMemo, useState } from "react";
import type { SymbolRecord, SymbolStatistics } from "@/types";

export type WatchlistBadgeVariant = "default" | "outline" | "success" | "warning" | "danger" | "paper" | "live" | "research";
export type WatchlistStatTone = "default" | "danger";
export type WatchlistListState = "loading" | "error" | "empty" | "ready";

export interface WatchlistApi {
  getSymbols: () => Promise<SymbolRecord[]>;
  getSymbolsStatistics: () => Promise<SymbolStatistics>;
  addSymbol: (symbol: string) => Promise<{ success: boolean; symbol: string }>;
  bulkAddSymbols: (symbols: string[]) => Promise<{ added: number; skipped: number; errors: string[] }>;
  removeSymbol: (symbol: string) => Promise<{ success: boolean; symbol: string }>;
}

export interface WatchlistSubmitFeedback {
  tone: "success" | "warning" | "danger";
  message: string;
}

export interface WatchlistStatCard {
  id: string;
  label: string;
  value: string;
  tone: WatchlistStatTone;
  ariaLabel: string;
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
  isRemoving: boolean;
  quoteHref: string;
  quoteAriaLabel: string;
  removeLabel: string;
  removeAriaLabel: string;
  removeDisabledReason: string | null;
  ariaLabel: string;
}

export interface WatchlistScreenViewModel {
  pendingSymbol: string;
  setPendingSymbol: (value: string) => void;
  refreshing: boolean;
  submitting: boolean;
  submitError: string | null;
  submitFeedback: WatchlistSubmitFeedback | null;
  loadError: string | null;
  stats: WatchlistStatCard[];
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
  refresh: () => Promise<void>;
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

  const addValidation = validatePendingSymbol(pendingSymbol);
  const pendingSymbols = parseWatchlistSymbols(pendingSymbol);
  const submitError = submitFeedback?.tone === "danger" ? submitFeedback.message : null;
  const rows = useMemo(() => buildWatchlistRows(symbols ?? [], removing), [symbols, removing]);
  const listState = buildListState(symbols, loadError);

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
    tableCaption: "Subscribed symbols sorted alphabetically with status, provider, latest event, collection counts, and actions.",
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
    refresh,
    addPendingSymbol,
    removeSymbol
  };
}

export function buildWatchlistRows(
  symbols: SymbolRecord[],
  removing: Record<string, boolean> = {}
): WatchlistRowViewModel[] {
  return [...symbols]
    .sort((left, right) => left.symbol.localeCompare(right.symbol))
    .map((record) => {
      const isRemoving = removing[record.symbol] === true;
      const providerLabel = record.provider ?? "No provider";
      const lastEventLabel = formatRelative(record.lastEventAt);
      const eventCountLabel = formatCount(record.eventCount);
      const historyLabel = record.hasHistoricalData ? "Available" : "Missing";

      return {
        symbol: record.symbol,
        status: record.status,
        statusVariant: statusVariant(record.status),
        providerLabel,
        lastEventLabel,
        eventCountLabel,
        historyLabel,
        hasHistoricalData: record.hasHistoricalData,
        isRemoving,
        quoteHref: `/data/quotes?symbol=${encodeURIComponent(record.symbol)}`,
        quoteAriaLabel: `View live quotes for ${record.symbol}`,
        removeLabel: isRemoving ? "Removing..." : "Remove",
        removeAriaLabel: isRemoving ? `Removing ${record.symbol} from watchlist` : `Remove ${record.symbol} from watchlist`,
        removeDisabledReason: isRemoving ? `${record.symbol} removal is already running.` : null,
        ariaLabel: `${record.symbol}. Status ${record.status}. Provider ${providerLabel}. Last event ${lastEventLabel}. ${eventCountLabel} events. History ${historyLabel}.`
      };
    });
}

export function buildWatchlistStats(stats: SymbolStatistics | null): WatchlistStatCard[] {
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
  tone: WatchlistStatTone = "default"
): WatchlistStatCard {
  const displayValue = value === undefined ? "-" : formatCount(value);
  return {
    id,
    label,
    value: displayValue,
    tone,
    ariaLabel: `${label}: ${displayValue}`
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
