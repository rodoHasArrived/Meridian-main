import type { DegradedModeStatus, SystemOverviewResponse } from "@/types";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function readString(value: unknown): string | null {
  return typeof value === "string" && value.length > 0 ? value : null;
}

/**
 * Narrows the backend /api/status `degradedMode` payload (simulated market data,
 * persistence posture) into the typed shape the workstation banner consumes.
 */
export function readDegradedMode(value: unknown): DegradedModeStatus | null {
  if (!isRecord(value)) {
    return null;
  }

  const marketDataMode = readString(value.marketDataMode);
  const persistenceMode = readString(value.persistenceMode);
  return {
    marketDataMode: marketDataMode === "live" || marketDataMode === "simulated" ? marketDataMode : "unknown",
    marketDataDetail: readString(value.marketDataDetail),
    persistenceMode: persistenceMode === "configured" || persistenceMode === "partial" ? persistenceMode : "none",
    missingPersistenceDomains: Array.isArray(value.missingPersistenceDomains)
      ? value.missingPersistenceDomains.filter((domain): domain is string => typeof domain === "string")
      : []
  };
}

export function fallbackSystemOverview(): SystemOverviewResponse {
  return {
    systemStatus: "Degraded",
    providersOnline: 0,
    providersTotal: 0,
    activeRuns: 0,
    openPositions: 0,
    activeBackfills: 0,
    symbolsMonitored: 0,
    storageHealth: "Warning",
    lastHeartbeatUtc: null,
    metrics: [],
    recentEvents: []
  };
}

export function deriveSystemStatus(
  isConnected: boolean,
  isStale: boolean,
  dropped: number,
  dropRate: number,
  queueUtilization: number
): SystemOverviewResponse["systemStatus"] {
  if (!isConnected) {
    return "Offline";
  }

  return isStale || dropped > 0 || dropRate > 0 || queueUtilization >= 0.8 ? "Degraded" : "Healthy";
}

export function deriveStorageHealth(
  systemStatus: SystemOverviewResponse["systemStatus"],
  dropped: number,
  queueUtilization: number
): SystemOverviewResponse["storageHealth"] {
  if (systemStatus === "Offline") {
    return "Critical";
  }

  return dropped > 0 || queueUtilization >= 0.8 ? "Warning" : "Healthy";
}
