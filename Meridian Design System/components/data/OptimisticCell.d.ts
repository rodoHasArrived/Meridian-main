/**
 * OptimisticCell — the write counterpart to `EditableCell` (4.2, UX-depth track B-2).
 *
 * A committed edit renders **provisional** (an existing data condition, no new vocabulary) until the
 * write confirms; a **rejection reverts the value with the reason attached**, never silently; and a
 * confirmed edit stays reversible by the operator — cell by cell (`Revert…`) or a whole row / the
 * current selection in one batch (`OptimisticRowBar`) — always with a **stated reason**. A revert
 * is recorded as its own write, never as an erasure of the first.
 *
 * The ledger lives in `OptimisticProvider`; a cell outside one keeps a private ledger and confirms
 * immediately (fixture behaviour). The cell displays the ledger's value while an edit is pending or
 * confirmed and falls back to `value` once it is rejected, reverted or dismissed — so the parent
 * model only needs to update on `onSettle({status:"confirmed"})`.
 *
 * @example
 * <OptimisticProvider commit={api.patch} revert={api.revertBatch} onSettle={applyToModel}>
 *   <OptimisticRowBar rowIds={selected} label="edits in selection" />
 *   <DataGrid columns={[{ key:"limit", cell:(r) => <OptimisticCell rowId={r.id} field="limit" value={r.limit} align="right" mode="number" /> }]} … />
 * </OptimisticProvider>
 */
export type OptimisticStatus = "pending" | "confirmed" | "rejected" | "reverting" | "reverted" | "dismissed";

export interface OptimisticEdit {
  id: string;
  rowId: string | number;
  field: string;
  prior: unknown;
  value: unknown;
  status: OptimisticStatus;
  /** Rejection reason verbatim, or the operator's revert reason. */
  reason?: string;
  receipt?: string;
  at: number;
  settledAt?: number;
  revertedAt?: number;
}

export interface OptimisticLedger {
  entries: OptimisticEdit[];
  subscribe(listener: (entries: OptimisticEdit[]) => void): () => void;
  latest(rowId: string | number, field: string): OptimisticEdit | null;
  forRow(rowId: string | number): OptimisticEdit[];
  commit(edit: Pick<OptimisticEdit, "rowId" | "field" | "prior" | "value">): Promise<void>;
  /** Reverts confirmed entries newest first; skips entries beneath newer in-flight or unselected confirmed edits of the same field. */
  revert(ids: string | string[], reason: string): Promise<void>;
  dismiss(id: string): void;
}

export interface OptimisticProviderProps {
  /** The write. Resolve to confirm (optionally `{receipt}`); **throw with the reason** to reject. */
  commit?: (edit: OptimisticEdit) => Promise<void | { receipt?: string }>;
  /** The compensating write for one or many confirmed edits, newest first, under one reason. Preserve this order when applying field priors. */
  revert?: (edits: OptimisticEdit[], reason: string) => Promise<void>;
  /** Fires on confirmed, rejected and reverted — the parent updates its model here. */
  onSettle?: (edit: OptimisticEdit) => void;
  onChange?: (entries: OptimisticEdit[]) => void;
  children?: React.ReactNode;
}
export declare function OptimisticProvider(props: OptimisticProviderProps): JSX.Element;
export declare function useOptimistic(): OptimisticLedger;
/** Bundle carrier. The compiler exposes only capitalized exports. */
export declare const OptimisticHooks: { useOptimistic: typeof useOptimistic };

export interface OptimisticCellProps {
  rowId: string | number;
  field: string;
  /** The model's committed value — shown when no edit is in flight or confirmed. */
  value: string | number | null | undefined;
  format?: (v: unknown) => string;
  /** @default "left" */
  align?: "left" | "right";
  validator?: (val: string) => true | string;
  /** @default "text" */
  mode?: "text" | "number";
  hint?: string;
  placeholder?: string;
  disabled?: boolean;
  /** Show the struck-through prior beside a provisional / confirmed value. @default true */
  showConfirmed?: boolean;
  className?: string;
  /** Observe the commit before it reaches the ledger. */
  onCommit?: (edit: Pick<OptimisticEdit, "rowId" | "field" | "prior" | "value">) => void;
}
export declare function OptimisticCell(props: OptimisticCellProps): JSX.Element;

export interface OptimisticRowBarProps extends React.HTMLAttributes<HTMLDivElement> {
  /** One row, the current selection, or omit for the whole ledger. */
  rowIds?: string | number | Array<string | number>;
  /** @default "edits on this row" */
  label?: string;
  /** Render the bar with this text when there are no edits (default: render nothing). */
  emptyLabel?: React.ReactNode;
  children?: React.ReactNode;
}
/** Tally of provisional · confirmed · rejected · reverted for the rows, plus one batch `Revert … confirmed…` with a reason. */
export declare function OptimisticRowBar(props: OptimisticRowBarProps): JSX.Element | null;
