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
import type { LoanCurrency, LoanStatusName, LoanPortfolioSummary, LoanSummary } from "@/types/direct-lending.types";

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
  currencyNotice: string | null;
  rows: LoanBookRowViewModel[];
  hasLoans: boolean;
  emptyText: string;
  tableAriaLabel: string;
  /** Summary announced to assistive tech when the desk settles. */
  statusAnnouncement: string;
}

const NOT_REPORTED = "—";

/**
 * Statuses that mean the facility needs attention. Normalize numeric host enums and
 * compare named enums case-insensitively across contract revisions.
 */
const IMPAIRED_STATUSES = new Set(["defaulted", "nonperforming", "non-performing", "workout"]);

const STATUS_NAMES = ["Draft", "Approved", "Active", "Suspended", "Matured", "Closed", "Defaulted", "NonPerforming", "Workout"];
const CURRENCY_NAMES = ["USD", "EUR", "GBP", "JPY", "Other"];

function enumLabel(value: string | number | null | undefined, names: string[], fallback: string): string {
  if (typeof value === "number") return names[value] ?? `${fallback} (${value})`;
  const label = typeof value === "string" ? value.trim() : "";
  if (/^\d+$/.test(label)) return names[Number(label)] ?? `${fallback} (${label})`;
  return label || fallback;
}

function currencyLabel(value: LoanCurrency | null | undefined): string {
  return enumLabel(value, CURRENCY_NAMES, "Unknown currency");
}

function normalizeStatus(status: LoanStatusName | null | undefined): string {
  return enumLabel(status, STATUS_NAMES, "Unknown").toLowerCase().replace(/\s+/g, "");
}

export function loanStatusTone(status: LoanStatusName | null | undefined): LoanBookTone {
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
  const currency = currencyLabel(loan.baseCurrency);
  const missing: string[] = [];

  if (!isReportedNumber(loan.commitmentAmount)) missing.push("Commitment");
  if (!isReportedNumber(loan.principalOutstanding)) missing.push("Principal outstanding");
  if (!isReportedNumber(loan.interestAccruedUnpaid)) missing.push("Interest accrued");
  if (!isReportedNumber(loan.availableToDraw)) missing.push("Available to draw");

  const statusLabel = enumLabel(loan.status, STATUS_NAMES, "Unknown");
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

/**
 * Totals the facilities needing attention.
 *
 * The three status counts are non-nullable in the contract, but they reach the browser as
 * unchecked JSON, and adding an absent one yields `NaN` — which would be read out as
 * "NaN needing attention". Absent counts are skipped and the shortfall reported, so the
 * census degrades to "at least N" rather than to a nonsense number.
 */
function impairedCount(summary: LoanPortfolioSummary): { total: number; complete: boolean } {
  const parts = [summary.defaultedLoans, summary.nonPerformingLoans, summary.workoutLoans];
  const reported = parts.filter(isReportedNumber);

  return {
    total: reported.reduce((sum, value) => sum + value, 0),
    complete: reported.length === parts.length
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
      currencyNotice: null,
      rows: [],
      hasLoans: false,
      statusAnnouncement: "Loan book has not loaded."
    };
  }

  // A legacy payload has no currency evidence for its scalar totals. Do not infer it
  // from a subset of rows, or silently label an unconverted mixed sum as USD.
  const totals = summary.currencyTotals ?? (summary.totalCurrency != null ? [{
    ...summary,
    currency: summary.totalCurrency
  }] : []);

  const metrics: LoanBookMetricViewModel[] = [
    countMetric("total-loans", "Facilities", summary.totalLoans, "default"),
    countMetric("active-loans", "Active", summary.activeLoans, "success"),
    countMetric("defaulted-loans", "Defaulted", summary.defaultedLoans, "danger"),
    countMetric("non-performing-loans", "Non-performing", summary.nonPerformingLoans, "danger"),
    countMetric("workout-loans", "Workout", summary.workoutLoans, "warning")
  ];

  for (const total of totals) {
    const currency = currencyLabel(total.currency);
    const fields = [
      ["principal-outstanding", "Principal outstanding", total.totalPrincipalOutstanding],
      ["available-to-draw", "Available to draw", total.totalAvailableToDraw],
      ["collateral-value", "Collateral value", total.totalCollateralValue]
    ] as const;
    for (const [id, label, amount] of fields) {
      const value = /^[a-z]{3}$/i.test(currency)
        ? formatCurrency(amount, { currency, fallback: NOT_REPORTED }) : NOT_REPORTED;
      metrics.push({
        id: totals.length === 1 ? id : `${id}-${currency}`,
        label: `${label} (${currency})`,
        value,
        tone: "default",
        ariaLabel: `${label} (${currency}): ${value}`
      });
    }
  }

  const rows = (summary.loans ?? []).map(buildLoanBookRow);
  const impaired = impairedCount(summary);
  const attention = impaired.complete
    ? `${impaired.total} needing attention.`
    : `at least ${impaired.total} needing attention; some status counts were not reported.`;

  return {
    ...base,
    loaded: true,
    metrics,
    currencyNotice: totals.some(total => !/^[a-z]{3}$/i.test(currencyLabel(total.currency)))
      ? "Totals for unspecified currencies are unavailable. No FX conversion has been applied."
      : totals.length > 1
      ? "Totals are grouped by currency. No FX conversion has been applied."
      : totals.length === 0 && summary.totalLoans > 0
        ? "Portfolio totals are unavailable because the service did not report their currency."
        : null,
    rows,
    hasLoans: rows.length > 0,
    statusAnnouncement:
      rows.length === 0
        ? "Loan book loaded with no facilities."
        : `Loan book loaded with ${rows.length} ${rows.length === 1 ? "facility" : "facilities"}, ${attention}`
  };
}
