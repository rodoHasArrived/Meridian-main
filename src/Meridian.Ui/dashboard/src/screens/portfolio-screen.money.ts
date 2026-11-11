import { formatNumber } from "@/lib/format";
import type { BrokerageHouseholdPortfolio } from "@/types";

export function normalizedPortfolioCurrency(currency: string | null | undefined): string | null {
  const code = currency?.trim().toUpperCase();
  return code && /^[A-Z]{3}$/.test(code) && code !== "XXX" ? code : null;
}

/** Native observations keep their own denomination; no implicit conversion or USD fallback. */
export function formatBrokerageMoney(
  value: number,
  currency: string | null | undefined,
  { precise = false, signed = false }: { precise?: boolean; signed?: boolean } = {}
): string {
  if (!Number.isFinite(value)) return "—";
  const code = normalizedPortfolioCurrency(currency);
  const digits = precise ? { minimumFractionDigits: 2, maximumFractionDigits: 6 } : { minimumFractionDigits: 0, maximumFractionDigits: 2 };
  if (!code) return `${signed && value >= 0 ? "+" : ""}${formatNumber(value, digits)} (currency unavailable)`;
  const formatted = new Intl.NumberFormat("en-US", {
    style: "currency", currency: code, currencyDisplay: code === "USD" ? "symbol" : "code",
    ...digits, ...(signed ? { signDisplay: "always" as const } : {})
  }).format(value);
  return formatted.replace(/\u00a0/g, " ");
}

/** The current household contract has no FX normalization proof. Never combine unlike native balances. */
export function formatBrokerageHouseholdMoney(
  portfolio: BrokerageHouseholdPortfolio | null | undefined,
  key: "totalEquity" | "totalCash" | "totalBuyingPower"
): string {
  if (!portfolio) return "—";
  const currency = normalizedPortfolioCurrency(portfolio.currency);
  if (!hasComparableHouseholdCurrency(portfolio)) {
    return "Not comparable — inspect account currencies";
  }
  return formatBrokerageMoney(portfolio[key], currency);
}

export function hasComparableHouseholdCurrency(portfolio: BrokerageHouseholdPortfolio): boolean {
  const currency = normalizedPortfolioCurrency(portfolio.currency);
  return currency !== null && portfolio.accounts.every(account => normalizedPortfolioCurrency(account.currency) === currency);
}
