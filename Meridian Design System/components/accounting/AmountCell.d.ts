import * as React from "react";
/**
 * Money primitive — a single right-aligned mono amount. Encodes sign, currency, fixed or
 * ISO-minor-unit decimals, accounting parentheses, zero-as-dash, an unsigned Dr/Cr side
 * suffix, compact metric notation, and a P&L color tone. The atom every accounting table
 * is built from — never hand-format currency next to it.
 *
 * **Value semantics.** Pass `envelope` — or an envelope-shaped object as `value` — and the
 * formatted amount renders through `ValueEnvelope`: condition, authority, evidence, clocks and
 * materiality travel with the figure and disclose on hover. Formatting stays here, provenance
 * stays in the envelope, and no facet component is ever stacked beside the number.
 * Law: `guidelines/VALUE_ENVELOPE.md`.
 *
 * @example
 * <AmountCell value={82440} currency="USD" envelope={{ condition: "confirmed", authority: "source",
 *   label: "1100 · Cash", clocks: { posting: "2026-06-02" } }} />
 * // or, carried on the data:
 * <AmountCell value={{ value: 82440, condition: "estimated", authority: "estimate" }} currency="USD" />
 */
import type { MeridianValueEnvelope } from "../operations/ValueEnvelope";

/** An envelope carried on the data: the figure plus its facets, no `label` required. */
export type EnvelopeValue = MeridianValueEnvelope & { value: number | string | null };

export interface AmountCellProps {
  value: number | string | EnvelopeValue;
  /** Envelope facets for this figure (everything but `value`). Renders through `ValueEnvelope`. */
  envelope?: Omit<MeridianValueEnvelope, "value">;
  /** Envelope mode only — `"right"` fills the numeric column. @default "right" */
  align?: "left" | "right";
  /** ISO code ("USD", "JPY") or a literal symbol ("$", "€"). */
  currency?: string;
  /** Fraction digits, or "auto" for the currency's ISO minor units (JPY 0, BHD 3). @default 2 */
  decimals?: number | "auto";
  /**
   * "pnl" colors negatives red / positives green; "muted" grays the figure;
   * "signed" forces an explicit leading +; "accounting" wraps negatives in parentheses.
   * @default "plain"
   */
  mode?: "plain" | "pnl" | "muted" | "signed" | "accounting";
  /** Negatives as (1,234.00) — statement convention. @default false */
  parens?: boolean;
  /** Exact 0 renders as an em dash. @default false */
  zeroDash?: boolean;
  /** Positives get an explicit leading +. @default false */
  signed?: boolean;
  /** Unsigned magnitude + small-caps Dr/Cr suffix (positive = Dr, negative = Cr). @default false */
  drcr?: boolean;
  /** Compact metric notation — $12.4M — with the full value as a title tooltip. Metric surfaces only. @default false */
  compact?: boolean;
  /** 600 weight — subtotals / totals. @default false */
  strong?: boolean;
  /** Metric emphasis — 600 weight at 1.25em. @default false */
  emphasis?: boolean;
  /** Wrapper element. @default "span" */
  as?: keyof React.JSX.IntrinsicElements;
  style?: React.CSSProperties;
}
export declare function AmountCell(props: AmountCellProps): React.JSX.Element;

/** True when a value is an envelope-shaped object (`value` plus at least one facet). */
export declare function isEnvelopeValue(v: unknown): boolean;
/** Split an envelope-shaped value into `{ value, envelope }`. */
export declare function splitEnvelopeValue(v: number | string | EnvelopeValue): {
  value: number | string | null;
  envelope?: Omit<MeridianValueEnvelope, "value">;
};
