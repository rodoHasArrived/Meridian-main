// Meridian ActionQueue — the tray of consequential actions in flight. `ActionEnvelope` states the
// fate of ONE instruction where it was issued; this is the screen-level surface that keeps every
// unresolved instruction visible until someone resolves it — because "operator pressed Post" and
// "the ledger agrees" are different moments, and between them nothing else on the screen says so.
//
// Ladder (derived from ActionEnvelope's four fates, no new vocabulary):
//   submitted → acknowledged → confirmed | rejected | timed out
// Every rung comes from the ANSWER, never from a clock. `inflight` is "submitted" for as long as it
// is in flight — an elapsed timer is not an acknowledgement, and the earlier version promoted rows
// to "Accepted by the adapter" after 1.5 s of wall time, which is a claim nothing had made.
// `landed` with a receipt id from the adapter is "confirmed"; `landed` without one is only
// "acknowledged" and stays in the tray as unresolved. `unlanded` is "timed out" — fate unknown,
// retry same key. Law: guidelines/ACTION_SEMANTICS.md · proposal: docs/UX_DEPTH_4_1.md §B-1.
import React from "react";
import { ActionHooks, ActionStateChip } from "./ActionEnvelope";

const { ActionContext } = ActionHooks;

export const QUEUE_RUNGS = [
  { id: "submitted", label: "Submitted", meaning: "Sent; awaiting an answer." },
  { id: "acknowledged", label: "Acknowledged", meaning: "The adapter answered yes but returned no receipt id — the system of record has not confirmed. Not a landing to act on." },
  { id: "confirmed", label: "Confirmed", meaning: "Landed with a receipt id from the adapter." },
  { id: "rejected", label: "Rejected", meaning: "Refused with a reason — part of the record." },
  { id: "timedout", label: "Timed out", meaning: "No answer. Fate unknown — retry with the same key or reconcile." },
];
/** The rung, read off the instruction's answer. No clock, no inference. */
export function rungFor(instruction) {
  if (!instruction) return "submitted";
  if (instruction.state === "rejected") return "rejected";
  if (instruction.state === "unlanded") return "timedout";
  if (instruction.state === "landed") return instruction.receipt && instruction.receipt.id ? "confirmed" : "acknowledged";
  return "submitted";
}

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return; injected = true;
  const el = document.createElement("style"); el.setAttribute("data-mds", "action-queue");
  el.textContent = `
.mds-aq{position:fixed;right:14px;bottom:40px;z-index:var(--z-popover,120);width:380px;max-width:calc(100vw - 28px);font-family:var(--font-body);color:var(--text-primary,#20252B);border:1px solid var(--border-strong,#AAA49A);background:var(--bg-light,#F8F7F3);box-shadow:var(--shadow-menu,0 2px 6px rgba(0,0,0,.18))}
.mds-aq[data-docked="inline"]{position:static;width:auto;max-width:none;box-shadow:none;border-color:var(--border,#D2CDC3)}
.mds-aq__hd{display:flex;align-items:center;gap:8px;min-height:32px;padding:0 10px;border-bottom:1px solid var(--border,#D2CDC3);background:var(--bg-medium,#EBE8E1);cursor:pointer;user-select:none}
.mds-aq__hd:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-aq__lbl{font:600 9.5px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-aq__sum{font:10.5px var(--font-data,monospace);color:var(--text-secondary,#535C65)}
.mds-aq__sum b{font-weight:600;color:var(--text-primary,#20252B)}
.mds-aq__sum[data-open="true"] b{color:var(--orange,#986717)}
.mds-aq__sum[data-unknown="true"] b{color:var(--red,#B54B42)}
.mds-aq__tog{margin-left:auto;border:0;background:transparent;color:var(--text-muted,#606870);font:11px var(--font-data,monospace);cursor:pointer;padding:2px 4px}
.mds-aq__list{margin:0;padding:0;list-style:none;max-height:300px;overflow:auto}
.mds-aq__row{display:grid;grid-template-columns:1fr auto;gap:2px 10px;padding:7px 10px;border-bottom:1px solid var(--border-divider,#E2DED5);align-items:center}
.mds-aq__row:last-child{border-bottom:0}
.mds-aq__row[data-rung="timedout"]{box-shadow:inset 2px 0 0 var(--red,#B54B42)}
.mds-aq__row[data-rung="rejected"]{box-shadow:inset 2px 0 0 var(--red-dim,#873832)}
.mds-aq__row[data-rung="submitted"],.mds-aq__row[data-rung="acknowledged"]{box-shadow:inset 2px 0 0 var(--orange,#986717)}.mds-aq__row[data-rung="confirmed"]{box-shadow:inset 2px 0 0 var(--green,#2E7655)}
.mds-aq__act{font:600 11.5px/1.4 var(--font-body);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.mds-aq__ladder{grid-column:1/-1;display:flex;align-items:center;gap:0;font:9px var(--font-data,monospace);letter-spacing:.04em;text-transform:uppercase;color:var(--text-disabled,#A4ABB2)}
.mds-aq__step{display:inline-flex;align-items:center;gap:4px}
.mds-aq__step::after{content:"›";margin:0 5px;color:var(--border-strong,#AAA49A)}
.mds-aq__step:last-child::after{content:none}
.mds-aq__step[data-done="true"]{color:var(--text-secondary,#535C65)}
.mds-aq__step[data-now="true"]{color:var(--text-primary,#20252B);font-weight:600}
.mds-aq__step[data-now="true"][data-rung="timedout"],.mds-aq__step[data-now="true"][data-rung="rejected"]{color:var(--red,#B54B42)}
.mds-aq__step[data-now="true"][data-rung="confirmed"]{color:var(--green,#2E7655)}
.mds-aq__meta{grid-column:1/-1;display:flex;flex-wrap:wrap;gap:2px 10px;font:9.5px/1.5 var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-aq__reason{grid-column:1/-1;margin:0;font:11px/1.45 var(--font-body);color:var(--text-primary,#20252B);padding:5px 8px;border-left:2px solid var(--red,#B54B42);background:var(--red-a10,rgba(181,75,66,.08))}
.mds-aq__acts{grid-column:1/-1;display:flex;gap:6px}
.mds-aq__btn{min-height:24px;padding:2px 9px;font:600 10.5px var(--font-body);cursor:pointer;background:var(--bg,#F2F0EB);border:1px solid var(--border-strong,#AAA49A);color:var(--text-primary,#20252B);border-radius:var(--radius-control,4px)}
.mds-aq__btn:hover{background:var(--bg-hover,#EDEFF0)}
.mds-aq__btn:focus-visible{outline:2px solid var(--interaction-focus,var(--focus,#2D6FB3));outline-offset:1px}
.mds-aq__btn[data-primary="true"]{background:var(--brand-axis,#29323A);border-color:var(--brand-axis,#29323A);color:var(--topbar-text,#F2F0EB)}
.mds-aq__empty{padding:10px;font:11px/1.5 var(--font-body);color:var(--text-muted,#606870)}
.mds-aq__clock{font-variant-numeric:tabular-nums}
@media print{.mds-aq{display:none}}
`;
  document.head.appendChild(el);
}

function elapsed(ms) { const s = Math.max(0, Math.round(ms / 1000)); return s < 60 ? s + "s" : Math.floor(s / 60) + "m " + (s % 60) + "s"; }

/**
 * The tray. Inside an `ActionEnvelopeProvider` it reads the live instruction list; otherwise pass
 * `instructions`. Unresolved rows (submitted · acknowledged · timed out) stay until resolved;
 * confirmed and acknowledged-by-operator rows leave after `settleMs`. `ackMs` is accepted and
 * ignored — rungs are read from the answer, not from elapsed time.
 */
export function ActionQueue({
  instructions: listProp, label = "Actions in flight", ackMs, settleMs = 6000,
  docked = "corner", defaultOpen = true, onRetry, onAcknowledge, onOpen, emptyLabel = "Nothing in flight.",
  hideWhenEmpty = true, className = "", ...rest
}) {
  inject();
  const ctx = React.useContext(ActionContext);
  const list = listProp || ctx?.instructions || [];
  const [open, setOpen] = React.useState(defaultOpen);
  const [acked, setAcked] = React.useState(() => new Set());
  const [, tick] = React.useState(0);
  const started = React.useRef(new Map());
  const now = Date.now();
  for (const i of list) if (!started.current.has(i.id)) started.current.set(i.id, now);
  const live = list.some((i) => i.state === "inflight");
  React.useEffect(() => { if (!live) return undefined; const t = setInterval(() => tick((n) => n + 1), 500); return () => clearInterval(t); }, [live]);
  // Settled rows leave on a clock, so the tray reads as a queue and not a log.
  React.useEffect(() => {
    const done = list.filter((i) => i.state === "landed" || acked.has(i.id));
    if (!done.length) return undefined;
    const t = setTimeout(() => tick((n) => n + 1), settleMs + 50);
    return () => clearTimeout(t);
  }, [list, acked, settleMs]);

  const rows = list.map((i) => {
    const s0 = started.current.get(i.id) || now;
    const rung = rungFor(i);
    const settledAt = i.receipt?.at ? Date.parse(i.receipt.at.replace(" ", "T")) : null;
    const gone = (rung === "confirmed" || acked.has(i.id)) && settledAt && now - settledAt > settleMs;
    return { i, rung, s0, gone };
  }).filter((r) => !r.gone);
  const unresolved = rows.filter((r) => r.rung !== "confirmed" && r.rung !== "rejected").length;
  const unknown = rows.filter((r) => r.rung === "timedout").length;
  const retry = onRetry || (ctx ? (i) => ctx.retry(i.action) : null);
  const ack = (i) => { setAcked((s) => new Set([...s, i.id])); onAcknowledge?.(i); };

  if (hideWhenEmpty && rows.length === 0 && docked === "corner") return null;
  const ladder = (rung) => {
    // A timed-out or rejected instruction never passed through acknowledgement, so its path does
    // not draw one as completed.
    const path = rung === "timedout" || rung === "rejected" ? ["submitted", rung] : ["submitted", "acknowledged", "confirmed"];
    const idx = path.indexOf(rung);
    return <span className="mds-aq__ladder" aria-hidden="true">{path.map((p, k) => <span key={p} className="mds-aq__step" data-rung={p} data-done={k < idx} data-now={k === idx}>{QUEUE_RUNGS.find((q) => q.id === p)?.label}</span>)}</span>;
  };
  return <section className={`mds-aq${className ? " " + className : ""}`} data-docked={docked} aria-label={label} aria-live="polite" {...rest}>
    <div className="mds-aq__hd" role="button" tabIndex={0} aria-expanded={open} onClick={() => setOpen((o) => !o)} onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); setOpen((o) => !o); } }}>
      <span className="mds-aq__lbl">{label}</span>
      <span className="mds-aq__sum" data-open={unresolved > 0} data-unknown={unknown > 0}><b>{unresolved}</b> unresolved{unknown ? <> · <b>{unknown}</b> unknown</> : null} · {rows.length} shown</span>
      <span className="mds-aq__tog">{open ? "▾" : "▸"}</span>
    </div>
    {open ? (rows.length === 0 ? <p className="mds-aq__empty">{emptyLabel}</p> : <ul className="mds-aq__list">
      {rows.map(({ i, rung, s0 }) => {
        const m = QUEUE_RUNGS.find((q) => q.id === rung);
        return <li key={i.id} className="mds-aq__row" data-rung={rung}>
          <span className="mds-aq__act" title={i.label}>{i.label || i.action}</span>
          <ActionStateChip state={i.state} attempt={i.receipt?.attempt} />
          {ladder(rung)}
          <span className="mds-aq__meta">
            <span>key {i.idempotencyKey}{i.keyDeclared === false ? " · undeclared" : ""}</span>
            {i.receipt?.id ? <span>receipt {i.receipt.id}</span> : null}
            <span className="mds-aq__clock">{i.state === "inflight" ? elapsed(now - s0) + " elapsed" : i.receipt?.latencyMs != null ? i.receipt.latencyMs + " ms" : null}</span>
          </span>
          {rung === "rejected" && i.reason ? <p className="mds-aq__reason"><b>Reason.</b> {i.reason}</p> : null}
          {rung === "timedout" || rung === "acknowledged" ? <p className="mds-aq__reason"><b>{m.meaning}</b></p> : null}
          {(rung === "timedout" || rung === "rejected" || onOpen) ? <span className="mds-aq__acts">
            {rung === "timedout" && retry ? <button type="button" className="mds-aq__btn" data-primary="true" onClick={() => retry(i)}>Retry · same key</button> : null}
            {rung === "rejected" && !acked.has(i.id) ? <button type="button" className="mds-aq__btn" onClick={() => ack(i)}>Acknowledge</button> : null}
            {onOpen ? <button type="button" className="mds-aq__btn" onClick={() => onOpen(i)}>Open</button> : null}
          </span> : null}
        </li>;
      })}
    </ul>) : null}
  </section>;
}

/** Status-bar mark: unresolved count, red when any fate is unknown. */
export function ActionQueueChip({ instructions: listProp, ackMs, label = "in flight", className = "", ...rest }) {
  inject();
  const ctx = React.useContext(ActionContext);
  const list = listProp || ctx?.instructions || [];
  const open = list.filter((i) => i.state === "inflight" || i.state === "unlanded" || (i.state === "landed" && !i.receipt?.id));
  const unknown = list.filter((i) => i.state === "unlanded").length;
  return <span className={`mds-aq__sum${className ? " " + className : ""}`} data-open={open.length > 0} data-unknown={unknown > 0} {...rest}><b>{open.length}</b> {label}{unknown ? ` · ${unknown} unknown` : ""}</span>;
}
