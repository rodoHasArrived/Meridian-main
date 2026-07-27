import { type KeyboardEvent, useEffect, useMemo, useRef, useState } from "react";
import { FileText, Landmark, Network, PencilLine, RotateCcw, XCircle } from "lucide-react";
import { useLocation } from "react-router-dom";
import { formatCurrency as formatCurrencyAmount, formatPercent as formatPercentAmount } from "@/lib/format";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FreshnessChip } from "@/components/ui/freshness-chip";
import { useReportRunStream } from "@/hooks/use-report-run-stream";
import { humanizeStatus, SeverityBadge } from "@/components/operations";
import { FinancialRecordExplorerShell } from "@/components/meridian/financial-record-explorer";
import { OperationalTrustSummary } from "@/components/meridian/operational-trust-summary";
import { ReportingHub } from "@/components/meridian/reporting-hub";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { TechnicalDetails } from "@/components/ui/technical-details";
import {
  approveReportTemplateDraft,
  pauseReportingSchedule,
  provisionReportingStarterKit,
  rejectReportTemplateDraft,
  resumeReportingSchedule,
  runReportingScheduleNow,
  saveReportingSchedule,
  submitReportTemplateDraft
} from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { cn } from "@/lib/utils";
import { buildReportingHubModel, formatReportingFamilyLabel } from "@/lib/reporting-hub";
import {
  normalizeReportingWorkspace,
  type ReportingWorkspacePayload
} from "@/lib/reporting-workspace";
import { workstationRouteWithQuery } from "@/lib/workspace";
import {
  hasRetainedReportingAsOfDate,
  presentReportingAsOfDate,
  presentReportingIdentifier,
  presentReportingRunStatusLabel,
  presentReportingStatusLabel,
  resolveReportingRunSeverityStatus,
  resolveReportPackProfileKeyCommand,
  useReportingScreenViewModel,
  type ReportingProfileRow,
  type ReportingExportStatusState,
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
  buildClientPackageScheduleFormatSelection,
  clientPackageScheduleArtifactFormats,
  updateScheduleArtifactFormatDraft,
  updateScheduleRunParameterDraft
} from "@/screens/reporting-screen.client-package";
import {
  ReportingBrandingAccessPanel,
  buildDefaultReportBrandingDraft,
  buildReportBrandingOverride,
  type ReportBrandingDraftField,
  type ReportBrandingDraftState
} from "@/screens/reporting-screen.branding-access";
import type { ExportsReportRunDraftState } from "@/screens/reporting-screen.exports-runner";
import {
  buildDefaultReportRunParameterDraft,
  validateAndBuildReportingRunParameters,
  type ReportRunParameterDraftField
} from "@/screens/report-run-parameters-screen.view-model";
import { ReportingDeliveryHistoryPanel } from "@/screens/reporting-screen.delivery-history";
import {
  ReportWriterDesignerGrid,
  ReportingReportWriterSection,
  useReportingReportWriter,
  type ReportWriterChartDraft,
  type ReportWriterDraftSettings,
  type ReportWriterDropZone,
  type ReportWriterFormatRuleDraft,
} from "@/screens/reporting-screen.report-writer";
import {
  ReportingScheduleManagementPanel,
  reportingScheduleArtifactFormats,
  reportingScheduleDeliveryModes,
  type ReportingScheduleArtifactFormat,
  type ReportingScheduleDraftField,
  type ReportingScheduleDraftState,
  type ReportingScheduleDraftTarget,
  type ReportingScheduleRecipientPrincipalKind,
  type ReportingScheduleManagementModel
} from "@/screens/reporting-screen.schedule-management";
import { TemplateLifecycleActionIcon } from "@/screens/reporting-screen.template-lifecycle";
import { ReportingRunAuditDisclosure } from "@/screens/reporting-screen.run-status-modules";
import { ReportingStarterKitChooser } from "@/screens/reporting-screen.starter-kit";
import {
  ReportingBackendReference,
  ReportingCommandStatusView,
  ReportingCutMetric,
  ReportingHighlight,
  type ReportingCommandStatus
} from "@/screens/reporting-screen.shared-components";
import {
  ReportingChip,
  ReportingWorkbenchContext
} from "@/screens/reporting-screen.workbench-context";
import type {
  AccountingWorkspaceResponse,
  GovernanceReportArtifactFormat,
  ReportPackDeliveryMode,
  ReportTemplateDecisionRequest,
  ReportTemplateDraftRequest,
  ReportingRunParameters,
  ReportingRunRequest,
  RenderReportTemplateRequest,
  ReportingScheduleUpsertRequest
} from "@/types";
import {
  buildReportAccessPolicy,
  buildReportWriterGridDefinition,
  buildReportWriterPreviewRows,
  normalizeDraftText,
  normalizeIdentifierToken,
  parseReportTemplateVersion
} from "@/screens/reporting-screen.report-writer-helpers";

interface ReportingScreenProps {
  data: ReportingWorkspacePayload | null;
  accounting?: AccountingWorkspaceResponse | null;
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
// The report-run live chip renders in the "Live" state whenever the SSE channel is healthy,
// so this budget only governs its internal age tick; a modest interval keeps the dot lively.
export const REPORT_RUN_STREAM_FRESHNESS_BUDGET_MS = 15_000;

// Governed report-pack workflow rows carry a `report-pack:{id}` run id (server ProjectWorkflowRun)
// that the run-stream endpoint cannot resolve — it only knows IReportingOrchestrationService run
// ids and 404s on the workflow scheme. Only generic reporting runs are streamable, so selection
// skips workflow rows instead of opening an SSE channel that would just 404-loop.
function isStreamableReportingRun(run: ReportingRunStatusRow): boolean {
  return !run.id.startsWith("report-pack:");
}

function normalizeReportingStatus(status: string): string {
  return status.trim().toLowerCase().replace(/[^a-z0-9]/g, "");
}

const defaultExportsReportRunRequester = "browser-workstation";
const reportingProfileColumns: DenseDataTableColumn<ReportingProfileRow>[] = [
  {
    id: "profile",
    label: "Profile",
    render: (profile) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{profile.name}</span>
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

export function ReportingScreen({ data, accounting, onRefreshLivePortfolioViews }: ReportingScreenProps) {
  const { pathname, search } = useLocation();
  const reportingData = normalizeReportingWorkspace(data);
  const accountingData = accounting ?? null;
  const vm = useReportingScreenViewModel(reportingData, undefined, pathname);
  const hubModel = useMemo(
    () => buildReportingHubModel(vm.runStatusRows, vm.templateRows, reportingData?.dailyWork ?? []),
    [reportingData?.dailyWork, vm.runStatusRows, vm.templateRows]
  );
  // Watch the most recent run over the report-run SSE stream. This is additive — the 30s
  // reporting poll is unchanged and remains the source of truth for the rendered rows. When the
  // channel is healthy it surfaces that run's approval/status transitions instantly; while it is
  // unhealthy (or where EventSource is unavailable, e.g. in tests) nothing extra renders.
  const watchedRunId = vm.runStatusRows.find(isStreamableReportingRun)?.id ?? null;
  const { status: watchedRunStreamStatus, healthy: watchedRunStreamHealthy } = useReportRunStream(watchedRunId);
  // When the watched run's stream is healthy, its pushed status supersedes the stale polled status
  // for that row, so the prominent badge reflects the live approval state instead of contradicting
  // the live line beneath it until the next 30s poll catches up.
  const isWatchedRunLive = (run: ReportingRunStatusRow): boolean =>
    run.id === watchedRunId && watchedRunStreamHealthy && watchedRunStreamStatus !== null;
  const resolveRowStatus = (run: ReportingRunStatusRow): string =>
    isWatchedRunLive(run) && watchedRunStreamStatus ? watchedRunStreamStatus.status : run.status;
  const resolveRowStatusLabel = (run: ReportingRunStatusRow): string =>
    presentReportingRunStatusLabel(resolveRowStatus(run), run.asOfDateLabel);
  const resolveRowSeverityStatus = (run: ReportingRunStatusRow): string =>
    resolveReportingRunSeverityStatus(resolveRowStatus(run), run.asOfDateLabel);
  const reportPackProfileButtonRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const shouldFocusReportPackProfile = useRef(false);
  const [templateLifecycleStatus, setTemplateLifecycleStatus] = useState<ReportingCommandStatus | null>(null);
  const [scheduleActionStatus, setScheduleActionStatus] = useState<ReportingCommandStatus | null>(null);
  const [starterKitStatus, setStarterKitStatus] = useState<ReportingCommandStatus | null>(null);
  const [scheduleDraft, setScheduleDraft] = useState<ReportingScheduleDraftState>(() => buildDefaultReportingScheduleDraft(reportingData));
  const [livePortfolioRefreshStatus, setLivePortfolioRefreshStatus] = useState<ReportingCommandStatus | null>(null);
  const [brandingDraft, setBrandingDraft] = useState<ReportBrandingDraftState>(() => buildDefaultReportBrandingDraft(reportingData));
  const reportWriterDatasetSources = reportingData?.reportWriterDatasetSources ?? [];
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
  const livePortfolioViews = reportingData?.livePortfolioViews ?? [];
  const shouldAutoRefreshLivePortfolioViews = livePortfolioViews.some((view) => view.isMarketTickLinked || view.state === "LiveLinked");
  const runningTemplateLifecycleActionId = templateLifecycleStatus?.state === "running" ? templateLifecycleStatus.id : null;
  const runningScheduleActionId = scheduleActionStatus?.state === "running" ? scheduleActionStatus.id : null;
  const runningStarterKitId = starterKitStatus?.state === "running" ? starterKitStatus.id : null;
  const isRefreshingLivePortfolioViews = livePortfolioRefreshStatus?.state === "running";
  const scheduleDistributionOptions = reportingData?.reportPackDistributions ?? [];
  const isDailyReportingCockpitLanding = vm.taskMode.id === "daily-reporting-cockpit";
  const isReportBuilderTaskMode = vm.taskMode.id === "report-builder";
  const isSchedulesTaskMode = vm.taskMode.id === "schedules";
  const isRunStatusTaskMode = vm.taskMode.id === "run-status";
  const isDeliveryEvidenceTaskMode = vm.taskMode.id === "delivery-evidence" || vm.taskMode.id === "report-pack-approval";
  const isExportsTaskMode = vm.taskMode.id === "exports";
  const isGovernanceTaskMode = vm.taskMode.id === "governance";
  const reportBuilderSearchParams = useMemo(() => new URLSearchParams(search), [search]);
  const requestedReportBuilderTemplateId = reportBuilderSearchParams.get("templateId")?.trim() ?? "";
  const requestedReportBuilderFamily = (
    reportBuilderSearchParams.get("family")
    ?? reportBuilderSearchParams.get("report")
    ?? ""
  ).trim();
  const focusedReportBuilderTemplate = useMemo(
    () => requestedReportBuilderTemplateId
      ? vm.templateRows.find((template) => (
          template.id === requestedReportBuilderTemplateId
          || template.templateName === requestedReportBuilderTemplateId
        )) ?? null
      : null,
    [requestedReportBuilderTemplateId, vm.templateRows]
  );
  const reportBuilderFamilyTemplates = useMemo(() => {
    if (!requestedReportBuilderFamily) {
      return [];
    }
    const familyToken = normalizeReportBuilderContextToken(requestedReportBuilderFamily);
    return vm.templateRows.filter((template) => normalizeReportBuilderContextToken(template.family) === familyToken);
  }, [requestedReportBuilderFamily, vm.templateRows]);
  const reportBuilderTemplateRows = useMemo(() => {
    const prioritized = focusedReportBuilderTemplate
      ? [focusedReportBuilderTemplate]
      : reportBuilderFamilyTemplates;
    if (prioritized.length === 0) {
      return vm.templateRows;
    }
    const prioritizedIds = new Set(prioritized.map((template) => template.id));
    return [...prioritized, ...vm.templateRows.filter((template) => !prioritizedIds.has(template.id))];
  }, [focusedReportBuilderTemplate, reportBuilderFamilyTemplates, vm.templateRows]);
  const writerTemplateRows = focusedReportBuilderTemplate
    ? [focusedReportBuilderTemplate]
    : reportBuilderFamilyTemplates.length > 0
      ? reportBuilderFamilyTemplates
      : vm.templateRows;
  const writerGrids = writerTemplateRows.flatMap((template) => template.writerGrids);
  const governanceScopeUnavailable = isGovernanceTaskMode && !vm.accessAudit.isAvailable;
  const latestRetainedAsOfDate = vm.runStatusRows.find((run) => hasRetainedReportingAsOfDate(run.asOfDateLabel))?.asOfDateLabel ?? null;
  const reportPackWorkflowRecord = [...(reportingData?.workflowRecords ?? [])]
    .sort((left, right) => right.updatedAt.localeCompare(left.updatedAt))[0] ?? null;
  const reportPackPeriodToken = reportPackWorkflowRecord?.period.match(/^\d{4}-\d{2}/)?.[0] ?? null;
  const reportPackWorkflowRun = reportPackWorkflowRecord
    ? [...vm.runStatusRows]
        .filter((run) => (
          run.templateId === reportPackWorkflowRecord.templateId.name
          && (!reportPackPeriodToken || run.asOfDateLabel.startsWith(reportPackPeriodToken))
        ))
        .sort((left, right) => (
          Number(right.isLatestGenerated) - Number(left.isLatestGenerated)
          || right.asOfDateLabel.localeCompare(left.asOfDateLabel)
          || right.runAttemptOrdinal - left.runAttemptOrdinal
        ))[0] ?? null
    : null;
  const reportPackWorkflowStatusLabel = presentReportingStatusLabel(
    reportPackWorkflowRun
      ? resolveRowStatus(reportPackWorkflowRun)
      : reportPackWorkflowRecord?.state.trim() || vm.workflowTaskPanel?.statusLabel || "Report pack review"
  );
  const showStarterKitChooser =
    isDailyReportingCockpitLanding &&
    vm.starterKitPanel.showChooser &&
    starterKitStatus?.state !== "success";
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

  function handleReportPackProfileKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const command = resolveReportPackProfileKeyCommand(event.key);
    if (!command) {
      return;
    }

    event.preventDefault();
    shouldFocusReportPackProfile.current = true;
    vm.selectAdjacentReportPackProfile(command);
  }

  async function handleTemplateLifecycleAction(
    template: ReportingTemplateRow,
    action: ReportingTemplateLifecycleActionRow
  ) {
    if (governanceScopeUnavailable || !action.isEnabled || runningTemplateLifecycleActionId) {
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
    setScheduleDraft((current) => {
      const next = {
        ...current,
        [field]: field === "deliveryMode"
          ? normalizeReportingScheduleDeliveryMode(value)
          : field === "recipientPrincipalKind"
            ? normalizeReportingScheduleRecipientPrincipalKind(value)
            : value
      } as ReportingScheduleDraftState;
      if (field === "cronExpression" || field === "nextAsOfDate") {
        next.dueAtUtc = resolveReportingScheduleDueAtUtc(next.nextAsOfDate, next.cronExpression, current.dueAtUtc);
      }
      if (field === "nextAsOfDate") {
        next.runParameters = {
          ...next.runParameters,
          periodId: value.slice(0, 7)
        };
      }
      if (field === "templateId") {
        const selectedTemplate = vm.templateRows.find((template) => template.id === value && template.canRunOnDemand);
        if (selectedTemplate) {
          next.templateId = selectedTemplate.templateName;
          next.templateVersion = selectedTemplate.versionNumber;
        }
      }
      return next;
    });
  }

  function updateScheduleRunParameters(field: ReportRunParameterDraftField, value: string | boolean) {
    setScheduleDraft((current) => updateScheduleRunParameterDraft(current, field, value));
  }

  function toggleScheduleDraftFormat(format: ReportingScheduleArtifactFormat, isSelected: boolean) {
    setScheduleDraft((current) => updateScheduleArtifactFormatDraft(current, format, isSelected));
  }

  function stageScheduleDraftDeliveryTarget() {
    if (!scheduleDraft.recipientPrincipalId.trim() || !scheduleDraft.recipientPrincipalKind) {
      setScheduleActionStatus({
        id: "schedule-draft:target",
        label: "Stage reporting schedule recipient",
        state: "error",
        message: "Select a recipient kind and enter its explicit principal ID before staging this target.",
        details: ["Scheduled delivery audiences must retain an exact User, Group, or Company principal."]
      });
      return;
    }

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

    let request: ReportingScheduleUpsertRequest;
    try {
      request = buildReportingScheduleUpsertRequest(scheduleDraft, brandingDraft, vm.templateRows);
    } catch (error) {
      const display = describeApiError(error, "The reporting schedule parameters are incomplete.");
      setScheduleActionStatus({
        id: statusId,
        label: "Save reporting schedule",
        state: "error",
        message: display.summary,
        details: display.details
      });
      return;
    }
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
            ? `Delivery targets: ${savedTargets.map((target) => `${target.distributionId} to ${target.recipientPrincipalKind ?? "Unknown"}:${target.recipientPrincipalId ?? "missing principal"} via ${target.deliveryMode ?? "SecurePortal"}`).join("; ")}`
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

  async function handleProvisionStarterKit(kitId: string, title: string) {
    const statusId = `starter-kit:${kitId}`;
    if (runningStarterKitId) {
      return;
    }

    setStarterKitStatus({
      id: statusId,
      label: "Provision reporting starter kit",
      state: "running",
      message: `${title} reporting desk is provisioning.`,
      details: []
    });

    try {
      const result = await provisionReportingStarterKit(kitId);
      setStarterKitStatus({
        id: statusId,
        label: "Provision reporting starter kit",
        state: "success",
        message: `${result.kit.displayName} reporting desk provisioned.`,
        details: [
          `Templates enabled: ${result.state.enabledTemplateIds.join(", ")}`,
          `Hub layout: ${result.state.defaultLayoutId ?? result.kit.defaultLayoutId}`,
          `Default period: ${result.state.defaultPeriod ?? result.kit.defaultPeriod}`,
          `Draft schedules: ${result.seededSchedules.map((schedule) => `${schedule.scheduleId} (${schedule.state})`).join("; ")}`
        ]
      });
    } catch (error) {
      const display = describeApiError(error, `${title} starter kit provisioning failed.`);
      setStarterKitStatus({
        id: statusId,
        label: "Provision reporting starter kit",
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

  if (!reportingData) {
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
    <div className="space-y-5">
      <ReportingWorkbenchContext taskMode={vm.taskMode} actions={vm.workbenchActions} />
      <OperationalTrustSummary
        source={{ value: "Governed Reporting service", tone: "ready" }}
        scope={{ value: vm.taskMode.label, detail: vm.taskMode.description, tone: "ready" }}
        freshness={{
          value: latestRetainedAsOfDate ? presentReportingAsOfDate(latestRetainedAsOfDate) : "No as-of date retained",
          detail: latestRetainedAsOfDate
            ? "Latest retained reporting period"
            : vm.runStatusRows.length > 0
              ? "Loaded runs do not retain an as-of date; confirm the reporting period before release"
              : "Generate a report to establish freshness",
          tone: latestRetainedAsOfDate ? "ready" : "review"
        }}
        completeness={{
          value: `${vm.templateRows.length} templates · ${vm.runStatusRows.length} runs`,
          detail: "Caller-visible Reporting records",
          tone: vm.templateRows.length > 0 ? "ready" : "review"
        }}
        blocker={governanceScopeUnavailable ? {
          value: "Access scope unavailable",
          detail: "Template lifecycle decisions are disabled until caller scope can be verified.",
          tone: "blocked"
        } : undefined}
        label="Reporting data confidence"
      />

      {showStarterKitChooser ? (
        <ReportingStarterKitChooser
          panel={vm.starterKitPanel}
          status={starterKitStatus}
          runningStarterKitId={runningStarterKitId}
          onProvision={handleProvisionStarterKit}
        />
      ) : isDailyReportingCockpitLanding && starterKitStatus ? (
        <ReportingCommandStatusView status={starterKitStatus} />
      ) : null}

      {isDailyReportingCockpitLanding ? (
        <ReportingHub model={hubModel} />
      ) : null}

      {isDailyReportingCockpitLanding ? null : (
        <>
      {isGovernanceTaskMode ? (
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
                {vm.accessAudit.isAvailable ? <Badge variant="outline">{vm.accessAudit.hiddenTotalLabel}</Badge> : null}
              </span>
            </div>
            <CardDescription>{vm.accessAudit.summary}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {vm.accessAudit.isAvailable ? (
            <>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-medium text-muted-foreground">Matched principal scopes</div>
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
            </>
            ) : (
              <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-3 text-sm leading-6 text-warning">
                Access counts are unavailable. Refresh Reporting or ask an administrator to verify the caller-scoped access audit before relying on visibility totals.
              </p>
            )}
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
      ) : null}

      {isDeliveryEvidenceTaskMode && reportingData.reportLineProvenanceExplorer ? (
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
          explorer={reportingData.reportLineProvenanceExplorer}
        >
          {null}
        </FinancialRecordExplorerShell>
      ) : null}

      {isReportBuilderTaskMode && (reportingData.portfolioCuts ?? []).length > 0 ? (
        <section role="region" aria-label="Portfolio reporting cuts">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Portfolio cuts</div>
              <CardTitle>Exposure, cash, P&L, and shadow NAV</CardTitle>
              <CardDescription>Fund, strategy, and tag views are projected from shared portfolio and NAV reporting data.</CardDescription>
            </CardHeader>
            <CardContent>
              <div role="list" aria-label="Portfolio reporting cut rows" className="grid gap-3 lg:grid-cols-3">
                {(reportingData.portfolioCuts ?? []).slice(0, 6).map((cut) => (
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

      {isReportBuilderTaskMode && (reportingData.livePortfolioViews ?? []).length > 0 ? (
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
                {(reportingData.livePortfolioViews ?? []).slice(0, 6).map((view) => (
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

      {isReportBuilderTaskMode && (reportingData.pnlSlices ?? []).length > 0 ? (
        <section role="region" aria-label="P&L slicing">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">P&L slices</div>
              <CardTitle>Daily, weekly, monthly, and yearly P&L</CardTitle>
              <CardDescription>Period windows are calculated from retained portfolio run timestamps and marked blocked when source runs are absent.</CardDescription>
            </CardHeader>
            <CardContent>
              <div role="list" aria-label="P&L slice rows" className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                {(reportingData.pnlSlices ?? []).map((slice) => (
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

      {isReportBuilderTaskMode && (reportingData.analyticsRows ?? []).length > 0 ? (
        <section role="region" aria-label="Top-N and contribution analytics">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Top-N analytics</div>
              <CardTitle>Winners, laggards, and contribution breakdowns</CardTitle>
              <CardDescription>Security, strategy, and asset-class rows come from retained portfolio P&L sources.</CardDescription>
            </CardHeader>
            <CardContent>
              <div role="list" aria-label="Top-N and contribution analytics rows" className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                {(reportingData.analyticsRows ?? []).map((row) => (
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

      {isReportBuilderTaskMode && (reportingData.crossFundConsolidations ?? []).length > 0 ? (
        <section role="region" aria-label="Cross-fund consolidations">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Cross-fund</div>
              <CardTitle>Company, fund, and entity rollups</CardTitle>
              <CardDescription>Reporting aggregates source-backed exposure, cash, P&L, and shadow NAV across available funds and entities.</CardDescription>
            </CardHeader>
            <CardContent>
              <div role="list" aria-label="Cross-fund consolidation rows" className="grid gap-3 lg:grid-cols-3">
                {(reportingData.crossFundConsolidations ?? []).slice(0, 6).map((row) => (
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

      {isExportsTaskMode && (reportingData.structuredExports ?? []).length > 0 ? (
        <section role="region" aria-label="Structured reporting exports">
          <Card className="panel-surface">
            <CardHeader>
              <div className="eyebrow-label">Structured exports</div>
              <CardTitle>Regulatory, warehouse, and decision outputs</CardTitle>
              <CardDescription>Source-backed JSON descriptors keep downstream exports tied to governed Reporting evidence.</CardDescription>
            </CardHeader>
            <CardContent>
              <div role="list" aria-label="Structured export rows" className="grid gap-3 lg:grid-cols-3">
                {(reportingData.structuredExports ?? []).map((structuredExport) => (
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

      {isReportBuilderTaskMode && (requestedReportBuilderTemplateId || requestedReportBuilderFamily) ? (
        <section
          role="status"
          aria-label="Report builder route context"
          className="rounded-md border border-primary/30 bg-primary/5 px-4 py-3"
        >
          <div className="eyebrow-label">Builder context</div>
          <div className="mt-1 flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <h2 className="text-sm font-semibold text-foreground">
                {focusedReportBuilderTemplate
                  ? `Review ${focusedReportBuilderTemplate.name}`
                  : requestedReportBuilderTemplateId
                    ? `${presentReportingIdentifier(requestedReportBuilderTemplateId.split(":", 1)[0], "Requested template")} is unavailable`
                    : `Set up ${formatReportingFamilyLabel(requestedReportBuilderFamily)}`}
              </h2>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">
                {focusedReportBuilderTemplate
                  ? `Template v${focusedReportBuilderTemplate.version} and its report-writer controls are prioritized below.`
                  : requestedReportBuilderTemplateId
                    ? "The requested template is no longer available to this operator. Choose another governed template below."
                    : reportBuilderFamilyTemplates.length > 0
                      ? `${reportBuilderFamilyTemplates.length} existing ${formatReportingFamilyLabel(requestedReportBuilderFamily)} template version${reportBuilderFamilyTemplates.length === 1 ? " is" : "s are"} prioritized below.`
                      : "No governed template exists for this family yet. Use the available builder controls and templates below as the starting point."}
              </p>
            </div>
            <Badge variant={focusedReportBuilderTemplate || reportBuilderFamilyTemplates.length > 0 ? "success" : "warning"}>
              {focusedReportBuilderTemplate ? "Template selected" : reportBuilderFamilyTemplates.length > 0 ? "Family selected" : "Setup required"}
            </Badge>
          </div>
        </section>
      ) : null}

      {isReportBuilderTaskMode ? (
      <ReportingBrandingAccessPanel
        themes={reportingData.brandingThemes ?? []}
        draft={brandingDraft}
        onDraftChange={updateBrandingDraft}
      />
      ) : null}

      {isReportBuilderTaskMode && writerGrids.length > 0 ? (
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

      {isReportBuilderTaskMode || isRunStatusTaskMode || isGovernanceTaskMode ? (
      <section className="grid gap-4 xl:grid-cols-[0.9fr_1.1fr]">
        {isReportBuilderTaskMode || isGovernanceTaskMode ? (
        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">{isGovernanceTaskMode ? "Template governance" : "Template families"}</div>
            <CardTitle>{isGovernanceTaskMode ? "Template lifecycle and access" : "Governed report templates"}</CardTitle>
            <CardDescription>
              {isGovernanceTaskMode
                ? "Review template access, validation, approval lineage, and the next permitted lifecycle decision."
                : "Design reusable report templates, review versions, and open the governed authoring surface. Run operations remain in Report Parameters."}
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            {(isReportBuilderTaskMode ? reportBuilderTemplateRows : vm.templateRows).map((template) => (
              <div
                key={template.id}
                aria-current={focusedReportBuilderTemplate?.id === template.id ? "true" : undefined}
                className={cn(
                  "rounded-md border bg-secondary/20 px-3 py-2",
                  focusedReportBuilderTemplate?.id === template.id ? "border-primary/50 bg-primary/5" : "border-border/70"
                )}
              >
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-semibold text-foreground">{template.name}</span>
                  <span className="flex flex-wrap items-center gap-1.5">
                    <SeverityBadge status={reportingStatusFromVariant[template.statusVariant]} label={template.statusLabel} />
                    <Badge variant="outline">{template.sourceLabel}</Badge>
                    <Badge variant="outline">{presentReportingIdentifier(template.family, "Report")}</Badge>
                  </span>
                </div>
                <p className="mt-1 text-xs text-muted-foreground">{template.version} · {template.sectionSummary}</p>
                {isGovernanceTaskMode ? (
                <>
                <details className="mt-2 rounded-md border border-border/60 bg-background/25">
                  <summary className="cursor-pointer px-3 py-2 text-xs font-semibold text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40">
                    Version, validation, and audit details
                  </summary>
                <div
                  role="group"
                  aria-label={`${template.name} template audit and version lineage`}
                  className="grid gap-3 border-t border-border/60 px-3 py-3 text-xs md:grid-cols-2"
                >
                  <span className="min-w-0">
                    <span className="block font-medium text-muted-foreground">Version</span>
                    <span className="mt-1 block break-words text-foreground">{template.versionLineageSummary}</span>
                  </span>
                  <span className="min-w-0">
                    <span className="block font-medium text-muted-foreground">Audit</span>
                    <span className="mt-1 block break-words text-foreground">
                      {template.auditTrailSummary} · {template.lastAuditSummary}
                    </span>
                  </span>
                  <span className="min-w-0">
                    <span className="block font-medium text-muted-foreground">Approval</span>
                    <span className="mt-1 block break-words text-foreground">
                      {template.latestApprovedLabel} · {template.decisionSummary}
                    </span>
                  </span>
                  <span className="min-w-0">
                    <span className="block font-medium text-muted-foreground">Validation</span>
                    <span className="mt-1 block break-words text-foreground">{template.validationSummary}</span>
                  </span>
                </div>
                </details>
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
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{template.accessGovernance.detail}</p>
                </div>
                </>
                ) : null}
                <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
                  <p className="min-w-0 flex-1 text-xs leading-5 text-muted-foreground">
                    <span className="block">{template.approvalSummary}</span>
                    <span className="block">{template.accessSummary}</span>
                  </p>
                  <span className="flex flex-wrap items-center gap-2">
                    {isReportBuilderTaskMode ? (
                    <Button asChild variant="outline" size="sm">
                      <a href={template.authoringHref} target="_blank" rel="noreferrer" aria-label={template.actionAriaLabel}>
                        <PencilLine className="h-4 w-4" aria-hidden="true" />
                        {template.actionLabel}
                      </a>
                    </Button>
                    ) : null}
                    {isGovernanceTaskMode ? template.lifecycleActions.map((action) => (
                      <Button
                        key={action.id}
                        variant={action.kind === "reject" ? "ghost" : "outline"}
                        size="sm"
                        aria-label={action.ariaLabel}
                        disabled={governanceScopeUnavailable || !action.isEnabled || Boolean(runningTemplateLifecycleActionId)}
                        disabledReason={governanceScopeUnavailable
                          ? "Access scope is unavailable. Refresh Reporting or ask an administrator before approving or rejecting a template."
                          : action.disabledReason}
                        busy={runningTemplateLifecycleActionId === action.id}
                        busyLabel={buildTemplateLifecycleBusyLabel(action.kind)}
                        onClick={() => void handleTemplateLifecycleAction(template, action)}
                      >
                        <TemplateLifecycleActionIcon action={action.kind} />
                        {action.label}
                      </Button>
                    )) : null}
                  </span>
                </div>
                {isGovernanceTaskMode && governanceScopeUnavailable && template.lifecycleActions.length > 0 ? (
                  <p role="alert" className="mt-2 rounded-sm border border-danger/30 bg-danger/10 px-2.5 py-2 text-xs leading-5 text-danger">
                    Approval and rejection are disabled because caller access scope could not be verified. Refresh Reporting or ask an administrator to restore the access audit.
                  </p>
                ) : null}
              </div>
            ))}
            {isGovernanceTaskMode && templateLifecycleStatus ? (
              <ReportingCommandStatusView status={templateLifecycleStatus} />
            ) : null}
          </CardContent>
        </Card>
        ) : null}

        {isRunStatusTaskMode ? (
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
                  <span className="min-w-0">
                    <span className="block text-sm font-semibold text-foreground">{run.templateLabel}</span>
                    <span className="mt-0.5 block text-xs text-muted-foreground">
                      {presentReportingIdentifier(run.family, "Report")} · {hasRetainedReportingAsOfDate(run.asOfDateLabel)
                        ? `As of ${presentReportingAsOfDate(run.asOfDateLabel)}`
                        : presentReportingAsOfDate(run.asOfDateLabel)}
                    </span>
                  </span>
                  <div className="flex items-center gap-2">
                    {isWatchedRunLive(run) ? (
                      <FreshnessChip
                        live
                        label={`Run ${run.id} status`}
                        timestamp={null}
                        staleBudgetMs={REPORT_RUN_STREAM_FRESHNESS_BUDGET_MS}
                      />
                    ) : null}
                    <SeverityBadge status={resolveRowSeverityStatus(run)} label={resolveRowStatusLabel(run)} />
                  </div>
                </div>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  {humanizeStatus(run.trigger)} run · {run.attemptLabel} · {run.comparisonSummary}
                </p>
                {resolveRowStatusLabel(run) === "Period confirmation required" ? (
                  <p role="status" className="mt-2 rounded-sm border border-warning/30 bg-warning/10 px-2.5 py-2 text-xs leading-5 text-warning">
                    The workflow state is retained, but this run has no as-of date. Confirm the report period before treating the output as approved or published.
                  </p>
                ) : null}
                {isWatchedRunLive(run) && watchedRunStreamStatus ? (
                  <p className="mt-1 text-xs leading-5 text-muted-foreground" data-testid="report-run-live-status">
                    Live · attempt {watchedRunStreamStatus.attemptCount} · streamed ahead of the 30s poll
                  </p>
                ) : null}
                <ReportingRunAuditDisclosure run={run} />
                {run.failureReason ? <p className="mt-1 text-xs text-warning">{run.failureReason}</p> : null}
                {normalizeReportingStatus(resolveRowStatus(run)) === "awaitingapproval" ? (
                  <Button asChild size="sm" variant="outline" className="mt-2">
                    <a href={workstationRouteWithQuery("reportingRunDetail", { runId: run.id })}>
                      Review approval details
                    </a>
                  </Button>
                ) : null}
                {run.hasNextActions ? (
                  <div className="mt-2 flex flex-wrap items-center gap-2 rounded-md border border-primary/25 bg-primary/10 px-3 py-2" aria-label={`${run.id} governed workflow continuation`}>
                    <p className="min-w-0 flex-1 text-xs leading-5 text-primary">
                      Legacy pack mutations are retired. Continue validation, approval, release, restatement, and distribution from the governed run.
                    </p>
                    <Button asChild size="sm" variant="outline">
                      <a href={workstationRouteWithQuery("reportingRunDetail", { runId: run.id })}>
                        Open governed run
                      </a>
                    </Button>
                  </div>
                ) : null}
              </div>
            )) : (
              <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                No report runs have been generated yet.
              </p>
            )}
          </CardContent>
        </Card>
        ) : null}
      </section>
      ) : null}

      {isReportBuilderTaskMode ? (
        <ReportingPrivateCapitalReadinessPanel data={accountingData} />
      ) : null}

      {isSchedulesTaskMode || isDeliveryEvidenceTaskMode ? (
      <section className="grid items-start gap-4 xl:grid-cols-[0.95fr_1.05fr]">
        {isSchedulesTaskMode ? (
        <ReportingScheduleManagementPanel
          model={scheduleModel}
          scheduleDraft={scheduleDraft}
          distributionOptions={scheduleDistributionOptions}
          datasetSources={reportWriterDatasetSources}
          templates={vm.templateRows}
          status={scheduleActionStatus}
          runningScheduleActionId={runningScheduleActionId}
          onDraftChange={updateScheduleDraft}
          onRunParameterChange={updateScheduleRunParameters}
          onToggleFormat={toggleScheduleDraftFormat}
          onStageTarget={stageScheduleDraftDeliveryTarget}
          onRemoveTarget={removeScheduleDraftDeliveryTarget}
          onSaveDraft={saveScheduleDraft}
          onScheduleAction={handleScheduleAction}
          onSchedulePlanRun={handleSchedulePlanRun}
        />
        ) : null}

        {isSchedulesTaskMode || isDeliveryEvidenceTaskMode ? (
        <ReportingDeliveryHistoryPanel
          deliveryAttempts={reportingData.deliveryAttempts ?? []}
        />
        ) : null}
      </section>
      ) : null}

      {isDeliveryEvidenceTaskMode && vm.workflowTaskPanel ? (
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
              <span className="flex flex-wrap items-center gap-2">
                <SeverityBadge
                  status={reportPackWorkflowRecord || reportPackWorkflowRun
                    ? reportPackWorkflowStatusLabel
                    : reportingStatusFromVariant[vm.workflowTaskPanel.statusVariant]}
                  label={reportPackWorkflowStatusLabel}
                />
                {reportPackWorkflowRun ? (
                  <Button asChild size="sm">
                    <a href={workstationRouteWithQuery("reportingRunDetail", { runId: reportPackWorkflowRun.id })}>
                      Open governed run
                    </a>
                  </Button>
                ) : null}
              </span>
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
              <details className="mt-3 rounded-md border border-border/60 bg-background/25">
                <summary className="flex cursor-pointer items-center justify-between gap-3 px-3 py-2 text-xs font-semibold text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40">
                  Publication evidence, provenance, and retained identifiers
                  <Badge variant="outline">{vm.workflowTaskPanel.publicationReview.evidenceSummary}</Badge>
                </summary>
              <div className="space-y-3 border-t border-border/60 px-3 py-3">
              <div className="grid gap-2 sm:grid-cols-2">
                {vm.workflowTaskPanel.publicationReview.fields.map((field) => (
                  <div key={field.label} className="rounded-md border border-border/70 bg-background/40 px-3 py-2">
                    <span className="block text-xs font-medium text-muted-foreground">{field.label}</span>
                    <span className={cn("mt-1 block break-all font-mono text-xs", field.className)}>{field.value}</span>
                  </div>
                ))}
              </div>
              <div className="flex flex-wrap items-center justify-between gap-2">
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
              </details>
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
              <details className="mt-3 rounded-md border border-border/60 bg-background/25">
                <summary className="flex cursor-pointer items-center justify-between gap-3 px-3 py-2 text-xs font-semibold text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40">
                  Changed lines and restatement evidence
                  <Badge variant="outline">{vm.workflowTaskPanel.restatementReview.evidenceSummary}</Badge>
                </summary>
              <div className="space-y-3 border-t border-border/60 px-3 py-3">
              <div className="grid gap-2 sm:grid-cols-2">
                {vm.workflowTaskPanel.restatementReview.fields.map((field) => (
                  <div key={field.label} className="rounded-md border border-border/70 bg-background/40 px-3 py-2">
                    <span className="block text-xs font-medium text-muted-foreground">{field.label}</span>
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
              </details>
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
              <TechnicalDetails
                label="System service references"
                description="Endpoint paths and service actions are retained for diagnostics and audit support."
              >
                <div
                  id={vm.workflowTaskPanel.backendPanelId}
                  aria-label={vm.workflowTaskPanel.backendLinksLabel}
                  className="grid gap-2"
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
              </TechnicalDetails>
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

      {isDeliveryEvidenceTaskMode && vm.exportStatus ? (
        <ReportingExportStatusPanel status={vm.exportStatus} />
      ) : null}

      {isDeliveryEvidenceTaskMode ? (
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
                aria-label="Report-pack distribution route recipients"
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
                      className="mt-2 inline-flex min-h-9 items-center text-xs font-medium text-primary underline-offset-2 hover:underline"
                      href={target.href}
                      aria-label={`Open ${target.label} report-pack distribution route`}
                    >
                      Open recipient workflow
                    </a>
                    <TechnicalDetails label="Recipient route details" className="mt-2">
                      <p className="break-all font-mono text-xs text-muted-foreground">{target.href}</p>
                    </TechnicalDetails>
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
      ) : null}

      {isExportsTaskMode ? (
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
              <p className="mt-1 text-xs text-muted-foreground">
                {vm.selectedProfile ? vm.selectedProfile.subtitle : vm.nextAction}
              </p>
            </div>
            <p className="mt-3 text-sm leading-6 text-muted-foreground">{vm.statusDetail}</p>
            {vm.exportStatus ? (
              <ReportingExportStatusPanel status={vm.exportStatus} className="mt-3" />
            ) : null}
            <p className="mt-3 text-xs text-muted-foreground">{vm.nextAction}</p>
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
      ) : null}
        </>
      )}
    </div>
  );
}

function ReportingExportStatusPanel({
  status,
  className
}: {
  status: ReportingExportStatusState;
  className?: string;
}) {
  return (
    <div
      role="status"
      aria-label={status.ariaLabel}
      className={cn("space-y-3 rounded-md border px-3 py-2 text-sm leading-6", status.className, className)}
    >
      <p>{status.text}</p>
      {status.fields.length > 0 ? (
        <dl className="grid gap-2 sm:grid-cols-2">
          {status.fields.map((field) => (
            <div
              key={field.label}
              className="rounded-sm border border-border/60 bg-background/25 px-2.5 py-2"
            >
              <dt className="text-xs font-medium text-muted-foreground">
                {field.label}
              </dt>
              <dd className={cn("mt-1 break-words font-mono text-xs", field.className)}>
                {field.value}
              </dd>
            </div>
          ))}
        </dl>
      ) : null}
      {status.warnings.length > 0 ? (
        <ul className="space-y-1 rounded-sm border border-warning/30 bg-warning/10 px-2.5 py-2 text-xs text-warning">
          {status.warnings.map((warning) => (
            <li key={warning}>{warning}</li>
          ))}
        </ul>
      ) : null}
      {status.artifacts.length > 0 ? (
        <dl
          aria-label="Export artifacts"
          className="space-y-1 rounded-sm border border-border/60 bg-background/25 px-2.5 py-2"
        >
          {status.artifacts.map((artifact) => (
            <div key={`${artifact.label}-${artifact.value}`} className="grid gap-1">
              <dt className="text-xs font-medium text-muted-foreground">
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

export function buildExportsReportRunRequest(
  template: ReportingTemplateRow | null,
  draft: ExportsReportRunDraftState,
  parameters?: ReportingRunParameters | null
): ReportingRunRequest {
  // Authorized restatement targets a specific released run's series: reuse its job id and as-of
  // date so the regenerated run versions into the same series (-v2) and trips the governed guard.
  // It carries its own template identity, so it does not depend on the current template selection.
  if (draft.restatementTargetRunId) {
    return {
      templateId: draft.restatementTemplateId,
      jobId: draft.restatementJobId,
      asOfDate: draft.restatementAsOfDate,
      maxRetries: parseExportsReportMaxRetries(draft.maxRetries),
      requestedBy: normalizeDraftText(draft.requestedBy, defaultExportsReportRunRequester),
      // Reuse the released run's dataset source so the restatement renders and diffs against the
      // same data, not the default retained dataset.
      datasetSourceId: normalizeOptionalDatasetSourceId(draft.restatementDatasetSourceId),
      retryReason: draft.retryReason.trim() || null,
      allowRestatement: true
    };
  }

  if (!template) {
    throw new Error("A report template must be selected to run a report.");
  }

  return {
    templateId: template.templateName,
    template: {
      name: template.templateName,
      version: template.versionNumber
    },
    asOfDate: normalizeDraftText(draft.asOfDate, new Date().toISOString().slice(0, 10)),
    maxRetries: parseExportsReportMaxRetries(draft.maxRetries),
    requestedBy: normalizeDraftText(draft.requestedBy, defaultExportsReportRunRequester),
    datasetSourceId: template.hasWriterGrids ? normalizeOptionalDatasetSourceId(draft.datasetSourceId) : null,
    parameters: parameters ?? null
  };
}

function parseExportsReportMaxRetries(value: string): number {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
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
  const details = [
    "Run retained for audit review",
    `Status: ${presentReportingStatusLabel(run.status)}`,
    `Trigger: ${presentReportingStatusLabel(run.trigger)}`
  ];
  if (run.asOfDate) {
    details.push(`As of: ${presentReportingAsOfDate(run.asOfDate)}`);
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
  const retainedTemplate = schedule?.template
    ?? (template
      ? { name: template.templateId, version: parseReportTemplateVersion(template.version) ?? 1 }
      : { name: templateId, version: 1 });
  const runParameters = buildDefaultReportRunParameterDraft({
    fundProfileId: reporting?.selectedFundProfileId ?? reporting?.fundProfileId,
    asOfDate: nextAsOfDate,
    parameters: schedule?.runParameters
  });
  const isClientPackage = runParameters.outputFormat === "ClientPackage";

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
    recipientPrincipalId: normalizeDraftText(firstTarget?.recipientPrincipalId, ""),
    recipientPrincipalKind: normalizeReportingScheduleRecipientPrincipalKind(firstTarget?.recipientPrincipalKind),
    deliveryNote: normalizeDraftText(firstTarget?.note ?? distribution?.pendingSummary, ""),
    formats: isClientPackage
      ? buildClientPackageScheduleFormatSelection()
      : buildScheduleFormatSelection(firstTarget?.formats),
    deliveryTargets: (schedule?.deliveryTargets ?? [])
      .map((target) => normalizeScheduleDraftTarget(target, isClientPackage)),
    templateVersion: retainedTemplate.version,
    runParameters
  };
}

function buildDefaultReportWriterDatasetSourceId(reporting: AccountingWorkspaceResponse["reporting"] | null): string {
  return reporting?.reportWriterDatasetSources?.[0]?.sourceId ?? "";
}

function normalizeOptionalDatasetSourceId(sourceId: string | null | undefined): string | null {
  const normalized = sourceId?.trim();
  return normalized ? normalized : null;
}

function normalizeReportBuilderContextToken(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, "");
}

export function resolveReportingScheduleDueAtUtc(
  nextAsOfDate: string,
  cronExpression: string,
  currentDueAtUtc: string
): string {
  const dateMatch = /^(\d{4})-(\d{2})-(\d{2})$/.exec(nextAsOfDate.trim());
  const cronFields = cronExpression.trim().split(/\s+/);
  const minute = Number.parseInt(cronFields[0] ?? "", 10);
  const hour = Number.parseInt(cronFields[1] ?? "", 10);
  if (!dateMatch || !Number.isInteger(minute) || minute < 0 || minute > 59 || !Number.isInteger(hour) || hour < 0 || hour > 23) {
    return currentDueAtUtc;
  }

  const year = Number.parseInt(dateMatch[1], 10);
  const monthIndex = Number.parseInt(dateMatch[2], 10) - 1;
  const day = Number.parseInt(dateMatch[3], 10);
  let dueDate = new Date(Date.UTC(year, monthIndex, day));
  if (
    dueDate.getUTCFullYear() !== year
    || dueDate.getUTCMonth() !== monthIndex
    || dueDate.getUTCDate() !== day
  ) {
    return currentDueAtUtc;
  }

  const normalizedCron = cronFields.join(" ");
  if (normalizedCron === "0 8 * * 1-5") {
    while (dueDate.getUTCDay() === 0 || dueDate.getUTCDay() === 6) {
      dueDate.setUTCDate(dueDate.getUTCDate() + 1);
    }
  } else if (normalizedCron === "0 8 * * 1") {
    while (dueDate.getUTCDay() !== 1) {
      dueDate.setUTCDate(dueDate.getUTCDate() + 1);
    }
  } else if (normalizedCron === "0 8 1 * *") {
    if (dueDate.getUTCDate() !== 1) {
      dueDate = new Date(Date.UTC(dueDate.getUTCFullYear(), dueDate.getUTCMonth() + 1, 1));
    }
  } else if (normalizedCron === "0 8 1 1,4,7,10 *") {
    const quarterlyMonths = new Set([0, 3, 6, 9]);
    let candidate = new Date(Date.UTC(dueDate.getUTCFullYear(), dueDate.getUTCMonth(), 1));
    if (candidate < dueDate || !quarterlyMonths.has(candidate.getUTCMonth())) {
      do {
        candidate = new Date(Date.UTC(candidate.getUTCFullYear(), candidate.getUTCMonth() + 1, 1));
      } while (!quarterlyMonths.has(candidate.getUTCMonth()));
    }
    dueDate = candidate;
  }

  const dueYear = dueDate.getUTCFullYear().toString().padStart(4, "0");
  const dueMonth = (dueDate.getUTCMonth() + 1).toString().padStart(2, "0");
  const dueDay = dueDate.getUTCDate().toString().padStart(2, "0");
  return `${dueYear}-${dueMonth}-${dueDay}T${hour.toString().padStart(2, "0")}:${minute.toString().padStart(2, "0")}:00Z`;
}

function buildReportingScheduleUpsertRequest(
  draft: ReportingScheduleDraftState,
  brandingDraft: ReportBrandingDraftState,
  templates: ReportingTemplateRow[]
): ReportingScheduleUpsertRequest {
  const scheduleId = normalizeIdentifierToken(draft.scheduleId, "sched-reporting-pack");
  const templateId = normalizeIdentifierToken(draft.templateId, "investor-monthly-statement");
  const nextAsOfDate = normalizeDraftText(draft.nextAsOfDate, new Date().toISOString().slice(0, 10));
  const deliveryNote = normalizeDraftText(draft.deliveryNote, "");
  const brandingThemeOverride = buildReportBrandingOverride(brandingDraft);
  const parameterValidation = validateAndBuildReportingRunParameters(draft.runParameters, nextAsOfDate);
  if (!parameterValidation.parameters) {
    throw new Error(parameterValidation.issues.join(" "));
  }
  const exactTemplate = templates.find((template) =>
    template.templateName === templateId && template.versionNumber === draft.templateVersion)
    ?? templates
      .filter((template) => template.templateName === templateId && template.canRunOnDemand)
      .reduce<ReportingTemplateRow | null>(
        (latest, template) => !latest || template.versionNumber > latest.versionNumber ? template : latest,
        null
      );
  if (!exactTemplate) {
    throw new Error("Select an approved reporting template version before saving the schedule.");
  }

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
    brandingThemeOverride,
    template: {
      name: exactTemplate.templateName,
      version: exactTemplate.versionNumber
    },
    runParameters: parameterValidation.parameters
  };
}

function buildReportingScheduleDeliveryTargets(
  draft: ReportingScheduleDraftState,
  currentDeliveryNote: string
): ReportingScheduleUpsertRequest["deliveryTargets"] {
  const targets = new Map<string, NonNullable<ReportingScheduleUpsertRequest["deliveryTargets"]>[number]>();
  for (const target of [...draft.deliveryTargets, buildCurrentScheduleDraftTarget(draft)]) {
    const distributionId = normalizeIdentifierToken(target.distributionId, "board-reporting-committee");
    const recipientPrincipalId = target.recipientPrincipalId.trim();
    const recipientPrincipalKind = normalizeReportingScheduleRecipientPrincipalKind(target.recipientPrincipalKind);
    if (!recipientPrincipalId || !recipientPrincipalKind) {
      throw new Error("Every scheduled delivery target requires an explicit User, Group, or Company recipient principal and ID.");
    }
    const note = target.distributionId === draft.distributionId
      ? currentDeliveryNote
      : normalizeDraftText(target.deliveryNote, "");
    targets.set(distributionId, {
      distributionId,
      recipientPrincipalId,
      recipientPrincipalKind,
      deliveryMode: normalizeReportingScheduleDeliveryMode(target.deliveryMode),
      formats: draft.runParameters.outputFormat === "ClientPackage"
        ? [...clientPackageScheduleArtifactFormats]
        : reportingScheduleArtifactFormats.filter((format) => target.formats[format]),
      note: note || null
    });
  }

  return Array.from(targets.values());
}

function buildCurrentScheduleDraftTarget(draft: ReportingScheduleDraftState): ReportingScheduleDraftTarget {
  return normalizeScheduleDraftTarget({
    distributionId: draft.distributionId,
    deliveryMode: draft.deliveryMode,
    recipientPrincipalId: draft.recipientPrincipalId,
    recipientPrincipalKind: draft.recipientPrincipalKind,
    note: draft.deliveryNote,
    formats: reportingScheduleArtifactFormats.filter((format) => draft.formats[format])
  }, draft.runParameters.outputFormat === "ClientPackage");
}

function normalizeScheduleDraftTarget(target: {
  distributionId: string;
  deliveryMode?: ReportPackDeliveryMode | null;
  recipientPrincipalId?: string | null;
  recipientPrincipalKind?: string | null;
  note?: string | null;
  formats?: readonly GovernanceReportArtifactFormat[] | null;
}, isClientPackage = false): ReportingScheduleDraftTarget {
  return {
    distributionId: normalizeIdentifierToken(target.distributionId, "board-reporting-committee"),
    deliveryMode: normalizeReportingScheduleDeliveryMode(target.deliveryMode),
    recipientPrincipalId: normalizeDraftText(target.recipientPrincipalId, ""),
    recipientPrincipalKind: normalizeReportingScheduleRecipientPrincipalKind(target.recipientPrincipalKind),
    deliveryNote: normalizeDraftText(target.note, ""),
    formats: isClientPackage
      ? buildClientPackageScheduleFormatSelection()
      : buildScheduleFormatSelection(target.formats)
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

function normalizeReportingScheduleRecipientPrincipalKind(
  value: string | null | undefined
): ReportingScheduleRecipientPrincipalKind | "" {
  return value === "User" || value === "Group" || value === "Company" ? value : "";
}

function parseScheduleMaxRetries(value: string): number {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? Math.max(0, parsed) : 1;
}

function formatReportingScheduleRunDetails(result: Awaited<ReturnType<typeof runReportingScheduleNow>>): string[] {
  return [
    "Run retained for audit review",
    `Deliveries: ${result.deliveryAttempts?.length ?? 0}`,
    ...(result.deliveryWarnings ?? []).map((warning) => `Delivery warning: ${warning}`)
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
