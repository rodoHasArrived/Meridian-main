import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { IntercompanyAgreementsPanel } from "./accounting-screen.intercompany-agreements";
import { ApiError } from "@/lib/api-errors";
import * as api from "@/lib/api/intercompany-agreements.api";
import type { AgreementCommand, AgreementDetail, AgreementDraft } from "@/types/intercompany-agreements";

const draft: AgreementDraft = { code: "IC-LOAN", name: "Operating loan", partyOneId: "11111111-1111-1111-1111-111111111111", partyOneRole: "Lender",
  partyTwoId: "22222222-2222-2222-2222-222222222222", partyTwoRole: "Borrower", agreementType: "Loan", currency: "USD", effectiveFrom: "2026-01-01",
  action: "Activate", purpose: "Working capital", terms: { notes: "Retained loan terms", fixedAnnualRate: 0.05 }, evidence: [{ reference: "evidence://evidence-vault/loan-doc", artifactId: "loan-pdf", contentHash: "source-hash" }] };
const detail: AgreementDetail = { agreementId: "33333333-3333-3333-3333-333333333333", aggregateVersion: 2, code: draft.code, name: draft.name,
  partyOneId: draft.partyOneId, partyOneRole: draft.partyOneRole, partyTwoId: draft.partyTwoId, partyTwoRole: draft.partyTwoRole,
  agreementType: "Loan", currency: "USD", asOf: "2026-11-12", effectiveStatus: "Unapproved", applicableRevision: null,
  revisions: [], history: [{ eventId: "audit", proposalId: "proposal", action: "Submit", actor: "author", recordedAtUtc: "2026-11-12T12:00:00Z", aggregateVersion: 2 }],
  proposals: [{ proposalId: "proposal", action: "Activate", state: "Submitted", draft, authors: ["author"], submitter: "author", submittedAtUtc: "2026-11-12T12:00:00Z",
    submittedHash: "frozen-hash", createdAtUtc: "2026-11-12T11:00:00Z", updatedAtUtc: "2026-11-12T12:00:00Z" }],
  capabilities: { canEdit: false, canSubmit: false, canApprove: true, canReject: true, canWithdraw: false, canAmend: false, canTerminate: false } };
function services(next: AgreementDetail = detail) {
  return { ...api, listIntercompanyAgreements: vi.fn().mockResolvedValue({ items: [next], canCreate: true }), getIntercompanyAgreement: vi.fn().mockResolvedValue(next),
    mutateIntercompanyAgreement: vi.fn().mockImplementation((_action: string, command: AgreementCommand) => Promise.resolve({ detail: { ...next, aggregateVersion: 3 },
      receipt: { action: "Approve", commandId: command.commandId }, replayed: false })) };
}

describe("IntercompanyAgreementsPanel", () => {
  it("shows current lifecycle migration warnings and preserves the warnings recorded in native audit history", async () => {
    const source = services({ ...detail, migrationWarnings: ["Entity A has no retained legacy lifecycle baseline."],
      history: [{ ...detail.history[0], migrationWarnings: ["Missing baseline was retained at submission."] }] });
    render(<IntercompanyAgreementsPanel services={source} />);
    fireEvent.click(await screen.findByRole("button", { name: "IC-LOAN · Operating loan" }));
    expect(await screen.findByText("Entity A has no retained legacy lifecycle baseline.")).toBeVisible();
    fireEvent.click(screen.getByText("Agreement audit history"));
    expect(screen.getByText("Missing baseline was retained at submission.")).toBeVisible();
    expect(source.mutateIntercompanyAgreement).not.toHaveBeenCalled();
  });
  it("uses the selected entity/date and renders retained review evidence without claiming a journal posted", async () => {
    const source = services();
    const { container } = render(<IntercompanyAgreementsPanel initialEntityId={draft.partyOneId} services={source} />);
    fireEvent.click(await screen.findByRole("button", { name: "IC-LOAN · Operating loan" }));
    const proposal = await screen.findByRole("region", { name: "Intercompany agreements" });
    expect(within(proposal).getByText(/Base version Not provided/)).toBeInTheDocument();
    expect(screen.getAllByText(/artifact loan-pdf · SHA-256 source-hash/).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Approve submitted terms" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Review decision rationale"), { target: { value: "Reviewed signed terms" } });
    fireEvent.click(screen.getByRole("button", { name: "Approve submitted terms" }));
    await screen.findByText("Approve retained at version 3.");
    expect(source.mutateIntercompanyAgreement).toHaveBeenCalledWith("approve", expect.objectContaining({
      agreementId: detail.agreementId, proposalId: "proposal", expectedAggregateVersion: 2, submittedHash: "frozen-hash", rationale: "Reviewed signed terms"
    }), expect.anything());
    const command = source.mutateIntercompanyAgreement.mock.calls[0][1];
    expect(command).not.toHaveProperty("actor"); expect(command).not.toHaveProperty("tenantId");
    expect((await axe(container)).violations).toEqual([]);
  });

  it("obeys independent-review and creation capabilities rather than inferring authority from identity", async () => {
    const source = services({ ...detail, capabilities: { ...detail.capabilities, canApprove: false, canReject: false, blockingReason: "An independent reviewer is required." } });
    source.listIntercompanyAgreements.mockResolvedValue({ items: [detail], canCreate: false });
    render(<IntercompanyAgreementsPanel services={source} />);
    fireEvent.click(await screen.findByRole("button", { name: "IC-LOAN · Operating loan" }));
    await screen.findByText("An independent reviewer is required.");
    fireEvent.change(screen.getByLabelText("Review decision rationale"), { target: { value: "Self review" } });
    expect(screen.getByRole("button", { name: "Approve submitted terms" })).toBeDisabled();
    const create = screen.getByRole("button", { name: "New agreement draft" });
    expect(create).toBeDisabled();
    expect(create).toHaveAccessibleDescription("Agreement draft creation is not available for your current access.");
    expect(source.mutateIntercompanyAgreement).not.toHaveBeenCalled();
  });

  it("retains local draft input and stable command identity across a retry after a conflict", async () => {
    const editable = { ...detail, proposals: [{ ...detail.proposals[0], state: "Draft" as const, submittedHash: null }], capabilities: { ...detail.capabilities, canEdit: true } };
    const source = services(editable);
    source.mutateIntercompanyAgreement.mockRejectedValue(new ApiError({ path: "/agreement", status: 409, detail: "The retained version changed." }));
    render(<IntercompanyAgreementsPanel services={source} />);
    fireEvent.click(await screen.findByRole("button", { name: "IC-LOAN · Operating loan" }));
    fireEvent.click(await screen.findByRole("button", { name: "Edit agreement draft" }));
    fireEvent.change(screen.getByLabelText("Agreement name"), { target: { value: "Unsaved reviewed loan" } });
    fireEvent.change(screen.getByLabelText("Change rationale"), { target: { value: "Updated signed terms" } });
    fireEvent.click(screen.getByRole("button", { name: "Save agreement draft" }));
    await screen.findByText("Agreement changed — local input retained");
    expect(screen.getByLabelText("Agreement name")).toHaveValue("Unsaved reviewed loan");
    fireEvent.click(screen.getByRole("button", { name: "Save agreement draft" }));
    await waitFor(() => expect(source.mutateIntercompanyAgreement).toHaveBeenCalledTimes(2));
    expect(source.mutateIntercompanyAgreement.mock.calls[0][1].commandId).toBe(source.mutateIntercompanyAgreement.mock.calls[1][1].commandId);
    expect(source.mutateIntercompanyAgreement.mock.calls[1][1].draft.evidence[0].contentHash).toBeUndefined();
  });

  it("reports unavailable authority separately from an empty registry and disables creation", async () => {
    const source = services(); source.listIntercompanyAgreements.mockRejectedValue(new Error("The authoritative registry is offline."));
    render(<IntercompanyAgreementsPanel services={source} />);
    await screen.findByText("The authoritative registry is offline.");
    expect(screen.queryByText("No authorized agreements match these filters.")).not.toBeInTheDocument();
    const create = screen.getByRole("button", { name: "New agreement draft" });
    expect(create).toBeDisabled();
    expect(create).toHaveAccessibleDescription("Refresh the agreement registry to check whether you can create a draft.");
  });

  it("explains loading and open-draft blockers and clears the reason when creation becomes available", async () => {
    const source = services();
    render(<IntercompanyAgreementsPanel services={source} />);
    const create = screen.getByRole("button", { name: "New agreement draft" });
    expect(create).toBeDisabled();
    expect(create).toHaveAccessibleDescription("Wait for the agreement registry to finish loading.");
    await waitFor(() => expect(create).toBeEnabled());
    expect(create).not.toHaveAttribute("aria-describedby");
    fireEvent.click(create);
    expect(create).toBeDisabled();
    expect(create).toHaveAccessibleDescription("Save or cancel the open agreement draft before starting another.");
    fireEvent.click(screen.getByRole("button", { name: "Discard local changes" }));
    expect(create).toBeEnabled();
    expect(create).not.toHaveAttribute("aria-describedby");
    expect(source.mutateIntercompanyAgreement).not.toHaveBeenCalled();
  });

  it("cancels a pending mutation when the session-scoped panel unmounts", async () => {
    const source = services(); source.mutateIntercompanyAgreement.mockReturnValue(new Promise(() => {}));
    const view = render(<IntercompanyAgreementsPanel services={source} />);
    fireEvent.click(await screen.findByRole("button", { name: "IC-LOAN · Operating loan" }));
    fireEvent.change(await screen.findByLabelText("Review decision rationale"), { target: { value: "Review exact submitted packet" } });
    fireEvent.click(screen.getByRole("button", { name: "Approve submitted terms" }));
    const signal = source.mutateIntercompanyAgreement.mock.calls[0][2].signal;
    view.unmount(); expect(signal.aborted).toBe(true);
  });

  it("refreshes current authority after recovering an old command receipt", async () => {
    const source = services();
    source.mutateIntercompanyAgreement.mockResolvedValue({ detail: { ...detail, aggregateVersion: 3 }, receipt: { action: "Approve" }, replayed: true });
    source.getIntercompanyAgreement.mockResolvedValueOnce(detail).mockResolvedValueOnce({ ...detail, aggregateVersion: 6,
      capabilities: { ...detail.capabilities, canApprove: false, canReject: false } });
    render(<IntercompanyAgreementsPanel services={source} />);
    fireEvent.click(await screen.findByRole("button", { name: "IC-LOAN · Operating loan" }));
    fireEvent.change(await screen.findByLabelText("Review decision rationale"), { target: { value: "Reviewed exact submitted terms" } });
    fireEvent.click(screen.getByRole("button", { name: "Approve submitted terms" }));
    await screen.findByText("Approve retained at version 3 (original receipt recovered; current version 6).");
    expect(screen.getByRole("button", { name: "Approve submitted terms" })).toBeDisabled();
    expect(source.getIntercompanyAgreement).toHaveBeenCalledTimes(2);
  });
});
