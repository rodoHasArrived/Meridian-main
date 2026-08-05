import { getJson, postJson } from "./api";
import {
  RISK_API_ENDPOINTS,
  riskEscalationApproveEndpoint,
  riskEscalationDenyEndpoint
} from "./workstation-endpoints";
import type { RiskEscalation, RiskEscalationApprovalResponse } from "@/types";

// --- Governed risk approvals ---

/**
 * Parked escalations the caller is authorized to see. The server scopes the list by fund
 * account, so this is already the caller's queue rather than the whole desk's.
 */
export function getRiskEscalations() {
  return getJson<RiskEscalation[]>(RISK_API_ENDPOINTS.escalations);
}

/**
 * Approves a parked escalation. `release: true` re-submits the retained order through the
 * risk gate in the same call. The server refuses self-approval (403) — the submitting
 * operator can never approve their own exception.
 */
export function approveRiskEscalation(escalationId: string, reason: string, release = true) {
  return postJson<RiskEscalationApprovalResponse>(
    riskEscalationApproveEndpoint(escalationId),
    { reason, release });
}

/** Denies a parked escalation. The retained order is withdrawn and can never be released. */
export function denyRiskEscalation(escalationId: string, reason: string) {
  return postJson<RiskEscalation>(riskEscalationDenyEndpoint(escalationId), { reason });
}
