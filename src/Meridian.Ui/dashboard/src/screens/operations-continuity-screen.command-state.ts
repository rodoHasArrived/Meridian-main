export interface ReopenWorkflowFormState {
  incidentId: string;
  justification: string;
  approvalReference: string;
  impactSummary: string;
}

export function formatChecklistCommandError(err: unknown): string {
  return formatCommandError(err, "Checklist acknowledgement command failed.");
}

export function formatBreakCommandError(err: unknown): string {
  return formatCommandError(err, "Break command failed.");
}

export function formatApprovalSubmitCommandError(err: unknown): string {
  return formatCommandError(err, "Approval submission command failed.");
}

export function formatApprovalDecisionCommandError(err: unknown): string {
  return formatCommandError(err, "Approval decision command failed.");
}

export function formatCloseWorkflowCommandError(err: unknown): string {
  return formatCommandError(err, "Close package publication command failed.");
}

export function buildReopenWorkflowFormDisabledReason(form: ReopenWorkflowFormState): string | null {
  if (!form.incidentId.trim()) {
    return "Incident id is required before governed reopen.";
  }

  if (!form.approvalReference.trim()) {
    return "Approval reference is required before governed reopen.";
  }

  if (!form.justification.trim()) {
    return "Justification is required before governed reopen.";
  }

  if (!form.impactSummary.trim()) {
    return "Impact summary is required before governed reopen.";
  }

  return null;
}

export function formatReopenWorkflowCommandError(err: unknown): string {
  return formatCommandError(err, "Governed reopen command failed.");
}

function formatCommandError(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message.trim().length > 0) {
    return err.message;
  }

  return fallback;
}
