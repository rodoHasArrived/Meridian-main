import { fireEvent, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderWithRouter } from "@/test/render";
import type { ConsolidationView } from "@/types/consolidation";
import { ConsolidationResults } from "./accounting-screen.consolidation-results";

describe("Governed consolidation presentation", () => {
  it("keeps original-currency booking breaks, translation, rounding and posted balances distinct", () => {
    const view: ConsolidationView = { request: { organizationId: "organization-a", ownershipRootId: "fund-a", eliminationBookId: "overlay-book", periodId: "period-a", asOf: "2026-11-12" }, fundProfileId: "fund-a", currency: "USD",
      scopeLimitation: "Three direct wholly owned entities; reviewed FX and income treatments.", entityIds: ["entity-a", "entity-b", "entity-c"], ownershipLinkIds: ["ownership-a"], sourceFingerprint: "sources-v4", ruleVersion: "w10-v4", translationFingerprint: "approved-rate-authority", canCreateDrafts: false,
      balances: [{ accountPath: "Assets:Group receivable", accountType: "Asset", grossBalance: 130, proposedEliminations: -110, postedEliminations: -10, consolidatedBalance: 120, previewBalance: 10, sources: [] }],
      matches: [{ postingEntityId: "entity-a", counterpartyId: "entity-b", receivable: 130, payable: 110, matchedAmount: 110, unmatchedReceivable: 20, unmatchedPayable: 0, sources: [], agreementId: "agreement-a", treatment: "consolidation.receivable-payable", matchingCurrency: "EUR", matchedTransactionAmount: 100, unmatchedFirstTransactionAmount: 10, unmatchedSecondTransactionAmount: 0, translationDifference: 3.25 }],
      drafts: [], blockers: [], translationReserve: { "entity-a": -4.01 }, translationDifferences: { "entity-a": -4 }, roundingDifferences: { "entity-a": -0.01 } };
    renderWithRouter(<ConsolidationResults view={view} />);
    expect(screen.getByText("w10-v4")).toBeVisible();
    const balances = within(screen.getByRole("region", { name: "Group balance table" }));
    expect(balances.getAllByRole("cell").slice(0, 5).map((cell) => cell.textContent)).toEqual(["130.00", "(110.00)", "(10.00)", "120.00", "10.00"]);
    const matching = within(screen.getByRole("region", { name: "Reciprocal match table" }));
    expect(matching.getByText("100.00 EUR")).toBeVisible();
    expect(matching.getByText("3.25 USD")).toBeVisible();
    const differences = within(screen.getByRole("region", { name: "Translation and rounding differences" }));
    expect(differences.getByRole("columnheader", { name: "Rounding difference" })).toBeVisible();
    expect(differences.getByText("(4.01) USD")).toBeVisible();
    expect(differences.getByText("(4.00) USD")).toBeVisible();
    expect(differences.getByText("(0.01) USD")).toBeVisible();
  });

  it.each([
    ["KWD", 100.001, "100.001", "JPY", 1000, "1,000", "0.000", "0"],
    ["JPY", 1000, "1,000", "KWD", 100.001, "100.001", "0", "0.000"]
  ] as const)("uses reporting %s minor units and each original/journal currency independently", (currency, amount, formatted, originalCurrency, originalAmount, originalFormatted, zero, originalZero) => {
    const source = { ledgerBookId: "source-book", journalEntryId: "source-journal", lineId: "source-line", entityId: "entity-a", counterpartyId: "entity-b", accountPath: "Assets:Group receivable", debit: amount, credit: 0, effectiveDate: "2026-11-12", drillThrough: "retained-line", transactionCurrency: originalCurrency, transactionDebit: originalAmount, transactionCredit: 0, counterpartyDisposition: "FormerPerimeterExternal" };
    const view: ConsolidationView = { request: { organizationId: "organization-a", ownershipRootId: "fund-a", eliminationBookId: "overlay-book", periodId: "period-a", asOf: "2026-11-12" }, fundProfileId: "fund-a", currency,
      scopeLimitation: "Governed functional-to-reporting translation.", entityIds: ["entity-a", "entity-b"], ownershipLinkIds: [], sourceFingerprint: "precision-sources", ruleVersion: "w10-v3", blockers: [],
      balances: [{ accountPath: source.accountPath, accountType: "Asset", grossBalance: amount, proposedEliminations: 0, postedEliminations: 0, consolidatedBalance: amount, previewBalance: amount, sources: [source] }],
      matches: [{ postingEntityId: "entity-a", counterpartyId: "entity-b", receivable: amount, payable: amount, matchedAmount: amount, unmatchedReceivable: 0, unmatchedPayable: 0, sources: [], matchingCurrency: originalCurrency,
        matchedTransactionAmount: originalAmount, unmatchedFirstTransactionAmount: 0, unmatchedSecondTransactionAmount: 0, translationDifference: amount }],
      drafts: [{ journalEntryId: "precision-draft", status: "Draft", requiresRenewedReview: false, adjustsJournalEntryId: null,
        lines: [{ lineId: "precision-line", side: "Debit", amount: originalAmount, currency: originalCurrency, accountPath: "Assets:Receivable", entityId: "entity-a" }] }],
      translationReserve: { "entity-a": amount }, translationDifferences: { "entity-a": amount }, roundingDifferences: { "entity-a": 0 } };
    renderWithRouter(<ConsolidationResults view={view} />);
    const balances = within(screen.getByRole("region", { name: "Group balance table" }));
    expect(balances.getAllByRole("cell").slice(0, 5).map((cell) => cell.textContent)).toEqual([formatted, zero, zero, formatted, formatted]);
    const matching = within(screen.getByRole("region", { name: "Reciprocal match table" }));
    expect(matching.getByText(`${originalFormatted} ${originalCurrency}`)).toBeVisible();
    expect(matching.getByText(`Unmatched ${originalZero} / ${originalZero} ${originalCurrency}`)).toBeVisible();
    const differences = within(screen.getByRole("region", { name: "Translation and rounding differences" }));
    expect(differences.getAllByText(`${formatted} ${currency}`)).toHaveLength(2);
    expect(differences.getByText(`${zero} ${currency}`)).toBeVisible();
    fireEvent.click(balances.getByText(`Sources for ${source.accountPath} (1)`));
    expect(balances.getByText("Former perimeter · external balance, not eliminated")).toBeVisible();
    expect(balances.getByText(`Debit ${formatted} ${currency} · Credit ${zero} ${currency}`)).toBeVisible();
    expect(balances.getByText(`Original transaction: debit ${originalFormatted} · credit ${originalZero} ${originalCurrency}`)).toBeVisible();
    fireEvent.click(screen.getByText("Journal lines for precision-draft"));
    expect(screen.getByText(`Debit ${originalFormatted} ${originalCurrency} · Assets:Receivable · Entity entity-a`)).toBeVisible();
  });
});
