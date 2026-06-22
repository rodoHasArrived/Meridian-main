/**
 * Masthead chrome — near-black brand bar capping the light workstation. Brand mark + module
 * breadcrumb · command search · UTC clock · environment mode badge.
 */
export interface WorkstationTopbarProps {
  /** Module shown after the brand slash, e.g. "Security Master". */
  moduleLabel?: string;
  /** @default "PAPER" */
  environment?: "LIVE" | "PAPER" | "FIXTURE";
  /** Mono clock text. @default "14:32:08 UTC" */
  clock?: string;
  /** Brand mark URL — use the light mark on the dark bar. @default "assets/brand/meridian-mark-light.svg" */
  brandSrc?: string;
  /** Called when the command-search affordance is clicked. */
  onSearch?: () => void;
}
export declare function WorkstationTopbar(props: WorkstationTopbarProps): JSX.Element;
