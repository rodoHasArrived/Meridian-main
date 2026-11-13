import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { LoanMaturityView, maturityBucketId } from "./loan-maturity-view";
import type { LoanPortfolioSummary } from "@/types/direct-lending.types";

const summary: LoanPortfolioSummary = {
  totalLoans: 4, activeLoans: 4, defaultedLoans: 0, nonPerformingLoans: 0, workoutLoans: 0,
  totalCommitment: null, totalPrincipalOutstanding: null, totalInterestAccruedUnpaid: null,
  totalPenaltyAccruedUnpaid: null, totalAvailableToDraw: null, totalCollateralValue: null,
  totalCurrency: null, currencyTotals: [],
  loans: ["a", "b", "c", "d"].map((loanId, index) => ({
    loanId, facilityName: `Synthetic facility ${loanId}`, borrowerId: `borrower-${loanId}`,
    borrowerName: "Synthetic borrower", status: "Active", baseCurrency: index < 2 ? "USD" : index === 2 ? "EUR" : "Other",
    commitmentAmount: 0, principalOutstanding: index < 2 ? 4503599627370496 : 0,
    interestAccruedUnpaid: 0, penaltyAccruedUnpaid: 0, availableToDraw: 0,
    originationDate: "2026-01-01", maturityDate: index === 3 ? "" : "2028-03-31",
    lastAccrualDate: null, lastPaymentDate: null
  })),
  maturityBuckets: [
  { currency: 0, year: 2028, quarter: 1, principalOutstanding: "9007199254740993.12345", loanIds: ["a", "b"] },
  { currency: "EUR", year: 2028, quarter: 1, principalOutstanding: "0", loanIds: ["c"] },
  { currency: "Other", year: null, quarter: null, principalOutstanding: null, loanIds: ["d"] }
] };

describe("loan maturity concentration", () => {
  it("retains exact server totals and selects currency-specific facility groups", async () => {
    const select = vi.fn();
    const { container } = render(<LoanMaturityView summary={summary} selectedId={null} onSelect={select} />);
    expect(screen.getByText("USD 9,007,199,254,740,993.12345")).toBeInTheDocument();
    expect(screen.getByText("EUR 0")).toBeInTheDocument();
    expect(screen.getByText("Amount unavailable")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /2028 Q1, 2 facilities/ }));
    expect(select).toHaveBeenCalledWith(maturityBucketId(summary.maturityBuckets![0]));
    expect((await axe(container)).violations).toEqual([]);
  });
  it("does not rebuild missing server totals from rounded row amounts", () => {
    render(<LoanMaturityView summary={{ ...summary, maturityBuckets: undefined }} selectedId="old" onSelect={vi.fn()} />);
    expect(screen.getByText(/aggregation was not supplied/)).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("selected maturity group is unavailable");
  });
});
