import { useState } from "react";
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
}

export interface ReportingExportServices {
  runExport: (profileId: string) => Promise<ExportAnalysisResult>;
}

export interface ReportingPackTargetRow {
  id: string;
  label: string;
  ariaLabel: string;
}

export interface ReportingScreenViewModel {
  title: string;
  description: string;
  countLabel: string;
  hasRows: boolean;
  rows: ReportingProfileRow[];
  emptyText: string;
  listLabel: string;
  visibleCountLabel: string;
  detailId: string;
  statusTitle: string;
  statusDetail: string;
  nextAction: string;
  selectedProfile: ReportingDetailViewModel | null;
  packTargets: ReportingPackTargetRow[];
  hasPackTargets: boolean;
  packTargetsSummary: string;
  packTargetsListLabel: string;
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
  services: ReportingExportServices = defaultReportingExportServices
): ReportingScreenViewModel {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [runningProfileId, setRunningProfileId] = useState<string | null>(null);
  const [exportStatus, setExportStatus] = useState<ReportingExportStatusState | null>(null);

  const selectProfile = (id: string) =>
    setSelectedId((prev) => (prev === id ? null : id));

  const runExportCommand = async (profileId: string, profileName: string) => {
    setRunningProfileId(profileId);
    setExportStatus(buildExportStatusStarting(profileName));

    try {
      const result = await services.runExport(profileId);
      setExportStatus(buildExportStatusResult(profileName, result));
    } catch (error) {
      setExportStatus(buildExportStatusFailure(profileName, error));
    } finally {
      setRunningProfileId(null);
    }
  };

  if (!reporting) {
    return {
      title: "Report packs",
      description: "No reporting data is available.",
      countLabel: "0 profiles",
      hasRows: false,
      rows: [],
      emptyText: "No export or reporting profiles are configured for this workspace.",
      listLabel: "Export profiles",
      visibleCountLabel: "0 visible",
      detailId: "reporting-profile-detail",
      statusTitle: "No profile selected",
      statusDetail: "Reporting data is unavailable. Check the Governance workspace or API connection.",
      nextAction: "—",
      selectedProfile: null,
      packTargets: [],
      hasPackTargets: false,
      packTargetsSummary: "No report-pack targets configured.",
      packTargetsListLabel: "Report-pack targets",
      loadingTitle: "Loading Reporting",
      loadingDetail: "Waiting for governed report-pack and export-profile data.",
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
  const packTargets: ReportingPackTargetRow[] = reporting.reportPackTargets.map((target) => ({
    id: target,
    label: target,
    ariaLabel: `${target} report-pack target`
  }));

  return {
    title: "Report packs",
    description: reporting.summary,
    countLabel,
    hasRows: rows.length > 0,
    rows,
    emptyText:
      "No export profiles are configured. Add reporting profiles to the governance configuration.",
    listLabel: "Export profiles",
    visibleCountLabel: `${rows.length} of ${profiles.length}`,
    detailId: "reporting-profile-detail",
    statusTitle: selectedProfileData ? `${selectedProfileData.name} selected` : "No profile selected",
    statusDetail: selectedProfileData
      ? `${selectedProfileData.name} exports governance outputs as ${selectedProfileData.format} for ${selectedProfileData.targetTool}.`
      : "Select a profile to review its configuration and export readiness.",
    nextAction: selectedProfileData
      ? "Use /api/export/analysis to trigger this profile, or /api/export/formats to list all format targets."
      : `${profiles.length} profile${profiles.length === 1 ? "" : "s"} available. Select one to review export details.`,
    selectedProfile: detail,
    packTargets,
    hasPackTargets: packCount > 0,
    packTargetsSummary:
      packCount > 0
        ? `${packCount} report-pack target${packCount === 1 ? "" : "s"} configured.`
        : "No report-pack targets configured.",
    packTargetsListLabel: "Report-pack targets",
    loadingTitle: "Loading Reporting",
    loadingDetail: "Waiting for governed report-pack and export-profile data.",
    exportStatus,
    runningProfileId,
    runExport: runExportCommand,
    selectProfile
  };
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
      variant: "default",
      ariaLabel: `Preview ${profile.name} export payload`,
      isDisabled: false,
      disabledReason: null,
      method: "GET",
      profileId: profile.id,
      isRunning: false
    },
    {
      id: "run",
      label: isRunningThisProfile ? "Running export" : "Run export",
      href: "/api/export/analysis",
      variant: "outline",
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
    text: `Starting ${profileName} export.`,
    tone: "default",
    ariaLabel: "Reporting export status"
  };
}

export function buildExportStatusResult(
  profileName: string,
  result: ExportAnalysisResult
): ReportingExportStatusState {
  const text = result.success
    ? `${profileName} export completed: ${result.filesGenerated} file${result.filesGenerated === 1 ? "" : "s"} generated.`
    : `${profileName} export failed: ${result.error ?? "No error detail returned."}`;

  return {
    text,
    tone: result.success ? "success" : "danger",
    ariaLabel: "Reporting export status"
  };
}

export function buildExportStatusFailure(profileName: string, error: unknown): ReportingExportStatusState {
  const message = error instanceof Error ? error.message : "Unknown export error.";

  return {
    text: `${profileName} export failed: ${message}`,
    tone: "danger",
    ariaLabel: "Reporting export status"
  };
}
