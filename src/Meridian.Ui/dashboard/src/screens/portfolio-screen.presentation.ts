import {
  formatNumber as formatNumberAmount,
  formatRatioAsPercent as formatRatioAsPercentAmount,
  formatPrefixedCurrency,
  formatSignedCurrency as formatSignedCurrencyAmount,
  pluralizeCount
} from "@/lib/format";
import type { TradingWorkspaceResponse } from "@/types";
import type {
  PortfolioDetailField,
  PortfolioPositionDetail,
  PortfolioPositionRow,
  PortfolioRunComparisonCard,
  PortfolioRunDetail,
  PortfolioRunRow
} from "@/screens/portfolio-screen.view-model";

export function pnlTone(value: string): "success" | "danger" | "default" {
  if (value.startsWith("+")) return "success";
  if (value.startsWith("-")) return "danger";
  return "default";
}

export function pnlFieldTone(value: string): PortfolioDetailField["tone"] {
  const tone = pnlTone(value);
  if (tone === "success") return "success";
  if (tone === "danger") return "danger";
  return "default";
}

export function comparisonToneForPnl(value: string): PortfolioRunComparisonCard["tone"] {
  const tone = pnlTone(value);
  if (tone === "success") return "success";
  if (tone === "danger") return "danger";
  return "default";
}

export function runStatusTone(
  status: string,
  pnl: PortfolioRunRow["pnlTone"]
): PortfolioRunDetail["statusTone"] {
  if (status === "Needs Review") return "warning";
  if (status === "Completed") return pnl === "danger" ? "warning" : "success";
  if (status === "Queued" || status === "Running") return "default";
  return pnl === "danger" ? "danger" : "default";
}

export function riskFieldTone(
  state: TradingWorkspaceResponse["risk"]["state"] | undefined
): PortfolioDetailField["tone"] {
  if (state === "Healthy") return "success";
  if (state === "Observe") return "warning";
  if (state === "Constrained") return "danger";
  return "muted";
}

export function riskTone(
  riskState: TradingWorkspaceResponse["risk"]["state"] | undefined,
  pnl: PortfolioPositionRow["pnlTone"]
): PortfolioPositionDetail["statusTone"] {
  if (riskState === "Constrained") return "danger";
  if (riskState === "Observe") return "warning";
  if (pnl === "danger") return "warning";
  if (pnl === "success" || riskState === "Healthy") return "success";
  return "default";
}

export function sumNumericStrings(values: string[]): number {
  return values.reduce((sum, value) => {
    const cleaned = value.replace(/[$+,]/g, "");
    const parsed = parseFloat(cleaned);
    return sum + (Number.isNaN(parsed) ? 0 : parsed);
  }, 0);
}

export function formatCurrency(value: number): string {
  return formatPrefixedCurrency(value, { maximumFractionDigits: 0 });
}

export function formatSignedCurrency(value: number): string {
  return formatSignedCurrencyAmount(value, { maximumFractionDigits: 0 });
}

export function formatCurrencyPrecise(value: number): string {
  return formatPrefixedCurrency(value, { minimumFractionDigits: 2 });
}

/** Input is a fraction of 1 (0.425 -> "42.5%"), not percent units. */
export function formatRatioAsPercent(value: number): string {
  return formatRatioAsPercentAmount(value);
}

export function formatNumber(value: number): string {
  return formatNumberAmount(value, { maximumFractionDigits: 4 });
}

export function formatCountLabel(count: number, noun: string): string {
  return pluralizeCount(count, noun);
}

export function formatDateTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? "—"
    : `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function padUtc(value: number): string {
  return value.toString().padStart(2, "0");
}
