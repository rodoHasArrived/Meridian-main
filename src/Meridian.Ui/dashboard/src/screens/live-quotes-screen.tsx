import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import {
  AlertCircle,
  ArrowDown,
  ArrowUp,
  CheckCircle2,
  ListPlus,
  LineChart,
  RefreshCw,
  Search,
  Send,
  TrendingUp
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { HistoricalChartCard } from "@/components/meridian/historical-chart";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { getLiveOrderbook, getLiveQuote, getLiveTrades, submitOrder } from "@/lib/api";
import {
  buildLiveQuotesMarketViewModel,
  computeIntradayMetrics,
  useQuickTradeTicket,
  type IntradayMetrics,
  type LiveQuotesPanelState,
  type QuickTradeTicketViewModel
} from "@/screens/live-quotes-screen.view-model";
import type {
  OrderBookResponse,
  QuotesResponse,
  TradeDataResponse,
  TradesResponse
} from "@/types";

export { computeIntradayMetrics };

const POLL_INTERVAL_MS = 2000;
const TRADE_HISTORY_LIMIT = 200;
const TRADE_TABLE_LIMIT = 25;

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

function mergeLoadState<T>(
  result: PromiseSettledResult<T>,
  current: LoadState<T>,
  fallbackMessage: string
): LoadState<T> {
  if (result.status === "fulfilled") {
    return { data: result.value, error: null };
  }

  const message = result.reason instanceof Error && result.reason.message
    ? result.reason.message
    : fallbackMessage;
  return { data: current.data, error: message };
}

export function LiveQuotesScreen() {
  const [searchParams, setSearchParams] = useSearchParams();
  const initialSymbol = (searchParams.get("symbol") ?? "").trim().toUpperCase();
  const [symbolInput, setSymbolInput] = useState(initialSymbol);
  const [activeSymbol, setActiveSymbol] = useState<string | null>(initialSymbol || null);
  const [quote, setQuote] = useState<LoadState<QuotesResponse>>({ data: null, error: null });
  const [trades, setTrades] = useState<LoadState<TradesResponse>>({ data: null, error: null });
  const [orderbook, setOrderbook] = useState<LoadState<OrderBookResponse>>({ data: null, error: null });
  const [refreshing, setRefreshing] = useState(false);
  const requestIdRef = useRef(0);
  const inFlightSymbolRef = useRef<string | null>(null);
  const quickTrade = useQuickTradeTicket(activeSymbol, { submitOrder });

  const fetchAll = useCallback(async (symbol: string) => {
    const requestedSymbol = symbol.trim().toUpperCase();
    if (!requestedSymbol || inFlightSymbolRef.current === requestedSymbol) return;

    const requestId = requestIdRef.current + 1;
    requestIdRef.current = requestId;
    inFlightSymbolRef.current = requestedSymbol;
    setRefreshing(true);
    try {
      const [q, t, ob] = await Promise.allSettled([
        getLiveQuote(requestedSymbol),
        getLiveTrades(requestedSymbol, TRADE_HISTORY_LIMIT),
        getLiveOrderbook(requestedSymbol, 10)
      ]);

      if (requestIdRef.current !== requestId) {
        return;
      }

      setQuote((current) => mergeLoadState(q, current, "Failed to load quote"));
      setTrades((current) => mergeLoadState(t, current, "Failed to load trades"));
      setOrderbook((current) => mergeLoadState(ob, current, "Failed to load order book"));
    } finally {
      if (requestIdRef.current === requestId) {
        inFlightSymbolRef.current = null;
        setRefreshing(false);
      }
    }
  }, []);

  useEffect(() => {
    if (!activeSymbol) return;
    void fetchAll(activeSymbol);
    const interval = window.setInterval(() => void fetchAll(activeSymbol), POLL_INTERVAL_MS);
    return () => window.clearInterval(interval);
  }, [activeSymbol, fetchAll]);

  useEffect(() => {
    const nextSymbol = (searchParams.get("symbol") ?? "").trim().toUpperCase();
    if (nextSymbol === (activeSymbol ?? "")) {
      return;
    }

    setSymbolInput(nextSymbol);
    setActiveSymbol(nextSymbol || null);
    setQuote({ data: null, error: null });
    setTrades({ data: null, error: null });
    setOrderbook({ data: null, error: null });
    quickTrade.resetTicket();
  }, [activeSymbol, quickTrade, searchParams]);

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault();
    const next = symbolInput.trim().toUpperCase();
    if (!next) return;
    setQuote({ data: null, error: null });
    setTrades({ data: null, error: null });
    setOrderbook({ data: null, error: null });
    setActiveSymbol(next);
    quickTrade.resetTicket();
    setSearchParams({ symbol: next }, { replace: true });
  };

  const marketVm = useMemo(() => buildLiveQuotesMarketViewModel({
    activeSymbol,
    quote,
    trades,
    orderbook,
    refreshing,
    tradeTableLimit: TRADE_TABLE_LIMIT
  }), [activeSymbol, orderbook, quote, refreshing, trades]);
  const quoteRow = marketVm.quoteRow;

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
              <Button asChild variant="outline" size="sm">
                <Link to="/data/watchlist" aria-label="Open symbol watchlist">
                  <ListPlus className="h-4 w-4" aria-hidden="true" />
                  <span className="ml-1.5">Watchlist</span>
                </Link>
              </Button>
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
              {marketVm.venueLabel ? <Badge variant="outline">{marketVm.venueLabel}</Badge> : null}
              {marketVm.stale ? <Badge variant="warning">Stale</Badge> : null}
              <span>Last update {marketVm.lastUpdateLabel}</span>
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
        <>
        <HistoricalChartCard symbol={activeSymbol} />
        <PriceChartCard
          symbol={activeSymbol}
          metrics={marketVm.intraday}
          loading={marketVm.tradesState.status === "loading"}
          error={marketVm.tradesState.status === "error" ? marketVm.tradesState.message : null}
        />
        <div className="grid gap-4 lg:grid-cols-[1fr_1fr]">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Best bid / offer</CardTitle>
              <CardDescription>Click bid or ask to seed the trade ticket</CardDescription>
            </CardHeader>
            <CardContent>
              {!marketVm.quoteState.showData || !quoteRow ? (
                <PanelStateMessage state={marketVm.quoteState} />
              ) : (
                <div className="space-y-3">
                  <PanelStateMessage state={marketVm.quoteState} />
                  <div className="grid gap-4 sm:grid-cols-2">
                    <BboPanel
                      label="Bid"
                      price={quoteRow.bidPrice}
                      size={quoteRow.bidSize}
                      tone="positive"
                      icon={<ArrowDown className="h-4 w-4" aria-hidden="true" />}
                      onSeed={() => quickTrade.seedTicket("Sell", quoteRow.bidPrice)}
                      seedLabel={`Sell ${activeSymbol} at bid ${formatPrice(quoteRow.bidPrice)}`}
                    />
                    <BboPanel
                      label="Ask"
                      price={quoteRow.askPrice}
                      size={quoteRow.askSize}
                      tone="negative"
                      icon={<ArrowUp className="h-4 w-4" aria-hidden="true" />}
                      onSeed={() => quickTrade.seedTicket("Buy", quoteRow.askPrice)}
                      seedLabel={`Buy ${activeSymbol} at ask ${formatPrice(quoteRow.askPrice)}`}
                    />
                    <MetricRow label="Mid" value={formatPrice(quoteRow.midPrice)} />
                    <MetricRow label="Spread" value={formatPrice(quoteRow.spread)} />
                    <MetricRow label="Sequence" value={quoteRow.sequenceNumber.toLocaleString()} />
                    <MetricRow label="Stream" value={quoteRow.streamId ?? "—"} />
                  </div>
                </div>
              )}
            </CardContent>
          </Card>

          <QuickTradeCard
            symbol={activeSymbol}
            vm={quickTrade}
          />

          <Card>
            <CardHeader>
              <CardTitle className="text-base">Order book (L2)</CardTitle>
              <CardDescription>{marketVm.orderbookDescription}</CardDescription>
            </CardHeader>
            <CardContent>
              {!marketVm.orderbookState.showData || !marketVm.orderbook ? (
                <PanelStateMessage state={marketVm.orderbookState} />
              ) : (
                <div className="space-y-3">
                  <PanelStateMessage state={marketVm.orderbookState} />
                  <DepthLadder
                    data={marketVm.orderbook}
                    onSeedBuy={(price) => quickTrade.seedTicket("Buy", price)}
                    onSeedSell={(price) => quickTrade.seedTicket("Sell", price)}
                    symbol={activeSymbol}
                  />
                </div>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-base">Recent trades</CardTitle>
              <CardDescription>{marketVm.tradesDescription}</CardDescription>
            </CardHeader>
            <CardContent>
              {!marketVm.tradesState.showData ? (
                <PanelStateMessage state={marketVm.tradesState} />
              ) : (
                <div className="space-y-3">
                  <PanelStateMessage state={marketVm.tradesState} />
                  <TradesTable trades={marketVm.tradeRows} />
                </div>
              )}
            </CardContent>
          </Card>
        </div>
        </>
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

function PanelStateMessage({ state }: { state: LiveQuotesPanelState }) {
  if (!state.message) {
    return null;
  }

  return (
    <p
      role={state.role}
      aria-live={state.role === "status" ? "polite" : undefined}
      className={`rounded-md border px-3 py-2 text-sm ${state.toneClass}`}
    >
      {state.message}
    </p>
  );
}

interface BboPanelProps {
  label: string;
  price: number;
  size: number;
  tone: "positive" | "negative";
  icon: React.ReactNode;
  onSeed?: () => void;
  seedLabel?: string;
}

function BboPanel({ label, price, size, tone, icon, onSeed, seedLabel }: BboPanelProps) {
  const toneClass = tone === "positive"
    ? "border-positive/30 bg-positive/5 text-positive"
    : "border-danger/30 bg-danger/5 text-danger";
  const interactive = typeof onSeed === "function" && Number.isFinite(price) && price > 0;
  const interactiveClass = interactive
    ? "cursor-pointer hover:bg-secondary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
    : "";

  if (!interactive) {
    return (
      <div className={`rounded-md border px-3 py-3 text-left ${toneClass}`}>
        <div className="flex items-center gap-1.5 text-xs uppercase tracking-wide">
          {icon}
          <span>{label}</span>
        </div>
        <div className="mt-2 font-mono text-2xl text-foreground">{formatPrice(price)}</div>
        <div className="mt-1 text-xs text-muted-foreground">{formatSize(size)} shares</div>
      </div>
    );
  }

  return (
    <button
      type="button"
      onClick={onSeed}
      aria-label={seedLabel ?? `${label} ${formatPrice(price)}`}
      className={`rounded-md border px-3 py-3 text-left transition-colors ${toneClass} ${interactiveClass}`}
    >
      <div className="flex items-center gap-1.5 text-xs uppercase tracking-wide">
        {icon}
        <span>{label}</span>
      </div>
      <div className="mt-2 font-mono text-2xl text-foreground">{formatPrice(price)}</div>
      <div className="mt-1 text-xs text-muted-foreground">{formatSize(size)} shares</div>
    </button>
  );
}

interface DepthLadderProps {
  data: OrderBookResponse;
  onSeedBuy?: (price: number) => void;
  onSeedSell?: (price: number) => void;
  symbol?: string;
}

function DepthLadder({ data, onSeedBuy, onSeedSell, symbol }: DepthLadderProps) {
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
          <button
            type="button"
            key={`bid-${level.level}`}
            onClick={() => onSeedSell?.(level.price)}
            aria-label={symbol ? `Sell ${symbol} at ${formatPrice(level.price)}` : `Sell at ${formatPrice(level.price)}`}
            className="relative flex w-full justify-between rounded-sm px-2 py-1 text-left transition-colors hover:bg-positive/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          >
            <span
              aria-hidden="true"
              className="absolute inset-y-0 right-0 rounded-sm bg-positive/15"
              style={{ width: `${(level.size / maxSize) * 100}%` }}
            />
            <span className="relative">{formatSize(level.size)}</span>
            <span className="relative text-positive">{formatPrice(level.price)}</span>
          </button>
        ))}
      </div>
      <div>
        <div className="mb-1 flex justify-between text-muted-foreground">
          <span>Price</span>
          <span>Ask size</span>
        </div>
        {data.asks.map((level) => (
          <button
            type="button"
            key={`ask-${level.level}`}
            onClick={() => onSeedBuy?.(level.price)}
            aria-label={symbol ? `Buy ${symbol} at ${formatPrice(level.price)}` : `Buy at ${formatPrice(level.price)}`}
            className="relative flex w-full justify-between rounded-sm px-2 py-1 text-left transition-colors hover:bg-danger/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          >
            <span
              aria-hidden="true"
              className="absolute inset-y-0 left-0 rounded-sm bg-danger/15"
              style={{ width: `${(level.size / maxSize) * 100}%` }}
            />
            <span className="relative text-danger">{formatPrice(level.price)}</span>
            <span className="relative">{formatSize(level.size)}</span>
          </button>
        ))}
      </div>
    </div>
  );
}

interface QuickTradeCardProps {
  symbol: string;
  vm: QuickTradeTicketViewModel;
}

function QuickTradeCard({ symbol, vm }: QuickTradeCardProps) {
  const { ticket, submitCommand, status } = vm;
  const statusToneClass = quickTicketStatusClass[status.tone];
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <TrendingUp className="h-4 w-4 text-primary" aria-hidden="true" />
          Quick trade
        </CardTitle>
        <CardDescription>
          Submit a paper or live order for {symbol}. Click the bid or ask above to seed the price.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={vm.submitTicket} className="space-y-3" aria-describedby={status.id}>
          <div className="grid gap-2 sm:grid-cols-2">
            <div className="flex flex-col gap-1">
              <label htmlFor="quick-ticket-side" className="text-xs uppercase tracking-wide text-muted-foreground">Side</label>
              <Select
                id="quick-ticket-side"
                value={ticket.side}
                onChange={(event) => vm.updateField("side", event.target.value as "Buy" | "Sell")}
                className={vm.sideToneClass}
                aria-label="Order side"
              >
                <option value="Buy">Buy</option>
                <option value="Sell">Sell</option>
              </Select>
            </div>
            <div className="flex flex-col gap-1">
              <label htmlFor="quick-ticket-type" className="text-xs uppercase tracking-wide text-muted-foreground">Type</label>
              <Select
                id="quick-ticket-type"
                value={ticket.type}
                onChange={(event) => vm.updateField("type", event.target.value as "Market" | "Limit")}
                aria-label="Order type"
              >
                <option value="Limit">Limit</option>
                <option value="Market">Market</option>
              </Select>
            </div>
          </div>

          <div className="grid gap-2 sm:grid-cols-2">
            <div className="flex flex-col gap-1">
              <label htmlFor="quick-ticket-quantity" className="text-xs uppercase tracking-wide text-muted-foreground">Quantity</label>
              <Input
                id="quick-ticket-quantity"
                type="number"
                inputMode="numeric"
                min={1}
                step={1}
                placeholder="100"
                value={ticket.quantity}
                onChange={(event) => vm.updateField("quantity", event.target.value)}
                autoComplete="off"
                spellCheck={false}
                error={vm.quantityInvalid}
                aria-label="Order quantity in shares"
                aria-describedby={status.id}
              />
            </div>
            <div className="flex flex-col gap-1">
              <label htmlFor="quick-ticket-price" className="text-xs uppercase tracking-wide text-muted-foreground">
                {ticket.type === "Market" ? "Price (market)" : "Limit price"}
              </label>
              <Input
                id="quick-ticket-price"
                type="number"
                inputMode="decimal"
                min={0}
                step="0.01"
                placeholder={ticket.type === "Market" ? "Best available" : "0.00"}
                value={ticket.type === "Market" ? "" : ticket.limitPrice}
                onChange={(event) => vm.updateField("limitPrice", event.target.value)}
                disabled={vm.priceDisabled}
                autoComplete="off"
                spellCheck={false}
                error={vm.priceInvalid}
                aria-label="Limit price"
                aria-describedby={status.id}
              />
            </div>
          </div>

          <Button
            type="submit"
            variant={submitCommand.variant}
            className="w-full"
            disabled={submitCommand.disabled}
            disabledReason={submitCommand.disabledReason}
            busy={submitCommand.busy}
            busyLabel={submitCommand.busyLabel}
            aria-label={submitCommand.ariaLabel}
          >
            <Send className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">{submitCommand.label}</span>
          </Button>

          <div
            id={status.id}
            role={status.role}
            aria-live="polite"
            className={`flex min-h-[1.25rem] items-center gap-1.5 text-xs ${statusToneClass}`}
          >
            {status.showSuccessIcon ? <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" /> : null}
            {status.showErrorIcon ? <AlertCircle className="h-3.5 w-3.5" aria-hidden="true" /> : null}
            <span>{status.message}</span>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

const quickTicketStatusClass = {
  default: "text-muted-foreground",
  success: "text-positive",
  danger: "text-danger"
} as const;

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

function formatChange(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return "—";
  const sign = value > 0 ? "+" : value < 0 ? "" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 })}`;
}

function formatChangePct(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return "—";
  const sign = value > 0 ? "+" : value < 0 ? "" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%`;
}

function formatVolume(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return "—";
  if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(2)}M`;
  if (value >= 1_000) return `${(value / 1_000).toFixed(1)}K`;
  return value.toLocaleString();
}

function formatWindowSpan(startIso: string | null, endIso: string | null): string {
  if (!startIso || !endIso) return "Waiting for prints";
  const start = new Date(startIso).getTime();
  const end = new Date(endIso).getTime();
  if (!Number.isFinite(start) || !Number.isFinite(end) || end < start) return "Waiting for prints";
  const seconds = Math.max(1, Math.round((end - start) / 1000));
  if (seconds < 60) return `over ${seconds}s`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `over ${minutes}m`;
  const hours = Math.round(minutes / 60);
  return `over ${hours}h`;
}

interface PriceChartCardProps {
  symbol: string;
  metrics: IntradayMetrics;
  loading: boolean;
  error: string | null;
}

function PriceChartCard({ symbol, metrics, loading, error }: PriceChartCardProps) {
  const tone = metrics.change === null
    ? "text-foreground"
    : metrics.change > 0
      ? "text-positive"
      : metrics.change < 0
        ? "text-danger"
        : "text-foreground";
  const stroke = metrics.change === null
    ? "var(--meridian-chart-stroke, #94a3b8)"
    : metrics.change > 0
      ? "var(--meridian-chart-positive, #10b981)"
      : metrics.change < 0
        ? "var(--meridian-chart-danger, #ef4444)"
        : "var(--meridian-chart-stroke, #94a3b8)";

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-1 sm:flex-row sm:items-end sm:justify-between">
          <div>
            <div className="eyebrow-label">Recent price action</div>
            <CardTitle className="flex items-center gap-2 text-base">
              <LineChart className="h-4 w-4 text-primary" aria-hidden="true" />
              {symbol} prints {formatWindowSpan(metrics.windowStart, metrics.windowEnd)}
            </CardTitle>
            <CardDescription>
              Last {metrics.count} trades streamed from the live pipeline. Chart shows trade-by-trade price; not a fixed-interval candle.
            </CardDescription>
          </div>
          <div className="flex flex-col items-start gap-0.5 sm:items-end" aria-live="polite">
            <span className="font-mono text-2xl text-foreground">{formatPrice(metrics.last)}</span>
            <span className={`font-mono text-xs ${tone}`}>
              {formatChange(metrics.change)} ({formatChangePct(metrics.changePct)})
            </span>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        {error && metrics.count === 0 ? (
          <p className="text-sm text-danger">{error}</p>
        ) : metrics.count === 0 ? (
          <p className="text-sm text-muted-foreground">
            {loading ? `Waiting for prints from ${symbol}…` : `No recent prints available for ${symbol}.`}
          </p>
        ) : (
          <div className="space-y-3">
            <PriceSparkline
              series={metrics.series}
              stroke={stroke}
              high={metrics.high}
              low={metrics.low}
              symbol={symbol}
            />
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-5">
              <ChartStat label="Open" value={formatPrice(metrics.open)} />
              <ChartStat label="High" value={formatPrice(metrics.high)} />
              <ChartStat label="Low" value={formatPrice(metrics.low)} />
              <ChartStat label="VWAP" value={formatPrice(metrics.vwap)} />
              <ChartStat label="Volume" value={formatVolume(metrics.volume)} />
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function ChartStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-border/60 bg-secondary/25 px-2.5 py-1.5">
      <div className="text-[10px] uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-0.5 font-mono text-sm text-foreground">{value}</div>
    </div>
  );
}

interface PriceSparklineProps {
  series: { ts: number; price: number }[];
  stroke: string;
  high: number | null;
  low: number | null;
  symbol: string;
}

function PriceSparkline({ series, stroke, high, low, symbol }: PriceSparklineProps) {
  const width = 800;
  const height = 180;
  const padX = 8;
  const padY = 14;

  if (series.length === 0 || high === null || low === null) {
    return null;
  }

  const minTs = series[0]!.ts;
  const maxTs = series[series.length - 1]!.ts;
  const tsSpan = Math.max(1, maxTs - minTs);
  const priceSpan = Math.max(high - low, Math.max(high * 0.0005, 0.01));
  const xFor = (ts: number) => padX + ((ts - minTs) / tsSpan) * (width - padX * 2);
  const yFor = (price: number) => padY + (1 - (price - low) / priceSpan) * (height - padY * 2);

  const pointsAttr = series.map((p) => `${xFor(p.ts).toFixed(2)},${yFor(p.price).toFixed(2)}`).join(" ");
  const lastPoint = series[series.length - 1]!;
  const lastX = xFor(lastPoint.ts);
  const lastY = yFor(lastPoint.price);
  const baseY = (height - padY).toFixed(2);
  const areaSegments = [`M ${xFor(series[0]!.ts).toFixed(2)} ${baseY}`];
  for (const point of series) {
    areaSegments.push(`L ${xFor(point.ts).toFixed(2)} ${yFor(point.price).toFixed(2)}`);
  }
  areaSegments.push(`L ${lastX.toFixed(2)} ${baseY} Z`);
  const areaPath = areaSegments.join(" ");

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      className="block h-44 w-full overflow-visible"
      role="img"
      aria-label={`Recent ${symbol} trade prices, ranging from ${formatPrice(low)} to ${formatPrice(high)}.`}
    >
      <line
        x1={padX}
        x2={width - padX}
        y1={yFor(high)}
        y2={yFor(high)}
        stroke="currentColor"
        strokeOpacity="0.15"
        strokeDasharray="4 4"
      />
      <line
        x1={padX}
        x2={width - padX}
        y1={yFor(low)}
        y2={yFor(low)}
        stroke="currentColor"
        strokeOpacity="0.15"
        strokeDasharray="4 4"
      />
      <path d={areaPath} fill={stroke} fillOpacity="0.12" stroke="none" />
      <polyline
        fill="none"
        stroke={stroke}
        strokeWidth="1.75"
        strokeLinejoin="round"
        strokeLinecap="round"
        points={pointsAttr}
      />
      <circle cx={lastX} cy={lastY} r="3.25" fill={stroke} stroke="currentColor" strokeOpacity="0.4" strokeWidth="1" />
      <text
        x={width - padX}
        y={Math.max(yFor(high) - 4, 12)}
        textAnchor="end"
        fontFamily="IBM Plex Mono, ui-monospace"
        fontSize="10"
        fill="currentColor"
        fillOpacity="0.55"
      >
        {formatPrice(high)}
      </text>
      <text
        x={width - padX}
        y={Math.min(yFor(low) + 12, height - 4)}
        textAnchor="end"
        fontFamily="IBM Plex Mono, ui-monospace"
        fontSize="10"
        fill="currentColor"
        fillOpacity="0.55"
      >
        {formatPrice(low)}
      </text>
    </svg>
  );
}
