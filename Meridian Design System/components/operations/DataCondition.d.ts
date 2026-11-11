/**
 * Data condition — Meridian's eleven-condition vocabulary for any analytical value:
 * `confirmed · provisional · estimated · stale · overridden · missing · na ·
 * pending · exception · reconciled · restricted`. Loose status strings are normalized
 * (`Matched` → reconciled, `BreaksDetected` → exception, `N/A` → na,
 * `Withheld`/`Redacted` → restricted), so read
 * models can pass their own words. Every condition renders as **text + marker**;
 * color is never the only signal and a blank cell never stands for a condition.
 *
 * - `DataCondition` — the chip/inline label (inspector rows, table status columns).
 * - `DataConditionValue` — a value carrying its condition, using Meridian notation:
 *   `4.35% Est.` · `4.35% · Stale 18m` · `Missing` · `Restricted · Desk policy` · `—`.
 * - `DataConditionLegend` — the "data-state contract" list an evidence inspector
 *   shows: one row per field, marker + name + right-aligned state.
 *
 * @example
 * <DataCondition condition="stale" detail="18m" chip />
 * <DataConditionValue value="4.35%" condition="estimated" align="right" />
 * <DataConditionLegend items={[
 *   { label: "Price observation", condition: "confirmed" },
 *   { label: "Scenario result", condition: "estimated" },
 *   { label: "Reference attributes", condition: "stale", state: "Stale 18m" },
 *   { label: "Optional liquidity field", condition: "missing" },
 *   { label: "Counterparty exposure", condition: "restricted", state: "Desk policy" },
 * ]} />
 */
export type MeridianDataCondition =
  | "confirmed" | "provisional" | "estimated" | "stale" | "overridden"
  | "missing" | "na" | "pending" | "exception" | "reconciled" | "restricted";

export interface DataConditionProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Condition key or any recognized status string. */
  condition: MeridianDataCondition | string;
  /** Qualifier rendered after the label — age, count, reason ("18m", "2 of 3"). */
  detail?: string;
  /** Bordered/washed chip instead of the bare inline label. */
  chip?: boolean;
  /** Override the label text (the marker and color stay bound to the condition). */
  label?: string;
  /** Hide the marker glyph — only when an adjacent marker already carries it. */
  showGlyph?: boolean;
}
export declare function DataCondition(props: DataConditionProps): JSX.Element;

export interface DataConditionValueProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Pre-formatted value text; `null`/omitted renders `—` (or `Missing`).
   *  Ignored entirely when `condition` is `restricted` — a withheld figure is
   *  never rendered, even if one is passed. */
  value?: string | number | null;
  condition: MeridianDataCondition | string;
  /** Age for stale, reason for overridden, **the withholding policy for restricted**
   *  — folded into the notation (`Restricted · Desk policy`). */
  detail?: string;
  /** `"right"` for numeric table columns. */
  align?: "left" | "right";
}
export declare function DataConditionValue(props: DataConditionValueProps): JSX.Element;

export interface DataConditionLegendItem {
  id?: string;
  /** Field or series name. */
  label: string;
  condition: MeridianDataCondition | string;
  /** Right-hand state text; defaults to the condition label ("Stale 18m"). */
  state?: string;
}
export interface DataConditionLegendProps extends React.HTMLAttributes<HTMLDivElement> {
  items: DataConditionLegendItem[];
}
export declare function DataConditionLegend(props: DataConditionLegendProps): JSX.Element;

/** 4.0: the condition vocabulary, re-homed from the retired top-level `CONDITIONS`
 *  export. 4.1: eleven entries — `restricted` added as the entitlement condition. */
export declare namespace DataCondition {
  const CONDITIONS: Record<string, { label: string; glyph: string; note: string }>;
}
