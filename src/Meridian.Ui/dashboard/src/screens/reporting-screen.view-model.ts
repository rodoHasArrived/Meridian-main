import { useEffect, useRef, useState } from "react";
import { runAnalysisExport } from "@/lib/api";
import { EXPORT_API_ENDPOINTS, exportPreviewEndpoint } from "@/lib/workstation-endpoints";
import { evidenceWorkbenchPath } from "@/lib/workspace";
import type { ExportAnalysisResult, GovernanceReportingProfile, GovernanceReportingSummary } from "@/types";

export type ReportingProfileBadgeTone = "primary" | "success" | "warning" | "muted";
export type ReportingBadgeVariant = "default" | "success" | "warning" | "outline";
export type ReportingWorkflowTone = "success" | "warning" | "muted";
export type ReportingDetailFieldTone = "default" | "success" | "warning" | "muted";
export type ReportingExportStatusTone = "default" | "success" | "danger";
export type ReportingFieldClassName = "text-foreground" | "text-success" | "text-warning" | "text-muted-foreground";
export type ReportingExportStatusClassName =
  | "border-border/70 bg-secondary/25 text-muted-foreground"
  | "border-success/30 bg-success/10 text-success"
  | "border-danger/35 bg-danger/10 text-danger";

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
  isDisabled: boolean;
  disabledReason: string | null;
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
  runExport: (profileId: string) => Promise<ExportAnalysisResult>;
}

export interface ReportingPackTargetRow {
  id: string;
  label: string;
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
  selectAriaLabel: string;
}

export interface ReportingWorkflowBackendLink {
  id: string;
  method: "GET" | "POST";
  label: string;
  href: string;
  ariaLabel: string;
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
  profiles: ReportingWorkflowProfileRow[];
  hasProfiles: boolean;
  profilesEmptyText: string;
  profilesEmptyAriaLabel: string;
  selectedSummary: string;
  backendLinksLabel: string;
  backendLinks: ReportingWorkflowBackendLink[];
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
}

const defaultReportingExportServices: ReportingExportServices = {
  runExport: (profileId) => runAnalysisExport(profileId)
};

export function useReportingScreenViewModel(
  reporting: GovernanceReportingSummary | null,
  services: ReportingExportServices = defaultReportingExportServices,
  pathname = "/reporting"
): ReportingScreenViewModel {
  const [selectedId, setSelectedId] = useState<string | null | undefined>(undefined);
  const [runningProfileId, setRunningProfileId] = useState<string | null>(null);
  const [exportStatus, setExportStatus] = useState<ReportingExportStatusState | null>(null);
  const exportCommandRevisionRef = useRef(0);
  const detailId = "reporting-profile-detail";

  useEffect(() => () => {
    exportCommandRevisionRef.current += 1;
  }, []);

  const selectProfile = (id: string) => {
    exportCommandRevisionRef.current += 1;
    setSelectedId((prev) => {
      const defaultProfileId = reporting ? defaultReportPackProfileId(reporting, pathname) : null;
      const activeId = prev === undefined ? defaultProfileId : prev;
      return activeId === id ? null : id;
    });
    setRunningProfileId(null);
    setExportStatus(null);
  };

  const runExportCommand = async (profileId: string, profileName: string) => {
    const commandRevision = exportCommandRevisionRef.current + 1;
    exportCommandRevisionRef.current = commandRevision;
    setRunningProfileId(profileId);
    setExportStatus(buildExportStatusStarting(profileName));

    try {
      const result = await services.runExport(profileId);
      if (exportCommandRevisionRef.current !== commandRevision) {
        return;
      }
      setExportStatus(buildExportStatusResult(profileId, profileName, result));
    } catch (error) {
      if (exportCommandRevisionRef.current !== commandRevision) {
        return;
      }
      setExportStatus(buildExportStatusFailure(profileName, error));
    } finally {
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
      detailId,
      statusTitle: "No profile selected",
      statusDetail: "Reporting data is unavailable. Check the Reporting workspace API connection.",
      statusBadgeLabel: "Waiting",
      statusBadgeVariant: "outline",
      nextAction: "—",
      selectedProfile: null,
      packTargets: [],
      hasPackTargets: false,
      packTargetsSummary: "No report-pack targets configured.",
      packTargetsListLabel: "Report-pack targets",
      packTargetsEmptyState: buildPackTargetsEmptyState(),
      workflowTaskPanel: null,
      loadingState: buildLoadingState(pathname),
      loadingTitle: "Loading Reporting",
      loadingDetail: "Waiting for governed report-pack and export evidence.",
      exportStatus,
      runningProfileId,
      runExport: runExportCommand,
      selectProfile
    };
  }

  const profiles = reporting.profiles;
  const recommended = new Set(reporting.recommendedProfiles);
  const defaultSelectedId = defaultReportPackProfileId(reporting, pathname);
  const effectiveSelectedId = selectedId === undefined ? defaultSelectedId : selectedId;
  const selectedProfileData: GovernanceReportingProfile | null =
    profiles.find((p) => p.id === effectiveSelectedId) ?? null;

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
        actions: buildProfileActions(selectedProfileData, runningProfileId)
      }
    : null;

  const countLabel = `${profiles.length} profile${profiles.length === 1 ? "" : "s"}`;
  const packCount = reporting.reportPackTargets.length;
  const packTargetCountLabel = String(packCount);
  const recommendedCountLabel = String(reporting.recommendedProfiles.length);
  const visibleCountLabel = `${rows.length} of ${profiles.length}`;
  const statusTitle = selectedProfileData ? `${selectedProfileData.name} selected` : "No profile selected";
  const listLabel = "Export profiles";
  const packTargets: ReportingPackTargetRow[] = reporting.reportPackTargets.map((target) => ({
    id: target,
    label: target,
    ariaLabel: `${target} report-pack target`
  }));
  const workflowTaskPanel = buildWorkflowTaskPanel({
    reporting,
    rows,
    packTargets,
    selectedProfile: selectedProfileData,
    pathname
  });

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
        ? `${packCount} report-pack target${packCount === 1 ? "" : "s"} configured.`
        : "No report-pack targets configured.",
    packTargetsListLabel: "Report-pack targets",
    packTargetsEmptyState: buildPackTargetsEmptyState(),
    workflowTaskPanel,
    loadingState: buildLoadingState(pathname),
    loadingTitle: "Loading Reporting",
    loadingDetail: "Waiting for governed report-pack and export evidence.",
    exportStatus,
    runningProfileId,
    runExport: runExportCommand,
    selectProfile
  };
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
    text: "No report-pack targets loaded. Configure governed targets in the governance policy before approving this packet.",
    ariaLabel: "No report-pack targets loaded"
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
    { label: "Pack targets", value: packTargetCountLabel },
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
    { label: "Targets", value: packTargetCountLabel },
    { label: "List", value: listLabel }
  ];
}

function buildPackTargetChips(
  packTargetCountLabel: string,
  statusTitle: string
): ReportingChipViewModel[] {
  return [
    { label: "Visible", value: packTargetCountLabel },
    { label: "Inspector", value: statusTitle }
  ];
}

function buildWorkflowTaskPanel({
  reporting,
  rows,
  packTargets,
  selectedProfile,
  pathname
}: {
  reporting: GovernanceReportingSummary;
  rows: ReportingProfileRow[];
  packTargets: ReportingPackTargetRow[];
  selectedProfile: GovernanceReportingProfile | null;
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
      ? "Targets missing"
      : readyProfiles > 0
        ? "Approval-ready"
        : "Evidence review";

  return {
    regionLabel: "Report-pack approval task",
    eyebrow: "Workflow task",
    title: "Report-pack approval",
    description:
      "Review loaded report-pack targets, pick the export profile that carries the packet evidence, then preview or run the backend export before approval.",
    statusLabel,
    statusTone,
    statusVariant: workflowStatusVariant(statusTone),
    chips: [
      { label: "Targets", value: String(packTargets.length) },
      { label: "Profiles", value: String(reporting.profiles.length) },
      { label: "Ready profiles", value: String(readyProfiles) },
      { label: "Backend", value: EXPORT_API_ENDPOINTS.reportPacks }
    ],
    targetsLabel: "Report-pack approval targets",
    targets: packTargets,
    hasTargets: packTargets.length > 0,
    targetsEmptyText: "No report-pack targets loaded. Configure governed targets before approving this packet.",
    targetsEmptyAriaLabel: "No report-pack approval targets",
    profileListLabel: "Report-pack export profiles",
    profiles: rows.map((row) => {
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

      return {
        id: row.id,
        name: row.name,
        summary: `${row.formatLabel} for ${row.targetLabel}`,
        readinessLabel,
        readinessTone,
        readinessVariant: workflowStatusVariant(readinessTone),
        isSelected: row.isSelected,
        selectAriaLabel: `Select ${row.name} for report-pack approval`
      };
    }),
    hasProfiles: rows.length > 0,
    profilesEmptyText: "No export profiles are configured. Add a governed profile before report-pack approval.",
    profilesEmptyAriaLabel: "No report-pack export profiles",
    selectedSummary: selectedProfile
      ? `${selectedProfile.name} is selected for report-pack approval using ${selectedProfile.format} output to ${selectedProfile.targetTool}.`
      : "Select a profile to enable packet preview and export actions.",
    backendLinksLabel: "Report-pack backend endpoints",
    backendLinks: [
      {
        id: "report-pack-catalog",
        method: "GET",
        label: "Report-pack catalog",
        href: EXPORT_API_ENDPOINTS.reportPacks,
        ariaLabel: "Open report-pack catalog backend endpoint"
      },
      {
        id: "export-preview",
        method: "GET",
        label: "Export preview",
        href: exportPreviewEndpoint(selectedProfile?.id),
        ariaLabel: selectedProfile
          ? `Preview ${selectedProfile.name} export payload`
          : "Open export preview backend endpoint"
      },
      {
        id: "export-run",
        method: "POST",
        label: "Run export",
        href: EXPORT_API_ENDPOINTS.analysis,
        ariaLabel: selectedProfile
          ? `Run ${selectedProfile.name} export analysis`
          : "Run export analysis backend endpoint"
      }
    ]
  };
}

function isReportPackRoute(pathname: string): boolean {
  const normalized = pathname.split(/[?#]/)[0]?.replace(/\/+$/, "") || "/reporting";
  return normalized === "/reporting/report-packs";
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
  runningProfileId: string | null
): ReportingProfileAction[] {
  const isRunningThisProfile = runningProfileId === profile.id;

  return [
    {
      id: "preview",
      label: "Preview payload",
      href: exportPreviewEndpoint(profile.id),
      variant: "outline",
      ariaLabel: `Preview ${profile.name} export payload`,
      describedById: `reporting-action-${profile.id}-preview-status`,
      statusText: "Opens the current export payload preview in a new browser tab.",
      isDisabled: false,
      disabledReason: null,
      method: "GET",
      profileId: profile.id,
      isRunning: false
    },
    {
      id: "run",
      label: isRunningThisProfile ? "Running export…" : "Run export",
      href: EXPORT_API_ENDPOINTS.analysis,
      variant: "default",
      ariaLabel: `Run ${profile.name} export analysis`,
      describedById: `reporting-action-${profile.id}-run-status`,
      statusText: isRunningThisProfile
        ? `${profile.name} export is running. Wait for the result before starting another export.`
        : "Runs the governed export through the backend mutation and reports generated artifacts here.",
      isDisabled: isRunningThisProfile,
      disabledReason: isRunningThisProfile ? `${profile.name} export is already running.` : null,
      method: "POST",
      profileId: profile.id,
      isRunning: isRunningThisProfile
    }
  ];
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
  const message = error instanceof Error ? error.message : "Unknown export error.";

  return {
    text: `${profileName} export failed. ${message}`,
    tone: "danger",
    className: exportStatusToneClass("danger"),
    ariaLabel: "Reporting export status",
    fields: [
      buildReportingDetailField("Profile", profileName, "default"),
      buildReportingDetailField("Failure", message, "warning")
    ],
    warnings: [],
    artifacts: []
  };
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
