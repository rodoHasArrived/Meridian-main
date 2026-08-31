/**
 * Action button — mirrors the desktop `PrimaryButtonStyle` / `GhostButtonStyle` / `LinkButtonStyle`.
 * `primary` is the solid teal-blue institutional action (one per screen); `ghost` is the paper
 * secondary; `danger` resolves to red on hover; `link` is a text-only info action.
 */
export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  /** @default "primary" */
  variant?: "primary" | "ghost" | "danger" | "link";
  /** `sm` · `default` (16/9 pad) · `lg` (20/12 pad, prominent CTAs) · `icon` (32×32 square). @default "default" */
  size?: "sm" | "default" | "lg" | "icon";
  /** Optional leading icon node (14–16px). */
  icon?: React.ReactNode;
  /** Replace the label with a spinner and set aria-busy. */
  busy?: boolean;
  busyLabel?: string | null;
  disabled?: boolean;
  children?: React.ReactNode;
}
export declare function Button(props: ButtonProps): JSX.Element;
