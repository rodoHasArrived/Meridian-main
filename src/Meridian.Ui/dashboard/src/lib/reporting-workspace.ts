import type {
  AccountingReportingSummary,
  AccountingWorkspaceResponse,
  ReportingWorkspaceResponse
} from "@/types";

type LegacyReportingWorkspaceEnvelope = Pick<AccountingWorkspaceResponse, "reporting">;

export type ReportingWorkspacePayload =
  | ReportingWorkspaceResponse
  | LegacyReportingWorkspaceEnvelope;

function isLegacyReportingWorkspaceEnvelope(
  payload: ReportingWorkspacePayload
): payload is LegacyReportingWorkspaceEnvelope {
  return "reporting" in payload;
}

export function normalizeReportingWorkspace(
  payload: ReportingWorkspacePayload | null | undefined
): AccountingReportingSummary | null {
  if (!payload) {
    return null;
  }

  return isLegacyReportingWorkspaceEnvelope(payload) ? payload.reporting : payload;
}
