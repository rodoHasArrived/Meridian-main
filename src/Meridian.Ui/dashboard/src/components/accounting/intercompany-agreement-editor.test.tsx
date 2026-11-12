import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { IntercompanyAgreementEditor, newAgreementDraft } from "./intercompany-agreement-editor";

describe("IntercompanyAgreementEditor exact terms", () => {
  it("retains exact rate text and additional evidence when saving a draft", () => {
    const save = vi.fn();
    const initial = { ...newAgreementDraft("entity", "2026-11-12"), code: "loan", name: "Loan", partyTwoId: "other", currency: "USD",
      terms: { fixedAnnualRate: "0.0312345678901234567890123456", settlementDays: 30 },
      evidence: [{ reference: "evidence://evidence-vault/first" }, { reference: "evidence://evidence-vault/second", artifactId: "second-artifact", contentHash: "server-retained-hash" }] };
    render(<IntercompanyAgreementEditor initial={initial} fixedIdentity={false} busy={false} onSave={save} onCancel={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("Change rationale"), { target: { value: "Reviewed exact terms" } });
    fireEvent.click(screen.getByRole("button", { name: "Save agreement draft" }));
    expect(save).toHaveBeenCalled();
    expect(save.mock.calls[0][0].terms.fixedAnnualRate).toBe("0.0312345678901234567890123456");
    expect(save.mock.calls[0][0].terms.settlementDays).toBe(30);
    expect(save.mock.calls[0][0].evidence[1]).toEqual(initial.evidence[1]);
  });
  it("rejects settlement days outside the integer contract range", () => {
    const save = vi.fn(); const initial = { ...newAgreementDraft("entity", "2026-11-12"), code: "loan", name: "Loan", partyTwoId: "other", currency: "USD" };
    render(<IntercompanyAgreementEditor initial={initial} fixedIdentity={false} busy={false} onSave={save} onCancel={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("Settlement days"), { target: { value: "9007199254740993" } });
    fireEvent.change(screen.getByLabelText("Change rationale"), { target: { value: "Reviewed terms" } });
    fireEvent.submit(screen.getByRole("form", { name: "Create agreement draft" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Settlement days must be a nonnegative whole number");
    expect(save).not.toHaveBeenCalled();
  });
});
