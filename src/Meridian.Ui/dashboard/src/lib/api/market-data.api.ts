// Symbol management, data quality monitoring, and live market data endpoints. Re-exported through @/lib/api.
import { QUALITY_API_ENDPOINTS, SYMBOL_API_ENDPOINTS, historicalBarsEndpoint, marketDataOrderbookEndpoint, marketDataQuoteEndpoint, marketDataQuotesSnapshotEndpoint, marketDataTradesEndpoint, symbolArchiveEndpoint, symbolRemoveEndpoint, symbolSearchEndpoint } from "@/lib/workstation-endpoints";
import { getJson, postJson, type ApiRequestOptions } from "@/lib/api/http-client";

// --- Symbol management ---

export function getSymbols(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").SymbolRecord[]>(SYMBOL_API_ENDPOINTS.symbols, options);
}

export function getCanonicalSymbolRegistry(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").CanonicalSymbolRegistryResponse>(SYMBOL_API_ENDPOINTS.registry, options);
}

export function getSymbolsStatistics(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").SymbolStatistics>(SYMBOL_API_ENDPOINTS.statistics, options);
}

export function searchSymbolsQuery(query: string, options: ApiRequestOptions = {}) {
  return getJson<import("@/types").SymbolRecord[]>(symbolSearchEndpoint(query), options);
}

export function addSymbol(symbol: string, provider?: string) {
  return postJson<{ success: boolean; symbol: string }>(SYMBOL_API_ENDPOINTS.add, { symbol, provider: provider ?? null });
}

export function removeSymbol(symbol: string) {
  return postJson<{ success: boolean; symbol: string }>(symbolRemoveEndpoint(symbol));
}

export function archiveSymbol(symbol: string) {
  return postJson<{ success: boolean; symbol: string }>(symbolArchiveEndpoint(symbol));
}

export function bulkAddSymbols(symbols: string[]) {
  return postJson<{ added: number; skipped: number; errors: string[] }>(SYMBOL_API_ENDPOINTS.bulkAdd, { symbols });
}

// --- Quality monitoring ---

export function getQualityDashboard() {
  return getJson<import("@/types").QualityDashboardResponse>(QUALITY_API_ENDPOINTS.dashboard);
}

export function getQualityGaps() {
  return getJson<import("@/types").QualityGapEntry[]>(QUALITY_API_ENDPOINTS.gaps);
}

export function remediateQualityGap(
  symbol: string,
  request: import("@/types").QualityGapRemediationRequest
) {
  return postJson<import("@/types").QualityGapRemediationResponse>(
    `${QUALITY_API_ENDPOINTS.gaps}/${encodeURIComponent(symbol)}`,
    request
  );
}

// --- Live market data ---

export function getLiveQuote(symbol: string, options: ApiRequestOptions = {}) {
  return getJson<import("@/types").QuotesResponse>(marketDataQuoteEndpoint(symbol), options);
}

export function getLiveTrades(symbol: string, limit = 25, options: ApiRequestOptions = {}) {
  return getJson<import("@/types").TradesResponse>(marketDataTradesEndpoint(symbol, limit), options);
}

export function getLiveOrderbook(symbol: string, levels = 10, options: ApiRequestOptions = {}) {
  return getJson<import("@/types").OrderBookResponse>(marketDataOrderbookEndpoint(symbol, levels), options);
}

export function getLiveQuotesSnapshot(symbols?: readonly string[], options: ApiRequestOptions = {}) {
  return getJson<import("@/types").QuotesSnapshotResponse>(marketDataQuotesSnapshotEndpoint(symbols), options);
}

export interface HistoricalBarsRequest {
  intervalMinutes: number;
  from?: string;
  to?: string;
  maxBars?: number;
}

export function getHistoricalBars(symbol: string, request: HistoricalBarsRequest, options: ApiRequestOptions = {}) {
  return getJson<import("@/types").HistoricalBarsResponse>(historicalBarsEndpoint(symbol, request), options);
}
