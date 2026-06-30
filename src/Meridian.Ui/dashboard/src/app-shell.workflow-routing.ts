import {
  normalizeLocalWorkstationRoute,
  WORKSTATION_ROUTE_CATALOG,
  workflowTargetPath,
  workspaceForPath
} from "@/lib/workspace";
import type { OperatorWorkItem } from "@/types";

export function routeForOperatorWorkItem(item: OperatorWorkItem): string {
  if (item.kind === "BrokerageSync") {
    return WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup;
  }

  return normalizeLocalWorkstationRoute(item.targetRoute)
    ?? routeFromOperatorWorkItemTarget(item)
    ?? fallbackRouteForOperatorWorkItemKind(item.kind);
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
    case "PromotionReview":
    case "ExecutionControl":
      return WORKSTATION_ROUTE_CATALOG.tradingReadiness;
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

function routePathname(route: string): string {
  const hashIndex = route.indexOf("#");
  const routeWithoutHash = hashIndex >= 0 ? route.slice(0, hashIndex) : route;
  const searchIndex = routeWithoutHash.indexOf("?");
  return searchIndex >= 0 ? routeWithoutHash.slice(0, searchIndex) : routeWithoutHash;
}
