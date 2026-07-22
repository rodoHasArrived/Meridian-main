import { hasRetainedReportingAsOfDateValue } from "@/lib/reporting-periods";
import { redactReportingCredentialText, safeReportingHref } from "@/lib/reporting-link-safety";
import type {
  ReportPackDeliveryAccessLink,
  ReportWriterDatasetSource,
  ReportingScheduleDeliveryPlan,
  ReportingScheduleDeliveryTarget,
  ReportingScheduleRecord,
  ReportingScheduledReleaseHandoff
} from "@/types";
import type {
  ReportingDeliveryAccessLinkRow,
  ReportingScheduleDeliveryPlanRow,
  ReportingScheduleRow,
  ReportingScheduledReleaseHandoffRow
} from "@/screens/reporting-screen.view-model";

const reportingTimestampFormatter = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
  timeZone: "UTC",
  timeZoneName: "short"
});

const reportingDateFormatter = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  year: "numeric",
  timeZone: "UTC"
});

export function hasRetainedReportingAsOfDate(value: string | null | undefined): boolean {
  return hasRetainedReportingAsOfDateValue(value);
}

export function presentReportingAsOfDate(value: string | null | undefined): string {
  if (!hasRetainedReportingAsOfDate(value)) {
    return "No as-of date retained";
  }

  const retained = value!.trim();
  const isoDate = retained.match(/^(\d{4}-\d{2}-\d{2})/)?.[1];
  if (!isoDate) {
    return retained;
  }

  const parsed = new Date(`${isoDate}T00:00:00Z`);
  return Number.isNaN(parsed.getTime()) ? retained : reportingDateFormatter.format(parsed);
}

export function formatReportingTimestamp(value: string): string {
  const timestamp = new Date(value);
  if (Number.isNaN(timestamp.getTime())) {
    return value;
  }

  return reportingTimestampFormatter.format(timestamp);
}

export function buildScheduleRows(
  schedules: ReportingScheduleRecord[],
  datasetSources: ReportWriterDatasetSource[]
): ReportingScheduleRow[] {
  return schedules.map((schedule) => ({
    id: schedule.scheduleId,
    templateId: schedule.templateId,
    state: schedule.state,
    stateVariant: schedule.state === "Active" ? "success" : schedule.state === "Paused" ? "warning" : "outline",
    cronLabel: schedule.cronExpression,
    dueLabel: formatReportingTimestamp(schedule.dueAtUtc),
    nextAsOfLabel: presentReportingAsOfDate(schedule.nextAsOfDate),
    lastRunLabel: schedule.lastRunAtUtc ? formatReportingTimestamp(schedule.lastRunAtUtc) : "Not run",
    runCountLabel: `${schedule.runCount} run${schedule.runCount === 1 ? "" : "s"}`,
    description: schedule.description?.trim() || "No schedule note",
    deliveryTargetLabel: formatScheduleDeliveryTargets(schedule.deliveryTargets),
    datasetSourceLabel: formatScheduleDatasetSource(schedule.datasetSourceId, datasetSources),
    ...buildScheduleReleaseView(schedule),
    canPause: schedule.state === "Active",
    canResume: schedule.state === "Paused",
    ariaLabel: `${schedule.scheduleId} ${schedule.templateId} reporting schedule is ${schedule.state}`
  }));
}

function buildScheduleReleaseView(
  schedule: ReportingScheduleRecord
): Pick<ReportingScheduleRow, "accessPolicySnapshotLabel" | "releaseGateLabel" | "releaseGateVariant" | "releaseHandoffs"> {
  const accessPolicySnapshotHash = schedule.accessPolicySnapshotHash?.trim() || null;
  const releaseHandoffs = buildScheduledReleaseHandoffRows(schedule.releaseDeliveryHandoffs);
  const deliveryTargetCount = schedule.deliveryTargets?.length ?? 0;
  const pendingCount = releaseHandoffs.filter((handoff) => handoff.state === "PendingRelease").length;
  const enqueuedCount = releaseHandoffs.filter((handoff) => handoff.state === "Enqueued").length;

  if (deliveryTargetCount === 0) {
    return {
      accessPolicySnapshotLabel: accessPolicySnapshotHash ?? "No access policy snapshot retained",
      releaseGateLabel: "No release delivery handoff required",
      releaseGateVariant: "success",
      releaseHandoffs
    };
  }

  if (!accessPolicySnapshotHash) {
    return {
      accessPolicySnapshotLabel: "No access policy snapshot retained",
      releaseGateLabel: "Release delivery blocked: access policy snapshot hash unavailable",
      releaseGateVariant: "warning",
      releaseHandoffs
    };
  }

  if (releaseHandoffs.length === 0) {
    return {
      accessPolicySnapshotLabel: accessPolicySnapshotHash,
      releaseGateLabel: "Release delivery ready: post-generation handoff will be retained",
      releaseGateVariant: "success",
      releaseHandoffs
    };
  }

  if (pendingCount > 0) {
    return {
      accessPolicySnapshotLabel: accessPolicySnapshotHash,
      releaseGateLabel: `${pendingCount} handoff${pendingCount === 1 ? "" : "s"} awaiting governance release; ${enqueuedCount} enqueued`,
      releaseGateVariant: "warning",
      releaseHandoffs
    };
  }

  return {
    accessPolicySnapshotLabel: accessPolicySnapshotHash,
    releaseGateLabel: `${enqueuedCount} release delivery handoff${enqueuedCount === 1 ? "" : "s"} enqueued`,
    releaseGateVariant: "success",
    releaseHandoffs
  };
}

function buildScheduledReleaseHandoffRows(
  handoffs: ReportingScheduledReleaseHandoff[] | null | undefined
): ReportingScheduledReleaseHandoffRow[] {
  return (handoffs ?? []).map<ReportingScheduledReleaseHandoffRow>((handoff) => ({
    id: handoff.handoffId,
    runId: handoff.runId,
    distributionLabel: handoff.distributionId === handoff.targetDistributionId
      ? handoff.distributionId
      : `${handoff.distributionId} to ${handoff.targetDistributionId}`,
    transportId: handoff.transportId,
    recipientLabel: handoff.recipientPrincipalId
      ? `${handoff.recipientPrincipalKind ?? "Unknown"} · ${handoff.recipientPrincipalId}`
      : "No typed recipient retained",
    formatsLabel: handoff.requestedFormats && handoff.requestedFormats.length > 0
      ? handoff.requestedFormats.join(", ")
      : "Formats resolved after release",
    state: handoff.state,
    stateVariant: handoff.state === "Enqueued" ? "success" : "warning",
    createdLabel: formatReportingTimestamp(handoff.createdAtUtc),
    enqueuedLabel: buildScheduledReleaseEnqueuedLabel(handoff),
    ariaLabel: `${handoff.handoffId} release delivery handoff ${handoff.state} for ${handoff.recipientPrincipalKind ?? "unknown"} recipient`
  }));
}

function buildScheduledReleaseEnqueuedLabel(handoff: ReportingScheduledReleaseHandoff): string {
  if (handoff.state !== "Enqueued") {
    return "Awaiting governance release";
  }

  const jobId = handoff.enqueuedDeliveryJobId?.trim() || null;
  const enqueuedAt = handoff.enqueuedAtUtc ? formatReportingTimestamp(handoff.enqueuedAtUtc) : null;
  if (jobId && enqueuedAt) {
    return `${jobId} · ${enqueuedAt}`;
  }

  return jobId ?? enqueuedAt ?? "Enqueued; job reference unavailable";
}

export function buildScheduleSummary(schedules: ReportingScheduleRow[]): string {
  if (schedules.length === 0) {
    return "No reporting schedules configured.";
  }

  const activeCount = schedules.filter((schedule) => schedule.state === "Active").length;
  return `${schedules.length} schedule${schedules.length === 1 ? "" : "s"} configured; ${activeCount} active.`;
}

export function buildScheduleDeliveryPlanRows(
  plans: ReportingScheduleDeliveryPlan[]
): ReportingScheduleDeliveryPlanRow[] {
  return plans.map((plan) => ({
    id: plan.planId,
    scheduleId: plan.scheduleId,
    templateId: plan.templateId,
    distributionId: plan.distributionId,
    recipient: plan.recipient,
    recipientRole: plan.recipientRole,
    channel: plan.channel,
    deliveryMode: plan.deliveryMode,
    formatsLabel: plan.formats.length > 0 ? plan.formats.join(", ") : "Pdf, Xlsx, Csv",
    readinessSummary: plan.readinessSummary,
    readinessVariant: plan.isReady ? "success" : "warning",
    dueLabel: formatReportingTimestamp(plan.dueAtUtc),
    nextAsOfLabel: presentReportingAsOfDate(plan.nextAsOfDate),
    ownerLabel: plan.owner,
    route: plan.route,
    note: plan.note,
    lastDeliveryLabel: formatSchedulePlanLastDelivery(plan),
    lastDeliveryHref: safeReportingHref(plan.lastDeliveryPackageRoute),
    lastDeliveryLinks: buildDeliveryAccessLinkRows(plan.lastDeliveryAccessLinks, `${plan.planId}-delivery-link`),
    accessExpiryLabel: plan.lastDeliveryAccessExpiresAtUtc
      ? formatReportingTimestamp(plan.lastDeliveryAccessExpiresAtUtc)
      : "No retained access expiry",
    accessSummaryLabel: plan.lastDeliveryAccessSummary?.trim()
      ? redactReportingCredentialText(plan.lastDeliveryAccessSummary.trim())
      : "No retained access summary",
    channelSummaryLabel: plan.lastDeliveryChannelSummary?.trim() || "No retained channel summary",
    downloadSummaryLabel: plan.lastDeliveryDownloadSummary?.trim() || "No retained download summary",
    notificationSummaryLabel: plan.lastDeliveryNotificationSummary?.trim() || "No retained notification proof",
    reportWriterDatasetSummaryLabel: plan.lastDeliveryReportWriterDatasetSummary?.trim() || "No retained report-writer dataset",
    reportWriterGridSummaryLabel: plan.lastDeliveryReportWriterGridSummary?.trim() || "No retained report-writer grids",
    integrityLabel: formatSchedulePlanIntegrity(plan),
    integritySummary: plan.lastDeliveryIntegritySummary ?? null,
    entitlementLabel: plan.lastDeliveryEntitlementScope?.trim() || "No retained delivery entitlement",
    brandingLabel: formatSchedulePlanBranding(plan),
    versionStamp: plan.versionStamp,
    ariaLabel: `${plan.recipient} ${plan.deliveryMode} scheduled delivery plan for ${plan.scheduleId}`
  }));
}

export function buildScheduleDeliveryPlanSummary(plans: ReportingScheduleDeliveryPlanRow[]): string {
  if (plans.length === 0) {
    return "No schedule delivery plans configured.";
  }

  const readyCount = plans.filter((plan) => plan.readinessVariant === "success").length;
  return `${plans.length} delivery plan${plans.length === 1 ? "" : "s"} configured; ${readyCount} ready.`;
}

export function formatStarterDeliveryTargets(
  targets: ReportingScheduleDeliveryTarget[] | null | undefined
): string {
  if (!targets || targets.length === 0) {
    return "Delivery targets need review";
  }

  return targets.map(formatScheduleDeliveryTarget).join("; ");
}

export function formatStarterPeriod(period: string | null | undefined): string {
  if (!period) {
    return "Default period";
  }

  const normalized = period.trim();
  if (normalized === "CurrentMonth") {
    return "Current month";
  }

  if (normalized === "CurrentQuarter") {
    return "Current quarter";
  }

  if (normalized === "CurrentBusinessDay") {
    return "Current business day";
  }

  return normalized.replace(/([a-z])([A-Z])/g, "$1 $2");
}

export function formatStarterTemplateId(templateId: string): string {
  return templateId
    .split(/[-_]/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}

function formatSchedulePlanBranding(plan: ReportingScheduleDeliveryPlan): string {
  if (plan.brandingTheme) {
    return `${plan.brandingTheme.name} · ${plan.brandingTheme.firmName} · ${plan.brandingTheme.themeId}`;
  }

  return plan.brandingThemeId?.trim() || "Default theme";
}

function formatScheduleDatasetSource(
  datasetSourceId: string | null | undefined,
  datasetSources: ReportWriterDatasetSource[]
): string {
  const normalizedSourceId = datasetSourceId?.trim();
  if (!normalizedSourceId) {
    return "Default retained dataset";
  }

  const source = datasetSources.find((candidate) =>
    candidate.sourceId.localeCompare(normalizedSourceId, undefined, { sensitivity: "base" }) === 0);

  return source ? `${source.label} (${source.rowCount})` : normalizedSourceId;
}

function buildDeliveryAccessLinkRows(
  links: ReportPackDeliveryAccessLink[] | null | undefined,
  idPrefix: string
): ReportingDeliveryAccessLinkRow[] {
  return (links ?? [])
    .filter((link) => link.href?.trim())
    .flatMap((link, index) => {
      const label = redactReportingCredentialText(link.label?.trim() || link.kind || "Delivery link");
      const href = safeReportingHref(link.href.trim(), {
        requireOpaqueFragment: link.requiresToken
      });
      if (!href) {
        return [];
      }
      const expiresLabel = link.expiresAtUtc ? `Expires ${formatReportingTimestamp(link.expiresAtUtc)}` : null;
      return [{
        id: `${idPrefix}-${index + 1}`,
        kind: link.kind?.trim() || "delivery-link",
        label,
        href,
        requiresOpaqueFragment: link.requiresToken,
        tokenLabel: link.requiresToken ? "Fragment gated" : "Internal",
        expiresLabel,
        description: link.description?.trim() || null,
        ariaLabel: `${label} ${link.requiresToken ? "fragment-token gated" : "internal route"}`
      }];
    });
}

function formatSchedulePlanLastDelivery(plan: ReportingScheduleDeliveryPlan): string {
  if (!plan.lastDeliveryAtUtc || !plan.lastDeliveryState) {
    return "No retained delivery yet";
  }

  return `${plan.lastDeliveryState} ${formatReportingTimestamp(plan.lastDeliveryAtUtc)}`;
}

function formatSchedulePlanIntegrity(plan: ReportingScheduleDeliveryPlan): string {
  const artifactCount = plan.lastDeliveryArtifactCount ?? 0;
  if (artifactCount <= 0) {
    return "No retained artifact checksums";
  }

  return `${artifactCount} artifact${artifactCount === 1 ? "" : "s"} with SHA-256`;
}

function formatScheduleDeliveryTargets(
  targets: ReportingScheduleDeliveryTarget[] | null | undefined
): string {
  if (!targets || targets.length === 0) {
    return "No delivery targets";
  }

  return targets.map(formatScheduleDeliveryTarget).join("; ");
}

function formatScheduleDeliveryTarget(target: ReportingScheduleDeliveryTarget): string {
  const formats = target.formats && target.formats.length > 0
    ? target.formats.join("/")
    : "Pdf/Xlsx/Csv";
  const mode = target.deliveryMode ?? "Policy";
  return `${target.distributionId} via ${mode} (${formats})`;
}
