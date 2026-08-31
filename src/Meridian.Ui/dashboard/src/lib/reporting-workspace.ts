import type {
  AccountingWorkspaceResponse,
  AccountingReportingSummary,
  ReportingWorkspaceResponse
} from "@/types";

export type ReportingWorkspacePayload =
  | ReportingWorkspaceResponse
  | AccountingWorkspaceResponse;

function isReportingWorkspaceEnvelope(
  payload: ReportingWorkspacePayload
): payload is AccountingWorkspaceResponse {
  return "reporting" in payload;
}

export function normalizeReportingWorkspace(
  payload: ReportingWorkspacePayload | null | undefined
): AccountingReportingSummary | null {
  if (!payload) {
    return null;
  }

  return isReportingWorkspaceEnvelope(payload) ? payload.reporting : payload;
}
