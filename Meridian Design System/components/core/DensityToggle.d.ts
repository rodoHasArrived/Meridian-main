/**
 * DensityToggle — operator control for the density token scopes
 * (`body[data-theme-density="terminal" | "compact" | "spacious"]`). Terminal is the densest
 * scope for multi-monitor ops walls; compact tightens rows and padding for dense monitoring;
 * spacious loosens for review; default clears the attribute. Writes the attribute onto a
 * target element (document.body by default), optionally persisting the choice to localStorage.
 * Segmented, flat, no transitions.
 */
import * as React from "react";

export type Density = "terminal" | "compact" | "default" | "spacious";

export interface DensityToggleProps {
  /** Controlled value. Omit for uncontrolled (self-managed) use. */
  value?: Density;
  /** Initial value when uncontrolled. @default "default" */
  defaultValue?: Density;
  onChange?: (value: Density) => void;
  /** Where to write `data-theme-density`: "body" | "html"/"root" | CSS selector | element | ref. @default "body" */
  target?: "body" | "html" | "root" | string | HTMLElement | React.RefObject<HTMLElement>;
  /** Apply the attribute to `target`. Set false to drive density yourself via onChange. @default true */
  apply?: boolean;
  /** localStorage key to persist the selection across reloads. @default null */
  persist?: string | null;
  /** Show text labels next to the row glyphs. @default true */
  showLabels?: boolean;
  /** @default "md" */
  size?: "sm" | "md";
  fullWidth?: boolean;
  className?: string;
}
export declare function DensityToggle(props: DensityToggleProps): JSX.Element;
