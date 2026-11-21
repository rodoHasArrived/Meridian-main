import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { isApiError } from "@/lib/api-errors";

export interface ApprovalDiscussionComment {
  commentId: string; approvalId: string; author: string; body: string; mentions: string[]; createdAtUtc: string; commandId: string;
}
export interface ApprovalDiscussionHistory {
  approvalId: string; status: string; operator: string | null; reviewer: string | null;
  rationale: string | null; submittedAtUtc: string | null; decidedAtUtc: string | null;
}
export interface ApprovalCollaboration {
  workflowId: string; version: number; approvalState: string; currentApprovalId: string | null;
  submitter: string | null; reviewer: string | null; comments: ApprovalDiscussionComment[];
  approvalHistory: ApprovalDiscussionHistory[]; canComment: boolean; canReassign: boolean; disabledReason: string | null;
}
export interface ApprovalCollaborationCommand {
  expectedVersion: number; approvalId: string; commandId: string; actor: string;
  body?: string; mentions?: string[]; reviewer?: string; rationale?: string;
}
export interface ApprovalCollaborationResult { success: boolean; errorCode?: string | null; errorMessage?: string | null; }

function approvalBase(workflowId: string) { return `/api/workstation/operations/continuity/${encodeURIComponent(workflowId)}/approval`; }
export async function getApprovalCollaboration(workflowId: string, options: ApiRequestOptions = {}): Promise<ApprovalCollaboration> {
  const result = await apiGetJson<ApprovalCollaboration>(`${approvalBase(workflowId)}/collaboration`, { ...options, allowDevelopmentFallback: false });
  if (!result || typeof result.workflowId !== "string" || result.workflowId.toLowerCase() !== workflowId.toLowerCase() || !Number.isSafeInteger(result.version)
    || !Array.isArray(result.comments) || !Array.isArray(result.approvalHistory)
    || typeof result.canComment !== "boolean" || typeof result.canReassign !== "boolean"
    || (result.canComment || result.canReassign) && !result.currentApprovalId) {
    throw new Error("Approval discussion does not match the selected workflow. Refresh before changing it.");
  }
  return result;
}

export async function applyApprovalCollaboration(workflowId: string, action: "comment" | "reassign", command: ApprovalCollaborationCommand,
  options: ApiRequestOptions = {}): Promise<ApprovalCollaborationResult> {
  try {
    return await apiPostJson<ApprovalCollaborationResult>(`${approvalBase(workflowId)}/${action === "comment" ? "comments" : "reviewer"}`,
      { ...command, actor: "" }, { ...options, allowDevelopmentFallback: false });
  } catch (failure) {
    if (isApiError(failure) && failure.responseBody && [400, 403, 404, 409, 422, 503].includes(failure.status)) {
      try {
        const result = JSON.parse(failure.responseBody) as ApprovalCollaborationResult;
        if (result.success === false) return result;
      } catch { /* An unverifiable response remains uncertain. */ }
    }
    throw failure;
  }
}
