// Meridian OptimisticCell — the write counterpart to EditableCell (UX-depth track B-2).
// A committed edit renders PROVISIONAL (an existing data condition — no new vocabulary) until the
// write confirms; a rejection reverts the value WITH THE REASON ATTACHED, never silently; and a
// confirmed edit stays reversible by the operator — cell by cell, or a whole row in one batch —
// with a stated reason. The ledger lives in OptimisticProvider so a row bar can act on many cells.
import React from "react";
import { EditableCell } from "./EditableCell";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "optimistic-cell");
  el.textContent = `
.mds-opt{position:relative;display:block;width:100%;min-width:0;}
.mds-opt__val{display:inline-flex;align-items:baseline;gap:6px;min-width:0;max-width:100%;}
.mds-opt__val--right{justify-content:flex-end;width:100%;}
.mds-opt__mark{font:600 10px var(--font-data);letter-spacing:.02em;white-space:nowrap;}
.mds-opt--pending .mds-opt__mark{color:var(--cond-provisional-fg,#2F648A);}
.mds-opt--confirmed .mds-opt__mark{color:var(--cond-confirmed-fg,#2E7655);}
.mds-opt--rejected .mds-opt__mark,.mds-opt--reverted .mds-opt__mark{color:var(--red,#B54B42);}
.mds-opt--pending .mds-editable__display{background:var(--cond-provisional-bg,transparent);border:1px dashed var(--cond-provisional-bd,#2F648A);}
.mds-opt--confirmed .mds-editable__display{border-left:2px solid var(--cond-confirmed-bd,#2E7655);}
.mds-opt--rejected .mds-editable__display{border:1px solid var(--red,#B54B42);}
.mds-opt__prior{font:11px var(--font-data);color:var(--text-muted,#606870);text-decoration:line-through;white-space:nowrap;}
.mds-opt__revert{background:none;border:1px solid transparent;border-radius:2px;padding:0 4px;min-height:24px;
  font:11px var(--font-body);color:var(--link,#2A5F96);cursor:pointer;opacity:0;transition:opacity 120ms cubic-bezier(.2,0,0,1);white-space:nowrap;}
.mds-opt:hover .mds-opt__revert,.mds-opt:focus-within .mds-opt__revert{opacity:1;}
.mds-opt__revert:hover{text-decoration:underline;color:var(--link-hover,#1F4A76);}
.mds-opt__revert:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;opacity:1;}
.mds-opt__note{position:absolute;z-index:6;top:100%;left:0;margin-top:4px;min-width:240px;max-width:360px;box-sizing:border-box;
  background:var(--surface-overlay-base,#FFF);border:1px solid var(--border-strong,#AAA49A);border-radius:var(--radius-overlay,6px);
  box-shadow:0 2px 8px rgba(0,0,0,.16);padding:8px 10px;font:12px var(--font-body);color:var(--text-primary,#20252B);text-align:left;}
.mds-opt__note--right{left:auto;right:0;}
.mds-opt__note--rejected{border-left:3px solid var(--red,#B54B42);}
.mds-opt__note-h{display:flex;align-items:baseline;gap:8px;font:600 10px var(--font-data);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870);margin-bottom:4px;}
.mds-opt__note-h b{color:var(--red,#B54B42);font-weight:600;}
.mds-opt__note p{margin:0 0 6px;line-height:1.45;}
.mds-opt__reason{width:100%;box-sizing:border-box;height:var(--theme-control-height,28px);padding:4px 8px;margin:4px 0 6px;
  border:1px solid var(--border,#D2CDC3);border-radius:2px;background:var(--bg-light,#F8F7F3);color:var(--text-primary,#20252B);font:12px var(--font-body);}
.mds-opt__reason:focus{border-color:var(--interaction-focus,#2D6FB3);outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px;}
.mds-opt__row{display:flex;gap:6px;justify-content:flex-end;align-items:center;}
.mds-opt__btn{min-height:24px;padding:0 10px;border-radius:var(--radius-button,4px);border:1px solid var(--border-strong,#AAA49A);background:var(--bg-light,#F8F7F3);color:var(--text-primary,#20252B);font:600 12px var(--font-body);cursor:pointer;}
.mds-opt__btn:hover{background:var(--bg-hover,#EDEFF0);}
.mds-opt__btn:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
.mds-opt__btn--primary{background:var(--brand-axis,#29323A);border-color:var(--brand-axis,#29323A);color:var(--ink-inverse,#F5F3EE);}
.mds-opt__btn--primary:hover{background:var(--brand-axis,#29323A);opacity:.92;}
.mds-opt__btn[disabled]{opacity:.5;cursor:default;}
.mds-optbar{display:flex;align-items:center;gap:10px;min-height:var(--theme-row-review,var(--theme-control-height,34px));padding:0 10px;
  border:1px solid var(--border,#D2CDC3);background:var(--card-surface-raised,#FCFBF8);font:12px var(--font-body);color:var(--text-secondary,#535C65);position:relative;}
.mds-optbar__n{font:600 12px var(--font-data);color:var(--text-primary,#20252B);font-variant-numeric:tabular-nums;}
.mds-optbar__cond{display:inline-flex;gap:10px;font:11px var(--font-data);}
.mds-optbar__cond i{font-style:normal;}
.mds-optbar__cond .p{color:var(--cond-provisional-fg,#2F648A);}
.mds-optbar__cond .c{color:var(--cond-confirmed-fg,#2E7655);}
.mds-optbar__cond .r{color:var(--red,#B54B42);}
.mds-optbar__spacer{flex:1;}
.mds-optbar .mds-opt__note{top:100%;left:auto;right:8px;}
`;
  document.head.appendChild(el);
}

/* ── ledger store — one per provider, or one private to a stray cell ── */
let seq = 0;
function createLedger(handlers = { current: {} }) {
  const h = () => handlers.current || {};
  let entries = [];
  const subs = new Set();
  const emit = () => subs.forEach((f) => f(entries));
  const patch = (id, fn) => { entries = entries.map((e) => (e.id === id ? { ...e, ...fn(e) } : e)); emit(); };
  const api = {
    subscribe(f) { subs.add(f); return () => subs.delete(f); },
    get entries() { return entries; },
    latest(rowId, field) { for (let i = entries.length - 1; i >= 0; i--) { const e = entries[i]; if (e.rowId === rowId && e.field === field && e.status !== "dismissed") return e; } return null; },
    forRow(rowId) { return entries.filter((e) => e.rowId === rowId); },
    async commit(edit) {
      const e = { id: `opt-${++seq}`, status: "pending", at: Date.now(), ...edit };
      entries = [...entries, e]; emit();
      try {
        const r = h().commit ? await h().commit(e) : undefined;
        patch(e.id, () => ({ status: "confirmed", settledAt: Date.now(), receipt: r && r.receipt }));
        h().onSettle && h().onSettle({ ...e, status: "confirmed" });
      } catch (err) {
        const reason = (err && err.message) || String(err || "rejected by the write path");
        patch(e.id, () => ({ status: "rejected", settledAt: Date.now(), reason }));
        h().onSettle && h().onSettle({ ...e, status: "rejected", reason });
      }
    },
    async revert(ids, reason) {
      const list = Array.isArray(ids) ? ids : [ids];
      const targets = entries.filter((e) => list.includes(e.id) && e.status === "confirmed");
      if (!targets.length) return;
      targets.forEach((t) => patch(t.id, () => ({ status: "reverting" })));
      try {
        if (h().revert) await h().revert(targets, reason);
        targets.forEach((t) => { patch(t.id, () => ({ status: "reverted", reason, revertedAt: Date.now() })); h().onSettle && h().onSettle({ ...t, status: "reverted", reason }); });
      } catch (err) {
        targets.forEach((t) => patch(t.id, () => ({ status: "confirmed", revertError: (err && err.message) || String(err) })));
      }
    },
    dismiss(id) { patch(id, () => ({ status: "dismissed" })); },
  };
  return api;
}

const Ctx = React.createContext(null);

export function OptimisticProvider({ commit, revert, onSettle, onChange, children }) {
  const handlers = React.useRef({});
  handlers.current = { commit, revert, onSettle };
  const ref = React.useRef(null);
  if (!ref.current) ref.current = createLedger(handlers);
  const [, force] = React.useState(0);
  React.useEffect(() => ref.current.subscribe((entries) => { force((n) => n + 1); onChange && onChange(entries); }), [onChange]);
  return <Ctx.Provider value={ref.current}>{children}</Ctx.Provider>;
}

function useLedger() {
  const ctx = React.useContext(Ctx);
  const local = React.useRef(null);
  if (!ctx && !local.current) local.current = createLedger();
  const ledger = ctx || local.current;
  const [, force] = React.useState(0);
  React.useEffect(() => ledger.subscribe(() => force((n) => n + 1)), [ledger]);
  return ledger;
}
export function useOptimistic() { return useLedger(); }
/** Bundle carrier — the compiler exposes only capitalized exports. */
export const OptimisticHooks = { useOptimistic };

const STATUS_LABEL = { pending: "Provisional", confirmed: "Confirmed", rejected: "Rejected", reverting: "Reverting", reverted: "Reverted" };

/* ── the reason form — shared by cell and bar ── */
function ReasonForm({ title, prompt, onConfirm, onCancel, confirmLabel = "Revert", right }) {
  const [reason, setReason] = React.useState("");
  const uid = React.useId();
  const ref = React.useRef(null);
  React.useEffect(() => { ref.current && ref.current.focus(); }, []);
  return (
    <form className={`mds-opt__note ${right ? "mds-opt__note--right" : ""}`} role="dialog" aria-label={title}
      onSubmit={(e) => { e.preventDefault(); if (reason.trim()) onConfirm(reason.trim()); }}
      onKeyDown={(e) => { if (e.key === "Escape") { e.stopPropagation(); onCancel(); } }}>
      <div className="mds-opt__note-h">{title}</div>
      <p>{prompt}</p>
      <label htmlFor={`${uid}-r`} style={{ font: "600 10px var(--font-data)", color: "var(--text-muted)" }}>Reason · required</label>
      <input id={`${uid}-r`} ref={ref} className="mds-opt__reason" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Why this goes back" />
      <div className="mds-opt__row">
        <button type="button" className="mds-opt__btn" onClick={onCancel}>Cancel</button>
        <button type="submit" className="mds-opt__btn mds-opt__btn--primary" disabled={!reason.trim()}>{confirmLabel}</button>
      </div>
    </form>
  );
}

export function OptimisticCell({
  rowId, field, value, format, align = "left", validator, mode = "text", hint = "", placeholder = "", disabled = false,
  showConfirmed = true, className = "", onCommit,
}) {
  inject();
  const ledger = useLedger();
  const [asking, setAsking] = React.useState(false);
  const revertRef = React.useRef(null);
  const returnFocus = React.useRef(false);
  const closeAsk = () => { returnFocus.current = true; setAsking(false); };
  React.useEffect(() => { if (!asking && returnFocus.current) { returnFocus.current = false; revertRef.current && revertRef.current.focus(); } }, [asking]);
  const entry = ledger.latest(rowId, field);
  const status = entry ? entry.status : "idle";
  const showing = entry && (status === "pending" || status === "confirmed" || status === "reverting") ? entry.value : value;
  const fmt = (v) => (format ? format(v) : v == null || v === "" ? "—" : String(v));
  const editable = !disabled && status !== "pending" && status !== "reverting";
  const right = align === "right";

  const commit = (draft) => {
    if (String(draft) === String(showing)) return;
    onCommit && onCommit({ rowId, field, prior: showing, value: draft });
    ledger.commit({ rowId, field, prior: showing, value: draft });
  };

  return (
    <div className={`mds-opt mds-opt--${status} ${className}`} data-status={status}>
      <EditableCell value={showing} onCommit={commit} validator={validator} mode={mode} hint={hint} placeholder={placeholder} disabled={!editable}>
        <span className={`mds-opt__val ${right ? "mds-opt__val--right" : ""}`}>
          {(status === "pending" || status === "confirmed") && showConfirmed && <span className="mds-opt__prior" aria-label={`was ${fmt(entry.prior)}`}>{fmt(entry.prior)}</span>}
          <span>{fmt(showing)}</span>
          {status !== "idle" && status !== "dismissed" && <span className="mds-opt__mark" aria-live="polite">{STATUS_LABEL[status]}</span>}
        </span>
      </EditableCell>
      {status === "confirmed" && !asking && (
        <div style={{ display: "flex", justifyContent: right ? "flex-end" : "flex-start", marginTop: -2 }}>
          <button type="button" ref={revertRef} className="mds-opt__revert" onClick={() => setAsking(true)} aria-label={`Revert ${field} to ${fmt(entry.prior)}`}>Revert…</button>
        </div>
      )}
      {asking && (
        <ReasonForm title="Revert this cell" right={right} confirmLabel="Revert cell"
          prompt={<>Puts <b>{fmt(entry.value)}</b> back to <b>{fmt(entry.prior)}</b>. The revert is recorded as its own write, not as an erasure of the first.</>}
          onCancel={closeAsk} onConfirm={(r) => { closeAsk(); ledger.revert(entry.id, r); }} />
      )}
      {status === "rejected" && (
        <div className={`mds-opt__note mds-opt__note--rejected ${right ? "mds-opt__note--right" : ""}`} role="status">
          <div className="mds-opt__note-h"><b>Rejected</b> <span>{fmt(entry.value)} → back to {fmt(entry.prior)}</span></div>
          <p>{entry.reason}</p>
          <div className="mds-opt__row"><button type="button" className="mds-opt__btn" onClick={() => ledger.dismiss(entry.id)}>Dismiss</button></div>
        </div>
      )}
      {status === "reverted" && (
        <div className={`mds-opt__note ${right ? "mds-opt__note--right" : ""}`} role="status">
          <div className="mds-opt__note-h">Reverted <span>{fmt(entry.value)} → {fmt(entry.prior)}</span></div>
          <p>{entry.reason}</p>
          <div className="mds-opt__row"><button type="button" className="mds-opt__btn" onClick={() => ledger.dismiss(entry.id)}>Dismiss</button></div>
        </div>
      )}
    </div>
  );
}

/** Row-batch revert. `rowIds` — one row, or the current selection. Renders nothing when the rows carry no edits. */
export function OptimisticRowBar({ rowIds, label = "edits on this row", emptyLabel = null, className = "", children, ...rest }) {
  inject();
  const ledger = useLedger();
  const [asking, setAsking] = React.useState(false);
  const revertRef = React.useRef(null);
  const returnFocus = React.useRef(false);
  const closeAsk = () => { returnFocus.current = true; setAsking(false); };
  React.useEffect(() => { if (!asking && returnFocus.current) { returnFocus.current = false; revertRef.current && revertRef.current.focus(); } }, [asking]);
  const ids = Array.isArray(rowIds) ? rowIds : rowIds == null ? null : [rowIds];
  const all = ids ? ledger.entries.filter((e) => ids.includes(e.rowId)) : ledger.entries;
  const live = all.filter((e) => e.status !== "dismissed");
  const n = (s) => live.filter((e) => e.status === s).length;
  const confirmed = live.filter((e) => e.status === "confirmed");
  if (!live.length) return emptyLabel ? <div className={`mds-optbar ${className}`} {...rest}><span>{emptyLabel}</span>{children}</div> : null;
  return (
    <div className={`mds-optbar ${className}`} role="group" aria-label={`Edit ledger · ${label}`} {...rest}>
      <span><span className="mds-optbar__n">{live.length}</span> {label}</span>
      <span className="mds-optbar__cond">
        {n("pending") > 0 && <i className="p">{n("pending")} provisional</i>}
        {n("confirmed") > 0 && <i className="c">{n("confirmed")} confirmed</i>}
        {n("rejected") > 0 && <i className="r">{n("rejected")} rejected</i>}
        {n("reverted") > 0 && <i className="r">{n("reverted")} reverted</i>}
      </span>
      <span className="mds-optbar__spacer" />
      {children}
      <button type="button" ref={revertRef} className="mds-opt__btn" disabled={!confirmed.length} onClick={() => setAsking(true)}>
        Revert {confirmed.length ? `${confirmed.length} confirmed` : "row"}…
      </button>
      {asking && (
        <ReasonForm title={`Revert ${confirmed.length} confirmed edit${confirmed.length === 1 ? "" : "s"}`} confirmLabel="Revert batch" right
          prompt={<>Every confirmed cell in {ids ? `${ids.length} row${ids.length === 1 ? "" : "s"}` : "this ledger"} goes back to its prior value under one reason. Provisional edits are left to settle first.</>}
          onCancel={closeAsk} onConfirm={(r) => { closeAsk(); ledger.revert(confirmed.map((e) => e.id), r); }} />
      )}
    </div>
  );
}
