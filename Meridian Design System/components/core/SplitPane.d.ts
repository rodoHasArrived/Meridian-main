/**
 * SplitPane — two-panel resizable layout for workstation rails (list | inspector,
 * chart | blotter). Exactly two children. The divider is a load-bearing 1px hairline with
 * a 7px grab zone; keyboard-resizable (arrows step 8px, Shift+arrows 40px, Home/End snap
 * to min/max); double-click resets to `initial`; `persistKey` remembers the size.
 *
 * @example
 * <SplitPane direction="horizontal" initial={320} min={220} max={520} primary="end" persistKey="alerts-inspector">
 *   <AlertTable />
 *   <AlertInspector />
 * </SplitPane>
 */
export interface SplitPaneProps {
  /** @default "horizontal" (side-by-side) */
  direction?: "horizontal" | "vertical";
  /** Exactly two panels. */
  children: React.ReactNode;
  /** Starting size (px) of the primary pane. @default 280 */
  initial?: number;
  /** @default 160 */
  min?: number;
  /** @default 560 */
  max?: number;
  /** Which pane keeps the fixed size — "start" (first child) or "end" (second). @default "start" */
  primary?: "start" | "end";
  /** Persist the size to localStorage under this key. */
  persistKey?: string | null;
  style?: React.CSSProperties;
}
export declare function SplitPane(props: SplitPaneProps): JSX.Element;
