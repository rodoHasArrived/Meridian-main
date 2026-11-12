import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CashDividendAttestationPanel } from "./cash-dividend-attestation-panel";
import * as api from "@/lib/cash-dividend-attestation-api";
import { ApiError } from "@/lib/api-errors";
import { cashDividendCaseId, cashDividendScope, cashDividendWorkspace, retainedCashDividendAttestation } from "@/test/cash-dividend-attestation-fixtures";

vi.mock("@/lib/cash-dividend-attestation-api", async importOriginal => ({
  ...await importOriginal<typeof import("@/lib/cash-dividend-attestation-api")>(),
  getCashDividendAttestationWorkspace: vi.fn(), changeCashDividendBookDeclaration: vi.fn(),
  attestCashDividend: vi.fn(), postAttestedCashDividend: vi.fn()
}));

beforeEach(() => {
  vi.resetAllMocks(); sessionStorage.clear();
  vi.mocked(api.getCashDividendAttestationWorkspace).mockResolvedValue(cashDividendWorkspace());
  const workspace = cashDividendWorkspace(); const attestation = retainedCashDividendAttestation(workspace);
  vi.mocked(api.attestCashDividend).mockResolvedValue({ case: { ...workspace.case, state: "PreparerAttested", version: 6 },
    attestation, transition: { resultingVersion: 6, toState: "PreparerAttested", actor: workspace.actor, reason: attestation.reason }, replayed: false });
  vi.mocked(api.changeCashDividendBookDeclaration).mockResolvedValue({ declaration: workspace.declaration!, replayed: false });
});

async function loaded() { await screen.findByText("Cash-dividend policy enabled"); }
function attest() {
  fireEvent.change(screen.getByLabelText("Cash-dividend attestation reason"), { target: { value: "Reviewed exact retained cash-dividend operands" } });
  fireEvent.click(screen.getByRole("checkbox", { name: api.CASH_DIVIDEND_ATTESTATION_CONFIRMATION }));
  fireEvent.click(screen.getByRole("button", { name: "Attest this cash-dividend projection" }));
}

describe("bounded native cash-dividend preparer attestation", () => {
  it("requires an explicit reason and confirmation and sends no amounts, actor or authority", async () => {
    render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await loaded();
    const button = screen.getByRole("button", { name: "Attest this cash-dividend projection" });
    expect(button).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Cash-dividend attestation reason"), { target: { value: "Reviewed exact retained cash-dividend operands" } });
    expect(button).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox", { name: api.CASH_DIVIDEND_ATTESTATION_CONFIRMATION }));
    fireEvent.click(button);
    await waitFor(() => expect(api.attestCashDividend).toHaveBeenCalledOnce());
    const workspace = cashDividendWorkspace();
    expect(api.attestCashDividend).toHaveBeenCalledWith(cashDividendCaseId, { expectedVersion: 5,
      idempotencyKey: expect.any(String), projectionId: workspace.preview!.projection.projectionId,
      expectedDeclarationRevision: 2, expectedEligibilityHash: workspace.preview!.eligibility.entitlementSnapshotHash,
      reason: "Reviewed exact retained cash-dividend operands", evidenceReference: workspace.evidenceReference,
      evidenceHash: workspace.evidenceHash, confirmation: workspace.confirmation });
    expect(api.postAttestedCashDividend).not.toHaveBeenCalled();
  });

  it.each(["policy-off", "no-c-grant", "no-preview", "foreign-preparer", "foreign-principal", "withdrawn", "missing-evidence", "unknown-policy"])("does not enable an attestation for %s", async condition => {
    const value = cashDividendWorkspace();
    if (condition === "policy-off") value.policyEnabled = false;
    if (condition === "no-c-grant") value.canAttestAccounting = false;
    if (condition === "no-preview") value.preview = null;
    if (condition === "foreign-preparer") value.preview!.projection.preparedBy = "different-preparer";
    if (condition === "foreign-principal") value.declaration!.principalId = "different-principal";
    if (condition === "withdrawn") value.declaration!.enabled = false;
    if (condition === "missing-evidence") value.evidenceHash = null;
    if (condition === "unknown-policy") value.policyVersion = "future-policy";
    vi.mocked(api.getCashDividendAttestationWorkspace).mockResolvedValue(value);
    render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await screen.findByRole("button", { name: "Refresh cash-dividend controls" });
    await waitFor(() => expect(screen.queryByText(/Loading the native case/)).not.toBeInTheDocument());
    const button = screen.queryByRole("button", { name: "Attest this cash-dividend projection" });
    if (button) expect(button).toBeDisabled();
    expect(api.attestCashDividend).not.toHaveBeenCalled();
    expect(api.postAttestedCashDividend).not.toHaveBeenCalled();
  });

  it("keeps declaration administration separate from economic attestation grants", async () => {
    const value = cashDividendWorkspace(); value.declaration = null; value.canAttestAccounting = false;
    vi.mocked(api.getCashDividendAttestationWorkspace).mockResolvedValue(value);
    render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await loaded();
    fireEvent.change(screen.getByLabelText("Cash-dividend declaration reason"), { target: { value: "Declared one reviewed USD book" } });
    const button = screen.getByRole("button", { name: "Enable cash-dividend declaration for this book" });
    expect(button).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox", { name: /I am the single principal for this USD book/ }));
    expect(button).toBeDisabled();
    expect(screen.getByText("dividend-receivable / Dividend receivable")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("checkbox", { name: /I explicitly confirm the server-provided dividend receivable account/ }));
    fireEvent.click(button);
    await waitFor(() => expect(api.changeCashDividendBookDeclaration).toHaveBeenCalledOnce());
    expect(api.changeCashDividendBookDeclaration).toHaveBeenCalledWith(cashDividendCaseId, {
      commandId: expect.any(String), expectedRevision: 0, ledgerBookId: cashDividendScope.ledgerBookId,
      enabled: true, policyVersion: value.policyVersion, policyHash: value.policyHash, reason: "Declared one reviewed USD book",
      accountRoleBinding: value.preview!.eligibility.accountRoleBinding });
    expect(api.attestCashDividend).not.toHaveBeenCalled();
  });

  it.each(["no-bind-capability", "missing-binding", "foreign-book-binding", "unsupported-binding"])("requires authoritative accounting roles for declaration: %s", async condition => {
    const value = cashDividendWorkspace(); value.declaration = null;
    if (condition === "no-bind-capability") value.canBindAccountingRoles = false;
    if (condition === "missing-binding") value.preview!.eligibility.accountRoleBinding = null;
    if (condition === "foreign-book-binding") value.preview!.eligibility.accountRoleBinding!.ledgerBookId = "foreign-book";
    if (condition === "unsupported-binding") value.preview!.eligibility.accountRoleBinding!.schemaVersion = 99;
    vi.mocked(api.getCashDividendAttestationWorkspace).mockResolvedValue(value);
    render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await loaded();
    expect(screen.getByRole("checkbox", { name: /I explicitly confirm the server-provided dividend receivable account/ })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Enable cash-dividend declaration for this book" })).toBeDisabled();
    expect(api.changeCashDividendBookDeclaration).not.toHaveBeenCalled();
  });

  it("recovers the exact confirmed accounting binding without taking new roles or authority from storage", async () => {
    const value = cashDividendWorkspace(); value.declaration = null;
    vi.mocked(api.getCashDividendAttestationWorkspace).mockResolvedValue(value);
    vi.mocked(api.changeCashDividendBookDeclaration).mockRejectedValueOnce(new Error("Declaration response lost"));
    const { unmount } = render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await loaded();
    fireEvent.change(screen.getByLabelText("Cash-dividend declaration reason"), { target: { value: "Reviewed exact roles and book limits" } });
    fireEvent.click(screen.getByRole("checkbox", { name: /I am the single principal for this USD book/ }));
    fireEvent.click(screen.getByRole("checkbox", { name: /I explicitly confirm the server-provided dividend receivable account/ }));
    fireEvent.click(screen.getByRole("button", { name: "Enable cash-dividend declaration for this book" }));
    await screen.findByRole("button", { name: "Retry same cash-dividend command" });
    const command = vi.mocked(api.changeCashDividendBookDeclaration).mock.calls[0]![1];
    const key = sessionStorage.key(0)!; const saved = JSON.parse(sessionStorage.getItem(key)!);
    saved.command.request.accountRoleBinding.authority = { canBindAccountingRoles: true };
    sessionStorage.setItem(key, JSON.stringify(saved));
    unmount();
    const changed = cashDividendWorkspace(); changed.declaration = null; changed.policyEnabled = false; changed.canBindAccountingRoles = false;
    changed.preview!.eligibility.accountRoleBinding!.dividendReceivableAccount = "new-server-account";
    vi.mocked(api.getCashDividendAttestationWorkspace).mockResolvedValue(changed);
    render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await screen.findByRole("button", { name: "Retry same cash-dividend command" });
    expect(api.changeCashDividendBookDeclaration).toHaveBeenCalledOnce();
    fireEvent.click(screen.getByRole("button", { name: "Retry same cash-dividend command" }));
    await waitFor(() => expect(api.changeCashDividendBookDeclaration).toHaveBeenCalledTimes(2));
    expect(vi.mocked(api.changeCashDividendBookDeclaration).mock.calls[1]![1]).toEqual(command);
  });

  it("requires a separate explicit posting command using the new attestation identity", async () => {
    const value = cashDividendWorkspace(); const attestation = retainedCashDividendAttestation(value);
    const attested = { ...value, case: { ...value.case, state: "PreparerAttested", version: 6 },
      canAttestAccounting: false, canPostAttestedAccounting: true, attestation };
    vi.mocked(api.getCashDividendAttestationWorkspace).mockResolvedValueOnce(value).mockResolvedValue(attested);
    vi.mocked(api.postAttestedCashDividend).mockResolvedValue({ case: { ...attested.case, state: "Posted", version: 7 },
      posting: { postingId: "posting-one", caseId: cashDividendCaseId, projectionId: attestation.projectionId,
        attestationId: attestation.attestationId, journalEntryId: "journal-one", ledgerBookId: cashDividendScope.ledgerBookId,
        periodId: cashDividendScope.periodId, accountingBasis: "US-GAAP", currency: "USD", totalDebits: 100, totalCredits: 100,
        postingStatus: "Posted", postedBy: value.actor, postedAtUtc: "2026-11-12T12:10:00Z" },
      transition: { resultingVersion: 7, toState: "Posted", actor: value.actor, reason: "Reviewed posting" }, replayed: false });
    render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await loaded(); attest();
    await screen.findByRole("region", { name: "Retained cash-dividend attestation" });
    expect(screen.getByText("Nov 12, 2026, 12:05 UTC")).toHaveAttribute("dateTime", attestation.attestedAtUtc);
    expect(api.postAttestedCashDividend).not.toHaveBeenCalled();
    const button = screen.getByRole("button", { name: "Post attested cash-dividend journal" });
    fireEvent.change(screen.getByLabelText("Attested posting reason"), { target: { value: "Post reviewed attested cash dividend" } });
    expect(button).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox", { name: /Post this attested cash-dividend journal under/ }));
    fireEvent.click(button);
    await waitFor(() => expect(api.postAttestedCashDividend).toHaveBeenCalledOnce());
    expect(api.postAttestedCashDividend).toHaveBeenCalledWith(cashDividendCaseId, {
      idempotencyKey: expect.any(String), expectedVersion: 6, projectionId: attestation.projectionId,
      attestationId: attestation.attestationId, reason: "Post reviewed attested cash dividend" });
    expect(vi.mocked(api.postAttestedCashDividend).mock.calls[0]![1]).not.toHaveProperty("approvalId");
  });

  it("recovers one exact uncertain command across remount without automatic resubmission", async () => {
    vi.mocked(api.attestCashDividend).mockRejectedValueOnce(new Error("Response lost after dispatch"));
    const { unmount } = render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await loaded(); attest();
    await screen.findByRole("button", { name: "Retry same cash-dividend command" });
    const command = vi.mocked(api.attestCashDividend).mock.calls[0]![1];
    unmount();
    const disabled = cashDividendWorkspace(); disabled.policyEnabled = false;
    disabled.canAttestAccounting = false; disabled.canPostAttestedAccounting = false; disabled.declaration!.enabled = false;
    vi.mocked(api.getCashDividendAttestationWorkspace).mockResolvedValue(disabled);
    render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await screen.findByRole("button", { name: "Retry same cash-dividend command" });
    expect(api.attestCashDividend).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: "Attest this cash-dividend projection" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Retry same cash-dividend command" }));
    await waitFor(() => expect(api.attestCashDividend).toHaveBeenCalledTimes(2));
    expect(vi.mocked(api.attestCashDividend).mock.calls[1]![1]).toEqual(command);
  });

  it("never restores another tenant's pending financial command even if identifiers are reused", async () => {
    vi.mocked(api.attestCashDividend).mockRejectedValueOnce(new Error("Response lost"));
    const { unmount } = render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await loaded(); attest();
    await screen.findByRole("button", { name: "Retry same cash-dividend command" });
    unmount();
    const foreign = cashDividendWorkspace();
    const foreignScope = { ...cashDividendScope, tenantId: "foreign-tenant" };
    foreign.scope = foreignScope; foreign.case = { ...foreign.case, scope: foreignScope };
    foreign.preview!.case = foreign.case;
    foreign.declaration!.tenantId = foreignScope.tenantId;
    vi.mocked(api.getCashDividendAttestationWorkspace).mockResolvedValue(foreign);
    render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={foreignScope} />);
    await loaded();
    expect(screen.queryByRole("button", { name: "Retry same cash-dividend command" })).not.toBeInTheDocument();
    expect(api.attestCashDividend).toHaveBeenCalledOnce();
  });

  it("never forwards authority or actor fields injected into local recovery storage", async () => {
    vi.mocked(api.attestCashDividend).mockRejectedValueOnce(new Error("Response lost"));
    const { unmount } = render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await loaded(); attest();
    await screen.findByRole("button", { name: "Retry same cash-dividend command" });
    const key = sessionStorage.key(0)!; const saved = JSON.parse(sessionStorage.getItem(key)!);
    saved.command.request.actor = "spoofed-user";
    saved.command.request.authority = { canAttestAccounting: true, canPostAccounting: true };
    saved.command.request.tenantId = "foreign-tenant";
    sessionStorage.setItem(key, JSON.stringify(saved));
    unmount();
    render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await screen.findByRole("button", { name: "Retry same cash-dividend command" });
    fireEvent.click(screen.getByRole("button", { name: "Retry same cash-dividend command" }));
    await waitFor(() => expect(api.attestCashDividend).toHaveBeenCalledTimes(2));
    const recovered = vi.mocked(api.attestCashDividend).mock.calls[1]![1];
    expect(recovered).not.toHaveProperty("actor");
    expect(recovered).not.toHaveProperty("authority");
    expect(recovered).not.toHaveProperty("tenantId");
    expect(recovered).toEqual(vi.mocked(api.attestCashDividend).mock.calls[0]![1]);
  });

  it("refuses stale confirmation after a definitive server conflict", async () => {
    vi.mocked(api.attestCashDividend).mockRejectedValueOnce(new ApiError({ path: "/cash-dividend-attestation", status: 409, detail: "Entitlement changed" }));
    render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await loaded(); attest();
    expect(await screen.findByRole("alert")).toHaveTextContent("Entitlement changed");
    expect(screen.queryByRole("button", { name: "Retry same cash-dividend command" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Attest this cash-dividend projection" })).not.toBeInTheDocument();
  });

  it("abandons old reads immediately when the selected case changes", async () => {
    let finish!: (value: ReturnType<typeof cashDividendWorkspace>) => void;
    vi.mocked(api.getCashDividendAttestationWorkspace).mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
    const { rerender } = render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    vi.mocked(api.getCashDividendAttestationWorkspace).mockRejectedValueOnce(new Error("New case unavailable"));
    rerender(<CashDividendAttestationPanel caseId="99999999-9999-4999-8999-999999999999" scope={cashDividendScope} />);
    await act(async () => finish(cashDividendWorkspace()));
    expect(await screen.findByText("New case unavailable")).toBeInTheDocument();
    expect(screen.queryByText("Cash-dividend policy enabled")).not.toBeInTheDocument();
    expect(api.attestCashDividend).not.toHaveBeenCalled();
  });

  it("is keyboard-addressable and has no axe accessibility violations", async () => {
    const { container } = render(<CashDividendAttestationPanel caseId={cashDividendCaseId} scope={cashDividendScope} />);
    await loaded();
    expect((await axe(container)).violations).toEqual([]);
  });
});
