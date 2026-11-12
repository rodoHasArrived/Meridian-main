// Meridian OptimisticCell (Concrete) — the write counterpart to EditableCell.
//
// Native TypeScript port of the Meridian Design System 4.3 `components/data/OptimisticCell.jsx`
// (OptimisticProvider, OptimisticCell, OptimisticRowBar). The behaviour rules and the wording are
// the package's:
//
// - A committed edit reads "Provisional" until the provider's `commit` resolves, then "Confirmed".
// - A throw rejects it: the value falls back to the model value and the reason stays on screen
//   until the operator dismisses it. A rejection is never silent.
// - A confirmed edit stays revertible: "Revert…" on the cell, or one batch from OptimisticRowBar.
//   A revert always needs a stated reason and is its own write, never an erasure of the first.
// - State is always a word; borders and tints only repeat it. Every target is at least 24px.
//
// What the port changes, and why:
// - A provider with a real `commit` is required (see optimistic-ledger.ts). Revert controls are
//   only offered when the provider also has a `revert` write.
// - The status word sits beside the cell in a polite live region instead of inside the labelled
//   button, where assistive technology would not read it.
// - Closing the reason form returns focus to "Revert…"; confirming it, which removes "Revert…",
//   returns focus to the cell, and so does dismissing a note. A failed compensating write is
//   shown, not swallowed, and one that landed in part keeps the reverts that landed.
// - Every change of the model value is reconciled with the ledger (see `reconcile` in
//   optimistic-ledger.ts): entries the model has moved past through another write path leave the
//   ledger, so the cell never shows, and no revert ever writes over, a value the model no longer
//   holds.
// - A write that got no answer reads "Unlanded", not "Rejected": its fate is unknown
//   (ACTION_SEMANTICS A-4). The commit handler says so by throwing `OptimisticUnlandedError`.
// - Text is 12px or larger and colours come from the dashboard's semantic tokens.
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  useSyncExternalStore,
  type FormEvent,
  type HTMLAttributes,
  type ReactNode,
  type RefObject
} from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { StatusBadge, type DesignSystemSeverity } from "@/design-system/status";
import { injectStyle } from "../operations/inject-style";
import { EditableCell } from "./editable-cell";
import {
  createOptimisticLedger,
  type OptimisticEdit,
  type OptimisticEditInput,
  type OptimisticHandlers,
  type OptimisticLedger,
  type OptimisticRowId,
  type OptimisticStatus
} from "./optimistic-ledger";

export { OptimisticRevertError, OptimisticUnlandedError } from "./optimistic-ledger";
export type {
  OptimisticEdit,
  OptimisticEditInput,
  OptimisticLedger,
  OptimisticRevertErrorDetail,
  OptimisticRowId,
  OptimisticStatus
} from "./optimistic-ledger";

const CSS = `
/* The package's --cond-* condition tokens are honoured when the token bridge defines them; until
   then the same roles resolve to the dashboard's semantic colours, which follow the theme. */
.mds-opt{position:relative;display:block;min-width:0;max-width:100%;font-family:var(--font-body,system-ui,sans-serif);}
.mds-opt__line{display:flex;flex-wrap:wrap;align-items:center;gap:4px 8px;min-width:0;}
.mds-opt--right .mds-opt__line{justify-content:flex-end;}
.mds-opt__mark{display:inline-flex;flex-wrap:wrap;align-items:center;gap:4px 6px;min-width:0;}
.mds-opt__mark-note{font-size:12px;line-height:1.4;color:var(--text-muted,#606870);}
.mds-opt__prior{font-family:var(--font-data,monospace);font-size:12px;color:var(--text-muted,#606870);
  text-decoration:line-through;white-space:nowrap;}
/* The state borders repeat the state word. Each selector carries the .mds-opt root as well as the
   state class so it outranks EditableCell's own disabled and hover rules (a provisional cell is
   always disabled), whichever stylesheet was injected last. */
.mds-opt.mds-opt--pending .mds-editable__display,
.mds-opt.mds-opt--pending .mds-editable__display:hover{background:var(--cond-provisional-bg,color-mix(in srgb,var(--purple,#2F648A) 10%,transparent));
  border:1px dashed var(--cond-provisional-bd,var(--purple,#2F648A));}
.mds-opt.mds-opt--confirmed .mds-editable__display{border-left:2px solid var(--cond-confirmed-bd,var(--green,#2E7655));}
.mds-opt.mds-opt--rejected .mds-editable__display{border:1px solid var(--red,#B54B42);}
.mds-opt.mds-opt--unlanded .mds-editable__display{border:1px dotted var(--severity-action-fg,#724D11);}
.mds-opt__revert.mds-btn{min-height:24px;padding:0 6px;}
.mds-opt__detail{margin:2px 0 0;font-size:12px;line-height:1.4;color:var(--text-muted,#606870);}
.mds-opt__detail--failed{color:var(--severity-blocked-fg,#883832);font-weight:600;}
.mds-opt__note{position:absolute;z-index:6;top:100%;left:0;margin-top:4px;min-width:240px;max-width:360px;box-sizing:border-box;
  background:var(--ws-surface-raised,var(--surface-overlay-base,#FFF));border:1px solid var(--border-strong,#AAA49A);
  border-radius:var(--radius-overlay,6px);box-shadow:var(--shadow-float,0 2px 8px rgba(0,0,0,.16));padding:8px 10px;
  font-size:12px;line-height:1.45;color:var(--text-primary,#20252B);text-align:left;}
.mds-opt__note--right{left:auto;right:0;}
.mds-opt__note--inline{position:static;z-index:auto;margin-top:6px;box-shadow:none;}
.mds-opt__note--rejected{border-left:3px solid var(--red,#B54B42);}
.mds-opt__note--unlanded{border-left:3px dotted var(--severity-action-fg,#724D11);}
.mds-opt__note-h{display:flex;flex-wrap:wrap;align-items:baseline;gap:4px 8px;margin:0 0 4px;
  font-family:var(--font-data,monospace);font-size:12px;font-weight:600;color:var(--text-muted,#606870);}
.mds-opt__note-h b{color:var(--severity-blocked-fg,#883832);font-weight:700;}
.mds-opt__note--unlanded .mds-opt__note-h b{color:var(--severity-action-fg,#724D11);}
.mds-opt__note p{margin:0 0 6px;}
.mds-opt__note label{display:block;margin:0 0 4px;font-size:12px;font-weight:600;color:var(--text-muted,#606870);}
.mds-opt__note .mds-opt__scope{margin:6px 0;color:var(--text-muted,#606870);}
.mds-opt__row{display:flex;flex-wrap:wrap;gap:6px;justify-content:flex-end;align-items:center;margin-top:6px;}
.mds-optbar{position:relative;display:flex;flex-wrap:wrap;align-items:center;gap:6px 10px;min-height:34px;padding:4px 10px;
  border:1px solid var(--ws-border,var(--border-color,#D2CDC3));border-radius:var(--radius-card,4px);
  background:var(--ws-surface-raised,var(--card-surface-raised,#FCFBF8));
  font-family:var(--font-body,system-ui,sans-serif);font-size:12px;color:var(--text-secondary,#535C65);}
.mds-optbar:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
.mds-optbar__n{font-family:var(--font-data,monospace);font-weight:600;color:var(--text-primary,#20252B);font-variant-numeric:tabular-nums;}
.mds-optbar__cond{display:inline-flex;flex-wrap:wrap;gap:4px 10px;font-family:var(--font-data,monospace);}
.mds-optbar__cond span{white-space:nowrap;}
.mds-optbar__cond .p{color:var(--cond-provisional-fg,var(--purple-dim,#2F648A));}
.mds-optbar__cond .c{color:var(--cond-confirmed-fg,var(--severity-ready-fg,#225840));}
.mds-optbar__cond .r{color:var(--severity-blocked-fg,#883832);}
.mds-optbar__cond .u{color:var(--severity-action-fg,#724D11);}
.mds-optbar__spacer{flex:1;}
.mds-optbar .mds-opt__note{left:auto;right:8px;}
.mds-optbar .mds-opt__note--inline{flex-basis:100%;max-width:none;}
@media (forced-colors:active){
  .mds-opt.mds-opt--pending .mds-editable__display,
  .mds-opt.mds-opt--pending .mds-editable__display:hover{border:1px dashed CanvasText;}
  .mds-opt.mds-opt--confirmed .mds-editable__display{border-left:2px solid CanvasText;}
  .mds-opt.mds-opt--rejected .mds-editable__display{border:2px solid Mark;}
  .mds-opt.mds-opt--unlanded .mds-editable__display{border:2px dotted CanvasText;}
  .mds-opt__note,.mds-optbar{border-color:CanvasText;}
}
`;

interface OptimisticContextValue {
  ledger: OptimisticLedger;
  reasonDisclosure: string | null;
}

const OptimisticContext = createContext<OptimisticContextValue | null>(null);

export interface OptimisticProviderProps {
  /**
   * The write. Resolve to confirm (optionally `{ receipt }`); throw with the reason to reject;
   * throw an `OptimisticUnlandedError` when no answer came back and the fate is unknown.
   */
  commit: OptimisticHandlers["commit"];
  /**
   * The compensating write for one or many confirmed edits, newest first, under one reason.
   * Keep that order when applying the priors. Without it no revert control is offered. When some
   * of a batch landed before a failure, throw an `OptimisticRevertError` naming those.
   */
  revert?: OptimisticHandlers["revert"];
  /** Fires on confirmed, rejected, unlanded and reverted; the parent updates its model here if it keeps one. */
  onSettle?: OptimisticHandlers["onSettle"];
  /** Observes ledger changes. A synchronous error is logged and interrupts nothing. */
  onChange?: (entries: readonly OptimisticEdit[]) => void;
  /**
   * Shown under every reason field and beside every stored revert reason: where the stated reason
   * is kept. Say so plainly when the write path has no reason or audit field.
   */
  reasonDisclosure?: string;
  children?: ReactNode;
}

/**
 * OptimisticProvider — owns one edit ledger for the cells and row bars beneath it.
 *
 * @example
 * <OptimisticProvider commit={api.patch} revert={api.revertBatch}>
 *   <OptimisticRowBar rowIds={selected} label="edits in selection" />
 *   <OptimisticCell rowId={row.id} field="limit" value={row.limit} mode="number" />
 * </OptimisticProvider>
 */
export function OptimisticProvider({ commit, revert, onSettle, onChange, reasonDisclosure, children }: OptimisticProviderProps) {
  const handlers = useRef<OptimisticHandlers>({ commit, revert, onSettle });
  const observer = useRef(onChange);
  // The ledger is created once; it reads the latest callbacks through the ref.
  useLayoutEffect(() => {
    handlers.current = { commit, revert, onSettle };
    observer.current = onChange;
  });
  const [ledger] = useState(() => createOptimisticLedger(handlers));
  useEffect(() => ledger.subscribe((entries) => observer.current?.(entries)), [ledger]);
  const context = useMemo<OptimisticContextValue>(
    () => ({ ledger, reasonDisclosure: reasonDisclosure?.trim() || null }),
    [ledger, reasonDisclosure]
  );
  return <OptimisticContext.Provider value={context}>{children}</OptimisticContext.Provider>;
}

function useOptimisticContext(caller: string): OptimisticContextValue {
  const context = useContext(OptimisticContext);
  if (!context) {
    // The package falls back to a private ledger that confirms at once. On a real screen that
    // would report a write nobody made, so the port refuses to render without a transport.
    throw new Error(`${caller} must be rendered inside an OptimisticProvider with a commit handler.`);
  }
  const { ledger } = context;
  useSyncExternalStore(ledger.subscribe, () => ledger.entries, () => ledger.entries);
  return context;
}

/** The provider's ledger. The caller re-renders on every ledger change. */
export function useOptimisticLedger(): OptimisticLedger {
  return useOptimisticContext("useOptimisticLedger").ledger;
}

/** The package's name for {@link useOptimisticLedger}. Not React's own `useOptimistic`. */
export { useOptimisticLedger as useOptimistic };

type VisibleStatus = Exclude<OptimisticStatus, "dismissed">;

const STATUS_LABEL: Record<VisibleStatus, string> = {
  pending: "Provisional",
  confirmed: "Confirmed",
  rejected: "Rejected",
  unlanded: "Unlanded",
  reverting: "Reverting",
  reverted: "Reverted"
};

// Tone only repeats the word; the word is the state.
const STATUS_SEVERITY: Record<VisibleStatus, DesignSystemSeverity> = {
  pending: "info",
  confirmed: "ready",
  rejected: "blocked",
  unlanded: "action",
  reverting: "review",
  reverted: "action"
};

export type OptimisticNotePlacement = "overlay" | "inline";

function noteClassName(placement: OptimisticNotePlacement, right: boolean, extra = ""): string {
  return [
    "mds-opt__note",
    right ? "mds-opt__note--right" : "",
    placement === "inline" ? "mds-opt__note--inline" : "",
    extra
  ].filter(Boolean).join(" ");
}

/**
 * Opens and closes a reason form, and puts focus back when it closes: on the control that opened
 * it, or on `fallback` when that control is gone or disabled by then (a confirmed revert removes
 * the cell's "Revert…" and can leave the batch button with nothing to revert).
 */
function useReasonForm(fallback: RefObject<HTMLElement | null>) {
  const [asking, setAsking] = useState(false);
  const opener = useRef<HTMLButtonElement | null>(null);
  const returnFocus = useRef(false);
  useEffect(() => {
    if (asking || !returnFocus.current) {
      return;
    }
    returnFocus.current = false;
    const target = opener.current && !opener.current.disabled ? opener.current : fallback.current;
    target?.focus();
  }, [asking, fallback]);
  const open = useCallback(() => setAsking(true), []);
  const close = useCallback((takeFocusBack = true) => {
    returnFocus.current = takeFocusBack;
    setAsking(false);
  }, []);
  return { asking, opener, open, close };
}

interface ReasonFormProps {
  title: string;
  prompt: ReactNode;
  confirmLabel: string;
  disclosure: string | null;
  /** Why the revert cannot be sent right now. The form stays open and keeps what was typed. */
  blockedReason: string | null;
  placement: OptimisticNotePlacement;
  right: boolean;
  onConfirm: (reason: string) => void;
  onCancel: () => void;
}

/** The reason form shared by the cell and the row bar. Non-modal: no focus trap, no backdrop. */
function ReasonForm({
  title,
  prompt,
  confirmLabel,
  disclosure,
  blockedReason,
  placement,
  right,
  onConfirm,
  onCancel
}: ReasonFormProps) {
  const [reason, setReason] = useState("");
  const id = useId();
  const input = useRef<HTMLInputElement | null>(null);
  useEffect(() => {
    input.current?.focus();
  }, []);
  const stated = reason.trim();
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    // Enter in the reason field submits the form even while the button is disabled.
    if (stated && !blockedReason) {
      onConfirm(stated);
    }
  };
  return (
    // The dialog role sits on a wrapper: ARIA does not allow it on the form element itself.
    <div
      className={noteClassName(placement, right)}
      role="dialog"
      aria-label={title}
      onKeyDown={(event) => {
        if (event.key === "Escape") {
          // Only this form closes; an enclosing drawer or dialog stays open.
          event.stopPropagation();
          onCancel();
        }
      }}
    >
      <form onSubmit={submit}>
        <div className="mds-opt__note-h">{title}</div>
        <p>{prompt}</p>
        <label htmlFor={`${id}-reason`}>Reason · required</label>
        <Input
          id={`${id}-reason`}
          ref={input}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          placeholder="Why this goes back"
          autoComplete="off"
          aria-required="true"
          aria-describedby={disclosure ? `${id}-scope` : undefined}
        />
        {disclosure ? <p id={`${id}-scope`} className="mds-opt__detail mds-opt__scope">{disclosure}</p> : null}
        <div className="mds-opt__row">
          <Button type="button" size="sm" variant="outline" onClick={onCancel}>Cancel</Button>
          <Button
            type="submit"
            size="sm"
            disabled={!stated || Boolean(blockedReason)}
            disabledReason={blockedReason ?? "State a reason to revert."}
          >
            {confirmLabel}
          </Button>
        </div>
      </form>
    </div>
  );
}

export interface OptimisticCellProps {
  rowId: OptimisticRowId;
  field: string;
  /**
   * The model's committed value: shown whenever no edit is provisional, confirmed or reverting.
   * Once a write lands it must read, as text, exactly like the draft that was committed: the
   * ledger compares the two to tell its own writes from another write path's.
   */
  value: string | number | null | undefined;
  /** Display formatter. Must return text: the result is also used in accessible names. */
  format?: (value: unknown) => string;
  /** What the field is called in accessible names, e.g. "Alpaca priority". @default `field` */
  label?: string;
  /** @default "left" */
  align?: "left" | "right";
  validator?: (draft: string) => true | string;
  /** @default "text" */
  mode?: "text" | "number";
  hint?: string;
  placeholder?: string;
  /** @default false */
  disabled?: boolean;
  /** Show the struck-through prior beside a provisional or confirmed value. @default true */
  showConfirmed?: boolean;
  /**
   * Said beside "Confirmed". Use it when a confirmed write is stored but not yet in effect, so
   * the word cannot be read as "applied".
   */
  confirmedNote?: string;
  /**
   * Set while a confirmed edit must not be reverted, for example while another write to the same
   * record is in flight. "Revert…" and the reason form's confirm are disabled with this as the reason.
   */
  revertBlockedReason?: string | null;
  /** `overlay` floats notes under the cell (the package layout); `inline` keeps them in flow. @default "overlay" */
  notePlacement?: OptimisticNotePlacement;
  className?: string;
  /** Observe the commit before it reaches the ledger. A failure here is logged and does not stop the write. */
  onCommit?: (edit: OptimisticEditInput) => void;
}

/**
 * OptimisticCell — an editable value whose write is shown honestly while it is in flight.
 *
 * @example
 * <OptimisticCell rowId={row.id} field="budget" value={row.budget} mode="number" validator={wholeNumber} />
 */
export function OptimisticCell({
  rowId,
  field,
  value,
  format,
  label,
  align = "left",
  validator,
  mode = "text",
  hint = "",
  placeholder = "",
  disabled = false,
  showConfirmed = true,
  confirmedNote,
  revertBlockedReason = null,
  notePlacement = "overlay",
  className = "",
  onCommit
}: OptimisticCellProps) {
  injectStyle("optimistic-cell", CSS);
  const { ledger, reasonDisclosure } = useOptimisticContext("OptimisticCell");
  const cell = useRef<HTMLDivElement | null>(null);
  const reasonForm = useReasonForm(cell);
  const entry = ledger.latest(rowId, field);
  const status: VisibleStatus | "idle" = entry && entry.status !== "dismissed" ? entry.status : "idle";
  const holdsLedgerValue = status === "pending" || status === "confirmed" || status === "reverting";
  const showing = entry && holdsLedgerValue ? entry.value : value;
  const text = (candidate: unknown) =>
    format ? format(candidate) : candidate == null || candidate === "" ? "—" : String(candidate);
  const name = label ?? field;
  // Any write in flight for this field holds the editor, not only the newest entry's: an older
  // edit can be reverting beneath a newer note, and a second write would race it.
  const writing = ledger.entries.some((candidate) =>
    candidate.rowId === rowId &&
    candidate.field === field &&
    (candidate.status === "pending" || candidate.status === "reverting"));
  const editable = !disabled && !writing;
  const right = align === "right";
  const showsPrior = Boolean(entry) && (status === "pending" || status === "confirmed") && showConfirmed;

  // The ledger stands for the model value only while the model agrees with it. Every entry of
  // this field is checked against the model on each change of either, not just the one on screen:
  // an older confirmed entry beneath a note would otherwise stay revertible after another write
  // path (or a refresh that found a newer value) had moved the field on.
  const entries = ledger.entries;
  useEffect(() => {
    ledger.reconcile(rowId, field, value);
  }, [ledger, entries, rowId, field, value]);

  // The reason form belongs to the confirmed entry it was opened on. If that entry stops being
  // the confirmed value (a batch revert, another write path), the form closes with it rather
  // than reopening later on a different entry.
  const { asking: askingReason, close: closeReasonForm } = reasonForm;
  useEffect(() => {
    if (askingReason && status !== "confirmed") {
      // The form is already gone. Take focus back only if it took focus down with it, not when
      // the operator is working somewhere else (the row bar's own form, for one).
      const active = document.activeElement;
      closeReasonForm(active === null || active === document.body);
    }
  }, [askingReason, status, closeReasonForm]);

  // The note and its Dismiss button unmount together; keep the keyboard user's place on the cell.
  const dismissNote = (id: string) => {
    ledger.dismiss(id);
    cell.current?.focus();
  };

  const commit = (draft: string) => {
    if (String(draft) === String(showing)) {
      return;
    }
    const edit: OptimisticEditInput = { rowId, field, prior: showing, value: draft };
    try {
      onCommit?.(edit);
    } catch (error) {
      console.error("OptimisticCell: onCommit observer failed", error);
    }
    void ledger.commit(edit);
  };

  return (
    <div
      className={`mds-opt mds-opt--${status} ${right ? "mds-opt--right" : ""} ${className}`.replace(/\s+/g, " ").trim()}
      data-status={status}
    >
      <div className="mds-opt__line">
        <EditableCell
          value={showing == null ? null : String(showing)}
          onCommit={commit}
          validator={validator}
          mode={mode}
          hint={hint}
          placeholder={placeholder}
          disabled={!editable}
          ariaLabel={`Edit ${name}: ${text(showing)}${entry && showsPrior ? `, was ${text(entry.prior)}` : ""}`}
          displayRef={cell}
        >
          {entry && showsPrior ? <span className="mds-opt__prior">{text(entry.prior)}</span> : null}
          <span>{text(showing)}</span>
        </EditableCell>
        <span className="mds-opt__mark" aria-live="polite">
          {status !== "idle" ? (
            <StatusBadge status={STATUS_SEVERITY[status]} label={STATUS_LABEL[status]} />
          ) : null}
          {status === "confirmed" && confirmedNote ? <span className="mds-opt__mark-note">{confirmedNote}</span> : null}
        </span>
        {entry && status === "confirmed" && ledger.canRevert && !reasonForm.asking ? (
          <Button
            ref={reasonForm.opener}
            size="sm"
            variant="ghost"
            className="mds-opt__revert"
            disabled={Boolean(revertBlockedReason)}
            disabledReason={revertBlockedReason}
            onClick={reasonForm.open}
            aria-label={`Revert ${name} to ${text(entry.prior)}`}
          >
            Revert…
          </Button>
        ) : null}
      </div>
      {entry && status === "confirmed" && entry.revertError ? (
        <p className="mds-opt__detail mds-opt__detail--failed" role="alert">
          {entry.revertUnlanded
            ? "Revert not confirmed. "
            : `Revert not applied; the value is still ${text(entry.value)}. `}
          {entry.revertError}
        </p>
      ) : null}
      {entry && status === "confirmed" && reasonForm.asking ? (
        <ReasonForm
          title="Revert this cell"
          confirmLabel="Revert cell"
          disclosure={reasonDisclosure}
          blockedReason={revertBlockedReason}
          placement={notePlacement}
          right={right}
          prompt={
            <>
              Puts <b>{text(entry.value)}</b> back to <b>{text(entry.prior)}</b>. The revert is sent as its own
              write, not as an erasure of the first.
            </>
          }
          onCancel={() => reasonForm.close()}
          onConfirm={(reason) => {
            reasonForm.close();
            void ledger.revert(entry.id, reason);
          }}
        />
      ) : null}
      {entry && status === "rejected" ? (
        <div className={noteClassName(notePlacement, right, "mds-opt__note--rejected")} role="status">
          <div className="mds-opt__note-h">
            <b>Rejected</b>
            <span>{text(entry.value)} → back to {text(entry.prior)}</span>
          </div>
          <p>{entry.reason}</p>
          <div className="mds-opt__row">
            <Button size="sm" variant="outline" onClick={() => dismissNote(entry.id)}>Dismiss</Button>
          </div>
        </div>
      ) : null}
      {entry && status === "unlanded" ? (
        // Not "back to": whether the value changed is exactly what is not known.
        <div className={noteClassName(notePlacement, right, "mds-opt__note--unlanded")} role="status">
          <div className="mds-opt__note-h">
            <b>Unlanded</b>
            <span>{text(entry.value)} sent · no answer</span>
          </div>
          <p>{entry.reason}</p>
          <div className="mds-opt__row">
            <Button size="sm" variant="outline" onClick={() => dismissNote(entry.id)}>Dismiss</Button>
          </div>
        </div>
      ) : null}
      {entry && status === "reverted" ? (
        <div className={noteClassName(notePlacement, right)} role="status">
          <div className="mds-opt__note-h">
            Reverted
            <span>{text(entry.value)} → {text(entry.prior)}</span>
          </div>
          <p>{entry.reason}</p>
          {reasonDisclosure ? <p className="mds-opt__scope">{reasonDisclosure}</p> : null}
          <div className="mds-opt__row">
            <Button size="sm" variant="outline" onClick={() => dismissNote(entry.id)}>Dismiss</Button>
          </div>
        </div>
      ) : null}
    </div>
  );
}

export interface OptimisticRowBarProps extends HTMLAttributes<HTMLDivElement> {
  /** One row, the current selection, or omit for the whole ledger. */
  rowIds?: OptimisticRowId | OptimisticRowId[];
  /** @default "edits on this row" */
  label?: string;
  /** Render the bar with this text when there are no edits. By default nothing is rendered. */
  emptyLabel?: ReactNode;
  /**
   * Set while no confirmed edit in scope may be reverted, for example while another write to one
   * of the records is in flight. The batch control and its confirm are disabled with this reason.
   */
  revertBlockedReason?: string | null;
  /** `overlay` floats the reason form under the bar (the package layout); `inline` keeps it in flow. @default "overlay" */
  notePlacement?: OptimisticNotePlacement;
  children?: ReactNode;
}

/**
 * OptimisticRowBar — the tally of provisional, confirmed, rejected and reverted edits for the
 * rows, plus one batch "Revert N confirmed…" under a single stated reason.
 */
export function OptimisticRowBar({
  rowIds,
  label = "edits on this row",
  emptyLabel = null,
  revertBlockedReason = null,
  notePlacement = "overlay",
  className = "",
  children,
  ...rest
}: OptimisticRowBarProps) {
  injectStyle("optimistic-cell", CSS);
  const { ledger, reasonDisclosure } = useOptimisticContext("OptimisticRowBar");
  const bar = useRef<HTMLDivElement | null>(null);
  const reasonForm = useReasonForm(bar);
  const ids = Array.isArray(rowIds) ? rowIds : rowIds == null ? null : [rowIds];
  const scoped = ids ? ledger.entries.filter((entry) => ids.includes(entry.rowId)) : ledger.entries;
  const live = scoped.filter((entry) => entry.status !== "dismissed");
  const count = (status: OptimisticStatus) => live.filter((entry) => entry.status === status).length;
  const confirmed = live.filter((entry) => entry.status === "confirmed");
  // What one batch would write: a confirmed edit beneath a newer provisional or unlanded edit of
  // the same field waits, so the control counts only the ones it would revert.
  const revertible = ledger.revertTargets(confirmed.map((entry) => entry.id));
  const batchBlockedReason = revertBlockedReason
    ?? (confirmed.length === 0
      ? "No confirmed edit to revert. Provisional edits settle first."
      : revertible.length === 0
        ? "Every confirmed edit here sits beneath a newer provisional or unlanded edit of the same field. That one settles or is dismissed first."
        : null);
  const classes = `mds-optbar ${className}`.trim();

  if (live.length === 0) {
    return emptyLabel ? (
      <div className={classes} {...rest}>
        <span>{emptyLabel}</span>
        {children}
      </div>
    ) : null;
  }

  return (
    <div ref={bar} tabIndex={-1} className={classes} role="group" aria-label={`Edit ledger · ${label}`} {...rest}>
      <span>
        <span className="mds-optbar__n">{live.length}</span> {label}
      </span>
      <span className="mds-optbar__cond">
        {count("pending") > 0 ? <span className="p">{count("pending")} provisional</span> : null}
        {count("confirmed") > 0 ? <span className="c">{count("confirmed")} confirmed</span> : null}
        {count("rejected") > 0 ? <span className="r">{count("rejected")} rejected</span> : null}
        {count("unlanded") > 0 ? <span className="u">{count("unlanded")} unlanded</span> : null}
        {count("reverted") > 0 ? <span className="r">{count("reverted")} reverted</span> : null}
      </span>
      <span className="mds-optbar__spacer" />
      {children}
      {ledger.canRevert ? (
        <Button
          ref={reasonForm.opener}
          size="sm"
          variant="outline"
          disabled={Boolean(batchBlockedReason)}
          disabledReason={batchBlockedReason}
          onClick={reasonForm.open}
        >
          Revert {revertible.length ? `${revertible.length} confirmed` : "row"}…
        </Button>
      ) : null}
      {reasonForm.asking ? (
        <ReasonForm
          title={`Revert ${revertible.length} confirmed edit${revertible.length === 1 ? "" : "s"}`}
          confirmLabel="Revert batch"
          disclosure={reasonDisclosure}
          blockedReason={batchBlockedReason}
          placement={notePlacement}
          right
          prompt={
            <>
              Every confirmed cell in {ids ? `${ids.length} row${ids.length === 1 ? "" : "s"}` : "this ledger"} goes back
              to its prior value under one reason. Provisional edits are left to settle first.
            </>
          }
          onCancel={() => reasonForm.close()}
          onConfirm={(reason) => {
            reasonForm.close();
            void ledger.revert(revertible.map((entry) => entry.id), reason);
          }}
        />
      ) : null}
    </div>
  );
}
