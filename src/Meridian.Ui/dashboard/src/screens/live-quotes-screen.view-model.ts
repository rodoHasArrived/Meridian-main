import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import type {
  OrderBookLevelDto,
  OrderBookResponse,
  OrderResult,
  OrderSubmitRequest,
  QuotesResponse,
  TradeDataResponse,
  TradesResponse
} from "@/types";

export const LIVE_QUOTES_POLL_INTERVAL_MS = 2000;
export const LIVE_QUOTES_TRADE_HISTORY_LIMIT = 200;
export const LIVE_QUOTES_TRADE_TABLE_LIMIT = 25;

export type QuickTicketPhase = "idle" | "submitting" | "submitted" | "error";

export interface QuickTicketForm {
  side: "Buy" | "Sell";
  type: "Market" | "Limit";
  quantity: string;
  limitPrice: string;
}

export interface QuickTicketState extends QuickTicketForm {
  phase: QuickTicketPhase;
  message: string | null;
  orderId: string | null;
}

export interface QuickTicketStatusViewModel {
  id: string;
  role: "status" | "alert";
  tone: "default" | "success" | "danger";
  message: string;
  showSuccessIcon: boolean;
  showErrorIcon: boolean;
}

export interface QuickTicketCommandViewModel {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
  busyLabel: string;
  variant: "default" | "destructive";
}

export interface QuickTradeTicketViewModel {
  ticket: QuickTicketState;
  submitting: boolean;
  priceDisabled: boolean;
  quantityInvalid: boolean;
  priceInvalid: boolean;
  sideToneClass: string;
  submitCommand: QuickTicketCommandViewModel;
  status: QuickTicketStatusViewModel;
  seedTicket: (side: "Buy" | "Sell", price: number) => void;
  updateField: <K extends keyof QuickTicketForm>(field: K, value: QuickTicketForm[K]) => void;
  submitTicket: (event: FormEvent) => Promise<void>;
  resetTicket: () => void;
}

export interface QuickTradeTicketApi {
  submitOrder: (request: OrderSubmitRequest) => Promise<OrderResult>;
}

export interface LiveQuotesLoadState<T> {
  data: T | null;
  error: string | null;
}

export interface LiveQuoteSymbolLookupCommandViewModel {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
}

export interface LiveQuoteSymbolLookupStatusViewModel {
  id: string;
  role: "status" | "alert";
  message: string;
  toneClass: string;
}

export interface LiveQuoteSymbolLookupViewModel {
  inputId: string;
  statusId: string;
  formLabel: string;
  inputLabel: string;
  inputPlaceholder: string;
  normalizedSymbol: string;
  inputInvalid: boolean;
  command: LiveQuoteSymbolLookupCommandViewModel;
  status: LiveQuoteSymbolLookupStatusViewModel;
}

export type LiveQuotesPanelStatus = "loading" | "error" | "empty" | "ready" | "warning";

export interface LiveQuotesPanelState {
  status: LiveQuotesPanelStatus;
  message: string | null;
  role: "status" | "alert";
  toneClass: string;
  showData: boolean;
}

export interface IntradayMetrics {
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

export interface LiveQuotesMetricRowViewModel {
  id: string;
  label: string;
  value: string;
}

export interface LiveQuotesBboPanelViewModel {
  id: "bid" | "ask";
  label: string;
  price: number;
  priceLabel: string;
  sizeLabel: string;
  seedSide: "Buy" | "Sell";
  seedLabel: string;
  tone: "positive" | "negative";
}

export interface LiveQuotesDepthLevelViewModel {
  id: string;
  level: number;
  price: number;
  priceLabel: string;
  sizeLabel: string;
  barWidth: string;
  seedLabel: string;
}

export interface LiveQuotesDepthLadderViewModel {
  bids: LiveQuotesDepthLevelViewModel[];
  asks: LiveQuotesDepthLevelViewModel[];
}

export interface LiveQuotesTradeRowViewModel {
  id: string;
  timeLabel: string;
  priceLabel: string;
  sizeLabel: string;
  aggressorLabel: string;
  aggressorTone: "positive" | "negative" | "muted";
  venueLabel: string;
}

export interface LiveQuotesPriceChartViewModel {
  title: string;
  description: string;
  lastPriceLabel: string;
  changeLabel: string;
  changeTone: "positive" | "negative" | "default";
  strokeTone: "positive" | "negative" | "default";
  strokeToken: string;
  statusMessage: string | null;
  statusRole: "status" | "alert";
  stats: LiveQuotesMetricRowViewModel[];
  ariaLabel: string;
  sparkline: LiveQuotesSparklineViewModel | null;
}

export interface LiveQuotesSparklinePointViewModel {
  x: string;
  y: string;
}

export interface LiveQuotesSparklineViewModel {
  viewBox: string;
  points: string;
  areaPath: string;
  lastPoint: LiveQuotesSparklinePointViewModel;
  guideStartX: string;
  guideEndX: string;
  highGuideY: string;
  lowGuideY: string;
  labelX: string;
  highLabelY: string;
  lowLabelY: string;
  highLabel: string;
  lowLabel: string;
  strokeToken: string;
  ariaLabel: string;
}

export interface LiveQuotesMarketDataViewModel {
  activeSymbol: string | null;
  quoteRow: QuotesResponse["quote"] | null;
  orderbook: OrderBookResponse | null;
  tradeHistory: TradeDataResponse[];
  tradeRows: TradeDataResponse[];
  tradeDisplayRows: LiveQuotesTradeRowViewModel[];
  intraday: IntradayMetrics;
  bboPanels: LiveQuotesBboPanelViewModel[];
  quoteMetrics: LiveQuotesMetricRowViewModel[];
  depthLadder: LiveQuotesDepthLadderViewModel;
  priceChart: LiveQuotesPriceChartViewModel;
  venueLabel: string | null;
  stale: boolean;
  lastUpdateLabel: string;
  quoteState: LiveQuotesPanelState;
  orderbookState: LiveQuotesPanelState;
  tradesState: LiveQuotesPanelState;
  orderbookDescription: string;
  tradesDescription: string;
}

export interface LiveQuotesApi {
  getLiveQuote: (symbol: string) => Promise<QuotesResponse>;
  getLiveTrades: (symbol: string, limit: number) => Promise<TradesResponse>;
  getLiveOrderbook: (symbol: string, depth: number) => Promise<OrderBookResponse>;
  submitOrder: (request: OrderSubmitRequest) => Promise<OrderResult>;
}

export interface LiveQuotesRouteBinding {
  routeSymbol: string;
  setRouteSymbol: (symbol: string) => void;
}

export interface LiveQuotesScreenViewModel {
  symbolInput: string;
  setSymbolInput: (value: string) => void;
  activeSymbol: string | null;
  lookup: LiveQuoteSymbolLookupViewModel;
  market: LiveQuotesMarketDataViewModel;
  quickTrade: QuickTradeTicketViewModel;
  refreshCommand: LiveQuoteRefreshCommandViewModel | null;
  pollIntervalSecondsLabel: string;
  submitLookup: (event: FormEvent<HTMLFormElement>) => void;
  refreshMarketData: () => Promise<void>;
}

export interface LiveQuoteRefreshCommandViewModel {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
}

export const initialQuickTicketState: QuickTicketState = {
  side: "Buy",
  type: "Limit",
  quantity: "",
  limitPrice: "",
  phase: "idle",
  message: null,
  orderId: null
};

export function useLiveQuotesScreenViewModel(
  api: LiveQuotesApi,
  route: LiveQuotesRouteBinding
): LiveQuotesScreenViewModel {
  const initialSymbol = normalizeLiveQuoteSymbol(route.routeSymbol);
  const [symbolInput, setSymbolInputState] = useState(initialSymbol);
  const [activeSymbol, setActiveSymbol] = useState<string | null>(initialSymbol || null);
  const [submittedEmptySymbol, setSubmittedEmptySymbol] = useState(false);
  const [quote, setQuote] = useState<LiveQuotesLoadState<QuotesResponse>>({ data: null, error: null });
  const [trades, setTrades] = useState<LiveQuotesLoadState<TradesResponse>>({ data: null, error: null });
  const [orderbook, setOrderbook] = useState<LiveQuotesLoadState<OrderBookResponse>>({ data: null, error: null });
  const [refreshing, setRefreshing] = useState(false);
  const requestIdRef = useRef(0);
  const inFlightSymbolRef = useRef<string | null>(null);
  const mountedRef = useRef(true);
  const quickTrade = useQuickTradeTicket(activeSymbol, { submitOrder: api.submitOrder });

  useEffect(() => {
    mountedRef.current = true;

    return () => {
      mountedRef.current = false;
      requestIdRef.current += 1;
      inFlightSymbolRef.current = null;
    };
  }, []);

  const resetMarketState = useCallback(() => {
    setQuote({ data: null, error: null });
    setTrades({ data: null, error: null });
    setOrderbook({ data: null, error: null });
  }, []);

  const fetchMarketData = useCallback(async (symbol: string) => {
    const requestedSymbol = normalizeLiveQuoteSymbol(symbol);
    if (!requestedSymbol || inFlightSymbolRef.current === requestedSymbol) {
      return;
    }

    const requestId = requestIdRef.current + 1;
    requestIdRef.current = requestId;
    inFlightSymbolRef.current = requestedSymbol;
    setRefreshing(true);

    try {
      const [quoteResult, tradesResult, orderbookResult] = await Promise.allSettled([
        api.getLiveQuote(requestedSymbol),
        api.getLiveTrades(requestedSymbol, LIVE_QUOTES_TRADE_HISTORY_LIMIT),
        api.getLiveOrderbook(requestedSymbol, 10)
      ]);

      if (!mountedRef.current || requestIdRef.current !== requestId) {
        return;
      }

      setQuote((current) => mergeLiveQuotesLoadState(quoteResult, current, "Failed to load quote"));
      setTrades((current) => mergeLiveQuotesLoadState(tradesResult, current, "Failed to load trades"));
      setOrderbook((current) => mergeLiveQuotesLoadState(orderbookResult, current, "Failed to load order book"));
    } finally {
      if (mountedRef.current && requestIdRef.current === requestId) {
        inFlightSymbolRef.current = null;
        setRefreshing(false);
      }
    }
  }, [api.getLiveOrderbook, api.getLiveQuote, api.getLiveTrades]);

  useEffect(() => {
    if (!activeSymbol) {
      return;
    }

    void fetchMarketData(activeSymbol);
    const interval = window.setInterval(() => void fetchMarketData(activeSymbol), LIVE_QUOTES_POLL_INTERVAL_MS);
    return () => window.clearInterval(interval);
  }, [activeSymbol, fetchMarketData]);

  useEffect(() => {
    const nextSymbol = normalizeLiveQuoteSymbol(route.routeSymbol);
    if (nextSymbol === (activeSymbol ?? "")) {
      return;
    }

    setSymbolInputState(nextSymbol);
    setSubmittedEmptySymbol(false);
    setActiveSymbol(nextSymbol || null);
    resetMarketState();
    quickTrade.resetTicket();
  }, [activeSymbol, quickTrade, resetMarketState, route.routeSymbol]);

  const setSymbolInput = useCallback((value: string) => {
    setSymbolInputState(value);
    setSubmittedEmptySymbol(false);
  }, []);

  const lookup = useMemo(() => buildLiveQuoteSymbolLookupViewModel({
    inputValue: symbolInput,
    activeSymbol,
    submittedEmpty: submittedEmptySymbol
  }), [activeSymbol, submittedEmptySymbol, symbolInput]);

  const market = useMemo(() => buildLiveQuotesMarketViewModel({
    activeSymbol,
    quote,
    trades,
    orderbook,
    refreshing,
    tradeTableLimit: LIVE_QUOTES_TRADE_TABLE_LIMIT
  }), [activeSymbol, orderbook, quote, refreshing, trades]);

  const submitLookup = useCallback((event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const next = lookup.normalizedSymbol;
    if (!next) {
      setSubmittedEmptySymbol(true);
      return;
    }

    setSubmittedEmptySymbol(false);
    resetMarketState();
    setActiveSymbol(next);
    quickTrade.resetTicket();
    route.setRouteSymbol(next);
  }, [lookup.normalizedSymbol, quickTrade, resetMarketState, route]);

  const refreshMarketData = useCallback(async () => {
    if (!activeSymbol) {
      return;
    }

    await fetchMarketData(activeSymbol);
  }, [activeSymbol, fetchMarketData]);

  return {
    symbolInput,
    setSymbolInput,
    activeSymbol,
    lookup,
    market,
    quickTrade,
    refreshCommand: buildLiveQuoteRefreshCommand(activeSymbol, refreshing),
    pollIntervalSecondsLabel: String(LIVE_QUOTES_POLL_INTERVAL_MS / 1000),
    submitLookup,
    refreshMarketData
  };
}

export function normalizeLiveQuoteSymbol(value: string): string {
  return value.trim().toUpperCase();
}

export function buildLiveQuoteSymbolLookupViewModel({
  inputValue,
  activeSymbol,
  submittedEmpty
}: {
  inputValue: string;
  activeSymbol: string | null;
  submittedEmpty: boolean;
}): LiveQuoteSymbolLookupViewModel {
  const normalizedSymbol = normalizeLiveQuoteSymbol(inputValue);
  const inputInvalid = submittedEmpty && normalizedSymbol.length === 0;
  const disabledReason = normalizedSymbol.length === 0
    ? "Enter a symbol before loading live market data."
    : null;

  return {
    inputId: "live-quote-symbol",
    statusId: "live-quote-symbol-status",
    formLabel: "Live quote symbol lookup",
    inputLabel: "Symbol",
    inputPlaceholder: "Enter a symbol (e.g. AAPL)",
    normalizedSymbol,
    inputInvalid,
    command: {
      label: "View quote",
      ariaLabel: normalizedSymbol
        ? `View live quote for ${normalizedSymbol}`
        : "View live quote",
      disabled: disabledReason !== null,
      disabledReason
    },
    status: buildLiveQuoteSymbolLookupStatus({
      activeSymbol,
      normalizedSymbol,
      inputInvalid
    })
  };
}

export function mergeLiveQuotesLoadState<T>(
  result: PromiseSettledResult<T>,
  current: LiveQuotesLoadState<T>,
  fallbackMessage: string
): LiveQuotesLoadState<T> {
  if (result.status === "fulfilled") {
    return { data: result.value, error: null };
  }

  const message = result.reason instanceof Error && result.reason.message
    ? result.reason.message
    : fallbackMessage;
  return { data: current.data, error: message };
}

export function buildLiveQuoteRefreshCommand(
  activeSymbol: string | null,
  refreshing: boolean
): LiveQuoteRefreshCommandViewModel | null {
  if (!activeSymbol) {
    return null;
  }

  return {
    label: refreshing ? "Refreshing" : "Refresh",
    ariaLabel: refreshing ? `Refreshing live data for ${activeSymbol}` : `Refresh live data for ${activeSymbol}`,
    disabled: refreshing,
    disabledReason: refreshing ? "Live market data refresh is already running." : null,
    busy: refreshing
  };
}

export function buildLiveQuotesMarketViewModel({
  activeSymbol,
  quote,
  trades,
  orderbook,
  refreshing,
  tradeTableLimit
}: {
  activeSymbol: string | null;
  quote: LiveQuotesLoadState<QuotesResponse>;
  trades: LiveQuotesLoadState<TradesResponse>;
  orderbook: LiveQuotesLoadState<OrderBookResponse>;
  refreshing: boolean;
  tradeTableLimit: number;
}): LiveQuotesMarketDataViewModel {
  const quoteRow = quote.data?.quote ?? null;
  const tradeHistory = trades.data?.trades ?? [];
  const tradeRows = tradeHistory.slice(0, tradeTableLimit);
  const intraday = computeIntradayMetrics(tradeHistory);
  const hasOrderbookRows = (orderbook.data?.bids.length ?? 0) > 0 || (orderbook.data?.asks.length ?? 0) > 0;
  const symbol = activeSymbol ?? "selected symbol";
  const maxDepthSize = Math.max(
    1,
    ...(orderbook.data?.bids.map((level) => level.size) ?? []),
    ...(orderbook.data?.asks.map((level) => level.size) ?? [])
  );

  return {
    activeSymbol,
    quoteRow,
    orderbook: orderbook.data,
    tradeHistory,
    tradeRows,
    tradeDisplayRows: tradeRows.map(buildTradeRow),
    intraday,
    bboPanels: buildBboPanels(symbol, quoteRow),
    quoteMetrics: buildQuoteMetrics(quoteRow),
    depthLadder: {
      bids: (orderbook.data?.bids ?? []).map((level) => buildDepthLevel(level, maxDepthSize, symbol, "Sell")),
      asks: (orderbook.data?.asks ?? []).map((level) => buildDepthLevel(level, maxDepthSize, symbol, "Buy"))
    },
    priceChart: buildPriceChartViewModel(symbol, intraday, refreshing, trades.error),
    venueLabel: quoteRow?.venue ?? orderbook.data?.venue ?? null,
    stale: orderbook.data?.isStale === true,
    lastUpdateLabel: formatMarketTimestamp(quoteRow?.timestamp ?? orderbook.data?.timestamp ?? null),
    quoteState: buildPanelState({
      loading: refreshing && !quote.data && !quote.error,
      error: quote.error,
      ready: quoteRow !== null,
      emptyMessage: `No quote data available for ${symbol}.`,
      loadingMessage: `Loading quote data for ${symbol}...`
    }),
    orderbookState: buildPanelState({
      loading: refreshing && !orderbook.data && !orderbook.error,
      error: orderbook.error,
      ready: hasOrderbookRows,
      emptyMessage: `No depth data available for ${symbol}.`,
      loadingMessage: `Loading depth for ${symbol}...`
    }),
    tradesState: buildPanelState({
      loading: refreshing && !trades.data && !trades.error,
      error: trades.error,
      ready: tradeRows.length > 0,
      emptyMessage: `No recent trades for ${symbol}.`,
      loadingMessage: `Loading recent trades for ${symbol}...`
    }),
    orderbookDescription: `Top ${orderbook.data?.bids.length ?? 0} bids / ${orderbook.data?.asks.length ?? 0} asks`,
    tradesDescription: tradeRows.length > 0 ? `Last ${tradeRows.length} prints` : "Recent prints"
  };
}

export function useQuickTradeTicket(
  activeSymbol: string | null,
  api: QuickTradeTicketApi
): QuickTradeTicketViewModel {
  const [ticket, setTicket] = useState<QuickTicketState>(initialQuickTicketState);

  const resetTicket = useCallback(() => {
    setTicket(initialQuickTicketState);
  }, []);

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

  const updateField = useCallback(<K extends keyof QuickTicketForm>(field: K, value: QuickTicketForm[K]) => {
    setTicket((current) => ({
      ...current,
      [field]: value,
      phase: current.phase === "submitted" ? "idle" : current.phase,
      message: current.phase === "error" ? null : current.message
    }));
  }, []);

  const submitTicket = useCallback(async (event: FormEvent) => {
    event.preventDefault();
    if (!activeSymbol) {
      return;
    }

    const validation = validateQuickTicket(ticket);
    if (validation) {
      setTicket((current) => ({ ...current, phase: "error", message: validation, orderId: null }));
      return;
    }

    const request = buildOrderRequest(activeSymbol, ticket);

    setTicket((current) => ({ ...current, phase: "submitting", message: null, orderId: null }));
    try {
      const result = await api.submitOrder(request);
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
    } catch (error) {
      setTicket((current) => ({
        ...current,
        phase: "error",
        message: error instanceof Error && error.message ? error.message : "Order submission failed.",
        orderId: null
      }));
    }
  }, [activeSymbol, api, ticket]);

  return useMemo(
    () => buildQuickTradeTicketViewModel({
      activeSymbol,
      ticket,
      seedTicket,
      updateField,
      submitTicket,
      resetTicket
    }),
    [activeSymbol, resetTicket, seedTicket, submitTicket, ticket, updateField]
  );
}

export function buildQuickTradeTicketViewModel({
  activeSymbol,
  ticket,
  seedTicket,
  updateField,
  submitTicket,
  resetTicket
}: {
  activeSymbol: string | null;
  ticket: QuickTicketState;
  seedTicket: QuickTradeTicketViewModel["seedTicket"];
  updateField: QuickTradeTicketViewModel["updateField"];
  submitTicket: QuickTradeTicketViewModel["submitTicket"];
  resetTicket: QuickTradeTicketViewModel["resetTicket"];
}): QuickTradeTicketViewModel {
  const validation = validateQuickTicket(ticket);
  const submitting = ticket.phase === "submitting";
  const symbolLabel = activeSymbol ?? "selected symbol";
  const submitLabel = buildSubmitLabel(ticket, symbolLabel);
  const disabledReason = submitting
    ? "Order submission is already running."
    : activeSymbol === null
      ? "Select a symbol before submitting an order."
      : validation;

  return {
    ticket,
    submitting,
    priceDisabled: ticket.type === "Market",
    quantityInvalid: validation !== null && validation.toLowerCase().includes("quantity"),
    priceInvalid: validation !== null && validation.toLowerCase().includes("limit price"),
    sideToneClass: ticket.side === "Buy"
      ? "bg-positive/10 text-positive border-positive/30"
      : "bg-danger/10 text-danger border-danger/30",
    submitCommand: {
      label: submitLabel,
      ariaLabel: activeSymbol
        ? `Submit ${ticket.side.toLowerCase()} order for ${activeSymbol}`
        : "Submit order",
      disabled: disabledReason !== null,
      disabledReason,
      busy: submitting,
      busyLabel: "Submitting...",
      variant: ticket.side === "Buy" ? "default" : "destructive"
    },
    status: buildQuickTicketStatus(ticket, validation),
    seedTicket,
    updateField,
    submitTicket,
    resetTicket
  };
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

export function buildOrderRequest(symbol: string, ticket: QuickTicketForm): OrderSubmitRequest {
  return {
    symbol,
    side: ticket.side,
    type: ticket.type,
    quantity: Number(ticket.quantity),
    limitPrice: ticket.type === "Market" ? null : Number(ticket.limitPrice)
  };
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

export function formatTicketPrice(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return "";
  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 4,
    useGrouping: false
  });
}

export function formatMarketPrice(value: number | null | undefined, fractionDigits = 4): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return "-";
  }
  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: fractionDigits
  });
}

export function formatMarketSize(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return "-";
  }
  return value.toLocaleString();
}

export function formatMarketTime(iso: string | null | undefined): string {
  if (!iso) return "-";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "-";
  return date.toLocaleTimeString(undefined, { hour12: false }) + "." + String(date.getMilliseconds()).padStart(3, "0");
}

function buildBboPanels(symbol: string, quoteRow: QuotesResponse["quote"] | null): LiveQuotesBboPanelViewModel[] {
  if (!quoteRow) {
    return [];
  }

  return [
    {
      id: "bid",
      label: "Bid",
      price: quoteRow.bidPrice,
      priceLabel: formatMarketPrice(quoteRow.bidPrice),
      sizeLabel: `${formatMarketSize(quoteRow.bidSize)} shares`,
      seedSide: "Sell",
      seedLabel: `Sell ${symbol} at bid ${formatMarketPrice(quoteRow.bidPrice)}`,
      tone: "positive"
    },
    {
      id: "ask",
      label: "Ask",
      price: quoteRow.askPrice,
      priceLabel: formatMarketPrice(quoteRow.askPrice),
      sizeLabel: `${formatMarketSize(quoteRow.askSize)} shares`,
      seedSide: "Buy",
      seedLabel: `Buy ${symbol} at ask ${formatMarketPrice(quoteRow.askPrice)}`,
      tone: "negative"
    }
  ];
}

function buildQuoteMetrics(quoteRow: QuotesResponse["quote"] | null): LiveQuotesMetricRowViewModel[] {
  if (!quoteRow) {
    return [];
  }

  return [
    { id: "mid", label: "Mid", value: formatMarketPrice(quoteRow.midPrice) },
    { id: "spread", label: "Spread", value: formatMarketPrice(quoteRow.spread) },
    { id: "sequence", label: "Sequence", value: formatMarketSize(quoteRow.sequenceNumber) },
    { id: "stream", label: "Stream", value: quoteRow.streamId ?? "-" }
  ];
}

function buildDepthLevel(
  level: OrderBookLevelDto,
  maxSize: number,
  symbol: string,
  seedSide: "Buy" | "Sell"
): LiveQuotesDepthLevelViewModel {
  const priceLabel = formatMarketPrice(level.price);
  return {
    id: `${level.side.toLowerCase()}-${level.level}`,
    level: level.level,
    price: level.price,
    priceLabel,
    sizeLabel: formatMarketSize(level.size),
    barWidth: `${(level.size / maxSize) * 100}%`,
    seedLabel: `${seedSide} ${symbol} at ${priceLabel}`
  };
}

function buildTradeRow(trade: TradeDataResponse): LiveQuotesTradeRowViewModel {
  const aggressor = trade.aggressor?.toLowerCase();
  return {
    id: `${trade.sequenceNumber}-${trade.timestamp}`,
    timeLabel: formatMarketTime(trade.timestamp),
    priceLabel: formatMarketPrice(trade.price),
    sizeLabel: formatMarketSize(trade.size),
    aggressorLabel: trade.aggressor || "-",
    aggressorTone: aggressor === "buy" ? "positive" : aggressor === "sell" ? "negative" : "muted",
    venueLabel: trade.venue ?? "-"
  };
}

function buildPriceChartViewModel(
  symbol: string,
  metrics: IntradayMetrics,
  loading: boolean,
  error: string | null
): LiveQuotesPriceChartViewModel {
  const changeTone: LiveQuotesPriceChartViewModel["changeTone"] = metrics.change === null
    ? "default"
    : metrics.change > 0
      ? "positive"
      : metrics.change < 0
        ? "negative"
        : "default";
  const statusMessage = error && metrics.count === 0
    ? error
    : metrics.count === 0
      ? loading
        ? `Waiting for prints from ${symbol}...`
        : `No recent prints available for ${symbol}.`
      : null;
  const ariaLabel = `Recent ${symbol} trade prices, ranging from ${formatMarketPrice(metrics.low)} to ${formatMarketPrice(metrics.high)}.`;
  const strokeToken = chartStrokeTokenForTone(changeTone);

  return {
    title: `${symbol} prints ${formatWindowSpan(metrics.windowStart, metrics.windowEnd)}`,
    description: `Last ${metrics.count} trades streamed from the live pipeline. Chart shows trade-by-trade price; not a fixed-interval candle.`,
    lastPriceLabel: formatMarketPrice(metrics.last),
    changeLabel: `${formatChange(metrics.change)} (${formatChangePct(metrics.changePct)})`,
    changeTone,
    strokeTone: changeTone,
    strokeToken,
    statusMessage,
    statusRole: error && metrics.count === 0 ? "alert" : "status",
    stats: [
      { id: "open", label: "Open", value: formatMarketPrice(metrics.open) },
      { id: "high", label: "High", value: formatMarketPrice(metrics.high) },
      { id: "low", label: "Low", value: formatMarketPrice(metrics.low) },
      { id: "vwap", label: "VWAP", value: formatMarketPrice(metrics.vwap) },
      { id: "volume", label: "Volume", value: formatVolume(metrics.volume) }
    ],
    ariaLabel,
    sparkline: buildPriceSparklineViewModel(metrics, strokeToken, ariaLabel)
  };
}

export function buildPriceSparklineViewModel(
  metrics: IntradayMetrics,
  strokeToken: string,
  ariaLabel: string
): LiveQuotesSparklineViewModel | null {
  const width = 800;
  const height = 180;
  const padX = 8;
  const padY = 14;
  const { series, high, low } = metrics;

  if (series.length === 0 || high === null || low === null) {
    return null;
  }

  const minTs = series[0]!.ts;
  const maxTs = series[series.length - 1]!.ts;
  const tsSpan = Math.max(1, maxTs - minTs);
  const priceSpan = Math.max(high - low, Math.max(high * 0.0005, 0.01));
  const xFor = (ts: number) => padX + ((ts - minTs) / tsSpan) * (width - padX * 2);
  const yFor = (price: number) => padY + (1 - (price - low) / priceSpan) * (height - padY * 2);
  const pointFor = (point: { ts: number; price: number }): LiveQuotesSparklinePointViewModel => ({
    x: xFor(point.ts).toFixed(2),
    y: yFor(point.price).toFixed(2)
  });
  const points = series.map(pointFor);
  const lastPoint = points[points.length - 1]!;
  const baseY = (height - padY).toFixed(2);
  const areaSegments = [`M ${points[0]!.x} ${baseY}`];

  for (const point of points) {
    areaSegments.push(`L ${point.x} ${point.y}`);
  }
  areaSegments.push(`L ${lastPoint.x} ${baseY} Z`);

  return {
    viewBox: `0 0 ${width} ${height}`,
    points: points.map((point) => `${point.x},${point.y}`).join(" "),
    areaPath: areaSegments.join(" "),
    lastPoint,
    guideStartX: padX.toFixed(2),
    guideEndX: (width - padX).toFixed(2),
    highGuideY: yFor(high).toFixed(2),
    lowGuideY: yFor(low).toFixed(2),
    labelX: (width - padX).toFixed(2),
    highLabelY: Math.max(yFor(high) - 4, 12).toFixed(2),
    lowLabelY: Math.min(yFor(low) + 12, height - 4).toFixed(2),
    highLabel: formatMarketPrice(high),
    lowLabel: formatMarketPrice(low),
    strokeToken,
    ariaLabel
  };
}

function chartStrokeTokenForTone(tone: LiveQuotesPriceChartViewModel["strokeTone"]): string {
  switch (tone) {
    case "positive":
      return "var(--chart-up)";
    case "negative":
      return "var(--chart-dn)";
    default:
      return "var(--chart-bench)";
  }
}

function buildPanelState({
  loading,
  error,
  ready,
  emptyMessage,
  loadingMessage
}: {
  loading: boolean;
  error: string | null;
  ready: boolean;
  emptyMessage: string;
  loadingMessage: string;
}): LiveQuotesPanelState {
  if (loading && !ready) {
    return {
      status: "loading",
      message: loadingMessage,
      role: "status",
      toneClass: "border-primary/25 bg-primary/10 text-primary",
      showData: false
    };
  }

  if (error && ready) {
    return {
      status: "warning",
      message: error,
      role: "alert",
      toneClass: "border-warning/35 bg-warning/10 text-warning",
      showData: true
    };
  }

  if (error) {
    return {
      status: "error",
      message: error,
      role: "alert",
      toneClass: "border-danger/30 bg-danger/10 text-danger",
      showData: false
    };
  }

  if (!ready) {
    return {
      status: "empty",
      message: emptyMessage,
      role: "status",
      toneClass: "border-border/70 bg-secondary/25 text-muted-foreground",
      showData: false
    };
  }

  return {
    status: "ready",
    message: null,
    role: "status",
    toneClass: "border-success/30 bg-success/10 text-success",
    showData: true
  };
}

function formatMarketTimestamp(iso: string | null | undefined): string {
  if (!iso) return "Unavailable";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "Unavailable";
  return date.toLocaleTimeString(undefined, { hour12: false }) + "." + String(date.getMilliseconds()).padStart(3, "0");
}

function formatChange(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return "-";
  const sign = value > 0 ? "+" : value < 0 ? "" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 })}`;
}

function formatChangePct(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return "-";
  const sign = value > 0 ? "+" : value < 0 ? "" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%`;
}

function formatVolume(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return "-";
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

function buildSubmitLabel(ticket: QuickTicketState, symbol: string): string {
  if (ticket.phase === "submitting") {
    return "Submitting...";
  }

  return `${ticket.side} ${symbol}${ticket.type === "Limit" && ticket.limitPrice ? ` @ ${ticket.limitPrice}` : ""}`;
}

function buildQuickTicketStatus(ticket: QuickTicketState, validation: string | null): QuickTicketStatusViewModel {
  if (ticket.phase === "submitted" && ticket.message) {
    return {
      id: "quick-ticket-status",
      role: "status",
      tone: "success",
      message: ticket.message,
      showSuccessIcon: true,
      showErrorIcon: false
    };
  }

  if (ticket.phase === "error" && ticket.message) {
    return {
      id: "quick-ticket-status",
      role: "alert",
      tone: "danger",
      message: ticket.message,
      showSuccessIcon: false,
      showErrorIcon: true
    };
  }

  return {
    id: "quick-ticket-status",
    role: validation ? "alert" : "status",
    tone: validation ? "danger" : "default",
    message: validation ?? "Orders route through Meridian's pre-trade risk and execution controls.",
    showSuccessIcon: false,
    showErrorIcon: validation !== null
  };
}

function buildLiveQuoteSymbolLookupStatus({
  activeSymbol,
  normalizedSymbol,
  inputInvalid
}: {
  activeSymbol: string | null;
  normalizedSymbol: string;
  inputInvalid: boolean;
}): LiveQuoteSymbolLookupStatusViewModel {
  if (inputInvalid) {
    return {
      id: "live-quote-symbol-status",
      role: "alert",
      message: "Enter a symbol before loading live market data.",
      toneClass: "text-danger"
    };
  }

  if (normalizedSymbol && normalizedSymbol !== activeSymbol) {
    return {
      id: "live-quote-symbol-status",
      role: "status",
      message: `Ready to load ${normalizedSymbol}.`,
      toneClass: "text-muted-foreground"
    };
  }

  if (activeSymbol) {
    return {
      id: "live-quote-symbol-status",
      role: "status",
      message: `${activeSymbol} live market panels are active.`,
      toneClass: "text-muted-foreground"
    };
  }

  return {
    id: "live-quote-symbol-status",
    role: "status",
    message: "Enter a symbol to load live BBO, recent trades, and L2 depth.",
    toneClass: "text-muted-foreground"
  };
}
