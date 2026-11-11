import { useEffect, useState } from "react";
import {
  searchSecurities as defaultSearchSecurities,
  searchSymbolsQuery as defaultSearchSymbolsQuery,
  getReconciliationStatementRun,
  type ApiRequestOptions
} from "@/lib/api";
import { WORKSTATION_ROUTE_CATALOG, appendRouteQuery } from "@/lib/workspace";
import type { SecurityMasterEntry, SymbolRecord } from "@/types";
import type { CommandPaletteEntityItem } from "@/components/meridian/command-palette.view-model";
import { getGovernedReportingRun } from "@/lib/reporting-governance-api";
import { isApiError } from "@/lib/api-errors";
import { parseCommandPaletteRecordQuery } from "./command-palette.entity-query";
import type { GovernedReportingRun } from "@/types/reporting-governance";
import type { StatementRunSummary } from "@/types";

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
  getReportRun?: (id: string, options?: ApiRequestOptions) => Promise<Pick<GovernedReportingRun, "runId" | "templateId" | "executionState" | "governanceState" | "scope">>;
  getStatementRun?: (id: string, options?: ApiRequestOptions) => Promise<StatementRunSummary>;
}

const emptyEntitySearchState: CommandPaletteEntitySearchState = {
  items: [],
  status: "idle",
  error: null
};

const defaultCommandPaletteEntitySearchServices: CommandPaletteEntitySearchServices = {
  searchSymbols: defaultSearchSymbolsQuery,
  searchSecurities: defaultSearchSecurities,
  getReportRun: getGovernedReportingRun,
  getStatementRun: getReconciliationStatementRun
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
  const [state, setState] = useState<CommandPaletteEntitySearchState & { query?: string }>(emptyEntitySearchState);
  const normalizedQuery = query.trim();

  useEffect(() => {
    if (!open || normalizedQuery.length < 2) {
      setState(emptyEntitySearchState);
      return;
    }

    const controller = new AbortController();
    const parsed = parseCommandPaletteRecordQuery(normalizedQuery);
    if (parsed.guidance) {
      setState({ query: normalizedQuery, items: [], status: parsed.kind === "unsupported" ? "error" : "idle", error: parsed.guidance });
      return () => controller.abort();
    }
    const timeout = window.setTimeout(() => {
      setState({ query: normalizedQuery, items: [], status: "searching", error: null });

      if (parsed.kind === "report" || parsed.kind === "statement") {
        const service = parsed.kind === "report" ? services.getReportRun : services.getStatementRun;
        if (!service) {
          setState({ query: normalizedQuery, items: [], status: "error", error: `${parsed.kind} lookup is unavailable in this session` });
          return;
        }
        const options = { signal: controller.signal, allowDevelopmentFallback: false };
        const request = parsed.kind === "report"
          ? services.getReportRun!(parsed.value, options).then(record => {
              if (record.runId !== parsed.value) throw new Error("The requested report run was not returned. No alternative record is shown.");
              return buildReportRunEntityItem(record);
            })
          : services.getStatementRun!(parsed.value, options).then(record => {
              if (record.runId !== parsed.value) throw new Error("The requested statement run was not returned. No alternative record is shown.");
              return buildStatementRunEntityItem(record);
            });
        void request.then(item => {
          if (!controller.signal.aborted) setState({ query: normalizedQuery, items: [item], status: "ready", error: null });
        }).catch(reason => {
          if (!controller.signal.aborted) setState({ query: normalizedQuery, items: [], status: "error", error: recordLookupError(reason, parsed.kind) });
        });
        return;
      }

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
          query: normalizedQuery,
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

  if (!open || normalizedQuery.length < 2) return emptyEntitySearchState;
  // Never expose a preceding query's financial record during debounce or loading.
  return state.query === normalizedQuery ? state : { items: [], status: "searching", error: null };
}

function recordLookupError(reason: unknown, kind: string): string {
  if (isApiError(reason)) {
    if (reason.status === 401) return "Sign in again to look up retained records.";
    if (reason.status === 403) return `Permission denied for this ${kind} record.`;
    if (reason.status === 404) return `No accessible ${kind} run matches this exact ID.`;
  }
  return reason instanceof Error && reason.message.startsWith("The requested")
    ? reason.message : `${kind} lookup service unavailable. Retry the search when the service is available.`;
}

function buildReportRunEntityItem(record: Awaited<ReturnType<NonNullable<CommandPaletteEntitySearchServices["getReportRun"]>>>): CommandPaletteEntityItem {
  return {
    id: `report:${record.runId}`, label: `Report run ${record.runId}`,
    description: `${record.templateId} · ${record.scope.periodId} · ${record.executionState} · ${record.governanceState}`,
    route: appendRouteQuery(WORKSTATION_ROUTE_CATALOG.reportingRunDetail, { runId: record.runId }),
    sourceLabel: "Report run", statusLabel: record.governanceState,
    commandLabel: `Inspect report run ${record.runId}`, ariaLabel: `Inspect retained report run ${record.runId}`
  };
}

function buildStatementRunEntityItem(record: StatementRunSummary): CommandPaletteEntityItem {
  return {
    id: `statement:${record.runId}`, label: `Statement run ${record.runId}`,
    description: `Import ${record.importId} · ${record.brokerCustodian ?? "Source not supplied"} · ${record.account ?? "Account not supplied"} · ${record.openExceptionCount} open exceptions`,
    route: `${appendRouteQuery(WORKSTATION_ROUTE_CATALOG.accountingReconciliation, { reconciliationRun: record.runId })}#statement-runs`,
    sourceLabel: "Statement run", statusLabel: record.status ?? "Retained",
    commandLabel: `Inspect statement run ${record.runId}`, ariaLabel: `Inspect statement run ${record.runId} and import ${record.importId}`
  };
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
