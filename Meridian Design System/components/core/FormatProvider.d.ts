import type React from "react";
/**
 * FormatProvider — declares the numeric surface for a screen: locale, currency, negative style,
 * digit system, price/quantity precision. `core/format.js` is the numeric authority (the only
 * `Intl` number-formatter constructor in the system); this is how a screen tells it what it is.
 *
 * The outermost provider also sets the process defaults, so module-level formatters that cannot
 * hold a React context follow the same surface. Nest a provider (with `applyGlobal={false}`) for
 * the one region that must differ — a source-currency column, a custodian statement.
 *
 * @example
 * <FormatProvider locale="de-DE" currency="EUR" negative="parens">
 *   <WorkstationShell>…</WorkstationShell>            // every number on the screen follows
 *   <FormatProvider currency="JPY" applyGlobal={false}>
 *     <LedgerTable rows={sourceCurrencyRows} />       // …except this table
 *   </FormatProvider>
 * </FormatProvider>
 *
 * @example
 * const fx = useFormat();                              // safe without a provider
 * fx.money(-1234.5, { parens: true });                 // (1,234.50)
 * fx.price(101.257, { tick: 0.0001 });                 // 101.2570 — precision from the instrument
 */
export type NegativeStyle = "minus" | "parens";

export interface NumericSurface {
  locale: string;
  currency: string;
  negative: NegativeStyle;
  numerals?: string | null;
  priceDecimals: number;
  quantityDecimals: number;
  tick?: number;
}

export interface BoundFormatters extends NumericSurface {
  /** Grouped number — `{ decimals, minDecimals, maxDecimals, signed, parens, suffix, zeroDash }`. */
  number(value: number | string, opts?: Record<string, unknown>): string;
  /** Negatives in parentheses — the statement convention. */
  accounting(value: number | string, opts?: Record<string, unknown>): string;
  money(value: number | string, opts?: Record<string, unknown>): string;
  moneyCompact(value: number | string, opts?: Record<string, unknown>): string;
  compact(value: number | string, opts?: Record<string, unknown>): string;
  quantity(value: number | string, maxDecimals?: number): string;
  /** Price at the instrument's precision — decimals from `tick` when given. */
  price(value: number | string, opts?: { tick?: number; decimals?: number; currency?: string }): string;
  /** Fraction in, percent out (`scale: 1` when the value is already in percent units). */
  percent(value: number | string, opts?: { decimals?: number; signed?: boolean; scale?: 1 | 100 }): string;
  bps(value: number | string, opts?: { decimals?: number; signed?: boolean }): string;
  /** Explicit-sign delta around any formatter. */
  delta(value: number | string, format?: (v: number) => string): string;
  symbol(code: string): string;
  decimalsFor(instrument?: { tick?: number }): number;
  glyphs: { minus: string; dash: string };
}

export interface FormatProviderProps extends Partial<NumericSurface> {
  /** Also set the process defaults so module-level formatters follow. @default true when outermost */
  applyGlobal?: boolean;
  children?: React.ReactNode;
}

export declare function FormatProvider(props: FormatProviderProps): React.JSX.Element;

/** The resolved surface with every formatter pre-bound. Falls back to process defaults with no provider. */
export declare function useFormat(overrides?: Partial<NumericSurface>): BoundFormatters;

export declare const NEGATIVE_STYLES: ReadonlyArray<{ id: NegativeStyle; label: string; example: string }>;

export declare const FormatHooks: {
  useFormat: typeof useFormat;
  FormatContext: React.Context<BoundFormatters | null>;
  NEGATIVE_STYLES: typeof NEGATIVE_STYLES;
};

/** The numeric authority on the namespace — `MeridianDesignSystem.Format.money(…)`. For templates and consumer code. */
export declare const Format: {
  setNumberDefaults(next: Partial<NumericSurface>): NumericSurface;
  getNumberDefaults(): NumericSurface;
  resetNumberDefaults(): NumericSurface;
  numberFormatter(locale?: string, opts?: Intl.NumberFormatOptions): Intl.NumberFormat;
  number(value: number | string, opts?: Record<string, unknown>): string;
  accounting(value: number | string, opts?: Record<string, unknown>): string;
  money(value: number | string, opts?: Record<string, unknown>): string;
  moneyCompact(value: number | string, opts?: Record<string, unknown>): string;
  compact(value: number | string, opts?: Record<string, unknown>): string;
  quantity(value: number | string, maxDecimals?: number, locale?: string): string;
  price(value: number | string, opts?: Record<string, unknown>): string;
  percent(value: number | string, opts?: Record<string, unknown>): string;
  bps(value: number | string, opts?: Record<string, unknown>): string;
  delta(value: number | string, format?: (v: number) => string): string;
  currencySymbol(code: string): string;
  currencyDecimals(code: string): number;
  decimalsForTick(tick: number): number;
  toNumber(value: unknown): number;
  GLYPHS: { minus: string; dash: string };
};
