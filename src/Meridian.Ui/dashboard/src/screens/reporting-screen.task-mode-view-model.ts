import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export type ReportingTaskModeId =
  | "daily-reporting-cockpit"
  | "report-builder"
  | "schedules"
  | "run-status"
  | "delivery-evidence"
  | "exports"
  | "governance"
  | "report-pack-approval";

export interface ReportingTaskModeViewModel {
  id: ReportingTaskModeId;
  label: string;
  /** Task title for the route body heading; the breadcrumb already shows the page name. */
  title: string;
  description: string;
  routeLabel: string;
  ariaLabel: string;
}

const reportingTaskModeDefinitions: Record<ReportingTaskModeId, ReportingTaskModeViewModel> = {
  "daily-reporting-cockpit": {
    id: "daily-reporting-cockpit",
    label: "Daily Reporting Cockpit",
    title: "Daily Reporting Cockpit",
    description: "See which reports are blocked today, who owns each one, and the next action.",
    routeLabel: "Daily Reporting Cockpit",
    ariaLabel: "Reporting task mode daily reporting cockpit"
  },
  "report-builder": {
    id: "report-builder",
    label: "Report Builder",
    title: "Report design workspace",
    description: "Design report layouts, branding, and reusable templates.",
    routeLabel: "Report Builder",
    ariaLabel: "Reporting task mode report builder"
  },
  schedules: {
    id: "schedules",
    label: "Scheduled Reports",
    title: "Scheduled report delivery",
    description: "Create, pause, resume, and run report schedules and their recipient lists.",
    routeLabel: "Scheduled Reports",
    ariaLabel: "Reporting task mode scheduled reports"
  },
  "run-status": {
    id: "run-status",
    label: "Run Status",
    title: "Report run readiness",
    description: "Track report runs and see what is blocking publication.",
    routeLabel: "Run Status",
    ariaLabel: "Reporting task mode run status"
  },
  "delivery-evidence": {
    id: "delivery-evidence",
    label: "Delivery Evidence",
    title: "Delivery Evidence",
    description: "See the records behind each report line and proof of delivery.",
    routeLabel: "Delivery Evidence",
    ariaLabel: "Reporting task mode delivery evidence"
  },
  exports: {
    id: "exports",
    label: "Exports",
    title: "Governed exports",
    description: "Preview exports and review the files and warnings each one produced.",
    routeLabel: "Exports",
    ariaLabel: "Reporting task mode exports"
  },
  governance: {
    id: "governance",
    label: "Governance",
    title: "Reporting controls",
    description: "Manage who can use each template and approve template changes.",
    routeLabel: "Governance",
    ariaLabel: "Reporting task mode governance"
  },
  "report-pack-approval": {
    id: "report-pack-approval",
    label: "Report packs",
    title: "Report packs awaiting approval",
    description: "Review report packs, clear recipient blockers, and approve or publish them.",
    routeLabel: "Report packs",
    ariaLabel: "Reporting task mode report packs"
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

  if (normalizedPathname.startsWith(WORKSTATION_ROUTE_CATALOG.reportingScheduled)) {
    return reportingTaskModeDefinitions.schedules;
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
