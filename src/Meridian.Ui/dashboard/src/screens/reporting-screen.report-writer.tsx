import { type DragEvent, type ReactNode, useState } from "react";
import { BarChart2, ChevronLeft, ChevronRight, Eye, Filter, GripVertical, Palette, PencilLine, Plus, RotateCcw, Trash2, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { ReportWriterChartPreview } from "@/components/meridian/report-writer-chart-preview";
import { ReportWriterGridDiffView } from "@/components/meridian/report-writer-grid-diff-view";
import { buildReportWriterGridDiff } from "@/lib/report-writer-grid-diff";
import { cellStyleClassName, resolveCellStyle } from "@/lib/report-writer-grid-format";
import { createReportTemplateDraft, renderReportTemplate } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { cn } from "@/lib/utils";
import type { ReportingWriterGridRow, ReportingWriterToken } from "@/screens/reporting-screen.view-model";
import type { ReportingCommandStatus } from "@/screens/reporting-screen.shared-components";
import type {
  ReportAccessMode,
  ReportAccessPrincipalKind,
  ReportWriterCellStyle,
  ReportWriterChartType,
  ReportWriterDatasetSource,
  ReportWriterFilterOperator,
  ReportWriterGridKind,
  ReportWriterGridRender,
  ReportTemplateDraftRequest,
  RenderReportTemplateRequest
} from "@/types";

export type ReportWriterDropZone = "rowFields" | "columnFields" | "metrics" | "formulas";
export type ReportWriterDraftState = Partial<Record<ReportWriterDropZone, ReportingWriterToken[]>>;
export type ReportWriterTokenDragOrigin = { gridId: string; zone: ReportWriterDropZone };
export type ReportWriterTokenDragPayload = { token: ReportingWriterToken; origin?: ReportWriterTokenDragOrigin | null };
export type ReportWriterFixtureDatasetProfile = "portfolioPositions" | "ledgerFacts" | "cashLadder" | "customRows";
export type ReportWriterPreviewDatasetProfile = ReportWriterFixtureDatasetProfile | `source:${string}`;
export type ReportWriterDraftSettingsField =
  | "name"
  | "displayName"
  | "gridKind"
  | "topN"
  | "previewDataset"
  | "accessMode"
  | "principalKind"
  | "principalId"
  | "filterField"
  | "filterOperator"
  | "filterValue";
export type ReportWriterCustomFormulaField = "name" | "label" | "expression";
export type ReportWriterChartDraftField = "type" | "categoryField" | "valueColumns";

export interface ReportWriterChartDraft {
  enabled: boolean;
  type: ReportWriterChartType;
  categoryField: string;
  valueColumns: string;
}

export interface ReportWriterFormatRuleDraft {
  id: string;
  column: string;
  operator: ReportWriterFilterOperator;
  value: string;
  style: ReportWriterCellStyle;
}

export interface ReportWriterDraftSettings {
  name: string;
  displayName: string;
  gridKind: ReportWriterGridKind;
  topN: string;
  previewDataset: ReportWriterPreviewDatasetProfile;
  accessMode: ReportAccessMode;
  principalKind: ReportAccessPrincipalKind;
  principalId: string;
  filterField: string;
  filterOperator: ReportWriterFilterOperator;
  filterValue: string;
}

export interface ReportWriterCustomFormulaDraft {
  name: string;
  label: string;
  expression: string;
}

export const reportWriterPreviewDatasetProfiles: { id: ReportWriterPreviewDatasetProfile; label: string }[] = [
  { id: "portfolioPositions", label: "Portfolio positions" },
  { id: "ledgerFacts", label: "Ledger facts" },
  { id: "cashLadder", label: "Cash ladder" },
  { id: "customRows", label: "Custom pasted rows" }
];

export function normalizeReportWriterPreviewDatasetProfile(value: string | null | undefined): ReportWriterPreviewDatasetProfile {
  return reportWriterPreviewDatasetProfiles.some((profile) => profile.id === value)
    || isReportWriterDatasetSourceProfile(value)
    ? value as ReportWriterPreviewDatasetProfile
    : "portfolioPositions";
}

function buildReportWriterPreviewDatasetProfiles(
  datasetSources: ReportWriterDatasetSource[]
): { id: ReportWriterPreviewDatasetProfile; label: string }[] {
  return [
    ...datasetSources.map((source) => ({
      id: buildReportWriterDatasetSourceProfile(source.sourceId),
      label: `${source.label} (${source.rowCount.toLocaleString()})`
    })),
    ...reportWriterPreviewDatasetProfiles
  ];
}

export function buildRetainedReportWriterDatasetRows(
  datasetSource: ReportWriterDatasetSource | null
): Record<string, string>[] | null {
  if (!datasetSource || datasetSource.rows.length === 0) {
    return null;
  }

  return datasetSource.rows.map((row) => ({ ...row }));
}

export function resolveReportWriterDatasetSource(
  datasetSources: ReportWriterDatasetSource[],
  previewDataset: ReportWriterPreviewDatasetProfile
): ReportWriterDatasetSource | null {
  if (!isReportWriterDatasetSourceProfile(previewDataset)) {
    return null;
  }

  const sourceId = previewDataset.slice("source:".length);
  return datasetSources.find((source) => source.sourceId === sourceId) ?? null;
}

type ReportWriterZones = Record<ReportWriterDropZone, ReportingWriterToken[]>;

interface UseReportingReportWriterOptions {
  datasetSources: ReportWriterDatasetSource[];
  buildDraftRequest: (
    grid: ReportingWriterGridRow,
    settings: ReportWriterDraftSettings,
    zones: ReportWriterZones
  ) => ReportTemplateDraftRequest;
  buildRenderRequest: (
    grid: ReportingWriterGridRow,
    zones: ReportWriterZones,
    settings: ReportWriterDraftSettings,
    customDatasetRows?: Record<string, string>[] | null,
    chartDraft?: ReportWriterChartDraft | null,
    formatRules?: ReportWriterFormatRuleDraft[] | null
  ) => RenderReportTemplateRequest;
}

export function useReportingReportWriter({
  datasetSources,
  buildDraftRequest,
  buildRenderRequest
}: UseReportingReportWriterOptions) {
  const [writerDrafts, setWriterDrafts] = useState<Record<string, ReportWriterDraftState>>({});
  const [writerDraftSettings, setWriterDraftSettings] = useState<Record<string, Partial<ReportWriterDraftSettings>>>({});
  const [writerCustomFormulas, setWriterCustomFormulas] = useState<Record<string, Partial<ReportWriterCustomFormulaDraft>>>({});
  const [writerCustomDatasetText, setWriterCustomDatasetText] = useState<Record<string, string>>({});
  const [writerDraftStatus, setWriterDraftStatus] = useState<ReportingCommandStatus | null>(null);
  const [writerPreviewStatus, setWriterPreviewStatus] = useState<ReportingCommandStatus | null>(null);
  const [writerPreviewByGridId, setWriterPreviewByGridId] = useState<Record<string, ReportWriterGridRender | null>>({});
  const [writerPreviousPreviewByGridId, setWriterPreviousPreviewByGridId] = useState<Record<string, ReportWriterGridRender | null>>({});
  const [writerChartDrafts, setWriterChartDrafts] = useState<Record<string, ReportWriterChartDraft>>({});
  const [writerFormatRuleDrafts, setWriterFormatRuleDrafts] = useState<Record<string, ReportWriterFormatRuleDraft[]>>({});
  const savingWriterDraftId = writerDraftStatus?.state === "running" ? writerDraftStatus.id : null;
  const previewingWriterDraftId = writerPreviewStatus?.state === "running" ? writerPreviewStatus.id : null;

  function getWriterZoneTokens(grid: ReportingWriterGridRow, zone: ReportWriterDropZone): ReportingWriterToken[] {
    return writerDrafts[grid.id]?.[zone] ?? grid[zone];
  }

  function getWriterDraftSettings(grid: ReportingWriterGridRow): ReportWriterDraftSettings {
    return {
      ...buildDefaultWriterDraftSettings(grid),
      ...writerDraftSettings[grid.id]
    };
  }

  function getWriterCustomFormula(grid: ReportingWriterGridRow): ReportWriterCustomFormulaDraft {
    return {
      ...buildDefaultWriterCustomFormula(grid),
      ...writerCustomFormulas[grid.id]
    };
  }

  function getWriterChartDraft(grid: ReportingWriterGridRow): ReportWriterChartDraft {
    return writerChartDrafts[grid.id] ?? { enabled: false, type: "Bar", categoryField: "", valueColumns: "" };
  }

  function getWriterFormatRules(grid: ReportingWriterGridRow): ReportWriterFormatRuleDraft[] {
    return writerFormatRuleDrafts[grid.id] ?? [];
  }

  function updateWriterDraftSetting(
    grid: ReportingWriterGridRow,
    field: ReportWriterDraftSettingsField,
    value: string
  ) {
    setWriterDraftSettings((current) => {
      const normalizedValue = normalizeWriterDraftSettingValue(field, value);
      const nextSettings = {
        ...current[grid.id],
        [field]: normalizedValue
      };

      if (field === "accessMode" && normalizedValue === "Private") {
        nextSettings.principalKind = "User";
      }

      if (field === "gridKind" && normalizedValue === "TopN" && !nextSettings.topN) {
        nextSettings.topN = "10";
      }

      return {
        ...current,
        [grid.id]: nextSettings
      };
    });
    setWriterPreviewByGridId((current) => clearWriterPreview(current, grid.id));
  }

  function updateWriterCustomFormula(
    grid: ReportingWriterGridRow,
    field: ReportWriterCustomFormulaField,
    value: string
  ) {
    setWriterCustomFormulas((current) => ({
      ...current,
      [grid.id]: {
        ...current[grid.id],
        [field]: value
      }
    }));
    setWriterPreviewByGridId((current) => clearWriterPreview(current, grid.id));
  }

  function updateWriterCustomDataset(grid: ReportingWriterGridRow, value: string) {
    setWriterCustomDatasetText((current) => ({
      ...current,
      [grid.id]: value
    }));
    setWriterPreviewByGridId((current) => clearWriterPreview(current, grid.id));
  }

  function updateWriterChartDraft(grid: ReportingWriterGridRow, field: ReportWriterChartDraftField | "enabled", value: string) {
    setWriterChartDrafts((current) => {
      const existing = current[grid.id] ?? { enabled: false, type: "Bar" as ReportWriterChartType, categoryField: "", valueColumns: "" };
      return {
        ...current,
        [grid.id]: field === "enabled"
          ? { ...existing, enabled: value === "true" }
          : { ...existing, [field]: value }
      };
    });
    setWriterPreviewByGridId((current) => clearWriterPreview(current, grid.id));
  }

  function addWriterFormatRule(grid: ReportingWriterGridRow) {
    const id = `rule-${Date.now()}`;
    setWriterFormatRuleDrafts((current) => ({
      ...current,
      [grid.id]: [...(current[grid.id] ?? []), { id, column: "", operator: "GreaterThan" as ReportWriterFilterOperator, value: "", style: "Warning" as ReportWriterCellStyle }]
    }));
    setWriterPreviewByGridId((current) => clearWriterPreview(current, grid.id));
  }

  function removeWriterFormatRule(grid: ReportingWriterGridRow, ruleId: string) {
    setWriterFormatRuleDrafts((current) => ({
      ...current,
      [grid.id]: (current[grid.id] ?? []).filter((rule) => rule.id !== ruleId)
    }));
    setWriterPreviewByGridId((current) => clearWriterPreview(current, grid.id));
  }

  function updateWriterFormatRule(grid: ReportingWriterGridRow, ruleId: string, field: keyof Omit<ReportWriterFormatRuleDraft, "id">, value: string) {
    setWriterFormatRuleDrafts((current) => ({
      ...current,
      [grid.id]: (current[grid.id] ?? []).map((rule) =>
        rule.id === ruleId ? { ...rule, [field]: value } : rule
      )
    }));
    setWriterPreviewByGridId((current) => clearWriterPreview(current, grid.id));
  }

  function handleWriterTokenDragStart(
    event: DragEvent<HTMLElement>,
    token: ReportingWriterToken,
    origin: ReportWriterTokenDragOrigin | null = null
  ) {
    event.dataTransfer.effectAllowed = origin ? "move" : "copy";
    event.dataTransfer.setData("application/x-meridian-report-writer-token", JSON.stringify({ token, origin }));
  }

  function handleWriterZoneDrop(event: DragEvent<HTMLElement>, grid: ReportingWriterGridRow, zone: ReportWriterDropZone) {
    event.preventDefault();
    const payload = event.dataTransfer.getData("application/x-meridian-report-writer-token");
    if (!payload) {
      return;
    }

    let dragPayload: ReportWriterTokenDragPayload;
    try {
      dragPayload = parseReportWriterTokenDragPayload(JSON.parse(payload));
    } catch {
      return;
    }

    setWriterDrafts((current) => {
      const { token, origin } = dragPayload;
      const existing = current[grid.id]?.[zone] ?? grid[zone];
      if (origin?.gridId === grid.id && origin.zone === zone) {
        return current;
      }

      if (existing.some((item) => item.id === token.id) && origin?.gridId !== grid.id) {
        return current;
      }

      const nextGridDraft: ReportWriterDraftState = { ...current[grid.id] };
      if (origin?.gridId === grid.id) {
        const originTokens = current[grid.id]?.[origin.zone] ?? grid[origin.zone];
        nextGridDraft[origin.zone] = originTokens.filter((item) => item.id !== token.id);
      }

      if (!existing.some((item) => item.id === token.id)) {
        nextGridDraft[zone] = [...existing, token];
      }

      return {
        ...current,
        [grid.id]: nextGridDraft
      };
    });
    setWriterPreviewByGridId((current) => clearWriterPreview(current, grid.id));
  }

  function removeWriterZoneToken(grid: ReportingWriterGridRow, zone: ReportWriterDropZone, tokenId: string) {
    setWriterDrafts((current) => {
      const existing = current[grid.id]?.[zone] ?? grid[zone];
      if (!existing.some((item) => item.id === tokenId)) {
        return current;
      }

      return {
        ...current,
        [grid.id]: {
          ...current[grid.id],
          [zone]: existing.filter((item) => item.id !== tokenId)
        }
      };
    });
    setWriterPreviewByGridId((current) => clearWriterPreview(current, grid.id));
  }

  function moveWriterZoneToken(
    grid: ReportingWriterGridRow,
    zone: ReportWriterDropZone,
    tokenId: string,
    direction: -1 | 1
  ) {
    setWriterDrafts((current) => {
      const existing = current[grid.id]?.[zone] ?? grid[zone];
      const tokenIndex = existing.findIndex((item) => item.id === tokenId);
      const nextIndex = tokenIndex + direction;
      if (tokenIndex < 0 || nextIndex < 0 || nextIndex >= existing.length) {
        return current;
      }

      const nextTokens = [...existing];
      const [token] = nextTokens.splice(tokenIndex, 1);
      nextTokens.splice(nextIndex, 0, token);

      return {
        ...current,
        [grid.id]: {
          ...current[grid.id],
          [zone]: nextTokens
        }
      };
    });
    setWriterPreviewByGridId((current) => clearWriterPreview(current, grid.id));
  }

  function resetWriterGrid(grid: ReportingWriterGridRow) {
    setWriterDrafts((current) => removeRecordEntry(current, grid.id));
    setWriterPreviewByGridId((current) => clearWriterPreview(current, grid.id));
    setWriterCustomFormulas((current) => removeRecordEntry(current, grid.id));
    setWriterDraftSettings((current) => removeRecordEntry(current, grid.id));
    setWriterCustomDatasetText((current) => removeRecordEntry(current, grid.id));
  }

  function getWriterCurrentZones(grid: ReportingWriterGridRow): ReportWriterZones {
    return {
      rowFields: getWriterZoneTokens(grid, "rowFields"),
      columnFields: getWriterZoneTokens(grid, "columnFields"),
      metrics: getWriterZoneTokens(grid, "metrics"),
      formulas: appendCustomFormulaToken(getWriterZoneTokens(grid, "formulas"), grid, getWriterCustomFormula(grid))
    };
  }

  async function saveWriterGridDraft(grid: ReportingWriterGridRow) {
    if (savingWriterDraftId) {
      return;
    }

    const settings = getWriterDraftSettings(grid);
    const request = buildDraftRequest(grid, settings, getWriterCurrentZones(grid));

    setWriterDraftStatus({
      id: grid.id,
      label: "Save report-writer draft",
      state: "running",
      message: `${settings.displayName} is saving.`,
      details: []
    });

    try {
      const result = await createReportTemplateDraft(request);
      setWriterDraftStatus({
        id: grid.id,
        label: "Save report-writer draft",
        state: "success",
        message: `${result.definition.displayName} draft saved.`,
        details: [
          `Template: ${result.definition.templateId.name}@v${result.definition.templateId.version}`,
          `Status: ${result.status}`,
          result.validationIssues.length > 0
            ? `Validation: ${result.validationIssues.join("; ")}`
            : "Validation: ready"
        ]
      });
    } catch (error) {
      const display = describeApiError(error, `${settings.displayName} draft failed.`);
      setWriterDraftStatus({
        id: grid.id,
        label: "Save report-writer draft",
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
  }

  async function previewWriterGrid(grid: ReportingWriterGridRow) {
    if (previewingWriterDraftId) {
      return;
    }

    const settings = getWriterDraftSettings(grid);
    const customDataset = settings.previewDataset === "customRows"
      ? parseReportWriterCustomDatasetRows(writerCustomDatasetText[grid.id] ?? "")
      : null;
    if (customDataset?.error) {
      setWriterPreviewStatus({
        id: grid.id,
        label: "Preview report-writer grid",
        state: "error",
        message: `${grid.title} custom dataset is invalid.`,
        details: [customDataset.error]
      });
      return;
    }

    const selectedDatasetSource = resolveReportWriterDatasetSource(datasetSources, settings.previewDataset);
    const retainedDatasetRows = buildRetainedReportWriterDatasetRows(selectedDatasetSource);
    const request = buildRenderRequest(
      grid,
      getWriterCurrentZones(grid),
      settings,
      customDataset?.rows ?? retainedDatasetRows,
      getWriterChartDraft(grid),
      getWriterFormatRules(grid));
    setWriterPreviewStatus({
      id: grid.id,
      label: "Preview report-writer grid",
      state: "running",
      message: `${grid.title} preview is rendering.`,
      details: []
    });

    try {
      const result = await renderReportTemplate(request);
      const renderedGrid = result.grids?.find((item) => item.gridId === grid.gridId) ?? result.grids?.[0] ?? null;
      const previousPreview = writerPreviewByGridId[grid.id] ?? null;
      setWriterPreviewByGridId((current) => ({
        ...current,
        [grid.id]: renderedGrid
      }));
      setWriterPreviousPreviewByGridId((current) => ({
        ...current,
        [grid.id]: previousPreview
      }));
      setWriterPreviewStatus({
        id: grid.id,
        label: "Preview report-writer grid",
        state: "success",
        message: `${grid.title} preview rendered.`,
        details: [
          `Template: ${result.templateId.name}@v${result.templateId.version}`,
          `Rows: ${renderedGrid?.rows.length ?? 0}`,
          customDataset
            ? `Dataset rows: ${customDataset.rows.length}`
            : retainedDatasetRows
              ? `Dataset rows: ${retainedDatasetRows.length}`
              : `Dataset profile: ${settings.previewDataset}`,
          result.missingRequiredParameters.length > 0
            ? `Missing parameters: ${result.missingRequiredParameters.join(", ")}`
            : "Required parameters: satisfied",
          ...(result.warnings ?? [])
        ]
      });
    } catch (error) {
      const display = describeApiError(error, `${grid.title} preview failed.`);
      setWriterPreviewStatus({
        id: grid.id,
        label: "Preview report-writer grid",
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
  }

  return {
    writerDraftStatus,
    writerPreviewStatus,
    savingWriterDraftId,
    previewingWriterDraftId,
    writerPreviewByGridId,
    writerPreviousPreviewByGridId,
    writerCustomDatasetText,
    getWriterZoneTokens,
    getWriterDraftSettings,
    getWriterCustomFormula,
    getWriterChartDraft,
    getWriterFormatRules,
    updateWriterDraftSetting,
    updateWriterCustomFormula,
    updateWriterCustomDataset,
    updateWriterChartDraft,
    addWriterFormatRule,
    removeWriterFormatRule,
    updateWriterFormatRule,
    handleWriterTokenDragStart,
    handleWriterZoneDrop,
    removeWriterZoneToken,
    moveWriterZoneToken,
    resetWriterGrid,
    saveWriterGridDraft,
    previewWriterGrid
  };
}

function buildReportWriterDatasetSourceProfile(sourceId: string): `source:${string}` {
  return `source:${sourceId}`;
}

function isReportWriterDatasetSourceProfile(value: string | null | undefined): value is `source:${string}` {
  return typeof value === "string" && value.startsWith("source:") && value.length > "source:".length;
}

function clearWriterPreview(
  current: Record<string, ReportWriterGridRender | null>,
  gridId: string
): Record<string, ReportWriterGridRender | null> {
  if (!(gridId in current)) {
    return current;
  }

  const next = { ...current };
  delete next[gridId];
  return next;
}

function removeRecordEntry<T>(current: Record<string, T>, key: string): Record<string, T> {
  if (!(key in current)) {
    return current;
  }

  const next = { ...current };
  delete next[key];
  return next;
}

function parseReportWriterTokenDragPayload(value: unknown): ReportWriterTokenDragPayload {
  if (value && typeof value === "object" && "token" in value) {
    const payload = value as ReportWriterTokenDragPayload;
    return {
      token: payload.token,
      origin: payload.origin && isReportWriterDropZone(payload.origin.zone)
        ? { gridId: payload.origin.gridId, zone: payload.origin.zone }
        : null
    };
  }

  return { token: value as ReportingWriterToken, origin: null };
}

function isReportWriterDropZone(value: string): value is ReportWriterDropZone {
  return value === "rowFields" || value === "columnFields" || value === "metrics" || value === "formulas";
}

function normalizeWriterDraftSettingValue(field: ReportWriterDraftSettingsField, value: string): string {
  if (field === "previewDataset") {
    return normalizeReportWriterPreviewDatasetProfile(value);
  }

  if (field === "gridKind") {
    return normalizeReportWriterGridKind(value);
  }

  if (field === "topN") {
    return normalizeReportWriterTopNText(value);
  }

  if (field === "accessMode") {
    return normalizeReportAccessMode(value);
  }

  if (field === "principalKind") {
    return normalizeReportAccessPrincipalKind(value);
  }

  return value;
}

function normalizeReportAccessMode(value: string): ReportAccessMode {
  return value === "Restricted" || value === "Private" ? value : "CompanyWide";
}

function normalizeReportAccessPrincipalKind(value: string): ReportAccessPrincipalKind {
  return value === "User" || value === "Company" ? value : "Group";
}

function buildDefaultWriterDraftSettings(grid: ReportingWriterGridRow): ReportWriterDraftSettings {
  const firstFilter = grid.filters[0] ?? null;
  return {
    name: grid.templateId,
    displayName: `${grid.title} Draft`,
    gridKind: normalizeReportWriterGridKind(grid.kind),
    topN: normalizeReportWriterTopNText(grid.topN?.toString() ?? "10"),
    previewDataset: "portfolioPositions",
    accessMode: "CompanyWide",
    principalKind: "Group",
    principalId: "reporting-ops",
    filterField: firstFilter?.field ?? "",
    filterOperator: normalizeReportWriterFilterOperator(firstFilter?.operator),
    filterValue: firstFilter?.value ?? ""
  };
}

function buildDefaultWriterCustomFormula(grid: ReportingWriterGridRow): ReportWriterCustomFormulaDraft {
  return {
    name: `${normalizeReportWriterIdentifierToken(grid.gridId, "grid")}CustomFormula`,
    label: "Custom formula",
    expression: ""
  };
}

function appendCustomFormulaToken(
  tokens: ReportingWriterToken[],
  grid: ReportingWriterGridRow,
  customFormula: ReportWriterCustomFormulaDraft
): ReportingWriterToken[] {
  const token = buildCustomFormulaToken(grid, customFormula);
  return token ? [...tokens, token] : tokens;
}

function buildCustomFormulaToken(
  grid: ReportingWriterGridRow,
  customFormula: ReportWriterCustomFormulaDraft
): ReportingWriterToken | null {
  const expression = normalizeReportWriterDraftText(customFormula.expression, "");
  if (!expression) {
    return null;
  }

  const fallbackName = `${normalizeReportWriterIdentifierToken(grid.gridId, "grid")}CustomFormula`;
  const name = normalizeReportWriterIdentifierToken(customFormula.name, fallbackName);
  const label = normalizeReportWriterDraftText(customFormula.label, name);
  return {
    id: `formula:${grid.id}:custom:${name}`,
    label,
    detail: expression,
    kind: "formula",
    name,
    expression
  };
}

function parseReportWriterCustomDatasetRows(text: string): { rows: Record<string, string>[]; error: null } | { rows: []; error: string } {
  const trimmed = text.trim();
  if (!trimmed) {
    return { rows: [], error: "Custom dataset requires JSON rows or CSV rows with a header." };
  }

  const parsed = trimmed.startsWith("[") ? parseReportWriterJsonDatasetRows(trimmed) : parseReportWriterCsvDatasetRows(trimmed);
  if (parsed.error) {
    return parsed;
  }

  if (parsed.rows.length === 0) {
    return { rows: [], error: "Custom dataset requires at least one row." };
  }

  return { rows: parsed.rows.slice(0, 100), error: null };
}

function parseReportWriterJsonDatasetRows(text: string): { rows: Record<string, string>[]; error: null } | { rows: []; error: string } {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return { rows: [], error: "Custom JSON dataset must be an array of row objects." };
  }

  if (!Array.isArray(parsed)) {
    return { rows: [], error: "Custom JSON dataset must be an array of row objects." };
  }

  const rows: Record<string, string>[] = [];
  for (const item of parsed) {
    if (!item || typeof item !== "object" || Array.isArray(item)) {
      return { rows: [], error: "Each custom JSON dataset row must be an object." };
    }

    const row: Record<string, string> = {};
    for (const [key, value] of Object.entries(item)) {
      if (!key.trim()) {
        continue;
      }

      row[key.trim()] = value == null ? "" : String(value);
    }

    if (Object.keys(row).length > 0) {
      rows.push(row);
    }
  }

  return { rows, error: null };
}

function parseReportWriterCsvDatasetRows(text: string): { rows: Record<string, string>[]; error: null } | { rows: []; error: string } {
  const lines = text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
  if (lines.length < 2) {
    return { rows: [], error: "Custom CSV dataset requires a header row and at least one data row." };
  }

  const headers = parseReportWriterCsvLine(lines[0]).map((header) => header.trim());
  if (headers.length === 0 || headers.every((header) => !header)) {
    return { rows: [], error: "Custom CSV dataset requires at least one header." };
  }

  const rows = lines.slice(1).map((line) => {
    const values = parseReportWriterCsvLine(line);
    const row: Record<string, string> = {};
    headers.forEach((header, index) => {
      if (header) {
        row[header] = values[index] ?? "";
      }
    });
    return row;
  }).filter((row) => Object.keys(row).length > 0);

  return { rows, error: null };
}

function parseReportWriterCsvLine(line: string): string[] {
  const values: string[] = [];
  let current = "";
  let inQuotes = false;

  for (let index = 0; index < line.length; index += 1) {
    const character = line[index];
    if (character === '"') {
      if (inQuotes && line[index + 1] === '"') {
        current += '"';
        index += 1;
      } else {
        inQuotes = !inQuotes;
      }
    } else if (character === "," && !inQuotes) {
      values.push(current);
      current = "";
    } else {
      current += character;
    }
  }

  values.push(current);
  return values;
}

function buildReportWriterDatasetFieldCatalog(
  datasetSource: ReportWriterDatasetSource
): ReportWriterDatasetSource["fields"] {
  return [...datasetSource.fields]
    .sort((left, right) => {
      const roleDelta = reportWriterDatasetFieldRoleRank(left.role) - reportWriterDatasetFieldRoleRank(right.role);
      return roleDelta !== 0 ? roleDelta : (left.label || left.name).localeCompare(right.label || right.name);
    })
    .slice(0, 8);
}

function reportWriterDatasetFieldRoleRank(role: string | null | undefined): number {
  const normalized = role?.toLowerCase();
  if (normalized === "dimension") {
    return 0;
  }

  if (normalized === "metric") {
    return 1;
  }

  if (normalized === "generated" || normalized === "formula") {
    return 2;
  }

  return 3;
}

export function ReportingReportWriterSection({ children }: { children: ReactNode }) {
  return (
    <section role="region" aria-label="No-code report writer">
      <Card className="panel-surface">
        <CardHeader>
          <div className="eyebrow-label">Report writer</div>
          <CardTitle>No-code grid designer</CardTitle>
          <CardDescription>Pivot, Top-N, contribution, and formula grids from governed template metadata.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-3 xl:grid-cols-2">
            {children}
          </div>
        </CardContent>
      </Card>
    </section>
  );
}

interface ReportWriterDesignerGridProps {
  grid: ReportingWriterGridRow;
  settings: ReportWriterDraftSettings;
  customFormula: ReportWriterCustomFormulaDraft;
  chartDraft: ReportWriterChartDraft;
  formatRules: ReportWriterFormatRuleDraft[];
  datasetSources: ReportWriterDatasetSource[];
  customDatasetText: string;
  isSaving: boolean;
  isPreviewing: boolean;
  preview: ReportWriterGridRender | null;
  previousPreview: ReportWriterGridRender | null;
  getZoneTokens: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone) => ReportingWriterToken[];
  onTokenDragStart: (event: DragEvent<HTMLElement>, token: ReportingWriterToken, origin?: ReportWriterTokenDragOrigin | null) => void;
  onZoneDrop: (event: DragEvent<HTMLElement>, grid: ReportingWriterGridRow, zone: ReportWriterDropZone) => void;
  onTokenRemove: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone, tokenId: string) => void;
  onTokenMove: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone, tokenId: string, direction: -1 | 1) => void;
  onReset: (grid: ReportingWriterGridRow) => void;
  onSettingsChange: (grid: ReportingWriterGridRow, field: ReportWriterDraftSettingsField, value: string) => void;
  onCustomFormulaChange: (grid: ReportingWriterGridRow, field: ReportWriterCustomFormulaField, value: string) => void;
  onCustomDatasetChange: (grid: ReportingWriterGridRow, value: string) => void;
  onChartDraftChange: (grid: ReportingWriterGridRow, field: ReportWriterChartDraftField | "enabled", value: string) => void;
  onFormatRuleAdd: (grid: ReportingWriterGridRow) => void;
  onFormatRuleRemove: (grid: ReportingWriterGridRow, ruleId: string) => void;
  onFormatRuleChange: (grid: ReportingWriterGridRow, ruleId: string, field: keyof Omit<ReportWriterFormatRuleDraft, "id">, value: string) => void;
  onPreview: (grid: ReportingWriterGridRow) => void | Promise<void>;
  onSave: (grid: ReportingWriterGridRow) => void | Promise<void>;
}

export function ReportWriterDesignerGrid({
  grid,
  settings,
  customFormula,
  chartDraft,
  formatRules,
  datasetSources,
  customDatasetText,
  isSaving,
  isPreviewing,
  preview,
  previousPreview,
  getZoneTokens,
  onTokenDragStart,
  onZoneDrop,
  onTokenRemove,
  onTokenMove,
  onReset,
  onSettingsChange,
  onCustomFormulaChange,
  onCustomDatasetChange,
  onChartDraftChange,
  onFormatRuleAdd,
  onFormatRuleRemove,
  onFormatRuleChange,
  onPreview,
  onSave
}: ReportWriterDesignerGridProps) {
  const accessPrincipalKindLocked = settings.accessMode !== "Restricted";
  const accessPrincipalIdDisabled = settings.accessMode === "CompanyWide";
  const accessPrincipalIdLabel = settings.accessMode === "Private" ? "Owner ID" : "Principal ID";
  const accessPolicySummary = buildReportAccessPolicySummary(settings);
  const topNDisabled = settings.gridKind !== "TopN";
  const topNLabel = buildReportWriterDraftTopNLabel(settings);
  const previewDatasetProfiles = buildReportWriterPreviewDatasetProfiles(datasetSources);
  const selectedDatasetSource = resolveReportWriterDatasetSource(datasetSources, settings.previewDataset);
  const rowTokens = getZoneTokens(grid, "rowFields");
  const columnTokens = getZoneTokens(grid, "columnFields");
  const metricTokens = getZoneTokens(grid, "metrics");
  const formulaTokens = getZoneTokens(grid, "formulas");
  const draftLayoutSummary = buildReportWriterDraftLayoutSummary(
    settings,
    rowTokens,
    columnTokens,
    metricTokens,
    formulaTokens,
    grid.filters.length
  );

  return (
    <div
      role="group"
      aria-label={grid.ariaLabel}
      className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3"
    >
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <h3 className="text-sm font-semibold text-foreground">{grid.title}</h3>
          <p className="mt-1 break-words font-mono text-[11px] text-muted-foreground">{grid.templateId} · v{grid.templateVersion}</p>
        </div>
        <span className="flex flex-wrap items-center gap-1.5">
          <Badge variant="outline">{settings.gridKind}</Badge>
          <Badge variant="outline">{topNLabel}</Badge>
        </span>
      </div>
      <p className="mt-2 text-xs leading-5 text-muted-foreground">{grid.summary}</p>
      <div
        role="status"
        aria-label={`${grid.title} current report-writer layout`}
        className="mt-2 rounded-md border border-border/70 bg-background/30 px-2.5 py-2 text-xs leading-5 text-muted-foreground"
      >
        {draftLayoutSummary}
      </div>
      <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
        <span className="flex flex-wrap items-center gap-1.5 font-mono text-[11px] text-muted-foreground">
          <span>{grid.sortLabel}</span>
          <span aria-hidden="true">·</span>
          <span>{grid.filterSummary}</span>
        </span>
        <Button
          type="button"
          size="sm"
          variant="ghost"
          aria-label={`Reset ${grid.title} report-writer draft`}
          onClick={() => onReset(grid)}
        >
          <RotateCcw className="h-4 w-4" aria-hidden="true" />
          Reset
        </Button>
      </div>
      <div className="mt-3 grid gap-2 md:grid-cols-2">
        <label className="space-y-1">
          <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Draft name</span>
          <Input
            value={settings.name}
            onChange={(event) => onSettingsChange(grid, "name", event.target.value)}
            aria-label={`${grid.title} draft name`}
            className="font-mono"
          />
        </label>
        <label className="space-y-1">
          <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Display name</span>
          <Input
            value={settings.displayName}
            onChange={(event) => onSettingsChange(grid, "displayName", event.target.value)}
            aria-label={`${grid.title} draft display name`}
          />
        </label>
        <label className="space-y-1">
          <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Preview dataset</span>
          <Select
            value={settings.previewDataset}
            onChange={(event) => onSettingsChange(grid, "previewDataset", event.target.value)}
            aria-label={`${grid.title} preview dataset`}
          >
            {previewDatasetProfiles.map((profile) => (
              <option key={profile.id} value={profile.id}>{profile.label}</option>
            ))}
          </Select>
        </label>
        <div className="grid gap-2 sm:grid-cols-[1fr_0.7fr]">
          <label className="space-y-1">
            <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Grid type</span>
            <Select
              value={settings.gridKind}
              onChange={(event) => onSettingsChange(grid, "gridKind", event.target.value)}
              aria-label={`${grid.title} draft grid type`}
            >
              <option value="Pivot">Pivot</option>
              <option value="TopN">Top-N</option>
              <option value="Contribution">Contribution</option>
              <option value="Detail">Detail</option>
            </Select>
          </label>
          <label className="space-y-1">
            <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Top N</span>
            <Input
              type="number"
              min={1}
              max={100}
              value={settings.topN}
              onChange={(event) => onSettingsChange(grid, "topN", event.target.value)}
              aria-label={`${grid.title} draft top-n count`}
              disabled={topNDisabled}
            />
          </label>
        </div>
        <label className="space-y-1">
          <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Access</span>
          <Select
            value={settings.accessMode}
            onChange={(event) => onSettingsChange(grid, "accessMode", event.target.value)}
            aria-label={`${grid.title} draft access mode`}
          >
            <option value="CompanyWide">Company-wide</option>
            <option value="Restricted">User or group</option>
            <option value="Private">User-locked</option>
          </Select>
        </label>
        <div className="grid gap-2 sm:grid-cols-[0.8fr_1.2fr]">
          <label className="space-y-1">
            <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Principal</span>
            <Select
              value={settings.principalKind}
              onChange={(event) => onSettingsChange(grid, "principalKind", event.target.value)}
              aria-label={`${grid.title} draft principal kind`}
              disabled={accessPrincipalKindLocked}
            >
              <option value="User">User</option>
              <option value="Group">Group</option>
              <option value="Company">Company</option>
            </Select>
          </label>
          <label className="space-y-1">
            <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{accessPrincipalIdLabel}</span>
            <Input
              value={settings.principalId}
              onChange={(event) => onSettingsChange(grid, "principalId", event.target.value)}
              aria-label={`${grid.title} draft principal id`}
              className="font-mono"
              disabled={accessPrincipalIdDisabled}
            />
          </label>
        </div>
      </div>
      {settings.previewDataset === "customRows" ? (
        <label className="mt-3 block space-y-1">
          <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Custom dataset rows</span>
          <textarea
            value={customDatasetText}
            onChange={(event) => onCustomDatasetChange(grid, event.target.value)}
            aria-label={`${grid.title} custom dataset rows`}
            className="min-h-28 w-full resize-y rounded-md border border-input bg-background px-3 py-2 font-mono text-xs text-foreground shadow-sm outline-none transition-colors placeholder:text-muted-foreground focus-visible:ring-2 focus-visible:ring-ring"
            spellCheck={false}
            placeholder={'[{"sector":"Technology","strategy":"Core","marketValue":"150","pnl":"15"}]\n\nsector,strategy,marketValue,pnl\nTechnology,Core,150,15'}
          />
        </label>
      ) : null}
      {selectedDatasetSource ? (
        <div
          className="mt-3 rounded-md border border-border/70 bg-background/30 px-2.5 py-2 text-xs leading-5 text-muted-foreground"
          aria-label={`${grid.title} retained dataset source`}
        >
          <span className="font-medium text-foreground">{selectedDatasetSource.label}</span>
          <span className="ml-2 font-mono">{selectedDatasetSource.rowCount.toLocaleString()} rows</span>
          <p className="mt-1">{selectedDatasetSource.description}</p>
          <div className="mt-2 grid gap-1.5 sm:grid-cols-2" role="list" aria-label={`${grid.title} retained dataset field catalog`}>
            {buildReportWriterDatasetFieldCatalog(selectedDatasetSource).map((field) => (
              <div key={field.name} role="listitem" className="rounded-sm border border-border/60 bg-secondary/25 px-2 py-1.5">
                <span className="block font-medium text-foreground">{field.label || field.name}</span>
                <span className="mt-0.5 block font-mono text-[11px] text-muted-foreground">
                  {field.name} · {field.role} · {field.dataType}
                </span>
                {field.description ? (
                  <span className="mt-1 block text-[11px] leading-4 text-muted-foreground">{field.description}</span>
                ) : null}
              </div>
            ))}
          </div>
        </div>
      ) : null}
      <p className="mt-2 rounded-md border border-border/70 bg-background/30 px-2.5 py-2 text-xs text-muted-foreground">
        {accessPolicySummary}
      </p>
      <div className="mt-3 rounded-md border border-border/70 bg-background/25 px-2.5 py-2">
        <div className="flex items-center gap-1.5">
          <Filter className="h-3.5 w-3.5 text-muted-foreground" aria-hidden="true" />
          <div className="eyebrow-label">Filter</div>
        </div>
        {grid.filters.length > 0 ? (
          <div className="mt-2 flex flex-wrap gap-1.5" aria-label={`${grid.title} saved filters`}>
            {grid.filters.map((filter) => (
              <Badge key={filter.id} variant="outline">{filter.summary}</Badge>
            ))}
          </div>
        ) : null}
        <div className="mt-2 grid gap-2 md:grid-cols-[1fr_0.8fr_1fr]">
          <label className="space-y-1">
            <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Field</span>
            <Select
              value={settings.filterField}
              onChange={(event) => onSettingsChange(grid, "filterField", event.target.value)}
              aria-label={`${grid.title} filter field`}
            >
              <option value="">No filter</option>
              {grid.sourceFields.map((field) => (
                <option key={field.id} value={field.fieldName ?? field.label}>{field.label}</option>
              ))}
            </Select>
          </label>
          <label className="space-y-1">
            <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Operator</span>
            <Select
              value={settings.filterOperator}
              onChange={(event) => onSettingsChange(grid, "filterOperator", event.target.value)}
              aria-label={`${grid.title} filter operator`}
              disabled={!settings.filterField}
            >
              <option value="Equals">=</option>
              <option value="NotEquals">!=</option>
              <option value="Contains">Contains</option>
              <option value="StartsWith">Starts with</option>
              <option value="EndsWith">Ends with</option>
              <option value="GreaterThan">&gt;</option>
              <option value="GreaterThanOrEqual">&gt;=</option>
              <option value="LessThan">&lt;</option>
              <option value="LessThanOrEqual">&lt;=</option>
              <option value="IsBlank">Is blank</option>
              <option value="IsNotBlank">Is not blank</option>
            </Select>
          </label>
          <label className="space-y-1">
            <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Value</span>
            <Input
              value={settings.filterValue}
              onChange={(event) => onSettingsChange(grid, "filterValue", event.target.value)}
              aria-label={`${grid.title} filter value`}
              className="font-mono"
              disabled={!settings.filterField || isBlankFilterOperator(settings.filterOperator)}
            />
          </label>
        </div>
      </div>
      <div className="mt-3 grid gap-3 lg:grid-cols-[0.85fr_1.15fr]">
        <div>
          <div className="eyebrow-label">Fields</div>
          <div role="list" aria-label={`${grid.title} source fields`} className="mt-2 flex flex-wrap gap-1.5">
            {grid.sourceFields.map((token) => (
              <ReportWriterTokenChip
                key={token.id}
                token={token}
                draggable
                onDragStart={onTokenDragStart}
              />
            ))}
          </div>
        </div>
        <div className="grid gap-2 sm:grid-cols-2">
          <ReportWriterDropZoneView
            grid={grid}
            zone="rowFields"
            label="Rows"
            tokens={rowTokens}
          onTokenDragStart={onTokenDragStart}
          onZoneDrop={onZoneDrop}
          onTokenRemove={onTokenRemove}
          onTokenMove={onTokenMove}
        />
          <ReportWriterDropZoneView
            grid={grid}
            zone="columnFields"
            label="Columns"
            tokens={columnTokens}
          onTokenDragStart={onTokenDragStart}
          onZoneDrop={onZoneDrop}
          onTokenRemove={onTokenRemove}
          onTokenMove={onTokenMove}
        />
          <ReportWriterDropZoneView
            grid={grid}
            zone="metrics"
            label="Metrics"
            tokens={metricTokens}
          onTokenDragStart={onTokenDragStart}
          onZoneDrop={onZoneDrop}
          onTokenRemove={onTokenRemove}
          onTokenMove={onTokenMove}
        />
          <ReportWriterDropZoneView
            grid={grid}
            zone="formulas"
            label="Formulas"
            tokens={formulaTokens}
          onTokenDragStart={onTokenDragStart}
          onZoneDrop={onZoneDrop}
          onTokenRemove={onTokenRemove}
          onTokenMove={onTokenMove}
        />
        </div>
      </div>
      <div className="mt-3 rounded-md border border-border/70 bg-background/25 px-2.5 py-2">
        <div className="eyebrow-label">Custom formula</div>
        <div className="mt-2 grid gap-2 md:grid-cols-[0.8fr_0.9fr_1.3fr]">
          <label className="space-y-1">
            <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Name</span>
            <Input
              value={customFormula.name}
              onChange={(event) => onCustomFormulaChange(grid, "name", event.target.value)}
              aria-label={`${grid.title} custom formula name`}
              className="font-mono"
            />
          </label>
          <label className="space-y-1">
            <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Label</span>
            <Input
              value={customFormula.label}
              onChange={(event) => onCustomFormulaChange(grid, "label", event.target.value)}
              aria-label={`${grid.title} custom formula label`}
            />
          </label>
          <label className="space-y-1">
            <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Expression</span>
            <Input
              value={customFormula.expression}
              onChange={(event) => onCustomFormulaChange(grid, "expression", event.target.value)}
              aria-label={`${grid.title} custom formula expression`}
              className="font-mono"
              placeholder="{pnl} / {marketValue} * 100"
            />
          </label>
        </div>
      </div>
      <div className="mt-3 rounded-md border border-border/70 bg-background/25 px-2.5 py-2">
        <div className="flex items-center justify-between gap-2">
          <div className="flex items-center gap-1.5">
            <Palette className="h-3.5 w-3.5 text-muted-foreground" aria-hidden="true" />
            <div className="eyebrow-label">Conditional formatting</div>
          </div>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            aria-label={`Add conditional formatting rule for ${grid.title}`}
            onClick={() => onFormatRuleAdd(grid)}
          >
            <Plus className="h-3.5 w-3.5" aria-hidden="true" />
            Add rule
          </Button>
        </div>
        {formatRules.length > 0 ? (
          <div className="mt-2 space-y-2">
            {formatRules.map((rule) => (
              <div key={rule.id} className="grid gap-2 md:grid-cols-[1fr_0.8fr_0.8fr_0.8fr_auto] items-end">
                <label className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Column</span>
                  <Select
                    value={rule.column}
                    onChange={(event) => onFormatRuleChange(grid, rule.id, "column", event.target.value)}
                    aria-label={`Formatting rule column for ${grid.title}`}
                  >
                    <option value="">Select column</option>
                    {grid.sourceFields.map((field) => (
                      <option key={field.id} value={field.fieldName ?? field.label}>{field.label}</option>
                    ))}
                  </Select>
                </label>
                <label className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Operator</span>
                  <Select
                    value={rule.operator}
                    onChange={(event) => onFormatRuleChange(grid, rule.id, "operator", event.target.value)}
                    aria-label={`Formatting rule operator for ${grid.title}`}
                  >
                    <option value="Equals">=</option>
                    <option value="NotEquals">≠</option>
                    <option value="GreaterThan">&gt;</option>
                    <option value="GreaterThanOrEqual">&gt;=</option>
                    <option value="LessThan">&lt;</option>
                    <option value="LessThanOrEqual">&lt;=</option>
                    <option value="Contains">Contains</option>
                    <option value="IsBlank">Is blank</option>
                    <option value="IsNotBlank">Is not blank</option>
                  </Select>
                </label>
                <label className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Value</span>
                  <Input
                    value={rule.value}
                    onChange={(event) => onFormatRuleChange(grid, rule.id, "value", event.target.value)}
                    aria-label={`Formatting rule value for ${grid.title}`}
                    className="font-mono"
                    disabled={isBlankFilterOperator(rule.operator)}
                  />
                </label>
                <label className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Style</span>
                  <Select
                    value={rule.style}
                    onChange={(event) => onFormatRuleChange(grid, rule.id, "style", event.target.value)}
                    aria-label={`Formatting rule style for ${grid.title}`}
                  >
                    <option value="Success">Success</option>
                    <option value="Warning">Warning</option>
                    <option value="Danger">Danger</option>
                    <option value="Info">Info</option>
                  </Select>
                </label>
                <Button
                  type="button"
                  size="sm"
                  variant="ghost"
                  aria-label={`Remove formatting rule from ${grid.title}`}
                  onClick={() => onFormatRuleRemove(grid, rule.id)}
                >
                  <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                </Button>
              </div>
            ))}
          </div>
        ) : (
          <p className="mt-2 text-xs text-muted-foreground">No rules. Add a rule to highlight cells by value.</p>
        )}
      </div>
      <div className="mt-3 rounded-md border border-border/70 bg-background/25 px-2.5 py-2">
        <div className="flex items-center gap-1.5">
          <BarChart2 className="h-3.5 w-3.5 text-muted-foreground" aria-hidden="true" />
          <div className="eyebrow-label">Inline chart</div>
        </div>
        <label className="mt-2 flex items-center gap-2 cursor-pointer">
          <Checkbox
            checked={chartDraft.enabled}
            onCheckedChange={(checked) => onChartDraftChange(grid, "enabled", String(checked === true))}
            aria-label={`Enable inline chart for ${grid.title}`}
          />
          <span className="text-xs text-foreground">Enable inline chart</span>
        </label>
        {chartDraft.enabled ? (
          <div className="mt-2 grid gap-2 md:grid-cols-[0.6fr_1fr_1.4fr]">
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Type</span>
              <Select
                value={chartDraft.type}
                onChange={(event) => onChartDraftChange(grid, "type", event.target.value)}
                aria-label={`Chart type for ${grid.title}`}
              >
                <option value="Bar">Bar</option>
                <option value="StackedBar">Stacked bar</option>
                <option value="Line">Line</option>
                <option value="Area">Area</option>
                <option value="Pie">Pie</option>
              </Select>
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Category field</span>
              <Select
                value={chartDraft.categoryField}
                onChange={(event) => onChartDraftChange(grid, "categoryField", event.target.value)}
                aria-label={`Chart category field for ${grid.title}`}
              >
                <option value="">Select field</option>
                {grid.sourceFields.map((field) => (
                  <option key={field.id} value={field.fieldName ?? field.label}>{field.label}</option>
                ))}
              </Select>
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Value columns (comma-separated)</span>
              <Input
                value={chartDraft.valueColumns}
                onChange={(event) => onChartDraftChange(grid, "valueColumns", event.target.value)}
                aria-label={`Chart value columns for ${grid.title}`}
                className="font-mono"
                placeholder="marketValue, pnl"
              />
            </label>
          </div>
        ) : null}
      </div>
      <div className="mt-3 flex flex-wrap justify-end gap-2">
        <Button
          type="button"
          size="sm"
          variant="secondary"
          aria-label={`Preview ${grid.title} report-writer grid`}
          disabled={isPreviewing}
          busy={isPreviewing}
          busyLabel="Previewing"
          onClick={() => void onPreview(grid)}
        >
          <Eye className="h-4 w-4" aria-hidden="true" />
          Preview
        </Button>
        <Button
          type="button"
          size="sm"
          aria-label={`Save ${grid.title} as governed report template draft`}
          disabled={isSaving}
          busy={isSaving}
          busyLabel="Saving"
          onClick={() => void onSave(grid)}
        >
          <PencilLine className="h-4 w-4" aria-hidden="true" />
          Save draft
        </Button>
      </div>
      {preview ? (
        <ReportWriterPreviewTable grid={grid} preview={preview} previousPreview={previousPreview} />
      ) : null}
    </div>
  );
}

function ReportWriterPreviewTable({ grid, preview, previousPreview }: { grid: ReportingWriterGridRow; preview: ReportWriterGridRender; previousPreview?: ReportWriterGridRender | null }) {
  const rows = preview.rows.slice(0, 5);
  const lineage = preview.lineage;
  const dataDictionary = preview.dataDictionary ?? [];
  const validationChecks = preview.validationChecks ?? [];
  const comparison = previousPreview ? buildReportWriterGridDiff(previousPreview, preview) : null;
  const comparisonChangeCount = comparison
    ? comparison.addedRowCount + comparison.removedRowCount + comparison.changedRowCount
    : 0;
  return (
    <div className="mt-3 rounded-md border border-border/70 bg-background/35 px-2.5 py-2" aria-label={`${grid.title} live preview`}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <div className="eyebrow-label">Live preview</div>
          <p className="mt-1 text-xs text-muted-foreground">{preview.title} · {preview.rows.length} row{preview.rows.length === 1 ? "" : "s"}</p>
        </div>
        <Badge variant="outline">{preview.kind}</Badge>
      </div>
      <div className="mt-2 max-h-56 overflow-auto rounded-sm border border-border/60">
        <table className="min-w-full table-fixed text-left text-xs">
          <thead className="bg-secondary/40 text-[10px] uppercase tracking-[0.12em] text-muted-foreground">
            <tr>
              {preview.columns.map((column) => (
                <th key={column.key} scope="col" className="min-w-28 px-2 py-1.5 font-semibold">
                  <span className="block truncate" title={column.label}>{column.label}</span>
                  <span className="mt-0.5 block truncate font-mono text-[9px] normal-case tracking-normal" title={column.role}>
                    {column.role}
                  </span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length > 0 ? rows.map((row) => (
              <tr key={row.rowKey} className="border-t border-border/50">
                {preview.columns.map((column) => (
                  <td
                    key={`${row.rowKey}:${column.key}`}
                    className={cn("px-2 py-1.5 font-mono text-foreground", cellStyleClassName(resolveCellStyle(row, column.key)))}
                  >
                    <span className="block truncate" title={row.values[column.key] ?? ""}>{row.values[column.key] ?? ""}</span>
                  </td>
                ))}
              </tr>
            )) : (
              <tr>
                <td className="px-2 py-2 text-muted-foreground" colSpan={Math.max(preview.columns.length, 1)}>
                  No rows returned.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
      {preview.chart ? <ReportWriterChartPreview chart={preview.chart} /> : null}
      {comparison ? (
        <details className="mt-2 rounded-sm border border-border/60 bg-secondary/20 px-2 py-1.5 text-xs">
          <summary className="cursor-pointer select-none text-[11px] font-semibold text-foreground">
            Changes since previous preview ({comparisonChangeCount} changed)
          </summary>
          <div className="mt-2">
            <ReportWriterGridDiffView diff={comparison} />
          </div>
        </details>
      ) : null}
      {lineage ? (
        <div className="mt-2 rounded-sm border border-border/60 bg-secondary/25 px-2 py-2 text-xs" aria-label={`${grid.title} preview audit trace`}>
          <div className="eyebrow-label">Audit trace</div>
          <dl className="mt-2 grid gap-2 sm:grid-cols-2">
            <div>
              <dt className="text-[10px] uppercase tracking-[0.12em] text-muted-foreground">Rows</dt>
              <dd className="mt-1 font-mono text-foreground">
                {lineage.inputRowCount} input / {lineage.filteredInputRowCount ?? lineage.inputRowCount} filtered / {lineage.outputRowCount} output
              </dd>
            </div>
            <div>
              <dt className="text-[10px] uppercase tracking-[0.12em] text-muted-foreground">Sources</dt>
              <dd className="mt-1 break-words font-mono text-foreground">{lineage.sourceFields.length > 0 ? lineage.sourceFields.join(", ") : "None"}</dd>
            </div>
            <div>
              <dt className="text-[10px] uppercase tracking-[0.12em] text-muted-foreground">Metrics</dt>
              <dd className="mt-1 break-words font-mono text-foreground">
                {lineage.metrics.length > 0
                  ? lineage.metrics.map((metric) => `${metric.name}=${metric.function}(${metric.sourceField})`).join(", ")
                  : "None"}
              </dd>
            </div>
            <div>
              <dt className="text-[10px] uppercase tracking-[0.12em] text-muted-foreground">Formulas</dt>
              <dd className="mt-1 break-words font-mono text-foreground">
                {lineage.formulas.length > 0
                  ? lineage.formulas.map((formula) => `${formula.name}=${formula.expression} [${formula.sourceFields.join(", ")}]`).join(", ")
                  : "None"}
              </dd>
            </div>
            <div>
              <dt className="text-[10px] uppercase tracking-[0.12em] text-muted-foreground">Filters</dt>
              <dd className="mt-1 break-words font-mono text-foreground">
                {lineage.filters && lineage.filters.length > 0
                  ? lineage.filters.map((filter) => `${filter.field} ${formatReportWriterFilterOperator(normalizeReportWriterFilterOperator(filter.operator))}${filter.value ? ` ${filter.value}` : ""}`).join(", ")
                  : "None"}
              </dd>
            </div>
          </dl>
        </div>
      ) : null}
      {dataDictionary.length > 0 ? (
        <div className="mt-2 rounded-sm border border-border/60 bg-secondary/25 px-2 py-2 text-xs" aria-label={`${grid.title} preview data dictionary`}>
          <div className="eyebrow-label">Data dictionary</div>
          <div role="list" aria-label={`${grid.title} preview data dictionary fields`} className="mt-2 grid gap-1.5 sm:grid-cols-2">
            {dataDictionary.map((field) => (
              <div key={field.key} role="listitem" className="rounded-sm border border-border/60 bg-background/35 px-2 py-1.5">
                <div className="flex flex-wrap items-center gap-1.5">
                  <span className="font-mono text-foreground">{field.label}</span>
                  <Badge variant={field.isGenerated ? "warning" : "outline"}>{field.role}</Badge>
                  <Badge variant="outline">{field.dataType}</Badge>
                </div>
                <p className="mt-1 break-words text-[11px] text-muted-foreground">
                  {field.sourceField}
                  {field.description ? ` · ${field.description}` : ""}
                </p>
              </div>
            ))}
          </div>
        </div>
      ) : null}
      {validationChecks.length > 0 ? (
        <div className="mt-2 rounded-sm border border-border/60 bg-secondary/25 px-2 py-2 text-xs" aria-label={`${grid.title} preview validation checks`}>
          <div className="eyebrow-label">Validation checks</div>
          <ul className="mt-2 space-y-1.5">
            {validationChecks.map((check) => (
              <li key={check.checkId} className="flex flex-wrap items-start gap-2 rounded-sm border border-border/60 bg-background/35 px-2 py-1.5">
                <Badge variant={reportWriterValidationCheckVariant(check.status)}>{check.status}</Badge>
                <span className="min-w-0">
                  <span className="block break-all font-mono text-foreground">{check.checkId}</span>
                  <span className="mt-1 block break-words text-[11px] text-muted-foreground">{check.detail}</span>
                </span>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
      {preview.warnings.length > 0 ? (
        <ul aria-label={`${grid.title} preview warnings`} className="mt-2 space-y-1 text-xs text-warning">
          {preview.warnings.map((warning) => (
            <li key={warning}>{warning}</li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

function reportWriterValidationCheckVariant(status: string): "outline" | "success" | "warning" | "danger" {
  const normalized = status.trim().toLowerCase();
  if (normalized === "passed" || normalized === "pass" || normalized === "ready" || normalized === "success") {
    return "success";
  }

  if (normalized === "failed" || normalized === "fail" || normalized === "error" || normalized === "blocked") {
    return "danger";
  }

  if (normalized === "warning" || normalized === "review" || normalized === "pending") {
    return "warning";
  }

  return "outline";
}

interface ReportWriterDropZoneViewProps {
  grid: ReportingWriterGridRow;
  zone: ReportWriterDropZone;
  label: string;
  tokens: ReportingWriterToken[];
  onTokenDragStart: (event: DragEvent<HTMLElement>, token: ReportingWriterToken, origin?: ReportWriterTokenDragOrigin | null) => void;
  onZoneDrop: (event: DragEvent<HTMLElement>, grid: ReportingWriterGridRow, zone: ReportWriterDropZone) => void;
  onTokenRemove: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone, tokenId: string) => void;
  onTokenMove: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone, tokenId: string, direction: -1 | 1) => void;
}

function ReportWriterDropZoneView({
  grid,
  zone,
  label,
  tokens,
  onTokenDragStart,
  onZoneDrop,
  onTokenRemove,
  onTokenMove
}: ReportWriterDropZoneViewProps) {
  return (
    <div
      role="list"
      aria-label={`${grid.title} ${label}`}
      className="min-h-24 rounded-md border border-dashed border-border/70 bg-background/25 px-2.5 py-2"
      onDragOver={(event) => event.preventDefault()}
      onDrop={(event) => onZoneDrop(event, grid, zone)}
    >
      <div className="mb-2 text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</div>
      <div className="flex flex-wrap gap-1.5">
        {tokens.length > 0 ? tokens.map((token, tokenIndex) => (
          <ReportWriterTokenChip
            key={token.id}
            token={token}
            draggable
            onDragStart={onTokenDragStart}
            dragOrigin={{ gridId: grid.id, zone }}
            canMovePrevious={tokenIndex > 0}
            canMoveNext={tokenIndex < tokens.length - 1}
            onMove={(direction) => onTokenMove(grid, zone, token.id, direction)}
            onRemove={() => onTokenRemove(grid, zone, token.id)}
          />
        )) : (
          <span className="text-xs text-muted-foreground">No fields</span>
        )}
      </div>
    </div>
  );
}

function ReportWriterTokenChip({
  token,
  draggable,
  onDragStart,
  dragOrigin,
  canMovePrevious,
  canMoveNext,
  onMove,
  onRemove
}: {
  token: ReportingWriterToken;
  draggable?: boolean;
  onDragStart: (event: DragEvent<HTMLElement>, token: ReportingWriterToken, origin?: ReportWriterTokenDragOrigin | null) => void;
  dragOrigin?: ReportWriterTokenDragOrigin | null;
  canMovePrevious?: boolean;
  canMoveNext?: boolean;
  onMove?: (direction: -1 | 1) => void;
  onRemove?: () => void;
}) {
  return (
    <span
      role="listitem"
      draggable={draggable}
      onDragStart={(event) => onDragStart(event, token, dragOrigin ?? null)}
      className="inline-flex max-w-full items-center gap-1.5 rounded-sm border border-border/70 bg-secondary/35 px-2 py-1 text-[11px] text-foreground"
      title={token.detail}
    >
      <GripVertical className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
      <Badge variant={token.kind === "formula" ? "warning" : token.kind === "metric" ? "success" : "outline"}>{token.kind}</Badge>
      <span className="truncate font-mono">{token.label}</span>
      {onMove ? (
        <span className="ml-0.5 inline-flex shrink-0 items-center gap-0.5">
          <button
            type="button"
            className="inline-flex h-4 w-4 items-center justify-center rounded-sm text-muted-foreground hover:bg-background/80 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-35"
            aria-label={`Move ${token.label} left`}
            disabled={!canMovePrevious}
            onClick={(event) => {
              event.preventDefault();
              event.stopPropagation();
              onMove(-1);
            }}
          >
            <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
          </button>
          <button
            type="button"
            className="inline-flex h-4 w-4 items-center justify-center rounded-sm text-muted-foreground hover:bg-background/80 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-35"
            aria-label={`Move ${token.label} right`}
            disabled={!canMoveNext}
            onClick={(event) => {
              event.preventDefault();
              event.stopPropagation();
              onMove(1);
            }}
          >
            <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
          </button>
        </span>
      ) : null}
      {onRemove ? (
        <button
          type="button"
          className="ml-0.5 inline-flex h-4 w-4 shrink-0 items-center justify-center rounded-sm text-muted-foreground hover:bg-background/80 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          aria-label={`Remove ${token.label}`}
          onClick={(event) => {
            event.preventDefault();
            event.stopPropagation();
            onRemove();
          }}
        >
          <XCircle className="h-3.5 w-3.5" aria-hidden="true" />
        </button>
      ) : null}
    </span>
  );
}

function normalizeReportWriterDraftText(value: string | null | undefined, fallback: string): string {
  const normalized = value?.trim();
  return normalized || fallback;
}

function normalizeReportWriterIdentifierToken(value: string | null | undefined, fallback: string): string {
  const normalized = normalizeReportWriterDraftText(value, fallback)
    .replace(/[^A-Za-z0-9_.-]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return normalized || fallback;
}

function buildReportAccessPolicySummary(settings: ReportWriterDraftSettings): string {
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

function buildReportWriterDraftTopNLabel(settings: ReportWriterDraftSettings): string {
  return settings.gridKind === "TopN" ? `Top ${parseReportWriterTopN(settings.topN)}` : settings.gridKind;
}

function buildReportWriterDraftLayoutSummary(
  settings: ReportWriterDraftSettings,
  rowTokens: ReportingWriterToken[],
  columnTokens: ReportingWriterToken[],
  metricTokens: ReportingWriterToken[],
  formulaTokens: ReportingWriterToken[],
  savedFilterCount: number
): string {
  const dimensionCount = rowTokens.length + columnTokens.length;
  const totalFilterCount = settings.filterField ? savedFilterCount + 1 : savedFilterCount;
  const filterSummary = settings.filterField
    ? `${totalFilterCount} filter${totalFilterCount === 1 ? "" : "s"} including draft filter`
    : `${savedFilterCount} saved filter${savedFilterCount === 1 ? "" : "s"}`;
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
