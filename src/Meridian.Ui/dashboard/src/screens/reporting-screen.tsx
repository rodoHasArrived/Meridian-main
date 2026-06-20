import { type DragEvent, type KeyboardEvent, useEffect, useRef, useState } from "react";
import { CheckCircle2, ChevronLeft, ChevronRight, Eye, FileText, Filter, GripVertical, Landmark, Network, PencilLine, Plus, RotateCcw, Send, Trash2, XCircle } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { FinancialRecordExplorerShell } from "@/components/meridian/financial-record-explorer";
import { MetricCard } from "@/components/meridian/metric-card";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import {
  apiPostJson,
  approveReportTemplateDraft,
  createReportTemplateDraft,
  deliverReportPack,
  generateReportPack,
  pauseReportingSchedule,
  previewReportPack,
  recordReportPackDeliveryFailure,
  rejectReportTemplateDraft,
  renderReportTemplate,
  resumeReportingSchedule,
  runDueReportingSchedules,
  runReportingNow,
  runReportingScheduleNow,
  saveReportingSchedule,
  submitReportTemplateDraft
} from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { cn } from "@/lib/utils";
import { evidenceWorkbenchPath, WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { FUND_STRUCTURE_API_ENDPOINTS, WORKSTATION_API_ENDPOINTS, reportingRunReportWriterGridEndpoint } from "@/lib/workstation-endpoints";
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
import type {
  AccountingWorkspaceResponse,
  GovernanceReportArtifactFormat,
  ManualJournalEntryStatus,
  PrivateCapitalActivityProjection,
  PrivateCapitalCapitalAccountSubledger,
  PrivateCapitalEvidenceCategory,
  PrivateCapitalFundEventLedgerRecord,
  PrivateCapitalLedgerImpact,
  PrivateCapitalReportOutput,
  ReportAccessMode,
  ReportAccessPrincipalKind,
  ReportBrandingTheme,
  ReportPackDeliveryAttempt,
  ReportPackDeliveryFailureRequest,
  ReportPackDeliveryPackage,
  ReportPackDeliveryMode,
  ReportWriterDatasetSource,
  ReportTemplateDecisionRequest,
  ReportTemplateDraftRequest,
  ReportWriterAggregateFunction,
  ReportWriterFilterDefinition,
  ReportWriterFilterOperator,
  ReportWriterGridDefinition,
  ReportWriterGridKind,
  ReportWriterGridRender,
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

interface ReportingCommandStatus {
  id: string;
  label: string;
  state: "running" | "success" | "error";
  message: string;
  details: string[];
}

type ReportWriterDropZone = "rowFields" | "columnFields" | "metrics" | "formulas";
type ReportWriterDraftState = Partial<Record<ReportWriterDropZone, ReportingWriterToken[]>>;
type ReportWriterTokenDragOrigin = { gridId: string; zone: ReportWriterDropZone };
type ReportWriterTokenDragPayload = { token: ReportingWriterToken; origin?: ReportWriterTokenDragOrigin | null };
type ReportingScheduleArtifactFormat = Extract<GovernanceReportArtifactFormat, "Pdf" | "Xlsx" | "Csv">;
type ReportWriterFixtureDatasetProfile = "portfolioPositions" | "ledgerFacts" | "cashLadder" | "customRows";
type ReportWriterPreviewDatasetProfile = ReportWriterFixtureDatasetProfile | `source:${string}`;
type ReportWriterDraftSettingsField =
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
type ReportWriterCustomFormulaField = "name" | "label" | "expression";
type ReportBrandingDraftField =
  | "themeId"
  | "name"
  | "firmName"
  | "primaryColor"
  | "accentColor"
  | "textColor"
  | "backgroundColor"
  | "logoUri"
  | "footerText"
  | "disclaimer";
type ReportingScheduleDraftField =
  | "scheduleId"
  | "templateId"
  | "cronExpression"
  | "nextAsOfDate"
  | "dueAtUtc"
  | "maxRetries"
  | "requestedBy"
  | "description"
  | "datasetSourceId"
  | "distributionId"
  | "deliveryMode"
  | "deliveryNote";
type ExportsReportRunDraftField = "templateRowId" | "asOfDate" | "maxRetries" | "requestedBy" | "datasetSourceId";

interface ReportWriterDraftSettings {
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

interface ReportWriterCustomFormulaDraft {
  name: string;
  label: string;
  expression: string;
}

interface ReportBrandingDraftState {
  themeId: string;
  name: string;
  firmName: string;
  primaryColor: string;
  accentColor: string;
  textColor: string;
  backgroundColor: string;
  logoUri: string;
  footerText: string;
  disclaimer: string;
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
  datasetSourceId: string;
  distributionId: string;
  deliveryMode: ReportPackDeliveryMode;
  deliveryNote: string;
  formats: Record<ReportingScheduleArtifactFormat, boolean>;
  deliveryTargets: ReportingScheduleDraftTarget[];
}

interface ExportsReportRunDraftState {
  templateRowId: string;
  asOfDate: string;
  maxRetries: string;
  requestedBy: string;
  datasetSourceId: string;
}

interface ReportingScheduleDraftTarget {
  distributionId: string;
  deliveryMode: ReportPackDeliveryMode;
  deliveryNote: string;
  formats: Record<ReportingScheduleArtifactFormat, boolean>;
}

const reportingScheduleArtifactFormats: ReportingScheduleArtifactFormat[] = ["Pdf", "Xlsx", "Csv"];
const reportingScheduleDeliveryModes: ReportPackDeliveryMode[] = ["SecurePortal", "EmailLink", "EvidenceVault", "InternalRoute"];
const structuredExportDownloadFormats = [
  { format: "json", label: "JSON" },
  { format: "csv", label: "CSV" },
  { format: "xls", label: "XLS" },
  { format: "xlsx", label: "XLSX" }
] as const;
const livePortfolioAutoRefreshIntervalMs = 60_000;
const defaultExportsReportRunRequester = "browser-workstation";
const reportWriterPreviewDatasetProfiles: { id: ReportWriterPreviewDatasetProfile; label: string }[] = [
  { id: "portfolioPositions", label: "Portfolio positions" },
  { id: "ledgerFacts", label: "Ledger facts" },
  { id: "cashLadder", label: "Cash ladder" },
  { id: "customRows", label: "Custom pasted rows" }
];

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
  const { pathname } = useLocation();
  const vm = useReportingScreenViewModel(data?.reporting ?? null, undefined, pathname);
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
  const [writerDrafts, setWriterDrafts] = useState<Record<string, ReportWriterDraftState>>({});
  const [writerDraftSettings, setWriterDraftSettings] = useState<Record<string, Partial<ReportWriterDraftSettings>>>({});
  const [writerCustomFormulas, setWriterCustomFormulas] = useState<Record<string, Partial<ReportWriterCustomFormulaDraft>>>({});
  const [writerCustomDatasetText, setWriterCustomDatasetText] = useState<Record<string, string>>({});
  const [writerDraftStatus, setWriterDraftStatus] = useState<ReportingCommandStatus | null>(null);
  const [writerPreviewStatus, setWriterPreviewStatus] = useState<ReportingCommandStatus | null>(null);
  const [brandingPackStatus, setBrandingPackStatus] = useState<ReportingCommandStatus | null>(null);
  const [livePortfolioRefreshStatus, setLivePortfolioRefreshStatus] = useState<ReportingCommandStatus | null>(null);
  const [brandingDraft, setBrandingDraft] = useState<ReportBrandingDraftState>(() => buildDefaultReportBrandingDraft(data?.reporting ?? null));
  const [writerPreviewByGridId, setWriterPreviewByGridId] = useState<Record<string, ReportWriterGridRender | null>>({});
  const livePortfolioRefreshInFlight = useRef(false);
  const reportWriterDatasetSources = data?.reporting.reportWriterDatasetSources ?? [];
  const livePortfolioViews = data?.reporting.livePortfolioViews ?? [];
  const shouldAutoRefreshLivePortfolioViews = livePortfolioViews.some((view) => view.isMarketTickLinked || view.state === "LiveLinked");
  const runningRunActionId = runActionStatus?.state === "running" ? runActionStatus.id : null;
  const runningTemplateRunId = templateRunStatus?.state === "running" ? templateRunStatus.id : null;
  const runningTemplateLifecycleActionId = templateLifecycleStatus?.state === "running" ? templateLifecycleStatus.id : null;
  const runningScheduleActionId = scheduleActionStatus?.state === "running" ? scheduleActionStatus.id : null;
  const runningDeliveryFailureId = deliveryFailureStatus?.state === "running" ? deliveryFailureStatus.id : null;
  const savingWriterDraftId = writerDraftStatus?.state === "running" ? writerDraftStatus.id : null;
  const previewingWriterDraftId = writerPreviewStatus?.state === "running" ? writerPreviewStatus.id : null;
  const runningBrandingThemeId = brandingPackStatus?.state === "running" ? brandingPackStatus.id : null;
  const isRefreshingLivePortfolioViews = livePortfolioRefreshStatus?.state === "running";
  const reportingFundProfileId = resolveReportingFundProfileId(data?.reporting ?? null);
  const writerGrids = vm.templateRows.flatMap((template) => template.writerGrids);
  const scheduleDistributionOptions = data?.reporting.reportPackDistributions ?? [];
  const privateCapitalActivity = resolveReportingPrivateCapitalActivity(data);
  const selectedExportsTemplate = resolveSelectedExportsTemplate(vm.templateRows, exportsRunDraft);
  const exportsRunRows = vm.runStatusRows.filter(isExportsOnDemandRun);
  const templateRowsKey = vm.templateRows.map((template) => template.id).join("|");

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
      asOfDate: new Date().toISOString().slice(0, 10),
      maxRetries: 0,
      datasetSourceId: template.hasWriterGrids ? normalizeOptionalDatasetSourceId(templateRunDatasetSourceId) : null
    }, template.runActionLabel);
  }

  function updateExportsReportRunDraft(field: ExportsReportRunDraftField, value: string) {
    setExportsRunDraft((current) => ({
      ...current,
      [field]: value
    }));
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
    setWriterCustomDatasetText((current) => {
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

    const selectedDatasetSource = resolveReportWriterDatasetSource(reportWriterDatasetSources, settings.previewDataset);
    const retainedDatasetRows = buildRetainedReportWriterDatasetRows(selectedDatasetSource);
    const request = buildRenderReportTemplateRequest(
      grid,
      getWriterCurrentZones(grid),
      settings,
      customDataset?.rows ?? retainedDatasetRows);
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
                    <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">
                      Freshness: {view.state} · cut={view.asOf} · source={view.sourceAsOfUtc ?? "unavailable"}
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
                    <div className="mt-3 flex flex-wrap justify-end gap-2">
                      <Button
                        aria-label={`Preview ${theme.name} branded report pack`}
                        busy={runningBrandingThemeId === `${theme.themeId}:preview`}
                        busyLabel="Previewing"
                        disabled={!reportingFundProfileId || Boolean(runningBrandingThemeId)}
                        disabledReason={reportingFundProfileId ? null : "A fund profile is required before previewing a governed report pack."}
                        onClick={() => void handlePreviewBrandedPack(theme)}
                        size="sm"
                        variant="secondary"
                      >
                        <Eye className="h-4 w-4" aria-hidden="true" />
                        Preview
                      </Button>
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
              <div role="group" aria-label="Custom report branding override" className="mt-3 rounded-md border border-border/70 bg-background/30 px-3 py-3">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <h4 className="text-sm font-semibold text-foreground">Custom styling override</h4>
                    <p className="mt-1 text-xs leading-5 text-muted-foreground">
                      Generate this report pack with firm-specific colors, logo, footer, and disclaimer metadata.
                    </p>
                  </div>
                  <Badge variant="outline">Override</Badge>
                </div>
                <div className="mt-3 grid gap-2 md:grid-cols-3">
                  <label className="space-y-1">
                    <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Theme ID</span>
                    <Input
                      value={brandingDraft.themeId}
                      onChange={(event) => updateBrandingDraft("themeId", event.target.value)}
                      aria-label="Custom branding theme ID"
                      className="font-mono"
                    />
                  </label>
                  <label className="space-y-1">
                    <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Theme name</span>
                    <Input
                      value={brandingDraft.name}
                      onChange={(event) => updateBrandingDraft("name", event.target.value)}
                      aria-label="Custom branding theme name"
                    />
                  </label>
                  <label className="space-y-1">
                    <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Firm</span>
                    <Input
                      value={brandingDraft.firmName}
                      onChange={(event) => updateBrandingDraft("firmName", event.target.value)}
                      aria-label="Custom branding firm name"
                    />
                  </label>
                  {([
                    ["primaryColor", "Primary", brandingDraft.primaryColor],
                    ["accentColor", "Accent", brandingDraft.accentColor],
                    ["textColor", "Text", brandingDraft.textColor],
                    ["backgroundColor", "Background", brandingDraft.backgroundColor]
                  ] as const).map(([field, label, value]) => (
                    <label key={field} className="space-y-1">
                      <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</span>
                      <span className="flex items-center gap-2">
                        <span
                          aria-hidden="true"
                          className="h-6 w-6 shrink-0 rounded-sm border border-border"
                          style={{ backgroundColor: normalizeBrandingColor(value, "#FFFFFF") }}
                        />
                        <Input
                          value={value}
                          onChange={(event) => updateBrandingDraft(field, event.target.value)}
                          aria-label={`Custom branding ${label.toLowerCase()} color`}
                          className="font-mono"
                        />
                      </span>
                    </label>
                  ))}
                  <label className="space-y-1 md:col-span-2">
                    <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Logo URI</span>
                    <Input
                      value={brandingDraft.logoUri}
                      onChange={(event) => updateBrandingDraft("logoUri", event.target.value)}
                      aria-label="Custom branding logo URI"
                    />
                  </label>
                </div>
                <div className="mt-2 grid gap-2 md:grid-cols-2">
                  <label className="space-y-1">
                    <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Footer</span>
                    <Input
                      value={brandingDraft.footerText}
                      onChange={(event) => updateBrandingDraft("footerText", event.target.value)}
                      aria-label="Custom branding footer text"
                    />
                  </label>
                  <label className="space-y-1">
                    <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Disclaimer</span>
                    <Input
                      value={brandingDraft.disclaimer}
                      onChange={(event) => updateBrandingDraft("disclaimer", event.target.value)}
                      aria-label="Custom branding disclaimer"
                    />
                  </label>
                </div>
                <div className="mt-3 flex flex-wrap justify-end gap-2">
                  <Button
                    type="button"
                    size="sm"
                    variant="secondary"
                    busy={runningBrandingThemeId === "custom-branding-override:preview"}
                    busyLabel="Previewing"
                    disabled={!reportingFundProfileId || Boolean(runningBrandingThemeId)}
                    disabledReason={reportingFundProfileId ? null : "A fund profile is required before previewing a governed report pack."}
                    onClick={() => void handlePreviewCustomBrandedPack()}
                    aria-label="Preview custom branded report pack"
                  >
                    <Eye className="h-4 w-4" aria-hidden="true" />
                    Preview custom
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    busy={runningBrandingThemeId === "custom-branding-override"}
                    busyLabel="Generating"
                    disabled={!reportingFundProfileId || Boolean(runningBrandingThemeId)}
                    disabledReason={reportingFundProfileId ? null : "A fund profile is required before generating a governed report pack."}
                    onClick={() => void handleGenerateCustomBrandedPack()}
                    aria-label="Generate custom branded report pack"
                  >
                    <FileText className="h-4 w-4" aria-hidden="true" />
                    Generate custom
                  </Button>
                </div>
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
                    datasetSources={reportWriterDatasetSources}
                    isSaving={savingWriterDraftId === grid.id}
                    isPreviewing={previewingWriterDraftId === grid.id}
                    preview={writerPreviewByGridId[grid.id] ?? null}
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

      <ExportsReportRunner
        draft={exportsRunDraft}
        templates={vm.templateRows}
        selectedTemplate={selectedExportsTemplate}
        datasetSources={reportWriterDatasetSources}
        recentRuns={exportsRunRows}
        status={templateRunStatus}
        runningTemplateRunId={runningTemplateRunId}
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
                  <Badge variant={run.status === "Failed" ? "warning" : "outline"}>{run.status}</Badge>
                </div>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  {run.family} · {run.trigger} · {run.lineageSummary} · {run.auditSummary}
                </p>
                <dl className="mt-2 grid gap-2 text-xs sm:grid-cols-2 xl:grid-cols-3" aria-label={`${run.id} audit metadata`}>
                  <div>
                    <dt className="text-[11px] uppercase text-muted-foreground">Run ID</dt>
                    <dd className="break-all font-mono text-foreground">{run.runIdLabel}</dd>
                  </div>
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
                    {run.generatedGridArtifacts.length > 0 ? (
                      <div className="mt-1 flex flex-wrap gap-1.5" aria-label={`${run.id} report-writer grid exports`}>
                        {run.generatedGridArtifacts.map((grid) => (
                          <span
                            key={grid.id}
                            className="inline-flex min-w-0 items-center gap-1.5 rounded-sm border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground"
                          >
                            <span className="max-w-[16rem] truncate text-foreground">{grid.label}</span>
                            <a className="text-primary underline-offset-2 hover:underline" href={grid.jsonHref} target="_blank" rel="noreferrer">
                              JSON
                            </a>
                            <a className="text-primary underline-offset-2 hover:underline" href={grid.csvHref} target="_blank" rel="noreferrer">
                              CSV
                            </a>
                            <a className="text-primary underline-offset-2 hover:underline" href={grid.pdfHref} target="_blank" rel="noreferrer">
                              PDF
                            </a>
                            <a className="text-primary underline-offset-2 hover:underline" href={grid.xlsHref} target="_blank" rel="noreferrer">
                              XLS
                            </a>
                            <a className="text-primary underline-offset-2 hover:underline" href={grid.xlsxHref} target="_blank" rel="noreferrer">
                              XLSX
                            </a>
                          </span>
                        ))}
                      </div>
                    ) : null}
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

      <ReportingPrivateCapitalReadinessPanel activity={privateCapitalActivity} />

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
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Dataset source</span>
                  <Select
                    value={scheduleDraft.datasetSourceId}
                    onChange={(event) => updateScheduleDraft("datasetSourceId", event.target.value)}
                    aria-label="Reporting schedule dataset source"
                  >
                    <option value="">Default retained dataset</option>
                    {reportWriterDatasetSources.map((source) => (
                      <option key={source.sourceId} value={source.sourceId}>
                        {source.label} ({source.rowCount})
                      </option>
                    ))}
                  </Select>
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
                    <div
                      key={format}
                      className="rounded-sm border border-border/70 bg-secondary/25 px-2.5 py-1.5 text-xs text-foreground"
                    >
                      <Checkbox
                        checked={scheduleDraft.formats[format]}
                        onCheckedChange={(checked) => toggleScheduleDraftFormat(format, checked)}
                        aria-label={`Reporting schedule ${format} format`}
                        label={format}
                      />
                    </div>
                  ))}
                </div>
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  disabled={Boolean(runningScheduleActionId)}
                  onClick={stageScheduleDraftDeliveryTarget}
                  aria-label="Stage reporting schedule delivery target"
                >
                  <Plus className="h-4 w-4" aria-hidden="true" />
                  Stage target
                </Button>
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
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  busy={runningScheduleActionId === "schedule-due:run"}
                  busyLabel="Running"
                  disabled={Boolean(runningScheduleActionId)}
                  onClick={() => void handleRunDueSchedules()}
                  aria-label="Run due reporting schedules"
                >
                  <RotateCcw className="h-4 w-4" aria-hidden="true" />
                  Run due
                </Button>
              </div>
              {scheduleDraft.deliveryTargets.length > 0 ? (
                <div className="mt-3 rounded-md border border-border/70 bg-background/30 px-3 py-2">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Staged delivery targets</span>
                    <Badge variant="outline">{scheduleDraft.deliveryTargets.length}</Badge>
                  </div>
                  <ul aria-label="Staged reporting schedule delivery targets" className="mt-2 space-y-1.5">
                    {scheduleDraft.deliveryTargets.map((target) => (
                      <li
                        key={target.distributionId}
                        className="flex flex-wrap items-center justify-between gap-2 rounded-sm border border-border/70 bg-secondary/20 px-2 py-1.5 text-xs"
                      >
                        <span className="min-w-0">
                          <span className="block break-all font-mono text-foreground">{target.distributionId}</span>
                          <span className="mt-0.5 block text-muted-foreground">
                            {target.deliveryMode} · {formatScheduleDraftTargetFormats(target)}
                          </span>
                        </span>
                        <Button
                          type="button"
                          size="icon"
                          variant="ghost"
                          disabled={Boolean(runningScheduleActionId)}
                          onClick={() => removeScheduleDraftDeliveryTarget(target.distributionId)}
                          aria-label={`Remove staged delivery target ${target.distributionId}`}
                        >
                          <Trash2 className="h-4 w-4" aria-hidden="true" />
                        </Button>
                      </li>
                    ))}
                  </ul>
                </div>
              ) : null}
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
                      <ReportingScheduleField label="Dataset" value={schedule.datasetSourceLabel} />
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
                        <ReportingScheduleField label="Access expiry" value={plan.accessExpiryLabel} />
                        <ReportingScheduleField label="Access" value={plan.accessSummaryLabel} />
                        <ReportingScheduleField label="Channel" value={plan.channelSummaryLabel} />
                        <ReportingScheduleField label="Downloads" value={plan.downloadSummaryLabel} />
                        <ReportingScheduleField label="Notifications" value={plan.notificationSummaryLabel} />
                        <ReportingScheduleField label="Writer dataset" value={plan.reportWriterDatasetSummaryLabel} />
                        <ReportingScheduleField label="Writer grids" value={plan.reportWriterGridSummaryLabel} />
                        <ReportingScheduleField label="Artifact integrity" value={plan.integrityLabel} />
                        <ReportingScheduleField label="Entitlement" value={plan.entitlementLabel} />
                        <ReportingScheduleField label="Branding" value={plan.brandingLabel} />
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
                      </div>
                      <div className="mt-3 flex flex-wrap items-center gap-2">
                        <Button
                          variant="outline"
                          size="sm"
                          busy={runningScheduleActionId === `${plan.id}:run`}
                          busyLabel="Running"
                          disabled={plan.readinessVariant !== "success" || Boolean(runningScheduleActionId)}
                          disabledReason={plan.readinessVariant !== "success" ? plan.readinessSummary : null}
                          onClick={() => void handleSchedulePlanRun(plan)}
                        >
                          Run schedule for recipient
                        </Button>
                      </div>
                      {plan.lastDeliveryLinks.length > 0 ? (
                        <div className="mt-2 flex flex-wrap gap-1.5" aria-label={`${plan.id} retained delivery access links`}>
                          {plan.lastDeliveryLinks.map((link) => (
                            <a
                              key={link.id}
                              href={link.href}
                              aria-label={link.ariaLabel}
                              className="inline-flex min-w-0 items-center gap-1.5 rounded-sm border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
                            >
                              <span className="max-w-[12rem] truncate text-foreground">{link.label}</span>
                              <Badge variant="outline">{link.tokenLabel}</Badge>
                              {link.expiresLabel ? <span>{link.expiresLabel}</span> : null}
                            </a>
                          ))}
                        </div>
                      ) : plan.lastDeliveryHref ? (
                        <div className="mt-2 flex flex-wrap gap-2 text-[11px]">
                          <a className="break-all font-mono text-primary underline-offset-2 hover:underline" href={plan.lastDeliveryHref}>
                            {plan.lastDeliveryHref}
                          </a>
                        </div>
                      ) : null}
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
            {deliveryFailureStatus ? (
              <ReportingCommandStatusView status={deliveryFailureStatus} />
            ) : null}
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
                        {attempt.package.deliveryChannelSummary ? (
                          <p>{attempt.package.deliveryChannelSummary}</p>
                        ) : null}
                        {attempt.package.deliveryAccessSummary ? (
                          <p>{attempt.package.deliveryAccessSummary}</p>
                        ) : null}
                        {attempt.package.downloadSummary ? (
                          <p>{attempt.package.downloadSummary}</p>
                        ) : null}
                        {attempt.package.accessExpiresAtUtc ? (
                          <p className="break-all font-mono text-[11px]">
                            Access expires: {attempt.package.accessExpiresAtUtc}
                          </p>
                        ) : null}
                        {attempt.package.brandingTheme ? (
                          <p className="break-all text-[11px]">
                            Branding: {attempt.package.brandingTheme.name} · {attempt.package.brandingTheme.firmName} · {attempt.package.brandingTheme.themeId}
                          </p>
                        ) : null}
                        {attempt.package.accessLinks?.length ? (
                          <div className="flex flex-wrap gap-1.5" aria-label={`${attempt.attemptId} package access links`}>
                            {attempt.package.accessLinks.map((link) => (
                              <a
                                key={`${attempt.attemptId}-${link.kind}-${link.href}`}
                                className="inline-flex min-w-0 items-center gap-1.5 rounded-sm border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
                                href={link.href}
                                aria-label={`${link.label} ${link.requiresToken ? "token gated" : "internal route"} ${link.href}`}
                              >
                                <span className="max-w-[12rem] truncate text-foreground">{link.label}</span>
                                <Badge variant="outline">{link.requiresToken ? "Token gated" : "Internal"}</Badge>
                                {link.expiresAtUtc ? <span>Expires {link.expiresAtUtc}</span> : null}
                              </a>
                            ))}
                          </div>
                        ) : attempt.package.secureLink.startsWith("/") ? (
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
                        {attempt.package.notifications?.length ? (
                          <ul aria-label={`${attempt.recipient} delivery notifications`} className="grid gap-1.5">
                            {attempt.package.notifications.map((notification) => (
                              <li
                                key={`${attempt.attemptId}-${notification.notificationId}`}
                                className="rounded-md border border-border/70 bg-background/40 px-2 py-2"
                              >
                                <div className="flex flex-wrap items-start justify-between gap-2">
                                  <span className="min-w-0">
                                    <span className="block font-semibold text-foreground">{notification.subject}</span>
                                    <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">
                                      {notification.notificationId}
                                    </span>
                                  </span>
                                  <Badge variant="outline">{notification.status}</Badge>
                                </div>
                                <p className="mt-1 text-xs leading-5 text-muted-foreground">{notification.body}</p>
                                <dl className="mt-2 grid gap-1 text-[11px] sm:grid-cols-2">
                                  <ReportingScheduleField label="Channel" value={`${notification.channel} · ${notification.deliveryMode}`} />
                                  <ReportingScheduleField label="Recipient" value={`${notification.recipient} · ${notification.recipientRole}`} />
                                  <ReportingScheduleField label="Created" value={notification.createdAtUtc} />
                                  <ReportingScheduleField label="Expires" value={notification.expiresAtUtc ?? "No expiry"} />
                                </dl>
                                <a
                                  className="mt-2 block break-all font-mono text-[11px] text-primary underline-offset-2 hover:underline"
                                  href={notification.href}
                                  aria-label={`${notification.subject} ${notification.requiresToken ? "token gated" : "internal route"} ${notification.href}`}
                                >
                                  {notification.href}
                                </a>
                              </li>
                            ))}
                          </ul>
                        ) : null}
                        <p className="break-all font-mono text-[11px]">
                          {attempt.package.retainedManifestPath}
                        </p>
                        {attempt.package.reportingRunId ? (
                          <p className="break-all font-mono text-[11px]">
                            Reporting run: {attempt.package.reportingRunId}
                          </p>
                        ) : null}
                        {attempt.package.reportingTemplateId ? (
                          <p className="break-all font-mono text-[11px]">
                            Template: {attempt.package.reportingTemplateId}
                          </p>
                        ) : null}
                        {attempt.package.reportingScheduleId ? (
                          <p className="break-all font-mono text-[11px]">
                            Schedule: {attempt.package.reportingScheduleId}
                          </p>
                        ) : null}
                        {hasReportingRunMetadata(attempt.package) ? (
                          <p className="break-all font-mono text-[11px]">
                            Run metadata: {formatReportingRunPackageMetadata(attempt.package)}
                          </p>
                        ) : null}
                        {hasReportWriterDatasetMetadata(attempt.package) ? (
                          <p className="break-all font-mono text-[11px]">
                            Dataset source: {formatReportWriterDatasetMetadata(attempt.package)}
                          </p>
                        ) : null}
                        {attempt.package.sourceArtifacts?.length ? (
                          <p className="break-all font-mono text-[11px]">
                            Source artifacts: {attempt.package.sourceArtifacts.join(", ")}
                          </p>
                        ) : null}
                        {attempt.package.generatedReportWriterGrids?.length ? (
                          <div className="break-all font-mono text-[11px]">
                            <p>
                              Generated grids: {attempt.package.generatedReportWriterGrids.map((grid) => {
                                const validationSummary = grid.validationSummary?.trim();
                                return `${grid.title || grid.gridId} (${grid.kind}, ${grid.dimensionCount}d/${grid.metricCount}m/${grid.formulaCount}f${validationSummary ? `, validation ${validationSummary}` : ""})`;
                              }).join(", ")}
                            </p>
                            {attempt.package.reportingRunId ? (
                              <div className="mt-1 flex flex-wrap gap-1.5" aria-label={`${attempt.attemptId} package report-writer grid exports`}>
                                {attempt.package.generatedReportWriterGrids.map((grid) => {
                                  const title = grid.title || grid.gridId;
                                  return (
                                    <span
                                      key={`${attempt.attemptId}-${grid.gridId}`}
                                      className="inline-flex min-w-0 items-center gap-1.5 rounded-sm border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground"
                                    >
                                      <span className="max-w-[16rem] truncate text-foreground">{title}</span>
                                      <a
                                        className="text-primary underline-offset-2 hover:underline"
                                        href={reportingRunReportWriterGridEndpoint(attempt.package.reportingRunId!, grid.gridId)}
                                        target="_blank"
                                        rel="noreferrer"
                                      >
                                        JSON
                                      </a>
                                      <a
                                        className="text-primary underline-offset-2 hover:underline"
                                        href={reportingRunReportWriterGridEndpoint(attempt.package.reportingRunId!, grid.gridId, "csv")}
                                        target="_blank"
                                        rel="noreferrer"
                                      >
                                        CSV
                                      </a>
                                      <a
                                        className="text-primary underline-offset-2 hover:underline"
                                        href={reportingRunReportWriterGridEndpoint(attempt.package.reportingRunId!, grid.gridId, "pdf")}
                                        target="_blank"
                                        rel="noreferrer"
                                      >
                                        PDF
                                      </a>
                                      <a
                                        className="text-primary underline-offset-2 hover:underline"
                                        href={reportingRunReportWriterGridEndpoint(attempt.package.reportingRunId!, grid.gridId, "xls")}
                                        target="_blank"
                                        rel="noreferrer"
                                      >
                                        XLS
                                      </a>
                                      <a
                                        className="text-primary underline-offset-2 hover:underline"
                                        href={reportingRunReportWriterGridEndpoint(attempt.package.reportingRunId!, grid.gridId, "xlsx")}
                                        target="_blank"
                                        rel="noreferrer"
                                      >
                                        XLSX
                                      </a>
                                    </span>
                                  );
                                })}
                              </div>
                            ) : null}
                          </div>
                        ) : null}
                        {attempt.package.renderedReportWriterGrids?.length ? (
                          <div className="rounded-md border border-border/70 bg-background/40 px-2 py-2 text-[11px]">
                            <p className="break-all font-mono">
                              Rendered grid rows: {attempt.package.renderedReportWriterGrids.map((grid) => `${grid.title || grid.gridId} (${grid.rows.length}r/${grid.columns.length}c)`).join(", ")}
                            </p>
                            <ul aria-label={`${attempt.recipient} rendered report-writer grid evidence`} className="mt-1 space-y-1">
                              {attempt.package.renderedReportWriterGrids.map((grid) => {
                                const title = grid.title || grid.gridId;
                                const dictionary = grid.dataDictionary ?? [];
                                const validationChecks = grid.validationChecks ?? [];
                                const generatedFieldCount = dictionary.filter((field) => field.isGenerated).length;
                                const passedCheckCount = validationChecks.filter((check) => check.status.toLowerCase() === "passed").length;
                                const issueCheckCount = validationChecks.length - passedCheckCount;
                                return (
                                  <li key={`${attempt.attemptId}-${grid.gridId}-evidence`} className="break-all">
                                    <p className="font-semibold text-foreground">
                                      {title} dictionary: {dictionary.length} field{dictionary.length === 1 ? "" : "s"}, {generatedFieldCount} generated
                                    </p>
                                    {dictionary.length ? (
                                      <p className="font-mono">
                                        {dictionary.slice(0, 4).map((field) => `${field.label || field.key}:${field.sourceField || "generated"}:${field.dataType}${field.isGenerated ? ":generated" : ""}`).join(" | ")}
                                      </p>
                                    ) : null}
                                    {validationChecks.length ? (
                                      <>
                                        <p className="mt-1 font-semibold text-foreground">
                                          {title} validation: {passedCheckCount} passed / {validationChecks.length} checks{issueCheckCount > 0 ? `, ${issueCheckCount} review` : ""}
                                        </p>
                                        <p className="font-mono">
                                          {validationChecks.slice(0, 4).map((check) => `${check.checkId}:${check.status}:${check.detail}`).join(" | ")}
                                        </p>
                                      </>
                                    ) : null}
                                  </li>
                                );
                              })}
                            </ul>
                          </div>
                        ) : null}
                        {attempt.package.lineProvenance?.length ? (
                          <div className="rounded-md border border-border/70 bg-background/40 px-2 py-2 text-[11px]">
                            <p className="font-semibold text-foreground">
                              Report-line provenance: {attempt.package.lineProvenance.length} line{attempt.package.lineProvenance.length === 1 ? "" : "s"}
                            </p>
                            <ul aria-label={`${attempt.recipient} package report-line provenance`} className="mt-1 space-y-1">
                              {attempt.package.lineProvenance.slice(0, 4).map((line) => (
                                <li key={`${attempt.attemptId}-${line.lineKey}-${line.evidenceId ?? line.sourceId}`} className="break-all">
                                  <span className="font-mono text-foreground">{line.lineKey}</span>
                                  <span> · {line.sourceKind} · {line.reportValue ?? "value pending"}</span>
                                  {line.financialRecordHref ? (
                                    <a
                                      className="ml-2 text-primary underline-offset-2 hover:underline"
                                      href={line.financialRecordHref}
                                      aria-label={`Open Financial Record Explorer for ${line.lineKey}`}
                                    >
                                      {formatFinancialRecordExplorerName(line.financialRecordExplorerId)}
                                    </a>
                                  ) : null}
                                </li>
                              ))}
                            </ul>
                          </div>
                        ) : null}
                        {attempt.package.deliveryEvidencePacket ? (
                          <div className="rounded-md border border-border/70 bg-background/40 px-2 py-2 text-[11px]">
                            <p className="font-semibold text-foreground">
                              Evidence packet: {attempt.package.deliveryEvidencePacket.packetKind} · {attempt.package.deliveryEvidencePacket.datasetVersion}
                            </p>
                            <p className="mt-1 break-all font-mono">
                              Template: {attempt.package.deliveryEvidencePacket.templateVersion} · Channel: {attempt.package.deliveryEvidencePacket.deliveryChannel}
                            </p>
                            <p className="mt-1">
                              Contents: {attempt.package.deliveryEvidencePacket.packageContents.length} · Support evidence: {attempt.package.deliveryEvidencePacket.supportEvidenceIds.length} · Delivery evidence: {attempt.package.deliveryEvidencePacket.deliveryEvidence.length}
                            </p>
                            {attempt.package.deliveryEvidencePacket.packageContents.length > 0 ? (
                              <p className="mt-1 break-all font-mono">
                                Package contents: {attempt.package.deliveryEvidencePacket.packageContents.join(" | ")}
                              </p>
                            ) : null}
                            {attempt.package.deliveryEvidencePacket.supportEvidenceIds.length > 0 ? (
                              <p className="mt-1 break-all font-mono">
                                Support evidence IDs: {attempt.package.deliveryEvidencePacket.supportEvidenceIds.join(" | ")}
                              </p>
                            ) : null}
                            {attempt.package.deliveryEvidencePacket.deliveryEvidence.length > 0 ? (
                              <ul aria-label={`${attempt.recipient} delivery packet evidence links`} className="mt-1 space-y-1">
                                {attempt.package.deliveryEvidencePacket.deliveryEvidence.map((evidence) => (
                                  <li key={`${attempt.attemptId}-${evidence.evidenceId}`} className="break-all font-mono">
                                    {evidence.route ? (
                                      <a
                                        href={evidence.route}
                                        className="text-primary underline-offset-2 hover:underline"
                                        aria-label={`Open delivery evidence ${evidence.evidenceId}`}
                                      >
                                        {evidence.evidenceId}
                                      </a>
                                    ) : (
                                      <span>{evidence.evidenceId}</span>
                                    )}
                                    <span> · {evidence.label} · {evidence.source}{evidence.capturedAtUtc ? ` · ${evidence.capturedAtUtc}` : ""}</span>
                                  </li>
                                ))}
                              </ul>
                            ) : null}
                            <p className="mt-1 break-all font-mono">
                              Entitlement: {attempt.package.deliveryEvidencePacket.entitlementScope} · Fund: {attempt.package.deliveryEvidencePacket.fundProfileId} · Account: {attempt.package.deliveryEvidencePacket.fundAccountId} · Period: {attempt.package.deliveryEvidencePacket.period}
                            </p>
                            {attempt.package.deliveryEvidencePacket.recipientList.length > 0 ? (
                              <ul aria-label={`${attempt.recipient} delivery packet recipients`} className="mt-1 space-y-1">
                                {attempt.package.deliveryEvidencePacket.recipientList.map((recipient) => (
                                  <li key={`${attempt.attemptId}-${recipient.distributionId}`} className="break-all font-mono">
                                    {recipient.distributionId}: {recipient.recipient} · {recipient.recipientRole} · {recipient.channel}
                                  </li>
                                ))}
                              </ul>
                            ) : null}
                            {attempt.package.deliveryEvidencePacket.approvalChain.length > 0 ? (
                              <ul aria-label={`${attempt.recipient} delivery packet approval chain`} className="mt-1 space-y-1">
                                {attempt.package.deliveryEvidencePacket.approvalChain.map((step) => (
                                  <li key={`${attempt.attemptId}-${step.at}-${step.actor}-${step.action}`} className="break-all font-mono">
                                    {step.at}: {step.actor} {step.action} {step.fromState} to {step.toState}{step.note ? ` - ${step.note}` : ""}
                                  </li>
                                ))}
                              </ul>
                            ) : null}
                            {attempt.package.deliveryEvidencePacket.requestHistory.length > 0 ? (
                              <p className="mt-1 break-all font-mono">
                                Request history: {attempt.package.deliveryEvidencePacket.requestHistory.join(" | ")}
                              </p>
                            ) : null}
                            {attempt.package.deliveryEvidencePacket.amendmentReason ? (
                              <p className="mt-1 break-all font-mono">
                                Amendment: {attempt.package.deliveryEvidencePacket.amendmentReason}
                              </p>
                            ) : null}
                            {attempt.package.deliveryEvidencePacket.restatementLineage ? (
                              <p className="mt-1 break-all font-mono">
                                Restatement lineage: {attempt.package.deliveryEvidencePacket.restatementLineage}
                              </p>
                            ) : null}
                            {attempt.package.deliveryEvidencePacket.auditEventReferences?.length ? (
                              <p className="mt-1 break-all font-mono">
                                Audit references: {attempt.package.deliveryEvidencePacket.auditEventReferences.join(" | ")}
                              </p>
                            ) : null}
                            {attempt.package.deliveryEvidencePacket.blockedDownstreamOutputs?.length ? (
                              <p className="mt-1 break-all font-mono text-warning">
                                Blocked downstream outputs: {attempt.package.deliveryEvidencePacket.blockedDownstreamOutputs.join(" | ")}
                              </p>
                            ) : null}
                            <Link
                              className="mt-2 inline-flex items-center gap-1 text-[11px] font-medium text-primary underline-offset-2 hover:underline"
                              to={reportPackDeliveryEvidencePath(attempt.reportId, attempt.attemptId)}
                              aria-label={`Open delivery evidence graph for ${attempt.recipient} delivery attempt`}
                            >
                              <Network className="h-3 w-3" aria-hidden="true" />
                              Open delivery evidence graph
                            </Link>
                          </div>
                        ) : null}
                        {attempt.package.artifacts.length > 0 ? (
                          <ul aria-label={`${attempt.recipient} package artifact integrity`} className="grid gap-2 pt-1">
                            {attempt.package.artifacts.map((artifact) => (
                              <li
                                key={`${attempt.attemptId}-${artifact.artifactName}`}
                                className="rounded-md border border-border/70 bg-background/40 px-2 py-2"
                              >
                                <div className="flex flex-wrap items-start justify-between gap-2">
                                  <span className="min-w-0">
                                    {artifact.downloadRoute ? (
                                      <a
                                        className="block break-all font-mono text-[11px] text-primary underline-offset-2 hover:underline"
                                        href={artifact.downloadRoute}
                                        aria-label={`Download ${artifact.artifactName}`}
                                      >
                                        {artifact.artifactName}
                                      </a>
                                    ) : (
                                      <span className="block break-all font-mono text-[11px] text-foreground">
                                        {artifact.artifactName}
                                      </span>
                                    )}
                                    <span className="mt-1 block break-all font-mono text-[11px]">
                                      {artifact.retainedPath}
                                    </span>
                                  </span>
                                  <Badge variant="outline">{artifact.format}</Badge>
                                </div>
                                <dl className="mt-2 grid gap-1 text-[11px] sm:grid-cols-2">
                                  <ReportingScheduleField label="Size" value={`${artifact.byteSize.toLocaleString()} bytes`} />
                                  <ReportingScheduleField label="Evidence" value={artifact.evidenceId} />
                                  <ReportingScheduleField label="Checksum" value={artifact.checksumSha256 || "Checksum pending"} />
                                  <ReportingScheduleField label="Version" value={artifact.versionStamp || "Version pending"} />
                                </dl>
                              </li>
                            ))}
                          </ul>
                        ) : null}
                      </div>
                    ) : null}
                    {attempt.failureReason ? <p className="mt-1 text-xs text-warning">{attempt.failureReason}</p> : null}
                    <div className="mt-3 flex flex-wrap items-center gap-2">
                      <Button
                        variant="outline"
                        size="sm"
                        busy={runningDeliveryFailureId === `${attempt.attemptId}:delivery-failure`}
                        busyLabel="Recording"
                        disabled={attempt.state === "Failed" || Boolean(runningDeliveryFailureId)}
                        disabledReason={attempt.state === "Failed" ? "This delivery attempt is already recorded as failed." : null}
                        onClick={() => void handleRecordDeliveryFailure(attempt)}
                      >
                        Record delivery failure
                      </Button>
                    </div>
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
  datasetSources: ReportWriterDatasetSource[];
  customDatasetText: string;
  isSaving: boolean;
  isPreviewing: boolean;
  preview: ReportWriterGridRender | null;
  getZoneTokens: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone) => ReportingWriterToken[];
  onTokenDragStart: (event: DragEvent<HTMLElement>, token: ReportingWriterToken, origin?: ReportWriterTokenDragOrigin | null) => void;
  onZoneDrop: (event: DragEvent<HTMLElement>, grid: ReportingWriterGridRow, zone: ReportWriterDropZone) => void;
  onTokenRemove: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone, tokenId: string) => void;
  onTokenMove: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone, tokenId: string, direction: -1 | 1) => void;
  onReset: (grid: ReportingWriterGridRow) => void;
  onSettingsChange: (grid: ReportingWriterGridRow, field: ReportWriterDraftSettingsField, value: string) => void;
  onCustomFormulaChange: (grid: ReportingWriterGridRow, field: ReportWriterCustomFormulaField, value: string) => void;
  onCustomDatasetChange: (grid: ReportingWriterGridRow, value: string) => void;
  onPreview: (grid: ReportingWriterGridRow) => void | Promise<void>;
  onSave: (grid: ReportingWriterGridRow) => void | Promise<void>;
}

function ReportWriterDesignerGrid({
  grid,
  settings,
  customFormula,
  datasetSources,
  customDatasetText,
  isSaving,
  isPreviewing,
  preview,
  getZoneTokens,
  onTokenDragStart,
  onZoneDrop,
  onTokenRemove,
  onTokenMove,
  onReset,
  onSettingsChange,
  onCustomFormulaChange,
  onCustomDatasetChange,
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
  const dataDictionary = preview.dataDictionary ?? [];
  const validationChecks = preview.validationChecks ?? [];
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

interface ExportsReportRunnerProps {
  draft: ExportsReportRunDraftState;
  templates: ReportingTemplateRow[];
  selectedTemplate: ReportingTemplateRow | null;
  datasetSources: ReportWriterDatasetSource[];
  recentRuns: ReportingRunStatusRow[];
  status: ReportingCommandStatus | null;
  runningTemplateRunId: string | null;
  onDraftChange: (field: ExportsReportRunDraftField, value: string) => void;
  onRun: () => void;
}

function ExportsReportRunner({
  draft,
  templates,
  selectedTemplate,
  datasetSources,
  recentRuns,
  status,
  runningTemplateRunId,
  onDraftChange,
  onRun
}: ExportsReportRunnerProps) {
  const selectedDataset = datasetSources.find((source) => source.sourceId === draft.datasetSourceId) ?? null;
  const runDisabledReason = resolveExportsRunDisabledReason(selectedTemplate);
  const isRunningSelected = Boolean(selectedTemplate && runningTemplateRunId === selectedTemplate.id);

  return (
    <section role="region" aria-label="Exports report runner">
      <Card className="panel-surface">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <div className="eyebrow-label">Exports</div>
              <CardTitle>Run a governed report now</CardTitle>
              <CardDescription>
                Submit approved Reporting templates through the shared run endpoint with explicit date, requester, retry, and dataset context.
              </CardDescription>
            </div>
            <span className="flex flex-wrap items-center gap-1.5">
              <Badge variant={selectedTemplate?.canRunOnDemand ? "success" : "warning"}>
                {selectedTemplate?.canRunOnDemand ? "Runnable" : "Gated"}
              </Badge>
              <Badge variant="outline">POST {FUND_STRUCTURE_API_ENDPOINTS.reportingRuns}</Badge>
              <Badge variant="outline">{WORKSTATION_ROUTE_CATALOG.reportingExports}</Badge>
            </span>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-3 lg:grid-cols-[1.3fr_0.7fr_0.7fr]">
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Template</span>
              <Select
                value={selectedTemplate?.id ?? draft.templateRowId}
                onChange={(event) => onDraftChange("templateRowId", event.target.value)}
                aria-label="Exports report template"
              >
                {templates.length > 0 ? templates.map((template) => (
                  <option key={template.id} value={template.id} disabled={!template.canRunOnDemand}>
                    {template.name} v{template.versionNumber} ({template.statusLabel})
                  </option>
                )) : (
                  <option value="">No report templates available</option>
                )}
              </Select>
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">As of</span>
              <Input
                type="date"
                value={draft.asOfDate}
                onChange={(event) => onDraftChange("asOfDate", event.target.value)}
                aria-label="Exports report as-of date"
              />
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Retries</span>
              <Input
                type="number"
                min="0"
                step="1"
                value={draft.maxRetries}
                onChange={(event) => onDraftChange("maxRetries", event.target.value)}
                aria-label="Exports report max retries"
              />
            </label>
          </div>

          <div className="grid gap-3 lg:grid-cols-[1fr_1fr_auto]">
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Dataset source</span>
              <Select
                value={draft.datasetSourceId}
                onChange={(event) => onDraftChange("datasetSourceId", event.target.value)}
                disabled={!selectedTemplate?.hasWriterGrids || datasetSources.length === 0}
                aria-label="Exports report dataset source"
              >
                <option value="">Default retained dataset</option>
                {datasetSources.map((source) => (
                  <option key={source.sourceId} value={source.sourceId}>
                    {source.label} ({source.rowCount})
                  </option>
                ))}
              </Select>
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Requested by</span>
              <Input
                value={draft.requestedBy}
                onChange={(event) => onDraftChange("requestedBy", event.target.value)}
                aria-label="Exports report requested by"
              />
            </label>
            <div className="flex items-end">
              <Button
                className="w-full lg:w-auto"
                disabled={Boolean(runDisabledReason) || Boolean(runningTemplateRunId)}
                disabledReason={runDisabledReason}
                busy={isRunningSelected}
                busyLabel="Running"
                onClick={onRun}
                aria-label={selectedTemplate ? `Run ${selectedTemplate.name} from Exports` : "Run report from Exports"}
              >
                <Send className="h-4 w-4" aria-hidden="true" />
                Run report
              </Button>
            </div>
          </div>

          <div className="grid gap-3 xl:grid-cols-[1fr_1fr]">
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3" aria-label="Exports selected template readiness">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="min-w-0">
                  <h4 className="text-sm font-semibold text-foreground">{selectedTemplate?.name ?? "No template selected"}</h4>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">
                    {selectedTemplate ? selectedTemplate.approvalSummary : "Load approved report templates before running an export report."}
                  </p>
                </div>
                <span className="flex flex-wrap justify-end gap-1.5">
                  {selectedTemplate ? (
                    <>
                      <Badge variant={selectedTemplate.statusVariant}>{selectedTemplate.statusLabel}</Badge>
                      <Badge variant={selectedTemplate.accessGovernance.postureVariant}>{selectedTemplate.accessGovernance.postureLabel}</Badge>
                      <Badge variant="outline">{selectedTemplate.family}</Badge>
                    </>
                  ) : <Badge variant="outline">Unavailable</Badge>}
                </span>
              </div>
              <dl className="mt-3 grid gap-2 text-xs sm:grid-cols-2" aria-label="Exports report request summary">
                <ReportingScheduleField label="Template" value={selectedTemplate?.templateName ?? "No template"} />
                <ReportingScheduleField label="Version" value={selectedTemplate?.version ?? "Unavailable"} />
                <ReportingScheduleField label="Sections" value={selectedTemplate?.sectionSummary ?? "Unavailable"} />
                <ReportingScheduleField label="Access" value={selectedTemplate?.accessSummary ?? "Unavailable"} />
                <ReportingScheduleField label="Dataset" value={selectedDataset ? `${selectedDataset.label} (${selectedDataset.rowCount})` : "Default retained dataset"} />
                <ReportingScheduleField label="Requester" value={draft.requestedBy.trim() || defaultExportsReportRunRequester} />
              </dl>
              {runDisabledReason ? <p className="mt-3 text-xs leading-5 text-warning">{runDisabledReason}</p> : null}
            </div>

            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h4 className="text-sm font-semibold text-foreground">Recent export runs</h4>
                <Badge variant="outline">{recentRuns.length}</Badge>
              </div>
              {recentRuns.length > 0 ? (
                <div role="list" aria-label="Recent Exports report runs" className="mt-3 space-y-2">
                  {recentRuns.slice(0, 3).map((run) => (
                    <div key={run.id} role="listitem" className="rounded-sm border border-border/60 bg-background/30 px-2.5 py-2 text-xs">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <span className="break-all font-mono text-foreground">{run.id}</span>
                        <Badge variant={run.status === "Failed" ? "warning" : "outline"}>{run.status}</Badge>
                      </div>
                      <p className="mt-1 text-muted-foreground">
                        {run.templateLabel} / {run.asOfDateLabel} / {run.datasetSourceLabel}
                      </p>
                    </div>
                  ))}
                </div>
              ) : (
                <p role="status" className="mt-3 text-sm text-muted-foreground">No export runs have been run yet.</p>
              )}
            </div>
          </div>

          {status?.label === "Exports report run" ? <ReportingCommandStatusView status={status} /> : null}
        </CardContent>
      </Card>
    </section>
  );
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

function buildDefaultReportBrandingDraft(reporting: AccountingWorkspaceResponse["reporting"] | null): ReportBrandingDraftState {
  const theme = reporting?.brandingThemes?.find((item) => !item.isBuiltIn)
    ?? reporting?.brandingThemes?.[0]
    ?? null;

  return {
    themeId: normalizeIdentifierToken(theme?.themeId, "custom-report-branding"),
    name: normalizeDraftText(theme?.name, "Custom Report Theme"),
    firmName: normalizeDraftText(theme?.firmName, "Meridian Reporting"),
    primaryColor: normalizeBrandingColor(theme?.primaryColor, "#195E63"),
    accentColor: normalizeBrandingColor(theme?.accentColor, "#C99700"),
    textColor: normalizeBrandingColor(theme?.textColor, "#111827"),
    backgroundColor: normalizeBrandingColor(theme?.backgroundColor, "#FFFFFF"),
    logoUri: normalizeDraftText(theme?.logoUri, ""),
    footerText: normalizeDraftText(theme?.footerText, "Confidential report pack."),
    disclaimer: normalizeDraftText(theme?.disclaimer, "Prepared for authorized investor review.")
  };
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

function buildReportBrandingOverride(draft: ReportBrandingDraftState): ReportBrandingTheme {
  const themeId = normalizeIdentifierToken(draft.themeId, "custom-report-branding");
  const name = normalizeDraftText(draft.name, "Custom Report Theme");
  const logoUri = normalizeDraftText(draft.logoUri, "");
  const footerText = normalizeDraftText(draft.footerText, "");
  const disclaimer = normalizeDraftText(draft.disclaimer, "");

  return {
    themeId,
    name,
    firmName: normalizeDraftText(draft.firmName, "Meridian Reporting"),
    primaryColor: normalizeBrandingColor(draft.primaryColor, "#195E63"),
    accentColor: normalizeBrandingColor(draft.accentColor, "#C99700"),
    textColor: normalizeBrandingColor(draft.textColor, "#111827"),
    backgroundColor: normalizeBrandingColor(draft.backgroundColor, "#FFFFFF"),
    logoUri: logoUri || null,
    footerText: footerText || null,
    disclaimer: disclaimer || null,
    isBuiltIn: false
  };
}

function normalizeBrandingColor(value: string | null | undefined, fallback: string): string {
  const normalized = normalizeDraftText(value, fallback).replace(/^#?([0-9A-Fa-f]{6})$/, "#$1");
  return /^#[0-9A-Fa-f]{6}$/.test(normalized)
    ? normalized.toUpperCase()
    : fallback;
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

function buildExportsReportRunRequest(
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

function resolveExportsRunDisabledReason(template: ReportingTemplateRow | null): string | null {
  if (!template) {
    return "No report template is available to run.";
  }

  return template.canRunOnDemand ? null : template.runDisabledReason ?? `${template.name} is not approved for export runs.`;
}

function isExportsOnDemandRun(run: ReportingRunStatusRow): boolean {
  const trigger = run.trigger.trim().toLowerCase().replace(/[^a-z]/g, "");
  return trigger === "adhoc" || trigger === "ondemand" || trigger === "manual";
}

function buildReportRunResultDetails(run: {
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

function formatScheduleDraftTargetFormats(target: ReportingScheduleDraftTarget): string {
  const formats = reportingScheduleArtifactFormats.filter((format) => target.formats[format]);
  return formats.length > 0 ? formats.join("/") : "No formats";
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

function normalizeReportWriterPreviewDatasetProfile(value: string | null | undefined): ReportWriterPreviewDatasetProfile {
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

function buildRetainedReportWriterDatasetRows(
  datasetSource: ReportWriterDatasetSource | null
): Record<string, string>[] | null {
  if (!datasetSource || datasetSource.rows.length === 0) {
    return null;
  }

  return datasetSource.rows.map((row) => ({ ...row }));
}

function resolveReportWriterDatasetSource(
  datasetSources: ReportWriterDatasetSource[],
  previewDataset: ReportWriterPreviewDatasetProfile
): ReportWriterDatasetSource | null {
  if (!isReportWriterDatasetSourceProfile(previewDataset)) {
    return null;
  }

  const sourceId = previewDataset.slice("source:".length);
  return datasetSources.find((source) => source.sourceId === sourceId) ?? null;
}

function buildReportWriterDatasetSourceProfile(sourceId: string): `source:${string}` {
  return `source:${sourceId}`;
}

function isReportWriterDatasetSourceProfile(value: string | null | undefined): value is `source:${string}` {
  return typeof value === "string" && value.startsWith("source:") && value.length > "source:".length;
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
  settings: ReportWriterDraftSettings,
  customDatasetRows: Record<string, string>[] | null = null
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
  settings: ReportWriterDraftSettings
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
    filters: buildWriterFilters(settings)
  };
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

function buildReportAccessPolicySummary(settings: ReportWriterDraftSettings): string {
  if (settings.accessMode === "CompanyWide") {
    return "Access policy: company-wide report with owner access retained.";
  }

  const principalId = normalizeDraftText(settings.principalId, "browser-workstation");
  if (settings.accessMode === "Private") {
    return `Access policy: user-locked to ${principalId}.`;
  }

  return `Access policy: ${settings.principalKind.toLowerCase()} ${principalId}.`;
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

function parseReportWriterTopN(value: string | number | null | undefined): number {
  const parsed = Number.parseInt(value?.toString() ?? "", 10);
  if (!Number.isFinite(parsed)) {
    return 10;
  }

  return Math.min(100, Math.max(1, parsed));
}

function normalizeReportWriterTopNText(value: string | null | undefined): string {
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

function ReportingPrivateCapitalReadinessPanel({ activity }: { activity: PrivateCapitalActivityProjection | null }) {
  const fundEventRecords = activity?.fundEventRecords ?? [];
  const subledgers = activity?.capitalAccountSubledgers ?? [];
  const ledgerImpacts = activity?.ledgerImpacts ?? [];
  const reportOutputs = activity?.reportOutputs ?? [];

  return (
    <section role="region" aria-label="Private-capital report readiness">
      <Card className="panel-surface">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <div className="eyebrow-label">Private capital</div>
              <CardTitle>Fund event ledger and capital account subledger</CardTitle>
              <CardDescription>
                Read-only report readiness from the Accounting manual journal workbench private-capital activity projection.
              </CardDescription>
            </div>
            <Badge variant={activity ? fundEventRecords.length > 0 ? "success" : "outline" : "warning"} dot>
              {activity ? "Source projection" : "Not loaded"}
            </Badge>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {activity ? (
            <>
              <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-5">
                <ReportingPrivateCapitalMetric
                  label="Fund events"
                  value={activity.fundEventCount.toLocaleString()}
                  detail={`${activity.postedFundEventCount.toLocaleString()} posted / ${activity.submittedFundEventCount.toLocaleString()} submitted`}
                />
                <ReportingPrivateCapitalMetric
                  label="Capital accounts"
                  value={activity.capitalAccountCount.toLocaleString()}
                  detail={`${activity.approvalQueueCount.toLocaleString()} approval queue`}
                />
                <ReportingPrivateCapitalMetric
                  label="Ledger impacts"
                  value={ledgerImpacts.length.toLocaleString()}
                  detail={`${ledgerImpacts.filter((impact) => impact.isPostingReady).length.toLocaleString()} posting-ready`}
                />
                <ReportingPrivateCapitalMetric
                  label="Report outputs"
                  value={reportOutputs.length.toLocaleString()}
                  detail={`${reportOutputs.filter((output) => output.isReportReady).length.toLocaleString()} report-ready / ${activity.publishedReportOutputCount.toLocaleString()} published`}
                />
                <ReportingPrivateCapitalMetric
                  label="Net activity"
                  value={formatReportingMoney(activity.netCapitalActivity, activity.currency)}
                  detail={`Projected ${activity.projectedAtUtc}`}
                />
              </div>

              {activity.validationIssues.length > 0 ? (
                <div role="status" aria-label="Private-capital projection warnings" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                  <p>{activity.validationIssues.length.toLocaleString()} projection issue{activity.validationIssues.length === 1 ? "" : "s"} retained with the shared activity payload.</p>
                  <ul className="mt-2 space-y-1 text-xs">
                    {activity.validationIssues.slice(0, 3).map((issue, index) => (
                      <li key={`${issue.code}-${index}`}>{issue.code}: {issue.message}</li>
                    ))}
                  </ul>
                </div>
              ) : null}

              {fundEventRecords.length > 0 ? (
                <div className="overflow-x-auto rounded-md border border-border/70">
                  <table className="w-full min-w-[1120px] text-sm" aria-label="Private-capital report-ready fund event records">
                    <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                      <tr>
                        <th className="px-3 py-2 text-left">Fund event</th>
                        <th className="px-3 py-2 text-left">Approval and readiness</th>
                        <th className="px-3 py-2 text-left">Report state</th>
                        <th className="px-3 py-2 text-left">Evidence categories</th>
                        <th className="px-3 py-2 text-left">Ledger impact</th>
                        <th className="px-3 py-2 text-left">Capital account</th>
                        <th className="px-3 py-2 text-left">Routes</th>
                      </tr>
                    </thead>
                    <tbody>
                      {fundEventRecords.map((record) => (
                        <ReportingPrivateCapitalFundEventRecordRow key={record.fundEventRecordId} activity={activity} record={record} />
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : (
                <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                  No private-capital fund event ledger records were included in the shared workbench projection.
                </p>
              )}

              <div className="grid gap-4 xl:grid-cols-[1fr_0.9fr]">
                <div className="overflow-x-auto rounded-md border border-border/70">
                  <table className="w-full min-w-[760px] text-sm" aria-label="Private-capital capital account subledger references">
                    <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                      <tr>
                        <th className="px-3 py-2 text-left">Subledger</th>
                        <th className="px-3 py-2 text-left">Roll-forward</th>
                        <th className="px-3 py-2 text-left">Event counts</th>
                        <th className="px-3 py-2 text-left">Evidence</th>
                      </tr>
                    </thead>
                    <tbody>
                      {subledgers.length > 0 ? subledgers.map((subledger) => (
                        <ReportingPrivateCapitalSubledgerRow key={subledger.subledgerId} subledger={subledger} />
                      )) : (
                        <tr>
                          <td colSpan={4} className="px-3 py-3 text-sm text-muted-foreground">
                            No account-level capital-account subledger references were included.
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </div>

                <div className="space-y-3">
                  <ReportingPrivateCapitalLedgerImpactList impacts={ledgerImpacts} />
                  <ReportingPrivateCapitalReportOutputList outputs={reportOutputs} />
                </div>
              </div>
            </>
          ) : (
            <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
              The loaded Reporting workspace payload does not include manualJournalWorkbench.privateCapitalActivity, so private-capital report readiness is not shown from local fallback data.
            </p>
          )}
        </CardContent>
      </Card>
    </section>
  );
}

function ReportingPrivateCapitalFundEventRecordRow({
  activity,
  record
}: {
  activity: PrivateCapitalActivityProjection;
  record: PrivateCapitalFundEventLedgerRecord;
}) {
  const commandCenterRoute = buildReportingFundEventCommandCenterRoute(activity, record);

  return (
    <tr className="border-t border-border/60 bg-background/30 align-top">
      <td className="px-3 py-2">
        <div className="font-semibold text-foreground">{record.fundEventType || record.fundEventId}</div>
        <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{record.fundEventRecordId}</div>
        <div className="mt-1 text-xs text-muted-foreground">{record.memo || record.journalEntryId}</div>
        <div className="mt-1 font-mono text-[11px] text-muted-foreground">{record.effectiveDate}</div>
      </td>
      <td className="px-3 py-2">
        <div className="flex flex-wrap gap-1.5">
          <Badge variant={privateCapitalApprovalVariant(record.approvalState)} dot>{record.approvalState}</Badge>
          <Badge variant={privateCapitalReadinessVariant(record.readiness)} dot>{record.readinessLabel || record.readiness}</Badge>
          {record.isPosted ? <Badge variant="success">Posted</Badge> : <Badge variant="outline">Unposted</Badge>}
        </div>
        <p className="mt-2 text-xs leading-5 text-muted-foreground">{record.readinessReason || "No readiness reason retained."}</p>
        <p className="mt-1 text-xs text-muted-foreground">{record.nextAction || "No next action retained."}</p>
      </td>
      <td className="px-3 py-2">
        <div className="flex flex-wrap gap-1.5">
          <Badge variant={record.isReportReady ? "success" : "warning"}>{record.isReportReady ? "Report-ready" : "Not report-ready"}</Badge>
          <Badge variant={record.isPublished ? "success" : "outline"}>{record.isPublished ? "Published" : "Not published"}</Badge>
        </div>
        <p className="mt-2 text-xs text-muted-foreground">
          {record.reportOutputCount.toLocaleString()} output{record.reportOutputCount === 1 ? "" : "s"} / {record.reportLineProvenanceCount.toLocaleString()} provenance line{record.reportLineProvenanceCount === 1 ? "" : "s"}
        </p>
        <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">
          {record.primaryReportOutputType ?? record.reportWorkflowState ?? record.publicationManifestId ?? "No primary report output"}
        </p>
      </td>
      <td className="px-3 py-2">
        <div className="text-xs text-muted-foreground">{record.evidenceLinkCount.toLocaleString()} retained evidence link{record.evidenceLinkCount === 1 ? "" : "s"}</div>
        <ReportingPrivateCapitalEvidenceCategories categories={record.evidenceCategories ?? []} />
      </td>
      <td className="px-3 py-2 text-xs">
        <div>{record.ledgerImpactCount.toLocaleString()} ledger impact{record.ledgerImpactCount === 1 ? "" : "s"}</div>
        <div className="mt-1">{record.isPostingReady ? "Posting-ready" : "Posting review"}</div>
        <div className="mt-1 font-mono text-muted-foreground">{formatReportingMoney(record.grossAmount, record.currency)} gross</div>
      </td>
      <td className="px-3 py-2 text-xs">
        <div className="break-all font-mono text-foreground">{record.capitalAccountId}</div>
        <div className="mt-1 break-all text-muted-foreground">{record.investorId ?? "Investor not assigned"}</div>
        <div className="mt-1 font-mono text-muted-foreground">
          {formatReportingMoney(record.capitalAccountOpeningNetActivity, record.currency)} to {formatReportingMoney(record.capitalAccountEndingNetActivity, record.currency)}
        </div>
        <div className="mt-1">{record.capitalAccountSubledgerEntryCount.toLocaleString()} subledger movement{record.capitalAccountSubledgerEntryCount === 1 ? "" : "s"}</div>
      </td>
      <td className="px-3 py-2 text-[11px]">
        <ReportingPrivateCapitalRouteLink label="Command center" href={commandCenterRoute} />
        <ReportingPrivateCapitalRouteLink label="Activity" href={record.activityRoute} />
        <ReportingPrivateCapitalRouteLink label="Evidence" href={record.evidenceRoute} />
        <ReportingPrivateCapitalRouteLink label="Approval" href={record.approvalRoute ?? null} />
        <ReportingPrivateCapitalRouteLink label="Report" href={record.primaryReportRoute ?? record.retainedManifestPath ?? null} />
      </td>
    </tr>
  );
}

function buildReportingFundEventCommandCenterRoute(
  activity: PrivateCapitalActivityProjection,
  record: PrivateCapitalFundEventLedgerRecord
): string {
  const params = new URLSearchParams();
  if (activity.fundProfileId?.trim()) {
    params.set("fundProfileId", activity.fundProfileId.trim());
  }

  if (activity.ledgerBookId?.trim() && isGuid(activity.ledgerBookId.trim())) {
    params.set("ledgerBookId", activity.ledgerBookId.trim());
  }

  params.set("fundEventId", record.fundEventId);
  return `${WORKSTATION_API_ENDPOINTS.privateCapitalFundEventCommandCenter}?${params.toString()}`;
}

function isGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

function ReportingPrivateCapitalSubledgerRow({ subledger }: { subledger: PrivateCapitalCapitalAccountSubledger }) {
  return (
    <tr className="border-t border-border/60 bg-background/30 align-top">
      <td className="px-3 py-2">
        <div className="break-all font-mono text-xs text-foreground">{subledger.capitalAccountId}</div>
        <div className="mt-1 break-all text-[11px] text-muted-foreground">{subledger.investorId ?? "Investor not assigned"}</div>
        <ReportingPrivateCapitalRouteLink label="Subledger" href={subledger.activityRoute} />
      </td>
      <td className="px-3 py-2 font-mono text-xs">
        <div>{formatReportingMoney(subledger.openingNetActivity, subledger.currency)} opening</div>
        <div className="mt-1">{formatReportingMoney(subledger.netCapitalActivity, subledger.currency)} net</div>
        <div className="mt-1">{formatReportingMoney(subledger.endingNetActivity, subledger.currency)} ending</div>
      </td>
      <td className="px-3 py-2 text-xs">
        <div>{subledger.fundEventCount.toLocaleString()} fund event{subledger.fundEventCount === 1 ? "" : "s"}</div>
        <div className="mt-1">{subledger.postedFundEventCount.toLocaleString()} posted</div>
        <div className="mt-1">{subledger.approvalQueueCount.toLocaleString()} approval queue</div>
        <div className="mt-1">{subledger.publishedReportOutputCount.toLocaleString()} published report output{subledger.publishedReportOutputCount === 1 ? "" : "s"}</div>
      </td>
      <td className="px-3 py-2 text-xs">
        <Badge variant={privateCapitalReadinessVariant(subledger.readiness ?? "EvidenceMissing")} dot>
          {subledger.readinessLabel || subledger.readiness || "Evidence missing"}
        </Badge>
        <div className="mt-1 text-[11px] text-muted-foreground">{subledger.readinessReason || "No subledger readiness reason"}</div>
        <ReportingPrivateCapitalRouteLink label={subledger.nextAction || "Next action"} href={subledger.nextActionRoute ?? subledger.activityRoute} />
        <div>{subledger.evidenceLinkCount.toLocaleString()} retained evidence link{subledger.evidenceLinkCount === 1 ? "" : "s"}</div>
        <div className="mt-1">{subledger.validationIssueCount.toLocaleString()} validation issue{subledger.validationIssueCount === 1 ? "" : "s"}</div>
        <ReportingPrivateCapitalEvidenceCategories categories={subledger.evidenceCategories ?? []} />
      </td>
    </tr>
  );
}

function ReportingPrivateCapitalLedgerImpactList({ impacts }: { impacts: PrivateCapitalLedgerImpact[] }) {
  return (
    <div className="rounded-md border border-border/70 bg-background/30 px-3 py-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <h4 className="text-sm font-semibold text-foreground">Ledger impacts</h4>
        <Badge variant="outline">{impacts.length.toLocaleString()}</Badge>
      </div>
      {impacts.length > 0 ? (
        <div role="list" aria-label="Private-capital ledger impacts" className="mt-3 space-y-2">
          {impacts.slice(0, 4).map((impact) => (
            <div key={impact.ledgerImpactId} role="listitem" className="rounded border border-border/60 bg-secondary/20 px-2.5 py-2 text-xs">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <span className="min-w-0">
                  <span className="block font-semibold text-foreground">{impact.fundEventType || impact.fundEventId}</span>
                  <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{impact.journalEntryId}</span>
                </span>
                <Badge variant={impact.isPostingReady ? "success" : impact.isBalanced ? "warning" : "danger"} dot>
                  {impact.isPostingReady ? "Posting-ready" : impact.isBalanced ? "Balanced review" : "Unbalanced"}
                </Badge>
              </div>
              <div className="mt-2 grid grid-cols-2 gap-2 font-mono text-[11px] text-muted-foreground">
                <span>Debits {formatReportingMoney(impact.totalDebits, impact.currency)}</span>
                <span>Credits {formatReportingMoney(impact.totalCredits, impact.currency)}</span>
                <span>Imbalance {formatReportingMoney(impact.imbalance, impact.currency)}</span>
                <span>{impact.lineCount.toLocaleString()} GL lines</span>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <p role="status" className="mt-3 text-sm text-muted-foreground">No retained ledger-impact rows were included.</p>
      )}
    </div>
  );
}

function ReportingPrivateCapitalReportOutputList({ outputs }: { outputs: PrivateCapitalReportOutput[] }) {
  return (
    <div className="rounded-md border border-border/70 bg-background/30 px-3 py-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <h4 className="text-sm font-semibold text-foreground">Report outputs</h4>
        <Badge variant="outline">{outputs.length.toLocaleString()}</Badge>
      </div>
      {outputs.length > 0 ? (
        <div role="list" aria-label="Private-capital report outputs" className="mt-3 space-y-2">
          {outputs.slice(0, 4).map((output) => (
            <div key={output.reportOutputId} role="listitem" className="rounded border border-border/60 bg-secondary/20 px-2.5 py-2 text-xs">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <span className="min-w-0">
                  <span className="block font-semibold text-foreground">{output.displayName || output.reportOutputType}</span>
                  <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{output.reportOutputId}</span>
                </span>
                <span className="flex flex-wrap justify-end gap-1.5">
                  <Badge variant={output.isReportReady ? "success" : "warning"}>{output.readinessLabel || (output.isReportReady ? "Report-ready" : "Review")}</Badge>
                  <Badge variant={output.isPublished ? "success" : "outline"}>{output.isPublished ? "Published" : "Unpublished"}</Badge>
                </span>
              </div>
              <p className="mt-2 text-[11px] text-muted-foreground">{output.readinessReason || "No report-output readiness reason"}</p>
              <p className="mt-2 font-mono text-[11px] text-muted-foreground">
                {formatReportingMoney(output.netCapitalActivity, output.currency)} / {output.approvalState} / {output.reportWorkflowState ?? "No workflow state"}
              </p>
              <p className="mt-1 text-[11px] text-muted-foreground">
                {output.evidenceLinkCount.toLocaleString()} evidence link{output.evidenceLinkCount === 1 ? "" : "s"} / {(output.reportLineProvenanceCount ?? 0).toLocaleString()} provenance line{output.reportLineProvenanceCount === 1 ? "" : "s"}
              </p>
              <ReportingPrivateCapitalRouteLink label={output.nextAction || "Output"} href={output.nextActionRoute ?? output.reportOutputRoute ?? output.reportRoute} />
            </div>
          ))}
        </div>
      ) : (
        <p role="status" className="mt-3 text-sm text-muted-foreground">No report-output rows were included.</p>
      )}
    </div>
  );
}

function ReportingPrivateCapitalEvidenceCategories({ categories }: { categories: PrivateCapitalEvidenceCategory[] }) {
  if (categories.length === 0) {
    return <div className="mt-2 text-[11px] text-muted-foreground">No evidence categories retained.</div>;
  }

  return (
    <div className="mt-2 space-y-1" aria-label="Private-capital retained evidence categories">
      {categories.map((category) => (
        <div key={category.categoryId} className="rounded-sm border border-border/60 bg-secondary/20 px-2 py-1">
          <div className="flex flex-wrap items-center gap-1.5">
            <Badge variant={category.isReady ? "success" : "warning"} dot>{category.label || category.categoryId}</Badge>
            <span className="font-mono text-[11px] text-muted-foreground">{category.evidenceLinkCount.toLocaleString()} evidence</span>
          </div>
          <p className="mt-1 text-[11px] leading-5 text-muted-foreground">{category.summary || "No evidence summary retained."}</p>
          {(category.requiredEvidence ?? []).length > 0 ? (
            <p className="mt-1 text-[11px] leading-5 text-muted-foreground">
              Required: {(category.requiredEvidence ?? []).join(", ")}
            </p>
          ) : null}
        </div>
      ))}
    </div>
  );
}

function ReportingPrivateCapitalMetric({ label, value, detail }: { label: string; value: string; detail: string }) {
  return (
    <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
      <div className="text-[11px] font-semibold uppercase text-muted-foreground">{label}</div>
      <div className="mt-1 font-mono text-base text-foreground">{value}</div>
      <div className="mt-1 text-xs leading-5 text-muted-foreground">{detail}</div>
    </div>
  );
}

function ReportingPrivateCapitalRouteLink({ label, href }: { label: string; href: string | null | undefined }) {
  if (!href) {
    return <div className="mt-1 text-[11px] text-muted-foreground">{label}: none retained</div>;
  }

  return (
    <a className="mt-1 block break-all font-mono text-primary underline-offset-2 hover:underline" href={href}>
      {label}: {href}
    </a>
  );
}

function privateCapitalApprovalVariant(status: ManualJournalEntryStatus): "outline" | "success" | "warning" | "danger" {
  if (status === "Approved") {
    return "success";
  }

  if (status === "Submitted") {
    return "warning";
  }

  if (status === "NeedsFix" || status === "Rejected") {
    return "danger";
  }

  return "outline";
}

function privateCapitalReadinessVariant(readiness: PrivateCapitalFundEventLedgerRecord["readiness"]): "outline" | "success" | "warning" | "danger" {
  if (readiness === "Published" || readiness === "Ready") {
    return "success";
  }

  if (readiness === "Blocked") {
    return "danger";
  }

  return readiness ? "warning" : "outline";
}

function resolveReportingPrivateCapitalActivity(data: AccountingWorkspaceResponse | null): PrivateCapitalActivityProjection | null {
  return data?.manualJournalWorkbench?.privateCapitalActivity ?? null;
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

function formatFinancialRecordExplorerName(explorerId: string | null | undefined): string {
  if (explorerId === "portfolio") {
    return "Portfolio Explorer";
  }

  if (explorerId === "security-instrument") {
    return "Security & Instrument Explorer";
  }

  return "Ledger Explorer";
}

function hasReportingRunMetadata(reportPackage: ReportPackDeliveryPackage): boolean {
  return Boolean(
    reportPackage.reportingRunAsOfDate ||
    reportPackage.reportingRunStatus ||
    reportPackage.reportingRunTrigger ||
    reportPackage.reportingRunAttemptCount != null ||
    reportPackage.reportingRunSectionCount != null ||
    reportPackage.reportingRunLineageLinkedSections != null
  );
}

function formatReportingRunPackageMetadata(reportPackage: ReportPackDeliveryPackage): string {
  return [
    reportPackage.reportingRunAsOfDate ? `asOf=${reportPackage.reportingRunAsOfDate}` : null,
    reportPackage.reportingRunStatus ? `status=${reportPackage.reportingRunStatus}` : null,
    reportPackage.reportingRunTrigger ? `trigger=${reportPackage.reportingRunTrigger}` : null,
    reportPackage.reportingRunAttemptCount != null ? `attempt=${reportPackage.reportingRunAttemptCount.toLocaleString()}` : null,
    reportPackage.reportingRunSectionCount != null ? `sections=${reportPackage.reportingRunSectionCount.toLocaleString()}` : null,
    reportPackage.reportingRunLineageLinkedSections != null ? `lineage=${reportPackage.reportingRunLineageLinkedSections.toLocaleString()}` : null
  ].filter((part): part is string => Boolean(part)).join(" · ");
}

function hasReportWriterDatasetMetadata(reportPackage: ReportPackDeliveryPackage): boolean {
  return Boolean(
    reportPackage.reportWriterDatasetSourceLabel ||
    reportPackage.reportWriterDatasetSourceId ||
    reportPackage.reportWriterDatasetRowCount != null
  );
}

function formatReportWriterDatasetMetadata(reportPackage: ReportPackDeliveryPackage): string {
  const source = reportPackage.reportWriterDatasetSourceLabel?.trim() ||
    reportPackage.reportWriterDatasetSourceId?.trim() ||
    "Report-writer dataset";
  return reportPackage.reportWriterDatasetRowCount == null
    ? source
    : `${source} (${reportPackage.reportWriterDatasetRowCount.toLocaleString()} row${reportPackage.reportWriterDatasetRowCount === 1 ? "" : "s"})`;
}

function reportPackDeliveryEvidencePath(reportId: string, attemptId: string): string {
  return evidenceWorkbenchPath("report-pack-delivery", `${reportId}:${attemptId}`);
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
    <StatusBanner
      role="status"
      aria-label={`${status.label} status`}
      tone={status.state === "success" ? "success" : status.state === "error" ? "warning" : "info"}
      title={status.message}
      detail={status.details.length > 0 ? (
        <ul className="mt-2 space-y-1 text-xs">
          {status.details.map((detail) => (
            <li key={detail}>{detail}</li>
          ))}
        </ul>
      ) : null}
    />
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

function buildReportPackDeliveryFailureRequest(attempt: ReportPackDeliveryAttempt): ReportPackDeliveryFailureRequest {
  return {
    distributionId: attempt.distributionId,
    actor: "browser-workstation",
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
