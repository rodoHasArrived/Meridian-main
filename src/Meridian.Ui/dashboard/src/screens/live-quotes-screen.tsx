import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ArrowDown, ArrowUp, LineChart, RefreshCw, Search } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { getLiveOrderbook, getLiveQuote, getLiveTrades } from "@/lib/api";
import type {
  OrderBookResponse,
  QuotesResponse,
  TradeDataResponse,
  TradesResponse
} from "@/types";

const POLL_INTERVAL_MS = 2000;

interface LoadState<T> {
  data: T | null;
  error: string | null;
}

function formatPrice(value: number | null | undefined, fractionDigits = 4): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return "—";
  }
  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: fractionDigits
  });
}

function formatSize(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return "—";
  }
  return value.toLocaleString();
}

function formatTimestamp(iso: string | null | undefined): string {
  if (!iso) return "—";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "—";
  return date.toLocaleTimeString(undefined, { hour12: false }) + "." + String(date.getMilliseconds()).padStart(3, "0");
}

export function LiveQuotesScreen() {
  const [symbolInput, setSymbolInput] = useState("");
  const [activeSymbol, setActiveSymbol] = useState<string | null>(null);
  const [quote, setQuote] = useState<LoadState<QuotesResponse>>({ data: null, error: null });
  const [trades, setTrades] = useState<LoadState<TradesResponse>>({ data: null, error: null });
  const [orderbook, setOrderbook] = useState<LoadState<OrderBookResponse>>({ data: null, error: null });
  const [refreshing, setRefreshing] = useState(false);
  const inFlightRef = useRef(false);

  const fetchAll = useCallback(async (symbol: string) => {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    setRefreshing(true);
    try {
      const [q, t, ob] = await Promise.allSettled([
        getLiveQuote(symbol),
        getLiveTrades(symbol, 25),
        getLiveOrderbook(symbol, 10)
      ]);
      setQuote(q.status === "fulfilled"
        ? { data: q.value, error: null }
        : { data: null, error: (q.reason as Error)?.message ?? "Failed to load quote" });
      setTrades(t.status === "fulfilled"
        ? { data: t.value, error: null }
        : { data: null, error: (t.reason as Error)?.message ?? "Failed to load trades" });
      setOrderbook(ob.status === "fulfilled"
        ? { data: ob.value, error: null }
        : { data: null, error: (ob.reason as Error)?.message ?? "Failed to load order book" });
    } finally {
      inFlightRef.current = false;
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    if (!activeSymbol) return;
    void fetchAll(activeSymbol);
    const interval = window.setInterval(() => void fetchAll(activeSymbol), POLL_INTERVAL_MS);
    return () => window.clearInterval(interval);
  }, [activeSymbol, fetchAll]);

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault();
    const next = symbolInput.trim().toUpperCase();
    if (!next) return;
    setQuote({ data: null, error: null });
    setTrades({ data: null, error: null });
    setOrderbook({ data: null, error: null });
    setActiveSymbol(next);
  };

  const quoteRow = quote.data?.quote;
  const stale = orderbook.data?.isStale === true;
  const venueLabel = quoteRow?.venue ?? orderbook.data?.venue ?? null;

  const tradeRows = useMemo(() => trades.data?.trades?.slice(0, 25) ?? [], [trades.data]);

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <div className="eyebrow-label">Data Lane</div>
          <CardTitle className="flex items-center gap-2">
            <LineChart className="h-5 w-5 text-primary" />
            Live quotes & order book
          </CardTitle>
          <CardDescription>
            Look up live bid/ask, recent trades, and L2 depth for any subscribed symbol. Refreshes every {POLL_INTERVAL_MS / 1000}s.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit} className="flex flex-col gap-3 sm:flex-row sm:items-center">
            <label htmlFor="live-quote-symbol" className="sr-only">Symbol</label>
            <Input
              id="live-quote-symbol"
              placeholder="Enter a symbol (e.g. AAPL)"
              value={symbolInput}
              onChange={(event) => setSymbolInput(event.target.value)}
              leadingIcon={<Search className="h-4 w-4" />}
              autoComplete="off"
              spellCheck={false}
            />
            <div className="flex items-center gap-2">
              <Button type="submit" variant="default">View quote</Button>
              {activeSymbol ? (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => void fetchAll(activeSymbol)}
                  aria-label="Refresh live data now"
                >
                  <RefreshCw className={`h-4 w-4 ${refreshing ? "animate-spin" : ""}`} aria-hidden="true" />
                  <span className="ml-1.5">Refresh</span>
                </Button>
              ) : null}
            </div>
          </form>
          {activeSymbol ? (
            <div className="mt-4 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <span className="font-semibold text-foreground">{activeSymbol}</span>
              {venueLabel ? <Badge variant="outline">{venueLabel}</Badge> : null}
              {stale ? <Badge variant="warning">Stale</Badge> : null}
              <span>Last update {formatTimestamp(quoteRow?.timestamp ?? orderbook.data?.timestamp ?? null)}</span>
            </div>
          ) : null}
        </CardContent>
      </Card>

      {!activeSymbol ? (
        <Card>
          <CardContent className="py-10 text-center text-sm text-muted-foreground">
            Enter a symbol above to see live BBO, recent trades, and L2 depth.
          </CardContent>
        </Card>
      ) : (
        <div className="grid gap-4 lg:grid-cols-[1fr_1fr]">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Best bid / offer</CardTitle>
              <CardDescription>Top-of-book quote and spread</CardDescription>
            </CardHeader>
            <CardContent>
              {quote.error && !quote.data ? (
                <p className="text-sm text-danger">{quote.error}</p>
              ) : !quoteRow ? (
                <p className="text-sm text-muted-foreground">No quote data available for {activeSymbol}.</p>
              ) : (
                <div className="grid gap-4 sm:grid-cols-2">
                  <BboPanel
                    label="Bid"
                    price={quoteRow.bidPrice}
                    size={quoteRow.bidSize}
                    tone="positive"
                    icon={<ArrowDown className="h-4 w-4" aria-hidden="true" />}
                  />
                  <BboPanel
                    label="Ask"
                    price={quoteRow.askPrice}
                    size={quoteRow.askSize}
                    tone="negative"
                    icon={<ArrowUp className="h-4 w-4" aria-hidden="true" />}
                  />
                  <MetricRow label="Mid" value={formatPrice(quoteRow.midPrice)} />
                  <MetricRow label="Spread" value={formatPrice(quoteRow.spread)} />
                  <MetricRow label="Sequence" value={quoteRow.sequenceNumber.toLocaleString()} />
                  <MetricRow label="Stream" value={quoteRow.streamId ?? "—"} />
                </div>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-base">Order book (L2)</CardTitle>
              <CardDescription>Top {orderbook.data?.bids.length ?? 0} bids / {orderbook.data?.asks.length ?? 0} asks</CardDescription>
            </CardHeader>
            <CardContent>
              {orderbook.error && !orderbook.data ? (
                <p className="text-sm text-danger">{orderbook.error}</p>
              ) : !orderbook.data || (orderbook.data.bids.length === 0 && orderbook.data.asks.length === 0) ? (
                <p className="text-sm text-muted-foreground">No depth data available for {activeSymbol}.</p>
              ) : (
                <DepthLadder data={orderbook.data} />
              )}
            </CardContent>
          </Card>

          <Card className="lg:col-span-2">
            <CardHeader>
              <CardTitle className="text-base">Recent trades</CardTitle>
              <CardDescription>Last {tradeRows.length} prints</CardDescription>
            </CardHeader>
            <CardContent>
              {trades.error && !trades.data ? (
                <p className="text-sm text-danger">{trades.error}</p>
              ) : tradeRows.length === 0 ? (
                <p className="text-sm text-muted-foreground">No recent trades for {activeSymbol}.</p>
              ) : (
                <TradesTable trades={tradeRows} />
              )}
            </CardContent>
          </Card>
        </div>
      )}
    </div>
  );
}

function MetricRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between border-b border-border/40 py-2 text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground">{value}</span>
    </div>
  );
}

interface BboPanelProps {
  label: string;
  price: number;
  size: number;
  tone: "positive" | "negative";
  icon: React.ReactNode;
}

function BboPanel({ label, price, size, tone, icon }: BboPanelProps) {
  const toneClass = tone === "positive"
    ? "border-positive/30 bg-positive/5 text-positive"
    : "border-danger/30 bg-danger/5 text-danger";
  return (
    <div className={`rounded-md border px-3 py-3 ${toneClass}`}>
      <div className="flex items-center gap-1.5 text-xs uppercase tracking-wide">
        {icon}
        <span>{label}</span>
      </div>
      <div className="mt-2 font-mono text-2xl text-foreground">{formatPrice(price)}</div>
      <div className="mt-1 text-xs text-muted-foreground">{formatSize(size)} shares</div>
    </div>
  );
}

function DepthLadder({ data }: { data: OrderBookResponse }) {
  const maxSize = Math.max(
    1,
    ...data.bids.map((l) => l.size),
    ...data.asks.map((l) => l.size)
  );
  return (
    <div className="grid grid-cols-2 gap-2 font-mono text-xs">
      <div>
        <div className="mb-1 flex justify-between text-muted-foreground">
          <span>Bid size</span>
          <span>Price</span>
        </div>
        {data.bids.map((level) => (
          <div key={`bid-${level.level}`} className="relative flex justify-between rounded-sm px-2 py-1">
            <span
              aria-hidden="true"
              className="absolute inset-y-0 right-0 rounded-sm bg-positive/15"
              style={{ width: `${(level.size / maxSize) * 100}%` }}
            />
            <span className="relative">{formatSize(level.size)}</span>
            <span className="relative text-positive">{formatPrice(level.price)}</span>
          </div>
        ))}
      </div>
      <div>
        <div className="mb-1 flex justify-between text-muted-foreground">
          <span>Price</span>
          <span>Ask size</span>
        </div>
        {data.asks.map((level) => (
          <div key={`ask-${level.level}`} className="relative flex justify-between rounded-sm px-2 py-1">
            <span
              aria-hidden="true"
              className="absolute inset-y-0 left-0 rounded-sm bg-danger/15"
              style={{ width: `${(level.size / maxSize) * 100}%` }}
            />
            <span className="relative text-danger">{formatPrice(level.price)}</span>
            <span className="relative">{formatSize(level.size)}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function TradesTable({ trades }: { trades: TradeDataResponse[] }) {
  return (
    <div className="overflow-x-auto">
      <table className="min-w-full text-sm">
        <thead>
          <tr className="border-b border-border/60 text-left text-xs uppercase tracking-wide text-muted-foreground">
            <th className="px-2 py-2 font-medium">Time</th>
            <th className="px-2 py-2 font-medium text-right">Price</th>
            <th className="px-2 py-2 font-medium text-right">Size</th>
            <th className="px-2 py-2 font-medium">Aggressor</th>
            <th className="px-2 py-2 font-medium">Venue</th>
          </tr>
        </thead>
        <tbody className="font-mono">
          {trades.map((t) => {
            const aggressor = t.aggressor?.toLowerCase();
            const aggressorClass = aggressor === "buy"
              ? "text-positive"
              : aggressor === "sell"
                ? "text-danger"
                : "text-muted-foreground";
            return (
              <tr key={`${t.sequenceNumber}-${t.timestamp}`} className="border-b border-border/30">
                <td className="px-2 py-1.5">{formatTimestamp(t.timestamp)}</td>
                <td className="px-2 py-1.5 text-right">{formatPrice(t.price)}</td>
                <td className="px-2 py-1.5 text-right">{formatSize(t.size)}</td>
                <td className={`px-2 py-1.5 ${aggressorClass}`}>{t.aggressor}</td>
                <td className="px-2 py-1.5 text-muted-foreground">{t.venue ?? "—"}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
