import type { NumericSurface } from "./FormatProvider";
export interface NumberOptions {
  locale?: string; decimals?: number; minDecimals?: number; maxDecimals?: number;
  grouping?: boolean; signed?: boolean; parens?: boolean; suffix?: string; zeroDash?: boolean;
  currency?: string; tick?: number; scale?: number; digits?: number;
}
export function setNumberDefaults(next?: Partial<NumericSurface>): NumericSurface;
export function resetNumberDefaults(): NumericSurface;
export function getNumberDefaults(): NumericSurface;
export function numberFormatter(locale?: string, options?: Intl.NumberFormatOptions): Intl.NumberFormat;
export function toNumber(value: unknown): number;
export function currencySymbol(code: string): string;
export function currencyDecimals(code: string): number;
export function decimalsForTick(tick: number | string): number;
export function formatNumber(value: unknown, options?: NumberOptions): string;
export function formatAccounting(value: unknown, options?: NumberOptions): string;
export function formatMoney(value: unknown, options?: NumberOptions): string;
export function formatMoneyCompact(value: unknown, options?: NumberOptions): string;
export function formatCompact(value: unknown, options?: NumberOptions): string;
export function formatQuantity(value: unknown, maxDecimals?: number, locale?: string): string;
export function formatPrice(value: unknown, options?: NumberOptions): string;
export function formatPercent(value: unknown, options?: NumberOptions): string;
export function formatBps(value: unknown, options?: NumberOptions): string;
export function formatSignedDelta(value: unknown, format?: (value: number) => string): string;
export const GLYPHS: { minus: string; dash: string };
