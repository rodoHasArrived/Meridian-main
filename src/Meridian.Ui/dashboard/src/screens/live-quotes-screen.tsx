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
import type {
  OrderBookResponse,
  OrderResult,
  OrderSubmitRequest,
  QuotesResponse,
  SessionStatsDto,
  TradeDataResponse,
  TradesResponse
} from "@/types";

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

export function LiveQuotesScreen() {
  const [searchParams, setSearchParams] = useSearchParams();
  const initialSymbol = (searchParams.get("symbol") ?? "").trim().toUpperCase();
  const [symbolInput, setSymbolInput] = useState(initialSymbol);
  const [activeSymbol, setActiveSymbol] = useState<string | null>(initialSymbol || null);
  const [quote, setQuote] = useState<LoadState<QuotesResponse>>({ data: null, error: null });
  const [trades, setTrades] = useState<LoadState<TradesResponse>>({ data: null, error: null });
  const [orderbook, setOrderbook] = useState<LoadState<OrderBookResponse>>({ data: null, error: null });
  const [refreshing, setRefreshing] = useState(false);
  const inFlightRef = useRef(false);
  const [ticket, setTicket] = useState<QuickTicketState>(initialQuickTicketState);

  const fetchAll = useCallback(async (symbol: string) => {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    setRefreshing(true);
    try {
      const [q, t, ob] = await Promise.allSettled([
        getLiveQuote(symbol),
        getLiveTrades(symbol, TRADE_HISTORY_LIMIT),
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
    setTicket(initialQuickTicketState);
    setSearchParams({ symbol: next }, { replace: true });
  };

  const seedTicket = useCallback((side: "Buy" | "Sell", price: number) => {
    setTicket((current) => ({
      ...current,
      side,
      type: "Limit",
      limitPrice: formatTicketPrice(price),
      phase: "idle",
      message: null,
      orderId: null
    }));
  }, []);

  const updateTicketField = useCallback(<K extends keyof QuickTicketForm>(field: K, value: QuickTicketForm[K]) => {
    setTicket((current) => ({
      ...current,
      [field]: value,
      phase: current.phase === "submitted" ? "idle" : current.phase,
      message: current.phase === "error" ? null : current.message
    }));
  }, []);

  const submitTicket = useCallback(async (event: React.FormEvent) => {
    event.preventDefault();
    if (!activeSymbol) return;

    const validation = validateQuickTicket(ticket);
    if (validation) {
      setTicket((current) => ({ ...current, phase: "error", message: validation, orderId: null }));
      return;
    }

    const request: OrderSubmitRequest = {
      symbol: activeSymbol,
      side: ticket.side,
      type: ticket.type,
      quantity: Number(ticket.quantity),
      limitPrice: ticket.type === "Market" ? null : Number(ticket.limitPrice)
    };

    setTicket((current) => ({ ...current, phase: "submitting", message: null, orderId: null }));
    try {
      const result: OrderResult = await submitOrder(request);
      if (result.success) {
        setTicket((current) => ({
          ...current,
          phase: "submitted",
          message: result.orderId ? `Order ${result.orderId} accepted.` : "Order accepted.",
          orderId: result.orderId
        }));
      } else {
        setTicket((current) => ({
          ...current,
          phase: "error",
          message: result.reason ?? "Order rejected.",
          orderId: null
        }));
      }
    } catch (err) {
      setTicket((current) => ({
        ...current,
        phase: "error",
        message: (err as Error)?.message ?? "Order submission failed.",
        orderId: null
      }));
    }
  }, [activeSymbol, ticket]);

  const quoteRow = quote.data?.quote;
  const session = quoteRow?.session ?? null;
  const stale = orderbook.data?.isStale === true;
  const venueLabel = quoteRow?.venue ?? orderbook.data?.venue ?? null;

  const tradeHistory = useMemo<TradeDataResponse[]>(() => trades.data?.trades ?? [], [trades.data]);
  const tradeRows = useMemo(() => tradeHistory.slice(0, TRADE_TABLE_LIMIT), [tradeHistory]);
  const intraday = useMemo(() => computeIntradayMetrics(tradeHistory), [tradeHistory]);

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
        <>
        <HistoricalChartCard symbol={activeSymbol} />
        <PriceChartCard
          symbol={activeSymbol}
          metrics={intraday}
          loading={refreshing && tradeHistory.length === 0}
          error={trades.error}
        />
        <div className="grid gap-4 lg:grid-cols-[1fr_1fr]">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Best bid / offer</CardTitle>
              <CardDescription>Click bid or ask to seed the trade ticket</CardDescription>
            </CardHeader>
            <CardContent>
              {quote.error && !quote.data ? (
                <p className="text-sm text-danger">{quote.error}</p>
              ) : !quoteRow ? (
                <p className="text-sm text-muted-foreground">No quote data available for {activeSymbol}.</p>
              ) : (
                <div className="space-y-4">
                  {session ? <SessionStatsBanner session={session} /> : null}
                  <div className="grid gap-4 sm:grid-cols-2">
                    <BboPanel
                      label="Bid"
                      price={quoteRow.bidPrice}
                      size={quoteRow.bidSize}
                      tone="positive"
                      icon={<ArrowDown className="h-4 w-4" aria-hidden="true" />}
                      onSeed={() => seedTicket("Sell", quoteRow.bidPrice)}
                      seedLabel={`Sell ${activeSymbol} at bid ${formatPrice(quoteRow.bidPrice)}`}
                    />
                    <BboPanel
                      label="Ask"
                      price={quoteRow.askPrice}
                      size={quoteRow.askSize}
                      tone="negative"
                      icon={<ArrowUp className="h-4 w-4" aria-hidden="true" />}
                      onSeed={() => seedTicket("Buy", quoteRow.askPrice)}
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
            ticket={ticket}
            onFieldChange={updateTicketField}
            onSubmit={submitTicket}
          />

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
                <DepthLadder
                  data={orderbook.data}
                  onSeedBuy={(price) => seedTicket("Buy", price)}
                  onSeedSell={(price) => seedTicket("Sell", price)}
                  symbol={activeSymbol}
                />
              )}
            </CardContent>
          </Card>

          <Card>
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

function SessionStatsBanner({ session }: { session: SessionStatsDto }) {
  const tone = session.change > 0
    ? "text-positive"
    : session.change < 0
      ? "text-danger"
      : "text-foreground";
  const sign = session.change > 0 ? "+" : session.change < 0 ? "" : "";
  const pct = session.changePercent;
  const pctText = pct === null || !Number.isFinite(pct)
    ? ""
    : ` (${pct > 0 ? "+" : pct < 0 ? "" : ""}${pct.toFixed(2)}%)`;
  const changeText = `${sign}${session.change.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 4
  })}${pctText}`;
  const volumeText = session.volume >= 1_000_000
    ? `${(session.volume / 1_000_000).toFixed(2)}M`
    : session.volume >= 1_000
      ? `${(session.volume / 1_000).toFixed(1)}K`
      : session.volume.toLocaleString();

  return (
    <div className="rounded-md border border-border/60 bg-secondary/25 px-3 py-2.5">
      <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
        <div className="flex items-baseline gap-3">
          <span className="text-xs uppercase tracking-wide text-muted-foreground">Today</span>
          <span className={`font-mono text-base ${tone}`} aria-label="Day change">{changeText}</span>
        </div>
        <span className="text-[11px] text-muted-foreground">Session {session.sessionDate}</span>
      </div>
      <div className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 font-mono text-xs sm:grid-cols-5">
        <SessionStatCell label="Open" value={formatPrice(session.open)} />
        <SessionStatCell label="High" value={formatPrice(session.high)} />
        <SessionStatCell label="Low" value={formatPrice(session.low)} />
        <SessionStatCell label="VWAP" value={formatPrice(session.vwap)} />
        <SessionStatCell label="Volume" value={volumeText} />
      </div>
    </div>
  );
}

function SessionStatCell({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between sm:flex-col sm:items-start">
      <span className="text-[10px] uppercase tracking-wide text-muted-foreground">{label}</span>
      <span className="text-foreground">{value}</span>
    </div>
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

type QuickTicketPhase = "idle" | "submitting" | "submitted" | "error";

interface QuickTicketForm {
  side: "Buy" | "Sell";
  type: "Market" | "Limit";
  quantity: string;
  limitPrice: string;
}

interface QuickTicketState extends QuickTicketForm {
  phase: QuickTicketPhase;
  message: string | null;
  orderId: string | null;
}

const initialQuickTicketState: QuickTicketState = {
  side: "Buy",
  type: "Limit",
  quantity: "",
  limitPrice: "",
  phase: "idle",
  message: null,
  orderId: null
};

function formatTicketPrice(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return "";
  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 4,
    useGrouping: false
  });
}

export function validateQuickTicket(state: QuickTicketForm): string | null {
  const qty = Number(state.quantity);
  if (!state.quantity || !Number.isFinite(qty) || qty <= 0) {
    return "Enter a quantity greater than zero.";
  }
  if (!Number.isInteger(qty)) {
    return "Quantity must be a whole number of shares.";
  }
  if (state.type === "Limit") {
    const price = Number(state.limitPrice);
    if (!state.limitPrice || !Number.isFinite(price) || price <= 0) {
      return "Enter a limit price greater than zero.";
    }
  }
  return null;
}

interface QuickTradeCardProps {
  symbol: string;
  ticket: QuickTicketState;
  onFieldChange: <K extends keyof QuickTicketForm>(field: K, value: QuickTicketForm[K]) => void;
  onSubmit: (event: React.FormEvent) => void;
}

function QuickTradeCard({ symbol, ticket, onFieldChange, onSubmit }: QuickTradeCardProps) {
  const submitting = ticket.phase === "submitting";
  const submitted = ticket.phase === "submitted";
  const errored = ticket.phase === "error";
  const sideTone = ticket.side === "Buy"
    ? "bg-positive/10 text-positive border-positive/30"
    : "bg-danger/10 text-danger border-danger/30";
  const submitLabel = submitting
    ? "Submitting…"
    : `${ticket.side} ${symbol}${ticket.type === "Limit" && ticket.limitPrice ? ` @ ${ticket.limitPrice}` : ""}`;

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
        <form onSubmit={onSubmit} className="space-y-3" aria-describedby="quick-ticket-status">
          <div className="grid gap-2 sm:grid-cols-2">
            <div className="flex flex-col gap-1">
              <label htmlFor="quick-ticket-side" className="text-xs uppercase tracking-wide text-muted-foreground">Side</label>
              <Select
                id="quick-ticket-side"
                value={ticket.side}
                onChange={(event) => onFieldChange("side", event.target.value as "Buy" | "Sell")}
                className={sideTone}
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
                onChange={(event) => onFieldChange("type", event.target.value as "Market" | "Limit")}
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
                onChange={(event) => onFieldChange("quantity", event.target.value)}
                autoComplete="off"
                spellCheck={false}
                aria-label="Order quantity in shares"
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
                onChange={(event) => onFieldChange("limitPrice", event.target.value)}
                disabled={ticket.type === "Market"}
                autoComplete="off"
                spellCheck={false}
                aria-label="Limit price"
              />
            </div>
          </div>

          <Button
            type="submit"
            variant={ticket.side === "Buy" ? "default" : "destructive"}
            className="w-full"
            disabled={submitting}
            aria-label={`Submit ${ticket.side.toLowerCase()} order for ${symbol}`}
          >
            <Send className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">{submitLabel}</span>
          </Button>

          <div id="quick-ticket-status" aria-live="polite" className="min-h-[1.25rem] text-xs">
            {submitted && ticket.message ? (
              <span className="flex items-center gap-1.5 text-positive">
                <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                {ticket.message}
              </span>
            ) : errored && ticket.message ? (
              <span className="flex items-center gap-1.5 text-danger">
                <AlertCircle className="h-3.5 w-3.5" aria-hidden="true" />
                {ticket.message}
              </span>
            ) : (
              <span className="text-muted-foreground">
                Orders route through Meridian's pre-trade risk and execution controls.
              </span>
            )}
          </div>
        </form>
      </CardContent>
    </Card>
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

interface IntradayMetrics {
  count: number;
  open: number | null;
  last: number | null;
  high: number | null;
  low: number | null;
  vwap: number | null;
  volume: number;
  change: number | null;
  changePct: number | null;
  windowStart: string | null;
  windowEnd: string | null;
  series: { ts: number; price: number }[];
}

export function computeIntradayMetrics(trades: readonly TradeDataResponse[]): IntradayMetrics {
  if (trades.length === 0) {
    return {
      count: 0,
      open: null,
      last: null,
      high: null,
      low: null,
      vwap: null,
      volume: 0,
      change: null,
      changePct: null,
      windowStart: null,
      windowEnd: null,
      series: []
    };
  }

  // The API returns most-recent trades first; chronological is needed for charts.
  const chronological = [...trades].reverse();
  const series: { ts: number; price: number }[] = [];
  let high = -Infinity;
  let low = Infinity;
  let volume = 0;
  let pxVolume = 0;
  for (const trade of chronological) {
    const price = Number(trade.price);
    const size = Number(trade.size);
    if (!Number.isFinite(price) || price <= 0) continue;
    const ts = new Date(trade.timestamp).getTime();
    if (Number.isFinite(ts)) {
      series.push({ ts, price });
    }
    if (price > high) high = price;
    if (price < low) low = price;
    if (Number.isFinite(size) && size > 0) {
      volume += size;
      pxVolume += size * price;
    }
  }

  const open = series[0]?.price ?? null;
  const last = series[series.length - 1]?.price ?? null;
  const change = open !== null && last !== null ? last - open : null;
  const changePct = change !== null && open !== null && open !== 0 ? (change / open) * 100 : null;
  const vwap = volume > 0 ? pxVolume / volume : null;

  return {
    count: chronological.length,
    open,
    last,
    high: Number.isFinite(high) ? high : null,
    low: Number.isFinite(low) ? low : null,
    vwap,
    volume,
    change,
    changePct,
    windowStart: chronological[0]?.timestamp ?? null,
    windowEnd: chronological[chronological.length - 1]?.timestamp ?? null,
    series
  };
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
