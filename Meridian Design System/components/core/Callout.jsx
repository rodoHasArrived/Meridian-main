// Meridian Callout — inline messaging block (info / success / warning / danger).
// Distinct from StatusBanner (run results) — this is for prose guidance inside content.
// Functional only: desaturated alpha-wash fill, solid left rule, no radius.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-callout{display:flex;gap:10px;padding:12px 14px;border:1px solid var(--border,#E4E3DE);
  border-left-width:3px;background:var(--bg-light,#FBFAF8);font-family:var(--font-body);}
.mds-callout-icon{flex:0 0 auto;font-size:14px;line-height:20px;}
.mds-callout-body{flex:1;min-width:0;}
.mds-callout-title{font-size:13px;font-weight:600;color:var(--text-primary,#22252A);
  margin:0 0 2px;line-height:20px;}
.mds-callout-text{font-size:13px;line-height:20px;color:var(--text-secondary,#4E5258);margin:0;}
.mds-callout--info{border-left-color:var(--accent,#A85436);background:var(--blue-a10,rgba(168,84,54,.10));}
.mds-callout--info .mds-callout-icon{color:var(--accent,#A85436);}
.mds-callout--success{border-left-color:var(--green,#3A7A56);background:var(--green-a10,rgba(58,122,86,.10));}
.mds-callout--success .mds-callout-icon{color:var(--green,#3A7A56);}
.mds-callout--warning{border-left-color:var(--orange,#8A5C12);background:var(--orange-a10,rgba(138,92,18,.10));}
.mds-callout--warning .mds-callout-icon{color:var(--orange,#8A5C12);}
.mds-callout--danger{border-left-color:var(--red,#A8443C);background:var(--red-a10,rgba(168,68,60,.10));}
.mds-callout--danger .mds-callout-icon{color:var(--red,#A8443C);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "callout");
  el.textContent = css;
  document.head.appendChild(el);
}

const ICONS = { info: "ℹ", success: "✓", warning: "▲", danger: "✕" };

export function Callout({ tone = "info", title, children, icon }) {
  inject();
  return (
    <div className={`mds-callout mds-callout--${tone}`} role="note">
      <span className="mds-callout-icon" aria-hidden="true">{icon ?? ICONS[tone]}</span>
      <div className="mds-callout-body">
        {title && <p className="mds-callout-title">{title}</p>}
        {children && <p className="mds-callout-text">{children}</p>}
      </div>
    </div>
  );
}

Callout.displayName = "Callout";
