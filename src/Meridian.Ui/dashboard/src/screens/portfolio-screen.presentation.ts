import { formatMoney, formatNumber as formatNumberAmount, formatRatioAsPercent as formatRatioAsPercentAmount, pluralizeCount, formatTimestamp } from "@/lib/format";
import type { BrokerageHouseholdPosition, TradingWorkspaceResponse } from "@/types";
import type {
  PortfolioBrokerageAccountOption,
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

export function numericPnlTone(value: number): "success" | "danger" | "default" {
  if (value > 0) return "success";
  if (value < 0) return "danger";
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

/** Input is a fraction of 1 (0.425 -> "42.5%"), not percent units. */
export function formatRatioAsPercent(value: number): string {
  return formatRatioAsPercentAmount(value);
}

export function formatNumber(value: number): string {
  return formatNumberAmount(value, { maximumFractionDigits: 4 });
}

export function formatBrokeragePositionAmounts(position: BrokerageHouseholdPosition) {
  function money(field: string, value: number, decimals: number, signed = false) {
    const proof = position.amountPassports?.[field];
    return proof?.origin === "unavailable" ? "—" : formatMoney(proof?.exactValue ?? value, {
      currency: position.currency?.trim() || null, decimals, signed
    });
  }
  return {
    quantity: position.amountPassports?.quantity?.origin === "unavailable" ? "—" : formatNumber(position.quantity),
    averagePrice: money("average-price", position.averageEntryPrice, 2),
    markPrice: money("mark", position.marketPrice, 2),
    marketValue: money("market-value", position.marketValue, 0),
    unrealizedPnl: money("unrealized-pnl", position.unrealizedPnl, 0, true)
  };
}

export function formatCountLabel(count: number, noun: string): string {
  return pluralizeCount(count, noun);
}

export function formatDateTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? "—"
    : formatTimestamp(date);
}

export function resolveBrokerageAccountFilterKeyCommand(
  key: string
): "next" | "previous" | "first" | "last" | null {
  if (key === "ArrowRight" || key === "ArrowDown") {
    return "next";
  }

  if (key === "ArrowLeft" || key === "ArrowUp") {
    return "previous";
  }

  if (key === "Home") {
    return "first";
  }

  if (key === "End") {
    return "last";
  }

  return null;
}

export function nextBrokerageAccountKey(
  options: PortfolioBrokerageAccountOption[],
  selectedKey: string,
  direction: "next" | "previous" | "first" | "last"
): string {
  if (options.length === 0) {
    return selectedKey;
  }

  if (direction === "first") {
    return options[0].key;
  }

  if (direction === "last") {
    return options[options.length - 1].key;
  }

  const selectedIndex = Math.max(0, options.findIndex((option) => option.key === selectedKey));
  const offset = direction === "next" ? 1 : -1;
  const nextIndex = (selectedIndex + offset + options.length) % options.length;
  return options[nextIndex].key;
}
