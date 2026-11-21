export const CLOSE_HUB_TABS = ["board", "blockers", "checklist", "approvals", "evidence"] as const;
export type CloseHubTab = typeof CLOSE_HUB_TABS[number];

export function closeHubTabFromSearch(search: string): CloseHubTab {
  const params = new URLSearchParams(search);
  const tab = params.get("closeTab");
  return CLOSE_HUB_TABS.find((candidate) => candidate === tab) ?? (params.has("reconciliationBreak") ? "blockers" : "board");
}

/** Change a screen control while retaining the complete operating context and anchor. */
export function updateWorkstationSearch(search: string, key: string, value: string | null): string {
  const params = new URLSearchParams(search);
  if (value === null) params.delete(key);
  else params.set(key, value);
  const result = params.toString();
  return result ? `?${result}` : "";
}

export function closeWorkspaceRedirect(pathname: string, search = "", hash = ""): string | null {
  const target = pathname === "/accounting/operations-continuity"
    ? "/accounting/close"
    : pathname === "/trading/readiness" ? "/settings/diagnostics" : null;
  return target ? `${target}${search}${hash}` : null;
}
