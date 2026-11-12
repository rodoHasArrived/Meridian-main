import type { LoanSummary } from "@/types/direct-lending.types";

export interface LoanMaturityBucket {
  id: string;
  currency: string;
  period: string;
  amount: number | null;
  loans: LoanSummary[];
  missingAmountCount: number;
}

const CURRENCIES = ["USD", "EUR", "GBP", "JPY"];

export function loanMaturityCurrency(value: string | number): string {
  const numeric = typeof value === "number" || /^\d+$/.test(String(value));
  if (numeric) return CURRENCIES[Number(value)] ?? "Unspecified currency";
  const text = String(value ?? "").trim().toUpperCase();
  return CURRENCIES.includes(text) ? text : "Unspecified currency";
}

/** Validate calendar days without applying the workstation's timezone. */
export function isCalendarDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value ?? "")) return false;
  const parsed = new Date(`${value}T00:00:00Z`);
  return Number.isFinite(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}

export function maturityPeriod(date: string, today: string): string {
  if (!isCalendarDate(date)) return "Unknown maturity";
  if (date < today) return "Overdue";
  return `${date.slice(0, 4)} Q${Math.ceil(Number(date.slice(5, 7)) / 3)}`;
}

/** Current retained outstanding, assigned to contractual maturity, never a payment forecast. */
export function buildLoanMaturityBuckets(loans: LoanSummary[], today: string): LoanMaturityBucket[] {
  const buckets = new Map<string, LoanMaturityBucket>();
  for (const loan of loans) {
    const currency = loanMaturityCurrency(loan.baseCurrency);
    const period = maturityPeriod(loan.maturityDate, today);
    const id = `${currency}:${period}`;
    let bucket = buckets.get(id);
    if (!bucket) {
      bucket = { id, currency, period, amount: currency === "Unspecified currency" ? null : 0, loans: [], missingAmountCount: 0 };
      buckets.set(id, bucket);
    }
    bucket.loans.push(loan);
    if (typeof loan.principalOutstanding === "number" && Number.isFinite(loan.principalOutstanding)) {
      if (bucket.amount != null) bucket.amount += loan.principalOutstanding;
    } else {
      bucket.missingAmountCount++;
    }
  }
  const order = (period: string) => period === "Overdue" ? "0" : period === "Unknown maturity" ? "9" : `1${period}`;
  for (const bucket of buckets.values()) if (bucket.missingAmountCount === bucket.loans.length) bucket.amount = null;
  return [...buckets.values()].sort((a, b) => a.currency.localeCompare(b.currency) || order(a.period).localeCompare(order(b.period)));
}
