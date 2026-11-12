import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { IntercompanyAgreementJournals } from "./intercompany-agreement-journals";

describe("IntercompanyAgreementJournals", () => {
  it("preserves exact amounts and distinct currencies while exposing immutable source identity", async () => {
    const load = vi.fn().mockResolvedValue({ agreementId: "agreement", ledgerBookId: "book", asOf: "2026-11-12", coverageState: "Covered", lines: [{
      journalEntryId: "journal", lineId: "line", periodId: "period", sequence: 7, accountingDate: "2026-10-31", postingEntityId: "entity", counterpartyId: "counterparty",
      accountName: "Intercompany Receivable", description: "Retained source line", functionalCurrency: "USD", debit: "12345678901234567890.123456789012", credit: "0",
      transactionCurrency: "EUR", transactionDebit: "112233445566778899.123456789012", transactionCredit: "0", intercompanyAgreementId: "agreement", intercompanyAgreementVersion: 2,
      coverageState: "HistoricalRevision", warning: "Posted against the originally approved terms.", sourceReference: "ledger://journal/line"
    }] });
    render(<IntercompanyAgreementJournals agreementId="agreement" asOf="2026-11-12" initialLedgerBookId="book" load={load} />);
    expect(await screen.findByText("112233445566778899.123456789012 / 0 EUR")).toBeInTheDocument();
    expect(screen.getByText("12345678901234567890.123456789012 / 0 USD")).toBeInTheDocument();
    fireEvent.click(screen.getByText("Inspect source journal facts"));
    expect(screen.getByText("ledger://journal/line")).toBeInTheDocument();
    expect(screen.getByText("Posted against the originally approved terms.")).toBeInTheDocument();
    expect(load).toHaveBeenCalledWith("agreement", { ledgerBookId: "book", asOf: "2026-11-12" }, expect.anything());
  });

  it("reports inaccessible source authority as unknown coverage without presenting an empty balance", async () => {
    const load = vi.fn().mockRejectedValue(new Error("The ledger book is inaccessible."));
    render(<IntercompanyAgreementJournals agreementId="agreement" asOf="2026-11-12" initialLedgerBookId="book" load={load} />);
    await screen.findByText("Agreement coverage unknown");
    expect(screen.queryByText(/No retained source lines/)).not.toBeInTheDocument();
  });
});
