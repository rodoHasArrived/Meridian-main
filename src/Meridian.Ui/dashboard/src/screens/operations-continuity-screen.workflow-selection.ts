import type { OperationsContinuityWorkflow, OperationsContinuityWorkflowSummary, OperationsEvidenceLink, OperationsChecklistControlApproval, OperationsCloseChecklistTask } from "@/types";

export function workflowMatchesSummary(detail: OperationsContinuityWorkflow, summary: OperationsContinuityWorkflowSummary): boolean {
  return detail.workflowId === summary.workflowId
    && detail.version === summary.version
    && detail.fundAccountId === summary.fundAccountId
    && detail.periodId === summary.periodId
    && (detail.ledgerBookId ?? null) === (summary.ledgerBookId ?? null);
}

export function compareWorkflowSummaries(left: OperationsContinuityWorkflowSummary, right: OperationsContinuityWorkflowSummary): number {
  return right.updatedAtUtc.localeCompare(left.updatedAtUtc);
}

/** A ready projection must describe the evidence that the selected detail will publish. */
export function workflowMatchesPublicationSnapshot(
  detail: OperationsContinuityWorkflow | null | undefined,
  decisionWorkflow: OperationsContinuityWorkflow | null | undefined
): boolean {
  return Boolean(detail && decisionWorkflow
    && workflowMatchesSummary(detail, decisionWorkflow)
    && detail.reportPackReadiness.reportPackId === decisionWorkflow.reportPackReadiness.reportPackId
    && detail.reportPackReadiness.isReady === decisionWorkflow.reportPackReadiness.isReady
    && evidenceIdentitiesMatch(detail.reportPackReadiness.evidenceLinks, decisionWorkflow.reportPackReadiness.evidenceLinks)
    && evidenceIdentitiesMatch(collectCloseWorkflowEvidenceLinks(detail), collectCloseWorkflowEvidenceLinks(decisionWorkflow))
    && identityMapsMatch(approvalIdentities(detail), approvalIdentities(decisionWorkflow)));
}

export function collectCloseWorkflowEvidenceLinks(workflow: OperationsContinuityWorkflow | null): OperationsEvidenceLink[] {
  if (!workflow) {
    return [];
  }
  const links = [
    ...workflow.reportPackReadiness.evidenceLinks,
    ...workflow.evidenceLinks,
    ...(workflow.evidencePackages ?? []).flatMap((item) => item.evidenceLinks),
    ...workflow.approvals.flatMap((item) => item.evidenceLinks)
  ];
  return links.filter((link, index) => links.findIndex((candidate) => candidate.evidenceId === link.evidenceId) === index);
}

function evidenceIdentitiesMatch(left: OperationsEvidenceLink[], right: OperationsEvidenceLink[]): boolean {
  return identityMapsMatch(evidenceIdentities(left), evidenceIdentities(right));
}

function identityMapsMatch(left: Map<string, string> | null, right: Map<string, string> | null): boolean {
  return Boolean(left && right && left.size === right.size
    && [...left].every(([id, identity]) => right.get(id) === identity));
}

function evidenceIdentities(links: OperationsEvidenceLink[]): Map<string, string> | null {
  const identities = new Map<string, string>();
  for (const link of links) {
    // Publication retains the first link for an evidence ID, in this same source order.
    if (!identities.has(link.evidenceId)) {
      const capturedAt = timestampIdentity(link.capturedAtUtc);
      if (capturedAt === undefined) {
        return null;
      }
      identities.set(link.evidenceId, JSON.stringify([
        link.source, link.route, capturedAt
      ]));
    }
  }
  return identities;
}

function approvalIdentities(workflow: OperationsContinuityWorkflow): Map<string, string> | null {
  const identities = new Map<string, string>();
  for (const approval of collectCloseWorkflowChecklistControlApprovals(workflow)) {
    const approvedAt = timestampIdentity(approval.approvedAtUtc);
    if (approvedAt === undefined || approvedAt === null) {
      return null;
    }
    const key = JSON.stringify([approval.taskId.toLowerCase(), approval.approvedBy.toLowerCase()]);
    identities.set(key, approvedAt);
  }
  return identities;
}

/** Compare DateTimeOffset instants without losing the server's 100 ns precision. */
function timestampIdentity(value: string | null | undefined): string | null | undefined {
  if (value === null || value === undefined) {
    return null;
  }
  const parts = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|([+-])(\d{2}):(\d{2}))$/.exec(value);
  if (!parts || parts[1]!.startsWith("0000")) {
    return undefined;
  }
  const wholeSeconds = parts[1]!;
  const milliseconds = Date.parse(`${wholeSeconds}Z`);
  const offsetHours = Number(parts[5] ?? 0);
  const offsetMinutes = Number(parts[6] ?? 0);
  if (!Number.isFinite(milliseconds) || new Date(milliseconds).toISOString().slice(0, 19) !== wholeSeconds
    || offsetHours > 14 || offsetMinutes > 59 || (offsetHours === 14 && offsetMinutes !== 0)) {
    return undefined;
  }
  const offset = (offsetHours * 60 + offsetMinutes) * (parts[4] === "-" ? -1 : 1);
  const utcMilliseconds = milliseconds - offset * 60_000;
  const utcYear = new Date(utcMilliseconds).getUTCFullYear();
  if (utcYear < 1 || utcYear > 9999) {
    return undefined;
  }
  return (BigInt(utcMilliseconds) * 10_000n + BigInt((parts[2] ?? "").padEnd(7, "0"))).toString();
}

export function isPendingWorkflowApprovalState(status: OperationsContinuityWorkflow["approvalState"]): boolean {
  return status === "Submitted" || status === "ReviewerAssigned";
}

export function collectSubmitApprovalChecklistControlApprovals(
  workflow: OperationsContinuityWorkflow | null
): OperationsChecklistControlApproval[] {
  if (!workflow) {
    return [];
  }

  const approvals: OperationsChecklistControlApproval[] = [];
  workflow.closeChecklist
    .filter((task) => task.gate !== "Approval"
      && isChecklistTaskReady(task)
      && !isChecklistTaskBlocked(task)
      && Boolean(task.evidencePointer?.trim()))
    .forEach((task) => {
      appendChecklistControlApproval(approvals, task.taskId, task.acknowledgedBy, task.acknowledgedAtUtc);
    });

  return distinctChecklistControlApprovals(approvals);
}

export function collectCloseWorkflowChecklistControlApprovals(
  workflow: OperationsContinuityWorkflow | null
): OperationsChecklistControlApproval[] {
  const approvals = collectSubmitApprovalChecklistControlApprovals(workflow);
  if (!workflow || workflow.approvalState !== "Approved") {
    return approvals;
  }

  const approvalTaskId = workflow.closeChecklist.find((task) => task.gate === "Approval")?.taskId ?? "close-gate-approval";
  const decision = workflow.approvals.at(-1);
  if (decision?.status === "Approved") {
    // Legacy records retain submission and decision as separate history entries.
    const submission = decision.submittedAtUtc
      ? decision
      : workflow.approvals.at(-2);
    if (submission && (submission === decision || isPendingWorkflowApprovalState(submission.status))) {
      appendChecklistControlApproval(approvals, approvalTaskId, submission.operator, submission.submittedAtUtc);
    }
    appendChecklistControlApproval(approvals, approvalTaskId, decision.reviewer, decision.decidedAtUtc);
  }

  return distinctChecklistControlApprovals(approvals);
}

export function collectApprovalDecisionChecklistControlApprovals(
  workflow: OperationsContinuityWorkflow,
  approval: OperationsContinuityWorkflow["approvals"][number]
): OperationsChecklistControlApproval[] {
  const approvals = collectSubmitApprovalChecklistControlApprovals(workflow);
  const approvalTaskId = workflow.closeChecklist.find((task) => task.gate === "Approval")?.taskId ?? "close-gate-approval";
  appendChecklistControlApproval(approvals, approvalTaskId, approval.operator, approval.submittedAtUtc);

  return distinctChecklistControlApprovals(approvals);
}

function appendChecklistControlApproval(
  approvals: OperationsChecklistControlApproval[],
  taskId: string | null | undefined,
  approvedBy: string | null | undefined,
  approvedAtUtc: string | null | undefined
): void {
  const cleanTaskId = taskId?.trim();
  const cleanApprover = approvedBy?.trim();
  const cleanTimestamp = approvedAtUtc?.trim();
  if (!cleanTaskId || !cleanApprover || !cleanTimestamp) {
    return;
  }

  approvals.push({
    taskId: cleanTaskId,
    approvedBy: cleanApprover,
    approvedAtUtc: cleanTimestamp
  });
}

function distinctChecklistControlApprovals(
  approvals: OperationsChecklistControlApproval[]
): OperationsChecklistControlApproval[] {
  return approvals.filter((approval, index, source) =>
    source.findIndex((candidate) =>
      candidate.taskId.toLowerCase() === approval.taskId.toLowerCase()
        && candidate.approvedBy.toLowerCase() === approval.approvedBy.toLowerCase()
    ) === index
  );
}

export function isChecklistTaskReady(task: OperationsCloseChecklistTask): boolean {
  const normalized = task.status?.trim().toLowerCase() ?? "";
  return normalized === "done" ||
    normalized === "complete" ||
    normalized === "completed" ||
    normalized === "acknowledged" ||
    Boolean(task.acknowledgedAtUtc);
}

export function isChecklistTaskBlocked(task: OperationsCloseChecklistTask): boolean {
  const normalized = task.status?.trim().toLowerCase() ?? "";
  return normalized === "blocked" ||
    normalized === "expired" ||
    Boolean(task.blockingReason?.trim());
}
