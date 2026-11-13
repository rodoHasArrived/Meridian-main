import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson, apiPostJson } from "@/lib/api";
import {
  getDraftAttestationWorkspace, getPreparerDraftSnapshot, recordPreparerDraftCheck, revokePreparerDraftCheck,
  PREPARER_DRAFT_CHECK, PREPARER_DRAFT_POLICY
} from "./preparer-draft-check-api";
import { PREPARER_ATTESTATION_DISCLOSURE } from "./single-principal-attestation-api";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn(), apiGetBlob: vi.fn() }));
const security = "11111111-1111-4111-8111-111111111111";
const run = "22222222-2222-4222-8222-222222222222";
const snapshot = { subjectKind: "contractual-cashflow-draft", subjectId: `Security/${security}/${run}`,
  revision: "a".repeat(64), hash: "b".repeat(64), sourceHash: "c".repeat(64), sourceArtifactId: "run-artifact",
  sourceActor: "preparer", sourceProvenance: "Unclassified", preparedBy: "preparer", confirmation: "Confirm this exact non-postable calculation.",
  eligible: true, checks: ["Run replay verified"] };
beforeEach(() => vi.resetAllMocks());

describe("preparer draft API boundary", () => {
  it("reads only an exact retained run without fixture fallback", async () => {
    vi.mocked(apiGetJson).mockResolvedValue(snapshot);
    await expect(getPreparerDraftSnapshot(security, run, { allowDevelopmentFallback: true })).resolves.toEqual(snapshot);
    expect(apiGetJson).toHaveBeenCalledWith(`/api/workstation/evidence/attestations/drafts/${security}/${run}`, { allowDevelopmentFallback: false });
  });
  it.each(["subject", "hash", "preparer", "confirmation", "checks"])("refuses incomplete or mismatched %s facts", async condition => {
    const changed = { ...snapshot };
    if (condition === "subject") changed.subjectId = "Security/foreign/run";
    if (condition === "hash") changed.hash = "unverified";
    if (condition === "preparer") changed.preparedBy = "";
    if (condition === "confirmation") changed.confirmation = "";
    if (condition === "checks") changed.checks = [];
    vi.mocked(apiGetJson).mockResolvedValue(changed);
    await expect(getPreparerDraftSnapshot(security, run)).rejects.toThrow("does not match");
  });
  it("does not turn missing workspace facts into a disabled or zero-assertion claim", async () => {
    vi.mocked(apiGetJson).mockResolvedValue({});
    await expect(getDraftAttestationWorkspace({ allowDevelopmentFallback: true })).rejects.toThrow("complete scoped");
    expect(apiGetJson).toHaveBeenCalledWith("/api/workstation/evidence/attestations", { allowDevelopmentFallback: false });
  });
  it("refuses an arbitrary identity before requesting a snapshot", async () => {
    await expect(getPreparerDraftSnapshot("not-a-security-id", run)).rejects.toThrow("exact retained");
    expect(apiGetJson).not.toHaveBeenCalled();
  });
  it("sends exact operands and no asserted actor or scope, then validates the returned receipt", async () => {
    const request = { commandId: "command-1", expectedDeclarationRevision: 4, actionId: PREPARER_DRAFT_CHECK,
      subjectKind: snapshot.subjectKind, subjectId: snapshot.subjectId, subjectRevision: snapshot.revision,
      subjectHash: snapshot.hash, reason: "Inspected this calculation", confirmation: snapshot.confirmation };
    const receipt = { ...request, receiptId: "receipt-1", schemaVersion: 1, decisionKind: "PreparerAttestation",
      disclosure: PREPARER_ATTESTATION_DISCLOSURE, policyVersion: PREPARER_DRAFT_POLICY };
    vi.mocked(apiPostJson).mockResolvedValue(receipt);
    await expect(recordPreparerDraftCheck(request)).resolves.toEqual(receipt);
    expect(apiPostJson).toHaveBeenCalledWith("/api/workstation/evidence/attestations/receipts", request, { allowDevelopmentFallback: false });
    expect(request).not.toHaveProperty("actor"); expect(request).not.toHaveProperty("tenantId"); expect(request).not.toHaveProperty("scope");
    vi.mocked(apiPostJson).mockResolvedValue({ ...receipt, commandId: "foreign-command" });
    await expect(recordPreparerDraftCheck(request)).rejects.toThrow("outcome is unconfirmed");
  });
  it("refuses an unsupported action and a mismatched revocation response", async () => {
    const request = { commandId: "command-1", expectedDeclarationRevision: 4, actionId: "Approve",
      subjectKind: snapshot.subjectKind, subjectId: snapshot.subjectId, subjectRevision: snapshot.revision,
      subjectHash: snapshot.hash, reason: "Inspected", confirmation: snapshot.confirmation };
    await expect(recordPreparerDraftCheck(request)).rejects.toThrow("Only the named");
    expect(apiPostJson).not.toHaveBeenCalled();
    vi.mocked(apiPostJson).mockResolvedValue({ receiptId: "wrong", commandId: "command-2", revision: 2 });
    await expect(revokePreparerDraftCheck({ receiptId: "receipt-1", commandId: "command-2", expectedReceiptRevision: 1, reason: "Correction" }))
      .rejects.toThrow("outcome is unconfirmed");
  });
});
