import { describe, expect, it } from "vitest";
import type { OperationsContinuityWorkflow, OperationsEvidenceLink } from "@/types";
import { collectCloseWorkflowChecklistControlApprovals, collectCloseWorkflowEvidenceLinks, workflowMatchesPublicationSnapshot } from "./operations-continuity-screen.workflow-selection";

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
    reportPackReadiness: { isReady: true, reportPackId: "report-pack", blockingReason: null, evidenceLinks: [{ ...evidence }] },
    closeChecklist: [{
      taskId: "ledger-control", gate: "LedgerPosting", label: "Ledger control", owner: "owner",
      requiredEvidence: "Retained ledger", dueDate: null, requiredApprovalCount: 1, expiresOn: null,
      status: "Done", blockingReason: null, evidencePointer: "ledger-evidence", remediationRoute: null,
      canAcknowledge: false, acknowledgedAtUtc: "2026-10-06T16:00:00Z", acknowledgedBy: "ledger-operator"
    }], closeReadiness: null, closePackage: null,
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
    ["approval evidence", (value) => { value.approvals[0]!.evidenceLinks = []; }],
    ["checklist task identity", (value) => { value.closeChecklist[0]!.taskId = "replacement-control"; }],
    ["checklist acknowledgement actor", (value) => { value.closeChecklist[0]!.acknowledgedBy = "replacement-operator"; }],
    ["checklist acknowledgement instant", (value) => { value.closeChecklist[0]!.acknowledgedAtUtc = "2026-10-06T16:00:00.0000001Z"; }],
    ["missing checklist acknowledgement", (value) => { value.closeChecklist[0]!.acknowledgedAtUtc = null; }],
    ["missing checklist evidence pointer", (value) => { value.closeChecklist[0]!.evidencePointer = null; }],
    ["blocked checklist control", (value) => { value.closeChecklist[0]!.blockingReason = "Repair this control"; }],
    ["expired checklist control", (value) => { value.closeChecklist[0]!.status = "Expired"; }],
    ["approval task identity", (value) => { value.closeChecklist.push({ ...value.closeChecklist[0]!, gate: "Approval", taskId: "custom-approval" }); }]
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

  const approvalChanges: [string, (value: OperationsContinuityWorkflow, submissionIndex: number) => void][] = [
    ["submission actor", (value, index) => { value.approvals[index]!.operator = "new-submitter"; }],
    ["submission instant", (value, index) => { value.approvals[index]!.submittedAtUtc = "2026-10-06T17:00:00.0000001Z"; }],
    ["reviewer identity", (value) => { value.approvals.at(-1)!.reviewer = "new-reviewer"; }],
    ["reviewer instant", (value) => { value.approvals.at(-1)!.decidedAtUtc = "2026-10-06T18:00:00.0000001Z"; }]
  ];
  describe.each(["current", "legacy"] as const)("%s approval history", (kind) => {
    it.each(approvalChanges)("blocks changed %s and accepts its matching repair", (_name, change) => {
      const current = workflow();
      if (kind === "legacy") {
        const approval = current.approvals[0]!;
        current.approvals = [
          { ...approval, approvalId: "submission", status: "Submitted", decidedAtUtc: null },
          { ...approval, operator: null, submittedAtUtc: null }
        ];
      }
      const repaired = structuredClone(current);
      change(repaired, 0);
      expect(collectCloseWorkflowChecklistControlApprovals(current)).toHaveLength(3);
      expect(workflowMatchesPublicationSnapshot(current, repaired)).toBe(false);
      expect(workflowMatchesPublicationSnapshot(repaired, current)).toBe(false);
      expect(workflowMatchesPublicationSnapshot(repaired, structuredClone(repaired))).toBe(true);
    });
  });

  it("retains the command's first approval identity and ignores labels, actor casing and checklist order", () => {
    const current = workflow();
    const same = structuredClone(current);
    same.closeChecklist[0]!.taskId = " LEDGER-CONTROL ";
    same.closeChecklist[0]!.acknowledgedBy = " LEDGER-OPERATOR ";
    same.closeChecklist[0]!.label = "Renamed control";
    same.closeChecklist.push({ ...current.closeChecklist[0]!, acknowledgedAtUtc: "2026-10-06T16:01:00Z" });
    expect(collectCloseWorkflowChecklistControlApprovals(same)).toHaveLength(3);
    expect(workflowMatchesPublicationSnapshot(current, same)).toBe(true);
    same.closeChecklist.reverse();
    expect(workflowMatchesPublicationSnapshot(current, same)).toBe(false);

    const reordered = structuredClone(current);
    current.closeChecklist.push({ ...current.closeChecklist[0]!, taskId: "second-control" });
    reordered.closeChecklist.unshift({ ...reordered.closeChecklist[0]!, taskId: "second-control" });
    expect(workflowMatchesPublicationSnapshot(current, reordered)).toBe(true);
  });

  it.each([
    ["2026-10-07T12:00:00Z", "2026-10-07T05:00:00-07:00"],
    ["2026-10-07T12:00:00.1234567Z", "2026-10-07T17:30:00.1234567+05:30"],
    ["2024-02-29T10:00:00.1000000Z", "2024-03-01T00:00:00.1+14:00"],
    ["1969-12-31T23:59:59.9999999Z", "1970-01-01T00:59:59.9999999+01:00"]
  ])("accepts evidence and approval timestamps identifying the same precise instant: %s", (utc, offset) => {
    const current = workflow();
    const same = workflow();
    for (const [value, timestamp] of [[current, utc], [same, offset]] as const) {
      value.reportPackReadiness.evidenceLinks[0]!.capturedAtUtc = timestamp;
      value.closeChecklist[0]!.acknowledgedAtUtc = timestamp;
      value.approvals[0]!.submittedAtUtc = timestamp;
      value.approvals[0]!.decidedAtUtc = timestamp;
    }
    expect(workflowMatchesPublicationSnapshot(current, same)).toBe(true);
    same.reportPackReadiness.evidenceLinks[0]!.capturedAtUtc = "2026-10-07T05:00:00.0000001-07:00";
    expect(workflowMatchesPublicationSnapshot(current, same)).toBe(false);
  });

  it("distinguishes a 100 ns capture or approval change across equivalent offset formats", () => {
    const current = workflow();
    current.reportPackReadiness.evidenceLinks[0]!.capturedAtUtc = "2026-10-07T12:00:00.1234567Z";
    current.closeChecklist[0]!.acknowledgedAtUtc = "2026-10-07T12:00:00.1234567Z";
    const changed = structuredClone(current);
    changed.reportPackReadiness.evidenceLinks[0]!.capturedAtUtc = "2026-10-07T17:30:00.1234568+05:30";
    expect(workflowMatchesPublicationSnapshot(current, changed)).toBe(false);
    changed.reportPackReadiness.evidenceLinks[0]!.capturedAtUtc = "2026-10-07T17:30:00.1234567+05:30";
    changed.closeChecklist[0]!.acknowledgedAtUtc = "2026-10-07T17:30:00.1234568+05:30";
    expect(workflowMatchesPublicationSnapshot(current, changed)).toBe(false);
  });

  it.each([
    "not-a-timestamp", "2026-02-30T12:00:00Z", "2026-10-07T12:00:00",
    "2026-10-07T12:00:00+14:01", "2026-10-07T12:00:00.12345678Z"
  ])("fails closed for malformed timestamp %s in both reads", (invalid) => {
    const malformedEvidence = workflow();
    malformedEvidence.reportPackReadiness.evidenceLinks[0]!.capturedAtUtc = invalid;
    expect(workflowMatchesPublicationSnapshot(malformedEvidence, structuredClone(malformedEvidence))).toBe(false);
    const malformedApproval = workflow();
    malformedApproval.closeChecklist[0]!.acknowledgedAtUtc = invalid;
    expect(workflowMatchesPublicationSnapshot(malformedApproval, structuredClone(malformedApproval))).toBe(false);
  });
});
