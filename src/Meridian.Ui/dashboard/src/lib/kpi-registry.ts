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
import type { WorkstationCrossScreenCounts } from "@/types/cross-screen-counts";
import { countOpenOrders, countOpenBreaks, formatProviderHealth, selectSharedProviderHealth } from "@/lib/cross-screen-counts";

/** Read models are inputs, never competing fallbacks for the same figure. */
export interface KpiReadModels {
  portfolio?: PortfolioWorkspaceResponse | null;
  portfolioExplorer?: FinancialRecordExplorerDto | null;
  trading?: TradingWorkspaceResponse | null;
  overview?: SystemOverviewResponse | null;
  strategy?: StrategyWorkspaceResponse | null;
  data?: DataWorkspaceResponse | null;
  accounting?: AccountingWorkspaceResponse | null;
  crossScreenCounts?: WorkstationCrossScreenCounts | null;
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
  evidence?: Partial<MetricSnapshot>;
}

export interface KpiDefinition {
  readonly label: string;
  readonly owner: Owner;
  readonly computation: string;
  readonly scopeDimensions: readonly string[];
  readonly currencyRule: string;
  readonly compute: (models: KpiReadModels) => ComputedKpi;
}

const unavailable = (reason = "Source data unavailable"): ComputedKpi => ({
  value: "Unavailable", delta: reason, tone: "warning", status: "unavailable", raw: null, currency: null
});
const scalar = (value: string | number, tone: Tone = "default", delta = ""): ComputedKpi => ({
  value: String(value), delta, tone, status: "available", raw: value, currency: null
});

export interface Money { units: bigint; scale: number; currency: string }
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

export function positionMoney(value: string, suppliedCurrency?: string | null): Money | null {
  const currency = suppliedCurrency?.trim().toUpperCase();
  if (!currency) return parseKpiMoney(value);
  if (!/^[A-Z]{3}$/.test(currency)) return null;
  const parsed = parseKpiMoney(value);
  if (!parsed) return parseKpiMoney(`${currency} ${value}`);
  // A producer's explicit ISO denomination outranks the generic dollar display convention.
  // An explicitly conflicting code (or an unambiguous euro/pound symbol) remains invalid.
  const genericDollar = /^[()+\-\s]*\$/.test(value.trim());
  if (parsed.currency !== currency && !genericDollar) return null;
  return { ...parsed, currency };
}

export function decimalText({ units, scale }: Pick<Money, "units" | "scale">): string {
  const negative = units < 0n;
  const digits = (negative ? -units : units).toString().padStart(scale + 1, "0");
  const whole = scale ? digits.slice(0, -scale) : digits;
  const fraction = scale ? digits.slice(-scale).replace(/0+$/, "") : "";
  return `${negative ? "-" : ""}${whole}${fraction ? `.${fraction}` : ""}`;
}

/** One display format for every holdings amount: headline totals and per-holding values alike. */
export function formatKpiMoney(money: Money, signed = false, alwaysCents = false): string {
  const decimal = decimalText(money);
  const negative = decimal.startsWith("-");
  const [whole, fraction] = decimal.replace(/^-/, "").split(".");
  const amount = `${whole.replace(/\B(?=(\d{3})+(?!\d))/g, ",")}${fraction || alwaysCents ? `.${(fraction ?? "").padEnd(2, "0")}` : ""}`;
  const prefix = currencySymbols[money.currency] ?? `${money.currency} `;
  return `${negative ? "-" : signed ? "+" : ""}${prefix}${amount}`;
}

function moneyResult(money: Money, signed = false, delta = "Live holdings · all accounts"): ComputedKpi {
  const decimal = decimalText(money);
  const negative = decimal.startsWith("-");
  return {
    value: formatKpiMoney(money, signed),
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
  const values = portfolio.positions.map((position) => positionMoney(position[field], position.currency));
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
      if (matches.length === 0) return unavailable("Figure not supplied by its source data");
      if (new Set(matches.map((metric) => metric.value)).size > 1) return unavailable("Conflicting values in its source data");
      const metric = matches[0];
      if (!metric.value?.trim() || ["—", "Unavailable"].includes(metric.value.trim())) return unavailable(metric.delta || "Value not supplied");
      const money = currency === true ? parseKpiMoney(metric.value) : null;
      if (currency === true && !money) return unavailable("Currency or value not supplied");
      if (currency === "compact-usd" && !/^\$\d+(?:\.\d+)?[kM]?$/.test(metric.value.trim())) return unavailable("Compact USD estimate not supplied");
      return { value: metric.value, delta: metric.delta, tone: metric.tone, status: "available", raw: money ? decimalText(money) : metric.value, currency: currency === "compact-usd" ? "USD" : money?.currency ?? null, evidence: metric };
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

function crossScreenProviders(models: KpiReadModels): ComputedKpi {
  const providers = selectSharedProviderHealth(models.crossScreenCounts, models.data?.providers);
  if (!providers) return unavailable("Provider health count unavailable");
  return scalar(formatProviderHealth(providers), providers.total > 0 && providers.healthy === providers.total ? "success" : "warning");
}

const providerDefinition: KpiDefinition = {
  label: "Providers healthy", owner: "crossScreenCounts",
  computation: "Healthy providers out of every listed provider, from the shared cross-screen count; use the Data provider list only when the shared count is absent. Never use separately counted overview cards.",
  scopeDimensions: holdingDimensions, currencyRule: "Not a monetary figure.", compute: crossScreenProviders
};

const dataProviderDefinition: KpiDefinition = {
  ...providerDefinition,
  computation: "The same figure as Today and Settings: healthy providers out of every provider in use, from the shared cross-screen count. The loaded Data list is counted only when the shared count is withheld (no provider permission), and then covers only the providers visible to the viewer."
};

function crossScreenOpenOrders(models: KpiReadModels): ComputedKpi {
  const count = countOpenOrders(models.trading?.openOrders) ?? models.crossScreenCounts?.openOrders?.count;
  if (count == null) return unavailable("Open-order count unavailable");
  const evidence = models.trading?.openOrders ? models.trading.metrics?.find((metric) => ["orders", "open-orders", "trading-open-orders"].includes(metric.id)) : undefined;
  return { ...scalar(count), evidence };
}

function crossScreenOpenBreaks(models: KpiReadModels): ComputedKpi {
  const count = models.accounting?.workspace?.openBreaks ?? countOpenBreaks(models.accounting?.breakQueue) ?? models.crossScreenCounts?.openBreaks?.count;
  if (count == null) return unavailable("Open-break count unavailable");
  const caption = models.accounting?.metrics?.find((metric) => metric.id === "open-breaks" || metric.id === "breaks");
  // The producer's history travels with the figure only when it ends on the same count shown here;
  // the count may come from another source, and a line ending elsewhere would contradict it.
  const lastHistoryValue = caption?.history?.at(-1)?.value;
  const history = lastHistoryValue === count ? { history: caption?.history, historyBasis: caption?.historyBasis } : {};
  return {
    ...scalar(count, count === 0 ? "success" : "warning", caption?.delta ?? ""),
    evidence: { baseline: caption?.baseline, comparisonPeriod: caption?.comparisonPeriod, ...history }
  };
}

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
  "trading.open-orders": {
    label: "Open orders", owner: "crossScreenCounts", computation: "Open orders counted once by the shared cross-screen definition: count the Trading open-order list when loaded, otherwise read the shared server count.",
    scopeDimensions: holdingDimensions, currencyRule: "Not a monetary figure.", compute: crossScreenOpenOrders
  },
  "trading.cash": sourceMetric("trading", "Cash", ["trading-cash"], true),
  "trading.portfolio-value": sourceMetric("trading", "Portfolio value", ["trading-portfolio-value"], true),
  "trading.fills": sourceMetric("trading", "Fills", ["fills"]),
  "trading.risk": sourceMetric("trading", "Risk", ["risk"]),
  "strategy.active-runs": {
    ...sourceMetric("strategy", "Active recent runs", ["active-runs"]),
    computation: "Read active-runs from the Strategy workspace's recent retained run population (currently up to six records), not the system-wide active-run count."
  },
  "strategy.queued-runs": sourceMetric("strategy", "Queued promotions", ["queued-runs"]),
  "strategy.queued": sourceMetric("strategy", "Queued", ["queued"]),
  "strategy.review-runs": sourceMetric("strategy", "Needs review", ["review-runs", "review"]),
  "strategy.winning-runs": sourceMetric("strategy", "Positive P&L", ["winning-runs"]),
  "strategy.promotions": sourceMetric("strategy", "Promotions", ["promotions"]),
  "data.providers-healthy": dataProviderDefinition,
  "data.backfills-running": sourceMetric("data", "Backfills running", ["backfills-running", "backfills"]),
  "data.exports-ready": sourceMetric("data", "Exports ready", ["exports-ready", "exports"]),
  "data.ops-review": sourceMetric("data", "Needs review", ["ops-review", "review"]),
  "data.kernel-critical-jumps": sourceMetric("data", "Kernel jump alerts", ["kernel-critical-jumps"]),
  "data.replacement-cost": sourceMetric("data", "Est. replacement cost", ["data-replacement-cost"], "compact-usd"),
  "accounting.open-breaks": {
    label: "Open breaks", owner: "crossScreenCounts", computation: "Use the Accounting server's open-break total before its possibly withheld case rows; otherwise read the shared server count. All sources use the unresolved, undismissed and unsigned-off break definition.",
    scopeDimensions: holdingDimensions, currencyRule: "Not a monetary figure.", compute: crossScreenOpenBreaks
  },
  "accounting.timing-drift": sourceMetric("accounting", "Timing drift", ["timing-drift", "drift"]),
  "accounting.security-gaps": sourceMetric("accounting", "Security gaps", ["security-gaps", "coverage"]),
  "accounting.audit-ready": sourceMetric("accounting", "Audit ready", ["audit-ready", "audit"]),
  "accounting.kernel-critical-jumps": sourceMetric("accounting", "Kernel jump alerts", ["kernel-critical-jumps"]),
  "system.providers-online": providerDefinition,
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
  /** What the figure counts, stated identically wherever it appears. */
  population: string;
  /** What a change in the figure is measured against, or null when the figure carries no change. */
  comparisonBasis: string | null;
  scope: string;
  canonicalValue: string;
  status: KpiStatus;
  currency: string | null;
}

/**
 * The fixed population and comparison basis of every registered figure. The shared read model
 * serves the same catalog (`GET /api/workstation/kpi-definitions`, WorkstationKpiDefinitions.cs) so
 * the desktop workstation states figures identically; `kpi-definitions.snapshot.json` pins the two.
 */
export const KPI_DEFINITIONS: Readonly<Record<KpiId, { population: string; comparisonBasis: string | null }>> = {
  "portfolio.total-exposure": { population: "Live holdings · all accounts", comparisonBasis: null },
  "portfolio.unrealized-pnl": { population: "Live holdings · all accounts", comparisonBasis: null },
  "portfolio.open-positions": { population: "Live holdings · all accounts", comparisonBasis: null },
  "portfolio.cash": { population: "Live holdings · all accounts", comparisonBasis: null },
  "portfolio.retained-market-value": { population: "Retained run records", comparisonBasis: null },
  "portfolio.retained-records": { population: "Retained run records", comparisonBasis: null },
  "trading.net-pnl": { population: "Brokerage session · selected account", comparisonBasis: "Session open" },
  "trading.open-orders": { population: "Open orders · selected account", comparisonBasis: null },
  "trading.cash": { population: "Brokerage session · selected account", comparisonBasis: null },
  "trading.portfolio-value": { population: "Brokerage session · selected account", comparisonBasis: null },
  "trading.fills": { population: "Brokerage session · selected account", comparisonBasis: null },
  "trading.risk": { population: "Brokerage session · selected account", comparisonBasis: null },
  "strategy.active-runs": { population: "Recent strategy runs", comparisonBasis: null },
  "strategy.queued-runs": { population: "Recent strategy runs", comparisonBasis: null },
  "strategy.queued": { population: "Recent strategy runs", comparisonBasis: null },
  "strategy.review-runs": { population: "Recent strategy runs", comparisonBasis: null },
  "strategy.winning-runs": { population: "Recent strategy runs", comparisonBasis: null },
  "strategy.promotions": { population: "Recent strategy runs", comparisonBasis: null },
  "data.providers-healthy": { population: "Providers in use", comparisonBasis: null },
  "data.backfills-running": { population: "Data operations", comparisonBasis: null },
  "data.exports-ready": { population: "Data operations", comparisonBasis: null },
  "data.ops-review": { population: "Data operations", comparisonBasis: null },
  "data.kernel-critical-jumps": { population: "Data operations", comparisonBasis: null },
  "data.replacement-cost": { population: "Data operations", comparisonBasis: null },
  "accounting.open-breaks": { population: "Unresolved reconciliation breaks", comparisonBasis: "Your last visit" },
  "accounting.timing-drift": { population: "Accounting workspace", comparisonBasis: null },
  "accounting.security-gaps": { population: "Accounting workspace", comparisonBasis: null },
  "accounting.audit-ready": { population: "Accounting workspace", comparisonBasis: null },
  "accounting.kernel-critical-jumps": { population: "Accounting workspace", comparisonBasis: null },
  "system.providers-online": { population: "Providers in use", comparisonBasis: null },
  "system.active-runs": { population: "All strategy runs · system", comparisonBasis: null },
  "system.storage-health": { population: "Storage · system", comparisonBasis: null }
};

/** The population a provider-health figure covers once the shared count is withheld. */
const PROVIDERS_VISIBLE_POPULATION = "Providers visible to you";

function kpiPopulation(id: KpiId, models: KpiReadModels): string {
  const population = KPI_DEFINITIONS[id].population;
  if ((id === "system.providers-online" || id === "data.providers-healthy") && !models.crossScreenCounts?.providers && models.data?.providers) {
    return PROVIDERS_VISIBLE_POPULATION;
  }
  return population;
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
    "active-runs": "strategy.active-runs",
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

/** The payload actually supplying a registered figure, including the shared-count fallback. */
export function kpiReadOwner(id: KpiId, models: KpiReadModels): Owner {
  const owner = KPI_REGISTRY[id].owner;
  if (owner !== "crossScreenCounts") return owner;
  if (id === "system.providers-online") return models.crossScreenCounts?.providers ? "crossScreenCounts" : "data";
  if (id === "data.providers-healthy") return models.crossScreenCounts?.providers ? "crossScreenCounts" : "data";
  if (id === "trading.open-orders") return models.trading?.openOrders ? "trading" : "crossScreenCounts";
  return models.accounting?.workspace?.openBreaks != null || models.accounting?.breakQueue ? "accounting" : "crossScreenCounts";
}

function readScope(id: KpiId, models: KpiReadModels): Record<string, string | null> {
  const owner = kpiReadOwner(id, models);
  if (owner === "portfolioExplorer") {
    const field = (...names: string[]) => readRetainedScopeField(models.portfolioExplorer, names).value;
    return { source: "retained-records", account: field("account", "account id"), run: field("run", "run id"), asOf: field("as of"), recordSource: field("source") };
  }
  // Portfolio endpoint is currently aggregate. URL scope must not relabel it as filtered.
  return { source: owner === "portfolio" ? "live-holdings" : owner, account: owner === "trading" ? models.trading?.brokerage?.account ?? null : "all" };
}

export function resolveKpi(id: KpiId, models: KpiReadModels, requestedScope?: KpiScope): ResolvedKpi {
  const definition: KpiDefinition = KPI_REGISTRY[id];
  const dimensions = readScope(id, models);
  const result = definition.compute(models);
  const scope = JSON.stringify(Object.fromEntries(Object.entries(dimensions).sort(([a], [b]) => a.localeCompare(b))));
  return applyKpiScope({
    ...result.evidence,
    id, label: definition.label, value: result.value, delta: result.delta, tone: result.tone, status: result.status, currency: result.currency, scope,
    population: kpiPopulation(id, models), comparisonBasis: KPI_DEFINITIONS[id].comparisonBasis,
    canonicalValue: JSON.stringify({ status: result.status, value: result.raw, currency: result.currency })
  }, requestedScope);
}

/** Validate after actual loaded scope has been attached; unavailable requests keep their identity. */
export function applyKpiScope(metric: ResolvedKpi, requestedScope?: KpiScope): ResolvedKpi {
  const definition: KpiDefinition = KPI_REGISTRY[metric.id];
  const dimensions = JSON.parse(metric.scope) as Record<string, string | null>;
  let invalid = false;
  for (const [key, value] of Object.entries(requestedScope ?? {})) {
    if (value == null || value === "") continue;
    if (!definition.scopeDimensions.includes(key) || dimensions[key] !== value) {
      invalid = true;
      dimensions[key] = value;
    }
  }
  if (!invalid) return metric;
  const result = unavailable("Requested scope is not supplied by its source data");
  return {
    ...metric, value: result.value, delta: result.delta, tone: result.tone, status: result.status, currency: null,
    freshness: metric.freshness === "stale" ? "stale" : "unverified",
    canonicalValue: JSON.stringify({ status: result.status, value: null, currency: null }),
    scope: JSON.stringify(Object.fromEntries(Object.entries(dimensions).sort(([a], [b]) => a.localeCompare(b))))
  };
}

export function kpiAttributes(metric: ResolvedKpi) {
  return {
    "data-kpi-id": metric.id,
    "data-kpi-scope": metric.scope,
    "data-kpi-value": metric.canonicalValue,
    "data-kpi-display": metric.value,
    "data-kpi-population": metric.population
  };
}
