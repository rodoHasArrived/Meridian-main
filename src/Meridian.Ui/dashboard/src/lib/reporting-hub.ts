/**
 * Builds the "Report Hub" overview model for the reporting workspace.
 *
 * The reporting screen is organized around the production queue (runs awaiting
 * approval / delivery), which suits the ops lead but not the analyst or PM who
 * just wants to answer "which report do I need, is it current, and where do I
 * open it?". This module derives a report-first model grouped by template family
 * — the navigation unit users actually think in — with a readiness signal and a
 * direct link to the latest output.
 *
 * It is pure: callers pass the already-shaped view-model rows (or any structural
 * subset), and it returns a deterministic model that is trivial to unit-test.
 */
import { compareIsoDate, formatReportingPeriodLabel } from "@/lib/reporting-periods";

export type ReportingHubReadiness =
  | "Released"
  | "Approved"
  | "InReview"
  | "Draft"
  | "Failed"
  | "NoRuns";

export type ReportingHubTone = "success" | "warning" | "danger" | "muted";
export type ReportingHubBadgeVariant = "success" | "warning" | "danger" | "outline";

export type ReportingHubProofPassportItemId =
  | "source"
  | "freshness"
  | "reconciliation"
  | "approvals"
  | "report-usage"
  | "blockers"
  | "evidence-packet"
  | "audit-trail";

export interface ReportingHubProofPassportItem {
  id: ReportingHubProofPassportItemId;
  label: string;
  value: string;
  detail: string;
  href: string | null;
  ariaLabel: string | null;
  badgeVariant: ReportingHubBadgeVariant;
}

export interface ReportingHubRunInput {
  templateId: string;
  family: string;
  status: string;
  asOfDateLabel: string;
  runIdLabel: string;
  runAttemptOrdinal?: number | null;
  isLatestGenerated?: boolean | null;
  isLatestApproved?: boolean | null;
  drilldownLinks: ReadonlyArray<{
    href: string;
    label: string;
    isBrowserNavigable: boolean;
  }>;
}

export interface ReportingHubTemplateInput {
  templateName: string;
  name: string;
  family: string;
}

export interface ReportingHubDailyWorkInput {
  workItemId: string;
  kind: string;
  title: string;
  statusLabel: string;
  detail: string;
  tone: string;
  owner: string;
  dueAtUtc: string | null;
  primaryActionLabel: string;
  primaryActionHref: string | null;
  evidenceGaps?: readonly string[] | null;
  context?: readonly string[] | null;
  secondaryActionLabel?: string | null;
  secondaryActionHref?: string | null;
}

export interface ReportingHubDailyWorkItem {
  workItemId: string;
  kind: string;
  kindLabel: string;
  title: string;
  statusLabel: string;
  detail: string;
  tone: ReportingHubTone;
  badgeVariant: ReportingHubBadgeVariant;
  owner: string;
  blockedLabel: string;
  affectedOutputLabel: string;
  dueLabel: string | null;
  dueAtUtc: string | null;
  nextActionLabel: string;
  primaryActionLabel: string;
  primaryActionHref: string | null;
  proofLabel: string;
  proofItems: string[];
  proofPassportStatusLabel: string;
  proofPassportSummary: string;
  proofPassportItems: ReportingHubProofPassportItem[];
  evidenceGaps: string[];
  context: string[];
  secondaryActionLabel: string | null;
  secondaryActionHref: string | null;
  controlQuestionSummary: string;
  ariaLabel: string;
}

export interface ReportingHubDecisionQueue {
  id: string;
  title: string;
  statusLabel: string;
  countLabel: string;
  detail: string;
  tone: ReportingHubTone;
  badgeVariant: ReportingHubBadgeVariant;
  isBlocked: boolean;
  ownerLabel: string;
  affectedOutputLabel: string;
  nextActionLabel: string;
  primaryActionLabel: string;
  primaryActionHref: string | null;
  secondaryActionLabel: string | null;
  secondaryActionHref: string | null;
  proofLabel: string;
  proofItems: string[];
  proofPassportStatusLabel: string;
  proofPassportSummary: string;
  proofPassportItems: ReportingHubProofPassportItem[];
  controlQuestionSummary: string;
  ariaLabel: string;
}

export interface ReportingHubCard {
  family: string;
  templateCount: number;
  runCount: number;
  readiness: ReportingHubReadiness;
  statusLabel: string;
  statusTone: ReportingHubTone;
  badgeVariant: ReportingHubBadgeVariant;
  latestRunId: string | null;
  latestAsOfLabel: string;
  latestStatusLabel: string | null;
  approvedAsOfLabel: string;
  isCurrent: boolean;
  needsAttention: boolean;
  openHref: string | null;
  openLabel: string | null;
  detail: string;
  ariaLabel: string;
}

export interface ReportingHubModel {
  dailyWork: ReportingHubDailyWorkItem[];
  dailyWorkSummaryLabel: string;
  decisionQueues: ReportingHubDecisionQueue[];
  decisionQueueSummaryLabel: string;
  cards: ReportingHubCard[];
  totalFamilies: number;
  currentCount: number;
  attentionCount: number;
  summaryLabel: string;
  isEmpty: boolean;
}

interface ReadinessPresentation {
  readiness: ReportingHubReadiness;
  statusLabel: string;
  statusTone: ReportingHubTone;
  badgeVariant: ReportingHubBadgeVariant;
}

function presentTone(tone: string | null | undefined): ReportingHubTone {
  switch ((tone ?? "").trim().toLowerCase()) {
    case "success":
      return "success";
    case "danger":
      return "danger";
    case "warning":
      return "warning";
    default:
      return "muted";
  }
}

function badgeVariantForTone(tone: ReportingHubTone): ReportingHubBadgeVariant {
  switch (tone) {
    case "success":
      return "success";
    case "danger":
      return "danger";
    case "warning":
      return "warning";
    default:
      return "outline";
  }
}

function presentReadiness(status: string | null): ReadinessPresentation {
  switch ((status ?? "").trim().toLowerCase()) {
    case "released":
      return { readiness: "Released", statusLabel: "Released", statusTone: "success", badgeVariant: "success" };
    case "approved":
      return { readiness: "Approved", statusLabel: "Approved", statusTone: "success", badgeVariant: "success" };
    case "inreview":
    case "in review":
      return { readiness: "InReview", statusLabel: "In review", statusTone: "warning", badgeVariant: "warning" };
    case "draft":
      return { readiness: "Draft", statusLabel: "Draft", statusTone: "muted", badgeVariant: "outline" };
    case "failed":
      return { readiness: "Failed", statusLabel: "Failed", statusTone: "danger", badgeVariant: "danger" };
    default:
      return { readiness: "NoRuns", statusLabel: "No runs yet", statusTone: "muted", badgeVariant: "outline" };
  }
}

function isApprovedStatus(status: string): boolean {
  const normalized = status.trim().toLowerCase();
  return normalized === "approved" || normalized === "released";
}

// Most recent first: ISO as-of dates compare correctly; non-ISO fallbacks sort stably after.
function byMostRecent(left: ReportingHubRunInput, right: ReportingHubRunInput): number {
  if (Boolean(left.isLatestGenerated) !== Boolean(right.isLatestGenerated)) {
    return left.isLatestGenerated ? -1 : 1;
  }

  const dateComparison = compareIsoDate(right.asOfDateLabel, left.asOfDateLabel);
  if (dateComparison !== 0) {
    return dateComparison;
  }

  return (right.runAttemptOrdinal ?? 1) - (left.runAttemptOrdinal ?? 1);
}

function resolveOpenLink(run: ReportingHubRunInput): { href: string; label: string } | null {
  const navigable = run.drilldownLinks.find((link) => link.isBrowserNavigable && link.href.trim().length > 0);
  if (!navigable) {
    return null;
  }

  return { href: navigable.href, label: "Open latest output" };
}

function countUnit(count: number, singular: string): string {
  return `${count} ${singular}${count === 1 ? "" : "s"}`;
}

function presentWorkKind(kind: string): string {
  switch (normalizeDailyWorkKind(kind)) {
    case "due-package":
      return "Due package";
    case "blocked-package":
      return "Blocked package";
    case "approval-needed":
      return "Approval needed";
    case "delivery-failure":
      return "Delivery failure";
    case "restatement":
      return "Restatement";
    case "evidence-gap":
      return "Evidence gap";
    default:
      return "Reporting work";
  }
}

function normalizeDailyWorkKind(kind: string): string {
  const normalized = kind.trim().toLowerCase();
  return normalized === "readiness-warning" ? "evidence-gap" : normalized;
}

function formatDueLabel(value: string | null): string | null {
  if (!value) {
    return null;
  }

  const datePart = value.slice(0, 10);
  return datePart ? `Due ${formatReportingPeriodLabel(datePart)}` : "Due date retained";
}

function compactStrings(values: readonly string[] | null | undefined): string[] {
  return [...new Set((values ?? []).map((value) => value.trim()).filter(Boolean))];
}

function blockedLabelForTone(tone: ReportingHubTone): string {
  switch (tone) {
    case "danger":
      return "Blocked";
    case "warning":
      return "Needs review";
    case "success":
      return "Ready";
    default:
      return "Pending";
  }
}

function titleSubject(title: string): string | null {
  const [, ...rest] = title.split(":");
  const subject = rest.join(":").trim();
  return subject.length > 0 ? subject : null;
}

function affectedOutputForItem(item: ReportingHubDailyWorkInput, context: string[]): string {
  return titleSubject(item.title) ?? context[0] ?? item.title;
}

function buildProofItems(item: ReportingHubDailyWorkInput, evidenceGaps: string[]): string[] {
  if (evidenceGaps.length > 0) {
    return evidenceGaps;
  }

  if (item.secondaryActionLabel && item.secondaryActionHref) {
    return [`${item.secondaryActionLabel}: ${item.secondaryActionHref}`];
  }

  if (item.primaryActionHref) {
    return [`${item.primaryActionLabel}: ${item.primaryActionHref}`];
  }

  return ["No retained proof route is linked to this work item."];
}

function buildProofLabel(evidenceGaps: string[], proofItems: string[]): string {
  if (evidenceGaps.length > 0) {
    return `${countUnit(evidenceGaps.length, "proof gap")}`;
  }

  return proofItems.length > 0 ? "Proof route retained" : "Proof pending";
}

function buildReportingProofPassportSummary(
  outputLabel: string,
  items: ReportingHubProofPassportItem[]
): string {
  return `Number Passport for ${outputLabel}: ${items.map((item) => `${item.label} ${item.value}`).join("; ")}.`;
}

function routeOwnedReportingDetail(facet: string): string {
  return `${facet} is owned by the Reporting route and shared reporting read model.`;
}

function reportingProofPassportVariant(tone: ReportingHubTone): ReportingHubBadgeVariant {
  return badgeVariantForTone(tone);
}

function firstProofRoute(primaryActionHref: string | null, secondaryActionHref: string | null): string | null {
  return primaryActionHref ?? secondaryActionHref ?? null;
}

function buildReportingProofPassportItems({
  sourceLabel,
  sourceDetail,
  sourceHref,
  sourceAriaLabel,
  freshnessLabel,
  freshnessDetail,
  ownerLabel,
  affectedOutputLabel,
  nextActionLabel,
  statusLabel,
  blockedLabel,
  proofLabel,
  proofItems,
  evidenceGaps,
  context,
  primaryActionHref,
  primaryActionLabel,
  secondaryActionHref,
  secondaryActionLabel,
  tone
}: {
  sourceLabel: string;
  sourceDetail: string;
  sourceHref: string | null;
  sourceAriaLabel: string | null;
  freshnessLabel: string;
  freshnessDetail: string;
  ownerLabel: string;
  affectedOutputLabel: string;
  nextActionLabel: string;
  statusLabel: string;
  blockedLabel: string;
  proofLabel: string;
  proofItems: string[];
  evidenceGaps: string[];
  context: string[];
  primaryActionHref: string | null;
  primaryActionLabel: string;
  secondaryActionHref: string | null;
  secondaryActionLabel: string | null;
  tone: ReportingHubTone;
}): ReportingHubProofPassportItem[] {
  const statusVariant = reportingProofPassportVariant(tone);
  const proofHref = firstProofRoute(primaryActionHref, secondaryActionHref);
  const proofDetail = evidenceGaps.length > 0
    ? evidenceGaps.slice(0, 2).join(" ")
    : proofItems[0] ?? routeOwnedReportingDetail("Evidence packet");
  const approvalMatches = [sourceLabel, statusLabel, nextActionLabel, proofLabel, ...proofItems, ...context]
    .join(" ")
    .toLowerCase()
    .includes("approval");
  const deliveryMatches = [sourceLabel, affectedOutputLabel, statusLabel, nextActionLabel, proofLabel, ...proofItems, ...context]
    .join(" ")
    .toLowerCase()
    .match(/report|delivery|statement|pack/);
  const evidenceMatches = evidenceGaps.length > 0 || proofItems.length > 0;

  return [
    {
      id: "source",
      label: "Source",
      value: sourceLabel,
      detail: `${sourceDetail} Owner: ${ownerLabel}.`,
      href: sourceHref,
      ariaLabel: sourceAriaLabel,
      badgeVariant: statusVariant
    },
    {
      id: "freshness",
      label: "Freshness",
      value: freshnessLabel,
      detail: freshnessDetail,
      href: proofHref,
      ariaLabel: proofHref ? `Open freshness support for ${affectedOutputLabel}` : null,
      badgeVariant: freshnessLabel === "No due date" ? "outline" : statusVariant
    },
    {
      id: "reconciliation",
      label: "Reconciliation",
      value: context.some((value) => value.toLowerCase().includes("reconcil")) ? "Review" : "Route-owned",
      detail: context.some((value) => value.toLowerCase().includes("reconcil"))
        ? context.join(", ")
        : routeOwnedReportingDetail("Reconciliation state"),
      href: primaryActionHref,
      ariaLabel: primaryActionHref ? `Open reconciliation support for ${affectedOutputLabel}` : null,
      badgeVariant: context.some((value) => value.toLowerCase().includes("reconcil")) ? statusVariant : "outline"
    },
    {
      id: "approvals",
      label: "Approvals",
      value: approvalMatches ? statusLabel : "Route-owned",
      detail: approvalMatches ? nextActionLabel : routeOwnedReportingDetail("Approval state"),
      href: primaryActionHref,
      ariaLabel: primaryActionHref ? `Open approval support for ${affectedOutputLabel}` : null,
      badgeVariant: approvalMatches ? statusVariant : "outline"
    },
    {
      id: "report-usage",
      label: "Report Usage",
      value: deliveryMatches ? affectedOutputLabel : "Route-owned",
      detail: deliveryMatches ? `Reporting output: ${affectedOutputLabel}.` : routeOwnedReportingDetail("Report usage"),
      href: primaryActionHref,
      ariaLabel: primaryActionHref ? `Open report usage for ${affectedOutputLabel}` : null,
      badgeVariant: deliveryMatches ? statusVariant : "outline"
    },
    {
      id: "blockers",
      label: "Blockers",
      value: blockedLabel,
      detail: statusLabel,
      href: primaryActionHref,
      ariaLabel: primaryActionHref ? `Open blockers for ${affectedOutputLabel}` : null,
      badgeVariant: statusVariant
    },
    {
      id: "evidence-packet",
      label: "Evidence Packet",
      value: proofLabel,
      detail: proofDetail,
      href: proofHref,
      ariaLabel: proofHref ? `Open evidence packet for ${affectedOutputLabel}` : null,
      badgeVariant: evidenceMatches ? statusVariant : "outline"
    },
    {
      id: "audit-trail",
      label: "Audit Trail",
      value: secondaryActionLabel ?? primaryActionLabel,
      detail: secondaryActionHref ?? primaryActionHref ?? routeOwnedReportingDetail("Audit trail"),
      href: secondaryActionHref ?? primaryActionHref,
      ariaLabel: (secondaryActionHref ?? primaryActionHref) ? `Open audit trail for ${affectedOutputLabel}` : null,
      badgeVariant: secondaryActionHref || primaryActionHref ? statusVariant : "outline"
    }
  ];
}

function buildDailyWorkItem(item: ReportingHubDailyWorkInput): ReportingHubDailyWorkItem {
  const tone = presentTone(item.tone);
  const kind = normalizeDailyWorkKind(item.kind);
  const kindLabel = presentWorkKind(kind);
  const evidenceGaps = compactStrings(item.evidenceGaps);
  const context = compactStrings(item.context);
  const blockedLabel = isBlockedDailyWorkInput(item) ? "Blocked" : blockedLabelForTone(tone);
  const affectedOutputLabel = affectedOutputForItem(item, context);
  const dueLabel = formatDueLabel(item.dueAtUtc);
  const proofItems = buildProofItems(item, evidenceGaps);
  const proofLabel = buildProofLabel(evidenceGaps, proofItems);
  const proofPassportItems = buildReportingProofPassportItems({
    sourceLabel: kindLabel,
    sourceDetail: item.detail,
    sourceHref: item.primaryActionHref,
    sourceAriaLabel: item.primaryActionHref ? `${item.primaryActionLabel}: ${item.title}` : null,
    freshnessLabel: dueLabel ?? "No due date",
    freshnessDetail: dueLabel ?? "No due date is retained for this Reporting work item.",
    ownerLabel: item.owner,
    affectedOutputLabel,
    nextActionLabel: item.primaryActionLabel,
    statusLabel: item.statusLabel,
    blockedLabel,
    proofLabel,
    proofItems,
    evidenceGaps,
    context,
    primaryActionHref: item.primaryActionHref,
    primaryActionLabel: item.primaryActionLabel,
    secondaryActionHref: item.secondaryActionHref ?? null,
    secondaryActionLabel: item.secondaryActionLabel ?? null,
    tone
  });
  const detailParts = [item.detail, dueLabel, evidenceGaps.length > 0 ? `${countUnit(evidenceGaps.length, "evidence gap")}` : ""]
    .filter((part): part is string => Boolean(part && part.trim().length > 0));
  const controlQuestionSummary = [
    `Blocked: ${blockedLabel}`,
    `Owner: ${item.owner}`,
    `Affected output: ${affectedOutputLabel}`,
    `Next action: ${item.primaryActionLabel}`,
    `Proof: ${proofLabel}`
  ].join(". ");

  return {
    workItemId: item.workItemId,
    kind,
    kindLabel,
    title: item.title,
    statusLabel: item.statusLabel,
    detail: item.detail,
    tone,
    badgeVariant: badgeVariantForTone(tone),
    owner: item.owner,
    blockedLabel,
    affectedOutputLabel,
    dueLabel,
    dueAtUtc: item.dueAtUtc,
    nextActionLabel: item.primaryActionLabel,
    primaryActionLabel: item.primaryActionLabel,
    primaryActionHref: item.primaryActionHref,
    proofLabel,
    proofItems,
    proofPassportStatusLabel: blockedLabel,
    proofPassportSummary: buildReportingProofPassportSummary(affectedOutputLabel, proofPassportItems),
    proofPassportItems,
    evidenceGaps,
    context,
    secondaryActionLabel: item.secondaryActionLabel ?? null,
    secondaryActionHref: item.secondaryActionHref ?? null,
    controlQuestionSummary,
    ariaLabel: `${kindLabel}: ${item.title}. ${item.statusLabel}. ${controlQuestionSummary}. ${detailParts.join(" ")}`
  };
}

function isBlockedDailyWorkInput(item: ReportingHubDailyWorkInput): boolean {
  const kind = normalizeDailyWorkKind(item.kind);
  return presentTone(item.tone) === "danger" || kind === "blocked-package" || kind === "delivery-failure";
}

function isBlockedDailyWorkItem(item: ReportingHubDailyWorkItem): boolean {
  return item.tone === "danger" || item.kind === "blocked-package" || item.kind === "delivery-failure";
}

function isReviewDailyWorkItem(item: ReportingHubDailyWorkItem): boolean {
  return item.tone === "warning";
}

function dailyWorkPriority(item: ReportingHubDailyWorkItem): number {
  switch (item.tone) {
    case "danger":
      return 0;
    case "warning":
      return 1;
    case "muted":
      return 2;
    default:
      return 3;
  }
}

function compareDailyWork(left: ReportingHubDailyWorkItem, right: ReportingHubDailyWorkItem): number {
  const priority = dailyWorkPriority(left) - dailyWorkPriority(right);
  if (priority !== 0) {
    return priority;
  }

  if (left.dueAtUtc && right.dueAtUtc) {
    return left.dueAtUtc.localeCompare(right.dueAtUtc);
  }

  if (left.dueAtUtc) {
    return -1;
  }

  if (right.dueAtUtc) {
    return 1;
  }

  return left.title.localeCompare(right.title);
}

function buildDailyWorkSummary(dailyWork: ReportingHubDailyWorkItem[]): string {
  if (dailyWork.length === 0) {
    return "No daily reporting work is queued.";
  }

  const blocked = dailyWork.filter(isBlockedDailyWorkItem).length;
  const review = dailyWork.filter(isReviewDailyWorkItem).length;
  return `${countUnit(dailyWork.length, "daily item")} · ${blocked} blocked · ${review} need review`;
}

function dailyWorkCategoryRank(kind: string): number {
  switch (kind) {
    case "blocked-package":
      return 0;
    case "delivery-failure":
      return 1;
    case "approval-needed":
      return 2;
    case "due-package":
      return 3;
    case "restatement":
      return 4;
    case "evidence-gap":
      return 5;
    default:
      return 9;
  }
}

function dailyWorkToneRank(item: ReportingHubDailyWorkItem): number {
  if (item.tone === "danger") {
    return 0;
  }

  if (item.tone === "warning") {
    return 1;
  }

  return 2;
}

function dailyWorkCategoryTitle(kind: string): string {
  switch (kind) {
    case "approval-needed":
      return "Approval review queue";
    case "blocked-package":
      return "Blocked package queue";
    case "delivery-failure":
      return "Delivery readiness queue";
    case "due-package":
      return "Due package queue";
    case "restatement":
      return "Restatement review queue";
    case "evidence-gap":
      return "Evidence and provenance gaps";
    default:
      return "Reporting work queue";
  }
}

function dailyWorkCategoryStatus(kind: string, hasBlockedItem: boolean, hasReviewItem: boolean): string {
  switch (kind) {
    case "approval-needed":
      return "Approvals";
    case "blocked-package":
      return "Blocked";
    case "delivery-failure":
      return "Delivery review";
    case "due-package":
      return "Due packages";
    case "restatement":
      return "Restatements";
    case "evidence-gap":
      return hasBlockedItem ? "Blocked evidence" : "Evidence review";
    default:
      return hasBlockedItem ? "Blocked" : hasReviewItem ? "Review" : "Ready";
  }
}

function buildDecisionQueueDetail(items: ReportingHubDailyWorkItem[], evidenceGapCount: number): string {
  const first = items[0];
  const due = first.dueLabel ? ` ${first.dueLabel}.` : "";
  const owner = first.owner.trim().length > 0 ? ` Owner: ${first.owner}.` : "";
  const gaps = evidenceGapCount > 0
    ? ` ${evidenceGapCount} retained evidence/provenance gap(s) require review.`
    : "";
  const context = first.context.length > 0 ? ` Context: ${first.context.slice(0, 3).join(", ")}.` : "";
  const additional = items.length > 1 ? ` ${items.length - 1} additional item(s) share this queue.` : "";

  return `${first.title}: ${first.detail}${owner}${due}${gaps}${context}${additional}`;
}

function buildDecisionQueueProofLabel(evidenceGapCount: number, first: ReportingHubDailyWorkItem): string {
  if (evidenceGapCount > 0) {
    return `${evidenceGapCount} proof gap${evidenceGapCount === 1 ? "" : "s"}`;
  }

  return first.proofLabel;
}

function buildDecisionQueueProofItems(items: ReportingHubDailyWorkItem[], evidenceGapCount: number): string[] {
  if (evidenceGapCount > 0) {
    return compactStrings(items.flatMap((item) => item.evidenceGaps)).slice(0, 4);
  }

  return items[0].proofItems.slice(0, 4);
}

function buildDecisionQueue(kind: string, items: ReportingHubDailyWorkItem[]): ReportingHubDecisionQueue {
  const orderedItems = [...items].sort((left, right) => {
    const tone = dailyWorkToneRank(left) - dailyWorkToneRank(right);
    if (tone !== 0) {
      return tone;
    }

    if (left.dueAtUtc && right.dueAtUtc) {
      return left.dueAtUtc.localeCompare(right.dueAtUtc);
    }

    if (left.dueAtUtc) {
      return -1;
    }

    if (right.dueAtUtc) {
      return 1;
    }

    return left.title.localeCompare(right.title);
  });
  const first = orderedItems[0];
  const evidenceGapCount = orderedItems.reduce((count, item) => count + item.evidenceGaps.length, 0);
  const hasBlockedItem = orderedItems.some(isBlockedDailyWorkItem);
  const hasReviewItem = orderedItems.some(isReviewDailyWorkItem) || evidenceGapCount > 0;
  const tone: ReportingHubTone = hasBlockedItem ? "danger" : hasReviewItem ? "warning" : "muted";
  const proofLabel = buildDecisionQueueProofLabel(evidenceGapCount, first);
  const proofItems = buildDecisionQueueProofItems(orderedItems, evidenceGapCount);
  const statusLabel = dailyWorkCategoryStatus(kind, hasBlockedItem, hasReviewItem);
  const blockedLabel = hasBlockedItem ? "Blocked" : hasReviewItem ? "Needs review" : "Ready";
  const proofPassportItems = buildReportingProofPassportItems({
    sourceLabel: dailyWorkCategoryTitle(kind),
    sourceDetail: buildDecisionQueueDetail(orderedItems, evidenceGapCount),
    sourceHref: first.primaryActionHref,
    sourceAriaLabel: first.primaryActionHref ? `${first.primaryActionLabel}: ${dailyWorkCategoryTitle(kind)}` : null,
    freshnessLabel: first.dueLabel ?? "No due date",
    freshnessDetail: first.dueLabel ?? "No due date is retained for this Reporting decision queue.",
    ownerLabel: first.owner,
    affectedOutputLabel: first.affectedOutputLabel,
    nextActionLabel: first.nextActionLabel,
    statusLabel,
    blockedLabel,
    proofLabel,
    proofItems,
    evidenceGaps: compactStrings(orderedItems.flatMap((item) => item.evidenceGaps)),
    context: compactStrings(orderedItems.flatMap((item) => item.context)),
    primaryActionHref: first.primaryActionHref,
    primaryActionLabel: first.primaryActionLabel,
    secondaryActionHref: first.secondaryActionHref,
    secondaryActionLabel: first.secondaryActionLabel,
    tone
  });
  const controlQuestionSummary = [
    `Blocked: ${blockedLabel}`,
    `Owner: ${first.owner}`,
    `Affected output: ${first.affectedOutputLabel}`,
    `Next action: ${first.nextActionLabel}`,
    `Proof: ${proofLabel}`
  ].join(". ");

  return {
    id: kind,
    title: dailyWorkCategoryTitle(kind),
    statusLabel,
    countLabel: evidenceGapCount > 0
      ? `${orderedItems.length} item(s), ${evidenceGapCount} gap(s)`
      : `${orderedItems.length} item(s)`,
    detail: buildDecisionQueueDetail(orderedItems, evidenceGapCount),
    tone,
    badgeVariant: badgeVariantForTone(tone),
    isBlocked: hasBlockedItem,
    ownerLabel: first.owner,
    affectedOutputLabel: first.affectedOutputLabel,
    nextActionLabel: first.nextActionLabel,
    primaryActionLabel: first.primaryActionLabel,
    primaryActionHref: first.primaryActionHref,
    secondaryActionLabel: first.secondaryActionLabel,
    secondaryActionHref: first.secondaryActionHref,
    proofLabel,
    proofItems,
    proofPassportStatusLabel: blockedLabel,
    proofPassportSummary: buildReportingProofPassportSummary(first.affectedOutputLabel, proofPassportItems),
    proofPassportItems,
    controlQuestionSummary,
    ariaLabel: `${dailyWorkCategoryTitle(kind)}. ${dailyWorkCategoryStatus(kind, hasBlockedItem, hasReviewItem)}. ${controlQuestionSummary}. ${buildDecisionQueueDetail(orderedItems, evidenceGapCount)}`
  };
}

function buildDecisionQueues(dailyWork: ReportingHubDailyWorkItem[]): ReportingHubDecisionQueue[] {
  const groups = new Map<string, ReportingHubDailyWorkItem[]>();
  for (const item of dailyWork) {
    const items = groups.get(item.kind) ?? [];
    items.push(item);
    groups.set(item.kind, items);
  }

  return [...groups.entries()]
    .sort(([left], [right]) => dailyWorkCategoryRank(left) - dailyWorkCategoryRank(right) || left.localeCompare(right))
    .map(([kind, items]) => buildDecisionQueue(kind, items));
}

function buildDecisionQueueSummary(decisionQueues: ReportingHubDecisionQueue[]): string {
  if (decisionQueues.length === 0) {
    return "No WPF-aligned reporting decision queues are active.";
  }

  const blocked = decisionQueues.filter((queue) => queue.isBlocked).length;
  const review = decisionQueues.filter((queue) => !queue.isBlocked && queue.tone === "warning").length;
  return `${countUnit(decisionQueues.length, "decision queue")} · ${blocked} blocked · ${review} need review`;
}

function buildCard(
  family: string,
  runs: ReportingHubRunInput[],
  templateNames: Set<string>
): ReportingHubCard {
  const sortedRuns = [...runs].sort(byMostRecent);
  const latestRun = sortedRuns[0] ?? null;
  const latestApproved = sortedRuns.find((run) => run.isLatestApproved) ??
    sortedRuns.find((run) => isApprovedStatus(run.status)) ??
    null;
  const presentation = presentReadiness(latestRun?.status ?? null);
  const openLink = latestRun ? resolveOpenLink(latestRun) : null;

  const isCurrent = presentation.readiness === "Released" || presentation.readiness === "Approved";
  const approvedAsOfLabel = latestApproved
    ? `Approved as of ${formatReportingPeriodLabel(latestApproved.asOfDateLabel)}`
    : "No approved output yet";
  const latestAsOfLabel = latestRun ? formatReportingPeriodLabel(latestRun.asOfDateLabel) : "—";
  const detail = `${countUnit(templateNames.size, "template")} · ${countUnit(runs.length, "run")}`;

  return {
    family,
    templateCount: templateNames.size,
    runCount: runs.length,
    readiness: presentation.readiness,
    statusLabel: presentation.statusLabel,
    statusTone: presentation.statusTone,
    badgeVariant: presentation.badgeVariant,
    latestRunId: latestRun?.runIdLabel ?? null,
    latestAsOfLabel,
    latestStatusLabel: latestRun ? presentation.statusLabel : null,
    approvedAsOfLabel,
    isCurrent,
    needsAttention: !isCurrent,
    openHref: openLink?.href ?? null,
    openLabel: openLink?.label ?? null,
    detail,
    ariaLabel: `${family} reporting family: ${presentation.statusLabel}. ${approvedAsOfLabel}. ${detail}.`
  };
}

export function buildReportingHubModel(
  runs: readonly ReportingHubRunInput[],
  templates: readonly ReportingHubTemplateInput[],
  dailyWorkInput: readonly ReportingHubDailyWorkInput[] = []
): ReportingHubModel {
  const runsByFamily = new Map<string, ReportingHubRunInput[]>();
  const templatesByFamily = new Map<string, Set<string>>();
  const dailyWork = dailyWorkInput.map(buildDailyWorkItem).sort(compareDailyWork);
  const decisionQueues = buildDecisionQueues(dailyWork);

  for (const template of templates) {
    const family = template.family?.trim() || "Uncategorized";
    const names = templatesByFamily.get(family) ?? new Set<string>();
    names.add(template.templateName || template.name);
    templatesByFamily.set(family, names);
  }

  for (const run of runs) {
    const family = run.family?.trim() || "Uncategorized";
    const familyRuns = runsByFamily.get(family) ?? [];
    familyRuns.push(run);
    runsByFamily.set(family, familyRuns);
    if (!templatesByFamily.has(family)) {
      templatesByFamily.set(family, new Set<string>());
    }
  }

  const families = [...templatesByFamily.keys()].sort((left, right) => left.localeCompare(right));
  const cards = families.map((family) =>
    buildCard(family, runsByFamily.get(family) ?? [], templatesByFamily.get(family) ?? new Set<string>())
  );

  const currentCount = cards.filter((card) => card.isCurrent).length;
  const attentionCount = cards.length - currentCount;
  const familyWord = cards.length === 1 ? "family" : "families";
  const summaryLabel = cards.length === 0
    ? "No report families are configured yet."
    : `${currentCount} of ${cards.length} ${familyWord} current · ${attentionCount} need attention`;

  return {
    dailyWork,
    dailyWorkSummaryLabel: buildDailyWorkSummary(dailyWork),
    decisionQueues,
    decisionQueueSummaryLabel: buildDecisionQueueSummary(decisionQueues),
    cards,
    totalFamilies: cards.length,
    currentCount,
    attentionCount,
    summaryLabel,
    isEmpty: cards.length === 0 && dailyWork.length === 0
  };
}
