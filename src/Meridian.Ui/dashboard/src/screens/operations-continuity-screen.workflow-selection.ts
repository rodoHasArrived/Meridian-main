import type { OperationsContinuityWorkflow, OperationsContinuityWorkflowSummary, OperationsEvidenceLink } from "@/types";

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
    && evidenceIdentitiesMatch(collectCloseWorkflowEvidenceLinks(detail), collectCloseWorkflowEvidenceLinks(decisionWorkflow)));
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
  const leftIdentities = evidenceIdentities(left);
  const rightIdentities = evidenceIdentities(right);
  return leftIdentities.size === rightIdentities.size
    && [...leftIdentities].every(([id, identity]) => rightIdentities.get(id) === identity);
}

function evidenceIdentities(links: OperationsEvidenceLink[]): Map<string, string> {
  const identities = new Map<string, string>();
  for (const link of links) {
    // Publication retains the first link for an evidence ID, in this same source order.
    if (!identities.has(link.evidenceId)) {
      identities.set(link.evidenceId, JSON.stringify([
        link.source, link.route, link.capturedAtUtc
      ]));
    }
  }
  return identities;
}
