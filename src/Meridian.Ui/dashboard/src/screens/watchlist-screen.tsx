import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Activity, AlertCircle, LineChart, Plus, RefreshCw, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import {
  addSymbol as addSymbolApi,
  getLiveQuotesSnapshot,
  getSymbols,
  getSymbolsStatistics,
  removeSymbol as removeSymbolApi
} from "@/lib/api";
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

function formatChange(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return "—";
  const sign = value > 0 ? "+" : value < 0 ? "" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 })}`;
}

function formatChangePercent(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return "—";
  const sign = value > 0 ? "+" : value < 0 ? "" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%`;
}

function changeTone(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value) || value === 0) return "text-muted-foreground";
  return value > 0 ? "text-positive" : "text-danger";
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
            Add, remove, and monitor symbols subscribed to the live data pipeline. Click a symbol to view live quotes.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <StatCard label="Total" value={stats?.totalSymbols} />
            <StatCard label="Monitored" value={stats?.monitoredSymbols} />
            <StatCard label="Archived" value={stats?.archivedSymbols} />
            <StatCard label="Errors" value={stats?.symbolsWithErrors} tone={stats && stats.symbolsWithErrors > 0 ? "danger" : "default"} />
          </div>

          <form onSubmit={handleAdd} className="mt-5 flex flex-col gap-2 sm:flex-row sm:items-center">
            <label htmlFor="add-symbol-input" className="sr-only">Add symbol</label>
            <Input
              id="add-symbol-input"
              placeholder="Add a symbol (e.g. MSFT)"
              value={pendingSymbol}
              onChange={(event) => setPendingSymbol(event.target.value)}
              autoComplete="off"
              spellCheck={false}
              error={submitError !== null}
              aria-describedby={submitError ? "add-symbol-error" : undefined}
            />
            <Button type="submit" variant="default" disabled={submitting || pendingSymbol.trim().length === 0}>
              <Plus className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">{submitting ? "Adding…" : "Add"}</span>
            </Button>
            <Button type="button" variant="outline" size="sm" onClick={() => void refresh()} aria-label="Refresh watchlist">
              <RefreshCw className={`h-4 w-4 ${refreshing ? "animate-spin" : ""}`} aria-hidden="true" />
              <span className="ml-1.5">Refresh</span>
            </Button>
          </form>
          {submitError ? (
            <p id="add-symbol-error" className="mt-2 flex items-center gap-1.5 text-xs text-danger">
              <AlertCircle className="h-3.5 w-3.5" aria-hidden="true" />
              {submitError}
            </p>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Subscribed symbols</CardTitle>
          <CardDescription>
            {sortedSymbols ? `${sortedSymbols.length} symbol${sortedSymbols.length === 1 ? "" : "s"} configured.` : "Loading…"}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {loadError && !sortedSymbols ? (
            <p className="text-sm text-danger">{loadError}</p>
          ) : !sortedSymbols ? (
            <p className="text-sm text-muted-foreground">Loading symbols…</p>
          ) : sortedSymbols.length === 0 ? (
            <p className="text-sm text-muted-foreground">No symbols configured. Add one above to start collecting live data.</p>
          ) : (
            <>
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
                      <th className="px-2 py-2 font-medium text-right">Chg</th>
                      <th className="px-2 py-2 font-medium text-right">Chg %</th>
                      <th className="px-2 py-2 font-medium text-right">Day H / L</th>
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
                          <td className={`px-2 py-1.5 text-right font-mono ${changeTone(quote?.session?.change ?? null)}`}>
                            {quote?.session ? formatChange(quote.session.change) : "—"}
                          </td>
                          <td className={`px-2 py-1.5 text-right font-mono ${changeTone(quote?.session?.changePercent ?? null)}`}>
                            {quote?.session ? formatChangePercent(quote.session.changePercent) : "—"}
                          </td>
                          <td className="px-2 py-1.5 text-right font-mono text-muted-foreground">
                            {quote?.session
                              ? `${formatPrice(quote.session.high)} / ${formatPrice(quote.session.low)}`
                              : "—"}
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
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function StatCard({ label, value, tone = "default" }: { label: string; value: number | undefined; tone?: "default" | "danger" }) {
  const toneClass = tone === "danger" ? "text-danger" : "text-foreground";
  return (
    <div className="rounded-md border border-border/60 bg-secondary/25 px-3 py-3">
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className={`mt-1 font-mono text-2xl ${toneClass}`}>
        {value === undefined ? "—" : value.toLocaleString()}
      </div>
    </div>
  );
}
