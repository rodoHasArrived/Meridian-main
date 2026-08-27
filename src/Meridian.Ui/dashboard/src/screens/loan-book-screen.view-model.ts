/**
 * Presentation logic for the direct-lending loan book.
 *
 * `/api/loans/portfolio` has been served all along and the browser workstation never
 * called it, so the loan book was visible only from the desktop client. This builds the
 * read surface: portfolio totals, a status census, and one row per facility.
 *
 * Two rules run through the whole file, because this is lending data an operator makes
 * money decisions on:
 *
 *  - A figure the server did not send is never rendered as a number. `formatCurrency`
 *    and `formatNumber` fall back to a dash, and the row records which fields were
 *    missing so the desk can say so rather than implying a zero balance.
 *  - Dates arrive as `DateOnly` strings and are passed through, not parsed. Re-parsing a
 *    bare date in a browser applies the local timezone and can shift the day, which on a
 *    maturity or accrual date is a wrong fact rather than a cosmetic one.
 */

import { formatCurrency, formatNumber } from "@/lib/format";
import type { LoanPortfolioSummary, LoanSummary } from "@/types/direct-lending.types";

export type LoanBookTone = "default" | "success" | "warning" | "danger";

export interface LoanBookMetricViewModel {
  id: string;
  label: string;
  value: string;
  tone: LoanBookTone;
  ariaLabel: string;
}

export interface LoanBookRowViewModel {
  loanId: string;
  facilityName: string;
  borrowerName: string;
  statusLabel: string;
  statusTone: LoanBookTone;
  currency: string;
  commitmentLabel: string;
  principalOutstandingLabel: string;
  interestAccruedLabel: string;
  availableToDrawLabel: string;
  maturityLabel: string;
  lastPaymentLabel: string;
  /** Fields the server did not report, so the desk can name them instead of showing a zero. */
  missing: string[];
  ariaLabel: string;
}

export interface LoanBookViewModel {
  title: string;
  description: string;
  loaded: boolean;
  metrics: LoanBookMetricViewModel[];
  rows: LoanBookRowViewModel[];
  hasLoans: boolean;
  emptyText: string;
  tableAriaLabel: string;
  /** Summary announced to assistive tech when the desk settles. */
  statusAnnouncement: string;
}

const NOT_REPORTED = "—";

/**
 * Statuses that mean the facility needs attention. Compared case-insensitively because the
 * server serializes the enum by name and casing has varied across contract revisions.
 */
const IMPAIRED_STATUSES = new Set(["defaulted", "nonperforming", "non-performing", "workout"]);

function normalizeStatus(status: string | null | undefined): string {
  return (status ?? "").trim().toLowerCase().replace(/\s+/g, "");
}

export function loanStatusTone(status: string | null | undefined): LoanBookTone {
  const normalized = normalizeStatus(status);
  if (!normalized) {
    return "default";
  }

  if (IMPAIRED_STATUSES.has(normalized)) {
    return "danger";
  }

  return normalized === "active" ? "success" : "default";
}

function isReportedNumber(value: number | null | undefined): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

/** Renders a date exactly as the server sent it; a blank or absent value reads as not reported. */
function dateLabel(value: string | null | undefined): string {
  const trimmed = (value ?? "").trim();
  return trimmed === "" ? NOT_REPORTED : trimmed;
}

export function buildLoanBookRow(loan: LoanSummary): LoanBookRowViewModel {
  const currency = (loan.baseCurrency ?? "").trim() || "USD";
  const missing: string[] = [];

  if (!isReportedNumber(loan.commitmentAmount)) missing.push("Commitment");
  if (!isReportedNumber(loan.principalOutstanding)) missing.push("Principal outstanding");
  if (!isReportedNumber(loan.interestAccruedUnpaid)) missing.push("Interest accrued");
  if (!isReportedNumber(loan.availableToDraw)) missing.push("Available to draw");

  const statusLabel = (loan.status ?? "").trim() || "Unknown";
  const facilityName = (loan.facilityName ?? "").trim() || "Unnamed facility";
  const borrowerName = (loan.borrowerName ?? "").trim() || "Unknown borrower";

  const money = (value: number | null | undefined) =>
    formatCurrency(value, { currency, fallback: NOT_REPORTED });

  const ariaParts = [
    facilityName,
    `borrower ${borrowerName}`,
    `status ${statusLabel}`,
    `principal outstanding ${money(loan.principalOutstanding)}`,
    `matures ${dateLabel(loan.maturityDate)}`
  ];

  if (missing.length > 0) {
    ariaParts.push(`${missing.join(", ").toLowerCase()} not reported`);
  }

  return {
    loanId: loan.loanId,
    facilityName,
    borrowerName,
    statusLabel,
    statusTone: loanStatusTone(loan.status),
    currency,
    commitmentLabel: money(loan.commitmentAmount),
    principalOutstandingLabel: money(loan.principalOutstanding),
    interestAccruedLabel: money(loan.interestAccruedUnpaid),
    availableToDrawLabel: money(loan.availableToDraw),
    maturityLabel: dateLabel(loan.maturityDate),
    lastPaymentLabel: dateLabel(loan.lastPaymentDate),
    missing,
    ariaLabel: ariaParts.join(", ")
  };
}

function countMetric(id: string, label: string, value: number, tone: LoanBookTone): LoanBookMetricViewModel {
  const rendered = formatNumber(value, { maximumFractionDigits: 0, fallback: NOT_REPORTED });
  return {
    id,
    label,
    value: rendered,
    tone: value > 0 ? tone : "default",
    ariaLabel: `${label}: ${rendered}`
  };
}

export function buildLoanBookViewModel(summary: LoanPortfolioSummary | null): LoanBookViewModel {
  const base = {
    title: "Loan book",
    description:
      "Direct-lending facilities, outstanding principal, accrued interest, and undrawn commitment.",
    tableAriaLabel: "Direct lending facilities",
    emptyText:
      "No direct-lending facilities are recorded. Facilities appear here once loans are originated in the lending service."
  };

  if (!summary) {
    return {
      ...base,
      loaded: false,
      metrics: [],
      rows: [],
      hasLoans: false,
      statusAnnouncement: "Loan book has not loaded."
    };
  }

  const money = (value: number | null | undefined) =>
    formatCurrency(value, { currency: "USD", fallback: NOT_REPORTED });

  const metrics: LoanBookMetricViewModel[] = [
    countMetric("total-loans", "Facilities", summary.totalLoans, "default"),
    countMetric("active-loans", "Active", summary.activeLoans, "success"),
    countMetric("defaulted-loans", "Defaulted", summary.defaultedLoans, "danger"),
    countMetric("non-performing-loans", "Non-performing", summary.nonPerformingLoans, "danger"),
    countMetric("workout-loans", "Workout", summary.workoutLoans, "warning"),
    {
      id: "principal-outstanding",
      label: "Principal outstanding",
      value: money(summary.totalPrincipalOutstanding),
      tone: "default",
      ariaLabel: `Principal outstanding: ${money(summary.totalPrincipalOutstanding)}`
    },
    {
      id: "available-to-draw",
      label: "Available to draw",
      value: money(summary.totalAvailableToDraw),
      tone: "default",
      ariaLabel: `Available to draw: ${money(summary.totalAvailableToDraw)}`
    },
    {
      id: "collateral-value",
      label: "Collateral value",
      value: money(summary.totalCollateralValue),
      tone: "default",
      ariaLabel: `Collateral value: ${money(summary.totalCollateralValue)}`
    }
  ];

  const rows = (summary.loans ?? []).map(buildLoanBookRow);
  const impaired = summary.defaultedLoans + summary.nonPerformingLoans + summary.workoutLoans;

  return {
    ...base,
    loaded: true,
    metrics,
    rows,
    hasLoans: rows.length > 0,
    statusAnnouncement:
      rows.length === 0
        ? "Loan book loaded with no facilities."
        : `Loan book loaded with ${rows.length} ${rows.length === 1 ? "facility" : "facilities"}, ${impaired} needing attention.`
  };
}
