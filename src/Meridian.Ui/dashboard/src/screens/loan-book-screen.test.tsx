import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import * as directLendingApi from "@/lib/api/direct-lending.api";
import { LoanBookScreen } from "@/screens/loan-book-screen";
import type { LoanPortfolioSummary } from "@/types/direct-lending.types";

vi.mock("@/lib/api/direct-lending.api", () => ({
  getLoanPortfolioSummary: vi.fn()
}));

const api = vi.mocked(directLendingApi);

afterEach(() => {
  vi.resetAllMocks();
});

function summary(overrides: Partial<LoanPortfolioSummary> = {}): LoanPortfolioSummary {
  return {
    totalLoans: 2,
    activeLoans: 1,
    defaultedLoans: 1,
    nonPerformingLoans: 0,
    workoutLoans: 0,
    totalCommitment: 9_000_000,
    totalPrincipalOutstanding: 6_400_000,
    totalInterestAccruedUnpaid: 21_000,
    totalPenaltyAccruedUnpaid: 0,
    totalAvailableToDraw: 2_600_000,
    totalCollateralValue: 11_000_000,
    loans: [
      {
        loanId: "11111111-1111-1111-1111-111111111111",
        facilityName: "Senior Term Facility A",
        borrowerId: "22222222-2222-2222-2222-222222222222",
        borrowerName: "Harbour Logistics",
        status: "Active",
        baseCurrency: "USD",
        commitmentAmount: 5_000_000,
        principalOutstanding: 3_250_000,
        interestAccruedUnpaid: 18_400,
        penaltyAccruedUnpaid: 0,
        availableToDraw: 1_750_000,
        originationDate: "2025-02-14",
        maturityDate: "2030-02-14",
        lastAccrualDate: "2026-08-25",
        lastPaymentDate: "2026-08-01"
      }
    ],
    ...overrides
  };
}

describe("LoanBookScreen", () => {
  it("renders the loan book from the portfolio read", async () => {
    api.getLoanPortfolioSummary.mockResolvedValue(summary());

    render(<LoanBookScreen />);

    expect(await screen.findByText("Senior Term Facility A")).toBeInTheDocument();
    expect(screen.getByText("Harbour Logistics")).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Principal outstanding: $6,400,000.00" })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Direct lending facilities" })).toBeInTheDocument();
  });

  it("separates a failed read from an empty loan book", async () => {
    api.getLoanPortfolioSummary.mockRejectedValue(new Error("Direct lending service is unavailable."));

    render(<LoanBookScreen />);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Loan book could not be loaded");
    expect(alert).toHaveTextContent("Direct lending service is unavailable.");
    // An outage must not render as a portfolio with zero facilities.
    expect(screen.queryByRole("table", { name: "Direct lending facilities" })).not.toBeInTheDocument();
  });

  it("renders an empty loan book as empty rather than as a failure", () => {
    render(<LoanBookScreen summary={summary({ loans: [], totalLoans: 0, activeLoans: 0, defaultedLoans: 0 })} />);

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.getByText(/No direct-lending facilities are recorded/)).toBeInTheDocument();
    expect(api.getLoanPortfolioSummary).not.toHaveBeenCalled();
  });

  it("re-reads the portfolio when refreshed", async () => {
    api.getLoanPortfolioSummary.mockResolvedValue(summary());

    render(<LoanBookScreen />);
    await screen.findByText("Senior Term Facility A");

    await userEvent.click(screen.getByRole("button", { name: "Refresh the loan book" }));

    await waitFor(() => expect(api.getLoanPortfolioSummary).toHaveBeenCalledTimes(2));
  });

  it("announces the loan book state to assistive technology", async () => {
    api.getLoanPortfolioSummary.mockResolvedValue(summary());

    render(<LoanBookScreen />);

    await waitFor(() =>
      expect(screen.getByText("Loan book loaded with 1 facility, 1 needing attention.")).toBeInTheDocument()
    );
  });
});
