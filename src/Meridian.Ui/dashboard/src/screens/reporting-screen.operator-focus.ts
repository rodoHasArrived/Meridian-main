import { buildOperatorFocusCandidate, type OperatorFocusCandidate } from "@/app-shell.operator-focus";
import { getReportPackDistributions } from "@/lib/reporting-distributions";
import {
  normalizeReportingWorkspace,
  type ReportingWorkspacePayload
} from "@/lib/reporting-workspace";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export function buildReportingOperatorFocusItems(reportingPayload: ReportingWorkspacePayload | null): OperatorFocusCandidate[] {
  const reporting = normalizeReportingWorkspace(reportingPayload);
  if (!reporting) {
    return [];
  }

  const distributions = getReportPackDistributions(reporting);
  return distributions.length === 0
    ? [buildOperatorFocusCandidate({
        id: "reporting:missing-report-pack-distribution",
        label: "Report-pack recipient missing",
        detail: "No governed report-pack distribution recipients are loaded for approval-ready output review.",
        route: WORKSTATION_ROUTE_CATALOG.reportingReportPacks,
        workspaceLabel: "Reporting",
        actionLabel: "Open report packs",
        tone: "review",
        sourcePriority: 30,
        sourceIndex: 0
      })]
    : [];
}
