import { useCallback, useEffect, useMemo, useState } from "react";
import { useRequestLifecycle } from "@/hooks/use-request-lifecycle";
import { getCanonicalSymbolRegistry } from "@/lib/api";
import type {
  CanonicalSymbolRegistryEntryResponse,
  CanonicalSymbolRegistryResponse,
  SymbolResolutionMode
} from "@/types";

export type CanonicalSymbolRegistryFetcher = typeof getCanonicalSymbolRegistry;
export type CanonicalSymbolModeTone = "success" | "warning" | "danger" | "info";

export interface CanonicalSymbolRegistryRow extends CanonicalSymbolRegistryEntryResponse {
  searchIndex: string;
}

export interface CanonicalSymbolRegistryPanelModel {
  registryVersion: string;
  resolutionMode: SymbolResolutionMode;
  modeTone: CanonicalSymbolModeTone;
  modeTitle: string;
  modeDetail: string;
  compareModeReturnsLegacy: boolean;
  totalMismatchCount: number;
  lastMismatchAt: string | null;
  recentMismatches: CanonicalSymbolRegistryResponse["recentMismatches"];
  migrations: CanonicalSymbolRegistryResponse["migrations"];
  symbols: CanonicalSymbolRegistryRow[];
  providerAliasCount: number;
  summary: string;
}

export interface CanonicalSymbolRegistryPanelViewModel {
  loading: boolean;
  error: string | null;
  model: CanonicalSymbolRegistryPanelModel | null;
  query: string;
  visibleSymbols: CanonicalSymbolRegistryRow[];
  setQuery: (query: string) => void;
  refresh: () => Promise<void>;
}

function modePresentation(mode: SymbolResolutionMode): Pick<
  CanonicalSymbolRegistryPanelModel,
  "modeTone" | "modeTitle" | "modeDetail"
> {
  switch (mode) {
    case "Canonical":
      return {
        modeTone: "success",
        modeTitle: "Canonical resolution active",
        modeDetail: "Provider requests use registry identity and provider-scoped aliases, with legacy fallback only for unknown securities."
      };
    case "Legacy":
      return {
        modeTone: "danger",
        modeTitle: "Legacy resolution active",
        modeDetail: "Provider requests bypass canonical registry decisions. Use Compare mode before promoting this environment to Canonical."
      };
    default:
      return {
        modeTone: "warning",
        modeTitle: "Compare mode preserves legacy output",
        modeDetail: "Each provider translation evaluates both paths, records disagreements, and still returns the legacy result."
      };
  }
}

function searchableValues(symbol: CanonicalSymbolRegistryEntryResponse): Array<string | null> {
  return [
    symbol.securityId,
    symbol.canonicalTicker,
    symbol.displayName,
    symbol.assetClass,
    symbol.exchange,
    symbol.currency,
    symbol.identifiers.isin,
    symbol.identifiers.figi,
    symbol.identifiers.compositeFigi,
    symbol.identifiers.cusip,
    symbol.identifiers.sedol,
    ...symbol.aliases.flatMap((alias) => [alias.alias, alias.source, alias.provider]),
    ...symbol.providerAliases.flatMap((alias) => [alias.provider, alias.symbol, alias.source]),
    ...symbol.provenanceSources
  ];
}

/** Project the shared registry snapshot into browser-only labels and a stable alias search index. */
export function buildCanonicalSymbolRegistryPanelModel(
  response: CanonicalSymbolRegistryResponse
): CanonicalSymbolRegistryPanelModel {
  const symbols = response.symbols
    .map((symbol) => ({
      ...symbol,
      searchIndex: searchableValues(symbol)
        .filter((value): value is string => Boolean(value?.trim()))
        .join(" ")
        .toLocaleLowerCase()
    }))
    .sort((left, right) => left.canonicalTicker.localeCompare(right.canonicalTicker));
  const providerAliasCount = symbols.reduce((total, symbol) => total + symbol.providerAliases.length, 0);

  return {
    registryVersion: response.registryVersion,
    resolutionMode: response.resolutionMode,
    ...modePresentation(response.resolutionMode),
    compareModeReturnsLegacy: response.compareModeReturnsLegacy,
    totalMismatchCount: response.totalMismatchCount,
    lastMismatchAt: response.lastMismatchAt,
    recentMismatches: response.recentMismatches,
    migrations: response.migrations,
    symbols,
    providerAliasCount,
    summary: `${symbols.length} canonical securit${symbols.length === 1 ? "y" : "ies"}, ` +
      `${providerAliasCount} provider alias${providerAliasCount === 1 ? "" : "es"}, ` +
      `${response.migrations.length} migration receipt${response.migrations.length === 1 ? "" : "s"}.`
  };
}

/** Match canonical ticker, SecurityId, identifiers, arbitrary aliases, providers, and provenance. */
export function filterCanonicalSymbols(
  symbols: CanonicalSymbolRegistryRow[],
  query: string
): CanonicalSymbolRegistryRow[] {
  const terms = query.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) return symbols;
  return symbols.filter((symbol) => terms.every((term) => symbol.searchIndex.includes(term)));
}

export function useCanonicalSymbolRegistryPanel(
  fetchRegistry: CanonicalSymbolRegistryFetcher = getCanonicalSymbolRegistry
): CanonicalSymbolRegistryPanelViewModel {
  const { status, start, succeed, fail, finish } = useRequestLifecycle({
    operation: "canonical-symbol-registry",
    failureMessage: "Canonical symbol registry is unavailable."
  });
  const [model, setModel] = useState<CanonicalSymbolRegistryPanelModel | null>(null);
  const [query, setQuery] = useState("");

  const refresh = useCallback(async () => {
    const request = start({ busyMode: "supersede" });
    if (!request) return;

    try {
      const response = await fetchRegistry({ signal: request.signal });
      if (!request.isCurrent()) return;
      request.safeSetState(setModel, buildCanonicalSymbolRegistryPanelModel(response));
      succeed(request);
    } catch (fetchError) {
      if (!request.isCurrent()) return;
      request.safeSetState(setModel, null);
      fail(request, fetchError);
    } finally {
      finish(request);
    }
  }, [fail, fetchRegistry, finish, start, succeed]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const visibleSymbols = useMemo(
    () => filterCanonicalSymbols(model?.symbols ?? [], query),
    [model?.symbols, query]
  );

  return {
    loading: status.phase === "idle" || status.inFlight,
    error: status.error,
    model,
    query,
    visibleSymbols,
    setQuery,
    refresh
  };
}
