// Meridian DecisionFlow — the ordered, guarded, version-aware progression from observation to a
// committed decision. Templates should not reinvent step state: the provider owns the step order,
// the requirement graph, guard results, URL and reload restoration, and — critically — invalidation
// when the underlying object version changes underneath a review that has already been performed.
import React from "react";

const rank = { satisfied: 0, warning: 1, blocking: 2 };
function summarize(guards) {
  const g = guards || [], blocking = g.filter(x => x.state === "blocking"), warning = g.filter(x => x.state === "warning");
  return { total: g.length, blocking: blocking.length, warning: warning.length, blocked: blocking.length > 0, firstBlocking: blocking[0] || null,
    sentence: blocking.length ? `${blocking.length} blocking ${blocking.length === 1 ? "guard" : "guards"}` : warning.length ? `${warning.length} ${warning.length === 1 ? "warning" : "warnings"}` : g.length ? "All guards satisfied" : "No guards declared" };
}
const REVERSIBILITY = {
  reversible:    { label: "Reversible",    note: "Can be undone without creating a compensating record" },
  compensatable: { label: "Compensatable", note: "The original record remains; a reversal creates a new one" },
  supersedable:  { label: "Supersedable",  note: "A new version replaces the effective state; the prior version remains" },
  irreversible:  { label: "Irreversible",  note: "This action cannot practically be reversed" },
};

const Ctx = React.createContext(null);
export function useDecisionFlow() {
  const v = React.useContext(Ctx);
  if (!v) throw new Error("Meridian: DecisionFlowRail / DecisionFlowStage / DecisionConsequenceBar must be rendered inside a DecisionFlowProvider.");
  return v;
}

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return; injected = true;
  const el = document.createElement("style"); el.setAttribute("data-mds", "decision-flow");
  el.textContent = `
.mds-df{font-family:var(--font-body);color:var(--text-primary,#20252B)}
.mds-dfrail{display:grid;gap:0;margin:0;padding:0;list-style:none;font-family:var(--font-body)}
.mds-dfrail__hd{font:600 9px var(--font-data,monospace);text-transform:uppercase;letter-spacing:.06em;color:var(--text-muted,#606870);padding-bottom:6px;border-bottom:1px solid var(--border,#D2CDC3)}
.mds-dfstep{border-bottom:1px solid var(--border-divider,#E2DED5)}
.mds-dfstep:last-child{border-bottom:0}
.mds-dfstep__btn{display:grid;grid-template-columns:20px 1fr;gap:9px;width:100%;text-align:left;background:none;border:0;border-left:2px solid transparent;padding:8px 9px;min-height:var(--row-review,44px);align-items:center;font:inherit;color:inherit;cursor:pointer}
.mds-dfstep__btn:hover{background:var(--bg-hover,#EDEFF0)}
.mds-dfstep__btn:focus-visible{outline:2px solid var(--interaction-focus,var(--focus,#2D6FB3));outline-offset:-2px}
.mds-dfstep__btn[aria-disabled="true"]{cursor:not-allowed;color:var(--text-muted,#606870)}
.mds-dfstep[data-current="true"] .mds-dfstep__btn{background:var(--interaction-selected-surface,var(--selected-surface,#E7EDF2));border-left-color:var(--interaction-selected,var(--selected,#496C8F))}
.mds-dfstep__n{font:600 10px var(--font-data,monospace);text-align:center;color:var(--text-muted,#606870)}
.mds-dfstep[data-state="done"] .mds-dfstep__n{color:var(--green-dim,#23583F)}
.mds-dfstep[data-state="blocked"] .mds-dfstep__n{color:var(--red-dim,#873832)}
.mds-dfstep__lbl{font:600 12px/1.4 var(--font-body)}
.mds-dfstep__meta{display:block;font:10px/1.5 var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-dfstage{display:grid;gap:10px}
.mds-dfstage__hd{display:flex;align-items:baseline;justify-content:space-between;gap:12px;border-bottom:1px solid var(--border,#D2CDC3);padding-bottom:6px}
.mds-dfstage__ttl{font:600 14px/1.3 var(--font-body)}
.mds-dfstage__pos{font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-dfstale{border:1px solid var(--orange,#986717);background:var(--orange-a10,rgba(152,103,23,.08));padding:9px 11px;display:grid;gap:4px}
.mds-dfstale__t{font:600 12px var(--font-body);color:var(--orange-dim,#724D11)}
.mds-dfstale__b{font:11px/1.5 var(--font-body);color:var(--text-secondary,#535C65);max-width:64ch;text-wrap:pretty}
.mds-dfbar{display:grid;grid-template-columns:1fr auto;gap:14px;align-items:center;border-top:1px solid var(--border-strong,#AAA49A);background:var(--bg-light,#F8F7F3);padding:10px 14px;min-height:var(--row-review,44px)}
.mds-dfbar__l{display:grid;gap:2px;min-width:0}
.mds-dfbar__st{font:11px/1.45 var(--font-body);color:var(--text-secondary,#535C65)}
.mds-dfbar__st[data-blocked="true"]{color:var(--red-dim,#873832);font-weight:600}
.mds-dfbar__ob{font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-dfbar__rev{font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-dfbar__acts{display:flex;gap:8px;align-items:center}
.mds-dfbtn{min-height:var(--row-review,44px);padding:9px 16px;font:600 12px var(--font-body);letter-spacing:.02em;cursor:pointer;border:1px solid var(--brand-axis,#29323A);background:var(--brand-axis,#29323A);color:var(--text-on-fill,#fff);white-space:nowrap}
.mds-dfbtn--ghost{background:var(--bg-light,#F8F7F3);color:var(--text-primary,#20252B);border-color:var(--border-strong,#AAA49A)}
.mds-dfbtn:hover{opacity:.92}
.mds-dfbtn:focus-visible{outline:2px solid var(--interaction-focus,var(--focus,#2D6FB3));outline-offset:2px}
.mds-dfbtn[aria-disabled="true"]{cursor:not-allowed;background:var(--bg-medium,#EBE8E1);color:var(--text-disabled,#A4ABB2);border-color:var(--border,#D2CDC3)}
@media(max-width:640px){.mds-dfbar{grid-template-columns:1fr;gap:8px}.mds-dfbar__acts{justify-content:stretch}.mds-dfbtn{flex:1}}
`;
  document.head.appendChild(el);
}

function readInitial({ urlParam, persistedKey, steps, initialStep }) {
  const ids = steps.map(s => s.id);
  if (typeof window !== "undefined") {
    if (urlParam) { const v = new URLSearchParams(window.location.search).get(urlParam); if (v && ids.includes(v)) return v; }
    if (persistedKey) { try { const v = window.localStorage.getItem(`mds.decisionflow.${persistedKey}`); if (v && ids.includes(v)) return v; } catch (e) { /* storage unavailable */ } }
  }
  return initialStep && ids.includes(initialStep) ? initialStep : ids[0];
}

export function DecisionFlowProvider({
  object, snapshotId, steps = [], initialStep, persistedKey, urlParam,
  onValidate, onTransition, actorId, children,
}) {
  inject();
  const [stepId, setStepId] = React.useState(() => readInitial({ urlParam, persistedKey, steps, initialStep }));
  const [completed, setCompleted] = React.useState({});
  const [guards, setGuards] = React.useState({});
  const [transitions, setTransitions] = React.useState([]);
  const [versionChanged, setVersionChanged] = React.useState(null);
  const version = object ? object.version : undefined;
  const seen = React.useRef(version);

  React.useEffect(() => {
    if (seen.current === version) return;
    const from = seen.current; seen.current = version;
    setCompleted(prev => {
      const next = {};
      steps.forEach(s => { if (prev[s.id] && s.stableAcrossVersions) next[s.id] = prev[s.id]; });
      return next;
    });
    setGuards({});
    setVersionChanged({ from, to: version });
  }, [version]); // eslint-disable-line react-hooks/exhaustive-deps

  React.useEffect(() => {
    if (typeof window === "undefined") return;
    if (urlParam) {
      const u = new URL(window.location.href);
      if (u.searchParams.get(urlParam) !== stepId) { u.searchParams.set(urlParam, stepId); window.history.replaceState(null, "", u); }
    }
    if (persistedKey) { try { window.localStorage.setItem(`mds.decisionflow.${persistedKey}`, stepId); } catch (e) { /* storage unavailable */ } }
  }, [stepId, urlParam, persistedKey]);

  const byId = React.useMemo(() => Object.fromEntries(steps.map(s => [s.id, s])), [steps]);
  const unmet = React.useCallback(id => ((byId[id] || {}).requires || []).filter(r => !completed[r] && !(byId[r] || {}).optional), [byId, completed]);
  const stepGuards = React.useCallback(id => guards[id] || (byId[id] || {}).guards || [], [guards, byId]);

  const validate = React.useCallback(async id => {
    if (!onValidate) return stepGuards(id);
    const result = (await onValidate(id)) || [];
    setGuards(g => ({ ...g, [id]: result }));
    return result;
  }, [onValidate, stepGuards]);

  // Entering a step validates it: a step's guards must be legible before the user works in it,
  // not only after an attempt. Cached results are reused until the object version invalidates them.
  const validated = React.useRef({});
  React.useEffect(() => {
    if (!onValidate || validated.current[stepId]) return;
    validated.current[stepId] = true;
    validate(stepId);
  }, [stepId, onValidate, validate]);
  React.useEffect(() => { validated.current = {}; }, [version]);

  const goTo = React.useCallback(async (id, opts = {}) => {
    const step = byId[id]; if (!step) return false;
    if (!opts.force && unmet(id).length) return false;
    const from = stepId;
    setStepId(id);
    if (step.onEnter) await step.onEnter();
    const t = { from, to: id, at: new Date().toISOString(), actorId, reason: opts.reason };
    setTransitions(list => [...list, t]);
    if (onTransition) onTransition(t);
    return true;
  }, [byId, unmet, stepId, actorId, onTransition]);

  const complete = React.useCallback(async (id = stepId, detail) => {
    const g = await validate(id);
    if (summarize(g).blocked) return false;
    setCompleted(c => ({ ...c, [id]: { at: new Date().toISOString(), actorId, ...detail } }));
    (byId[id] || {}).invalidates && setCompleted(c => {
      const next = { ...c }; (byId[id].invalidates || []).forEach(k => { delete next[k]; }); return next;
    });
    const order = steps.map(s => s.id), i = order.indexOf(id);
    const nextId = order.slice(i + 1).find(n => !unmet(n).length || (byId[n].requires || []).every(r => r === id || completed[r]));
    if (nextId) await goTo(nextId, { force: true });
    return true;
  }, [stepId, validate, actorId, byId, steps, unmet, completed, goTo]);

  const acknowledgeVersionChange = React.useCallback(() => setVersionChanged(null), []);
  const current = byId[stepId] || steps[0] || null;
  const summary = summarize(stepGuards(stepId));

  const value = {
    object, snapshotId, steps, stepId, step: current, index: steps.findIndex(s => s.id === stepId),
    completed, guards, stepGuards, summary, transitions, versionChanged, acknowledgeVersionChange,
    unmet, goTo, complete, validate, isBlocked: summary.blocked,
  };
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

// Namespace export: the flow contract as one reachable object — including the hook, which a
// lowercase export alone would not put on the design-system namespace.
export const DecisionFlow = {
  reversibility: REVERSIBILITY,
  use: useDecisionFlow,
  useDecisionFlow,
  Provider: props => <DecisionFlowProvider {...props} />,
  Rail: props => <DecisionFlowRail {...props} />,
  Stage: props => <DecisionFlowStage {...props} />,
  ConsequenceBar: props => <DecisionConsequenceBar {...props} />,
};

export function DecisionFlowRail({ label = "Decision", showGuardCounts = true, className = "", ...rest }) {
  const f = useDecisionFlow();
  return <div className={`mds-df${className ? " " + className : ""}`} {...rest}>
    <div className="mds-dfrail__hd">{label}{f.object ? ` · ${f.object.type} ${f.object.id} v${f.object.version}` : ""}</div>
    <ol className="mds-dfrail">{f.steps.map((s, i) => {
      const blocked = f.unmet(s.id).length > 0;
      const g = summarize(f.stepGuards(s.id));
      const state = f.completed[s.id] ? "done" : g.blocked ? "blocked" : blocked ? "locked" : "open";
      const mark = state === "done" ? "✓" : state === "blocked" ? "▲" : state === "locked" ? "·" : String(i + 1);
      const meta = [
        state === "done" ? "Complete" : state === "blocked" ? g.sentence : state === "locked" ? `Requires ${f.unmet(s.id).map(r => (f.steps.find(x => x.id === r) || {}).label || r).join(", ")}` : s.optional ? "Optional" : "Available",
        showGuardCounts && g.total && state !== "blocked" ? g.sentence : null,
      ].filter(Boolean).join(" · ");
      return <li key={s.id} className="mds-dfstep" data-state={state} data-current={s.id === f.stepId ? "true" : undefined}>
        <button type="button" className="mds-dfstep__btn" aria-current={s.id === f.stepId ? "step" : undefined}
          aria-disabled={blocked ? "true" : undefined} onClick={() => { if (!blocked) f.goTo(s.id); }}>
          <span className="mds-dfstep__n" aria-hidden="true">{mark}</span>
          <span><span className="mds-dfstep__lbl">{s.label}</span><span className="mds-dfstep__meta">{meta}</span></span>
        </button></li>;
    })}</ol>
  </div>;
}

export function DecisionFlowStage({ title, children, showVersionNotice = true, className = "", ...rest }) {
  const f = useDecisionFlow();
  const vc = f.versionChanged;
  return <section className={`mds-df mds-dfstage${className ? " " + className : ""}`} aria-label={`${(f.step || {}).label || "Step"} — decision step ${f.index + 1} of ${f.steps.length}`} {...rest}>
    <header className="mds-dfstage__hd">
      <span className="mds-dfstage__ttl">{title || (f.step || {}).label}</span>
      <span className="mds-dfstage__pos">Step {f.index + 1} of {f.steps.length}{f.snapshotId ? ` · ${f.snapshotId}` : ""}</span>
    </header>
    {showVersionNotice && vc ? <div className="mds-dfstale" role="status">
      <span className="mds-dfstale__t">The object changed underneath this review</span>
      <span className="mds-dfstale__b">{f.object ? `${f.object.type} ${f.object.id} moved from version ${vc.from} to version ${vc.to}. ` : ""}
        Completed review, impact preview and approval state generated against the earlier version have been invalidated. Re-run validation before continuing.</span>
      <span><button type="button" className="mds-dfbtn mds-dfbtn--ghost" onClick={f.acknowledgeVersionChange}>Acknowledge and re-validate</button></span>
    </div> : null}
    {children}
  </section>;
}

export function DecisionConsequenceBar({
  primaryLabel, onPrimary, secondaryLabel, onSecondary, reversibility, guards,
  requireCompletion = true, className = "", ...rest
}) {
  const f = useDecisionFlow();
  const g = guards || f.stepGuards(f.stepId);
  const s = summarize(g);
  const unmet = f.unmet(f.stepId);
  const disabled = s.blocked || (requireCompletion && unmet.length > 0);
  const rev = reversibility ? REVERSIBILITY[reversibility] || { label: reversibility, note: "" } : null;
  return <div className={`mds-df mds-dfbar${className ? " " + className : ""}`} {...rest}>
    <span className="mds-dfbar__l">
      <span className="mds-dfbar__st" data-blocked={s.blocked ? "true" : undefined}>
        {s.blocked ? `${s.sentence} — ${s.firstBlocking.label}` : unmet.length ? `Requires ${unmet.map(r => (f.steps.find(x => x.id === r) || {}).label || r).join(", ")}` : s.sentence}</span>
      {f.object ? <span className="mds-dfbar__ob">{f.object.type} {f.object.id} · version {f.object.version}{f.snapshotId ? ` · snapshot ${f.snapshotId}` : ""}</span> : null}
      {rev ? <span className="mds-dfbar__rev">Reversibility · {rev.label}{rev.note ? ` — ${rev.note}` : ""}</span> : null}
    </span>
    <span className="mds-dfbar__acts">
      {secondaryLabel ? <button type="button" className="mds-dfbtn mds-dfbtn--ghost" onClick={onSecondary}>{secondaryLabel}</button> : null}
      {primaryLabel ? <button type="button" className="mds-dfbtn" aria-disabled={disabled ? "true" : undefined}
        onClick={() => { if (!disabled && onPrimary) onPrimary({ object: f.object, snapshotId: f.snapshotId, stepId: f.stepId, guards: g }); }}>{primaryLabel}</button> : null}
    </span>
  </div>;
}
