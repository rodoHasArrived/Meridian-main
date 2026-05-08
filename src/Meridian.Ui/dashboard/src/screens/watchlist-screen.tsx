import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Activity, AlertCircle, LineChart, Plus, RefreshCw, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import {
  addSymbol as addSymbolApi,
  getSymbols,
  getSymbolsStatistics,
  removeSymbol as removeSymbolApi
} from "@/lib/api";
import type { SymbolRecord, SymbolStatistics } from "@/types";

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
            <div className="overflow-x-auto">
              <table className="min-w-full text-sm">
                <thead>
                  <tr className="border-b border-border/60 text-left text-xs uppercase tracking-wide text-muted-foreground">
                    <th className="px-2 py-2 font-medium">Symbol</th>
                    <th className="px-2 py-2 font-medium">Status</th>
                    <th className="px-2 py-2 font-medium">Provider</th>
                    <th className="px-2 py-2 font-medium">Last event</th>
                    <th className="px-2 py-2 font-medium text-right">Events</th>
                    <th className="px-2 py-2 font-medium">History</th>
                    <th className="px-2 py-2" />
                  </tr>
                </thead>
                <tbody>
                  {sortedSymbols.map((row) => {
                    const isRemoving = removing[row.symbol] === true;
                    return (
                      <tr key={row.symbol} className="border-b border-border/30">
                        <td className="px-2 py-1.5 font-mono font-semibold">{row.symbol}</td>
                        <td className="px-2 py-1.5">
                          <Badge variant={statusVariant(row.status)} dot>{row.status}</Badge>
                        </td>
                        <td className="px-2 py-1.5 text-muted-foreground">{row.provider ?? "—"}</td>
                        <td className="px-2 py-1.5 text-muted-foreground">{formatRelative(row.lastEventAt)}</td>
                        <td className="px-2 py-1.5 text-right font-mono">{row.eventCount.toLocaleString()}</td>
                        <td className="px-2 py-1.5 text-muted-foreground">
                          {row.hasHistoricalData ? <span className="text-positive">Yes</span> : "—"}
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
