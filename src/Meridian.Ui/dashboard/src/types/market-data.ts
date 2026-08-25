export interface SymbolRecord {
  symbol: string;
  status: "Active" | "Monitored" | "Archived" | "Error";
  provider: string | null;
  lastEventAt: string | null;
  eventCount: number;
  hasHistoricalData: boolean;
}

export interface SymbolStatistics {
  totalSymbols: number;
  monitoredSymbols: number;
  archivedSymbols: number;
  symbolsWithErrors: number;
  totalEventsLast24h: number;
}

export interface SessionStatsDto {
  sessionDate: string;
  open: number;
  high: number;
  low: number;
  last: number;
  volume: number;
  vwap: number;
  tradeCount: number;
  change: number;
  changePercent: number | null;
  firstTradeAt: string;
  lastTradeAt: string;
}

export interface QuoteDataResponse {
  symbol: string;
  timestamp: string;
  bidPrice: number;
  bidSize: number;
  askPrice: number;
  askSize: number;
  midPrice: number | null;
  spread: number | null;
  sequenceNumber: number;
  streamId: string | null;
  venue: string | null;
  session: SessionStatsDto | null;
}

export interface QuotesResponse {
  symbol: string;
  quote: QuoteDataResponse | null;
  timestamp: string;
}

export interface QuotesSnapshotItem {
  symbol: string;
  timestamp: string;
  bidPrice: number;
  bidSize: number;
  askPrice: number;
  askSize: number;
  midPrice: number | null;
  spread: number | null;
  lastPrice: number | null;
  lastSize: number | null;
  lastTradeTimestamp: string | null;
  sequenceNumber: number;
  streamId: string | null;
  venue: string | null;
  session: SessionStatsDto | null;
}

export interface QuotesSnapshotResponse {
  timestamp: string;
  count: number;
  quotes: QuotesSnapshotItem[];
}

export interface TradeDataResponse {
  symbol: string;
  timestamp: string;
  price: number;
  size: number;
  aggressor: string;
  sequenceNumber: number;
  streamId: string | null;
  venue: string | null;
}

export interface TradesResponse {
  symbol: string;
  trades: TradeDataResponse[];
  count: number;
  timestamp: string;
}

export interface OrderBookLevelDto {
  side: string;
  level: number;
  price: number;
  size: number;
  marketMaker: string | null;
}

export interface OrderBookResponse {
  symbol: string;
  timestamp: string;
  bids: OrderBookLevelDto[];
  asks: OrderBookLevelDto[];
  midPrice: number | null;
  imbalance: number | null;
  marketState: string;
  sequenceNumber: number;
  isStale: boolean;
  streamId: string | null;
  venue: string | null;
}

/**
 * Mirrors C# `HistoricalBarPoint` (registry-checked; keep comments out of the body).
 * `source` is the provenance label of the events aggregated into the bucket: a single
 * vendor label, the `"mixed"` sentinel when the bucket folds together more than one
 * provenance state (two vendors, or labeled plus unlabeled events), or null when no
 * contributing event was labeled. `isAdjusted` is the price-adjustment regime:
 * true/false only when every contributing event declares that same regime; mixed or
 * undeclared regimes collapse to null (unknown).
 */
export interface HistoricalBarPoint {
  start: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
  vwap: number;
  tradeCount: number;
  source?: string | null;
  isAdjusted?: boolean | null;
}

/**
 * Mirrors C# `HistoricalBarsResult`. `sources` is the distinct provenance labels observed
 * across the returned bars' contributing events (unlabeled events contribute nothing, so an
 * empty list with non-empty `bars` means the series is entirely unattributed).
 */
export interface HistoricalBarsResponse {
  success: boolean;
  message: string | null;
  symbol: string;
  intervalMinutes: number;
  from: string | null;
  to: string | null;
  totalBars: number;
  filesProcessed: number;
  totalFiles: number;
  queryTimeMs: number;
  bars: HistoricalBarPoint[];
  sources: string[];
}
