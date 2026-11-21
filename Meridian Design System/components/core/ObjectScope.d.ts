import type React from "react";
/**
 * Analytical scope — the object a figure is true of, stated on the figure.
 *
 * `Duration 8.22` can mean the instrument's duration, this holding's
 * contribution, the portfolio's duration, or an accounting proxy. Meridian
 * does not let the reader guess: a surface declares its own object with
 * `ObjectScopeProvider`, and any figure belonging to a different object wears
 * its scope word. The scope vocabulary is the object model's
 * (`core/ObjectModel` › `OBJECT_TYPES`), so the word on a figure is the word in
 * the object header. Law: `guidelines/ANALYTICAL_SCOPE.md`.
 *
 * @example
 * <ObjectScopeProvider scope="position" object="GA-FI-01">
 *   <ScopedValue label="Market value" value="$24.84M" size="lg" />
 *   <ScopedValue scope="security" label="Duration" value="8.22" explain />
 *   <ScopedValue scope="portfolio" label="Duration contribution" value="0.21" />
 *   <ScopeMismatch />
 * </ObjectScopeProvider>
 */
export type ScopeId = "security" | "lot" | "position" | "account" | "portfolio" | "legalEntity";
export type ScopeKind = "instrument" | "holding" | "aggregate";
export type ScopeRelationId = "same" | "sibling" | "instrument" | "holding" | "aggregate";

export interface AnalyticalScope {
  id: ScopeId;
  kind: ScopeKind;
  /** The question a figure in this scope answers. */
  answers: string;
}
/** The scope ladder — six object types that can own a figure. */
export declare const ANALYTICAL_SCOPES: AnalyticalScope[];

export interface ScopeMetaResult {
  id: string | null;
  /** From the object model, so scope words and object headers agree. */
  label: string;
  kind: ScopeKind;
  answers: string | null;
  known: boolean;
}
export declare function scopeMeta(id?: string | null): ScopeMetaResult;

export interface ScopeRelation {
  id: ScopeRelationId;
  /** Plain-language reason the figure is out of scope; null when it is in scope. */
  note: string | null;
}
/** How a figure's scope relates to the surface it is read on. */
export declare function scopeRelation(declared?: string | null, ambient?: string | null): ScopeRelation;

export interface ObjectScopeProviderProps {
  /** The object this region is about. */
  scope: ScopeId | string;
  /** The surface's own object id — `GA-FI-01`, a CUSIP. */
  object?: string | number | null;
  label?: string | null;
  children?: React.ReactNode;
}
/** Declares the object a region is about. Nest freely — a position drawer inside a
 *  security workspace re-declares scope for its own subtree. */
export declare function ObjectScopeProvider(props: ObjectScopeProviderProps): React.JSX.Element;

export interface ScopeMarkProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Omit to print the ambient scope. Pass to mark a figure that belongs elsewhere. */
  scope?: ScopeId | string;
  /** `label` — bare small-caps word (default, for use above a value).
   *  `chip` — bordered; dashed when foreign. `inline` — dotted-underlined when foreign. */
  variant?: "label" | "chip" | "inline";
  /** Precomputed relation, when the caller already has one. */
  relation?: ScopeRelation;
}
export declare function ScopeMark(props: ScopeMarkProps): React.JSX.Element | null;

export interface ScopedValueProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Omit for a figure that belongs to the surface's own object. */
  scope?: ScopeId | string;
  label?: React.ReactNode;
  value?: React.ReactNode;
  /** Free note under the figure — source, basis, caveat. */
  note?: React.ReactNode;
  size?: "sm" | "md" | "lg";
  /** False lays scope, label and value on one baseline. @default true */
  stack?: boolean;
  /** Print the relation sentence ("A property of the instrument — true of every
   *  holding of it, not of this one."). Use on the first foreign figure in a region. */
  explain?: boolean;
  /** Stable key for the region audit; defaults to the label. */
  id?: string;
  /** Replaces the rendered figure — an envelope, a delta, a sparkline. */
  children?: React.ReactNode;
}
/** A figure that states its own scope: scope word · label · value. */
export declare function ScopedValue(props: ScopedValueProps): React.JSX.Element;

export interface ScopeMismatchProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "title"> {
  title?: React.ReactNode;
}
/** Lists the figures in this region that belong to another object. Renders nothing when
 *  every figure is in scope. */
export declare function ScopeMismatch(props: ScopeMismatchProps): React.JSX.Element | null;

export declare const ScopeHooks: {
  useScope(): { scope: string | null; object: string | number | null; label: string | null; parent: string | null } | null;
  scopeMeta: typeof scopeMeta;
  scopeRelation: typeof scopeRelation;
  ANALYTICAL_SCOPES: typeof ANALYTICAL_SCOPES;
};
export declare const ObjectScope: {
  Provider: typeof ObjectScopeProvider;
  Mark: typeof ScopeMark;
  Value: typeof ScopedValue;
  Mismatch: typeof ScopeMismatch;
  meta: typeof scopeMeta;
  relation: typeof scopeRelation;
  SCOPES: typeof ANALYTICAL_SCOPES;
};
