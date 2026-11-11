// Meridian ContinuityFrame — retains last-known-good evidence through stale, partial, and offline states.
// 3.2.0: self-ticking age from `since`, retained-content dimming and interaction lock, retry affordance.
import React from "react";

const META = {
  live: { label: "Live", glyph: "●", tone: "ok" },
  delayed: { label: "Delayed", glyph: "◔", tone: "warn" },
  stale: { label: "Stale", glyph: "◔", tone: "warn" },
  partial: { label: "Partial", glyph: "◐", tone: "warn" },
  offline: { label: "Offline", glyph: "■", tone: "neutral" },
  recovering: { label: "Recovering", glyph: "◇", tone: "info" },
  error: { label: "Error", glyph: "▲", tone: "danger" },
};
const RETAINED = ["stale", "partial", "offline", "error"];
let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "continuity-frame");
  el.textContent = `
.mds-cont{display:flex;flex-direction:column;min-height:0;border:1px solid var(--border,#D2CDC3);background:var(--surface-well,var(--card-surface-raised,#FCFBF8));font-family:var(--font-body);position:relative;}
.mds-cont__status{min-height:30px;display:flex;align-items:center;gap:8px;padding:0 10px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--surface-recessed,var(--bg-medium,#EBE8E1));font-size:11px;color:var(--text-secondary,#535C65);}
.mds-cont__mark{font-size:9px;color:var(--neutral,#6D757D)}.mds-cont__mark--ok{color:var(--green,#2E7655)}.mds-cont__mark--warn{color:var(--orange,#986717)}.mds-cont__mark--danger{color:var(--red,#B54B42)}.mds-cont__mark--info{color:var(--purple,#2F648A)}
.mds-cont__label{font-weight:600;color:var(--text-primary,#20252B)}.mds-cont__detail{font-family:var(--font-data,monospace);color:var(--text-muted,#606870)}.mds-cont__spacer{flex:1}.mds-cont__read{font:600 9px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--pending,#7A6B52)}
.mds-cont__body{min-height:0;position:relative}.mds-cont--stale .mds-cont__body,.mds-cont--partial .mds-cont__body,.mds-cont--offline .mds-cont__body{box-shadow:inset 3px 0 0 var(--pending,#7A6B52)}
.mds-cont--error .mds-cont__body{box-shadow:inset 3px 0 0 var(--red,#B54B42)}
.mds-cont__retry{height:22px;padding:0 8px;border:1px solid var(--border-strong,#AAA49A);border-radius:var(--radius-control,4px);background:var(--surface-plate,var(--bg-light,#F8F7F3));color:var(--text-primary,#20252B);font:600 10px var(--font-data,monospace);letter-spacing:.04em;text-transform:uppercase;cursor:pointer;}
.mds-cont__retry:hover{background:var(--bg-hover,#EDEFF0);border-color:var(--border-hover,#BEB8AD)}.mds-cont__retry:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}.mds-cont__retry[disabled]{opacity:.5;cursor:default}
.mds-cont--dim .mds-cont__body>*{opacity:.55}
.mds-cont--locked .mds-cont__body{pointer-events:none;}
.mds-cont__retained{position:absolute;top:6px;right:6px;z-index:4;display:inline-flex;align-items:center;gap:5px;padding:2px 6px;border:1px solid var(--border-strong,#AAA49A);background:var(--surface-overlay,var(--surface-overlay-base,#fff));font:600 9px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--pending,#7A6B52);pointer-events:none;}
@media (prefers-reduced-motion:no-preference){.mds-cont--recovering .mds-cont__mark{animation:mds-cont-pulse 1.4s ease-in-out infinite}@keyframes mds-cont-pulse{50%{opacity:.35}}}
@media print{.mds-cont--dim .mds-cont__body>*{opacity:1}.mds-cont__retry{display:none}}
`;
  document.head.appendChild(el);
}

function formatAge(ms) {
  const s = Math.max(0, Math.round(ms / 1000));
  if (s < 60) return `${s}s`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m}m ${String(s % 60).padStart(2, "0")}s`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h ${String(m % 60).padStart(2, "0")}m`;
  return `${Math.floor(h / 24)}d ${String(h % 24).padStart(2, "0")}h`;
}
/** Ticks only while `since` is set and no explicit `age` was supplied by the host. */
function useTickingAge(since, enabled) {
  const [age, setAge] = React.useState(() => (since && enabled ? formatAge(Date.now() - new Date(since).getTime()) : null));
  React.useEffect(() => {
    if (!since || !enabled) { setAge(null); return undefined; }
    const at = new Date(since).getTime();
    if (Number.isNaN(at)) { setAge(null); return undefined; }
    let cancelled = false;
    const step = () => {
      if (cancelled) return;
      const elapsed = Date.now() - at;
      setAge((current) => { const next = formatAge(elapsed); return next === current ? current : next; });
      timer = setTimeout(step, elapsed < 60000 ? 1000 : elapsed < 3600000 ? 15000 : 60000);
    };
    let timer = setTimeout(step, 0);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [since, enabled]);
  return age;
}

export function ContinuityStatus({
  state = "live", subject = "Data", snapshotAt, age, since, coverage, readOnly,
  onRetry, retryLabel = "Retry", retrying = false, className = "",
}) {
  inject();
  const meta = META[state] || META.live;
  const ticking = useTickingAge(since, age == null);
  const shownAge = age ?? ticking;
  const detail = state === "live"
    ? (shownAge ? `updated ${shownAge} ago` : "current")
    : state === "partial"
      ? (coverage || "coverage incomplete")
      : snapshotAt
        ? `showing snapshot ${snapshotAt}`
        : shownAge ? `${shownAge} old` : "last-known-good retained";
  return <div className={`mds-cont__status${className ? " " + className : ""}`} role="status" aria-live="polite">
    <span className={`mds-cont__mark mds-cont__mark--${meta.tone}`} aria-hidden="true">{meta.glyph}</span>
    <span className="mds-cont__label">{meta.label}</span><span>{subject}</span><span className="mds-cont__detail">{detail}</span>
    <span className="mds-cont__spacer" />{coverage && state !== "partial" ? <span className="mds-cont__detail">{coverage}</span> : null}
    {readOnly ? <span className="mds-cont__read">Read only</span> : null}
    {onRetry ? <button type="button" className="mds-cont__retry" onClick={onRetry} disabled={retrying} aria-label={`${retryLabel} ${subject || ""}`.trim()}>{retrying ? "Retrying" : retryLabel}</button> : null}
  </div>;
}

export function ContinuityFrame({
  state = "live", subject, snapshotAt, age, since, coverage, readOnly,
  dim = "auto", retainedLabel = "Retained", onRetry, retryLabel, retrying,
  children, className = "", ...rest
}) {
  inject();
  const retained = RETAINED.includes(state);
  const locked = readOnly ?? ["offline", "stale", "error"].includes(state);
  const dimmed = dim === "auto" ? ["offline", "stale", "error"].includes(state) : Boolean(dim);
  return <section
    className={`mds-cont mds-cont--${state}${dimmed ? " mds-cont--dim" : ""}${locked ? " mds-cont--locked" : ""}${className ? " " + className : ""}`}
    data-continuity-state={state}
    data-readonly={locked ? "true" : undefined}
    {...rest}
  >
    <ContinuityStatus
      state={state} subject={subject} snapshotAt={snapshotAt} age={age} since={since}
      coverage={coverage} readOnly={locked} onRetry={onRetry} retryLabel={retryLabel} retrying={retrying}
    />
    <div className="mds-cont__body" aria-busy={state === "recovering" || undefined}>
      {retained && retainedLabel ? <span className="mds-cont__retained">{retainedLabel}</span> : null}
      {children}
    </div>
  </section>;
}
