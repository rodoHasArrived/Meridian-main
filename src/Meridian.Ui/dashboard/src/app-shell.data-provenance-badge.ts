/**
 * Persistent, non-dismissable data-provenance badge shared by the operator shell. It mirrors the
 * `app-shell.development-fixture-notice` status-region pattern, but unlike a dismissable notice it
 * can never be closed: whenever the workstation is showing simulated, seeded, or sample data the
 * operator must see the label. The C# seam is `Meridian.Contracts.Operations.DataProvenance`.
 */

export type DataProvenanceKind = "real" | "simulated" | "seeded" | "sample" | "unknown";

export interface DataProvenanceBadgeViewModel {
  role: "status";
  ariaLive: "polite";
  /** Real data renders no badge; every other provenance renders one. */
  visible: boolean;
  /** Provenance badges are always persistent — this is a literal `false`, never a toggle. */
  dismissable: false;
  provenance: DataProvenanceKind;
  /** Short uppercase label (e.g. `SIMULATED`); empty string for real data. */
  label: string;
  headline: string;
  detail: string;
  ariaLabel: string;
}

const NON_REAL_COPY: Record<
  Exclude<DataProvenanceKind, "real">,
  { label: string; headline: string; detail: string }
> = {
  simulated: {
    label: "SIMULATED",
    headline: "Simulated data",
    detail:
      "This data is simulated. Do not treat quotes, fills, valuations, or P&L as real."
  },
  seeded: {
    label: "SEEDED",
    headline: "Seeded demo data",
    detail:
      "This is seeded demo data shipped without live credentials. It is not real market or account data."
  },
  sample: {
    label: "SAMPLE",
    headline: "Sample data",
    detail: "This is sample placeholder data with no operational meaning."
  },
  unknown: {
    label: "UNKNOWN",
    headline: "Data source unverified",
    detail:
      "The workstation could not confirm whether this session runs on real or simulated data. Retry the live-data check before trusting quotes, fills, valuations, or P&L."
  }
};

/**
 * Provenance kinds the server can assert over the wire. `unknown` is deliberately absent: it is a
 * client-side transport state (the probe never answered), never a server claim, so a wire token
 * "unknown" still fails closed to `simulated` below.
 */
const KNOWN_PROVENANCE: readonly DataProvenanceKind[] = ["real", "simulated", "seeded", "sample"];

/**
 * Normalizes a loose provenance token from the wire. Unknown tokens resolve to `simulated` — never
 * `real` — so an unrecognized value can never be silently upgraded into "real".
 */
export function normalizeDataProvenance(token: unknown): DataProvenanceKind {
  if (typeof token !== "string") {
    return "simulated";
  }

  const normalized = token.trim().toLowerCase();
  if (normalized === "real" || normalized === "live") {
    return "real";
  }
  if ((KNOWN_PROVENANCE as readonly string[]).includes(normalized)) {
    return normalized as DataProvenanceKind;
  }
  if (normalized === "demo" || normalized === "fixture" || normalized === "seed") {
    return "seeded";
  }
  if (normalized === "placeholder") {
    return "sample";
  }
  return "simulated";
}

export interface WorkstationDataProvenanceInput {
  usingDevelopmentFixtures: boolean;
  demoMode: {
    enabled?: boolean;
    provenance?: unknown;
  } | null;
}

/**
 * Resolves the shell-wide trust label. A positive demo-mode response and any development-fixture
 * usage take precedence over a real-data claim. Until the server has explicitly reported that demo
 * mode is disabled, the shell reports `unknown` — a distinct persistent badge with a retry
 * affordance — rather than branding a real install SIMULATED because one probe failed, and never
 * silently claiming real data.
 */
export function resolveWorkstationDataProvenance({
  usingDevelopmentFixtures,
  demoMode
}: WorkstationDataProvenanceInput): DataProvenanceKind {
  if (demoMode?.enabled) {
    const reported = normalizeDataProvenance(demoMode.provenance);
    return reported === "real" ? "simulated" : reported;
  }

  if (usingDevelopmentFixtures) {
    return "seeded";
  }

  return demoMode ? "real" : "unknown";
}

export function buildDataProvenanceBadgeViewModel({
  provenance,
  detail
}: {
  provenance: DataProvenanceKind;
  detail?: string;
}): DataProvenanceBadgeViewModel {
  if (provenance === "real") {
    return {
      role: "status",
      ariaLive: "polite",
      visible: false,
      dismissable: false,
      provenance,
      label: "",
      headline: "",
      detail: "",
      ariaLabel: ""
    };
  }

  const copy = NON_REAL_COPY[provenance];
  const resolvedDetail = detail?.trim() ? detail.trim() : copy.detail;

  return {
    role: "status",
    ariaLive: "polite",
    visible: true,
    dismissable: false,
    provenance,
    label: copy.label,
    headline: copy.headline,
    detail: resolvedDetail,
    ariaLabel: `${copy.label}: ${copy.headline}. ${resolvedDetail}`
  };
}
