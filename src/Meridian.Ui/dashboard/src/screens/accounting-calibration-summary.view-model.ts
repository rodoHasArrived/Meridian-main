import type { ApiErrorDisplay } from "@/lib/api-errors";
import {formatPrefixedCurrency as formatCurrency, pluralizeCount} from "@/lib/format";
import type { ReconciliationCalibrationStatus, ReconciliationCalibrationSummary } from "@/types";

export type CalibrationStatusTone = "success" | "warning" | "danger";
export type CalibrationStatusIcon = "check" | "alert";

export interface CalibrationSummaryMetricViewModel {
  id: string;
  label: string;
  value: number;
  tone: "default" | "warning";
  ariaLabel: string;
}

export interface CalibrationProfileRowViewModel {
  toleranceProfileId: string;
  exceptionRoute: string;
  highestSeverity: string;
  maxToleranceBandLabel: string;
  totalBreakCount: number;
  openBreakCount: number;
  inReviewBreakCount: number;
  resolvedBreakCount: number;
  pendingSignoffCount: number;
  signedOffCount: number;
  lastUpdatedLabel: string;
  ariaLabel: string;
  selectAriaLabel: string;
  detailPanelId: string;
  isSelected: boolean;
}

export interface CalibrationProfileDetailFieldViewModel {
  label: string;
  value: string;
}

export interface CalibrationProfileDetailViewModel {
  id: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "danger";
  ariaLabel: string;
  fields: CalibrationProfileDetailFieldViewModel[];
}

export interface CalibrationSummaryRefreshCommandViewModel {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
}

export interface CalibrationSummaryViewState {
  status: ReconciliationCalibrationStatus;
  statusLabel: string;
  statusTone: CalibrationStatusTone;
  statusIcon: CalibrationStatusIcon;
  statusTextClassName: string;
  statusBannerClassName: string;
  summary: string;
  asOfLabel: string;
  totalBreakCount: number;
  openBreakCount: number;
  criticalOpenBreakCount: number;
  pendingSignoffCount: number;
  signedOffCount: number;
  missingMetadataCount: number;
  metricRows: CalibrationSummaryMetricViewModel[];
  profileRows: CalibrationProfileRowViewModel[];
  hasProfiles: boolean;
  profilesLabel: string;
  tableAriaLabel: string;
  emptyText: string;
  detailPanelId: string;
  selectedProfileId: string | null;
  selectedProfile: CalibrationProfileDetailViewModel | null;
  refreshCommand: CalibrationSummaryRefreshCommandViewModel;
  errorText: string | null;
  errorDetails: string[];
  loadingText: string | null;
  statusAnnouncement: string;
}

export interface CalibrationSummaryViewModel extends CalibrationSummaryViewState {
  selectProfile: (profileId: string) => void;
  refresh: () => void;
}

const CALIBRATION_PROFILE_DETAIL_PANEL_ID = "calibration-profile-detail-panel";

export function buildCalibrationSummaryViewState(
  summary: ReconciliationCalibrationSummary | null,
  loading: boolean,
  error: string | ApiErrorDisplay | null,
  selectedProfileId: string | null = null
): CalibrationSummaryViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const statusTone = calibrationStatusTone(summary?.status ?? null);
  const profiles = summary?.profiles ?? [];
  const effectiveSelectedProfileId = selectedProfileId && profiles.some((profile) => profile.toleranceProfileId === selectedProfileId)
    ? selectedProfileId
    : profiles[0]?.toleranceProfileId ?? null;
  const profileRows = profiles.map((profile) => buildCalibrationProfileRow(profile, effectiveSelectedProfileId));
  const selectedProfileRow = profileRows.find((profile) => profile.toleranceProfileId === effectiveSelectedProfileId) ?? null;
  const metricRows = buildCalibrationSummaryMetrics(summary);

  return {
    status: summary?.status ?? "Ready",
    statusLabel: calibrationStatusLabel(summary?.status ?? null, loading),
    statusTone,
    statusIcon: statusTone === "success" ? "check" : "alert",
    statusTextClassName: calibrationStatusTextClass(statusTone),
    statusBannerClassName: calibrationStatusBannerClass(statusTone),
    summary: summary?.summary ?? "",
    asOfLabel: summary?.asOf ? formatSecurityDate(summary.asOf) : "—",
    totalBreakCount: summary?.totalBreakCount ?? 0,
    openBreakCount: summary?.openBreakCount ?? 0,
    criticalOpenBreakCount: summary?.criticalOpenBreakCount ?? 0,
    pendingSignoffCount: summary?.pendingSignoffCount ?? 0,
    signedOffCount: summary?.signedOffCount ?? 0,
    missingMetadataCount: summary?.missingCalibrationMetadataCount ?? 0,
    metricRows,
    profileRows,
    hasProfiles: profileRows.length > 0,
    profilesLabel: profileRows.length === 1 ? "1 tolerance profile" : `${profileRows.length} tolerance profiles`,
    tableAriaLabel: "Tolerance profile health by reconciliation route",
    emptyText: loading
      ? "Loading tolerance profiles..."
      : errorText
        ? "Tolerance profiles are unavailable until the calibration summary reloads."
        : "No tolerance profiles loaded. Run provider calibration before accepting reconciliation readiness.",
    detailPanelId: CALIBRATION_PROFILE_DETAIL_PANEL_ID,
    selectedProfileId: selectedProfileRow?.toleranceProfileId ?? null,
    selectedProfile: selectedProfileRow ? buildCalibrationProfileDetail(selectedProfileRow) : null,
    refreshCommand: buildCalibrationSummaryRefreshCommand(loading, errorText),
    errorText,
    errorDetails: normalizedError?.details ?? [],
    loadingText: loading ? "Loading calibration summary..." : null,
    statusAnnouncement: errorText
      ? `Calibration summary error: ${errorText}`
      : loading
        ? "Loading calibration summary."
        : summary
          ? `Calibration status: ${calibrationStatusLabel(summary.status, false)}. ${summary.summary}`
          : ""
  };
}

function buildCalibrationSummaryRefreshCommand(
  loading: boolean,
  errorText: string | null
): CalibrationSummaryRefreshCommandViewModel {
  if (loading) {
    return {
      label: "Refreshing...",
      ariaLabel: "Calibration summary refresh is already running",
      disabled: true,
      disabledReason: "Calibration summary refresh is already running."
    };
  }

  return {
    label: errorText ? "Retry calibration summary" : "Refresh calibration",
    ariaLabel: errorText ? "Retry calibration summary load" : "Refresh calibration summary",
    disabled: false,
    disabledReason: null
  };
}

function buildCalibrationSummaryMetrics(
  summary: ReconciliationCalibrationSummary | null
): CalibrationSummaryMetricViewModel[] {
  const totalBreakCount = summary?.totalBreakCount ?? 0;
  const openBreakCount = summary?.openBreakCount ?? 0;
  const criticalOpenBreakCount = summary?.criticalOpenBreakCount ?? 0;
  const pendingSignoffCount = summary?.pendingSignoffCount ?? 0;
  const signedOffCount = summary?.signedOffCount ?? 0;
  const missingMetadataCount = summary?.missingCalibrationMetadataCount ?? 0;

  return [
    buildCalibrationSummaryMetric("total", "Total breaks", totalBreakCount, false),
    buildCalibrationSummaryMetric("open", "Open", openBreakCount, openBreakCount > 0),
    buildCalibrationSummaryMetric("critical-open", "Critical open", criticalOpenBreakCount, criticalOpenBreakCount > 0),
    buildCalibrationSummaryMetric("pending-signoff", "Pending sign-off", pendingSignoffCount, pendingSignoffCount > 0),
    buildCalibrationSummaryMetric("signed-off", "Signed off", signedOffCount, false),
    buildCalibrationSummaryMetric("missing-metadata", "Missing metadata", missingMetadataCount, missingMetadataCount > 0)
  ];
}

function buildCalibrationSummaryMetric(
  id: string,
  label: string,
  value: number,
  warn: boolean
): CalibrationSummaryMetricViewModel {
  return {
    id,
    label,
    value,
    tone: warn ? "warning" : "default",
    ariaLabel: `${label}: ${value}`
  };
}

function buildCalibrationProfileRow(
  profile: ReconciliationCalibrationSummary["profiles"][number],
  selectedProfileId: string | null
): CalibrationProfileRowViewModel {
  const isSelected = profile.toleranceProfileId === selectedProfileId;
  const statusLabel = calibrationProfileStatusLabel(profile);

  return {
    toleranceProfileId: profile.toleranceProfileId,
    exceptionRoute: profile.exceptionRoute,
    highestSeverity: profile.highestSeverity,
    maxToleranceBandLabel: profile.maxToleranceBand === null ? "Policy default" : formatCurrency(profile.maxToleranceBand),
    totalBreakCount: profile.totalBreakCount,
    openBreakCount: profile.openBreakCount,
    inReviewBreakCount: profile.inReviewBreakCount,
    resolvedBreakCount: profile.resolvedBreakCount,
    pendingSignoffCount: profile.pendingSignoffCount,
    signedOffCount: profile.signedOffCount,
    lastUpdatedLabel: formatSecurityDate(profile.lastUpdatedAt),
    ariaLabel: `Profile ${profile.toleranceProfileId}: ${profile.openBreakCount} open, ${profile.pendingSignoffCount} pending sign-off, severity ${profile.highestSeverity}`,
    selectAriaLabel: `Inspect tolerance profile ${profile.toleranceProfileId}: ${statusLabel}`,
    detailPanelId: CALIBRATION_PROFILE_DETAIL_PANEL_ID,
    isSelected
  };
}

function buildCalibrationProfileDetail(
  profile: CalibrationProfileRowViewModel
): CalibrationProfileDetailViewModel {
  const statusLabel = calibrationProfileStatusLabel(profile);
  const statusTone = calibrationProfileStatusTone(profile);

  return {
    id: `${CALIBRATION_PROFILE_DETAIL_PANEL_ID}-${toDomId(profile.toleranceProfileId)}`,
    title: `Selected tolerance profile - ${profile.toleranceProfileId}`,
    subtitle: `${profile.exceptionRoute} route - ${profile.highestSeverity} severity`,
    description: `${statusLabel}. ${formatCount(profile.totalBreakCount, "break")} tracked for this exception route, with ${formatCount(profile.openBreakCount, "open break")} and ${formatCount(profile.pendingSignoffCount, "pending sign-off")}.`,
    statusLabel,
    statusTone,
    ariaLabel: `Tolerance profile detail for ${profile.toleranceProfileId}`,
    fields: [
      { label: "Tolerance band", value: profile.maxToleranceBandLabel },
      { label: "Total breaks", value: String(profile.totalBreakCount) },
      { label: "Open", value: String(profile.openBreakCount) },
      { label: "In review", value: String(profile.inReviewBreakCount) },
      { label: "Resolved", value: String(profile.resolvedBreakCount) },
      { label: "Pending sign-off", value: String(profile.pendingSignoffCount) },
      { label: "Signed off", value: String(profile.signedOffCount) },
      { label: "Last updated", value: profile.lastUpdatedLabel }
    ]
  };
}

function calibrationProfileStatusLabel(
  profile: Pick<CalibrationProfileRowViewModel, "highestSeverity" | "openBreakCount" | "pendingSignoffCount">
): string {
  if (profile.highestSeverity.toLowerCase() === "critical" || profile.openBreakCount > 0) {
    return "Operator review required";
  }

  if (profile.pendingSignoffCount > 0) {
    return "Pending sign-off";
  }

  return "Within tolerance";
}

function calibrationProfileStatusTone(
  profile: Pick<CalibrationProfileRowViewModel, "highestSeverity" | "openBreakCount" | "pendingSignoffCount">
): CalibrationProfileDetailViewModel["statusTone"] {
  if (profile.highestSeverity.toLowerCase() === "critical") {
    return "danger";
  }

  if (profile.openBreakCount > 0 || profile.pendingSignoffCount > 0) {
    return "warning";
  }

  return "success";
}

function calibrationStatusTone(status: ReconciliationCalibrationStatus | null): CalibrationStatusTone {
  if (status === "Ready") {
    return "success";
  }

  if (status === "Blocked") {
    return "danger";
  }

  return "warning";
}

function calibrationStatusTextClass(tone: CalibrationStatusTone): string {
  if (tone === "success") {
    return "text-success";
  }

  if (tone === "danger") {
    return "text-danger";
  }

  return "text-warning";
}

function calibrationStatusBannerClass(tone: CalibrationStatusTone): string {
  if (tone === "success") {
    return "border-success/30 bg-success/5";
  }

  if (tone === "danger") {
    return "border-danger/30 bg-danger/5";
  }

  return "border-warning/30 bg-warning/5";
}

function calibrationStatusLabel(status: ReconciliationCalibrationStatus | null, loading: boolean): string {
  if (loading) {
    return "Loading...";
  }

  if (status === "Ready") {
    return "Ready";
  }

  if (status === "Blocked") {
    return "Blocked";
  }

  if (status === "ReviewRequired") {
    return "Review required";
  }

  return "Unknown";
}

function formatCount(count: number, singular: string): string {
  return pluralizeCount(count, singular);
}

function formatSecurityDate(value: string | null | undefined): string {
  if (!value) {
    return "—";
  }

  const match = /^\d{4}-\d{2}-\d{2}/.exec(value);
  return match?.[0] ?? value;
}

function toDomId(value: string): string {
  const normalized = value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "profile";
}

function normalizeApiErrorDisplay(error: string | ApiErrorDisplay | null): ApiErrorDisplay | null {
  if (!error) {
    return null;
  }

  if (typeof error === "string") {
    return { summary: error, details: [] };
  }

  return error;
}
