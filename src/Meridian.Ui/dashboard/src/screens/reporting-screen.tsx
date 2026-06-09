import { type DragEvent, type KeyboardEvent, useEffect, useRef, useState } from "react";
import { CheckCircle2, Eye, FileText, Filter, GripVertical, Landmark, Network, PencilLine, RotateCcw, Send, XCircle } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { MetricCard } from "@/components/meridian/metric-card";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import {
  apiPostJson,
  approveReportTemplateDraft,
  createReportTemplateDraft,
  deliverReportPack,
  generateReportPack,
  pauseReportingSchedule,
  rejectReportTemplateDraft,
  renderReportTemplate,
  resumeReportingSchedule,
  runReportingNow,
  runReportingScheduleNow,
  saveReportingSchedule,
  submitReportTemplateDraft
} from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { cn } from "@/lib/utils";
import {
  resolveReportPackProfileKeyCommand,
  useReportingScreenViewModel,
  type ReportingProfileRow,
  type ReportingRunActionRow,
  type ReportingRunStatusRow,
  type ReportingScheduleRow,
  type ReportingTemplateLifecycleActionRow,
  type ReportingTemplateRow,
  type ReportingWriterGridRow,
  type ReportingWriterToken
} from "@/screens/reporting-screen.view-model";
import type {
  AccountingWorkspaceResponse,
  GovernanceReportArtifactFormat,
  ReportAccessMode,
  ReportAccessPrincipalKind,
  ReportBrandingTheme,
  ReportPackDeliveryMode,
  ReportTemplateDecisionRequest,
  ReportTemplateDraftRequest,
  ReportWriterAggregateFunction,
  ReportWriterFilterDefinition,
  ReportWriterFilterOperator,
  ReportWriterGridDefinition,
  ReportWriterGridKind,
  ReportWriterGridRender,
  ReportWriterMetricDefinition,
  RenderReportTemplateRequest,
  ReportingScheduleUpsertRequest,
  ReportingWorkflowEvidenceLink
} from "@/types";

interface ReportingScreenProps {
  data: AccountingWorkspaceResponse | null;
}

interface ReportingCommandStatus {
  id: string;
  label: string;
  state: "running" | "success" | "error";
  message: string;
  details: string[];
}

type ReportWriterDropZone = "rowFields" | "columnFields" | "metrics" | "formulas";
type ReportWriterDraftState = Partial<Record<ReportWriterDropZone, ReportingWriterToken[]>>;
type ReportingScheduleArtifactFormat = Extract<GovernanceReportArtifactFormat, "Pdf" | "Xlsx" | "Csv">;
type ReportWriterDraftSettingsField =
  | "name"
  | "displayName"
  | "accessMode"
  | "principalKind"
  | "principalId"
  | "filterField"
  | "filterOperator"
  | "filterValue";
type ReportWriterCustomFormulaField = "name" | "label" | "expression";
type ReportingScheduleDraftField =
  | "scheduleId"
  | "templateId"
  | "cronExpression"
  | "nextAsOfDate"
  | "dueAtUtc"
  | "maxRetries"
  | "requestedBy"
  | "description"
  | "distributionId"
  | "deliveryMode"
  | "deliveryNote";

interface ReportWriterDraftSettings {
  name: string;
  displayName: string;
  accessMode: ReportAccessMode;
  principalKind: ReportAccessPrincipalKind;
  principalId: string;
  filterField: string;
  filterOperator: ReportWriterFilterOperator;
  filterValue: string;
}

interface ReportWriterCustomFormulaDraft {
  name: string;
  label: string;
  expression: string;
}

interface ReportingScheduleDraftState {
  scheduleId: string;
  templateId: string;
  cronExpression: string;
  nextAsOfDate: string;
  dueAtUtc: string;
  maxRetries: string;
  requestedBy: string;
  description: string;
  distributionId: string;
  deliveryMode: ReportPackDeliveryMode;
  deliveryNote: string;
  formats: Record<ReportingScheduleArtifactFormat, boolean>;
}

const reportingScheduleArtifactFormats: ReportingScheduleArtifactFormat[] = ["Pdf", "Xlsx", "Csv"];
const reportingScheduleDeliveryModes: ReportPackDeliveryMode[] = ["SecurePortal", "EmailLink", "EvidenceVault", "InternalRoute"];

const reportingProfileColumns: DenseDataTableColumn<ReportingProfileRow>[] = [
  {
    id: "profile",
    label: "Profile",
    render: (profile) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{profile.name}</span>
        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{profile.id}</span>
      </span>
    )
  },
  {
    id: "target",
    label: "Target",
    render: (profile) => (
      <span className="block min-w-0">
        <span className="block font-medium text-foreground">{profile.targetLabel}</span>
        <span className="mt-1 block break-words text-xs leading-5 text-muted-foreground">{profile.description}</span>
      </span>
    )
  },
  {
    id: "format",
    label: "Format",
    render: (profile) => <span className="font-mono text-xs text-foreground">{profile.formatLabel}</span>
  },
  {
    id: "evidence",
    label: "Evidence",
    render: (profile) => (
      <span className="flex flex-wrap gap-1.5">
        {profile.badges.length > 0 ? profile.badges.map((badge) => (
          <Badge key={badge.label} variant={badge.variant}>
            {badge.label}
          </Badge>
        )) : (
          <Badge variant="outline">Evidence pending</Badge>
        )}
      </span>
    )
  }
];

export function ReportingScreen({ data }: ReportingScreenProps) {
  const { pathname } = useLocation();
  const vm = useReportingScreenViewModel(data?.reporting ?? null, undefined, pathname);
  const reportPackProfileButtonRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const shouldFocusReportPackProfile = useRef(false);
  const [runActionStatus, setRunActionStatus] = useState<ReportingCommandStatus | null>(null);
  const [templateRunStatus, setTemplateRunStatus] = useState<ReportingCommandStatus | null>(null);
  const [templateLifecycleStatus, setTemplateLifecycleStatus] = useState<ReportingCommandStatus | null>(null);
  const [scheduleActionStatus, setScheduleActionStatus] = useState<ReportingCommandStatus | null>(null);
  const [scheduleDraft, setScheduleDraft] = useState<ReportingScheduleDraftState>(() => buildDefaultReportingScheduleDraft(data?.reporting ?? null));
  const [writerDrafts, setWriterDrafts] = useState<Record<string, ReportWriterDraftState>>({});
  const [writerDraftSettings, setWriterDraftSettings] = useState<Record<string, Partial<ReportWriterDraftSettings>>>({});
  const [writerCustomFormulas, setWriterCustomFormulas] = useState<Record<string, Partial<ReportWriterCustomFormulaDraft>>>({});
  const [writerDraftStatus, setWriterDraftStatus] = useState<ReportingCommandStatus | null>(null);
  const [writerPreviewStatus, setWriterPreviewStatus] = useState<ReportingCommandStatus | null>(null);
  const [brandingPackStatus, setBrandingPackStatus] = useState<ReportingCommandStatus | null>(null);
  const [writerPreviewByGridId, setWriterPreviewByGridId] = useState<Record<string, ReportWriterGridRender | null>>({});
  const runningRunActionId = runActionStatus?.state === "running" ? runActionStatus.id : null;
  const runningTemplateRunId = templateRunStatus?.state === "running" ? templateRunStatus.id : null;
  const runningTemplateLifecycleActionId = templateLifecycleStatus?.state === "running" ? templateLifecycleStatus.id : null;
  const runningScheduleActionId = scheduleActionStatus?.state === "running" ? scheduleActionStatus.id : null;
  const savingWriterDraftId = writerDraftStatus?.state === "running" ? writerDraftStatus.id : null;
  const previewingWriterDraftId = writerPreviewStatus?.state === "running" ? writerPreviewStatus.id : null;
  const runningBrandingThemeId = brandingPackStatus?.state === "running" ? brandingPackStatus.id : null;
  const reportingFundProfileId = resolveReportingFundProfileId(data?.reporting ?? null);
  const writerGrids = vm.templateRows.flatMap((template) => template.writerGrids);
  const scheduleDistributionOptions = data?.reporting.reportPackDistributions ?? [];

  useEffect(() => {
    if (!shouldFocusReportPackProfile.current) {
      return;
    }

    shouldFocusReportPackProfile.current = false;
    const selectedProfileId = vm.workflowTaskPanel?.selectedProfileId;
    if (selectedProfileId) {
      reportPackProfileButtonRefs.current[selectedProfileId]?.focus();
    }
  }, [vm.workflowTaskPanel?.selectedProfileId]);

  function handleReportPackProfileKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const command = resolveReportPackProfileKeyCommand(event.key);
    if (!command) {
      return;
    }

    event.preventDefault();
    shouldFocusReportPackProfile.current = true;
    vm.selectAdjacentReportPackProfile(command);
  }

  async function handleRunAction(run: ReportingRunStatusRow, action: ReportingRunActionRow) {
    if (!action.isEnabled || action.method !== "POST" || runningRunActionId) {
      return;
    }

    if (action.kind === "restatement") {
      setRunActionStatus({
        id: action.id,
        label: action.label,
        state: "error",
        message: "Restatement requires changed-line evidence before it can be submitted.",
        details: ["Open the report-pack workflow and attach changed report lines with retained evidence."]
      });
      return;
    }

    setRunActionStatus({
      id: action.id,
      label: action.label,
      state: "running",
      message: `${action.label} is running.`,
      details: []
    });

    try {
      await executeRunAction(run, action);
      setRunActionStatus({
        id: action.id,
        label: action.label,
        state: "success",
        message: `${action.label} completed.`,
        details: []
      });
    } catch (error) {
      const display = describeApiError(error, `${action.label} failed.`);
      setRunActionStatus({
        id: action.id,
        label: action.label,
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
  }

  async function handleTemplateRun(template: ReportingTemplateRow) {
    if (!template.canRunOnDemand || runningTemplateRunId) {
      return;
    }

    setTemplateRunStatus({
      id: template.id,
      label: template.runActionLabel,
      state: "running",
      message: `${template.name} is running.`,
      details: []
    });

    try {
      const result = await runReportingNow({
        templateId: template.templateName,
        asOfDate: new Date().toISOString().slice(0, 10),
        maxRetries: 0
      });
      setTemplateRunStatus({
        id: template.id,
        label: template.runActionLabel,
        state: "success",
        message: `${template.name} run created.`,
        details: [`Run ID: ${result.run.runId}`, `Status: ${result.run.status}`]
      });
    } catch (error) {
      const display = describeApiError(error, `${template.name} run failed.`);
      setTemplateRunStatus({
        id: template.id,
        label: template.runActionLabel,
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
  }

  async function handleTemplateLifecycleAction(
    template: ReportingTemplateRow,
    action: ReportingTemplateLifecycleActionRow
  ) {
    if (!action.isEnabled || runningTemplateLifecycleActionId) {
      return;
    }

    setTemplateLifecycleStatus({
      id: action.id,
      label: "Report template lifecycle",
      state: "running",
      message: `${action.label} is running for ${template.name}.`,
      details: []
    });

    try {
      const request = buildReportTemplateDecisionRequest(template, action);
      const result = await executeTemplateLifecycleAction(template, action, request);
      setTemplateLifecycleStatus({
        id: action.id,
        label: "Report template lifecycle",
        state: "success",
        message: `${template.name} moved to ${result.status}.`,
        details: [
          `Template: ${result.definition.templateId.name}@v${result.definition.templateId.version}`,
          `Action: ${action.label}`,
          result.decisionRationale ? `Rationale: ${result.decisionRationale}` : `Target: ${action.targetStatus}`
        ]
      });
    } catch (error) {
      const display = describeApiError(error, `${action.label} ${template.name} failed.`);
      setTemplateLifecycleStatus({
        id: action.id,
        label: "Report template lifecycle",
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
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

  function updateWriterDraftSetting(
    grid: ReportingWriterGridRow,
    field: ReportWriterDraftSettingsField,
    value: string
  ) {
    setWriterDraftSettings((current) => ({
      ...current,
      [grid.id]: {
        ...current[grid.id],
        [field]: value
      }
    }));
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

  function handleWriterTokenDragStart(event: DragEvent<HTMLElement>, token: ReportingWriterToken) {
    event.dataTransfer.effectAllowed = "copy";
    event.dataTransfer.setData("application/x-meridian-report-writer-token", JSON.stringify(token));
  }

  function handleWriterZoneDrop(event: DragEvent<HTMLElement>, grid: ReportingWriterGridRow, zone: ReportWriterDropZone) {
    event.preventDefault();
    const payload = event.dataTransfer.getData("application/x-meridian-report-writer-token");
    if (!payload) {
      return;
    }

    let token: ReportingWriterToken;
    try {
      token = JSON.parse(payload) as ReportingWriterToken;
    } catch {
      return;
    }

    setWriterDrafts((current) => {
      const existing = current[grid.id]?.[zone] ?? grid[zone];
      if (existing.some((item) => item.id === token.id)) {
        return current;
      }

      return {
        ...current,
        [grid.id]: {
          ...current[grid.id],
          [zone]: [...existing, token]
        }
      };
    });
    setWriterPreviewByGridId((current) => clearWriterPreview(current, grid.id));
  }

  function resetWriterGrid(grid: ReportingWriterGridRow) {
    setWriterDrafts((current) => {
      if (!current[grid.id]) {
        return current;
      }

      const next = { ...current };
      delete next[grid.id];
      return next;
    });
    setWriterPreviewByGridId((current) => clearWriterPreview(current, grid.id));
    setWriterCustomFormulas((current) => {
      if (!current[grid.id]) {
        return current;
      }

      const next = { ...current };
      delete next[grid.id];
      return next;
    });
    setWriterDraftSettings((current) => {
      if (!current[grid.id]) {
        return current;
      }

      const next = { ...current };
      delete next[grid.id];
      return next;
    });
  }

  function getWriterCurrentZones(grid: ReportingWriterGridRow): Record<ReportWriterDropZone, ReportingWriterToken[]> {
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
    const request = buildReportTemplateDraftRequest(grid, settings, getWriterCurrentZones(grid));

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

    const request = buildRenderReportTemplateRequest(grid, getWriterCurrentZones(grid), getWriterDraftSettings(grid));
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
      setWriterPreviewByGridId((current) => ({
        ...current,
        [grid.id]: renderedGrid
      }));
      setWriterPreviewStatus({
        id: grid.id,
        label: "Preview report-writer grid",
        state: "success",
        message: `${grid.title} preview rendered.`,
        details: [
          `Template: ${result.templateId.name}@v${result.templateId.version}`,
          `Rows: ${renderedGrid?.rows.length ?? 0}`,
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

  async function handleScheduleAction(schedule: ReportingScheduleRow, action: "pause" | "resume" | "run") {
    const statusId = `${schedule.id}:${action}`;
    if (runningScheduleActionId) {
      return;
    }

    const label = action === "run"
      ? `Run ${schedule.id}`
      : action === "pause"
        ? `Pause ${schedule.id}`
        : `Resume ${schedule.id}`;
    setScheduleActionStatus({
      id: statusId,
      label,
      state: "running",
      message: `${label} is running.`,
      details: []
    });

    try {
      let details: string[] = [];
      if (action === "pause") {
        await pauseReportingSchedule(schedule.id);
      } else if (action === "resume") {
        await resumeReportingSchedule(schedule.id);
      } else {
        const result = await runReportingScheduleNow(schedule.id);
        details = [
          `Run ID: ${result.run.runId}`,
          `Deliveries: ${result.deliveryAttempts?.length ?? 0}`,
          ...(result.deliveryWarnings ?? []).map((warning) => `Delivery warning: ${warning}`)
        ];
      }

      setScheduleActionStatus({
        id: statusId,
        label,
        state: "success",
        message: `${label} completed.`,
        details
      });
    } catch (error) {
      const display = describeApiError(error, `${label} failed.`);
      setScheduleActionStatus({
        id: statusId,
        label,
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
  }

  function updateScheduleDraft(field: ReportingScheduleDraftField, value: string) {
    setScheduleDraft((current) => ({
      ...current,
      [field]: field === "deliveryMode" ? normalizeReportingScheduleDeliveryMode(value) : value
    } as ReportingScheduleDraftState));
  }

  function toggleScheduleDraftFormat(format: ReportingScheduleArtifactFormat, isSelected: boolean) {
    setScheduleDraft((current) => ({
      ...current,
      formats: {
        ...current.formats,
        [format]: isSelected
      }
    }));
  }

  async function saveScheduleDraft() {
    const statusId = "schedule-draft:save";
    if (runningScheduleActionId) {
      return;
    }

    const request = buildReportingScheduleUpsertRequest(scheduleDraft);
    const formats = request.deliveryTargets?.[0]?.formats ?? [];
    if (formats.length === 0) {
      setScheduleActionStatus({
        id: statusId,
        label: "Save reporting schedule",
        state: "error",
        message: "Select at least one report artifact format before saving the schedule.",
        details: ["PDF, XLSX, or CSV must be selected for scheduled delivery."]
      });
      return;
    }

    setScheduleActionStatus({
      id: statusId,
      label: "Save reporting schedule",
      state: "running",
      message: `${request.scheduleId} is saving.`,
      details: []
    });

    try {
      const result = await saveReportingSchedule(request);
      const target = result.deliveryTargets?.[0] ?? request.deliveryTargets?.[0] ?? null;
      setScheduleActionStatus({
        id: statusId,
        label: "Save reporting schedule",
        state: "success",
        message: `Reporting schedule ${result.scheduleId} saved.`,
        details: [
          `Template: ${result.templateId}`,
          target ? `Delivery: ${target.distributionId} via ${target.deliveryMode ?? "SecurePortal"}` : "Delivery: no target",
          `Formats: ${(target?.formats ?? formats).join(", ")}`
        ]
      });
    } catch (error) {
      const display = describeApiError(error, `${request.scheduleId} save failed.`);
      setScheduleActionStatus({
        id: statusId,
        label: "Save reporting schedule",
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
  }

  async function handleGenerateBrandedPack(theme: ReportBrandingTheme) {
    if (!reportingFundProfileId || runningBrandingThemeId) {
      return;
    }

    setBrandingPackStatus({
      id: theme.themeId,
      label: "Generate branded report pack",
      state: "running",
      message: `${theme.name} report pack is generating.`,
      details: []
    });

    try {
      const result = await generateReportPack({
        fundProfileId: reportingFundProfileId,
        auditActor: "browser.reporting",
        reportKind: "BoardPacket",
        formats: ["Pdf", "Xlsx", "Csv"],
        brandingThemeId: theme.themeId,
        decisionRationale: `Generated from Reporting branding theme ${theme.name}.`
      });

      setBrandingPackStatus({
        id: theme.themeId,
        label: "Generate branded report pack",
        state: "success",
        message: `${theme.name} report pack generated.`,
        details: [
          `Report ID: ${result.reportId}`,
          `Artifacts: ${result.artifacts.length}`,
          `Theme: ${result.brandingTheme?.name ?? theme.name}`
        ]
      });
    } catch (error) {
      const display = describeApiError(error, `${theme.name} report pack generation failed.`);
      setBrandingPackStatus({
        id: theme.themeId,
        label: "Generate branded report pack",
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
  }

  if (!data) {
    return (
      <Card
        role={vm.loadingState.role}
        aria-busy={vm.loadingState.ariaBusy}
        aria-live={vm.loadingState.ariaLive}
        aria-labelledby={vm.loadingState.titleId}
        aria-describedby={vm.loadingState.detailId}
        className="panel-surface border-[var(--state-pending-bd)] bg-[var(--state-pending-bg)]"
      >
        <CardHeader className="space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <Badge
              variant="outline"
              className="border-[var(--state-pending-bd)] bg-[var(--state-pending-bg)] text-[var(--state-pending-fg)]"
              dot
            >
              {vm.loadingState.badgeLabel}
            </Badge>
            <ReportingChip label="Route" value={vm.loadingState.routeLabel} />
          </div>
          <CardTitle id={vm.loadingState.titleId}>{vm.loadingState.title}</CardTitle>
          <CardDescription id={vm.loadingState.detailId}>{vm.loadingState.detail}</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <div className="space-y-8">
      <section
        role="region"
        aria-label="Reporting workbench context"
        className="panel-surface-strong flex flex-wrap items-center justify-between gap-3 px-4 py-4"
      >
        <div className="min-w-0">
          <div className="eyebrow-label">Reporting lane</div>
          <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            Governed export workbench
          </h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            Report packs, export routes, and review evidence stay in one cockpit so governed output can be
            checked before it leaves Reporting.
          </p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          {vm.workbenchActions.map((action) => (
            <Button key={action.id} asChild variant="outline" size="sm">
              <Link to={action.href} aria-label={action.ariaLabel}>
                <Network className="h-4 w-4" aria-hidden="true" />
                {action.label}
              </Link>
            </Button>
          ))}
          {vm.workbenchChips.map((chip) => (
            <ReportingChip key={chip.label} label={chip.label} value={chip.value} />
          ))}
        </div>
      </section>

      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => (
          <MetricCard key={metric.id} {...metric} />
        ))}
      </section>

      {(data.reporting.portfolioCuts ?? []).length > 0 ? (
        <section role="region" aria-label="Portfolio reporting cuts">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Portfolio cuts</div>
              <CardTitle>Exposure, cash, P&L, and shadow NAV</CardTitle>
              <CardDescription>Fund, strategy, and tag views are projected from the shared portfolio and NAV reporting payload.</CardDescription>
            </CardHeader>
            <CardContent>
              <div role="list" aria-label="Portfolio reporting cut rows" className="grid gap-3 lg:grid-cols-3">
                {(data.reporting.portfolioCuts ?? []).slice(0, 6).map((cut) => (
                  <div
                    key={cut.cutId}
                    role="listitem"
                    aria-label={`${cut.label} ${cut.kind} portfolio reporting cut`}
                    className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <span className="min-w-0">
                        <span className="block font-semibold text-foreground">{cut.label}</span>
                        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{cut.cutId}</span>
                      </span>
                      <Badge variant="outline">{cut.kind}</Badge>
                    </div>
                    <dl className="mt-3 grid grid-cols-2 gap-x-3 gap-y-2 text-xs">
                      <ReportingCutMetric label="Gross" value={formatReportingMoney(cut.grossExposure, cut.currency)} />
                      <ReportingCutMetric label="Net" value={formatReportingMoney(cut.netExposure, cut.currency)} />
                      <ReportingCutMetric label="Cash" value={formatReportingMoney(cut.totalCash, cut.currency)} />
                      <ReportingCutMetric label="P&L" value={formatReportingMoney(cut.totalPnl, cut.currency)} />
                      <ReportingCutMetric label="Shadow NAV" value={formatReportingMoney(cut.shadowNav, cut.currency)} />
                      <ReportingCutMetric label="Variance" value={formatReportingMoney(cut.shadowNavVariance, cut.currency)} />
                    </dl>
                    <p className="mt-2 text-xs text-muted-foreground">
                      {cut.sourceCount} source{cut.sourceCount === 1 ? "" : "s"} · {cut.versionStamp ?? cut.asOf}
                    </p>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        </section>
      ) : null}

      {(data.reporting.livePortfolioViews ?? []).length > 0 ? (
        <section role="region" aria-label="Live portfolio views">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Live views</div>
              <CardTitle>Tick-linked portfolio reporting</CardTitle>
              <CardDescription>Reporting cuts carry shared live-summary routes, source freshness, liquidity, and cash-ladder evidence.</CardDescription>
            </CardHeader>
            <CardContent>
              <div role="list" aria-label="Live portfolio view rows" className="grid gap-3 lg:grid-cols-3">
                {(data.reporting.livePortfolioViews ?? []).slice(0, 6).map((view) => (
                  <div
                    key={view.viewId}
                    role="listitem"
                    aria-label={`${view.label} ${view.kind} live portfolio view`}
                    className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <span className="min-w-0">
                        <span className="block font-semibold text-foreground">{view.label}</span>
                        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{view.viewId}</span>
                      </span>
                      <span className="flex flex-wrap items-center justify-end gap-1.5">
                        <Badge variant="outline">{view.kind}</Badge>
                        <Badge variant="outline">{view.state}</Badge>
                      </span>
                    </div>
                    <dl className="mt-3 grid grid-cols-2 gap-x-3 gap-y-2 text-xs">
                      <ReportingCutMetric label="Gross" value={formatReportingMoney(view.grossExposure, view.currency)} />
                      <ReportingCutMetric label="Net" value={formatReportingMoney(view.netExposure, view.currency)} />
                      <ReportingCutMetric label="Cash" value={formatReportingMoney(view.totalCash, view.currency)} />
                      <ReportingCutMetric label="P&L" value={formatReportingMoney(view.totalPnl, view.currency)} />
                    </dl>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{view.liquiditySummary}</p>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{view.telemetrySummary}</p>
                    <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">
                      {view.sourceCount} source{view.sourceCount === 1 ? "" : "s"} · {view.sourceAsOfUtc ?? view.asOf}
                    </p>
                    <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
                      <span className="text-xs text-muted-foreground">{view.cashLadderSummary}</span>
                      <span className="flex flex-wrap gap-2">
                        {view.cashLadderRoute ? (
                          <Button asChild variant="outline" size="sm">
                            <a href={view.cashLadderRoute} target="_blank" rel="noreferrer" aria-label={`Open ${view.label} cash ladder`}>
                              <FileText className="h-4 w-4" aria-hidden="true" />
                              Cash
                            </a>
                          </Button>
                        ) : null}
                        <Button asChild variant="outline" size="sm">
                          <a href={view.route} target="_blank" rel="noreferrer" aria-label={`Open ${view.label} live portfolio view`}>
                            <FileText className="h-4 w-4" aria-hidden="true" />
                            Open
                          </a>
                        </Button>
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        </section>
      ) : null}

      {(data.reporting.pnlSlices ?? []).length > 0 ? (
        <section role="region" aria-label="P&L slicing">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">P&L slices</div>
              <CardTitle>Daily, weekly, monthly, and yearly P&L</CardTitle>
              <CardDescription>Period windows are calculated from retained portfolio run timestamps and marked blocked when source runs are absent.</CardDescription>
            </CardHeader>
            <CardContent>
              <div role="list" aria-label="P&L slice rows" className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                {(data.reporting.pnlSlices ?? []).map((slice) => (
                  <div
                    key={slice.sliceId}
                    role="listitem"
                    aria-label={`${slice.label} ${slice.period} P&L slice`}
                    className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <span className="min-w-0">
                        <span className="block font-semibold text-foreground">{slice.label}</span>
                        <span className="mt-1 block font-mono text-[11px] text-muted-foreground">
                          {formatReportingDateRange(slice.startDate, slice.endDate)}
                        </span>
                      </span>
                      <span className="flex flex-wrap items-center justify-end gap-1.5">
                        <Badge variant="outline">{slice.period}</Badge>
                        <Badge variant="outline">{slice.sourceCount > 0 ? "Source-backed" : "Blocked"}</Badge>
                      </span>
                    </div>
                    <dl className="mt-3 grid grid-cols-2 gap-x-3 gap-y-2 text-xs">
                      <ReportingCutMetric label="Realized" value={formatReportingMoney(slice.realizedPnl, slice.currency)} />
                      <ReportingCutMetric label="Unrealized" value={formatReportingMoney(slice.unrealizedPnl, slice.currency)} />
                      <ReportingCutMetric label="P&L" value={formatReportingMoney(slice.totalPnl, slice.currency)} />
                      <ReportingCutMetric label="Prior" value={formatReportingMoney(slice.priorTotalPnl, slice.currency)} />
                      <ReportingCutMetric label="Change" value={formatReportingMoney(slice.pnlChange, slice.currency)} />
                      <ReportingCutMetric label="Sources" value={slice.sourceCount.toLocaleString()} />
                    </dl>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{slice.readinessSummary}</p>
                    <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">{slice.versionStamp ?? slice.asOf}</p>
                    <div className="mt-3 flex justify-end">
                      <Button asChild variant="outline" size="sm">
                        <a href={slice.route} target="_blank" rel="noreferrer" aria-label={`Open ${slice.label} P&L slice`}>
                          <FileText className="h-4 w-4" aria-hidden="true" />
                          Open
                        </a>
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        </section>
      ) : null}

      {(data.reporting.analyticsRows ?? []).length > 0 ? (
        <section role="region" aria-label="Top-N and contribution analytics">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Top-N analytics</div>
              <CardTitle>Winners, laggards, and contribution breakdowns</CardTitle>
              <CardDescription>Security, strategy, and asset-class rows come from retained portfolio P&L sources.</CardDescription>
            </CardHeader>
            <CardContent>
              <div role="list" aria-label="Top-N and contribution analytics rows" className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                {(data.reporting.analyticsRows ?? []).map((row) => (
                  <div
                    key={row.analyticsId}
                    role="listitem"
                    aria-label={`${row.label} ${row.kind} ${row.scope} analytics row`}
                    className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <span className="min-w-0">
                        <span className="block font-semibold text-foreground">{row.label}</span>
                        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">
                          {row.symbol ?? row.analyticsId}
                        </span>
                      </span>
                      <span className="flex flex-wrap items-center justify-end gap-1.5">
                        <Badge variant="outline">{row.kind}</Badge>
                        <Badge variant="outline">{row.scope}</Badge>
                      </span>
                    </div>
                    <dl className="mt-3 grid grid-cols-2 gap-x-3 gap-y-2 text-xs">
                      <ReportingCutMetric label="Rank" value={row.rank.toLocaleString()} />
                      <ReportingCutMetric label="Class" value={row.classification ?? "Unclassified"} />
                      <ReportingCutMetric label="Realized" value={formatReportingMoney(row.realizedPnl, row.currency)} />
                      <ReportingCutMetric label="Unrealized" value={formatReportingMoney(row.unrealizedPnl, row.currency)} />
                      <ReportingCutMetric label="P&L" value={formatReportingMoney(row.totalPnl, row.currency)} />
                      <ReportingCutMetric label="Contribution" value={formatReportingPercent(row.contributionPercent)} />
                    </dl>
                    <div className="mt-3" aria-label={`${row.label} heat-map intensity ${formatReportingPercent(row.heatMapIntensity)}`}>
                      <div className="h-2 overflow-hidden rounded-sm bg-muted">
                        <div
                          className={cn(
                            "h-full rounded-sm",
                            row.totalPnl < 0 ? "bg-warning" : "bg-success"
                          )}
                          style={{ width: formatHeatMapWidth(row.heatMapIntensity) }}
                        />
                      </div>
                      <div className="mt-1 flex items-center justify-between gap-2 text-[11px] text-muted-foreground">
                        <span>{formatReportingPercent(row.heatMapIntensity)} intensity</span>
                        <span>
                          {row.sourceCount} source{row.sourceCount === 1 ? "" : "s"}
                        </span>
                      </div>
                    </div>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{row.readinessSummary}</p>
                    <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">{row.versionStamp ?? row.asOf}</p>
                    <div className="mt-3 flex justify-end">
                      <Button asChild variant="outline" size="sm">
                        <a href={row.route} target="_blank" rel="noreferrer" aria-label={`Open ${row.label} analytics row`}>
                          <FileText className="h-4 w-4" aria-hidden="true" />
                          Open
                        </a>
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        </section>
      ) : null}

      {(data.reporting.crossFundConsolidations ?? []).length > 0 ? (
        <section role="region" aria-label="Cross-fund consolidations">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Cross-fund</div>
              <CardTitle>Company, fund, and entity rollups</CardTitle>
              <CardDescription>Reporting aggregates source-backed exposure, cash, P&L, and shadow NAV across available funds and entities.</CardDescription>
            </CardHeader>
            <CardContent>
              <div role="list" aria-label="Cross-fund consolidation rows" className="grid gap-3 lg:grid-cols-3">
                {(data.reporting.crossFundConsolidations ?? []).slice(0, 6).map((row) => (
                  <div
                    key={row.consolidationId}
                    role="listitem"
                    aria-label={`${row.label} ${row.scope} cross-fund consolidation`}
                    className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <span className="min-w-0">
                        <span className="block font-semibold text-foreground">{row.label}</span>
                        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{row.consolidationId}</span>
                      </span>
                      <span className="flex flex-wrap items-center justify-end gap-1.5">
                        <Badge variant="outline">{row.scope}</Badge>
                        <Badge variant="outline">{row.isReady ? "Ready" : "Blocked"}</Badge>
                      </span>
                    </div>
                    <dl className="mt-3 grid grid-cols-2 gap-x-3 gap-y-2 text-xs">
                      <ReportingCutMetric label="Funds" value={row.fundCount.toLocaleString()} />
                      <ReportingCutMetric label="Entities" value={row.entityCount.toLocaleString()} />
                      <ReportingCutMetric label="Gross" value={formatReportingMoney(row.grossExposure, row.currency)} />
                      <ReportingCutMetric label="Net" value={formatReportingMoney(row.netExposure, row.currency)} />
                      <ReportingCutMetric label="Cash" value={formatReportingMoney(row.totalCash, row.currency)} />
                      <ReportingCutMetric label="P&L" value={formatReportingMoney(row.totalPnl, row.currency)} />
                    </dl>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{row.readinessSummary}</p>
                    <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">
                      {row.sourceCount} source{row.sourceCount === 1 ? "" : "s"} · {row.versionStamp ?? row.asOf}
                    </p>
                    <div className="mt-3 flex justify-end">
                      <Button asChild variant="outline" size="sm" disabled={!row.isReady}>
                        <a href={row.route} target="_blank" rel="noreferrer" aria-label={`Open ${row.label} cross-fund consolidation`}>
                          <FileText className="h-4 w-4" aria-hidden="true" />
                          Open
                        </a>
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        </section>
      ) : null}

      {(data.reporting.structuredExports ?? []).length > 0 ? (
        <section role="region" aria-label="Structured reporting exports">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Structured exports</div>
              <CardTitle>Regulatory, warehouse, and decision outputs</CardTitle>
              <CardDescription>Source-backed JSON descriptors keep downstream exports tied to governed Reporting evidence.</CardDescription>
            </CardHeader>
            <CardContent>
              <div role="list" aria-label="Structured export rows" className="grid gap-3 lg:grid-cols-3">
                {(data.reporting.structuredExports ?? []).map((structuredExport) => (
                  <div
                    key={structuredExport.exportId}
                    role="listitem"
                    aria-label={`${structuredExport.label} structured export`}
                    className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <span className="min-w-0">
                        <span className="block font-semibold text-foreground">{structuredExport.label}</span>
                        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">
                          {structuredExport.exportId}
                        </span>
                      </span>
                      <span className="flex flex-wrap items-center justify-end gap-1.5">
                        <Badge variant="outline">{structuredExport.purpose}</Badge>
                        <Badge variant="outline">{structuredExport.isReady ? "Ready" : "Blocked"}</Badge>
                      </span>
                    </div>
                    <dl className="mt-3 grid grid-cols-2 gap-x-3 gap-y-2 text-xs">
                      <ReportingCutMetric label="Format" value={structuredExport.format} />
                      <ReportingCutMetric label="Rows" value={structuredExport.rowCount.toLocaleString()} />
                      <ReportingCutMetric label="Fields" value={structuredExport.fieldCount.toLocaleString()} />
                      <ReportingCutMetric label="Sources" value={structuredExport.sourceCount.toLocaleString()} />
                      <ReportingCutMetric label="Schema" value={`v${structuredExport.schemaVersion}`} />
                      <ReportingCutMetric label="Currency" value={structuredExport.currency} />
                    </dl>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">
                      {structuredExport.consumer} · {structuredExport.validationSummary ?? structuredExport.dataset}
                    </p>
                    <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">{structuredExport.retainedPath}</p>
                    <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
                      <span className="break-all font-mono text-[11px] text-muted-foreground">
                        {structuredExport.versionStamp ?? structuredExport.asOf}
                      </span>
                      <Button asChild variant="outline" size="sm" disabled={!structuredExport.isReady}>
                        <a href={structuredExport.route} target="_blank" rel="noreferrer" aria-label={`Open ${structuredExport.label} structured export`}>
                          <FileText className="h-4 w-4" aria-hidden="true" />
                          Open
                        </a>
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        </section>
      ) : null}

      {(data.reporting.brandingThemes ?? []).length > 0 ? (
        <section role="region" aria-label="Report branding themes">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Branding</div>
              <CardTitle>Investor-ready styling themes</CardTitle>
              <CardDescription>Report packs carry shared firm identity, colors, footer text, and disclaimer metadata into generated documents.</CardDescription>
            </CardHeader>
            <CardContent>
              <div role="list" aria-label="Report branding theme rows" className="grid gap-3 lg:grid-cols-3">
                {(data.reporting.brandingThemes ?? []).map((theme) => (
                  <div
                    key={theme.themeId}
                    role="listitem"
                    aria-label={`${theme.name} report branding theme`}
                    className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <span className="min-w-0">
                        <span className="block font-semibold text-foreground">{theme.name}</span>
                        <span className="mt-1 block text-xs text-muted-foreground">{theme.firmName}</span>
                      </span>
                      <Badge variant="outline">{theme.isBuiltIn ? "Built-in" : "Custom"}</Badge>
                    </div>
                    <div className="mt-3 flex flex-wrap gap-2" aria-label={`${theme.name} color palette`}>
                      {[
                        ["Primary", theme.primaryColor],
                        ["Accent", theme.accentColor],
                        ["Text", theme.textColor],
                        ["Background", theme.backgroundColor]
                      ].map(([label, color]) => (
                        <span key={`${theme.themeId}-${label}`} className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
                          <span
                            aria-hidden="true"
                            className="h-4 w-4 rounded-sm border border-border"
                            style={{ backgroundColor: color }}
                          />
                          <span className="font-mono">{color}</span>
                        </span>
                      ))}
                    </div>
                    <p className="mt-3 text-xs leading-5 text-muted-foreground">
                      {theme.footerText ?? "No footer text"} · {theme.disclaimer ?? "No disclaimer"}
                    </p>
                    <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">{theme.logoUri ?? theme.themeId}</p>
                    <div className="mt-3 flex justify-end">
                      <Button
                        aria-label={`Generate ${theme.name} branded report pack`}
                        busy={runningBrandingThemeId === theme.themeId}
                        busyLabel="Generating"
                        disabled={!reportingFundProfileId || Boolean(runningBrandingThemeId)}
                        disabledReason={reportingFundProfileId ? null : "A fund profile is required before generating a governed report pack."}
                        onClick={() => void handleGenerateBrandedPack(theme)}
                        size="sm"
                        variant="outline"
                      >
                        <FileText className="h-4 w-4" aria-hidden="true" />
                        Generate
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
              {brandingPackStatus ? (
                <div className="mt-3">
                  <ReportingCommandStatusView status={brandingPackStatus} />
                </div>
              ) : null}
            </CardContent>
          </Card>
        </section>
      ) : null}

      {writerGrids.length > 0 ? (
        <section role="region" aria-label="No-code report writer">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Report writer</div>
              <CardTitle>No-code grid designer</CardTitle>
              <CardDescription>Pivot, Top-N, contribution, and formula grids from governed template metadata.</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="grid gap-3 xl:grid-cols-2">
                {writerGrids.map((grid) => (
                  <ReportWriterDesignerGrid
                    key={grid.id}
                    grid={grid}
                    settings={getWriterDraftSettings(grid)}
                    customFormula={getWriterCustomFormula(grid)}
                    isSaving={savingWriterDraftId === grid.id}
                    isPreviewing={previewingWriterDraftId === grid.id}
                    preview={writerPreviewByGridId[grid.id] ?? null}
                    getZoneTokens={getWriterZoneTokens}
                    onTokenDragStart={handleWriterTokenDragStart}
                    onZoneDrop={handleWriterZoneDrop}
                    onReset={resetWriterGrid}
                    onSettingsChange={updateWriterDraftSetting}
                    onCustomFormulaChange={updateWriterCustomFormula}
                    onPreview={previewWriterGrid}
                    onSave={saveWriterGridDraft}
                  />
                ))}
              </div>
              {writerPreviewStatus ? (
                <div className="mt-3">
                  <ReportingCommandStatusView status={writerPreviewStatus} />
                </div>
              ) : null}
              {writerDraftStatus ? (
                <div className="mt-3">
                  <ReportingCommandStatusView status={writerDraftStatus} />
                </div>
              ) : null}
            </CardContent>
          </Card>
        </section>
      ) : null}

      <section className="grid gap-4 xl:grid-cols-[0.9fr_1.1fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">Template families</div>
            <CardTitle>Governed report templates</CardTitle>
            <CardDescription>Investor statements, SEC packets, and shadow NAV packs share the same run contract.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            {vm.templateRows.map((template) => (
              <div key={template.id} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-semibold text-foreground">{template.name}</span>
                  <span className="flex flex-wrap items-center gap-1.5">
                    <Badge variant={template.statusVariant}>{template.statusLabel}</Badge>
                    <Badge variant="outline">{template.sourceLabel}</Badge>
                    <Badge variant="outline">{template.family}</Badge>
                    <Badge variant="outline">{template.accessMode}</Badge>
                  </span>
                </div>
                <p className="mt-1 text-xs text-muted-foreground">
                  {template.version} · {template.sectionSummary} · <span className="font-mono">{template.id}</span>
                </p>
                <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
                  <p className="min-w-0 flex-1 text-xs leading-5 text-muted-foreground">
                    <span className="block">{template.approvalSummary}</span>
                    <span className="block">{template.accessSummary}</span>
                  </p>
                  <span className="flex flex-wrap items-center gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      aria-label={template.runActionAriaLabel}
                      disabled={!template.canRunOnDemand || Boolean(runningTemplateRunId)}
                      disabledReason={template.runDisabledReason}
                      busy={runningTemplateRunId === template.id}
                      busyLabel="Running"
                      onClick={() => void handleTemplateRun(template)}
                    >
                      <FileText className="h-4 w-4" aria-hidden="true" />
                      {template.runActionLabel}
                    </Button>
                    <Button asChild variant="outline" size="sm">
                      <a href={template.authoringHref} target="_blank" rel="noreferrer" aria-label={template.actionAriaLabel}>
                        <PencilLine className="h-4 w-4" aria-hidden="true" />
                        {template.actionLabel}
                      </a>
                    </Button>
                    {template.lifecycleActions.map((action) => (
                      <Button
                        key={action.id}
                        variant={action.kind === "reject" ? "ghost" : "outline"}
                        size="sm"
                        aria-label={action.ariaLabel}
                        disabled={!action.isEnabled || Boolean(runningTemplateLifecycleActionId)}
                        disabledReason={action.disabledReason}
                        busy={runningTemplateLifecycleActionId === action.id}
                        busyLabel={buildTemplateLifecycleBusyLabel(action.kind)}
                        onClick={() => void handleTemplateLifecycleAction(template, action)}
                      >
                        <TemplateLifecycleActionIcon action={action.kind} />
                        {action.label}
                      </Button>
                    ))}
                  </span>
                </div>
              </div>
            ))}
            {templateRunStatus ? (
              <ReportingCommandStatusView status={templateRunStatus} />
            ) : null}
            {templateLifecycleStatus ? (
              <ReportingCommandStatusView status={templateLifecycleStatus} />
            ) : null}
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">Run status</div>
            <CardTitle>Report run audit and lineage</CardTitle>
            <CardDescription>Recent manifests keep approval status, retry attempts, and dataset lineage visible.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            {vm.hasRunStatusRows ? vm.runStatusRows.map((run) => (
              <div key={run.id} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-mono text-sm text-foreground">{run.id}</span>
                  <Badge variant={run.status === "Failed" ? "warning" : "outline"}>{run.status}</Badge>
                </div>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  {run.family} · {run.trigger} · {run.lineageSummary} · {run.auditSummary}
                </p>
                {run.failureReason ? <p className="mt-1 text-xs text-warning">{run.failureReason}</p> : null}
                {run.hasDrilldownLinks ? (
                  <div className="mt-2 flex flex-wrap gap-1.5" aria-label={`${run.id} drilldown links`}>
                    {run.drilldownLinks.map((link) => link.isBrowserNavigable ? (
                      <a
                        key={link.id}
                        href={link.href}
                        target="_blank"
                        rel="noreferrer"
                        aria-label={link.ariaLabel}
                        className="inline-flex min-w-0 items-center gap-1.5 rounded-sm border border-border/70 bg-secondary/35 px-2 py-1 text-[11px] text-foreground hover:bg-secondary/55 focus:outline-none focus:ring-2 focus:ring-primary/40"
                      >
                        <Badge variant="outline">{link.kind}</Badge>
                        <span className="truncate">{link.label}</span>
                      </a>
                    ) : (
                      <span
                        key={link.id}
                        role="group"
                        aria-label={link.ariaLabel}
                        className="inline-flex min-w-0 items-center gap-1.5 rounded-sm border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground"
                      >
                        <Badge variant="outline">{link.kind}</Badge>
                        <span className="truncate">{link.label}</span>
                      </span>
                    ))}
                  </div>
                ) : null}
                {run.hasNextActions ? (
                  <div className="mt-2 flex flex-wrap gap-1.5" aria-label={`${run.id} next actions`}>
                    {run.nextActions.map((action) => (
                      <Button
                        key={action.id}
                        aria-label={action.ariaLabel}
                        disabled={!action.isEnabled || action.method !== "POST" || action.kind === "restatement" || Boolean(runningRunActionId)}
                        busy={runningRunActionId === action.id}
                        busyLabel="Running"
                        disabledReason={action.disabledReason ?? (action.kind === "restatement" ? "Restatement requires changed-line evidence." : null)}
                        onClick={() => void handleRunAction(run, action)}
                        size="sm"
                        variant={action.isEnabled ? "outline" : "ghost"}
                        className={cn(
                          "min-w-0 justify-start px-2 py-1 text-[11px]",
                          action.isEnabled
                            ? "border-primary/35 bg-primary/10 text-primary hover:bg-primary/15"
                            : "border-border/60 bg-secondary/20 text-muted-foreground"
                        )}
                      >
                        <Badge variant="outline">{action.method}</Badge>
                        <span className="truncate">{action.label}</span>
                      </Button>
                    ))}
                  </div>
                ) : null}
              </div>
            )) : (
              <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                No report runs have been generated yet.
              </p>
            )}
            {runActionStatus ? (
              <ReportingCommandStatusView status={runActionStatus} />
            ) : null}
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4 xl:grid-cols-[0.95fr_1.05fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">Scheduling</div>
            <CardTitle>Reporting schedules</CardTitle>
            <CardDescription>{vm.scheduleSummary}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <div role="group" aria-label="Schedule report distribution" className="rounded-md border border-border/70 bg-background/30 px-3 py-3">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <h4 className="text-sm font-semibold text-foreground">Schedule delivery</h4>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">
                    Save or update governed report-pack schedules with PDF/XLSX/CSV delivery targets.
                  </p>
                </div>
                <Badge variant="outline">{scheduleDraft.deliveryMode}</Badge>
              </div>
              <div className="mt-3 grid gap-2 md:grid-cols-3">
                <label className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Schedule ID</span>
                  <Input
                    value={scheduleDraft.scheduleId}
                    onChange={(event) => updateScheduleDraft("scheduleId", event.target.value)}
                    aria-label="Reporting schedule ID"
                    className="font-mono"
                  />
                </label>
                <label className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Template ID</span>
                  <Input
                    value={scheduleDraft.templateId}
                    onChange={(event) => updateScheduleDraft("templateId", event.target.value)}
                    aria-label="Reporting schedule template ID"
                    className="font-mono"
                  />
                </label>
                <label className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Cron</span>
                  <Input
                    value={scheduleDraft.cronExpression}
                    onChange={(event) => updateScheduleDraft("cronExpression", event.target.value)}
                    aria-label="Reporting schedule cron expression"
                    className="font-mono"
                  />
                </label>
                <label className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Next as of</span>
                  <Input
                    value={scheduleDraft.nextAsOfDate}
                    onChange={(event) => updateScheduleDraft("nextAsOfDate", event.target.value)}
                    aria-label="Reporting schedule next as-of date"
                    className="font-mono"
                  />
                </label>
                <label className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Due UTC</span>
                  <Input
                    value={scheduleDraft.dueAtUtc}
                    onChange={(event) => updateScheduleDraft("dueAtUtc", event.target.value)}
                    aria-label="Reporting schedule due at UTC"
                    className="font-mono"
                  />
                </label>
                <label className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Retries</span>
                  <Input
                    type="number"
                    min={0}
                    value={scheduleDraft.maxRetries}
                    onChange={(event) => updateScheduleDraft("maxRetries", event.target.value)}
                    aria-label="Reporting schedule max retries"
                    className="font-mono"
                  />
                </label>
                <label className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Requested by</span>
                  <Input
                    value={scheduleDraft.requestedBy}
                    onChange={(event) => updateScheduleDraft("requestedBy", event.target.value)}
                    aria-label="Reporting schedule requested by"
                    className="font-mono"
                  />
                </label>
                <label className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Distribution</span>
                  <Select
                    value={scheduleDraft.distributionId}
                    onChange={(event) => updateScheduleDraft("distributionId", event.target.value)}
                    aria-label="Reporting schedule distribution"
                  >
                    {scheduleDistributionOptions.some((target) => target.distributionId === scheduleDraft.distributionId) ? null : (
                      <option value={scheduleDraft.distributionId}>{scheduleDraft.distributionId}</option>
                    )}
                    {scheduleDistributionOptions.map((target) => (
                      <option key={target.distributionId} value={target.distributionId}>
                        {target.recipient}
                      </option>
                    ))}
                  </Select>
                </label>
                <label className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Delivery mode</span>
                  <Select
                    value={scheduleDraft.deliveryMode}
                    onChange={(event) => updateScheduleDraft("deliveryMode", event.target.value)}
                    aria-label="Reporting schedule delivery mode"
                  >
                    {reportingScheduleDeliveryModes.map((mode) => (
                      <option key={mode} value={mode}>{mode}</option>
                    ))}
                  </Select>
                </label>
              </div>
              <label className="mt-2 block space-y-1">
                <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Description</span>
                <Input
                  value={scheduleDraft.description}
                  onChange={(event) => updateScheduleDraft("description", event.target.value)}
                  aria-label="Reporting schedule description"
                />
              </label>
              <label className="mt-2 block space-y-1">
                <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Delivery note</span>
                <Input
                  value={scheduleDraft.deliveryNote}
                  onChange={(event) => updateScheduleDraft("deliveryNote", event.target.value)}
                  aria-label="Reporting schedule delivery note"
                />
              </label>
              <div className="mt-3 flex flex-wrap items-center justify-between gap-3">
                <div role="group" aria-label="Reporting schedule formats" className="flex flex-wrap gap-2">
                  {reportingScheduleArtifactFormats.map((format) => (
                    <label
                      key={format}
                      className="inline-flex items-center gap-2 rounded-sm border border-border/70 bg-secondary/25 px-2.5 py-1.5 text-xs text-foreground"
                    >
                      <input
                        type="checkbox"
                        checked={scheduleDraft.formats[format]}
                        onChange={(event) => toggleScheduleDraftFormat(format, event.target.checked)}
                        aria-label={`Reporting schedule ${format} format`}
                        className="h-3.5 w-3.5 accent-primary"
                      />
                      <span>{format}</span>
                    </label>
                  ))}
                </div>
                <Button
                  type="button"
                  size="sm"
                  busy={runningScheduleActionId === "schedule-draft:save"}
                  busyLabel="Saving"
                  disabled={Boolean(runningScheduleActionId)}
                  onClick={() => void saveScheduleDraft()}
                  aria-label="Save reporting schedule"
                >
                  <Send className="h-4 w-4" aria-hidden="true" />
                  Save schedule
                </Button>
              </div>
            </div>
            {vm.hasScheduleRows ? (
              <div role="list" aria-label={vm.scheduleListLabel} className="space-y-2">
                {vm.scheduleRows.map((schedule) => (
                  <div
                    key={schedule.id}
                    role="listitem"
                    aria-label={schedule.ariaLabel}
                    className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <span className="min-w-0">
                        <span className="block font-semibold text-foreground">{schedule.templateId}</span>
                        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">
                          {schedule.id}
                        </span>
                      </span>
                      <Badge variant={schedule.stateVariant}>{schedule.state}</Badge>
                    </div>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{schedule.description}</p>
                    <dl className="mt-3 grid gap-2 sm:grid-cols-2">
                      <ReportingScheduleField label="Cron" value={schedule.cronLabel} />
                      <ReportingScheduleField label="Due" value={schedule.dueLabel} />
                      <ReportingScheduleField label="As of" value={schedule.nextAsOfLabel} />
                      <ReportingScheduleField label="Last run" value={schedule.lastRunLabel} />
                      <ReportingScheduleField label="Delivery" value={schedule.deliveryTargetLabel} />
                    </dl>
                    <div className="mt-3 flex flex-wrap items-center gap-2">
                      <Badge variant="outline">{schedule.runCountLabel}</Badge>
                      <Button
                        size="sm"
                        variant="outline"
                        busy={runningScheduleActionId === `${schedule.id}:run`}
                        busyLabel="Running"
                        disabled={Boolean(runningScheduleActionId)}
                        onClick={() => void handleScheduleAction(schedule, "run")}
                      >
                        Run now
                      </Button>
                      <Button
                        size="sm"
                        variant="outline"
                        busy={runningScheduleActionId === `${schedule.id}:pause`}
                        busyLabel="Pausing"
                        disabled={!schedule.canPause || Boolean(runningScheduleActionId)}
                        disabledReason={schedule.canPause ? null : "Only active schedules can be paused."}
                        onClick={() => void handleScheduleAction(schedule, "pause")}
                      >
                        Pause
                      </Button>
                      <Button
                        size="sm"
                        variant="outline"
                        busy={runningScheduleActionId === `${schedule.id}:resume`}
                        busyLabel="Resuming"
                        disabled={!schedule.canResume || Boolean(runningScheduleActionId)}
                        disabledReason={schedule.canResume ? null : "Only paused schedules can be resumed."}
                        onClick={() => void handleScheduleAction(schedule, "resume")}
                      >
                        Resume
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                {vm.scheduleEmptyText}
              </p>
            )}
            {scheduleActionStatus ? (
              <ReportingCommandStatusView status={scheduleActionStatus} />
            ) : null}
            <div className="border-t border-border/70 pt-3">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <h4 className="text-sm font-semibold text-foreground">Delivery plans</h4>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{vm.scheduleDeliveryPlanSummary}</p>
                </div>
                <Badge variant="outline">{vm.scheduleDeliveryPlanRows.length} target{vm.scheduleDeliveryPlanRows.length === 1 ? "" : "s"}</Badge>
              </div>
              {vm.hasScheduleDeliveryPlanRows ? (
                <div role="list" aria-label={vm.scheduleDeliveryPlanListLabel} className="mt-3 space-y-2">
                  {vm.scheduleDeliveryPlanRows.map((plan) => (
                    <div
                      key={plan.id}
                      role="listitem"
                      aria-label={plan.ariaLabel}
                      className="rounded-md border border-border/70 bg-background/40 px-3 py-2"
                    >
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <span className="min-w-0">
                          <span className="block font-semibold text-foreground">{plan.recipient}</span>
                          <span className="mt-1 block text-xs text-muted-foreground">
                            {plan.recipientRole} · {plan.channel}
                          </span>
                        </span>
                        <Badge variant={plan.readinessVariant}>{plan.deliveryMode}</Badge>
                      </div>
                      <p className="mt-2 text-xs leading-5 text-muted-foreground">{plan.readinessSummary}</p>
                      <dl className="mt-3 grid gap-2 sm:grid-cols-2">
                        <ReportingScheduleField label="Formats" value={plan.formatsLabel} />
                        <ReportingScheduleField label="Due" value={plan.dueLabel} />
                        <ReportingScheduleField label="As of" value={plan.nextAsOfLabel} />
                        <ReportingScheduleField label="Owner" value={plan.ownerLabel} />
                        <ReportingScheduleField label="Last delivery" value={plan.lastDeliveryLabel} />
                        <ReportingScheduleField label="Artifact integrity" value={plan.integrityLabel} />
                        <ReportingScheduleField label="Schedule" value={plan.scheduleId} />
                      </dl>
                      {plan.integritySummary ? (
                        <p className="mt-2 text-xs leading-5 text-muted-foreground">{plan.integritySummary}</p>
                      ) : null}
                      {plan.note ? (
                        <p className="mt-2 text-xs leading-5 text-muted-foreground">{plan.note}</p>
                      ) : null}
                      <div className="mt-2 flex flex-wrap gap-2 text-[11px]">
                        <a className="break-all font-mono text-primary underline-offset-2 hover:underline" href={plan.route}>
                          {plan.route}
                        </a>
                        {plan.lastDeliveryHref ? (
                          <a className="break-all font-mono text-primary underline-offset-2 hover:underline" href={plan.lastDeliveryHref}>
                            {plan.lastDeliveryHref}
                          </a>
                        ) : null}
                      </div>
                      {plan.versionStamp ? (
                        <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">{plan.versionStamp}</p>
                      ) : null}
                    </div>
                  ))}
                </div>
              ) : (
                <p role="status" className="mt-3 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                  {vm.scheduleDeliveryPlanEmptyText}
                </p>
              )}
            </div>
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">Delivery history</div>
            <CardTitle>Distribution attempts</CardTitle>
            <CardDescription>Published report-pack delivery attempts are retained by recipient and channel.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            {(data.reporting.deliveryAttempts ?? []).length > 0 ? (
              <div role="list" aria-label="Report-pack delivery attempts" className="space-y-2">
                {(data.reporting.deliveryAttempts ?? []).slice(0, 6).map((attempt) => (
                  <div
                    key={attempt.attemptId}
                    role="listitem"
                    aria-label={`${attempt.recipient} delivery attempt ${attempt.state}`}
                    className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <span className="min-w-0">
                        <span className="block font-semibold text-foreground">{attempt.recipient}</span>
                        <span className="mt-1 block text-xs text-muted-foreground">{attempt.recipientRole} · {attempt.channel}</span>
                      </span>
                      <Badge variant={attempt.state === "Failed" ? "warning" : "success"}>{attempt.state}</Badge>
                    </div>
                    <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">{attempt.deliveryReference}</p>
                    <p className="mt-1 text-xs leading-5 text-muted-foreground">
                      Attempt {attempt.attemptNumber} by {attempt.actor} at {attempt.attemptedAtUtc}
                    </p>
                    {attempt.package ? (
                      <div className="mt-2 space-y-1 text-xs leading-5 text-muted-foreground">
                        <p>
                          {attempt.package.deliveryMode} package · {attempt.package.formats.join(", ")}
                        </p>
                        {attempt.package.secureLink.startsWith("/") ? (
                          <a
                            className="block break-all font-mono text-[11px] text-primary underline-offset-2 hover:underline"
                            href={attempt.package.secureLink}
                          >
                            {attempt.package.secureLink}
                          </a>
                        ) : (
                          <p className="break-all font-mono text-[11px]">
                            {attempt.package.secureLink}
                          </p>
                        )}
                        <p className="break-all font-mono text-[11px]">
                          {attempt.package.retainedManifestPath}
                        </p>
                        {attempt.package.artifacts.some((artifact) => artifact.downloadRoute) ? (
                          <ul aria-label={`${attempt.recipient} package artifact downloads`} className="flex flex-wrap gap-2 pt-1">
                            {attempt.package.artifacts
                              .filter((artifact) => artifact.downloadRoute)
                              .map((artifact) => (
                                <li key={`${attempt.attemptId}-${artifact.artifactName}`}>
                                  <a
                                    className="break-all font-mono text-[11px] text-primary underline-offset-2 hover:underline"
                                    href={artifact.downloadRoute ?? undefined}
                                    aria-label={`Download ${artifact.artifactName}`}
                                  >
                                    {artifact.artifactName}
                                  </a>
                                </li>
                              ))}
                          </ul>
                        ) : null}
                      </div>
                    ) : null}
                    {attempt.failureReason ? <p className="mt-1 text-xs text-warning">{attempt.failureReason}</p> : null}
                  </div>
                ))}
              </div>
            ) : (
              <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                No report-pack delivery attempts have been retained yet.
              </p>
            )}
          </CardContent>
        </Card>
      </section>

      {vm.workflowTaskPanel ? (
        <section
          role="region"
          aria-label={vm.workflowTaskPanel.regionLabel}
          className="panel-surface-strong grid gap-4 px-4 py-4 xl:grid-cols-[1.05fr_0.95fr]"
        >
          <div className="min-w-0 space-y-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="eyebrow-label">{vm.workflowTaskPanel.eyebrow}</div>
                <h3 className="mt-2 text-lg font-semibold text-foreground">{vm.workflowTaskPanel.title}</h3>
                <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">
                  {vm.workflowTaskPanel.description}
                </p>
              </div>
              <Badge variant={vm.workflowTaskPanel.statusVariant}>
                {vm.workflowTaskPanel.statusLabel}
              </Badge>
            </div>
            <div className="flex flex-wrap gap-2">
              {vm.workflowTaskPanel.chips.map((chip) => (
                <ReportingChip key={chip.label} label={chip.label} value={chip.value} />
              ))}
            </div>
            <div
              role="status"
              id={vm.workflowTaskPanel.selectedSummaryId}
              aria-label="Selected report-pack profile"
              className="rounded-md border border-primary/25 bg-primary/10 px-3 py-2 text-sm leading-6 text-primary"
            >
              {vm.workflowTaskPanel.selectedSummary}
            </div>
            <div
              role="region"
              aria-label={vm.workflowTaskPanel.publicationReview.regionLabel}
              className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3"
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <h4 className="text-sm font-semibold text-foreground">{vm.workflowTaskPanel.publicationReview.title}</h4>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">
                    {vm.workflowTaskPanel.publicationReview.description}
                  </p>
                </div>
                <Badge variant={vm.workflowTaskPanel.publicationReview.statusVariant}>
                  {vm.workflowTaskPanel.publicationReview.statusLabel}
                </Badge>
              </div>
              <p className="mt-3 rounded-md border border-border/70 bg-background/40 px-3 py-2 text-sm leading-6 text-foreground">
                {vm.workflowTaskPanel.publicationReview.summaryText}
              </p>
              <div className="mt-3 grid gap-2 sm:grid-cols-2">
                {vm.workflowTaskPanel.publicationReview.fields.map((field) => (
                  <div key={field.label} className="rounded-md border border-border/70 bg-background/40 px-3 py-2">
                    <span className="block text-[11px] uppercase tracking-wide text-muted-foreground">{field.label}</span>
                    <span className={cn("mt-1 block break-all font-mono text-xs", field.className)}>{field.value}</span>
                  </div>
                ))}
              </div>
              <div className="mt-3">
                <Badge variant="outline">{vm.workflowTaskPanel.publicationReview.evidenceSummary}</Badge>
              </div>
            </div>
            <div
              role="region"
              aria-label={vm.workflowTaskPanel.restatementReview.regionLabel}
              className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3"
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <h4 className="text-sm font-semibold text-foreground">{vm.workflowTaskPanel.restatementReview.title}</h4>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">
                    {vm.workflowTaskPanel.restatementReview.description}
                  </p>
                </div>
                <Badge variant={vm.workflowTaskPanel.restatementReview.statusVariant}>
                  {vm.workflowTaskPanel.restatementReview.statusLabel}
                </Badge>
              </div>
              <p className="mt-3 rounded-md border border-border/70 bg-background/40 px-3 py-2 text-sm leading-6 text-foreground">
                {vm.workflowTaskPanel.restatementReview.summaryText}
              </p>
              <div className="mt-3 grid gap-2 sm:grid-cols-2">
                {vm.workflowTaskPanel.restatementReview.fields.map((field) => (
                  <div key={field.label} className="rounded-md border border-border/70 bg-background/40 px-3 py-2">
                    <span className="block text-[11px] uppercase tracking-wide text-muted-foreground">{field.label}</span>
                    <span className={cn("mt-1 block break-all font-mono text-xs", field.className)}>{field.value}</span>
                  </div>
                ))}
              </div>
              <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
                <div className="eyebrow-label">{vm.workflowTaskPanel.restatementReview.changedLinesLabel}</div>
                <Badge variant="outline">{vm.workflowTaskPanel.restatementReview.evidenceSummary}</Badge>
              </div>
              {vm.workflowTaskPanel.restatementReview.hasChangedLines ? (
                <div role="list" aria-label={vm.workflowTaskPanel.restatementReview.changedLinesLabel} className="mt-2 grid gap-2">
                  {vm.workflowTaskPanel.restatementReview.changedLines.map((line) => (
                    <div
                      key={line.id}
                      role="listitem"
                      aria-label={line.ariaLabel}
                      className="rounded-md border border-border/70 bg-background/40 px-3 py-2"
                    >
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <span className="font-mono text-xs text-foreground">{line.lineKey}</span>
                        <Badge variant="warning">{line.valueBridge}</Badge>
                      </div>
                      {line.evidenceHref ? (
                        <a
                          href={line.evidenceHref}
                          className="mt-2 inline-flex text-xs text-primary hover:underline focus:outline-none focus:ring-2 focus:ring-primary/40"
                          aria-label={`Open evidence for ${line.lineKey}`}
                        >
                          {line.evidenceLabel}
                        </a>
                      ) : (
                        <span className="mt-2 block text-xs text-muted-foreground">{line.evidenceLabel}</span>
                      )}
                    </div>
                  ))}
                </div>
              ) : (
                <p role="status" className="mt-2 rounded-md border border-border/70 bg-background/40 px-3 py-2 text-sm leading-6 text-muted-foreground">
                  {vm.workflowTaskPanel.restatementReview.emptyText}
                </p>
              )}
            </div>
            <div>
              <div className="eyebrow-label">Actions</div>
              {vm.workflowTaskPanel.hasActions ? (
                <div
                  id={vm.workflowTaskPanel.actionPanelId}
                  role="list"
                  aria-label={vm.workflowTaskPanel.actionListLabel}
                  className="mt-2 grid gap-2 sm:grid-cols-2"
                >
                  {vm.workflowTaskPanel.actions.map((action) => (
                    <div
                      key={action.id}
                      role="listitem"
                      className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
                    >
                      <div className="flex flex-wrap items-center gap-2">
                        <Button
                          asChild={action.method === "GET" && !action.isDisabled}
                          disabled={action.isDisabled}
                          busy={action.isRunning}
                          busyLabel={action.busyLabel}
                          disabledReason={action.disabledReason}
                          size="sm"
                          variant={action.variant}
                          aria-label={action.ariaLabel}
                          aria-describedby={action.describedById}
                          onClick={
                            action.method === "POST" && vm.selectedProfile
                              ? () => void vm.runExport(action.profileId, vm.selectedProfile!.title)
                              : undefined
                          }
                        >
                          {action.isDisabled ? (
                            action.label
                          ) : action.method === "POST" ? (
                            action.label
                          ) : (
                            <a href={action.href} target="_blank" rel="noreferrer" aria-label={action.ariaLabel}>
                              {action.label}
                            </a>
                          )}
                        </Button>
                        <Badge variant={action.statusBadgeVariant} aria-label={action.statusBadgeAriaLabel}>
                          {action.statusBadgeLabel}
                        </Badge>
                      </div>
                      <p id={action.describedById} className="mt-2 text-xs leading-5 text-muted-foreground">
                        {action.descriptionText}
                      </p>
                    </div>
                  ))}
                </div>
              ) : (
                <p
                  id={vm.workflowTaskPanel.actionPanelId}
                  role="status"
                  aria-label={vm.workflowTaskPanel.actionsEmptyAriaLabel}
                  className="mt-2 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning"
                >
                  {vm.workflowTaskPanel.actionsEmptyText}
                </p>
              )}
            </div>
            <div className="grid gap-3 md:grid-cols-2">
              <div>
                <div className="eyebrow-label">Recipients</div>
                {vm.workflowTaskPanel.hasTargets ? (
                  <div role="list" aria-label={vm.workflowTaskPanel.targetsLabel} className="mt-2 grid gap-2">
                    {vm.workflowTaskPanel.targets.map((target) => (
                      <div
                        key={target.id}
                        role="listitem"
                        aria-label={target.ariaLabel}
                        className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2"
                      >
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <span className="text-sm font-semibold text-foreground">{target.label}</span>
                          <Badge variant={target.stateLabel.includes("Pending") || target.stateLabel === "Blocked" ? "warning" : "outline"}>
                            {target.stateLabel}
                          </Badge>
                        </div>
                        <p className="mt-1 text-xs leading-5 text-muted-foreground">
                          {target.channel} · {target.ownerLabel} · {target.pendingSummary}
                        </p>
                      </div>
                    ))}
                  </div>
                ) : (
                  <p
                    role="status"
                    aria-label={vm.workflowTaskPanel.targetsEmptyAriaLabel}
                    className="mt-2 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning"
                  >
                    {vm.workflowTaskPanel.targetsEmptyText}
                  </p>
                )}
              </div>
              <div>
                <div className="eyebrow-label">Backend</div>
                <div
                  id={vm.workflowTaskPanel.backendPanelId}
                  aria-label={vm.workflowTaskPanel.backendLinksLabel}
                  className="mt-2 grid gap-2"
                >
                  {vm.workflowTaskPanel.backendLinks.map((link) => link.isBrowserNavigable ? (
                    <a
                      key={link.id}
                      href={link.href}
                      target="_blank"
                      rel="noreferrer"
                      aria-label={link.ariaLabel}
                      className="flex min-w-0 items-center gap-2 rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-sm hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
                    >
                      <ReportingBackendReference link={link} />
                    </a>
                  ) : (
                    <div
                      key={link.id}
                      role="group"
                      aria-label={link.ariaLabel}
                      className="flex min-w-0 items-center gap-2 rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm"
                    >
                      <ReportingBackendReference link={link} />
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </div>
          <div>
            <div className="eyebrow-label">Approval profile</div>
            {vm.workflowTaskPanel.hasProfiles ? (
              <div
                role="list"
                aria-label={vm.workflowTaskPanel.profileListLabel}
                aria-describedby={vm.workflowTaskPanel.profileKeyboardHelpId}
                className="mt-3 grid gap-2"
                onKeyDown={handleReportPackProfileKeyDown}
              >
                <span id={vm.workflowTaskPanel.profileKeyboardHelpId} className="sr-only">
                  {vm.workflowTaskPanel.profileKeyboardHelpText}
                </span>
                {vm.workflowTaskPanel.profiles.map((profile) => (
                  <div key={profile.id} role="listitem">
                    <button
                      ref={(node) => {
                        reportPackProfileButtonRefs.current[profile.id] = node;
                      }}
                      type="button"
                      aria-pressed={profile.isSelected}
                      aria-controls={profile.controlsId}
                      aria-expanded={profile.isExpanded}
                      aria-describedby={profile.descriptionId}
                      aria-label={profile.selectAriaLabel}
                      tabIndex={profile.tabIndex}
                      onClick={() => vm.selectProfile(profile.id)}
                      className={cn(
                        "w-full rounded-md border px-3 py-3 text-left transition-colors focus:outline-none focus:ring-2 focus:ring-primary/40",
                        profile.isSelected
                          ? "border-primary/45 bg-primary/10"
                          : "border-border/70 bg-secondary/25 hover:bg-secondary/45"
                      )}
                    >
                      <span className="flex items-start justify-between gap-3">
                        <span>
                          <span className="block font-semibold text-foreground">{profile.name}</span>
                          <span id={profile.descriptionId} className="mt-1 block text-xs text-muted-foreground">
                            {profile.summary}
                          </span>
                        </span>
                        <Badge variant={profile.readinessVariant}>{profile.readinessLabel}</Badge>
                      </span>
                    </button>
                  </div>
                ))}
              </div>
            ) : (
              <p
                role="status"
                aria-label={vm.workflowTaskPanel.profilesEmptyAriaLabel}
                className="mt-3 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning"
              >
                {vm.workflowTaskPanel.profilesEmptyText}
              </p>
            )}
          </div>
        </section>
      ) : null}

      <section className="grid gap-4 xl:grid-cols-[1.2fr_0.8fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">Reporting Lane</div>
            <CardTitle className="flex items-center gap-2">
              <FileText className="h-5 w-5 text-primary" />
              Evidence and distribution
            </CardTitle>
            <CardDescription>
              Export profiles stay tied to accounting evidence, loader posture, and governed distribution recipients.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-3 md:grid-cols-3">
            <ReportingHighlight
              title="Profile wiring"
              description="Each queue row keeps profile ID, format, target tool, and loader posture visible before export."
            />
            <ReportingHighlight
              title="Pack routing"
              description="Report-pack distribution records show the recipient, delivery channel, and pending work."
            />
            <ReportingHighlight
              title="Review posture"
              description="Dictionary and loader evidence remain attached so governed output can be reviewed without context switching."
            />
          </CardContent>
        </Card>

        <Card className="panel-surface-strong text-foreground">
          <CardHeader>
            <div className="eyebrow-label">Distribution</div>
            <CardTitle>Report-pack recipients</CardTitle>
            <CardDescription>{vm.packTargetsSummary}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3 text-sm text-foreground/85">
            <div className="flex flex-wrap gap-2">
              {vm.packTargetChips.map((chip) => (
                <ReportingChip key={chip.label} label={chip.label} value={chip.value} />
              ))}
            </div>
            {vm.hasPackTargets ? (
              <div
                role="list"
                aria-label={vm.packTargetsListLabel}
                className="data-grid-surface space-y-2 border-0 bg-background/40 p-3"
              >
                {vm.packTargets.map((target) => (
                  <div
                    key={target.id}
                    role="listitem"
                    aria-label={target.ariaLabel}
                    className="rounded-md border border-border/70 bg-background/20 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <span className="inline-flex min-w-0 items-start gap-2">
                        <FileText className="mt-0.5 h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                        <span className="min-w-0">
                          <span className="block font-semibold text-foreground">{target.label}</span>
                          <span className="mt-1 block text-xs leading-5 text-muted-foreground">
                            {target.recipientRole} · {target.channel}
                          </span>
                        </span>
                      </span>
                      <Badge variant={target.stateLabel.includes("Pending") || target.stateLabel === "Blocked" ? "warning" : "outline"}>
                        {target.pendingItemsLabel}
                      </Badge>
                    </div>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{target.pendingSummary}</p>
                    <div className="mt-2 flex flex-wrap gap-2 text-xs text-muted-foreground">
                      <span>Owner: {target.ownerLabel}</span>
                      <span>Due: {target.dueLabel}</span>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <p
                role="status"
                aria-label={vm.packTargetsEmptyState.ariaLabel}
                className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning"
              >
                {vm.packTargetsEmptyState.text}
              </p>
            )}
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Landmark className="h-4 w-4 text-primary" />
                  Governed export queue
                </CardTitle>
                <CardDescription className="mt-2">{vm.description}</CardDescription>
              </div>
              <Badge variant="outline">{vm.countLabel}</Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              {vm.queueChips.map((chip) => (
                <ReportingChip key={chip.label} label={chip.label} value={chip.value} />
              ))}
            </div>
            <div>
              {vm.hasRows ? (
                <DenseDataTable
                  columns={reportingProfileColumns}
                  rows={vm.rows}
                  getRowId={(profile) => profile.id}
                  getRowAriaLabel={(profile) => profile.selectAriaLabel}
                  getRowSelectAriaLabel={(profile) => profile.selectAriaLabel}
                  getRowAriaControls={(profile) => profile.controlsId}
                  getRowAriaExpanded={(profile) => profile.isExpanded}
                  onRowSelect={(profile) => vm.selectProfile(profile.id)}
                  selectedRowId={vm.selectedProfile?.id ?? null}
                  emptyText={vm.emptyText}
                  ariaLabel={vm.listLabel}
                  caption={`${vm.listLabel}. Select a row to inspect export evidence and actions.`}
                />
              ) : (
                <div
                  role="status"
                  className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning"
                >
                  {vm.emptyText}
                </div>
              )}
            </div>
          </CardContent>
        </Card>

        <aside
          id={vm.detailId}
          role="region"
          aria-label={vm.statusTitle}
          aria-live="polite"
          className="row-detail-panel h-fit min-w-0"
        >
          <div className="head flex items-center justify-between gap-3">
            <span>Selected profile inspector</span>
            <Badge variant={vm.statusBadgeVariant}>
              {vm.statusBadgeLabel}
            </Badge>
          </div>
          <div className="body">
            <div className="min-w-0">
              <h3 className="text-sm font-semibold text-foreground">
                {vm.selectedProfile?.title ?? vm.statusTitle}
              </h3>
              <p className="mt-1 font-mono text-xs text-muted-foreground">
                {vm.selectedProfile ? `${vm.selectedProfile.id} · ${vm.selectedProfile.subtitle}` : vm.nextAction}
              </p>
            </div>
            <p className="mt-3 text-sm leading-6 text-muted-foreground">{vm.statusDetail}</p>
            {vm.exportStatus ? (
              <div
                role="status"
                aria-label={vm.exportStatus.ariaLabel}
                className={cn("mt-3 space-y-3 rounded-md border px-3 py-2 text-sm leading-6", vm.exportStatus.className)}
              >
                <p>{vm.exportStatus.text}</p>
                {vm.exportStatus.fields.length > 0 ? (
                  <dl className="grid gap-2 sm:grid-cols-2">
                    {vm.exportStatus.fields.map((field) => (
                      <div
                        key={field.label}
                        className="rounded-sm border border-border/60 bg-background/25 px-2.5 py-2"
                      >
                        <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
                          {field.label}
                        </dt>
                        <dd className={cn("mt-1 break-words font-mono text-xs", field.className)}>
                          {field.value}
                        </dd>
                      </div>
                    ))}
                  </dl>
                ) : null}
                {vm.exportStatus.warnings.length > 0 ? (
                  <ul className="space-y-1 rounded-sm border border-warning/30 bg-warning/10 px-2.5 py-2 text-xs text-warning">
                    {vm.exportStatus.warnings.map((warning) => (
                      <li key={warning}>{warning}</li>
                    ))}
                  </ul>
                ) : null}
                {vm.exportStatus.artifacts.length > 0 ? (
                  <dl
                    aria-label="Export artifacts"
                    className="space-y-1 rounded-sm border border-border/60 bg-background/25 px-2.5 py-2"
                  >
                    {vm.exportStatus.artifacts.map((artifact) => (
                      <div key={`${artifact.label}-${artifact.value}`} className="grid gap-1">
                        <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
                          {artifact.label}
                        </dt>
                        <dd className={cn("break-words font-mono text-xs", artifact.className)}>
                          {artifact.value}
                        </dd>
                      </div>
                    ))}
                  </dl>
                ) : null}
              </div>
            ) : null}
            <p className="mt-3 font-mono text-xs text-muted-foreground">{vm.nextAction}</p>
            {vm.selectedProfile ? (
              <div className="mt-4 space-y-3 border-t border-border/70 pt-4">
                <p className="text-sm leading-6 text-muted-foreground">{vm.selectedProfile.description}</p>
                <div
                  role="status"
                  aria-label={`${vm.selectedProfile.title} readiness`}
                  className="rounded-md border border-primary/25 bg-primary/10 px-3 py-2 text-sm leading-6 text-primary"
                >
                  {vm.selectedProfile.readinessSummary}
                </div>
                <dl className="grid gap-2">
                  {vm.selectedProfile.fields.map((field) => (
                    <div
                      key={field.label}
                      className="grid grid-cols-[minmax(0,0.6fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2"
                    >
                      <dt className="text-xs text-muted-foreground">{field.label}</dt>
                      <dd className={cn("text-right font-mono text-xs", field.className)}>
                        {field.value}
                      </dd>
                    </div>
                  ))}
                </dl>
                <div className="grid gap-2 pt-2" role="list" aria-label={`${vm.selectedProfile.title} export actions`}>
                  {vm.selectedProfile.actions.map((action) => (
                    <div
                      key={action.id}
                      role="listitem"
                      className="rounded-md border border-border/60 bg-secondary/20 px-3 py-2"
                    >
                      <div className="flex flex-wrap items-center gap-2">
                        <Button
                          asChild={action.method === "GET" && !action.isDisabled}
                          disabled={action.isDisabled}
                          busy={action.isRunning}
                          busyLabel={action.busyLabel}
                          disabledReason={action.disabledReason}
                          size="sm"
                          variant={action.variant}
                          aria-label={action.ariaLabel}
                          aria-describedby={action.describedById}
                          onClick={
                            action.method === "POST"
                              ? () => void vm.runExport(action.profileId, vm.selectedProfile!.title)
                              : undefined
                          }
                        >
                          {action.isDisabled ? (
                            action.label
                          ) : action.method === "POST" ? (
                            action.label
                          ) : (
                            <a href={action.href} target="_blank" rel="noreferrer" aria-label={action.ariaLabel}>
                              {action.label}
                            </a>
                          )}
                        </Button>
                        <Badge variant={action.statusBadgeVariant} aria-label={action.statusBadgeAriaLabel}>
                          {action.statusBadgeLabel}
                        </Badge>
                      </div>
                      <p id={action.describedById} className="mt-2 text-xs leading-5 text-muted-foreground">
                        {action.descriptionText}
                      </p>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}
          </div>
        </aside>
      </section>
    </div>
  );
}

function ReportingHighlight({ title, description }: { title: string; description: string }) {
  return (
    <div className="rounded-lg border border-border/70 bg-secondary/35 p-4">
      <div className="font-semibold">{title}</div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{description}</p>
    </div>
  );
}

function ReportingChip({ label, value }: { label: string; value: string }) {
  return (
    <div className="toolbar-chip" aria-label={`${label} ${value}`}>
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground">{value}</span>
    </div>
  );
}

function ReportingCutMetric({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words font-mono text-xs text-foreground">{value}</dd>
    </div>
  );
}

function TemplateLifecycleActionIcon({ action }: { action: ReportingTemplateLifecycleActionRow["kind"] }) {
  if (action === "approve") {
    return <CheckCircle2 className="h-4 w-4" aria-hidden="true" />;
  }

  if (action === "reject") {
    return <XCircle className="h-4 w-4" aria-hidden="true" />;
  }

  return <Send className="h-4 w-4" aria-hidden="true" />;
}

interface ReportWriterDesignerGridProps {
  grid: ReportingWriterGridRow;
  settings: ReportWriterDraftSettings;
  customFormula: ReportWriterCustomFormulaDraft;
  isSaving: boolean;
  isPreviewing: boolean;
  preview: ReportWriterGridRender | null;
  getZoneTokens: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone) => ReportingWriterToken[];
  onTokenDragStart: (event: DragEvent<HTMLElement>, token: ReportingWriterToken) => void;
  onZoneDrop: (event: DragEvent<HTMLElement>, grid: ReportingWriterGridRow, zone: ReportWriterDropZone) => void;
  onReset: (grid: ReportingWriterGridRow) => void;
  onSettingsChange: (grid: ReportingWriterGridRow, field: ReportWriterDraftSettingsField, value: string) => void;
  onCustomFormulaChange: (grid: ReportingWriterGridRow, field: ReportWriterCustomFormulaField, value: string) => void;
  onPreview: (grid: ReportingWriterGridRow) => void | Promise<void>;
  onSave: (grid: ReportingWriterGridRow) => void | Promise<void>;
}

function ReportWriterDesignerGrid({
  grid,
  settings,
  customFormula,
  isSaving,
  isPreviewing,
  preview,
  getZoneTokens,
  onTokenDragStart,
  onZoneDrop,
  onReset,
  onSettingsChange,
  onCustomFormulaChange,
  onPreview,
  onSave
}: ReportWriterDesignerGridProps) {
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
          <Badge variant="outline">{grid.kind}</Badge>
          <Badge variant="outline">{grid.topNLabel}</Badge>
        </span>
      </div>
      <p className="mt-2 text-xs leading-5 text-muted-foreground">{grid.summary}</p>
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
              disabled={settings.accessMode === "CompanyWide"}
            >
              <option value="User">User</option>
              <option value="Group">Group</option>
              <option value="Company">Company</option>
            </Select>
          </label>
          <label className="space-y-1">
            <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Principal ID</span>
            <Input
              value={settings.principalId}
              onChange={(event) => onSettingsChange(grid, "principalId", event.target.value)}
              aria-label={`${grid.title} draft principal id`}
              className="font-mono"
              disabled={settings.accessMode === "CompanyWide"}
            />
          </label>
        </div>
      </div>
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
            tokens={getZoneTokens(grid, "rowFields")}
            onTokenDragStart={onTokenDragStart}
            onZoneDrop={onZoneDrop}
          />
          <ReportWriterDropZoneView
            grid={grid}
            zone="columnFields"
            label="Columns"
            tokens={getZoneTokens(grid, "columnFields")}
            onTokenDragStart={onTokenDragStart}
            onZoneDrop={onZoneDrop}
          />
          <ReportWriterDropZoneView
            grid={grid}
            zone="metrics"
            label="Metrics"
            tokens={getZoneTokens(grid, "metrics")}
            onTokenDragStart={onTokenDragStart}
            onZoneDrop={onZoneDrop}
          />
          <ReportWriterDropZoneView
            grid={grid}
            zone="formulas"
            label="Formulas"
            tokens={getZoneTokens(grid, "formulas")}
            onTokenDragStart={onTokenDragStart}
            onZoneDrop={onZoneDrop}
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
        <ReportWriterPreviewTable grid={grid} preview={preview} />
      ) : null}
    </div>
  );
}

function ReportWriterPreviewTable({ grid, preview }: { grid: ReportingWriterGridRow; preview: ReportWriterGridRender }) {
  const rows = preview.rows.slice(0, 5);
  const lineage = preview.lineage;
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
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length > 0 ? rows.map((row) => (
              <tr key={row.rowKey} className="border-t border-border/50">
                {preview.columns.map((column) => (
                  <td key={`${row.rowKey}:${column.key}`} className="px-2 py-1.5 font-mono text-foreground">
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
                  ? lineage.formulas.map((formula) => `${formula.name}=[${formula.sourceFields.join(", ")}]`).join(", ")
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
      {preview.warnings.length > 0 ? (
        <ul className="mt-2 space-y-1 text-xs text-warning">
          {preview.warnings.map((warning) => (
            <li key={warning}>{warning}</li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

interface ReportWriterDropZoneViewProps {
  grid: ReportingWriterGridRow;
  zone: ReportWriterDropZone;
  label: string;
  tokens: ReportingWriterToken[];
  onTokenDragStart: (event: DragEvent<HTMLElement>, token: ReportingWriterToken) => void;
  onZoneDrop: (event: DragEvent<HTMLElement>, grid: ReportingWriterGridRow, zone: ReportWriterDropZone) => void;
}

function ReportWriterDropZoneView({
  grid,
  zone,
  label,
  tokens,
  onTokenDragStart,
  onZoneDrop
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
        {tokens.length > 0 ? tokens.map((token) => (
          <ReportWriterTokenChip
            key={token.id}
            token={token}
            draggable
            onDragStart={onTokenDragStart}
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
  onDragStart
}: {
  token: ReportingWriterToken;
  draggable?: boolean;
  onDragStart: (event: DragEvent<HTMLElement>, token: ReportingWriterToken) => void;
}) {
  return (
    <span
      role="listitem"
      draggable={draggable}
      onDragStart={(event) => onDragStart(event, token)}
      className="inline-flex max-w-full items-center gap-1.5 rounded-sm border border-border/70 bg-secondary/35 px-2 py-1 text-[11px] text-foreground"
      title={token.detail}
    >
      <GripVertical className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
      <Badge variant={token.kind === "formula" ? "warning" : token.kind === "metric" ? "success" : "outline"}>{token.kind}</Badge>
      <span className="truncate font-mono">{token.label}</span>
    </span>
  );
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

function buildDefaultWriterDraftSettings(grid: ReportingWriterGridRow): ReportWriterDraftSettings {
  const firstFilter = grid.filters[0] ?? null;
  return {
    name: grid.templateId,
    displayName: `${grid.title} Draft`,
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
    name: `${normalizeIdentifierToken(grid.gridId, "grid")}CustomFormula`,
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
  const expression = normalizeDraftText(customFormula.expression, "");
  if (!expression) {
    return null;
  }

  const fallbackName = `${normalizeIdentifierToken(grid.gridId, "grid")}CustomFormula`;
  const name = normalizeIdentifierToken(customFormula.name, fallbackName);
  const label = normalizeDraftText(customFormula.label, name);
  return {
    id: `formula:${grid.id}:custom:${name}`,
    label,
    detail: expression,
    kind: "formula",
    name,
    expression
  };
}

function buildReportTemplateDraftRequest(
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

function buildRenderReportTemplateRequest(
  grid: ReportingWriterGridRow,
  zones: Record<ReportWriterDropZone, ReportingWriterToken[]>,
  settings: ReportWriterDraftSettings
): RenderReportTemplateRequest {
  const gridDefinition = buildReportWriterGridDefinition(grid, zones, settings);
  return {
    templateId: {
      name: grid.templateId,
      version: parseReportTemplateVersion(grid.templateVersion) ?? 1
    },
    parameters: {
      period: "preview-period",
      asOfDate: "preview-as-of",
      preview: "browser-report-writer"
    },
    datasetRows: buildReportWriterPreviewRows(gridDefinition),
    grids: [gridDefinition]
  };
}

function buildReportWriterGridDefinition(
  grid: ReportingWriterGridRow,
  zones: Record<ReportWriterDropZone, ReportingWriterToken[]>,
  settings: ReportWriterDraftSettings
): ReportWriterGridDefinition {
  return {
    gridId: grid.gridId,
    title: grid.title,
    kind: normalizeReportWriterGridKind(grid.kind),
    rowFields: normalizeStringList(zones.rowFields.map(resolveWriterFieldName)),
    columnFields: normalizeStringList(zones.columnFields.map(resolveWriterFieldName)),
    metrics: normalizeWriterMetrics(zones.metrics),
    formulas: normalizeWriterFormulas(zones.formulas),
    topN: grid.kind === "TopN" ? grid.topN ?? 10 : grid.topN,
    sortBy: grid.sortBy,
    sortDescending: grid.sortDescending,
    filters: buildWriterFilters(settings)
  };
}

function buildReportWriterPreviewRows(grid: ReportWriterGridDefinition): Record<string, string>[] {
  const dimensionFields = normalizeStringList([
    ...(grid.rowFields ?? []),
    ...(grid.columnFields ?? [])
  ]);
  const metricSourceFields = normalizeStringList((grid.metrics ?? []).map((metric) => metric.sourceField));
  const formulaFields = normalizeStringList((grid.formulas ?? []).flatMap((formula) => extractReportWriterFormulaFields(formula.expression)));
  const numericFields = normalizeStringList([
    ...metricSourceFields,
    ...formulaFields,
    ...(grid.sortBy ? [grid.sortBy] : [])
  ]).filter((field) => !dimensionFields.some((dimension) => dimension.toLowerCase() === field.toLowerCase()));
  const fields = normalizeStringList([...dimensionFields, ...numericFields]);
  const filters = grid.filters ?? [];
  const filterFields = normalizeStringList(filters.map((filter) => filter.field));

  if (fields.length === 0 && filterFields.length === 0) {
    return [{ previewRow: "1" }, { previewRow: "2" }];
  }

  return Array.from({ length: 4 }, (_, index) => {
    const row: Record<string, string> = {};
    for (const field of dimensionFields) {
      row[field] = previewDimensionValue(field, index);
    }

    for (const field of numericFields) {
      row[field] = previewNumericValue(field, index);
    }

    for (const filter of filters) {
      if (!filter.field) {
        continue;
      }

      row[filter.field] = previewFilterValue(filter, index);
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
    ownerPrincipalId: settings.accessMode === "Private" ? principalId : "browser-workstation",
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

function normalizeWriterMetrics(tokens: ReportingWriterToken[]): ReportWriterMetricDefinition[] {
  const metrics = tokens
    .map(tokenToMetricDefinition)
    .filter((metric): metric is ReportWriterMetricDefinition => Boolean(metric));
  return dedupeBy(metrics, (metric) => metric.name.toLowerCase());
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

  return Array.from(expression.matchAll(/\{([^}]+)\}/g), (match) => match[1]?.trim() ?? "").filter(Boolean);
}

function previewDimensionValue(field: string, index: number): string {
  const normalized = field.toLowerCase();
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

function previewNumericValue(field: string, index: number): string {
  const normalized = field.toLowerCase();
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

function previewFilterValue(filter: ReportWriterFilterDefinition, index: number): string {
  const operator = normalizeReportWriterFilterOperator(filter.operator);
  const value = filter.value ?? "";
  if (operator === "IsBlank") {
    return index === 0 ? "" : previewDimensionValue(filter.field, index);
  }

  if (operator === "IsNotBlank") {
    return index === 0 ? previewDimensionValue(filter.field, index) : "";
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

  return index < 2 ? value : previewDimensionValue(filter.field, index);
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

function normalizeReportWriterGridKind(kind: string): ReportWriterGridKind {
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

function normalizeReportWriterFilterOperator(value: ReportWriterFilterOperator | string | null | undefined): ReportWriterFilterOperator {
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

function formatReportWriterFilterOperator(operator: ReportWriterFilterOperator): string {
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

function isBlankFilterOperator(operator: ReportWriterFilterOperator | string): boolean {
  const normalized = normalizeReportWriterFilterOperator(operator);
  return normalized === "IsBlank" || normalized === "IsNotBlank";
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

function ReportingScheduleField({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-sm border border-border/60 bg-background/30 px-2.5 py-2">
      <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words font-mono text-xs text-foreground">{value}</dd>
    </div>
  );
}

function formatReportingMoney(value: number, currency: string): string {
  try {
    return new Intl.NumberFormat(undefined, {
      style: "currency",
      currency: currency || "USD",
      maximumFractionDigits: Math.abs(value) >= 1000 ? 0 : 2
    }).format(value);
  } catch {
    return `${currency || "USD"} ${value.toLocaleString(undefined, { maximumFractionDigits: 2 })}`;
  }
}

function formatReportingDateRange(startDate: string, endDate: string): string {
  return startDate === endDate ? startDate : `${startDate} to ${endDate}`;
}

function formatReportingPercent(value: number): string {
  return `${value.toLocaleString(undefined, { maximumFractionDigits: 2 })}%`;
}

function formatHeatMapWidth(value: number): string {
  if (!Number.isFinite(value) || value <= 0) {
    return "2%";
  }

  return `${Math.min(100, Math.max(2, value))}%`;
}

function resolveReportingFundProfileId(reporting: AccountingWorkspaceResponse["reporting"] | null): string | null {
  const direct = reporting?.fundProfileId?.trim() || reporting?.selectedFundProfileId?.trim();
  if (direct) {
    return direct;
  }

  return reporting?.workflowRecords
    ?.map((record) => record.fundProfileId?.trim())
    .find((fundProfileId): fundProfileId is string => Boolean(fundProfileId)) ?? null;
}

function ReportingCommandStatusView({ status }: { status: ReportingCommandStatus }) {
  return (
    <div
      role="status"
      aria-label={`${status.label} status`}
      className={cn(
        "rounded-md border px-3 py-2 text-sm leading-6",
        status.state === "success"
          ? "border-success/30 bg-success/10 text-success"
          : status.state === "error"
            ? "border-warning/35 bg-warning/10 text-warning"
            : "border-primary/30 bg-primary/10 text-primary"
      )}
    >
      <p>{status.message}</p>
      {status.details.length > 0 ? (
        <ul className="mt-2 space-y-1 text-xs">
          {status.details.map((detail) => (
            <li key={detail}>{detail}</li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

async function executeRunAction(run: ReportingRunStatusRow, action: ReportingRunActionRow): Promise<void> {
  if (action.kind.startsWith("delivery:")) {
    const reportId = extractReportPackId(run, action);
    const distributionId = action.kind.slice("delivery:".length);
    await deliverReportPack(reportId, {
      distributionId,
      actor: "browser-workstation",
      note: "Delivered from browser Reporting workspace.",
      formats: ["Pdf", "Xlsx", "Csv"],
      evidenceLinks: buildEvidenceLinksFromRun(run)
    });
    return;
  }

  if (action.kind === "approval-reject") {
    await apiPostJson<unknown>(action.href, {
      reason: "Returned from browser Reporting workspace.",
      actor: "browser-workstation",
      actorRole: "Reviewer",
      evidenceLinks: buildEvidenceLinksFromRun(run)
    });
    return;
  }

  if (action.kind === "publication") {
    const reportId = extractReportPackId(run, action);
    await apiPostJson<unknown>(action.href, {
      signedOffBy: "browser-workstation",
      evidenceHash: `sha256:${normalizeEvidenceToken(run.id)}`,
      manifestId: `browser-${normalizeEvidenceToken(reportId)}`,
      retainedManifestPath: `workstation/reporting/${normalizeEvidenceToken(reportId)}/manifest.json`,
      evidenceLinks: buildEvidenceLinksFromRun(run),
      note: "Published from browser Reporting workspace."
    });
    return;
  }

  await apiPostJson<unknown>(action.href);
}

function buildReportTemplateDecisionRequest(
  template: ReportingTemplateRow,
  action: ReportingTemplateLifecycleActionRow
): ReportTemplateDecisionRequest {
  if (action.kind === "approve") {
    return {
      rationale: "Approved from browser Reporting workspace.",
      approvalReference: `browser-template-approval:${template.templateName}:v${template.versionNumber}`
    };
  }

  if (action.kind === "reject") {
    return {
      rationale: "Returned from browser Reporting workspace."
    };
  }

  return {
    rationale: "Ready for controller review."
  };
}

function executeTemplateLifecycleAction(
  template: ReportingTemplateRow,
  action: ReportingTemplateLifecycleActionRow,
  request: ReportTemplateDecisionRequest
) {
  if (action.kind === "approve") {
    return approveReportTemplateDraft(template.templateName, template.versionNumber, request);
  }

  if (action.kind === "reject") {
    return rejectReportTemplateDraft(template.templateName, template.versionNumber, request);
  }

  return submitReportTemplateDraft(template.templateName, template.versionNumber, request);
}

function buildTemplateLifecycleBusyLabel(action: ReportingTemplateLifecycleActionRow["kind"]): string {
  if (action === "approve") {
    return "Approving";
  }

  if (action === "reject") {
    return "Rejecting";
  }

  return "Submitting";
}

function extractReportPackId(run: ReportingRunStatusRow, action: ReportingRunActionRow): string {
  const hrefMatch = action.href.match(/\/reporting\/packs\/([0-9a-fA-F-]{36})(?:\/|$)/);
  if (hrefMatch?.[1]) {
    return hrefMatch[1];
  }

  if (run.id.startsWith("report-pack:")) {
    return run.id.slice("report-pack:".length);
  }

  return run.id;
}

function buildEvidenceLinksFromRun(run: ReportingRunStatusRow): ReportingWorkflowEvidenceLink[] {
  const links = run.drilldownLinks
    .filter((link) => link.kind.includes("evidence") || link.href.includes("/evidence"))
    .map((link) => ({
      evidenceId: normalizeEvidenceToken(link.id),
      label: link.label,
      route: link.href,
      source: link.source || "reporting",
      capturedAtUtc: null
    }));

  if (links.length > 0) {
    return links;
  }

  return [{
    evidenceId: normalizeEvidenceToken(run.id),
    label: `${run.templateId} report run`,
    route: null,
    source: "reporting",
    capturedAtUtc: null
  }];
}

function normalizeEvidenceToken(value: string): string {
  const normalized = value.toLowerCase().replace(/[^a-z0-9-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "reporting-evidence";
}

function ReportingBackendReference({
  link
}: {
  link: {
    method: string;
    label: string;
    href: string;
    interactionLabel: string;
  };
}) {
  return (
    <>
      <Badge variant="outline">{link.method}</Badge>
      <span className="min-w-0">
        <span className="block font-semibold text-foreground">{link.label}</span>
        <span className="block break-all font-mono text-[11px] text-muted-foreground">{link.href}</span>
        <span className="mt-1 inline-flex rounded-sm border border-border/60 px-1.5 py-0.5 text-[10px] uppercase text-muted-foreground">
          {link.interactionLabel}
        </span>
      </span>
    </>
  );
}
