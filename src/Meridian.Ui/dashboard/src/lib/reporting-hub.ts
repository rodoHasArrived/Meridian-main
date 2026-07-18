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
import {
  compareIsoDate,
  formatReportingPeriodLabel,
  hasRetainedReportingAsOfDateValue,
  reportingRunRequiresPeriodConfirmation
} from "@/lib/reporting-periods";
import { pluralizeCount } from "@/lib/format";
import { workstationRouteWithQuery } from "@/lib/workspace";
export type ReportingHubReadiness =
  | "Released"
  | "Approved"
  | "InReview"
  | "Draft"
  | "Failed"
  | "NoRuns";

export type ReportingHubTone = "success" | "warning" | "danger" | "muted";
export type ReportingHubBadgeVariant = "success" | "warning" | "danger" | "outline";

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
  id?: string;
  templateName: string;
  name: string;
  family: string;
  canRunOnDemand?: boolean;
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
  blockedLabel: string;
  detail: string;
  tone: ReportingHubTone;
  badgeVariant: ReportingHubBadgeVariant;
  owner: string;
  affectedOutputLabel: string;
  dueLabel: string | null;
  primaryActionLabel: string;
  primaryActionHref: string | null;
  nextActionLabel: string;
  proofLabel: string;
  evidenceGaps: string[];
  context: string[];
  secondaryActionLabel: string | null;
  secondaryActionHref: string | null;
  ariaLabel: string;
}

export interface ReportingHubCard {
  familyKey: string;
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
  nextActionHref: string;
  nextActionLabel: string;
  detail: string;
  ariaLabel: string;
}

export interface ReportingHubModel {
  dailyWork: ReportingHubDailyWorkItem[];
  dailyWorkSummaryLabel: string;
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
    case "published":
    case "released":
      return {
        readiness: "Released",
        statusLabel: (status ?? "").trim().toLowerCase() === "published" ? "Published" : "Released",
        statusTone: "success",
        badgeVariant: "success"
      };
    case "approved":
      return { readiness: "Approved", statusLabel: "Approved", statusTone: "success", badgeVariant: "success" };
    case "awaitingapproval":
    case "awaiting approval":
      return { readiness: "InReview", statusLabel: "Awaiting approval", statusTone: "warning", badgeVariant: "warning" };
    case "controllerreviewed":
    case "controller reviewed":
      return { readiness: "InReview", statusLabel: "Controller reviewed", statusTone: "warning", badgeVariant: "warning" };
    case "generated":
    case "evidencebundled":
    case "evidence bundled":
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
  return normalized === "approved" || normalized === "released" || normalized === "published";
}

// Most recent first: ISO as-of dates compare correctly; non-ISO fallbacks sort stably after.
function byMostRecent(left: ReportingHubRunInput, right: ReportingHubRunInput): number {
  const leftHasRetainedDate = hasRetainedReportingAsOfDateValue(left.asOfDateLabel);
  const rightHasRetainedDate = hasRetainedReportingAsOfDateValue(right.asOfDateLabel);
  if (leftHasRetainedDate !== rightHasRetainedDate) {
    return leftHasRetainedDate ? -1 : 1;
  }

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
  return pluralizeCount(count, singular);
}

function presentWorkKind(kind: string): string {
  switch (kind.trim().toLowerCase()) {
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

function presentBlockedLabel(kind: string, tone: ReportingHubTone, statusLabel: string): string {
  const normalizedKind = kind.trim().toLowerCase();
  if (tone === "danger" || normalizedKind === "blocked-package" || normalizedKind === "delivery-failure") {
    return statusLabel.trim() || "Blocked";
  }

  if (tone === "warning") {
    return statusLabel.trim() || "Needs review";
  }

  return "Not blocked";
}

function buildAffectedOutputLabel(item: ReportingHubDailyWorkInput, context: readonly string[]): string {
  return context[0] ?? item.title;
}

function buildProofLabel(evidenceGaps: readonly string[], secondaryActionLabel: string | null | undefined): string {
  if (evidenceGaps.length > 0) {
    return countUnit(evidenceGaps.length, "evidence gap");
  }

  return secondaryActionLabel?.trim() || "Proof retained";
}

function buildDailyWorkItem(item: ReportingHubDailyWorkInput): ReportingHubDailyWorkItem {
  const tone = presentTone(item.tone);
  const kindLabel = presentWorkKind(item.kind);
  const evidenceGaps = compactStrings(item.evidenceGaps);
  const context = compactStrings(item.context);
  const dueLabel = formatDueLabel(item.dueAtUtc);
  const owner = item.owner.trim() || "Unassigned";
  const affectedOutputLabel = buildAffectedOutputLabel(item, context);
  const blockedLabel = presentBlockedLabel(item.kind, tone, item.statusLabel);
  const nextActionLabel = item.primaryActionLabel.trim() || "Review";
  const proofLabel = buildProofLabel(evidenceGaps, item.secondaryActionLabel);
  const detailParts = [item.detail, dueLabel, evidenceGaps.length > 0 ? `${countUnit(evidenceGaps.length, "evidence gap")}` : ""]
    .filter((part): part is string => part != null && part.trim().length > 0);

  return {
    workItemId: item.workItemId,
    kind: item.kind,
    kindLabel,
    title: item.title,
    statusLabel: item.statusLabel,
    blockedLabel,
    detail: item.detail,
    tone,
    badgeVariant: badgeVariantForTone(tone),
    owner,
    affectedOutputLabel,
    dueLabel,
    primaryActionLabel: item.primaryActionLabel,
    primaryActionHref: item.primaryActionHref,
    nextActionLabel,
    proofLabel,
    evidenceGaps,
    context,
    secondaryActionLabel: item.secondaryActionLabel ?? null,
    secondaryActionHref: item.secondaryActionHref ?? null,
    ariaLabel: `${kindLabel}: ${item.title}. ${blockedLabel}. Owner ${owner}. Output ${affectedOutputLabel}. Next action ${nextActionLabel}. Proof ${proofLabel}. ${detailParts.join(" ")}`
  };
}

function buildDailyWorkSummary(dailyWork: ReportingHubDailyWorkItem[], familyAttentionCount: number): string {
  if (dailyWork.length === 0) {
    return familyAttentionCount > 0
      ? `No urgent work queued · ${countUnit(familyAttentionCount, "report family")} ${familyAttentionCount === 1 ? "needs" : "need"} setup or review`
      : "No urgent reporting work is queued.";
  }

  const blocked = dailyWork.filter((item) => item.tone === "danger").length;
  const review = dailyWork.filter((item) => item.tone === "warning").length;
  return `${countUnit(dailyWork.length, "daily item")} · ${blocked} blocked · ${review} need review`;
}

function buildCard(
  familyKey: string,
  runs: ReportingHubRunInput[],
  templates: ReportingHubTemplateInput[]
): ReportingHubCard {
  const sortedRuns = [...runs].sort(byMostRecent);
  const latestRun = sortedRuns[0] ?? null;
  const latestApproved = sortedRuns.find((run) => run.isLatestApproved) ??
    sortedRuns.find((run) => isApprovedStatus(run.status)) ??
    null;
  const rawPresentation = presentReadiness(latestRun?.status ?? null);
  const needsPeriodConfirmation = latestRun
    ? reportingRunRequiresPeriodConfirmation(latestRun.status, latestRun.asOfDateLabel)
    : false;
  const presentation: ReadinessPresentation = needsPeriodConfirmation
    ? {
        readiness: "InReview",
        statusLabel: "Period confirmation required",
        statusTone: "warning",
        badgeVariant: "warning"
      }
    : rawPresentation;
  const openLink = latestRun ? resolveOpenLink(latestRun) : null;
  const family = formatReportingFamilyLabel(familyKey);
  const templateNames = new Set(templates.map((template) => template.templateName || template.name));
  const isCurrent = presentation.readiness === "Released" || presentation.readiness === "Approved";
  const action = resolveFamilyAction(familyKey, family, templates, latestRun, openLink, isCurrent);
  const approvedAsOfLabel = latestApproved
    ? hasRetainedReportingAsOfDateValue(latestApproved.asOfDateLabel)
      ? `Approved as of ${formatReportingPeriodLabel(latestApproved.asOfDateLabel)}`
      : "Approved output needs period confirmation"
    : "No approved output yet";
  const latestAsOfLabel = latestRun
    ? hasRetainedReportingAsOfDateValue(latestRun.asOfDateLabel)
      ? formatReportingPeriodLabel(latestRun.asOfDateLabel)
      : "No period confirmed"
    : "—";
  const detail = `${countUnit(templateNames.size, "template")} · ${countUnit(runs.length, "run")}`;

  return {
    familyKey,
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
    nextActionHref: action.href,
    nextActionLabel: action.label,
    detail,
    ariaLabel: `${family} reporting family: ${presentation.statusLabel}. ${approvedAsOfLabel}. Next action ${action.label}. ${detail}.`
  };
}

function resolveFamilyAction(
  familyKey: string,
  familyLabel: string,
  templates: readonly ReportingHubTemplateInput[],
  latestRun: ReportingHubRunInput | null,
  openLink: { href: string; label: string } | null,
  isCurrent: boolean
): { href: string; label: string } {
  if (openLink && isCurrent) {
    return openLink;
  }

  if (latestRun && !isCurrent) {
    return {
      href: openLink?.href ?? workstationRouteWithQuery("reportingRunStatus", { runId: latestRun.runIdLabel }),
      label: "Review latest run"
    };
  }

  const runnable = templates.find((template) => template.canRunOnDemand === true);
  if (runnable) {
    return {
      href: workstationRouteWithQuery("reportingRunParameters", {
        templateId: runnable.id?.trim() || runnable.templateName
      }),
      label: `Run ${familyLabel}`
    };
  }

  if (latestRun) {
    return {
      href: workstationRouteWithQuery("reportingRunStatus", { runId: latestRun.runIdLabel }),
      label: "Review latest run"
    };
  }

  return {
    href: workstationRouteWithQuery("reportingReportBuilder", { family: familyKey }),
    label: `Set up ${familyLabel}`
  };
}

export function formatReportingFamilyLabel(value: string): string {
  const normalized = value
    .trim()
    .replace(/[_-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/\s+/g, " ");

  if (!normalized) {
    return "Uncategorized";
  }

  return normalized
    .split(" ")
    .map((word) => word.length <= 3 && word === word.toUpperCase()
      ? word
      : `${word.charAt(0).toUpperCase()}${word.slice(1)}`)
    .join(" ");
}

export function buildReportingHubModel(
  runs: readonly ReportingHubRunInput[],
  templates: readonly ReportingHubTemplateInput[],
  dailyWorkInput: readonly ReportingHubDailyWorkInput[] = []
): ReportingHubModel {
  const runsByFamily = new Map<string, ReportingHubRunInput[]>();
  const templatesByFamily = new Map<string, ReportingHubTemplateInput[]>();
  const dailyWork = dailyWorkInput.map(buildDailyWorkItem);

  for (const template of templates) {
    const family = template.family?.trim() || "Uncategorized";
    const familyTemplates = templatesByFamily.get(family) ?? [];
    if (!familyTemplates.some((candidate) => (candidate.id ?? candidate.templateName) === (template.id ?? template.templateName))) {
      familyTemplates.push(template);
    }
    templatesByFamily.set(family, familyTemplates);
  }

  for (const run of runs) {
    const family = run.family?.trim() || "Uncategorized";
    const familyRuns = runsByFamily.get(family) ?? [];
    familyRuns.push(run);
    runsByFamily.set(family, familyRuns);
    if (!templatesByFamily.has(family)) {
      templatesByFamily.set(family, []);
    }
  }

  const families = [...templatesByFamily.keys()].sort((left, right) => left.localeCompare(right));
  const cards = families.map((family) =>
    buildCard(family, runsByFamily.get(family) ?? [], templatesByFamily.get(family) ?? [])
  );

  const currentCount = cards.filter((card) => card.isCurrent).length;
  const attentionCount = cards.length - currentCount;
  const familyWord = cards.length === 1 ? "family" : "families";
  const summaryLabel = cards.length === 0
    ? "No report families are configured yet."
    : `${currentCount} of ${cards.length} ${familyWord} current · ${attentionCount} need attention`;

  return {
    dailyWork,
    dailyWorkSummaryLabel: buildDailyWorkSummary(dailyWork, attentionCount),
    cards,
    totalFamilies: cards.length,
    currentCount,
    attentionCount,
    summaryLabel,
    isEmpty: cards.length === 0 && dailyWork.length === 0
  };
}
