import { useEffect, useState } from "react";
import { StatusBanner } from "@/components/ui/status-banner";
import {
  buildDataProvenanceBadgeViewModel,
  type DataProvenanceKind
} from "@/app-shell.data-provenance-badge";
import {
  provenanceBannerSeenKey,
  readSeenProvenanceBanner,
  writeSeenProvenanceBanner
} from "@/lib/provenance-banner-seen";
import type { SessionInfo } from "@/types";
import { getLocalStorage } from "@/lib/theme";

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
   * Shrink to a thin top-edge stripe once this browser has shown the full banner for the same
   * provenance and environment. Any change to either brings the full banner back.
   */
  compactAfterSeen?: boolean;
  /** Injected for tests; defaults to browser localStorage. */
  storage?: Storage | null;
}

/**
 * Persistent, non-dismissable provenance badge. Rendered near the masthead so it rides above every
 * workspace. There is deliberately no close control: whenever the workstation is showing simulated,
 * seeded, or sample data the operator keeps seeing the label. Real data renders nothing. The
 * `unknown` state — the probe never answered, so nothing was confirmed either way — renders as a
 * warning with a retry control instead of the simulated danger banner, so a transient probe failure
 * cannot brand a real install SIMULATED for the whole session.
 *
 * With `compactAfterSeen`, a pairing this browser has already shown in full renders as a thin
 * stripe that keeps the same status role and full text for assistive technology, while the
 * masthead trust pill keeps the provenance visibly named. `unknown` (it carries the retry) and any
 * LIVE or unresolved session keep the full banner.
 */
export function DataProvenanceBanner({
  provenance,
  detail,
  onRetryLiveData,
  retryBusy,
  environment = null,
  compactAfterSeen = false,
  storage = getLocalStorage()
}: DataProvenanceBannerProps) {
  const badge = buildDataProvenanceBadgeViewModel({ provenance, detail });
  // Read once per mount: within the session that first shows a pairing, the full banner stays up;
  // the next load of the same pairing gets the stripe.
  const [seenAtMount] = useState(() => (compactAfterSeen ? readSeenProvenanceBanner(storage) : null));
  const environmentResolved = environment !== null;
  const seenKey = environmentResolved ? provenanceBannerSeenKey(badge.provenance, environment) : null;
  const compact = compactAfterSeen
    && badge.visible
    && badge.provenance !== "unknown"
    && environmentResolved
    && environment !== "live"
    && seenAtMount === seenKey;

  useEffect(() => {
    if (compactAfterSeen && badge.visible && seenKey) {
      writeSeenProvenanceBanner(seenKey, storage);
    }
  }, [badge.visible, compactAfterSeen, seenKey, storage]);

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
          data-tone={badge.provenance === "unknown" ? "warning" : "danger"}
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
