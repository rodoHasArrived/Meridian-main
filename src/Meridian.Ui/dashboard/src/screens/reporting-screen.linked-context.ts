import {
  appendLinkedContextSearchValue,
  buildLinkedContextItem,
  type AppShellLinkedContextItem
} from "@/app-shell.linked-context";
import { countPendingReportPackDistributions, getReportPackDistributions } from "@/lib/reporting-distributions";
import {
  normalizeReportingWorkspace,
  type ReportingWorkspacePayload
} from "@/lib/reporting-workspace";
import { pluralizeCount } from "@/lib/format";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export function buildReportingLinkedContextItem(
  reportingPayload: ReportingWorkspacePayload | null,
  symbol: string
): AppShellLinkedContextItem {
  const reporting = normalizeReportingWorkspace(reportingPayload);
  const route = appendLinkedContextSearchValue(WORKSTATION_ROUTE_CATALOG.reportingEvidence, "symbol", symbol);
  if (!reporting) {
    return buildLinkedContextItem({
      id: "reporting-evidence",
      label: "Evidence packet",
      detail: `Open audit lineage and report evidence with ${symbol} retained.`,
      route,
      workspaceLabel: "Reporting",
      statusLabel: "Waiting",
      tone: "pending"
    });
  }

  const distributions = getReportPackDistributions(reporting);
  const pendingCount = countPendingReportPackDistributions(reporting);
  const packCount = distributions.length;
  return buildLinkedContextItem({
    id: "reporting-evidence",
    label: "Evidence packet",
    detail: packCount > 0
      ? `${formatReportingLinkedContextCount(packCount, "report recipient")} can carry ${symbol} context into governed review; ${formatReportingLinkedContextCount(pendingCount, "distribution")} pending.`
      : `No report-pack recipient is loaded while reviewing ${symbol}.`,
    route,
    workspaceLabel: "Reporting",
    statusLabel: packCount > 0 ? (pendingCount > 0 ? "Pending distribution" : "Packet ready") : "Needs recipient",
    tone: packCount > 0 && pendingCount === 0 ? "ready" : "review"
  });
}

function formatReportingLinkedContextCount(count: number, singular: string, plural = `${singular}s`): string {
  return pluralizeCount(count, singular, { plural });
}
