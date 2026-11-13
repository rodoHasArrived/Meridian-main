const SECURITY_MASTER_DRILL_IN_TAB_IDS = ["identity", "reference", "schedules", "lots", "passport", "evidence"] as const;

type SecurityMasterDrillInTabId = (typeof SECURITY_MASTER_DRILL_IN_TAB_IDS)[number];

export function isSecurityMasterDrillInTabId(value: string | null): value is SecurityMasterDrillInTabId {
  return SECURITY_MASTER_DRILL_IN_TAB_IDS.includes(value as SecurityMasterDrillInTabId);
}

export function resolveSecurityMasterDrillInTab(search: string): SecurityMasterDrillInTabId {
  const requested = new URLSearchParams(search).get("tab");
  return isSecurityMasterDrillInTabId(requested) ? requested : "identity";
}

export function buildSecurityMasterDrillInSearch(search: string, tabId: SecurityMasterDrillInTabId): string {
  const params = new URLSearchParams(search);
  params.set("tab", tabId);
  const serialized = params.toString();
  return serialized ? `?${serialized}` : "";
}

export function resolveExplorerSecurityId(recordId: string): string | null {
  const [recordType, securityId, ...extraSegments] = recordId.split(":");
  return (recordType === "security" || recordType === "security-instrument") && securityId && extraSegments.length === 0
    ? securityId
    : null;
}
