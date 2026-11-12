import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetBlob, apiGetJson, apiPostJson } from "@/lib/api";
import { getAttestationSubject, getAttestationWorkspace, getPreparerAttestationProof,
  PREPARER_ATTESTATION_DISCLOSURE, recordPreparerAttestation } from "./single-principal-attestation-api";

vi.mock("@/lib/api", () => ({ apiGetBlob: vi.fn(), apiGetJson: vi.fn(), apiPostJson: vi.fn() }));
beforeEach(() => vi.resetAllMocks());
describe("attestation API authority boundary", () => {
  it("prohibits fixture fallback on workspace and subject reads", async () => {
    vi.mocked(apiGetJson).mockResolvedValue({ subjectKind: "evidence-document", subjectId: "vault/document", revision: "r", hash: "h", sourceHash: "s", checks: [] });
    await getAttestationWorkspace();
    await getAttestationSubject("vault", "document", { allowDevelopmentFallback: true });
    expect(apiGetJson).toHaveBeenNthCalledWith(1, "/api/workstation/evidence/attestations", { allowDevelopmentFallback: false });
    expect(apiGetJson).toHaveBeenNthCalledWith(2, "/api/workstation/evidence/attestations/subjects?vaultId=vault&documentId=document", { allowDevelopmentFallback: false });
  });
  it("refuses a response for a different retained document", async () => {
    vi.mocked(apiGetJson).mockResolvedValue({ subjectKind: "evidence-document", subjectId: "other/document", revision: "r", hash: "h", sourceHash: "s", checks: [] });
    await expect(getAttestationSubject("vault", "document")).rejects.toThrow("does not match");
  });
  it("uses the existing mutation helper without supplying actor or scope assertions", async () => {
    const request = { commandId: "command", expectedDeclarationRevision: 2, actionId: "ATTEST-EVIDENCE-ACK", subjectKind: "evidence-document", subjectId: "vault/document", subjectRevision: "r", subjectHash: "h", reason: "Inspected", confirmation: "Confirmed" };
    await recordPreparerAttestation(request);
    expect(apiPostJson).toHaveBeenCalledWith("/api/workstation/evidence/attestations/receipts", request, {});
  });
  it("downloads the matching original proof bytes without fixture fallback or JSON rewriting", async () => {
    const raw = '{"exactAmount":12345678901234567890.123456789}';
    const proof = { schemaVersion: 1, kind: "PreparerAttestationEvidence", disclosure: PREPARER_ATTESTATION_DISCLOSURE,
      receipt: { receiptId: "receipt-one", schemaVersion: 1, decisionKind: "PreparerAttestation", disclosure: PREPARER_ATTESTATION_DISCLOSURE }, raw };
    const original = { text: () => Promise.resolve(JSON.stringify(proof)), type: "application/json" } as Blob;
    vi.mocked(apiGetBlob).mockResolvedValue(original);
    expect(await getPreparerAttestationProof("receipt-one", { allowDevelopmentFallback: true })).toBe(original);
    expect(apiGetBlob).toHaveBeenCalledWith("/api/workstation/evidence/attestations/receipts/receipt-one/export", { allowDevelopmentFallback: false });
  });
  it.each(["foreign-receipt", "unsupported-schema", "ordinary-approval"])("refuses %s proof returned from the artifact route", async condition => {
    const proof = { schemaVersion: 1, kind: "PreparerAttestationEvidence", disclosure: PREPARER_ATTESTATION_DISCLOSURE,
      receipt: { receiptId: "receipt-one", schemaVersion: 1, decisionKind: "PreparerAttestation", disclosure: PREPARER_ATTESTATION_DISCLOSURE } };
    if (condition === "foreign-receipt") proof.receipt.receiptId = "someone-else";
    if (condition === "unsupported-schema") proof.schemaVersion = 999;
    if (condition === "ordinary-approval") proof.receipt.decisionKind = "IndependentReview";
    vi.mocked(apiGetBlob).mockResolvedValue({ text: () => Promise.resolve(JSON.stringify(proof)) } as Blob);
    await expect(getPreparerAttestationProof("receipt-one")).rejects.toThrow("does not match");
  });
  it("does not replace a denied proof read with a local artifact", async () => {
    vi.mocked(apiGetBlob).mockRejectedValue(new Error("Read permission denied"));
    await expect(getPreparerAttestationProof("receipt-one")).rejects.toThrow("Read permission denied");
  });
});
