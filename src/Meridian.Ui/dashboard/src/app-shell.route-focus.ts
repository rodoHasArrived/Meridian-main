import type { WorkspaceSummary } from "@/types";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
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
  const workspaceTitle = pathname === "/" ? "Daily Control Tower" : `${activeWorkspace.label} Workstation`;
  const targetElementId = normalizeHashTarget(hash);
  const targetLabel = targetElementId ? formatHashTargetLabel(targetElementId) : null;

  return {
    routeKey: `${pathname}${routeFocusSearch(pathname, search)}${hash}`,
    announcement: targetLabel
      ? `${workspaceTitle} loaded. Jumping to ${targetLabel}.`
      : `${workspaceTitle} loaded.`,
    documentTitle: `${workspaceTitle} - Meridian`,
    targetElementId,
    fallbackElementId: "workbench-content"
  };
}

function routeFocusSearch(pathname: string, search: string): string {
  const normalizedPath = pathname.replace(/\/$/, "").toLowerCase();
  if (normalizedPath !== WORKSTATION_ROUTE_CATALOG.reporting) return search;

  // Selecting a queue item stays in the same workbench. Treating its URL state
  // as navigation would steal the row/detail focus managed by the shared table.
  const params = new URLSearchParams(search);
  params.delete(REPORTING_WORK_SELECTION_PARAM);
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
