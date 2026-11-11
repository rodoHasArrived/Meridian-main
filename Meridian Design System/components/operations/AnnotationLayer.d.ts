/**
 * AnnotationLayer — a note anchored to a grid cell, a series point, a ledger line or an object
 * header (4.2, UX-depth track E-1). Review was attestation-only: an operator could sign a figure but
 * not mark one up.
 *
 * Every note carries the **object version** it was written against, and the operator picks its
 * lifecycle at write time: **follow** — a later version marks the note *stale* (mirroring attestation
 * supersession; the note can be re-affirmed with *Still holds at vN*) — or **pin** — the note stays
 * bound to the version it names and later versions show the pin. Stale is a data condition, so it
 * renders as text + marker (dashed ¶, `stale · v12 → v14`), never colour alone.
 *
 * `AnnotationAnchor` wraps the thing a note sits on and shows the ¶ marker (empty anchors reveal it
 * on hover / focus); `AnnotationPanel` is the standing list for an EvidenceRail. One
 * `AnnotationProvider` per object in view — its `objectVersion` is what staleness is judged against.
 *
 * @example
 * <AnnotationProvider objectId="REC-2026-07-CASH" objectVersion={14} author="a.patel" annotations={seed} onChange={persist}>
 *   <AnnotationAnchor anchor={{ kind:"cell", ref:"row:1310/closing", label:"1310 · Closing" }}>{formatMoney(v)}</AnnotationAnchor>
 *   <AnnotationPanel onFocus={(n) => select(n.anchor.ref)} />
 * </AnnotationProvider>
 */
export type AnnotationAnchorKind = "cell" | "point" | "line" | "header";
export interface AnnotationAnchorRef {
  kind: AnnotationAnchorKind;
  /** Stable key for the anchored thing — `"row:1310/closing"`, `"series:ust/10Y"`, `"line:JE-1080"`. */
  ref: string;
  label?: string;
}
export interface Annotation {
  id: string;
  objectId?: string;
  anchor: AnnotationAnchorRef;
  text: string;
  author: string;
  at: number;
  /** Object version the note was written against. */
  version: number;
  /** @default "follow" */
  lifecycle: "follow" | "pin";
  /** Derived: `lifecycle === "follow" && version < objectVersion`. */
  stale?: boolean;
}
export interface AnnotationProviderProps {
  objectId?: string;
  /** Current version of the object in view — staleness is judged against it. @default 1 */
  objectVersion?: number;
  /** @default "you" */
  author?: string;
  /** Seed notes (a persisted set). */
  annotations?: Omit<Annotation, "stale">[];
  onChange?: (notes: Annotation[]) => void;
  /** Default for `AnnotationPanel.onFocus`. */
  onFocus?: (note: Annotation) => void;
  children?: React.ReactNode;
}
export declare function AnnotationProvider(props: AnnotationProviderProps): JSX.Element;

export interface AnnotationsApi {
  notes: Annotation[];
  current: number;
  objectId?: string;
  add(note: Pick<Annotation, "anchor" | "text"> & Partial<Pick<Annotation, "lifecycle" | "version" | "author">>): Annotation;
  remove(id: string): void;
  /** Re-affirm a stale note at the current version. */
  repin(id: string, version: number): void;
}
export declare function useAnnotations(): AnnotationsApi;
/** Bundle carrier. The compiler exposes only capitalized exports. */
export declare const AnnotationHooks: { useAnnotations: typeof useAnnotations };

export interface AnnotationAnchorProps extends React.HTMLAttributes<HTMLSpanElement> {
  anchor: AnnotationAnchorRef;
  /** Lay out as a block row (ledger line, header) rather than inline (cell, point). @default false */
  block?: boolean;
  /** Which edge the popover hangs from. @default "right" */
  popover?: "left" | "right";
  readOnly?: boolean;
  children?: React.ReactNode;
}
export declare function AnnotationAnchor(props: AnnotationAnchorProps): JSX.Element;

export interface AnnotationPanelProps extends React.HTMLAttributes<HTMLElement> {
  /** @default "Annotations" */
  title?: string;
  onFocus?: (note: Annotation) => void;
  emptyText?: React.ReactNode;
}
/** All · Stale · Pinned list of the object's notes; a row calls `onFocus`. */
export declare function AnnotationPanel(props: AnnotationPanelProps): JSX.Element;
