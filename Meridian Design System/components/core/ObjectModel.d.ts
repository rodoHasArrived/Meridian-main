/**
 * ObjectModel — the seventeen financial objects Meridian manipulates, and the identity contract each
 * one answers. Instead of every workstation inventing its own identity header, a screen names the
 * object type and `ObjectHeader` states type, identifier, name, entity/book context, condition,
 * as-of, material value, version, evidence coverage, and open exceptions — in that fixed order.
 *
 * This is a pattern, not a card: one rule, no fill, no shadow. It sits directly under the workspace
 * context bar and above the work plane (`guidelines/OBJECT_MODEL.md`).
 *
 * @example
 * <ObjectHeader
 *   type="position"
 *   id="91282CKQ3" identifierLabel="CUSIP"
 *   name="US Treasury 4.375% 05/15/34"
 *   context={{ portfolio: "General Account", book: "STAT" }}
 *   value={{ label: "Market value", amount: "$46.9M", secondary: "Par $48.2M" }}
 *   condition="confirmed" asOf="2026-07-31" version={4}
 *   coverage={{ attached: 99, required: 100, label: "sources" }}
 *   exceptions={{ count: 1, blocking: true }}
 *   onOpenEvidence={openEvidence} onOpenExceptions={openExceptions}
 * />
 */

export type MeridianObjectType =
  | "security" | "position" | "lot" | "account" | "journal" | "transaction"
  | "portfolio" | "legalEntity" | "accountingPeriod" | "reconciliation" | "exception"
  | "control" | "evidenceItem" | "calculation" | "attestation" | "scenario" | "strategy";

/**
 * The canonical cross-system reference to ONE financial object at ONE version.
 *
 * Every subsystem that points at an object — decision flow, evidence rail, consequence rail,
 * decision snapshot, action envelope, attestation — refers to it through this type and no other.
 * Widening the vocabulary means adding to `MeridianObjectType`; a subsystem that declares its own
 * object union forks the identity model (that is how `strategy` came to exist for `ObjectHeader`
 * and not for `DecisionFlow`). Enforced by conformance rule C-15.
 */
export interface ObjectRef {
  type: MeridianObjectType;
  id: string;
  /** Optimistic-concurrency identity. A decision is only reproducible against a stated version. */
  version: string | number;
  label?: string;
}

export interface ObjectTypeMeta {
  id: MeridianObjectType;
  label: string;
  plural: string;
  /** The primary key a human quotes — CUSIP, Account, Entry, Control. */
  identifierLabel: string;
  nameLabel: string;
  /** What "how much" means for this object. Never assumed to be a currency amount. */
  material: string;
  /** The states this object moves through. */
  lifecycle: string[];
  /** What evidence means for this object. */
  evidence?: string;
  /** Workspace-context facets that qualify it. */
  context: string[];
  /** `TimeContext` roles that apply (see `guidelines/TIME_SEMANTICS.md`). */
  timeRoles?: string[];
}
/** The object ledger, in reading order. */
export declare const OBJECT_TYPES: ObjectTypeMeta[];
/** Ledger entry for a type id, with a permissive fallback for unknown types. */
export declare function objectMeta(type: string): ObjectTypeMeta;

export interface ObjectDescriptor {
  type: MeridianObjectType | string;
  /** Primary identifier — the value a human quotes. */
  id?: string | number | null;
  /** Human-readable name. */
  name?: React.ReactNode;
  /** Override the ledger's identifier label. */
  identifierLabel?: string;
  /** `{ entity, portfolio, book, basis, … }` or an explicit `{label, value}[]`. */
  context?: Record<string, React.ReactNode> | { label: string; value: React.ReactNode }[];
  /** One of the eleven data conditions. */
  condition?: string;
  asOf?: React.ReactNode;
  /** Material value — `{ label, amount, secondary }`, or a bare value using the ledger's label. */
  value?: { label?: string; amount?: React.ReactNode; secondary?: React.ReactNode } | React.ReactNode;
  version?: string | number;
  /** `{ attached, required, label }` renders "99 of 100 sources" and flags an incomplete set. */
  coverage?: { attached?: number; required?: number; label?: string } | string;
  /** A count, or `{ count, blocking, label }`. */
  exceptions?: number | { count: number; blocking?: boolean; label?: string };
}
/** Normalizes any record into the identity contract, so a header, a chip, a breadcrumb, and a
 *  search result describe the same object the same way. */
export declare function describeObject(object: ObjectDescriptor): Required<ObjectDescriptor> & {
  typeLabel: string; materialLabel: string;
};

export interface ObjectHeaderProps extends ObjectDescriptor, Omit<React.HTMLAttributes<HTMLElement>, "id"> {
  /** Makes the evidence facet a button. */
  onOpenEvidence?: () => void;
  /** Makes the exceptions facet a button. */
  onOpenExceptions?: () => void;
  /** Tighter for inspector rails and split panes. @default false */
  dense?: boolean;
}
/** The identity contract, rendered. Facets the object does not carry are absent, never blank. */
export declare function ObjectHeader(props: ObjectHeaderProps): JSX.Element;

/** Bundle carrier — helpers for consumers of `_ds_bundle.js`. */
export declare const ObjectHooks: {
  describeObject: typeof describeObject;
  objectMeta: typeof objectMeta;
  OBJECT_TYPES: ObjectTypeMeta[];
};
/** Module carrier — `ObjectModel.Header` / `.describe` / `.meta` / `.TYPES`. */
export declare const ObjectModel: {
  Header: typeof ObjectHeader;
  describe: typeof describeObject;
  meta: typeof objectMeta;
  TYPES: ObjectTypeMeta[];
};
