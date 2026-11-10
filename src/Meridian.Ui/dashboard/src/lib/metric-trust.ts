// Trust rules for headline figures (stat strips and metric cards). A figure an operator relies on
// has to answer three questions without a cross-check: where does it come from, how old is it, and
// what is its change measured against. This module is the one place those rules live; the shared
// StatStrip and MetricCard render whatever it resolves, so a screen cannot show a baseline-less
// delta or a stale figure dressed as a fresh one by accident.
import { formatUtcTimestamp } from "@/lib/format";
import type { MetricFreshness, MetricProvenance, MetricSnapshot } from "@/types";

export type MetricDeltaKind = "change" | "caption";

export interface ResolvedMetricDelta {
  /** `change` is a quantity measured against something; `caption` is descriptive text. */
  kind: MetricDeltaKind;
  text: string;
}

// A quantity with nothing after it: "+8%", "0%", "+2", "-$1,200", "▲ 3.1 bps".
const BARE_CHANGE = /^[+\-−±▲▼△▽]?\s*[$€£¥]?\d[\d,.]*\s*(?:%|bps?|pp|pts?|x|[KMB])?$/i;
// A sign glued to one word ("+session") points a direction but measures nothing.
const DIRECTION_FLAG = /^[+\-−±][a-z]+$/i;
const STARTS_WITH_QUANTITY = /^[+\-−±▲▼△▽]?\s*[$€£¥]?\d/;

/**
 * Decides what a metric's `delta` may show. A bare change is rendered only together with the
 * `baseline` it is measured against ("+8% vs prior session"); without one it is dropped, because a
 * percentage with no comparison period reads as information and carries none. A change that
 * already names its comparison ("3 new since your last visit") and descriptive captions pass
 * through unchanged.
 */
export function resolveMetricDelta(delta: string | null | undefined, baseline?: string | null): ResolvedMetricDelta | null {
  const text = delta?.trim() ?? "";
  if (!text || DIRECTION_FLAG.test(text)) {
    return null;
  }

  if (BARE_CHANGE.test(text)) {
    const comparedTo = baseline?.trim();
    return comparedTo ? { kind: "change", text: `${text} ${comparedTo}` } : null;
  }

  return { kind: STARTS_WITH_QUANTITY.test(text) ? "change" : "caption", text };
}

const ISO_TIMESTAMP = /^\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2})?/;

/** An ISO timestamp as a UTC label with the year; any other text is shown as given. */
export function formatMetricAsOf(asOf: string | null | undefined): string | null {
  const text = asOf?.trim();
  if (!text) {
    return null;
  }

  if (ISO_TIMESTAMP.test(text)) {
    const date = new Date(text);
    if (!Number.isNaN(date.getTime())) {
      return formatUtcTimestamp(date);
    }
  }

  return text;
}

/** "Source: Portfolio workspace · As of Jun 30, 2026, 18:05 UTC · Scope: All accounts". */
export function describeMetricProvenance(provenance: MetricProvenance | null | undefined): string | null {
  const source = provenance?.source.trim();
  if (!provenance || !source) {
    return null;
  }

  const asOf = formatMetricAsOf(provenance.asOf);
  const scope = provenance.scope?.trim();
  return [
    `Source: ${source}`,
    asOf ? `As of ${asOf}` : null,
    scope ? `Scope: ${scope}` : null
  ].filter(Boolean).join(" · ");
}

export interface MetricTrustViewModel {
  delta: ResolvedMetricDelta | null;
  /** The figure is fed by data past its freshness budget or with no evidence behind it. */
  degraded: boolean;
  freshness: MetricFreshness;
  /** Short visible marker for a degraded figure, e.g. "Stale" / "Unverified". */
  freshnessLabel: string | null;
  /** Spoken and hover explanation of a degraded figure, e.g. "Stale: as of Jun 30, 2026, 18:05 UTC." */
  freshnessDetail: string | null;
  provenanceText: string | null;
  /** Hover text: freshness first, then where the figure comes from. */
  title: string | null;
}

/** Everything the shared figure components need to render one metric honestly. */
export function buildMetricTrust(metric: MetricSnapshot): MetricTrustViewModel {
  const freshness = metric.freshness ?? "fresh";
  const asOf = formatMetricAsOf(metric.provenance?.asOf);
  const provenanceText = describeMetricProvenance(metric.provenance);
  const freshnessLabel = freshness === "stale" ? "Stale" : freshness === "unverified" ? "Unverified" : null;
  const freshnessDetail = freshness === "stale"
    ? asOf ? `Stale: as of ${asOf}.` : "Stale: the source is past its freshness budget."
    : freshness === "unverified"
      ? "Unverified: no current evidence backs this figure."
      : null;

  return {
    delta: resolveMetricDelta(metric.delta, metric.baseline),
    degraded: freshness !== "fresh",
    freshness,
    freshnessLabel,
    freshnessDetail,
    provenanceText,
    title: [freshnessDetail, provenanceText].filter(Boolean).join(" ") || null
  };
}

export interface MetricContext {
  provenance?: MetricProvenance | null;
  freshness?: MetricFreshness | null;
  baseline?: string | null;
}

/**
 * Stamps a page's source, as-of, scope and freshness onto its headline metrics. A value the
 * metric already carries wins, so a figure taken from a different feed keeps its own provenance.
 */
export function withMetricContext<T extends MetricSnapshot>(metrics: readonly T[], context: MetricContext): T[] {
  return metrics.map((metric) => ({
    ...metric,
    ...(metric.provenance === undefined && context.provenance ? { provenance: context.provenance } : {}),
    ...(metric.freshness === undefined && context.freshness ? { freshness: context.freshness } : {}),
    ...(metric.baseline === undefined && context.baseline ? { baseline: context.baseline } : {})
  }));
}

/** The provenance every metric in a strip shares, or null when they differ or none is stated. */
export function sharedMetricProvenance(metrics: readonly MetricSnapshot[]): MetricProvenance | null {
  const [first, ...rest] = metrics.map((metric) => metric.provenance ?? null);
  if (!first) {
    return null;
  }

  const firstText = describeMetricProvenance(first);
  return firstText !== null && rest.every((provenance) => describeMetricProvenance(provenance) === firstText) ? first : null;
}

/** The worst freshness in a strip: stale outranks unverified, which outranks fresh. */
export function worstMetricFreshness(metrics: readonly MetricSnapshot[]): MetricFreshness {
  const states = metrics.map((metric) => metric.freshness ?? "fresh");
  return states.includes("stale") ? "stale" : states.includes("unverified") ? "unverified" : "fresh";
}

/** Maps a data-confidence tone (the page's trust summary) onto figure freshness. */
export function metricFreshnessFromTrustTone(tone: "ready" | "review" | "blocked" | "unknown" | null | undefined): MetricFreshness {
  if (tone === "ready") {
    return "fresh";
  }
  return tone === "review" || tone === "blocked" ? "stale" : "unverified";
}

// An optional sign, currency code or symbol, then one grouped number: "+$1,239", "(1,234.50)", "EUR 12".
const DISPLAYED_AMOUNT = /^(\()?\s*([+\-−])?\s*(?:[A-Z]{3}\s*)?[$€£¥]?\s*(\d[\d,]*(?:\.\d+)?)\s*(?:[A-Z]{3})?\s*\)?$/;

/**
 * A displayed amount back to a number: "+$1,239" -> 1239, "(1,234.50)" -> -1234.5. Text that is
 * not a single amount ("—", "1/2", "Observe") is null, so callers fall back to comparing text.
 */
export function parseDisplayedNumber(value: string | null | undefined): number | null {
  const match = DISPLAYED_AMOUNT.exec(value?.trim() ?? "");
  if (!match) {
    return null;
  }

  const parsed = Number(match[3]!.replace(/,/g, ""));
  if (!Number.isFinite(parsed)) {
    return null;
  }

  return match[1] || match[2] === "-" || match[2] === "−" ? -parsed : parsed;
}

function displayedDecimals(value: string): number {
  const match = /\.(\d+)\D*$/.exec(value.trim());
  return match ? match[1]!.length : 0;
}

function sameDisplayedFigure(first: string, second: string): boolean {
  const a = parseDisplayedNumber(first);
  const b = parseDisplayedNumber(second);
  if (a === null || b === null) {
    return first.trim() === second.trim();
  }

  // Compare at the coarser of the two displayed precisions: "$18,900" equals "$18,900.25".
  const scale = 10 ** Math.min(displayedDecimals(first), displayedDecimals(second));
  return Math.round(a * scale) === Math.round(b * scale);
}

export interface HeadlineFigure {
  /** Shared key that pairs the same quantity across two panels, e.g. "unrealized-pnl". */
  key: string;
  label: string;
  value: string;
}

export interface HeadlineFigureSet {
  /** Where on the page the figures appear, e.g. "Header strip". */
  name: string;
  provenance: MetricProvenance;
  figures: readonly HeadlineFigure[];
}

export interface FigureDifferenceRow {
  key: string;
  label: string;
  first: string;
  second: string;
}

export interface FigureDifferenceNote {
  title: string;
  /** Why the two panels disagree, in one sentence. */
  summary: string;
  /** True when both panels claim the same source, so the gap has no explanation yet. */
  unexplained: boolean;
  rows: FigureDifferenceRow[];
  sources: Array<{ name: string; text: string }>;
}

/**
 * Explains two sets of figures on one page that disagree. Returns null when every figure they
 * share matches, so the note appears only when an operator would otherwise have to reconcile the
 * page by hand.
 */
export function buildFigureDifferenceNote(first: HeadlineFigureSet, second: HeadlineFigureSet): FigureDifferenceNote | null {
  const secondByKey = new Map(second.figures.map((figure) => [figure.key, figure]));
  const rows: FigureDifferenceRow[] = [];
  for (const figure of first.figures) {
    const other = secondByKey.get(figure.key);
    if (other && !sameDisplayedFigure(figure.value, other.value)) {
      rows.push({ key: figure.key, label: figure.label, first: figure.value, second: other.value });
    }
  }

  if (rows.length === 0) {
    return null;
  }

  const firstText = describeMetricProvenance(first.provenance) ?? "Source not stated";
  const secondText = describeMetricProvenance(second.provenance) ?? "Source not stated";
  const unexplained = firstText === secondText;

  return {
    title: "Why these differ",
    summary: unexplained
      ? `${first.name} and ${second.name} report the same source but different figures. Treat both as unverified until the difference is traced.`
      : `${first.name} and ${second.name} read different records, so their figures are not expected to match.`,
    unexplained,
    rows,
    sources: [
      { name: first.name, text: firstText },
      { name: second.name, text: secondText }
    ]
  };
}
