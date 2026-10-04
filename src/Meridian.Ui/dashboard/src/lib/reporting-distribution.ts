/**
 * Distribution history and retention for published reports.
 *
 * Once a controlled report leaves the building, what matters is the record of where
 * it went and how long it must be kept. Both are evidence rather than convenience:
 * a restatement is only meaningful if you can show who received the superseded
 * version, and a disposal is only defensible if you can show the retention period
 * had actually elapsed.
 *
 * The safety property here is about disposal. A record with **no** retention policy
 * is not eligible for disposal - it is unclassified, which blocks disposal until
 * somebody classifies it. Treating "no policy" as "no obligation" would let the
 * least-governed records be destroyed first, which is precisely backwards.
 */
import { formatNumber, pluralizeCount } from "@/lib/format";
import type { DesignSystemSeverity } from "@/design-system/status";

/** Events in the life of a published report. */
export const DISTRIBUTION_EVENT_KINDS = [
  "Published",
  "Distributed",
  "Delivered",
  "Opened",
  "Archived",
  "Superseded",
  "Restated"
] as const;

export type DistributionEventKind = typeof DISTRIBUTION_EVENT_KINDS[number];

export const DISTRIBUTION_EVENT_LABELS: Record<DistributionEventKind, string> = {
  Published: "Published",
  Distributed: "Distributed",
  Delivered: "Delivered",
  Opened: "Opened",
  Archived: "Archived",
  Superseded: "Superseded",
  Restated: "Restated"
};

const EVENT_LOOKUP: Record<string, DistributionEventKind> = {
  published: "Published",
  publish: "Published",
  distributed: "Distributed",
  sent: "Distributed",
  distribute: "Distributed",
  delivered: "Delivered",
  delivery: "Delivered",
  opened: "Opened",
  read: "Opened",
  archived: "Archived",
  archive: "Archived",
  superseded: "Superseded",
  restated: "Restated",
  restatement: "Restated"
};

/** Resolve an event kind, or `null` when it is not one the model knows. */
export function normalizeDistributionEventKind(
  value: string | null | undefined
): DistributionEventKind | null {
  const key = value?.trim().toLowerCase().replace(/[\s_-]+/g, "") ?? "";
  return key ? EVENT_LOOKUP[key] ?? null : null;
}

export interface DistributionEventInput {
  kind: string;
  timestampUtc?: string | null;
  /** Version the event concerns, e.g. "09". */
  version?: string | null;
  /** Who or what received it. */
  recipient?: string | null;
  detail?: string | null;
}

export interface DistributionEvent {
  kind: DistributionEventKind | null;
  label: string;
  timestampUtc: string | null;
  /** Milliseconds since epoch, or `null` when the timestamp could not be read. */
  timestampMs: number | null;
  version: string | null;
  recipient: string | null;
  detail: string | null;
}

export interface DistributionHistoryModel {
  /** Events with a usable timestamp, oldest first. */
  events: readonly DistributionEvent[];
  /**
   * Events retained but not placed on the timeline, because their timestamp could
   * not be read or their kind is unrecognized. Never silently discarded.
   */
  unplacedEvents: readonly DistributionEvent[];
  /** Distinct recipients across every distribution event. */
  recipients: readonly string[];
  distributedCount: number;
  /** True when the report was published but never distributed. */
  isPublishedUndistributed: boolean;
  severity: DesignSystemSeverity;
  summary: string;
}

/**
 * Build the ordered distribution timeline.
 *
 * Events whose timestamp cannot be read are kept in `unplacedEvents` rather than
 * dropped or sorted to the front. A distribution that happened is a fact even when
 * its clock reading is unusable, and losing it from the record would understate who
 * holds a copy.
 */
export function buildDistributionHistory(
  inputs: readonly DistributionEventInput[]
): DistributionHistoryModel {
  const normalized = inputs.map(normalizeEvent);

  const placed = normalized
    .filter((event) => event.timestampMs !== null && event.kind !== null)
    .sort((left, right) => (left.timestampMs as number) - (right.timestampMs as number));
  const unplaced = normalized.filter((event) => event.timestampMs === null || event.kind === null);

  const distributionEvents = normalized.filter(
    (event) => event.kind === "Distributed" || event.kind === "Delivered"
  );
  const recipients = [
    ...new Set(
      distributionEvents
        .map((event) => event.recipient)
        .filter((recipient): recipient is string => recipient !== null)
    )
  ];

  const hasPublished = normalized.some((event) => event.kind === "Published");
  const isPublishedUndistributed = hasPublished && distributionEvents.length === 0;

  const severity: DesignSystemSeverity = unplaced.length > 0
    ? "review"
    : isPublishedUndistributed
      ? "review"
      : placed.length > 0
        ? "info"
        : "ready";

  return {
    events: placed,
    unplacedEvents: unplaced,
    recipients,
    distributedCount: distributionEvents.length,
    isPublishedUndistributed,
    severity,
    summary: describeHistory(placed.length, distributionEvents.length, recipients.length, unplaced.length, isPublishedUndistributed)
  };
}

function normalizeEvent(input: DistributionEventInput): DistributionEvent {
  const kind = normalizeDistributionEventKind(input.kind);
  const timestampUtc = nonEmpty(input.timestampUtc);
  const parsed = timestampUtc === null ? Number.NaN : Date.parse(timestampUtc);

  return {
    kind,
    label: kind === null ? input.kind.trim() : DISTRIBUTION_EVENT_LABELS[kind],
    timestampUtc,
    timestampMs: Number.isNaN(parsed) ? null : parsed,
    version: nonEmpty(input.version),
    recipient: nonEmpty(input.recipient),
    detail: nonEmpty(input.detail)
  };
}

function describeHistory(
  placedCount: number,
  distributedCount: number,
  recipientCount: number,
  unplacedCount: number,
  isPublishedUndistributed: boolean
): string {
  if (placedCount === 0 && unplacedCount === 0) {
    return "No distribution history recorded.";
  }

  const parts: string[] = [];
  if (distributedCount > 0) {
    parts.push(
      recipientCount > 0
        ? `${pluralizeCount(distributedCount, "distribution")} to ${pluralizeCount(recipientCount, "recipient")}.`
        : `${pluralizeCount(distributedCount, "distribution")} recorded.`
    );
  }
  if (isPublishedUndistributed) {
    parts.push("Published but no distribution recorded.");
  }
  if (unplacedCount > 0) {
    parts.push(`${pluralizeCount(unplacedCount, "event")} could not be placed on the timeline.`);
  }
  if (parts.length === 0) {
    parts.push(`${pluralizeCount(placedCount, "event")} recorded.`);
  }
  return parts.join(" ");
}

export interface RetentionPolicy {
  /** Whole years the record must be kept. `null` means unclassified. */
  years: number | null;
  /** Archive formats the policy requires, e.g. "PDF/A". */
  archiveFormats: readonly string[];
  /** True when the policy requires a full lineage snapshot alongside the output. */
  requiresLineageSnapshot: boolean;
}

export type RetentionStatus =
  /** Inside the retention period. */
  | "Retained"
  /** Retention has elapsed; the record may be dispositioned. */
  | "Eligible"
  /** No policy governs this record, so disposal is blocked. */
  | "Unclassified"
  /** A policy exists but the retention clock cannot be read. */
  | "Indeterminate";

const RETENTION_SEVERITY: Record<RetentionStatus, DesignSystemSeverity> = {
  Retained: "ready",
  Eligible: "info",
  Unclassified: "action",
  Indeterminate: "review"
};

const RETENTION_LABEL: Record<RetentionStatus, string> = {
  Retained: "Retained",
  Eligible: "Eligible for disposition",
  Unclassified: "No retention policy",
  Indeterminate: "Retention cannot be determined"
};

export interface RetentionModel {
  status: RetentionStatus;
  severity: DesignSystemSeverity;
  label: string;
  /** The instant retention elapses, when it can be computed. */
  retainUntilUtc: string | null;
  /** Whole years remaining; negative once elapsed. `null` when indeterminate. */
  yearsRemaining: number | null;
  policy: RetentionPolicy;
  /** True only when the record may actually be destroyed. */
  isDisposable: boolean;
  summary: string;
}

export interface RetentionEvaluationInput {
  policy?: Partial<RetentionPolicy> | null;
  /** When the retention clock started, usually the publication date. */
  publishedAtUtc?: string | null;
  /** Evaluation instant; defaults to now. */
  evaluatedAtUtc?: string | Date | null;
}

/**
 * Evaluate a record against its retention policy.
 *
 * `isDisposable` is true only for `Eligible`. An unclassified record and one whose
 * clock cannot be read are both undisposable: the first has no stated obligation to
 * satisfy, the second has one that cannot be shown to be satisfied, and destroying
 * either would be a decision made without evidence.
 */
export function evaluateRetention(input: RetentionEvaluationInput): RetentionModel {
  const policy: RetentionPolicy = {
    years:
      typeof input.policy?.years === "number" && Number.isFinite(input.policy.years) && input.policy.years >= 0
        ? input.policy.years
        : null,
    archiveFormats: (input.policy?.archiveFormats ?? [])
      .map((format) => nonEmpty(format))
      .filter((format): format is string => format !== null),
    requiresLineageSnapshot: input.policy?.requiresLineageSnapshot === true
  };

  if (policy.years === null) {
    return {
      status: "Unclassified",
      severity: RETENTION_SEVERITY.Unclassified,
      label: RETENTION_LABEL.Unclassified,
      retainUntilUtc: null,
      yearsRemaining: null,
      policy,
      isDisposable: false,
      summary: "No retention period is set for this record, so it cannot be dispositioned."
    };
  }

  const publishedAt = parseInstant(input.publishedAtUtc);
  if (publishedAt === null) {
    return {
      status: "Indeterminate",
      severity: RETENTION_SEVERITY.Indeterminate,
      label: RETENTION_LABEL.Indeterminate,
      retainUntilUtc: null,
      yearsRemaining: null,
      policy,
      isDisposable: false,
      summary: `Retention is ${pluralizeCount(policy.years, "year")} but the publication date is unknown, so the period cannot be measured.`
    };
  }

  const retainUntil = new Date(publishedAt);
  retainUntil.setUTCFullYear(retainUntil.getUTCFullYear() + policy.years);
  const evaluatedAt = parseInstant(input.evaluatedAtUtc) ?? Date.now();

  const elapsed = evaluatedAt >= retainUntil.getTime();
  const status: RetentionStatus = elapsed ? "Eligible" : "Retained";
  const yearsRemaining = (retainUntil.getTime() - evaluatedAt) / (365.25 * 24 * 3_600_000);

  return {
    status,
    severity: RETENTION_SEVERITY[status],
    label: RETENTION_LABEL[status],
    retainUntilUtc: retainUntil.toISOString(),
    yearsRemaining: Math.round(yearsRemaining * 10) / 10,
    policy,
    isDisposable: status === "Eligible",
    summary: elapsed
      ? `Retention of ${pluralizeCount(policy.years, "year")} elapsed; the record may be dispositioned.`
      : `Retained for ${pluralizeCount(policy.years, "year")}; ${formatNumber(Math.max(yearsRemaining, 0), { maximumFractionDigits: 1 })} remaining.`
  };
}

function nonEmpty(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? "";
  return trimmed.length > 0 ? trimmed : null;
}

function parseInstant(value: string | Date | null | undefined): number | null {
  if (value instanceof Date) {
    const time = value.getTime();
    return Number.isNaN(time) ? null : time;
  }
  const trimmed = nonEmpty(value);
  if (trimmed === null) {
    return null;
  }
  const parsed = Date.parse(trimmed);
  return Number.isNaN(parsed) ? null : parsed;
}
