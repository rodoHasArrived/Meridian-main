// Meridian ActionEnvelope — the write-side twin of `ValueEnvelope`. A figure on this system can
// always say where it came from; until now an instruction could not say what became of it.
//
// `ActionGuard` answers *may I*. This answers *did it* — and it makes the mutating control DECLARE,
// before the attempt, the five things the deployment checklist asks in prose: its endpoint, its
// idempotency key, the permission it consumes, whether it is optimistic or confirmed, and where a
// failure will surface. Then it renders the four fates an instruction can have:
//
//   in flight  sent, no answer yet — resending without the same key is a second instruction
//   landed     acknowledged by the system of record, with a receipt
//   unlanded   the adapter did not answer: the fate is UNKNOWN, not failed (ADAPTER_SEMANTICS)
//   rejected   decided and refused, with a reason that is part of the record
//
// "Failed" is not one of them. A timeout that silently reads as failure is how a desk sends the
// same order twice. Law: guidelines/ACTION_SEMANTICS.md.
//
// LANDING IS EXPLICIT. The provider never infers one: it requires `ok === true` from the transport,
// takes the receipt id FROM THE ADAPTER, and mints nothing. An absent transport, an answer it
// cannot read, and a refusal without a reason are all UNLANDED — "assume it worked" is the failure
// mode this component exists to remove. Demos pass a NAMED fixture transport (`fixtureTransport`
// below, or `tests/mock-transport.js`), never nothing.
//
// And everything declared here is LEGIBILITY, not enforcement. The server is the authority that
// decides; this surface only makes the instruction, its permission and its fate readable.
import React from "react";

export const ACTION_STATES = [
  { id: "idle", label: "Not sent", glyph: "·", severity: "info", meaning: "Declared, not yet submitted." },
  { id: "inflight", label: "In flight", glyph: "◐", severity: "review", meaning: "Sent; no answer yet. A resend must carry the same idempotency key." },
  { id: "landed", label: "Landed", glyph: "✓", severity: "ready", meaning: "Acknowledged by the system of record. A receipt id from the adapter is the evidence — without one the landing is unconfirmed." },
  { id: "unlanded", label: "Unlanded", glyph: "◔", severity: "action", meaning: "No answer from the adapter — the fate is unknown, not failed. Retry with the same key, or reconcile before deciding." },
  { id: "rejected", label: "Rejected", glyph: "▲", severity: "blocked", meaning: "Decided and refused. The reason is part of the record." },
];
const STATE = Object.fromEntries(ACTION_STATES.map((s) => [s.id, s]));
const stateMeta = (id) => STATE[id] || STATE.idle;

const POSTURES = {
  confirmed: { label: "Confirmed", note: "No local effect until the instruction lands." },
  optimistic: { label: "Optimistic", note: "Local effect shown immediately, marked provisional until it lands." },
};

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return; injected = true;
  const el = document.createElement("style"); el.setAttribute("data-mds", "action-envelope");
  el.textContent = `
.mds-act{font-family:var(--font-body);color:var(--text-primary,#20252B);border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);display:grid;gap:0}
.mds-act__hd{display:flex;align-items:center;gap:9px;padding:8px 10px;border-bottom:1px solid var(--border-divider,#E6E2DA);background:var(--bg-medium,#EBE8E1)}
.mds-act__lbl{font:600 12px/1.4 var(--font-body)}
.mds-act__ep{font:10px/1.4 var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-act__decl{display:grid;grid-template-columns:auto 1fr;gap:3px 12px;margin:0;padding:9px 10px;font:11px/1.5 var(--font-body);border-bottom:1px solid var(--border-divider,#E6E2DA)}
.mds-act__decl dt{font:600 9px/1.6 var(--font-data,monospace);text-transform:uppercase;letter-spacing:.06em;color:var(--text-muted,#606870);white-space:nowrap}
.mds-act__decl dd{margin:0;font-family:var(--font-data,monospace);font-size:11px;color:var(--text-secondary,#535C65);word-break:break-all}
.mds-act__decl dd b{font-weight:600;color:var(--text-primary,#20252B)}
.mds-act__decl dd.undeclared{color:var(--red-dim,#873832)}
.mds-act__body{padding:9px 10px;display:grid;gap:7px}
.mds-act__meaning{margin:0;font:11px/1.55 var(--font-body);color:var(--text-secondary,#535C65);max-width:64ch;text-wrap:pretty}
.mds-act__reason{margin:0;padding:7px 9px;font:11px/1.55 var(--font-body);border-left:2px solid var(--red,#B54B42);background:var(--red-a10,rgba(181,75,66,.08));color:var(--text-primary,#20252B);max-width:64ch}
.mds-act__reason[data-tone="review"]{border-left-color:var(--orange,#986717);background:var(--orange-a10,rgba(152,103,23,.10))}
.mds-act__note{margin:0;font:9.5px/1.5 var(--font-data,monospace);color:var(--text-muted,#606870);max-width:72ch}
.mds-act__receipt{display:flex;flex-wrap:wrap;gap:2px 14px;margin:0;font:10px/1.6 var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-act__receipt b{font-weight:600;color:var(--text-secondary,#535C65)}
.mds-act__acts{display:flex;flex-wrap:wrap;gap:6px}
.mds-act__btn{min-height:28px;padding:4px 11px;font:600 11px var(--font-body);cursor:pointer;background:var(--bg,#F2F0EB);border:1px solid var(--border-strong,#AAA49A);color:var(--text-primary,#20252B);border-radius:var(--radius-control,4px)}
.mds-act__btn:hover{background:var(--bg-hover,#EDEFF0)}
.mds-act__btn:focus-visible{outline:2px solid var(--interaction-focus,var(--focus,#2D6FB3));outline-offset:1px}
.mds-act__btn[data-primary="true"]{background:var(--graphite,#29323A);border-color:var(--graphite,#29323A);color:var(--topbar-text,#F2F0EB)}
.mds-act__btn:disabled{cursor:not-allowed;color:var(--text-disabled,#A4ABB2);border-color:var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3)}
.mds-chip{display:inline-flex;align-items:baseline;gap:5px;font:11px/1.35 var(--font-body);border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);padding:1px 6px;white-space:nowrap;border-radius:var(--radius-chip,2px)}
.mds-chip__g{font:10px var(--font-data,monospace)}
.mds-chip__t{font-variant:all-small-caps;font-weight:600;letter-spacing:.04em}
.mds-chip__k{font:9px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-chip[data-sev="ready"]{color:var(--severity-ready-fg,#2E7655);border-color:var(--severity-ready-bd,#9DC4AE);background:var(--severity-ready-bg,#E9F2EC)}
.mds-chip[data-sev="review"]{color:var(--severity-review-fg,#725012);border-color:var(--severity-review-bd,#D6C08A);background:var(--severity-review-bg,#F6EFE0)}
.mds-chip[data-sev="action"]{color:var(--severity-action-fg,#873832);border-color:var(--severity-action-bd,#D8AFA9);background:var(--severity-action-bg,#F6E9E7)}
.mds-chip[data-sev="blocked"]{color:var(--severity-blocked-fg,#873832);border-color:var(--severity-blocked-bd,#B54B42);background:var(--severity-blocked-bg,#F6E9E7);font-weight:600}
.mds-chip[data-sev="info"]{color:var(--text-secondary,#535C65)}
.mds-act[data-state="unlanded"]>.mds-act__hd{box-shadow:inset 2px 0 0 var(--red,#B54B42)}
.mds-act[data-state="inflight"]>.mds-act__hd{box-shadow:inset 2px 0 0 var(--orange,#986717)}
.mds-act[data-state="landed"]>.mds-act__hd{box-shadow:inset 2px 0 0 var(--green,#2E7655)}
.mds-led{border:1px solid var(--border,#D2CDC3);background:var(--surface-well,#FCFBF8);font-family:var(--font-body);display:grid;gap:0}
.mds-led__hd{display:flex;align-items:baseline;justify-content:space-between;gap:10px;padding:7px 10px;border-bottom:1px solid var(--border-strong,#AAA49A);background:var(--bg-medium,#EBE8E1)}
.mds-led__lbl{font:600 9px/1.6 var(--font-data,monospace);text-transform:uppercase;letter-spacing:.06em;color:var(--text-muted,#606870)}
.mds-led__sum{font:10px/1.6 var(--font-data,monospace);color:var(--text-secondary,#535C65)}
.mds-led__list{margin:0;padding:0;list-style:none;display:grid;gap:0}
.mds-led__row{display:grid;grid-template-columns:1fr auto;gap:2px 10px;padding:6px 10px;border-bottom:1px solid var(--border-divider,#E6E2DA);min-height:var(--row-standard,36px);align-content:center}
.mds-led__row:last-child{border-bottom:0}
.mds-led__act{font:600 11px/1.45 var(--font-body)}
.mds-led__meta{grid-column:1/-1;font:9.5px/1.5 var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-led__empty{padding:9px 10px;font:11px/1.5 var(--font-body);color:var(--text-muted,#606870)}
`;
  document.head.appendChild(el);
}

/** The instruction's fate as an inline mark — for a blotter cell, a queue row, a status bar. */
export function ActionStateChip({ state = "idle", attempt, idempotencyKey, className = "", ...rest }) {
  inject();
  const m = stateMeta(state);
  return <span className={`mds-chip${className ? " " + className : ""}`} data-sev={m.severity} title={m.meaning} {...rest}>
    <span className="mds-chip__g" aria-hidden="true">{m.glyph}</span>
    <span className="mds-chip__t">{m.label}</span>
    {attempt > 1 ? <span className="mds-chip__k">att {attempt}</span> : null}
    {idempotencyKey ? <span className="mds-chip__k">{idempotencyKey}</span> : null}
  </span>;
}

/**
 * The declaration + fate of one mutating action. Uncontrolled inside an `ActionEnvelopeProvider`
 * (pass `action` and it reads the live instruction), or fully controlled via `state`/`receipt`.
 */
export function ActionEnvelope({
  action,
  label,
  endpoint,
  idempotencyKey,
  permission,
  posture = "confirmed",
  failureSurface,
  state: stateProp,
  receipt: receiptProp,
  reason: reasonProp,
  submitLabel,
  onSubmit,
  onRetry,
  onAcknowledge,
  disabled = false,
  showDeclaration = true,
  className = "",
  ...rest
}) {
  inject();
  const ctx = React.useContext(ActionContext);
  const live = ctx && action ? ctx.instructionFor(action) : null;
  const state = stateProp || live?.state || "idle";
  const receipt = receiptProp || live?.receipt || null;
  const reason = reasonProp || live?.reason || null;
  const key = idempotencyKey || live?.idempotencyKey;
  const m = stateMeta(state);
  const post = POSTURES[posture] || POSTURES.confirmed;
  const held = permission ? permission.held !== false : undefined;
  const submit = onSubmit || (ctx && action ? () => ctx.submit(action) : null);
  const retry = onRetry || (ctx && action ? () => ctx.retry(action) : null);

  return <section className={`mds-act${className ? " " + className : ""}`} data-state={state} aria-label={`${label || action} — ${m.label}`} {...rest}>
    <header className="mds-act__hd">
      <span className="mds-act__lbl">{label || action}</span>
      {endpoint ? <span className="mds-act__ep">{endpoint}</span> : null}
      <span style={{ marginLeft: "auto" }}><ActionStateChip state={state} attempt={receipt?.attempt} /></span>
    </header>
    {showDeclaration ? <dl className="mds-act__decl">
      <dt>Idempotency</dt><dd className={key ? "" : "undeclared"}>{key || "undeclared — a resend would be a second instruction"}</dd>
      <dt>Permission</dt><dd>{permission ? <><b>{permission.required || "—"}</b>{held === false ? " · not held" : held ? " · held" : ""}{permission.actor ? ` · ${permission.actor}` : ""}</> : <span className="undeclared">undeclared</span>}</dd>
      <dt>Posture</dt><dd><b>{post.label}</b> · {post.note}</dd>
      <dt>Failure</dt><dd className={failureSurface ? "" : "undeclared"}>{failureSurface || "undeclared — a failure would have nowhere to appear"}</dd>
    </dl> : null}
    <div className="mds-act__body">
      <p className="mds-act__meaning">{m.meaning}</p>
      {state === "rejected" && reason ? <p className="mds-act__reason"><b>Reason.</b> {reason}</p> : null}
      {state === "unlanded" ? <p className="mds-act__reason"><b>Do not re-key this instruction.</b> Retry carries the same idempotency key{key ? ` (${key})` : ""}; a fresh key would submit it twice.</p> : null}
      {state === "landed" && receipt && !receipt.id ? <p className="mds-act__reason" data-tone="review"><b>Acknowledged without a receipt.</b> The adapter answered yes and returned no receipt id, so nothing here evidences the write. Confirm against the system of record before treating it as posted.</p> : null}
      {receipt ? <p className="mds-act__receipt">
        {receipt.id ? <span><b>id</b> {receipt.id}</span> : <span><b>id</b> none returned</span>}
        {receipt.at ? <span><b>at</b> {receipt.at}</span> : null}
        {receipt.latencyMs != null ? <span><b>latency</b> {receipt.latencyMs} ms</span> : null}
        {receipt.attempt ? <span><b>attempt</b> {receipt.attempt}</span> : null}
        {receipt.duplicateOf ? <span><b>deduped</b> against {receipt.duplicateOf}</span> : null}
      </p> : null}
      <div className="mds-act__acts">
        {state === "idle" && submit ? <button type="button" className="mds-act__btn" data-primary="true" disabled={disabled || held === false} onClick={submit}>{submitLabel || "Submit"}</button> : null}
        {state === "inflight" ? <button type="button" className="mds-act__btn" disabled>Submitting…</button> : null}
        {(state === "unlanded" || state === "rejected") && retry ? <button type="button" className="mds-act__btn" data-primary={state === "unlanded" ? "true" : undefined} onClick={retry}>{state === "unlanded" ? "Retry with the same key" : "Resubmit after change"}</button> : null}
        {(state === "landed" || state === "rejected") && onAcknowledge ? <button type="button" className="mds-act__btn" onClick={onAcknowledge}>Acknowledge</button> : null}
      </div>
      {showDeclaration ? <p className="mds-act__note">Declared here, enforced by the server. These checks make the instruction legible; they do not authorize it.</p> : null}
    </div>
  </section>;
}

/** Every instruction this session sent, and what became of it. The write-side audit surface. */
export function InstructionLedger({ instructions, label = "Instructions this session", emptyLabel = "No instruction submitted yet.", className = "", ...rest }) {
  inject();
  const ctx = React.useContext(ActionContext);
  const list = instructions || ctx?.instructions || [];
  const open = list.filter((i) => i.state === "inflight" || i.state === "unlanded").length;
  return <section className={`mds-led${className ? " " + className : ""}`} aria-label={label} {...rest}>
    <header className="mds-led__hd"><span className="mds-led__lbl">{label}</span><span className="mds-led__sum">{list.length} sent · {open} unresolved</span></header>
    {list.length === 0 ? <p className="mds-led__empty">{emptyLabel}</p> : <ul className="mds-led__list">
      {list.map((i) => <li key={i.id} className="mds-led__row">
        <span className="mds-led__act">{i.label || i.action}</span>
        <ActionStateChip state={i.state} attempt={i.receipt?.attempt} />
        <span className="mds-led__meta">{[i.idempotencyKey, i.receipt?.id, i.receipt?.at, i.receipt?.latencyMs != null ? i.receipt.latencyMs + " ms" : null, i.reason].filter(Boolean).join(" · ")}</span>
      </li>)}
    </ul>}
  </section>;
}

const ActionContext = React.createContext(null);

/**
 * Reads a transport answer. Landing is EXPLICIT — `ok === true` and nothing less. A bare `{}`, an
 * `undefined`, a `true`, a string: anything this system cannot read as an acknowledgement is
 * UNLANDED, because the alternative (assume it worked) is how a desk stops chasing a write that
 * never happened. A refusal without a reason is not a decision either — also unlanded.
 * `receiptId` comes from the ADAPTER or is absent; the provider never mints one, so an
 * acknowledgement with no receipt cannot present itself as confirmed (`ActionQueue` keeps it on
 * the `acknowledged` rung and the envelope says the landing is unevidenced).
 */
function readAnswer(answer) {
  if (answer == null || typeof answer !== "object") {
    return { state: "unlanded", reason: `Adapter answered ${answer === undefined ? "nothing" : JSON.stringify(answer)} — not an acknowledgement this system can read. The fate is unknown; retry with the same key or reconcile.` };
  }
  if (answer.ok === false) {
    return answer.reason
      ? { state: "rejected", reason: String(answer.reason), receiptId: answer.id || null }
      : { state: "unlanded", reason: "Adapter refused without a reason. A refusal with no reason is not a decision an operator can act on — treat the fate as unknown and reconcile." };
  }
  if (answer.ok !== true) {
    return { state: "unlanded", reason: "Adapter answer carried no ok flag, so the response was never validated. An unvalidated response is not a confirmation — the fate is unknown." };
  }
  return { state: "landed", receiptId: answer.id || null, duplicateOf: answer.duplicateOf || null, unreceipted: !answer.id };
}

/**
 * A NAMED fixture transport, for cards, templates and demos. Deliberately not a default: an absent
 * transport leaves instructions unlanded, so a demo has to say out loud that its network is fake.
 * `decide(instruction)` may return `{ ok:false, reason }`, `"silent"` (never answers — the
 * envelope's timeout decides) or `"lost"` (the write happens, the answer does not).
 */
export function fixtureTransport({ latencyMs = 900, receiptPrefix = "FIX-", decide } = {}) {
  const landed = new Map();
  let n = 0;
  const t = async (instruction) => {
    const verdict = decide ? decide(instruction) : null;
    if (verdict === "silent") return new Promise(() => {});
    await new Promise((r) => setTimeout(r, latencyMs));
    const seen = landed.get(instruction.idempotencyKey);
    if (seen) return { ok: true, id: seen, duplicateOf: seen, replay: true };
    if (verdict && verdict.ok === false) return verdict.reason ? verdict : { ok: false, reason: "Refused by the fixture without a stated reason." };
    const id = receiptPrefix + ++n;
    landed.set(instruction.idempotencyKey, id);
    if (verdict === "lost") throw new Error("answer lost after the write — the instruction landed, the receipt did not");
    return { ok: true, id };
  };
  t.isFixture = true;
  t.landed = () => [...landed.values()];
  return t;
}

/**
 * Runs declared actions against a transport and owns the fate of every instruction.
 * `actions` is a map of id → declaration { label, endpoint, idempotencyKey, permission, posture,
 * failureSurface, payload }. `transport(instruction)` resolves { ok, id, reason }; see `readAnswer`
 * for what counts as a landing. Rejecting, timing out, or answering something unreadable is
 * UNLANDED, never rejected: a reason is a decision, silence is not. With no transport at all,
 * nothing lands — pass `fixtureTransport()` for a demo.
 */
export function ActionEnvelopeProvider({ actions = {}, transport, timeoutMs = 8000, onSettled, children }) {
  const [instructions, setInstructions] = React.useState([]);
  const seq = React.useRef(0);
  // Stable for the life of the provider, so an UNDECLARED idempotency key still dedupes a
  // double-clicked Submit instead of minting a fresh identity per click.
  const epoch = React.useRef(`S${Date.now().toString(36).toUpperCase()}`);
  const listRef = React.useRef(instructions);
  listRef.current = instructions;
  // Keys reserved synchronously at submit time: several submit() calls before React re-renders all
  // see the same stale listRef, so the in-flight guard cannot rely on state alone.
  const reserved = React.useRef(new Map());
  // The effective declaration (declared action + per-call overrides) each instruction was sent with,
  // so a retry resends exactly that instruction rather than rebuilding it without its overrides.
  const sentDecls = React.useRef(new Map());

  const patch = React.useCallback((id, next) => {
    setInstructions((list) => list.map((i) => (i.id === id ? { ...i, ...next } : i)));
  }, []);

  const send = React.useCallback(async (actionId, decl, attempt, key, priorId, keyDeclared = true) => {
    const id = priorId || `INS-${++seq.current}`;
    sentDecls.current.set(id, decl);
    const started = Date.now();
    const stamp = () => new Date().toISOString().replace("T", " ").replace(/\.\d+Z$/, "Z");
    const record = {
      id, action: actionId, label: decl.label || actionId, idempotencyKey: key, keyDeclared,
      state: "inflight", reason: null, receipt: { attempt, at: stamp() },
    };
    if (priorId) patch(priorId, { state: "inflight", reason: null, receipt: { attempt, at: stamp() } });
    else setInstructions((list) => [record, ...list]);

    const instruction = {
      action: actionId, endpoint: decl.endpoint, idempotencyKey: key, attempt,
      posture: decl.posture || "confirmed", permission: decl.permission, payload: decl.payload,
    };
    let outcome;
    if (!transport) {
      // A provider with no transport used to answer `{ ok: true }` to itself: every instruction
      // landed against nothing, carrying a receipt id minted locally.
      outcome = { state: "unlanded", reason: "No transport declared on ActionEnvelopeProvider — the instruction was never sent. Pass a named fixture transport for a demo." };
      if (typeof console !== "undefined") console.warn(`[Meridian] ActionEnvelopeProvider has no transport — "${actionId}" cannot land. Declare one (fixtureTransport() for demos).`);
    } else {
      let timer;
      try {
        const answer = await Promise.race([
          Promise.resolve(transport(instruction)),
          new Promise((_, rej) => { timer = setTimeout(() => rej(new Error("no answer within " + timeoutMs + " ms")), timeoutMs); }),
        ]);
        outcome = readAnswer(answer);
      } catch (e) {
        // No answer is NOT a failure: the instruction may have landed. This is the state the
        // whole contract exists for.
        outcome = { state: "unlanded", reason: String((e && e.message) || e) };
      } finally { clearTimeout(timer); }
    }
    const settled = {
      state: outcome.state,
      reason: outcome.state === "landed" ? null : outcome.reason || null,
      receipt: { id: outcome.receiptId || null, at: stamp(), latencyMs: Date.now() - started, attempt, duplicateOf: outcome.duplicateOf || undefined, unreceipted: outcome.unreceipted || undefined },
    };
    patch(id, settled);
    onSettled?.({ action: actionId, id, ...settled });
    return { id, ...settled };
  }, [patch, transport, timeoutMs, onSettled]);

  const submit = React.useCallback((actionId, overrides = {}) => {
    const decl = { ...(actions[actionId] || {}), ...overrides };
    const keyDeclared = decl.idempotencyKey != null && decl.idempotencyKey !== "";
    const key = keyDeclared ? decl.idempotencyKey : `${actionId}:${epoch.current}`;
    // A double-clicked Submit is one instruction, not two: the key is the identity. The undeclared
    // fallback is stable per provider rather than per click — an invented-per-click key made this
    // guard unreachable, which is the defect the declaration panel was already reporting in words.
    const pending = reserved.current.get(key);
    if (pending) return pending.then((r) => ({ id: r.id, state: r.state, deduped: true }));
    const existing = listRef.current.find((i) => i.idempotencyKey === key && (i.state === "inflight" || i.state === "landed"));
    if (existing) {
      patch(existing.id, { receipt: { ...existing.receipt, duplicateOf: existing.receipt?.id || existing.id } });
      return Promise.resolve({ id: existing.id, state: existing.state, deduped: true });
    }
    const run = send(actionId, decl, 1, key, null, keyDeclared);
    reserved.current.set(key, run);
    const release = () => { if (reserved.current.get(key) === run) reserved.current.delete(key); };
    run.then(release, release);
    return run;
  }, [actions, patch, send]);

  const retry = React.useCallback((actionId) => {
    const prior = listRef.current.find((i) => i.action === actionId && (i.state === "unlanded" || i.state === "rejected"));
    // A retry with nothing to retry used to fall through to submit(), minting a FRESH key — the one
    // thing the unlanded fate exists to prevent. It is now a no-op that says so.
    if (!prior) {
      if (typeof console !== "undefined") console.warn(`[Meridian] retry("${actionId}"): no unresolved instruction to retry. Use submit() to send one.`);
      return Promise.resolve(null);
    }
    const decl = sentDecls.current.get(prior.id) || actions[actionId] || {};
    // Same key, incremented attempt — the retry asks "did the first one land?", it does not resend.
    return send(actionId, decl, (prior.receipt?.attempt || 1) + 1, prior.idempotencyKey, prior.id, prior.keyDeclared !== false);
  }, [actions, send]);

  const value = React.useMemo(() => ({
    instructions,
    instructionFor: (actionId) => instructions.find((i) => i.action === actionId) || null,
    submit, retry,
    unresolved: instructions.filter((i) => i.state === "inflight" || i.state === "unlanded" || (i.state === "landed" && !i.receipt?.id)),
  }), [instructions, submit, retry]);

  return <ActionContext.Provider value={value}>{typeof children === "function" ? children(value) : children}</ActionContext.Provider>;
}

/** Hook carrier — `const { useAction } = MeridianDesignSystem.ActionHooks`. */
export const ActionHooks = {
  useAction: () => React.useContext(ActionContext),
  ActionContext,
  ACTION_STATES,
  stateMeta,
  readAnswer,
  fixtureTransport,
};
