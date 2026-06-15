import { useEffect, useRef, useState } from "react";
import { runAnalysisExport } from "@/lib/api";
import type { ApiRequestOptions } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import {
  EXPORT_API_ENDPOINTS,
  exportPreviewEndpoint,
  reportPackEvidenceBundleEndpoint,
  reportingRunReportWriterGridEndpoint
} from "@/lib/workstation-endpoints";
import { evidenceWorkbenchPath, WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { getReportPackDistributions } from "@/lib/reporting-distributions";
import type { ExportAnalysisResult, GovernanceReportingProfile, GovernanceReportingSummary, ReportPackDeliveryAccessLink, ReportWriterAggregateFunction, ReportWriterDatasetSource, ReportWriterFilterOperator, ReportingScheduleDeliveryPlan, ReportingScheduleDeliveryTarget, ReportingRunStatusProjection, ReportingScheduleRecord, ReportingTemplateGridMetadata, ReportingTemplateMetadata, ReportingWorkflowChangedLine, ReportingWorkflowEvidenceLink, ReportingWorkflowLineProvenance, ReportingWorkflowRecord } from "@/types";

export type ReportingProfileBadgeTone = "primary" | "success" | "warning" | "muted";
export type ReportingBadgeVariant = "default" | "success" | "warning" | "outline";
export type ReportingWorkflowTone = "success" | "warning" | "muted";
export type ReportingDetailFieldTone = "default" | "success" | "warning" | "muted";
export type ReportingExportStatusTone = "default" | "success" | "danger";
export type ReportingFieldClassName = "text-foreground" | "text-success" | "text-warning" | "text-muted-foreground";
export type ReportingProfileKeyCommand = "next" | "previous" | "first" | "last";
export type ReportingProfileActionSurface = "profile-detail" | "report-pack-task";
export type ReportingExportStatusClassName =
  | "border-border/70 bg-secondary/25 text-muted-foreground"
  | "border-success/30 bg-success/10 text-success"
  | "border-danger/35 bg-danger/10 text-danger";

const formatReportPackDistributionDue = (dueAtUtc: string | null): string => {
  if (!dueAtUtc) {
    return "No due date";
  }

  const due = new Date(dueAtUtc);
  if (Number.isNaN(due.getTime())) {
    return dueAtUtc;
  }

  return due.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit"
  });
};

export interface ReportingProfileBadge {
  label: string;
  tone: ReportingProfileBadgeTone;
  variant: ReportingBadgeVariant;
}

export interface ReportingProfileRow {
  id: string;
  name: string;
  targetLabel: string;
  formatLabel: string;
  description: string;
  isSelected: boolean;
  isExpanded: boolean;
  isRecommended: boolean;
  controlsId: string;
  badges: ReportingProfileBadge[];
  selectAriaLabel: string;
}

export interface ReportingProfileAction {
  id: "preview" | "run";
  label: string;
  href: string;
  variant: "default" | "outline";
  ariaLabel: string;
  describedById: string;
  statusText: string;
  descriptionText: string;
  statusBadgeLabel: string;
  statusBadgeAriaLabel: string;
  statusBadgeVariant: ReportingBadgeVariant;
  isDisabled: boolean;
  disabledReason: string | null;
  busyLabel: string | null;
  method: "GET" | "POST";
  profileId: string;
  isRunning: boolean;
}

export interface ReportingDetailField {
  label: string;
  value: string;
  tone: ReportingDetailFieldTone;
  className: ReportingFieldClassName;
}

export interface ReportingDetailViewModel {
  id: string;
  title: string;
  subtitle: string;
  description: string;
  fields: ReportingDetailField[];
  readinessSummary: string;
  actions: ReportingProfileAction[];
}

export interface ReportingExportStatusState {
  text: string;
  tone: ReportingExportStatusTone;
  className: ReportingExportStatusClassName;
  ariaLabel: string;
  fields: ReportingDetailField[];
  warnings: string[];
  artifacts: ReportingDetailField[];
}

export interface ReportingExportServices {
  runExport: (profileId: string, options?: ApiRequestOptions) => Promise<ExportAnalysisResult>;
}

export interface ReportingPackTargetRow {
  id: string;
  label: string;
  recipientRole: string;
  channel: string;
  stateLabel: string;
  pendingItemsLabel: string;
  pendingSummary: string;
  ownerLabel: string;
  dueLabel: string;
  lastSentLabel: string;
  href: string;
  ariaLabel: string;
}

export interface ReportingPackTargetsEmptyState {
  text: string;
  ariaLabel: string;
}

export interface ReportingChipViewModel {
  label: string;
  value: string;
}

export interface ReportingAccessAuditCountRow {
  id: string;
  label: string;
  visibleLabel: string;
  hiddenLabel: string;
  hasHidden: boolean;
}

export interface ReportingAccessAuditViewModel {
  evaluationScope: string;
  summary: string;
  principalScopes: string[];
  scopeLabel: string;
  hiddenTotalLabel: string;
  postureLabel: string;
  postureVariant: ReportingBadgeVariant;
  countRows: ReportingAccessAuditCountRow[];
  denialReasons: string[];
  hasDenialReasons: boolean;
  ariaLabel: string;
}

export interface ReportingTemplateRow {
  id: string;
  templateName: string;
  name: string;
  family: string;
  version: string;
  versionNumber: number;
  sectionSummary: string;
  statusLabel: string;
  statusVariant: Exclude<ReportingBadgeVariant, "default">;
  sourceLabel: string;
  approvalSummary: string;
  accessMode: string;
  accessSummary: string;
  isAccessible: boolean;
  accessGovernance: ReportingTemplateAccessGovernanceRow;
  latestApprovedLabel: string;
  versionLineageSummary: string;
  auditTrailSummary: string;
  lastAuditSummary: string;
  decisionSummary: string;
  validationSummary: string;
  authoringHref: string;
  actionLabel: string;
  actionAriaLabel: string;
  canRunOnDemand: boolean;
  runActionLabel: string;
  runActionAriaLabel: string;
  runDisabledReason: string | null;
  lifecycleActions: ReportingTemplateLifecycleActionRow[];
  hasLifecycleActions: boolean;
  writerGrids: ReportingWriterGridRow[];
  hasWriterGrids: boolean;
  writerGridSummary: string;
}

export interface ReportingTemplateAccessGovernanceRow {
  modeLabel: string;
  scopeLabel: string;
  postureLabel: string;
  postureVariant: ReportingBadgeVariant;
  detail: string;
  ariaLabel: string;
}

export type ReportingTemplateLifecycleActionKind = "submit" | "approve" | "reject";

export interface ReportingTemplateLifecycleActionRow {
  id: string;
  kind: ReportingTemplateLifecycleActionKind;
  label: string;
  ariaLabel: string;
  targetStatus: string;
  isEnabled: boolean;
  disabledReason: string | null;
}

export type ReportingWriterTokenKind = "field" | "metric" | "formula";

export interface ReportingWriterToken {
  id: string;
  label: string;
  detail: string;
  kind: ReportingWriterTokenKind;
  fieldName?: string | null;
  sourceField?: string | null;
  function?: ReportWriterAggregateFunction | null;
  expression?: string | null;
  name?: string | null;
  dataType?: string | null;
  dataset?: string | null;
  role?: string | null;
}

export interface ReportingWriterFilterRow {
  id: string;
  field: string;
  operator: ReportWriterFilterOperator | string;
  value: string | null;
  label: string;
  summary: string;
}

export interface ReportingWriterGridRow {
  id: string;
  gridId: string;
  templateId: string;
  templateVersion: string;
  family: string;
  title: string;
  kind: string;
  topN: number | null;
  sortBy: string | null;
  sortDescending: boolean;
  summary: string;
  sourceFields: ReportingWriterToken[];
  rowFields: ReportingWriterToken[];
  columnFields: ReportingWriterToken[];
  metrics: ReportingWriterToken[];
  formulas: ReportingWriterToken[];
  filters: ReportingWriterFilterRow[];
  topNLabel: string;
  sortLabel: string;
  filterSummary: string;
  ariaLabel: string;
}

export interface ReportingRunStatusRow {
  id: string;
  templateId: string;
  family: string;
  status: string;
  trigger: string;
  runIdLabel: string;
  templateLabel: string;
  asOfDateLabel: string;
  attemptLabel: string;
  sectionLabel: string;
  lineageLabel: string;
  artifactLabel: string;
  artifactNames: string[];
  hasArtifacts: boolean;
  generatedGridLabel: string;
  generatedGridNames: string[];
  generatedGridArtifacts: ReportingGeneratedGridArtifactRow[];
  hasGeneratedGrids: boolean;
  datasetSourceLabel: string;
  lineageSummary: string;
  auditSummary: string;
  failureReason: string | null;
  drilldownLinks: ReportingRunLinkRow[];
  nextActions: ReportingRunActionRow[];
  hasDrilldownLinks: boolean;
  hasNextActions: boolean;
}

export interface ReportingGeneratedGridArtifactRow {
  id: string;
  label: string;
  jsonHref: string;
  csvHref: string;
  xlsHref: string;
  xlsxHref: string;
}

export interface ReportingRunLinkRow {
  id: string;
  kind: string;
  label: string;
  href: string;
  method: string;
  source: string;
  isBrowserNavigable: boolean;
  interactionLabel: "Open" | "Reference";
  ariaLabel: string;
}

export interface ReportingRunActionRow {
  id: string;
  kind: string;
  label: string;
  href: string;
  method: string;
  isEnabled: boolean;
  disabledReason: string | null;
  isBrowserNavigable: boolean;
  interactionLabel: "Open" | "Reference";
  ariaLabel: string;
}

export interface ReportingScheduleRow {
  id: string;
  templateId: string;
  state: string;
  stateVariant: Exclude<ReportingBadgeVariant, "default">;
  cronLabel: string;
  dueLabel: string;
  nextAsOfLabel: string;
  lastRunLabel: string;
  runCountLabel: string;
  description: string;
  deliveryTargetLabel: string;
  datasetSourceLabel: string;
  canPause: boolean;
  canResume: boolean;
  ariaLabel: string;
}

export interface ReportingScheduleDeliveryPlanRow {
  id: string;
  scheduleId: string;
  templateId: string;
  distributionId: string;
  recipient: string;
  recipientRole: string;
  channel: string;
  deliveryMode: string;
  formatsLabel: string;
  readinessSummary: string;
  readinessVariant: Exclude<ReportingBadgeVariant, "default">;
  dueLabel: string;
  nextAsOfLabel: string;
  ownerLabel: string;
  route: string;
  note: string | null;
  lastDeliveryLabel: string;
  lastDeliveryHref: string | null;
  lastDeliveryLinks: ReportingDeliveryAccessLinkRow[];
  integrityLabel: string;
  integritySummary: string | null;
  brandingLabel: string;
  versionStamp: string | null;
  ariaLabel: string;
}

export interface ReportingDeliveryAccessLinkRow {
  id: string;
  kind: string;
  label: string;
  href: string;
  tokenLabel: string;
  expiresLabel: string | null;
  description: string | null;
  ariaLabel: string;
}

export interface ReportingWorkbenchAction {
  id: "evidence";
  label: string;
  href: string;
  ariaLabel: string;
}

export interface ReportingWorkflowProfileRow {
  id: string;
  name: string;
  summary: string;
  readinessLabel: string;
  readinessTone: ReportingWorkflowTone;
  readinessVariant: Exclude<ReportingBadgeVariant, "default">;
  isSelected: boolean;
  isExpanded: boolean;
  controlsId: string;
  descriptionId: string;
  tabIndex: 0 | -1;
  selectAriaLabel: string;
}

export interface ReportingWorkflowBackendLink {
  id: string;
  method: "GET" | "POST";
  label: string;
  href: string;
  ariaLabel: string;
  isBrowserNavigable: boolean;
  interactionLabel: "Open" | "Reference";
}

export interface ReportingRestatementChangedLineRow {
  id: string;
  lineKey: string;
  valueBridge: string;
  evidenceLabel: string;
  evidenceHref: string | null;
  ariaLabel: string;
}

export interface ReportingRestatementReviewPanel {
  regionLabel: string;
  title: string;
  description: string;
  statusLabel: string;
  statusVariant: Exclude<ReportingBadgeVariant, "default">;
  summaryText: string;
  fields: ReportingDetailField[];
  changedLinesLabel: string;
  changedLines: ReportingRestatementChangedLineRow[];
  hasChangedLines: boolean;
  evidenceSummary: string;
  emptyText: string;
}

export interface ReportingLineProvenanceRow {
  id: string;
  lineKey: string;
  sourceLabel: string;
  valueLabel: string;
  evidenceLabel: string;
  evidenceHref: string | null;
  financialRecordLabel: string;
  financialRecordHref: string | null;
  ariaLabel: string;
}

export interface ReportingPublicationEvidenceRow {
  id: string;
  label: string;
  sourceLabel: string;
  capturedLabel: string;
  href: string | null;
  ariaLabel: string;
}

export interface ReportingPublicationReviewPanel {
  regionLabel: string;
  title: string;
  description: string;
  statusLabel: string;
  statusVariant: Exclude<ReportingBadgeVariant, "default">;
  summaryText: string;
  fields: ReportingDetailField[];
  evidenceSummary: string;
  evidenceLinksLabel: string;
  evidenceLinks: ReportingPublicationEvidenceRow[];
  hasEvidenceLinks: boolean;
  evidenceLinksEmptyText: string;
  lineProvenanceLabel: string;
  lineProvenanceRows: ReportingLineProvenanceRow[];
  hasLineProvenance: boolean;
  lineProvenanceEmptyText: string;
}

export interface ReportingWorkflowTaskPanel {
  regionLabel: string;
  eyebrow: string;
  title: string;
  description: string;
  statusLabel: string;
  statusTone: ReportingWorkflowTone;
  statusVariant: Exclude<ReportingBadgeVariant, "default">;
  chips: ReportingChipViewModel[];
  targetsLabel: string;
  targets: ReportingPackTargetRow[];
  hasTargets: boolean;
  targetsEmptyText: string;
  targetsEmptyAriaLabel: string;
  profileListLabel: string;
  profileKeyboardHelpId: string;
  profileKeyboardHelpText: string;
  selectedProfileId: string | null;
  profiles: ReportingWorkflowProfileRow[];
  hasProfiles: boolean;
  profilesEmptyText: string;
  profilesEmptyAriaLabel: string;
  selectedSummary: string;
  selectedSummaryId: string;
  actionListLabel: string;
  actionPanelId: string;
  actions: ReportingProfileAction[];
  hasActions: boolean;
  actionsEmptyText: string;
  actionsEmptyAriaLabel: string;
  backendLinksLabel: string;
  backendPanelId: string;
  backendLinks: ReportingWorkflowBackendLink[];
  publicationReview: ReportingPublicationReviewPanel;
  restatementReview: ReportingRestatementReviewPanel;
}

export interface ReportingLoadingState {
  role: "status";
  ariaBusy: true;
  ariaLive: "polite";
  titleId: string;
  detailId: string;
  title: string;
  detail: string;
  badgeLabel: string;
  routeLabel: string;
}

export interface ReportingScreenViewModel {
  title: string;
  description: string;
  countLabel: string;
  recommendedCountLabel: string;
  packTargetCountLabel: string;
  hasRows: boolean;
  rows: ReportingProfileRow[];
  emptyText: string;
  listLabel: string;
  visibleCountLabel: string;
  workbenchActions: ReportingWorkbenchAction[];
  workbenchChips: ReportingChipViewModel[];
  queueChips: ReportingChipViewModel[];
  packTargetChips: ReportingChipViewModel[];
  accessAudit: ReportingAccessAuditViewModel;
  templateRows: ReportingTemplateRow[];
  runStatusRows: ReportingRunStatusRow[];
  hasRunStatusRows: boolean;
  scheduleRows: ReportingScheduleRow[];
  hasScheduleRows: boolean;
  scheduleSummary: string;
  scheduleListLabel: string;
  scheduleEmptyText: string;
  scheduleDeliveryPlanRows: ReportingScheduleDeliveryPlanRow[];
  hasScheduleDeliveryPlanRows: boolean;
  scheduleDeliveryPlanSummary: string;
  scheduleDeliveryPlanListLabel: string;
  scheduleDeliveryPlanEmptyText: string;
  detailId: string;
  statusTitle: string;
  statusDetail: string;
  statusBadgeLabel: string;
  statusBadgeVariant: "default" | "outline";
  nextAction: string;
  selectedProfile: ReportingDetailViewModel | null;
  packTargets: ReportingPackTargetRow[];
  hasPackTargets: boolean;
  packTargetsSummary: string;
  packTargetsListLabel: string;
  packTargetsEmptyState: ReportingPackTargetsEmptyState;
  workflowTaskPanel: ReportingWorkflowTaskPanel | null;
  loadingState: ReportingLoadingState;
  loadingTitle: string;
  loadingDetail: string;
  exportStatus: ReportingExportStatusState | null;
  runningProfileId: string | null;
  runExport: (profileId: string, profileName: string) => Promise<void>;
  selectProfile: (id: string) => void;
  selectAdjacentReportPackProfile: (direction: ReportingProfileKeyCommand) => void;
}

const defaultReportingExportServices: ReportingExportServices = {
  runExport: (profileId, options) => runAnalysisExport(profileId, options)
};

const REPORT_PACK_PROFILE_ACTIONS_ID = "report-pack-profile-actions";
const REPORT_PACK_PROFILE_BACKEND_ID = "report-pack-profile-backend-links";
const REPORT_PACK_PROFILE_SUMMARY_ID = "report-pack-profile-selected-summary";
const REPORT_PACK_PROFILE_KEYBOARD_HELP_ID = "report-pack-profile-keyboard-help";

export function useReportingScreenViewModel(
  reporting: GovernanceReportingSummary | null,
  services: ReportingExportServices = defaultReportingExportServices,
  pathname: string = WORKSTATION_ROUTE_CATALOG.reporting
): ReportingScreenViewModel {
  const [selectedId, setSelectedId] = useState<string | null | undefined>(undefined);
  const [runningProfileId, setRunningProfileId] = useState<string | null>(null);
  const [exportStatus, setExportStatus] = useState<ReportingExportStatusState | null>(null);
  const exportCommandRevisionRef = useRef(0);
  const exportAbortRef = useRef<AbortController | null>(null);
  const detailId = "reporting-profile-detail";

  useEffect(() => () => {
    exportCommandRevisionRef.current += 1;
    exportAbortRef.current?.abort();
    exportAbortRef.current = null;
  }, []);

  const cancelExportCommand = () => {
    exportCommandRevisionRef.current += 1;
    exportAbortRef.current?.abort();
    exportAbortRef.current = null;
    setRunningProfileId(null);
    setExportStatus(null);
  };

  const selectProfile = (id: string) => {
    cancelExportCommand();
    setSelectedId((prev) => {
      const defaultProfileId = reporting ? defaultReportPackProfileId(reporting, pathname) : null;
      const activeId = prev === undefined ? defaultProfileId : prev;
      return activeId === id ? null : id;
    });
  };

  const selectAdjacentReportPackProfile = (direction: ReportingProfileKeyCommand) => {
    if (!reporting || reporting.profiles.length === 0) {
      return;
    }

    cancelExportCommand();
    setSelectedId((prev) => {
      const defaultProfileId = defaultReportPackProfileId(reporting, pathname);
      const activeId = prev === undefined ? defaultProfileId : prev;
      return adjacentReportPackProfileId(reporting.profiles, activeId, direction);
    });
  };

  const runExportCommand = async (profileId: string, profileName: string) => {
    const commandRevision = exportCommandRevisionRef.current + 1;
    exportCommandRevisionRef.current = commandRevision;
    exportAbortRef.current?.abort();
    const controller = new AbortController();
    exportAbortRef.current = controller;
    setRunningProfileId(profileId);
    setExportStatus(buildExportStatusStarting(profileName));

    try {
      const result = await services.runExport(profileId, { signal: controller.signal });
      if (exportCommandRevisionRef.current !== commandRevision) {
        return;
      }
      setExportStatus(buildExportStatusResult(profileId, profileName, result));
    } catch (error) {
      if (exportCommandRevisionRef.current !== commandRevision || isAbortError(error)) {
        return;
      }
      setExportStatus(buildExportStatusFailure(profileName, error));
    } finally {
      if (exportAbortRef.current === controller) {
        exportAbortRef.current = null;
      }
      if (exportCommandRevisionRef.current === commandRevision) {
        setRunningProfileId(null);
      }
    }
  };

  if (!reporting) {
    return {
      title: "Report packs",
      description: "No reporting data is available.",
      countLabel: "0 profiles",
      recommendedCountLabel: "0",
      packTargetCountLabel: "0",
      hasRows: false,
      rows: [],
      emptyText: "No export or reporting profiles are configured for this workspace.",
      listLabel: "Export profiles",
      visibleCountLabel: "0 visible",
      workbenchActions: buildWorkbenchActions(null),
      workbenchChips: buildWorkbenchChips("0 profiles", "0", "0"),
      queueChips: buildQueueChips("0 visible", "0", "0", "Export profiles"),
      packTargetChips: buildPackTargetChips("0", "No profile selected"),
      accessAudit: buildReportingAccessAudit(null),
      templateRows: [],
      runStatusRows: [],
      hasRunStatusRows: false,
      scheduleRows: [],
      hasScheduleRows: false,
      scheduleSummary: "No reporting schedules configured.",
      scheduleListLabel: "Reporting schedules",
      scheduleEmptyText: "No reporting schedules are configured.",
      scheduleDeliveryPlanRows: [],
      hasScheduleDeliveryPlanRows: false,
      scheduleDeliveryPlanSummary: "No schedule delivery plans configured.",
      scheduleDeliveryPlanListLabel: "Reporting schedule delivery plans",
      scheduleDeliveryPlanEmptyText: "No scheduled delivery targets are configured.",
      detailId,
      statusTitle: "No profile selected",
      statusDetail: "Reporting data is unavailable. Check the Reporting workspace API connection.",
      statusBadgeLabel: "Waiting",
      statusBadgeVariant: "outline",
      nextAction: "—",
      selectedProfile: null,
      packTargets: [],
      hasPackTargets: false,
      packTargetsSummary: "No report-pack recipients configured.",
      packTargetsListLabel: "Report-pack distribution recipients",
      packTargetsEmptyState: buildPackTargetsEmptyState(),
      workflowTaskPanel: null,
      loadingState: buildLoadingState(pathname),
      loadingTitle: "Loading Reporting",
      loadingDetail: "Waiting for governed report-pack and export evidence.",
      exportStatus,
      runningProfileId,
      runExport: runExportCommand,
      selectProfile,
      selectAdjacentReportPackProfile
    };
  }

  const profiles = reporting.profiles;
  const recommended = new Set(reporting.recommendedProfiles);
  const defaultSelectedId = defaultReportPackProfileId(reporting, pathname);
  const effectiveSelectedId = selectedId === undefined ? defaultSelectedId : selectedId;
  const selectedProfileData: GovernanceReportingProfile | null =
    profiles.find((p) => p.id === effectiveSelectedId) ?? null;
  const profileDetailActions = selectedProfileData
    ? buildProfileActions(selectedProfileData, runningProfileId, "profile-detail")
    : [];
  const reportPackTaskActions = selectedProfileData
    ? buildProfileActions(selectedProfileData, runningProfileId, "report-pack-task")
    : [];

  const rows: ReportingProfileRow[] = profiles.map((p) => ({
    id: p.id,
    name: p.name,
    targetLabel: p.targetTool,
    formatLabel: p.format,
    description: p.description,
    isSelected: p.id === effectiveSelectedId,
    isExpanded: p.id === effectiveSelectedId,
    isRecommended: recommended.has(p.id),
    controlsId: detailId,
    selectAriaLabel: `Select ${p.name} export profile`,
    badges: [
      ...(recommended.has(p.id) ? [buildReportingBadge("Recommended", "primary")] : []),
      ...(p.loaderScript ? [buildReportingBadge("Loader", "success")] : []),
      ...(p.dataDictionary ? [buildReportingBadge("Dictionary", "success")] : [])
    ]
  }));

  const detail: ReportingDetailViewModel | null = selectedProfileData
    ? {
        id: selectedProfileData.id,
        title: selectedProfileData.name,
        subtitle: `${selectedProfileData.format} · ${selectedProfileData.targetTool}`,
        description: selectedProfileData.description,
        fields: [
          buildReportingDetailField("Profile ID", selectedProfileData.id, "default"),
          buildReportingDetailField("Format", selectedProfileData.format, "default"),
          buildReportingDetailField("Target", selectedProfileData.targetTool, "default"),
          buildReportingDetailField(
            "Loader script",
            selectedProfileData.loaderScript ? "Included" : "Not included",
            selectedProfileData.loaderScript ? "success" : "muted"
          ),
          buildReportingDetailField(
            "Data dictionary",
            selectedProfileData.dataDictionary ? "Included" : "Not included",
            selectedProfileData.dataDictionary ? "success" : "muted"
          )
        ],
        readinessSummary: buildProfileReadinessSummary(selectedProfileData),
        actions: profileDetailActions
      }
    : null;

  const countLabel = `${profiles.length} profile${profiles.length === 1 ? "" : "s"}`;
  const distributions = getReportPackDistributions(reporting);
  const packCount = distributions.length;
  const pendingDistributionCount = distributions.filter((distribution) => distribution.pendingItems > 0).length;
  const packTargetCountLabel = String(packCount);
  const recommendedCountLabel = String(reporting.recommendedProfiles.length);
  const visibleCountLabel = `${rows.length} of ${profiles.length}`;
  const statusTitle = selectedProfileData ? `${selectedProfileData.name} selected` : "No profile selected";
  const listLabel = "Export profiles";
  const packTargets: ReportingPackTargetRow[] = distributions.map((distribution) => ({
    id: distribution.distributionId,
    label: distribution.recipient,
    recipientRole: distribution.recipientRole,
    channel: distribution.channel,
    stateLabel: distribution.state,
    pendingItemsLabel: `${distribution.pendingItems} pending`,
    pendingSummary: distribution.pendingSummary,
    ownerLabel: distribution.owner,
    dueLabel: formatReportPackDistributionDue(distribution.dueAtUtc),
    lastSentLabel: distribution.lastSentAtUtc ? formatReportPackDistributionDue(distribution.lastSentAtUtc) : "Not sent",
    href: distribution.route,
    ariaLabel: `${distribution.recipient} report-pack distribution: ${distribution.pendingSummary}`
  }));
  const workflowTaskPanel = buildWorkflowTaskPanel({
    reporting,
    rows,
    packTargets,
    selectedProfile: selectedProfileData,
    selectedProfileActions: reportPackTaskActions,
    pathname
  });
  const templateRows = buildTemplateRows(reporting.templates ?? []);
  const runStatusRows = buildRunStatusRows(reporting.recentRuns ?? []);
  const scheduleRows = buildScheduleRows(reporting.schedules ?? [], reporting.reportWriterDatasetSources ?? []);
  const scheduleDeliveryPlanRows = buildScheduleDeliveryPlanRows(reporting.scheduleDeliveryPlans ?? []);
  const accessAudit = buildReportingAccessAudit(reporting);

  return {
    title: "Report packs",
    description: reporting.summary,
    countLabel,
    recommendedCountLabel,
    packTargetCountLabel,
    hasRows: rows.length > 0,
    rows,
    emptyText:
      "No export profiles are configured. Add a governed profile to restore reporting evidence.",
    listLabel,
    visibleCountLabel,
    workbenchActions: buildWorkbenchActions(selectedProfileData),
    workbenchChips: buildWorkbenchChips(countLabel, packTargetCountLabel, recommendedCountLabel),
    queueChips: buildQueueChips(visibleCountLabel, recommendedCountLabel, packTargetCountLabel, listLabel),
    packTargetChips: buildPackTargetChips(packTargetCountLabel, statusTitle),
    accessAudit,
    templateRows,
    runStatusRows,
    hasRunStatusRows: runStatusRows.length > 0,
    scheduleRows,
    hasScheduleRows: scheduleRows.length > 0,
    scheduleSummary: buildScheduleSummary(scheduleRows),
    scheduleListLabel: "Reporting schedules",
    scheduleEmptyText: "No reporting schedules are configured.",
    scheduleDeliveryPlanRows,
    hasScheduleDeliveryPlanRows: scheduleDeliveryPlanRows.length > 0,
    scheduleDeliveryPlanSummary: buildScheduleDeliveryPlanSummary(scheduleDeliveryPlanRows),
    scheduleDeliveryPlanListLabel: "Reporting schedule delivery plans",
    scheduleDeliveryPlanEmptyText: "No scheduled delivery targets are configured.",
    detailId,
    statusTitle,
    statusDetail: selectedProfileData
      ? `${selectedProfileData.name} routes ${selectedProfileData.format} output to ${selectedProfileData.targetTool}.`
      : "Select a profile to inspect export evidence and ready-state.",
    statusBadgeLabel: selectedProfileData ? "Selected" : "Waiting",
    statusBadgeVariant: selectedProfileData ? "default" : "outline",
    nextAction: selectedProfileData
      ? `POST ${EXPORT_API_ENDPOINTS.analysis} · GET ${exportPreviewEndpoint(selectedProfileData.id)}`
      : `${profiles.length} profile${profiles.length === 1 ? "" : "s"} on desk. Select one to inspect export evidence.`,
    selectedProfile: detail,
    packTargets,
    hasPackTargets: packCount > 0,
    packTargetsSummary:
      packCount > 0
        ? `${packCount} report-pack recipient${packCount === 1 ? "" : "s"} configured; ${pendingDistributionCount} pending.`
        : "No report-pack recipients configured.",
    packTargetsListLabel: "Report-pack distribution recipients",
    packTargetsEmptyState: buildPackTargetsEmptyState(),
    workflowTaskPanel,
    loadingState: buildLoadingState(pathname),
    loadingTitle: "Loading Reporting",
    loadingDetail: "Waiting for governed report-pack and export evidence.",
    exportStatus,
    runningProfileId,
    runExport: runExportCommand,
    selectProfile,
    selectAdjacentReportPackProfile
  };
}

export function buildRestatementReviewPanel(records: ReportingWorkflowRecord[] = []): ReportingRestatementReviewPanel {
  const restatedRecords = records.filter((record) => record.state === "Restated" || record.restatement);
  const selected = [...restatedRecords].sort((left, right) => right.updatedAt.localeCompare(left.updatedAt))[0] ?? null;
  const changedLines = selected?.restatement?.changedLines ?? [];
  const evidenceCount = countRestatementEvidence(selected, changedLines);
  const statusLabel = selected?.state ?? "No restatements";
  const approver = selected?.restatement?.approver?.trim() || "approval pending";
  const reason = selected?.restatement?.reasonCode?.trim() || "No restatement reason captured";

  return {
    regionLabel: "Report-pack restatement review",
    title: "Restatement review",
    description: "Review, approval, and publication-sensitive line changes stay attached to report-pack evidence.",
    statusLabel,
    statusVariant: selected ? "warning" : "outline",
    summaryText: selected
      ? `${reason} approved by ${approver}.`
      : "No report-pack restatement record is loaded for this approval packet.",
    fields: [
      buildReportingDetailField("Workflow records", String(records.length), records.length > 0 ? "default" : "muted"),
      buildReportingDetailField("Restated records", String(restatedRecords.length), restatedRecords.length > 0 ? "warning" : "muted"),
      buildReportingDetailField("Report ID", selected?.reportId ?? "None", selected ? "default" : "muted"),
      buildReportingDetailField("State", selected?.state ?? "No restatement", selected ? "warning" : "muted"),
      buildReportingDetailField("Prior version", selected?.restatement?.priorVersionReportId ?? "None", selected?.restatement?.priorVersionReportId ? "default" : "muted"),
      buildReportingDetailField("Changed lines", String(changedLines.length), changedLines.length > 0 ? "warning" : "muted"),
      buildReportingDetailField("Audit actions", String(selected?.auditTrail.length ?? 0), selected?.auditTrail.length ? "default" : "muted"),
      buildReportingDetailField("Publication", selected?.publication?.manifestId ?? (selected ? "Publication pending" : "No publication"), selected?.publication ? "success" : "muted")
    ],
    changedLinesLabel: "Restatement changed lines",
    changedLines: changedLines.map((line, index) => buildRestatementChangedLineRow(line, index)),
    hasChangedLines: changedLines.length > 0,
    evidenceSummary: `${evidenceCount} evidence link${evidenceCount === 1 ? "" : "s"}`,
    emptyText: "No changed report lines require restatement review."
  };
}

export function buildPublicationReviewPanel(records: ReportingWorkflowRecord[] = []): ReportingPublicationReviewPanel {
  const publishedRecords = records.filter((record) => record.publication);
  const selected = [...publishedRecords]
    .sort((left, right) => {
      const leftTime = left.publication?.signedOffAt ?? left.updatedAt;
      const rightTime = right.publication?.signedOffAt ?? right.updatedAt;
      return rightTime.localeCompare(leftTime);
    })[0] ?? null;
  const publication = selected?.publication ?? null;
  const evidenceCount = publication?.evidenceLinks?.length ?? 0;
  const lineProvenance = selected?.lineProvenance ?? [];
  const signedOffBy = publication?.signedOffBy?.trim() || "signer pending";
  const publicationTime = publication?.signedOffAt?.trim() || "Publication time pending";

  return {
    regionLabel: "Report-pack publication review",
    title: "Publication review",
    description: "Signed-off publication metadata stays aligned with the shared backend report-pack DTO.",
    statusLabel: selected?.state ?? "No publication",
    statusVariant: selected ? "success" : "outline",
    summaryText: publication
      ? `${publication.manifestId} signed off by ${signedOffBy} at ${publicationTime}.`
      : "No report-pack publication record is loaded for this approval packet.",
    fields: [
      buildReportingDetailField("Workflow records", String(records.length), records.length > 0 ? "default" : "muted"),
      buildReportingDetailField("Published records", String(publishedRecords.length), publishedRecords.length > 0 ? "success" : "muted"),
      buildReportingDetailField("Report ID", selected?.reportId ?? "None", selected ? "default" : "muted"),
      buildReportingDetailField("Signed off by", signedOffBy, publication ? "success" : "muted"),
      buildReportingDetailField("Evidence hash", publication?.evidenceHash ?? "None", publication?.evidenceHash ? "success" : "muted"),
      buildReportingDetailField("Manifest path", publication?.retainedManifestPath ?? "None", publication?.retainedManifestPath ? "default" : "muted"),
      buildReportingDetailField("Publication time", publicationTime, publication ? "default" : "muted"),
      buildReportingDetailField("Evidence links", String(evidenceCount), evidenceCount > 0 ? "success" : "muted"),
      buildReportingDetailField("Line provenance", String(lineProvenance.length), lineProvenance.length > 0 ? "success" : "muted")
    ],
    evidenceSummary: `${evidenceCount} evidence link${evidenceCount === 1 ? "" : "s"} / ${lineProvenance.length} provenance line${lineProvenance.length === 1 ? "" : "s"}`,
    evidenceLinksLabel: "Publication evidence links",
    evidenceLinks: (publication?.evidenceLinks ?? []).map(buildPublicationEvidenceRow),
    hasEvidenceLinks: evidenceCount > 0,
    evidenceLinksEmptyText: "No retained publication evidence links are attached to the selected publication.",
    lineProvenanceLabel: "Report-line provenance drill-through",
    lineProvenanceRows: lineProvenance.map((line, index) => buildLineProvenanceRow(line, publication?.evidenceLinks ?? [], index)),
    hasLineProvenance: lineProvenance.length > 0,
    lineProvenanceEmptyText: "No retained report-line provenance is attached to the selected publication."
  };
}

function buildPublicationEvidenceRow(link: ReportingWorkflowEvidenceLink): ReportingPublicationEvidenceRow {
  return {
    id: link.evidenceId,
    label: link.label,
    sourceLabel: `${link.evidenceId} · ${link.source}`,
    capturedLabel: link.capturedAtUtc ?? "capture time pending",
    href: link.route ?? null,
    ariaLabel: `${link.label} publication evidence from ${link.source}`
  };
}

function buildRestatementChangedLineRow(line: ReportingWorkflowChangedLine, index: number): ReportingRestatementChangedLineRow {
  const evidenceLinks = line.evidenceLinks ?? [];
  const evidenceCount = evidenceLinks.length;
  const firstEvidence = evidenceLinks[0] ?? null;
  return {
    id: `${line.lineKey || "line"}-${index}`,
    lineKey: line.lineKey,
    valueBridge: `${line.previousValue} -> ${line.currentValue}`,
    evidenceLabel: `${evidenceCount} evidence link${evidenceCount === 1 ? "" : "s"}`,
    evidenceHref: firstEvidence?.route ?? null,
    ariaLabel: `${line.lineKey} changed from ${line.previousValue} to ${line.currentValue} with ${evidenceCount} evidence link${evidenceCount === 1 ? "" : "s"}`
  };
}

function buildLineProvenanceRow(
  line: ReportingWorkflowLineProvenance,
  publicationEvidenceLinks: ReportingWorkflowEvidenceLink[],
  index: number
): ReportingLineProvenanceRow {
  const evidence = publicationEvidenceLinks.find((link) => link.evidenceId === line.evidenceId) ?? null;
  const explorerLabel = formatFinancialRecordExplorerLabel(line.financialRecordExplorerId);
  const sourceLabel = [
    line.sourceKind,
    line.sourceId,
    line.ledgerEntryId ? `ledger=${line.ledgerEntryId}` : null,
    line.reconciliationOutcome ? `recon=${line.reconciliationOutcome}` : null
  ].filter(Boolean).join(" · ");

  return {
    id: `${line.lineKey || "line"}-${index}`,
    lineKey: line.lineKey,
    sourceLabel,
    valueLabel: line.reportValue ? `value ${line.reportValue}` : "No report value",
    evidenceLabel: evidence?.label ?? line.evidenceId ?? "No retained evidence",
    evidenceHref: evidence?.route ?? null,
    financialRecordLabel: explorerLabel,
    financialRecordHref: line.financialRecordHref ?? null,
    ariaLabel: `${line.lineKey} provenance from ${line.sourceKind} with ${explorerLabel}`
  };
}

function formatFinancialRecordExplorerLabel(explorerId: string | null | undefined): string {
  if (explorerId === "portfolio") {
    return "Open Portfolio Explorer";
  }

  if (explorerId === "security-instrument") {
    return "Open Security & Instrument Explorer";
  }

  return "Open Ledger Explorer";
}

function countRestatementEvidence(selected: ReportingWorkflowRecord | null, changedLines: ReportingWorkflowChangedLine[]): number {
  if (!selected) {
    return 0;
  }

  return changedLines.reduce((total, line) => total + (line.evidenceLinks?.length ?? 0), 0);
}

function buildTemplateRows(templates: ReportingTemplateMetadata[]): ReportingTemplateRow[] {
  return templates.map((template) => {
    const writerGrids = buildWriterGridRows(template);
    const gridCount = template.reportWriterGrids?.length ?? 0;
    const gridMetricCount = template.reportWriterGrids?.reduce((total, grid) => total + grid.metricCount + grid.formulaCount, 0) ?? 0;
    const sectionLabel = `${template.sections.length} section${template.sections.length === 1 ? "" : "s"}`;
    const gridLabel = gridCount > 0
      ? `; ${gridCount} report-writer grid${gridCount === 1 ? "" : "s"} with ${gridMetricCount} metric${gridMetricCount === 1 ? "" : "s"}`
      : "";
    const isAccessible = template.isAccessible ?? true;
    const lifecycleStatus = template.lifecycleStatus ?? "Approved";
    const versionNumber = parseTemplateVersionNumber(template.version);
    const runDisabledReason = resolveTemplateRunDisabledReason(template, isAccessible, lifecycleStatus);
    const lifecycleActions = buildTemplateLifecycleActions(template, isAccessible, lifecycleStatus);
    const auditTrail = template.auditTrail ?? [];
    const validationIssues = template.validationIssues ?? [];

    return {
      id: `${template.templateId}:${template.version}`,
      templateName: template.templateId,
      name: template.name,
      family: template.family,
      version: template.version,
      versionNumber,
      sectionSummary: `${sectionLabel}${gridLabel}`,
      statusLabel: lifecycleStatus,
      statusVariant: templateStatusVariant(template.lifecycleStatus),
      sourceLabel: template.isBuiltIn === false ? "Custom" : "Built-in",
      approvalSummary: template.approvalSummary ?? (
        template.isBuiltIn === false
          ? "Custom template approval metadata is pending."
          : "Built-in approved template."
      ),
      accessMode: template.accessMode ?? "CompanyWide",
      accessSummary: template.accessSummary ?? "Company-wide access",
      isAccessible,
      accessGovernance: buildTemplateAccessGovernance(template, isAccessible),
      latestApprovedLabel: template.isLatestApproved ? "Latest approved" : "Not latest approved",
      versionLineageSummary: buildTemplateVersionLineageSummary(template),
      auditTrailSummary: `${auditTrail.length} audit event${auditTrail.length === 1 ? "" : "s"}`,
      lastAuditSummary: buildTemplateLastAuditSummary(template),
      decisionSummary: buildTemplateDecisionSummary(template),
      validationSummary: validationIssues.length > 0
        ? `${validationIssues.length} validation issue${validationIssues.length === 1 ? "" : "s"}`
        : "No validation issues",
      authoringHref: template.authoringRoute ?? `/api/fund-structure/reporting/templates/${template.templateId}/versions/${template.version}`,
      actionLabel: template.isBuiltIn === false ? "Review version" : "Draft revision",
      actionAriaLabel: template.isBuiltIn === false
        ? `Review ${template.name} template version`
        : `Draft a revision of ${template.name}`,
      canRunOnDemand: isAccessible && lifecycleStatus === "Approved",
      runActionLabel: "Run report",
      runActionAriaLabel: `Run ${template.name} report on demand`,
      runDisabledReason,
      lifecycleActions,
      hasLifecycleActions: lifecycleActions.length > 0,
      writerGrids,
      hasWriterGrids: writerGrids.length > 0,
      writerGridSummary: writerGrids.length > 0
        ? `${writerGrids.length} grid${writerGrids.length === 1 ? "" : "s"} ready for no-code layout`
        : "No report-writer grids"
    };
  });
}

function buildTemplateAccessGovernance(
  template: ReportingTemplateMetadata,
  isAccessible: boolean
): ReportingTemplateAccessGovernanceRow {
  const mode = template.accessMode ?? "CompanyWide";
  const summary = template.accessSummary ?? "Company-wide access";
  const modeLabel = formatTemplateAccessMode(mode);
  const scopeLabel = resolveTemplateAccessScopeLabel(mode, summary);
  const postureLabel = isAccessible ? "Runnable" : "Access blocked";
  return {
    modeLabel,
    scopeLabel,
    postureLabel,
    postureVariant: isAccessible ? "success" : "warning",
    detail: isAccessible
      ? `${summary}; run and lifecycle actions use the shared access evaluation.`
      : `${summary}; run and lifecycle actions are disabled by the shared access evaluation.`,
    ariaLabel: `${template.name} access governance ${modeLabel} ${postureLabel}`
  };
}

function formatTemplateAccessMode(mode: string): string {
  if (mode === "Private") {
    return "User-locked";
  }

  if (mode === "Restricted") {
    return "User/group";
  }

  return "Company-wide";
}

function resolveTemplateAccessScopeLabel(mode: string, summary: string): string {
  if (mode === "Private") {
    return "Owner";
  }

  if (mode === "Restricted") {
    if (/\buser\b/i.test(summary)) {
      return "User";
    }

    if (/\bcompany\b/i.test(summary)) {
      return "Company";
    }

    return "Group";
  }

  return "Company";
}

function buildTemplateVersionLineageSummary(template: ReportingTemplateMetadata): string {
  const basedOn = template.basedOnTemplateId
    ? `based on ${template.basedOnTemplateId.name}@v${template.basedOnTemplateId.version}`
    : "no prior template";
  const updatedBy = template.updatedBy?.trim() || template.createdBy?.trim() || "unknown actor";
  const updatedAt = template.updatedAt ?? template.createdAt;
  const updated = updatedAt ? `updated ${formatTimestamp(updatedAt)} by ${updatedBy}` : `updated by ${updatedBy}`;
  return `${template.templateId}@v${template.version} ${basedOn}; ${updated}`;
}

function buildTemplateLastAuditSummary(template: ReportingTemplateMetadata): string {
  const auditTrail = template.auditTrail ?? [];
  const last = auditTrail[auditTrail.length - 1];
  if (!last) {
    return "No template audit events retained in payload";
  }

  return `${last.action} ${last.fromStatus}->${last.toStatus} by ${last.actor} at ${formatTimestamp(last.at)}`;
}

function buildTemplateDecisionSummary(template: ReportingTemplateMetadata): string {
  const decision = template.decisionRationale?.trim();
  const approvalReference = template.approvalReference?.trim();
  const approvedBy = template.approvedBy?.trim();
  const rejectedBy = template.rejectedBy?.trim();
  const submittedBy = template.submittedBy?.trim();
  const reviewer = approvedBy
    ? `approved by ${approvedBy}`
    : rejectedBy
      ? `rejected by ${rejectedBy}`
      : submittedBy
        ? `submitted by ${submittedBy}`
        : null;

  return [
    reviewer,
    approvalReference ? `ref ${approvalReference}` : null,
    decision
  ].filter(Boolean).join("; ") || "No review decision recorded";
}

function buildTemplateLifecycleActions(
  template: ReportingTemplateMetadata,
  isAccessible: boolean,
  lifecycleStatus: string
): ReportingTemplateLifecycleActionRow[] {
  if (template.isBuiltIn !== false || !isAccessible) {
    return [];
  }

  if (lifecycleStatus === "Draft" || lifecycleStatus === "Rejected") {
    return [
      {
        id: `${template.templateId}:${template.version}:submit`,
        kind: "submit",
        label: "Submit",
        ariaLabel: `Submit ${template.name} template version ${template.version} for review`,
        targetStatus: "InReview",
        isEnabled: true,
        disabledReason: null
      }
    ];
  }

  if (lifecycleStatus === "InReview") {
    return [
      {
        id: `${template.templateId}:${template.version}:approve`,
        kind: "approve",
        label: "Approve",
        ariaLabel: `Approve ${template.name} template version ${template.version}`,
        targetStatus: "Approved",
        isEnabled: true,
        disabledReason: null
      },
      {
        id: `${template.templateId}:${template.version}:reject`,
        kind: "reject",
        label: "Reject",
        ariaLabel: `Reject ${template.name} template version ${template.version}`,
        targetStatus: "Rejected",
        isEnabled: true,
        disabledReason: null
      }
    ];
  }

  return [];
}

function buildWriterGridRows(template: ReportingTemplateMetadata): ReportingWriterGridRow[] {
  return (template.reportWriterGrids ?? []).map((grid) => {
    const rowFields = buildFieldTokens(grid.rowFields, "row", grid.dimensionCount);
    const columnFields = buildFieldTokens(grid.columnFields, "column", 0);
    const metrics = buildMetricTokens(grid);
    const formulas = buildFormulaTokens(grid);
    const filters = buildFilterRows(grid);
    const sourceFields = buildSourceFieldTokens(grid, rowFields, columnFields, metrics, formulas);
    const sortLabel = grid.sortBy
      ? `${grid.sortDescending === false ? "Ascending" : "Descending"} by ${grid.sortBy}`
      : "Default order";
    const topNLabel = grid.kind === "TopN" && grid.topN
      ? `Top ${grid.topN}`
      : grid.kind;
    const filterSummary = filters.length > 0
      ? `${filters.length} saved filter${filters.length === 1 ? "" : "s"}`
      : "No saved filters";

    return {
      id: `${template.templateId}:${template.version}:${grid.gridId}`,
      gridId: grid.gridId,
      templateId: template.templateId,
      templateVersion: template.version,
      family: template.family,
      title: grid.title,
      kind: grid.kind,
      topN: grid.topN ?? null,
      sortBy: grid.sortBy ?? null,
      sortDescending: grid.sortDescending ?? true,
      summary: `${grid.kind} grid with ${rowFields.length + columnFields.length} dimension${rowFields.length + columnFields.length === 1 ? "" : "s"}, ${metrics.length} metric${metrics.length === 1 ? "" : "s"}, ${formulas.length} formula${formulas.length === 1 ? "" : "s"}, and ${filters.length} saved filter${filters.length === 1 ? "" : "s"}.`,
      sourceFields,
      rowFields,
      columnFields,
      metrics,
      formulas,
      filters,
      topNLabel,
      sortLabel,
      filterSummary,
      ariaLabel: `${template.name} ${grid.title} ${grid.kind} report-writer grid`
    };
  });
}

function buildFieldTokens(fields: string[] | null | undefined, zone: string, fallbackCount: number): ReportingWriterToken[] {
  if (fields && fields.length > 0) {
    return fields.map((field) => ({
      id: `${zone}:${field}`,
      label: field,
      detail: "Dataset field",
      kind: "field",
      fieldName: field
    }));
  }

  return Array.from({ length: fallbackCount }, (_, index) => ({
    id: `${zone}:field-${index + 1}`,
    label: `Field ${index + 1}`,
    detail: "Field metadata unavailable",
    kind: "field",
    fieldName: null
  }));
}

function buildMetricTokens(grid: ReportingTemplateGridMetadata): ReportingWriterToken[] {
  if (grid.metrics && grid.metrics.length > 0) {
    return grid.metrics.map((metric) => ({
      id: `metric:${metric.name}`,
      label: metric.label ?? metric.name,
      detail: `${metric.function}(${metric.sourceField})`,
      kind: "metric",
      name: metric.name,
      sourceField: metric.sourceField,
      function: normalizeAggregateFunction(metric.function),
      fieldName: metric.sourceField
    }));
  }

  return Array.from({ length: grid.metricCount }, (_, index) => ({
    id: `metric:metric-${index + 1}`,
    label: `Metric ${index + 1}`,
    detail: "Metric metadata unavailable",
    kind: "metric",
    name: null,
    sourceField: null,
    function: "Sum",
    fieldName: null
  }));
}

function buildFormulaTokens(grid: ReportingTemplateGridMetadata): ReportingWriterToken[] {
  if (grid.formulas && grid.formulas.length > 0) {
    return grid.formulas.map((formula) => ({
      id: `formula:${formula.name}`,
      label: formula.label ?? formula.name,
      detail: formula.expression,
      kind: "formula",
      name: formula.name,
      expression: formula.expression
    }));
  }

  return Array.from({ length: grid.formulaCount }, (_, index) => ({
    id: `formula:formula-${index + 1}`,
    label: `Formula ${index + 1}`,
    detail: "Formula metadata unavailable",
    kind: "formula",
    name: null,
    expression: null
  }));
}

function buildFilterRows(grid: ReportingTemplateGridMetadata): ReportingWriterFilterRow[] {
  return (grid.filters ?? []).map((filter, index) => {
    const operator = normalizeFilterOperator(filter.operator);
    const value = filter.value?.trim() || null;
    const label = filter.label?.trim() || `${filter.field} ${formatFilterOperator(operator)}${value ? ` ${value}` : ""}`.trim();
    return {
      id: `filter:${filter.field}:${operator}:${value ?? index}`,
      field: filter.field,
      operator,
      value,
      label,
      summary: `${filter.field} ${formatFilterOperator(operator)}${value ? ` ${value}` : ""}`.trim()
    };
  });
}

function buildSourceFieldTokens(
  grid: ReportingTemplateGridMetadata,
  rowFields: ReportingWriterToken[],
  columnFields: ReportingWriterToken[],
  metrics: ReportingWriterToken[],
  formulas: ReportingWriterToken[]
): ReportingWriterToken[] {
  if (grid.sourceFields && grid.sourceFields.length > 0) {
    return grid.sourceFields.map((field) => ({
      id: `source:${field.name}`,
      label: field.label || field.name,
      detail: `${field.dataset} · ${field.role} · ${field.dataType}${field.description ? ` · ${field.description}` : ""}`,
      kind: field.role?.toLowerCase() === "generated" ? "formula" : field.role?.toLowerCase() === "metric" ? "metric" : "field",
      fieldName: field.name,
      sourceField: field.name,
      name: field.name,
      function: field.role?.toLowerCase() === "metric" ? "Sum" : null,
      expression: field.role?.toLowerCase() === "generated" ? `{${field.name}}` : null,
      dataType: field.dataType,
      dataset: field.dataset,
      role: field.role
    }));
  }

  const names = new Set<string>();
  for (const token of [...rowFields, ...columnFields]) {
    if (!token.label.startsWith("Field ")) {
      names.add(token.label);
    }
  }

  for (const metric of grid.metrics ?? []) {
    names.add(metric.sourceField);
  }

  for (const formula of grid.formulas ?? []) {
    for (const field of extractFormulaFields(formula.expression)) {
      names.add(field);
    }
  }

  for (const filter of grid.filters ?? []) {
    names.add(filter.field);
  }

  if (grid.sortBy) {
    names.add(grid.sortBy);
  }

  if (names.size === 0) {
    for (const token of [...metrics, ...formulas]) {
      names.add(token.label);
    }
  }

  return Array.from(names)
    .sort((left, right) => left.localeCompare(right))
    .map((name) => ({
      id: `source:${name}`,
      label: name,
      detail: "Available dataset field",
      kind: "field",
      fieldName: name
    }));
}

function normalizeFilterOperator(value: ReportWriterFilterOperator | string | null | undefined): ReportWriterFilterOperator {
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

function formatFilterOperator(operator: ReportWriterFilterOperator | string): string {
  switch (normalizeFilterOperator(operator)) {
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

function normalizeAggregateFunction(value: string | null | undefined): ReportWriterAggregateFunction {
  switch ((value ?? "").toLowerCase()) {
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

function extractFormulaFields(expression: string): string[] {
  const fields = new Set<string>();
  const matches = expression.matchAll(/\{([^}]+)\}/g);
  for (const match of matches) {
    const field = match[1]?.trim();
    if (field) {
      fields.add(field);
    }
  }

  return Array.from(fields);
}

function parseTemplateVersionNumber(version: string): number {
  const match = version.match(/\d+/);
  const parsed = match ? Number.parseInt(match[0], 10) : Number.NaN;
  return Number.isInteger(parsed) && parsed > 0 ? parsed : 1;
}

function resolveTemplateRunDisabledReason(
  template: ReportingTemplateMetadata,
  isAccessible: boolean,
  lifecycleStatus: string
): string | null {
  if (!isAccessible) {
    return "Current user does not have access to this report template.";
  }

  return lifecycleStatus === "Approved"
    ? null
    : "Only approved templates can be run on demand.";
}

function templateStatusVariant(status?: string): Exclude<ReportingBadgeVariant, "default"> {
  if (status === "Approved") {
    return "success";
  }

  if (status === "Draft" || status === "InReview") {
    return "warning";
  }

  return "outline";
}

function buildRunStatusRows(runs: ReportingRunStatusProjection[]): ReportingRunStatusRow[] {
  return runs.map((run) => {
    const drilldownLinks = buildRunLinkRows(run);
    const nextActions = buildRunActionRows(run);

    return {
      id: run.runId,
      templateId: run.templateId,
      family: run.family,
      status: run.status,
      trigger: run.trigger,
      runIdLabel: run.runId,
      templateLabel: run.templateId,
      asOfDateLabel: run.asOfDate?.trim() || "As-of date unavailable",
      attemptLabel: `${run.attemptCount} attempt${run.attemptCount === 1 ? "" : "s"}`,
      sectionLabel: `${run.sectionCount} section${run.sectionCount === 1 ? "" : "s"}`,
      lineageLabel: `${run.lineageLinkedSections}/${run.sectionCount} linked`,
      artifactLabel: `${run.artifacts.length} artifact${run.artifacts.length === 1 ? "" : "s"}`,
      artifactNames: run.artifacts,
      hasArtifacts: run.artifacts.length > 0,
      generatedGridLabel: buildGeneratedGridLabel(run),
      generatedGridNames: buildGeneratedGridNames(run),
      generatedGridArtifacts: buildGeneratedGridArtifactRows(run),
      hasGeneratedGrids: (run.generatedReportWriterGrids?.length ?? 0) > 0,
      datasetSourceLabel: buildRunDatasetSourceLabel(run),
      lineageSummary: `${run.lineageLinkedSections}/${run.sectionCount} sections linked`,
      auditSummary: run.auditActions.length > 0 ? run.auditActions.join(" → ") : "No audit actions",
      failureReason: run.failureReason,
      drilldownLinks,
      nextActions,
      hasDrilldownLinks: drilldownLinks.length > 0,
      hasNextActions: nextActions.length > 0
    };
  });
}

function buildRunDatasetSourceLabel(run: ReportingRunStatusProjection): string {
  const sourceLabel = run.reportWriterDatasetSourceLabel?.trim();
  const sourceId = run.reportWriterDatasetSourceId?.trim();
  const rowCount = run.reportWriterDatasetRowCount;
  if (!sourceLabel && !sourceId && rowCount == null) {
    return "No report-writer dataset retained";
  }

  const baseLabel = sourceLabel || sourceId || "Report-writer dataset";
  return rowCount == null
    ? baseLabel
    : `${baseLabel} (${rowCount} row${rowCount === 1 ? "" : "s"})`;
}

function buildGeneratedGridLabel(run: ReportingRunStatusProjection): string {
  const grids = run.generatedReportWriterGrids ?? [];
  if (grids.length === 0) {
    return "No generated report-writer grids";
  }

  const formulaCount = grids.reduce((total, grid) => total + Math.max(0, grid.formulaCount), 0);
  const validationCount = grids.reduce((total, grid) => total + Math.max(0, grid.validationPassedCount ?? 0) + Math.max(0, grid.validationWarningCount ?? 0) + Math.max(0, grid.validationFailedCount ?? 0), 0);
  const validationIssues = grids.reduce((total, grid) => total + Math.max(0, grid.validationWarningCount ?? 0) + Math.max(0, grid.validationFailedCount ?? 0), 0);
  const validationSummary = validationCount > 0
    ? `; validation ${validationCount - validationIssues} passed / ${validationCount} checks${validationIssues > 0 ? `, ${validationIssues} review` : ""}`
    : "";
  return `${grids.length} generated grid${grids.length === 1 ? "" : "s"} with ${formulaCount} formula${formulaCount === 1 ? "" : "s"}${validationSummary}`;
}

function buildGeneratedGridNames(run: ReportingRunStatusProjection): string[] {
  return (run.generatedReportWriterGrids ?? []).map((grid) => {
    const title = grid.title?.trim() || grid.gridId;
    const counts = [
      `${grid.dimensionCount}d`,
      `${grid.metricCount}m`,
      `${grid.formulaCount}f`
    ].join("/");
    const validationSummary = grid.validationSummary?.trim();
    return validationSummary
      ? `${title} (${grid.kind}, ${counts}, validation ${validationSummary})`
      : `${title} (${grid.kind}, ${counts})`;
  });
}

function buildGeneratedGridArtifactRows(run: ReportingRunStatusProjection): ReportingGeneratedGridArtifactRow[] {
  return (run.generatedReportWriterGrids ?? []).map((grid) => {
    const title = grid.title?.trim() || grid.gridId;
    const validationSummary = grid.validationSummary?.trim();
    const label = validationSummary
      ? `${title} (${grid.kind}, validation ${validationSummary})`
      : `${title} (${grid.kind})`;
    return {
      id: `${run.runId}-${grid.gridId}`,
      label,
      jsonHref: reportingRunReportWriterGridEndpoint(run.runId, grid.gridId),
      csvHref: reportingRunReportWriterGridEndpoint(run.runId, grid.gridId, "csv"),
      xlsHref: reportingRunReportWriterGridEndpoint(run.runId, grid.gridId, "xls"),
      xlsxHref: reportingRunReportWriterGridEndpoint(run.runId, grid.gridId, "xlsx")
    };
  });
}

function buildRunLinkRows(run: ReportingRunStatusProjection): ReportingRunLinkRow[] {
  const typedLinks = run.drilldownLinks ?? [];
  if (typedLinks.length > 0) {
    return typedLinks.map((link) => ({
      id: link.id,
      kind: link.kind,
      label: link.label,
      href: link.href,
      method: link.method,
      source: link.source,
      isBrowserNavigable: link.isBrowserNavigable,
      interactionLabel: link.isBrowserNavigable ? "Open" : "Reference",
      ariaLabel: link.isBrowserNavigable
        ? `${link.method} ${link.href} for ${link.label}`
        : `Reference-only ${link.method} ${link.href} for ${link.label}`
    }));
  }

  return run.artifacts.map((artifact, index) => ({
    id: `${run.runId}-artifact-${index}`,
    kind: "artifact",
    label: artifactLabel(artifact),
    href: artifact,
    method: "GET",
    source: run.family,
    isBrowserNavigable: artifact.startsWith("/"),
    interactionLabel: artifact.startsWith("/") ? "Open" : "Reference",
    ariaLabel: artifact.startsWith("/")
      ? `GET ${artifact} for ${artifactLabel(artifact)}`
      : `Reference-only artifact ${artifact}`
  }));
}

function buildRunActionRows(run: ReportingRunStatusProjection): ReportingRunActionRow[] {
  return (run.nextActions ?? []).map((action) => ({
    id: action.id,
    kind: action.kind,
    label: action.label,
    href: action.href,
    method: action.method,
    isEnabled: action.isEnabled,
    disabledReason: action.disabledReason,
    isBrowserNavigable: action.isBrowserNavigable,
    interactionLabel: action.isBrowserNavigable ? "Open" : "Reference",
    ariaLabel: action.isEnabled
      ? `${action.method} ${action.href} for ${action.label}`
      : `${action.label} unavailable${action.disabledReason ? `: ${action.disabledReason}` : ""}`
  }));
}

function buildScheduleRows(
  schedules: ReportingScheduleRecord[],
  datasetSources: ReportWriterDatasetSource[]
): ReportingScheduleRow[] {
  return schedules.map((schedule) => ({
    id: schedule.scheduleId,
    templateId: schedule.templateId,
    state: schedule.state,
    stateVariant: schedule.state === "Active" ? "success" : schedule.state === "Paused" ? "warning" : "outline",
    cronLabel: schedule.cronExpression,
    dueLabel: formatTimestamp(schedule.dueAtUtc),
    nextAsOfLabel: schedule.nextAsOfDate,
    lastRunLabel: schedule.lastRunAtUtc ? formatTimestamp(schedule.lastRunAtUtc) : "Not run",
    runCountLabel: `${schedule.runCount} run${schedule.runCount === 1 ? "" : "s"}`,
    description: schedule.description?.trim() || "No schedule note",
    deliveryTargetLabel: formatScheduleDeliveryTargets(schedule.deliveryTargets),
    datasetSourceLabel: formatScheduleDatasetSource(schedule.datasetSourceId, datasetSources),
    canPause: schedule.state === "Active",
    canResume: schedule.state === "Paused",
    ariaLabel: `${schedule.scheduleId} ${schedule.templateId} reporting schedule is ${schedule.state}`
  }));
}

function buildScheduleSummary(schedules: ReportingScheduleRow[]): string {
  if (schedules.length === 0) {
    return "No reporting schedules configured.";
  }

  const activeCount = schedules.filter((schedule) => schedule.state === "Active").length;
  return `${schedules.length} schedule${schedules.length === 1 ? "" : "s"} configured; ${activeCount} active.`;
}

function buildScheduleDeliveryPlanRows(plans: ReportingScheduleDeliveryPlan[]): ReportingScheduleDeliveryPlanRow[] {
  return plans.map((plan) => ({
    id: plan.planId,
    scheduleId: plan.scheduleId,
    templateId: plan.templateId,
    distributionId: plan.distributionId,
    recipient: plan.recipient,
    recipientRole: plan.recipientRole,
    channel: plan.channel,
    deliveryMode: plan.deliveryMode,
    formatsLabel: plan.formats.length > 0 ? plan.formats.join(", ") : "Pdf, Xlsx, Csv",
    readinessSummary: plan.readinessSummary,
    readinessVariant: plan.isReady ? "success" : "warning",
    dueLabel: formatTimestamp(plan.dueAtUtc),
    nextAsOfLabel: plan.nextAsOfDate,
    ownerLabel: plan.owner,
    route: plan.route,
    note: plan.note,
    lastDeliveryLabel: formatSchedulePlanLastDelivery(plan),
    lastDeliveryHref: plan.lastDeliverySecureLink ?? plan.lastDeliveryPackageRoute,
    lastDeliveryLinks: buildDeliveryAccessLinkRows(plan.lastDeliveryAccessLinks, `${plan.planId}-delivery-link`),
    integrityLabel: formatSchedulePlanIntegrity(plan),
    integritySummary: plan.lastDeliveryIntegritySummary ?? null,
    brandingLabel: formatSchedulePlanBranding(plan),
    versionStamp: plan.versionStamp,
    ariaLabel: `${plan.recipient} ${plan.deliveryMode} scheduled delivery plan for ${plan.scheduleId}`
  }));
}

function formatSchedulePlanBranding(plan: ReportingScheduleDeliveryPlan): string {
  if (plan.brandingTheme) {
    return `${plan.brandingTheme.name} · ${plan.brandingTheme.firmName} · ${plan.brandingTheme.themeId}`;
  }

  return plan.brandingThemeId?.trim() || "Default theme";
}

function formatScheduleDatasetSource(
  datasetSourceId: string | null | undefined,
  datasetSources: ReportWriterDatasetSource[]
): string {
  const normalizedSourceId = datasetSourceId?.trim();
  if (!normalizedSourceId) {
    return "Default retained dataset";
  }

  const source = datasetSources.find((candidate) =>
    candidate.sourceId.localeCompare(normalizedSourceId, undefined, { sensitivity: "base" }) === 0);

  return source ? `${source.label} (${source.rowCount})` : normalizedSourceId;
}

function buildDeliveryAccessLinkRows(
  links: ReportPackDeliveryAccessLink[] | null | undefined,
  idPrefix: string
): ReportingDeliveryAccessLinkRow[] {
  return (links ?? [])
    .filter((link) => link.href?.trim())
    .map((link, index) => {
      const label = link.label?.trim() || link.kind || "Delivery link";
      const href = link.href.trim();
      const expiresLabel = link.expiresAtUtc ? `Expires ${formatTimestamp(link.expiresAtUtc)}` : null;
      return {
        id: `${idPrefix}-${index + 1}`,
        kind: link.kind?.trim() || "delivery-link",
        label,
        href,
        tokenLabel: link.requiresToken ? "Token gated" : "Internal",
        expiresLabel,
        description: link.description?.trim() || null,
        ariaLabel: `${label} ${link.requiresToken ? "token gated" : "internal route"} ${href}`
      };
    });
}

function buildScheduleDeliveryPlanSummary(plans: ReportingScheduleDeliveryPlanRow[]): string {
  if (plans.length === 0) {
    return "No schedule delivery plans configured.";
  }

  const readyCount = plans.filter((plan) => plan.readinessVariant === "success").length;
  return `${plans.length} delivery plan${plans.length === 1 ? "" : "s"} configured; ${readyCount} ready.`;
}

function formatSchedulePlanLastDelivery(plan: ReportingScheduleDeliveryPlan): string {
  if (!plan.lastDeliveryAtUtc || !plan.lastDeliveryState) {
    return "No retained delivery yet";
  }

  return `${plan.lastDeliveryState} ${formatTimestamp(plan.lastDeliveryAtUtc)}`;
}

function formatSchedulePlanIntegrity(plan: ReportingScheduleDeliveryPlan): string {
  const artifactCount = plan.lastDeliveryArtifactCount ?? 0;
  if (artifactCount <= 0) {
    return "No retained artifact checksums";
  }

  return `${artifactCount} artifact${artifactCount === 1 ? "" : "s"} with SHA-256`;
}

function formatScheduleDeliveryTargets(targets: ReportingScheduleDeliveryTarget[] | null | undefined): string {
  if (!targets || targets.length === 0) {
    return "No delivery targets";
  }

  return targets.map(formatScheduleDeliveryTarget).join("; ");
}

function formatScheduleDeliveryTarget(target: ReportingScheduleDeliveryTarget): string {
  const formats = target.formats && target.formats.length > 0
    ? target.formats.join("/")
    : "Pdf/Xlsx/Csv";
  const mode = target.deliveryMode ?? "Policy";
  return `${target.distributionId} via ${mode} (${formats})`;
}

function formatTimestamp(value: string): string {
  const timestamp = new Date(value);
  if (Number.isNaN(timestamp.getTime())) {
    return value;
  }

  return timestamp.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit"
  });
}

function artifactLabel(artifact: string): string {
  if (artifact.startsWith("/api/fund-structure/report-packs/") && artifact.includes("/evidence-bundle")) {
    return "Evidence bundle";
  }

  if (artifact.startsWith("/api/fund-structure/report-packs/") && artifact.includes("/ledger-provenance")) {
    return "Ledger provenance";
  }

  if (artifact.startsWith("publication-manifest:")) {
    return "Publication manifest";
  }

  if (artifact.startsWith("restatement:")) {
    return "Restatement";
  }

  if (artifact.startsWith("schedule:")) {
    return "Schedule source";
  }

  if (artifact.startsWith("evidence:")) {
    return "Evidence reference";
  }

  return "Artifact";
}

function buildLoadingState(pathname: string): ReportingLoadingState {
  const title = "Loading Reporting";

  return {
    role: "status",
    ariaBusy: true,
    ariaLive: "polite",
    titleId: "reporting-loading-title",
    detailId: "reporting-loading-detail",
    title,
    detail: "Waiting for governed report-pack and export evidence.",
    badgeLabel: "Loading",
    routeLabel: isReportPackRoute(pathname) ? "Report packs" : "Reporting"
  };
}

function buildPackTargetsEmptyState(): ReportingPackTargetsEmptyState {
  return {
    text: "No report-pack recipients loaded. Configure distribution records before approving this packet.",
    ariaLabel: "No report-pack recipients loaded"
  };
}

function buildWorkbenchActions(selectedProfile: GovernanceReportingProfile | null): ReportingWorkbenchAction[] {
  const subjectId = selectedProfile?.id ?? "current";
  const label = selectedProfile ? "Profile evidence" : "Evidence";

  return [
    {
      id: "evidence",
      label,
      href: evidenceWorkbenchPath("report-pack", subjectId),
      ariaLabel: selectedProfile
        ? `Open ${selectedProfile.name} report-pack evidence`
        : "Open current report-pack evidence"
    }
  ];
}

function buildWorkbenchChips(
  countLabel: string,
  packTargetCountLabel: string,
  recommendedCountLabel: string
): ReportingChipViewModel[] {
  return [
    { label: "Profiles", value: countLabel },
    { label: "Recipients", value: packTargetCountLabel },
    { label: "Recommended", value: recommendedCountLabel },
    { label: "Export route", value: EXPORT_API_ENDPOINTS.analysis }
  ];
}

function buildQueueChips(
  visibleCountLabel: string,
  recommendedCountLabel: string,
  packTargetCountLabel: string,
  listLabel: string
): ReportingChipViewModel[] {
  return [
    { label: "Visible", value: visibleCountLabel },
    { label: "Recommended", value: recommendedCountLabel },
    { label: "Recipients", value: packTargetCountLabel },
    { label: "List", value: listLabel }
  ];
}

function buildPackTargetChips(
  packTargetCountLabel: string,
  statusTitle: string
): ReportingChipViewModel[] {
  return [
    { label: "Recipients", value: packTargetCountLabel },
    { label: "Inspector", value: statusTitle }
  ];
}

function buildReportingAccessAudit(reporting: GovernanceReportingSummary | null): ReportingAccessAuditViewModel {
  const audit = reporting?.accessAudit ?? null;
  if (!audit) {
    return {
      evaluationScope: "Unavailable",
      summary: "Reporting access audit evidence is not available for this payload.",
      principalScopes: ["scope:unavailable"],
      scopeLabel: "scope:unavailable",
      hiddenTotalLabel: "0 hidden",
      postureLabel: "Unknown",
      postureVariant: "outline",
      countRows: buildAccessAuditCountRows(),
      denialReasons: ["The server did not include aggregate access-audit counts for this Reporting payload."],
      hasDenialReasons: true,
      ariaLabel: "Reporting access audit unavailable"
    };
  }

  const hiddenTotal =
    audit.hiddenTemplateCount +
    audit.hiddenReportPackCount +
    audit.hiddenScheduleCount +
    audit.hiddenDeliveryAttemptCount +
    audit.hiddenStructuredExportCount;
  const principalScopes = audit.principalScopes.length > 0 ? audit.principalScopes : ["scope:anonymous"];
  const postureLabel = hiddenTotal > 0 ? "Scoped" : "Fully visible";

  return {
    evaluationScope: audit.evaluationScope,
    summary: audit.summary,
    principalScopes,
    scopeLabel: principalScopes.join(" · "),
    hiddenTotalLabel: `${hiddenTotal} hidden`,
    postureLabel,
    postureVariant: hiddenTotal > 0 ? "warning" : "success",
    countRows: buildAccessAuditCountRows(audit),
    denialReasons: audit.denialReasons,
    hasDenialReasons: audit.denialReasons.length > 0,
    ariaLabel: `${audit.evaluationScope} reporting access audit: ${hiddenTotal} hidden object${hiddenTotal === 1 ? "" : "s"}`
  };
}

function buildAccessAuditCountRows(
  audit?: GovernanceReportingSummary["accessAudit"] | null
): ReportingAccessAuditCountRow[] {
  return [
    buildAccessAuditCountRow("templates", "Templates", audit?.visibleTemplateCount ?? 0, audit?.hiddenTemplateCount ?? 0),
    buildAccessAuditCountRow("report-packs", "Report packs", audit?.visibleReportPackCount ?? 0, audit?.hiddenReportPackCount ?? 0),
    buildAccessAuditCountRow("schedules", "Schedules", audit?.visibleScheduleCount ?? 0, audit?.hiddenScheduleCount ?? 0),
    buildAccessAuditCountRow("deliveries", "Deliveries", audit?.visibleDeliveryAttemptCount ?? 0, audit?.hiddenDeliveryAttemptCount ?? 0),
    buildAccessAuditCountRow("structured-exports", "Structured exports", audit?.visibleStructuredExportCount ?? 0, audit?.hiddenStructuredExportCount ?? 0)
  ];
}

function buildAccessAuditCountRow(
  id: string,
  label: string,
  visibleCount: number,
  hiddenCount: number
): ReportingAccessAuditCountRow {
  return {
    id,
    label,
    visibleLabel: `${visibleCount} visible`,
    hiddenLabel: `${hiddenCount} hidden`,
    hasHidden: hiddenCount > 0
  };
}

function buildWorkflowTaskPanel({
  reporting,
  rows,
  packTargets,
  selectedProfile,
  selectedProfileActions,
  pathname
}: {
  reporting: GovernanceReportingSummary;
  rows: ReportingProfileRow[];
  packTargets: ReportingPackTargetRow[];
  selectedProfile: GovernanceReportingProfile | null;
  selectedProfileActions: ReportingProfileAction[];
  pathname: string;
}): ReportingWorkflowTaskPanel | null {
  if (!isReportPackRoute(pathname)) {
    return null;
  }

  const readyProfiles = reporting.profiles.filter((profile) => profile.loaderScript && profile.dataDictionary).length;
  const statusTone: ReportingWorkflowTaskPanel["statusTone"] =
    packTargets.length === 0 ? "warning" : readyProfiles > 0 ? "success" : "muted";
  const statusLabel =
    packTargets.length === 0
      ? "Recipients missing"
      : readyProfiles > 0
        ? "Approval-ready"
        : "Evidence review";
  const evidenceBundleReportId = resolveEvidenceBundleReportId(reporting.workflowRecords ?? []);

  return {
    regionLabel: "Report-pack approval task",
    eyebrow: "Workflow task",
    title: "Report-pack approval",
    description:
      "Review report-pack recipients, pick the export profile that carries the packet evidence, then preview or run the backend export before approval.",
    statusLabel,
    statusTone,
    statusVariant: workflowStatusVariant(statusTone),
    chips: [
      { label: "Recipients", value: String(packTargets.length) },
      { label: "Profiles", value: String(reporting.profiles.length) },
      { label: "Ready profiles", value: String(readyProfiles) },
      { label: "Backend", value: EXPORT_API_ENDPOINTS.reportPacks }
    ],
    targetsLabel: "Report-pack distribution recipients",
    targets: packTargets,
    hasTargets: packTargets.length > 0,
    targetsEmptyText: "No report-pack recipients loaded. Configure distribution records before approving this packet.",
    targetsEmptyAriaLabel: "No report-pack distribution recipients",
    profileListLabel: "Report-pack export profiles",
    profileKeyboardHelpId: REPORT_PACK_PROFILE_KEYBOARD_HELP_ID,
    profileKeyboardHelpText: "Use arrow keys, Home, and End to move between report-pack profiles.",
    selectedProfileId: selectedProfile?.id ?? null,
    profiles: rows.map((row, index) => {
      const profile = reporting.profiles.find((item) => item.id === row.id);
      const loaderReady = profile?.loaderScript === true;
      const dictionaryReady = profile?.dataDictionary === true;
      const readinessTone: ReportingWorkflowProfileRow["readinessTone"] =
        loaderReady && dictionaryReady ? "success" : loaderReady || dictionaryReady ? "warning" : "muted";
      const readinessLabel =
        loaderReady && dictionaryReady
          ? "Packet evidence ready"
          : loaderReady
            ? "Loader only"
          : dictionaryReady
            ? "Dictionary only"
            : "Evidence missing";
      const isSelected = row.isSelected;

      return {
        id: row.id,
        name: row.name,
        summary: `${row.formatLabel} for ${row.targetLabel}`,
        readinessLabel,
        readinessTone,
        readinessVariant: workflowStatusVariant(readinessTone),
        isSelected,
        isExpanded: isSelected,
        controlsId: `${REPORT_PACK_PROFILE_SUMMARY_ID} ${REPORT_PACK_PROFILE_ACTIONS_ID} ${REPORT_PACK_PROFILE_BACKEND_ID}`,
        descriptionId: `report-pack-profile-${sanitizeDomId(row.id)}-description`,
        tabIndex: isSelected || (!selectedProfile && index === 0) ? 0 : -1,
        selectAriaLabel: `Select ${row.name} for report-pack approval`
      };
    }),
    hasProfiles: rows.length > 0,
    profilesEmptyText: "No export profiles are configured. Add a governed profile before report-pack approval.",
    profilesEmptyAriaLabel: "No report-pack export profiles",
    selectedSummary: selectedProfile
      ? `${selectedProfile.name} is selected for report-pack approval using ${selectedProfile.format} output to ${selectedProfile.targetTool}.`
      : "Select a profile to enable packet preview and export actions.",
    selectedSummaryId: REPORT_PACK_PROFILE_SUMMARY_ID,
    actionListLabel: "Selected report-pack export actions",
    actionPanelId: REPORT_PACK_PROFILE_ACTIONS_ID,
    actions: selectedProfileActions,
    hasActions: selectedProfileActions.length > 0,
    actionsEmptyText: "Select a report-pack profile before previewing or running export analysis.",
    actionsEmptyAriaLabel: "No selected report-pack export actions",
    backendLinksLabel: "Report-pack backend endpoints",
    backendPanelId: REPORT_PACK_PROFILE_BACKEND_ID,
    publicationReview: buildPublicationReviewPanel(reporting.workflowRecords ?? []),
    restatementReview: buildRestatementReviewPanel(reporting.workflowRecords ?? []),
    backendLinks: [
      buildWorkflowBackendLink({
        id: "report-pack-catalog",
        method: "GET",
        label: "Report-pack catalog",
        href: EXPORT_API_ENDPOINTS.reportPacks
      }),
      buildWorkflowBackendLink({
        id: "evidence-bundle",
        method: "GET",
        label: evidenceBundleReportId ? "Evidence bundle export" : "Evidence bundle route",
        href: reportPackEvidenceBundleEndpoint(evidenceBundleReportId ?? undefined)
      }),
      buildWorkflowBackendLink({
        id: "export-preview",
        method: "GET",
        label: selectedProfile ? `${selectedProfile.name} export preview` : "Export preview",
        href: exportPreviewEndpoint(selectedProfile?.id)
      }),
      buildWorkflowBackendLink({
        id: "export-run",
        method: "POST",
        label: selectedProfile ? `${selectedProfile.name} export analysis` : "Run export",
        href: EXPORT_API_ENDPOINTS.analysis
      })
    ]
  };
}

function resolveEvidenceBundleReportId(records: ReportingWorkflowRecord[]): string | null {
  const selected = [...records].sort((left, right) => right.updatedAt.localeCompare(left.updatedAt))[0];
  return selected && isGuid(selected.reportId) ? selected.reportId : null;
}

function isGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

export function resolveReportPackProfileKeyCommand(key: string): ReportingProfileKeyCommand | null {
  if (key === "ArrowRight" || key === "ArrowDown") {
    return "next";
  }

  if (key === "ArrowLeft" || key === "ArrowUp") {
    return "previous";
  }

  if (key === "Home") {
    return "first";
  }

  if (key === "End") {
    return "last";
  }

  return null;
}

function adjacentReportPackProfileId(
  profiles: GovernanceReportingProfile[],
  activeId: string | null,
  direction: ReportingProfileKeyCommand
): string | null {
  if (profiles.length === 0) {
    return null;
  }

  if (direction === "first") {
    return profiles[0].id;
  }

  if (direction === "last") {
    return profiles[profiles.length - 1].id;
  }

  const currentIndex = Math.max(0, profiles.findIndex((profile) => profile.id === activeId));
  const nextIndex = direction === "next"
    ? (currentIndex + 1) % profiles.length
    : (currentIndex - 1 + profiles.length) % profiles.length;

  return profiles[nextIndex].id;
}

function buildWorkflowBackendLink({
  id,
  method,
  label,
  href
}: {
  id: string;
  method: ReportingWorkflowBackendLink["method"];
  label: string;
  href: string;
}): ReportingWorkflowBackendLink {
  const isBrowserNavigable = method === "GET" && !href.includes("{");

  return {
    id,
    method,
    label,
    href,
    isBrowserNavigable,
    interactionLabel: isBrowserNavigable ? "Open" : "Reference",
    ariaLabel: isBrowserNavigable
      ? `${method} ${href} for ${label}`
      : `Reference-only ${method} ${href} for ${label}`
  };
}

function isReportPackRoute(pathname: string): boolean {
  const normalized = pathname.split(/[?#]/)[0]?.replace(/\/+$/, "") || WORKSTATION_ROUTE_CATALOG.reporting;
  return normalized === WORKSTATION_ROUTE_CATALOG.reportingReportPacks;
}

function defaultReportPackProfileId(reporting: GovernanceReportingSummary, pathname: string): string | null {
  if (!isReportPackRoute(pathname) || reporting.profiles.length === 0) {
    return null;
  }

  const recommended = new Set(reporting.recommendedProfiles);
  let bestProfile = reporting.profiles[0];
  let bestScore = reportPackProfileScore(bestProfile, recommended);
  for (let index = 1; index < reporting.profiles.length; index += 1) {
    const profile = reporting.profiles[index];
    const score = reportPackProfileScore(profile, recommended);
    if (score > bestScore) {
      bestProfile = profile;
      bestScore = score;
    }
  }

  return bestProfile?.id ?? null;
}

function reportPackProfileScore(profile: GovernanceReportingProfile, recommended: ReadonlySet<string>): number {
  return (recommended.has(profile.id) ? 4 : 0)
    + (profile.loaderScript ? 2 : 0)
    + (profile.dataDictionary ? 2 : 0);
}

function buildProfileReadinessSummary(profile: GovernanceReportingProfile): string {
  if (profile.loaderScript && profile.dataDictionary) {
    return "Loader and dictionary evidence are ready for governed packet generation.";
  }

  if (profile.loaderScript) {
    return "Loader evidence is present. Attach the data dictionary before external review.";
  }

  if (profile.dataDictionary) {
    return "Data dictionary is present. Loader automation is not attached to this profile.";
  }

  return "Profile can be previewed, but loader and dictionary evidence are not attached.";
}

function buildProfileActions(
  profile: GovernanceReportingProfile,
  runningProfileId: string | null,
  surface: ReportingProfileActionSurface = "profile-detail"
): ReportingProfileAction[] {
  const isRunningThisProfile = runningProfileId === profile.id;
  const runningReason = isRunningThisProfile ? `${profile.name} export is already running.` : null;
  const evidenceDisabledReason = buildProfileExportEvidenceDisabledReason(profile);
  const runDisabledReason = runningReason ?? evidenceDisabledReason;
  const runStatusText = isRunningThisProfile
    ? `${profile.name} export is running. Wait for the result before starting another export.`
    : evidenceDisabledReason
      ? evidenceDisabledReason
      : "Runs the governed export through the backend mutation and reports generated artifacts here.";

  return [
    {
      id: "preview",
      label: "Preview payload",
      href: exportPreviewEndpoint(profile.id),
      variant: "outline",
      ariaLabel: `Preview ${profile.name} export payload`,
      describedById: buildProfileActionDescriptionId(profile.id, "preview", surface),
      statusText: "Opens the current export payload preview in a new browser tab.",
      descriptionText: "Opens the current export payload preview in a new browser tab.",
      statusBadgeLabel: "GET",
      statusBadgeAriaLabel: `${profile.name} export preview uses GET`,
      statusBadgeVariant: "outline",
      isDisabled: false,
      disabledReason: null,
      busyLabel: null,
      method: "GET",
      profileId: profile.id,
      isRunning: false
    },
    {
      id: "run",
      label: isRunningThisProfile ? "Running export…" : "Run export",
      href: EXPORT_API_ENDPOINTS.analysis,
      variant: "default",
      ariaLabel: evidenceDisabledReason
        ? `Run ${profile.name} export analysis unavailable until required evidence is attached`
        : `Run ${profile.name} export analysis`,
      describedById: buildProfileActionDescriptionId(profile.id, "run", surface),
      statusText: runStatusText,
      descriptionText: runStatusText,
      statusBadgeLabel: isRunningThisProfile ? "Running" : evidenceDisabledReason ? "Gated" : "POST",
      statusBadgeAriaLabel: isRunningThisProfile
        ? `${profile.name} export is running`
        : evidenceDisabledReason
          ? `${profile.name} export analysis is gated by missing evidence`
          : `${profile.name} export analysis uses POST`,
      statusBadgeVariant: isRunningThisProfile || evidenceDisabledReason ? "warning" : "outline",
      isDisabled: runDisabledReason !== null,
      disabledReason: runDisabledReason,
      busyLabel: isRunningThisProfile ? "Running export…" : null,
      method: "POST",
      profileId: profile.id,
      isRunning: isRunningThisProfile
    }
  ];
}

function buildProfileActionDescriptionId(
  profileId: string,
  actionId: ReportingProfileAction["id"],
  surface: ReportingProfileActionSurface
): string {
  return `reporting-action-${sanitizeDomId(profileId)}-${actionId}-${surface}-status`;
}

function buildProfileExportEvidenceDisabledReason(profile: GovernanceReportingProfile): string | null {
  const missing: string[] = [];
  if (!profile.loaderScript) {
    missing.push("loader automation");
  }

  if (!profile.dataDictionary) {
    missing.push("data dictionary");
  }

  if (missing.length === 0) {
    return null;
  }

  const missingLabel = missing.length === 1
    ? missing[0]
    : `${missing.slice(0, -1).join(", ")} and ${missing[missing.length - 1]}`;
  return `${profile.name} export requires ${missingLabel} evidence before running a governed POST export. Preview remains available.`;
}

export function buildExportStatusStarting(profileName: string): ReportingExportStatusState {
  return {
    text: `Starting ${profileName} export…`,
    tone: "default",
    className: exportStatusToneClass("default"),
    ariaLabel: "Reporting export status",
    fields: [
      buildReportingDetailField("Profile", profileName, "default"),
      buildReportingDetailField("State", "Running", "warning")
    ],
    warnings: [],
    artifacts: []
  };
}

export function buildExportStatusResult(
  requestedProfileId: string,
  profileName: string,
  result: ExportAnalysisResult
): ReportingExportStatusState {
  const text = result.success
    ? `${profileName} export completed — ${result.filesGenerated} file${result.filesGenerated === 1 ? "" : "s"} generated.`
    : `${profileName} export failed. ${result.error ?? "No error detail returned."}`;

  return {
    text,
    tone: result.success ? "success" : "danger",
    className: exportStatusToneClass(result.success ? "success" : "danger"),
    ariaLabel: "Reporting export status",
    fields: buildExportResultFields(requestedProfileId, result),
    warnings: buildExportResultWarnings(requestedProfileId, result),
    artifacts: buildExportArtifactFields(result)
  };
}

export function buildExportStatusFailure(profileName: string, error: unknown): ReportingExportStatusState {
  const fallback = `${profileName} export failed without backend detail.`;
  const detail = describeApiError(error, fallback);

  return {
    text: `${profileName} export failed. ${detail.summary}`,
    tone: "danger",
    className: exportStatusToneClass("danger"),
    ariaLabel: "Reporting export status",
    fields: [
      buildReportingDetailField("Profile", profileName, "default"),
      buildReportingDetailField("Failure", detail.summary, "warning")
    ],
    warnings: detail.details,
    artifacts: []
  };
}

function isAbortError(error: unknown): boolean {
  return (error instanceof DOMException || error instanceof Error) && error.name === "AbortError";
}

function buildExportResultFields(requestedProfileId: string, result: ExportAnalysisResult): ReportingDetailField[] {
  const byteLabel = formatBytes(result.totalBytes);
  const durationLabel = `${result.durationSeconds.toLocaleString(undefined, { maximumFractionDigits: 2 })}s`;
  const symbolsLabel = result.symbols?.length ? result.symbols.join(", ") : "All configured symbols";

  return [
    buildReportingDetailField("Job ID", result.jobId ?? "Unavailable", result.jobId ? "default" : "muted"),
    buildReportingDetailField("Status", result.status, result.success ? "success" : "warning"),
    buildReportingDetailField("Requested", requestedProfileId, "default"),
    buildReportingDetailField("Profile", result.profileId, "default"),
    buildReportingDetailField("Symbols", symbolsLabel, result.symbols?.length ? "default" : "muted"),
    buildReportingDetailField("Output", result.outputDirectory ?? "Unavailable", result.outputDirectory ? "default" : "muted"),
    buildReportingDetailField("Files", String(result.filesGenerated), result.filesGenerated > 0 ? "success" : "warning"),
    buildReportingDetailField("Records", result.totalRecords.toLocaleString(), result.totalRecords > 0 ? "default" : "muted"),
    buildReportingDetailField("Bytes", byteLabel, result.totalBytes > 0 ? "default" : "muted"),
    buildReportingDetailField("Duration", durationLabel, "muted"),
    buildReportingDetailField("Timestamp", result.timestamp, "muted")
  ];
}

function buildExportResultWarnings(requestedProfileId: string, result: ExportAnalysisResult): string[] {
  const warnings = [...(result.warnings ?? [])];
  if (result.profileId && requestedProfileId && result.profileId !== requestedProfileId) {
    warnings.unshift(`Requested profile ${requestedProfileId} resolved as ${result.profileId}.`);
  }

  return warnings;
}

function buildExportArtifactFields(result: ExportAnalysisResult): ReportingDetailField[] {
  return (result.files ?? []).map((file) =>
    buildReportingDetailField(
      file.symbol ? `${file.symbol} ${file.format ?? "file"}` : file.format ?? "File",
      `${file.path} · ${file.recordCount.toLocaleString()} records · ${formatBytes(file.sizeBytes)}`,
      file.recordCount > 0 ? "default" : "warning"
    )
  );
}

function buildReportingBadge(label: string, tone: ReportingProfileBadgeTone): ReportingProfileBadge {
  return {
    label,
    tone,
    variant: badgeVariant(tone)
  };
}

function buildReportingDetailField(
  label: string,
  value: string,
  tone: ReportingDetailFieldTone
): ReportingDetailField {
  return {
    label,
    value,
    tone,
    className: fieldToneClass(tone)
  };
}

function badgeVariant(tone: ReportingProfileBadgeTone): ReportingBadgeVariant {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "muted") return "outline";
  return "default";
}

function workflowStatusVariant(tone: ReportingWorkflowTone): Exclude<ReportingBadgeVariant, "default"> {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  return "outline";
}

function fieldToneClass(tone: ReportingDetailFieldTone): ReportingFieldClassName {
  if (tone === "success") return "text-success";
  if (tone === "warning") return "text-warning";
  if (tone === "muted") return "text-muted-foreground";
  return "text-foreground";
}

function exportStatusToneClass(tone: ReportingExportStatusTone): ReportingExportStatusClassName {
  if (tone === "success") return "border-success/30 bg-success/10 text-success";
  if (tone === "danger") return "border-danger/35 bg-danger/10 text-danger";
  return "border-border/70 bg-secondary/25 text-muted-foreground";
}

function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value <= 0) {
    return "0 B";
  }

  const units = ["B", "KB", "MB", "GB"];
  let amount = value;
  let unitIndex = 0;

  while (amount >= 1024 && unitIndex < units.length - 1) {
    amount /= 1024;
    unitIndex += 1;
  }

  return `${amount.toLocaleString(undefined, { maximumFractionDigits: amount >= 10 ? 1 : 2 })} ${units[unitIndex]}`;
}

function sanitizeDomId(value: string): string {
  const normalized = value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9_-]+/g, "-")
    .replace(/^-+|-+$/g, "");

  return normalized || "profile";
}
