/**
 * Dataset certification, and how it propagates to the reports that consume it.
 *
 * A certified dataset is an assertion by an accountable team that a body of data is
 * fit to report on for a given period. Reports consuming it inherit that assertion -
 * which is the useful part, and also the dangerous part, because an inherited
 * certification is invisible until it is wrong.
 *
 * So certification is bound to a fingerprint of the data it certified. When the
 * dataset changes underneath, the certification is **invalidated**, not quietly
 * carried forward, and every consuming report block inherits the invalidation. This
 * is the same version-binding rule as approval, applied one layer down.
 *
 * Inheritance is fail-closed throughout: a report consuming a dataset with no
 * certification, or one whose state cannot be determined, is not certified. Only an
 * explicit, current certification on every dataset a report consumes makes that
 * report certified.
 */
import { pluralizeCount } from "@/lib/format";
import type { DesignSystemSeverity } from "@/design-system/status";

export type CertificationState =
  /** Certified, and the data still matches what was certified. */
  | "Certified"
  /** Certified, but the dataset changed afterwards. */
  | "Invalidated"
  /** No certification has been recorded. */
  | "Uncertified"
  /** A certification exists but cannot be tied to the data, so it proves nothing. */
  | "Indeterminate";

const CERTIFICATION_SEVERITY: Record<CertificationState, DesignSystemSeverity> = {
  Certified: "ready",
  Invalidated: "action",
  Uncertified: "review",
  Indeterminate: "action"
};

const CERTIFICATION_LABEL: Record<CertificationState, string> = {
  Certified: "Certified",
  Invalidated: "Certification invalidated",
  Uncertified: "Not certified",
  Indeterminate: "Certification cannot be verified"
};

/** Worst-wins ordering: a single bad dataset decides the report. */
const CERTIFICATION_RANK: Record<CertificationState, number> = {
  Certified: 0,
  Uncertified: 1,
  Indeterminate: 2,
  Invalidated: 3
};

export interface DatasetCertificationInput {
  datasetId: string;
  datasetName: string;
  /** The accountable team, e.g. "Investment Analytics". */
  certifiedBy?: string | null;
  certifiedAtUtc?: string | null;
  /** Period the certification covers, e.g. "Sep 2026". */
  periodLabel?: string | null;
  /** Digest of the data as certified. */
  certifiedFingerprint?: string | null;
  /** Digest of the data as it stands now. */
  currentFingerprint?: string | null;
}

export interface DatasetCertification {
  datasetId: string;
  datasetName: string;
  state: CertificationState;
  severity: DesignSystemSeverity;
  label: string;
  certifiedBy: string | null;
  certifiedAtUtc: string | null;
  periodLabel: string | null;
  /** True when the dataset moved after it was certified. */
  hasMoved: boolean;
  reason: string;
}

/**
 * Evaluate one dataset's certification.
 *
 * A certification with no recorded certifier is not a certification: the whole point
 * is that a named team stands behind the data. A certification that carries no
 * fingerprint, when a current fingerprint is available to compare against, is
 * `Indeterminate` rather than `Certified` - nobody can show what it covered.
 */
export function evaluateDatasetCertification(
  input: DatasetCertificationInput
): DatasetCertification {
  const certifiedBy = nonEmpty(input.certifiedBy);
  const certifiedFingerprint = nonEmpty(input.certifiedFingerprint);
  const currentFingerprint = nonEmpty(input.currentFingerprint);

  let state: CertificationState;
  let hasMoved = false;

  if (certifiedBy === null) {
    state = "Uncertified";
  } else if (currentFingerprint !== null && certifiedFingerprint === null) {
    state = "Indeterminate";
  } else {
    hasMoved =
      certifiedFingerprint !== null &&
      currentFingerprint !== null &&
      certifiedFingerprint !== currentFingerprint;
    state = hasMoved ? "Invalidated" : "Certified";
  }

  return {
    datasetId: input.datasetId,
    datasetName: input.datasetName,
    state,
    severity: CERTIFICATION_SEVERITY[state],
    label: CERTIFICATION_LABEL[state],
    certifiedBy,
    certifiedAtUtc: nonEmpty(input.certifiedAtUtc),
    periodLabel: nonEmpty(input.periodLabel),
    hasMoved,
    reason: describeDataset(input.datasetName, state, certifiedBy, input.periodLabel)
  };
}

function describeDataset(
  datasetName: string,
  state: CertificationState,
  certifiedBy: string | null,
  periodLabel: string | null | undefined
): string {
  const period = nonEmpty(periodLabel);
  switch (state) {
    case "Certified":
      return `${datasetName} certified by ${certifiedBy}${period === null ? "" : ` for ${period}`}.`;
    case "Invalidated":
      return `${datasetName} changed after it was certified; the certification no longer covers the current data.`;
    case "Indeterminate":
      return `${datasetName} carries a certification that is not tied to a state of the data.`;
    default:
      return `${datasetName} has no recorded certification.`;
  }
}

export interface InheritedCertificationModel {
  /** Every dataset the report consumes, worst state first. */
  datasets: readonly DatasetCertification[];
  /** The state the report inherits: the worst across its datasets. */
  state: CertificationState;
  severity: DesignSystemSeverity;
  label: string;
  /** Datasets whose certification was invalidated. */
  invalidatedDatasets: readonly DatasetCertification[];
  /** True only when every dataset is currently certified. */
  isFullyCertified: boolean;
  summary: string;
}

/**
 * Derive the certification a report inherits from the datasets it consumes.
 *
 * The result is the worst state across the datasets, never an average or a majority.
 * One invalidated dataset is enough to make a report's numbers unsupported, and a
 * report that consumes nothing at all is `Uncertified` rather than trivially
 * certified - an empty set of assertions is not a clean bill of health.
 */
export function buildInheritedCertification(
  datasets: readonly DatasetCertification[]
): InheritedCertificationModel {
  const ordered = [...datasets].sort(
    (left, right) =>
      CERTIFICATION_RANK[right.state] - CERTIFICATION_RANK[left.state] ||
      left.datasetName.localeCompare(right.datasetName)
  );

  const state: CertificationState = ordered.length === 0
    ? "Uncertified"
    : (ordered[0] as DatasetCertification).state;

  const invalidatedDatasets = ordered.filter((dataset) => dataset.state === "Invalidated");
  const isFullyCertified = ordered.length > 0 && ordered.every((dataset) => dataset.state === "Certified");

  return {
    datasets: ordered,
    state,
    severity: CERTIFICATION_SEVERITY[state],
    label: CERTIFICATION_LABEL[state],
    invalidatedDatasets,
    isFullyCertified,
    summary: describeInherited(ordered, state, invalidatedDatasets)
  };
}

function describeInherited(
  datasets: readonly DatasetCertification[],
  state: CertificationState,
  invalidated: readonly DatasetCertification[]
): string {
  if (datasets.length === 0) {
    return "This report consumes no certified dataset.";
  }

  if (invalidated.length > 0) {
    const names = invalidated.map((dataset) => dataset.datasetName).join(", ");
    return `${pluralizeCount(invalidated.length, "dataset")} changed after certification (${names}); affected blocks are no longer certified.`;
  }

  if (state === "Certified") {
    return `All ${pluralizeCount(datasets.length, "dataset")} currently certified.`;
  }

  const offending = datasets.filter((dataset) => dataset.state === state).length;
  return `${pluralizeCount(offending, "dataset")} ${state === "Indeterminate" ? "cannot be verified" : "not certified"}.`;
}

export interface CertifiedBlockInput {
  blockId: string;
  blockLabel: string;
  /** Dataset ids this block's values come from. */
  datasetIds: readonly string[];
}

export interface CertifiedBlock {
  blockId: string;
  blockLabel: string;
  state: CertificationState;
  severity: DesignSystemSeverity;
  label: string;
  /** Datasets behind this block that are not currently certified. */
  offendingDatasetNames: readonly string[];
}

/**
 * Propagate dataset certification down to the report blocks that consume it.
 *
 * A block naming a dataset that is not in the supplied set is `Indeterminate`: the
 * block depends on something this evaluation could not see, and an unseen dependency
 * is not a certified one.
 */
export function propagateCertificationToBlocks(
  blocks: readonly CertifiedBlockInput[],
  datasets: readonly DatasetCertification[]
): readonly CertifiedBlock[] {
  const byId = new Map(datasets.map((dataset) => [dataset.datasetId, dataset]));

  return blocks.map((block) => {
    let worst: CertificationState = block.datasetIds.length === 0 ? "Uncertified" : "Certified";
    const offendingDatasetNames: string[] = [];

    for (const datasetId of block.datasetIds) {
      const dataset = byId.get(datasetId);
      if (dataset === undefined) {
        if (CERTIFICATION_RANK.Indeterminate > CERTIFICATION_RANK[worst]) {
          worst = "Indeterminate";
        }
        offendingDatasetNames.push(datasetId);
        continue;
      }

      if (CERTIFICATION_RANK[dataset.state] > CERTIFICATION_RANK[worst]) {
        worst = dataset.state;
      }
      if (dataset.state !== "Certified") {
        offendingDatasetNames.push(dataset.datasetName);
      }
    }

    return {
      blockId: block.blockId,
      blockLabel: block.blockLabel,
      state: worst,
      severity: CERTIFICATION_SEVERITY[worst],
      label: CERTIFICATION_LABEL[worst],
      offendingDatasetNames
    };
  });
}

function nonEmpty(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? "";
  return trimmed.length > 0 ? trimmed : null;
}
