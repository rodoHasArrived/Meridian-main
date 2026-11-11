/**
 * Masthead chrome — near-black brand bar capping the light workstation. Brand mark + module
 * breadcrumb · command search · UTC clock · environment mode badge · signed-in operator.
 */
export interface WorkstationTopbarProps {
  /**
   * Module shown after the brand slash, e.g. "Security Master". Only used as the fallback crumb
   * when no app map is loaded — with `MeridianAppMap` present the label comes from the map, so
   * the rail, the palette and the masthead can never disagree about what a screen is called.
   */
  moduleLabel?: string;
  /**
   * The breadcrumb path. **Omit it** — the masthead derives Domain › parent screen › screen ›
   * object from `MeridianAppMap` and the URL, and renders a back affordance when there is real
   * in-suite history or a parent screen. Pass an array to override, e.g. to append a step or
   * drawer as the current crumb.
   */
  breadcrumb?: Array<{ label: string; href?: string; onClick?: () => void; current?: boolean }>;
  /** @default "PAPER" */
  environment?: "LIVE" | "PAPER" | "FIXTURE";
  /**
   * Mono clock text. **Omit it** — the masthead then reads the shared world clock
   * (`MeridianWorld`) and ticks, and the status dot reflects real feed health. Pass a string only
   * to freeze the clock deliberately (a print artifact, a screenshot spec).
   */
  clock?: string;
  /** Brand mark URL — use the light mark on the dark bar. @default "assets/brand/meridian-mark-light.svg" */
  brandSrc?: string;
  /** Called when the command-search affordance is clicked. */
  onSearch?: () => void;
  /** Placeholder text in the search field. @default "Search this screen, or go to…" */
  searchLabel?: string;
  /** Key hint shown in the field. @default "/" */
  searchHint?: string;
  /**
   * The signed-in operator. **Omit it** — the masthead reads `MeridianWorld.OPERATOR`, so it is
   * the same person the audit trails credit. Pass an object to override, or `null` to suppress.
   */
  operator?: { name: string; role?: string; roleLabel?: string; desk?: string; title?: string } | null;
  /** Sign out. Defaults to routing to the sign-in screen via the app map. */
  onSignOut?: () => void;
  /** Hide the operator chip — for pre-auth surfaces. @default true */
  showOperator?: boolean;
}
export declare function WorkstationTopbar(props: WorkstationTopbarProps): JSX.Element;
