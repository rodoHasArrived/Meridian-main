import { type KeyboardEvent, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { FileText, Landmark, Network, PencilLine, RotateCcw, XCircle } from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import { formatCurrency as formatCurrencyAmount, formatPercent as formatPercentAmount } from "@/lib/format";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FreshnessChip } from "@/components/ui/freshness-chip";
import { Select } from "@/components/ui/select";
import { SeverityBadge } from "@/components/operations";
import { registerCommandPaletteActions } from "@/components/meridian/command-palette.actions";
import {
  encodeViewStateEnvelope,
  readViewStateFromSearch,
  stripViewStateFromSearch,
  VIEW_STATE_QUERY_KEY
} from "@/lib/view-state-envelope";
import { FinancialRecordExplorerShell } from "@/components/meridian/financial-record-explorer";
import { MetricSnapshotCard } from "@/components/meridian/metric-card";
import { ReportingPeriodSwitcher } from "@/components/meridian/reporting-period-switcher";
import { ReportingHub } from "@/components/meridian/reporting-hub";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import {
  apiPostJson,
  approveReportTemplateDraft,
  deliverReportPack,
  generateReportPack,
  pauseReportingSchedule,
  previewReportPack,
  recordReportPackDeliveryFailure,
  rejectReportTemplateDraft,
  resumeReportingSchedule,
  runDueReportingSchedules,
  runReportingNow,
  runReportingScheduleNow,
  saveReportingSchedule,
  submitReportTemplateDraft
} from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { cn } from "@/lib/utils";
import { todayIsoDate } from "@/lib/reporting-periods";
import { buildReportingHubModel } from "@/lib/reporting-hub";
import {
  resolveReportPackProfileKeyCommand,
  useReportingScreenViewModel,
  type ReportingProfileRow,
  type ReportingRunActionRow,
  type ReportingRunStatusRow,
  type ReportingScheduleDeliveryPlanRow,
  type ReportingScheduleRow,
  type ReportingTemplateLifecycleActionRow,
  type ReportingTemplateRow,
  type ReportingWriterGridRow,
  type ReportingWriterToken
} from "@/screens/reporting-screen.view-model";
import { ReportingPrivateCapitalReadinessPanel } from "@/screens/reporting-screen.private-capital-readiness";
import {
  ReportingBrandingAccessPanel,
  buildDefaultReportBrandingDraft,
  buildReportBrandingOverride,
  type ReportBrandingDraftField,
  type ReportBrandingDraftState
} from "@/screens/reporting-screen.branding-access";
import {
  ExportsReportRunner,
  type ExportsReportRunDraftField,
  type ExportsReportRunDraftState
} from "@/screens/reporting-screen.exports-runner";
import { ReportingDeliveryHistoryPanel } from "@/screens/reporting-screen.delivery-history";
import {
  ReportWriterDesignerGrid,
  ReportingReportWriterSection,
  formatReportWriterFilterOperator,
  isBlankFilterOperator,
  normalizeReportWriterFilterOperator,
  normalizeReportWriterGridKind,
  parseReportWriterTopN,
  useReportingReportWriter,
  type ReportWriterChartDraft,
  type ReportWriterDraftSettings,
  type ReportWriterDropZone,
  type ReportWriterFormatRuleDraft,
  type ReportWriterPreviewDatasetProfile
} from "@/screens/reporting-screen.report-writer";
import {
  ReportingScheduleManagementPanel,
  reportingScheduleArtifactFormats,
  reportingScheduleDeliveryModes,
  type ReportingScheduleArtifactFormat,
  type ReportingScheduleDraftField,
  type ReportingScheduleDraftState,
  type ReportingScheduleDraftTarget,
  type ReportingScheduleManagementModel
} from "@/screens/reporting-screen.schedule-management";
import { TemplateLifecycleActionIcon } from "@/screens/reporting-screen.template-lifecycle";
import { ReportingGeneratedGridExportLinks, ReportingRunVersionFields } from "@/screens/reporting-screen.run-status-modules";
import {
  ReportingBackendReference,
  ReportingCommandStatusView,
  type ReportingCommandStatus
} from "@/screens/reporting-screen.shared-components";
import { ReportingTaskModeLauncher } from "@/screens/reporting-screen.task-modes";
import {
  ReportingChip,
  ReportingWorkbenchContext
} from "@/screens/reporting-screen.workbench-context";
import type {
  AccountingWorkspaceResponse,
  GovernanceReportArtifactFormat,
  ReportBrandingTheme,
  ReportPackDeliveryAttempt,
  ReportPackDeliveryFailureRequest,
  ReportPackDeliveryMode,
  ReportTemplateDecisionRequest,
  ReportTemplateDraftRequest,
  ReportWriterAggregateFunction,
  ReportWriterChartDefinition,
  ReportWriterFilterDefinition,
  ReportWriterFormatRule,
  ReportWriterGridDefinition,
  ReportWriterGridKind,
  ReportingRunRequest,
  ReportWriterMetricDefinition,
  RenderReportTemplateRequest,
  ReportingScheduleUpsertRequest,
  ReportingWorkflowEvidenceLink
} from "@/types";

interface ReportingScreenProps {
  data: AccountingWorkspaceResponse | null;
  onRefreshLivePortfolioViews?: () => Promise<void> | void;
}

const structuredExportDownloadFormats = [
  { format: "json", label: "JSON" },
  { format: "csv", label: "CSV" },
  { format: "xls", label: "XLS" },
  { format: "xlsx", label: "XLSX" }
] as const;

// Concrete severity layer: reporting read-model badge variant → operator-readiness status
// string (Ready · Review · Action · Blocked · Info) consumed by SeverityBadge. Used for the
// run/approval/delivery STATUS chips; informational count/outline badges keep their neutral look.
const reportingStatusFromVariant: Record<
  "default" | "outline" | "success" | "warning" | "danger" | "paper" | "live" | "research",
  string
> = {
  success: "Ready",
  warning: "ReviewRequired",
  danger: "Blocked",
  outline: "Info",
  default: "Info",
  paper: "ReviewRequired",
  live: "Blocked",
  research: "Info"
};
const livePortfolioAutoRefreshIntervalMs = 60_000;
const LIVE_PORTFOLIO_FRESHNESS_BUDGET_MS = 2 * livePortfolioAutoRefreshIntervalMs;
const defaultExportsReportRunRequester = "browser-workstation";
const EXPORTS_VIEW_STATE_SCREEN = "reporting-exports";
const exportsViewReflectDebounceMs = 300;
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

export function ReportingScreen({ data, onRefreshLivePortfolioViews }: ReportingScreenProps) {
  const { pathname, search } = useLocation();
  const navigate = useNavigate();
  const vm = useReportingScreenViewModel(data?.reporting ?? null, undefined, pathname);
  const hubModel = useMemo(
    () => buildReportingHubModel(vm.runStatusRows, vm.templateRows, data?.reporting?.dailyWork ?? []),
    [data?.reporting?.dailyWork, vm.runStatusRows, vm.templateRows]
  );
  const reportPackProfileButtonRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const shouldFocusReportPackProfile = useRef(false);
  const [runActionStatus, setRunActionStatus] = useState<ReportingCommandStatus | null>(null);
  const [templateRunStatus, setTemplateRunStatus] = useState<ReportingCommandStatus | null>(null);
  const [templateLifecycleStatus, setTemplateLifecycleStatus] = useState<ReportingCommandStatus | null>(null);
  const [scheduleActionStatus, setScheduleActionStatus] = useState<ReportingCommandStatus | null>(null);
  const [deliveryFailureStatus, setDeliveryFailureStatus] = useState<ReportingCommandStatus | null>(null);
  const [scheduleDraft, setScheduleDraft] = useState<ReportingScheduleDraftState>(() => buildDefaultReportingScheduleDraft(data?.reporting ?? null));
  const [exportsRunDraft, setExportsRunDraft] = useState<ExportsReportRunDraftState>(() => buildDefaultExportsReportRunDraft(data?.reporting ?? null));
  const [templateRunDatasetSourceId, setTemplateRunDatasetSourceId] = useState(() => buildDefaultReportWriterDatasetSourceId(data?.reporting ?? null));
  const [templateRunAsOfDate, setTemplateRunAsOfDate] = useState<string>(() => todayIsoDate());
  const [brandingPackStatus, setBrandingPackStatus] = useState<ReportingCommandStatus | null>(null);
  const [livePortfolioRefreshStatus, setLivePortfolioRefreshStatus] = useState<ReportingCommandStatus | null>(null);
  const [brandingDraft, setBrandingDraft] = useState<ReportBrandingDraftState>(() => buildDefaultReportBrandingDraft(data?.reporting ?? null));
  const reportWriterDatasetSources = data?.reporting.reportWriterDatasetSources ?? [];
  const {
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
  } = useReportingReportWriter({
    datasetSources: reportWriterDatasetSources,
    buildDraftRequest: buildReportTemplateDraftRequest,
    buildRenderRequest: buildRenderReportTemplateRequest
  });
  const livePortfolioRefreshInFlight = useRef(false);
  const livePortfolioViews = data?.reporting.livePortfolioViews ?? [];
  const shouldAutoRefreshLivePortfolioViews = livePortfolioViews.some((view) => view.isMarketTickLinked || view.state === "LiveLinked");
  const runningRunActionId = runActionStatus?.state === "running" ? runActionStatus.id : null;
  const runningTemplateRunId = templateRunStatus?.state === "running" ? templateRunStatus.id : null;
  const runningTemplateLifecycleActionId = templateLifecycleStatus?.state === "running" ? templateLifecycleStatus.id : null;
  const runningScheduleActionId = scheduleActionStatus?.state === "running" ? scheduleActionStatus.id : null;
  const runningDeliveryFailureId = deliveryFailureStatus?.state === "running" ? deliveryFailureStatus.id : null;
  const runningBrandingThemeId = brandingPackStatus?.state === "running" ? brandingPackStatus.id : null;
  const isRefreshingLivePortfolioViews = livePortfolioRefreshStatus?.state === "running";
  const reportingFundProfileId = resolveReportingFundProfileId(data?.reporting ?? null);
  const writerGrids = vm.templateRows.flatMap((template) => template.writerGrids);
  const scheduleDistributionOptions = data?.reporting.reportPackDistributions ?? [];
  const isDailyReportingCockpitLanding = vm.taskMode.id === "daily-reporting-cockpit";
  const scheduleModel: ReportingScheduleManagementModel = {
    scheduleSummary: vm.scheduleSummary,
    hasScheduleRows: vm.hasScheduleRows,
    scheduleListLabel: vm.scheduleListLabel,
    scheduleRows: vm.scheduleRows,
    scheduleEmptyText: vm.scheduleEmptyText,
    scheduleDeliveryPlanSummary: vm.scheduleDeliveryPlanSummary,
    scheduleDeliveryPlanRows: vm.scheduleDeliveryPlanRows,
    hasScheduleDeliveryPlanRows: vm.hasScheduleDeliveryPlanRows,
    scheduleDeliveryPlanListLabel: vm.scheduleDeliveryPlanListLabel,
    scheduleDeliveryPlanEmptyText: vm.scheduleDeliveryPlanEmptyText
  };
  const exportsRunRows = vm.runStatusRows.filter(isExportsOnDemandRun);
  const templateRowsKey = vm.templateRows.map((template) => template.id).join("|");
  const selectedExportsTemplate = useMemo(
    () => resolveSelectedExportsTemplate(vm.templateRows, exportsRunDraft),
    [exportsRunDraft.templateRowId, templateRowsKey, vm.templateRows]
  );

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

  useEffect(() => {
    if (!onRefreshLivePortfolioViews || !shouldAutoRefreshLivePortfolioViews) {
      return;
    }

    const timer = window.setInterval(() => {
      void handleRefreshLivePortfolioViews("auto");
    }, livePortfolioAutoRefreshIntervalMs);

    return () => window.clearInterval(timer);
  }, [onRefreshLivePortfolioViews, shouldAutoRefreshLivePortfolioViews]);

  useEffect(() => {
    if (!data?.reporting) {
      return;
    }

    setExportsRunDraft((current) => {
      if (current.templateRowId && vm.templateRows.some((template) => template.id === current.templateRowId)) {
        return current;
      }

      return buildDefaultExportsReportRunDraft(data.reporting);
    });
  }, [data?.reporting, templateRowsKey]);

  const exportsViewHydratedRef = useRef(false);
  useEffect(() => {
    if (exportsViewHydratedRef.current || vm.templateRows.length === 0) {
      return;
    }

    exportsViewHydratedRef.current = true;
    const envelope = readViewStateFromSearch(search, EXPORTS_VIEW_STATE_SCREEN);
    if (!envelope) {
      return;
    }

    const templateRowId = typeof envelope.state.selectedExportsTemplateId === "string"
      ? envelope.state.selectedExportsTemplateId
      : null;
    const asOfDate = typeof envelope.state.asOfDate === "string" ? envelope.state.asOfDate : null;
    const template = templateRowId ? vm.templateRows.find((row) => row.id === templateRowId) : null;
    if (!template || !template.canRunOnDemand) {
      return;
    }

    setExportsRunDraft((current) => ({
      ...current,
      templateRowId: template.id,
      asOfDate: asOfDate ?? current.asOfDate
    }));
  }, [search, templateRowsKey, vm.templateRows]);

  const reflectExportsViewTimer = useRef<number | null>(null);
  const shouldReflectExportsViewState = useRef(false);
  const reflectExportsViewState = useCallback((nextDraft: ExportsReportRunDraftState) => {
    if (reflectExportsViewTimer.current !== null) {
      window.clearTimeout(reflectExportsViewTimer.current);
    }

    reflectExportsViewTimer.current = window.setTimeout(() => {
      reflectExportsViewTimer.current = null;
      const template = resolveSelectedExportsTemplate(vm.templateRows, nextDraft);
      const token = template
        ? encodeViewStateEnvelope({
            v: 1,
            screen: EXPORTS_VIEW_STATE_SCREEN,
            state: { selectedExportsTemplateId: template.id, asOfDate: nextDraft.asOfDate }
          })
        : null;

      // When the state cannot encode, strip any carried token so a stale view
      // param never lingers in the shareable URL.
      const params = new URLSearchParams(stripViewStateFromSearch(search));
      if (token) {
        params.set(VIEW_STATE_QUERY_KEY, token);
      }

      const nextSearch = params.toString();
      navigate(nextSearch ? `${pathname}?${nextSearch}` : pathname, { replace: true });
    }, exportsViewReflectDebounceMs);
  }, [navigate, pathname, search, vm.templateRows]);

  useEffect(() => () => {
    if (reflectExportsViewTimer.current !== null) {
      window.clearTimeout(reflectExportsViewTimer.current);
    }
  }, []);

  useEffect(() => {
    if (!shouldReflectExportsViewState.current) {
      return;
    }

    shouldReflectExportsViewState.current = false;
    reflectExportsViewState(exportsRunDraft);
  }, [exportsRunDraft, reflectExportsViewState]);

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

    await executeTemplateRun(template, {
      templateId: template.templateName,
      asOfDate: templateRunAsOfDate,
      maxRetries: 0,
      datasetSourceId: template.hasWriterGrids ? normalizeOptionalDatasetSourceId(templateRunDatasetSourceId) : null
    }, template.runActionLabel);
  }

  function updateExportsReportRunDraft(field: ExportsReportRunDraftField, value: string) {
    shouldReflectExportsViewState.current = true;
    setExportsRunDraft((current) => {
      return { ...current, [field]: value };
    });
  }

  async function handleExportsReportRun() {
    if (!selectedExportsTemplate || !selectedExportsTemplate.canRunOnDemand || runningTemplateRunId) {
      return;
    }

    await executeTemplateRun(
      selectedExportsTemplate,
      buildExportsReportRunRequest(selectedExportsTemplate, exportsRunDraft),
      "Exports report run"
    );
  }

  const handleExportsReportRunRef = useRef(handleExportsReportRun);
  handleExportsReportRunRef.current = handleExportsReportRun;

  const selectedExportsTemplateId = selectedExportsTemplate?.id ?? null;
  const selectedExportsTemplateName = selectedExportsTemplate?.name ?? null;
  const selectedExportsTemplateCanRun = selectedExportsTemplate?.canRunOnDemand ?? false;
  const selectedExportsTemplateRunDisabledReason = selectedExportsTemplate?.runDisabledReason ?? null;

  useEffect(() => {
    if (!selectedExportsTemplateId || !selectedExportsTemplateName) {
      return;
    }

    const disabled = !selectedExportsTemplateCanRun || Boolean(runningTemplateRunId);
    return registerCommandPaletteActions("reporting-screen", [
      {
        id: "reporting-run-exports",
        verbLabel: `Run exports report: ${selectedExportsTemplateName}`,
        description: "Start the selected on-demand exports report run with the drafted as-of date.",
        keywords: ["report", "export", "run"],
        confirm: true,
        disabled,
        disabledReason: runningTemplateRunId
          ? "A template run is already in progress."
          : selectedExportsTemplateRunDisabledReason,
        run: async () => {
          await handleExportsReportRunRef.current();
          return {
            title: `${selectedExportsTemplateName} run requested.`,
            detail: "Track progress under Reporting exports run status.",
            tone: "success" as const
          };
        }
      }
    ]);
  }, [
    runningTemplateRunId,
    selectedExportsTemplateCanRun,
    selectedExportsTemplateId,
    selectedExportsTemplateName,
    selectedExportsTemplateRunDisabledReason
  ]);

  async function executeTemplateRun(
    template: ReportingTemplateRow,
    request: ReportingRunRequest,
    statusLabel: string
  ) {
    setTemplateRunStatus({
      id: template.id,
      label: statusLabel,
      state: "running",
      message: `${template.name} is running.`,
      details: []
    });

    try {
      const result = await runReportingNow(request);
      setTemplateRunStatus({
        id: template.id,
        label: statusLabel,
        state: "success",
        message: `${template.name} run created.`,
        details: buildReportRunResultDetails(result.run)
      });
    } catch (error) {
      const display = describeApiError(error, `${template.name} run failed.`);
      setTemplateRunStatus({
        id: template.id,
        label: statusLabel,
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

  async function handleRefreshLivePortfolioViews(trigger: "manual" | "auto" = "manual") {
    if (!onRefreshLivePortfolioViews || livePortfolioRefreshInFlight.current) {
      return;
    }

    livePortfolioRefreshInFlight.current = true;
    setLivePortfolioRefreshStatus({
      id: "live-portfolio-views",
      label: "Refresh live portfolio views",
      state: "running",
      message: trigger === "auto" ? "Refreshing live portfolio views from market tick cadence." : "Refreshing live portfolio views.",
      details: [
        trigger === "auto"
          ? "Reporting is using the shared portfolio refresh lane on the live tick cadence."
          : "Reporting is using the shared portfolio refresh lane."
      ]
    });

    try {
      await onRefreshLivePortfolioViews();
      setLivePortfolioRefreshStatus({
        id: "live-portfolio-views",
        label: "Refresh live portfolio views",
        state: "success",
        message: trigger === "auto" ? "Live portfolio views refreshed from market tick cadence." : "Live portfolio views refreshed.",
        details: ["Portfolio summary data was refreshed through the shared workstation portfolio route."]
      });
    } catch (error) {
      const display = describeApiError(error, "Live portfolio view refresh failed.");
      setLivePortfolioRefreshStatus({
        id: "live-portfolio-views",
        label: "Refresh live portfolio views",
        state: "error",
        message: display.summary,
        details: display.details
      });
    } finally {
      livePortfolioRefreshInFlight.current = false;
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
        details = formatReportingScheduleRunDetails(result);
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

  async function handleSchedulePlanRun(plan: ReportingScheduleDeliveryPlanRow) {
    const statusId = `${plan.id}:run`;
    if (runningScheduleActionId) {
      return;
    }

    const label = `Run ${plan.scheduleId} for ${plan.recipient}`;
    setScheduleActionStatus({
      id: statusId,
      label,
      state: "running",
      message: `${label} is running.`,
      details: [`Delivery mode: ${plan.deliveryMode}`, `Formats: ${plan.formatsLabel}`]
    });

    try {
      const result = await runReportingScheduleNow(plan.scheduleId);
      const recipientAttempts = (result.deliveryAttempts ?? []).filter(
        (attempt) => attempt.distributionId === plan.distributionId || attempt.recipient === plan.recipient
      );
      setScheduleActionStatus({
        id: statusId,
        label,
        state: "success",
        message: `${label} completed.`,
        details: [
          ...formatReportingScheduleRunDetails(result),
          `Recipient deliveries: ${recipientAttempts.length}`,
          `Target: ${plan.recipient} via ${plan.deliveryMode}`
        ]
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

  function stageScheduleDraftDeliveryTarget() {
    setScheduleDraft((current) => {
      const target = buildCurrentScheduleDraftTarget(current);
      return {
        ...current,
        deliveryTargets: [
          ...current.deliveryTargets.filter((item) => item.distributionId !== target.distributionId),
          target
        ]
      };
    });
  }

  function removeScheduleDraftDeliveryTarget(distributionId: string) {
    setScheduleDraft((current) => ({
      ...current,
      deliveryTargets: current.deliveryTargets.filter((target) => target.distributionId !== distributionId)
    }));
  }

  async function saveScheduleDraft() {
    const statusId = "schedule-draft:save";
    if (runningScheduleActionId) {
      return;
    }

    const request = buildReportingScheduleUpsertRequest(scheduleDraft, brandingDraft);
    const targets = request.deliveryTargets ?? [];
    if (targets.some((target) => (target.formats ?? []).length === 0)) {
      setScheduleActionStatus({
        id: statusId,
        label: "Save reporting schedule",
        state: "error",
        message: "Select at least one report artifact format for every scheduled delivery target.",
        details: ["PDF, XLSX, or CSV must be selected before a delivery target can be saved."]
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
      const savedTargets = result.deliveryTargets?.length ? result.deliveryTargets : request.deliveryTargets ?? [];
      setScheduleActionStatus({
        id: statusId,
        label: "Save reporting schedule",
        state: "success",
        message: `Reporting schedule ${result.scheduleId} saved.`,
        details: [
          `Template: ${result.templateId}`,
          savedTargets.length > 0
            ? `Delivery targets: ${savedTargets.map((target) => `${target.distributionId} via ${target.deliveryMode ?? "SecurePortal"}`).join("; ")}`
            : "Delivery targets: none",
          `Formats: ${savedTargets.map((target) => `${target.distributionId}=${(target.formats ?? []).join("/")}`).join("; ")}`,
          result.brandingThemeOverride
            ? `Branding: ${result.brandingThemeOverride.name} · ${result.brandingThemeOverride.firmName} · ${result.brandingThemeOverride.themeId}`
            : result.brandingThemeId
              ? `Branding: ${result.brandingThemeId}`
              : "Branding: default theme"
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

  async function handleRunDueSchedules() {
    const statusId = "schedule-due:run";
    if (runningScheduleActionId) {
      return;
    }

    setScheduleActionStatus({
      id: statusId,
      label: "Run due reporting schedules",
      state: "running",
      message: "Due reporting schedules are running.",
      details: []
    });

    try {
      const result = await runDueReportingSchedules();
      const deliveryCount = result.runs.reduce((total, run) => total + (run.deliveryAttempts?.length ?? 0), 0);
      const warningDetails = result.runs.flatMap((run) => run.deliveryWarnings ?? []);
      setScheduleActionStatus({
        id: statusId,
        label: "Run due reporting schedules",
        state: "success",
        message: `Due schedule run completed for ${result.runs.length} schedule${result.runs.length === 1 ? "" : "s"}.`,
        details: [
          `Evaluated: ${result.evaluatedAtUtc}`,
          `Deliveries: ${deliveryCount}`,
          ...warningDetails.map((warning) => `Delivery warning: ${warning}`)
        ]
      });
    } catch (error) {
      const display = describeApiError(error, "Run due reporting schedules failed.");
      setScheduleActionStatus({
        id: statusId,
        label: "Run due reporting schedules",
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
  }

  async function handleRecordDeliveryFailure(attempt: ReportPackDeliveryAttempt) {
    const statusId = `${attempt.attemptId}:delivery-failure`;
    if (runningDeliveryFailureId || attempt.state === "Failed") {
      return;
    }

    const label = `Record ${attempt.recipient} delivery failure`;
    setDeliveryFailureStatus({
      id: statusId,
      label,
      state: "running",
      message: `${label} is running.`,
      details: [`Attempt: ${attempt.attemptId}`, `Distribution: ${attempt.distributionId}`]
    });

    try {
      const result = await recordReportPackDeliveryFailure(attempt.reportId, buildReportPackDeliveryFailureRequest(attempt));
      setDeliveryFailureStatus({
        id: statusId,
        label,
        state: "success",
        message: `${attempt.recipient} delivery failure recorded.`,
        details: [
          `Attempt ID: ${result.attemptId}`,
          `State: ${result.state}`,
          `Reason: ${result.failureReason ?? "Delivery failure recorded from Reporting workspace."}`
        ]
      });
    } catch (error) {
      const display = describeApiError(error, `${label} failed.`);
      setDeliveryFailureStatus({
        id: statusId,
        label,
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

  async function handlePreviewBrandedPack(theme: ReportBrandingTheme) {
    const statusId = `${theme.themeId}:preview`;
    if (!reportingFundProfileId || runningBrandingThemeId) {
      return;
    }

    setBrandingPackStatus({
      id: statusId,
      label: "Preview branded report pack",
      state: "running",
      message: `${theme.name} report pack preview is rendering.`,
      details: []
    });

    try {
      const result = await previewReportPack({
        fundProfileId: reportingFundProfileId,
        reportKind: "BoardPacket",
        brandingThemeId: theme.themeId
      });

      setBrandingPackStatus({
        id: statusId,
        label: "Preview branded report pack",
        state: "success",
        message: `${theme.name} report pack preview rendered.`,
        details: formatReportPackPreviewDetails(result)
      });
    } catch (error) {
      const display = describeApiError(error, `${theme.name} report pack preview failed.`);
      setBrandingPackStatus({
        id: statusId,
        label: "Preview branded report pack",
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
  }

  function updateBrandingDraft(field: ReportBrandingDraftField, value: string) {
    setBrandingDraft((current) => ({
      ...current,
      [field]: value
    }));
  }

  async function handleGenerateCustomBrandedPack() {
    const statusId = "custom-branding-override";
    if (!reportingFundProfileId || runningBrandingThemeId) {
      return;
    }

    const theme = buildReportBrandingOverride(brandingDraft);
    setBrandingPackStatus({
      id: statusId,
      label: "Generate custom branded report pack",
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
        brandingThemeOverride: theme,
        decisionRationale: `Generated from custom Reporting branding override ${theme.name}.`
      });

      setBrandingPackStatus({
        id: statusId,
        label: "Generate custom branded report pack",
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
        id: statusId,
        label: "Generate custom branded report pack",
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
  }

  async function handlePreviewCustomBrandedPack() {
    const statusId = "custom-branding-override:preview";
    if (!reportingFundProfileId || runningBrandingThemeId) {
      return;
    }

    const theme = buildReportBrandingOverride(brandingDraft);
    setBrandingPackStatus({
      id: statusId,
      label: "Preview custom branded report pack",
      state: "running",
      message: `${theme.name} report pack preview is rendering.`,
      details: []
    });

    try {
      const result = await previewReportPack({
        fundProfileId: reportingFundProfileId,
        reportKind: "BoardPacket",
        brandingThemeOverride: theme
      });

      setBrandingPackStatus({
        id: statusId,
        label: "Preview custom branded report pack",
        state: "success",
        message: `${theme.name} report pack preview rendered.`,
        details: formatReportPackPreviewDetails(result)
      });
    } catch (error) {
      const display = describeApiError(error, `${theme.name} report pack preview failed.`);
      setBrandingPackStatus({
        id: statusId,
        label: "Preview custom branded report pack",
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
      <ReportingWorkbenchContext
        taskMode={vm.taskMode}
        actions={vm.workbenchActions}
        chips={vm.workbenchChips}
      />

      <ReportingHub model={hubModel} />

      {isDailyReportingCockpitLanding ? (
        <ReportingTaskModeLauncher />
      ) : (
        <>
      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => (
          <MetricSnapshotCard key={metric.id} {...metric} />
        ))}
      </section>

      <section role="region" aria-label="Reporting access audit">
        <Card className="panel-surface" aria-label={vm.accessAudit.ariaLabel}>
          <CardHeader>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <div className="eyebrow-label">Report access</div>
                <CardTitle className="mt-2">User, group, and company scope</CardTitle>
              </div>
              <span className="flex flex-wrap items-center gap-1.5">
                <Badge variant={vm.accessAudit.postureVariant}>{vm.accessAudit.postureLabel}</Badge>
                <Badge variant="outline">{vm.accessAudit.evaluationScope}</Badge>
                <Badge variant="outline">{vm.accessAudit.hiddenTotalLabel}</Badge>
              </span>
            </div>
            <CardDescription>{vm.accessAudit.summary}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-[11px] uppercase text-muted-foreground">Matched principal scopes</div>
              <div className="mt-1 break-words font-mono text-xs text-foreground">{vm.accessAudit.scopeLabel}</div>
            </div>
            <div role="list" aria-label="Reporting access visible and hidden counts" className="grid gap-2 md:grid-cols-5">
              {vm.accessAudit.countRows.map((row) => (
                <div key={row.id} role="listitem" className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
                  <div className="text-xs font-semibold text-foreground">{row.label}</div>
                  <div className="mt-2 flex flex-wrap gap-1.5">
                    <Badge variant="success">{row.visibleLabel}</Badge>
                    <Badge variant={row.hasHidden ? "warning" : "outline"}>{row.hiddenLabel}</Badge>
                  </div>
                </div>
              ))}
            </div>
            {vm.accessAudit.hasDenialReasons ? (
              <ul aria-label="Reporting access denial reasons" className="grid gap-1.5 text-xs leading-5 text-muted-foreground">
                {vm.accessAudit.denialReasons.map((reason) => (
                  <li key={reason}>{reason}</li>
                ))}
              </ul>
            ) : null}
          </CardContent>
        </Card>
      </section>

      {data.reporting.reportLineProvenanceExplorer ? (
        <FinancialRecordExplorerShell
          className="report-line-provenance-explorer"
          explorerLabel="Report-line provenance"
          title="Report-Line Provenance Explorer"
          titleId="report-line-provenance-explorer-title"
          description="Drill from governed report lines into retained source records, reconciliations, journals, approvals, delivery history, and restatement evidence."
          scopeItems={[]}
          savedViews={[]}
          summaryItems={[]}
          appliedFilters={[]}
          explorer={data.reporting.reportLineProvenanceExplorer}
        >
          {null}
        </FinancialRecordExplorerShell>
      ) : null}

      {(data.reporting.portfolioCuts ?? []).length > 0 ? (
        <section role="region" aria-label="Portfolio reporting cuts">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Portfolio cuts</div>
              <CardTitle>Exposure, cash, P&L, and shadow NAV</CardTitle>
              <CardDescription>Fund, strategy, and tag views are projected from shared portfolio and NAV reporting data.</CardDescription>
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
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <CardTitle>Tick-linked portfolio reporting</CardTitle>
                  <CardDescription>Reporting cuts carry shared live-summary routes, source freshness, liquidity, and cash-ladder evidence.</CardDescription>
                </div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => void handleRefreshLivePortfolioViews()}
                  disabled={!onRefreshLivePortfolioViews || isRefreshingLivePortfolioViews}
                  aria-label="Refresh live portfolio reporting views"
                >
                  <RotateCcw className="h-4 w-4" aria-hidden="true" />
                  Refresh
                </Button>
              </div>
              {livePortfolioRefreshStatus ? (
                <div className="pt-2">
                  <ReportingCommandStatusView status={livePortfolioRefreshStatus} />
                </div>
              ) : null}
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
                      <ReportingCutMetric label="Settlement" value={formatReportingMoney(view.pendingSettlement, view.currency)} />
                      <ReportingCutMetric label="P&L" value={formatReportingMoney(view.totalPnl, view.currency)} />
                      <ReportingCutMetric label="Shadow NAV" value={formatReportingMoney(view.shadowNav, view.currency)} />
                    </dl>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{view.liquiditySummary}</p>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{view.telemetrySummary}</p>
                    {view.tickFreshnessSummary ? (
                      <p className="mt-2 text-xs leading-5 text-muted-foreground">{view.tickFreshnessSummary}</p>
                    ) : null}
                    {(view.readinessBlockers ?? []).length > 0 ? (
                      <ul aria-label={`${view.label} readiness blockers`} className="mt-2 space-y-1 text-xs text-destructive">
                        {(view.readinessBlockers ?? []).map((blocker) => (
                          <li key={blocker} className="flex gap-1.5 leading-5">
                            <XCircle className="mt-0.5 h-3.5 w-3.5 flex-none" aria-hidden="true" />
                            <span>{blocker}</span>
                          </li>
                        ))}
                      </ul>
                    ) : null}
                    <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">
                      {view.sourceCount} source{view.sourceCount === 1 ? "" : "s"} · {view.sourceAsOfUtc ?? view.asOf}
                    </p>
                    <p className="mt-1 flex flex-wrap items-center gap-2 break-all font-mono text-[11px] text-muted-foreground">
                      <span>Freshness: {view.state} · cut={view.asOf} · source={view.sourceAsOfUtc ?? "unavailable"}</span>
                      <FreshnessChip
                        label={`${view.label} source data`}
                        staleBudgetMs={LIVE_PORTFOLIO_FRESHNESS_BUDGET_MS}
                        timestamp={view.sourceAsOfUtc ?? null}
                      />
                    </p>
                    <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">
                      Market tick: {view.isMarketTickLinked ? "linked" : "snapshot"} · provider={view.marketDataProvider ?? "unavailable"} · age={view.marketTickAgeSeconds ?? "n/a"}s · seq={view.marketTickSequence ?? "n/a"} · tick={view.marketTickAsOfUtc ?? view.sourceAsOfUtc ?? "unavailable"}
                    </p>
                    {view.freshnessPolicy ? (
                      <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">
                        Policy: {view.freshnessPolicy.policyName} · evaluated={view.freshnessPolicy.evaluatedAtUtc} · sourceAge={view.freshnessPolicy.sourceAgeSeconds ?? "n/a"}s · liveWindow={view.freshnessPolicy.liveLinkWindowSeconds}s · staleWindow={view.freshnessPolicy.staleWindowSeconds}s
                      </p>
                    ) : null}
                    {view.freshnessPolicy?.reason ? (
                      <p className="mt-2 text-xs leading-5 text-muted-foreground">{view.freshnessPolicy.reason}</p>
                    ) : null}
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
                      <ReportingCutMetric label="Shadow NAV" value={formatReportingMoney(row.shadowNav, row.currency)} />
                      <ReportingCutMetric label="Variance" value={formatReportingMoney(row.shadowNavVariance, row.currency)} />
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
                      <ReportingCutMetric
                        label="Lineage"
                        value={(structuredExport.rowLineageCount ?? structuredExport.rowCount).toLocaleString()}
                      />
                    </dl>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">
                      {structuredExport.consumer} · {structuredExport.validationSummary ?? structuredExport.dataset}
                    </p>
                    <div className="mt-2 space-y-1 break-all font-mono text-[11px] text-muted-foreground">
                      <p>Dataset: {structuredExport.dataset}</p>
                      <p>As of: {structuredExport.asOf}</p>
                      <p>API route: {structuredExport.route}</p>
                    </div>
                    {!structuredExport.isReady && structuredExport.readinessBlockers?.length ? (
                      <ul
                        aria-label={`${structuredExport.label} structured export readiness blockers`}
                        className="mt-2 space-y-1 rounded-md border border-warning/30 bg-warning/10 px-2 py-2 text-xs leading-5 text-warning"
                      >
                        {structuredExport.readinessBlockers.map((blocker) => (
                          <li key={blocker}>{blocker}</li>
                        ))}
                      </ul>
                    ) : null}
                    <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">Retained path: {structuredExport.retainedPath}</p>
                    {structuredExport.retainedManifestPath ? (
                      <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">
                        Manifest: {structuredExport.retainedManifestPath}
                      </p>
                    ) : null}
                    {structuredExport.integritySummary ? (
                      <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">
                        Integrity: {structuredExport.integritySummary}
                      </p>
                    ) : null}
                    {structuredExport.integrityHashSha256 ? (
                      <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">
                        SHA-256: {structuredExport.integrityHashSha256}
                      </p>
                    ) : null}
                    <div className="mt-2 flex flex-wrap gap-1.5">
                      {structuredExport.dataDictionaryRoute ? (
                        <a
                          className="inline-flex items-center gap-1 rounded-sm border border-border/70 px-2 py-1 text-[11px] font-medium text-primary underline-offset-2 hover:underline"
                          href={structuredExport.dataDictionaryRoute}
                          aria-label={`Open ${structuredExport.label} data dictionary`}
                        >
                          <FileText className="h-3.5 w-3.5" aria-hidden="true" />
                          Data dictionary
                        </a>
                      ) : null}
                      {structuredExport.evidenceRoute ? (
                        <a
                          className="inline-flex items-center gap-1 rounded-sm border border-border/70 px-2 py-1 text-[11px] font-medium text-primary underline-offset-2 hover:underline"
                          href={structuredExport.evidenceRoute}
                          aria-label={`Open ${structuredExport.label} evidence`}
                        >
                          <FileText className="h-3.5 w-3.5" aria-hidden="true" />
                          Evidence
                        </a>
                      ) : null}
                    </div>
                    {(structuredExport.tags ?? []).length > 0 ? (
                      <div role="group" className="mt-2 flex flex-wrap gap-1.5" aria-label={`${structuredExport.label} export tags`}>
                        {(structuredExport.tags ?? []).map((tag) => (
                          <Badge key={tag} variant="outline">{tag}</Badge>
                        ))}
                      </div>
                    ) : null}
                    <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
                      <span className="break-all font-mono text-[11px] text-muted-foreground">
                        {structuredExport.versionStamp ?? structuredExport.asOf}
                      </span>
                      <span className="flex flex-wrap justify-end gap-1.5">
                        {structuredExportDownloadFormats.map((download) => (
                          structuredExport.isReady ? (
                            <Button asChild key={download.format} variant="outline" size="sm">
                              <a
                                href={buildStructuredExportDownloadHref(structuredExport.route, download.format)}
                                target="_blank"
                                rel="noreferrer"
                                aria-label={`Download ${structuredExport.label} structured export as ${download.label}`}
                              >
                                <FileText className="h-4 w-4" aria-hidden="true" />
                                {download.label}
                              </a>
                            </Button>
                          ) : (
                            <Button
                              key={download.format}
                              variant="outline"
                              size="sm"
                              disabled
                              aria-label={`${structuredExport.label} structured export ${download.label} download blocked`}
                            >
                              <FileText className="h-4 w-4" aria-hidden="true" />
                              {download.label}
                            </Button>
                          )
                        ))}
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        </section>
      ) : null}

      <ReportingBrandingAccessPanel
        themes={data.reporting.brandingThemes ?? []}
        draft={brandingDraft}
        status={brandingPackStatus}
        runningBrandingThemeId={runningBrandingThemeId}
        reportingFundProfileId={reportingFundProfileId}
        onDraftChange={updateBrandingDraft}
        onPreviewTheme={handlePreviewBrandedPack}
        onGenerateTheme={handleGenerateBrandedPack}
        onPreviewCustom={handlePreviewCustomBrandedPack}
        onGenerateCustom={handleGenerateCustomBrandedPack}
      />

      {writerGrids.length > 0 ? (
        <ReportingReportWriterSection>
          {writerGrids.map((grid) => (
            <ReportWriterDesignerGrid
              key={grid.id}
              grid={grid}
              settings={getWriterDraftSettings(grid)}
              customFormula={getWriterCustomFormula(grid)}
              chartDraft={getWriterChartDraft(grid)}
              formatRules={getWriterFormatRules(grid)}
              datasetSources={reportWriterDatasetSources}
              isSaving={savingWriterDraftId === grid.id}
              isPreviewing={previewingWriterDraftId === grid.id}
              preview={writerPreviewByGridId[grid.id] ?? null}
              previousPreview={writerPreviousPreviewByGridId[grid.id] ?? null}
              getZoneTokens={getWriterZoneTokens}
              onTokenDragStart={handleWriterTokenDragStart}
              onZoneDrop={handleWriterZoneDrop}
              onTokenRemove={removeWriterZoneToken}
              onTokenMove={moveWriterZoneToken}
              onReset={resetWriterGrid}
              onSettingsChange={updateWriterDraftSetting}
              onCustomFormulaChange={updateWriterCustomFormula}
              customDatasetText={writerCustomDatasetText[grid.id] ?? ""}
              onCustomDatasetChange={updateWriterCustomDataset}
              onChartDraftChange={updateWriterChartDraft}
              onFormatRuleAdd={addWriterFormatRule}
              onFormatRuleRemove={removeWriterFormatRule}
              onFormatRuleChange={updateWriterFormatRule}
              onPreview={previewWriterGrid}
              onSave={saveWriterGridDraft}
            />
          ))}
          {writerPreviewStatus ? (
            <div className="mt-3 xl:col-span-2">
              <ReportingCommandStatusView status={writerPreviewStatus} />
            </div>
          ) : null}
          {writerDraftStatus ? (
            <div className="mt-3 xl:col-span-2">
              <ReportingCommandStatusView status={writerDraftStatus} />
            </div>
          ) : null}
        </ReportingReportWriterSection>
      ) : null}

      <ExportsReportRunner
        draft={exportsRunDraft}
        templates={vm.templateRows}
        selectedTemplate={selectedExportsTemplate}
        datasetSources={reportWriterDatasetSources}
        recentRuns={exportsRunRows}
        status={templateRunStatus}
        runningTemplateRunId={runningTemplateRunId}
        defaultRequester={defaultExportsReportRunRequester}
        onDraftChange={updateExportsReportRunDraft}
        onRun={() => void handleExportsReportRun()}
      />

      <section className="grid gap-4 xl:grid-cols-[0.9fr_1.1fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">Template families</div>
            <CardTitle>Governed report templates</CardTitle>
            <CardDescription>Investor statements, SEC packets, and shadow NAV packs share the same run contract.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            <ReportingPeriodSwitcher
              asOfDate={templateRunAsOfDate}
              onSelect={setTemplateRunAsOfDate}
              disabled={Boolean(runningTemplateRunId)}
            />
            <p className="text-[11px] leading-4 text-muted-foreground">
              On-demand template runs use this as-of period. Switch periods to regenerate the same report for a prior month, quarter, or year.
            </p>
            {vm.templateRows.map((template) => (
              <div key={template.id} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-semibold text-foreground">{template.name}</span>
                  <span className="flex flex-wrap items-center gap-1.5">
                    <SeverityBadge status={reportingStatusFromVariant[template.statusVariant]} label={template.statusLabel} />
                    <Badge variant="outline">{template.sourceLabel}</Badge>
                    <Badge variant="outline">{template.family}</Badge>
                    <Badge variant="outline">{template.accessMode}</Badge>
                  </span>
                </div>
                <p className="mt-1 text-xs text-muted-foreground">
                  {template.version} · {template.sectionSummary} · <span className="font-mono">{template.id}</span>
                </p>
                <div
                  role="group"
                  aria-label={`${template.name} template audit and version lineage`}
                  className="mt-2 grid gap-2 rounded-md border border-border/60 bg-background/25 px-2 py-2 text-xs md:grid-cols-2"
                >
                  <span className="min-w-0">
                    <span className="block text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Version</span>
                    <span className="mt-1 block break-words text-foreground">{template.versionLineageSummary}</span>
                  </span>
                  <span className="min-w-0">
                    <span className="block text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Audit</span>
                    <span className="mt-1 block break-words text-foreground">
                      {template.auditTrailSummary} · {template.lastAuditSummary}
                    </span>
                  </span>
                  <span className="min-w-0">
                    <span className="block text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Approval</span>
                    <span className="mt-1 block break-words text-foreground">
                      {template.latestApprovedLabel} · {template.decisionSummary}
                    </span>
                  </span>
                  <span className="min-w-0">
                    <span className="block text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Validation</span>
                    <span className="mt-1 block break-words text-foreground">{template.validationSummary}</span>
                  </span>
                </div>
                <div
                  role="group"
                  aria-label={template.accessGovernance.ariaLabel}
                  className="mt-2 rounded-md border border-border/60 bg-background/25 px-2 py-2 text-xs"
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="flex flex-wrap items-center gap-1.5">
                      <Badge variant="outline">{template.accessGovernance.modeLabel}</Badge>
                      <Badge variant="outline">{template.accessGovernance.scopeLabel}</Badge>
                      <Badge variant={template.accessGovernance.postureVariant}>{template.accessGovernance.postureLabel}</Badge>
                    </span>
                    <span className="break-all font-mono text-[11px] text-muted-foreground">{template.accessMode}</span>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{template.accessGovernance.detail}</p>
                </div>
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
                {template.hasWriterGrids && reportWriterDatasetSources.length > 0 ? (
                  <label className="mt-2 block space-y-1">
                    <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Run dataset</span>
                    <Select
                      value={templateRunDatasetSourceId}
                      onChange={(event) => setTemplateRunDatasetSourceId(event.target.value)}
                      aria-label={`${template.name} on-demand report-writer dataset source`}
                    >
                      <option value="">Default retained dataset</option>
                      {reportWriterDatasetSources.map((source) => (
                        <option key={source.sourceId} value={source.sourceId}>
                          {source.label} ({source.rowCount})
                        </option>
                      ))}
                    </Select>
                  </label>
                ) : null}
              </div>
            ))}
            {templateRunStatus && templateRunStatus.label !== "Exports report run" ? (
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
                  <SeverityBadge status={run.status} label={run.status} />
                </div>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  {run.family} · {run.trigger} · {run.lineageSummary} · {run.auditSummary}
                </p>
                <dl className="mt-2 grid gap-2 text-xs sm:grid-cols-2 xl:grid-cols-3" aria-label={`${run.id} audit metadata`}>
                    <div>
                      <dt className="text-[11px] uppercase text-muted-foreground">Run ID</dt>
                      <dd className="break-all font-mono text-foreground">{run.runIdLabel}</dd>
                    </div>
                      <ReportingRunVersionFields run={run} />
                      <div>
                        <dt className="text-[11px] uppercase text-muted-foreground">Template</dt>
                        <dd className="break-all font-mono text-foreground">{run.templateLabel}</dd>
                    </div>
                  <div>
                    <dt className="text-[11px] uppercase text-muted-foreground">As of</dt>
                    <dd className="font-mono text-foreground">{run.asOfDateLabel}</dd>
                  </div>
                  <div>
                    <dt className="text-[11px] uppercase text-muted-foreground">Trigger</dt>
                    <dd className="text-foreground">{run.trigger}</dd>
                  </div>
                    <div>
                      <dt className="text-[11px] uppercase text-muted-foreground">Attempts</dt>
                      <dd className="text-foreground">{run.attemptLabel}</dd>
                    </div>
                      <div>
                        <dt className="text-[11px] uppercase text-muted-foreground">Sections</dt>
                        <dd className="text-foreground">{run.sectionLabel}</dd>
                      </div>
                  <div>
                    <dt className="text-[11px] uppercase text-muted-foreground">Lineage</dt>
                    <dd className="text-foreground">{run.lineageLabel}</dd>
                  </div>
                  <div className="sm:col-span-2 xl:col-span-3">
                    <dt className="text-[11px] uppercase text-muted-foreground">Artifacts</dt>
                    <dd className="break-all font-mono text-foreground">
                      {run.hasArtifacts ? `${run.artifactLabel}: ${run.artifactNames.join(", ")}` : run.artifactLabel}
                    </dd>
                  </div>
                  <div className="sm:col-span-2 xl:col-span-3">
                    <dt className="text-[11px] uppercase text-muted-foreground">Dataset source</dt>
                    <dd className="break-all font-mono text-foreground">{run.datasetSourceLabel}</dd>
                  </div>
                  <div className="sm:col-span-2 xl:col-span-3">
                    <dt className="text-[11px] uppercase text-muted-foreground">Generated grids</dt>
                    <dd className="break-all font-mono text-foreground">
                      {run.hasGeneratedGrids ? `${run.generatedGridLabel}: ${run.generatedGridNames.join(", ")}` : run.generatedGridLabel}
                    </dd>
                      <ReportingGeneratedGridExportLinks run={run} />
                    </div>
                </dl>
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

      <ReportingPrivateCapitalReadinessPanel data={data} />

      <section className="grid gap-4 xl:grid-cols-[0.95fr_1.05fr]">
        <ReportingScheduleManagementPanel
          model={scheduleModel}
          scheduleDraft={scheduleDraft}
          distributionOptions={scheduleDistributionOptions}
          datasetSources={reportWriterDatasetSources}
          status={scheduleActionStatus}
          runningScheduleActionId={runningScheduleActionId}
          onDraftChange={updateScheduleDraft}
          onToggleFormat={toggleScheduleDraftFormat}
          onStageTarget={stageScheduleDraftDeliveryTarget}
          onRemoveTarget={removeScheduleDraftDeliveryTarget}
          onSaveDraft={saveScheduleDraft}
          onRunDue={handleRunDueSchedules}
          onScheduleAction={handleScheduleAction}
          onSchedulePlanRun={handleSchedulePlanRun}
        />

        <ReportingDeliveryHistoryPanel
          deliveryAttempts={data.reporting.deliveryAttempts ?? []}
          deliveryFailureStatus={deliveryFailureStatus}
          runningDeliveryFailureId={runningDeliveryFailureId}
          onRecordDeliveryFailure={handleRecordDeliveryFailure}
        />
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
              <SeverityBadge
                status={reportingStatusFromVariant[vm.workflowTaskPanel.statusVariant]}
                label={vm.workflowTaskPanel.statusLabel}
              />
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
                <SeverityBadge
                  status={reportingStatusFromVariant[vm.workflowTaskPanel.publicationReview.statusVariant]}
                  label={vm.workflowTaskPanel.publicationReview.statusLabel}
                />
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
              <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
                <div className="eyebrow-label">{vm.workflowTaskPanel.publicationReview.evidenceLinksLabel}</div>
                <Badge variant="outline">
                  {vm.workflowTaskPanel.publicationReview.evidenceLinks.length} link{vm.workflowTaskPanel.publicationReview.evidenceLinks.length === 1 ? "" : "s"}
                </Badge>
              </div>
              {vm.workflowTaskPanel.publicationReview.hasEvidenceLinks ? (
                <div role="list" aria-label={vm.workflowTaskPanel.publicationReview.evidenceLinksLabel} className="mt-2 grid gap-2">
                  {vm.workflowTaskPanel.publicationReview.evidenceLinks.map((evidence) => (
                    <div
                      key={evidence.id}
                      role="listitem"
                      aria-label={evidence.ariaLabel}
                      className="rounded-md border border-border/70 bg-background/40 px-3 py-2"
                    >
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <span className="break-all font-mono text-xs text-foreground">{evidence.label}</span>
                        <Badge variant="outline">{evidence.capturedLabel}</Badge>
                      </div>
                      <p className="mt-1 break-all text-xs leading-5 text-muted-foreground">{evidence.sourceLabel}</p>
                      {evidence.href ? (
                        <a
                          href={evidence.href}
                          className="mt-2 inline-flex text-xs text-primary underline-offset-2 hover:underline"
                          aria-label={`Open ${evidence.label} publication evidence`}
                        >
                          {evidence.href}
                        </a>
                      ) : null}
                    </div>
                  ))}
                </div>
              ) : (
                <p role="status" className="mt-2 rounded-md border border-border/70 bg-background/40 px-3 py-2 text-sm leading-6 text-muted-foreground">
                  {vm.workflowTaskPanel.publicationReview.evidenceLinksEmptyText}
                </p>
              )}
              <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
                <div className="eyebrow-label">{vm.workflowTaskPanel.publicationReview.lineProvenanceLabel}</div>
                <Badge variant="outline">
                  {vm.workflowTaskPanel.publicationReview.lineProvenanceRows.length} line{vm.workflowTaskPanel.publicationReview.lineProvenanceRows.length === 1 ? "" : "s"}
                </Badge>
              </div>
              {vm.workflowTaskPanel.publicationReview.hasLineProvenance ? (
                <div role="list" aria-label={vm.workflowTaskPanel.publicationReview.lineProvenanceLabel} className="mt-2 grid gap-2">
                  {vm.workflowTaskPanel.publicationReview.lineProvenanceRows.map((line) => (
                    <div
                      key={line.id}
                      role="listitem"
                      aria-label={line.ariaLabel}
                      className="rounded-md border border-border/70 bg-background/40 px-3 py-2"
                    >
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <span className="break-all font-mono text-xs text-foreground">{line.lineKey}</span>
                        <Badge variant="outline">{line.valueLabel}</Badge>
                      </div>
                      <p className="mt-1 break-all text-xs leading-5 text-muted-foreground">{line.sourceLabel}</p>
                      <div className="mt-2 flex flex-wrap gap-3 text-xs">
                        {line.financialRecordHref ? (
                          <a
                            href={line.financialRecordHref}
                            className="inline-flex items-center gap-1 text-primary underline-offset-2 hover:underline"
                            aria-label={`${line.financialRecordLabel} for ${line.lineKey}`}
                          >
                            <Network className="h-3 w-3" aria-hidden="true" />
                            {line.financialRecordLabel}
                          </a>
                        ) : (
                          <span className="text-muted-foreground">{line.financialRecordLabel} unavailable</span>
                        )}
                        {line.evidenceHref ? (
                          <a
                            href={line.evidenceHref}
                            className="text-primary underline-offset-2 hover:underline"
                            aria-label={`Open retained evidence for ${line.lineKey}`}
                          >
                            {line.evidenceLabel}
                          </a>
                        ) : (
                          <span className="text-muted-foreground">{line.evidenceLabel}</span>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                <p role="status" className="mt-2 rounded-md border border-border/70 bg-background/40 px-3 py-2 text-sm leading-6 text-muted-foreground">
                  {vm.workflowTaskPanel.publicationReview.lineProvenanceEmptyText}
                </p>
              )}
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
                <SeverityBadge
                  status={reportingStatusFromVariant[vm.workflowTaskPanel.restatementReview.statusVariant]}
                  label={vm.workflowTaskPanel.restatementReview.statusLabel}
                />
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
                <div className="eyebrow-label">Export service</div>
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
                      <span>State: {target.stateLabel}</span>
                      <span>Owner: {target.ownerLabel}</span>
                      <span>Due: {target.dueLabel}</span>
                      <span>Last sent: {target.lastSentLabel}</span>
                    </div>
                    <a
                      className="mt-2 inline-flex break-all font-mono text-[11px] font-medium text-primary underline-offset-2 hover:underline"
                      href={target.href}
                      aria-label={`Open ${target.label} report-pack distribution route`}
                    >
                      {target.href}
                    </a>
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
        </>
      )}
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

function ReportingCutMetric({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words font-mono text-xs text-foreground">{value}</dd>
    </div>
  );
}

function buildStructuredExportDownloadHref(
  route: string,
  format: (typeof structuredExportDownloadFormats)[number]["format"]
): string {
  const [path, query = ""] = route.split("?", 2);
  const params = new URLSearchParams(query);
  if (format === "json") {
    params.delete("format");
  } else {
    params.set("format", format);
  }

  const queryString = params.toString();
  return queryString ? `${path}?${queryString}` : path;
}

function buildDefaultExportsReportRunDraft(reporting: AccountingWorkspaceResponse["reporting"] | null): ExportsReportRunDraftState {
  const template = reporting?.templates?.find((item) => (item.isAccessible ?? true) && (item.lifecycleStatus ?? "Approved") === "Approved")
    ?? reporting?.templates?.[0]
    ?? null;

  return {
    templateRowId: template ? `${template.templateId}:${template.version}` : "",
    asOfDate: new Date().toISOString().slice(0, 10),
    maxRetries: "0",
    requestedBy: defaultExportsReportRunRequester,
    datasetSourceId: buildDefaultReportWriterDatasetSourceId(reporting)
  };
}

function resolveSelectedExportsTemplate(
  templates: ReportingTemplateRow[],
  draft: ExportsReportRunDraftState
): ReportingTemplateRow | null {
  return templates.find((template) => template.id === draft.templateRowId)
    ?? templates.find((template) => template.canRunOnDemand)
    ?? templates[0]
    ?? null;
}

export function buildExportsReportRunRequest(
  template: ReportingTemplateRow,
  draft: ExportsReportRunDraftState
): ReportingRunRequest {
  return {
    templateId: template.templateName,
    asOfDate: normalizeDraftText(draft.asOfDate, new Date().toISOString().slice(0, 10)),
    maxRetries: parseExportsReportMaxRetries(draft.maxRetries),
    requestedBy: normalizeDraftText(draft.requestedBy, defaultExportsReportRunRequester),
    datasetSourceId: template.hasWriterGrids ? normalizeOptionalDatasetSourceId(draft.datasetSourceId) : null
  };
}

function parseExportsReportMaxRetries(value: string): number {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
}

export function isExportsOnDemandRun(run: ReportingRunStatusRow): boolean {
  const trigger = run.trigger.trim().toLowerCase().replace(/[^a-z]/g, "");
  return trigger === "adhoc" || trigger === "ondemand" || trigger === "manual";
}

export function buildReportRunResultDetails(run: {
  runId: string;
  status: string;
  trigger: string;
  asOfDate?: string | null;
  reportWriterDatasetSourceLabel?: string | null;
  reportWriterDatasetSourceId?: string | null;
  reportWriterDatasetRowCount?: number | null;
}): string[] {
  const details = [`Run ID: ${run.runId}`, `Status: ${run.status}`, `Trigger: ${run.trigger}`];
  if (run.asOfDate) {
    details.push(`As of: ${run.asOfDate}`);
  }

  const source = run.reportWriterDatasetSourceLabel?.trim() || run.reportWriterDatasetSourceId?.trim();
  if (source) {
    details.push(run.reportWriterDatasetRowCount == null ? `Dataset: ${source}` : `Dataset: ${source} (${run.reportWriterDatasetRowCount} rows)`);
  }

  return details;
}

function buildDefaultReportingScheduleDraft(reporting: AccountingWorkspaceResponse["reporting"] | null): ReportingScheduleDraftState {
  const schedule = reporting?.schedules?.[0] ?? null;
  const firstTarget = schedule?.deliveryTargets?.[0] ?? null;
  const template = reporting?.templates?.find((item) => item.isLatestApproved || item.lifecycleStatus === "Approved" || item.isBuiltIn)
    ?? reporting?.templates?.[0]
    ?? null;
  const distribution = reporting?.reportPackDistributions?.find((item) => item.distributionId === firstTarget?.distributionId)
    ?? reporting?.reportPackDistributions?.[0]
    ?? null;
  const templateId = normalizeIdentifierToken(schedule?.templateId ?? template?.templateId, "investor-monthly-statement");
  const scheduleId = normalizeIdentifierToken(schedule?.scheduleId, `sched-${templateId}`);
  const nextAsOfDate = normalizeDraftText(schedule?.nextAsOfDate, new Date().toISOString().slice(0, 10));
  const dueAtUtc = normalizeDraftText(schedule?.dueAtUtc, `${nextAsOfDate}T20:00:00Z`);

  return {
    scheduleId,
    templateId,
    cronExpression: normalizeDraftText(schedule?.cronExpression, "0 8 1 * *"),
    nextAsOfDate,
    dueAtUtc,
    maxRetries: String(Math.max(0, schedule?.maxRetries ?? 1)),
    requestedBy: normalizeDraftText(schedule?.requestedBy ?? distribution?.owner, "browser-workstation"),
    description: normalizeDraftText(schedule?.description, "Scheduled governed report pack."),
    datasetSourceId: normalizeOptionalDatasetSourceId(schedule?.datasetSourceId ?? buildDefaultReportWriterDatasetSourceId(reporting)) ?? "",
    distributionId: normalizeIdentifierToken(firstTarget?.distributionId ?? distribution?.distributionId, "board-reporting-committee"),
    deliveryMode: normalizeReportingScheduleDeliveryMode(firstTarget?.deliveryMode),
    deliveryNote: normalizeDraftText(firstTarget?.note ?? distribution?.pendingSummary, ""),
    formats: buildScheduleFormatSelection(firstTarget?.formats),
    deliveryTargets: (schedule?.deliveryTargets ?? []).map(normalizeScheduleDraftTarget)
  };
}

function buildDefaultReportWriterDatasetSourceId(reporting: AccountingWorkspaceResponse["reporting"] | null): string {
  return reporting?.reportWriterDatasetSources?.[0]?.sourceId ?? "";
}

function normalizeOptionalDatasetSourceId(sourceId: string | null | undefined): string | null {
  const normalized = sourceId?.trim();
  return normalized ? normalized : null;
}

function buildReportingScheduleUpsertRequest(
  draft: ReportingScheduleDraftState,
  brandingDraft: ReportBrandingDraftState
): ReportingScheduleUpsertRequest {
  const scheduleId = normalizeIdentifierToken(draft.scheduleId, "sched-reporting-pack");
  const templateId = normalizeIdentifierToken(draft.templateId, "investor-monthly-statement");
  const nextAsOfDate = normalizeDraftText(draft.nextAsOfDate, new Date().toISOString().slice(0, 10));
  const deliveryNote = normalizeDraftText(draft.deliveryNote, "");
  const brandingThemeOverride = buildReportBrandingOverride(brandingDraft);

  return {
    scheduleId,
    templateId,
    cronExpression: normalizeDraftText(draft.cronExpression, "0 8 1 * *"),
    nextAsOfDate,
    dueAtUtc: normalizeDraftText(draft.dueAtUtc, `${nextAsOfDate}T20:00:00Z`),
    maxRetries: parseScheduleMaxRetries(draft.maxRetries),
    requestedBy: normalizeDraftText(draft.requestedBy, "browser-workstation"),
    description: normalizeDraftText(draft.description, "Scheduled governed report pack."),
    state: "Active",
    deliveryTargets: buildReportingScheduleDeliveryTargets(draft, deliveryNote),
    datasetSourceId: normalizeOptionalDatasetSourceId(draft.datasetSourceId),
    brandingThemeId: brandingThemeOverride.themeId,
    brandingThemeOverride
  };
}

function buildReportingScheduleDeliveryTargets(
  draft: ReportingScheduleDraftState,
  currentDeliveryNote: string
): ReportingScheduleUpsertRequest["deliveryTargets"] {
  const targets = new Map<string, NonNullable<ReportingScheduleUpsertRequest["deliveryTargets"]>[number]>();
  for (const target of [...draft.deliveryTargets, buildCurrentScheduleDraftTarget(draft)]) {
    const distributionId = normalizeIdentifierToken(target.distributionId, "board-reporting-committee");
    const note = target.distributionId === draft.distributionId
      ? currentDeliveryNote
      : normalizeDraftText(target.deliveryNote, "");
    targets.set(distributionId, {
      distributionId,
      deliveryMode: normalizeReportingScheduleDeliveryMode(target.deliveryMode),
      formats: reportingScheduleArtifactFormats.filter((format) => target.formats[format]),
      note: note || null
    });
  }

  return Array.from(targets.values());
}

function buildCurrentScheduleDraftTarget(draft: ReportingScheduleDraftState): ReportingScheduleDraftTarget {
  return normalizeScheduleDraftTarget({
    distributionId: draft.distributionId,
    deliveryMode: draft.deliveryMode,
    note: draft.deliveryNote,
    formats: reportingScheduleArtifactFormats.filter((format) => draft.formats[format])
  });
}

function normalizeScheduleDraftTarget(target: {
  distributionId: string;
  deliveryMode?: ReportPackDeliveryMode | null;
  note?: string | null;
  formats?: readonly GovernanceReportArtifactFormat[] | null;
}): ReportingScheduleDraftTarget {
  return {
    distributionId: normalizeIdentifierToken(target.distributionId, "board-reporting-committee"),
    deliveryMode: normalizeReportingScheduleDeliveryMode(target.deliveryMode),
    deliveryNote: normalizeDraftText(target.note, ""),
    formats: buildScheduleFormatSelection(target.formats)
  };
}

function buildScheduleFormatSelection(
  formats: readonly GovernanceReportArtifactFormat[] | null | undefined
): ReportingScheduleDraftState["formats"] {
  const selected = formats
    ?.filter(isReportingScheduleArtifactFormat)
    ?? reportingScheduleArtifactFormats;

  return {
    Pdf: selected.includes("Pdf"),
    Xlsx: selected.includes("Xlsx"),
    Csv: selected.includes("Csv")
  };
}

function isReportingScheduleArtifactFormat(format: GovernanceReportArtifactFormat): format is ReportingScheduleArtifactFormat {
  return reportingScheduleArtifactFormats.includes(format as ReportingScheduleArtifactFormat);
}

function normalizeReportingScheduleDeliveryMode(value: string | null | undefined): ReportPackDeliveryMode {
  return reportingScheduleDeliveryModes.includes(value as ReportPackDeliveryMode)
    ? value as ReportPackDeliveryMode
    : "SecurePortal";
}

function parseScheduleMaxRetries(value: string): number {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? Math.max(0, parsed) : 1;
}

function formatReportingScheduleRunDetails(result: Awaited<ReturnType<typeof runReportingScheduleNow>>): string[] {
  return [
    `Run ID: ${result.run.runId}`,
    `Deliveries: ${result.deliveryAttempts?.length ?? 0}`,
    ...(result.deliveryWarnings ?? []).map((warning) => `Delivery warning: ${warning}`)
  ];
}

function formatReportPackPreviewDetails(result: Awaited<ReturnType<typeof previewReportPack>>): string[] {
  const assetClasses = result.assetClassSections
    .map((section) => `${section.assetClass}: ${formatReportingMoney(section.total, result.currency)}`)
    .join("; ");

  return [
    `Preview ID: ${result.reportId}`,
    `Fund: ${result.displayName}`,
    `Report kind: ${result.reportKind}`,
    `As of: ${result.asOf}`,
    `Total net assets: ${formatReportingMoney(result.totalNetAssets, result.currency)}`,
    `Trial balance lines: ${result.trialBalanceLineCount}`,
    `Asset-class sections: ${result.assetClassSectionCount}`,
    result.brandingTheme
      ? `Branding: ${result.brandingTheme.name} · ${result.brandingTheme.firmName} · ${result.brandingTheme.themeId}`
      : "Branding: default theme",
    assetClasses ? `Asset classes: ${assetClasses}` : "Asset classes: none"
  ];
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

function formatReportingMoney(value: number, currency: string): string {
  return formatCurrencyAmount(value, { currency, maximumFractionDigits: Math.abs(value) >= 1000 ? 0 : 2 });
}

function formatReportingDateRange(startDate: string, endDate: string): string {
  return startDate === endDate ? startDate : `${startDate} to ${endDate}`;
}

function formatReportingPercent(value: number): string {
  return formatPercentAmount(value);
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

async function executeRunAction(run: ReportingRunStatusRow, action: ReportingRunActionRow): Promise<void> {
  if (action.kind.startsWith("delivery:")) {
    const reportId = extractReportPackId(run, action);
    const distributionId = action.kind.slice("delivery:".length);
    await deliverReportPack(reportId, {
      distributionId,
      note: "Delivered from browser Reporting workspace.",
      formats: ["Pdf", "Xlsx", "Csv"],
      evidenceLinks: buildEvidenceLinksFromRun(run)
    });
    return;
  }

  if (action.kind === "approval-reject") {
    await apiPostJson<unknown>(action.href, {
      reason: "Returned from browser Reporting workspace.",
      evidenceLinks: buildEvidenceLinksFromRun(run)
    });
    return;
  }

  if (action.kind === "publication") {
    const reportId = extractReportPackId(run, action);
    await apiPostJson<unknown>(action.href, {
      signedOffBy: "server-authenticated-actor",
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

function buildReportPackDeliveryFailureRequest(attempt: ReportPackDeliveryAttempt): ReportPackDeliveryFailureRequest {
  return {
    distributionId: attempt.distributionId,
    deliveryReference: `delivery-failure:${normalizeEvidenceToken(attempt.attemptId)}`,
    note: `Delivery failure recorded from Reporting workspace for ${attempt.recipient}.`,
    failureReason: `Operator recorded delivery failure for ${attempt.recipient} after attempt ${attempt.attemptNumber}.`,
    evidenceLinks: [
      {
        evidenceId: normalizeEvidenceToken(attempt.attemptId),
        label: `${attempt.recipient} delivery attempt ${attempt.attemptNumber}`,
        route: attempt.package?.portalRoute ?? attempt.package?.secureLink ?? null,
        source: "report-pack-delivery",
        capturedAtUtc: attempt.attemptedAtUtc
      }
    ]
  };
}

function normalizeEvidenceToken(value: string): string {
  const normalized = value.toLowerCase().replace(/[^a-z0-9-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "reporting-evidence";
}
