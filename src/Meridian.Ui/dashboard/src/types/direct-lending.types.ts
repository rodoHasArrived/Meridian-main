/**
 * Browser-side mirrors of the direct-lending read contract.
 *
 * These follow `LoanPortfolioSummaryDto` and `LoanSummaryDto` in
 * `src/Meridian.Contracts/DirectLending/DirectLendingDtos.cs`. Only the read surface is
 * mirrored: loan servicing commands (drawdowns, accruals, collateral, servicer statements)
 * remain desktop-owned, so nothing here describes a mutation.
 *
 * Money arrives as a decimal from the server and is typed `number` here to match the rest
 * of the dashboard's read models. Dates arrive as `DateOnly` and are carried as the raw
 * ISO strings the server emits rather than being parsed, so a malformed or absent date is
 * reported as sent instead of being coerced into a wrong day.
 */

/** Loan lifecycle state as the server reports it; unknown values are preserved verbatim. */
export type LoanStatusName = string;

export interface LoanSummary {
  loanId: string;
  facilityName: string;
  borrowerId: string;
  borrowerName: string;
  status: LoanStatusName;
  baseCurrency: string;
  commitmentAmount: number;
  principalOutstanding: number;
  interestAccruedUnpaid: number;
  penaltyAccruedUnpaid: number;
  availableToDraw: number;
  originationDate: string;
  maturityDate: string;
  /** Null when the loan has never accrued; distinct from an accrual dated today. */
  lastAccrualDate: string | null;
  /** Null when no payment has ever been received; distinct from a zero payment. */
  lastPaymentDate: string | null;
}

export interface LoanPortfolioSummary {
  totalLoans: number;
  activeLoans: number;
  defaultedLoans: number;
  nonPerformingLoans: number;
  workoutLoans: number;
  totalCommitment: number;
  totalPrincipalOutstanding: number;
  totalInterestAccruedUnpaid: number;
  totalPenaltyAccruedUnpaid: number;
  totalAvailableToDraw: number;
  totalCollateralValue: number;
  loans: LoanSummary[];
}
