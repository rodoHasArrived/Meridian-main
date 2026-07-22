/**
 * Keyboard shortcut hint — crisp key caps for operator workstations. Renders a single key
 * or a combo (split on `+` or whitespace) as `min-width 18px` caps with a 2px bottom border
 * for a tactile look. Mono key glyphs, tracks tokens for white-label + dark.
 */
export interface KbdProps extends React.HTMLAttributes<HTMLSpanElement> {
  /**
   * The shortcut. A string combo (`"Ctrl+Enter"`, `"⌘ K"`) is split into individual caps,
   * or pass an explicit array of key labels. If omitted, string `children` is used.
   */
  keys?: string | string[];
  /** `default` (muted, on medium bg) · `solid` (primary text, on light bg). @default "default" */
  variant?: "default" | "solid";
  children?: React.ReactNode;
}
export declare function Kbd(props: KbdProps): JSX.Element;
