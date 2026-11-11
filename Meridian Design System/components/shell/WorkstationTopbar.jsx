// Meridian masthead — graphite brand bar (--topbar-bg) capping the light workstation.
// Brand mark + module breadcrumb · command search · UTC clock · environment mode badge.
//
// The clock drives itself (4.4 · docs/SUITE_COHESION_4_4.md › C-1). The suite used to ship six
// different frozen wall clocks, each beside a green dot claiming a live feed — the fastest way for
// a screen to read as a mock. Pass no `clock` and the masthead reads the one shared world instant
// and ticks; the dot reflects real feed health. Pass a string only to freeze it deliberately.
import React from "react";
import { Badge } from "../core/Badge";
import { UserMenu } from "./SessionControls";

/** Subscribe to the shared world clock. Falls back to a static string with no world loaded. */
function useWorldClock(override) {
  const W = typeof window !== "undefined" ? window.MeridianWorld : null;
  const [, force] = React.useReducer((n) => n + 1, 0);
  React.useEffect(() => {
    if (override != null || !W) return undefined;
    return W.subscribe(force);
  }, [override, W]);
  if (override != null) return { label: override, ok: true, pulse: true };
  if (!W) return { label: "14:32:08 UTC", ok: true, pulse: true };
  return { label: W.clock(), ok: W.health() === "ok", pulse: Math.floor(W.now() / 1000) % 2 === 0 };
}

/** The signed-in operator, from the shared world unless the caller overrides it. */
function useOperator(override) {
  if (override === null) return null;
  if (override) return override;
  const W = typeof window !== "undefined" ? window.MeridianWorld : null;
  return W ? W.OPERATOR : null;
}

/**
 * The breadcrumb (4.4 · E-1). Omit the prop and the masthead derives it from the app map and the
 * URL — Domain › parent screen › screen › object — so all 27 screens gained a way back without a
 * line of per-screen work. Re-derives when a screen names its object or history moves.
 */
function useTrail(override, moduleLabel) {
  const M = typeof window !== "undefined" ? window.MeridianAppMap : null;
  const [, force] = React.useReducer((n) => n + 1, 0);
  React.useEffect(() => {
    if (override || !M) return undefined;
    const off = M.onChange ? M.onChange(force) : null;
    window.addEventListener("popstate", force);
    return () => { if (off) off(); window.removeEventListener("popstate", force); };
  }, [override, M]);
  if (override) return { crumbs: override, back: null };
  if (!M || !M.trail) return { crumbs: [{ label: moduleLabel, current: true }], back: null };
  const crumbs = M.trail();
  return {
    crumbs: crumbs.length ? crumbs : [{ label: moduleLabel, current: true }],
    back: M.backTarget ? M.backTarget() : null,
  };
}

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.ws-masthead{display:grid;min-height:48px;
  grid-template-columns:minmax(0,max-content) minmax(220px,1fr) max-content max-content max-content;
  gap:.75rem;align-items:center;padding:0 1rem;
  background:var(--topbar-bg,#29323A);border-bottom:1px solid var(--topbar-border,#3A444D);
  font-family:var(--font-body);}
.ws-brand{display:inline-flex;align-items:center;gap:.5rem;min-width:0;}
.ws-brand img{width:22px;height:22px;flex:0 0 auto;}
.ws-brand__name{font-family:var(--font-display);font-size:14px;font-weight:600;color:var(--topbar-text,#F1EFEA);}
.ws-brand__sep{color:var(--topbar-sep,#4A545D);}
.ws-brand__mod{font-size:13px;color:var(--topbar-text-muted,#A9B1B8);}
.ws-crumbs{display:flex;align-items:center;gap:.5rem;min-width:0;}
.ws-crumbs>*{display:inline-flex;align-items:center;gap:.5rem;min-width:0;}
.ws-crumb{font-family:inherit;font-size:13px;color:var(--topbar-text-muted,#A9B1B8);
  max-width:22ch;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
button.ws-crumb{background:none;border:0;padding:0;cursor:pointer;}
button.ws-crumb:hover{color:var(--topbar-text,#F1EFEA);text-decoration:underline;text-underline-offset:2px;}
.ws-crumb--current{color:var(--topbar-text,#F1EFEA);}
.ws-crumb--domain{color:var(--topbar-text-faint,#969EA6);}
.ws-back{display:inline-flex;align-items:center;justify-content:center;width:22px;height:22px;
  flex:0 0 auto;margin-right:-.125rem;background:none;border:0;border-radius:var(--radius-button,2px);
  color:var(--topbar-text-muted,#A9B1B8);font-size:15px;line-height:1;cursor:pointer;}
.ws-back:hover{background:var(--topbar-field-bg,#20272D);color:var(--topbar-text,#F1EFEA);}
/* Crumbs give way before the search field does, and they drop from the outside in: the domain
   first, then any grandparent, leaving parent › current. The thresholds are set for the band
   these screens are actually viewed in (~900–1000px preview panes), not for a full desktop. */
@media (max-width:1280px){.ws-crumbs>.ws-crumbitem--domain{display:none;}}
@media (max-width:1040px){.ws-crumbs>.ws-crumbitem:not(:last-child):not(:nth-last-child(2)){display:none;}}
@media (max-width:820px){.ws-crumbs>.ws-crumbitem:not(:last-child){display:none;}}
.ws-search{display:flex;align-items:center;gap:.5rem;width:100%;max-width:560px;height:30px;
  padding:0 .625rem;border:1px solid var(--topbar-field-border,#38424B);border-radius:var(--radius-button,2px);
  background:var(--topbar-field-bg,#20272D);color:var(--topbar-text-faint,#969EA6);text-align:left;cursor:text;
  font-family:var(--font-body);font-size:12px;transition:border-color .12s ease;}
.ws-search:hover{border-color:var(--topbar-field-border-hover,#46515C);}
.ws-search__txt{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.ws-kbd{font-family:var(--font-data);font-size:10px;border:1px solid var(--topbar-field-border,#38424B);
  border-radius:var(--radius-chip,2px);padding:1px 5px;color:var(--topbar-text-faint,#969EA6);}
.ws-status{display:inline-flex;align-items:center;gap:.5rem;font-family:var(--font-data);
  font-size:11px;color:var(--topbar-text-muted,#A9B1B8);font-variant-numeric:tabular-nums;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "masthead");
  el.textContent = css;
  document.head.appendChild(el);
}

export function WorkstationTopbar({
  moduleLabel = "Workstation", environment = "PAPER", clock, breadcrumb,
  brandSrc = "assets/brand/meridian-mark-light.svg", onSearch, searchHint = "/",
  searchLabel = "Search this screen, or go to…", operator, onSignOut, showOperator = true
}) {
  inject();
  const env = String(environment).toLowerCase();
  const time = useWorldClock(clock);
  const who = useOperator(operator);
  const { crumbs, back } = useTrail(breadcrumb, moduleLabel);
  const P = typeof window !== "undefined" ? window.MeridianPrefs : null;
  const [, bump] = React.useReducer((n) => n + 1, 0);
  React.useEffect(() => (P ? P.subscribe(bump) : undefined), [P]);
  // Theme and density live in the menu because they are the operator's, not the screen's — and
  // they persist across navigation (docs/SUITE_COHESION_4_4.md › D). The screen's own tweak still
  // seeds a first visit; choosing here overrides it everywhere until reset.
  const prefItems = P ? [
    { label: (P.theme("light") === "dark" ? "Light theme" : "Dark theme"),
      onSelect: () => P.set("theme", P.theme("light") === "dark" ? "light" : "dark") },
    { label: "Density · " + P.density("default"),
      onSelect: () => { const o = P.VALID.density; P.set("density", o[(o.indexOf(P.density("default")) + 1) % o.length]); } },
    P.isSet() ? { label: "Reset display to screen defaults", onSelect: () => { P.clear(); window.location.reload(); } } : null,
  ].filter(Boolean) : [];
  return (
    // Live mode paints a hard 2px red inset hairline across the masthead — operators feel "real money".
    <header className="ws-masthead" style={env === "live" ? { boxShadow: "inset 0 2px 0 0 var(--mode-live)" } : undefined}>
      <div className="ws-brand">
        {/* Back is shown only when there is an honest destination — real in-suite history, or a
            parent screen. No dead chevron on a screen you arrived at cold. */}
        {back ? (
          <button className="ws-back" type="button" onClick={back.run} title={back.label} aria-label={back.label}>‹</button>
        ) : null}
        <img src={brandSrc} alt="Meridian" />
        <span className="ws-brand__name">Meridian</span>
        {/* Domain › parent › screen › object, derived from the app map (4.4 · E-1). Each crumb
            above the current one navigates; the last one is where you are. */}
        <nav className="ws-crumbs" aria-label="Breadcrumb">
          {crumbs.map((c, i) => (
            <span key={(c.id || c.label) + ":" + i} className={"ws-crumbitem" + (c.domain ? " ws-crumbitem--domain" : "")}>
              <span className="ws-brand__sep" aria-hidden="true">/</span>
              {c.onClick ? (
                <button className="ws-crumb" type="button" onClick={c.onClick}>{c.label}</button>
              ) : (
                <span className={"ws-crumb" + (c.current ? " ws-crumb--current ws-brand__mod" : "") + (c.domain ? " ws-crumb--domain" : "")}
                  aria-current={c.current ? "page" : undefined}>{c.label}</span>
              )}
            </span>
          ))}
        </nav>
      </div>
      {/* The field is real on every screen: ds-base.js mounts GlobalSearch and binds both this
          button and "/" to it, so `onSearch` is an override, not a requirement. */}
      <button className="ws-search" onClick={onSearch} type="button">
        <span aria-hidden="true">⌕</span>
        <span className="ws-search__txt">{searchLabel}</span>
        <span className="ws-kbd">{searchHint}</span>
      </button>
      <div className="ws-status">
        <span title={time.ok ? "Feeds healthy" : "A feed is lagging"} style={{
          width: 6, height: 6, borderRadius: 999, display: "inline-block",
          background: time.ok ? "var(--chrome-ok, #65A983)" : "var(--chrome-warn, #C69B4D)",
          opacity: time.pulse ? 1 : 0.5, transition: "opacity .45s ease",
        }} />
        <span>{time.label}</span>
      </div>
      {/* Environment chip — reuse Badge's solid live/paper/fixture variants instead of a hand-rolled twin */}
      <Badge data-environment-badge="" variant={["live", "paper", "fixture"].includes(env) ? env : "neutral"} dot>
        {String(environment)}
      </Badge>
      {/* Who is signed in (4.4 · B-5). No screen showed the operator, yet accounting silently
          disables posting by role and session-start spends a screen establishing an identity.
          Read from the shared world, so it is the same person in every audit trail. */}
      {showOperator && who ? (
        <UserMenu name={who.name} role={who.roleLabel || who.role} detail={who.desk || who.title}
          items={prefItems}
          onSignOut={onSignOut || (() => {
            const M = typeof window !== "undefined" ? window.MeridianAppMap : null;
            if (M) M.go("session-start");
          })} />
      ) : null}
    </header>
  );
}
