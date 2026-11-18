// Time-axis tick selection and labels for the SVG charts (UX Phase F). Ticks are thinned by the
// pixels available so labels never overlap, and ISO dates read as "Aug '25" (or "Aug 5" when the
// whole axis sits inside a couple of months) instead of raw "2025-08-31".

import { niceTicks } from "./ticks";

/** Minimum horizontal distance between two time labels, in CSS pixels. */
export const MIN_TIME_TICK_GAP_PX = 72;

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"] as const;
const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})(?:$|[T\s])/;

/**
 * Indices of the time labels to draw for `count` evenly spaced points across `plotWidth` pixels.
 * Always includes the first and last point; the label before the last is dropped when it would
 * sit closer than `minGap` to it.
 */
export function selectTimeTickIndices(count: number, plotWidth: number, desired = 7, minGap = MIN_TIME_TICK_GAP_PX): number[] {
  if (count <= 0) return [];
  if (count === 1) return [0];
  const spacing = plotWidth / (count - 1);
  const maxLabels = Math.max(2, Math.floor(plotWidth / minGap) + 1);
  const step = Math.max(1, Math.round(count / Math.max(1, desired)), Math.ceil((count - 1) / (maxLabels - 1)));
  const indices: number[] = [];
  for (let index = 0; index < count; index += step) indices.push(index);
  const last = count - 1;
  if (indices[indices.length - 1] !== last) {
    while (indices.length > 1 && (last - indices[indices.length - 1]!) * spacing < minGap) indices.pop();
    indices.push(last);
  }
  return indices;
}

/**
 * A time-axis label. ISO dates become "Aug '25" when the axis spans more than about two months,
 * otherwise "Aug 5"; anything else is returned unchanged.
 */
export function formatTimeTickLabel(label: string, spanDays: number): string {
  const match = ISO_DATE.exec(label.trim());
  if (!match) return label;
  const [, year, month, day] = match;
  const monthLabel = MONTHS[Number(month) - 1];
  if (!monthLabel) return label;
  return spanDays > 62 ? `${monthLabel} '${year!.slice(-2)}` : `${monthLabel} ${Number(day)}`;
}

/** Days between the first and last ISO-dated labels, or 0 when they are not ISO dates. */
export function timeLabelSpanDays(labels: readonly string[]): number {
  if (labels.length < 2) return 0;
  const first = Date.parse(labels[0]!.trim().slice(0, 10));
  const last = Date.parse(labels[labels.length - 1]!.trim().slice(0, 10));
  return Number.isFinite(first) && Number.isFinite(last) ? Math.abs(last - first) / 86_400_000 : 0;
}

/** Minimum vertical distance between two value labels, in CSS pixels. */
export const MIN_VALUE_TICK_GAP_PX = 24;

/**
 * Nice value ticks for [min, max] that fit `plotHeight` pixels: asks `niceTicks` for fewer
 * divisions until neighbouring labels sit at least `minGap` apart.
 */
export function fitValueTicks(min: number, max: number, desired: number, plotHeight: number, minGap = MIN_VALUE_TICK_GAP_PX): number[] {
  const span = max - min || 1;
  for (let count = Math.max(1, desired); count >= 1; count -= 1) {
    const ticks = niceTicks(min, max, count);
    const step = ticks.length > 1 ? ticks[1]! - ticks[0]! : span;
    if ((step / span) * plotHeight >= minGap || count === 1) return ticks;
  }
  return [];
}

/** Axis text: the body face with tabular figures, not the monospace data face. */
export const AXIS_LABEL_STYLE = {
  fontFamily: "var(--font-body, var(--font-sans, system-ui, sans-serif))",
  fontSize: 12,
  fontVariantNumeric: "tabular-nums"
} as const;
