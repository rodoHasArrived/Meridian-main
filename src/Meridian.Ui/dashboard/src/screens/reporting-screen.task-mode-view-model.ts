import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export type ReportingTaskModeId =
  | "daily-reporting-cockpit"
  | "report-builder"
  | "run-status"
  | "delivery-evidence"
  | "exports"
  | "governance"
  | "report-pack-approval";

export interface ReportingTaskModeViewModel {
  id: ReportingTaskModeId;
  label: string;
  description: string;
  routeLabel: string;
  ariaLabel: string;
}

const reportingTaskModeDefinitions: Record<ReportingTaskModeId, ReportingTaskModeViewModel> = {
  "daily-reporting-cockpit": {
    id: "daily-reporting-cockpit",
    label: "Daily Reporting Cockpit",
    description: "Reporting opens on the daily decision queue: blocked output, owner, affected report, next action, and retained proof.",
    routeLabel: "Daily Reporting Cockpit",
    ariaLabel: "Reporting task mode daily reporting cockpit"
  },
  "report-builder": {
    id: "report-builder",
    label: "Report Builder",
    description: "Reporting owns governed output design, report-writer grids, branding, schedules, and delivery history.",
    routeLabel: "Report Builder",
    ariaLabel: "Reporting task mode report builder"
  },
  "run-status": {
    id: "run-status",
    label: "Run Status",
    description: "Reporting owns report-run readiness, queue posture, schedule state, and publication blockers.",
    routeLabel: "Run Status",
    ariaLabel: "Reporting task mode run status"
  },
  "delivery-evidence": {
    id: "delivery-evidence",
    label: "Delivery Evidence",
    description: "Reporting owns retained evidence packets, report-line provenance, recipient support, and audit-ready delivery proof.",
    routeLabel: "Delivery Evidence",
    ariaLabel: "Reporting task mode delivery evidence"
  },
  exports: {
    id: "exports",
    label: "Exports",
    description: "Reporting owns governed export previews, generated artifacts, warnings, and retained manifest support.",
    routeLabel: "Exports",
    ariaLabel: "Reporting task mode exports"
  },
  governance: {
    id: "governance",
    label: "Governance",
    description: "Reporting owns access scope, template approval posture, lifecycle decisions, and audit-ready controls.",
    routeLabel: "Governance",
    ariaLabel: "Reporting task mode governance"
  },
  "report-pack-approval": {
    id: "report-pack-approval",
    label: "Delivery Evidence",
    description: "Reporting owns governed output review, recipient checks, export preview, and publication evidence.",
    routeLabel: "Delivery Evidence",
    ariaLabel: "Reporting task mode delivery evidence"
  }
};

export function buildReportingTaskMode(pathname: string): ReportingTaskModeViewModel {
  const normalizedPathname = normalizeReportingPathname(pathname);

  if (normalizedPathname === WORKSTATION_ROUTE_CATALOG.reporting) {
    return reportingTaskModeDefinitions["daily-reporting-cockpit"];
  }

  if (normalizedPathname.startsWith(WORKSTATION_ROUTE_CATALOG.reportingReportBuilder)) {
    return reportingTaskModeDefinitions["report-builder"];
  }

  if (isReportPackRoute(pathname)) {
    return reportingTaskModeDefinitions["report-pack-approval"];
  }

  if (pathname.includes("/run-status")) {
    return reportingTaskModeDefinitions["run-status"];
  }

  if (pathname.includes("/evidence")) {
    return reportingTaskModeDefinitions["delivery-evidence"];
  }

  if (pathname.includes("/exports")) {
    return reportingTaskModeDefinitions.exports;
  }

  if (pathname.includes("/governance")) {
    return reportingTaskModeDefinitions.governance;
  }

  return reportingTaskModeDefinitions["report-builder"];
}

export function isReportPackRoute(pathname: string): boolean {
  const normalized = pathname.split(/[?#]/)[0]?.replace(/\/+$/, "") || WORKSTATION_ROUTE_CATALOG.reporting;
  return normalized === WORKSTATION_ROUTE_CATALOG.reportingReportPacks;
}

function normalizeReportingPathname(pathname: string): string {
  const [withoutQuery] = pathname.split(/[?#]/);
  return (withoutQuery?.replace(/\/+$/, "") || WORKSTATION_ROUTE_CATALOG.reporting).toLowerCase();
}
