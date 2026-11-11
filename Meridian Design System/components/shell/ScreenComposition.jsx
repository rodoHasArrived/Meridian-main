// Meridian screen composition — a screen is a DECLARATION (archetype + regions),
// not a fork of a template. defineScreen() states the manifest; ScreenComposition
// renders it and fills regions from slots. An archetype change propagates to
// every screen that declares it instead of costing 31 template edits.
import React from "react";

export const ARCHETYPES = {
  monitor:     { label: "Monitor",     regions: ["context", "metrics", "primary", "secondary", "status"] },
  investigate: { label: "Investigate", regions: ["context", "claim", "primary", "evidence", "action"] },
  compose:     { label: "Compose",     regions: ["context", "inputs", "primary", "analytics", "action"] },
  report:      { label: "Report",      regions: ["masthead", "primary", "notes"] },
  admin:       { label: "Admin",       regions: ["context", "primary", "danger"] },
  start:       { label: "Start",       regions: ["primary"] },
};

const SPAN = { context: 12, masthead: 12, metrics: 12, claim: 12, primary: 8, secondary: 4,
  evidence: 4, analytics: 4, inputs: 4, action: 12, danger: 12, notes: 12, status: 12 };

/** Declare a screen. Validates its regions against its archetype. */
export function defineScreen(spec) {
  const arch = ARCHETYPES[spec.archetype] || ARCHETYPES.monitor;
  const regions = (spec.regions || arch.regions).map((r) =>
    typeof r === "string" ? { id: r, role: r, span: SPAN[r] || 12 } : { span: SPAN[r.role || r.id] || 12, role: r.id, ...r });
  const unknown = regions.filter((r) => arch.regions.indexOf(r.role) === -1).map((r) => r.role);
  return { ...spec, archetype: spec.archetype, regions, warnings: unknown.length ? [`Regions outside the ${arch.label} archetype: ${unknown.join(", ")}`] : [] };
}

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-sc{display:grid;grid-template-columns:repeat(12,minmax(0,1fr));gap:12px;padding:12px;background:var(--bg-canvas,#F2F0EB);}
.mds-sc__rg{display:flex;flex-direction:column;min-width:0;background:var(--bg-light,#F8F7F3);
  border:1px solid var(--border,#D2CDC3);border-radius:2px;}
.mds-sc__hd{display:flex;justify-content:space-between;align-items:baseline;gap:10px;padding:6px 10px;
  border-bottom:1px solid var(--border-divider,#E4E0D8);}
.mds-sc__ti{font-family:var(--font-body,system-ui);font-size:12px;font-weight:var(--weight-medium,500);color:var(--text-primary,#20252B);}
.mds-sc__ro{font-family:var(--font-body,system-ui);font-size:10px;font-variant:all-small-caps;letter-spacing:.06em;color:var(--text-muted,#606870);}
.mds-sc__bd{padding:10px;min-height:36px;}
.mds-sc__rg--action{background:var(--surface-recessed, #EBE8E1);}
.mds-sc__rg--danger{border-color:var(--red, #B54B42);}
.mds-sc__empty{font-family:var(--font-body,system-ui);font-size:11px;color:var(--text-muted,#606870);}
.mds-sc__warn{grid-column:1/-1;font-family:var(--font-body,system-ui);font-size:11px;color:var(--amber, #986717);}`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "screen-composition");
  el.textContent = css;
  document.head.appendChild(el);
}

export function ScreenComposition({ screen, slots = {}, showRoles = false, className = "", children, ...rest }) {
  inject();
  const s = screen && screen.regions ? screen : defineScreen(screen || {});
  // Region content may also arrive as markup children tagged data-region="<id>",
  // so a template can fill regions without handing over React nodes as props.
  const tagged = {};
  React.Children.forEach(children, (c) => {
    const id = c && c.props && (c.props["data-region"] || c.props.region);
    if (id) tagged[id] = c;
  });
  return (
    <div className={`mds-sc${className ? " " + className : ""}`} {...rest}>
      {(s.warnings || []).map((w) => <div className="mds-sc__warn" key={w}>▲ {w}</div>)}
      {s.regions.map((r) => (
        <section className={`mds-sc__rg mds-sc__rg--${r.role}`} key={r.id} style={{ gridColumn: `span ${r.span || SPAN[r.role || r.id] || 12}` }}>
          {r.title || showRoles ? (
            <div className="mds-sc__hd">
              <span className="mds-sc__ti">{r.title || r.id}</span>
              {showRoles ? <span className="mds-sc__ro">{r.role}</span> : null}
            </div>
          ) : null}
          <div className="mds-sc__bd">{slots[r.id] || tagged[r.id] || <span className="mds-sc__empty">{r.hint || `${r.role} region`}</span>}</div>
        </section>
      ))}
    </div>
  );
}

// The bundler exposes only capitalized exports, so defineScreen hangs off the carrier.
ScreenComposition.define = defineScreen;
ScreenComposition.archetypes = ARCHETYPES;
