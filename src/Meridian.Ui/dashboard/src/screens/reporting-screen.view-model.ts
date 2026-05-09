import { useEffect, useRef, useState } from "react";
import { runAnalysisExport } from "@/lib/api";
import type { ExportAnalysisResult, GovernanceReportingProfile, GovernanceReportingSummary } from "@/types";

export type ReportingProfileBadgeTone = "primary" | "success" | "warning" | "muted";

export interface ReportingProfileRow {
  id: string;
  name: string;
  targetLabel: string;
  formatLabel: string;
  description: string;
  isSelected: boolean;
  isRecommended: boolean;
  badges: Array<{ label: string; tone: ReportingProfileBadgeTone }>;
  selectAriaLabel: string;
}

export interface ReportingProfileAction {
  id: "preview" | "run";
  label: string;
  href: string;
  variant: "default" | "outline";
  ariaLabel: string;
  isDisabled: boolean;
  disabledReason: string | null;
  method: "GET" | "POST";
  profileId: string;
  isRunning: boolean;
}

export interface ReportingDetailField {
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "muted";
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
  tone: "default" | "success" | "danger";
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

export interface ReportingChipViewModel {
  label: string;
  value: string;
}

export interface ReportingWorkflowProfileRow {
  id: string;
  name: string;
  summary: string;
  readinessLabel: string;
  readinessTone: "success" | "warning" | "muted";
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
  statusTone: "success" | "warning" | "muted";
  chips: ReportingChipViewModel[];
  targetsLabel: string;
  targets: ReportingPackTargetRow[];
  profileListLabel: string;
  profiles: ReportingWorkflowProfileRow[];
  selectedSummary: string;
  backendLinksLabel: string;
  backendLinks: ReportingWorkflowBackendLink[];
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
  workbenchChips: ReportingChipViewModel[];
  queueChips: ReportingChipViewModel[];
  packTargetChips: ReportingChipViewModel[];
  detailId: string;
  statusTitle: string;
  statusDetail: string;
  nextAction: string;
  selectedProfile: ReportingDetailViewModel | null;
  packTargets: ReportingPackTargetRow[];
  hasPackTargets: boolean;
  packTargetsSummary: string;
  packTargetsListLabel: string;
  workflowTaskPanel: ReportingWorkflowTaskPanel | null;
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
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [runningProfileId, setRunningProfileId] = useState<string | null>(null);
  const [exportStatus, setExportStatus] = useState<ReportingExportStatusState | null>(null);
  const exportCommandRevisionRef = useRef(0);

  useEffect(() => () => {
    exportCommandRevisionRef.current += 1;
  }, []);

  const selectProfile = (id: string) => {
    exportCommandRevisionRef.current += 1;
    setSelectedId((prev) => (prev === id ? null : id));
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
      workbenchChips: buildWorkbenchChips("0 profiles", "0", "0"),
      queueChips: buildQueueChips("0 visible", "0", "0", "Export profiles"),
      packTargetChips: buildPackTargetChips("0", "No profile selected"),
      detailId: "reporting-profile-detail",
      statusTitle: "No profile selected",
      statusDetail: "Reporting data is unavailable. Check the Governance workspace or API connection.",
      nextAction: "—",
      selectedProfile: null,
      packTargets: [],
      hasPackTargets: false,
      packTargetsSummary: "No report-pack targets configured.",
      packTargetsListLabel: "Report-pack targets",
      workflowTaskPanel: null,
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
  const selectedProfileData: GovernanceReportingProfile | null =
    profiles.find((p) => p.id === selectedId) ?? null;

  const rows: ReportingProfileRow[] = profiles.map((p) => ({
    id: p.id,
    name: p.name,
    targetLabel: p.targetTool,
    formatLabel: p.format,
    description: p.description,
    isSelected: p.id === selectedId,
    isRecommended: recommended.has(p.id),
    selectAriaLabel: `Select ${p.name} export profile`,
    badges: [
      ...(recommended.has(p.id) ? [{ label: "Recommended", tone: "primary" as const }] : []),
      ...(p.loaderScript ? [{ label: "Loader", tone: "success" as const }] : []),
      ...(p.dataDictionary ? [{ label: "Dictionary", tone: "success" as const }] : [])
    ]
  }));

  const detail: ReportingDetailViewModel | null = selectedProfileData
    ? {
        id: selectedProfileData.id,
        title: selectedProfileData.name,
        subtitle: `${selectedProfileData.format} · ${selectedProfileData.targetTool}`,
        description: selectedProfileData.description,
        fields: [
          { label: "Profile ID", value: selectedProfileData.id, tone: "default" },
          { label: "Format", value: selectedProfileData.format, tone: "default" },
          { label: "Target", value: selectedProfileData.targetTool, tone: "default" },
          {
            label: "Loader script",
            value: selectedProfileData.loaderScript ? "Included" : "Not included",
            tone: selectedProfileData.loaderScript ? "success" : "muted"
          },
          {
            label: "Data dictionary",
            value: selectedProfileData.dataDictionary ? "Included" : "Not included",
            tone: selectedProfileData.dataDictionary ? "success" : "muted"
          }
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
    workbenchChips: buildWorkbenchChips(countLabel, packTargetCountLabel, recommendedCountLabel),
    queueChips: buildQueueChips(visibleCountLabel, recommendedCountLabel, packTargetCountLabel, listLabel),
    packTargetChips: buildPackTargetChips(packTargetCountLabel, statusTitle),
    detailId: "reporting-profile-detail",
    statusTitle,
    statusDetail: selectedProfileData
      ? `${selectedProfileData.name} routes ${selectedProfileData.format} output to ${selectedProfileData.targetTool}.`
      : "Select a profile to inspect export evidence and ready-state.",
    nextAction: selectedProfileData
      ? `POST /api/export/analysis · GET /api/export/preview?profile=${selectedProfileData.id}`
      : `${profiles.length} profile${profiles.length === 1 ? "" : "s"} on desk. Select one to inspect export evidence.`,
    selectedProfile: detail,
    packTargets,
    hasPackTargets: packCount > 0,
    packTargetsSummary:
      packCount > 0
        ? `${packCount} report-pack target${packCount === 1 ? "" : "s"} configured.`
        : "No report-pack targets configured.",
    packTargetsListLabel: "Report-pack targets",
    workflowTaskPanel,
    loadingTitle: "Loading Reporting",
    loadingDetail: "Waiting for governed report-pack and export evidence.",
    exportStatus,
    runningProfileId,
    runExport: runExportCommand,
    selectProfile
  };
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
    { label: "Export route", value: "/api/export/analysis" }
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
    chips: [
      { label: "Targets", value: String(packTargets.length) },
      { label: "Profiles", value: String(reporting.profiles.length) },
      { label: "Ready profiles", value: String(readyProfiles) },
      { label: "Backend", value: "/api/fund-structure/report-packs" }
    ],
    targetsLabel: "Report-pack approval targets",
    targets: packTargets,
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
        isSelected: row.isSelected,
        selectAriaLabel: `Select ${row.name} for report-pack approval`
      };
    }),
    selectedSummary: selectedProfile
      ? `${selectedProfile.name} is selected for report-pack approval using ${selectedProfile.format} output to ${selectedProfile.targetTool}.`
      : "Select a profile to enable packet preview and export actions.",
    backendLinksLabel: "Report-pack backend endpoints",
    backendLinks: [
      {
        id: "report-pack-catalog",
        method: "GET",
        label: "Report-pack catalog",
        href: "/api/fund-structure/report-packs",
        ariaLabel: "Open report-pack catalog backend endpoint"
      },
      {
        id: "export-preview",
        method: "GET",
        label: "Export preview",
        href: selectedProfile ? `/api/export/preview?profile=${encodeURIComponent(selectedProfile.id)}` : "/api/export/preview",
        ariaLabel: selectedProfile
          ? `Preview ${selectedProfile.name} export payload`
          : "Open export preview backend endpoint"
      },
      {
        id: "export-run",
        method: "POST",
        label: "Run export",
        href: "/api/export/analysis",
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
  const profileQuery = `profile=${encodeURIComponent(profile.id)}`;
  const isRunningThisProfile = runningProfileId === profile.id;

  return [
    {
      id: "preview",
      label: "Preview payload",
      href: `/api/export/preview?${profileQuery}`,
      variant: "outline",
      ariaLabel: `Preview ${profile.name} export payload`,
      isDisabled: false,
      disabledReason: null,
      method: "GET",
      profileId: profile.id,
      isRunning: false
    },
    {
      id: "run",
      label: isRunningThisProfile ? "Running export…" : "Run export",
      href: "/api/export/analysis",
      variant: "default",
      ariaLabel: `Run ${profile.name} export analysis`,
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
    ariaLabel: "Reporting export status",
    fields: [
      { label: "Profile", value: profileName, tone: "default" },
      { label: "State", value: "Running", tone: "warning" }
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
    ariaLabel: "Reporting export status",
    fields: [
      { label: "Profile", value: profileName, tone: "default" },
      { label: "Failure", value: message, tone: "warning" }
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
    { label: "Job ID", value: result.jobId ?? "Unavailable", tone: result.jobId ? "default" : "muted" },
    { label: "Status", value: result.status, tone: result.success ? "success" : "warning" },
    { label: "Requested", value: requestedProfileId, tone: "default" },
    { label: "Profile", value: result.profileId, tone: "default" },
    { label: "Symbols", value: symbolsLabel, tone: result.symbols?.length ? "default" : "muted" },
    { label: "Output", value: result.outputDirectory ?? "Unavailable", tone: result.outputDirectory ? "default" : "muted" },
    { label: "Files", value: String(result.filesGenerated), tone: result.filesGenerated > 0 ? "success" : "warning" },
    { label: "Records", value: result.totalRecords.toLocaleString(), tone: result.totalRecords > 0 ? "default" : "muted" },
    { label: "Bytes", value: byteLabel, tone: result.totalBytes > 0 ? "default" : "muted" },
    { label: "Duration", value: durationLabel, tone: "muted" },
    { label: "Timestamp", value: result.timestamp, tone: "muted" }
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
  return (result.files ?? []).map((file) => ({
    label: file.symbol ? `${file.symbol} ${file.format ?? "file"}` : file.format ?? "File",
    value: `${file.path} · ${file.recordCount.toLocaleString()} records · ${formatBytes(file.sizeBytes)}`,
    tone: file.recordCount > 0 ? "default" : "warning"
  }));
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
