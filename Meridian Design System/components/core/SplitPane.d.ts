import React from "react";

export type SplitSize = number | string;

export interface SplitPaneProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onResize"> {
  /** @default "horizontal" (side-by-side) */
  direction?: "horizontal" | "vertical";
  /** Exactly two panels. */
  children: React.ReactNode;
  /** Starting size of the primary pane — px number or "%" string. @default 280 */
  initial?: SplitSize;
  /** @default 160 */
  min?: SplitSize;
  /** @default 560 */
  max?: SplitSize;
  /** Floor for the *other* pane, so a large `max` can't crush it. @default 0 */
  minSecondary?: SplitSize;
  /** Which pane keeps the fixed size — "start" (first child) or "end". @default "start" */
  primary?: "start" | "end";
  /** Double-click / Enter collapses the primary pane to zero instead of resetting to `initial` */
  collapsible?: boolean;
  /** Controlled collapse state */
  collapsed?: boolean;
  onCollapsedChange?: (collapsed: boolean) => void;
  /** Lock the divider (still visible, not focusable or draggable) */
  disabled?: boolean;
  /** Fires continuously during a drag */
  onResize?: (size: number) => void;
  /** Fires once when a drag or keyboard resize settles — the point to sync layout to a server */
  onResizeEnd?: (size: number) => void;
  /** Persist the size to localStorage under this key (written at drag end) */
  persistKey?: string | null;
  /** Accessible name of the divider. @default "Resize panel" */
  dividerLabel?: string;
}

/**
 * Two-panel resizable layout for workstation rails (list | inspector, chart | blotter). Exactly two
 * children. The divider is a load-bearing 1px hairline with a 7px grab zone; keyboard-resizable
 * (arrows 8px, Shift+arrows 40px, Home/End snap to min/max, Enter toggles collapse when
 * `collapsible`); double-click resets to `initial`, or collapses when `collapsible`.
 *
 * Sizes accept px numbers or "%" strings and are re-clamped when the container resizes, so a rail
 * cannot end up wider than its window.
 *
 * @example
 * <SplitPane direction="horizontal" initial={320} min={220} max="45%" minSecondary={360}
 *            primary="end" collapsible persistKey="alerts-inspector">
 *   <AlertTable />
 *   <AlertInspector />
 * </SplitPane>
 */
export declare function SplitPane(props: SplitPaneProps): JSX.Element;
