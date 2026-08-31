/**
 * Compact mutually-exclusive view switch — denser than `RadioGroup`, made for toolbars
 * (table/chart, Day/Week/Month, status filters). The active segment lifts onto a light
 * surface with the one subtle card shadow. Optional leading icon and trailing count per segment.
 */
export interface SegmentOption {
  value: string;
  label: React.ReactNode;
  /** Leading glyph/icon node. */
  icon?: React.ReactNode;
  /** Trailing mono count (e.g. filter tallies). */
  count?: number;
  disabled?: boolean;
}
export interface SegmentedControlProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onChange"> {
  /** Segments — strings or `{value, label, icon?, count?, disabled?}`. */
  options: (string | SegmentOption)[];
  /** Selected value. */
  value?: string;
  /** Fired with the newly selected value. */
  onChange?: (value: string) => void;
  /** Density. @default "md" */
  size?: "sm" | "md";
  /** Stretch segments to fill the container width. @default false */
  fullWidth?: boolean;
}
export declare function SegmentedControl(props: SegmentedControlProps): JSX.Element;
