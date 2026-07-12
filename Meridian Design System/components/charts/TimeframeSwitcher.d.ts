/**
 * TimeframeSwitcher — mono segmented resolution picker for chart toolbars (1m · 5m · 15m ·
 * 1h · 1D · 1W by default). One active value, radio semantics, sm by default.
 *
 * AsOfControl — the session clock. Every analysis surface states its time basis explicitly:
 * **Live** (green dot, ticking UTC stamp, "Freeze" action) ⇄ **As-of** (amber AS-OF chip,
 * frozen UTC datetime input, "Return to live"). Emits `{ mode, asOf }` — `asOf` is `null`
 * while live.
 *
 * @example
 * <TimeframeSwitcher value={tf} onChange={setTf} />
 * <TimeframeSwitcher options={["1D", "1W", "1M", "1Y"]} defaultValue="1D" />
 * <AsOfControl onChange={({ mode, asOf }) => reprice(mode === "live" ? null : asOf)} />
 */
export interface TimeframeSwitcherProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Strings or { value, label }. @default ["1m","5m","15m","1h","1D","1W"] */
  options?: (string | { value: string; label: string })[];
  /** Controlled active value. */
  value?: string;
  /** Uncontrolled initial value. @default first option */
  defaultValue?: string;
  onChange?: (value: string) => void;
  /** @default "sm" */
  size?: "sm" | "md";
}
export declare function TimeframeSwitcher(props: TimeframeSwitcherProps): JSX.Element;

export interface AsOfControlProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Controlled mode. */
  mode?: "live" | "asof";
  /** Uncontrolled initial mode. @default "live" */
  defaultMode?: "live" | "asof";
  /** The frozen as-of instant (Date, epoch ms, or ISO string) when controlled. */
  asOf?: Date | number | string;
  /** Fires on Freeze / Return to live / as-of edit. `asOf` is null while live. */
  onChange?: (basis: { mode: "live" | "asof"; asOf: Date | null }) => void;
  /** Show the date in the live stamp (vs time-of-day only). @default true */
  withDate?: boolean;
}
export declare function AsOfControl(props: AsOfControlProps): JSX.Element;
