import { useEffect, useState } from "react";
import {
  searchSecurities as defaultSearchSecurities,
  searchSymbolsQuery as defaultSearchSymbolsQuery,
  type ApiRequestOptions
} from "@/lib/api";
import { WORKSTATION_ROUTE_CATALOG, appendRouteQuery } from "@/lib/workspace";
import type { SecurityMasterEntry, SymbolRecord } from "@/types";
import type { CommandPaletteEntityItem } from "@/components/meridian/command-palette.view-model";

const COMMAND_PALETTE_ENTITY_DEBOUNCE_MS = 250;
const COMMAND_PALETTE_ENTITY_RESULT_LIMIT = 8;

export type CommandPaletteEntitySearchStatus = "idle" | "searching" | "ready" | "degraded" | "error";

export interface CommandPaletteEntitySearchState {
  items: CommandPaletteEntityItem[];
  status: CommandPaletteEntitySearchStatus;
  error: string | null;
}

export interface CommandPaletteEntitySearchServices {
  searchSymbols: (query: string, options?: ApiRequestOptions) => Promise<SymbolRecord[]>;
  searchSecurities: (
    query: string,
    take?: number,
    activeOnly?: boolean,
    options?: ApiRequestOptions
  ) => Promise<SecurityMasterEntry[]>;
}

const emptyEntitySearchState: CommandPaletteEntitySearchState = {
  items: [],
  status: "idle",
  error: null
};

const defaultCommandPaletteEntitySearchServices: CommandPaletteEntitySearchServices = {
  searchSymbols: defaultSearchSymbolsQuery,
  searchSecurities: defaultSearchSecurities
};

export function useCommandPaletteEntitySearch({
  open,
  query,
  services = defaultCommandPaletteEntitySearchServices
}: {
  open: boolean;
  query: string;
  services?: CommandPaletteEntitySearchServices;
}): CommandPaletteEntitySearchState {
  const [state, setState] = useState<CommandPaletteEntitySearchState>(emptyEntitySearchState);
  const normalizedQuery = query.trim();

  useEffect(() => {
    if (!open || normalizedQuery.length < 2) {
      setState(emptyEntitySearchState);
      return;
    }

    const controller = new AbortController();
    const timeout = window.setTimeout(() => {
      setState((current) => ({ ...current, status: "searching", error: null }));

      void Promise.allSettled([
        services.searchSymbols(normalizedQuery, { signal: controller.signal }),
        services.searchSecurities(normalizedQuery, COMMAND_PALETTE_ENTITY_RESULT_LIMIT, true, {
          signal: controller.signal
        })
      ]).then(([symbolResult, securityResult]) => {
        if (controller.signal.aborted) {
          return;
        }

        const symbols = symbolResult.status === "fulfilled" ? symbolResult.value : [];
        const securities = securityResult.status === "fulfilled" ? securityResult.value : [];
        const failures = [
          symbolResult.status === "rejected" ? "symbol search" : null,
          securityResult.status === "rejected" ? "security search" : null
        ].filter((failure): failure is string => failure !== null);

        setState({
          items: buildCommandPaletteEntityItems(symbols, securities),
          status: failures.length === 0 ? "ready" : failures.length === 2 ? "error" : "degraded",
          error: failures.length > 0 ? `${failures.join(" and ")} unavailable` : null
        });
      });
    }, COMMAND_PALETTE_ENTITY_DEBOUNCE_MS);

    return () => {
      window.clearTimeout(timeout);
      controller.abort();
    };
  }, [normalizedQuery, open, services]);

  return state;
}

export function buildCommandPaletteEntityItems(
  symbols: SymbolRecord[],
  securities: SecurityMasterEntry[]
): CommandPaletteEntityItem[] {
  return [
    ...dedupeSymbols(symbols).slice(0, COMMAND_PALETTE_ENTITY_RESULT_LIMIT).map(buildSymbolEntityItem),
    ...dedupeSecurities(securities).slice(0, COMMAND_PALETTE_ENTITY_RESULT_LIMIT).map(buildSecurityEntityItem)
  ];
}

function buildSymbolEntityItem(record: SymbolRecord): CommandPaletteEntityItem {
  const symbol = record.symbol.toUpperCase();
  const provider = record.provider ? `Provider ${record.provider}` : "Provider not assigned";
  const history = record.hasHistoricalData ? "Historical data retained" : "No historical data retained";
  return {
    id: `symbol:${symbol}`,
    label: symbol,
    description: `${provider} - ${record.status} - ${history} - ${record.eventCount} event${record.eventCount === 1 ? "" : "s"}`,
    route: appendRouteQuery(WORKSTATION_ROUTE_CATALOG.dataQuotes, { symbol }),
    sourceLabel: "Symbol",
    statusLabel: record.status,
    commandLabel: `Open ${symbol} quotes`,
    ariaLabel: `Open symbol ${symbol} in Live quotes`
  };
}

function buildSecurityEntityItem(entry: SecurityMasterEntry): CommandPaletteEntityItem {
  const identifier = entry.classification.primaryIdentifierValue ?? entry.securityId;
  const assetClass = entry.classification.assetClass;
  return {
    id: `security:${entry.securityId}`,
    label: entry.displayName,
    description: `${assetClass} - ${identifier} - ${entry.status}`,
    route: appendRouteQuery(WORKSTATION_ROUTE_CATALOG.accountingAssetDetail, {
      securityId: entry.securityId
    }),
    sourceLabel: "Security",
    statusLabel: entry.status,
    commandLabel: `Open ${entry.displayName} security detail`,
    ariaLabel: `Open security ${entry.displayName} in Security Master detail`
  };
}

function dedupeSymbols(symbols: SymbolRecord[]): SymbolRecord[] {
  const seen = new Set<string>();
  const result: SymbolRecord[] = [];
  for (const record of symbols) {
    const key = record.symbol.toUpperCase();
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    result.push(record);
  }
  return result;
}

function dedupeSecurities(securities: SecurityMasterEntry[]): SecurityMasterEntry[] {
  const seen = new Set<string>();
  const result: SecurityMasterEntry[] = [];
  for (const entry of securities) {
    if (seen.has(entry.securityId)) {
      continue;
    }
    seen.add(entry.securityId);
    result.push(entry);
  }
  return result;
}
