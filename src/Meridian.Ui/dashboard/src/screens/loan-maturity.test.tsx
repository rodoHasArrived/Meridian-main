import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { axe } from "jest-axe";
import { buildLoanMaturityBuckets, isCalendarDate, maturityPeriod } from "@/screens/loan-maturity";
import { LoanMaturityView } from "@/screens/loan-maturity-view";
import hostPortfolio from "@/test/fixtures/loan-portfolio.host.json";
import type { LoanSummary } from "@/types/direct-lending.types";

/** Deliberately emulate unchecked host JSON so missing balances exercise runtime safeguards. */
function unsafeWireLoanFixture(payload: unknown): LoanSummary {
  if (!payload || typeof payload !== "object" || !("loanId" in payload) || typeof payload.loanId !== "string"
    || !("maturityDate" in payload) || typeof payload.maturityDate !== "string") {
    throw new Error("Malformed test fixture identity or calendar-date carrier.");
  }
  return payload as LoanSummary;
}

describe("loan maturity concentration", () => {
  const base = hostPortfolio.loans[0] as LoanSummary;
  it("uses true date-only quarter boundaries and exposes invalid dates", () => {
    expect(isCalendarDate("2026-02-30")).toBe(false);
    expect(maturityPeriod("2026-03-31", "2026-03-31")).toBe("2026 Q1");
    expect(maturityPeriod("2026-04-01", "2026-03-31")).toBe("2026 Q2");
    expect(maturityPeriod("2026-03-30", "2026-03-31")).toBe("Overdue");
    expect(maturityPeriod("bad", "2026-03-31")).toBe("Unknown maturity");
  });
  it("isolates currencies, keeps unreported amounts distinct from zero and withholds unspecified sums", () => {
    const buckets = buildLoanMaturityBuckets([
      { ...base, principalOutstanding: 20, baseCurrency: 0, maturityDate: "2027-01-01" },
      unsafeWireLoanFixture({ ...base, loanId: "missing", principalOutstanding: undefined, baseCurrency: "USD", maturityDate: "2027-01-01" }),
      { ...base, loanId: "eur", principalOutstanding: 30, baseCurrency: "EUR", maturityDate: "2027-01-01" },
      { ...base, loanId: "other", principalOutstanding: 40, baseCurrency: 4, maturityDate: "bad" }
    ], "2026-11-12");
    expect(buckets.find(bucket => bucket.currency === "USD")).toMatchObject({ amount: 20, missingAmountCount: 1 });
    expect(buckets.find(bucket => bucket.currency === "EUR")).toMatchObject({ amount: 30 });
    expect(buckets.find(bucket => bucket.currency === "Unspecified currency")).toMatchObject({ amount: null, period: "Unknown maturity" });
  });
  it("opens only the chosen currency/quarter facilities with their retained amounts", async () => {
    const { container } = render(<LoanMaturityView loans={[
      { ...base, facilityName: "Dollar facility", borrowerName: "Borrower A", principalOutstanding: 125, baseCurrency: "USD", maturityDate: "2027-01-01" },
      { ...base, loanId: "eur", facilityName: "Euro facility", principalOutstanding: 900, baseCurrency: "EUR", maturityDate: "2027-01-01" },
      { ...base, loanId: "jpy", facilityName: "Yen facility", principalOutstanding: 125, baseCurrency: "JPY", maturityDate: "2027-01-01" }
    ]} today="2026-11-12" />);
    expect(screen.getByRole("button", { name: "2027 Q1 1 facility ¥125; Inspect JPY maturity bucket" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "2027 Q1 1 facility $125.00; Inspect USD maturity bucket" }));
    const table = screen.getByRole("table", { name: "Facilities in selected maturity bucket" });
    expect(within(table).getByText("Dollar facility", { exact: false })).toBeInTheDocument();
    expect(within(table).getByText("$125.00")).toBeInTheDocument();
    expect(within(table).queryByText("Euro facility")).not.toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
    await userEvent.click(screen.getByRole("button", { name: "Clear maturity selection" }));
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});
