import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson, apiPostJson } from "@/lib/api";
import { getAttestationSubject, getAttestationWorkspace, recordPreparerAttestation } from "./single-principal-attestation-api";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));
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
});
