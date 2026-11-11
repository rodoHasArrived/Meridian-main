// Meridian FormatProvider — declares the numeric surface once per screen: locale, currency,
// negative style, digit system, price/quantity precision. Everything numeric downstream reads it,
// including the module-level formatters that cannot hold a React context (the provider also sets
// the process defaults in `core/format.js`), so one declaration moves every number on the screen.
//
// Nest a second provider for the one region that must differ — a source-currency column, a
// custodian statement in its own locale. Nothing else may reach for `Intl`.
import React from "react";
import {
  setNumberDefaults, getNumberDefaults, resetNumberDefaults, numberFormatter,
  formatNumber, formatAccounting, formatMoney, formatMoneyCompact, formatCompact,
  formatQuantity, formatPrice, formatPercent, formatBps, formatSignedDelta,
  currencySymbol, currencyDecimals, decimalsForTick, toNumber, GLYPHS,
} from "./format.js";

/** The negative-figure conventions: an explicit U+2212 minus, or accounting parentheses. */
export const NEGATIVE_STYLES = [
  { id: "minus", label: "Minus", example: "\u22121,234.00" },
  { id: "parens", label: "Parentheses", example: "(1,234.00)" },
];

const FormatContext = React.createContext(null);

// A resolved surface, with every formatter pre-bound to it — call sites pass values, never locales.
function bind(surface) {
  const base = { locale: surface.locale, currency: surface.currency };
  const withBase = (o) => ({ ...base, ...o });
  return {
    ...surface,
    number: (v, o) => formatNumber(v, withBase(o)),
    accounting: (v, o) => formatAccounting(v, withBase(o)),
    money: (v, o) => formatMoney(v, withBase(o)),
    moneyCompact: (v, o) => formatMoneyCompact(v, withBase(o)),
    compact: (v, o) => formatCompact(v, withBase(o)),
    quantity: (v, max) => formatQuantity(v, max ?? surface.quantityDecimals, surface.locale),
    price: (v, o) => formatPrice(v, { tick: surface.tick, decimals: surface.priceDecimals, ...withBase(o) }),
    percent: (v, o) => formatPercent(v, withBase(o)),
    bps: (v, o) => formatBps(v, withBase(o)),
    delta: formatSignedDelta,
    symbol: currencySymbol,
    decimalsFor: (instrument) => (instrument && instrument.tick != null ? decimalsForTick(instrument.tick) : surface.priceDecimals),
    glyphs: GLYPHS,
  };
}

/**
 * Declare the numeric surface. Place one at the shell; nest for a region that must differ.
 * `applyGlobal` (default true for the outermost provider) also sets the process defaults so
 * module-level formatters follow — set it false on a nested override.
 */
export function FormatProvider({
  locale, currency, negative, numerals, priceDecimals, quantityDecimals, tick,
  applyGlobal, children,
}) {
  const parent = React.useContext(FormatContext);
  const surface = React.useMemo(() => {
    const d = getNumberDefaults();
    return {
      locale: locale || parent?.locale || d.locale,
      currency: currency || parent?.currency || d.currency,
      negative: negative || parent?.negative || d.negative,
      numerals: numerals || parent?.numerals || d.numerals,
      priceDecimals: priceDecimals ?? parent?.priceDecimals ?? d.priceDecimals,
      quantityDecimals: quantityDecimals ?? parent?.quantityDecimals ?? d.quantityDecimals,
      tick: tick ?? parent?.tick,
    };
  }, [locale, currency, negative, numerals, priceDecimals, quantityDecimals, tick, parent]);

  const global = applyGlobal !== undefined ? applyGlobal : !parent;
  // Layout effect, not effect: module-level formatters run during the same commit.
  React.useLayoutEffect(() => {
    if (!global) return;
    setNumberDefaults(surface);
  }, [global, surface]);
  if (global && typeof window !== "undefined") setNumberDefaults(surface);

  const value = React.useMemo(() => bind(surface), [surface]);
  return <FormatContext.Provider value={value}>{children}</FormatContext.Provider>;
}

/**
 * The resolved numeric surface with formatters bound to it. Safe without a provider — falls back
 * to the process defaults, so a component can adopt this without requiring one.
 */
export function useFormat(overrides) {
  const ctx = React.useContext(FormatContext);
  const o = overrides || null;
  return React.useMemo(() => {
    const base = ctx || bind({ ...getNumberDefaults() });
    if (!o) return base;
    const keys = Object.keys(o).filter((k) => o[k] !== undefined && o[k] !== null);
    if (!keys.length) return base;
    return bind({ ...base, ...Object.fromEntries(keys.map((k) => [k, o[k]])) });
  }, [ctx, o && o.locale, o && o.currency, o && o.negative, o && o.priceDecimals, o && o.quantityDecimals, o && o.tick]);
}

/** Hook carrier — `const { useFormat } = MeridianDesignSystem.FormatHooks`. */
export const FormatHooks = { useFormat, FormatContext, NEGATIVE_STYLES };

/**
 * The numeric authority itself, on the namespace: `MeridianDesignSystem.Format.money(…)`.
 * Templates and consumer code format through this — never through their own `Intl` constructor.
 */
export const Format = {
  setNumberDefaults, getNumberDefaults, resetNumberDefaults, numberFormatter,
  number: formatNumber, accounting: formatAccounting, money: formatMoney,
  moneyCompact: formatMoneyCompact, compact: formatCompact, quantity: formatQuantity,
  price: formatPrice, percent: formatPercent, bps: formatBps, delta: formatSignedDelta,
  currencySymbol, currencyDecimals, decimalsForTick, toNumber, GLYPHS,
  formatNumber, formatAccounting, formatMoney, formatMoneyCompact, formatCompact,
  formatQuantity, formatPrice, formatPercent, formatBps, formatSignedDelta,
};
