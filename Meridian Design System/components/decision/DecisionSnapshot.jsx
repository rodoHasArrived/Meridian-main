// Meridian DecisionSnapshot — exactly what was reviewed, frozen at the moment of the decision.
//
// An approval that records "A. Patel clicked Approve at 11:42" is not evidence of anything. A
// snapshot binds the decision to the object AND its version, the workspace context, the time roles,
// the evidence set, the calculation versions, the materiality assessment, the guards that passed and
// the consequences that were shown. It is immutable by construction (deep-frozen on capture), and
// `verify` answers the only question that matters afterwards: is this proof still about the thing in
// front of me, or has the underlying object moved on?
//
// ActionGuard answers "may I". ActionEnvelope answers "did it land". This answers "what did I
// actually review" — the fact an attestation is bound to.
import React from "react";

const SECTIONS = ["object", "context", "time", "evidence", "calculations", "materiality", "guards", "consequences", "decision"];

function deepFreeze(value) {
  if (value == null || typeof value !== "object" || Object.isFrozen(value)) return value;
  Object.getOwnPropertyNames(value).forEach((k) => deepFreeze(value[k]));
  return Object.freeze(value);
}

const captureSnapshot = (input = {}) => {
  const { object, context, time, evidence, calculations, materiality, guards, consequences, decision, id, capturedAt } = input;
  return deepFreeze({
    id: id || `SNAP-${Date.now().toString(36).toUpperCase()}`,
    object: object || null,
    context: context || null,
    time: time || null,
    evidence: Array.isArray(evidence) ? evidence.slice() : [],
    calculations: Array.isArray(calculations) ? calculations.slice() : [],
    materiality: materiality || null,
    guards: Array.isArray(guards) ? guards.slice() : [],
    consequences: consequences || null,
    decision: decision || null,
    capturedAt: capturedAt || new Date().toISOString().replace("T", " ").slice(0, 19) + "Z",
  });
};

const evidenceKey = (list) => (list || []).map((e) => `${e.id}@${e.version ?? ""}#${e.hash || ""}:${e.condition || ""}`).sort().join("|");
const calcKey = (list) => (list || []).map((c) => `${c.id}@${c.version ?? ""}#${c.inputHash || ""}`).sort().join("|");

/** Findings, not a boolean: a snapshot can be superseded, or merely worth re-reading. */
const verifySnapshot = (snapshot, current = {}) => {
  if (!snapshot) return [];
  const out = [];
  const push = (field, was, now, effect, note) => out.push({ field, was, now, effect, note });
  if (current.object && snapshot.object && String(current.object.version) !== String(snapshot.object.version)) {
    push("object.version", snapshot.object.version, current.object.version, "void", "The object changed after the decision — the approval is void, not stale.");
  }
  if (current.object && snapshot.object && current.object.id && current.object.id !== snapshot.object.id) {
    push("object.id", snapshot.object.id, current.object.id, "void", "This proof is about a different object.");
  }
  if (current.object && snapshot.object && current.object.type != null && snapshot.object.type != null && current.object.type !== snapshot.object.type) {
    push("object.type", snapshot.object.type, current.object.type, "void", "This proof is about a different kind of object.");
  }
  if (current.evidence && evidenceKey(current.evidence) !== evidenceKey(snapshot.evidence)) {
    push("evidence", `${snapshot.evidence.length} items`, `${current.evidence.length} items`, "void", "The evidence set behind the decision changed.");
  }
  if (current.calculations && calcKey(current.calculations) !== calcKey(snapshot.calculations)) {
    push("calculations", calcKey(snapshot.calculations) ? "prior versions" : "none", "new versions", "review", "A calculation was re-run; the figures reviewed may no longer be the current ones.");
  }
  if (current.context && snapshot.context) {
    for (const k of ["entity", "book", "basis", "period", "currency", "scenario"]) {
      if (current.context[k] != null && snapshot.context[k] != null && current.context[k] !== snapshot.context[k]) {
        push(`context.${k}`, snapshot.context[k], current.context[k], "review", "The decision was taken under a different workspace context.");
      }
    }
  }
  return out;
};

const describeSnapshot = (snapshot) => {
  if (!snapshot) return "";
  const d = snapshot.decision || {};
  const o = snapshot.object || {};
  const ctx = snapshot.context || {};
  const facets = ["entity", "book", "basis", "period"].map((k) => ctx[k]).filter(Boolean).join(" · ");
  const parts = [
    d.actorId || d.actorName ? `${d.actorName || d.actorId}${d.authority ? ` (${d.authority})` : ""}` : "An actor",
    d.type ? `${d.type}` : "decided",
    [o.label || o.type, o.id].filter(Boolean).join(" "),
    o.version != null ? `version ${o.version}` : null,
    facets ? `under ${facets}` : null,
    snapshot.evidence.length ? `on ${snapshot.evidence.length} evidence ${snapshot.evidence.length === 1 ? "item" : "items"}` : "with no evidence attached",
    snapshot.calculations.length ? `calculation ${snapshot.calculations.map((c) => `${c.id} v${c.version}`).join(", ")}` : null,
    snapshot.guards.length ? `after ${snapshot.guards.length} ${snapshot.guards.length === 1 ? "guard" : "guards"}` : null,
    `at ${snapshot.capturedAt}`,
  ].filter(Boolean);
  return parts.join(" ") + ".";
};

function useDecisionSnapshot(input, current) {
  const [snapshot, setSnapshot] = React.useState(null);
  const capture = React.useCallback((override) => {
    const next = captureSnapshot({ ...input, ...override });
    setSnapshot(next);
    return next;
  }, [input]);
  const findings = React.useMemo(() => verifySnapshot(snapshot, current || {}), [snapshot, current]);
  return {
    snapshot, capture, findings,
    isVoid: findings.some((f) => f.effect === "void"),
    describe: () => describeSnapshot(snapshot),
    release: () => setSnapshot(null),
  };
}

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "decision-snapshot");
  el.textContent = `
.mds-snap{border:1px solid var(--border,#D2CDC3);background:var(--surface-plate,var(--bg-light,#F8F7F3));font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-snap__head{padding:8px 11px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--bg-medium,#EBE8E1);display:flex;flex-wrap:wrap;gap:3px 10px;align-items:baseline;justify-content:space-between}
.mds-snap__eyebrow{font:600 9px var(--font-data,monospace);letter-spacing:.07em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-snap__id{font:600 11px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B)}
.mds-snap__seal{display:inline-flex;align-items:center;gap:5px;padding:1px 6px;border:1px solid var(--border-strong,#AAA49A);border-radius:var(--radius-chip,2px);font:600 9px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-secondary,#535C65);background:var(--surface-well,var(--card-surface-raised,#FCFBF8))}
.mds-snap__seal[data-state="void"]{border-color:var(--red,#B54B42);color:var(--red-dim,#873832);background:var(--red-a10,rgba(181,75,66,.10))}
.mds-snap__seal[data-state="review"]{border-color:var(--orange,#986717);color:var(--orange-dim,#725012);background:var(--orange-a10,rgba(152,103,23,.10))}
.mds-snap__sentence{margin:0;padding:9px 11px;border-bottom:1px solid var(--border-divider,#E2DED5);font:12px/18px var(--font-body);color:var(--text-primary,#20252B);text-wrap:pretty}
.mds-snap__findings{margin:0;padding:7px 11px;border-bottom:1px solid var(--border-divider,#E2DED5);border-left:3px solid var(--red,#B54B42);list-style:none;background:var(--red-a10,rgba(181,75,66,.10))}
.mds-snap__findings[data-effect="review"]{border-left-color:var(--orange,#986717);background:var(--orange-a10,rgba(152,103,23,.10))}
.mds-snap__finding{display:grid;gap:1px;padding:2px 0;font-size:10px}
.mds-snap__ffield{font:600 10px var(--font-data,monospace);color:var(--text-primary,#20252B)}
.mds-snap__fnote{color:var(--text-secondary,#535C65);line-height:15px}
.mds-snap__grid{display:grid;grid-template-columns:max-content 1fr;gap:0;margin:0;padding:0}
.mds-snap__grid dt{padding:4px 10px 4px 11px;border-bottom:1px solid var(--border-divider,#E2DED5);font:600 9px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-muted,#606870);white-space:nowrap}
.mds-snap__grid dd{margin:0;padding:4px 11px 4px 0;border-bottom:1px solid var(--border-divider,#E2DED5);font:11px var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B);word-break:break-word}
.mds-snap__sub{display:block;font:9px/13px var(--font-body);color:var(--text-muted,#606870)}
.mds-snap__foot{padding:6px 11px;font:9px/14px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-snap__empty{padding:12px 11px;font-size:11px;color:var(--text-muted,#606870)}
@media print{.mds-snap{break-inside:avoid}}
`;
  document.head.appendChild(el);
}

export function DecisionSnapshotPanel({
  snapshot, current, findings, title = "Decision snapshot",
  showSentence = true, className = "", ...rest
}) {
  inject();
  const uid = React.useId();
  const found = findings || verifySnapshot(snapshot, current || {});
  if (!snapshot) {
    return <section className={`mds-snap${className ? " " + className : ""}`} {...rest}>
      <p className="mds-snap__empty">No snapshot captured. A commit or attestation binds to a snapshot taken before the attempt.</p>
    </section>;
  }
  const worst = found.some((f) => f.effect === "void") ? "void" : found.length ? "review" : "sealed";
  const o = snapshot.object || {};
  const ctx = snapshot.context || {};
  const time = snapshot.time || {};
  const rows = [
    ["Object", [o.label, o.type, o.id].filter(Boolean).join(" · "), o.version != null ? `Version ${o.version}` : null],
    ["Context", ["entity", "book", "basis", "period", "currency", "scenario"].map((k) => ctx[k]).filter(Boolean).join(" · ") || "—", null],
    ["Time", ["asOf", "evidenceAt", "calculatedAt", "reportingPeriod"].filter((k) => time[k]).map((k) => `${k} ${time[k]}`).join(" · ") || "—", null],
    ["Evidence", `${snapshot.evidence.length} ${snapshot.evidence.length === 1 ? "item" : "items"}`, snapshot.evidence.length ? snapshot.evidence.map((e) => `${e.id}${e.version ? ` v${e.version}` : ""}${e.condition ? ` (${e.condition})` : ""}`).join(" · ") : "No evidence bound to this decision"],
    ["Calculation", snapshot.calculations.length ? snapshot.calculations.map((c) => `${c.id} v${c.version}`).join(" · ") : "—", snapshot.calculations.some((c) => c.inputHash) ? snapshot.calculations.map((c) => c.inputHash).filter(Boolean).join(" · ") : null],
    ["Materiality", snapshot.materiality ? (snapshot.materiality.label || snapshot.materiality.classification || String(snapshot.materiality)) : "—", snapshot.materiality && snapshot.materiality.note ? snapshot.materiality.note : null],
    ["Guards", `${snapshot.guards.length} evaluated`, snapshot.guards.length ? snapshot.guards.map((g) => `${g.label || g.id} — ${g.state || g.status || "unknown"}`).join(" · ") : null],
    ["Reversibility", snapshot.consequences && snapshot.consequences.reversibility ? snapshot.consequences.reversibility : "—", null],
    ["Decision", snapshot.decision ? [snapshot.decision.type, snapshot.decision.actorName || snapshot.decision.actorId, snapshot.decision.authority].filter(Boolean).join(" · ") : "Not yet decided", snapshot.decision && snapshot.decision.rationale ? snapshot.decision.rationale : null],
    ["Captured", snapshot.capturedAt, null],
  ];

  return <section className={`mds-snap${className ? " " + className : ""}`} aria-labelledby={`${uid}-t`} {...rest}>
    <header className="mds-snap__head">
      <span>
        <span className="mds-snap__eyebrow" id={`${uid}-t`}>{title}</span>
        <br />
        <span className="mds-snap__id">{snapshot.id}</span>
      </span>
      <span className="mds-snap__seal" data-state={worst}>
        {worst === "void" ? "Superseded" : worst === "review" ? "Re-read" : "Sealed"}
      </span>
    </header>

    {showSentence ? <p className="mds-snap__sentence">{describeSnapshot(snapshot)}</p> : null}

    {found.length ? <ul className="mds-snap__findings" data-effect={worst}>
      {found.map((f, i) => <li className="mds-snap__finding" key={f.field + i} aria-setsize={found.length} aria-posinset={i + 1}>
        <span className="mds-snap__ffield">{f.field}: {String(f.was)} → {String(f.now)}</span>
        <span className="mds-snap__fnote">{f.note}</span>
      </li>)}
    </ul> : null}

    <dl className="mds-snap__grid">
      {rows.map(([label, value, sub]) => <React.Fragment key={label}>
        <dt>{label}</dt>
        <dd>{value}{sub ? <span className="mds-snap__sub">{sub}</span> : null}</dd>
      </React.Fragment>)}
    </dl>

    <p className="mds-snap__foot">Immutable — captured before the attempt and frozen. A change to the object, the evidence set, or the calculation version voids this proof rather than updating it.</p>
  </section>;
}

/** Bundle carrier — the contract, reachable from a template without an import. */
export const DecisionSnapshot = {
  SECTIONS,
  capture: captureSnapshot,
  verify: verifySnapshot,
  describe: describeSnapshot,
  Panel: DecisionSnapshotPanel,
  use: useDecisionSnapshot,
};

/** Hook carrier. */
export const SnapshotHooks = { useDecisionSnapshot, captureDecisionSnapshot: captureSnapshot, verifyDecisionSnapshot: verifySnapshot, describeDecisionSnapshot: describeSnapshot };
