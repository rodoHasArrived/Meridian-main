/** Multi-line text field — same paper/border/focus treatment as Input, vertically resizable. */
export interface TextAreaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  /** Small-caps label rendered above the field. Omit both label and error to render a bare `<textarea>`. */
  label?: string;
  /** Error message; turns the border red and renders below. */
  error?: string;
  /** @default 4 */
  rows?: number;
}
export declare function TextArea(props: TextAreaProps): JSX.Element;
