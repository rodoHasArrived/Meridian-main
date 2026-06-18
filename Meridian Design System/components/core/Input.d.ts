/** Mono text field on white paper — hairline border, teal-blue focus ring, optional label + error. */
export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  /** Small-caps label rendered above the field. */
  label?: string;
  /** Error message; turns the border red and renders below. */
  error?: string;
}
export declare function Input(props: InputProps): JSX.Element;
