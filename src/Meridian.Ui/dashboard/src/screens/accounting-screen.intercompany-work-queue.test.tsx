import { act, fireEvent, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import type { EntityOperationScope } from "@/types/entity-operations";
import type { AgreementWorkItem, AgreementWorkQueue } from "@/types/intercompany-work-queue";
import { IntercompanyAgreementWorkQueue } from "./accounting-screen.intercompany-work-queue";

const book = { ledgerBookId: "book-a", fundProfileId: "profile-a", fundStructureNodeId: "entity-a", fundStructureNodeKind: "Entity", displayName: "Operating book", baseCurrency: "USD", createdAt: "2026-01-01", updatedAt: "2026-01-01", accountingBasis: "Primary" as const, accountingPolicyId: "primary", accountingPolicyVersion: "v1" };
const scope: EntityOperationScope = { book, books: [book], capabilities: { canManageAgreements: true, canManageLedger: true, canManageLifecycle: true, canViewLedger: true }, graph: { organizations: [], entities: [{ entityId: "entity-a", name: "Operating entity", baseCurrency: "USD", lifecycleStatus: "Active" }], ownershipLinks: [] } };
const candidate: AgreementWorkItem = {
  caseId: "case-a", ledgerBookId: "book-a", agreementId: "agreement-a", agreementVersion: 2,
  findingCode: "intercompany.agreement.expiring", severity: "Warning", message: "Loan coverage ends on 2026-12-01.",
  suggestedAction: "Review an amendment or controlled wind-down.", dueDate: "2026-12-01", observedAsOf: "2026-11-23",
  isCurrentlyObserved: true, isTracked: false, state: "Open", owner: null, firstSeenAtUtc: null, lastObservedAtUtc: null,
  ageDays: null, daysOverdue: 0, lastObservedSourceFingerprint: "source-1", version: 0, recordHash: null, history: []
};
const tracked: AgreementWorkItem = { ...candidate, isTracked: true, version: 4, recordHash: "case-hash-4", firstSeenAtUtc: "2026-11-20T12:00:00Z", lastObservedAtUtc: "2026-11-22T12:00:00Z", ageDays: 3,
  history: [{ commandId: "prior-command", version: 4, recordHash: "case-hash-4", action: "Observe", state: "Open", actor: "operator-a", recordedAtUtc: "2026-11-22T12:00:00Z", reason: "Track renewal", owner: null, note: null, evidenceReferences: [], observedAsOf: "2026-11-22", sourceFingerprint: "source-0",
    sourceVersions: [{ sourceKind: "legal-agreement", sourceId: "agreement-a", version: "2", contentHash: "retained-legal-hash" }] }] };
function queue(item = candidate): AgreementWorkQueue {
  return { ledgerBookId: "book-a", fundProfileId: "profile-a", asOf: "2026-11-23", expiryHorizonDays: 30, evaluatedAtUtc: "2026-11-23T12:00:00Z", status: "Current", isComplete: true, sourceFingerprint: "source-1", canManage: true, cases: [item], guarantees: [], diagnostics: [] };
}
function services(value = queue()) {
  return { getAgreementWorkQueue: vi.fn().mockResolvedValue(value), observeAgreementWorkItem: vi.fn().mockResolvedValue(tracked), updateAgreementWorkItem: vi.fn().mockResolvedValue(tracked) };
}
async function loadAndSelect() {
  fireEvent.click(screen.getByRole("button", { name: "Load lifecycle queue" }));
  fireEvent.click(await screen.findByRole("button", { name: candidate.message }));
}

describe("Agreement lifecycle queue", () => {
  it("loads read-only before explicit tracking and preserves exact guarantee caps with unknown exposure", async () => {
    const value = queue();
    value.guarantees = [{ agreementId: "guarantee-a", code: "G-1", name: "Synthetic guarantee", agreementVersion: 3, payloadHash: "legal-hash",
      partyOneId: "entity-a", partyOneRole: "Guarantor", partyTwoId: "entity-b", partyTwoRole: "Obligor", currency: "USD", beneficiaryReference: "Bank reference",
      limitAmount: "12345678901234567890.12345678", effectiveFrom: "2026-01-01", effectiveTo: "2027-01-01", terminationEffectiveDate: null, effectiveStatus: "Active",
      currentExposure: null, exposureStatus: "Unavailable", exposureUnavailableReason: "No reviewed underlying obligation mapping." }];
    const api = services(value);
    const { container } = renderWithRouter(<IntercompanyAgreementWorkQueue scope={scope} asOf="2026-11-23" expiryHorizonDays={30} services={api} />);
    expect(api.getAgreementWorkQueue).not.toHaveBeenCalled();
    await loadAndSelect();
    expect(screen.getByText("12345678901234567890.12345678 USD")).toBeVisible();
    expect(screen.getByText("Not available")).toBeVisible();
    expect(screen.getByRole("button", { name: "Track this finding" })).toBeDisabled();
    expect(api.observeAgreementWorkItem).not.toHaveBeenCalled();
    expect(api.updateAgreementWorkItem).not.toHaveBeenCalled();
    expect((await axe(container)).violations).toEqual([]);
  });
  it("tracks exactly the displayed source and untracked version, then reloads authority", async () => {
    const api = services(); api.getAgreementWorkQueue.mockResolvedValueOnce(queue()).mockResolvedValue(queue(tracked));
    renderWithRouter(<IntercompanyAgreementWorkQueue scope={scope} asOf="2026-11-23" expiryHorizonDays={30} services={api} />);
    await loadAndSelect();
    fireEvent.change(screen.getByLabelText("Lifecycle action reason"), { target: { value: "Follow up before expiry" } });
    fireEvent.click(screen.getByRole("button", { name: "Track this finding" }));
    await screen.findByText("Case action retained at version 4.");
    expect(api.observeAgreementWorkItem).toHaveBeenCalledWith({ ledgerBookId: "book-a", fundProfileId: "profile-a", asOf: "2026-11-23", expiryHorizonDays: 30 },
      expect.objectContaining({ commandId: expect.any(String), caseId: "case-a", expectedVersion: 0, expectedRecordHash: null, expectedSourceFingerprint: "source-1", reason: "Follow up before expiry" }), expect.any(Object));
    expect(api.getAgreementWorkQueue).toHaveBeenCalledTimes(2);
    expect(screen.getByRole("cell", { name: /3 calendar days/ })).toBeVisible();
    fireEvent.click(screen.getByText("Lifecycle case history and source evidence"));
    expect(screen.getByRole("list", { name: "Retained source versions for case version 4" })).toHaveTextContent("retained-legal-hash");
  });
  it("retains assignment and requires explicit evidence before resolving a case", async () => {
    const api = services(queue(tracked));
    renderWithRouter(<IntercompanyAgreementWorkQueue scope={scope} asOf="2026-11-23" expiryHorizonDays={30} services={api} />);
    await loadAndSelect();
    fireEvent.change(screen.getByLabelText("Lifecycle action reason"), { target: { value: "Owner assigned" } });
    fireEvent.change(screen.getByLabelText("Lifecycle owner"), { target: { value: "operator-b" } });
    fireEvent.click(screen.getByRole("button", { name: "Retain case action" }));
    await waitFor(() => expect(api.updateAgreementWorkItem).toHaveBeenCalledOnce());
    expect(api.updateAgreementWorkItem.mock.calls[0][1]).toMatchObject({ caseId: "case-a", action: "AssignOwner", expectedVersion: 4, expectedRecordHash: "case-hash-4", expectedSourceFingerprint: "source-1", owner: "operator-b", reason: "Owner assigned" });
    await screen.findByRole("button", { name: "Retain case action" });
    fireEvent.change(screen.getByLabelText("Lifecycle case action"), { target: { value: "Resolve" } });
    fireEvent.change(screen.getByLabelText("Lifecycle action reason"), { target: { value: "Reviewed amendment retained" } });
    expect(screen.getByRole("button", { name: "Retain case action" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Lifecycle evidence references (one per line)"), { target: { value: "retained:amendment-7" } });
    expect(screen.getByRole("button", { name: "Retain case action" })).toBeEnabled();
  });
  it.each(["Partial", "Stale", "Unavailable"])("keeps retained missing findings open and disables commands for %s source evidence", async status => {
    const api = services({ ...queue({ ...tracked, isCurrentlyObserved: false }), status, isComplete: false });
    renderWithRouter(<IntercompanyAgreementWorkQueue scope={scope} asOf="2026-11-23" expiryHorizonDays={30} services={api} />);
    await loadAndSelect();
    fireEvent.change(screen.getByLabelText("Lifecycle action reason"), { target: { value: "Do not lose history" } });
    expect(screen.getByRole("cell", { name: /Open/ })).toHaveTextContent("Not observed at this cutoff");
    expect(screen.getByRole("button", { name: "Retain case action" })).toBeDisabled();
    expect(api.updateAgreementWorkItem).not.toHaveBeenCalled();
  });
  it("honors server read-only capability even when the surrounding screen can manage ledger", async () => {
    const api = services({ ...queue(tracked), canManage: false });
    renderWithRouter(<IntercompanyAgreementWorkQueue scope={scope} asOf="2026-11-23" expiryHorizonDays={30} services={api} />);
    await loadAndSelect();
    fireEvent.change(screen.getByLabelText("Lifecycle action reason"), { target: { value: "Reason" } });
    expect(screen.getByRole("button", { name: "Retain case action" })).toBeDisabled();
    expect(screen.getByText(/Read-only access/)).toBeVisible();
  });
  it("requires refresh after a conflicting update and retains local input through that refresh", async () => {
    const api = services(queue(tracked)); api.updateAgreementWorkItem.mockRejectedValueOnce(new Error("Case changed after review"));
    renderWithRouter(<IntercompanyAgreementWorkQueue scope={scope} asOf="2026-11-23" expiryHorizonDays={30} services={api} />);
    await loadAndSelect();
    fireEvent.change(screen.getByLabelText("Lifecycle action reason"), { target: { value: "Retained local reason" } });
    fireEvent.click(screen.getByRole("button", { name: "Retain case action" }));
    await screen.findByRole("alert");
    expect(screen.getByRole("button", { name: "Retain case action" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Load lifecycle queue" }));
    await screen.findByRole("button", { name: "Retain case action" });
    expect(screen.getByLabelText("Lifecycle action reason")).toHaveValue("Retained local reason");
  });
  it("keeps a confirmed action visible when its authority reload fails", async () => {
    const api = services(queue(tracked));
    api.getAgreementWorkQueue.mockResolvedValueOnce(queue(tracked)).mockRejectedValueOnce(new Error("Source unavailable"));
    renderWithRouter(<IntercompanyAgreementWorkQueue scope={scope} asOf="2026-11-23" expiryHorizonDays={30} services={api} />);
    await loadAndSelect();
    fireEvent.change(screen.getByLabelText("Lifecycle action reason"), { target: { value: "Assign renewal follow-up" } });
    fireEvent.click(screen.getByRole("button", { name: "Retain case action" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("The case action was retained");
    expect(screen.getByText("Case action retained at version 4.")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Retain case action" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Load lifecycle queue" })).toBeEnabled();
  });
  it("discards a delayed response and selected history when book or date scope changes", async () => {
    let finish!: (value: AgreementWorkQueue) => void;
    const api = services(); api.getAgreementWorkQueue.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const { rerender } = renderWithRouter(<IntercompanyAgreementWorkQueue scope={scope} asOf="2026-11-23" expiryHorizonDays={30} services={api} />);
    fireEvent.click(screen.getByRole("button", { name: "Load lifecycle queue" }));
    rerender(<IntercompanyAgreementWorkQueue scope={scope} asOf="2026-11-24" expiryHorizonDays={30} services={api} />);
    await act(async () => { finish(queue(tracked)); });
    expect(screen.queryByText(candidate.message)).not.toBeInTheDocument();
    expect(screen.queryByText("3 calendar days")).not.toBeInTheDocument();
    expect(screen.getByText(/as of 2026-11-24/)).toBeVisible();
  });
});
