import type {
  HistoricalBarsResponse,
  OrderBookResponse,
  QuotesResponse,
  QuotesSnapshotResponse,
  SymbolRecord,
  SymbolStatistics,
  TradesResponse
} from "@/types/market-data";
import { MARKET_DATA_API_ENDPOINTS, SYMBOL_API_ENDPOINTS } from "../workstation-endpoints";
import {
  apiRoutePattern,
  readFixtureSearchParams,
  readSymbolFromPath,
  type DynamicFixturePattern
} from "./fixture-resolver";

interface FixtureMarketProfile {
  bidPrice: number;
  bidSize: number;
  askPrice: number;
  askSize: number;
  lastPrice: number;
  venue: string;
  streamId: string;
}

const fixtureMarketTimestamp = "2026-05-08T15:00:00.000Z";

const fixtureMarketProfiles: Record<string, FixtureMarketProfile> = {
  AAPL: { bidPrice: 188.05, bidSize: 200, askPrice: 188.07, askSize: 150, lastPrice: 188.06, venue: "NASDAQ", streamId: "fixture-aapl" },
  MSFT: { bidPrice: 421.1, bidSize: 300, askPrice: 421.2, askSize: 250, lastPrice: 421.15, venue: "NASDAQ", streamId: "fixture-msft" },
  NVDA: { bidPrice: 950.2, bidSize: 80, askPrice: 950.45, askSize: 65, lastPrice: 950.35, venue: "NASDAQ", streamId: "fixture-nvda" },
  QQQ: { bidPrice: 438.24, bidSize: 420, askPrice: 438.28, askSize: 390, lastPrice: 438.26, venue: "NASDAQ", streamId: "fixture-qqq" },
  SPY: { bidPrice: 512.44, bidSize: 500, askPrice: 512.48, askSize: 520, lastPrice: 512.46, venue: "NYSE Arca", streamId: "fixture-spy" }
};

const fixtureSymbolRecords: SymbolRecord[] = [
  { symbol: "AAPL", status: "Active", provider: "Alpaca", lastEventAt: fixtureMarketTimestamp, eventCount: 1842, hasHistoricalData: true },
  { symbol: "MSFT", status: "Active", provider: "Alpaca", lastEventAt: fixtureMarketTimestamp, eventCount: 1328, hasHistoricalData: true },
  { symbol: "QQQ", status: "Monitored", provider: "Alpaca", lastEventAt: fixtureMarketTimestamp, eventCount: 942, hasHistoricalData: true },
  { symbol: "SPY", status: "Monitored", provider: "Alpaca", lastEventAt: fixtureMarketTimestamp, eventCount: 1104, hasHistoricalData: true }
];

const fixtureSymbolStatistics: SymbolStatistics = {
  totalSymbols: fixtureSymbolRecords.length,
  monitoredSymbols: fixtureSymbolRecords.filter((symbol) => symbol.status === "Active" || symbol.status === "Monitored").length,
  archivedSymbols: 0,
  symbolsWithErrors: 0,
  totalEventsLast24h: fixtureSymbolRecords.reduce((total, symbol) => total + symbol.eventCount, 0)
};

export const marketDataFixtureRoutes = {
  [SYMBOL_API_ENDPOINTS.symbols]: fixtureSymbolRecords,
  [SYMBOL_API_ENDPOINTS.statistics]: fixtureSymbolStatistics
} satisfies Record<string, unknown>;

export const marketDataFixturePatterns: DynamicFixturePattern[] = [
  { pattern: apiRoutePattern(MARKET_DATA_API_ENDPOINTS.quotes, "/[^/]+"), resolve: (cleanPath) => buildFixtureQuote(readSymbolFromPath(cleanPath)) },
  { pattern: apiRoutePattern(MARKET_DATA_API_ENDPOINTS.trades, "/[^/]+"), resolve: (cleanPath) => buildFixtureTrades(readSymbolFromPath(cleanPath)) },
  { pattern: apiRoutePattern(MARKET_DATA_API_ENDPOINTS.orderbook, "/[^/]+"), resolve: (cleanPath) => buildFixtureOrderbook(readSymbolFromPath(cleanPath)) },
  {
    pattern: apiRoutePattern(MARKET_DATA_API_ENDPOINTS.historical, "/[^/]+/bars"),
    resolve: (cleanPath, path) => buildFixtureHistoricalBars(readSymbolFromPath(cleanPath, 1), path)
  },
  { pattern: apiRoutePattern(MARKET_DATA_API_ENDPOINTS.quotesSnapshot), resolve: (_cleanPath, path) => buildFixtureQuotesSnapshot(path) }
];

function getFixtureMarketProfile(symbol: string): FixtureMarketProfile {
  const normalized = symbol.trim().toUpperCase();
  const known = fixtureMarketProfiles[normalized];
  if (known) {
    return known;
  }

  const offset = Math.max(0, Math.min(8, normalized.length)) * 0.13;
  return {
    ...fixtureMarketProfiles.AAPL!,
    bidPrice: roundMarketPrice(fixtureMarketProfiles.AAPL!.bidPrice + offset),
    askPrice: roundMarketPrice(fixtureMarketProfiles.AAPL!.askPrice + offset),
    lastPrice: roundMarketPrice(fixtureMarketProfiles.AAPL!.lastPrice + offset),
    streamId: `fixture-${normalized.toLowerCase()}`
  };
}

function buildFixtureQuote(symbol: string): QuotesResponse {
  const normalized = symbol.trim().toUpperCase() || "AAPL";
  const profile = getFixtureMarketProfile(normalized);
  return {
    symbol: normalized,
    timestamp: fixtureMarketTimestamp,
    quote: {
      symbol: normalized,
      timestamp: fixtureMarketTimestamp,
      bidPrice: profile.bidPrice,
      bidSize: profile.bidSize,
      askPrice: profile.askPrice,
      askSize: profile.askSize,
      midPrice: roundMarketPrice((profile.bidPrice + profile.askPrice) / 2),
      spread: roundMarketPrice(profile.askPrice - profile.bidPrice),
      sequenceNumber: 42,
      streamId: profile.streamId,
      venue: profile.venue,
      session: null
    }
  };
}

function buildFixtureQuotesSnapshot(path: string): QuotesSnapshotResponse {
  const params = readFixtureSearchParams(path);
  const requestedSymbols = (params.get("symbols") ?? "")
    .split(",")
    .map((symbol) => symbol.trim().toUpperCase())
    .filter(Boolean);
  const symbols = requestedSymbols.length > 0 ? requestedSymbols : fixtureSymbolRecords.map((symbol) => symbol.symbol);

  return {
    timestamp: fixtureMarketTimestamp,
    count: symbols.length,
    quotes: symbols.map((symbol, index) => {
      const profile = getFixtureMarketProfile(symbol);
      return {
        symbol,
        timestamp: fixtureMarketTimestamp,
        bidPrice: profile.bidPrice,
        bidSize: profile.bidSize,
        askPrice: profile.askPrice,
        askSize: profile.askSize,
        midPrice: roundMarketPrice((profile.bidPrice + profile.askPrice) / 2),
        spread: roundMarketPrice(profile.askPrice - profile.bidPrice),
        lastPrice: profile.lastPrice,
        lastSize: 100 + index * 25,
        lastTradeTimestamp: fixtureMarketTimestamp,
        sequenceNumber: 1000 + index,
        streamId: profile.streamId,
        venue: profile.venue,
        session: null
      };
    })
  };
}

function buildFixtureTrades(symbol: string): TradesResponse {
  const normalized = symbol.trim().toUpperCase() || "AAPL";
  const profile = getFixtureMarketProfile(normalized);
  const baseTimestamp = new Date(fixtureMarketTimestamp).getTime();
  const offsets = [0.03, -0.01, 0.07, -0.04, -0.08, 0.02, -0.12, -0.05];
  const trades = offsets.map((offset, index) => ({
    symbol: normalized,
    timestamp: new Date(baseTimestamp - index * 30_000).toISOString(),
    price: roundMarketPrice(profile.lastPrice + offset),
    size: 50 + index * 25,
    aggressor: index % 3 === 0 ? "Buy" : index % 3 === 1 ? "Sell" : "Neutral",
    sequenceNumber: 500 - index,
    streamId: profile.streamId,
    venue: profile.venue
  }));

  return {
    symbol: normalized,
    trades,
    count: trades.length,
    timestamp: fixtureMarketTimestamp
  };
}

function buildFixtureOrderbook(symbol: string): OrderBookResponse {
  const normalized = symbol.trim().toUpperCase() || "AAPL";
  const profile = getFixtureMarketProfile(normalized);
  return {
    symbol: normalized,
    timestamp: fixtureMarketTimestamp,
    bids: [0, 1, 2, 3, 4].map((level) => ({
      side: "Bid",
      level: level + 1,
      price: roundMarketPrice(profile.bidPrice - level * 0.02),
      size: Math.max(25, profile.bidSize - level * 20),
      marketMaker: null
    })),
    asks: [0, 1, 2, 3, 4].map((level) => ({
      side: "Ask",
      level: level + 1,
      price: roundMarketPrice(profile.askPrice + level * 0.02),
      size: Math.max(25, profile.askSize - level * 15),
      marketMaker: null
    })),
    midPrice: roundMarketPrice((profile.bidPrice + profile.askPrice) / 2),
    imbalance: roundMarketPrice((profile.bidSize - profile.askSize) / Math.max(1, profile.bidSize + profile.askSize)),
    marketState: "Open",
    sequenceNumber: 42,
    isStale: false,
    streamId: profile.streamId,
    venue: profile.venue
  };
}

function buildFixtureHistoricalBars(symbol: string, path: string): HistoricalBarsResponse {
  const normalized = symbol.trim().toUpperCase() || "AAPL";
  const profile = getFixtureMarketProfile(normalized);
  const params = readFixtureSearchParams(path);
  const intervalMinutes = Number(params.get("intervalMinutes") ?? 5);
  const start = new Date("2026-05-08T13:30:00.000Z").getTime();
  const offsets = [-0.72, -0.46, -0.3, -0.16, 0.05, 0.12, 0.01, 0.18, 0.31, 0.24, 0.36, 0.29];
  const bars = offsets.map((offset, index) => {
    const open = roundMarketPrice(profile.lastPrice + offset);
    const close = roundMarketPrice(profile.lastPrice + offsets[Math.min(index + 1, offsets.length - 1)]!);
    const high = roundMarketPrice(Math.max(open, close) + 0.08);
    const low = roundMarketPrice(Math.min(open, close) - 0.07);
    const volume = 15_000 + index * 1_250;
    return {
      start: new Date(start + index * intervalMinutes * 60_000).toISOString(),
      open,
      high,
      low,
      close,
      volume,
      vwap: roundMarketPrice((open + high + low + close) / 4),
      tradeCount: 40 + index * 3
    };
  });

  return {
    success: true,
    message: null,
    symbol: normalized,
    intervalMinutes: Number.isFinite(intervalMinutes) && intervalMinutes > 0 ? intervalMinutes : 5,
    from: params.get("from"),
    to: params.get("to"),
    totalBars: bars.length,
    filesProcessed: 1,
    totalFiles: 1,
    queryTimeMs: 3,
    bars
  };
}

function roundMarketPrice(value: number): number {
  return Math.round(value * 10000) / 10000;
}
