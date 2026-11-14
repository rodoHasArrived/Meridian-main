import type {
  AccountingWorkspaceResponse,
  DataWorkspaceResponse,
  FinancialRecordExplorerDto,
  MetricSnapshot,
  PortfolioWorkspaceResponse,
  StrategyWorkspaceResponse,
  SystemOverviewResponse,
  TradingWorkspaceResponse
} from "@/types";

/** Read models are inputs, never competing fallbacks for the same figure. */
export interface KpiReadModels {
  portfolio?: PortfolioWorkspaceResponse | null;
  portfolioExplorer?: FinancialRecordExplorerDto | null;
  trading?: TradingWorkspaceResponse | null;
  overview?: SystemOverviewResponse | null;
  strategy?: StrategyWorkspaceResponse | null;
  data?: DataWorkspaceResponse | null;
  accounting?: AccountingWorkspaceResponse | null;
}

export type KpiScope = Readonly<Record<string, string | null | undefined>>;
export type KpiStatus = "available" | "empty" | "unavailable";
type Owner = keyof KpiReadModels;
type Tone = MetricSnapshot["tone"];

interface ComputedKpi {
  value: string;
  delta: string;
  tone: Tone;
  status: KpiStatus;
  raw: string | number | null;
  currency: string | null;
}

export interface KpiDefinition {
  readonly label: string;
  readonly owner: Owner;
  readonly computation: string;
  readonly scopeDimensions: readonly string[];
  readonly currencyRule: string;
  readonly compute: (models: KpiReadModels) => ComputedKpi;
}

const unavailable = (reason = "Owning read model unavailable"): ComputedKpi => ({
  value: "Unavailable", delta: reason, tone: "warning", status: "unavailable", raw: null, currency: null
});
const scalar = (value: string | number, tone: Tone = "default", delta = ""): ComputedKpi => ({
  value: String(value), delta, tone, status: "available", raw: value, currency: null
});

interface Money { units: bigint; scale: number; currency: string }
const symbolCurrencies: Record<string, string> = { "$": "USD", "€": "EUR", "£": "GBP" };
const currencySymbols: Record<string, string> = { USD: "$", EUR: "€", GBP: "£" };

/** The workstation's dollar-formatted API contract denotes USD. Bare numbers do not. */
export function parseKpiMoney(value: string): Money | null {
  let text = value?.trim().replace(/−/g, "-");
  if (!text) return null;
  const parentheses = text.startsWith("(") && text.endsWith(")");
  if (parentheses) text = text.slice(1, -1).trim();
  const match = /^([+-]?)\s*(USD|EUR|GBP|CAD|AUD|CHF|JPY|NZD|HKD|SGD|CNY|SEK|NOK|DKK|INR|BRL|ZAR|\$|€|£)\s*([+-]?)\s*((?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d{1,18})?)$/.exec(text);
  if (!match || (match[1] && match[3]) || (parentheses && (match[1] || match[3]))) return null;
  const currency = symbolCurrencies[match[2]] ?? match[2];
  const [whole, fraction = ""] = match[4].replace(/,/g, "").split(".");
  const negative = parentheses || (match[1] || match[3]) === "-";
  return { units: BigInt(`${whole}${fraction}`) * (negative ? -1n : 1n), scale: fraction.length, currency };
}

function decimalText({ units, scale }: Money): string {
  const negative = units < 0n;
  const digits = (negative ? -units : units).toString().padStart(scale + 1, "0");
  const whole = scale ? digits.slice(0, -scale) : digits;
  const fraction = scale ? digits.slice(-scale).replace(/0+$/, "") : "";
  return `${negative ? "-" : ""}${whole}${fraction ? `.${fraction}` : ""}`;
}

function moneyResult(money: Money, signed = false, delta = "Live holdings · all accounts"): ComputedKpi {
  const decimal = decimalText(money);
  const negative = decimal.startsWith("-");
  const [whole, fraction] = decimal.replace(/^-/, "").split(".");
  const amount = `${whole.replace(/\B(?=(\d{3})+(?!\d))/g, ",")}${fraction ? `.${fraction.padEnd(2, "0")}` : ""}`;
  const prefix = currencySymbols[money.currency] ?? `${money.currency} `;
  return {
    value: `${negative ? "-" : signed ? "+" : ""}${prefix}${amount}`,
    delta,
    tone: signed ? negative ? "danger" : "success" : "default",
    status: "available",
    raw: decimal,
    currency: money.currency
  };
}

function sumPositions(models: KpiReadModels, field: "exposure" | "unrealizedPnl"): ComputedKpi {
  const portfolio = models.portfolio;
  if (!Array.isArray(portfolio?.positions)) return unavailable();
  if (portfolio.positions.length === 0) return {
    ...unavailable("No holdings · currency not supplied"), value: "—", status: "empty", tone: "default"
  };
  const values = portfolio.positions.map((position) => parseKpiMoney(position[field]));
  if (values.some((value) => !value)) return unavailable("Currency or value not supplied for every holding");
  const parsed = values as Money[];
  if (new Set(parsed.map((value) => value.currency)).size !== 1) return unavailable("Mixed currencies · no FX conversion supplied");
  const scale = Math.max(...parsed.map((value) => value.scale));
  const units = parsed.reduce((total, value) => {
    const amount = field === "exposure" && value.units < 0n ? -value.units : value.units;
    return total + amount * 10n ** BigInt(scale - value.scale);
  }, 0n);
  return moneyResult({ units, scale, currency: parsed[0].currency }, field === "unrealizedPnl");
}

function sourceMetric(owner: "trading" | "strategy" | "data" | "accounting" | "portfolio", label: string, aliases: readonly string[], currency: boolean | "compact-usd" = false): KpiDefinition {
  return {
    label, owner,
    computation: `Read the owner's metric ${aliases.join(" / ")} without page-local recalculation.`,
    scopeDimensions: ["source", "account"],
    currencyRule: currency === "compact-usd" ? "Owner supplies a rounded compact USD estimate ($, k, M); retain its display and USD denomination without treating it as an exact amount." : currency ? "Preserve the owner's explicit denomination and display; never convert or infer a bare amount." : "Not a monetary figure.",
    compute: (models) => {
      const metrics = models[owner]?.metrics;
      if (!metrics) return unavailable();
      const matches = metrics.filter((metric) => aliases.includes(metric.id));
      if (matches.length === 0) return unavailable("Figure not supplied by owning read model");
      if (new Set(matches.map((metric) => metric.value)).size > 1) return unavailable("Conflicting values in owning read model");
      const metric = matches[0];
      if (!metric.value?.trim() || ["—", "Unavailable"].includes(metric.value.trim())) return unavailable(metric.delta || "Value not supplied");
      const money = currency === true ? parseKpiMoney(metric.value) : null;
      if (currency === true && !money) return unavailable("Currency or value not supplied");
      if (currency === "compact-usd" && !/^\$\d+(?:\.\d+)?[kM]?$/.test(metric.value.trim())) return unavailable("Compact USD estimate not supplied");
      return { value: metric.value, delta: metric.delta, tone: metric.tone, status: "available", raw: money ? decimalText(money) : metric.value, currency: currency === "compact-usd" ? "USD" : money?.currency ?? null };
    }
  };
}

function retainedSummary(label: string, aliases: readonly string[], currency = false): KpiDefinition {
  return {
    label, owner: "portfolioExplorer", computation: `Read retained explorer summary ${aliases.join(" / ")}; never substitute live holdings.`,
    scopeDimensions: ["source", "account", "run", "asOf", "recordSource"],
    currencyRule: currency ? "Retain the summary's explicit currency; market value is not exposure." : "Not a monetary figure.",
    compute: ({ portfolioExplorer }) => {
      if (!portfolioExplorer) return unavailable();
      if (portfolioExplorer.isBlocked) return unavailable(portfolioExplorer.blockedReason || "Retained records blocked");
      if (retainedScopeFields.some((names) => readRetainedScopeField(portfolioExplorer, names).ambiguous)) return unavailable("Retained source scope is ambiguous");
      const summary = portfolioExplorer.summaryItems.filter((item) => aliases.includes(item.label.trim().toLowerCase()));
      if (summary.length !== 1) return unavailable("Retained summary unavailable or ambiguous");
      const item = summary[0];
      const money = currency ? parseKpiMoney(item.value) : null;
      if (currency && !money) return unavailable("Retained currency or value not supplied");
      return { ...scalar(item.value, "default", "Retained run records"), raw: money ? decimalText(money) : item.value, currency: money?.currency ?? null };
    }
  };
}

const holdingDimensions = ["source", "account"] as const;
const holdingCurrencyRule = "Sum only explicitly denominated amounts in one currency, exactly as decimals; no FX conversion, no missing-value or partial-sum fallback.";

export const KPI_REGISTRY = {
  "portfolio.total-exposure": {
    label: "Total exposure", owner: "portfolio", computation: "Sum the absolute exposure of every live holding; short positions contribute to gross exposure.",
    scopeDimensions: holdingDimensions, currencyRule: holdingCurrencyRule, compute: (models: KpiReadModels) => sumPositions(models, "exposure")
  },
  "portfolio.unrealized-pnl": {
    label: "Unrealized P&L", owner: "portfolio", computation: "Sum signed unrealized P&L for every live holding.",
    scopeDimensions: holdingDimensions, currencyRule: holdingCurrencyRule, compute: (models: KpiReadModels) => sumPositions(models, "unrealizedPnl")
  },
  "portfolio.open-positions": {
    label: "Open positions", owner: "portfolio", computation: "Count the positions supplied by the live Portfolio read model; never use the system overview count.",
    scopeDimensions: holdingDimensions, currencyRule: "Not a monetary figure.",
    compute: ({ portfolio }: KpiReadModels) => Array.isArray(portfolio?.positions) ? scalar(portfolio.positions.length, portfolio.positions.length ? "success" : "default", "Live holdings · all accounts") : unavailable()
  },
  "portfolio.cash": sourceMetric("portfolio", "Cash", ["portfolio-cash", "cash"], true),
  "portfolio.retained-market-value": retainedSummary("Market value", ["market value"], true),
  "portfolio.retained-records": retainedSummary("Retained positions", ["positions", "position records"]),
  "trading.net-pnl": sourceMetric("trading", "Net P&L", ["trading-net-pnl", "pnl"], true),
  "trading.open-orders": sourceMetric("trading", "Open orders", ["trading-open-orders", "orders"]),
  "trading.cash": sourceMetric("trading", "Cash", ["trading-cash"], true),
  "trading.portfolio-value": sourceMetric("trading", "Portfolio value", ["trading-portfolio-value"], true),
  "trading.fills": sourceMetric("trading", "Fills", ["fills"]),
  "trading.risk": sourceMetric("trading", "Risk", ["risk"]),
  "strategy.active-runs": {
    ...sourceMetric("strategy", "Active recent runs", ["active-runs"]),
    computation: "Read active-runs from the Strategy workspace's recent retained run population (currently up to six records), not the system-wide active-run count."
  },
  "strategy.runs": sourceMetric("strategy", "Runs", ["runs"]),
  "strategy.queued-runs": sourceMetric("strategy", "Queued promotions", ["queued-runs"]),
  "strategy.queued": sourceMetric("strategy", "Queued", ["queued"]),
  "strategy.review-runs": sourceMetric("strategy", "Needs review", ["review-runs", "review"]),
  "strategy.winning-runs": sourceMetric("strategy", "Positive P&L", ["winning-runs"]),
  "strategy.promotions": sourceMetric("strategy", "Promotions", ["promotions"]),
  "data.providers-healthy": sourceMetric("data", "Providers healthy", ["providers-healthy", "providers"]),
  "data.backfills-running": sourceMetric("data", "Backfills running", ["backfills-running", "backfills"]),
  "data.exports-ready": sourceMetric("data", "Exports ready", ["exports-ready", "exports"]),
  "data.ops-review": sourceMetric("data", "Needs review", ["ops-review", "review"]),
  "data.kernel-critical-jumps": sourceMetric("data", "Kernel jump alerts", ["kernel-critical-jumps"]),
  "data.replacement-cost": sourceMetric("data", "Est. replacement cost", ["data-replacement-cost"], "compact-usd"),
  "accounting.open-breaks": sourceMetric("accounting", "Open breaks", ["open-breaks", "breaks"]),
  "accounting.timing-drift": sourceMetric("accounting", "Timing drift", ["timing-drift", "drift"]),
  "accounting.security-gaps": sourceMetric("accounting", "Security gaps", ["security-gaps", "coverage"]),
  "accounting.audit-ready": sourceMetric("accounting", "Audit ready", ["audit-ready", "audit"]),
  "accounting.kernel-critical-jumps": sourceMetric("accounting", "Kernel jump alerts", ["kernel-critical-jumps"]),
  "system.providers-online": {
    label: "Providers online", owner: "overview", computation: "Read providersOnline / providersTotal from system overview.",
    scopeDimensions: ["source", "account"], currencyRule: "Not a monetary figure.",
    compute: ({ overview }: KpiReadModels) => overview ? scalar(`${overview.providersOnline} / ${overview.providersTotal}`, overview.providersOnline === overview.providersTotal ? "success" : "warning") : unavailable()
  },
  "system.active-runs": {
    label: "Active runs", owner: "overview", computation: "Read the system overview's activeRuns; this is the system population, not the Strategy workspace's retained runs.",
    scopeDimensions: ["source", "account"], currencyRule: "Not a monetary figure.",
    compute: ({ overview }: KpiReadModels) => overview ? scalar(overview.activeRuns) : unavailable()
  },
  "system.storage-health": {
    label: "Storage health", owner: "overview", computation: "Read storageHealth from system overview.",
    scopeDimensions: ["source", "account"], currencyRule: "Not a monetary figure.",
    compute: ({ overview }: KpiReadModels) => overview ? scalar(overview.storageHealth, overview.storageHealth === "Healthy" ? "success" : overview.storageHealth === "Critical" ? "danger" : "warning") : unavailable()
  }
} as const satisfies Record<string, KpiDefinition>;

export type KpiId = keyof typeof KPI_REGISTRY;
export interface ResolvedKpi extends MetricSnapshot {
  id: KpiId;
  scope: string;
  canonicalValue: string;
  status: KpiStatus;
  currency: string | null;
}

export const WORKSPACE_KPI_IDS = {
  portfolio: ["portfolio.total-exposure", "portfolio.unrealized-pnl", "portfolio.cash", "portfolio.open-positions"],
  trading: ["trading.net-pnl", "trading.open-orders", "trading.cash", "trading.portfolio-value"],
  strategy: ["strategy.active-runs", "strategy.queued-runs", "strategy.review-runs", "strategy.winning-runs"],
  data: ["data.providers-healthy", "data.backfills-running", "data.exports-ready", "data.ops-review"],
  accounting: ["accounting.open-breaks", "accounting.timing-drift", "accounting.security-gaps", "accounting.audit-ready"],
  settings: ["system.providers-online", "system.active-runs", "portfolio.open-positions", "system.storage-health"],
  today: ["portfolio.total-exposure", "portfolio.open-positions", "system.active-runs", "system.providers-online"],
  retainedPortfolio: ["portfolio.retained-market-value", "portfolio.retained-records"]
} as const satisfies Record<string, readonly KpiId[]>;

const workspaceSourceIds = {
  trading: {
    "trading-net-pnl": "trading.net-pnl", pnl: "trading.net-pnl",
    "trading-open-orders": "trading.open-orders", orders: "trading.open-orders",
    "trading-cash": "trading.cash", "trading-portfolio-value": "trading.portfolio-value",
    fills: "trading.fills", risk: "trading.risk"
  },
  strategy: {
    "active-runs": "strategy.active-runs", runs: "strategy.runs",
    "queued-runs": "strategy.queued-runs", queued: "strategy.queued",
    "review-runs": "strategy.review-runs", review: "strategy.review-runs",
    "winning-runs": "strategy.winning-runs", promotions: "strategy.promotions"
  },
  data: {
    "providers-healthy": "data.providers-healthy", providers: "data.providers-healthy",
    "backfills-running": "data.backfills-running", backfills: "data.backfills-running",
    "exports-ready": "data.exports-ready", exports: "data.exports-ready",
    "ops-review": "data.ops-review", review: "data.ops-review",
    "kernel-critical-jumps": "data.kernel-critical-jumps", "data-replacement-cost": "data.replacement-cost"
  },
  accounting: {
    "open-breaks": "accounting.open-breaks", breaks: "accounting.open-breaks",
    "timing-drift": "accounting.timing-drift", drift: "accounting.timing-drift",
    "security-gaps": "accounting.security-gaps", coverage: "accounting.security-gaps",
    "audit-ready": "accounting.audit-ready", audit: "accounting.audit-ready",
    "kernel-critical-jumps": "accounting.kernel-critical-jumps"
  }
} as const satisfies Record<string, Record<string, KpiId>>;

/** Select registered figures only; callers cannot supply the value or redefine its label. */
export function workspaceKpiIds(owner: keyof typeof workspaceSourceIds, metrics?: readonly Pick<MetricSnapshot, "id">[] | null): readonly KpiId[] {
  if (!metrics) return WORKSPACE_KPI_IDS[owner];
  const sourceIds: Readonly<Record<string, KpiId>> = workspaceSourceIds[owner];
  return [...new Set(metrics.flatMap(({ id }) => sourceIds[id] ? [sourceIds[id]] : []))];
}

const retainedScopeFields = [["account", "account id"], ["run", "run id"], ["as of"], ["source"]] as const;

function readRetainedScopeField(explorer: FinancialRecordExplorerDto | null | undefined, names: readonly string[]) {
  const values = [...new Set((explorer?.scopeItems ?? [])
    .filter((item) => names.includes(item.label.trim().toLowerCase()))
    .map((item) => item.value.trim()).filter(Boolean))];
  return { value: values.length === 1 ? values[0] : null, ambiguous: values.length > 1 };
}

function readScope(definition: KpiDefinition, models: KpiReadModels): Record<string, string | null> {
  if (definition.owner === "portfolioExplorer") {
    const field = (...names: string[]) => readRetainedScopeField(models.portfolioExplorer, names).value;
    return { source: "retained-records", account: field("account", "account id"), run: field("run", "run id"), asOf: field("as of"), recordSource: field("source") };
  }
  // Portfolio endpoint is currently aggregate. URL scope must not relabel it as filtered.
  return { source: definition.owner === "portfolio" ? "live-holdings" : definition.owner, account: definition.owner === "trading" ? models.trading?.brokerage?.account ?? null : "all" };
}

export function resolveKpi(id: KpiId, models: KpiReadModels, requestedScope?: KpiScope): ResolvedKpi {
  const definition: KpiDefinition = KPI_REGISTRY[id];
  const dimensions = readScope(definition, models);
  let result = definition.compute(models);
  // A consumer can request a scope, but cannot claim the owner supplied a different one.
  for (const [key, value] of Object.entries(requestedScope ?? {})) {
    if (value == null || value === "") continue;
    if (!definition.scopeDimensions.includes(key) || dimensions[key] !== value) {
      result = unavailable("Requested scope is not supplied by owning read model");
      dimensions[key] = value;
    }
  }
  const scope = JSON.stringify(Object.fromEntries(Object.entries(dimensions).sort(([a], [b]) => a.localeCompare(b))));
  return {
    id, label: definition.label, value: result.value, delta: result.delta, tone: result.tone, status: result.status, currency: result.currency, scope,
    canonicalValue: JSON.stringify({ status: result.status, value: result.raw, currency: result.currency })
  };
}

export function kpiAttributes(metric: ResolvedKpi) {
  return {
    "data-kpi-id": metric.id,
    "data-kpi-scope": metric.scope,
    "data-kpi-value": metric.canonicalValue,
    "data-kpi-display": metric.value
  };
}
