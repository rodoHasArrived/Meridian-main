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

export interface ReportingHubRunInput {
  templateId: string;
  family: string;
  status: string;
  asOfDateLabel: string;
  runIdLabel: string;
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
  return compareIsoDate(right.asOfDateLabel, left.asOfDateLabel);
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

function buildCard(
  family: string,
  runs: ReportingHubRunInput[],
  templateNames: Set<string>
): ReportingHubCard {
  const sortedRuns = [...runs].sort(byMostRecent);
  const latestRun = sortedRuns[0] ?? null;
  const latestApproved = sortedRuns.find((run) => isApprovedStatus(run.status)) ?? null;
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
  templates: readonly ReportingHubTemplateInput[]
): ReportingHubModel {
  const runsByFamily = new Map<string, ReportingHubRunInput[]>();
  const templatesByFamily = new Map<string, Set<string>>();

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
    cards,
    totalFamilies: cards.length,
    currentCount,
    attentionCount,
    summaryLabel,
    isEmpty: cards.length === 0
  };
}
