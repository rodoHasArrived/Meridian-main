import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PreparerAttestationEvidence, PreparerAttestationProofDownload } from "./preparer-attestation-evidence";
import { getPreparerAttestationProof, PREPARER_ATTESTATION_DISCLOSURE } from "@/lib/single-principal-attestation-api";
import type { PreparerAttestationEvidenceProjection } from "@/types/preparer-attestation-evidence";

vi.mock("@/lib/single-principal-attestation-api", async importOriginal => ({
  ...await importOriginal<typeof import("@/lib/single-principal-attestation-api")>(),
  getPreparerAttestationProof: vi.fn()
}));

const scope = { tenantId: "tenant-one", companyId: "company-one", ledgerBookId: null };
function projection(): PreparerAttestationEvidenceProjection {
  const declaration = { declarationId: "decl-one", revision: 2, scope, enabled: true, principalId: "preparer-one",
    policyVersion: "single-principal-evidence-v1", policyHash: "policy-hash", changedBy: "preparer-one",
    recordedAt: "2026-11-12T12:00:00Z", reason: "One principal" };
  const receipt = { receiptId: "receipt-one", commandId: "command-one", assertionId: "command-one", schemaVersion: 1,
    decisionKind: "PreparerAttestation", disclosure: PREPARER_ATTESTATION_DISCLOSURE, scope,
    actionId: "ATTEST-EVIDENCE-ACK", subjectKind: "evidence-document", subjectId: "vault-one/document-one",
    subjectRevision: "revision-one", subjectHash: "subject-hash", sourceArtifactId: "artifact-one", sourceHash: "source-hash",
    sourceActor: "external-importer", sourceProvenance: "Synthetic", preparedBy: "preparer-one", attestedBy: "preparer-one",
    origin: "HumanOperator", declarationId: "decl-one", declarationRevision: 2, policyVersion: "single-principal-evidence-v1",
    policyHash: "policy-hash", recordedAt: "2026-11-12T12:01:00Z", reason: "Inspected retained original", confirmation: "Confirmed", checks: ["Source hash matches"] };
  return { scope, schemaVersion: 1, availability: "Available", assessedAt: "2026-11-12T12:02:00Z",
    disclosure: PREPARER_ATTESTATION_DISCLOSURE, boundary: "Evidence acknowledgments only",
    recordedReceiptCount: 1, unrevokedReceiptCount: 1, revokedReceiptCount: 0, syntheticReceiptCount: 1,
    unclassifiedReceiptCount: 0, acknowledgments: [{ receipt, originalDeclaration: declaration, revocation: null,
      applicability: "ExactSource" }], warnings: [] };
}

beforeEach(() => vi.resetAllMocks());
afterEach(() => vi.unstubAllGlobals());

describe("separate preparer evidence classification", () => {
  it("leaves omitted legacy projection absent", () => {
    const { container } = render(<PreparerAttestationEvidence />);
    expect(container).toBeEmptyDOMElement();
  });

  it("shows receipt counts, canonical disclosure and source provenance without amount assurance", async () => {
    const { container } = render(<PreparerAttestationEvidence projection={projection()} expectedScope={scope} />);
    expect(screen.getByText(PREPARER_ATTESTATION_DISCLOSURE)).toBeInTheDocument();
    expect(screen.getByText(/1 recorded evidence-task receipt/)).toBeInTheDocument();
    expect(screen.getByText(/They do not approve an amount/)).toBeInTheDocument();
    expect(screen.getByText(/Unrevoked does not mean currently supported/)).toBeInTheDocument();
    expect(screen.getByText(/Source provenance: Synthetic/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Download original proof" })).toBeEnabled();
    expect((await axe(container)).violations).toEqual([]);
  });

  it("preserves revoked disclosure and separately dated revocation", () => {
    const value = projection();
    value.unrevokedReceiptCount = 0; value.revokedReceiptCount = 1;
    value.acknowledgments[0]!.applicability = "Revoked";
    value.acknowledgments[0]!.revocation = { commandId: "revoke-one", receiptId: "receipt-one", revision: 2,
      actor: "preparer-one", recordedAt: "2026-11-12T13:00:00Z", reason: "Correcting an erroneous assertion" };
    render(<PreparerAttestationEvidence projection={value} />);
    expect(screen.getByText(PREPARER_ATTESTATION_DISCLOSURE)).toBeInTheDocument();
    expect(screen.getByText("Revoked; original assertion retained.")).toBeInTheDocument();
    expect(screen.getByText(/Correcting an erroneous assertion/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Download original proof" })).toBeEnabled();
  });

  it.each(["StaleSource", "Unavailable", "FutureApplicability"])("does not present %s as current support", applicability => {
    const value = projection(); value.acknowledgments[0]!.applicability = applicability;
    render(<PreparerAttestationEvidence projection={value} />);
    expect(screen.getByText(applicability === "StaleSource" ? /Source changed/ : /Current source applicability is unavailable/)).toBeInTheDocument();
    expect(screen.queryByText(/exact source snapshot matches/)).not.toBeInTheDocument();
  });

  it.each(["unknown-version", "unknown-disclosure", "foreign-scope", "contradictory-counts"])("shows %s as unavailable rather than zero or verified", condition => {
    const value = projection();
    if (condition === "unknown-version") value.schemaVersion = 999;
    if (condition === "unknown-disclosure") value.disclosure = "Independently approved";
    if (condition === "foreign-scope") value.scope = { ...scope, tenantId: "foreign" };
    if (condition === "contradictory-counts") value.recordedReceiptCount = 2;
    render(<PreparerAttestationEvidence projection={value} expectedScope={scope} />);
    expect(screen.getByText(/Preparer evidence classification unavailable/)).toBeInTheDocument();
    expect(screen.queryByText(PREPARER_ATTESTATION_DISCLOSURE)).not.toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(screen.queryByText("Independently approved")).not.toBeInTheDocument();
  });

  it("keeps unavailable counts unknown", () => {
    const value = projection(); value.availability = "Unavailable";
    value.recordedReceiptCount = value.unrevokedReceiptCount = value.revokedReceiptCount = null;
    value.syntheticReceiptCount = value.unclassifiedReceiptCount = null; value.acknowledgments = [];
    value.warnings = ["Current assertion read permission is unavailable."];
    render(<PreparerAttestationEvidence projection={value} />);
    expect(screen.getByText(/classification unavailable/)).toBeInTheDocument();
    expect(screen.getByText(value.warnings[0]!)).toBeInTheDocument();
    expect(screen.queryByText(/0 recorded/)).not.toBeInTheDocument();
  });

  it.each(["schema", "scope", "declaration", "decision", "policy", "origin", "preparer"])("does not upgrade an unsupported %s entry", condition => {
    const value = projection(); const entry = value.acknowledgments[0]!;
    if (condition === "schema") entry.receipt.schemaVersion = 99;
    if (condition === "scope") entry.receipt.scope = { ...scope, companyId: "foreign" };
    if (condition === "declaration") entry.originalDeclaration.revision = 99;
    if (condition === "decision") entry.receipt.decisionKind = "IndependentReview";
    if (condition === "policy") entry.originalDeclaration.policyHash = "different-policy";
    if (condition === "origin") entry.receipt.origin = "AutomationAssistant";
    if (condition === "preparer") entry.receipt.preparedBy = "different-preparer";
    render(<PreparerAttestationEvidence projection={value} />);
    expect(screen.getByText(/Review classification unavailable — unsupported receipt/)).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("keeps an inline Number Passport valid phrasing content", () => {
    const { container } = render(<span><PreparerAttestationEvidence projection={projection()} inline /></span>);
    expect(screen.getByRole("group", { name: "Preparer evidence classification" })).toBeInTheDocument();
    expect(container.querySelector("section, article, div, p")).toBeNull();
  });
});

describe("original assertion proof download", () => {
  it("abandons an old receipt download immediately when selection changes", async () => {
    let finish!: (blob: Blob) => void;
    vi.mocked(getPreparerAttestationProof).mockReturnValue(new Promise(resolve => { finish = resolve; }));
    const create = vi.fn(); vi.stubGlobal("URL", { createObjectURL: create, revokeObjectURL: vi.fn() });
    const { rerender } = render(<PreparerAttestationProofDownload receiptId="receipt-one" />);
    fireEvent.click(screen.getByRole("button", { name: "Download original proof" }));
    const signal = vi.mocked(getPreparerAttestationProof).mock.calls[0]![1]!.signal!;
    rerender(<PreparerAttestationProofDownload receiptId="receipt-two" />);
    expect(signal.aborted).toBe(true);
    expect(screen.getByRole("button", { name: "Download original proof" })).toBeEnabled();
    await act(async () => finish(new Blob(["old receipt"])));
    expect(create).not.toHaveBeenCalled();
  });
  it("keeps authorization errors visible and creates no alternate artifact", async () => {
    vi.mocked(getPreparerAttestationProof).mockRejectedValue(new Error("Current receipt read permission denied"));
    render(<PreparerAttestationProofDownload receiptId="receipt-one" />);
    fireEvent.click(screen.getByRole("button", { name: "Download original proof" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Current receipt read permission denied");
    expect(getPreparerAttestationProof).toHaveBeenCalledWith("receipt-one", { signal: expect.any(AbortSignal) });
  });

  it("downloads the original Blob only on explicit request", async () => {
    const blob = new Blob(["{\"immutable\":true}"], { type: "application/json" });
    vi.mocked(getPreparerAttestationProof).mockResolvedValue(blob);
    const create = vi.fn().mockReturnValue("blob:original-proof"); const revoke = vi.fn();
    vi.stubGlobal("URL", { createObjectURL: create, revokeObjectURL: revoke });
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined);
    render(<PreparerAttestationProofDownload receiptId="receipt-one" />);
    expect(getPreparerAttestationProof).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Download original proof" }));
    await waitFor(() => expect(click).toHaveBeenCalledOnce());
    expect(create).toHaveBeenCalledWith(blob);
    expect(revoke).toHaveBeenCalledWith("blob:original-proof");
    click.mockRestore();
  });
});
