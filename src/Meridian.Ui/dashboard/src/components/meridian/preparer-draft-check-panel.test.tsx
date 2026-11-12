import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { PreparerDraftCheckPanel } from "./preparer-draft-check-panel";
import * as api from "@/lib/preparer-draft-check-api";
import * as common from "@/lib/single-principal-attestation-api";
import { ApiError } from "@/lib/api-errors";
import type { PreparerReceipt } from "@/lib/single-principal-attestation-api";

vi.mock("@/lib/preparer-draft-check-api", async importOriginal => ({
  ...await importOriginal<typeof import("@/lib/preparer-draft-check-api")>(), getDraftAttestationWorkspace: vi.fn(),
  getPreparerDraftSnapshot: vi.fn(), recordPreparerDraftCheck: vi.fn(), revokePreparerDraftCheck: vi.fn()
}));
vi.mock("@/lib/single-principal-attestation-api", async importOriginal => ({
  ...await importOriginal<typeof import("@/lib/single-principal-attestation-api")>(),
  changePrincipalDeclaration: vi.fn(), getPreparerAttestationProof: vi.fn()
}));
const security = "11111111-1111-4111-8111-111111111111";
const run = "22222222-2222-4222-8222-222222222222";
const scope = { tenantId: "tenant-one", companyId: "company-one", ledgerBookId: null };
const target: api.PreparerDraftSnapshot = { subjectKind: "contractual-cashflow-draft", subjectId: `Security/${security}/${run}`,
  revision: "a".repeat(64), hash: "b".repeat(64), sourceHash: "c".repeat(64), sourceArtifactId: "run-artifact", sourceActor: "preparer",
  sourceProvenance: "Synthetic", preparedBy: "preparer", confirmation: "I checked this exact retained non-postable calculation. This is not approval.",
  eligible: true, checks: ["Exact run replay verified.", "No posting authority."] };
function initial(): api.DraftAttestationWorkspace {
  return { actor: "preparer", canAdministerDeclaration: true, canAttest: true, canRevoke: true, canAttestDrafts: true, canRevokeDrafts: true,
    state: { scope, policyEnabled: true, policyVersion: api.PREPARER_DRAFT_POLICY, policyHash: "d".repeat(64),
      disclosure: common.PREPARER_ATTESTATION_DISCLOSURE, confirmation: "Evidence ACK confirmation is different.", allowedActions: [api.PREPARER_DRAFT_CHECK],
      declaration: { declarationId: "declaration-1", revision: 4, scope, enabled: true, principalId: "preparer",
        policyVersion: api.PREPARER_DRAFT_POLICY, policyHash: "d".repeat(64), changedBy: "preparer", recordedAt: "2026-11-12T12:00:00Z", reason: "Solo scope" },
      declarationHistory: [], receipts: [], revocations: [], auditTrail: [] } };
}
function receipt(): PreparerReceipt {
  return { receiptId: "receipt-1", commandId: "command-1", assertionId: "command-1", schemaVersion: 1,
    decisionKind: "PreparerAttestation", disclosure: common.PREPARER_ATTESTATION_DISCLOSURE, scope,
    actionId: api.PREPARER_DRAFT_CHECK, subjectKind: target.subjectKind, subjectId: target.subjectId,
    subjectRevision: target.revision, subjectHash: target.hash, sourceArtifactId: target.sourceArtifactId, sourceHash: target.sourceHash,
    sourceActor: "preparer", sourceProvenance: "Synthetic", preparedBy: "preparer", attestedBy: "preparer", origin: "HumanOperator",
    declarationId: "declaration-1", declarationRevision: 4, policyVersion: api.PREPARER_DRAFT_POLICY, policyHash: "d".repeat(64),
    recordedAt: "2026-11-12T12:01:00Z", reason: "Inspected", confirmation: target.confirmation, checks: target.checks };
}
let current = initial();
beforeEach(() => {
  vi.resetAllMocks(); current = initial();
  vi.mocked(api.getDraftAttestationWorkspace).mockImplementation(async () => structuredClone(current));
  vi.mocked(api.getPreparerDraftSnapshot).mockResolvedValue(target);
  vi.mocked(api.recordPreparerDraftCheck).mockImplementation(async request => {
    const value = { ...receipt(), ...request }; current.state.receipts.push(value); return value;
  });
  vi.mocked(common.changePrincipalDeclaration).mockImplementation(async request => {
    current.state.declaration = { ...current.state.declaration!, ...request, revision: request.expectedRevision + 1 };
    return current.state.declaration;
  });
  vi.mocked(api.revokePreparerDraftCheck).mockImplementation(async request => {
    const value = { ...request, revision: 2, actor: "preparer", recordedAt: "2026-11-12T12:03:00Z" };
    current.state.revocations.push(value); return value;
  });
});
async function ready() {
  await waitFor(() => expect(screen.getByLabelText("Draft-check reason")).toBeEnabled());
}
function fill() {
  fireEvent.change(screen.getByLabelText("Draft-check reason"), { target: { value: "Inspected exact retained calculation" } });
  fireEvent.click(screen.getByRole("checkbox", { name: target.confirmation }));
}

describe("retained calculation preparer check", () => {
  it("records exact server operands and displays the separate canonical disclosure", async () => {
    const { container } = render(<PreparerDraftCheckPanel securityId={security} runId={run} />);
    await ready(); fill();
    fireEvent.click(screen.getByRole("button", { name: "Attest my non-postable draft check" }));
    expect(await screen.findByText(common.PREPARER_ATTESTATION_DISCLOSURE)).toBeInTheDocument();
    expect(api.recordPreparerDraftCheck).toHaveBeenCalledWith(expect.objectContaining({ actionId: api.PREPARER_DRAFT_CHECK,
      subjectId: target.subjectId, subjectRevision: target.revision, subjectHash: target.hash,
      expectedDeclarationRevision: 4, confirmation: target.confirmation }));
    const request = vi.mocked(api.recordPreparerDraftCheck).mock.calls[0][0];
    expect(request).not.toHaveProperty("actor"); expect(request).not.toHaveProperty("scope");
    expect(screen.getByRole("button", { name: "Download original proof" })).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });
  it("requires explicit v2 declaration reaffirmation", async () => {
    current.state.declaration!.policyVersion = "single-principal-evidence-v1";
    render(<PreparerDraftCheckPanel securityId={security} runId={run} />);
    await screen.findByText(/must explicitly enable or reaffirm/);
    expect(screen.getByRole("button", { name: "Attest my non-postable draft check" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Declaration reason"), { target: { value: "Confirm this non-posting policy" } });
    fireEvent.click(screen.getByRole("button", { name: "Enable or reaffirm draft declaration" }));
    await ready();
    expect(common.changePrincipalDeclaration).toHaveBeenCalledWith(expect.objectContaining({ expectedRevision: 4,
      policyVersion: api.PREPARER_DRAFT_POLICY, principalId: "preparer", enabled: true }));
  });
  it.each(["disabled", "missing-grant", "different-preparer"])("keeps %s checks unavailable", async failure => {
    if (failure === "disabled") { current.state.policyEnabled = false; current.state.allowedActions = []; }
    if (failure === "missing-grant") current.canAttestDrafts = false;
    if (failure === "different-preparer") vi.mocked(api.getPreparerDraftSnapshot).mockResolvedValue({ ...target, preparedBy: "someone-else" });
    render(<PreparerDraftCheckPanel securityId={security} runId={run} />);
    await screen.findByText(/Exact revision/);
    expect(screen.getByLabelText("Draft-check reason")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Attest my non-postable draft check" })).toBeDisabled();
    expect(api.recordPreparerDraftCheck).not.toHaveBeenCalled();
  });
  it("shows unbound legacy refusal without inventing a preview", async () => {
    vi.mocked(api.getPreparerDraftSnapshot).mockRejectedValue(new ApiError({ path: "/draft", status: 404 }));
    render(<PreparerDraftCheckPanel securityId={security} runId={run} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Legacy unbound runs remain ineligible");
    expect(screen.getByRole("button", { name: "Attest my non-postable draft check" })).toBeDisabled();
  });
  it("retries an uncertain outcome with the same command and locked operands", async () => {
    vi.mocked(api.recordPreparerDraftCheck).mockRejectedValueOnce(new Error("Connection lost"));
    render(<PreparerDraftCheckPanel securityId={security} runId={run} />);
    await ready(); fill();
    fireEvent.click(screen.getByRole("button", { name: "Attest my non-postable draft check" }));
    const retry = await screen.findByRole("button", { name: "Retry same draft command" });
    expect(screen.getByLabelText("Draft-check reason")).toBeDisabled();
    fireEvent.click(retry);
    await waitFor(() => expect(api.recordPreparerDraftCheck).toHaveBeenCalledTimes(2));
    expect(vi.mocked(api.recordPreparerDraftCheck).mock.calls[1][0]).toEqual(vi.mocked(api.recordPreparerDraftCheck).mock.calls[0][0]);
  });
  it("requires refresh and fresh confirmation after a stale refusal", async () => {
    vi.mocked(api.recordPreparerDraftCheck).mockRejectedValueOnce(new ApiError({ path: "/draft", status: 409, detail: "The declaration changed." }));
    render(<PreparerDraftCheckPanel securityId={security} runId={run} />);
    await ready(); fill(); fireEvent.click(screen.getByRole("button", { name: "Attest my non-postable draft check" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Refresh the declaration and retained run");
    expect(screen.queryByRole("button", { name: "Retry same draft command" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Refresh draft check" }));
    await ready();
    expect(screen.getByLabelText("Draft-check reason")).toHaveValue("");
    expect(screen.getByRole("checkbox", { name: target.confirmation })).not.toBeChecked();
  });
  it("retains proof and permits own corrective revocation after withdrawal and provider disablement", async () => {
    current.state.receipts = [receipt()]; current.state.policyEnabled = false; current.state.allowedActions = [];
    current.state.policyVersion = "single-principal-evidence-v1";
    current.state.declaration!.enabled = false;
    current.canAttestDrafts = false;
    vi.mocked(api.getPreparerDraftSnapshot).mockRejectedValue(new ApiError({ path: "/draft", status: 404 }));
    render(<PreparerDraftCheckPanel securityId={security} runId={run} />);
    await screen.findByText(common.PREPARER_ATTESTATION_DISCLOSURE);
    fireEvent.change(screen.getByLabelText("Draft revocation reason"), { target: { value: "Correcting my earlier assertion" } });
    fireEvent.click(screen.getByRole("button", { name: "Revoke my draft check" }));
    expect(await screen.findByText(/Revoked by preparer/)).toBeInTheDocument();
    expect(api.revokePreparerDraftCheck).toHaveBeenCalledWith(expect.objectContaining({ receiptId: "receipt-1", expectedReceiptRevision: 1 }));
    expect(screen.getByRole("button", { name: "Download original proof" })).toBeInTheDocument();
  });
  it("requires the explicit draft revocation capability even when recording is allowed", async () => {
    current.state.receipts = [receipt()]; current.canRevokeDrafts = false;
    render(<PreparerDraftCheckPanel securityId={security} runId={run} />);
    await screen.findByText(common.PREPARER_ATTESTATION_DISCLOSURE);
    expect(screen.queryByRole("button", { name: "Revoke my draft check" })).not.toBeInTheDocument();
  });
  it("does not show unsupported classifications as assurance or expose revoke controls", async () => {
    current.state.receipts = [{ ...receipt(), schemaVersion: 999, disclosure: "Independently approved" }];
    render(<PreparerDraftCheckPanel securityId={security} runId={run} />);
    expect(await screen.findByText(/Review classification unavailable/)).toBeInTheDocument();
    expect(screen.queryByText("Independently approved")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Revoke my draft check" })).not.toBeInTheDocument();
  });
  it("discards delayed operands when the retained run changes", async () => {
    let finish!: (value: api.PreparerDraftSnapshot) => void;
    vi.mocked(api.getPreparerDraftSnapshot).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const { rerender } = render(<PreparerDraftCheckPanel securityId={security} runId={run} />);
    const nextRun = "33333333-3333-4333-8333-333333333333";
    const next = { ...target, subjectId: `Security/${security}/${nextRun}`, revision: "f".repeat(64) };
    vi.mocked(api.getPreparerDraftSnapshot).mockResolvedValue(next);
    rerender(<PreparerDraftCheckPanel securityId={security} runId={nextRun} />);
    await ready();
    await act(async () => finish(target));
    expect(screen.getByText(/Exact revision/)).toHaveTextContent(next.revision);
    fill(); fireEvent.click(screen.getByRole("button", { name: "Attest my non-postable draft check" }));
    await waitFor(() => expect(api.recordPreparerDraftCheck).toHaveBeenCalledWith(expect.objectContaining({ subjectId: next.subjectId, subjectRevision: next.revision })));
  });
});
