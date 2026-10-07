import { describe, expect, it } from "vitest";
import type { OperationsContinuityWorkflow, OperationsEvidenceLink } from "@/types";
import { collectCloseWorkflowEvidenceLinks, workflowMatchesPublicationSnapshot } from "./operations-continuity-screen.workflow-selection";

const evidence: OperationsEvidenceLink = {
  evidenceId: "report-evidence", label: "Retained report evidence", route: "/report/1",
  source: "reporting", capturedAtUtc: "2026-10-06T18:00:00Z"
};

function workflow(): OperationsContinuityWorkflow {
  return {
    workflowId: "workflow", fundAccountId: "account", ledgerBookId: "book", periodId: "2026-10",
    securityMasterSnapshotId: null, brokerSource: "custodian", status: "ReadyForClose", version: 4,
    createdAtUtc: "2026-10-01T18:00:00Z", updatedAtUtc: "2026-10-06T18:00:00Z", gates: [], nextActions: [],
    brokerIntakeState: "Complete", securityMasterState: "Complete", ledgerPostingState: "Complete",
    reconciliationState: "Complete", approvalState: "Approved", timeline: [], breakCases: [], ledgerPreview: null,
    approvals: [{ approvalId: "approval", status: "Approved", operator: "operator", reviewer: "reviewer",
      rationale: null, submittedAtUtc: "2026-10-06T17:00:00Z", decidedAtUtc: "2026-10-06T18:00:00Z",
      evidenceLinks: [{ ...evidence, evidenceId: "approval-evidence" }] }],
    reportPackReadiness: { isReady: true, reportPackId: "report-pack", blockingReason: null, evidenceLinks: [evidence] },
    closeChecklist: [], closeReadiness: null, closePackage: null,
    evidenceLinks: [{ ...evidence, evidenceId: "workflow-evidence" }], blockers: [],
    evidencePackages: [{ packageId: "package", label: "Close package", status: "Ready", isReady: true,
      summary: "Retained", routeHint: null, completeCategoryCount: 1, requiredCategoryCount: 1, evidenceLinkCount: 1,
      evidenceLinks: [{ ...evidence, evidenceId: "package-evidence" }] }]
  };
}

describe("publication snapshot consistency", () => {
  const mismatches: [string, (value: OperationsContinuityWorkflow) => void][] = [
    ["report pack", (value) => { value.reportPackReadiness.reportPackId = "repaired-report-pack"; }],
    ["report readiness", (value) => { value.reportPackReadiness.isReady = false; }],
    ["report evidence ID", (value) => { value.reportPackReadiness.evidenceLinks = [{ ...evidence, evidenceId: "new-evidence" }]; }],
    ["report evidence source", (value) => { value.reportPackReadiness.evidenceLinks = [{ ...evidence, source: "other-source" }]; }],
    ["report evidence route", (value) => { value.reportPackReadiness.evidenceLinks = [{ ...evidence, route: "/report/2" }]; }],
    ["report evidence capture time", (value) => { value.reportPackReadiness.evidenceLinks = [{ ...evidence, capturedAtUtc: "2026-10-06T19:00:00Z" }]; }],
    ["report evidence sub-millisecond capture time", (value) => { value.reportPackReadiness.evidenceLinks = [{ ...evidence, capturedAtUtc: "2026-10-06T18:00:00.0000001Z" }]; }],
    ["workflow evidence", (value) => { value.evidenceLinks = [{ ...evidence, evidenceId: "repaired-workflow-evidence" }]; }],
    ["package evidence", (value) => { value.evidencePackages![0]!.evidenceLinks = []; }],
    ["approval evidence", (value) => { value.approvals[0]!.evidenceLinks = []; }]
  ];

  it.each(mismatches)("blocks unchanged workflow versions with mismatched %s in either response", (_name, change) => {
    const current = workflow();
    const repaired = workflow();
    change(repaired);
    expect(repaired.version).toBe(current.version);
    expect(workflowMatchesPublicationSnapshot(current, repaired)).toBe(false);
    expect(workflowMatchesPublicationSnapshot(repaired, current)).toBe(false);
    expect(workflowMatchesPublicationSnapshot(repaired, structuredClone(repaired))).toBe(true);
  });

  it("does not mask a changed report evidence set with the same evidence elsewhere in the command", () => {
    const current = workflow();
    const repaired = workflow();
    current.evidenceLinks.push(evidence);
    repaired.evidenceLinks.push(evidence);
    repaired.reportPackReadiness.evidenceLinks = [];
    expect(workflowMatchesPublicationSnapshot(current, repaired)).toBe(false);
  });

  it("ignores labels and collection order", () => {
    const current = workflow();
    current.reportPackReadiness.evidenceLinks.push({ ...evidence, evidenceId: "second-report" });
    const same = structuredClone(current);
    same.reportPackReadiness.evidenceLinks.reverse();
    same.reportPackReadiness.evidenceLinks = same.reportPackReadiness.evidenceLinks.map((link) => ({
      ...link, label: "Updated display label"
    }));
    expect(workflowMatchesPublicationSnapshot(current, same)).toBe(true);
  });

  it("matches the close command's first evidence identity when later sources repeat an ID", () => {
    const current = workflow();
    const same = workflow();
    const repeated = { ...evidence, route: "/superseded-report", capturedAtUtc: "2026-10-05T18:00:00Z" };
    same.evidenceLinks.push(repeated);
    same.evidencePackages![0]!.evidenceLinks.push(repeated);
    same.approvals[0]!.evidenceLinks.push(repeated);
    expect(collectCloseWorkflowEvidenceLinks(same).filter((link) => link.evidenceId === evidence.evidenceId)).toEqual([evidence]);
    expect(workflowMatchesPublicationSnapshot(current, same)).toBe(true);
    same.reportPackReadiness.evidenceLinks = [repeated];
    expect(workflowMatchesPublicationSnapshot(current, same)).toBe(false);
  });

  it("requires both independently loaded workflows", () => {
    expect(workflowMatchesPublicationSnapshot(null, workflow())).toBe(false);
    expect(workflowMatchesPublicationSnapshot(workflow(), undefined)).toBe(false);
  });
});
