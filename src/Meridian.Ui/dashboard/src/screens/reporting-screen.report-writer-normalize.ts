// Pure normalisation, labelling and filter-operator helpers for the report writer,
// split out of reporting-screen.report-writer.tsx. Public names are re-exported from there.
import type { ReportingWriterToken } from "@/screens/reporting-screen.view-model";
import type { ReportWriterDraftSettings } from "@/screens/reporting-screen.report-writer";
import type { ReportWriterFilterOperator, ReportWriterGridKind } from "@/types";

export function normalizeReportWriterDraftText(value: string | null | undefined, fallback: string): string {
  const normalized = value?.trim();
  return normalized || fallback;
}

export function normalizeReportWriterIdentifierToken(value: string | null | undefined, fallback: string): string {
  const normalized = normalizeReportWriterDraftText(value, fallback)
    .replace(/[^A-Za-z0-9_.-]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return normalized || fallback;
}

export function buildReportAccessPolicySummary(settings: ReportWriterDraftSettings): string {
  if (settings.accessMode === "CompanyWide") {
    return "Access policy: company-wide report with owner access retained.";
  }

  const principalId = normalizeReportWriterDraftText(settings.principalId, "browser-workstation");
  if (settings.accessMode === "Private") {
    return `Access policy: user-locked to ${principalId}.`;
  }

  return `Access policy: ${settings.principalKind.toLowerCase()} ${principalId}.`;
}

export function normalizeReportWriterGridKind(kind: string): ReportWriterGridKind {
  switch (kind.toLowerCase()) {
    case "detail":
      return "Detail";
    case "topn":
    case "top-n":
      return "TopN";
    case "contribution":
      return "Contribution";
    default:
      return "Pivot";
  }
}

export function parseReportWriterTopN(value: string | number | null | undefined): number {
  const parsed = Number.parseInt(value?.toString() ?? "", 10);
  if (!Number.isFinite(parsed)) {
    return 10;
  }

  return Math.min(100, Math.max(1, parsed));
}

export function normalizeReportWriterTopNText(value: string | null | undefined): string {
  return parseReportWriterTopN(value).toString();
}

export function buildReportWriterDraftTopNLabel(settings: ReportWriterDraftSettings): string {
  return settings.gridKind === "TopN" ? `Top ${parseReportWriterTopN(settings.topN)}` : settings.gridKind;
}

export function buildReportWriterDraftLayoutSummary(
  settings: ReportWriterDraftSettings,
  rowTokens: ReportingWriterToken[],
  columnTokens: ReportingWriterToken[],
  metricTokens: ReportingWriterToken[],
  formulaTokens: ReportingWriterToken[],
  savedFilterCount: number
): string {
  const dimensionCount = rowTokens.length + columnTokens.length;
  // The draft carries every saved filter, so the draft rows are the whole filter set.
  const draftFilterCount = (settings.filterField ? 1 : 0)
    + settings.extraFilters.filter((filter) => filter.field).length;
  const filterSummary = draftFilterCount > 0
    ? `${draftFilterCount} filter${draftFilterCount === 1 ? "" : "s"} in draft`
    : savedFilterCount > 0
      ? `No filters in draft (${savedFilterCount} saved filter${savedFilterCount === 1 ? "" : "s"} removed)`
      : "No filters";
  return [
    `${settings.gridKind} draft`,
    `${dimensionCount} dimension${dimensionCount === 1 ? "" : "s"}`,
    `${metricTokens.length} metric${metricTokens.length === 1 ? "" : "s"}`,
    `${formulaTokens.length} formula${formulaTokens.length === 1 ? "" : "s"}`,
    filterSummary,
    settings.gridKind === "TopN" ? `Top ${parseReportWriterTopN(settings.topN)}` : null
  ].filter((part): part is string => Boolean(part)).join(" · ");
}

export function normalizeReportWriterFilterOperator(value: ReportWriterFilterOperator | string | null | undefined): ReportWriterFilterOperator {
  switch ((value ?? "").toString().toLowerCase()) {
    case "notequals":
    case "not-equals":
      return "NotEquals";
    case "contains":
      return "Contains";
    case "startswith":
    case "starts-with":
      return "StartsWith";
    case "endswith":
    case "ends-with":
      return "EndsWith";
    case "greaterthan":
    case "greater-than":
      return "GreaterThan";
    case "greaterthanorequal":
    case "greater-than-or-equal":
      return "GreaterThanOrEqual";
    case "lessthan":
    case "less-than":
      return "LessThan";
    case "lessthanorequal":
    case "less-than-or-equal":
      return "LessThanOrEqual";
    case "isblank":
    case "is-blank":
      return "IsBlank";
    case "isnotblank":
    case "is-not-blank":
      return "IsNotBlank";
    default:
      return "Equals";
  }
}

export function formatReportWriterFilterOperator(operator: ReportWriterFilterOperator): string {
  switch (operator) {
    case "NotEquals":
      return "!=";
    case "Contains":
      return "contains";
    case "StartsWith":
      return "starts with";
    case "EndsWith":
      return "ends with";
    case "GreaterThan":
      return ">";
    case "GreaterThanOrEqual":
      return ">=";
    case "LessThan":
      return "<";
    case "LessThanOrEqual":
      return "<=";
    case "IsBlank":
      return "is blank";
    case "IsNotBlank":
      return "is not blank";
    default:
      return "=";
  }
}

export function isBlankFilterOperator(operator: ReportWriterFilterOperator | string): boolean {
  const normalized = normalizeReportWriterFilterOperator(operator);
  return normalized === "IsBlank" || normalized === "IsNotBlank";
}
