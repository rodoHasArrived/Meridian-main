// Meridian assurance — certification as carried state on a BALANCE (account × period × basis),
// not only on a close. O-4 of docs/ACCOUNTING_INTERFACE_4_3.md: "which of these TB lines has a
// preparer even looked at?" had no answer on any surface. Stages climb unprepared → prepared →
// reviewed → certified; each step is a named signature bound to the balance it vouched for, so a
// later movement voids the signature instead of letting it stand over a different number.
import React from "react";
import { formatMoney } from "../core/format.js";

export const ASSURANCE_STAGES = [
  { id: "unprepared", label: "Unprepared", rank: 0, note: "No one has looked at this balance." },
  { id: "prepared", label: "Prepared", rank: 1, note: "A preparer has agreed the balance to its proof." },
  { id: "reviewed", label: "Reviewed", rank: 2, note: "A second person has checked the preparer's work." },
  { id: "certified", label: "Certified", rank: 3, note: "Signed as fairly stated, bound to this balance." },
];
const STAGE = Object.fromEntries(ASSURANCE_STAGES.map((s) => [s.id, s]));
const NEXT = { unprepared: "prepared", prepared: "reviewed", reviewed: "certified", certified: null };
const VERB = { prepared: "Prepare", reviewed: "Review", certified: "Certify" };

/** The stage a record is in, after checking its signatures still bind the current balance. */
export function assuranceState(record, currentBalance) {
  const sigs = (record && record.signatures) || [];
  if (!sigs.length) return { stage: "unprepared", voided: false, signatures: [], live: [], history: [] };
  const binds = (s) => currentBalance == null || s.balance == null || Math.abs(s.balance - currentBalance) < 0.005;
  // The LIVE chain is the trailing run of signatures taken against the balance as it stands now.
  // Everything before a movement is history — void, but never deleted: an audit trail that erases
  // the signature it voided cannot show who vouched for what, and a re-preparation used to drop
  // every earlier signature on the floor.
  let i = sigs.length;
  while (i > 0 && binds(sigs[i - 1])) i -= 1;
  const live = sigs.slice(i), history = sigs.slice(0, i);
  const top = sigs[sigs.length - 1];
  const voided = live.length === 0;
  return {
    stage: voided ? "unprepared" : live[live.length - 1].stage,
    voided, movedFrom: voided ? top.balance : undefined,
    signatures: sigs, live, history,
  };
}

/** Coverage of a set of accounts — the lock gate reads this, not task completion. */
export function assuranceCoverage(accounts, records, currentBalances = {}, { requiredStage = "certified", materialOnly = true } = {}) {
  const need = STAGE[requiredStage].rank;
  const rows = accounts.map((a) => {
    const st = assuranceState(records[a.code], currentBalances[a.code]);
    return { ...a, stage: st.stage, voided: st.voided, covered: STAGE[st.stage].rank >= need };
  });
  const inScope = materialOnly ? rows.filter((r) => r.material) : rows;
  const uncovered = inScope.filter((r) => !r.covered);
  return { rows, inScope, uncovered, covered: inScope.length - uncovered.length, total: inScope.length, complete: uncovered.length === 0 };
}

/** Advance one stage, binding the signature to the balance as it stands now. Pure. */
export function signAssurance(record, { stage, actor, authority, at, balance, snapshotId }) {
  const cur = assuranceState(record, balance);
  const target = stage || NEXT[cur.stage];
  if (!target) return record;
  if (STAGE[target].rank !== STAGE[cur.stage].rank + 1) throw new Error(`Cannot move ${cur.stage} → ${target}; stages are climbed one at a time.`);
  const sig = { stage: target, actor, authority, at: at || new Date().toISOString(), balance, snapshotId };
  // APPEND, never replace. A re-preparation after a movement keeps every earlier signature;
  // `assuranceState` decides which of them still bind the balance in front of you.
  return { ...(record || {}), signatures: [...cur.signatures, sig] };
}
/** Every signature ever taken on this record, oldest first — including the voided ones. */
export const assuranceHistory = (record) => ((record && record.signatures) || []).slice();
export const nextAssuranceStage = (stage) => NEXT[stage];
export const assuranceVerb = (stage) => VERB[NEXT[stage]] || null;

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-asr{display:inline-flex;align-items:center;gap:6px;height:20px;padding:0 7px;border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-chip,2px);font:600 9.5px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-muted,#606870);background:var(--bg-light,#F8F7F3);white-space:nowrap}
.mds-asr::before{content:"";width:6px;height:6px;border-radius:50%;background:currentColor;opacity:.55}
.mds-asr[data-stage="prepared"]{color:var(--state-pending-fg,#5C503E);border-color:var(--pending,#7A6B52)}
.mds-asr[data-stage="reviewed"]{color:var(--purple-dim,#234B68);border-color:var(--color-info,#2F648A)}
.mds-asr[data-stage="certified"]{color:var(--green-dim,#225840);border-color:var(--green,#2E7655);background:var(--green-a10,rgba(46,118,85,.1))}
.mds-asr[data-voided="true"]{text-decoration:line-through;text-decoration-color:var(--red,#B54B42)}
.mds-asr__by{text-transform:none;letter-spacing:0;font-weight:500;color:var(--text-secondary,#535C65)}
.mds-cert{border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);font-family:var(--font-data,monospace);font-size:11.5px}
.mds-cert table{width:100%;border-collapse:collapse}
.mds-cert th{padding:7px 10px;text-align:left;font:600 9.5px var(--font-body);font-variant:all-small-caps;letter-spacing:.04em;color:var(--text-muted,#606870);background:var(--bg-medium,#EBE8E1);border-bottom:1px solid var(--border-strong,#AAA49A)}
.mds-cert th.c,.mds-cert td.c{text-align:center;width:88px}
.mds-cert td{padding:6px 10px;border-top:1px solid var(--border-divider,#E2DED5);color:var(--text-primary,#20252B);white-space:nowrap}
.mds-cert td.r{text-align:right;font-variant-numeric:tabular-nums}
.mds-cert tr[data-click="true"]{cursor:pointer}.mds-cert tr[data-click="true"]:hover td{background:var(--bg-hover,#EDEFF0)}
.mds-cert tr[data-click="true"]:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-cert__dot{display:inline-block;width:10px;height:10px;border:1px solid var(--border-strong,#AAA49A);border-radius:50%;background:transparent;vertical-align:middle}
.mds-cert__dot[data-on="true"]{background:var(--green,#2E7655);border-color:var(--green,#2E7655)}
.mds-cert__dot[data-void="true"]{background:transparent;border-color:var(--red,#B54B42);border-style:dashed}
.mds-cert__mat{font:600 9px var(--font-data,monospace);letter-spacing:.05em;color:var(--state-pending-fg,#5C503E);margin-left:6px}
.mds-cert__foot{display:flex;gap:14px;align-items:center;padding:8px 10px;border-top:1px solid var(--border,#D2CDC3);font-size:11px;color:var(--text-secondary,#535C65)}
.mds-cert__foot b{color:var(--text-primary,#20252B);font-weight:600}
`;
  const el = document.createElement("style"); el.setAttribute("data-mds", "assurance"); el.textContent = css; document.head.appendChild(el);
}

/** The stage as a chip. `record` + `balance` derive it; or pass `stage` directly. */
export function AssuranceChip({ record, balance, stage, showActor = false, className = "", ...rest }) {
  inject();
  const st = stage ? { stage, voided: false, signatures: [] } : assuranceState(record, balance);
  const top = st.signatures[st.signatures.length - 1];
  const label = st.voided ? `${STAGE[top.stage].label} · void` : STAGE[st.stage].label;
  const title = st.voided ? `${STAGE[top.stage].label} by ${top.actor} against ${formatMoney(top.balance)} — balance has since moved, signature void. ${st.signatures.length} signature${st.signatures.length === 1 ? "" : "s"} retained in history.` : top ? `${label} by ${top.actor} (${top.authority || "—"}) at ${top.at}` : STAGE.unprepared.note;
  return <span className={`mds-asr${className ? " " + className : ""}`} data-stage={st.voided ? "unprepared" : st.stage} data-voided={st.voided ? "true" : undefined} title={title} aria-label={title} {...rest}>
    {label}{showActor && top && !st.voided ? <span className="mds-asr__by">{top.actor}</span> : null}
  </span>;
}

/** Accounts × stages — the close's real progress bar. */
export function CertificationMatrix({ accounts = [], records = {}, balances = {}, currency = "USD", requiredStage = "certified", onOpenAccount, caption = "Certification by account", className = "", ...rest }) {
  inject();
  const cov = assuranceCoverage(accounts, records, balances, { requiredStage, materialOnly: false });
  const stages = ASSURANCE_STAGES.slice(1);
  return <div className={`mds-cert${className ? " " + className : ""}`} role="region" aria-label={caption} {...rest}>
    <table>
      <thead><tr><th scope="col">Account</th><th scope="col" style={{ textAlign: "right" }}>Balance</th>{stages.map((s) => <th key={s.id} scope="col" className="c">{s.label}</th>)}<th scope="col">Last signature</th></tr></thead>
      <tbody>
        {cov.rows.map((r) => {
          const st = assuranceState(records[r.code], balances[r.code]);
          const top = st.signatures[st.signatures.length - 1];
          const rank = STAGE[st.stage].rank;
          return <tr key={r.code} data-click={onOpenAccount ? "true" : undefined} tabIndex={onOpenAccount ? 0 : undefined}
            onClick={onOpenAccount ? () => onOpenAccount(r.code) : undefined}
            onKeyDown={onOpenAccount ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onOpenAccount(r.code); } } : undefined}>
            <td>{r.code} · {r.name}{r.material ? <span className="mds-cert__mat" title="Material — required for lock">MATERIAL</span> : null}</td>
            <td className="r">{formatMoney(Math.abs(balances[r.code] ?? 0), { currency })}</td>
            {stages.map((s) => <td key={s.id} className="c"><span className="mds-cert__dot" data-on={rank >= s.rank ? "true" : undefined} data-void={st.voided && top && STAGE[top.stage].rank >= s.rank ? "true" : undefined} aria-label={`${s.label}: ${rank >= s.rank ? "done" : st.voided && top && STAGE[top.stage].rank >= s.rank ? "void — balance moved" : "not yet"}`} /></td>)}
            <td style={{ color: "var(--text-secondary)" }}>{top ? `${top.actor} · ${top.at.replace("T", " ").slice(0, 16)}Z${st.voided ? " · void — balance moved" : ""}` : "—"}</td>
          </tr>;
        })}
      </tbody>
    </table>
    <div className="mds-cert__foot">
      <span><b>{cov.rows.filter((r) => r.covered).length}</b> of {cov.rows.length} accounts {STAGE[requiredStage].label.toLowerCase()}</span>
      <span><b>{cov.rows.filter((r) => r.material && r.covered).length}</b> of {cov.rows.filter((r) => r.material).length} material</span>
      {cov.rows.some((r) => r.voided) ? <span style={{ color: "var(--red-dim,#883832)" }}><b>{cov.rows.filter((r) => r.voided).length}</b> signature{cov.rows.filter((r) => r.voided).length === 1 ? "" : "s"} void — balance moved since signing</span> : null}
    </div>
  </div>;
}

/** Module carrier — `const { Assurance } = window.<Namespace>`. */
export const Assurance = { STAGES: ASSURANCE_STAGES, state: assuranceState, coverage: assuranceCoverage, sign: signAssurance, history: assuranceHistory, next: nextAssuranceStage, verb: assuranceVerb, Chip: AssuranceChip, Matrix: CertificationMatrix };
