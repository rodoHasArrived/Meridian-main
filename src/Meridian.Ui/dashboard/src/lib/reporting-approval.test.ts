import { describe, expect, it } from "vitest";
import {
  buildAttestation,
  evaluateApproval,
  normalizeApprovalTransition,
  resolveAttestationRequirements,
  type ApprovalRecordInput
} from "@/lib/reporting-approval";

function approved(overrides: Partial<ApprovalRecordInput> = {}): ApprovalRecordInput {
  return {
    transition: "Approve",
    person: "Jane Smith",
    timestampUtc: "2026-09-30T20:42:00Z",
    version: "09",
    dataFingerprint: "CLOSE-2026-09-v4",
    ...overrides
  };
}

describe("evaluateApproval", () => {
  it("reports an approval that still covers the current version and data", () => {
    const model = evaluateApproval({
      records: [approved()],
      currentVersion: "09",
      currentDataFingerprint: "CLOSE-2026-09-v4"
    });

    expect(model.status).toBe("Approved");
    expect(model.severity).toBe("ready");
    expect(model.reason).toContain("Jane Smith");
    expect(model.versionMoved).toBe(false);
    expect(model.dataMoved).toBe(false);
  });

  it("does not leave an approval standing when the version moves", () => {
    const model = evaluateApproval({
      records: [approved()],
      currentVersion: "10",
      currentDataFingerprint: "CLOSE-2026-09-v4"
    });

    expect(model.status).toBe("ChangesRequireReview");
    expect(model.severity).toBe("action");
    expect(model.versionMoved).toBe(true);
    expect(model.reason).toContain("Previously approved");
    expect(model.reason).toContain("the version");
  });

  it("does not leave an approval standing when the data moves underneath it", () => {
    const model = evaluateApproval({
      records: [approved()],
      currentVersion: "09",
      currentDataFingerprint: "CLOSE-2026-09-v5"
    });

    expect(model.status).toBe("ChangesRequireReview");
    expect(model.dataMoved).toBe(true);
    expect(model.reason).toContain("the underlying data");
  });

  it("names both when the version and the data moved", () => {
    const model = evaluateApproval({
      records: [approved()],
      currentVersion: "10",
      currentDataFingerprint: "CLOSE-2026-09-v5"
    });

    expect(model.reason).toContain("the version and the underlying data");
  });

  it("does not judge a data state as if it were a fingerprint", () => {
    // The report is still "Confirmed", but every number underneath it moved.
    const model = evaluateApproval({
      records: [approved({ dataState: "Confirmed", dataFingerprint: "CLOSE-2026-09-v4" })],
      currentVersion: "09",
      currentDataFingerprint: "CLOSE-2026-09-v9"
    });

    expect(model.status).toBe("ChangesRequireReview");
  });

  it("reports an untied approval as unverifiable rather than approved", () => {
    const model = evaluateApproval({
      records: [approved({ version: null })],
      currentVersion: "09",
      currentDataFingerprint: "CLOSE-2026-09-v4"
    });

    expect(model.status).toBe("Indeterminate");
    expect(model.severity).toBe("action");
  });

  it("reports an approval with no data fingerprint as unverifiable", () => {
    const model = evaluateApproval({
      records: [approved({ dataFingerprint: null })],
      currentVersion: "09",
      currentDataFingerprint: "CLOSE-2026-09-v4"
    });

    expect(model.status).toBe("Indeterminate");
  });

  it("approves when the caller tracks no fingerprint at all", () => {
    const model = evaluateApproval({ records: [approved({ dataFingerprint: null })], currentVersion: "09" });

    expect(model.status).toBe("Approved");
  });

  it("reports no approval when none was recorded", () => {
    const model = evaluateApproval({
      records: [{ transition: "Review", person: "Ann Lee" }],
      currentVersion: "09"
    });

    expect(model.status).toBe("NotApproved");
    expect(model.approval).toBeNull();
  });

  it("takes the most recent approval when several exist", () => {
    const model = evaluateApproval({
      records: [
        approved({ person: "Early Approver", timestampUtc: "2026-09-01T10:00:00Z", version: "08" }),
        approved({ person: "Jane Smith", timestampUtc: "2026-09-30T20:42:00Z", version: "09" })
      ],
      currentVersion: "09",
      currentDataFingerprint: "CLOSE-2026-09-v4"
    });

    expect(model.approval?.person).toBe("Jane Smith");
    expect(model.status).toBe("Approved");
  });

  it("orders records by timestamp", () => {
    const model = evaluateApproval({
      records: [
        approved({ transition: "Approve", timestampUtc: "2026-09-30T20:42:00Z" }),
        approved({ transition: "Prepare", timestampUtc: "2026-09-28T09:00:00Z" })
      ]
    });

    expect(model.records.map((record) => record.transition)).toEqual(["Prepare", "Approve"]);
  });

  it("falls back to lifecycle order when timestamps are unusable", () => {
    const model = evaluateApproval({
      records: [
        { transition: "Publish" },
        { transition: "Prepare" },
        { transition: "Review" }
      ]
    });

    expect(model.records.map((record) => record.transition)).toEqual(["Prepare", "Review", "Publish"]);
  });

  it("marks an unattributed record rather than inventing a person", () => {
    const model = evaluateApproval({ records: [approved({ person: "  " })] });

    expect(model.records[0]?.person).toBe("Unattributed");
    expect(model.records[0]?.isAttributed).toBe(false);
  });

  it("builds the transition strip from records and the workflow state", () => {
    const model = evaluateApproval({
      records: [{ transition: "Prepare" }, { transition: "Review" }],
      workflowState: "ReadyForApproval"
    });

    expect(model.marks).toEqual({
      Prepare: "complete",
      Review: "complete",
      Approve: "current",
      Publish: "pending"
    });
  });
});

describe("normalizeApprovalTransition", () => {
  it("accepts common spellings", () => {
    expect(normalizeApprovalTransition("approved")).toBe("Approve");
    expect(normalizeApprovalTransition("PUBLICATION")).toBe("Publish");
    expect(normalizeApprovalTransition("re_view")).toBe("Review");
  });

  it("resolves an unknown transition to the least privileged stage", () => {
    expect(normalizeApprovalTransition("rubber-stamp")).toBe("Prepare");
    expect(normalizeApprovalTransition(null)).toBe("Prepare");
  });
});

describe("resolveAttestationRequirements", () => {
  it("requires everything for regulatory output and less for analytical", () => {
    expect(resolveAttestationRequirements("Regulatory")).toHaveLength(5);
    expect(resolveAttestationRequirements("Analytical")).toEqual([
      "sectionsComplete",
      "exceptionsDisclosed"
    ]);
  });

  it("lets a template add requirements but not remove the class floor", () => {
    const resolved = resolveAttestationRequirements("Analytical", ["reconciliationPassed"]);

    expect(resolved).toContain("sectionsComplete");
    expect(resolved).toContain("exceptionsDisclosed");
    expect(resolved).toContain("reconciliationPassed");
  });

  it("ignores an unrecognized additional requirement", () => {
    expect(resolveAttestationRequirements("Analytical", ["vibes"])).toEqual([
      "sectionsComplete",
      "exceptionsDisclosed"
    ]);
  });
});

describe("buildAttestation", () => {
  const ALL_CONFIRMED = {
    sectionsComplete: true,
    exceptionsDisclosed: true,
    sourceCoverageMeetsPolicy: true,
    reconciliationPassed: true,
    commentaryReviewed: true
  } as const;

  it("attests when every statement is affirmed and signed", () => {
    const model = buildAttestation({
      reportClass: "Accounting",
      confirmations: ALL_CONFIRMED,
      attestedBy: "Jane Smith",
      attestedAtUtc: "2026-09-30T20:42:00Z"
    });

    expect(model.isAttested).toBe(true);
    expect(model.severity).toBe("ready");
    expect(model.summary).toBe("All 5 statements affirmed by Jane Smith.");
  });

  it("does not attest when a statement is unanswered", () => {
    const model = buildAttestation({
      reportClass: "Accounting",
      confirmations: { ...ALL_CONFIRMED, commentaryReviewed: null },
      attestedBy: "Jane Smith"
    });

    expect(model.isAttested).toBe(false);
    expect(model.unansweredRequirements).toEqual(["commentaryReviewed"]);
    expect(model.severity).toBe("review");
  });

  it("treats a missing confirmation key as unanswered, not affirmed", () => {
    const model = buildAttestation({
      reportClass: "Accounting",
      confirmations: { sectionsComplete: true },
      attestedBy: "Jane Smith"
    });

    expect(model.isAttested).toBe(false);
    expect(model.unansweredRequirements).toHaveLength(4);
  });

  it("does not attest when a statement is denied", () => {
    const model = buildAttestation({
      reportClass: "Accounting",
      confirmations: { ...ALL_CONFIRMED, reconciliationPassed: false },
      attestedBy: "Jane Smith"
    });

    expect(model.isAttested).toBe(false);
    expect(model.deniedRequirements).toEqual(["reconciliationPassed"]);
    expect(model.severity).toBe("action");
  });

  it("does not accept an unsigned attestation", () => {
    const model = buildAttestation({ reportClass: "Accounting", confirmations: ALL_CONFIRMED });

    expect(model.isAttested).toBe(false);
    expect(model.summary).toContain("unsigned");
  });

  it("renders one line per required statement with its label", () => {
    const model = buildAttestation({ reportClass: "Analytical", confirmations: {}, attestedBy: "A" });

    expect(model.lines.map((line) => line.label)).toEqual([
      "Required sections are complete",
      "Material exceptions are disclosed"
    ]);
  });
});
