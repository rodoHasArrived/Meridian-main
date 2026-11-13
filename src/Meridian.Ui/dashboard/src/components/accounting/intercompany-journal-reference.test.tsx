import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { IntercompanyJournalReference } from "./intercompany-journal-reference";
import type { ManualJournalEntryLine } from "@/types";

const agreementId = "11111111-1111-1111-1111-111111111111";
const counterpartyId = "22222222-2222-2222-2222-222222222222";
const line: ManualJournalEntryLine = { lineId: "line", side: "Debit", amount: 100, currency: "USD", accountPath: "Assets:Intercompany Receivable",
  dimensions: { instrumentId: "retained-instrument", counterpartyId, intercompanyAgreementId: agreementId, intercompanyAgreementVersion: 2 } };

describe("IntercompanyJournalReference", () => {
  it("keeps pinned provenance inspectable while authoring capability is absent", async () => {
    const load = vi.fn().mockResolvedValue({ items: [], referencePostingSupported: false });
    const apply = vi.fn();
    render(<MemoryRouter><IntercompanyJournalReference line={line} entityId="entity" onApply={apply} loadCapability={load} /></MemoryRouter>);
    expect(screen.getByRole("link", { name: `Inspect agreement ${agreementId}` })).toHaveAttribute("href", expect.stringContaining(`agreementId=${agreementId}`));
    expect(await screen.findByText(/compatible posting writers/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Line agreement ID")).not.toBeInTheDocument();
    expect(apply).not.toHaveBeenCalled();
  });
  it("requires complete paired provenance and preserves all other dimensions when applied", async () => {
    const apply = vi.fn(); const load = vi.fn().mockResolvedValue({ items: [], referencePostingSupported: true });
    render(<MemoryRouter><IntercompanyJournalReference line={line} entityId="entity" onApply={apply} loadCapability={load} /></MemoryRouter>);
    const version = await screen.findByLabelText("Line approved agreement version");
    fireEvent.change(version, { target: { value: "" } });
    expect(screen.getByRole("button", { name: "Apply line agreement reference" })).toBeDisabled();
    fireEvent.change(version, { target: { value: "3" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply line agreement reference" }));
    expect(apply).toHaveBeenCalledWith({ dimensions: { instrumentId: "retained-instrument", counterpartyId, intercompanyAgreementId: agreementId, intercompanyAgreementVersion: 3 } });
  });
  it("keeps authoring closed when capability lookup fails", async () => {
    const load = vi.fn().mockRejectedValue(new Error("registry unavailable"));
    render(<MemoryRouter><IntercompanyJournalReference line={line} entityId="entity" onApply={vi.fn()} loadCapability={load} /></MemoryRouter>);
    expect(await screen.findByText(/compatible posting writers/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Line agreement ID")).not.toBeInTheDocument();
  });
});
