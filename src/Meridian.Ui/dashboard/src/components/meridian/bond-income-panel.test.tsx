import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { BondIncomePanel } from "./bond-income-panel";
import type { BondIncomePreview } from "@/types/bond-workflow.types";

const result: BondIncomePreview = { ledgerBookId: "book", taxLotRecordId: "lot", fingerprint: "retained-fingerprint", income: { modelVersion: "bond-income-v1", floaterPolicy: "ProspectiveReset", effectiveAnnualYield: "0.04", openingCarryingBasis: "9900", closingCarryingBasis: "9910", couponIncome: "-5", premiumDiscountIncome: "10", indexationIncome: "0", totalAccountingIncome: "5", scheduledPrincipal: "0", capitalizedFees: "20", expensedFees: "1", withholdingTax: "0", interestReceivable: "0", interestPayable: "5", journalComponents: [] } };
function fill() { fireEvent.change(screen.getByLabelText("Account ID"), { target: { value: "account" } }); fireEvent.change(screen.getByLabelText("Ledger book ID"), { target: { value: "book" } }); fireEvent.change(screen.getByLabelText("Tax lot record ID"), { target: { value: "lot" } }); }
describe("BondIncomePanel", () => {
  it("shows the authoritative policy and signed coupon payable independently of market income", async () => {
    const preview = vi.fn(async () => result); render(<BondIncomePanel preview={preview} />); fill(); fireEvent.click(screen.getByRole("button", { name: "Preview accounting income" }));
    await screen.findByRole("region", { name: "Bond accounting income result" });
    expect(screen.getByText(/Prospective reset recalculation/)).toBeInTheDocument(); expect(screen.getByText("-5")).toBeInTheDocument(); expect(screen.getByText("Interest payable")).toBeInTheDocument();
  });
  it("rejects mismatched lot responses", async () => {
    const preview = vi.fn(async () => ({ ...result, taxLotRecordId: "other-lot" })); render(<BondIncomePanel preview={preview} />); fill(); fireEvent.click(screen.getByRole("button", { name: "Preview accounting income" }));
    await screen.findByRole("alert"); expect(screen.queryByRole("region", { name: "Bond accounting income result" })).not.toBeInTheDocument();
  });
});
