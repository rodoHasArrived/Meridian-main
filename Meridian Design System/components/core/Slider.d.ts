/**
 * Single-value range control — flat square thumb over a filled track (institutional, not a rounded
 * consumer dial). Wraps a native `<input type="range">` so keyboard (arrows/Home/End/PgUp/PgDn) and
 * ARIA come for free; only the visuals are overridden. Good for backtest parameter sweeps
 * (min yield, max duration), risk thresholds, density/zoom controls.
 *
 * @example
 * <Slider label="Min yield" value={minYield} onChange={setMinYield}
 *   min={0} max={10} step={0.25} showValue valueFmt={(v) => v.toFixed(2) + "%"} />
 */
export interface SliderMark {
  value: number;
  label?: string;
}
export interface SliderProps
  extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "value" | "onChange" | "min" | "max" | "step" | "type"> {
  /** Small-caps label rendered above the track. */
  label?: string;
  /** @default 0 */
  value?: number;
  onChange?: (value: number) => void;
  /** @default 0 */
  min?: number;
  /** @default 100 */
  max?: number;
  /** @default 1 */
  step?: number;
  /** Show the current value (or `valueFmt(value)`) at the top-right of the label row. @default false */
  showValue?: boolean;
  /** Custom formatter for the displayed value, e.g. `(v) => v.toFixed(2) + "%"`. */
  valueFmt?: (value: number) => string;
  /** Tick labels rendered under the track, evenly spaced left-to-right (display only — does not snap the thumb). */
  marks?: SliderMark[];
  /** Track fill / thumb color. @default "accent" */
  variant?: "accent" | "success" | "warning" | "danger";
  disabled?: boolean;
}
export declare function Slider(props: SliderProps): JSX.Element;
