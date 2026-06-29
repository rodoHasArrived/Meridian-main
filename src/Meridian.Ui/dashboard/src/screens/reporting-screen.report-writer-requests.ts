import {
  formatReportWriterFilterOperator,
  isBlankFilterOperator,
  normalizeReportWriterFilterOperator,
  normalizeReportWriterGridKind,
  parseReportWriterTopN,
  type ReportWriterChartDraft,
  type ReportWriterDraftSettings,
  type ReportWriterDropZone,
  type ReportWriterFormatRuleDraft,
  type ReportWriterPreviewDatasetProfile
} from "@/screens/reporting-screen.report-writer";
import type { ReportingWriterGridRow, ReportingWriterToken } from "@/screens/reporting-screen.view-model";
import type {
  ReportTemplateDraftRequest,
  ReportWriterAggregateFunction,
  ReportWriterChartDefinition,
  ReportWriterFilterDefinition,
  ReportWriterFilterOperator,
  ReportWriterFormatRule,
  ReportWriterGridDefinition,
  ReportWriterGridKind,
  ReportWriterMetricDefinition,
  RenderReportTemplateRequest
} from "@/types";

export function buildReportTemplateDraftRequest(
  grid: ReportingWriterGridRow,
  settings: ReportWriterDraftSettings,
  zones: Record<ReportWriterDropZone, ReportingWriterToken[]>
): ReportTemplateDraftRequest {
  const gridDefinition = buildReportWriterGridDefinition(grid, zones, settings);
  return {
    name: normalizeDraftText(settings.name, `${grid.templateId}-draft`),
    displayName: normalizeDraftText(settings.displayName, `${grid.title} Draft`),
    sections: [],
    parameters: [],
    family: grid.family || "CustomReport",
    basedOnVersion: parseReportTemplateVersion(grid.templateVersion),
    rationale: `No-code report-writer draft from ${grid.templateId} ${grid.title}.`,
    grids: [gridDefinition],
    accessPolicy: buildReportAccessPolicy(settings)
  };
}

export function buildRenderReportTemplateRequest(
  grid: ReportingWriterGridRow,
  zones: Record<ReportWriterDropZone, ReportingWriterToken[]>,
  settings: ReportWriterDraftSettings,
  customDatasetRows: Record<string, string>[] | null = null,
  chartDraft?: ReportWriterChartDraft | null,
  formatRules?: ReportWriterFormatRuleDraft[] | null
): RenderReportTemplateRequest {
  const gridDefinition = buildReportWriterGridDefinition(grid, zones, settings, chartDraft, formatRules);
  return {
    templateId: {
      name: grid.templateId,
      version: parseReportTemplateVersion(grid.templateVersion) ?? 1
    },
    parameters: {
      period: "preview-period",
      asOfDate: "preview-as-of",
      preview: "browser-report-writer",
      previewDataset: settings.previewDataset
    },
    datasetRows: customDatasetRows ?? buildReportWriterPreviewRows(gridDefinition, settings.previewDataset),
    grids: [gridDefinition]
  };
}

function buildReportWriterGridDefinition(
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

function buildWriterFormatRules(drafts: ReportWriterFormatRuleDraft[] | null | undefined): ReportWriterFormatRule[] | null {
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

function buildWriterChartDefinition(draft: ReportWriterChartDraft | null | undefined): ReportWriterChartDefinition | null {
  if (!draft?.enabled || !draft.categoryField.trim()) return null;
  const valueColumns = draft.valueColumns
    .split(",")
    .map((v) => v.trim())
    .filter((v) => v.length > 0);
  if (valueColumns.length === 0) return null;
  return { type: draft.type, categoryField: draft.categoryField.trim(), valueColumns };
}

function buildReportWriterPreviewRows(
  grid: ReportWriterGridDefinition,
  profile: ReportWriterPreviewDatasetProfile
): Record<string, string>[] {
  const dimensionFields = normalizeStringList([
    ...(grid.rowFields ?? []),
    ...(grid.columnFields ?? [])
  ]);
  const metricSourceFields = normalizeStringList((grid.metrics ?? []).map((metric) => metric.sourceField));
  const formulaFields = normalizeStringList((grid.formulas ?? []).flatMap((formula) => extractReportWriterFormulaFields(formula.expression)))
    .filter((field) => grid.kind !== "Contribution" || !isGeneratedContributionField(field));
  const numericFields = normalizeStringList([
    ...metricSourceFields,
    ...formulaFields,
    ...(grid.sortBy ? [grid.sortBy] : [])
  ]).filter((field) =>
    !dimensionFields.some((dimension) => dimension.toLowerCase() === field.toLowerCase())
    && (grid.kind !== "Contribution" || !isGeneratedContributionField(field)));
  const fields = normalizeStringList([...dimensionFields, ...numericFields]);
  const filters = grid.filters ?? [];
  const filterFields = normalizeStringList(filters.map((filter) => filter.field));

  if (fields.length === 0 && filterFields.length === 0) {
    return [{ previewDataset: profile, previewRow: "1" }, { previewDataset: profile, previewRow: "2" }];
  }

  return Array.from({ length: 4 }, (_, index) => {
    const row: Record<string, string> = { previewDataset: profile };
    for (const field of dimensionFields) {
      row[field] = previewDimensionValue(field, index, profile);
    }

    for (const field of numericFields) {
      row[field] = grid.kind === "Contribution" && isPnlLikeField(field)
        ? previewContributionPnlValue(index, profile)
        : previewNumericValue(field, index, profile);
    }

    for (const filter of filters) {
      if (!filter.field) {
        continue;
      }

      row[filter.field] = previewFilterValue(filter, index, profile);
    }

    return row;
  });
}

function buildReportAccessPolicy(settings: ReportWriterDraftSettings): ReportTemplateDraftRequest["accessPolicy"] {
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

function buildWriterFilters(settings: ReportWriterDraftSettings): ReportWriterFilterDefinition[] | null {
  const field = normalizeDraftText(settings.filterField, "");
  if (!field) {
    return null;
  }

  const operator = normalizeReportWriterFilterOperator(settings.filterOperator);
  const value = isBlankFilterOperator(operator)
    ? null
    : normalizeDraftText(settings.filterValue, "");
  if (!isBlankFilterOperator(operator) && !value) {
    return null;
  }

  return [
    {
      field,
      operator,
      value,
      label: isBlankFilterOperator(operator)
        ? `${field} ${formatReportWriterFilterOperator(operator)}`
        : `${field} ${formatReportWriterFilterOperator(operator)} ${value}`
    }
  ];
}

function normalizeWriterMetrics(
  tokens: ReportingWriterToken[],
  gridKind: ReportWriterGridKind | null = null
): ReportWriterMetricDefinition[] {
  const metrics = tokens
    .map(tokenToMetricDefinition)
    .filter((metric): metric is ReportWriterMetricDefinition => Boolean(metric));
  const deduped = dedupeBy(metrics, (metric) => metric.name.toLowerCase());
  return gridKind === "Contribution" ? preferContributionMetric(deduped) : deduped;
}

function tokenToMetricDefinition(token: ReportingWriterToken): ReportWriterMetricDefinition | null {
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

function preferContributionMetric(metrics: ReportWriterMetricDefinition[]): ReportWriterMetricDefinition[] {
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

function normalizeWriterFormulas(tokens: ReportingWriterToken[]) {
  const formulas = tokens
    .map(tokenToFormulaDefinition)
    .filter((formula): formula is NonNullable<ReturnType<typeof tokenToFormulaDefinition>> => Boolean(formula));
  return dedupeBy(formulas, (formula) => formula.name.toLowerCase());
}

function tokenToFormulaDefinition(token: ReportingWriterToken) {
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

function resolveWriterFieldName(token: ReportingWriterToken): string {
  return normalizeDraftText(token.fieldName ?? token.sourceField ?? token.name ?? token.label, "");
}

function extractReportWriterFormulaFields(expression: string | null | undefined): string[] {
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

      fields.push(identifier);
      position = identifierStart + Math.max(identifier.length, 1);
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

function readReportWriterFunctionFieldArgument(
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

function readReportWriterIdentifier(expression: string, start: number): string {
  let position = start;
  while (position < expression.length && isReportWriterIdentifierPart(expression[position])) {
    position += 1;
  }

  return expression.slice(start, position);
}

function skipReportWriterWhitespace(expression: string, position: number): number {
  while (position < expression.length && /\s/.test(expression[position])) {
    position += 1;
  }

  return position;
}

function isReportWriterIdentifierStart(value: string | undefined): boolean {
  return Boolean(value && /[A-Za-z_]/.test(value));
}

function isReportWriterIdentifierPart(value: string | undefined): boolean {
  return Boolean(value && /[A-Za-z0-9_.-]/.test(value));
}

function isReportWriterFormulaFunction(identifier: string): boolean {
  return ["abs", "min", "max", "safedivide", "percent", "basispoints", "round"].includes(identifier.toLowerCase());
}

function isGeneratedContributionField(field: string | null | undefined): boolean {
  const normalized = normalizeIdentifierToken(field ?? "", "").toLowerCase();
  return normalized === "contributionpercent" || normalized === "contributionabspercent";
}

function isPnlLikeField(field: string | null | undefined): boolean {
  const normalized = (field ?? "").toLowerCase().replace(/[^a-z0-9]+/g, "");
  return normalized.includes("pnl") || normalized.includes("profitloss");
}

function previewDimensionValue(field: string, index: number, profile: ReportWriterPreviewDatasetProfile): string {
  const normalized = field.toLowerCase();
  if (profile === "ledgerFacts") {
    if (normalized.includes("sector")) {
      return ["Operating expense", "Capital activity", "Financing", "Revenue"][index] ?? "Ledger";
    }

    if (normalized.includes("strategy")) {
      return ["Close accrual", "Investor activity", "Cash financing", "Management fee"][index] ?? "Ledger";
    }

    if (normalized.includes("fund")) {
      return ["Fund Alpha", "Fund Alpha", "Fund Beta", "Fund Beta"][index] ?? "Fund Alpha";
    }

    if (normalized.includes("security") || normalized.includes("asset")) {
      return ["GL-6000", "GL-3100", "GL-2100", "GL-4100"][index] ?? "Ledger line";
    }
  }

  if (profile === "cashLadder") {
    if (normalized.includes("sector")) {
      return ["Cash", "Settlement", "Financing", "Reserve"][index] ?? "Cash";
    }

    if (normalized.includes("strategy")) {
      return ["T+0 liquidity", "T+1 settlement", "Credit facility", "Operating reserve"][index] ?? "Cash ladder";
    }

    if (normalized.includes("fund")) {
      return ["Fund Alpha", "Fund Alpha", "Fund Alpha", "Fund Beta"][index] ?? "Fund Alpha";
    }

    if (normalized.includes("security") || normalized.includes("asset")) {
      return ["USD sweep", "Broker receivable", "Credit draw", "Reserve cash"][index] ?? "Cash bucket";
    }
  }

  if (normalized.includes("sector")) {
    return ["Technology", "Technology", "Rates", "Credit"][index] ?? "Other";
  }

  if (normalized.includes("strategy")) {
    return ["Core", "Growth", "Rates", "Credit"][index] ?? "Core";
  }

  if (normalized.includes("fund")) {
    return ["Fund A", "Fund A", "Fund B", "Fund B"][index] ?? "Fund A";
  }

  if (normalized.includes("region")) {
    return ["North America", "Europe", "Asia Pacific", "North America"][index] ?? "North America";
  }

  if (normalized.includes("security") || normalized.includes("asset")) {
    return ["ABC Corp", "XYZ Fund", "UST 10Y", "Cash USD"][index] ?? "Position";
  }

  return `${formatPreviewFieldLabel(field)} ${(index % 2) + 1}`;
}

function previewNumericValue(field: string, index: number, profile: ReportWriterPreviewDatasetProfile): string {
  const normalized = field.toLowerCase();
  if (profile === "ledgerFacts") {
    if (normalized.includes("pnl") || normalized.includes("p&l")) {
      return ["25", "-7", "4", "12"][index] ?? "0";
    }

    if (normalized.includes("cash") || normalized.includes("liquidity")) {
      return ["350", "150", "500", "225"][index] ?? "0";
    }

    if (normalized.includes("nav") || normalized.includes("value") || normalized.includes("exposure")) {
      return ["250", "125", "80", "60"][index] ?? "0";
    }
  }

  if (profile === "cashLadder") {
    if (normalized.includes("pnl") || normalized.includes("p&l")) {
      return ["1", "0", "-1", "0"][index] ?? "0";
    }

    if (normalized.includes("cash") || normalized.includes("liquidity")) {
      return ["1250", "900", "650", "300"][index] ?? "0";
    }

    if (normalized.includes("nav") || normalized.includes("value") || normalized.includes("exposure")) {
      return ["1200", "875", "600", "275"][index] ?? "0";
    }
  }

  if (normalized.includes("pnl") || normalized.includes("p&l")) {
    return ["10", "5", "-2", "4"][index] ?? "0";
  }

  if (normalized.includes("cash") || normalized.includes("liquidity")) {
    return ["1000", "750", "400", "250"][index] ?? "0";
  }

  if (normalized.includes("nav") || normalized.includes("value") || normalized.includes("exposure")) {
    return ["100", "50", "75", "25"][index] ?? "0";
  }

  if (normalized.includes("percent") || normalized.includes("pct")) {
    return ["12.5", "8.25", "-3.5", "6"][index] ?? "0";
  }

  return String((index + 1) * 10);
}

function previewContributionPnlValue(index: number, profile: ReportWriterPreviewDatasetProfile): string {
  if (profile === "ledgerFacts") {
    return ["150", "-50", "0", "25"][index] ?? "0";
  }

  if (profile === "cashLadder") {
    return ["12", "-4", "0", "2"][index] ?? "0";
  }

  return ["150", "-50", "0", "25"][index] ?? "0";
}

function previewFilterValue(
  filter: ReportWriterFilterDefinition,
  index: number,
  profile: ReportWriterPreviewDatasetProfile
): string {
  const operator = normalizeReportWriterFilterOperator(filter.operator);
  const value = filter.value ?? "";
  if (operator === "IsBlank") {
    return index === 0 ? "" : previewDimensionValue(filter.field, index, profile);
  }

  if (operator === "IsNotBlank") {
    return index === 0 ? previewDimensionValue(filter.field, index, profile) : "";
  }

  if (["GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual"].includes(operator)) {
    const numeric = Number.parseFloat(value);
    if (Number.isFinite(numeric)) {
      return index < 2 ? String(numeric + 10 + index) : String(numeric - 10 - index);
    }
  }

  if (operator === "Contains") {
    return index < 2 ? `Preview ${value} ${index + 1}` : `Other ${index + 1}`;
  }

  if (operator === "StartsWith") {
    return index < 2 ? `${value}${index + 1}` : `Other ${index + 1}`;
  }

  if (operator === "EndsWith") {
    return index < 2 ? `Preview ${index + 1}${value}` : `Other ${index + 1}`;
  }

  if (operator === "NotEquals") {
    return index < 2 ? `${value}-alternate-${index + 1}` : value;
  }

  return index < 2 ? value : previewDimensionValue(filter.field, index, profile);
}

function formatPreviewFieldLabel(field: string): string {
  const spaced = field
    .replace(/[_-]+/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .trim();
  if (!spaced) {
    return "Value";
  }

  return spaced.replace(/\b\w/g, (character) => character.toUpperCase());
}

function normalizeAggregateFunction(value: ReportWriterAggregateFunction | string | null | undefined): ReportWriterAggregateFunction {
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

function normalizeStringList(values: string[]): string[] {
  return Array.from(new Set(values.map((value) => value.trim()).filter(Boolean)));
}

function normalizeDraftText(value: string | null | undefined, fallback: string): string {
  const normalized = value?.trim();
  return normalized || fallback;
}

function normalizeIdentifierToken(value: string | null | undefined, fallback: string): string {
  const normalized = normalizeDraftText(value, fallback)
    .replace(/[^A-Za-z0-9_.-]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return normalized || fallback;
}

function parseReportTemplateVersion(version: string): number | null {
  const first = version.split(".", 1)[0];
  const parsed = Number.parseInt(first, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

function dedupeBy<T>(items: T[], keySelector: (item: T) => string): T[] {
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
