import { formatReportWriterFilterOperator, isBlankFilterOperator, normalizeReportWriterFilterOperator, normalizeReportWriterGridKind, parseReportWriterTopN } from "@/screens/reporting-screen.report-writer";
import type {
  ReportWriterChartDraft,
  ReportWriterDraftSettings,
  ReportWriterDropZone,
  ReportWriterFormatRuleDraft
} from "@/screens/reporting-screen.report-writer";
import type { ReportingWriterGridRow, ReportingWriterToken } from "@/screens/reporting-screen.view-model";
import type { ReportTemplateDraftRequest, ReportWriterAggregateFunction, ReportWriterChartDefinition, ReportWriterFilterDefinition, ReportWriterFormatRule, ReportWriterGridDefinition, ReportWriterGridKind, ReportWriterMetricDefinition } from "@/types";

// Report-writer request/formula/preview builders extracted from reporting-screen.tsx
// to keep the screen root focused on rendering. These are pure helpers (no JSX, no
// component state): grid/access/filter/metric/formula request builders, the formula
// field parser.

export function buildReportWriterGridDefinition(
  grid: ReportingWriterGridRow,
  zones: Record<ReportWriterDropZone, ReportingWriterToken[]>,
  settings: ReportWriterDraftSettings,
  chartDraft?: ReportWriterChartDraft | null,
  formatRules?: ReportWriterFormatRuleDraft[] | null
): ReportWriterGridDefinition {
  const kind = normalizeReportWriterGridKind(settings.gridKind);
  const metrics = normalizeWriterMetrics(zones.metrics, kind);
  return {
    gridId: grid.gridId,
    title: grid.title,
    kind,
    rowFields: normalizeStringList(zones.rowFields.map(resolveWriterFieldName)),
    columnFields: normalizeStringList(zones.columnFields.map(resolveWriterFieldName)),
    metrics,
    formulas: normalizeWriterFormulas(zones.formulas),
    topN: kind === "TopN" ? parseReportWriterTopN(settings.topN) : null,
    sortBy: kind === "Contribution" ? "contributionAbsPercent" : grid.sortBy,
    sortDescending: grid.sortDescending,
    filters: buildWriterFilters(settings),
    formatRules: buildWriterFormatRules(formatRules),
    chart: buildWriterChartDefinition(chartDraft)
  };
}

export function buildWriterFormatRules(drafts: ReportWriterFormatRuleDraft[] | null | undefined): ReportWriterFormatRule[] | null {
  if (!drafts || drafts.length === 0) return null;
  const valid = drafts.filter((d) => d.column.trim().length > 0);
  if (valid.length === 0) return null;
  return valid.map((d) => ({
    column: d.column.trim(),
    operator: d.operator,
    value: d.value || null,
    style: d.style
  }));
}

export function buildWriterChartDefinition(draft: ReportWriterChartDraft | null | undefined): ReportWriterChartDefinition | null {
  if (!draft?.enabled || !draft.categoryField.trim()) return null;
  const valueColumns = draft.valueColumns
    .split(",")
    .map((v) => v.trim())
    .filter((v) => v.length > 0);
  if (valueColumns.length === 0) return null;
  return { type: draft.type, categoryField: draft.categoryField.trim(), valueColumns };
}

export function buildReportAccessPolicy(settings: ReportWriterDraftSettings): ReportTemplateDraftRequest["accessPolicy"] {
  if (settings.accessMode === "CompanyWide") {
    return {
      mode: "CompanyWide",
      allowOwnerAccess: true
    };
  }

  const principalId = normalizeDraftText(settings.principalId, "browser-workstation");
  const principalKind = settings.accessMode === "Private" ? "User" : settings.principalKind;
  return {
    mode: settings.accessMode,
    ownerPrincipalId: settings.accessMode === "Private" ? principalId : null,
    principals: [
      {
        kind: principalKind,
        principalId,
        displayName: principalId
      }
    ],
    allowOwnerAccess: true
  };
}

export function buildWriterFilters(settings: ReportWriterDraftSettings): ReportWriterFilterDefinition[] | null {
  const candidates = [
    { field: settings.filterField, operator: settings.filterOperator, value: settings.filterValue },
    ...(settings.extraFilters ?? [])
  ];
  const filters: ReportWriterFilterDefinition[] = [];
  for (const candidate of candidates) {
    const field = normalizeDraftText(candidate.field, "");
    if (!field) {
      continue;
    }

    const operator = normalizeReportWriterFilterOperator(candidate.operator);
    const value = isBlankFilterOperator(operator)
      ? null
      : normalizeDraftText(candidate.value, "");
    // An incomplete row (a comparison with no value yet) is left out rather than sent.
    if (!isBlankFilterOperator(operator) && !value) {
      continue;
    }

    filters.push({
      field,
      operator,
      value,
      label: isBlankFilterOperator(operator)
        ? `${field} ${formatReportWriterFilterOperator(operator)}`
        : `${field} ${formatReportWriterFilterOperator(operator)} ${value}`
    });
  }

  return filters.length > 0 ? filters : null;
}

export function normalizeWriterMetrics(
  tokens: ReportingWriterToken[],
  gridKind: ReportWriterGridKind | null = null
): ReportWriterMetricDefinition[] {
  const metrics = tokens
    .map(tokenToMetricDefinition)
    .filter((metric): metric is ReportWriterMetricDefinition => Boolean(metric));
  const deduped = dedupeBy(metrics, (metric) => metric.name.toLowerCase());
  return gridKind === "Contribution" ? preferContributionMetric(deduped) : deduped;
}

export function tokenToMetricDefinition(token: ReportingWriterToken): ReportWriterMetricDefinition | null {
  if (token.kind === "formula") {
    return null;
  }

  const sourceField = normalizeDraftText(token.sourceField ?? token.fieldName ?? token.label, "");
  if (!sourceField) {
    return null;
  }

  const name = normalizeIdentifierToken(token.name ?? sourceField, sourceField);
  return {
    name,
    sourceField,
    function: normalizeAggregateFunction(token.function),
    label: token.kind === "metric" ? token.label : sourceField
  };
}

export function preferContributionMetric(metrics: ReportWriterMetricDefinition[]): ReportWriterMetricDefinition[] {
  const contributionIndex = metrics.findIndex((metric) =>
    isPnlLikeField(metric.name)
    || isPnlLikeField(metric.sourceField)
    || isPnlLikeField(metric.label));
  if (contributionIndex <= 0) {
    return metrics;
  }

  const next = [...metrics];
  const [contributionMetric] = next.splice(contributionIndex, 1);
  next.unshift(contributionMetric);
  return next;
}

export function normalizeWriterFormulas(tokens: ReportingWriterToken[]) {
  const formulas = tokens
    .map(tokenToFormulaDefinition)
    .filter((formula): formula is NonNullable<ReturnType<typeof tokenToFormulaDefinition>> => Boolean(formula));
  return dedupeBy(formulas, (formula) => formula.name.toLowerCase());
}

export function tokenToFormulaDefinition(token: ReportingWriterToken) {
  if (token.kind === "metric") {
    const metricName = normalizeIdentifierToken(token.name ?? token.label, "");
    return metricName
      ? {
          name: `${metricName}Formula`,
          expression: `{${metricName}}`,
          label: `${token.label} formula`
        }
      : null;
  }

  if (token.kind === "field") {
    const field = normalizeDraftText(token.fieldName ?? token.sourceField ?? token.label, "");
    return field
      ? {
          name: normalizeIdentifierToken(field, "fieldFormula"),
          expression: `{${field}}`,
          label: field
        }
      : null;
  }

  const name = normalizeIdentifierToken(token.name ?? token.label, "");
  const expression = normalizeDraftText(token.expression ?? token.detail, "");
  return name && expression
    ? {
        name,
        expression,
        label: token.label
      }
    : null;
}

export function resolveWriterFieldName(token: ReportingWriterToken): string {
  return normalizeDraftText(token.fieldName ?? token.sourceField ?? token.name ?? token.label, "");
}

/**
 * Mirrors the server rule for bare references containing hyphens: the whole identifier when it is
 * a known name, otherwise the longest known hyphen-delimited prefix (the rest is a subtraction).
 */
function resolveReportWriterReferenceLength(identifier: string, knownReferences: readonly string[] | null | undefined): number {
  if (!knownReferences || !identifier.includes("-")) {
    return identifier.length;
  }

  const known = new Set(knownReferences.filter(Boolean).map((name) => name.toLowerCase()));
  if (known.has(identifier.toLowerCase())) {
    return identifier.length;
  }

  for (let split = identifier.lastIndexOf("-"); split > 0; split = identifier.lastIndexOf("-", split - 1)) {
    if (known.has(identifier.slice(0, split).toLowerCase())) {
      return split;
    }
  }

  return identifier.length;
}

export function extractReportWriterFormulaFields(
  expression: string | null | undefined,
  knownReferences?: readonly string[] | null
): string[] {
  if (!expression) {
    return [];
  }

  const fields: string[] = [];
  let position = 0;
  while (position < expression.length) {
    const current = expression[position];
    if (isReportWriterIdentifierStart(current)) {
      const identifierStart = position;
      const identifier = readReportWriterIdentifier(expression, position);
      position += identifier.length;
      const nextToken = skipReportWriterWhitespace(expression, position);
      if (identifier.toLowerCase() === "total" && expression[nextToken] === "(") {
        const totalArgument = readReportWriterFunctionFieldArgument(expression, nextToken + 1);
        if (totalArgument) {
          fields.push(totalArgument.field);
          position = totalArgument.nextPosition;
          continue;
        }
      }

      if (isReportWriterFormulaFunction(identifier) && expression[nextToken] === "(") {
        position = nextToken + 1;
        continue;
      }

      const referenceLength = resolveReportWriterReferenceLength(identifier, knownReferences);
      fields.push(identifier.slice(0, referenceLength));
      position = identifierStart + Math.max(referenceLength, 1);
      continue;
    }

    if (current !== "{") {
      position += 1;
      continue;
    }

    const end = expression.indexOf("}", position + 1);
    if (end < 0) {
      break;
    }

    const field = expression.slice(position + 1, end).trim();
    if (field) {
      fields.push(field);
    }

    position = end + 1;
  }

  return normalizeStringList(fields);
}

export function readReportWriterFunctionFieldArgument(
  expression: string,
  argumentStart: number
): { field: string; nextPosition: number } | null {
  const start = skipReportWriterWhitespace(expression, argumentStart);
  if (start >= expression.length) {
    return null;
  }

  if (expression[start] === "{") {
    const closeBrace = expression.indexOf("}", start + 1);
    if (closeBrace < 0) {
      return null;
    }

    const closeParen = skipReportWriterWhitespace(expression, closeBrace + 1);
    if (expression[closeParen] !== ")") {
      return null;
    }

    const field = expression.slice(start + 1, closeBrace).trim();
    return field ? { field, nextPosition: closeParen + 1 } : null;
  }

  const close = expression.indexOf(")", start);
  if (close < 0) {
    return null;
  }

  const field = expression.slice(start, close).trim();
  return field ? { field, nextPosition: close + 1 } : null;
}

export function readReportWriterIdentifier(expression: string, start: number): string {
  let position = start;
  while (position < expression.length && isReportWriterIdentifierPart(expression[position])) {
    position += 1;
  }

  return expression.slice(start, position);
}

export function skipReportWriterWhitespace(expression: string, position: number): number {
  while (position < expression.length && /\s/.test(expression[position])) {
    position += 1;
  }

  return position;
}

export function isReportWriterIdentifierStart(value: string | undefined): boolean {
  return Boolean(value && /[A-Za-z_]/.test(value));
}

export function isReportWriterIdentifierPart(value: string | undefined): boolean {
  return Boolean(value && /[A-Za-z0-9_.-]/.test(value));
}

export function isReportWriterFormulaFunction(identifier: string): boolean {
  return ["abs", "min", "max", "median", "stdev", "safedivide", "percent", "basispoints", "round", "if"].includes(identifier.toLowerCase());
}

export function isPnlLikeField(field: string | null | undefined): boolean {
  const normalized = (field ?? "").toLowerCase().replace(/[^a-z0-9]+/g, "");
  return normalized.includes("pnl") || normalized.includes("profitloss");
}

export function normalizeAggregateFunction(value: ReportWriterAggregateFunction | string | null | undefined): ReportWriterAggregateFunction {
  switch ((value ?? "").toString().toLowerCase()) {
    case "count":
      return "Count";
    case "average":
      return "Average";
    case "min":
      return "Min";
    case "max":
      return "Max";
    default:
      return "Sum";
  }
}

export function normalizeStringList(values: string[]): string[] {
  return Array.from(new Set(values.map((value) => value.trim()).filter(Boolean)));
}

export function normalizeDraftText(value: string | null | undefined, fallback: string): string {
  const normalized = value?.trim();
  return normalized || fallback;
}

export function normalizeIdentifierToken(value: string | null | undefined, fallback: string): string {
  const normalized = normalizeDraftText(value, fallback)
    .replace(/[^A-Za-z0-9_.-]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return normalized || fallback;
}

export function parseReportTemplateVersion(version: string): number | null {
  const first = version.split(".", 1)[0];
  const parsed = Number.parseInt(first, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

export function dedupeBy<T>(items: T[], keySelector: (item: T) => string): T[] {
  const seen = new Set<string>();
  const output: T[] = [];
  for (const item of items) {
    const key = keySelector(item);
    if (!seen.has(key)) {
      seen.add(key);
      output.push(item);
    }
  }

  return output;
}
