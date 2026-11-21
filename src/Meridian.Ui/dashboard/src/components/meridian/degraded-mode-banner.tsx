import { StatusBanner } from "@/components/ui/status-banner";
import { buildDegradedModeNotices } from "@/lib/degraded-mode";
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
  const notices = buildDegradedModeNotices(degradedMode);

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
