import type { WorkspaceSummary } from "@/types";
import { WORKSTATION_ROUTE_CATALOG, resolveWorkstationRouteBreadcrumbLabel } from "@/lib/workspace";
import { VIEW_STATE_QUERY_KEY } from "@/lib/view-state-envelope";
import { REPORTING_WORK_SELECTION_PARAM } from "@/components/meridian/reporting-hub.selection";

export interface AppShellRouteFocusState {
  routeKey: string;
  announcement: string;
  documentTitle: string;
  targetElementId: string | null;
  fallbackElementId: string;
}

export function buildRouteFocusState(
  pathname: string,
  search: string,
  hash: string,
  activeWorkspace: WorkspaceSummary
): AppShellRouteFocusState {
  // Name the page the way the header breadcrumb does ("Trading", "Orders - Trading"), page first so
  // browser tabs stay distinguishable; the product name already ends the document title.
  const pageParts = pathname === "/" ? ["Daily Control Tower"] : routePageParts(pathname, activeWorkspace);
  const workspaceTitle = pageParts.join(" - ");
  const announcedTitle = pageParts.join(", ");
  const targetElementId = normalizeHashTarget(hash);
  const targetLabel = targetElementId ? formatHashTargetLabel(targetElementId) : null;

  return {
    routeKey: `${pathname}${routeFocusSearch(pathname, search)}${hash}`,
    announcement: targetLabel
      ? `${announcedTitle} loaded. Jumping to ${targetLabel}.`
      : `${announcedTitle} loaded.`,
    documentTitle: `${workspaceTitle} - Meridian`,
    targetElementId,
    fallbackElementId: "workbench-content"
  };
}

function routePageParts(pathname: string, activeWorkspace: WorkspaceSummary): string[] {
  const pageLabel = resolveWorkstationRouteBreadcrumbLabel(pathname, activeWorkspace);
  return pageLabel === activeWorkspace.label ? [activeWorkspace.label] : [pageLabel, activeWorkspace.label];
}

function routeFocusSearch(pathname: string, search: string): string {
  const normalizedPath = pathname.replace(/\/$/, "").toLowerCase();
  const isReporting = normalizedPath === WORKSTATION_ROUTE_CATALOG.reporting;
  const isPortfolio = normalizedPath === WORKSTATION_ROUTE_CATALOG.portfolio;
  const isFamilyOffice = normalizedPath === WORKSTATION_ROUTE_CATALOG.portfolioFamilyOffice;
  const isMarketData = normalizedPath === WORKSTATION_ROUTE_CATALOG.dataQuotes;
  const isReconciliation = normalizedPath === WORKSTATION_ROUTE_CATALOG.accountingReconciliation
    || normalizedPath === WORKSTATION_ROUTE_CATALOG.accountingExceptions;
  const taskKeys = normalizedPath === WORKSTATION_ROUTE_CATALOG.strategy
    ? ["strategyRun", "strategyTask"]
    : normalizedPath === WORKSTATION_ROUTE_CATALOG.data
      ? ["dataJob", "dataTask"]
      : normalizedPath === WORKSTATION_ROUTE_CATALOG.settings
        ? ["settingsTask"] : [];
  if (!isReporting && !isPortfolio && !isFamilyOffice && !isMarketData && !isReconciliation && taskKeys.length === 0) return search;

  // Queue selection, search, and table controls stay in their current workbench.
  // Treating screen-local URL state as navigation would steal the control/row focus.
  const params = new URLSearchParams(search);
  if (taskKeys.length > 0) {
    for (const key of taskKeys) {
      params.delete(key);
      params.delete(`${key}Search`);
    }
  } else if (isReconciliation) {
    for (const key of ["reconciliationRun", "reconciliationBreak", "reconciliationSearch", "reconciliationStatus", "reconciliationSort"]) {
      params.delete(key);
    }
  } else if (isFamilyOffice) {
    params.delete("ownershipNode");
  } else {
    params.delete(isReporting ? REPORTING_WORK_SELECTION_PARAM : VIEW_STATE_QUERY_KEY);
  }
  const focusSearch = params.toString();
  return focusSearch ? `?${focusSearch}` : "";
}

function normalizeHashTarget(hash: string): string | null {
  if (!hash.startsWith("#") || hash.length <= 1) {
    return null;
  }

  try {
    return decodeURIComponent(hash.slice(1));
  } catch {
    return hash.slice(1);
  }
}

function formatHashTargetLabel(targetElementId: string): string {
  return targetElementId
    .split(/[-_\s]+/)
    .filter(Boolean)
    .join(" ")
    .toLowerCase();
}
