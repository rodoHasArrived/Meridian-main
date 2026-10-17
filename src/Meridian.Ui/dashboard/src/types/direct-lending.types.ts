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
export type LoanStatusName = string | number;
/** The shared host currently writes enums as numbers; named-enum hosts remain supported. */
export type LoanCurrency = string | number;

export interface LoanSummary {
  loanId: string;
  facilityName: string;
  borrowerId: string;
  borrowerName: string;
  status: LoanStatusName;
  baseCurrency: LoanCurrency;
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
  totalCommitment: number | null;
  totalPrincipalOutstanding: number | null;
  totalInterestAccruedUnpaid: number | null;
  totalPenaltyAccruedUnpaid: number | null;
  totalAvailableToDraw: number | null;
  totalCollateralValue: number | null;
  loans: LoanSummary[];
  /** No FX translation: totals are denominated separately in each retained currency. */
  currencyTotals?: LoanPortfolioCurrencyTotals[] | null;
  /** Legacy scalar totals have a currency only when the complete population has one. */
  totalCurrency?: LoanCurrency | null;
}

export interface LoanPortfolioCurrencyTotals {
  currency: LoanCurrency;
  totalCommitment: number | null;
  totalPrincipalOutstanding: number | null;
  totalInterestAccruedUnpaid: number | null;
  totalPenaltyAccruedUnpaid: number | null;
  totalAvailableToDraw: number | null;
  totalCollateralValue: number | null;
}
