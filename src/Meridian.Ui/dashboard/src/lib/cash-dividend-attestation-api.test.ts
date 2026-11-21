import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson, apiPostJson } from "@/lib/api";
import { getCashDividendAttestationWorkspace, changeCashDividendBookDeclaration, attestCashDividend, postAttestedCashDividend } from "./cash-dividend-attestation-api";
import { cashDividendCaseId, cashDividendScope, cashDividendWorkspace, retainedCashDividendAttestation } from "@/test/cash-dividend-attestation-fixtures";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));
beforeEach(() => vi.resetAllMocks());

describe("native cash-dividend API boundary", () => {
  it("forbids fixture fallback on authoritative workspace reads", async () => {
    vi.mocked(apiGetJson).mockResolvedValue(cashDividendWorkspace());
    await getCashDividendAttestationWorkspace(cashDividendCaseId, cashDividendScope, { allowDevelopmentFallback: true });
    expect(apiGetJson).toHaveBeenCalledWith(`/api/security-master/corporate-actions/cases/${cashDividendCaseId}/cash-dividend-attestation`,
      { allowDevelopmentFallback: false });
  });
  it.each(["case", "book", "tenant", "projection", "attestation"])("refuses an unrelated %s binding", async condition => {
    const value = cashDividendWorkspace();
    if (condition === "case") value.case.caseId = "different-case";
    if (condition === "book") value.scope = { ...value.scope, ledgerBookId: "different-book" };
    if (condition === "tenant") value.scope = { ...value.scope, tenantId: "different-tenant" };
    if (condition === "projection") value.preview!.projection.caseId = "different-case";
    if (condition === "attestation") value.attestation = { ...retainedCashDividendAttestation(value), caseId: "different-case" };
    vi.mocked(apiGetJson).mockResolvedValue(value);
    await expect(getCashDividendAttestationWorkspace(cashDividendCaseId, cashDividendScope)).rejects.toThrow(/does not match|belongs to/);
  });
  it("keeps attestation identity distinct from approval and checks uncertain mutation responses", async () => {
    const value = cashDividendWorkspace(); const attestation = retainedCashDividendAttestation(value);
    const attestRequest = { idempotencyKey: "command-one", expectedVersion: 5, projectionId: attestation.projectionId,
      expectedDeclarationRevision: 2, expectedEligibilityHash: attestation.entitlementSnapshotHash,
      reason: "Reviewed", evidenceReference: value.evidenceReference!, evidenceHash: value.evidenceHash!, confirmation: value.confirmation };
    vi.mocked(apiPostJson).mockResolvedValue({ case: value.case, attestation });
    await attestCashDividend(cashDividendCaseId, attestRequest);
    expect(apiPostJson).toHaveBeenCalledWith(`/api/security-master/corporate-actions/cases/${cashDividendCaseId}/cash-dividend-attestation/attest`, attestRequest, {});
    vi.mocked(apiPostJson).mockResolvedValue({ case: value.case, posting: { caseId: cashDividendCaseId,
      projectionId: attestation.projectionId, attestationId: "different-attestation" } });
    await expect(postAttestedCashDividend(cashDividendCaseId, { idempotencyKey: "post-one", expectedVersion: 6,
      projectionId: attestation.projectionId, attestationId: attestation.attestationId, reason: "Post reviewed" })).rejects.toThrow("does not match");
  });
  it("does not accept a declaration response that changed the explicitly confirmed accounting roles", async () => {
    const value = cashDividendWorkspace();
    const request = { commandId: "declare-one", expectedRevision: 1, ledgerBookId: cashDividendScope.ledgerBookId,
      enabled: true, policyVersion: value.policyVersion, policyHash: value.policyHash, reason: "Confirmed exact existing accounts",
      accountRoleBinding: value.preview!.eligibility.accountRoleBinding };
    vi.mocked(apiPostJson).mockResolvedValue({ declaration: { ...value.declaration,
      accountRoleBinding: { ...request.accountRoleBinding, dividendReceivableAccount: "changed-account" } } });
    await expect(changeCashDividendBookDeclaration(cashDividendCaseId, request)).rejects.toThrow("does not match this exact command");
  });
});
