import type { AccountingReportingSummary, ReportPackDistributionRecord } from "@/types";

export function getReportPackDistributions(
  reporting: Pick<AccountingReportingSummary, "reportPackDistributions" | "reportPackTargets"> | null | undefined
): ReportPackDistributionRecord[] {
  const distributions = reporting?.reportPackDistributions ?? [];
  if (distributions.length > 0) {
    return distributions;
  }

  return (reporting?.reportPackTargets ?? []).map((target) => ({
    distributionId: target,
    recipient: formatLegacyRecipient(target),
    recipientRole: target,
    channel: "Legacy target",
    state: "Configured",
    pendingItems: 0,
    pendingSummary: "Legacy report-pack target is configured without recipient detail.",
    owner: "Reporting",
    dueAtUtc: null,
    lastSentAtUtc: null,
    route: "/reporting/report-packs"
  }));
}

export function countPendingReportPackDistributions(
  reporting: Pick<AccountingReportingSummary, "reportPackDistributions" | "reportPackTargets"> | null | undefined
): number {
  return getReportPackDistributions(reporting).filter((distribution) => distribution.pendingItems > 0).length;
}

export function formatReportPackRecipientList(
  reporting: Pick<AccountingReportingSummary, "reportPackDistributions" | "reportPackTargets"> | null | undefined
): string {
  const recipients = getReportPackDistributions(reporting).map((distribution) => distribution.recipient);
  return recipients.length > 0 ? recipients.join(", ") : "No report-pack recipients";
}

function formatLegacyRecipient(target: string): string {
  return target
    .split(/[-_\s]+/)
    .filter(Boolean)
    .map((part) => `${part.charAt(0).toUpperCase()}${part.slice(1)}`)
    .join(" ") || "Report-pack recipient";
}
