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
.mds-badge--neutral{background:var(--bg-hover,#EDEFF0);border-color:var(--border,#D2CDC3);color:var(--text-secondary,#535C65);}
.mds-badge--info{background:var(--blue-a10,rgba(47,111,143,.10));border-color:var(--semantic-info,#2F648A);color:var(--semantic-info,#2F648A);}
.mds-badge--success{background:var(--green-a10,rgba(74,124,94,.10));border-color:var(--green,#2E7655);color:var(--green-dim,#375D46);}
.mds-badge--warning{background:var(--orange-a10,rgba(183,121,31,.10));border-color:var(--orange,#986717);color:var(--orange-dim,#735414);}
.mds-badge--danger{background:var(--red-a10,rgba(166,67,63,.10));border-color:var(--red,#B54B42);color:var(--red-dim,#7D322F);}
/* environment modes — SOLID filled chips (you-are-here), a different register from washed status */
.mds-badge--live{background:var(--mode-live,#B54B42);border-color:var(--mode-live,#B54B42);color:var(--text-on-fill,#fff);font-weight:700;}
.mds-badge--paper{background:var(--mode-paper,#C56343);border-color:var(--mode-paper,#C56343);color:var(--text-on-fill,#fff);font-weight:700;}
.mds-badge--fixture{background:var(--mode-fixture,#986717);border-color:var(--mode-fixture,#986717);color:var(--text-on-fill,#fff);font-weight:700;}
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
