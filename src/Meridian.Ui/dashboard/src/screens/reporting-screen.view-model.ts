import { useState } from "react";
import type { GovernanceReportingProfile, GovernanceReportingSummary } from "@/types";

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
  packTargets: string[];
  hasPackTargets: boolean;
  packTargetsSummary: string;
  selectProfile: (id: string) => void;
}

export function useReportingScreenViewModel(
  reporting: GovernanceReportingSummary | null
): ReportingScreenViewModel {
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const selectProfile = (id: string) =>
    setSelectedId((prev) => (prev === id ? null : id));

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
        ]
      }
    : null;

  const countLabel = `${profiles.length} profile${profiles.length === 1 ? "" : "s"}`;
  const packCount = reporting.reportPackTargets.length;

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
    packTargets: reporting.reportPackTargets,
    hasPackTargets: packCount > 0,
    packTargetsSummary:
      packCount > 0
        ? `${packCount} report-pack target${packCount === 1 ? "" : "s"} configured.`
        : "No report-pack targets configured.",
    selectProfile
  };
}
