/** Numeric field with +/- stepper buttons — mono value, hairline border. `min`/`max` clamp the value
 * and disable the boundary button; uncontrolled beyond the initial `value` (keeps its own state). */
export interface NumberInputProps {
  /** Small-caps label rendered above the field. */
  label?: string;
  /** Initial value. @default 0 */
  value?: number;
  /** Fires with the clamped new number on every change (typed or stepped). */
  onChange?: (value: number) => void;
  min?: number;
  max?: number;
  /** Amount the +/- buttons adjust by. @default 1 */
  step?: number;
  disabled?: boolean;
}
export declare function NumberInput(props: NumberInputProps): JSX.Element;
