// Optimistic edit ledger — the state machine behind OptimisticCell / OptimisticRowBar.
//
// Native TypeScript port of the ledger store in the Meridian Design System 4.3
// `components/data/OptimisticCell.jsx`. The rules are the package's:
//
//   (new) -> pending -> confirmed | rejected
//   confirmed -> reverting -> reverted            (the compensating write landed)
//   confirmed -> reverting -> confirmed + revertError  (the compensating write threw)
//   any -> dismissed
//
// - Transport outcomes own ledger state. A synchronous failure in `onSettle` or in a subscriber is
//   logged and cannot turn a confirmed write into a rejection, undo a successful compensation,
//   starve later subscribers, or stop the remaining batch notifications.
// - A revert is a compensating write recorded beside the first one, never an erasure: the entry
//   stays in the ledger as `reverted` with its reason.
// - Batch reverts unwind newest first, and never beneath a newer in-flight write or a newer
//   confirmed write that is not part of the same batch.
//
// Deliberate differences from the package. The first two exist because the package defaults would
// let a real screen claim a write that never happened; the rest because a real screen has other
// write paths, transports that go silent, and batches that land in part:
// - `commit` is required. The package confirms immediately when there is no transport.
// - Without a `revert` handler, `canRevert` is false and `revert()` changes nothing. The package
//   marks the entries reverted locally.
// - `pending -> unlanded` when `commit` throws an `OptimisticUnlandedError`: no answer came back,
//   so the fate of the write is unknown (ACTION_SEMANTICS A-4). It is not a rejection, and nothing
//   beneath it is reverted until it is dismissed or found in the model.
// - `reconcile()` compares a field's entries with the model value the screen holds. An entry the
//   model has moved past through another write path leaves the ledger with everything older for
//   that field, so an old undo can never be written over a newer value.
// - A `revert` handler that wrote only some of its targets throws an `OptimisticRevertError`
//   naming the ones that landed. Those are recorded as reverted; only the rest stay confirmed.
//
// The ledger is in-memory and lives as long as its provider. It is not a second source of product
// state: the durable value is whatever the consumer's `commit` / `revert` wrote.

export type OptimisticStatus =
  | "pending"
  | "confirmed"
  | "rejected"
  | "unlanded"
  | "reverting"
  | "reverted"
  | "dismissed";

export type OptimisticRowId = string | number;

export interface OptimisticEdit {
  id: string;
  rowId: OptimisticRowId;
  field: string;
  prior: unknown;
  value: unknown;
  status: OptimisticStatus;
  /** The rejection reason verbatim, what is known about an unlanded write, or the operator's stated revert reason. */
  reason?: string;
  receipt?: string;
  /** Why the last compensating write did not land. The entry is `confirmed` again while this is set. */
  revertError?: string;
  /** True when the last compensating write got no answer, so it is not known whether it landed. */
  revertUnlanded?: boolean;
  at: number;
  settledAt?: number;
  revertedAt?: number;
}

export type OptimisticEditInput = Pick<OptimisticEdit, "rowId" | "field" | "prior" | "value">;

/**
 * Thrown by `commit` or `revert` when the write got no usable answer: a dropped connection, a
 * server fault, a timeout. The fate of the write is unknown, which is not a refusal.
 */
export class OptimisticUnlandedError extends Error {
  constructor(message: string, options?: { cause?: unknown }) {
    super(message, options);
    this.name = "OptimisticUnlandedError";
  }
}

export interface OptimisticRevertErrorDetail {
  /** Ids of the targets whose compensating write landed before the failure. */
  landed?: readonly string[];
  /** Ids of the targets whose compensating write got no answer. */
  unlanded?: readonly string[];
  /** Why each remaining target is still confirmed, by id. A target not listed gets the message. */
  reasons?: Readonly<Record<string, string>>;
  cause?: unknown;
}

/**
 * Thrown by a `revert` handler that can say more than "the whole batch failed": which
 * compensating writes landed, and why each of the others did not.
 */
export class OptimisticRevertError extends Error {
  readonly landed: readonly string[];
  readonly unlanded: readonly string[];
  readonly reasons: Readonly<Record<string, string>>;

  constructor(message: string, detail: OptimisticRevertErrorDetail = {}) {
    super(message, { cause: detail.cause });
    this.name = "OptimisticRevertError";
    this.landed = detail.landed ?? [];
    this.unlanded = detail.unlanded ?? [];
    this.reasons = detail.reasons ?? {};
  }
}

export interface OptimisticHandlers {
  /**
   * The write. Resolve to confirm (optionally with a `receipt`); throw with the reason to reject;
   * throw an `OptimisticUnlandedError` when no answer came back.
   */
  commit: (edit: OptimisticEdit) => Promise<void | { receipt?: string }>;
  /**
   * The compensating write for one or many confirmed edits, newest first, under one reason.
   * Apply the priors in the order given. Throw to leave the edits confirmed; throw an
   * `OptimisticRevertError` when some of them landed first.
   */
  revert?: (edits: OptimisticEdit[], reason: string) => Promise<void>;
  /** Fires once per outcome: confirmed, rejected, unlanded, reverted. Never for a failed revert. */
  onSettle?: (edit: OptimisticEdit) => void;
}

export interface OptimisticLedger {
  readonly entries: readonly OptimisticEdit[];
  /** Whether a compensating write exists. Revert controls are not offered without one. */
  readonly canRevert: boolean;
  subscribe(listener: (entries: readonly OptimisticEdit[]) => void): () => void;
  /** The newest entry for a row and field that has not been dismissed. */
  latest(rowId: OptimisticRowId, field: string): OptimisticEdit | null;
  forRow(rowId: OptimisticRowId): OptimisticEdit[];
  commit(edit: OptimisticEditInput): Promise<void>;
  /**
   * The entries `revert` would write for these ids, newest first: confirmed, and not beneath a
   * newer provisional, reverting or unlanded edit, or a newer confirmed edit outside the ids.
   */
  revertTargets(ids: string | string[]): OptimisticEdit[];
  /** Reverts {@link revertTargets} for the ids under one reason. */
  revert(ids: string | string[], reason: string): Promise<void>;
  /**
   * Compare a field's entries with the value the model holds now. Call it whenever either
   * changes; OptimisticCell does. It changes the ledger only when the model shows that an entry
   * is no longer the current value, or that an unanswered write landed after all.
   */
  reconcile(rowId: OptimisticRowId, field: string, modelValue: unknown): void;
  dismiss(id: string): void;
}

let sequence = 0;

function describeFailure(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message) {
    return error.message;
  }
  if (typeof error === "string" && error) {
    return error;
  }
  return error == null ? fallback : String(error);
}

const inFlight = (entry: OptimisticEdit) => entry.status === "pending" || entry.status === "reverting";

/**
 * Create one ledger. `handlers` is read on every call, so a provider can keep the latest
 * callbacks in a ref without rebuilding the ledger.
 */
export function createOptimisticLedger(handlers: { readonly current: OptimisticHandlers }): OptimisticLedger {
  let entries: readonly OptimisticEdit[] = [];
  const subscribers = new Set<(entries: readonly OptimisticEdit[]) => void>();
  // Reconciliation bookkeeping. It lives here, not in a cell, so it survives a cell remounting.
  // `${id}:${status}` once the model has been seen holding what the entry stands for in that status.
  const inModel = new Set<string>();
  // The last model value reconciled per row and field.
  const modelValues = new Map<string, string>();
  // Entries committed while the model agreed with the value on screen.
  const committedInSync = new Set<string>();
  // The stated reason of a compensating write whose fate is unknown.
  const unansweredRevertReasons = new Map<string, string>();

  const cellKey = (rowId: OptimisticRowId, field: string) => `${typeof rowId}:${String(rowId)}\u0000${field}`;

  const notify = <T,>(listener: ((value: T) => void) | undefined, value: T, name: "ledger" | "onSettle") => {
    if (!listener) {
      return;
    }
    try {
      listener(value);
    } catch (error) {
      console.error(`OptimisticProvider: ${name} observer failed`, error);
    }
  };
  const emit = () => {
    for (const subscriber of [...subscribers]) {
      notify(subscriber, entries, "ledger");
    }
  };
  const settle = (edit: OptimisticEdit) => notify(handlers.current.onSettle, edit, "onSettle");
  const patchMany = (changes: ReadonlyMap<string, Partial<OptimisticEdit>>) => {
    if (changes.size === 0) {
      return;
    }
    entries = entries.map((entry) => {
      const change = changes.get(entry.id);
      return change ? { ...entry, ...change } : entry;
    });
    emit();
  };
  const patch = (id: string, change: Partial<OptimisticEdit>) => patchMany(new Map([[id, change]]));

  const revertTargets = (ids: string | string[]): OptimisticEdit[] => {
    const selected = Array.isArray(ids) ? ids : [ids];
    // Compensating writes unwind newest first. Applying 10 -> 20 and then 20 -> 30 oldest first
    // would finish at 20 while claiming both writes were reverted. Never undo beneath a newer
    // in-flight write, a newer write of unknown fate, or a newer confirmed write that was not
    // selected.
    return entries
      .filter((entry, index) =>
        selected.includes(entry.id) &&
        entry.status === "confirmed" &&
        !entries.slice(index + 1).some((later) =>
          later.rowId === entry.rowId &&
          later.field === entry.field &&
          (later.status === "pending" ||
            later.status === "reverting" ||
            later.status === "unlanded" ||
            (later.status === "confirmed" && !selected.includes(later.id)))))
      .reverse();
  };

  const markReverted = (targets: readonly OptimisticEdit[], reason: string) => {
    const revertedAt = Date.now();
    patchMany(new Map(targets.map((target) => [
      target.id,
      { status: "reverted", reason, revertedAt, revertError: undefined, revertUnlanded: undefined }
    ])));
    for (const target of targets) {
      unansweredRevertReasons.delete(target.id);
      settle({ ...target, status: "reverted", reason, revertError: undefined, revertUnlanded: undefined });
    }
  };

  return {
    get entries() {
      return entries;
    },
    get canRevert() {
      return typeof handlers.current.revert === "function";
    },
    subscribe(listener) {
      subscribers.add(listener);
      return () => {
        subscribers.delete(listener);
      };
    },
    latest(rowId, field) {
      for (let index = entries.length - 1; index >= 0; index -= 1) {
        const entry = entries[index];
        if (entry.rowId === rowId && entry.field === field && entry.status !== "dismissed") {
          return entry;
        }
      }
      return null;
    },
    forRow(rowId) {
      return entries.filter((entry) => entry.rowId === rowId);
    },
    async commit(edit) {
      const entry: OptimisticEdit = { ...edit, id: `opt-${++sequence}`, status: "pending", at: Date.now() };
      if (modelValues.get(cellKey(edit.rowId, edit.field)) === String(edit.prior)) {
        committedInSync.add(entry.id);
      }
      entries = [...entries, entry];
      emit();
      let result: void | { receipt?: string };
      try {
        result = await handlers.current.commit(entry);
      } catch (error) {
        const status = error instanceof OptimisticUnlandedError ? "unlanded" : "rejected";
        const reason = describeFailure(error, "rejected by the write path");
        patch(entry.id, { status, settledAt: Date.now(), reason });
        settle({ ...entry, status, reason });
        return;
      }
      patch(entry.id, { status: "confirmed", settledAt: Date.now(), receipt: result ? result.receipt : undefined });
      settle({ ...entry, status: "confirmed" });
    },
    revertTargets,
    async revert(ids, reason) {
      const revertHandler = handlers.current.revert;
      if (!revertHandler) {
        return;
      }
      const targets = revertTargets(ids);
      if (targets.length === 0) {
        return;
      }
      patchMany(new Map(targets.map((target) => [
        target.id,
        { status: "reverting", revertError: undefined, revertUnlanded: undefined }
      ])));
      try {
        await revertHandler(targets, reason);
      } catch (error) {
        const detail = error instanceof OptimisticRevertError ? error : null;
        const fallback = describeFailure(error, "the compensating write did not land");
        const landed = targets.filter((target) => detail?.landed.includes(target.id));
        const failures = new Map<string, Partial<OptimisticEdit>>();
        for (const target of targets) {
          if (landed.includes(target)) {
            continue;
          }
          const unanswered = detail ? detail.unlanded.includes(target.id) : error instanceof OptimisticUnlandedError;
          if (unanswered) {
            unansweredRevertReasons.set(target.id, reason);
          } else {
            unansweredRevertReasons.delete(target.id);
          }
          failures.set(target.id, {
            status: "confirmed",
            revertError: detail?.reasons[target.id] ?? fallback,
            revertUnlanded: unanswered ? true : undefined
          });
        }
        // The writes that landed are real: they are recorded as reverted, with the reason.
        patchMany(failures);
        markReverted(landed, reason);
        return;
      }
      markReverted(targets, reason);
    },
    reconcile(rowId, field, modelValue) {
      const model = String(modelValue);
      modelValues.set(cellKey(rowId, field), model);
      const live = entries.filter((entry) =>
        entry.rowId === rowId && entry.field === field && entry.status !== "dismissed");
      // While a write is in flight the model is in motion; wait for it to settle.
      if (live.length === 0 || live.some(inFlight)) {
        return;
      }
      for (let index = live.length - 1; index >= 0; index -= 1) {
        const entry = live[index];
        if (entry.status === "rejected") {
          // Nothing was written, so the entry says nothing about the model.
          continue;
        }
        if (entry.status === "unlanded") {
          // The answer never came. If the model now holds the value that was sent, it landed.
          if (index === live.length - 1 && committedInSync.has(entry.id) && model === String(entry.value)) {
            inModel.add(`${entry.id}:confirmed`);
            patch(entry.id, { status: "confirmed", reason: undefined, settledAt: Date.now() });
            settle({ ...entry, status: "confirmed", reason: undefined });
            return;
          }
          continue;
        }
        if (entry.status === "reverted") {
          const key = `${entry.id}:reverted`;
          if (!inModel.has(key)) {
            if (model === String(entry.value) && model !== String(entry.prior)) {
              // The model has not been put back yet.
              return;
            }
            inModel.add(key);
          }
          continue;
        }
        // The newest confirmed entry: what the ledger says the field holds.
        const key = `${entry.id}:confirmed`;
        if (model === String(entry.value)) {
          inModel.add(key);
          return;
        }
        if (inModel.has(key) && entry.revertUnlanded && model === String(entry.prior)) {
          // The compensating write got no answer, and the model now holds the prior: it landed.
          markReverted([entry], unansweredRevertReasons.get(entry.id) ?? "");
          return;
        }
        if (!inModel.has(key) && model === String(entry.prior)) {
          // The model has not caught up with this write yet.
          return;
        }
        // The model held this value and has moved on, or holds something this ledger never
        // wrote: another write path changed the field. This entry and everything older for the
        // field describe values the model has moved past. Reverting any of them would overwrite
        // the newer value, so they leave the ledger. Newer notes stay until they are dismissed.
        patchMany(new Map(live.slice(0, index + 1).map((stale) => [stale.id, { status: "dismissed" }])));
        return;
      }
    },
    dismiss(id) {
      patch(id, { status: "dismissed" });
    }
  };
}
