import { StatusBanner } from "@/components/ui/status-banner";
import {
  buildDataProvenanceBadgeViewModel,
  type DataProvenanceKind
} from "@/app-shell.data-provenance-badge";

export interface DataProvenanceBannerProps {
  provenance: DataProvenanceKind;
  detail?: string;
}

/**
 * Persistent, non-dismissable provenance badge. Rendered near the masthead so it rides above every
 * workspace. There is deliberately no close control: whenever the workstation is showing simulated,
 * seeded, or sample data the operator keeps seeing the label. Real data renders nothing.
 */
export function DataProvenanceBanner({ provenance, detail }: DataProvenanceBannerProps) {
  const badge = buildDataProvenanceBadgeViewModel({ provenance, detail });
  if (!badge.visible) {
    return null;
  }

  return (
    <section
      aria-label="Data provenance"
      className="border-b border-border bg-surface px-4 py-2"
    >
      <StatusBanner
        tone="danger"
        role={badge.role}
        aria-live={badge.ariaLive}
        title={`${badge.label} — ${badge.headline}`}
        detail={badge.detail}
        data-testid={`data-provenance-${badge.provenance}`}
      />
    </section>
  );
}
