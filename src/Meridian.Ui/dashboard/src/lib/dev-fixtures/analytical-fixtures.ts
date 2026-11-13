import type { LoanPortfolioSummary, LoanSummary } from "@/types/direct-lending.types";
import { UI_API_ROUTES } from "../ui-api-routes.generated";
import { buildAnalyticalFamilyOverview } from "./family-analytical-fixtures";
import { analyticalMarginControl } from "./margin-analytical-fixtures";
import { analyticalStrategySweeps } from "./strategy-analytical-fixtures";
import { resolveAnalyticalExecutionAudit } from "./execution-analytical-fixtures";
import { apiRoutePattern, readFixtureSearchParams, type DynamicFixturePattern } from "./fixture-resolver";

// Deterministic demonstration records, served only by the explicit fixture layer.
// These are never a replacement for a failed authoritative read.
const loans: LoanSummary[] = [
  loan(1, "Harbour warehouse", "Harbour Logistics", "USD", 2_400_000, "2027-03-31"),
  loan(2, "Harbour equipment", "Harbour Logistics", "USD", 800_000, "2027-06-30"),
  loan(3, "Cedar expansion", "Cedar Manufacturing", "EUR", 1_100_000, "2027-03-31"),
  loan(4, "Cedar revolver", "Cedar Manufacturing", "EUR", 450_000, "2028-09-30"),
  loan(5, "North quay bridge", "North Quay Properties", "USD", 300_000, "2025-12-31"),
  loan(6, "North quay review", "North Quay Properties", "USD", 150_000, "")
];

export const analyticalLoanPortfolio: LoanPortfolioSummary = {
  totalLoans: loans.length,
  activeLoans: loans.length,
  defaultedLoans: 0,
  nonPerformingLoans: 0,
  workoutLoans: 0,
  totalCommitment: null,
  totalPrincipalOutstanding: null,
  totalInterestAccruedUnpaid: null,
  totalPenaltyAccruedUnpaid: null,
  totalAvailableToDraw: null,
  totalCollateralValue: null,
  totalCurrency: null,
  currencyTotals: ["USD", "EUR"].map((currency) => {
    const rows = loans.filter((row) => row.baseCurrency === currency);
    return {
      currency,
      totalCommitment: rows.reduce((sum, row) => sum + row.commitmentAmount, 0),
      totalPrincipalOutstanding: rows.reduce((sum, row) => sum + row.principalOutstanding, 0),
      totalInterestAccruedUnpaid: 0,
      totalPenaltyAccruedUnpaid: 0,
      totalAvailableToDraw: rows.reduce((sum, row) => sum + row.availableToDraw, 0),
      totalCollateralValue: null
    };
  }),
  loans
};

export const analyticalFixtureRoutes: Record<string, unknown> = {
  [UI_API_ROUTES.DirectLendingPortfolioSummary]: analyticalLoanPortfolio,
  [UI_API_ROUTES.ReconciliationMarginControl]: analyticalMarginControl,
  [UI_API_ROUTES.RunsSweeps]: analyticalStrategySweeps
};

export const analyticalFixturePatterns: DynamicFixturePattern[] = [{
  pattern: apiRoutePattern(`${UI_API_ROUTES.ExecutionAudit}/search`),
  resolve: (_cleanPath, path) => resolveAnalyticalExecutionAudit(path)
}, {
  pattern: apiRoutePattern(UI_API_ROUTES.WorkstationFamilyOfficeOverview),
  resolve: (_cleanPath, path) => {
    const overview = buildAnalyticalFamilyOverview();
    const requestedDate = readFixtureSearchParams(path).get("asOf");
    if (!requestedDate) return overview;
    const parsed = new Date(`${requestedDate}T00:00:00Z`);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(requestedDate) || !Number.isFinite(parsed.getTime())
      || parsed.toISOString().slice(0, 10) !== requestedDate) return undefined;
    // Only the legal relationship date changes; valuations retain their original dates.
    overview.ownershipGraph.asOfDate = requestedDate;
    overview.ownershipGraph.edges = overview.ownershipGraph.edges.filter((edge) =>
      (!edge.effectiveFrom || edge.effectiveFrom <= requestedDate)
      && (!edge.effectiveTo || requestedDate <= edge.effectiveTo));
    return overview;
  }
}];

function loan(id: number, facilityName: string, borrowerName: string, baseCurrency: string,
  principalOutstanding: number, maturityDate: string): LoanSummary {
  return {
    loanId: `10000000-0000-0000-0000-${String(id).padStart(12, "0")}`,
    borrowerId: `20000000-0000-0000-0000-${String(Math.ceil(id / 2)).padStart(12, "0")}`,
    facilityName,
    borrowerName,
    status: "Active",
    baseCurrency,
    commitmentAmount: principalOutstanding * 1.25,
    principalOutstanding,
    interestAccruedUnpaid: 0,
    penaltyAccruedUnpaid: 0,
    availableToDraw: principalOutstanding * 0.25,
    originationDate: "2024-01-15",
    maturityDate,
    lastAccrualDate: null,
    lastPaymentDate: null
  };
}
