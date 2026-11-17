// Prior-period comparisons for NAV, run attribution and the cash ladder (UX Phase E). All three return
// one shape (PeriodComparisonDto in Meridian.Contracts): a status, the two windows compared, lines with
// current, prior, change and change %, and notes on what the figures include. Amounts arrive as JSON
// numbers or decimal strings; they are kept as decimal text so the variance columns format them exactly.
import { getJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

export type PeriodComparisonStatus = "Ready" | "Unavailable" | "Incompatible";

export interface PeriodComparisonWindow {
  label: string;
  start: string | null;
  end: string;
  isPartial: boolean;
}

export interface PeriodComparisonLine {
  key: string;
  label: string;
  current: string;
  prior: string;
  change: string;
  percentChange: string | null;
  isTotal: boolean;
}

export interface PeriodComparison {
  status: PeriodComparisonStatus;
  reason: string | null;
  subject: string;
  currency: string | null;
  current: PeriodComparisonWindow;
  baseline: PeriodComparisonWindow | null;
  lines: PeriodComparisonLine[];
  notes: string[];
  generatedAt: string;
}

type RawAmount = number | string;
interface RawLine extends Omit<PeriodComparisonLine, "current" | "prior" | "change" | "percentChange" | "isTotal"> {
  current: RawAmount; prior: RawAmount; change: RawAmount; percentChange: RawAmount | null; isTotal?: boolean;
}
type RawComparison = Omit<PeriodComparison, "lines"> & { lines: RawLine[] };

function amountText(value: RawAmount | null | undefined, field: string): string {
  if (typeof value === "number" && Number.isFinite(value)) return String(value);
  if (typeof value === "string" && /^-?\d+(?:\.\d+)?$/.test(value.trim())) return value.trim();
  throw new Error(`The comparison response has an invalid ${field} amount.`);
}

/** Checks the response shape and normalises amounts to decimal text. */
export function normalizePeriodComparison(raw: RawComparison | null | undefined): PeriodComparison {
  if (!raw || !["Ready", "Unavailable", "Incompatible"].includes(raw.status) || !raw.current || !Array.isArray(raw.lines) || !Array.isArray(raw.notes)) {
    throw new Error("The comparison response is not a recognised prior-period comparison.");
  }
  return {
    ...raw,
    lines: raw.lines.map((line) => ({
      key: line.key,
      label: line.label,
      current: amountText(line.current, "current"),
      prior: amountText(line.prior, "prior"),
      change: amountText(line.change, "change"),
      percentChange: line.percentChange === null || line.percentChange === undefined ? null : amountText(line.percentChange, "percent"),
      isTotal: Boolean(line.isTotal)
    }))
  };
}

function withQuery(path: string, query: Record<string, string | null | undefined>): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) if (value) params.set(key, value);
  const suffix = params.toString();
  return suffix ? `${path}?${suffix}` : path;
}

/** Ledger NAV now against NAV at a prior date (default: the end of the previous month). */
export async function getNavComparison(query: { fundProfileId: string; asOf?: string | null; baselineAsOf?: string | null; currency?: string | null }, options: ApiRequestOptions = {}) {
  const raw = await getJson<RawComparison>(withQuery(UI_API_ROUTES.FundStructureNavComparison, query), { ...options, allowDevelopmentFallback: false });
  return normalizePeriodComparison(raw);
}

/** A run's per-symbol P&L for the month containing `periodEnd` (default: its last day) against the month before. */
export async function getRunAttributionPeriodComparison(runId: string, periodEnd?: string | null, options: ApiRequestOptions = {}) {
  const path = UI_API_ROUTES.RunsAttributionPeriodComparison.replace("{runId}", encodeURIComponent(runId));
  const raw = await getJson<RawComparison>(withQuery(path, { periodEnd }), { ...options, allowDevelopmentFallback: false });
  return normalizePeriodComparison(raw);
}

/** Today's cash ladder against the ladder rebuilt as of a prior date (default: the end of the previous month). */
export async function getCashLadderComparison(query: { scenario?: string | null; horizonDays?: number; bucketDays?: number; minimumCash?: number | null; fundAccountId?: string | null; baselineAsOf?: string | null }, options: ApiRequestOptions = {}) {
  const raw = await getJson<RawComparison>(withQuery(UI_API_ROUTES.PortfolioCashLadderComparison, {
    scenario: query.scenario,
    horizonDays: query.horizonDays === undefined ? null : String(query.horizonDays),
    bucketDays: query.bucketDays === undefined ? null : String(query.bucketDays),
    minimumCash: query.minimumCash === undefined || query.minimumCash === null ? null : String(query.minimumCash),
    fundAccountId: query.fundAccountId,
    baselineAsOf: query.baselineAsOf
  }), { ...options, allowDevelopmentFallback: false });
  return normalizePeriodComparison(raw);
}

export interface FundNavTrendPoint {
  asOf: string;
  /** Ledger NAV at the end of that day, as decimal text. */
  totalNav: string;
  componentCount: number;
}

export interface FundNavTrend {
  fundProfileId: string;
  subject: string;
  currency: string;
  points: FundNavTrendPoint[];
  notes: string[];
  generatedAt: string;
}

type RawNavTrend = Omit<FundNavTrend, "points"> & { points: Array<Omit<FundNavTrendPoint, "totalNav"> & { totalNav: RawAmount }> };

/** Ledger NAV at each recent month-end, ending with NAV now. */
export async function getNavTrend(query: { fundProfileId: string; months?: number }, options: ApiRequestOptions = {}): Promise<FundNavTrend> {
  const raw = await getJson<RawNavTrend>(withQuery(UI_API_ROUTES.FundStructureNavTrend, {
    fundProfileId: query.fundProfileId,
    months: query.months === undefined ? null : String(query.months)
  }), { ...options, allowDevelopmentFallback: false });
  if (!raw || !Array.isArray(raw.points) || !Array.isArray(raw.notes)) throw new Error("The NAV trend response is not recognised.");
  return {
    ...raw,
    points: raw.points.map((point) => ({ asOf: point.asOf, componentCount: point.componentCount, totalNav: amountText(point.totalNav, "NAV") }))
  };
}
