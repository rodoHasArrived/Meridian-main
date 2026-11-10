import { StatusBanner } from "@/components/ui/status-banner";
import {
  buildDataProvenanceBadgeViewModel,
  type DataProvenanceKind
} from "@/app-shell.data-provenance-badge";
import type { SessionInfo } from "@/types";

export interface DataProvenanceBannerProps {
  provenance: DataProvenanceKind;
  detail?: string;
  /** Re-runs the demo-mode probe. Rendered as a retry control only for `unknown` provenance. */
  onRetryLiveData?: () => void;
  retryBusy?: boolean;
  /**
   * Session environment, or null while the session is still resolving. Compaction is decided
   * against it: an unresolved environment and a LIVE session always get the full banner.
   */
  environment?: SessionInfo["environment"] | null;
  /**
   * The masthead environment badge already names this provenance beside PAPER / DEMO, so the
   * banner shrinks to a thin top-edge stripe instead of repeating the label at full width.
   */
  labeledInMasthead?: boolean;
}

/**
 * Persistent, non-dismissable provenance label. Rendered near the masthead so it rides above every
 * workspace. There is deliberately no close control: whenever the workstation is showing simulated,
 * seeded, or sample data the operator keeps seeing the label. Real data renders nothing.
 *
 * With `labeledInMasthead`, the masthead environment badge carries the visible label
 * ("PAPER · SEEDED"), so this renders as a thin stripe that keeps the same status role and full
 * text for assistive technology. The full-width banner is kept for the cases where it says
 * something the badge cannot: `unknown` provenance -- the probe never answered, so it carries the
 * retry control and a warning rather than a false SIMULATED claim -- and any LIVE or unresolved
 * session, where non-real data must not be easy to miss.
 */
export function DataProvenanceBanner({
  provenance,
  detail,
  onRetryLiveData,
  retryBusy,
  environment = null,
  labeledInMasthead = false
}: DataProvenanceBannerProps) {
  const badge = buildDataProvenanceBadgeViewModel({ provenance, detail });
  const compact = labeledInMasthead
    && badge.visible
    && badge.provenance !== "unknown"
    && environment !== null
    && environment !== "live";

  if (!badge.visible) {
    return null;
  }

  if (compact) {
    const fullText = `${badge.label} — ${badge.headline}. ${badge.detail}`;
    return (
      <section
        aria-label="Data provenance"
        className="workstation-provenance workstation-provenance--stripe"
        data-compact="true"
      >
        <div
          className="workstation-provenance-stripe"
          data-tone="danger"
          data-testid={`data-provenance-${badge.provenance}`}
          role={badge.role}
          aria-live={badge.ariaLive}
          title={fullText}
        >
          <span className="sr-only">{fullText}</span>
        </div>
      </section>
    );
  }

  const showRetry = badge.provenance === "unknown" && typeof onRetryLiveData === "function";

  return (
    <section
      aria-label="Data provenance"
      className="workstation-provenance border-b border-border bg-surface px-4 py-2"
    >
      <StatusBanner
        className="workstation-provenance-banner"
        tone={badge.provenance === "unknown" ? "warning" : "danger"}
        role={badge.role}
        aria-live={badge.ariaLive}
        title={`${badge.label} — ${badge.headline}`}
        detail={badge.detail}
        data-testid={`data-provenance-${badge.provenance}`}
      />
      {showRetry ? (
        <button
          type="button"
          className="mt-2 rounded-[2px] border border-border bg-surface px-3 py-1 text-xs font-semibold text-foreground hover:bg-muted disabled:opacity-60"
          aria-label={
            retryBusy
              ? "Retrying live Meridian workspace data"
              : "Retry live Meridian workspace data"
          }
          disabled={retryBusy}
          onClick={onRetryLiveData}
        >
          {retryBusy ? "Retrying live data" : "Retry live data"}
        </button>
      ) : null}
    </section>
  );
}
