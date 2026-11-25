import {
  appendRouteQuery,
  normalizeLocalWorkstationRoute,
  WORKSTATION_ROUTE_CATALOG,
  workflowTargetPath,
  workspaceForPath
} from "@/lib/workspace";
import type { OperatorWorkItem } from "@/types";

export function routeForOperatorWorkItem(item: OperatorWorkItem): string {
  const route = item.kind === "BrokerageSync"
    ? WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup
    : normalizeLocalWorkstationRoute(item.targetRoute)
      ?? routeFromOperatorWorkItemTarget(item)
      ?? fallbackRouteForOperatorWorkItemKind(item.kind);

  return withOperatorWorkItemScope(canonicalTradingWorkItemRoute(route, item.kind), item);
}

/** The source account owns this record link; remembered scope must not narrow it to another fund. */
export function withOperatorWorkItemScope(route: string, item: Pick<OperatorWorkItem, "fundAccountId">): string {
  const fundAccountId = item.fundAccountId?.trim();
  return fundAccountId ? appendRouteQuery(route, { fundAccountId, recordScope: "1" }) : route;
}

export function actionLabelForOperatorWorkItem(item: OperatorWorkItem): string {
  switch (item.kind) {
    case "PaperReplay":
      return "Open replay evidence";
    case "PromotionReview":
      return "Open promotion review";
    case "BrokerageSync":
      return "Fix provider setup";
    case "SecurityMasterCoverage":
      return "Open Security Master";
    case "ReconciliationBreak":
      return "Open break queue";
    case "LedgerPeriodClose":
      return "Open reconciliation";
    case "ReportPackApproval":
      return "Open report packs";
    case "ProviderTrustGate":
      return "Open provider trust";
    case "ExecutionControl":
      return "Open execution controls";
    case "BrokerExecutionReconciliation":
      return "Review broker orders";
  }
}

export function workspaceLabelForRoute(route: string): string {
  return workspaceForPath(routePathname(route)).label;
}

function routeFromOperatorWorkItemTarget(item: OperatorWorkItem): string | null {
  if (!item.targetPageTag && !item.workspace) {
    return null;
  }

  return workflowTargetPath(item.targetPageTag, item.workspace);
}

function fallbackRouteForOperatorWorkItemKind(kind: OperatorWorkItem["kind"]): string {
  switch (kind) {
    case "PaperReplay":
      return `${WORKSTATION_ROUTE_CATALOG.trading}#session-replay-panel`;
    case "PromotionReview":
      return `${WORKSTATION_ROUTE_CATALOG.trading}#promotion-gate-panel`;
    case "ExecutionControl":
      return WORKSTATION_ROUTE_CATALOG.tradingRisk;
    case "BrokerExecutionReconciliation":
      return `${WORKSTATION_ROUTE_CATALOG.trading}#brokerage-recovery`;
    case "BrokerageSync":
      return WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup;
    case "SecurityMasterCoverage":
      return WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster;
    case "ReconciliationBreak":
    case "LedgerPeriodClose":
      return WORKSTATION_ROUTE_CATALOG.accountingReconciliation;
    case "ReportPackApproval":
      return WORKSTATION_ROUTE_CATALOG.reportingReportPacks;
    case "ProviderTrustGate":
      return WORKSTATION_ROUTE_CATALOG.dataProviders;
  }
}

/** Generic and retired Trading targets still need the work item's actual task surface. */
function canonicalTradingWorkItemRoute(route: string, kind: OperatorWorkItem["kind"]): string {
  const pathname = routePathname(route);
  const canonicalPathname = pathname.replace(/\/+$/, "");
  if (canonicalPathname !== WORKSTATION_ROUTE_CATALOG.trading && canonicalPathname !== WORKSTATION_ROUTE_CATALOG.tradingReadiness) {
    return route;
  }
  const target = fallbackRouteForOperatorWorkItemKind(kind);
  if (!target.startsWith(`${WORKSTATION_ROUTE_CATALOG.trading}#`) && target !== WORKSTATION_ROUTE_CATALOG.tradingRisk) {
    return route;
  }
  const hashIndex = route.indexOf("#");
  const search = route.slice(pathname.length, hashIndex >= 0 ? hashIndex : undefined);
  const targetPath = routePathname(target);
  const hash = hashIndex >= 0 && canonicalPathname !== WORKSTATION_ROUTE_CATALOG.tradingReadiness
    ? route.slice(hashIndex)
    : target.slice(targetPath.length);
  return `${targetPath}${search}${hash}`;
}

function routePathname(route: string): string {
  const hashIndex = route.indexOf("#");
  const routeWithoutHash = hashIndex >= 0 ? route.slice(0, hashIndex) : route;
  const searchIndex = routeWithoutHash.indexOf("?");
  return searchIndex >= 0 ? routeWithoutHash.slice(0, searchIndex) : routeWithoutHash;
}
