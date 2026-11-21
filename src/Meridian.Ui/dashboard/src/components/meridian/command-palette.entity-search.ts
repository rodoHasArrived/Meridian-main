import { useEffect, useState } from "react";
import { getJson, type ApiRequestOptions } from "@/lib/api";
import { isApiError } from "@/lib/api-errors";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import { WORKSTATION_ROUTE_CATALOG, appendRouteQuery } from "@/lib/workspace";
import type { CommandPaletteEntityItem } from "@/components/meridian/command-palette.view-model";

const SEARCH_DEBOUNCE_MS = 250;
const MAX_QUERY_LENGTH = 256;
const SYMBOL_RESULT_LIMIT = 8;

export interface WorkstationRecordSearchHit {
  recordType: string;
  recordId: string;
  label: string;
  scope: string;
  asOfPosture: string;
  status: string;
  href: string;
}

export interface WorkstationRecordSearchResult {
  items: WorkstationRecordSearchHit[];
  isPartial: boolean;
}

interface CommandPaletteEntitySearchState {
  items: CommandPaletteEntityItem[];
  status: "idle" | "searching" | "ready" | "degraded" | "error";
  error: string | null;
}

interface PaletteSymbol {
  symbol: string;
  status?: string;
}

export interface CommandPaletteEntitySearchServices {
  searchSymbols: (query: string, options?: ApiRequestOptions) => Promise<PaletteSymbol[]>;
  searchRecords: (query: string, options?: ApiRequestOptions) => Promise<WorkstationRecordSearchResult>;
}

const idleState: CommandPaletteEntitySearchState = { items: [], status: "idle", error: null };
const searchingState: CommandPaletteEntitySearchState = { items: [], status: "searching", error: null };

const defaultSearchServices: CommandPaletteEntitySearchServices = {
  searchSymbols: async (query, options) => {
    const response = await getJson<{ results: PaletteSymbol[] } | PaletteSymbol[]>(
      `${UI_API_ROUTES.SymbolsSearch}?${new URLSearchParams({ q: query })}`, options);
    const results = Array.isArray(response) ? response : response?.results;
    if (!Array.isArray(results)) {
      throw new Error("Invalid market symbol search response");
    }
    return results;
  },
  searchRecords: (query, options) => getJson(
    `${UI_API_ROUTES.WorkstationRecordSearch}?${new URLSearchParams({ q: query })}`, options)
};

export function useCommandPaletteEntitySearch({
  open,
  query,
  scopeKey = "",
  services = defaultSearchServices
}: {
  open: boolean;
  query: string;
  scopeKey?: string;
  services?: CommandPaletteEntitySearchServices;
}): CommandPaletteEntitySearchState {
  const [response, setResponse] = useState<{
    query: string;
    scopeKey: string;
    services: CommandPaletteEntitySearchServices;
    state: CommandPaletteEntitySearchState;
  } | null>(null);
  const normalizedQuery = query.trim();
  const enabled = open && normalizedQuery.length > 0 && normalizedQuery.length <= MAX_QUERY_LENGTH;

  useEffect(() => {
    setResponse(null);
    if (!enabled) {
      return;
    }

    const controller = new AbortController();
    const options = { signal: controller.signal, allowDevelopmentFallback: false };
    const timer = window.setTimeout(async () => {
      const [recordResult, symbolResult] = await Promise.allSettled([
        services.searchRecords(normalizedQuery, options),
        services.searchSymbols(normalizedQuery, options)
      ]);
      if (controller.signal.aborted) {
        return;
      }

      const recordResponse = recordResult.status === "fulfilled" ? recordResult.value : null;
      const symbols = symbolResult.status === "fulfilled" ? symbolResult.value : [];
      // A refused family contributes neither a hit nor an error that distinguishes a hidden match.
      const recordFailure = recordResult.status === "rejected" && !isAccessRefusal(recordResult.reason);
      const symbolFailure = symbolResult.status === "rejected" && !isAccessRefusal(symbolResult.reason);
      const degraded = recordResponse?.isPartial || recordFailure || symbolFailure;
      setResponse({
        query: normalizedQuery,
        scopeKey,
        services,
        state: {
          items: buildCommandPaletteEntityItems(recordResponse?.items ?? [], symbols, normalizedQuery),
          status: recordFailure && symbolFailure ? "error" : degraded ? "degraded" : "ready",
          error: degraded ? "Some record sources are temporarily unavailable" : null
        }
      });
    }, SEARCH_DEBOUNCE_MS);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [enabled, normalizedQuery, scopeKey, services]);

  if (!enabled) {
    return idleState;
  }
  return response?.query === normalizedQuery && response.scopeKey === scopeKey && response.services === services
    ? response.state
    : searchingState;
}

function isAccessRefusal(reason: unknown) {
  return isApiError(reason) && (reason.status === 401 || reason.status === 403);
}

export function buildCommandPaletteEntityItems(
  records: WorkstationRecordSearchHit[],
  symbols: PaletteSymbol[],
  query: string
): CommandPaletteEntityItem[] {
  const term = query.toUpperCase();
  const eligibleSymbols = symbols.filter((record) => {
    const symbol = record.symbol.toUpperCase();
    return symbol === term || (term.length >= 2 && symbol.startsWith(term));
  });
  const symbolItems = [...new Map(eligibleSymbols.map((record) => [record.symbol.toUpperCase(), record])).values()]
    .sort((left, right) => Number(right.symbol.toUpperCase() === term) - Number(left.symbol.toUpperCase() === term)
      || left.symbol.localeCompare(right.symbol))
    .slice(0, SYMBOL_RESULT_LIMIT)
    .map(buildSymbolEntityItem);

  return [...records.map(buildRecordEntityItem), ...symbolItems];
}

function buildRecordEntityItem(record: WorkstationRecordSearchHit): CommandPaletteEntityItem {
  const status = record.status.replace(/([a-z\d])([A-Z])/g, "$1 $2");
  const description = `${record.scope} · ${record.asOfPosture}`;
  return {
    id: `record:${record.href}`,
    label: record.label,
    description,
    route: record.href,
    sourceLabel: record.recordType,
    statusLabel: status,
    recordId: record.recordId,
    commandLabel: record.label,
    ariaLabel: `Open ${record.recordType} ${record.label}, ${record.recordId}, ${record.scope}, ${record.asOfPosture}, ${status}`
  };
}

function buildSymbolEntityItem(record: PaletteSymbol): CommandPaletteEntityItem {
  const symbol = record.symbol.toUpperCase();
  return {
    id: `symbol:${symbol}`,
    label: symbol,
    description: "Market data catalog",
    route: appendRouteQuery(WORKSTATION_ROUTE_CATALOG.dataQuotes, { symbol }),
    sourceLabel: "Symbol",
    statusLabel: record.status ?? "Configured",
    commandLabel: `Open ${symbol} quotes`,
    ariaLabel: `Open symbol ${symbol} in Live quotes`
  };
}
