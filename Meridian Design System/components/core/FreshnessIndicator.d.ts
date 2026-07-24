/**
 * FreshnessIndicator — live-data status primitive. A colored status dot plus an optional
 * source name and last-seen readout, encoding the voice rule "name the system, the time, the
 * evidence". Place beside any streaming value — quote feeds, provider links, run telemetry.
 * Flat by default; only `connecting` carries a quiet pulse. `ConnectionDot` is the bare dot
 * for inline use next to a number.
 */
import * as React from "react";

export type FreshnessStatus =
  | "live" | "fresh" | "stale" | "delayed" | "offline" | "connecting" | "idle";

export interface FreshnessIndicatorProps {
  /** @default "live" */
  status?: FreshnessStatus;
  /** System name shown in bold (e.g. "Polygon", "IBKR"). */
  source?: string;
  /** Last successful update — Date, epoch ms, or ISO string. Drives the time readout. */
  lastSeen?: Date | number | string;
  /** "relative" → "6m ago" (auto-ticks) · "utc" → "14:02:11Z" · "none". @default "relative" */
  timeFormat?: "relative" | "utc" | "none";
  /** Show the small-caps state word. @default true */
  showState?: boolean;
  /** Wrap in a square washed chip. @default false */
  badge?: boolean;
  /** Re-render interval for relative time, ms. @default 1000 */
  tickMs?: number;
  className?: string;
}
export declare function FreshnessIndicator(props: FreshnessIndicatorProps): JSX.Element;

export interface ConnectionDotProps {
  /** @default "live" */
  status?: FreshnessStatus;
  /** Diameter in px. @default 8 */
  size?: number;
  /** Hover title (falls back to the status word). */
  title?: string;
}
export declare function ConnectionDot(props: ConnectionDotProps): JSX.Element;
