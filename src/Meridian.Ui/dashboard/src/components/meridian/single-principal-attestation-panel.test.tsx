import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SinglePrincipalAttestationPanel } from "./single-principal-attestation-panel";
import * as api from "@/lib/single-principal-attestation-api";
import { ApiError } from "@/lib/api-errors";
import type { AttestationSubject, AttestationWorkspace, PreparerReceipt } from "@/lib/single-principal-attestation-api";

vi.mock("@/lib/single-principal-attestation-api", async (importOriginal) => ({
  ...await importOriginal<typeof import("@/lib/single-principal-attestation-api")>(),
  getAttestationWorkspace: vi.fn(), getAttestationSubject: vi.fn(), changePrincipalDeclaration: vi.fn(),
  recordPreparerAttestation: vi.fn(), revokePreparerAttestation: vi.fn(), getPreparerAttestationProof: vi.fn()
}));

const scope = { tenantId: "tenant-1", companyId: "company-1", ledgerBookId: null };
const subject: AttestationSubject = {
  subjectKind: "evidence-document", subjectId: "vault-1/document-1", revision: "retained-revision-7",
  hash: "retained-hash", sourceArtifactId: "artifact-1", sourceHash: "source-hash", sourceActor: "source-importer",
  sourceProvenance: "Synthetic", eligible: true, checks: ["Source bytes verified."]
};
function workspace(): AttestationWorkspace {
  return {
    actor: "preparer-1", canAttest: true, canAdministerDeclaration: true, canRevoke: true,
    state: { scope, policyEnabled: true, policyVersion: "policy-v1", policyHash: "policy-hash",
      disclosure: "Attested by preparer — not independently reviewed", confirmation: "I inspected this retained evidence. This is not approval.",
      allowedActions: [api.EVIDENCE_ACKNOWLEDGMENT],
      declaration: { declarationId: "declaration-1", revision: 4, scope, enabled: true, principalId: "preparer-1",
        policyVersion: "policy-v1", policyHash: "policy-hash", changedBy: "preparer-1", recordedAt: "2026-11-12T12:00:00Z", reason: "Solo operation" },
      declarationHistory: [], receipts: [], revocations: [], auditTrail: [] }
  };
}
function receipt(): PreparerReceipt {
  return { receiptId: "receipt-1", commandId: "command-1", assertionId: "command-1", schemaVersion: 1,
    decisionKind: "PreparerAttestation", disclosure: "Attested by preparer — not independently reviewed", scope,
    actionId: api.EVIDENCE_ACKNOWLEDGMENT, subjectKind: subject.subjectKind, subjectId: subject.subjectId,
    subjectRevision: "old-revision", subjectHash: "old-hash", sourceArtifactId: "artifact-1", sourceHash: "source-hash",
    sourceActor: "source-importer", sourceProvenance: "Synthetic", preparedBy: "preparer-1", attestedBy: "preparer-1",
    origin: "HumanOperator", declarationId: "declaration-1", declarationRevision: 4, policyVersion: "policy-v1",
    policyHash: "policy-hash", recordedAt: "2026-11-12T12:00:00Z", reason: "Inspected retained source", confirmation: "Confirmed", checks: [] };
}
async function loaded() { await screen.findByText("Policy enabled"); }
function confirm() {
  fireEvent.change(screen.getByLabelText("Attestation reason"), { target: { value: "Inspected original retained bytes" } });
  fireEvent.click(screen.getByRole("checkbox"));
}

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.getAttestationWorkspace).mockResolvedValue(workspace());
  vi.mocked(api.getAttestationSubject).mockResolvedValue(subject);
  vi.mocked(api.recordPreparerAttestation).mockResolvedValue({});
  vi.mocked(api.changePrincipalDeclaration).mockResolvedValue({});
  vi.mocked(api.revokePreparerAttestation).mockResolvedValue({});
});

describe("single-principal evidence acknowledgment", () => {
  it("requires an explicit audited reconfirmation before upgrading the current declaration policy", async () => {
    const value = workspace(); value.state.policyVersion = "policy-v2";
    vi.mocked(api.getAttestationWorkspace).mockResolvedValue(value);
    render(<SinglePrincipalAttestationPanel vaultId="vault-1" documentId="document-1" />);
    await loaded();
    expect(api.changePrincipalDeclaration).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Reconfirm current policy" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Declaration reason"), { target: { value: "Reviewed the new non-posting policy" } });
    fireEvent.click(screen.getByRole("button", { name: "Reconfirm current policy" }));
    await waitFor(() => expect(api.changePrincipalDeclaration).toHaveBeenCalledOnce());
    expect(api.changePrincipalDeclaration).toHaveBeenCalledWith({ commandId: expect.any(String), expectedRevision: 4,
      enabled: true, principalId: "preparer-1", policyVersion: "policy-v2", reason: "Reviewed the new non-posting policy" });
  });
  it("keeps the default-off policy and missing grants explicit without offering approval", async () => {
    const value = workspace();
    value.state.policyEnabled = false;
    value.state.declaration = null;
    value.canAttest = false;
    value.canAdministerDeclaration = false;
    value.canRevoke = false;
    vi.mocked(api.getAttestationWorkspace).mockResolvedValue(value);
    render(<SinglePrincipalAttestationPanel vaultId="vault-1" documentId="document-1" />);
    await screen.findByText("Policy disabled (default)");
    expect(screen.getByText("Missing preparer-attestation grant.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Attest evidence inspection" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: /approve|accept/i })).not.toBeInTheDocument();
    expect(api.recordPreparerAttestation).not.toHaveBeenCalled();
  });

  it("requires reason and confirmation and copies only the exact server snapshot", async () => {
    render(<SinglePrincipalAttestationPanel vaultId="vault-1" documentId="document-1" />);
    await loaded();
    const button = screen.getByRole("button", { name: "Attest evidence inspection" });
    expect(button).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Attestation reason"), { target: { value: "Inspected original retained bytes" } });
    expect(button).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(button);
    await waitFor(() => expect(api.recordPreparerAttestation).toHaveBeenCalledOnce());
    expect(api.recordPreparerAttestation).toHaveBeenCalledWith({
      commandId: expect.any(String), expectedDeclarationRevision: 4, actionId: api.EVIDENCE_ACKNOWLEDGMENT,
      subjectKind: subject.subjectKind, subjectId: subject.subjectId, subjectRevision: subject.revision,
      subjectHash: subject.hash, reason: "Inspected original retained bytes", confirmation: workspace().state.confirmation
    });
    expect(screen.getByText(workspace().state.disclosure)).toBeInTheDocument();
  });

  it("refuses unsupported evidence and a declaration belonging to another principal", async () => {
    const value = workspace();
    value.state.declaration!.principalId = "someone-else";
    vi.mocked(api.getAttestationWorkspace).mockResolvedValue(value);
    vi.mocked(api.getAttestationSubject).mockResolvedValue({ ...subject, eligible: false });
    render(<SinglePrincipalAttestationPanel vaultId="vault-1" documentId="document-1" />);
    await loaded();
    expect(screen.getByText("The active declaration belongs to another principal.")).toBeInTheDocument();
    expect(screen.getByText(/document is not eligible/)).toBeInTheDocument();
    expect(screen.getByRole("checkbox")).toBeDisabled();
  });

  it("preserves the same command after an uncertain write and blocks another assertion", async () => {
    vi.mocked(api.recordPreparerAttestation).mockRejectedValueOnce(new Error("Response lost"));
    render(<SinglePrincipalAttestationPanel vaultId="vault-1" documentId="document-1" />);
    await loaded(); confirm();
    fireEvent.click(screen.getByRole("button", { name: "Attest evidence inspection" }));
    await screen.findByRole("button", { name: "Retry same command" });
    expect(screen.getByRole("button", { name: "Attest evidence inspection" })).toBeDisabled();
    const original = vi.mocked(api.recordPreparerAttestation).mock.calls[0][0];
    fireEvent.click(screen.getByRole("button", { name: "Retry same command" }));
    await waitFor(() => expect(api.recordPreparerAttestation).toHaveBeenCalledTimes(2));
    expect(vi.mocked(api.recordPreparerAttestation).mock.calls[1][0]).toEqual(original);
  });

  it("requires refresh and fresh confirmation after a definite version conflict", async () => {
    vi.mocked(api.recordPreparerAttestation).mockRejectedValueOnce(new ApiError({ path: "/attestations", status: 409, detail: "Source changed" }));
    render(<SinglePrincipalAttestationPanel vaultId="vault-1" documentId="document-1" />);
    await loaded(); confirm();
    fireEvent.click(screen.getByRole("button", { name: "Attest evidence inspection" }));
    await screen.findByText(/Refresh the current policy and evidence/);
    expect(screen.queryByRole("button", { name: "Retry same command" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Refresh attestation" }));
    await loaded();
    expect(screen.getByRole("checkbox")).not.toBeChecked();
  });

  it("retains labeled history when current source cannot resolve and permits only own revocation", async () => {
    const value = workspace();
    value.state.policyEnabled = false;
    value.state.receipts = [receipt(), { ...receipt(), receiptId: "foreign-receipt", attestedBy: "another-operator" }];
    vi.mocked(api.getAttestationWorkspace).mockResolvedValue(value);
    vi.mocked(api.getAttestationSubject).mockRejectedValue(new Error("Current source unavailable"));
    render(<SinglePrincipalAttestationPanel vaultId="vault-1" documentId="document-1" />);
    await screen.findByText("Retained preparer assertions (2)");
    expect(screen.getAllByRole("button", { name: "Revoke my assertion" })).toHaveLength(1);
    fireEvent.change(screen.getByLabelText("Revocation reason"), { target: { value: "Incorrect assertion" } });
    fireEvent.click(screen.getByRole("button", { name: "Revoke my assertion" }));
    await waitFor(() => expect(api.revokePreparerAttestation).toHaveBeenCalledWith({ commandId: expect.any(String), receiptId: "receipt-1", expectedReceiptRevision: 1, reason: "Incorrect assertion" }));
  });

  it("does not interpret an unknown decision or receipt schema as approval or offer its revocation", async () => {
    const value = workspace();
    value.state.receipts = [
      { ...receipt(), schemaVersion: 99, disclosure: "Independently approved" },
      { ...receipt(), receiptId: "other-type", decisionKind: "Unknown", disclosure: "Verified" }
    ];
    vi.mocked(api.getAttestationWorkspace).mockResolvedValue(value);
    render(<SinglePrincipalAttestationPanel vaultId="vault-1" documentId="document-1" />);
    await loaded();
    expect(screen.getAllByText("Review classification unavailable — unsupported receipt")).toHaveLength(2);
    expect(screen.queryByText("Independently approved")).not.toBeInTheDocument();
    expect(screen.queryByText("Verified")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Revoke my assertion" })).not.toBeInTheDocument();
  });

  it("shows current support separately from historical receipt and preserves source provenance", async () => {
    const value = workspace();
    value.state.declaration!.enabled = false;
    value.state.receipts = [{ ...receipt(), attestedBy: " PREPARER-1 " }];
    vi.mocked(api.getAttestationWorkspace).mockResolvedValue(value);
    render(<SinglePrincipalAttestationPanel vaultId="vault-1" documentId="document-1" />);
    await loaded();
    expect(screen.getByText("Historical assertion; current support is not assessed. No independent approval.")).toBeInTheDocument();
    expect(screen.getByText("Source provenance: Synthetic. Source actor: source-importer.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Revoke my assertion" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Attest evidence inspection" })).toBeDisabled();
  });

  it("binds declarations to the loaded actor and current revision", async () => {
    const value = workspace(); value.state.declaration = null;
    vi.mocked(api.getAttestationWorkspace).mockResolvedValue(value);
    render(<SinglePrincipalAttestationPanel vaultId="vault-1" documentId="document-1" />);
    await loaded();
    fireEvent.change(screen.getByLabelText("Declaration reason"), { target: { value: "Single principal" } });
    fireEvent.submit(screen.getByLabelText("Declaration reason").closest("form")!);
    await waitFor(() => expect(api.changePrincipalDeclaration).toHaveBeenCalledWith({ commandId: expect.any(String), expectedRevision: 0, enabled: true, principalId: "preparer-1", policyVersion: "policy-v1", reason: "Single principal" }));
  });

  it("ignores a delayed snapshot after document selection changes", async () => {
    let resolveFirst!: (value: AttestationSubject) => void;
    vi.mocked(api.getAttestationSubject).mockImplementationOnce(() => new Promise((resolve) => { resolveFirst = resolve; }));
    const view = render(<SinglePrincipalAttestationPanel vaultId="vault-1" documentId="document-1" />);
    vi.mocked(api.getAttestationSubject).mockResolvedValue({ ...subject, subjectId: "vault-1/document-2", revision: "second-revision" });
    view.rerender(<SinglePrincipalAttestationPanel vaultId="vault-1" documentId="document-2" />);
    await loaded();
    await act(async () => resolveFirst(subject));
    expect(screen.getByText("second-revision")).toBeInTheDocument();
    expect(screen.queryByText("retained-revision-7")).not.toBeInTheDocument();
  });

  it("has accessible labels and semantic status for the ready panel", async () => {
    const { container } = render(<SinglePrincipalAttestationPanel vaultId="vault-1" documentId="document-1" />);
    await loaded();
    expect((await axe(container)).violations).toEqual([]);
  });
});
