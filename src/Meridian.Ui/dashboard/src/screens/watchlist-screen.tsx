<<<<<<< Updated upstream
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
=======
>>>>>>> Stashed changes
import { Link } from "react-router-dom";
import { Activity, AlertCircle, CheckCircle2, LineChart, Plus, RefreshCw, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { DenseDataTable, type DenseDataTableColumn, ToolbarStrip } from "@/components/meridian/ui-kit-primitives";
import {
  addSymbol as addSymbolApi,
<<<<<<< Updated upstream
  getLiveQuotesSnapshot,
=======
  bulkAddSymbols,
>>>>>>> Stashed changes
  getSymbols,
  getSymbolsStatistics,
  removeSymbol as removeSymbolApi
} from "@/lib/api";
<<<<<<< Updated upstream
import type { QuotesSnapshotItem, SymbolRecord, SymbolStatistics } from "@/types";

const QUOTE_POLL_INTERVAL_MS = 2000;
const QUOTE_STALE_THRESHOLD_MS = 15_000;

function formatPrice(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 });
}

function formatSize(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  return value.toLocaleString();
}

function formatSpread(spread: number | null | undefined, mid: number | null | undefined): string {
  if (spread === null || spread === undefined || Number.isNaN(spread)) return "—";
  if (mid && mid > 0) {
    const bps = (spread / mid) * 10_000;
    return `${spread.toFixed(2)} (${bps.toFixed(1)} bps)`;
  }
  return spread.toFixed(2);
}

function formatRelative(iso: string | null): string {
  if (!iso) return "Never";
  const ts = new Date(iso).getTime();
  if (Number.isNaN(ts)) return "Never";
  const diff = Date.now() - ts;
  if (diff < 0) return new Date(iso).toLocaleString();
  const seconds = Math.round(diff / 1000);
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.round(hours / 24);
  return `${days}d ago`;
}

function statusVariant(status: SymbolRecord["status"]) {
  switch (status) {
    case "Active":
      return "success" as const;
    case "Monitored":
      return "default" as const;
    case "Archived":
      return "outline" as const;
    case "Error":
      return "danger" as const;
    default:
      return "outline" as const;
  }
}

export function WatchlistScreen() {
  const [symbols, setSymbols] = useState<SymbolRecord[] | null>(null);
  const [stats, setStats] = useState<SymbolStatistics | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [pendingSymbol, setPendingSymbol] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [removing, setRemoving] = useState<Record<string, boolean>>({});
  const [quotes, setQuotes] = useState<Record<string, QuotesSnapshotItem>>({});
  const [quoteError, setQuoteError] = useState<string | null>(null);
  const [quoteFetchedAt, setQuoteFetchedAt] = useState<number | null>(null);
  const previousMidRef = useRef<Record<string, number>>({});
  const inFlightRef = useRef(false);

  const refresh = useCallback(async () => {
    setRefreshing(true);
    try {
      const [s, st] = await Promise.allSettled([getSymbols(), getSymbolsStatistics()]);
      if (s.status === "fulfilled") {
        setSymbols(s.value);
        setLoadError(null);
      } else {
        setLoadError((s.reason as Error)?.message ?? "Failed to load symbols");
      }
      if (st.status === "fulfilled") {
        setStats(st.value);
      }
    } finally {
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const fetchQuotes = useCallback(async (subscribed: readonly string[]) => {
    if (inFlightRef.current || subscribed.length === 0) return;
    inFlightRef.current = true;
    try {
      const response = await getLiveQuotesSnapshot(subscribed);
      const next: Record<string, QuotesSnapshotItem> = {};
      for (const q of response.quotes) next[q.symbol.toUpperCase()] = q;
      setQuotes((current) => {
        const prev: Record<string, number> = {};
        for (const [sym, q] of Object.entries(current)) {
          if (q.midPrice !== null && q.midPrice !== undefined) prev[sym] = q.midPrice;
        }
        previousMidRef.current = prev;
        return next;
      });
      setQuoteFetchedAt(Date.now());
      setQuoteError(null);
    } catch (err) {
      setQuoteError((err as Error)?.message ?? "Failed to load live quotes");
    } finally {
      inFlightRef.current = false;
    }
  }, []);

  const subscribedSymbols = useMemo(() => {
    if (!symbols) return [] as string[];
    return symbols.map((s) => s.symbol);
  }, [symbols]);

  useEffect(() => {
    if (subscribedSymbols.length === 0) {
      setQuotes({});
      return;
    }
    void fetchQuotes(subscribedSymbols);
    const id = window.setInterval(() => void fetchQuotes(subscribedSymbols), QUOTE_POLL_INTERVAL_MS);
    return () => window.clearInterval(id);
  }, [fetchQuotes, subscribedSymbols]);

  const handleAdd = async (event: React.FormEvent) => {
    event.preventDefault();
    const next = pendingSymbol.trim().toUpperCase();
    if (!next || submitting) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      const result = await addSymbolApi(next);
      if (!result.success) {
        setSubmitError(`Could not add ${next}.`);
        return;
      }
      setPendingSymbol("");
      await refresh();
    } catch (err) {
      setSubmitError((err as Error)?.message ?? "Failed to add symbol");
    } finally {
      setSubmitting(false);
    }
  };

  const handleRemove = async (symbol: string) => {
    setRemoving((current) => ({ ...current, [symbol]: true }));
    try {
      await removeSymbolApi(symbol);
      await refresh();
    } catch (err) {
      setLoadError((err as Error)?.message ?? `Failed to remove ${symbol}`);
    } finally {
      setRemoving((current) => {
        const { [symbol]: _ignored, ...rest } = current;
        return rest;
      });
    }
  };

  const sortedSymbols = useMemo(() => {
    if (!symbols) return null;
    return [...symbols].sort((a, b) => a.symbol.localeCompare(b.symbol));
  }, [symbols]);
=======
import { useWatchlistScreenViewModel, type WatchlistRowViewModel } from "@/screens/watchlist-screen.view-model";

export function WatchlistScreen() {
  const vm = useWatchlistScreenViewModel({
    getSymbols,
    getSymbolsStatistics,
    addSymbol: addSymbolApi,
    bulkAddSymbols,
    removeSymbol: removeSymbolApi
  });
  const FeedbackIcon = vm.submitFeedback?.tone === "success" ? CheckCircle2 : AlertCircle;
>>>>>>> Stashed changes

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <div className="eyebrow-label">Data Lane</div>
          <CardTitle className="flex items-center gap-2">
            <Activity className="h-5 w-5 text-primary" />
            Symbol watchlist
          </CardTitle>
          <CardDescription>
            Add, remove, and monitor symbols subscribed to the live data pipeline. Open a symbol to view live quotes.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            {vm.stats.map((stat) => (
              <StatCard key={stat.id} label={stat.label} value={stat.value} tone={stat.tone} ariaLabel={stat.ariaLabel} />
            ))}
          </div>

          <form
            onSubmit={(event) => void vm.addPendingSymbol(event)}
            className="mt-5 flex flex-col gap-2 sm:flex-row sm:items-center"
            aria-label={vm.formLabel}
          >
            <label htmlFor={vm.inputId} className="sr-only">Add symbol</label>
            <Input
              id={vm.inputId}
              placeholder="Add symbols (e.g. MSFT, SPY)"
              value={vm.pendingSymbol}
              onChange={(event) => vm.setPendingSymbol(event.target.value)}
              autoComplete="off"
              spellCheck={false}
              error={vm.submitFeedback?.tone === "danger"}
              disabled={vm.submitting}
              aria-describedby={vm.inputHelpId}
            />
            <Button
              type="submit"
              variant="default"
              disabled={vm.addDisabled}
              disabledReason={vm.addDisabledReason}
              busy={vm.submitting}
              busyLabel="Adding..."
              aria-label={vm.addButtonAriaLabel}
            >
              <Plus className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">{vm.addButtonLabel}</span>
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => void vm.refresh()}
              aria-label={vm.refreshButtonAriaLabel}
              disabled={vm.refreshDisabled}
              busy={vm.refreshing}
              busyLabel="Refreshing..."
            >
              <RefreshCw className={`h-4 w-4 ${vm.refreshing ? "animate-spin" : ""}`} aria-hidden="true" />
              <span className="ml-1.5">{vm.refreshButtonLabel}</span>
            </Button>
          </form>
          <p id="add-symbol-help" className="mt-2 text-xs text-muted-foreground">
            {vm.inputHelpText}
          </p>
          {vm.submitFeedback ? (
            <p
              id="add-symbol-feedback"
              role={vm.submitFeedback.tone === "success" ? "status" : "alert"}
              className={`mt-2 flex items-center gap-1.5 text-xs ${feedbackTextClass[vm.submitFeedback.tone]}`}
            >
              <FeedbackIcon className="h-3.5 w-3.5" aria-hidden="true" />
              {vm.submitFeedback.message}
            </p>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Subscribed symbols</CardTitle>
          <CardDescription>
            {vm.listDescription}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <ToolbarStrip items={vm.toolbarItems} ariaLabel="Symbol watchlist status" />
          {vm.listState === "error" ? (
            <p role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
              {vm.listDescription}
            </p>
          ) : vm.listState === "loading" ? (
            <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-4 py-3 text-sm text-muted-foreground">
              {vm.listDescription}
            </p>
          ) : (
            <>
<<<<<<< Updated upstream
              <div className="mb-3 flex items-center justify-between text-xs text-muted-foreground" data-testid="watchlist-quote-status">
                <span aria-live="polite">
                  {quoteFetchedAt
                    ? `Live prices · updated ${formatRelative(new Date(quoteFetchedAt).toISOString())}`
                    : quoteError
                      ? "Live prices unavailable"
                      : "Live prices · waiting for first tick…"}
                </span>
                {quoteError ? (
                  <span className="flex items-center gap-1 text-warning">
                    <AlertCircle className="h-3 w-3" aria-hidden="true" />
                    {quoteError}
                  </span>
                ) : null}
              </div>
              <div className="overflow-x-auto">
                <table className="min-w-full text-sm">
                  <thead>
                    <tr className="border-b border-border/60 text-left text-xs uppercase tracking-wide text-muted-foreground">
                      <th className="px-2 py-2 font-medium">Symbol</th>
                      <th className="px-2 py-2 font-medium">Status</th>
                      <th className="px-2 py-2 font-medium text-right">Bid × Size</th>
                      <th className="px-2 py-2 font-medium text-right">Ask × Size</th>
                      <th className="px-2 py-2 font-medium text-right">Last</th>
                      <th className="px-2 py-2 font-medium text-right">Spread</th>
                      <th className="px-2 py-2 font-medium">Quote age</th>
                      <th className="px-2 py-2" />
                    </tr>
                  </thead>
                  <tbody>
                    {sortedSymbols.map((row) => {
                      const isRemoving = removing[row.symbol] === true;
                      const quote = quotes[row.symbol.toUpperCase()];
                      const previousMid = previousMidRef.current[row.symbol.toUpperCase()];
                      const lastTone = quote && quote.lastPrice !== null && previousMid !== undefined
                        ? quote.lastPrice > previousMid
                          ? "text-positive"
                          : quote.lastPrice < previousMid
                            ? "text-danger"
                            : "text-foreground"
                        : "text-foreground";
                      const quoteAgeMs = quote ? Date.now() - new Date(quote.timestamp).getTime() : null;
                      const isStale = quoteAgeMs !== null && quoteAgeMs > QUOTE_STALE_THRESHOLD_MS;
                      return (
                        <tr key={row.symbol} className="border-b border-border/30">
                          <td className="px-2 py-1.5 font-mono font-semibold">{row.symbol}</td>
                          <td className="px-2 py-1.5">
                            <Badge variant={statusVariant(row.status)} dot>{row.status}</Badge>
                          </td>
                          <td className="px-2 py-1.5 text-right font-mono">
                            {quote ? (
                              <span className="text-foreground">
                                {formatPrice(quote.bidPrice)}
                                <span className="ml-1 text-xs text-muted-foreground">× {formatSize(quote.bidSize)}</span>
                              </span>
                            ) : (
                              <span className="text-muted-foreground">—</span>
                            )}
                          </td>
                          <td className="px-2 py-1.5 text-right font-mono">
                            {quote ? (
                              <span className="text-foreground">
                                {formatPrice(quote.askPrice)}
                                <span className="ml-1 text-xs text-muted-foreground">× {formatSize(quote.askSize)}</span>
                              </span>
                            ) : (
                              <span className="text-muted-foreground">—</span>
                            )}
                          </td>
                          <td className={`px-2 py-1.5 text-right font-mono ${lastTone}`}>
                            {quote ? formatPrice(quote.lastPrice) : <span className="text-muted-foreground">—</span>}
                          </td>
                          <td className="px-2 py-1.5 text-right font-mono text-muted-foreground">
                            {quote ? formatSpread(quote.spread, quote.midPrice) : "—"}
                          </td>
                          <td className={`px-2 py-1.5 ${isStale ? "text-warning" : "text-muted-foreground"}`}>
                            {quote ? formatRelative(quote.timestamp) : "Never"}
                          </td>
                          <td className="px-2 py-1.5 text-right">
                            <div className="flex justify-end gap-1.5">
                              <Button asChild variant="outline" size="sm">
                                <Link
                                  to={`/data/quotes?symbol=${encodeURIComponent(row.symbol)}`}
                                  aria-label={`View live quotes for ${row.symbol}`}
                                >
                                  <LineChart className="h-3.5 w-3.5" aria-hidden="true" />
                                  <span className="ml-1">Quote</span>
                                </Link>
                              </Button>
                              <Button
                                type="button"
                                variant="outline"
                                size="sm"
                                disabled={isRemoving}
                                onClick={() => void handleRemove(row.symbol)}
                                aria-label={`Remove ${row.symbol} from watchlist`}
                              >
                                <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                                <span className="ml-1">{isRemoving ? "Removing…" : "Remove"}</span>
                              </Button>
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
=======
              {vm.loadError ? (
                <p role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {vm.loadError}
                </p>
              ) : null}
              <DenseDataTable
                columns={buildColumns(vm.removeSymbol)}
                rows={vm.rows}
                getRowId={(row) => row.symbol}
                getRowAriaLabel={(row) => row.ariaLabel}
                emptyText={vm.listDescription}
                ariaLabel={vm.tableLabel}
                caption={vm.tableCaption}
              />
>>>>>>> Stashed changes
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

const feedbackTextClass = {
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
} as const;

function buildColumns(removeSymbol: (symbol: string) => Promise<void>): DenseDataTableColumn<WatchlistRowViewModel>[] {
  return [
    {
      id: "symbol",
      label: "Symbol",
      className: "font-mono font-semibold text-foreground",
      render: (row) => row.symbol
    },
    {
      id: "status",
      label: "Status",
      render: (row) => <Badge variant={row.statusVariant} dot>{row.status}</Badge>
    },
    {
      id: "provider",
      label: "Provider",
      className: "text-muted-foreground",
      render: (row) => row.providerLabel
    },
    {
      id: "last-event",
      label: "Last event",
      className: "text-muted-foreground",
      render: (row) => row.lastEventLabel
    },
    {
      id: "events",
      label: "Events",
      align: "right",
      className: "font-mono",
      render: (row) => row.eventCountLabel
    },
    {
      id: "history",
      label: "History",
      className: "text-muted-foreground",
      render: (row) => row.hasHistoricalData ? <span className="text-success">Available</span> : row.historyLabel
    },
    {
      id: "actions",
      label: "Actions",
      align: "right",
      render: (row) => (
        <div className="flex justify-end gap-1.5">
          <Button asChild variant="outline" size="sm">
            <Link to={row.quoteHref} aria-label={row.quoteAriaLabel}>
              <LineChart className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="ml-1">Quote</span>
            </Link>
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={row.isRemoving}
            disabledReason={row.removeDisabledReason}
            onClick={() => void removeSymbol(row.symbol)}
            aria-label={row.removeAriaLabel}
          >
            <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
            <span className="ml-1">{row.removeLabel}</span>
          </Button>
        </div>
      )
    }
  ];
}

function StatCard({ label, value, tone = "default", ariaLabel }: { label: string; value: string; tone?: "default" | "danger"; ariaLabel: string }) {
  const toneClass = tone === "danger" ? "text-danger" : "text-foreground";
  return (
    <div className="rounded-md border border-border/60 bg-secondary/25 px-3 py-3" role="group" aria-label={ariaLabel}>
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className={`mt-1 font-mono text-2xl ${toneClass}`}>
        {value}
      </div>
    </div>
  );
}
