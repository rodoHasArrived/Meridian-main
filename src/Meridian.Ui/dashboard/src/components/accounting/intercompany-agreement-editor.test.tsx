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

it("saves structured loan declarations as exact decimal text and clears legacy scalar alternatives", () => {
  const save = vi.fn(); const initial = { ...newAgreementDraft("entity-a", "2026-11-13"), code: "loan", name: "Loan", partyTwoId: "entity-b", currency: "USD" };
  render(<IntercompanyAgreementEditor initial={initial} fixedIdentity={false} busy={false} onSave={save} onCancel={vi.fn()} />);
  fireEvent.change(screen.getByLabelText("Legal terms format"), { target: { value: "StructuredV1" } });
  expect(screen.getByLabelText("Declared business calendar")).toHaveValue("");
  fireEvent.change(screen.getByLabelText("Accrual schedule end"), { target: { value: "2027-11-13" } });
  fireEvent.change(screen.getByLabelText("Declared business calendar"), { target: { value: "WEEKENDS" } });
  fireEvent.change(screen.getByLabelText("Structured annual rate (fraction)"), { target: { value: "0.0312345678901234567890123456" } });
  fireEvent.change(screen.getByLabelText("Declared day count"), { target: { value: "Actual360" } });
  fireEvent.change(screen.getByLabelText("Declared compounding"), { target: { value: "Simple" } });
  fireEvent.change(screen.getByLabelText("Change rationale"), { target: { value: "Signed structured terms" } });
  fireEvent.submit(screen.getByRole("form", { name: "Create agreement draft" }));
  expect(save.mock.calls[0][0].terms).toMatchObject({ fixedAnnualRate: null, settlementDays: null, structuredTerms: { fixedAnnualRate: "0.0312345678901234567890123456", rateBasis: "Fixed", dayCount: "Actual360", compounding: "Simple", schedule: { calendarId: "WEEKENDS" } } });
});

it("keeps a floating declaration free of the former fixed rate", () => {
  const save = vi.fn(); const initial = { ...newAgreementDraft("entity-a", "2026-11-13"), code: "loan", name: "Loan", partyTwoId: "entity-b", currency: "USD" };
  render(<IntercompanyAgreementEditor initial={initial} fixedIdentity={false} busy={false} onSave={save} onCancel={vi.fn()} />);
  fireEvent.change(screen.getByLabelText("Legal terms format"), { target: { value: "StructuredV1" } });
  fireEvent.change(screen.getByLabelText("Structured annual rate (fraction)"), { target: { value: "0.05" } });
  fireEvent.change(screen.getByLabelText("Declared rate basis"), { target: { value: "Floating" } });
  fireEvent.change(screen.getByLabelText("Floating index reference"), { target: { value: "SOFR-declared" } });
  fireEvent.change(screen.getByLabelText("Declared spread (basis points)"), { target: { value: "123.4567890123456789012345678" } });
  fireEvent.submit(screen.getByRole("form", { name: "Create agreement draft" }));
  expect(save.mock.calls[0][0].terms.structuredTerms).toMatchObject({ rateBasis: "Floating", fixedAnnualRate: null, floatingIndexReference: "SOFR-declared", spreadBps: "123.4567890123456789012345678" });
});

it("records guarantee beneficiary and an unknown limit without forcing a payment schedule", () => {
  const save = vi.fn(); const initial = { ...newAgreementDraft("entity-a", "2026-11-13"), code: "guarantee", name: "Guarantee", partyTwoId: "entity-b", currency: "USD" };
  render(<IntercompanyAgreementEditor initial={initial} fixedIdentity={false} busy={false} onSave={save} onCancel={vi.fn()} />);
  fireEvent.change(screen.getByLabelText("Agreement type"), { target: { value: "Guarantee" } });
  fireEvent.change(screen.getByLabelText("Legal terms format"), { target: { value: "StructuredV1" } });
  fireEvent.change(screen.getByLabelText("Guarantee beneficiary reference"), { target: { value: "External lender" } });
  expect(screen.queryByLabelText("Accrual schedule start")).not.toBeInTheDocument();
  fireEvent.submit(screen.getByRole("form", { name: "Create agreement draft" }));
  expect(save.mock.calls[0][0].terms.structuredTerms).toEqual({ schemaVersion: 1, guarantee: { beneficiaryReference: "External lender", limitAmount: null } });
});
