import type { DegradedModeStatus } from "@/types";

export interface DegradedModeNotice {
  key: "simulated-market-data" | "persistence";
  title: string;
  detail: string;
}

/**
 * The degraded host postures an operator must be told about: simulated market data and
 * money-path stores without persistence. The red banner and the shell attention state both read
 * this list, so the banner can never be up while the masthead reports all clear.
 */
export function buildDegradedModeNotices(degradedMode: DegradedModeStatus | null | undefined): DegradedModeNotice[] {
  if (!degradedMode) {
    return [];
  }

  const notices: DegradedModeNotice[] = [];

  if (degradedMode.marketDataMode === "simulated") {
    notices.push({
      key: "simulated-market-data",
      title: "SIMULATED MARKET DATA",
      detail: degradedMode.marketDataDetail
        ?? "The configured streaming source generates synthetic prices. Do not treat quotes, fills, or P&L as real."
    });
  }

  if (degradedMode.persistenceMode !== "configured") {
    const missing = degradedMode.missingPersistenceDomains;
    notices.push({
      key: "persistence",
      title: `PERSISTENCE: ${degradedMode.persistenceMode.toUpperCase()}`,
      detail: missing.length > 0
        ? `Store domains without a database (data is lost on restart): ${missing.join(", ")}. `
          + "Set MERIDIAN_DATABASE_URL to enable persistence."
        : "No store domain has a database configured. Set MERIDIAN_DATABASE_URL to enable persistence."
    });
  }

  return notices;
}
