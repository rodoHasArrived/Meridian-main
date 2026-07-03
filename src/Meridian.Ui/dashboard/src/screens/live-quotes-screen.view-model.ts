import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { useQuotesStream } from "@/hooks/use-quotes-stream";
import { useRequestLifecycle, type RequestLifecycleStatus } from "@/hooks/use-request-lifecycle";
import type { ApiRequestOptions } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { workflowTargetPath } from "@/lib/workspace";
import type {
  OrderBookLevelDto,
  OrderBookResponse,
  OrderResult,
  OrderSubmitRequest,
  QuotesResponse,
  QuotesSnapshotItem,
  QuotesSnapshotResponse,
  SessionStatsDto,
  TradeDataResponse,
  TradesResponse
} from "@/types";

export const LIVE_QUOTES_POLL_INTERVAL_MS = 2000;
export const LIVE_QUOTES_FRESHNESS_BUDGET_MS = 2 * LIVE_QUOTES_POLL_INTERVAL_MS;
export const LIVE_QUOTES_TRADE_HISTORY_LIMIT = 200;
export const LIVE_QUOTES_TRADE_TABLE_LIMIT = 25;
export const LIVE_QUOTES_EMPTY_VALUE = "—";
export const LIVE_QUOTES_DEFAULT_SYMBOL_SET = ["AAPL", "MSFT", "SPY", "QQQ"];
export const LIVE_QUOTES_PINNED_SET_STORAGE_KEY = "meridian.liveMarketData.pinnedSymbolSet.v1";
export const LIVE_QUOTES_MAX_SYMBOL_SET_SIZE = 24;

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
  details: string[];
  orderId: string | null;
  validationVisible?: boolean;
  acknowledged: boolean;
}

export interface QuickTicketStatusViewModel {
  id: string;
  role: "status" | "alert";
  tone: "default" | "success" | "danger";
  message: string;
  details: string[];
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
  requestStatus: RequestLifecycleStatus;
}

export interface QuickTradeTicketApi {
  submitOrder: (request: OrderSubmitRequest) => Promise<OrderResult>;
}

export interface LiveQuotesLoadState<T> {
  data: T | null;
  error: string | null;
}

export interface LiveQuotesRequestStatusModel {
  market: RequestLifecycleStatus;
  snapshot: RequestLifecycleStatus;
  orderSubmission: RequestLifecycleStatus;
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

export type LiveMarketDataCommandId =
  | "add-symbol"
  | "remove-symbol"
  | "pin-set"
  | "open-order-book"
  | "open-replay"
  | "open-ticket"
  | "pause-updates"
  | "reset-layout";

export interface LiveMarketDataCommandViewModel {
  id: LiveMarketDataCommandId;
  label: string;
  ariaLabel: string;
  href: string | null;
  disabled: boolean;
  disabledReason: string | null;
  pressed?: boolean;
}

export interface LiveMarketDataMarketStatusBadgeViewModel {
  id: string;
  label: string;
  value: string;
  tone: "success" | "warning" | "danger" | "default";
  ariaLabel: string;
}

export interface LiveQuoteMatrixRowViewModel {
  id: string;
  symbol: string;
  bidLabel: string;
  askLabel: string;
  lastLabel: string;
  spreadLabel: string;
  venueLabel: string;
  sequenceLabel: string;
  providerLabel: string;
  quoteAgeLabel: string;
  tickRateLabel: string;
  stateLabel: string;
  stateTone: "success" | "warning" | "danger" | "default";
  selected: boolean;
  ariaLabel: string;
  selectAriaLabel: string;
}

export interface LiveMarketDataDetailViewModel {
  ariaLabel: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "danger" | "default";
  fields: LiveQuotesMetricRowViewModel[];
}

export interface LiveMarketDataDiagnosticsViewModel {
  statusLabel: string;
  statusTone: "success" | "warning" | "danger" | "default";
  summary: string;
  providerLatencyLabel: string;
  throughputLabel: string;
  connectionQualityLabel: string;
  tickHealthLabel: string;
  items: LiveQuotesMetricRowViewModel[];
}

export interface LiveMarketDataWorkspaceViewModel {
  title: string;
  description: string;
  symbolSet: string[];
  symbolSetLabel: string;
  selectedSymbol: string | null;
  selectedSymbolSummary: string;
  matrixRows: LiveQuoteMatrixRowViewModel[];
  selectedDetail: LiveMarketDataDetailViewModel;
  diagnostics: LiveMarketDataDiagnosticsViewModel;
  marketStatusBadges: LiveMarketDataMarketStatusBadgeViewModel[];
  commands: Record<LiveMarketDataCommandId, LiveMarketDataCommandViewModel>;
  matrixTableLabel: string;
  matrixEmptyText: string;
  snapshotState: LiveQuotesPanelState;
  paused: boolean;
  selectSymbol: (symbol: string) => void;
  removeSelectedSymbol: () => void;
  loadStarterSet: () => void;
  pinSymbolSet: () => void;
  togglePaused: () => void;
}

export interface LiveQuotesApi {
  getLiveQuote: (symbol: string, options?: ApiRequestOptions) => Promise<QuotesResponse>;
  getLiveTrades: (symbol: string, limit: number, options?: ApiRequestOptions) => Promise<TradesResponse>;
  getLiveOrderbook: (symbol: string, depth: number, options?: ApiRequestOptions) => Promise<OrderBookResponse>;
  getLiveQuotesSnapshot: (symbols?: readonly string[], options?: ApiRequestOptions) => Promise<QuotesSnapshotResponse>;
  submitOrder: (request: OrderSubmitRequest) => Promise<OrderResult>;
}

export interface LiveQuotesRouteBinding {
  routeSymbol: string;
  routeSymbols?: string;
  setRouteSymbol: (symbol: string) => void;
  setRouteSymbols?: (symbols: readonly string[], selectedSymbol: string | null) => void;
}

export interface LiveQuotesScreenViewModel {
  symbolInput: string;
  setSymbolInput: (value: string) => void;
  activeSymbol: string | null;
  lookup: LiveQuoteSymbolLookupViewModel;
  workspace: LiveMarketDataWorkspaceViewModel;
  market: LiveQuotesMarketDataViewModel;
  quickTrade: QuickTradeTicketViewModel;
  refreshCommand: LiveQuoteRefreshCommandViewModel | null;
  pollIntervalSecondsLabel: string;
  submitLookup: (event: FormEvent<HTMLFormElement>) => void;
  refreshMarketData: () => Promise<void>;
  requestStatus: LiveQuotesRequestStatusModel;
  quoteStreamHealthy: boolean;
}

export interface LiveQuoteRefreshCommandViewModel {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
  busy: boolean;
}


const idleOrderSubmissionStatus: RequestLifecycleStatus = {
  operation: "quick trade order submission",
  phase: "idle",
  inFlight: false,
  version: 0,
  message: "Ready to submit order.",
  error: null,
  startedAt: null,
  settledAt: null,
  lastSucceededAt: null,
  staleDiscardCount: 0,
  backoff: { attempt: 0, retryCount: 0, nextRetryDelayMs: null, maxRetries: 0 }
};

export const initialQuickTicketState: QuickTicketState = {
  side: "Buy",
  type: "Limit",
  quantity: "",
  limitPrice: "",
  phase: "idle",
  message: null,
  details: [],
  orderId: null,
  validationVisible: false,
  acknowledged: false
};

export function useLiveQuotesScreenViewModel(
  api: LiveQuotesApi,
  route: LiveQuotesRouteBinding
): LiveQuotesScreenViewModel {
  const initialSymbolSet = initialLiveMarketDataSymbolSet(route.routeSymbols, route.routeSymbol);
  const initialSymbol = normalizeLiveQuoteSymbol(route.routeSymbol) || (initialSymbolSet[0] ?? "");
  const [symbolInput, setSymbolInputState] = useState(initialSymbolSet.join(", "));
  const [symbolSet, setSymbolSet] = useState<string[]>(initialSymbolSet);
  const [activeSymbol, setActiveSymbol] = useState<string | null>(initialSymbol || null);
  const [submittedEmptySymbol, setSubmittedEmptySymbol] = useState(false);
  const [quote, setQuote] = useState<LiveQuotesLoadState<QuotesResponse>>({ data: null, error: null });
  const [trades, setTrades] = useState<LiveQuotesLoadState<TradesResponse>>({ data: null, error: null });
  const [orderbook, setOrderbook] = useState<LiveQuotesLoadState<OrderBookResponse>>({ data: null, error: null });
  const [snapshot, setSnapshot] = useState<LiveQuotesLoadState<QuotesSnapshotResponse>>({ data: null, error: null });
  const [selectedTradeId, setSelectedTradeId] = useState<string | null>(null);
  const [selectedDepthLevelId, setSelectedDepthLevelId] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [refreshingSnapshot, setRefreshingSnapshot] = useState(false);
  const [paused, setPaused] = useState(false);
  const streamSymbols = useMemo(() => (paused ? [] : symbolSet), [paused, symbolSet]);
  const quotesStream = useQuotesStream(streamSymbols);
  const marketLifecycle = useRequestLifecycle({
    operation: "live quote market panels",
    runningMessage: "Refreshing live quote, trade, and order-book panels.",
    successMessage: "Live market panels refreshed.",
    failureMessage: "Live market refresh failed.",
    staleMessage: "Older live market response discarded.",
    maxRetries: 2
  });
  const snapshotLifecycle = useRequestLifecycle({
    operation: "live quote matrix",
    runningMessage: "Refreshing live quote matrix.",
    successMessage: "Live quote matrix refreshed.",
    failureMessage: "Live quote matrix refresh failed.",
    staleMessage: "Older live quote matrix response discarded.",
    maxRetries: 2
  });
  const quickTrade = useQuickTradeTicket(activeSymbol, { submitOrder: api.submitOrder });

  const resetMarketState = useCallback(() => {
    setQuote({ data: null, error: null });
    setTrades({ data: null, error: null });
    setOrderbook({ data: null, error: null });
  }, []);

  const resetSelectedPanels = useCallback(() => {
    quickTrade.resetTicket();
    setSelectedTradeId(null);
    setSelectedDepthLevelId(null);
  }, [quickTrade]);

  const fetchMarketData = useCallback(async (symbol: string) => {
    const requestedSymbol = normalizeLiveQuoteSymbol(symbol);
    if (!requestedSymbol) {
      return;
    }

    const token = marketLifecycle.start({ runningMessage: `Refreshing live market panels for ${requestedSymbol}.` });
    if (!token) {
      return;
    }

    const requestOptions = { signal: token.signal };
    token.safeSetState(setRefreshing, true);

    try {
      const [quoteResult, tradesResult, orderbookResult] = await Promise.allSettled([
        api.getLiveQuote(requestedSymbol, requestOptions),
        api.getLiveTrades(requestedSymbol, LIVE_QUOTES_TRADE_HISTORY_LIMIT, requestOptions),
        api.getLiveOrderbook(requestedSymbol, 10, requestOptions)
      ]);

      if (!token.isCurrent()) {
        marketLifecycle.markStale(token.version);
        return;
      }

      token.safeSetState(setQuote, (current) => mergeLiveQuotesLoadState(quoteResult, current, "Failed to load quote"));
      token.safeSetState(setTrades, (current) => mergeLiveQuotesLoadState(tradesResult, current, "Failed to load trades"));
      token.safeSetState(setOrderbook, (current) => mergeLiveQuotesLoadState(orderbookResult, current, "Failed to load order book"));
      const hasFailure = [quoteResult, tradesResult, orderbookResult].some((result) => result.status === "rejected");
      if (hasFailure) {
        const failure = [quoteResult, tradesResult, orderbookResult].find((result) => result.status === "rejected");
        marketLifecycle.fail(token, failure?.status === "rejected" ? failure.reason : null, { fallback: "Live market refresh failed." });
      } else {
        marketLifecycle.succeed(token);
      }
    } finally {
      if (token.isCurrent()) {
        token.safeSetState(setRefreshing, false);
      }
      marketLifecycle.finish(token);
    }
  }, [api.getLiveOrderbook, api.getLiveQuote, api.getLiveTrades, marketLifecycle.fail, marketLifecycle.finish, marketLifecycle.markStale, marketLifecycle.start, marketLifecycle.succeed]);

  const fetchSnapshotData = useCallback(async (symbols: readonly string[]) => {
    const normalizedSymbols = normalizeLiveQuoteSymbols(symbols.join(","));
    const snapshotKey = normalizedSymbols.join(",");
    if (normalizedSymbols.length === 0) {
      return;
    }

    const token = snapshotLifecycle.start({ runningMessage: `Refreshing live quote matrix for ${snapshotKey}.` });
    if (!token) {
      return;
    }

    token.safeSetState(setRefreshingSnapshot, true);

    try {
      const result = await Promise.allSettled([
        api.getLiveQuotesSnapshot(normalizedSymbols, { signal: token.signal })
      ]);

      if (!token.isCurrent()) {
        snapshotLifecycle.markStale(token.version);
        return;
      }

      token.safeSetState(setSnapshot, (current) => mergeLiveQuotesLoadState(result[0], current, "Failed to load quote matrix"));
      if (result[0].status === "fulfilled") {
        snapshotLifecycle.succeed(token);
      } else {
        snapshotLifecycle.fail(token, result[0].reason, { fallback: "Live quote matrix refresh failed." });
      }
    } finally {
      if (token.isCurrent()) {
        token.safeSetState(setRefreshingSnapshot, false);
      }
      snapshotLifecycle.finish(token);
    }
  }, [api.getLiveQuotesSnapshot, snapshotLifecycle.fail, snapshotLifecycle.finish, snapshotLifecycle.markStale, snapshotLifecycle.start, snapshotLifecycle.succeed]);

  useEffect(() => {
    if (!activeSymbol || paused) {
      return;
    }

    void fetchMarketData(activeSymbol);
    const interval = window.setInterval(() => void fetchMarketData(activeSymbol), LIVE_QUOTES_POLL_INTERVAL_MS);
    return () => window.clearInterval(interval);
  }, [activeSymbol, fetchMarketData, paused]);

  useEffect(() => {
    if (!quotesStream.snapshot) {
      return;
    }

    const pushed = quotesStream.snapshot;
    setSnapshot((current) => mergeLiveQuotesLoadState({ status: "fulfilled", value: pushed }, current, "Failed to load quote matrix"));
  }, [quotesStream.snapshot]);

  useEffect(() => {
    if (symbolSet.length === 0 || paused) {
      return;
    }

    void fetchSnapshotData(symbolSet);
    if (quotesStream.healthy) {
      // The SSE stream feeds the matrix; polling stays suspended until it degrades.
      return;
    }

    const interval = window.setInterval(() => void fetchSnapshotData(symbolSet), LIVE_QUOTES_POLL_INTERVAL_MS);
    return () => window.clearInterval(interval);
  }, [fetchSnapshotData, paused, quotesStream.healthy, symbolSet]);

  useEffect(() => {
    const nextSymbolSet = initialLiveMarketDataSymbolSet(route.routeSymbols, route.routeSymbol);
    const nextSymbol = normalizeLiveQuoteSymbol(route.routeSymbol) || (nextSymbolSet[0] ?? "");
    const currentSymbolSetKey = symbolSet.join(",");
    const nextSymbolSetKey = nextSymbolSet.join(",");
    if (nextSymbol === (activeSymbol ?? "") && nextSymbolSetKey === currentSymbolSetKey) {
      return;
    }

    setSymbolInputState(nextSymbolSet.join(", "));
    setSymbolSet(nextSymbolSet);
    setSubmittedEmptySymbol(false);
    setActiveSymbol(nextSymbol || null);
    resetMarketState();
    resetSelectedPanels();
  }, [activeSymbol, resetMarketState, resetSelectedPanels, route.routeSymbol, route.routeSymbols, symbolSet]);

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

  const selectSymbol = useCallback((symbol: string) => {
    const next = normalizeLiveQuoteSymbol(symbol);
    if (!next || next === activeSymbol) {
      return;
    }

    setActiveSymbol(next);
    resetMarketState();
    resetSelectedPanels();
    if (route.setRouteSymbols) {
      route.setRouteSymbols(symbolSet, next);
    } else {
      route.setRouteSymbol(next);
    }
  }, [activeSymbol, resetMarketState, resetSelectedPanels, route, symbolSet]);

  const removeSelectedSymbol = useCallback(() => {
    if (!activeSymbol) {
      return;
    }

    const nextSet = symbolSet.filter((symbol) => symbol !== activeSymbol);
    const nextActiveSymbol = nextSet[0] ?? null;
    setSymbolSet(nextSet);
    setActiveSymbol(nextActiveSymbol);
    setSymbolInputState(nextSet.join(", "));
    resetMarketState();
    resetSelectedPanels();
    setSnapshot((current) => current.data ? {
      ...current,
      data: {
        ...current.data,
        count: current.data.quotes.filter((row) => row.symbol !== activeSymbol).length,
        quotes: current.data.quotes.filter((row) => row.symbol !== activeSymbol)
      }
    } : current);
    if (route.setRouteSymbols) {
      route.setRouteSymbols(nextSet, nextActiveSymbol);
    } else {
      route.setRouteSymbol(nextActiveSymbol ?? "");
    }
  }, [activeSymbol, resetMarketState, resetSelectedPanels, route, symbolSet]);

  const loadStarterSet = useCallback(() => {
    const nextSet = [...LIVE_QUOTES_DEFAULT_SYMBOL_SET];
    const nextActiveSymbol = nextSet[0] ?? null;
    setSymbolSet(nextSet);
    setActiveSymbol(nextActiveSymbol);
    setSymbolInputState(nextSet.join(", "));
    setSubmittedEmptySymbol(false);
    resetMarketState();
    resetSelectedPanels();
    if (route.setRouteSymbols) {
      route.setRouteSymbols(nextSet, nextActiveSymbol);
    } else {
      route.setRouteSymbol(nextActiveSymbol ?? "");
    }
  }, [resetMarketState, resetSelectedPanels, route]);

  const pinSymbolSet = useCallback(() => {
    persistLiveMarketDataPinnedSet(symbolSet);
  }, [symbolSet]);

  const togglePaused = useCallback(() => {
    setPaused((current) => !current);
  }, []);

  const workspace = useMemo(() => buildLiveMarketDataWorkspaceViewModel({
    symbolSet,
    selectedSymbol: activeSymbol,
    snapshot,
    snapshotRefreshing: refreshingSnapshot,
    market,
    paused,
    selectSymbol,
    removeSelectedSymbol,
    loadStarterSet,
    pinSymbolSet,
    togglePaused
  }), [
    activeSymbol,
    loadStarterSet,
    market,
    paused,
    pinSymbolSet,
    refreshingSnapshot,
    removeSelectedSymbol,
    selectSymbol,
    snapshot,
    symbolSet,
    togglePaused
  ]);

  const submitLookup = useCallback((event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const nextSet = normalizeLiveQuoteSymbols(symbolInput);
    const next = nextSet[0] ?? "";
    if (nextSet.length === 0) {
      setSubmittedEmptySymbol(true);
      return;
    }

    setSubmittedEmptySymbol(false);
    setSymbolSet(nextSet);
    resetMarketState();
    setActiveSymbol(next);
    resetSelectedPanels();
    if (route.setRouteSymbols) {
      route.setRouteSymbols(nextSet, next);
    } else {
      route.setRouteSymbol(next);
    }
  }, [resetMarketState, resetSelectedPanels, route, symbolInput]);

  const refreshMarketData = useCallback(async () => {
    await Promise.all([
      activeSymbol ? fetchMarketData(activeSymbol) : Promise.resolve(),
      symbolSet.length > 0 ? fetchSnapshotData(symbolSet) : Promise.resolve()
    ]);
  }, [activeSymbol, fetchMarketData, fetchSnapshotData, symbolSet]);

  return {
    symbolInput,
    setSymbolInput,
    activeSymbol,
    lookup,
    workspace,
    market,
    quickTrade,
    refreshCommand: buildLiveQuoteRefreshCommand(activeSymbol, refreshing || refreshingSnapshot),
    pollIntervalSecondsLabel: String(LIVE_QUOTES_POLL_INTERVAL_MS / 1000),
    submitLookup,
    refreshMarketData,
    requestStatus: {
      market: marketLifecycle.status,
      snapshot: snapshotLifecycle.status,
      orderSubmission: quickTrade.requestStatus
    },
    quoteStreamHealthy: quotesStream.healthy
  };
}

export function normalizeLiveQuoteSymbol(value: string): string {
  return value.trim().toUpperCase();
}

export function normalizeLiveQuoteSymbols(value: string): string[] {
  const seen = new Set<string>();
  const symbols: string[] = [];
  for (const candidate of value.split(/[\s,;|]+/)) {
    const symbol = normalizeLiveQuoteSymbol(candidate).replace(/[^A-Z0-9.-]/g, "");
    if (!symbol || seen.has(symbol)) {
      continue;
    }

    seen.add(symbol);
    symbols.push(symbol);
    if (symbols.length >= LIVE_QUOTES_MAX_SYMBOL_SET_SIZE) {
      break;
    }
  }

  return symbols;
}

export function initialLiveMarketDataSymbolSet(routeSymbols: string | undefined, routeSymbol: string): string[] {
  const requested = normalizeLiveQuoteSymbols(routeSymbols || routeSymbol);
  if (requested.length > 0) {
    return requested;
  }

  return readLiveMarketDataPinnedSet();
}

export function readLiveMarketDataPinnedSet(): string[] {
  if (typeof window === "undefined") {
    return [];
  }

  try {
    const raw = window.localStorage.getItem(LIVE_QUOTES_PINNED_SET_STORAGE_KEY);
    if (!raw) {
      return [];
    }

    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? normalizeLiveQuoteSymbols(parsed.join(",")) : [];
  } catch {
    return [];
  }
}

export function persistLiveMarketDataPinnedSet(symbols: readonly string[]) {
  if (typeof window === "undefined") {
    return;
  }

  const normalized = normalizeLiveQuoteSymbols(symbols.join(","));
  if (normalized.length === 0) {
    window.localStorage.removeItem(LIVE_QUOTES_PINNED_SET_STORAGE_KEY);
    return;
  }

  window.localStorage.setItem(LIVE_QUOTES_PINNED_SET_STORAGE_KEY, JSON.stringify(normalized));
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
  const normalizedSymbols = normalizeLiveQuoteSymbols(inputValue);
  const normalizedSymbol = normalizedSymbols.join(", ");
  const inputInvalid = submittedEmpty && normalizedSymbols.length === 0;
  const disabledReason = normalizedSymbols.length === 0
    ? "Enter a symbol before loading live market data."
    : null;
  const symbolLabel = normalizedSymbols.length > 1
    ? `${normalizedSymbols.length} symbols`
    : normalizedSymbols[0] ?? "";

  return {
    inputId: "live-quote-symbol",
    statusId: "live-quote-symbol-status",
    formLabel: "Live market data symbol set",
    inputLabel: "Symbol",
    inputPlaceholder: "Enter symbols (e.g. AAPL, MSFT, SPY)",
    normalizedSymbol,
    inputInvalid,
    command: {
      label: "Add symbol set",
      ariaLabel: normalizedSymbols.length > 1
        ? `Load live market data for ${symbolLabel}`
        : symbolLabel
          ? `View live quote for ${symbolLabel}`
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

export function buildLiveMarketDataWorkspaceViewModel({
  symbolSet,
  selectedSymbol,
  snapshot,
  snapshotRefreshing,
  market,
  paused,
  selectSymbol,
  removeSelectedSymbol,
  loadStarterSet,
  pinSymbolSet,
  togglePaused
}: {
  symbolSet: readonly string[];
  selectedSymbol: string | null;
  snapshot: LiveQuotesLoadState<QuotesSnapshotResponse>;
  snapshotRefreshing: boolean;
  market: LiveQuotesMarketDataViewModel;
  paused: boolean;
  selectSymbol: (symbol: string) => void;
  removeSelectedSymbol: () => void;
  loadStarterSet: () => void;
  pinSymbolSet: () => void;
  togglePaused: () => void;
}): LiveMarketDataWorkspaceViewModel {
  const quoteBySymbol = new Map((snapshot.data?.quotes ?? []).map((quote) => [quote.symbol, quote]));
  const matrixRows = symbolSet.map((symbol) => buildLiveQuoteMatrixRow({
    symbol,
    quote: quoteBySymbol.get(symbol) ?? quoteSnapshotFromSelectedMarket(symbol, selectedSymbol, market),
    selected: symbol === selectedSymbol
  }));
  const selectedRow = matrixRows.find((row) => row.selected) ?? matrixRows[0] ?? null;
  const selectedQuote = selectedRow ? quoteBySymbol.get(selectedRow.symbol) ?? quoteSnapshotFromSelectedMarket(selectedRow.symbol, selectedSymbol, market) : null;
  const hasAnyLoadedQuote = matrixRows.some((row) => row.stateTone !== "default");
  const snapshotState = buildPanelState({
    loading: snapshotRefreshing && !snapshot.data && !snapshot.error,
    error: snapshot.error,
    ready: matrixRows.length > 0 && hasAnyLoadedQuote,
    emptyMessage: symbolSet.length === 0
      ? "No symbols selected. Load the starter set or enter symbols to begin monitoring."
      : "Waiting for the quote matrix to return current rows.",
    loadingMessage: `Loading quote matrix for ${formatCount(symbolSet.length, "symbol")}…`
  });
  const diagnostics = buildLiveMarketDataDiagnostics({
    snapshot,
    snapshotRefreshing,
    matrixRows,
    market,
    paused
  });
  const selectedDetail = buildLiveMarketDataDetail(selectedRow, selectedQuote, market);

  return {
    title: "Live Market Data Workspace",
    description: "Monitor real-time quotes, trades, BBO, spreads, provider posture, and latency from one symbol set.",
    symbolSet: [...symbolSet],
    symbolSetLabel: symbolSet.length > 0 ? symbolSet.join(", ") : "No symbols",
    selectedSymbol: selectedRow?.symbol ?? null,
    selectedSymbolSummary: selectedRow
      ? `${selectedRow.symbol} ${selectedRow.stateLabel}; bid ${selectedRow.bidLabel}, ask ${selectedRow.askLabel}, spread ${selectedRow.spreadLabel}.`
      : "No selected symbol summary is available.",
    matrixRows,
    selectedDetail,
    diagnostics,
    marketStatusBadges: buildLiveMarketStatusBadges(matrixRows, diagnostics, paused),
    commands: buildLiveMarketDataCommands({
      selectedSymbol: selectedRow?.symbol ?? null,
      symbolSet,
      paused,
      snapshotRefreshing
    }),
    matrixTableLabel: "Live quote matrix",
    matrixEmptyText: "No symbols are in the quote matrix.",
    snapshotState,
    paused,
    selectSymbol,
    removeSelectedSymbol,
    loadStarterSet,
    pinSymbolSet,
    togglePaused
  };
}

function buildLiveQuoteMatrixRow({
  symbol,
  quote,
  selected
}: {
  symbol: string;
  quote: QuotesSnapshotItem | null;
  selected: boolean;
}): LiveQuoteMatrixRowViewModel {
  const quoteAgeMs = quote ? Date.now() - new Date(quote.timestamp).getTime() : null;
  const stale = quoteAgeMs !== null && quoteAgeMs > LIVE_QUOTES_POLL_INTERVAL_MS * 3;
  const stateTone: LiveQuoteMatrixRowViewModel["stateTone"] = !quote
    ? "default"
    : stale
      ? "warning"
      : quote.spread !== null && quote.midPrice !== null && quote.midPrice > 0 && quote.spread / quote.midPrice > 0.005
        ? "warning"
        : "success";
  const stateLabel = !quote ? "Waiting" : stale ? "Stale" : stateTone === "warning" ? "Wide spread" : "Live";
  const spreadLabel = formatMarketPrice(quote?.spread);
  const venueLabel = quote?.venue ?? "Unreported";
  const lastTradeAgeLabel = quote?.lastTradeTimestamp ? formatRelativeAge(quote.lastTradeTimestamp) : LIVE_QUOTES_EMPTY_VALUE;

  return {
    id: symbol,
    symbol,
    bidLabel: quote ? formatPriceSize(quote.bidPrice, quote.bidSize) : LIVE_QUOTES_EMPTY_VALUE,
    askLabel: quote ? formatPriceSize(quote.askPrice, quote.askSize) : LIVE_QUOTES_EMPTY_VALUE,
    lastLabel: formatMarketPrice(quote?.lastPrice),
    spreadLabel,
    venueLabel,
    sequenceLabel: quote ? formatMarketSize(quote.sequenceNumber) : LIVE_QUOTES_EMPTY_VALUE,
    providerLabel: quote?.streamId ?? "No stream",
    quoteAgeLabel: quote ? formatRelativeAge(quote.timestamp) : "No quote",
    tickRateLabel: lastTradeAgeLabel === LIVE_QUOTES_EMPTY_VALUE ? "No tape" : `Last print ${lastTradeAgeLabel}`,
    stateLabel,
    stateTone,
    selected,
    ariaLabel: `${symbol}. ${stateLabel}. Bid ${quote ? formatPriceSize(quote.bidPrice, quote.bidSize) : "not available"}. Ask ${quote ? formatPriceSize(quote.askPrice, quote.askSize) : "not available"}. Spread ${spreadLabel}. Venue ${venueLabel}. Quote age ${quote ? formatRelativeAge(quote.timestamp) : "not available"}.`,
    selectAriaLabel: `Inspect ${symbol} live market data`
  };
}

function quoteSnapshotFromSelectedMarket(
  symbol: string,
  selectedSymbol: string | null,
  market: LiveQuotesMarketDataViewModel
): QuotesSnapshotItem | null {
  const quote = market.quoteRow;
  if (!quote || symbol !== selectedSymbol) {
    return null;
  }

  const latestTrade = market.tradeHistory[0] ?? null;
  return {
    symbol,
    timestamp: quote.timestamp,
    bidPrice: quote.bidPrice,
    bidSize: quote.bidSize,
    askPrice: quote.askPrice,
    askSize: quote.askSize,
    midPrice: quote.midPrice,
    spread: quote.spread,
    lastPrice: latestTrade?.price ?? quote.session?.last ?? quote.midPrice,
    lastSize: latestTrade?.size ?? null,
    lastTradeTimestamp: latestTrade?.timestamp ?? quote.session?.lastTradeAt ?? null,
    sequenceNumber: quote.sequenceNumber,
    streamId: quote.streamId,
    venue: quote.venue,
    session: quote.session
  };
}

function buildLiveMarketDataDetail(
  selectedRow: LiveQuoteMatrixRowViewModel | null,
  selectedQuote: QuotesSnapshotItem | null,
  market: LiveQuotesMarketDataViewModel
): LiveMarketDataDetailViewModel {
  if (!selectedRow) {
    return {
      ariaLabel: "Selected symbol summary",
      title: "No symbol selected",
      subtitle: "Load a symbol set to inspect detail",
      description: "The selected-symbol detail panel announces quote posture, venue, spread, tick health, and latency once a row is selected.",
      statusLabel: "Empty",
      statusTone: "default",
      fields: []
    };
  }

  const midLabel = formatMarketPrice(selectedQuote?.midPrice ?? market.quoteRow?.midPrice);
  const session = selectedQuote?.session ?? market.quoteRow?.session ?? null;
  return {
    ariaLabel: `${selectedRow.symbol} selected symbol summary`,
    title: `${selectedRow.symbol} live summary`,
    subtitle: `${selectedRow.venueLabel} · ${selectedRow.providerLabel}`,
    description: `BBO ${selectedRow.bidLabel} / ${selectedRow.askLabel}. Status ${selectedRow.stateLabel}; spread state is shown with text and tone.`,
    statusLabel: selectedRow.stateLabel,
    statusTone: selectedRow.stateTone,
    fields: [
      { id: "bid", label: "Bid x size", value: selectedRow.bidLabel },
      { id: "ask", label: "Ask x size", value: selectedRow.askLabel },
      { id: "last", label: "Last print", value: selectedRow.lastLabel },
      { id: "mid", label: "Mid", value: midLabel },
      { id: "spread", label: "Spread", value: selectedRow.spreadLabel },
      { id: "quote-age", label: "Quote age", value: selectedRow.quoteAgeLabel },
      { id: "tick-health", label: "Tick health", value: selectedRow.tickRateLabel },
      { id: "session", label: "Session", value: session ? `${session.sessionDate} ${formatChange(session.change)} (${formatChangePct(session.changePercent)})` : LIVE_QUOTES_EMPTY_VALUE }
    ]
  };
}

function buildLiveMarketDataDiagnostics({
  snapshot,
  snapshotRefreshing,
  matrixRows,
  market,
  paused
}: {
  snapshot: LiveQuotesLoadState<QuotesSnapshotResponse>;
  snapshotRefreshing: boolean;
  matrixRows: readonly LiveQuoteMatrixRowViewModel[];
  market: LiveQuotesMarketDataViewModel;
  paused: boolean;
}): LiveMarketDataDiagnosticsViewModel {
  const warningRows = matrixRows.filter((row) => row.stateTone === "warning").length;
  const waitingRows = matrixRows.filter((row) => row.stateTone === "default").length;
  const errorCount = [snapshot.error, market.quoteState.status === "error" ? market.quoteState.message : null, market.orderbookState.status === "error" ? market.orderbookState.message : null, market.tradesState.status === "error" ? market.tradesState.message : null].filter(Boolean).length;
  const statusTone: LiveMarketDataDiagnosticsViewModel["statusTone"] = errorCount > 0
    ? "danger"
    : paused || warningRows > 0 || waitingRows > 0
      ? "warning"
      : "success";
  const statusLabel = errorCount > 0 ? "Degraded" : paused ? "Paused" : statusTone === "warning" ? "Review" : "Healthy";
  const throughputLabel = market.tradeHistory.length > 0
    ? `${formatCount(market.tradeHistory.length, "print")} buffered`
    : "No tape prints buffered";
  const providerLatencyLabel = matrixRows.length > 0
    ? `${formatCount(matrixRows.filter((row) => row.stateTone === "success").length, "symbol")} live`
    : "No provider ticks";
  const connectionQualityLabel = errorCount > 0
    ? `${formatCount(errorCount, "feed")} degraded`
    : snapshotRefreshing
      ? "Refreshing"
      : paused
        ? "Updates paused"
        : "Connected";
  const tickHealthLabel = warningRows > 0
    ? `${formatCount(warningRows, "symbol")} need review`
    : waitingRows > 0
      ? `${formatCount(waitingRows, "symbol")} waiting`
      : "Ticks current";

  return {
    statusLabel,
    statusTone,
    summary: `${connectionQualityLabel}; ${providerLatencyLabel}; ${tickHealthLabel}.`,
    providerLatencyLabel,
    throughputLabel,
    connectionQualityLabel,
    tickHealthLabel,
    items: [
      { id: "provider-latency", label: "Provider posture", value: providerLatencyLabel },
      { id: "throughput", label: "Throughput", value: throughputLabel },
      { id: "connection", label: "Connection quality", value: connectionQualityLabel },
      { id: "tick-health", label: "Tick health", value: tickHealthLabel }
    ]
  };
}

function buildLiveMarketStatusBadges(
  rows: readonly LiveQuoteMatrixRowViewModel[],
  diagnostics: LiveMarketDataDiagnosticsViewModel,
  paused: boolean
): LiveMarketDataMarketStatusBadgeViewModel[] {
  return [
    {
      id: "connection",
      label: "Connection",
      value: diagnostics.connectionQualityLabel,
      tone: diagnostics.statusTone,
      ariaLabel: `Connection ${diagnostics.connectionQualityLabel}`
    },
    {
      id: "coverage",
      label: "Coverage",
      value: `${rows.filter((row) => row.stateTone === "success").length}/${rows.length || 0} live`,
      tone: rows.length === 0 ? "default" : rows.every((row) => row.stateTone === "success") ? "success" : "warning",
      ariaLabel: `${rows.filter((row) => row.stateTone === "success").length} of ${rows.length || 0} symbols live`
    },
    {
      id: "updates",
      label: "Updates",
      value: paused ? "Paused" : `${LIVE_QUOTES_POLL_INTERVAL_MS / 1000}s`,
      tone: paused ? "warning" : "success",
      ariaLabel: paused ? "Updates paused" : `Updates every ${LIVE_QUOTES_POLL_INTERVAL_MS / 1000} seconds`
    }
  ];
}

function buildLiveMarketDataCommands({
  selectedSymbol,
  symbolSet,
  paused,
  snapshotRefreshing
}: {
  selectedSymbol: string | null;
  symbolSet: readonly string[];
  paused: boolean;
  snapshotRefreshing: boolean;
}): Record<LiveMarketDataCommandId, LiveMarketDataCommandViewModel> {
  const noSelectionReason = selectedSymbol ? null : "Select a symbol before using this action.";
  return {
    "add-symbol": {
      id: "add-symbol",
      label: "Add symbol",
      ariaLabel: "Add symbols to the live market data workspace",
      href: null,
      disabled: false,
      disabledReason: null
    },
    "remove-symbol": {
      id: "remove-symbol",
      label: "Remove symbol",
      ariaLabel: selectedSymbol ? `Remove ${selectedSymbol} from the live market data workspace` : "Remove selected symbol",
      href: null,
      disabled: selectedSymbol === null,
      disabledReason: noSelectionReason
    },
    "pin-set": {
      id: "pin-set",
      label: "Pin set",
      ariaLabel: `Pin current symbol set: ${symbolSet.join(", ") || "empty"}`,
      href: null,
      disabled: symbolSet.length === 0,
      disabledReason: symbolSet.length === 0 ? "Add at least one symbol before pinning the set." : null
    },
    "open-order-book": {
      id: "open-order-book",
      label: "Open order book",
      ariaLabel: selectedSymbol ? `Open order book for ${selectedSymbol}` : "Open order book",
      href: selectedSymbol ? `/data/quotes?symbol=${encodeURIComponent(selectedSymbol)}#order-book` : null,
      disabled: selectedSymbol === null,
      disabledReason: noSelectionReason
    },
    "open-replay": {
      id: "open-replay",
      label: "Open replay",
      ariaLabel: selectedSymbol ? `Open replay workflow for ${selectedSymbol}` : "Open replay workflow",
      href: selectedSymbol ? `/trading/readiness?symbol=${encodeURIComponent(selectedSymbol)}#session-replay` : null,
      disabled: selectedSymbol === null,
      disabledReason: noSelectionReason
    },
    "open-ticket": {
      id: "open-ticket",
      label: "Open ticket",
      ariaLabel: selectedSymbol ? `Open trade ticket for ${selectedSymbol}` : "Open trade ticket",
      href: selectedSymbol ? `/data/quotes?symbol=${encodeURIComponent(selectedSymbol)}#quick-ticket` : null,
      disabled: selectedSymbol === null,
      disabledReason: noSelectionReason
    },
    "pause-updates": {
      id: "pause-updates",
      label: paused ? "Resume updates" : "Pause updates",
      ariaLabel: paused ? "Resume live market data updates" : "Pause live market data updates",
      href: null,
      disabled: snapshotRefreshing,
      disabledReason: snapshotRefreshing ? "Wait for the current refresh to complete before pausing updates." : null,
      pressed: paused
    },
    "reset-layout": {
      id: "reset-layout",
      label: "Reset layout",
      ariaLabel: "Reset live market data workspace to starter symbols",
      href: null,
      disabled: false,
      disabledReason: null
    }
  };
}

export function useQuickTradeTicket(
  activeSymbol: string | null,
  api: QuickTradeTicketApi
): QuickTradeTicketViewModel {
  const [ticket, setTicket] = useState<QuickTicketState>(initialQuickTicketState);
  const activeSymbolRef = useRef(activeSymbol);
  const submitLifecycle = useRequestLifecycle({
    operation: "quick trade order submission",
    runningMessage: "Submitting quick trade order.",
    successMessage: "Quick trade order submitted.",
    failureMessage: "Quick trade order submission failed.",
    staleMessage: "Older quick trade submission response discarded.",
    maxRetries: 1
  });

  activeSymbolRef.current = activeSymbol;

  const resetTicket = useCallback(() => {
    submitLifecycle.invalidate();
    setTicket(initialQuickTicketState);
  }, [submitLifecycle.invalidate]);

  useEffect(() => {
    submitLifecycle.invalidate();
    setTicket(initialQuickTicketState);
  }, [activeSymbol, submitLifecycle.invalidate]);

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
      details: [],
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
      details: shouldClearQuickTicketFeedbackMessage(current.phase) ? [] : current.details,
      validationVisible: true,
      acknowledged: false
    }));
  }, []);

  const setReviewAcknowledged = useCallback((value: boolean) => {
    setTicket((current) => ({
      ...current,
      acknowledged: value,
      phase: value ? "idle" : resetQuickTicketFeedbackPhase(current.phase),
      message: value || shouldClearQuickTicketFeedbackMessage(current.phase) ? null : current.message,
      details: value || shouldClearQuickTicketFeedbackMessage(current.phase) ? [] : current.details
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
        phase: "error" as const,
        message: validation,
        details: [],
        orderId: null,
        validationVisible: true
      }));
      return;
    }

    if (!ticket.acknowledged) {
      setTicket((current) => ({
        ...current,
        phase: "error" as const,
        message: "Review and acknowledge the ticket before submitting.",
        details: [],
        orderId: null,
        validationVisible: false
      }));
      return;
    }

    const request = buildOrderRequest(submitSymbol, ticket);
    const token = submitLifecycle.start();
    if (!token) {
      return;
    }
    const applyCurrentSubmission = (update: (current: QuickTicketState) => QuickTicketState) => {
      if (token.isCurrent() && activeSymbolRef.current === submitSymbol) {
        token.safeSetState(setTicket, update);
      }
    };

    token.safeSetState(setTicket, (current) => ({ ...current, phase: "submitting" as const, message: null, details: [], orderId: null }));
    try {
      const result = await api.submitOrder(request);
      if (result.success) {
        applyCurrentSubmission((current) => ({
          ...current,
          phase: "submitted" as const,
          message: result.orderId ? `Order ${result.orderId} accepted.` : "Order accepted.",
          details: [],
          orderId: result.orderId,
          validationVisible: false,
          acknowledged: false
        }));
        submitLifecycle.succeed(token);
      } else {
        applyCurrentSubmission((current) => ({
          ...current,
          phase: "error" as const,
          message: result.reason ?? "Order rejected.",
          details: [],
          orderId: null,
          validationVisible: false,
          acknowledged: false
        }));
        submitLifecycle.fail(token, result.reason ?? "Order rejected.", { fallback: "Order rejected." });
      }
    } catch (error) {
      const display = describeApiError(error, "Order submission failed.");
      applyCurrentSubmission((current) => ({
        ...current,
        phase: "error" as const,
        message: display.summary,
        details: display.details,
        orderId: null,
        validationVisible: false,
        acknowledged: false
      }));
      submitLifecycle.fail(token, error, { fallback: "Order submission failed." });
    } finally {
      submitLifecycle.finish(token);
    }
  }, [activeSymbol, api, submitLifecycle.fail, submitLifecycle.finish, submitLifecycle.start, submitLifecycle.succeed, ticket]);

  return useMemo(
    () => buildQuickTradeTicketViewModel({
      activeSymbol,
      ticket,
      seedTicket,
      updateField,
      setReviewAcknowledged,
      submitTicket,
      resetTicket,
      requestStatus: submitLifecycle.status
    }),
    [activeSymbol, resetTicket, seedTicket, setReviewAcknowledged, submitLifecycle.status, submitTicket, ticket, updateField]
  );
}

export function buildQuickTradeTicketViewModel({
  activeSymbol,
  ticket,
  seedTicket,
  updateField,
  setReviewAcknowledged,
  submitTicket,
  resetTicket,
  requestStatus
}: {
  activeSymbol: string | null;
  ticket: QuickTicketState;
  seedTicket: QuickTradeTicketViewModel["seedTicket"];
  updateField: QuickTradeTicketViewModel["updateField"];
  setReviewAcknowledged: QuickTradeTicketViewModel["setReviewAcknowledged"];
  submitTicket: QuickTradeTicketViewModel["submitTicket"];
  resetTicket: QuickTradeTicketViewModel["resetTicket"];
  requestStatus?: RequestLifecycleStatus;
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
    resetTicket,
    requestStatus: requestStatus ?? idleOrderSubmissionStatus
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

function formatPriceSize(price: number | null | undefined, size: number | null | undefined): string {
  const priceLabel = formatMarketPrice(price);
  const sizeLabel = formatMarketSize(size);
  if (priceLabel === LIVE_QUOTES_EMPTY_VALUE || sizeLabel === LIVE_QUOTES_EMPTY_VALUE) {
    return LIVE_QUOTES_EMPTY_VALUE;
  }

  return `${priceLabel} x ${sizeLabel}`;
}

export function formatMarketTime(iso: string | null | undefined): string {
  if (!iso) return LIVE_QUOTES_EMPTY_VALUE;
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return LIVE_QUOTES_EMPTY_VALUE;
  return formatUtcTimeWithMilliseconds(date);
}

function formatRelativeAge(iso: string | null | undefined): string {
  if (!iso) return LIVE_QUOTES_EMPTY_VALUE;
  const then = new Date(iso).getTime();
  if (!Number.isFinite(then)) return LIVE_QUOTES_EMPTY_VALUE;
  const elapsedMs = Math.max(0, Date.now() - then);
  if (elapsedMs < 1000) return "just now";
  const seconds = Math.round(elapsedMs / 1000);
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.round(minutes / 60);
  return `${hours}h ago`;
}

function formatCount(count: number, noun: string): string {
  return `${count.toLocaleString()} ${noun}${count === 1 ? "" : "s"}`;
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
      details: [],
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
      details: ticket.details,
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
      details: [],
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
      details: [],
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
    details: [],
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
    const count = normalizeLiveQuoteSymbols(normalizedSymbol).length;
    return {
      id: "live-quote-symbol-status",
      role: "status",
      message: count > 1 ? `Ready to load ${count} symbols.` : `Ready to load ${normalizedSymbol}.`,
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
