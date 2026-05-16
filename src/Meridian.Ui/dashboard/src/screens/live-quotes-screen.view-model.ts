import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import type { ApiRequestOptions } from "@/lib/api";
import { workflowTargetPath } from "@/lib/workspace";
import type {
  OrderBookLevelDto,
  OrderBookResponse,
  OrderResult,
  OrderSubmitRequest,
  QuotesResponse,
  SessionStatsDto,
  TradeDataResponse,
  TradesResponse
} from "@/types";

export const LIVE_QUOTES_POLL_INTERVAL_MS = 2000;
export const LIVE_QUOTES_TRADE_HISTORY_LIMIT = 200;
export const LIVE_QUOTES_TRADE_TABLE_LIMIT = 25;
export const LIVE_QUOTES_EMPTY_VALUE = "—";

export type QuickTicketPhase = "idle" | "seeded" | "submitting" | "submitted" | "error";

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
  validationVisible?: boolean;
  acknowledged: boolean;
}

export interface QuickTicketStatusViewModel {
  id: string;
  role: "status" | "alert";
  tone: "default" | "success" | "danger";
  message: string;
  showSuccessIcon: boolean;
  showErrorIcon: boolean;
  actions: QuickTicketStatusActionViewModel[];
}

export interface QuickTicketStatusActionViewModel {
  id: string;
  label: string;
  href: string;
  ariaLabel: string;
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

export interface QuickTicketReviewAcknowledgementViewModel {
  id: string;
  label: string;
  description: string;
  checked: boolean;
  disabled: boolean;
  disabledReason: string | null;
}

export type QuickTicketField = "side" | "type" | "quantity" | "limitPrice";

export interface QuickTicketFieldViewModel {
  field: QuickTicketField;
  id: string;
  label: string;
  ariaLabel: string;
  placeholder: string | null;
  describedBy: string;
  inputMode: "numeric" | "decimal" | null;
  min: number | null;
  step: number | string | null;
  disabled: boolean;
  disabledReason: string | null;
}

export interface QuickTradeTicketViewModel {
  ticket: QuickTicketState;
  formLabel: string;
  fields: Record<QuickTicketField, QuickTicketFieldViewModel>;
  submitting: boolean;
  priceDisabled: boolean;
  quantityInvalid: boolean;
  priceInvalid: boolean;
  sideToneClass: string;
  reviewAcknowledgement: QuickTicketReviewAcknowledgementViewModel;
  submitCommand: QuickTicketCommandViewModel;
  status: QuickTicketStatusViewModel;
  seedTicket: (side: "Buy" | "Sell", price: number) => void;
  updateField: <K extends keyof QuickTicketForm>(field: K, value: QuickTicketForm[K]) => void;
  setReviewAcknowledged: (value: boolean) => void;
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
  side: "bid" | "ask";
  sideLabel: string;
  level: number;
  price: number;
  priceLabel: string;
  sizeLabel: string;
  barWidth: string;
  seedLabel: string;
  selectLabel: string;
  detailPanelId: string;
  expanded: boolean;
  tone: "positive" | "negative";
}

export interface LiveQuotesDepthLadderViewModel {
  bids: LiveQuotesDepthLevelViewModel[];
  asks: LiveQuotesDepthLevelViewModel[];
  selectedLevelId: string | null;
  selectedDetail: LiveQuotesDepthLevelDetailViewModel | null;
  selectLevel: (id: string) => void;
  detailPanelId: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  tableLabel: string;
  caption: string;
}

export interface LiveQuotesDepthLevelDetailField {
  label: string;
  value: string;
}

export interface LiveQuotesDepthLevelDetailViewModel {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusBadgeVariant: "success" | "warning";
  ariaLabel: string;
  fields: LiveQuotesDepthLevelDetailField[];
}

export interface LiveQuotesTradeRowViewModel {
  id: string;
  timeLabel: string;
  priceLabel: string;
  sizeLabel: string;
  aggressorLabel: string;
  aggressorTone: "positive" | "negative" | "muted";
  venueLabel: string;
  detailPanelId: string;
  expanded: boolean;
  ariaLabel: string;
  selectAriaLabel: string;
}

export interface LiveQuotesTradeDetailField {
  label: string;
  value: string;
}

export interface LiveQuotesTradeDetailViewModel {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusBadgeVariant: "outline" | "success" | "warning";
  ariaLabel: string;
  fields: LiveQuotesTradeDetailField[];
}

export interface LiveQuotesSessionStatCellViewModel {
  id: string;
  label: string;
  value: string;
}

export interface LiveQuotesSessionStatsViewModel {
  id: string;
  ariaLabel: string;
  descriptionId: string;
  description: string;
  periodLabel: string;
  dateLabel: string;
  changeLabel: string;
  changeAriaLabel: string;
  changeTone: "positive" | "negative" | "default";
  stats: LiveQuotesSessionStatCellViewModel[];
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
  selectedTradeId: string | null;
  selectedTradeDetail: LiveQuotesTradeDetailViewModel | null;
  selectTrade: (id: string) => void;
  intraday: IntradayMetrics;
  bboPanels: LiveQuotesBboPanelViewModel[];
  quoteMetrics: LiveQuotesMetricRowViewModel[];
  depthLadder: LiveQuotesDepthLadderViewModel;
  priceChart: LiveQuotesPriceChartViewModel;
  sessionStats: LiveQuotesSessionStatsViewModel | null;
  tradesTableLabel: string;
  tradesTableCaption: string;
  tradesDetailPanelId: string;
  tradesDetailEmptyTitle: string;
  tradesDetailEmptyText: string;
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
  getLiveQuote: (symbol: string, options?: ApiRequestOptions) => Promise<QuotesResponse>;
  getLiveTrades: (symbol: string, limit: number, options?: ApiRequestOptions) => Promise<TradesResponse>;
  getLiveOrderbook: (symbol: string, depth: number, options?: ApiRequestOptions) => Promise<OrderBookResponse>;
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
  orderId: null,
  validationVisible: false,
  acknowledged: false
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
  const [selectedTradeId, setSelectedTradeId] = useState<string | null>(null);
  const [selectedDepthLevelId, setSelectedDepthLevelId] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const requestIdRef = useRef(0);
  const inFlightSymbolRef = useRef<string | null>(null);
  const mountedRef = useRef(true);
  const marketAbortRef = useRef<AbortController | null>(null);
  const quickTrade = useQuickTradeTicket(activeSymbol, { submitOrder: api.submitOrder });

  useEffect(() => {
    mountedRef.current = true;

    return () => {
      mountedRef.current = false;
      requestIdRef.current += 1;
      inFlightSymbolRef.current = null;
      marketAbortRef.current?.abort();
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
    marketAbortRef.current?.abort();
    const controller = new AbortController();
    marketAbortRef.current = controller;
    const requestOptions = { signal: controller.signal };
    inFlightSymbolRef.current = requestedSymbol;
    setRefreshing(true);

    try {
      const [quoteResult, tradesResult, orderbookResult] = await Promise.allSettled([
        api.getLiveQuote(requestedSymbol, requestOptions),
        api.getLiveTrades(requestedSymbol, LIVE_QUOTES_TRADE_HISTORY_LIMIT, requestOptions),
        api.getLiveOrderbook(requestedSymbol, 10, requestOptions)
      ]);

      if (!mountedRef.current || requestIdRef.current !== requestId) {
        return;
      }

      setQuote((current) => mergeLiveQuotesLoadState(quoteResult, current, "Failed to load quote"));
      setTrades((current) => mergeLiveQuotesLoadState(tradesResult, current, "Failed to load trades"));
      setOrderbook((current) => mergeLiveQuotesLoadState(orderbookResult, current, "Failed to load order book"));
    } finally {
      if (mountedRef.current && requestIdRef.current === requestId) {
        if (marketAbortRef.current === controller) {
          marketAbortRef.current = null;
        }
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
    setSelectedTradeId(null);
    setSelectedDepthLevelId(null);
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
    selectedTradeId,
    selectTrade: setSelectedTradeId,
    selectedDepthLevelId,
    selectDepthLevel: setSelectedDepthLevelId,
    tradeTableLimit: LIVE_QUOTES_TRADE_TABLE_LIMIT
  }), [activeSymbol, orderbook, quote, refreshing, selectedDepthLevelId, selectedTradeId, trades]);

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
    setSelectedDepthLevelId(null);
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
  selectedTradeId = null,
  selectTrade = () => {},
  selectedDepthLevelId = null,
  selectDepthLevel = () => {},
  tradeTableLimit
}: {
  activeSymbol: string | null;
  quote: LiveQuotesLoadState<QuotesResponse>;
  trades: LiveQuotesLoadState<TradesResponse>;
  orderbook: LiveQuotesLoadState<OrderBookResponse>;
  refreshing: boolean;
  selectedTradeId?: string | null;
  selectTrade?: (id: string) => void;
  selectedDepthLevelId?: string | null;
  selectDepthLevel?: (id: string) => void;
  tradeTableLimit: number;
}): LiveQuotesMarketDataViewModel {
  const quoteRow = quote.data?.quote ?? null;
  const tradeHistory = trades.data?.trades ?? [];
  const tradeRows = tradeHistory.slice(0, tradeTableLimit);
  const tradeDetailPanelId = "live-quotes-trade-detail";
  const baseTradeRows = tradeRows.map((trade) => buildTradeRow(trade, tradeDetailPanelId));
  const stableSelectedTradeId = baseTradeRows.some((row) => row.id === selectedTradeId)
    ? selectedTradeId
    : baseTradeRows[0]?.id ?? null;
  const selectedTrade = tradeRows.find((trade) => liveQuoteTradeId(trade) === stableSelectedTradeId) ?? null;
  const tradeDisplayRows = baseTradeRows.map((row) => ({
    ...row,
    expanded: row.id === stableSelectedTradeId
  }));
  const intraday = computeIntradayMetrics(tradeHistory);
  const hasOrderbookRows = (orderbook.data?.bids.length ?? 0) > 0 || (orderbook.data?.asks.length ?? 0) > 0;
  const symbol = activeSymbol ?? "selected symbol";
  const maxDepthSize = Math.max(
    1,
    ...(orderbook.data?.bids.map((level) => level.size) ?? []),
    ...(orderbook.data?.asks.map((level) => level.size) ?? [])
  );
  const depthDetailPanelId = "live-quotes-depth-level-detail";
  const baseBidLevels = (orderbook.data?.bids ?? []).map((level) => buildDepthLevel(level, maxDepthSize, symbol, "Sell", depthDetailPanelId));
  const baseAskLevels = (orderbook.data?.asks ?? []).map((level) => buildDepthLevel(level, maxDepthSize, symbol, "Buy", depthDetailPanelId));
  const allDepthLevels = [...baseBidLevels, ...baseAskLevels];
  const stableSelectedDepthLevelId = allDepthLevels.some((level) => level.id === selectedDepthLevelId)
    ? selectedDepthLevelId
    : allDepthLevels[0]?.id ?? null;
  const selectedDepthLevel = allDepthLevels.find((level) => level.id === stableSelectedDepthLevelId) ?? null;
  const markDepthLevelSelection = (level: LiveQuotesDepthLevelViewModel): LiveQuotesDepthLevelViewModel => ({
    ...level,
    expanded: level.id === stableSelectedDepthLevelId
  });

  return {
    activeSymbol,
    quoteRow,
    orderbook: orderbook.data,
    tradeHistory,
    tradeRows,
    tradeDisplayRows,
    selectedTradeId: stableSelectedTradeId,
    selectedTradeDetail: selectedTrade ? buildTradeDetail(selectedTrade) : null,
    selectTrade,
    intraday,
    bboPanels: buildBboPanels(symbol, quoteRow),
    quoteMetrics: buildQuoteMetrics(quoteRow),
    depthLadder: {
      bids: baseBidLevels.map(markDepthLevelSelection),
      asks: baseAskLevels.map(markDepthLevelSelection),
      selectedLevelId: stableSelectedDepthLevelId,
      selectedDetail: selectedDepthLevel ? buildDepthLevelDetail(selectedDepthLevel, orderbook.data) : null,
      selectLevel: selectDepthLevel,
      detailPanelId: depthDetailPanelId,
      detailEmptyTitle: "No depth level selected",
      detailEmptyText: allDepthLevels.length > 0
        ? "Select a bid or ask level to inspect depth evidence."
        : `No depth levels are available for ${symbol}.`,
      tableLabel: `${symbol} order book depth ladder`,
      caption: `Select a ${symbol} bid or ask level to seed the ticket and inspect venue, sequence, and depth evidence.`
    },
    priceChart: buildPriceChartViewModel(symbol, intraday, refreshing, trades.error),
    sessionStats: buildLiveQuotesSessionStatsViewModel(symbol, quoteRow?.session ?? null),
    tradesTableLabel: `Recent ${symbol} trade prints`,
    tradesTableCaption: `Select a ${symbol} trade print to inspect sequence, stream, and venue evidence.`,
    tradesDetailPanelId: tradeDetailPanelId,
    tradesDetailEmptyTitle: "No trade selected",
    tradesDetailEmptyText: tradeRows.length > 0
      ? "Select a trade print to inspect tape evidence."
      : `No recent trades for ${symbol}.`,
    venueLabel: quoteRow?.venue ?? orderbook.data?.venue ?? null,
    stale: orderbook.data?.isStale === true,
    lastUpdateLabel: formatMarketTimestamp(quoteRow?.timestamp ?? orderbook.data?.timestamp ?? null),
    quoteState: buildPanelState({
      loading: refreshing && !quote.data && !quote.error,
      error: quote.error,
      ready: quoteRow !== null,
      emptyMessage: `No quote data available for ${symbol}.`,
      loadingMessage: `Loading quote data for ${symbol}…`
    }),
    orderbookState: buildPanelState({
      loading: refreshing && !orderbook.data && !orderbook.error,
      error: orderbook.error,
      ready: hasOrderbookRows,
      emptyMessage: `No depth data available for ${symbol}.`,
      loadingMessage: `Loading depth for ${symbol}…`
    }),
    tradesState: buildPanelState({
      loading: refreshing && !trades.data && !trades.error,
      error: trades.error,
      ready: tradeRows.length > 0,
      emptyMessage: `No recent trades for ${symbol}.`,
      loadingMessage: `Loading recent trades for ${symbol}…`
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
  const activeSymbolRef = useRef(activeSymbol);
  const submitRevisionRef = useRef(0);

  activeSymbolRef.current = activeSymbol;

  const resetTicket = useCallback(() => {
    submitRevisionRef.current += 1;
    setTicket(initialQuickTicketState);
  }, []);

  useEffect(() => {
    submitRevisionRef.current += 1;
    setTicket(initialQuickTicketState);
  }, [activeSymbol]);

  useEffect(() => () => {
    submitRevisionRef.current += 1;
  }, []);

  const seedTicket = useCallback((side: "Buy" | "Sell", price: number) => {
    const priceLabel = formatTicketPrice(price);
    const symbolLabel = activeSymbolRef.current ?? "selected symbol";
    setTicket((current) => ({
      ...current,
      side,
      type: "Limit",
      limitPrice: priceLabel,
      phase: "seeded",
      message: buildQuickTicketSeededMessage(symbolLabel, side, priceLabel),
      orderId: null,
      validationVisible: false,
      acknowledged: false
    }));
  }, []);

  const updateField = useCallback(<K extends keyof QuickTicketForm>(field: K, value: QuickTicketForm[K]) => {
    setTicket((current) => ({
      ...current,
      [field]: value,
      phase: resetQuickTicketFeedbackPhase(current.phase),
      message: shouldClearQuickTicketFeedbackMessage(current.phase) ? null : current.message,
      validationVisible: true,
      acknowledged: false
    }));
  }, []);

  const setReviewAcknowledged = useCallback((value: boolean) => {
    setTicket((current) => ({
      ...current,
      acknowledged: value,
      phase: value ? "idle" : resetQuickTicketFeedbackPhase(current.phase),
      message: value || shouldClearQuickTicketFeedbackMessage(current.phase) ? null : current.message
    }));
  }, []);

  const submitTicket = useCallback(async (event: FormEvent) => {
    event.preventDefault();
    const submitSymbol = activeSymbol;
    if (!submitSymbol) {
      return;
    }

    const validation = validateQuickTicket(ticket);
    if (validation) {
      setTicket((current) => ({
        ...current,
        phase: "error",
        message: validation,
        orderId: null,
        validationVisible: true
      }));
      return;
    }

    if (!ticket.acknowledged) {
      setTicket((current) => ({
        ...current,
        phase: "error",
        message: "Review and acknowledge the ticket before submitting.",
        orderId: null,
        validationVisible: false
      }));
      return;
    }

    const request = buildOrderRequest(submitSymbol, ticket);
    const submitRevision = submitRevisionRef.current + 1;
    submitRevisionRef.current = submitRevision;
    const applyCurrentSubmission = (update: (current: QuickTicketState) => QuickTicketState) => {
      if (submitRevisionRef.current === submitRevision && activeSymbolRef.current === submitSymbol) {
        setTicket(update);
      }
    };

    setTicket((current) => ({ ...current, phase: "submitting", message: null, orderId: null }));
    try {
      const result = await api.submitOrder(request);
      if (result.success) {
        applyCurrentSubmission((current) => ({
          ...current,
          phase: "submitted",
          message: result.orderId ? `Order ${result.orderId} accepted.` : "Order accepted.",
          orderId: result.orderId,
          validationVisible: false,
          acknowledged: false
        }));
      } else {
        applyCurrentSubmission((current) => ({
          ...current,
          phase: "error",
          message: result.reason ?? "Order rejected.",
          orderId: null,
          validationVisible: false,
          acknowledged: false
        }));
      }
    } catch (error) {
      applyCurrentSubmission((current) => ({
        ...current,
        phase: "error",
        message: error instanceof Error && error.message ? error.message : "Order submission failed.",
        orderId: null,
        validationVisible: false,
        acknowledged: false
      }));
    }
  }, [activeSymbol, api, ticket]);

  return useMemo(
    () => buildQuickTradeTicketViewModel({
      activeSymbol,
      ticket,
      seedTicket,
      updateField,
      setReviewAcknowledged,
      submitTicket,
      resetTicket
    }),
    [activeSymbol, resetTicket, seedTicket, setReviewAcknowledged, submitTicket, ticket, updateField]
  );
}

export function buildQuickTradeTicketViewModel({
  activeSymbol,
  ticket,
  seedTicket,
  updateField,
  setReviewAcknowledged,
  submitTicket,
  resetTicket
}: {
  activeSymbol: string | null;
  ticket: QuickTicketState;
  seedTicket: QuickTradeTicketViewModel["seedTicket"];
  updateField: QuickTradeTicketViewModel["updateField"];
  setReviewAcknowledged: QuickTradeTicketViewModel["setReviewAcknowledged"];
  submitTicket: QuickTradeTicketViewModel["submitTicket"];
  resetTicket: QuickTradeTicketViewModel["resetTicket"];
}): QuickTradeTicketViewModel {
  const validation = validateQuickTicket(ticket);
  const submitting = ticket.phase === "submitting";
  const surfaceValidation = shouldSurfaceQuickTicketValidation(ticket, validation);
  const symbolLabel = activeSymbol ?? "selected symbol";
  const submitLabel = buildSubmitLabel(ticket, symbolLabel);
  const statusId = "quick-ticket-status";
  const disabledReason = submitting
    ? "Order submission is already running."
    : activeSymbol === null
      ? "Select a symbol before submitting an order."
      : validation ?? (ticket.acknowledged ? null : "Review and acknowledge the ticket before submitting.");

  return {
    ticket,
    formLabel: `Quick trade ticket for ${symbolLabel}`,
    fields: buildQuickTicketFields(ticket, statusId, submitting),
    submitting,
    priceDisabled: submitting || ticket.type === "Market",
    quantityInvalid: surfaceValidation && validation !== null && validation.toLowerCase().includes("quantity"),
    priceInvalid: surfaceValidation && validation !== null && validation.toLowerCase().includes("limit price"),
    sideToneClass: ticket.side === "Buy"
      ? "bg-positive/10 text-positive border-positive/30"
      : "bg-danger/10 text-danger border-danger/30",
    reviewAcknowledgement: buildQuickTicketReviewAcknowledgement({
      activeSymbol,
      ticket,
      validation,
      submitting
    }),
    submitCommand: {
      label: submitLabel,
      ariaLabel: activeSymbol
        ? `Submit ${ticket.side.toLowerCase()} order for ${activeSymbol}`
        : "Submit order",
      disabled: disabledReason !== null,
      disabledReason,
      busy: submitting,
      busyLabel: "Submitting…",
      variant: ticket.side === "Buy" ? "default" : "destructive"
    },
    status: buildQuickTicketStatus(ticket, validation, surfaceValidation, activeSymbol),
    seedTicket,
    updateField,
    setReviewAcknowledged,
    submitTicket,
    resetTicket
  };
}

export function buildQuickTicketFields(
  ticket: QuickTicketForm,
  statusId = "quick-ticket-status",
  submitting = false
): Record<QuickTicketField, QuickTicketFieldViewModel> {
  const submittingReason = submitting
    ? "Order submission is in progress; wait before editing the ticket."
    : null;

  return {
    side: {
      field: "side",
      id: "quick-ticket-side",
      label: "Side",
      ariaLabel: "Order side",
      placeholder: null,
      describedBy: statusId,
      inputMode: null,
      min: null,
      step: null,
      disabled: submitting,
      disabledReason: submittingReason
    },
    type: {
      field: "type",
      id: "quick-ticket-type",
      label: "Type",
      ariaLabel: "Order type",
      placeholder: null,
      describedBy: statusId,
      inputMode: null,
      min: null,
      step: null,
      disabled: submitting,
      disabledReason: submittingReason
    },
    quantity: {
      field: "quantity",
      id: "quick-ticket-quantity",
      label: "Quantity",
      ariaLabel: "Order quantity in shares",
      placeholder: "100",
      describedBy: statusId,
      inputMode: "numeric",
      min: 1,
      step: 1,
      disabled: submitting,
      disabledReason: submittingReason
    },
    limitPrice: {
      field: "limitPrice",
      id: "quick-ticket-price",
      label: ticket.type === "Market" ? "Price (market)" : "Limit price",
      ariaLabel: ticket.type === "Market" ? "Market order price" : "Limit price",
      placeholder: ticket.type === "Market" ? "Best available" : "0.00",
      describedBy: statusId,
      inputMode: "decimal",
      min: 0,
      step: "0.01",
      disabled: submitting || ticket.type === "Market",
      disabledReason: submittingReason ?? (ticket.type === "Market"
        ? "Market orders route at the best available price."
        : null)
    }
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

function buildQuickTicketReviewAcknowledgement({
  activeSymbol,
  ticket,
  validation,
  submitting
}: {
  activeSymbol: string | null;
  ticket: QuickTicketState;
  validation: string | null;
  submitting: boolean;
}): QuickTicketReviewAcknowledgementViewModel {
  const disabledReason = submitting
    ? "Order submission is in progress."
    : activeSymbol === null
      ? "Select a symbol before acknowledging the ticket."
      : validation;
  const symbolLabel = activeSymbol ?? "selected symbol";
  const orderDescription = validation
    ? "Complete the required ticket fields before acknowledging the order."
    : `${ticket.side} ${ticket.quantity} ${symbolLabel} as a ${ticket.type.toLowerCase()} order${ticket.type === "Limit" ? ` at ${ticket.limitPrice}` : " at market"}.`;

  return {
    id: "quick-ticket-review-acknowledgement",
    label: "I reviewed this order ticket",
    description: orderDescription,
    checked: ticket.acknowledged,
    disabled: disabledReason !== null,
    disabledReason
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
    return LIVE_QUOTES_EMPTY_VALUE;
  }
  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: fractionDigits
  });
}

export function formatMarketSize(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return LIVE_QUOTES_EMPTY_VALUE;
  }
  return value.toLocaleString();
}

export function formatMarketTime(iso: string | null | undefined): string {
  if (!iso) return LIVE_QUOTES_EMPTY_VALUE;
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return LIVE_QUOTES_EMPTY_VALUE;
  return formatUtcTimeWithMilliseconds(date);
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
    { id: "stream", label: "Stream", value: quoteRow.streamId ?? LIVE_QUOTES_EMPTY_VALUE }
  ];
}

export function buildLiveQuotesSessionStatsViewModel(
  symbol: string,
  session: SessionStatsDto | null | undefined
): LiveQuotesSessionStatsViewModel | null {
  if (!session) {
    return null;
  }

  const changeTone: LiveQuotesSessionStatsViewModel["changeTone"] = session.change > 0
    ? "positive"
    : session.change < 0
      ? "negative"
      : "default";
  const changeLabel = `${formatChange(session.change)} (${formatChangePct(session.changePercent)})`;
  const volumeLabel = formatVolume(session.volume);

  return {
    id: "live-quotes-session-stats",
    ariaLabel: `${symbol} session statistics`,
    descriptionId: "live-quotes-session-stats-description",
    description: `Session ${session.sessionDate} quote evidence from ${formatMarketTimestamp(session.firstTradeAt)} to ${formatMarketTimestamp(session.lastTradeAt)}.`,
    periodLabel: "Today",
    dateLabel: `Session ${session.sessionDate}`,
    changeLabel,
    changeAriaLabel: `Day change ${changeLabel}`,
    changeTone,
    stats: [
      { id: "open", label: "Open", value: formatMarketPrice(session.open) },
      { id: "high", label: "High", value: formatMarketPrice(session.high) },
      { id: "low", label: "Low", value: formatMarketPrice(session.low) },
      { id: "vwap", label: "VWAP", value: formatMarketPrice(session.vwap) },
      { id: "volume", label: "Volume", value: volumeLabel }
    ]
  };
}

function buildDepthLevel(
  level: OrderBookLevelDto,
  maxSize: number,
  symbol: string,
  seedSide: "Buy" | "Sell",
  detailPanelId: string
): LiveQuotesDepthLevelViewModel {
  const priceLabel = formatMarketPrice(level.price);
  const side = level.side.toLowerCase() === "ask" ? "ask" : "bid";
  const sideLabel = side === "ask" ? "Ask" : "Bid";
  return {
    id: `${level.side.toLowerCase()}-${level.level}`,
    side,
    sideLabel,
    level: level.level,
    price: level.price,
    priceLabel,
    sizeLabel: formatMarketSize(level.size),
    barWidth: `${(level.size / maxSize) * 100}%`,
    seedLabel: `${seedSide} ${symbol} at ${priceLabel}`,
    selectLabel: `Inspect ${symbol} ${sideLabel.toLowerCase()} level ${level.level} at ${priceLabel}; ${seedSide} ${symbol} at ${priceLabel}`,
    detailPanelId,
    expanded: false,
    tone: side === "bid" ? "positive" : "negative"
  };
}

function buildDepthLevelDetail(
  level: LiveQuotesDepthLevelViewModel,
  orderbook: OrderBookResponse | null
): LiveQuotesDepthLevelDetailViewModel {
  const venueLabel = orderbook?.venue ?? "Unreported";
  const streamLabel = orderbook?.streamId ?? "Unreported";
  const sequenceLabel = orderbook ? formatMarketSize(orderbook.sequenceNumber) : LIVE_QUOTES_EMPTY_VALUE;
  const timestampLabel = formatMarketTimestamp(orderbook?.timestamp ?? null);
  const marketStateLabel = orderbook?.marketState ?? "Unreported";
  const imbalanceLabel = orderbook?.imbalance === null || orderbook?.imbalance === undefined
    ? LIVE_QUOTES_EMPTY_VALUE
    : formatChange(orderbook.imbalance);
  const midLabel = formatMarketPrice(orderbook?.midPrice);

  return {
    id: level.id,
    eyebrow: "Selected depth level",
    title: `${level.sideLabel} level ${level.level} @ ${level.priceLabel}`,
    subtitle: `${venueLabel} · ${marketStateLabel}`,
    description: `${level.sizeLabel} shares are visible at ${level.priceLabel}. Selecting this level seeds a ${level.side === "bid" ? "sell" : "buy"} limit ticket.`,
    statusLabel: level.sideLabel,
    statusBadgeVariant: level.side === "bid" ? "success" : "warning",
    ariaLabel: `${level.sideLabel} level ${level.level} detail`,
    fields: [
      { label: "Price", value: level.priceLabel },
      { label: "Size", value: level.sizeLabel },
      { label: "Level", value: String(level.level) },
      { label: "Venue", value: venueLabel },
      { label: "Sequence", value: sequenceLabel },
      { label: "Stream", value: streamLabel },
      { label: "Mid", value: midLabel },
      { label: "Imbalance", value: imbalanceLabel },
      { label: "Timestamp", value: timestampLabel }
    ]
  };
}

function liveQuoteTradeId(trade: TradeDataResponse): string {
  return `${trade.sequenceNumber}-${trade.timestamp}`;
}

function buildTradeRow(trade: TradeDataResponse, detailPanelId: string): LiveQuotesTradeRowViewModel {
  const aggressor = trade.aggressor?.toLowerCase();
  const id = liveQuoteTradeId(trade);
  const priceLabel = formatMarketPrice(trade.price);
  const sizeLabel = formatMarketSize(trade.size);
  const aggressorLabel = trade.aggressor || "Unreported";
  const venueLabel = trade.venue ?? "Unreported";
  return {
    id,
    timeLabel: formatMarketTime(trade.timestamp),
    priceLabel,
    sizeLabel,
    aggressorLabel,
    aggressorTone: aggressor === "buy" ? "positive" : aggressor === "sell" ? "negative" : "muted",
    venueLabel,
    detailPanelId,
    expanded: false,
    ariaLabel: `${trade.symbol} trade ${trade.sequenceNumber}: ${sizeLabel} shares at ${priceLabel}, ${aggressorLabel} aggressor, venue ${venueLabel}`,
    selectAriaLabel: `Inspect ${trade.symbol} trade ${trade.sequenceNumber} at ${priceLabel}`
  };
}

function buildTradeDetail(trade: TradeDataResponse): LiveQuotesTradeDetailViewModel {
  const aggressor = trade.aggressor?.toLowerCase();
  const aggressorLabel = trade.aggressor || "Unreported";
  const venueLabel = trade.venue ?? "Unreported";
  const streamLabel = trade.streamId ?? "Unreported";
  const priceLabel = formatMarketPrice(trade.price);
  const sizeLabel = formatMarketSize(trade.size);
  const statusBadgeVariant: LiveQuotesTradeDetailViewModel["statusBadgeVariant"] =
    aggressor === "buy"
      ? "success"
      : aggressor === "sell"
        ? "warning"
        : "outline";

  return {
    id: liveQuoteTradeId(trade),
    eyebrow: "Selected trade",
    title: `${trade.symbol} print ${trade.sequenceNumber}`,
    subtitle: `${formatMarketTime(trade.timestamp)} · ${venueLabel}`,
    description: `${sizeLabel} shares printed at ${priceLabel}. Aggressor: ${aggressorLabel}.`,
    statusLabel: aggressorLabel,
    statusBadgeVariant,
    ariaLabel: `${trade.symbol} trade ${trade.sequenceNumber} detail`,
    fields: [
      { label: "Price", value: priceLabel },
      { label: "Size", value: sizeLabel },
      { label: "Sequence", value: String(trade.sequenceNumber) },
      { label: "Stream", value: streamLabel },
      { label: "Venue", value: venueLabel },
      { label: "Timestamp", value: formatMarketTimestamp(trade.timestamp) }
    ]
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
        ? `Waiting for prints from ${symbol}…`
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
  if (!iso) return LIVE_QUOTES_EMPTY_VALUE;
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return LIVE_QUOTES_EMPTY_VALUE;
  return formatUtcTimeWithMilliseconds(date);
}

function formatUtcTimeWithMilliseconds(date: Date): string {
  return [
    String(date.getUTCHours()).padStart(2, "0"),
    String(date.getUTCMinutes()).padStart(2, "0"),
    String(date.getUTCSeconds()).padStart(2, "0")
  ].join(":") + `.${String(date.getUTCMilliseconds()).padStart(3, "0")} UTC`;
}

function formatChange(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return LIVE_QUOTES_EMPTY_VALUE;
  const sign = value > 0 ? "+" : value < 0 ? "" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 })}`;
}

function formatChangePct(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return LIVE_QUOTES_EMPTY_VALUE;
  const sign = value > 0 ? "+" : value < 0 ? "" : "";
  return `${sign}${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%`;
}

function formatVolume(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return LIVE_QUOTES_EMPTY_VALUE;
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
    return "Submitting…";
  }

  return `${ticket.side} ${symbol}${ticket.type === "Limit" && ticket.limitPrice ? ` @ ${ticket.limitPrice}` : ""}`;
}

function buildQuickTicketStatus(
  ticket: QuickTicketState,
  validation: string | null,
  surfaceValidation: boolean,
  activeSymbol: string | null
): QuickTicketStatusViewModel {
  if (ticket.phase === "submitted" && ticket.message) {
    return {
      id: "quick-ticket-status",
      role: "status",
      tone: "success",
      message: ticket.message,
      showSuccessIcon: true,
      showErrorIcon: false,
      actions: [buildQuickTicketReadinessAction("accepted", activeSymbol, ticket.orderId)]
    };
  }

  if (ticket.phase === "error" && ticket.message) {
    return {
      id: "quick-ticket-status",
      role: "alert",
      tone: "danger",
      message: ticket.message,
      showSuccessIcon: false,
      showErrorIcon: true,
      actions: isQuickTicketSubmissionFailure(ticket, surfaceValidation)
        ? [buildQuickTicketReadinessAction("rejected", activeSymbol, ticket.orderId)]
        : []
    };
  }

  if (surfaceValidation && validation) {
    return {
      id: "quick-ticket-status",
      role: "alert",
      tone: "danger",
      message: validation,
      showSuccessIcon: false,
      showErrorIcon: true,
      actions: []
    };
  }

  if (ticket.phase === "seeded" && ticket.message) {
    return {
      id: "quick-ticket-status",
      role: "status",
      tone: "success",
      message: ticket.message,
      showSuccessIcon: true,
      showErrorIcon: false,
      actions: []
    };
  }

  return {
    id: "quick-ticket-status",
    role: "status",
    tone: "default",
    message: buildQuickTicketGuidance(ticket, validation),
    showSuccessIcon: false,
    showErrorIcon: false,
    actions: []
  };
}

function buildQuickTicketReadinessAction(
  outcome: "accepted" | "rejected",
  activeSymbol: string | null,
  orderId: string | null
): QuickTicketStatusActionViewModel {
  const symbolLabel = activeSymbol ?? "selected symbol";
  const route = workflowTargetPath("TradingReadiness", "trading");
  const orderLabel = orderId ? `order ${orderId}` : `${symbolLabel} order`;

  return {
    id: "trading-readiness",
    label: "Review readiness",
    href: route,
    ariaLabel: outcome === "accepted"
      ? `Open Trading readiness after ${orderLabel} was accepted`
      : `Open Trading readiness after ${symbolLabel} order submission failed`
  };
}

function isQuickTicketSubmissionFailure(
  ticket: QuickTicketState,
  surfaceValidation: boolean
): boolean {
  return ticket.phase === "error"
    && ticket.message !== null
    && !surfaceValidation
    && ticket.message !== "Review and acknowledge the ticket before submitting.";
}

function shouldSurfaceQuickTicketValidation(ticket: QuickTicketState, validation: string | null): boolean {
  if (!validation) {
    return false;
  }

  return ticket.validationVisible === true || (ticket.phase === "error" && ticket.message === validation);
}

function resetQuickTicketFeedbackPhase(phase: QuickTicketPhase): QuickTicketPhase {
  return phase === "seeded" || phase === "submitted" || phase === "error" ? "idle" : phase;
}

function shouldClearQuickTicketFeedbackMessage(phase: QuickTicketPhase): boolean {
  return phase === "seeded" || phase === "submitted" || phase === "error";
}

function buildQuickTicketSeededMessage(symbol: string, side: "Buy" | "Sell", priceLabel: string): string {
  const action = side.toLowerCase();
  const renderedPrice = priceLabel || "the selected price";
  return `Seeded ${action} ${symbol} limit ticket at ${renderedPrice}. Enter quantity, then acknowledge before submitting.`;
}

function buildQuickTicketGuidance(ticket: QuickTicketState, validation: string | null): string {
  if (ticket.phase === "submitting") {
    return "Submitting order to Meridian execution controls.";
  }

  if (!validation) {
    return ticket.acknowledged
      ? "Orders route through Meridian's pre-trade risk and execution controls."
      : "Review side, quantity, and price, then acknowledge before submitting.";
  }

  const lower = validation.toLowerCase();
  if (lower.includes("quantity")) {
    return "Enter a quantity to enable order submission.";
  }

  if (lower.includes("limit price")) {
    return "Enter a limit price to enable order submission.";
  }

  return "Complete the required ticket fields to enable order submission.";
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
