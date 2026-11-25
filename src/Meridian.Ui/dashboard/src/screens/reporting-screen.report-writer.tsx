import { type DragEvent, type ReactNode, useEffect, useId, useMemo, useRef, useState } from "react";
import { BarChart2, ChevronLeft, ChevronRight, Eye, Filter, GripVertical, Palette, PencilLine, Plus, RotateCcw, Trash2, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { TextArea } from "@/components/ui/text-area";
import { PrincipalIdInput } from "@/components/meridian/principal-id-input";
import { ReportWriterExtraFilters } from "@/screens/reporting-screen.report-writer-filters";
import { buildReportWriterDatasetFieldCatalog, parseReportWriterCustomDatasetRows } from "@/screens/reporting-screen.report-writer-dataset";
import { ReportWriterChartPreview } from "@/components/meridian/report-writer-chart-preview";
import { ReportWriterGridDiffView } from "@/components/meridian/report-writer-grid-diff-view";
import { buildReportWriterGridDiff } from "@/lib/report-writer-grid-diff";
import { cellStyleClassName, resolveCellStyle } from "@/lib/report-writer-grid-format";
import { createReportTemplateDraft, renderReportTemplate } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { cn } from "@/lib/utils";
import { buildReportWriterCatalogTokens, presentReportingIdentifier } from "@/screens/reporting-screen.view-model";
import { reportWriterSamples, type ReportWriterSampleDataset } from "@/screens/reporting-screen.report-writer-samples";
import type { ReportingWriterGridRow, ReportingWriterToken } from "@/screens/reporting-screen.view-model";
import {
  isAbortError,
  REPORT_WRITER_AUTO_PREVIEW_MAX_DATASET_ROWS,
  useReportWriterAutoPreview,
  type ReportWriterPreviewPhase,
  type ReportWriterQuietPreviewError
} from "@/screens/reporting-screen.report-writer-auto-preview";
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
import { formatCount } from "@/lib/format";
import { usesTrackedCaps } from "@/design-system/label-case";

export type ReportWriterDropZone = "rowFields" | "columnFields" | "metrics" | "formulas";
export type ReportWriterDraftState = Partial<Record<ReportWriterDropZone, ReportingWriterToken[]>>;
export type ReportWriterTokenDragOrigin = { gridId: string; zone: ReportWriterDropZone };
export type ReportWriterTokenDragPayload = { token: ReportingWriterToken; origin?: ReportWriterTokenDragOrigin | null };
export type ReportWriterPreviewDatasetProfile = "" | ReportWriterSampleDataset | "customRows" | `source:${string}`;

interface ReportWriterPreview {
  grid: ReportWriterGridRender;
  sourceDetail: string;
  source: ReportWriterDatasetSource | null;
}
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

/** One additional filter row; the first filter lives in the flat filterField/Operator/Value settings. */
export interface ReportWriterFilterDraft {
  id: string;
  field: string;
  operator: ReportWriterFilterOperator;
  value: string;
}

export type ReportWriterFilterDraftField = "field" | "operator" | "value";

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
  /** Filters after the first. Every filter must match for a row to be kept (AND). */
  extraFilters: ReportWriterFilterDraft[];
}

export interface ReportWriterCustomFormulaDraft {
  name: string;
  label: string;
  expression: string;
}

type ReportWriterZones = Record<ReportWriterDropZone, ReportingWriterToken[]>;
type PreviewRequest = { gridId: string; controller: AbortController; source: ReportWriterDatasetSource | null };

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
    datasetRows: Record<string, string>[],
    chartDraft?: ReportWriterChartDraft | null,
    formatRules?: ReportWriterFormatRuleDraft[] | null
  ) => RenderReportTemplateRequest;
  /** Auto-preview debounce override, for tests. */
  autoPreviewDebounceMs?: number;
}

export function useReportingReportWriter({
  datasetSources,
  buildDraftRequest,
  buildRenderRequest,
  autoPreviewDebounceMs
}: UseReportingReportWriterOptions) {
  const [writerDrafts, setWriterDrafts] = useState<Record<string, ReportWriterDraftState>>({});
  const [writerDraftSettings, setWriterDraftSettings] = useState<Record<string, Partial<ReportWriterDraftSettings>>>({});
  const [writerCustomFormulas, setWriterCustomFormulas] = useState<Record<string, Partial<ReportWriterCustomFormulaDraft>>>({});
  const [writerCustomDatasetText, setWriterCustomDatasetText] = useState<Record<string, string>>({});
  const [writerDraftStatus, setWriterDraftStatus] = useState<ReportingCommandStatus | null>(null);
  const [writerPreviewStatus, setWriterPreviewStatus] = useState<ReportingCommandStatus | null>(null);
  const [writerPreviewByGridId, setWriterPreviewByGridId] = useState<Record<string, ReportWriterPreview | null>>({});
  const [writerPreviousPreviewByGridId, setWriterPreviousPreviewByGridId] = useState<Record<string, ReportWriterGridRender | null>>({});
  const [writerChartDrafts, setWriterChartDrafts] = useState<Record<string, ReportWriterChartDraft>>({});
  const [writerFormatRuleDrafts, setWriterFormatRuleDrafts] = useState<Record<string, ReportWriterFormatRuleDraft[]>>({});
  const activePreview = useRef<PreviewRequest | null>(null);
  const savingWriterDraftId = writerDraftStatus?.state === "running" ? writerDraftStatus.id : null;
  const previewingWriterDraftId = writerPreviewStatus?.state === "running" ? writerPreviewStatus.id : null;
  const [writerPreviewPhaseByGridId, setWriterPreviewPhaseByGridId] = useState<Record<string, ReportWriterPreviewPhase>>({});
  const [writerAutoPreviewDisabledByGridId, setWriterAutoPreviewDisabledByGridId] = useState<Record<string, boolean>>({});
  const [writerQuietPreviewErrorByGridId, setWriterQuietPreviewErrorByGridId] = useState<Record<string, ReportWriterQuietPreviewError>>({});
  const [writerEditingGridId, setWriterEditingGridId] = useState<string | null>(null);

  // Auto preview (docs/engineering/blueprints/report-writer-auto-preview-blueprint.md). Timers fire
  // after React re-renders with the edit applied, so they call the latest render function through
  // a ref rather than the closure captured at edit time.
  const writerGridsByIdRef = useRef(new Map<string, ReportingWriterGridRow>());
  const latestWriterAutoPreviewRef = useRef<{
    render: (grid: ReportingWriterGridRow, signal: AbortSignal) => Promise<void>;
    canAutoRender: (grid: ReportingWriterGridRow) => boolean;
  } | null>(null);
  const writerAutoPreview = useReportWriterAutoPreview({
    debounceMs: autoPreviewDebounceMs,
    canAutoRender: (gridId) => {
      const grid = writerGridsByIdRef.current.get(gridId);
      return Boolean(grid && latestWriterAutoPreviewRef.current?.canAutoRender(grid));
    },
    render: async (gridId, signal) => {
      const grid = writerGridsByIdRef.current.get(gridId);
      if (grid && latestWriterAutoPreviewRef.current) {
        await latestWriterAutoPreviewRef.current.render(grid, signal);
      }
    }
  });

  useEffect(() => {
    const request = activePreview.current;
    if (request?.source && !datasetSources.includes(request.source)) {
      request.controller.abort();
    }
  }, [datasetSources]);

  useEffect(() => () => activePreview.current?.controller.abort(), []);

  function getWriterPreview(grid: ReportingWriterGridRow): ReportWriterPreview | null {
    const preview = writerPreviewByGridId[grid.id];
    return preview && (!preview.source || datasetSources.includes(preview.source)) ? preview : null;
  }

  /**
   * An edit makes the grid's preview out of date. A layout edit keeps the last preview on screen,
   * dimmed, and schedules a debounced auto re-render. A dataset change clears the preview and its
   * comparison baseline, because neither describes the newly selected data.
   */
  function invalidateWriterPreview(grid: ReportingWriterGridRow, datasetChanged = false) {
    writerGridsByIdRef.current.set(grid.id, grid);
    if (activePreview.current?.gridId === grid.id) {
      activePreview.current.controller.abort();
      activePreview.current = null;
    }
    setWriterPreviewStatus((current) => current?.id === grid.id ? null : current);
    if (datasetChanged) {
      setWriterPreviewByGridId((current) => removeRecordEntry(current, grid.id));
      setWriterPreviousPreviewByGridId((current) => removeRecordEntry(current, grid.id));
    }
    setWriterPreviewPhaseByGridId((current) => ({ ...current, [grid.id]: "stale" }));
    if (isWriterAutoPreviewEnabled(grid)) {
      writerAutoPreview.schedule(grid.id);
    } else {
      writerAutoPreview.cancel(grid.id);
    }
  }

  function getWriterPreviewPhase(grid: ReportingWriterGridRow): ReportWriterPreviewPhase {
    return writerPreviewPhaseByGridId[grid.id] ?? (getWriterPreview(grid) ? "ready" : "idle");
  }

  function isWriterAutoPreviewEnabled(grid: ReportingWriterGridRow): boolean {
    return !writerAutoPreviewDisabledByGridId[grid.id];
  }

  function setWriterAutoPreviewEnabled(grid: ReportingWriterGridRow, enabled: boolean) {
    writerGridsByIdRef.current.set(grid.id, grid);
    setWriterAutoPreviewDisabledByGridId((current) => enabled
      ? removeRecordEntry(current, grid.id)
      : { ...current, [grid.id]: true });
    if (!enabled) {
      writerAutoPreview.cancel(grid.id);
      setWriterPreviewPhaseByGridId((current) => current[grid.id] === "rendering"
        ? { ...current, [grid.id]: "stale" }
        : current);
    } else if (getWriterPreviewPhase(grid) === "stale") {
      writerAutoPreview.schedule(grid.id);
    }
  }

  /** Auto-preview errors stay hidden while the operator is still typing in the grid. */
  function getWriterQuietPreviewError(grid: ReportingWriterGridRow): ReportWriterQuietPreviewError | null {
    return writerEditingGridId === grid.id ? null : writerQuietPreviewErrorByGridId[grid.id] ?? null;
  }

  function setWriterEditSession(gridId: string | null) {
    setWriterEditingGridId(gridId);
  }

  function retryWriterAutoPreview(grid: ReportingWriterGridRow) {
    writerGridsByIdRef.current.set(grid.id, grid);
    writerAutoPreview.runNow(grid.id);
  }

  function resolveWriterPreviewDataset(grid: ReportingWriterGridRow) {
    return resolvePreviewDataset(getWriterDraftSettings(grid).previewDataset, datasetSources, writerCustomDatasetText[grid.id] ?? "");
  }

  function isWriterAutoPreviewPausedForSize(grid: ReportingWriterGridRow): boolean {
    return (resolveWriterPreviewDataset(grid).rows?.length ?? 0) > REPORT_WRITER_AUTO_PREVIEW_MAX_DATASET_ROWS;
  }

  function canAutoRenderWriterPreview(grid: ReportingWriterGridRow): boolean {
    // No dataset chosen yet: nothing to preview, and no error worth raising while the operator edits.
    return isWriterAutoPreviewEnabled(grid)
      && getWriterDraftSettings(grid).previewDataset !== ""
      && !isWriterAutoPreviewPausedForSize(grid);
  }

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
    invalidateWriterPreview(grid, field === "previewDataset");
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
    invalidateWriterPreview(grid);
  }

  function updateWriterCustomDataset(grid: ReportingWriterGridRow, value: string) {
    setWriterCustomDatasetText((current) => ({
      ...current,
      [grid.id]: value
    }));
    invalidateWriterPreview(grid, true);
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
    invalidateWriterPreview(grid);
  }

  function updateWriterExtraFilters(
    grid: ReportingWriterGridRow,
    update: (filters: ReportWriterFilterDraft[]) => ReportWriterFilterDraft[]
  ) {
    setWriterDraftSettings((current) => ({
      ...current,
      [grid.id]: {
        ...current[grid.id],
        extraFilters: update(current[grid.id]?.extraFilters ?? buildDefaultWriterDraftSettings(grid).extraFilters)
      }
    }));
    invalidateWriterPreview(grid);
  }

  function addWriterFilter(grid: ReportingWriterGridRow) {
    const id = `filter-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
    updateWriterExtraFilters(grid, (filters) => [...filters, { id, field: "", operator: "Equals", value: "" }]);
  }

  function removeWriterFilter(grid: ReportingWriterGridRow, filterId: string) {
    updateWriterExtraFilters(grid, (filters) => filters.filter((filter) => filter.id !== filterId));
  }

  function updateWriterFilter(grid: ReportingWriterGridRow, filterId: string, field: ReportWriterFilterDraftField, value: string) {
    updateWriterExtraFilters(grid, (filters) => filters.map((filter) =>
      filter.id === filterId
        ? { ...filter, [field]: field === "operator" ? normalizeReportWriterFilterOperator(value) : value }
        : filter));
  }

  function addWriterFormatRule(grid: ReportingWriterGridRow) {
    const id = `rule-${Date.now()}`;
    setWriterFormatRuleDrafts((current) => ({
      ...current,
      [grid.id]: [...(current[grid.id] ?? []), { id, column: "", operator: "GreaterThan" as ReportWriterFilterOperator, value: "", style: "Warning" as ReportWriterCellStyle }]
    }));
    invalidateWriterPreview(grid);
  }

  function removeWriterFormatRule(grid: ReportingWriterGridRow, ruleId: string) {
    setWriterFormatRuleDrafts((current) => ({
      ...current,
      [grid.id]: (current[grid.id] ?? []).filter((rule) => rule.id !== ruleId)
    }));
    invalidateWriterPreview(grid);
  }

  function updateWriterFormatRule(grid: ReportingWriterGridRow, ruleId: string, field: keyof Omit<ReportWriterFormatRuleDraft, "id">, value: string) {
    setWriterFormatRuleDrafts((current) => ({
      ...current,
      [grid.id]: (current[grid.id] ?? []).map((rule) =>
        rule.id === ruleId ? { ...rule, [field]: value } : rule
      )
    }));
    invalidateWriterPreview(grid);
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

    const { token, origin } = dragPayload;
    placeWriterToken(grid, zone, token, origin);
  }

  function placeWriterToken(
    grid: ReportingWriterGridRow,
    zone: ReportWriterDropZone,
    token: ReportingWriterToken,
    origin?: ReportWriterTokenDragOrigin | null
  ) {
    setWriterDrafts((current) => {
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
    invalidateWriterPreview(grid);
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
    invalidateWriterPreview(grid);
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
    invalidateWriterPreview(grid);
  }

  function resetWriterGrid(grid: ReportingWriterGridRow) {
    setWriterDrafts((current) => removeRecordEntry(current, grid.id));
    invalidateWriterPreview(grid, true);
    // Reset is not an edit to preview: drop the pending render and all preview state.
    writerAutoPreview.cancel(grid.id);
    setWriterPreviewPhaseByGridId((current) => removeRecordEntry(current, grid.id));
    setWriterQuietPreviewErrorByGridId((current) => removeRecordEntry(current, grid.id));
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
    const zones = getWriterCurrentZones(grid);
    const request = buildDraftRequest(grid, settings, zones);
    const dataset = resolvePreviewDataset(settings.previewDataset, datasetSources, writerCustomDatasetText[grid.id] ?? "");
    const incompatible = dataset.rows ? findIncompatibleFields(zones, settings, dataset.fields) : [];

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
            ? `Template validation · ${result.validationIssues.join("; ")}`
            : "Template validation passed.",
          !dataset.rows
            ? "Dataset fields are unverified. Choose a preview dataset to check them."
            : incompatible.length > 0
              ? `Fields absent from the preview dataset · ${incompatible.join(", ")}`
              : "Dataset fields checked against the selected preview dataset."
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
    if (activePreview.current) {
      return;
    }

    writerAutoPreview.cancel(grid.id);

    const settings = getWriterDraftSettings(grid);
    const dataset = resolvePreviewDataset(settings.previewDataset, datasetSources, writerCustomDatasetText[grid.id] ?? "");
    if (!dataset.rows) {
      setWriterPreviewStatus({
        id: grid.id,
        label: "Preview report-writer grid",
        state: "error",
        message: `${grid.title} needs a preview dataset.`,
        details: [dataset.detail]
      });
      return;
    }

    const request = buildRenderRequest(
      grid,
      getWriterCurrentZones(grid),
      settings,
      dataset.rows,
      getWriterChartDraft(grid),
      getWriterFormatRules(grid));
    const previewRequest: PreviewRequest = { gridId: grid.id, controller: new AbortController(), source: dataset.source };
    activePreview.current = previewRequest;
    setWriterPreviewPhaseByGridId((current) => ({ ...current, [grid.id]: "rendering" }));
    setWriterPreviewStatus({
      id: grid.id,
      label: "Preview report-writer grid",
      state: "running",
      message: `${grid.title} preview is rendering.`,
      details: []
    });

    try {
      const result = await renderReportTemplate(request, { signal: previewRequest.controller.signal });
      if (previewRequest.controller.signal.aborted) return;
      const renderedGrid = result.grids?.find((item) => item.gridId === grid.gridId) ?? result.grids?.[0] ?? null;
      const previousPreview = getWriterPreview(grid)?.grid ?? null;
      const failed = renderedGrid?.validationChecks?.some((check) => check.status === "Failed");
      const warnings = Array.from(new Set([...(result.warnings ?? []), ...(renderedGrid?.warnings ?? [])]));
      setWriterPreviewByGridId((current) => ({
        ...current,
        [grid.id]: renderedGrid ? { grid: renderedGrid, sourceDetail: dataset.detail, source: dataset.source } : null
      }));
      setWriterPreviousPreviewByGridId((current) => ({
        ...current,
        [grid.id]: previousPreview
      }));
      setWriterPreviewPhaseByGridId((current) => ({ ...current, [grid.id]: "ready" }));
      setWriterQuietPreviewErrorByGridId((current) => removeRecordEntry(current, grid.id));
      setWriterPreviewStatus({
        id: grid.id,
        label: "Preview report-writer grid",
        state: failed ? "error" : warnings.length > 0 ? "warning" : "success",
        message: failed ? `${grid.title} preview has invalid fields.`
          : warnings.length > 0 ? `${grid.title} preview rendered with warnings.` : `${grid.title} preview rendered.`,
        details: [
          `Template: ${result.templateId.name}@v${result.templateId.version}`,
          `Rows: ${renderedGrid?.rows.length ?? 0}`,
          `Dataset rows: ${dataset.rows.length}`,
          result.missingRequiredParameters.length > 0
            ? `Missing parameters: ${result.missingRequiredParameters.join(", ")}`
            : "Required parameters: satisfied",
          ...warnings
        ]
      });
    } catch (error) {
      if (previewRequest.controller.signal.aborted) return;
      const display = describeApiError(error, `${grid.title} preview failed.`);
      setWriterPreviewPhaseByGridId((current) => ({ ...current, [grid.id]: "error" }));
      setWriterPreviewStatus({
        id: grid.id,
        label: "Preview report-writer grid",
        state: "error",
        message: display.summary,
        details: display.details
      });
    } finally {
      if (activePreview.current === previewRequest) {
        activePreview.current = null;
        if (previewRequest.controller.signal.aborted) {
          setWriterPreviewStatus((current) => current?.id === grid.id ? null : current);
        }
      }
    }
  }

  /**
   * Auto preview render. Unlike the manual Preview button it never touches the global
   * writerPreviewStatus; outcomes go to the grid's phase and quiet error instead.
   */
  async function renderWriterAutoPreview(grid: ReportingWriterGridRow, signal: AbortSignal) {
    const settings = getWriterDraftSettings(grid);
    const dataset = resolveWriterPreviewDataset(grid);
    if (!dataset.rows) {
      setWriterPreviewPhaseByGridId((current) => ({ ...current, [grid.id]: "error" }));
      setWriterQuietPreviewErrorByGridId((current) => ({
        ...current,
        [grid.id]: { summary: `${grid.title} needs a preview dataset.`, details: [dataset.detail] }
      }));
      return;
    }

    const request = buildRenderRequest(
      grid,
      getWriterCurrentZones(grid),
      settings,
      dataset.rows,
      getWriterChartDraft(grid),
      getWriterFormatRules(grid));
    setWriterPreviewPhaseByGridId((current) => ({ ...current, [grid.id]: "rendering" }));

    try {
      const result = await renderReportTemplate(request, { signal });
      if (signal.aborted) return;
      const renderedGrid = result.grids?.find((item) => item.gridId === grid.gridId) ?? result.grids?.[0] ?? null;
      const previousPreview = getWriterPreview(grid)?.grid ?? null;
      const failed = renderedGrid?.validationChecks?.some((check) => check.status === "Failed");
      const warnings = Array.from(new Set([...(result.warnings ?? []), ...(renderedGrid?.warnings ?? [])]));
      setWriterPreviewByGridId((current) => ({
        ...current,
        [grid.id]: renderedGrid ? { grid: renderedGrid, sourceDetail: dataset.detail, source: dataset.source } : null
      }));
      setWriterPreviousPreviewByGridId((current) => ({ ...current, [grid.id]: previousPreview }));
      setWriterPreviewPhaseByGridId((current) => ({ ...current, [grid.id]: "ready" }));
      setWriterQuietPreviewErrorByGridId((current) => failed
        ? { ...current, [grid.id]: { summary: `${grid.title} preview has invalid fields.`, details: warnings } }
        : removeRecordEntry(current, grid.id));
    } catch (error) {
      if (signal.aborted || isAbortError(error)) return;
      const display = describeApiError(error, `${grid.title} preview failed.`);
      setWriterPreviewPhaseByGridId((current) => ({ ...current, [grid.id]: "error" }));
      setWriterQuietPreviewErrorByGridId((current) => ({
        ...current,
        [grid.id]: { summary: display.summary, details: display.details }
      }));
    }
  }

  useEffect(() => {
    latestWriterAutoPreviewRef.current = {
      render: renderWriterAutoPreview,
      canAutoRender: canAutoRenderWriterPreview
    };
  });

  const statusSource = writerPreviewStatus ? writerPreviewByGridId[writerPreviewStatus.id]?.source : null;
  return {
    writerDraftStatus,
    writerPreviewStatus: statusSource && !datasetSources.includes(statusSource) ? null : writerPreviewStatus,
    savingWriterDraftId,
    previewingWriterDraftId,
    getWriterPreview,
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
    addWriterFilter,
    removeWriterFilter,
    updateWriterFilter,
    addWriterFormatRule,
    removeWriterFormatRule,
    updateWriterFormatRule,
    handleWriterTokenDragStart,
    handleWriterZoneDrop,
    placeWriterToken,
    removeWriterZoneToken,
    moveWriterZoneToken,
    resetWriterGrid,
    saveWriterGridDraft,
    previewWriterGrid,
    getWriterPreviewPhase,
    isWriterAutoPreviewEnabled,
    setWriterAutoPreviewEnabled,
    isWriterAutoPreviewPausedForSize,
    getWriterQuietPreviewError,
    setWriterEditSession,
    retryWriterAutoPreview
  };
}

function buildReportWriterDatasetSourceProfile(sourceId: string): `source:${string}` {
  return `source:${sourceId}`;
}

function isReportWriterDatasetSourceProfile(value: string | null | undefined): value is `source:${string}` {
  return typeof value === "string" && value.startsWith("source:") && value.length > "source:".length;
}

function isReportWriterSampleDataset(value: string): value is ReportWriterSampleDataset {
  return Object.hasOwn(reportWriterSamples, value);
}

function resolvePreviewDataset(
  profile: ReportWriterPreviewDatasetProfile,
  sources: ReportWriterDatasetSource[],
  customText: string
): { rows: Record<string, string>[] | null; fields: ReportingWriterToken[]; source: ReportWriterDatasetSource | null; detail: string } {
  if (isReportWriterDatasetSourceProfile(profile)) {
    const source = sources.find((item) => buildReportWriterDatasetSourceProfile(item.sourceId) === profile) ?? null;
    return source && source.rows.length > 0
      ? { rows: source.rows, fields: buildReportWriterCatalogTokens(source.fields), source, detail: `Retained dataset · ${source.label}` }
      : { rows: null, fields: [], source, detail: "The selected retained dataset is unavailable. Choose another dataset to preview." };
  }
  if (isReportWriterSampleDataset(profile)) {
    const sample = reportWriterSamples[profile];
    return {
      rows: sample.rows,
      fields: buildPreviewFieldTokens(sample.rows, "Sample data"),
      source: null,
      detail: `Sample data · ${sample.label}`
    };
  }
  if (profile === "customRows") {
    const data = parseReportWriterCustomDatasetRows(customText);
    return data.error
      ? { rows: null, fields: [], source: null, detail: data.error }
      : { rows: data.rows, fields: buildPreviewFieldTokens(data.rows, "Pasted rows"), source: null, detail: "Operator-pasted preview dataset." };
  }
  return { rows: null, fields: [], source: null, detail: "Choose a dataset to see available fields and preview the grid." };
}

function buildPreviewFieldTokens(rows: Record<string, string>[], dataset: string): ReportingWriterToken[] {
  const keys = new Map<string, string>();
  for (const row of rows) {
    for (const key of Object.keys(row)) {
      const normalized = key.toLowerCase();
      if (!keys.has(normalized)) keys.set(normalized, key);
    }
  }
  return buildReportWriterCatalogTokens(Array.from(keys.values(), (name) => {
    const values = rows.map((row) => row[name]?.trim()).filter(Boolean);
    const numeric = values.length > 0 && values.every((value) => /^[+-]?(0|[1-9]\d*)(\.\d+)?$/.test(value));
    const label = name === "pnl" ? "P&L" : name
      .replace(/([a-z])([A-Z])(?=[a-z])/g, (_, left: string, right: string) => `${left} ${right.toLowerCase()}`)
      .replace(/[_-]+/g, " ")
      .replace(/^./, (character) => character.toUpperCase());
    return { name, label, role: numeric ? "metric" : "dimension", dataType: numeric ? "decimal" : "string", dataset };
  }));
}

function findIncompatibleFields(
  zones: Pick<ReportWriterZones, "rowFields" | "columnFields" | "metrics">,
  settings: ReportWriterDraftSettings,
  fields: ReportingWriterToken[]
): string[] {
  const known = new Set(fields.map((field) => (field.fieldName ?? field.label).trim().toLowerCase()));
  const selected = [
    ...zones.rowFields.map((token) => token.fieldName),
    ...zones.columnFields.map((token) => token.fieldName),
    ...zones.metrics.map((token) => token.sourceField ?? token.fieldName),
    settings.filterField,
    ...settings.extraFilters.map((filter) => filter.field)
  ];
  return [...new Set(selected.filter((field): field is string =>
    Boolean(field && field !== "*" && !known.has(field.trim().toLowerCase()))))];
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
    return value === "customRows" || isReportWriterDatasetSourceProfile(value) || isReportWriterSampleDataset(value) ? value : "";
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
    previewDataset: "",
    accessMode: "CompanyWide",
    principalKind: "Group",
    principalId: "reporting-ops",
    filterField: firstFilter?.field ?? "",
    filterOperator: normalizeReportWriterFilterOperator(firstFilter?.operator),
    filterValue: firstFilter?.value ?? "",
    // Every saved filter is carried into the draft so preview and save keep all of them.
    extraFilters: grid.filters.slice(1).map((filter) => ({
      id: `saved-${filter.id}`,
      field: filter.field,
      operator: normalizeReportWriterFilterOperator(filter.operator),
      value: filter.value ?? ""
    }))
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

export function ReportingReportWriterSection({ children }: { children: ReactNode }) {
  return (
    <section role="region" aria-label="No-code report writer">
      <Card className="panel-surface">
        <CardHeader>
          <div className="eyebrow-label">Report writer</div>
          <CardTitle>No-code grid designer</CardTitle>
          <CardDescription>Pivot, Top-N, contribution, and formula grids from template metadata.</CardDescription>
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
  preview: ReportWriterPreview | null;
  previousPreview: ReportWriterGridRender | null;
  getZoneTokens: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone) => ReportingWriterToken[];
  onTokenDragStart: (event: DragEvent<HTMLElement>, token: ReportingWriterToken, origin?: ReportWriterTokenDragOrigin | null) => void;
  onZoneDrop: (event: DragEvent<HTMLElement>, grid: ReportingWriterGridRow, zone: ReportWriterDropZone) => void;
  onFieldAdd: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone, token: ReportingWriterToken) => void;
  onTokenRemove: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone, tokenId: string) => void;
  onTokenMove: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone, tokenId: string, direction: -1 | 1) => void;
  onReset: (grid: ReportingWriterGridRow) => void;
  onSettingsChange: (grid: ReportingWriterGridRow, field: ReportWriterDraftSettingsField, value: string) => void;
  onCustomFormulaChange: (grid: ReportingWriterGridRow, field: ReportWriterCustomFormulaField, value: string) => void;
  onCustomDatasetChange: (grid: ReportingWriterGridRow, value: string) => void;
  onChartDraftChange: (grid: ReportingWriterGridRow, field: ReportWriterChartDraftField | "enabled", value: string) => void;
  onFilterAdd: (grid: ReportingWriterGridRow) => void;
  onFilterRemove: (grid: ReportingWriterGridRow, filterId: string) => void;
  onFilterChange: (grid: ReportingWriterGridRow, filterId: string, field: ReportWriterFilterDraftField, value: string) => void;
  onFormatRuleAdd: (grid: ReportingWriterGridRow) => void;
  onFormatRuleRemove: (grid: ReportingWriterGridRow, ruleId: string) => void;
  onFormatRuleChange: (grid: ReportingWriterGridRow, ruleId: string, field: keyof Omit<ReportWriterFormatRuleDraft, "id">, value: string) => void;
  onPreview: (grid: ReportingWriterGridRow) => void | Promise<void>;
  onSave: (grid: ReportingWriterGridRow) => void | Promise<void>;
  /** Auto-preview state and handlers; omit to render the designer without auto-preview controls. */
  autoPreview?: ReportWriterAutoPreviewView;
}

export interface ReportWriterAutoPreviewView {
  phase: ReportWriterPreviewPhase;
  enabled: boolean;
  pausedForSize: boolean;
  quietError: ReportWriterQuietPreviewError | null;
  onToggle: (enabled: boolean) => void;
  onRetry: () => void;
  /** True while the operator is typing in a formula or dataset field; errors wait for blur. */
  onEditSessionChange: (editing: boolean) => void;
}

function describeReportWriterAutoPreviewStatus(
  autoPreview: ReportWriterAutoPreviewView
): { label: string; variant: "outline" | "warning" | "danger" } | null {
  if (autoPreview.enabled && autoPreview.pausedForSize && autoPreview.phase === "stale") {
    return { label: "Auto preview paused — large dataset", variant: "warning" };
  }

  switch (autoPreview.phase) {
    case "rendering":
      return { label: "Updating preview…", variant: "outline" };
    case "stale":
      return autoPreview.enabled
        ? { label: "Preview out of date — updating shortly", variant: "outline" }
        : { label: "Preview out of date", variant: "warning" };
    case "error":
      return autoPreview.quietError ? { label: "Preview failed", variant: "danger" } : null;
    case "ready":
      return { label: "Preview up to date", variant: "outline" };
    default:
      return null;
  }
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
  onFieldAdd,
  onTokenRemove,
  onTokenMove,
  onReset,
  onSettingsChange,
  onCustomFormulaChange,
  onCustomDatasetChange,
  onChartDraftChange,
  onFilterAdd,
  onFilterRemove,
  onFilterChange,
  onFormatRuleAdd,
  onFormatRuleRemove,
  onFormatRuleChange,
  onPreview,
  onSave,
  autoPreview
}: ReportWriterDesignerGridProps) {
  const autoPreviewStatus = autoPreview ? describeReportWriterAutoPreviewStatus(autoPreview) : null;
  const previewDimmed = autoPreview?.phase === "stale" || autoPreview?.phase === "rendering";
  const editSessionHandlers = autoPreview
    ? {
        onFocus: () => autoPreview.onEditSessionChange(true),
        onBlur: () => autoPreview.onEditSessionChange(false)
      }
    : {};
  const accessPrincipalKindLocked = settings.accessMode !== "Restricted";
  const accessPrincipalIdDisabled = settings.accessMode === "CompanyWide";
  const accessPrincipalIdLabel = settings.accessMode === "Private" ? "Owner ID" : "Principal ID";
  const accessPolicySummary = buildReportAccessPolicySummary(settings);
  const topNDisabled = settings.gridKind !== "TopN";
  const topNLabel = buildReportWriterDraftTopNLabel(settings);
  const { previewDataset } = settings;
  const dataset = useMemo(() => resolvePreviewDataset(previewDataset, datasetSources, customDatasetText),
    [previewDataset, datasetSources, customDatasetText]);
  const sourceFields = dataset.fields;
  const selectedDatasetSource = dataset.source;
  const rowTokens = getZoneTokens(grid, "rowFields");
  const columnTokens = getZoneTokens(grid, "columnFields");
  const metricTokens = getZoneTokens(grid, "metrics");
  const formulaTokens = getZoneTokens(grid, "formulas");
  const incompatible = dataset.rows
    ? findIncompatibleFields({ rowFields: rowTokens, columnFields: columnTokens, metrics: metricTokens }, settings, sourceFields)
    : [];
  const noPreviewHost = grid.isNew && !grid.previewHost;
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
          <p className="mt-1 text-xs text-muted-foreground">{grid.isNew ? "New report · not saved yet" : `${presentReportingIdentifier(grid.templateId)} · v${grid.templateVersion}`}</p>
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
        <span className="flex flex-wrap items-center gap-1.5 font-mono text-xs text-muted-foreground">
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
          <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Draft name</span>
          <Input
            value={settings.name}
            onChange={(event) => onSettingsChange(grid, "name", event.target.value)}
            aria-label={`${grid.title} draft name`}
            className="font-mono"
          />
        </label>
        <label className="space-y-1">
          <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Display name</span>
          <Input
            value={settings.displayName}
            onChange={(event) => onSettingsChange(grid, "displayName", event.target.value)}
            aria-label={`${grid.title} draft display name`}
          />
        </label>
        <label className="space-y-1">
          <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Preview dataset</span>
          <Select
            value={settings.previewDataset}
            onChange={(event) => onSettingsChange(grid, "previewDataset", event.target.value)}
            aria-label={`${grid.title} preview dataset`}
          >
            <option value="">Choose a dataset</option>
            {isReportWriterDatasetSourceProfile(previewDataset) && !selectedDatasetSource ? (
              <option value={previewDataset} disabled>Retained dataset unavailable</option>
            ) : null}
            {datasetSources.length > 0 ? (
              <optgroup label="Retained datasets">
                {datasetSources.map((source) => (
                  <option key={source.sourceId} value={buildReportWriterDatasetSourceProfile(source.sourceId)}>
                    {source.label} ({formatCount(source.rowCount)})
                  </option>
                ))}
              </optgroup>
            ) : null}
            <option value="customRows">Custom pasted rows</option>
            <optgroup label="Sample data · preview only">
              {Object.entries(reportWriterSamples).map(([profile, sample]) => (
                <option key={profile} value={profile}>Sample {sample.label.toLowerCase()}</option>
              ))}
            </optgroup>
          </Select>
          <span className="block text-xs text-muted-foreground">
            Governed runs validate fields against the certified ledger snapshot.
          </span>
        </label>
        <div className="grid gap-2 sm:grid-cols-[1fr_0.7fr]">
          <label className="space-y-1">
            <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Grid type</span>
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
            <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Top N</span>
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
          <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Access</span>
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
            <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Principal</span>
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
            <span className={cn("text-xs text-muted-foreground", usesTrackedCaps(accessPrincipalIdLabel) && "type-label--caps")}>{accessPrincipalIdLabel}</span>
            <PrincipalIdInput
              principalKind={settings.principalKind}
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
          <span className="text-xs text-muted-foreground">Custom dataset rows</span>
          <TextArea
            value={customDatasetText}
            onChange={(event) => onCustomDatasetChange(grid, event.target.value)}
            {...editSessionHandlers}
            aria-label={`${grid.title} custom dataset rows`}
            className="min-h-28 font-mono text-xs"
            spellCheck={false}
            placeholder={'[{"sector":"Technology","strategy":"Core","marketValue":"150","pnl":"15"}]\n\nsector,strategy,marketValue,pnl\nTechnology,Core,150,15'}
          />
        </label>
      ) : null}
      {!dataset.rows || incompatible.length > 0 ? (
        <p role="status" className="mt-3 text-xs text-muted-foreground">
          {!dataset.rows ? dataset.detail : `Fields absent from this dataset · ${incompatible.join(", ")}`}
        </p>
      ) : null}
      {selectedDatasetSource && dataset.rows ? (
        <div
          className="mt-3 rounded-md border border-border/70 bg-background/30 px-2.5 py-2 text-xs leading-5 text-muted-foreground"
          aria-label={`${grid.title} retained dataset source`}
        >
          <span className="font-medium text-foreground">{selectedDatasetSource.label}</span>
          <span className="ml-2 font-mono">{formatCount(selectedDatasetSource.rowCount)} rows</span>
          <p className="mt-1">{selectedDatasetSource.description}</p>
          <div className="mt-2 grid gap-1.5 sm:grid-cols-2" role="list" aria-label={`${grid.title} retained dataset field catalog`}>
            {buildReportWriterDatasetFieldCatalog(selectedDatasetSource).map((field) => (
              <div key={field.name} role="listitem" className="rounded-sm border border-border/60 bg-secondary/25 px-2 py-1.5">
                <span className="block font-medium text-foreground">{field.label || field.name}</span>
                <span className="mt-0.5 block font-mono text-xs text-muted-foreground">
                  {field.name} · {field.role} · {field.dataType}
                </span>
                {field.description ? (
                  <span className="mt-1 block text-xs leading-4 text-muted-foreground">{field.description}</span>
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
            <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Field</span>
            <Select
              value={settings.filterField}
              onChange={(event) => onSettingsChange(grid, "filterField", event.target.value)}
              aria-label={`${grid.title} filter field`}
            >
              <option value="">No filter</option>
              {settings.filterField && !sourceFields.some((field) => field.fieldName === settings.filterField) ? (
                <option value={settings.filterField}>{settings.filterField}</option>
              ) : null}
              {sourceFields.map((field) => (
                <option key={field.id} value={field.fieldName ?? field.label}>{field.label}</option>
              ))}
            </Select>
          </label>
          <label className="space-y-1">
            <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Operator</span>
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
            <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Value</span>
            <Input
              value={settings.filterValue}
              onChange={(event) => onSettingsChange(grid, "filterValue", event.target.value)}
              aria-label={`${grid.title} filter value`}
              className="font-mono"
              disabled={!settings.filterField || isBlankFilterOperator(settings.filterOperator)}
            />
          </label>
        </div>
        <ReportWriterExtraFilters
          grid={grid}
          sourceFields={sourceFields}
          filters={settings.extraFilters}
          onFilterAdd={onFilterAdd}
          onFilterRemove={onFilterRemove}
          onFilterChange={onFilterChange}
        />
      </div>
      <div className="mt-3 grid gap-3 lg:grid-cols-[0.85fr_1.15fr]">
        <div>
          <div className="eyebrow-label">Fields</div>
          <div role="list" aria-label={`${grid.title} source fields`} className="mt-2 flex flex-wrap gap-1.5">
            {sourceFields.map((token) => (
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
            sourceFields={sourceFields}
            onFieldAdd={onFieldAdd}
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
            sourceFields={sourceFields}
            onFieldAdd={onFieldAdd}
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
            sourceFields={sourceFields}
            onFieldAdd={onFieldAdd}
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
            <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Name</span>
            <Input
              value={customFormula.name}
              onChange={(event) => onCustomFormulaChange(grid, "name", event.target.value)}
              aria-label={`${grid.title} custom formula name`}
              className="font-mono"
            />
          </label>
          <label className="space-y-1">
            <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Label</span>
            <Input
              value={customFormula.label}
              onChange={(event) => onCustomFormulaChange(grid, "label", event.target.value)}
              aria-label={`${grid.title} custom formula label`}
            />
          </label>
          <label className="space-y-1">
            <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Expression</span>
            <Input
              value={customFormula.expression}
              onChange={(event) => onCustomFormulaChange(grid, "expression", event.target.value)}
              {...editSessionHandlers}
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
                  <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Column</span>
                  <Select
                    value={rule.column}
                    onChange={(event) => onFormatRuleChange(grid, rule.id, "column", event.target.value)}
                    aria-label={`Formatting rule column for ${grid.title}`}
                  >
                    <option value="">Select column</option>
                    {sourceFields.map((field) => (
                      <option key={field.id} value={field.fieldName ?? field.label}>{field.label}</option>
                    ))}
                  </Select>
                </label>
                <label className="space-y-1">
                  <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Operator</span>
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
                  <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Value</span>
                  <Input
                    value={rule.value}
                    onChange={(event) => onFormatRuleChange(grid, rule.id, "value", event.target.value)}
                    aria-label={`Formatting rule value for ${grid.title}`}
                    className="font-mono"
                    disabled={isBlankFilterOperator(rule.operator)}
                  />
                </label>
                <label className="space-y-1">
                  <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Style</span>
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
              <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Type</span>
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
              <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Category field</span>
              <Select
                value={chartDraft.categoryField}
                onChange={(event) => onChartDraftChange(grid, "categoryField", event.target.value)}
                aria-label={`Chart category field for ${grid.title}`}
              >
                <option value="">Select field</option>
                {sourceFields.map((field) => (
                  <option key={field.id} value={field.fieldName ?? field.label}>{field.label}</option>
                ))}
              </Select>
            </label>
            <label className="space-y-1">
              <span className="text-xs text-muted-foreground">Value columns (comma-separated)</span>
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
      <div className="mt-3 flex flex-wrap items-center justify-end gap-2">
        {autoPreview ? (
          <>
            <div role="status" aria-live="polite" aria-label={`${grid.title} preview status`} className="mr-auto min-h-6">
              {autoPreviewStatus ? <Badge variant={autoPreviewStatus.variant}>{autoPreviewStatus.label}</Badge> : null}
            </div>
            <Checkbox
              checked={autoPreview.enabled}
              onCheckedChange={(checked) => autoPreview.onToggle(checked === true)}
              label="Auto preview"
              aria-label={`Auto preview ${grid.title} report-writer grid`}
            />
          </>
        ) : null}
        <Button
          type="button"
          size="sm"
          variant="secondary"
          aria-label={`Preview ${grid.title} report-writer grid`}
          disabled={isPreviewing || !dataset.rows || noPreviewHost}
          disabledReason={!dataset.rows ? dataset.detail : noPreviewHost ? "An accessible template is required to preview the grid." : undefined}
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
          aria-label={`Save ${grid.title} as report template draft`}
          disabled={isSaving}
          busy={isSaving}
          busyLabel="Saving"
          onClick={() => void onSave(grid)}
        >
          <PencilLine className="h-4 w-4" aria-hidden="true" />
          Save draft
        </Button>
      </div>
      {autoPreview?.quietError ? (
        <div
          className="mt-3 flex flex-wrap items-start justify-between gap-2 rounded-md border border-destructive/40 bg-destructive/5 px-2.5 py-2 text-xs"
          aria-label={`${grid.title} auto preview error`}
        >
          <div className="min-w-0">
            <p className="font-medium text-foreground">{autoPreview.quietError.summary}</p>
            {autoPreview.quietError.details.map((detail) => (
              <p key={detail} className="mt-0.5 text-muted-foreground">{detail}</p>
            ))}
          </div>
          <Button
            type="button"
            size="sm"
            variant="secondary"
            aria-label={`Retry ${grid.title} auto preview`}
            onClick={autoPreview.onRetry}
          >
            <RotateCcw className="h-4 w-4" aria-hidden="true" />
            Retry
          </Button>
        </div>
      ) : null}
      {preview ? (
        <ReportWriterPreviewTable grid={grid} preview={preview} previousPreview={previousPreview} dimmed={previewDimmed} />
      ) : null}
    </div>
  );
}

function ReportWriterPreviewTable({
  grid,
  preview: rendered,
  previousPreview,
  dimmed = false
}: {
  grid: ReportingWriterGridRow;
  preview: ReportWriterPreview;
  previousPreview?: ReportWriterGridRender | null;
  /** The preview no longer matches the designer; it stays visible but de-emphasised. */
  dimmed?: boolean;
}) {
  const proofId = useId();
  const preview = rendered.grid;
  const rows = preview.chart ? preview.rows : preview.rows.slice(0, 5);
  const chartKeys = preview.chart ? [preview.chart.categoryField, ...preview.chart.series.map(({ key }) => key)] : [];
  const proofColumns = [...new Set(chartKeys)].filter((key) => !preview.columns.some((column) => column.key.toLowerCase() === key.toLowerCase()));
  const columns = [...preview.columns, ...proofColumns.map((key) => ({ key, label: key, role: "source field" }))];
  const lineage = preview.lineage;
  const dataDictionary = preview.dataDictionary ?? [];
  const validationChecks = preview.validationChecks ?? [];
  const comparison = previousPreview ? buildReportWriterGridDiff(previousPreview, preview) : null;
  const comparisonChangeCount = comparison
    ? comparison.addedRowCount + comparison.removedRowCount + comparison.changedRowCount
    : 0;
  return (
    <div
      className={cn("mt-3 rounded-md border border-border/70 bg-background/35 px-2.5 py-2 transition-opacity", dimmed && "opacity-60")}
      aria-label={`${grid.title} live preview`}
      aria-busy={dimmed || undefined}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <div className="eyebrow-label">Live preview</div>
          <p className="mt-1 text-xs text-muted-foreground">{preview.title} · {preview.rows.length} row{preview.rows.length === 1 ? "" : "s"}</p>
          <p className="mt-1 text-xs text-muted-foreground">{rendered.sourceDetail}</p>
        </div>
        <Badge variant="outline">{preview.kind}</Badge>
      </div>
      {preview.warnings.length > 0 ? (
        <ul aria-label={`${grid.title} preview warnings`} className="mt-2 space-y-1 text-xs text-warning">
          {preview.warnings.map((warning) => (
            <li key={warning}>{warning}</li>
          ))}
        </ul>
      ) : null}
      <div className="mt-2 max-h-56 overflow-auto rounded-sm border border-border/60" role="region" aria-label={`${grid.title} preview table scroll area`} tabIndex={0}>
        <table className="min-w-full table-fixed text-left text-xs" aria-label={`${grid.title} preview rows`}>
          <thead className="bg-secondary/40 text-xs uppercase tracking-[0.12em] text-muted-foreground">
            <tr>
            {columns.map((column) => (
                <th key={column.key} scope="col" className="min-w-28 px-2 py-1.5 font-semibold">
                  <span className="block truncate" title={column.label}>{column.label}</span>
                  <span className="mt-0.5 block truncate font-mono text-xs normal-case tracking-normal" title={column.role}>
                    {column.role}
                  </span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length > 0 ? rows.map((row, index) => (
              <tr key={`${row.rowKey}:${index}`} id={`${proofId}-row-${index}`} tabIndex={-1} className="border-t border-border/50 target:bg-secondary/60">
                {columns.map((column) => (
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
                <td className="px-2 py-2 text-muted-foreground" colSpan={Math.max(columns.length, 1)}>
                  No rows returned.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
      {preview.chart ? (
        <ReportWriterChartPreview chart={preview.chart} grid={preview} proofId={proofId} sourceDetail={rendered.sourceDetail} />
      ) : null}
      {comparison ? (
        <details className="mt-2 rounded-sm border border-border/60 bg-secondary/20 px-2 py-1.5 text-xs">
          <summary className="cursor-pointer select-none text-xs font-semibold text-foreground">
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
              <dt className="text-xs uppercase tracking-[0.12em] text-muted-foreground">Rows</dt>
              <dd className="mt-1 font-mono text-foreground">
                {lineage.inputRowCount} input / {lineage.filteredInputRowCount ?? lineage.inputRowCount} filtered / {lineage.outputRowCount} output
              </dd>
            </div>
            <div>
              <dt className="text-xs uppercase tracking-[0.12em] text-muted-foreground">Sources</dt>
              <dd className="mt-1 break-words font-mono text-foreground">{lineage.sourceFields.length > 0 ? lineage.sourceFields.join(", ") : "None"}</dd>
            </div>
            <div>
              <dt className="text-xs uppercase tracking-[0.12em] text-muted-foreground">Metrics</dt>
              <dd className="mt-1 break-words font-mono text-foreground">
                {lineage.metrics.length > 0
                  ? lineage.metrics.map((metric) => `${metric.name}=${metric.function}(${metric.sourceField})`).join(", ")
                  : "None"}
              </dd>
            </div>
            <div>
              <dt className="text-xs uppercase tracking-[0.12em] text-muted-foreground">Formulas</dt>
              <dd className="mt-1 break-words font-mono text-foreground">
                {lineage.formulas.length > 0
                  ? lineage.formulas.map((formula) => `${formula.name}=${formula.expression} [${formula.sourceFields.join(", ")}]`).join(", ")
                  : "None"}
              </dd>
            </div>
            <div>
              <dt className="text-xs uppercase tracking-[0.12em] text-muted-foreground">Filters</dt>
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
                <p className="mt-1 break-words text-xs text-muted-foreground">
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
                  <span className="mt-1 block break-words text-xs text-muted-foreground">{check.detail}</span>
                </span>
              </li>
            ))}
          </ul>
        </div>
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
  sourceFields?: ReportingWriterToken[];
  onFieldAdd?: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone, token: ReportingWriterToken) => void;
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
  sourceFields,
  onFieldAdd,
  onTokenDragStart,
  onZoneDrop,
  onTokenRemove,
  onTokenMove
}: ReportWriterDropZoneViewProps) {
  const fields = sourceFields?.filter((field) => zone !== "metrics" || field.kind !== "formula") ?? [];
  return (
    // The zone is a labelled drop target; its tokens form a list only when there are tokens, so
    // an empty zone is not announced as a list with no items (axe aria-required-children).
    <div
      role="group"
      aria-label={`${grid.title} ${label} drop zone`}
      className="min-h-24 rounded-md border border-dashed border-border/70 bg-background/25 px-2.5 py-2"
      onDragOver={(event) => event.preventDefault()}
      onDrop={(event) => onZoneDrop(event, grid, zone)}
    >
      <div className={cn("mb-2 text-xs text-muted-foreground", usesTrackedCaps(label) && "type-label--caps")}>{label}</div>
      <div
        role={tokens.length > 0 ? "list" : undefined}
        aria-label={tokens.length > 0 ? `${grid.title} ${label}` : undefined}
        className="flex flex-wrap gap-1.5"
      >
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
      {fields.length > 0 && onFieldAdd ? (
        <Select
          className="mt-2"
          aria-label={`Add field to ${grid.title} ${label}`}
          value=""
          onChange={(event) => {
            const field = fields.find((item) => item.id === event.target.value);
            if (field) onFieldAdd(grid, zone, field);
          }}
        >
          <option value="">Add field</option>
          {fields.map((field) => (
            <option key={field.id} value={field.id}
              disabled={tokens.some((token) => token.fieldName?.toLowerCase() === field.fieldName?.toLowerCase())}>
              {field.label}
            </option>
          ))}
        </Select>
      ) : null}
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
      className="inline-flex min-w-0 max-w-full flex-wrap items-center gap-1.5 rounded-sm border border-border/70 bg-secondary/35 px-2 py-1 text-xs text-foreground"
      title={token.detail}
    >
      <span className={cn("inline-flex min-w-0 flex-wrap items-center gap-1.5", (onMove || onRemove) && "w-full")}>
        <GripVertical className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
        <Badge variant={token.kind === "formula" ? "warning" : token.kind === "metric" ? "success" : "outline"}>{token.kind}</Badge>
        <span className="max-w-full break-all font-mono">{token.label}</span>
      </span>
      {(onMove || onRemove) ? (
        <span className="inline-flex shrink-0 items-center gap-0.5">
          {onMove ? (
            <>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="shrink-0"
                style={{ width: 24, height: 24 }}
                aria-label={`Move ${token.label} left`}
                disabled={!canMovePrevious}
                disabledReason="This token is already first in the list."
                onClick={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  onMove(-1);
                }}
              >
                <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="shrink-0"
                style={{ width: 24, height: 24 }}
                aria-label={`Move ${token.label} right`}
                disabled={!canMoveNext}
                disabledReason="This token is already last in the list."
                onClick={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  onMove(1);
                }}
              >
                <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
              </Button>
            </>
          ) : null}
          {onRemove ? (
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="shrink-0"
              style={{ width: 24, height: 24 }}
              aria-label={`Remove ${token.label}`}
              onClick={(event) => {
                event.preventDefault();
                event.stopPropagation();
                onRemove();
              }}
            >
              <XCircle className="h-3.5 w-3.5" aria-hidden="true" />
            </Button>
          ) : null}
        </span>
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
