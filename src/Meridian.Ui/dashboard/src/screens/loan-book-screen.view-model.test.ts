import { describe, expect, it } from "vitest";

import {
  buildLoanBookRow,
  buildLoanBookViewModel,
  loanStatusTone
} from "@/screens/loan-book-screen.view-model";
import type { LoanPortfolioSummary, LoanSummary } from "@/types/direct-lending.types";

function loan(overrides: Partial<LoanSummary> = {}): LoanSummary {
  return {
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
    lastPaymentDate: "2026-08-01",
    ...overrides
  };
}

function summary(overrides: Partial<LoanPortfolioSummary> = {}): LoanPortfolioSummary {
  return {
    totalLoans: 3,
    activeLoans: 2,
    defaultedLoans: 1,
    nonPerformingLoans: 0,
    workoutLoans: 0,
    totalCommitment: 12_000_000,
    totalPrincipalOutstanding: 8_100_000,
    totalInterestAccruedUnpaid: 42_000,
    totalPenaltyAccruedUnpaid: 5_000,
    totalAvailableToDraw: 3_900_000,
    totalCollateralValue: 14_500_000,
    loans: [loan()],
    ...overrides
  };
}

describe("loan book view model", () => {
  it("reports an unloaded desk without inventing totals", () => {
    const view = buildLoanBookViewModel(null);

    expect(view.loaded).toBe(false);
    expect(view.metrics).toHaveLength(0);
    expect(view.rows).toHaveLength(0);
    expect(view.statusAnnouncement).toBe("Loan book has not loaded.");
  });

  it("renders portfolio totals and a status census", () => {
    const view = buildLoanBookViewModel(summary());

    expect(view.loaded).toBe(true);
    expect(view.metrics.find((metric) => metric.id === "total-loans")?.value).toBe("3");
    expect(view.metrics.find((metric) => metric.id === "principal-outstanding")?.value).toBe("$8,100,000.00");
    expect(view.metrics.find((metric) => metric.id === "collateral-value")?.value).toBe("$14,500,000.00");
    expect(view.statusAnnouncement).toBe("Loan book loaded with 1 facility, 1 needing attention.");
  });

  it("tones an impaired status as a danger and a healthy one as success", () => {
    expect(loanStatusTone("Active")).toBe("success");
    expect(loanStatusTone("Defaulted")).toBe("danger");
    expect(loanStatusTone("NonPerforming")).toBe("danger");
    expect(loanStatusTone("Non-Performing")).toBe("danger");
    expect(loanStatusTone("Workout")).toBe("danger");
    expect(loanStatusTone("Repaid")).toBe("default");
    expect(loanStatusTone(null)).toBe("default");
  });

  it("keeps a zero count from being toned as a problem", () => {
    const view = buildLoanBookViewModel(summary({ defaultedLoans: 0 }));
    const defaulted = view.metrics.find((metric) => metric.id === "defaulted-loans");

    expect(defaulted?.value).toBe("0");
    expect(defaulted?.tone).toBe("default");
  });

  it("names the amounts the server did not report instead of rendering them as zero", () => {
    const row = buildLoanBookRow(
      loan({
        principalOutstanding: null as unknown as number,
        availableToDraw: undefined as unknown as number
      })
    );

    expect(row.principalOutstandingLabel).toBe("—");
    expect(row.availableToDrawLabel).toBe("—");
    expect(row.missing).toEqual(["Principal outstanding", "Available to draw"]);
    expect(row.ariaLabel).toContain("principal outstanding, available to draw not reported");
    // A reported figure on the same row still renders.
    expect(row.commitmentLabel).toBe("$5,000,000.00");
  });

  it("passes DateOnly values through rather than reparsing them into a shifted day", () => {
    const row = buildLoanBookRow(loan({ maturityDate: "2030-02-14", lastPaymentDate: null }));

    expect(row.maturityLabel).toBe("2030-02-14");
    expect(row.lastPaymentLabel).toBe("—");
  });

  it("renders each facility in its own base currency", () => {
    const row = buildLoanBookRow(loan({ baseCurrency: "EUR", commitmentAmount: 1_000_000 }));

    expect(row.commitmentLabel).toBe("€1,000,000.00");
  });

  it("falls back to placeholder identity text rather than rendering a blank row", () => {
    const row = buildLoanBookRow(loan({ facilityName: "  ", borrowerName: "", status: "" }));

    expect(row.facilityName).toBe("Unnamed facility");
    expect(row.borrowerName).toBe("Unknown borrower");
    expect(row.statusLabel).toBe("Unknown");
    expect(row.statusTone).toBe("default");
  });

  it("does not announce NaN when the server omits a status count", () => {
    // The counts are non-nullable in the contract but arrive as unchecked JSON. Summing an
    // absent one would read out as "NaN needing attention" to a screen reader.
    const view = buildLoanBookViewModel(summary({ workoutLoans: undefined as unknown as number }));

    expect(view.statusAnnouncement).not.toContain("NaN");
    expect(view.statusAnnouncement).toBe(
      "Loan book loaded with 1 facility, at least 1 needing attention; some status counts were not reported."
    );
  });

  it("reports an empty loan book as empty rather than as unloaded", () => {
    const view = buildLoanBookViewModel(summary({ loans: [], totalLoans: 0, activeLoans: 0, defaultedLoans: 0 }));

    expect(view.loaded).toBe(true);
    expect(view.hasLoans).toBe(false);
    expect(view.statusAnnouncement).toBe("Loan book loaded with no facilities.");
    expect(view.emptyText).toContain("No direct-lending facilities are recorded");
  });
});
