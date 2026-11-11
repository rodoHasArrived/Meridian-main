import React from "react";

/**
 * The two reading depths. A third level is a second screen, not a third dial position
 * (`guidelines/PROGRESSIVE_DISCLOSURE.md`).
 */
export type DetailLevel = "summary" | "full";
export type DisclosureSeverity = "none" | "info" | "warning" | "critical";

export interface DisclosureApi {
  screenId: string;
  /** The screen dial. */
  detail: DetailLevel;
  /** `detail === "summary"`. */
  summary: boolean;
  levels: DetailLevel[];
  /** Active per-region overrides, keyed by region id. */
  regions: Record<string, DetailLevel>;
  setDetail(level: DetailLevel): void;
  /** A region's override, falling back to the screen dial. */
  detailFor(regionId?: string | null): DetailLevel;
  /** `null` clears the override so the region follows the screen again. */
  setDetailFor(regionId: string, level: DetailLevel | null): void;
  overridden(regionId?: string | null): boolean;
  /** Operator collapse wins; otherwise `fallback` (the dial's answer). */
  isOpen(regionId?: string | null, fallback?: boolean): boolean;
  /** True once the operator has toggled this region themselves. */
  pinned(regionId?: string | null): boolean;
  setOpen(regionId: string, open: boolean): void;
  toggle(regionId: string, fallback?: boolean): void;
  /** Forget region overrides and operator pins; everything follows the dial again. */
  reset(): void;
}

export interface DisclosureProviderProps {
  /** Storage scope — `mds.disclosure.<screenId>`. @default "global" */
  screenId?: string;
  /** Controlled screen depth. Omit to let the provider own it. */
  detail?: DetailLevel;
  /** @default "full" */
  defaultDetail?: DetailLevel;
  /** Seed region overrides, e.g. `{ evidence: "full" }`. */
  regions?: Record<string, DetailLevel>;
  /** Persist dial, overrides and operator collapse to localStorage. @default true */
  persist?: boolean;
  /** Query param mirroring the screen dial so a link carries reading depth. @default "detail" */
  urlParam?: string | null;
  /** Storage shim for tests or a host-provided store. @default window.localStorage */
  storage?: Pick<Storage, "getItem" | "setItem">;
  onChange?: (e: { screenId: string; detail: DetailLevel | null; scope: "screen" | "region"; region?: string }) => void;
  children?: React.ReactNode;
}

export interface DetailDialProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Omit for the screen dial (belongs in the chrome). Set to scope the dial to one region. */
  region?: string;
  /** @default { summary: "Summary", full: "Full" } */
  labels?: Partial<Record<DetailLevel, string>>;
  /** @default "sm" */
  size?: "sm" | "md";
  fullWidth?: boolean;
  /** Region dials only: show "follows screen" / the reset affordance. @default true */
  showReset?: boolean;
  resetLabel?: string;
}

export interface DisclosureResidueProps {
  count?: number | null;
  countLabel?: string;
  severity?: DisclosureSeverity;
  severityLabel?: string;
  /** Pre-formatted clock or age — the region's last observation. */
  freshness?: string;
  children?: React.ReactNode;
}

export interface DisclosureRegionProps extends React.HTMLAttributes<HTMLElement> {
  /** Region id — the key for overrides and persisted collapse. Falls back to a string `title`. */
  id?: string;
  title: React.ReactNode;
  /** Small mono qualifier beside the title (a source, a version). */
  kicker?: React.ReactNode;
  /** Collapsed at summary depth, open at full depth. @default false */
  secondary?: boolean;
  /** Force the un-pinned default open state, ignoring `secondary` and the dial. */
  defaultOpen?: boolean;
  /** Render a region-scoped dial in the header. @default false */
  dial?: boolean;
  /** Residue: how much is in there. Shown collapsed and open alike. */
  count?: number | null;
  countLabel?: string;
  /** Worst severity inside — never hidden by collapse. */
  severity?: DisclosureSeverity;
  severityLabel?: string;
  freshness?: string;
  /** Extra residue: a node replaces the standard line, a string appends to it. */
  residue?: React.ReactNode;
  /** Header controls (buttons, links) — not the primary action. */
  actions?: React.ReactNode;
  /** How many fields/rows this region withholds at summary depth; renders the reveal bar. */
  withheld?: number;
  withheldLabel?: string;
  revealLabel?: string;
  /** Drop body padding — for a grid or table that owns its own edges. @default false */
  bare?: boolean;
  children?: React.ReactNode;
}

export interface DetailOnlyProps {
  /** Scope to a region's depth instead of the screen's. */
  region?: string;
  /** What is being held back, stated in the notice. */
  label?: string;
  /** Render the "Show" notice at summary depth. @default true */
  notice?: boolean;
  children?: React.ReactNode;
}

export declare const DETAIL_LEVELS: DetailLevel[];
export declare function DisclosureProvider(props: DisclosureProviderProps): JSX.Element;
export declare function DetailDial(props: DetailDialProps): JSX.Element;
export declare function DisclosureResidue(props: DisclosureResidueProps): JSX.Element;
export declare function DisclosureRegion(props: DisclosureRegionProps): JSX.Element;
export declare function DetailOnly(props: DetailOnlyProps): JSX.Element | null;
/** Null outside a provider — consumers must degrade to full detail, never to hidden. */
export declare function useDisclosure(): DisclosureApi | null;
/** `tier: "full"` columns/fields/rows hide at summary depth; untiered ones always show. */
export declare function atDepth(tier?: string, detail?: DetailLevel): boolean;
/** Worst of a severity list — what a collapsed region must still report. */
export declare function severityOf(list: DisclosureSeverity[]): DisclosureSeverity;
export declare const DisclosureHooks: {
  useDisclosure: typeof useDisclosure;
  atDepth: typeof atDepth;
  severityOf: typeof severityOf;
};
/** The file's carrier — every disclosure primitive reachable as one object. */
export declare const Disclosure: {
  Provider: typeof DisclosureProvider;
  Dial: typeof DetailDial;
  Region: typeof DisclosureRegion;
  Residue: typeof DisclosureResidue;
  DetailOnly: typeof DetailOnly;
  useDisclosure: typeof useDisclosure;
  atDepth: typeof atDepth;
  severityOf: typeof severityOf;
  LEVELS: DetailLevel[];
};
