/**
 * Delta — signed change value with the explicit-sign content rule built in. Mono, tabular,
 * dim semantic tone: positive green-dim, negative red-dim, zero muted "±0.00".
 *
 * @example
 * <Delta value={1.84} suffix="%" />           // +1.84%
 * <Delta value={-4118.22} decimals={2} />     // -4118.22
 * <Delta value={0.32} tone="down" arrow />    // ▼ +0.32 (tone override for inverted metrics)
 */
export interface DeltaProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** The signed change. Non-numeric renders an em dash. */
  value: number | string;
  /** Fixed decimal places. @default 2 */
  decimals?: number;
  /** Unit suffix, e.g. "%" or " bps". */
  suffix?: string;
  /** Prepend ▲/▼ (aria-hidden — the sign carries the meaning). @default false */
  arrow?: boolean;
  /** Override the auto tone (e.g. a drawdown where "up" is bad). */
  tone?: "up" | "down" | "flat";
}
export declare function Delta(props: DeltaProps): JSX.Element;
