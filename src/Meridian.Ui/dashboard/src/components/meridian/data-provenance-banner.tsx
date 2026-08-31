import { StatusBanner } from "@/components/ui/status-banner";
import {
  buildDataProvenanceBadgeViewModel,
  type DataProvenanceKind
} from "@/app-shell.data-provenance-badge";

export interface DataProvenanceBannerProps {
  provenance: DataProvenanceKind;
  detail?: string;
  /** Re-runs the demo-mode probe. Rendered as a retry control only for `unknown` provenance. */
  onRetryLiveData?: () => void;
  retryBusy?: boolean;
}

/**
 * Persistent, non-dismissable provenance badge. Rendered near the masthead so it rides above every
 * workspace. There is deliberately no close control: whenever the workstation is showing simulated,
 * seeded, or sample data the operator keeps seeing the label. Real data renders nothing. The
 * `unknown` state — the probe never answered, so nothing was confirmed either way — renders as a
 * warning with a retry control instead of the simulated danger banner, so a transient probe failure
 * cannot brand a real install SIMULATED for the whole session.
 */
export function DataProvenanceBanner({
  provenance,
  detail,
  onRetryLiveData,
  retryBusy
}: DataProvenanceBannerProps) {
  const badge = buildDataProvenanceBadgeViewModel({ provenance, detail });
  if (!badge.visible) {
    return null;
  }

  const showRetry = badge.provenance === "unknown" && typeof onRetryLiveData === "function";

  return (
    <section
      aria-label="Data provenance"
      className="border-b border-border bg-surface px-4 py-2"
    >
      <StatusBanner
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
