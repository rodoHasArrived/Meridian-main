import type { IncomeComparison, IncomeContribution } from "@/types/reporting-income-comparison";

export interface IncomeWaterfallStage {
  label: string;
  amount: string;
  startPct: number;
  endPct: number;
  direction: "total" | "increase" | "decrease" | "zero";
  contributions: IncomeContribution[];
}

export interface IncomeWaterfall {
  reason: string | null;
  stages: IncomeWaterfallStage[];
  zeroPct: number;
}

/** Group digits directly; neither retained amounts nor sums pass through floating point. */
export function formatIncomeAmount(value: string | null, currency: string, signed = false) {
  if (value === null) return "Not comparable";
  const negative = value.startsWith("-");
  const [integer, retainedFraction = ""] = (negative ? value.slice(1) : value).split(".");
  const zero = /^0(?:\.0+)?$/.test(negative ? value.slice(1) : value);
  const fraction = retainedFraction.replace(/0+$/, "").padEnd(2, "0");
  return `${negative && !zero ? "-" : signed && !zero ? "+" : ""}${integer.replace(/\B(?=(\d{3})+(?!\d))/g, ",")}.${fraction} ${currency}`;
}

function decimalText(value: bigint, scale: number) {
  const sign = value < 0n ? "-" : "";
  const digits = (value < 0n ? -value : value).toString().padStart(scale + 1, "0");
  return scale ? `${sign}${digits.slice(0, -scale)}.${digits.slice(-scale)}` : `${sign}${digits}`;
}

/**
 * Plot retained attribution, never derive it from live journals. Bigint arithmetic verifies
 * the bridge exactly; conversion to number happens only for bounded drawing coordinates.
 * Unexplained records describe the residual and must not be added to it a second time.
 */
export function buildIncomeWaterfall(c: IncomeComparison): IncomeWaterfall {
  const unavailable = (reason: string): IncomeWaterfall => ({ reason, stages: [], zeroPct: 0 });
  if (!c.compatible || c.baseline.currency !== c.current.currency || c.differences.some((item) => !item.compatible)) {
    return unavailable("The retained inputs are incompatible. Review the comparison dimensions before interpreting movement.");
  }
  const values = [c.baselineAmount, c.currentAmount, c.movement, c.explainedAmount, c.residualAmount, ...c.contributions.map((item) => item.amount)];
  if (values.some((value) => typeof value !== "string" || !/^-?(?:0|[1-9]\d*)(?:\.\d+)?$/.test(value))) {
    return unavailable("The retained bridge has missing or invalid amounts. Exact records remain available below.");
  }
  if (c.contributions.some((item) => !["Journal", "Population", "Methodology", "Unexplained"].includes(item.kind))) {
    return unavailable("The retained explanation contains an unsupported contribution category. Review its records before interpreting movement.");
  }
  const texts = values as string[];
  const scale = Math.max(...texts.map((value) => value.split(".")[1]?.length ?? 0));
  const units = (value: string) => {
    const [integer, fraction = ""] = value.split(".");
    return BigInt(`${integer}${fraction.padEnd(scale, "0")}`);
  };
  const [baseline, current, movement, explained, residual] = texts.slice(0, 5).map(units);
  const groups = ["Journal", "Population", "Methodology"].map((kind) => {
    const contributions = c.contributions.filter((item) => item.kind === kind);
    return { kind, contributions, value: contributions.reduce((sum, item) => sum + units(item.amount), 0n) };
  });
  if (current - baseline !== movement || explained + residual !== movement || groups.reduce((sum, group) => sum + group.value, 0n) !== explained) {
    return unavailable("The retained amounts do not form a consistent bridge. Review the exact totals and supporting records below.");
  }
  const raw = [{ label: "Baseline", amount: c.baselineAmount!, from: 0n, to: baseline, total: true, contributions: [] as IncomeContribution[] }];
  let running = baseline;
  for (const group of groups) {
    raw.push({ label: `${group.kind} changes`, amount: decimalText(group.value, scale), from: running, to: running + group.value, total: false, contributions: group.contributions });
    running += group.value;
  }
  raw.push({ label: "Unexplained difference", amount: c.residualAmount!, from: running, to: current, total: false, contributions: c.contributions.filter((item) => item.kind === "Unexplained") });
  raw.push({ label: "Current", amount: c.currentAmount!, from: 0n, to: current, total: true, contributions: [] });
  const points = raw.flatMap((item) => [item.from, item.to]);
  const low = points.reduce((min, value) => value < min ? value : min, 0n);
  const high = points.reduce((max, value) => value > max ? value : max, 0n);
  const span = high - low || 1n;
  // At most 1,000,000 before Number conversion, independent of retained amount size.
  const percent = (value: bigint) => Number(((value - low) * 1_000_000n) / span) / 10_000;
  return {
    reason: null,
    zeroPct: percent(0n),
    stages: raw.map((item) => ({ label: item.label, amount: item.amount, startPct: percent(item.from), endPct: percent(item.to), direction: item.total ? "total" : item.to > item.from ? "increase" : item.to < item.from ? "decrease" : "zero", contributions: item.contributions }))
  };
}
