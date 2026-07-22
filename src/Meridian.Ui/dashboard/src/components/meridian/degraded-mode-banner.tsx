import { StatusBanner } from "@/components/ui/status-banner";
import type { DegradedModeStatus } from "@/types";

export interface DegradedModeBannerProps {
  degradedMode: DegradedModeStatus | null | undefined;
}

/**
 * Persistent red banner for degraded host postures: simulated market data and/or money-path
 * stores running without persistence. Driven by the backend /api/status payload, never by
 * client-side fixture state, so it reflects what the host is actually doing.
 */
export function DegradedModeBanner({ degradedMode }: DegradedModeBannerProps) {
  if (!degradedMode) {
    return null;
  }

  const notices: { key: string; title: string; detail: string }[] = [];

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

  if (notices.length === 0) {
    return null;
  }

  return (
    <section aria-label="Degraded mode warnings" className="border-b border-border bg-surface px-4 py-2">
      <div className="flex flex-col gap-2">
        {notices.map(notice => (
          <StatusBanner
            key={notice.key}
            tone="danger"
            role="alert"
            title={notice.title}
            detail={notice.detail}
            data-testid={`degraded-mode-${notice.key}`}
          />
        ))}
      </div>
    </section>
  );
}
