// Meridian badge — mirrors NeutralBadgeStyle + Badge{Success,Danger,Warning,Info}Style
// in ThemeSurfaces.xaml. Compact 2px tag, alpha-10 fill + solid semantic border, dim
// semantic text for STATUS. Environment modes (live/paper/fixture) are SOLID chips.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-badge{display:inline-flex;align-items:center;gap:6px;border:1px solid;
  padding:3px 8px;font-family:var(--font-body);font-size:11px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;white-space:nowrap;line-height:1.3;border-radius:var(--radius-chip,2px);}
.mds-badge__dot{height:6px;width:6px;background:currentColor;flex:0 0 auto;}
.mds-badge--neutral{background:var(--bg-hover,#F0EEE9);border-color:var(--border,#E4E3DE);color:var(--text-secondary,#4E5258);}
.mds-badge--info{background:var(--blue-a10,rgba(168,84,54,.10));border-color:var(--accent,#A85436);color:var(--accent,#A85436);}
.mds-badge--success{background:var(--green-a10,rgba(58,122,86,.10));border-color:var(--green,#3A7A56);color:var(--green-dim,#2C5C40);}
.mds-badge--warning{background:var(--orange-a10,rgba(138,92,18,.10));border-color:var(--orange,#8A5C12);color:var(--orange-dim,#68450E);}
.mds-badge--danger{background:var(--red-a10,rgba(168,68,60,.10));border-color:var(--red,#A8443C);color:var(--red-dim,#7E332D);}
/* environment modes — SOLID filled chips (you-are-here), a different register from washed status */
.mds-badge--live{background:var(--mode-live,#A8443C);border-color:var(--mode-live,#A8443C);color:var(--text-on-fill,#fff);font-weight:700;}
.mds-badge--paper{background:var(--mode-paper,#A85436);border-color:var(--mode-paper,#A85436);color:var(--text-on-fill,#fff);font-weight:700;}
.mds-badge--fixture{background:var(--mode-fixture,#8A5C12);border-color:var(--mode-fixture,#8A5C12);color:var(--text-on-fill,#fff);font-weight:700;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "badge");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Badge({ variant = "neutral", dot = false, className = "", children, ...rest }) {
  inject();
  return (
    <span className={`mds-badge mds-badge--${variant}${className ? " " + className : ""}`} {...rest}>
      {dot && <span className="mds-badge__dot" aria-hidden="true" />}
      {children}
    </span>
  );
}
